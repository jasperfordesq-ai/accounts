$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$writer = Join-Path $PSScriptRoot "write-container-supply-chain-report.ps1"
$verifier = Join-Path $PSScriptRoot "verify-container-supply-chain-report.ps1"
$machinePackVerifier = Join-Path $PSScriptRoot "verify-ci-machine-evidence-pack.ps1"
$commitSha = "a" * 40
$runUrl = "https://github.com/example/accounts/actions/runs/123"
$backendImageName = "ghcr.io/example/accounts-api"
$frontendImageName = "ghcr.io/example/accounts-frontend"
$backendDigest = "sha256:" + ("1" * 64)
$frontendDigest = "sha256:" + ("2" * 64)
$backendReference = "accounts-api-ci:$commitSha"
$frontendReference = "accounts-frontend-ci:$commitSha"
$temporaryBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$temporaryRoot = [IO.Path]::GetFullPath(
    (Join-Path $temporaryBase "accounts-container-supply-chain-parser-$([Guid]::NewGuid().ToString('N'))"))
if (-not $temporaryRoot.StartsWith($temporaryBase, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Synthetic container evidence path escaped the operating-system temporary directory."
}

New-Item -ItemType Directory -Path $temporaryRoot -Force | Out-Null

$machineVerifierText = Get-Content -LiteralPath $machinePackVerifier -Raw
$machineDefinitionStart = $machineVerifierText.IndexOf("function Add-Failure", [StringComparison]::Ordinal)
$machineExecutionStart = $machineVerifierText.IndexOf("`n`$failures =", [StringComparison]::Ordinal)
if ($machineDefinitionStart -lt 0 -or $machineExecutionStart -le $machineDefinitionStart) {
    throw "Could not isolate CI machine evidence verifier function definitions for contract tests."
}
Invoke-Expression $machineVerifierText.Substring(
    $machineDefinitionStart,
    $machineExecutionStart - $machineDefinitionStart)

function Write-JsonFile([string]$Path, $Value) {
    $Value | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $Path -Encoding utf8
}

function New-CleanScan([string]$Target) {
    [ordered]@{
        SchemaVersion = 2
        ArtifactName = $Target
        ArtifactType = "container_image"
        Results = @(
            [ordered]@{
                Target = $Target
                Class = "lang-pkgs"
                Type = "node-pkg"
                Packages = @()
            }
        )
    }
}

function Read-MachineEvidence([string]$Path) {
    $evidence = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    $evidence | Add-Member -NotePropertyName __path -NotePropertyValue (Resolve-Path -LiteralPath $Path).Path -Force
    return $evidence
}

function Assert-MachineSupplyChainContract(
    [string]$ReportPath,
    [string]$VerificationPath,
    [bool]$AllowVerificationOnly,
    [string]$ExpectedFailure = ""
) {
    $machineFailures = [System.Collections.Generic.List[string]]::new()
    Assert-ContainerSupplyChainEvidence `
        (Read-MachineEvidence $ReportPath) `
        (Read-MachineEvidence $VerificationPath) `
        $commitSha `
        $runUrl `
        $AllowVerificationOnly `
        $machineFailures

    if ([string]::IsNullOrWhiteSpace($ExpectedFailure)) {
        if ($machineFailures.Count -ne 0) {
            throw "CI machine supply-chain contract unexpectedly failed: $($machineFailures -join '; ')"
        }
        return
    }

    if (-not (@($machineFailures) | Where-Object { $_ -like "*$ExpectedFailure*" })) {
        throw "Expected CI machine supply-chain failure containing '$ExpectedFailure', received: $($machineFailures -join '; ')"
    }
}

function Invoke-WriterCase(
    [Parameter(Mandatory = $true)]$BackendScan,
    [string]$ExpectedFailure = ""
) {
    $backendScanPath = Join-Path $temporaryRoot "backend-trivy.json"
    $frontendScanPath = Join-Path $temporaryRoot "frontend-trivy.json"
    $backendSbomPath = Join-Path $temporaryRoot "backend-sbom.spdx.json"
    $frontendSbomPath = Join-Path $temporaryRoot "frontend-sbom.spdx.json"
    $reportPath = Join-Path $temporaryRoot "container-supply-chain-report.json"
    $verificationPath = Join-Path $temporaryRoot "container-supply-chain-verification-report.json"

    Write-JsonFile $backendScanPath $BackendScan
    Write-JsonFile $frontendScanPath (New-CleanScan $frontendReference)
    Write-JsonFile $backendSbomPath ([ordered]@{ spdxVersion = "SPDX-2.3" })
    Write-JsonFile $frontendSbomPath ([ordered]@{ spdxVersion = "SPDX-2.3" })
    Remove-Item -LiteralPath $reportPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $verificationPath -Force -ErrorAction SilentlyContinue

    try {
        & $writer `
            -PromotionMode verification-only `
            -CommitSha $commitSha `
            -GitHubActionsRunUrl $runUrl `
            -BackendImageName $backendImageName `
            -BackendImageReference $backendReference `
            -BackendDigest $backendDigest `
            -FrontendImageName $frontendImageName `
            -FrontendImageReference $frontendReference `
            -FrontendDigest $frontendDigest `
            -BackendScanPath $backendScanPath `
            -FrontendScanPath $frontendScanPath `
            -BackendSbomPath $backendSbomPath `
            -FrontendSbomPath $frontendSbomPath `
            -SmokeVerified `
            -EvidencePath $reportPath
    }
    catch {
        if ([string]::IsNullOrWhiteSpace($ExpectedFailure)) {
            throw
        }
        if ($_.Exception.Message -notlike "*$ExpectedFailure*") {
            throw "Expected failure containing '$ExpectedFailure', received: $($_.Exception.Message)"
        }
        return
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedFailure)) {
        throw "Expected writer failure containing '$ExpectedFailure', but the writer passed."
    }

    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    if ($report.status -ne "blocked" -or $report.promotionMode -ne "verification-only") {
        throw "Synthetic verification-only report did not retain its explicit blocked boundary."
    }
    if (@($report.images).Count -ne 2 -or @($report.images | Where-Object { $_.scan.passed }).Count -ne 2) {
        throw "Synthetic clean scans were not retained as two passed image scan records."
    }

    & $verifier `
        -EvidencePath $reportPath `
        -ReportPath $verificationPath `
        -CommitSha $commitSha `
        -GitHubActionsRunUrl $runUrl `
        -AllowUnpromoted
    $verification = Get-Content -LiteralPath $verificationPath -Raw | ConvertFrom-Json
    if ($verification.status -ne "passed" -or $verification.allowUnpromoted -ne $true) {
        throw "Synthetic clean scan report did not pass strict unpromoted verification."
    }

    Assert-MachineSupplyChainContract $reportPath $verificationPath $true
}

function Assert-VerifierRejectsRetainedBlockedFinding {
    $backendScanPath = Join-Path $temporaryRoot "backend-trivy.json"
    $reportPath = Join-Path $temporaryRoot "container-supply-chain-report.json"
    $verificationPath = Join-Path $temporaryRoot "container-supply-chain-verification-report.json"
    $blockedScan = New-CleanScan $backendReference
    $blockedScan.Results[0].Vulnerabilities = @(
        [ordered]@{ VulnerabilityID = "CVE-SYNTHETIC-RETAINED"; Severity = "CRITICAL" }
    )
    Write-JsonFile $backendScanPath $blockedScan

    try {
        & $verifier `
            -EvidencePath $reportPath `
            -ReportPath $verificationPath `
            -CommitSha $commitSha `
            -GitHubActionsRunUrl $runUrl `
            -AllowUnpromoted
    }
    catch {
        if ($_.Exception.Message -notlike "*retained Trivy report contains HIGH/CRITICAL vulnerabilities*") {
            throw "Retained verifier did not report the injected CRITICAL finding: $($_.Exception.Message)"
        }
        return
    }

    throw "Retained verifier accepted a CRITICAL finding after the report claimed zero findings."
}

function Invoke-PromotedMachineCase {
    $backendScanPath = Join-Path $temporaryRoot "backend-trivy.json"
    $frontendScanPath = Join-Path $temporaryRoot "frontend-trivy.json"
    $backendSbomPath = Join-Path $temporaryRoot "backend-sbom.spdx.json"
    $frontendSbomPath = Join-Path $temporaryRoot "frontend-sbom.spdx.json"
    $backendProvenancePath = Join-Path $temporaryRoot "backend-provenance.json"
    $frontendProvenancePath = Join-Path $temporaryRoot "frontend-provenance.json"
    $reportPath = Join-Path $temporaryRoot "container-supply-chain-report.json"
    $verificationPath = Join-Path $temporaryRoot "container-supply-chain-verification-report.json"
    $backendExactReference = "$backendImageName@$backendDigest"
    $frontendExactReference = "$frontendImageName@$frontendDigest"

    Write-JsonFile $backendScanPath (New-CleanScan $backendExactReference)
    Write-JsonFile $frontendScanPath (New-CleanScan $frontendExactReference)
    Write-JsonFile $backendSbomPath ([ordered]@{ spdxVersion = "SPDX-2.3" })
    Write-JsonFile $frontendSbomPath ([ordered]@{ spdxVersion = "SPDX-2.3" })
    Write-JsonFile $backendProvenancePath ([ordered]@{ subject = "backend" })
    Write-JsonFile $frontendProvenancePath ([ordered]@{ subject = "frontend" })

    & $writer `
        -PromotionMode promoted `
        -CommitSha $commitSha `
        -GitHubActionsRunUrl $runUrl `
        -BackendImageName $backendImageName `
        -BackendImageReference $backendExactReference `
        -BackendDigest $backendDigest `
        -FrontendImageName $frontendImageName `
        -FrontendImageReference $frontendExactReference `
        -FrontendDigest $frontendDigest `
        -BackendScanPath $backendScanPath `
        -FrontendScanPath $frontendScanPath `
        -BackendSbomPath $backendSbomPath `
        -FrontendSbomPath $frontendSbomPath `
        -BackendProvenancePath $backendProvenancePath `
        -FrontendProvenancePath $frontendProvenancePath `
        -BackendAttestationUrl "https://github.com/example/accounts/attestations/111" `
        -FrontendAttestationUrl "https://github.com/example/accounts/attestations/222" `
        -SmokeVerified `
        -EvidencePath $reportPath

    & $verifier `
        -EvidencePath $reportPath `
        -ReportPath $verificationPath `
        -CommitSha $commitSha `
        -GitHubActionsRunUrl $runUrl

    Assert-MachineSupplyChainContract $reportPath $verificationPath $false
}

function Assert-MachineContractRejectsMutation(
    [string]$Label,
    [scriptblock]$MutateSupplyChain,
    [scriptblock]$MutateVerification,
    [string]$ExpectedFailure
) {
    Invoke-WriterCase (New-CleanScan $backendReference)
    $reportPath = Join-Path $temporaryRoot "container-supply-chain-report.json"
    $verificationPath = Join-Path $temporaryRoot "container-supply-chain-verification-report.json"
    $supplyChain = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    $verification = Get-Content -LiteralPath $verificationPath -Raw | ConvertFrom-Json

    & $MutateSupplyChain $supplyChain
    Write-JsonFile $reportPath $supplyChain
    $verification.evidenceReport.byteSize = (Get-Item -LiteralPath $reportPath).Length
    $verification.evidenceReport.sha256 = (Get-FileHash -LiteralPath $reportPath -Algorithm SHA256).Hash.ToLowerInvariant()
    & $MutateVerification $verification
    Write-JsonFile $verificationPath $verification

    try {
        Assert-MachineSupplyChainContract $reportPath $verificationPath $true $ExpectedFailure
    } catch {
        throw "CI machine verification-only mutation '$Label' did not fail as expected: $($_.Exception.Message)"
    }
}

function Assert-PartialMachinePackBoundary {
    Invoke-WriterCase (New-CleanScan $backendReference)
    $partialReportPath = Join-Path $temporaryRoot "partial-ci-machine-evidence-pack-report.json"

    try {
        & $machinePackVerifier `
            -EvidenceDirectory $temporaryRoot `
            -ReportPath $partialReportPath `
            -CommitSha $commitSha `
            -GitHubActionsRunUrl $runUrl `
            -AllowVerificationOnlySupplyChain 2>$null
        throw "Partial CI machine evidence pack unexpectedly passed."
    } catch {
        if (-not (Test-Path -LiteralPath $partialReportPath -PathType Leaf)) {
            throw "Partial CI machine evidence pack did not emit its failure report: $($_.Exception.Message)"
        }
    }

    $partialReport = Get-Content -LiteralPath $partialReportPath -Raw | ConvertFrom-Json
    if ($partialReport.supplyChainEvidenceMode -ne "verification-only" -or
        $partialReport.allowVerificationOnlySupplyChain -ne $true -or
        $partialReport.releaseEligible -ne $false) {
        throw "PR-only CI machine report did not preserve verification-only, non-release metadata."
    }
    if (@($partialReport.failures | Where-Object {
        $_ -eq "container-supply-chain-report.json must have status 'passed'."
    }).Count -ne 0) {
        throw "PR-only CI machine report still applied the generic passed-status requirement to blocked source evidence."
    }
    if (@($partialReport.failures | Where-Object {
        $_ -like "container-supply-chain-report.json*" -or
        $_ -like "container-supply-chain-verification-report.json*"
    }).Count -ne 0) {
        throw "Valid PR-only supply-chain evidence created CI machine contract failures: $($partialReport.failures -join '; ')"
    }
}

try {
    Invoke-PromotedMachineCase

    # Authentic clean Trivy JSON omits Vulnerabilities on evaluated targets.
    Invoke-WriterCase (New-CleanScan $backendReference)
    Assert-VerifierRejectsRetainedBlockedFinding

    $emptyVulnerabilities = New-CleanScan $backendReference
    $emptyVulnerabilities.Results[0].Vulnerabilities = @()
    Invoke-WriterCase $emptyVulnerabilities

    $lowVulnerability = New-CleanScan $backendReference
    $lowVulnerability.Results[0].Vulnerabilities = @(
        [ordered]@{ VulnerabilityID = "CVE-SYNTHETIC-LOW"; Severity = "LOW" }
    )
    Invoke-WriterCase $lowVulnerability

    $highVulnerability = New-CleanScan $backendReference
    $highVulnerability.Results[0].Vulnerabilities = @(
        [ordered]@{ VulnerabilityID = "CVE-SYNTHETIC-HIGH"; Severity = "HIGH" }
    )
    Invoke-WriterCase $highVulnerability "contains 1 HIGH/CRITICAL vulnerabilities"

    $criticalVulnerability = New-CleanScan $backendReference
    $criticalVulnerability.Results[0].Vulnerabilities = @(
        [ordered]@{ VulnerabilityID = "CVE-SYNTHETIC-CRITICAL"; Severity = "CRITICAL" }
    )
    Invoke-WriterCase $criticalVulnerability "contains 1 HIGH/CRITICAL vulnerabilities"

    Invoke-WriterCase "not-a-json-object" "root must be a JSON object"

    $wrongSchema = New-CleanScan $backendReference
    $wrongSchema.SchemaVersion = 1
    Invoke-WriterCase $wrongSchema "SchemaVersion must be 2"

    $stringSchema = New-CleanScan $backendReference
    $stringSchema.SchemaVersion = "2"
    Invoke-WriterCase $stringSchema "SchemaVersion must be 2"

    $wrongArtifact = New-CleanScan "ghcr.io/example/not-the-scanned-image:verification"
    Invoke-WriterCase $wrongArtifact "ArtifactName must match the exact scanned image reference"

    $wrongArtifactType = New-CleanScan $backendReference
    $wrongArtifactType.ArtifactType = "filesystem"
    Invoke-WriterCase $wrongArtifactType "ArtifactType must be container_image"

    Invoke-WriterCase ([ordered]@{
        SchemaVersion = 2
        ArtifactName = $backendReference
        ArtifactType = "container_image"
    }) "must contain a Results array"
    Invoke-WriterCase ([ordered]@{
        SchemaVersion = 2
        ArtifactName = $backendReference
        ArtifactType = "container_image"
        Results = @()
    }) "Results array must not be empty"
    Invoke-WriterCase ([ordered]@{
        SchemaVersion = 2
        ArtifactName = $backendReference
        ArtifactType = "container_image"
        Results = [ordered]@{ Target = "not-an-array"; Class = "os-pkgs"; Type = "alpine" }
    }) "must contain a Results array"
    Invoke-WriterCase ([ordered]@{
        SchemaVersion = 2
        ArtifactName = $backendReference
        ArtifactType = "container_image"
        Results = @([ordered]@{ Class = "os-pkgs"; Type = "alpine" })
    }) "non-empty Target"

    $numericTarget = New-CleanScan $backendReference
    $numericTarget.Results[0].Target = 42
    Invoke-WriterCase $numericTarget "non-empty Target"

    $arrayClass = New-CleanScan $backendReference
    $arrayClass.Results[0].Class = @("lang-pkgs")
    Invoke-WriterCase $arrayClass "non-empty Class"

    $malformedVulnerability = New-CleanScan $backendReference
    $malformedVulnerability.Results[0].Vulnerabilities = @([ordered]@{ VulnerabilityID = "CVE-SYNTHETIC" })
    Invoke-WriterCase $malformedVulnerability "non-empty Severity"

    $nonArrayVulnerabilities = New-CleanScan $backendReference
    $nonArrayVulnerabilities.Results[0].Vulnerabilities = [ordered]@{
        VulnerabilityID = "CVE-SYNTHETIC"
        Severity = "LOW"
    }
    Invoke-WriterCase $nonArrayVulnerabilities "Vulnerabilities must be an array"

    $nullVulnerabilities = New-CleanScan $backendReference
    $nullVulnerabilities.Results[0].Vulnerabilities = $null
    Invoke-WriterCase $nullVulnerabilities "Vulnerabilities must be an array"

    $missingVulnerabilityId = New-CleanScan $backendReference
    $missingVulnerabilityId.Results[0].Vulnerabilities = @([ordered]@{ Severity = "LOW" })
    Invoke-WriterCase $missingVulnerabilityId "non-empty VulnerabilityID"

    $numericVulnerabilityId = New-CleanScan $backendReference
    $numericVulnerabilityId.Results[0].Vulnerabilities = @(
        [ordered]@{ VulnerabilityID = 20990003; Severity = "LOW" }
    )
    Invoke-WriterCase $numericVulnerabilityId "non-empty VulnerabilityID"

    $numericSeverity = New-CleanScan $backendReference
    $numericSeverity.Results[0].Vulnerabilities = @(
        [ordered]@{ VulnerabilityID = "CVE-SYNTHETIC"; Severity = 5 }
    )
    Invoke-WriterCase $numericSeverity "non-empty Severity"

    $unsupportedSeverity = New-CleanScan $backendReference
    $unsupportedSeverity.Results[0].Vulnerabilities = @(
        [ordered]@{ VulnerabilityID = "CVE-SYNTHETIC"; Severity = "UNBOUNDED" }
    )
    Invoke-WriterCase $unsupportedSeverity "unsupported Severity"

    Invoke-WriterCase (New-CleanScan $backendReference)
    Assert-MachineSupplyChainContract `
        (Join-Path $temporaryRoot "container-supply-chain-report.json") `
        (Join-Path $temporaryRoot "container-supply-chain-verification-report.json") `
        $false `
        "status must be passed for promoted evidence"

    $noSupplyMutation = { param($value) }
    Assert-MachineContractRejectsMutation "source status" { param($s) $s.status = "passed" } $noSupplyMutation "status must be blocked"
    Assert-MachineContractRejectsMutation "promotion mode" { param($s) $s.promotionMode = "promoted" } $noSupplyMutation "promotionMode must be verification-only"
    Assert-MachineContractRejectsMutation "source release eligibility" { param($s) $s.releaseEligible = $true } $noSupplyMutation "releaseEligible must be false"
    Assert-MachineContractRejectsMutation "string source release eligibility" { param($s) $s.releaseEligible = "false" } $noSupplyMutation "releaseEligible must be false"
    Assert-MachineContractRejectsMutation "exact promotion blockers" {
        param($s)
        $s.blockingFailures = @($s.blockingFailures) + "Synthetic unexpected blocker"
    } $noSupplyMutation "exactly the three verification-only promotion blockers"
    Assert-MachineContractRejectsMutation "scan exact-production flag" { param($s) $s.policy.scanExactProductionReferences = $true } $noSupplyMutation "policy.scanExactProductionReferences must be false"
    Assert-MachineContractRejectsMutation "registry flag" { param($s) $s.controls.registryCredentialsAvailable = $true } $noSupplyMutation "controls.registryCredentialsAvailable must be false"
    Assert-MachineContractRejectsMutation "digest-smoke flag" { param($s) $s.controls.productionSmokeUsedExactDigestReferences = $true } $noSupplyMutation "controls.productionSmokeUsedExactDigestReferences must be false"
    Assert-MachineContractRejectsMutation "mutable tag flag" { param($s) $s.controls.mutableProductionTagsUsed = $true } $noSupplyMutation "controls.mutableProductionTagsUsed must be false"
    Assert-MachineContractRejectsMutation "local tag flag" { param($s) $s.controls.localVerificationTagsUsed = $false } $noSupplyMutation "controls.localVerificationTagsUsed must be true"
    Assert-MachineContractRejectsMutation "string local tag flag" { param($s) $s.controls.localVerificationTagsUsed = "true" } $noSupplyMutation "controls.localVerificationTagsUsed must be true"
    Assert-MachineContractRejectsMutation "registry push" { param($s) $s.images[0].pushedToRegistry = $true } $noSupplyMutation "pushedToRegistry must be false"
    Assert-MachineContractRejectsMutation "registry pull" { param($s) $s.images[0].pulledForSmoke = $true } $noSupplyMutation "pulledForSmoke must be false"
    Assert-MachineContractRejectsMutation "local smoke tag" { param($s) $s.images[0].productionSmokeReference = "accounts-api-ci:latest" } $noSupplyMutation "productionSmokeReference must equal"
    Assert-MachineContractRejectsMutation "local scan tag" { param($s) $s.images[0].scan.imageReference = "accounts-api-ci:latest" } $noSupplyMutation "zero HIGH/CRITICAL Trivy scan of the exact expected image reference"
    Assert-MachineContractRejectsMutation "build once" { param($s) $s.images[0].builtInvocationCount = 2 } $noSupplyMutation "builtInvocationCount must be exactly 1"
    Assert-MachineContractRejectsMutation "high vulnerability count" { param($s) $s.images[0].scan.highCriticalVulnerabilityCount = 1 } $noSupplyMutation "zero HIGH/CRITICAL Trivy scan"
    Assert-MachineContractRejectsMutation "failed scan" { param($s) $s.images[0].scan.passed = $false } $noSupplyMutation "scan.passed must be true"
    Assert-MachineContractRejectsMutation "non-blocking scanner" { param($s) $s.images[0].scan.failOnDetected = $false } $noSupplyMutation "scan.failOnDetected must be true"
    Assert-MachineContractRejectsMutation "ignored unfixed findings" { param($s) $s.images[0].scan.ignoreUnfixed = $true } $noSupplyMutation "scan.ignoreUnfixed must be false"
    Assert-MachineContractRejectsMutation "scan severity inventory" { param($s) $s.images[0].scan.severities = @("HIGH") } $noSupplyMutation "scan.severities must contain exactly HIGH and CRITICAL"
    Assert-MachineContractRejectsMutation "SBOM format" { param($s) $s.images[0].sbom.format = "cyclonedx-json" } $noSupplyMutation "sbom must be SPDX JSON"
    Assert-MachineContractRejectsMutation "provenance claim" { param($s) $s.images[0].provenance.attested = $true } $noSupplyMutation "provenance.attested must be false"
    Assert-MachineContractRejectsMutation "two image inventory" { param($s) $s.images = @($s.images[0]) } $noSupplyMutation "images must contain exactly backend and frontend"
    Assert-MachineContractRejectsMutation "four distinct retained files" {
        param($s)
        $s.images[0].sbom.file = $s.images[0].scan.file
    } $noSupplyMutation "must retain 4 distinct"

    Assert-MachineContractRejectsMutation "verification status" $noSupplyMutation { param($v) $v.status = "failed" } "status must be passed"
    Assert-MachineContractRejectsMutation "verification mode" $noSupplyMutation { param($v) $v.promotionMode = "promoted" } "explicitly allowed verification-only verification"
    Assert-MachineContractRejectsMutation "verification allow flag" $noSupplyMutation { param($v) $v.allowUnpromoted = $false } "allowUnpromoted must be true"
    Assert-MachineContractRejectsMutation "string verification allow flag" $noSupplyMutation { param($v) $v.allowUnpromoted = "true" } "allowUnpromoted must be true"
    Assert-MachineContractRejectsMutation "verification release eligibility" $noSupplyMutation { param($v) $v.releaseEligible = $true } "releaseEligible must be false"
    Assert-MachineContractRejectsMutation "candidate identity" { param($s) $s.candidate.commitSha = "b" * 40 } $noSupplyMutation "candidate.commitSha must match the exact release commit"
    Assert-MachineContractRejectsMutation "source report hash" $noSupplyMutation { param($v) $v.evidenceReport.sha256 = "0" * 64 } "evidenceReport must hash the retained supply-chain report"
    Assert-MachineContractRejectsMutation "digest inventory" $noSupplyMutation { param($v) $v.verifiedImageDigests = @($v.verifiedImageDigests[0]) } "verifiedImageDigests must match both exact image digests"
    Assert-MachineContractRejectsMutation "retained file inventory" $noSupplyMutation { param($v) $v.retainedEvidenceFiles = @($v.retainedEvidenceFiles[0..2]) } "retainedEvidenceFiles must match all 4"
    Assert-MachineContractRejectsMutation "retained file metadata" $noSupplyMutation {
        param($v)
        $v.retainedEvidenceFiles[0].byteSize = [long]$v.retainedEvidenceFiles[0].byteSize + 1
    } "retainedEvidenceFiles metadata must exactly match"
    Assert-MachineContractRejectsMutation "verification failures" $noSupplyMutation { param($v) $v.failures = @("Synthetic verifier failure") } "failures must be empty"

    Assert-PartialMachinePackBoundary

    Write-Host "Container supply-chain parser and CI machine evidence-mode regression tests passed."
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot -PathType Container) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
