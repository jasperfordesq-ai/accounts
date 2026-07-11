param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw $Message
    }
}

function Assert-ThrowsContaining([scriptblock]$Action, [string]$ExpectedText) {
    try {
        & $Action
    } catch {
        if ($_.Exception.Message.IndexOf($ExpectedText, [StringComparison]::Ordinal) -ge 0) {
            return
        }
        throw "Expected failure containing '$ExpectedText', received: $($_.Exception.Message)"
    }

    throw "Expected failure containing '$ExpectedText', but the action succeeded."
}

$backupScriptPath = Join-Path $PSScriptRoot "backup-postgres.ps1"
$restoreScriptPath = Join-Path $PSScriptRoot "restore-postgres.ps1"
$verifyScriptPath = Join-Path $PSScriptRoot "verify-postgres-backup.ps1"
$releasePackScriptPath = Join-Path $PSScriptRoot "verify-release-artifact-pack.ps1"
$machinePackScriptPath = Join-Path $PSScriptRoot "verify-ci-machine-evidence-pack.ps1"
$scriptPaths = @($backupScriptPath, $restoreScriptPath, $verifyScriptPath, $releasePackScriptPath, $machinePackScriptPath)

foreach ($scriptPath in $scriptPaths) {
    $tokens = $null
    $parseErrors = $null
    [Management.Automation.Language.Parser]::ParseFile(
        $scriptPath,
        [ref]$tokens,
        [ref]$parseErrors) | Out-Null
    Assert-True ($parseErrors.Count -eq 0) "$([IO.Path]::GetFileName($scriptPath)) must parse without PowerShell errors."
}

$invalidCommitSha = "A" * 40
$validRunUrl = "https://github.com/example/accounts/actions/runs/123456"
Assert-ThrowsContaining {
    & $backupScriptPath `
        -OutputDirectory (Join-Path ([IO.Path]::GetTempPath()) "unused-backup-output") `
        -Database "accounts" `
        -User "accounts" `
        -ReleaseCandidate $invalidCommitSha `
        -AllowUnencryptedBackupForLocalDryRun
} "ReleaseCandidate must be a full lowercase 40-character hexadecimal Git commit SHA."

Assert-ThrowsContaining {
    & $verifyScriptPath `
        -BackupPath (Join-Path ([IO.Path]::GetTempPath()) "missing.dump.cms") `
        -SourceDatabase "accounts" `
        -User "accounts" `
        -ReleaseCandidate $invalidCommitSha `
        -GitHubActionsRunUrl $validRunUrl
} "ReleaseCandidate must be a full lowercase 40-character hexadecimal Git commit SHA."

$restoreScript = Get-Content -LiteralPath $restoreScriptPath -Raw
$directoryModeIndex = $restoreScript.IndexOf('Set-UnixFileMode $temporaryDecryptDirectory "700"', [StringComparison]::Ordinal)
$fileCreateIndex = $restoreScript.IndexOf('New-Item -ItemType File -Path $restoreSourcePath', [StringComparison]::Ordinal)
$fileModeIndex = $restoreScript.IndexOf('Set-UnixFileMode $restoreSourcePath "600"', [StringComparison]::Ordinal)
$decryptIndex = $restoreScript.IndexOf('& $openssl cms -decrypt', [StringComparison]::Ordinal)
Assert-True ($directoryModeIndex -ge 0 -and $directoryModeIndex -lt $decryptIndex) "The Unix decrypt directory must be chmod 0700 before OpenSSL writes."
Assert-True ($fileCreateIndex -ge 0 -and $fileCreateIndex -lt $fileModeIndex) "The Unix decrypted file must be precreated before chmod."
Assert-True ($fileModeIndex -ge 0 -and $fileModeIndex -lt $decryptIndex) "The Unix decrypted file must be chmod 0600 before OpenSSL writes."
Assert-True ($restoreScript.Contains('Remove-TemporaryDecryptDirectory $temporaryDecryptDirectory')) "Decrypt failure and completion paths must remove the temporary directory."
Assert-True ($restoreScript.Contains('Remove-Item -LiteralPath $resolvedTemporaryDirectory -Recurse -Force -ErrorAction Stop')) "Decrypted host cleanup failures must be release-blocking."
Assert-True (-not $restoreScript.Contains('Remove-Item -LiteralPath $resolvedTemporaryDirectory -Recurse -Force -ErrorAction SilentlyContinue')) "Decrypted host cleanup must not suppress deletion failures."
$copyAttemptIndex = $restoreScript.IndexOf('$containerCopyAttempted = $true', [StringComparison]::Ordinal)
$containerCopyIndex = $restoreScript.IndexOf('Invoke-NativeCommand "Copy PostgreSQL backup into container"', [StringComparison]::Ordinal)
Assert-True ($copyAttemptIndex -ge 0 -and $copyAttemptIndex -lt $containerCopyIndex) "Container cleanup must be armed before a restore copy can partially fail."

$backupScript = Get-Content -LiteralPath $backupScriptPath -Raw
$backupStagingHelperIndex = $backupScript.IndexOf('function Remove-PrivateBackupStagingDirectory', [StringComparison]::Ordinal)
$backupStagingAssignmentIndex = $backupScript.IndexOf('$temporaryStagingDirectory =', [StringComparison]::Ordinal)
$backupDirectoryModeIndex = $backupScript.IndexOf('Set-UnixFileMode $temporaryStagingDirectory "700"', [StringComparison]::Ordinal)
$backupFileModeIndex = $backupScript.IndexOf('Set-UnixFileMode $backupPath "600"', [StringComparison]::Ordinal)
$backupCopyIndex = $backupScript.IndexOf('Invoke-NativeCommand "Copy PostgreSQL backup out of container"', [StringComparison]::Ordinal)
Assert-True ($backupStagingHelperIndex -ge 0 -and $backupStagingHelperIndex -lt $backupStagingAssignmentIndex) "Backup staging cleanup must be defined before operational calls."
Assert-True ($backupDirectoryModeIndex -ge 0 -and $backupDirectoryModeIndex -lt $backupCopyIndex) "The plaintext backup staging directory must be chmod 0700 before copy."
Assert-True ($backupFileModeIndex -ge 0 -and $backupFileModeIndex -lt $backupCopyIndex) "The plaintext backup staging file must be chmod 0600 before copy."

$releaseTokens = $null
$releaseParseErrors = $null
$releaseAst = [Management.Automation.Language.Parser]::ParseFile(
    $releasePackScriptPath,
    [ref]$releaseTokens,
    [ref]$releaseParseErrors)
$requiredFunctions = @("Add-Failure", "Get-JsonProperty", "Get-FileSha256")
foreach ($functionName in $requiredFunctions) {
    $functionAst = $releaseAst.FindAll({
        param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq $functionName
    }, $true) | Select-Object -First 1
    Assert-True ($null -ne $functionAst) "Release-pack verifier must define $functionName."
    . ([scriptblock]::Create($functionAst.Extent.Text))
}

$machineTokens = $null
$machineParseErrors = $null
$machineAst = [Management.Automation.Language.Parser]::ParseFile(
    $machinePackScriptPath,
    [ref]$machineTokens,
    [ref]$machineParseErrors)
$releaseLinkageAst = $releaseAst.FindAll({
    param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq "Assert-RestoreArtifactLinkage"
}, $true) | Select-Object -First 1
$machineLinkageAst = $machineAst.FindAll({
    param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq "Assert-RestoreArtifactLinkage"
}, $true) | Select-Object -First 1
Assert-True ($null -ne $releaseLinkageAst) "Release-pack verifier must define Assert-RestoreArtifactLinkage."
Assert-True ($null -ne $machineLinkageAst) "Machine-pack verifier must define Assert-RestoreArtifactLinkage."
Assert-True ($releaseLinkageAst.Extent.Text -ceq $machineLinkageAst.Extent.Text) "Release and machine evidence packs must retain exactly equivalent restore-artifact linkage contracts."

$releaseRootLinkAst = $releaseAst.FindAll({
    param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq "Assert-EvidenceDirectoryIsNotLink"
}, $true) | Select-Object -First 1
$machineRootLinkAst = $machineAst.FindAll({
    param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq "Assert-EvidenceDirectoryIsNotLink"
}, $true) | Select-Object -First 1
Assert-True ($null -ne $releaseRootLinkAst) "Release-pack verifier must define Assert-EvidenceDirectoryIsNotLink."
Assert-True ($null -ne $machineRootLinkAst) "Machine-pack verifier must define Assert-EvidenceDirectoryIsNotLink."
Assert-True ($releaseRootLinkAst.Extent.Text -ceq $machineRootLinkAst.Extent.Text) "Release and machine evidence packs must reject a linked evidence root identically."
. ([scriptblock]::Create($releaseRootLinkAst.Extent.Text))

$releaseLinkageDefinition = $releaseLinkageAst.Extent.Text.Replace(
    "function Assert-RestoreArtifactLinkage",
    "function Assert-ReleaseRestoreArtifactLinkage")
$machineLinkageDefinition = $machineLinkageAst.Extent.Text.Replace(
    "function Assert-RestoreArtifactLinkage",
    "function Assert-MachineRestoreArtifactLinkage")
. ([scriptblock]::Create($releaseLinkageDefinition))
. ([scriptblock]::Create($machineLinkageDefinition))

function Assert-RestoreArtifactLinkage {
    param(
        [object]$RestoreEvidence,
        [string]$Directory,
        [string]$ExpectedCommitSha,
        [string]$ExpectedRunUrl,
        [Collections.Generic.List[string]]$Failures
    )

    $releaseFailures = [Collections.Generic.List[string]]::new()
    $machineFailures = [Collections.Generic.List[string]]::new()
    Assert-ReleaseRestoreArtifactLinkage $RestoreEvidence $Directory $ExpectedCommitSha $ExpectedRunUrl $releaseFailures
    Assert-MachineRestoreArtifactLinkage $RestoreEvidence $Directory $ExpectedCommitSha $ExpectedRunUrl $machineFailures
    Assert-True ($releaseFailures.Count -eq $machineFailures.Count) "Release and machine restore-artifact linkage contracts returned different failure counts."
    for ($index = 0; $index -lt $releaseFailures.Count; $index++) {
        Assert-True ($releaseFailures[$index] -ceq $machineFailures[$index]) "Release and machine restore-artifact linkage contracts returned different failures."
        $Failures.Add($releaseFailures[$index]) | Out-Null
    }
}

$testDirectory = Join-Path ([IO.Path]::GetTempPath()) ("accounts-backup-evidence-test-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $testDirectory | Out-Null

try {
    $commitSha = "0123456789abcdef0123456789abcdef01234567"
    $backupName = "accounts-20260711-120000.dump.cms"
    $backupPath = Join-Path $testDirectory $backupName
    $checksumPath = "$backupPath.sha256"
    $manifestPath = "$backupPath.manifest.json"
    $utf8NoBom = [Text.UTF8Encoding]::new($false)
    $ascii = [Text.ASCIIEncoding]::new()

    $backupBytes = $utf8NoBom.GetBytes("synthetic encrypted PostgreSQL backup evidence")
    [IO.File]::WriteAllBytes($backupPath, $backupBytes)
    $backupSha256 = (Get-FileHash -LiteralPath $backupPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksumLine = "$backupSha256  $backupName"
    [IO.File]::WriteAllText($checksumPath, $checksumLine, $ascii)
    Assert-True ([IO.File]::ReadAllText($checksumPath) -ceq $checksumLine) "Checksum fixtures must preserve the production no-newline sidecar format."

    $syntheticBackupCreatedAtUtc = [DateTimeOffset]::UtcNow.AddMinutes(-2)
    $syntheticDrillStartedAtUtc = $syntheticBackupCreatedAtUtc.AddSeconds(30)
    $syntheticDrillCompletedAtUtc = $syntheticDrillStartedAtUtc.AddSeconds(5)

    $manifest = [ordered]@{
        formatVersion = 1
        status = "created"
        createdAtUtc = $syntheticBackupCreatedAtUtc.ToString("o")
        database = "accounts"
        environment = "production"
        releaseCandidate = $commitSha
        backupFileName = $backupName
        backupSha256 = $backupSha256
        byteSize = $backupBytes.Length
        encrypted = $true
        encryptionAlgorithm = "CMS/AES-256-CBC"
        encryptionCertificateFileSha256 = "1" * 64
        plaintextDumpRetained = $false
    }
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 4), $utf8NoBom)

    $restoreEvidence = [pscustomobject]@{
        status = "passed"
        completedAtUtc = $syntheticDrillCompletedAtUtc.ToString("o")
        releaseCandidate = $commitSha
        githubActionsRunUrl = $validRunUrl
        backupFileName = $backupName
        backupByteSize = $backupBytes.Length
        backupSha256 = $backupSha256
        backupChecksumFileName = [IO.Path]::GetFileName($checksumPath)
        backupChecksumSha256 = (Get-FileHash -LiteralPath $checksumPath -Algorithm SHA256).Hash.ToLowerInvariant()
        backupManifestFileName = [IO.Path]::GetFileName($manifestPath)
        backupManifestSha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
        backupManifestReleaseCandidate = $commitSha
        recoveryMetrics = [pscustomobject]@{
            backupCreatedAtUtc = $syntheticBackupCreatedAtUtc.ToString("o")
            drillStartedAtUtc = $syntheticDrillStartedAtUtc.ToString("o")
            drillCompletedAtUtc = $syntheticDrillCompletedAtUtc.ToString("o")
            rpoSecondsAtDrill = 30
            rtoSeconds = 5
            rpoTargetSeconds = 86400
            rtoTargetSeconds = 14400
            rpoTargetMet = $true
            rtoTargetMet = $true
        }
    }

    $failures = [Collections.Generic.List[string]]::new()
    Assert-RestoreArtifactLinkage $restoreEvidence $testDirectory $commitSha $validRunUrl $failures
    Assert-True ($failures.Count -eq 0) "A complete, hash-bound backup evidence set must pass linkage verification: $($failures -join '; ')"

    $linkedRootTarget = Join-Path ([IO.Path]::GetTempPath()) ("accounts-evidence-root-target-" + [Guid]::NewGuid().ToString("N"))
    $linkedRootPath = Join-Path ([IO.Path]::GetTempPath()) ("accounts-evidence-root-link-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $linkedRootTarget | Out-Null
    try {
        if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
            New-Item -ItemType Junction -Path $linkedRootPath -Target $linkedRootTarget | Out-Null
        } else {
            New-Item -ItemType SymbolicLink -Path $linkedRootPath -Target $linkedRootTarget | Out-Null
        }
        Assert-ThrowsContaining {
            Assert-EvidenceDirectoryIsNotLink $linkedRootPath
        } "EvidenceDirectory must be a self-contained directory and must not itself be a filesystem link."
    } finally {
        if (Test-Path -LiteralPath $linkedRootPath) {
            Remove-Item -LiteralPath $linkedRootPath -Force
        }
        Remove-Item -LiteralPath $linkedRootTarget -Force
    }

    $duplicatePath = Join-Path $testDirectory "duplicate.DUMP.CMS"
    [IO.File]::WriteAllBytes($duplicatePath, $backupBytes)
    $failures = [Collections.Generic.List[string]]::new()
    Assert-RestoreArtifactLinkage $restoreEvidence $testDirectory $commitSha $validRunUrl $failures
    Assert-True (@($failures | Where-Object { $_ -like "*exactly one encrypted PostgreSQL*" }).Count -eq 1) "Duplicate encrypted backups must fail unambiguous-set verification."
    Remove-Item -LiteralPath $duplicatePath -Force

    $plaintextLeakPath = Join-Path $testDirectory "leak.DUMP"
    [IO.File]::WriteAllBytes($plaintextLeakPath, $backupBytes)
    $failures = [Collections.Generic.List[string]]::new()
    Assert-RestoreArtifactLinkage $restoreEvidence $testDirectory $commitSha $validRunUrl $failures
    Assert-True (@($failures | Where-Object { $_ -like "*must not retain plaintext*" }).Count -eq 1) "Case-variant plaintext backups must fail no-plaintext verification."
    Remove-Item -LiteralPath $plaintextLeakPath -Force

    $unsafePlaintextLeakPath = Join-Path $testDirectory "client data.dump"
    [IO.File]::WriteAllBytes($unsafePlaintextLeakPath, $backupBytes)
    $failures = [Collections.Generic.List[string]]::new()
    Assert-RestoreArtifactLinkage $restoreEvidence $testDirectory $commitSha $validRunUrl $failures
    Assert-True (@($failures | Where-Object { $_ -like "*forbidden even when their names are noncanonical*" }).Count -eq 1) "Unsafe-named plaintext backups must not evade extension-wide detection."
    Assert-True (@($failures | Where-Object { $_ -like "*must not retain plaintext*" }).Count -eq 1) "Unsafe-named plaintext backups must fail no-plaintext verification."
    Remove-Item -LiteralPath $unsafePlaintextLeakPath -Force

    $unsafeEncryptedPath = Join-Path $testDirectory "client data.dump.cms"
    [IO.File]::WriteAllBytes($unsafeEncryptedPath, $backupBytes)
    $failures = [Collections.Generic.List[string]]::new()
    Assert-RestoreArtifactLinkage $restoreEvidence $testDirectory $commitSha $validRunUrl $failures
    Assert-True (@($failures | Where-Object { $_ -like "*canonical safe .dump.cms form*" }).Count -eq 1) "Unsafe-named encrypted backups must not evade extension-wide detection."
    Assert-True (@($failures | Where-Object { $_ -like "*exactly one encrypted PostgreSQL*" }).Count -eq 1) "Unsafe-named encrypted backups must count against the one-set invariant."
    Remove-Item -LiteralPath $unsafeEncryptedPath -Force

    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        $linkedEncryptedPath = Join-Path $testDirectory "linked.dump.cms"
        New-Item -ItemType SymbolicLink -Path $linkedEncryptedPath -Target $backupPath | Out-Null
        $failures = [Collections.Generic.List[string]]::new()
        Assert-RestoreArtifactLinkage $restoreEvidence $testDirectory $commitSha $validRunUrl $failures
        Assert-True (@($failures | Where-Object { $_ -like "*self-contained*filesystem links*" }).Count -eq 1) "Linked backup artifacts must be rejected before hashing."
        Remove-Item -LiteralPath $linkedEncryptedPath -Force

        $linkedDirectoryTarget = Join-Path $testDirectory "linked-target"
        $linkedDirectoryPath = Join-Path $testDirectory "linked-directory"
        New-Item -ItemType Directory -Path $linkedDirectoryTarget | Out-Null
        New-Item -ItemType SymbolicLink -Path $linkedDirectoryPath -Target $linkedDirectoryTarget | Out-Null
        $failures = [Collections.Generic.List[string]]::new()
        Assert-RestoreArtifactLinkage $restoreEvidence $testDirectory $commitSha $validRunUrl $failures
        Assert-True (@($failures | Where-Object { $_ -like "*self-contained*filesystem links*" }).Count -eq 1) "Linked evidence directories must be rejected even when they contain no classified backup file."
        Remove-Item -LiteralPath $linkedDirectoryPath -Force
        Remove-Item -LiteralPath $linkedDirectoryTarget -Force
    }

    [IO.File]::WriteAllText($checksumPath, "$checksumLine`n", $ascii)
    $restoreEvidence.backupChecksumSha256 = (Get-FileHash -LiteralPath $checksumPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $failures = [Collections.Generic.List[string]]::new()
    Assert-RestoreArtifactLinkage $restoreEvidence $testDirectory $commitSha $validRunUrl $failures
    Assert-True (@($failures | Where-Object { $_ -like "*exact backup SHA-256 and filename*" }).Count -eq 1) "A newline-tampered checksum sidecar must fail exact-content verification even when its file hash is updated."
    [IO.File]::WriteAllText($checksumPath, $checksumLine, $ascii)
    $restoreEvidence.backupChecksumSha256 = (Get-FileHash -LiteralPath $checksumPath -Algorithm SHA256).Hash.ToLowerInvariant()

    $restoreEvidence.releaseCandidate = "f" * 40
    $failures = [Collections.Generic.List[string]]::new()
    Assert-RestoreArtifactLinkage $restoreEvidence $testDirectory $commitSha $validRunUrl $failures
    Assert-True (@($failures | Where-Object { $_ -like "*exactly match the release-pack commit*" }).Count -eq 1) "A mismatched restore-drill candidate must fail release-pack identity binding."
    Assert-True (@($failures | Where-Object { $_ -like "*same exact release candidate*" }).Count -eq 1) "A mismatched restore-drill candidate must fail manifest identity binding."
    $restoreEvidence.releaseCandidate = $commitSha

    $restoreEvidence.recoveryMetrics.backupCreatedAtUtc = $syntheticDrillStartedAtUtc.AddSeconds(1).ToString("o")
    $restoreEvidence.recoveryMetrics.rpoSecondsAtDrill = -1
    $failures = [Collections.Generic.List[string]]::new()
    Assert-RestoreArtifactLinkage $restoreEvidence $testDirectory $commitSha $validRunUrl $failures
    Assert-True (@($failures | Where-Object { $_ -like "*timestamps must be ordered*" }).Count -eq 1) "Future backup timestamps must fail ordered recovery evidence."
    Assert-True (@($failures | Where-Object { $_ -like "*measurements must be non-negative*" }).Count -eq 1) "Negative RPO measurements must fail recovery evidence."
    $restoreEvidence.recoveryMetrics.backupCreatedAtUtc = $syntheticBackupCreatedAtUtc.ToString("o")
    $restoreEvidence.recoveryMetrics.rpoSecondsAtDrill = 30

    $restoreEvidence.completedAtUtc = $syntheticDrillCompletedAtUtc.AddMilliseconds(1).ToString("o")
    $failures = [Collections.Generic.List[string]]::new()
    Assert-RestoreArtifactLinkage $restoreEvidence $testDirectory $commitSha $validRunUrl $failures
    Assert-True (@($failures | Where-Object { $_ -like "*bind exactly to the retained manifest creation time and report completion time*" }).Count -eq 1) "Top-level completion time must bind to the measured drill completion."
    $restoreEvidence.completedAtUtc = $syntheticDrillCompletedAtUtc.ToString("o")

    $manifest.createdAtUtc = $syntheticBackupCreatedAtUtc.AddSeconds(1).ToString("o")
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 4), $utf8NoBom)
    $restoreEvidence.backupManifestSha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $failures = [Collections.Generic.List[string]]::new()
    Assert-RestoreArtifactLinkage $restoreEvidence $testDirectory $commitSha $validRunUrl $failures
    Assert-True (@($failures | Where-Object { $_ -like "*bind exactly to the retained manifest creation time and report completion time*" }).Count -eq 1) "RPO timing must bind to the retained manifest creation time even when the manifest hash is updated."
    $manifest.createdAtUtc = $syntheticBackupCreatedAtUtc.ToString("o")
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 4), $utf8NoBom)
    $restoreEvidence.backupManifestSha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()

    [IO.File]::WriteAllBytes($backupPath, $utf8NoBom.GetBytes("tampered encrypted backup"))
    $failures = [Collections.Generic.List[string]]::new()
    Assert-RestoreArtifactLinkage $restoreEvidence $testDirectory $commitSha $validRunUrl $failures
    Assert-True (@($failures | Where-Object { $_ -like "*backupSha256 must match*" }).Count -eq 1) "Tampered backup bytes must fail retained-backup hash binding."
} finally {
    $resolvedTestDirectory = [IO.Path]::GetFullPath($testDirectory)
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    Assert-True ($resolvedTestDirectory.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) "Refusing to remove a test path outside the operating-system temporary directory."
    Remove-Item -LiteralPath $resolvedTestDirectory -Recurse -Force -ErrorAction SilentlyContinue
}

$integrationDirectory = Join-Path ([IO.Path]::GetTempPath()) ("accounts-backup-roundtrip-test-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $integrationDirectory | Out-Null
try {
    $opensslCommand = Get-Command openssl -ErrorAction SilentlyContinue
    if ($null -eq $opensslCommand) {
        foreach ($candidate in @(
            "C:\Program Files\Git\usr\bin\openssl.exe",
            "C:\Program Files\Git\mingw64\bin\openssl.exe")) {
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                $opensslCommand = Get-Item -LiteralPath $candidate
                break
            }
        }
    }
    Assert-True ($null -ne $opensslCommand) "OpenSSL is required for the focused backup encryption/decryption round-trip."
    $opensslPath = if ($opensslCommand.PSObject.Properties.Name -contains "Source") {
        $opensslCommand.Source
    } else {
        $opensslCommand.FullName
    }

    $certificatePath = Join-Path $integrationDirectory "backup-certificate.pem"
    $privateKeyPath = Join-Path $integrationDirectory "backup-private-key.pem"
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        & $opensslPath req -x509 -newkey rsa:2048 -nodes `
            -keyout $privateKeyPath `
            -out $certificatePath `
            -subj "/CN=accounts-backup-evidence-test" `
            -days 1 2>$null
        $certificateExitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    Assert-True ($certificateExitCode -eq 0) "OpenSSL must create the focused-test certificate and private key."

    $global:FakeDockerMode = ""
    $global:RestoreContainerCleanupCount = 0
    $global:ObservedBackupStagingDirectoryMode = -1
    $global:ObservedBackupStagingFileMode = -1
    $global:ObservedRestoreStagingDirectoryMode = -1
    $global:ObservedRestoreStagingFileMode = -1
    function global:docker {
        $arguments = @($args | ForEach-Object { [string]$_ })
        $global:LASTEXITCODE = 0
        $cpIndex = [Array]::IndexOf($arguments, "cp")
        if ($cpIndex -ge 0 -and $arguments.Count -gt ($cpIndex + 2)) {
            $copySource = $arguments[$cpIndex + 1]
            $copyTarget = $arguments[$cpIndex + 2]
            if ($copyTarget -cnotmatch '^[A-Za-z0-9_.-]+:/') {
                if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
                    $global:ObservedBackupStagingDirectoryMode = [int][IO.File]::GetUnixFileMode((Split-Path -Parent $copyTarget))
                    $global:ObservedBackupStagingFileMode = [int][IO.File]::GetUnixFileMode($copyTarget)
                }
                [IO.File]::WriteAllBytes($copyTarget, [Text.UTF8Encoding]::new($false).GetBytes("synthetic pg_dump custom-format payload"))
                if ($global:FakeDockerMode -ceq "backup-partial-copy") {
                    $global:LASTEXITCODE = 17
                    return
                }
            } else {
                if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
                    $global:ObservedRestoreStagingDirectoryMode = [int][IO.File]::GetUnixFileMode((Split-Path -Parent $copySource))
                    $global:ObservedRestoreStagingFileMode = [int][IO.File]::GetUnixFileMode($copySource)
                }
                if ($global:FakeDockerMode -ceq "restore-partial-copy") {
                    $global:LASTEXITCODE = 18
                    return
                }
            }
        }

        if ([Array]::IndexOf($arguments, "rm") -ge 0 -and
            @($arguments | Where-Object { $_ -like "/var/lib/postgresql/data/.accounts-restore-*" }).Count -gt 0) {
            $global:RestoreContainerCleanupCount++
            if ($global:FakeDockerMode -ceq "restore-container-cleanup-failure") {
                $global:LASTEXITCODE = 19
                return
            }
        }

        $psqlIndex = [Array]::IndexOf($arguments, "psql")
        if ($psqlIndex -ge 0) {
            $commandIndex = [Array]::IndexOf($arguments, "--command")
            $sql = if ($commandIndex -ge 0 -and $arguments.Count -gt ($commandIndex + 1)) { $arguments[$commandIndex + 1] } else { "" }
            if ($sql.IndexOf("IntegrityHash", [StringComparison]::Ordinal) -ge 0 -or
                $sql.IndexOf("LastIntegrityHash", [StringComparison]::Ordinal) -ge 0) {
                Write-Output "0"
            } elseif ($sql -match '^select count\(\*\)') {
                Write-Output "1"
            } else {
                Write-Output "stable-source-and-restore-value"
            }
        }

    }

    $roundTripCommitSha = "89abcdef0123456789abcdef0123456789abcdef"
    $backupOutputDirectory = Join-Path $integrationDirectory "retained-backup"
    $partialOutputDirectory = Join-Path $integrationDirectory "partial-backup"
    $stagingDirectoriesBefore = @(Get-ChildItem -LiteralPath ([IO.Path]::GetTempPath()) -Directory -Filter "accounts-backup-staging-*" | ForEach-Object { $_.FullName })
    $global:FakeDockerMode = "backup-partial-copy"
    Assert-ThrowsContaining {
        & $backupScriptPath `
            -OutputDirectory $partialOutputDirectory `
            -Database "accounts" `
            -User "accounts" `
            -EncryptionCertificateFile $certificatePath `
            -ReleaseCandidate $roundTripCommitSha
    } "Copy PostgreSQL backup out of container"
    $global:FakeDockerMode = ""
    $stagingDirectoriesAfter = @(Get-ChildItem -LiteralPath ([IO.Path]::GetTempPath()) -Directory -Filter "accounts-backup-staging-*" | ForEach-Object { $_.FullName })
    Assert-True (@($stagingDirectoriesAfter | Where-Object { $stagingDirectoriesBefore -notcontains $_ }).Count -eq 0) "A partial backup copy must not leave a private plaintext staging directory."
    Assert-True (@(Get-ChildItem -LiteralPath $partialOutputDirectory -File -ErrorAction SilentlyContinue).Count -eq 0) "A partial backup copy must not leave plaintext or partial release output."

    & $backupScriptPath `
        -OutputDirectory $backupOutputDirectory `
        -Database "accounts" `
        -User "accounts" `
        -EncryptionCertificateFile $certificatePath `
        -ReleaseCandidate $roundTripCommitSha

    $encryptedBackups = @(Get-ChildItem -LiteralPath $backupOutputDirectory -Filter "*.dump.cms" -File)
    Assert-True ($encryptedBackups.Count -eq 1) "Focused backup creation must retain exactly one encrypted backup."
    Assert-True (@(Get-ChildItem -LiteralPath $backupOutputDirectory -Filter "*.dump" -File).Count -eq 0) "Focused backup creation must remove the plaintext dump."
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        Assert-True ($global:ObservedBackupStagingDirectoryMode -eq [Convert]::ToInt32("700", 8)) "Observed plaintext backup staging directory mode must be 0700."
        Assert-True ($global:ObservedBackupStagingFileMode -eq [Convert]::ToInt32("600", 8)) "Observed plaintext backup staging file mode must be 0600 before copy."
    }
    $retainedBackup = $encryptedBackups[0]
    $retainedChecksum = Get-Item -LiteralPath "$($retainedBackup.FullName).sha256"
    $retainedManifest = Get-Item -LiteralPath "$($retainedBackup.FullName).manifest.json"

    $decryptDirectoriesBefore = @(Get-ChildItem -LiteralPath ([IO.Path]::GetTempPath()) -Directory -Filter "accounts-backup-decrypt-*" | ForEach-Object { $_.FullName })
    $env:RESTORE_CONFIRM = "accounts_restore_verify"
    $global:RestoreContainerCleanupCount = 0
    $global:FakeDockerMode = "restore-partial-copy"
    Assert-ThrowsContaining {
        & $restoreScriptPath `
            -BackupPath $retainedBackup.FullName `
            -TargetDatabase "accounts_restore_verify" `
            -User "accounts" `
            -DecryptionCertificateFile $certificatePath `
            -DecryptionPrivateKeyFile $privateKeyPath `
            -Clean
    } "Copy PostgreSQL backup into container"
    Assert-True ($global:RestoreContainerCleanupCount -eq 1) "A partial restore copy must still attempt encrypted-backup removal inside the database container."
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        Assert-True ($global:ObservedRestoreStagingDirectoryMode -eq [Convert]::ToInt32("700", 8)) "Observed decrypted-backup staging directory mode must be 0700."
        Assert-True ($global:ObservedRestoreStagingFileMode -eq [Convert]::ToInt32("600", 8)) "Observed decrypted-backup staging file mode must be 0600 before container copy."
    }
    $decryptDirectoriesAfter = @(Get-ChildItem -LiteralPath ([IO.Path]::GetTempPath()) -Directory -Filter "accounts-backup-decrypt-*" | ForEach-Object { $_.FullName })
    Assert-True (@($decryptDirectoriesAfter | Where-Object { $decryptDirectoriesBefore -notcontains $_ }).Count -eq 0) "A partial restore copy must not leave a decrypted host staging directory."

    $global:RestoreContainerCleanupCount = 0
    $global:FakeDockerMode = "restore-container-cleanup-failure"
    Assert-ThrowsContaining {
        & $restoreScriptPath `
            -BackupPath $retainedBackup.FullName `
            -TargetDatabase "accounts_restore_verify" `
            -User "accounts" `
            -DecryptionCertificateFile $certificatePath `
            -DecryptionPrivateKeyFile $privateKeyPath `
            -Clean
    } "Remove temporary PostgreSQL backup from container"
    Assert-True ($global:RestoreContainerCleanupCount -eq 1) "A failed container cleanup must be visible to the restore operator."
    $decryptDirectoriesAfter = @(Get-ChildItem -LiteralPath ([IO.Path]::GetTempPath()) -Directory -Filter "accounts-backup-decrypt-*" | ForEach-Object { $_.FullName })
    Assert-True (@($decryptDirectoriesAfter | Where-Object { $decryptDirectoriesBefore -notcontains $_ }).Count -eq 0) "A failed container cleanup must not prevent decrypted host staging cleanup."
    $global:FakeDockerMode = ""

    $restoreReportPath = Join-Path $backupOutputDirectory "restore-drill-report.json"
    & $verifyScriptPath `
        -BackupPath $encryptedBackups[0].FullName `
        -SourceDatabase "accounts" `
        -VerifyDatabase "accounts_restore_verify" `
        -User "accounts" `
        -DecryptionCertificateFile $certificatePath `
        -DecryptionPrivateKeyFile $privateKeyPath `
        -EvidencePath $restoreReportPath `
        -ReleaseCandidate $roundTripCommitSha `
        -GitHubActionsRunUrl $validRunUrl

    $restoreReport = Get-Content -LiteralPath $restoreReportPath -Raw | ConvertFrom-Json
    Assert-True ([string]$restoreReport.status -ceq "passed") "Focused restore evidence must pass."
    Assert-True ([string]$restoreReport.releaseCandidate -ceq $roundTripCommitSha) "Restore evidence must retain the exact release candidate."
    Assert-True ([string]$restoreReport.githubActionsRunUrl -ceq $validRunUrl) "Restore evidence must retain the exact Actions run URL."
    Assert-True ([int64]$restoreReport.backupByteSize -eq [int64]$retainedBackup.Length) "Restore evidence must retain the exact encrypted-backup byte size."
    Assert-True ([string]$restoreReport.backupChecksumFileName -ceq $retainedChecksum.Name) "Restore evidence must retain the exact checksum filename."
    Assert-True ([string]$restoreReport.backupChecksumSha256 -ceq (Get-FileSha256 $retainedChecksum.FullName)) "Restore evidence must retain the checksum-file hash."
    Assert-True ([string]$restoreReport.backupManifestSha256 -ceq (Get-FileSha256 $retainedManifest.FullName)) "Restore evidence must retain the manifest-file hash."
    Assert-True ([string]$restoreReport.backupManifestReleaseCandidate -ceq $roundTripCommitSha) "Restore evidence must bind the manifest to the exact release candidate."

    $productionChecksumLine = [IO.File]::ReadAllText($retainedChecksum.FullName)
    [IO.File]::WriteAllText($retainedChecksum.FullName, "$productionChecksumLine`n", $ascii)
    Assert-ThrowsContaining {
        & $verifyScriptPath `
            -BackupPath $retainedBackup.FullName `
            -SourceDatabase "accounts" `
            -VerifyDatabase "accounts_restore_verify" `
            -User "accounts" `
            -DecryptionCertificateFile $certificatePath `
            -DecryptionPrivateKeyFile $privateKeyPath `
            -ReleaseCandidate $roundTripCommitSha `
            -GitHubActionsRunUrl $validRunUrl
    } "Checksum file is not in sha256 format"
    [IO.File]::WriteAllText($retainedChecksum.FullName, $productionChecksumLine, $ascii)

    $productionManifestText = [IO.File]::ReadAllText($retainedManifest.FullName)
    $futureManifest = $productionManifestText | ConvertFrom-Json
    $futureManifest.createdAtUtc = [DateTimeOffset]::UtcNow.AddMinutes(5).ToString("o")
    [IO.File]::WriteAllText($retainedManifest.FullName, ($futureManifest | ConvertTo-Json -Depth 4), $utf8NoBom)
    Assert-ThrowsContaining {
        & $verifyScriptPath `
            -BackupPath $retainedBackup.FullName `
            -SourceDatabase "accounts" `
            -VerifyDatabase "accounts_restore_verify" `
            -User "accounts" `
            -DecryptionCertificateFile $certificatePath `
            -DecryptionPrivateKeyFile $privateKeyPath `
            -ReleaseCandidate $roundTripCommitSha `
            -GitHubActionsRunUrl $validRunUrl
    } "createdAtUtc cannot be later than the restore drill start time"
    [IO.File]::WriteAllText($retainedManifest.FullName, $productionManifestText, $utf8NoBom)
} finally {
    Remove-Item Function:\docker -Force -ErrorAction SilentlyContinue
    $resolvedIntegrationDirectory = [IO.Path]::GetFullPath($integrationDirectory)
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    Assert-True ($resolvedIntegrationDirectory.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) "Refusing to remove an integration-test path outside the operating-system temporary directory."
    Remove-Item -LiteralPath $resolvedIntegrationDirectory -Recurse -Force -ErrorAction SilentlyContinue
}

$verifyScript = Get-Content -LiteralPath $verifyScriptPath -Raw
foreach ($requiredField in @(
    "releaseCandidate = `$ReleaseCandidate",
    "githubActionsRunUrl = `$GitHubActionsRunUrl",
    "backupByteSize = `$backupByteSize",
    "backupChecksumFileName = `$backupChecksumFileName",
    "backupChecksumSha256 = `$backupChecksumSha256",
    "backupManifestSha256 = `$backupManifestSha256",
    "backupManifestReleaseCandidate = [string]`$backupManifest.releaseCandidate")) {
    Assert-True ($verifyScript.Contains($requiredField)) "Restore evidence must retain field expression: $requiredField"
}

Write-Host "PostgreSQL backup evidence regression tests passed."
