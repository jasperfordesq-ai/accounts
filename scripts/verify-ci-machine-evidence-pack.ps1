param(
    [string]$EvidenceDirectory = ".",
    [string]$ReportPath = "",
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

function Get-JsonProperty {
    param(
        [object]$Object,
        [string[]]$Path
    )

    $current = $Object
    foreach ($segment in $Path) {
        if ($null -eq $current -or -not ($current.PSObject.Properties.Name -contains $segment)) {
            return $null
        }

        $current = $current.$segment
    }

    return $current
}

function Find-EvidenceFile {
    param(
        [string]$Directory,
        [string]$FileName,
        [System.Collections.Generic.List[string]]$Failures
    )

    $matches = @(Get-ChildItem -LiteralPath $Directory -Recurse -File -Filter $FileName)
    if ($matches.Count -eq 0) {
        Add-Failure $Failures "Missing CI machine evidence file: $FileName"
        return ""
    }

    if ($matches.Count -gt 1) {
        Add-Failure $Failures "Ambiguous CI machine evidence file '$FileName' found $($matches.Count) times."
        return ""
    }

    return $matches[0].FullName
}

function Read-JsonEvidence {
    param(
        [string]$Directory,
        [string]$FileName,
        [System.Collections.Generic.List[string]]$Failures
    )

    $path = Find-EvidenceFile $Directory $FileName $Failures
    if ([string]::IsNullOrWhiteSpace($path)) {
        return [pscustomobject]@{ __missing = $true; __path = "" }
    }

    try {
        $json = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        $json | Add-Member -NotePropertyName __path -NotePropertyValue $path -Force
        return $json
    } catch {
        Add-Failure $Failures "CI machine evidence file is not valid JSON: $FileName"
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

    if ([string](Get-JsonProperty $Evidence @("status")) -ne "passed") {
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

function Assert-VisualSmokeDimensionEvidence {
    param(
        [object]$VisualSmoke,
        [System.Collections.Generic.List[string]]$Failures
    )

    if ($VisualSmoke.PSObject.Properties.Name -contains "__missing" -or
        $VisualSmoke.PSObject.Properties.Name -contains "__invalid") {
        return
    }

    $expectedViewports = @(
        [pscustomobject]@{ name = "desktop"; width = 1440; height = 1000 },
        [pscustomobject]@{ name = "mobile"; width = 390; height = 844 }
    )
    $expectedLayoutChecks = @(
        "browser-console-errors",
        "page-horizontal-overflow",
        "visible-text-overlap"
    )
    $viewportDimensions = Get-JsonProperty $VisualSmoke @("viewportDimensions")
    if ($null -eq $viewportDimensions -or @($viewportDimensions).Count -eq 0) {
        Add-Failure $Failures "visual-smoke-evidence-report.json viewportDimensions must be present."
    } else {
        foreach ($expected in $expectedViewports) {
            $actual = @($viewportDimensions) | Where-Object { [string](Get-JsonProperty $_ @("name")) -eq $expected.name } | Select-Object -First 1
            if ($null -eq $actual) {
                Add-Failure $Failures "visual-smoke-evidence-report.json viewportDimensions must include $($expected.name)."
                continue
            }

            if ([int](Get-JsonProperty $actual @("width")) -ne [int]$expected.width -or
                [int](Get-JsonProperty $actual @("height")) -ne [int]$expected.height) {
                Add-Failure $Failures "visual-smoke-evidence-report.json viewportDimensions.$($expected.name) must be $($expected.width)x$($expected.height)."
            }
        }
    }

    $screenshots = Get-JsonProperty $VisualSmoke @("screenshots")
    if ($null -eq $screenshots -or @($screenshots).Count -eq 0) {
        Add-Failure $Failures "visual-smoke-evidence-report.json screenshots must include PNG dimension evidence."
        return
    }

    $index = 0
    foreach ($screenshot in @($screenshots)) {
        $viewportName = [string](Get-JsonProperty $screenshot @("viewportName"))
        $expected = $expectedViewports | Where-Object { $_.name -eq $viewportName } | Select-Object -First 1
        if ($null -eq $expected) {
            Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.$index viewportName must be a planned viewport."
            $index += 1
            continue
        }

        $imageWidth = Get-JsonProperty $screenshot @("imageWidth")
        $imageHeight = Get-JsonProperty $screenshot @("imageHeight")
        $expectedViewportWidth = Get-JsonProperty $screenshot @("expectedViewportWidth")
        $minimumViewportHeight = Get-JsonProperty $screenshot @("minimumViewportHeight")
        $pixelSampleCount = Get-JsonProperty $screenshot @("pixelSampleCount")
        $sampledDistinctColorCount = Get-JsonProperty $screenshot @("sampledDistinctColorCount")
        $luminanceRange = Get-JsonProperty $screenshot @("luminanceRange")
        $pngIdatByteSize = Get-JsonProperty $screenshot @("pngIdatByteSize")
        $layoutCheckResults = @(Get-JsonProperty $screenshot @("layoutCheckResults"))

        if ($null -eq $imageWidth -or [int]$imageWidth -ne [int]$expected.width) {
            Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.imageWidth must match planned viewport width."
        }
        if ($null -eq $expectedViewportWidth -or [int]$expectedViewportWidth -ne [int]$expected.width) {
            Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.expectedViewportWidth must match planned viewport width."
        }
        if ($null -eq $imageHeight -or [int]$imageHeight -lt [int]$expected.height) {
            Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.imageHeight must be at least the planned viewport height."
        }
        if ($null -eq $minimumViewportHeight -or [int]$minimumViewportHeight -ne [int]$expected.height) {
            Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.minimumViewportHeight must match planned viewport height."
        }
        if ($null -eq $pngIdatByteSize -or [int]$pngIdatByteSize -le 0) {
            Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.pngIdatByteSize must prove retained PNG image data."
        }
        if ($null -eq $pixelSampleCount -or [int]$pixelSampleCount -le 0) {
            Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.pixelSampleCount must be greater than zero."
        }
        if ($null -eq $sampledDistinctColorCount -or [int]$sampledDistinctColorCount -lt 4) {
            Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.sampledDistinctColorCount must be at least 4."
        }
        if ($null -eq $luminanceRange -or [int]$luminanceRange -lt 10) {
            Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.luminanceRange must be at least 10."
        }
        foreach ($layoutCheck in $expectedLayoutChecks) {
            $layoutResult = $layoutCheckResults |
                Where-Object { [string](Get-JsonProperty $_ @("check")) -eq $layoutCheck } |
                Select-Object -First 1
            if ($null -eq $layoutResult) {
                Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.layoutCheckResults must include $layoutCheck."
            } elseif ([string](Get-JsonProperty $layoutResult @("status")) -ne "passed") {
                Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.layoutCheckResults.$layoutCheck status must be passed."
            }
        }

        $index += 1
    }
}

function Get-FileSha256 {
    param(
        [string]$Path
    )

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

$failures = [System.Collections.Generic.List[string]]::new()
$resolvedDirectory = Resolve-Path -LiteralPath $EvidenceDirectory -ErrorAction Stop
$releaseCommitSha = $CommitSha.Trim()
$releaseRunUrl = $GitHubActionsRunUrl.Trim()

if ($releaseCommitSha.Length -eq 0) {
    Add-Failure $failures "CommitSha is required for CI machine evidence packs."
} elseif ($releaseCommitSha -notmatch '^[0-9a-fA-F]{7,40}$') {
    Add-Failure $failures "CommitSha must be a 7-40 character hexadecimal Git commit SHA."
}

if ($releaseRunUrl.Length -eq 0) {
    Add-Failure $failures "GitHubActionsRunUrl is required for CI machine evidence packs."
} elseif ($releaseRunUrl -notmatch '^https://github\.com/.+/actions/runs/[0-9]+') {
    Add-Failure $failures "GitHubActionsRunUrl must be a GitHub Actions run URL."
}

$requiredJsonFiles = @(
    "dependency-audit-report.json",
    "production-safety-report.json",
    "monitoring-error-routing-report.json",
    "structured-log-report.json",
    "restore-drill-report.json",
    "no-direct-filing-submission-report.json",
    "production-readiness-report.json",
    "production-readiness-verification-report.json",
    "visual-smoke-manifest.json",
    "visual-smoke-evidence-report.json",
    "accountant-workbench-evidence-report.json"
)

$dependency = Read-JsonEvidence $resolvedDirectory.Path "dependency-audit-report.json" $failures
$productionSafety = Read-JsonEvidence $resolvedDirectory.Path "production-safety-report.json" $failures
$monitoring = Read-JsonEvidence $resolvedDirectory.Path "monitoring-error-routing-report.json" $failures
$structuredLog = Read-JsonEvidence $resolvedDirectory.Path "structured-log-report.json" $failures
$restore = Read-JsonEvidence $resolvedDirectory.Path "restore-drill-report.json" $failures
$noDirectSubmission = Read-JsonEvidence $resolvedDirectory.Path "no-direct-filing-submission-report.json" $failures
$productionReadiness = Read-JsonEvidence $resolvedDirectory.Path "production-readiness-report.json" $failures
$productionReadinessVerification = Read-JsonEvidence $resolvedDirectory.Path "production-readiness-verification-report.json" $failures
$visualManifest = Read-JsonEvidence $resolvedDirectory.Path "visual-smoke-manifest.json" $failures
$visualSmoke = Read-JsonEvidence $resolvedDirectory.Path "visual-smoke-evidence-report.json" $failures
$accountantWorkbench = Read-JsonEvidence $resolvedDirectory.Path "accountant-workbench-evidence-report.json" $failures

$allEvidence = [ordered]@{
    "dependency-audit-report.json" = $dependency
    "production-safety-report.json" = $productionSafety
    "monitoring-error-routing-report.json" = $monitoring
    "structured-log-report.json" = $structuredLog
    "restore-drill-report.json" = $restore
    "no-direct-filing-submission-report.json" = $noDirectSubmission
    "production-readiness-report.json" = $productionReadiness
    "production-readiness-verification-report.json" = $productionReadinessVerification
    "visual-smoke-manifest.json" = $visualManifest
    "visual-smoke-evidence-report.json" = $visualSmoke
    "accountant-workbench-evidence-report.json" = $accountantWorkbench
}

foreach ($entry in $allEvidence.GetEnumerator()) {
    if ($entry.Key -in @("production-readiness-report.json", "visual-smoke-manifest.json")) {
        continue
    }

    Assert-StatusPassed $entry.Value $entry.Key $failures
}

if (-not ($dependency.PSObject.Properties.Name -contains "__missing")) {
    Assert-NonEmptyString (Get-JsonProperty $dependency @("frontend", "packageLockSha256")) "dependency-audit-report.json frontend.packageLockSha256" $failures
    Assert-Truthy (Get-JsonProperty $dependency @("backend", "nugetAudit", "enabled")) "dependency-audit-report.json backend.nugetAudit.enabled" $failures
    Assert-Truthy (Get-JsonProperty $dependency @("ci", "runsNpmAuditModerate")) "dependency-audit-report.json ci.runsNpmAuditModerate" $failures
    Assert-Truthy (Get-JsonProperty $dependency @("ci", "runsCiActionVerifier")) "dependency-audit-report.json ci.runsCiActionVerifier" $failures
}

if (-not ($productionSafety.PSObject.Properties.Name -contains "__missing")) {
    if ([string](Get-JsonProperty $productionSafety @("migrationSafety", "apiDependsOnMigrate")) -ne "service_completed_successfully") {
        Add-Failure $failures "production-safety-report.json migrationSafety.apiDependsOnMigrate must be service_completed_successfully."
    }
    Assert-Truthy (Get-JsonProperty $productionSafety @("seedSafety", "bootstrapOwnerPasswordOnlyOnMigrate")) "production-safety-report.json seedSafety.bootstrapOwnerPasswordOnlyOnMigrate" $failures
    if ((Get-JsonProperty $productionSafety @("workflowSafety", "productionSmokeUsesBuildFlag")) -ne $false) {
        Add-Failure $failures "production-safety-report.json workflowSafety.productionSmokeUsesBuildFlag must be false."
    }
}

if (-not ($monitoring.PSObject.Properties.Name -contains "__missing")) {
    Assert-NonEmptyString (Get-JsonProperty $monitoring @("provider")) "monitoring-error-routing-report.json provider" $failures
    Assert-NonEmptyString (Get-JsonProperty $monitoring @("eventId")) "monitoring-error-routing-report.json eventId" $failures
    Assert-NonEmptyString (Get-JsonProperty $monitoring @("correlationId")) "monitoring-error-routing-report.json correlationId" $failures
    Assert-NonEmptyString (Get-JsonProperty $monitoring @("baseUrl")) "monitoring-error-routing-report.json baseUrl" $failures
}

if (-not ($structuredLog.PSObject.Properties.Name -contains "__missing")) {
    if ([int](Get-JsonProperty $structuredLog @("jsonLogLineCount")) -le 0) {
        Add-Failure $failures "structured-log-report.json jsonLogLineCount must be greater than zero."
    }
    Assert-Truthy (Get-JsonProperty $structuredLog @("matchedMonitoringSmokeLine")) "structured-log-report.json matchedMonitoringSmokeLine" $failures
    $monitoringCorrelationId = [string](Get-JsonProperty $monitoring @("correlationId"))
    if (-not [string]::IsNullOrWhiteSpace($monitoringCorrelationId) -and
        [string](Get-JsonProperty $structuredLog @("monitoringCorrelationId")) -ne $monitoringCorrelationId) {
        Add-Failure $failures "structured-log-report.json monitoringCorrelationId must match monitoring-error-routing-report.json correlationId."
    }
}

if (-not ($restore.PSObject.Properties.Name -contains "__missing")) {
    if ([string](Get-JsonProperty $restore @("backupSha256")) -notmatch '^[0-9a-f]{64}$') {
        Add-Failure $failures "restore-drill-report.json backupSha256 must be a lowercase SHA-256 hash."
    }
    foreach ($check in @((Get-JsonProperty $restore @("tableChecks")))) {
        if ([int](Get-JsonProperty $check @("restoredCount")) -ne [int](Get-JsonProperty $check @("sourceCount"))) {
            Add-Failure $failures "restore-drill-report.json table '$((Get-JsonProperty $check @("table")))' restoredCount must match sourceCount."
        }
    }
    foreach ($table in @("tenants", "user accounts")) {
        if (-not (@((Get-JsonProperty $restore @("tableChecks"))) | Where-Object { [string](Get-JsonProperty $_ @("table")) -eq $table })) {
            Add-Failure $failures "restore-drill-report.json tableChecks must include $table."
        }
    }
}

if (-not ($noDirectSubmission.PSObject.Properties.Name -contains "__missing")) {
    if ([int](Get-JsonProperty $noDirectSubmission @("failureCount")) -ne 0) {
        Add-Failure $failures "no-direct-filing-submission-report.json failureCount must be zero."
    }
    foreach ($route in @('"/cro-status"', '"/cro-payment"', '"/validate-ixbrl"')) {
        Assert-ArrayContains @((Get-JsonProperty $noDirectSubmission @("allowedRecordedWorkflowRoutes"))) $route "no-direct-filing-submission-report.json allowedRecordedWorkflowRoutes" $failures
    }
}

if (-not ($productionReadiness.PSObject.Properties.Name -contains "__missing")) {
    if ([string](Get-JsonProperty $productionReadiness @("overallStatus")) -ne "review-required") {
        Add-Failure $failures "production-readiness-report.json overallStatus must be review-required."
    }
    if ([int](Get-JsonProperty $productionReadiness @("productionScorecard", "targetScore")) -ne 700) {
        Add-Failure $failures "production-readiness-report.json productionScorecard.targetScore must be 700."
    }
    foreach ($requiredEvidence in @("production-scorecard", "production-readiness-report", "production-readiness-verification-report", "release-verification-manifest", "release-blocker-register")) {
        Assert-ArrayContains @((Get-JsonProperty $productionReadiness @("assurancePacket", "evidenceItems"))) $requiredEvidence "production-readiness-report.json assurancePacket.evidenceItems" $failures
    }
}

if (-not ($productionReadinessVerification.PSObject.Properties.Name -contains "__missing")) {
    if ([int](Get-JsonProperty $productionReadinessVerification @("failureCount")) -ne 0) {
        Add-Failure $failures "production-readiness-verification-report.json failureCount must be zero."
    }
    if ([int](Get-JsonProperty $productionReadinessVerification @("requiredCoverage", "expectedVisualScreenshotCount")) -ne 28) {
        Add-Failure $failures "production-readiness-verification-report.json requiredCoverage.expectedVisualScreenshotCount must be 28."
    }
}

if (-not ($visualManifest.PSObject.Properties.Name -contains "__missing")) {
    if ([string](Get-JsonProperty $visualManifest @("artifactName")) -ne "visual-smoke-screenshots") {
        Add-Failure $failures "visual-smoke-manifest.json artifactName must be visual-smoke-screenshots."
    }
    if ([int](Get-JsonProperty $visualManifest @("expectedScreenshotCount")) -ne 28) {
        Add-Failure $failures "visual-smoke-manifest.json expectedScreenshotCount must be 28."
    }
}

if (-not ($visualSmoke.PSObject.Properties.Name -contains "__missing")) {
    if ([int](Get-JsonProperty $visualSmoke @("screenshotCount")) -ne 28 -or [int](Get-JsonProperty $visualSmoke @("expectedScreenshotCount")) -ne 28) {
        Add-Failure $failures "visual-smoke-evidence-report.json must cover 28 expected screenshots."
    }
    if ([int](Get-JsonProperty $visualSmoke @("routeCount")) -ne 7) {
        Add-Failure $failures "visual-smoke-evidence-report.json routeCount must be 7."
    }
    Assert-VisualSmokeDimensionEvidence $visualSmoke $failures
}

if (-not ($accountantWorkbench.PSObject.Properties.Name -contains "__missing")) {
    if ([int](Get-JsonProperty $accountantWorkbench @("routeCount")) -ne 7) {
        Add-Failure $failures "accountant-workbench-evidence-report.json routeCount must be 7."
    }
    if ([int](Get-JsonProperty $accountantWorkbench @("screenshotCount")) -ne 28 -or [int](Get-JsonProperty $accountantWorkbench @("expectedScreenshotCount")) -ne 28) {
        Add-Failure $failures "accountant-workbench-evidence-report.json must cover 28 expected screenshots."
    }
    if ([int](Get-JsonProperty $accountantWorkbench @("routeAcceptanceCount")) -ne 7) {
        Add-Failure $failures "accountant-workbench-evidence-report.json routeAcceptanceCount must be 7."
    }
    if ([string](Get-JsonProperty $accountantWorkbench @("requiredCoverage", "routeAcceptanceSignOffGate")) -ne "qualified-accountant-route-acceptance") {
        Add-Failure $failures "accountant-workbench-evidence-report.json requiredCoverage.routeAcceptanceSignOffGate must be qualified-accountant-route-acceptance."
    }
}

$evidenceFileManifest = @(
    foreach ($file in Get-ChildItem -LiteralPath $resolvedDirectory.Path -Recurse -File | Sort-Object FullName) {
        $relativePath = $file.FullName.Substring($resolvedDirectory.Path.Length).TrimStart(
            [IO.Path]::DirectorySeparatorChar,
            [IO.Path]::AltDirectorySeparatorChar)
        [ordered]@{
            fileName = $file.Name
            relativePath = $relativePath
            byteSize = $file.Length
            sha256 = Get-FileSha256 $file.FullName
        }
    }
)

$report = [ordered]@{
    status = if ($failures.Count -eq 0) { "passed" } else { "failed" }
    checkedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    evidenceDirectory = $resolvedDirectory.Path
    releaseCandidate = [ordered]@{
        commitSha = $releaseCommitSha
        githubActionsRunUrl = $releaseRunUrl
        identityProvided = ($releaseCommitSha.Length -gt 0 -and $releaseRunUrl.Length -gt 0)
    }
    requiredFiles = $requiredJsonFiles
    humanEvidenceStillRequired = @(
        "release-evidence-report.json",
        "source-law-review-template.md named reviewer completion",
        "visual-qa-signoff-template.md named reviewer completion",
        "qualified-accountant-acceptance-template.md named accountant completion",
        "external-ros-ixbrl-validation-template.md external validation references",
        "manual-handoff-acceptance-template.md named handoff acceptance",
        "monitoring-provider-confirmation-template.md provider-console confirmation"
    )
    evidenceFiles = $evidenceFileManifest
    failureCount = $failures.Count
    failures = $failures.ToArray()
}

if ($ReportPath.Trim().Length -gt 0) {
    $reportDirectory = Split-Path -Parent $ReportPath
    if ($reportDirectory -and -not (Test-Path -LiteralPath $reportDirectory)) {
        New-Item -ItemType Directory -Path $reportDirectory | Out-Null
    }

    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
}

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) {
        Write-Error $failure -ErrorAction Continue
    }
    throw "CI machine evidence pack verification failed with $($failures.Count) issue(s)."
}

Write-Host "CI machine evidence pack verification passed for $($resolvedDirectory.Path)."
