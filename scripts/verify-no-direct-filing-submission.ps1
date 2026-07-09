param(
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot ".."),
    [string]$EvidencePath = "",
    [string]$CommitSha = "",
    [string]$GitHubActionsRunUrl = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Add-Failure {
    param(
        [System.Collections.Generic.List[string]]$Failures,
        [string]$Message
    )

    $Failures.Add($Message) | Out-Null
}

function Read-RepoFile {
    param(
        [string]$Root,
        [string]$RelativePath,
        [System.Collections.Generic.List[string]]$Failures
    )

    $path = Join-Path $Root $RelativePath
    if (-not (Test-Path -LiteralPath $path)) {
        Add-Failure $Failures "Missing required no-direct-submission evidence source: $RelativePath"
        return ""
    }

    return Get-Content -LiteralPath $path -Raw
}

function Assert-ContainsText {
    param(
        [string]$Content,
        [string]$Needle,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    if ($Content.IndexOf($Needle, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
        Add-Failure $Failures "$Context is missing required no-direct-submission evidence: $Needle"
    }
}

function Assert-DoesNotMatch {
    param(
        [string]$Content,
        [string]$Pattern,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    $matches = [regex]::Matches($Content, $Pattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    foreach ($match in $matches) {
        Add-Failure $Failures "$Context contains forbidden direct filing automation pattern '$Pattern': $($match.Value)"
    }
}

$failures = [System.Collections.Generic.List[string]]::new()
$resolvedRoot = Resolve-Path -LiteralPath $RepositoryRoot -ErrorAction Stop
$releaseCommitSha = $CommitSha.Trim()
$releaseRunUrl = $GitHubActionsRunUrl.Trim()

if ($releaseCommitSha.Length -eq 0) {
    Add-Failure $failures "CommitSha is required for no-direct filing submission evidence."
} elseif ($releaseCommitSha -notmatch '^[0-9a-fA-F]{7,40}$') {
    Add-Failure $failures "CommitSha must be a 7-40 character hexadecimal Git commit SHA."
}

if ($releaseRunUrl.Length -eq 0) {
    Add-Failure $failures "GitHubActionsRunUrl is required for no-direct filing submission evidence."
} elseif ($releaseRunUrl -notmatch '^https://github\.com/.+/actions/runs/[0-9]+') {
    Add-Failure $failures "GitHubActionsRunUrl must be a GitHub Actions run URL."
}

$filingWorkflowPath = "backend/Accounts.Api/Endpoints/FilingWorkflowEndpoints.cs"
$revenueEndpointsPath = "backend/Accounts.Api/Endpoints/RevenueEndpoints.cs"
$filingServicePath = "backend/Accounts.Api/Services/FilingWorkflowService.cs"
$readinessReportPath = "backend/Accounts.Api/Services/ProductionReadinessReportService.cs"
$filingReviewCentrePath = "frontend/src/components/period/FilingReviewCentre.tsx"
$periodPagePath = "frontend/src/app/companies/[companyId]/periods/[periodId]/page.tsx"
$apiClientPath = "frontend/src/lib/api.ts"

$filingWorkflow = Read-RepoFile $resolvedRoot $filingWorkflowPath $failures
$revenueEndpoints = Read-RepoFile $resolvedRoot $revenueEndpointsPath $failures
$filingService = Read-RepoFile $resolvedRoot $filingServicePath $failures
$readinessReport = Read-RepoFile $resolvedRoot $readinessReportPath $failures
$filingReviewCentre = Read-RepoFile $resolvedRoot $filingReviewCentrePath $failures
$periodPage = Read-RepoFile $resolvedRoot $periodPagePath $failures
$apiClient = Read-RepoFile $resolvedRoot $apiClientPath $failures

$allowedFilingRoutes = @(
    '"/status"',
    '"/readiness-profile"',
    '"/cro-status"',
    '"/cro-payment"',
    '"/charity-report-generated"',
    '"/charity-status"',
    '"/mark-generated"',
    '"/validate-ixbrl"'
)

foreach ($route in $allowedFilingRoutes) {
    Assert-ContainsText $filingWorkflow $route "Filing workflow endpoints" $failures
}

foreach ($text in @(
    "Results.StatusCode(StatusCodes.Status410Gone)",
    "UpdateCroStatusAsync",
    "ConfirmCroPaymentAsync",
    "ValidateIxbrlEndpointAsync",
    "SubmissionReference"
)) {
    Assert-ContainsText $filingWorkflow $text "Filing workflow endpoints" $failures
}

foreach ($text in @(
    'MapGet("/tax-computation"',
    'MapGet("/ct1-support"',
    'MapGet("/ixbrl"',
    "GenerateFinalIxbrlAsync"
)) {
    Assert-ContainsText $revenueEndpoints $text "Revenue endpoints" $failures
}

foreach ($text in @(
    "CRO and ROS final submission remain external actions recorded in workflow state.",
    "Direct CRO submission",
    "Direct ROS submission",
    "updateCroFilingStatus",
    "submissionReference"
)) {
    Assert-ContainsText "$filingReviewCentre`n$periodPage`n$apiClient" $text "Frontend filing workflow" $failures
}

foreach ($text in @(
    "No direct CRO/ROS submission automation",
    "records workflow states and external references only",
    "no-direct-cro-ros-submission-control",
    "No direct CRO/ROS submission automation is enforced as recorded workflow states only."
)) {
    Assert-ContainsText $readinessReport $text "Production readiness report" $failures
}

$operationalBackendFiles = @(
    "backend/Accounts.Api/Endpoints/FilingWorkflowEndpoints.cs",
    "backend/Accounts.Api/Endpoints/RevenueEndpoints.cs",
    "backend/Accounts.Api/Services/FilingWorkflowService.cs"
)

$forbiddenOutboundPatterns = @(
    "\bnew\s+HttpClient\b",
    "\bIHttpClientFactory\b",
    "\.PostAsync\s*\(",
    "\.PutAsync\s*\(",
    "\.SendAsync\s*\(",
    "\.DeleteAsync\s*\(",
    "\bRestClient\b",
    "\bSubmitTo(?:Cro|Ros|Core|Revenue)\b",
    "\b(?:Cro|Ros|Core|Revenue)SubmissionClient\b"
)

foreach ($relativePath in $operationalBackendFiles) {
    $content = Read-RepoFile $resolvedRoot $relativePath $failures
    foreach ($pattern in $forbiddenOutboundPatterns) {
        Assert-DoesNotMatch $content $pattern $relativePath $failures
    }
}

$forbiddenRoutePatterns = @(
    'Map(?:Post|Put|Get|Delete)\(\s*"/submit',
    'Map(?:Post|Put|Get|Delete)\(\s*"/submission',
    'Map(?:Post|Put|Get|Delete)\(\s*"/ros-submit',
    'Map(?:Post|Put|Get|Delete)\(\s*"/cro-submit'
)

foreach ($pattern in $forbiddenRoutePatterns) {
    Assert-DoesNotMatch $filingWorkflow $pattern "Filing workflow endpoint routes" $failures
    Assert-DoesNotMatch $revenueEndpoints $pattern "Revenue endpoint routes" $failures
}

$report = [ordered]@{
    status = if ($failures.Count -eq 0) { "passed" } else { "failed" }
    checkedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    repositoryRoot = $resolvedRoot.Path
    releaseCandidate = [ordered]@{
        commitSha = $releaseCommitSha
        githubActionsRunUrl = $releaseRunUrl
        identityProvided = ($releaseCommitSha.Length -gt 0 -and $releaseRunUrl.Length -gt 0)
    }
    files = [ordered]@{
        filingWorkflowEndpoints = Join-Path $resolvedRoot $filingWorkflowPath
        revenueEndpoints = Join-Path $resolvedRoot $revenueEndpointsPath
        filingWorkflowService = Join-Path $resolvedRoot $filingServicePath
        productionReadinessReport = Join-Path $resolvedRoot $readinessReportPath
        filingReviewCentre = Join-Path $resolvedRoot $filingReviewCentrePath
        periodWorkspace = Join-Path $resolvedRoot $periodPagePath
        frontendApiClient = Join-Path $resolvedRoot $apiClientPath
    }
    allowedRecordedWorkflowRoutes = $allowedFilingRoutes
    forbiddenOutboundPatterns = $forbiddenOutboundPatterns
    forbiddenRoutePatterns = $forbiddenRoutePatterns
    failureCount = $failures.Count
    failures = $failures.ToArray()
}

if ($EvidencePath.Trim().Length -gt 0) {
    $evidenceDirectory = Split-Path -Parent $EvidencePath
    if ($evidenceDirectory -and -not (Test-Path -LiteralPath $evidenceDirectory)) {
        New-Item -ItemType Directory -Path $evidenceDirectory | Out-Null
    }

    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $EvidencePath -Encoding UTF8
}

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) {
        Write-Error $failure -ErrorAction Continue
    }
    throw "No-direct filing submission verification failed with $($failures.Count) issue(s)."
}

Write-Host "No-direct filing submission verification passed for $($resolvedRoot.Path)."
