param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\artifacts\release-evidence-workspace"),
    [string]$TemplateDirectory = (Join-Path $PSScriptRoot "..\Docs\release-evidence"),
    [string]$CommitSha = "",
    [string]$GitHubActionsRunUrl = "",
    [string]$ProductionReadinessReportPath = "",
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
        [string]$EvidenceName
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

function Copy-PreparedTemplate {
    param(
        [string]$TemplatePath,
        [string]$DestinationPath,
        [hashtable]$Fields
    )

    if ((Test-Path -LiteralPath $DestinationPath) -and -not $Force) {
        throw "Refusing to overwrite existing evidence file '$DestinationPath'. Use -Force only for a fresh generated workspace, not over reviewer edits."
    }

    $content = Get-Content -LiteralPath $TemplatePath -Raw
    foreach ($field in $Fields.GetEnumerator()) {
        $content = Set-MarkdownField $content $field.Key ([string]$field.Value)
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
$visualSmokeEvidenceReportPath = Resolve-OptionalPath $VisualSmokeEvidenceReportPath
$monitoringErrorRoutingReportPath = Resolve-OptionalPath $MonitoringErrorRoutingReportPath
$structuredLogReportPath = Resolve-OptionalPath $StructuredLogReportPath
$visualSmokeEvidenceDirectory = if ([string]::IsNullOrWhiteSpace($visualSmokeEvidenceReportPath)) { "" } else { Split-Path -Parent $visualSmokeEvidenceReportPath }
$visualSmokeManifestPath = if ([string]::IsNullOrWhiteSpace($visualSmokeEvidenceDirectory)) { "" } else { Join-Path $visualSmokeEvidenceDirectory "visual-smoke-manifest.json" }
$accountantWorkbenchEvidenceReportPath = if ([string]::IsNullOrWhiteSpace($visualSmokeEvidenceDirectory)) { "" } else { Join-Path $visualSmokeEvidenceDirectory "accountant-workbench-evidence-report.json" }

$productionReadinessReport = Read-JsonFile $productionReadinessReportPath
$visualSmokeEvidenceReport = Read-JsonFile $visualSmokeEvidenceReportPath
$monitoringErrorRoutingReport = Read-JsonFile $monitoringErrorRoutingReportPath
$structuredLogReport = Read-JsonFile $structuredLogReportPath

$productionReadinessTimestamp = [string](Get-JsonPropertyValue $productionReadinessReport "generatedAt")
$checkedAtUtc = [string](Get-JsonPropertyValue $monitoringErrorRoutingReport "checkedAtUtc")

New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null

$retainedMachineEvidence = @(
    Copy-MachineEvidenceInput $productionReadinessReportPath $resolvedOutputDirectory "production-readiness-report.json" "Production readiness report"
    Copy-MachineEvidenceInput $visualSmokeManifestPath $resolvedOutputDirectory "visual-smoke-manifest.json" "Visual smoke manifest"
    Copy-MachineEvidenceInput $visualSmokeEvidenceReportPath $resolvedOutputDirectory "visual-smoke-evidence-report.json" "Visual smoke evidence report"
    Copy-MachineEvidenceInput $accountantWorkbenchEvidenceReportPath $resolvedOutputDirectory "accountant-workbench-evidence-report.json" "Accountant workbench evidence report"
    Copy-MachineEvidenceInput $monitoringErrorRoutingReportPath $resolvedOutputDirectory "monitoring-error-routing-report.json" "Monitoring error routing report"
    Copy-MachineEvidenceInput $structuredLogReportPath $resolvedOutputDirectory "structured-log-report.json" "Structured log report"
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
        Fields = $commonFields + $timestampFields
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
            "Structured log file" = [string](Get-JsonPropertyValue $structuredLogReport "structuredLogFile")
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

foreach ($template in $preparedTemplates) {
    $source = Join-Path $resolvedTemplateDirectory $template.FileName
    $destination = Join-Path $resolvedOutputDirectory $template.FileName
    Copy-PreparedTemplate $source $destination $template.Fields
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
    preparedTemplates = @($preparedTemplates | ForEach-Object { $_.FileName })
    retainedMachineEvidence = @($retainedMachineEvidence)
    reviewerQueue = @($reviewerQueue)
    humanFieldsLeftBlank = @(
        "reviewer/operator/accountant identity and role",
        "review dates and signatures",
        "source-law source row decisions",
        "visual route pass/fail cells",
        "golden corpus and route acceptance rows",
        "external ROS/iXBRL validation references and artifact hashes",
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

- Production readiness report: production-readiness-report.json
- Visual smoke manifest: visual-smoke-manifest.json
- Visual smoke evidence report: visual-smoke-evidence-report.json
- Accountant workbench evidence report: accountant-workbench-evidence-report.json
- Monitoring error routing report: monitoring-error-routing-report.json
- Structured log report: structured-log-report.json

## Reviewer Queue

| Evidence gate | Template file | Required reviewer | Sign-off gate | Human action still required |
| --- | --- | --- | --- | --- |
$($reviewerRows -join "`n")

## Reviewer Completion Ledger

Use ``release-evidence-reviewer-completion.json`` as the handoff checklist. It is generated with all six entries in ``pending-human-evidence`` status and must not be treated as approval. The release evidence verifier remains the authority after reviewers complete the Markdown templates.

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
