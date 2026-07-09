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

$productionReadinessReport = Read-JsonFile $productionReadinessReportPath
$visualSmokeEvidenceReport = Read-JsonFile $visualSmokeEvidenceReportPath
$monitoringErrorRoutingReport = Read-JsonFile $monitoringErrorRoutingReportPath
$structuredLogReport = Read-JsonFile $structuredLogReportPath

$productionReadinessTimestamp = [string](Get-JsonPropertyValue $productionReadinessReport "generatedAt")
$checkedAtUtc = [string](Get-JsonPropertyValue $monitoringErrorRoutingReport "checkedAtUtc")

New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null

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

foreach ($template in $preparedTemplates) {
    $source = Join-Path $resolvedTemplateDirectory $template.FileName
    $destination = Join-Path $resolvedOutputDirectory $template.FileName
    Copy-PreparedTemplate $source $destination $template.Fields
}

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
    preparedTemplates = @($preparedTemplates | ForEach-Object { $_.FileName })
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

[pscustomobject]@{
    status = "pending-human-evidence"
    outputDirectory = $resolvedOutputDirectory
    manifestPath = $manifestPath
    preparedTemplateCount = $preparedTemplates.Count
}
