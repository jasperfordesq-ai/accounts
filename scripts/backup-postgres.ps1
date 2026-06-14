param(
    [string]$ComposeFile = "compose.production.yml",
    [string]$Service = "db",
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,
    [string]$Database = $env:POSTGRES_DB,
    [string]$User = $env:POSTGRES_USER,
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

if ([string]::IsNullOrWhiteSpace($Database)) {
    throw "POSTGRES_DB or -Database is required."
}
if ([string]::IsNullOrWhiteSpace($User)) {
    throw "POSTGRES_USER or -User is required."
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    throw "-OutputDirectory is required and must point to an off-repository backup location."
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
$backupPath = Join-Path $outputDirectoryFullPath "$safeDatabase-$timestamp.dump"
$hashPath = "$backupPath.sha256"
$containerBackupPath = "/tmp/$safeDatabase-$timestamp.dump"

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
} finally {
    Invoke-NativeCommand "Remove temporary PostgreSQL backup from container" {
        docker compose -f $ComposeFile exec -T $Service rm -f $containerBackupPath
    }
}

$hash = Get-FileHash -Algorithm SHA256 -Path $backupPath
"$($hash.Hash.ToLowerInvariant())  $(Split-Path -Leaf $backupPath)" | Set-Content -NoNewline -Encoding ascii -Path $hashPath

Write-Host "Backup written: $backupPath"
Write-Host "sha256 written: $hashPath"
