param(
    [Parameter(Mandatory = $true)]
    [string]$BaseUrl,
    [string]$ComposeFile = "compose.production.yml",
    [string]$EvidencePath = (Join-Path ([System.IO.Path]::GetTempPath()) "production-failover-report.json"),
    [int]$FailureDetectionSeconds = 30,
    [int]$ApiRecoverySeconds = 120,
    [int]$DatabaseRecoverySeconds = 180,
    [string]$CommitSha = $env:GITHUB_SHA,
    [string]$GitHubActionsRunUrl = $(if ($env:GITHUB_REPOSITORY -and $env:GITHUB_RUN_ID) { "https://github.com/$env:GITHUB_REPOSITORY/actions/runs/$env:GITHUB_RUN_ID" } else { "" }),
    [Parameter(Mandatory = $true)]
    [switch]$ConfirmEphemeralCandidateStack,
    [Parameter(Mandatory = $true)]
    [string]$ExpectedComposeProject,
    [switch]$SkipCertificateCheck
)

$ErrorActionPreference = "Stop"

if ($FailureDetectionSeconds -le 0 -or $ApiRecoverySeconds -le 0 -or $DatabaseRecoverySeconds -le 0) {
    throw "Failure detection and recovery targets must be positive seconds."
}
if (-not $ConfirmEphemeralCandidateStack) {
    throw "Refusing service interruption without -ConfirmEphemeralCandidateStack. Never run this drill against production."
}
if ($ExpectedComposeProject -notmatch '^[a-z0-9][a-z0-9_.-]*$') {
    throw "ExpectedComposeProject must be an explicit lowercase Docker Compose project name."
}
if ($CommitSha -cnotmatch '^[0-9a-f]{40}$') {
    throw "CommitSha must be a full lowercase 40-character hexadecimal Git commit SHA."
}
if ($GitHubActionsRunUrl -cnotmatch '^https://github\.com/[^/\s]+/[^/\s]+/actions/runs/[0-9]+$') {
    throw "GitHubActionsRunUrl must be an exact GitHub Actions run URL."
}

$origin = [Uri]$BaseUrl
if ($origin.Scheme -ne "https" -and -not ($origin.IsLoopback -and $origin.Scheme -eq "http")) {
    throw "BaseUrl must use HTTPS except for an explicit loopback target."
}

$composePath = (Resolve-Path -LiteralPath $ComposeFile).Path
$evidenceFullPath = [System.IO.Path]::GetFullPath($EvidencePath)
$evidenceDirectory = Split-Path -Parent $evidenceFullPath
New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null

$failures = [System.Collections.Generic.List[string]]::new()
$observations = [System.Collections.Generic.List[object]]::new()
$readyUri = [Uri]::new($origin, "/health/ready")

function Invoke-Compose([string[]]$Arguments) {
    & docker compose -f $composePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

$composeInventoryText = @((Invoke-Compose @("ps", "--format", "json"))) -join [Environment]::NewLine
try {
    $parsedComposeInventory = $composeInventoryText | ConvertFrom-Json
    $composeInventory = @($parsedComposeInventory)
} catch {
    throw "Unable to inspect the candidate Compose service inventory as JSON."
}
foreach ($requiredService in @("api", "db")) {
    $serviceRows = @($composeInventory | Where-Object { [string]$_.Service -eq $requiredService })
    if ($serviceRows.Count -ne 1 -or [string]$serviceRows[0].Project -cne $ExpectedComposeProject -or [string]$serviceRows[0].State -ne "running") {
        throw "Refusing interruption: expected exactly one running '$requiredService' service in Compose project '$ExpectedComposeProject'."
    }
}

function Get-ReadyStatus {
    $request = @{
        Uri = $readyUri.AbsoluteUri
        Method = "Get"
        TimeoutSec = 10
        UseBasicParsing = $true
    }
    if ($SkipCertificateCheck) {
        if (-not (Get-Command Invoke-WebRequest).Parameters.ContainsKey("SkipCertificateCheck")) {
            throw "SkipCertificateCheck requires PowerShell 7 or later."
        }
        $request.SkipCertificateCheck = $true
    }

    try {
        $response = Invoke-WebRequest @request
        return [int]$response.StatusCode
    }
    catch {
        if ($null -ne $_.Exception.Response -and $null -ne $_.Exception.Response.StatusCode) {
            return [int]$_.Exception.Response.StatusCode
        }
        return 0
    }
}

function Wait-ForReadinessState(
    [string]$Phase,
    [bool]$ExpectedHealthy,
    [int]$TimeoutSeconds
) {
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $lastStatus = 0
    do {
        $lastStatus = Get-ReadyStatus
        $healthy = $lastStatus -eq 200
        if ($healthy -eq $ExpectedHealthy) {
            $stopwatch.Stop()
            $observation = [ordered]@{
                phase = $Phase
                expectedHealthy = $ExpectedHealthy
                observedStatusCode = $(if ($lastStatus -eq 0) { $null } else { $lastStatus })
                elapsedMilliseconds = [math]::Round($stopwatch.Elapsed.TotalMilliseconds, 3)
                passed = $true
            }
            $observations.Add($observation)
            return $observation
        }
        Start-Sleep -Seconds 1
    } while ($stopwatch.Elapsed.TotalSeconds -lt $TimeoutSeconds)

    $stopwatch.Stop()
    $observation = [ordered]@{
        phase = $Phase
        expectedHealthy = $ExpectedHealthy
        observedStatusCode = $(if ($lastStatus -eq 0) { $null } else { $lastStatus })
        elapsedMilliseconds = [math]::Round($stopwatch.Elapsed.TotalMilliseconds, 3)
        passed = $false
    }
    $observations.Add($observation)
    $failures.Add("$Phase did not reach expected healthy=$ExpectedHealthy within $TimeoutSeconds seconds.")
    return $observation
}

$apiStopped = $false
$databaseStopped = $false
$startedAt = [DateTimeOffset]::UtcNow

try {
    $initial = Wait-ForReadinessState -Phase "initial-ready" -ExpectedHealthy $true -TimeoutSeconds 30
    if (-not $initial.passed) { throw $failures[0] }

    Invoke-Compose @("stop", "--timeout", "15", "api")
    $apiStopped = $true
    $apiFailure = Wait-ForReadinessState -Phase "api-host-failure-detected" -ExpectedHealthy $false -TimeoutSeconds $FailureDetectionSeconds

    Invoke-Compose @("start", "api")
    $apiStopped = $false
    $apiRecovery = Wait-ForReadinessState -Phase "api-host-recovered" -ExpectedHealthy $true -TimeoutSeconds $ApiRecoverySeconds

    if ($apiRecovery.passed) {
        Invoke-Compose @("stop", "--timeout", "20", "db")
        $databaseStopped = $true
        $databaseFailure = Wait-ForReadinessState -Phase "database-failure-detected" -ExpectedHealthy $false -TimeoutSeconds $FailureDetectionSeconds

        Invoke-Compose @("start", "db")
        $databaseStopped = $false
        $databaseRecovery = Wait-ForReadinessState -Phase "database-recovered" -ExpectedHealthy $true -TimeoutSeconds $DatabaseRecoverySeconds
    }
}
catch {
    $failures.Add("failover-drill-error:$($_.Exception.GetType().Name)")
}
finally {
    try {
        if ($databaseStopped) { Invoke-Compose @("start", "db") }
        if ($apiStopped) { Invoke-Compose @("start", "api") }
    }
    catch {
        $failures.Add("stack-recovery-cleanup-error:$($_.Exception.GetType().Name)")
    }

    $completedAt = [DateTimeOffset]::UtcNow
    $report = [ordered]@{
        schemaVersion = "accounts-production-failover-v1"
        status = $(if ($failures.Count -eq 0 -and @($observations).Count -eq 5 -and @($observations | Where-Object { -not $_.passed }).Count -eq 0) { "passed" } else { "failed" })
        generatedAtUtc = $completedAt.ToString("o")
        releaseCandidate = [ordered]@{
            commitSha = $CommitSha
            githubActionsRunUrl = $GitHubActionsRunUrl
        }
        targetOrigin = $origin.GetLeftPart([UriPartial]::Authority)
        executionScope = [ordered]@{
            confirmedEphemeralCandidateStack = [bool]$ConfirmEphemeralCandidateStack
            expectedComposeProject = $ExpectedComposeProject
            observedServices = @($composeInventory | Where-Object { [string]$_.Service -in @("api", "db") } | ForEach-Object {
                [ordered]@{
                    project = [string]$_.Project
                    service = [string]$_.Service
                    state = [string]$_.State
                }
            })
        }
        targets = [ordered]@{
            failureDetectionSeconds = $FailureDetectionSeconds
            apiRecoverySeconds = $ApiRecoverySeconds
            databaseRecoverySeconds = $DatabaseRecoverySeconds
        }
        elapsedMilliseconds = [math]::Round(($completedAt - $startedAt).TotalMilliseconds, 3)
        observations = @($observations)
        failures = @($failures)
        privacy = [ordered]@{
            responseBodiesRetained = $false
            authenticationRetained = $false
            tenantOrClientIdentifiersRetained = $false
        }
        scopeBoundary = "Ephemeral candidate-stack interruption proves fail-closed health detection and bounded service recovery only; it is not production host failover, off-host restore, RPO/RTO evidence, or named-operator acceptance."
    }
    [IO.File]::WriteAllText(
        $evidenceFullPath,
        ($report | ConvertTo-Json -Depth 8),
        [Text.UTF8Encoding]::new($false)
    )
}

if ($report.status -ne "passed") {
    throw "Production failover drill failed: $($failures -join '; '). Evidence: $evidenceFullPath"
}

Write-Host "Production failover drill passed. Evidence: $evidenceFullPath"
