param(
    [Parameter(Mandatory = $true)]
    [string]$EvidencePath,
    [string]$ReportPath = "",
    [string]$CommitSha = "",
    [string]$GitHubActionsRunUrl = "",
    [switch]$AllowUnpromoted
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$failures = [System.Collections.Generic.List[string]]::new()

function Add-Failure([string]$Message) {
    $failures.Add($Message) | Out-Null
}

function Assert-True($Value, [string]$Context) {
    if ($Value -ne $true) { Add-Failure "$Context must be true." }
}

function Assert-False($Value, [string]$Context) {
    if ($Value -ne $false) { Add-Failure "$Context must be false." }
}

function Assert-FileEvidence($FileEvidence, [string]$Directory, [string]$Context) {
    if ($null -eq $FileEvidence) {
        Add-Failure "$Context file evidence is required."
        return
    }

    $fileName = [string]$FileEvidence.fileName
    $path = Join-Path $Directory $fileName
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        Add-Failure "$Context retained file is missing: $fileName"
        return
    }

    $item = Get-Item -LiteralPath $path
    if ($item.Length -ne [long]$FileEvidence.byteSize) {
        Add-Failure "$Context byte size does not match retained file $fileName."
    }
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -cne [string]$FileEvidence.sha256) {
        Add-Failure "$Context SHA-256 does not match retained file $fileName."
    }
}

function Get-RetainedFilePath($FileEvidence, [string]$Directory, [string]$Context) {
    if ($null -eq $FileEvidence -or [string]::IsNullOrWhiteSpace([string]$FileEvidence.fileName)) {
        return ""
    }

    $path = Join-Path $Directory ([string]$FileEvidence.fileName)
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        return ""
    }

    return $path
}

function Assert-ScanFile($FileEvidence, [string]$Directory, [string]$Context) {
    $path = Get-RetainedFilePath $FileEvidence $Directory $Context
    if ([string]::IsNullOrWhiteSpace($path)) { return }

    try {
        $scan = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        $highCritical = @(
            @($scan.Results) |
                ForEach-Object { @($_.Vulnerabilities) } |
                Where-Object { $null -ne $_ -and [string]$_.Severity -in @("HIGH", "CRITICAL") }
        )
        if ($highCritical.Count -ne 0) {
            Add-Failure "$Context retained Trivy report contains HIGH/CRITICAL vulnerabilities."
        }
    } catch {
        Add-Failure "$Context retained Trivy report is not valid JSON."
    }
}

function Assert-SbomFile($FileEvidence, [string]$Directory, [string]$Context) {
    $path = Get-RetainedFilePath $FileEvidence $Directory $Context
    if ([string]::IsNullOrWhiteSpace($path)) { return }

    try {
        $sbom = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        if ([string]$sbom.spdxVersion -notmatch '^SPDX-') {
            Add-Failure "$Context retained SBOM must be SPDX JSON."
        }
    } catch {
        Add-Failure "$Context retained SBOM is not valid JSON."
    }
}

function Assert-ProvenanceFile($FileEvidence, [string]$Directory, [string]$Context) {
    $path = Get-RetainedFilePath $FileEvidence $Directory $Context
    if ([string]::IsNullOrWhiteSpace($path)) { return }

    try {
        $bundle = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        if ($null -eq $bundle) {
            Add-Failure "$Context retained GitHub provenance bundle must contain JSON evidence."
        }
    } catch {
        Add-Failure "$Context retained GitHub provenance bundle is not valid JSON."
    }
}

if (-not (Test-Path -LiteralPath $EvidencePath -PathType Leaf)) {
    throw "Container supply-chain report is missing: $EvidencePath"
}

$evidence = Get-Content -LiteralPath $EvidencePath -Raw | ConvertFrom-Json
$directory = Split-Path -Parent (Resolve-Path -LiteralPath $EvidencePath)
$promoted = [string]$evidence.promotionMode -eq "promoted"

if ($AllowUnpromoted) {
    if ($promoted) {
        if ([string]$evidence.status -ne "passed") { Add-Failure "Promoted evidence status must be passed." }
        Assert-True $evidence.releaseEligible "releaseEligible"
    } else {
        if ([string]$evidence.status -ne "blocked") { Add-Failure "Unpromoted evidence status must be explicitly blocked." }
        Assert-False $evidence.releaseEligible "releaseEligible"
    }
} else {
    if ([string]$evidence.status -ne "passed") { Add-Failure "status must be passed for release evidence." }
    if (-not $promoted) { Add-Failure "promotionMode must be promoted for release evidence." }
    Assert-True $evidence.releaseEligible "releaseEligible"
}

if ([string]$evidence.candidate.commitSha -cnotmatch '^[0-9a-f]{40}$') {
    Add-Failure "candidate.commitSha must be the full lowercase 40-character release commit SHA."
}
if ([string]$evidence.candidate.githubActionsRunUrl -cnotmatch '^https://github\.com/[^/]+/[^/]+/actions/runs/[0-9]+$') {
    Add-Failure "candidate.githubActionsRunUrl must identify the exact GitHub Actions run."
}

if (-not [string]::IsNullOrWhiteSpace($CommitSha) -and [string]$evidence.candidate.commitSha -cne $CommitSha) {
    Add-Failure "candidate.commitSha does not match the expected release commit."
}
if (-not [string]::IsNullOrWhiteSpace($GitHubActionsRunUrl) -and [string]$evidence.candidate.githubActionsRunUrl -cne $GitHubActionsRunUrl) {
    Add-Failure "candidate.githubActionsRunUrl does not match the expected workflow run."
}

Assert-True $evidence.policy.buildOncePerComponent "policy.buildOncePerComponent"
Assert-True $evidence.policy.immutableRegistryDigestsRequired "policy.immutableRegistryDigestsRequired"
Assert-True $evidence.policy.githubProvenanceRequired "policy.githubProvenanceRequired"
Assert-True $evidence.policy.productionSmokeMustPullExactDigests "policy.productionSmokeMustPullExactDigests"
if ([string]$evidence.policy.sbomFormat -ne "spdx-json") {
    Add-Failure "policy.sbomFormat must be spdx-json."
}
if ($promoted) {
    Assert-True $evidence.policy.scanExactProductionReferences "policy.scanExactProductionReferences"
} else {
    Assert-False $evidence.policy.scanExactProductionReferences "policy.scanExactProductionReferences"
}
if (@($evidence.policy.failOnSeverities).Count -ne 2 -or
    -not (@($evidence.policy.failOnSeverities) -contains "HIGH") -or
    -not (@($evidence.policy.failOnSeverities) -contains "CRITICAL")) {
    Add-Failure "policy.failOnSeverities must contain exactly HIGH and CRITICAL."
}
Assert-True $evidence.controls.backendAndMigrationUseSameDigest "controls.backendAndMigrationUseSameDigest"
Assert-True $evidence.controls.productionSmokeVerified "controls.productionSmokeVerified"

$images = @($evidence.images)
$retainedFileNames = [System.Collections.Generic.List[string]]::new()
if ($images.Count -ne 2) {
    Add-Failure "images must contain exactly backend and frontend entries."
}
if (@($images.component | Sort-Object) -join "," -ne "backend,frontend") {
    Add-Failure "images must contain backend and frontend components."
}

foreach ($image in $images) {
    $context = "images.$($image.component)"
    if ([string]$image.imageName -cnotmatch '^ghcr\.io/[a-z0-9._/-]+$') {
        Add-Failure "$context.imageName must be a lowercase, tag-free GHCR image name."
    }
    if ([string]$image.digest -cnotmatch '^sha256:[0-9a-f]{64}$') {
        Add-Failure "$context.digest must be a lowercase sha256 digest."
    }
    $expectedReference = "$($image.imageName)@$($image.digest)"
    if ([string]$image.exactDigestReference -cne $expectedReference) {
        Add-Failure "$context.exactDigestReference must match imageName@digest."
    }
    if ([int]$image.builtInvocationCount -ne 1) {
        Add-Failure "$context.builtInvocationCount must be exactly 1."
    }

    Assert-True $image.scan.passed "$context.scan.passed"
    Assert-True $image.scan.failOnDetected "$context.scan.failOnDetected"
    Assert-False $image.scan.ignoreUnfixed "$context.scan.ignoreUnfixed"
    if ([string]$image.scan.scanner -ne "Trivy") {
        Add-Failure "$context.scan.scanner must be Trivy."
    }
    if (@($image.scan.severities).Count -ne 2 -or
        -not (@($image.scan.severities) -contains "HIGH") -or
        -not (@($image.scan.severities) -contains "CRITICAL")) {
        Add-Failure "$context.scan.severities must contain exactly HIGH and CRITICAL."
    }
    if ($promoted -and [string]$image.scan.imageReference -cne $expectedReference) {
        Add-Failure "$context.scan.imageReference must be the exact promoted digest reference."
    }
    if ([int]$image.scan.highCriticalVulnerabilityCount -ne 0) {
        Add-Failure "$context.scan.highCriticalVulnerabilityCount must be zero."
    }
    Assert-FileEvidence $image.scan.file $directory "$context.scan"
    Assert-ScanFile $image.scan.file $directory "$context.scan"
    $retainedFileNames.Add([string]$image.scan.file.fileName) | Out-Null

    if ([string]$image.sbom.format -ne "spdx-json" -or [string]$image.sbom.spdxVersion -notmatch '^SPDX-') {
        Add-Failure "$context.sbom must be SPDX JSON."
    }
    Assert-FileEvidence $image.sbom.file $directory "$context.sbom"
    Assert-SbomFile $image.sbom.file $directory "$context.sbom"
    $retainedFileNames.Add([string]$image.sbom.file.fileName) | Out-Null

    if ($promoted) {
        Assert-True $image.pushedToRegistry "$context.pushedToRegistry"
        Assert-True $image.pulledForSmoke "$context.pulledForSmoke"
        Assert-True $image.provenance.attested "$context.provenance.attested"
        if ([string]$image.productionSmokeReference -cne $expectedReference) {
            Add-Failure "$context.productionSmokeReference must be the exact promoted digest."
        }
        if ([string]$image.provenance.attestationUrl -notmatch '^https://github\.com/.+/attestations/') {
            Add-Failure "$context.provenance.attestationUrl must be a GitHub attestation URL."
        }
        Assert-FileEvidence $image.provenance.file $directory "$context.provenance"
        Assert-ProvenanceFile $image.provenance.file $directory "$context.provenance"
        $retainedFileNames.Add([string]$image.provenance.file.fileName) | Out-Null
    } else {
        Assert-False $image.pushedToRegistry "$context.pushedToRegistry"
        Assert-False $image.pulledForSmoke "$context.pulledForSmoke"
        Assert-False $image.provenance.attested "$context.provenance.attested"
    }
}

$expectedRetainedFileCount = if ($promoted) { 6 } else { 4 }
if ($retainedFileNames.Count -ne $expectedRetainedFileCount -or
    @($retainedFileNames | Select-Object -Unique).Count -ne $expectedRetainedFileCount) {
    Add-Failure "Backend and frontend scan, SBOM, and provenance evidence must use distinct retained files."
}

if ($promoted) {
    Assert-True $evidence.controls.registryCredentialsAvailable "controls.registryCredentialsAvailable"
    Assert-True $evidence.controls.productionSmokeUsedExactDigestReferences "controls.productionSmokeUsedExactDigestReferences"
    Assert-False $evidence.controls.mutableProductionTagsUsed "controls.mutableProductionTagsUsed"
    Assert-False $evidence.controls.localVerificationTagsUsed "controls.localVerificationTagsUsed"
    if (@($evidence.blockingFailures).Count -ne 0) { Add-Failure "blockingFailures must be empty for promoted evidence." }
} else {
    Assert-False $evidence.releaseEligible "releaseEligible"
    Assert-False $evidence.controls.registryCredentialsAvailable "controls.registryCredentialsAvailable"
    Assert-False $evidence.controls.productionSmokeUsedExactDigestReferences "controls.productionSmokeUsedExactDigestReferences"
    Assert-False $evidence.controls.mutableProductionTagsUsed "controls.mutableProductionTagsUsed"
    Assert-True $evidence.controls.localVerificationTagsUsed "controls.localVerificationTagsUsed"
    if (@($evidence.blockingFailures).Count -lt 3) {
        Add-Failure "Unpromoted evidence must retain explicit registry, provenance, and digest-smoke blockers."
    }
}

if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path $directory "container-supply-chain-verification-report.json"
}
$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory)) {
    New-Item -ItemType Directory -Force -Path $reportDirectory | Out-Null
}

[ordered]@{
    status = if ($failures.Count -eq 0) { "passed" } else { "failed" }
    verifiedAtUtc = [DateTime]::UtcNow.ToString("o")
    allowUnpromoted = [bool]$AllowUnpromoted
    promotionMode = [string]$evidence.promotionMode
    releaseEligible = [bool]$evidence.releaseEligible
    commitSha = [string]$evidence.candidate.commitSha
    githubActionsRunUrl = [string]$evidence.candidate.githubActionsRunUrl
    evidenceReport = [ordered]@{
        fileName = [IO.Path]::GetFileName($EvidencePath)
        byteSize = (Get-Item -LiteralPath $EvidencePath).Length
        sha256 = (Get-FileHash -LiteralPath $EvidencePath -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    verifiedImageDigests = @($images | ForEach-Object { [string]$_.exactDigestReference })
    retainedEvidenceFiles = @(
        $images | ForEach-Object {
            $_.scan.file
            $_.sbom.file
            if ($null -ne $_.provenance.file) { $_.provenance.file }
        }
    )
    failures = @($failures)
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ReportPath -Encoding UTF8

if ($failures.Count -gt 0) {
    throw "Container supply-chain verification failed: $($failures -join '; ')"
}

Write-Host "Container supply-chain verification passed: $ReportPath"
