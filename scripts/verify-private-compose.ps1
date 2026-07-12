param(
    [string]$ComposeFile = "compose.private.yml",
    [string]$EvidencePath = ""
)

$ErrorActionPreference = "Stop"

$RepositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$ResolvedComposeFile = if ([System.IO.Path]::IsPathRooted($ComposeFile)) {
    [System.IO.Path]::GetFullPath($ComposeFile)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $RepositoryRoot $ComposeFile))
}
$SecretRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("accounts-private-compose-secrets-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $SecretRoot | Out-Null

function New-SecretFile([string]$Name, [string]$Value) {
    $path = Join-Path $SecretRoot $Name
    [System.IO.File]::WriteAllText($path, $Value)
    return $path
}

function PrivateComposeEnv {
    $postgresPassword = "private-verifier-postgres-password"
    $applicationPassword = "private-verifier-application-password"
    $apiKey = "private-verifier-frontend-api-key"
    $migrationConnection = "Host=db;Port=5432;Database=accounts;Username=accounts;Password=$postgresPassword;SSL Mode=Disable"
    $applicationConnection = "Host=db;Port=5432;Database=accounts;Username=accounts_api;Password=$applicationPassword;SSL Mode=Disable"

    @(
        @("POSTGRES_DB", "accounts"),
        @("POSTGRES_USER", "accounts"),
        @("ACCOUNTS_POSTGRES_IMAGE", "docker.io/library/postgres@sha256:3333333333333333333333333333333333333333333333333333333333333333"),
        @("ACCOUNTS_API_IMAGE", "ghcr.io/example/accounts-api@sha256:1111111111111111111111111111111111111111111111111111111111111111"),
        @("ACCOUNTS_FRONTEND_IMAGE", "ghcr.io/example/accounts-frontend@sha256:2222222222222222222222222222222222222222222222222222222222222222"),
        @("PRIVATE_INSTALLATION_ID", "2f46b0f5-5b33-4f02-9341-b8dcf95fb35e"),
        @("PRIVATE_FRONTEND_PORT", "3500"),
        @("PRIVATE_SERVER_ORIGIN", "https://accounts-device.example-tailnet.ts.net"),
        @("PRIVATE_LOCAL_ORIGIN", "http://localhost:3500"),
        @("PRIVATE_ALLOWED_HOSTS", "accounts-device.example-tailnet.ts.net;localhost;127.0.0.1"),
        @("PRIVATE_TENANT_NAME", "Private verifier organisation"),
        @("PRIVATE_TENANT_SLUG", "private-verifier"),
        @("PRIVATE_OWNER_EMAIL", "owner@example.ie"),
        @("PRIVATE_OWNER_DISPLAY_NAME", "Private Owner"),
        @("AUDIT_INTEGRITY_ACTIVE_KEY_ID", "private-audit-primary"),
        @("MFA_ENCRYPTION_ACTIVE_KEY_ID", "private-mfa-primary"),
        @("POSTGRES_PASSWORD_FILE", (New-SecretFile "postgres_password" $postgresPassword)),
        @("POSTGRES_APPLICATION_PASSWORD_FILE", (New-SecretFile "postgres_application_password" $applicationPassword)),
        @("ACCOUNTS_MIGRATION_CONNECTION_STRING_FILE", (New-SecretFile "accounts_migration_connection_string" $migrationConnection)),
        @("ACCOUNTS_APPLICATION_CONNECTION_STRING_FILE", (New-SecretFile "accounts_application_connection_string" $applicationConnection)),
        @("AUTH_SESSION_SIGNING_KEY_FILE", (New-SecretFile "auth_session_signing_key" ("A" * 96))),
        @("AUDIT_INTEGRITY_SIGNING_KEY_FILE", (New-SecretFile "audit_integrity_signing_key" ("B" * 96))),
        @("DATABASE_TENANT_CONTEXT_KEY_FILE", (New-SecretFile "database_tenant_context_key" ("C" * 96))),
        @("IDENTITY_HMAC_KEY_FILE", (New-SecretFile "identity_hmac_key" ("D" * 96))),
        @("MFA_ENCRYPTION_KEY_FILE", (New-SecretFile "mfa_encryption_key" "EEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEE=")),
        @("ACCOUNTS_API_KEY_HASH_FILE", (New-SecretFile "accounts_api_key_hash" ("0" * 64))),
        @("ACCOUNTS_API_KEY_FILE", (New-SecretFile "accounts_api_key" $apiKey)),
        @("PRIVATE_INITIAL_OWNER_PASSWORD_FILE", (New-SecretFile "private_initial_owner_password" "PrivateVerifierOwner1!ChangeMe"))
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

function Invoke-PrivateComposeConfigJson {
    if (-not (Test-Path -LiteralPath $ResolvedComposeFile -PathType Leaf)) {
        throw "Private compose file does not exist: $ResolvedComposeFile"
    }

    $output = Invoke-WithTemporaryEnvironment (PrivateComposeEnv) {
        & docker compose -f $ResolvedComposeFile --profile initialize --profile owner-recovery config --format json
    }
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose config --format json failed with exit code $LASTEXITCODE."
    }

    return ($output | ConvertFrom-Json)
}

function Assert-Equal([string]$Description, $Actual, $Expected) {
    if ($Actual -ne $Expected) {
        throw "$Description expected '$Expected' but was '$Actual'."
    }
}

function Assert-Missing($Value, [string]$Description) {
    if ($null -ne $Value) {
        throw "$Description must be absent."
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
    if ($null -eq $environment) { return $null }
    if ($environment -is [System.Collections.IDictionary]) { return $environment[$Name] }

    $property = $environment.PSObject.Properties[$Name]
    if ($null -ne $property) { return $property.Value }

    foreach ($entry in @($environment)) {
        if ($entry -is [string] -and $entry.StartsWith("$Name=", [StringComparison]::Ordinal)) {
            return $entry.Substring($Name.Length + 1)
        }
    }
    return $null
}

function Assert-ServiceEnvironmentValue($Service, [string]$ServiceName, [string]$Name, [string]$Expected) {
    Assert-Equal "$ServiceName environment $Name" (Get-ServiceEnvironmentValue $Service $Name) $Expected
}

function Assert-HardenedService($Service, [string]$ServiceName) {
    Assert-Equal "$ServiceName read-only root filesystem" $Service.read_only $true
    if (@($Service.security_opt) -notcontains "no-new-privileges:true") {
        throw "$ServiceName must enable no-new-privileges."
    }
    if (@($Service.cap_drop) -notcontains "ALL") {
        throw "$ServiceName must drop all Linux capabilities."
    }
    if ($Service.privileged -eq $true) {
        throw "$ServiceName must not be privileged."
    }
    if ([int]$Service.pids_limit -le 0 -or [decimal]$Service.mem_limit -le 0 -or [decimal]$Service.cpus -le 0) {
        throw "$ServiceName must define positive PID, memory, and CPU limits."
    }
    if (@($Service.tmpfs).Count -eq 0) {
        throw "$ServiceName must provide bounded tmpfs storage for its read-only root filesystem."
    }
    if ($Service.PSObject.Properties.Name -contains "build") {
        throw "$ServiceName must use a compiled image and must not define a build context."
    }
    if ($Service.PSObject.Properties.Name -contains "devices" -or $Service.pid -eq "host" -or $Service.network_mode -eq "host") {
        throw "$ServiceName must not receive host devices, PID namespace, or host networking."
    }
    $logging = $Service.logging
    Assert-Equal "$ServiceName logging driver" $logging.driver "json-file"
    Assert-Equal "$ServiceName log max-size" $logging.options.'max-size' "10m"
    Assert-Equal "$ServiceName log max-file" ([string]$logging.options.'max-file') "3"
}

function Assert-PrivateLabels($Service, [string]$ServiceName, [string]$ExpectedComponent) {
    $labels = $Service.labels
    if ($null -eq $labels) { throw "$ServiceName must carry installation-scoped Private Server labels." }
    Assert-Equal "$ServiceName deployment label" $labels.'ie.filingbridge.deployment-mode' "PrivateServer"
    Assert-Equal "$ServiceName installation label" $labels.'ie.filingbridge.installation-id' "2f46b0f5-5b33-4f02-9341-b8dcf95fb35e"
    Assert-Equal "$ServiceName component label" $labels.'ie.filingbridge.component' $ExpectedComponent
}

function Assert-ImmutableDigest([string]$Reference, [string]$Description) {
    if ($Reference -cnotmatch '^[a-z0-9][a-z0-9._/:_-]*@sha256:[0-9a-f]{64}$') {
        throw "$Description must be an immutable lowercase digest reference, but was '$Reference'."
    }
}

function Assert-NoPublishedPorts($Service, [string]$ServiceName) {
    $portsProperty = $Service.PSObject.Properties["ports"]
    if ($null -ne $portsProperty -and @($portsProperty.Value).Count -gt 0) {
        throw "$ServiceName must not publish a host port in Private Server mode."
    }
}

function Assert-NoBindMounts($Service, [string]$ServiceName) {
    $volumesProperty = $Service.PSObject.Properties["volumes"]
    if ($null -eq $volumesProperty) { return }
    foreach ($mount in @($volumesProperty.Value)) {
        if ($mount.type -eq "bind") {
            throw "$ServiceName must not bind-mount host source or configuration directories."
        }
    }
}

function Assert-Command($Service, [string]$ServiceName, [string]$Expected) {
    $command = @($Service.command)
    if ($command.Count -ne 1 -or $command[0] -ne $Expected) {
        throw "$ServiceName must run exactly '$Expected'."
    }
}

function Assert-ConnectionSecret($Config, [string]$SecretName, [string]$ExpectedUser) {
    $secret = $Config.secrets.$SecretName
    if ($null -eq $secret -or [string]::IsNullOrWhiteSpace([string]$secret.file)) {
        throw "$SecretName must be backed by a host secret file."
    }
    if (-not (Test-Path -LiteralPath $secret.file -PathType Leaf)) {
        throw "$SecretName secret file is not readable during verification."
    }

    $connection = Get-Content -LiteralPath $secret.file -Raw
    foreach ($required in @("Host=db", "Database=accounts", "Username=$ExpectedUser", "SSL Mode=Disable")) {
        if ($connection -notmatch [regex]::Escape($required)) {
            throw "$SecretName must contain '$required'."
        }
    }
    if ($connection -match 'SSL Mode\s*=\s*(Allow|Prefer|Require|VerifyCA|VerifyFull)' -or
        $connection -match 'Trust Server Certificate\s*=\s*true') {
        throw "$SecretName must use explicit SSL Mode=Disable, not an ambiguous or trust-bypassing TLS mode."
    }
}

try {
    $config = Invoke-PrivateComposeConfigJson
    $services = $config.services
    $expectedServices = @("api", "db", "frontend", "migrate", "private-initialize", "private-owner-recovery", "role-provision")
    $actualServices = @($services.PSObject.Properties.Name | Sort-Object)

    Assert-Equal "compose project name" $config.name "accounts-private"
    if (($actualServices -join "|") -ne (($expectedServices | Sort-Object) -join "|")) {
        throw "Private compose services expected '$($expectedServices -join ", ")' but were '$($actualServices -join ", ")'."
    }

    foreach ($serviceName in $expectedServices) {
        Assert-HardenedService $services.$serviceName $serviceName
        Assert-NoBindMounts $services.$serviceName $serviceName
        Assert-ImmutableDigest ([string]$services.$serviceName.image) "$serviceName image"
    }
    $expectedComponents = @{
        db = "database"
        'role-provision' = "role-provision"
        migrate = "migrate"
        'private-initialize' = "private-initialize"
        'private-owner-recovery' = "private-owner-recovery"
        api = "api"
        frontend = "frontend"
    }
    foreach ($serviceName in $expectedServices) {
        Assert-PrivateLabels $services.$serviceName $serviceName $expectedComponents[$serviceName]
    }

    foreach ($serviceName in @("migrate", "private-initialize", "private-owner-recovery", "api")) {
        Assert-Equal "$serviceName API image" $services.$serviceName.image $services.api.image
    }
    Assert-Equal "role-provision PostgreSQL image" $services.'role-provision'.image $services.db.image
    Assert-Equal "role-provision non-root user" $services.'role-provision'.user "postgres"
    if ($services.api.image -eq $services.frontend.image -or $services.api.image -eq $services.db.image) {
        throw "API, frontend, and PostgreSQL must use distinct image references."
    }

    foreach ($serviceName in @("db", "role-provision", "migrate", "private-initialize", "private-owner-recovery", "api")) {
        Assert-NoPublishedPorts $services.$serviceName $serviceName
    }
    $frontendPorts = @($services.frontend.ports)
    if ($frontendPorts.Count -ne 1 -or
        $frontendPorts[0].host_ip -ne "127.0.0.1" -or
        [int]$frontendPorts[0].target -ne 3000 -or
        [int]$frontendPorts[0].published -ne 3500) {
        throw "frontend must publish only 127.0.0.1:3500 to container port 3000 during verification."
    }

    Assert-ExactNetworks $services.db "db" @("api_db")
    Assert-ExactNetworks $services.'role-provision' "role-provision" @("api_db")
    Assert-ExactNetworks $services.migrate "migrate" @("api_db")
    Assert-ExactNetworks $services.'private-initialize' "private-initialize" @("api_db")
    Assert-ExactNetworks $services.'private-owner-recovery' "private-owner-recovery" @("api_db")
    Assert-ExactNetworks $services.api "api" @("api_db", "frontend_api")
    Assert-ExactNetworks $services.frontend "frontend" @("frontend_api", "frontend_ingress")
    Assert-Equal "frontend/API network isolation" $config.networks.frontend_api.internal $true
    Assert-Equal "API/database network isolation" $config.networks.api_db.internal $true
    if ($config.networks.frontend_ingress.internal -eq $true) {
        throw "The frontend ingress network must remain non-internal so Docker Desktop publishes its loopback port."
    }
    Assert-Equal "loopback ingress network driver" $config.networks.frontend_ingress.driver "bridge"
    Assert-Equal "loopback ingress bridge masquerade setting" $config.networks.frontend_ingress.driver_opts.'com.docker.network.bridge.enable_ip_masquerade' "false"
    if (@($config.networks.PSObject.Properties.Name | Sort-Object) -join "|" -ne "api_db|frontend_api|frontend_ingress") {
        throw "Private Server must define only internal api_db/frontend_api plus the frontend-only loopback ingress bridge."
    }

    $dbMounts = @($services.db.volumes)
    if ($dbMounts.Count -ne 1 -or $dbMounts[0].type -ne "volume" -or
        $dbMounts[0].source -ne "accounts_private_data" -or
        $dbMounts[0].target -ne "/var/lib/postgresql/data") {
        throw "db must use only the accounts_private_data named volume for PostgreSQL data."
    }
    Assert-Equal "private volume resource count" @($config.volumes.PSObject.Properties.Name).Count 1
    if ($config.volumes.PSObject.Properties.Name -notcontains "accounts_private_data") {
        throw "Private Server must define the accounts_private_data named volume."
    }
    Assert-Equal "database volume deployment label" $config.volumes.accounts_private_data.labels.'ie.filingbridge.deployment-mode' "PrivateServer"
    Assert-Equal "database volume installation label" $config.volumes.accounts_private_data.labels.'ie.filingbridge.installation-id' "2f46b0f5-5b33-4f02-9341-b8dcf95fb35e"
    Assert-Equal "database volume component label" $config.volumes.accounts_private_data.labels.'ie.filingbridge.component' "database-volume"
    foreach ($serviceName in @("role-provision", "migrate", "private-initialize", "private-owner-recovery", "api", "frontend")) {
        $volumesProperty = $services.$serviceName.PSObject.Properties["volumes"]
        if ($null -ne $volumesProperty -and @($volumesProperty.Value).Count -gt 0) {
            throw "$serviceName must not mount a persistent or source volume."
        }
    }

    Assert-Command $services.migrate "migrate" "--migrate-only"
    Assert-Command $services.'private-initialize' "private-initialize" "--private-initialize"
    Assert-Command $services.'private-owner-recovery' "private-owner-recovery" "--private-owner-recovery"
    Assert-Equal "private initializer profile" (@($services.'private-initialize'.profiles) -join "|") "initialize"
    Assert-Equal "owner recovery profile" (@($services.'private-owner-recovery'.profiles) -join "|") "owner-recovery"

    foreach ($serviceName in @("db", "api", "frontend")) {
        Assert-Equal "$serviceName restart policy" $services.$serviceName.restart "unless-stopped"
    }
    foreach ($serviceName in @("role-provision", "migrate", "private-initialize", "private-owner-recovery")) {
        Assert-Equal "$serviceName restart policy" $services.$serviceName.restart "no"
    }
    Assert-Equal "role-provision database dependency" $services.'role-provision'.depends_on.db.condition "service_healthy"
    Assert-Equal "migration role dependency" $services.migrate.depends_on.'role-provision'.condition "service_completed_successfully"
    Assert-Equal "initializer migration dependency" $services.'private-initialize'.depends_on.migrate.condition "service_completed_successfully"
    Assert-Equal "owner recovery migration dependency" $services.'private-owner-recovery'.depends_on.migrate.condition "service_completed_successfully"
    Assert-Equal "API migration dependency" $services.api.depends_on.migrate.condition "service_completed_successfully"
    Assert-Equal "frontend API dependency" $services.frontend.depends_on.api.condition "service_healthy"

    $commonApiServices = @("migrate", "private-initialize", "private-owner-recovery", "api")
    foreach ($serviceName in $commonApiServices) {
        $service = $services.$serviceName
        foreach ($setting in @(
            @("ASPNETCORE_ENVIRONMENT", "Production"),
            @("Deployment__Mode", "PrivateServer"),
            @("Deployment__InstallationId", "2f46b0f5-5b33-4f02-9341-b8dcf95fb35e"),
            @("DatabaseStartup__AutoMigrateOnStartup", "false"),
            @("DatabaseStartup__SeedDemoData", "false"),
            @("DatabaseStartup__AllowInsecureDatabaseConnection", "true"),
            @("DatabaseTenantIsolation__Required", "true"),
            @("DatabaseTenantIsolation__ApplicationGroupRole", "accounts_api_rls"),
            @("DatabaseTenantIsolation__ApplicationLoginRole", "accounts_api"),
            @("AuthSession__SecureCookiesInProduction", "true"),
            @("IdentitySecurity__RequireInProduction", "true"),
            @("IdentitySecurity__BreachedPasswordCheckEnabled", "false"),
            @("IdentitySecurity__BreachedPasswordFailClosed", "false"),
            @("AllowedOrigins__0", "https://accounts-device.example-tailnet.ts.net"),
            @("AllowedOrigins__1", "http://localhost:3500"),
            @("RateLimits__TrustForwardedFor", "false"),
            @("TRUST_PROXY_HEADERS", "false"),
            @("ApiAccess__Enabled", "true"),
            @("ApiAccess__RequireInProduction", "true"),
            @("Monitoring__ErrorTrackingProvider", "LocalStructuredLogs"),
            @("Monitoring__StructuredJsonConsole", "true"),
            @("Monitoring__IncludeCorrelationId", "true"),
            @("DeadlineDelivery__RequireInProduction", "false"),
            @("DeadlineDelivery__Enabled", "false"),
            @("BootstrapOwner__Enabled", "false")
        )) {
            Assert-ServiceEnvironmentValue $service $serviceName $setting[0] $setting[1]
        }
        Assert-ServiceEnvironmentValue $service $serviceName "AuthSession__SigningKey_FILE" "/run/secrets/auth_session_signing_key"
        Assert-ServiceEnvironmentValue $service $serviceName "AuditIntegrity__SigningKeys__0__SigningKey_FILE" "/run/secrets/audit_integrity_signing_key"
        Assert-ServiceEnvironmentValue $service $serviceName "DatabaseTenantIsolation__ContextSigningKey_FILE" "/run/secrets/database_tenant_context_key"
        Assert-ServiceEnvironmentValue $service $serviceName "IdentitySecurity__IdentityHmacKey_FILE" "/run/secrets/identity_hmac_key"
        Assert-ServiceEnvironmentValue $service $serviceName "IdentitySecurity__MfaEncryptionKeys__0__EncryptionKey_FILE" "/run/secrets/mfa_encryption_key"
        Assert-ServiceEnvironmentValue $service $serviceName "ApiAccess__Keys__0__KeyHash_FILE" "/run/secrets/accounts_api_key_hash"
        Assert-Missing (Get-ServiceEnvironmentValue $service "POSTGRES_PASSWORD") "$serviceName plaintext database password"
        Assert-Missing (Get-ServiceEnvironmentValue $service "AuthSession__SigningKey") "$serviceName plaintext session key"
        Assert-Missing (Get-ServiceEnvironmentValue $service "BootstrapOwner__OwnerInitialPassword") "$serviceName bootstrap password"
    }

    foreach ($serviceName in @("migrate", "private-initialize", "private-owner-recovery")) {
        Assert-ServiceEnvironmentValue $services.$serviceName $serviceName "ConnectionStrings__DefaultConnection_FILE" "/run/secrets/accounts_migration_connection_string"
    }
    Assert-ServiceEnvironmentValue $services.api "api" "ConnectionStrings__DefaultConnection_FILE" "/run/secrets/accounts_application_connection_string"
    Assert-ServiceEnvironmentValue $services.frontend "frontend" "DEPLOYMENT_MODE" "PrivateServer"
    Assert-ServiceEnvironmentValue $services.frontend "frontend" "PRIVATE_LOCAL_ORIGIN" "http://localhost:3500"
    Assert-ServiceEnvironmentValue $services.frontend "frontend" "TRUST_PROXY_HEADERS" "false"
    Assert-ServiceEnvironmentValue $services.frontend "frontend" "ACCOUNTS_API_KEY_FILE" "/run/secrets/accounts_api_key"

    Assert-ServiceEnvironmentValue $services.'private-initialize' "private-initialize" "PrivateInitialization__TenantName" "Private verifier organisation"
    Assert-ServiceEnvironmentValue $services.'private-initialize' "private-initialize" "PrivateInitialization__TenantSlug" "private-verifier"
    Assert-ServiceEnvironmentValue $services.'private-initialize' "private-initialize" "PrivateInitialization__OwnerEmail" "owner@example.ie"
    Assert-ServiceEnvironmentValue $services.'private-initialize' "private-initialize" "PrivateInitialization__OwnerDisplayName" "Private Owner"
    Assert-ServiceEnvironmentValue $services.'private-initialize' "private-initialize" "PrivateInitialization__OwnerInitialPassword_FILE" "/run/secrets/private_initial_owner_password"
    Assert-ServiceEnvironmentValue $services.'private-owner-recovery' "private-owner-recovery" "PrivateOwnerRecovery__TenantSlug" ""

    $cryptoSecrets = @("auth_session_signing_key", "audit_integrity_signing_key", "database_tenant_context_key", "identity_hmac_key", "mfa_encryption_key", "accounts_api_key_hash")
    foreach ($serviceName in $commonApiServices) {
        Assert-ServiceSecretsInclude $services.$serviceName $serviceName $cryptoSecrets
    }
    Assert-ServiceSecretsInclude $services.migrate "migrate" @("accounts_migration_connection_string")
    Assert-ServiceSecretsInclude $services.'private-initialize' "private-initialize" @("accounts_migration_connection_string", "private_initial_owner_password")
    Assert-ServiceSecretsInclude $services.'private-owner-recovery' "private-owner-recovery" @("accounts_migration_connection_string")
    Assert-ServiceSecretsInclude $services.api "api" @("accounts_application_connection_string")
    Assert-ServiceSecretsInclude $services.frontend "frontend" @("accounts_api_key")
    foreach ($serviceName in @("migrate", "private-owner-recovery", "api", "frontend")) {
        if (@(Service-SecretNames $services.$serviceName) -contains "private_initial_owner_password") {
            throw "$serviceName must not receive the one-time initial Owner password."
        }
    }
    if (@(Service-SecretNames $services.api) -contains "accounts_migration_connection_string") {
        throw "api must never receive the privileged migration connection string."
    }
    if (@(Service-SecretNames $services.migrate) -contains "accounts_application_connection_string") {
        throw "migrate must not receive the application login connection string."
    }

    $roleCommand = @($services.'role-provision'.command) -join "`n"
    foreach ($required in @("accounts_api", "NOSUPERUSER", "NOBYPASSRLS", "NOCREATEDB", "NOCREATEROLE", "sslmode=disable")) {
        if ($roleCommand -notmatch [regex]::Escape($required)) {
            throw "role-provision command must retain '$required'."
        }
    }
    $databaseHealthcheck = @($services.db.healthcheck.test) -join " "
    if ($databaseHealthcheck -notmatch "sslmode=disable") {
        throw "Private PostgreSQL healthcheck must use explicit sslmode=disable on the isolated network."
    }
    Assert-ConnectionSecret $config "accounts_migration_connection_string" "accounts"
    Assert-ConnectionSecret $config "accounts_application_connection_string" "accounts_api"

    $backendDockerfile = Get-Content -LiteralPath (Join-Path $RepositoryRoot "Dockerfile.backend") -Raw
    $frontendDockerfile = Get-Content -LiteralPath (Join-Path $RepositoryRoot "Dockerfile.frontend") -Raw
    if ($backendDockerfile -notmatch '(?m)^USER \$APP_UID\s*$') {
        throw "Backend runtime image must retain its non-root APP_UID user."
    }
    if ($frontendDockerfile -notmatch '(?m)^USER nextjs\s*$') {
        throw "Frontend runtime image must retain its non-root nextjs user."
    }

    $composeSource = Get-Content -LiteralPath $ResolvedComposeFile -Raw
    foreach ($forbidden in @('accounts_dev', 'SeedDemoData: "true"', 'AutoMigrateOnStartup: "true"', 'api_egress', 'Caddy', 'Funnel')) {
        if ($composeSource -cmatch [regex]::Escape($forbidden)) {
            throw "Private compose contains forbidden development/public-ingress fragment '$forbidden'."
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($EvidencePath)) {
        $evidenceDirectory = Split-Path -Parent $EvidencePath
        if (-not [string]::IsNullOrWhiteSpace($evidenceDirectory)) {
            New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null
        }
        [ordered]@{
            status = "passed"
            checkedAtUtc = [DateTime]::UtcNow.ToString("o")
            composeFile = [System.IO.Path]::GetFileName($ResolvedComposeFile)
            projectName = $config.name
            services = $actualServices
            runtimeServices = @("db", "api", "frontend")
            oneShotServices = @("role-provision", "migrate", "private-initialize", "private-owner-recovery")
            profiles = @("initialize", "owner-recovery")
            deploymentMode = Get-ServiceEnvironmentValue $services.api "Deployment__Mode"
            productionRuntime = Get-ServiceEnvironmentValue $services.api "ASPNETCORE_ENVIRONMENT"
            publishedPorts = @("127.0.0.1:3500:3000")
            internalNetworks = @("api_db", "frontend_api")
            loopbackIngressNetwork = [ordered]@{
                name = "frontend_ingress"
                attachedServices = @("frontend")
                ipMasqueradeDisabled = $true
                dockerDesktopEgressIsolationClaimed = $false
            }
            installationScopedLabels = $true
            databaseVolume = "accounts_private_data"
            databaseTransport = "SSL Mode=Disable on unpublished internal network"
            externalProvidersRequired = $false
            forwardedHeadersTrusted = $false
            sourceBindMountsPresent = $false
            demoSeedEnabled = $false
            runtimeHasInitialOwnerPassword = $false
            digestPinnedImages = $true
        } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $EvidencePath -Encoding UTF8
        Write-Host "Private Server compose evidence written: $EvidencePath"
    }

    Write-Host "Private Server compose contract OK"
} finally {
    Remove-Item -LiteralPath $SecretRoot -Recurse -Force -ErrorAction SilentlyContinue
}
