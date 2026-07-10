param(
    [Parameter(Mandatory = $true)][string]$EvidenceDirectory,
    [Parameter(Mandatory = $true)][string]$TrustPolicyPath,
    [Parameter(Mandatory = $true)][string]$TrustPolicySha256,
    [Parameter(Mandatory = $true)][string]$CommitSha,
    [Parameter(Mandatory = $true)][string]$GitHubActionsRunUrl,
    [Parameter(Mandatory = $true)][DateTimeOffset]$CandidateRunCompletedAtUtc,
    [string]$ReportPath = "",
    [string]$OpenSslPath = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

Import-Module (Join-Path $PSScriptRoot "ReleaseEvidenceCrypto.psm1") -Force

$requiredSigners = @(
    [pscustomobject]@{ TemplateFile = "visual-qa-signoff-template.md"; SignerSlot = "visual-qa-reviewer" },
    [pscustomobject]@{ TemplateFile = "source-law-review-template.md"; SignerSlot = "source-law-reviewer" },
    [pscustomobject]@{ TemplateFile = "source-law-review-template.md"; SignerSlot = "source-law-qualified-accountant" },
    [pscustomobject]@{ TemplateFile = "external-ros-ixbrl-validation-template.md"; SignerSlot = "external-ros-ixbrl-reviewer" },
    [pscustomobject]@{ TemplateFile = "qualified-accountant-acceptance-template.md"; SignerSlot = "qualified-accountant" },
    [pscustomobject]@{ TemplateFile = "manual-handoff-acceptance-template.md"; SignerSlot = "manual-handoff-reviewer" },
    [pscustomobject]@{ TemplateFile = "monitoring-provider-confirmation-template.md"; SignerSlot = "monitoring-release-operator" }
)

$failures = [System.Collections.Generic.List[string]]::new()
$signatureResults = [System.Collections.Generic.List[object]]::new()

function Add-Failure {
    param([string]$Message)
    $failures.Add($Message) | Out-Null
}

function Get-JsonValue {
    param($Object, [string[]]$Path)

    $current = $Object
    foreach ($segment in $Path) {
        if ($null -eq $current) { return $null }
        $property = $current.PSObject.Properties[$segment]
        if ($null -eq $property) { return $null }
        $current = $property.Value
    }
    return $current
}

function Read-JsonSafely {
    param([string]$Path, [string]$Context)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        Add-Failure "Missing $Context file: $Path"
        return $null
    }

    try {
        return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    } catch {
        Add-Failure "$Context must be valid JSON: $($_.Exception.Message)"
        return $null
    }
}

function Test-ExactValue {
    param($Actual, $Expected, [string]$Context)

    if (-not [string]::Equals([string]$Actual, [string]$Expected, [StringComparison]::Ordinal)) {
        Add-Failure "$Context must be '$Expected'."
        return $false
    }
    return $true
}

function Test-RealValue {
    param($Value, [string]$Context)

    $text = [string]$Value
    if ([string]::IsNullOrWhiteSpace($text) -or $text.Trim().Length -lt 3 -or
        $text.Trim() -match "(?i)^(accepted|none|n/a|pending|todo|tbd|test|unknown)$") {
        Add-Failure "$Context must be a real, non-placeholder value."
        return $false
    }
    return $true
}

function Test-HttpsReference {
    param($Value, [string]$Context)

    $uri = $null
    if (-not [Uri]::TryCreate([string]$Value, [UriKind]::Absolute, [ref]$uri) -or
        -not [string]::Equals($uri.Scheme, "https", [StringComparison]::OrdinalIgnoreCase) -or
        [string]::IsNullOrWhiteSpace($uri.Host) -or
        -not [string]::IsNullOrWhiteSpace($uri.UserInfo) -or
        -not [string]::IsNullOrWhiteSpace($uri.Query) -or
        -not [string]::IsNullOrWhiteSpace($uri.Fragment)) {
        Add-Failure "$Context must be a public absolute HTTPS reference without credentials, query parameters, or fragments."
        return $false
    }
    return $true
}

function Get-Rfc2253CommonName {
    param([string]$Subject)

    $match = [regex]::Match($Subject, "(?:^|,)CN=((?:\\.|[^,])*)")
    if (-not $match.Success) {
        return ""
    }

    $commonName = $match.Groups[1].Value
    foreach ($escapedCharacter in @(",", "+", '"', "<", ">", ";", "=", "#", " ")) {
        $commonName = $commonName.Replace("\$escapedCharacter", $escapedCharacter)
    }
    return $commonName.Replace("\\", "\")
}

function Resolve-ContainedFile {
    param([string]$BaseDirectory, [string]$RelativePath, [string]$Context)

    if ([string]::IsNullOrWhiteSpace($RelativePath) -or [IO.Path]::IsPathRooted($RelativePath)) {
        Add-Failure "$Context must be a relative file path."
        return $null
    }

    $candidate = Join-Path $BaseDirectory $RelativePath
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        Add-Failure "$Context file is missing: $RelativePath"
        return $null
    }

    $resolvedBase = [IO.Path]::GetFullPath($BaseDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $resolvedCandidate = (Resolve-Path -LiteralPath $candidate).Path
    $pathComparison = if ([IO.Path]::DirectorySeparatorChar -eq '\') {
        [StringComparison]::OrdinalIgnoreCase
    } else {
        [StringComparison]::Ordinal
    }
    if (-not $resolvedCandidate.StartsWith($resolvedBase, $pathComparison)) {
        Add-Failure "$Context must remain inside its trust-policy directory."
        return $null
    }

    return $resolvedCandidate
}

if ($CommitSha -notmatch "^[0-9a-fA-F]{40}$") {
    throw "CommitSha must be a full 40-character hexadecimal commit SHA."
}
$CommitSha = $CommitSha.ToLowerInvariant()
if ($GitHubActionsRunUrl -notmatch "^https://github\.com/[^/\s]+/[^/\s]+/actions/runs/[0-9]+/?$") {
    throw "GitHubActionsRunUrl must be an exact GitHub Actions run URL."
}
$GitHubActionsRunUrl = $GitHubActionsRunUrl.TrimEnd("/")
if ($CandidateRunCompletedAtUtc.Offset -ne [TimeSpan]::Zero) {
    throw "CandidateRunCompletedAtUtc must be a UTC timestamp."
}
$candidateRunCompletedAt = $CandidateRunCompletedAtUtc.ToUniversalTime()
if ($TrustPolicySha256 -notmatch "^[0-9a-fA-F]{64}$") {
    throw "TrustPolicySha256 must be a 64-character hexadecimal SHA-256 digest supplied out of band."
}
$TrustPolicySha256 = $TrustPolicySha256.ToLowerInvariant()

$resolvedEvidenceDirectory = (Resolve-Path -LiteralPath $EvidenceDirectory).Path
$resolvedTrustPolicyPath = (Resolve-Path -LiteralPath $TrustPolicyPath).Path
$trustPolicyDirectory = Split-Path -Parent $resolvedTrustPolicyPath
if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path $resolvedEvidenceDirectory "durable-release-evidence-report.json"
}
$resolvedReportPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ReportPath)
$reportDirectory = Split-Path -Parent $resolvedReportPath
if (-not (Test-Path -LiteralPath $reportDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}

$openssl = Resolve-AccountsOpenSsl $OpenSslPath
$actualTrustPolicySha256 = Get-AccountsFileSha256 $resolvedTrustPolicyPath
if ($actualTrustPolicySha256 -ne $TrustPolicySha256) {
    Add-Failure "Trust policy SHA-256 does not match the independently supplied digest."
}

$trustPolicy = Read-JsonSafely $resolvedTrustPolicyPath "release-evidence trust policy"
$policyRoots = @()
$policySigners = @()
if ($null -ne $trustPolicy) {
    Test-ExactValue (Get-JsonValue $trustPolicy @("schemaVersion")) "accounts.release-evidence.trust-policy/v1" "Trust policy schemaVersion" | Out-Null
    Test-ExactValue (Get-JsonValue $trustPolicy @("releaseCandidate", "commitSha")) $CommitSha "Trust policy releaseCandidate.commitSha" | Out-Null
    Test-ExactValue (([string](Get-JsonValue $trustPolicy @("releaseCandidate", "githubActionsRunUrl"))).TrimEnd("/")) $GitHubActionsRunUrl "Trust policy releaseCandidate.githubActionsRunUrl" | Out-Null
    Test-ExactValue (Get-JsonValue $trustPolicy @("releaseCandidate", "githubActionsCompletedAtUtc")) $candidateRunCompletedAt.ToString("O") "Trust policy releaseCandidate.githubActionsCompletedAtUtc" | Out-Null
    $policyRoots = @(Get-JsonValue $trustPolicy @("trustedRoots"))
    $policySigners = @(Get-JsonValue $trustPolicy @("signers"))
}

if ($policyRoots.Count -eq 0) {
    Add-Failure "Trust policy must contain at least one independently supplied trusted root."
}
if ($policySigners.Count -ne $requiredSigners.Count) {
    Add-Failure "Trust policy must contain exactly $($requiredSigners.Count) signer entries."
}

$rootById = @{}
foreach ($root in $policyRoots) {
    $rootId = [string](Get-JsonValue $root @("rootId"))
    if (-not (Test-RealValue $rootId "Trust policy trustedRoots.rootId")) { continue }
    if ($rootById.ContainsKey($rootId)) {
        Add-Failure "Trust policy contains duplicate trusted root '$rootId'."
        continue
    }

    $rootFile = Resolve-ContainedFile $trustPolicyDirectory ([string](Get-JsonValue $root @("certificateFile"))) "Trusted root '$rootId' certificateFile"
    $expectedFingerprint = ([string](Get-JsonValue $root @("sha256Fingerprint"))).ToLowerInvariant()
    if ($expectedFingerprint -notmatch "^[0-9a-f]{64}$") {
        Add-Failure "Trusted root '$rootId' sha256Fingerprint must be 64 lowercase hexadecimal characters."
    }

    if ($null -ne $rootFile) {
        try {
            $rootPemText = [IO.File]::ReadAllText($rootFile)
            $beginCertificateCount = [regex]::Matches($rootPemText, "-----BEGIN CERTIFICATE-----").Count
            $endCertificateCount = [regex]::Matches($rootPemText, "-----END CERTIFICATE-----").Count
            if ($beginCertificateCount -ne 1 -or $endCertificateCount -ne 1 -or
                $rootPemText -cnotmatch '\A\s*-----BEGIN CERTIFICATE-----[A-Za-z0-9+/=\r\n]+-----END CERTIFICATE-----\s*\z') {
                Add-Failure "Trusted root '$rootId' certificateFile must contain exactly one PEM certificate and no additional trust material."
                $rootFile = $null
            }
        } catch {
            Add-Failure "Trusted root '$rootId' certificate file could not be read: $($_.Exception.Message)"
            $rootFile = $null
        }
    }

    if ($null -ne $rootFile) {
        try {
            $actualFingerprint = Get-AccountsCertificateFingerprint $openssl $rootFile
            if ($actualFingerprint -ne $expectedFingerprint) {
                Add-Failure "Trusted root '$rootId' certificate fingerprint does not match the trust policy."
            }
        } catch {
            Add-Failure "Trusted root '$rootId' could not be inspected: $($_.Exception.Message)"
        }
    }

    $rootById[$rootId] = [pscustomobject]@{
        RootId = $rootId
        CertificatePath = $rootFile
        Fingerprint = $expectedFingerprint
    }
}

$policySignerByKey = @{}
foreach ($policySigner in $policySigners) {
    $templateFile = [string](Get-JsonValue $policySigner @("templateFile"))
    $signerSlot = [string](Get-JsonValue $policySigner @("signerSlot"))
    $key = "$templateFile|$signerSlot"
    if ($policySignerByKey.ContainsKey($key)) {
        Add-Failure "Trust policy contains duplicate signer entry '$key'."
    } else {
        $policySignerByKey[$key] = $policySigner
    }
}

$expectedSignatureFiles = @($requiredSigners | ForEach-Object { "$($_.TemplateFile).$($_.SignerSlot).signature.json" })
$evidencePathPrefix = $resolvedEvidenceDirectory.TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar
) + [IO.Path]::DirectorySeparatorChar
$actualSignatureFiles = @(Get-ChildItem -LiteralPath $resolvedEvidenceDirectory -Recurse -File -Filter "*.signature.json" | ForEach-Object {
    $_.FullName.Substring($evidencePathPrefix.Length).Replace('\', '/')
})
foreach ($unexpectedFile in @($actualSignatureFiles | Where-Object { $_ -cnotin $expectedSignatureFiles })) {
    Add-Failure "Unexpected detached signature sidecar is present: $unexpectedFile"
}

foreach ($requiredSigner in $requiredSigners) {
    $templateFile = $requiredSigner.TemplateFile
    $signerSlot = $requiredSigner.SignerSlot
    $key = "$templateFile|$signerSlot"
    $signatureFile = "$templateFile.$signerSlot.signature.json"
    $signaturePath = Join-Path $resolvedEvidenceDirectory $signatureFile
    $templatePath = Join-Path $resolvedEvidenceDirectory $templateFile
    $resultFailures = [System.Collections.Generic.List[string]]::new()
    $result = [ordered]@{
        templateFile = $templateFile
        signerSlot = $signerSlot
        signatureFile = $signatureFile
        signerName = ""
        professionalCapacity = ""
        credentialReference = ""
        credentialVerifiedAtUtc = ""
        credentialVerifiedBy = ""
        signedAtUtc = ""
        templateSha256 = ""
        certificateSha256Fingerprint = ""
        certificateSubjectRfc2253 = ""
        certificateCommonName = ""
        certificateSerialNumber = ""
        certificatePublicKeyAlgorithm = ""
        certificatePublicKeyBits = 0
        trustRootId = ""
        signatureValid = $false
        certificateKeyUsageValid = $false
        certificateBasicConstraintsValid = $false
        certificateExtendedKeyUsageValid = $false
        certificateSecurityLevelValid = $false
        certificateChainValidAtSigningTime = $false
        certificateChainValidAtVerificationTime = $false
        credentialBindingValid = $false
        passed = $false
        failures = @()
    }

    function Add-ResultFailure {
        param([string]$Message)
        $resultFailures.Add($Message) | Out-Null
        Add-Failure "${signatureFile}: $Message"
    }

    $policySigner = $null
    if ($policySignerByKey.ContainsKey($key)) {
        $policySigner = $policySignerByKey[$key]
    } else {
        Add-ResultFailure "Trust policy is missing signer entry '$key'."
    }

    if (-not (Test-Path -LiteralPath $templatePath -PathType Leaf)) {
        Add-ResultFailure "Signed template is missing."
    }
    $envelope = Read-JsonSafely $signaturePath "detached signature sidecar '$signatureFile'"
    if ($null -eq $envelope) {
        Add-ResultFailure "Detached signature envelope could not be read."
    }

    $credentialVerifiedAt = [DateTimeOffset]::MinValue
    if ($null -ne $policySigner) {
        $expectedName = [string](Get-JsonValue $policySigner @("expectedName"))
        $expectedCapacity = [string](Get-JsonValue $policySigner @("expectedProfessionalCapacity"))
        $expectedCredential = [string](Get-JsonValue $policySigner @("expectedCredentialReference"))
        $expectedCertificateSubject = [string](Get-JsonValue $policySigner @("expectedCertificateSubjectRfc2253"))
        $trustRootId = [string](Get-JsonValue $policySigner @("trustedRootId"))
        $result.trustRootId = $trustRootId
        Test-RealValue $expectedName "$key expectedName" | Out-Null
        Test-RealValue $expectedCapacity "$key expectedProfessionalCapacity" | Out-Null
        Test-HttpsReference $expectedCredential "$key expectedCredentialReference" | Out-Null
        Test-RealValue $expectedCertificateSubject "$key expectedCertificateSubjectRfc2253" | Out-Null
        Test-ExactValue (Get-JsonValue $policySigner @("credentialVerification", "status")) "verified" "$key credentialVerification.status" | Out-Null
        $result.credentialVerifiedBy = [string](Get-JsonValue $policySigner @("credentialVerification", "verifiedBy"))
        Test-RealValue $result.credentialVerifiedBy "$key credentialVerification.verifiedBy" | Out-Null
        if ([string]::Equals($result.credentialVerifiedBy.Trim(), $expectedName.Trim(), [StringComparison]::OrdinalIgnoreCase)) {
            Add-ResultFailure "credentialVerification.verifiedBy must identify an independent person, not the signer."
        }
        Test-HttpsReference (Get-JsonValue $policySigner @("credentialVerification", "evidenceReference")) "$key credentialVerification.evidenceReference" | Out-Null
        if (-not [DateTimeOffset]::TryParse([string](Get-JsonValue $policySigner @("credentialVerification", "verifiedAtUtc")), [ref]$credentialVerifiedAt) -or $credentialVerifiedAt.Offset -ne [TimeSpan]::Zero) {
            Add-ResultFailure "credentialVerification.verifiedAtUtc must be a UTC timestamp."
        } else {
            $result.credentialVerifiedAtUtc = $credentialVerifiedAt.ToUniversalTime().ToString("O")
        }
        if (-not $rootById.ContainsKey($trustRootId)) {
            Add-ResultFailure "trustedRootId '$trustRootId' is not defined."
        }
    }

    if ($null -ne $envelope) {
        if ([string](Get-JsonValue $envelope @("schemaVersion")) -ne "accounts.release-evidence.detached-signature/v1") { Add-ResultFailure "Envelope schemaVersion is invalid." }
        if ([string](Get-JsonValue $envelope @("signatureAlgorithm")) -ne "openssl-evp-sha256") { Add-ResultFailure "Envelope signatureAlgorithm is invalid." }
        if ([string](Get-JsonValue $envelope @("statementEncoding")) -ne "base64" -or [string](Get-JsonValue $envelope @("signatureEncoding")) -ne "base64") { Add-ResultFailure "Envelope encodings must be base64." }
        if ((Get-Content -LiteralPath $signaturePath -Raw) -match "PRIVATE KEY") { Add-ResultFailure "Envelope contains forbidden private-key material." }
        $decodedCertificateEntries = [System.Collections.Generic.List[object]]::new()
        $decodedCertificateEntries.Add([pscustomobject]@{
            Label = "signerCertificatePem"
            Value = [string](Get-JsonValue $envelope @("signerCertificatePem"))
        }) | Out-Null
        $chainIndex = 0
        foreach ($chainCertificatePem in @((Get-JsonValue $envelope @("certificateChainPem")))) {
            $decodedCertificateEntries.Add([pscustomobject]@{
                Label = "certificateChainPem[$chainIndex]"
                Value = [string]$chainCertificatePem
            }) | Out-Null
            $chainIndex++
        }
        foreach ($certificateEntry in $decodedCertificateEntries) {
            if ($certificateEntry.Value -match "(?i)PRIVATE\s+KEY" -or
                $certificateEntry.Value -cnotmatch '\A\s*-----BEGIN CERTIFICATE-----[A-Za-z0-9+/=\r\n]+-----END CERTIFICATE-----\s*\z') {
                Add-ResultFailure "Decoded $($certificateEntry.Label) must contain exactly one public PEM certificate and no private-key material."
            }
        }
    }

    if ($null -ne $policySigner -and $null -ne $envelope -and (Test-Path -LiteralPath $templatePath -PathType Leaf)) {
        $temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) ("accounts-release-verify-" + [Guid]::NewGuid().ToString("N"))
        New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null
        try {
            $statementPath = Join-Path $temporaryDirectory "statement.json"
            $detachedSignaturePath = Join-Path $temporaryDirectory "statement.signature"
            $signerCertificatePath = Join-Path $temporaryDirectory "signer-certificate.pem"
            $publicKeyPath = Join-Path $temporaryDirectory "signer-public-key.pem"
            $intermediateChainPath = Join-Path $temporaryDirectory "intermediate-chain.pem"
            try {
                [IO.File]::WriteAllBytes($statementPath, [Convert]::FromBase64String([string](Get-JsonValue $envelope @("statementBase64"))))
                [IO.File]::WriteAllBytes($detachedSignaturePath, [Convert]::FromBase64String([string](Get-JsonValue $envelope @("signatureBase64"))))
                Write-AccountsUtf8NoBom $signerCertificatePath ([string](Get-JsonValue $envelope @("signerCertificatePem")))
            } catch {
                Add-ResultFailure "Envelope base64 or certificate content is invalid: $($_.Exception.Message)"
            }

            $statement = $null
            if (Test-Path -LiteralPath $statementPath -PathType Leaf) {
                try {
                    $statement = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($statementPath)) | ConvertFrom-Json
                } catch {
                    Add-ResultFailure "Signed statement is not valid UTF-8 JSON."
                }
            }

            $signedAt = [DateTimeOffset]::MinValue
            if ($null -ne $statement) {
                if ([string](Get-JsonValue $statement @("schemaVersion")) -ne "accounts.release-evidence.signature-statement/v1") { Add-ResultFailure "Statement schemaVersion is invalid." }
                if ([string](Get-JsonValue $statement @("releaseCandidate", "commitSha")) -ne $CommitSha) { Add-ResultFailure "Statement commit SHA does not match the release candidate." }
                if (([string](Get-JsonValue $statement @("releaseCandidate", "githubActionsRunUrl"))).TrimEnd("/") -ne $GitHubActionsRunUrl) { Add-ResultFailure "Statement Actions run URL does not match the release candidate." }
                if ([string](Get-JsonValue $statement @("releaseCandidate", "githubActionsCompletedAtUtc")) -ne $candidateRunCompletedAt.ToString("O")) { Add-ResultFailure "Statement Actions completion time does not match the release candidate." }
                if ([string](Get-JsonValue $statement @("template", "fileName")) -ne $templateFile) { Add-ResultFailure "Statement template file name is not bound to the required template." }
                if ([string](Get-JsonValue $statement @("signer", "slot")) -ne $signerSlot) { Add-ResultFailure "Statement signer slot is invalid." }

                $actualTemplate = Get-Item -LiteralPath $templatePath
                $actualTemplateSha256 = Get-AccountsFileSha256 $templatePath
                $result.templateSha256 = $actualTemplateSha256
                if ([long](Get-JsonValue $statement @("template", "byteSize")) -ne $actualTemplate.Length) { Add-ResultFailure "Template byte size has changed since signing." }
                if ([string](Get-JsonValue $statement @("template", "sha256")) -ne $actualTemplateSha256) { Add-ResultFailure "Template SHA-256 has changed since signing." }

                $result.signerName = [string](Get-JsonValue $statement @("signer", "name"))
                $result.professionalCapacity = [string](Get-JsonValue $statement @("signer", "professionalCapacity"))
                $result.credentialReference = [string](Get-JsonValue $statement @("signer", "credentialReference"))
                if ($result.signerName -ne [string](Get-JsonValue $policySigner @("expectedName"))) { Add-ResultFailure "Signed signer name does not match the independent trust policy." }
                if ($result.professionalCapacity -ne [string](Get-JsonValue $policySigner @("expectedProfessionalCapacity"))) { Add-ResultFailure "Signed professional capacity does not match the independent trust policy." }
                if ($result.credentialReference -ne [string](Get-JsonValue $policySigner @("expectedCredentialReference"))) { Add-ResultFailure "Signed credential reference does not match the independent trust policy." }
                if (Test-HttpsReference $result.credentialReference "$key signed credentialReference") {
                    $result.credentialBindingValid = $true
                }

                $result.signedAtUtc = [string](Get-JsonValue $statement @("signedAtUtc"))
                if (-not [DateTimeOffset]::TryParse($result.signedAtUtc, [ref]$signedAt) -or $signedAt.Offset -ne [TimeSpan]::Zero) {
                    Add-ResultFailure "signedAtUtc must be a UTC timestamp."
                } elseif ($signedAt -gt [DateTimeOffset]::UtcNow.AddMinutes(5)) {
                    Add-ResultFailure "signedAtUtc cannot be in the future."
                } elseif ($signedAt -lt $candidateRunCompletedAt) {
                    Add-ResultFailure "signedAtUtc cannot predate the successful candidate CI completion time."
                } elseif ($credentialVerifiedAt -ne [DateTimeOffset]::MinValue -and $credentialVerifiedAt -gt $signedAt) {
                    Add-ResultFailure "Credential verification cannot occur after the signature it authorizes."
                } elseif ($credentialVerifiedAt -ne [DateTimeOffset]::MinValue -and ($signedAt - $credentialVerifiedAt).TotalDays -gt 30) {
                    Add-ResultFailure "Credential verification must be refreshed within 30 days before signing."
                }
            }

            if (Test-Path -LiteralPath $signerCertificatePath -PathType Leaf) {
                try {
                    $actualCertificateFingerprint = Get-AccountsCertificateFingerprint $openssl $signerCertificatePath
                    $actualCertificateSubject = Get-AccountsCertificateSubject $openssl $signerCertificatePath
                    $actualCertificateSerial = Get-AccountsCertificateSerialNumber $openssl $signerCertificatePath
                    $result.certificateSha256Fingerprint = $actualCertificateFingerprint
                    $result.certificateSubjectRfc2253 = $actualCertificateSubject
                    $result.certificateCommonName = Get-Rfc2253CommonName $actualCertificateSubject
                    $result.certificateSerialNumber = $actualCertificateSerial

                    $allowedFingerprints = @((Get-JsonValue $policySigner @("allowedCertificateFingerprintsSha256")) | ForEach-Object { ([string]$_).ToLowerInvariant() })
                    if ($allowedFingerprints.Count -eq 0 -or $actualCertificateFingerprint -notin $allowedFingerprints) { Add-ResultFailure "Signer certificate fingerprint is not trusted for this signer slot." }
                    if ($actualCertificateSubject -ne [string](Get-JsonValue $policySigner @("expectedCertificateSubjectRfc2253"))) { Add-ResultFailure "Signer certificate subject does not match the independent trust policy." }
                    if ([string]::IsNullOrWhiteSpace($result.certificateCommonName) -or $result.certificateCommonName -ne [string](Get-JsonValue $policySigner @("expectedName"))) { Add-ResultFailure "Signer certificate common name must exactly match the independently verified signer name." }
                    if ($null -ne $statement) {
                        if ([string](Get-JsonValue $statement @("certificate", "sha256Fingerprint")) -ne $actualCertificateFingerprint) { Add-ResultFailure "Statement certificate fingerprint does not match the signing certificate." }
                        if ([string](Get-JsonValue $statement @("certificate", "subjectRfc2253")) -ne $actualCertificateSubject) { Add-ResultFailure "Statement certificate subject does not match the signing certificate." }
                        if ([string](Get-JsonValue $statement @("certificate", "serialNumber")) -ne $actualCertificateSerial) { Add-ResultFailure "Statement certificate serial number does not match the signing certificate." }
                    }

                    $keyUsageText = (Invoke-AccountsOpenSsl $openssl @("x509", "-in", $signerCertificatePath, "-noout", "-ext", "keyUsage") "Inspect signer certificate key usage extension") -join "`n"
                    if ($keyUsageText -match "(?im)^\s*Digital Signature(?:\s*,|\s*$)" -and
                        $keyUsageText -notmatch "(?i)Certificate Sign|CRL Sign") {
                        $result.certificateKeyUsageValid = $true
                    } else {
                        Add-ResultFailure "Signer certificate keyUsage must explicitly permit Digital Signature and must not permit certificate or CRL signing."
                    }
                    $basicConstraintsText = (Invoke-AccountsOpenSsl $openssl @("x509", "-in", $signerCertificatePath, "-noout", "-ext", "basicConstraints") "Inspect signer certificate basic constraints extension") -join "`n"
                    if ($basicConstraintsText -match "(?i)CA\s*:\s*FALSE") {
                        $result.certificateBasicConstraintsValid = $true
                    } else {
                        Add-ResultFailure "Signer certificate basicConstraints must explicitly require CA:FALSE."
                    }
                    $extendedKeyUsageText = (Invoke-AccountsOpenSsl $openssl @("x509", "-in", $signerCertificatePath, "-noout", "-ext", "extendedKeyUsage") "Inspect signer certificate extended key usage extension") -join "`n"
                    if ($extendedKeyUsageText -match "(?i)E-mail Protection|Code Signing") {
                        $result.certificateExtendedKeyUsageValid = $true
                    } else {
                        Add-ResultFailure "Signer certificate extendedKeyUsage must permit E-mail Protection or Code Signing."
                    }

                    Invoke-AccountsOpenSsl $openssl @("x509", "-in", $signerCertificatePath, "-pubkey", "-noout", "-out", $publicKeyPath) "Extract signer public key" | Out-Null
                    $certificateText = (Invoke-AccountsOpenSsl $openssl @("x509", "-in", $signerCertificatePath, "-noout", "-text") "Inspect signer public-key algorithm") -join "`n"
                    $publicKeyText = (Invoke-AccountsOpenSsl $openssl @("pkey", "-pubin", "-in", $publicKeyPath, "-noout", "-text") "Inspect signer public-key strength") -join "`n"
                    $algorithmMatch = [regex]::Match($certificateText, '(?im)^\s*Public Key Algorithm:\s*(?<algorithm>[^\r\n]+)')
                    $bitsMatch = [regex]::Match($publicKeyText, '(?im)^\s*(?:RSA )?Public-Key:\s*\((?<bits>[0-9]+) bit\)')
                    if ($algorithmMatch.Success) {
                        $result.certificatePublicKeyAlgorithm = $algorithmMatch.Groups['algorithm'].Value.Trim()
                    }
                    if ($bitsMatch.Success) {
                        $result.certificatePublicKeyBits = [int]$bitsMatch.Groups['bits'].Value
                    }
                    $isStrongRsa = $result.certificatePublicKeyAlgorithm -match '(?i)rsa' -and $result.certificatePublicKeyBits -ge 2048
                    $isStrongEc = $result.certificatePublicKeyAlgorithm -match '(?i)(?:id-ecPublicKey|EC)' -and $result.certificatePublicKeyBits -ge 224
                    if ($isStrongRsa -or $isStrongEc) {
                        $result.certificateSecurityLevelValid = $true
                    } else {
                        Add-ResultFailure "Signer certificate public key must be RSA 2048-bit or stronger, or EC 224-bit or stronger."
                    }
                    try {
                        Invoke-AccountsOpenSsl $openssl @("dgst", "-sha256", "-verify", $publicKeyPath, "-signature", $detachedSignaturePath, $statementPath) "Verify detached release-evidence signature" | Out-Null
                        $result.signatureValid = $true
                    } catch {
                        Add-ResultFailure "Detached signature verification failed."
                    }

                    $rootId = [string](Get-JsonValue $policySigner @("trustedRootId"))
                    if ($rootById.ContainsKey($rootId) -and $null -ne $rootById[$rootId].CertificatePath) {
                        $chainPemValues = @((Get-JsonValue $envelope @("certificateChainPem")) | ForEach-Object { [string]$_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
                        if ($chainPemValues.Count -gt 0) {
                            Write-AccountsUtf8NoBom $intermediateChainPath (($chainPemValues -join "`n").Trim() + "`n")
                        }

                        if ($signedAt -ne [DateTimeOffset]::MinValue) {
                            $signingTimeVerifyArguments = [System.Collections.Generic.List[string]]::new()
                            @("verify", "-purpose", "any", "-auth_level", "2", "-x509_strict", "-no-CApath", "-no-CAstore", "-attime", $signedAt.ToUnixTimeSeconds().ToString([Globalization.CultureInfo]::InvariantCulture), "-CAfile", $rootById[$rootId].CertificatePath) | ForEach-Object { $signingTimeVerifyArguments.Add($_) }
                            if ($chainPemValues.Count -gt 0) {
                                $signingTimeVerifyArguments.Add("-untrusted")
                                $signingTimeVerifyArguments.Add($intermediateChainPath)
                            }
                            $signingTimeVerifyArguments.Add($signerCertificatePath)
                            try {
                                Invoke-AccountsOpenSsl $openssl $signingTimeVerifyArguments.ToArray() "Verify signer certificate chain at signing time" | Out-Null
                                $result.certificateChainValidAtSigningTime = $true
                            } catch {
                                Add-ResultFailure "Signer certificate chain is not trusted at the signing time."
                            }
                        }

                        $currentVerifyArguments = [System.Collections.Generic.List[string]]::new()
                        @("verify", "-purpose", "any", "-auth_level", "2", "-x509_strict", "-no-CApath", "-no-CAstore", "-CAfile", $rootById[$rootId].CertificatePath) | ForEach-Object { $currentVerifyArguments.Add($_) }
                        if ($chainPemValues.Count -gt 0) {
                            $currentVerifyArguments.Add("-untrusted")
                            $currentVerifyArguments.Add($intermediateChainPath)
                        }
                        $currentVerifyArguments.Add($signerCertificatePath)
                        try {
                            Invoke-AccountsOpenSsl $openssl $currentVerifyArguments.ToArray() "Verify signer certificate chain at verification time" | Out-Null
                            $result.certificateChainValidAtVerificationTime = $true
                        } catch {
                            Add-ResultFailure "Signer certificate chain must remain valid at verification/publication time when no trusted timestamp is retained."
                        }
                    }
                } catch {
                    Add-ResultFailure "Signer certificate could not be inspected: $($_.Exception.Message)"
                }
            }
        } finally {
            Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    $result.passed = $resultFailures.Count -eq 0 -and $result.signatureValid -and
        $result.certificateKeyUsageValid -and $result.certificateBasicConstraintsValid -and
        $result.certificateExtendedKeyUsageValid -and $result.certificateSecurityLevelValid -and
        $result.certificateChainValidAtSigningTime -and
        $result.certificateChainValidAtVerificationTime -and $result.credentialBindingValid
    $result.failures = @($resultFailures)
    $signatureResults.Add([pscustomobject]$result) | Out-Null
    Remove-Item Function:\Add-ResultFailure -ErrorAction SilentlyContinue
}

$sourceLawReviewerResult = $signatureResults | Where-Object { $_.signerSlot -eq "source-law-reviewer" } | Select-Object -First 1
$sourceLawAccountantResult = $signatureResults | Where-Object { $_.signerSlot -eq "source-law-qualified-accountant" } | Select-Object -First 1
if ($null -ne $sourceLawReviewerResult -and $null -ne $sourceLawAccountantResult -and
    ([string]::Equals($sourceLawReviewerResult.signerName, $sourceLawAccountantResult.signerName, [StringComparison]::OrdinalIgnoreCase) -or
     [string]::Equals($sourceLawReviewerResult.certificateSha256Fingerprint, $sourceLawAccountantResult.certificateSha256Fingerprint, [StringComparison]::OrdinalIgnoreCase))) {
    $sourceIndependenceFailure = "Source-law reviewer and source-law qualified-accountant signatures must use distinct independently verified people and certificates."
    Add-Failure $sourceIndependenceFailure
    foreach ($sourceResult in @($sourceLawReviewerResult, $sourceLawAccountantResult)) {
        $sourceResult.passed = $false
        $sourceResult.failures = @($sourceResult.failures) + $sourceIndependenceFailure
    }
}

$report = [ordered]@{
    schemaVersion = "accounts.release-evidence.durable-verification-report/v1"
    status = if ($failures.Count -eq 0) { "passed" } else { "failed" }
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    releaseCandidate = [ordered]@{
        commitSha = $CommitSha
        githubActionsRunUrl = $GitHubActionsRunUrl
        githubActionsCompletedAtUtc = $candidateRunCompletedAt.ToString("O")
    }
    trustPolicy = [ordered]@{
        fileName = Split-Path -Leaf $resolvedTrustPolicyPath
        expectedSha256 = $TrustPolicySha256
        actualSha256 = $actualTrustPolicySha256
        independentlyPinned = $actualTrustPolicySha256 -eq $TrustPolicySha256
        trustedRootCount = $policyRoots.Count
    }
    requiredTemplateCount = 6
    requiredSignatureCount = $requiredSigners.Count
    verifiedSignatureCount = @($signatureResults | Where-Object { $_.passed }).Count
    signatureResults = @($signatureResults)
    failureCount = $failures.Count
    failures = @($failures)
    completionPolicy = "This report proves detached-signature and trust-policy integrity only. It cannot create human acceptance, professional qualification, external validation, or durable publication evidence."
}

Write-AccountsUtf8NoBom $resolvedReportPath ($report | ConvertTo-Json -Depth 12)

if ($failures.Count -gt 0) {
    throw "Durable release-evidence verification failed with $($failures.Count) issue(s). See '$resolvedReportPath'."
}

Write-Host "Durable release-evidence verification passed for $($requiredSigners.Count) trusted signatures across six templates."
