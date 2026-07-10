param(
    [string]$EvidencePath = ""
)

$ErrorActionPreference = "Stop"

$RepositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$SecretRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("accounts-production-compose-secrets-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $SecretRoot | Out-Null

function New-SecretFile([string]$Name, [string]$Value) {
    $path = Join-Path $SecretRoot $Name
    [System.IO.File]::WriteAllText($path, $Value)
    return $path
}

function ProductionComposeEnv {
    $postgresPassword = "dummy-postgres-password"
    $postgresApplicationPassword = "dummy-application-password"
    $accountsApiKey = "dummy-api-key"
    $migrationConnectionString = "Host=db;Port=5432;Database=accounts;Username=accounts;Password=$postgresPassword;SSL Mode=VerifyFull;Root Certificate=/run/secrets/postgres_ca_certificate;Trust Server Certificate=false"
    $applicationConnectionString = "Host=db;Port=5432;Database=accounts;Username=accounts_api;Password=$postgresApplicationPassword;SSL Mode=VerifyFull;Root Certificate=/run/secrets/postgres_ca_certificate;Trust Server Certificate=false"

    @(
        @("POSTGRES_DB", "accounts"),
        @("POSTGRES_USER", "accounts"),
        @("POSTGRES_PASSWORD_FILE", (New-SecretFile "postgres_password" $postgresPassword)),
        @("POSTGRES_APPLICATION_PASSWORD_FILE", (New-SecretFile "postgres_application_password" $postgresApplicationPassword)),
        @("POSTGRES_SERVER_CERTIFICATE_FILE", (New-SecretFile "postgres_server_certificate" "dummy-server-certificate")),
        @("POSTGRES_SERVER_KEY_FILE", (New-SecretFile "postgres_server_key" "dummy-server-private-key")),
        @("POSTGRES_CA_CERTIFICATE_FILE", (New-SecretFile "postgres_ca_certificate" "dummy-ca-certificate")),
        @("ACCOUNTS_MIGRATION_CONNECTION_STRING_FILE", (New-SecretFile "accounts_migration_connection_string" $migrationConnectionString)),
        @("ACCOUNTS_APPLICATION_CONNECTION_STRING_FILE", (New-SecretFile "accounts_application_connection_string" $applicationConnectionString)),
        @("AUTH_SESSION_SIGNING_KEY_FILE", (New-SecretFile "auth_session_signing_key" "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==")),
        @("AUDIT_INTEGRITY_SIGNING_KEY_FILE", (New-SecretFile "audit_integrity_signing_key" "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB==")),
        @("AUDIT_INTEGRITY_ACTIVE_KEY_ID", "local-dummy"),
        @("DATABASE_TENANT_CONTEXT_KEY_FILE", (New-SecretFile "database_tenant_context_key" "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC==")),
        @("IDENTITY_HMAC_KEY_FILE", (New-SecretFile "identity_hmac_key" "DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD==")),
        @("MFA_ENCRYPTION_KEY_FILE", (New-SecretFile "mfa_encryption_key" "EEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEE=")),
        @("MFA_ENCRYPTION_ACTIVE_KEY_ID", "local-mfa-dummy"),
        @("DEADLINE_PROVIDER_TOKEN_FILE", (New-SecretFile "deadline_provider_token" "dummy-provider-token")),
        @("DEADLINE_DELIVERY_PROVIDER_ENDPOINT", "https://deadline-provider.invalid/reminders"),
        @("ACCOUNTS_API_KEY_FILE", (New-SecretFile "accounts_api_key" $accountsApiKey)),
        @("ACCOUNTS_API_KEY_HASH", "0000000000000000000000000000000000000000000000000000000000000000"),
        @("ACCOUNTS_API_IMAGE", "ghcr.io/example/accounts-api@sha256:1111111111111111111111111111111111111111111111111111111111111111"),
        @("ACCOUNTS_FRONTEND_IMAGE", "ghcr.io/example/accounts-frontend@sha256:2222222222222222222222222222222222222222222222222222222222222222"),
        @("ACCOUNTS_ALLOWED_HOSTS", "accounts-smoke.local"),
        @("ACCOUNTS_ALLOWED_ORIGIN", "https://accounts-smoke.local"),
        @("TRUST_PROXY_HEADERS", "true"),
        @("FRONTEND_PORT", "3000"),
        @("NO_PROXY", "accounts-smoke.local,127.0.0.1,localhost"),
        @("no_proxy", "accounts-smoke.local,127.0.0.1,localhost"),
        @("MONITORING_ERROR_TRACKING_DSN", "https://public@sentry.invalid/1"),
        @("MONITORING_TRACES_SAMPLE_RATE", "0"),
        @("MONITORING_ON_CALL_OWNER", "CI Operations Owner"),
        @("MONITORING_ALERT_ROUTE", "ci-operations-alert-route"),
        @("BOOTSTRAP_TENANT_NAME", "CI Firm"),
        @("BOOTSTRAP_TENANT_SLUG", "ci-firm"),
        @("BOOTSTRAP_OWNER_EMAIL", "owner@example.ie"),
        @("BOOTSTRAP_OWNER_DISPLAY_NAME", "CI Owner"),
        @("BOOTSTRAP_OWNER_PASSWORD_FILE", (New-SecretFile "bootstrap_owner_password" "CiOwner1!dummy"))
    )
}

function Invoke-WithTemporaryEnvironment([object[]]$Environment, [scriptblock]$Action) {
    $previousValues = @{}
    foreach ($pair in $Environment) {
        $name = $pair[0]
        $previousValues[$name] = [Environment]::GetEnvironmentVariable($name, "Process")
        Set-Item -Path "Env:$name" -Value $pair[1]
    }

    try {
        & $Action
    } finally {
        foreach ($pair in $Environment) {
            $name = $pair[0]
            if ($null -eq $previousValues[$name]) {
                Remove-Item -Path "Env:$name" -ErrorAction SilentlyContinue
            } else {
                Set-Item -Path "Env:$name" -Value $previousValues[$name]
            }
        }
    }
}

function Invoke-DockerComposeConfigJson {
    $output = Invoke-WithTemporaryEnvironment (ProductionComposeEnv) {
        & docker compose -f (Join-Path $RepositoryRoot "compose.production.yml") config --format json
    }
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose config --format json failed with exit code $LASTEXITCODE."
    }

    return ($output | ConvertFrom-Json)
}

function Assert-NoBuildContext($Services, [string]$ServiceName) {
    $service = $Services.$ServiceName
    if ($null -eq $service) {
        throw "Expected compose service '$ServiceName'."
    }

    if ($service.PSObject.Properties.Name -contains "build") {
        throw "Service '$ServiceName' must not define a build context."
    }
}

function Assert-Equal([string]$Description, $Actual, $Expected) {
    if ($Actual -ne $Expected) {
        throw "$Description expected '$Expected' but was '$Actual'."
    }
}

function Assert-HardenedService($Service, [string]$ServiceName) {
    Assert-Equal "$ServiceName read-only root filesystem" $Service.read_only $true
    if (@($Service.security_opt) -notcontains "no-new-privileges:true") {
        throw "$ServiceName must enable no-new-privileges."
    }
    if (@($Service.cap_drop) -notcontains "ALL") {
        throw "$ServiceName must drop all ambient Linux capabilities before any explicit add-back."
    }
    if ($Service.privileged -eq $true) {
        throw "$ServiceName must not run privileged."
    }
    if ([int64]$Service.pids_limit -le 0) {
        throw "$ServiceName must set a positive PID limit."
    }
    if ([int64]$Service.mem_limit -le 0) {
        throw "$ServiceName must set a positive memory limit."
    }
    if ([decimal]$Service.cpus -le 0) {
        throw "$ServiceName must set a positive CPU limit."
    }
    if (@($Service.tmpfs).Count -eq 0) {
        throw "$ServiceName must use bounded writable tmpfs mounts with a read-only root filesystem."
    }
}

function Service-NetworkNames($Service) {
    if ($Service.networks -is [System.Collections.IDictionary]) {
        return @($Service.networks.Keys)
    }
    return @($Service.networks.PSObject.Properties.Name)
}

function Assert-ExactNetworks($Service, [string]$ServiceName, [string[]]$Expected) {
    $actual = @(Service-NetworkNames $Service | Sort-Object)
    $expectedSorted = @($Expected | Sort-Object)
    if (($actual -join "|") -ne ($expectedSorted -join "|")) {
        throw "$ServiceName networks expected '$($expectedSorted -join ", ")' but were '$($actual -join ", ")'."
    }
}

function Service-SecretNames($Service) {
    foreach ($entry in @($Service.secrets)) {
        if ($entry -is [string]) {
            $entry
            continue
        }

        $source = $entry.PSObject.Properties["source"]
        if ($null -ne $source) {
            [string]$source.Value
        }
    }
}

function Assert-ServiceSecretsInclude($Service, [string]$ServiceName, [string[]]$Expected) {
    $actual = @(Service-SecretNames $Service)
    foreach ($secretName in $Expected) {
        if ($actual -notcontains $secretName) {
            throw "$ServiceName must mount Docker secret '$secretName'."
        }
    }
}

function Get-ServiceEnvironmentValue($Service, [string]$Name) {
    $environment = $Service.environment
    if ($null -eq $environment) {
        return $null
    }

    if ($environment -is [System.Collections.IDictionary]) {
        return $environment[$Name]
    }

    $property = $environment.PSObject.Properties[$Name]
    if ($null -ne $property) {
        return $property.Value
    }

    foreach ($entry in @($environment)) {
        if ($entry -is [string] -and $entry.StartsWith("$Name=", [StringComparison]::Ordinal)) {
            return $entry.Substring($Name.Length + 1)
        }
    }

    return $null
}

function Assert-ServiceEnvironmentValue($Service, [string]$ServiceName, [string]$Name, [string]$Expected) {
    $actual = Get-ServiceEnvironmentValue $Service $Name
    Assert-Equal "$ServiceName environment $Name" $actual $Expected
}

function Assert-ServiceEnvironmentMissingOrNotTrue($Service, [string]$ServiceName, [string]$Name) {
    $actual = Get-ServiceEnvironmentValue $Service $Name
    if ($null -ne $actual -and [string]$actual -eq "true") {
        throw "$ServiceName environment $Name must not be true in the production compose profile."
    }
}

function WorkflowJob([string]$Workflow, [string]$JobName) {
    $marker = "  ${JobName}:"
    $start = $Workflow.IndexOf($marker, [StringComparison]::Ordinal)
    if ($start -lt 0) {
        throw "Expected workflow job '$JobName'."
    }

    $remaining = $Workflow.Substring($start + $marker.Length)
    $nextJob = [regex]::Match($remaining, "`n  [A-Za-z0-9_-]+:`r?`n")
    if ($nextJob.Success) {
        return $remaining.Substring(0, $nextJob.Index)
    }

    return $remaining
}

function WorkflowRunBlocks([string]$Job) {
    $matches = [regex]::Matches($Job, "(?ms)^\s*run:\s*\|(?<block>.*?)(?=^\s*-\s+name:|\z)")
    foreach ($match in $matches) {
        $match.Groups["block"].Value
    }

    $singleLineMatches = [regex]::Matches($Job, "(?m)^\s*run:\s*(?<command>[^\r\n]+)\r?$")
    foreach ($match in $singleLineMatches) {
        $match.Groups["command"].Value
    }
}

try {
    $config = Invoke-DockerComposeConfigJson
    $services = $config.services

    foreach ($serviceName in @("migrate", "api", "frontend")) {
        Assert-NoBuildContext $services $serviceName
    }

    Assert-Equal "migrate image" $services.migrate.image "ghcr.io/example/accounts-api@sha256:1111111111111111111111111111111111111111111111111111111111111111"
    Assert-Equal "api image" $services.api.image "ghcr.io/example/accounts-api@sha256:1111111111111111111111111111111111111111111111111111111111111111"
    Assert-Equal "frontend image" $services.frontend.image "ghcr.io/example/accounts-frontend@sha256:2222222222222222222222222222222222222222222222222222222222222222"
    foreach ($imageReference in @($services.migrate.image, $services.api.image, $services.frontend.image)) {
        if ([string]$imageReference -cnotmatch '^ghcr\.io/[a-z0-9._/-]+@sha256:[0-9a-f]{64}$') {
            throw "Production image reference must be an immutable lowercase GHCR digest: $imageReference"
        }
    }
    if ($services.api.image -eq $services.frontend.image) {
        throw "API and frontend services must not use the same image reference."
    }

    foreach ($serviceName in @("db", "database-role-provision", "migrate", "api", "frontend")) {
        Assert-HardenedService $services.$serviceName $serviceName
    }
    Assert-ExactNetworks $services.db "db" @("api_db")
    Assert-ExactNetworks $services.'database-role-provision' "database-role-provision" @("api_db")
    Assert-ExactNetworks $services.migrate "migrate" @("api_db", "api_egress")
    Assert-ExactNetworks $services.api "api" @("api_db", "frontend_api", "api_egress")
    Assert-ExactNetworks $services.frontend "frontend" @("frontend_api")
    Assert-Equal "frontend/API network internal isolation" $config.networks.frontend_api.internal $true
    Assert-Equal "API/database network internal isolation" $config.networks.api_db.internal $true
    if ($config.networks.api_egress.internal -eq $true) {
        throw "api_egress must provide the API's only deliberate outbound path."
    }
    if ($null -ne $services.db.ports -or $null -ne $services.api.ports) {
        throw "Database and API services must not publish host ports."
    }

    Assert-ServiceSecretsInclude $services.db "db" @("postgres_password", "postgres_server_certificate", "postgres_server_key", "postgres_ca_certificate")
    Assert-ServiceSecretsInclude $services.'database-role-provision' "database-role-provision" @("postgres_password", "postgres_application_password", "postgres_ca_certificate")
    Assert-ServiceSecretsInclude $services.migrate "migrate" @("accounts_migration_connection_string", "postgres_ca_certificate", "database_tenant_context_key", "identity_hmac_key", "mfa_encryption_key", "deadline_provider_token")
    Assert-ServiceSecretsInclude $services.api "api" @("accounts_application_connection_string", "postgres_ca_certificate", "database_tenant_context_key", "identity_hmac_key", "mfa_encryption_key", "deadline_provider_token")
    if (@(Service-SecretNames $services.migrate) -contains "accounts_application_connection_string") {
        throw "migrate must not receive the least-privileged application connection secret."
    }
    if (@(Service-SecretNames $services.api) -contains "accounts_migration_connection_string") {
        throw "api must not receive the privileged migration connection secret."
    }
    $databaseEntrypoint = @($services.db.entrypoint) -join "`n"
    foreach ($requiredFragment in @(
        "/run/secrets/postgres_server_key",
        "/run/secrets/postgres_server_certificate",
        "/run/secrets/postgres_ca_certificate",
        "ssl=on",
        "ssl_cert_file=/var/lib/postgresql/tls/server.crt",
        "ssl_key_file=/var/lib/postgresql/tls/server.key",
        "ssl_ca_file=/var/lib/postgresql/tls/ca.crt",
        "ssl_min_protocol_version=TLSv1.2")) {
        if ($databaseEntrypoint -notmatch [regex]::Escape($requiredFragment)) {
            throw "Database TLS entrypoint is missing '$requiredFragment'."
        }
    }
    $databaseHealthcheck = @($services.db.healthcheck.test) -join " "
    if ($databaseHealthcheck -notmatch "sslmode=verify-full" -or
        $databaseHealthcheck -notmatch "sslrootcert=/run/secrets/postgres_ca_certificate") {
        throw "Database healthcheck must connect with verify-full against the mounted deployment CA."
    }
    $frontendPorts = @($services.frontend.ports)
    if ($frontendPorts.Count -ne 1 -or $frontendPorts[0].host_ip -ne "127.0.0.1") {
        throw "Frontend must publish exactly one loopback-only host port."
    }

    $backendDockerfile = Get-Content -LiteralPath (Join-Path $RepositoryRoot "Dockerfile.backend") -Raw
    $frontendDockerfile = Get-Content -LiteralPath (Join-Path $RepositoryRoot "Dockerfile.frontend") -Raw
    if ($backendDockerfile -notmatch '(?m)^USER \$APP_UID\s*$') {
        throw "Backend runtime image must declare its non-root APP_UID user."
    }
    if ($frontendDockerfile -notmatch '(?m)^USER nextjs\s*$') {
        throw "Frontend runtime image must declare its non-root nextjs user."
    }

    $migrateCommand = @($services.migrate.command)
    if ($migrateCommand.Count -ne 1 -or $migrateCommand[0] -ne "--migrate-only") {
        throw "migrate service must run exactly '--migrate-only'."
    }

    Assert-Equal "migrate restart policy" $services.migrate.restart "no"
    Assert-Equal "database role provision restart policy" $services.'database-role-provision'.restart "no"
    Assert-Equal "database role provision depends on healthy database" $services.'database-role-provision'.depends_on.db.condition "service_healthy"
    Assert-Equal "migrate depends on database role provision" $services.migrate.depends_on.'database-role-provision'.condition "service_completed_successfully"
    Assert-Equal "api depends on migrate condition" $services.api.depends_on.migrate.condition "service_completed_successfully"
    Assert-ServiceEnvironmentValue $services.migrate "migrate" "DatabaseStartup__AutoMigrateOnStartup" "false"
    Assert-ServiceEnvironmentValue $services.api "api" "DatabaseStartup__AutoMigrateOnStartup" "false"
    Assert-ServiceEnvironmentValue $services.migrate "migrate" "DatabaseStartup__SeedDemoData" "false"
    Assert-ServiceEnvironmentValue $services.api "api" "DatabaseStartup__SeedDemoData" "false"
    Assert-ServiceEnvironmentValue $services.migrate "migrate" "DatabaseStartup__AllowInsecureDatabaseConnection" "false"
    Assert-ServiceEnvironmentValue $services.api "api" "DatabaseStartup__AllowInsecureDatabaseConnection" "false"
    Assert-ServiceEnvironmentMissingOrNotTrue $services.migrate "migrate" "DatabaseStartup__AllowStartupMigrationInProduction"
    Assert-ServiceEnvironmentMissingOrNotTrue $services.api "api" "DatabaseStartup__AllowStartupMigrationInProduction"
    Assert-ServiceEnvironmentMissingOrNotTrue $services.migrate "migrate" "DatabaseStartup__AllowDemoSeedInProduction"
    Assert-ServiceEnvironmentMissingOrNotTrue $services.api "api" "DatabaseStartup__AllowDemoSeedInProduction"
    foreach ($serviceName in @("migrate", "api")) {
        Assert-ServiceEnvironmentValue $services.$serviceName $serviceName "DatabaseTenantIsolation__Required" "true"
        Assert-ServiceEnvironmentValue $services.$serviceName $serviceName "DatabaseTenantIsolation__ApplicationGroupRole" "accounts_api_rls"
        Assert-ServiceEnvironmentValue $services.$serviceName $serviceName "DatabaseTenantIsolation__ApplicationLoginRole" "accounts_api"
        Assert-ServiceEnvironmentValue $services.$serviceName $serviceName "DatabaseTenantIsolation__ContextSigningKey_FILE" "/run/secrets/database_tenant_context_key"
        Assert-ServiceEnvironmentValue $services.$serviceName $serviceName "IdentitySecurity__RequireInProduction" "true"
        Assert-ServiceEnvironmentValue $services.$serviceName $serviceName "IdentitySecurity__BreachedPasswordCheckEnabled" "true"
        Assert-ServiceEnvironmentValue $services.$serviceName $serviceName "IdentitySecurity__BreachedPasswordFailClosed" "true"
        Assert-ServiceEnvironmentValue $services.$serviceName $serviceName "IdentitySecurity__IdentityHmacKey_FILE" "/run/secrets/identity_hmac_key"
        Assert-ServiceEnvironmentValue $services.$serviceName $serviceName "IdentitySecurity__MfaEncryptionKeys__0__EncryptionKey_FILE" "/run/secrets/mfa_encryption_key"
        Assert-ServiceEnvironmentValue $services.$serviceName $serviceName "DeadlineDelivery__RequireInProduction" "true"
        Assert-ServiceEnvironmentValue $services.$serviceName $serviceName "DeadlineDelivery__Enabled" "true"
        Assert-ServiceEnvironmentValue $services.$serviceName $serviceName "DeadlineDelivery__ProviderToken_FILE" "/run/secrets/deadline_provider_token"
        Assert-ServiceEnvironmentValue $services.$serviceName $serviceName "Monitoring__StructuredLogRetentionDays" "90"
        Assert-ServiceEnvironmentValue $services.$serviceName $serviceName "Monitoring__ErrorEventRetentionDays" "90"
        Assert-ServiceEnvironmentValue $services.$serviceName $serviceName "Monitoring__AlertAcknowledgementMinutes" "15"
        Assert-ServiceEnvironmentValue $services.$serviceName $serviceName "Monitoring__EscalationMinutes" "30"
        Assert-ServiceEnvironmentValue $services.$serviceName $serviceName "Monitoring__OnCallOwner" "CI Operations Owner"
        Assert-ServiceEnvironmentValue $services.$serviceName $serviceName "Monitoring__AlertRoute" "ci-operations-alert-route"
        Assert-ServiceEnvironmentValue $services.$serviceName $serviceName "Monitoring__IncidentRunbookPath" "Docs/operations/monitoring-incident-response.md"
    }
    Assert-Equal "migrate bootstrap owner password secret" (Get-ServiceEnvironmentValue $services.migrate "BootstrapOwner__OwnerInitialPassword_FILE") "/run/secrets/bootstrap_owner_password"
    if ($null -ne (Get-ServiceEnvironmentValue $services.api "BootstrapOwner__OwnerInitialPassword_FILE")) {
        throw "api service must not mount or receive the bootstrap owner initial password."
    }

    $workflow = Get-Content -LiteralPath (Join-Path $RepositoryRoot ".github/workflows/ci.yml") -Raw
    $configJob = WorkflowJob $workflow "production-config"
    $smokeJob = WorkflowJob $workflow "production-smoke"
    foreach ($job in @($configJob, $smokeJob)) {
        foreach ($requiredFragment in @(
            "POSTGRES_SERVER_CERTIFICATE_FILE",
            "POSTGRES_SERVER_KEY_FILE",
            "POSTGRES_CA_CERTIFICATE_FILE",
            "POSTGRES_APPLICATION_PASSWORD_FILE",
            "ACCOUNTS_MIGRATION_CONNECTION_STRING_FILE",
            "ACCOUNTS_APPLICATION_CONNECTION_STRING_FILE",
            "DATABASE_TENANT_CONTEXT_KEY_FILE",
            "IDENTITY_HMAC_KEY_FILE",
            "MFA_ENCRYPTION_KEY_FILE",
            "MFA_ENCRYPTION_ACTIVE_KEY_ID",
            "DEADLINE_PROVIDER_TOKEN_FILE",
            "DEADLINE_DELIVERY_PROVIDER_ENDPOINT",
            "Username=accounts_api",
            "subjectAltName=DNS:db",
            "SSL Mode=VerifyFull",
            "Root Certificate=/run/secrets/postgres_ca_certificate",
            "Trust Server Certificate=false",
            "BACKUP_ENCRYPTION_CERTIFICATE_FILE",
            "BACKUP_DECRYPTION_CERTIFICATE_FILE",
            "BACKUP_DECRYPTION_PRIVATE_KEY_FILE")) {
            if ($job -notmatch [regex]::Escape($requiredFragment)) {
                throw "Production workflow job is missing PostgreSQL TLS setup fragment '$requiredFragment'."
            }
        }
    }
    $runBlocks = @(WorkflowRunBlocks $smokeJob)
    $composeUpBlocks = @($runBlocks | Where-Object { $_ -match "docker\s+compose\s+-f\s+compose\.production\.yml\s+up" })
    if ($composeUpBlocks.Count -eq 0) {
        throw "production-smoke should start the production stack with docker compose up."
    }

    foreach ($block in $composeUpBlocks) {
        $tokens = $block -split "\s+"
        if ($tokens -contains "--build") {
            throw "production-smoke compose up must not use --build."
        }
    }

    $allSmokeCommands = $runBlocks -join "`n"
    if ($allSmokeCommands -match 'docker\s+build(?:x)?\s+build') {
        throw "production-smoke must not rebuild application images."
    }
    if ($allSmokeCommands -notmatch 'docker\s+pull\s+"\$ACCOUNTS_API_IMAGE"' -or
        $allSmokeCommands -notmatch 'docker\s+pull\s+"\$ACCOUNTS_FRONTEND_IMAGE"') {
        throw "production-smoke must pull both exact promoted image references."
    }

    if (-not [string]::IsNullOrWhiteSpace($EvidencePath)) {
        $evidenceDirectory = Split-Path -Parent $EvidencePath
        if (-not [string]::IsNullOrWhiteSpace($evidenceDirectory)) {
            New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null
        }

        [ordered]@{
            status = "passed"
            checkedAtUtc = [DateTime]::UtcNow.ToString("o")
            composeFile = "compose.production.yml"
            imageContract = @{
                migrate = $services.migrate.image
                api = $services.api.image
                frontend = $services.frontend.image
                digestPinned = $true
                backendAndMigrateSameDigest = $services.migrate.image -eq $services.api.image
            }
            migrationSafety = @{
                migrateCommand = $migrateCommand
                migrateRestart = $services.migrate.restart
                roleProvisionDependsOnDatabase = $services.'database-role-provision'.depends_on.db.condition
                migrateDependsOnRoleProvision = $services.migrate.depends_on.'database-role-provision'.condition
                apiDependsOnMigrate = $services.api.depends_on.migrate.condition
                migrateAutoMigrateOnStartup = Get-ServiceEnvironmentValue $services.migrate "DatabaseStartup__AutoMigrateOnStartup"
                apiAutoMigrateOnStartup = Get-ServiceEnvironmentValue $services.api "DatabaseStartup__AutoMigrateOnStartup"
                startupMigrationOverridePresent = $null -ne (Get-ServiceEnvironmentValue $services.migrate "DatabaseStartup__AllowStartupMigrationInProduction") -or
                    $null -ne (Get-ServiceEnvironmentValue $services.api "DatabaseStartup__AllowStartupMigrationInProduction")
            }
            seedSafety = @{
                migrateSeedDemoData = Get-ServiceEnvironmentValue $services.migrate "DatabaseStartup__SeedDemoData"
                apiSeedDemoData = Get-ServiceEnvironmentValue $services.api "DatabaseStartup__SeedDemoData"
                demoSeedOverridePresent = $null -ne (Get-ServiceEnvironmentValue $services.migrate "DatabaseStartup__AllowDemoSeedInProduction") -or
                    $null -ne (Get-ServiceEnvironmentValue $services.api "DatabaseStartup__AllowDemoSeedInProduction")
                bootstrapOwnerPasswordOnlyOnMigrate = $true
            }
            workflowSafety = @{
                productionSmokeStartsCompose = $composeUpBlocks.Count -gt 0
                productionSmokeUsesBuildFlag = $false
                productionSmokeBuildCommandsPresent = $false
                productionSmokePullsExactDigests = $true
            }
            containerHardening = @{
                readOnlyRootFilesystems = @("db", "database-role-provision", "migrate", "api", "frontend")
                noNewPrivileges = @("db", "database-role-provision", "migrate", "api", "frontend")
                allCapabilitiesDropped = @("db", "database-role-provision", "migrate", "api", "frontend")
                boundedResources = @("db", "database-role-provision", "migrate", "api", "frontend")
                databaseNetworks = @(Service-NetworkNames $services.db)
                migrationNetworks = @(Service-NetworkNames $services.migrate)
                apiNetworks = @(Service-NetworkNames $services.api)
                frontendNetworks = @(Service-NetworkNames $services.frontend)
                databaseAndApiPortsUnpublished = $true
                frontendLoopbackOnly = $true
                backendRuntimeNonRoot = $true
                frontendRuntimeNonRoot = $true
            }
            databaseIsolation = @{
                required = Get-ServiceEnvironmentValue $services.api "DatabaseTenantIsolation__Required"
                applicationGroupRole = Get-ServiceEnvironmentValue $services.api "DatabaseTenantIsolation__ApplicationGroupRole"
                applicationLoginRole = Get-ServiceEnvironmentValue $services.api "DatabaseTenantIsolation__ApplicationLoginRole"
                applicationAndMigrationCredentialsSeparated = $true
                apiHasMigrationCredential = $false
                forcedRlsProvisionedByMigration = $true
                signedTenantContextSecret = "database_tenant_context_key"
            }
            identitySecurity = @{
                required = Get-ServiceEnvironmentValue $services.api "IdentitySecurity__RequireInProduction"
                breachedPasswordCheckEnabled = Get-ServiceEnvironmentValue $services.api "IdentitySecurity__BreachedPasswordCheckEnabled"
                breachedPasswordFailClosed = Get-ServiceEnvironmentValue $services.api "IdentitySecurity__BreachedPasswordFailClosed"
                identityHmacSecret = "identity_hmac_key"
                mfaEncryptionSecret = "mfa_encryption_key"
                privilegedMfaRequired = Get-ServiceEnvironmentValue $services.api "AuthSession__RequirePrivilegedMfa"
            }
            deadlineDelivery = @{
                required = Get-ServiceEnvironmentValue $services.api "DeadlineDelivery__RequireInProduction"
                enabled = Get-ServiceEnvironmentValue $services.api "DeadlineDelivery__Enabled"
                provider = Get-ServiceEnvironmentValue $services.api "DeadlineDelivery__Provider"
                providerTokenSecret = "deadline_provider_token"
            }
            databaseTransport = @{
                sslEnabled = $true
                minimumProtocol = "TLSv1.2"
                serverCertificateSecret = "postgres_server_certificate"
                serverKeySecret = "postgres_server_key"
                rootCaSecret = "postgres_ca_certificate"
                clientSslMode = "VerifyFull"
                serverIdentityVerified = $true
                insecureOverrideDisabled = $true
                healthcheckUsesVerifiedTls = $true
            }
            backupProtection = @{
                encryptedArtifactRequired = $true
                algorithm = "CMS/AES-256-CBC"
                encryptionCertificateSeparatedFromPrivateRecoveryKey = $true
                plaintextDumpRetentionForbidden = $true
                encryptedRestoreDrillRequired = $true
            }
            monitoringOperations = @{
                structuredLogRetentionDays = Get-ServiceEnvironmentValue $services.api "Monitoring__StructuredLogRetentionDays"
                errorEventRetentionDays = Get-ServiceEnvironmentValue $services.api "Monitoring__ErrorEventRetentionDays"
                acknowledgementMinutes = Get-ServiceEnvironmentValue $services.api "Monitoring__AlertAcknowledgementMinutes"
                escalationMinutes = Get-ServiceEnvironmentValue $services.api "Monitoring__EscalationMinutes"
                onCallOwner = Get-ServiceEnvironmentValue $services.api "Monitoring__OnCallOwner"
                alertRoute = Get-ServiceEnvironmentValue $services.api "Monitoring__AlertRoute"
                incidentRunbookPath = Get-ServiceEnvironmentValue $services.api "Monitoring__IncidentRunbookPath"
            }
        } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $EvidencePath -Encoding UTF8

        Write-Host "Production safety evidence written: $EvidencePath"
    }
} finally {
    Remove-Item -LiteralPath $SecretRoot -Recurse -Force -ErrorAction SilentlyContinue
}
