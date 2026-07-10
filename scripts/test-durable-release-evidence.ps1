param(
    [string]$OpenSslPath = "",
    [switch]$KeepFixture
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

Import-Module (Join-Path $PSScriptRoot "ReleaseEvidenceCrypto.psm1") -Force

$openssl = Resolve-AccountsOpenSsl $OpenSslPath
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ("accounts-durable-release-evidence-test-" + [Guid]::NewGuid().ToString("N"))
$evidenceDirectory = Join-Path $fixtureRoot "evidence"
$keyDirectory = Join-Path $fixtureRoot "private-keys"
$trustDirectory = Join-Path $evidenceDirectory "trust"
New-Item -ItemType Directory -Path $evidenceDirectory, $keyDirectory, $trustDirectory -Force | Out-Null

$commitSha = "0123456789abcdef0123456789abcdef01234567"
$runUrl = "https://github.com/example/accounts/actions/runs/123456789"
$runCompletedAtUtc = [DateTimeOffset]::UtcNow.AddMinutes(-2)
$credentialVerifiedAtUtc = $runCompletedAtUtc.AddMinutes(-1)

$signers = @(
    [pscustomobject]@{ TemplateFile = "visual-qa-signoff-template.md"; Slot = "visual-qa-reviewer"; Name = "Synthetic Visual Reviewer"; Capacity = "Independent visual accessibility reviewer" },
    [pscustomobject]@{ TemplateFile = "source-law-review-template.md"; Slot = "source-law-reviewer"; Name = "Synthetic Source Law Reviewer"; Capacity = "Irish company-law source reviewer" },
    [pscustomobject]@{ TemplateFile = "source-law-review-template.md"; Slot = "source-law-qualified-accountant"; Name = "Synthetic Source Accountant"; Capacity = "Chartered accountant reviewing source-law impact" },
    [pscustomobject]@{ TemplateFile = "external-ros-ixbrl-validation-template.md"; Slot = "external-ros-ixbrl-reviewer"; Name = "Synthetic ROS Reviewer"; Capacity = "External ROS and iXBRL validation reviewer" },
    [pscustomobject]@{ TemplateFile = "qualified-accountant-acceptance-template.md"; Slot = "qualified-accountant"; Name = "Synthetic Acceptance Accountant"; Capacity = "Chartered accountant performing release acceptance" },
    [pscustomobject]@{ TemplateFile = "manual-handoff-acceptance-template.md"; Slot = "manual-handoff-reviewer"; Name = "Synthetic Handoff Reviewer"; Capacity = "Independent manual filing handoff reviewer" },
    [pscustomobject]@{ TemplateFile = "monitoring-provider-confirmation-template.md"; Slot = "monitoring-release-operator"; Name = "Synthetic Release Operator"; Capacity = "Production monitoring release operator" }
)

function Invoke-ExpectedVerificationFailure {
    param([string]$ExpectedFailurePattern, [string]$TrustPolicySha256)

    $threw = $false
    try {
        & (Join-Path $PSScriptRoot "verify-durable-release-evidence.ps1") `
            -EvidenceDirectory $evidenceDirectory `
            -TrustPolicyPath $trustPolicyPath `
            -TrustPolicySha256 $TrustPolicySha256 `
            -CommitSha $commitSha `
            -GitHubActionsRunUrl $runUrl `
            -CandidateRunCompletedAtUtc $runCompletedAtUtc `
            -ReportPath $reportPath `
            -OpenSslPath $openssl
    } catch {
        $threw = $true
    }

    if (-not $threw) {
        throw "Expected durable release-evidence verification to fail."
    }

    $failureReport = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    if ($failureReport.status -ne "failed" -or (@($failureReport.failures) -join "`n") -notmatch $ExpectedFailurePattern) {
        throw "Expected failure report to contain pattern '$ExpectedFailurePattern'."
    }
}

function Invoke-ExpectedActionFailure {
    param(
        [Parameter(Mandatory = $true)][scriptblock]$Action,
        [Parameter(Mandatory = $true)][string]$ExpectedFailurePattern
    )

    $caughtMessage = ""
    try {
        & $Action
    } catch {
        $caughtMessage = $_.Exception.Message
    }
    if ($caughtMessage -notmatch $ExpectedFailurePattern) {
        throw "Expected action failure matching '$ExpectedFailurePattern'; received '$caughtMessage'."
    }
}

try {
    $rootKeyPath = Join-Path $keyDirectory "synthetic-root.key.pem"
    $rootCertificatePath = Join-Path $trustDirectory "synthetic-root.pem"
    Invoke-AccountsOpenSsl $openssl @("ecparam", "-name", "prime256v1", "-genkey", "-noout", "-out", $rootKeyPath) "Create synthetic trusted root key" | Out-Null
    Invoke-AccountsOpenSsl $openssl @(
        "req", "-x509", "-new", "-key", $rootKeyPath, "-sha256",
        "-out", $rootCertificatePath, "-days", "2",
        "-subj", "/CN=Synthetic Release Evidence Root",
        "-addext", "basicConstraints=critical,CA:TRUE",
        "-addext", "keyUsage=critical,keyCertSign,cRLSign"
    ) "Create synthetic trusted root" | Out-Null

    $leafExtensionsPath = Join-Path $fixtureRoot "leaf-extensions.cnf"
    Write-AccountsUtf8NoBom $leafExtensionsPath @"
basicConstraints=critical,CA:FALSE
keyUsage=critical,digitalSignature
extendedKeyUsage=clientAuth,emailProtection
subjectKeyIdentifier=hash
authorityKeyIdentifier=keyid,issuer
"@

    $policySigners = [System.Collections.Generic.List[object]]::new()
    $certificateInventory = @{}
    $serial = 1000
    foreach ($signer in $signers) {
        $templatePath = Join-Path $evidenceDirectory $signer.TemplateFile
        if (-not (Test-Path -LiteralPath $templatePath)) {
            Write-AccountsUtf8NoBom $templatePath @"
# Synthetic completed release evidence

- Commit SHA: $commitSha
- GitHub Actions run URL: $runUrl
- Synthetic fixture only: behavioral cryptographic verification, never human acceptance.
"@
        }

        $safeSlot = $signer.Slot
        $keyPath = Join-Path $keyDirectory "$safeSlot.key.pem"
        $csrPath = Join-Path $fixtureRoot "$safeSlot.csr.pem"
        $certificatePath = Join-Path $fixtureRoot "$safeSlot.cert.pem"
        Invoke-AccountsOpenSsl $openssl @("ecparam", "-name", "prime256v1", "-genkey", "-noout", "-out", $keyPath) "Create synthetic signer key for $safeSlot" | Out-Null
        Invoke-AccountsOpenSsl $openssl @(
            "req", "-new", "-key", $keyPath, "-sha256",
            "-out", $csrPath, "-subj", "/CN=$($signer.Name)"
        ) "Create synthetic signer key and CSR for $safeSlot" | Out-Null
        Invoke-AccountsOpenSsl $openssl @(
            "x509", "-req", "-in", $csrPath, "-CA", $rootCertificatePath, "-CAkey", $rootKeyPath,
            "-set_serial", ([string]$serial), "-out", $certificatePath, "-days", "2", "-sha256",
            "-extfile", $leafExtensionsPath
        ) "Issue synthetic signer certificate for $safeSlot" | Out-Null
        $serial++

        $credentialReference = "https://credentials.example.invalid/professionals/$safeSlot"
        $signaturePath = Join-Path $evidenceDirectory "$($signer.TemplateFile).$safeSlot.signature.json"
        $signingTime = [DateTimeOffset]::UtcNow
        & (Join-Path $PSScriptRoot "new-release-evidence-signature.ps1") `
            -TemplatePath $templatePath `
            -OutputPath $signaturePath `
            -SignerSlot $safeSlot `
            -SignerName $signer.Name `
            -ProfessionalCapacity $signer.Capacity `
            -CredentialReference $credentialReference `
            -CommitSha $commitSha `
            -GitHubActionsRunUrl $runUrl `
            -CandidateRunCompletedAtUtc $runCompletedAtUtc `
            -CertificatePath $certificatePath `
            -PrivateKeyPath $keyPath `
            -SignedAtUtc $signingTime `
            -OpenSslPath $openssl | Out-Null

        $fingerprint = Get-AccountsCertificateFingerprint $openssl $certificatePath
        $subject = Get-AccountsCertificateSubject $openssl $certificatePath
        $certificateInventory[$safeSlot] = [pscustomobject]@{ CertificatePath = $certificatePath; KeyPath = $keyPath; Fingerprint = $fingerprint; Subject = $subject }
        $policySigners.Add([ordered]@{
            templateFile = $signer.TemplateFile
            signerSlot = $safeSlot
            expectedName = $signer.Name
            expectedProfessionalCapacity = $signer.Capacity
            expectedCredentialReference = $credentialReference
            expectedCertificateSubjectRfc2253 = $subject
            allowedCertificateFingerprintsSha256 = @($fingerprint)
            trustedRootId = "synthetic-release-root"
            credentialVerification = [ordered]@{
                status = "verified"
                verifiedAtUtc = $credentialVerifiedAtUtc.ToString("O")
                verifiedBy = "Synthetic credential policy fixture"
                evidenceReference = "https://credentials.example.invalid/verifications/$safeSlot"
            }
        }) | Out-Null
    }

    $trustPolicyPath = Join-Path $evidenceDirectory "release-evidence-trust-policy.json"
    $trustPolicy = [ordered]@{
        schemaVersion = "accounts.release-evidence.trust-policy/v1"
        releaseCandidate = [ordered]@{
            commitSha = $commitSha
            githubActionsRunUrl = $runUrl
            githubActionsCompletedAtUtc = $runCompletedAtUtc.ToString("O")
        }
        trustedRoots = @([ordered]@{
            rootId = "synthetic-release-root"
            certificateFile = "trust/synthetic-root.pem"
            sha256Fingerprint = Get-AccountsCertificateFingerprint $openssl $rootCertificatePath
        })
        signers = @($policySigners)
        policyNotice = "Synthetic fixture only; this policy is not reviewer identity or qualification evidence."
    }
    Write-AccountsUtf8NoBom $trustPolicyPath ($trustPolicy | ConvertTo-Json -Depth 10)
    $trustPolicySha256 = Get-AccountsFileSha256 $trustPolicyPath
    $reportPath = Join-Path $evidenceDirectory "durable-release-evidence-report.json"

    & (Join-Path $PSScriptRoot "verify-durable-release-evidence.ps1") `
        -EvidenceDirectory $evidenceDirectory `
        -TrustPolicyPath $trustPolicyPath `
        -TrustPolicySha256 $trustPolicySha256 `
        -CommitSha $commitSha `
        -GitHubActionsRunUrl $runUrl `
        -CandidateRunCompletedAtUtc $runCompletedAtUtc `
        -ReportPath $reportPath `
        -OpenSslPath $openssl
    $positiveReport = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    if ($positiveReport.status -ne "passed" -or $positiveReport.verifiedSignatureCount -ne 7) {
        throw "Positive synthetic signature verification did not pass all seven signer slots."
    }
    if (@($positiveReport.signatureResults | Where-Object {
        -not $_.certificateKeyUsageValid -or
        -not $_.certificateBasicConstraintsValid -or
        -not $_.certificateExtendedKeyUsageValid -or
        -not $_.certificateSecurityLevelValid -or
        [int]$_.certificatePublicKeyBits -lt 224 -or
        [string]::IsNullOrWhiteSpace([string]$_.certificatePublicKeyAlgorithm) -or
        -not $_.certificateChainValidAtSigningTime -or
        -not $_.certificateChainValidAtVerificationTime
    }).Count -ne 0) {
        throw "Positive synthetic signature verification did not retain every certificate policy and current-validity control."
    }

    $unexpectedNestedDirectory = Join-Path $evidenceDirectory "nested"
    $unexpectedNestedSignature = Join-Path $unexpectedNestedDirectory "fake.signature.json"
    New-Item -ItemType Directory -Path $unexpectedNestedDirectory -Force | Out-Null
    Write-AccountsUtf8NoBom $unexpectedNestedSignature '{"synthetic":"unexpected detached signature sidecar"}'
    try {
        Invoke-ExpectedVerificationFailure "Unexpected detached signature sidecar is present: nested/fake.signature.json" $trustPolicySha256
    } finally {
        Remove-Item -LiteralPath $unexpectedNestedDirectory -Recurse -Force
    }

    $visualSigner = $signers | Where-Object Slot -eq "visual-qa-reviewer"
    $visualCertificate = $certificateInventory["visual-qa-reviewer"]
    $forbiddenKeyPath = Join-Path $evidenceDirectory "accidental-reviewer-private.key.pem"
    Copy-Item -LiteralPath $visualCertificate.KeyPath -Destination $forbiddenKeyPath
    try {
        Invoke-ExpectedActionFailure -ExpectedFailurePattern "must remain outside" -Action {
            & (Join-Path $PSScriptRoot "new-release-evidence-signature.ps1") `
                -TemplatePath (Join-Path $evidenceDirectory $visualSigner.TemplateFile) `
                -OutputPath (Join-Path $evidenceDirectory "forbidden-private-material.signature.json") `
                -SignerSlot $visualSigner.Slot `
                -SignerName $visualSigner.Name `
                -ProfessionalCapacity $visualSigner.Capacity `
                -CredentialReference "https://credentials.example.invalid/professionals/visual-qa-reviewer" `
                -CommitSha $commitSha `
                -GitHubActionsRunUrl $runUrl `
                -CandidateRunCompletedAtUtc $runCompletedAtUtc `
                -CertificatePath $visualCertificate.CertificatePath `
                -PrivateKeyPath $forbiddenKeyPath `
                -OpenSslPath $openssl | Out-Null
        }
    } finally {
        Remove-Item -LiteralPath $forbiddenKeyPath -Force -ErrorAction SilentlyContinue
    }

    Invoke-ExpectedActionFailure -ExpectedFailurePattern "without credentials, query parameters, or fragments" -Action {
        & (Join-Path $PSScriptRoot "new-release-evidence-signature.ps1") `
            -TemplatePath (Join-Path $evidenceDirectory $visualSigner.TemplateFile) `
            -OutputPath (Join-Path $evidenceDirectory "forbidden-credential-url.signature.json") `
            -SignerSlot $visualSigner.Slot `
            -SignerName $visualSigner.Name `
            -ProfessionalCapacity $visualSigner.Capacity `
            -CredentialReference "https://user:secret@credentials.example.invalid/reviewer?token=secret#fragment" `
            -CommitSha $commitSha `
            -GitHubActionsRunUrl $runUrl `
            -CandidateRunCompletedAtUtc $runCompletedAtUtc `
            -CertificatePath $visualCertificate.CertificatePath `
            -PrivateKeyPath $visualCertificate.KeyPath `
            -OpenSslPath $openssl | Out-Null
    }

    Invoke-ExpectedActionFailure -ExpectedFailurePattern "cannot predate the successful candidate CI completion time" -Action {
        & (Join-Path $PSScriptRoot "new-release-evidence-signature.ps1") `
            -TemplatePath (Join-Path $evidenceDirectory $visualSigner.TemplateFile) `
            -OutputPath (Join-Path $evidenceDirectory "forbidden-backdated.signature.json") `
            -SignerSlot $visualSigner.Slot `
            -SignerName $visualSigner.Name `
            -ProfessionalCapacity $visualSigner.Capacity `
            -CredentialReference "https://credentials.example.invalid/professionals/visual-qa-reviewer" `
            -CommitSha $commitSha `
            -GitHubActionsRunUrl $runUrl `
            -CandidateRunCompletedAtUtc $runCompletedAtUtc `
            -CertificatePath $visualCertificate.CertificatePath `
            -PrivateKeyPath $visualCertificate.KeyPath `
            -SignedAtUtc $runCompletedAtUtc.AddSeconds(-1) `
            -OpenSslPath $openssl | Out-Null
    }

    $originalPolicyBytesForIndependence = [IO.File]::ReadAllBytes($trustPolicyPath)
    $selfVerifiedPolicy = Get-Content -LiteralPath $trustPolicyPath -Raw | ConvertFrom-Json
    $selfVerifiedPolicy.signers[0].credentialVerification.verifiedBy = $selfVerifiedPolicy.signers[0].expectedName
    Write-AccountsUtf8NoBom $trustPolicyPath ($selfVerifiedPolicy | ConvertTo-Json -Depth 10)
    Invoke-ExpectedVerificationFailure "must identify an independent person" (Get-AccountsFileSha256 $trustPolicyPath)
    [IO.File]::WriteAllBytes($trustPolicyPath, $originalPolicyBytesForIndependence)

    $originalRootBytes = [IO.File]::ReadAllBytes($rootCertificatePath)
    try {
        [IO.File]::AppendAllText($rootCertificatePath, "`n" + [IO.File]::ReadAllText($visualCertificate.CertificatePath))
        Invoke-ExpectedVerificationFailure "exactly one PEM certificate" $trustPolicySha256
    } finally {
        [IO.File]::WriteAllBytes($rootCertificatePath, $originalRootBytes)
    }

    if ([IO.Path]::DirectorySeparatorChar -eq '/') {
        $caseVariantSibling = Join-Path $fixtureRoot "Evidence"
        New-Item -ItemType Directory -Path $caseVariantSibling | Out-Null
        Copy-Item -LiteralPath $rootCertificatePath -Destination (Join-Path $caseVariantSibling "root.pem")
        $originalContainmentPolicyBytes = [IO.File]::ReadAllBytes($trustPolicyPath)
        $caseVariantPolicy = Get-Content -LiteralPath $trustPolicyPath -Raw | ConvertFrom-Json
        $caseVariantPolicy.trustedRoots[0].certificateFile = "../Evidence/root.pem"
        Write-AccountsUtf8NoBom $trustPolicyPath ($caseVariantPolicy | ConvertTo-Json -Depth 10)
        Invoke-ExpectedVerificationFailure "must remain inside its trust-policy directory" (Get-AccountsFileSha256 $trustPolicyPath)
        [IO.File]::WriteAllBytes($trustPolicyPath, $originalContainmentPolicyBytes)
    }

    $decodedKeySignaturePath = Join-Path $evidenceDirectory "visual-qa-signoff-template.md.visual-qa-reviewer.signature.json"
    $originalDecodedKeySignatureBytes = [IO.File]::ReadAllBytes($decodedKeySignaturePath)
    $decodedKeyEnvelope = Get-Content -LiteralPath $decodedKeySignaturePath -Raw | ConvertFrom-Json
    $decodedKeyEnvelope.certificateChainPem = @("-----BEGIN PRIVATE KEY-----`nYWJj`n-----END PRIVATE KEY-----")
    $escapedDecodedKeyJson = ($decodedKeyEnvelope | ConvertTo-Json -Depth 8).Replace("PRIVATE KEY", "PRIVATE\u0020KEY")
    Write-AccountsUtf8NoBom $decodedKeySignaturePath $escapedDecodedKeyJson
    Invoke-ExpectedVerificationFailure "Decoded certificateChainPem.*no private-key material" $trustPolicySha256
    [IO.File]::WriteAllBytes($decodedKeySignaturePath, $originalDecodedKeySignatureBytes)

    Invoke-ExpectedVerificationFailure "Trust policy SHA-256 does not match" ("f" * 64)

    $tamperTemplatePath = Join-Path $evidenceDirectory "visual-qa-signoff-template.md"
    $originalTemplateBytes = [IO.File]::ReadAllBytes($tamperTemplatePath)
    [IO.File]::AppendAllText($tamperTemplatePath, "`nunauthorised change")
    Invoke-ExpectedVerificationFailure "Template (byte size|SHA-256) has changed since signing" $trustPolicySha256
    [IO.File]::WriteAllBytes($tamperTemplatePath, $originalTemplateBytes)

    $tamperSignaturePath = Join-Path $evidenceDirectory "qualified-accountant-acceptance-template.md.qualified-accountant.signature.json"
    $originalSignatureBytes = [IO.File]::ReadAllBytes($tamperSignaturePath)
    $tamperedEnvelope = Get-Content -LiteralPath $tamperSignaturePath -Raw | ConvertFrom-Json
    $tamperedStatement = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($tamperedEnvelope.statementBase64)) | ConvertFrom-Json
    $tamperedStatement.signer.name = "Tampered Accountant Name"
    $tamperedStatementJson = $tamperedStatement | ConvertTo-Json -Depth 8 -Compress
    $tamperedEnvelope.statementBase64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($tamperedStatementJson))
    Write-AccountsUtf8NoBom $tamperSignaturePath ($tamperedEnvelope | ConvertTo-Json -Depth 8)
    Invoke-ExpectedVerificationFailure "(Signed signer name does not match|Detached signature verification failed)" $trustPolicySha256
    [IO.File]::WriteAllBytes($tamperSignaturePath, $originalSignatureBytes)

    $sourceReviewer = $signers | Where-Object Slot -eq "source-law-reviewer"
    $sourceAccountant = $signers | Where-Object Slot -eq "source-law-qualified-accountant"
    $sourceReviewerCertificate = $certificateInventory[$sourceReviewer.Slot]
    $sourceAccountantSignaturePath = Join-Path $evidenceDirectory "$($sourceAccountant.TemplateFile).$($sourceAccountant.Slot).signature.json"
    $originalSourceAccountantSignatureBytes = [IO.File]::ReadAllBytes($sourceAccountantSignaturePath)
    $originalSourceIndependencePolicyBytes = [IO.File]::ReadAllBytes($trustPolicyPath)
    Remove-Item -LiteralPath $sourceAccountantSignaturePath
    & (Join-Path $PSScriptRoot "new-release-evidence-signature.ps1") `
        -TemplatePath (Join-Path $evidenceDirectory $sourceAccountant.TemplateFile) `
        -OutputPath $sourceAccountantSignaturePath `
        -SignerSlot $sourceAccountant.Slot `
        -SignerName $sourceReviewer.Name `
        -ProfessionalCapacity $sourceReviewer.Capacity `
        -CredentialReference "https://credentials.example.invalid/professionals/$($sourceReviewer.Slot)" `
        -CommitSha $commitSha `
        -GitHubActionsRunUrl $runUrl `
        -CandidateRunCompletedAtUtc $runCompletedAtUtc `
        -CertificatePath $sourceReviewerCertificate.CertificatePath `
        -PrivateKeyPath $sourceReviewerCertificate.KeyPath `
        -SignedAtUtc ([DateTimeOffset]::UtcNow) `
        -OpenSslPath $openssl | Out-Null
    $duplicateSourcePolicy = Get-Content -LiteralPath $trustPolicyPath -Raw | ConvertFrom-Json
    $duplicateSourceAccountantPolicy = $duplicateSourcePolicy.signers | Where-Object signerSlot -eq $sourceAccountant.Slot
    $duplicateSourceAccountantPolicy.expectedName = $sourceReviewer.Name
    $duplicateSourceAccountantPolicy.expectedProfessionalCapacity = $sourceReviewer.Capacity
    $duplicateSourceAccountantPolicy.expectedCredentialReference = "https://credentials.example.invalid/professionals/$($sourceReviewer.Slot)"
    $duplicateSourceAccountantPolicy.expectedCertificateSubjectRfc2253 = $sourceReviewerCertificate.Subject
    $duplicateSourceAccountantPolicy.allowedCertificateFingerprintsSha256 = @($sourceReviewerCertificate.Fingerprint)
    Write-AccountsUtf8NoBom $trustPolicyPath ($duplicateSourcePolicy | ConvertTo-Json -Depth 10)
    Invoke-ExpectedVerificationFailure "must use distinct independently verified people and certificates" (Get-AccountsFileSha256 $trustPolicyPath)
    [IO.File]::WriteAllBytes($sourceAccountantSignaturePath, $originalSourceAccountantSignatureBytes)
    [IO.File]::WriteAllBytes($trustPolicyPath, $originalSourceIndependencePolicyBytes)

    $monitoringSigner = $signers | Where-Object Slot -eq "monitoring-release-operator"
    $monitoringSignaturePath = Join-Path $evidenceDirectory "$($monitoringSigner.TemplateFile).$($monitoringSigner.Slot).signature.json"
    $originalMonitoringSignatureBytes = [IO.File]::ReadAllBytes($monitoringSignaturePath)
    $originalMonitoringPolicyBytes = [IO.File]::ReadAllBytes($trustPolicyPath)

    $expiringKeyPath = Join-Path $keyDirectory "expiring-signer.key.pem"
    $expiringCsrPath = Join-Path $fixtureRoot "expiring-signer.csr.pem"
    $expiringCertificatePath = Join-Path $fixtureRoot "expiring-signer.cert.pem"
    Invoke-AccountsOpenSsl $openssl @("ecparam", "-name", "prime256v1", "-genkey", "-noout", "-out", $expiringKeyPath) "Create expiring signer key" | Out-Null
    Invoke-AccountsOpenSsl $openssl @("req", "-new", "-key", $expiringKeyPath, "-sha256", "-out", $expiringCsrPath, "-subj", "/CN=$($monitoringSigner.Name)") "Create expiring signer CSR" | Out-Null
    $expiringNotBefore = [DateTimeOffset]::UtcNow.AddMinutes(-5)
    $expiringNotAfter = [DateTimeOffset]::UtcNow.AddSeconds(5)
    Invoke-AccountsOpenSsl $openssl @(
        "x509", "-req", "-in", $expiringCsrPath, "-CA", $rootCertificatePath, "-CAkey", $rootKeyPath,
        "-set_serial", "8001", "-out", $expiringCertificatePath, "-sha256", "-extfile", $leafExtensionsPath,
        "-not_before", $expiringNotBefore.ToString("yyyyMMddHHmmss'Z'"),
        "-not_after", $expiringNotAfter.ToString("yyyyMMddHHmmss'Z'")
    ) "Issue a soon-expiring synthetic signer certificate" | Out-Null
    Remove-Item -LiteralPath $monitoringSignaturePath
    & (Join-Path $PSScriptRoot "new-release-evidence-signature.ps1") `
        -TemplatePath (Join-Path $evidenceDirectory $monitoringSigner.TemplateFile) `
        -OutputPath $monitoringSignaturePath `
        -SignerSlot $monitoringSigner.Slot `
        -SignerName $monitoringSigner.Name `
        -ProfessionalCapacity $monitoringSigner.Capacity `
        -CredentialReference "https://credentials.example.invalid/professionals/$($monitoringSigner.Slot)" `
        -CommitSha $commitSha `
        -GitHubActionsRunUrl $runUrl `
        -CandidateRunCompletedAtUtc $runCompletedAtUtc `
        -CertificatePath $expiringCertificatePath `
        -PrivateKeyPath $expiringKeyPath `
        -SignedAtUtc ([DateTimeOffset]::UtcNow) `
        -OpenSslPath $openssl | Out-Null
    $expiringPolicy = Get-Content -LiteralPath $trustPolicyPath -Raw | ConvertFrom-Json
    $expiringPolicySigner = $expiringPolicy.signers | Where-Object signerSlot -eq $monitoringSigner.Slot
    $expiringPolicySigner.allowedCertificateFingerprintsSha256 = @(Get-AccountsCertificateFingerprint $openssl $expiringCertificatePath)
    $expiringPolicySigner.expectedCertificateSubjectRfc2253 = Get-AccountsCertificateSubject $openssl $expiringCertificatePath
    Write-AccountsUtf8NoBom $trustPolicyPath ($expiringPolicy | ConvertTo-Json -Depth 10)
    Start-Sleep -Seconds 6
    Invoke-ExpectedVerificationFailure "must remain valid at verification/publication time" (Get-AccountsFileSha256 $trustPolicyPath)
    [IO.File]::WriteAllBytes($monitoringSignaturePath, $originalMonitoringSignatureBytes)
    [IO.File]::WriteAllBytes($trustPolicyPath, $originalMonitoringPolicyBytes)

    $badExtensionsPath = Join-Path $fixtureRoot "bad-leaf-extensions.cnf"
    Write-AccountsUtf8NoBom $badExtensionsPath @"
basicConstraints=critical,CA:TRUE
keyUsage=critical,keyCertSign,cRLSign
extendedKeyUsage=serverAuth
subjectKeyIdentifier=hash
authorityKeyIdentifier=keyid,issuer
"@
    $badPolicyKeyPath = Join-Path $keyDirectory "bad-policy-signer.key.pem"
    $badPolicyCsrPath = Join-Path $fixtureRoot "bad-policy-signer.csr.pem"
    $badPolicyCertificatePath = Join-Path $fixtureRoot "bad-policy-signer.cert.pem"
    Invoke-AccountsOpenSsl $openssl @("ecparam", "-name", "prime256v1", "-genkey", "-noout", "-out", $badPolicyKeyPath) "Create bad-policy signer key" | Out-Null
    Invoke-AccountsOpenSsl $openssl @("req", "-new", "-key", $badPolicyKeyPath, "-sha256", "-out", $badPolicyCsrPath, "-subj", "/CN=$($monitoringSigner.Name)/OU=Digital Signature") "Create bad-policy signer CSR" | Out-Null
    Invoke-AccountsOpenSsl $openssl @(
        "x509", "-req", "-in", $badPolicyCsrPath, "-CA", $rootCertificatePath, "-CAkey", $rootKeyPath,
        "-set_serial", "8002", "-out", $badPolicyCertificatePath, "-days", "2", "-sha256", "-extfile", $badExtensionsPath
    ) "Issue signer certificate with forbidden certificate policy" | Out-Null
    Remove-Item -LiteralPath $monitoringSignaturePath
    & (Join-Path $PSScriptRoot "new-release-evidence-signature.ps1") `
        -TemplatePath (Join-Path $evidenceDirectory $monitoringSigner.TemplateFile) `
        -OutputPath $monitoringSignaturePath `
        -SignerSlot $monitoringSigner.Slot `
        -SignerName $monitoringSigner.Name `
        -ProfessionalCapacity $monitoringSigner.Capacity `
        -CredentialReference "https://credentials.example.invalid/professionals/$($monitoringSigner.Slot)" `
        -CommitSha $commitSha `
        -GitHubActionsRunUrl $runUrl `
        -CandidateRunCompletedAtUtc $runCompletedAtUtc `
        -CertificatePath $badPolicyCertificatePath `
        -PrivateKeyPath $badPolicyKeyPath `
        -SignedAtUtc ([DateTimeOffset]::UtcNow) `
        -OpenSslPath $openssl | Out-Null
    $badCertificatePolicy = Get-Content -LiteralPath $trustPolicyPath -Raw | ConvertFrom-Json
    $badCertificatePolicySigner = $badCertificatePolicy.signers | Where-Object signerSlot -eq $monitoringSigner.Slot
    $badCertificatePolicySigner.allowedCertificateFingerprintsSha256 = @(Get-AccountsCertificateFingerprint $openssl $badPolicyCertificatePath)
    $badCertificatePolicySigner.expectedCertificateSubjectRfc2253 = Get-AccountsCertificateSubject $openssl $badPolicyCertificatePath
    Write-AccountsUtf8NoBom $trustPolicyPath ($badCertificatePolicy | ConvertTo-Json -Depth 10)
    Invoke-ExpectedVerificationFailure "keyUsage must explicitly permit Digital Signature" (Get-AccountsFileSha256 $trustPolicyPath)
    $badPolicyReportText = (@((Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json).failures) -join "`n")
    foreach ($requiredBadPolicyFailure in @("basicConstraints must explicitly require CA:FALSE", "extendedKeyUsage must permit E-mail Protection or Code Signing")) {
        if ($badPolicyReportText -notmatch [regex]::Escape($requiredBadPolicyFailure)) {
            throw "Bad certificate policy fixture did not fail '$requiredBadPolicyFailure'."
        }
    }
    [IO.File]::WriteAllBytes($monitoringSignaturePath, $originalMonitoringSignatureBytes)
    [IO.File]::WriteAllBytes($trustPolicyPath, $originalMonitoringPolicyBytes)

    $weakRootKeyPath = Join-Path $keyDirectory "weak-root.key.pem"
    $weakRootCertificatePath = Join-Path $fixtureRoot "weak-root.cert.pem"
    $weakLeafKeyPath = Join-Path $keyDirectory "weak-leaf.key.pem"
    $weakLeafCsrPath = Join-Path $fixtureRoot "weak-leaf.csr.pem"
    $weakLeafCertificatePath = Join-Path $fixtureRoot "weak-leaf.cert.pem"
    Invoke-AccountsOpenSsl $openssl @("genpkey", "-algorithm", "RSA", "-pkeyopt", "rsa_keygen_bits:1024", "-out", $weakRootKeyPath) "Create weak RSA root key" | Out-Null
    Invoke-AccountsOpenSsl $openssl @(
        "req", "-x509", "-new", "-key", $weakRootKeyPath, "-sha256",
        "-out", $weakRootCertificatePath, "-days", "2",
        "-subj", "/CN=Weak Synthetic Release Root",
        "-addext", "basicConstraints=critical,CA:TRUE",
        "-addext", "keyUsage=critical,keyCertSign,cRLSign",
        "-addext", "subjectKeyIdentifier=hash",
        "-addext", "authorityKeyIdentifier=keyid:always"
    ) "Create weak RSA root certificate" | Out-Null
    Invoke-AccountsOpenSsl $openssl @("genpkey", "-algorithm", "RSA", "-pkeyopt", "rsa_keygen_bits:1024", "-out", $weakLeafKeyPath) "Create weak RSA signer key" | Out-Null
    Invoke-AccountsOpenSsl $openssl @("req", "-new", "-key", $weakLeafKeyPath, "-sha256", "-out", $weakLeafCsrPath, "-subj", "/CN=$($monitoringSigner.Name)") "Create weak RSA signer CSR" | Out-Null
    Invoke-AccountsOpenSsl $openssl @(
        "x509", "-req", "-in", $weakLeafCsrPath, "-CA", $weakRootCertificatePath, "-CAkey", $weakRootKeyPath,
        "-set_serial", "8501", "-out", $weakLeafCertificatePath, "-days", "2", "-sha256", "-extfile", $leafExtensionsPath
    ) "Issue weak RSA signer certificate" | Out-Null
    $originalTrustedRootBytes = [IO.File]::ReadAllBytes($rootCertificatePath)
    try {
        [IO.File]::WriteAllBytes($rootCertificatePath, [IO.File]::ReadAllBytes($weakRootCertificatePath))
        Remove-Item -LiteralPath $monitoringSignaturePath
        & (Join-Path $PSScriptRoot "new-release-evidence-signature.ps1") `
            -TemplatePath (Join-Path $evidenceDirectory $monitoringSigner.TemplateFile) `
            -OutputPath $monitoringSignaturePath `
            -SignerSlot $monitoringSigner.Slot `
            -SignerName $monitoringSigner.Name `
            -ProfessionalCapacity $monitoringSigner.Capacity `
            -CredentialReference "https://credentials.example.invalid/professionals/$($monitoringSigner.Slot)" `
            -CommitSha $commitSha `
            -GitHubActionsRunUrl $runUrl `
            -CandidateRunCompletedAtUtc $runCompletedAtUtc `
            -CertificatePath $weakLeafCertificatePath `
            -PrivateKeyPath $weakLeafKeyPath `
            -SignedAtUtc ([DateTimeOffset]::UtcNow) `
            -OpenSslPath $openssl | Out-Null
        $weakPolicy = Get-Content -LiteralPath $trustPolicyPath -Raw | ConvertFrom-Json
        $weakPolicy.trustedRoots[0].sha256Fingerprint = Get-AccountsCertificateFingerprint $openssl $weakRootCertificatePath
        $weakPolicySigner = $weakPolicy.signers | Where-Object signerSlot -eq $monitoringSigner.Slot
        $weakPolicySigner.allowedCertificateFingerprintsSha256 = @(Get-AccountsCertificateFingerprint $openssl $weakLeafCertificatePath)
        $weakPolicySigner.expectedCertificateSubjectRfc2253 = Get-AccountsCertificateSubject $openssl $weakLeafCertificatePath
        Write-AccountsUtf8NoBom $trustPolicyPath ($weakPolicy | ConvertTo-Json -Depth 10)
        Invoke-ExpectedVerificationFailure "public key must be RSA 2048-bit or stronger" (Get-AccountsFileSha256 $trustPolicyPath)
    } finally {
        [IO.File]::WriteAllBytes($monitoringSignaturePath, $originalMonitoringSignatureBytes)
        [IO.File]::WriteAllBytes($rootCertificatePath, $originalTrustedRootBytes)
        [IO.File]::WriteAllBytes($trustPolicyPath, $originalMonitoringPolicyBytes)
    }

    $untrustedRootKeyPath = Join-Path $keyDirectory "untrusted-root.key.pem"
    $untrustedRootCertificatePath = Join-Path $fixtureRoot "untrusted-root.cert.pem"
    Invoke-AccountsOpenSsl $openssl @("ecparam", "-name", "prime256v1", "-genkey", "-noout", "-out", $untrustedRootKeyPath) "Create untrusted synthetic root key" | Out-Null
    Invoke-AccountsOpenSsl $openssl @(
        "req", "-x509", "-new", "-key", $untrustedRootKeyPath, "-sha256",
        "-out", $untrustedRootCertificatePath, "-days", "2",
        "-subj", "/CN=Untrusted Synthetic Root",
        "-addext", "basicConstraints=critical,CA:TRUE",
        "-addext", "keyUsage=critical,keyCertSign,cRLSign"
    ) "Create untrusted synthetic root" | Out-Null

    $untrustedSlot = "monitoring-release-operator"
    $untrustedSigner = $signers | Where-Object Slot -eq $untrustedSlot
    $untrustedKeyPath = Join-Path $keyDirectory "untrusted-signer.key.pem"
    $untrustedCsrPath = Join-Path $fixtureRoot "untrusted-signer.csr.pem"
    $untrustedCertificatePath = Join-Path $fixtureRoot "untrusted-signer.cert.pem"
    Invoke-AccountsOpenSsl $openssl @("ecparam", "-name", "prime256v1", "-genkey", "-noout", "-out", $untrustedKeyPath) "Create untrusted signer key" | Out-Null
    Invoke-AccountsOpenSsl $openssl @(
        "req", "-new", "-key", $untrustedKeyPath, "-sha256",
        "-out", $untrustedCsrPath, "-subj", "/CN=$($untrustedSigner.Name)"
    ) "Create untrusted signer CSR" | Out-Null
    Invoke-AccountsOpenSsl $openssl @(
        "x509", "-req", "-in", $untrustedCsrPath, "-CA", $untrustedRootCertificatePath, "-CAkey", $untrustedRootKeyPath,
        "-set_serial", "9001", "-out", $untrustedCertificatePath, "-days", "2", "-sha256", "-extfile", $leafExtensionsPath
    ) "Issue untrusted signer certificate" | Out-Null

    $untrustedSignaturePath = Join-Path $evidenceDirectory "$($untrustedSigner.TemplateFile).$untrustedSlot.signature.json"
    $originalUntrustedTargetBytes = [IO.File]::ReadAllBytes($untrustedSignaturePath)
    Remove-Item -LiteralPath $untrustedSignaturePath
    & (Join-Path $PSScriptRoot "new-release-evidence-signature.ps1") `
        -TemplatePath (Join-Path $evidenceDirectory $untrustedSigner.TemplateFile) `
        -OutputPath $untrustedSignaturePath `
        -SignerSlot $untrustedSlot `
        -SignerName $untrustedSigner.Name `
        -ProfessionalCapacity $untrustedSigner.Capacity `
        -CredentialReference "https://credentials.example.invalid/professionals/$untrustedSlot" `
        -CommitSha $commitSha `
        -GitHubActionsRunUrl $runUrl `
        -CandidateRunCompletedAtUtc $runCompletedAtUtc `
        -CertificatePath $untrustedCertificatePath `
        -PrivateKeyPath $untrustedKeyPath `
        -SignedAtUtc ([DateTimeOffset]::UtcNow) `
        -OpenSslPath $openssl | Out-Null

    $originalPolicyBytes = [IO.File]::ReadAllBytes($trustPolicyPath)
    $untrustedPolicy = Get-Content -LiteralPath $trustPolicyPath -Raw | ConvertFrom-Json
    $untrustedPolicySigner = $untrustedPolicy.signers | Where-Object signerSlot -eq $untrustedSlot
    $untrustedPolicySigner.allowedCertificateFingerprintsSha256 = @(Get-AccountsCertificateFingerprint $openssl $untrustedCertificatePath)
    $untrustedPolicySigner.expectedCertificateSubjectRfc2253 = Get-AccountsCertificateSubject $openssl $untrustedCertificatePath
    Write-AccountsUtf8NoBom $trustPolicyPath ($untrustedPolicy | ConvertTo-Json -Depth 10)
    $untrustedPolicySha256 = Get-AccountsFileSha256 $trustPolicyPath
    $ambientCaDirectory = Join-Path $fixtureRoot "ambient-openssl-ca-directory"
    New-Item -ItemType Directory -Path $ambientCaDirectory -Force | Out-Null
    $ambientRootHash = (Invoke-AccountsOpenSsl $openssl @("x509", "-hash", "-noout", "-in", $untrustedRootCertificatePath) "Hash ambient synthetic CA").Trim()
    Copy-Item -LiteralPath $untrustedRootCertificatePath -Destination (Join-Path $ambientCaDirectory "$ambientRootHash.0")
    $originalSslCertDir = $env:SSL_CERT_DIR
    try {
        $env:SSL_CERT_DIR = $ambientCaDirectory
        Invoke-AccountsOpenSsl $openssl @("verify", "-purpose", "any", "-CAfile", $rootCertificatePath, $untrustedCertificatePath) "Prove ambient CA directory would bypass CAfile-only verification" | Out-Null
        Invoke-ExpectedVerificationFailure "certificate chain is not trusted" $untrustedPolicySha256
    } finally {
        $env:SSL_CERT_DIR = $originalSslCertDir
    }
    [IO.File]::WriteAllBytes($untrustedSignaturePath, $originalUntrustedTargetBytes)
    [IO.File]::WriteAllBytes($trustPolicyPath, $originalPolicyBytes)

    Write-Host "Durable release-evidence synthetic tests passed: seven valid signatures plus identity, time, certificate-policy, key-strength, containment, independence, unexpected-sidecar, private-material, tamper, ambient-CA-store, and untrusted-chain failures."
} finally {
    if ($KeepFixture) {
        Write-Host "Synthetic fixture retained at $fixtureRoot"
    } else {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
