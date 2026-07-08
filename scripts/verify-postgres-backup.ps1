param(
    [Parameter(Mandatory = $true)]
    [string]$BackupPath,
    [string]$ComposeFile = "compose.production.yml",
    [string]$Service = "db",
    [string]$User = $env:POSTGRES_USER,
    [string]$SourceDatabase = $env:POSTGRES_DB,
    [string]$VerifyDatabase = "accounts_restore_verify",
    [string]$EvidencePath,
    [switch]$KeepVerifyDatabase
)

$ErrorActionPreference = "Stop"

function Assert-SafePostgresIdentifier([string]$Value, [string]$Name) {
    if ($Value -notmatch '^[A-Za-z_][A-Za-z0-9_]{0,62}$') {
        throw "$Name must be a PostgreSQL identifier using letters, numbers, and underscores."
    }
}

function Invoke-NativeCommand([string]$Description, [scriptblock]$Command) {
    & $Command
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "Native command failed ($Description) with exit code $exitCode."
    }
}

function Invoke-ScalarQuery([string]$Description, [string]$Database, [string]$Sql) {
    $output = & docker compose -f $ComposeFile exec -T $Service psql `
        --username $User `
        --dbname $Database `
        --tuples-only `
        --no-align `
        --command $Sql
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "Native command failed ($Description) with exit code $exitCode."
    }

    $value = ($output | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1).Trim()
    if ($value -notmatch '^\d+$') {
        throw "Query '$Description' did not return a non-negative integer count."
    }

    return [int]$value
}

function Assert-RestoredCountMatchesSource([string]$TableName, [string]$Sql, [switch]$RequireNonEmpty) {
    $sourceCount = Invoke-ScalarQuery "Count source $TableName" $SourceDatabase $Sql
    if ($RequireNonEmpty -and $sourceCount -le 0) {
        throw "Source database table '$TableName' is empty; restore drill cannot prove data preservation."
    }

    $restoredCount = Invoke-ScalarQuery "Count restored $TableName" $VerifyDatabase $Sql
    if ($restoredCount -ne $sourceCount) {
        throw "Restored table '$TableName' count $restoredCount did not match source count $sourceCount."
    }

    return [pscustomobject]@{
        table = $TableName
        sourceCount = $sourceCount
        restoredCount = $restoredCount
        requireNonEmpty = [bool]$RequireNonEmpty
    }
}

if ([string]::IsNullOrWhiteSpace($SourceDatabase)) {
    throw "POSTGRES_DB or -SourceDatabase is required."
}
if ([string]::IsNullOrWhiteSpace($User)) {
    throw "POSTGRES_USER or -User is required."
}
if (-not (Test-Path -LiteralPath $BackupPath)) {
    throw "Backup file not found: $BackupPath"
}
Assert-SafePostgresIdentifier $User "User"
Assert-SafePostgresIdentifier $SourceDatabase "SourceDatabase"
Assert-SafePostgresIdentifier $VerifyDatabase "VerifyDatabase"
if ($VerifyDatabase.Equals($SourceDatabase, [StringComparison]::OrdinalIgnoreCase)) {
    throw "VerifyDatabase must be different from SourceDatabase."
}
if (-not [string]::IsNullOrWhiteSpace($EvidencePath)) {
    $evidenceDirectory = Split-Path -Parent ([System.IO.Path]::GetFullPath($EvidencePath))
    if (-not [string]::IsNullOrWhiteSpace($evidenceDirectory)) {
        New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null
    }
}

$hashPath = "$BackupPath.sha256"
if (-not (Test-Path -LiteralPath $hashPath)) {
    throw "Checksum file not found: $hashPath"
}
$backupHashLine = (Get-Content -LiteralPath $hashPath -Raw).Trim()
$backupSha256 = ($backupHashLine -split '\s+', 2)[0]
if ($backupSha256 -notmatch '^[0-9a-fA-F]{64}$') {
    throw "Checksum file is not in sha256 format: $hashPath"
}

Invoke-NativeCommand "Drop previous restore verification database" {
    docker compose -f $ComposeFile exec -T $Service dropdb --username $User --if-exists $VerifyDatabase
}
Invoke-NativeCommand "Create restore verification database" {
    docker compose -f $ComposeFile exec -T $Service createdb --username $User $VerifyDatabase
}

try {
    $env:RESTORE_CONFIRM = $VerifyDatabase
    & "$PSScriptRoot\restore-postgres.ps1" `
        -BackupPath $BackupPath `
        -ComposeFile $ComposeFile `
        -Service $Service `
        -TargetDatabase $VerifyDatabase `
        -User $User `
        -Clean

    $tableChecks = @(
        Assert-RestoredCountMatchesSource "tenants" "select count(*) from tenants;" -RequireNonEmpty
        Assert-RestoredCountMatchesSource "user accounts" "select count(*) from user_accounts;" -RequireNonEmpty
        Assert-RestoredCountMatchesSource "companies" "select count(*) from companies;"
        Assert-RestoredCountMatchesSource "accounting periods" "select count(*) from accounting_periods;"
    )

    if (-not [string]::IsNullOrWhiteSpace($EvidencePath)) {
        $evidence = [pscustomobject]@{
            status = "passed"
            completedAtUtc = (Get-Date).ToUniversalTime().ToString("O")
            backupFileName = Split-Path -Leaf $BackupPath
            backupSha256 = $backupSha256.ToLowerInvariant()
            sourceDatabase = $SourceDatabase
            verifyDatabase = $VerifyDatabase
            composeFile = $ComposeFile
            service = $Service
            tableChecks = $tableChecks
        }
        $evidence | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 -Path $EvidencePath
        Write-Host "Restore evidence written: $EvidencePath"
    }
} finally {
    if (-not $KeepVerifyDatabase) {
        Invoke-NativeCommand "Drop restore verification database" {
            docker compose -f $ComposeFile exec -T $Service dropdb --username $User --if-exists $VerifyDatabase
        }
    }
}

Write-Host "Restore drill completed for $VerifyDatabase"
