param(
    [string]$EvidenceDirectory = (Join-Path $PSScriptRoot "..\Docs\release-evidence"),
    [string]$ReportPath = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Add-Failure {
    param(
        [System.Collections.Generic.List[string]]$Failures,
        [string]$Message
    )

    $Failures.Add($Message) | Out-Null
}

function Read-EvidenceFile {
    param(
        [string]$Path,
        [System.Collections.Generic.List[string]]$Failures
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        Add-Failure $Failures "Missing evidence file: $Path"
        return ""
    }

    return Get-Content -LiteralPath $Path -Raw
}

function Assert-ContainsText {
    param(
        [string]$Content,
        [string]$Needle,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    if ($Content.IndexOf($Needle, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
        Add-Failure $Failures "$Context is missing required text: $Needle"
    }
}

function Assert-FilledField {
    param(
        [string]$Content,
        [string]$FieldName,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    $value = Get-FieldValue $Content $FieldName
    if ($null -eq $value) {
        Add-Failure $Failures "$Context field '$FieldName' must be filled."
        return
    }

    if ($value.Length -eq 0 -or $value -match "^(tbd|todo|n/a|none|pending)$") {
        Add-Failure $Failures "$Context field '$FieldName' contains a placeholder value."
    }
}

function Get-FieldValue {
    param(
        [string]$Content,
        [string]$FieldName
    )

    $escaped = [regex]::Escape($FieldName)
    $pattern = "(?im)^\s*-?\s*$escaped\s*:\s*(\S.*?)\s*$"
    $match = [regex]::Match($Content, $pattern)

    if (-not $match.Success) {
        return $null
    }

    return $match.Groups[1].Value.Trim()
}

function Assert-FieldMatchesPattern {
    param(
        [string]$Content,
        [string]$FieldName,
        [string]$Pattern,
        [string]$Description,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    $value = Get-FieldValue $Content $FieldName
    if ($null -eq $value -or $value.Length -eq 0) {
        return
    }

    if ($value -notmatch $Pattern) {
        Add-Failure $Failures "$Context field '$FieldName' must be $Description."
    }
}

function Assert-CommitShaField {
    param(
        [string]$Content,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    Assert-FieldMatchesPattern $Content "Commit SHA" "^[0-9a-fA-F]{40}$" "a 40-character hexadecimal Git commit SHA" $Context $Failures
}

function Assert-GitHubActionsRunUrlField {
    param(
        [string]$Content,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    Assert-FieldMatchesPattern $Content "GitHub Actions run URL" "^https://github\.com/[^/\s]+/[^/\s]+/actions/runs/[0-9]+(?:[/?#].*)?$" "a GitHub Actions run URL" $Context $Failures
}

function Assert-UtcTimestampField {
    param(
        [string]$Content,
        [string]$FieldName,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    $value = Get-FieldValue $Content $FieldName
    if ($null -eq $value -or $value.Length -eq 0) {
        return
    }

    if ($value -notmatch "(?:Z|\+00:00)$") {
        Add-Failure $Failures "$Context field '$FieldName' must be an explicit UTC timestamp ending in Z or +00:00."
        return
    }

    $parsed = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse($value, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal, [ref]$parsed)) {
        Add-Failure $Failures "$Context field '$FieldName' must be a valid UTC timestamp."
    }
}

function Assert-Sha256Field {
    param(
        [string]$Content,
        [string]$FieldName,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    Assert-FieldMatchesPattern $Content $FieldName "^[0-9a-fA-F]{64}$" "a 64-character hexadecimal SHA-256 digest" $Context $Failures
}

function Assert-PositiveIntegerField {
    param(
        [string]$Content,
        [string]$FieldName,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    $value = Get-FieldValue $Content $FieldName
    if ($null -eq $value -or $value.Length -eq 0) {
        return
    }

    $number = 0
    if (-not [int]::TryParse($value, [ref]$number) -or $number -le 0) {
        Add-Failure $Failures "$Context field '$FieldName' must be a positive integer."
    }
}

function Assert-NoUncheckedBoxes {
    param(
        [string]$Content,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    $unchecked = [regex]::Matches($Content, "(?im)^\s*-\s*\[\s\]\s+(.+?)\s*$")
    foreach ($item in $unchecked) {
        $label = $item.Groups[1].Value.Trim()
        if ($label.StartsWith("Rejected;", [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        Add-Failure $Failures "$Context has unchecked release evidence item: $label"
    }
}

function Assert-CheckedDecision {
    param(
        [string]$Content,
        [string]$DecisionText,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    $escaped = [regex]::Escape($DecisionText)
    if ($Content -notmatch "(?im)^\s*-\s*\[[xX]\]\s+$escaped\s*$") {
        Add-Failure $Failures "$Context must check decision '$DecisionText'."
    }
}

function Assert-UncheckedDecision {
    param(
        [string]$Content,
        [string]$DecisionText,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    $escaped = [regex]::Escape($DecisionText)
    if ($Content -match "(?im)^\s*-\s*\[[xX]\]\s+$escaped\s*$") {
        Add-Failure $Failures "$Context must not check rejection decision '$DecisionText' for accepted release evidence."
    }
}

function Assert-CompletedTableRows {
    param(
        [string]$Content,
        [string[]]$RowLabels,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    $lines = $Content -split "\r?\n"
    foreach ($label in $RowLabels) {
        $escaped = [regex]::Escape($label)
        $row = $lines | Where-Object { $_ -match "^\|\s*$escaped\s*\|" } | Select-Object -First 1
        if (-not $row) {
            Add-Failure $Failures "$Context table is missing row '$label'."
            continue
        }

        if ($row -match "\|\s*\|") {
            Add-Failure $Failures "$Context table row '$label' has empty cells."
        }
    }
}

function Assert-CompletedTableColumnMatches {
    param(
        [string]$Content,
        [string[]]$RowLabels,
        [int]$ColumnIndex,
        [string]$ColumnLabel,
        [string]$Pattern,
        [string]$Description,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    $lines = $Content -split "\r?\n"
    foreach ($label in $RowLabels) {
        $escaped = [regex]::Escape($label)
        $row = $lines | Where-Object { $_ -match "^\|\s*$escaped\s*\|" } | Select-Object -First 1
        if (-not $row) {
            continue
        }

        $cells = @($row.Trim() -split "\|").Where({ $_.Trim().Length -gt 0 })
        if ($cells.Count -le $ColumnIndex) {
            Add-Failure $Failures "$Context table row '$label' is missing column '$ColumnLabel'."
            continue
        }

        $value = $cells[$ColumnIndex].Trim()
        if ($value -notmatch $Pattern) {
            Add-Failure $Failures "$Context table row '$label' column '$ColumnLabel' must be $Description."
        }
    }
}

function Assert-ReleaseIdentityFields {
    param(
        [string]$Content,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    Assert-CommitShaField $Content $Context $Failures
    Assert-GitHubActionsRunUrlField $Content $Context $Failures
}

function Get-ReleaseEvidenceIdentity {
    param(
        [string]$Content,
        [string]$EvidenceName
    )

    $commitSha = Get-FieldValue $Content "Commit SHA"
    $runUrl = Get-FieldValue $Content "GitHub Actions run URL"
    if ([string]::IsNullOrWhiteSpace($commitSha) -or [string]::IsNullOrWhiteSpace($runUrl)) {
        return $null
    }

    return [pscustomobject]@{
        evidenceName = $EvidenceName
        commitSha = $commitSha
        githubActionsRunUrl = $runUrl
    }
}

function Get-FileSha256 {
    param(
        [string]$Path
    )

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function New-EvidenceFileManifestItem {
    param(
        [string]$EvidenceName,
        [string]$Path,
        [string]$Content
    )

    $fileName = Split-Path -Leaf $Path
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return [ordered]@{
            evidenceName = $EvidenceName
            fileName = $fileName
            path = $Path
            present = $false
            byteSize = 0
            sha256 = ""
            hasReleaseIdentity = $false
        }
    }

    $fileInfo = Get-Item -LiteralPath $Path
    [ordered]@{
        evidenceName = $EvidenceName
        fileName = $fileName
        path = $Path
        present = $true
        byteSize = $fileInfo.Length
        sha256 = Get-FileSha256 $Path
        hasReleaseIdentity = $null -ne (Get-ReleaseEvidenceIdentity $Content $EvidenceName)
    }
}

function Assert-ConsistentReleaseIdentity {
    param(
        [object[]]$Identities,
        [System.Collections.Generic.List[string]]$Failures
    )

    if (@($Identities).Count -le 1) {
        return
    }

    $first = @($Identities)[0]
    foreach ($identity in @($Identities)) {
        if (-not [string]::Equals([string]$identity.commitSha, [string]$first.commitSha, [StringComparison]::OrdinalIgnoreCase)) {
            Add-Failure $Failures "Release evidence identity mismatch: $($identity.evidenceName) Commit SHA must match $($first.evidenceName)."
        }

        if (-not [string]::Equals([string]$identity.githubActionsRunUrl, [string]$first.githubActionsRunUrl, [StringComparison]::OrdinalIgnoreCase)) {
            Add-Failure $Failures "Release evidence identity mismatch: $($identity.evidenceName) GitHub Actions run URL must match $($first.evidenceName)."
        }
    }
}

$canonicalGoldenCorpusScenarioCodes = @(
    "micro-ltd",
    "small-abridged-ltd",
    "dac-small",
    "clg-charity",
    "medium-audit-required"
)

$requiredRouteCodes = @(
    "dashboard",
    "company-detail",
    "period-workspace",
    "filing-review",
    "financial-statements",
    "production-readiness",
    "workbench-preview"
)

$requiredManualHandoffScenarioCodes = @(
    "medium-audit-required"
)

$requiredManualHandoffPathCodes = @(
    "plc-public-company",
    "unlimited-company",
    "excluded-regulated-entity",
    "group-consolidation",
    "audit-required-without-auditor-report",
    "complex-corporation-tax",
    "direct-cro-ros-submission"
)

$requiredReleaseArtifactNames = @(
    "dependency-audit-release",
    "production-safety-config",
    "monitoring-error-routing-smoke",
    "structured-json-log-sample",
    "postgres-backup-restore-drill",
    "production-readiness-report",
    "production-readiness-verification-report.json",
    "visual-smoke-screenshots"
)

$requiredSourceLawSourceIds = @(
    "cro-financial-statements-requirements",
    "cro-guarantee-company",
    "cro-unlimited-company",
    "cro-group-company",
    "cro-medium-company",
    "cro-auditors-report",
    "revenue-ixbrl-overview",
    "revenue-ixbrl-contents",
    "revenue-accepted-taxonomies",
    "frc-frs-102",
    "frc-frs-105",
    "charities-regulator-annual-report"
)

function Test-VisualEvidence {
    param(
        [string]$Content,
        [System.Collections.Generic.List[string]]$Failures
    )

    $context = "Visual QA evidence"
    foreach ($text in @("visual-smoke-screenshots", "visual-smoke-manifest.json", "visual-smoke-evidence-report.json", "accountant-workbench-evidence-report.json", "Reviewer signature")) {
        Assert-ContainsText $Content $text $context $Failures
    }

    foreach ($field in @("Commit SHA", "GitHub Actions run URL", "Visual smoke manifest file", "Visual smoke evidence report file", "Accountant workbench evidence report file", "Reviewer name", "Reviewer role", "Review date/time UTC", "Reviewer signature")) {
        Assert-FilledField $Content $field $context $Failures
    }

    Assert-ReleaseIdentityFields $Content $context $Failures
    Assert-UtcTimestampField $Content "Review date/time UTC" $context $Failures
    Assert-NoUncheckedBoxes $Content $context $Failures
    Assert-CheckedDecision $Content "Accepted for this release candidate." $context $Failures
    Assert-UncheckedDecision $Content "Rejected; defects listed below must be fixed and re-reviewed." $context $Failures
    Assert-CompletedTableRows $Content $requiredRouteCodes $context $Failures
}

function Test-AccountantEvidence {
    param(
        [string]$Content,
        [System.Collections.Generic.List[string]]$Failures
    )

    $context = "Qualified-accountant acceptance evidence"
    foreach ($text in $requiredReleaseArtifactNames + @(
        "Direct CRO submission remains unsupported",
        "Direct ROS submission remains unsupported",
        "Qualified accountant signature"
    )) {
        Assert-ContainsText $Content $text $context $Failures
    }

    foreach ($field in @("Commit SHA", "GitHub Actions run URL", "Production readiness report timestamp", "Accountant name", "Qualification / professional body", "Firm / reviewer capacity", "Review date/time UTC", "Qualified accountant signature")) {
        Assert-FilledField $Content $field $context $Failures
    }

    Assert-ReleaseIdentityFields $Content $context $Failures
    Assert-UtcTimestampField $Content "Production readiness report timestamp" $context $Failures
    Assert-UtcTimestampField $Content "Review date/time UTC" $context $Failures
    Assert-NoUncheckedBoxes $Content $context $Failures
    Assert-CheckedDecision $Content "Accepted for real filing preparation subject to external CRO/ROS processes." $context $Failures
    Assert-UncheckedDecision $Content "Rejected; issues below must be remediated and re-reviewed." $context $Failures
    Assert-CompletedTableRows $Content $canonicalGoldenCorpusScenarioCodes $context $Failures
    Assert-CompletedTableRows $Content $requiredRouteCodes $context $Failures

    foreach ($staleScenarioCode in @("micro-ltd-standard", "small-ltd-abridged")) {
        if ($Content.IndexOf($staleScenarioCode, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            Add-Failure $Failures "$context contains stale non-canonical scenario code '$staleScenarioCode'."
        }
    }
}

function Test-ManualHandoffEvidence {
    param(
        [string]$Content,
        [System.Collections.Generic.List[string]]$Failures
    )

    $context = "Manual handoff acceptance evidence"
    foreach ($text in @(
        "Manual Handoff Acceptance",
        "medium-audit-required",
        "Signed auditor report evidence",
        "Manual handoff note",
        "Filing readiness profile snapshot",
        "Unsupported automated filing paths remain blocked",
        "Accepted as manual handoff evidence for this release candidate.",
        "Reviewer signature"
    )) {
        Assert-ContainsText $Content $text $context $Failures
    }

    foreach ($field in @(
        "Commit SHA",
        "GitHub Actions run URL",
        "Production readiness report timestamp",
        "Reviewer name",
        "Reviewer role",
        "Firm / reviewer capacity",
        "Review date/time UTC",
        "Reviewer signature"
    )) {
        Assert-FilledField $Content $field $context $Failures
    }

    Assert-ReleaseIdentityFields $Content $context $Failures
    Assert-UtcTimestampField $Content "Production readiness report timestamp" $context $Failures
    Assert-UtcTimestampField $Content "Review date/time UTC" $context $Failures
    Assert-NoUncheckedBoxes $Content $context $Failures
    Assert-CheckedDecision $Content "Accepted as manual handoff evidence for this release candidate." $context $Failures
    Assert-UncheckedDecision $Content "Rejected; manual handoff issues below must be remediated and re-reviewed." $context $Failures
    Assert-CompletedTableRows $Content $requiredManualHandoffScenarioCodes $context $Failures
    Assert-CompletedTableRows $Content $requiredManualHandoffPathCodes $context $Failures
}

function Test-ExternalRosIxbrlEvidence {
    param(
        [string]$Content,
        [System.Collections.Generic.List[string]]$Failures
    )

    $context = "External ROS/iXBRL validation evidence"
    foreach ($text in @(
        "External ROS/iXBRL validation",
        "Internal XML checks are not Revenue acceptance evidence",
        "Generated iXBRL SHA-256",
        "Taxonomy package",
        "Accepted as external ROS/iXBRL validation evidence for this release candidate.",
        "Reviewer signature"
    )) {
        Assert-ContainsText $Content $text $context $Failures
    }

    foreach ($field in @(
        "Commit SHA",
        "GitHub Actions run URL",
        "Production readiness report timestamp",
        "Reviewer name",
        "Reviewer role",
        "Review date/time UTC",
        "External validation provider",
        "Validation environment",
        "Validation run/reference id",
        "Validation report file or URL",
        "Generated iXBRL artifact name",
        "Generated iXBRL SHA-256",
        "Taxonomy package",
        "Company/period reference",
        "Reviewer signature"
    )) {
        Assert-FilledField $Content $field $context $Failures
    }

    Assert-ReleaseIdentityFields $Content $context $Failures
    Assert-UtcTimestampField $Content "Production readiness report timestamp" $context $Failures
    Assert-UtcTimestampField $Content "Review date/time UTC" $context $Failures
    Assert-Sha256Field $Content "Generated iXBRL SHA-256" $context $Failures
    Assert-NoUncheckedBoxes $Content $context $Failures
    Assert-CheckedDecision $Content "Accepted as external ROS/iXBRL validation evidence for this release candidate." $context $Failures
    Assert-UncheckedDecision $Content "Rejected; validation issues below must be remediated and re-reviewed." $context $Failures
    Assert-CompletedTableRows $Content $canonicalGoldenCorpusScenarioCodes $context $Failures
    Assert-CompletedTableColumnMatches $Content $canonicalGoldenCorpusScenarioCodes 2 "Artifact hash" "^[0-9a-fA-F]{64}$" "a 64-character hexadecimal SHA-256 digest" $context $Failures
}

function Test-MonitoringEvidence {
    param(
        [string]$Content,
        [System.Collections.Generic.List[string]]$Failures
    )

    $context = "Monitoring provider confirmation evidence"
    foreach ($text in @(
        "monitoring-error-routing-smoke",
        "structured-json-log-sample",
        "monitoring-error-routing-report.json",
        "structured-log-report.json",
        "/api/system/monitoring/error-smoke",
        "No PII or client filing data",
        "Operator signature"
    )) {
        Assert-ContainsText $Content $text $context $Failures
    }

    foreach ($field in @("Commit SHA", "GitHub Actions run URL", "Operator name", "Operator role", "Confirmation date/time UTC", "Provider", "Event id", "Correlation id", "Base URL", "Checked at UTC", "Structured log file", "JSON log line count", "Matched monitoring smoke line", "Provider event URL or reference", "Operator signature")) {
        Assert-FilledField $Content $field $context $Failures
    }

    Assert-ReleaseIdentityFields $Content $context $Failures
    Assert-UtcTimestampField $Content "Confirmation date/time UTC" $context $Failures
    Assert-UtcTimestampField $Content "Checked at UTC" $context $Failures
    Assert-PositiveIntegerField $Content "JSON log line count" $context $Failures
    Assert-FieldMatchesPattern $Content "Matched monitoring smoke line" "^yes$" "yes" $context $Failures
    Assert-NoUncheckedBoxes $Content $context $Failures
}

function Test-SourceLawEvidence {
    param(
        [string]$Content,
        [System.Collections.Generic.List[string]]$Failures
    )

    $context = "Source-law review evidence"
    foreach ($text in @(
        "source-law-snapshot-fingerprint",
        "source-law-traceability-index",
        "source-law-maintenance-protocol",
        "source-law-review-ledger",
        "source-law-change-review-note",
        "qualified-accountant-source-law-signoff",
        "Accepted as source-law review evidence for this release candidate.",
        "Reviewer signature",
        "Qualified accountant source-law sign-off"
    )) {
        Assert-ContainsText $Content $text $context $Failures
    }

    foreach ($field in @(
        "Commit SHA",
        "GitHub Actions run URL",
        "Production readiness report timestamp",
        "Source-law snapshot fingerprint",
        "Source-law snapshot content hash",
        "Reviewer name",
        "Reviewer role",
        "Review date/time UTC",
        "Qualified accountant name",
        "Qualification / professional body",
        "Reviewer signature",
        "Qualified accountant source-law sign-off"
    )) {
        Assert-FilledField $Content $field $context $Failures
    }

    Assert-ReleaseIdentityFields $Content $context $Failures
    Assert-UtcTimestampField $Content "Production readiness report timestamp" $context $Failures
    Assert-UtcTimestampField $Content "Review date/time UTC" $context $Failures
    Assert-Sha256Field $Content "Source-law snapshot content hash" $context $Failures
    Assert-NoUncheckedBoxes $Content $context $Failures
    Assert-CheckedDecision $Content "Accepted as source-law review evidence for this release candidate." $context $Failures
    Assert-UncheckedDecision $Content "Rejected; source-law issues below must be remediated and re-reviewed." $context $Failures
    Assert-CompletedTableRows $Content $requiredSourceLawSourceIds $context $Failures
}

$failures = [System.Collections.Generic.List[string]]::new()
$resolvedDirectory = Resolve-Path -LiteralPath $EvidenceDirectory -ErrorAction Stop

$visualPath = Join-Path $resolvedDirectory "visual-qa-signoff-template.md"
$sourceLawPath = Join-Path $resolvedDirectory "source-law-review-template.md"
$externalRosIxbrlPath = Join-Path $resolvedDirectory "external-ros-ixbrl-validation-template.md"
$accountantPath = Join-Path $resolvedDirectory "qualified-accountant-acceptance-template.md"
$manualHandoffPath = Join-Path $resolvedDirectory "manual-handoff-acceptance-template.md"
$monitoringPath = Join-Path $resolvedDirectory "monitoring-provider-confirmation-template.md"

$visual = [string](Read-EvidenceFile $visualPath $failures)
$sourceLaw = [string](Read-EvidenceFile $sourceLawPath $failures)
$externalRosIxbrl = [string](Read-EvidenceFile $externalRosIxbrlPath $failures)
$accountant = [string](Read-EvidenceFile $accountantPath $failures)
$manualHandoff = [string](Read-EvidenceFile $manualHandoffPath $failures)
$monitoring = [string](Read-EvidenceFile $monitoringPath $failures)

$evidenceFiles = @(
    New-EvidenceFileManifestItem "visualQa" $visualPath $visual
    New-EvidenceFileManifestItem "sourceLawReview" $sourceLawPath $sourceLaw
    New-EvidenceFileManifestItem "externalRosIxbrlValidation" $externalRosIxbrlPath $externalRosIxbrl
    New-EvidenceFileManifestItem "qualifiedAccountantAcceptance" $accountantPath $accountant
    New-EvidenceFileManifestItem "manualHandoffAcceptance" $manualHandoffPath $manualHandoff
    New-EvidenceFileManifestItem "monitoringProviderConfirmation" $monitoringPath $monitoring
)

if ($visual.Trim().Length -gt 0) {
    Test-VisualEvidence $visual $failures
}

if ($sourceLaw.Trim().Length -gt 0) {
    Test-SourceLawEvidence $sourceLaw $failures
}

if ($accountant.Trim().Length -gt 0) {
    Test-AccountantEvidence $accountant $failures
}

if ($manualHandoff.Trim().Length -gt 0) {
    Test-ManualHandoffEvidence $manualHandoff $failures
}

if ($externalRosIxbrl.Trim().Length -gt 0) {
    Test-ExternalRosIxbrlEvidence $externalRosIxbrl $failures
}

if ($monitoring.Trim().Length -gt 0) {
    Test-MonitoringEvidence $monitoring $failures
}

$releaseEvidenceIdentities = @(
    Get-ReleaseEvidenceIdentity $visual "visualQa"
    Get-ReleaseEvidenceIdentity $sourceLaw "sourceLawReview"
    Get-ReleaseEvidenceIdentity $externalRosIxbrl "externalRosIxbrlValidation"
    Get-ReleaseEvidenceIdentity $accountant "qualifiedAccountantAcceptance"
    Get-ReleaseEvidenceIdentity $manualHandoff "manualHandoffAcceptance"
    Get-ReleaseEvidenceIdentity $monitoring "monitoringProviderConfirmation"
) | Where-Object { $null -ne $_ }
Assert-ConsistentReleaseIdentity $releaseEvidenceIdentities $failures

$releaseCandidateCommitSha = ""
$releaseCandidateRunUrl = ""
if (@($releaseEvidenceIdentities).Count -gt 0) {
    $releaseCandidateCommitSha = [string]@($releaseEvidenceIdentities)[0].commitSha
    $releaseCandidateRunUrl = [string]@($releaseEvidenceIdentities)[0].githubActionsRunUrl
}

$releaseIdentityConsistent = $true
if (@($releaseEvidenceIdentities | Select-Object -ExpandProperty commitSha -Unique).Count -gt 1 -or
    @($releaseEvidenceIdentities | Select-Object -ExpandProperty githubActionsRunUrl -Unique).Count -gt 1) {
    $releaseIdentityConsistent = $false
}

$report = [ordered]@{
    status = if ($failures.Count -eq 0) { "passed" } else { "failed" }
    checkedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    evidenceDirectory = $resolvedDirectory.Path
    releaseCandidate = [ordered]@{
        commitSha = $releaseCandidateCommitSha
        githubActionsRunUrl = $releaseCandidateRunUrl
        identityConsistent = $releaseIdentityConsistent
        evidenceIdentityCount = @($releaseEvidenceIdentities).Count
    }
    evidenceIdentities = @($releaseEvidenceIdentities)
    evidenceFiles = $evidenceFiles
    files = [ordered]@{
        visualQa = $visualPath
        sourceLawReview = $sourceLawPath
        externalRosIxbrlValidation = $externalRosIxbrlPath
        qualifiedAccountantAcceptance = $accountantPath
        manualHandoffAcceptance = $manualHandoffPath
        monitoringProviderConfirmation = $monitoringPath
    }
    requiredCoverage = [ordered]@{
        goldenCorpusScenarioCodes = $canonicalGoldenCorpusScenarioCodes
        externalRosIxbrlScenarioCodes = $canonicalGoldenCorpusScenarioCodes
        sourceLawSourceIds = $requiredSourceLawSourceIds
        routeCodes = $requiredRouteCodes
        manualHandoffScenarioCodes = $requiredManualHandoffScenarioCodes
        manualHandoffPathCodes = $requiredManualHandoffPathCodes
        releaseArtifactNames = $requiredReleaseArtifactNames
        releaseEvidenceTemplateFiles = @($evidenceFiles | ForEach-Object { $_.fileName })
    }
    failureCount = $failures.Count
    failures = $failures.ToArray()
}

if ($ReportPath.Trim().Length -gt 0) {
    $reportDirectory = Split-Path -Parent $ReportPath
    if ($reportDirectory -and -not (Test-Path -LiteralPath $reportDirectory)) {
        New-Item -ItemType Directory -Path $reportDirectory | Out-Null
    }

    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
}

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) {
        Write-Error $failure -ErrorAction Continue
    }
    throw "Release evidence verification failed with $($failures.Count) issue(s)."
}

Write-Host "Release evidence verification passed for $($resolvedDirectory.Path)."
