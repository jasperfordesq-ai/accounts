param(
    [string]$Repository = "jasperfordesq-ai/accounts",
    [string]$Branch = "main",
    [string]$CommitSha = "",
    [string]$EvidencePath = "github-governance-report.json"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($CommitSha)) {
    $CommitSha = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw "Unable to resolve the candidate commit." }
}

$failures = [System.Collections.Generic.List[string]]::new()

function Invoke-GitHubJson {
    param([Parameter(Mandatory = $true)][string]$Path)

    $output = & gh api $Path 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw ($output -join [Environment]::NewLine)
    }
    return ($output -join [Environment]::NewLine) | ConvertFrom-Json
}

function Require-Enabled {
    param(
        $Actual,
        [Parameter(Mandatory = $true)][string]$Label
    )
    if ($Actual -ne $true -and $Actual -ne "enabled" -and $Actual -ne "configured") {
        $failures.Add("$Label is not enabled.")
    }
}

$repositoryState = Invoke-GitHubJson "repos/$Repository"
$automatedSecurityFixes = Invoke-GitHubJson "repos/$Repository/automated-security-fixes"
$codeScanning = Invoke-GitHubJson "repos/$Repository/code-scanning/default-setup"
$commit = Invoke-GitHubJson "repos/$Repository/git/commits/$CommitSha"

$protection = $null
try {
    $protection = Invoke-GitHubJson "repos/$Repository/branches/$Branch/protection"
}
catch {
    $failures.Add("Branch '$Branch' is not protected or its protection could not be inspected: $($_.Exception.Message)")
}

$requiredSignatures = $null
try {
    $requiredSignatures = Invoke-GitHubJson "repos/$Repository/branches/$Branch/protection/required_signatures"
}
catch {
    $failures.Add("Signed commits are not required on '$Branch'.")
}

$requiredChecks = @(
    "Workflow Hygiene",
    "Backend",
    "Frontend",
    "Production Compose Config",
    "Production Stack Smoke",
    "CI Machine Evidence Pack"
)
$configuredChecks = @()

if ($null -ne $protection) {
    $configuredChecks = @($protection.required_status_checks.contexts)
    $configuredChecks += @($protection.required_status_checks.checks | ForEach-Object { $_.context })
    $configuredChecks = @($configuredChecks | Where-Object { $_ } | Sort-Object -Unique)

    Require-Enabled $protection.required_status_checks.strict "Strict required-status-check synchronization"
    foreach ($requiredCheck in $requiredChecks) {
        if ($configuredChecks -notcontains $requiredCheck) {
            $failures.Add("Required status check is missing: $requiredCheck")
        }
    }

    $reviews = $protection.required_pull_request_reviews
    if ($null -eq $reviews -or [int]$reviews.required_approving_review_count -lt 1) {
        $failures.Add("At least one approving pull-request review is required.")
    }
    Require-Enabled $reviews.dismiss_stale_reviews "Stale-review dismissal"
    Require-Enabled $reviews.require_code_owner_reviews "Code-owner review"
    Require-Enabled $protection.enforce_admins.enabled "Administrator branch-protection enforcement"

    if ($protection.allow_force_pushes.enabled -ne $false) {
        $failures.Add("Force pushes are not blocked.")
    }
    if ($protection.allow_deletions.enabled -ne $false) {
        $failures.Add("Protected-branch deletion is not blocked.")
    }
}

Require-Enabled $requiredSignatures.enabled "Required signed commits"
Require-Enabled $repositoryState.security_and_analysis.secret_scanning.status "Secret scanning"
Require-Enabled $repositoryState.security_and_analysis.secret_scanning_push_protection.status "Secret-scanning push protection"
Require-Enabled $repositoryState.security_and_analysis.dependabot_security_updates.status "Dependabot security updates"
Require-Enabled $automatedSecurityFixes.enabled "Dependabot automated security fixes"
Require-Enabled $codeScanning.state "CodeQL default setup"

if ($codeScanning.query_suite -ne "extended") {
    $failures.Add("CodeQL must use the extended query suite.")
}
if ($codeScanning.schedule -ne "weekly") {
    $failures.Add("CodeQL must run on a weekly schedule.")
}

Require-Enabled $commit.verification.verified "Release-candidate commit signature verification"

$report = [ordered]@{
    generatedAtUtc = [DateTime]::UtcNow.ToString("o")
    repository = $Repository
    branch = $Branch
    commitSha = $CommitSha
    status = if ($failures.Count -eq 0) { "passed" } else { "failed" }
    branchProtection = [ordered]@{
        configured = $null -ne $protection
        strictStatusChecks = $protection.required_status_checks.strict
        configuredChecks = $configuredChecks
        requiredChecks = $requiredChecks
        approvingReviewCount = $protection.required_pull_request_reviews.required_approving_review_count
        dismissStaleReviews = $protection.required_pull_request_reviews.dismiss_stale_reviews
        requireCodeOwnerReviews = $protection.required_pull_request_reviews.require_code_owner_reviews
        enforceAdmins = $protection.enforce_admins.enabled
        allowForcePushes = $protection.allow_force_pushes.enabled
        allowDeletions = $protection.allow_deletions.enabled
        requireSignedCommits = $requiredSignatures.enabled
    }
    security = [ordered]@{
        secretScanning = $repositoryState.security_and_analysis.secret_scanning.status
        pushProtection = $repositoryState.security_and_analysis.secret_scanning_push_protection.status
        dependabotSecurityUpdates = $repositoryState.security_and_analysis.dependabot_security_updates.status
        automatedSecurityFixes = $automatedSecurityFixes.enabled
        codeScanningState = $codeScanning.state
        codeScanningQuerySuite = $codeScanning.query_suite
        codeScanningSchedule = $codeScanning.schedule
        codeScanningLanguages = @($codeScanning.languages)
    }
    commitVerification = [ordered]@{
        verified = $commit.verification.verified
        reason = $commit.verification.reason
        verifiedAt = $commit.verification.verified_at
    }
    failures = @($failures)
}

$parent = Split-Path -Parent $EvidencePath
if (-not [string]::IsNullOrWhiteSpace($parent)) {
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
}
$report | ConvertTo-Json -Depth 8 | Set-Content -Path $EvidencePath -Encoding utf8

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) { Write-Error $failure -ErrorAction Continue }
    exit 1
}

Write-Host "GitHub governance policy passed for $Repository@$CommitSha."
