param(
    [string]$EvidenceDirectory = ".",
    [string]$ReportPath = ""
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

function Read-JsonEvidence {
    param(
        [string]$Directory,
        [string]$FileName,
        [System.Collections.Generic.List[string]]$Failures
    )

    $path = Join-Path $Directory $FileName
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        Add-Failure $Failures "Missing release artifact evidence file: $FileName"
        return [pscustomobject]@{ __missing = $true; __path = $path }
    }

    try {
        $json = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        $json | Add-Member -NotePropertyName __path -NotePropertyValue $path -Force
        return $json
    } catch {
        Add-Failure $Failures "Release artifact evidence file is not valid JSON: $FileName"
        return [pscustomobject]@{ __invalid = $true; __path = $path }
    }
}

function Assert-StatusPassed {
    param(
        [object]$Evidence,
        [string]$FileName,
        [System.Collections.Generic.List[string]]$Failures
    )

    if ($Evidence.PSObject.Properties.Name -contains "__missing" -or $Evidence.PSObject.Properties.Name -contains "__invalid") {
        return
    }

    if ([string]$Evidence.status -ne "passed") {
        Add-Failure $Failures "$FileName must have status 'passed'."
    }
}

function Assert-Truthy {
    param(
        [object]$Value,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    if ($Value -ne $true) {
        Add-Failure $Failures "$Context must be true."
    }
}

function Assert-NonEmptyString {
    param(
        [object]$Value,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    if ([string]::IsNullOrWhiteSpace([string]$Value)) {
        Add-Failure $Failures "$Context must be present."
    }
}

function Assert-ArrayContains {
    param(
        [object[]]$Values,
        [string]$Needle,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    if (-not (@($Values) -contains $Needle)) {
        Add-Failure $Failures "$Context must include $Needle."
    }
}

$failures = [System.Collections.Generic.List[string]]::new()
$resolvedDirectory = Resolve-Path -LiteralPath $EvidenceDirectory -ErrorAction Stop

$dependency = Read-JsonEvidence $resolvedDirectory.Path "dependency-audit-report.json" $failures
$productionSafety = Read-JsonEvidence $resolvedDirectory.Path "production-safety-report.json" $failures
$monitoring = Read-JsonEvidence $resolvedDirectory.Path "monitoring-error-routing-report.json" $failures
$structuredLog = Read-JsonEvidence $resolvedDirectory.Path "structured-log-report.json" $failures
$restore = Read-JsonEvidence $resolvedDirectory.Path "restore-drill-report.json" $failures
$noDirectSubmission = Read-JsonEvidence $resolvedDirectory.Path "no-direct-filing-submission-report.json" $failures
$visualSmoke = Read-JsonEvidence $resolvedDirectory.Path "visual-smoke-evidence-report.json" $failures
$accountantWorkbench = Read-JsonEvidence $resolvedDirectory.Path "accountant-workbench-evidence-report.json" $failures
$releaseEvidence = Read-JsonEvidence $resolvedDirectory.Path "release-evidence-report.json" $failures

$allEvidence = [ordered]@{
    "dependency-audit-report.json" = $dependency
    "production-safety-report.json" = $productionSafety
    "monitoring-error-routing-report.json" = $monitoring
    "structured-log-report.json" = $structuredLog
    "restore-drill-report.json" = $restore
    "no-direct-filing-submission-report.json" = $noDirectSubmission
    "visual-smoke-evidence-report.json" = $visualSmoke
    "accountant-workbench-evidence-report.json" = $accountantWorkbench
    "release-evidence-report.json" = $releaseEvidence
}

foreach ($entry in $allEvidence.GetEnumerator()) {
    Assert-StatusPassed $entry.Value $entry.Key $failures
}

if (-not ($dependency.PSObject.Properties.Name -contains "__missing")) {
    Assert-NonEmptyString $dependency.frontend.packageLockSha256 "dependency-audit-report.json frontend.packageLockSha256" $failures
    Assert-Truthy $dependency.backend.nugetAudit.enabled "dependency-audit-report.json backend.nugetAudit.enabled" $failures
    Assert-Truthy $dependency.ci.runsNpmAuditModerate "dependency-audit-report.json ci.runsNpmAuditModerate" $failures
    Assert-Truthy $dependency.ci.runsCiActionVerifier "dependency-audit-report.json ci.runsCiActionVerifier" $failures
}

if (-not ($productionSafety.PSObject.Properties.Name -contains "__missing")) {
    if ([string]$productionSafety.migrationSafety.apiDependsOnMigrate -ne "service_completed_successfully") {
        Add-Failure $failures "production-safety-report.json migrationSafety.apiDependsOnMigrate must be service_completed_successfully."
    }
    Assert-Truthy $productionSafety.seedSafety.bootstrapOwnerPasswordOnlyOnMigrate "production-safety-report.json seedSafety.bootstrapOwnerPasswordOnlyOnMigrate" $failures
    if ($productionSafety.workflowSafety.productionSmokeUsesBuildFlag -ne $false) {
        Add-Failure $failures "production-safety-report.json workflowSafety.productionSmokeUsesBuildFlag must be false."
    }
}

if (-not ($monitoring.PSObject.Properties.Name -contains "__missing")) {
    Assert-NonEmptyString $monitoring.provider "monitoring-error-routing-report.json provider" $failures
    Assert-NonEmptyString $monitoring.eventId "monitoring-error-routing-report.json eventId" $failures
    Assert-NonEmptyString $monitoring.correlationId "monitoring-error-routing-report.json correlationId" $failures
    Assert-NonEmptyString $monitoring.baseUrl "monitoring-error-routing-report.json baseUrl" $failures
}

if (-not ($structuredLog.PSObject.Properties.Name -contains "__missing")) {
    if ([int]$structuredLog.jsonLogLineCount -le 0) {
        Add-Failure $failures "structured-log-report.json jsonLogLineCount must be greater than zero."
    }
    Assert-Truthy $structuredLog.matchedMonitoringSmokeLine "structured-log-report.json matchedMonitoringSmokeLine" $failures
    if (-not [string]::IsNullOrWhiteSpace([string]$monitoring.correlationId) -and
        [string]$structuredLog.monitoringCorrelationId -ne [string]$monitoring.correlationId) {
        Add-Failure $failures "structured-log-report.json monitoringCorrelationId must match monitoring-error-routing-report.json correlationId."
    }
}

if (-not ($restore.PSObject.Properties.Name -contains "__missing")) {
    if ([string]$restore.backupSha256 -notmatch '^[0-9a-f]{64}$') {
        Add-Failure $failures "restore-drill-report.json backupSha256 must be a lowercase SHA-256 hash."
    }
    foreach ($check in @($restore.tableChecks)) {
        if ([int]$check.restoredCount -ne [int]$check.sourceCount) {
            Add-Failure $failures "restore-drill-report.json table '$($check.table)' restoredCount must match sourceCount."
        }
    }
    foreach ($table in @("tenants", "user accounts")) {
        if (-not (@($restore.tableChecks) | Where-Object { [string]$_.table -eq $table })) {
            Add-Failure $failures "restore-drill-report.json tableChecks must include $table."
        }
    }
}

if (-not ($noDirectSubmission.PSObject.Properties.Name -contains "__missing")) {
    if ([int]$noDirectSubmission.failureCount -ne 0) {
        Add-Failure $failures "no-direct-filing-submission-report.json failureCount must be zero."
    }
    foreach ($route in @('"/cro-status"', '"/cro-payment"', '"/validate-ixbrl"')) {
        Assert-ArrayContains @($noDirectSubmission.allowedRecordedWorkflowRoutes) $route "no-direct-filing-submission-report.json allowedRecordedWorkflowRoutes" $failures
    }
}

if (-not ($visualSmoke.PSObject.Properties.Name -contains "__missing")) {
    if ([int]$visualSmoke.screenshotCount -ne 28 -or [int]$visualSmoke.expectedScreenshotCount -ne 28) {
        Add-Failure $failures "visual-smoke-evidence-report.json must cover 28 expected screenshots."
    }
    if ([int]$visualSmoke.routeCount -ne 7) {
        Add-Failure $failures "visual-smoke-evidence-report.json routeCount must be 7."
    }
}

if (-not ($accountantWorkbench.PSObject.Properties.Name -contains "__missing")) {
    if ([int]$accountantWorkbench.routeCount -ne 7) {
        Add-Failure $failures "accountant-workbench-evidence-report.json routeCount must be 7."
    }
    if ([int]$accountantWorkbench.screenshotCount -ne 28 -or [int]$accountantWorkbench.expectedScreenshotCount -ne 28) {
        Add-Failure $failures "accountant-workbench-evidence-report.json must cover 28 expected screenshots."
    }
    foreach ($coverageProperty in @("routeCodes", "routeKeys", "workflowStages", "themes", "viewports", "reviewChecks", "layoutChecks", "evidenceFiles")) {
        if ($null -eq $accountantWorkbench.requiredCoverage.$coverageProperty -or @($accountantWorkbench.requiredCoverage.$coverageProperty).Count -eq 0) {
            Add-Failure $failures "accountant-workbench-evidence-report.json requiredCoverage.$coverageProperty must be present."
        }
    }
    foreach ($requiredEvidenceFile in @("visual-smoke-manifest.json", "visual-smoke-evidence-report.json", "accountant-workbench-evidence-report.json")) {
        Assert-ArrayContains @($accountantWorkbench.requiredCoverage.evidenceFiles) $requiredEvidenceFile "accountant-workbench-evidence-report.json requiredCoverage.evidenceFiles" $failures
    }
}

if (-not ($releaseEvidence.PSObject.Properties.Name -contains "__missing")) {
    if ([int]$releaseEvidence.failureCount -ne 0) {
        Add-Failure $failures "release-evidence-report.json failureCount must be zero."
    }
    foreach ($coverageProperty in @("sourceLawSourceIds", "goldenCorpusScenarioCodes", "externalRosIxbrlScenarioCodes", "routeCodes", "manualHandoffScenarioCodes", "manualHandoffPathCodes", "releaseArtifactNames")) {
        if ($null -eq $releaseEvidence.requiredCoverage.$coverageProperty -or @($releaseEvidence.requiredCoverage.$coverageProperty).Count -eq 0) {
            Add-Failure $failures "release-evidence-report.json requiredCoverage.$coverageProperty must be present."
        }
    }
}

$report = [ordered]@{
    status = if ($failures.Count -eq 0) { "passed" } else { "failed" }
    checkedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    evidenceDirectory = $resolvedDirectory.Path
    requiredFiles = @($allEvidence.Keys)
    failureCount = $failures.Count
    failures = $failures.ToArray()
}

if ($ReportPath.Trim().Length -gt 0) {
    $reportDirectory = Split-Path -Parent $ReportPath
    if ($reportDirectory -and -not (Test-Path -LiteralPath $reportDirectory)) {
        New-Item -ItemType Directory -Path $reportDirectory | Out-Null
    }

    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
}

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) {
        Write-Error $failure -ErrorAction Continue
    }
    throw "Release artifact pack verification failed with $($failures.Count) issue(s)."
}

Write-Host "Release artifact pack verification passed for $($resolvedDirectory.Path)."
