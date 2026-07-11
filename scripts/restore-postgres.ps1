param(
    [Parameter(Mandatory = $true)]
    [string]$BackupPath,
    [string]$ComposeFile = "compose.production.yml",
    [string]$Service = "db",
    [string]$TargetDatabase = $env:POSTGRES_DB,
    [string]$User = $env:POSTGRES_USER,
    [string]$DecryptionCertificateFile = $env:BACKUP_DECRYPTION_CERTIFICATE_FILE,
    [string]$DecryptionPrivateKeyFile = $env:BACKUP_DECRYPTION_PRIVATE_KEY_FILE,
    [switch]$Clean,
    [switch]$AllowUnverifiedBackupRestore,
    [switch]$AllowUnencryptedBackupRestore
)

$ErrorActionPreference = "Stop"

function Assert-SafePostgresIdentifier([string]$Value, [string]$Name) {
    if ($Value -notmatch '^[A-Za-z_][A-Za-z0-9_]{0,62}$') {
        throw "$Name must be a PostgreSQL identifier using letters, numbers, and underscores."
    }
}

function Assert-SafeBackupLeafName([string]$Leaf) {
    if ($Leaf -notmatch '^[A-Za-z0-9_.-]+\.dump(?:\.cms)?$') {
        throw "Backup filename must be a .dump or .dump.cms file using only letters, numbers, dots, dashes, and underscores."
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

function Set-UnixFileMode([string]$Path, [string]$Mode, [string]$Description) {
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
        return
    }

    Invoke-NativeCommand $Description {
        & chmod $Mode -- $Path
    }
    $expectedMode = [Convert]::ToInt32($Mode, 8)
    $actualMode = [int][IO.File]::GetUnixFileMode($Path)
    if ($actualMode -ne $expectedMode) {
        throw "$Description did not produce Unix mode $Mode on $Path."
    }
}

function Remove-TemporaryDecryptDirectory([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) {
        return
    }

    $resolvedTemporaryDirectory = [IO.Path]::GetFullPath($Path)
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $comparison = if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
        [StringComparison]::OrdinalIgnoreCase
    } else {
        [StringComparison]::Ordinal
    }
    if (-not $resolvedTemporaryDirectory.StartsWith($temporaryRoot, $comparison)) {
        throw "Refusing to remove a decrypted-backup path outside the operating-system temporary directory."
    }

    if (Test-Path -LiteralPath $resolvedTemporaryDirectory) {
        Remove-Item -LiteralPath $resolvedTemporaryDirectory -Recurse -Force -ErrorAction Stop
    }
    if (Test-Path -LiteralPath $resolvedTemporaryDirectory) {
        throw "Decrypted-backup temporary directory could not be removed: $resolvedTemporaryDirectory"
    }
}

function Resolve-OpenSsl {
    $command = Get-Command openssl -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    foreach ($candidate in @(
        "C:\Program Files\Git\usr\bin\openssl.exe",
        "C:\Program Files\Git\mingw64\bin\openssl.exe")) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return $candidate
        }
    }

    throw "OpenSSL is required to decrypt production backups."
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

$encrypted = $leaf.EndsWith(".cms", [StringComparison]::OrdinalIgnoreCase)
$restoreSourcePath = [IO.Path]::GetFullPath($BackupPath)
$temporaryDecryptDirectory = ""
$manifestPath = "$BackupPath.manifest.json"
if ($encrypted) {
    foreach ($requiredFile in @($DecryptionCertificateFile, $DecryptionPrivateKeyFile, $manifestPath)) {
        if ([string]::IsNullOrWhiteSpace($requiredFile) -or -not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
            throw "Encrypted backup restore requires the decryption certificate, private key, and adjacent manifest: $requiredFile"
        }
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $backupSha256 = (Get-FileHash -LiteralPath $BackupPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $certificateSha256 = (Get-FileHash -LiteralPath $DecryptionCertificateFile -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($manifest.encrypted -ne $true -or [string]$manifest.encryptionAlgorithm -ne "CMS/AES-256-CBC") {
        throw "Backup manifest does not identify the expected CMS/AES-256-CBC encrypted format."
    }
    if ([string]$manifest.backupSha256 -ne $backupSha256) {
        throw "Backup manifest SHA-256 does not match the encrypted backup."
    }
    if ([string]$manifest.encryptionCertificateFileSha256 -ne $certificateSha256) {
        throw "Backup manifest encryption certificate does not match the supplied decryption certificate."
    }

    try {
        $temporaryDecryptDirectory = Join-Path ([IO.Path]::GetTempPath()) ("accounts-backup-decrypt-" + [Guid]::NewGuid().ToString("N"))
        New-Item -ItemType Directory -Path $temporaryDecryptDirectory | Out-Null
        Set-UnixFileMode $temporaryDecryptDirectory "700" "Restrict decrypted-backup temporary directory permissions"

        $restoreSourcePath = Join-Path $temporaryDecryptDirectory ([IO.Path]::GetFileNameWithoutExtension($leaf))
        if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
            New-Item -ItemType File -Path $restoreSourcePath | Out-Null
            Set-UnixFileMode $restoreSourcePath "600" "Restrict decrypted-backup temporary file permissions"
        }

        $openssl = Resolve-OpenSsl
        Invoke-NativeCommand "Decrypt PostgreSQL backup into a temporary restore file" {
            & $openssl cms -decrypt -binary -inform DER `
                -in $BackupPath `
                -recip $DecryptionCertificateFile `
                -inkey $DecryptionPrivateKeyFile `
                -out $restoreSourcePath
        }
        Set-UnixFileMode $restoreSourcePath "600" "Reassert decrypted-backup temporary file permissions after OpenSSL"
        if (-not (Test-Path -LiteralPath $restoreSourcePath -PathType Leaf) -or (Get-Item -LiteralPath $restoreSourcePath).Length -le 0) {
            throw "Decrypted PostgreSQL backup is missing or empty."
        }
    } catch {
        Remove-TemporaryDecryptDirectory $temporaryDecryptDirectory
        throw
    }
} elseif (-not $AllowUnencryptedBackupRestore) {
    throw "Plaintext .dump restore is disabled. Supply an encrypted .dump.cms backup or use -AllowUnencryptedBackupRestore only for a documented local/break-glass operation."
}

$restoreLeaf = Split-Path -Leaf $restoreSourcePath
$containerPath = "/var/lib/postgresql/data/.accounts-restore-$restoreLeaf"
$containerCopyAttempted = $false

try {
    $containerCopyAttempted = $true
    Invoke-NativeCommand "Copy PostgreSQL backup into container" {
        docker compose -f $ComposeFile cp $restoreSourcePath "${Service}:$containerPath"
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
    try {
        if ($containerCopyAttempted) {
            Invoke-NativeCommand "Remove temporary PostgreSQL backup from container" {
                docker compose -f $ComposeFile exec -T $Service rm -f "$containerPath"
            }
        }
    } finally {
        Remove-TemporaryDecryptDirectory $temporaryDecryptDirectory
    }
}

Write-Host "Restore completed into database: $TargetDatabase"
