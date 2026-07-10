param(
    [Parameter(Mandatory = $true)][string]$EvidenceDirectory,
    [Parameter(Mandatory = $true)][string]$TrustPolicyPath,
    [Parameter(Mandatory = $true)][string]$TrustPolicySha256,
    [Parameter(Mandatory = $true)][string]$CommitSha,
    [Parameter(Mandatory = $true)][string]$GitHubActionsRunUrl,
    [Parameter(Mandatory = $true)][DateTimeOffset]$CandidateRunCompletedAtUtc,
    [string]$ReportPath = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$manifestSchema = "accounts.release-evidence.publication-manifest/v1"
$trustPolicySchema = "accounts.release-evidence.trust-policy/v1"
$externalTemplateRelativePath = "external-ros-ixbrl-validation-template.md"
$externalSignatureRelativePath = "$externalTemplateRelativePath.external-ros-ixbrl-reviewer.signature.json"
$canonicalScenarios = @(
    "micro-ltd",
    "small-abridged-ltd",
    "dac-small",
    "clg-charity",
    "medium-audit-required"
)

$failures = [System.Collections.Generic.List[string]]::new()
$inventoryResults = [System.Collections.Generic.List[object]]::new()

function Add-Failure {
    param([string]$Message)
    $failures.Add($Message) | Out-Null
}

function Get-PathComparison {
    if ([IO.Path]::DirectorySeparatorChar -eq '\') {
        return [StringComparison]::OrdinalIgnoreCase
    }
    return [StringComparison]::Ordinal
}

function Test-PathEqual {
    param([string]$Left, [string]$Right)
    return [string]::Equals($Left, $Right, (Get-PathComparison))
}

function Test-PathInsideDirectory {
    param([string]$Path, [string]$Directory)

    $directoryPrefix = $Directory.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar
    ) + [IO.Path]::DirectorySeparatorChar
    return $Path.StartsWith($directoryPrefix, (Get-PathComparison))
}

function Get-NormalizedPathKey {
    param([string]$RelativePath)
    # A durable pack must extract without ambiguity on every supported consumer OS.
    # Normalize case even on a case-sensitive runner so paths that collide on
    # Windows/macOS cannot enter the signed inventory.
    return $RelativePath.ToLowerInvariant()
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

function Read-StrictUtf8 {
    param([string]$Path, [string]$Context)

    try {
        $bytes = [IO.File]::ReadAllBytes($Path)
        $encoding = New-Object Text.UTF8Encoding($false, $true)
        $text = $encoding.GetString($bytes)
        if ($text.IndexOf([char]0) -ge 0) {
            Add-Failure "$Context must not contain NUL bytes."
            return $null
        }
        if ($text.Length -gt 0 -and $text[0] -eq [char]0xFEFF) {
            $text = $text.Substring(1)
        }
        if ($text -match '[\u0001-\u0008\u000B\u000C\u000E-\u001F\u007F]') {
            Add-Failure "$Context must be plain UTF-8 text without binary control characters."
            return $null
        }
        return $text
    } catch {
        Add-Failure "$Context must be valid UTF-8: $($_.Exception.Message)"
        return $null
    }
}

function Read-JsonSafely {
    param([string]$Path, [string]$Context)

    $text = Read-StrictUtf8 $Path $Context
    if ($null -eq $text) { return $null }
    try {
        $value = $text | ConvertFrom-Json
        Test-NoPrivateKeyMaterial $value $Context
        return $value
    } catch {
        Add-Failure "$Context must be valid JSON: $($_.Exception.Message)"
        return $null
    }
}

function Test-NoPrivateKeyMaterial {
    param($Value, [string]$Context)

    if ($null -eq $Value) { return }
    if ($Value -is [string]) {
        $text = [string]$Value
        if ($text -match '(?i)BEGIN\s+(?:[A-Z0-9 ]+\s+)?PRIVATE\s+KEY') {
            Add-Failure "$Context must not contain decoded private-key material."
            return
        }
        $compact = $text -replace '\s', ''
        if ($compact.Length -ge 32 -and $compact -match '^[A-Za-z0-9+/_=-]+$') {
            try {
                $normalized = $compact.Replace('-', '+').Replace('_', '/')
                switch ($normalized.Length % 4) {
                    2 { $normalized += '==' }
                    3 { $normalized += '=' }
                }
                if ($normalized.Length % 4 -eq 0) {
                    $decodedBytes = [Convert]::FromBase64String($normalized)
                    $strictUtf8 = New-Object Text.UTF8Encoding($false, $true)
                    $decodedText = $strictUtf8.GetString($decodedBytes)
                    if ($decodedText -match '(?i)BEGIN\s+(?:[A-Z0-9 ]+\s+)?PRIVATE\s+KEY') {
                        Add-Failure "$Context must not contain base64-encoded private-key material."
                    }
                }
            } catch {
                # Non-base64 or non-text strings are allowed; only decoded key markers fail.
            }
        }
        return
    }
    if ($Value -is [Collections.IDictionary]) {
        foreach ($entry in $Value.GetEnumerator()) {
            Test-NoPrivateKeyMaterial $entry.Value $Context
        }
        return
    }
    if ($Value -is [Collections.IEnumerable]) {
        foreach ($item in $Value) {
            Test-NoPrivateKeyMaterial $item $Context
        }
        return
    }
    foreach ($property in @($Value.PSObject.Properties)) {
        Test-NoPrivateKeyMaterial $property.Value $Context
    }
}

function Test-ExactProperties {
    param($Object, [string[]]$ExpectedProperties, [string]$Context)

    if ($null -eq $Object) {
        Add-Failure "$Context is required."
        return $false
    }
    $actual = @($Object.PSObject.Properties.Name | Sort-Object)
    $expected = @($ExpectedProperties | Sort-Object)
    if (($actual -join "|") -cne ($expected -join "|")) {
        Add-Failure "$Context must contain exactly these properties: $($ExpectedProperties -join ', ')."
        return $false
    }
    return $true
}

function Test-ExactValue {
    param($Actual, $Expected, [string]$Context)

    if (-not [string]::Equals([string]$Actual, [string]$Expected, [StringComparison]::Ordinal)) {
        Add-Failure "$Context must be '$Expected'."
        return $false
    }
    return $true
}

function Test-NoReparsePoint {
    param([IO.FileSystemInfo]$Item, [string]$Context)

    if (($Item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        Add-Failure "$Context must not be a symbolic link, junction, or other reparse point."
        return $false
    }
    return $true
}

function Get-CanonicalRelativePath {
    param([string]$AbsolutePath, [string]$RootPath)

    if (-not (Test-PathInsideDirectory $AbsolutePath $RootPath)) {
        return $null
    }
    $rootPrefix = $RootPath.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar
    ) + [IO.Path]::DirectorySeparatorChar
    return $AbsolutePath.Substring($rootPrefix.Length).Replace('\', '/')
}

function Resolve-SafeInventoryPath {
    param([string]$RelativePath, [string]$RootPath, [string]$Context)

    if ([string]::IsNullOrWhiteSpace($RelativePath) -or
        $RelativePath.Length -gt 512 -or
        [IO.Path]::IsPathRooted($RelativePath) -or
        $RelativePath.Contains('\') -or
        $RelativePath.StartsWith('/') -or
        $RelativePath.EndsWith('/') -or
        $RelativePath.Contains('//')) {
        Add-Failure "$Context must be a safe canonical forward-slash relative path."
        return $null
    }

    $segments = @($RelativePath.Split('/'))
    if ([string]::Equals($segments[0], 'verified-publication', [StringComparison]::OrdinalIgnoreCase)) {
        Add-Failure "$Context uses reserved publication staging namespace '$($segments[0])'."
        return $null
    }
    foreach ($segment in $segments) {
        $windowsBaseName = $segment.Split('.')[0]
        if ($segment -in @("", ".", "..") -or
            $segment.Length -gt 128 -or
            $segment -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$' -or
            $segment.EndsWith('.')) {
            Add-Failure "$Context contains an unsafe path segment '$segment'."
            return $null
        }
        if ($windowsBaseName -match '^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$') {
            Add-Failure "$Context contains reserved Windows device path segment '$segment'."
            return $null
        }
    }

    try {
        $platformRelativePath = $RelativePath.Replace('/', [IO.Path]::DirectorySeparatorChar)
        $candidate = [IO.Path]::GetFullPath((Join-Path $RootPath $platformRelativePath))
    } catch {
        Add-Failure "$Context could not be canonicalized: $($_.Exception.Message)"
        return $null
    }

    if (-not (Test-PathInsideDirectory $candidate $RootPath)) {
        Add-Failure "$Context escapes the evidence directory after canonicalization."
        return $null
    }
    return $candidate
}

function Get-RegularEvidenceFiles {
    param([string]$RootPath)

    $files = [System.Collections.Generic.List[IO.FileInfo]]::new()
    $directories = New-Object 'System.Collections.Generic.Stack[IO.DirectoryInfo]'
    $rootItem = Get-Item -LiteralPath $RootPath -Force
    if (-not (Test-NoReparsePoint $rootItem "Evidence directory")) {
        return @()
    }
    $directories.Push($rootItem)

    while ($directories.Count -gt 0) {
        $directory = $directories.Pop()
        foreach ($item in @(Get-ChildItem -LiteralPath $directory.FullName -Force)) {
            if (-not (Test-NoReparsePoint $item "Evidence item '$($item.FullName)'")) {
                continue
            }
            if ($item.PSIsContainer) {
                $directories.Push($item)
            } elseif ($item -is [IO.FileInfo]) {
                $files.Add($item) | Out-Null
            } else {
                Add-Failure "Evidence item '$($item.FullName)' must be a regular file or directory."
            }
        }
    }
    return @($files)
}

function Get-ExpectedMediaType {
    param([string]$Extension)

    switch ($Extension.ToLowerInvariant()) {
        ".json" { return "application/json" }
        ".md" { return "text/markdown; charset=utf-8" }
        ".txt" { return "text/plain; charset=utf-8" }
        ".log" { return "text/plain; charset=utf-8" }
        ".png" { return "image/png" }
        ".pem" { return "application/x-pem-file" }
        ".crt" { return "application/pkix-cert" }
        ".html" { return "text/html; charset=utf-8" }
        ".xhtml" { return "application/xhtml+xml; charset=utf-8" }
        default { return $null }
    }
}

function Test-PublicCertificateFile {
    param([string]$Path, [string]$Context)

    $text = Read-StrictUtf8 $Path $Context
    if ($null -eq $text) { return }
    if ($text -match '(?i)PRIVATE\s+KEY') {
        Add-Failure "$Context must contain public certificate material only; private-key material is forbidden."
        return
    }
    $match = [regex]::Match(
        $text,
        '\A\s*-----BEGIN CERTIFICATE-----\s*(?<body>[A-Za-z0-9+/=\r\n]+?)\s*-----END CERTIFICATE-----\s*\z',
        [Text.RegularExpressions.RegexOptions]::CultureInvariant
    )
    if (-not $match.Success -or
        [regex]::Matches($text, '-----BEGIN CERTIFICATE-----').Count -ne 1 -or
        [regex]::Matches($text, '-----END CERTIFICATE-----').Count -ne 1) {
        Add-Failure "$Context must contain exactly one public PEM certificate."
        return
    }
    try {
        $der = [Convert]::FromBase64String(($match.Groups['body'].Value -replace '\s', ''))
        $certificate = New-Object Security.Cryptography.X509Certificates.X509Certificate2 -ArgumentList (,$der)
        $certificate.Dispose()
    } catch {
        Add-Failure "$Context must contain a parseable X.509 certificate: $($_.Exception.Message)"
    }
}

function Get-DecodedCssInspectionText {
    param([string]$Css)

    $decoded = [regex]::Replace(
        $Css,
        '/\*[\s\S]*?\*/',
        '',
        [Text.RegularExpressions.RegexOptions]::CultureInvariant
    )
    $decoded = [regex]::Replace(
        $decoded,
        '\\(?<hex>[0-9a-fA-F]{1,6})\s?',
        {
            param($match)
            $codePoint = [Convert]::ToInt32($match.Groups['hex'].Value, 16)
            if ($codePoint -le 0 -or $codePoint -gt 0x10FFFF) { return [char]0xFFFD }
            return [char]::ConvertFromUtf32($codePoint)
        },
        [Text.RegularExpressions.RegexOptions]::CultureInvariant
    )
    return [regex]::Replace(
        $decoded,
        '\\(.)',
        '$1',
        [Text.RegularExpressions.RegexOptions]::Singleline -bor [Text.RegularExpressions.RegexOptions]::CultureInvariant
    )
}

function Test-RetainedXhtmlContent {
    param([string]$Path, [string]$Context)

    $settings = New-Object Xml.XmlReaderSettings
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $settings.MaxCharactersFromEntities = 0
    $settings.MaxCharactersInDocument = 50MB

    $document = New-Object Xml.XmlDocument
    $document.PreserveWhitespace = $true
    $document.XmlResolver = $null
    try {
        $reader = [Xml.XmlReader]::Create($Path, $settings)
        try {
            $document.Load($reader)
        } finally {
            $reader.Dispose()
        }
    } catch {
        Add-Failure "$Context must be well-formed, DTD-free XHTML/XML retained as untrusted evidence: $($_.Exception.Message)"
        return
    }

    if ($null -eq $document.DocumentElement -or
        $document.DocumentElement.LocalName -cne 'html') {
        Add-Failure "$Context must have an XHTML/XML html root element."
        return
    }
    if (@($document.SelectNodes('//processing-instruction()')).Count -gt 0) {
        Add-Failure "$Context must not contain processing instructions."
    }

    $forbiddenElements = @{
        'applet' = $true; 'audio' = $true; 'base' = $true; 'button' = $true;
        'embed' = $true; 'form' = $true; 'frame' = $true; 'frameset' = $true;
        'iframe' = $true; 'img' = $true; 'input' = $true; 'link' = $true;
        'math' = $true; 'object' = $true; 'portal' = $true; 'script' = $true;
        'select' = $true; 'source' = $true; 'svg' = $true; 'textarea' = $true;
        'track' = $true; 'video' = $true
    }
    $urlAttributes = @{
        'action' = $true; 'background' = $true; 'cite' = $true; 'data' = $true;
        'formaction' = $true; 'href' = $true; 'longdesc' = $true; 'manifest' = $true;
        'ping' = $true; 'poster' = $true; 'src' = $true; 'srcset' = $true
    }
    foreach ($element in @($document.SelectNodes('//*'))) {
        $localName = $element.LocalName.ToLowerInvariant()
        $namespace = [string]$element.NamespaceURI
        $isHtmlNamespace = [string]::IsNullOrEmpty($namespace) -or
            $namespace -ceq 'http://www.w3.org/1999/xhtml'
        if ($isHtmlNamespace -and $forbiddenElements.ContainsKey($localName)) {
            Add-Failure "$Context contains forbidden active or externally loading HTML element '$localName'."
        }
        if ($namespace -in @('http://www.w3.org/2000/svg', 'http://www.w3.org/1998/Math/MathML')) {
            Add-Failure "$Context contains forbidden active SVG/MathML content."
        }
        if ($isHtmlNamespace -and $localName -ceq 'meta' -and
            $null -ne $element.Attributes['http-equiv']) {
            Add-Failure "$Context must not contain meta http-equiv behavior."
        }

        foreach ($attribute in @($element.Attributes)) {
            $attributeName = $attribute.LocalName.ToLowerInvariant()
            $attributeValue = [string]$attribute.Value
            if ($attributeName.StartsWith('on', [StringComparison]::OrdinalIgnoreCase)) {
                Add-Failure "$Context must not contain event-handler attributes."
            }
            if ($isHtmlNamespace -and $urlAttributes.ContainsKey($attributeName)) {
                $trimmed = $attributeValue.Trim()
                $isSafeFragment = $localName -ceq 'a' -and
                    $attributeName -ceq 'href' -and
                    $trimmed.StartsWith('#', [StringComparison]::Ordinal)
                if (-not [string]::IsNullOrEmpty($trimmed) -and -not $isSafeFragment) {
                    Add-Failure "$Context must not contain externally resolving HTML URL attributes."
                }
            }
            if ($attributeName -ceq 'style') {
                $css = Get-DecodedCssInspectionText $attributeValue
                if ($css -match '(?i)(?:@import|url\s*\(|expression\s*\(|behavior\s*:|-moz-binding\s*:|https?\s*:|data\s*:|//)') {
                    Add-Failure "$Context must not contain active or externally resolving inline CSS."
                }
            }
        }

        if ($isHtmlNamespace -and $localName -ceq 'style') {
            $css = Get-DecodedCssInspectionText ([string]$element.InnerText)
            if ($css -match '(?i)(?:@import|url\s*\(|expression\s*\(|behavior\s*:|-moz-binding\s*:|https?\s*:|data\s*:|//)') {
                Add-Failure "$Context must not contain active or externally resolving CSS."
            }
        }
    }
}

function Test-FileContent {
    param([string]$Path, [string]$RelativePath, [string]$ExpectedMediaType)

    $extension = [IO.Path]::GetExtension($Path).ToLowerInvariant()
    $context = "Inventory file '$RelativePath'"
    switch ($extension) {
        ".json" {
            Read-JsonSafely $Path $context | Out-Null
        }
        { $_ -in @(".md", ".txt", ".log") } {
            Read-StrictUtf8 $Path $context | Out-Null
        }
        ".png" {
            $bytes = [IO.File]::ReadAllBytes($Path)
            $magic = [byte[]](0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)
            if ($bytes.Length -lt $magic.Length) {
                Add-Failure "$context must contain the PNG signature."
            } else {
                for ($index = 0; $index -lt $magic.Length; $index++) {
                    if ($bytes[$index] -ne $magic[$index]) {
                        Add-Failure "$context must contain the PNG signature."
                        break
                    }
                }
            }
        }
        { $_ -in @(".pem", ".crt") } {
            Test-PublicCertificateFile $Path $context
        }
        { $_ -in @(".html", ".xhtml") } {
            Read-StrictUtf8 $Path $context | Out-Null
            Test-RetainedXhtmlContent $Path $context
        }
        default {
            Add-Failure "$context uses unsupported file extension '$extension'; archives and opaque binaries are forbidden."
        }
    }
}

function Get-FileSha256 {
    param([string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Write-Utf8NoBom {
    param([string]$Path, [string]$Content)
    $encoding = New-Object Text.UTF8Encoding($false)
    [IO.File]::WriteAllText($Path, $Content, $encoding)
}

if ($CommitSha -cnotmatch '^[0-9a-f]{40}$') {
    throw "CommitSha must be a full lowercase 40-character hexadecimal commit SHA."
}
if ($GitHubActionsRunUrl -notmatch '^https://github\.com/[^/\s]+/[^/\s]+/actions/runs/[0-9]+/?$') {
    throw "GitHubActionsRunUrl must be an exact GitHub Actions run URL."
}
$GitHubActionsRunUrl = $GitHubActionsRunUrl.TrimEnd('/')
if ($CandidateRunCompletedAtUtc.Offset -ne [TimeSpan]::Zero) {
    throw "CandidateRunCompletedAtUtc must be a UTC timestamp."
}
$candidateCompletedAt = $CandidateRunCompletedAtUtc.ToUniversalTime()
if ($TrustPolicySha256 -cnotmatch '^[0-9a-f]{64}$') {
    throw "TrustPolicySha256 must be a lowercase 64-character SHA-256 digest supplied out of band."
}
if (-not (Test-Path -LiteralPath $EvidenceDirectory -PathType Container)) {
    throw "EvidenceDirectory does not exist: $EvidenceDirectory"
}
if (-not (Test-Path -LiteralPath $TrustPolicyPath -PathType Leaf)) {
    throw "TrustPolicyPath does not exist: $TrustPolicyPath"
}

$resolvedEvidenceDirectory = (Resolve-Path -LiteralPath $EvidenceDirectory).Path.TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar
)
$resolvedTrustPolicyPath = (Resolve-Path -LiteralPath $TrustPolicyPath).Path
if (-not (Test-PathInsideDirectory $resolvedTrustPolicyPath $resolvedEvidenceDirectory)) {
    throw "TrustPolicyPath must be a regular file inside EvidenceDirectory."
}

if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $evidenceParent = Split-Path -Parent $resolvedEvidenceDirectory
    $evidenceLeaf = Split-Path -Leaf $resolvedEvidenceDirectory
    $ReportPath = Join-Path $evidenceParent "$evidenceLeaf-publication-inventory-report.json"
}
$resolvedReportPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ReportPath)
if ((Test-PathInsideDirectory $resolvedReportPath $resolvedEvidenceDirectory) -or
    (Test-PathEqual $resolvedReportPath $resolvedEvidenceDirectory)) {
    throw "ReportPath must remain outside EvidenceDirectory so verification does not mutate the immutable publication set."
}
$reportDirectory = Split-Path -Parent $resolvedReportPath
if (-not (Test-Path -LiteralPath $reportDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}

$trustPolicyItem = Get-Item -LiteralPath $resolvedTrustPolicyPath -Force
Test-NoReparsePoint $trustPolicyItem "Trust policy" | Out-Null
$actualTrustPolicySha256 = Get-FileSha256 $resolvedTrustPolicyPath
if ($actualTrustPolicySha256 -cne $TrustPolicySha256) {
    Add-Failure "Trust policy SHA-256 does not match the independently supplied out-of-band digest."
}

$trustPolicy = Read-JsonSafely $resolvedTrustPolicyPath "Trust policy"
$manifestPath = $null
$manifestExpectedSha256 = ""
if ($null -ne $trustPolicy) {
    Test-ExactValue (Get-JsonValue $trustPolicy @("schemaVersion")) $trustPolicySchema "Trust policy schemaVersion" | Out-Null
    Test-ExactValue (Get-JsonValue $trustPolicy @("releaseCandidate", "commitSha")) $CommitSha "Trust policy releaseCandidate.commitSha" | Out-Null
    Test-ExactValue (([string](Get-JsonValue $trustPolicy @("releaseCandidate", "githubActionsRunUrl"))).TrimEnd('/')) $GitHubActionsRunUrl "Trust policy releaseCandidate.githubActionsRunUrl" | Out-Null
    Test-ExactValue (Get-JsonValue $trustPolicy @("releaseCandidate", "githubActionsCompletedAtUtc")) $candidateCompletedAt.ToString("O") "Trust policy releaseCandidate.githubActionsCompletedAtUtc" | Out-Null

    $publicationManifest = Get-JsonValue $trustPolicy @("publicationManifest")
    if (Test-ExactProperties $publicationManifest @("fileName", "sha256") "Trust policy publicationManifest") {
        $manifestFileName = [string](Get-JsonValue $publicationManifest @("fileName"))
        $manifestExpectedSha256 = [string](Get-JsonValue $publicationManifest @("sha256"))
        if ([IO.Path]::GetFileName($manifestFileName) -cne $manifestFileName -or
            $manifestFileName -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._-]*\.json$') {
            Add-Failure "Trust policy publicationManifest.fileName must be a safe JSON file name in the evidence-directory root."
        } else {
            $manifestPath = Resolve-SafeInventoryPath $manifestFileName $resolvedEvidenceDirectory "Trust policy publicationManifest.fileName"
        }
        if ($manifestExpectedSha256 -cnotmatch '^[0-9a-f]{64}$') {
            Add-Failure "Trust policy publicationManifest.sha256 must be 64 lowercase hexadecimal characters."
        }
    }
}

$manifest = $null
if ($null -ne $manifestPath) {
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        Add-Failure "Publication manifest file is missing: $manifestPath"
    } else {
        $manifestItem = Get-Item -LiteralPath $manifestPath -Force
        Test-NoReparsePoint $manifestItem "Publication manifest" | Out-Null
        $actualManifestSha256 = Get-FileSha256 $manifestPath
        if ($manifestExpectedSha256 -cne $actualManifestSha256) {
            Add-Failure "Publication manifest SHA-256 does not match the out-of-band-pinned trust policy."
        }
        $manifest = Read-JsonSafely $manifestPath "Publication manifest"
    }
}

$manifestEntries = @()
if ($null -ne $manifest) {
    Test-ExactProperties $manifest @("schemaVersion", "releaseCandidate", "files") "Publication manifest" | Out-Null
    Test-ExactValue (Get-JsonValue $manifest @("schemaVersion")) $manifestSchema "Publication manifest schemaVersion" | Out-Null
    $manifestCandidate = Get-JsonValue $manifest @("releaseCandidate")
    Test-ExactProperties $manifestCandidate @("commitSha", "githubActionsRunUrl", "githubActionsCompletedAtUtc") "Publication manifest releaseCandidate" | Out-Null
    Test-ExactValue (Get-JsonValue $manifest @("releaseCandidate", "commitSha")) $CommitSha "Publication manifest releaseCandidate.commitSha" | Out-Null
    Test-ExactValue (([string](Get-JsonValue $manifest @("releaseCandidate", "githubActionsRunUrl"))).TrimEnd('/')) $GitHubActionsRunUrl "Publication manifest releaseCandidate.githubActionsRunUrl" | Out-Null
    Test-ExactValue (Get-JsonValue $manifest @("releaseCandidate", "githubActionsCompletedAtUtc")) $candidateCompletedAt.ToString("O") "Publication manifest releaseCandidate.githubActionsCompletedAtUtc" | Out-Null
    $manifestEntries = @(Get-JsonValue $manifest @("files"))
    if ($manifestEntries.Count -eq 0) {
        Add-Failure "Publication manifest files must be a non-empty array."
    }
}

$allRegularFiles = @(Get-RegularEvidenceFiles $resolvedEvidenceDirectory)
$actualEvidenceByKey = @{}
foreach ($file in $allRegularFiles) {
    $resolvedFile = $file.FullName
    if ((Test-PathEqual $resolvedFile $resolvedTrustPolicyPath) -or
        ($null -ne $manifestPath -and (Test-PathEqual $resolvedFile $manifestPath))) {
        continue
    }
    $relativePath = Get-CanonicalRelativePath $resolvedFile $resolvedEvidenceDirectory
    if ($null -eq $relativePath) {
        Add-Failure "Evidence file escaped the canonical evidence-directory boundary: $resolvedFile"
        continue
    }
    $key = Get-NormalizedPathKey $relativePath
    if ($actualEvidenceByKey.ContainsKey($key)) {
        Add-Failure "Evidence directory contains OS-ambiguous duplicate path '$relativePath'."
    } else {
        $actualEvidenceByKey[$key] = [pscustomobject]@{
            RelativePath = $relativePath
            File = $file
        }
    }
}

$manifestEntryByKey = @{}
$externalEntries = [System.Collections.Generic.List[object]]::new()
foreach ($entry in $manifestEntries) {
    $entryContext = "Publication manifest file entry"
    if (-not (Test-ExactProperties $entry @("relativePath", "byteSize", "sha256", "mediaType", "classification") $entryContext)) {
        continue
    }
    $relativePath = [string](Get-JsonValue $entry @("relativePath"))
    $candidatePath = Resolve-SafeInventoryPath $relativePath $resolvedEvidenceDirectory "$entryContext relativePath"
    $key = Get-NormalizedPathKey $relativePath
    if ($manifestEntryByKey.ContainsKey($key)) {
        Add-Failure "Publication manifest contains duplicate or OS-ambiguous relativePath '$relativePath'."
        continue
    }
    $manifestEntryByKey[$key] = $entry

    if (($null -ne $manifestPath -and (Test-PathEqual $candidatePath $manifestPath)) -or
        (Test-PathEqual $candidatePath $resolvedTrustPolicyPath)) {
        Add-Failure "Publication manifest must exclude itself and the trust policy: '$relativePath'."
        continue
    }

    $sha256 = [string](Get-JsonValue $entry @("sha256"))
    if ($sha256 -cnotmatch '^[0-9a-f]{64}$') {
        Add-Failure "Publication manifest '$relativePath' sha256 must be 64 lowercase hexadecimal characters."
    }
    $byteSizeText = [string](Get-JsonValue $entry @("byteSize"))
    $expectedByteSize = 0L
    if ($byteSizeText -notmatch '^[1-9][0-9]*$' -or
        -not [long]::TryParse($byteSizeText, [ref]$expectedByteSize)) {
        Add-Failure "Publication manifest '$relativePath' byteSize must be a positive 64-bit integer."
    }
    $classification = [string](Get-JsonValue $entry @("classification"))
    if ($classification -cnotmatch '^[a-z0-9]+(?:-[a-z0-9]+)*$') {
        Add-Failure "Publication manifest '$relativePath' classification must be a lowercase non-placeholder slug."
    }

    $extension = [IO.Path]::GetExtension($relativePath).ToLowerInvariant()
    $expectedMediaType = Get-ExpectedMediaType $extension
    if ($null -eq $expectedMediaType) {
        Add-Failure "Inventory file '$relativePath' uses unsupported file extension '$extension'; archives and opaque binaries are forbidden."
    } else {
        Test-ExactValue (Get-JsonValue $entry @("mediaType")) $expectedMediaType "Publication manifest '$relativePath' mediaType" | Out-Null
    }

    if (-not $actualEvidenceByKey.ContainsKey($key)) {
        Add-Failure "Publication manifest references a missing regular evidence file '$relativePath'."
        continue
    }
    $actual = $actualEvidenceByKey[$key]
    if ($actual.RelativePath -cne $relativePath) {
        Add-Failure "Publication manifest relativePath '$relativePath' must preserve the canonical on-disk path '$($actual.RelativePath)'."
    }
    if ($actual.File.Length -ne $expectedByteSize) {
        Add-Failure "Publication manifest '$relativePath' byteSize does not match the regular evidence file."
    }
    $actualSha256 = Get-FileSha256 $actual.File.FullName
    if ($actualSha256 -cne $sha256) {
        Add-Failure "Publication manifest '$relativePath' sha256 does not match the regular evidence file."
    }
    if ($null -ne $expectedMediaType) {
        Test-FileContent $actual.File.FullName $relativePath $expectedMediaType
    }

    $inventoryResults.Add([ordered]@{
        relativePath = $relativePath
        byteSize = $actual.File.Length
        sha256 = $actualSha256
        mediaType = [string](Get-JsonValue $entry @("mediaType"))
        classification = $classification
        contentTypeValid = $null -ne $expectedMediaType
    }) | Out-Null
    if ($classification -ceq "external-ixbrl") {
        $externalEntries.Add([pscustomobject]@{
            RelativePath = $relativePath
            Sha256 = $sha256
            Extension = $extension
        }) | Out-Null
    }
}

foreach ($actualKey in @($actualEvidenceByKey.Keys)) {
    if (-not $manifestEntryByKey.ContainsKey($actualKey)) {
        Add-Failure "Publication directory contains an unmanifested extra regular evidence file '$($actualEvidenceByKey[$actualKey].RelativePath)'."
    }
}

if ($manifestEntryByKey.Count -ne $actualEvidenceByKey.Count) {
    Add-Failure "Publication manifest and evidence directory must have an exact no-extra/no-missing regular-file set."
}

if ($externalEntries.Count -ne $canonicalScenarios.Count) {
    Add-Failure "Publication manifest must contain exactly five external-ixbrl HTML/XHTML entries."
}
$externalScenarioByCode = @{}
$externalHashSet = @{}
foreach ($externalEntry in $externalEntries) {
    if ($externalEntry.Extension -notin @(".html", ".xhtml")) {
        Add-Failure "External iXBRL entry '$($externalEntry.RelativePath)' must be an HTML or XHTML file."
        continue
    }
    $scenarioCode = [IO.Path]::GetFileNameWithoutExtension($externalEntry.RelativePath)
    if ($scenarioCode -cnotin $canonicalScenarios) {
        Add-Failure "External iXBRL entry '$($externalEntry.RelativePath)' must use a canonical scenario code as its file name."
        continue
    }
    if ($externalScenarioByCode.ContainsKey($scenarioCode)) {
        Add-Failure "External iXBRL scenario '$scenarioCode' must have exactly one distinct path."
    } else {
        $externalScenarioByCode[$scenarioCode] = $externalEntry
    }
    if ($externalHashSet.ContainsKey($externalEntry.Sha256)) {
        Add-Failure "External iXBRL files must have distinct SHA-256 hashes."
    } else {
        $externalHashSet[$externalEntry.Sha256] = $externalEntry.RelativePath
    }
}
foreach ($scenarioCode in $canonicalScenarios) {
    if (-not $externalScenarioByCode.ContainsKey($scenarioCode)) {
        Add-Failure "Publication manifest is missing external-ixbrl evidence for canonical scenario '$scenarioCode'."
    }
}

$externalTemplateKey = Get-NormalizedPathKey $externalTemplateRelativePath
$externalSignatureKey = Get-NormalizedPathKey $externalSignatureRelativePath
$externalTemplateText = $null
if (-not $actualEvidenceByKey.ContainsKey($externalTemplateKey) -or
    -not $manifestEntryByKey.ContainsKey($externalTemplateKey)) {
    Add-Failure "The signed external ROS template '$externalTemplateRelativePath' must be present and inventoried."
} else {
    $externalTemplateText = Read-StrictUtf8 $actualEvidenceByKey[$externalTemplateKey].File.FullName "Signed external ROS template"
}
if (-not $actualEvidenceByKey.ContainsKey($externalSignatureKey) -or
    -not $manifestEntryByKey.ContainsKey($externalSignatureKey)) {
    Add-Failure "The external ROS detached-signature sidecar '$externalSignatureRelativePath' must be present and inventoried."
} else {
    $signatureEnvelope = Read-JsonSafely $actualEvidenceByKey[$externalSignatureKey].File.FullName "External ROS detached-signature sidecar"
    if ($null -ne $signatureEnvelope) {
        Test-ExactValue (Get-JsonValue $signatureEnvelope @("schemaVersion")) "accounts.release-evidence.detached-signature/v1" "External ROS detached-signature schemaVersion" | Out-Null
        Test-ExactValue (Get-JsonValue $signatureEnvelope @("signatureAlgorithm")) "openssl-evp-sha256" "External ROS detached-signature signatureAlgorithm" | Out-Null
        $statementBase64 = [string](Get-JsonValue $signatureEnvelope @("statementBase64"))
        try {
            $statementBytes = [Convert]::FromBase64String($statementBase64)
            $strictUtf8 = New-Object Text.UTF8Encoding($false, $true)
            $statementText = $strictUtf8.GetString($statementBytes)
            $signatureStatement = $statementText | ConvertFrom-Json
            Test-ExactValue (Get-JsonValue $signatureStatement @("schemaVersion")) "accounts.release-evidence.signature-statement/v1" "External ROS signature statement schemaVersion" | Out-Null
            Test-ExactValue (Get-JsonValue $signatureStatement @("releaseCandidate", "commitSha")) $CommitSha "External ROS signature statement releaseCandidate.commitSha" | Out-Null
            Test-ExactValue (([string](Get-JsonValue $signatureStatement @("releaseCandidate", "githubActionsRunUrl"))).TrimEnd('/')) $GitHubActionsRunUrl "External ROS signature statement releaseCandidate.githubActionsRunUrl" | Out-Null
            Test-ExactValue (Get-JsonValue $signatureStatement @("releaseCandidate", "githubActionsCompletedAtUtc")) $candidateCompletedAt.ToString("O") "External ROS signature statement releaseCandidate.githubActionsCompletedAtUtc" | Out-Null
            Test-ExactValue (Get-JsonValue $signatureStatement @("template", "fileName")) $externalTemplateRelativePath "External ROS signature statement template.fileName" | Out-Null
            if ($actualEvidenceByKey.ContainsKey($externalTemplateKey)) {
                Test-ExactValue (Get-JsonValue $signatureStatement @("template", "byteSize")) $actualEvidenceByKey[$externalTemplateKey].File.Length "External ROS signature statement template.byteSize" | Out-Null
                Test-ExactValue (Get-JsonValue $signatureStatement @("template", "sha256")) (Get-FileSha256 $actualEvidenceByKey[$externalTemplateKey].File.FullName) "External ROS signature statement template.sha256" | Out-Null
            }
            Test-ExactValue (Get-JsonValue $signatureStatement @("signer", "slot")) "external-ros-ixbrl-reviewer" "External ROS signature statement signer.slot" | Out-Null
            if ([string]::IsNullOrWhiteSpace([string](Get-JsonValue $signatureEnvelope @("signatureBase64")))) {
                Add-Failure "External ROS detached-signature sidecar must contain detached signature bytes."
            }
        } catch {
            Add-Failure "External ROS detached-signature statement must be strict UTF-8 base64 JSON: $($_.Exception.Message)"
        }
    }
}

if ($null -ne $externalTemplateText) {
    $templateScenarioHashes = @{}
    foreach ($line in @($externalTemplateText -split '\r?\n')) {
        if ($line -notmatch '^\s*\|') { continue }
        $cells = @($line.Split('|') | ForEach-Object { $_.Trim() })
        if ($cells.Count -lt 5) { continue }
        $scenarioCode = $cells[1]
        if ($scenarioCode -cin $canonicalScenarios) {
            $templateScenarioHashes[$scenarioCode] = $cells[3]
        }
    }
    foreach ($scenarioCode in $canonicalScenarios) {
        if (-not $externalScenarioByCode.ContainsKey($scenarioCode)) { continue }
        $expectedHash = $externalScenarioByCode[$scenarioCode].Sha256
        if (-not $templateScenarioHashes.ContainsKey($scenarioCode) -or
            $templateScenarioHashes[$scenarioCode] -cne $expectedHash -or
            $externalTemplateText -cnotmatch "(?i)(?<![0-9a-f])$([regex]::Escape($expectedHash))(?![0-9a-f])") {
            Add-Failure "Signed external ROS template must contain the exact inventoried SHA-256 for scenario '$scenarioCode'."
        }
    }
}

$report = [ordered]@{
    schemaVersion = "accounts.release-evidence.publication-inventory-report/v1"
    status = if ($failures.Count -eq 0) { "passed" } else { "failed" }
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    releaseCandidate = [ordered]@{
        commitSha = $CommitSha
        githubActionsRunUrl = $GitHubActionsRunUrl
        githubActionsCompletedAtUtc = $candidateCompletedAt.ToString("O")
    }
    trustPolicy = [ordered]@{
        relativePath = Get-CanonicalRelativePath $resolvedTrustPolicyPath $resolvedEvidenceDirectory
        suppliedSha256 = $TrustPolicySha256
        actualSha256 = $actualTrustPolicySha256
        publicationManifestSha256 = $manifestExpectedSha256
    }
    regularEvidenceFileCount = $actualEvidenceByKey.Count
    manifestEntryCount = $manifestEntryByKey.Count
    externalIxbrlScenarioCodes = @($externalScenarioByCode.Keys | Sort-Object)
    externalIxbrlHandling = [ordered]@{
        classification = "untrusted-active-document-evidence"
        structuralInspection = "well-formed DTD-free XHTML/XML; common active and externally loading browser content rejected"
        requiredReviewBoundary = "extract and inspect only in an offline sandboxed viewer; inventory verification is not a complete HTML safety guarantee"
    }
    inventory = @($inventoryResults | Sort-Object relativePath)
    failures = @($failures)
}
Write-Utf8NoBom $resolvedReportPath ($report | ConvertTo-Json -Depth 10)

if ($failures.Count -gt 0) {
    throw "Durable release publication inventory verification failed with $($failures.Count) failure(s). See $resolvedReportPath"
}

Write-Host "Durable release publication inventory verified: $($actualEvidenceByKey.Count) files, five candidate-bound external iXBRL scenarios."
