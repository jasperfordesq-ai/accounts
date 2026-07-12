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
foreach ($command in @("setup", "start", "status", "stop", "logs", "backup", "verify-backup", "restore", "update", "owner-recovery", "tailscale", "diagnose", "support-bundle", "uninstall", "purge-data", "DryRun")) {
    Assert-True ($help -match [regex]::Escape($command)) "help must document $command"
}
Assert-True ($help -match 'does not submit to CRO/ROS') "help must preserve the no-direct-filing boundary"
Assert-True ($help -match 'qualified-accountant') "help must preserve the professional-review boundary"

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("filingbridge-operator-test-" + [Guid]::NewGuid().ToString("N"))
$stateDirectory = Join-Path $testRoot ("state path with spaces " + [char]0x00E1)
$global:FbOperatorCalls = New-Object System.Collections.Generic.List[object]
$global:FbFakeServices = @("db", "api", "frontend")
$global:FbFakeContainerIds = @{
    db = ("d" * 64)
    api = ("a" * 64)
    frontend = ("f" * 64)
}
$global:FbFakeState = $null
$global:FbFakeTailscaleStatus = '{"version":"etag-empty"}'
$global:FbMutateReleaseComposeOnValidate = ""
$fakeInvoker = {
    param($FilePath, $Arguments, $Description, $Mutating)
    $argumentStrings = @($Arguments | ForEach-Object { [string]$_ })
    $global:FbOperatorCalls.Add([pscustomobject]@{ file = $FilePath; arguments = $argumentStrings; description = $Description; mutating = $Mutating })
    if ($Description -in @("Validate the isolated Private Server topology", "Pull exact update image digests") -and -not [string]::IsNullOrWhiteSpace($global:FbMutateReleaseComposeOnValidate)) {
        [IO.File]::AppendAllText($global:FbMutateReleaseComposeOnValidate, "`n# synthetic post-verification source mutation", [Text.UTF8Encoding]::new($false))
        $global:FbMutateReleaseComposeOnValidate = ""
    }
    if ($FilePath -eq "docker" -and $argumentStrings -contains "compose") {
        if ($argumentStrings -contains "down") { $global:FbFakeServices = @() }
        elseif ($argumentStrings -contains "stop") {
            $stopped = @($argumentStrings | Where-Object { $_ -in @("db", "api", "frontend") })
            $global:FbFakeServices = @($global:FbFakeServices | Where-Object { $_ -notin $stopped })
        }
        elseif ($argumentStrings -contains "start" -or $argumentStrings -contains "up") {
            $started = @($argumentStrings | Where-Object { $_ -in @("db", "api", "frontend") })
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
    if ($Description -match '^Resolve existing (db|api|frontend) runtime container$') {
        return [pscustomobject]@{ ExitCode = 0; Output = @($global:FbFakeContainerIds[$Matches[1]]) }
    }
    if ($Description -match '^Verify existing (db|api|frontend) runtime container ownership$') {
        $service = $Matches[1]
        return [pscustomobject]@{
            ExitCode = 0
            Output = @("$($global:FbFakeState.composeProject)|$service|PrivateServer|$($global:FbFakeState.instanceId)")
        }
    }
    if ($Description -eq 'Inspect Private Server containers') {
        return [pscustomobject]@{ ExitCode = 0; Output = @("db", "api", "frontend") }
    }
    if ($Description -eq 'Record current Private Server service state') {
        return [pscustomobject]@{ ExitCode = 0; Output = $global:FbFakeServices }
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
    if ($Description -match '^Read important-table fingerprints from ') {
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

    $releaseRoot = Join-Path $testRoot "synthetic-release"
    New-Item -ItemType Directory -Path $releaseRoot | Out-Null
    $requiredReleaseFiles = @(
        "FilingBridge.cmd", "compose.private.yml", ".env.private.example",
        "scripts/private-server.ps1", "scripts/PrivateServer/PrivateServer.psm1",
        "Docs/deployment/README.md", "Docs/deployment/private-server.md",
        "deploy/private/release-manifest.schema.json", "README.md", "LICENSE", "NOTICE",
        "THIRD_PARTY_NOTICES.md", "CONTRIBUTORS.md")
    foreach ($relative in $requiredReleaseFiles) {
        $destination = Join-Path $releaseRoot $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $repositoryRoot $relative) -Destination $destination
    }
    $releaseManifestObject = [ordered]@{
        schemaVersion = "filingbridge.private-server.release/v1"
        version = "1.2.3"
        generatedAtUtc = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        candidate = [ordered]@{ commitSha = ("a" * 40); githubActionsRunUrl = "https://github.com/jasperfordesq-ai/accounts/actions/runs/1" }
        supportedHosts = @("windows-x64")
        images = [ordered]@{
            backend = [ordered]@{ exactDigestReference = "ghcr.io/jasperfordesq-ai/accounts-api@sha256:$('1' * 64)" }
            frontend = [ordered]@{ exactDigestReference = "ghcr.io/jasperfordesq-ai/accounts-frontend@sha256:$('2' * 64)" }
            postgres = [ordered]@{ exactDigestReference = "postgres@sha256:$('3' * 64)" }
        }
        files = @($requiredReleaseFiles | ForEach-Object {
            $releaseFile = Join-Path $releaseRoot $_
            [ordered]@{
                path = $_.Replace('\', '/')
                byteSize = (Get-Item -LiteralPath $releaseFile).Length
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
    [IO.File]::WriteAllText($syntheticManifestPath, ($releaseManifestObject | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))

    $unicodeTenantName = "Carthanas $([char]0x00D3) C$([char]0x00F3)na$([char]0x00ED)"
    $unicodeOwnerName = "M$([char]0x00E1)ire O'Connor"
    $mainSetupCallStart = $global:FbOperatorCalls.Count
    Invoke-FilingBridgePrivateServer -Command setup -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory -TenantName $unicodeTenantName -OwnerEmail "owner@example.ie" -OwnerName $unicodeOwnerName -BuildLocal -NonInteractive -SkipPrerequisiteChecks 6>$null
    $statePath = Join-Path $stateDirectory "server.json"
    Assert-True (Test-Path -LiteralPath $statePath -PathType Leaf) "setup must create state outside the checkout"
    $state = ([IO.File]::ReadAllText($statePath, $testUtf8)) | ConvertFrom-Json
    $global:FbFakeState = $state
    Assert-True ($state.status -eq "ready") "successful setup must record ready state"
    Assert-True ($state.composeProject -match '^filingbridge-[a-f0-9]{12}$') "setup must generate a unique Compose project"
    $parsedInstanceId = [Guid]::Empty
    Assert-True ([Guid]::TryParse([string]$state.instanceId, [ref]$parsedInstanceId)) "setup must generate an installation GUID"
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot "private.env"))) "setup must not write state into the checkout"
    Assert-True ($state.reviewedRelease -eq $false) "a source build or self-asserted manifest must never be recorded as reviewed"
    Assert-True ($state.releaseIntegrityStatus -eq "source-build-unreviewed") "source build integrity status must be explicit"
    $secretDirectory = Join-Path $stateDirectory "secrets"
    foreach ($cryptoName in @("identity_hmac_key", "mfa_encryption_key", "database_tenant_context_key", "auth_session_signing_key", "audit_integrity_signing_key", "backup_authentication_key")) {
        $value = [IO.File]::ReadAllText((Join-Path $secretDirectory $cryptoName))
        Assert-True ([Convert]::FromBase64String($value).Length -ge 32) "$cryptoName must contain backend-compatible standard Base64"
    }
    $sentinel = [IO.File]::ReadAllText((Join-Path $secretDirectory "private_initial_owner_password"))
    Assert-True ($sentinel -eq "INITIALIZATION-COMPLETE-NO-PASSWORD") "ephemeral Owner password must be erased after setup"
    $envText = [IO.File]::ReadAllText((Join-Path $stateDirectory "private.env"), $testUtf8)
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
        $secondCommandOutput = @(& powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $dispatcherPath stop -StateDirectory $stateDirectory -DryRun 2>&1 | ForEach-Object { [string]$_ })
        $secondCommandExit = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $savedErrorActionPreference
        & $privateServerModule { param($lock) Exit-FbInstallationLock $lock } $heldLock
    }
    Assert-True ($secondCommandExit -ne 0) "a second process must not enter a lifecycle command while the installation lock is held"
    Assert-True (($secondCommandOutput -join "`n") -match '(?s)exclusive lifecycle lock.*no Docker') "lock contention must fail before any Docker or state mutation"

    $global:FbOperatorCalls.Clear()
    Invoke-FilingBridgePrivateServer -Command start -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory 6>$null
    $directStartCalls = @($global:FbOperatorCalls | Where-Object { $_.file -eq "docker" -and $_.arguments.Count -eq 2 -and $_.arguments[0] -eq "start" })
    Assert-True ($directStartCalls.Count -eq 3) "daily start must invoke docker start once for each exact existing runtime container"
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
    Copy-Item -LiteralPath (Join-Path $repositoryRoot "compose.private.yml") -Destination (Join-Path $releaseRoot "compose.private.yml") -Force

    $global:FbOperatorCalls.Clear()
    Invoke-FilingBridgePrivateServer -Command owner-recovery -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory -OwnerEmail "owner@example.ie" -Confirmation "RECOVER PRIVATE SERVER OWNER" -NonInteractive 6>$null
    $recoveryArguments = ($global:FbOperatorCalls | ForEach-Object { $_.arguments -join " " }) -join "`n"
    Assert-True ($recoveryArguments -notmatch 'opaque/test') "raw Owner reset token must never appear in native command arguments"
    Assert-True ($recoveryArguments -match 'private-owner-recovery') "Owner recovery must use the dedicated backend one-shot"
    Assert-True ($recoveryArguments -match "PrivateOwnerRecovery__TenantSlug=$([regex]::Escape([string]$state.tenantSlug))") "Owner recovery must pass the exact .NET configuration key for the protected saved tenant slug"
    Assert-True ($recoveryArguments -match "PrivateOwnerRecovery__ConfirmInstallationId=$([regex]::Escape([string]$state.instanceId))") "Owner recovery must pass the exact .NET installation confirmation key"

    $global:FbOperatorCalls.Clear()
    Invoke-FilingBridgePrivateServer -Command tailscale -Action enable -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory 6>$null
    $tailscaleCalls = ($global:FbOperatorCalls | ForEach-Object { $_.description + " " + ($_.arguments -join " ") }) -join "`n"
    Assert-True ($tailscaleCalls -match 'Enable private Tailscale Serve HTTPS') "Tailscale enable must configure Serve"
    Assert-True ($tailscaleCalls -notmatch '\bfunnel\b') "operator must never configure Tailscale Funnel"
    Assert-Throws { Invoke-FilingBridgePrivateServer -Command purge-data -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory -Confirmation ("PURGE " + $state.instanceId) -NonInteractive -DryRun 6>$null } 'Disable the recorded Tailscale Serve route before purge' "purge must not orphan a recorded Tailscale Serve route"
    Invoke-FilingBridgePrivateServer -Command tailscale -Action disable -RepositoryRoot $repositoryRoot -StateDirectory $stateDirectory 6>$null

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

Write-Host "Private Server operator contract tests passed."
