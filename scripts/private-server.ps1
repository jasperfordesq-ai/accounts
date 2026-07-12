[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Command = "help",

    [Parameter(Position = 1)]
    [string]$Action,

    [Alias("state-dir")]
    [string]$StateDirectory,

    [Alias("release-manifest")]
    [string]$ReleaseManifest,

    [Alias("tenant-name")]
    [string]$TenantName,

    [Alias("tenant-slug")]
    [string]$TenantSlug,

    [Alias("owner-email")]
    [string]$OwnerEmail,

    [Alias("owner-name")]
    [string]$OwnerName,

    [Alias("origin")]
    [string]$PublicOrigin,

    [ValidateRange(1024, 65535)]
    [int]$Port = 3500,

    [Alias("output-dir")]
    [string]$OutputDirectory,

    [Alias("backup-path")]
    [string]$BackupPath,

    [Alias("backup-recipient")]
    [string]$BackupRecipient,

    [Alias("age-identity")]
    [string]$AgeIdentityFile,

    [string]$Confirmation,

    [Alias("tail")]
    [ValidateRange(1, 5000)]
    [int]$TailLines = 250,

    [Alias("dry-run")]
    [switch]$DryRun,

    [Alias("non-interactive")]
    [switch]$NonInteractive,

    [Alias("build-local")]
    [switch]$BuildLocal,

    [Alias("plaintext-database-only")]
    [switch]$PlaintextDatabaseOnly,

    [Alias("allow-plaintext-database-only-restore")]
    [switch]$AllowPlaintextDatabaseOnlyRestore
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$modulePath = Join-Path $PSScriptRoot "PrivateServer\PrivateServer.psm1"
if (-not (Test-Path -LiteralPath $modulePath -PathType Leaf)) {
    throw "Private Server operator module was not found: $modulePath"
}

Import-Module $modulePath -Force

$arguments = @{
    Command                           = $Command
    Action                            = $Action
    StateDirectory                    = $StateDirectory
    RepositoryRoot                    = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
    ReleaseManifest                   = $ReleaseManifest
    TenantName                        = $TenantName
    TenantSlug                        = $TenantSlug
    OwnerEmail                        = $OwnerEmail
    OwnerName                         = $OwnerName
    PublicOrigin                      = $PublicOrigin
    Port                              = $Port
    OutputDirectory                   = $OutputDirectory
    BackupPath                        = $BackupPath
    BackupRecipient                   = $BackupRecipient
    AgeIdentityFile                   = $AgeIdentityFile
    Confirmation                      = $Confirmation
    TailLines                         = $TailLines
    DryRun                            = $DryRun
    NonInteractive                    = $NonInteractive
    BuildLocal                        = $BuildLocal
    PlaintextDatabaseOnly             = $PlaintextDatabaseOnly
    AllowPlaintextDatabaseOnlyRestore = $AllowPlaintextDatabaseOnlyRestore
}

try {
    Invoke-FilingBridgePrivateServer @arguments
} catch {
    Write-Error (Protect-PrivateServerText -Text $_.Exception.Message)
    exit 1
}
