param(
    [Parameter(Mandatory = $true)]
    [string]$BackupPath,
    [string]$ComposeFile = "compose.production.yml",
    [string]$Service = "db",
    [string]$User = $env:POSTGRES_USER,
    [string]$SourceDatabase = $env:POSTGRES_DB,
    [string]$VerifyDatabase = "accounts_restore_verify",
    [string]$DecryptionCertificateFile = $env:BACKUP_DECRYPTION_CERTIFICATE_FILE,
    [string]$DecryptionPrivateKeyFile = $env:BACKUP_DECRYPTION_PRIVATE_KEY_FILE,
    [string]$EvidencePath,
    [string]$ReleaseCandidate = $env:GITHUB_SHA,
    [string]$GitHubActionsRunUrl = $env:GITHUB_ACTIONS_RUN_URL,
    [ValidateRange(1, 604800)]
    [int]$RpoTargetSeconds = 86400,
    [ValidateRange(1, 604800)]
    [int]$RtoTargetSeconds = 14400,
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

function Invoke-TextScalarQuery([string]$Description, [string]$Database, [string]$Sql) {
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

    $values = @($output | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($values.Count -ne 1) {
        throw "Query '$Description' must return exactly one scalar value."
    }
    return $values[0].Trim()
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

function Assert-RestoredValueMatchesSource([string]$CheckName, [string]$Sql) {
    $sourceValue = Invoke-TextScalarQuery "Read source $CheckName" $SourceDatabase $Sql
    $restoredValue = Invoke-TextScalarQuery "Read restored $CheckName" $VerifyDatabase $Sql
    if ($restoredValue -ne $sourceValue) {
        throw "Restored '$CheckName' value '$restoredValue' did not match source '$sourceValue'."
    }

    return [pscustomobject]@{
        check = $CheckName
        sourceValue = $sourceValue
        restoredValue = $restoredValue
        matched = $true
    }
}

if ([string]::IsNullOrWhiteSpace($SourceDatabase)) {
    throw "POSTGRES_DB or -SourceDatabase is required."
}
if ([string]::IsNullOrWhiteSpace($User)) {
    throw "POSTGRES_USER or -User is required."
}
if ($ReleaseCandidate -cnotmatch '^[0-9a-f]{40}$') {
    throw "ReleaseCandidate must be a full lowercase 40-character hexadecimal Git commit SHA."
}
if ($GitHubActionsRunUrl -cnotmatch '^https://github\.com/[^/\s]+/[^/\s]+/actions/runs/[0-9]+$') {
    throw "GitHubActionsRunUrl must be an exact GitHub Actions run URL."
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
$backupFile = Get-Item -LiteralPath $BackupPath
$backupFileName = $backupFile.Name
$backupByteSize = [int64]$backupFile.Length
if ($backupByteSize -le 0) {
    throw "Backup file must be non-empty: $BackupPath"
}
$backupHashLine = [IO.File]::ReadAllText($hashPath)
$backupHashMatch = [regex]::Match($backupHashLine, '\A(?<hash>[0-9a-f]{64})  (?<file>[A-Za-z0-9_.-]+\.dump\.cms)\z')
if (-not $backupHashMatch.Success) {
    throw "Checksum file is not in sha256 format: $hashPath"
}
$backupSha256 = $backupHashMatch.Groups['hash'].Value
if ($backupHashMatch.Groups['file'].Value -cne $backupFileName) {
    throw "Checksum file must reference the exact retained backup file '$backupFileName'."
}
$actualBackupSha256 = (Get-FileHash -LiteralPath $BackupPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualBackupSha256 -cne $backupSha256) {
    throw "Backup SHA-256 does not match the checksum sidecar."
}
$backupChecksumFileName = (Get-Item -LiteralPath $hashPath).Name
$backupChecksumSha256 = (Get-FileHash -LiteralPath $hashPath -Algorithm SHA256).Hash.ToLowerInvariant()

$drillStartedAtUtc = [DateTimeOffset]::UtcNow
$encrypted = $BackupPath.EndsWith(".dump.cms", [StringComparison]::OrdinalIgnoreCase)
$manifestPath = "$BackupPath.manifest.json"
if (-not $encrypted) {
    throw "Restore assurance requires an encrypted .dump.cms backup retained outside the source database container."
}
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Encrypted backup manifest not found: $manifestPath"
}
$backupManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($backupManifest.encrypted -ne $true -or [string]$backupManifest.encryptionAlgorithm -cne "CMS/AES-256-CBC") {
    throw "Backup manifest must identify CMS/AES-256-CBC encryption."
}
if ([string]$backupManifest.backupSha256 -cne $backupSha256) {
    throw "Backup manifest SHA-256 does not match the checksum sidecar."
}
if ([string]$backupManifest.backupFileName -cne $backupFileName) {
    throw "Backup manifest file name does not match the retained encrypted backup."
}
if ([int64]$backupManifest.byteSize -ne $backupByteSize) {
    throw "Backup manifest byte size does not match the retained encrypted backup."
}
if ([string]$backupManifest.releaseCandidate -cne $ReleaseCandidate) {
    throw "Backup manifest release candidate does not match the restore drill candidate."
}
if ($backupManifest.plaintextDumpRetained -ne $false) {
    throw "Backup manifest must prove the plaintext dump was removed after encryption."
}
$backupCreatedAtUtc = [DateTimeOffset]::MinValue
if (-not [DateTimeOffset]::TryParse([string]$backupManifest.createdAtUtc, [ref]$backupCreatedAtUtc)) {
    throw "Backup manifest createdAtUtc is invalid."
}
if ($backupCreatedAtUtc.Offset -ne [TimeSpan]::Zero) {
    throw "Backup manifest createdAtUtc must use UTC."
}
if ($backupCreatedAtUtc -gt $drillStartedAtUtc) {
    throw "Backup manifest createdAtUtc cannot be later than the restore drill start time."
}
$backupManifestSha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()

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
        -DecryptionCertificateFile $DecryptionCertificateFile `
        -DecryptionPrivateKeyFile $DecryptionPrivateKeyFile `
        -Clean

    $tableChecks = @(
        Assert-RestoredCountMatchesSource "tenants" "select count(*) from tenants;" -RequireNonEmpty
        Assert-RestoredCountMatchesSource "user accounts" "select count(*) from user_accounts;" -RequireNonEmpty
        Assert-RestoredCountMatchesSource "user company access" "select count(*) from user_company_accesses;"
        Assert-RestoredCountMatchesSource "companies" "select count(*) from companies;"
        Assert-RestoredCountMatchesSource "accounting periods" "select count(*) from accounting_periods;"
        Assert-RestoredCountMatchesSource "bank accounts" "select count(*) from bank_accounts;"
        Assert-RestoredCountMatchesSource "import batches" "select count(*) from import_batches;"
        Assert-RestoredCountMatchesSource "imported transactions" "select count(*) from imported_transactions;"
        Assert-RestoredCountMatchesSource "adjustments" "select count(*) from adjustments;"
        Assert-RestoredCountMatchesSource "CRO filing packages" "select count(*) from cro_filing_packages;"
        Assert-RestoredCountMatchesSource "Revenue filing packages" "select count(*) from revenue_filing_packages;"
        Assert-RestoredCountMatchesSource "charity filing packages" "select count(*) from charity_filing_packages;"
        Assert-RestoredCountMatchesSource "filing histories" "select count(*) from filing_histories;"
        Assert-RestoredCountMatchesSource "audit logs" "select count(*) from audit_logs;"
        Assert-RestoredCountMatchesSource "audit integrity checkpoints" "select count(*) from audit_integrity_checkpoints;"
    )

    $schemaChecks = @(
        Assert-RestoredValueMatchesSource "EF migration identity" "select coalesce(max(`"MigrationId`"), '') from `"__EFMigrationsHistory`";"
    )
    $figureChecks = @(
        Assert-RestoredValueMatchesSource "imported transaction amount total" "select coalesce(sum(`"Amount`"), 0)::text from imported_transactions;"
        Assert-RestoredValueMatchesSource "adjustment amount total" "select coalesce(sum(`"Amount`"), 0)::text from adjustments;"
        Assert-RestoredValueMatchesSource "opening balance total" "select coalesce(sum(`"Amount`"), 0)::text from opening_balances;"
        Assert-RestoredValueMatchesSource "CRO retained artifact bytes" "select coalesce(sum(octet_length(`"AccountsPdfArtifact`")), 0)::text from cro_filing_packages;"
        Assert-RestoredValueMatchesSource "Revenue retained artifact bytes" "select coalesce(sum(octet_length(`"IxbrlArtifact`")), 0)::text from revenue_filing_packages;"
    )
    $fingerprintChecks = @(
        Assert-RestoredValueMatchesSource "companies full-row fingerprint" "select md5(coalesce(string_agg(to_jsonb(t)::text, '|' order by t.`"Id`"), '')) from companies t;"
        Assert-RestoredValueMatchesSource "periods full-row fingerprint" "select md5(coalesce(string_agg(to_jsonb(t)::text, '|' order by t.`"Id`"), '')) from accounting_periods t;"
        Assert-RestoredValueMatchesSource "transactions full-row fingerprint" "select md5(coalesce(string_agg(to_jsonb(t)::text, '|' order by t.`"Id`"), '')) from imported_transactions t;"
        Assert-RestoredValueMatchesSource "adjustments full-row fingerprint" "select md5(coalesce(string_agg(to_jsonb(t)::text, '|' order by t.`"Id`"), '')) from adjustments t;"
        Assert-RestoredValueMatchesSource "CRO packages full-row fingerprint" "select md5(coalesce(string_agg(to_jsonb(t)::text, '|' order by t.`"Id`"), '')) from cro_filing_packages t;"
        Assert-RestoredValueMatchesSource "Revenue packages full-row fingerprint" "select md5(coalesce(string_agg(to_jsonb(t)::text, '|' order by t.`"Id`"), '')) from revenue_filing_packages t;"
        Assert-RestoredValueMatchesSource "charity packages full-row fingerprint" "select md5(coalesce(string_agg(to_jsonb(t)::text, '|' order by t.`"Id`"), '')) from charity_filing_packages t;"
        Assert-RestoredValueMatchesSource "audit log full-row fingerprint" "select md5(coalesce(string_agg(to_jsonb(t)::text, '|' order by t.`"Id`"), '')) from audit_logs t;"
        Assert-RestoredValueMatchesSource "audit checkpoint full-row fingerprint" "select md5(coalesce(string_agg(to_jsonb(t)::text, '|' order by t.`"Id`"), '')) from audit_integrity_checkpoints t;"
    )
    $invalidAuditHashCount = Invoke-ScalarQuery "Count invalid source audit hashes" $SourceDatabase "select count(*) from audit_logs where `"IntegrityHash`" is null or length(`"IntegrityHash`") <> 64;"
    $invalidRestoredAuditHashCount = Invoke-ScalarQuery "Count invalid restored audit hashes" $VerifyDatabase "select count(*) from audit_logs where `"IntegrityHash`" is null or length(`"IntegrityHash`") <> 64;"
    if ($invalidAuditHashCount -ne 0 -or $invalidRestoredAuditHashCount -ne 0) {
        throw "Audit hash integrity check found unhashed or malformed audit rows."
    }
    $invalidCheckpointCount = Invoke-ScalarQuery "Count invalid source audit checkpoints" $SourceDatabase "select count(*) from audit_integrity_checkpoints where length(`"LastIntegrityHash`") <> 64 or coalesce(`"Signature`", '') = '' or coalesce(`"KeyId`", '') = '';"
    $invalidRestoredCheckpointCount = Invoke-ScalarQuery "Count invalid restored audit checkpoints" $VerifyDatabase "select count(*) from audit_integrity_checkpoints where length(`"LastIntegrityHash`") <> 64 or coalesce(`"Signature`", '') = '' or coalesce(`"KeyId`", '') = '';"
    if ($invalidCheckpointCount -ne 0 -or $invalidRestoredCheckpointCount -ne 0) {
        throw "Audit checkpoint integrity check found malformed checkpoint rows."
    }

    $drillCompletedAtUtc = [DateTimeOffset]::UtcNow
    $rtoSeconds = [Math]::Round(($drillCompletedAtUtc - $drillStartedAtUtc).TotalSeconds, 3)
    $rpoSecondsAtDrill = [Math]::Round(($drillStartedAtUtc - $backupCreatedAtUtc).TotalSeconds, 3)
    if ($rpoSecondsAtDrill -lt 0 -or $rtoSeconds -lt 0) {
        throw "Restore recovery measurements must be non-negative and use ordered UTC timestamps."
    }
    $rpoTargetMet = $rpoSecondsAtDrill -le $RpoTargetSeconds
    $rtoTargetMet = $rtoSeconds -le $RtoTargetSeconds

    if (-not [string]::IsNullOrWhiteSpace($EvidencePath)) {
        $evidence = [pscustomobject]@{
            status = if ($rpoTargetMet -and $rtoTargetMet) { "passed" } else { "failed" }
            completedAtUtc = $drillCompletedAtUtc.UtcDateTime.ToString("O")
            releaseCandidate = $ReleaseCandidate
            githubActionsRunUrl = $GitHubActionsRunUrl
            backupFileName = $backupFileName
            backupByteSize = $backupByteSize
            backupSha256 = $backupSha256
            backupChecksumFileName = $backupChecksumFileName
            backupChecksumSha256 = $backupChecksumSha256
            backupManifestFileName = Split-Path -Leaf $manifestPath
            backupManifestSha256 = $backupManifestSha256
            backupManifestReleaseCandidate = [string]$backupManifest.releaseCandidate
            backupEncryption = [ordered]@{
                encrypted = $true
                algorithm = "CMS/AES-256-CBC"
                encryptionCertificateFileSha256 = [string]$backupManifest.encryptionCertificateFileSha256
                plaintextDumpRetained = $false
                restoredFromEncryptedCopy = $true
            }
            sourceDatabase = $SourceDatabase
            verifyDatabase = $VerifyDatabase
            composeFile = $ComposeFile
            service = $Service
            tableChecks = $tableChecks
            schemaChecks = $schemaChecks
            figureChecks = $figureChecks
            fingerprintChecks = $fingerprintChecks
            auditIntegrityChecks = [ordered]@{
                sourceInvalidAuditHashCount = $invalidAuditHashCount
                restoredInvalidAuditHashCount = $invalidRestoredAuditHashCount
                sourceInvalidCheckpointCount = $invalidCheckpointCount
                restoredInvalidCheckpointCount = $invalidRestoredCheckpointCount
                passed = $true
            }
            recoveryMetrics = [ordered]@{
                backupCreatedAtUtc = $backupCreatedAtUtc.ToString("o")
                drillStartedAtUtc = $drillStartedAtUtc.ToString("o")
                drillCompletedAtUtc = $drillCompletedAtUtc.ToString("o")
                rpoSecondsAtDrill = $rpoSecondsAtDrill
                rtoSeconds = $rtoSeconds
                rpoTargetSeconds = $RpoTargetSeconds
                rtoTargetSeconds = $RtoTargetSeconds
                rpoTargetMet = $rpoTargetMet
                rtoTargetMet = $rtoTargetMet
            }
        }
        $evidence | ConvertTo-Json -Depth 7 | Set-Content -Encoding utf8 -Path $EvidencePath
        Write-Host "Restore evidence written: $EvidencePath"
    }

    $targetFailures = @()
    if (-not $rpoTargetMet) {
        $targetFailures += "RPO measured $rpoSecondsAtDrill seconds against a $RpoTargetSeconds-second target"
    }
    if (-not $rtoTargetMet) {
        $targetFailures += "RTO measured $rtoSeconds seconds against a $RtoTargetSeconds-second target"
    }
    if ($targetFailures.Count -gt 0) {
        throw "Restore recovery target failure: $($targetFailures -join '; ')."
    }
} finally {
    if (-not $KeepVerifyDatabase) {
        Invoke-NativeCommand "Drop restore verification database" {
            docker compose -f $ComposeFile exec -T $Service dropdb --username $User --if-exists $VerifyDatabase
        }
    }
}

Write-Host "Restore drill completed for $VerifyDatabase"
