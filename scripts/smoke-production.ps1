param(
    [string]$BaseUrl = $env:ACCOUNTS_FRONTEND_URL,
    [string]$Email = $env:SMOKE_LOGIN_EMAIL,
    [string]$Password = $env:SMOKE_LOGIN_PASSWORD,
    [int]$CompanyId = 0,
    [int]$PeriodId = 0,
    [switch]$CheckDownloads,
    [string]$OutputDirectory = (Join-Path ([System.IO.Path]::GetTempPath()) "accounts-smoke"),
    [int]$TimeoutSeconds = 30,
    [int]$DownloadTimeoutSeconds = 120,
    [switch]$AllowInsecureHttp
)

$ErrorActionPreference = "Stop"

function Require-Value([string]$Value, [string]$Name) {
    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw "$Name is required."
    }
}

function Get-HeaderValue($Headers, [string]$Name) {
    $value = $Headers[$Name]
    if ($null -eq $value) {
        return ""
    }

    return [string](@($value) | Select-Object -First 1)
}

function Get-HeaderValues($Headers, [string]$Name) {
    if ($Headers -is [System.Net.WebHeaderCollection]) {
        $values = $Headers.GetValues($Name)
        if ($null -eq $values) {
            return @()
        }

        return @($values)
    }

    $values = $Headers[$Name]
    if ($null -eq $values) {
        return @()
    }

    return @($values)
}

function Test-ContainsIgnoreCase([string]$Value, [string]$Needle) {
    if ($null -eq $Value -or $null -eq $Needle) {
        return $false
    }

    return $Value.IndexOf($Needle, [StringComparison]::OrdinalIgnoreCase) -ge 0
}

function Assert-SetCookieAttribute($Response, [string]$CookieName, [string]$Attribute) {
    $setCookies = @(Get-HeaderValues -Headers $Response.Headers -Name "Set-Cookie")
    $cookie = $setCookies |
        Where-Object { $_.StartsWith("$CookieName=", [StringComparison]::OrdinalIgnoreCase) } |
        Select-Object -First 1

    if ([string]::IsNullOrWhiteSpace($cookie)) {
        throw "HTTPS login did not set cookie '$CookieName'."
    }

    $attributes = @($cookie.Split(";") | ForEach-Object { $_.Trim() })
    $hasAttribute = $attributes | Where-Object { $_.Equals($Attribute, [StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($hasAttribute)) {
        throw "Cookie '$CookieName' from HTTPS login did not include '$Attribute': $cookie"
    }
}

function Assert-SecurityHeader($Response, [string]$Name, [string]$ExpectedText) {
    $value = Get-HeaderValue -Headers $Response.Headers -Name $Name
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "Missing security header '$Name'."
    }
    if (-not (Test-ContainsIgnoreCase -Value $value -Needle $ExpectedText)) {
        throw "Security header '$Name' had unexpected value '$value'. Expected to contain '$ExpectedText'."
    }
}

function Get-CspDirectives([string]$Policy) {
    $directives = @{}
    foreach ($part in $Policy.Split(";")) {
        $trimmed = $part.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed)) {
            continue
        }

        $tokens = $trimmed -split "\s+"
        if ($tokens.Count -eq 0) {
            continue
        }

        $name = $tokens[0].ToLowerInvariant()
        $directives[$name] = @($tokens | Select-Object -Skip 1)
    }

    return $directives
}

function Get-CspDirective($Directives, [string]$Name) {
    $key = $Name.ToLowerInvariant()
    if (-not $Directives.ContainsKey($key)) {
        return @()
    }

    return @($Directives[$key])
}

function Assert-ContentSecurityPolicy($Response, [switch]$AllowUnsafeInlineScripts) {
    $value = Get-HeaderValue -Headers $Response.Headers -Name "Content-Security-Policy"
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "Missing security header 'Content-Security-Policy'."
    }

    $directives = Get-CspDirectives -Policy $value
    $scriptSrc = @(Get-CspDirective -Directives $directives -Name "script-src")
    if ($scriptSrc.Count -eq 0) {
        throw "Content-Security-Policy is missing script-src: '$value'."
    }
    if ($scriptSrc -notcontains "'self'") {
        throw "Content-Security-Policy script-src is missing 'self': '$value'."
    }
    if ($scriptSrc -notcontains "'strict-dynamic'") {
        throw "Content-Security-Policy script-src is missing 'strict-dynamic': '$value'."
    }

    $nonceTokens = @($scriptSrc | Where-Object { $_ -match "^'nonce-.+'$" })
    if ($nonceTokens.Count -eq 0) {
        throw "Content-Security-Policy script-src is missing a nonce: '$value'."
    }

    if (-not $AllowUnsafeInlineScripts) {
        if ($scriptSrc -contains "'unsafe-inline'") {
            throw "Content-Security-Policy script-src allows unsafe-inline scripts in production: '$value'."
        }
        if ($scriptSrc -contains "'unsafe-eval'") {
            throw "Content-Security-Policy script-src allows unsafe-eval scripts in production: '$value'."
        }
        $normalizedScriptSrc = "script-src " + ($scriptSrc -join " ")
        if (Test-ContainsIgnoreCase -Value $normalizedScriptSrc -Needle "script-src 'self' 'unsafe-inline'") {
            throw "Content-Security-Policy regressed to unsafe inline script-src: '$value'."
        }
    }

    $scriptSrcAttr = @(Get-CspDirective -Directives $directives -Name "script-src-attr")
    if ($scriptSrcAttr.Count -ne 1 -or $scriptSrcAttr[0] -ne "'none'") {
        throw "Content-Security-Policy script-src-attr must be exactly 'none': '$value'."
    }

    $frameAncestors = @(Get-CspDirective -Directives $directives -Name "frame-ancestors")
    if ($frameAncestors.Count -ne 1 -or $frameAncestors[0] -ne "'none'") {
        throw "Content-Security-Policy frame-ancestors must be exactly 'none': '$value'."
    }

    $nonce = $nonceTokens[0].Substring("'nonce-".Length).TrimEnd("'")
    if ([string]::IsNullOrWhiteSpace($Response.Content) -or -not (Test-ContainsIgnoreCase -Value $Response.Content -Needle "theme-init.js")) {
        throw "Response HTML did not include the theme-init.js bootstrap script."
    }
    if (-not (Test-ContainsIgnoreCase -Value $Response.Content -Needle "nonce=`"$nonce`"")) {
        throw "Response HTML did not include the CSP nonce on the theme-init.js script."
    }

    return $nonce
}

function Get-CsrfToken([Microsoft.PowerShell.Commands.WebRequestSession]$Session, [Uri]$BaseUri) {
    $cookie = $Session.Cookies.GetCookies($BaseUri) | Where-Object { $_.Name -eq "accounts_csrf" } | Select-Object -First 1
    if ($null -eq $cookie -or [string]::IsNullOrWhiteSpace($cookie.Value)) {
        throw "accounts_csrf cookie was not set after login."
    }

    return $cookie.Value
}

function Invoke-SmokeDownload(
    [string]$Url,
    [string]$OutputPath,
    [Microsoft.PowerShell.Commands.WebRequestSession]$Session,
    [int]$Timeout,
    [string]$ExpectedContentType,
    [string]$ExpectedText
) {
    $response = Invoke-WebRequest `
        -Uri $Url `
        -Method Get `
        -UseBasicParsing `
        -WebSession $Session `
        -OutFile $OutputPath `
        -PassThru `
        -TimeoutSec $Timeout

    $contentType = ($response.Headers["Content-Type"] | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($contentType) -or -not $contentType.StartsWith($ExpectedContentType, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unexpected content type '$contentType' from $Url. Expected $ExpectedContentType."
    }

    $item = Get-Item -LiteralPath $OutputPath
    if ($item.Length -le 0) {
        throw "Downloaded file is empty: $OutputPath"
    }

    $prefixLength = [Math]::Min([int]$item.Length, 4096)
    $buffer = [byte[]]::new($prefixLength)
    $stream = [System.IO.File]::OpenRead($OutputPath)
    try {
        [void]$stream.Read($buffer, 0, $buffer.Length)
    } finally {
        $stream.Dispose()
    }

    $prefix = [System.Text.Encoding]::UTF8.GetString($buffer)
    if (-not (Test-ContainsIgnoreCase -Value $prefix -Needle $ExpectedText)) {
        throw "Downloaded file did not contain expected marker '$ExpectedText': $OutputPath"
    }
}

Require-Value $BaseUrl "ACCOUNTS_FRONTEND_URL or -BaseUrl"
Require-Value $Email "SMOKE_LOGIN_EMAIL or -Email"
Require-Value $Password "SMOKE_LOGIN_PASSWORD or -Password"

if ($TimeoutSeconds -le 0) {
    throw "TimeoutSeconds must be greater than zero."
}
if ($DownloadTimeoutSeconds -le 0) {
    throw "DownloadTimeoutSeconds must be greater than zero."
}
if ($CheckDownloads -and ($CompanyId -le 0 -or $PeriodId -le 0)) {
    throw "-CheckDownloads requires -CompanyId and -PeriodId."
}

try {
    $baseUri = [Uri]$BaseUrl
} catch {
    throw "ACCOUNTS_FRONTEND_URL or -BaseUrl must be an absolute URL."
}

if ($baseUri.Scheme -notin @("http", "https")) {
    throw "ACCOUNTS_FRONTEND_URL or -BaseUrl must use http or https."
}
if ($baseUri.Scheme -eq "http" -and -not $AllowInsecureHttp) {
    throw "ACCOUNTS_FRONTEND_URL or -BaseUrl must use https. AllowInsecureHttp is only for local dry runs against a non-production host."
}

$base = $baseUri.AbsoluteUri.TrimEnd("/")
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession

Write-Host "Checking frontend and upstream readiness..."
$readyResponse = Invoke-WebRequest `
    -Uri "$base/health/ready" `
    -Method Get `
    -UseBasicParsing `
    -WebSession $session `
    -TimeoutSec $TimeoutSeconds
if (-not $AllowInsecureHttp) {
    Assert-SecurityHeader -Response $readyResponse -Name "Strict-Transport-Security" -ExpectedText "max-age=31536000"
}

Write-Host "Checking frontend security headers..."
$homeResponse = Invoke-WebRequest `
    -Uri "$base/" `
    -Method Get `
    -UseBasicParsing `
    -WebSession $session `
    -TimeoutSec $TimeoutSeconds

$firstCspNonce = Assert-ContentSecurityPolicy -Response $homeResponse -AllowUnsafeInlineScripts:$AllowInsecureHttp
$secondHomeResponse = Invoke-WebRequest `
    -Uri "$base/" `
    -Method Get `
    -UseBasicParsing `
    -WebSession $session `
    -TimeoutSec $TimeoutSeconds
$secondCspNonce = Assert-ContentSecurityPolicy -Response $secondHomeResponse -AllowUnsafeInlineScripts:$AllowInsecureHttp
if ($firstCspNonce -eq $secondCspNonce) {
    throw "CSP nonce was reused across two homepage requests."
}
Assert-SecurityHeader -Response $homeResponse -Name "X-Frame-Options" -ExpectedText "DENY"
Assert-SecurityHeader -Response $homeResponse -Name "X-Content-Type-Options" -ExpectedText "nosniff"
Assert-SecurityHeader -Response $homeResponse -Name "Referrer-Policy" -ExpectedText "no-referrer"
Assert-SecurityHeader -Response $homeResponse -Name "Permissions-Policy" -ExpectedText "camera=()"
if (-not $AllowInsecureHttp) {
    Assert-SecurityHeader -Response $homeResponse -Name "Strict-Transport-Security" -ExpectedText "max-age=31536000"
}

Write-Host "Signing in through frontend proxy..."
$loginBody = @{
    email = $Email
    password = $Password
} | ConvertTo-Json
$loginResponse = Invoke-WebRequest `
    -Uri "$base/api/auth/login" `
    -Method Post `
    -ContentType "application/json" `
    -Body $loginBody `
    -UseBasicParsing `
    -WebSession $session `
    -TimeoutSec $TimeoutSeconds

if (-not $AllowInsecureHttp) {
    Assert-SecurityHeader -Response $loginResponse -Name "Strict-Transport-Security" -ExpectedText "max-age=31536000"
    Assert-SetCookieAttribute -Response $loginResponse -CookieName "accounts_session" -Attribute "Secure"
    Assert-SetCookieAttribute -Response $loginResponse -CookieName "accounts_csrf" -Attribute "Secure"
}

$csrfToken = Get-CsrfToken -Session $session -BaseUri $baseUri

Write-Host "Checking authenticated session..."
$currentUserResponse = Invoke-WebRequest `
    -Uri "$base/api/auth/me" `
    -Method Get `
    -UseBasicParsing `
    -WebSession $session `
    -TimeoutSec $TimeoutSeconds
if (-not $AllowInsecureHttp) {
    Assert-SecurityHeader -Response $currentUserResponse -Name "Strict-Transport-Security" -ExpectedText "max-age=31536000"
}
$currentUser = $currentUserResponse.Content | ConvertFrom-Json

if ([string]::IsNullOrWhiteSpace($currentUser.email)) {
    throw "Authenticated /api/auth/me response did not include an email."
}

Write-Host "Checking company list through frontend proxy..."
Invoke-RestMethod `
    -Uri "$base/api/companies" `
    -Method Get `
    -WebSession $session `
    -TimeoutSec $TimeoutSeconds | Out-Null

if ($CheckDownloads) {
    New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
    $accountsPackage = Join-Path $OutputDirectory "accounts-package-smoke.pdf"
    $ixbrlPackage = Join-Path $OutputDirectory "ixbrl-smoke.xhtml"

    Write-Host "Downloading sample accounts package..."
    Invoke-SmokeDownload `
        -Url "$base/api/companies/$CompanyId/periods/$PeriodId/documents/accounts-package" `
        -OutputPath $accountsPackage `
        -Session $session `
        -Timeout $DownloadTimeoutSeconds `
        -ExpectedContentType "application/pdf" `
        -ExpectedText "%PDF"

    Write-Host "Downloading sample iXBRL package..."
    Invoke-SmokeDownload `
        -Url "$base/api/companies/$CompanyId/periods/$PeriodId/revenue/ixbrl" `
        -OutputPath $ixbrlPackage `
        -Session $session `
        -Timeout $DownloadTimeoutSeconds `
        -ExpectedContentType "application/xhtml+xml" `
        -ExpectedText "xmlns"
}

Write-Host "Checking CSRF-protected logout..."
Invoke-RestMethod `
    -Uri "$base/api/auth/logout" `
    -Method Post `
    -ContentType "application/json" `
    -Headers @{ "X-CSRF-Token" = $csrfToken } `
    -Body "{}" `
    -WebSession $session `
    -TimeoutSec $TimeoutSeconds | Out-Null

Write-Host "Checking session is cleared after logout..."
$postLogoutStatusCode = $null
try {
    $postLogoutResponse = Invoke-WebRequest `
        -Uri "$base/api/auth/me" `
        -Method Get `
        -UseBasicParsing `
        -WebSession $session `
        -TimeoutSec $TimeoutSeconds
    $postLogoutStatusCode = [int]$postLogoutResponse.StatusCode
} catch {
    if ($_.Exception.Response -and $_.Exception.Response.StatusCode) {
        $postLogoutStatusCode = [int]$_.Exception.Response.StatusCode
    } else {
        throw
    }
}

if ($postLogoutStatusCode -ne [int][System.Net.HttpStatusCode]::Unauthorized) {
    throw "Expected /api/auth/me to be unauthorized after logout, got HTTP $postLogoutStatusCode."
}

Write-Host "Production smoke check completed."
