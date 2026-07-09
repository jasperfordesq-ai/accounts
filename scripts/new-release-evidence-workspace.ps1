param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\artifacts\release-evidence-workspace"),
    [string]$TemplateDirectory = (Join-Path $PSScriptRoot "..\Docs\release-evidence"),
    [string]$CommitSha = "",
    [string]$GitHubActionsRunUrl = "",
    [string]$ProductionReadinessReportPath = "",
    [string]$ProductionReadinessVerificationReportPath = "",
    [string]$VisualSmokeEvidenceReportPath = "",
    [string]$MonitoringErrorRoutingReportPath = "",
    [string]$StructuredLogReportPath = "",
    [switch]$Force
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Resolve-OptionalPath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return ""
    }

    return (Resolve-Path -LiteralPath $Path).Path
}

function Read-JsonFile {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $null
    }

    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
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

function Copy-MachineEvidenceInput {
    param(
        [string]$SourcePath,
        [string]$OutputDirectory,
        [string]$RequiredFileName,
        [string]$EvidenceName,
        [string]$SourceArtifactName,
        [string]$SourceArtifactFile
    )

    if ([string]::IsNullOrWhiteSpace($SourcePath) -or -not (Test-Path -LiteralPath $SourcePath -PathType Leaf)) {
        throw "Machine evidence input '$EvidenceName' is required for reviewer workspace generation."
    }

    $destinationPath = Join-Path $OutputDirectory $RequiredFileName
    Copy-Item -LiteralPath $SourcePath -Destination $destinationPath -Force
    $destination = Get-Item -LiteralPath $destinationPath

    [ordered]@{
        evidenceName = $EvidenceName
        fileName = $RequiredFileName
        sourceArtifactName = $SourceArtifactName
        sourceArtifactFile = $SourceArtifactFile
        sourcePath = $SourcePath
        byteSize = $destination.Length
        sha256 = Get-FileSha256 $destinationPath
    }
}

function Get-CurrentCommitSha {
    $sha = (& git -C (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to resolve current Git commit SHA."
    }

    return $sha
}

function Assert-CommitSha {
    param([string]$Value)

    if ($Value -notmatch "^[0-9a-fA-F]{40}$") {
        throw "CommitSha must be a 40-character hexadecimal Git commit SHA."
    }
}

function Assert-GitHubActionsRunUrl {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return
    }

    if ($Value -notmatch "^https://github\.com/[^/\s]+/[^/\s]+/actions/runs/[0-9]+(?:[/?#].*)?$") {
        throw "GitHubActionsRunUrl must be an exact GitHub Actions run URL."
    }
}

function Set-MarkdownField {
    param(
        [string]$Content,
        [string]$FieldName,
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $Content
    }

    $escaped = [regex]::Escape($FieldName)
    $pattern = "(?m)^([`t ]*-?[`t ]*$escaped[`t ]*:)[`t ]*.*$"
    if (-not [regex]::IsMatch($Content, $pattern)) {
        throw "Template is missing field '$FieldName'."
    }

    return [regex]::Replace($Content, $pattern, "`$1 $Value")
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

    if ($Value -is [DateTimeOffset]) {
        return $Value.ToUniversalTime().ToString("o")
    }

    if ($Value -is [DateTime]) {
        return $Value.ToUniversalTime().ToString("o")
    }

    return [string]$Value
}

function Get-JsonPathValue {
    param(
        $Object,
        [string[]]$Path
    )

    $current = $Object
    foreach ($segment in $Path) {
        $current = Get-JsonPropertyValue $current $segment
        if ($null -eq $current) {
            return $null
        }
    }

    return $current
}

function Get-MinimumVisualMetric {
    param(
        $VisualReport,
        [string[]]$Path
    )

    if ($null -eq $VisualReport) {
        return ""
    }

    $screenshots = @(Get-JsonPropertyValue $VisualReport "screenshots")
    $values = New-Object System.Collections.Generic.List[decimal]
    foreach ($screenshot in $screenshots) {
        $current = $screenshot
        foreach ($segment in $Path) {
            $current = Get-JsonPropertyValue $current $segment
            if ($null -eq $current) {
                break
            }
        }

        if ($null -ne $current) {
            $values.Add([decimal]$current) | Out-Null
        }
    }

    if ($values.Count -eq 0) {
        return ""
    }

    return ([Linq.Enumerable]::Min($values)).ToString([Globalization.CultureInfo]::InvariantCulture)
}

function Get-VisualRouteNames {
    param($VisualReport)

    $routes = @((Get-JsonPropertyValue $VisualReport "routeCoverage") | ForEach-Object {
        [string](Get-JsonPropertyValue $_ "routeName")
    } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

    if ($routes.Count -eq 0) {
        $routes = @((Get-JsonPropertyValue $VisualReport "screenshots") | ForEach-Object {
            [string](Get-JsonPropertyValue $_ "routeName")
        } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
    }

    if ($routes.Count -eq 0) {
        throw "Visual smoke evidence report must contain routeCoverage or screenshots routeName entries."
    }

    return @($routes)
}

function Get-SourceLawSnapshotContentHash {
    param($ProductionReadinessReport)

    $hash = [string](Get-JsonPathValue $ProductionReadinessReport @("sourceLawSnapshot", "contentHash"))
    if ([string]::IsNullOrWhiteSpace($hash)) {
        $hash = [string](Get-JsonPathValue $ProductionReadinessReport @("assurancePacket", "sourceLawSnapshotHash"))
    }

    if ($hash -notmatch "^sha256:([0-9a-f]{64})$") {
        throw "Production readiness report must include sourceLawSnapshot.contentHash as sha256:<64 lowercase hex>."
    }

    return $Matches[1]
}

function Get-SourceLawSourceIds {
    param($ProductionReadinessReport)

    $sourceIds = @((Get-JsonPathValue $ProductionReadinessReport @("sourceLawSnapshot", "sources")) | ForEach-Object {
        [string](Get-JsonPropertyValue $_ "sourceId")
    } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

    if ($sourceIds.Count -eq 0) {
        $sourceIds = @((Get-JsonPropertyValue $ProductionReadinessReport "sourceLawReviewLedger") | ForEach-Object {
            [string](Get-JsonPropertyValue $_ "sourceId")
        } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    }

    if ($sourceIds.Count -eq 0) {
        throw "Production readiness report must contain sourceLawSnapshot.sources or sourceLawReviewLedger sourceId entries."
    }

    return @($sourceIds)
}

function Get-GoldenCorpusScenarioCodes {
    param($ProductionReadinessReport)

    $scenarioCodes = @((Get-JsonPropertyValue $ProductionReadinessReport "goldenFilingCorpus") | ForEach-Object {
        [string](Get-JsonPropertyValue $_ "code")
    } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

    if ($scenarioCodes.Count -eq 0) {
        $scenarioCodes = @((Get-JsonPropertyValue $ProductionReadinessReport "goldenEvidenceLedger") | ForEach-Object {
            [string](Get-JsonPropertyValue $_ "scenarioCode")
        } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    }

    if ($scenarioCodes.Count -eq 0) {
        throw "Production readiness report must contain goldenFilingCorpus code or goldenEvidenceLedger scenarioCode entries."
    }

    return @($scenarioCodes)
}

function Get-AccountantWorkbenchRouteNames {
    param($AccountantWorkbenchReport)

    $routeNames = @((Get-JsonPropertyValue $AccountantWorkbenchReport "routeAcceptance") | ForEach-Object {
        [string](Get-JsonPropertyValue $_ "routeName")
    } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

    if ($routeNames.Count -eq 0) {
        $routeNames = @((Get-JsonPathValue $AccountantWorkbenchReport @("requiredCoverage", "routeCodes")) | ForEach-Object {
            [string]$_
        } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    }

    if ($routeNames.Count -eq 0) {
        throw "Accountant workbench evidence report must contain routeAcceptance routeName or requiredCoverage.routeCodes entries."
    }

    return @($routeNames)
}

function Set-VisualQaRouteReferenceNotes {
    param(
        [string]$Content,
        [string[]]$RouteNames
    )

    $updated = $Content
    foreach ($routeName in $RouteNames) {
        $escaped = [regex]::Escape($routeName)
        $pattern = "(?m)^(\|\s*$escaped\s*\|\s*[^|]*\|\s*[^|]*\|\s*[^|]*\|\s*[^|]*\|)\s*[^|]*\|$"
        if (-not [regex]::IsMatch($updated, $pattern)) {
            throw "Visual QA template is missing route row '$routeName'."
        }

        $reference = "visual-smoke-evidence-report.json#routeAcceptance.$routeName"
        $updated = [regex]::Replace($updated, $pattern, "`$1 $reference |")
    }

    return $updated
}

function Set-QualifiedAccountantScenarioReferences {
    param(
        [string]$Content,
        [string[]]$ScenarioCodes
    )

    $updated = $Content
    foreach ($scenarioCode in $ScenarioCodes) {
        $escaped = [regex]::Escape($scenarioCode)
        $pattern = "(?m)^(\|\s*$escaped\s*\|\s*[^|]*\|\s*[^|]*\|\s*[^|]*\|\s*[^|]*\|\s*[^|]*\|\s*[^|]*\|)\s*[^|]*\|$"
        if (-not [regex]::IsMatch($updated, $pattern)) {
            throw "Qualified-accountant acceptance template is missing scenario row '$scenarioCode'."
        }

        $reference = "qualified-accountant-walkthrough-ledger#$scenarioCode"
        $updated = [regex]::Replace($updated, $pattern, "`$1 $reference |")
    }

    return $updated
}

function Set-QualifiedAccountantRouteReferences {
    param(
        [string]$Content,
        [string[]]$RouteNames
    )

    $updated = $Content
    foreach ($routeName in $RouteNames) {
        $escaped = [regex]::Escape($routeName)
        $pattern = "(?m)^(\|\s*$escaped\s*\|\s*[^|]*\|\s*[^|]*\|)\s*[^|]*\|\s*[^|]*\|$"
        if (-not [regex]::IsMatch($updated, $pattern)) {
            throw "Qualified-accountant acceptance template is missing route row '$routeName'."
        }

        $workbenchReference = "accountant-workbench-evidence-report.json#routeAcceptance.$routeName"
        $walkthroughReference = "qualified-accountant-route-walkthrough#$routeName"
        $updated = [regex]::Replace($updated, $pattern, "`$1 $workbenchReference | $walkthroughReference |")
    }

    return $updated
}

function Set-ExternalRosIxbrlScenarioReferences {
    param(
        [string]$Content,
        [string[]]$ScenarioCodes
    )

    $updated = $Content
    foreach ($scenarioCode in $ScenarioCodes) {
        $escaped = [regex]::Escape($scenarioCode)
        $pattern = "(?m)^(\|\s*$escaped\s*\|)\s*[^|]*\|\s*[^|]*\|\s*[^|]*\|\s*[^|]*\|\s*[^|]*\|$"
        if (-not [regex]::IsMatch($updated, $pattern)) {
            throw "External ROS/iXBRL validation template is missing scenario row '$scenarioCode'."
        }

        $externalReference = "external-ros-validation-ledger#$scenarioCode"
        $taxonomyReference = "revenue-taxonomy-package-ledger#$scenarioCode"
        $updated = [regex]::Replace($updated, $pattern, "`$1 $externalReference |  | $taxonomyReference |  |  |")
    }

    return $updated
}

function Set-SourceLawReviewNoteReferences {
    param(
        [string]$Content,
        [string[]]$SourceIds
    )

    $updated = $Content
    foreach ($sourceId in $SourceIds) {
        $escaped = [regex]::Escape($sourceId)
        $pattern = "(?m)^(\|\s*$escaped\s*\|\s*[^|]*\|\s*[^|]*\|\s*[^|]*\|\s*[^|]*\|\s*[^|]*\|)\s*[^|]*\|$"
        if (-not [regex]::IsMatch($updated, $pattern)) {
            throw "Source-law review template is missing source row '$sourceId'."
        }

        $reference = "source-law-review-ledger#$sourceId"
        $updated = [regex]::Replace($updated, $pattern, "`$1 $reference |")
    }

    return $updated
}

function Copy-PreparedTemplate {
    param(
        [string]$TemplatePath,
        [string]$DestinationPath,
        [hashtable]$Fields,
        [string[]]$VisualRouteNames = @(),
        [string[]]$SourceLawSourceIds = @(),
        [string[]]$GoldenCorpusScenarioCodes = @(),
        [string[]]$AccountantWorkbenchRouteNames = @()
    )

    if ((Test-Path -LiteralPath $DestinationPath) -and -not $Force) {
        throw "Refusing to overwrite existing evidence file '$DestinationPath'. Use -Force only for a fresh generated workspace, not over reviewer edits."
    }

    $content = Get-Content -LiteralPath $TemplatePath -Raw
    foreach ($field in $Fields.GetEnumerator()) {
        $content = Set-MarkdownField $content $field.Key ([string]$field.Value)
    }

    if ((Split-Path -Leaf $DestinationPath) -eq "visual-qa-signoff-template.md") {
        $content = Set-VisualQaRouteReferenceNotes $content $VisualRouteNames
    }

    if ((Split-Path -Leaf $DestinationPath) -eq "source-law-review-template.md") {
        $content = Set-SourceLawReviewNoteReferences $content $SourceLawSourceIds
    }

    if ((Split-Path -Leaf $DestinationPath) -eq "external-ros-ixbrl-validation-template.md") {
        $content = Set-ExternalRosIxbrlScenarioReferences $content $GoldenCorpusScenarioCodes
    }

    if ((Split-Path -Leaf $DestinationPath) -eq "qualified-accountant-acceptance-template.md") {
        $content = Set-QualifiedAccountantScenarioReferences $content $GoldenCorpusScenarioCodes
        $content = Set-QualifiedAccountantRouteReferences $content $AccountantWorkbenchRouteNames
    }

    Set-Content -LiteralPath $DestinationPath -Value $content -NoNewline
}

$resolvedTemplateDirectory = (Resolve-Path -LiteralPath $TemplateDirectory).Path
$resolvedOutputDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)

if ([string]::IsNullOrWhiteSpace($CommitSha)) {
    $CommitSha = Get-CurrentCommitSha
}

Assert-CommitSha $CommitSha
Assert-GitHubActionsRunUrl $GitHubActionsRunUrl

$productionReadinessReportPath = Resolve-OptionalPath $ProductionReadinessReportPath
$productionReadinessVerificationReportPath = Resolve-OptionalPath $ProductionReadinessVerificationReportPath
$visualSmokeEvidenceReportPath = Resolve-OptionalPath $VisualSmokeEvidenceReportPath
$monitoringErrorRoutingReportPath = Resolve-OptionalPath $MonitoringErrorRoutingReportPath
$structuredLogReportPath = Resolve-OptionalPath $StructuredLogReportPath
$visualSmokeEvidenceDirectory = if ([string]::IsNullOrWhiteSpace($visualSmokeEvidenceReportPath)) { "" } else { Split-Path -Parent $visualSmokeEvidenceReportPath }
$visualSmokeManifestPath = if ([string]::IsNullOrWhiteSpace($visualSmokeEvidenceDirectory)) { "" } else { Join-Path $visualSmokeEvidenceDirectory "visual-smoke-manifest.json" }
$accountantWorkbenchEvidenceReportPath = if ([string]::IsNullOrWhiteSpace($visualSmokeEvidenceDirectory)) { "" } else { Join-Path $visualSmokeEvidenceDirectory "accountant-workbench-evidence-report.json" }

$productionReadinessReport = Read-JsonFile $productionReadinessReportPath
$productionReadinessVerificationReport = Read-JsonFile $productionReadinessVerificationReportPath
$visualSmokeEvidenceReport = Read-JsonFile $visualSmokeEvidenceReportPath
$monitoringErrorRoutingReport = Read-JsonFile $monitoringErrorRoutingReportPath
$structuredLogReport = Read-JsonFile $structuredLogReportPath
$visualRouteNames = Get-VisualRouteNames $visualSmokeEvidenceReport
$sourceLawSnapshotHash = Get-SourceLawSnapshotContentHash $productionReadinessReport
$sourceLawSourceIds = Get-SourceLawSourceIds $productionReadinessReport
$goldenCorpusScenarioCodes = Get-GoldenCorpusScenarioCodes $productionReadinessReport
$accountantWorkbenchEvidenceReport = Read-JsonFile $accountantWorkbenchEvidenceReportPath
$accountantWorkbenchRouteNames = Get-AccountantWorkbenchRouteNames $accountantWorkbenchEvidenceReport

$productionReadinessTimestamp = Convert-JsonValueToEvidenceString (Get-JsonPropertyValue $productionReadinessReport "generatedAt")
$checkedAtUtc = Convert-JsonValueToEvidenceString (Get-JsonPropertyValue $monitoringErrorRoutingReport "checkedAtUtc")

New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null

$retainedMachineEvidence = @(
    Copy-MachineEvidenceInput $productionReadinessReportPath $resolvedOutputDirectory "production-readiness-report.json" "Production readiness report" "production-readiness-report" "production-readiness-report.json"
    Copy-MachineEvidenceInput $productionReadinessVerificationReportPath $resolvedOutputDirectory "production-readiness-verification-report.json" "Production readiness verification report" "production-readiness-report" "production-readiness-verification-report.json"
    Copy-MachineEvidenceInput $visualSmokeManifestPath $resolvedOutputDirectory "visual-smoke-manifest.json" "Visual smoke manifest" "visual-smoke-screenshots" "visual-smoke-manifest.json"
    Copy-MachineEvidenceInput $visualSmokeEvidenceReportPath $resolvedOutputDirectory "visual-smoke-evidence-report.json" "Visual smoke evidence report" "visual-smoke-screenshots" "visual-smoke-evidence-report.json"
    Copy-MachineEvidenceInput $accountantWorkbenchEvidenceReportPath $resolvedOutputDirectory "accountant-workbench-evidence-report.json" "Accountant workbench evidence report" "visual-smoke-screenshots" "accountant-workbench-evidence-report.json"
    Copy-MachineEvidenceInput $monitoringErrorRoutingReportPath $resolvedOutputDirectory "monitoring-error-routing-report.json" "Monitoring error routing report" "monitoring-error-routing-smoke" "monitoring-error-routing-report.json"
    Copy-MachineEvidenceInput $structuredLogReportPath $resolvedOutputDirectory "structured-log-report.json" "Structured log report" "structured-json-log-sample" "structured-log-report.json"
)

$commonFields = @{
    "Commit SHA" = $CommitSha
    "GitHub Actions run URL" = $GitHubActionsRunUrl
}

$timestampFields = @{
    "Production readiness report timestamp" = $productionReadinessTimestamp
}

$preparedTemplates = @(
    [pscustomobject]@{
        FileName = "visual-qa-signoff-template.md"
        Fields = $commonFields + @{
            "Visual smoke manifest file" = "visual-smoke-manifest.json"
            "Visual smoke evidence report file" = "visual-smoke-evidence-report.json"
            "Accountant workbench evidence report file" = "accountant-workbench-evidence-report.json"
            "Minimum PNG IDAT byte size" = Get-MinimumVisualMetric $visualSmokeEvidenceReport @("pngIdatByteSize")
            "Minimum screenshot pixel sample count" = Get-MinimumVisualMetric $visualSmokeEvidenceReport @("pixelSampleCount")
            "Minimum sampled distinct color count" = Get-MinimumVisualMetric $visualSmokeEvidenceReport @("sampledDistinctColorCount")
            "Minimum screenshot luminance range" = Get-MinimumVisualMetric $visualSmokeEvidenceReport @("luminanceRange")
            "Minimum automated contrast ratio" = Get-MinimumVisualMetric $visualSmokeEvidenceReport @("themeContrastResult", "minimumContrastRatio")
        }
    },
    [pscustomobject]@{
        FileName = "source-law-review-template.md"
        Fields = $commonFields + $timestampFields + @{
            "Source-law snapshot fingerprint" = "source-law-snapshot-fingerprint#$sourceLawSnapshotHash"
            "Source-law snapshot content hash" = $sourceLawSnapshotHash
        }
    },
    [pscustomobject]@{
        FileName = "qualified-accountant-acceptance-template.md"
        Fields = $commonFields + $timestampFields
    },
    [pscustomobject]@{
        FileName = "external-ros-ixbrl-validation-template.md"
        Fields = $commonFields + $timestampFields
    },
    [pscustomobject]@{
        FileName = "manual-handoff-acceptance-template.md"
        Fields = $commonFields + $timestampFields
    },
    [pscustomobject]@{
        FileName = "monitoring-provider-confirmation-template.md"
        Fields = $commonFields + @{
            "Provider" = [string](Get-JsonPropertyValue $monitoringErrorRoutingReport "provider")
            "Event id" = [string](Get-JsonPropertyValue $monitoringErrorRoutingReport "eventId")
            "Correlation id" = [string](Get-JsonPropertyValue $monitoringErrorRoutingReport "correlationId")
            "Base URL" = [string](Get-JsonPropertyValue $monitoringErrorRoutingReport "baseUrl")
            "Checked at UTC" = $checkedAtUtc
            "Structured log file" = [string](Get-FirstJsonPropertyValue $structuredLogReport @("structuredLogFile", "logFileName"))
            "JSON log line count" = [string](Get-JsonPropertyValue $structuredLogReport "jsonLogLineCount")
            "Matched monitoring smoke line" = if ([bool](Get-JsonPropertyValue $structuredLogReport "matchedMonitoringSmokeLine")) { "yes" } else { "" }
        }
    }
)

$reviewerQueue = @(
    [pscustomobject]@{
        EvidenceGate = "Visual QA sign-off"
        TemplateFile = "visual-qa-signoff-template.md"
        ReviewerRole = "Named visual QA reviewer"
        SignOffGate = "visual-qa-screenshot-review"
        HumanAction = "Review every retained light/dark desktop/mobile screenshot and record exact pass cells, notes, decision, reviewer identity, UTC time, and signature."
    },
    [pscustomobject]@{
        EvidenceGate = "Source-law review"
        TemplateFile = "source-law-review-template.md"
        ReviewerRole = "Named source-law reviewer plus qualified accountant"
        SignOffGate = "source-law-change-review"
        HumanAction = "Check current CRO, Revenue, FRC, and Charities Regulator sources, record source-row outcomes, qualified-accountant source-law sign-off, UTC time, and signatures."
    },
    [pscustomobject]@{
        EvidenceGate = "External ROS/iXBRL validation"
        TemplateFile = "external-ros-ixbrl-validation-template.md"
        ReviewerRole = "External ROS/iXBRL validation reviewer"
        SignOffGate = "external-ros-validation-evidence"
        HumanAction = "Retain external validation provider references for the exact generated iXBRL hashes, taxonomy package references, warnings/errors status, decision, UTC time, and signature."
    },
    [pscustomobject]@{
        EvidenceGate = "Qualified-accountant acceptance"
        TemplateFile = "qualified-accountant-acceptance-template.md"
        ReviewerRole = "Named qualified accountant"
        SignOffGate = "qualified-accountant-final-signoff"
        HumanAction = "Walk the golden corpus and workbench routes, record accepted scenario and route rows, accountant identity, UTC time, and signature."
    },
    [pscustomobject]@{
        EvidenceGate = "Manual handoff acceptance"
        TemplateFile = "manual-handoff-acceptance-template.md"
        ReviewerRole = "Named manual handoff reviewer"
        SignOffGate = "manual-accountant-acceptance"
        HumanAction = "Review audit-required and unsupported paths, retain exact handoff evidence anchors, accepted decisions, reviewer identity, UTC time, and signature."
    },
    [pscustomobject]@{
        EvidenceGate = "Monitoring provider confirmation"
        TemplateFile = "monitoring-provider-confirmation-template.md"
        ReviewerRole = "Named release operator"
        SignOffGate = "production-monitoring"
        HumanAction = "Confirm the controlled smoke event in the real provider, retain provider URL/reference, no-PII and alert-routing review, accepted decision, UTC time, and signature."
    }
)

$machineEvidenceSummaryFile = "release-evidence-machine-summary.json"
$machineEvidenceSummary = [ordered]@{
    status = "pending-human-evidence"
    generatedAt = [DateTimeOffset]::UtcNow.ToString("O")
    releaseCandidate = [ordered]@{
        commitSha = $CommitSha
        githubActionsRunUrl = $GitHubActionsRunUrl
    }
    retainedMachineEvidence = @($retainedMachineEvidence)
    productionReadiness = [ordered]@{
        generatedAt = $productionReadinessTimestamp
        overallStatus = [string](Get-JsonPropertyValue $productionReadinessReport "overallStatus")
        scorecardStatus = [string](Get-JsonPathValue $productionReadinessReport @("productionScorecard", "status"))
        currentScore = Get-JsonPathValue $productionReadinessReport @("productionScorecard", "currentScore")
        targetScore = Get-JsonPathValue $productionReadinessReport @("productionScorecard", "targetScore")
        verificationStatus = [string](Get-JsonPropertyValue $productionReadinessVerificationReport "status")
        verificationFailureCount = Get-JsonPropertyValue $productionReadinessVerificationReport "failureCount"
        humanReleaseEvidenceCloseoutStepCodes = @((Get-JsonPathValue $productionReadinessVerificationReport @("requiredCoverage", "humanReleaseEvidenceCloseoutStepCodes")) | ForEach-Object { [string]$_ })
    }
    visualEvidence = [ordered]@{
        manifestFile = "visual-smoke-manifest.json"
        evidenceReportFile = "visual-smoke-evidence-report.json"
        accountantWorkbenchEvidenceReportFile = "accountant-workbench-evidence-report.json"
        screenshotCount = Get-JsonPropertyValue $visualSmokeEvidenceReport "screenshotCount"
        expectedScreenshotCount = Get-JsonPropertyValue $visualSmokeEvidenceReport "expectedScreenshotCount"
        routeCount = Get-JsonPropertyValue $visualSmokeEvidenceReport "routeCount"
        minimumPngIdatByteSize = Get-MinimumVisualMetric $visualSmokeEvidenceReport @("pngIdatByteSize")
        minimumScreenshotPixelSampleCount = Get-MinimumVisualMetric $visualSmokeEvidenceReport @("pixelSampleCount")
        minimumSampledDistinctColorCount = Get-MinimumVisualMetric $visualSmokeEvidenceReport @("sampledDistinctColorCount")
        minimumScreenshotLuminanceRange = Get-MinimumVisualMetric $visualSmokeEvidenceReport @("luminanceRange")
        minimumAutomatedContrastRatio = Get-MinimumVisualMetric $visualSmokeEvidenceReport @("themeContrastResult", "minimumContrastRatio")
    }
    monitoringEvidence = [ordered]@{
        provider = [string](Get-JsonPropertyValue $monitoringErrorRoutingReport "provider")
        eventId = [string](Get-JsonPropertyValue $monitoringErrorRoutingReport "eventId")
        correlationId = [string](Get-JsonPropertyValue $monitoringErrorRoutingReport "correlationId")
        baseUrl = [string](Get-JsonPropertyValue $monitoringErrorRoutingReport "baseUrl")
        checkedAtUtc = $checkedAtUtc
        structuredLogFile = [string](Get-FirstJsonPropertyValue $structuredLogReport @("structuredLogFile", "logFileName"))
        jsonLogLineCount = Get-JsonPropertyValue $structuredLogReport "jsonLogLineCount"
        matchedMonitoringSmokeLine = [bool](Get-JsonPropertyValue $structuredLogReport "matchedMonitoringSmokeLine")
    }
    reviewerQueue = @($reviewerQueue)
    completionPolicy = "This summary is machine evidence only; all six human evidence templates must still be completed by named reviewers."
}

$machineEvidenceSummaryPath = Join-Path $resolvedOutputDirectory $machineEvidenceSummaryFile
$machineEvidenceSummary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $machineEvidenceSummaryPath

foreach ($template in $preparedTemplates) {
    $source = Join-Path $resolvedTemplateDirectory $template.FileName
    $destination = Join-Path $resolvedOutputDirectory $template.FileName
    Copy-PreparedTemplate $source $destination $template.Fields $visualRouteNames $sourceLawSourceIds $goldenCorpusScenarioCodes $accountantWorkbenchRouteNames
}

$completionLedgerFile = "release-evidence-reviewer-completion.json"
$completionLedger = [ordered]@{
    status = "pending-human-evidence"
    generatedAt = [DateTimeOffset]::UtcNow.ToString("O")
    releaseCandidate = [ordered]@{
        commitSha = $CommitSha
        githubActionsRunUrl = $GitHubActionsRunUrl
    }
    completionPolicy = "All six entries must be completed by named human reviewers before release evidence can pass."
    entries = @($reviewerQueue | ForEach-Object {
        [ordered]@{
            evidenceGate = $_.EvidenceGate
            templateFile = $_.TemplateFile
            reviewerRole = $_.ReviewerRole
            signOffGate = $_.SignOffGate
            status = "pending-human-evidence"
            completed = $false
            completedBy = ""
            completedAtUtc = ""
            evidenceReportStatus = "incomplete-before-review"
            humanAction = $_.HumanAction
        }
    })
}

$completionLedgerPath = Join-Path $resolvedOutputDirectory $completionLedgerFile
$completionLedger | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $completionLedgerPath

$manifest = [ordered]@{
    status = "pending-human-evidence"
    generatedAt = [DateTimeOffset]::UtcNow.ToString("O")
    commitSha = $CommitSha
    githubActionsRunUrl = $GitHubActionsRunUrl
    sourceTemplateDirectory = $resolvedTemplateDirectory
    outputDirectory = $resolvedOutputDirectory
    productionReadinessReportPath = $productionReadinessReportPath
    visualSmokeEvidenceReportPath = $visualSmokeEvidenceReportPath
    monitoringErrorRoutingReportPath = $monitoringErrorRoutingReportPath
    structuredLogReportPath = $structuredLogReportPath
    reviewerIndexFile = "release-evidence-reviewer-index.md"
    reviewerCompletionFile = $completionLedgerFile
    machineEvidenceSummaryFile = $machineEvidenceSummaryFile
    preparedTemplates = @($preparedTemplates | ForEach-Object { $_.FileName })
    retainedMachineEvidence = @($retainedMachineEvidence)
    reviewerQueue = @($reviewerQueue)
    humanFieldsLeftBlank = @(
        "reviewer/operator/accountant identity and role",
        "review dates and signatures",
        "source-law source row decisions",
        "visual route pass/fail cells",
        "golden corpus and route acceptance rows",
        "external ROS/iXBRL provider/run evidence, artifact hashes, warnings/errors and decisions",
        "manual handoff scenario/path decisions",
        "monitoring provider URL/reference and operator decision"
    )
}

$manifestPath = Join-Path $resolvedOutputDirectory "release-evidence-workspace-manifest.json"
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath

$reviewerRows = @($reviewerQueue | ForEach-Object {
    "| $($_.EvidenceGate) | $($_.TemplateFile) | $($_.ReviewerRole) | $($_.SignOffGate) | $($_.HumanAction) |"
})

$indexContent = @"
# Release Evidence Reviewer Workspace

Status: pending-human-evidence

This workspace is reviewer preparation only. It is not release approval and it is not professional sign-off.

## Release Candidate

- Commit SHA: $CommitSha
- GitHub Actions run URL: $GitHubActionsRunUrl
- Production readiness report timestamp: $productionReadinessTimestamp

## Machine Evidence Inputs

Machine summary: release-evidence-machine-summary.json

| Evidence input | Retained file | Source CI artifact |
| --- | --- | --- |
| Production readiness report | production-readiness-report.json | production-readiness-report / production-readiness-report.json |
| Production readiness verification report | production-readiness-verification-report.json | production-readiness-report / production-readiness-verification-report.json |
| Visual smoke manifest | visual-smoke-manifest.json | visual-smoke-screenshots / visual-smoke-manifest.json |
| Visual smoke evidence report | visual-smoke-evidence-report.json | visual-smoke-screenshots / visual-smoke-evidence-report.json |
| Accountant workbench evidence report | accountant-workbench-evidence-report.json | visual-smoke-screenshots / accountant-workbench-evidence-report.json |
| Monitoring error routing report | monitoring-error-routing-report.json | monitoring-error-routing-smoke / monitoring-error-routing-report.json |
| Structured log report | structured-log-report.json | structured-json-log-sample / structured-log-report.json |

## Reviewer Queue

| Evidence gate | Template file | Required reviewer | Sign-off gate | Human action still required |
| --- | --- | --- | --- | --- |
$($reviewerRows -join "`n")

## Reviewer Completion Ledger

Use ``release-evidence-reviewer-completion.json`` as the handoff checklist. It is generated with all six entries in ``pending-human-evidence`` status and must not be treated as approval. The release evidence verifier remains the authority after reviewers complete the Markdown templates.

## Reviewer Handoff Files

After workspace verification runs, retain ``release-evidence-reviewer-blockers.md``, ``release-evidence-verifier-output.txt``, and ``release-evidence-workspace-verification-report.json`` with this index. These files show why the prepared workspace is still blocked before named human sign-off and preserve the machine-evidence provenance chain reviewers must not overwrite.

## Reviewer Closeout Sequence

1. Complete the six Markdown templates with named reviewer identities, UTC timestamps, retained evidence references, accepted decisions, and signatures.
2. Run ``scripts/verify-release-evidence.ps1 -EvidenceDirectory <this-workspace> -ReportPath <this-workspace>/release-evidence-report.json`` and retain the passing ``release-evidence-report.json``.
3. Confirm ``release-evidence-report.json`` has six accepted ``humanEvidenceCompletion`` entries and no blocking failures.
4. Run ``scripts/verify-release-artifact-pack.ps1`` against the final collected release artifacts for the same commit SHA and GitHub Actions run URL.

## Completion Gate

Run `scripts/verify-release-evidence.ps1 -EvidenceDirectory <this-workspace> -ReportPath <this-workspace>/release-evidence-report.json` after all reviewers complete the templates. The workspace must remain blocked until the verifier passes with all six human evidence templates completed by named reviewers.
"@

$reviewerIndexPath = Join-Path $resolvedOutputDirectory "release-evidence-reviewer-index.md"
Set-Content -LiteralPath $reviewerIndexPath -Value $indexContent -NoNewline

[pscustomobject]@{
    status = "pending-human-evidence"
    outputDirectory = $resolvedOutputDirectory
    manifestPath = $manifestPath
    reviewerIndexPath = $reviewerIndexPath
    reviewerCompletionPath = $completionLedgerPath
    preparedTemplateCount = $preparedTemplates.Count
}
