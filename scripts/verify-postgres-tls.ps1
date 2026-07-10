param(
    [string]$ComposeFile = "compose.production.yml",
    [string]$Service = "db",
    [string]$User = $env:POSTGRES_USER,
    [string]$Database = $env:POSTGRES_DB,
    [string]$PasswordFile = $env:POSTGRES_PASSWORD_FILE,
    [string]$ConnectionStringFile = $env:ACCOUNTS_MIGRATION_CONNECTION_STRING_FILE,
    [string]$ServerCertificateFile = $env:POSTGRES_SERVER_CERTIFICATE_FILE,
    [string]$CaCertificateFile = $env:POSTGRES_CA_CERTIFICATE_FILE,
    [string]$CommitSha = $env:GITHUB_SHA,
    [string]$GitHubActionsRunUrl = "",
    [Parameter(Mandatory = $true)]
    [string]$EvidencePath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Assert-SafePostgresIdentifier([string]$Value, [string]$Name) {
    if ($Value -notmatch '^[A-Za-z_][A-Za-z0-9_]{0,62}$') {
        throw "$Name must be a PostgreSQL identifier using letters, numbers, and underscores."
    }
}

function Invoke-NativeCapture([scriptblock]$Command, [switch]$AllowFailure) {
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = @(& $Command 2>&1)
        $exitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previousPreference
    }

    if (-not $AllowFailure -and $exitCode -ne 0) {
        throw "Native command failed with exit code $exitCode."
    }

    return [pscustomobject]@{
        ExitCode = $exitCode
        Lines = @($output | ForEach-Object { [string]$_ })
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

    throw "OpenSSL is required to inspect the PostgreSQL server certificate."
}

foreach ($required in @(
    @{ Name = "POSTGRES_USER or -User"; Value = $User },
    @{ Name = "POSTGRES_DB or -Database"; Value = $Database },
    @{ Name = "POSTGRES_PASSWORD_FILE or -PasswordFile"; Value = $PasswordFile },
    @{ Name = "ACCOUNTS_MIGRATION_CONNECTION_STRING_FILE or -ConnectionStringFile"; Value = $ConnectionStringFile },
    @{ Name = "POSTGRES_SERVER_CERTIFICATE_FILE or -ServerCertificateFile"; Value = $ServerCertificateFile },
    @{ Name = "POSTGRES_CA_CERTIFICATE_FILE or -CaCertificateFile"; Value = $CaCertificateFile })) {
    if ([string]::IsNullOrWhiteSpace([string]$required.Value)) {
        throw "$($required.Name) is required."
    }
}

Assert-SafePostgresIdentifier $User "User"
Assert-SafePostgresIdentifier $Database "Database"
foreach ($path in @($PasswordFile, $ConnectionStringFile, $ServerCertificateFile, $CaCertificateFile)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required PostgreSQL TLS evidence input was not found: $path"
    }
}

$connectionString = (Get-Content -LiteralPath $ConnectionStringFile -Raw).Trim()
foreach ($requirement in @(
    @{ Pattern = '(?i)(?:^|;)\s*Host\s*=\s*db\s*(?:;|$)'; Message = "Host=db" },
    @{ Pattern = '(?i)(?:^|;)\s*SSL\s+Mode\s*=\s*VerifyFull\s*(?:;|$)'; Message = "SSL Mode=VerifyFull" },
    @{ Pattern = '(?i)(?:^|;)\s*Root\s+Certificate\s*=\s*/run/secrets/postgres_ca_certificate\s*(?:;|$)'; Message = "the mounted PostgreSQL CA path" },
    @{ Pattern = '(?i)(?:^|;)\s*Trust\s+Server\s+Certificate\s*=\s*false\s*(?:;|$)'; Message = "Trust Server Certificate=false" })) {
    if ($connectionString -notmatch $requirement.Pattern) {
        throw "Production connection string must contain $($requirement.Message)."
    }
}

$password = (Get-Content -LiteralPath $PasswordFile -Raw).TrimEnd("`r", "`n")
if ([string]::IsNullOrWhiteSpace($password)) {
    throw "PostgreSQL password file is empty."
}

$verifiedConnection = "host=db port=5432 dbname=$Database user=$User sslmode=verify-full sslrootcert=/run/secrets/postgres_ca_certificate connect_timeout=10"
$sql = "SELECT ssl::text, version, cipher, bits FROM pg_stat_ssl WHERE pid = pg_backend_pid();"
$query = Invoke-NativeCapture {
    docker compose -f $ComposeFile exec -T -e "PGPASSWORD=$password" $Service `
        psql $verifiedConnection --no-align --tuples-only --field-separator "|" --command $sql
}
$proofLine = @($query.Lines | Where-Object { $_ -match '^(?:true|t)\|TLSv[0-9.]+\|[^|]+\|[0-9]+$' } | Select-Object -Last 1)
if ($proofLine.Count -ne 1) {
    throw "PostgreSQL TLS query did not return one parseable encrypted-session row."
}
$proofParts = $proofLine[0] -split '\|', 4

# hostaddr fixes the TCP destination while an intentionally-wrong host value drives certificate
# identity validation. The query must fail specifically because verify-full rejects the hostname.
$mismatchConnection = "host=not-db hostaddr=127.0.0.1 port=5432 dbname=$Database user=$User sslmode=verify-full sslrootcert=/run/secrets/postgres_ca_certificate connect_timeout=10"
$mismatch = Invoke-NativeCapture -AllowFailure {
    docker compose -f $ComposeFile exec -T -e "PGPASSWORD=$password" $Service `
        psql $mismatchConnection --no-align --tuples-only --command "SELECT 1;"
}
$password = $null
$mismatchText = $mismatch.Lines -join "`n"
if ($mismatch.ExitCode -eq 0 -or $mismatchText -notmatch '(?i)(certificate.*does not match|hostname mismatch|host name.*not-db)') {
    throw "PostgreSQL verify-full did not prove rejection of an incorrect server hostname."
}

$openssl = Resolve-OpenSsl
$certificateInspection = Invoke-NativeCapture {
    & $openssl x509 -in $ServerCertificateFile -noout -sha256 -fingerprint -serial -subject -issuer -dates -ext subjectAltName
}
$inspectionText = $certificateInspection.Lines -join "`n"
if ($inspectionText -notmatch '(?i)DNS:db(?:\s|,|$)') {
    throw "PostgreSQL server certificate SAN must include DNS:db."
}
$certificateExpiry = Invoke-NativeCapture -AllowFailure {
    & $openssl x509 -in $ServerCertificateFile -noout -checkend 0
}
if ($certificateExpiry.ExitCode -ne 0) {
    throw "PostgreSQL server certificate is expired or not currently valid."
}

$containerIdResult = Invoke-NativeCapture {
    docker compose -f $ComposeFile ps -q $Service
}
$containerId = ($containerIdResult.Lines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Last 1).Trim()
if ($containerId -notmatch '^[0-9a-f]{12,64}$') {
    throw "Could not resolve the running PostgreSQL container id."
}
$containerImageResult = Invoke-NativeCapture {
    docker inspect $containerId --format '{{.Config.Image}}|{{.Image}}'
}
$containerImage = ($containerImageResult.Lines | Where-Object { $_ -match '\|' } | Select-Object -Last 1).Trim() -split '\|', 2

$fingerprintLine = [regex]::Match($inspectionText, '(?im)^sha256 Fingerprint=(?<value>[0-9A-F:]+)\s*$')
$serialLine = [regex]::Match($inspectionText, '(?im)^serial=(?<value>[0-9A-F]+)\s*$')
$notBeforeLine = [regex]::Match($inspectionText, '(?im)^notBefore=(?<value>.+)\s*$')
$notAfterLine = [regex]::Match($inspectionText, '(?im)^notAfter=(?<value>.+)\s*$')
if (-not ($fingerprintLine.Success -and $serialLine.Success -and $notBeforeLine.Success -and $notAfterLine.Success)) {
    throw "OpenSSL certificate inspection did not return fingerprint, serial, and validity fields."
}

$evidenceDirectory = Split-Path -Parent ([IO.Path]::GetFullPath($EvidencePath))
if (-not [string]::IsNullOrWhiteSpace($evidenceDirectory)) {
    New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null
}

[ordered]@{
    status = "passed"
    checkedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    releaseCandidate = [ordered]@{
        commitSha = $CommitSha
        githubActionsRunUrl = $GitHubActionsRunUrl
    }
    connectionPolicy = [ordered]@{
        host = "db"
        sslMode = "VerifyFull"
        rootCertificate = "/run/secrets/postgres_ca_certificate"
        trustServerCertificate = $false
        connectionStringSecretInspectedWithoutRetention = $true
    }
    runtimeSession = [ordered]@{
        ssl = $true
        protocol = $proofParts[1]
        cipher = $proofParts[2]
        cipherBits = [int]$proofParts[3]
        hostnameMismatchRejected = $true
        database = $Database
        service = $Service
        containerImageReference = $containerImage[0]
        containerImageId = $containerImage[1]
    }
    certificate = [ordered]@{
        serverCertificateFileSha256 = (Get-FileHash -LiteralPath $ServerCertificateFile -Algorithm SHA256).Hash.ToLowerInvariant()
        caCertificateFileSha256 = (Get-FileHash -LiteralPath $CaCertificateFile -Algorithm SHA256).Hash.ToLowerInvariant()
        certificateFingerprintSha256 = $fingerprintLine.Groups["value"].Value.Replace(":", "").ToLowerInvariant()
        serial = $serialLine.Groups["value"].Value.ToLowerInvariant()
        subjectAlternativeName = "DNS:db"
        notBefore = $notBeforeLine.Groups["value"].Value.Trim()
        notAfter = $notAfterLine.Groups["value"].Value.Trim()
        currentlyValid = $true
    }
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $EvidencePath -Encoding UTF8

Write-Host "PostgreSQL certificate-verified TLS evidence written: $EvidencePath"
