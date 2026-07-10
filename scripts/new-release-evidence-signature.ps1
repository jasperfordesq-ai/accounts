param(
    [Parameter(Mandatory = $true)][string]$TemplatePath,
    [string]$OutputPath = "",
    [Parameter(Mandatory = $true)][ValidateSet(
        "visual-qa-reviewer",
        "source-law-reviewer",
        "source-law-qualified-accountant",
        "external-ros-ixbrl-reviewer",
        "qualified-accountant",
        "manual-handoff-reviewer",
        "monitoring-release-operator"
    )][string]$SignerSlot,
    [Parameter(Mandatory = $true)][string]$SignerName,
    [Parameter(Mandatory = $true)][string]$ProfessionalCapacity,
    [Parameter(Mandatory = $true)][string]$CredentialReference,
    [Parameter(Mandatory = $true)][string]$CommitSha,
    [Parameter(Mandatory = $true)][string]$GitHubActionsRunUrl,
    [Parameter(Mandatory = $true)][DateTimeOffset]$CandidateRunCompletedAtUtc,
    [Parameter(Mandatory = $true)][string]$CertificatePath,
    [Parameter(Mandatory = $true)][string]$PrivateKeyPath,
    [string]$PrivateKeyPassphraseFile = "",
    [string[]]$ChainCertificatePaths = @(),
    [DateTimeOffset]$SignedAtUtc = [DateTimeOffset]::UtcNow,
    [string]$OpenSslPath = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

Import-Module (Join-Path $PSScriptRoot "ReleaseEvidenceCrypto.psm1") -Force

$slotTemplates = [ordered]@{
    "visual-qa-reviewer" = "visual-qa-signoff-template.md"
    "source-law-reviewer" = "source-law-review-template.md"
    "source-law-qualified-accountant" = "source-law-review-template.md"
    "external-ros-ixbrl-reviewer" = "external-ros-ixbrl-validation-template.md"
    "qualified-accountant" = "qualified-accountant-acceptance-template.md"
    "manual-handoff-reviewer" = "manual-handoff-acceptance-template.md"
    "monitoring-release-operator" = "monitoring-provider-confirmation-template.md"
}

function Assert-RealIdentityValue {
    param([string]$Value, [string]$Label)

    if ([string]::IsNullOrWhiteSpace($Value) -or $Value.Trim().Length -lt 3) {
        throw "$Label must be a real, non-empty value."
    }

    if ($Value.Trim() -match "(?i)^(accepted|none|n/a|pending|todo|tbd|test|unknown)$") {
        throw "$Label must not be a placeholder."
    }
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

Assert-RealIdentityValue $SignerName "SignerName"
Assert-RealIdentityValue $ProfessionalCapacity "ProfessionalCapacity"

$credentialUri = $null
if (-not [Uri]::TryCreate($CredentialReference, [UriKind]::Absolute, [ref]$credentialUri) -or
    -not [string]::Equals($credentialUri.Scheme, "https", [StringComparison]::OrdinalIgnoreCase) -or
    [string]::IsNullOrWhiteSpace($credentialUri.Host) -or
    -not [string]::IsNullOrWhiteSpace($credentialUri.UserInfo) -or
    -not [string]::IsNullOrWhiteSpace($credentialUri.Query) -or
    -not [string]::IsNullOrWhiteSpace($credentialUri.Fragment)) {
    throw "CredentialReference must be a public absolute HTTPS reference without credentials, query parameters, or fragments."
}

$resolvedTemplate = (Resolve-Path -LiteralPath $TemplatePath).Path
$templateFileName = Split-Path -Leaf $resolvedTemplate
$expectedTemplate = [string]$slotTemplates[$SignerSlot]
if (-not [string]::Equals($templateFileName, $expectedTemplate, [StringComparison]::Ordinal)) {
    throw "Signer slot '$SignerSlot' must sign '$expectedTemplate', not '$templateFileName'."
}

$resolvedCertificate = (Resolve-Path -LiteralPath $CertificatePath).Path
$resolvedPrivateKey = (Resolve-Path -LiteralPath $PrivateKeyPath).Path
$resolvedPrivateKeyPassphraseFile = if ([string]::IsNullOrWhiteSpace($PrivateKeyPassphraseFile)) {
    ""
} else {
    (Resolve-Path -LiteralPath $PrivateKeyPassphraseFile).Path
}
$resolvedChainCertificates = @($ChainCertificatePaths | ForEach-Object { (Resolve-Path -LiteralPath $_).Path })
$templateDirectory = (Split-Path -Parent $resolvedTemplate).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$pathComparison = if ([IO.Path]::DirectorySeparatorChar -eq '\') {
    [StringComparison]::OrdinalIgnoreCase
} else {
    [StringComparison]::Ordinal
}
foreach ($privateMaterial in @($resolvedPrivateKey, $resolvedPrivateKeyPassphraseFile) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) {
    if ($privateMaterial.StartsWith($templateDirectory, $pathComparison)) {
        throw "Private keys and passphrase files must remain outside the release-evidence/template directory."
    }
}
$openssl = Resolve-AccountsOpenSsl $OpenSslPath
Write-Verbose "Using OpenSSL at $openssl"

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path (Split-Path -Parent $resolvedTemplate) "$templateFileName.$SignerSlot.signature.json"
}
$resolvedOutput = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
if (Test-Path -LiteralPath $resolvedOutput) {
    throw "Refusing to overwrite detached signature sidecar '$resolvedOutput'. Remove it only as part of an explicitly restarted review."
}

$outputDirectory = Split-Path -Parent $resolvedOutput
if (-not (Test-Path -LiteralPath $outputDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

$certificateFingerprint = Get-AccountsCertificateFingerprint $openssl $resolvedCertificate
$certificateSubject = Get-AccountsCertificateSubject $openssl $resolvedCertificate
$certificateSerialNumber = Get-AccountsCertificateSerialNumber $openssl $resolvedCertificate
$templateItem = Get-Item -LiteralPath $resolvedTemplate
$signedAt = $SignedAtUtc.ToUniversalTime()
if ($signedAt -gt [DateTimeOffset]::UtcNow.AddMinutes(5)) {
    throw "SignedAtUtc cannot be in the future."
}
if ($signedAt -lt $candidateRunCompletedAt) {
    throw "SignedAtUtc cannot predate the successful candidate CI completion time."
}

$statement = [ordered]@{
    schemaVersion = "accounts.release-evidence.signature-statement/v1"
    releaseCandidate = [ordered]@{
        commitSha = $CommitSha
        githubActionsRunUrl = $GitHubActionsRunUrl
        githubActionsCompletedAtUtc = $candidateRunCompletedAt.ToString("O")
    }
    template = [ordered]@{
        fileName = $templateFileName
        byteSize = $templateItem.Length
        sha256 = Get-AccountsFileSha256 $resolvedTemplate
    }
    signer = [ordered]@{
        slot = $SignerSlot
        name = $SignerName.Trim()
        professionalCapacity = $ProfessionalCapacity.Trim()
        credentialReference = $credentialUri.AbsoluteUri
    }
    certificate = [ordered]@{
        sha256Fingerprint = $certificateFingerprint
        subjectRfc2253 = $certificateSubject
        serialNumber = $certificateSerialNumber
    }
    signedAtUtc = $signedAt.ToString("O")
}

$statementJson = $statement | ConvertTo-Json -Depth 8 -Compress
Write-Verbose "Prepared candidate-bound statement for $templateFileName / $SignerSlot"
$temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) ("accounts-release-signature-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null

try {
    $statementPath = Join-Path $temporaryDirectory "statement.json"
    $signaturePath = Join-Path $temporaryDirectory "statement.signature"
    $publicKeyPath = Join-Path $temporaryDirectory "certificate-public-key.pem"
    Write-AccountsUtf8NoBom $statementPath $statementJson
    Write-Verbose "Wrote exact statement bytes to temporary storage"

    $signArguments = [System.Collections.Generic.List[string]]::new()
    @("dgst", "-sha256", "-sign", $resolvedPrivateKey) | ForEach-Object { $signArguments.Add($_) }
    if (-not [string]::IsNullOrWhiteSpace($resolvedPrivateKeyPassphraseFile)) {
        $signArguments.Add("-passin")
        $signArguments.Add("file:$resolvedPrivateKeyPassphraseFile")
    }
    @("-out", $signaturePath, $statementPath) | ForEach-Object { $signArguments.Add($_) }
    Invoke-AccountsOpenSsl $openssl $signArguments.ToArray() "Sign release-evidence statement" | Out-Null
    Invoke-AccountsOpenSsl $openssl @(
        "x509", "-in", $resolvedCertificate, "-pubkey", "-noout", "-out", $publicKeyPath
    ) "Extract signing-certificate public key" | Out-Null
    Invoke-AccountsOpenSsl $openssl @(
        "dgst", "-sha256", "-verify", $publicKeyPath, "-signature", $signaturePath, $statementPath
    ) "Confirm private key matches signing certificate" | Out-Null
    Write-Verbose "Created detached OpenSSL signature"

    # Get-Content decorates strings with PSPath metadata in Windows PowerShell 5;
    # ConvertTo-Json then recursively serializes those extended properties. Read
    # exact plain strings through System.IO so sidecars stay small and portable.
    $certificatePem = [IO.File]::ReadAllText($resolvedCertificate)
    $chainPem = @($resolvedChainCertificates | ForEach-Object { [IO.File]::ReadAllText($_) })
    Write-Verbose "Loaded public certificate material"
    $envelope = [ordered]@{
        schemaVersion = "accounts.release-evidence.detached-signature/v1"
        signatureAlgorithm = "openssl-evp-sha256"
        statementEncoding = "base64"
        statementBase64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($statementPath))
        signatureEncoding = "base64"
        signatureBase64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($signaturePath))
        signerCertificatePem = $certificatePem
        certificateChainPem = $chainPem
    }

    Write-Verbose "Serializing detached signature envelope"
    $envelopeJson = $envelope | ConvertTo-Json -Depth 4
    Write-Verbose "Serialized detached signature envelope"
    if ($envelopeJson -match "PRIVATE KEY") {
        throw "Detached signature envelope must never contain private-key material."
    }
    Write-AccountsUtf8NoBom $resolvedOutput $envelopeJson
    Write-Verbose "Wrote detached signature envelope to $resolvedOutput"
} finally {
    Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force -ErrorAction SilentlyContinue
}

[pscustomobject]@{
    status = "created"
    templateFile = $templateFileName
    signerSlot = $SignerSlot
    signerName = $SignerName.Trim()
    certificateSha256Fingerprint = $certificateFingerprint
    signatureFile = Split-Path -Leaf $resolvedOutput
    signaturePath = $resolvedOutput
}
