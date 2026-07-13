$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("filingbridge-release-tests-" + [Guid]::NewGuid().ToString("N"))
$sourceRoot = Join-Path $testRoot "source"
$evidenceRoot = Join-Path $testRoot "evidence"
$outputRoot = Join-Path $testRoot "output"
$currentPowerShell = (Get-Process -Id $PID).Path

function Copy-TestFile([string]$RelativePath) {
    $source = Join-Path $repositoryRoot $RelativePath
    $destination = Join-Path $sourceRoot $RelativePath
    $parent = Split-Path -Parent $destination
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    Copy-Item -LiteralPath $source -Destination $destination
}

function Invoke-Git([object[]]$Arguments) {
    $previous = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $output = @(& git -C $sourceRoot @Arguments 2>&1)
        $exitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previous
    }
    if ($exitCode -ne 0) {
        throw "Test Git command failed: git $($Arguments -join ' ')`n$($output -join [Environment]::NewLine)"
    }
    return @($output)
}

function New-EvidenceFile([string]$Name, [string]$Content) {
    $path = Join-Path $evidenceRoot $Name
    [IO.File]::WriteAllText($path, $Content, [Text.UTF8Encoding]::new($false))
    $item = Get-Item -LiteralPath $path -Force
    return [pscustomobject][ordered]@{
        fileName = $Name
        byteSize = [long]$item.Length
        sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

function Write-Json([string]$Path, $Value) {
    [IO.File]::WriteAllText(
        $Path,
        (($Value | ConvertTo-Json -Depth 12) + [Environment]::NewLine),
        [Text.UTF8Encoding]::new($false))
}

function Invoke-ChildScript(
    [string]$Executable,
    [string]$ScriptPath,
    [object[]]$Arguments) {
    $previous = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $output = @(& $Executable -NoProfile -ExecutionPolicy Bypass -File $ScriptPath @Arguments 2>&1)
        $exitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previous
    }
    return [pscustomobject]@{ ExitCode = $exitCode; Output = @($output) }
}

function Assert-Throws([scriptblock]$Action, [string]$ExpectedText) {
    try {
        & $Action
    } catch {
        if ($_.Exception.Message -notmatch [Regex]::Escape($ExpectedText)) {
            throw "Expected failure containing '$ExpectedText', got: $($_.Exception.Message)"
        }
        return
    }
    throw "Expected a failure containing '$ExpectedText'."
}

try {
    New-Item -ItemType Directory -Path $sourceRoot, $evidenceRoot, $outputRoot | Out-Null
    $sourceFiles = @(
        "FilingBridge.cmd",
        "filingbridge",
        "compose.private.yml",
        ".env.private.example",
        "scripts/private-server.ps1",
        "scripts/PrivateServer/PrivateServer.psm1",
        "scripts/smoke-production.ps1",
        "scripts/verify-linux-private-host.sh",
        "scripts/build-private-server-release.ps1",
        "scripts/verify-private-server-release.ps1",
        "Docs/deployment/README.md",
        "Docs/deployment/private-server.md",
        "Docs/deployment/private-server-linux.md",
        "Docs/deployment/GOOGLE_CLOUD_PRIVATE_SERVER.md",
        "Docs/deployment/LOCAL_WINDOWS_READINESS.md",
        "Docs/deployment/LINUX_CLOUD_READINESS.md",
        "deploy/private/release-manifest.schema.json",
        "README.md",
        "LICENSE",
        "NOTICE",
        "THIRD_PARTY_NOTICES.md",
        "CONTRIBUTORS.md"
    )
    foreach ($file in $sourceFiles) { Copy-TestFile $file }

    [void](Invoke-Git @("init", "--initial-branch=main"))
    [void](Invoke-Git @("config", "user.email", "release-test@example.invalid"))
    [void](Invoke-Git @("config", "user.name", "Private release test"))
    [void](Invoke-Git @("add", "--all"))
    [void](Invoke-Git @("commit", "--message", "Private release fixture"))
    $commitSha = ((Invoke-Git @("rev-parse", "HEAD")) -join "").Trim()
    $runUrl = "https://github.com/jasperfordesq-ai/accounts/actions/runs/123456"

    $backendTrivy = New-EvidenceFile "backend-trivy.json" '{"Results":[]}'
    $backendSbom = New-EvidenceFile "backend-sbom.spdx.json" '{"spdxVersion":"SPDX-2.3"}'
    $backendProvenance = New-EvidenceFile "backend-provenance.jsonl" '{"verified":true}'
    $frontendTrivy = New-EvidenceFile "frontend-trivy.json" '{"Results":[]}'
    $frontendSbom = New-EvidenceFile "frontend-sbom.spdx.json" '{"spdxVersion":"SPDX-2.3"}'
    $frontendProvenance = New-EvidenceFile "frontend-provenance.jsonl" '{"verified":true}'
    $retained = @(
        $backendTrivy, $backendSbom, $backendProvenance,
        $frontendTrivy, $frontendSbom, $frontendProvenance)
    $backendReference = "ghcr.io/jasperfordesq-ai/accounts-api@sha256:" + ("1" * 64)
    $frontendReference = "ghcr.io/jasperfordesq-ai/accounts-frontend@sha256:" + ("2" * 64)
    $postgresReference = "postgres@sha256:" + ("3" * 64)
    $reportPath = Join-Path $evidenceRoot "container-supply-chain-report.json"
    $report = [ordered]@{
        status = "passed"
        promotionMode = "promoted"
        releaseEligible = $true
        candidate = [ordered]@{ commitSha = $commitSha; githubActionsRunUrl = $runUrl }
        images = @(
            [ordered]@{
                component = "backend"
                exactDigestReference = $backendReference
                scan = [ordered]@{ file = $backendTrivy }
                sbom = [ordered]@{ file = $backendSbom }
                provenance = [ordered]@{ file = $backendProvenance }
            },
            [ordered]@{
                component = "frontend"
                exactDigestReference = $frontendReference
                scan = [ordered]@{ file = $frontendTrivy }
                sbom = [ordered]@{ file = $frontendSbom }
                provenance = [ordered]@{ file = $frontendProvenance }
            })
        blockingFailures = @()
    }
    Write-Json $reportPath $report
    $reportItem = Get-Item -LiteralPath $reportPath
    $verificationPath = Join-Path $evidenceRoot "container-supply-chain-verification-report.json"
    Write-Json $verificationPath ([ordered]@{
        status = "passed"
        allowUnpromoted = $false
        promotionMode = "promoted"
        releaseEligible = $true
        commitSha = $commitSha
        githubActionsRunUrl = $runUrl
        evidenceReport = [ordered]@{
            fileName = $reportItem.Name
            byteSize = [long]$reportItem.Length
            sha256 = (Get-FileHash -LiteralPath $reportPath -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        verifiedImageDigests = @($backendReference, $frontendReference)
        retainedEvidenceFiles = $retained
        failures = @()
    })

    $builder = Join-Path $sourceRoot "scripts/build-private-server-release.ps1"
    $verifier = Join-Path $sourceRoot "scripts/verify-private-server-release.ps1"
    Assert-Throws {
        & $builder -Version "01.0.0" -CandidateCommitSha $commitSha `
            -GitHubActionsRunUrl $runUrl -SupplyChainEvidenceDirectory $evidenceRoot `
            -PostgresImage $postgresReference -OutputDirectory $outputRoot | Out-Null
    } "semantic version"
    $buildResult = & $builder `
        -Version "0.1.0-test.1" `
        -CandidateCommitSha $commitSha `
        -GitHubActionsRunUrl $runUrl `
        -SupplyChainEvidenceDirectory $evidenceRoot `
        -PostgresImage $postgresReference `
        -OutputDirectory $outputRoot | ConvertFrom-Json
    if (-not (Test-Path -LiteralPath $buildResult.archivePath -PathType Leaf)) {
        throw "Private Server release builder did not create the archive."
    }

    $verifyArguments = @(
        "-ArchivePath", [string]$buildResult.archivePath,
        "-ExpectedVersion", "0.1.0-test.1",
        "-ExpectedCommitSha", $commitSha,
        "-ExpectedGitHubActionsRunUrl", $runUrl)
    $verified = Invoke-ChildScript $currentPowerShell $verifier $verifyArguments
    if ($verified.ExitCode -ne 0) {
        throw "Release verifier rejected the valid bundle: $($verified.Output -join [Environment]::NewLine)"
    }

    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
        $windowsPowerShell = (Get-Command powershell.exe -ErrorAction Stop).Source
        $windowsVerified = Invoke-ChildScript $windowsPowerShell $verifier $verifyArguments
        if ($windowsVerified.ExitCode -ne 0) {
            throw "Windows PowerShell 5.1 rejected the valid bundle: $($windowsVerified.Output -join [Environment]::NewLine)"
        }
    }

    $tamperedDirectory = Join-Path $testRoot "tampered"
    Expand-Archive -LiteralPath $buildResult.archivePath -DestinationPath $tamperedDirectory
    $manifestPath = Join-Path $tamperedDirectory "release.json"
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $manifest.images.backend.exactDigestReference = "ghcr.io/attacker/accounts-api@sha256:" + ("9" * 64)
    Write-Json $manifestPath $manifest
    $tamperedArchive = Join-Path $testRoot "tampered.zip"
    [IO.Compression.ZipFile]::CreateFromDirectory($tamperedDirectory, $tamperedArchive)
    $tamperedHash = (Get-FileHash -LiteralPath $tamperedArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText("$tamperedArchive.sha256", "$tamperedHash  tampered.zip`r`n", [Text.Encoding]::ASCII)
    $tamperedResult = Invoke-ChildScript $currentPowerShell $verifier @("-ArchivePath", $tamperedArchive)
    if ($tamperedResult.ExitCode -eq 0 -or ($tamperedResult.Output -join " ") -notmatch "backend image") {
        throw "Release verifier did not reject a substituted backend repository."
    }

    $duplicateCaseArchive = Join-Path $testRoot "duplicate-case.zip"
    $duplicateStream = [IO.File]::Create($duplicateCaseArchive)
    try {
        $duplicateZip = [IO.Compression.ZipArchive]::new(
            $duplicateStream,
            [IO.Compression.ZipArchiveMode]::Create,
            $false)
        try {
            foreach ($entryName in @("Evidence.txt", "evidence.txt")) {
                $entry = $duplicateZip.CreateEntry($entryName)
                $writer = New-Object IO.StreamWriter($entry.Open(), [Text.UTF8Encoding]::new($false))
                try { $writer.Write("duplicate-path-test") } finally { $writer.Dispose() }
            }
        } finally { $duplicateZip.Dispose() }
    } finally { $duplicateStream.Dispose() }
    $duplicateHash = (Get-FileHash -LiteralPath $duplicateCaseArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText(
        "$duplicateCaseArchive.sha256",
        "$duplicateHash  duplicate-case.zip`r`n",
        [Text.Encoding]::ASCII)
    $duplicateResult = Invoke-ChildScript $currentPowerShell $verifier @("-ArchivePath", $duplicateCaseArchive)
    if ($duplicateResult.ExitCode -eq 0 -or ($duplicateResult.Output -join " ") -notmatch "unsafe or duplicate") {
        throw "Release verifier did not reject case-insensitive duplicate archive paths."
    }

    $untrackedPath = Join-Path $sourceRoot "untracked-release-input.txt"
    [IO.File]::WriteAllText($untrackedPath, "dirty")
    Assert-Throws {
        & $builder -Version "0.1.0-test.2" -CandidateCommitSha $commitSha `
            -GitHubActionsRunUrl $runUrl -SupplyChainEvidenceDirectory $evidenceRoot `
            -PostgresImage $postgresReference -OutputDirectory (Join-Path $testRoot "dirty-output") | Out-Null
    } "must be clean"
    Remove-Item -LiteralPath $untrackedPath

    [IO.File]::AppendAllText($reportPath, " ")
    Assert-Throws {
        & $builder -Version "0.1.0-test.3" -CandidateCommitSha $commitSha `
            -GitHubActionsRunUrl $runUrl -SupplyChainEvidenceDirectory $evidenceRoot `
            -PostgresImage $postgresReference -OutputDirectory (Join-Path $testRoot "substituted-evidence-output") | Out-Null
    } "not hash-bound"

    Write-Host "Private Server release builder/verifier integration tests OK"
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        $resolved = [IO.Path]::GetFullPath($testRoot)
        $temporary = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if ($resolved.StartsWith($temporary, [StringComparison]::OrdinalIgnoreCase) -and
            [IO.Path]::GetFileName($resolved).StartsWith("filingbridge-release-tests-", [StringComparison]::Ordinal)) {
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
    }
}
