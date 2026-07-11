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

function Test-IsJsonObject($Value) {
    return $null -ne $Value -and
        $Value.GetType().FullName -eq "System.Management.Automation.PSCustomObject"
}

function Test-IsSchemaVersionTwo($Value) {
    if ($null -eq $Value -or $Value.GetType().FullName -notin @(
        "System.Byte", "System.SByte", "System.Int16", "System.UInt16",
        "System.Int32", "System.UInt32", "System.Int64", "System.UInt64"
    )) {
        return $false
    }
    return [decimal]$Value -eq 2
}

function Test-IsNonEmptyJsonString($Value) {
    return $Value -is [string] -and -not [string]::IsNullOrWhiteSpace($Value)
}

function Get-ScanEvidence([string]$Path, [string]$Reference, [string]$Label) {
    $file = Get-FileEvidence $Path "$Label Trivy scan"
    $scan = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if (-not (Test-IsJsonObject $scan)) {
        throw "$Label Trivy scan root must be a JSON object."
    }

    $schemaVersionProperty = $scan.PSObject.Properties["SchemaVersion"]
    if ($null -eq $schemaVersionProperty -or -not (Test-IsSchemaVersionTwo $schemaVersionProperty.Value)) {
        throw "$Label Trivy scan SchemaVersion must be 2."
    }
    $artifactNameProperty = $scan.PSObject.Properties["ArtifactName"]
    if ($null -eq $artifactNameProperty -or
        -not (Test-IsNonEmptyJsonString $artifactNameProperty.Value) -or
        $artifactNameProperty.Value -cne $Reference) {
        throw "$Label Trivy scan ArtifactName must match the exact scanned image reference."
    }
    $artifactTypeProperty = $scan.PSObject.Properties["ArtifactType"]
    if ($null -eq $artifactTypeProperty -or
        -not (Test-IsNonEmptyJsonString $artifactTypeProperty.Value) -or
        $artifactTypeProperty.Value -cne "container_image") {
        throw "$Label Trivy scan ArtifactType must be container_image."
    }

    $resultsProperty = $scan.PSObject.Properties["Results"]
    if ($null -eq $resultsProperty -or $resultsProperty.Value -isnot [System.Array]) {
        throw "$Label Trivy scan must contain a Results array."
    }

    $results = @($resultsProperty.Value)
    if ($results.Count -eq 0) {
        throw "$Label Trivy scan Results array must not be empty."
    }

    $vulnerabilities = [System.Collections.Generic.List[object]]::new()
    foreach ($result in $results) {
        if (-not (Test-IsJsonObject $result)) {
            throw "$Label Trivy scan Results entries must be JSON objects."
        }

        $targetProperty = $result.PSObject.Properties["Target"]
        if ($null -eq $targetProperty -or -not (Test-IsNonEmptyJsonString $targetProperty.Value)) {
            throw "$Label Trivy scan Results entries must identify a non-empty Target."
        }
        foreach ($requiredPropertyName in @("Class", "Type")) {
            $requiredProperty = $result.PSObject.Properties[$requiredPropertyName]
            if ($null -eq $requiredProperty -or -not (Test-IsNonEmptyJsonString $requiredProperty.Value)) {
                throw "$Label Trivy scan Results entries must contain a non-empty $requiredPropertyName."
            }
        }

        $vulnerabilitiesProperty = $result.PSObject.Properties["Vulnerabilities"]
        if ($null -eq $vulnerabilitiesProperty) {
            # Trivy omits this property for a clean target. A non-empty Results inventory and
            # Target still prove that the scanner evaluated the image component.
            continue
        }
        if ($null -eq $vulnerabilitiesProperty.Value -or $vulnerabilitiesProperty.Value -isnot [System.Array]) {
            throw "$Label Trivy Vulnerabilities must be an array when present."
        }

        foreach ($vulnerability in @($vulnerabilitiesProperty.Value)) {
            if (-not (Test-IsJsonObject $vulnerability)) {
                throw "$Label Trivy Vulnerabilities entries must be JSON objects."
            }

            $severityProperty = $vulnerability.PSObject.Properties["Severity"]
            if ($null -eq $severityProperty -or -not (Test-IsNonEmptyJsonString $severityProperty.Value)) {
                throw "$Label Trivy Vulnerabilities entries must contain a non-empty Severity."
            }
            $vulnerabilityIdProperty = $vulnerability.PSObject.Properties["VulnerabilityID"]
            if ($null -eq $vulnerabilityIdProperty -or -not (Test-IsNonEmptyJsonString $vulnerabilityIdProperty.Value)) {
                throw "$Label Trivy Vulnerabilities entries must contain a non-empty VulnerabilityID."
            }
            $severity = ([string]$severityProperty.Value).ToUpperInvariant()
            if ($severity -notin @("UNKNOWN", "LOW", "MEDIUM", "HIGH", "CRITICAL")) {
                throw "$Label Trivy Vulnerabilities entry has an unsupported Severity '$severity'."
            }
            $vulnerabilities.Add($vulnerability)
        }
    }

    $highCritical = @(
        $vulnerabilities |
            Where-Object { ([string]$_.Severity).ToUpperInvariant() -in @("HIGH", "CRITICAL") }
    )
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
