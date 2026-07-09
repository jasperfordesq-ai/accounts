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
    $pattern = "(?im)^[`t ]*-?[`t ]*$escaped[`t ]*:[`t ]*(.*?)[`t ]*$"
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

function Assert-MinimumIntegerField {
    param(
        [string]$Content,
        [string]$FieldName,
        [int]$MinimumValue,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    $value = Get-FieldValue $Content $FieldName
    if ($null -eq $value -or $value.Length -eq 0) {
        return
    }

    $number = 0
    if (-not [int]::TryParse($value, [ref]$number) -or $number -lt $MinimumValue) {
        Add-Failure $Failures "$Context field '$FieldName' must be an integer greater than or equal to $MinimumValue."
    }
}

function Assert-MinimumDecimalField {
    param(
        [string]$Content,
        [string]$FieldName,
        [decimal]$MinimumValue,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    $value = Get-FieldValue $Content $FieldName
    if ($null -eq $value -or $value.Length -eq 0) {
        return
    }

    $number = [decimal]0
    if (-not [decimal]::TryParse($value, [Globalization.NumberStyles]::Number, [Globalization.CultureInfo]::InvariantCulture, [ref]$number) -or $number -lt $MinimumValue) {
        Add-Failure $Failures "$Context field '$FieldName' must be a number greater than or equal to $MinimumValue."
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

function Assert-CompletedTableColumnMatchesRouteReference {
    param(
        [string]$Content,
        [string[]]$RowLabels,
        [int]$ColumnIndex,
        [string]$ColumnLabel,
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
        $expected = "accountant-workbench-evidence-report.json#routeAcceptance.$label"
        if (-not [string]::Equals($value, $expected, [StringComparison]::OrdinalIgnoreCase)) {
            Add-Failure $Failures "$Context table row '$label' column '$ColumnLabel' must be $expected."
        }
    }
}

function Assert-CompletedTableColumnContainsRowLabel {
    param(
        [string]$Content,
        [string[]]$RowLabels,
        [int]$ColumnIndex,
        [string]$ColumnLabel,
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
        if ($value.IndexOf($label, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
            Add-Failure $Failures "$Context table row '$label' column '$ColumnLabel' must include row code '$label'."
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

    $identityList = @($Identities | Where-Object { $null -ne $_ })
    if ($identityList.Length -le 1) {
        return
    }

    $first = $identityList[0]
    foreach ($identity in $identityList) {
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
    foreach ($text in @(
        "visual-smoke-screenshots",
        "visual-smoke-manifest.json",
        "visual-smoke-evidence-report.json",
        "accountant-workbench-evidence-report.json",
        "screenshot nonblank pixel diversity evidence",
        "theme-contrast",
        "pngIdatByteSize",
        "pixelSampleCount",
        "sampledDistinctColorCount",
        "luminanceRange",
        "themeContrastResult.minimumContrastRatio",
        "Reviewer signature"
    )) {
        Assert-ContainsText $Content $text $context $Failures
    }

    foreach ($field in @(
        "Commit SHA",
        "GitHub Actions run URL",
        "Visual smoke manifest file",
        "Visual smoke evidence report file",
        "Accountant workbench evidence report file",
        "Minimum PNG IDAT byte size",
        "Minimum screenshot pixel sample count",
        "Minimum sampled distinct color count",
        "Minimum screenshot luminance range",
        "Minimum automated contrast ratio",
        "Reviewer name",
        "Reviewer role",
        "Review date/time UTC",
        "Reviewer signature"
    )) {
        Assert-FilledField $Content $field $context $Failures
    }

    Assert-ReleaseIdentityFields $Content $context $Failures
    Assert-UtcTimestampField $Content "Review date/time UTC" $context $Failures
    Assert-PositiveIntegerField $Content "Minimum PNG IDAT byte size" $context $Failures
    Assert-PositiveIntegerField $Content "Minimum screenshot pixel sample count" $context $Failures
    Assert-MinimumIntegerField $Content "Minimum sampled distinct color count" 4 $context $Failures
    Assert-MinimumIntegerField $Content "Minimum screenshot luminance range" 10 $context $Failures
    Assert-MinimumDecimalField $Content "Minimum automated contrast ratio" ([decimal]3.0) $context $Failures
    Assert-NoUncheckedBoxes $Content $context $Failures
    Assert-CheckedDecision $Content "Accepted for this release candidate." $context $Failures
    Assert-UncheckedDecision $Content "Rejected; defects listed below must be fixed and re-reviewed." $context $Failures
    Assert-CompletedTableRows $Content $requiredRouteCodes $context $Failures
    Assert-CompletedTableColumnMatches $Content $requiredRouteCodes 1 "Desktop light" "^(pass|accepted)$" "pass or accepted" $context $Failures
    Assert-CompletedTableColumnMatches $Content $requiredRouteCodes 2 "Desktop dark" "^(pass|accepted)$" "pass or accepted" $context $Failures
    Assert-CompletedTableColumnMatches $Content $requiredRouteCodes 3 "Mobile light" "^(pass|accepted)$" "pass or accepted" $context $Failures
    Assert-CompletedTableColumnMatches $Content $requiredRouteCodes 4 "Mobile dark" "^(pass|accepted)$" "pass or accepted" $context $Failures
    Assert-CompletedTableColumnMatches $Content $requiredRouteCodes 5 "Notes" "^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+" "a real retained visual evidence note or reference" $context $Failures
    Assert-CompletedTableColumnContainsRowLabel $Content $requiredRouteCodes 5 "Notes" $context $Failures
}

function Test-AccountantEvidence {
    param(
        [string]$Content,
        [System.Collections.Generic.List[string]]$Failures
    )

    $context = "Qualified-accountant acceptance evidence"
    foreach ($text in $requiredReleaseArtifactNames + @(
        "accountant-workbench-evidence-report.json",
        "Scenario evidence reference",
        "Workbench evidence reference",
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
    Assert-CompletedTableColumnMatches $Content $canonicalGoldenCorpusScenarioCodes 1 "Outputs" "^accepted$" "accepted" $context $Failures
    Assert-CompletedTableColumnMatches $Content $canonicalGoldenCorpusScenarioCodes 2 "Gates" "^accepted$" "accepted" $context $Failures
    Assert-CompletedTableColumnMatches $Content $canonicalGoldenCorpusScenarioCodes 3 "Source-law evidence" "^accepted$" "accepted" $context $Failures
    Assert-CompletedTableColumnMatches $Content $canonicalGoldenCorpusScenarioCodes 4 "Wording" "^accepted$" "accepted" $context $Failures
    Assert-CompletedTableColumnMatches $Content $canonicalGoldenCorpusScenarioCodes 5 "Workbench journey" "^accepted$" "accepted" $context $Failures
    Assert-CompletedTableColumnMatches $Content $canonicalGoldenCorpusScenarioCodes 6 "Decision" "^accepted$" "accepted for this release candidate" $context $Failures
    Assert-CompletedTableColumnMatches $Content $canonicalGoldenCorpusScenarioCodes 7 "Scenario evidence reference" "^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+" "a real retained scenario walkthrough evidence reference" $context $Failures
    Assert-CompletedTableColumnContainsRowLabel $Content $canonicalGoldenCorpusScenarioCodes 7 "Scenario evidence reference" $context $Failures
    Assert-CompletedTableColumnMatches $Content $requiredRouteCodes 1 "Decision question answered" "^(yes|accepted)$" "yes or accepted" $context $Failures
    Assert-CompletedTableColumnMatches $Content $requiredRouteCodes 2 "Evidence accepted" "^accepted$" "accepted" $context $Failures
    Assert-CompletedTableColumnMatches $Content $requiredRouteCodes 3 "Workbench evidence reference" "^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+" "a real retained workbench evidence reference" $context $Failures
    Assert-CompletedTableColumnMatchesRouteReference $Content $requiredRouteCodes 3 "Workbench evidence reference" $context $Failures

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
    Assert-CompletedTableColumnMatches $Content $requiredManualHandoffScenarioCodes 1 "Auditor evidence" "^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+" "a real signed auditor evidence reference" $context $Failures
    Assert-CompletedTableColumnMatches $Content $requiredManualHandoffScenarioCodes 2 "Manual handoff note" "^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+" "a real manual handoff note reference" $context $Failures
    Assert-CompletedTableColumnMatches $Content $requiredManualHandoffScenarioCodes 3 "Filing readiness snapshot" "^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+" "a real filing readiness snapshot reference" $context $Failures
    Assert-CompletedTableColumnMatches $Content $requiredManualHandoffScenarioCodes 4 "Decision" "^(accepted|accepted\b.*)$" "accepted for this release candidate" $context $Failures
    Assert-CompletedTableColumnMatches $Content $requiredManualHandoffPathCodes 1 "Release evidence reference" "^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+" "a real unsupported-path evidence reference" $context $Failures
    Assert-CompletedTableColumnMatches $Content $requiredManualHandoffPathCodes 2 "Reviewer decision" "^(accepted|accepted\b.*)$" "accepted" $context $Failures
    Assert-CompletedTableColumnContainsRowLabel $Content $requiredManualHandoffScenarioCodes 1 "Auditor evidence" $context $Failures
    Assert-CompletedTableColumnContainsRowLabel $Content $requiredManualHandoffScenarioCodes 2 "Manual handoff note" $context $Failures
    Assert-CompletedTableColumnContainsRowLabel $Content $requiredManualHandoffScenarioCodes 3 "Filing readiness snapshot" $context $Failures
    Assert-CompletedTableColumnContainsRowLabel $Content $requiredManualHandoffPathCodes 1 "Release evidence reference" $context $Failures
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
    Assert-CompletedTableColumnMatches $Content $canonicalGoldenCorpusScenarioCodes 1 "External reference" "^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+" "a real external validation reference" $context $Failures
    Assert-CompletedTableColumnMatches $Content $canonicalGoldenCorpusScenarioCodes 2 "Artifact hash" "^[0-9a-fA-F]{64}$" "a 64-character hexadecimal SHA-256 digest" $context $Failures
    Assert-CompletedTableColumnMatches $Content $canonicalGoldenCorpusScenarioCodes 3 "Taxonomy package" "^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+" "a real retained taxonomy package reference" $context $Failures
    Assert-CompletedTableColumnMatches $Content $canonicalGoldenCorpusScenarioCodes 4 "Warnings/errors" "^(none|accepted\b.*|remediated\b.*)$" "none, accepted, or remediated warnings/errors" $context $Failures
    Assert-CompletedTableColumnMatches $Content $canonicalGoldenCorpusScenarioCodes 5 "Decision" "^(accepted|accepted\b.*)$" "accepted for this release candidate" $context $Failures
    Assert-CompletedTableColumnContainsRowLabel $Content $canonicalGoldenCorpusScenarioCodes 1 "External reference" $context $Failures
    Assert-CompletedTableColumnContainsRowLabel $Content $canonicalGoldenCorpusScenarioCodes 3 "Taxonomy package" $context $Failures
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
        "Accepted as monitoring-provider confirmation evidence for this release candidate.",
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
    Assert-FieldMatchesPattern $Content "Provider" "^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+" "a real monitoring provider name" $context $Failures
    Assert-FieldMatchesPattern $Content "Event id" "^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+" "a real provider event id" $context $Failures
    Assert-FieldMatchesPattern $Content "Correlation id" "^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+" "a real monitoring correlation id" $context $Failures
    Assert-FieldMatchesPattern $Content "Base URL" "^https://.+" "an HTTPS provider base URL" $context $Failures
    Assert-FieldMatchesPattern $Content "Provider event URL or reference" "^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+" "a real provider event URL or evidence reference" $context $Failures
    Assert-NoUncheckedBoxes $Content $context $Failures
    Assert-CheckedDecision $Content "Accepted as monitoring-provider confirmation evidence for this release candidate." $context $Failures
    Assert-UncheckedDecision $Content "Rejected; monitoring-provider confirmation issues below must be remediated and re-reviewed." $context $Failures
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
    Assert-CompletedTableColumnMatches $Content $requiredSourceLawSourceIds 1 "URL reachable" "^yes$" "yes" $context $Failures
    Assert-CompletedTableColumnMatches $Content $requiredSourceLawSourceIds 2 "Effective date checked" "^([0-9]{4}-[0-9]{2}-[0-9]{2}|not dated)$" "YYYY-MM-DD or not dated" $context $Failures
    Assert-CompletedTableColumnMatches $Content $requiredSourceLawSourceIds 3 "Guidance wording compared" "^yes$" "yes" $context $Failures
    Assert-CompletedTableColumnMatches $Content $requiredSourceLawSourceIds 4 "Platform impact" "^(no change|reflected\b.*|blocking\b.*)$" "no change, reflected, or blocking" $context $Failures
    Assert-CompletedTableColumnMatches $Content $requiredSourceLawSourceIds 5 "Decision" "^accepted$" "accepted for this release candidate" $context $Failures
    Assert-CompletedTableColumnMatches $Content $requiredSourceLawSourceIds 6 "Notes" "^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+" "a real retained per-source note or evidence reference" $context $Failures
    Assert-CompletedTableColumnContainsRowLabel $Content $requiredSourceLawSourceIds 6 "Notes" $context $Failures
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
$releaseEvidenceIdentities = @($releaseEvidenceIdentities)
Assert-ConsistentReleaseIdentity $releaseEvidenceIdentities $failures

$releaseCandidateCommitSha = ""
$releaseCandidateRunUrl = ""
if ($releaseEvidenceIdentities.Length -gt 0) {
    $releaseCandidateCommitSha = [string]$releaseEvidenceIdentities[0].commitSha
    $releaseCandidateRunUrl = [string]$releaseEvidenceIdentities[0].githubActionsRunUrl
}

$releaseIdentityConsistent = $true
$uniqueCommitShas = @($releaseEvidenceIdentities | Select-Object -ExpandProperty commitSha -Unique)
$uniqueRunUrls = @($releaseEvidenceIdentities | Select-Object -ExpandProperty githubActionsRunUrl -Unique)
if ($uniqueCommitShas.Length -gt 1 -or $uniqueRunUrls.Length -gt 1) {
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
        evidenceIdentityCount = $releaseEvidenceIdentities.Length
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
