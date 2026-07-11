param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-ThrowsContaining([scriptblock]$Action, [string]$ExpectedText) {
    try {
        & $Action
    } catch {
        if ($_.Exception.Message.IndexOf($ExpectedText, [StringComparison]::OrdinalIgnoreCase) -ge 0) { return }
        throw "Expected failure containing '$ExpectedText', received: $($_.Exception.Message)"
    }
    throw "Expected failure containing '$ExpectedText', but the action succeeded."
}

$smokeScriptPath = Join-Path $PSScriptRoot "smoke-production.ps1"
$tokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile(
    $smokeScriptPath,
    [ref]$tokens,
    [ref]$parseErrors)
Assert-True ($parseErrors.Count -eq 0) "smoke-production.ps1 must parse before the handoff writer is tested."
$writerAst = $ast.FindAll({
    param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -ceq "Write-EphemeralMfaHandoff"
}, $true) | Select-Object -First 1
Assert-True ($null -ne $writerAst) "smoke-production.ps1 must define Write-EphemeralMfaHandoff."
. ([scriptblock]::Create($writerAst.Extent.Text))

$originalRunnerTemp = $env:RUNNER_TEMP
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("accounts-smoke-mfa-writer-" + [Guid]::NewGuid().ToString("N"))
$outsideRoot = Join-Path ([IO.Path]::GetTempPath()) ("accounts-smoke-mfa-outside-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
New-Item -ItemType Directory -Path $outsideRoot | Out-Null

try {
    $script:AllowEphemeralMfaEnrollment = $true
    $env:RUNNER_TEMP = if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
        $temporaryRoot.ToUpperInvariant()
    } else {
        $temporaryRoot
    }
    $secret = "JBSWY3DPEHPK3PXP"
    $handoffPath = Join-Path $temporaryRoot "nested/totp-handoff.json"

    Write-EphemeralMfaHandoff $handoffPath $secret 42
    Assert-True (Test-Path -LiteralPath $handoffPath -PathType Leaf) "The handoff writer must create its file."
    $payload = Get-Content -LiteralPath $handoffPath -Raw | ConvertFrom-Json
    Assert-True ($payload.schemaVersion -ceq "accounts-visual-mfa-handoff-v1") "The handoff schema version must be exact."
    Assert-True ($payload.secret -ceq $secret -and [int64]$payload.lastAcceptedCounter -eq 42) "The handoff payload must retain the disposable secret and accepted counter."
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        $expectedMode = [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite
        Assert-True ([IO.File]::GetUnixFileMode($handoffPath) -eq $expectedMode) "The handoff file must be created atomically with mode 0600."
    }

    $originalPayload = Get-Content -LiteralPath $handoffPath -Raw
    Assert-ThrowsContaining {
        Write-EphemeralMfaHandoff $handoffPath "KRUGS4ZANFZSAYJA" 43
    } "exists"
    Assert-True ((Get-Content -LiteralPath $handoffPath -Raw) -ceq $originalPayload) "A stale handoff must never be overwritten."

    $outsidePath = Join-Path $outsideRoot "outside.json"
    Assert-ThrowsContaining {
        Write-EphemeralMfaHandoff $outsidePath $secret 44
    } "inside RUNNER_TEMP"
    Assert-True (-not (Test-Path -LiteralPath $outsidePath)) "An outside-root handoff must not be created."

    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        $linkTarget = Join-Path $outsideRoot "link-target"
        New-Item -ItemType Directory -Path $linkTarget | Out-Null
        $linkPath = Join-Path $temporaryRoot "linked"
        New-Item -ItemType SymbolicLink -Path $linkPath -Target $linkTarget | Out-Null
        Assert-ThrowsContaining {
            Write-EphemeralMfaHandoff (Join-Path $linkPath "escaped.json") $secret 45
        } "filesystem link or junction"
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $linkTarget "escaped.json"))) "A linked parent must not receive the handoff."
    }
} finally {
    $env:RUNNER_TEMP = $originalRunnerTemp
    foreach ($path in @($temporaryRoot, $outsideRoot)) {
        $resolved = [IO.Path]::GetFullPath($path)
        $systemTemporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        Assert-True ($resolved.StartsWith($systemTemporaryRoot, [StringComparison]::OrdinalIgnoreCase)) "Refusing to remove a test path outside the operating-system temporary directory."
        Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "Smoke MFA handoff writer regression tests passed."
