<#
.SYNOPSIS
Merges a pull request with administrator privileges only after it is verifiably green.
Merge commits are never offered because main requires linear history.

.DESCRIPTION
The sole code owner cannot approve their own pull requests, so the required-review rule on main
can only be satisfied by an administrator merge. This script narrows that override: it refuses to
merge unless the pull request is open, not a draft, targets main, is mergeable, already contains
the current tip of main, has every check passed or skipped with none pending, and has no
unresolved review threads. The merge is pinned to the exact head commit that was verified, so a
push after verification makes the merge fail.

.EXAMPLE
pwsh scripts/merge-green-pr.ps1 -PullRequest 83
pwsh scripts/merge-green-pr.ps1 -PullRequest 83 -DryRun
#>
param(
    [Parameter(Mandatory = $true)][int]$PullRequest,
    [string]$Repository = "jasperfordesq-ai/accounts",
    [string]$BaseBranch = "main",
    [ValidateSet("squash", "rebase")][string]$Method = "squash",
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$failures = [System.Collections.Generic.List[string]]::new()

function Invoke-GhJson([string[]]$Arguments) {
    $output = & gh @Arguments
    if ($LASTEXITCODE -ne 0) { throw "gh $($Arguments -join ' ') failed with exit code $LASTEXITCODE." }
    return ($output -join "`n") | ConvertFrom-Json
}

$pr = Invoke-GhJson @("pr", "view", "$PullRequest", "--repo", $Repository,
    "--json", "number,title,state,isDraft,mergeable,baseRefName,headRefOid,url")

if ($pr.state -ne "OPEN") { $failures.Add("Pull request is $($pr.state), not OPEN.") }
if ($pr.isDraft) { $failures.Add("Pull request is a draft.") }
if ($pr.baseRefName -ne $BaseBranch) { $failures.Add("Pull request targets '$($pr.baseRefName)', not '$BaseBranch'.") }
if ($pr.mergeable -ne "MERGEABLE") { $failures.Add("Pull request mergeability is '$($pr.mergeable)'.") }

# MERGEABLE only means no conflicts. Main requires strict status checks, so the verified head must
# already contain the current base tip; otherwise CI never ran against the latest main.
$comparison = Invoke-GhJson @("api", "repos/$Repository/compare/$($BaseBranch)...$($pr.headRefOid)")
if ([int]$comparison.behind_by -ne 0) {
    $failures.Add("Head is $($comparison.behind_by) commit(s) behind '$BaseBranch'; update the branch and let CI rerun.")
}

# gh pr checks exits non-zero while checks are pending or failing; read the JSON regardless.
$checksJson = & gh pr checks "$PullRequest" --repo $Repository --json "name,bucket,state"
$checks = @((($checksJson -join "`n") | ConvertFrom-Json))
if ($checks.Count -eq 0) {
    $failures.Add("No checks are reported for the head commit.")
}
foreach ($check in $checks) {
    if ($check.bucket -notin @("pass", "skipping")) {
        $failures.Add("Check '$($check.name)' is '$($check.bucket)' ($($check.state)).")
    }
}

$owner, $name = $Repository.Split("/", 2)
$threadQuery = 'query($owner:String!,$name:String!,$number:Int!){repository(owner:$owner,name:$name){pullRequest(number:$number){reviewThreads(first:100){totalCount nodes{isResolved}}}}}'
$threads = Invoke-GhJson @("api", "graphql", "-f", "query=$threadQuery",
    "-F", "owner=$owner", "-F", "name=$name", "-F", "number=$PullRequest")
$threadData = $threads.data.repository.pullRequest.reviewThreads
if ($threadData.totalCount -gt 100) { $failures.Add("More than 100 review threads; resolve state cannot be fully verified.") }
$unresolved = @($threadData.nodes | Where-Object { -not $_.isResolved }).Count
if ($unresolved -gt 0) { $failures.Add("$unresolved review thread(s) are unresolved.") }

Write-Host "PR #$($pr.number): $($pr.title)"
Write-Host "Head commit: $($pr.headRefOid)"
Write-Host "Checks: $($checks.Count) reported; unresolved threads: $unresolved"

if ($failures.Count -gt 0) {
    Write-Host "Refusing to merge:" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}

if ($DryRun) {
    Write-Host "Dry run: all gates pass; would $Method-merge with --admin." -ForegroundColor Green
    exit 0
}

& gh pr merge "$PullRequest" --repo $Repository "--$Method" --admin --match-head-commit $pr.headRefOid
if ($LASTEXITCODE -ne 0) { throw "gh pr merge failed with exit code $LASTEXITCODE." }
Write-Host "Merged $($pr.url) at verified head $($pr.headRefOid)." -ForegroundColor Green
