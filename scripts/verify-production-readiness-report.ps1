param(
    [string]$ReportPath = "production-readiness-report.json",
    [string]$EvidencePath = ""
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
        [string]$Name
    )

    if ($null -eq $Object) {
        return $null
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
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

function Assert-ObjectArrayHasCode {
    param(
        [object[]]$Items,
        [string]$Code,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    if (-not (@($Items) | Where-Object { [string](Get-JsonProperty $_ "code") -eq $Code })) {
        Add-Failure $Failures "$Context must include code '$Code'."
    }
}

function Assert-StringArrayContains {
    param(
        [object[]]$Items,
        [string]$Value,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    if (-not (@($Items) -contains $Value)) {
        Add-Failure $Failures "$Context must include '$Value'."
    }
}

$failures = [System.Collections.Generic.List[string]]::new()

if (-not (Test-Path -LiteralPath $ReportPath -PathType Leaf)) {
    Add-Failure $failures "Missing production readiness report: $ReportPath"
    $report = $null
    $resolvedReportPath = $ReportPath
} else {
    $resolvedReportPath = (Resolve-Path -LiteralPath $ReportPath).Path
    try {
        $report = Get-Content -LiteralPath $resolvedReportPath -Raw | ConvertFrom-Json
    } catch {
        Add-Failure $failures "Production readiness report is not valid JSON: $ReportPath"
        $report = $null
    }
}

$requiredCategoryCodes = @(
    "architecture-documentation",
    "backend-statutory-accounting-engine",
    "frontend-accountant-workbench",
    "security-auth-tenant-platform-guardrails"
)
$requiredScenarioCodes = @("micro-ltd", "small-abridged-ltd", "dac-small", "clg-charity", "medium-audit-required")
$requiredSourceIds = @(
    "cro-financial-statements-requirements",
    "cro-medium-company",
    "cro-auditors-report",
    "revenue-accepted-taxonomies",
    "frc-frs-102",
    "frc-frs-105",
    "charities-regulator-annual-report"
)
$requiredManifestCodes = @(
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
$requiredDefaultCiManifestCodes = @(
    "backend-golden-corpus",
    "frontend-workbench-contract",
    "frontend-production-build",
    "visual-smoke-light-dark",
    "production-readiness-report-verification",
    "production-stack-smoke",
    "backup-restore-drill",
    "ci-machine-evidence-pack"
)
$requiredManualManifestCodes = @(
    "release-artifact-pack",
    "qualified-accountant-final-signoff",
    "source-law-change-review",
    "external-ros-validation-evidence",
    "no-direct-cro-ros-submission-control",
    "manual-accountant-acceptance"
)
$requiredAssuranceEvidence = @(
    "production-scorecard",
    "production-readiness-report",
    "production-readiness-verification-report",
    "release-verification-manifest",
    "release-blocker-register",
    "source-law-snapshot-fingerprint",
    "golden-filing-corpus",
    "visual-smoke-screenshots",
    "accountant-workbench-evidence-report"
)

if ($null -ne $report) {
    if ([string](Get-JsonProperty $report "overallStatus") -ne "review-required") {
        Add-Failure $failures "overallStatus must be review-required."
    }
    Assert-NonEmptyString (Get-JsonProperty $report "generatedAt") "generatedAt" $failures

    $scorecard = Get-JsonProperty $report "productionScorecard"
    if ($null -eq $scorecard) {
        Add-Failure $failures "productionScorecard must be present."
    } else {
        $currentScore = [int](Get-JsonProperty $scorecard "currentScore")
        $targetScore = [int](Get-JsonProperty $scorecard "targetScore")
        $categories = @((Get-JsonProperty $scorecard "categories"))
        if ($currentScore -le 0) {
            Add-Failure $failures "productionScorecard.currentScore must be greater than zero."
        }
        if ($targetScore -ne 700) {
            Add-Failure $failures "productionScorecard.targetScore must be 700."
        }
        if ([string](Get-JsonProperty $scorecard "status") -ne "review-required") {
            Add-Failure $failures "productionScorecard.status must be review-required."
        }
        foreach ($categoryCode in $requiredCategoryCodes) {
            Assert-ObjectArrayHasCode $categories $categoryCode "productionScorecard.categories" $failures
        }

        $categoryCurrentTotal = 0
        $categoryTargetTotal = 0
        foreach ($category in $categories) {
            $categoryCurrentTotal += [int](Get-JsonProperty $category "currentScore")
            $categoryTargetTotal += [int](Get-JsonProperty $category "targetScore")
        }
        if ($categoryCurrentTotal -ne $currentScore) {
            Add-Failure $failures "productionScorecard.currentScore must equal the sum of category current scores."
        }
        if ($categoryTargetTotal -ne $targetScore) {
            Add-Failure $failures "productionScorecard.targetScore must equal the sum of category target scores."
        }
    }

    $sourceLawSnapshot = Get-JsonProperty $report "sourceLawSnapshot"
    if ($null -eq $sourceLawSnapshot) {
        Add-Failure $failures "sourceLawSnapshot must be present."
    } else {
        $sources = @((Get-JsonProperty $sourceLawSnapshot "sources"))
        if ([int](Get-JsonProperty $sourceLawSnapshot "sourceCount") -ne $sources.Count) {
            Add-Failure $failures "sourceLawSnapshot.sourceCount must match sources length."
        }
        if ([string](Get-JsonProperty $sourceLawSnapshot "contentHash") -notmatch '^sha256:[0-9a-f]{64}$') {
            Add-Failure $failures "sourceLawSnapshot.contentHash must be a sha256 fingerprint."
        }
        foreach ($sourceId in $requiredSourceIds) {
            if (-not ($sources | Where-Object { [string](Get-JsonProperty $_ "sourceId") -eq $sourceId })) {
                Add-Failure $failures "sourceLawSnapshot.sources must include '$sourceId'."
            }
        }
    }

    $goldenCorpus = @((Get-JsonProperty $report "goldenFilingCorpus"))
    foreach ($scenarioCode in $requiredScenarioCodes) {
        Assert-ObjectArrayHasCode $goldenCorpus $scenarioCode "goldenFilingCorpus" $failures
    }
    foreach ($scenario in $goldenCorpus) {
        $scenarioCode = [string](Get-JsonProperty $scenario "code")
        if (@((Get-JsonProperty $scenario "evidenceVerifiers")).Count -eq 0) {
            Add-Failure $failures "goldenFilingCorpus scenario '$scenarioCode' must include evidenceVerifiers."
        }
        if ($null -eq (Get-JsonProperty $scenario "evidencePack")) {
            Add-Failure $failures "goldenFilingCorpus scenario '$scenarioCode' must include evidencePack."
        }
        if ([string](Get-JsonProperty $scenario "coverageStatus") -ne "covered") {
            Add-Failure $failures "goldenFilingCorpus scenario '$scenarioCode' must be covered."
        }
    }

    $assurancePacket = Get-JsonProperty $report "assurancePacket"
    if ($null -eq $assurancePacket) {
        Add-Failure $failures "assurancePacket must be present."
    } else {
        $evidenceItems = @((Get-JsonProperty $assurancePacket "evidenceItems"))
        foreach ($evidence in $requiredAssuranceEvidence) {
            Assert-StringArrayContains $evidenceItems $evidence "assurancePacket.evidenceItems" $failures
        }
    }

    $releaseBlockers = @((Get-JsonProperty $report "releaseBlockerRegister"))
    if ($releaseBlockers.Count -eq 0) {
        Add-Failure $failures "releaseBlockerRegister must include blocking entries."
    }
    foreach ($blocker in $releaseBlockers) {
        $blockerCode = [string](Get-JsonProperty $blocker "code")
        Assert-NonEmptyString $blockerCode "releaseBlockerRegister.code" $failures
        Assert-NonEmptyString (Get-JsonProperty $blocker "nextAction") "releaseBlockerRegister '$blockerCode' nextAction" $failures
        Assert-NonEmptyString (Get-JsonProperty $blocker "evidenceArtifact") "releaseBlockerRegister '$blockerCode' evidenceArtifact" $failures
        if ((Get-JsonProperty $blocker "blocksRelease") -ne $true) {
            Add-Failure $failures "releaseBlockerRegister '$blockerCode' must block release."
        }
    }

    $manifest = @((Get-JsonProperty $report "releaseVerificationManifest"))
    foreach ($manifestCode in $requiredManifestCodes) {
        Assert-ObjectArrayHasCode $manifest $manifestCode "releaseVerificationManifest" $failures
    }
    foreach ($manifestCode in $requiredDefaultCiManifestCodes) {
        $row = @($manifest | Where-Object { [string](Get-JsonProperty $_ "code") -eq $manifestCode } | Select-Object -First 1)
        if ($row.Count -gt 0) {
            if ([string](Get-JsonProperty $row[0] "ciScope") -ne "default-ci") {
                Add-Failure $failures "releaseVerificationManifest '$manifestCode' ciScope must be default-ci."
            }
            if ((Get-JsonProperty $row[0] "runsInDefaultCi") -ne $true) {
                Add-Failure $failures "releaseVerificationManifest '$manifestCode' runsInDefaultCi must be true."
            }
        }
    }
    foreach ($manifestCode in $requiredManualManifestCodes) {
        $row = @($manifest | Where-Object { [string](Get-JsonProperty $_ "code") -eq $manifestCode } | Select-Object -First 1)
        if ($row.Count -gt 0) {
            if ([string](Get-JsonProperty $row[0] "ciScope") -ne "manual-release") {
                Add-Failure $failures "releaseVerificationManifest '$manifestCode' ciScope must be manual-release."
            }
            if ((Get-JsonProperty $row[0] "runsInDefaultCi") -ne $false) {
                Add-Failure $failures "releaseVerificationManifest '$manifestCode' runsInDefaultCi must be false."
            }
        }
    }
    if (-not ($manifest | Where-Object { [string](Get-JsonProperty $_ "command") -like "*verify-production-readiness-report.ps1*" })) {
        Add-Failure $failures "releaseVerificationManifest must include verify-production-readiness-report.ps1."
    }
    if (-not ($manifest | Where-Object { [string](Get-JsonProperty $_ "command") -like "*verify-release-artifact-pack.ps1*" })) {
        Add-Failure $failures "releaseVerificationManifest must include verify-release-artifact-pack.ps1."
    }
    $ciMachineEvidencePack = @($manifest | Where-Object { [string](Get-JsonProperty $_ "code") -eq "ci-machine-evidence-pack" } | Select-Object -First 1)
    if ($ciMachineEvidencePack.Count -gt 0) {
        if ([string](Get-JsonProperty $ciMachineEvidencePack[0] "evidenceArtifact") -ne "ci-machine-evidence-pack") {
            Add-Failure $failures "releaseVerificationManifest 'ci-machine-evidence-pack' evidenceArtifact must be ci-machine-evidence-pack."
        }
        if ([string](Get-JsonProperty $ciMachineEvidencePack[0] "command") -notlike "*verify-ci-machine-evidence-pack.ps1*") {
            Add-Failure $failures "releaseVerificationManifest 'ci-machine-evidence-pack' must include verify-ci-machine-evidence-pack.ps1."
        }
    }

    $visualQa = Get-JsonProperty $report "visualQaCoverage"
    if ($null -eq $visualQa) {
        Add-Failure $failures "visualQaCoverage must be present."
    } else {
        if ([int](Get-JsonProperty $visualQa "expectedScreenshotCount") -ne 28) {
            Add-Failure $failures "visualQaCoverage.expectedScreenshotCount must be 28."
        }
        if (@((Get-JsonProperty $visualQa "routes")).Count -ne 7) {
            Add-Failure $failures "visualQaCoverage.routes must include 7 routes."
        }
    }
}

$evidence = [ordered]@{
    status = if ($failures.Count -eq 0) { "passed" } else { "failed" }
    checkedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    reportPath = $resolvedReportPath
    requiredCoverage = [ordered]@{
        categoryCodes = $requiredCategoryCodes
        goldenCorpusScenarioCodes = $requiredScenarioCodes
        sourceLawSourceIds = $requiredSourceIds
        releaseVerificationManifestCodes = $requiredManifestCodes
        assuranceEvidenceItems = $requiredAssuranceEvidence
        expectedVisualScreenshotCount = 28
        expectedVisualRouteCount = 7
    }
    failureCount = $failures.Count
    failures = $failures.ToArray()
}

if ($EvidencePath.Trim().Length -gt 0) {
    $evidenceDirectory = Split-Path -Parent $EvidencePath
    if ($evidenceDirectory -and -not (Test-Path -LiteralPath $evidenceDirectory)) {
        New-Item -ItemType Directory -Path $evidenceDirectory | Out-Null
    }

    $evidence | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $EvidencePath -Encoding UTF8
}

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) {
        Write-Error $failure -ErrorAction Continue
    }
    throw "Production readiness report verification failed with $($failures.Count) issue(s)."
}

Write-Host "Production readiness report verification passed for $resolvedReportPath."
