param(
    [Parameter(Mandatory = $true)]
    [string]$BackupPath,
    [string]$ComposeFile = "compose.production.yml",
    [string]$Service = "db",
    [string]$TargetDatabase = $env:POSTGRES_DB,
    [string]$User = $env:POSTGRES_USER,
    [switch]$Clean,
    [switch]$AllowUnverifiedBackupRestore
)

$ErrorActionPreference = "Stop"

function Assert-SafePostgresIdentifier([string]$Value, [string]$Name) {
    if ($Value -notmatch '^[A-Za-z_][A-Za-z0-9_]{0,62}$') {
        throw "$Name must be a PostgreSQL identifier using letters, numbers, and underscores."
    }
}

function Assert-SafeBackupLeafName([string]$Leaf) {
    if ($Leaf -notmatch '^[A-Za-z0-9_.-]+\.dump$') {
        throw "Backup filename must be a .dump file using only letters, numbers, dots, dashes, and underscores."
    }
}

function Assert-BackupHashMatches([string]$Path, [switch]$AllowUnverified) {
    $hashPath = "$Path.sha256"
    if (-not (Test-Path -LiteralPath $hashPath)) {
        if ($AllowUnverified) {
            Write-Warning "No checksum file found for $Path. Continuing because -AllowUnverifiedBackupRestore was supplied."
            return
        }

        throw "Checksum file not found: $hashPath. Restore refused; pass -AllowUnverifiedBackupRestore only for a documented break-glass restore."
    }

    $line = (Get-Content -LiteralPath $hashPath -TotalCount 1 -ErrorAction Stop).Trim()
    if ($line -notmatch '^(?<hash>[A-Fa-f0-9]{64})(\s+\*?(?<file>.+))?$') {
        throw "Checksum file is not in sha256 format: $hashPath"
    }

    $expectedHash = $Matches['hash'].ToLowerInvariant()
    $expectedFile = $Matches['file']
    if (-not [string]::IsNullOrWhiteSpace($expectedFile) -and $expectedFile.Trim() -ne (Split-Path -Leaf $Path)) {
        throw "Checksum file references '$($expectedFile.Trim())', not backup '$(Split-Path -Leaf $Path)'."
    }

    $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
    if ($actualHash -ne $expectedHash) {
        throw "Backup checksum mismatch for $Path."
    }
}

function Invoke-NativeCommand([string]$Description, [scriptblock]$Command) {
    & $Command
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "Native command failed ($Description) with exit code $exitCode."
    }
}

if ([string]::IsNullOrWhiteSpace($TargetDatabase)) {
    throw "POSTGRES_DB or -TargetDatabase is required."
}
if ([string]::IsNullOrWhiteSpace($User)) {
    throw "POSTGRES_USER or -User is required."
}
if (-not (Test-Path -LiteralPath $BackupPath)) {
    throw "Backup file not found: $BackupPath"
}
if ($env:RESTORE_CONFIRM -ne $TargetDatabase) {
    throw "Set RESTORE_CONFIRM=$TargetDatabase before restoring to this database."
}

$leaf = Split-Path -Leaf $BackupPath
Assert-SafePostgresIdentifier $TargetDatabase "TargetDatabase"
Assert-SafePostgresIdentifier $User "User"
Assert-SafeBackupLeafName $leaf
Assert-BackupHashMatches $BackupPath -AllowUnverified:$AllowUnverifiedBackupRestore

$containerPath = "/tmp/$leaf"

try {
    Invoke-NativeCommand "Copy PostgreSQL backup into container" {
        docker compose -f $ComposeFile cp $BackupPath "${Service}:$containerPath"
    }

    $restoreArgs = @(
        "compose", "-f", $ComposeFile,
        "exec", "-T", $Service,
        "pg_restore",
        "--username", $User,
        "--dbname", $TargetDatabase,
        "--single-transaction",
        "--exit-on-error",
        "--no-owner",
        "--no-acl"
    )
    if ($Clean) {
        $restoreArgs += @("--clean", "--if-exists")
    }
    $restoreArgs += $containerPath
    Invoke-NativeCommand "Restore PostgreSQL backup" {
        & docker @restoreArgs
    }
} finally {
    Invoke-NativeCommand "Remove temporary PostgreSQL backup from container" {
        docker compose -f $ComposeFile exec -T $Service rm -f "$containerPath"
    }
}

Write-Host "Restore completed into database: $TargetDatabase"
