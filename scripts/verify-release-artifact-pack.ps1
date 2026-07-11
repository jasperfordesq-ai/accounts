param(
    [string]$EvidenceDirectory = ".",
    [string]$ReportPath = "",
    [string]$CommitSha = "",
    [string]$GitHubActionsRunUrl = ""
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

function Assert-EvidenceDirectoryIsNotLink {
    param([string]$Directory)

    $directoryEntry = Get-Item -LiteralPath $Directory -Force -ErrorAction Stop
    $isLink =
        ($directoryEntry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        ($null -ne $directoryEntry.PSObject.Properties['LinkType'] -and
            -not [string]::IsNullOrWhiteSpace([string]$directoryEntry.LinkType))
    if ($isLink) {
        throw "EvidenceDirectory must be a self-contained directory and must not itself be a filesystem link."
    }
}

function Get-JsonProperty {
    param(
        [object]$Object,
        [string[]]$Path
    )

    $current = $Object
    foreach ($segment in $Path) {
        if ($null -eq $current -or -not ($current.PSObject.Properties.Name -contains $segment)) {
            return $null
        }

        $current = $current.$segment
    }

    return $current
}

function Read-JsonEvidence {
    param(
        [string]$Directory,
        [string]$FileName,
        [System.Collections.Generic.List[string]]$Failures
    )

    $path = Join-Path $Directory $FileName
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        Add-Failure $Failures "Missing release artifact evidence file: $FileName"
        return [pscustomobject]@{ __missing = $true; __path = $path }
    }

    try {
        $json = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        $json | Add-Member -NotePropertyName __path -NotePropertyValue $path -Force
        return $json
    } catch {
        Add-Failure $Failures "Release artifact evidence file is not valid JSON: $FileName"
        return [pscustomobject]@{ __invalid = $true; __path = $path }
    }
}

function Read-JsonEvidenceFile {
    param(
        [string]$Path,
        [string]$FileName,
        [System.Collections.Generic.List[string]]$Failures
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        Add-Failure $Failures "Missing release artifact evidence file: $FileName"
        return [pscustomobject]@{ __missing = $true; __path = $Path }
    }

    try {
        $json = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
        $json | Add-Member -NotePropertyName __path -NotePropertyValue $Path -Force
        return $json
    } catch {
        Add-Failure $Failures "Release artifact evidence file is not valid JSON: $FileName"
        return [pscustomobject]@{ __invalid = $true; __path = $Path }
    }
}

function Assert-StatusPassed {
    param(
        [object]$Evidence,
        [string]$FileName,
        [System.Collections.Generic.List[string]]$Failures
    )

    if ($Evidence.PSObject.Properties.Name -contains "__missing" -or $Evidence.PSObject.Properties.Name -contains "__invalid") {
        return
    }

    if ([string]$Evidence.status -ne "passed") {
        Add-Failure $Failures "$FileName must have status 'passed'."
    }
}

function Assert-Truthy {
    param(
        [object]$Value,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    if ($Value -ne $true) {
        Add-Failure $Failures "$Context must be true."
    }
}

function Assert-NonEmptyString {
    param(
        [object]$Value,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    if ([string]::IsNullOrWhiteSpace([string]$Value)) {
        Add-Failure $Failures "$Context must be present."
    }
}

function Assert-TextContains {
    param(
        [string]$Value,
        [string]$Needle,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    if ([string]::IsNullOrWhiteSpace($Value) -or $Value.IndexOf($Needle, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
        Add-Failure $Failures "$Context must mention $Needle."
    }
}

function Assert-ArrayContains {
    param(
        [object[]]$Values,
        [string]$Needle,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    if (-not (@($Values) -contains $Needle)) {
        Add-Failure $Failures "$Context must include $Needle."
    }
}

function Assert-ArrayContainsExactly {
    param(
        [object[]]$Values,
        [string[]]$Expected,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    $actual = @($Values | ForEach-Object { [string]$_ })
    if ($actual.Count -ne $Expected.Count) {
        Add-Failure $Failures "$Context must include exactly $($Expected.Count) item(s)."
    }

    foreach ($expectedValue in $Expected) {
        Assert-ArrayContains $actual $expectedValue $Context $Failures
    }
}

function Find-RetainedVisualSmokeScreenshot {
    param(
        [string]$Directory,
        [string]$FileName,
        [System.Collections.Generic.List[string]]$Failures
    )

    if ([string]::IsNullOrWhiteSpace($FileName)) {
        Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.fileName must be present before retained PNG matching."
        return ""
    }

    $matches = @(Get-ChildItem -LiteralPath $Directory -Recurse -File -Filter $FileName)
    if ($matches.Count -eq 0) {
        Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.$FileName retained PNG file must be present in the evidence pack."
        return ""
    }

    if ($matches.Count -gt 1) {
        Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.$FileName retained PNG file must be unambiguous in the evidence pack."
        return ""
    }

    return $matches[0].FullName
}

$expectedAccountantWorkbenchWorkflowStages = @(
    "Setup",
    "Import",
    "Classify",
    "Year-End",
    "Statements",
    "Notes",
    "Review",
    "Filing"
)

$expectedAccountantWorkbenchThemes = @("light", "dark")
$expectedAccountantWorkbenchViewports = @("mobile", "tablet", "desktop")
$expectedAccountantWorkbenchThemeViewportCoverage = @("dark/desktop", "dark/mobile", "dark/tablet", "light/desktop", "light/mobile", "light/tablet")

$expectedAccountantWorkbenchRouteAcceptance = @(
    [pscustomobject]@{ routeName = "dashboard"; routeKey = "dashboard"; label = "Dashboard"; expectedText = "Firm command centre"; workflowStages = $expectedAccountantWorkbenchWorkflowStages },
    [pscustomobject]@{ routeName = "production-readiness"; routeKey = "readiness"; label = "Production readiness"; expectedText = "Production Readiness Checklist"; workflowStages = @("Review", "Filing") },
    [pscustomobject]@{ routeName = "company-detail"; routeKey = "company"; label = "Company detail"; expectedText = "Company command centre"; workflowStages = @("Setup") },
    [pscustomobject]@{ routeName = "period-workspace"; routeKey = "period"; label = "Period workspace"; expectedText = "Filing readiness"; workflowStages = $expectedAccountantWorkbenchWorkflowStages },
    [pscustomobject]@{ routeName = "filing-review"; routeKey = "filing"; label = "Filing review"; expectedText = "Filing readiness profile"; workflowStages = @("Review", "Filing") },
    [pscustomobject]@{ routeName = "financial-statements"; routeKey = "financialStatements"; label = "Financial statements"; expectedText = "Financial Statements"; workflowStages = @("Statements") },
    [pscustomobject]@{ routeName = "workbench-preview"; routeKey = "workbenchPreview"; label = "Workbench preview"; expectedText = "Workbench Component Preview"; workflowStages = $expectedAccountantWorkbenchWorkflowStages }
)

$expectedAccountantWorkbenchReviewChecks = @(
    "accountant-workflow-hierarchy",
    "table-scanability",
    "theme-contrast",
    "axe-wcag-2.2-a-aa",
    "responsive-density",
    "loading-error-empty-states",
    "canonical-url-tab-state",
    "semantic-capture-distinctness",
    "stale-conflict-states"
)

$expectedAccountantWorkbenchLayoutChecks = @(
    "browser-console-errors",
    "page-horizontal-overflow",
    "visible-text-overlap"
)

$expectedAccountantWorkbenchExpectedTextChecks = @(
    "route expected accountant decision text",
    "visual smoke screenshots carry route expected accountant decision text",
    "visual smoke routeKey matches planned routeKey",
    "visual smoke screenshots carry stable routeKey",
    "visual smoke screenshots carry passed layout check results",
    "visual smoke screenshots carry passed automated theme contrast results",
    "visual smoke screenshots carry passed axe-core WCAG 2.2 A/AA results"
)

$expectedAccountantWorkbenchEvidenceFiles = @(
    "visual-smoke-manifest.json",
    "visual-smoke-evidence-report.json",
    "accountant-workbench-evidence-report.json"
)

$expectedVisualStateIds = @(
    "login", "password-change", "dashboard", "onboarding", "production-readiness", "company-detail",
    "period-workspace", "classification", "categorisation", "year-end", "adjustments", "notes", "charity",
    "financial-statements", "statement-source-trail", "statement-profit-and-loss", "statement-balance-sheet",
    "statement-tax-computation", "statement-cash-flow", "statement-equity-changes", "statement-directors-report",
    "filing-review", "workbench-preview", "state-loading", "state-empty", "state-maximum-data", "state-error",
    "state-partial-error", "state-permission-denied", "state-read-only", "state-stale", "state-conflict"
)
$expectedVisualMaterialRoutes = @(
    "login", "password-change", "onboarding", "classification", "categorisation", "year-end", "adjustments",
    "notes", "charity", "statement-trial-balance", "statement-source-trail", "statement-profit-and-loss",
    "statement-balance-sheet", "statement-tax-computation", "statement-cash-flow", "statement-equity-changes",
    "statement-directors-report", "filing"
)
$expectedVisualUiStates = @(
    "loading", "empty", "maximum-data", "error", "partial-error", "permission-denied", "read-only", "stale", "conflict"
)

$expectedReleaseEvidenceWorkspaceInventory = @(
    "visual-qa-signoff-template.md",
    "source-law-review-template.md",
    "external-ros-ixbrl-validation-template.md",
    "qualified-accountant-acceptance-template.md",
    "manual-handoff-acceptance-template.md",
    "monitoring-provider-confirmation-template.md",
    "production-readiness-report.json",
    "production-readiness-verification-report.json",
    "visual-smoke-manifest.json",
    "visual-smoke-evidence-report.json",
    "accountant-workbench-evidence-report.json",
    "monitoring-error-routing-report.json",
    "structured-log-report.json",
    "release-evidence-workspace-manifest.json",
    "release-evidence-machine-summary.json",
    "release-evidence-reviewer-index.md",
    "release-evidence-reviewer-completion.json",
    "release-evidence-reviewer-assignments.json",
    "release-evidence-reviewer-blockers.md",
    "release-evidence-report.json",
    "release-evidence-verifier-output.txt"
)

$expectedReleaseEvidenceMachineSummaryInputs = @(
    [pscustomobject]@{ fileName = "production-readiness-report.json"; sourceArtifactName = "production-readiness-report"; sourceArtifactFile = "production-readiness-report.json" },
    [pscustomobject]@{ fileName = "production-readiness-verification-report.json"; sourceArtifactName = "production-readiness-report"; sourceArtifactFile = "production-readiness-verification-report.json" },
    [pscustomobject]@{ fileName = "visual-smoke-manifest.json"; sourceArtifactName = "visual-smoke-screenshots"; sourceArtifactFile = "visual-smoke-manifest.json" },
    [pscustomobject]@{ fileName = "visual-smoke-evidence-report.json"; sourceArtifactName = "visual-smoke-screenshots"; sourceArtifactFile = "visual-smoke-evidence-report.json" },
    [pscustomobject]@{ fileName = "accountant-workbench-evidence-report.json"; sourceArtifactName = "visual-smoke-screenshots"; sourceArtifactFile = "accountant-workbench-evidence-report.json" },
    [pscustomobject]@{ fileName = "monitoring-error-routing-report.json"; sourceArtifactName = "monitoring-error-routing-smoke"; sourceArtifactFile = "monitoring-error-routing-report.json" },
    [pscustomobject]@{ fileName = "structured-log-report.json"; sourceArtifactName = "structured-json-log-sample"; sourceArtifactFile = "structured-log-report.json" }
)

$expectedPreparedHumanTemplateControls = @(
    [pscustomobject]@{
        fileName = "visual-qa-signoff-template.md"
        context = "Prepared visual QA template"
        blankFields = @("Reviewer name", "Reviewer role", "Review date/time UTC", "Reviewer signature")
    },
    [pscustomobject]@{
        fileName = "source-law-review-template.md"
        context = "Prepared source-law template"
        blankFields = @(
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
        fileName = "external-ros-ixbrl-validation-template.md"
        context = "Prepared external ROS/iXBRL template"
        blankFields = @(
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
        fileName = "qualified-accountant-acceptance-template.md"
        context = "Prepared qualified-accountant template"
        blankFields = @(
            "Accountant name",
            "Qualification / professional body",
            "Firm / reviewer capacity",
            "Review date/time UTC",
            "Qualified accountant signature"
        )
    },
    [pscustomobject]@{
        fileName = "manual-handoff-acceptance-template.md"
        context = "Prepared manual handoff template"
        blankFields = @(
            "Reviewer name",
            "Reviewer role",
            "Firm / reviewer capacity",
            "Review date/time UTC",
            "Reviewer signature"
        )
    }
)

$mutableReleaseEvidenceWorkspaceInventoryFiles = @(
    "visual-qa-signoff-template.md",
    "source-law-review-template.md",
    "external-ros-ixbrl-validation-template.md",
    "qualified-accountant-acceptance-template.md",
    "manual-handoff-acceptance-template.md",
    "monitoring-provider-confirmation-template.md",
    "release-evidence-report.json"
)

function Assert-AccountantWorkbenchRequiredCoverage {
    param(
        [object]$AccountantWorkbench,
        [System.Collections.Generic.List[string]]$Failures
    )

    Assert-ArrayContainsExactly @((Get-JsonProperty $AccountantWorkbench @("requiredCoverage", "workflowStages"))) $expectedAccountantWorkbenchWorkflowStages "accountant-workbench-evidence-report.json requiredCoverage.workflowStages" $Failures
    Assert-ArrayContainsExactly @((Get-JsonProperty $AccountantWorkbench @("requiredCoverage", "themes"))) $expectedAccountantWorkbenchThemes "accountant-workbench-evidence-report.json requiredCoverage.themes" $Failures
    Assert-ArrayContainsExactly @((Get-JsonProperty $AccountantWorkbench @("requiredCoverage", "viewports"))) $expectedAccountantWorkbenchViewports "accountant-workbench-evidence-report.json requiredCoverage.viewports" $Failures
    Assert-ArrayContainsExactly @((Get-JsonProperty $AccountantWorkbench @("requiredCoverage", "reviewChecks"))) $expectedAccountantWorkbenchReviewChecks "accountant-workbench-evidence-report.json requiredCoverage.reviewChecks" $Failures
    Assert-ArrayContainsExactly @((Get-JsonProperty $AccountantWorkbench @("requiredCoverage", "layoutChecks"))) $expectedAccountantWorkbenchLayoutChecks "accountant-workbench-evidence-report.json requiredCoverage.layoutChecks" $Failures
    Assert-ArrayContainsExactly @((Get-JsonProperty $AccountantWorkbench @("requiredCoverage", "expectedTextChecks"))) $expectedAccountantWorkbenchExpectedTextChecks "accountant-workbench-evidence-report.json requiredCoverage.expectedTextChecks" $Failures
    Assert-ArrayContainsExactly @((Get-JsonProperty $AccountantWorkbench @("requiredCoverage", "evidenceFiles"))) $expectedAccountantWorkbenchEvidenceFiles "accountant-workbench-evidence-report.json requiredCoverage.evidenceFiles" $Failures

    $expectedLayoutCheckEvidence = $expectedAccountantWorkbenchLayoutChecks | ForEach-Object { "$($_):passed" }
    Assert-ArrayContainsExactly @((Get-JsonProperty $AccountantWorkbench @("requiredCoverage", "layoutCheckEvidence"))) $expectedLayoutCheckEvidence "accountant-workbench-evidence-report.json requiredCoverage.layoutCheckEvidence" $Failures
    Assert-ArrayContainsExactly @((Get-JsonProperty $AccountantWorkbench @("requiredCoverage", "contrastCheckEvidence"))) @("theme-contrast:passed", "minimum-ratio:3") "accountant-workbench-evidence-report.json requiredCoverage.contrastCheckEvidence" $Failures
    Assert-ArrayContainsExactly @((Get-JsonProperty $AccountantWorkbench @("requiredCoverage", "accessibilityCheckEvidence"))) @("axe-wcag-2.2-a-aa:passed", "wcag2a:covered", "wcag2aa:covered", "wcag21a:covered", "wcag21aa:covered", "wcag22aa:covered", "violations:0") "accountant-workbench-evidence-report.json requiredCoverage.accessibilityCheckEvidence" $Failures
}

function Assert-AccountantWorkbenchRouteAcceptance {
    param(
        [object]$AccountantWorkbench,
        [System.Collections.Generic.List[string]]$Failures
    )

    $routeAcceptance = @($AccountantWorkbench.routeAcceptance)
    $routeReadiness = @($AccountantWorkbench.routeReadiness)
    foreach ($expected in $expectedAccountantWorkbenchRouteAcceptance) {
        Assert-ArrayContains @($AccountantWorkbench.requiredCoverage.routeCodes) $expected.routeName "accountant-workbench-evidence-report.json requiredCoverage.routeCodes" $Failures
        Assert-ArrayContains @($AccountantWorkbench.requiredCoverage.routeKeys) $expected.routeKey "accountant-workbench-evidence-report.json requiredCoverage.routeKeys" $Failures

        foreach ($evidenceId in @(
            "$($expected.routeName)-accountant-route-acceptance-note",
            "$($expected.routeName)-visual-smoke-screenshots-reviewed",
            "$($expected.routeName)-qualified-accountant-route-acceptance"
        )) {
            Assert-ArrayContains @($AccountantWorkbench.requiredCoverage.routeAcceptanceEvidence) $evidenceId "accountant-workbench-evidence-report.json requiredCoverage.routeAcceptanceEvidence" $Failures
        }

        $readiness = $routeReadiness | Where-Object { [string]$_.routeName -eq $expected.routeName } | Select-Object -First 1
        if ($null -eq $readiness) {
            Add-Failure $Failures "accountant-workbench-evidence-report.json routeReadiness must include $($expected.routeName)."
        } else {
            if ([string]$readiness.routeKey -ne [string]$expected.routeKey) {
                Add-Failure $Failures "accountant-workbench-evidence-report.json routeReadiness.$($expected.routeName).routeKey must be $($expected.routeKey)."
            }
            if ([string]$readiness.expectedText -ne [string]$expected.expectedText) {
                Add-Failure $Failures "accountant-workbench-evidence-report.json routeReadiness.$($expected.routeName).expectedText must be $($expected.expectedText)."
            }
            Assert-ArrayContainsExactly @($readiness.workflowStages) @($expected.workflowStages) "accountant-workbench-evidence-report.json routeReadiness.$($expected.routeName).workflowStages" $Failures
            Assert-ArrayContainsExactly @($readiness.themeViewportCoverage) $expectedAccountantWorkbenchThemeViewportCoverage "accountant-workbench-evidence-report.json routeReadiness.$($expected.routeName).themeViewportCoverage" $Failures
            if ([int]$readiness.screenshotCount -ne 6) {
                Add-Failure $Failures "accountant-workbench-evidence-report.json routeReadiness.$($expected.routeName).screenshotCount must be 6."
            }
            if ([int]$readiness.layoutCheckResultCount -ne 18) {
                Add-Failure $Failures "accountant-workbench-evidence-report.json routeReadiness.$($expected.routeName).layoutCheckResultCount must be 18."
            }
            if ([int]$readiness.contrastCheckResultCount -ne 6) {
                Add-Failure $Failures "accountant-workbench-evidence-report.json routeReadiness.$($expected.routeName).contrastCheckResultCount must be 6."
            }
            if ([int]$readiness.accessibilityCheckResultCount -ne 6 -or [int]$readiness.accessibilityViolationCount -ne 0) {
                Add-Failure $Failures "accountant-workbench-evidence-report.json routeReadiness.$($expected.routeName) must retain 6 passed accessibility results with zero violations."
            }
            if ([decimal]$readiness.minimumContrastRatio -lt 3.0) {
                Add-Failure $Failures "accountant-workbench-evidence-report.json routeReadiness.$($expected.routeName).minimumContrastRatio must be at least 3."
            }
            if ([string]$readiness.reviewStatus -ne "required-review") {
                Add-Failure $Failures "accountant-workbench-evidence-report.json routeReadiness.$($expected.routeName).reviewStatus must be required-review."
            }
            foreach ($reviewCheck in $expectedAccountantWorkbenchReviewChecks) {
                Assert-ArrayContains @($readiness.requiredReviewChecks) $reviewCheck "accountant-workbench-evidence-report.json routeReadiness.$($expected.routeName).requiredReviewChecks" $Failures
            }
        }

        $acceptance = $routeAcceptance | Where-Object { [string]$_.routeName -eq $expected.routeName } | Select-Object -First 1
        if ($null -eq $acceptance) {
            Add-Failure $Failures "accountant-workbench-evidence-report.json routeAcceptance must include $($expected.routeName)."
            continue
        }

        if ([string]$acceptance.routeKey -ne [string]$expected.routeKey) {
            Add-Failure $Failures "accountant-workbench-evidence-report.json routeAcceptance.$($expected.routeName).routeKey must be $($expected.routeKey)."
        }
        if ([string]$acceptance.label -ne [string]$expected.label) {
            Add-Failure $Failures "accountant-workbench-evidence-report.json routeAcceptance.$($expected.routeName).label must be $($expected.label)."
        }
        if ([string]$acceptance.expectedText -ne [string]$expected.expectedText) {
            Add-Failure $Failures "accountant-workbench-evidence-report.json routeAcceptance.$($expected.routeName).expectedText must be $($expected.expectedText)."
        }
        Assert-ArrayContainsExactly @($acceptance.workflowStages) @($expected.workflowStages) "accountant-workbench-evidence-report.json routeAcceptance.$($expected.routeName).workflowStages" $Failures
        if ([string]$acceptance.screenshotReviewEvidence -ne "$($expected.routeName)-light-dark-mobile-tablet-desktop-screenshot-review") {
            Add-Failure $Failures "accountant-workbench-evidence-report.json routeAcceptance.$($expected.routeName).screenshotReviewEvidence must be $($expected.routeName)-light-dark-mobile-tablet-desktop-screenshot-review."
        }
        if ([string]$acceptance.reviewStatus -ne "required-review") {
            Add-Failure $Failures "accountant-workbench-evidence-report.json routeAcceptance.$($expected.routeName).reviewStatus must be required-review."
        }
        foreach ($evidenceId in @(
            "$($expected.routeName)-accountant-route-acceptance-note",
            "$($expected.routeName)-visual-smoke-screenshots-reviewed",
            "$($expected.routeName)-qualified-accountant-route-acceptance"
        )) {
            Assert-ArrayContains @($acceptance.requiredAcceptanceEvidence) $evidenceId "accountant-workbench-evidence-report.json routeAcceptance.$($expected.routeName).requiredAcceptanceEvidence" $Failures
        }
    }
}

function Assert-VisualSmokeDimensionEvidence {
    param(
        [object]$VisualSmoke,
        [string]$Directory,
        [System.Collections.Generic.List[string]]$Failures
    )

    if ($VisualSmoke.PSObject.Properties.Name -contains "__missing" -or
        $VisualSmoke.PSObject.Properties.Name -contains "__invalid") {
        return
    }

    $expectedViewports = @(
        [pscustomobject]@{ name = "mobile"; width = 390; height = 844 },
        [pscustomobject]@{ name = "tablet"; width = 768; height = 1024 },
        [pscustomobject]@{ name = "desktop"; width = 1440; height = 1000 }
    )
    $expectedThemes = @("light", "dark")
    $expectedLayoutChecks = @(
        "browser-console-errors",
        "page-horizontal-overflow",
        "visible-text-overlap"
    )
    $expectedContrastCheck = "theme-contrast"
    $expectedAccessibilityCheck = "axe-wcag-2.2-a-aa"
    $expectedAccessibilityTags = @("wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa")
    $expectedResponsiveAcceptanceCheck = "responsive-workflow-acceptance"
    $minimumContrastRatio = [decimal]3.0
    $uiComponentOptionalStateIds = @(
        "state-loading",
        "state-empty",
        "state-permission-denied",
        "state-read-only",
        "state-stale"
    )

    Assert-ArrayContainsExactly @((Get-JsonProperty $VisualSmoke @("themes"))) $expectedThemes "visual-smoke-evidence-report.json themes" $Failures
    Assert-ArrayContainsExactly @((Get-JsonProperty $VisualSmoke @("viewports"))) @($expectedViewports | ForEach-Object { $_.name }) "visual-smoke-evidence-report.json viewports" $Failures

    if ([string](Get-JsonProperty $VisualSmoke @("inventoryVersion")) -ne "canonical-material-states-v1") {
        Add-Failure $Failures "visual-smoke-evidence-report.json inventoryVersion must be canonical-material-states-v1."
    }
    if ([int](Get-JsonProperty $VisualSmoke @("inventoryStateCount")) -ne 32 -or
        [int](Get-JsonProperty $VisualSmoke @("routeCount")) -ne 32) {
        Add-Failure $Failures "visual-smoke-evidence-report.json inventoryStateCount and routeCount must both be 32."
    }
    if ([int](Get-JsonProperty $VisualSmoke @("accountantWorkbenchRouteCount")) -ne 7) {
        Add-Failure $Failures "visual-smoke-evidence-report.json accountantWorkbenchRouteCount must be 7."
    }
    if ([int](Get-JsonProperty $VisualSmoke @("screenshotCount")) -ne 192 -or
        [int](Get-JsonProperty $VisualSmoke @("expectedScreenshotCount")) -ne 192) {
        Add-Failure $Failures "visual-smoke-evidence-report.json screenshotCount and expectedScreenshotCount must both be 192."
    }
    Assert-ArrayContainsExactly @((Get-JsonProperty $VisualSmoke @("requiredMaterialRoutes"))) $expectedVisualMaterialRoutes "visual-smoke-evidence-report.json requiredMaterialRoutes" $Failures
    Assert-ArrayContainsExactly @((Get-JsonProperty $VisualSmoke @("requiredUiStates"))) $expectedVisualUiStates "visual-smoke-evidence-report.json requiredUiStates" $Failures
    if ((Get-JsonProperty $VisualSmoke @("semanticDistinctnessPassed")) -ne $true) {
        Add-Failure $Failures "visual-smoke-evidence-report.json semanticDistinctnessPassed must be true."
    }
    if ([int](Get-JsonProperty $VisualSmoke @("semanticContentHashCount")) -lt 32) {
        Add-Failure $Failures "visual-smoke-evidence-report.json semanticContentHashCount must prove at least 32 distinct canonical states."
    }

    if ([int](Get-JsonProperty $VisualSmoke @("layoutCheckResultCount")) -ne 576) {
        Add-Failure $Failures "visual-smoke-evidence-report.json layoutCheckResultCount must be 576."
    }
    if ([string](Get-JsonProperty $VisualSmoke @("layoutChecksPassed")) -ne "True") {
        Add-Failure $Failures "visual-smoke-evidence-report.json layoutChecksPassed must be true."
    }
    if ([int](Get-JsonProperty $VisualSmoke @("contrastCheckResultCount")) -ne 192) {
        Add-Failure $Failures "visual-smoke-evidence-report.json contrastCheckResultCount must be 192."
    }
    if ([string](Get-JsonProperty $VisualSmoke @("themeContrastChecksPassed")) -ne "True") {
        Add-Failure $Failures "visual-smoke-evidence-report.json themeContrastChecksPassed must be true."
    }
    if ([decimal](Get-JsonProperty $VisualSmoke @("minimumContrastRatio")) -lt $minimumContrastRatio) {
        Add-Failure $Failures "visual-smoke-evidence-report.json minimumContrastRatio must be at least 3."
    }
    if ([string](Get-JsonProperty $VisualSmoke @("accessibilityCheck")) -ne $expectedAccessibilityCheck -or
        [string](Get-JsonProperty $VisualSmoke @("accessibilityChecksPassed")) -ne "True") {
        Add-Failure $Failures "visual-smoke-evidence-report.json accessibility check must be axe-wcag-2.2-a-aa and passed."
    }
    Assert-ArrayContainsExactly @((Get-JsonProperty $VisualSmoke @("accessibilityTags"))) $expectedAccessibilityTags "visual-smoke-evidence-report.json accessibilityTags" $Failures
    if ([int](Get-JsonProperty $VisualSmoke @("accessibilityCheckResultCount")) -ne 192 -or
        [int](Get-JsonProperty $VisualSmoke @("accessibilityViolationCount")) -ne 0) {
        Add-Failure $Failures "visual-smoke-evidence-report.json must retain 192 passed accessibility results with zero violations."
    }
    if ([string](Get-JsonProperty $VisualSmoke @("responsiveAcceptanceCheck")) -ne $expectedResponsiveAcceptanceCheck -or
        [string](Get-JsonProperty $VisualSmoke @("responsiveAcceptanceChecksPassed")) -ne "True" -or
        [int](Get-JsonProperty $VisualSmoke @("responsiveAcceptanceCheckResultCount")) -ne 192) {
        Add-Failure $Failures "visual-smoke-evidence-report.json must retain 192 passed responsive workflow acceptance results."
    }
    if ([int](Get-JsonProperty $VisualSmoke @("totalBytes")) -le 0) {
        Add-Failure $Failures "visual-smoke-evidence-report.json totalBytes must prove retained screenshot bytes."
    }

    $viewportDimensions = Get-JsonProperty $VisualSmoke @("viewportDimensions")
    if ($null -eq $viewportDimensions -or @($viewportDimensions).Count -eq 0) {
        Add-Failure $Failures "visual-smoke-evidence-report.json viewportDimensions must be present."
    } else {
        if (@($viewportDimensions).Count -ne $expectedViewports.Count) {
            Add-Failure $Failures "visual-smoke-evidence-report.json viewportDimensions must include exactly $($expectedViewports.Count) planned viewport(s)."
        }
        foreach ($expected in $expectedViewports) {
            $actual = @($viewportDimensions) | Where-Object { [string](Get-JsonProperty $_ @("name")) -eq $expected.name } | Select-Object -First 1
            if ($null -eq $actual) {
                Add-Failure $Failures "visual-smoke-evidence-report.json viewportDimensions must include $($expected.name)."
                continue
            }

            if ([int](Get-JsonProperty $actual @("width")) -ne [int]$expected.width -or
                [int](Get-JsonProperty $actual @("height")) -ne [int]$expected.height) {
                Add-Failure $Failures "visual-smoke-evidence-report.json viewportDimensions.$($expected.name) must be $($expected.width)x$($expected.height)."
            }
        }
    }

    $routeCoverage = Get-JsonProperty $VisualSmoke @("routeCoverage")
    if ($null -eq $routeCoverage -or @($routeCoverage).Count -eq 0) {
        Add-Failure $Failures "visual-smoke-evidence-report.json routeCoverage must be present."
    } else {
        if (@($routeCoverage).Count -ne $expectedVisualStateIds.Count) {
            Add-Failure $Failures "visual-smoke-evidence-report.json routeCoverage must include exactly 32 canonical state rows."
        }
        Assert-ArrayContainsExactly @($routeCoverage | ForEach-Object { [string](Get-JsonProperty $_ @("stateId")) }) $expectedVisualStateIds "visual-smoke-evidence-report.json routeCoverage.stateId" $Failures
        foreach ($stateId in $expectedVisualStateIds) {
            $actualRoute = @($routeCoverage) | Where-Object { [string](Get-JsonProperty $_ @("stateId")) -eq $stateId } | Select-Object -First 1
            if ($null -eq $actualRoute) {
                Add-Failure $Failures "visual-smoke-evidence-report.json routeCoverage must include $stateId."
                continue
            }
            if ([string](Get-JsonProperty $actualRoute @("routeName")) -ne $stateId) {
                Add-Failure $Failures "visual-smoke-evidence-report.json routeCoverage.$stateId.routeName must match stateId."
            }
            if ([int](Get-JsonProperty $actualRoute @("screenshotCount")) -ne 6) {
                Add-Failure $Failures "visual-smoke-evidence-report.json routeCoverage.$stateId.screenshotCount must be 6."
            }
            if ([string](Get-JsonProperty $actualRoute @("reviewStatus")) -ne "required-review") {
                Add-Failure $Failures "visual-smoke-evidence-report.json routeCoverage.$stateId.reviewStatus must be required-review."
            }
            foreach ($reviewCheck in $expectedAccountantWorkbenchReviewChecks) {
                Assert-ArrayContains @((Get-JsonProperty $actualRoute @("requiredReviewChecks"))) $reviewCheck "visual-smoke-evidence-report.json routeCoverage.$stateId.requiredReviewChecks" $Failures
            }
            Assert-NonEmptyString (Get-JsonProperty $actualRoute @("routeKey")) "visual-smoke-evidence-report.json routeCoverage.$stateId.routeKey" $Failures
            Assert-NonEmptyString (Get-JsonProperty $actualRoute @("uiState")) "visual-smoke-evidence-report.json routeCoverage.$stateId.uiState" $Failures
            Assert-NonEmptyString (Get-JsonProperty $actualRoute @("canonicalUrlTemplate")) "visual-smoke-evidence-report.json routeCoverage.$stateId.canonicalUrlTemplate" $Failures
            Assert-NonEmptyString (Get-JsonProperty $actualRoute @("canonicalTabState", "kind")) "visual-smoke-evidence-report.json routeCoverage.$stateId.canonicalTabState.kind" $Failures
            Assert-NonEmptyString (Get-JsonProperty $actualRoute @("expectedText")) "visual-smoke-evidence-report.json routeCoverage.$stateId.expectedText" $Failures
            Assert-NonEmptyString (Get-JsonProperty $actualRoute @("expectedStateText")) "visual-smoke-evidence-report.json routeCoverage.$stateId.expectedStateText" $Failures
        }
    }

    $screenshots = Get-JsonProperty $VisualSmoke @("screenshots")
    if ($null -eq $screenshots -or @($screenshots).Count -eq 0) {
        Add-Failure $Failures "visual-smoke-evidence-report.json screenshots must include PNG dimension evidence."
        return
    }

    if (@($screenshots).Count -ne 192) {
        Add-Failure $Failures "visual-smoke-evidence-report.json screenshots must include exactly 192 retained screenshots."
    }

    foreach ($stateId in $expectedVisualStateIds) {
        foreach ($theme in $expectedThemes) {
            foreach ($expectedViewport in $expectedViewports) {
                $expectedFileName = "$stateId-$theme-$($expectedViewport.name).png"
                $actualScreenshot = @($screenshots) | Where-Object {
                    [string](Get-JsonProperty $_ @("stateId")) -eq $stateId -and
                    [string](Get-JsonProperty $_ @("theme")) -eq [string]$theme -and
                    [string](Get-JsonProperty $_ @("viewportName")) -eq [string]$expectedViewport.name
                } | Select-Object -First 1

                if ($null -eq $actualScreenshot) {
                    Add-Failure $Failures "visual-smoke-evidence-report.json screenshots must include $stateId/$theme/$($expectedViewport.name)."
                    continue
                }
                if ([string](Get-JsonProperty $actualScreenshot @("fileName")) -ne $expectedFileName) {
                    Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.$stateId.$theme.$($expectedViewport.name).fileName must be $expectedFileName."
                }
                if ([string](Get-JsonProperty $actualScreenshot @("reviewStatus")) -ne "required-review") {
                    Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.$stateId.$theme.$($expectedViewport.name).reviewStatus must be required-review."
                }
            }
        }
    }

    $index = 0
    foreach ($screenshot in @($screenshots)) {
        $viewportName = [string](Get-JsonProperty $screenshot @("viewportName"))
        $expected = $expectedViewports | Where-Object { $_.name -eq $viewportName } | Select-Object -First 1
        if ($null -eq $expected) {
            Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.$index viewportName must be a planned viewport."
            $index += 1
            continue
        }

        $imageWidth = Get-JsonProperty $screenshot @("imageWidth")
        $imageHeight = Get-JsonProperty $screenshot @("imageHeight")
        $expectedViewportWidth = Get-JsonProperty $screenshot @("expectedViewportWidth")
        $minimumViewportHeight = Get-JsonProperty $screenshot @("minimumViewportHeight")
        $pixelSampleCount = Get-JsonProperty $screenshot @("pixelSampleCount")
        $sampledDistinctColorCount = Get-JsonProperty $screenshot @("sampledDistinctColorCount")
        $luminanceRange = Get-JsonProperty $screenshot @("luminanceRange")
        $byteSize = Get-JsonProperty $screenshot @("byteSize")
        $sha256 = [string](Get-JsonProperty $screenshot @("sha256"))
        $fileName = [string](Get-JsonProperty $screenshot @("fileName"))
        $pngIdatByteSize = Get-JsonProperty $screenshot @("pngIdatByteSize")
        $layoutCheckResults = @(Get-JsonProperty $screenshot @("layoutCheckResults"))
        $themeContrastResult = Get-JsonProperty $screenshot @("themeContrastResult")
        $accessibilityResult = Get-JsonProperty $screenshot @("accessibilityResult")
        $responsiveAcceptanceResult = Get-JsonProperty $screenshot @("responsiveAcceptanceResult")
        $stateId = [string](Get-JsonProperty $screenshot @("stateId"))
        $routeName = [string](Get-JsonProperty $screenshot @("routeName"))
        $materialRoute = Get-JsonProperty $screenshot @("materialRoute")
        $canonicalUrl = [string](Get-JsonProperty $screenshot @("canonicalUrl"))
        $observedUrl = [string](Get-JsonProperty $screenshot @("observedUrl"))
        $semanticContentSha256 = [string](Get-JsonProperty $screenshot @("semanticContentSha256"))
        $semanticContentByteSize = Get-JsonProperty $screenshot @("semanticContentByteSize")

        if ($stateId -notin $expectedVisualStateIds -or $routeName -ne $stateId) {
            Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.stateId and routeName must identify one canonical state."
        }
        foreach ($field in @("routeKey", "uiState", "authMode", "expectedText", "expectedStateText", "canonicalUrlTemplate")) {
            Assert-NonEmptyString (Get-JsonProperty $screenshot @($field)) "visual-smoke-evidence-report.json screenshots.$field" $Failures
        }
        if ([string](Get-JsonProperty $screenshot @("authMode")) -notin @("anonymous", "authenticated")) {
            Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.authMode must be anonymous or authenticated."
        }
        if ($null -ne $materialRoute -and -not [string]::IsNullOrWhiteSpace([string]$materialRoute) -and [string]$materialRoute -notin $expectedVisualMaterialRoutes) {
            Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.materialRoute must reference the canonical material-route inventory."
        }
        if ($null -eq (Get-JsonProperty $screenshot @("canonicalQuery"))) {
            Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.canonicalQuery must be present."
        }
        foreach ($field in @("kind", "id", "label")) {
            Assert-NonEmptyString (Get-JsonProperty $screenshot @("canonicalTabState", $field)) "visual-smoke-evidence-report.json screenshots.canonicalTabState.$field" $Failures
        }
        if ([string]::IsNullOrWhiteSpace($canonicalUrl) -or $canonicalUrl -ne $observedUrl) {
            Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.observedUrl must exactly match canonicalUrl."
        }
        if ($semanticContentSha256 -notmatch '^sha256:[0-9a-f]{64}$') {
            Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.semanticContentSha256 must be a canonical sha256 checksum."
        }
        if ($null -eq $semanticContentByteSize -or [int]$semanticContentByteSize -le 0) {
            Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.semanticContentByteSize must be greater than zero."
        }

        if ($null -eq $imageWidth -or [int]$imageWidth -ne [int]$expected.width) {
            Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.imageWidth must match planned viewport width."
        }
        if ($null -eq $expectedViewportWidth -or [int]$expectedViewportWidth -ne [int]$expected.width) {
            Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.expectedViewportWidth must match planned viewport width."
        }
        if ($null -eq $imageHeight -or [int]$imageHeight -lt [int]$expected.height) {
            Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.imageHeight must be at least the planned viewport height."
        }
        if ($null -eq $minimumViewportHeight -or [int]$minimumViewportHeight -ne [int]$expected.height) {
            Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.minimumViewportHeight must match planned viewport height."
        }
        if ($null -eq $byteSize -or [int]$byteSize -le 0) {
            Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.byteSize must prove retained screenshot bytes."
        }
        if ($sha256 -notmatch '^sha256:[0-9a-f]{64}$') {
            Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.sha256 must be a canonical sha256 checksum."
        }
        $screenshotPath = Find-RetainedVisualSmokeScreenshot $Directory $fileName $Failures
        if (-not [string]::IsNullOrWhiteSpace($screenshotPath)) {
            $screenshotInfo = Get-Item -LiteralPath $screenshotPath
            if ($null -ne $byteSize -and [int64]$byteSize -ne [int64]$screenshotInfo.Length) {
                Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.$fileName byteSize must match the retained PNG file."
            }

            $actualSha256 = "sha256:$(Get-FileSha256 $screenshotPath)"
            if ($sha256 -match '^sha256:[0-9a-f]{64}$' -and $sha256 -ne $actualSha256) {
                Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.$fileName sha256 must match the retained PNG file."
            }
        }
        if ($null -eq $pngIdatByteSize -or [int]$pngIdatByteSize -le 0) {
            Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.pngIdatByteSize must prove retained PNG image data."
        }
        if ($null -eq $pixelSampleCount -or [int]$pixelSampleCount -le 0) {
            Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.pixelSampleCount must be greater than zero."
        }
        if ($null -eq $sampledDistinctColorCount -or [int]$sampledDistinctColorCount -lt 4) {
            Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.sampledDistinctColorCount must be at least 4."
        }
        if ($null -eq $luminanceRange -or [int]$luminanceRange -lt 10) {
            Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.luminanceRange must be at least 10."
        }
        foreach ($layoutCheck in $expectedLayoutChecks) {
            $layoutResult = $layoutCheckResults |
                Where-Object { [string](Get-JsonProperty $_ @("check")) -eq $layoutCheck } |
                Select-Object -First 1
            if ($null -eq $layoutResult) {
                Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.layoutCheckResults must include $layoutCheck."
            } elseif ([string](Get-JsonProperty $layoutResult @("status")) -ne "passed") {
                Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.layoutCheckResults.$layoutCheck status must be passed."
            }
        }
        if ($null -eq $themeContrastResult) {
            Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.themeContrastResult must be present."
        } else {
            if ([string](Get-JsonProperty $themeContrastResult @("check")) -ne $expectedContrastCheck) {
                Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.themeContrastResult.check must be theme-contrast."
            }
            if ([string](Get-JsonProperty $themeContrastResult @("status")) -ne "passed") {
                Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.themeContrastResult.status must be passed."
            }
            if ([int](Get-JsonProperty $themeContrastResult @("sampledTextCount")) -le 0) {
                Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.themeContrastResult.sampledTextCount must be greater than zero."
            }
            if ([int](Get-JsonProperty $themeContrastResult @("failingTextCount")) -ne 0) {
                Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.themeContrastResult.failingTextCount must be zero."
            }
            if ($null -eq (Get-JsonProperty $themeContrastResult @("failingUiComponentCount")) -or
                [int](Get-JsonProperty $themeContrastResult @("failingUiComponentCount")) -ne 0) {
                Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.themeContrastResult.failingUiComponentCount must be present and zero."
            }
            if ([int](Get-JsonProperty $themeContrastResult @("sampledNormalTextCount")) -le 0) {
                Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.themeContrastResult.sampledNormalTextCount must be greater than zero."
            }
            if ([int](Get-JsonProperty $themeContrastResult @("sampledInteractiveTextCount")) -le 0) {
                Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.themeContrastResult.sampledInteractiveTextCount must be greater than zero."
            }
            $sampledUiComponentCount = [int](Get-JsonProperty $themeContrastResult @("sampledUiComponentCount"))
            $minimumUiComponentContrastRatio = [decimal](Get-JsonProperty $themeContrastResult @("minimumUiComponentContrastRatio"))
            if ($sampledUiComponentCount -le 0 -and $stateId -notin $uiComponentOptionalStateIds) {
                Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.themeContrastResult.sampledUiComponentCount must be greater than zero."
            }
            if ($sampledUiComponentCount -le 0 -and $minimumUiComponentContrastRatio -ne 0) {
                Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.themeContrastResult.minimumUiComponentContrastRatio must be zero when no UI component is rendered."
            }
            if ([decimal](Get-JsonProperty $themeContrastResult @("minimumContrastRatio")) -lt $minimumContrastRatio) {
                Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.themeContrastResult.minimumContrastRatio must be at least 3."
            }
            if ([decimal](Get-JsonProperty $themeContrastResult @("requiredMinimumContrastRatio")) -ne $minimumContrastRatio) {
                Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.themeContrastResult.requiredMinimumContrastRatio must be 3."
            }
            if ([decimal](Get-JsonProperty $themeContrastResult @("requiredNormalTextContrastRatio")) -ne 4.5) {
                Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.themeContrastResult.requiredNormalTextContrastRatio must be 4.5."
            }
            if ([decimal](Get-JsonProperty $themeContrastResult @("requiredLargeTextContrastRatio")) -ne 3) {
                Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.themeContrastResult.requiredLargeTextContrastRatio must be 3."
            }
            if ([decimal](Get-JsonProperty $themeContrastResult @("requiredUiComponentContrastRatio")) -ne 3) {
                Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.themeContrastResult.requiredUiComponentContrastRatio must be 3."
            }
            if ([decimal](Get-JsonProperty $themeContrastResult @("minimumNormalTextContrastRatio")) -lt 4.5) {
                Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.themeContrastResult.minimumNormalTextContrastRatio must be at least 4.5."
            }
            if ([decimal](Get-JsonProperty $themeContrastResult @("minimumLargeTextContrastRatio")) -lt 3) {
                Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.themeContrastResult.minimumLargeTextContrastRatio must be at least 3."
            }
            if ($sampledUiComponentCount -gt 0 -and $minimumUiComponentContrastRatio -lt 3) {
                Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.themeContrastResult.minimumUiComponentContrastRatio must be at least 3."
            }
        }

        if ($null -eq $accessibilityResult) {
            Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.accessibilityResult must be present."
        } else {
            if ([string](Get-JsonProperty $accessibilityResult @("check")) -ne $expectedAccessibilityCheck -or
                [string](Get-JsonProperty $accessibilityResult @("status")) -ne "passed" -or
                [string](Get-JsonProperty $accessibilityResult @("engine")) -ne "axe-core") {
                Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.accessibilityResult must be a passed axe-core WCAG check."
            }
            if ([string](Get-JsonProperty $accessibilityResult @("engineVersion")) -notmatch '^4\.12\.[0-9]+$' -or
                [string](Get-JsonProperty $accessibilityResult @("standard")) -ne "WCAG 2.2 A/AA") {
                Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.accessibilityResult must identify axe-core 4.12 and WCAG 2.2 A/AA."
            }
            Assert-ArrayContainsExactly @((Get-JsonProperty $accessibilityResult @("tags"))) $expectedAccessibilityTags "visual-smoke-evidence-report.json screenshots.accessibilityResult.tags" $Failures
            $accessibilityViolations = @((Get-JsonProperty $accessibilityResult @("violations")) | Where-Object { $null -ne $_ })
            if ([int](Get-JsonProperty $accessibilityResult @("violationCount")) -ne 0 -or
                $accessibilityViolations.Count -ne 0 -or
                [int](Get-JsonProperty $accessibilityResult @("passCount")) -le 0) {
                Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.accessibilityResult must retain zero violations and positive passes."
            }
        }

        if ($null -eq $responsiveAcceptanceResult) {
            Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.responsiveAcceptanceResult must be present."
        } else {
            if ([string](Get-JsonProperty $responsiveAcceptanceResult @("check")) -ne $expectedResponsiveAcceptanceCheck -or
                [string](Get-JsonProperty $responsiveAcceptanceResult @("status")) -ne "passed" -or
                [int](Get-JsonProperty $responsiveAcceptanceResult @("viewportHeight")) -ne [int]$expected.height -or
                [int](Get-JsonProperty $responsiveAcceptanceResult @("documentHeight")) -le 0) {
                Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.responsiveAcceptanceResult must be passed with captured dimensions."
            }
            $responsiveAssertions = @((Get-JsonProperty $responsiveAcceptanceResult @("assertions")) | Where-Object { [string](Get-JsonProperty $_ @("status")) -eq "passed" } | ForEach-Object { [string](Get-JsonProperty $_ @("check")) })
            if ($stateId -eq "dashboard") {
                Assert-ArrayContains $responsiveAssertions "dashboard-work-before-release-evidence" "visual-smoke-evidence-report.json dashboard responsive assertions" $Failures
            }
            if ($stateId -eq "dashboard" -and $viewportName -eq "desktop") {
                Assert-ArrayContains $responsiveAssertions "dashboard-first-action-within-desktop-viewport" "visual-smoke-evidence-report.json dashboard desktop responsive assertions" $Failures
            }
            if ($stateId -eq "production-readiness" -and $viewportName -eq "mobile") {
                foreach ($requiredAssertion in @("readiness-priority-surface-within-two-mobile-viewports", "readiness-initial-height-within-eight-mobile-viewports", "readiness-supporting-ledgers-collapsed-initially")) {
                    Assert-ArrayContains $responsiveAssertions $requiredAssertion "visual-smoke-evidence-report.json production readiness mobile responsive assertions" $Failures
                }
                if ([decimal](Get-JsonProperty $responsiveAcceptanceResult @("documentViewportRatio")) -gt 8) {
                    Add-Failure $Failures "visual-smoke-evidence-report.json production readiness mobile documentViewportRatio must not exceed 8."
                }
            }
        }

        $index += 1
    }
}

function Assert-VisualSmokeManifestEvidence {
    param(
        [object]$VisualManifest,
        [object]$VisualSmoke,
        [System.Collections.Generic.List[string]]$Failures
    )

    if ($VisualManifest.PSObject.Properties.Name -contains "__missing" -or
        $VisualManifest.PSObject.Properties.Name -contains "__invalid" -or
        $VisualSmoke.PSObject.Properties.Name -contains "__missing" -or
        $VisualSmoke.PSObject.Properties.Name -contains "__invalid") {
        return
    }

    if ([string](Get-JsonProperty $VisualManifest @("artifactName")) -ne "visual-smoke-screenshots") {
        Add-Failure $Failures "visual-smoke-manifest.json artifactName must be visual-smoke-screenshots."
    }
    if ([string](Get-JsonProperty $VisualManifest @("manifestFileName")) -ne "visual-smoke-manifest.json") {
        Add-Failure $Failures "visual-smoke-manifest.json manifestFileName must be visual-smoke-manifest.json."
    }
    if ([string](Get-JsonProperty $VisualManifest @("inventoryVersion")) -ne "canonical-material-states-v1") {
        Add-Failure $Failures "visual-smoke-manifest.json inventoryVersion must be canonical-material-states-v1."
    }
    if ([int](Get-JsonProperty $VisualManifest @("inventoryStateCount")) -ne 32) {
        Add-Failure $Failures "visual-smoke-manifest.json inventoryStateCount must be 32."
    }
    if ([int](Get-JsonProperty $VisualManifest @("expectedScreenshotCount")) -ne 192) {
        Add-Failure $Failures "visual-smoke-manifest.json expectedScreenshotCount must be 192."
    }

    Assert-ArrayContainsExactly @((Get-JsonProperty $VisualManifest @("layoutChecks"))) $expectedAccountantWorkbenchLayoutChecks "visual-smoke-manifest.json layoutChecks" $Failures
    if ([string](Get-JsonProperty $VisualManifest @("accessibilityCheck")) -ne "axe-wcag-2.2-a-aa" -or
        [string](Get-JsonProperty $VisualManifest @("responsiveAcceptanceCheck")) -ne "responsive-workflow-acceptance") {
        Add-Failure $Failures "visual-smoke-manifest.json must declare the canonical accessibility and responsive acceptance checks."
    }
    Assert-ArrayContainsExactly @((Get-JsonProperty $VisualManifest @("reviewChecks"))) $expectedAccountantWorkbenchReviewChecks "visual-smoke-manifest.json reviewChecks" $Failures
    Assert-ArrayContainsExactly @((Get-JsonProperty $VisualManifest @("themes"))) $expectedAccountantWorkbenchThemes "visual-smoke-manifest.json themes" $Failures
    Assert-ArrayContainsExactly @((Get-JsonProperty $VisualManifest @("requiredMaterialRoutes"))) $expectedVisualMaterialRoutes "visual-smoke-manifest.json requiredMaterialRoutes" $Failures
    Assert-ArrayContainsExactly @((Get-JsonProperty $VisualManifest @("requiredUiStates"))) $expectedVisualUiStates "visual-smoke-manifest.json requiredUiStates" $Failures

    $routeAudits = @(Get-JsonProperty $VisualManifest @("routeAudits"))
    $stateInventory = @(Get-JsonProperty $VisualManifest @("stateInventory"))
    if ($routeAudits.Count -ne 32 -or $stateInventory.Count -ne 32) {
        Add-Failure $Failures "visual-smoke-manifest.json routeAudits and stateInventory must each include exactly 32 canonical states."
    }
    Assert-ArrayContainsExactly @($routeAudits | ForEach-Object { [string](Get-JsonProperty $_ @("stateId")) }) $expectedVisualStateIds "visual-smoke-manifest.json routeAudits.stateId" $Failures
    Assert-ArrayContainsExactly @($stateInventory | ForEach-Object { [string](Get-JsonProperty $_ @("stateId")) }) $expectedVisualStateIds "visual-smoke-manifest.json stateInventory.stateId" $Failures

    foreach ($stateId in $expectedVisualStateIds) {
        $routeAudit = $routeAudits |
            Where-Object { [string](Get-JsonProperty $_ @("stateId")) -eq $stateId } |
            Select-Object -First 1

        if ($null -eq $routeAudit) {
            Add-Failure $Failures "visual-smoke-manifest.json routeAudits must include $stateId."
            continue
        }

        if ([string](Get-JsonProperty $routeAudit @("routeName")) -ne $stateId) {
            Add-Failure $Failures "visual-smoke-manifest.json routeAudits.$stateId.routeName must match stateId."
        }
        if ([int](Get-JsonProperty $routeAudit @("screenshotCount")) -ne 6) {
            Add-Failure $Failures "visual-smoke-manifest.json routeAudits.$stateId.screenshotCount must be 6."
        }
        if ([string](Get-JsonProperty $routeAudit @("reviewStatus")) -ne "required-review") {
            Add-Failure $Failures "visual-smoke-manifest.json routeAudits.$stateId.reviewStatus must be required-review."
        }
        Assert-ArrayContainsExactly @((Get-JsonProperty $routeAudit @("reviewChecks"))) $expectedAccountantWorkbenchReviewChecks "visual-smoke-manifest.json routeAudits.$stateId.reviewChecks" $Failures
        foreach ($field in @("routeKey", "label", "uiState", "canonicalUrlTemplate", "expectedText", "expectedStateText")) {
            Assert-NonEmptyString (Get-JsonProperty $routeAudit @($field)) "visual-smoke-manifest.json routeAudits.$stateId.$field" $Failures
        }
        foreach ($field in @("kind", "id", "label")) {
            Assert-NonEmptyString (Get-JsonProperty $routeAudit @("canonicalTabState", $field)) "visual-smoke-manifest.json routeAudits.$stateId.canonicalTabState.$field" $Failures
        }
        $inventoryRow = $stateInventory | Where-Object { [string](Get-JsonProperty $_ @("stateId")) -eq $stateId } | Select-Object -First 1
        if ($null -eq $inventoryRow -or
            ($inventoryRow | ConvertTo-Json -Depth 20 -Compress) -cne ($routeAudit | ConvertTo-Json -Depth 20 -Compress)) {
            Add-Failure $Failures "visual-smoke-manifest.json stateInventory.$stateId must exactly match routeAudits.$stateId."
        }
    }

    $manifestScreenshots = @(Get-JsonProperty $VisualManifest @("screenshots"))
    $evidenceScreenshots = @(Get-JsonProperty $VisualSmoke @("screenshots"))
    if ($manifestScreenshots.Count -ne 192) {
        Add-Failure $Failures "visual-smoke-manifest.json screenshots must include exactly 192 retained screenshots."
    }

    $expectedViewports = @(
        [pscustomobject]@{ name = "mobile"; width = 390; height = 844 },
        [pscustomobject]@{ name = "tablet"; width = 768; height = 1024 },
        [pscustomobject]@{ name = "desktop"; width = 1440; height = 1000 }
    )

    foreach ($stateId in $expectedVisualStateIds) {
        foreach ($theme in $expectedAccountantWorkbenchThemes) {
            foreach ($expectedViewport in $expectedViewports) {
                $expectedFileName = "$stateId-$theme-$($expectedViewport.name).png"
                $manifestScreenshot = $manifestScreenshots |
                    Where-Object {
                        [string](Get-JsonProperty $_ @("stateId")) -eq $stateId -and
                        [string](Get-JsonProperty $_ @("theme")) -eq [string]$theme -and
                        [string](Get-JsonProperty $_ @("viewportName")) -eq [string]$expectedViewport.name
                    } |
                    Select-Object -First 1
                $evidenceScreenshot = $evidenceScreenshots |
                    Where-Object {
                        [string](Get-JsonProperty $_ @("stateId")) -eq $stateId -and
                        [string](Get-JsonProperty $_ @("theme")) -eq [string]$theme -and
                        [string](Get-JsonProperty $_ @("viewportName")) -eq [string]$expectedViewport.name
                    } |
                    Select-Object -First 1

                if ($null -eq $manifestScreenshot) {
                    Add-Failure $Failures "visual-smoke-manifest.json screenshots must include $stateId/$theme/$($expectedViewport.name)."
                    continue
                }
                if ($null -eq $evidenceScreenshot) {
                    continue
                }

                if ([string](Get-JsonProperty $manifestScreenshot @("fileName")) -ne $expectedFileName) {
                    Add-Failure $Failures "visual-smoke-manifest.json screenshots.$stateId.$theme.$($expectedViewport.name).fileName must be $expectedFileName."
                }
                if ([IO.Path]::GetFileName([string](Get-JsonProperty $manifestScreenshot @("artifactPath"))) -ne $expectedFileName) {
                    Add-Failure $Failures "visual-smoke-manifest.json screenshots.$stateId.$theme.$($expectedViewport.name).artifactPath must end with $expectedFileName."
                }
                if ([string](Get-JsonProperty $manifestScreenshot @("reviewStatus")) -ne "required-review") {
                    Add-Failure $Failures "visual-smoke-manifest.json screenshots.$stateId.$theme.$($expectedViewport.name).reviewStatus must be required-review."
                }

                foreach ($field in @("stateId", "routeName", "routeKey", "materialRoute", "uiState", "authMode", "fileName", "expectedText", "expectedStateText", "canonicalUrlTemplate", "canonicalUrl", "observedUrl", "semanticContentSha256", "semanticContentByteSize", "reviewStatus", "byteSize", "sha256", "imageWidth", "minimumViewportHeight")) {
                    if ([string](Get-JsonProperty $manifestScreenshot @($field)) -ne [string](Get-JsonProperty $evidenceScreenshot @($field))) {
                        Add-Failure $Failures "visual-smoke-manifest.json screenshots.$stateId.$theme.$($expectedViewport.name).$field must match visual-smoke-evidence-report.json."
                    }
                }
            }
        }
    }
}

function Get-FileSha256 {
    param(
        [string]$Path
    )

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Assert-RestoreArtifactLinkage {
    param(
        [object]$RestoreEvidence,
        [string]$Directory,
        [string]$ExpectedCommitSha,
        [string]$ExpectedRunUrl,
        [System.Collections.Generic.List[string]]$Failures
    )

    $parseEvidenceTimestamp = {
        param([object]$Value, [ref]$Result)
        if ($Value -is [DateTimeOffset]) {
            $Result.Value = ([DateTimeOffset]$Value).ToUniversalTime()
            return $true
        }
        if ($Value -is [DateTime]) {
            $Result.Value = [DateTimeOffset]::new(([DateTime]$Value).ToUniversalTime())
            return $true
        }
        return [DateTimeOffset]::TryParse(
            [string]$Value,
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::RoundtripKind,
            $Result)
    }

    if ($RestoreEvidence.PSObject.Properties.Name -contains "__missing" -or
        $RestoreEvidence.PSObject.Properties.Name -contains "__invalid") {
        return
    }

    $restoreCommitSha = [string](Get-JsonProperty $RestoreEvidence @("releaseCandidate"))
    $restoreRunUrl = [string](Get-JsonProperty $RestoreEvidence @("githubActionsRunUrl"))
    if ($restoreCommitSha -cnotmatch '^[0-9a-f]{40}$') {
        Add-Failure $Failures "restore-drill-report.json releaseCandidate must be a full lowercase 40-character hexadecimal Git commit SHA."
    }
    if ($restoreRunUrl -cnotmatch '^https://github\.com/[^/\s]+/[^/\s]+/actions/runs/[0-9]+$') {
        Add-Failure $Failures "restore-drill-report.json githubActionsRunUrl must be an exact GitHub Actions run URL."
    }
    if ($ExpectedCommitSha.Length -gt 0 -and $restoreCommitSha -cne $ExpectedCommitSha) {
        Add-Failure $Failures "restore-drill-report.json releaseCandidate must exactly match the release-pack commit."
    }
    if ($ExpectedRunUrl.Length -gt 0 -and $restoreRunUrl -cne $ExpectedRunUrl) {
        Add-Failure $Failures "restore-drill-report.json githubActionsRunUrl must exactly match the release-pack run URL."
    }

    $retainedEntries = @(Get-ChildItem -LiteralPath $Directory -Recurse -Force)
    $linkedRetainedEntries = @($retainedEntries | Where-Object {
        ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        ($null -ne $_.PSObject.Properties['LinkType'] -and -not [string]::IsNullOrWhiteSpace([string]$_.LinkType))
    })
    if ($linkedRetainedEntries.Count -ne 0) {
        Add-Failure $Failures "Release evidence must be self-contained and must not include filesystem links: $(@($linkedRetainedEntries.Name) -join ', ')"
        return
    }
    $retainedFiles = @($retainedEntries | Where-Object { -not $_.PSIsContainer })
    $encryptedBackups = @($retainedFiles | Where-Object { $_.Name.EndsWith('.dump.cms', [StringComparison]::OrdinalIgnoreCase) })
    $checksumFiles = @($retainedFiles | Where-Object { $_.Name.EndsWith('.dump.cms.sha256', [StringComparison]::OrdinalIgnoreCase) })
    $manifestFiles = @($retainedFiles | Where-Object { $_.Name.EndsWith('.dump.cms.manifest.json', [StringComparison]::OrdinalIgnoreCase) })
    $plaintextBackups = @($retainedFiles | Where-Object { $_.Name.EndsWith('.dump', [StringComparison]::OrdinalIgnoreCase) })

    foreach ($candidate in $encryptedBackups) {
        if ($candidate.Name -cnotmatch '^[A-Za-z0-9_.-]+\.dump\.cms$') {
            Add-Failure $Failures "Encrypted PostgreSQL backup filenames must use the canonical safe .dump.cms form: $($candidate.Name)"
        }
    }
    foreach ($candidate in $checksumFiles) {
        if ($candidate.Name -cnotmatch '^[A-Za-z0-9_.-]+\.dump\.cms\.sha256$') {
            Add-Failure $Failures "Encrypted-backup checksum filenames must use the canonical safe .dump.cms.sha256 form: $($candidate.Name)"
        }
    }
    foreach ($candidate in $manifestFiles) {
        if ($candidate.Name -cnotmatch '^[A-Za-z0-9_.-]+\.dump\.cms\.manifest\.json$') {
            Add-Failure $Failures "Encrypted-backup manifest filenames must use the canonical safe .dump.cms.manifest.json form: $($candidate.Name)"
        }
    }
    foreach ($candidate in $plaintextBackups) {
        if ($candidate.Name -cnotmatch '^[A-Za-z0-9_.-]+\.dump$') {
            Add-Failure $Failures "Plaintext PostgreSQL dump filenames are forbidden even when their names are noncanonical: $($candidate.Name)"
        }
    }
    if ($encryptedBackups.Count -ne 1) {
        Add-Failure $Failures "Release artifact pack must retain exactly one encrypted PostgreSQL .dump.cms backup; found $($encryptedBackups.Count)."
    }
    if ($checksumFiles.Count -ne 1) {
        Add-Failure $Failures "Release artifact pack must retain exactly one encrypted-backup .sha256 sidecar; found $($checksumFiles.Count)."
    }
    if ($manifestFiles.Count -ne 1) {
        Add-Failure $Failures "Release artifact pack must retain exactly one encrypted-backup manifest; found $($manifestFiles.Count)."
    }
    if ($plaintextBackups.Count -ne 0) {
        Add-Failure $Failures "Release artifact pack must not retain plaintext PostgreSQL .dump files."
    }
    if ($encryptedBackups.Count -ne 1 -or $checksumFiles.Count -ne 1 -or $manifestFiles.Count -ne 1) {
        return
    }

    $backup = $encryptedBackups[0]
    $checksum = $checksumFiles[0]
    $manifestFile = $manifestFiles[0]
    $expectedChecksumFileName = "$($backup.Name).sha256"
    $expectedManifestFileName = "$($backup.Name).manifest.json"
    if ($checksum.Name -cne $expectedChecksumFileName -or $manifestFile.Name -cne $expectedManifestFileName) {
        Add-Failure $Failures "The retained PostgreSQL backup, checksum, and manifest must form one exact adjacent filename set."
    }
    if (([IO.Path]::GetFullPath($checksum.DirectoryName) -cne [IO.Path]::GetFullPath($backup.DirectoryName)) -or
        ([IO.Path]::GetFullPath($manifestFile.DirectoryName) -cne [IO.Path]::GetFullPath($backup.DirectoryName))) {
        Add-Failure $Failures "The retained PostgreSQL backup, checksum, and manifest must be adjacent in one evidence directory."
    }

    if ([string](Get-JsonProperty $RestoreEvidence @("backupFileName")) -cne $backup.Name) {
        Add-Failure $Failures "restore-drill-report.json backupFileName must exactly match the retained encrypted backup."
    }
    if ([int64](Get-JsonProperty $RestoreEvidence @("backupByteSize")) -ne [int64]$backup.Length -or $backup.Length -le 0) {
        Add-Failure $Failures "restore-drill-report.json backupByteSize must match the non-empty retained encrypted backup."
    }

    $backupSha256 = [string](Get-JsonProperty $RestoreEvidence @("backupSha256"))
    if ($backupSha256 -cnotmatch '^[0-9a-f]{64}$' -or (Get-FileSha256 $backup.FullName) -cne $backupSha256) {
        Add-Failure $Failures "restore-drill-report.json backupSha256 must match the retained encrypted backup."
    }

    if ([string](Get-JsonProperty $RestoreEvidence @("backupChecksumFileName")) -cne $checksum.Name) {
        Add-Failure $Failures "restore-drill-report.json backupChecksumFileName must exactly match the retained checksum sidecar."
    }
    $checksumSha256 = [string](Get-JsonProperty $RestoreEvidence @("backupChecksumSha256"))
    if ($checksumSha256 -cnotmatch '^[0-9a-f]{64}$' -or (Get-FileSha256 $checksum.FullName) -cne $checksumSha256) {
        Add-Failure $Failures "restore-drill-report.json backupChecksumSha256 must match the retained checksum sidecar."
    }
    $expectedChecksumLine = "$backupSha256  $($backup.Name)"
    if ([IO.File]::ReadAllText($checksum.FullName) -cne $expectedChecksumLine) {
        Add-Failure $Failures "The retained checksum sidecar must contain the exact backup SHA-256 and filename."
    }

    if ([string](Get-JsonProperty $RestoreEvidence @("backupManifestFileName")) -cne $manifestFile.Name) {
        Add-Failure $Failures "restore-drill-report.json backupManifestFileName must exactly match the retained backup manifest."
    }
    $manifestSha256 = [string](Get-JsonProperty $RestoreEvidence @("backupManifestSha256"))
    if ($manifestSha256 -cnotmatch '^[0-9a-f]{64}$' -or (Get-FileSha256 $manifestFile.FullName) -cne $manifestSha256) {
        Add-Failure $Failures "restore-drill-report.json backupManifestSha256 must match the retained backup manifest."
    }

    try {
        $manifest = Get-Content -LiteralPath $manifestFile.FullName -Raw | ConvertFrom-Json
    } catch {
        Add-Failure $Failures "The retained PostgreSQL backup manifest must be valid JSON."
        return
    }

    $manifestReleaseCandidate = [string](Get-JsonProperty $manifest @("releaseCandidate"))
    if ([string](Get-JsonProperty $RestoreEvidence @("backupManifestReleaseCandidate")) -cne $manifestReleaseCandidate -or
        $manifestReleaseCandidate -cne $restoreCommitSha) {
        Add-Failure $Failures "The restore report and retained backup manifest must identify the same exact release candidate."
    }
    if ([string](Get-JsonProperty $manifest @("backupFileName")) -cne $backup.Name -or
        [string](Get-JsonProperty $manifest @("backupSha256")) -cne $backupSha256 -or
        [int64](Get-JsonProperty $manifest @("byteSize")) -ne [int64]$backup.Length) {
        Add-Failure $Failures "The retained backup manifest filename, SHA-256, and byte size must match the encrypted backup."
    }
    if ((Get-JsonProperty $manifest @("encrypted")) -ne $true -or
        [string](Get-JsonProperty $manifest @("encryptionAlgorithm")) -cne "CMS/AES-256-CBC" -or
        (Get-JsonProperty $manifest @("plaintextDumpRetained")) -ne $false) {
        Add-Failure $Failures "The retained backup manifest must prove CMS/AES-256-CBC encryption with no plaintext dump retained."
    }

    $recoveryMetrics = Get-JsonProperty $RestoreEvidence @("recoveryMetrics")
    $backupCreatedAtUtc = [DateTimeOffset]::MinValue
    $drillStartedAtUtc = [DateTimeOffset]::MinValue
    $drillCompletedAtUtc = [DateTimeOffset]::MinValue
    $timestampsValid =
        (& $parseEvidenceTimestamp (Get-JsonProperty $recoveryMetrics @("backupCreatedAtUtc")) ([ref]$backupCreatedAtUtc)) -and
        (& $parseEvidenceTimestamp (Get-JsonProperty $recoveryMetrics @("drillStartedAtUtc")) ([ref]$drillStartedAtUtc)) -and
        (& $parseEvidenceTimestamp (Get-JsonProperty $recoveryMetrics @("drillCompletedAtUtc")) ([ref]$drillCompletedAtUtc))
    if (-not $timestampsValid) {
        Add-Failure $Failures "restore-drill-report.json recoveryMetrics must contain valid backup, drill-start, and drill-completion timestamps."
    } else {
        if ($backupCreatedAtUtc.Offset -ne [TimeSpan]::Zero -or
            $drillStartedAtUtc.Offset -ne [TimeSpan]::Zero -or
            $drillCompletedAtUtc.Offset -ne [TimeSpan]::Zero) {
            Add-Failure $Failures "restore-drill-report.json recoveryMetrics timestamps must use UTC."
        }
        if ($backupCreatedAtUtc -gt $drillStartedAtUtc -or $drillStartedAtUtc -gt $drillCompletedAtUtc) {
            Add-Failure $Failures "restore-drill-report.json recoveryMetrics timestamps must be ordered backup-created, drill-started, drill-completed."
        }

        $manifestCreatedAtUtc = [DateTimeOffset]::MinValue
        $reportCompletedAtUtc = [DateTimeOffset]::MinValue
        $identityTimestampsValid =
            (& $parseEvidenceTimestamp (Get-JsonProperty $manifest @("createdAtUtc")) ([ref]$manifestCreatedAtUtc)) -and
            (& $parseEvidenceTimestamp (Get-JsonProperty $RestoreEvidence @("completedAtUtc")) ([ref]$reportCompletedAtUtc))
        if (-not $identityTimestampsValid -or
            $manifestCreatedAtUtc.Offset -ne [TimeSpan]::Zero -or
            $reportCompletedAtUtc.Offset -ne [TimeSpan]::Zero -or
            $manifestCreatedAtUtc -ne $backupCreatedAtUtc -or
            $reportCompletedAtUtc -ne $drillCompletedAtUtc) {
            Add-Failure $Failures "restore-drill-report.json recoveryMetrics timestamps must bind exactly to the retained manifest creation time and report completion time."
        }

        $rpoSeconds = [double](Get-JsonProperty $recoveryMetrics @("rpoSecondsAtDrill"))
        $rtoSeconds = [double](Get-JsonProperty $recoveryMetrics @("rtoSeconds"))
        $rpoTargetSeconds = [double](Get-JsonProperty $recoveryMetrics @("rpoTargetSeconds"))
        $rtoTargetSeconds = [double](Get-JsonProperty $recoveryMetrics @("rtoTargetSeconds"))
        $calculatedRpoSeconds = [Math]::Round(($drillStartedAtUtc - $backupCreatedAtUtc).TotalSeconds, 3)
        $calculatedRtoSeconds = [Math]::Round(($drillCompletedAtUtc - $drillStartedAtUtc).TotalSeconds, 3)
        if ($rpoSeconds -lt 0 -or $rtoSeconds -lt 0 -or
            [Math]::Abs($rpoSeconds - $calculatedRpoSeconds) -gt 0.0005 -or
            [Math]::Abs($rtoSeconds - $calculatedRtoSeconds) -gt 0.0005) {
            Add-Failure $Failures "restore-drill-report.json recoveryMetrics RPO/RTO measurements must be non-negative and match the ordered timestamps."
        }
        if ($rpoTargetSeconds -le 0 -or $rtoTargetSeconds -le 0) {
            Add-Failure $Failures "restore-drill-report.json recoveryMetrics RPO/RTO targets must be positive."
        }
        if ((Get-JsonProperty $recoveryMetrics @("rpoTargetMet")) -ne ($rpoSeconds -ge 0 -and $rpoSeconds -le $rpoTargetSeconds) -or
            (Get-JsonProperty $recoveryMetrics @("rtoTargetMet")) -ne ($rtoSeconds -ge 0 -and $rtoSeconds -le $rtoTargetSeconds)) {
            Add-Failure $Failures "restore-drill-report.json recoveryMetrics target results must match the retained RPO/RTO measurements."
        }
    }
}

function Assert-SupplyChainRetainedFile {
    param(
        [object]$FileEvidence,
        [object]$SupplyChainReport,
        [string]$Context,
        [System.Collections.Generic.List[string]]$Failures
    )

    $fileName = [string](Get-JsonProperty $FileEvidence @("fileName"))
    if ([string]::IsNullOrWhiteSpace($fileName) -or [IO.Path]::GetFileName($fileName) -ne $fileName) {
        Add-Failure $Failures "$Context fileName must be a safe retained filename."
        return
    }

    $supplyChainDirectory = Split-Path -Parent ([string](Get-JsonProperty $SupplyChainReport @("__path")))
    $path = Join-Path $supplyChainDirectory $fileName
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        Add-Failure $Failures "$Context retained file is missing: $fileName"
        return
    }

    $file = Get-Item -LiteralPath $path
    if ($file.Length -ne [long](Get-JsonProperty $FileEvidence @("byteSize"))) {
        Add-Failure $Failures "$Context retained byte size does not match $fileName."
    }
    if ((Get-FileSha256 $path) -cne [string](Get-JsonProperty $FileEvidence @("sha256"))) {
        Add-Failure $Failures "$Context retained SHA-256 does not match $fileName."
    }
}

function Assert-ContainerSupplyChainEvidence {
    param(
        [object]$SupplyChain,
        [object]$Verification,
        [string]$ExpectedCommitSha,
        [string]$ExpectedRunUrl,
        [System.Collections.Generic.List[string]]$Failures
    )

    foreach ($evidence in @($SupplyChain, $Verification)) {
        if ($evidence.PSObject.Properties.Name -contains "__missing" -or
            $evidence.PSObject.Properties.Name -contains "__invalid") {
            return
        }
    }

    if ([string](Get-JsonProperty $SupplyChain @("promotionMode")) -ne "promoted") {
        Add-Failure $Failures "container-supply-chain-report.json promotionMode must be promoted."
    }
    Assert-Truthy (Get-JsonProperty $SupplyChain @("releaseEligible")) "container-supply-chain-report.json releaseEligible" $Failures
    foreach ($pathText in @(
        "policy.buildOncePerComponent",
        "policy.immutableRegistryDigestsRequired",
        "policy.scanExactProductionReferences",
        "policy.githubProvenanceRequired",
        "policy.productionSmokeMustPullExactDigests",
        "controls.registryCredentialsAvailable",
        "controls.backendAndMigrationUseSameDigest",
        "controls.productionSmokeVerified",
        "controls.productionSmokeUsedExactDigestReferences"
    )) {
        $path = $pathText.Split('.')
        Assert-Truthy (Get-JsonProperty $SupplyChain $path) "container-supply-chain-report.json $pathText" $Failures
    }
    if ((Get-JsonProperty $SupplyChain @("controls", "mutableProductionTagsUsed")) -ne $false -or
        (Get-JsonProperty $SupplyChain @("controls", "localVerificationTagsUsed")) -ne $false) {
        Add-Failure $Failures "container-supply-chain-report.json must prove no mutable production or local verification tags were used for promotion."
    }
    if ([string](Get-JsonProperty $SupplyChain @("policy", "sbomFormat")) -ne "spdx-json") {
        Add-Failure $Failures "container-supply-chain-report.json policy.sbomFormat must be spdx-json."
    }
    $severities = @((Get-JsonProperty $SupplyChain @("policy", "failOnSeverities")))
    if ($severities.Count -ne 2 -or -not ($severities -contains "HIGH") -or -not ($severities -contains "CRITICAL")) {
        Add-Failure $Failures "container-supply-chain-report.json policy.failOnSeverities must contain exactly HIGH and CRITICAL."
    }
    if (@((Get-JsonProperty $SupplyChain @("blockingFailures"))).Count -ne 0) {
        Add-Failure $Failures "container-supply-chain-report.json blockingFailures must be empty for promoted evidence."
    }

    $candidateCommit = [string](Get-JsonProperty $SupplyChain @("candidate", "commitSha"))
    $candidateRunUrl = [string](Get-JsonProperty $SupplyChain @("candidate", "githubActionsRunUrl"))
    if ($candidateCommit -cnotmatch '^[0-9a-f]{40}$') {
        Add-Failure $Failures "container-supply-chain-report.json candidate.commitSha must be a full lowercase commit SHA."
    } elseif (-not [string]::IsNullOrWhiteSpace($ExpectedCommitSha) -and $candidateCommit -cne $ExpectedCommitSha) {
        Add-Failure $Failures "container-supply-chain-report.json candidate.commitSha must match the exact release commit."
    }
    if ($candidateRunUrl -notmatch '^https://github\.com/[^/]+/[^/]+/actions/runs/[0-9]+$') {
        Add-Failure $Failures "container-supply-chain-report.json candidate.githubActionsRunUrl must be an exact GitHub Actions run URL."
    } elseif (-not [string]::IsNullOrWhiteSpace($ExpectedRunUrl) -and $candidateRunUrl -cne $ExpectedRunUrl) {
        Add-Failure $Failures "container-supply-chain-report.json candidate.githubActionsRunUrl must match the exact workflow run."
    }

    $images = @((Get-JsonProperty $SupplyChain @("images")))
    if ($images.Count -ne 2 -or (@($images | ForEach-Object { [string](Get-JsonProperty $_ @("component")) } | Sort-Object) -join ",") -ne "backend,frontend") {
        Add-Failure $Failures "container-supply-chain-report.json images must contain exactly backend and frontend."
    }
    $exactReferences = [System.Collections.Generic.List[string]]::new()
    $digests = [System.Collections.Generic.List[string]]::new()
    $retainedFileNames = [System.Collections.Generic.List[string]]::new()
    foreach ($image in $images) {
        $component = [string](Get-JsonProperty $image @("component"))
        $context = "container-supply-chain-report.json images.$component"
        $imageName = [string](Get-JsonProperty $image @("imageName"))
        $digest = [string](Get-JsonProperty $image @("digest"))
        $exactReference = "$imageName@$digest"
        $exactReferences.Add($exactReference) | Out-Null
        $digests.Add($digest) | Out-Null
        if ($imageName -cnotmatch '^ghcr\.io/[a-z0-9._/-]+$' -or $digest -cnotmatch '^sha256:[0-9a-f]{64}$') {
            Add-Failure $Failures "$context must identify a lowercase tag-free GHCR image and sha256 digest."
        }
        foreach ($property in @("exactDigestReference", "productionSmokeReference")) {
            if ([string](Get-JsonProperty $image @($property)) -cne $exactReference) {
                Add-Failure $Failures "$context.$property must equal imageName@digest."
            }
        }
        if ([int](Get-JsonProperty $image @("builtInvocationCount")) -ne 1) {
            Add-Failure $Failures "$context.builtInvocationCount must be exactly 1."
        }
        foreach ($property in @("pushedToRegistry", "pulledForSmoke")) {
            Assert-Truthy (Get-JsonProperty $image @($property)) "$context.$property" $Failures
        }
        if ([string](Get-JsonProperty $image @("scan", "imageReference")) -cne $exactReference -or
            [string](Get-JsonProperty $image @("scan", "scanner")) -ne "Trivy" -or
            [int](Get-JsonProperty $image @("scan", "highCriticalVulnerabilityCount")) -ne 0) {
            Add-Failure $Failures "$context.scan must be a passing zero HIGH/CRITICAL Trivy scan of the exact digest."
        }
        Assert-Truthy (Get-JsonProperty $image @("scan", "passed")) "$context.scan.passed" $Failures
        if ([string](Get-JsonProperty $image @("sbom", "format")) -ne "spdx-json" -or
            [string](Get-JsonProperty $image @("sbom", "spdxVersion")) -notmatch '^SPDX-') {
            Add-Failure $Failures "$context.sbom must be SPDX JSON."
        }
        Assert-Truthy (Get-JsonProperty $image @("provenance", "attested")) "$context.provenance.attested" $Failures
        if ([string](Get-JsonProperty $image @("provenance", "attestationUrl")) -notmatch '^https://github\.com/.+/attestations/[0-9]+$') {
            Add-Failure $Failures "$context.provenance.attestationUrl must be a GitHub attestation URL."
        }
        Assert-SupplyChainRetainedFile (Get-JsonProperty $image @("scan", "file")) $SupplyChain "$context.scan" $Failures
        Assert-SupplyChainRetainedFile (Get-JsonProperty $image @("sbom", "file")) $SupplyChain "$context.sbom" $Failures
        Assert-SupplyChainRetainedFile (Get-JsonProperty $image @("provenance", "file")) $SupplyChain "$context.provenance" $Failures
        foreach ($fileKind in @("scan", "sbom", "provenance")) {
            $retainedFileNames.Add([string](Get-JsonProperty $image @($fileKind, "file", "fileName"))) | Out-Null
        }
    }
    if (@($digests | Select-Object -Unique).Count -ne 2) {
        Add-Failure $Failures "container-supply-chain-report.json backend and frontend digests must be distinct."
    }
    if ($retainedFileNames.Count -ne 6 -or @($retainedFileNames | Select-Object -Unique).Count -ne 6) {
        Add-Failure $Failures "container-supply-chain-report.json must retain six distinct scan, SBOM and provenance files."
    }

    if ([string](Get-JsonProperty $Verification @("promotionMode")) -ne "promoted" -or
        (Get-JsonProperty $Verification @("allowUnpromoted")) -ne $false) {
        Add-Failure $Failures "container-supply-chain-verification-report.json must be a strict promoted verification."
    }
    Assert-Truthy (Get-JsonProperty $Verification @("releaseEligible")) "container-supply-chain-verification-report.json releaseEligible" $Failures
    if ([string](Get-JsonProperty $Verification @("commitSha")) -cne $candidateCommit -or
        [string](Get-JsonProperty $Verification @("githubActionsRunUrl")) -cne $candidateRunUrl) {
        Add-Failure $Failures "container-supply-chain-verification-report.json candidate identity must match the exact release candidate."
    }
    $sourcePath = [string](Get-JsonProperty $SupplyChain @("__path"))
    if ([string](Get-JsonProperty $Verification @("evidenceReport", "fileName")) -ne "container-supply-chain-report.json" -or
        [long](Get-JsonProperty $Verification @("evidenceReport", "byteSize")) -ne (Get-Item -LiteralPath $sourcePath).Length -or
        [string](Get-JsonProperty $Verification @("evidenceReport", "sha256")) -cne (Get-FileSha256 $sourcePath)) {
        Add-Failure $Failures "container-supply-chain-verification-report.json evidenceReport must hash the retained supply-chain report."
    }
    $verifiedReferences = @((Get-JsonProperty $Verification @("verifiedImageDigests")) | ForEach-Object { [string]$_ } | Sort-Object)
    if (($verifiedReferences -join ",") -cne (@($exactReferences | Sort-Object) -join ",")) {
        Add-Failure $Failures "container-supply-chain-verification-report.json verifiedImageDigests must match both promoted image digests."
    }
    $verifiedFiles = @((Get-JsonProperty $Verification @("retainedEvidenceFiles")) | ForEach-Object { [string](Get-JsonProperty $_ @("fileName")) } | Sort-Object)
    if (($verifiedFiles -join ",") -cne (@($retainedFileNames | Sort-Object) -join ",")) {
        Add-Failure $Failures "container-supply-chain-verification-report.json retainedEvidenceFiles must match all six retained image evidence files."
    }
}

function Assert-ReleaseEvidenceTemplateManifest {
    param(
        [object]$ReleaseEvidence,
        [string]$Directory,
        [object[]]$RequiredTemplates,
        [System.Collections.Generic.List[string]]$Failures
    )

    if ($ReleaseEvidence.PSObject.Properties.Name -contains "__missing" -or
        $ReleaseEvidence.PSObject.Properties.Name -contains "__invalid") {
        return
    }

    $manifest = @(Get-JsonProperty $ReleaseEvidence @("evidenceFiles"))
    if ($manifest.Count -eq 0) {
        Add-Failure $Failures "release-evidence-report.json evidenceFiles must include retained release evidence template hashes."
        return
    }

    foreach ($required in $RequiredTemplates) {
        Assert-ArrayContains @((Get-JsonProperty $ReleaseEvidence @("requiredCoverage", "releaseEvidenceTemplateFiles"))) $required.fileName "release-evidence-report.json requiredCoverage.releaseEvidenceTemplateFiles" $Failures

        $entry = $manifest |
            Where-Object {
                [string](Get-JsonProperty $_ @("fileName")) -eq [string]$required.fileName -and
                [string](Get-JsonProperty $_ @("evidenceName")) -eq [string]$required.evidenceName
            } |
            Select-Object -First 1

        if ($null -eq $entry) {
            Add-Failure $Failures "release-evidence-report.json evidenceFiles must include $($required.fileName)."
            continue
        }

        if ((Get-JsonProperty $entry @("present")) -ne $true) {
            Add-Failure $Failures "release-evidence-report.json evidenceFiles.$($required.fileName).present must be true."
        }

        $manifestSha = [string](Get-JsonProperty $entry @("sha256"))
        if ($manifestSha -notmatch '^[0-9a-f]{64}$') {
            Add-Failure $Failures "release-evidence-report.json evidenceFiles.$($required.fileName).sha256 must be a lowercase SHA-256 hash."
        }

        $manifestByteSize = Get-JsonProperty $entry @("byteSize")
        if ($null -eq $manifestByteSize -or [int]$manifestByteSize -le 0) {
            Add-Failure $Failures "release-evidence-report.json evidenceFiles.$($required.fileName).byteSize must be greater than zero."
        }

        $templatePath = Join-Path $Directory $required.fileName
        if (-not (Test-Path -LiteralPath $templatePath -PathType Leaf)) {
            Add-Failure $Failures "Release artifact pack must include completed release evidence template: $($required.fileName)"
            continue
        }

        $templateInfo = Get-Item -LiteralPath $templatePath
        if ($null -ne $manifestByteSize -and [int64]$manifestByteSize -ne [int64]$templateInfo.Length) {
            Add-Failure $Failures "release-evidence-report.json evidenceFiles.$($required.fileName).byteSize must match the retained template file."
        }

        $actualSha = Get-FileSha256 $templatePath
        if ($manifestSha -match '^[0-9a-f]{64}$' -and $manifestSha -ne $actualSha) {
            Add-Failure $Failures "release-evidence-report.json evidenceFiles.$($required.fileName).sha256 must match the retained template file."
        }
    }
}

function Assert-ReleaseEvidenceHumanCompletionManifest {
    param(
        [object]$ReleaseEvidence,
        [object[]]$RequiredTemplates,
        [System.Collections.Generic.List[string]]$Failures
    )

    if ($ReleaseEvidence.PSObject.Properties.Name -contains "__missing" -or
        $ReleaseEvidence.PSObject.Properties.Name -contains "__invalid") {
        return
    }

    $completion = @(Get-JsonProperty $ReleaseEvidence @("humanEvidenceCompletion"))
    if ($completion.Count -eq 0) {
        Add-Failure $Failures "release-evidence-report.json humanEvidenceCompletion must include completed human release evidence gate entries."
        return
    }

    if ($completion.Count -ne $RequiredTemplates.Count) {
        Add-Failure $Failures "release-evidence-report.json humanEvidenceCompletion must include exactly $($RequiredTemplates.Count) completed human evidence gate entries."
    }

    foreach ($required in $RequiredTemplates) {
        $entry = $completion |
            Where-Object {
                [string](Get-JsonProperty $_ @("evidenceName")) -eq [string]$required.evidenceName -and
                [string](Get-JsonProperty $_ @("templateFile")) -eq [string]$required.fileName
            } |
            Select-Object -First 1

        if ($null -eq $entry) {
            Add-Failure $Failures "release-evidence-report.json humanEvidenceCompletion must include $($required.evidenceName) for $($required.fileName)."
            continue
        }

        if ([string](Get-JsonProperty $entry @("requiredReviewerRole")) -ne [string]$required.requiredReviewerRole) {
            Add-Failure $Failures "release-evidence-report.json humanEvidenceCompletion.$($required.evidenceName).requiredReviewerRole must be $($required.requiredReviewerRole)."
        }

        if ([string](Get-JsonProperty $entry @("signOffGate")) -ne [string]$required.signOffGate) {
            Add-Failure $Failures "release-evidence-report.json humanEvidenceCompletion.$($required.evidenceName).signOffGate must be $($required.signOffGate)."
        }

        if ((Get-JsonProperty $entry @("present")) -ne $true) {
            Add-Failure $Failures "release-evidence-report.json humanEvidenceCompletion.$($required.evidenceName).present must be true."
        }

        if ((Get-JsonProperty $entry @("hasReleaseIdentity")) -ne $true) {
            Add-Failure $Failures "release-evidence-report.json humanEvidenceCompletion.$($required.evidenceName).hasReleaseIdentity must be true."
        }

        if ([string](Get-JsonProperty $entry @("status")) -ne "accepted") {
            Add-Failure $Failures "release-evidence-report.json humanEvidenceCompletion.$($required.evidenceName).status must be accepted."
        }

        if ([int](Get-JsonProperty $entry @("blockingFailureCount")) -ne 0) {
            Add-Failure $Failures "release-evidence-report.json humanEvidenceCompletion.$($required.evidenceName).blockingFailureCount must be 0."
        }

        if (@((Get-JsonProperty $entry @("blockingFailures"))).Count -ne 0) {
            Add-Failure $Failures "release-evidence-report.json humanEvidenceCompletion.$($required.evidenceName).blockingFailures must be empty."
        }
    }
}

function Assert-ReleaseEvidenceProductionScorecardCompletion {
    param(
        [object]$ReleaseEvidence,
        [object[]]$RequiredTemplates,
        [System.Collections.Generic.List[string]]$Failures
    )

    if ($ReleaseEvidence.PSObject.Properties.Name -contains "__missing" -or
        $ReleaseEvidence.PSObject.Properties.Name -contains "__invalid") {
        return
    }

    $scorecard = Get-JsonProperty $ReleaseEvidence @("productionScorecardCompletion")
    if ($null -eq $scorecard) {
        Add-Failure $Failures "release-evidence-report.json productionScorecardCompletion must be present."
        return
    }

    if ([string](Get-JsonProperty $scorecard @("status")) -ne "complete") {
        Add-Failure $Failures "release-evidence-report.json productionScorecardCompletion.status must be complete."
    }
    if ([int](Get-JsonProperty $scorecard @("currentScore")) -ne 1000) {
        Add-Failure $Failures "release-evidence-report.json productionScorecardCompletion.currentScore must be 1000."
    }
    if ([int](Get-JsonProperty $scorecard @("targetScore")) -ne 1000) {
        Add-Failure $Failures "release-evidence-report.json productionScorecardCompletion.targetScore must be 1000."
    }
    if ([string](Get-JsonProperty $scorecard @("scoreBasis")) -ne "independent-audit-control-ledger-v1") {
        Add-Failure $Failures "release-evidence-report.json productionScorecardCompletion.scoreBasis must be independent-audit-control-ledger-v1."
    }
    if ([string](Get-JsonProperty $scorecard @("auditedCommit")) -ne "7ea54cc6d1769ced568ac1568d190cc2bb4b16d1") {
        Add-Failure $Failures "release-evidence-report.json productionScorecardCompletion.auditedCommit must identify the exact independently audited baseline commit."
    }
    if ([string](Get-JsonProperty $scorecard @("evidencePolicy")) -notlike "*exact live candidate*" -or
        [string](Get-JsonProperty $scorecard @("evidencePolicy")) -notlike "*artifact hashes*") {
        Add-Failure $Failures "release-evidence-report.json productionScorecardCompletion.evidencePolicy must require exact live candidate artifact hashes."
    }
    if ([int](Get-JsonProperty $scorecard @("openEngineeringOrAssuranceControlCount")) -ne 0) {
        Add-Failure $Failures "release-evidence-report.json productionScorecardCompletion.openEngineeringOrAssuranceControlCount must be zero."
    }
    if (@((Get-JsonProperty $scorecard @("openEngineeringOrAssuranceControls"))).Count -ne 0) {
        Add-Failure $Failures "release-evidence-report.json productionScorecardCompletion.openEngineeringOrAssuranceControls must be empty."
    }
    if ([int](Get-JsonProperty $scorecard @("acceptedHumanEvidenceCount")) -ne $RequiredTemplates.Count) {
        Add-Failure $Failures "release-evidence-report.json productionScorecardCompletion.acceptedHumanEvidenceCount must equal the required human evidence count."
    }
    if ([int](Get-JsonProperty $scorecard @("requiredHumanEvidenceCount")) -ne $RequiredTemplates.Count) {
        Add-Failure $Failures "release-evidence-report.json productionScorecardCompletion.requiredHumanEvidenceCount must equal the required human evidence count."
    }
    if (@((Get-JsonProperty $scorecard @("remainingHumanEvidence"))).Count -ne 0) {
        Add-Failure $Failures "release-evidence-report.json productionScorecardCompletion.remainingHumanEvidence must be empty."
    }
    if ([string](Get-JsonProperty $scorecard @("completionPolicy")) -notlike "*zero blocking failures*") {
        Add-Failure $Failures "release-evidence-report.json productionScorecardCompletion.completionPolicy must mention zero blocking failures."
    }

    foreach ($required in $RequiredTemplates) {
        Assert-ArrayContains @((Get-JsonProperty $scorecard @("acceptedHumanEvidence"))) ([string]$required.evidenceName) "release-evidence-report.json productionScorecardCompletion.acceptedHumanEvidence" $Failures
    }

    $expectedCategories = @(
        [pscustomobject]@{ code = "architecture-documentation"; currentScore = 150; targetScore = 150; completionGate = "all-weighted-controls-passed" },
        [pscustomobject]@{ code = "backend-statutory-accounting-engine"; currentScore = 350; targetScore = 350; completionGate = "all-weighted-controls-passed" },
        [pscustomobject]@{ code = "frontend-accountant-workbench"; currentScore = 250; targetScore = 250; completionGate = "all-weighted-controls-passed" },
        [pscustomobject]@{ code = "security-auth-tenant-platform-guardrails"; currentScore = 250; targetScore = 250; completionGate = "all-weighted-controls-passed" }
    )

    $categories = @((Get-JsonProperty $scorecard @("categories")))
    foreach ($expected in $expectedCategories) {
        $category = $categories |
            Where-Object { [string](Get-JsonProperty $_ @("code")) -eq $expected.code } |
            Select-Object -First 1

        if ($null -eq $category) {
            Add-Failure $Failures "release-evidence-report.json productionScorecardCompletion.categories must include $($expected.code)."
            continue
        }

        if ([int](Get-JsonProperty $category @("currentScore")) -ne $expected.currentScore) {
            Add-Failure $Failures "release-evidence-report.json productionScorecardCompletion.categories.$($expected.code).currentScore must be $($expected.currentScore)."
        }
        if ([int](Get-JsonProperty $category @("targetScore")) -ne $expected.targetScore) {
            Add-Failure $Failures "release-evidence-report.json productionScorecardCompletion.categories.$($expected.code).targetScore must be $($expected.targetScore)."
        }
        if ([string](Get-JsonProperty $category @("completionGate")) -ne $expected.completionGate) {
            Add-Failure $Failures "release-evidence-report.json productionScorecardCompletion.categories.$($expected.code).completionGate must be $($expected.completionGate)."
        }
    }
}

function Assert-ReleaseEvidenceWorkspaceControlManifest {
    param(
        [object]$ReleaseEvidence,
        [string]$Directory,
        [object[]]$RequiredWorkspaceControls,
        [System.Collections.Generic.List[string]]$Failures
    )

    if ($ReleaseEvidence.PSObject.Properties.Name -contains "__missing" -or
        $ReleaseEvidence.PSObject.Properties.Name -contains "__invalid") {
        return
    }

    $manifest = @(Get-JsonProperty $ReleaseEvidence @("workspaceControlFiles"))
    if ($manifest.Count -eq 0) {
        Add-Failure $Failures "release-evidence-report.json workspaceControlFiles must include retained release evidence workspace control hashes."
        return
    }

    foreach ($required in $RequiredWorkspaceControls) {
        Assert-ArrayContains @((Get-JsonProperty $ReleaseEvidence @("requiredCoverage", "releaseEvidenceWorkspaceFiles"))) $required.fileName "release-evidence-report.json requiredCoverage.releaseEvidenceWorkspaceFiles" $Failures

        $entry = $manifest |
            Where-Object {
                [string](Get-JsonProperty $_ @("fileName")) -eq [string]$required.fileName -and
                [string](Get-JsonProperty $_ @("evidenceName")) -eq [string]$required.evidenceName
            } |
            Select-Object -First 1

        if ($null -eq $entry) {
            Add-Failure $Failures "release-evidence-report.json workspaceControlFiles must include $($required.fileName)."
            continue
        }

        if ((Get-JsonProperty $entry @("present")) -ne $true) {
            Add-Failure $Failures "release-evidence-report.json workspaceControlFiles.$($required.fileName).present must be true."
        }

        $manifestSha = [string](Get-JsonProperty $entry @("sha256"))
        if ($manifestSha -notmatch '^[0-9a-f]{64}$') {
            Add-Failure $Failures "release-evidence-report.json workspaceControlFiles.$($required.fileName).sha256 must be a lowercase SHA-256 hash."
        }

        $manifestByteSize = Get-JsonProperty $entry @("byteSize")
        if ($null -eq $manifestByteSize -or [int]$manifestByteSize -le 0) {
            Add-Failure $Failures "release-evidence-report.json workspaceControlFiles.$($required.fileName).byteSize must be greater than zero."
        }

        $controlPath = Join-Path $Directory $required.fileName
        if (-not (Test-Path -LiteralPath $controlPath -PathType Leaf)) {
            Add-Failure $Failures "Release artifact pack must include retained release evidence workspace control file: $($required.fileName)"
            continue
        }

        $controlInfo = Get-Item -LiteralPath $controlPath
        if ($null -ne $manifestByteSize -and [int64]$manifestByteSize -ne [int64]$controlInfo.Length) {
            Add-Failure $Failures "release-evidence-report.json workspaceControlFiles.$($required.fileName).byteSize must match the retained workspace control file."
        }

        $actualSha = Get-FileSha256 $controlPath
        if ($manifestSha -match '^[0-9a-f]{64}$' -and $manifestSha -ne $actualSha) {
            Add-Failure $Failures "release-evidence-report.json workspaceControlFiles.$($required.fileName).sha256 must match the retained workspace control file."
        }
    }
}

function Assert-ReleaseEvidenceMachineSummary {
    param(
        [object]$MachineSummary,
        [object]$ReleaseEvidence,
        [string]$Directory,
        [string]$ReleaseCommitSha,
        [string]$ReleaseRunUrl,
        [System.Collections.Generic.List[string]]$Failures
    )

    if ($MachineSummary.PSObject.Properties.Name -contains "__missing" -or
        $MachineSummary.PSObject.Properties.Name -contains "__invalid") {
        return
    }

    $expectedCommitSha = $ReleaseCommitSha
    if ([string]::IsNullOrWhiteSpace($expectedCommitSha)) {
        $expectedCommitSha = [string](Get-JsonProperty $ReleaseEvidence @("releaseCandidate", "commitSha"))
    }

    $expectedRunUrl = $ReleaseRunUrl
    if ([string]::IsNullOrWhiteSpace($expectedRunUrl)) {
        $expectedRunUrl = [string](Get-JsonProperty $ReleaseEvidence @("releaseCandidate", "githubActionsRunUrl"))
    }

    if (-not [string]::IsNullOrWhiteSpace($expectedCommitSha)) {
        $actualCommitSha = [string](Get-JsonProperty $MachineSummary @("releaseCandidate", "commitSha"))
        if (-not [string]::Equals($actualCommitSha, $expectedCommitSha, [StringComparison]::OrdinalIgnoreCase)) {
            Add-Failure $Failures "release-evidence-machine-summary.json releaseCandidate.commitSha must match the release evidence candidate."
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($expectedRunUrl)) {
        $actualRunUrl = [string](Get-JsonProperty $MachineSummary @("releaseCandidate", "githubActionsRunUrl"))
        if (-not [string]::Equals($actualRunUrl, $expectedRunUrl, [StringComparison]::OrdinalIgnoreCase)) {
            Add-Failure $Failures "release-evidence-machine-summary.json releaseCandidate.githubActionsRunUrl must match the release evidence candidate."
        }
    }

    Assert-TextContains ([string](Get-JsonProperty $MachineSummary @("completionPolicy"))) "machine evidence only" "release-evidence-machine-summary.json completionPolicy" $Failures
    Assert-TextContains ([string](Get-JsonProperty $MachineSummary @("completionPolicy"))) "named reviewers" "release-evidence-machine-summary.json completionPolicy" $Failures

    $retainedMachineEvidence = @((Get-JsonProperty $MachineSummary @("retainedMachineEvidence")))
    if ($retainedMachineEvidence.Count -ne $expectedReleaseEvidenceMachineSummaryInputs.Count) {
        Add-Failure $Failures "release-evidence-machine-summary.json retainedMachineEvidence must contain exactly $($expectedReleaseEvidenceMachineSummaryInputs.Count) entries."
    }

    foreach ($expected in $expectedReleaseEvidenceMachineSummaryInputs) {
        $expectedFileName = [string](Get-JsonProperty $expected @("fileName"))
        $entry = $retainedMachineEvidence |
            Where-Object { [string]::Equals([string](Get-JsonProperty $_ @("fileName")), $expectedFileName, [StringComparison]::OrdinalIgnoreCase) } |
            Select-Object -First 1

        if ($null -eq $entry) {
            Add-Failure $Failures "release-evidence-machine-summary.json retainedMachineEvidence must include $expectedFileName."
            continue
        }

        foreach ($propertyName in @("sourceArtifactName", "sourceArtifactFile")) {
            $actualValue = [string](Get-JsonProperty $entry @($propertyName))
            $expectedValue = [string](Get-JsonProperty $expected @($propertyName))
            if ($actualValue -ne $expectedValue) {
                Add-Failure $Failures "release-evidence-machine-summary.json retainedMachineEvidence.$expectedFileName.$propertyName must be $expectedValue."
            }
        }

        $byteSize = Get-JsonProperty $entry @("byteSize")
        if ($null -eq $byteSize -or [int64]$byteSize -le 0) {
            Add-Failure $Failures "release-evidence-machine-summary.json retainedMachineEvidence.$expectedFileName.byteSize must be a positive integer."
        }

        $sha256 = [string](Get-JsonProperty $entry @("sha256"))
        if ($sha256 -notmatch '^[0-9a-f]{64}$') {
            Add-Failure $Failures "release-evidence-machine-summary.json retainedMachineEvidence.$expectedFileName.sha256 must be a lowercase 64-character SHA-256 digest."
        }

        $retainedPath = Join-Path $Directory $expectedFileName
        if (-not (Test-Path -LiteralPath $retainedPath -PathType Leaf)) {
            Add-Failure $Failures "Release artifact pack must retain machine summary evidence file: $expectedFileName"
            continue
        }

        $retainedFile = Get-Item -LiteralPath $retainedPath
        if ($null -ne $byteSize -and [int64]$byteSize -ne [int64]$retainedFile.Length) {
            Add-Failure $Failures "release-evidence-machine-summary.json retainedMachineEvidence.$expectedFileName.byteSize must match the retained evidence file."
        }

        if ($sha256 -match '^[0-9a-f]{64}$' -and $sha256 -ne (Get-FileSha256 $retainedPath)) {
            Add-Failure $Failures "release-evidence-machine-summary.json retainedMachineEvidence.$expectedFileName.sha256 must match the retained evidence file."
        }
    }

    $productionReadiness = Get-JsonProperty $MachineSummary @("productionReadiness")
    if ([string](Get-JsonProperty $productionReadiness @("scoreBasis")) -ne "independent-audit-control-ledger-v1") {
        Add-Failure $Failures "release-evidence-machine-summary.json productionReadiness.scoreBasis must be independent-audit-control-ledger-v1."
    }
    if ([string](Get-JsonProperty $productionReadiness @("auditBaselineDate")) -ne "2026-07-10") {
        Add-Failure $Failures "release-evidence-machine-summary.json productionReadiness.auditBaselineDate must be 2026-07-10."
    }
    if ([string](Get-JsonProperty $productionReadiness @("auditedCommit")) -ne "7ea54cc6d1769ced568ac1568d190cc2bb4b16d1") {
        Add-Failure $Failures "release-evidence-machine-summary.json productionReadiness.auditedCommit must identify the exact independently audited baseline commit."
    }
    if ([string](Get-JsonProperty $productionReadiness @("evidencePolicy")) -notlike "*exact live candidate*" -or
        [string](Get-JsonProperty $productionReadiness @("evidencePolicy")) -notlike "*artifact hashes*") {
        Add-Failure $Failures "release-evidence-machine-summary.json productionReadiness.evidencePolicy must require exact live candidate artifact hashes."
    }
    if ([int](Get-JsonProperty $productionReadiness @("targetScore")) -ne 1000) {
        Add-Failure $Failures "release-evidence-machine-summary.json productionReadiness.targetScore must be 1000."
    }
    $openControls = @((Get-JsonProperty $productionReadiness @("openEngineeringOrAssuranceControls")))
    if ([int](Get-JsonProperty $productionReadiness @("openEngineeringOrAssuranceControlCount")) -ne $openControls.Count) {
        Add-Failure $Failures "release-evidence-machine-summary.json productionReadiness.openEngineeringOrAssuranceControlCount must match the open control list."
    }
    $controlCodes = @((Get-JsonProperty $productionReadiness @("scorecardControlCodes")))
    if ($controlCodes.Count -eq 0) {
        Add-Failure $Failures "release-evidence-machine-summary.json productionReadiness.scorecardControlCodes must retain the verified weighted control inventory."
    }
    foreach ($openControl in $openControls) {
        Assert-ArrayContains $controlCodes ([string]$openControl) "release-evidence-machine-summary.json productionReadiness.scorecardControlCodes" $Failures
    }
    if ([string](Get-JsonProperty $productionReadiness @("verificationStatus")) -ne "passed") {
        Add-Failure $Failures "release-evidence-machine-summary.json productionReadiness.verificationStatus must be passed."
    }

    $verificationFailureCount = Get-JsonProperty $productionReadiness @("verificationFailureCount")
    if ($null -eq $verificationFailureCount -or [int]$verificationFailureCount -ne 0) {
        Add-Failure $Failures "release-evidence-machine-summary.json productionReadiness.verificationFailureCount must be 0."
    }

    foreach ($closeoutStepCode in $requiredHumanReleaseEvidenceCloseoutStepCodes) {
        Assert-ArrayContains @((Get-JsonProperty $productionReadiness @("humanReleaseEvidenceCloseoutStepCodes"))) $closeoutStepCode "release-evidence-machine-summary.json productionReadiness.humanReleaseEvidenceCloseoutStepCodes" $Failures
    }

    $pickupFilesByEvidence = Get-JsonProperty $productionReadiness @("humanReleaseEvidenceReviewerPickupFiles")
    foreach ($required in $requiredReleaseEvidenceTemplates) {
        $expectedEvidenceName = [string](Get-JsonProperty $required @("evidenceName"))
        foreach ($requiredPickupFile in @((Get-JsonProperty $required @("requiredPickupFiles")))) {
            Assert-ArrayContains @((Get-JsonProperty $pickupFilesByEvidence @($expectedEvidenceName))) ([string]$requiredPickupFile) "release-evidence-machine-summary.json productionReadiness.humanReleaseEvidenceReviewerPickupFiles.$expectedEvidenceName" $Failures
        }
    }

    $reviewerQueue = @((Get-JsonProperty $MachineSummary @("reviewerQueue")))
    if ($reviewerQueue.Count -ne $requiredReleaseEvidenceTemplates.Count) {
        Add-Failure $Failures "release-evidence-machine-summary.json reviewerQueue must contain exactly $($requiredReleaseEvidenceTemplates.Count) entries."
    }

    foreach ($required in $requiredReleaseEvidenceTemplates) {
        $expectedEvidenceName = [string](Get-JsonProperty $required @("evidenceName"))
        $entry = $reviewerQueue |
            Where-Object { [string]::Equals([string](Get-JsonProperty $_ @("EvidenceName")), $expectedEvidenceName, [StringComparison]::OrdinalIgnoreCase) } |
            Select-Object -First 1

        if ($null -eq $entry) {
            Add-Failure $Failures "release-evidence-machine-summary.json reviewerQueue must include $expectedEvidenceName."
            continue
        }

        if ([string](Get-JsonProperty $entry @("TemplateFile")) -ne [string](Get-JsonProperty $required @("fileName"))) {
            Add-Failure $Failures "release-evidence-machine-summary.json reviewerQueue.$expectedEvidenceName.TemplateFile must match the required template."
        }

        if ([string](Get-JsonProperty $entry @("ReviewerRole")) -ne [string](Get-JsonProperty $required @("requiredReviewerRole"))) {
            Add-Failure $Failures "release-evidence-machine-summary.json reviewerQueue.$expectedEvidenceName.ReviewerRole must match the required reviewer role."
        }

        if ([string](Get-JsonProperty $entry @("SignOffGate")) -ne [string](Get-JsonProperty $required @("signOffGate"))) {
            Add-Failure $Failures "release-evidence-machine-summary.json reviewerQueue.$expectedEvidenceName.SignOffGate must match the required sign-off gate."
        }

        Assert-TextContains ([string](Get-JsonProperty $entry @("HumanAction"))) "record" "release-evidence-machine-summary.json reviewerQueue.$expectedEvidenceName.HumanAction" $Failures

        foreach ($requiredPickupFile in @((Get-JsonProperty $required @("requiredPickupFiles")))) {
            Assert-ArrayContains @((Get-JsonProperty $entry @("RequiredPickupFiles"))) ([string]$requiredPickupFile) "release-evidence-machine-summary.json reviewerQueue.$expectedEvidenceName.RequiredPickupFiles" $Failures
        }
    }
}

function Assert-ReleaseEvidenceWorkspaceVerificationReport {
    param(
        [object]$WorkspaceVerificationReport,
        [object]$ReleaseEvidence,
        [string]$ReleaseCommitSha,
        [string]$ReleaseRunUrl,
        [string[]]$ExpectedWorkspaceFiles,
        [System.Collections.Generic.List[string]]$Failures
    )

    if ($WorkspaceVerificationReport.PSObject.Properties.Name -contains "__missing" -or
        $WorkspaceVerificationReport.PSObject.Properties.Name -contains "__invalid") {
        return
    }

    if ([string](Get-JsonProperty $WorkspaceVerificationReport @("status")) -ne "passed") {
        Add-Failure $Failures "release-evidence-workspace-verification-report.json status must be passed."
    }

    if ([int](Get-JsonProperty $WorkspaceVerificationReport @("failureCount")) -ne 0) {
        Add-Failure $Failures "release-evidence-workspace-verification-report.json failureCount must be 0."
    }

    if ((Get-JsonProperty $WorkspaceVerificationReport @("releaseCandidate", "identityProvided")) -ne $true) {
        Add-Failure $Failures "release-evidence-workspace-verification-report.json releaseCandidate.identityProvided must be true."
    }

    $expectedCommitSha = $ReleaseCommitSha
    if ([string]::IsNullOrWhiteSpace($expectedCommitSha)) {
        $expectedCommitSha = [string](Get-JsonProperty $ReleaseEvidence @("releaseCandidate", "commitSha"))
    }

    $expectedRunUrl = $ReleaseRunUrl
    if ([string]::IsNullOrWhiteSpace($expectedRunUrl)) {
        $expectedRunUrl = [string](Get-JsonProperty $ReleaseEvidence @("releaseCandidate", "githubActionsRunUrl"))
    }

    if (-not [string]::IsNullOrWhiteSpace($expectedCommitSha)) {
        $actualCommitSha = [string](Get-JsonProperty $WorkspaceVerificationReport @("releaseCandidate", "commitSha"))
        if (-not [string]::Equals($actualCommitSha, $expectedCommitSha, [StringComparison]::OrdinalIgnoreCase)) {
            Add-Failure $Failures "release-evidence-workspace-verification-report.json releaseCandidate.commitSha must match the release evidence candidate."
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($expectedRunUrl)) {
        $actualRunUrl = [string](Get-JsonProperty $WorkspaceVerificationReport @("releaseCandidate", "githubActionsRunUrl"))
        if (-not [string]::Equals($actualRunUrl, $expectedRunUrl, [StringComparison]::OrdinalIgnoreCase)) {
            Add-Failure $Failures "release-evidence-workspace-verification-report.json releaseCandidate.githubActionsRunUrl must match the release evidence candidate."
        }
    }

    Assert-ArrayContainsExactly @((Get-JsonProperty $WorkspaceVerificationReport @("requiredWorkspaceFiles"))) $ExpectedWorkspaceFiles "release-evidence-workspace-verification-report.json requiredWorkspaceFiles" $Failures

    $workspaceFiles = @((Get-JsonProperty $WorkspaceVerificationReport @("workspaceFiles")))
    $workspaceFileNames = @($workspaceFiles | ForEach-Object { [string](Get-JsonProperty $_ @("fileName")) })
    Assert-ArrayContainsExactly $workspaceFileNames $ExpectedWorkspaceFiles "release-evidence-workspace-verification-report.json workspaceFiles" $Failures

    foreach ($expectedFile in $ExpectedWorkspaceFiles) {
        $entry = $workspaceFiles |
            Where-Object { [string]::Equals([string](Get-JsonProperty $_ @("fileName")), $expectedFile, [StringComparison]::OrdinalIgnoreCase) } |
            Select-Object -First 1

        if ($null -eq $entry) {
            continue
        }

        if ([int](Get-JsonProperty $entry @("byteSize")) -le 0) {
            Add-Failure $Failures "release-evidence-workspace-verification-report.json workspaceFiles.$expectedFile.byteSize must be greater than zero."
        }

        if ([string](Get-JsonProperty $entry @("sha256")) -notmatch '^[0-9a-f]{64}$') {
            Add-Failure $Failures "release-evidence-workspace-verification-report.json workspaceFiles.$expectedFile.sha256 must be a lowercase SHA-256 hash."
        }
    }

    Assert-ReleaseEvidenceWorkspacePreparedHumanControls $WorkspaceVerificationReport $Failures
    Assert-ReleaseEvidenceWorkspacePendingHumanBlockers $WorkspaceVerificationReport $Failures
    Assert-ReleaseEvidenceWorkspaceReviewerAssignments $WorkspaceVerificationReport $Failures
}

function Assert-ReleaseEvidenceWorkspacePreparedHumanControls {
    param(
        [object]$WorkspaceVerificationReport,
        [System.Collections.Generic.List[string]]$Failures
    )

    $controls = @((Get-JsonProperty $WorkspaceVerificationReport @("preparedHumanTemplateControls")))
    if ($controls.Count -ne $expectedPreparedHumanTemplateControls.Count) {
        Add-Failure $Failures "release-evidence-workspace-verification-report.json preparedHumanTemplateControls must include exactly $($expectedPreparedHumanTemplateControls.Count) item(s)."
    }

    foreach ($expected in $expectedPreparedHumanTemplateControls) {
        $expectedFile = [string](Get-JsonProperty $expected @("fileName"))
        $entry = $controls |
            Where-Object { [string]::Equals([string](Get-JsonProperty $_ @("fileName")), $expectedFile, [StringComparison]::OrdinalIgnoreCase) } |
            Select-Object -First 1

        if ($null -eq $entry) {
            Add-Failure $Failures "release-evidence-workspace-verification-report.json preparedHumanTemplateControls must include $expectedFile."
            continue
        }

        if ([string](Get-JsonProperty $entry @("context")) -ne [string](Get-JsonProperty $expected @("context"))) {
            Add-Failure $Failures "release-evidence-workspace-verification-report.json preparedHumanTemplateControls.$expectedFile.context must match the prepared template context."
        }

        if ([string](Get-JsonProperty $entry @("checkboxPolicy")) -ne "unchecked-before-named-human-signoff") {
            Add-Failure $Failures "release-evidence-workspace-verification-report.json preparedHumanTemplateControls.$expectedFile.checkboxPolicy must be unchecked-before-named-human-signoff."
        }

        Assert-ArrayContainsExactly @((Get-JsonProperty $entry @("blankFields"))) @((Get-JsonProperty $expected @("blankFields"))) "release-evidence-workspace-verification-report.json preparedHumanTemplateControls.$expectedFile.blankFields" $Failures
    }
}

function Assert-ReleaseEvidenceWorkspacePendingHumanBlockers {
    param(
        [object]$WorkspaceVerificationReport,
        [System.Collections.Generic.List[string]]$Failures
    )

    $blockers = @((Get-JsonProperty $WorkspaceVerificationReport @("pendingHumanEvidenceBlockers")))
    if ($blockers.Count -ne $requiredReleaseEvidenceTemplates.Count) {
        Add-Failure $Failures "release-evidence-workspace-verification-report.json pendingHumanEvidenceBlockers must include exactly $($requiredReleaseEvidenceTemplates.Count) item(s)."
    }

    foreach ($required in $requiredReleaseEvidenceTemplates) {
        $expectedEvidenceName = [string](Get-JsonProperty $required @("evidenceName"))
        $entry = $blockers |
            Where-Object { [string]::Equals([string](Get-JsonProperty $_ @("evidenceName")), $expectedEvidenceName, [StringComparison]::OrdinalIgnoreCase) } |
            Select-Object -First 1

        if ($null -eq $entry) {
            Add-Failure $Failures "release-evidence-workspace-verification-report.json pendingHumanEvidenceBlockers must include $expectedEvidenceName."
            continue
        }

        if ([string](Get-JsonProperty $entry @("templateFile")) -ne [string](Get-JsonProperty $required @("fileName"))) {
            Add-Failure $Failures "release-evidence-workspace-verification-report.json pendingHumanEvidenceBlockers.$expectedEvidenceName.templateFile must match the required template."
        }

        if ([string](Get-JsonProperty $entry @("requiredReviewerRole")) -ne [string](Get-JsonProperty $required @("requiredReviewerRole"))) {
            Add-Failure $Failures "release-evidence-workspace-verification-report.json pendingHumanEvidenceBlockers.$expectedEvidenceName.requiredReviewerRole must match the required reviewer role."
        }

        if ([string](Get-JsonProperty $entry @("signOffGate")) -ne [string](Get-JsonProperty $required @("signOffGate"))) {
            Add-Failure $Failures "release-evidence-workspace-verification-report.json pendingHumanEvidenceBlockers.$expectedEvidenceName.signOffGate must match the required sign-off gate."
        }

        if ([string](Get-JsonProperty $entry @("status")) -ne "incomplete") {
            Add-Failure $Failures "release-evidence-workspace-verification-report.json pendingHumanEvidenceBlockers.$expectedEvidenceName.status must be incomplete."
        }

        if ([int](Get-JsonProperty $entry @("blockingFailureCount")) -le 0) {
            Add-Failure $Failures "release-evidence-workspace-verification-report.json pendingHumanEvidenceBlockers.$expectedEvidenceName.blockingFailureCount must be greater than zero."
        }

        if ([string]::IsNullOrWhiteSpace([string](Get-JsonProperty $entry @("firstBlockingFailure")))) {
            Add-Failure $Failures "release-evidence-workspace-verification-report.json pendingHumanEvidenceBlockers.$expectedEvidenceName.firstBlockingFailure must be present."
        }
    }
}

function Assert-ReleaseEvidenceWorkspaceReviewerAssignments {
    param(
        [object]$WorkspaceVerificationReport,
        [System.Collections.Generic.List[string]]$Failures
    )

    $assignments = @((Get-JsonProperty $WorkspaceVerificationReport @("reviewerAssignmentInventory")))
    if ($assignments.Count -ne $requiredReleaseEvidenceTemplates.Count) {
        Add-Failure $Failures "release-evidence-workspace-verification-report.json reviewerAssignmentInventory must include exactly $($requiredReleaseEvidenceTemplates.Count) item(s)."
    }

    foreach ($required in $requiredReleaseEvidenceTemplates) {
        $expectedEvidenceName = [string](Get-JsonProperty $required @("evidenceName"))
        $entry = $assignments |
            Where-Object { [string]::Equals([string](Get-JsonProperty $_ @("evidenceName")), $expectedEvidenceName, [StringComparison]::OrdinalIgnoreCase) } |
            Select-Object -First 1

        if ($null -eq $entry) {
            Add-Failure $Failures "release-evidence-workspace-verification-report.json reviewerAssignmentInventory must include $expectedEvidenceName."
            continue
        }

        if ([string](Get-JsonProperty $entry @("templateFile")) -ne [string](Get-JsonProperty $required @("fileName"))) {
            Add-Failure $Failures "release-evidence-workspace-verification-report.json reviewerAssignmentInventory.$expectedEvidenceName.templateFile must match the required template."
        }

        if ([string](Get-JsonProperty $entry @("requiredReviewerRole")) -ne [string](Get-JsonProperty $required @("requiredReviewerRole"))) {
            Add-Failure $Failures "release-evidence-workspace-verification-report.json reviewerAssignmentInventory.$expectedEvidenceName.requiredReviewerRole must match the required reviewer role."
        }

        if ([string](Get-JsonProperty $entry @("signOffGate")) -ne [string](Get-JsonProperty $required @("signOffGate"))) {
            Add-Failure $Failures "release-evidence-workspace-verification-report.json reviewerAssignmentInventory.$expectedEvidenceName.signOffGate must match the required sign-off gate."
        }

        if ([string](Get-JsonProperty $entry @("assignmentStatus")) -ne "unassigned") {
            Add-Failure $Failures "release-evidence-workspace-verification-report.json reviewerAssignmentInventory.$expectedEvidenceName.assignmentStatus must be unassigned."
        }

        if ([string](Get-JsonProperty $entry @("escalationOwnerRole")) -ne "Release operator") {
            Add-Failure $Failures "release-evidence-workspace-verification-report.json reviewerAssignmentInventory.$expectedEvidenceName.escalationOwnerRole must be Release operator."
        }

        foreach ($blankField in @("assignedReviewerName", "assignedReviewerEmail", "dueAtUtc")) {
            if (-not [string]::IsNullOrWhiteSpace([string](Get-JsonProperty $entry @($blankField)))) {
                Add-Failure $Failures "release-evidence-workspace-verification-report.json reviewerAssignmentInventory.$expectedEvidenceName.$blankField must be blank before named reviewer routing."
            }
        }

        if ([string]::IsNullOrWhiteSpace([string](Get-JsonProperty $entry @("humanAction")))) {
            Add-Failure $Failures "release-evidence-workspace-verification-report.json reviewerAssignmentInventory.$expectedEvidenceName.humanAction must be present."
        }

        foreach ($requiredPickupFile in @((Get-JsonProperty $required @("requiredPickupFiles")))) {
            Assert-ArrayContains @((Get-JsonProperty $entry @("reviewerPickupFiles"))) ([string]$requiredPickupFile) "release-evidence-workspace-verification-report.json reviewerAssignmentInventory.$expectedEvidenceName.reviewerPickupFiles" $Failures
        }
    }
}

function Assert-ReleaseEvidenceWorkspaceInventoryRetention {
    param(
        [object]$WorkspaceVerificationReport,
        [string]$Directory,
        [string[]]$ExpectedWorkspaceFiles,
        [System.Collections.Generic.List[string]]$Failures
    )

    if ($WorkspaceVerificationReport.PSObject.Properties.Name -contains "__missing" -or
        $WorkspaceVerificationReport.PSObject.Properties.Name -contains "__invalid") {
        return
    }

    $workspaceFiles = @((Get-JsonProperty $WorkspaceVerificationReport @("workspaceFiles")))
    foreach ($expectedFile in $ExpectedWorkspaceFiles) {
        $entry = $workspaceFiles |
            Where-Object { [string]::Equals([string](Get-JsonProperty $_ @("fileName")), $expectedFile, [StringComparison]::OrdinalIgnoreCase) } |
            Select-Object -First 1

        if ($null -eq $entry) {
            continue
        }

        $retainedPath = Join-Path $Directory $expectedFile
        if (-not (Test-Path -LiteralPath $retainedPath -PathType Leaf)) {
            Add-Failure $Failures "Release artifact pack must retain workspace inventory file: $expectedFile"
            continue
        }

        if ($expectedFile -in $mutableReleaseEvidenceWorkspaceInventoryFiles) {
            continue
        }

        $expectedByteSize = [int64](Get-JsonProperty $entry @("byteSize"))
        $actualFile = Get-Item -LiteralPath $retainedPath
        if ($expectedByteSize -gt 0 -and [int64]$actualFile.Length -ne $expectedByteSize) {
            Add-Failure $Failures "Release artifact pack retained workspace inventory file $expectedFile byteSize must match release-evidence-workspace-verification-report.json."
        }

        $expectedSha256 = [string](Get-JsonProperty $entry @("sha256"))
        if ($expectedSha256 -match '^[0-9a-f]{64}$') {
            $actualSha256 = Get-FileSha256 $retainedPath
            if ($actualSha256 -ne $expectedSha256) {
                Add-Failure $Failures "Release artifact pack retained workspace inventory file $expectedFile sha256 must match release-evidence-workspace-verification-report.json."
            }
        }
    }
}

$failures = [System.Collections.Generic.List[string]]::new()
Assert-EvidenceDirectoryIsNotLink $EvidenceDirectory
$resolvedDirectory = Resolve-Path -LiteralPath $EvidenceDirectory -ErrorAction Stop
$releaseCommitSha = $CommitSha.Trim()
$releaseRunUrl = $GitHubActionsRunUrl.Trim()

if (($releaseCommitSha.Length -gt 0 -and $releaseRunUrl.Length -eq 0) -or
    ($releaseCommitSha.Length -eq 0 -and $releaseRunUrl.Length -gt 0)) {
    Add-Failure $failures "CommitSha and GitHubActionsRunUrl must be provided together when release candidate identity is supplied."
}

if ($releaseCommitSha.Length -gt 0 -and $releaseCommitSha -cnotmatch '^[0-9a-f]{40}$') {
    Add-Failure $failures "CommitSha must be a full lowercase 40-character hexadecimal Git commit SHA."
}

if ($releaseRunUrl.Length -gt 0 -and $releaseRunUrl -cnotmatch '^https://github\.com/[^/\s]+/[^/\s]+/actions/runs/[0-9]+$') {
    Add-Failure $failures "GitHubActionsRunUrl must be an exact GitHub Actions run URL."
}

$dependency = Read-JsonEvidence $resolvedDirectory.Path "dependency-audit-report.json" $failures
$productionSafety = Read-JsonEvidence $resolvedDirectory.Path "production-safety-report.json" $failures
$containerSupplyChain = Read-JsonEvidence $resolvedDirectory.Path "container-supply-chain-report.json" $failures
$containerSupplyChainVerification = Read-JsonEvidence $resolvedDirectory.Path "container-supply-chain-verification-report.json" $failures
$monitoring = Read-JsonEvidence $resolvedDirectory.Path "monitoring-error-routing-report.json" $failures
$structuredLog = Read-JsonEvidence $resolvedDirectory.Path "structured-log-report.json" $failures
$postgresTls = Read-JsonEvidence $resolvedDirectory.Path "postgres-tls-report.json" $failures
$restore = Read-JsonEvidence $resolvedDirectory.Path "restore-drill-report.json" $failures
$capacityProfile = Read-JsonEvidence $resolvedDirectory.Path "capacity-profile-report.json" $failures
$productionFailover = Read-JsonEvidence $resolvedDirectory.Path "production-failover-report.json" $failures
$migrationUpgrade = Read-JsonEvidence $resolvedDirectory.Path "migration-upgrade-report.json" $failures
$migrationUpgradeVerification = Read-JsonEvidence $resolvedDirectory.Path "migration-upgrade-verification-report.json" $failures
$noDirectSubmission = Read-JsonEvidence $resolvedDirectory.Path "no-direct-filing-submission-report.json" $failures
$productionReadiness = Read-JsonEvidence $resolvedDirectory.Path "production-readiness-report.json" $failures
$productionReadinessVerification = Read-JsonEvidence $resolvedDirectory.Path "production-readiness-verification-report.json" $failures
$visualManifest = Read-JsonEvidence $resolvedDirectory.Path "visual-smoke-manifest.json" $failures
$visualSmoke = Read-JsonEvidence $resolvedDirectory.Path "visual-smoke-evidence-report.json" $failures
$accountantWorkbench = Read-JsonEvidence $resolvedDirectory.Path "accountant-workbench-evidence-report.json" $failures
$releaseEvidence = Read-JsonEvidence $resolvedDirectory.Path "release-evidence-report.json" $failures
$releaseEvidenceMachineSummaryPath = Join-Path $resolvedDirectory.Path "release-evidence-machine-summary.json"
$releaseEvidenceMachineSummary = Read-JsonEvidenceFile $releaseEvidenceMachineSummaryPath "release-evidence-machine-summary.json" $failures
$releaseEvidenceWorkspaceVerificationReportPath = Join-Path $resolvedDirectory.Path "release-evidence-workspace-verification-report.json"
$releaseEvidenceWorkspaceVerificationReport = Read-JsonEvidenceFile $releaseEvidenceWorkspaceVerificationReportPath "release-evidence-workspace-verification-report.json" $failures

$requiredReadinessManifestCodes = @(
    "backend-golden-corpus",
    "frontend-workbench-contract",
    "frontend-production-build",
    "visual-smoke-light-dark",
    "production-readiness-report-verification",
    "ci-machine-evidence-pack",
    "release-artifact-pack",
    "production-stack-smoke",
    "postgres-transport-tls",
    "backup-restore-drill",
    "postgres-migration-upgrade-gate",
    "qualified-accountant-final-signoff",
    "source-law-change-review",
    "external-ros-validation-evidence",
    "no-direct-cro-ros-submission-control",
    "manual-accountant-acceptance"
)
$requiredHumanReleaseEvidenceCloseoutStepCodes = @(
    "pick-up-reviewer-workspace",
    "complete-human-evidence-templates",
    "run-release-evidence-verifier",
    "confirm-human-evidence-completion",
    "verify-release-artifact-pack"
)

$requiredReleaseEvidenceTemplates = @(
    [pscustomobject]@{ evidenceName = "visualQa"; fileName = "visual-qa-signoff-template.md"; requiredReviewerRole = "Named visual QA reviewer"; signOffGate = "visual-qa-screenshot-review"; requiredPickupFiles = @("visual-qa-signoff-template.md", "visual-smoke-manifest.json", "visual-smoke-evidence-report.json", "accountant-workbench-evidence-report.json", "release-evidence-reviewer-blockers.md") },
    [pscustomobject]@{ evidenceName = "sourceLawReview"; fileName = "source-law-review-template.md"; requiredReviewerRole = "Named source-law reviewer plus qualified accountant"; signOffGate = "source-law-change-review"; requiredPickupFiles = @("source-law-review-template.md", "production-readiness-report.json", "production-readiness-verification-report.json", "release-evidence-reviewer-blockers.md") },
    [pscustomobject]@{ evidenceName = "externalRosIxbrlValidation"; fileName = "external-ros-ixbrl-validation-template.md"; requiredReviewerRole = "External ROS/iXBRL validation reviewer"; signOffGate = "external-ros-validation-evidence"; requiredPickupFiles = @("external-ros-ixbrl-validation-template.md", "production-readiness-report.json", "release-evidence-reviewer-blockers.md") },
    [pscustomobject]@{ evidenceName = "qualifiedAccountantAcceptance"; fileName = "qualified-accountant-acceptance-template.md"; requiredReviewerRole = "Named qualified accountant"; signOffGate = "qualified-accountant-final-signoff"; requiredPickupFiles = @("qualified-accountant-acceptance-template.md", "production-readiness-report.json", "accountant-workbench-evidence-report.json", "release-evidence-reviewer-blockers.md") },
    [pscustomobject]@{ evidenceName = "manualHandoffAcceptance"; fileName = "manual-handoff-acceptance-template.md"; requiredReviewerRole = "Named manual handoff reviewer"; signOffGate = "manual-accountant-acceptance"; requiredPickupFiles = @("manual-handoff-acceptance-template.md", "production-readiness-report.json", "release-evidence-reviewer-blockers.md") },
    [pscustomobject]@{ evidenceName = "monitoringProviderConfirmation"; fileName = "monitoring-provider-confirmation-template.md"; requiredReviewerRole = "Named release operator"; signOffGate = "production-monitoring"; requiredPickupFiles = @("monitoring-provider-confirmation-template.md", "monitoring-error-routing-report.json", "structured-log-report.json", "release-evidence-reviewer-blockers.md") }
)

$requiredReleaseEvidenceWorkspaceControls = @(
    [pscustomobject]@{ evidenceName = "releaseEvidenceWorkspaceManifest"; fileName = "release-evidence-workspace-manifest.json" },
    [pscustomobject]@{ evidenceName = "releaseEvidenceMachineSummary"; fileName = "release-evidence-machine-summary.json" },
    [pscustomobject]@{ evidenceName = "releaseEvidenceWorkspaceVerificationReport"; fileName = "release-evidence-workspace-verification-report.json" }
)

$requiredReleaseEvidenceReviewerHandoffFiles = @(
    [pscustomobject]@{ evidenceName = "releaseEvidenceReviewerIndex"; fileName = "release-evidence-reviewer-index.md" },
    [pscustomobject]@{ evidenceName = "releaseEvidenceReviewerCompletion"; fileName = "release-evidence-reviewer-completion.json" },
    [pscustomobject]@{ evidenceName = "releaseEvidenceReviewerAssignments"; fileName = "release-evidence-reviewer-assignments.json" },
    [pscustomobject]@{ evidenceName = "releaseEvidenceReviewerBlockers"; fileName = "release-evidence-reviewer-blockers.md" },
    [pscustomobject]@{ evidenceName = "releaseEvidenceVerifierOutput"; fileName = "release-evidence-verifier-output.txt" }
)

$allEvidence = [ordered]@{
    "dependency-audit-report.json" = $dependency
    "production-safety-report.json" = $productionSafety
    "container-supply-chain-report.json" = $containerSupplyChain
    "container-supply-chain-verification-report.json" = $containerSupplyChainVerification
    "monitoring-error-routing-report.json" = $monitoring
    "structured-log-report.json" = $structuredLog
    "postgres-tls-report.json" = $postgresTls
    "restore-drill-report.json" = $restore
    "capacity-profile-report.json" = $capacityProfile
    "production-failover-report.json" = $productionFailover
    "migration-upgrade-report.json" = $migrationUpgrade
    "migration-upgrade-verification-report.json" = $migrationUpgradeVerification
    "no-direct-filing-submission-report.json" = $noDirectSubmission
    "production-readiness-report.json" = $productionReadiness
    "production-readiness-verification-report.json" = $productionReadinessVerification
    "visual-smoke-manifest.json" = $visualManifest
    "visual-smoke-evidence-report.json" = $visualSmoke
    "accountant-workbench-evidence-report.json" = $accountantWorkbench
    "release-evidence-report.json" = $releaseEvidence
}

foreach ($entry in $allEvidence.GetEnumerator()) {
    if ($entry.Key -in @("production-readiness-report.json", "visual-smoke-manifest.json")) {
        continue
    }

    Assert-StatusPassed $entry.Value $entry.Key $failures
}

if (-not ($dependency.PSObject.Properties.Name -contains "__missing")) {
    Assert-NonEmptyString $dependency.frontend.packageLockSha256 "dependency-audit-report.json frontend.packageLockSha256" $failures
    Assert-Truthy $dependency.backend.nugetAudit.enabled "dependency-audit-report.json backend.nugetAudit.enabled" $failures
    Assert-Truthy $dependency.ci.runsNpmAuditModerate "dependency-audit-report.json ci.runsNpmAuditModerate" $failures
    Assert-Truthy $dependency.ci.runsCiActionVerifier "dependency-audit-report.json ci.runsCiActionVerifier" $failures
}

if (-not ($productionSafety.PSObject.Properties.Name -contains "__missing")) {
    if ([string]$productionSafety.migrationSafety.apiDependsOnMigrate -ne "service_completed_successfully") {
        Add-Failure $failures "production-safety-report.json migrationSafety.apiDependsOnMigrate must be service_completed_successfully."
    }
    if ([string](Get-JsonProperty $productionSafety @("migrationSafety", "roleProvisionDependsOnDatabase")) -ne "service_healthy") {
        Add-Failure $failures "production-safety-report.json migrationSafety.roleProvisionDependsOnDatabase must be service_healthy."
    }
    if ([string](Get-JsonProperty $productionSafety @("migrationSafety", "migrateDependsOnRoleProvision")) -ne "service_completed_successfully") {
        Add-Failure $failures "production-safety-report.json migrationSafety.migrateDependsOnRoleProvision must be service_completed_successfully."
    }
    Assert-Truthy $productionSafety.seedSafety.bootstrapOwnerPasswordOnlyOnMigrate "production-safety-report.json seedSafety.bootstrapOwnerPasswordOnlyOnMigrate" $failures
    if ($productionSafety.workflowSafety.productionSmokeUsesBuildFlag -ne $false) {
        Add-Failure $failures "production-safety-report.json workflowSafety.productionSmokeUsesBuildFlag must be false."
    }
    Assert-Truthy (Get-JsonProperty $productionSafety @("imageContract", "digestPinned")) "production-safety-report.json imageContract.digestPinned" $failures
    Assert-Truthy (Get-JsonProperty $productionSafety @("imageContract", "backendAndMigrateSameDigest")) "production-safety-report.json imageContract.backendAndMigrateSameDigest" $failures
    if ((Get-JsonProperty $productionSafety @("workflowSafety", "productionSmokeBuildCommandsPresent")) -ne $false) {
        Add-Failure $failures "production-safety-report.json workflowSafety.productionSmokeBuildCommandsPresent must be false."
    }
    Assert-Truthy (Get-JsonProperty $productionSafety @("workflowSafety", "productionSmokePullsExactDigests")) "production-safety-report.json workflowSafety.productionSmokePullsExactDigests" $failures
    Assert-Truthy (Get-JsonProperty $productionSafety @("databaseTransport", "sslEnabled")) "production-safety-report.json databaseTransport.sslEnabled" $failures
    Assert-Truthy (Get-JsonProperty $productionSafety @("databaseTransport", "serverIdentityVerified")) "production-safety-report.json databaseTransport.serverIdentityVerified" $failures
    Assert-Truthy (Get-JsonProperty $productionSafety @("databaseTransport", "insecureOverrideDisabled")) "production-safety-report.json databaseTransport.insecureOverrideDisabled" $failures
    if ([string](Get-JsonProperty $productionSafety @("databaseIsolation", "required")) -ne "true") {
        Add-Failure $failures "production-safety-report.json databaseIsolation.required must be true."
    }
    if ([string](Get-JsonProperty $productionSafety @("databaseIsolation", "applicationLoginRole")) -ne "accounts_api") {
        Add-Failure $failures "production-safety-report.json databaseIsolation.applicationLoginRole must be accounts_api."
    }
    Assert-Truthy (Get-JsonProperty $productionSafety @("databaseIsolation", "applicationAndMigrationCredentialsSeparated")) "production-safety-report.json databaseIsolation.applicationAndMigrationCredentialsSeparated" $failures
    if ((Get-JsonProperty $productionSafety @("databaseIsolation", "apiHasMigrationCredential")) -ne $false) {
        Add-Failure $failures "production-safety-report.json databaseIsolation.apiHasMigrationCredential must be false."
    }
    Assert-Truthy (Get-JsonProperty $productionSafety @("databaseIsolation", "forcedRlsProvisionedByMigration")) "production-safety-report.json databaseIsolation.forcedRlsProvisionedByMigration" $failures
    if ([string](Get-JsonProperty $productionSafety @("identitySecurity", "required")) -ne "true" -or
        [string](Get-JsonProperty $productionSafety @("identitySecurity", "breachedPasswordCheckEnabled")) -ne "true" -or
        [string](Get-JsonProperty $productionSafety @("identitySecurity", "breachedPasswordFailClosed")) -ne "true" -or
        [string](Get-JsonProperty $productionSafety @("identitySecurity", "privilegedMfaRequired")) -ne "true") {
        Add-Failure $failures "production-safety-report.json identitySecurity must require privileged MFA and fail-closed breached-password checks."
    }
    if ([string](Get-JsonProperty $productionSafety @("deadlineDelivery", "required")) -ne "true" -or
        [string](Get-JsonProperty $productionSafety @("deadlineDelivery", "enabled")) -ne "true") {
        Add-Failure $failures "production-safety-report.json deadlineDelivery must be required and enabled."
    }
    Assert-Truthy (Get-JsonProperty $productionSafety @("backupProtection", "encryptedArtifactRequired")) "production-safety-report.json backupProtection.encryptedArtifactRequired" $failures
    Assert-Truthy (Get-JsonProperty $productionSafety @("backupProtection", "plaintextDumpRetentionForbidden")) "production-safety-report.json backupProtection.plaintextDumpRetentionForbidden" $failures
    Assert-Truthy (Get-JsonProperty $productionSafety @("backupProtection", "encryptedRestoreDrillRequired")) "production-safety-report.json backupProtection.encryptedRestoreDrillRequired" $failures
}

Assert-ContainerSupplyChainEvidence $containerSupplyChain $containerSupplyChainVerification $releaseCommitSha $releaseRunUrl $failures

if (-not ($monitoring.PSObject.Properties.Name -contains "__missing")) {
    Assert-NonEmptyString $monitoring.provider "monitoring-error-routing-report.json provider" $failures
    Assert-NonEmptyString $monitoring.eventId "monitoring-error-routing-report.json eventId" $failures
    Assert-NonEmptyString $monitoring.correlationId "monitoring-error-routing-report.json correlationId" $failures
    Assert-NonEmptyString $monitoring.baseUrl "monitoring-error-routing-report.json baseUrl" $failures
    Assert-NonEmptyString (Get-JsonProperty $monitoring @("clientEvent", "eventId")) "monitoring-error-routing-report.json clientEvent.eventId" $failures
    Assert-NonEmptyString (Get-JsonProperty $monitoring @("clientEvent", "correlationId")) "monitoring-error-routing-report.json clientEvent.correlationId" $failures
    if ([string](Get-JsonProperty $monitoring @("clientEvent", "eventCode")) -ne "render-exception") {
        Add-Failure $failures "monitoring-error-routing-report.json clientEvent.eventCode must be render-exception."
    }
    if ([string](Get-JsonProperty $monitoring @("clientEvent", "route")) -ne "/companies/{id}/periods/{id}/{redacted}") {
        Add-Failure $failures "monitoring-error-routing-report.json clientEvent.route must retain only the controlled route shape."
    }
    Assert-Truthy (Get-JsonProperty $monitoring @("clientEvent", "sensitiveInputAbsent")) "monitoring-error-routing-report.json clientEvent.sensitiveInputAbsent" $failures
}

if (-not ($structuredLog.PSObject.Properties.Name -contains "__missing")) {
    if ([int]$structuredLog.jsonLogLineCount -lt 2) {
        Add-Failure $failures "structured-log-report.json jsonLogLineCount must include both controlled monitoring lines."
    }
    Assert-Truthy $structuredLog.matchedMonitoringSmokeLine "structured-log-report.json matchedMonitoringSmokeLine" $failures
    Assert-Truthy (Get-JsonProperty $structuredLog @("matchedClientMonitoringLine")) "structured-log-report.json matchedClientMonitoringLine" $failures
    Assert-Truthy (Get-JsonProperty $structuredLog @("syntheticSensitiveMarkersAbsent")) "structured-log-report.json syntheticSensitiveMarkersAbsent" $failures
    if (-not [string]::IsNullOrWhiteSpace([string]$monitoring.correlationId) -and
        [string]$structuredLog.monitoringCorrelationId -ne [string]$monitoring.correlationId) {
        Add-Failure $failures "structured-log-report.json monitoringCorrelationId must match monitoring-error-routing-report.json correlationId."
    }
    $clientMonitoringCorrelationId = [string](Get-JsonProperty $monitoring @("clientEvent", "correlationId"))
    if (-not [string]::IsNullOrWhiteSpace($clientMonitoringCorrelationId) -and
        [string](Get-JsonProperty $structuredLog @("clientMonitoringCorrelationId")) -ne $clientMonitoringCorrelationId) {
        Add-Failure $failures "structured-log-report.json clientMonitoringCorrelationId must match monitoring-error-routing-report.json clientEvent.correlationId."
    }
}

if (-not ($postgresTls.PSObject.Properties.Name -contains "__missing")) {
    Assert-Truthy (Get-JsonProperty $postgresTls @("runtimeSession", "ssl")) "postgres-tls-report.json runtimeSession.ssl" $failures
    Assert-Truthy (Get-JsonProperty $postgresTls @("runtimeSession", "hostnameMismatchRejected")) "postgres-tls-report.json runtimeSession.hostnameMismatchRejected" $failures
    Assert-Truthy (Get-JsonProperty $postgresTls @("certificate", "currentlyValid")) "postgres-tls-report.json certificate.currentlyValid" $failures
    if ([string](Get-JsonProperty $postgresTls @("connectionPolicy", "sslMode")) -ne "VerifyFull") {
        Add-Failure $failures "postgres-tls-report.json connectionPolicy.sslMode must be VerifyFull."
    }
    if ((Get-JsonProperty $postgresTls @("connectionPolicy", "trustServerCertificate")) -ne $false) {
        Add-Failure $failures "postgres-tls-report.json connectionPolicy.trustServerCertificate must be false."
    }
    if ($releaseCommitSha.Length -gt 0 -and -not [string]::Equals([string](Get-JsonProperty $postgresTls @("releaseCandidate", "commitSha")), $releaseCommitSha, [StringComparison]::OrdinalIgnoreCase)) {
        Add-Failure $failures "postgres-tls-report.json releaseCandidate.commitSha must match the release-pack commit."
    }
    if ($releaseRunUrl.Length -gt 0 -and [string](Get-JsonProperty $postgresTls @("releaseCandidate", "githubActionsRunUrl")) -ne $releaseRunUrl) {
        Add-Failure $failures "postgres-tls-report.json releaseCandidate.githubActionsRunUrl must match the release-pack run URL."
    }
}

if (-not ($restore.PSObject.Properties.Name -contains "__missing") -and
    -not ($restore.PSObject.Properties.Name -contains "__invalid")) {
    Assert-RestoreArtifactLinkage $restore $resolvedDirectory.Path $releaseCommitSha $releaseRunUrl $failures
    if ([string]$restore.backupSha256 -cnotmatch '^[0-9a-f]{64}$') {
        Add-Failure $failures "restore-drill-report.json backupSha256 must be a lowercase SHA-256 hash."
    }
    foreach ($check in @($restore.tableChecks)) {
        if ([int]$check.restoredCount -ne [int]$check.sourceCount) {
            Add-Failure $failures "restore-drill-report.json table '$($check.table)' restoredCount must match sourceCount."
        }
    }
    foreach ($table in @("tenants", "user accounts")) {
        if (-not (@($restore.tableChecks) | Where-Object { [string]$_.table -eq $table })) {
            Add-Failure $failures "restore-drill-report.json tableChecks must include $table."
        }
    }
    Assert-Truthy (Get-JsonProperty $restore @("backupEncryption", "encrypted")) "restore-drill-report.json backupEncryption.encrypted" $failures
    Assert-Truthy (Get-JsonProperty $restore @("backupEncryption", "restoredFromEncryptedCopy")) "restore-drill-report.json backupEncryption.restoredFromEncryptedCopy" $failures
    if ((Get-JsonProperty $restore @("backupEncryption", "plaintextDumpRetained")) -ne $false) {
        Add-Failure $failures "restore-drill-report.json backupEncryption.plaintextDumpRetained must be false."
    }
    if ([string](Get-JsonProperty $restore @("backupEncryption", "algorithm")) -ne "CMS/AES-256-CBC") {
        Add-Failure $failures "restore-drill-report.json backupEncryption.algorithm must be CMS/AES-256-CBC."
    }
    Assert-Truthy (Get-JsonProperty $restore @("auditIntegrityChecks", "passed")) "restore-drill-report.json auditIntegrityChecks.passed" $failures
    Assert-Truthy (Get-JsonProperty $restore @("recoveryMetrics", "rpoTargetMet")) "restore-drill-report.json recoveryMetrics.rpoTargetMet" $failures
    Assert-Truthy (Get-JsonProperty $restore @("recoveryMetrics", "rtoTargetMet")) "restore-drill-report.json recoveryMetrics.rtoTargetMet" $failures
    foreach ($collection in @("schemaChecks", "figureChecks", "fingerprintChecks")) {
        $checks = @((Get-JsonProperty $restore @($collection)))
        if ($checks.Count -eq 0 -or @($checks | Where-Object { (Get-JsonProperty $_ @("matched")) -ne $true }).Count -ne 0) {
            Add-Failure $failures "restore-drill-report.json $collection must contain matched source/restore checks."
        }
    }
}

if (-not ($capacityProfile.PSObject.Properties.Name -contains "__missing")) {
    if ([string](Get-JsonProperty $capacityProfile @("schemaVersion")) -ne "accounts-capacity-profile-v1" -or
        [string](Get-JsonProperty $capacityProfile @("profile")) -ne "bounded-production-stack-health-v1") {
        Add-Failure $failures "capacity-profile-report.json must use the canonical bounded production-stack profile schema."
    }
    if ((Get-JsonProperty $capacityProfile @("releaseCandidate", "identityProvided")) -ne $true -or
        [string](Get-JsonProperty $capacityProfile @("releaseCandidate", "commitSha")) -cne $releaseCommitSha -or
        [string](Get-JsonProperty $capacityProfile @("releaseCandidate", "githubActionsRunUrl")) -cne $releaseRunUrl) {
        Add-Failure $failures "capacity-profile-report.json release candidate identity must exactly match the release pack."
    }
    if ([string](Get-JsonProperty $capacityProfile @("targetOrigin")) -cne "https://accounts-smoke.local") {
        Add-Failure $failures "capacity-profile-report.json targetOrigin must be the candidate HTTPS smoke ingress."
    }
    if ([int](Get-JsonProperty $capacityProfile @("thresholds", "requests")) -ne 120 -or
        [int](Get-JsonProperty $capacityProfile @("thresholds", "concurrency")) -ne 12 -or
        [double](Get-JsonProperty $capacityProfile @("thresholds", "p95Milliseconds")) -ne 1000 -or
        [double](Get-JsonProperty $capacityProfile @("thresholds", "maximumErrorRatePercent")) -ne 0 -or
        [double](Get-JsonProperty $capacityProfile @("thresholds", "minimumThroughputPerSecond")) -ne 10 -or
        [int](Get-JsonProperty $capacityProfile @("thresholds", "timeoutMilliseconds")) -ne 5000) {
        Add-Failure $failures "capacity-profile-report.json thresholds must preserve the canonical 120-request, 12-concurrency, zero-error bounded profile."
    }
    if ([int](Get-JsonProperty $capacityProfile @("requestCount")) -ne 120 -or
        [int](Get-JsonProperty $capacityProfile @("failedCount")) -ne 0 -or
        [double](Get-JsonProperty $capacityProfile @("errorRatePercent")) -ne 0 -or
        [double](Get-JsonProperty $capacityProfile @("p95Milliseconds")) -gt [double](Get-JsonProperty $capacityProfile @("thresholds", "p95Milliseconds")) -or
        [double](Get-JsonProperty $capacityProfile @("throughputPerSecond")) -lt [double](Get-JsonProperty $capacityProfile @("thresholds", "minimumThroughputPerSecond"))) {
        Add-Failure $failures "capacity-profile-report.json measurements must satisfy every bounded profile threshold."
    }
    $capacityEndpointSeries = @((Get-JsonProperty $capacityProfile @("endpointSeries")))
    if ($capacityEndpointSeries.Count -ne 2) {
        Add-Failure $failures "capacity-profile-report.json endpointSeries must contain exactly the two canonical health endpoints."
    }
    foreach ($endpoint in @("/health", "/health/ready")) {
        $endpointRows = @($capacityEndpointSeries | Where-Object { [string](Get-JsonProperty $_ @("endpoint")) -eq $endpoint })
        if ($endpointRows.Count -ne 1 -or [int](Get-JsonProperty $endpointRows[0] @("count")) -ne 60 -or
            [int](Get-JsonProperty $endpointRows[0] @("failedCount")) -ne 0 -or
            [double](Get-JsonProperty $endpointRows[0] @("p95Milliseconds")) -gt 1000) {
            Add-Failure $failures "capacity-profile-report.json endpointSeries must retain exactly 60 successful threshold-compliant '$endpoint' measurements."
        }
    }
    if (@((Get-JsonProperty $capacityProfile @("failureCodes"))).Count -ne 0 -or
        @((Get-JsonProperty $capacityProfile @("thresholdFailures"))).Count -ne 0) {
        Add-Failure $failures "capacity-profile-report.json must contain no failure codes or threshold failures."
    }
    foreach ($privacyField in @("requestBodiesSent", "responseBodiesRetained", "authenticationUsed", "clientOrTenantIdentifiersRetained")) {
        if ((Get-JsonProperty $capacityProfile @("privacy", $privacyField)) -ne $false) {
            Add-Failure $failures "capacity-profile-report.json privacy.$privacyField must be false."
        }
    }
    $capacityBoundary = [string](Get-JsonProperty $capacityProfile @("scopeBoundary"))
    foreach ($requiredBoundaryText in @("production-scale financial-write", "document-generation", "host-failover", "named recovery drills")) {
        if ($capacityBoundary -notlike "*$requiredBoundaryText*") {
            Add-Failure $failures "capacity-profile-report.json scopeBoundary must preserve the '$requiredBoundaryText' limitation."
        }
    }
}

if (-not ($productionFailover.PSObject.Properties.Name -contains "__missing")) {
    if ([string](Get-JsonProperty $productionFailover @("schemaVersion")) -ne "accounts-production-failover-v1") {
        Add-Failure $failures "production-failover-report.json schemaVersion must be accounts-production-failover-v1."
    }
    if ([string](Get-JsonProperty $productionFailover @("releaseCandidate", "commitSha")) -cne $releaseCommitSha) {
        Add-Failure $failures "production-failover-report.json releaseCandidate.commitSha must exactly match the release-pack commit."
    }
    if ([string](Get-JsonProperty $productionFailover @("releaseCandidate", "githubActionsRunUrl")) -cne $releaseRunUrl) {
        Add-Failure $failures "production-failover-report.json releaseCandidate.githubActionsRunUrl must exactly match the release-pack run URL."
    }
    if ([string](Get-JsonProperty $productionFailover @("targetOrigin")) -cne "https://accounts-smoke.local") {
        Add-Failure $failures "production-failover-report.json targetOrigin must be the candidate HTTPS smoke ingress."
    }
    if ([int](Get-JsonProperty $productionFailover @("targets", "failureDetectionSeconds")) -ne 30 -or
        [int](Get-JsonProperty $productionFailover @("targets", "apiRecoverySeconds")) -ne 120 -or
        [int](Get-JsonProperty $productionFailover @("targets", "databaseRecoverySeconds")) -ne 180) {
        Add-Failure $failures "production-failover-report.json must retain the canonical 30/120/180-second detection and recovery targets."
    }
    if ((Get-JsonProperty $productionFailover @("executionScope", "confirmedEphemeralCandidateStack")) -ne $true -or
        [string](Get-JsonProperty $productionFailover @("executionScope", "expectedComposeProject")) -cne "accounts-production") {
        Add-Failure $failures "production-failover-report.json must prove explicit interruption of the accounts-production ephemeral candidate project."
    }
    $observedFailoverServices = @((Get-JsonProperty $productionFailover @("executionScope", "observedServices")))
    if ($observedFailoverServices.Count -ne 2) {
        Add-Failure $failures "production-failover-report.json executionScope.observedServices must contain exactly the api and db services."
    }
    foreach ($serviceName in @("api", "db")) {
        $serviceRows = @($observedFailoverServices | Where-Object { [string](Get-JsonProperty $_ @("service")) -eq $serviceName })
        if ($serviceRows.Count -ne 1 -or [string](Get-JsonProperty $serviceRows[0] @("project")) -cne "accounts-production" -or
            [string](Get-JsonProperty $serviceRows[0] @("state")) -ne "running") {
            Add-Failure $failures "production-failover-report.json executionScope must contain one running accounts-production/$serviceName service."
        }
    }

    $failoverTargets = [ordered]@{
        "initial-ready" = [ordered]@{ expectedHealthy = $true; timeoutSeconds = 30 }
        "api-host-failure-detected" = [ordered]@{ expectedHealthy = $false; timeoutSeconds = 30 }
        "api-host-recovered" = [ordered]@{ expectedHealthy = $true; timeoutSeconds = 120 }
        "database-failure-detected" = [ordered]@{ expectedHealthy = $false; timeoutSeconds = 30 }
        "database-recovered" = [ordered]@{ expectedHealthy = $true; timeoutSeconds = 180 }
    }
    $failoverObservations = @((Get-JsonProperty $productionFailover @("observations")))
    if ($failoverObservations.Count -ne $failoverTargets.Count) {
        Add-Failure $failures "production-failover-report.json observations must contain exactly five failover phases."
    }
    foreach ($phaseName in $failoverTargets.Keys) {
        $phaseRows = @($failoverObservations | Where-Object { [string](Get-JsonProperty $_ @("phase")) -eq $phaseName })
        if ($phaseRows.Count -ne 1) {
            Add-Failure $failures "production-failover-report.json observations must contain exactly one '$phaseName' phase."
            continue
        }

        $phaseRow = $phaseRows[0]
        $phaseContract = $failoverTargets[$phaseName]
        if ((Get-JsonProperty $phaseRow @("expectedHealthy")) -ne $phaseContract.expectedHealthy -or
            (Get-JsonProperty $phaseRow @("passed")) -ne $true) {
            Add-Failure $failures "production-failover-report.json phase '$phaseName' must pass with expectedHealthy=$($phaseContract.expectedHealthy)."
        }
        $observedStatusCode = Get-JsonProperty $phaseRow @("observedStatusCode")
        if (($phaseContract.expectedHealthy -and [int]$observedStatusCode -ne 200) -or
            (-not $phaseContract.expectedHealthy -and $null -ne $observedStatusCode -and [int]$observedStatusCode -eq 200)) {
            Add-Failure $failures "production-failover-report.json phase '$phaseName' observedStatusCode contradicts expectedHealthy."
        }
        $timeoutSeconds = [double]$phaseContract.timeoutSeconds
        $elapsedMilliseconds = [double](Get-JsonProperty $phaseRow @("elapsedMilliseconds"))
        if ($timeoutSeconds -le 0 -or $elapsedMilliseconds -lt 0 -or $elapsedMilliseconds -gt ($timeoutSeconds * 1000)) {
            Add-Failure $failures "production-failover-report.json phase '$phaseName' must complete within its positive target."
        }
    }

    if (@((Get-JsonProperty $productionFailover @("failures"))).Count -ne 0) {
        Add-Failure $failures "production-failover-report.json failures must be empty."
    }
    foreach ($privacyField in @("responseBodiesRetained", "authenticationRetained", "tenantOrClientIdentifiersRetained")) {
        if ((Get-JsonProperty $productionFailover @("privacy", $privacyField)) -ne $false) {
            Add-Failure $failures "production-failover-report.json privacy.$privacyField must be false."
        }
    }
    $failoverBoundary = [string](Get-JsonProperty $productionFailover @("scopeBoundary"))
    foreach ($requiredBoundaryText in @("not production host failover", "off-host restore", "RPO/RTO", "named-operator acceptance")) {
        if ($failoverBoundary -notlike "*$requiredBoundaryText*") {
            Add-Failure $failures "production-failover-report.json scopeBoundary must preserve the '$requiredBoundaryText' limitation."
        }
    }
}

if (-not ($migrationUpgrade.PSObject.Properties.Name -contains "__missing")) {
    if ([string](Get-JsonProperty $migrationUpgrade @("database", "previousReleaseMigration")) -ne "20260621123340_AddCroSignatories") {
        Add-Failure $failures "migration-upgrade-report.json must use the supported previous-release migration floor."
    }
    if ([int](Get-JsonProperty $migrationUpgrade @("freshDatabase", "pendingMigrationCount")) -ne 0 -or
        [int](Get-JsonProperty $migrationUpgrade @("freshDatabase", "appliedMigrationCount")) -ne [int](Get-JsonProperty $migrationUpgrade @("database", "migrationCount"))) {
        Add-Failure $failures "migration-upgrade-report.json freshDatabase must apply every migration with zero pending migrations."
    }
    foreach ($checkName in @("tenant-and-user", "company-and-accounting-period", "financial-rows-and-figures", "filing-snapshots-and-artifacts", "audit-chain-and-checkpoints")) {
        $checks = @((Get-JsonProperty $migrationUpgrade @("previousReleaseUpgrade", "preservationChecks")) | Where-Object { [string](Get-JsonProperty $_ @("name")) -eq $checkName })
        if ($checks.Count -ne 1 -or [string](Get-JsonProperty $checks[0] @("status")) -ne "passed" -or
            [int](Get-JsonProperty $checks[0] @("beforeRowCount")) -le 0 -or
            [int](Get-JsonProperty $checks[0] @("beforeRowCount")) -ne [int](Get-JsonProperty $checks[0] @("afterRowCount")) -or
            [string](Get-JsonProperty $checks[0] @("beforeSha256")) -notmatch '^[0-9a-f]{64}$' -or
            [string](Get-JsonProperty $checks[0] @("beforeSha256")) -cne [string](Get-JsonProperty $checks[0] @("afterSha256"))) {
            Add-Failure $failures "migration-upgrade-report.json must preserve exact positive '$checkName' row and fingerprint evidence."
        }
    }
    Assert-Truthy (Get-JsonProperty $migrationUpgrade @("previousReleaseUpgrade", "auditChainCryptographicallyValid")) "migration-upgrade-report.json previousReleaseUpgrade.auditChainCryptographicallyValid" $failures
    foreach ($field in @("failureObserved", "partialSchemaAbsent", "dataPreserved", "migrationHistoryPreserved")) {
        Assert-Truthy (Get-JsonProperty $migrationUpgrade @("failureRollback", $field)) "migration-upgrade-report.json failureRollback.$field" $failures
    }
    if ([int](Get-JsonProperty $migrationUpgrade @("failureRollback", "transactionSuppressedSqlOperationCount")) -ne 0) {
        Add-Failure $failures "migration-upgrade-report.json failureRollback.transactionSuppressedSqlOperationCount must be zero."
    }
    if ([string](Get-JsonProperty $migrationUpgrade @("encryptedRecoveryIntegration", "requiredCompanionReport")) -ne "restore-drill-report.json" -or
        (Get-JsonProperty $migrationUpgrade @("encryptedRecoveryIntegration", "requiredInSameReleasePack")) -ne $true) {
        Add-Failure $failures "migration-upgrade-report.json must require the encrypted restore drill in the same evidence pack."
    }
    if ($releaseCommitSha.Length -gt 0 -and -not [string]::Equals([string](Get-JsonProperty $migrationUpgrade @("releaseCandidate", "commitSha")), $releaseCommitSha, [StringComparison]::OrdinalIgnoreCase)) {
        Add-Failure $failures "migration-upgrade-report.json releaseCandidate.commitSha must match the release-pack commit."
    }
    if ($releaseRunUrl.Length -gt 0 -and [string](Get-JsonProperty $migrationUpgrade @("releaseCandidate", "gitHubActionsRunUrl")) -ne $releaseRunUrl) {
        Add-Failure $failures "migration-upgrade-report.json releaseCandidate.gitHubActionsRunUrl must match the release-pack run URL."
    }
}

if (-not ($migrationUpgradeVerification.PSObject.Properties.Name -contains "__missing")) {
    if ([int](Get-JsonProperty $migrationUpgradeVerification @("failureCount")) -ne 0 -or
        [string](Get-JsonProperty $migrationUpgradeVerification @("previousReleaseMigration")) -ne "20260621123340_AddCroSignatories" -or
        [string](Get-JsonProperty $migrationUpgradeVerification @("encryptedRecoveryCompanionReport")) -ne "restore-drill-report.json") {
        Add-Failure $failures "migration-upgrade-verification-report.json must retain a passed supported-floor verification tied to encrypted recovery."
    }
    if ($releaseCommitSha.Length -gt 0 -and -not [string]::Equals([string](Get-JsonProperty $migrationUpgradeVerification @("releaseCandidate", "commitSha")), $releaseCommitSha, [StringComparison]::OrdinalIgnoreCase)) {
        Add-Failure $failures "migration-upgrade-verification-report.json releaseCandidate.commitSha must match the release-pack commit."
    }
    if ($releaseRunUrl.Length -gt 0 -and [string](Get-JsonProperty $migrationUpgradeVerification @("releaseCandidate", "gitHubActionsRunUrl")) -ne $releaseRunUrl) {
        Add-Failure $failures "migration-upgrade-verification-report.json releaseCandidate.gitHubActionsRunUrl must match the release-pack run URL."
    }
}

if (-not ($noDirectSubmission.PSObject.Properties.Name -contains "__missing")) {
    if ([int]$noDirectSubmission.failureCount -ne 0) {
        Add-Failure $failures "no-direct-filing-submission-report.json failureCount must be zero."
    }
    if ((Get-JsonProperty $noDirectSubmission @("releaseCandidate", "identityProvided")) -ne $true) {
        Add-Failure $failures "no-direct-filing-submission-report.json releaseCandidate.identityProvided must be true."
    }
    if ($releaseCommitSha.Length -gt 0) {
        $noDirectCommitSha = [string](Get-JsonProperty $noDirectSubmission @("releaseCandidate", "commitSha"))
        if (-not [string]::Equals($noDirectCommitSha, $releaseCommitSha, [StringComparison]::OrdinalIgnoreCase)) {
            Add-Failure $failures "no-direct-filing-submission-report.json releaseCandidate.commitSha must match CommitSha."
        }
    }
    if ($releaseRunUrl.Length -gt 0) {
        $noDirectRunUrl = [string](Get-JsonProperty $noDirectSubmission @("releaseCandidate", "githubActionsRunUrl"))
        if (-not [string]::Equals($noDirectRunUrl, $releaseRunUrl, [StringComparison]::OrdinalIgnoreCase)) {
            Add-Failure $failures "no-direct-filing-submission-report.json releaseCandidate.githubActionsRunUrl must match GitHubActionsRunUrl."
        }
    }
    foreach ($route in @('"/cro-status"', '"/cro-payment"', '"/validate-ixbrl"')) {
        Assert-ArrayContains @($noDirectSubmission.allowedRecordedWorkflowRoutes) $route "no-direct-filing-submission-report.json allowedRecordedWorkflowRoutes" $failures
    }
}

if (-not ($productionReadiness.PSObject.Properties.Name -contains "__missing")) {
    if ([string]$productionReadiness.overallStatus -ne "review-required") {
        Add-Failure $failures "production-readiness-report.json overallStatus must be review-required."
    }
    Assert-NonEmptyString $productionReadiness.generatedAt "production-readiness-report.json generatedAt" $failures
    if ($null -eq $productionReadiness.productionScorecard) {
        Add-Failure $failures "production-readiness-report.json productionScorecard must be present."
    } else {
        if ([int]$productionReadiness.productionScorecard.currentScore -le 0) {
            Add-Failure $failures "production-readiness-report.json productionScorecard.currentScore must be greater than zero."
        }
        if ([int]$productionReadiness.productionScorecard.targetScore -ne 1000) {
            Add-Failure $failures "production-readiness-report.json productionScorecard.targetScore must be 1000."
        }
        if ([string]$productionReadiness.productionScorecard.scoreBasis -ne "independent-audit-control-ledger-v1") {
            Add-Failure $failures "production-readiness-report.json productionScorecard.scoreBasis must be independent-audit-control-ledger-v1."
        }
        if ([string]$productionReadiness.productionScorecard.auditedCommit -ne "7ea54cc6d1769ced568ac1568d190cc2bb4b16d1") {
            Add-Failure $failures "production-readiness-report.json productionScorecard.auditedCommit must identify the exact independently audited baseline commit."
        }
        foreach ($categoryCode in @("architecture-documentation", "backend-statutory-accounting-engine", "frontend-accountant-workbench", "security-auth-tenant-platform-guardrails")) {
            if (-not (@($productionReadiness.productionScorecard.categories) | Where-Object { [string]$_.code -eq $categoryCode })) {
                Add-Failure $failures "production-readiness-report.json productionScorecard.categories must include $categoryCode."
            }
        }
    }
    foreach ($requiredEvidence in @("production-scorecard", "production-readiness-report", "production-readiness-verification-report", "release-verification-manifest", "release-blocker-register")) {
        Assert-ArrayContains @($productionReadiness.assurancePacket.evidenceItems) $requiredEvidence "production-readiness-report.json assurancePacket.evidenceItems" $failures
    }
    foreach ($requiredCollection in @("sourceLawSnapshot", "goldenFilingCorpus", "releaseBlockerRegister", "releaseVerificationManifest", "visualQaCoverage")) {
        if ($null -eq $productionReadiness.$requiredCollection) {
            Add-Failure $failures "production-readiness-report.json $requiredCollection must be present."
        }
    }
}

if (-not ($productionReadinessVerification.PSObject.Properties.Name -contains "__missing")) {
    if ([int]$productionReadinessVerification.failureCount -ne 0) {
        Add-Failure $failures "production-readiness-verification-report.json failureCount must be zero."
    }
    foreach ($coverageProperty in @("categoryCodes", "goldenCorpusScenarioCodes", "sourceLawSourceIds", "releaseVerificationManifestCodes", "humanReleaseEvidenceCodes", "humanReleaseEvidenceCloseoutStepCodes", "assuranceEvidenceItems")) {
        if ($null -eq $productionReadinessVerification.requiredCoverage.$coverageProperty -or @($productionReadinessVerification.requiredCoverage.$coverageProperty).Count -eq 0) {
            Add-Failure $failures "production-readiness-verification-report.json requiredCoverage.$coverageProperty must be present."
        }
    }
    if ([string]$productionReadinessVerification.requiredCoverage.scoreBasis -ne "independent-audit-control-ledger-v1") {
        Add-Failure $failures "production-readiness-verification-report.json requiredCoverage.scoreBasis must be independent-audit-control-ledger-v1."
    }
    if ([string]$productionReadinessVerification.requiredCoverage.auditedCommit -ne "7ea54cc6d1769ced568ac1568d190cc2bb4b16d1") {
        Add-Failure $failures "production-readiness-verification-report.json requiredCoverage.auditedCommit must identify the exact independently audited baseline commit."
    }
    foreach ($scenarioCode in @("micro-ltd", "small-abridged-ltd", "dac-small", "clg-charity", "medium-audit-required")) {
        Assert-ArrayContains @($productionReadinessVerification.requiredCoverage.goldenCorpusScenarioCodes) $scenarioCode "production-readiness-verification-report.json requiredCoverage.goldenCorpusScenarioCodes" $failures
    }
    foreach ($manifestCode in $requiredReadinessManifestCodes) {
        Assert-ArrayContains @($productionReadinessVerification.requiredCoverage.releaseVerificationManifestCodes) $manifestCode "production-readiness-verification-report.json requiredCoverage.releaseVerificationManifestCodes" $failures
    }
    foreach ($requiredTemplate in $requiredReleaseEvidenceTemplates) {
        Assert-ArrayContains @($productionReadinessVerification.requiredCoverage.humanReleaseEvidenceCodes) $requiredTemplate.evidenceName "production-readiness-verification-report.json requiredCoverage.humanReleaseEvidenceCodes" $failures
    }
    foreach ($closeoutStepCode in $requiredHumanReleaseEvidenceCloseoutStepCodes) {
        Assert-ArrayContains @($productionReadinessVerification.requiredCoverage.humanReleaseEvidenceCloseoutStepCodes) $closeoutStepCode "production-readiness-verification-report.json requiredCoverage.humanReleaseEvidenceCloseoutStepCodes" $failures
    }
    if ([int]$productionReadinessVerification.requiredCoverage.expectedVisualScreenshotCount -ne 192) {
        Add-Failure $failures "production-readiness-verification-report.json requiredCoverage.expectedVisualScreenshotCount must be 192."
    }
    if ([int]$productionReadinessVerification.requiredCoverage.expectedVisualRouteCount -ne 32 -or
        [int]$productionReadinessVerification.requiredCoverage.expectedAccountantWorkbenchRouteCount -ne 7) {
        Add-Failure $failures "production-readiness-verification-report.json must retain 32 canonical visual states and 7 accountant routes."
    }
    if (-not [string]::IsNullOrWhiteSpace([string]$productionReadiness.__path) -and
        -not [string]::IsNullOrWhiteSpace([string]$productionReadinessVerification.reportPath) -and
        [IO.Path]::GetFileName([string]$productionReadinessVerification.reportPath) -ne "production-readiness-report.json") {
        Add-Failure $failures "production-readiness-verification-report.json reportPath must reference production-readiness-report.json."
    }
}

if (-not ($visualSmoke.PSObject.Properties.Name -contains "__missing")) {
    if ([int](Get-JsonProperty $visualSmoke @("screenshotCount")) -ne 192 -or
        [int](Get-JsonProperty $visualSmoke @("expectedScreenshotCount")) -ne 192) {
        Add-Failure $failures "visual-smoke-evidence-report.json must cover 192 expected screenshots."
    }
    if ([int](Get-JsonProperty $visualSmoke @("routeCount")) -ne 32) {
        Add-Failure $failures "visual-smoke-evidence-report.json routeCount must be 32."
    }
    Assert-VisualSmokeDimensionEvidence $visualSmoke $resolvedDirectory.Path $failures
}

Assert-VisualSmokeManifestEvidence $visualManifest $visualSmoke $failures

if (-not ($accountantWorkbench.PSObject.Properties.Name -contains "__missing")) {
    if ([int](Get-JsonProperty $accountantWorkbench @("routeCount")) -ne 7) {
        Add-Failure $failures "accountant-workbench-evidence-report.json routeCount must be 7."
    }
    if ([int](Get-JsonProperty $accountantWorkbench @("screenshotCount")) -ne 42 -or
        [int](Get-JsonProperty $accountantWorkbench @("expectedScreenshotCount")) -ne 42) {
        Add-Failure $failures "accountant-workbench-evidence-report.json must cover 42 expected accountant-route screenshots."
    }
    if ([int](Get-JsonProperty $accountantWorkbench @("visualSmokeTotalScreenshotCount")) -ne 192 -or
        [int](Get-JsonProperty $accountantWorkbench @("visualSmokeExpectedScreenshotCount")) -ne 192) {
        Add-Failure $failures "accountant-workbench-evidence-report.json visual smoke totals must both be 192."
    }
    if ([int](Get-JsonProperty $accountantWorkbench @("routeAcceptanceCount")) -ne 7) {
        Add-Failure $failures "accountant-workbench-evidence-report.json routeAcceptanceCount must be 7."
    }
    foreach ($coverageProperty in @("routeCodes", "routeKeys", "workflowStages", "themes", "viewports", "reviewChecks", "layoutChecks", "expectedTextChecks", "routeAcceptanceEvidence", "evidenceFiles")) {
        $coverageValue = Get-JsonProperty $accountantWorkbench @("requiredCoverage", $coverageProperty)
        if ($null -eq $coverageValue -or @($coverageValue).Count -eq 0) {
            Add-Failure $failures "accountant-workbench-evidence-report.json requiredCoverage.$coverageProperty must be present."
        }
    }
    if ([string](Get-JsonProperty $accountantWorkbench @("requiredCoverage", "routeAcceptanceSignOffGate")) -ne "qualified-accountant-route-acceptance") {
        Add-Failure $failures "accountant-workbench-evidence-report.json requiredCoverage.routeAcceptanceSignOffGate must be qualified-accountant-route-acceptance."
    }
    Assert-AccountantWorkbenchRequiredCoverage $accountantWorkbench $failures
    foreach ($requiredEvidenceFile in @("visual-smoke-manifest.json", "visual-smoke-evidence-report.json", "accountant-workbench-evidence-report.json")) {
        Assert-ArrayContains @((Get-JsonProperty $accountantWorkbench @("requiredCoverage", "evidenceFiles"))) $requiredEvidenceFile "accountant-workbench-evidence-report.json requiredCoverage.evidenceFiles" $failures
    }
    Assert-ArrayContains @((Get-JsonProperty $accountantWorkbench @("requiredCoverage", "expectedTextChecks"))) "visual smoke screenshots carry route expected accountant decision text" "accountant-workbench-evidence-report.json requiredCoverage.expectedTextChecks" $failures
    foreach ($route in @((Get-JsonProperty $accountantWorkbench @("routeReadiness")))) {
        if ([int](Get-JsonProperty $route @("expectedTextEvidenceCount")) -ne 6) {
            Add-Failure $failures "accountant-workbench-evidence-report.json routeReadiness.expectedTextEvidenceCount must be 6 for every route."
        }
    }
    foreach ($route in @((Get-JsonProperty $accountantWorkbench @("routeAcceptance")))) {
        Assert-NonEmptyString (Get-JsonProperty $route @("routeName")) "accountant-workbench-evidence-report.json routeAcceptance.routeName" $failures
        Assert-NonEmptyString (Get-JsonProperty $route @("routeKey")) "accountant-workbench-evidence-report.json routeAcceptance.routeKey" $failures
        Assert-NonEmptyString (Get-JsonProperty $route @("expectedText")) "accountant-workbench-evidence-report.json routeAcceptance.expectedText" $failures
        Assert-Truthy (Get-JsonProperty $route @("blocksRelease")) "accountant-workbench-evidence-report.json routeAcceptance.blocksRelease" $failures
        if ([string](Get-JsonProperty $route @("signOffGate")) -ne "qualified-accountant-route-acceptance") {
            Add-Failure $failures "accountant-workbench-evidence-report.json routeAcceptance.signOffGate must be qualified-accountant-route-acceptance."
        }
        if (-not (@((Get-JsonProperty $route @("requiredAcceptanceEvidence"))) | Where-Object { [string]$_ -like "*qualified-accountant-route-acceptance" })) {
            Add-Failure $failures "accountant-workbench-evidence-report.json routeAcceptance.requiredAcceptanceEvidence must include qualified-accountant-route-acceptance."
        }
    }
    Assert-AccountantWorkbenchRouteAcceptance $accountantWorkbench $failures
}

if (-not ($releaseEvidence.PSObject.Properties.Name -contains "__missing")) {
    if ([int]$releaseEvidence.failureCount -ne 0) {
        Add-Failure $failures "release-evidence-report.json failureCount must be zero."
    }
    if ((Get-JsonProperty $releaseEvidence @("releaseCandidate", "identityConsistent")) -ne $true) {
        Add-Failure $failures "release-evidence-report.json releaseCandidate.identityConsistent must be true."
    }
    if ([int](Get-JsonProperty $releaseEvidence @("releaseCandidate", "evidenceIdentityCount")) -ne 6) {
        Add-Failure $failures "release-evidence-report.json releaseCandidate.evidenceIdentityCount must be 6."
    }
    if ($releaseCommitSha.Length -gt 0) {
        $releaseEvidenceCommitSha = [string](Get-JsonProperty $releaseEvidence @("releaseCandidate", "commitSha"))
        if (-not [string]::Equals($releaseEvidenceCommitSha, $releaseCommitSha, [StringComparison]::OrdinalIgnoreCase)) {
            Add-Failure $failures "release-evidence-report.json releaseCandidate.commitSha must match CommitSha."
        }
    }
    if ($releaseRunUrl.Length -gt 0) {
        $releaseEvidenceRunUrl = [string](Get-JsonProperty $releaseEvidence @("releaseCandidate", "githubActionsRunUrl"))
        if (-not [string]::Equals($releaseEvidenceRunUrl, $releaseRunUrl, [StringComparison]::OrdinalIgnoreCase)) {
            Add-Failure $failures "release-evidence-report.json releaseCandidate.githubActionsRunUrl must match GitHubActionsRunUrl."
        }
    }
    foreach ($coverageProperty in @("sourceLawSourceIds", "goldenCorpusScenarioCodes", "externalRosIxbrlScenarioCodes", "routeCodes", "manualHandoffScenarioCodes", "manualHandoffPathCodes", "releaseArtifactNames", "releaseEvidenceTemplateFiles", "releaseEvidenceWorkspaceFiles")) {
        if ($null -eq $releaseEvidence.requiredCoverage.$coverageProperty -or @($releaseEvidence.requiredCoverage.$coverageProperty).Count -eq 0) {
            Add-Failure $failures "release-evidence-report.json requiredCoverage.$coverageProperty must be present."
        }
    }
    Assert-ReleaseEvidenceTemplateManifest $releaseEvidence $resolvedDirectory.Path $requiredReleaseEvidenceTemplates $failures
    Assert-ReleaseEvidenceHumanCompletionManifest $releaseEvidence $requiredReleaseEvidenceTemplates $failures
    Assert-ReleaseEvidenceProductionScorecardCompletion $releaseEvidence $requiredReleaseEvidenceTemplates $failures
    Assert-ReleaseEvidenceWorkspaceControlManifest $releaseEvidence $resolvedDirectory.Path $requiredReleaseEvidenceWorkspaceControls $failures
    Assert-ReleaseEvidenceMachineSummary $releaseEvidenceMachineSummary $releaseEvidence $resolvedDirectory.Path $releaseCommitSha $releaseRunUrl $failures
    Assert-ReleaseEvidenceWorkspaceVerificationReport $releaseEvidenceWorkspaceVerificationReport $releaseEvidence $releaseCommitSha $releaseRunUrl $expectedReleaseEvidenceWorkspaceInventory $failures
    Assert-ReleaseEvidenceWorkspaceInventoryRetention $releaseEvidenceWorkspaceVerificationReport $resolvedDirectory.Path $expectedReleaseEvidenceWorkspaceInventory $failures
}

$evidenceFileManifest = @(
    foreach ($entry in $allEvidence.GetEnumerator()) {
        $evidence = $entry.Value
        if ($evidence.PSObject.Properties.Name -contains "__missing" -or
            $evidence.PSObject.Properties.Name -contains "__invalid") {
            continue
        }

        $filePath = [string]$evidence.__path
        $fileInfo = Get-Item -LiteralPath $filePath
        [ordered]@{
            fileName = $entry.Key
            path = $filePath
            byteSize = $fileInfo.Length
            sha256 = Get-FileSha256 $filePath
            checkedAtUtc = if ($evidence.PSObject.Properties.Name -contains "checkedAtUtc") { [string]$evidence.checkedAtUtc } else { "" }
            status = if ($evidence.PSObject.Properties.Name -contains "status") { [string]$evidence.status } else { "" }
        }
    }
)

$releaseEvidenceTemplateManifest = @(
    foreach ($template in $requiredReleaseEvidenceTemplates) {
        $templatePath = Join-Path $resolvedDirectory.Path $template.fileName
        if (-not (Test-Path -LiteralPath $templatePath -PathType Leaf)) {
            continue
        }

        $fileInfo = Get-Item -LiteralPath $templatePath
        [ordered]@{
            fileName = $template.fileName
            evidenceName = $template.evidenceName
            evidenceType = "release-evidence-template"
            path = $templatePath
            byteSize = $fileInfo.Length
            sha256 = Get-FileSha256 $templatePath
            checkedAtUtc = ""
            status = "retained"
        }
    }
)

$releaseEvidenceWorkspaceControlManifest = @(
    foreach ($control in $requiredReleaseEvidenceWorkspaceControls) {
        $controlPath = Join-Path $resolvedDirectory.Path $control.fileName
        if (-not (Test-Path -LiteralPath $controlPath -PathType Leaf)) {
            continue
        }

        $fileInfo = Get-Item -LiteralPath $controlPath
        [ordered]@{
            fileName = $control.fileName
            evidenceName = $control.evidenceName
            evidenceType = "release-evidence-workspace-control"
            path = $controlPath
            byteSize = $fileInfo.Length
            sha256 = Get-FileSha256 $controlPath
            checkedAtUtc = ""
            status = "retained"
        }
    }
)

$releaseEvidenceReviewerHandoffManifest = @(
    foreach ($handoff in $requiredReleaseEvidenceReviewerHandoffFiles) {
        $handoffPath = Join-Path $resolvedDirectory.Path $handoff.fileName
        if (-not (Test-Path -LiteralPath $handoffPath -PathType Leaf)) {
            continue
        }

        $fileInfo = Get-Item -LiteralPath $handoffPath
        [ordered]@{
            fileName = $handoff.fileName
            evidenceName = $handoff.evidenceName
            evidenceType = "release-evidence-reviewer-handoff"
            path = $handoffPath
            byteSize = $fileInfo.Length
            sha256 = Get-FileSha256 $handoffPath
            checkedAtUtc = ""
            status = "retained"
        }
    }
)

$releaseEvidenceWorkspaceSummary = [ordered]@{
    status = "missing"
    verificationStatus = ""
    failureCount = $null
    releaseCandidateCommitSha = ""
    releaseCandidateRunUrl = ""
    requiredWorkspaceFileCount = 0
    workspaceFileCount = 0
    preparedHumanTemplateControlCount = 0
    pendingHumanEvidenceBlockerCount = 0
    reviewerAssignmentInventoryCount = 0
    unassignedReviewerAssignmentCount = 0
    blankReviewerAssignmentFieldCount = 0
    reviewerAssignmentPickupFileGuidanceCount = 0
    reviewerAssignmentPickupFiles = @()
}

$releaseEvidenceScorecardSummary = [ordered]@{
    status = "missing"
    scorecardStatus = ""
    currentScore = $null
    targetScore = $null
    acceptedHumanEvidenceCount = 0
    requiredHumanEvidenceCount = $requiredReleaseEvidenceTemplates.Count
    remainingHumanEvidenceCount = 0
    categoryScores = @()
}

if (-not ($releaseEvidence.PSObject.Properties.Name -contains "__missing") -and
    -not ($releaseEvidence.PSObject.Properties.Name -contains "__invalid")) {
    $scorecardCompletion = Get-JsonProperty $releaseEvidence @("productionScorecardCompletion")
    if ($null -ne $scorecardCompletion) {
        $releaseEvidenceScorecardSummary = [ordered]@{
            status = "retained"
            scorecardStatus = [string](Get-JsonProperty $scorecardCompletion @("status"))
            currentScore = [int](Get-JsonProperty $scorecardCompletion @("currentScore"))
            targetScore = [int](Get-JsonProperty $scorecardCompletion @("targetScore"))
            acceptedHumanEvidenceCount = [int](Get-JsonProperty $scorecardCompletion @("acceptedHumanEvidenceCount"))
            requiredHumanEvidenceCount = [int](Get-JsonProperty $scorecardCompletion @("requiredHumanEvidenceCount"))
            remainingHumanEvidenceCount = @((Get-JsonProperty $scorecardCompletion @("remainingHumanEvidence"))).Count
            categoryScores = @((Get-JsonProperty $scorecardCompletion @("categories")) | ForEach-Object {
                [ordered]@{
                    code = [string](Get-JsonProperty $_ @("code"))
                    currentScore = [int](Get-JsonProperty $_ @("currentScore"))
                    targetScore = [int](Get-JsonProperty $_ @("targetScore"))
                    completionGate = [string](Get-JsonProperty $_ @("completionGate"))
                }
            })
        }
    }
}

if (-not ($releaseEvidenceWorkspaceVerificationReport.PSObject.Properties.Name -contains "__missing") -and
    -not ($releaseEvidenceWorkspaceVerificationReport.PSObject.Properties.Name -contains "__invalid")) {
    $assignmentInventory = @((Get-JsonProperty $releaseEvidenceWorkspaceVerificationReport @("reviewerAssignmentInventory")))
    $assignmentPickupFileGuidance = @(
        foreach ($assignment in $assignmentInventory) {
            $evidenceName = [string](Get-JsonProperty $assignment @("evidenceName"))
            $required = $requiredReleaseEvidenceTemplates |
                Where-Object { [string](Get-JsonProperty $_ @("evidenceName")) -eq $evidenceName } |
                Select-Object -First 1
            $pickupFiles = @((Get-JsonProperty $assignment @("reviewerPickupFiles")) | ForEach-Object { [string]$_ })
            $requiredPickupFiles = if ($null -eq $required) {
                @()
            } else {
                @((Get-JsonProperty $required @("requiredPickupFiles")) | ForEach-Object { [string]$_ })
            }
            $missingPickupFiles = @($requiredPickupFiles | Where-Object { -not ($pickupFiles -contains $_) })

            [ordered]@{
                evidenceName = $evidenceName
                templateFile = [string](Get-JsonProperty $assignment @("templateFile"))
                signOffGate = [string](Get-JsonProperty $assignment @("signOffGate"))
                requiredReviewerRole = [string](Get-JsonProperty $assignment @("requiredReviewerRole"))
                pickupFileCount = $pickupFiles.Count
                requiredPickupFileCount = $requiredPickupFiles.Count
                missingPickupFileCount = $missingPickupFiles.Count
                reviewerPickupFiles = @($pickupFiles)
            }
        }
    )
    $assignmentPickupFileGuidanceCount = @(
        foreach ($pickupGuidance in $assignmentPickupFileGuidance) {
            if ([int]$pickupGuidance["requiredPickupFileCount"] -gt 0 -and [int]$pickupGuidance["missingPickupFileCount"] -eq 0) {
                $pickupGuidance
            }
        }
    ).Count

    $releaseEvidenceWorkspaceSummary = [ordered]@{
        status = "retained"
        verificationStatus = [string](Get-JsonProperty $releaseEvidenceWorkspaceVerificationReport @("status"))
        failureCount = [int](Get-JsonProperty $releaseEvidenceWorkspaceVerificationReport @("failureCount"))
        releaseCandidateCommitSha = [string](Get-JsonProperty $releaseEvidenceWorkspaceVerificationReport @("releaseCandidate", "commitSha"))
        releaseCandidateRunUrl = [string](Get-JsonProperty $releaseEvidenceWorkspaceVerificationReport @("releaseCandidate", "githubActionsRunUrl"))
        requiredWorkspaceFileCount = @((Get-JsonProperty $releaseEvidenceWorkspaceVerificationReport @("requiredWorkspaceFiles"))).Count
        workspaceFileCount = @((Get-JsonProperty $releaseEvidenceWorkspaceVerificationReport @("workspaceFiles"))).Count
        preparedHumanTemplateControlCount = @((Get-JsonProperty $releaseEvidenceWorkspaceVerificationReport @("preparedHumanTemplateControls"))).Count
        pendingHumanEvidenceBlockerCount = @((Get-JsonProperty $releaseEvidenceWorkspaceVerificationReport @("pendingHumanEvidenceBlockers"))).Count
        reviewerAssignmentInventoryCount = $assignmentInventory.Count
        unassignedReviewerAssignmentCount = @($assignmentInventory | Where-Object { [string](Get-JsonProperty $_ @("assignmentStatus")) -eq "unassigned" }).Count
        blankReviewerAssignmentFieldCount = @($assignmentInventory | Where-Object {
            [string]::IsNullOrWhiteSpace([string](Get-JsonProperty $_ @("assignedReviewerName"))) -and
            [string]::IsNullOrWhiteSpace([string](Get-JsonProperty $_ @("assignedReviewerEmail"))) -and
            [string]::IsNullOrWhiteSpace([string](Get-JsonProperty $_ @("dueAtUtc")))
        }).Count
        reviewerAssignmentPickupFileGuidanceCount = $assignmentPickupFileGuidanceCount
        reviewerAssignmentPickupFiles = @($assignmentPickupFileGuidance)
    }
}

$report = [ordered]@{
    status = if ($failures.Count -eq 0) { "passed" } else { "failed" }
    checkedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    evidenceDirectory = $resolvedDirectory.Path
    releaseCandidate = [ordered]@{
        commitSha = $releaseCommitSha
        githubActionsRunUrl = $releaseRunUrl
        identityProvided = ($releaseCommitSha.Length -gt 0 -and $releaseRunUrl.Length -gt 0)
    }
    requiredFiles = @($allEvidence.Keys) + @($requiredReleaseEvidenceTemplates | ForEach-Object { $_.fileName }) + @($requiredReleaseEvidenceWorkspaceControls | ForEach-Object { $_.fileName }) + @($requiredReleaseEvidenceReviewerHandoffFiles | ForEach-Object { $_.fileName })
    evidenceFiles = @($evidenceFileManifest) + @($releaseEvidenceTemplateManifest) + @($releaseEvidenceWorkspaceControlManifest) + @($releaseEvidenceReviewerHandoffManifest)
    releaseEvidenceScorecardSummary = $releaseEvidenceScorecardSummary
    releaseEvidenceWorkspaceSummary = $releaseEvidenceWorkspaceSummary
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
    throw "Release artifact pack verification failed with $($failures.Count) issue(s)."
}

Write-Host "Release artifact pack verification passed for $($resolvedDirectory.Path)."
