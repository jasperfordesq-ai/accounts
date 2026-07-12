$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Net.Http

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$verifier = Join-Path $PSScriptRoot "verify-private-compose.ps1"
$sourceCompose = Join-Path $repositoryRoot "compose.private.yml"
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("accounts-private-compose-tests-" + [Guid]::NewGuid().ToString("N"))
$powerShellExecutable = (Get-Process -Id $PID).Path
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null

function Invoke-Verifier([string]$ComposePath) {
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $output = & $powerShellExecutable -NoProfile -ExecutionPolicy Bypass -File $verifier -ComposeFile $ComposePath 2>&1
        $exitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    [pscustomobject]@{
        ExitCode = $exitCode
        Output = @($output) -join "`n"
    }
}

function Write-MutatedCompose([string]$Name, [scriptblock]$Mutate) {
    $source = Get-Content -LiteralPath $sourceCompose -Raw
    $mutated = & $Mutate $source
    $path = Join-Path $testRoot $Name
    [System.IO.File]::WriteAllText($path, $mutated)
    return $path
}

function Assert-Fails([string]$Name, [string]$ComposePath, [string]$ExpectedMessage) {
    $result = Invoke-Verifier $ComposePath
    if ($result.ExitCode -eq 0) {
        throw "$Name unexpectedly passed the Private Server compose verifier."
    }
    if ($result.Output -notmatch [regex]::Escape($ExpectedMessage)) {
        throw "$Name failed without expected message '$ExpectedMessage'. Output: $($result.Output)"
    }
}

function Test-LoopbackIngressNetwork {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    try {
        $listener.Start()
        $port = ([Net.IPEndPoint]$listener.LocalEndpoint).Port
    } finally {
        $listener.Stop()
    }

    $project = "fb-ingress-test-" + [Guid]::NewGuid().ToString("N").Substring(0, 12)
    $composePath = Join-Path $testRoot "loopback-ingress.yml"
    $source = @"
services:
  probe:
    image: node:24-alpine@sha256:a0b9bf06e4e6193cf7a0f58816cc935ff8c2a908f81e6f1a95432d679c54fbfd
    command: ["node", "-e", "require('http').createServer((q,r)=>r.end('filingbridge-loopback-ok')).listen(3000,'0.0.0.0')"]
    read_only: true
    ports:
      - "127.0.0.1:${port}:3000"
    networks:
      - frontend_ingress
    healthcheck:
      test: ["CMD", "node", "-e", "fetch('http://127.0.0.1:3000').then(r=>{if(!r.ok)process.exit(1)}).catch(()=>process.exit(1))"]
      interval: 1s
      timeout: 3s
      retries: 30
networks:
  frontend_ingress:
    driver: bridge
    driver_opts:
      com.docker.network.bridge.enable_ip_masquerade: "false"
"@
    [IO.File]::WriteAllText($composePath, $source)
    try {
        & docker compose --project-name $project -f $composePath up -d --wait --wait-timeout 60 | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Loopback ingress probe container did not become healthy." }
        $published = @(& docker compose --project-name $project -f $composePath port probe 3000)
        if ($LASTEXITCODE -ne 0 -or ($published -join "") -notmatch "127\.0\.0\.1:$port") {
            throw "Docker did not publish the frontend-only ingress bridge on loopback."
        }
        $client = [Net.Http.HttpClient]::new()
        $client.Timeout = [TimeSpan]::FromSeconds(10)
        try {
            $body = $client.GetStringAsync("http://127.0.0.1:$port").GetAwaiter().GetResult()
        } finally {
            $client.Dispose()
        }
        if ($body -ne "filingbridge-loopback-ok") {
            throw "Loopback ingress probe returned unexpected content."
        }
    } finally {
        $previousErrorActionPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = "Continue"
            & docker compose --project-name $project -f $composePath down --volumes --remove-orphans *> $null
        } finally {
            $ErrorActionPreference = $previousErrorActionPreference
        }
    }
}

try {
    $baseline = Invoke-Verifier $sourceCompose
    if ($baseline.ExitCode -ne 0) {
        throw "Baseline Private Server compose verification failed: $($baseline.Output)"
    }

    $publicApi = Write-MutatedCompose "public-api.yml" {
        param($source)
        $source -replace '(?m)^  api:\r?\n', "  api:`n    ports:`n      - `"0.0.0.0:8080:8080`"`n"
    }
    Assert-Fails "published API port" $publicApi "api must not publish a host port"

    $publicFrontend = Write-MutatedCompose "public-frontend.yml" {
        param($source)
        $source.Replace('"127.0.0.1:${PRIVATE_FRONTEND_PORT:-3500}:3000"', '"0.0.0.0:${PRIVATE_FRONTEND_PORT:-3500}:3000"')
    }
    Assert-Fails "non-loopback frontend" $publicFrontend "frontend must publish only 127.0.0.1:3500"

    $egressNetwork = Write-MutatedCompose "api-egress.yml" {
        param($source)
        $source -replace '(?m)^      - frontend_api\r?\n    secrets:', "      - frontend_api`n      - api_egress`n    secrets:" -replace '(?m)^  api_db:\r?\n    internal: true\r?\n', "  api_db:`n    internal: true`n  api_egress:`n    internal: false`n"
    }
    Assert-Fails "API egress network" $egressNetwork "api networks expected"

    $apiOnIngress = Write-MutatedCompose "api-on-ingress.yml" {
        param($source)
        $source -replace '(?m)^      - frontend_api\r?\n    secrets:', "      - frontend_api`n      - frontend_ingress`n    secrets:"
    }
    Assert-Fails "API on loopback ingress" $apiOnIngress "api networks expected"

    $sourceMount = Write-MutatedCompose "source-mount.yml" {
        param($source)
        $source -replace '(?m)^  api:\r?\n', "  api:`n    volumes:`n      - .:/app`n"
    }
    Assert-Fails "API source mount" $sourceMount "api must not bind-mount"

    $mutableImage = Write-MutatedCompose "mutable-image.yml" {
        param($source)
        $source.Replace('"${ACCOUNTS_FRONTEND_IMAGE:?set ACCOUNTS_FRONTEND_IMAGE to an immutable digest reference}"', '"ghcr.io/example/accounts-frontend:latest"')
    }
    Assert-Fails "mutable frontend image" $mutableImage "frontend image must be an immutable lowercase digest reference"

    $ownerPasswordLeak = Write-MutatedCompose "owner-password-leak.yml" {
        param($source)
        $source -replace '(?m)^      - accounts_application_connection_string\r?$', "      - accounts_application_connection_string`n      - private_initial_owner_password"
    }
    Assert-Fails "runtime Owner password" $ownerPasswordLeak "api must not receive the one-time initial Owner password"

    Test-LoopbackIngressNetwork

    Write-Host "Private Server compose verifier mutation and live loopback tests OK"
} finally {
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
