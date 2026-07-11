$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$writer = Join-Path $PSScriptRoot "write-container-supply-chain-report.ps1"
$verifier = Join-Path $PSScriptRoot "verify-container-supply-chain-report.ps1"
$backendReference = "ghcr.io/example/accounts-api:verification"
$frontendReference = "ghcr.io/example/accounts-frontend:verification"
$temporaryBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$temporaryRoot = [IO.Path]::GetFullPath(
    (Join-Path $temporaryBase "accounts-container-supply-chain-parser-$([Guid]::NewGuid().ToString('N'))"))
if (-not $temporaryRoot.StartsWith($temporaryBase, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Synthetic container evidence path escaped the operating-system temporary directory."
}

New-Item -ItemType Directory -Path $temporaryRoot -Force | Out-Null

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
            -CommitSha ("a" * 40) `
            -GitHubActionsRunUrl "https://github.com/example/accounts/actions/runs/123" `
            -BackendImageName "ghcr.io/example/accounts-api" `
            -BackendImageReference $backendReference `
            -BackendDigest ("sha256:" + ("1" * 64)) `
            -FrontendImageName "ghcr.io/example/accounts-frontend" `
            -FrontendImageReference $frontendReference `
            -FrontendDigest ("sha256:" + ("2" * 64)) `
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
        -CommitSha ("a" * 40) `
        -GitHubActionsRunUrl "https://github.com/example/accounts/actions/runs/123" `
        -AllowUnpromoted
    $verification = Get-Content -LiteralPath $verificationPath -Raw | ConvertFrom-Json
    if ($verification.status -ne "passed" -or $verification.allowUnpromoted -ne $true) {
        throw "Synthetic clean scan report did not pass strict unpromoted verification."
    }
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
            -CommitSha ("a" * 40) `
            -GitHubActionsRunUrl "https://github.com/example/accounts/actions/runs/123" `
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

try {
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

    Write-Host "Container supply-chain Trivy parser regression tests passed."
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot -PathType Container) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
