[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$modulePath = Join-Path $PSScriptRoot "PrivateServer.psm1"
$dispatcherPath = Join-Path $repositoryRoot "scripts\private-server.ps1"
$launcherPath = Join-Path $repositoryRoot "FilingBridge.cmd"

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "ASSERTION FAILED: $Message" }
}

function Assert-Throws([scriptblock]$Action, [string]$Pattern, [string]$Message) {
    try {
        & $Action
    } catch {
        if ($_.Exception.Message -notmatch $Pattern) {
            throw "ASSERTION FAILED: $Message (unexpected error: $($_.Exception.Message))"
        }
        return
    }
    throw "ASSERTION FAILED: $Message (no error was thrown)"
}

foreach ($path in @($modulePath, $dispatcherPath)) {
    $tokens = $null
    $errors = $null
    [Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors) | Out-Null
    Assert-True ($errors.Count -eq 0) "PowerShell parser must accept $path"
}
Assert-True (Test-Path -LiteralPath $launcherPath -PathType Leaf) "FilingBridge.cmd must exist"
$testUtf8 = New-Object Text.UTF8Encoding($false, $true)
$launcher = [IO.File]::ReadAllText($launcherPath, $testUtf8)
Assert-True ($launcher -match 'private-server\.ps1') "CMD launcher must delegate to the supported PowerShell dispatcher"
Assert-True ($launcher -match 'NoProfile') "CMD launcher must avoid ambient PowerShell profiles"
$moduleSource = [IO.File]::ReadAllText($modulePath, $testUtf8)
Assert-True ($moduleSource -match 'Wait-FbUriHealth\s+-Uri\s+"\$origin/health/ready"') "Tailscale enable must probe the real external HTTPS readiness path"
Assert-True ($moduleSource -match 'Roll back the failed FilingBridge Tailscale Serve route') "failed external Tailscale verification must roll back Serve"
Assert-True ($moduleSource -notmatch '(?im)tailscale[^\r\n]*\bfunnel\b') "operator source must never invoke Tailscale Funnel"
Assert-True ($moduleSource -match 'WSL 2\.1\.5 or newer') "prerequisites must enforce the current minimum WSL version"
Assert-True ($moduleSource -match 'VirtualizationFirmwareEnabled') "prerequisites must inspect firmware virtualization support"
Assert-True ($moduleSource -match 'label=ie\.filingbridge\.deployment-mode=PrivateServer') "prerequisites must detect conflicting Private Server Docker resources"
Assert-True ($moduleSource -match 'state-disk-free') "diagnostics must retain an operational free-space check"
Assert-True ($moduleSource -match '"filingbridge-switch", \$PreservedDatabase\) "Preserve the current database') "restore must preserve the active database under the generated preservation name"
Assert-True ($moduleSource -match 'restoreRecoveryRequired') "an incomplete restore rollback must leave a durable blocked state"
Assert-True ($moduleSource -notmatch 'Return to the preserved pre-restore database" -Mutating -IgnoreExitCode') "restore must not ignore a failed database rollback"
Assert-True ($moduleSource -notmatch 'db:\$containerPath|/tmp/filingbridge-(?:backup|verify|restore)') "backup verification and restore must not stage dumps in the database tmpfs"
Assert-True ($moduleSource -match '(?s)function Restore-FbCandidateDatabase\s*\{.*?\$containerUserArguments\s*=\s*@\(Get-FbOperatorContainerUserArguments\).*?Invoke-FbCompose.*?\+\s*\$containerUserArguments\s*\+') "candidate restore must run with the Linux operator UID/GID that owns the host-mounted dump"
Assert-True ($moduleSource -match 'backup_authentication_key') "backup restore must require a dedicated installation authentication key"
Assert-True ($moduleSource -match 'Enter-FbInstallationLock') "every lifecycle command must take the installation mutex"

Import-Module $modulePath -Force
$privateServerModule = Get-Module PrivateServer
if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
    $nativeProgress = & $privateServerModule {
        Invoke-FbNative -FilePath $env:ComSpec -Arguments @("/d", "/c", "echo fb-benign-native-progress 1>&2 & exit /b 0") -Description "Capture benign native progress"
    }
    Assert-True ($nativeProgress.ExitCode -eq 0) "native stderr with exit code zero must remain successful under Windows PowerShell 5.1"
    Assert-True (($nativeProgress.Output -join "`n") -match 'fb-benign-native-progress') "successful native stderr must remain available to diagnostics"

    $nativeFailure = $null
    try {
        & $privateServerModule {
            Invoke-FbNative -FilePath $env:ComSpec -Arguments @("/d", "/c", "echo password=fb-native-secret 1>&2 & exit /b 7") -Description "Reject failed native command"
        }
    } catch {
        $nativeFailure = $_.Exception.Message
    }
    Assert-True (-not [string]::IsNullOrWhiteSpace($nativeFailure)) "nonzero native exit code must still throw"
    Assert-True ($nativeFailure -match 'exit code 7') "native failure must report its real exit code"
    Assert-True ($nativeFailure -notmatch 'fb-native-secret') "native failure output must remain sanitised"

    $argumentProbe = Join-Path ([IO.Path]::GetTempPath()) ("filingbridge-native-argv-" + [Guid]::NewGuid().ToString("N") + ".ps1")
    try {
        [IO.File]::WriteAllText(
            $argumentProbe,
            'param([string]$First,[string]$Second); [Console]::Out.Write($First + [char]31 + $Second)',
            [Text.UTF8Encoding]::new($false))
        $firstArgument = 'value with spaces'
        $secondArgument = 'embedded"quote\and trailing\'
        $argumentResult = & $privateServerModule {
            param($probe, $first, $second)
            Invoke-FbNative -FilePath "powershell.exe" -Arguments @("-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", $probe, $first, $second) -Description "Preserve exact native argv"
        } $argumentProbe $firstArgument $secondArgument
        Assert-True (($argumentResult.Output -join "`n") -ceq ($firstArgument + [char]31 + $secondArgument)) "Windows native invocation must preserve spaces, embedded quotes, and trailing backslashes in exact argv"
    } finally {
        if (Test-Path -LiteralPath $argumentProbe) { Remove-Item -LiteralPath $argumentProbe -Force }
    }
}
Assert-True ((& $privateServerModule { Compare-FbSemanticVersion "1.0.0" "1.0.0-preview.9" }) -eq 1) "stable semantic release must follow its prerelease"
Assert-True ((& $privateServerModule { Compare-FbSemanticVersion "1.0.0-preview.10" "1.0.0-preview.2" }) -eq 1) "numeric prerelease identifiers must compare numerically"
Assert-Throws { & $privateServerModule { Compare-FbSemanticVersion "01.0.0" "1.0.0" } } 'not a supported semantic version' "leading-zero semantic versions must be rejected"
foreach ($malformedEmptyStatus in @('true', '[1]', '{"Services":1}', '{"Foreground":true}', '{"AllowFunnel":[]}')) {
    Assert-True (-not (& $privateServerModule { param($json) Test-FbEmptyTailscaleServeStatus ($json | ConvertFrom-Json) } $malformedEmptyStatus)) "scalar/array Serve semantics must never be treated as empty"
}
$ownedStateFixture = [pscustomobject]@{ port = 3500; tailscaleDnsName = "machine.example-tailnet.ts.net" }
Assert-Throws {
    & $privateServerModule {
        param($state)
        Assert-FbOwnedTailscaleServeRoute ('{"TCP":{"443":{"HTTPS":true}},"Web":{"machine.example-tailnet.ts.net:443":{"Handlers":{"/":{"Proxy":"http://127.0.0.1:3500"}}}},"AllowFunnel":true}' | ConvertFrom-Json) $state
    } $ownedStateFixture
} 'ownership cannot be proven' "scalar true AllowFunnel must fail closed"
foreach ($httpsLiteral in @('1', '"true"')) {
    Assert-Throws {
        & $privateServerModule {
            param($state, $literal)
            $json = '{"TCP":{"443":{"HTTPS":' + $literal + '}},"Web":{"machine.example-tailnet.ts.net:443":{"Handlers":{"/":{"Proxy":"http://127.0.0.1:3500"}}}}}'
            Assert-FbOwnedTailscaleServeRoute ($json | ConvertFrom-Json) $state
        } $ownedStateFixture $httpsLiteral
    } 'ownership cannot be proven' "non-boolean HTTPS configuration must fail closed"
}

$secrets = @(1..32 | ForEach-Object { New-PrivateServerRandomSecret })
Assert-True (@($secrets | Select-Object -Unique).Count -eq 32) "generated secrets must be unique"
foreach ($secret in $secrets) {
    $decoded = [Convert]::FromBase64String($secret)
    Assert-True ($decoded.Length -ge 32) "generated cryptographic secrets must be standard Base64 with >=32 decoded bytes"
}
$password = New-PrivateServerOwnerPassword
Assert-True ($password.Length -ge 24) "Owner password must be long"
Assert-True ($password -cmatch '[A-Z]') "Owner password must contain uppercase"
Assert-True ($password -cmatch '[a-z]') "Owner password must contain lowercase"
Assert-True ($password -match '[0-9]') "Owner password must contain a number"
Assert-True ($password -match '[!@#%*_=+\-]') "Owner password must contain a symbol"

$redacted = Protect-PrivateServerText 'password=example token: abc Authorization: Bearer opaque ?token=queryvalue'
Assert-True ($redacted -notmatch 'example|abc|opaque|queryvalue') "redactor must remove common credential forms"
$help = Get-PrivateServerHelp
foreach ($command in @("setup", "start", "status", "stop", "logs", "backup", "verify-backup", "restore", "export-recovery-key", "recover-host", "reboot-check", "local-check", "update", "owner-recovery", "tailscale", "connector enable [--origin local|tailscale] | disable | status", "mfa require | off | status", "diagnose", "support-bundle", "uninstall", "purge-data", "DryRun")) {
    Assert-True ($help -match [regex]::Escape($command)) "help must document $command"
}
Assert-True ($help -match 'does not submit to CRO/ROS') "help must preserve the no-direct-filing boundary"
Assert-True ($help -match 'qualified-accountant') "help must preserve the professional-review boundary"
Assert-True ($help -match 'Claude\.ai custom connectors need public ingress') "help must state that Claude.ai is not supported on a Private Server"
Assert-True ($moduleSource -notmatch '(?im)connector[^\r\n]*\bfunnel\b') "connector commands must never involve Tailscale Funnel"

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("filingbridge-operator-test-" + [Guid]::NewGuid().ToString("N"))
$stateDirectory = Join-Path $testRoot ("state path with spaces " + [char]0x00E1)
$global:FbOperatorCalls = New-Object System.Collections.Generic.List[object]
$global:FbFakeServices = @("db", "api", "frontend", "mcp")
$global:FbFakeContainerIds = @{
    db = ("d" * 64)
    api = ("a" * 64)
    frontend = ("f" * 64)
    mcp = ("e" * 64)
}
$global:FbFakeState = $null
$global:FbExpectedOwnershipProject = ""
$global:FbExpectedOwnershipInstance = ""
$global:FbFakeRebootStartMinutes = 5
$global:FbFakeTailscaleStatus = '{"version":"etag-empty"}'
$global:FbMutateReleaseComposeOnValidate = ""
$global:FbFailDescriptionPattern = ""
$global:FbExpectedFrontendPort = 3500
$global:FbFakePublishedService = ""
$fakeInvoker = {
    param($FilePath, $Arguments, $Description, $Mutating)
    $argumentStrings = @($Arguments | ForEach-Object { [string]$_ })
    $global:FbOperatorCalls.Add([pscustomobject]@{ file = $FilePath; arguments = $argumentStrings; description = $Description; mutating = $Mutating })
    if (-not [string]::IsNullOrWhiteSpace($global:FbFailDescriptionPattern) -and $Description -match $global:FbFailDescriptionPattern) {
        return [pscustomobject]@{ ExitCode = 17; Output = @("synthetic controlled failure") }
    }
    if ($FilePath -eq "id") {
        if ($Description -eq "Resolve the Linux operator user ID" -and $argumentStrings.Count -eq 1 -and $argumentStrings[0] -eq "-u") {
            return [pscustomobject]@{ ExitCode = 0; Output = @("1000") }
        }
        if ($Description -eq "Resolve the Linux operator group ID" -and $argumentStrings.Count -eq 1 -and $argumentStrings[0] -eq "-g") {
            return [pscustomobject]@{ ExitCode = 0; Output = @("1000") }
        }
        $nativeOutput = @(& $FilePath @argumentStrings 2>&1)
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $nativeOutput }
    }
    if ($FilePath -in @("chmod", "stat")) {
        $nativeOutput = @(& $FilePath @argumentStrings 2>&1)
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $nativeOutput }
    }
    if ($Description -in @("Validate the isolated Private Server topology", "Pull exact update image digests") -and -not [string]::IsNullOrWhiteSpace($global:FbMutateReleaseComposeOnValidate)) {
        [IO.File]::AppendAllText($global:FbMutateReleaseComposeOnValidate, "`n# synthetic post-verification source mutation", [Text.UTF8Encoding]::new($false))
        $global:FbMutateReleaseComposeOnValidate = ""
    }
    if ($FilePath -eq "docker" -and $argumentStrings -contains "compose") {
        if ($argumentStrings -contains "down") { $global:FbFakeServices = @() }
        elseif ($argumentStrings -contains "stop") {
            $stopped = @($argumentStrings | Where-Object { $_ -in @("db", "api", "frontend", "mcp") })
            $global:FbFakeServices = @($global:FbFakeServices | Where-Object { $_ -notin $stopped })
        }
        elseif ($argumentStrings -contains "start" -or $argumentStrings -contains "up") {
            $started = @($argumentStrings | Where-Object { $_ -in @("db", "api", "frontend", "mcp") })
            $global:FbFakeServices = @($global:FbFakeServices + $started | Select-Object -Unique)
        }
    }
    if ($FilePath -eq "docker" -and $argumentStrings.Count -eq 2 -and $argumentStrings[0] -eq "start") {
        $service = @($global:FbFakeContainerIds.Keys | Where-Object { $global:FbFakeContainerIds[$_] -eq $argumentStrings[1] })
        if ($service.Count -eq 1) {
            $global:FbFakeServices = @($global:FbFakeServices + $service[0] | Select-Object -Unique)
            return [pscustomobject]@{ ExitCode = 0; Output = @($argumentStrings[1]) }
        }
        return [pscustomobject]@{ ExitCode = 1; Output = @("unknown synthetic container") }
    }
    if ($Description -match '^Resolve existing (db|api|frontend|mcp) runtime container$') {
        $projectIndex = [Array]::IndexOf($argumentStrings, "--project-name")
        $environmentIndex = [Array]::IndexOf($argumentStrings, "--env-file")
        $global:FbExpectedOwnershipProject = [string]$argumentStrings[$projectIndex + 1]
        $environmentPath = [string]$argumentStrings[$environmentIndex + 1]
        $ownershipState = ([IO.File]::ReadAllText((Join-Path (Split-Path -Parent $environmentPath) "server.json"), [Text.UTF8Encoding]::new($false, $true))) | ConvertFrom-Json
        $global:FbExpectedOwnershipInstance = [string]$ownershipState.instanceId
        return [pscustomobject]@{ ExitCode = 0; Output = @($global:FbFakeContainerIds[$Matches[1]]) }
    }
    if ($Description -match '^Verify existing (db|api|frontend|mcp) runtime container ownership$') {
        $service = $Matches[1]
        return [pscustomobject]@{
            ExitCode = 0
            Output = @("$global:FbExpectedOwnershipProject|$service|PrivateServer|$global:FbExpectedOwnershipInstance")
        }
    }
    if ($Description -match '^Read (db|api|frontend|mcp) reboot runtime evidence$') {
        $service = $Matches[1]
        $boot = [DateTimeOffset]::Parse([string]$env:FILINGBRIDGE_TEST_BOOT_ID, [Globalization.CultureInfo]::InvariantCulture)
        return [pscustomobject]@{
            ExitCode = 0
            Output = @("$($global:FbFakeContainerIds[$service])|$($boot.AddMinutes($global:FbFakeRebootStartMinutes).ToUniversalTime().ToString('o'))")
        }
    }
    if ($Description -eq 'Inspect Private Server containers') {
        return [pscustomobject]@{ ExitCode = 0; Output = @("db", "api", "frontend") }
    }
    if ($Description -eq 'Record current Private Server service state') {
        return [pscustomobject]@{ ExitCode = 0; Output = $global:FbFakeServices }
    }
    if ($Description -eq 'Inspect frontend published port') {
        return [pscustomobject]@{ ExitCode = 0; Output = @("127.0.0.1:$global:FbExpectedFrontendPort") }
    }
    if ($Description -match '^Inspect (api|db|mcp) host port bindings$') {
        $service = $Matches[1]
        $containerPort = switch ($service) { "api" { "8080/tcp" } "mcp" { "3100/tcp" } default { "5432/tcp" } }
        $value = if ($global:FbFakePublishedService -ceq $service) {
            '{"' + $containerPort + '":[{"HostIp":"0.0.0.0","HostPort":"' + $(switch ($service) { "api" { "18080" } "mcp" { "13100" } default { "15432" } }) + '"}]}'
        } else { '{"' + $containerPort + '":null}' }
        return [pscustomobject]@{ ExitCode = 0; Output = @($value) }
    }
    if ($Description -eq "Create a PostgreSQL custom-format dump directly in private host staging") {
        $volumeIndex = [Array]::IndexOf($argumentStrings, "--volume")
        $volume = [string]$argumentStrings[$volumeIndex + 1]
        $hostDirectory = [regex]::Match($volume, '^(?<host>.+):/backup:rw$').Groups['host'].Value
        $hostPath = Join-Path $hostDirectory ([IO.Path]::GetFileName([string]$argumentStrings[-1]))
        [IO.File]::WriteAllBytes($hostPath, [Text.Encoding]::UTF8.GetBytes("fake-custom-format-dump"))
        return [pscustomobject]@{ ExitCode = 0; Output = @() }
    }
    if ($Description -eq "Restore the host-mounted dump into a disposable verification database") {
        return [pscustomobject]@{ ExitCode = 0; Output = @("42") }
    }
    if ($Description -eq "Verify EF migration history in the disposable database") {
        return [pscustomobject]@{ ExitCode = 0; Output = @("12") }
    }
    if ($Description -match '^Read important-table fingerprints') {
        return [pscustomobject]@{ ExitCode = 0; Output = @('{"tables":[{"table":"accounting_periods","rowCount":2,"fingerprint":"11111111111111111111111111111111"},{"table":"audit_logs","rowCount":3,"fingerprint":"22222222222222222222222222222222"},{"table":"companies","rowCount":1,"fingerprint":"33333333333333333333333333333333"},{"table":"tenants","rowCount":1,"fingerprint":"44444444444444444444444444444444"},{"table":"user_accounts","rowCount":1,"fingerprint":"55555555555555555555555555555555"}]}') }
    }
    if ($Description -eq "Run physical-host Owner recovery") {
        return [pscustomobject]@{ ExitCode = 0; Output = @('{"ownerUserId":1,"ownerEmail":"owner@example.ie","resetToken":"opaque/test+token=","expiresAtUtc":"2099-01-01T00:00:00Z"}') }
    }
    if ($FilePath -eq "age") {
        $outputIndex = [Array]::IndexOf($argumentStrings, "--output")
        if ($outputIndex -lt 0 -or $outputIndex + 1 -ge $argumentStrings.Count) {
            return [pscustomobject]@{ ExitCode = 2; Output = @("missing output") }
        }
        $outputPath = $argumentStrings[$outputIndex + 1]
        $inputPath = $argumentStrings[-1]
        Copy-Item -LiteralPath $inputPath -Destination $outputPath -Force
        return [pscustomobject]@{ ExitCode = 0; Output = @() }
    }
    if ($FilePath -eq "tailscale" -and $argumentStrings.Count -ge 2 -and $argumentStrings[0] -eq "serve" -and $argumentStrings -contains "--json") {
        return [pscustomobject]@{ ExitCode = 0; Output = @($global:FbFakeTailscaleStatus) }
    }
    if ($FilePath -eq "tailscale" -and $argumentStrings.Count -ge 2 -and $argumentStrings[0] -eq "serve" -and $argumentStrings -contains "--bg") {
        $target = [string]$argumentStrings[-1]
        $global:FbFakeTailscaleStatus = '{"version":"etag-owned","TCP":{"443":{"HTTPS":true}},"Web":{"machine.example-tailnet.ts.net:443":{"Handlers":{"/":{"Proxy":"' + $target + '"}}}},"AllowFunnel":{"machine.example-tailnet.ts.net:443":false}}'
        return [pscustomobject]@{ ExitCode = 0; Output = @() }
    }
    if ($FilePath -eq "tailscale" -and $argumentStrings.Count -ge 2 -and $argumentStrings[0] -eq "serve" -and $argumentStrings -contains "off") {
        $global:FbFakeTailscaleStatus = '{"version":"etag-empty"}'
        return [pscustomobject]@{ ExitCode = 0; Output = @() }
    }
    if ($FilePath -eq "tailscale" -and $argumentStrings -contains "--json") {
        return [pscustomobject]@{ ExitCode = 0; Output = @('{"Self":{"DNSName":"machine.example-tailnet.ts.net."}}') }
    }
    return [pscustomobject]@{ ExitCode = 0; Output = @() }
}

try {
    New-Item -ItemType Directory -Path $testRoot | Out-Null
    Set-PrivateServerCommandInvoker $fakeInvoker

    $linuxContainerUserArguments = @(& $privateServerModule {
        Get-FbOperatorContainerUserArguments -IsLinux $true
    })
    Assert-True (($linuxContainerUserArguments -join "|") -ceq "--user|1000:1000") "Linux host-mounted database helpers must run with the operator numeric UID/GID"
    $windowsContainerUserArguments = @(& $privateServerModule {
        Get-FbOperatorContainerUserArguments -IsLinux $false
    })
    Assert-True ($windowsContainerUserArguments.Count -eq 0) "Windows database helpers must preserve the Compose service user"
    $global:FbOperatorCalls.Clear()

    $releaseRoot = Join-Path $testRoot "synthetic-release"
    New-Item -ItemType Directory -Path $releaseRoot | Out-Null
    $requiredReleaseFiles = @(
        "FilingBridge.cmd", "filingbridge", "compose.private.yml", ".env.private.example",
        "scripts/private-server.ps1", "scripts/PrivateServer/PrivateServer.psm1",
        "scripts/smoke-production.ps1", "scripts/verify-linux-private-host.sh",
        "Docs/deployment/README.md", "Docs/deployment/private-server.md", "Docs/deployment/private-server-linux.md",
        "Docs/deployment/GOOGLE_CLOUD_PRIVATE_SERVER.md", "Docs/deployment/LOCAL_WINDOWS_READINESS.md", "Docs/deployment/LINUX_CLOUD_READINESS.md",
        "deploy/private/release-manifest.schema.json", "README.md", "LICENSE", "NOTICE",
        "THIRD_PARTY_NOTICES.md", "CONTRIBUTORS.md")
    foreach ($relative in $requiredReleaseFiles) {
        $source = Join-Path $repositoryRoot $relative
        $destination = Join-Path $releaseRoot $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Assert-True (Test-Path -LiteralPath $source -PathType Leaf) "release fixture source must exist: $relative"
        [IO.File]::Copy($source, $destination, $false)
    }
    $releaseManifestObject = [ordered]@{
        schemaVersion = "filingbridge.private-server.release/v1"
        version = "1.2.3"
        generatedAtUtc = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        candidate = [ordered]@{ commitSha = ("a" * 40); githubActionsRunUrl = "https://github.com/jasperfordesq-ai/accounts/actions/runs/1" }
        supportedHosts = @("windows-x64", "ubuntu-x64")
        images = [ordered]@{
            backend = [ordered]@{ exactDigestReference = "ghcr.io/jasperfordesq-ai/accounts-api@sha256:$('1' * 64)" }
            frontend = [ordered]@{ exactDigestReference = "ghcr.io/jasperfordesq-ai/accounts-frontend@sha256:$('2' * 64)" }
            postgres = [ordered]@{ exactDigestReference = "postgres@sha256:$('3' * 64)" }
            mcp = [ordered]@{ exactDigestReference = "ghcr.io/jasperfordesq-ai/accounts-mcp@sha256:$('4' * 64)" }
        }
        files = @($requiredReleaseFiles | ForEach-Object {
            $releaseFile = Join-Path $releaseRoot $_
            [ordered]@{
                path = $_.Replace('\', '/')
                byteSize = (Get-Item -LiteralPath $releaseFile -Force).Length
                sha256 = (Get-FileHash -LiteralPath $releaseFile -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        })
        statutoryAssurance = [ordered]@{ status = "release-blocked"; noDirectSubmission = $true; qualifiedAccountantRequired = $true }
    }
    $syntheticManifestPath = Join-Path $releaseRoot "release.json"
    [IO.File]::WriteAllText($syntheticManifestPath, ($releaseManifestObject | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    Invoke-FilingBridgePrivateServer -Command setup -RepositoryRoot $releaseRoot -StateDirectory (Join-Path $testRoot "manifest-dry-state") -ReleaseManifest $syntheticManifestPath -TenantName "Manifest Charity" -OwnerEmail "manifest@example.ie" -OwnerName "Manifest Owner" -NonInteractive -SkipPrerequisiteChecks -DryRun -Port 35491 6>$null
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $testRoot "manifest-dry-state"))) "manifest setup dry-run must not create state"
    $mutationState = Join-Path $testRoot "manifest-snapshot-state"
    $mutationCallStart = $global:FbOperatorCalls.Count
    $global:FbMutateReleaseComposeOnValidate = Join-Path $releaseRoot "compose.private.yml"
    Invoke-FilingBridgePrivateServer -Command setup -RepositoryRoot $releaseRoot -StateDirectory $mutationState -ReleaseManifest $syntheticManifestPath -TenantName "Snapshot Charity" -OwnerEmail "snapshot@example.ie" -OwnerName "Snapshot Owner" -NonInteractive -SkipPrerequisiteChecks -Port 35494 6>$null
    $installedSnapshot = Join-Path $mutationState "compose.private.installed.yml"
    $mutationComposeCalls = @($global:FbOperatorCalls | Select-Object -Skip $mutationCallStart | Where-Object { $_.file -eq "docker" -and $_.arguments -contains "compose" })
    Assert-True ($mutationComposeCalls.Count -gt 0) "reviewed setup fixture must exercise Compose"
    foreach ($call in $mutationComposeCalls) {
        $fileIndex = [Array]::IndexOf([string[]]$call.arguments, "-f")
        Assert-True ($fileIndex -ge 0 -and [string]$call.arguments[$fileIndex + 1] -eq $installedSnapshot) "setup must use only the immutable installed Compose snapshot after verification"
    }
    Assert-True ((Get-FileHash $installedSnapshot -Algorithm SHA256).Hash -ne (Get-FileHash (Join-Path $releaseRoot "compose.private.yml") -Algorithm SHA256).Hash) "test seam must mutate only the source Compose after snapshot creation"
    Copy-Item -LiteralPath (Join-Path $repositoryRoot "compose.private.yml") -Destination (Join-Path $releaseRoot "compose.private.yml") -Force
    $releaseManifestObject.images.backend.exactDigestReference = "ghcr.io/attacker/accounts-api@sha256:$('1' * 64)"
    [IO.File]::WriteAllText($syntheticManifestPath, ($releaseManifestObject | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    Assert-Throws {
        Invoke-FilingBridgePrivateServer -Command setup -RepositoryRoot $releaseRoot -StateDirectory (Join-Path $testRoot "bad-manifest-state") -ReleaseManifest $syntheticManifestPath -TenantName "Manifest Charity" -OwnerEmail "manifest@example.ie" -OwnerName "Manifest Owner" -NonInteractive -SkipPrerequisiteChecks -DryRun -Port 35492 6>$null
    } 'exact FilingBridge GHCR API' "release manifest must not redirect image pulls to another registry namespace"
    $releaseManifestObject.images.backend.exactDigestReference = "ghcr.io/jasperfordesq-ai/accounts-api@sha256:$('1' * 64)"
    $releaseManifestObject.images.mcp.exactDigestReference = "ghcr.io/attacker/accounts-mcp@sha256:$('4' * 64)"
    [IO.File]::WriteAllText($syntheticManifestPath, ($releaseManifestObject | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    Assert-Throws {
        Invoke-FilingBridgePrivateServer -Command setup -RepositoryRoot $releaseRoot -StateDirectory (Join-Path $testRoot "bad-mcp-manifest-state") -ReleaseManifest $syntheticManifestPath -TenantName "Manifest Charity" -OwnerEmail "manifest@example.ie" -OwnerName "Manifest Owner" -NonInteractive -SkipPrerequisiteChecks -DryRun -Port 35492 6>$null
    } 'exact FilingBridge GHCR MCP connector' "release manifest must not redirect the MCP connector image to another registry namespace"
    $releaseManifestObject.images.Remove("mcp")
    [IO.File]::WriteAllText($syntheticManifestPath, ($releaseManifestObject | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    Assert-Throws {
        Invoke-FilingBridgePrivateServer -Command setup -RepositoryRoot $releaseRoot -StateDirectory (Join-Path $testRoot "no-mcp-manifest-state") -ReleaseManifest $syntheticManifestPath -TenantName "Manifest Charity" -OwnerEmail "manifest@example.ie" -OwnerName "Manifest Owner" -NonInteractive -SkipPrerequisiteChecks -DryRun -Port 35492 6>$null
    } 'has no images\.mcp' "a release whose Compose runs the MCP connector must pin images.mcp"
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $testRoot "no-mcp-manifest-state"))) "a release refused for a missing MCP image must not create state"
    $releaseManifestObject.images["mcp"] = [ordered]@{ exactDigestReference = "ghcr.io/jasperfordesq-ai/accounts-mcp@sha256:$('4' * 64)" }
    [IO.File]::WriteAllText($syntheticManifestPath, ($releaseManifestObject | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))

    $unicodeTenantName = "Carthanas $([char]0x00D3) C$([char]0x00F3)na$([char]0x00ED)"
    $unicodeOwnerName = "M$([char]0x00E1)ire O'Connor"
    $mainSetupCallStart = $global:FbOperatorCalls.Count
    Invoke-FilingBridgePrivateServer -Command setup -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory -TenantName $unicodeTenantName -OwnerEmail "owner@example.ie" -OwnerName $unicodeOwnerName -BuildLocal -NonInteractive -SkipPrerequisiteChecks -Port 35490 6>$null
    $statePath = Join-Path $stateDirectory "server.json"
    Assert-True (Test-Path -LiteralPath $statePath -PathType Leaf) "setup must create state outside the checkout"
    $state = ([IO.File]::ReadAllText($statePath, $testUtf8)) | ConvertFrom-Json
    $global:FbFakeState = $state
    Assert-True ($state.status -eq "ready") "successful setup must record ready state"
    if ([Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::Linux)) {
        $mountedSecretMode = (& stat -c '%a' -- (Join-Path $state.secretDirectory "accounts_api_key")).Trim()
        $hostOnlySecretMode = (& stat -c '%a' -- (Join-Path $state.secretDirectory "backup_authentication_key")).Trim()
        $secretDirectoryMode = (& stat -c '%a' -- $state.secretDirectory).Trim()
        Assert-True ($mountedSecretMode -ceq "644") "container-mounted Linux secrets must be readable by non-root container users"
        Assert-True ($hostOnlySecretMode -ceq "600") "host-only Linux secrets must remain owner-only"
        Assert-True ($secretDirectoryMode -ceq "700") "the Linux secret directory must prevent traversal by other host users"
    }
    Assert-True ($state.composeProject -match '^filingbridge-[a-f0-9]{12}$') "setup must generate a unique Compose project"
    $parsedInstanceId = [Guid]::Empty
    Assert-True ([Guid]::TryParse([string]$state.instanceId, [ref]$parsedInstanceId)) "setup must generate an installation GUID"
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot "private.env"))) "setup must not write state into the checkout"
    Assert-True ($state.reviewedRelease -eq $false) "a source build or self-asserted manifest must never be recorded as reviewed"
    Assert-True ($state.releaseIntegrityStatus -eq "source-build-unreviewed") "source build integrity status must be explicit"
    $secretDirectory = Join-Path $stateDirectory "secrets"
    foreach ($cryptoName in @("identity_hmac_key", "mfa_encryption_key", "database_tenant_context_key", "auth_session_signing_key", "audit_integrity_signing_key", "backup_authentication_key", "connector_signing_key", "accounts_mcp_api_key")) {
        $value = [IO.File]::ReadAllText((Join-Path $secretDirectory $cryptoName))
        Assert-True ([Convert]::FromBase64String($value).Length -ge 32) "$cryptoName must contain backend-compatible standard Base64"
    }
    $sentinel = [IO.File]::ReadAllText((Join-Path $secretDirectory "private_initial_owner_password"))
    Assert-True ($sentinel -eq "INITIALIZATION-COMPLETE-NO-PASSWORD") "ephemeral Owner password must be erased after setup"
    $envText = [IO.File]::ReadAllText((Join-Path $stateDirectory "private.env"), $testUtf8)
    $mcpKeyText = [IO.File]::ReadAllText((Join-Path $secretDirectory "accounts_mcp_api_key"), $testUtf8)
    $mcpHashText = [IO.File]::ReadAllText((Join-Path $secretDirectory "accounts_mcp_api_key_hash"), $testUtf8)
    Assert-True ($mcpHashText -ceq (& $privateServerModule { param($value) Get-FbSha256Text $value } $mcpKeyText)) "setup must give the API exactly the SHA-256 hash of the MCP connector service key"
    Assert-True ($mcpKeyText -cne [IO.File]::ReadAllText((Join-Path $secretDirectory "accounts_api_key"), $testUtf8)) "the MCP connector service key must differ from the frontend proxy key"
    Assert-True (-not [string]::IsNullOrWhiteSpace([string]$state.images.mcp)) "a source build must build and record the MCP connector image"
    Assert-True ($state.connectorEnabled -eq $false) "setup must leave the MCP connector disabled"
    Assert-True ($envText -match "(?m)^ACCOUNTS_MCP_IMAGE='filingbridge-private-mcp:") "environment must pin the MCP connector image"
    Assert-True ($envText -match "(?m)^PRIVATE_CONNECTOR_ENABLED='false'\r?$") "environment must keep the MCP connector off by default"
    Assert-True ($envText -match "(?m)^PRIVATE_REQUIRE_PRIVILEGED_MFA='false'\r?$") "environment must record privileged MFA as off by default"
    Assert-True ($envText -match "(?m)^PRIVATE_CONNECTOR_PUBLIC_ORIGIN='http://localhost:35490'\r?$") "connector origin must default to the exact loopback origin"
    Assert-True ($envText -match "(?m)^PRIVATE_CONNECTOR_PUBLIC_HOST='localhost:35490'\r?$") "connector host must match the default loopback origin"
    Assert-True ($envText -match "(?m)^ACCOUNTS_MCP_API_KEY_FILE='.+accounts_mcp_api_key'\r?$" -and $envText -match "(?m)^ACCOUNTS_MCP_API_KEY_HASH_FILE='.+accounts_mcp_api_key_hash'\r?$") "environment must reference the MCP connector key files"
    foreach ($secretFile in @(Get-ChildItem -LiteralPath $secretDirectory -File | Where-Object { $_.Name -ne "private_initial_owner_password" })) {
        $secretValue = [IO.File]::ReadAllText($secretFile.FullName)
        Assert-True (-not $envText.Contains($secretValue)) "environment file must not contain raw secret '$($secretFile.Name)'"
        $allArguments = ($global:FbOperatorCalls | ForEach-Object { $_.arguments -join " " }) -join "`n"
        Assert-True (-not $allArguments.Contains($secretValue)) "native command arguments must not contain raw secret '$($secretFile.Name)'"
    }
    $validStateText = [IO.File]::ReadAllText($statePath, $testUtf8)
    foreach ($pathField in @("stateDirectory", "secretDirectory", "environmentFile", "installedComposeFile")) {
        $mutatedState = $validStateText | ConvertFrom-Json
        $mutatedState.$pathField = Join-Path $testRoot ("redirected-" + $pathField)
        [IO.File]::WriteAllText($statePath, (($mutatedState | ConvertTo-Json -Depth 12) + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
        Assert-Throws { Invoke-FilingBridgePrivateServer -Command status -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory 6>$null } 'state path binding is invalid' "state path '$pathField' must not redirect an operator command"
    }
    [IO.File]::WriteAllText($statePath, $validStateText, [Text.UTF8Encoding]::new($false))
    $setupCallLines = @($global:FbOperatorCalls | Select-Object -Skip $mainSetupCallStart | ForEach-Object { $_.arguments -join " " })
    Assert-True (@($setupCallLines | Where-Object { $_ -match '\bup\b.*\bdb\b' -and $_ -notmatch '\b(role-provision|migrate)\b' }).Count -ge 1) "setup must wait on PostgreSQL separately"
    Assert-True (@($setupCallLines | Where-Object { $_ -match '\bup\b.*\b(role-provision|migrate)\b' }).Count -eq 0) "setup must not put completed one-shot services under Compose --wait"
    Assert-True (@($setupCallLines | Where-Object { $_ -match '\brun\s+--rm\s+--no-deps\s+role-provision\b' }).Count -eq 1) "setup must exit-check role provisioning as an explicit one-shot"
    Assert-True (@($setupCallLines | Where-Object { $_ -match '\brun\s+--rm\s+--no-deps\s+migrate\b' }).Count -eq 1) "setup must exit-check migration as an explicit one-shot"
    Assert-True (@($setupCallLines | Where-Object { $_ -match '\bup\b.*\bapi frontend mcp$' }).Count -eq 1) "setup must start the MCP connector service with the application runtime"
    Assert-True (@($setupCallLines | Where-Object { $_ -match 'Dockerfile\.mcp' }).Count -eq 1) "a source-build setup must build the MCP connector image locally"
    if ($null -ne (Get-Command docker -ErrorAction SilentlyContinue)) {
        & docker compose --project-name $state.composeProject --env-file (Join-Path $stateDirectory "private.env") -f (Join-Path $repositoryRoot "compose.private.yml") config --quiet
        Assert-True ($LASTEXITCODE -eq 0) "generated environment and secret paths must render the real Private Server Compose file"
    }

    Assert-Throws { Invoke-FilingBridgePrivateServer -Command setup -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory -TenantName X -OwnerEmail x@example.ie -OwnerName X -BuildLocal -NonInteractive -SkipPrerequisiteChecks 6>$null } 'refuses to overwrite' "setup must refuse an existing state directory"
    Assert-Throws { Invoke-FilingBridgePrivateServer -Command setup -RepositoryRoot $repositoryRoot -StateDirectory (Join-Path $testRoot "short-slug") -TenantName X -TenantSlug ab -OwnerEmail x@example.ie -OwnerName X -BuildLocal -NonInteractive -SkipPrerequisiteChecks -DryRun -Port 35493 6>$null } '3-50' "operator slug validation must match the backend initializer minimum"

    $heldLock = & $privateServerModule { param($path) Enter-FbInstallationLock $path } $stateDirectory
    $savedErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $childPowerShell = (Get-Process -Id $PID).Path
        if ([string]::IsNullOrWhiteSpace($childPowerShell)) { throw "Could not resolve the current PowerShell executable for the lifecycle-lock subprocess test." }
        $secondCommandOutput = @(& $childPowerShell -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $dispatcherPath stop -StateDirectory $stateDirectory -DryRun 2>&1 | ForEach-Object { [string]$_ })
        $secondCommandExit = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $savedErrorActionPreference
        & $privateServerModule { param($lock) Exit-FbInstallationLock $lock } $heldLock
    }
    Assert-True ($secondCommandExit -ne 0) "a second process must not enter a lifecycle command while the installation lock is held"
    Assert-True (($secondCommandOutput -join "`n") -match '(?s)exclusive\s+lifecycle lock.*no Docker') "lock contention must fail before any Docker or state mutation"

    $global:FbOperatorCalls.Clear()
    Invoke-FilingBridgePrivateServer -Command start -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory 6>$null
    $directStartCalls = @($global:FbOperatorCalls | Where-Object { $_.file -eq "docker" -and $_.arguments.Count -eq 2 -and $_.arguments[0] -eq "start" })
    Assert-True ($directStartCalls.Count -eq 4) "daily start must invoke docker start once for each exact existing runtime container, including the MCP connector"
    Assert-True (@($directStartCalls | Where-Object { $_.arguments[1] -notin $global:FbFakeContainerIds.Values }).Count -eq 0) "daily start must use only the verified existing runtime container IDs"
    $composeStartCalls = @($global:FbOperatorCalls | Where-Object { $_.file -eq "docker" -and $_.arguments -contains "compose" -and $_.arguments -contains "start" })
    Assert-True ($composeStartCalls.Count -eq 0) "daily start must not traverse missing one-shot Compose dependencies"
    $startCalls = ($global:FbOperatorCalls | ForEach-Object { $_.arguments -join " " }) -join "`n"
    Assert-True ($startCalls -notmatch '\b(build|pull|migrate|seed|up|run)\b') "daily start must not build, pull, migrate, seed, create, or run one-shots"
    foreach ($call in @($global:FbOperatorCalls | Where-Object { $_.file -eq "docker" -and $_.arguments -contains "compose" })) {
        $callArguments = @($call.arguments | ForEach-Object { [string]$_ })
        $fileIndex = [Array]::IndexOf($callArguments, "-f")
        Assert-True ($fileIndex -ge 0 -and $callArguments[$fileIndex + 1] -eq (Join-Path $stateDirectory "compose.private.installed.yml")) "routine commands must execute only the installed Compose snapshot"
    }

    $global:FbOperatorCalls.Clear()
    Invoke-FilingBridgePrivateServer -Command stop -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory 6>$null
    $stopCalls = ($global:FbOperatorCalls | ForEach-Object { $_.arguments -join " " }) -join "`n"
    Assert-True ($stopCalls -notmatch '(--volumes|-v\b)') "ordinary stop must preserve volumes"
    Assert-True ($stopCalls -match '\bstop --timeout 60 mcp frontend api db\b') "ordinary stop must stop the MCP connector with the other application services"
    Assert-True ($global:FbFakeServices.Count -eq 0) "ordinary stop must leave no runtime service running"

    $backupDirectory = Join-Path $testRoot "backups"
    Invoke-FilingBridgePrivateServer -Command backup -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory -OutputDirectory $backupDirectory -PlaintextDatabaseOnly 6>$null
    $dump = @(Get-ChildItem -LiteralPath $backupDirectory -Filter "*.dump" -File)
    Assert-True ($dump.Count -eq 1) "explicit database-only backup must publish one dump"
    $envelope = ([IO.File]::ReadAllText(($dump[0].FullName + ".manifest.json"), $testUtf8)) | ConvertFrom-Json
    Assert-True ($envelope.completeRecoverySet -eq $false) "plaintext database-only backup must never claim complete recovery"
    Assert-True ($envelope.databaseVerification.tableCount -eq 42) "backup must retain disposable restore evidence"
    Assert-True ($envelope.databaseVerification.migrationCount -eq 12) "backup must retain migration evidence"
    Assert-True ($envelope.databaseVerification.fingerprintsMatched -eq $true) "backup must match source and restored important-table evidence"
    Assert-True (@($envelope.databaseVerification.importantTables).Count -eq 5) "backup must retain all important-table counts and fingerprints"
    Assert-True ($envelope.authentication.algorithm -eq "HMAC-SHA256") "plaintext backup must carry installation authentication"

    Invoke-FilingBridgePrivateServer -Command verify-backup -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory -BackupPath $dump[0].FullName -AllowPlaintextDatabaseOnlyRestore 3>$null 6>$null
    $validEnvelopeText = [IO.File]::ReadAllText(($dump[0].FullName + ".manifest.json"), $testUtf8)
    $tamperedEnvelope = $validEnvelopeText | ConvertFrom-Json
    $tamperedEnvelope.status = "forged"
    [IO.File]::WriteAllText(($dump[0].FullName + ".manifest.json"), (($tamperedEnvelope | ConvertTo-Json -Depth 12) + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
    Assert-Throws {
        Invoke-FilingBridgePrivateServer -Command verify-backup -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory -BackupPath $dump[0].FullName -AllowPlaintextDatabaseOnlyRestore 3>$null 6>$null
    } 'authentication failed' "a self-consistent hash is insufficient without the installation HMAC"
    [IO.File]::WriteAllText(($dump[0].FullName + ".manifest.json"), $validEnvelopeText, [Text.UTF8Encoding]::new($false))

    $arbitraryParent = Join-Path $testRoot "operator-owned-existing-parent"
    New-Item -ItemType Directory -Path $arbitraryParent | Out-Null
    $sentinelPath = Join-Path $arbitraryParent "keep-me.txt"
    [IO.File]::WriteAllText($sentinelPath, "unchanged", [Text.UTF8Encoding]::new($false))
    $parentProtectedBefore = if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) { (Get-Acl $arbitraryParent).AreAccessRulesProtected } else { $null }
    Invoke-FilingBridgePrivateServer -Command backup -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory -OutputDirectory $arbitraryParent -PlaintextDatabaseOnly 6>$null
    Assert-True ([IO.File]::ReadAllText($sentinelPath, $testUtf8) -eq "unchanged") "backup must not modify files in an arbitrary existing parent"
    Assert-True (@(Get-ChildItem -LiteralPath $arbitraryParent -Filter "*.dump" -File).Count -eq 0) "backup must allocate a managed child under an arbitrary existing parent"
    Assert-True (@(Get-ChildItem -LiteralPath $arbitraryParent -Filter "*.dump" -File -Recurse).Count -eq 1) "managed backup child must contain the published dump"
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
        Assert-True ((Get-Acl $arbitraryParent).AreAccessRulesProtected -eq $parentProtectedBefore) "backup must not rewrite the arbitrary parent ACL"
    }
    Assert-Throws {
        Invoke-FilingBridgePrivateServer -Command backup -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory -OutputDirectory ([IO.Path]::GetPathRoot($stateDirectory)) -PlaintextDatabaseOnly 6>$null
    } 'filesystem root' "backup must refuse to ACL or publish into a filesystem root"

    $identityPath = Join-Path $testRoot "age-identity.txt"
    [IO.File]::WriteAllText($identityPath, "AGE-SECRET-KEY-TEST-ONLY", [Text.UTF8Encoding]::new($false))
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
        $null = @(& icacls.exe $backupDirectory "/grant" "*S-1-1-0:(OI)(CI)R" "/Q" 2>&1)
        Assert-True ($LASTEXITCODE -eq 0) "test must be able to add an unexpected backup-directory ACL"
    }
    Invoke-FilingBridgePrivateServer -Command backup -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory -OutputDirectory $backupDirectory -BackupRecipient "age1testrecipient" 6>$null
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
        $backupAcl = Get-Acl -LiteralPath $backupDirectory
        $backupSids = @($backupAcl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]) | ForEach-Object { $_.IdentityReference.Value })
        Assert-True ($backupSids -notcontains "S-1-1-0") "reused backup destination must not retain a broad explicit Everyone ACL"
    }
    $complete = @(Get-ChildItem -LiteralPath $backupDirectory -Filter "*.fbbackup.age" -File)
    Assert-True ($complete.Count -eq 1) "encrypted complete backup must publish one recovery envelope"
    $completeManifest = ([IO.File]::ReadAllText(($complete[0].FullName + ".manifest.json"), $testUtf8)) | ConvertFrom-Json
    Assert-True ($completeManifest.completeRecoverySet -eq $true) "encrypted backup must identify a complete recovery set"
    Assert-True ($completeManifest.authentication.algorithm -eq "HMAC-SHA256") "encrypted backup ciphertext must carry installation authentication"
    Invoke-FilingBridgePrivateServer -Command verify-backup -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory -BackupPath $complete[0].FullName -AgeIdentityFile $identityPath 6>$null
    $decryptCall = @($global:FbOperatorCalls | Where-Object { $_.description -eq "Decrypt the complete recovery set" } | Select-Object -Last 1)
    Assert-True ($decryptCall.Count -eq 1) "encrypted verification must invoke age once"
    Assert-True ([string]$decryptCall[0].arguments[-1] -ne $complete[0].FullName) "age must never reopen the attacker-writable original path after authentication"
    Assert-True ([string]$decryptCall[0].arguments[-1] -match 'authenticated-recovery-set\.fbbackup\.age$') "age must read only the re-hashed ACL-restricted copy"

    $recoveryKeyDirectory = Join-Path $testRoot "separate-recovery-trust"
    Invoke-FilingBridgePrivateServer -Command export-recovery-key -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory -OutputDirectory $recoveryKeyDirectory -Confirmation ("EXPORT RECOVERY KEY " + $state.instanceId) -NonInteractive 3>$null 6>$null
    $recoveryKeys = @(Get-ChildItem -LiteralPath $recoveryKeyDirectory -Filter "*.key" -File -Recurse)
    Assert-True ($recoveryKeys.Count -eq 1) "recovery trust export must publish one separately retainable key"
    Assert-True ([IO.File]::ReadAllText($recoveryKeys[0].FullName, $testUtf8).Trim() -eq [IO.File]::ReadAllText((Join-Path $state.secretDirectory "backup_authentication_key"), $testUtf8).Trim()) "exported recovery trust anchor must match the installation backup authenticator"
    $wrongRecoveryKey = Join-Path $testRoot "wrong-recovery.key"
    [IO.File]::WriteAllText($wrongRecoveryKey, (New-PrivateServerRandomSecret), [Text.UTF8Encoding]::new($false))
    Assert-Throws {
        Invoke-FilingBridgePrivateServer -Command recover-host -RepositoryRoot $repositoryRoot -StateDirectory (Join-Path $testRoot "wrong-host-state") -BackupPath $complete[0].FullName -AgeIdentityFile $identityPath -RecoveryAuthenticationKeyFile $wrongRecoveryKey -Confirmation ("RECOVER HOST " + $state.instanceId) -BuildLocal -NonInteractive -SkipPrerequisiteChecks -Port 35495 6>$null
    } 'trust anchor' "replacement-host recovery must reject a different separately retained authentication key before decryption"

    $recoveredStateDirectory = Join-Path $testRoot "recovered-host-state"
    $global:FbOperatorCalls.Clear()
    Invoke-FilingBridgePrivateServer -Command recover-host -RepositoryRoot $repositoryRoot -StateDirectory $recoveredStateDirectory -BackupPath $complete[0].FullName -AgeIdentityFile $identityPath -RecoveryAuthenticationKeyFile $recoveryKeys[0].FullName -Confirmation ("RECOVER HOST " + $state.instanceId) -BuildLocal -NonInteractive -SkipPrerequisiteChecks -Port 35496 6>$null
    $recoveredState = ([IO.File]::ReadAllText((Join-Path $recoveredStateDirectory "server.json"), $testUtf8)) | ConvertFrom-Json
    $global:FbFakeState = $recoveredState
    Assert-True ($recoveredState.status -eq "ready") "replacement-host recovery must finish in ready state only after health and fingerprint checks"
    Assert-True ($recoveredState.recoveredFromInstanceId -eq $state.instanceId) "replacement-host state must retain the source installation identity"
    Assert-True ($recoveredState.instanceId -ne $state.instanceId) "replacement host must use a new isolated installation and Compose identity"
    Assert-True ($recoveredState.tenantSlug -eq $state.tenantSlug) "replacement host must preserve tenant-qualified login identity"
    Assert-True ([IO.File]::ReadAllText((Join-Path $recoveredState.secretDirectory "mfa_encryption_key"), $testUtf8) -eq [IO.File]::ReadAllText((Join-Path $state.secretDirectory "mfa_encryption_key"), $testUtf8)) "replacement host must preserve MFA decryption continuity"
    Assert-True ([IO.File]::ReadAllText((Join-Path $recoveredState.secretDirectory "auth_session_signing_key"), $testUtf8) -ne [IO.File]::ReadAllText((Join-Path $state.secretDirectory "auth_session_signing_key"), $testUtf8)) "replacement host must invalidate all captured browser sessions"
    $recoveredMcpKey = [IO.File]::ReadAllText((Join-Path $recoveredState.secretDirectory "accounts_mcp_api_key"), $testUtf8)
    Assert-True ($recoveredMcpKey -ne [IO.File]::ReadAllText((Join-Path $state.secretDirectory "accounts_mcp_api_key"), $testUtf8)) "replacement host must regenerate the MCP connector service key"
    Assert-True ([IO.File]::ReadAllText((Join-Path $recoveredState.secretDirectory "accounts_mcp_api_key_hash"), $testUtf8) -ceq (& $privateServerModule { param($value) Get-FbSha256Text $value } $recoveredMcpKey)) "replacement host must bind the regenerated MCP key to its API hash"
    Assert-True ($recoveredState.connectorEnabled -eq $false) "replacement host must start with the MCP connector disabled"
    $hostRecoveryCalls = ($global:FbOperatorCalls | ForEach-Object { $_.description }) -join "`n"
    Assert-True ($hostRecoveryCalls -match 'disposable verification database') "replacement-host recovery must independently restore-test the database before selection"
    Assert-True ($hostRecoveryCalls -match 'Read important-table fingerprints from the recovered database') "replacement-host recovery must compare live post-start business-data fingerprints"

    $global:FbExpectedFrontendPort = 35496
    $global:FbFakePublishedService = "api"
    Assert-Throws {
        Invoke-FilingBridgePrivateServer -Command local-check -RepositoryRoot $repositoryRoot -StateDirectory $recoveredStateDirectory 6>$null
    } 'api has a host-published port' "local acceptance must reject a real HostConfig API port binding"
    $global:FbFakePublishedService = "mcp"
    Assert-Throws {
        Invoke-FilingBridgePrivateServer -Command local-check -RepositoryRoot $repositoryRoot -StateDirectory $recoveredStateDirectory 6>$null
    } 'mcp has a host-published port' "local acceptance must reject a host-published MCP connector port"
    $global:FbFakePublishedService = ""
    Invoke-FilingBridgePrivateServer -Command local-check -RepositoryRoot $repositoryRoot -StateDirectory $recoveredStateDirectory 6>$null
    $localReports = @(Get-ChildItem -LiteralPath (Join-Path $recoveredStateDirectory "acceptance") -Filter "local-check-*.json" -File)
    Assert-True ($localReports.Count -eq 1) "local acceptance must retain one machine-readable report"
    $localReport = ([IO.File]::ReadAllText($localReports[0].FullName, $testUtf8)) | ConvertFrom-Json
    Assert-True ($localReport.status -eq "passed" -and $localReport.frontendBinding -eq "127.0.0.1:35496") "local acceptance must prove exact IPv4 loopback frontend publication"
    Assert-True ($localReport.apiHostPortPublished -eq $false -and $localReport.databaseHostPortPublished -eq $false) "local acceptance must prove API and database host ports remain absent"
    Assert-True ($localReport.mcpConnectorServicePresent -eq $true -and $localReport.mcpHostPortPublished -eq $false) "local acceptance must prove the MCP connector service publishes no host port"
    Assert-True ((@($localReport.runningServices) -join ",") -eq "db,api,frontend,mcp") "local acceptance must require the MCP connector service to run"
    Assert-True ($localReport.businessDataFingerprintCount -eq 5) "local acceptance must retain all five business-data fingerprints"

    $env:FILINGBRIDGE_TEST_BOOT_ID = "2026-07-12T01:00:00.0000000Z"
    Invoke-FilingBridgePrivateServer -Command reboot-check -Action prepare -RepositoryRoot $repositoryRoot -StateDirectory $recoveredStateDirectory 6>$null
    $pendingReboot = Join-Path $recoveredStateDirectory "acceptance\reboot-check.pending.json"
    Assert-True (Test-Path -LiteralPath $pendingReboot -PathType Leaf) "reboot preparation must retain pre-reboot identity and data fingerprints"
    $pendingRebootRecord = ([IO.File]::ReadAllText($pendingReboot, $testUtf8)) | ConvertFrom-Json
    Assert-True ($pendingRebootRecord.bootIdentityFormat -eq "opaque-boot-identity/v1") "reboot preparation must version its opaque boot identity"
    $pendingRebootRecord.PSObject.Properties.Remove("bootIdentityFormat")
    [IO.File]::WriteAllText($pendingReboot, (($pendingRebootRecord | ConvertTo-Json -Depth 12) + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
    Assert-Throws {
        Invoke-FilingBridgePrivateServer -Command reboot-check -Action verify -RepositoryRoot $repositoryRoot -StateDirectory $recoveredStateDirectory 6>$null
    } 'unsupported boot identity format' "legacy or changed boot identity formats must fail closed instead of faking a reboot"
    Remove-Item -LiteralPath $pendingReboot -Force
    Invoke-FilingBridgePrivateServer -Command reboot-check -Action prepare -RepositoryRoot $repositoryRoot -StateDirectory $recoveredStateDirectory 6>$null
    $releaseBoundState = ([IO.File]::ReadAllText((Join-Path $recoveredStateDirectory "server.json"), $testUtf8)) | ConvertFrom-Json
    $originalReleaseCommit = [string]$releaseBoundState.releaseCommitSha
    $releaseBoundState.releaseCommitSha = "changed-after-prepare"
    [IO.File]::WriteAllText((Join-Path $recoveredStateDirectory "server.json"), (($releaseBoundState | ConvertTo-Json -Depth 12) + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
    Assert-Throws {
        Invoke-FilingBridgePrivateServer -Command reboot-check -Action verify -RepositoryRoot $repositoryRoot -StateDirectory $recoveredStateDirectory 6>$null
    } 'release or Compose identity changed' "reboot verification must stay bound to the prepared release and Compose identity"
    $releaseBoundState.releaseCommitSha = $originalReleaseCommit
    [IO.File]::WriteAllText((Join-Path $recoveredStateDirectory "server.json"), (($releaseBoundState | ConvertTo-Json -Depth 12) + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
    Assert-Throws {
        Invoke-FilingBridgePrivateServer -Command reboot-check -Action verify -RepositoryRoot $repositoryRoot -StateDirectory $recoveredStateDirectory 6>$null
    } 'has not rebooted' "reboot verification must reject a check completed in the same Windows boot"
    $env:FILINGBRIDGE_TEST_BOOT_ID = "2026-07-12T02:00:00.0000000Z"
    $global:FbFakeRebootStartMinutes = 31
    Assert-Throws {
        Invoke-FilingBridgePrivateServer -Command reboot-check -Action verify -RepositoryRoot $repositoryRoot -StateDirectory $recoveredStateDirectory 6>$null
    } 'automatically restarted within 30 minutes' "reboot verification must reject services started outside the automatic Docker startup window"
    $global:FbFakeRebootStartMinutes = 5
    Invoke-FilingBridgePrivateServer -Command reboot-check -Action verify -RepositoryRoot $repositoryRoot -StateDirectory $recoveredStateDirectory 6>$null
    Assert-True (-not (Test-Path -LiteralPath $pendingReboot)) "successful reboot verification must consume the pending marker"
    $rebootReports = @(Get-ChildItem -LiteralPath (Join-Path $recoveredStateDirectory "acceptance") -Filter "reboot-check-*.json" -File)
    Assert-True ($rebootReports.Count -eq 1) "successful reboot verification must retain one machine-readable report"
    $rebootReport = ([IO.File]::ReadAllText($rebootReports[0].FullName, $testUtf8)) | ConvertFrom-Json
    Assert-True ($rebootReport.status -eq "passed" -and $rebootReport.importantTablesMatched -eq $true) "reboot evidence must prove service health and unchanged business-data fingerprints"
    Assert-True ($rebootReport.acceptanceContract -eq "release-bound-auto-restart/v1" -and @($rebootReport.runtimeAfter).Count -eq 4) "reboot evidence must bind the release and retain automatic container-restart timing, including the MCP connector"
    Remove-Item Env:\FILINGBRIDGE_TEST_BOOT_ID -ErrorAction SilentlyContinue

    $failedRecoveryStateDirectory = Join-Path $testRoot "failed-recovered-host-state"
    $global:FbFailDescriptionPattern = '^Start the recovered Private Server runtime$'
    Assert-Throws {
        Invoke-FilingBridgePrivateServer -Command recover-host -RepositoryRoot $repositoryRoot -StateDirectory $failedRecoveryStateDirectory -BackupPath $complete[0].FullName -AgeIdentityFile $identityPath -RecoveryAuthenticationKeyFile $recoveryKeys[0].FullName -Confirmation ("RECOVER HOST " + $state.instanceId) -BuildLocal -NonInteractive -SkipPrerequisiteChecks -Port 35497 6>$null
    } 'writers stopped' "failed replacement-host health/start must prove application writers were stopped before returning"
    $global:FbFailDescriptionPattern = ""
    $failedRecoveryState = ([IO.File]::ReadAllText((Join-Path $failedRecoveryStateDirectory "server.json"), $testUtf8)) | ConvertFrom-Json
    Assert-True ($failedRecoveryState.status -eq "hostRecoveryFailed") "failed replacement-host recovery with proven writer shutdown must retain an explicit blocked state"
    Assert-True ($global:FbFakeServices -notcontains "api" -and $global:FbFakeServices -notcontains "frontend" -and $global:FbFakeServices -notcontains "mcp") "failed replacement-host recovery must leave application writers stopped"
    $global:FbFakeServices = @("db", "api", "frontend", "mcp")

    Assert-Throws { Invoke-FilingBridgePrivateServer -Command restore -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory -BackupPath $dump[0].FullName -Confirmation "wrong" -NonInteractive -AllowPlaintextDatabaseOnlyRestore 6>$null } 'did not match' "restore must require exact typed confirmation"
    Invoke-FilingBridgePrivateServer -Command restore -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory -BackupPath $dump[0].FullName -Confirmation ("RESTORE " + $state.instanceId) -NonInteractive -AllowPlaintextDatabaseOnlyRestore -DryRun 6>$null
    $state.status = "updateFailed"
    $state | Add-Member -NotePropertyName lastPreUpdateBackup -NotePropertyValue $dump[0].FullName -Force
    [IO.File]::WriteAllText($statePath, (($state | ConvertTo-Json -Depth 12) + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
    Invoke-FilingBridgePrivateServer -Command restore -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory -BackupPath $dump[0].FullName -Confirmation ("RESTORE " + $state.instanceId) -NonInteractive -AllowPlaintextDatabaseOnlyRestore -DryRun 6>$null
    $state.status = "ready"
    [IO.File]::WriteAllText($statePath, (($state | ConvertTo-Json -Depth 12) + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))

    $forwardStateText = [IO.File]::ReadAllText($statePath, $testUtf8)
    $forwardState = $forwardStateText | ConvertFrom-Json
    $forwardState.releaseVersion = "2.0.0"
    $forwardState.releaseCommitSha = "b" * 40
    [IO.File]::WriteAllText($statePath, (($forwardState | ConvertTo-Json -Depth 12) + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
    Assert-Throws {
        Invoke-FilingBridgePrivateServer -Command update -RepositoryRoot $releaseRoot -StateDirectory $stateDirectory -ReleaseManifest $syntheticManifestPath -NonInteractive 6>$null
    } 'forward-only' "update must reject a semantic downgrade before backup or migration"
    $forwardState.releaseVersion = "1.2.3"
    $forwardState.releaseCommitSha = "b" * 40
    [IO.File]::WriteAllText($statePath, (($forwardState | ConvertTo-Json -Depth 12) + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
    Assert-Throws {
        Invoke-FilingBridgePrivateServer -Command update -RepositoryRoot $releaseRoot -StateDirectory $stateDirectory -ReleaseManifest $syntheticManifestPath -NonInteractive 6>$null
    } 'different immutable identity' "update must reject reuse of one semantic version for a different commit or image set"
    [IO.File]::WriteAllText($statePath, $forwardStateText, [Text.UTF8Encoding]::new($false))

    # Reshape the installation into the 0.1.0-preview.9 contract: no MCP connector image in state,
    # no connector/MCP secrets, an environment and installed Compose snapshot without the mcp
    # service, and only db/api/frontend running. The update must accept the new manifest, generate
    # the new secrets on its environment write, and start the new service without manual work.
    $legacyState = $forwardStateText | ConvertFrom-Json
    $legacyState.releaseVersion = "0.1.0-preview.9"
    $legacyState.releaseCommitSha = "9" * 40
    $legacyState.images = [pscustomobject]@{
        api = "ghcr.io/jasperfordesq-ai/accounts-api@sha256:$('7' * 64)"
        frontend = "ghcr.io/jasperfordesq-ai/accounts-frontend@sha256:$('8' * 64)"
        postgres = "postgres@sha256:$('3' * 64)"
    }
    foreach ($legacyField in @("connectorEnabled", "connectorPublicOrigin")) { $legacyState.PSObject.Properties.Remove($legacyField) }
    $legacyCompose = [IO.File]::ReadAllText((Join-Path $repositoryRoot "compose.private.yml"), $testUtf8).Replace("`r`n", "`n")
    $legacyCompose = [regex]::Replace($legacyCompose, '(?ms)^  mcp:\n.*?(?=^volumes:)', '')
    $legacyCompose = [regex]::Replace($legacyCompose, '(?m)^      MCP_URL: .*\n', '')
    $legacyCompose = [regex]::Replace($legacyCompose, '(?m)^      ApiAccess__Keys__1__.*\n', '')
    $legacyCompose = [regex]::Replace($legacyCompose, '(?m)^      - accounts_mcp_api_key(?:_hash)?\n', '')
    $legacyCompose = [regex]::Replace($legacyCompose, '(?ms)^  accounts_mcp_api_key(?:_hash)?:\n    file: [^\n]*\n', '')
    $legacyCompose = $legacyCompose.Replace('${PRIVATE_CONNECTOR_PUBLIC_ORIGIN:?set PRIVATE_CONNECTOR_PUBLIC_ORIGIN to the exact connector origin}', '${PRIVATE_LOCAL_ORIGIN:?set PRIVATE_LOCAL_ORIGIN to the exact loopback HTTP origin}')
    Assert-True ($legacyCompose -cnotmatch 'ACCOUNTS_MCP|accounts_mcp|mcp:3100|PRIVATE_CONNECTOR_PUBLIC') "the synthetic preview.9 Compose snapshot must not reference the MCP connector"
    $legacySnapshot = Join-Path $stateDirectory "compose.private.installed.yml"
    [IO.File]::WriteAllText($legacySnapshot, $legacyCompose, [Text.UTF8Encoding]::new($false))
    $legacyState.composeFileSha256 = (Get-FileHash -LiteralPath $legacySnapshot -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($statePath, (($legacyState | ConvertTo-Json -Depth 12) + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
    $legacyEnvironment = @([IO.File]::ReadAllLines((Join-Path $stateDirectory "private.env"), $testUtf8) | Where-Object { $_ -notmatch '^(ACCOUNTS_MCP_|PRIVATE_CONNECTOR_PUBLIC_|CONNECTOR_SIGNING_KEY_FILE|PRIVATE_CONNECTOR_ENABLED)' })
    [IO.File]::WriteAllText((Join-Path $stateDirectory "private.env"), (($legacyEnvironment -join "`n") + "`n"), [Text.UTF8Encoding]::new($false))
    foreach ($legacyAbsentSecret in @("accounts_mcp_api_key", "accounts_mcp_api_key_hash", "connector_signing_key")) {
        Remove-Item -LiteralPath (Join-Path $state.secretDirectory $legacyAbsentSecret) -Force
    }
    $global:FbFakeServices = @("db", "api", "frontend")

    $noMcpManifest = [IO.File]::ReadAllText($syntheticManifestPath, $testUtf8) | ConvertFrom-Json
    $noMcpManifest.images.PSObject.Properties.Remove("mcp")
    $completeManifestText = [IO.File]::ReadAllText($syntheticManifestPath, $testUtf8)
    [IO.File]::WriteAllText($syntheticManifestPath, (($noMcpManifest | ConvertTo-Json -Depth 12) + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
    $refusedUpdateCallStart = $global:FbOperatorCalls.Count
    Assert-Throws {
        Invoke-FilingBridgePrivateServer -Command update -RepositoryRoot $releaseRoot -StateDirectory $stateDirectory -ReleaseManifest $syntheticManifestPath -OutputDirectory $backupDirectory -PlaintextDatabaseOnly -NonInteractive 3>$null 6>$null
    } 'has no images\.mcp' "update must refuse a release that runs the MCP connector without pinning images.mcp"
    Assert-True (@($global:FbOperatorCalls | Select-Object -Skip $refusedUpdateCallStart | Where-Object { $_.mutating }).Count -eq 0) "a refused incomplete release must not back up, pull, stop, or migrate anything"
    Assert-True (([IO.File]::ReadAllText($statePath, $testUtf8) | ConvertFrom-Json).releaseVersion -eq "0.1.0-preview.9") "a refused update must leave the installed release recorded"
    [IO.File]::WriteAllText($syntheticManifestPath, $completeManifestText, [Text.UTF8Encoding]::new($false))

    $updateCallStart = $global:FbOperatorCalls.Count
    $global:FbMutateReleaseComposeOnValidate = Join-Path $releaseRoot "compose.private.yml"
    Invoke-FilingBridgePrivateServer -Command update -RepositoryRoot $releaseRoot -StateDirectory $stateDirectory -ReleaseManifest $syntheticManifestPath -OutputDirectory $backupDirectory -PlaintextDatabaseOnly -NonInteractive 3>$null 6>$null
    $updateSnapshot = Join-Path $stateDirectory "compose.private.installed.yml"
    $updateComposeCalls = @($global:FbOperatorCalls | Select-Object -Skip $updateCallStart | Where-Object { $_.file -eq "docker" -and $_.arguments -contains "compose" })
    Assert-True ($updateComposeCalls.Count -gt 0) "successful update fixture must exercise Compose"
    foreach ($call in $updateComposeCalls) {
        $callArguments = @($call.arguments | ForEach-Object { [string]$_ })
        $fileIndex = [Array]::IndexOf($callArguments, "-f")
        Assert-True ($fileIndex -ge 0 -and $callArguments[$fileIndex + 1] -eq $updateSnapshot) "update must use only the immutable installed Compose snapshot after target verification"
    }
    Assert-True ((Get-FileHash $updateSnapshot -Algorithm SHA256).Hash -ne (Get-FileHash (Join-Path $releaseRoot "compose.private.yml") -Algorithm SHA256).Hash) "update test seam must mutate only the source Compose after snapshot creation"
    $upgradedState = ([IO.File]::ReadAllText($statePath, $testUtf8)) | ConvertFrom-Json
    Assert-True ($upgradedState.status -eq "ready" -and $upgradedState.releaseVersion -eq "1.2.3") "a preview.9 installation must update to a release that adds the MCP connector"
    Assert-True ([string]$upgradedState.images.mcp -ceq "ghcr.io/jasperfordesq-ai/accounts-mcp@sha256:$('4' * 64)") "update must record the release's digest-pinned MCP connector image"
    $upgradedEnvironment = [IO.File]::ReadAllText((Join-Path $stateDirectory "private.env"), $testUtf8)
    Assert-True ($upgradedEnvironment -match "(?m)^ACCOUNTS_MCP_IMAGE='ghcr\.io/jasperfordesq-ai/accounts-mcp@sha256:4{64}'\r?$") "the update environment write must pin the MCP connector image"
    Assert-True ($upgradedEnvironment -match "(?m)^PRIVATE_CONNECTOR_ENABLED='false'\r?$" -and $upgradedEnvironment -match "(?m)^PRIVATE_CONNECTOR_PUBLIC_ORIGIN='http://localhost:35490'\r?$") "an updated installation must keep the connector off at the loopback origin"
    foreach ($generatedSecret in @("accounts_mcp_api_key", "accounts_mcp_api_key_hash", "connector_signing_key")) {
        Assert-True (Test-Path -LiteralPath (Join-Path $state.secretDirectory $generatedSecret) -PathType Leaf) "update must generate '$generatedSecret' for an installation that predates it"
    }
    $upgradedMcpKey = [IO.File]::ReadAllText((Join-Path $state.secretDirectory "accounts_mcp_api_key"), $testUtf8)
    Assert-True ([IO.File]::ReadAllText((Join-Path $state.secretDirectory "accounts_mcp_api_key_hash"), $testUtf8) -ceq (& $privateServerModule { param($value) Get-FbSha256Text $value } $upgradedMcpKey)) "the generated MCP key must be bound to its API hash"
    $updateCallLines = @($global:FbOperatorCalls | Select-Object -Skip $updateCallStart | ForEach-Object { $_.arguments -join " " })
    Assert-True (@($updateCallLines | Where-Object { $_ -match '\bpull --policy always$' }).Count -eq 1) "update must pull the exact target digests, including the MCP connector, before downtime"
    Assert-True (@($updateCallLines | Where-Object { $_ -match '\bup -d --no-deps --wait --wait-timeout 300 api frontend mcp$' }).Count -eq 1) "update must start and health-check the MCP connector service"
    Assert-True ($global:FbFakeServices -contains "mcp") "the MCP connector service must run after an update from preview.9"
    Assert-True (@($updateCallLines | Where-Object { $_ -match '\bstop\b.*\bmcp\b' }).Count -eq 0) "a previously running frontend means the new MCP connector service stays running"
    Copy-Item -LiteralPath (Join-Path $repositoryRoot "compose.private.yml") -Destination (Join-Path $releaseRoot "compose.private.yml") -Force

    $nextManifest = [IO.File]::ReadAllText($syntheticManifestPath, $testUtf8) | ConvertFrom-Json
    $nextManifest.version = "1.2.4"
    $nextManifest.candidate.commitSha = "c" * 40
    foreach ($file in @($nextManifest.files)) {
        $candidateFile = Join-Path $releaseRoot ([string]$file.path)
        $file.byteSize = (Get-Item -LiteralPath $candidateFile -Force).Length
        $file.sha256 = (Get-FileHash -LiteralPath $candidateFile -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    [IO.File]::WriteAllText($syntheticManifestPath, (($nextManifest | ConvertTo-Json -Depth 12) + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
    $global:FbFailDescriptionPattern = '^Run controlled forward migrations for the update$'
    Assert-Throws {
        Invoke-FilingBridgePrivateServer -Command update -RepositoryRoot $releaseRoot -StateDirectory $stateDirectory -ReleaseManifest $syntheticManifestPath -OutputDirectory $backupDirectory -PlaintextDatabaseOnly -NonInteractive 3>$null 6>$null
    } 'verified pre-update backup' "a forced migration failure must retain a verified recovery point and stop writers"
    $global:FbFailDescriptionPattern = ""
    $failedUpdateState = ([IO.File]::ReadAllText($statePath, $testUtf8)) | ConvertFrom-Json
    Assert-True ($failedUpdateState.status -eq "updateFailed") "forced update failure must leave an explicit recoverable state"
    Assert-True (Test-Path -LiteralPath ([string]$failedUpdateState.lastPreUpdateBackup) -PathType Leaf) "forced update failure must retain its exact pre-update backup"
    Assert-True ($global:FbFakeServices -notcontains "api" -and $global:FbFakeServices -notcontains "frontend" -and $global:FbFakeServices -notcontains "mcp") "forced update failure must leave all application writers, including the MCP connector, stopped"
    Invoke-FilingBridgePrivateServer -Command restore -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory -BackupPath ([string]$failedUpdateState.lastPreUpdateBackup) -Confirmation ("RESTORE " + $state.instanceId) -NonInteractive -AllowPlaintextDatabaseOnlyRestore 3>$null 6>$null
    $recoveredUpdateState = ([IO.File]::ReadAllText($statePath, $testUtf8)) | ConvertFrom-Json
    Assert-True ($recoveredUpdateState.status -eq "ready") "explicit restore after forced update failure must return the installation to ready state"
    $restoredMcpKey = [IO.File]::ReadAllText((Join-Path $state.secretDirectory "accounts_mcp_api_key"), $testUtf8)
    Assert-True ($restoredMcpKey -cne $upgradedMcpKey) "restore must regenerate the MCP connector service key"
    Assert-True ([IO.File]::ReadAllText((Join-Path $state.secretDirectory "accounts_mcp_api_key_hash"), $testUtf8) -ceq (& $privateServerModule { param($value) Get-FbSha256Text $value } $restoredMcpKey)) "restore must bind the regenerated MCP key to its API hash"

    $global:FbOperatorCalls.Clear()
    Invoke-FilingBridgePrivateServer -Command owner-recovery -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory -OwnerEmail "owner@example.ie" -Confirmation "RECOVER PRIVATE SERVER OWNER" -NonInteractive 6>$null
    $recoveryArguments = ($global:FbOperatorCalls | ForEach-Object { $_.arguments -join " " }) -join "`n"
    Assert-True ($recoveryArguments -notmatch 'opaque/test') "raw Owner reset token must never appear in native command arguments"
    Assert-True ($recoveryArguments -match 'private-owner-recovery') "Owner recovery must use the dedicated backend one-shot"
    Assert-True ($recoveryArguments -match "PrivateOwnerRecovery__TenantSlug=$([regex]::Escape([string]$state.tenantSlug))") "Owner recovery must pass the exact .NET configuration key for the protected saved tenant slug"
    Assert-True ($recoveryArguments -match "PrivateOwnerRecovery__ConfirmInstallationId=$([regex]::Escape([string]$state.instanceId))") "Owner recovery must pass the exact .NET installation confirmation key"

    function Read-ConnectorFixture {
        return [pscustomobject]@{
            State = ([IO.File]::ReadAllText($statePath, $testUtf8)) | ConvertFrom-Json
            Environment = [IO.File]::ReadAllText((Join-Path $stateDirectory "private.env"), $testUtf8)
        }
    }
    $connectorStatus = @(Invoke-FilingBridgePrivateServer -Command connector -Action status -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory 6>&1 | ForEach-Object { [string]$_ })
    Assert-True (($connectorStatus -join "`n") -match 'MCP connector: disabled' -and ($connectorStatus -join "`n") -match 'MCP URL: http://localhost:35490/mcp') "connector status must report the disabled connector and its loopback MCP URL"
    Assert-Throws { Invoke-FilingBridgePrivateServer -Command connector -Action enable -PublicOrigin tailscale -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory 6>$null } 'Tailscale Serve is not configured' "connector enable --origin tailscale must refuse an installation without Tailscale Serve"
    Assert-Throws { Invoke-FilingBridgePrivateServer -Command connector -Action enable -PublicOrigin "https://public.example.com" -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory 6>$null } "must be 'local' or 'tailscale'" "connector enable must accept only the local or tailscale origin"
    Assert-Throws { Invoke-FilingBridgePrivateServer -Command connector -Action disable -PublicOrigin local -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory 6>$null } 'applies only to' "--origin must be refused outside connector enable"
    Assert-Throws { Invoke-FilingBridgePrivateServer -Command connector -Action open -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory 6>$null } 'connector requires one action' "connector must reject unknown actions"
    Assert-True ((Read-ConnectorFixture).State.connectorEnabled -eq $false) "refused connector commands must not change state"

    $global:FbOperatorCalls.Clear()
    Invoke-FilingBridgePrivateServer -Command connector -Action enable -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory -DryRun 6>$null
    Assert-True (@($global:FbOperatorCalls | Where-Object { $_.mutating }).Count -eq 0 -and (Read-ConnectorFixture).State.connectorEnabled -eq $false) "connector enable dry-run must not change state or containers"

    $global:FbOperatorCalls.Clear()
    $enableOutput = @(Invoke-FilingBridgePrivateServer -Command connector -Action enable -PublicOrigin local -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory 6>&1 | ForEach-Object { [string]$_ })
    $connectorFixture = Read-ConnectorFixture
    Assert-True ($connectorFixture.State.connectorEnabled -eq $true -and $connectorFixture.State.connectorPublicOrigin -eq "http://localhost:35490") "connector enable must record the enabled loopback connector"
    Assert-True ($connectorFixture.Environment -match "(?m)^PRIVATE_CONNECTOR_ENABLED='true'\r?$" -and $connectorFixture.Environment -match "(?m)^PRIVATE_CONNECTOR_PUBLIC_ORIGIN='http://localhost:35490'\r?$") "connector enable must rewrite the environment"
    $enableCalls = @($global:FbOperatorCalls | Where-Object { $_.file -eq "docker" } | ForEach-Object { $_.arguments -join " " })
    Assert-True (@($enableCalls | Where-Object { $_ -match '\bup -d --no-deps --force-recreate --wait --wait-timeout 300 api mcp$' }).Count -eq 1) "connector enable must recreate exactly the API and MCP connector services and wait for health"
    Assert-True (@($enableCalls | Where-Object { $_ -match '\bfrontend\b|\bdb\b' }).Count -eq 0) "connector enable must not recreate the frontend or database"
    Assert-True (($enableOutput -join "`n") -match [regex]::Escape('claude mcp add --transport http filingbridge http://localhost:35490/mcp')) "connector enable must print the Claude Code command"
    Assert-True (($enableOutput -join "`n") -match 'codex mcp login filingbridge') "connector enable must print the Codex sign-in command"
    Assert-True (($enableOutput -join "`n") -match 'Claude\.ai custom connectors are not supported on a Private Server') "connector enable must state that Claude.ai is not supported on a Private Server"

    $global:FbFailDescriptionPattern = '^Enable the MCP connector'
    Assert-Throws {
        Invoke-FilingBridgePrivateServer -Command connector -Action enable -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory 6>$null
    } 'previous connector configuration was restored' "a failed connector enable must restore the previous connector configuration"
    $global:FbFailDescriptionPattern = ""
    Assert-True ((Read-ConnectorFixture).State.connectorEnabled -eq $true) "a failed re-enable must keep the previously recorded connector state"

    $global:FbOperatorCalls.Clear()
    Invoke-FilingBridgePrivateServer -Command tailscale -Action enable -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory 6>$null
    $tailscaleCalls = ($global:FbOperatorCalls | ForEach-Object { $_.description + " " + ($_.arguments -join " ") }) -join "`n"
    Assert-True ($tailscaleCalls -match 'Enable private Tailscale Serve HTTPS') "Tailscale enable must configure Serve"
    Assert-True ($tailscaleCalls -notmatch '\bfunnel\b') "operator must never configure Tailscale Funnel"
    Assert-True ($tailscaleCalls -match 'Apply the exact private HTTPS origin .*--force-recreate api frontend mcp') "Tailscale enable must recreate the MCP connector with the other application services"
    Assert-True ((Read-ConnectorFixture).Environment -match "(?m)^PRIVATE_CONNECTOR_PUBLIC_ORIGIN='http://localhost:35490'\r?$") "Tailscale enable must not move the connector origin by itself"

    $global:FbOperatorCalls.Clear()
    Invoke-FilingBridgePrivateServer -Command connector -Action enable -PublicOrigin tailscale -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory 6>$null
    $connectorFixture = Read-ConnectorFixture
    Assert-True ($connectorFixture.State.connectorPublicOrigin -eq "https://machine.example-tailnet.ts.net") "connector enable --origin tailscale must record the exact Tailscale Serve origin"
    Assert-True ($connectorFixture.Environment -match "(?m)^PRIVATE_CONNECTOR_PUBLIC_ORIGIN='https://machine\.example-tailnet\.ts\.net'\r?$" -and $connectorFixture.Environment -match "(?m)^PRIVATE_CONNECTOR_PUBLIC_HOST='machine\.example-tailnet\.ts\.net'\r?$") "the connector issuer and allowed host must follow the Tailscale origin"
    $tailscaleStatus = @(Invoke-FilingBridgePrivateServer -Command connector -Action status -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory 6>&1 | ForEach-Object { [string]$_ })
    Assert-True (($tailscaleStatus -join "`n") -match 'MCP connector: enabled' -and ($tailscaleStatus -join "`n") -match 'MCP URL: https://machine\.example-tailnet\.ts\.net/mcp') "connector status must report the enabled Tailscale MCP URL"

    Assert-Throws { Invoke-FilingBridgePrivateServer -Command purge-data -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory -Confirmation ("PURGE " + $state.instanceId) -NonInteractive -DryRun 6>$null } 'Disable the recorded Tailscale Serve route before purge' "purge must not orphan a recorded Tailscale Serve route"
    Invoke-FilingBridgePrivateServer -Command tailscale -Action disable -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory 6>$null
    $connectorFixture = Read-ConnectorFixture
    Assert-True ($connectorFixture.State.connectorPublicOrigin -eq "http://localhost:35490" -and $connectorFixture.Environment -match "(?m)^PRIVATE_CONNECTOR_PUBLIC_ORIGIN='http://localhost:35490'\r?$") "disabling Tailscale must return the connector issuer to loopback"

    # The recorded MFA choice must survive every later environment rewrite (connector, Tailscale).
    Assert-Throws { Invoke-FilingBridgePrivateServer -Command mfa -Action maybe -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory 6>$null } 'mfa requires one action' "mfa must reject unknown actions"
    $global:FbOperatorCalls.Clear()
    Invoke-FilingBridgePrivateServer -Command mfa -Action require -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory -DryRun 6>$null
    Assert-True (@($global:FbOperatorCalls | Where-Object { $_.mutating }).Count -eq 0 -and $null -eq (Read-ConnectorFixture).State.PSObject.Properties["requirePrivilegedMfa"]) "mfa require dry-run must not change state or containers"
    $global:FbOperatorCalls.Clear()
    Invoke-FilingBridgePrivateServer -Command mfa -Action require -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory 6>$null
    $mfaFixture = Read-ConnectorFixture
    Assert-True ($mfaFixture.State.requirePrivilegedMfa -eq $true -and $mfaFixture.Environment -match "(?m)^PRIVATE_REQUIRE_PRIVILEGED_MFA='true'\r?$") "mfa require must record the choice and rewrite the environment"
    $mfaCalls = @($global:FbOperatorCalls | Where-Object { $_.file -eq "docker" } | ForEach-Object { $_.arguments -join " " })
    Assert-True (@($mfaCalls | Where-Object { $_ -match '\bup -d --no-deps --force-recreate --wait --wait-timeout 300 api$' }).Count -eq 1) "mfa require must recreate only the API"
    $mfaStatus = @(Invoke-FilingBridgePrivateServer -Command mfa -Action status -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory 6>&1 | ForEach-Object { [string]$_ })
    Assert-True (($mfaStatus -join "`n") -match 'Reviewer: required') "mfa status must report the required setting"

    $global:FbOperatorCalls.Clear()
    Invoke-FilingBridgePrivateServer -Command connector -Action disable -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory 6>$null
    $connectorFixture = Read-ConnectorFixture
    Assert-True ($connectorFixture.State.connectorEnabled -eq $false -and $connectorFixture.Environment -match "(?m)^PRIVATE_CONNECTOR_ENABLED='false'\r?$") "connector disable must turn the connector off"
    Assert-True ($connectorFixture.Environment -match "(?m)^PRIVATE_REQUIRE_PRIVILEGED_MFA='true'\r?$") "a connector change must keep the recorded MFA requirement"
    $disableCalls = @($global:FbOperatorCalls | Where-Object { $_.file -eq "docker" } | ForEach-Object { $_.arguments -join " " })
    Assert-True (@($disableCalls | Where-Object { $_ -match '\bup -d --no-deps --force-recreate --wait --wait-timeout 300 api$' }).Count -eq 1) "connector disable must recreate only the API"

    # The fake Docker seam reports no image identities, so diagnose ends with a failure; only the
    # connector-specific checks it prints first are asserted here.
    $diagnoseOutput = New-Object System.Collections.Generic.List[string]
    try { Invoke-FilingBridgePrivateServer -Command diagnose -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory 6>&1 | ForEach-Object { $diagnoseOutput.Add([string]$_) } } catch { }
    foreach ($diagnosticName in @("secret:accounts_mcp_api_key", "secret:accounts_mcp_api_key_hash", "mcp-service-key-binding", "mcp-connector")) {
        Assert-True (@($diagnoseOutput | Where-Object { $_ -match "^PASS $([regex]::Escape($diagnosticName)):" }).Count -eq 1) "diagnose must check $diagnosticName"
    }
    $validMcpHash = [IO.File]::ReadAllText((Join-Path $state.secretDirectory "accounts_mcp_api_key_hash"), $testUtf8)
    [IO.File]::WriteAllText((Join-Path $state.secretDirectory "accounts_mcp_api_key_hash"), ("0" * 64), [Text.UTF8Encoding]::new($false))
    $brokenDiagnose = New-Object System.Collections.Generic.List[string]
    try { Invoke-FilingBridgePrivateServer -Command diagnose -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory 6>&1 | ForEach-Object { $brokenDiagnose.Add([string]$_) } } catch { }
    Assert-True (@($brokenDiagnose | Where-Object { $_ -match '^FAIL mcp-service-key-binding:' }).Count -eq 1) "diagnose must fail an MCP key that does not match the API hash"
    # The repair the advice names (any environment write) must actually replace the mismatched pair.
    & $privateServerModule { param($directory) Initialize-FbMcpServiceKey -SecretDirectory $directory } $state.secretDirectory
    $repairedMcpKey = [IO.File]::ReadAllText((Join-Path $state.secretDirectory "accounts_mcp_api_key"), $testUtf8).Trim()
    $repairedMcpHash = [IO.File]::ReadAllText((Join-Path $state.secretDirectory "accounts_mcp_api_key_hash"), $testUtf8).Trim()
    Assert-True ($repairedMcpHash -ceq (& $privateServerModule { param($value) Get-FbSha256Text $value } $repairedMcpKey)) "an environment write must repair an MCP key pair whose hash does not match"
    Assert-True ($repairedMcpHash -cne $validMcpHash.Trim()) "the repaired pair must be freshly generated"
    $consistentMcpKeyBefore = $repairedMcpKey
    & $privateServerModule { param($directory) Initialize-FbMcpServiceKey -SecretDirectory $directory } $state.secretDirectory
    Assert-True ([IO.File]::ReadAllText((Join-Path $state.secretDirectory "accounts_mcp_api_key"), $testUtf8).Trim() -ceq $consistentMcpKeyBefore) "a consistent MCP key pair must be left unchanged"

    $legacyConnectorState = ([IO.File]::ReadAllText($statePath, $testUtf8)) | ConvertFrom-Json
    $legacyConnectorStateText = [IO.File]::ReadAllText($statePath, $testUtf8)
    $legacyConnectorState.images.PSObject.Properties.Remove("mcp")
    [IO.File]::WriteAllText($statePath, (($legacyConnectorState | ConvertTo-Json -Depth 12) + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
    Assert-Throws { Invoke-FilingBridgePrivateServer -Command connector -Action enable -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory 6>$null } 'does not run the MCP connector service' "connector enable must refuse a release without the MCP connector service"
    [IO.File]::WriteAllText($statePath, $legacyConnectorStateText, [Text.UTF8Encoding]::new($false))

    $supportDirectory = Join-Path $testRoot "support"
    Invoke-FilingBridgePrivateServer -Command support-bundle -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory -OutputDirectory $supportDirectory -TailLines 25 6>$null
    $supportBundles = @(Get-ChildItem -LiteralPath $supportDirectory -Filter "*.zip" -File)
    Assert-True ($supportBundles.Count -eq 1) "support-bundle must publish one ZIP"
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $supportArchive = [IO.Compression.ZipFile]::OpenRead($supportBundles[0].FullName)
    try {
        $entryNames = @($supportArchive.Entries | ForEach-Object { $_.FullName })
        Assert-True ($entryNames -contains "instance.redacted.json") "support bundle must contain redacted instance state"
        Assert-True ($entryNames -contains "application.redacted.log") "support bundle must contain bounded redacted logs"
        Assert-True ($entryNames -notcontains "private.env") "support bundle must exclude the environment file"
    } finally { $supportArchive.Dispose() }

    Assert-Throws { Invoke-FilingBridgePrivateServer -Command purge-data -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory -Confirmation "PURGE someone-else" -NonInteractive -DryRun 6>$null } 'did not match' "purge must require installation-specific confirmation"
    Invoke-FilingBridgePrivateServer -Command purge-data -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory -Confirmation ("PURGE " + $state.instanceId) -NonInteractive -DryRun 6>$null
    Assert-True (Test-Path -LiteralPath $stateDirectory -PathType Container) "dry-run purge must not remove state"

    Assert-Throws { Invoke-FilingBridgePrivateServer -Command "not-a-command" -RepositoryRoot $repositoryRoot } 'Unknown command' "unknown commands must be rejected"
} finally {
    Remove-Item Env:\FILINGBRIDGE_TEST_BOOT_ID -ErrorAction SilentlyContinue
    Reset-PrivateServerCommandInvoker
    if (Test-Path -LiteralPath $testRoot) {
        $resolved = [IO.Path]::GetFullPath($testRoot)
        $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if ($resolved.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
    }
    Remove-Variable FbOperatorCalls -Scope Global -ErrorAction SilentlyContinue
    Remove-Variable FbFakeServices -Scope Global -ErrorAction SilentlyContinue
    Remove-Variable FbFakeContainerIds -Scope Global -ErrorAction SilentlyContinue
    Remove-Variable FbFakeState -Scope Global -ErrorAction SilentlyContinue
}

# The lock-contention regression intentionally observes a nonzero child native
# exit. Clear that expected probe result so dot-sourcing this successful test in
# a GitHub Actions pwsh step cannot inherit a false process failure.
$global:LASTEXITCODE = 0
Write-Host "Private Server operator contract tests passed."
