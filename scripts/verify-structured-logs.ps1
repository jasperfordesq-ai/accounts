param(
    [Parameter(Mandatory = $true)]
    [string]$LogPath,

    [string]$MonitoringEvidencePath = "",
    [string]$EvidencePath = ""
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $LogPath -PathType Leaf)) {
    throw "LogPath does not exist: $LogPath"
}

$jsonLines = New-Object System.Collections.Generic.List[object]
$rawLines = @(Get-Content -LiteralPath $LogPath)
foreach ($line in $rawLines) {
    if ([string]::IsNullOrWhiteSpace($line)) {
        continue
    }

    $trimmed = $line.Trim()
    if (-not $trimmed.StartsWith("{") -and $trimmed.IndexOf("|", [StringComparison]::Ordinal) -ge 0) {
        $trimmed = $trimmed.Substring($trimmed.IndexOf("|", [StringComparison]::Ordinal) + 1).Trim()
    }

    if (-not $trimmed.StartsWith("{")) {
        continue
    }

    try {
        $jsonLines.Add(($trimmed | ConvertFrom-Json))
    } catch {
        throw "Structured log line was not valid JSON: $trimmed"
    }
}

if ($jsonLines.Count -eq 0) {
    throw "No JSON log lines were found in $LogPath."
}

function Get-PropertyValue($Object, [string[]]$Names) {
    foreach ($name in $Names) {
        $property = $Object.PSObject.Properties[$name]
        if ($null -ne $property -and $null -ne $property.Value -and -not [string]::IsNullOrWhiteSpace([string]$property.Value)) {
            return [string]$property.Value
        }
    }

    return ""
}

$structuredLine = $jsonLines |
    Where-Object {
        -not [string]::IsNullOrWhiteSpace((Get-PropertyValue $_ @("Timestamp", "timestamp", "@t"))) -and
        -not [string]::IsNullOrWhiteSpace((Get-PropertyValue $_ @("LogLevel", "Level", "level", "@l"))) -and
        -not [string]::IsNullOrWhiteSpace((Get-PropertyValue $_ @("Category", "SourceContext", "category")))
    } |
    Select-Object -First 1

if ($null -eq $structuredLine) {
    throw "No JSON log line contained timestamp, level and category fields."
}

$monitoringCorrelationId = ""
$matchedMonitoringSmokeLine = $false
if (-not [string]::IsNullOrWhiteSpace($MonitoringEvidencePath)) {
    if (-not (Test-Path -LiteralPath $MonitoringEvidencePath -PathType Leaf)) {
        throw "MonitoringEvidencePath does not exist: $MonitoringEvidencePath"
    }

    $monitoringEvidence = Get-Content -LiteralPath $MonitoringEvidencePath -Raw | ConvertFrom-Json
    $monitoringCorrelationId = [string]$monitoringEvidence.correlationId
    if ([string]::IsNullOrWhiteSpace($monitoringCorrelationId)) {
        throw "Monitoring evidence did not include a correlationId."
    }

    $matchedMonitoringSmokeLine = @($rawLines | Where-Object {
        $_.IndexOf($monitoringCorrelationId, [StringComparison]::Ordinal) -ge 0 -and
        $_.IndexOf("Controlled monitoring smoke event emitted", [StringComparison]::Ordinal) -ge 0
    }).Count -gt 0

    if (-not $matchedMonitoringSmokeLine) {
        throw "No structured log line matched the monitoring smoke correlation id '$monitoringCorrelationId'."
    }
}

if (-not [string]::IsNullOrWhiteSpace($EvidencePath)) {
    $evidenceDirectory = Split-Path -Parent $EvidencePath
    if (-not [string]::IsNullOrWhiteSpace($evidenceDirectory)) {
        New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null
    }

    [ordered]@{
        status = "passed"
        checkedAtUtc = [DateTime]::UtcNow.ToString("o")
        logFileName = [System.IO.Path]::GetFileName($LogPath)
        jsonLogLineCount = $jsonLines.Count
        structuredFields = @{
            timestamp = Get-PropertyValue $structuredLine @("Timestamp", "timestamp", "@t")
            level = Get-PropertyValue $structuredLine @("LogLevel", "Level", "level", "@l")
            category = Get-PropertyValue $structuredLine @("Category", "SourceContext", "category")
        }
        monitoringCorrelationId = $monitoringCorrelationId
        matchedMonitoringSmokeLine = $matchedMonitoringSmokeLine
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $EvidencePath -Encoding UTF8

    Write-Host "Structured log evidence written: $EvidencePath"
}

Write-Host "Structured log verification passed: $($jsonLines.Count) JSON line(s)."
