param(
    [string]$ComposeFile = "compose.production.yml",
    [string]$Service = "db",
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,
    [string]$Database = $env:POSTGRES_DB,
    [string]$User = $env:POSTGRES_USER,
    [string]$EncryptionCertificateFile = $env:BACKUP_ENCRYPTION_CERTIFICATE_FILE,
    [string]$ReleaseCandidate = $env:GITHUB_SHA,
    [string]$EnvironmentName = "production",
    [switch]$AllowUnencryptedBackupForLocalDryRun,
    [switch]$AllowRepositoryOutputForLocalDryRun
)

$ErrorActionPreference = "Stop"

function Convert-ToFullPath([string]$Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $Path))
}

function Test-IsWithinPath([string]$CandidatePath, [string]$ParentPath) {
    $directorySeparator = [System.IO.Path]::DirectorySeparatorChar
    $alternateSeparator = [System.IO.Path]::AltDirectorySeparatorChar
    $normalizedCandidate = [System.IO.Path]::GetFullPath($CandidatePath).TrimEnd($directorySeparator, $alternateSeparator)
    $normalizedParent = [System.IO.Path]::GetFullPath($ParentPath).TrimEnd($directorySeparator, $alternateSeparator)

    if ($normalizedCandidate.Equals($normalizedParent, [StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    return $normalizedCandidate.StartsWith("$normalizedParent$directorySeparator", [StringComparison]::OrdinalIgnoreCase)
}

function Resolve-ReparsePointTarget([System.IO.FileSystemInfo]$Item) {
    $resolved = [System.IO.Path]::GetFullPath($Item.FullName)
    for ($depth = 0; $depth -lt 16; $depth++) {
        if (-not (Test-Path -LiteralPath $resolved)) {
            return $resolved
        }

        $currentItem = Get-Item -LiteralPath $resolved -Force
        if (($currentItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -eq 0) {
            return [System.IO.Path]::GetFullPath($currentItem.FullName)
        }

        $target = @($currentItem.Target) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1
        if ([string]::IsNullOrWhiteSpace($target)) {
            return [System.IO.Path]::GetFullPath($currentItem.FullName)
        }

        if (-not [System.IO.Path]::IsPathRooted($target)) {
            $target = Join-Path (Split-Path -Parent $currentItem.FullName) $target
        }

        $nextResolved = [System.IO.Path]::GetFullPath($target)
        if ($nextResolved.Equals($resolved, [StringComparison]::OrdinalIgnoreCase)) {
            return $resolved
        }

        $resolved = $nextResolved
    }

    throw "Too many nested filesystem links while resolving backup output path."
}

function Resolve-PathForContainment([string]$Path) {
    $fullPath = Convert-ToFullPath $Path
    $root = [System.IO.Path]::GetPathRoot($fullPath)
    if ([string]::IsNullOrWhiteSpace($root)) {
        return $fullPath
    }

    $segments = $fullPath.Substring($root.Length).Split(
        [char[]]@([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar),
        [System.StringSplitOptions]::RemoveEmptyEntries)

    $resolved = $root
    foreach ($segment in $segments) {
        $candidate = Join-Path $resolved $segment
        if (Test-Path -LiteralPath $candidate) {
            $item = Get-Item -LiteralPath $candidate -Force
            $resolved = Resolve-ReparsePointTarget $item
        } else {
            $resolved = $candidate
        }
    }

    return [System.IO.Path]::GetFullPath($resolved)
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

function Remove-PrivateBackupStagingDirectory([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) {
        return
    }

    $resolvedStagingDirectory = [IO.Path]::GetFullPath($Path)
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $comparison = if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
        [StringComparison]::OrdinalIgnoreCase
    } else {
        [StringComparison]::Ordinal
    }
    if (-not $resolvedStagingDirectory.StartsWith($temporaryRoot, $comparison)) {
        throw "Refusing to remove a backup staging path outside the operating-system temporary directory."
    }

    if (Test-Path -LiteralPath $resolvedStagingDirectory) {
        Remove-Item -LiteralPath $resolvedStagingDirectory -Recurse -Force -ErrorAction Stop
    }
    if (Test-Path -LiteralPath $resolvedStagingDirectory) {
        throw "Private plaintext backup staging directory could not be removed: $resolvedStagingDirectory"
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

    throw "OpenSSL is required to encrypt production backups."
}

if ([string]::IsNullOrWhiteSpace($Database)) {
    throw "POSTGRES_DB or -Database is required."
}
if ([string]::IsNullOrWhiteSpace($User)) {
    throw "POSTGRES_USER or -User is required."
}
if ($ReleaseCandidate -cnotmatch '^[0-9a-f]{40}$') {
    throw "ReleaseCandidate must be a full lowercase 40-character hexadecimal Git commit SHA."
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    throw "-OutputDirectory is required and must point to an off-repository backup location."
}
if ([string]::IsNullOrWhiteSpace($EncryptionCertificateFile) -and -not $AllowUnencryptedBackupForLocalDryRun) {
    throw "BACKUP_ENCRYPTION_CERTIFICATE_FILE or -EncryptionCertificateFile is required. Production backups cannot be written in plaintext."
}
if (-not [string]::IsNullOrWhiteSpace($EncryptionCertificateFile) -and -not (Test-Path -LiteralPath $EncryptionCertificateFile -PathType Leaf)) {
    throw "Backup encryption certificate was not found: $EncryptionCertificateFile"
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$outputDirectoryFullPath = Convert-ToFullPath $OutputDirectory
$repositoryContainmentPath = Resolve-PathForContainment $repositoryRoot
$outputContainmentPath = Resolve-PathForContainment $outputDirectoryFullPath
if ((Test-IsWithinPath -CandidatePath $outputContainmentPath -ParentPath $repositoryContainmentPath) -and -not $AllowRepositoryOutputForLocalDryRun) {
    throw "OutputDirectory must be outside the repository. Pass -AllowRepositoryOutputForLocalDryRun only for local dry runs."
}

New-Item -ItemType Directory -Force -Path $outputDirectoryFullPath | Out-Null

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$safeDatabase = $Database -replace "[^A-Za-z0-9_.-]", "_"
$backupFileName = "$safeDatabase-$timestamp.dump"
$temporaryStagingDirectory = Join-Path ([IO.Path]::GetTempPath()) ("accounts-backup-staging-" + [Guid]::NewGuid().ToString("N"))
$backupPath = Join-Path $temporaryStagingDirectory $backupFileName
$plaintextOutputPath = Join-Path $outputDirectoryFullPath $backupFileName
$containerBackupPath = "/var/lib/postgresql/data/.accounts-backup-$safeDatabase-$timestamp.dump"
$outputPath = $plaintextOutputPath
$encrypted = $false
$encryptionAlgorithm = "none"
$encryptionCertificateSha256 = ""
$plaintextDumpRetained = $false

try {
    New-Item -ItemType Directory -Path $temporaryStagingDirectory | Out-Null
    Set-UnixFileMode $temporaryStagingDirectory "700" "Restrict plaintext-backup staging directory permissions"
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        New-Item -ItemType File -Path $backupPath | Out-Null
        Set-UnixFileMode $backupPath "600" "Restrict plaintext-backup staging file permissions"
    }

    try {
        Invoke-NativeCommand "Create PostgreSQL backup inside container" {
            docker compose -f $ComposeFile exec -T $Service pg_dump `
                --username $User `
                --dbname $Database `
                --format=custom `
                --no-owner `
                --no-acl `
                --file $containerBackupPath
        }

        Invoke-NativeCommand "Copy PostgreSQL backup out of container" {
            docker compose -f $ComposeFile cp "${Service}:$containerBackupPath" $backupPath
        }
        Set-UnixFileMode $backupPath "600" "Reassert plaintext-backup staging file permissions after container copy"
        if (-not (Test-Path -LiteralPath $backupPath -PathType Leaf) -or (Get-Item -LiteralPath $backupPath).Length -le 0) {
            throw "Copied PostgreSQL backup is missing or empty."
        }
    } finally {
        Invoke-NativeCommand "Remove temporary PostgreSQL backup from container" {
            docker compose -f $ComposeFile exec -T $Service rm -f $containerBackupPath
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($EncryptionCertificateFile)) {
        $openssl = Resolve-OpenSsl
        $outputPath = "$plaintextOutputPath.cms"
        try {
            Invoke-NativeCommand "Encrypt PostgreSQL backup with the release backup certificate" {
                & $openssl cms -encrypt -binary -aes-256-cbc `
                    -in $backupPath `
                    -outform DER `
                    -out $outputPath `
                    $EncryptionCertificateFile
            }
            $encrypted = $true
            $encryptionAlgorithm = "CMS/AES-256-CBC"
            $encryptionCertificateSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $EncryptionCertificateFile).Hash.ToLowerInvariant()
        } catch {
            if (Test-Path -LiteralPath $outputPath -PathType Leaf) {
                Remove-Item -LiteralPath $outputPath -Force -ErrorAction Stop
            }
            throw
        }
    } else {
        Move-Item -LiteralPath $backupPath -Destination $plaintextOutputPath -Force
        Set-UnixFileMode $plaintextOutputPath "600" "Restrict local plaintext-backup output permissions"
        $plaintextDumpRetained = $true
    }
} finally {
    Remove-PrivateBackupStagingDirectory $temporaryStagingDirectory
}

if (-not (Test-Path -LiteralPath $outputPath -PathType Leaf)) {
    throw "Backup output was not created: $outputPath"
}

$hashPath = "$outputPath.sha256"
$manifestPath = "$outputPath.manifest.json"
$hash = Get-FileHash -Algorithm SHA256 -LiteralPath $outputPath
$backupSha256 = $hash.Hash.ToLowerInvariant()
"$backupSha256  $(Split-Path -Leaf $outputPath)" | Set-Content -NoNewline -Encoding ascii -Path $hashPath

[ordered]@{
    formatVersion = 1
    status = "created"
    createdAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    database = $Database
    environment = $EnvironmentName
    releaseCandidate = $ReleaseCandidate
    backupFileName = Split-Path -Leaf $outputPath
    backupSha256 = $backupSha256
    byteSize = (Get-Item -LiteralPath $outputPath).Length
    encrypted = $encrypted
    encryptionAlgorithm = $encryptionAlgorithm
    encryptionCertificateFileSha256 = $encryptionCertificateSha256
    plaintextDumpRetained = $plaintextDumpRetained
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Write-Host "Backup written: $outputPath"
Write-Host "sha256 written: $hashPath"
Write-Host "manifest written: $manifestPath"
