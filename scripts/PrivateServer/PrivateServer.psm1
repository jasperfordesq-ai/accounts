Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
try { Add-Type -AssemblyName System.Net.Http -ErrorAction Stop } catch { }
try { Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction Stop } catch { }

$script:PrivateServerCommandInvoker = $null
$script:Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$script:Utf8Strict = New-Object System.Text.UTF8Encoding($false, $true)
$script:StateFileName = "server.json"
$script:EnvironmentFileName = "private.env"
$script:SupportedStateFormat = 1
$script:RecoveryCriticalSecrets = @(
    "auth_session_signing_key",
    "audit_integrity_signing_key",
    "database_tenant_context_key",
    "identity_hmac_key",
    "mfa_encryption_key",
    "backup_authentication_key"
)
$script:MaximumCompleteBackupArchiveBytes = 1900MB

function Set-PrivateServerCommandInvoker {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][scriptblock]$Invoker)
    $script:PrivateServerCommandInvoker = $Invoker
}

function Reset-PrivateServerCommandInvoker {
    [CmdletBinding()]
    param()
    $script:PrivateServerCommandInvoker = $null
}

function Protect-PrivateServerText {
    [CmdletBinding()]
    param([AllowNull()][string]$Text)

    if ($null -eq $Text) { return "" }

    $protected = $Text
    $protected = [regex]::Replace(
        $protected,
        '(?i)("[^"]*(?:password|token|secret|cookie|authorization|session|connectionString|signingKey|encryptionKey)[^"]*"\s*:\s*")[^"]*(")',
        '$1[REDACTED]$2')
    $protected = [regex]::Replace(
        $protected,
        '(?i)(Bearer\s+)[A-Za-z0-9._~+\-/]+=*',
        '$1[REDACTED]')
    $protected = [regex]::Replace(
        $protected,
        '(?im)(password|passwd|pwd|token|secret|cookie|authorization|session|connection\s*string|key)\s*([=:])\s*([^\s,;\}\]]+|"[^"]*")',
        '$1$2[REDACTED]')
    $protected = [regex]::Replace(
        $protected,
        '(?i)(Host=[^;\r\n]+;[^\r\n]*?(?:Password|Pwd)=)[^;\r\n]+',
        '$1[REDACTED]')
    $protected = [regex]::Replace(
        $protected,
        '(?i)([?&#](?:token|code|secret)=)[^&#\s]+',
        '$1[REDACTED]')
    return $protected
}

function Protect-FbSupportText {
    param([AllowNull()][string]$Text)
    $protected = Protect-PrivateServerText $Text
    $protected = [regex]::Replace($protected, '(?i)\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b', '[EMAIL-REDACTED]')
    $protected = [regex]::Replace($protected, '(?i)C:\\Users\\[^\\/\r\n]+', 'C:\Users\[USER-REDACTED]')
    $protected = [regex]::Replace($protected, '(?<![\d.])(?!127\.0\.0\.1\b)(?:\d{1,3}\.){3}\d{1,3}(?![\d.])', '[IP-REDACTED]')
    return $protected
}

function Get-FbProperty {
    param(
        [AllowNull()]$Object,
        [Parameter(Mandatory = $true)][string]$Name,
        $Default = $null
    )
    if ($null -eq $Object) { return $Default }
    if ($Object -is [Collections.IDictionary]) {
        if ($Object.Contains($Name)) { return $Object[$Name] }
        return $Default
    }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $Default }
    return $property.Value
}

function ConvertTo-FbFullPath {
    param([Parameter(Mandatory = $true)][string]$Path, [string]$BasePath)
    if ([IO.Path]::IsPathRooted($Path)) {
        return [IO.Path]::GetFullPath($Path)
    }
    if ([string]::IsNullOrWhiteSpace($BasePath)) { $BasePath = (Get-Location).Path }
    return [IO.Path]::GetFullPath((Join-Path $BasePath $Path))
}

function Test-FbPathWithin {
    param([string]$Candidate, [string]$Parent)
    $candidateFull = [IO.Path]::GetFullPath($Candidate).TrimEnd('\', '/')
    $parentFull = [IO.Path]::GetFullPath($Parent).TrimEnd('\', '/')
    if ($candidateFull.Equals($parentFull, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    return $candidateFull.StartsWith(
        $parentFull + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)
}

function Get-FbDefaultStateDirectory {
    if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        throw "LOCALAPPDATA is unavailable. Supply -StateDirectory explicitly."
    }
    return [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA "FilingBridge\server"))
}

function Resolve-FbStateDirectory {
    param([string]$StateDirectory)
    if ([string]::IsNullOrWhiteSpace($StateDirectory)) {
        return Get-FbDefaultStateDirectory
    }
    return ConvertTo-FbFullPath -Path $StateDirectory
}

function Assert-FbSafeOperatorPath {
    param([Parameter(Mandatory = $true)][string]$Path, [string]$Description = "Path")
    if ($Path.Length -gt 165) { throw "$Description is too long for reliable Docker Desktop and legacy Windows path handling: $Path" }
    if ($Path -match "[\r\n\x00]") { throw "$Description contains a control character." }
    if ($Path.Contains('"')) { throw "$Description may not contain a double quote." }
}

function Assert-FbNoReparseAncestor {
    param([Parameter(Mandatory = $true)][string]$Path, [string]$Description = "Path")
    $full = [IO.Path]::GetFullPath($Path)
    $root = [IO.Path]::GetPathRoot($full)
    $relative = $full.Substring($root.Length)
    $current = $root
    foreach ($segment in $relative.Split([char[]]@('\', '/'), [StringSplitOptions]::RemoveEmptyEntries)) {
        $current = Join-Path $current $segment
        if (-not (Test-Path -LiteralPath $current)) { continue }
        $item = Get-Item -LiteralPath $current -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "$Description may not traverse a filesystem link or junction: $current"
        }
    }
}

function Write-FbTextExclusive {
    param([string]$Path, [string]$Value)
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $bytes = $script:Utf8NoBom.GetBytes($Value)
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.Flush($true)
    } finally {
        $stream.Dispose()
    }
}

function Read-FbUtf8Text {
    param([Parameter(Mandatory = $true)][string]$Path)
    return [IO.File]::ReadAllText($Path, $script:Utf8Strict)
}

function Write-FbTextAtomic {
    param([string]$Path, [string]$Value)
    $parent = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    $temporary = Join-Path $parent (".fb-" + [Guid]::NewGuid().ToString("N") + ".tmp")
    try {
        [IO.File]::WriteAllText($temporary, $Value, $script:Utf8NoBom)
        Move-Item -LiteralPath $temporary -Destination $Path -Force
    } finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
    }
}

function Write-FbJsonAtomic {
    param([string]$Path, $Value)
    Write-FbTextAtomic -Path $Path -Value (($Value | ConvertTo-Json -Depth 12) + [Environment]::NewLine)
}

function Set-FbRestrictedAcl {
    param([Parameter(Mandatory = $true)][string]$Path)
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        $mode = if ((Get-Item -LiteralPath $Path -Force).PSIsContainer) { "700" } else { "600" }
        $null = Invoke-FbNative -FilePath "chmod" -Arguments @($mode, "--", $Path) -Description "Restrict private state permissions" -Mutating
        return
    }

    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $currentSid = $identity.User
    $systemSid = New-Object Security.Principal.SecurityIdentifier("S-1-5-18")
    $administratorsSid = New-Object Security.Principal.SecurityIdentifier("S-1-5-32-544")
    $item = Get-Item -LiteralPath $Path -Force
    $permission = if ($item.PSIsContainer) { "(OI)(CI)F" } else { "F" }
    $allowedSids = @($currentSid.Value, $systemSid.Value, $administratorsSid.Value)
    $grantArguments = @(
        $Path,
        "/inheritance:r",
        "/grant:r",
        "*$($currentSid.Value):$permission",
        "*$($systemSid.Value):$permission",
        "*$($administratorsSid.Value):$permission",
        "/Q"
    )
    $null = @(& icacls.exe @grantArguments 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "Could not restrict NTFS permissions for Private Server state: $Path" }

    # Remove any pre-existing explicit grant/deny identities that were not one of the
    # three operator principals. New setup paths have none, but backup destinations
    # may be reused and must not silently retain broader access.
    $acl = Get-Acl -LiteralPath $Path
    $rules = @($acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]))
    foreach ($rule in $rules) {
        $sidValue = $rule.IdentityReference.Value
        if ($allowedSids -contains $sidValue) { continue }
        $null = @(& icacls.exe $Path "/remove:g" "*$sidValue" "/remove:d" "*$sidValue" "/Q" 2>&1)
        if ($LASTEXITCODE -ne 0) { throw "Could not remove an unexpected NTFS identity from Private Server state: $Path" }
    }

    $verifiedAcl = Get-Acl -LiteralPath $Path
    if (-not $verifiedAcl.AreAccessRulesProtected) { throw "Private Server NTFS permissions still inherit from a parent: $Path" }
    foreach ($rule in @($verifiedAcl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]))) {
        if ($allowedSids -notcontains $rule.IdentityReference.Value) { throw "Private Server path retains an unexpected NTFS identity: $Path" }
    }
}

function New-PrivateServerRandomSecret {
    [CmdletBinding()]
    param([ValidateRange(32, 256)][int]$ByteCount = 48)
    $bytes = New-Object byte[] $ByteCount
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    # Backend cryptographic configuration deliberately accepts standard Base64 only.
    # Forty-eight bytes yields a 64-character value without padding and at least
    # 384 bits of entropy; it is also safe in secret files and Npgsql passwords.
    return [Convert]::ToBase64String($bytes)
}

function Get-FbRandomIndex {
    param([int]$MaximumExclusive)
    if ($MaximumExclusive -le 0) { throw "Random index maximum must be positive." }
    $bytes = New-Object byte[] 4
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $limit = [uint32]::MaxValue - ([uint32]::MaxValue % [uint32]$MaximumExclusive)
        do {
            $rng.GetBytes($bytes)
            $value = [BitConverter]::ToUInt32($bytes, 0)
        } while ($value -ge $limit)
        return [int]($value % [uint32]$MaximumExclusive)
    } finally { $rng.Dispose() }
}

function New-PrivateServerOwnerPassword {
    [CmdletBinding()]
    param([ValidateRange(24, 128)][int]$Length = 32)
    $groups = @(
        "ABCDEFGHJKLMNPQRSTUVWXYZ",
        "abcdefghijkmnopqrstuvwxyz",
        "23456789",
        "!@#%*-_=+"
    )
    $all = $groups -join ""
    $characters = New-Object System.Collections.Generic.List[char]
    foreach ($group in $groups) { $characters.Add($group[(Get-FbRandomIndex $group.Length)]) }
    while ($characters.Count -lt $Length) { $characters.Add($all[(Get-FbRandomIndex $all.Length)]) }
    for ($index = $characters.Count - 1; $index -gt 0; $index--) {
        $swapIndex = Get-FbRandomIndex ($index + 1)
        $value = $characters[$index]
        $characters[$index] = $characters[$swapIndex]
        $characters[$swapIndex] = $value
    }
    return -join $characters
}

function Get-FbSha256Text {
    param([string]$Value)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $bytes = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Value)) } finally { $sha.Dispose() }
    return ([BitConverter]::ToString($bytes)).Replace("-", "").ToLowerInvariant()
}

function Enter-FbInstallationLock {
    param([string]$StateDirectory)
    $resolved = (Resolve-FbStateDirectory $StateDirectory).TrimEnd('\', '/').ToLowerInvariant()
    $scope = if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) { "Global\" } else { "" }
    $name = $scope + "FilingBridge.PrivateServer." + (Get-FbSha256Text $resolved)
    $mutex = New-Object Threading.Mutex($false, $name)
    $acquired = $false
    try {
        try { $acquired = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $acquired = $true }
        if (-not $acquired) {
            throw "Another FilingBridge command already holds the exclusive lifecycle lock for this installation. Wait for it to finish; no Docker or state mutation was attempted."
        }
        return [pscustomobject]@{ Mutex = $mutex; Acquired = $true; Name = $name }
    } catch {
        if (-not $acquired) { $mutex.Dispose() }
        throw
    }
}

function Exit-FbInstallationLock {
    param($Lock)
    if ($null -eq $Lock) { return }
    try {
        if ([bool](Get-FbProperty $Lock "Acquired" $false)) { $Lock.Mutex.ReleaseMutex() }
    } finally { $Lock.Mutex.Dispose() }
}

function Get-FbHmacSha256 {
    param([byte[]]$Key, [string]$Value)
    $hmac = New-Object Security.Cryptography.HMACSHA256 -ArgumentList (,$Key)
    try { $bytes = $hmac.ComputeHash($script:Utf8NoBom.GetBytes($Value)) } finally { $hmac.Dispose() }
    return ([BitConverter]::ToString($bytes)).Replace("-", "").ToLowerInvariant()
}

function Test-FbFixedTimeHexEqual {
    param([string]$Expected, [string]$Actual)
    if ($Expected -cnotmatch '^[a-f0-9]{64}$' -or $Actual -cnotmatch '^[a-f0-9]{64}$') { return $false }
    $expectedBytes = New-Object byte[] 32
    $actualBytes = New-Object byte[] 32
    for ($index = 0; $index -lt 32; $index++) {
        $expectedBytes[$index] = [Convert]::ToByte($Expected.Substring($index * 2, 2), 16)
        $actualBytes[$index] = [Convert]::ToByte($Actual.Substring($index * 2, 2), 16)
    }
    [int]$difference = 0
    for ($index = 0; $index -lt 32; $index++) {
        $difference = $difference -bor ([int]$expectedBytes[$index] -bxor [int]$actualBytes[$index])
    }
    return $difference -eq 0
}

function New-FbNativeResult {
    param([int]$ExitCode, [object[]]$Output)
    return [pscustomobject]@{ ExitCode = $ExitCode; Output = @($Output | ForEach-Object { [string]$_ }) }
}

function Get-FbRawUtcTimestampProperty {
    param([string]$Json, [string]$PropertyName, [switch]$RequireRoundTrip)
    $property = '"' + [Regex]::Escape($PropertyName) + '"\s*:'
    if ([Regex]::Matches($Json, $property).Count -ne 1) { return "" }
    $timestamp = if ($RequireRoundTrip) {
        '[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\.[0-9]{7}Z'
    } else {
        '[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?Z'
    }
    $match = [Regex]::Match($Json, $property + '\s*"(?<value>' + $timestamp + ')"')
    if (-not $match.Success) { return "" }
    return $match.Groups['value'].Value
}

function ConvertTo-FbCanonicalUtcTimestampText {
    param($Value)
    if ($Value -is [DateTime]) {
        return ([DateTime]$Value).ToUniversalTime().ToString("o", [Globalization.CultureInfo]::InvariantCulture)
    }
    if ($Value -is [DateTimeOffset]) {
        return ([DateTimeOffset]$Value).ToUniversalTime().ToString("o", [Globalization.CultureInfo]::InvariantCulture)
    }
    return [string]$Value
}

function ConvertTo-FbWindowsNativeArgument {
    param([AllowEmptyString()][string]$Value)
    if ($null -eq $Value) { $Value = "" }
    if ($Value.Length -gt 0 -and $Value -notmatch '[\s"]') { return $Value }

    # Apply the CommandLineToArgvW/CRT quoting contract used by Windows native
    # processes. In particular, double runs of backslashes before embedded or
    # closing quotes so multiline shell scripts remain one exact Docker argv.
    $builder = New-Object Text.StringBuilder
    [void]$builder.Append('"')
    $slashes = 0
    foreach ($character in $Value.ToCharArray()) {
        if ($character -eq '\') { $slashes++; continue }
        if ($character -eq '"') {
            [void]$builder.Append(((('\' * (($slashes * 2) + 1))) -join ''))
            [void]$builder.Append('"')
            $slashes = 0
            continue
        }
        if ($slashes -gt 0) {
            [void]$builder.Append(((('\' * $slashes)) -join ''))
            $slashes = 0
        }
        [void]$builder.Append($character)
    }
    if ($slashes -gt 0) { [void]$builder.Append(((('\' * ($slashes * 2))) -join '')) }
    [void]$builder.Append('"')
    return $builder.ToString()
}

function Invoke-FbNative {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][object[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$Description,
        [switch]$Mutating,
        [switch]$IgnoreExitCode,
        [switch]$DryRun
    )

    if ($DryRun -and $Mutating) {
        Write-Host "DRY RUN: $Description"
        return New-FbNativeResult -ExitCode 0 -Output @()
    }

    if ($null -ne $script:PrivateServerCommandInvoker) {
        $result = & $script:PrivateServerCommandInvoker $FilePath $Arguments $Description ([bool]$Mutating)
        if ($null -eq $result) { $result = New-FbNativeResult -ExitCode 0 -Output @() }
        if (-not $IgnoreExitCode -and [int](Get-FbProperty $result "ExitCode" 0) -ne 0) {
            $safe = Protect-PrivateServerText -Text ((@(Get-FbProperty $result "Output" @()) | Select-Object -Last 8) -join [Environment]::NewLine)
            throw "$Description failed with exit code $($result.ExitCode). $safe"
        }
        return $result
    }

    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
        # Windows PowerShell 5.1 both promotes benign native stderr to
        # NativeCommandError under Stop and corrupts embedded quoting when an argv
        # such as a multiline `sh -ec` script is splatted through the call operator.
        # ProcessStartInfo plus explicit Windows quoting preserves the exact argv;
        # draining both streams concurrently avoids pipe-buffer deadlock.
        $processStart = New-Object Diagnostics.ProcessStartInfo
        $processStart.FileName = $FilePath
        $processStart.UseShellExecute = $false
        $processStart.RedirectStandardOutput = $true
        $processStart.RedirectStandardError = $true
        $processStart.CreateNoWindow = $true
        $processStart.Arguments = (@($Arguments | ForEach-Object { ConvertTo-FbWindowsNativeArgument ([string]$_) }) -join ' ')
        $process = New-Object Diagnostics.Process
        $process.StartInfo = $processStart
        try {
            if (-not $process.Start()) { throw "$Description could not start its native process." }
            $stdoutTask = $process.StandardOutput.ReadToEndAsync()
            $stderrTask = $process.StandardError.ReadToEndAsync()
            $process.WaitForExit()
            $exitCode = [int]$process.ExitCode
            $stdout = $stdoutTask.Result
            $stderr = $stderrTask.Result
        } finally {
            $process.Dispose()
        }
        $global:LASTEXITCODE = $exitCode
        $output = @(
            @($stdout -split '\r?\n' | Where-Object { $_ -ne '' })
            @($stderr -split '\r?\n' | Where-Object { $_ -ne '' })
        )
    } else {
        $previousErrorActionPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = "Continue"
            $global:LASTEXITCODE = $null
            $output = @(& $FilePath @Arguments 2>&1 | ForEach-Object { [string]$_ })
            $invocationSucceeded = $?
            $exitCode = if ($null -eq $global:LASTEXITCODE) {
                if ($invocationSucceeded) { 0 } else { 1 }
            } else {
                [int]$global:LASTEXITCODE
            }
        } finally {
            $ErrorActionPreference = $previousErrorActionPreference
        }
    }
    $result = New-FbNativeResult -ExitCode $exitCode -Output $output
    if (-not $IgnoreExitCode -and $exitCode -ne 0) {
        $safe = Protect-PrivateServerText -Text (($output | Select-Object -Last 8) -join [Environment]::NewLine)
        throw "$Description failed with exit code $exitCode. $safe"
    }
    return $result
}

function Get-FbComposeBaseArguments {
    param($State, [string]$ComposeFile)
    return @(
        "compose",
        "--project-name", [string]$State.composeProject,
        "--env-file", [string]$State.environmentFile,
        "-f", $ComposeFile
    )
}

function Invoke-FbCompose {
    param(
        $State,
        [string]$ComposeFile,
        [object[]]$Arguments,
        [string]$Description,
        [switch]$Mutating,
        [switch]$IgnoreExitCode,
        [switch]$DryRun
    )
    $allArguments = @(Get-FbComposeBaseArguments -State $State -ComposeFile $ComposeFile) + @($Arguments)
    return Invoke-FbNative -FilePath "docker" -Arguments $allArguments -Description $Description -Mutating:$Mutating -IgnoreExitCode:$IgnoreExitCode -DryRun:$DryRun
}

function ConvertTo-FbDotEnvValue {
    param([AllowEmptyString()][string]$Value)
    if ($Value -match "[\r\n\x00]") { throw "A configuration value contains a prohibited control character." }
    return "'" + $Value.Replace("'", "\'") + "'"
}

function ConvertTo-FbDockerPath {
    param([string]$Path)
    return ([IO.Path]::GetFullPath($Path)).Replace('\', '/')
}

function Write-FbEnvironmentFile {
    param($State)
    $secretDirectory = [string]$State.secretDirectory
    $localOrigin = "http://localhost:$($State.port)"
    $serverOrigin = [string](Get-FbProperty $State "publicOrigin" $localOrigin)
    if ([string]::IsNullOrWhiteSpace($serverOrigin)) { $serverOrigin = $localOrigin }
    $serverUri = [Uri]$serverOrigin
    $allowedHosts = @("localhost", "127.0.0.1")
    if (-not [string]::IsNullOrWhiteSpace($serverUri.Host)) { $allowedHosts += $serverUri.Host }
    $allowedHosts = @($allowedHosts | Select-Object -Unique) -join ";"

    $values = [ordered]@{
        ACCOUNTS_API_IMAGE                            = [string]$State.images.api
        ACCOUNTS_FRONTEND_IMAGE                       = [string]$State.images.frontend
        ACCOUNTS_POSTGRES_IMAGE                       = [string]$State.images.postgres
        PRIVATE_INSTALLATION_ID                       = [string]$State.instanceId
        PRIVATE_FRONTEND_PORT                         = [string]$State.port
        PRIVATE_LOCAL_ORIGIN                          = $localOrigin
        PRIVATE_SERVER_ORIGIN                         = $serverOrigin
        PRIVATE_ALLOWED_HOSTS                         = $allowedHosts
        PRIVATE_TENANT_NAME                           = [string]$State.tenantName
        PRIVATE_TENANT_SLUG                           = [string]$State.tenantSlug
        PRIVATE_OWNER_EMAIL                           = [string]$State.ownerEmail
        PRIVATE_OWNER_DISPLAY_NAME                    = [string]$State.ownerName
        POSTGRES_DB                                   = "accounts"
        POSTGRES_USER                                 = "accounts"
        MFA_ENCRYPTION_ACTIVE_KEY_ID                  = [string]$State.mfaKeyId
        AUDIT_INTEGRITY_ACTIVE_KEY_ID                 = [string]$State.auditKeyId
        POSTGRES_PASSWORD_FILE                        = ConvertTo-FbDockerPath (Join-Path $secretDirectory "postgres_password")
        POSTGRES_APPLICATION_PASSWORD_FILE            = ConvertTo-FbDockerPath (Join-Path $secretDirectory "postgres_application_password")
        ACCOUNTS_MIGRATION_CONNECTION_STRING_FILE     = ConvertTo-FbDockerPath (Join-Path $secretDirectory "accounts_migration_connection_string")
        ACCOUNTS_APPLICATION_CONNECTION_STRING_FILE   = ConvertTo-FbDockerPath (Join-Path $secretDirectory "accounts_application_connection_string")
        AUTH_SESSION_SIGNING_KEY_FILE                 = ConvertTo-FbDockerPath (Join-Path $secretDirectory "auth_session_signing_key")
        AUDIT_INTEGRITY_SIGNING_KEY_FILE              = ConvertTo-FbDockerPath (Join-Path $secretDirectory "audit_integrity_signing_key")
        DATABASE_TENANT_CONTEXT_KEY_FILE              = ConvertTo-FbDockerPath (Join-Path $secretDirectory "database_tenant_context_key")
        IDENTITY_HMAC_KEY_FILE                        = ConvertTo-FbDockerPath (Join-Path $secretDirectory "identity_hmac_key")
        MFA_ENCRYPTION_KEY_FILE                       = ConvertTo-FbDockerPath (Join-Path $secretDirectory "mfa_encryption_key")
        ACCOUNTS_API_KEY_HASH_FILE                    = ConvertTo-FbDockerPath (Join-Path $secretDirectory "accounts_api_key_hash")
        ACCOUNTS_API_KEY_FILE                         = ConvertTo-FbDockerPath (Join-Path $secretDirectory "accounts_api_key")
        PRIVATE_INITIAL_OWNER_PASSWORD_FILE           = ConvertTo-FbDockerPath (Join-Path $secretDirectory "private_initial_owner_password")
    }
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# Generated by FilingBridge Private Server. Values are non-secret or secret-file paths.")
    foreach ($entry in $values.GetEnumerator()) {
        $lines.Add("$($entry.Key)=$(ConvertTo-FbDotEnvValue ([string]$entry.Value))")
    }
    Write-FbTextAtomic -Path ([string]$State.environmentFile) -Value (($lines -join [Environment]::NewLine) + [Environment]::NewLine)
    Set-FbRestrictedAcl -Path ([string]$State.environmentFile)
}

function Read-FbState {
    param([string]$StateDirectory, [switch]$AllowNonReady)
    $resolved = Resolve-FbStateDirectory $StateDirectory
    $statePath = Join-Path $resolved $script:StateFileName
    Assert-FbNoReparseAncestor $resolved "State directory"
    Assert-FbNoReparseAncestor $statePath "State file"
    if (-not (Test-Path -LiteralPath $statePath -PathType Leaf)) {
        throw "Private Server is not configured at $resolved. Run setup first."
    }
    $state = (Read-FbUtf8Text $statePath) | ConvertFrom-Json
    if ([int](Get-FbProperty $state "formatVersion" 0) -ne $script:SupportedStateFormat) {
        throw "Private Server state format is unsupported. Use a compatible FilingBridge release."
    }
    $expectedPaths = [ordered]@{
        stateDirectory = $resolved
        secretDirectory = Join-Path $resolved "secrets"
        environmentFile = Join-Path $resolved $script:EnvironmentFileName
        installedComposeFile = Join-Path $resolved "compose.private.installed.yml"
    }
    foreach ($entry in $expectedPaths.GetEnumerator()) {
        $recorded = [string](Get-FbProperty $state $entry.Key "")
        if ([string]::IsNullOrWhiteSpace($recorded) -or
            -not [IO.Path]::GetFullPath($recorded).Equals(
                [IO.Path]::GetFullPath([string]$entry.Value),
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "Private Server state path binding is invalid for '$($entry.Key)'. No command was run."
        }
        Assert-FbNoReparseAncestor ([string]$entry.Value) "Private Server $($entry.Key)"
    }
    $instanceId = [Guid]::Empty
    if (-not [Guid]::TryParse([string](Get-FbProperty $state "instanceId" ""), [ref]$instanceId) -or
        [string](Get-FbProperty $state "composeProject" "") -cne ("filingbridge-" + $instanceId.ToString("N").Substring(0, 12))) {
        throw "Private Server state installation/project identity is invalid. No command was run."
    }
    if (-not $AllowNonReady -and [string]$state.status -ne "ready") {
        throw "Private Server state is '$($state.status)', not ready. Run diagnose before making changes."
    }
    return $state
}

function Save-FbState {
    param($State)
    $State.updatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    Write-FbJsonAtomic -Path (Join-Path ([string]$State.stateDirectory) $script:StateFileName) -Value $State
    Set-FbRestrictedAcl -Path (Join-Path ([string]$State.stateDirectory) $script:StateFileName)
}

function Get-FbComposeFile {
    param([string]$RepositoryRoot)
    $path = Join-Path $RepositoryRoot "compose.private.yml"
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Private Server Compose file is missing: $path" }
    return [IO.Path]::GetFullPath($path)
}

function Assert-FbComposeMatchesState {
    param($State, [string]$ComposeFile)
    $expected = [string](Get-FbProperty $State "composeFileSha256" "")
    if ($expected -cnotmatch '^[a-f0-9]{64}$') { throw "Private Server state has no valid Compose-file identity. Use a supported update/recovery workflow." }
    $actual = (Get-FileHash -LiteralPath $ComposeFile -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -cne $expected) {
        throw "compose.private.yml does not match the installed release. Run lifecycle commands from the installed release directory, or use the explicit update command with a verified new release."
    }
}

function Get-FbReleaseManifestPath {
    param([string]$RequestedPath, [string]$RepositoryRoot)
    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) { return ConvertTo-FbFullPath $RequestedPath $RepositoryRoot }
    if (-not [string]::IsNullOrWhiteSpace($env:FILINGBRIDGE_RELEASE_MANIFEST)) {
        return ConvertTo-FbFullPath $env:FILINGBRIDGE_RELEASE_MANIFEST $RepositoryRoot
    }
    $candidate = Join-Path $RepositoryRoot "release.json"
    if (Test-Path -LiteralPath $candidate -PathType Leaf) { return [IO.Path]::GetFullPath($candidate) }
    return ""
}

function Read-FbReleaseManifest {
    param([string]$Path, [string]$RepositoryRoot)
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "A release.json manifest was not found. Supply -ReleaseManifest, set FILINGBRIDGE_RELEASE_MANIFEST, or use -BuildLocal explicitly for an unreviewed source build."
    }
    $Path = [IO.Path]::GetFullPath($Path)
    $canonicalManifest = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot "release.json"))
    if (-not $Path.Equals($canonicalManifest, [StringComparison]::OrdinalIgnoreCase)) {
        throw "A compiled release must use the canonical release.json at the extracted release root. Nested or partial manifests are refused; use -BuildLocal explicitly for a source tree."
    }
    $manifestText = Read-FbUtf8Text $Path
    $manifest = $manifestText | ConvertFrom-Json
    if ([string](Get-FbProperty $manifest "schemaVersion" "") -ne "filingbridge.private-server.release/v1") {
        throw "release.json has an unsupported schemaVersion."
    }
    $version = [string](Get-FbProperty $manifest "version" "")
    if ($version -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-(?:0|[1-9][0-9]*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*))*)?$') { throw "release.json version is not a canonical semantic version." }
    # PowerShell 7 coerces ISO JSON strings to DateTime and then formats them with
    # the current culture when cast back to string. Validate the single raw JSON
    # string so PS5 and PS7 enforce the same UTC contract.
    $generatedAtUtc = Get-FbRawUtcTimestampProperty $manifestText "generatedAtUtc"
    $generatedTimestamp = [DateTimeOffset]::MinValue
    if (-not $generatedAtUtc.EndsWith("Z", [StringComparison]::Ordinal) -or -not [DateTimeOffset]::TryParse($generatedAtUtc, [ref]$generatedTimestamp)) {
        throw "release.json generatedAtUtc must be a UTC timestamp."
    }
    $supportedHosts = @(Get-FbProperty $manifest "supportedHosts" @())
    if ($supportedHosts.Count -ne 1 -or [string]$supportedHosts[0] -cne "windows-x64") { throw "This release must declare exactly the supported windows-x64 host." }
    $images = Get-FbProperty $manifest "images"
    $api = [string](Get-FbProperty (Get-FbProperty $images "backend") "exactDigestReference" "")
    $frontend = [string](Get-FbProperty (Get-FbProperty $images "frontend") "exactDigestReference" "")
    $postgres = [string](Get-FbProperty (Get-FbProperty $images "postgres") "exactDigestReference" "")
    if ($api -cnotmatch '^ghcr\.io/jasperfordesq-ai/accounts-api@sha256:[a-f0-9]{64}$') { throw "release.json backend image must be the exact FilingBridge GHCR API digest reference." }
    if ($frontend -cnotmatch '^ghcr\.io/jasperfordesq-ai/accounts-frontend@sha256:[a-f0-9]{64}$') { throw "release.json frontend image must be the exact FilingBridge GHCR frontend digest reference." }
    if ($postgres -cnotmatch '^postgres@sha256:[a-f0-9]{64}$') { throw "release.json PostgreSQL image must be the exact official postgres digest reference." }
    $candidate = Get-FbProperty $manifest "candidate"
    $commitSha = [string](Get-FbProperty $candidate "commitSha" "")
    if ($commitSha -cnotmatch '^[0-9a-f]{40}$') { throw "release.json candidate.commitSha must be a full lowercase Git commit SHA." }
    $runUrl = [string](Get-FbProperty $candidate "githubActionsRunUrl" "")
    if ($runUrl -cnotmatch '^https://github\.com/jasperfordesq-ai/accounts/actions/runs/[1-9][0-9]*$') { throw "release.json candidate.githubActionsRunUrl must identify this repository's GitHub Actions run." }
    $assurance = Get-FbProperty $manifest "statutoryAssurance"
    if ([string](Get-FbProperty $assurance "status" "") -ne "release-blocked" -or
        (Get-FbProperty $assurance "noDirectSubmission" $false) -ne $true -or
        (Get-FbProperty $assurance "qualifiedAccountantRequired" $false) -ne $true) {
        throw "release.json must preserve release-blocked statutory assurance, no-direct-submission, and qualified-accountant gates."
    }

    $manifestFiles = @{}
    $fileEntries = @(Get-FbProperty $manifest "files" @())
    if ($fileEntries.Count -eq 0) { throw "release.json must inventory every release payload file." }
    foreach ($file in $fileEntries) {
        $relativePath = Assert-FbSafeRelativeBackupPath ([string](Get-FbProperty $file "path" ""))
        $fullPath = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot $relativePath))
        if (-not (Test-FbPathWithin $fullPath $RepositoryRoot)) { throw "release.json file escapes the release directory: $relativePath" }
        $relativeKey = $relativePath.Replace('\', '/').ToLowerInvariant()
        if ($manifestFiles.ContainsKey($relativeKey)) { throw "release.json contains a duplicate file path: $relativePath" }
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { throw "Release file is missing: $relativePath" }
        $expectedSize = [long](Get-FbProperty $file "byteSize" -1)
        $expectedHash = [string](Get-FbProperty $file "sha256" "")
        if ($expectedSize -lt 1 -or $expectedHash -cnotmatch '^[a-f0-9]{64}$') { throw "release.json has invalid size/hash evidence for: $relativePath" }
        if ((Get-Item -LiteralPath $fullPath -Force).Length -ne $expectedSize) { throw "Release file byte size mismatch: $relativePath" }
        $actualHash = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -ne $expectedHash) { throw "Release file SHA-256 mismatch: $relativePath" }
        $manifestFiles[$relativeKey] = [pscustomobject]@{ byteSize = $expectedSize; sha256 = $expectedHash }
    }
    foreach ($required in @(
        "FilingBridge.cmd", "compose.private.yml", ".env.private.example",
        "scripts/private-server.ps1", "scripts/PrivateServer/PrivateServer.psm1", "scripts/smoke-production.ps1",
        "Docs/deployment/README.md", "Docs/deployment/private-server.md", "Docs/deployment/LOCAL_WINDOWS_READINESS.md",
        "deploy/private/release-manifest.schema.json", "README.md", "LICENSE", "NOTICE",
        "THIRD_PARTY_NOTICES.md", "CONTRIBUTORS.md")) {
        if (-not $manifestFiles.ContainsKey($required.ToLowerInvariant())) { throw "release.json omits required release file: $required" }
    }
    foreach ($actualFile in @(Get-ChildItem -LiteralPath $RepositoryRoot -File -Recurse -Force)) {
        $actualRelative = $actualFile.FullName.Substring($RepositoryRoot.TrimEnd('\', '/').Length).TrimStart('\', '/').Replace('\', '/')
        if ($actualRelative -eq "release.json") { continue }
        if (-not $manifestFiles.ContainsKey($actualRelative.ToLowerInvariant())) { throw "Release directory contains an unmanifested file: $actualRelative" }
    }
    return [pscustomobject]@{
        path = [IO.Path]::GetFullPath($Path)
        sha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
        version = $version
        commitSha = $commitSha
        images = [pscustomobject]@{ api = $api; frontend = $frontend; postgres = $postgres }
        composeSha256 = [string]$manifestFiles["compose.private.yml"].sha256
        reviewed = $false
        integrityStatus = "manifest-integrity-checked"
    }
}

function New-FbLocalBuildRelease {
    param([string]$RepositoryRoot, [string]$InstanceId, [switch]$DryRun)
    $tagSuffix = $InstanceId.Replace("-", "").Substring(0, 12) + "-" + (Get-Date).ToUniversalTime().ToString("yyyyMMddHHmmss")
    $apiImage = "filingbridge-private-api:$tagSuffix"
    $frontendImage = "filingbridge-private-frontend:$tagSuffix"
    $postgresImage = "postgres:16.4-alpine"
    $null = Invoke-FbNative -FilePath "docker" -Arguments @("build", "--pull", "-f", (Join-Path $RepositoryRoot "Dockerfile.backend"), "-t", $apiImage, $RepositoryRoot) -Description "Build the compiled backend image" -Mutating -DryRun:$DryRun
    $null = Invoke-FbNative -FilePath "docker" -Arguments @("build", "--pull", "-f", (Join-Path $RepositoryRoot "Dockerfile.frontend"), "-t", $frontendImage, $RepositoryRoot) -Description "Build the compiled frontend image" -Mutating -DryRun:$DryRun
    $null = Invoke-FbNative -FilePath "docker" -Arguments @("pull", $postgresImage) -Description "Pull the PostgreSQL runtime image" -Mutating -DryRun:$DryRun
    return [pscustomobject]@{
        path = ""
        sha256 = ""
        version = "source-build"
        commitSha = ""
        images = [pscustomobject]@{ api = $apiImage; frontend = $frontendImage; postgres = $postgresImage }
        reviewed = $false
        integrityStatus = "source-build-unreviewed"
    }
}

function Test-FbTcpPortAvailable {
    param([int]$Port)
    $listener = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, $Port)
    try {
        $listener.Start()
        return $true
    } catch {
        return $false
    } finally {
        try { $listener.Stop() } catch { }
    }
}

function Assert-FbPrerequisites {
    param(
        [string]$StateDirectory,
        [string]$RepositoryRoot,
        [int]$Port,
        [switch]$SkipExternalChecks
    )
    if ($SkipExternalChecks -and $null -ne $script:PrivateServerCommandInvoker) {
        Assert-FbSafeOperatorPath $StateDirectory "State directory"
        Assert-FbSafeOperatorPath $RepositoryRoot "Release directory"
        if (-not (Test-FbTcpPortAvailable $Port)) { throw "Loopback port $Port is already in use." }
        return
    }
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        throw "FilingBridge Private Server is currently supported only on Windows x64."
    }
    if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne [Runtime.InteropServices.Architecture]::X64) {
        throw "FilingBridge Private Server currently requires Windows x64. ARM64 is not a certified host."
    }
    $operatingSystem = Get-CimInstance Win32_OperatingSystem -ErrorAction SilentlyContinue
    if ($null -eq $operatingSystem) { throw "Windows support posture could not be verified." }
    if ([int]$operatingSystem.ProductType -ne 1) { throw "Docker Desktop and FilingBridge Private Server are not supported on Windows Server." }
    $windowsBuild = [int]$operatingSystem.BuildNumber
    if ($windowsBuild -lt 22631) {
        throw "A currently supported Windows 11 x64 host at version 23H2 (build 22631) or newer is required. Windows 10 is not certified for this Private Server preview."
    }
    Assert-FbSafeOperatorPath $StateDirectory "State directory"
    Assert-FbSafeOperatorPath $RepositoryRoot "Release directory"
    if (-not (Test-FbTcpPortAvailable $Port)) { throw "Loopback port $Port is already in use. Choose another -Port." }

    try {
        $computer = Get-CimInstance Win32_ComputerSystem -ErrorAction Stop
        if ([long]$computer.TotalPhysicalMemory -lt 8GB) { throw "At least 8 GB of physical memory is required." }
        if ([int]$computer.NumberOfLogicalProcessors -lt 4) { throw "At least four logical processors are required." }
        $processors = @(Get-CimInstance Win32_Processor -ErrorAction Stop)
        $firmwareVirtualization = @($processors | Where-Object { $_.VirtualizationFirmwareEnabled -eq $true }).Count -gt 0
        if (-not [bool]$computer.HypervisorPresent -and -not $firmwareVirtualization) {
            throw "Hardware virtualization is not enabled in BIOS/UEFI. Enable it before using Docker Desktop with WSL2."
        }
    } catch {
        if ($_.Exception.Message -match '^(At least |Hardware virtualization)') { throw }
        throw "Processor, memory, and virtualization readiness could not be verified: $($_.Exception.Message)"
    }
    $stateRoot = [IO.Path]::GetPathRoot($StateDirectory)
    $drive = Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='$($stateRoot.TrimEnd('\'))'" -ErrorAction SilentlyContinue
    if ($null -ne $drive -and [long]$drive.FreeSpace -lt 20GB) { throw "At least 20 GB of free space is required on $stateRoot." }

    if ($SkipExternalChecks) { return }
    if ($null -eq (Get-Command docker -ErrorAction SilentlyContinue)) { throw "Docker Desktop is not installed or docker.exe is not on PATH." }
    if ($null -eq (Get-Command wsl.exe -ErrorAction SilentlyContinue)) { throw "WSL2 is not installed. Enable WSL2 before setup." }
    $serverService = Get-CimInstance Win32_Service -Filter "Name='LanmanServer'" -ErrorAction SilentlyContinue
    if ($null -eq $serverService -or [string]$serverService.StartMode -ne "Auto") {
        throw "The Windows Server service (LanmanServer) must exist and use Automatic startup for Docker Desktop."
    }
    $wslVersion = Invoke-FbNative -FilePath "wsl.exe" -Arguments @("--version") -Description "Verify the installed WSL version"
    # Windows PowerShell 5.1 can surface wsl.exe's UTF-16 output with embedded NULs.
    # Normalise them before applying version/state checks.
    $wslVersionText = ($wslVersion.Output -join [Environment]::NewLine).Replace([string][char]0, "")
    $wslVersionMatch = [regex]::Match($wslVersionText, '(?im)^WSL\s+version:\s*(?<version>\d+\.\d+\.\d+)')
    if (-not $wslVersionMatch.Success -or [Version]::Parse($wslVersionMatch.Groups['version'].Value) -lt [Version]::Parse("2.1.5")) {
        throw "WSL 2.1.5 or newer is required. Run 'wsl --update' and retry."
    }
    $docker = Invoke-FbNative -FilePath "docker" -Arguments @("version", "--format", "{{.Server.Os}}") -Description "Verify Docker Desktop Linux engine"
    if ((($docker.Output -join "").Trim()).ToLowerInvariant() -ne "linux") {
        throw "Docker Desktop must be running the Linux container engine."
    }
    $dockerArchitecture = Invoke-FbNative -FilePath "docker" -Arguments @("info", "--format", "{{.Architecture}}") -Description "Verify Docker engine architecture"
    if ((($dockerArchitecture.Output -join "").Trim()).ToLowerInvariant() -notin @("x86_64", "amd64")) {
        throw "Docker Desktop must expose an x86-64 Linux engine for this windows-x64 release."
    }
    $existingContainers = Invoke-FbNative -FilePath "docker" -Arguments @(
        "ps", "--all",
        "--filter", "label=ie.filingbridge.deployment-mode=PrivateServer",
        "--format", "{{.ID}}") -Description "Detect an existing FilingBridge Private Server project"
    $existingVolumes = Invoke-FbNative -FilePath "docker" -Arguments @(
        "volume", "ls",
        "--filter", "label=ie.filingbridge.deployment-mode=PrivateServer",
        "--format", "{{.Name}}") -Description "Detect an existing FilingBridge Private Server data volume"
    $existingResources = @($existingContainers.Output + $existingVolumes.Output | ForEach-Object { $_.Trim() } | Where-Object { $_ } | Select-Object -Unique)
    if ($existingResources.Count -gt 0) {
        throw "An existing FilingBridge Private Server Docker project or data volume was detected. Use its saved state directory; setup will not create a competing installation."
    }
    $composeVersionResult = Invoke-FbNative -FilePath "docker" -Arguments @("compose", "version", "--short") -Description "Verify Docker Compose"
    $composeVersionText = ($composeVersionResult.Output -join "").Trim()
    $composeVersionMatch = [regex]::Match($composeVersionText, '(?<version>\d+\.\d+\.\d+)')
    if (-not $composeVersionMatch.Success -or [Version]::Parse($composeVersionMatch.Groups['version'].Value) -lt [Version]::Parse("2.20.0")) {
        throw "Docker Compose 2.20.0 or newer is required for health-aware startup."
    }
    $wsl = Invoke-FbNative -FilePath "wsl.exe" -Arguments @("--list", "--verbose") -Description "Verify WSL2"
    $wslListText = ($wsl.Output -join [Environment]::NewLine).Replace([string][char]0, "")
    if ($wslListText -notmatch '(?m)\s2\s*$') {
        throw "No WSL2 distribution was detected. Docker Desktop must use WSL2."
    }
}

function ConvertTo-FbTenantSlug {
    param([string]$Value)
    $slug = $Value.Trim().ToLowerInvariant()
    $slug = [regex]::Replace($slug, '[^a-z0-9]+', '-')
    $slug = $slug.Trim('-')
    if ($slug.Length -gt 50) { $slug = $slug.Substring(0, 50).TrimEnd('-') }
    if ($slug -notmatch '^[a-z0-9][a-z0-9-]{2,49}$') { throw "Tenant slug must contain 3-50 lowercase letters, numbers, or dashes." }
    return $slug
}

function Read-FbRequiredInput {
    param([string]$Value, [string]$Prompt, [switch]$NonInteractive)
    if (-not [string]::IsNullOrWhiteSpace($Value)) { return $Value.Trim() }
    if ($NonInteractive) { throw "$Prompt is required in non-interactive mode." }
    $read = Read-Host $Prompt
    if ([string]::IsNullOrWhiteSpace($read)) { throw "$Prompt is required." }
    return $read.Trim()
}

function New-FbInitialSecrets {
    param([string]$SecretDirectory, [string]$OwnerPassword)
    $postgresPassword = New-PrivateServerRandomSecret
    $applicationPassword = New-PrivateServerRandomSecret
    $apiKey = New-PrivateServerRandomSecret
    $secrets = [ordered]@{
        postgres_password                      = $postgresPassword
        postgres_application_password          = $applicationPassword
        accounts_migration_connection_string   = "Host=db;Port=5432;Database=accounts;Username=accounts;Password=$postgresPassword;SSL Mode=Disable;Timeout=15;Command Timeout=120"
        accounts_application_connection_string = "Host=db;Port=5432;Database=accounts;Username=accounts_api;Password=$applicationPassword;SSL Mode=Disable;Timeout=15;Command Timeout=120"
        auth_session_signing_key                = New-PrivateServerRandomSecret
        audit_integrity_signing_key              = New-PrivateServerRandomSecret
        database_tenant_context_key              = New-PrivateServerRandomSecret
        identity_hmac_key                        = New-PrivateServerRandomSecret
        mfa_encryption_key                       = New-PrivateServerRandomSecret
        backup_authentication_key                = New-PrivateServerRandomSecret
        accounts_api_key_hash                    = Get-FbSha256Text $apiKey
        accounts_api_key                         = $apiKey
        private_initial_owner_password            = $OwnerPassword
    }
    foreach ($entry in $secrets.GetEnumerator()) {
        $path = Join-Path $SecretDirectory $entry.Key
        Write-FbTextExclusive -Path $path -Value ([string]$entry.Value)
        Set-FbRestrictedAcl -Path $path
    }
}

function Remove-FbEphemeralOwnerPassword {
    param($State)
    $path = Join-Path ([string]$State.secretDirectory) "private_initial_owner_password"
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        Remove-Item -LiteralPath $path -Force
    }
    # Compose resolves every declared secret file even when the initialize profile is
    # inactive. Retain a deliberately invalid, non-secret sentinel at the same path so
    # routine lifecycle commands continue to render after the real password is erased.
    Write-FbTextExclusive -Path $path -Value "INITIALIZATION-COMPLETE-NO-PASSWORD"
    Set-FbRestrictedAcl -Path $path
}

function Wait-FbUriHealth {
    param([string]$Uri, [int]$TimeoutSeconds = 300, [switch]$DryRun)
    if ($DryRun) { Write-Host "DRY RUN: wait for readiness at $Uri"; return }
    if ($null -ne $script:PrivateServerCommandInvoker) { return }
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $handler = New-Object Net.Http.HttpClientHandler
    $handler.AllowAutoRedirect = $false
    $client = New-Object Net.Http.HttpClient($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(10)
    try {
        do {
            try {
                $response = $client.GetAsync($Uri).GetAwaiter().GetResult()
                if ([int]$response.StatusCode -eq 200) { return }
            } catch { }
            Start-Sleep -Seconds 2
        } while ((Get-Date) -lt $deadline)
    } finally {
        $client.Dispose()
        $handler.Dispose()
    }
    throw "FilingBridge did not become ready at $Uri within $TimeoutSeconds seconds."
}

function Wait-FbHttpHealth {
    param([int]$Port, [int]$TimeoutSeconds = 300, [switch]$DryRun)
    Wait-FbUriHealth -Uri "http://127.0.0.1:$Port/health/ready" -TimeoutSeconds $TimeoutSeconds -DryRun:$DryRun
}

function Invoke-FbSetup {
    param(
        [string]$StateDirectory,
        [string]$RepositoryRoot,
        [string]$ReleaseManifest,
        [string]$TenantName,
        [string]$TenantSlug,
        [string]$OwnerEmail,
        [string]$OwnerName,
        [string]$PublicOrigin,
        [int]$Port,
        [switch]$DryRun,
        [switch]$NonInteractive,
        [switch]$BuildLocal,
        [switch]$SkipPrerequisiteChecks
    )
    if ($SkipPrerequisiteChecks -and $null -eq $script:PrivateServerCommandInvoker) {
        throw "Skipping prerequisite checks is available only through the injected operator test seam."
    }
    $resolvedStateDirectory = Resolve-FbStateDirectory $StateDirectory
    $composeFile = Get-FbComposeFile $RepositoryRoot
    if (Test-FbPathWithin $resolvedStateDirectory $RepositoryRoot) {
        throw "Private Server state must be outside the release/source directory."
    }
    Assert-FbNoReparseAncestor $resolvedStateDirectory "State directory"
    if (Test-Path -LiteralPath $resolvedStateDirectory) {
        throw "Setup refuses to overwrite existing state at $resolvedStateDirectory. Use status or diagnose; use purge-data only when intentional."
    }
    Assert-FbPrerequisites -StateDirectory $resolvedStateDirectory -RepositoryRoot $RepositoryRoot -Port $Port -SkipExternalChecks:$SkipPrerequisiteChecks
    $tenant = Read-FbRequiredInput $TenantName "Organisation/tenant name" -NonInteractive:$NonInteractive
    if ([string]::IsNullOrWhiteSpace($TenantSlug)) { $TenantSlug = ConvertTo-FbTenantSlug $tenant } else { $TenantSlug = ConvertTo-FbTenantSlug $TenantSlug }
    $email = Read-FbRequiredInput $OwnerEmail "Owner email" -NonInteractive:$NonInteractive
    if ($email -notmatch '^[^\s@]+@[^\s@]+\.[^\s@]+$') { throw "Owner email is not valid." }
    $displayName = Read-FbRequiredInput $OwnerName "Owner display name" -NonInteractive:$NonInteractive
    foreach ($value in @($tenant, $TenantSlug, $email, $displayName)) {
        if ($value.Length -gt 160 -or $value -match "[\r\n\x00]") { throw "Setup identity fields must be single-line values of at most 160 characters." }
    }
    $localOrigin = "http://localhost:$Port"
    if ([string]::IsNullOrWhiteSpace($PublicOrigin)) { $PublicOrigin = $localOrigin }
    $requestedPublicOrigin = $PublicOrigin
    $originUri = $null
    if (-not [Uri]::TryCreate($PublicOrigin, [UriKind]::Absolute, [ref]$originUri)) { throw "Public origin must be an absolute URL." }
    if ($originUri.AbsolutePath -ne "/" -or -not [string]::IsNullOrWhiteSpace($originUri.Query) -or -not [string]::IsNullOrWhiteSpace($originUri.Fragment) -or -not [string]::IsNullOrWhiteSpace($originUri.UserInfo)) {
        throw "Public origin must contain only scheme, host, and optional port."
    }
    if ($PublicOrigin -cne $localOrigin) {
        if ($originUri.Scheme -cne "https" -or $originUri.Host -notmatch '^[A-Za-z0-9](?:[A-Za-z0-9.-]*[A-Za-z0-9])?\.ts\.net$' -or -not $originUri.IsDefaultPort) {
            throw "Remote Private Server origin must be exact Tailscale HTTPS in the form https://machine.tailnet.ts.net (default port 443, no credentials or path)."
        }
    }

    $instanceId = [Guid]::NewGuid().ToString("D")
    $release = $null
    if ($BuildLocal) {
        if (-not [string]::IsNullOrWhiteSpace($ReleaseManifest)) { throw "-BuildLocal and -ReleaseManifest cannot be combined." }
        if ($DryRun) {
            $release = [pscustomobject]@{
                path = ""; sha256 = ""; version = "source-build"; commitSha = ""; reviewed = $false; integrityStatus = "source-build-unreviewed"
                images = [pscustomobject]@{ api = "filingbridge-private-api:<generated>"; frontend = "filingbridge-private-frontend:<generated>"; postgres = "postgres:16.4-alpine" }
            }
        } else {
            $release = New-FbLocalBuildRelease -RepositoryRoot $RepositoryRoot -InstanceId $instanceId
        }
    } else {
        $manifestPath = Get-FbReleaseManifestPath $ReleaseManifest $RepositoryRoot
        $release = Read-FbReleaseManifest -Path $manifestPath -RepositoryRoot $RepositoryRoot
    }

    if ($DryRun) {
        Write-Host "DRY RUN: setup would create an isolated state directory at $resolvedStateDirectory"
        Write-Host "DRY RUN: setup would create a unique Compose project and generated secret files"
        Write-Host "DRY RUN: setup would pull/build compiled images, migrate, initialize one Owner, and wait for real health"
        return
    }

    $ownerPassword = New-PrivateServerOwnerPassword
    $secretDirectory = Join-Path $resolvedStateDirectory "secrets"
    $stateWritten = $false
    try {
        New-Item -ItemType Directory -Path $secretDirectory -Force:$false | Out-Null
        Set-FbRestrictedAcl -Path $resolvedStateDirectory
        Set-FbRestrictedAcl -Path $secretDirectory
        New-FbInitialSecrets -SecretDirectory $secretDirectory -OwnerPassword $ownerPassword
        $installedComposeFile = Join-Path $resolvedStateDirectory "compose.private.installed.yml"
        Write-FbTextAtomic -Path $installedComposeFile -Value (Read-FbUtf8Text $composeFile)
        Set-FbRestrictedAcl -Path $installedComposeFile
        if (-not $BuildLocal -and (Get-FileHash -LiteralPath $installedComposeFile -Algorithm SHA256).Hash.ToLowerInvariant() -cne [string]$release.composeSha256) {
            throw "compose.private.yml changed after release verification; setup refused the mutable source and did not run Compose."
        }
        $now = (Get-Date).ToUniversalTime().ToString("o")
        $state = [pscustomobject][ordered]@{
            formatVersion        = $script:SupportedStateFormat
            status               = "initializing"
            instanceId           = $instanceId
            composeProject       = "filingbridge-" + $instanceId.Replace("-", "").Substring(0, 12)
            stateDirectory       = $resolvedStateDirectory
            releaseDirectory     = $RepositoryRoot
            secretDirectory      = $secretDirectory
            environmentFile      = Join-Path $resolvedStateDirectory $script:EnvironmentFileName
            installedComposeFile = $installedComposeFile
            composeFileSha256    = (Get-FileHash -LiteralPath $installedComposeFile -Algorithm SHA256).Hash.ToLowerInvariant()
            port                 = $Port
            localOrigin          = $localOrigin
            publicOrigin         = $localOrigin
            tenantName           = $tenant
            tenantSlug           = $TenantSlug
            ownerEmail           = $email.ToLowerInvariant()
            ownerName            = $displayName
            mfaKeyId             = "mfa-" + $instanceId.Replace("-", "").Substring(0, 12)
            auditKeyId           = "audit-" + $instanceId.Replace("-", "").Substring(0, 12)
            releaseVersion       = [string]$release.version
            releaseCommitSha     = [string]$release.commitSha
            releaseManifest      = [string]$release.path
            releaseManifestSha256 = [string]$release.sha256
            reviewedRelease      = [bool]$release.reviewed
            releaseIntegrityStatus = [string]$release.integrityStatus
            images               = $release.images
            tailscaleEnabled     = $false
            tailscaleDnsName     = ""
            backupRecipient      = ""
            createdAtUtc         = $now
            updatedAtUtc         = $now
        }
        Write-FbEnvironmentFile $state
        Save-FbState $state
        $stateWritten = $true
        $null = Invoke-FbCompose $state $installedComposeFile @("config", "--quiet") "Validate the isolated Private Server topology"
        if (-not $BuildLocal) {
            $null = Invoke-FbCompose $state $installedComposeFile @("pull", "--policy", "always") "Pull exact release images" -Mutating
        }
        $null = Invoke-FbCompose $state $installedComposeFile @("up", "-d", "--wait", "--wait-timeout", "300", "db") "Start and verify the isolated PostgreSQL service" -Mutating
        $null = Invoke-FbCompose $state $installedComposeFile @("run", "--rm", "--no-deps", "role-provision") "Provision the least-privileged database role" -Mutating
        $null = Invoke-FbCompose $state $installedComposeFile @("run", "--rm", "--no-deps", "migrate") "Run controlled database migrations" -Mutating
        $null = Invoke-FbCompose $state $installedComposeFile @("--profile", "initialize", "run", "--rm", "--no-deps", "private-initialize") "Initialize the empty Private Server database" -Mutating
        Remove-FbEphemeralOwnerPassword $state
        $null = Invoke-FbCompose $state $installedComposeFile @("up", "-d", "--no-deps", "--wait", "--wait-timeout", "300", "api", "frontend") "Start the compiled Private Server runtime" -Mutating
        Wait-FbHttpHealth -Port $Port
        if ($requestedPublicOrigin -cne $localOrigin) {
            if ($null -eq (Get-Command tailscale -ErrorAction SilentlyContinue) -and $null -eq $script:PrivateServerCommandInvoker) {
                throw "A remote -PublicOrigin requires Tailscale to be installed and connected."
            }
            $detectedDnsName = Get-FbTailscaleDnsName
            if ($requestedPublicOrigin -cne "https://$detectedDnsName") {
                throw "Requested Tailscale origin does not match this computer's detected Tailscale DNS name: https://$detectedDnsName"
            }
            Invoke-FbTailscale $state $installedComposeFile "enable"
        }
        $state.status = "ready"
        Save-FbState $state
        Write-Host "FilingBridge Private Server is ready at $localOrigin"
        Write-Host "Release status: $($state.releaseIntegrityStatus). A manifest integrity check is not an out-of-band ZIP checksum or signature."
        Write-Host "Workspace slug: $($state.tenantSlug)"
        Write-Host "Owner email: $($state.ownerEmail)"
        Write-Host "One-time Owner password: $ownerPassword"
        Write-Host "Store it in a password manager now. It is not retained by the operator."
    } catch {
        try {
            if ($stateWritten -and (Test-Path -LiteralPath (Join-Path $resolvedStateDirectory $script:StateFileName))) {
                $failed = Read-FbState $resolvedStateDirectory -AllowNonReady
                $failed.status = "setupFailed"
                Save-FbState $failed
            } elseif (Test-Path -LiteralPath $resolvedStateDirectory -PathType Container) {
                $resolvedIncomplete = [IO.Path]::GetFullPath($resolvedStateDirectory)
                if ($resolvedIncomplete -ne [IO.Path]::GetPathRoot($resolvedIncomplete) -and -not (Test-FbPathWithin $resolvedIncomplete $RepositoryRoot)) {
                    Remove-Item -LiteralPath $resolvedIncomplete -Recurse -Force
                }
            }
        } catch { }
        throw
    } finally {
        if (Test-Path -LiteralPath $secretDirectory) {
            $ephemeral = Join-Path $secretDirectory "private_initial_owner_password"
            try {
                if (Test-Path -LiteralPath $ephemeral) { Remove-Item -LiteralPath $ephemeral -Force -ErrorAction SilentlyContinue }
                Write-FbTextExclusive -Path $ephemeral -Value "INITIALIZATION-COMPLETE-NO-PASSWORD"
                Set-FbRestrictedAcl -Path $ephemeral
            } catch { }
        }
        $ownerPassword = $null
    }
}

function Get-FbOwnedRuntimeContainerId {
    param($State, [string]$ComposeFile, [string]$Service)
    if ($Service -notin @("db", "api", "frontend")) { throw "Unsupported Private Server runtime service '$Service'." }

    $idResult = Invoke-FbCompose $State $ComposeFile @("ps", "--all", "--quiet", $Service) "Resolve existing $Service runtime container"
    $containerIds = @($idResult.Output | ForEach-Object { $_.Trim().ToLowerInvariant() } | Where-Object { $_ -cmatch '^[a-f0-9]{12,64}$' } | Select-Object -Unique)
    if ($containerIds.Count -ne 1) {
        throw "Runtime container '$Service' does not exist exactly once. Daily start cannot build, migrate, seed, or recreate it; use a supported update/reinstall workflow."
    }

    $containerId = $containerIds[0]
    $labels = Invoke-FbNative -FilePath "docker" -Arguments @(
        "inspect", "--format",
        '{{index .Config.Labels "com.docker.compose.project"}}|{{index .Config.Labels "com.docker.compose.service"}}|{{index .Config.Labels "ie.filingbridge.deployment-mode"}}|{{index .Config.Labels "ie.filingbridge.installation-id"}}',
        $containerId
    ) -Description "Verify existing $Service runtime container ownership"
    $actual = (($labels.Output | Select-Object -Last 1) -join "").Trim()
    $expected = "$($State.composeProject)|$Service|PrivateServer|$($State.instanceId)"
    if ($actual -cne $expected) {
        throw "Runtime container '$Service' does not carry the exact saved project, service, deployment-mode, and installation ownership labels (expected '$expected'; received '$actual')."
    }
    return $containerId
}

function Start-FbExistingRuntimeContainers {
    param($State, [string]$ComposeFile, [string[]]$Services, [string]$Description, [switch]$DryRun)
    $requested = @($Services)
    $unexpected = @($requested | Where-Object { $_ -notin @("db", "api", "frontend") })
    if ($unexpected.Count -gt 0) { throw "Unsupported Private Server runtime service selection." }
    $runtime = @(@("db", "api", "frontend") | Where-Object { $requested -contains $_ })
    if ($runtime.Count -eq 0) { return }
    if ($DryRun) {
        Write-Host "DRY RUN: $Description (existing exact containers only): $($runtime -join ', ')"
        return
    }

    # Resolve and verify every immutable container identity before starting any
    # service. docker compose start traverses depends_on and rejects the removed
    # one-shot migration containers; docker start cannot create or recreate them.
    $containers = @{}
    foreach ($service in $runtime) {
        $containers[$service] = Get-FbOwnedRuntimeContainerId $State $ComposeFile $service
    }
    foreach ($service in $runtime) {
        $null = Invoke-FbNative -FilePath "docker" -Arguments @("start", [string]$containers[$service]) -Description "${Description}: start existing $service container" -Mutating
        Wait-FbContainerHealth $State $ComposeFile $service ([string]$containers[$service])
    }
}

function Invoke-FbStart {
    param($State, [string]$ComposeFile, [switch]$DryRun)
    Start-FbExistingRuntimeContainers $State $ComposeFile @("db", "api", "frontend") "Start Private Server without build, migration, seed, creation, or recreation" -DryRun:$DryRun
    Wait-FbHttpHealth -Port ([int]$State.port) -DryRun:$DryRun
    Write-Host "FilingBridge is ready at $($State.localOrigin)"
}

function Invoke-FbStop {
    param($State, [string]$ComposeFile, [switch]$DryRun)
    $null = Invoke-FbCompose $State $ComposeFile @("stop", "--timeout", "60", "frontend", "api", "db") "Stop Private Server while preserving data" -Mutating -DryRun:$DryRun
    Write-Host "FilingBridge is stopped. The PostgreSQL volume and private state were preserved."
}

function Invoke-FbStatus {
    param($State, [string]$ComposeFile)
    Write-Host "Instance: $($State.instanceId)"
    Write-Host "State: $($State.status)"
    Write-Host "Release: $($State.releaseVersion)"
    Write-Host "Release commit: $([string](Get-FbProperty $State 'releaseCommitSha' '[source-build]'))"
    Write-Host "Release integrity: $([string](Get-FbProperty $State 'releaseIntegrityStatus' 'unknown'))"
    Write-Host "API image: $($State.images.api)"
    Write-Host "Frontend image: $($State.images.frontend)"
    Write-Host "PostgreSQL image: $($State.images.postgres)"
    Write-Host "Workspace slug: $($State.tenantSlug)"
    Write-Host "Local URL: $($State.localOrigin)"
    Write-Host "Private URL: $($State.publicOrigin)"
    if ([string]$State.status -eq "updateFailed") {
        $recoveryBackup = [string](Get-FbProperty $State "lastPreUpdateBackup" "")
        if (-not [string]::IsNullOrWhiteSpace($recoveryBackup)) {
            Write-Host "Verified pre-update recovery set: $recoveryBackup"
        }
        Write-Host "Recovery: run the explicit restore command from the previous installed release directory; changing an image cannot reverse a migration."
    }
    $result = Invoke-FbCompose $State $ComposeFile @("ps", "--format", "table {{.Service}}\t{{.State}}\t{{.Health}}") "Read Private Server container status" -IgnoreExitCode
    foreach ($line in $result.Output) { Write-Host (Protect-PrivateServerText $line) }
    try {
        if ($null -eq $script:PrivateServerCommandInvoker) {
            $handler = New-Object Net.Http.HttpClientHandler
            $handler.AllowAutoRedirect = $false
            $client = New-Object Net.Http.HttpClient($handler)
            $client.Timeout = [TimeSpan]::FromSeconds(5)
            try {
                $response = $client.GetAsync("http://127.0.0.1:$($State.port)/health/ready").GetAwaiter().GetResult()
                Write-Host "Loopback readiness: HTTP $([int]$response.StatusCode)"
            } finally { $client.Dispose(); $handler.Dispose() }
        }
    } catch { Write-Host "Loopback readiness: unavailable" }
}

function Invoke-FbLogs {
    param($State, [string]$ComposeFile, [int]$TailLines)
    $result = Invoke-FbCompose $State $ComposeFile @("logs", "--no-color", "--tail", [string]$TailLines, "api", "frontend") "Read bounded Private Server logs" -IgnoreExitCode
    foreach ($line in @($result.Output | Select-Object -Last $TailLines)) {
        Write-Host (Protect-PrivateServerText $line)
    }
}

function Get-FbTailscaleDnsName {
    $status = Invoke-FbNative -FilePath "tailscale" -Arguments @("status", "--json") -Description "Read Tailscale status"
    $json = ($status.Output -join [Environment]::NewLine) | ConvertFrom-Json
    $self = Get-FbProperty $json "Self"
    $dnsName = [string](Get-FbProperty $self "DNSName" "")
    $dnsName = $dnsName.Trim().TrimEnd('.')
    if ($dnsName -notmatch '^[A-Za-z0-9.-]+\.ts\.net$') { throw "Tailscale did not report a valid tailnet DNS name for this computer." }
    return $dnsName.ToLowerInvariant()
}

function Test-FbWindowsAdministrator {
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { return $false }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-FbTailscaleProxyTargets {
    param($Value)
    $targets = New-Object System.Collections.Generic.List[string]
    $pending = New-Object System.Collections.Stack
    $pending.Push($Value)
    while ($pending.Count -gt 0) {
        $current = $pending.Pop()
        if ($null -eq $current -or $current -is [string] -or $current.GetType().IsPrimitive) { continue }
        if ($current -is [Collections.IDictionary]) {
            foreach ($key in $current.Keys) {
                $child = $current[$key]
                if ([string]$key -ceq "Proxy" -and $child -is [string]) { $targets.Add([string]$child) }
                else { $pending.Push($child) }
            }
            continue
        }
        if ($current -is [Collections.IEnumerable] -and $current -isnot [pscustomobject]) {
            foreach ($child in $current) { $pending.Push($child) }
            continue
        }
        foreach ($property in $current.PSObject.Properties) {
            if ($property.Name -ceq "Proxy" -and $property.Value -is [string]) { $targets.Add([string]$property.Value) }
            else { $pending.Push($property.Value) }
        }
    }
    return $targets.ToArray()
}

function Test-FbJsonObjectValue {
    param($Value)
    if ($null -eq $Value) { return $false }
    if ($Value -is [Collections.IDictionary]) { return $true }
    $baseObject = $Value.PSObject.BaseObject
    return $null -ne $baseObject -and $baseObject.GetType().FullName -eq "System.Management.Automation.PSCustomObject"
}

function Assert-FbOwnedTailscaleServeRoute {
    param($Status, $State)
    if (-not (Test-FbJsonObjectValue $Status)) {
        throw "Tailscale Serve route ownership cannot be proven from a non-object status response. No Serve route was changed."
    }
    $expected = "http://127.0.0.1:$($State.port)"
    $topProperties = @($Status.PSObject.Properties)
    $unknownTop = @($topProperties | Where-Object { $_.Name -notin @("TCP", "Web", "AllowFunnel", "Foreground", "Services", "version") })
    $tcp = Get-FbProperty $Status "TCP"
    $web = Get-FbProperty $Status "Web"
    if (-not (Test-FbJsonObjectValue $tcp) -or -not (Test-FbJsonObjectValue $web)) {
        throw "Tailscale Serve route ownership cannot be proven from malformed TCP/Web configuration. No Serve route was changed."
    }
    [object[]]$tcpProperties = if ($null -eq $tcp) { @() } else { @($tcp.PSObject.Properties) }
    [object[]]$webProperties = if ($null -eq $web) { @() } else { @($web.PSObject.Properties) }
    $expectedWebName = "$([string](Get-FbProperty $State 'tailscaleDnsName' '')):443"
    $tcp443 = if ($tcpProperties.Count -eq 1 -and $tcpProperties[0].Name -ceq "443") { $tcpProperties[0].Value } else { $null }
    [object[]]$tcp443Properties = if ($null -eq $tcp443) { @() } else { @($tcp443.PSObject.Properties) }
    $web443 = if ($webProperties.Count -eq 1 -and $webProperties[0].Name.EndsWith(":443", [StringComparison]::OrdinalIgnoreCase)) { $webProperties[0].Value } else { $null }
    $handlers = Get-FbProperty $web443 "Handlers"
    [object[]]$handlerProperties = if ($null -eq $handlers) { @() } else { @($handlers.PSObject.Properties) }
    $rootHandler = if ($handlerProperties.Count -eq 1 -and $handlerProperties[0].Name -ceq "/") { $handlerProperties[0].Value } else { $null }
    [object[]]$rootProperties = if ($null -eq $rootHandler) { @() } else { @($rootHandler.PSObject.Properties) }
    $funnel = Get-FbProperty $Status "AllowFunnel"
    $funnelEnabled = $false
    $funnelMalformed = $null -ne $funnel -and -not (Test-FbJsonObjectValue $funnel)
    if ($null -ne $funnel -and -not $funnelMalformed) {
        foreach ($property in @($funnel.PSObject.Properties)) {
            if ($property.Value -isnot [bool] -or [bool]$property.Value) { $funnelEnabled = $true }
        }
    }
    $foreground = Get-FbProperty $Status "Foreground"
    $services = Get-FbProperty $Status "Services"
    $version = [string](Get-FbProperty $Status "version" "")
    $versionInvalid = $version.Length -gt 256 -or $version -match '[\x00\r\n]'
    $foregroundMalformed = $null -ne $foreground -and -not (Test-FbJsonObjectValue $foreground)
    $servicesMalformed = $null -ne $services -and -not (Test-FbJsonObjectValue $services)
    $foregroundNonempty = $foregroundMalformed -or ($null -ne $foreground -and @($foreground.PSObject.Properties).Count -gt 0)
    $servicesNonempty = $servicesMalformed -or ($null -ne $services -and @($services.PSObject.Properties).Count -gt 0)
    if ($unknownTop.Count -gt 0 -or
        -not (Test-FbJsonObjectValue $tcp443) -or -not (Test-FbJsonObjectValue $web443) -or
        -not (Test-FbJsonObjectValue $handlers) -or -not (Test-FbJsonObjectValue $rootHandler) -or
        $tcpProperties.Count -ne 1 -or $tcp443Properties.Count -ne 1 -or
        $tcp443Properties[0].Name -cne "HTTPS" -or $tcp443Properties[0].Value -isnot [bool] -or -not [bool]$tcp443Properties[0].Value -or
        $webProperties.Count -ne 1 -or $webProperties[0].Name -cne $expectedWebName -or $handlerProperties.Count -ne 1 -or
        $rootProperties.Count -ne 1 -or $rootProperties[0].Name -cne "Proxy" -or
        [string]$rootProperties[0].Value -cne $expected -or $funnelMalformed -or $funnelEnabled -or
        $foregroundNonempty -or $servicesNonempty -or $versionInvalid) {
        throw "Tailscale Serve route ownership cannot be proven for this FilingBridge installation. No Serve route was changed; inspect 'tailscale serve status' manually."
    }
}

function Test-FbEmptyTailscaleServeStatus {
    param($Status)
    if (-not (Test-FbJsonObjectValue $Status)) { return $false }
    $properties = @($Status.PSObject.Properties)
    if ($properties.Count -eq 0) { return $true }
    foreach ($property in $properties) {
        if ($property.Name -eq "version") {
            if ($property.Value -isnot [string] -or ([string]$property.Value).Length -gt 256 -or [string]$property.Value -match '[\x00\r\n]') { return $false }
            continue
        }
        if ($property.Name -notin @("TCP", "Web", "AllowFunnel", "Foreground", "Services")) { return $false }
        if ($null -ne $property.Value -and -not (Test-FbJsonObjectValue $property.Value)) { return $false }
        if ($null -ne $property.Value -and @($property.Value.PSObject.Properties).Count -gt 0) { return $false }
    }
    return $true
}

function Read-FbTailscaleServeJson {
    $result = Invoke-FbNative -FilePath "tailscale" -Arguments @("serve", "status", "--json") -Description "Read exact Tailscale Serve route ownership"
    $text = ($result.Output -join [Environment]::NewLine).Trim()
    if ([string]::IsNullOrWhiteSpace($text)) { return [pscustomobject]@{} }
    try { return $text | ConvertFrom-Json -ErrorAction Stop } catch { throw "Tailscale Serve returned malformed JSON; no route was changed." }
}

function Invoke-FbTailscale {
    param($State, [string]$ComposeFile, [string]$Action, [switch]$DryRun)
    $normalized = $Action.Trim().ToLowerInvariant()
    if ($normalized -notin @("enable", "disable", "status")) { throw "tailscale requires one action: enable, disable, or status." }
    if ($null -eq (Get-Command tailscale -ErrorAction SilentlyContinue) -and $null -eq $script:PrivateServerCommandInvoker) {
        throw "Tailscale is not installed or tailscale.exe is not on PATH."
    }
    if ($normalized -eq "status") {
        $result = Invoke-FbNative -FilePath "tailscale" -Arguments @("serve", "status") -Description "Read Tailscale Serve status" -IgnoreExitCode
        foreach ($line in $result.Output) { Write-Host (Protect-PrivateServerText $line) }
        return
    }
    if (-not $DryRun -and $null -eq $script:PrivateServerCommandInvoker -and -not (Test-FbWindowsAdministrator)) {
        throw "Changing Tailscale Serve on Windows requires an Administrator terminal. Reopen the terminal as Administrator and retry only this command."
    }
    if ($normalized -eq "enable") {
        $dnsName = if ($DryRun) { "this-machine.tailnet.ts.net" } else { Get-FbTailscaleDnsName }
        $origin = "https://$dnsName"
        if (-not $DryRun) {
            $existingStatus = Read-FbTailscaleServeJson
            if (-not (Test-FbEmptyTailscaleServeStatus $existingStatus)) {
                if (-not [bool](Get-FbProperty $State "tailscaleEnabled" $false)) {
                    throw "Tailscale already has a Serve configuration. FilingBridge refuses to overwrite an unrelated route; inspect 'tailscale serve status' first."
                }
                Assert-FbOwnedTailscaleServeRoute $existingStatus $State
                Wait-FbUriHealth -Uri "$origin/health/ready" -TimeoutSeconds 120
                Write-Host "Private HTTPS is already enabled at $origin"
                return
            }
        }
        $previousOrigin = [string]$State.publicOrigin
        $State.publicOrigin = $origin
        $State.tailscaleDnsName = $dnsName
        $State.tailscaleEnabled = $true
        if (-not $DryRun) { Write-FbEnvironmentFile $State; Save-FbState $State }
        try {
            $null = Invoke-FbCompose $State $ComposeFile @("up", "-d", "--no-deps", "--force-recreate", "api", "frontend") "Apply the exact private HTTPS origin" -Mutating -DryRun:$DryRun
            Wait-FbHttpHealth -Port ([int]$State.port) -DryRun:$DryRun
            $null = Invoke-FbNative -FilePath "tailscale" -Arguments @("serve", "--bg", "--yes", "--https=443", "http://127.0.0.1:$($State.port)") -Description "Enable private Tailscale Serve HTTPS" -Mutating -DryRun:$DryRun
            Wait-FbUriHealth -Uri "$origin/health/ready" -TimeoutSeconds 120 -DryRun:$DryRun
            Write-Host "Private HTTPS is enabled at $origin"
        } catch {
            $failure = $_
            $routeDisabled = $false
            try {
                $null = Invoke-FbNative -FilePath "tailscale" -Arguments @("serve", "--https=443", "off") -Description "Roll back the failed FilingBridge Tailscale Serve route" -Mutating -DryRun:$DryRun
                if (-not $DryRun) {
                    $afterOff = Read-FbTailscaleServeJson
                    if (-not (Test-FbEmptyTailscaleServeStatus $afterOff)) { throw "Tailscale Serve reports remaining or unknown configuration after the off command." }
                }
                $routeDisabled = $true
            } catch {
                if ($null -eq $State.PSObject.Properties["tailscaleRecoveryRequired"]) { $State | Add-Member -NotePropertyName tailscaleRecoveryRequired -NotePropertyValue $true }
                else { $State.tailscaleRecoveryRequired = $true }
                if (-not $DryRun) { Save-FbState $State }
                throw "Tailscale HTTPS verification failed and FilingBridge could not prove that its Serve route was removed. The saved state still records the route as enabled. Inspect and disable the exact route manually; no ownership state was discarded. Original failure: $($failure.Exception.Message) Route cleanup failure: $($_.Exception.Message)"
            }
            if (-not $routeDisabled) { throw "Tailscale route cleanup was not proven." }
            $State.publicOrigin = $previousOrigin
            $State.tailscaleDnsName = ""
            $State.tailscaleEnabled = $false
            if ($null -ne $State.PSObject.Properties["tailscaleRecoveryRequired"]) { $State.tailscaleRecoveryRequired = $false }
            if (-not $DryRun) {
                Write-FbEnvironmentFile $State
                Save-FbState $State
                $null = Invoke-FbCompose $State $ComposeFile @("up", "-d", "--no-deps", "--force-recreate", "api", "frontend") "Restore the previous origin after failed Tailscale verification" -Mutating
                Wait-FbHttpHealth -Port ([int]$State.port) -TimeoutSeconds 120
            }
            throw "Tailscale Serve did not pass the real external HTTPS readiness probe; its route and origin configuration were rolled back. $($failure.Exception.Message)"
        }
        return
    }

    if (-not [bool](Get-FbProperty $State "tailscaleEnabled" $false)) {
        throw "This FilingBridge installation has no recorded Tailscale Serve route to disable."
    }
    if (-not $DryRun) {
        $ownedStatus = Read-FbTailscaleServeJson
        Assert-FbOwnedTailscaleServeRoute $ownedStatus $State
    }
    $null = Invoke-FbNative -FilePath "tailscale" -Arguments @("serve", "--https=443", "off") -Description "Disable the FilingBridge Tailscale Serve route" -Mutating -DryRun:$DryRun
    if (-not $DryRun) {
        if (-not (Test-FbEmptyTailscaleServeStatus (Read-FbTailscaleServeJson))) {
            throw "Tailscale Serve did not confirm an empty semantic configuration after removal of the owned route. Saved ownership state was retained."
        }
    }
    $State.publicOrigin = [string]$State.localOrigin
    $State.tailscaleDnsName = ""
    $State.tailscaleEnabled = $false
    if (-not $DryRun) { Write-FbEnvironmentFile $State; Save-FbState $State }
    $null = Invoke-FbCompose $State $ComposeFile @("up", "-d", "--no-deps", "--force-recreate", "api", "frontend") "Return FilingBridge to local-only origin" -Mutating -DryRun:$DryRun
    Wait-FbHttpHealth -Port ([int]$State.port) -DryRun:$DryRun
    Write-Host "Tailscale Serve is disabled. FilingBridge remains available locally."
}

function Remove-FbTemporaryDirectory {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) { return }
    $resolved = [IO.Path]::GetFullPath($Path)
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (-not (Test-FbPathWithin $resolved $temporaryRoot) -or $resolved.Equals($temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a staging directory outside the operating-system temporary directory: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

function Get-FbRunningServices {
    param($State, [string]$ComposeFile)
    $result = Invoke-FbCompose $State $ComposeFile @("ps", "--status", "running", "--services") "Record current Private Server service state"
    $services = @($result.Output | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    $unexpected = @($services | Where-Object { $_ -notin @("db", "api", "frontend") })
    if ($unexpected.Count -gt 0 -or @($services | Select-Object -Unique).Count -ne $services.Count) {
        throw "Private Server service state could not be interpreted safely; no backup, restore, or update was attempted."
    }
    return $services
}

function Wait-FbServiceHealth {
    param($State, [string]$ComposeFile, [string[]]$Services, [int]$TimeoutSeconds = 300, [switch]$DryRun)
    $runtime = @($Services | Where-Object { $_ -in @("db", "api", "frontend") } | Select-Object -Unique)
    if ($runtime.Count -eq 0) { return }
    if ($DryRun) { Write-Host "DRY RUN: wait for healthy services: $($runtime -join ', ')"; return }
    foreach ($service in $runtime) {
        $containerId = Get-FbOwnedRuntimeContainerId $State $ComposeFile $service
        Wait-FbContainerHealth $State $ComposeFile $service $containerId -TimeoutSeconds $TimeoutSeconds
    }
}

function Wait-FbContainerHealth {
    param($State, [string]$ComposeFile, [string]$Service, [string]$ContainerId, [int]$TimeoutSeconds = 300)
    if ($null -ne $script:PrivateServerCommandInvoker) { return }
    if ($Service -notin @("db", "api", "frontend") -or $ContainerId -cnotmatch '^[a-f0-9]{12,64}$') {
        throw "Private Server service health target is invalid."
    }
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $health = Invoke-FbNative -FilePath "docker" -Arguments @(
            "inspect", "--format", "{{.State.Status}}|{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}", $ContainerId
        ) -Description "Read $Service container health" -IgnoreExitCode
        $value = (($health.Output | Select-Object -Last 1) -join "").Trim().ToLowerInvariant()
        if ($health.ExitCode -eq 0 -and $value -eq "running|healthy") { break }
        if ($value.StartsWith("exited|", [StringComparison]::Ordinal) -or $value.StartsWith("dead|", [StringComparison]::Ordinal)) {
            throw "Private Server service '$Service' exited before becoming healthy."
        }
        Start-Sleep -Seconds 2
    } while ((Get-Date) -lt $deadline)
    if ($health.ExitCode -ne 0 -or $value -ne "running|healthy") {
        throw "Private Server service '$Service' did not become healthy within $TimeoutSeconds seconds."
    }
}

function Start-FbDatabaseForOperation {
    param($State, [string]$ComposeFile, [string]$Description, [switch]$DryRun)
    Start-FbExistingRuntimeContainers $State $ComposeFile @("db") $Description -DryRun:$DryRun
}

function Restore-FbRunningServices {
    param($State, [string]$ComposeFile, [string[]]$PreviouslyRunning, [switch]$DryRun)
    $runtime = @($PreviouslyRunning | Where-Object { $_ -in @("db", "api", "frontend") } | Select-Object -Unique)
    if ($runtime.Count -gt 0) {
        Start-FbExistingRuntimeContainers $State $ComposeFile $runtime "Restore the previous service state" -DryRun:$DryRun
    }
}

function Assert-FbWritersQuiesced {
    param($State, [string]$ComposeFile, [string]$Operation)
    $running = @(Get-FbRunningServices $State $ComposeFile)
    if ($running -contains "api" -or $running -contains "frontend") {
        throw "$Operation requires API/frontend writers to be stopped, but a writer still reports running."
    }
    if ($running -notcontains "db") { throw "$Operation requires healthy PostgreSQL to remain running." }
}

function Get-FbHostBackupMount {
    param([string]$HostPath)
    $resolved = [IO.Path]::GetFullPath($HostPath)
    Assert-FbSafeOperatorPath $resolved "Backup staging path"
    $parent = Split-Path -Parent $resolved
    $leaf = [IO.Path]::GetFileName($resolved)
    if ([string]::IsNullOrWhiteSpace($leaf) -or $leaf -match '[\\/:]') { throw "Backup staging filename is unsafe." }
    return [pscustomobject]@{
        hostDirectory = $parent
        containerPath = "/backup/$leaf"
        volume = "$(ConvertTo-FbDockerPath $parent):/backup:rw"
    }
}

function New-FbDatabaseDump {
    param($State, [string]$ComposeFile, [string]$HostDumpPath, [switch]$DryRun)
    $mount = Get-FbHostBackupMount $HostDumpPath
    $dumpScript = 'export PGPASSWORD="$(cat /run/secrets/postgres_password)"; umask 077; rm -f "$1"; exec pg_dump --host db --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" --format=custom --no-owner --no-acl --file "$1"'
    $null = Invoke-FbCompose $State $ComposeFile @(
        "run", "--rm", "--no-deps", "--entrypoint", "/bin/sh", "--volume", $mount.volume,
        "role-provision", "-ec", $dumpScript, "filingbridge-backup", $mount.containerPath
    ) "Create a PostgreSQL custom-format dump directly in private host staging" -Mutating -DryRun:$DryRun
    if (-not $DryRun -and (-not (Test-Path -LiteralPath $HostDumpPath -PathType Leaf) -or (Get-Item -LiteralPath $HostDumpPath -Force).Length -le 0)) {
        throw "PostgreSQL backup output is missing or empty."
    }
}

function Get-FbImportantTableEvidence {
    param(
        $State,
        [string]$ComposeFile,
        [string]$Database,
        [string]$Description = "Read important-table fingerprints"
    )
    $query = @'
SELECT json_build_object(
  'tables',
  json_agg(
    json_build_object(
      'table', evidence.table_name,
      'rowCount', evidence.row_count,
      'fingerprint', evidence.fingerprint
    ) ORDER BY evidence.table_name
  )
)::text
FROM (
  SELECT 'accounting_periods' AS table_name, count(*)::bigint AS row_count,
         md5(coalesce(string_agg(row_hash, '' ORDER BY row_hash), '')) AS fingerprint
  FROM (SELECT md5(row_to_json(value)::text) AS row_hash FROM public.accounting_periods value) rows
  UNION ALL
  SELECT 'audit_logs', count(*)::bigint,
         md5(coalesce(string_agg(row_hash, '' ORDER BY row_hash), ''))
  FROM (SELECT md5(row_to_json(value)::text) AS row_hash FROM public.audit_logs value) rows
  UNION ALL
  SELECT 'companies', count(*)::bigint,
         md5(coalesce(string_agg(row_hash, '' ORDER BY row_hash), ''))
  FROM (SELECT md5(row_to_json(value)::text) AS row_hash FROM public.companies value) rows
  UNION ALL
  SELECT 'tenants', count(*)::bigint,
         md5(coalesce(string_agg(row_hash, '' ORDER BY row_hash), ''))
  FROM (SELECT md5(row_to_json(value)::text) AS row_hash FROM public.tenants value) rows
  UNION ALL
  SELECT 'user_accounts', count(*)::bigint,
         md5(coalesce(string_agg(row_hash, '' ORDER BY row_hash), ''))
  FROM (SELECT md5(row_to_json(value)::text) AS row_hash FROM public.user_accounts value) rows
) evidence;
'@
    $scriptText = 'export PGPASSWORD="$(cat /run/secrets/postgres_password)"; exec psql --username "$POSTGRES_USER" --dbname "$1" --no-align --tuples-only --set=ON_ERROR_STOP=1 --command "$2"'
    $result = Invoke-FbCompose $State $ComposeFile @("exec", "-T", "db", "sh", "-ec", $scriptText, "filingbridge-fingerprint", $Database, $query) $Description
    $jsonLine = @($result.Output | ForEach-Object { $_.Trim() } | Where-Object { $_.StartsWith('{') } | Select-Object -Last 1)
    if ($jsonLine.Count -ne 1) { throw "$Description did not return one JSON evidence object." }
    try { $parsed = $jsonLine[0] | ConvertFrom-Json -ErrorAction Stop } catch { throw "$Description returned malformed JSON evidence." }
    $tables = @(Get-FbProperty $parsed "tables" @())
    $required = @("accounting_periods", "audit_logs", "companies", "tenants", "user_accounts")
    if ($tables.Count -ne $required.Count) { throw "$Description did not cover every required table." }
    $normalized = New-Object System.Collections.Generic.List[object]
    foreach ($table in @($tables | Sort-Object { [string](Get-FbProperty $_ "table" "") })) {
        $name = [string](Get-FbProperty $table "table" "")
        $rowCount = [long](Get-FbProperty $table "rowCount" -1)
        $fingerprint = [string](Get-FbProperty $table "fingerprint" "")
        if ($required -notcontains $name -or $rowCount -lt 0 -or $fingerprint -cnotmatch '^[0-9a-f]{32}$') {
            throw "$Description returned invalid evidence for '$name'."
        }
        $normalized.Add([pscustomobject][ordered]@{ table = $name; rowCount = $rowCount; fingerprint = $fingerprint })
    }
    if ((@($normalized | ForEach-Object { $_.table }) -join '|') -cne ($required -join '|')) {
        throw "$Description returned duplicate or unexpected table evidence."
    }
    return $normalized.ToArray()
}

function Assert-FbImportantTableEvidenceMatches {
    param([object[]]$Expected, [object[]]$Actual, [string]$Description)
    $expectedItems = @($Expected | Sort-Object { [string](Get-FbProperty $_ "table" "") })
    $actualItems = @($Actual | Sort-Object { [string](Get-FbProperty $_ "table" "") })
    if ($expectedItems.Count -ne 5 -or $actualItems.Count -ne 5) { throw "$Description is incomplete." }
    for ($index = 0; $index -lt $expectedItems.Count; $index++) {
        $expectedTable = [string](Get-FbProperty $expectedItems[$index] "table" "")
        $actualTable = [string](Get-FbProperty $actualItems[$index] "table" "")
        if ($expectedTable -cne $actualTable -or
            [long](Get-FbProperty $expectedItems[$index] "rowCount" -1) -ne [long](Get-FbProperty $actualItems[$index] "rowCount" -2) -or
            [string](Get-FbProperty $expectedItems[$index] "fingerprint" "") -cne [string](Get-FbProperty $actualItems[$index] "fingerprint" "")) {
            throw "$Description differs for important table '$expectedTable'."
        }
    }
}

function Test-FbDatabaseDumpRestore {
    param($State, [string]$ComposeFile, [string]$HostDumpPath, [object[]]$ExpectedImportantTables = @(), [switch]$DryRun)
    $suffix = [Guid]::NewGuid().ToString("N").Substring(0, 16)
    $verifyDatabase = "fb_verify_$suffix"
    $mount = Get-FbHostBackupMount $HostDumpPath
    $verification = [ordered]@{
        database = $verifyDatabase
        tableCount = 0
        migrationCount = 0
        importantTables = @()
        fingerprintsMatched = $false
        verifiedAtUtc = ""
    }
    try {
        $scriptText = 'export PGPASSWORD="$(cat /run/secrets/postgres_password)"; dropdb --host db --username "$POSTGRES_USER" --if-exists "$1"; createdb --host db --username "$POSTGRES_USER" "$1"; pg_restore --host db --username "$POSTGRES_USER" --dbname "$1" --single-transaction --exit-on-error --no-owner --no-acl "$2"; psql --host db --username "$POSTGRES_USER" --dbname "$1" --no-align --tuples-only --command "SELECT count(*) FROM pg_catalog.pg_tables WHERE schemaname=''public''"'
        $result = Invoke-FbCompose $State $ComposeFile @(
            "run", "--rm", "--no-deps", "--entrypoint", "/bin/sh", "--volume", $mount.volume,
            "role-provision", "-ec", $scriptText, "filingbridge-verify", $verifyDatabase, $mount.containerPath
        ) "Restore the host-mounted dump into a disposable verification database" -Mutating -DryRun:$DryRun
        if (-not $DryRun) {
            $numbers = @($result.Output | ForEach-Object { $_.Trim() } | Where-Object { $_ -match '^\d+$' })
            if ($numbers.Count -eq 0 -or [int]$numbers[-1] -le 0) { throw "Disposable restore contained no public tables." }
            $verification.tableCount = [int]$numbers[-1]
            $migrationScript = 'export PGPASSWORD="$(cat /run/secrets/postgres_password)"; psql --username "$POSTGRES_USER" --dbname "$1" --no-align --tuples-only --command "SELECT count(*) FROM \"__EFMigrationsHistory\""'
            $migrationResult = Invoke-FbCompose $State $ComposeFile @("exec", "-T", "db", "sh", "-ec", $migrationScript, "filingbridge-verify", $verifyDatabase) "Verify EF migration history in the disposable database"
            $migrationNumbers = @($migrationResult.Output | ForEach-Object { $_.Trim() } | Where-Object { $_ -match '^\d+$' })
            if ($migrationNumbers.Count -eq 0 -or [int]$migrationNumbers[-1] -le 0) { throw "Disposable restore contained no EF migration history." }
            $verification.migrationCount = [int]$migrationNumbers[-1]
            $restoredEvidence = @(Get-FbImportantTableEvidence $State $ComposeFile $verifyDatabase "Read important-table fingerprints from the disposable restore")
            if (@($ExpectedImportantTables).Count -gt 0) {
                Assert-FbImportantTableEvidenceMatches -Expected @($ExpectedImportantTables) -Actual $restoredEvidence -Description "Source and disposable-restore evidence"
            }
            $verification.importantTables = $restoredEvidence
            $verification.fingerprintsMatched = (@($ExpectedImportantTables).Count -gt 0)
            $verification.verifiedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        }
        return [pscustomobject]$verification
    } finally {
        $cleanupScript = 'export PGPASSWORD="$(cat /run/secrets/postgres_password)"; exec dropdb --host db --username "$POSTGRES_USER" --if-exists --force "$1"'
        $null = Invoke-FbCompose $State $ComposeFile @(
            "run", "--rm", "--no-deps", "--entrypoint", "/bin/sh",
            "role-provision", "-ec", $cleanupScript, "filingbridge-cleanup", $verifyDatabase
        ) "Remove the disposable verification database" -Mutating -DryRun:$DryRun
    }
}

function Get-FbManagedOutputMarkerPath {
    param([string]$Directory)
    return Join-Path $Directory ".filingbridge-private-output.json"
}

function Test-FbManagedOutputDirectory {
    param([string]$Directory, $State, [string]$Purpose)
    $markerPath = Get-FbManagedOutputMarkerPath $Directory
    if (-not (Test-Path -LiteralPath $markerPath -PathType Leaf)) { return $false }
    try {
        $marker = (Read-FbUtf8Text $markerPath) | ConvertFrom-Json
        return [string](Get-FbProperty $marker "schemaVersion" "") -eq "filingbridge.private-server.output/v1"
            -and [string](Get-FbProperty $marker "instanceId" "") -eq [string]$State.instanceId
            -and [string](Get-FbProperty $marker "purpose" "") -eq $Purpose
    } catch {
        return $false
    }
}

function Assert-FbSafeManagedOutputPath {
    param([string]$Path, $State, [string]$Description)
    $fullPath = [IO.Path]::GetFullPath($Path)
    $pathRoot = [IO.Path]::GetPathRoot($fullPath)
    if (-not [string]::IsNullOrWhiteSpace($pathRoot) -and $fullPath.Equals($pathRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Description may not be a filesystem root. Choose a dedicated leaf directory."
    }
    $resolved = $fullPath.TrimEnd('\', '/')
    if ($resolved.Length -gt 165) {
        throw "$Description is too deep for atomic backup/support filenames on legacy Windows path handling. Choose a shorter dedicated directory."
    }
    foreach ($protected in @(
        [string]$State.stateDirectory,
        [string](Get-FbProperty $State "releaseDirectory" ""))) {
        if ([string]::IsNullOrWhiteSpace($protected)) { continue }
        if ((Test-FbPathWithin $resolved $protected) -or (Test-FbPathWithin $protected $resolved)) {
            throw "$Description must be separate from, and must not contain, the Private Server state or release directory."
        }
    }
    Assert-FbNoReparseAncestor $resolved $Description
}

function Resolve-FbManagedOutputDirectory {
    param(
        [string]$RequestedDirectory,
        $State,
        [string]$Purpose,
        [string]$DefaultLeafName
    )
    if ([string]::IsNullOrWhiteSpace($RequestedDirectory)) {
        $documents = [Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments)
        if ([string]::IsNullOrWhiteSpace($documents)) { $documents = $HOME }
        $RequestedDirectory = Join-Path $documents $DefaultLeafName
    }
    $requested = ConvertTo-FbFullPath $RequestedDirectory
    Assert-FbSafeManagedOutputPath $requested $State "$Purpose output"
    if (-not (Test-Path -LiteralPath $requested)) { return $requested }
    if (-not (Test-Path -LiteralPath $requested -PathType Container)) {
        throw "$Purpose output must be a directory: $requested"
    }
    if (Test-FbManagedOutputDirectory $requested $State $Purpose) { return $requested }

    # Never rewrite ACLs on an arbitrary existing directory. Allocate a dedicated,
    # installation-labelled child and restrict only that managed leaf.
    $leaf = "FilingBridge-$($State.instanceId.Replace('-', '').Substring(0, 12))-$Purpose"
    $managed = Join-Path $requested $leaf
    Assert-FbSafeManagedOutputPath $managed $State "$Purpose managed output"
    if ((Test-Path -LiteralPath $managed) -and -not (Test-FbManagedOutputDirectory $managed $State $Purpose)) {
        throw "$Purpose managed output already exists without this installation's marker: $managed"
    }
    return $managed
}

function Initialize-FbManagedOutputDirectory {
    param([string]$Directory, $State, [string]$Purpose)
    if (-not (Test-Path -LiteralPath $Directory -PathType Container)) {
        New-Item -ItemType Directory -Path $Directory -Force:$false | Out-Null
    }
    Set-FbRestrictedAcl -Path $Directory
    $markerPath = Get-FbManagedOutputMarkerPath $Directory
    if (-not (Test-Path -LiteralPath $markerPath -PathType Leaf)) {
        Write-FbTextExclusive -Path $markerPath -Value (([ordered]@{
            schemaVersion = "filingbridge.private-server.output/v1"
            instanceId = [string]$State.instanceId
            purpose = $Purpose
            createdAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        } | ConvertTo-Json -Depth 3) + [Environment]::NewLine)
    }
    if (-not (Test-FbManagedOutputDirectory $Directory $State $Purpose)) {
        throw "$Purpose output marker does not belong to this installation."
    }
    Set-FbRestrictedAcl -Path $markerPath
}

function Get-FbBackupOutputDirectory {
    param([string]$OutputDirectory, $State)
    return Resolve-FbManagedOutputDirectory $OutputDirectory $State "Backups" "FilingBridge Backups"
}

function Get-FbBackupFileInventory {
    param([string]$Root)
    $items = New-Object System.Collections.Generic.List[object]
    foreach ($file in @(Get-ChildItem -LiteralPath $Root -File -Recurse -Force | Sort-Object FullName)) {
        $relative = $file.FullName.Substring($Root.TrimEnd('\', '/').Length).TrimStart('\', '/').Replace('\', '/')
        $items.Add([pscustomobject][ordered]@{
            path = $relative
            byteSize = [long]$file.Length
            sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        })
    }
    return $items.ToArray()
}

function New-FbCompleteRecoveryPayload {
    param($State, [string]$DumpPath, [string]$PayloadDirectory, $DatabaseVerification)
    New-Item -ItemType Directory -Path $PayloadDirectory | Out-Null
    $databaseDirectory = Join-Path $PayloadDirectory "database"
    $stateDirectory = Join-Path $PayloadDirectory "state"
    $secretDirectory = Join-Path $stateDirectory "secrets"
    New-Item -ItemType Directory -Path $databaseDirectory, $secretDirectory | Out-Null
    Copy-Item -LiteralPath $DumpPath -Destination (Join-Path $databaseDirectory "accounts.dump")
    Copy-Item -LiteralPath (Join-Path ([string]$State.stateDirectory) $script:StateFileName) -Destination (Join-Path $stateDirectory $script:StateFileName)
    Copy-Item -LiteralPath ([string]$State.environmentFile) -Destination (Join-Path $stateDirectory $script:EnvironmentFileName)
    $installedCompose = [string](Get-FbProperty $State "installedComposeFile" "")
    if ([string]::IsNullOrWhiteSpace($installedCompose) -or -not (Test-Path -LiteralPath $installedCompose -PathType Leaf)) { throw "Installed Compose snapshot is missing; complete recovery backup refused." }
    Copy-Item -LiteralPath $installedCompose -Destination (Join-Path $stateDirectory "compose.private.installed.yml")
    foreach ($secret in @(Get-ChildItem -LiteralPath ([string]$State.secretDirectory) -File -Force)) {
        if ($secret.Name -eq "private_initial_owner_password") { continue }
        Copy-Item -LiteralPath $secret.FullName -Destination (Join-Path $secretDirectory $secret.Name)
    }
    $inventory = Get-FbBackupFileInventory $PayloadDirectory
    $internalManifest = [ordered]@{
        schemaVersion = "filingbridge.private-server.backup/v1"
        completeRecoverySet = $true
        instanceId = [string]$State.instanceId
        releaseVersion = [string]$State.releaseVersion
        releaseCommitSha = [string]$State.releaseCommitSha
        database = "accounts"
        databaseVerification = $DatabaseVerification
        recoveryCriticalSecrets = $script:RecoveryCriticalSecrets
        createdAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        files = $inventory
    }
    Write-FbJsonAtomic -Path (Join-Path $PayloadDirectory "manifest.json") -Value $internalManifest
    return $internalManifest
}

function Assert-FbBackupHash {
    param([string]$BackupPath, $ExternalManifest)
    if ([string](Get-FbProperty $ExternalManifest "backupFileName" "") -cne [IO.Path]::GetFileName($BackupPath)) {
        throw "Backup filename does not match its authenticated manifest."
    }
    $expected = [string](Get-FbProperty $ExternalManifest "backupSha256" "")
    if ($expected -cnotmatch '^[a-f0-9]{64}$') { throw "Backup manifest has no valid SHA-256 digest." }
    $actual = (Get-FileHash -LiteralPath $BackupPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $expected) { throw "Backup SHA-256 does not match its manifest." }
    if ([long](Get-FbProperty $ExternalManifest "byteSize" -1) -ne (Get-Item -LiteralPath $BackupPath -Force).Length) {
        throw "Backup byte size does not match its manifest."
    }
}

function Read-FbBackupAuthenticationKey {
    param($State)
    $path = Join-Path ([string]$State.secretDirectory) "backup_authentication_key"
    return Read-FbBackupAuthenticationKeyFile $path "The installation backup-authentication key is missing. No backup was restored."
}

function Read-FbBackupAuthenticationKeyFile {
    param([string]$Path, [string]$MissingMessage = "The recovery authentication key file is missing.")
    if ([string]::IsNullOrWhiteSpace($Path)) { throw $MissingMessage }
    $path = ConvertTo-FbFullPath $Path
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw $MissingMessage
    }
    try { $key = [Convert]::FromBase64String((Read-FbUtf8Text $path).Trim()) } catch { throw "The installation backup-authentication key is invalid." }
    if ($key.Length -lt 32) { throw "The installation backup-authentication key is too short." }
    return $key
}

function Get-FbBackupAuthenticationMessage {
    param($Manifest)
    $databaseVerification = Get-FbProperty $Manifest "databaseVerification" @{}
    $canonicalImportantTables = @(@(Get-FbProperty $databaseVerification "importantTables" @()) | ForEach-Object {
        [ordered]@{
            table = [string](Get-FbProperty $_ "table" "")
            rowCount = [long](Get-FbProperty $_ "rowCount" -1)
            fingerprint = [string](Get-FbProperty $_ "fingerprint" "")
        }
    })
    $canonicalVerification = [ordered]@{
        database = [string](Get-FbProperty $databaseVerification "database" "")
        tableCount = [int](Get-FbProperty $databaseVerification "tableCount" 0)
        migrationCount = [int](Get-FbProperty $databaseVerification "migrationCount" 0)
        importantTables = $canonicalImportantTables
        fingerprintsMatched = [bool](Get-FbProperty $databaseVerification "fingerprintsMatched" $false)
        verifiedAtUtc = ConvertTo-FbCanonicalUtcTimestampText (Get-FbProperty $databaseVerification "verifiedAtUtc" "")
    }
    $verificationJson = $canonicalVerification | ConvertTo-Json -Depth 12 -Compress
    $lines = @(
        "schemaVersion=$([string](Get-FbProperty $Manifest 'schemaVersion' ''))",
        "status=$([string](Get-FbProperty $Manifest 'status' ''))",
        "completeRecoverySet=$(([bool](Get-FbProperty $Manifest 'completeRecoverySet' $false)).ToString().ToLowerInvariant())",
        "warning=$([string](Get-FbProperty $Manifest 'warning' ''))",
        "encrypted=$(([bool](Get-FbProperty $Manifest 'encrypted' $false)).ToString().ToLowerInvariant())",
        "encryption=$([string](Get-FbProperty $Manifest 'encryption' ''))",
        "recipient=$([string](Get-FbProperty $Manifest 'recipient' ''))",
        "instanceId=$([string](Get-FbProperty $Manifest 'instanceId' ''))",
        "releaseVersion=$([string](Get-FbProperty $Manifest 'releaseVersion' ''))",
        "releaseCommitSha=$([string](Get-FbProperty $Manifest 'releaseCommitSha' ''))",
        "backupFileName=$([string](Get-FbProperty $Manifest 'backupFileName' ''))",
        "backupSha256=$([string](Get-FbProperty $Manifest 'backupSha256' ''))",
        "byteSize=$([Convert]::ToString([long](Get-FbProperty $Manifest 'byteSize' -1), [Globalization.CultureInfo]::InvariantCulture))",
        "databaseVerificationSha256=$(Get-FbSha256Text $verificationJson)",
        "createdAtUtc=$(ConvertTo-FbCanonicalUtcTimestampText (Get-FbProperty $Manifest 'createdAtUtc' ''))"
    )
    return ($lines -join "`n")
}

function Add-FbBackupAuthentication {
    param($State, [Collections.IDictionary]$Manifest)
    $key = Read-FbBackupAuthenticationKey $State
    try {
        $keyId = (Get-FbSha256Text ([Convert]::ToBase64String($key))).Substring(0, 16)
        $Manifest["authentication"] = [ordered]@{
            algorithm = "HMAC-SHA256"
            keyId = $keyId
            value = Get-FbHmacSha256 $key (Get-FbBackupAuthenticationMessage $Manifest)
        }
    } finally { [Array]::Clear($key, 0, $key.Length) }
}

function Assert-FbBackupAuthentication {
    param($State, $Manifest)
    $key = Read-FbBackupAuthenticationKey $State
    try { Assert-FbBackupAuthenticationWithKey $key $Manifest }
    finally { [Array]::Clear($key, 0, $key.Length) }
}

function Assert-FbBackupAuthenticationWithKey {
    param([byte[]]$Key, $Manifest)
    $authentication = Get-FbProperty $Manifest "authentication"
    if ([string](Get-FbProperty $authentication "algorithm" "") -cne "HMAC-SHA256") {
        throw "Backup has no supported installation authentication. No database restore was attempted."
    }
    $supplied = [string](Get-FbProperty $authentication "value" "")
    try {
        $expectedKeyId = (Get-FbSha256Text ([Convert]::ToBase64String($Key))).Substring(0, 16)
        if ([string](Get-FbProperty $authentication "keyId" "") -cne $expectedKeyId) {
            throw "Backup was not authenticated by the supplied recovery trust anchor."
        }
        $expected = Get-FbHmacSha256 $Key (Get-FbBackupAuthenticationMessage $Manifest)
        if (-not (Test-FbFixedTimeHexEqual $expected $supplied)) {
            throw "Backup authentication failed. The recovery set was modified or did not originate from this installation; no database restore was attempted."
        }
    } finally { }
}

function Invoke-FbBackup {
    param(
        $State,
        [string]$ComposeFile,
        [string]$OutputDirectory,
        [string]$BackupRecipient,
        [switch]$PlaintextDatabaseOnly,
        [switch]$DryRun
    )
    $resolvedOutput = Get-FbBackupOutputDirectory $OutputDirectory $State
    if (-not $PlaintextDatabaseOnly) {
        if ([string]::IsNullOrWhiteSpace($BackupRecipient)) { $BackupRecipient = [string](Get-FbProperty $State "backupRecipient" "") }
        if ([string]::IsNullOrWhiteSpace($BackupRecipient)) {
            throw "A complete recovery set requires -BackupRecipient (an age recipient). Use -PlaintextDatabaseOnly only for an explicit local database-only dump."
        }
        if ($null -eq (Get-Command age -ErrorAction SilentlyContinue) -and $null -eq $script:PrivateServerCommandInvoker) {
            throw "The vetted 'age' encryption tool is required for a complete portable recovery set. No plaintext key archive will be created."
        }
    }
    if ($DryRun) {
        Write-Host "DRY RUN: quiesce application writers and preserve the previous service state"
        Write-Host "DRY RUN: create a PostgreSQL custom dump and restore it into a disposable verification database"
        if ($PlaintextDatabaseOnly) { Write-Host "DRY RUN: publish an explicitly incomplete plaintext database-only backup" }
        else { Write-Host "DRY RUN: encrypt the database and key/config companion with age before atomic publication" }
        return ""
    }

    Initialize-FbManagedOutputDirectory $resolvedOutput $State "Backups"
    $timestamp = (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss")
    $staging = Join-Path ([IO.Path]::GetTempPath()) ("filingbridge-backup-" + [Guid]::NewGuid().ToString("N"))
    $previouslyRunning = @(Get-FbRunningServices $State $ComposeFile)
    $startedDatabase = $false
    try {
        New-Item -ItemType Directory -Path $staging | Out-Null
        Set-FbRestrictedAcl -Path $staging
        if ($previouslyRunning -notcontains "db") {
            Start-FbDatabaseForOperation $State $ComposeFile "Start PostgreSQL temporarily for backup"
            $startedDatabase = $true
        } else {
            Wait-FbServiceHealth $State $ComposeFile @("db")
        }
        $writers = @($previouslyRunning | Where-Object { $_ -in @("frontend", "api") })
        if ($writers.Count -gt 0) {
            $null = Invoke-FbCompose $State $ComposeFile (@("stop", "--timeout", "60") + $writers) "Quiesce application writers for a consistent backup" -Mutating
        }
        Assert-FbWritersQuiesced $State $ComposeFile "Backup"
        $sourceImportantTables = @(Get-FbImportantTableEvidence $State $ComposeFile "accounts" "Read important-table fingerprints from the active database")
        $dumpPath = Join-Path $staging "accounts.dump"
        New-FbDatabaseDump $State $ComposeFile $dumpPath
        $verification = Test-FbDatabaseDumpRestore -State $State -ComposeFile $ComposeFile -HostDumpPath $dumpPath -ExpectedImportantTables $sourceImportantTables
        $instanceShort = ([string]$State.instanceId).Replace("-", "").Substring(0, 12)
        $leafBase = "filingbridge-$instanceShort-$timestamp-$([Guid]::NewGuid().ToString('N').Substring(0, 8))"
        if ($PlaintextDatabaseOnly) {
            $partial = Join-Path $resolvedOutput (".$leafBase.dump.partial")
            $final = Join-Path $resolvedOutput "$leafBase.dump"
            Copy-Item -LiteralPath $dumpPath -Destination $partial
            Move-Item -LiteralPath $partial -Destination $final
            $manifest = [ordered]@{
                schemaVersion = "filingbridge.private-server.backup-envelope/v1"
                status = "verified"
                completeRecoverySet = $false
                warning = "Database-only plaintext backup; not sufficient for host loss because recovery-critical keys are excluded."
                encrypted = $false
                instanceId = [string]$State.instanceId
                releaseVersion = [string]$State.releaseVersion
                releaseCommitSha = [string]$State.releaseCommitSha
                backupFileName = [IO.Path]::GetFileName($final)
                backupSha256 = (Get-FileHash -LiteralPath $final -Algorithm SHA256).Hash.ToLowerInvariant()
                byteSize = (Get-Item -LiteralPath $final -Force).Length
                databaseVerification = $verification
                createdAtUtc = (Get-Date).ToUniversalTime().ToString("o")
            }
            Add-FbBackupAuthentication $State $manifest
            Write-FbJsonAtomic -Path "$final.manifest.json" -Value $manifest
            Write-FbTextAtomic -Path "$final.sha256" -Value ($manifest.backupSha256 + "  " + [IO.Path]::GetFileName($final) + [Environment]::NewLine)
            Write-Warning $manifest.warning
            Write-Host "Verified database-only backup: $final"
            return $final
        }

        $payload = Join-Path $staging "payload"
        $null = New-FbCompleteRecoveryPayload $State $dumpPath $payload $verification
        $archive = Join-Path $staging "$leafBase.zip"
        $payloadBytes = [long](@(Get-ChildItem -LiteralPath $payload -File -Recurse -Force | Measure-Object -Property Length -Sum).Sum)
        if ($payloadBytes -gt $script:MaximumCompleteBackupArchiveBytes) {
            throw "The complete recovery payload is larger than the supported 1.9 GB Compress-Archive safety limit. Create and retain the authenticated database-only backup, then contact support for a streamed complete-backup workflow."
        }
        Compress-Archive -Path (Join-Path $payload "*") -DestinationPath $archive -CompressionLevel Optimal
        $partial = Join-Path $resolvedOutput (".$leafBase.fbbackup.age.partial")
        $final = Join-Path $resolvedOutput "$leafBase.fbbackup.age"
        $null = Invoke-FbNative -FilePath "age" -Arguments @("--recipient", $BackupRecipient, "--output", $partial, $archive) -Description "Encrypt the complete recovery set with age" -Mutating
        if (-not (Test-Path -LiteralPath $partial -PathType Leaf) -or (Get-Item -LiteralPath $partial -Force).Length -le 0) { throw "Encrypted recovery set is missing or empty." }
        Move-Item -LiteralPath $partial -Destination $final
        $manifest = [ordered]@{
            schemaVersion = "filingbridge.private-server.backup-envelope/v1"
            status = "verified-before-encryption"
            completeRecoverySet = $true
            encrypted = $true
            encryption = "age"
            recipient = $BackupRecipient
            instanceId = [string]$State.instanceId
            releaseVersion = [string]$State.releaseVersion
            releaseCommitSha = [string]$State.releaseCommitSha
            backupFileName = [IO.Path]::GetFileName($final)
            backupSha256 = (Get-FileHash -LiteralPath $final -Algorithm SHA256).Hash.ToLowerInvariant()
            byteSize = (Get-Item -LiteralPath $final -Force).Length
            databaseVerification = $verification
            createdAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        }
        Add-FbBackupAuthentication $State $manifest
        Write-FbJsonAtomic -Path "$final.manifest.json" -Value $manifest
        Write-FbTextAtomic -Path "$final.sha256" -Value ($manifest.backupSha256 + "  " + [IO.Path]::GetFileName($final) + [Environment]::NewLine)
        $State.backupRecipient = $BackupRecipient
        Save-FbState $State
        Write-Host "Encrypted, verified complete recovery set: $final"
        return $final
    } finally {
        try {
            Restore-FbRunningServices $State $ComposeFile $previouslyRunning
            if ($startedDatabase -and $previouslyRunning -notcontains "db") {
                $null = Invoke-FbCompose $State $ComposeFile @("stop", "db") "Restore PostgreSQL to its previous stopped state" -Mutating
            }
        } finally {
            Remove-FbTemporaryDirectory $staging
        }
    }
}

function Expand-FbEncryptedBackup {
    param([string]$BackupPath, [string]$AgeIdentityFile, [string]$Destination)
    if ([string]::IsNullOrWhiteSpace($AgeIdentityFile) -or -not (Test-Path -LiteralPath $AgeIdentityFile -PathType Leaf)) {
        throw "-AgeIdentityFile is required to decrypt and fully verify this recovery set."
    }
    if ($null -eq (Get-Command age -ErrorAction SilentlyContinue) -and $null -eq $script:PrivateServerCommandInvoker) { throw "The age executable is required." }
    New-Item -ItemType Directory -Path $Destination | Out-Null
    Set-FbRestrictedAcl -Path $Destination
    $archive = Join-Path $Destination "payload.zip"
    $null = Invoke-FbNative -FilePath "age" -Arguments @("--decrypt", "--identity", $AgeIdentityFile, "--output", $archive, $BackupPath) -Description "Decrypt the complete recovery set" -Mutating
    Assert-FbSafeZipArchive $archive
    $expanded = Join-Path $Destination "expanded"
    Expand-Archive -LiteralPath $archive -DestinationPath $expanded
    Remove-Item -LiteralPath $archive -Force
    return $expanded
}

function Get-FbRequiredRecoveryPaths {
    return @(
        "database/accounts.dump",
        "state/server.json",
        "state/private.env",
        "state/compose.private.installed.yml",
        "state/secrets/postgres_password",
        "state/secrets/postgres_application_password",
        "state/secrets/accounts_migration_connection_string",
        "state/secrets/accounts_application_connection_string",
        "state/secrets/auth_session_signing_key",
        "state/secrets/audit_integrity_signing_key",
        "state/secrets/database_tenant_context_key",
        "state/secrets/identity_hmac_key",
        "state/secrets/mfa_encryption_key",
        "state/secrets/backup_authentication_key",
        "state/secrets/accounts_api_key_hash",
        "state/secrets/accounts_api_key"
    )
}

function Assert-FbSafeRelativeBackupPath {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path) -or $Path.Length -gt 240 -or $Path -match '[\x00\r\n:]' -or $Path.StartsWith('/') -or $Path.StartsWith('\')) {
        throw "Recovery set contains an unsafe path."
    }
    $normalized = $Path.Replace('\', '/')
    if ([IO.Path]::IsPathRooted($normalized) -or @($normalized.Split('/') | Where-Object { $_ -in @("", ".", "..") }).Count -gt 0) {
        throw "Recovery set contains an unsafe relative path: $Path"
    }
    return $normalized
}

function Assert-FbSafeZipArchive {
    param([string]$ArchivePath)
    if (-not (Test-Path -LiteralPath $ArchivePath -PathType Leaf)) { throw "Decrypted recovery archive is missing." }
    $archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        if ($archive.Entries.Count -gt 256) { throw "Recovery archive has too many entries." }
        $seen = @{}
        $files = @{}
        [long]$totalLength = 0
        foreach ($entry in $archive.Entries) {
            $isDirectory = $entry.FullName.EndsWith('/')
            $entryName = if ($isDirectory) { $entry.FullName.Substring(0, $entry.FullName.Length - 1) } else { $entry.FullName }
            $path = Assert-FbSafeRelativeBackupPath $entryName
            $key = $path.ToLowerInvariant()
            if ($seen.ContainsKey($key)) { throw "Recovery archive contains a duplicate path: $path" }
            $seen[$key] = $true
            $unixType = (([int64]$entry.ExternalAttributes -shr 16) -band 0xF000)
            if ($isDirectory) {
                if ($unixType -notin @(0, 0x4000)) { throw "Recovery archive contains a non-regular directory entry: $path" }
                continue
            }
            if ($unixType -notin @(0, 0x8000)) { throw "Recovery archive contains a non-regular file entry: $path" }
            $files[$key] = $true
            if ($entry.Length -gt 5GB) { throw "Recovery archive entry is unreasonably large: $path" }
            $totalLength += [long]$entry.Length
            if ($totalLength -gt 8GB) { throw "Recovery archive expands beyond the supported 8 GB envelope limit." }
            if ($entry.Length -gt 100MB -and $entry.CompressedLength -gt 0 -and ($entry.Length / $entry.CompressedLength) -gt 1000) {
                throw "Recovery archive entry has an unsafe compression ratio: $path"
            }
        }
        foreach ($required in @("manifest.json") + @(Get-FbRequiredRecoveryPaths)) {
            if (-not $files.ContainsKey($required.ToLowerInvariant())) { throw "Recovery archive is missing required entry: $required" }
        }
    } finally { $archive.Dispose() }
}

function Assert-FbInternalBackupInventory {
    param([string]$ExpandedPath)
    $manifestPath = Join-Path $ExpandedPath "manifest.json"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Recovery set has no internal manifest." }
    $manifest = (Read-FbUtf8Text $manifestPath) | ConvertFrom-Json
    if ([string](Get-FbProperty $manifest "schemaVersion" "") -ne "filingbridge.private-server.backup/v1") { throw "Recovery set format is unsupported." }
    if ((Get-FbProperty $manifest "completeRecoverySet" $false) -ne $true) { throw "Internal recovery manifest is not a complete recovery set." }
    $manifestPaths = @{}
    foreach ($file in @(Get-FbProperty $manifest "files" @())) {
        $relative = Assert-FbSafeRelativeBackupPath ([string](Get-FbProperty $file "path" ""))
        $relativeKey = $relative.ToLowerInvariant()
        if ($manifestPaths.ContainsKey($relativeKey)) { throw "Recovery-set manifest contains a duplicate path: $relative" }
        $manifestPaths[$relativeKey] = $true
        $path = [IO.Path]::GetFullPath((Join-Path $ExpandedPath $relative))
        if (-not (Test-FbPathWithin $path $ExpandedPath)) { throw "Recovery-set inventory path escapes its envelope." }
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Recovery-set file is missing: $relative" }
        if ((Get-Item -LiteralPath $path -Force).Length -ne [long](Get-FbProperty $file "byteSize" -1)) { throw "Recovery-set byte size mismatch: $relative" }
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($hash -ne [string](Get-FbProperty $file "sha256" "")) { throw "Recovery-set SHA-256 mismatch: $relative" }
    }
    foreach ($required in @(Get-FbRequiredRecoveryPaths)) {
        if (-not $manifestPaths.ContainsKey($required.ToLowerInvariant())) { throw "Recovery-set manifest omits required file: $required" }
    }
    $actualPaths = @{}
    foreach ($actual in @(Get-ChildItem -LiteralPath $ExpandedPath -File -Recurse -Force)) {
        $relative = $actual.FullName.Substring($ExpandedPath.TrimEnd('\', '/').Length).TrimStart('\', '/').Replace('\', '/')
        if ($relative -eq "manifest.json") { continue }
        $key = $relative.ToLowerInvariant()
        if ($actualPaths.ContainsKey($key)) { throw "Expanded recovery set contains a duplicate case-insensitive path: $relative" }
        $actualPaths[$key] = $true
        if (-not $manifestPaths.ContainsKey($key)) { throw "Recovery set contains an unmanifested file: $relative" }
    }
    foreach ($pathKey in $manifestPaths.Keys) {
        if (-not $actualPaths.ContainsKey($pathKey)) { throw "Recovery manifest references a file that was not expanded: $pathKey" }
    }
    return $manifest
}

function Test-FbBackupReleaseCompatibility {
    param([string]$BackupVersion, [string]$CurrentVersion)
    if ($BackupVersion -ceq $CurrentVersion) { return $true }
    if ($BackupVersion.Contains('-') -or $CurrentVersion.Contains('-')) { return $false }
    $backupMatch = [regex]::Match($BackupVersion, '^v?(?<version>\d+\.\d+\.\d+)(?:[-+].*)?$')
    $currentMatch = [regex]::Match($CurrentVersion, '^v?(?<version>\d+\.\d+\.\d+)(?:[-+].*)?$')
    if (-not $backupMatch.Success -or -not $currentMatch.Success) { return $false }
    $backupSemantic = [Version]::Parse($backupMatch.Groups['version'].Value)
    $currentSemantic = [Version]::Parse($currentMatch.Groups['version'].Value)
    return $backupSemantic -le $currentSemantic
}

function ConvertTo-FbSemanticVersionParts {
    param([string]$Version)
    $match = [regex]::Match($Version, '^(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)\.(?<patch>0|[1-9][0-9]*)(?:-(?<pre>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$')
    if (-not $match.Success) { throw "Release version '$Version' is not a supported semantic version." }
    $pre = @()
    if ($match.Groups['pre'].Success) {
        $pre = @($match.Groups['pre'].Value.Split('.'))
        foreach ($identifier in $pre) {
            if ($identifier -match '^0[0-9]+$') { throw "Release version '$Version' has a non-canonical numeric prerelease identifier." }
        }
    }
    return [pscustomobject]@{
        major = [uint64]$match.Groups['major'].Value
        minor = [uint64]$match.Groups['minor'].Value
        patch = [uint64]$match.Groups['patch'].Value
        prerelease = $pre
    }
}

function Compare-FbSemanticVersion {
    param([string]$Left, [string]$Right)
    $leftParts = ConvertTo-FbSemanticVersionParts $Left
    $rightParts = ConvertTo-FbSemanticVersionParts $Right
    foreach ($name in @('major', 'minor', 'patch')) {
        if ($leftParts.$name -lt $rightParts.$name) { return -1 }
        if ($leftParts.$name -gt $rightParts.$name) { return 1 }
    }
    $leftPre = @($leftParts.prerelease)
    $rightPre = @($rightParts.prerelease)
    if ($leftPre.Count -eq 0 -and $rightPre.Count -eq 0) { return 0 }
    if ($leftPre.Count -eq 0) { return 1 }
    if ($rightPre.Count -eq 0) { return -1 }
    $length = [Math]::Max($leftPre.Count, $rightPre.Count)
    for ($index = 0; $index -lt $length; $index++) {
        if ($index -ge $leftPre.Count) { return -1 }
        if ($index -ge $rightPre.Count) { return 1 }
        $leftNumeric = $leftPre[$index] -match '^[0-9]+$'
        $rightNumeric = $rightPre[$index] -match '^[0-9]+$'
        if ($leftNumeric -and $rightNumeric) {
            $leftNumber = [uint64]$leftPre[$index]
            $rightNumber = [uint64]$rightPre[$index]
            if ($leftNumber -lt $rightNumber) { return -1 }
            if ($leftNumber -gt $rightNumber) { return 1 }
            continue
        }
        if ($leftNumeric -and -not $rightNumeric) { return -1 }
        if (-not $leftNumeric -and $rightNumeric) { return 1 }
        $comparison = [string]::CompareOrdinal($leftPre[$index], $rightPre[$index])
        if ($comparison -lt 0) { return -1 }
        if ($comparison -gt 0) { return 1 }
    }
    return 0
}

function Assert-FbForwardUpdate {
    param($State, $NewRelease, [switch]$BuildLocal, [switch]$DryRun)
    $currentVersion = [string]$State.releaseVersion
    $targetVersion = [string]$NewRelease.version
    if ($currentVersion -eq 'source-build') {
        if ($targetVersion -eq 'source-build') {
            if ($DryRun) { return }
            return # explicitly unreviewed source builds have no comparable release sequence
        }
        $null = ConvertTo-FbSemanticVersionParts $targetVersion
        return
    }
    $null = ConvertTo-FbSemanticVersionParts $currentVersion
    if ($BuildLocal -or $targetVersion -eq 'source-build') {
        throw "Update refuses to replace a semantic compiled release with an unreviewed source build. Install a strictly newer reviewed release."
    }
    $comparison = Compare-FbSemanticVersion $targetVersion $currentVersion
    if ($comparison -lt 0) {
        throw "Update is forward-only: target release '$targetVersion' is older than installed release '$currentVersion'. Use no implicit downgrade path."
    }
    if ($comparison -eq 0) {
        $sameCommit = [string]$NewRelease.commitSha -ceq [string]$State.releaseCommitSha
        $sameImages = [string]$NewRelease.images.api -ceq [string]$State.images.api -and
            [string]$NewRelease.images.frontend -ceq [string]$State.images.frontend -and
            [string]$NewRelease.images.postgres -ceq [string]$State.images.postgres
        if (-not $sameCommit -or -not $sameImages) {
            throw "Release version '$targetVersion' is already installed with different immutable identity. Reusing a version for another commit or image is refused."
        }
        throw "Release version '$targetVersion' with the same commit and images is already installed; no update was performed."
    }
}

function Invoke-FbVerifyBackup {
    param($State, [string]$ComposeFile, [string]$BackupPath, [string]$AgeIdentityFile, [switch]$AllowPlaintextDatabaseOnlyRestore)
    if ([string]::IsNullOrWhiteSpace($BackupPath)) { throw "-BackupPath is required." }
    $resolved = ConvertTo-FbFullPath $BackupPath
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) { throw "Backup was not found: $resolved" }
    $externalPath = "$resolved.manifest.json"
    if (-not (Test-Path -LiteralPath $externalPath -PathType Leaf)) { throw "Backup envelope manifest was not found: $externalPath" }
    $external = (Read-FbUtf8Text $externalPath) | ConvertFrom-Json
    if ([string](Get-FbProperty $external "schemaVersion" "") -ne "filingbridge.private-server.backup-envelope/v1") { throw "Backup envelope format is unsupported." }
    Assert-FbBackupAuthentication $State $external
    Assert-FbBackupHash $resolved $external
    if ([string](Get-FbProperty $external "instanceId" "") -ne [string]$State.instanceId) { throw "Backup belongs to a different Private Server installation." }
    $externalReleaseVersion = [string](Get-FbProperty $external "releaseVersion" "")
    if (-not (Test-FbBackupReleaseCompatibility $externalReleaseVersion ([string]$State.releaseVersion))) {
        throw "Backup release '$externalReleaseVersion' is not compatible with current release '$($State.releaseVersion)'. Restore with the same or a newer semantic release."
    }
    $externalReleaseCommit = [string](Get-FbProperty $external "releaseCommitSha" "")
    if ($externalReleaseVersion -ceq [string]$State.releaseVersion -and
        -not [string]::IsNullOrWhiteSpace($externalReleaseCommit) -and
        $externalReleaseCommit -cne [string]$State.releaseCommitSha) {
        throw "Backup claims the current release version but a different release commit."
    }
    $externalDatabaseVerification = Get-FbProperty $external "databaseVerification"
    $externalImportantTables = @(Get-FbProperty $externalDatabaseVerification "importantTables" @())
    if ($externalImportantTables.Count -ne 5 -or (Get-FbProperty $externalDatabaseVerification "fingerprintsMatched" $false) -ne $true) {
        throw "Backup envelope lacks matched important-table row-count and fingerprint evidence."
    }
    if ((Get-FbProperty $external "completeRecoverySet" $false) -eq $false) {
        if (-not $AllowPlaintextDatabaseOnlyRestore) { throw "This is an incomplete plaintext database-only backup. Repeat with -AllowPlaintextDatabaseOnlyRestore to acknowledge that limitation." }
        $plaintextStaging = Join-Path ([IO.Path]::GetTempPath()) ("filingbridge-verify-plaintext-" + [Guid]::NewGuid().ToString("N"))
        try {
            New-Item -ItemType Directory -Path $plaintextStaging | Out-Null
            Set-FbRestrictedAcl $plaintextStaging
            $trustedDump = Join-Path $plaintextStaging "authenticated-accounts.dump"
            Copy-Item -LiteralPath $resolved -Destination $trustedDump
            Set-FbRestrictedAcl $trustedDump
            $trustedHash = (Get-FileHash -LiteralPath $trustedDump -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($trustedHash -cne [string](Get-FbProperty $external "backupSha256" "") -or
                (Get-Item -LiteralPath $trustedDump -Force).Length -ne [long](Get-FbProperty $external "byteSize" -1)) {
                throw "Backup changed while it was copied into authenticated private staging; no database restore was attempted."
            }
            $verification = Test-FbDatabaseDumpRestore -State $State -ComposeFile $ComposeFile -HostDumpPath $trustedDump -ExpectedImportantTables $externalImportantTables
            Write-Warning "Verified database contents, but this backup omits recovery-critical keys and is not sufficient for host loss."
            return [pscustomobject]@{ backupPath = $resolved; completeRecoverySet = $false; dumpPath = $trustedDump; expandedPath = ""; internalManifest = $null; databaseVerification = $verification; stagingPath = $plaintextStaging }
        } catch {
            Remove-FbTemporaryDirectory $plaintextStaging
            throw
        }
    }
    $staging = Join-Path ([IO.Path]::GetTempPath()) ("filingbridge-verify-backup-" + [Guid]::NewGuid().ToString("N"))
    try {
        New-Item -ItemType Directory -Path $staging | Out-Null
        Set-FbRestrictedAcl $staging
        $trustedEncrypted = Join-Path $staging "authenticated-recovery-set.fbbackup.age"
        Copy-Item -LiteralPath $resolved -Destination $trustedEncrypted
        Set-FbRestrictedAcl $trustedEncrypted
        $trustedHash = (Get-FileHash -LiteralPath $trustedEncrypted -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($trustedHash -cne [string](Get-FbProperty $external "backupSha256" "") -or
            (Get-Item -LiteralPath $trustedEncrypted -Force).Length -ne [long](Get-FbProperty $external "byteSize" -1)) {
            throw "Encrypted backup changed while it was copied into authenticated private staging; no archive or database restore was attempted."
        }
        $expanded = Expand-FbEncryptedBackup $trustedEncrypted $AgeIdentityFile (Join-Path $staging "decrypted")
        $internal = Assert-FbInternalBackupInventory $expanded
        if ([string]$internal.instanceId -ne [string]$State.instanceId) { throw "Backup belongs to a different Private Server installation." }
        if ([string]$internal.instanceId -ne [string]$external.instanceId -or [string]$internal.releaseVersion -ne $externalReleaseVersion) {
            throw "Backup envelope and internal recovery identity do not agree."
        }
        if ([string](Get-FbProperty $internal "releaseCommitSha" "") -ne $externalReleaseCommit) { throw "Backup envelope and internal release commit do not agree." }
        $internalVerification = Get-FbProperty $internal "databaseVerification"
        $internalImportantTables = @(Get-FbProperty $internalVerification "importantTables" @())
        Assert-FbImportantTableEvidenceMatches -Expected $externalImportantTables -Actual $internalImportantTables -Description "Backup envelope and encrypted recovery-set evidence"
        $dump = Join-Path $expanded "database\accounts.dump"
        $verification = Test-FbDatabaseDumpRestore -State $State -ComposeFile $ComposeFile -HostDumpPath $dump -ExpectedImportantTables $internalImportantTables
        Write-Host "Complete recovery set verified, including a disposable PostgreSQL restore."
        return [pscustomobject]@{ backupPath = $resolved; completeRecoverySet = $true; dumpPath = $dump; expandedPath = $expanded; internalManifest = $internal; databaseVerification = $verification; stagingPath = $staging }
    } catch {
        Remove-FbTemporaryDirectory $staging
        throw
    }
}

function Confirm-FbTypedAction {
    param([string]$Expected, [string]$Confirmation, [string]$Prompt, [switch]$NonInteractive)
    if ([string]::IsNullOrWhiteSpace($Confirmation)) {
        if ($NonInteractive) { throw "-Confirmation '$Expected' is required in non-interactive mode." }
        $Confirmation = Read-Host "$Prompt Type exactly: $Expected"
    }
    if ($Confirmation -cne $Expected) { throw "Confirmation did not match exactly. No destructive action was taken." }
}

function Restore-FbCandidateDatabase {
    param($State, [string]$ComposeFile, [string]$DumpPath, [string]$CandidateDatabase)
    $mount = Get-FbHostBackupMount $DumpPath
    $restoreScript = 'export PGPASSWORD="$(cat /run/secrets/postgres_password)"; dropdb --host db --username "$POSTGRES_USER" --if-exists --force "$1"; createdb --host db --username "$POSTGRES_USER" "$1"; exec pg_restore --host db --username "$POSTGRES_USER" --dbname "$1" --single-transaction --exit-on-error --no-owner --no-acl "$2"'
    $null = Invoke-FbCompose $State $ComposeFile @(
        "run", "--rm", "--no-deps", "--entrypoint", "/bin/sh", "--volume", $mount.volume,
        "role-provision", "-ec", $restoreScript, "filingbridge-restore", $CandidateDatabase, $mount.containerPath
    ) "Restore the authenticated host-mounted backup into an isolated candidate database" -Mutating
}

function Switch-FbDatabase {
    param($State, [string]$ComposeFile, [string]$CandidateDatabase, [string]$PreservedDatabase)
    $terminateScript = 'export PGPASSWORD="$(cat /run/secrets/postgres_password)"; psql --username "$POSTGRES_USER" --dbname postgres --set=ON_ERROR_STOP=1 --command "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname IN (''accounts'', ''$1'', ''$2'') AND pid <> pg_backend_pid()"'
    $null = Invoke-FbCompose $State $ComposeFile @("exec", "-T", "db", "sh", "-ec", $terminateScript, "filingbridge-switch", $CandidateDatabase, $PreservedDatabase) "Terminate database sessions before the controlled restore switch" -Mutating
    $renamedCurrent = $false
    try {
        $renameCurrent = 'export PGPASSWORD="$(cat /run/secrets/postgres_password)"; psql --username "$POSTGRES_USER" --dbname postgres --set=ON_ERROR_STOP=1 --command "ALTER DATABASE accounts RENAME TO $1"'
        $null = Invoke-FbCompose $State $ComposeFile @("exec", "-T", "db", "sh", "-ec", $renameCurrent, "filingbridge-switch", $PreservedDatabase) "Preserve the current database under a recovery name" -Mutating
        $renamedCurrent = $true
        $selectCandidate = 'export PGPASSWORD="$(cat /run/secrets/postgres_password)"; psql --username "$POSTGRES_USER" --dbname postgres --set=ON_ERROR_STOP=1 --command "ALTER DATABASE $1 RENAME TO accounts"'
        $null = Invoke-FbCompose $State $ComposeFile @("exec", "-T", "db", "sh", "-ec", $selectCandidate, "filingbridge-switch", $CandidateDatabase) "Select the verified restored database" -Mutating
    } catch {
        $failure = $_
        if ($renamedCurrent) {
            $selfRollback = 'export PGPASSWORD="$(cat /run/secrets/postgres_password)"; psql --username "$POSTGRES_USER" --dbname postgres --set=ON_ERROR_STOP=1 --command "ALTER DATABASE $1 RENAME TO accounts"'
            $rollback = Invoke-FbCompose $State $ComposeFile @("exec", "-T", "db", "sh", "-ec", $selfRollback, "filingbridge-switch-rollback", $PreservedDatabase) "Undo a partially completed database-name switch" -Mutating -IgnoreExitCode
            if ($rollback.ExitCode -ne 0) {
                $recoveryException = New-Object InvalidOperationException("Database selection failed after the current database was renamed, and self-rollback also failed. Application writers must remain stopped for manual recovery. $($failure.Exception.Message)")
                $recoveryException.Data["FilingBridgeDatabaseRecoveryRequired"] = $true
                throw $recoveryException
            }
        }
        throw "Database selection failed; any completed current-database rename was rolled back. $($failure.Exception.Message)"
    }
}

function Undo-FbDatabaseSwitch {
    param($State, [string]$ComposeFile, [string]$FailedRestoredDatabase, [string]$PreservedDatabase)
    $rollbackScript = 'export PGPASSWORD="$(cat /run/secrets/postgres_password)"; psql --username "$POSTGRES_USER" --dbname postgres --set=ON_ERROR_STOP=1 --command "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname IN (''accounts'', ''$1'', ''$2'') AND pid <> pg_backend_pid()"; psql --username "$POSTGRES_USER" --dbname postgres --set=ON_ERROR_STOP=1 --command "ALTER DATABASE accounts RENAME TO $1"; psql --username "$POSTGRES_USER" --dbname postgres --set=ON_ERROR_STOP=1 --command "ALTER DATABASE $2 RENAME TO accounts"'
    $null = Invoke-FbCompose $State $ComposeFile @("exec", "-T", "db", "sh", "-ec", $rollbackScript, "filingbridge-rollback", $FailedRestoredDatabase, $PreservedDatabase) "Return to the preserved pre-restore database" -Mutating
}

function New-FbMergedSecretDirectory {
    param($State, [string]$ExpandedPath)
    $temporary = Join-Path ([string]$State.stateDirectory) ("secrets.restore-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $temporary | Out-Null
    Set-FbRestrictedAcl $temporary
    foreach ($current in @(Get-ChildItem -LiteralPath ([string]$State.secretDirectory) -File -Force)) {
        Copy-Item -LiteralPath $current.FullName -Destination (Join-Path $temporary $current.Name)
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpandedPath)) {
        $recoveredSecretDirectory = Join-Path $ExpandedPath "state\secrets"
        foreach ($name in $script:RecoveryCriticalSecrets) {
            $source = Join-Path $recoveredSecretDirectory $name
            if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Complete recovery set is missing recovery-critical secret '$name'." }
            Copy-Item -LiteralPath $source -Destination (Join-Path $temporary $name) -Force
        }
    }
    # Every database restore invalidates every previously issued browser session,
    # including sessions captured at the backup point. Other recovery-critical keys
    # retain audit/MFA/identity continuity; only the session-signing key is rotated.
    [IO.File]::WriteAllText((Join-Path $temporary "auth_session_signing_key"), (New-PrivateServerRandomSecret), $script:Utf8NoBom)
    foreach ($file in @(Get-ChildItem -LiteralPath $temporary -File -Force)) { Set-FbRestrictedAcl $file.FullName }
    return $temporary
}

function Switch-FbSecretDirectory {
    param($State, [string]$MergedDirectory, [string]$PreservedDirectory)
    Move-Item -LiteralPath ([string]$State.secretDirectory) -Destination $PreservedDirectory
    try {
        Move-Item -LiteralPath $MergedDirectory -Destination ([string]$State.secretDirectory)
    } catch {
        Move-Item -LiteralPath $PreservedDirectory -Destination ([string]$State.secretDirectory)
        throw
    }
}

function Undo-FbSecretSwitch {
    param($State, [string]$PreservedDirectory, [string]$FailedDirectory)
    if (-not (Test-Path -LiteralPath $PreservedDirectory -PathType Container)) { return }
    if (Test-Path -LiteralPath ([string]$State.secretDirectory) -PathType Container) {
        Move-Item -LiteralPath ([string]$State.secretDirectory) -Destination $FailedDirectory
    }
    Move-Item -LiteralPath $PreservedDirectory -Destination ([string]$State.secretDirectory)
}

function Invoke-FbRestore {
    param(
        $State,
        [string]$ComposeFile,
        [string]$BackupPath,
        [string]$AgeIdentityFile,
        [string]$Confirmation,
        [switch]$NonInteractive,
        [switch]$AllowPlaintextDatabaseOnlyRestore,
        [switch]$DryRun
    )
    if ([string]::IsNullOrWhiteSpace($BackupPath)) { throw "-BackupPath is required." }
    $expected = "RESTORE $($State.instanceId)"
    Confirm-FbTypedAction $expected $Confirmation "Restore replaces the active database but preserves it under a recovery name." -NonInteractive:$NonInteractive
    if ($DryRun) {
        Write-Host "DRY RUN: fully verify the recovery set in a disposable database"
        Write-Host "DRY RUN: stop writers, restore a candidate, preserve the current database, switch, migrate, and health-check"
        Write-Host "DRY RUN: any failed post-switch check would restore the preserved database and keys"
        return
    }
    $verified = $null
    $previouslyRunning = @()
    $startedDatabase = $false
    $candidate = "fb_restore_" + [Guid]::NewGuid().ToString("N").Substring(0, 16)
    $stamp = (Get-Date).ToUniversalTime().ToString("yyyyMMddHHmmss")
    $restoreSuffix = [Guid]::NewGuid().ToString("N").Substring(0, 8)
    $preservedDatabase = "accounts_before_${stamp}_$restoreSuffix"
    $failedDatabase = "accounts_failed_${stamp}_$restoreSuffix"
    $preservedSecrets = Join-Path ([string]$State.stateDirectory) "secrets.before-$stamp"
    $failedSecrets = Join-Path ([string]$State.stateDirectory) "secrets.failed-restore-$stamp"
    $mergedSecrets = ""
    $databaseSwitched = $false
    $secretsSwitched = $false
    try {
        $previouslyRunning = @(Get-FbRunningServices $State $ComposeFile)
        if ($previouslyRunning -notcontains "db") {
            Start-FbDatabaseForOperation $State $ComposeFile "Start PostgreSQL temporarily for restore verification"
            $startedDatabase = $true
        } else {
            Wait-FbServiceHealth $State $ComposeFile @("db")
        }
        $verified = Invoke-FbVerifyBackup $State $ComposeFile $BackupPath $AgeIdentityFile -AllowPlaintextDatabaseOnlyRestore:$AllowPlaintextDatabaseOnlyRestore
        $writers = @($previouslyRunning | Where-Object { $_ -in @("frontend", "api") })
        if ($writers.Count -gt 0) {
            $null = Invoke-FbCompose $State $ComposeFile (@("stop", "--timeout", "60") + $writers) "Stop application writers before restore" -Mutating
        }
        Assert-FbWritersQuiesced $State $ComposeFile "Restore"
        Restore-FbCandidateDatabase $State $ComposeFile ([string]$verified.dumpPath) $candidate
        $recoveredStatePath = if ([bool]$verified.completeRecoverySet) { [string]$verified.expandedPath } else { "" }
        $mergedSecrets = New-FbMergedSecretDirectory $State $recoveredStatePath
        Switch-FbDatabase $State $ComposeFile $candidate $preservedDatabase
        $databaseSwitched = $true
        Switch-FbSecretDirectory $State $mergedSecrets $preservedSecrets
        $secretsSwitched = $true
        $null = Invoke-FbCompose $State $ComposeFile @("run", "--rm", "--no-deps", "role-provision") "Reapply the least-privileged database login after restore" -Mutating
        $null = Invoke-FbCompose $State $ComposeFile @("run", "--rm", "--no-deps", "migrate") "Apply forward-only migrations to the restored database" -Mutating
        $null = Invoke-FbCompose $State $ComposeFile @("up", "-d", "--no-deps", "--force-recreate", "api", "frontend") "Start restored Private Server for health verification" -Mutating
        Wait-FbHttpHealth ([int]$State.port)
        if ($previouslyRunning -notcontains "frontend") { $null = Invoke-FbCompose $State $ComposeFile @("stop", "frontend") "Return frontend to its previous stopped state" -Mutating }
        if ($previouslyRunning -notcontains "api") { $null = Invoke-FbCompose $State $ComposeFile @("stop", "api") "Return API to its previous stopped state" -Mutating }
        if ($startedDatabase) { $null = Invoke-FbCompose $State $ComposeFile @("stop", "db") "Return PostgreSQL to its previous stopped state" -Mutating }
        $State.status = "ready"
        $preserved = @(Get-FbProperty $State "preservedDatabases" @()) + @($preservedDatabase)
        if ($null -eq $State.PSObject.Properties["preservedDatabases"]) { $State | Add-Member -NotePropertyName preservedDatabases -NotePropertyValue $preserved }
        else { $State.preservedDatabases = $preserved }
        Save-FbState $State
        Write-Host "Restore completed and passed health checks."
        Write-Host "All pre-restore browser sessions were invalidated by rotating the session-signing key."
        Write-Host "The pre-restore database remains preserved as '$preservedDatabase' for deliberate recovery/cleanup."
        if ($secretsSwitched) { Write-Host "The pre-restore secret set remains preserved at '$preservedSecrets'." }
    } catch {
        $originalFailure = $_
        $recoveryFailures = New-Object System.Collections.Generic.List[string]
        $writerStateProven = $true
        if ($originalFailure.Exception.Data.Contains("FilingBridgeDatabaseRecoveryRequired")) {
            $recoveryFailures.Add("database switch self-rollback failed")
            $writerStateProven = $false
        }
        if ($databaseSwitched) {
            try {
                $null = Invoke-FbCompose $State $ComposeFile @("stop", "--timeout", "60", "frontend", "api") "Stop restored writers before selecting the preserved database" -Mutating
            } catch {
                $writerStateProven = $false
                $recoveryFailures.Add("writer quiescence could not be proven: $($_.Exception.Message)")
            }
            if ($writerStateProven) {
                try {
                    Undo-FbDatabaseSwitch $State $ComposeFile $failedDatabase $preservedDatabase
                } catch {
                    $recoveryFailures.Add("database rollback failed: $($_.Exception.Message)")
                }
            }
        }
        if ($secretsSwitched -and $writerStateProven) {
            try {
                Undo-FbSecretSwitch $State $preservedSecrets $failedSecrets
            } catch {
                $recoveryFailures.Add("secret rollback failed: $($_.Exception.Message)")
            }
        }
        if ($recoveryFailures.Count -gt 0) {
            try {
                $State.status = "restoreRecoveryRequired"
                Save-FbState $State
            } catch { }
            $writerWarning = if ($writerStateProven) { "API/frontend writers were stopped." } else { "API/frontend writer state is unproven; do not use the service and stop the project at the Docker layer before manual recovery." }
            throw "Restore failed and automatic rollback did not complete. $writerWarning Do not start writers or delete the preserved database/secret directories. Original failure: $($originalFailure.Exception.Message) Recovery failure: $($recoveryFailures -join ' ')"
        }
        try {
            Restore-FbRunningServices $State $ComposeFile $previouslyRunning
        } catch {
            throw "Restore failed and the preserved pre-restore database and keys were selected again, but the previous service state could not be restored. Original failure: $($originalFailure.Exception.Message) Service-state failure: $($_.Exception.Message)"
        }
        if ($startedDatabase) { $null = Invoke-FbCompose $State $ComposeFile @("stop", "db") "Return PostgreSQL to its previous stopped state after failed restore" -Mutating }
        throw "Restore failed; the preserved pre-restore database and keys were selected again. $($originalFailure.Exception.Message)"
    } finally {
        if ($null -ne $verified) {
            $stagingPath = [string](Get-FbProperty $verified "stagingPath" "")
            if (-not [string]::IsNullOrWhiteSpace($stagingPath)) { Remove-FbTemporaryDirectory $stagingPath }
        }
        if (-not [string]::IsNullOrWhiteSpace($mergedSecrets) -and (Test-Path -LiteralPath $mergedSecrets -PathType Container)) {
            $resolvedMerged = [IO.Path]::GetFullPath($mergedSecrets)
            $resolvedStateRoot = [IO.Path]::GetFullPath([string]$State.stateDirectory)
            if ((Test-FbPathWithin $resolvedMerged $resolvedStateRoot) -and -not $resolvedMerged.Equals($resolvedStateRoot, [StringComparison]::OrdinalIgnoreCase)) {
                Remove-Item -LiteralPath $resolvedMerged -Recurse -Force
            }
        }
    }
}

function Invoke-FbUpdate {
    param(
        $State,
        [string]$ComposeFile,
        [string]$RepositoryRoot,
        [string]$ReleaseManifest,
        [string]$OutputDirectory,
        [string]$BackupRecipient,
        [string]$AgeIdentityFile,
        [switch]$PlaintextDatabaseOnly,
        [switch]$BuildLocal,
        [switch]$DryRun
    )
    $wasUninstalled = ([string]$State.status -eq "uninstalled")
    $installedComposeFile = [string](Get-FbProperty $State "installedComposeFile" "")
    if ([string]::IsNullOrWhiteSpace($installedComposeFile) -or -not (Test-Path -LiteralPath $installedComposeFile -PathType Leaf)) {
        throw "Installed Compose snapshot is missing; update cannot create a trustworthy pre-update backup."
    }
    Assert-FbComposeMatchesState $State $installedComposeFile
    $oldInstalledComposeText = Read-FbUtf8Text $installedComposeFile
    if (-not [string]::IsNullOrWhiteSpace($OutputDirectory) -and (Test-FbPathWithin (ConvertTo-FbFullPath $OutputDirectory) $RepositoryRoot)) {
        throw "Pre-update backup output must be outside the new release/source directory."
    }
    if ($BuildLocal -and [string]$State.releaseVersion -ne "source-build") {
        throw "Update refuses to replace a semantic compiled release with an unreviewed source build. Install a strictly newer reviewed release."
    }
    $newRelease = if ($BuildLocal) {
        if ($DryRun) { [pscustomobject]@{ path = ""; sha256 = ""; version = "source-build"; commitSha = ""; reviewed = $false; integrityStatus = "source-build-unreviewed"; images = $State.images } }
        else { New-FbLocalBuildRelease $RepositoryRoot ([string]$State.instanceId) }
    } else {
        Read-FbReleaseManifest (Get-FbReleaseManifestPath $ReleaseManifest $RepositoryRoot) $RepositoryRoot
    }
    Assert-FbForwardUpdate $State $newRelease -BuildLocal:$BuildLocal -DryRun:$DryRun
    if ($DryRun) {
        $null = Invoke-FbBackup $State $installedComposeFile $OutputDirectory $BackupRecipient -PlaintextDatabaseOnly:$PlaintextDatabaseOnly -DryRun
        Write-Host "DRY RUN: pull/build the selected compiled release, stop writers, migrate, start, and health-check"
        Write-Host "DRY RUN: database migration rollback would not be claimed or attempted"
        return
    }
    if ($wasUninstalled) {
        $null = Invoke-FbCompose $State $installedComposeFile @("up", "-d", "--no-deps", "--wait", "--wait-timeout", "300", "db") "Recreate PostgreSQL from the retained Private Server volume for reinstall" -Mutating
    }
    $backup = Invoke-FbBackup $State $installedComposeFile $OutputDirectory $BackupRecipient -PlaintextDatabaseOnly:$PlaintextDatabaseOnly
    if ($null -eq $State.PSObject.Properties["lastPreUpdateBackup"]) {
        $State | Add-Member -NotePropertyName lastPreUpdateBackup -NotePropertyValue $backup
    } else {
        $State.lastPreUpdateBackup = $backup
    }
    Save-FbState $State
    $verification = $null
    try {
        $verification = Invoke-FbVerifyBackup $State $installedComposeFile $backup $AgeIdentityFile -AllowPlaintextDatabaseOnlyRestore:$PlaintextDatabaseOnly
    } finally {
        if ($null -ne $verification) {
            $verifyStaging = [string](Get-FbProperty $verification "stagingPath" "")
            if (-not [string]::IsNullOrWhiteSpace($verifyStaging)) { Remove-FbTemporaryDirectory $verifyStaging }
        }
    }
    $old = [pscustomobject]@{
        images = $State.images
        releaseVersion = [string]$State.releaseVersion
        releaseCommitSha = [string]$State.releaseCommitSha
        releaseManifest = [string]$State.releaseManifest
        releaseManifestSha256 = [string]$State.releaseManifestSha256
        reviewedRelease = [bool]$State.reviewedRelease
        releaseIntegrityStatus = [string](Get-FbProperty $State "releaseIntegrityStatus" "unknown")
        releaseDirectory = [string](Get-FbProperty $State "releaseDirectory" $RepositoryRoot)
        composeFileSha256 = [string](Get-FbProperty $State "composeFileSha256" "")
        installedComposeFile = $installedComposeFile
    }
    $previouslyRunning = @(Get-FbRunningServices $State $installedComposeFile)
    try {
        $State.images = $newRelease.images
        $State.releaseVersion = [string]$newRelease.version
        $State.releaseCommitSha = [string]$newRelease.commitSha
        $State.releaseManifest = [string]$newRelease.path
        $State.releaseManifestSha256 = [string]$newRelease.sha256
        $State.reviewedRelease = [bool]$newRelease.reviewed
        if ($null -eq $State.PSObject.Properties["releaseIntegrityStatus"]) {
            $State | Add-Member -NotePropertyName releaseIntegrityStatus -NotePropertyValue ([string]$newRelease.integrityStatus)
        } else {
            $State.releaseIntegrityStatus = [string]$newRelease.integrityStatus
        }
        if ($null -eq $State.PSObject.Properties["releaseDirectory"]) { $State | Add-Member -NotePropertyName releaseDirectory -NotePropertyValue $RepositoryRoot }
        else { $State.releaseDirectory = $RepositoryRoot }
        if ($null -eq $State.PSObject.Properties["composeFileSha256"]) {
            $State | Add-Member -NotePropertyName composeFileSha256 -NotePropertyValue ((Get-FileHash -LiteralPath $ComposeFile -Algorithm SHA256).Hash.ToLowerInvariant())
        } else {
            $State.composeFileSha256 = (Get-FileHash -LiteralPath $ComposeFile -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        Write-FbTextAtomic -Path $installedComposeFile -Value (Read-FbUtf8Text $ComposeFile)
        Set-FbRestrictedAcl -Path $installedComposeFile
        if (-not $BuildLocal -and (Get-FileHash -LiteralPath $installedComposeFile -Algorithm SHA256).Hash.ToLowerInvariant() -cne [string]$newRelease.composeSha256) {
            throw "compose.private.yml changed after release verification; update refused the mutable source and did not run the target topology."
        }
        $State.composeFileSha256 = (Get-FileHash -LiteralPath $installedComposeFile -Algorithm SHA256).Hash.ToLowerInvariant()
        $State.status = "updating"
        Write-FbEnvironmentFile $State
        Save-FbState $State
        if (-not $BuildLocal) { $null = Invoke-FbCompose $State $installedComposeFile @("pull", "--policy", "always") "Pull exact update image digests" -Mutating }
        $writers = @($previouslyRunning | Where-Object { $_ -in @("frontend", "api") })
        if ($writers.Count -gt 0) { $null = Invoke-FbCompose $State $installedComposeFile (@("stop", "--timeout", "60") + $writers) "Stop writers for the controlled update" -Mutating }
        $null = Invoke-FbCompose $State $installedComposeFile @("up", "-d", "--wait", "--wait-timeout", "300", "db") "Start and verify PostgreSQL for the update" -Mutating
        Assert-FbWritersQuiesced $State $installedComposeFile "Update"
        $null = Invoke-FbCompose $State $installedComposeFile @("run", "--rm", "--no-deps", "role-provision") "Reapply the least-privileged database login for the update" -Mutating
        $null = Invoke-FbCompose $State $installedComposeFile @("run", "--rm", "--no-deps", "migrate") "Run controlled forward migrations for the update" -Mutating
        $null = Invoke-FbCompose $State $installedComposeFile @("up", "-d", "--no-deps", "--wait", "--wait-timeout", "300", "api", "frontend") "Start and verify the updated runtime" -Mutating
        Wait-FbHttpHealth ([int]$State.port)
        if (-not $wasUninstalled -and $previouslyRunning -notcontains "frontend") { $null = Invoke-FbCompose $State $installedComposeFile @("stop", "frontend") "Restore previous frontend state" -Mutating }
        if (-not $wasUninstalled -and $previouslyRunning -notcontains "api") { $null = Invoke-FbCompose $State $installedComposeFile @("stop", "api") "Restore previous API state" -Mutating }
        if (-not $wasUninstalled -and $previouslyRunning -notcontains "db") { $null = Invoke-FbCompose $State $installedComposeFile @("stop", "db") "Restore previous PostgreSQL state" -Mutating }
        $State.status = "ready"
        Save-FbState $State
        Write-Host "Update completed and passed health checks. Pre-update backup: $backup"
    } catch {
        $updateFailure = $_
        $State.images = $old.images
        $State.releaseVersion = $old.releaseVersion
        $State.releaseCommitSha = $old.releaseCommitSha
        $State.releaseManifest = $old.releaseManifest
        $State.releaseManifestSha256 = $old.releaseManifestSha256
        $State.reviewedRelease = $old.reviewedRelease
        $State.releaseIntegrityStatus = $old.releaseIntegrityStatus
        $State.releaseDirectory = $old.releaseDirectory
        $State.composeFileSha256 = $old.composeFileSha256
        Write-FbTextAtomic -Path $old.installedComposeFile -Value $oldInstalledComposeText
        Set-FbRestrictedAcl -Path $old.installedComposeFile
        $State.status = "updateFailed"
        Write-FbEnvironmentFile $State
        Save-FbState $State
        try {
            $null = Invoke-FbCompose $State $old.installedComposeFile @("stop", "--timeout", "60", "frontend", "api") "Keep writers stopped after failed update" -Mutating
        } catch {
            throw "Update failed and FilingBridge could not prove that API/frontend writers stopped. Do not use the service. Run 'FilingBridge.cmd stop' from the previous installed release directory, verify containers are stopped, then run the explicit restore against '$backup'. Old image references were restored, but an old image cannot reverse a database migration. Update failure: $($updateFailure.Exception.Message) Writer-stop failure: $($_.Exception.Message)"
        }
        throw "Update failed. API/frontend writers were stopped and the verified pre-update backup is '$backup'. Old image references were restored, but an old image cannot reverse a database migration. Run the explicit restore command from the previous installed release directory after reviewing logs. $($updateFailure.Exception.Message)"
    }
}

function Invoke-FbOwnerRecovery {
    param($State, [string]$ComposeFile, [string]$OwnerEmail, [string]$Confirmation, [switch]$NonInteractive, [switch]$DryRun)
    if ([string]::IsNullOrWhiteSpace($OwnerEmail)) { $OwnerEmail = [string]$State.ownerEmail }
    if ($OwnerEmail -notmatch '^[^\s@]+@[^\s@]+\.[^\s@]+$') { throw "Owner email is not valid." }
    $tenantSlug = [string]$State.tenantSlug
    if ([string]::IsNullOrWhiteSpace($tenantSlug)) { throw "Private Server state does not contain the configured tenant slug." }
    $phrase = "RECOVER PRIVATE SERVER OWNER"
    Confirm-FbTypedAction $phrase $Confirmation "Owner recovery revokes active authentication state and issues a one-time reset token." -NonInteractive:$NonInteractive
    if ($DryRun) {
        Write-Host "DRY RUN: verify the existing database/runtime without migration, then run the dedicated recovery one-shot."
    } elseif ([string]$State.status -eq "setupFailed") {
        $null = Invoke-FbCompose $State $ComposeFile @("up", "-d", "--no-deps", "--wait", "--wait-timeout", "300", "db") "Verify PostgreSQL before recovering a setup whose initializer already committed" -Mutating
        $null = Invoke-FbCompose $State $ComposeFile @("up", "-d", "--no-deps", "--wait", "--wait-timeout", "300", "api", "frontend") "Verify the compiled runtime before issuing a setup-recovery token" -Mutating
        Wait-FbHttpHealth ([int]$State.port)
    } else {
        Invoke-FbStart $State $ComposeFile
    }
    $arguments = @(
        "--profile", "owner-recovery", "run", "--rm", "--no-deps",
        "-e", "PrivateOwnerRecovery__ConfirmInstallationId=$($State.instanceId)",
        "-e", "PrivateOwnerRecovery__TenantSlug=$tenantSlug",
        "-e", "PrivateOwnerRecovery__OwnerEmail=$OwnerEmail",
        "-e", "PrivateOwnerRecovery__ConfirmOwnerEmail=$OwnerEmail",
        "-e", "PrivateOwnerRecovery__ConfirmationPhrase=$phrase",
        "private-owner-recovery"
    )
    $result = Invoke-FbCompose $State $ComposeFile $arguments "Run physical-host Owner recovery" -Mutating -DryRun:$DryRun
    if ($DryRun) { Write-Host "DRY RUN: reset token output would be withheld unless the one-shot command succeeded."; return }
    $recovery = $null
    foreach ($line in @($result.Output | Select-Object -Last 30)) {
        try {
            $candidate = $line | ConvertFrom-Json -ErrorAction Stop
            if ($null -ne $candidate.PSObject.Properties["resetToken"] -or $null -ne $candidate.PSObject.Properties["token"]) { $recovery = $candidate }
        } catch { }
    }
    if ($null -eq $recovery) { throw "Owner recovery succeeded but its one-time JSON result could not be parsed. The output was withheld; inspect the implementation before retrying." }
    $token = [string](Get-FbProperty $recovery "resetToken" (Get-FbProperty $recovery "token" ""))
    if ([string]::IsNullOrWhiteSpace($token)) { throw "Owner recovery returned no reset token." }
    if ([string]$State.status -eq "setupFailed") { $State.status = "ready"; Save-FbState $State }
    Write-Host "Owner recovery completed for $OwnerEmail. Existing sessions, MFA state, and terminal tokens were revoked by the backend."
    $escapedToken = [Uri]::EscapeDataString($token)
    $expiresAtUtc = [string](Get-FbProperty $recovery "expiresAtUtc" "")
    Write-Host "Open this one-time reset link locally: $($State.localOrigin)/reset-password#token=$escapedToken"
    if (-not [string]::IsNullOrWhiteSpace($expiresAtUtc)) { Write-Host "The reset token expires at $expiresAtUtc." }
    Write-Host "The raw token is shown once and is not retained by the operator or database."
    $token = $null
    $escapedToken = $null
}

function Invoke-FbExportRecoveryKey {
    param(
        $State,
        [string]$OutputDirectory,
        [string]$Confirmation,
        [switch]$NonInteractive,
        [switch]$DryRun
    )
    $expected = "EXPORT RECOVERY KEY $($State.instanceId)"
    Confirm-FbTypedAction $expected $Confirmation "Export the separate trust anchor required for replacement-host recovery." -NonInteractive:$NonInteractive
    $resolvedOutput = Resolve-FbManagedOutputDirectory $OutputDirectory $State "RecoveryKeys" "FilingBridge Recovery Keys"
    if ($DryRun) {
        Write-Host "DRY RUN: export the installation backup-authentication trust anchor to $resolvedOutput"
        Write-Host "DRY RUN: the key must be retained separately from encrypted backups and the age identity"
        return
    }
    Initialize-FbManagedOutputDirectory $resolvedOutput $State "RecoveryKeys"
    $source = Join-Path ([string]$State.secretDirectory) "backup_authentication_key"
    $key = Read-FbBackupAuthenticationKey $State
    try {
        $keyId = (Get-FbSha256Text ([Convert]::ToBase64String($key))).Substring(0, 16)
        $destination = Join-Path $resolvedOutput ("filingbridge-{0}-recovery-authentication-{1}.key" -f ([string]$State.instanceId).Replace('-', '').Substring(0, 12), $keyId)
        if (Test-Path -LiteralPath $destination) { throw "Recovery authentication key already exists at $destination; no file was replaced." }
        Write-FbTextExclusive -Path $destination -Value ((Read-FbUtf8Text $source).Trim() + [Environment]::NewLine)
        Set-FbRestrictedAcl $destination
        Write-Host "Recovery authentication key exported: $destination"
        Write-Warning "Store this trust anchor separately from both the encrypted recovery set and its age identity. Anyone holding all three can recover the installation."
    } finally { [Array]::Clear($key, 0, $key.Length) }
}

function Get-FbBootIdentity {
    return [string](Get-FbBootEvidence).identity
}

function Assert-FbImportantTableRowCountsMatch {
    param([object[]]$Expected, [object[]]$Actual, [string]$Description)
    $expectedItems = @($Expected | Sort-Object { [string](Get-FbProperty $_ "table" "") })
    $actualItems = @($Actual | Sort-Object { [string](Get-FbProperty $_ "table" "") })
    if ($expectedItems.Count -ne 5 -or $actualItems.Count -ne 5) { throw "$Description is incomplete." }
    for ($index = 0; $index -lt $expectedItems.Count; $index++) {
        $expectedTable = [string](Get-FbProperty $expectedItems[$index] "table" "")
        if ($expectedTable -cne [string](Get-FbProperty $actualItems[$index] "table" "") -or
            [long](Get-FbProperty $expectedItems[$index] "rowCount" -1) -ne [long](Get-FbProperty $actualItems[$index] "rowCount" -2)) {
            throw "$Description differs for important table '$expectedTable'."
        }
    }
}

function Get-FbBootEvidence {
    if ($null -ne $script:PrivateServerCommandInvoker -and -not [string]::IsNullOrWhiteSpace($env:FILINGBRIDGE_TEST_BOOT_ID)) {
        $testBoot = [DateTimeOffset]::MinValue
        if (-not [DateTimeOffset]::TryParse([string]$env:FILINGBRIDGE_TEST_BOOT_ID, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal, [ref]$testBoot)) {
            throw "The injected reboot-test identity must be an ISO-8601 timestamp."
        }
        return [pscustomobject][ordered]@{
            identity = "test-boot-" + (Get-FbSha256Text ([string]$env:FILINGBRIDGE_TEST_BOOT_ID)).Substring(0, 24)
            startedAtUtc = $testBoot.ToUniversalTime().ToString("o")
        }
    }
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw "The reboot acceptance check is supported only on Windows." }
    $operatingSystem = Get-CimInstance -ClassName Win32_OperatingSystem -ErrorAction Stop
    $lastBoot = [DateTimeOffset]$operatingSystem.LastBootUpTime
    return [pscustomobject][ordered]@{
        identity = "windows-boot-" + $lastBoot.ToUniversalTime().Ticks.ToString([Globalization.CultureInfo]::InvariantCulture)
        startedAtUtc = $lastBoot.ToUniversalTime().ToString("o")
    }
}

function Get-FbRebootRuntimeEvidence {
    param($State, [string]$ComposeFile)
    $evidence = New-Object System.Collections.Generic.List[object]
    foreach ($service in @("db", "api", "frontend")) {
        $containerId = Get-FbOwnedRuntimeContainerId $State $ComposeFile $service
        $inspection = Invoke-FbNative -FilePath "docker" -Arguments @(
            "inspect", "--format", "{{.Id}}|{{.State.StartedAt}}", $containerId
        ) -Description "Read $service reboot runtime evidence"
        $value = (($inspection.Output | Select-Object -Last 1) -join "").Trim()
        $match = [regex]::Match($value, '^(?<id>[a-f0-9]{12,64})\|(?<started>[^|]+)$')
        $started = [DateTimeOffset]::MinValue
        if ($inspection.ExitCode -ne 0 -or -not $match.Success -or
            -not [DateTimeOffset]::TryParse($match.Groups['started'].Value, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal, [ref]$started)) {
            throw "Reboot evidence for '$service' could not be interpreted safely."
        }
        if ($match.Groups['id'].Value -cne $containerId) { throw "Reboot evidence for '$service' did not match its owned container." }
        $evidence.Add([pscustomobject][ordered]@{ service = $service; containerId = $containerId; startedAtUtc = $started.ToUniversalTime().ToString("o") })
    }
    return $evidence.ToArray()
}

function Get-FbAcceptanceDirectory {
    param($State)
    $directory = Join-Path ([string]$State.stateDirectory) "acceptance"
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        New-Item -ItemType Directory -Path $directory | Out-Null
    }
    Set-FbRestrictedAcl $directory
    return $directory
}

function Invoke-FbRebootCheck {
    param($State, [string]$ComposeFile, [string]$Action, [string]$OutputDirectory, [switch]$DryRun)
    $normalizedAction = if ([string]::IsNullOrWhiteSpace($Action)) { "status" } else { $Action.Trim().ToLowerInvariant() }
    if ($normalizedAction -notin @("prepare", "verify", "status")) { throw "reboot-check action must be prepare, verify, or status." }
    $acceptanceDirectory = Get-FbAcceptanceDirectory $State
    $pendingPath = Join-Path $acceptanceDirectory "reboot-check.pending.json"
    if ($normalizedAction -eq "status") {
        if (Test-Path -LiteralPath $pendingPath -PathType Leaf) { Write-Host (Read-FbUtf8Text $pendingPath) }
        else { Write-Host "No reboot acceptance check is pending." }
        return
    }
    if ($DryRun) {
        Write-Host "DRY RUN: $normalizedAction the Windows reboot persistence acceptance check"
        return
    }
    if ($normalizedAction -eq "prepare") {
        if (Test-Path -LiteralPath $pendingPath -PathType Leaf) { throw "A reboot acceptance check is already pending. Verify it after reboot before preparing another." }
        $running = @(Get-FbRunningServices $State $ComposeFile)
        foreach ($required in @("db", "api", "frontend")) { if ($running -notcontains $required) { throw "Cannot prepare reboot evidence because '$required' is not running." } }
        Wait-FbHttpHealth -Port ([int]$State.port)
        $tables = @(Get-FbImportantTableEvidence $State $ComposeFile "accounts" "Read important-table fingerprints from the database before Windows reboot")
        $bootBefore = Get-FbBootEvidence
        $record = [ordered]@{
            schemaVersion = "filingbridge.private-server.reboot-check/v2"; status = "pending"
            instanceId = [string]$State.instanceId; releaseVersion = [string]$State.releaseVersion
            releaseCommitSha = [string]$State.releaseCommitSha; preparedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
            composeProject = [string]$State.composeProject; composeFileSha256 = [string]$State.composeFileSha256
            acceptanceContract = "release-bound-auto-restart/v1"
            bootIdentityFormat = "opaque-boot-identity/v1"; bootIdentityBefore = [string]$bootBefore.identity
            bootStartedAtBeforeUtc = [string]$bootBefore.startedAtUtc
            expectedServices = @("db", "api", "frontend")
            runtimeBefore = @(Get-FbRebootRuntimeEvidence $State $ComposeFile)
            importantTablesBefore = $tables
        }
        Write-FbJsonAtomic -Path $pendingPath -Value $record
        Set-FbRestrictedAcl $pendingPath
        Write-Host "Reboot check prepared. Reboot Windows normally, wait for Docker Desktop, then run: FilingBridge.cmd reboot-check verify"
        return
    }
    if (-not (Test-Path -LiteralPath $pendingPath -PathType Leaf)) { throw "No prepared reboot acceptance record was found." }
    $pending = (Read-FbUtf8Text $pendingPath) | ConvertFrom-Json
    if ([string]$pending.instanceId -cne [string]$State.instanceId -or [string]$pending.status -cne "pending") { throw "Pending reboot evidence does not belong to this installation." }
    if ([string](Get-FbProperty $pending "bootIdentityFormat" "") -cne "opaque-boot-identity/v1") {
        throw "Pending reboot evidence uses an unsupported boot identity format. Remove it and run reboot-check prepare again before rebooting."
    }
    if ([string](Get-FbProperty $pending "schemaVersion" "") -cne "filingbridge.private-server.reboot-check/v2" -or
        [string](Get-FbProperty $pending "acceptanceContract" "") -cne "release-bound-auto-restart/v1") {
        throw "Pending reboot evidence uses an unsupported acceptance contract. Remove it and prepare the check again."
    }
    foreach ($identity in @(
        @("releaseVersion", [string]$State.releaseVersion),
        @("releaseCommitSha", [string]$State.releaseCommitSha),
        @("composeProject", [string]$State.composeProject),
        @("composeFileSha256", [string]$State.composeFileSha256)
    )) {
        if ([string](Get-FbProperty $pending $identity[0] "") -cne $identity[1]) {
            throw "The installed release or Compose identity changed after reboot evidence was prepared. Prepare and perform a new reboot check for this release."
        }
    }
    $bootAfter = Get-FbBootEvidence
    if ([string]$bootAfter.identity -ceq [string]$pending.bootIdentityBefore) { throw "Windows has not rebooted since this check was prepared." }
    $running = @(Get-FbRunningServices $State $ComposeFile)
    foreach ($required in @("db", "api", "frontend")) { if ($running -notcontains $required) { throw "Reboot persistence failed because '$required' did not return automatically." } }
    $runtimeAfter = @(Get-FbRebootRuntimeEvidence $State $ComposeFile)
    $bootStartedAfter = [DateTimeOffset]::Parse([string]$bootAfter.startedAtUtc, [Globalization.CultureInfo]::InvariantCulture)
    foreach ($current in $runtimeAfter) {
        $before = @($pending.runtimeBefore | Where-Object { [string]$_.service -ceq [string]$current.service })
        $started = [DateTimeOffset]::Parse([string]$current.startedAtUtc, [Globalization.CultureInfo]::InvariantCulture)
        if ($before.Count -ne 1 -or [string]$before[0].containerId -cne [string]$current.containerId -or
            [string]$before[0].startedAtUtc -ceq [string]$current.startedAtUtc -or
            $started -lt $bootStartedAfter -or $started -gt $bootStartedAfter.AddMinutes(30)) {
            throw "Reboot persistence failed because '$($current.service)' was not the prepared container automatically restarted within 30 minutes of Windows boot."
        }
    }
    Wait-FbHttpHealth -Port ([int]$State.port)
    $tablesAfter = @(Get-FbImportantTableEvidence $State $ComposeFile "accounts" "Read important-table fingerprints from the database after Windows reboot")
    Assert-FbImportantTableEvidenceMatches -Expected @($pending.importantTablesBefore) -Actual $tablesAfter -Description "Windows reboot persistence"
    $completed = [ordered]@{
        schemaVersion = "filingbridge.private-server.reboot-check/v2"; status = "passed"
        instanceId = [string]$State.instanceId; releaseVersion = [string]$State.releaseVersion
        releaseCommitSha = [string]$State.releaseCommitSha; preparedAtUtc = [string]$pending.preparedAtUtc
        composeProject = [string]$State.composeProject; composeFileSha256 = [string]$State.composeFileSha256
        acceptanceContract = "release-bound-auto-restart/v1"
        verifiedAtUtc = (Get-Date).ToUniversalTime().ToString("o"); bootIdentityFormat = "opaque-boot-identity/v1"
        bootIdentityBefore = [string]$pending.bootIdentityBefore
        bootIdentityAfter = [string]$bootAfter.identity; bootStartedAtAfterUtc = [string]$bootAfter.startedAtUtc
        servicesRunning = @("db", "api", "frontend"); runtimeBefore = @($pending.runtimeBefore); runtimeAfter = $runtimeAfter
        readinessUri = "http://127.0.0.1:$($State.port)/health/ready"; importantTablesMatched = $true
        importantTablesBefore = @($pending.importantTablesBefore); importantTablesAfter = $tablesAfter
    }
    $completedPath = Join-Path $acceptanceDirectory ("reboot-check-{0}.json" -f (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmssfff"))
    Write-FbJsonAtomic -Path $completedPath -Value $completed
    Set-FbRestrictedAcl $completedPath
    Remove-Item -LiteralPath $pendingPath -Force
    if (-not [string]::IsNullOrWhiteSpace($OutputDirectory)) {
        $resolvedOutput = Resolve-FbManagedOutputDirectory $OutputDirectory $State "Acceptance" "FilingBridge Acceptance"
        Initialize-FbManagedOutputDirectory $resolvedOutput $State "Acceptance"
        Copy-Item -LiteralPath $completedPath -Destination (Join-Path $resolvedOutput ([IO.Path]::GetFileName($completedPath)))
    }
    Write-Host "Windows reboot persistence passed with unchanged business-data fingerprints: $completedPath"
}

function Invoke-FbLocalCheck {
    param($State, [string]$ComposeFile, [string]$OutputDirectory, [switch]$DryRun)
    if ($DryRun) {
        Write-Host "DRY RUN: verify runtime ownership, health, loopback-only frontend publication, absent API/database host ports, and business-data fingerprints"
        return
    }
    $running = @(Get-FbRunningServices $State $ComposeFile)
    foreach ($required in @("db", "api", "frontend")) { if ($running -notcontains $required) { throw "Local acceptance failed because '$required' is not running." } }
    Wait-FbHttpHealth -Port ([int]$State.port)
    $frontendPort = Invoke-FbCompose $State $ComposeFile @("port", "frontend", "3000") "Inspect frontend published port" -IgnoreExitCode
    if ($frontendPort.ExitCode -ne 0 -or @($frontendPort.Output).Count -ne 1 -or [string]$frontendPort.Output[0] -cne "127.0.0.1:$($State.port)") {
        throw "Local acceptance requires the frontend to publish exactly 127.0.0.1:$($State.port)."
    }
    $unpublished = [ordered]@{}
    foreach ($entry in @([pscustomobject]@{ service = "api"; port = "8080" }, [pscustomobject]@{ service = "db"; port = "5432" })) {
        $probe = Invoke-FbCompose $State $ComposeFile @("port", $entry.service, $entry.port) "Inspect $($entry.service) unpublished port" -IgnoreExitCode
        if ($probe.ExitCode -eq 0 -and @($probe.Output | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) }).Count -gt 0) {
            throw "Local acceptance failed because $($entry.service) has a host-published port."
        }
        $unpublished[$entry.service] = $true
    }
    $tables = @(Get-FbImportantTableEvidence $State $ComposeFile "accounts" "Read important-table fingerprints from the local acceptance database")
    $report = [ordered]@{
        schemaVersion = "filingbridge.private-server.local-check/v1"; status = "passed"
        instanceId = [string]$State.instanceId; releaseVersion = [string]$State.releaseVersion
        releaseCommitSha = [string]$State.releaseCommitSha; checkedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        deploymentMode = "PrivateServer"; runningServices = @("db", "api", "frontend")
        frontendBinding = "127.0.0.1:$($State.port)"; apiHostPortPublished = $false; databaseHostPortPublished = $false
        readinessUri = "http://127.0.0.1:$($State.port)/health/ready"; readinessPassed = $true
        tenantQualifiedLoginConfigured = $true; workspaceSlug = [string]$State.tenantSlug
        businessDataFingerprintCount = $tables.Count; importantTables = $tables
        statutoryBoundary = "No direct CRO/ROS submission; qualified-accountant review remains required for real filing reliance."
    }
    $acceptanceDirectory = Get-FbAcceptanceDirectory $State
    $path = Join-Path $acceptanceDirectory ("local-check-{0}.json" -f (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmssfff"))
    Write-FbJsonAtomic -Path $path -Value $report
    Set-FbRestrictedAcl $path
    if (-not [string]::IsNullOrWhiteSpace($OutputDirectory)) {
        $resolvedOutput = Resolve-FbManagedOutputDirectory $OutputDirectory $State "Acceptance" "FilingBridge Acceptance"
        Initialize-FbManagedOutputDirectory $resolvedOutput $State "Acceptance"
        Copy-Item -LiteralPath $path -Destination (Join-Path $resolvedOutput ([IO.Path]::GetFileName($path)))
    }
    Write-Host "Local Private Server acceptance passed: $path"
}

function Invoke-FbRecoverHost {
    param(
        [string]$StateDirectory,
        [string]$RepositoryRoot,
        [string]$ReleaseManifest,
        [string]$BackupPath,
        [string]$AgeIdentityFile,
        [string]$RecoveryAuthenticationKeyFile,
        [string]$Confirmation,
        [int]$Port,
        [switch]$BuildLocal,
        [switch]$NonInteractive,
        [switch]$DryRun,
        [switch]$SkipPrerequisiteChecks
    )
    if ($SkipPrerequisiteChecks -and $null -eq $script:PrivateServerCommandInvoker) {
        throw "Skipping prerequisite checks is available only through the injected operator test seam."
    }
    if ([string]::IsNullOrWhiteSpace($BackupPath)) { throw "-BackupPath is required for replacement-host recovery." }
    if ([string]::IsNullOrWhiteSpace($AgeIdentityFile)) { throw "-AgeIdentityFile is required for replacement-host recovery." }
    if ([string]::IsNullOrWhiteSpace($RecoveryAuthenticationKeyFile)) { throw "-RecoveryAuthenticationKeyFile is required for replacement-host recovery." }
    $resolvedStateDirectory = Resolve-FbStateDirectory $StateDirectory
    if (Test-FbPathWithin $resolvedStateDirectory $RepositoryRoot) { throw "Private Server state must be outside the release/source directory." }
    Assert-FbNoReparseAncestor $resolvedStateDirectory "State directory"
    if (Test-Path -LiteralPath $resolvedStateDirectory) { throw "Replacement-host recovery refuses to overwrite existing state at $resolvedStateDirectory." }
    $resolvedBackup = ConvertTo-FbFullPath $BackupPath
    $externalPath = "$resolvedBackup.manifest.json"
    if (-not (Test-Path -LiteralPath $resolvedBackup -PathType Leaf) -or -not (Test-Path -LiteralPath $externalPath -PathType Leaf)) {
        throw "The encrypted backup and its envelope manifest are both required."
    }
    $external = (Read-FbUtf8Text $externalPath) | ConvertFrom-Json
    if ([string](Get-FbProperty $external "schemaVersion" "") -ne "filingbridge.private-server.backup-envelope/v1" -or
        (Get-FbProperty $external "completeRecoverySet" $false) -ne $true -or
        (Get-FbProperty $external "encrypted" $false) -ne $true -or
        [string](Get-FbProperty $external "encryption" "") -ne "age") {
        throw "Replacement-host recovery requires a complete age-encrypted recovery set."
    }
    $sourceInstanceId = [string](Get-FbProperty $external "instanceId" "")
    $sourceGuid = [Guid]::Empty
    if (-not [Guid]::TryParse($sourceInstanceId, [ref]$sourceGuid)) { throw "Backup envelope has no valid source installation identity." }
    Confirm-FbTypedAction "RECOVER HOST $sourceInstanceId" $Confirmation "Recovering onto another host must occur only while the source installation is offline." -NonInteractive:$NonInteractive
    Assert-FbBackupHash $resolvedBackup $external
    $trustKey = Read-FbBackupAuthenticationKeyFile $RecoveryAuthenticationKeyFile "The separately retained recovery authentication key is required."
    try { Assert-FbBackupAuthenticationWithKey $trustKey $external }
    finally { [Array]::Clear($trustKey, 0, $trustKey.Length) }

    Assert-FbPrerequisites -StateDirectory $resolvedStateDirectory -RepositoryRoot $RepositoryRoot -Port $Port -SkipExternalChecks:$SkipPrerequisiteChecks
    $composeFile = Get-FbComposeFile $RepositoryRoot
    $newInstanceId = [Guid]::NewGuid().ToString("D")
    $release = if ($BuildLocal) {
        if (-not [string]::IsNullOrWhiteSpace($ReleaseManifest)) { throw "-BuildLocal and -ReleaseManifest cannot be combined." }
        if ($DryRun) {
            [pscustomobject]@{ path = ""; sha256 = ""; version = "source-build"; commitSha = ""; reviewed = $false; integrityStatus = "source-build-unreviewed"; images = [pscustomobject]@{ api = "filingbridge-private-api:<generated>"; frontend = "filingbridge-private-frontend:<generated>"; postgres = "postgres:16.4-alpine" } }
        } else { New-FbLocalBuildRelease -RepositoryRoot $RepositoryRoot -InstanceId $newInstanceId }
    } else {
        Read-FbReleaseManifest -Path (Get-FbReleaseManifestPath $ReleaseManifest $RepositoryRoot) -RepositoryRoot $RepositoryRoot
    }
    if (-not (Test-FbBackupReleaseCompatibility ([string]$external.releaseVersion) ([string]$release.version))) {
        throw "Backup release '$($external.releaseVersion)' is not compatible with recovery release '$($release.version)'. Use the same or a newer semantic release."
    }
    $sourceReleaseCommit = [string](Get-FbProperty $external "releaseCommitSha" "")
    if ([string]$external.releaseVersion -ceq [string]$release.version -and
        -not [string]::IsNullOrWhiteSpace($sourceReleaseCommit) -and
        $sourceReleaseCommit -cne [string]$release.commitSha) {
        throw "Backup claims the recovery release version but a different release commit."
    }
    if ($DryRun) {
        Write-Host "DRY RUN: authenticate and decrypt the complete recovery set"
        Write-Host "DRY RUN: create a new isolated installation identity and PostgreSQL volume"
        Write-Host "DRY RUN: restore and fingerprint-check the database, preserve cryptographic continuity, rotate browser sessions, migrate forward, and health-check"
        return
    }

    $staging = Join-Path ([IO.Path]::GetTempPath()) ("filingbridge-recover-host-" + [Guid]::NewGuid().ToString("N"))
    $state = $null
    $composeStarted = $false
    $stateDirectoryCreated = $false
    try {
        New-Item -ItemType Directory -Path $staging | Out-Null
        Set-FbRestrictedAcl $staging
        $trustedBackup = Join-Path $staging "authenticated-recovery-set.fbbackup.age"
        Copy-Item -LiteralPath $resolvedBackup -Destination $trustedBackup
        Set-FbRestrictedAcl $trustedBackup
        Assert-FbBackupHash $trustedBackup ([pscustomobject]@{ backupFileName = [IO.Path]::GetFileName($trustedBackup); backupSha256 = [string]$external.backupSha256; byteSize = [long]$external.byteSize })
        $expanded = Expand-FbEncryptedBackup $trustedBackup $AgeIdentityFile (Join-Path $staging "decrypted")
        $internal = Assert-FbInternalBackupInventory $expanded
        if ([string]$internal.instanceId -cne $sourceInstanceId -or [string]$internal.releaseVersion -cne [string]$external.releaseVersion -or
            [string](Get-FbProperty $internal "releaseCommitSha" "") -cne [string](Get-FbProperty $external "releaseCommitSha" "")) {
            throw "Backup envelope and encrypted recovery identity do not agree."
        }
        $recoveredSecrets = Join-Path $expanded "state\secrets"
        $recoveredKey = Read-FbBackupAuthenticationKeyFile (Join-Path $recoveredSecrets "backup_authentication_key")
        $suppliedKey = Read-FbBackupAuthenticationKeyFile $RecoveryAuthenticationKeyFile
        try {
            if (-not (Test-FbFixedTimeHexEqual (Get-FbSha256Text ([Convert]::ToBase64String($recoveredKey))) (Get-FbSha256Text ([Convert]::ToBase64String($suppliedKey))))) {
                throw "Encrypted recovery contents do not contain the separately retained authentication trust anchor."
            }
        } finally { [Array]::Clear($recoveredKey, 0, $recoveredKey.Length); [Array]::Clear($suppliedKey, 0, $suppliedKey.Length) }
        $oldState = (Read-FbUtf8Text (Join-Path $expanded "state\server.json")) | ConvertFrom-Json
        if ([string](Get-FbProperty $oldState "instanceId" "") -cne $sourceInstanceId) { throw "Recovered state and backup identity do not agree." }
        if ([int](Get-FbProperty $oldState "formatVersion" 0) -ne $script:SupportedStateFormat) { throw "Recovered state format is not supported by this release." }
        $expectedSourceShort = $sourceGuid.ToString("N").Substring(0, 12)
        if ([string](Get-FbProperty $oldState "mfaKeyId" "") -cne "mfa-$expectedSourceShort" -or
            [string](Get-FbProperty $oldState "auditKeyId" "") -cne "audit-$expectedSourceShort") {
            throw "Recovered MFA/audit key identity does not match the authenticated source installation."
        }
        $recoveredTenantSlug = [string](Get-FbProperty $oldState "tenantSlug" "")
        if ($recoveredTenantSlug -cnotmatch '^[a-z0-9](?:[a-z0-9-]{1,48}[a-z0-9])$') { throw "Recovered workspace slug is invalid." }
        foreach ($value in @([string]$oldState.tenantName, [string]$oldState.ownerEmail, [string]$oldState.ownerName)) {
            if ([string]::IsNullOrWhiteSpace($value) -or $value.Length -gt 160 -or $value -match "[\r\n\x00]") { throw "Recovered tenant/Owner identity is invalid." }
        }
        if ([string]$oldState.ownerEmail -notmatch '^[^\s@]+@[^\s@]+\.[^\s@]+$') { throw "Recovered Owner email is invalid." }

        New-Item -ItemType Directory -Path $resolvedStateDirectory -Force:$false | Out-Null
        $stateDirectoryCreated = $true
        $secretDirectory = Join-Path $resolvedStateDirectory "secrets"
        New-Item -ItemType Directory -Path $secretDirectory -Force:$false | Out-Null
        Set-FbRestrictedAcl $resolvedStateDirectory
        Set-FbRestrictedAcl $secretDirectory
        foreach ($required in @(Get-FbRequiredRecoveryPaths | Where-Object { $_ -like "state/secrets/*" })) {
            $name = Split-Path $required -Leaf
            Copy-Item -LiteralPath (Join-Path $recoveredSecrets $name) -Destination (Join-Path $secretDirectory $name)
            Set-FbRestrictedAcl (Join-Path $secretDirectory $name)
        }
        Write-FbTextExclusive -Path (Join-Path $secretDirectory "private_initial_owner_password") -Value "RECOVERED-HOST-NO-INITIAL-PASSWORD"
        Set-FbRestrictedAcl (Join-Path $secretDirectory "private_initial_owner_password")
        [IO.File]::WriteAllText((Join-Path $secretDirectory "auth_session_signing_key"), (New-PrivateServerRandomSecret), $script:Utf8NoBom)
        Set-FbRestrictedAcl (Join-Path $secretDirectory "auth_session_signing_key")
        $installedComposeFile = Join-Path $resolvedStateDirectory "compose.private.installed.yml"
        Write-FbTextAtomic -Path $installedComposeFile -Value (Read-FbUtf8Text $composeFile)
        Set-FbRestrictedAcl $installedComposeFile
        if (-not $BuildLocal -and (Get-FileHash -LiteralPath $installedComposeFile -Algorithm SHA256).Hash.ToLowerInvariant() -cne [string]$release.composeSha256) {
            throw "compose.private.yml changed after release verification; host recovery refused mutable source."
        }
        $now = (Get-Date).ToUniversalTime().ToString("o")
        $localOrigin = "http://localhost:$Port"
        $state = [pscustomobject][ordered]@{
            formatVersion = $script:SupportedStateFormat; status = "recovering"; instanceId = $newInstanceId
            composeProject = "filingbridge-" + $newInstanceId.Replace("-", "").Substring(0, 12)
            stateDirectory = $resolvedStateDirectory; releaseDirectory = $RepositoryRoot; secretDirectory = $secretDirectory
            environmentFile = Join-Path $resolvedStateDirectory $script:EnvironmentFileName; installedComposeFile = $installedComposeFile
            composeFileSha256 = (Get-FileHash -LiteralPath $installedComposeFile -Algorithm SHA256).Hash.ToLowerInvariant()
            port = $Port; localOrigin = $localOrigin; publicOrigin = $localOrigin
            tenantName = [string]$oldState.tenantName; tenantSlug = [string]$oldState.tenantSlug
            ownerEmail = [string]$oldState.ownerEmail; ownerName = [string]$oldState.ownerName
            mfaKeyId = [string]$oldState.mfaKeyId; auditKeyId = [string]$oldState.auditKeyId
            releaseVersion = [string]$release.version; releaseCommitSha = [string]$release.commitSha
            releaseManifest = [string]$release.path; releaseManifestSha256 = [string]$release.sha256
            reviewedRelease = [bool]$release.reviewed; releaseIntegrityStatus = [string]$release.integrityStatus; images = $release.images
            tailscaleEnabled = $false; tailscaleDnsName = ""; backupRecipient = [string](Get-FbProperty $external "recipient" "")
            recoveredFromInstanceId = $sourceInstanceId; recoveredAtUtc = $now; createdAtUtc = $now; updatedAtUtc = $now
        }
        Write-FbEnvironmentFile $state
        Save-FbState $state
        $null = Invoke-FbCompose $state $installedComposeFile @("config", "--quiet") "Validate the recovered Private Server topology"
        if (-not $BuildLocal) { $null = Invoke-FbCompose $state $installedComposeFile @("pull", "--policy", "always") "Pull exact recovery release images" -Mutating }
        $null = Invoke-FbCompose $state $installedComposeFile @("up", "-d", "--wait", "--wait-timeout", "300", "db") "Start a new isolated PostgreSQL service for host recovery" -Mutating
        $composeStarted = $true
        $expectedTables = @(Get-FbProperty (Get-FbProperty $external "databaseVerification") "importantTables" @())
        if ($expectedTables.Count -ne 5) { throw "Recovery envelope lacks the five required business-data fingerprints." }
        $dump = Join-Path $expanded "database\accounts.dump"
        $null = Test-FbDatabaseDumpRestore -State $state -ComposeFile $installedComposeFile -HostDumpPath $dump -ExpectedImportantTables $expectedTables
        $candidate = "fb_host_recovery_" + [Guid]::NewGuid().ToString("N").Substring(0, 12)
        Restore-FbCandidateDatabase $state $installedComposeFile $dump $candidate
        Switch-FbDatabase $state $installedComposeFile $candidate ("empty_before_host_recovery_" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
        $restoredTables = @(Get-FbImportantTableEvidence $state $installedComposeFile "accounts" "Read important-table fingerprints before host-recovery migrations")
        Assert-FbImportantTableEvidenceMatches -Expected $expectedTables -Actual $restoredTables -Description "Replacement-host restore before forward migrations"
        $null = Invoke-FbCompose $state $installedComposeFile @("run", "--rm", "--no-deps", "role-provision") "Reapply the least-privileged database login after host recovery" -Mutating
        $null = Invoke-FbCompose $state $installedComposeFile @("run", "--rm", "--no-deps", "migrate") "Apply forward-only migrations after host recovery" -Mutating
        $null = Invoke-FbCompose $state $installedComposeFile @("up", "-d", "--no-deps", "--wait", "--wait-timeout", "300", "api", "frontend") "Start the recovered Private Server runtime" -Mutating
        Wait-FbHttpHealth -Port $Port
        $actualTables = @(Get-FbImportantTableEvidence $state $installedComposeFile "accounts" "Read important-table fingerprints from the recovered database")
        Assert-FbImportantTableRowCountsMatch -Expected $expectedTables -Actual $actualTables -Description "Replacement-host recovery after forward migrations"
        $state.status = "ready"
        Save-FbState $state
        Write-Host "Replacement-host recovery completed and passed database fingerprint and health checks at $localOrigin"
        Write-Host "All pre-recovery browser sessions were invalidated. Source installation $sourceInstanceId must remain offline."
    } catch {
        $recoveryFailure = $_
        $writerStopFailure = ""
        if ($null -ne $state -and $composeStarted) {
            try {
                $null = Invoke-FbCompose $state ([string]$state.installedComposeFile) @("stop", "--timeout", "60", "frontend", "api") "Keep writers stopped after failed host recovery" -Mutating
                Assert-FbWritersQuiesced $state ([string]$state.installedComposeFile) "Replacement-host recovery"
            } catch { $writerStopFailure = $_.Exception.Message }
        }
        if ($null -ne $state) {
            try {
                $state.status = if ([string]::IsNullOrWhiteSpace($writerStopFailure)) { "hostRecoveryFailed" } else { "hostRecoveryRecoveryRequired" }
                Save-FbState $state
            } catch { }
        } elseif ($stateDirectoryCreated -and (Test-Path -LiteralPath $resolvedStateDirectory -PathType Container)) {
            try {
                $resolvedIncomplete = [IO.Path]::GetFullPath($resolvedStateDirectory)
                Assert-FbNoReparseAncestor $resolvedIncomplete "Incomplete recovery state directory"
                if ($resolvedIncomplete -ne [IO.Path]::GetPathRoot($resolvedIncomplete) -and -not (Test-FbPathWithin $resolvedIncomplete $RepositoryRoot)) {
                    Remove-Item -LiteralPath $resolvedIncomplete -Recurse -Force
                }
            } catch { }
        }
        if (-not [string]::IsNullOrWhiteSpace($writerStopFailure)) {
            throw "Replacement-host recovery failed and application-writer shutdown could not be proven. Do not use the recovered service; stop its Docker project before diagnosis. Original failure: $($recoveryFailure.Exception.Message) Writer-stop failure: $writerStopFailure"
        }
        throw "Replacement-host recovery failed with application writers stopped. $($recoveryFailure.Exception.Message)"
    } finally { Remove-FbTemporaryDirectory $staging }
}

function Invoke-FbVerifyBackupCommand {
    param($State, [string]$ComposeFile, [string]$BackupPath, [string]$AgeIdentityFile, [switch]$AllowPlaintextDatabaseOnlyRestore)
    $previouslyRunning = @(Get-FbRunningServices $State $ComposeFile)
    $startedDatabase = $false
    $verified = $null
    try {
        if ($previouslyRunning -notcontains "db") {
            Start-FbDatabaseForOperation $State $ComposeFile "Start PostgreSQL temporarily for backup verification"
            $startedDatabase = $true
        } else {
            Wait-FbServiceHealth $State $ComposeFile @("db")
        }
        $verified = Invoke-FbVerifyBackup $State $ComposeFile $BackupPath $AgeIdentityFile -AllowPlaintextDatabaseOnlyRestore:$AllowPlaintextDatabaseOnlyRestore
    } finally {
        if ($null -ne $verified) {
            $staging = [string](Get-FbProperty $verified "stagingPath" "")
            if (-not [string]::IsNullOrWhiteSpace($staging)) { Remove-FbTemporaryDirectory $staging }
        }
        if ($startedDatabase) { $null = Invoke-FbCompose $State $ComposeFile @("stop", "db") "Return PostgreSQL to its previous stopped state" -Mutating }
    }
}

function Invoke-FbDiagnose {
    param($State, [string]$ComposeFile, [string]$RepositoryRoot)
    $checks = New-Object System.Collections.Generic.List[object]
    $checks.Add([pscustomobject]@{ name = "state-format"; passed = ([int]$State.formatVersion -eq $script:SupportedStateFormat); detail = "format $($State.formatVersion)" })
    $checks.Add([pscustomobject]@{ name = "state-directory"; passed = (Test-Path -LiteralPath ([string]$State.stateDirectory) -PathType Container); detail = [string]$State.stateDirectory })
    $checks.Add([pscustomobject]@{ name = "compose-file"; passed = (Test-Path -LiteralPath $ComposeFile -PathType Leaf); detail = $ComposeFile })
    try {
        $stateDrive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot([string]$State.stateDirectory))
        $freeGiB = [Math]::Round($stateDrive.AvailableFreeSpace / 1GB, 1)
        $checks.Add([pscustomobject]@{ name = "state-disk-free"; passed = ($stateDrive.AvailableFreeSpace -ge 5GB); detail = "$freeGiB GiB free (5 GiB operational floor; Docker VHDX capacity must also be monitored)" })
    } catch {
        $checks.Add([pscustomobject]@{ name = "state-disk-free"; passed = $false; detail = "could not read free space" })
    }
    foreach ($secretName in @(
        "postgres_password", "postgres_application_password", "accounts_migration_connection_string",
        "accounts_application_connection_string", "auth_session_signing_key", "audit_integrity_signing_key",
        "database_tenant_context_key", "identity_hmac_key", "mfa_encryption_key", "backup_authentication_key", "accounts_api_key_hash", "accounts_api_key")) {
        $path = Join-Path ([string]$State.secretDirectory) $secretName
        $checks.Add([pscustomobject]@{ name = "secret:$secretName"; passed = (Test-Path -LiteralPath $path -PathType Leaf); detail = if (Test-Path -LiteralPath $path) { "present" } else { "missing" } })
    }
    foreach ($nativeCheck in @(
        [pscustomobject]@{ name = "docker-engine"; file = "docker"; args = @("version", "--format", "{{.Server.Os}}") },
        [pscustomobject]@{ name = "docker-compose"; file = "docker"; args = @("compose", "version", "--short") })) {
        try {
            $result = Invoke-FbNative $nativeCheck.file $nativeCheck.args $nativeCheck.name -IgnoreExitCode
            $checks.Add([pscustomobject]@{ name = $nativeCheck.name; passed = ($result.ExitCode -eq 0); detail = Protect-PrivateServerText (($result.Output | Select-Object -First 1) -join "") })
        } catch { $checks.Add([pscustomobject]@{ name = $nativeCheck.name; passed = $false; detail = Protect-PrivateServerText $_.Exception.Message }) }
    }
    try {
        $render = Invoke-FbCompose $State $ComposeFile @("config", "--quiet") "Validate resolved Private Server configuration" -IgnoreExitCode
        $checks.Add([pscustomobject]@{ name = "compose-config"; passed = ($render.ExitCode -eq 0); detail = if ($render.ExitCode -eq 0) { "valid" } else { Protect-PrivateServerText (($render.Output | Select-Object -Last 3) -join " ") } })
    } catch { $checks.Add([pscustomobject]@{ name = "compose-config"; passed = $false; detail = Protect-PrivateServerText $_.Exception.Message }) }
    foreach ($imageEntry in @(
        [pscustomobject]@{ service = "api"; expected = [string]$State.images.api },
        [pscustomobject]@{ service = "frontend"; expected = [string]$State.images.frontend },
        [pscustomobject]@{ service = "db"; expected = [string]$State.images.postgres })) {
        try {
            $container = Invoke-FbCompose $State $ComposeFile @("ps", "--all", "--quiet", $imageEntry.service) "Resolve $($imageEntry.service) container identity"
            $containerId = (($container.Output | Select-Object -Last 1) -join "").Trim()
            if ([string]::IsNullOrWhiteSpace($containerId)) { throw "container is absent" }
            $configured = Invoke-FbNative "docker" @("inspect", "--format", "{{.Config.Image}}", $containerId) "Read $($imageEntry.service) configured image" -IgnoreExitCode
            $actualImage = (($configured.Output | Select-Object -Last 1) -join "").Trim()
            $checks.Add([pscustomobject]@{ name = "image:$($imageEntry.service)"; passed = ($configured.ExitCode -eq 0 -and $actualImage -ceq $imageEntry.expected); detail = if ($actualImage) { $actualImage } else { "unavailable" } })
        } catch { $checks.Add([pscustomobject]@{ name = "image:$($imageEntry.service)"; passed = $false; detail = Protect-PrivateServerText $_.Exception.Message }) }
    }
    try {
        $running = @(Get-FbRunningServices $State $ComposeFile)
        if ($running -contains "db") {
            $migrationScript = 'export PGPASSWORD="$(cat /run/secrets/postgres_password)"; exec psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" --no-align --tuples-only --set=ON_ERROR_STOP=1 --command "SELECT coalesce(max(\"MigrationId\"), '''') FROM \"__EFMigrationsHistory\""'
            $migration = Invoke-FbCompose $State $ComposeFile @("exec", "-T", "db", "sh", "-ec", $migrationScript) "Read applied migration identity"
            $migrationId = (($migration.Output | ForEach-Object { $_.Trim() } | Where-Object { $_ } | Select-Object -Last 1) -join "")
            $checks.Add([pscustomobject]@{ name = "database-migration"; passed = (-not [string]::IsNullOrWhiteSpace($migrationId)); detail = if ($migrationId) { $migrationId } else { "no applied migration identity" } })
            $sizeScript = 'export PGPASSWORD="$(cat /run/secrets/postgres_password)"; exec psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" --no-align --tuples-only --set=ON_ERROR_STOP=1 --command "SELECT pg_database_size(current_database())"'
            $sizeResult = Invoke-FbCompose $State $ComposeFile @("exec", "-T", "db", "sh", "-ec", $sizeScript) "Read database size for complete-backup capacity"
            $sizeText = (($sizeResult.Output | ForEach-Object { $_.Trim() } | Where-Object { $_ -match '^\d+$' } | Select-Object -Last 1) -join "")
            [long]$databaseBytes = 0
            $sizeValid = [long]::TryParse($sizeText, [ref]$databaseBytes)
            $checks.Add([pscustomobject]@{
                name = "complete-backup-capacity"
                passed = ($sizeValid -and $databaseBytes -lt $script:MaximumCompleteBackupArchiveBytes)
                detail = if ($sizeValid) { "$([Math]::Round($databaseBytes / 1MB, 1)) MiB database size versus 1.9 GB complete-payload ceiling; actual payload is measured at backup" } else { "database size unavailable" }
            })
        } else {
            $checks.Add([pscustomobject]@{ name = "database-migration"; passed = $true; detail = "not queried because PostgreSQL is stopped" })
            $checks.Add([pscustomobject]@{ name = "complete-backup-capacity"; passed = $true; detail = "not queried because PostgreSQL is stopped; complete payload ceiling remains 1.9 GB" })
        }
    } catch { $checks.Add([pscustomobject]@{ name = "database-migration"; passed = $false; detail = Protect-PrivateServerText $_.Exception.Message }) }
    if ([bool](Get-FbProperty $State "tailscaleEnabled" $false)) {
        try {
            Assert-FbOwnedTailscaleServeRoute (Read-FbTailscaleServeJson) $State
            $checks.Add([pscustomobject]@{ name = "tailscale-route"; passed = $true; detail = "exact saved loopback route owned" })
        } catch { $checks.Add([pscustomobject]@{ name = "tailscale-route"; passed = $false; detail = Protect-PrivateServerText $_.Exception.Message }) }
    }
    $portAvailable = Test-FbTcpPortAvailable ([int]$State.port)
    $checks.Add([pscustomobject]@{ name = "loopback-port"; passed = $true; detail = if ($portAvailable) { "not listening" } else { "in use (expected while running)" } })
    foreach ($check in $checks) {
        $marker = if ($check.passed) { "PASS" } else { "FAIL" }
        Write-Host "$marker $($check.name): $($check.detail)"
    }
    if (@($checks | Where-Object { -not $_.passed }).Count -gt 0) { throw "One or more Private Server diagnostic checks failed." }
}

function ConvertTo-FbRedactedState {
    param($State)
    return [ordered]@{
        formatVersion = [int]$State.formatVersion
        status = [string]$State.status
        instanceId = [string]$State.instanceId
        composeProject = [string]$State.composeProject
        port = [int]$State.port
        localOrigin = [string]$State.localOrigin
        publicOrigin = if ([string]$State.publicOrigin -eq [string]$State.localOrigin) { [string]$State.publicOrigin } else { "https://[TAILNET-HOST-REDACTED]" }
        tenantName = "[REDACTED]"
        tenantSlug = "[REDACTED]"
        ownerEmail = "[REDACTED]"
        ownerName = "[REDACTED]"
        releaseVersion = [string]$State.releaseVersion
        releaseCommitSha = [string]$State.releaseCommitSha
        reviewedRelease = [bool]$State.reviewedRelease
        releaseIntegrityStatus = [string](Get-FbProperty $State "releaseIntegrityStatus" "unknown")
        tailscaleEnabled = [bool]$State.tailscaleEnabled
        createdAtUtc = [string]$State.createdAtUtc
        updatedAtUtc = [string]$State.updatedAtUtc
    }
}

function Invoke-FbSupportBundle {
    param($State, [string]$ComposeFile, [string]$OutputDirectory, [int]$TailLines)
    $output = Resolve-FbManagedOutputDirectory $OutputDirectory $State "Support" "FilingBridge Support"
    Initialize-FbManagedOutputDirectory $output $State "Support"
    $staging = Join-Path ([IO.Path]::GetTempPath()) ("filingbridge-support-" + [Guid]::NewGuid().ToString("N"))
    try {
        New-Item -ItemType Directory -Path $staging | Out-Null
        Set-FbRestrictedAcl $staging
        Write-FbJsonAtomic (Join-Path $staging "instance.redacted.json") (ConvertTo-FbRedactedState $State)
        $versions = Invoke-FbNative "docker" @("version") "Read Docker version for support bundle" -IgnoreExitCode
        Write-FbTextAtomic (Join-Path $staging "docker-version.txt") (Protect-FbSupportText (($versions.Output -join [Environment]::NewLine) + [Environment]::NewLine))
        $composeVersion = Invoke-FbNative "docker" @("compose", "version") "Read Compose version for support bundle" -IgnoreExitCode
        Write-FbTextAtomic (Join-Path $staging "compose-version.txt") (Protect-FbSupportText (($composeVersion.Output -join [Environment]::NewLine) + [Environment]::NewLine))
        $status = Invoke-FbCompose $State $ComposeFile @("ps", "--all") "Read service state for support bundle" -IgnoreExitCode
        Write-FbTextAtomic (Join-Path $staging "services.txt") (Protect-FbSupportText (($status.Output -join [Environment]::NewLine) + [Environment]::NewLine))
        $logs = Invoke-FbCompose $State $ComposeFile @("logs", "--no-color", "--tail", [string]$TailLines, "api", "frontend") "Read bounded logs for support bundle" -IgnoreExitCode
        Write-FbTextAtomic (Join-Path $staging "application.redacted.log") (Protect-FbSupportText ((@($logs.Output | Select-Object -Last $TailLines) -join [Environment]::NewLine) + [Environment]::NewLine))
        $readme = @"
FilingBridge Private Server redacted support bundle
Created: $((Get-Date).ToUniversalTime().ToString("o"))

This bundle intentionally excludes database dumps, secret files, environment files,
and full unbounded logs. Bounded logs receive best-effort credential, email, IP and
path redaction but can still contain client/accounting metadata in arbitrary messages.
Treat the bundle as sensitive and review every file before sharing it.
"@
        Write-FbTextAtomic (Join-Path $staging "README.txt") $readme
        $leaf = "filingbridge-support-$((Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss')).zip"
        $partial = Join-Path $output (".$leaf.partial.zip")
        $final = Join-Path $output $leaf
        Compress-Archive -Path (Join-Path $staging "*") -DestinationPath $partial -CompressionLevel Optimal
        Move-Item -LiteralPath $partial -Destination $final
        Set-FbRestrictedAcl $final
        Write-Host "Redacted, bounded support bundle: $final"
        Write-Host "Review every file before sharing it."
    } finally { Remove-FbTemporaryDirectory $staging }
}

function Invoke-FbUninstall {
    param($State, [string]$ComposeFile, [switch]$DryRun)
    if ([bool](Get-FbProperty $State "tailscaleEnabled" $false)) {
        throw "Disable the recorded Tailscale Serve route before uninstalling: FilingBridge.cmd tailscale disable"
    }
    $null = Invoke-FbCompose $State $ComposeFile @("down", "--remove-orphans", "--timeout", "60") "Remove Private Server containers and networks while retaining data" -Mutating -DryRun:$DryRun
    if (-not $DryRun) { $State.status = "uninstalled"; Save-FbState $State }
    Write-Host "Runtime containers were removed. The PostgreSQL volume and private state were retained."
}

function Invoke-FbPurgeData {
    param($State, [string]$ComposeFile, [string]$Confirmation, [switch]$NonInteractive, [switch]$DryRun)
    if ([bool](Get-FbProperty $State "tailscaleEnabled" $false)) {
        throw "Disable the recorded Tailscale Serve route before purge: FilingBridge.cmd tailscale disable"
    }
    $expected = "PURGE $($State.instanceId)"
    Confirm-FbTypedAction $expected $Confirmation "This permanently deletes this installation's containers, PostgreSQL volume, configuration, and keys." -NonInteractive:$NonInteractive
    $stateDirectory = [IO.Path]::GetFullPath([string]$State.stateDirectory)
    $stateFile = Join-Path $stateDirectory $script:StateFileName
    if ([string]::IsNullOrWhiteSpace($stateDirectory) -or $stateDirectory -eq [IO.Path]::GetPathRoot($stateDirectory) -or -not (Test-Path -LiteralPath $stateFile -PathType Leaf)) {
        throw "Refusing to purge an unverified state directory."
    }
    $null = Invoke-FbCompose $State $ComposeFile @("down", "--volumes", "--remove-orphans", "--timeout", "60") "Permanently delete this Private Server Docker project and volume" -Mutating -DryRun:$DryRun
    if ($DryRun) { Write-Host "DRY RUN: private state directory would be removed only after the exact typed confirmation."; return }
    $reloaded = (Read-FbUtf8Text $stateFile) | ConvertFrom-Json
    if ([string]$reloaded.instanceId -ne [string]$State.instanceId) { throw "State identity changed during purge; directory removal refused." }
    Remove-Item -LiteralPath $stateDirectory -Recurse -Force
    Write-Host "Private Server data and state were permanently purged."
}

function Get-PrivateServerHelp {
    [CmdletBinding()]
    param()
    return @'
FilingBridge Private Server (Windows x64)

Usage:
  FilingBridge.cmd setup -TenantName <name> -OwnerEmail <email> -OwnerName <name> [-ReleaseManifest <release.json>]
  FilingBridge.cmd start | status | stop | logs
  FilingBridge.cmd backup [-OutputDirectory <dir>] [-BackupRecipient <age-recipient>]
  FilingBridge.cmd backup -PlaintextDatabaseOnly [-OutputDirectory <dir>]
  FilingBridge.cmd verify-backup -BackupPath <path> [-AgeIdentityFile <identity>]
  FilingBridge.cmd restore -BackupPath <path> [-AgeIdentityFile <identity>]
  FilingBridge.cmd export-recovery-key -OutputDirectory <separate-offline-directory>
  FilingBridge.cmd recover-host -BackupPath <path> -AgeIdentityFile <identity> -RecoveryAuthenticationKeyFile <key>
  FilingBridge.cmd reboot-check prepare | verify | status
  FilingBridge.cmd local-check [-OutputDirectory <evidence-directory>]
  FilingBridge.cmd update -ReleaseManifest <release.json> [-BackupRecipient <recipient>] [-AgeIdentityFile <identity>]
  FilingBridge.cmd owner-recovery [-OwnerEmail <email>]
  FilingBridge.cmd tailscale enable | disable | status
  FilingBridge.cmd diagnose | support-bundle | uninstall | purge-data
  FilingBridge.cmd <command> -DryRun

Safety:
  setup never overwrites existing state; start never builds, migrates, or seeds; stop
  and uninstall preserve data; purge-data and restore require an exact installation-
  specific confirmation. Complete portable backups use age encryption. Explicit
  plaintext backups contain only PostgreSQL and are not sufficient for host loss.
  Replacement-host recovery requires a complete encrypted backup, its separate age
  identity, and an exported recovery authentication key. Keep all three separately.

Private Server does not submit to CRO/ROS and does not replace qualified-accountant,
source-law, external-validation, or public-production acceptance gates.
'@
}

function Invoke-FilingBridgePrivateServer {
    [CmdletBinding()]
    param(
        [string]$Command = "help",
        [string]$Action,
        [string]$StateDirectory,
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [string]$ReleaseManifest,
        [string]$TenantName,
        [string]$TenantSlug,
        [string]$OwnerEmail,
        [string]$OwnerName,
        [string]$PublicOrigin,
        [int]$Port = 3500,
        [string]$OutputDirectory,
        [string]$BackupPath,
        [string]$BackupRecipient,
        [string]$AgeIdentityFile,
        [string]$RecoveryAuthenticationKeyFile,
        [string]$Confirmation,
        [int]$TailLines = 250,
        [switch]$DryRun,
        [switch]$NonInteractive,
        [switch]$BuildLocal,
        [switch]$PlaintextDatabaseOnly,
        [switch]$AllowPlaintextDatabaseOnlyRestore,
        [switch]$SkipPrerequisiteChecks
    )
    $normalized = $Command.Trim().ToLowerInvariant()
    if ($normalized -eq "dry-run") {
        if ([string]::IsNullOrWhiteSpace($Action)) { throw "dry-run requires a command, for example: dry-run start" }
        $normalized = $Action.Trim().ToLowerInvariant()
        $Action = ""
        $DryRun = $true
    }
    if ($normalized -in @("help", "-h", "--help", "/?")) { Write-Host (Get-PrivateServerHelp); return }
    $supported = @("setup", "start", "status", "stop", "logs", "backup", "verify-backup", "restore", "export-recovery-key", "recover-host", "reboot-check", "local-check", "update", "owner-recovery", "tailscale", "diagnose", "support-bundle", "uninstall", "purge-data")
    if ($normalized -notin $supported) { throw "Unknown command '$Command'. Run 'FilingBridge.cmd help' for supported commands." }
    $installationLock = Enter-FbInstallationLock $StateDirectory
    try {
        $RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
        if ($normalized -eq "setup") {
            Invoke-FbSetup $StateDirectory $RepositoryRoot $ReleaseManifest $TenantName $TenantSlug $OwnerEmail $OwnerName $PublicOrigin $Port -DryRun:$DryRun -NonInteractive:$NonInteractive -BuildLocal:$BuildLocal -SkipPrerequisiteChecks:$SkipPrerequisiteChecks
            return
        }
        if ($normalized -eq "recover-host") {
            Invoke-FbRecoverHost $StateDirectory $RepositoryRoot $ReleaseManifest $BackupPath $AgeIdentityFile $RecoveryAuthenticationKeyFile $Confirmation $Port -BuildLocal:$BuildLocal -NonInteractive:$NonInteractive -DryRun:$DryRun -SkipPrerequisiteChecks:$SkipPrerequisiteChecks
            return
        }
        $allowNonReady = $normalized -in @("status", "stop", "logs", "diagnose", "support-bundle", "uninstall", "purge-data", "update", "owner-recovery", "restore")
        $state = Read-FbState $StateDirectory -AllowNonReady:$allowNonReady
        if ($normalized -eq "update" -and [string]$state.status -notin @("ready", "uninstalled")) {
            throw "Update is supported only for a ready or deliberately uninstalled Private Server, not state '$($state.status)'."
        }
        if ($normalized -eq "owner-recovery" -and [string]$state.status -notin @("ready", "setupFailed")) {
            throw "Owner recovery is supported only for a ready Private Server or a setup whose initializer may already have committed, not state '$($state.status)'."
        }
        if ($normalized -eq "restore" -and [string]$state.status -notin @("ready", "updateFailed")) {
            throw "Restore is supported only for a ready Private Server or explicit recovery from updateFailed, not state '$($state.status)'."
        }
        $sourceComposeFile = Get-FbComposeFile $RepositoryRoot
        $installedComposeFile = [string]$state.installedComposeFile
        Assert-FbComposeMatchesState $state $installedComposeFile
        if ($normalized -ne "update") {
            # The caller must still use the matching installed release directory, but
            # runtime commands execute only the ACL-restricted snapshot in state.
            Assert-FbComposeMatchesState $state $sourceComposeFile
        }
        $composeFile = if ($normalized -eq "update") { $sourceComposeFile } else { $installedComposeFile }
        switch ($normalized) {
            "start" { Invoke-FbStart $state $composeFile -DryRun:$DryRun }
            "status" { Invoke-FbStatus $state $composeFile }
            "stop" { Invoke-FbStop $state $composeFile -DryRun:$DryRun }
            "logs" { Invoke-FbLogs $state $composeFile $TailLines }
            "backup" { $null = Invoke-FbBackup $state $composeFile $OutputDirectory $BackupRecipient -PlaintextDatabaseOnly:$PlaintextDatabaseOnly -DryRun:$DryRun }
            "verify-backup" { Invoke-FbVerifyBackupCommand $state $composeFile $BackupPath $AgeIdentityFile -AllowPlaintextDatabaseOnlyRestore:$AllowPlaintextDatabaseOnlyRestore }
            "restore" { Invoke-FbRestore $state $composeFile $BackupPath $AgeIdentityFile $Confirmation -NonInteractive:$NonInteractive -AllowPlaintextDatabaseOnlyRestore:$AllowPlaintextDatabaseOnlyRestore -DryRun:$DryRun }
            "export-recovery-key" { Invoke-FbExportRecoveryKey $state $OutputDirectory $Confirmation -NonInteractive:$NonInteractive -DryRun:$DryRun }
            "reboot-check" { Invoke-FbRebootCheck $state $composeFile $Action $OutputDirectory -DryRun:$DryRun }
            "local-check" { Invoke-FbLocalCheck $state $composeFile $OutputDirectory -DryRun:$DryRun }
            "update" { Invoke-FbUpdate $state $composeFile $RepositoryRoot $ReleaseManifest $OutputDirectory $BackupRecipient $AgeIdentityFile -PlaintextDatabaseOnly:$PlaintextDatabaseOnly -BuildLocal:$BuildLocal -DryRun:$DryRun }
            "owner-recovery" { Invoke-FbOwnerRecovery $state $composeFile $OwnerEmail $Confirmation -NonInteractive:$NonInteractive -DryRun:$DryRun }
            "tailscale" { Invoke-FbTailscale $state $composeFile $Action -DryRun:$DryRun }
            "diagnose" { Invoke-FbDiagnose $state $composeFile $RepositoryRoot }
            "support-bundle" { Invoke-FbSupportBundle $state $composeFile $OutputDirectory $TailLines }
            "uninstall" { Invoke-FbUninstall $state $composeFile -DryRun:$DryRun }
            "purge-data" { Invoke-FbPurgeData $state $composeFile $Confirmation -NonInteractive:$NonInteractive -DryRun:$DryRun }
        }
    } finally {
        Exit-FbInstallationLock $installationLock
    }
}

Export-ModuleMember -Function @(
    "Invoke-FilingBridgePrivateServer",
    "Get-PrivateServerHelp",
    "Protect-PrivateServerText",
    "New-PrivateServerRandomSecret",
    "New-PrivateServerOwnerPassword",
    "Set-PrivateServerCommandInvoker",
    "Reset-PrivateServerCommandInvoker"
)
