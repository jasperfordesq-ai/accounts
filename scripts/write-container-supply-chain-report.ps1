param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("promoted", "verification-only")]
    [string]$PromotionMode,
    [Parameter(Mandatory = $true)]
    [string]$CommitSha,
    [Parameter(Mandatory = $true)]
    [string]$GitHubActionsRunUrl,
    [Parameter(Mandatory = $true)]
    [string]$BackendImageName,
    [Parameter(Mandatory = $true)]
    [string]$BackendImageReference,
    [Parameter(Mandatory = $true)]
    [string]$BackendDigest,
    [Parameter(Mandatory = $true)]
    [string]$FrontendImageName,
    [Parameter(Mandatory = $true)]
    [string]$FrontendImageReference,
    [Parameter(Mandatory = $true)]
    [string]$FrontendDigest,
    [Parameter(Mandatory = $true)]
    [string]$BackendScanPath,
    [Parameter(Mandatory = $true)]
    [string]$FrontendScanPath,
    [Parameter(Mandatory = $true)]
    [string]$BackendSbomPath,
    [Parameter(Mandatory = $true)]
    [string]$FrontendSbomPath,
    [string]$BackendProvenancePath = "",
    [string]$FrontendProvenancePath = "",
    [string]$BackendAttestationUrl = "",
    [string]$FrontendAttestationUrl = "",
    [switch]$SmokeVerified,
    [Parameter(Mandatory = $true)]
    [string]$EvidencePath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Assert-Digest([string]$Digest, [string]$Label) {
    if ($Digest -cnotmatch '^sha256:[0-9a-f]{64}$') {
        throw "$Label must be a lowercase sha256 digest."
    }
}

function Assert-ImageName([string]$ImageName, [string]$Label) {
    if ($ImageName -cnotmatch '^ghcr\.io/[a-z0-9._/-]+$') {
        throw "$Label must be a lowercase, tag-free GHCR image name."
    }
}

function Get-FileEvidence([string]$Path, [string]$Label) {
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Label evidence file is missing: $Path"
    }

    $item = Get-Item -LiteralPath $Path
    if ($item.Length -le 0) {
        throw "$Label evidence file must not be empty: $Path"
    }

    [ordered]@{
        fileName = $item.Name
        byteSize = $item.Length
        sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

function Get-ScanEvidence([string]$Path, [string]$Reference, [string]$Label) {
    $file = Get-FileEvidence $Path "$Label Trivy scan"
    $scan = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    $vulnerabilities = @(
        @($scan.Results) |
            ForEach-Object { @($_.Vulnerabilities) } |
            Where-Object { $null -ne $_ }
    )
    $highCritical = @($vulnerabilities | Where-Object { [string]$_.Severity -in @("HIGH", "CRITICAL") })
    if ($highCritical.Count -ne 0) {
        throw "$Label image scan contains $($highCritical.Count) HIGH/CRITICAL vulnerabilities."
    }

    [ordered]@{
        scanner = "Trivy"
        imageReference = $Reference
        severities = @("HIGH", "CRITICAL")
        failOnDetected = $true
        ignoreUnfixed = $false
        highCriticalVulnerabilityCount = 0
        passed = $true
        file = $file
    }
}

function Get-SbomEvidence([string]$Path, [string]$Label) {
    $file = Get-FileEvidence $Path "$Label SPDX SBOM"
    $sbom = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ([string]$sbom.spdxVersion -notmatch '^SPDX-') {
        throw "$Label SBOM must be SPDX JSON."
    }

    [ordered]@{
        format = "spdx-json"
        spdxVersion = [string]$sbom.spdxVersion
        file = $file
    }
}

function Get-ProvenanceEvidence(
    [string]$Path,
    [string]$AttestationUrl,
    [bool]$Required,
    [string]$Label
) {
    if (-not $Required) {
        return [ordered]@{
            attested = $false
            attestationUrl = ""
            file = $null
        }
    }

    if ($AttestationUrl -notmatch '^https://github\.com/.+/attestations/') {
        throw "$Label GitHub provenance attestation URL is missing or malformed."
    }

    [ordered]@{
        attested = $true
        attestationUrl = $AttestationUrl
        file = Get-FileEvidence $Path "$Label provenance bundle"
    }
}

Assert-Digest $BackendDigest "Backend digest"
Assert-Digest $FrontendDigest "Frontend digest"
Assert-ImageName $BackendImageName "Backend image name"
Assert-ImageName $FrontendImageName "Frontend image name"
if ($CommitSha -cnotmatch '^[0-9a-f]{40}$') {
    throw "CommitSha must be the full lowercase 40-character release commit SHA."
}
if ($GitHubActionsRunUrl -cnotmatch '^https://github\.com/[^/]+/[^/]+/actions/runs/[0-9]+$') {
    throw "GitHubActionsRunUrl must identify the exact GitHub Actions run."
}
if ($BackendDigest -eq $FrontendDigest) {
    throw "Backend and frontend digests must identify distinct images."
}
if (-not $SmokeVerified) {
    throw "Container supply-chain evidence can be written only after the production smoke completes."
}

$promoted = $PromotionMode -eq "promoted"
$backendExactReference = "$BackendImageName@$BackendDigest"
$frontendExactReference = "$FrontendImageName@$FrontendDigest"
if ($promoted) {
    if ($BackendImageReference -cne $backendExactReference) {
        throw "Backend production smoke reference must equal the pushed backend digest reference."
    }
    if ($FrontendImageReference -cne $frontendExactReference) {
        throw "Frontend production smoke reference must equal the pushed frontend digest reference."
    }
}

$backendScan = Get-ScanEvidence $BackendScanPath $BackendImageReference "Backend"
$frontendScan = Get-ScanEvidence $FrontendScanPath $FrontendImageReference "Frontend"
$backendSbom = Get-SbomEvidence $BackendSbomPath "Backend"
$frontendSbom = Get-SbomEvidence $FrontendSbomPath "Frontend"
$backendProvenance = Get-ProvenanceEvidence $BackendProvenancePath $BackendAttestationUrl $promoted "Backend"
$frontendProvenance = Get-ProvenanceEvidence $FrontendProvenancePath $FrontendAttestationUrl $promoted "Frontend"
$retainedArtifactPaths = @($BackendScanPath, $FrontendScanPath, $BackendSbomPath, $FrontendSbomPath)
if ($promoted) {
    $retainedArtifactPaths += @($BackendProvenancePath, $FrontendProvenancePath)
}
$distinctArtifactPaths = @($retainedArtifactPaths | ForEach-Object { [IO.Path]::GetFullPath($_) } | Select-Object -Unique)
if ($distinctArtifactPaths.Count -ne $retainedArtifactPaths.Count) {
    throw "Backend and frontend scan, SBOM, and provenance evidence must be retained as distinct files."
}

$blockingFailures = [System.Collections.Generic.List[string]]::new()
if (-not $promoted) {
    $blockingFailures.Add("GHCR promotion was not authorised for this event; local verification images are not release artifacts.")
    $blockingFailures.Add("GitHub build provenance was not attested because registry promotion credentials were unavailable.")
    $blockingFailures.Add("Production smoke used local verification tags rather than pulled immutable registry digests.")
}

$reportDirectory = Split-Path -Parent $EvidencePath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory)) {
    New-Item -ItemType Directory -Force -Path $reportDirectory | Out-Null
}

$report = [ordered]@{
    status = if ($promoted) { "passed" } else { "blocked" }
    generatedAtUtc = [DateTime]::UtcNow.ToString("o")
    promotionMode = $PromotionMode
    releaseEligible = $promoted
    candidate = [ordered]@{
        commitSha = $CommitSha
        githubActionsRunUrl = $GitHubActionsRunUrl
    }
    policy = [ordered]@{
        buildOncePerComponent = $true
        immutableRegistryDigestsRequired = $true
        scanExactProductionReferences = $promoted
        failOnSeverities = @("HIGH", "CRITICAL")
        sbomFormat = "spdx-json"
        githubProvenanceRequired = $true
        productionSmokeMustPullExactDigests = $true
    }
    controls = [ordered]@{
        registryCredentialsAvailable = $promoted
        backendAndMigrationUseSameDigest = $true
        productionSmokeVerified = $true
        productionSmokeUsedExactDigestReferences = $promoted
        mutableProductionTagsUsed = $false
        localVerificationTagsUsed = -not $promoted
    }
    images = @(
        [ordered]@{
            component = "backend"
            imageName = $BackendImageName
            digest = $BackendDigest
            exactDigestReference = $backendExactReference
            productionSmokeReference = $BackendImageReference
            builtInvocationCount = 1
            pushedToRegistry = $promoted
            pulledForSmoke = $promoted
            scan = $backendScan
            sbom = $backendSbom
            provenance = $backendProvenance
        },
        [ordered]@{
            component = "frontend"
            imageName = $FrontendImageName
            digest = $FrontendDigest
            exactDigestReference = $frontendExactReference
            productionSmokeReference = $FrontendImageReference
            builtInvocationCount = 1
            pushedToRegistry = $promoted
            pulledForSmoke = $promoted
            scan = $frontendScan
            sbom = $frontendSbom
            provenance = $frontendProvenance
        }
    )
    blockingFailures = @($blockingFailures)
}

$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $EvidencePath -Encoding UTF8
Write-Host "Container supply-chain report written: $EvidencePath ($($report.status))"
