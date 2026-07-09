param(
    [string]$WorkspaceDirectory = (Join-Path $PSScriptRoot "..\artifacts\release-evidence-workspace"),
    [string]$ReportPath = "",
    [string]$CommitSha = "",
    [string]$GitHubActionsRunUrl = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$requiredTemplates = @(
    "visual-qa-signoff-template.md",
    "source-law-review-template.md",
    "external-ros-ixbrl-validation-template.md",
    "qualified-accountant-acceptance-template.md",
    "manual-handoff-acceptance-template.md",
    "monitoring-provider-confirmation-template.md"
)

$requiredWorkspaceFiles = @(
    $requiredTemplates +
    @(
        "production-readiness-report.json",
        "visual-smoke-manifest.json",
        "visual-smoke-evidence-report.json",
        "accountant-workbench-evidence-report.json",
        "monitoring-error-routing-report.json",
        "structured-log-report.json",
        "release-evidence-workspace-manifest.json",
        "release-evidence-machine-summary.json",
        "release-evidence-reviewer-index.md",
        "release-evidence-reviewer-completion.json",
        "release-evidence-reviewer-blockers.md",
        "release-evidence-report.json",
        "release-evidence-verifier-output.txt"
    )
)

$requiredReviewerQueue = @(
    [pscustomobject]@{
        EvidenceGate = "Visual QA sign-off"
        TemplateFile = "visual-qa-signoff-template.md"
        ReviewerRole = "Named visual QA reviewer"
        SignOffGate = "visual-qa-screenshot-review"
    },
    [pscustomobject]@{
        EvidenceGate = "Source-law review"
        TemplateFile = "source-law-review-template.md"
        ReviewerRole = "Named source-law reviewer plus qualified accountant"
        SignOffGate = "source-law-change-review"
    },
    [pscustomobject]@{
        EvidenceGate = "External ROS/iXBRL validation"
        TemplateFile = "external-ros-ixbrl-validation-template.md"
        ReviewerRole = "External ROS/iXBRL validation reviewer"
        SignOffGate = "external-ros-validation-evidence"
    },
    [pscustomobject]@{
        EvidenceGate = "Qualified-accountant acceptance"
        TemplateFile = "qualified-accountant-acceptance-template.md"
        ReviewerRole = "Named qualified accountant"
        SignOffGate = "qualified-accountant-final-signoff"
    },
    [pscustomobject]@{
        EvidenceGate = "Manual handoff acceptance"
        TemplateFile = "manual-handoff-acceptance-template.md"
        ReviewerRole = "Named manual handoff reviewer"
        SignOffGate = "manual-accountant-acceptance"
    },
    [pscustomobject]@{
        EvidenceGate = "Monitoring provider confirmation"
        TemplateFile = "monitoring-provider-confirmation-template.md"
        ReviewerRole = "Named release operator"
        SignOffGate = "production-monitoring"
    }
)

$requiredMachineEvidenceFiles = @(
    "production-readiness-report.json",
    "visual-smoke-manifest.json",
    "visual-smoke-evidence-report.json",
    "accountant-workbench-evidence-report.json",
    "monitoring-error-routing-report.json",
    "structured-log-report.json"
)

$requiredMachineEvidenceProvenance = @(
    [pscustomobject]@{ FileName = "production-readiness-report.json"; SourceArtifactName = "production-readiness-report"; SourceArtifactFile = "production-readiness-report.json" },
    [pscustomobject]@{ FileName = "visual-smoke-manifest.json"; SourceArtifactName = "visual-smoke-screenshots"; SourceArtifactFile = "visual-smoke-manifest.json" },
    [pscustomobject]@{ FileName = "visual-smoke-evidence-report.json"; SourceArtifactName = "visual-smoke-screenshots"; SourceArtifactFile = "visual-smoke-evidence-report.json" },
    [pscustomobject]@{ FileName = "accountant-workbench-evidence-report.json"; SourceArtifactName = "visual-smoke-screenshots"; SourceArtifactFile = "accountant-workbench-evidence-report.json" },
    [pscustomobject]@{ FileName = "monitoring-error-routing-report.json"; SourceArtifactName = "monitoring-error-routing-smoke"; SourceArtifactFile = "monitoring-error-routing-report.json" },
    [pscustomobject]@{ FileName = "structured-log-report.json"; SourceArtifactName = "structured-json-log-sample"; SourceArtifactFile = "structured-log-report.json" }
)

function Add-Failure {
    param(
        [System.Collections.Generic.List[string]]$Failures,
        [string]$Message
    )

    $Failures.Add($Message) | Out-Null
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

function Assert-ArrayContains {
    param(
        [object[]]$Values,
        [string]$Expected,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    if (-not (@($Values) | Where-Object { [string]$_ -eq $Expected })) {
        Add-Failure $Failures "$Context must include $Expected."
    }
}

function Assert-TextContains {
    param(
        [string]$Content,
        [string]$Needle,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    if ($Content.IndexOf($Needle, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
        Add-Failure $Failures "$Context must include '$Needle'."
    }
}

function Get-FileSha256 {
    param([string]$Path)

    $stream = [IO.File]::OpenRead($Path)
    try {
        $sha = [System.Security.Cryptography.SHA256]::Create()
        try {
            return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace("-", "").ToLowerInvariant()
        } finally {
            $sha.Dispose()
        }
    } finally {
        $stream.Dispose()
    }
}

function Format-MarkdownTableCell {
    param($Value)

    return ([string]$Value).Replace("`r", " ").Replace("`n", " ").Replace("|", "\|")
}

function Write-ReviewerBlockerSummary {
    param(
        $Report,
        [string]$Path
    )

    $completion = @((Get-JsonPropertyValue $Report "humanEvidenceCompletion"))
    $rows = @($completion | ForEach-Object {
        $blockingFailures = @((Get-JsonPropertyValue $_ "blockingFailures"))
        $firstBlockingFailure = if ($blockingFailures.Count -gt 0) { [string]$blockingFailures[0] } else { "" }
        "| $(Format-MarkdownTableCell (Get-JsonPropertyValue $_ "evidenceName")) | $(Format-MarkdownTableCell (Get-JsonPropertyValue $_ "templateFile")) | $(Format-MarkdownTableCell (Get-JsonPropertyValue $_ "requiredReviewerRole")) | $(Format-MarkdownTableCell (Get-JsonPropertyValue $_ "signOffGate")) | $(Format-MarkdownTableCell (Get-JsonPropertyValue $_ "status")) | $(Format-MarkdownTableCell (Get-JsonPropertyValue $_ "blockingFailureCount")) | $(Format-MarkdownTableCell $firstBlockingFailure) |"
    })

    $content = @"
# Release Evidence Reviewer Blockers

Status: reviewer-action-required

This summary is generated from ``release-evidence-report.json``. It is not release approval and it is not professional sign-off.

| Evidence name | Template file | Required reviewer | Sign-off gate | Status | Blocking failures | First blocker |
| --- | --- | --- | --- | --- | ---: | --- |
$($rows -join "`n")

Run ``scripts/verify-release-evidence.ps1`` again after named reviewers complete the Markdown templates. The release remains blocked until all six rows are accepted.
"@

    Set-Content -LiteralPath $Path -Value $content -NoNewline
}

$resolvedWorkspace = Resolve-Path -LiteralPath $WorkspaceDirectory
if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path $resolvedWorkspace.Path "release-evidence-report.json"
}

$failures = New-Object System.Collections.Generic.List[string]

$manifestPath = Join-Path $resolvedWorkspace.Path "release-evidence-workspace-manifest.json"
$machineEvidenceSummaryPath = Join-Path $resolvedWorkspace.Path "release-evidence-machine-summary.json"
$reviewerIndexPath = Join-Path $resolvedWorkspace.Path "release-evidence-reviewer-index.md"
$reviewerCompletionPath = Join-Path $resolvedWorkspace.Path "release-evidence-reviewer-completion.json"
$reviewerBlockersPath = Join-Path $resolvedWorkspace.Path "release-evidence-reviewer-blockers.md"
$releaseEvidenceVerifierOutputPath = Join-Path $resolvedWorkspace.Path "release-evidence-verifier-output.txt"
$manifest = $null

if (-not (Test-Path -LiteralPath $manifestPath)) {
    Add-Failure $failures "Workspace must include release-evidence-workspace-manifest.json."
} else {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json

    if ([string](Get-JsonPropertyValue $manifest "status") -ne "pending-human-evidence") {
        Add-Failure $failures "Workspace manifest status must be pending-human-evidence."
    }

    if ([string](Get-JsonPropertyValue $manifest "reviewerIndexFile") -ne "release-evidence-reviewer-index.md") {
        Add-Failure $failures "Workspace manifest reviewerIndexFile must be release-evidence-reviewer-index.md."
    }

    if ([string](Get-JsonPropertyValue $manifest "reviewerCompletionFile") -ne "release-evidence-reviewer-completion.json") {
        Add-Failure $failures "Workspace manifest reviewerCompletionFile must be release-evidence-reviewer-completion.json."
    }

    if ([string](Get-JsonPropertyValue $manifest "machineEvidenceSummaryFile") -ne "release-evidence-machine-summary.json") {
        Add-Failure $failures "Workspace manifest machineEvidenceSummaryFile must be release-evidence-machine-summary.json."
    }

    if (-not [string]::IsNullOrWhiteSpace($CommitSha) -and [string](Get-JsonPropertyValue $manifest "commitSha") -ne $CommitSha) {
        Add-Failure $failures "Workspace manifest commitSha must match CommitSha."
    }

    if (-not [string]::IsNullOrWhiteSpace($GitHubActionsRunUrl) -and [string](Get-JsonPropertyValue $manifest "githubActionsRunUrl") -ne $GitHubActionsRunUrl) {
        Add-Failure $failures "Workspace manifest githubActionsRunUrl must match GitHubActionsRunUrl."
    }

    foreach ($template in $requiredTemplates) {
        Assert-ArrayContains @((Get-JsonPropertyValue $manifest "preparedTemplates")) $template "Workspace manifest preparedTemplates" $failures
    }

    $retainedMachineEvidence = @((Get-JsonPropertyValue $manifest "retainedMachineEvidence"))
    if ($retainedMachineEvidence.Count -ne $requiredMachineEvidenceFiles.Count) {
        Add-Failure $failures "Workspace manifest retainedMachineEvidence must contain exactly $($requiredMachineEvidenceFiles.Count) entries."
    }

    foreach ($expectedMachineEvidence in $requiredMachineEvidenceProvenance) {
        $requiredMachineEvidenceFile = $expectedMachineEvidence.FileName
        $entry = $retainedMachineEvidence | Where-Object {
            [string](Get-JsonPropertyValue $_ "fileName") -eq $requiredMachineEvidenceFile
        } | Select-Object -First 1

        if ($null -eq $entry) {
            Add-Failure $failures "Workspace manifest retainedMachineEvidence must include $requiredMachineEvidenceFile."
            continue
        }

        $manifestByteSize = [string](Get-JsonPropertyValue $entry "byteSize")
        $manifestSha256 = [string](Get-JsonPropertyValue $entry "sha256")
        $retainedPath = Join-Path $resolvedWorkspace.Path $requiredMachineEvidenceFile

        if ([string](Get-JsonPropertyValue $entry "sourceArtifactName") -ne $expectedMachineEvidence.SourceArtifactName) {
            Add-Failure $failures "Workspace manifest retainedMachineEvidence.$requiredMachineEvidenceFile.sourceArtifactName must be $($expectedMachineEvidence.SourceArtifactName)."
        }

        if ([string](Get-JsonPropertyValue $entry "sourceArtifactFile") -ne $expectedMachineEvidence.SourceArtifactFile) {
            Add-Failure $failures "Workspace manifest retainedMachineEvidence.$requiredMachineEvidenceFile.sourceArtifactFile must be $($expectedMachineEvidence.SourceArtifactFile)."
        }

        if ($manifestByteSize -notmatch "^[1-9][0-9]*$") {
            Add-Failure $failures "Workspace manifest retainedMachineEvidence.$requiredMachineEvidenceFile.byteSize must be a positive integer."
        }

        if ($manifestSha256 -notmatch "^[0-9a-f]{64}$") {
            Add-Failure $failures "Workspace manifest retainedMachineEvidence.$requiredMachineEvidenceFile.sha256 must be a lowercase 64-character SHA-256 digest."
        }

        if (Test-Path -LiteralPath $retainedPath -PathType Leaf) {
            $retainedItem = Get-Item -LiteralPath $retainedPath
            if ($manifestByteSize -match "^[1-9][0-9]*$" -and [int64]$manifestByteSize -ne $retainedItem.Length) {
                Add-Failure $failures "Workspace manifest retainedMachineEvidence.$requiredMachineEvidenceFile.byteSize must match the retained file byte size."
            }

            if ($manifestSha256 -match "^[0-9a-f]{64}$" -and $manifestSha256 -ne (Get-FileSha256 $retainedPath)) {
                Add-Failure $failures "Workspace manifest retainedMachineEvidence.$requiredMachineEvidenceFile.sha256 must match the retained file SHA-256 digest."
            }
        }
    }

    $preparedTemplates = @((Get-JsonPropertyValue $manifest "preparedTemplates"))
    if ($preparedTemplates.Count -ne $requiredTemplates.Count) {
        Add-Failure $failures "Workspace manifest preparedTemplates must contain exactly $($requiredTemplates.Count) entries."
    }

    $reviewerQueue = @((Get-JsonPropertyValue $manifest "reviewerQueue"))
    if ($reviewerQueue.Count -ne $requiredReviewerQueue.Count) {
        Add-Failure $failures "Workspace manifest reviewerQueue must contain exactly $($requiredReviewerQueue.Count) entries."
    }

    foreach ($expected in $requiredReviewerQueue) {
        $entry = $reviewerQueue | Where-Object {
            [string](Get-JsonPropertyValue $_ "TemplateFile") -eq $expected.TemplateFile
        } | Select-Object -First 1

        if ($null -eq $entry) {
            Add-Failure $failures "Workspace manifest reviewerQueue must include $($expected.TemplateFile)."
            continue
        }

        foreach ($propertyName in @("EvidenceGate", "ReviewerRole", "SignOffGate")) {
            $actual = [string](Get-JsonPropertyValue $entry $propertyName)
            $expectedValue = [string](Get-JsonPropertyValue $expected $propertyName)
            if ($actual -ne $expectedValue) {
                Add-Failure $failures "Workspace manifest reviewerQueue.$($expected.TemplateFile).$propertyName must be '$expectedValue'."
            }
        }

        if ([string]::IsNullOrWhiteSpace([string](Get-JsonPropertyValue $entry "HumanAction"))) {
            Add-Failure $failures "Workspace manifest reviewerQueue.$($expected.TemplateFile).HumanAction must describe the remaining human-only action."
        }
    }

    foreach ($humanField in @(
        "reviewer/operator/accountant identity and role",
        "review dates and signatures",
        "external ROS/iXBRL validation references and artifact hashes"
    )) {
        Assert-ArrayContains @((Get-JsonPropertyValue $manifest "humanFieldsLeftBlank")) $humanField "Workspace manifest humanFieldsLeftBlank" $failures
    }
}

if (-not (Test-Path -LiteralPath $machineEvidenceSummaryPath)) {
    Add-Failure $failures "Workspace must include release-evidence-machine-summary.json."
} else {
    $machineEvidenceSummary = Get-Content -LiteralPath $machineEvidenceSummaryPath -Raw | ConvertFrom-Json

    if ([string](Get-JsonPropertyValue $machineEvidenceSummary "status") -ne "pending-human-evidence") {
        Add-Failure $failures "Machine evidence summary status must be pending-human-evidence."
    }

    if (-not [string]::IsNullOrWhiteSpace($CommitSha) -and [string](Get-JsonPropertyValue (Get-JsonPropertyValue $machineEvidenceSummary "releaseCandidate") "commitSha") -ne $CommitSha) {
        Add-Failure $failures "Machine evidence summary releaseCandidate.commitSha must match CommitSha."
    }

    if (-not [string]::IsNullOrWhiteSpace($GitHubActionsRunUrl) -and [string](Get-JsonPropertyValue (Get-JsonPropertyValue $machineEvidenceSummary "releaseCandidate") "githubActionsRunUrl") -ne $GitHubActionsRunUrl) {
        Add-Failure $failures "Machine evidence summary releaseCandidate.githubActionsRunUrl must match GitHubActionsRunUrl."
    }

    Assert-TextContains ([string](Get-JsonPropertyValue $machineEvidenceSummary "completionPolicy")) "machine evidence only" "release-evidence-machine-summary.json completionPolicy" $failures
    Assert-TextContains ([string](Get-JsonPropertyValue $machineEvidenceSummary "completionPolicy")) "named reviewers" "release-evidence-machine-summary.json completionPolicy" $failures

    $summaryRetainedMachineEvidence = @((Get-JsonPropertyValue $machineEvidenceSummary "retainedMachineEvidence"))
    if ($summaryRetainedMachineEvidence.Count -ne $requiredMachineEvidenceFiles.Count) {
        Add-Failure $failures "Machine evidence summary retainedMachineEvidence must contain exactly $($requiredMachineEvidenceFiles.Count) entries."
    }

    foreach ($expectedMachineEvidence in $requiredMachineEvidenceProvenance) {
        $requiredMachineEvidenceFile = $expectedMachineEvidence.FileName
        $summaryEntry = $summaryRetainedMachineEvidence | Where-Object {
            [string](Get-JsonPropertyValue $_ "fileName") -eq $requiredMachineEvidenceFile
        } | Select-Object -First 1

        if ($null -eq $summaryEntry) {
            Add-Failure $failures "Machine evidence summary retainedMachineEvidence must include $requiredMachineEvidenceFile."
            continue
        }

        foreach ($propertyName in @("sourceArtifactName", "sourceArtifactFile", "byteSize", "sha256")) {
            $summaryValue = [string](Get-JsonPropertyValue $summaryEntry $propertyName)
            $manifestValue = ""
            if ($null -ne $manifest) {
                $manifestEntry = @((Get-JsonPropertyValue $manifest "retainedMachineEvidence")) | Where-Object {
                    [string](Get-JsonPropertyValue $_ "fileName") -eq $requiredMachineEvidenceFile
                } | Select-Object -First 1
                if ($null -ne $manifestEntry) {
                    $manifestValue = [string](Get-JsonPropertyValue $manifestEntry $propertyName)
                }
            }

            if ([string]::IsNullOrWhiteSpace($summaryValue)) {
                Add-Failure $failures "Machine evidence summary retainedMachineEvidence.$requiredMachineEvidenceFile.$propertyName must be present."
            } elseif ($null -ne $manifest -and $summaryValue -ne $manifestValue) {
                Add-Failure $failures "Machine evidence summary retainedMachineEvidence.$requiredMachineEvidenceFile.$propertyName must match the workspace manifest."
            }
        }
    }

    $summaryVisualEvidence = Get-JsonPropertyValue $machineEvidenceSummary "visualEvidence"
    foreach ($expectedVisualFile in @("visual-smoke-manifest.json", "visual-smoke-evidence-report.json", "accountant-workbench-evidence-report.json")) {
        $summaryVisualText = $summaryVisualEvidence | ConvertTo-Json -Depth 4
        Assert-TextContains $summaryVisualText $expectedVisualFile "release-evidence-machine-summary.json visualEvidence" $failures
    }

    $summaryMonitoringEvidence = Get-JsonPropertyValue $machineEvidenceSummary "monitoringEvidence"
    foreach ($field in @("provider", "eventId", "correlationId", "baseUrl", "checkedAtUtc", "structuredLogFile")) {
        if ([string]::IsNullOrWhiteSpace([string](Get-JsonPropertyValue $summaryMonitoringEvidence $field))) {
            Add-Failure $failures "Machine evidence summary monitoringEvidence.$field must be present."
        }
    }

    $summaryReviewerQueue = @((Get-JsonPropertyValue $machineEvidenceSummary "reviewerQueue"))
    if ($summaryReviewerQueue.Count -ne $requiredReviewerQueue.Count) {
        Add-Failure $failures "Machine evidence summary reviewerQueue must contain exactly $($requiredReviewerQueue.Count) entries."
    }
}

if (-not (Test-Path -LiteralPath $reviewerIndexPath)) {
    Add-Failure $failures "Workspace must include release-evidence-reviewer-index.md."
} else {
    $reviewerIndex = Get-Content -LiteralPath $reviewerIndexPath -Raw
    foreach ($needle in @(
        "Release Evidence Reviewer Workspace",
        "Status: pending-human-evidence",
        "This workspace is reviewer preparation only.",
        "It is not release approval",
        "Reviewer Queue",
        "Reviewer Completion Ledger",
        "release-evidence-reviewer-completion.json",
        "release-evidence-machine-summary.json",
        "Completion Gate",
        "scripts/verify-release-evidence.ps1"
    )) {
        Assert-TextContains $reviewerIndex $needle "release-evidence-reviewer-index.md" $failures
    }

    foreach ($expected in $requiredReviewerQueue) {
        Assert-TextContains $reviewerIndex $expected.EvidenceGate "release-evidence-reviewer-index.md" $failures
        Assert-TextContains $reviewerIndex $expected.TemplateFile "release-evidence-reviewer-index.md" $failures
        Assert-TextContains $reviewerIndex $expected.ReviewerRole "release-evidence-reviewer-index.md" $failures
        Assert-TextContains $reviewerIndex $expected.SignOffGate "release-evidence-reviewer-index.md" $failures
    }

    foreach ($requiredMachineEvidenceFile in $requiredMachineEvidenceFiles) {
        Assert-TextContains $reviewerIndex $requiredMachineEvidenceFile "release-evidence-reviewer-index.md" $failures
    }
}

if (-not (Test-Path -LiteralPath $reviewerCompletionPath)) {
    Add-Failure $failures "Workspace must include release-evidence-reviewer-completion.json."
} else {
    $completionLedger = Get-Content -LiteralPath $reviewerCompletionPath -Raw | ConvertFrom-Json

    if ([string](Get-JsonPropertyValue $completionLedger "status") -ne "pending-human-evidence") {
        Add-Failure $failures "Reviewer completion ledger status must be pending-human-evidence."
    }

    if (-not [string]::IsNullOrWhiteSpace($CommitSha) -and [string](Get-JsonPropertyValue (Get-JsonPropertyValue $completionLedger "releaseCandidate") "commitSha") -ne $CommitSha) {
        Add-Failure $failures "Reviewer completion ledger releaseCandidate.commitSha must match CommitSha."
    }

    if (-not [string]::IsNullOrWhiteSpace($GitHubActionsRunUrl) -and [string](Get-JsonPropertyValue (Get-JsonPropertyValue $completionLedger "releaseCandidate") "githubActionsRunUrl") -ne $GitHubActionsRunUrl) {
        Add-Failure $failures "Reviewer completion ledger releaseCandidate.githubActionsRunUrl must match GitHubActionsRunUrl."
    }

    $completionEntries = @((Get-JsonPropertyValue $completionLedger "entries"))
    if ($completionEntries.Count -ne $requiredReviewerQueue.Count) {
        Add-Failure $failures "Reviewer completion ledger entries must contain exactly $($requiredReviewerQueue.Count) entries."
    }

    foreach ($expected in $requiredReviewerQueue) {
        $entry = $completionEntries | Where-Object {
            [string](Get-JsonPropertyValue $_ "templateFile") -eq $expected.TemplateFile
        } | Select-Object -First 1

        if ($null -eq $entry) {
            Add-Failure $failures "Reviewer completion ledger entries must include $($expected.TemplateFile)."
            continue
        }

        $expectedFields = @{
            evidenceGate = $expected.EvidenceGate
            reviewerRole = $expected.ReviewerRole
            signOffGate = $expected.SignOffGate
            status = "pending-human-evidence"
            evidenceReportStatus = "incomplete-before-review"
        }

        foreach ($propertyName in $expectedFields.Keys) {
            $actual = [string](Get-JsonPropertyValue $entry $propertyName)
            $expectedValue = [string]$expectedFields[$propertyName]
            if ($actual -ne $expectedValue) {
                Add-Failure $failures "Reviewer completion ledger $($expected.TemplateFile).$propertyName must be '$expectedValue'."
            }
        }

        if ([bool](Get-JsonPropertyValue $entry "completed")) {
            Add-Failure $failures "Reviewer completion ledger $($expected.TemplateFile).completed must remain false before named human sign-off."
        }

        if (-not [string]::IsNullOrWhiteSpace([string](Get-JsonPropertyValue $entry "completedBy"))) {
            Add-Failure $failures "Reviewer completion ledger $($expected.TemplateFile).completedBy must be blank before named human sign-off."
        }

        if (-not [string]::IsNullOrWhiteSpace([string](Get-JsonPropertyValue $entry "completedAtUtc"))) {
            Add-Failure $failures "Reviewer completion ledger $($expected.TemplateFile).completedAtUtc must be blank before named human sign-off."
        }

        if ([string]::IsNullOrWhiteSpace([string](Get-JsonPropertyValue $entry "humanAction"))) {
            Add-Failure $failures "Reviewer completion ledger $($expected.TemplateFile).humanAction must describe the remaining human-only action."
        }
    }
}

foreach ($template in $requiredTemplates) {
    if (-not (Test-Path -LiteralPath (Join-Path $resolvedWorkspace.Path $template))) {
        Add-Failure $failures "Workspace must include $template."
    }
}

foreach ($requiredMachineEvidenceFile in $requiredMachineEvidenceFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $resolvedWorkspace.Path $requiredMachineEvidenceFile))) {
        Add-Failure $failures "Workspace must include retained machine evidence file $requiredMachineEvidenceFile."
    }
}

$releaseEvidenceStillBlocked = $false
try {
    & (Join-Path $PSScriptRoot "verify-release-evidence.ps1") `
        -EvidenceDirectory $resolvedWorkspace.Path `
        -ReportPath $ReportPath `
        *> $releaseEvidenceVerifierOutputPath
} catch {
    $releaseEvidenceStillBlocked = $true
}

if (-not $releaseEvidenceStillBlocked) {
    Add-Failure $failures "Prepared release evidence workspace unexpectedly passed before named human sign-off."
}

if (-not (Test-Path -LiteralPath $ReportPath)) {
    Add-Failure $failures "Workspace verification must write release-evidence-report.json."
} else {
    $report = Get-Content -LiteralPath $ReportPath -Raw | ConvertFrom-Json
    if ([string](Get-JsonPropertyValue $report "status") -ne "failed") {
        Add-Failure $failures "Prepared release evidence workspace report must remain failed before named human sign-off."
    }

    $completion = @((Get-JsonPropertyValue $report "humanEvidenceCompletion"))
    if ($completion.Count -ne $requiredTemplates.Count) {
        Add-Failure $failures "Prepared release evidence workspace report must include six humanEvidenceCompletion entries."
    }

    Write-ReviewerBlockerSummary $report $reviewerBlockersPath

    foreach ($entry in $completion) {
        if ([string](Get-JsonPropertyValue $entry "status") -ne "incomplete") {
            Add-Failure $failures "Prepared release evidence workspace must keep all human evidence entries incomplete."
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($CommitSha) -and [string](Get-JsonPropertyValue (Get-JsonPropertyValue $report "releaseCandidate") "commitSha") -ne $CommitSha) {
        Add-Failure $failures "Prepared release evidence report commitSha must match CommitSha."
    }

    if (-not [string]::IsNullOrWhiteSpace($GitHubActionsRunUrl) -and [string](Get-JsonPropertyValue (Get-JsonPropertyValue $report "releaseCandidate") "githubActionsRunUrl") -ne $GitHubActionsRunUrl) {
        Add-Failure $failures "Prepared release evidence report githubActionsRunUrl must match GitHubActionsRunUrl."
    }
}

if (-not (Test-Path -LiteralPath $reviewerBlockersPath)) {
    Add-Failure $failures "Workspace must include release-evidence-reviewer-blockers.md."
} else {
    $reviewerBlockers = Get-Content -LiteralPath $reviewerBlockersPath -Raw
    foreach ($needle in @(
        "Release Evidence Reviewer Blockers",
        "Status: reviewer-action-required",
        "generated from ``release-evidence-report.json``",
        "It is not release approval",
        "Run ``scripts/verify-release-evidence.ps1`` again"
    )) {
        Assert-TextContains $reviewerBlockers $needle "release-evidence-reviewer-blockers.md" $failures
    }

    foreach ($expected in $requiredReviewerQueue) {
        Assert-TextContains $reviewerBlockers $expected.TemplateFile "release-evidence-reviewer-blockers.md" $failures
        Assert-TextContains $reviewerBlockers $expected.ReviewerRole "release-evidence-reviewer-blockers.md" $failures
        Assert-TextContains $reviewerBlockers $expected.SignOffGate "release-evidence-reviewer-blockers.md" $failures
    }
}

$workspaceFiles = @(
    foreach ($file in Get-ChildItem -LiteralPath $resolvedWorkspace.Path -File | Sort-Object Name) {
        [ordered]@{
            fileName = $file.Name
            byteSize = $file.Length
            sha256 = Get-FileSha256 $file.FullName
        }
    }
)

$inventoriedFileNames = @($workspaceFiles | ForEach-Object { [string]$_["fileName"] })
foreach ($requiredWorkspaceFile in $requiredWorkspaceFiles) {
    if (-not ($inventoriedFileNames | Where-Object { $_ -eq $requiredWorkspaceFile })) {
        Add-Failure $failures "Workspace file inventory must include $requiredWorkspaceFile."
    }
}

foreach ($inventoriedFileName in $inventoriedFileNames) {
    if ($inventoriedFileName -eq "release-evidence-workspace-verification-report.json") {
        continue
    }

    if (-not ($requiredWorkspaceFiles | Where-Object { $_ -eq $inventoriedFileName })) {
        Add-Failure $failures "Workspace file inventory must not include unexpected file $inventoriedFileName."
    }
}

$verificationReport = [ordered]@{
    status = if ($failures.Count -eq 0) { "passed" } else { "failed" }
    checkedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    workspaceDirectory = $resolvedWorkspace.Path
    releaseEvidenceReportPath = $ReportPath
    releaseEvidenceVerifierOutputPath = $releaseEvidenceVerifierOutputPath
    reviewerCompletionPath = $reviewerCompletionPath
    reviewerBlockersPath = $reviewerBlockersPath
    machineEvidenceSummaryPath = $machineEvidenceSummaryPath
    workspaceFiles = $workspaceFiles
    requiredWorkspaceFiles = $requiredWorkspaceFiles
    requiredTemplateCount = $requiredTemplates.Count
    failureCount = $failures.Count
    failures = @($failures)
}

$workspaceReportPath = Join-Path $resolvedWorkspace.Path "release-evidence-workspace-verification-report.json"
$verificationReport | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $workspaceReportPath

if ($failures.Count -gt 0) {
    throw "Release evidence workspace verification failed with $($failures.Count) issue(s)."
}

Write-Host "Release evidence workspace verification passed for $($resolvedWorkspace.Path)."
