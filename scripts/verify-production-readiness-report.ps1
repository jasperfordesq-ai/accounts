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

function Assert-StringArrayExactly {
    param(
        [object[]]$Actual,
        [string[]]$Expected,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    $actualValues = @($Actual | ForEach-Object { [string]$_ })
    if ($actualValues.Count -ne $Expected.Count -or
        [string]::Join("`n", $actualValues) -cne [string]::Join("`n", $Expected)) {
        Add-Failure $Failures "$Context must exactly match: $([string]::Join(', ', $Expected))."
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
$requiredCategoryTargetScores = @{
    "architecture-documentation" = 150
    "backend-statutory-accounting-engine" = 350
    "frontend-accountant-workbench" = 250
    "security-auth-tenant-platform-guardrails" = 250
}
$verifiedScorecardControlCodes = @()
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
$requiredVisualStateIds = @(
    "login", "password-change", "dashboard", "onboarding", "production-readiness", "company-detail",
    "period-workspace", "classification", "categorisation", "year-end", "adjustments", "notes", "charity",
    "financial-statements", "statement-source-trail", "statement-profit-and-loss", "statement-balance-sheet",
    "statement-tax-computation", "statement-cash-flow", "statement-equity-changes", "statement-directors-report",
    "filing-review", "workbench-preview", "state-loading", "state-empty", "state-maximum-data", "state-error",
    "state-partial-error", "state-permission-denied", "state-read-only", "state-stale", "state-conflict"
)
$requiredVisualMaterialRoutes = @(
    "login", "password-change", "onboarding", "classification", "categorisation", "year-end", "adjustments",
    "notes", "charity", "statement-trial-balance", "statement-source-trail", "statement-profit-and-loss",
    "statement-balance-sheet", "statement-tax-computation", "statement-cash-flow", "statement-equity-changes",
    "statement-directors-report", "filing"
)
$requiredVisualUiStates = @(
    "loading", "empty", "maximum-data", "error", "partial-error", "permission-denied", "read-only", "stale", "conflict"
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
    "postgres-transport-tls",
    "backup-restore-drill",
    "postgres-migration-upgrade-gate",
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
    "postgres-transport-tls",
    "backup-restore-drill",
    "postgres-migration-upgrade-gate",
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
$requiredHumanReleaseEvidenceCodes = @(
    "visualQa",
    "sourceLawReview",
    "externalRosIxbrlValidation",
    "qualifiedAccountantAcceptance",
    "manualHandoffAcceptance",
    "monitoringProviderConfirmation"
)
$requiredHumanReleaseEvidenceReviewerPickupFiles = @{
    visualQa = @("visual-qa-signoff-template.md", "visual-smoke-manifest.json", "visual-smoke-evidence-report.json", "accountant-workbench-evidence-report.json", "release-evidence-reviewer-blockers.md")
    sourceLawReview = @("source-law-review-template.md", "production-readiness-report.json", "production-readiness-verification-report.json", "release-evidence-reviewer-blockers.md")
    externalRosIxbrlValidation = @("external-ros-ixbrl-validation-template.md", "production-readiness-report.json", "release-evidence-reviewer-blockers.md")
    qualifiedAccountantAcceptance = @("qualified-accountant-acceptance-template.md", "production-readiness-report.json", "accountant-workbench-evidence-report.json", "release-evidence-reviewer-blockers.md")
    manualHandoffAcceptance = @("manual-handoff-acceptance-template.md", "production-readiness-report.json", "release-evidence-reviewer-blockers.md")
    monitoringProviderConfirmation = @("monitoring-provider-confirmation-template.md", "monitoring-error-routing-report.json", "structured-log-report.json", "release-evidence-reviewer-blockers.md")
}
$requiredHumanReleaseEvidenceCloseoutSteps = @(
    [pscustomobject]@{
        code = "pick-up-reviewer-workspace"
        sequence = 1
        artifact = "release-evidence-reviewer-workspace"
        detailTerms = @("release-evidence-reviewer-index.md", "pending human blocker inventory")
    },
    [pscustomobject]@{
        code = "complete-human-evidence-templates"
        sequence = 2
        artifact = "Docs/release-evidence/*.md"
        detailTerms = @("retained Markdown templates", "named reviewers")
    },
    [pscustomobject]@{
        code = "run-release-evidence-verifier"
        sequence = 3
        artifact = "scripts/verify-release-evidence.ps1"
        detailTerms = @("release-evidence-report.json", "exact candidate")
    },
    [pscustomobject]@{
        code = "confirm-human-evidence-completion"
        sequence = 4
        artifact = "release-evidence-report.json"
        detailTerms = @("humanEvidenceCompletion", "zero blocking failures")
    },
    [pscustomobject]@{
        code = "verify-release-artifact-pack"
        sequence = 5
        artifact = "scripts/verify-release-artifact-pack.ps1"
        detailTerms = @("same commit SHA", "GitHub Actions run URL")
    }
)
$requiredAssuranceEvidence = @(
    "production-scorecard",
    "production-readiness-report",
    "production-readiness-verification-report",
    "postgres-migration-upgrade-gate",
    "release-verification-manifest",
    "human-release-evidence",
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
        if ($targetScore -ne 1000) {
            Add-Failure $failures "productionScorecard.targetScore must be 1000."
        }
        if ([string](Get-JsonProperty $scorecard "status") -ne "remediation-required") {
            Add-Failure $failures "productionScorecard.status must be remediation-required."
        }
        if ([string](Get-JsonProperty $scorecard "scoreBasis") -ne "independent-audit-control-ledger-v1") {
            Add-Failure $failures "productionScorecard.scoreBasis must be independent-audit-control-ledger-v1."
        }
        if ([string](Get-JsonProperty $scorecard "auditBaselineDate") -ne "2026-07-10") {
            Add-Failure $failures "productionScorecard.auditBaselineDate must be 2026-07-10."
        }
        if ([string](Get-JsonProperty $scorecard "auditedCommit") -ne "7ea54cc6d1769ced568ac1568d190cc2bb4b16d1") {
            Add-Failure $failures "productionScorecard.auditedCommit must identify the exact independently audited baseline commit."
        }
        $scorecardEvidencePolicy = [string](Get-JsonProperty $scorecard "evidencePolicy")
        foreach ($requiredPolicyTerm in @("exact live candidate", "artifact hashes", "human/external")) {
            if ($scorecardEvidencePolicy -notlike "*$requiredPolicyTerm*") {
                Add-Failure $failures "productionScorecard.evidencePolicy must mention $requiredPolicyTerm."
            }
        }
        foreach ($categoryCode in $requiredCategoryCodes) {
            Assert-ObjectArrayHasCode $categories $categoryCode "productionScorecard.categories" $failures
        }

        $categoryCurrentTotal = 0
        $categoryTargetTotal = 0
        foreach ($category in $categories) {
            $categoryCode = [string](Get-JsonProperty $category "code")
            $categoryCurrentScore = [int](Get-JsonProperty $category "currentScore")
            $categoryTargetScore = [int](Get-JsonProperty $category "targetScore")
            $controls = @((Get-JsonProperty $category "controls"))
            $controlCurrentScore = 0
            $controlTargetScore = 0

            $categoryCurrentTotal += $categoryCurrentScore
            $categoryTargetTotal += $categoryTargetScore

            if ($requiredCategoryTargetScores.ContainsKey($categoryCode) -and $categoryTargetScore -ne [int]$requiredCategoryTargetScores[$categoryCode]) {
                Add-Failure $failures "productionScorecard category '$categoryCode' targetScore must be $($requiredCategoryTargetScores[$categoryCode])."
            }
            if ($controls.Count -eq 0) {
                Add-Failure $failures "productionScorecard category '$categoryCode' must include weighted controls."
            }

            foreach ($control in $controls) {
                $controlCode = [string](Get-JsonProperty $control "code")
                $weight = [int](Get-JsonProperty $control "weight")
                $passed = (Get-JsonProperty $control "passed") -eq $true
                $assuranceClass = [string](Get-JsonProperty $control "assuranceClass")
                $status = [string](Get-JsonProperty $control "status")
                $controlEvidence = @((Get-JsonProperty $control "evidence"))
                $blockingAuditIds = @((Get-JsonProperty $control "blockingAuditItemIds"))

                $verifiedScorecardControlCodes += "$categoryCode`:$controlCode"
                if ($weight -le 0) {
                    Add-Failure $failures "productionScorecard control '$categoryCode/$controlCode' must have a positive weight."
                }
                if (-not (@("code", "machine", "human-external") -contains $assuranceClass)) {
                    Add-Failure $failures "productionScorecard control '$categoryCode/$controlCode' assuranceClass must be code, machine, or human-external."
                }
                if (($passed -and $status -ne "passed") -or (-not $passed -and $status -ne "open")) {
                    Add-Failure $failures "productionScorecard control '$categoryCode/$controlCode' status must agree with passed."
                }
                if ($controlEvidence.Count -eq 0) {
                    Add-Failure $failures "productionScorecard control '$categoryCode/$controlCode' must retain objective evidence."
                }
                if (-not $passed -and $blockingAuditIds.Count -eq 0) {
                    Add-Failure $failures "Open productionScorecard control '$categoryCode/$controlCode' must identify blocking audit item IDs."
                }

                $controlTargetScore += $weight
                if ($passed) {
                    $controlCurrentScore += $weight
                }
            }

            if ($controlCurrentScore -ne $categoryCurrentScore) {
                Add-Failure $failures "productionScorecard category '$categoryCode' currentScore must equal passed control weights."
            }
            if ($controlTargetScore -ne $categoryTargetScore) {
                Add-Failure $failures "productionScorecard category '$categoryCode' targetScore must equal all control weights."
            }
            if (($categoryCode -eq "backend-statutory-accounting-engine" -or $categoryCode -eq "security-auth-tenant-platform-guardrails") -and
                @($controls | Where-Object { (Get-JsonProperty $_ "passed") -ne $true }).Count -gt 0 -and
                $categoryCurrentScore -eq $categoryTargetScore) {
                Add-Failure $failures "productionScorecard category '$categoryCode' cannot report full marks while controls remain open."
            }
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
        $coverageStatus = [string](Get-JsonProperty $scenario "coverageStatus")
        if ($coverageStatus -notin @("covered", "machine-covered-review-pending")) {
            Add-Failure $failures "goldenFilingCorpus scenario '$scenarioCode' must be covered or explicitly machine-covered-review-pending."
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
    $migrationUpgradeGate = @($manifest | Where-Object { [string](Get-JsonProperty $_ "code") -eq "postgres-migration-upgrade-gate" } | Select-Object -First 1)
    if ($migrationUpgradeGate.Count -gt 0) {
        $migrationCommand = [string](Get-JsonProperty $migrationUpgradeGate[0] "command")
        if ($migrationCommand -notlike "*has-pending-model-changes*" -or
            $migrationCommand -notlike "*MigrationUpgradePostgresTests*" -or
            $migrationCommand -notlike "*verify-migration-upgrade-evidence.ps1*") {
            Add-Failure $failures "releaseVerificationManifest 'postgres-migration-upgrade-gate' must include drift, PostgreSQL upgrade and evidence verification commands."
        }
        if ([string](Get-JsonProperty $migrationUpgradeGate[0] "evidenceArtifact") -ne "postgres-migration-upgrade-gate") {
            Add-Failure $failures "releaseVerificationManifest 'postgres-migration-upgrade-gate' evidenceArtifact must be postgres-migration-upgrade-gate."
        }
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

    $humanReleaseEvidence = @((Get-JsonProperty $report "humanReleaseEvidence"))
    foreach ($humanEvidenceCode in $requiredHumanReleaseEvidenceCodes) {
        Assert-ObjectArrayHasCode $humanReleaseEvidence $humanEvidenceCode "humanReleaseEvidence" $failures
    }
    foreach ($humanEvidence in $humanReleaseEvidence) {
        $humanEvidenceCode = [string](Get-JsonProperty $humanEvidence "code")
        $templateFile = [string](Get-JsonProperty $humanEvidence "templateFile")
        Assert-NonEmptyString $templateFile "humanReleaseEvidence '$humanEvidenceCode' templateFile" $failures
        Assert-NonEmptyString (Get-JsonProperty $humanEvidence "requiredReviewerRole") "humanReleaseEvidence '$humanEvidenceCode' requiredReviewerRole" $failures
        Assert-NonEmptyString (Get-JsonProperty $humanEvidence "signOffGate") "humanReleaseEvidence '$humanEvidenceCode' signOffGate" $failures
        Assert-NonEmptyString (Get-JsonProperty $humanEvidence "releaseChecklistCode") "humanReleaseEvidence '$humanEvidenceCode' releaseChecklistCode" $failures
        Assert-NonEmptyString (Get-JsonProperty $humanEvidence "releaseManifestCode") "humanReleaseEvidence '$humanEvidenceCode' releaseManifestCode" $failures
        Assert-NonEmptyString (Get-JsonProperty $humanEvidence "evidenceArtifact") "humanReleaseEvidence '$humanEvidenceCode' evidenceArtifact" $failures
        if ([string](Get-JsonProperty $humanEvidence "status") -ne "pending-human-evidence") {
            Add-Failure $failures "humanReleaseEvidence '$humanEvidenceCode' status must be pending-human-evidence before named sign-off."
        }
        if ((Get-JsonProperty $humanEvidence "blocksRelease") -ne $true) {
            Add-Failure $failures "humanReleaseEvidence '$humanEvidenceCode' must block release before named sign-off."
        }
        if (@((Get-JsonProperty $humanEvidence "requiredEvidence")).Count -lt 2) {
            Add-Failure $failures "humanReleaseEvidence '$humanEvidenceCode' must include retained requiredEvidence references."
        }
        $reviewerPickupFiles = @((Get-JsonProperty $humanEvidence "reviewerPickupFiles") | ForEach-Object { [string]$_ })
        $expectedReviewerPickupFiles = @($requiredHumanReleaseEvidenceReviewerPickupFiles[$humanEvidenceCode])
        foreach ($expectedReviewerPickupFile in $expectedReviewerPickupFiles) {
            if (-not ($reviewerPickupFiles -contains $expectedReviewerPickupFile)) {
                Add-Failure $failures "humanReleaseEvidence '$humanEvidenceCode' reviewerPickupFiles must include expected pickup file '$expectedReviewerPickupFile'."
            }
        }
    }

    $humanReleaseEvidenceCloseout = @((Get-JsonProperty $report "humanReleaseEvidenceCloseout"))
    if ($humanReleaseEvidenceCloseout.Count -ne $requiredHumanReleaseEvidenceCloseoutSteps.Count) {
        Add-Failure $failures "humanReleaseEvidenceCloseout must include exactly $($requiredHumanReleaseEvidenceCloseoutSteps.Count) release-operator steps."
    }
    for ($index = 0; $index -lt $requiredHumanReleaseEvidenceCloseoutSteps.Count; $index++) {
        $expectedStep = $requiredHumanReleaseEvidenceCloseoutSteps[$index]
        if ($index -ge $humanReleaseEvidenceCloseout.Count -or $null -eq $humanReleaseEvidenceCloseout[$index]) {
            Add-Failure $failures "humanReleaseEvidenceCloseout must include step '$($expectedStep.code)' at sequence $($expectedStep.sequence)."
            continue
        }

        $actualStep = $humanReleaseEvidenceCloseout[$index]
        $actualCode = [string](Get-JsonProperty $actualStep "code")
        if ($actualCode -ne $expectedStep.code) {
            Add-Failure $failures "humanReleaseEvidenceCloseout.$index.code must be $($expectedStep.code)."
        }
        if ([int](Get-JsonProperty $actualStep "sequence") -ne [int]$expectedStep.sequence) {
            Add-Failure $failures "humanReleaseEvidenceCloseout.$actualCode.sequence must be $($expectedStep.sequence)."
        }
        if ([string](Get-JsonProperty $actualStep "artifact") -ne $expectedStep.artifact) {
            Add-Failure $failures "humanReleaseEvidenceCloseout.$actualCode.artifact must be $($expectedStep.artifact)."
        }
        if ((Get-JsonProperty $actualStep "blocksRelease") -ne $true) {
            Add-Failure $failures "humanReleaseEvidenceCloseout.$actualCode must block release before named sign-off."
        }

        $detail = [string](Get-JsonProperty $actualStep "detail")
        foreach ($detailTerm in @($expectedStep.detailTerms)) {
            if ($detail -notlike "*$detailTerm*") {
                Add-Failure $failures "humanReleaseEvidenceCloseout.$actualCode.detail must mention $detailTerm."
            }
        }
        if (($actualCode -eq "complete-human-evidence-templates" -or $actualCode -eq "confirm-human-evidence-completion") -and $detail -notlike "*$($requiredHumanReleaseEvidenceCodes.Count)*") {
            Add-Failure $failures "humanReleaseEvidenceCloseout.$actualCode.detail must mention the six human evidence templates."
        }
    }

    $visualQa = Get-JsonProperty $report "visualQaCoverage"
    if ($null -eq $visualQa) {
        Add-Failure $failures "visualQaCoverage must be present."
    } else {
        if ([string](Get-JsonProperty $visualQa "inventoryVersion") -ne "canonical-material-states-v1") {
            Add-Failure $failures "visualQaCoverage.inventoryVersion must be canonical-material-states-v1."
        }
        if ([int](Get-JsonProperty $visualQa "inventoryStateCount") -ne 32 -or
            [int](Get-JsonProperty $visualQa "routeCount") -ne 32) {
            Add-Failure $failures "visualQaCoverage inventoryStateCount and routeCount must both be 32."
        }
        if ([int](Get-JsonProperty $visualQa "accountantWorkbenchRouteCount") -ne 7) {
            Add-Failure $failures "visualQaCoverage.accountantWorkbenchRouteCount must be 7."
        }
        if ([int](Get-JsonProperty $visualQa "expectedScreenshotCount") -ne 192) {
            Add-Failure $failures "visualQaCoverage.expectedScreenshotCount must be 192."
        }
        if (@((Get-JsonProperty $visualQa "routes")).Count -ne 7) {
            Add-Failure $failures "visualQaCoverage.routes must include the 7 accountant workbench routes."
        }
        $stateInventory = @((Get-JsonProperty $visualQa "stateInventory"))
        if ($stateInventory.Count -ne 32) {
            Add-Failure $failures "visualQaCoverage.stateInventory must include 32 canonical state rows."
        }
        Assert-StringArrayExactly @($stateInventory | ForEach-Object { Get-JsonProperty $_ "stateId" }) $requiredVisualStateIds "visualQaCoverage.stateInventory.stateId" $failures
        Assert-StringArrayExactly @((Get-JsonProperty $visualQa "requiredMaterialRoutes")) $requiredVisualMaterialRoutes "visualQaCoverage.requiredMaterialRoutes" $failures
        Assert-StringArrayExactly @((Get-JsonProperty $visualQa "requiredUiStates")) $requiredVisualUiStates "visualQaCoverage.requiredUiStates" $failures
        Assert-StringArrayExactly @((Get-JsonProperty $visualQa "themes")) @("light", "dark") "visualQaCoverage.themes" $failures
        $viewports = @((Get-JsonProperty $visualQa "viewports"))
        foreach ($viewport in @(
            [pscustomobject]@{ name = "mobile"; width = 390; height = 844 },
            [pscustomobject]@{ name = "tablet"; width = 768; height = 1024 },
            [pscustomobject]@{ name = "desktop"; width = 1440; height = 1000 }
        )) {
            $actual = $viewports | Where-Object { [string](Get-JsonProperty $_ "name") -eq $viewport.name } | Select-Object -First 1
            if ($null -eq $actual -or
                [int](Get-JsonProperty $actual "width") -ne $viewport.width -or
                [int](Get-JsonProperty $actual "height") -ne $viewport.height) {
                Add-Failure $failures "visualQaCoverage.viewports must include $($viewport.name) at $($viewport.width)x$($viewport.height)."
            }
        }
        if ($viewports.Count -ne 3) {
            Add-Failure $failures "visualQaCoverage.viewports must include exactly mobile, tablet and desktop."
        }
        if ((Get-JsonProperty $visualQa "semanticDistinctnessRequired") -ne $true) {
            Add-Failure $failures "visualQaCoverage.semanticDistinctnessRequired must be true."
        }
        if (@((Get-JsonProperty $visualQa "artifacts")).Count -ne 192) {
            Add-Failure $failures "visualQaCoverage.artifacts must plan all 192 canonical captures."
        }
    }
}

$evidence = [ordered]@{
    status = if ($failures.Count -eq 0) { "passed" } else { "failed" }
    checkedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    reportPath = $resolvedReportPath
    requiredCoverage = [ordered]@{
        categoryCodes = $requiredCategoryCodes
        scoreBasis = "independent-audit-control-ledger-v1"
        auditBaselineDate = "2026-07-10"
        auditedCommit = "7ea54cc6d1769ced568ac1568d190cc2bb4b16d1"
        scorecardControlCodes = @($verifiedScorecardControlCodes | Sort-Object -Unique)
        goldenCorpusScenarioCodes = $requiredScenarioCodes
        sourceLawSourceIds = $requiredSourceIds
        releaseVerificationManifestCodes = $requiredManifestCodes
        humanReleaseEvidenceCodes = $requiredHumanReleaseEvidenceCodes
        humanReleaseEvidenceReviewerPickupFilePolicy = "Each humanReleaseEvidence row must include the full expected per-gate reviewerPickupFiles list."
        humanReleaseEvidenceReviewerPickupFiles = $requiredHumanReleaseEvidenceReviewerPickupFiles
        humanReleaseEvidenceCloseoutStepCodes = @($requiredHumanReleaseEvidenceCloseoutSteps | ForEach-Object { $_.code })
        assuranceEvidenceItems = $requiredAssuranceEvidence
        expectedVisualInventoryVersion = "canonical-material-states-v1"
        expectedVisualScreenshotCount = 192
        expectedVisualRouteCount = 32
        expectedAccountantWorkbenchRouteCount = 7
        requiredVisualStateIds = $requiredVisualStateIds
        requiredVisualMaterialRoutes = $requiredVisualMaterialRoutes
        requiredVisualUiStates = $requiredVisualUiStates
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
