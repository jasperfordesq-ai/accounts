param(
    [Parameter(Mandatory = $true)]
    [string]$ArchivePath,
    [string]$ExpectedVersion,
    [string]$ExpectedCommitSha,
    [string]$ExpectedGitHubActionsRunUrl
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Add-Failure([System.Collections.Generic.List[string]]$Failures, [string]$Message) {
    $Failures.Add($Message)
}

$resolvedArchive = (Resolve-Path -LiteralPath $ArchivePath -ErrorAction Stop).Path
$failures = [System.Collections.Generic.List[string]]::new()
$checksumPath = "$resolvedArchive.sha256"
if (-not (Test-Path -LiteralPath $checksumPath -PathType Leaf)) {
    Add-Failure $failures "Release archive checksum sidecar is missing."
} else {
    $checksumLine = (Get-Content -LiteralPath $checksumPath -TotalCount 1).Trim()
    $expectedName = [IO.Path]::GetFileName($resolvedArchive)
    if ($checksumLine -cnotmatch "^([0-9a-f]{64})  $([Regex]::Escape($expectedName))$") {
        Add-Failure $failures "Release checksum sidecar has an invalid format or file name."
    } elseif ((Get-FileHash -LiteralPath $resolvedArchive -Algorithm SHA256).Hash.ToLowerInvariant() -cne $Matches[1]) {
        Add-Failure $failures "Release archive SHA-256 does not match its checksum sidecar."
    }
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("filingbridge-private-verify-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
try {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archiveEntryNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $archiveStream = [IO.File]::OpenRead($resolvedArchive)
    try {
        $zip = [IO.Compression.ZipArchive]::new(
            $archiveStream,
            [IO.Compression.ZipArchiveMode]::Read,
            $false)
        try {
            if ($zip.Entries.Count -gt 1000) {
                Add-Failure $failures "Release archive contains too many entries."
            }
            [long]$totalUncompressedBytes = 0
            foreach ($entry in $zip.Entries) {
                $entryName = $entry.FullName.Replace('\', '/')
                $pathForSegments = if ($entryName.EndsWith('/', [StringComparison]::Ordinal)) { $entryName.Substring(0, $entryName.Length - 1) } else { $entryName }
                $unsafeSegment = @($pathForSegments.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count -gt 0
                $totalUncompressedBytes += $entry.Length
                if ([string]::IsNullOrWhiteSpace($entryName) -or
                    $entryName.StartsWith('/', [StringComparison]::Ordinal) -or
                    $entryName.Contains(':') -or
                    $unsafeSegment -or
                    -not $archiveEntryNames.Add($entryName)) {
                    Add-Failure $failures "Release archive contains an unsafe or duplicate entry."
                }
                $unixFileType = (($entry.ExternalAttributes -shr 16) -band 0xF000)
                if ($unixFileType -eq 0xA000) {
                    Add-Failure $failures "Release archive must not contain symbolic links."
                }
                if ($entry.Length -gt 536870912) {
                    Add-Failure $failures "Release archive contains an unexpectedly large entry."
                }
            }
            if ($totalUncompressedBytes -gt 1073741824) {
                Add-Failure $failures "Release archive expands beyond the one-gigabyte verification limit."
            }
        }
        finally {
            $zip.Dispose()
        }
    }
    finally {
        $archiveStream.Dispose()
    }

    if ($failures.Count -gt 0) {
        throw "Release archive structural verification failed before extraction: $($failures -join ' ')"
    }

    Expand-Archive -LiteralPath $resolvedArchive -DestinationPath $temporaryRoot
    $manifestPath = Join-Path $temporaryRoot "release.json"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        Add-Failure $failures "release.json is missing from the archive root."
    } else {
        $manifestText = Get-Content -LiteralPath $manifestPath -Raw
        $manifest = $manifestText | ConvertFrom-Json
        if ([string]$manifest.schemaVersion -ne "filingbridge.private-server.release/v1") {
            Add-Failure $failures "release.json schemaVersion is invalid."
        }
        if ([string]$manifest.version -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-(?:0|[1-9][0-9]*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*))*)?$') {
            Add-Failure $failures "release.json version is invalid."
        }
        $generatedAtProperties = [Regex]::Matches($manifestText, '"generatedAtUtc"\s*:')
        $generatedAtMatch = [Regex]::Match($manifestText, '"generatedAtUtc"\s*:\s*"(?<value>[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\.[0-9]{7}Z)"')
        $generatedAt = [DateTimeOffset]::MinValue
        if ($generatedAtProperties.Count -ne 1 -or -not $generatedAtMatch.Success -or
            -not [DateTimeOffset]::TryParseExact(
                $generatedAtMatch.Groups['value'].Value,
                'o',
                [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::None,
                [ref]$generatedAt) -or
            $generatedAt.Offset -ne [TimeSpan]::Zero) {
            Add-Failure $failures "release.json generatedAtUtc must be a UTC round-trip timestamp."
        }
        if ($ExpectedVersion -and [string]$manifest.version -cne $ExpectedVersion) {
            Add-Failure $failures "release.json version does not match the expected release."
        }
        if ([string]$manifest.candidate.commitSha -cnotmatch '^[0-9a-f]{40}$' -or
            ($ExpectedCommitSha -and [string]$manifest.candidate.commitSha -cne $ExpectedCommitSha)) {
            Add-Failure $failures "release.json candidate commit is invalid or mismatched."
        }
        if ([string]$manifest.candidate.githubActionsRunUrl -cnotmatch '^https://github\.com/jasperfordesq-ai/accounts/actions/runs/[1-9][0-9]*$' -or
            ($ExpectedGitHubActionsRunUrl -and [string]$manifest.candidate.githubActionsRunUrl -cne $ExpectedGitHubActionsRunUrl)) {
            Add-Failure $failures "release.json Actions run URL is invalid or mismatched."
        }
        if (@($manifest.supportedHosts).Count -ne 2 -or
            @($manifest.supportedHosts) -notcontains "windows-x64" -or
            @($manifest.supportedHosts) -notcontains "ubuntu-x64") {
            Add-Failure $failures "release.json must honestly support exactly windows-x64 and ubuntu-x64."
        }
        foreach ($component in @("backend", "frontend", "postgres")) {
            $reference = [string]$manifest.images.$component.exactDigestReference
            $pattern = switch ($component) {
                "backend" { '^ghcr\.io/jasperfordesq-ai/accounts-api@sha256:[0-9a-f]{64}$' }
                "frontend" { '^ghcr\.io/jasperfordesq-ai/accounts-frontend@sha256:[0-9a-f]{64}$' }
                default { '^postgres@sha256:[0-9a-f]{64}$' }
            }
            if ($reference -cnotmatch $pattern) {
                Add-Failure $failures "release.json $component image is not an exact digest reference."
            }
        }
        if ([string]$manifest.statutoryAssurance.status -ne "release-blocked" -or
            $manifest.statutoryAssurance.noDirectSubmission -ne $true -or
            $manifest.statutoryAssurance.qualifiedAccountantRequired -ne $true) {
            Add-Failure $failures "release.json must retain the release-blocked/no-direct/professional-review boundary."
        }

        $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in @($manifest.files)) {
            $relative = [string]$entry.path
            if ([string]::IsNullOrWhiteSpace($relative) -or $relative.Contains("..") -or -not $seen.Add($relative)) {
                Add-Failure $failures "release.json contains an unsafe or duplicate file path."
                continue
            }
            $fullPath = [IO.Path]::GetFullPath((Join-Path $temporaryRoot $relative))
            if (-not $fullPath.StartsWith([IO.Path]::GetFullPath($temporaryRoot) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
                -not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
                Add-Failure $failures "Manifested release file is missing or outside the archive: $relative"
                continue
            }
            $item = Get-Item -LiteralPath $fullPath -Force
            if ([long]$entry.byteSize -ne $item.Length -or
                [string]$entry.sha256 -cne (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash.ToLowerInvariant()) {
                Add-Failure $failures "Manifested release file hash/size mismatch: $relative"
            }
        }
        $expectedArchiveFiles = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($manifestedPath in $seen) { [void]$expectedArchiveFiles.Add($manifestedPath) }
        [void]$expectedArchiveFiles.Add("release.json")
        $actualArchiveFiles = @($archiveEntryNames | Where-Object { -not $_.EndsWith('/', [StringComparison]::Ordinal) })
        $actualArchiveFileSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($archiveFile in $actualArchiveFiles) { [void]$actualArchiveFileSet.Add($archiveFile) }
        $unexpectedArchiveFiles = @($actualArchiveFiles | Where-Object { -not $expectedArchiveFiles.Contains($_) })
        $missingArchiveFiles = @($expectedArchiveFiles | Where-Object { -not $actualArchiveFileSet.Contains($_) })
        if ($unexpectedArchiveFiles.Count -gt 0 -or $missingArchiveFiles.Count -gt 0) {
            Add-Failure $failures (
                "Release archive file inventory mismatch. Unexpected: [{0}]. Missing: [{1}]." -f
                    (($unexpectedArchiveFiles | Select-Object -First 5) -join ", "),
                    (($missingArchiveFiles | Select-Object -First 5) -join ", "))
        }
        foreach ($required in @(
            "FilingBridge.cmd",
            "filingbridge",
            "compose.private.yml",
            ".env.private.example",
            "scripts/private-server.ps1",
            "scripts/PrivateServer/PrivateServer.psm1",
            "scripts/smoke-production.ps1",
            "scripts/verify-linux-private-host.sh",
            "Docs/deployment/private-server.md",
            "Docs/deployment/private-server-linux.md",
            "Docs/deployment/GOOGLE_CLOUD_PRIVATE_SERVER.md",
            "Docs/deployment/LOCAL_WINDOWS_READINESS.md",
            "Docs/deployment/LINUX_CLOUD_READINESS.md",
            "LICENSE",
            "NOTICE")) {
            if (-not $seen.Contains($required)) {
                Add-Failure $failures "release.json does not retain required file: $required"
            }
        }
    }
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}

$result = [ordered]@{
    schemaVersion = "filingbridge.private-server.release-verification/v1"
    status = if ($failures.Count -eq 0) { "passed" } else { "failed" }
    archiveFileName = [IO.Path]::GetFileName($resolvedArchive)
    archiveSha256 = (Get-FileHash -LiteralPath $resolvedArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    failures = @($failures)
}
$result | ConvertTo-Json -Depth 4
if ($failures.Count -gt 0) { exit 1 }
