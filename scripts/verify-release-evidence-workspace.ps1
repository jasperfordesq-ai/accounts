param(
    [string]$WorkspaceDirectory = (Join-Path $PSScriptRoot "..\artifacts\release-evidence-workspace"),
    [string]$ReportPath = "",
    [string]$CommitSha = "",
    [string]$GitHubActionsRunUrl = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$requiredTemplates = @(
    "visual-qa-signoff-template.md",
    "source-law-review-template.md",
    "external-ros-ixbrl-validation-template.md",
    "qualified-accountant-acceptance-template.md",
    "manual-handoff-acceptance-template.md",
    "monitoring-provider-confirmation-template.md"
)

$requiredWorkspaceFiles = @(
    $requiredTemplates +
    @(
        "production-readiness-report.json",
        "production-readiness-verification-report.json",
        "visual-smoke-manifest.json",
        "visual-smoke-evidence-report.json",
        "accountant-workbench-evidence-report.json",
        "monitoring-error-routing-report.json",
        "structured-log-report.json",
        "release-evidence-workspace-manifest.json",
        "release-evidence-machine-summary.json",
        "release-evidence-reviewer-index.md",
        "release-evidence-reviewer-completion.json",
        "release-evidence-reviewer-assignments.json",
        "release-evidence-reviewer-blockers.md",
        "release-evidence-report.json",
        "release-evidence-verifier-output.txt"
    )
)

$requiredReviewerQueue = @(
    [pscustomobject]@{
        EvidenceName = "visualQa"
        EvidenceGate = "Visual QA sign-off"
        TemplateFile = "visual-qa-signoff-template.md"
        ReviewerRole = "Named visual QA reviewer"
        SignOffGate = "visual-qa-screenshot-review"
        RequiredPickupFiles = @("visual-qa-signoff-template.md", "visual-smoke-manifest.json", "visual-smoke-evidence-report.json", "accountant-workbench-evidence-report.json", "release-evidence-reviewer-blockers.md")
    },
    [pscustomobject]@{
        EvidenceName = "sourceLawReview"
        EvidenceGate = "Source-law review"
        TemplateFile = "source-law-review-template.md"
        ReviewerRole = "Named source-law reviewer plus qualified accountant"
        SignOffGate = "source-law-change-review"
        RequiredPickupFiles = @("source-law-review-template.md", "production-readiness-report.json", "production-readiness-verification-report.json", "release-evidence-reviewer-blockers.md")
    },
    [pscustomobject]@{
        EvidenceName = "externalRosIxbrlValidation"
        EvidenceGate = "External ROS/iXBRL validation"
        TemplateFile = "external-ros-ixbrl-validation-template.md"
        ReviewerRole = "External ROS/iXBRL validation reviewer"
        SignOffGate = "external-ros-validation-evidence"
        RequiredPickupFiles = @("external-ros-ixbrl-validation-template.md", "production-readiness-report.json", "release-evidence-reviewer-blockers.md")
    },
    [pscustomobject]@{
        EvidenceName = "qualifiedAccountantAcceptance"
        EvidenceGate = "Qualified-accountant acceptance"
        TemplateFile = "qualified-accountant-acceptance-template.md"
        ReviewerRole = "Named qualified accountant"
        SignOffGate = "qualified-accountant-final-signoff"
        RequiredPickupFiles = @("qualified-accountant-acceptance-template.md", "production-readiness-report.json", "accountant-workbench-evidence-report.json", "release-evidence-reviewer-blockers.md")
    },
    [pscustomobject]@{
        EvidenceName = "manualHandoffAcceptance"
        EvidenceGate = "Manual handoff acceptance"
        TemplateFile = "manual-handoff-acceptance-template.md"
        ReviewerRole = "Named manual handoff reviewer"
        SignOffGate = "manual-accountant-acceptance"
        RequiredPickupFiles = @("manual-handoff-acceptance-template.md", "production-readiness-report.json", "release-evidence-reviewer-blockers.md")
    },
    [pscustomobject]@{
        EvidenceName = "monitoringProviderConfirmation"
        EvidenceGate = "Monitoring provider confirmation"
        TemplateFile = "monitoring-provider-confirmation-template.md"
        ReviewerRole = "Named release operator"
        SignOffGate = "production-monitoring"
        RequiredPickupFiles = @("monitoring-provider-confirmation-template.md", "monitoring-error-routing-report.json", "structured-log-report.json", "release-evidence-reviewer-blockers.md")
    }
)

$requiredMachineEvidenceFiles = @(
    "production-readiness-report.json",
    "production-readiness-verification-report.json",
    "visual-smoke-manifest.json",
    "visual-smoke-evidence-report.json",
    "accountant-workbench-evidence-report.json",
    "monitoring-error-routing-report.json",
    "structured-log-report.json"
)

$requiredMachineEvidenceProvenance = @(
    [pscustomobject]@{ FileName = "production-readiness-report.json"; SourceArtifactName = "production-readiness-report"; SourceArtifactFile = "production-readiness-report.json" },
    [pscustomobject]@{ FileName = "production-readiness-verification-report.json"; SourceArtifactName = "production-readiness-report"; SourceArtifactFile = "production-readiness-verification-report.json" },
    [pscustomobject]@{ FileName = "visual-smoke-manifest.json"; SourceArtifactName = "visual-smoke-screenshots"; SourceArtifactFile = "visual-smoke-manifest.json" },
    [pscustomobject]@{ FileName = "visual-smoke-evidence-report.json"; SourceArtifactName = "visual-smoke-screenshots"; SourceArtifactFile = "visual-smoke-evidence-report.json" },
    [pscustomobject]@{ FileName = "accountant-workbench-evidence-report.json"; SourceArtifactName = "visual-smoke-screenshots"; SourceArtifactFile = "accountant-workbench-evidence-report.json" },
    [pscustomobject]@{ FileName = "monitoring-error-routing-report.json"; SourceArtifactName = "monitoring-error-routing-smoke"; SourceArtifactFile = "monitoring-error-routing-report.json" },
    [pscustomobject]@{ FileName = "structured-log-report.json"; SourceArtifactName = "structured-json-log-sample"; SourceArtifactFile = "structured-log-report.json" }
)

$requiredManualHandoffScenarioCodes = @(
    "medium-audit-required"
)

$requiredManualHandoffPathCodes = @(
    "plc-public-company",
    "unlimited-company",
    "excluded-regulated-entity",
    "group-consolidation",
    "audit-required-without-auditor-report",
    "complex-corporation-tax",
    "direct-cro-ros-submission"
)

function Add-Failure {
    param(
        [System.Collections.Generic.List[string]]$Failures,
        [string]$Message
    )

    $Failures.Add($Message) | Out-Null
}

function Get-JsonPropertyValue {
    param(
        $Object,
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

function Get-FirstJsonPropertyValue {
    param(
        $Object,
        [string[]]$Names
    )

    foreach ($name in $Names) {
        $value = Get-JsonPropertyValue $Object $name
        if ($null -ne $value -and -not [string]::IsNullOrWhiteSpace([string]$value)) {
            return $value
        }
    }

    return ""
}

function Convert-JsonValueToEvidenceString {
    param($Value)

    if ($null -eq $Value) {
        return ""
    }

    if ($Value -is [DateTime]) {
        return ([DateTimeOffset]$Value).ToUniversalTime().ToString("O")
    }

    if ($Value -is [DateTimeOffset]) {
        return $Value.ToUniversalTime().ToString("O")
    }

    return [string]$Value
}

function Get-MarkdownFieldValue {
    param(
        [string]$Content,
        [string]$FieldName
    )

    $escaped = [regex]::Escape($FieldName)
    $match = [regex]::Match($Content, "(?m)^[`t ]*-?[`t ]*$escaped[`t ]*:[`t ]*(.*)$")
    if (-not $match.Success) {
        return $null
    }

    return $match.Groups[1].Value.Trim()
}

function Assert-MarkdownFieldEquals {
    param(
        [string]$Content,
        [string]$FieldName,
        [string]$Expected,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    $actual = Get-MarkdownFieldValue $Content $FieldName
    if ($null -eq $actual) {
        Add-Failure $Failures "$Context must include field $FieldName."
        return
    }

    if ($actual -ne $Expected) {
        Add-Failure $Failures "$Context $FieldName field must be $Expected."
    }
}

function Assert-MarkdownFieldBlank {
    param(
        [string]$Content,
        [string]$FieldName,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    $actual = Get-MarkdownFieldValue $Content $FieldName
    if ($null -eq $actual) {
        Add-Failure $Failures "$Context must include field $FieldName."
        return
    }

    if (-not [string]::IsNullOrWhiteSpace($actual)) {
        Add-Failure $Failures "$Context $FieldName field must remain blank before named operator sign-off."
    }
}

function Assert-PreparedHumanFieldBlank {
    param(
        [string]$Content,
        [string]$FieldName,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    $actual = Get-MarkdownFieldValue $Content $FieldName
    if ($null -eq $actual) {
        Add-Failure $Failures "$Context must include field $FieldName."
        return
    }

    if (-not [string]::IsNullOrWhiteSpace($actual)) {
        Add-Failure $Failures "$Context $FieldName field must remain blank before named human sign-off."
    }
}

function Assert-PreparedTemplateHumanFieldsBlank {
    param(
        [string]$WorkspaceDirectory,
        [System.Collections.Generic.List[string]]$Failures
    )

    $verifiedTemplates = @()
    $templateHumanFields = @(
        [pscustomobject]@{
            FileName = "visual-qa-signoff-template.md"
            Context = "Prepared visual QA template"
            Fields = @("Reviewer name", "Reviewer role", "Review date/time UTC", "Reviewer signature")
        },
        [pscustomobject]@{
            FileName = "source-law-review-template.md"
            Context = "Prepared source-law template"
            Fields = @(
                "Reviewer name",
                "Reviewer role",
                "Review date/time UTC",
                "Qualified accountant name",
                "Qualification / professional body",
                "Reviewer signature",
                "Qualified accountant source-law sign-off"
            )
        },
        [pscustomobject]@{
            FileName = "external-ros-ixbrl-validation-template.md"
            Context = "Prepared external ROS/iXBRL template"
            Fields = @(
                "Reviewer name",
                "Reviewer role",
                "Review date/time UTC",
                "External validation provider",
                "Validation environment",
                "Validation run/reference id",
                "Validation report file or URL",
                "Generated iXBRL artifact name",
                "Generated iXBRL SHA-256",
                "Taxonomy package",
                "Company/period reference",
                "Reviewer signature"
            )
        },
        [pscustomobject]@{
            FileName = "qualified-accountant-acceptance-template.md"
            Context = "Prepared qualified-accountant template"
            Fields = @(
                "Accountant name",
                "Qualification / professional body",
                "Firm / reviewer capacity",
                "Review date/time UTC",
                "Qualified accountant signature"
            )
        },
        [pscustomobject]@{
            FileName = "manual-handoff-acceptance-template.md"
            Context = "Prepared manual handoff template"
            Fields = @(
                "Reviewer name",
                "Reviewer role",
                "Firm / reviewer capacity",
                "Review date/time UTC",
                "Reviewer signature"
            )
        }
    )

    foreach ($template in $templateHumanFields) {
        $path = Join-Path $WorkspaceDirectory $template.FileName
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            continue
        }

        $content = Get-Content -LiteralPath $path -Raw
        foreach ($field in $template.Fields) {
            Assert-PreparedHumanFieldBlank $content $field $template.Context $Failures
        }

        if ([regex]::IsMatch($content, "(?im)^[`t ]*-[`t ]*\[[xX]\]")) {
            Add-Failure $Failures "$($template.Context) must leave human acceptance and evidence checkboxes unchecked before named human sign-off."
        }

        $verifiedTemplates += [ordered]@{
            fileName = $template.FileName
            context = $template.Context
            blankFields = @($template.Fields)
            checkboxPolicy = "unchecked-before-named-human-signoff"
        }
    }

    return @($verifiedTemplates)
}

function Assert-MarkdownTimestampFieldEquals {
    param(
        [string]$Content,
        [string]$FieldName,
        [string]$Expected,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    $actual = Get-MarkdownFieldValue $Content $FieldName
    if ($null -eq $actual) {
        Add-Failure $Failures "$Context must include field $FieldName."
        return
    }

    try {
        $actualInstant = [DateTimeOffset]::Parse($actual, [Globalization.CultureInfo]::InvariantCulture).ToUniversalTime()
        $expectedInstant = [DateTimeOffset]::Parse($Expected, [Globalization.CultureInfo]::InvariantCulture).ToUniversalTime()
        if ($actualInstant.UtcTicks -ne $expectedInstant.UtcTicks) {
            Add-Failure $Failures "$Context $FieldName field must be $Expected."
        }
    } catch {
        if ($actual -ne $Expected) {
            Add-Failure $Failures "$Context $FieldName field must be $Expected."
        }
    }
}

function Assert-ArrayContains {
    param(
        [object[]]$Values,
        [string]$Expected,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    if (-not (@($Values) | Where-Object { [string]$_ -eq $Expected })) {
        Add-Failure $Failures "$Context must include $Expected."
    }
}

function Assert-TextContains {
    param(
        [string]$Content,
        [string]$Needle,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    if ($Content.IndexOf($Needle, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
        Add-Failure $Failures "$Context must include '$Needle'."
    }
}

function Assert-VisualQaPreparedRouteReferences {
    param(
        [string]$WorkspaceDirectory,
        [System.Collections.Generic.List[string]]$Failures
    )

    $visualReportPath = Join-Path $WorkspaceDirectory "visual-smoke-evidence-report.json"
    $visualQaTemplatePath = Join-Path $WorkspaceDirectory "visual-qa-signoff-template.md"

    if (-not (Test-Path -LiteralPath $visualReportPath -PathType Leaf) -or -not (Test-Path -LiteralPath $visualQaTemplatePath -PathType Leaf)) {
        return
    }

    $visualReport = Get-Content -LiteralPath $visualReportPath -Raw | ConvertFrom-Json
    $routeNames = @((Get-JsonPropertyValue $visualReport "routeCoverage") | ForEach-Object {
        [string](Get-JsonPropertyValue $_ "routeName")
    } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

    if ($routeNames.Count -eq 0) {
        Add-Failure $Failures "visual-smoke-evidence-report.json routeCoverage must include visual QA route names."
        return
    }

    $content = Get-Content -LiteralPath $visualQaTemplatePath -Raw
    $lines = $content -split "\r?\n"
    foreach ($routeName in $routeNames) {
        $escaped = [regex]::Escape($routeName)
        $row = $lines | Where-Object { $_ -match "^\|\s*$escaped\s*\|" } | Select-Object -First 1
        if (-not $row) {
            Add-Failure $Failures "Prepared visual QA template must include route row $routeName."
            continue
        }

        $cells = @($row -split "\|")
        if ($cells.Count -lt 8) {
            Add-Failure $Failures "Prepared visual QA template route row $routeName must include all decision and notes cells."
            continue
        }

        foreach ($decisionIndex in 2..5) {
            if (-not [string]::IsNullOrWhiteSpace($cells[$decisionIndex])) {
                Add-Failure $Failures "Prepared visual QA template route row $routeName must leave route pass/fail decision cells blank before named human sign-off."
            }
        }

        $expectedReference = "visual-smoke-evidence-report.json#routeAcceptance.$routeName"
        if ($cells[6].Trim() -ne $expectedReference) {
            Add-Failure $Failures "Prepared visual QA template route row $routeName Notes cell must be $expectedReference."
        }
    }
}

function Assert-SourceLawPreparedEvidenceReferences {
    param(
        [string]$WorkspaceDirectory,
        [System.Collections.Generic.List[string]]$Failures
    )

    $productionReadinessPath = Join-Path $WorkspaceDirectory "production-readiness-report.json"
    $sourceLawTemplatePath = Join-Path $WorkspaceDirectory "source-law-review-template.md"

    if (-not (Test-Path -LiteralPath $productionReadinessPath -PathType Leaf) -or -not (Test-Path -LiteralPath $sourceLawTemplatePath -PathType Leaf)) {
        return
    }

    $productionReadiness = Get-Content -LiteralPath $productionReadinessPath -Raw | ConvertFrom-Json
    $sourceLawSnapshot = Get-JsonPropertyValue $productionReadiness "sourceLawSnapshot"
    $contentHash = [string](Get-JsonPropertyValue $sourceLawSnapshot "contentHash")
    if ($contentHash -notmatch "^sha256:([0-9a-f]{64})$") {
        Add-Failure $Failures "production-readiness-report.json sourceLawSnapshot.contentHash must be sha256:<64 lowercase hex>."
        return
    }

    $bareHash = $Matches[1]
    $sourceIds = @((Get-JsonPropertyValue $sourceLawSnapshot "sources") | ForEach-Object {
        [string](Get-JsonPropertyValue $_ "sourceId")
    } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

    if ($sourceIds.Count -eq 0) {
        Add-Failure $Failures "production-readiness-report.json sourceLawSnapshot.sources must include source IDs."
        return
    }

    $content = Get-Content -LiteralPath $sourceLawTemplatePath -Raw
    Assert-TextContains $content "Source-law snapshot fingerprint: source-law-snapshot-fingerprint#$bareHash" "Prepared source-law template" $Failures
    Assert-TextContains $content "Source-law snapshot content hash: $bareHash" "Prepared source-law template" $Failures

    $lines = $content -split "\r?\n"
    foreach ($sourceId in $sourceIds) {
        $escaped = [regex]::Escape($sourceId)
        $row = $lines | Where-Object { $_ -match "^\|\s*$escaped\s*\|" } | Select-Object -First 1
        if (-not $row) {
            Add-Failure $Failures "Prepared source-law template must include source row $sourceId."
            continue
        }

        $cells = @($row -split "\|")
        if ($cells.Count -lt 9) {
            Add-Failure $Failures "Prepared source-law template source row $sourceId must include all review and notes cells."
            continue
        }

        foreach ($decisionIndex in 2..6) {
            if (-not [string]::IsNullOrWhiteSpace($cells[$decisionIndex])) {
                Add-Failure $Failures "Prepared source-law template source row $sourceId must leave source review decision cells blank before named human sign-off."
            }
        }

        $expectedReference = "source-law-review-ledger#$sourceId"
        if ($cells[7].Trim() -ne $expectedReference) {
            Add-Failure $Failures "Prepared source-law template source row $sourceId Notes cell must be $expectedReference."
        }
    }
}

function Assert-ExternalRosIxbrlPreparedEvidenceReferences {
    param(
        [string]$WorkspaceDirectory,
        [System.Collections.Generic.List[string]]$Failures
    )

    $productionReadinessPath = Join-Path $WorkspaceDirectory "production-readiness-report.json"
    $externalRosTemplatePath = Join-Path $WorkspaceDirectory "external-ros-ixbrl-validation-template.md"

    if (-not (Test-Path -LiteralPath $productionReadinessPath -PathType Leaf) -or -not (Test-Path -LiteralPath $externalRosTemplatePath -PathType Leaf)) {
        return
    }

    $productionReadiness = Get-Content -LiteralPath $productionReadinessPath -Raw | ConvertFrom-Json
    $scenarioCodes = @((Get-JsonPropertyValue $productionReadiness "goldenFilingCorpus") | ForEach-Object {
        [string](Get-JsonPropertyValue $_ "code")
    } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

    if ($scenarioCodes.Count -eq 0) {
        Add-Failure $Failures "production-readiness-report.json goldenFilingCorpus must include scenario codes."
        return
    }

    $content = Get-Content -LiteralPath $externalRosTemplatePath -Raw
    $lines = $content -split "\r?\n"
    foreach ($scenarioCode in $scenarioCodes) {
        $escaped = [regex]::Escape($scenarioCode)
        $row = $lines | Where-Object { $_ -match "^\|\s*$escaped\s*\|" } | Select-Object -First 1
        if (-not $row) {
            Add-Failure $Failures "Prepared external ROS/iXBRL template must include scenario row $scenarioCode."
            continue
        }

        $cells = @($row -split "\|")
        if ($cells.Count -lt 8) {
            Add-Failure $Failures "Prepared external ROS/iXBRL template scenario row $scenarioCode must include all validation and decision cells."
            continue
        }

        $hasPrematureHumanEvidence = $false
        foreach ($decisionIndex in @(3, 5, 6)) {
            if (-not [string]::IsNullOrWhiteSpace($cells[$decisionIndex])) {
                $hasPrematureHumanEvidence = $true
            }
        }

        if ($hasPrematureHumanEvidence) {
            Add-Failure $Failures "Prepared external ROS/iXBRL template scenario row $scenarioCode must leave artifact hash, warnings/errors and decision cells blank before named external validation sign-off."
        }

        $expectedExternalReference = "external-ros-validation-ledger#$scenarioCode"
        if ($cells[2].Trim() -ne $expectedExternalReference) {
            Add-Failure $Failures "Prepared external ROS/iXBRL template scenario row $scenarioCode External reference cell must be $expectedExternalReference."
        }

        $expectedTaxonomyReference = "revenue-taxonomy-package-ledger#$scenarioCode"
        if ($cells[4].Trim() -ne $expectedTaxonomyReference) {
            Add-Failure $Failures "Prepared external ROS/iXBRL template scenario row $scenarioCode Taxonomy package cell must be $expectedTaxonomyReference."
        }
    }
}

function Assert-ManualHandoffPreparedEvidenceReferences {
    param(
        [string]$WorkspaceDirectory,
        [System.Collections.Generic.List[string]]$Failures
    )

    $manualHandoffTemplatePath = Join-Path $WorkspaceDirectory "manual-handoff-acceptance-template.md"

    if (-not (Test-Path -LiteralPath $manualHandoffTemplatePath -PathType Leaf)) {
        return
    }

    $content = Get-Content -LiteralPath $manualHandoffTemplatePath -Raw
    $lines = $content -split "\r?\n"

    foreach ($scenarioCode in $requiredManualHandoffScenarioCodes) {
        $escaped = [regex]::Escape($scenarioCode)
        $row = $lines | Where-Object { $_ -match "^\|\s*$escaped\s*\|" } | Select-Object -First 1
        if (-not $row) {
            Add-Failure $Failures "Prepared manual handoff template must include scenario row $scenarioCode."
            continue
        }

        $cells = @($row -split "\|")
        if ($cells.Count -lt 7) {
            Add-Failure $Failures "Prepared manual handoff template scenario row $scenarioCode must include all evidence and decision cells."
            continue
        }

        if (-not [string]::IsNullOrWhiteSpace($cells[5])) {
            Add-Failure $Failures "Prepared manual handoff template scenario row $scenarioCode must leave scenario decision cells blank before named manual handoff sign-off."
        }

        $expectedAuditorEvidence = "signed-auditor-report-evidence#$scenarioCode"
        if ($cells[2].Trim() -ne $expectedAuditorEvidence) {
            Add-Failure $Failures "Prepared manual handoff template scenario row $scenarioCode Auditor evidence cell must be $expectedAuditorEvidence."
        }

        $expectedHandoffNote = "manual-handoff-note#$scenarioCode"
        if ($cells[3].Trim() -ne $expectedHandoffNote) {
            Add-Failure $Failures "Prepared manual handoff template scenario row $scenarioCode Manual handoff note cell must be $expectedHandoffNote."
        }

        $expectedReadinessSnapshot = "filing-readiness-snapshot#$scenarioCode"
        if ($cells[4].Trim() -ne $expectedReadinessSnapshot) {
            Add-Failure $Failures "Prepared manual handoff template scenario row $scenarioCode Filing readiness snapshot cell must be $expectedReadinessSnapshot."
        }
    }

    foreach ($pathCode in $requiredManualHandoffPathCodes) {
        $escaped = [regex]::Escape($pathCode)
        $row = $lines | Where-Object { $_ -match "^\|\s*$escaped\s*\|" } | Select-Object -First 1
        if (-not $row) {
            Add-Failure $Failures "Prepared manual handoff template must include unsupported-path row $pathCode."
            continue
        }

        $cells = @($row -split "\|")
        if ($cells.Count -lt 5) {
            Add-Failure $Failures "Prepared manual handoff template unsupported-path row $pathCode must include evidence and decision cells."
            continue
        }

        if (-not [string]::IsNullOrWhiteSpace($cells[3])) {
            Add-Failure $Failures "Prepared manual handoff template unsupported-path row $pathCode must leave reviewer decision cells blank before named manual handoff sign-off."
        }

        $expectedReference = "unsupported-path-evidence#$pathCode"
        if ($cells[2].Trim() -ne $expectedReference) {
            Add-Failure $Failures "Prepared manual handoff template unsupported-path row $pathCode Release evidence reference cell must be $expectedReference."
        }
    }
}

function Assert-MonitoringProviderPreparedEvidenceReferences {
    param(
        [string]$WorkspaceDirectory,
        [System.Collections.Generic.List[string]]$Failures
    )

    $monitoringReportPath = Join-Path $WorkspaceDirectory "monitoring-error-routing-report.json"
    $structuredLogReportPath = Join-Path $WorkspaceDirectory "structured-log-report.json"
    $monitoringTemplatePath = Join-Path $WorkspaceDirectory "monitoring-provider-confirmation-template.md"

    if (-not (Test-Path -LiteralPath $monitoringReportPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $structuredLogReportPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $monitoringTemplatePath -PathType Leaf)) {
        return
    }

    $monitoringReport = Get-Content -LiteralPath $monitoringReportPath -Raw | ConvertFrom-Json
    $structuredLogReport = Get-Content -LiteralPath $structuredLogReportPath -Raw | ConvertFrom-Json
    $content = Get-Content -LiteralPath $monitoringTemplatePath -Raw
    $context = "Prepared monitoring-provider template"

    Assert-MarkdownFieldEquals $content "Provider" ([string](Get-JsonPropertyValue $monitoringReport "provider")) $context $Failures
    Assert-MarkdownFieldEquals $content "Event id" ([string](Get-JsonPropertyValue $monitoringReport "eventId")) $context $Failures
    Assert-MarkdownFieldEquals $content "Correlation id" ([string](Get-JsonPropertyValue $monitoringReport "correlationId")) $context $Failures
    Assert-MarkdownFieldEquals $content "Base URL" ([string](Get-JsonPropertyValue $monitoringReport "baseUrl")) $context $Failures
    Assert-MarkdownTimestampFieldEquals $content "Checked at UTC" (Convert-JsonValueToEvidenceString (Get-JsonPropertyValue $monitoringReport "checkedAtUtc")) $context $Failures
    Assert-MarkdownFieldEquals $content "Structured log file" ([string](Get-FirstJsonPropertyValue $structuredLogReport @("structuredLogFile", "logFileName"))) $context $Failures
    Assert-MarkdownFieldEquals $content "JSON log line count" ([string](Get-JsonPropertyValue $structuredLogReport "jsonLogLineCount")) $context $Failures
    $matchedMonitoringSmokeLine = if ([bool](Get-JsonPropertyValue $structuredLogReport "matchedMonitoringSmokeLine")) { "yes" } else { "" }
    Assert-MarkdownFieldEquals $content "Matched monitoring smoke line" $matchedMonitoringSmokeLine $context $Failures

    foreach ($humanOnlyField in @(
        "Operator name",
        "Operator role",
        "Confirmation date/time UTC",
        "Provider event URL or reference",
        "Operator notes",
        "Operator signature"
    )) {
        Assert-MarkdownFieldBlank $content $humanOnlyField $context $Failures
    }

    if ([regex]::IsMatch($content, "(?im)^[`t ]*-[`t ]*\[[xX]\]")) {
        Add-Failure $Failures "Prepared monitoring-provider template must leave provider confirmation and decision checkboxes unchecked before named operator sign-off."
    }
}

function Assert-QualifiedAccountantPreparedEvidenceReferences {
    param(
        [string]$WorkspaceDirectory,
        [System.Collections.Generic.List[string]]$Failures
    )

    $productionReadinessPath = Join-Path $WorkspaceDirectory "production-readiness-report.json"
    $accountantWorkbenchPath = Join-Path $WorkspaceDirectory "accountant-workbench-evidence-report.json"
    $qualifiedAccountantTemplatePath = Join-Path $WorkspaceDirectory "qualified-accountant-acceptance-template.md"

    if (-not (Test-Path -LiteralPath $productionReadinessPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $accountantWorkbenchPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $qualifiedAccountantTemplatePath -PathType Leaf)) {
        return
    }

    $productionReadiness = Get-Content -LiteralPath $productionReadinessPath -Raw | ConvertFrom-Json
    $scenarioCodes = @((Get-JsonPropertyValue $productionReadiness "goldenFilingCorpus") | ForEach-Object {
        [string](Get-JsonPropertyValue $_ "code")
    } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

    if ($scenarioCodes.Count -eq 0) {
        Add-Failure $Failures "production-readiness-report.json goldenFilingCorpus must include scenario codes."
        return
    }

    $accountantWorkbench = Get-Content -LiteralPath $accountantWorkbenchPath -Raw | ConvertFrom-Json
    $routeNames = @((Get-JsonPropertyValue $accountantWorkbench "routeAcceptance") | ForEach-Object {
        [string](Get-JsonPropertyValue $_ "routeName")
    } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

    if ($routeNames.Count -eq 0) {
        Add-Failure $Failures "accountant-workbench-evidence-report.json routeAcceptance must include route names."
        return
    }

    $content = Get-Content -LiteralPath $qualifiedAccountantTemplatePath -Raw
    $lines = $content -split "\r?\n"

    foreach ($scenarioCode in $scenarioCodes) {
        $escaped = [regex]::Escape($scenarioCode)
        $row = $lines | Where-Object { $_ -match "^\|\s*$escaped\s*\|" } | Select-Object -First 1
        if (-not $row) {
            Add-Failure $Failures "Prepared qualified-accountant template must include scenario row $scenarioCode."
            continue
        }

        $cells = @($row -split "\|")
        if ($cells.Count -lt 10) {
            Add-Failure $Failures "Prepared qualified-accountant template scenario row $scenarioCode must include all review and evidence cells."
            continue
        }

        foreach ($decisionIndex in 2..7) {
            if (-not [string]::IsNullOrWhiteSpace($cells[$decisionIndex])) {
                Add-Failure $Failures "Prepared qualified-accountant template scenario row $scenarioCode must leave scenario acceptance cells blank before named professional sign-off."
            }
        }

        $expectedReference = "qualified-accountant-walkthrough-ledger#$scenarioCode"
        if ($cells[8].Trim() -ne $expectedReference) {
            Add-Failure $Failures "Prepared qualified-accountant template scenario row $scenarioCode Scenario evidence reference cell must be $expectedReference."
        }
    }

    foreach ($routeName in $routeNames) {
        $escaped = [regex]::Escape($routeName)
        $row = $lines | Where-Object { $_ -match "^\|\s*$escaped\s*\|" } | Select-Object -First 1
        if (-not $row) {
            Add-Failure $Failures "Prepared qualified-accountant template must include route row $routeName."
            continue
        }

        $cells = @($row -split "\|")
        if ($cells.Count -lt 7) {
            Add-Failure $Failures "Prepared qualified-accountant template route row $routeName must include all decision and evidence cells."
            continue
        }

        foreach ($decisionIndex in 2..3) {
            if (-not [string]::IsNullOrWhiteSpace($cells[$decisionIndex])) {
                Add-Failure $Failures "Prepared qualified-accountant template route row $routeName must leave route acceptance cells blank before named professional sign-off."
            }
        }

        $expectedWorkbenchReference = "accountant-workbench-evidence-report.json#routeAcceptance.$routeName"
        if ($cells[4].Trim() -ne $expectedWorkbenchReference) {
            Add-Failure $Failures "Prepared qualified-accountant template route row $routeName Workbench evidence reference cell must be $expectedWorkbenchReference."
        }

        $expectedWalkthroughReference = "qualified-accountant-route-walkthrough#$routeName"
        if ($cells[5].Trim() -ne $expectedWalkthroughReference) {
            Add-Failure $Failures "Prepared qualified-accountant template route row $routeName Notes cell must be $expectedWalkthroughReference."
        }
    }
}

function Get-FileSha256 {
    param([string]$Path)

    $stream = [IO.File]::OpenRead($Path)
    try {
        $sha = [System.Security.Cryptography.SHA256]::Create()
        try {
            return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace("-", "").ToLowerInvariant()
        } finally {
            $sha.Dispose()
        }
    } finally {
        $stream.Dispose()
    }
}

function Format-MarkdownTableCell {
    param($Value)

    return ([string]$Value).Replace("`r", " ").Replace("`n", " ").Replace("|", "\|")
}

function Write-ReviewerBlockerSummary {
    param(
        $Report,
        [string]$Path
    )

    $completion = @((Get-JsonPropertyValue $Report "humanEvidenceCompletion"))
    $rows = @($completion | ForEach-Object {
        $blockingFailures = @((Get-JsonPropertyValue $_ "blockingFailures"))
        $firstBlockingFailure = if ($blockingFailures.Count -gt 0) { [string]$blockingFailures[0] } else { "" }
        "| $(Format-MarkdownTableCell (Get-JsonPropertyValue $_ "evidenceName")) | $(Format-MarkdownTableCell (Get-JsonPropertyValue $_ "templateFile")) | $(Format-MarkdownTableCell (Get-JsonPropertyValue $_ "requiredReviewerRole")) | $(Format-MarkdownTableCell (Get-JsonPropertyValue $_ "signOffGate")) | $(Format-MarkdownTableCell (Get-JsonPropertyValue $_ "status")) | $(Format-MarkdownTableCell (Get-JsonPropertyValue $_ "blockingFailureCount")) | $(Format-MarkdownTableCell $firstBlockingFailure) |"
    })

    $content = @"
# Release Evidence Reviewer Blockers

Status: reviewer-action-required

This summary is generated from ``release-evidence-report.json``. It is not release approval and it is not professional sign-off.

| Evidence name | Template file | Required reviewer | Sign-off gate | Status | Blocking failures | First blocker |
| --- | --- | --- | --- | --- | ---: | --- |
$($rows -join "`n")

Run ``scripts/verify-release-evidence.ps1`` again after named reviewers complete the Markdown templates. The release remains blocked until all six rows are accepted.
"@

    Set-Content -LiteralPath $Path -Value $content -NoNewline
}

function New-PendingHumanEvidenceBlockerSummary {
    param($Report)

    return @((Get-JsonPropertyValue $Report "humanEvidenceCompletion") | ForEach-Object {
        $blockingFailures = @((Get-JsonPropertyValue $_ "blockingFailures"))
        [ordered]@{
            evidenceName = [string](Get-JsonPropertyValue $_ "evidenceName")
            templateFile = [string](Get-JsonPropertyValue $_ "templateFile")
            requiredReviewerRole = [string](Get-JsonPropertyValue $_ "requiredReviewerRole")
            signOffGate = [string](Get-JsonPropertyValue $_ "signOffGate")
            status = [string](Get-JsonPropertyValue $_ "status")
            blockingFailureCount = [int](Get-JsonPropertyValue $_ "blockingFailureCount")
            firstBlockingFailure = if ($blockingFailures.Count -gt 0) { [string]$blockingFailures[0] } else { "" }
        }
    })
}

$resolvedWorkspace = Resolve-Path -LiteralPath $WorkspaceDirectory
if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path $resolvedWorkspace.Path "release-evidence-report.json"
}

$failures = New-Object System.Collections.Generic.List[string]

$manifestPath = Join-Path $resolvedWorkspace.Path "release-evidence-workspace-manifest.json"
$machineEvidenceSummaryPath = Join-Path $resolvedWorkspace.Path "release-evidence-machine-summary.json"
$reviewerIndexPath = Join-Path $resolvedWorkspace.Path "release-evidence-reviewer-index.md"
$reviewerCompletionPath = Join-Path $resolvedWorkspace.Path "release-evidence-reviewer-completion.json"
$reviewerAssignmentPath = Join-Path $resolvedWorkspace.Path "release-evidence-reviewer-assignments.json"
$reviewerBlockersPath = Join-Path $resolvedWorkspace.Path "release-evidence-reviewer-blockers.md"
$releaseEvidenceVerifierOutputPath = Join-Path $resolvedWorkspace.Path "release-evidence-verifier-output.txt"
$manifest = $null
$pendingHumanEvidenceBlockers = @()
$reviewerAssignmentInventory = @()

if (-not (Test-Path -LiteralPath $manifestPath)) {
    Add-Failure $failures "Workspace must include release-evidence-workspace-manifest.json."
} else {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json

    if ([string](Get-JsonPropertyValue $manifest "status") -ne "pending-human-evidence") {
        Add-Failure $failures "Workspace manifest status must be pending-human-evidence."
    }

    if ([string](Get-JsonPropertyValue $manifest "reviewerIndexFile") -ne "release-evidence-reviewer-index.md") {
        Add-Failure $failures "Workspace manifest reviewerIndexFile must be release-evidence-reviewer-index.md."
    }

    if ([string](Get-JsonPropertyValue $manifest "reviewerCompletionFile") -ne "release-evidence-reviewer-completion.json") {
        Add-Failure $failures "Workspace manifest reviewerCompletionFile must be release-evidence-reviewer-completion.json."
    }

    if ([string](Get-JsonPropertyValue $manifest "reviewerAssignmentFile") -ne "release-evidence-reviewer-assignments.json") {
        Add-Failure $failures "Workspace manifest reviewerAssignmentFile must be release-evidence-reviewer-assignments.json."
    }

    if ([string](Get-JsonPropertyValue $manifest "machineEvidenceSummaryFile") -ne "release-evidence-machine-summary.json") {
        Add-Failure $failures "Workspace manifest machineEvidenceSummaryFile must be release-evidence-machine-summary.json."
    }

    if (-not [string]::IsNullOrWhiteSpace($CommitSha) -and [string](Get-JsonPropertyValue $manifest "commitSha") -ne $CommitSha) {
        Add-Failure $failures "Workspace manifest commitSha must match CommitSha."
    }

    if (-not [string]::IsNullOrWhiteSpace($GitHubActionsRunUrl) -and [string](Get-JsonPropertyValue $manifest "githubActionsRunUrl") -ne $GitHubActionsRunUrl) {
        Add-Failure $failures "Workspace manifest githubActionsRunUrl must match GitHubActionsRunUrl."
    }

    foreach ($template in $requiredTemplates) {
        Assert-ArrayContains @((Get-JsonPropertyValue $manifest "preparedTemplates")) $template "Workspace manifest preparedTemplates" $failures
    }

    $retainedMachineEvidence = @((Get-JsonPropertyValue $manifest "retainedMachineEvidence"))
    if ($retainedMachineEvidence.Count -ne $requiredMachineEvidenceFiles.Count) {
        Add-Failure $failures "Workspace manifest retainedMachineEvidence must contain exactly $($requiredMachineEvidenceFiles.Count) entries."
    }

    foreach ($expectedMachineEvidence in $requiredMachineEvidenceProvenance) {
        $requiredMachineEvidenceFile = $expectedMachineEvidence.FileName
        $entry = $retainedMachineEvidence | Where-Object {
            [string](Get-JsonPropertyValue $_ "fileName") -eq $requiredMachineEvidenceFile
        } | Select-Object -First 1

        if ($null -eq $entry) {
            Add-Failure $failures "Workspace manifest retainedMachineEvidence must include $requiredMachineEvidenceFile."
            continue
        }

        $manifestByteSize = [string](Get-JsonPropertyValue $entry "byteSize")
        $manifestSha256 = [string](Get-JsonPropertyValue $entry "sha256")
        $retainedPath = Join-Path $resolvedWorkspace.Path $requiredMachineEvidenceFile

        if ([string](Get-JsonPropertyValue $entry "sourceArtifactName") -ne $expectedMachineEvidence.SourceArtifactName) {
            Add-Failure $failures "Workspace manifest retainedMachineEvidence.$requiredMachineEvidenceFile.sourceArtifactName must be $($expectedMachineEvidence.SourceArtifactName)."
        }

        if ([string](Get-JsonPropertyValue $entry "sourceArtifactFile") -ne $expectedMachineEvidence.SourceArtifactFile) {
            Add-Failure $failures "Workspace manifest retainedMachineEvidence.$requiredMachineEvidenceFile.sourceArtifactFile must be $($expectedMachineEvidence.SourceArtifactFile)."
        }

        if ($manifestByteSize -notmatch "^[1-9][0-9]*$") {
            Add-Failure $failures "Workspace manifest retainedMachineEvidence.$requiredMachineEvidenceFile.byteSize must be a positive integer."
        }

        if ($manifestSha256 -notmatch "^[0-9a-f]{64}$") {
            Add-Failure $failures "Workspace manifest retainedMachineEvidence.$requiredMachineEvidenceFile.sha256 must be a lowercase 64-character SHA-256 digest."
        }

        if (Test-Path -LiteralPath $retainedPath -PathType Leaf) {
            $retainedItem = Get-Item -LiteralPath $retainedPath
            if ($manifestByteSize -match "^[1-9][0-9]*$" -and [int64]$manifestByteSize -ne $retainedItem.Length) {
                Add-Failure $failures "Workspace manifest retainedMachineEvidence.$requiredMachineEvidenceFile.byteSize must match the retained file byte size."
            }

            if ($manifestSha256 -match "^[0-9a-f]{64}$" -and $manifestSha256 -ne (Get-FileSha256 $retainedPath)) {
                Add-Failure $failures "Workspace manifest retainedMachineEvidence.$requiredMachineEvidenceFile.sha256 must match the retained file SHA-256 digest."
            }
        }
    }

    $preparedTemplates = @((Get-JsonPropertyValue $manifest "preparedTemplates"))
    if ($preparedTemplates.Count -ne $requiredTemplates.Count) {
        Add-Failure $failures "Workspace manifest preparedTemplates must contain exactly $($requiredTemplates.Count) entries."
    }

    $reviewerQueue = @((Get-JsonPropertyValue $manifest "reviewerQueue"))
    if ($reviewerQueue.Count -ne $requiredReviewerQueue.Count) {
        Add-Failure $failures "Workspace manifest reviewerQueue must contain exactly $($requiredReviewerQueue.Count) entries."
    }

    foreach ($expected in $requiredReviewerQueue) {
        $entry = $reviewerQueue | Where-Object {
            [string](Get-JsonPropertyValue $_ "TemplateFile") -eq $expected.TemplateFile
        } | Select-Object -First 1

        if ($null -eq $entry) {
            Add-Failure $failures "Workspace manifest reviewerQueue must include $($expected.TemplateFile)."
            continue
        }

        foreach ($propertyName in @("EvidenceGate", "ReviewerRole", "SignOffGate")) {
            $actual = [string](Get-JsonPropertyValue $entry $propertyName)
            $expectedValue = [string](Get-JsonPropertyValue $expected $propertyName)
            if ($actual -ne $expectedValue) {
                Add-Failure $failures "Workspace manifest reviewerQueue.$($expected.TemplateFile).$propertyName must be '$expectedValue'."
            }
        }

        if ([string]::IsNullOrWhiteSpace([string](Get-JsonPropertyValue $entry "HumanAction"))) {
            Add-Failure $failures "Workspace manifest reviewerQueue.$($expected.TemplateFile).HumanAction must describe the remaining human-only action."
        }

        $requiredPickupFiles = @((Get-JsonPropertyValue $entry "RequiredPickupFiles") | ForEach-Object { [string]$_ })
        foreach ($requiredPickupFile in @($expected.RequiredPickupFiles)) {
            Assert-ArrayContains $requiredPickupFiles $requiredPickupFile "Workspace manifest reviewerQueue.$($expected.TemplateFile).RequiredPickupFiles" $failures
        }
    }

    foreach ($humanField in @(
        "reviewer/operator/accountant identity and role",
        "review dates and signatures",
        "external ROS/iXBRL provider/run evidence, artifact hashes, warnings/errors and decisions"
    )) {
        Assert-ArrayContains @((Get-JsonPropertyValue $manifest "humanFieldsLeftBlank")) $humanField "Workspace manifest humanFieldsLeftBlank" $failures
    }
}

if (-not (Test-Path -LiteralPath $machineEvidenceSummaryPath)) {
    Add-Failure $failures "Workspace must include release-evidence-machine-summary.json."
} else {
    $machineEvidenceSummary = Get-Content -LiteralPath $machineEvidenceSummaryPath -Raw | ConvertFrom-Json

    if ([string](Get-JsonPropertyValue $machineEvidenceSummary "status") -ne "pending-human-evidence") {
        Add-Failure $failures "Machine evidence summary status must be pending-human-evidence."
    }

    if (-not [string]::IsNullOrWhiteSpace($CommitSha) -and [string](Get-JsonPropertyValue (Get-JsonPropertyValue $machineEvidenceSummary "releaseCandidate") "commitSha") -ne $CommitSha) {
        Add-Failure $failures "Machine evidence summary releaseCandidate.commitSha must match CommitSha."
    }

    if (-not [string]::IsNullOrWhiteSpace($GitHubActionsRunUrl) -and [string](Get-JsonPropertyValue (Get-JsonPropertyValue $machineEvidenceSummary "releaseCandidate") "githubActionsRunUrl") -ne $GitHubActionsRunUrl) {
        Add-Failure $failures "Machine evidence summary releaseCandidate.githubActionsRunUrl must match GitHubActionsRunUrl."
    }

    Assert-TextContains ([string](Get-JsonPropertyValue $machineEvidenceSummary "completionPolicy")) "machine evidence only" "release-evidence-machine-summary.json completionPolicy" $failures
    Assert-TextContains ([string](Get-JsonPropertyValue $machineEvidenceSummary "completionPolicy")) "named reviewers" "release-evidence-machine-summary.json completionPolicy" $failures

    $summaryRetainedMachineEvidence = @((Get-JsonPropertyValue $machineEvidenceSummary "retainedMachineEvidence"))
    if ($summaryRetainedMachineEvidence.Count -ne $requiredMachineEvidenceFiles.Count) {
        Add-Failure $failures "Machine evidence summary retainedMachineEvidence must contain exactly $($requiredMachineEvidenceFiles.Count) entries."
    }

    foreach ($expectedMachineEvidence in $requiredMachineEvidenceProvenance) {
        $requiredMachineEvidenceFile = $expectedMachineEvidence.FileName
        $summaryEntry = $summaryRetainedMachineEvidence | Where-Object {
            [string](Get-JsonPropertyValue $_ "fileName") -eq $requiredMachineEvidenceFile
        } | Select-Object -First 1

        if ($null -eq $summaryEntry) {
            Add-Failure $failures "Machine evidence summary retainedMachineEvidence must include $requiredMachineEvidenceFile."
            continue
        }

        foreach ($propertyName in @("sourceArtifactName", "sourceArtifactFile", "byteSize", "sha256")) {
            $summaryValue = [string](Get-JsonPropertyValue $summaryEntry $propertyName)
            $manifestValue = ""
            if ($null -ne $manifest) {
                $manifestEntry = @((Get-JsonPropertyValue $manifest "retainedMachineEvidence")) | Where-Object {
                    [string](Get-JsonPropertyValue $_ "fileName") -eq $requiredMachineEvidenceFile
                } | Select-Object -First 1
                if ($null -ne $manifestEntry) {
                    $manifestValue = [string](Get-JsonPropertyValue $manifestEntry $propertyName)
                }
            }

            if ([string]::IsNullOrWhiteSpace($summaryValue)) {
                Add-Failure $failures "Machine evidence summary retainedMachineEvidence.$requiredMachineEvidenceFile.$propertyName must be present."
            } elseif ($null -ne $manifest -and $summaryValue -ne $manifestValue) {
                Add-Failure $failures "Machine evidence summary retainedMachineEvidence.$requiredMachineEvidenceFile.$propertyName must match the workspace manifest."
            }
        }
    }

    $summaryVisualEvidence = Get-JsonPropertyValue $machineEvidenceSummary "visualEvidence"
    foreach ($expectedVisualFile in @("visual-smoke-manifest.json", "visual-smoke-evidence-report.json", "accountant-workbench-evidence-report.json")) {
        $summaryVisualText = $summaryVisualEvidence | ConvertTo-Json -Depth 4
        Assert-TextContains $summaryVisualText $expectedVisualFile "release-evidence-machine-summary.json visualEvidence" $failures
    }

    $summaryProductionReadiness = Get-JsonPropertyValue $machineEvidenceSummary "productionReadiness"
    if ([string](Get-JsonPropertyValue $summaryProductionReadiness "verificationStatus") -ne "passed") {
        Add-Failure $failures "Machine evidence summary productionReadiness.verificationStatus must be passed."
    }
    if ([int](Get-JsonPropertyValue $summaryProductionReadiness "verificationFailureCount") -ne 0) {
        Add-Failure $failures "Machine evidence summary productionReadiness.verificationFailureCount must be zero."
    }
    foreach ($closeoutStepCode in @(
        "pick-up-reviewer-workspace",
        "complete-human-evidence-templates",
        "run-release-evidence-verifier",
        "confirm-human-evidence-completion",
        "verify-release-artifact-pack"
    )) {
        Assert-ArrayContains @((Get-JsonPropertyValue $summaryProductionReadiness "humanReleaseEvidenceCloseoutStepCodes")) $closeoutStepCode "Machine evidence summary productionReadiness.humanReleaseEvidenceCloseoutStepCodes" $failures
    }

    $summaryMonitoringEvidence = Get-JsonPropertyValue $machineEvidenceSummary "monitoringEvidence"
    foreach ($field in @("provider", "eventId", "correlationId", "baseUrl", "checkedAtUtc")) {
        if ([string]::IsNullOrWhiteSpace([string](Get-JsonPropertyValue $summaryMonitoringEvidence $field))) {
            Add-Failure $failures "Machine evidence summary monitoringEvidence.$field must be present."
        }
    }

    if ([int](Get-JsonPropertyValue $summaryMonitoringEvidence "jsonLogLineCount") -le 0) {
        Add-Failure $failures "Machine evidence summary monitoringEvidence.jsonLogLineCount must be greater than zero."
    }

    if ([bool](Get-JsonPropertyValue $summaryMonitoringEvidence "matchedMonitoringSmokeLine") -ne $true) {
        Add-Failure $failures "Machine evidence summary monitoringEvidence.matchedMonitoringSmokeLine must be true."
    }

    $summaryReviewerQueue = @((Get-JsonPropertyValue $machineEvidenceSummary "reviewerQueue"))
    if ($summaryReviewerQueue.Count -ne $requiredReviewerQueue.Count) {
        Add-Failure $failures "Machine evidence summary reviewerQueue must contain exactly $($requiredReviewerQueue.Count) entries."
    }
}

Assert-VisualQaPreparedRouteReferences $resolvedWorkspace.Path $failures
Assert-SourceLawPreparedEvidenceReferences $resolvedWorkspace.Path $failures
Assert-ExternalRosIxbrlPreparedEvidenceReferences $resolvedWorkspace.Path $failures
Assert-QualifiedAccountantPreparedEvidenceReferences $resolvedWorkspace.Path $failures
Assert-ManualHandoffPreparedEvidenceReferences $resolvedWorkspace.Path $failures
Assert-MonitoringProviderPreparedEvidenceReferences $resolvedWorkspace.Path $failures
$preparedHumanTemplateControls = @(Assert-PreparedTemplateHumanFieldsBlank $resolvedWorkspace.Path $failures)

if (-not (Test-Path -LiteralPath $reviewerIndexPath)) {
    Add-Failure $failures "Workspace must include release-evidence-reviewer-index.md."
} else {
    $reviewerIndex = Get-Content -LiteralPath $reviewerIndexPath -Raw
    foreach ($needle in @(
        "Release Evidence Reviewer Workspace",
        "Status: pending-human-evidence",
        "This workspace is reviewer preparation only.",
        "It is not release approval",
        "Reviewer Queue",
        "Reviewer pickup files",
        "Reviewer Completion Ledger",
        "Reviewer Assignment Ledger",
        "Reviewer Handoff Files",
        "release-evidence-reviewer-blockers.md",
        "release-evidence-verifier-output.txt",
        "release-evidence-workspace-verification-report.json",
        "Reviewer Closeout Sequence",
        "release-evidence-reviewer-workspace",
        "release-evidence-reviewer-assignments.json",
        "pending human blocker inventory",
        "six accepted ``humanEvidenceCompletion`` entries",
        "scripts/verify-release-artifact-pack.ps1",
        "release-evidence-reviewer-completion.json",
        "release-evidence-machine-summary.json",
        "Completion Gate",
        "scripts/verify-release-evidence.ps1"
    )) {
        Assert-TextContains $reviewerIndex $needle "release-evidence-reviewer-index.md" $failures
    }

    foreach ($expected in $requiredReviewerQueue) {
        Assert-TextContains $reviewerIndex $expected.EvidenceGate "release-evidence-reviewer-index.md" $failures
        Assert-TextContains $reviewerIndex $expected.TemplateFile "release-evidence-reviewer-index.md" $failures
        Assert-TextContains $reviewerIndex $expected.ReviewerRole "release-evidence-reviewer-index.md" $failures
        Assert-TextContains $reviewerIndex $expected.SignOffGate "release-evidence-reviewer-index.md" $failures
    }

    foreach ($requiredMachineEvidenceFile in $requiredMachineEvidenceFiles) {
        Assert-TextContains $reviewerIndex $requiredMachineEvidenceFile "release-evidence-reviewer-index.md" $failures
    }
}

if (-not (Test-Path -LiteralPath $reviewerCompletionPath)) {
    Add-Failure $failures "Workspace must include release-evidence-reviewer-completion.json."
} else {
    $completionLedger = Get-Content -LiteralPath $reviewerCompletionPath -Raw | ConvertFrom-Json

    if ([string](Get-JsonPropertyValue $completionLedger "status") -ne "pending-human-evidence") {
        Add-Failure $failures "Reviewer completion ledger status must be pending-human-evidence."
    }

    if (-not [string]::IsNullOrWhiteSpace($CommitSha) -and [string](Get-JsonPropertyValue (Get-JsonPropertyValue $completionLedger "releaseCandidate") "commitSha") -ne $CommitSha) {
        Add-Failure $failures "Reviewer completion ledger releaseCandidate.commitSha must match CommitSha."
    }

    if (-not [string]::IsNullOrWhiteSpace($GitHubActionsRunUrl) -and [string](Get-JsonPropertyValue (Get-JsonPropertyValue $completionLedger "releaseCandidate") "githubActionsRunUrl") -ne $GitHubActionsRunUrl) {
        Add-Failure $failures "Reviewer completion ledger releaseCandidate.githubActionsRunUrl must match GitHubActionsRunUrl."
    }

    $completionEntries = @((Get-JsonPropertyValue $completionLedger "entries"))
    if ($completionEntries.Count -ne $requiredReviewerQueue.Count) {
        Add-Failure $failures "Reviewer completion ledger entries must contain exactly $($requiredReviewerQueue.Count) entries."
    }

    foreach ($expected in $requiredReviewerQueue) {
        $entry = $completionEntries | Where-Object {
            [string](Get-JsonPropertyValue $_ "templateFile") -eq $expected.TemplateFile
        } | Select-Object -First 1

        if ($null -eq $entry) {
            Add-Failure $failures "Reviewer completion ledger entries must include $($expected.TemplateFile)."
            continue
        }

        $expectedFields = @{
            evidenceGate = $expected.EvidenceGate
            reviewerRole = $expected.ReviewerRole
            signOffGate = $expected.SignOffGate
            status = "pending-human-evidence"
            evidenceReportStatus = "incomplete-before-review"
        }

        foreach ($propertyName in $expectedFields.Keys) {
            $actual = [string](Get-JsonPropertyValue $entry $propertyName)
            $expectedValue = [string]$expectedFields[$propertyName]
            if ($actual -ne $expectedValue) {
                Add-Failure $failures "Reviewer completion ledger $($expected.TemplateFile).$propertyName must be '$expectedValue'."
            }
        }

        if ([bool](Get-JsonPropertyValue $entry "completed")) {
            Add-Failure $failures "Reviewer completion ledger $($expected.TemplateFile).completed must remain false before named human sign-off."
        }

        if (-not [string]::IsNullOrWhiteSpace([string](Get-JsonPropertyValue $entry "completedBy"))) {
            Add-Failure $failures "Reviewer completion ledger $($expected.TemplateFile).completedBy must be blank before named human sign-off."
        }

        if (-not [string]::IsNullOrWhiteSpace([string](Get-JsonPropertyValue $entry "completedAtUtc"))) {
            Add-Failure $failures "Reviewer completion ledger $($expected.TemplateFile).completedAtUtc must be blank before named human sign-off."
        }

        if ([string]::IsNullOrWhiteSpace([string](Get-JsonPropertyValue $entry "humanAction"))) {
            Add-Failure $failures "Reviewer completion ledger $($expected.TemplateFile).humanAction must describe the remaining human-only action."
        }
    }
}

if (-not (Test-Path -LiteralPath $reviewerAssignmentPath)) {
    Add-Failure $failures "Workspace must include release-evidence-reviewer-assignments.json."
} else {
    $assignmentLedger = Get-Content -LiteralPath $reviewerAssignmentPath -Raw | ConvertFrom-Json
    $assignmentInventory = @()

    if ([string](Get-JsonPropertyValue $assignmentLedger "status") -ne "pending-human-assignment") {
        Add-Failure $failures "Reviewer assignment ledger status must be pending-human-assignment."
    }

    if (-not [string]::IsNullOrWhiteSpace($CommitSha) -and [string](Get-JsonPropertyValue (Get-JsonPropertyValue $assignmentLedger "releaseCandidate") "commitSha") -ne $CommitSha) {
        Add-Failure $failures "Reviewer assignment ledger releaseCandidate.commitSha must match CommitSha."
    }

    if (-not [string]::IsNullOrWhiteSpace($GitHubActionsRunUrl) -and [string](Get-JsonPropertyValue (Get-JsonPropertyValue $assignmentLedger "releaseCandidate") "githubActionsRunUrl") -ne $GitHubActionsRunUrl) {
        Add-Failure $failures "Reviewer assignment ledger releaseCandidate.githubActionsRunUrl must match GitHubActionsRunUrl."
    }

    Assert-TextContains ([string](Get-JsonPropertyValue $assignmentLedger "assignmentPolicy")) "routing metadata only" "release-evidence-reviewer-assignments.json assignmentPolicy" $failures
    Assert-TextContains ([string](Get-JsonPropertyValue $assignmentLedger "assignmentPolicy")) "not evidence acceptance" "release-evidence-reviewer-assignments.json assignmentPolicy" $failures

    $assignmentEntries = @((Get-JsonPropertyValue $assignmentLedger "entries"))
    if ($assignmentEntries.Count -ne $requiredReviewerQueue.Count) {
        Add-Failure $failures "Reviewer assignment ledger entries must contain exactly $($requiredReviewerQueue.Count) entries."
    }

    foreach ($expected in $requiredReviewerQueue) {
        $entry = $assignmentEntries | Where-Object {
            [string](Get-JsonPropertyValue $_ "templateFile") -eq $expected.TemplateFile
        } | Select-Object -First 1

        if ($null -eq $entry) {
            Add-Failure $failures "Reviewer assignment ledger entries must include $($expected.TemplateFile)."
            continue
        }

        $expectedFields = @{
            evidenceGate = $expected.EvidenceGate
            requiredReviewerRole = $expected.ReviewerRole
            signOffGate = $expected.SignOffGate
            assignmentStatus = "unassigned"
            escalationOwnerRole = "Release operator"
        }

        foreach ($propertyName in $expectedFields.Keys) {
            $actual = [string](Get-JsonPropertyValue $entry $propertyName)
            $expectedValue = [string]$expectedFields[$propertyName]
            if ($actual -ne $expectedValue) {
                Add-Failure $failures "Reviewer assignment ledger $($expected.TemplateFile).$propertyName must be '$expectedValue'."
            }
        }

        foreach ($blankField in @("assignedReviewerName", "assignedReviewerEmail", "dueAtUtc")) {
            if (-not [string]::IsNullOrWhiteSpace([string](Get-JsonPropertyValue $entry $blankField))) {
                Add-Failure $failures "Reviewer assignment ledger $($expected.TemplateFile).$blankField must be blank before named reviewer routing."
            }
        }

        if ([string]::IsNullOrWhiteSpace([string](Get-JsonPropertyValue $entry "humanAction"))) {
            Add-Failure $failures "Reviewer assignment ledger $($expected.TemplateFile).humanAction must describe the remaining human-only action."
        }

        $reviewerPickupFiles = @((Get-JsonPropertyValue $entry "reviewerPickupFiles") | ForEach-Object { [string]$_ })
        foreach ($requiredPickupFile in @($expected.RequiredPickupFiles)) {
            Assert-ArrayContains $reviewerPickupFiles $requiredPickupFile "Reviewer assignment ledger $($expected.TemplateFile).reviewerPickupFiles" $failures
        }

        $assignmentInventory += [ordered]@{
            evidenceName = $expected.EvidenceName
            evidenceGate = $expected.EvidenceGate
            templateFile = $expected.TemplateFile
            requiredReviewerRole = $expected.ReviewerRole
            signOffGate = $expected.SignOffGate
            assignmentStatus = [string](Get-JsonPropertyValue $entry "assignmentStatus")
            assignedReviewerName = [string](Get-JsonPropertyValue $entry "assignedReviewerName")
            assignedReviewerEmail = [string](Get-JsonPropertyValue $entry "assignedReviewerEmail")
            dueAtUtc = [string](Get-JsonPropertyValue $entry "dueAtUtc")
            escalationOwnerRole = [string](Get-JsonPropertyValue $entry "escalationOwnerRole")
            humanAction = [string](Get-JsonPropertyValue $entry "humanAction")
            reviewerPickupFiles = @($reviewerPickupFiles)
        }
    }

    $reviewerAssignmentInventory = $assignmentInventory
}

foreach ($template in $requiredTemplates) {
    if (-not (Test-Path -LiteralPath (Join-Path $resolvedWorkspace.Path $template))) {
        Add-Failure $failures "Workspace must include $template."
    }
}

foreach ($requiredMachineEvidenceFile in $requiredMachineEvidenceFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $resolvedWorkspace.Path $requiredMachineEvidenceFile))) {
        Add-Failure $failures "Workspace must include retained machine evidence file $requiredMachineEvidenceFile."
    }
}

$releaseEvidenceStillBlocked = $false
try {
    & (Join-Path $PSScriptRoot "verify-release-evidence.ps1") `
        -EvidenceDirectory $resolvedWorkspace.Path `
        -ReportPath $ReportPath `
        *> $releaseEvidenceVerifierOutputPath
} catch {
    $releaseEvidenceStillBlocked = $true
}

if (-not $releaseEvidenceStillBlocked) {
    Add-Failure $failures "Prepared release evidence workspace unexpectedly passed before named human sign-off."
}

if (-not (Test-Path -LiteralPath $ReportPath)) {
    Add-Failure $failures "Workspace verification must write release-evidence-report.json."
} else {
    $report = Get-Content -LiteralPath $ReportPath -Raw | ConvertFrom-Json
    if ([string](Get-JsonPropertyValue $report "status") -ne "failed") {
        Add-Failure $failures "Prepared release evidence workspace report must remain failed before named human sign-off."
    }

    $completion = @((Get-JsonPropertyValue $report "humanEvidenceCompletion"))
    if ($completion.Count -ne $requiredTemplates.Count) {
        Add-Failure $failures "Prepared release evidence workspace report must include six humanEvidenceCompletion entries."
    }

    Write-ReviewerBlockerSummary $report $reviewerBlockersPath
    $pendingHumanEvidenceBlockers = @(New-PendingHumanEvidenceBlockerSummary $report)

    foreach ($entry in $completion) {
        if ([string](Get-JsonPropertyValue $entry "status") -ne "incomplete") {
            Add-Failure $failures "Prepared release evidence workspace must keep all human evidence entries incomplete."
        }

        if ([int](Get-JsonPropertyValue $entry "blockingFailureCount") -le 0) {
            Add-Failure $failures "Prepared release evidence workspace humanEvidenceCompletion entries must retain at least one blocker before named human sign-off."
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($CommitSha) -and [string](Get-JsonPropertyValue (Get-JsonPropertyValue $report "releaseCandidate") "commitSha") -ne $CommitSha) {
        Add-Failure $failures "Prepared release evidence report commitSha must match CommitSha."
    }

    if (-not [string]::IsNullOrWhiteSpace($GitHubActionsRunUrl) -and [string](Get-JsonPropertyValue (Get-JsonPropertyValue $report "releaseCandidate") "githubActionsRunUrl") -ne $GitHubActionsRunUrl) {
        Add-Failure $failures "Prepared release evidence report githubActionsRunUrl must match GitHubActionsRunUrl."
    }
}

if (-not (Test-Path -LiteralPath $reviewerBlockersPath)) {
    Add-Failure $failures "Workspace must include release-evidence-reviewer-blockers.md."
} else {
    $reviewerBlockers = Get-Content -LiteralPath $reviewerBlockersPath -Raw
    foreach ($needle in @(
        "Release Evidence Reviewer Blockers",
        "Status: reviewer-action-required",
        "generated from ``release-evidence-report.json``",
        "It is not release approval",
        "Run ``scripts/verify-release-evidence.ps1`` again"
    )) {
        Assert-TextContains $reviewerBlockers $needle "release-evidence-reviewer-blockers.md" $failures
    }

    foreach ($expected in $requiredReviewerQueue) {
        Assert-TextContains $reviewerBlockers $expected.TemplateFile "release-evidence-reviewer-blockers.md" $failures
        Assert-TextContains $reviewerBlockers $expected.ReviewerRole "release-evidence-reviewer-blockers.md" $failures
        Assert-TextContains $reviewerBlockers $expected.SignOffGate "release-evidence-reviewer-blockers.md" $failures
    }
}

$workspaceFiles = @(
    foreach ($file in Get-ChildItem -LiteralPath $resolvedWorkspace.Path -File | Sort-Object Name) {
        [ordered]@{
            fileName = $file.Name
            byteSize = $file.Length
            sha256 = Get-FileSha256 $file.FullName
        }
    }
)

$verificationCommitSha = $CommitSha
$verificationRunUrl = $GitHubActionsRunUrl
if ([string]::IsNullOrWhiteSpace($verificationCommitSha) -and $null -ne $manifest) {
    $verificationCommitSha = [string](Get-JsonPropertyValue $manifest "commitSha")
}
if ([string]::IsNullOrWhiteSpace($verificationRunUrl) -and $null -ne $manifest) {
    $verificationRunUrl = [string](Get-JsonPropertyValue $manifest "githubActionsRunUrl")
}

$inventoriedFileNames = @($workspaceFiles | ForEach-Object { [string]$_["fileName"] })
foreach ($requiredWorkspaceFile in $requiredWorkspaceFiles) {
    if (-not ($inventoriedFileNames | Where-Object { $_ -eq $requiredWorkspaceFile })) {
        Add-Failure $failures "Workspace file inventory must include $requiredWorkspaceFile."
    }
}

foreach ($inventoriedFileName in $inventoriedFileNames) {
    if ($inventoriedFileName -eq "release-evidence-workspace-verification-report.json") {
        continue
    }

    if (-not ($requiredWorkspaceFiles | Where-Object { $_ -eq $inventoriedFileName })) {
        Add-Failure $failures "Workspace file inventory must not include unexpected file $inventoriedFileName."
    }
}

$verificationReport = [ordered]@{
    status = if ($failures.Count -eq 0) { "passed" } else { "failed" }
    checkedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    workspaceDirectory = $resolvedWorkspace.Path
    releaseEvidenceReportPath = $ReportPath
    releaseEvidenceVerifierOutputPath = $releaseEvidenceVerifierOutputPath
    reviewerCompletionPath = $reviewerCompletionPath
    reviewerAssignmentPath = $reviewerAssignmentPath
    reviewerBlockersPath = $reviewerBlockersPath
    machineEvidenceSummaryPath = $machineEvidenceSummaryPath
    releaseCandidate = [ordered]@{
        commitSha = $verificationCommitSha
        githubActionsRunUrl = $verificationRunUrl
        identityProvided = (-not [string]::IsNullOrWhiteSpace($verificationCommitSha) -and -not [string]::IsNullOrWhiteSpace($verificationRunUrl))
    }
    workspaceFiles = $workspaceFiles
    requiredWorkspaceFiles = $requiredWorkspaceFiles
    preparedHumanTemplateControls = $preparedHumanTemplateControls
    pendingHumanEvidenceBlockers = $pendingHumanEvidenceBlockers
    reviewerAssignmentInventory = $reviewerAssignmentInventory
    requiredTemplateCount = $requiredTemplates.Count
    failureCount = $failures.Count
    failures = @($failures)
}

$workspaceReportPath = Join-Path $resolvedWorkspace.Path "release-evidence-workspace-verification-report.json"
$verificationReport | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $workspaceReportPath

if ($failures.Count -gt 0) {
    throw "Release evidence workspace verification failed with $($failures.Count) issue(s)."
}

Write-Host "Release evidence workspace verification passed for $($resolvedWorkspace.Path)."
