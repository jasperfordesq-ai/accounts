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
if ($CommitSha -cnotmatch '^[0-9a-f]{40}$') {
    throw "CommitSha must be a full lowercase 40-character hexadecimal Git commit SHA."
}

$failures = [System.Collections.Generic.List[string]]::new()
$requiredCheckAppId = 15368
$requiredCodeScanningLanguages = @("actions", "csharp", "javascript-typescript")

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
$branchState = Invoke-GitHubJson "repos/$Repository/branches/$Branch"
$automatedSecurityFixes = Invoke-GitHubJson "repos/$Repository/automated-security-fixes"
$codeScanning = Invoke-GitHubJson "repos/$Repository/code-scanning/default-setup"
$commit = Invoke-GitHubJson "repos/$Repository/git/commits/$CommitSha"
$dependabotAlertsEnabled = $true
try {
    $null = & gh api "repos/$Repository/vulnerability-alerts" 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Dependabot vulnerability-alert endpoint rejected the request." }
}
catch {
    $dependabotAlertsEnabled = $false
    $failures.Add("Dependabot vulnerability alerts are not enabled or could not be inspected.")
}

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
$configuredCheckBindings = @()
$reviewBypassAllowanceCount = 0
$reviewBypassShapeValid = $false
$reviewBypassUnsupportedForUserRepository = $false

if ($null -ne $protection) {
    $configuredChecks = @($protection.required_status_checks.contexts)
    $configuredChecks += @($protection.required_status_checks.checks | ForEach-Object { $_.context })
    $configuredChecks = @($configuredChecks | Where-Object { $_ } | Sort-Object -Unique)
    $checkRows = @($protection.required_status_checks.checks | Where-Object { $null -ne $_ })
    $configuredCheckBindings = @($checkRows | ForEach-Object {
        [ordered]@{ context = [string]$_.context; appId = [int64]$_.app_id }
    })

    Require-Enabled $protection.required_status_checks.strict "Strict required-status-check synchronization"
    foreach ($requiredCheck in $requiredChecks) {
        if ($configuredChecks -notcontains $requiredCheck) {
            $failures.Add("Required status check is missing: $requiredCheck")
        }
        $matchingRows = @($checkRows | Where-Object { [string]$_.context -ceq $requiredCheck })
        if ($matchingRows.Count -ne 1) {
            $failures.Add("Required status check '$requiredCheck' must have exactly one app-bound checks row.")
        } elseif ([int64]$matchingRows[0].app_id -ne $requiredCheckAppId) {
            $failures.Add("Required status check '$requiredCheck' must be bound to GitHub Actions app id $requiredCheckAppId.")
        }
    }
    if ((@($configuredChecks | Sort-Object) -join "`n") -cne (@($requiredChecks | Sort-Object) -join "`n")) {
        $failures.Add("Protected-branch required status checks must contain exactly the canonical six-check inventory.")
    }

    $reviews = $protection.required_pull_request_reviews
    if ($null -eq $reviews -or [int]$reviews.required_approving_review_count -lt 1) {
        $failures.Add("At least one approving pull-request review is required.")
    }
    Require-Enabled $reviews.dismiss_stale_reviews "Stale-review dismissal"
    Require-Enabled $reviews.require_code_owner_reviews "Code-owner review"
    Require-Enabled $protection.enforce_admins.enabled "Administrator branch-protection enforcement"
    Require-Enabled $protection.required_linear_history.enabled "Required linear history"
    Require-Enabled $protection.required_conversation_resolution.enabled "Required conversation resolution"

    if ($null -ne $reviews) {
        $bypassProperty = $reviews.PSObject.Properties['bypass_pull_request_allowances']
        if ($null -eq $bypassProperty -or $null -eq $bypassProperty.Value) {
            if ([string]$repositoryState.owner.type -ceq "User") {
                # GitHub omits this organization-only shape for personal repositories; omission is
                # the API's authoritative representation that user/team/app allowances cannot exist.
                $reviewBypassShapeValid = $true
                $reviewBypassUnsupportedForUserRepository = $true
            } else {
                $failures.Add("Pull-request review bypass allowances must be explicitly reported by the GitHub API.")
            }
        } else {
            $bypass = $bypassProperty.Value
            $bypassUsersProperty = $bypass.PSObject.Properties['users']
            $bypassTeamsProperty = $bypass.PSObject.Properties['teams']
            $bypassAppsProperty = $bypass.PSObject.Properties['apps']
            $reviewBypassShapeValid =
                $null -ne $bypassUsersProperty -and $null -ne $bypassUsersProperty.Value -and
                $null -ne $bypassTeamsProperty -and $null -ne $bypassTeamsProperty.Value -and
                $null -ne $bypassAppsProperty -and $null -ne $bypassAppsProperty.Value
            if (-not $reviewBypassShapeValid) {
                $failures.Add("Pull-request review bypass allowances must explicitly include users, teams, and apps arrays.")
            } else {
                $reviewBypassUsers = @($bypassUsersProperty.Value | Where-Object { $null -ne $_ })
                $reviewBypassTeams = @($bypassTeamsProperty.Value | Where-Object { $null -ne $_ })
                $reviewBypassApps = @($bypassAppsProperty.Value | Where-Object { $null -ne $_ })
                $reviewBypassAllowanceCount = $reviewBypassUsers.Count + $reviewBypassTeams.Count + $reviewBypassApps.Count
                if ($reviewBypassAllowanceCount -ne 0) {
                    $failures.Add("Pull-request review bypass allowances must be empty.")
                }
            }
        }
    }

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
$configuredCodeScanningLanguages = @($codeScanning.languages | ForEach-Object { [string]$_ } | Sort-Object -Unique)
foreach ($requiredLanguage in $requiredCodeScanningLanguages) {
    if ($configuredCodeScanningLanguages -notcontains $requiredLanguage) {
        $failures.Add("CodeQL default setup is missing required language: $requiredLanguage")
    }
}

Require-Enabled $commit.verification.verified "Release-candidate commit signature verification"
if ([string]$branchState.commit.sha -cne $CommitSha) {
    $failures.Add("Release-candidate commit must exactly match the protected branch HEAD.")
}

$report = [ordered]@{
    generatedAtUtc = [DateTime]::UtcNow.ToString("o")
    repository = $Repository
    branch = $Branch
    commitSha = $CommitSha
    branchHeadCommitSha = [string]$branchState.commit.sha
    status = if ($failures.Count -eq 0) { "passed" } else { "failed" }
    branchProtection = [ordered]@{
        configured = $null -ne $protection
        strictStatusChecks = $protection.required_status_checks.strict
        configuredChecks = $configuredChecks
        configuredCheckBindings = $configuredCheckBindings
        requiredChecks = $requiredChecks
        requiredCheckAppId = $requiredCheckAppId
        approvingReviewCount = $protection.required_pull_request_reviews.required_approving_review_count
        dismissStaleReviews = $protection.required_pull_request_reviews.dismiss_stale_reviews
        requireCodeOwnerReviews = $protection.required_pull_request_reviews.require_code_owner_reviews
        enforceAdmins = $protection.enforce_admins.enabled
        requireLinearHistory = $protection.required_linear_history.enabled
        requireConversationResolution = $protection.required_conversation_resolution.enabled
        reviewBypassAllowanceCount = $reviewBypassAllowanceCount
        reviewBypassShapeValid = $reviewBypassShapeValid
        reviewBypassUnsupportedForUserRepository = $reviewBypassUnsupportedForUserRepository
        allowForcePushes = $protection.allow_force_pushes.enabled
        allowDeletions = $protection.allow_deletions.enabled
        requireSignedCommits = $requiredSignatures.enabled
    }
    security = [ordered]@{
        secretScanning = $repositoryState.security_and_analysis.secret_scanning.status
        pushProtection = $repositoryState.security_and_analysis.secret_scanning_push_protection.status
        dependabotSecurityUpdates = $repositoryState.security_and_analysis.dependabot_security_updates.status
        dependabotVulnerabilityAlerts = $dependabotAlertsEnabled
        automatedSecurityFixes = $automatedSecurityFixes.enabled
        codeScanningState = $codeScanning.state
        codeScanningQuerySuite = $codeScanning.query_suite
        codeScanningSchedule = $codeScanning.schedule
        codeScanningLanguages = $configuredCodeScanningLanguages
        requiredCodeScanningLanguages = $requiredCodeScanningLanguages
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
