param(
    [string]$NpmAuditJsonPath = "",

    [Parameter(Mandatory = $true)]
    [string]$EvidencePath
)

$ErrorActionPreference = "Stop"

$RepositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))

function Get-FileSha256([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required file does not exist: $Path"
    }

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Read-JsonFile([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "JSON file does not exist: $Path"
    }

    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

function Read-LockfileVersion([string]$Path) {
    $content = Get-Content -LiteralPath $Path -Raw
    $match = [regex]::Match($content, '"lockfileVersion"\s*:\s*(\d+)')
    if (-not $match.Success) {
        throw "frontend/package-lock.json did not include lockfileVersion."
    }

    return [int]$match.Groups[1].Value
}

$frontendPackagePath = Join-Path $RepositoryRoot "frontend/package.json"
$frontendLockPath = Join-Path $RepositoryRoot "frontend/package-lock.json"
$directoryBuildPropsPath = Join-Path $RepositoryRoot "backend/Directory.Build.props"
$workflowPath = Join-Path $RepositoryRoot ".github/workflows/ci.yml"
$globalJsonPath = Join-Path $RepositoryRoot "global.json"
$nodeVersionPath = Join-Path $RepositoryRoot ".nvmrc"

$packageJson = Read-JsonFile $frontendPackagePath
$packageLockVersion = Read-LockfileVersion $frontendLockPath
$directoryBuildProps = Get-Content -LiteralPath $directoryBuildPropsPath -Raw
$workflow = Get-Content -LiteralPath $workflowPath -Raw

if ($packageLockVersion -ne 3) {
    throw "frontend/package-lock.json must use lockfileVersion 3."
}

if ($workflow.IndexOf("npm ci", [StringComparison]::Ordinal) -lt 0) {
    throw "CI must install frontend dependencies with npm ci."
}

if ($workflow.IndexOf("npm audit --audit-level=moderate", [StringComparison]::Ordinal) -lt 0) {
    throw "CI must run npm audit --audit-level=moderate."
}

if ($workflow.IndexOf("node scripts/verify-ci-actions.mjs", [StringComparison]::Ordinal) -lt 0) {
    throw "CI must run scripts/verify-ci-actions.mjs."
}

foreach ($requiredText in @("<NuGetAudit>true</NuGetAudit>", "<NuGetAuditMode>all</NuGetAuditMode>", "<NuGetAuditLevel>low</NuGetAuditLevel>", "NU1901", "NU1902", "NU1903", "NU1904")) {
    if ($directoryBuildProps.IndexOf($requiredText, [StringComparison]::Ordinal) -lt 0) {
        throw "backend/Directory.Build.props is missing NuGet audit policy text: $requiredText"
    }
}

$npmAuditSummary = [ordered]@{
    provided = $false
}
if (-not [string]::IsNullOrWhiteSpace($NpmAuditJsonPath)) {
    $audit = Read-JsonFile $NpmAuditJsonPath
    $vulnerabilities = $audit.metadata.vulnerabilities
    if ($null -eq $vulnerabilities) {
        throw "npm audit JSON did not include metadata.vulnerabilities."
    }

    $moderate = [int]$vulnerabilities.moderate
    $high = [int]$vulnerabilities.high
    $critical = [int]$vulnerabilities.critical
    if (($moderate + $high + $critical) -gt 0) {
        throw "npm audit reported moderate/high/critical vulnerabilities."
    }

    $npmAuditSummary = [ordered]@{
        provided = $true
        auditLevel = "moderate"
        low = [int]$vulnerabilities.low
        moderate = $moderate
        high = $high
        critical = $critical
        total = [int]$vulnerabilities.total
        dependencies = [int]$audit.metadata.dependencies.total
    }
}

$evidenceDirectory = Split-Path -Parent $EvidencePath
if (-not [string]::IsNullOrWhiteSpace($evidenceDirectory)) {
    New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null
}

[ordered]@{
    status = "passed"
    checkedAtUtc = [DateTime]::UtcNow.ToString("o")
    frontend = @{
        packageName = [string]$packageJson.name
        packageVersion = [string]$packageJson.version
        nodeVersion = (Get-Content -LiteralPath $nodeVersionPath -Raw).Trim()
        packageJsonSha256 = Get-FileSha256 $frontendPackagePath
        packageLockSha256 = Get-FileSha256 $frontendLockPath
        lockfileVersion = $packageLockVersion
        npmAudit = $npmAuditSummary
    }
    backend = @{
        globalJsonSha256 = Get-FileSha256 $globalJsonPath
        directoryBuildPropsSha256 = Get-FileSha256 $directoryBuildPropsPath
        nugetAudit = @{
            enabled = $true
            mode = "all"
            level = "low"
            warningsAsErrors = @("NU1901", "NU1902", "NU1903", "NU1904")
        }
    }
    ci = @{
        workflowSha256 = Get-FileSha256 $workflowPath
        installsWithNpmCi = $true
        runsNpmAuditModerate = $true
        runsCiActionVerifier = $true
    }
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $EvidencePath -Encoding UTF8

Write-Host "Dependency evidence written: $EvidencePath"
