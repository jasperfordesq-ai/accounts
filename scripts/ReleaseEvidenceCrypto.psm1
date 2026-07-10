Set-StrictMode -Version Latest

function Resolve-AccountsOpenSsl {
    param([string]$ExplicitPath = "")

    $candidates = [System.Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        $candidates.Add($ExplicitPath)
    }

    $command = Get-Command openssl -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $command) {
        $candidates.Add($command.Source)
    }

    $git = Get-Command git -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $git -and -not [string]::IsNullOrWhiteSpace($git.Source)) {
        $gitDirectory = Split-Path -Parent $git.Source
        $gitRoot = Split-Path -Parent $gitDirectory
        $candidates.Add((Join-Path $gitRoot "usr\bin\openssl.exe"))
        $candidates.Add((Join-Path $gitRoot "mingw64\bin\openssl.exe"))
    }

    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        $candidates.Add((Join-Path $env:ProgramFiles "Git\usr\bin\openssl.exe"))
        $candidates.Add((Join-Path $env:ProgramFiles "Git\mingw64\bin\openssl.exe"))
    }

    foreach ($candidate in @($candidates | Select-Object -Unique)) {
        if ([string]::IsNullOrWhiteSpace($candidate)) {
            continue
        }

        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw "OpenSSL is required. Install it or pass -OpenSslPath. Git for Windows OpenSSL was also searched."
}

function Invoke-AccountsOpenSsl {
    param(
        [Parameter(Mandatory = $true)][string]$OpenSslPath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [string]$Context = "OpenSSL command"
    )

    # Windows PowerShell 5 surfaces native stderr as ErrorRecord objects when the
    # caller uses ErrorActionPreference=Stop. OpenSSL writes key-generation
    # progress to stderr even on success, so capture it without converting a
    # successful native exit into a terminating PowerShell error.
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $output = @(& $OpenSslPath @Arguments 2>&1)
        $exitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    if ($exitCode -ne 0) {
        $details = ($output | ForEach-Object { [string]$_ }) -join [Environment]::NewLine
        throw "$Context failed with exit code $exitCode. $details"
    }

    return $output | ForEach-Object { [string]$_ }
}

function Get-AccountsFileSha256 {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Cannot hash missing file '$Path'."
    }

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-AccountsCertificateFingerprint {
    param(
        [Parameter(Mandatory = $true)][string]$OpenSslPath,
        [Parameter(Mandatory = $true)][string]$CertificatePath
    )

    $output = (Invoke-AccountsOpenSsl $OpenSslPath @(
        "x509", "-in", $CertificatePath, "-noout", "-fingerprint", "-sha256"
    ) "Read certificate SHA-256 fingerprint") -join "`n"

    if ($output -notmatch "(?i)Fingerprint\s*=\s*([0-9A-F:]{95})") {
        throw "OpenSSL did not return a SHA-256 certificate fingerprint for '$CertificatePath'."
    }

    return $Matches[1].Replace(":", "").ToLowerInvariant()
}

function Get-AccountsCertificateSubject {
    param(
        [Parameter(Mandatory = $true)][string]$OpenSslPath,
        [Parameter(Mandatory = $true)][string]$CertificatePath
    )

    $output = (Invoke-AccountsOpenSsl $OpenSslPath @(
        "x509", "-in", $CertificatePath, "-noout", "-subject", "-nameopt", "RFC2253"
    ) "Read certificate subject") -join "`n"

    $subject = $output -replace "(?i)^subject\s*=\s*", ""
    $subject = $subject.Trim()
    if ([string]::IsNullOrWhiteSpace($subject)) {
        throw "OpenSSL returned an empty certificate subject for '$CertificatePath'."
    }

    return $subject
}

function Get-AccountsCertificateSerialNumber {
    param(
        [Parameter(Mandatory = $true)][string]$OpenSslPath,
        [Parameter(Mandatory = $true)][string]$CertificatePath
    )

    $output = (Invoke-AccountsOpenSsl $OpenSslPath @(
        "x509", "-in", $CertificatePath, "-noout", "-serial"
    ) "Read certificate serial number") -join "`n"

    $serial = ($output -replace "(?i)^serial\s*=\s*", "").Replace(":", "").Trim().ToLowerInvariant()
    if ($serial -notmatch "^[0-9a-f]+$") {
        throw "OpenSSL returned an invalid certificate serial number for '$CertificatePath'."
    }

    return $serial
}

function Write-AccountsUtf8NoBom {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Content
    )

    $encoding = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($Path, $Content, $encoding)
}

Export-ModuleMember -Function @(
    "Resolve-AccountsOpenSsl",
    "Invoke-AccountsOpenSsl",
    "Get-AccountsFileSha256",
    "Get-AccountsCertificateFingerprint",
    "Get-AccountsCertificateSubject",
    "Get-AccountsCertificateSerialNumber",
    "Write-AccountsUtf8NoBom"
)
