param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [Parameter(Mandatory = $true)]
    [string]$CandidateCommitSha,
    [Parameter(Mandatory = $true)]
    [string]$GitHubActionsRunUrl,
    [Parameter(Mandatory = $true)]
    [string]$SupplyChainEvidenceDirectory,
    [Parameter(Mandatory = $true)]
    [string]$PostgresImage,
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Assert-ExactImageReference([string]$Value, [string]$Name, [string]$PrefixPattern) {
    if ($Value -cnotmatch "^(?:$PrefixPattern)@sha256:[0-9a-f]{64}$") {
        throw "$Name must be an exact lowercase sha256 image reference."
    }
}

function Copy-ReleaseItem([string]$SourceRoot, [string]$StageRoot, [string]$RelativePath) {
    $source = Join-Path $SourceRoot $RelativePath
    if (-not (Test-Path -LiteralPath $source)) {
        throw "Required Private Server release file is missing: $RelativePath"
    }
    $destination = Join-Path $StageRoot $RelativePath
    $parent = Split-Path -Parent $destination
    if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
    Copy-Item -LiteralPath $source -Destination $destination -Recurse -Force
}

if ($Version -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-(?:0|[1-9][0-9]*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*))*)?$') {
    throw "Version must use semantic version form, for example 0.1.0-preview.1."
}
if ($CandidateCommitSha -cnotmatch '^[0-9a-f]{40}$') {
    throw "CandidateCommitSha must be a full lowercase Git commit SHA."
}
$expectedRunUrl = "https://github.com/jasperfordesq-ai/accounts/actions/runs/"
if (-not $GitHubActionsRunUrl.StartsWith($expectedRunUrl, [StringComparison]::Ordinal) -or
    $GitHubActionsRunUrl.Substring($expectedRunUrl.Length) -notmatch '^[1-9][0-9]*$') {
    throw "GitHubActionsRunUrl must identify an accounts repository Actions run."
}
Assert-ExactImageReference $PostgresImage "PostgresImage" "postgres"

$sourceRoot = Split-Path -Parent $PSScriptRoot
$sourceHead = (& git -C $sourceRoot rev-parse HEAD 2>$null).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceHead -cne $CandidateCommitSha) {
    throw "The release source checkout must be the exact requested candidate commit."
}
$sourceStatus = @(& git -C $sourceRoot status --porcelain=v1 --untracked-files=all 2>$null)
if ($LASTEXITCODE -ne 0 -or $sourceStatus.Count -ne 0) {
    throw "The release source checkout must be clean; refusing to package modified or untracked files."
}
$evidenceDirectory = [IO.Path]::GetFullPath($SupplyChainEvidenceDirectory)
$reportPath = Join-Path $evidenceDirectory "container-supply-chain-report.json"
$verificationPath = Join-Path $evidenceDirectory "container-supply-chain-verification-report.json"
if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $verificationPath -PathType Leaf)) {
    throw "The promoted container-supply-chain report and verification report are required."
}

$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$verification = Get-Content -LiteralPath $verificationPath -Raw | ConvertFrom-Json
if ([string]$report.status -ne "passed" -or [string]$report.promotionMode -ne "promoted" -or
    $report.releaseEligible -ne $true -or @($report.blockingFailures).Count -ne 0) {
    throw "The container supply-chain report is not a release-eligible promoted report."
}
if ([string]$verification.status -ne "passed" -or $verification.releaseEligible -ne $true -or
    $verification.allowUnpromoted -eq $true -or
    [string]$verification.promotionMode -ne "promoted" -or
    @($verification.failures).Count -ne 0) {
    throw "The container supply-chain verification report is not a strict promoted verification."
}
if ([string]$report.candidate.commitSha -cne $CandidateCommitSha -or
    [string]$report.candidate.githubActionsRunUrl -cne $GitHubActionsRunUrl -or
    [string]$verification.commitSha -cne $CandidateCommitSha -or
    [string]$verification.githubActionsRunUrl -cne $GitHubActionsRunUrl) {
    throw "Container supply-chain candidate identity does not match the requested release."
}

$reportItem = Get-Item -LiteralPath $reportPath
$reportHash = (Get-FileHash -LiteralPath $reportPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ([string]$verification.evidenceReport.fileName -cne "container-supply-chain-report.json" -or
    [long]$verification.evidenceReport.byteSize -ne $reportItem.Length -or
    [string]$verification.evidenceReport.sha256 -cne $reportHash) {
    throw "The verification report is not hash-bound to the supplied container supply-chain report."
}

$backend = @($report.images | Where-Object component -eq "backend")
$frontend = @($report.images | Where-Object component -eq "frontend")
if ($backend.Count -ne 1 -or $frontend.Count -ne 1) {
    throw "Container supply-chain evidence must contain exactly one backend and one frontend image."
}
$backendReference = [string]$backend[0].exactDigestReference
$frontendReference = [string]$frontend[0].exactDigestReference
Assert-ExactImageReference $backendReference "Backend image" "ghcr\.io/jasperfordesq-ai/accounts-api"
Assert-ExactImageReference $frontendReference "Frontend image" "ghcr\.io/jasperfordesq-ai/accounts-frontend"

$verifiedDigests = @($verification.verifiedImageDigests)
if ($verifiedDigests.Count -ne 2 -or
    $verifiedDigests -cnotcontains $backendReference -or
    $verifiedDigests -cnotcontains $frontendReference) {
    throw "The verification report does not bind exactly the selected backend and frontend image digests."
}

$retainedEvidence = @($verification.retainedEvidenceFiles)
if ($retainedEvidence.Count -lt 6) {
    throw "The verification report does not retain the required scan, SBOM, and provenance evidence."
}
$retainedNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($entry in $retainedEvidence) {
    $fileName = [string]$entry.fileName
    if ([string]::IsNullOrWhiteSpace($fileName) -or
        [IO.Path]::GetFileName($fileName) -cne $fileName -or
        -not $retainedNames.Add($fileName)) {
        throw "The verification report contains an unsafe or duplicate retained evidence file name."
    }
    $path = Join-Path $evidenceDirectory $fileName
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Verified supply-chain evidence is missing: $fileName"
    }
    $item = Get-Item -LiteralPath $path -Force
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ([long]$entry.byteSize -ne $item.Length -or [string]$entry.sha256 -cne $hash) {
        throw "Verified supply-chain evidence hash/size mismatch: $fileName"
    }
}

foreach ($image in @($backend[0], $frontend[0])) {
    foreach ($evidence in @($image.scan.file, $image.sbom.file, $image.provenance.file)) {
        $fileName = [string]$evidence.fileName
        if (-not $retainedNames.Contains($fileName)) {
            throw "Selected image evidence is absent from the verification report: $fileName"
        }
        $verifiedEvidence = @($retainedEvidence | Where-Object fileName -CEQ $fileName)
        if ($verifiedEvidence.Count -ne 1 -or
            [long]$verifiedEvidence[0].byteSize -ne [long]$evidence.byteSize -or
            [string]$verifiedEvidence[0].sha256 -cne [string]$evidence.sha256) {
            throw "Selected image evidence does not match the verification manifest: $fileName"
        }
    }
}

$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("filingbridge-private-release-" + [Guid]::NewGuid().ToString("N"))
$stageRoot = Join-Path $temporaryRoot "payload"
New-Item -ItemType Directory -Force -Path $stageRoot | Out-Null

try {
    $requiredItems = @(
        "FilingBridge.cmd",
        "compose.private.yml",
        ".env.private.example",
        "scripts/private-server.ps1",
        "scripts/PrivateServer/PrivateServer.psm1",
        "Docs/deployment/README.md",
        "Docs/deployment/private-server.md",
        "deploy/private/release-manifest.schema.json",
        "README.md",
        "LICENSE",
        "NOTICE",
        "THIRD_PARTY_NOTICES.md",
        "CONTRIBUTORS.md"
    )
    foreach ($item in $requiredItems) { Copy-ReleaseItem $sourceRoot $stageRoot $item }

    $evidenceStage = Join-Path $stageRoot "evidence/container-supply-chain"
    New-Item -ItemType Directory -Force -Path $evidenceStage | Out-Null
    foreach ($evidenceFile in @($reportItem, (Get-Item -LiteralPath $verificationPath))) {
        Copy-Item -LiteralPath $evidenceFile.FullName -Destination (Join-Path $evidenceStage $evidenceFile.Name)
    }
    foreach ($entry in $retainedEvidence) {
        $fileName = [string]$entry.fileName
        Copy-Item -LiteralPath (Join-Path $evidenceDirectory $fileName) -Destination (Join-Path $evidenceStage $fileName)
    }

    $files = @(Get-ChildItem -LiteralPath $stageRoot -File -Recurse -Force | Sort-Object FullName | ForEach-Object {
        $relative = $_.FullName.Substring($stageRoot.TrimEnd('\', '/').Length).TrimStart('\', '/').Replace('\', '/')
        [ordered]@{
            path = $relative
            byteSize = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    })

    $manifest = [ordered]@{
        schemaVersion = "filingbridge.private-server.release/v1"
        version = $Version
        generatedAtUtc = [DateTime]::UtcNow.ToString("o")
        candidate = [ordered]@{
            commitSha = $CandidateCommitSha
            githubActionsRunUrl = $GitHubActionsRunUrl
        }
        supportedHosts = @("windows-x64")
        images = [ordered]@{
            backend = [ordered]@{ exactDigestReference = $backendReference }
            frontend = [ordered]@{ exactDigestReference = $frontendReference }
            postgres = [ordered]@{ exactDigestReference = $PostgresImage }
        }
        files = $files
        statutoryAssurance = [ordered]@{
            status = "release-blocked"
            noDirectSubmission = $true
            qualifiedAccountantRequired = $true
        }
    }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $stageRoot "release.json") -Encoding UTF8

    $archiveName = "FilingBridge-PrivateServer-$Version.zip"
    $archivePath = Join-Path $outputRoot $archiveName
    $checksumPath = "$archivePath.sha256"
    if ((Test-Path -LiteralPath $archivePath) -or (Test-Path -LiteralPath $checksumPath)) {
        throw "Refusing to overwrite an existing release archive or checksum: $archivePath"
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory(
        $stageRoot,
        $archivePath,
        [IO.Compression.CompressionLevel]::Optimal,
        $false)

    $archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$archiveHash  $archiveName" | Set-Content -LiteralPath $checksumPath -Encoding ASCII
    [pscustomobject]@{
        archivePath = $archivePath
        checksumPath = $checksumPath
        sha256 = $archiveHash
        byteSize = (Get-Item -LiteralPath $archivePath).Length
        backendImage = $backendReference
        frontendImage = $frontendReference
        postgresImage = $PostgresImage
    } | ConvertTo-Json -Depth 4
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
