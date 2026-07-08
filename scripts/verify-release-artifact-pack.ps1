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

function Assert-ReleaseEvidenceTemplateManifest {
    param(
        [object]$ReleaseEvidence,
        [string]$Directory,
        [object[]]$RequiredTemplates,
        [System.Collections.Generic.List[string]]$Failures
    )

    if ($ReleaseEvidence.PSObject.Properties.Name -contains "__missing" -or
        $ReleaseEvidence.PSObject.Properties.Name -contains "__invalid") {
        return
    }

    $manifest = @(Get-JsonProperty $ReleaseEvidence @("evidenceFiles"))
    if ($manifest.Count -eq 0) {
        Add-Failure $Failures "release-evidence-report.json evidenceFiles must include retained release evidence template hashes."
        return
    }

    foreach ($required in $RequiredTemplates) {
        $entry = $manifest |
            Where-Object {
                [string](Get-JsonProperty $_ @("fileName")) -eq [string]$required.fileName -and
                [string](Get-JsonProperty $_ @("evidenceName")) -eq [string]$required.evidenceName
            } |
            Select-Object -First 1

        if ($null -eq $entry) {
            Add-Failure $Failures "release-evidence-report.json evidenceFiles must include $($required.fileName)."
            continue
        }

        if ((Get-JsonProperty $entry @("present")) -ne $true) {
            Add-Failure $Failures "release-evidence-report.json evidenceFiles.$($required.fileName).present must be true."
        }

        $manifestSha = [string](Get-JsonProperty $entry @("sha256"))
        if ($manifestSha -notmatch '^[0-9a-f]{64}$') {
            Add-Failure $Failures "release-evidence-report.json evidenceFiles.$($required.fileName).sha256 must be a lowercase SHA-256 hash."
        }

        $manifestByteSize = Get-JsonProperty $entry @("byteSize")
        if ($null -eq $manifestByteSize -or [int]$manifestByteSize -le 0) {
            Add-Failure $Failures "release-evidence-report.json evidenceFiles.$($required.fileName).byteSize must be greater than zero."
        }

        $templatePath = Join-Path $Directory $required.fileName
        if (-not (Test-Path -LiteralPath $templatePath -PathType Leaf)) {
            Add-Failure $Failures "Release artifact pack must include completed release evidence template: $($required.fileName)"
            continue
        }

        $templateInfo = Get-Item -LiteralPath $templatePath
        if ($null -ne $manifestByteSize -and [int64]$manifestByteSize -ne [int64]$templateInfo.Length) {
            Add-Failure $Failures "release-evidence-report.json evidenceFiles.$($required.fileName).byteSize must match the retained template file."
        }

        $actualSha = Get-FileSha256 $templatePath
        if ($manifestSha -match '^[0-9a-f]{64}$' -and $manifestSha -ne $actualSha) {
            Add-Failure $Failures "release-evidence-report.json evidenceFiles.$($required.fileName).sha256 must match the retained template file."
        }
    }
}

$failures = [System.Collections.Generic.List[string]]::new()
$resolvedDirectory = Resolve-Path -LiteralPath $EvidenceDirectory -ErrorAction Stop
$releaseCommitSha = $CommitSha.Trim()
$releaseRunUrl = $GitHubActionsRunUrl.Trim()

if (($releaseCommitSha.Length -gt 0 -and $releaseRunUrl.Length -eq 0) -or
    ($releaseCommitSha.Length -eq 0 -and $releaseRunUrl.Length -gt 0)) {
    Add-Failure $failures "CommitSha and GitHubActionsRunUrl must be provided together when release candidate identity is supplied."
}

if ($releaseCommitSha.Length -gt 0 -and $releaseCommitSha -notmatch '^[0-9a-fA-F]{7,40}$') {
    Add-Failure $failures "CommitSha must be a 7-40 character hexadecimal Git commit SHA."
}

if ($releaseRunUrl.Length -gt 0 -and $releaseRunUrl -notmatch '^https://github\.com/.+/actions/runs/[0-9]+') {
    Add-Failure $failures "GitHubActionsRunUrl must be a GitHub Actions run URL."
}

$dependency = Read-JsonEvidence $resolvedDirectory.Path "dependency-audit-report.json" $failures
$productionSafety = Read-JsonEvidence $resolvedDirectory.Path "production-safety-report.json" $failures
$monitoring = Read-JsonEvidence $resolvedDirectory.Path "monitoring-error-routing-report.json" $failures
$structuredLog = Read-JsonEvidence $resolvedDirectory.Path "structured-log-report.json" $failures
$restore = Read-JsonEvidence $resolvedDirectory.Path "restore-drill-report.json" $failures
$noDirectSubmission = Read-JsonEvidence $resolvedDirectory.Path "no-direct-filing-submission-report.json" $failures
$productionReadiness = Read-JsonEvidence $resolvedDirectory.Path "production-readiness-report.json" $failures
$productionReadinessVerification = Read-JsonEvidence $resolvedDirectory.Path "production-readiness-verification-report.json" $failures
$visualSmoke = Read-JsonEvidence $resolvedDirectory.Path "visual-smoke-evidence-report.json" $failures
$accountantWorkbench = Read-JsonEvidence $resolvedDirectory.Path "accountant-workbench-evidence-report.json" $failures
$releaseEvidence = Read-JsonEvidence $resolvedDirectory.Path "release-evidence-report.json" $failures

$requiredReadinessManifestCodes = @(
    "backend-golden-corpus",
    "frontend-workbench-contract",
    "frontend-production-build",
    "visual-smoke-light-dark",
    "production-readiness-report-verification",
    "ci-machine-evidence-pack",
    "release-artifact-pack",
    "production-stack-smoke",
    "backup-restore-drill",
    "qualified-accountant-final-signoff",
    "source-law-change-review",
    "external-ros-validation-evidence",
    "no-direct-cro-ros-submission-control",
    "manual-accountant-acceptance"
)

$requiredReleaseEvidenceTemplates = @(
    [pscustomobject]@{ evidenceName = "visualQa"; fileName = "visual-qa-signoff-template.md" },
    [pscustomobject]@{ evidenceName = "sourceLawReview"; fileName = "source-law-review-template.md" },
    [pscustomobject]@{ evidenceName = "externalRosIxbrlValidation"; fileName = "external-ros-ixbrl-validation-template.md" },
    [pscustomobject]@{ evidenceName = "qualifiedAccountantAcceptance"; fileName = "qualified-accountant-acceptance-template.md" },
    [pscustomobject]@{ evidenceName = "manualHandoffAcceptance"; fileName = "manual-handoff-acceptance-template.md" },
    [pscustomobject]@{ evidenceName = "monitoringProviderConfirmation"; fileName = "monitoring-provider-confirmation-template.md" }
)

$allEvidence = [ordered]@{
    "dependency-audit-report.json" = $dependency
    "production-safety-report.json" = $productionSafety
    "monitoring-error-routing-report.json" = $monitoring
    "structured-log-report.json" = $structuredLog
    "restore-drill-report.json" = $restore
    "no-direct-filing-submission-report.json" = $noDirectSubmission
    "production-readiness-report.json" = $productionReadiness
    "production-readiness-verification-report.json" = $productionReadinessVerification
    "visual-smoke-evidence-report.json" = $visualSmoke
    "accountant-workbench-evidence-report.json" = $accountantWorkbench
    "release-evidence-report.json" = $releaseEvidence
}

foreach ($entry in $allEvidence.GetEnumerator()) {
    if ($entry.Key -eq "production-readiness-report.json") {
        continue
    }

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

if (-not ($productionReadiness.PSObject.Properties.Name -contains "__missing")) {
    if ([string]$productionReadiness.overallStatus -ne "review-required") {
        Add-Failure $failures "production-readiness-report.json overallStatus must be review-required."
    }
    Assert-NonEmptyString $productionReadiness.generatedAt "production-readiness-report.json generatedAt" $failures
    if ($null -eq $productionReadiness.productionScorecard) {
        Add-Failure $failures "production-readiness-report.json productionScorecard must be present."
    } else {
        if ([int]$productionReadiness.productionScorecard.currentScore -le 0) {
            Add-Failure $failures "production-readiness-report.json productionScorecard.currentScore must be greater than zero."
        }
        if ([int]$productionReadiness.productionScorecard.targetScore -ne 700) {
            Add-Failure $failures "production-readiness-report.json productionScorecard.targetScore must be 700."
        }
        foreach ($categoryCode in @("architecture-documentation", "backend-statutory-accounting-engine", "frontend-accountant-workbench", "security-auth-tenant-platform-guardrails")) {
            if (-not (@($productionReadiness.productionScorecard.categories) | Where-Object { [string]$_.code -eq $categoryCode })) {
                Add-Failure $failures "production-readiness-report.json productionScorecard.categories must include $categoryCode."
            }
        }
    }
    foreach ($requiredEvidence in @("production-scorecard", "production-readiness-report", "production-readiness-verification-report", "release-verification-manifest", "release-blocker-register")) {
        Assert-ArrayContains @($productionReadiness.assurancePacket.evidenceItems) $requiredEvidence "production-readiness-report.json assurancePacket.evidenceItems" $failures
    }
    foreach ($requiredCollection in @("sourceLawSnapshot", "goldenFilingCorpus", "releaseBlockerRegister", "releaseVerificationManifest", "visualQaCoverage")) {
        if ($null -eq $productionReadiness.$requiredCollection) {
            Add-Failure $failures "production-readiness-report.json $requiredCollection must be present."
        }
    }
}

if (-not ($productionReadinessVerification.PSObject.Properties.Name -contains "__missing")) {
    if ([int]$productionReadinessVerification.failureCount -ne 0) {
        Add-Failure $failures "production-readiness-verification-report.json failureCount must be zero."
    }
    foreach ($coverageProperty in @("categoryCodes", "goldenCorpusScenarioCodes", "sourceLawSourceIds", "releaseVerificationManifestCodes", "assuranceEvidenceItems")) {
        if ($null -eq $productionReadinessVerification.requiredCoverage.$coverageProperty -or @($productionReadinessVerification.requiredCoverage.$coverageProperty).Count -eq 0) {
            Add-Failure $failures "production-readiness-verification-report.json requiredCoverage.$coverageProperty must be present."
        }
    }
    foreach ($scenarioCode in @("micro-ltd", "small-abridged-ltd", "dac-small", "clg-charity", "medium-audit-required")) {
        Assert-ArrayContains @($productionReadinessVerification.requiredCoverage.goldenCorpusScenarioCodes) $scenarioCode "production-readiness-verification-report.json requiredCoverage.goldenCorpusScenarioCodes" $failures
    }
    foreach ($manifestCode in $requiredReadinessManifestCodes) {
        Assert-ArrayContains @($productionReadinessVerification.requiredCoverage.releaseVerificationManifestCodes) $manifestCode "production-readiness-verification-report.json requiredCoverage.releaseVerificationManifestCodes" $failures
    }
    if ([int]$productionReadinessVerification.requiredCoverage.expectedVisualScreenshotCount -ne 28) {
        Add-Failure $failures "production-readiness-verification-report.json requiredCoverage.expectedVisualScreenshotCount must be 28."
    }
    if (-not [string]::IsNullOrWhiteSpace([string]$productionReadiness.__path) -and
        -not [string]::IsNullOrWhiteSpace([string]$productionReadinessVerification.reportPath) -and
        [IO.Path]::GetFileName([string]$productionReadinessVerification.reportPath) -ne "production-readiness-report.json") {
        Add-Failure $failures "production-readiness-verification-report.json reportPath must reference production-readiness-report.json."
    }
}

if (-not ($visualSmoke.PSObject.Properties.Name -contains "__missing")) {
    if ([int]$visualSmoke.screenshotCount -ne 28 -or [int]$visualSmoke.expectedScreenshotCount -ne 28) {
        Add-Failure $failures "visual-smoke-evidence-report.json must cover 28 expected screenshots."
    }
    if ([int]$visualSmoke.routeCount -ne 7) {
        Add-Failure $failures "visual-smoke-evidence-report.json routeCount must be 7."
    }
    Assert-VisualSmokeDimensionEvidence $visualSmoke $failures
}

if (-not ($accountantWorkbench.PSObject.Properties.Name -contains "__missing")) {
    if ([int]$accountantWorkbench.routeCount -ne 7) {
        Add-Failure $failures "accountant-workbench-evidence-report.json routeCount must be 7."
    }
    if ([int]$accountantWorkbench.screenshotCount -ne 28 -or [int]$accountantWorkbench.expectedScreenshotCount -ne 28) {
        Add-Failure $failures "accountant-workbench-evidence-report.json must cover 28 expected screenshots."
    }
    if ([int]$accountantWorkbench.routeAcceptanceCount -ne 7) {
        Add-Failure $failures "accountant-workbench-evidence-report.json routeAcceptanceCount must be 7."
    }
    foreach ($coverageProperty in @("routeCodes", "routeKeys", "workflowStages", "themes", "viewports", "reviewChecks", "layoutChecks", "expectedTextChecks", "routeAcceptanceEvidence", "evidenceFiles")) {
        if ($null -eq $accountantWorkbench.requiredCoverage.$coverageProperty -or @($accountantWorkbench.requiredCoverage.$coverageProperty).Count -eq 0) {
            Add-Failure $failures "accountant-workbench-evidence-report.json requiredCoverage.$coverageProperty must be present."
        }
    }
    if ([string]$accountantWorkbench.requiredCoverage.routeAcceptanceSignOffGate -ne "qualified-accountant-route-acceptance") {
        Add-Failure $failures "accountant-workbench-evidence-report.json requiredCoverage.routeAcceptanceSignOffGate must be qualified-accountant-route-acceptance."
    }
    foreach ($requiredEvidenceFile in @("visual-smoke-manifest.json", "visual-smoke-evidence-report.json", "accountant-workbench-evidence-report.json")) {
        Assert-ArrayContains @($accountantWorkbench.requiredCoverage.evidenceFiles) $requiredEvidenceFile "accountant-workbench-evidence-report.json requiredCoverage.evidenceFiles" $failures
    }
    foreach ($route in @($accountantWorkbench.routeAcceptance)) {
        Assert-NonEmptyString $route.routeName "accountant-workbench-evidence-report.json routeAcceptance.routeName" $failures
        Assert-NonEmptyString $route.routeKey "accountant-workbench-evidence-report.json routeAcceptance.routeKey" $failures
        Assert-NonEmptyString $route.expectedText "accountant-workbench-evidence-report.json routeAcceptance.expectedText" $failures
        Assert-Truthy $route.blocksRelease "accountant-workbench-evidence-report.json routeAcceptance.blocksRelease" $failures
        if ([string]$route.signOffGate -ne "qualified-accountant-route-acceptance") {
            Add-Failure $failures "accountant-workbench-evidence-report.json routeAcceptance.signOffGate must be qualified-accountant-route-acceptance."
        }
        if (-not (@($route.requiredAcceptanceEvidence) | Where-Object { [string]$_ -like "*qualified-accountant-route-acceptance" })) {
            Add-Failure $failures "accountant-workbench-evidence-report.json routeAcceptance.requiredAcceptanceEvidence must include qualified-accountant-route-acceptance."
        }
    }
}

if (-not ($releaseEvidence.PSObject.Properties.Name -contains "__missing")) {
    if ([int]$releaseEvidence.failureCount -ne 0) {
        Add-Failure $failures "release-evidence-report.json failureCount must be zero."
    }
    if ((Get-JsonProperty $releaseEvidence @("releaseCandidate", "identityConsistent")) -ne $true) {
        Add-Failure $failures "release-evidence-report.json releaseCandidate.identityConsistent must be true."
    }
    if ([int](Get-JsonProperty $releaseEvidence @("releaseCandidate", "evidenceIdentityCount")) -ne 6) {
        Add-Failure $failures "release-evidence-report.json releaseCandidate.evidenceIdentityCount must be 6."
    }
    if ($releaseCommitSha.Length -gt 0) {
        $releaseEvidenceCommitSha = [string](Get-JsonProperty $releaseEvidence @("releaseCandidate", "commitSha"))
        if (-not [string]::Equals($releaseEvidenceCommitSha, $releaseCommitSha, [StringComparison]::OrdinalIgnoreCase)) {
            Add-Failure $failures "release-evidence-report.json releaseCandidate.commitSha must match CommitSha."
        }
    }
    if ($releaseRunUrl.Length -gt 0) {
        $releaseEvidenceRunUrl = [string](Get-JsonProperty $releaseEvidence @("releaseCandidate", "githubActionsRunUrl"))
        if (-not [string]::Equals($releaseEvidenceRunUrl, $releaseRunUrl, [StringComparison]::OrdinalIgnoreCase)) {
            Add-Failure $failures "release-evidence-report.json releaseCandidate.githubActionsRunUrl must match GitHubActionsRunUrl."
        }
    }
    foreach ($coverageProperty in @("sourceLawSourceIds", "goldenCorpusScenarioCodes", "externalRosIxbrlScenarioCodes", "routeCodes", "manualHandoffScenarioCodes", "manualHandoffPathCodes", "releaseArtifactNames")) {
        if ($null -eq $releaseEvidence.requiredCoverage.$coverageProperty -or @($releaseEvidence.requiredCoverage.$coverageProperty).Count -eq 0) {
            Add-Failure $failures "release-evidence-report.json requiredCoverage.$coverageProperty must be present."
        }
    }
    Assert-ReleaseEvidenceTemplateManifest $releaseEvidence $resolvedDirectory.Path $requiredReleaseEvidenceTemplates $failures
}

$evidenceFileManifest = @(
    foreach ($entry in $allEvidence.GetEnumerator()) {
        $evidence = $entry.Value
        if ($evidence.PSObject.Properties.Name -contains "__missing" -or
            $evidence.PSObject.Properties.Name -contains "__invalid") {
            continue
        }

        $filePath = [string]$evidence.__path
        $fileInfo = Get-Item -LiteralPath $filePath
        [ordered]@{
            fileName = $entry.Key
            path = $filePath
            byteSize = $fileInfo.Length
            sha256 = Get-FileSha256 $filePath
            checkedAtUtc = if ($evidence.PSObject.Properties.Name -contains "checkedAtUtc") { [string]$evidence.checkedAtUtc } else { "" }
            status = if ($evidence.PSObject.Properties.Name -contains "status") { [string]$evidence.status } else { "" }
        }
    }
)

$releaseEvidenceTemplateManifest = @(
    foreach ($template in $requiredReleaseEvidenceTemplates) {
        $templatePath = Join-Path $resolvedDirectory.Path $template.fileName
        if (-not (Test-Path -LiteralPath $templatePath -PathType Leaf)) {
            continue
        }

        $fileInfo = Get-Item -LiteralPath $templatePath
        [ordered]@{
            fileName = $template.fileName
            evidenceName = $template.evidenceName
            evidenceType = "release-evidence-template"
            path = $templatePath
            byteSize = $fileInfo.Length
            sha256 = Get-FileSha256 $templatePath
            checkedAtUtc = ""
            status = "retained"
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
    requiredFiles = @($allEvidence.Keys) + @($requiredReleaseEvidenceTemplates | ForEach-Object { $_.fileName })
    evidenceFiles = @($evidenceFileManifest) + @($releaseEvidenceTemplateManifest)
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
