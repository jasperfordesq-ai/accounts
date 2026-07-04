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
    $accountsApiKey = "dummy-api-key"
    $accountsConnectionString = "Host=db;Port=5432;Database=accounts;Username=accounts;Password=$postgresPassword"

    @(
        @("POSTGRES_DB", "accounts"),
        @("POSTGRES_USER", "accounts"),
        @("POSTGRES_PASSWORD_FILE", (New-SecretFile "postgres_password" $postgresPassword)),
        @("ACCOUNTS_CONNECTION_STRING_FILE", (New-SecretFile "accounts_connection_string" $accountsConnectionString)),
        @("AUTH_SESSION_SIGNING_KEY_FILE", (New-SecretFile "auth_session_signing_key" "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==")),
        @("AUDIT_INTEGRITY_SIGNING_KEY_FILE", (New-SecretFile "audit_integrity_signing_key" "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB==")),
        @("AUDIT_INTEGRITY_ACTIVE_KEY_ID", "local-dummy"),
        @("ACCOUNTS_API_KEY_FILE", (New-SecretFile "accounts_api_key" $accountsApiKey)),
        @("ACCOUNTS_API_KEY_HASH", "0000000000000000000000000000000000000000000000000000000000000000"),
        @("ACCOUNTS_API_IMAGE", "accounts-api-ci:verify"),
        @("ACCOUNTS_FRONTEND_IMAGE", "accounts-frontend-ci:verify"),
        @("ACCOUNTS_ALLOWED_HOSTS", "accounts-smoke.local"),
        @("ACCOUNTS_ALLOWED_ORIGIN", "https://accounts-smoke.local"),
        @("TRUST_PROXY_HEADERS", "true"),
        @("FRONTEND_PORT", "3000"),
        @("NO_PROXY", "accounts-smoke.local,127.0.0.1,localhost"),
        @("no_proxy", "accounts-smoke.local,127.0.0.1,localhost"),
        @("MONITORING_ERROR_TRACKING_DSN", "https://public@sentry.invalid/1"),
        @("MONITORING_TRACES_SAMPLE_RATE", "0"),
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

    Assert-Equal "migrate image" $services.migrate.image "accounts-api-ci:verify"
    Assert-Equal "api image" $services.api.image "accounts-api-ci:verify"
    Assert-Equal "frontend image" $services.frontend.image "accounts-frontend-ci:verify"
    if ($services.api.image -eq $services.frontend.image) {
        throw "API and frontend services must not use the same image reference."
    }

    $workflow = Get-Content -LiteralPath (Join-Path $RepositoryRoot ".github/workflows/ci.yml") -Raw
    $smokeJob = WorkflowJob $workflow "production-smoke"
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
} finally {
    Remove-Item -LiteralPath $SecretRoot -Recurse -Force -ErrorAction SilentlyContinue
}
