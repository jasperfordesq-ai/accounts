param(
    [string]$ReportPath = "migration-upgrade-report.json",
    [string]$ConfigurationPath = "config/migration-gate.json",
    [string]$ToolManifestPath = ".config/dotnet-tools.json",
    [string]$EvidencePath = "migration-upgrade-verification-report.json",
    [string]$CommitSha = "",
    [string]$GitHubActionsRunUrl = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Add-Failure {
    param([System.Collections.Generic.List[string]]$Failures, [string]$Message)
    $Failures.Add($Message) | Out-Null
}

function Get-Property {
    param([object]$Object, [string]$Name)
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Assert-Passed {
    param([object]$Value, [string]$Context, [System.Collections.Generic.List[string]]$Failures)
    if ([string]$Value -ne "passed") { Add-Failure $Failures "$Context must be 'passed'." }
}

function Assert-CanonicalSha256 {
    param([object]$Value, [string]$Context, [System.Collections.Generic.List[string]]$Failures)
    if ([string]$Value -cnotmatch '^[0-9a-f]{64}$') { Add-Failure $Failures "$Context must be a lowercase SHA-256 digest." }
}

function Read-JsonFile {
    param([string]$Path, [string]$Description, [System.Collections.Generic.List[string]]$Failures)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        Add-Failure $Failures "Missing ${Description}: $Path"
        return $null
    }
    try {
        return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    } catch {
        Add-Failure $Failures "$Description is not valid JSON: $Path"
        return $null
    }
}

$failures = [System.Collections.Generic.List[string]]::new()
$report = Read-JsonFile $ReportPath "migration upgrade report" $failures
$configuration = Read-JsonFile $ConfigurationPath "migration gate configuration" $failures
$toolManifest = Read-JsonFile $ToolManifestPath "pinned .NET tool manifest" $failures

$requiredPreservationChecks = @()
$requiredTables = @(
    "tenants", "user_accounts", "companies", "accounting_periods",
    "imported_transactions", "adjustments", "opening_balances",
    "cro_filing_packages", "revenue_filing_packages",
    "audit_logs", "audit_integrity_checkpoints"
)

if ($null -ne $configuration) {
    if ([int](Get-Property $configuration "formatVersion") -ne 1) {
        Add-Failure $failures "migration-gate.json formatVersion must be 1."
    }
    $requiredPreservationChecks = @((Get-Property $configuration "requiredPreservationChecks"))
    foreach ($required in @("migration-upgrade-report.json", "migration-upgrade-verification-report.json", "restore-drill-report.json")) {
        if (-not (@((Get-Property $configuration "requiredEvidenceFiles")) -contains $required)) {
            Add-Failure $failures "migration-gate.json requiredEvidenceFiles must include '$required'."
        }
    }
}

if ($null -ne $configuration -and $null -ne $toolManifest) {
    $dotnetEf = Get-Property (Get-Property $toolManifest "tools") "dotnet-ef"
    if ([string](Get-Property $dotnetEf "version") -cne [string](Get-Property $configuration "entityFrameworkToolVersion")) {
        Add-Failure $failures ".config/dotnet-tools.json dotnet-ef version must match migration-gate.json."
    }
    if (-not (@((Get-Property $dotnetEf "commands")) -contains "dotnet-ef")) {
        Add-Failure $failures ".config/dotnet-tools.json must expose the dotnet-ef command."
    }
}

if ($null -ne $report -and $null -ne $configuration) {
    if ([int](Get-Property $report "formatVersion") -ne 1) { Add-Failure $failures "formatVersion must be 1." }
    Assert-Passed (Get-Property $report "status") "status" $failures
    if (@((Get-Property $report "failures")).Count -ne 0) { Add-Failure $failures "failures must be empty." }

    $generatedAt = [string](Get-Property $report "generatedAtUtc")
    $parsedGeneratedAt = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse($generatedAt, [ref]$parsedGeneratedAt) -or $parsedGeneratedAt.Offset -ne [TimeSpan]::Zero) {
        Add-Failure $failures "generatedAtUtc must be a valid UTC timestamp."
    }

    $identity = Get-Property $report "releaseCandidate"
    $actualCommitSha = [string](Get-Property $identity "commitSha")
    $actualRunUrl = [string](Get-Property $identity "gitHubActionsRunUrl")
    if (-not [string]::IsNullOrWhiteSpace($CommitSha) -and $actualCommitSha -cne $CommitSha) {
        Add-Failure $failures "releaseCandidate.commitSha must match -CommitSha."
    }
    if (-not [string]::IsNullOrWhiteSpace($GitHubActionsRunUrl) -and $actualRunUrl -cne $GitHubActionsRunUrl) {
        Add-Failure $failures "releaseCandidate.gitHubActionsRunUrl must match -GitHubActionsRunUrl."
    }
    if ($actualCommitSha -cnotmatch '^[0-9a-f]{40}$') { Add-Failure $failures "releaseCandidate.commitSha must be a lowercase 40-character Git SHA." }
    if ($actualRunUrl -notmatch '^https://github\.com/[^/]+/[^/]+/actions/runs/[0-9]+$') { Add-Failure $failures "releaseCandidate.gitHubActionsRunUrl must be a canonical GitHub Actions run URL." }

    $toolchain = Get-Property $report "toolchain"
    foreach ($field in @("dotnetSdkVersion", "entityFrameworkToolVersion", "entityFrameworkPackageVersion", "npgsqlProviderVersion")) {
        if ([string](Get-Property $toolchain $field) -cne [string](Get-Property $configuration $field)) {
            Add-Failure $failures "toolchain.$field must match migration-gate.json."
        }
    }

    $database = Get-Property $report "database"
    if ([string](Get-Property $database "engine") -cne [string](Get-Property $configuration "databaseEngine")) { Add-Failure $failures "database.engine must match migration-gate.json." }
    if ([string](Get-Property $database "previousReleaseMigration") -cne [string](Get-Property $configuration "previousReleaseMigration")) { Add-Failure $failures "database.previousReleaseMigration must match the supported upgrade floor." }
    if ([string](Get-Property $database "supportedUpgradeFloorBasis") -cne [string](Get-Property $configuration "supportedUpgradeFloorBasis")) { Add-Failure $failures "database.supportedUpgradeFloorBasis must match migration-gate.json." }
    if ([string]::IsNullOrWhiteSpace([string](Get-Property $database "latestMigration"))) { Add-Failure $failures "database.latestMigration must be present." }
    if ([int](Get-Property $database "migrationCount") -le 0) { Add-Failure $failures "database.migrationCount must be positive." }
    if ([string](Get-Property $database "serverVersion") -notmatch '^16\.4(?:\.|$)') { Add-Failure $failures "database.serverVersion must prove PostgreSQL 16.4." }

    $fresh = Get-Property $report "freshDatabase"
    Assert-Passed (Get-Property $fresh "status") "freshDatabase.status" $failures
    if ([int](Get-Property $fresh "appliedMigrationCount") -ne [int](Get-Property $database "migrationCount")) { Add-Failure $failures "freshDatabase.appliedMigrationCount must equal database.migrationCount." }
    if ([int](Get-Property $fresh "pendingMigrationCount") -ne 0) { Add-Failure $failures "freshDatabase.pendingMigrationCount must be zero." }
    if ([int](Get-Property $fresh "tableCount") -lt $requiredTables.Count) { Add-Failure $failures "freshDatabase.tableCount is below the required table count." }
    $actualTables = @((Get-Property $fresh "requiredTables"))
    foreach ($table in $requiredTables) {
        if (-not ($actualTables -contains $table)) { Add-Failure $failures "freshDatabase.requiredTables must include '$table'." }
    }

    $upgrade = Get-Property $report "previousReleaseUpgrade"
    Assert-Passed (Get-Property $upgrade "status") "previousReleaseUpgrade.status" $failures
    if ([string](Get-Property $upgrade "sourceMigration") -cne [string](Get-Property $configuration "previousReleaseMigration")) { Add-Failure $failures "previousReleaseUpgrade.sourceMigration must match the supported upgrade floor." }
    if ([string](Get-Property $upgrade "targetMigration") -cne [string](Get-Property $database "latestMigration")) { Add-Failure $failures "previousReleaseUpgrade.targetMigration must match database.latestMigration." }
    if ([int](Get-Property $upgrade "sourceMigrationCount") -ge [int](Get-Property $upgrade "targetMigrationCount")) { Add-Failure $failures "Previous-release migration count must be lower than the target migration count." }
    if ([int](Get-Property $upgrade "targetMigrationCount") -ne [int](Get-Property $database "migrationCount")) { Add-Failure $failures "previousReleaseUpgrade.targetMigrationCount must equal database.migrationCount." }
    if ((Get-Property $upgrade "auditChainCryptographicallyValid") -ne $true) { Add-Failure $failures "previousReleaseUpgrade.auditChainCryptographicallyValid must be true." }

    $preservationChecks = @((Get-Property $upgrade "preservationChecks"))
    foreach ($requiredName in $requiredPreservationChecks) {
        $matches = @($preservationChecks | Where-Object { [string](Get-Property $_ "name") -ceq [string]$requiredName })
        if ($matches.Count -ne 1) {
            Add-Failure $failures "previousReleaseUpgrade.preservationChecks must contain exactly one '$requiredName' row."
            continue
        }
        $check = $matches[0]
        Assert-Passed (Get-Property $check "status") "preservationChecks[$requiredName].status" $failures
        $beforeCount = [int](Get-Property $check "beforeRowCount")
        $afterCount = [int](Get-Property $check "afterRowCount")
        if ($beforeCount -le 0 -or $afterCount -ne $beforeCount) { Add-Failure $failures "preservationChecks[$requiredName] must retain a positive identical row count." }
        $beforeSha = Get-Property $check "beforeSha256"
        $afterSha = Get-Property $check "afterSha256"
        Assert-CanonicalSha256 $beforeSha "preservationChecks[$requiredName].beforeSha256" $failures
        Assert-CanonicalSha256 $afterSha "preservationChecks[$requiredName].afterSha256" $failures
        if ([string]$beforeSha -cne [string]$afterSha) { Add-Failure $failures "preservationChecks[$requiredName] fingerprints must match before and after upgrade." }
    }

    $rollback = Get-Property $report "failureRollback"
    Assert-Passed (Get-Property $rollback "status") "failureRollback.status" $failures
    foreach ($field in @("failureObserved", "partialSchemaAbsent", "dataPreserved", "migrationHistoryPreserved")) {
        if ((Get-Property $rollback $field) -ne $true) { Add-Failure $failures "failureRollback.$field must be true." }
    }
    if ([string](Get-Property $rollback "sqlState") -cne "P0001") { Add-Failure $failures "failureRollback.sqlState must be P0001." }
    if ([int](Get-Property $rollback "transactionSuppressedSqlOperationCount") -ne 0) { Add-Failure $failures "failureRollback.transactionSuppressedSqlOperationCount must be zero." }
    if ([string](Get-Property $rollback "recoveryMode") -notmatch 'transactional') { Add-Failure $failures "failureRollback.recoveryMode must document transactional recovery." }

    $recovery = Get-Property $report "encryptedRecoveryIntegration"
    if ([string](Get-Property $recovery "requiredCompanionReport") -cne "restore-drill-report.json") { Add-Failure $failures "encryptedRecoveryIntegration.requiredCompanionReport must be restore-drill-report.json." }
    if ((Get-Property $recovery "requiredInSameReleasePack") -ne $true) { Add-Failure $failures "encryptedRecoveryIntegration.requiredInSameReleasePack must be true." }
}

$sourceManifest = @()
foreach ($source in @($ReportPath, $ConfigurationPath, $ToolManifestPath)) {
    if (Test-Path -LiteralPath $source -PathType Leaf) {
        $resolved = (Resolve-Path -LiteralPath $source).Path
        $item = Get-Item -LiteralPath $resolved
        $sourceManifest += [pscustomobject]@{
            fileName = $item.Name
            byteSize = $item.Length
            sha256 = (Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
}

$verification = [ordered]@{
    formatVersion = 1
    status = if ($failures.Count -eq 0) { "passed" } else { "failed" }
    generatedAtUtc = [DateTime]::UtcNow.ToString("o")
    releaseCandidate = [ordered]@{
        commitSha = if ($null -ne $report) { Get-Property (Get-Property $report "releaseCandidate") "commitSha" } else { $null }
        gitHubActionsRunUrl = if ($null -ne $report) { Get-Property (Get-Property $report "releaseCandidate") "gitHubActionsRunUrl" } else { $null }
    }
    previousReleaseMigration = if ($null -ne $configuration) { Get-Property $configuration "previousReleaseMigration" } else { $null }
    requiredPreservationChecks = $requiredPreservationChecks
    encryptedRecoveryCompanionReport = "restore-drill-report.json"
    sourceFiles = $sourceManifest
    failureCount = $failures.Count
    failures = @($failures)
}

$resolvedEvidencePath = [System.IO.Path]::GetFullPath($EvidencePath)
$evidenceDirectory = Split-Path -Parent $resolvedEvidencePath
if (-not [string]::IsNullOrWhiteSpace($evidenceDirectory)) { New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null }
$verification | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $resolvedEvidencePath -Encoding utf8

if ($failures.Count -gt 0) {
    Write-Error ("Migration upgrade evidence verification failed:`n - " + ($failures -join "`n - "))
    exit 1
}

Write-Host "Migration upgrade evidence verification passed: $resolvedEvidencePath"
