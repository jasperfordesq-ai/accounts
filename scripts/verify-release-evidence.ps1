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

function Read-JsonEvidenceFile {
    param(
        [string]$Path,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        Add-Failure $Failures "Missing evidence file: $Path"
        return $null
    }

    try {
        return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    } catch {
        Add-Failure $Failures "$Context must be valid JSON: $($_.Exception.Message)"
        return $null
    }
}

function Get-JsonPropertyValue {
    param(
        $Object,
        [string]$Name
    )

    if ($null -eq $Object) {
        return $null
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
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

function Assert-FieldEquals {
    param(
        [string]$Content,
        [string]$FieldName,
        [string]$ExpectedValue,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    $value = Get-FieldValue $Content $FieldName
    if ($null -eq $value) {
        return
    }

    if (-not [string]::Equals($value, $ExpectedValue, [StringComparison]::Ordinal)) {
        Add-Failure $Failures "$Context field '$FieldName' must be $ExpectedValue."
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

function Assert-CompletedTableColumnMatchesVisualRouteReference {
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
        $expected = "visual-smoke-evidence-report.json#routeAcceptance.$label"
        if (-not [string]::Equals($value, $expected, [StringComparison]::OrdinalIgnoreCase)) {
            Add-Failure $Failures "$Context table row '$label' column '$ColumnLabel' must be $expected."
        }
    }
}

function Assert-CompletedTableColumnMatchesRouteWalkthroughNote {
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
        $expected = "qualified-accountant-route-walkthrough#$label"
        if (-not [string]::Equals($value, $expected, [StringComparison]::OrdinalIgnoreCase)) {
            Add-Failure $Failures "$Context table row '$label' column '$ColumnLabel' must be $expected."
        }
    }
}

function Assert-CompletedTableColumnMatchesSourceLawNote {
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
        $expected = "source-law-review-ledger#$label"
        if (-not [string]::Equals($value, $expected, [StringComparison]::OrdinalIgnoreCase)) {
            Add-Failure $Failures "$Context table row '$label' column '$ColumnLabel' must be $expected."
        }
    }
}

function Assert-CompletedTableColumnMatchesScenarioWalkthroughReference {
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
        $expected = "qualified-accountant-walkthrough-ledger#$label"
        if (-not [string]::Equals($value, $expected, [StringComparison]::OrdinalIgnoreCase)) {
            Add-Failure $Failures "$Context table row '$label' column '$ColumnLabel' must be $expected."
        }
    }
}

function Assert-CompletedTableColumnMatchesExternalValidationReference {
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
        $expected = "external-ros-validation-ledger#$label"
        if (-not [string]::Equals($value, $expected, [StringComparison]::OrdinalIgnoreCase)) {
            Add-Failure $Failures "$Context table row '$label' column '$ColumnLabel' must be $expected."
        }
    }
}

function Assert-CompletedTableColumnMatchesTaxonomyPackageReference {
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
        $expected = "revenue-taxonomy-package-ledger#$label"
        if (-not [string]::Equals($value, $expected, [StringComparison]::OrdinalIgnoreCase)) {
            Add-Failure $Failures "$Context table row '$label' column '$ColumnLabel' must be $expected."
        }
    }
}

function Assert-CompletedTableColumnMatchesEvidenceAnchor {
    param(
        [string]$Content,
        [string[]]$RowLabels,
        [int]$ColumnIndex,
        [string]$ColumnLabel,
        [string]$AnchorPrefix,
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
        $expected = "$AnchorPrefix#$label"
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

function Assert-JsonStringEquals {
    param(
        $Object,
        [string]$PropertyName,
        [string]$ExpectedValue,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    $value = [string](Get-JsonPropertyValue $Object $PropertyName)
    if (-not [string]::Equals($value, $ExpectedValue, [StringComparison]::OrdinalIgnoreCase)) {
        Add-Failure $Failures "$Context $PropertyName must be $ExpectedValue."
    }
}

function Assert-JsonArrayContains {
    param(
        $Values,
        [string]$ExpectedValue,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    $matches = @($Values | Where-Object {
        [string]::Equals([string]$_, $ExpectedValue, [StringComparison]::OrdinalIgnoreCase)
    })

    if ($matches.Count -eq 0) {
        Add-Failure $Failures "$Context must include $ExpectedValue."
    }
}

function Assert-MachineEvidenceEntries {
    param(
        $Entries,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures,
        $ExpectedEntries = $null,
        $ReferenceEntries = $null,
        [string]$ReferenceContext = ""
    )

    $entryList = @($Entries | Where-Object { $null -ne $_ })
    if ($entryList.Count -ne $requiredMachineEvidenceFiles.Count) {
        Add-Failure $Failures "$Context retainedMachineEvidence must contain exactly $($requiredMachineEvidenceFiles.Count) entries."
    }

    $expectedList = @($ExpectedEntries | Where-Object { $null -ne $_ })
    if ($expectedList.Count -eq 0) {
        $expectedList = @($requiredMachineEvidenceFiles | ForEach-Object {
            [pscustomobject]@{ FileName = [string]$_; SourceArtifactName = ""; SourceArtifactFile = "" }
        })
    }

    $referenceEntryList = @($ReferenceEntries | Where-Object { $null -ne $_ })

    foreach ($expected in $expectedList) {
        $fileName = [string](Get-JsonPropertyValue $expected "FileName")
        $entry = $entryList | Where-Object {
            [string]::Equals([string](Get-JsonPropertyValue $_ "fileName"), $fileName, [StringComparison]::OrdinalIgnoreCase)
        } | Select-Object -First 1

        if ($null -eq $entry) {
            Add-Failure $Failures "$Context retainedMachineEvidence must include $fileName."
            continue
        }

        $byteSize = [int](Get-JsonPropertyValue $entry "byteSize")
        if ($byteSize -le 0) {
            Add-Failure $Failures "$Context retainedMachineEvidence.$fileName.byteSize must be a positive integer."
        }

        $sha256 = [string](Get-JsonPropertyValue $entry "sha256")
        if ($sha256 -notmatch "^[0-9a-f]{64}$") {
            Add-Failure $Failures "$Context retainedMachineEvidence.$fileName.sha256 must be a lowercase 64-character SHA-256 digest."
        }

        foreach ($propertyName in @("sourceArtifactName", "sourceArtifactFile")) {
            $expectedPropertyName = $propertyName.Substring(0, 1).ToUpperInvariant() + $propertyName.Substring(1)
            $expectedValue = [string](Get-JsonPropertyValue $expected $expectedPropertyName)
            if (-not [string]::IsNullOrWhiteSpace($expectedValue)) {
                $actualValue = [string](Get-JsonPropertyValue $entry $propertyName)
                if (-not [string]::Equals($actualValue, $expectedValue, [StringComparison]::OrdinalIgnoreCase)) {
                    Add-Failure $Failures "$Context retainedMachineEvidence.$fileName.$propertyName must be $expectedValue."
                }
            }
        }

        if ($referenceEntryList.Count -gt 0) {
            $referenceEntry = $referenceEntryList | Where-Object {
                [string]::Equals([string](Get-JsonPropertyValue $_ "fileName"), $fileName, [StringComparison]::OrdinalIgnoreCase)
            } | Select-Object -First 1

            if ($null -eq $referenceEntry) {
                Add-Failure $Failures "$Context retainedMachineEvidence.$fileName must match $ReferenceContext."
                continue
            }

            foreach ($propertyName in @("sourceArtifactName", "sourceArtifactFile", "byteSize", "sha256")) {
                $actualValue = [string](Get-JsonPropertyValue $entry $propertyName)
                $referenceValue = [string](Get-JsonPropertyValue $referenceEntry $propertyName)
                if (-not [string]::Equals($actualValue, $referenceValue, [StringComparison]::OrdinalIgnoreCase)) {
                    Add-Failure $Failures "$Context retainedMachineEvidence.$fileName.$propertyName must match $ReferenceContext."
                }
            }
        }
    }
}

function Assert-WorkspaceVerificationInventory {
    param(
        $WorkspaceVerificationReport,
        [System.Collections.Generic.List[string]]$Failures
    )

    $expectedWorkspaceFiles = @(
        $requiredReleaseEvidenceTemplateFiles +
        $requiredMachineEvidenceFiles +
        @(
            "release-evidence-workspace-manifest.json",
            "release-evidence-machine-summary.json",
            "release-evidence-reviewer-index.md",
            "release-evidence-reviewer-completion.json",
            "release-evidence-reviewer-assignments.json",
            "release-evidence-reviewer-blockers.md",
            "release-evidence-report.json",
            "release-evidence-verifier-output.txt"
        )
    )

    $requiredWorkspaceFiles = @((Get-JsonPropertyValue $WorkspaceVerificationReport "requiredWorkspaceFiles") | ForEach-Object { [string]$_ })
    if ($requiredWorkspaceFiles.Count -ne $expectedWorkspaceFiles.Count) {
        Add-Failure $Failures "Release evidence workspace verification report requiredWorkspaceFiles must contain exactly $($expectedWorkspaceFiles.Count) entries."
    }

    foreach ($expectedFile in $expectedWorkspaceFiles) {
        Assert-JsonArrayContains $requiredWorkspaceFiles $expectedFile "Release evidence workspace verification report requiredWorkspaceFiles" $Failures
    }

    $workspaceFiles = @((Get-JsonPropertyValue $WorkspaceVerificationReport "workspaceFiles"))
    $workspaceFileNames = @($workspaceFiles | ForEach-Object { [string](Get-JsonPropertyValue $_ "fileName") })
    if ($workspaceFileNames.Count -ne $expectedWorkspaceFiles.Count) {
        Add-Failure $Failures "Release evidence workspace verification report workspaceFiles must contain exactly $($expectedWorkspaceFiles.Count) entries."
    }

    foreach ($expectedFile in $expectedWorkspaceFiles) {
        Assert-JsonArrayContains $workspaceFileNames $expectedFile "Release evidence workspace verification report workspaceFiles" $Failures

        $entry = $workspaceFiles | Where-Object {
            [string]::Equals([string](Get-JsonPropertyValue $_ "fileName"), $expectedFile, [StringComparison]::OrdinalIgnoreCase)
        } | Select-Object -First 1

        if ($null -eq $entry) {
            continue
        }

        $byteSize = [int](Get-JsonPropertyValue $entry "byteSize")
        if ($byteSize -le 0) {
            Add-Failure $Failures "Release evidence workspace verification report workspaceFiles.$expectedFile.byteSize must be a positive integer."
        }

        $sha256 = [string](Get-JsonPropertyValue $entry "sha256")
        if ($sha256 -notmatch "^[0-9a-f]{64}$") {
            Add-Failure $Failures "Release evidence workspace verification report workspaceFiles.$expectedFile.sha256 must be a lowercase 64-character SHA-256 digest."
        }
    }
}

function Assert-PreparedHumanTemplateControls {
    param(
        $WorkspaceVerificationReport,
        [System.Collections.Generic.List[string]]$Failures
    )

    $controls = @((Get-JsonPropertyValue $WorkspaceVerificationReport "preparedHumanTemplateControls"))
    if ($controls.Count -ne $requiredPreparedHumanTemplateControls.Count) {
        Add-Failure $Failures "Release evidence workspace verification report preparedHumanTemplateControls must contain exactly $($requiredPreparedHumanTemplateControls.Count) entries."
    }

    foreach ($expected in $requiredPreparedHumanTemplateControls) {
        $expectedFile = [string]$expected.FileName
        $entry = $controls | Where-Object {
            [string]::Equals([string](Get-JsonPropertyValue $_ "fileName"), $expectedFile, [StringComparison]::OrdinalIgnoreCase)
        } | Select-Object -First 1

        if ($null -eq $entry) {
            Add-Failure $Failures "Release evidence workspace verification report preparedHumanTemplateControls must include $expectedFile."
            continue
        }

        Assert-JsonStringEquals $entry "context" ([string]$expected.Context) "Release evidence workspace verification report preparedHumanTemplateControls.$expectedFile" $Failures
        Assert-JsonStringEquals $entry "checkboxPolicy" "unchecked-before-named-human-signoff" "Release evidence workspace verification report preparedHumanTemplateControls.$expectedFile" $Failures

        $actualFields = @((Get-JsonPropertyValue $entry "blankFields") | ForEach-Object { [string]$_ })
        if ($actualFields.Count -ne $expected.Fields.Count) {
            Add-Failure $Failures "Release evidence workspace verification report preparedHumanTemplateControls.$expectedFile.blankFields must contain exactly $($expected.Fields.Count) entries."
        }

        foreach ($field in $expected.Fields) {
            Assert-JsonArrayContains $actualFields $field "Release evidence workspace verification report preparedHumanTemplateControls.$expectedFile.blankFields" $Failures
        }
    }
}

function Assert-PendingHumanEvidenceBlockers {
    param(
        $WorkspaceVerificationReport,
        [System.Collections.Generic.List[string]]$Failures
    )

    $blockers = @((Get-JsonPropertyValue $WorkspaceVerificationReport "pendingHumanEvidenceBlockers"))
    if ($blockers.Count -ne $requiredPendingHumanEvidenceBlockers.Count) {
        Add-Failure $Failures "Release evidence workspace verification report pendingHumanEvidenceBlockers must contain exactly $($requiredPendingHumanEvidenceBlockers.Count) entries."
    }

    foreach ($expected in $requiredPendingHumanEvidenceBlockers) {
        $expectedEvidenceName = [string]$expected.EvidenceName
        $entry = $blockers | Where-Object {
            [string]::Equals([string](Get-JsonPropertyValue $_ "evidenceName"), $expectedEvidenceName, [StringComparison]::OrdinalIgnoreCase)
        } | Select-Object -First 1

        if ($null -eq $entry) {
            Add-Failure $Failures "Release evidence workspace verification report pendingHumanEvidenceBlockers must include $expectedEvidenceName."
            continue
        }

        Assert-JsonStringEquals $entry "templateFile" ([string]$expected.TemplateFile) "Release evidence workspace verification report pendingHumanEvidenceBlockers.$expectedEvidenceName" $Failures
        Assert-JsonStringEquals $entry "requiredReviewerRole" ([string]$expected.RequiredReviewerRole) "Release evidence workspace verification report pendingHumanEvidenceBlockers.$expectedEvidenceName" $Failures
        Assert-JsonStringEquals $entry "signOffGate" ([string]$expected.SignOffGate) "Release evidence workspace verification report pendingHumanEvidenceBlockers.$expectedEvidenceName" $Failures
        Assert-JsonStringEquals $entry "status" "incomplete" "Release evidence workspace verification report pendingHumanEvidenceBlockers.$expectedEvidenceName" $Failures

        if ([int](Get-JsonPropertyValue $entry "blockingFailureCount") -le 0) {
            Add-Failure $Failures "Release evidence workspace verification report pendingHumanEvidenceBlockers.$expectedEvidenceName.blockingFailureCount must be greater than zero."
        }

        if ([string]::IsNullOrWhiteSpace([string](Get-JsonPropertyValue $entry "firstBlockingFailure"))) {
            Add-Failure $Failures "Release evidence workspace verification report pendingHumanEvidenceBlockers.$expectedEvidenceName.firstBlockingFailure must be present."
        }
    }
}

function Assert-ReviewerAssignmentInventory {
    param(
        $WorkspaceVerificationReport,
        [System.Collections.Generic.List[string]]$Failures
    )

    $assignments = @((Get-JsonPropertyValue $WorkspaceVerificationReport "reviewerAssignmentInventory"))
    if ($assignments.Count -ne $requiredPendingHumanEvidenceBlockers.Count) {
        Add-Failure $Failures "Release evidence workspace verification report reviewerAssignmentInventory must contain exactly $($requiredPendingHumanEvidenceBlockers.Count) entries."
    }

    foreach ($expected in $requiredPendingHumanEvidenceBlockers) {
        $expectedEvidenceName = [string]$expected.EvidenceName
        $entry = $assignments | Where-Object {
            [string]::Equals([string](Get-JsonPropertyValue $_ "evidenceName"), $expectedEvidenceName, [StringComparison]::OrdinalIgnoreCase)
        } | Select-Object -First 1

        if ($null -eq $entry) {
            Add-Failure $Failures "Release evidence workspace verification report reviewerAssignmentInventory must include $expectedEvidenceName."
            continue
        }

        Assert-JsonStringEquals $entry "templateFile" ([string]$expected.TemplateFile) "Release evidence workspace verification report reviewerAssignmentInventory.$expectedEvidenceName" $Failures
        Assert-JsonStringEquals $entry "requiredReviewerRole" ([string]$expected.RequiredReviewerRole) "Release evidence workspace verification report reviewerAssignmentInventory.$expectedEvidenceName" $Failures
        Assert-JsonStringEquals $entry "signOffGate" ([string]$expected.SignOffGate) "Release evidence workspace verification report reviewerAssignmentInventory.$expectedEvidenceName" $Failures
        Assert-JsonStringEquals $entry "assignmentStatus" "unassigned" "Release evidence workspace verification report reviewerAssignmentInventory.$expectedEvidenceName" $Failures
        Assert-JsonStringEquals $entry "escalationOwnerRole" "Release operator" "Release evidence workspace verification report reviewerAssignmentInventory.$expectedEvidenceName" $Failures

        foreach ($blankField in @("assignedReviewerName", "assignedReviewerEmail", "dueAtUtc")) {
            if (-not [string]::IsNullOrWhiteSpace([string](Get-JsonPropertyValue $entry $blankField))) {
                Add-Failure $Failures "Release evidence workspace verification report reviewerAssignmentInventory.$expectedEvidenceName.$blankField must be blank before named reviewer routing."
            }
        }

        if ([string]::IsNullOrWhiteSpace([string](Get-JsonPropertyValue $entry "humanAction"))) {
            Add-Failure $Failures "Release evidence workspace verification report reviewerAssignmentInventory.$expectedEvidenceName.humanAction must be present."
        }

        $reviewerPickupFiles = @((Get-JsonPropertyValue $entry "reviewerPickupFiles") | ForEach-Object { [string]$_ })
        foreach ($requiredPickupFile in @($expected.RequiredPickupFiles)) {
            Assert-JsonArrayContains $reviewerPickupFiles $requiredPickupFile "Release evidence workspace verification report reviewerAssignmentInventory.$expectedEvidenceName.reviewerPickupFiles" $Failures
        }
    }
}

function Test-ReleaseWorkspaceControlEvidence {
    param(
        $WorkspaceManifest,
        $MachineEvidenceSummary,
        $WorkspaceVerificationReport,
        [string]$ReleaseCandidateCommitSha,
        [string]$ReleaseCandidateRunUrl,
        [System.Collections.Generic.List[string]]$Failures
    )

    if ($null -ne $WorkspaceManifest) {
        Assert-JsonStringEquals $WorkspaceManifest "machineEvidenceSummaryFile" "release-evidence-machine-summary.json" "Release evidence workspace manifest" $Failures
        foreach ($templateFile in $requiredReleaseEvidenceTemplateFiles) {
            Assert-JsonArrayContains (Get-JsonPropertyValue $WorkspaceManifest "preparedTemplates") $templateFile "Release evidence workspace manifest preparedTemplates" $Failures
        }

        Assert-MachineEvidenceEntries (Get-JsonPropertyValue $WorkspaceManifest "retainedMachineEvidence") "Release evidence workspace manifest" $Failures $requiredMachineEvidenceProvenance

        if (-not [string]::IsNullOrWhiteSpace($ReleaseCandidateCommitSha)) {
            Assert-JsonStringEquals $WorkspaceManifest "commitSha" $ReleaseCandidateCommitSha "Release evidence workspace manifest" $Failures
        }

        if (-not [string]::IsNullOrWhiteSpace($ReleaseCandidateRunUrl)) {
            Assert-JsonStringEquals $WorkspaceManifest "githubActionsRunUrl" $ReleaseCandidateRunUrl "Release evidence workspace manifest" $Failures
        }
    }

    if ($null -ne $MachineEvidenceSummary) {
        $summaryCandidate = Get-JsonPropertyValue $MachineEvidenceSummary "releaseCandidate"
        if (-not [string]::IsNullOrWhiteSpace($ReleaseCandidateCommitSha)) {
            Assert-JsonStringEquals $summaryCandidate "commitSha" $ReleaseCandidateCommitSha "Release evidence machine summary releaseCandidate" $Failures
        }

        if (-not [string]::IsNullOrWhiteSpace($ReleaseCandidateRunUrl)) {
            Assert-JsonStringEquals $summaryCandidate "githubActionsRunUrl" $ReleaseCandidateRunUrl "Release evidence machine summary releaseCandidate" $Failures
        }

        Assert-MachineEvidenceEntries (Get-JsonPropertyValue $MachineEvidenceSummary "retainedMachineEvidence") "Release evidence machine summary" $Failures $requiredMachineEvidenceProvenance (Get-JsonPropertyValue $WorkspaceManifest "retainedMachineEvidence") "Release evidence workspace manifest"

        Assert-ContainsText ([string](Get-JsonPropertyValue $MachineEvidenceSummary "completionPolicy")) "machine evidence only" "Release evidence machine summary completionPolicy" $Failures
        Assert-ContainsText ([string](Get-JsonPropertyValue $MachineEvidenceSummary "completionPolicy")) "named reviewers" "Release evidence machine summary completionPolicy" $Failures

        $reviewerQueue = @((Get-JsonPropertyValue $MachineEvidenceSummary "reviewerQueue"))
        if ($reviewerQueue.Count -ne $requiredReviewerQueue.Count) {
            Add-Failure $Failures "Release evidence machine summary reviewerQueue must contain exactly $($requiredReviewerQueue.Count) entries."
        }

        $summaryProductionReadiness = Get-JsonPropertyValue $MachineEvidenceSummary "productionReadiness"
        Assert-JsonStringEquals $summaryProductionReadiness "verificationStatus" "passed" "Release evidence machine summary productionReadiness" $Failures
        $summaryVerificationFailureCount = Get-JsonPropertyValue $summaryProductionReadiness "verificationFailureCount"
        if ($null -eq $summaryVerificationFailureCount -or [int]$summaryVerificationFailureCount -ne 0) {
            Add-Failure $Failures "Release evidence machine summary productionReadiness.verificationFailureCount must be 0."
        }

        foreach ($closeoutStepCode in $requiredHumanReleaseEvidenceCloseoutStepCodes) {
            Assert-JsonArrayContains (Get-JsonPropertyValue $summaryProductionReadiness "humanReleaseEvidenceCloseoutStepCodes") $closeoutStepCode "Release evidence machine summary productionReadiness.humanReleaseEvidenceCloseoutStepCodes" $Failures
        }

        $summaryReviewerPickupFilesByEvidence = Get-JsonPropertyValue $summaryProductionReadiness "humanReleaseEvidenceReviewerPickupFiles"
        foreach ($expected in $requiredPendingHumanEvidenceBlockers) {
            $queueEntry = $reviewerQueue | Where-Object {
                [string]::Equals([string](Get-JsonPropertyValue $_ "EvidenceName"), [string]$expected.EvidenceName, [StringComparison]::OrdinalIgnoreCase)
            } | Select-Object -First 1

            if ($null -eq $queueEntry) {
                Add-Failure $Failures "Release evidence machine summary reviewerQueue must include $($expected.EvidenceName)."
            } else {
                Assert-JsonStringEquals $queueEntry "TemplateFile" ([string]$expected.TemplateFile) "Release evidence machine summary reviewerQueue.$($expected.EvidenceName)" $Failures
                Assert-JsonStringEquals $queueEntry "ReviewerRole" ([string]$expected.RequiredReviewerRole) "Release evidence machine summary reviewerQueue.$($expected.EvidenceName)" $Failures
                Assert-JsonStringEquals $queueEntry "SignOffGate" ([string]$expected.SignOffGate) "Release evidence machine summary reviewerQueue.$($expected.EvidenceName)" $Failures
                Assert-ContainsText ([string](Get-JsonPropertyValue $queueEntry "HumanAction")) "record" "Release evidence machine summary reviewerQueue.$($expected.EvidenceName).HumanAction" $Failures
            }

            $summaryReviewerPickupFiles = @((Get-JsonPropertyValue $summaryReviewerPickupFilesByEvidence $expected.EvidenceName) | ForEach-Object { [string]$_ })
            foreach ($requiredPickupFile in @($expected.RequiredPickupFiles)) {
                Assert-JsonArrayContains $summaryReviewerPickupFiles $requiredPickupFile "Release evidence machine summary productionReadiness.humanReleaseEvidenceReviewerPickupFiles.$($expected.EvidenceName)" $Failures
                if ($null -ne $queueEntry) {
                    Assert-JsonArrayContains (Get-JsonPropertyValue $queueEntry "RequiredPickupFiles") $requiredPickupFile "Release evidence machine summary reviewerQueue.$($expected.EvidenceName).RequiredPickupFiles" $Failures
                }
            }
        }

        $monitoringEvidence = Get-JsonPropertyValue $MachineEvidenceSummary "monitoringEvidence"
        if ([int](Get-JsonPropertyValue $monitoringEvidence "jsonLogLineCount") -le 0) {
            Add-Failure $Failures "Release evidence machine summary monitoringEvidence.jsonLogLineCount must be greater than zero."
        }

        if ([bool](Get-JsonPropertyValue $monitoringEvidence "matchedMonitoringSmokeLine") -ne $true) {
            Add-Failure $Failures "Release evidence machine summary monitoringEvidence.matchedMonitoringSmokeLine must be true."
        }
    }

    if ($null -ne $WorkspaceVerificationReport) {
        Assert-JsonStringEquals $WorkspaceVerificationReport "status" "passed" "Release evidence workspace verification report" $Failures

        if ([int](Get-JsonPropertyValue $WorkspaceVerificationReport "failureCount") -ne 0) {
            Add-Failure $Failures "Release evidence workspace verification report failureCount must be 0."
        }

        $workspaceVerificationCandidate = Get-JsonPropertyValue $WorkspaceVerificationReport "releaseCandidate"
        if (-not [string]::IsNullOrWhiteSpace($ReleaseCandidateCommitSha)) {
            Assert-JsonStringEquals $workspaceVerificationCandidate "commitSha" $ReleaseCandidateCommitSha "Release evidence workspace verification report releaseCandidate" $Failures
        }

        if (-not [string]::IsNullOrWhiteSpace($ReleaseCandidateRunUrl)) {
            Assert-JsonStringEquals $workspaceVerificationCandidate "githubActionsRunUrl" $ReleaseCandidateRunUrl "Release evidence workspace verification report releaseCandidate" $Failures
        }

        Assert-WorkspaceVerificationInventory $WorkspaceVerificationReport $Failures
        Assert-PreparedHumanTemplateControls $WorkspaceVerificationReport $Failures
        Assert-PendingHumanEvidenceBlockers $WorkspaceVerificationReport $Failures
        Assert-ReviewerAssignmentInventory $WorkspaceVerificationReport $Failures
    }
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

function New-HumanEvidenceCompletionItem {
    param(
        [string]$EvidenceName,
        [string]$TemplateFile,
        [string]$RequiredReviewerRole,
        [string]$SignOffGate,
        [string]$Content,
        [System.Collections.Generic.List[string]]$EvidenceFailures
    )

    $present = Test-Path -LiteralPath $TemplateFile -PathType Leaf
    $hasContent = -not [string]::IsNullOrWhiteSpace($Content)
    $identity = Get-ReleaseEvidenceIdentity $Content $EvidenceName
    $status = "incomplete"

    if (-not $present) {
        $status = "missing-template"
    } elseif (-not $hasContent) {
        $status = "not-started"
    } elseif ($EvidenceFailures.Count -eq 0) {
        $status = "accepted"
    }

    [ordered]@{
        evidenceName = $EvidenceName
        templateFile = Split-Path -Leaf $TemplateFile
        requiredReviewerRole = $RequiredReviewerRole
        signOffGate = $SignOffGate
        present = $present
        hasReleaseIdentity = $null -ne $identity
        status = $status
        blockingFailureCount = $EvidenceFailures.Count
        blockingFailures = $EvidenceFailures.ToArray()
    }
}

function New-ProductionScorecardCompletion {
    param(
        [object[]]$HumanEvidenceCompletion,
        [System.Collections.Generic.List[string]]$Failures
    )

    $completionItems = @($HumanEvidenceCompletion)
    $acceptedItems = @($completionItems | Where-Object {
        [string]::Equals([string]$_["status"], "accepted", [StringComparison]::Ordinal)
    })
    $acceptedEvidenceNames = @($acceptedItems | ForEach-Object { [string]$_["evidenceName"] })
    $remainingEvidenceNames = @($completionItems | Where-Object {
        -not [string]::Equals([string]$_["status"], "accepted", [StringComparison]::Ordinal)
    } | ForEach-Object { [string]$_["evidenceName"] })

    $visualQaAccepted = $acceptedEvidenceNames -contains "visualQa"
    $allHumanEvidenceAccepted = $acceptedItems.Count -eq $requiredPendingHumanEvidenceBlockers.Count
    $releaseScorecardComplete = $allHumanEvidenceAccepted -and $Failures.Count -eq 0
    $frontendRemainingHumanEvidence = @()
    if (-not $visualQaAccepted) {
        $frontendRemainingHumanEvidence = @("visualQa")
    }

    $architectureScore = if ($releaseScorecardComplete) { 100 } else { 99 }
    $frontendScore = if ($visualQaAccepted) { 200 } else { 199 }
    $categories = @(
        [ordered]@{
            code = "architecture-documentation"
            currentScore = $architectureScore
            targetScore = 100
            completionGate = "all-human-release-evidence-accepted"
            requiredHumanEvidence = @($requiredPendingHumanEvidenceBlockers | ForEach-Object { [string]$_.EvidenceName })
            remainingHumanEvidence = $remainingEvidenceNames
        }
        [ordered]@{
            code = "backend-statutory-accounting-engine"
            currentScore = 250
            targetScore = 250
            completionGate = "machine-and-template-verification-complete"
            requiredHumanEvidence = @()
            remainingHumanEvidence = @()
        }
        [ordered]@{
            code = "frontend-accountant-workbench"
            currentScore = $frontendScore
            targetScore = 200
            completionGate = "visual-qa-human-evidence-accepted"
            requiredHumanEvidence = @("visualQa")
            remainingHumanEvidence = $frontendRemainingHumanEvidence
        }
        [ordered]@{
            code = "security-auth-tenant-platform-guardrails"
            currentScore = 150
            targetScore = 150
            completionGate = "machine-and-template-verification-complete"
            requiredHumanEvidence = @()
            remainingHumanEvidence = @()
        }
    )

    [ordered]@{
        status = if ($releaseScorecardComplete) { "complete" } else { "blocked" }
        currentScore = @($categories | ForEach-Object { [int]$_["currentScore"] } | Measure-Object -Sum).Sum
        targetScore = 700
        acceptedHumanEvidenceCount = $acceptedItems.Count
        requiredHumanEvidenceCount = $requiredPendingHumanEvidenceBlockers.Count
        acceptedHumanEvidence = $acceptedEvidenceNames
        remainingHumanEvidence = $remainingEvidenceNames
        completionPolicy = "Score reaches 700/700 only when all six named human release-evidence templates are accepted and this verifier has zero blocking failures."
        categories = $categories
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

$requiredReleaseEvidenceTemplateFiles = @(
    "visual-qa-signoff-template.md",
    "source-law-review-template.md",
    "external-ros-ixbrl-validation-template.md",
    "qualified-accountant-acceptance-template.md",
    "manual-handoff-acceptance-template.md",
    "monitoring-provider-confirmation-template.md"
)

$requiredPendingHumanEvidenceBlockers = @(
    [pscustomobject]@{ EvidenceName = "visualQa"; TemplateFile = "visual-qa-signoff-template.md"; RequiredReviewerRole = "Named visual QA reviewer"; SignOffGate = "visual-qa-screenshot-review"; RequiredPickupFiles = @("visual-qa-signoff-template.md", "visual-smoke-manifest.json", "visual-smoke-evidence-report.json", "accountant-workbench-evidence-report.json", "release-evidence-reviewer-blockers.md") },
    [pscustomobject]@{ EvidenceName = "sourceLawReview"; TemplateFile = "source-law-review-template.md"; RequiredReviewerRole = "Named source-law reviewer plus qualified accountant"; SignOffGate = "source-law-change-review"; RequiredPickupFiles = @("source-law-review-template.md", "production-readiness-report.json", "production-readiness-verification-report.json", "release-evidence-reviewer-blockers.md") },
    [pscustomobject]@{ EvidenceName = "externalRosIxbrlValidation"; TemplateFile = "external-ros-ixbrl-validation-template.md"; RequiredReviewerRole = "External ROS/iXBRL validation reviewer"; SignOffGate = "external-ros-validation-evidence"; RequiredPickupFiles = @("external-ros-ixbrl-validation-template.md", "production-readiness-report.json", "release-evidence-reviewer-blockers.md") },
    [pscustomobject]@{ EvidenceName = "qualifiedAccountantAcceptance"; TemplateFile = "qualified-accountant-acceptance-template.md"; RequiredReviewerRole = "Named qualified accountant"; SignOffGate = "qualified-accountant-final-signoff"; RequiredPickupFiles = @("qualified-accountant-acceptance-template.md", "production-readiness-report.json", "accountant-workbench-evidence-report.json", "release-evidence-reviewer-blockers.md") },
    [pscustomobject]@{ EvidenceName = "manualHandoffAcceptance"; TemplateFile = "manual-handoff-acceptance-template.md"; RequiredReviewerRole = "Named manual handoff reviewer"; SignOffGate = "manual-accountant-acceptance"; RequiredPickupFiles = @("manual-handoff-acceptance-template.md", "production-readiness-report.json", "release-evidence-reviewer-blockers.md") },
    [pscustomobject]@{ EvidenceName = "monitoringProviderConfirmation"; TemplateFile = "monitoring-provider-confirmation-template.md"; RequiredReviewerRole = "Named release operator"; SignOffGate = "production-monitoring"; RequiredPickupFiles = @("monitoring-provider-confirmation-template.md", "monitoring-error-routing-report.json", "structured-log-report.json", "release-evidence-reviewer-blockers.md") }
)

$requiredPreparedHumanTemplateControls = @(
    [pscustomobject]@{
        FileName = "visual-qa-signoff-template.md"
        Context = "Prepared visual QA template"
        Fields = @("Reviewer name", "Reviewer role", "Review date/time UTC", "Reviewer signature")
    },
    [pscustomobject]@{
        FileName = "source-law-review-template.md"
        Context = "Prepared source-law template"
        Fields = @(
            "Reviewer name",
            "Reviewer role",
            "Review date/time UTC",
            "Qualified accountant name",
            "Qualification / professional body",
            "Reviewer signature",
            "Qualified accountant source-law sign-off"
        )
    },
    [pscustomobject]@{
        FileName = "external-ros-ixbrl-validation-template.md"
        Context = "Prepared external ROS/iXBRL template"
        Fields = @(
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
        )
    },
    [pscustomobject]@{
        FileName = "qualified-accountant-acceptance-template.md"
        Context = "Prepared qualified-accountant template"
        Fields = @(
            "Accountant name",
            "Qualification / professional body",
            "Firm / reviewer capacity",
            "Review date/time UTC",
            "Qualified accountant signature"
        )
    },
    [pscustomobject]@{
        FileName = "manual-handoff-acceptance-template.md"
        Context = "Prepared manual handoff template"
        Fields = @(
            "Reviewer name",
            "Reviewer role",
            "Firm / reviewer capacity",
            "Review date/time UTC",
            "Reviewer signature"
        )
    }
)

$requiredMachineEvidenceFiles = @(
    "production-readiness-report.json",
    "production-readiness-verification-report.json",
    "visual-smoke-manifest.json",
    "visual-smoke-evidence-report.json",
    "accountant-workbench-evidence-report.json",
    "monitoring-error-routing-report.json",
    "structured-log-report.json"
)

$requiredMachineEvidenceProvenance = @(
    [pscustomobject]@{ FileName = "production-readiness-report.json"; SourceArtifactName = "production-readiness-report"; SourceArtifactFile = "production-readiness-report.json" },
    [pscustomobject]@{ FileName = "production-readiness-verification-report.json"; SourceArtifactName = "production-readiness-report"; SourceArtifactFile = "production-readiness-verification-report.json" },
    [pscustomobject]@{ FileName = "visual-smoke-manifest.json"; SourceArtifactName = "visual-smoke-screenshots"; SourceArtifactFile = "visual-smoke-manifest.json" },
    [pscustomobject]@{ FileName = "visual-smoke-evidence-report.json"; SourceArtifactName = "visual-smoke-screenshots"; SourceArtifactFile = "visual-smoke-evidence-report.json" },
    [pscustomobject]@{ FileName = "accountant-workbench-evidence-report.json"; SourceArtifactName = "visual-smoke-screenshots"; SourceArtifactFile = "accountant-workbench-evidence-report.json" },
    [pscustomobject]@{ FileName = "monitoring-error-routing-report.json"; SourceArtifactName = "monitoring-error-routing-smoke"; SourceArtifactFile = "monitoring-error-routing-report.json" },
    [pscustomobject]@{ FileName = "structured-log-report.json"; SourceArtifactName = "structured-json-log-sample"; SourceArtifactFile = "structured-log-report.json" }
)

$requiredReviewerQueue = @(
    "visual-qa-screenshot-review",
    "source-law-change-review",
    "external-ros-validation-evidence",
    "qualified-accountant-final-signoff",
    "manual-accountant-acceptance",
    "production-monitoring"
)

$requiredHumanReleaseEvidenceCloseoutStepCodes = @(
    "pick-up-reviewer-workspace",
    "complete-human-evidence-templates",
    "run-release-evidence-verifier",
    "confirm-human-evidence-completion",
    "verify-release-artifact-pack"
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
    Assert-FieldEquals $Content "Visual smoke manifest file" "visual-smoke-manifest.json" $context $Failures
    Assert-FieldEquals $Content "Visual smoke evidence report file" "visual-smoke-evidence-report.json" $context $Failures
    Assert-FieldEquals $Content "Accountant workbench evidence report file" "accountant-workbench-evidence-report.json" $context $Failures
    Assert-UtcTimestampField $Content "Review date/time UTC" $context $Failures
    Assert-FieldMatchesPattern $Content "Reviewer name" "^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+" "a real visual QA reviewer name" $context $Failures
    Assert-FieldMatchesPattern $Content "Reviewer role" "^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+" "a real visual QA reviewer role" $context $Failures
    Assert-FieldMatchesPattern $Content "Reviewer signature" "^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+" "a real visual QA reviewer signature" $context $Failures
    Assert-PositiveIntegerField $Content "Minimum PNG IDAT byte size" $context $Failures
    Assert-PositiveIntegerField $Content "Minimum screenshot pixel sample count" $context $Failures
    Assert-MinimumIntegerField $Content "Minimum sampled distinct color count" 4 $context $Failures
    Assert-MinimumIntegerField $Content "Minimum screenshot luminance range" 10 $context $Failures
    Assert-MinimumDecimalField $Content "Minimum automated contrast ratio" ([decimal]3.0) $context $Failures
    Assert-NoUncheckedBoxes $Content $context $Failures
    Assert-CheckedDecision $Content "Accepted for this release candidate." $context $Failures
    Assert-UncheckedDecision $Content "Rejected; defects listed below must be fixed and re-reviewed." $context $Failures
    Assert-CompletedTableRows $Content $requiredRouteCodes $context $Failures
    Assert-CompletedTableColumnMatches $Content $requiredRouteCodes 1 "Desktop light" "^pass$" "exactly pass" $context $Failures
    Assert-CompletedTableColumnMatches $Content $requiredRouteCodes 2 "Desktop dark" "^pass$" "exactly pass" $context $Failures
    Assert-CompletedTableColumnMatches $Content $requiredRouteCodes 3 "Mobile light" "^pass$" "exactly pass" $context $Failures
    Assert-CompletedTableColumnMatches $Content $requiredRouteCodes 4 "Mobile dark" "^pass$" "exactly pass" $context $Failures
    Assert-CompletedTableColumnMatches $Content $requiredRouteCodes 5 "Notes" "^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+" "a real retained visual evidence note or reference" $context $Failures
    Assert-CompletedTableColumnMatchesVisualRouteReference $Content $requiredRouteCodes 5 "Notes" $context $Failures
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
    Assert-FieldMatchesPattern $Content "Accountant name" "^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+" "a real accountant name" $context $Failures
    Assert-FieldMatchesPattern $Content "Qualification / professional body" "^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+" "a real qualification or professional body" $context $Failures
    Assert-FieldMatchesPattern $Content "Firm / reviewer capacity" "^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+" "a real firm or reviewer capacity" $context $Failures
    Assert-FieldMatchesPattern $Content "Qualified accountant signature" "^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+" "a real qualified-accountant signature" $context $Failures
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
    Assert-CompletedTableColumnMatchesScenarioWalkthroughReference $Content $canonicalGoldenCorpusScenarioCodes 7 "Scenario evidence reference" $context $Failures
    Assert-CompletedTableColumnMatches $Content $requiredRouteCodes 1 "Decision question answered" "^yes$" "exactly yes" $context $Failures
    Assert-CompletedTableColumnMatches $Content $requiredRouteCodes 2 "Evidence accepted" "^accepted$" "accepted" $context $Failures
    Assert-CompletedTableColumnMatches $Content $requiredRouteCodes 3 "Workbench evidence reference" "^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+" "a real retained workbench evidence reference" $context $Failures
    Assert-CompletedTableColumnMatchesRouteReference $Content $requiredRouteCodes 3 "Workbench evidence reference" $context $Failures
    Assert-CompletedTableColumnMatches $Content $requiredRouteCodes 4 "Notes" "^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+" "a real retained route walkthrough note or reference" $context $Failures
    Assert-CompletedTableColumnMatchesRouteWalkthroughNote $Content $requiredRouteCodes 4 "Notes" $context $Failures

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
    Assert-CompletedTableColumnMatches $Content $requiredManualHandoffScenarioCodes 4 "Decision" "^accepted$" "exactly accepted for this release candidate" $context $Failures
    Assert-CompletedTableColumnMatches $Content $requiredManualHandoffPathCodes 1 "Release evidence reference" "^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+" "a real unsupported-path evidence reference" $context $Failures
    Assert-CompletedTableColumnMatches $Content $requiredManualHandoffPathCodes 2 "Reviewer decision" "^accepted$" "exactly accepted" $context $Failures
    Assert-CompletedTableColumnMatchesEvidenceAnchor $Content $requiredManualHandoffScenarioCodes 1 "Auditor evidence" "signed-auditor-report-evidence" $context $Failures
    Assert-CompletedTableColumnMatchesEvidenceAnchor $Content $requiredManualHandoffScenarioCodes 2 "Manual handoff note" "manual-handoff-note" $context $Failures
    Assert-CompletedTableColumnMatchesEvidenceAnchor $Content $requiredManualHandoffScenarioCodes 3 "Filing readiness snapshot" "filing-readiness-snapshot" $context $Failures
    Assert-CompletedTableColumnMatchesEvidenceAnchor $Content $requiredManualHandoffPathCodes 1 "Release evidence reference" "unsupported-path-evidence" $context $Failures
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
    Assert-FieldMatchesPattern $Content "External validation provider" "^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+" "a real external validation provider" $context $Failures
    Assert-FieldMatchesPattern $Content "Validation environment" "^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+" "a real validation environment" $context $Failures
    Assert-FieldMatchesPattern $Content "Validation run/reference id" "^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+" "a real validation run or reference id" $context $Failures
    Assert-FieldMatchesPattern $Content "Validation report file or URL" "^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+" "a real validation report file or URL" $context $Failures
    Assert-FieldMatchesPattern $Content "Generated iXBRL artifact name" "^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+\.(xhtml|html|zip)$" "a retained .xhtml, .html, or .zip artifact name" $context $Failures
    Assert-Sha256Field $Content "Generated iXBRL SHA-256" $context $Failures
    Assert-FieldMatchesPattern $Content "Taxonomy package" "^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+" "a real taxonomy package or retained package reference" $context $Failures
    Assert-FieldMatchesPattern $Content "Company/period reference" "^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+" "a real company/period reference" $context $Failures
    Assert-NoUncheckedBoxes $Content $context $Failures
    Assert-CheckedDecision $Content "Accepted as external ROS/iXBRL validation evidence for this release candidate." $context $Failures
    Assert-UncheckedDecision $Content "Rejected; validation issues below must be remediated and re-reviewed." $context $Failures
    Assert-CompletedTableRows $Content $canonicalGoldenCorpusScenarioCodes $context $Failures
    Assert-CompletedTableColumnMatches $Content $canonicalGoldenCorpusScenarioCodes 1 "External reference" "^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+" "a real external validation reference" $context $Failures
    Assert-CompletedTableColumnMatches $Content $canonicalGoldenCorpusScenarioCodes 2 "Artifact hash" "^[0-9a-fA-F]{64}$" "a 64-character hexadecimal SHA-256 digest" $context $Failures
    Assert-CompletedTableColumnMatches $Content $canonicalGoldenCorpusScenarioCodes 3 "Taxonomy package" "^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+" "a real retained taxonomy package reference" $context $Failures
    Assert-CompletedTableColumnMatches $Content $canonicalGoldenCorpusScenarioCodes 4 "Warnings/errors" "^(none|accepted|remediated)$" "exactly none, accepted, or remediated" $context $Failures
    Assert-CompletedTableColumnMatches $Content $canonicalGoldenCorpusScenarioCodes 5 "Decision" "^accepted$" "exactly accepted for this release candidate" $context $Failures
    Assert-CompletedTableColumnMatchesExternalValidationReference $Content $canonicalGoldenCorpusScenarioCodes 1 "External reference" $context $Failures
    Assert-CompletedTableColumnMatchesTaxonomyPackageReference $Content $canonicalGoldenCorpusScenarioCodes 3 "Taxonomy package" $context $Failures
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
    Assert-FieldMatchesPattern $Content "Source-law snapshot fingerprint" "^source-law-snapshot-fingerprint#[A-Za-z0-9._:-]+$" "an exact source-law-snapshot-fingerprint retained evidence anchor" $context $Failures
    Assert-Sha256Field $Content "Source-law snapshot content hash" $context $Failures
    Assert-FieldMatchesPattern $Content "Reviewer name" "^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+" "a real reviewer name" $context $Failures
    Assert-FieldMatchesPattern $Content "Reviewer role" "^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+" "a real reviewer role" $context $Failures
    Assert-FieldMatchesPattern $Content "Qualified accountant name" "^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+" "a real qualified accountant name" $context $Failures
    Assert-FieldMatchesPattern $Content "Qualification / professional body" "^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+" "a real qualification or professional body" $context $Failures
    Assert-FieldMatchesPattern $Content "Reviewer signature" "^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+" "a real reviewer signature" $context $Failures
    Assert-FieldMatchesPattern $Content "Qualified accountant source-law sign-off" "^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+" "a real qualified-accountant source-law sign-off" $context $Failures
    Assert-NoUncheckedBoxes $Content $context $Failures
    Assert-CheckedDecision $Content "Accepted as source-law review evidence for this release candidate." $context $Failures
    Assert-UncheckedDecision $Content "Rejected; source-law issues below must be remediated and re-reviewed." $context $Failures
    Assert-CompletedTableRows $Content $requiredSourceLawSourceIds $context $Failures
    Assert-CompletedTableColumnMatches $Content $requiredSourceLawSourceIds 1 "URL reachable" "^yes$" "yes" $context $Failures
    Assert-CompletedTableColumnMatches $Content $requiredSourceLawSourceIds 2 "Effective date checked" "^([0-9]{4}-[0-9]{2}-[0-9]{2}|not dated)$" "YYYY-MM-DD or not dated" $context $Failures
    Assert-CompletedTableColumnMatches $Content $requiredSourceLawSourceIds 3 "Guidance wording compared" "^yes$" "yes" $context $Failures
    Assert-CompletedTableColumnMatches $Content $requiredSourceLawSourceIds 4 "Platform impact" "^(no change|reflected|blocking)$" "exactly no change, reflected, or blocking" $context $Failures
    Assert-CompletedTableColumnMatches $Content $requiredSourceLawSourceIds 5 "Decision" "^accepted$" "accepted for this release candidate" $context $Failures
    Assert-CompletedTableColumnMatches $Content $requiredSourceLawSourceIds 6 "Notes" "^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+" "a real retained per-source note or evidence reference" $context $Failures
    Assert-CompletedTableColumnMatchesSourceLawNote $Content $requiredSourceLawSourceIds 6 "Notes" $context $Failures
}

$failures = [System.Collections.Generic.List[string]]::new()
$resolvedDirectory = Resolve-Path -LiteralPath $EvidenceDirectory -ErrorAction Stop

$visualPath = Join-Path $resolvedDirectory "visual-qa-signoff-template.md"
$sourceLawPath = Join-Path $resolvedDirectory "source-law-review-template.md"
$externalRosIxbrlPath = Join-Path $resolvedDirectory "external-ros-ixbrl-validation-template.md"
$accountantPath = Join-Path $resolvedDirectory "qualified-accountant-acceptance-template.md"
$manualHandoffPath = Join-Path $resolvedDirectory "manual-handoff-acceptance-template.md"
$monitoringPath = Join-Path $resolvedDirectory "monitoring-provider-confirmation-template.md"
$workspaceManifestPath = Join-Path $resolvedDirectory "release-evidence-workspace-manifest.json"
$machineEvidenceSummaryPath = Join-Path $resolvedDirectory "release-evidence-machine-summary.json"
$workspaceVerificationReportPath = Join-Path $resolvedDirectory "release-evidence-workspace-verification-report.json"

$visual = [string](Read-EvidenceFile $visualPath $failures)
$sourceLaw = [string](Read-EvidenceFile $sourceLawPath $failures)
$externalRosIxbrl = [string](Read-EvidenceFile $externalRosIxbrlPath $failures)
$accountant = [string](Read-EvidenceFile $accountantPath $failures)
$manualHandoff = [string](Read-EvidenceFile $manualHandoffPath $failures)
$monitoring = [string](Read-EvidenceFile $monitoringPath $failures)
$workspaceManifest = Read-JsonEvidenceFile $workspaceManifestPath "release-evidence-workspace-manifest.json" $failures
$machineEvidenceSummary = Read-JsonEvidenceFile $machineEvidenceSummaryPath "release-evidence-machine-summary.json" $failures
$workspaceVerificationReport = Read-JsonEvidenceFile $workspaceVerificationReportPath "release-evidence-workspace-verification-report.json" $failures

$evidenceFiles = @(
    New-EvidenceFileManifestItem "visualQa" $visualPath $visual
    New-EvidenceFileManifestItem "sourceLawReview" $sourceLawPath $sourceLaw
    New-EvidenceFileManifestItem "externalRosIxbrlValidation" $externalRosIxbrlPath $externalRosIxbrl
    New-EvidenceFileManifestItem "qualifiedAccountantAcceptance" $accountantPath $accountant
    New-EvidenceFileManifestItem "manualHandoffAcceptance" $manualHandoffPath $manualHandoff
    New-EvidenceFileManifestItem "monitoringProviderConfirmation" $monitoringPath $monitoring
)

$workspaceControlFiles = @(
    New-EvidenceFileManifestItem "releaseEvidenceWorkspaceManifest" $workspaceManifestPath ""
    New-EvidenceFileManifestItem "releaseEvidenceMachineSummary" $machineEvidenceSummaryPath ""
    New-EvidenceFileManifestItem "releaseEvidenceWorkspaceVerificationReport" $workspaceVerificationReportPath ""
)

$visualFailures = [System.Collections.Generic.List[string]]::new()
$sourceLawFailures = [System.Collections.Generic.List[string]]::new()
$externalRosIxbrlFailures = [System.Collections.Generic.List[string]]::new()
$accountantFailures = [System.Collections.Generic.List[string]]::new()
$manualHandoffFailures = [System.Collections.Generic.List[string]]::new()
$monitoringFailures = [System.Collections.Generic.List[string]]::new()

if ($visual.Trim().Length -gt 0) {
    Test-VisualEvidence $visual $visualFailures
}

if ($sourceLaw.Trim().Length -gt 0) {
    Test-SourceLawEvidence $sourceLaw $sourceLawFailures
}

if ($accountant.Trim().Length -gt 0) {
    Test-AccountantEvidence $accountant $accountantFailures
}

if ($manualHandoff.Trim().Length -gt 0) {
    Test-ManualHandoffEvidence $manualHandoff $manualHandoffFailures
}

if ($externalRosIxbrl.Trim().Length -gt 0) {
    Test-ExternalRosIxbrlEvidence $externalRosIxbrl $externalRosIxbrlFailures
}

if ($monitoring.Trim().Length -gt 0) {
    Test-MonitoringEvidence $monitoring $monitoringFailures
}

$humanEvidenceCompletion = @(
    New-HumanEvidenceCompletionItem "visualQa" $visualPath "Named visual QA reviewer" "visual-qa-screenshot-review" $visual $visualFailures
    New-HumanEvidenceCompletionItem "sourceLawReview" $sourceLawPath "Named source-law reviewer plus qualified accountant" "source-law-change-review" $sourceLaw $sourceLawFailures
    New-HumanEvidenceCompletionItem "externalRosIxbrlValidation" $externalRosIxbrlPath "External ROS/iXBRL validation reviewer" "external-ros-validation-evidence" $externalRosIxbrl $externalRosIxbrlFailures
    New-HumanEvidenceCompletionItem "qualifiedAccountantAcceptance" $accountantPath "Named qualified accountant" "qualified-accountant-final-signoff" $accountant $accountantFailures
    New-HumanEvidenceCompletionItem "manualHandoffAcceptance" $manualHandoffPath "Named manual handoff reviewer" "manual-accountant-acceptance" $manualHandoff $manualHandoffFailures
    New-HumanEvidenceCompletionItem "monitoringProviderConfirmation" $monitoringPath "Named release operator" "production-monitoring" $monitoring $monitoringFailures
)

foreach ($evidenceFailureList in @(
    $visualFailures,
    $sourceLawFailures,
    $externalRosIxbrlFailures,
    $accountantFailures,
    $manualHandoffFailures,
    $monitoringFailures
)) {
    foreach ($failure in $evidenceFailureList) {
        Add-Failure $failures $failure
    }
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

Test-ReleaseWorkspaceControlEvidence $workspaceManifest $machineEvidenceSummary $workspaceVerificationReport $releaseCandidateCommitSha $releaseCandidateRunUrl $failures

$productionScorecardCompletion = New-ProductionScorecardCompletion $humanEvidenceCompletion $failures

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
    workspaceControlFiles = $workspaceControlFiles
    humanEvidenceCompletion = $humanEvidenceCompletion
    productionScorecardCompletion = $productionScorecardCompletion
    files = [ordered]@{
        visualQa = $visualPath
        sourceLawReview = $sourceLawPath
        externalRosIxbrlValidation = $externalRosIxbrlPath
        qualifiedAccountantAcceptance = $accountantPath
        manualHandoffAcceptance = $manualHandoffPath
        monitoringProviderConfirmation = $monitoringPath
        releaseEvidenceWorkspaceManifest = $workspaceManifestPath
        releaseEvidenceMachineSummary = $machineEvidenceSummaryPath
        releaseEvidenceWorkspaceVerificationReport = $workspaceVerificationReportPath
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
        releaseEvidenceWorkspaceFiles = @($workspaceControlFiles | ForEach-Object { $_.fileName })
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
