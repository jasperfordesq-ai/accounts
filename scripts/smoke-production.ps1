param(
    [string]$BaseUrl = $env:ACCOUNTS_FRONTEND_URL,
    [string]$TenantSlug = $env:SMOKE_TENANT_SLUG,
    [string]$Email = $env:SMOKE_LOGIN_EMAIL,
    [string]$Password = $env:SMOKE_LOGIN_PASSWORD,
    [string]$NewPassword = $env:SMOKE_NEW_PASSWORD,
    [string]$TotpSecret = $env:SMOKE_TOTP_SECRET,
    [int]$CompanyId = 0,
    [int]$PeriodId = 0,
    [switch]$CheckDownloads,
    [switch]$CheckMonitoringErrorRouting,
    [switch]$AllowEphemeralMfaEnrollment,
    [string]$EphemeralMfaHandoffPath = $env:SMOKE_EPHEMERAL_MFA_HANDOFF_PATH,
    [switch]$AllowRetainedMfaEnrollment,
    [string]$RetainedMfaHandoffPath = $env:SMOKE_RETAINED_MFA_HANDOFF_PATH,
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

function Import-LoopbackSecureCookies(
    $Response,
    [Microsoft.PowerShell.Commands.WebRequestSession]$Session,
    [Uri]$BaseUri
) {
    if ($BaseUri.Scheme -ne "http" -or -not $BaseUri.IsLoopback) {
        throw "Secure-cookie loopback import is restricted to an HTTP loopback URL."
    }

    $setCookies = @(Get-HeaderValues -Headers $Response.Headers -Name "Set-Cookie")
    $secureUriBuilder = [UriBuilder]::new($BaseUri)
    $secureUriBuilder.Scheme = "https"
    $secureUri = $secureUriBuilder.Uri
    $parsedCookies = [System.Net.CookieContainer]::new()
    foreach ($header in $setCookies) {
        try { $parsedCookies.SetCookies($secureUri, [string]$header) } catch {
            throw "Loopback login returned malformed Set-Cookie evidence."
        }
    }
    foreach ($cookieName in @("accounts_session", "accounts_csrf")) {
        $parsed = $parsedCookies.GetCookies($secureUri)[$cookieName]
        if ($null -eq $parsed -or [string]::IsNullOrWhiteSpace($parsed.Value)) {
            throw "Loopback login did not return cookie '$cookieName'."
        }
        if (-not $parsed.Secure) {
            throw "Loopback login cookie '$cookieName' was not protected with Secure by the server."
        }
        $cookie = [System.Net.Cookie]::new($cookieName, $parsed.Value, "/", $BaseUri.Host)
        $cookie.HttpOnly = $cookieName -eq "accounts_session"
        $cookie.Secure = $false
        $Session.Cookies.Add($BaseUri, $cookie)
    }
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
Require-Value $TenantSlug "SMOKE_TENANT_SLUG or -TenantSlug"
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
if ($AllowEphemeralMfaEnrollment -and $AllowRetainedMfaEnrollment) {
    throw "Ephemeral and retained MFA enrollment modes cannot be combined."
}
if ($AllowRetainedMfaEnrollment -and [string]::IsNullOrWhiteSpace($RetainedMfaHandoffPath)) {
    throw "-AllowRetainedMfaEnrollment requires -RetainedMfaHandoffPath or SMOKE_RETAINED_MFA_HANDOFF_PATH."
}
$ownerWorkflowReportTarget = [IO.Path]::GetFullPath((Join-Path $OutputDirectory "owner-workflow-report.json"))
if (Test-Path -LiteralPath $ownerWorkflowReportTarget) {
    throw "Owner workflow report already exists at $ownerWorkflowReportTarget; no login or account mutation was attempted. Use a new output directory."
}

function ConvertFrom-Base32([string]$Value) {
    $alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"
    $normalized = ($Value -replace '[\s=-]', '').ToUpperInvariant()
    if ([string]::IsNullOrWhiteSpace($normalized)) {
        throw "The MFA TOTP secret is empty."
    }

    $bytes = [System.Collections.Generic.List[byte]]::new()
    [int64]$buffer = 0
    $bits = 0
    foreach ($character in $normalized.ToCharArray()) {
        $index = $alphabet.IndexOf($character)
        if ($index -lt 0) {
            throw "The MFA TOTP secret is not valid Base32."
        }
        $buffer = ($buffer -shl 5) -bor $index
        $bits += 5
        while ($bits -ge 8) {
            $bits -= 8
            $bytes.Add([byte](($buffer -shr $bits) -band 0xff))
            if ($bits -eq 0) {
                $buffer = 0
            } else {
                $buffer = $buffer -band (([int64]1 -shl $bits) - 1)
            }
        }
    }
    return $bytes.ToArray()
}

function Get-TotpCounter([DateTimeOffset]$At = [DateTimeOffset]::UtcNow) {
    return [int64][Math]::Floor($At.ToUnixTimeSeconds() / 30)
}

function New-TotpCode([string]$Secret, [DateTimeOffset]$At = [DateTimeOffset]::UtcNow) {
    $key = ConvertFrom-Base32 $Secret
    [int64]$counter = Get-TotpCounter $At
    $counterBytes = [BitConverter]::GetBytes($counter)
    if ([BitConverter]::IsLittleEndian) {
        [Array]::Reverse($counterBytes)
    }
    $hmac = [System.Security.Cryptography.HMACSHA1]::new($key)
    try {
        $digest = $hmac.ComputeHash($counterBytes)
    } finally {
        $hmac.Dispose()
        [Array]::Clear($key, 0, $key.Length)
    }
    $offset = $digest[$digest.Length - 1] -band 0x0f
    $binary = (($digest[$offset] -band 0x7f) -shl 24) -bor
        (($digest[$offset + 1] -band 0xff) -shl 16) -bor
        (($digest[$offset + 2] -band 0xff) -shl 8) -bor
        ($digest[$offset + 3] -band 0xff)
    return ($binary % 1000000).ToString("D6", [Globalization.CultureInfo]::InvariantCulture)
}

function Write-EphemeralMfaHandoff([string]$Path, [string]$Secret, [int64]$LastAcceptedCounter) {
    if ([string]::IsNullOrWhiteSpace($Path)) {
        return
    }
    if (-not $AllowEphemeralMfaEnrollment) {
        throw "An MFA handoff is allowed only with -AllowEphemeralMfaEnrollment on a disposable candidate stack."
    }
    if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) {
        throw "EphemeralMfaHandoffPath requires RUNNER_TEMP so the secret cannot escape runner-temporary storage."
    }

    $runnerRoot = [System.IO.Path]::GetFullPath($env:RUNNER_TEMP).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $runningOnWindows = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
    $pathComparison = if ($runningOnWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    if (-not $fullPath.StartsWith($runnerRoot, $pathComparison)) {
        throw "EphemeralMfaHandoffPath must remain inside RUNNER_TEMP."
    }
    $runnerRootItem = Get-Item -LiteralPath $runnerRoot.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) -Force
    if (($runnerRootItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "RUNNER_TEMP must not be a filesystem link or junction for an MFA handoff."
    }

    $directory = Split-Path -Parent $fullPath
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $runnerRootWithoutSeparator = $runnerRoot.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $relativeDirectory = $directory.Substring($runnerRootWithoutSeparator.Length).TrimStart(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $currentDirectory = $runnerRootWithoutSeparator
    foreach ($segment in $relativeDirectory.Split(
        [char[]]@([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar),
        [System.StringSplitOptions]::RemoveEmptyEntries)) {
        $currentDirectory = Join-Path $currentDirectory $segment
        $directoryItem = Get-Item -LiteralPath $currentDirectory -Force
        if (($directoryItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "EphemeralMfaHandoffPath must not traverse a filesystem link or junction."
        }
    }
    $payload = @{
        schemaVersion = "accounts-visual-mfa-handoff-v1"
        secret = $Secret
        lastAcceptedCounter = $LastAcceptedCounter
    } | ConvertTo-Json -Compress
    $payloadBytes = [System.Text.UTF8Encoding]::new($false).GetBytes($payload)
    $created = $false
    try {
        if ($runningOnWindows) {
            $stream = [System.IO.File]::Open(
                $fullPath,
                [System.IO.FileMode]::CreateNew,
                [System.IO.FileAccess]::Write,
                [System.IO.FileShare]::None)
        } else {
            $expectedMode = [System.IO.UnixFileMode]::UserRead -bor [System.IO.UnixFileMode]::UserWrite
            $options = [System.IO.FileStreamOptions]::new()
            $options.Access = [System.IO.FileAccess]::Write
            $options.Mode = [System.IO.FileMode]::CreateNew
            $options.Share = [System.IO.FileShare]::None
            $options.Options = [System.IO.FileOptions]::WriteThrough
            $options.UnixCreateMode = $expectedMode
            $stream = [System.IO.FileStream]::new($fullPath, $options)
        }
        $created = $true
        try {
            $stream.Write($payloadBytes, 0, $payloadBytes.Length)
            $stream.Flush($true)
        } finally {
            $stream.Dispose()
        }
    } catch {
        if ($created) {
            Remove-Item -LiteralPath $fullPath -Force -ErrorAction SilentlyContinue
        }
        throw
    } finally {
        [Array]::Clear($payloadBytes, 0, $payloadBytes.Length)
    }

    if (-not $runningOnWindows) {
        $mode = [System.IO.File]::GetUnixFileMode($fullPath)
        $expectedMode = [System.IO.UnixFileMode]::UserRead -bor [System.IO.UnixFileMode]::UserWrite
        if ($mode -ne $expectedMode) {
            Remove-Item -LiteralPath $fullPath -Force -ErrorAction SilentlyContinue
            throw "The ephemeral MFA handoff did not retain mode 0600."
        }
    }
}

function Reserve-OwnerWorkflowReport([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    $reservation = "$resolved.reserved"
    $parent = Split-Path -Parent $resolved
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    if (Test-Path -LiteralPath $resolved) {
        throw "Owner workflow report already exists at $resolved; no login or account mutation was attempted. Use a new output directory."
    }
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes(("reservedAtUtc={0}{1}" -f [DateTimeOffset]::UtcNow.ToString("o"), [Environment]::NewLine))
    try {
        if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
            $stream = [IO.File]::Open($reservation, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        } else {
            $options = [IO.FileStreamOptions]::new()
            $options.Access = [IO.FileAccess]::Write
            $options.Mode = [IO.FileMode]::CreateNew
            $options.Share = [IO.FileShare]::None
            $options.Options = [IO.FileOptions]::WriteThrough
            $options.UnixCreateMode = [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite
            $stream = [IO.FileStream]::new($reservation, $options)
        }
        try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
    } catch {
        throw "Owner workflow evidence directory is already reserved by another or interrupted run; no login or account mutation was attempted: $reservation"
    } finally {
        [Array]::Clear($bytes, 0, $bytes.Length)
    }
    if (Test-Path -LiteralPath $resolved) {
        Remove-Item -LiteralPath $reservation -Force -ErrorAction SilentlyContinue
        throw "Owner workflow report appeared while its path was being reserved; no login or account mutation was attempted. Use a new output directory."
    }
    return $reservation
}

function Write-OwnerWorkflowReport([string]$Path, [string]$ReservationPath, $Report) {
    $resolved = [IO.Path]::GetFullPath($Path)
    $resolvedReservation = [IO.Path]::GetFullPath($ReservationPath)
    if ($resolvedReservation -cne "$resolved.reserved" -or -not (Test-Path -LiteralPath $resolvedReservation -PathType Leaf)) {
        throw "Owner workflow report reservation is missing or does not match the evidence target."
    }
    $parent = Split-Path -Parent $resolved
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    if (Test-Path -LiteralPath $resolved) {
        throw "Owner workflow report already exists at $resolved; no evidence was replaced."
    }
    $json = ($Report | ConvertTo-Json -Depth 8) + [Environment]::NewLine
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($json)
    $stream = [IO.File]::Open($resolved, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.Flush($true)
    } finally {
        $stream.Dispose()
    }
    try {
        if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
            $acl = New-Object Security.AccessControl.FileSecurity
            $acl.SetAccessRuleProtection($true, $false)
            $identity = [Security.Principal.WindowsIdentity]::GetCurrent().User
            $rule = New-Object Security.AccessControl.FileSystemAccessRule(
                $identity,
                [Security.AccessControl.FileSystemRights]::FullControl,
                [Security.AccessControl.AccessControlType]::Allow)
            $acl.SetOwner($identity)
            $acl.SetAccessRule($rule)
            [IO.File]::SetAccessControl($resolved, $acl)
        } else {
            [IO.File]::SetUnixFileMode($resolved, [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite)
        }
    } catch {
        Remove-Item -LiteralPath $resolved -Force -ErrorAction SilentlyContinue
        throw
    }
    Remove-Item -LiteralPath $resolvedReservation -Force
    return $resolved
}

function Reserve-RetainedMfaHandoff([string]$Path) {
    if (-not $AllowRetainedMfaEnrollment) {
        throw "A retained MFA handoff requires -AllowRetainedMfaEnrollment."
    }
    $resolved = [IO.Path]::GetFullPath($Path)
    $parent = Split-Path -Parent $resolved
    if (Test-Path -LiteralPath $resolved) { throw "Retained MFA handoff already exists at $resolved; no secret was replaced." }
    if (Test-Path -LiteralPath $parent) {
        throw "Retained MFA handoff requires a new dedicated parent directory so its ACL can be established before any secret is written: $parent"
    }
    $fullParent = [IO.Path]::GetFullPath($parent)
    $root = [IO.Path]::GetPathRoot($fullParent)
    $current = $root
    foreach ($segment in $fullParent.Substring($root.Length).Split([char[]]@('\', '/'), [StringSplitOptions]::RemoveEmptyEntries)) {
        $current = Join-Path $current $segment
        if (-not (Test-Path -LiteralPath $current)) { continue }
        $item = Get-Item -LiteralPath $current -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Retained MFA handoff must not traverse a filesystem link or junction: $current"
        }
    }
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
        New-Item -ItemType Directory -Path $parent | Out-Null
    } else {
        $directoryMode = [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite -bor [IO.UnixFileMode]::UserExecute
        [IO.Directory]::CreateDirectory($parent, $directoryMode) | Out-Null
    }
    try {
        if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
            $directoryAcl = New-Object Security.AccessControl.DirectorySecurity
            $directoryAcl.SetAccessRuleProtection($true, $false)
            $identity = [Security.Principal.WindowsIdentity]::GetCurrent().User
            $directoryRule = New-Object Security.AccessControl.FileSystemAccessRule(
                $identity,
                [Security.AccessControl.FileSystemRights]::FullControl,
                [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit',
                [Security.AccessControl.PropagationFlags]::None,
                [Security.AccessControl.AccessControlType]::Allow)
            $directoryAcl.SetOwner($identity)
            $directoryAcl.SetAccessRule($directoryRule)
            [IO.Directory]::SetAccessControl($parent, $directoryAcl)
        }
        return $resolved
    } catch {
        if ((Test-Path -LiteralPath $parent -PathType Container) -and @(Get-ChildItem -LiteralPath $parent -Force).Count -eq 0) {
            Remove-Item -LiteralPath $parent -Force -ErrorAction SilentlyContinue
        }
        throw
    }
}

function Initialize-RetainedMfaHandoff([string]$Path, [string]$Secret, [int64]$LastAcceptedCounter) {
    if (-not $AllowRetainedMfaEnrollment) {
        throw "A retained MFA handoff requires -AllowRetainedMfaEnrollment."
    }
    if ([string]::IsNullOrWhiteSpace($Secret) -or $Secret -cnotmatch '^[A-Z2-7]{16,128}$') {
        throw "The retained MFA enrollment secret is invalid."
    }
    $resolved = [IO.Path]::GetFullPath($Path)
    $parent = Split-Path -Parent $resolved
    if (-not (Test-Path -LiteralPath $parent -PathType Container) -or (Get-Item -LiteralPath $parent -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "The preflighted retained MFA handoff directory is missing or unsafe: $parent"
    }
    if (Test-Path -LiteralPath $resolved) { throw "Retained MFA handoff already exists at $resolved; no secret was replaced." }
    if (@(Get-ChildItem -LiteralPath $parent -Force).Count -ne 0) { throw "The preflighted retained MFA handoff directory is no longer empty." }
    try {
        $payload = [ordered]@{
            schemaVersion = "filingbridge.private-server.owner-mfa-handoff/v1"
            status = "pending"
            createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
            secret = $Secret
            recoveryCodes = @()
            lastAcceptedCounter = $LastAcceptedCounter
        }
        $json = ($payload | ConvertTo-Json -Depth 4) + [Environment]::NewLine
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes($json)
        try {
            if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
                $stream = [IO.File]::Open($resolved, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
            } else {
                $options = [IO.FileStreamOptions]::new()
                $options.Access = [IO.FileAccess]::Write
                $options.Mode = [IO.FileMode]::CreateNew
                $options.Share = [IO.FileShare]::None
                $options.Options = [IO.FileOptions]::WriteThrough
                $options.UnixCreateMode = [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite
                $stream = [IO.FileStream]::new($resolved, $options)
            }
            try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
        } finally {
            [Array]::Clear($bytes, 0, $bytes.Length)
            $json = $null
            $payload = $null
        }
        return $resolved
    } catch {
        if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Force -ErrorAction SilentlyContinue }
        throw
    }
}

function Complete-RetainedMfaHandoff([string]$Path, [string[]]$RecoveryCodes) {
    $resolved = [IO.Path]::GetFullPath($Path)
    $codes = @($RecoveryCodes | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
    if ($codes.Count -lt 8 -or @($codes | Select-Object -Unique).Count -ne $codes.Count) {
        throw "Retained MFA enrollment did not return the required unique recovery codes; the protected pending seed remains at $resolved."
    }
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) { throw "Protected pending MFA handoff is missing at $resolved." }
    $pending = Get-Content -LiteralPath $resolved -Raw | ConvertFrom-Json
    if ([string]$pending.schemaVersion -cne "filingbridge.private-server.owner-mfa-handoff/v1" -or
        [string]$pending.status -cne "pending" -or [string]::IsNullOrWhiteSpace([string]$pending.secret)) {
        throw "Protected pending MFA handoff is invalid; no secret file was replaced."
    }
    $parent = Split-Path -Parent $resolved
    $temporary = Join-Path $parent (".owner-mfa-" + [Guid]::NewGuid().ToString("N") + ".tmp")
    $payload = [ordered]@{
        schemaVersion = "filingbridge.private-server.owner-mfa-handoff/v1"
        status = "complete"
        createdAtUtc = [string]$pending.createdAtUtc
        completedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
        secret = [string]$pending.secret
        recoveryCodes = $codes
        lastAcceptedCounter = [int64]$pending.lastAcceptedCounter
    }
    $json = ($payload | ConvertTo-Json -Depth 4) + [Environment]::NewLine
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($json)
    try {
        if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
            $stream = [IO.File]::Open($temporary, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        } else {
            $options = [IO.FileStreamOptions]::new()
            $options.Access = [IO.FileAccess]::Write
            $options.Mode = [IO.FileMode]::CreateNew
            $options.Share = [IO.FileShare]::None
            $options.Options = [IO.FileOptions]::WriteThrough
            $options.UnixCreateMode = [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite
            $stream = [IO.FileStream]::new($temporary, $options)
        }
        try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
        Move-Item -LiteralPath $temporary -Destination $resolved -Force
        return $resolved
    } finally {
        [Array]::Clear($bytes, 0, $bytes.Length)
        $json = $null
        $payload = $null
        $pending = $null
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue }
    }
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
if ($baseUri.Scheme -eq "http" -and $AllowInsecureHttp -and -not $baseUri.IsLoopback) {
    throw "AllowInsecureHttp is restricted to an HTTP loopback URL."
}

$base = $baseUri.AbsoluteUri.TrimEnd("/")
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$ephemeralEnrollmentSecret = $null
$acceptedTotpCounter = $null
$mfaEnrolledDuringCheck = $false
$mfaHandoffRetained = $false
$ephemeralMfaHandoffWritten = $false
$retainedEnrollmentSecret = $null
$passwordRotationRequired = $false
$passwordRotated = $false
if ($AllowEphemeralMfaEnrollment -and [string]::IsNullOrWhiteSpace($EphemeralMfaHandoffPath) -and -not [string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) {
    $EphemeralMfaHandoffPath = Join-Path $env:RUNNER_TEMP "accounts-visual-auth/totp-handoff.json"
}
$ownerWorkflowReservationPath = Reserve-OwnerWorkflowReport $ownerWorkflowReportTarget
$retainedMfaReservedPath = try {
    if ($AllowRetainedMfaEnrollment) { Reserve-RetainedMfaHandoff $RetainedMfaHandoffPath } else { "" }
} catch {
    Remove-Item -LiteralPath $ownerWorkflowReservationPath -Force -ErrorAction SilentlyContinue
    throw
}

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
    tenantSlug = $TenantSlug.Trim().ToLowerInvariant()
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

if ([int]$loginResponse.StatusCode -eq 202) {
    $challenge = $loginResponse.Content | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace([string]$challenge.challengeToken)) {
        throw "MFA login response did not include a challenge token."
    }
    $effectiveTotpSecret = if ([bool]$challenge.requiresEnrollment) {
        if (-not $AllowEphemeralMfaEnrollment -and -not $AllowRetainedMfaEnrollment) {
            throw "The smoke account requires MFA enrollment. Enrol it out of band, or explicitly use the retained/ephemeral enrollment handoff appropriate to this installation."
        }
        if ([string]::IsNullOrWhiteSpace([string]$challenge.enrollmentSecret)) {
            throw "MFA enrollment response did not include an enrollment secret."
        }
        $mfaEnrolledDuringCheck = $true
        if ($AllowRetainedMfaEnrollment) {
            $retainedEnrollmentSecret = [string]$challenge.enrollmentSecret
            $retainedEnrollmentSecret
        } else {
            $ephemeralEnrollmentSecret = [string]$challenge.enrollmentSecret
            $ephemeralEnrollmentSecret
        }
    } else {
        $TotpSecret
    }
    if ([string]::IsNullOrWhiteSpace($effectiveTotpSecret)) {
        throw "The smoke account requires MFA. Set SMOKE_TOTP_SECRET or -TotpSecret for an already-enrolled account."
    }

    Write-Host "Completing privileged-account MFA through frontend proxy..."
    $totpAt = [DateTimeOffset]::UtcNow
    $acceptedTotpCounter = Get-TotpCounter $totpAt
    if (-not [string]::IsNullOrWhiteSpace($retainedEnrollmentSecret)) {
        $retainedPath = Initialize-RetainedMfaHandoff `
            -Path $retainedMfaReservedPath `
            -Secret $retainedEnrollmentSecret `
            -LastAcceptedCounter $acceptedTotpCounter
        Write-Host "Protected pending Owner MFA seed written before enrollment completion: $retainedPath"
    }
    $mfaBody = @{
        challengeToken = [string]$challenge.challengeToken
        totpCode = New-TotpCode $effectiveTotpSecret $totpAt
        recoveryCode = $null
    } | ConvertTo-Json
    $loginResponse = Invoke-WebRequest `
        -Uri "$base/api/auth/mfa/challenge" `
        -Method Post `
        -ContentType "application/json" `
        -Body $mfaBody `
        -UseBasicParsing `
        -WebSession $session `
        -TimeoutSec $TimeoutSeconds
    if (-not [string]::IsNullOrWhiteSpace($retainedEnrollmentSecret)) {
        $completion = $loginResponse.Content | ConvertFrom-Json
        $retainedPath = Complete-RetainedMfaHandoff `
            -Path $retainedMfaReservedPath `
            -RecoveryCodes @($completion.recoveryCodes)
        $retainedEnrollmentSecret = $null
        $completion = $null
        $mfaHandoffRetained = $true
        Write-Host "Retained Owner MFA handoff written: $retainedPath"
    }
    $mfaBody = $null
    $effectiveTotpSecret = $null
    $challenge = $null
}

if ([int]$loginResponse.StatusCode -ne 200) {
    throw "Authentication did not complete successfully. HTTP status: $($loginResponse.StatusCode)."
}
if (-not $AllowInsecureHttp) {
    Assert-SecurityHeader -Response $loginResponse -Name "Strict-Transport-Security" -ExpectedText "max-age=31536000"
    Assert-SetCookieAttribute -Response $loginResponse -CookieName "accounts_session" -Attribute "Secure"
    Assert-SetCookieAttribute -Response $loginResponse -CookieName "accounts_csrf" -Attribute "Secure"
} else {
    Import-LoopbackSecureCookies -Response $loginResponse -Session $session -BaseUri $baseUri
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
if ($currentUser.mfaVerified -ne $true -or [string]$currentUser.mfaMethod -cne "totp") {
    throw "Authenticated /api/auth/me response did not prove a fresh TOTP MFA session."
}

$passwordRotationRequired = [bool]$currentUser.mustChangePassword
if ($passwordRotationRequired -or -not [string]::IsNullOrWhiteSpace($NewPassword)) {
    if ([string]::IsNullOrWhiteSpace($NewPassword)) {
        throw "The authenticated Owner must rotate the temporary password. Set SMOKE_NEW_PASSWORD or -NewPassword."
    }
    if ($NewPassword -ceq $Password) {
        throw "The new Owner password must differ from the temporary/current password."
    }
    Write-Host "Rotating the Owner password through the CSRF-protected frontend proxy..."
    $passwordBody = @{
        currentPassword = $Password
        newPassword = $NewPassword
    } | ConvertTo-Json
    try {
        $passwordResponse = Invoke-WebRequest `
            -Uri "$base/api/auth/password" `
            -Method Post `
            -ContentType "application/json" `
            -Headers @{ "X-CSRF-Token" = $csrfToken } `
            -Body $passwordBody `
            -UseBasicParsing `
            -WebSession $session `
            -TimeoutSec $TimeoutSeconds
    } finally {
        $passwordBody = $null
        $Password = $null
        $NewPassword = $null
    }
    if ([int]$passwordResponse.StatusCode -ne 200) {
        throw "Owner password rotation did not complete successfully. HTTP status: $($passwordResponse.StatusCode)."
    }
    if ($AllowInsecureHttp) {
        Import-LoopbackSecureCookies -Response $passwordResponse -Session $session -BaseUri $baseUri
    }
    $currentUser = $passwordResponse.Content | ConvertFrom-Json
    if ([bool]$currentUser.mustChangePassword) {
        throw "Owner password rotation returned an account that still requires a password change."
    }
    $passwordRotated = $true
    $csrfToken = Get-CsrfToken -Session $session -BaseUri $baseUri
}
if ([bool]$currentUser.mustChangePassword) {
    throw "Authenticated Owner workflow cannot continue while a mandatory password change remains outstanding."
}
$Password = $null
$NewPassword = $null

Write-Host "Checking company list through frontend proxy..."
Invoke-RestMethod `
    -Uri "$base/api/companies" `
    -Method Get `
    -WebSession $session `
    -TimeoutSec $TimeoutSeconds | Out-Null

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

Write-Host "Capturing production readiness report..."
$productionReadinessReport = Invoke-RestMethod `
    -Uri "$base/api/system/production-readiness" `
    -Method Get `
    -WebSession $session `
    -TimeoutSec $TimeoutSeconds

if ([string]::IsNullOrWhiteSpace([string]$productionReadinessReport.overallStatus)) {
    throw "Production readiness report did not include overallStatus."
}
if ($null -eq $productionReadinessReport.productionScorecard -or
    [int]$productionReadinessReport.productionScorecard.currentScore -le 0 -or
    [int]$productionReadinessReport.productionScorecard.targetScore -le 0) {
    throw "Production readiness report did not include a valid productionScorecard."
}
if ($null -eq $productionReadinessReport.releaseBlockerRegister -or
    @($productionReadinessReport.releaseBlockerRegister).Count -eq 0) {
    throw "Production readiness report did not include releaseBlockerRegister entries."
}

$productionReadinessReportPath = Join-Path $OutputDirectory "production-readiness-report.json"
$productionReadinessReport |
    ConvertTo-Json -Depth 30 |
    Set-Content -LiteralPath $productionReadinessReportPath -Encoding UTF8
Write-Host "Production readiness report written: $productionReadinessReportPath"

if ($CheckMonitoringErrorRouting) {
    Write-Host "Checking controlled monitoring error routing..."
    $monitoringResponse = Invoke-RestMethod `
        -Uri "$base/api/system/monitoring/error-smoke" `
        -Method Post `
        -ContentType "application/json" `
        -Headers @{ "X-CSRF-Token" = $csrfToken } `
        -Body "{}" `
        -WebSession $session `
        -TimeoutSec $TimeoutSeconds

    if ($monitoringResponse.status -ne "reported") {
        throw "Monitoring smoke endpoint returned unexpected status '$($monitoringResponse.status)'."
    }
    if ([string]::IsNullOrWhiteSpace($monitoringResponse.correlationId)) {
        throw "Monitoring smoke response did not include a correlationId."
    }
    if ([string]::IsNullOrWhiteSpace($monitoringResponse.eventId)) {
        throw "Monitoring smoke response did not include an eventId."
    }

    Write-Host "Checking controlled client monitoring routing..."
    $clientMonitoringBody = @{
        eventCode = "render-exception"
        route = "/companies/742/periods/73/client%40example.ie?token=NeverSendThis"
    } | ConvertTo-Json
    $clientMonitoringResponse = Invoke-RestMethod `
        -Uri "$base/api/system/monitoring/client-event" `
        -Method Post `
        -ContentType "application/json" `
        -Headers @{ "X-CSRF-Token" = $csrfToken } `
        -Body $clientMonitoringBody `
        -WebSession $session `
        -TimeoutSec $TimeoutSeconds

    if ($clientMonitoringResponse.status -ne "reported") {
        throw "Client monitoring smoke endpoint returned unexpected status '$($clientMonitoringResponse.status)'."
    }
    if ($clientMonitoringResponse.eventCode -ne "render-exception") {
        throw "Client monitoring smoke endpoint returned unexpected event code '$($clientMonitoringResponse.eventCode)'."
    }
    if ([string]::IsNullOrWhiteSpace($clientMonitoringResponse.eventId) -or
        [string]::IsNullOrWhiteSpace($clientMonitoringResponse.correlationId)) {
        throw "Client monitoring smoke response did not include provider event and correlation identifiers."
    }
    if ($clientMonitoringResponse.route -ne "/companies/{id}/periods/{id}/{redacted}") {
        throw "Client monitoring smoke response did not retain only the normalized route shape."
    }
    $clientMonitoringJson = $clientMonitoringResponse | ConvertTo-Json -Depth 4 -Compress
    if ($clientMonitoringJson -match "client@example.ie|NeverSendThis|client%40example.ie") {
        throw "Client monitoring smoke response exposed synthetic sensitive input."
    }

    $monitoringEvidencePath = Join-Path $OutputDirectory "monitoring-error-routing-report.json"
    [ordered]@{
        status = "passed"
        checkedAtUtc = [DateTime]::UtcNow.ToString("o")
        baseUrl = $base
        provider = $monitoringResponse.provider
        eventId = $monitoringResponse.eventId
        correlationId = $monitoringResponse.correlationId
        clientEvent = [ordered]@{
            eventCode = $clientMonitoringResponse.eventCode
            eventId = $clientMonitoringResponse.eventId
            correlationId = $clientMonitoringResponse.correlationId
            route = $clientMonitoringResponse.route
            sensitiveInputAbsent = $true
        }
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $monitoringEvidencePath -Encoding UTF8
    Write-Host "Monitoring error-routing evidence written: $monitoringEvidencePath"
}

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

if (-not [string]::IsNullOrWhiteSpace($ephemeralEnrollmentSecret) -and $null -ne $acceptedTotpCounter) {
    Write-EphemeralMfaHandoff `
        -Path $EphemeralMfaHandoffPath `
        -Secret $ephemeralEnrollmentSecret `
        -LastAcceptedCounter $acceptedTotpCounter
    $ephemeralEnrollmentSecret = $null
    $acceptedTotpCounter = $null
    $ephemeralMfaHandoffWritten = $true
}

$downloadEvidence = [ordered]@{ checked = [bool]$CheckDownloads }
if ($CheckDownloads) {
    $downloadEvidence.accountsPackage = [ordered]@{
        fileName = [IO.Path]::GetFileName($accountsPackage)
        byteSize = (Get-Item -LiteralPath $accountsPackage -Force).Length
        sha256 = (Get-FileHash -LiteralPath $accountsPackage -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $downloadEvidence.ixbrlPackage = [ordered]@{
        fileName = [IO.Path]::GetFileName($ixbrlPackage)
        byteSize = (Get-Item -LiteralPath $ixbrlPackage -Force).Length
        sha256 = (Get-FileHash -LiteralPath $ixbrlPackage -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
$ownerWorkflowReportPath = Write-OwnerWorkflowReport `
    -Path $ownerWorkflowReportTarget `
    -ReservationPath $ownerWorkflowReservationPath `
    -Report ([ordered]@{
        schemaVersion = "filingbridge.private-server.owner-workflow/v1"
        status = "passed"
        checkedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
        baseUrl = $base
        tenantSlug = $TenantSlug.Trim().ToLowerInvariant()
        authenticatedSessionPassed = $true
        passwordRotationRequired = $passwordRotationRequired
        passwordRotated = $passwordRotated
        mfaVerified = $true
        mfaMethod = "totp"
        mfaEnrolledDuringCheck = $mfaEnrolledDuringCheck
        mfaHandoffRetained = $mfaHandoffRetained
        ephemeralMfaHandoffWritten = $ephemeralMfaHandoffWritten
        companyListPassed = $true
        downloads = $downloadEvidence
        csrfLogoutPassed = $true
        postLogoutUnauthorized = $true
    })
Write-Host "Owner workflow evidence written: $ownerWorkflowReportPath"

Write-Host "Production smoke check completed."
