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
$reportWriterAst = $ast.FindAll({
    param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -ceq "Write-OwnerWorkflowReport"
}, $true) | Select-Object -First 1
Assert-True ($null -ne $reportWriterAst) "smoke-production.ps1 must define Write-OwnerWorkflowReport."
. ([scriptblock]::Create($reportWriterAst.Extent.Text))
$retainedInitializerAst = $ast.FindAll({
    param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -ceq "Initialize-RetainedMfaHandoff"
}, $true) | Select-Object -First 1
Assert-True ($null -ne $retainedInitializerAst) "smoke-production.ps1 must define Initialize-RetainedMfaHandoff."
. ([scriptblock]::Create($retainedInitializerAst.Extent.Text))
$retainedReserveAst = $ast.FindAll({
    param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -ceq "Reserve-RetainedMfaHandoff"
}, $true) | Select-Object -First 1
Assert-True ($null -ne $retainedReserveAst) "smoke-production.ps1 must define Reserve-RetainedMfaHandoff."
. ([scriptblock]::Create($retainedReserveAst.Extent.Text))
$retainedCompleterAst = $ast.FindAll({
    param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -ceq "Complete-RetainedMfaHandoff"
}, $true) | Select-Object -First 1
Assert-True ($null -ne $retainedCompleterAst) "smoke-production.ps1 must define Complete-RetainedMfaHandoff."
. ([scriptblock]::Create($retainedCompleterAst.Extent.Text))

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

    $reportPath = Join-Path $temporaryRoot "owner-workflow-report.json"
    $writtenReport = Write-OwnerWorkflowReport $reportPath ([ordered]@{
        schemaVersion = "filingbridge.private-server.owner-workflow/v1"
        status = "passed"
        passwordRotated = $true
        mfaVerified = $true
    })
    Assert-True ($writtenReport -ceq [IO.Path]::GetFullPath($reportPath)) "The report writer must return the exact retained evidence path."
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    Assert-True ($report.schemaVersion -ceq "filingbridge.private-server.owner-workflow/v1" -and $report.passwordRotated -eq $true -and $report.mfaVerified -eq $true) "The retained Owner report must preserve password and MFA evidence."
    Assert-ThrowsContaining {
        Write-OwnerWorkflowReport $reportPath ([ordered]@{ status = "replacement" })
    } "already exists"
    Assert-True (((Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json).schemaVersion) -ceq "filingbridge.private-server.owner-workflow/v1") "Owner evidence must never be overwritten."

    $script:AllowRetainedMfaEnrollment = $true
    $retainedPath = Join-Path $temporaryRoot "retained-owner-mfa/owner-mfa.json"
    $retainedCodes = 1..10 | ForEach-Object { "RECOVERY-CODE-{0:D2}" -f $_ }
    Reserve-RetainedMfaHandoff $retainedPath | Out-Null
    Initialize-RetainedMfaHandoff $retainedPath "JBSWY3DPEHPK3PXP" 46 | Out-Null
    $pendingRetained = Get-Content -LiteralPath $retainedPath -Raw | ConvertFrom-Json
    Assert-True ($pendingRetained.status -ceq "pending" -and $pendingRetained.secret -ceq "JBSWY3DPEHPK3PXP" -and @($pendingRetained.recoveryCodes).Count -eq 0) "Pending retained MFA handoff must preserve the seed before enrollment is committed."
    Complete-RetainedMfaHandoff $retainedPath $retainedCodes | Out-Null
    $retained = Get-Content -LiteralPath $retainedPath -Raw | ConvertFrom-Json
    Assert-True ($retained.schemaVersion -ceq "filingbridge.private-server.owner-mfa-handoff/v1") "Retained MFA handoff must use the exact schema."
    Assert-True ($retained.status -ceq "complete" -and $retained.secret -ceq "JBSWY3DPEHPK3PXP" -and @($retained.recoveryCodes).Count -eq 10) "Retained MFA handoff must preserve the enrollment seed and unique recovery codes."
    Assert-ThrowsContaining {
        Reserve-RetainedMfaHandoff (Join-Path $temporaryRoot "retained-owner-mfa/second.json")
    } "new dedicated parent directory"

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
