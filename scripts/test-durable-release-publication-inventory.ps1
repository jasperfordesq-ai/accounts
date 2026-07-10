param(
    [switch]$KeepFixture
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ("accounts-publication-inventory-test-" + [Guid]::NewGuid().ToString("N"))
$evidenceDirectory = Join-Path $fixtureRoot "private-publication"
$externalDirectory = Join-Path $evidenceDirectory "external-ixbrl"
$reportPath = Join-Path $fixtureRoot "publication-inventory-report.json"
New-Item -ItemType Directory -Path $externalDirectory -Force | Out-Null

$commitSha = "0123456789abcdef0123456789abcdef01234567"
$runUrl = "https://github.com/example/accounts/actions/runs/123456789"
$runCompletedAtUtc = [DateTimeOffset]::Parse("2026-07-10T10:30:00.0000000+00:00")
$manifestFileName = "release-evidence-publication-manifest.json"
$manifestPath = Join-Path $evidenceDirectory $manifestFileName
$trustPolicyPath = Join-Path $evidenceDirectory "release-evidence-trust-policy.json"
$verifierPath = Join-Path $PSScriptRoot "verify-durable-release-publication-inventory.ps1"
$canonicalScenarios = @(
    "micro-ltd",
    "small-abridged-ltd",
    "dac-small",
    "clg-charity",
    "medium-audit-required"
)

function Write-Utf8NoBom {
    param([string]$Path, [string]$Content)
    $encoding = New-Object Text.UTF8Encoding($false)
    [IO.File]::WriteAllText($Path, $Content, $encoding)
}

function Get-Sha256 {
    param([string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function New-Descriptor {
    param([string]$RelativePath, [string]$Classification, [string]$MediaType = "")
    return [pscustomobject]@{
        RelativePath = $RelativePath
        Classification = $Classification
        MediaType = $MediaType
    }
}

function Get-MediaType {
    param([string]$RelativePath, [string]$ExplicitMediaType)
    if (-not [string]::IsNullOrWhiteSpace($ExplicitMediaType)) { return $ExplicitMediaType }
    switch ([IO.Path]::GetExtension($RelativePath).ToLowerInvariant()) {
        ".json" { return "application/json" }
        ".md" { return "text/markdown; charset=utf-8" }
        ".txt" { return "text/plain; charset=utf-8" }
        ".log" { return "text/plain; charset=utf-8" }
        ".html" { return "text/html; charset=utf-8" }
        ".xhtml" { return "application/xhtml+xml; charset=utf-8" }
        default { return "application/octet-stream" }
    }
}

function Write-ManifestAndPinnedPolicy {
    param([object[]]$Descriptors)

    $entries = [System.Collections.Generic.List[object]]::new()
    foreach ($descriptor in $Descriptors) {
        $path = Join-Path $evidenceDirectory $descriptor.RelativePath.Replace('/', [IO.Path]::DirectorySeparatorChar)
        $item = Get-Item -LiteralPath $path
        $entries.Add([ordered]@{
            relativePath = $descriptor.RelativePath
            byteSize = $item.Length
            sha256 = Get-Sha256 $path
            mediaType = Get-MediaType $descriptor.RelativePath $descriptor.MediaType
            classification = $descriptor.Classification
        }) | Out-Null
    }
    $manifest = [ordered]@{
        schemaVersion = "accounts.release-evidence.publication-manifest/v1"
        releaseCandidate = [ordered]@{
            commitSha = $commitSha
            githubActionsRunUrl = $runUrl
            githubActionsCompletedAtUtc = $runCompletedAtUtc.ToString("O")
        }
        files = @($entries)
    }
    Write-Utf8NoBom $manifestPath ($manifest | ConvertTo-Json -Depth 8)

    $trustPolicy = [ordered]@{
        schemaVersion = "accounts.release-evidence.trust-policy/v1"
        releaseCandidate = [ordered]@{
            commitSha = $commitSha
            githubActionsRunUrl = $runUrl
            githubActionsCompletedAtUtc = $runCompletedAtUtc.ToString("O")
        }
        trustedRoots = @()
        signers = @()
        publicationManifest = [ordered]@{
            fileName = $manifestFileName
            sha256 = Get-Sha256 $manifestPath
        }
        policyNotice = "Synthetic publication-inventory fixture only; not human or external acceptance evidence."
    }
    Write-Utf8NoBom $trustPolicyPath ($trustPolicy | ConvertTo-Json -Depth 8)
}

function Update-PolicyManifestPin {
    $trustPolicy = Get-Content -LiteralPath $trustPolicyPath -Raw | ConvertFrom-Json
    $trustPolicy.publicationManifest.sha256 = Get-Sha256 $manifestPath
    Write-Utf8NoBom $trustPolicyPath ($trustPolicy | ConvertTo-Json -Depth 8)
}

function Invoke-InventoryVerifier {
    & $verifierPath `
        -EvidenceDirectory $evidenceDirectory `
        -TrustPolicyPath $trustPolicyPath `
        -TrustPolicySha256 (Get-Sha256 $trustPolicyPath) `
        -CommitSha $commitSha `
        -GitHubActionsRunUrl $runUrl `
        -CandidateRunCompletedAtUtc $runCompletedAtUtc `
        -ReportPath $reportPath
}

function Invoke-ExpectedFailure {
    param([string]$FailurePattern, [string]$CaseName)

    $threw = $false
    try {
        Invoke-InventoryVerifier
    } catch {
        $threw = $true
    }
    if (-not $threw) {
        throw "Expected publication inventory case '$CaseName' to fail."
    }
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    $failureText = @($report.failures) -join "`n"
    if ($report.status -ne "failed" -or $failureText -notmatch $FailurePattern) {
        throw "Publication inventory case '$CaseName' did not report expected pattern '$FailurePattern'. Failures: $failureText"
    }
}

function Invoke-XhtmlContentFailure {
    param([string]$Content, [string]$FailurePattern, [string]$CaseName)

    $artifactPath = Join-Path $externalDirectory "micro-ltd.xhtml"
    $originalBytes = [IO.File]::ReadAllBytes($artifactPath)
    try {
        Write-Utf8NoBom $artifactPath $Content
        Write-ManifestAndPinnedPolicy @($descriptors)
        Invoke-ExpectedFailure $FailurePattern $CaseName
    } finally {
        [IO.File]::WriteAllBytes($artifactPath, $originalBytes)
        Write-ManifestAndPinnedPolicy @($descriptors)
    }
}

try {
    $descriptors = [System.Collections.Generic.List[object]]::new()
    $externalHashes = @{}
    $scenarioIndex = 0
    foreach ($scenarioCode in $canonicalScenarios) {
        $scenarioIndex++
        $relativePath = "external-ixbrl/$scenarioCode.xhtml"
        $artifactPath = Join-Path $evidenceDirectory $relativePath.Replace('/', [IO.Path]::DirectorySeparatorChar)
        Write-Utf8NoBom $artifactPath @"
<?xml version="1.0" encoding="UTF-8"?>
<html xmlns="http://www.w3.org/1999/xhtml" xmlns:ix="http://www.xbrl.org/2013/inlineXBRL">
  <head><title>Synthetic $scenarioCode external validation artifact</title></head>
  <body><ix:nonNumeric name="synthetic:Scenario" contextRef="c$scenarioIndex">$scenarioCode-$scenarioIndex</ix:nonNumeric></body>
</html>
"@
        $externalHashes[$scenarioCode] = Get-Sha256 $artifactPath
        $descriptors.Add((New-Descriptor $relativePath "external-ixbrl")) | Out-Null
    }

    $templatePath = Join-Path $evidenceDirectory "external-ros-ixbrl-validation-template.md"
    $tableRows = @($canonicalScenarios | ForEach-Object {
        "| $_ | external-ros-validation-ledger#$_ | $($externalHashes[$_]) | revenue-taxonomy-package-ledger#$_ | none | accepted |"
    }) -join "`n"
    Write-Utf8NoBom $templatePath @"
# Synthetic External ROS/iXBRL Validation

This fixture exercises publication inventory binding only and is never acceptance evidence.

| Scenario | External reference | Artifact hash | Taxonomy package | Warnings/errors | Decision |
| --- | --- | --- | --- | --- | --- |
$tableRows
"@
    $descriptors.Add((New-Descriptor "external-ros-ixbrl-validation-template.md" "release-evidence-template")) | Out-Null

    $templateItem = Get-Item -LiteralPath $templatePath
    $signatureStatement = [ordered]@{
        schemaVersion = "accounts.release-evidence.signature-statement/v1"
        releaseCandidate = [ordered]@{
            commitSha = $commitSha
            githubActionsRunUrl = $runUrl
            githubActionsCompletedAtUtc = $runCompletedAtUtc.ToString("O")
        }
        template = [ordered]@{
            fileName = "external-ros-ixbrl-validation-template.md"
            byteSize = $templateItem.Length
            sha256 = Get-Sha256 $templatePath
        }
        signer = [ordered]@{
            slot = "external-ros-ixbrl-reviewer"
            name = "Synthetic ROS Reviewer"
            professionalCapacity = "Synthetic publication inventory test"
            credentialReference = "https://credentials.example.invalid/synthetic"
        }
        certificate = [ordered]@{
            sha256Fingerprint = ("a" * 64)
            subjectRfc2253 = "CN=Synthetic ROS Reviewer"
            serialNumber = "01"
        }
        signedAtUtc = $runCompletedAtUtc.AddMinutes(1).ToString("O")
    }
    $statementJson = $signatureStatement | ConvertTo-Json -Depth 8 -Compress
    $signatureEnvelope = [ordered]@{
        schemaVersion = "accounts.release-evidence.detached-signature/v1"
        signatureAlgorithm = "openssl-evp-sha256"
        statementEncoding = "base64"
        statementBase64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($statementJson))
        signatureEncoding = "base64"
        signatureBase64 = [Convert]::ToBase64String([byte[]](1, 2, 3, 4))
        signerCertificatePem = "synthetic-sidecar-is-cryptographically-verified-by-the-separate-durable-verifier"
        certificateChainPem = @()
    }
    $signatureRelativePath = "external-ros-ixbrl-validation-template.md.external-ros-ixbrl-reviewer.signature.json"
    Write-Utf8NoBom (Join-Path $evidenceDirectory $signatureRelativePath) ($signatureEnvelope | ConvertTo-Json -Depth 8)
    $descriptors.Add((New-Descriptor $signatureRelativePath "detached-signature")) | Out-Null

    $readmeRelativePath = "publication-scope.txt"
    Write-Utf8NoBom (Join-Path $evidenceDirectory $readmeRelativePath) "Synthetic private publication inventory fixture; contains no real filing data."
    $descriptors.Add((New-Descriptor $readmeRelativePath "supporting-evidence")) | Out-Null

    Write-ManifestAndPinnedPolicy @($descriptors)
    Invoke-InventoryVerifier
    $positiveReport = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    if ($positiveReport.status -ne "passed" -or
        $positiveReport.regularEvidenceFileCount -ne $descriptors.Count -or
        @($positiveReport.externalIxbrlScenarioCodes).Count -ne 5) {
        throw "Positive publication inventory fixture did not pass the exact file/scenario contract."
    }

    $extraPath = Join-Path $evidenceDirectory "unmanifested-extra.txt"
    Write-Utf8NoBom $extraPath "not in manifest"
    Invoke-ExpectedFailure "unmanifested extra regular evidence file" "extra file"
    Remove-Item -LiteralPath $extraPath -Force

    $driftPath = Join-Path $externalDirectory "micro-ltd.xhtml"
    $originalDriftBytes = [IO.File]::ReadAllBytes($driftPath)
    [IO.File]::AppendAllText($driftPath, "`n<!-- drift -->", (New-Object Text.UTF8Encoding($false)))
    Invoke-ExpectedFailure "(byteSize|sha256) does not match" "hash drift"
    [IO.File]::WriteAllBytes($driftPath, $originalDriftBytes)

    $originalManifestBytes = [IO.File]::ReadAllBytes($manifestPath)
    $originalPolicyBytes = [IO.File]::ReadAllBytes($trustPolicyPath)
    $traversalManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $traversalManifest.files[0].relativePath = "../outside.xhtml"
    Write-Utf8NoBom $manifestPath ($traversalManifest | ConvertTo-Json -Depth 8)
    Update-PolicyManifestPin
    Invoke-ExpectedFailure "(unsafe path segment|escapes the evidence directory|safe canonical)" "path traversal"
    [IO.File]::WriteAllBytes($manifestPath, $originalManifestBytes)
    [IO.File]::WriteAllBytes($trustPolicyPath, $originalPolicyBytes)

    $deviceNameManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $deviceNameManifest.files[0].relativePath = "NUL.json"
    Write-Utf8NoBom $manifestPath ($deviceNameManifest | ConvertTo-Json -Depth 8)
    Update-PolicyManifestPin
    Invoke-ExpectedFailure "reserved Windows device path segment" "cross-platform reserved device name"
    [IO.File]::WriteAllBytes($manifestPath, $originalManifestBytes)
    [IO.File]::WriteAllBytes($trustPolicyPath, $originalPolicyBytes)

    $caseVariantRelativePath = "Publication-Scope.txt"
    $caseVariantPath = Join-Path $evidenceDirectory $caseVariantRelativePath
    $caseVariantCreated = $false
    if ([IO.Path]::DirectorySeparatorChar -ne '\') {
        Copy-Item -LiteralPath (Join-Path $evidenceDirectory $readmeRelativePath) -Destination $caseVariantPath
        $caseVariantCreated = $true
    }
    try {
        $caseCollisionDescriptors = @($descriptors) + @((New-Descriptor $caseVariantRelativePath "supporting-evidence"))
        Write-ManifestAndPinnedPolicy $caseCollisionDescriptors
        Invoke-ExpectedFailure "duplicate or OS-ambiguous relativePath|OS-ambiguous duplicate path" "case-variant path collision"
    } finally {
        if ($caseVariantCreated) {
            Remove-Item -LiteralPath $caseVariantPath -Force
        }
        Write-ManifestAndPinnedPolicy @($descriptors)
    }

    $reservedDirectory = Join-Path $evidenceDirectory "verified-publication"
    New-Item -ItemType Directory -Path $reservedDirectory -Force | Out-Null
    foreach ($reservedRelativePath in @(
        "verified-publication/durable-release-evidence-report.json",
        "verified-publication/nested/attacker-controlled.json"
    )) {
        $reservedPath = Join-Path $evidenceDirectory $reservedRelativePath.Replace('/', [IO.Path]::DirectorySeparatorChar)
        New-Item -ItemType Directory -Path (Split-Path -Parent $reservedPath) -Force | Out-Null
        Write-Utf8NoBom $reservedPath '{"synthetic":"reserved staging collision"}'
        try {
            $reservedDescriptors = @($descriptors) + @((New-Descriptor $reservedRelativePath "supporting-evidence"))
            Write-ManifestAndPinnedPolicy $reservedDescriptors
            Invoke-ExpectedFailure "reserved publication staging namespace" "reserved staging collision $reservedRelativePath"
        } finally {
            Remove-Item -LiteralPath $reservedPath -Force
            Write-ManifestAndPinnedPolicy @($descriptors)
        }
    }
    Remove-Item -LiteralPath $reservedDirectory -Recurse -Force

    $entityEncodedJavascript = @"
<?xml version="1.0" encoding="UTF-8"?>
<html xmlns="http://www.w3.org/1999/xhtml"><body><a href="java&#x73;cript:alert(1)">unsafe</a></body></html>
"@
    Invoke-XhtmlContentFailure $entityEncodedJavascript "externally resolving HTML URL attributes" "entity-encoded javascript URL"

    $metaRefresh = @"
<?xml version="1.0" encoding="UTF-8"?>
<html xmlns="http://www.w3.org/1999/xhtml"><head><meta http-equiv="refresh" content="0;url=https://evil.invalid" /></head><body>unsafe</body></html>
"@
    Invoke-XhtmlContentFailure $metaRefresh "meta http-equiv" "meta refresh"

    $embeddedActiveContent = @"
<?xml version="1.0" encoding="UTF-8"?>
<html xmlns="http://www.w3.org/1999/xhtml"><body><iframe src="https://evil.invalid"></iframe><object data="https://evil.invalid"></object></body></html>
"@
    Invoke-XhtmlContentFailure $embeddedActiveContent "forbidden active or externally loading HTML element" "embedded active content"

    $escapedCssExternalUrl = @"
<?xml version="1.0" encoding="UTF-8"?>
<html xmlns="http://www.w3.org/1999/xhtml"><head><style>body { background: u\72l(https://evil.invalid/a.png); }</style></head><body>unsafe</body></html>
"@
    Invoke-XhtmlContentFailure $escapedCssExternalUrl "active or externally resolving CSS" "escaped CSS external URL"

    $remoteImage = @"
<?xml version="1.0" encoding="UTF-8"?>
<html xmlns="http://www.w3.org/1999/xhtml"><body><img src="https://evil.invalid/pixel" alt="" /></body></html>
"@
    Invoke-XhtmlContentFailure $remoteImage "forbidden active or externally loading HTML element|externally resolving HTML URL attributes" "remote image"

    $anchorPing = @"
<?xml version="1.0" encoding="UTF-8"?>
<html xmlns="http://www.w3.org/1999/xhtml"><body><a href="#local" ping="https://evil.invalid/collect">unsafe</a><span id="local">target</span></body></html>
"@
    Invoke-XhtmlContentFailure $anchorPing "externally resolving HTML URL attributes" "anchor ping beacon"

    $signaturePath = Join-Path $evidenceDirectory $signatureRelativePath
    $originalSignatureBytes = [IO.File]::ReadAllBytes($signaturePath)
    try {
        $privateKeyMarker = "-----BEGIN PRIVATE KEY-----`nsynthetic-forbidden-material`n-----END PRIVATE KEY-----"
        $mutatedSignature = Get-Content -LiteralPath $signaturePath -Raw | ConvertFrom-Json
        $mutatedSignature.signerCertificatePem = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($privateKeyMarker))
        Write-Utf8NoBom $signaturePath ($mutatedSignature | ConvertTo-Json -Depth 8)
        Write-ManifestAndPinnedPolicy @($descriptors)
        Invoke-ExpectedFailure "base64-encoded private-key material" "encoded private key in JSON"
    } finally {
        [IO.File]::WriteAllBytes($signaturePath, $originalSignatureBytes)
        Write-ManifestAndPinnedPolicy @($descriptors)
    }

    $linkTargetPath = Join-Path $fixtureRoot "outside-link-target.txt"
    $linkPath = Join-Path $evidenceDirectory "forbidden-link.txt"
    Write-Utf8NoBom $linkTargetPath "outside publication root"
    $linkCreated = $false
    try {
        New-Item -ItemType SymbolicLink -Path $linkPath -Target $linkTargetPath -Force | Out-Null
        $linkCreated = $true
        Invoke-ExpectedFailure "(symbolic link|reparse point|junction)" "symbolic link"
    } catch {
        if ($linkCreated) { throw }
        Write-Host "Publication inventory symlink case skipped because this host cannot create symbolic links: $($_.Exception.Message)"
    } finally {
        if (Test-Path -LiteralPath $linkPath) {
            Remove-Item -LiteralPath $linkPath -Force -ErrorAction SilentlyContinue
        }
    }

    $archiveRelativePath = "forbidden-archive.zip"
    $archivePath = Join-Path $evidenceDirectory $archiveRelativePath
    [IO.File]::WriteAllBytes($archivePath, [byte[]](0x50, 0x4B, 0x03, 0x04, 0x00))
    $archiveDescriptors = @($descriptors) + @((New-Descriptor $archiveRelativePath "supporting-evidence" "application/zip"))
    Write-ManifestAndPinnedPolicy $archiveDescriptors
    Invoke-ExpectedFailure "unsupported file extension.*archives and opaque binaries are forbidden" "archive"
    Remove-Item -LiteralPath $archivePath -Force
    Write-ManifestAndPinnedPolicy @($descriptors)

    $missingScenarioPath = Join-Path $externalDirectory "medium-audit-required.xhtml"
    $missingScenarioBytes = [IO.File]::ReadAllBytes($missingScenarioPath)
    Remove-Item -LiteralPath $missingScenarioPath -Force
    $missingDescriptors = @($descriptors | Where-Object RelativePath -ne "external-ixbrl/medium-audit-required.xhtml")
    Write-ManifestAndPinnedPolicy $missingDescriptors
    Invoke-ExpectedFailure "(exactly five external-ixbrl|missing external-ixbrl evidence.*medium-audit-required)" "missing external scenario"
    [IO.File]::WriteAllBytes($missingScenarioPath, $missingScenarioBytes)
    Write-ManifestAndPinnedPolicy @($descriptors)

    $canonicalScenarioPath = Join-Path $externalDirectory "micro-ltd.xhtml"
    $wrongScenarioPath = Join-Path $externalDirectory "not-a-canonical-scenario.xhtml"
    Move-Item -LiteralPath $canonicalScenarioPath -Destination $wrongScenarioPath
    $wrongScenarioDescriptors = @($descriptors | ForEach-Object {
        if ($_.RelativePath -eq "external-ixbrl/micro-ltd.xhtml") {
            New-Descriptor "external-ixbrl/not-a-canonical-scenario.xhtml" "external-ixbrl"
        } else {
            $_
        }
    })
    Write-ManifestAndPinnedPolicy $wrongScenarioDescriptors
    Invoke-ExpectedFailure "(must use a canonical scenario code|missing external-ixbrl evidence.*micro-ltd)" "noncanonical external scenario"
    Move-Item -LiteralPath $wrongScenarioPath -Destination $canonicalScenarioPath
    Write-ManifestAndPinnedPolicy @($descriptors)

    Invoke-InventoryVerifier
    Write-Host "Durable release publication inventory synthetic tests passed: positive, extra, drift, traversal, reserved-device, case-collision, staging-collision, active-content, encoded-private-key, symlink-if-supported, archive, missing, and noncanonical scenario cases."
} finally {
    if ($KeepFixture) {
        Write-Host "Synthetic publication inventory fixture retained at $fixtureRoot"
    } else {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
