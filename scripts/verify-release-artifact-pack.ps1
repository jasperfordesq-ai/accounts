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
$expectedAccountantWorkbenchViewports = @("desktop", "mobile")
$expectedAccountantWorkbenchThemeViewportCoverage = @("dark/desktop", "dark/mobile", "light/desktop", "light/mobile")

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
    "mobile-density",
    "loading-error-empty-states"
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
    "visual smoke screenshots carry passed automated theme contrast results"
)

$expectedAccountantWorkbenchEvidenceFiles = @(
    "visual-smoke-manifest.json",
    "visual-smoke-evidence-report.json",
    "accountant-workbench-evidence-report.json"
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
    "release-evidence-reviewer-blockers.md",
    "release-evidence-report.json",
    "release-evidence-verifier-output.txt"
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
            if ([int]$readiness.screenshotCount -ne 4) {
                Add-Failure $Failures "accountant-workbench-evidence-report.json routeReadiness.$($expected.routeName).screenshotCount must be 4."
            }
            if ([int]$readiness.layoutCheckResultCount -ne 12) {
                Add-Failure $Failures "accountant-workbench-evidence-report.json routeReadiness.$($expected.routeName).layoutCheckResultCount must be 12."
            }
            if ([int]$readiness.contrastCheckResultCount -ne 4) {
                Add-Failure $Failures "accountant-workbench-evidence-report.json routeReadiness.$($expected.routeName).contrastCheckResultCount must be 4."
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
        if ([string]$acceptance.screenshotReviewEvidence -ne "$($expected.routeName)-light-dark-desktop-mobile-screenshot-review") {
            Add-Failure $Failures "accountant-workbench-evidence-report.json routeAcceptance.$($expected.routeName).screenshotReviewEvidence must be $($expected.routeName)-light-dark-desktop-mobile-screenshot-review."
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
        [pscustomobject]@{ name = "desktop"; width = 1440; height = 1000 },
        [pscustomobject]@{ name = "mobile"; width = 390; height = 844 }
    )
    $expectedThemes = @("light", "dark")
    $expectedLayoutChecks = @(
        "browser-console-errors",
        "page-horizontal-overflow",
        "visible-text-overlap"
    )
    $expectedContrastCheck = "theme-contrast"
    $minimumContrastRatio = [decimal]3.0

    Assert-ArrayContainsExactly @((Get-JsonProperty $VisualSmoke @("themes"))) $expectedThemes "visual-smoke-evidence-report.json themes" $Failures
    Assert-ArrayContainsExactly @((Get-JsonProperty $VisualSmoke @("viewports"))) @($expectedViewports | ForEach-Object { $_.name }) "visual-smoke-evidence-report.json viewports" $Failures

    if ([int](Get-JsonProperty $VisualSmoke @("layoutCheckResultCount")) -ne 84) {
        Add-Failure $Failures "visual-smoke-evidence-report.json layoutCheckResultCount must be 84."
    }
    if ([string](Get-JsonProperty $VisualSmoke @("layoutChecksPassed")) -ne "True") {
        Add-Failure $Failures "visual-smoke-evidence-report.json layoutChecksPassed must be true."
    }
    if ([int](Get-JsonProperty $VisualSmoke @("contrastCheckResultCount")) -ne 28) {
        Add-Failure $Failures "visual-smoke-evidence-report.json contrastCheckResultCount must be 28."
    }
    if ([string](Get-JsonProperty $VisualSmoke @("themeContrastChecksPassed")) -ne "True") {
        Add-Failure $Failures "visual-smoke-evidence-report.json themeContrastChecksPassed must be true."
    }
    if ([decimal](Get-JsonProperty $VisualSmoke @("minimumContrastRatio")) -lt $minimumContrastRatio) {
        Add-Failure $Failures "visual-smoke-evidence-report.json minimumContrastRatio must be at least 3."
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
        if (@($routeCoverage).Count -ne $expectedAccountantWorkbenchRouteAcceptance.Count) {
            Add-Failure $Failures "visual-smoke-evidence-report.json routeCoverage must include exactly 7 route(s)."
        }
        foreach ($expectedRoute in $expectedAccountantWorkbenchRouteAcceptance) {
            $actualRoute = @($routeCoverage) | Where-Object { [string](Get-JsonProperty $_ @("routeName")) -eq $expectedRoute.routeName } | Select-Object -First 1
            if ($null -eq $actualRoute) {
                Add-Failure $Failures "visual-smoke-evidence-report.json routeCoverage must include $($expectedRoute.routeName)."
                continue
            }
            if ([string](Get-JsonProperty $actualRoute @("routeKey")) -ne [string]$expectedRoute.routeKey) {
                Add-Failure $Failures "visual-smoke-evidence-report.json routeCoverage.$($expectedRoute.routeName).routeKey must be $($expectedRoute.routeKey)."
            }
            if ([int](Get-JsonProperty $actualRoute @("screenshotCount")) -ne 4) {
                Add-Failure $Failures "visual-smoke-evidence-report.json routeCoverage.$($expectedRoute.routeName).screenshotCount must be 4."
            }
            if ([string](Get-JsonProperty $actualRoute @("reviewStatus")) -ne "required-review") {
                Add-Failure $Failures "visual-smoke-evidence-report.json routeCoverage.$($expectedRoute.routeName).reviewStatus must be required-review."
            }
            foreach ($reviewCheck in $expectedAccountantWorkbenchReviewChecks) {
                Assert-ArrayContains @((Get-JsonProperty $actualRoute @("requiredReviewChecks"))) $reviewCheck "visual-smoke-evidence-report.json routeCoverage.$($expectedRoute.routeName).requiredReviewChecks" $Failures
            }
        }
    }

    $screenshots = Get-JsonProperty $VisualSmoke @("screenshots")
    if ($null -eq $screenshots -or @($screenshots).Count -eq 0) {
        Add-Failure $Failures "visual-smoke-evidence-report.json screenshots must include PNG dimension evidence."
        return
    }

    if (@($screenshots).Count -ne 28) {
        Add-Failure $Failures "visual-smoke-evidence-report.json screenshots must include exactly 28 retained screenshots."
    }

    foreach ($expectedRoute in $expectedAccountantWorkbenchRouteAcceptance) {
        foreach ($theme in $expectedThemes) {
            foreach ($expectedViewport in $expectedViewports) {
                $expectedFileName = "$($expectedRoute.routeName)-$theme-$($expectedViewport.name).png"
                $actualScreenshot = @($screenshots) | Where-Object {
                    [string](Get-JsonProperty $_ @("routeName")) -eq [string]$expectedRoute.routeName -and
                    [string](Get-JsonProperty $_ @("theme")) -eq [string]$theme -and
                    [string](Get-JsonProperty $_ @("viewportName")) -eq [string]$expectedViewport.name
                } | Select-Object -First 1

                if ($null -eq $actualScreenshot) {
                    Add-Failure $Failures "visual-smoke-evidence-report.json screenshots must include $($expectedRoute.routeName)/$theme/$($expectedViewport.name)."
                    continue
                }
                if ([string](Get-JsonProperty $actualScreenshot @("routeKey")) -ne [string]$expectedRoute.routeKey) {
                    Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.$($expectedRoute.routeName).$theme.$($expectedViewport.name).routeKey must be $($expectedRoute.routeKey)."
                }
                if ([string](Get-JsonProperty $actualScreenshot @("fileName")) -ne $expectedFileName) {
                    Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.$($expectedRoute.routeName).$theme.$($expectedViewport.name).fileName must be $expectedFileName."
                }
                if ([string](Get-JsonProperty $actualScreenshot @("expectedText")) -ne [string]$expectedRoute.expectedText) {
                    Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.$($expectedRoute.routeName).$theme.$($expectedViewport.name).expectedText must be $($expectedRoute.expectedText)."
                }
                if ([string](Get-JsonProperty $actualScreenshot @("reviewStatus")) -ne "required-review") {
                    Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.$($expectedRoute.routeName).$theme.$($expectedViewport.name).reviewStatus must be required-review."
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
            if ([decimal](Get-JsonProperty $themeContrastResult @("minimumContrastRatio")) -lt $minimumContrastRatio) {
                Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.themeContrastResult.minimumContrastRatio must be at least 3."
            }
            if ([decimal](Get-JsonProperty $themeContrastResult @("requiredMinimumContrastRatio")) -ne $minimumContrastRatio) {
                Add-Failure $Failures "visual-smoke-evidence-report.json screenshots.themeContrastResult.requiredMinimumContrastRatio must be 3."
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
    if ([int](Get-JsonProperty $VisualManifest @("expectedScreenshotCount")) -ne 28) {
        Add-Failure $Failures "visual-smoke-manifest.json expectedScreenshotCount must be 28."
    }

    Assert-ArrayContainsExactly @((Get-JsonProperty $VisualManifest @("layoutChecks"))) $expectedAccountantWorkbenchLayoutChecks "visual-smoke-manifest.json layoutChecks" $Failures
    Assert-ArrayContainsExactly @((Get-JsonProperty $VisualManifest @("reviewChecks"))) $expectedAccountantWorkbenchReviewChecks "visual-smoke-manifest.json reviewChecks" $Failures

    $routeAudits = @(Get-JsonProperty $VisualManifest @("routeAudits"))
    if ($routeAudits.Count -ne $expectedAccountantWorkbenchRouteAcceptance.Count) {
        Add-Failure $Failures "visual-smoke-manifest.json routeAudits must include exactly 7 route(s)."
    }

    foreach ($expectedRoute in $expectedAccountantWorkbenchRouteAcceptance) {
        $routeAudit = $routeAudits |
            Where-Object { [string](Get-JsonProperty $_ @("routeName")) -eq [string]$expectedRoute.routeName } |
            Select-Object -First 1

        if ($null -eq $routeAudit) {
            Add-Failure $Failures "visual-smoke-manifest.json routeAudits must include $($expectedRoute.routeName)."
            continue
        }

        if ([string](Get-JsonProperty $routeAudit @("routeKey")) -ne [string]$expectedRoute.routeKey) {
            Add-Failure $Failures "visual-smoke-manifest.json routeAudits.$($expectedRoute.routeName).routeKey must be $($expectedRoute.routeKey)."
        }
        if ([string](Get-JsonProperty $routeAudit @("label")) -ne [string]$expectedRoute.label) {
            Add-Failure $Failures "visual-smoke-manifest.json routeAudits.$($expectedRoute.routeName).label must be $($expectedRoute.label)."
        }
        Assert-ArrayContainsExactly @((Get-JsonProperty $routeAudit @("workflowStages"))) @($expectedRoute.workflowStages) "visual-smoke-manifest.json routeAudits.$($expectedRoute.routeName).workflowStages" $Failures
        if ([int](Get-JsonProperty $routeAudit @("screenshotCount")) -ne 4) {
            Add-Failure $Failures "visual-smoke-manifest.json routeAudits.$($expectedRoute.routeName).screenshotCount must be 4."
        }
        if ([string](Get-JsonProperty $routeAudit @("reviewStatus")) -ne "required-review") {
            Add-Failure $Failures "visual-smoke-manifest.json routeAudits.$($expectedRoute.routeName).reviewStatus must be required-review."
        }
        Assert-ArrayContainsExactly @((Get-JsonProperty $routeAudit @("reviewChecks"))) $expectedAccountantWorkbenchReviewChecks "visual-smoke-manifest.json routeAudits.$($expectedRoute.routeName).reviewChecks" $Failures
    }

    $manifestScreenshots = @(Get-JsonProperty $VisualManifest @("screenshots"))
    $evidenceScreenshots = @(Get-JsonProperty $VisualSmoke @("screenshots"))
    if ($manifestScreenshots.Count -ne 28) {
        Add-Failure $Failures "visual-smoke-manifest.json screenshots must include exactly 28 retained screenshots."
    }

    $expectedViewports = @(
        [pscustomobject]@{ name = "desktop"; width = 1440; height = 1000 },
        [pscustomobject]@{ name = "mobile"; width = 390; height = 844 }
    )

    foreach ($expectedRoute in $expectedAccountantWorkbenchRouteAcceptance) {
        foreach ($theme in $expectedAccountantWorkbenchThemes) {
            foreach ($expectedViewport in $expectedViewports) {
                $expectedFileName = "$($expectedRoute.routeName)-$theme-$($expectedViewport.name).png"
                $manifestScreenshot = $manifestScreenshots |
                    Where-Object {
                        [string](Get-JsonProperty $_ @("routeName")) -eq [string]$expectedRoute.routeName -and
                        [string](Get-JsonProperty $_ @("theme")) -eq [string]$theme -and
                        [string](Get-JsonProperty $_ @("viewportName")) -eq [string]$expectedViewport.name
                    } |
                    Select-Object -First 1
                $evidenceScreenshot = $evidenceScreenshots |
                    Where-Object {
                        [string](Get-JsonProperty $_ @("routeName")) -eq [string]$expectedRoute.routeName -and
                        [string](Get-JsonProperty $_ @("theme")) -eq [string]$theme -and
                        [string](Get-JsonProperty $_ @("viewportName")) -eq [string]$expectedViewport.name
                    } |
                    Select-Object -First 1

                if ($null -eq $manifestScreenshot) {
                    Add-Failure $Failures "visual-smoke-manifest.json screenshots must include $($expectedRoute.routeName)/$theme/$($expectedViewport.name)."
                    continue
                }
                if ($null -eq $evidenceScreenshot) {
                    continue
                }

                if ([string](Get-JsonProperty $manifestScreenshot @("routeKey")) -ne [string]$expectedRoute.routeKey) {
                    Add-Failure $Failures "visual-smoke-manifest.json screenshots.$($expectedRoute.routeName).$theme.$($expectedViewport.name).routeKey must be $($expectedRoute.routeKey)."
                }
                if ([string](Get-JsonProperty $manifestScreenshot @("fileName")) -ne $expectedFileName) {
                    Add-Failure $Failures "visual-smoke-manifest.json screenshots.$($expectedRoute.routeName).$theme.$($expectedViewport.name).fileName must be $expectedFileName."
                }
                if ([IO.Path]::GetFileName([string](Get-JsonProperty $manifestScreenshot @("artifactPath"))) -ne $expectedFileName) {
                    Add-Failure $Failures "visual-smoke-manifest.json screenshots.$($expectedRoute.routeName).$theme.$($expectedViewport.name).artifactPath must end with $expectedFileName."
                }
                if ([string](Get-JsonProperty $manifestScreenshot @("expectedText")) -ne [string]$expectedRoute.expectedText) {
                    Add-Failure $Failures "visual-smoke-manifest.json screenshots.$($expectedRoute.routeName).$theme.$($expectedViewport.name).expectedText must be $($expectedRoute.expectedText)."
                }
                if ([string](Get-JsonProperty $manifestScreenshot @("reviewStatus")) -ne "required-review") {
                    Add-Failure $Failures "visual-smoke-manifest.json screenshots.$($expectedRoute.routeName).$theme.$($expectedViewport.name).reviewStatus must be required-review."
                }

                foreach ($field in @("fileName", "routeKey", "expectedText", "reviewStatus", "byteSize", "sha256", "imageWidth", "minimumViewportHeight")) {
                    if ([string](Get-JsonProperty $manifestScreenshot @($field)) -ne [string](Get-JsonProperty $evidenceScreenshot @($field))) {
                        Add-Failure $Failures "visual-smoke-manifest.json screenshots.$($expectedRoute.routeName).$theme.$($expectedViewport.name).$field must match visual-smoke-evidence-report.json."
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
$resolvedDirectory = Resolve-Path -LiteralPath $EvidenceDirectory -ErrorAction Stop
$releaseCommitSha = $CommitSha.Trim()
$releaseRunUrl = $GitHubActionsRunUrl.Trim()

if (($releaseCommitSha.Length -gt 0 -and $releaseRunUrl.Length -eq 0) -or
    ($releaseCommitSha.Length -eq 0 -and $releaseRunUrl.Length -gt 0)) {
    Add-Failure $failures "CommitSha and GitHubActionsRunUrl must be provided together when release candidate identity is supplied."
}

if ($releaseCommitSha.Length -gt 0 -and $releaseCommitSha -notmatch '^[0-9a-fA-F]{7,40}$') {
    Add-Failure $failures "CommitSha must be a 7-40 character hexadecimal Git commit SHA."
}

if ($releaseRunUrl.Length -gt 0 -and $releaseRunUrl -notmatch '^https://github\.com/.+/actions/runs/[0-9]+') {
    Add-Failure $failures "GitHubActionsRunUrl must be a GitHub Actions run URL."
}

$dependency = Read-JsonEvidence $resolvedDirectory.Path "dependency-audit-report.json" $failures
$productionSafety = Read-JsonEvidence $resolvedDirectory.Path "production-safety-report.json" $failures
$monitoring = Read-JsonEvidence $resolvedDirectory.Path "monitoring-error-routing-report.json" $failures
$structuredLog = Read-JsonEvidence $resolvedDirectory.Path "structured-log-report.json" $failures
$restore = Read-JsonEvidence $resolvedDirectory.Path "restore-drill-report.json" $failures
$noDirectSubmission = Read-JsonEvidence $resolvedDirectory.Path "no-direct-filing-submission-report.json" $failures
$productionReadiness = Read-JsonEvidence $resolvedDirectory.Path "production-readiness-report.json" $failures
$productionReadinessVerification = Read-JsonEvidence $resolvedDirectory.Path "production-readiness-verification-report.json" $failures
$visualManifest = Read-JsonEvidence $resolvedDirectory.Path "visual-smoke-manifest.json" $failures
$visualSmoke = Read-JsonEvidence $resolvedDirectory.Path "visual-smoke-evidence-report.json" $failures
$accountantWorkbench = Read-JsonEvidence $resolvedDirectory.Path "accountant-workbench-evidence-report.json" $failures
$releaseEvidence = Read-JsonEvidence $resolvedDirectory.Path "release-evidence-report.json" $failures
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
    "backup-restore-drill",
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
    [pscustomobject]@{ evidenceName = "visualQa"; fileName = "visual-qa-signoff-template.md"; requiredReviewerRole = "Named visual QA reviewer"; signOffGate = "visual-qa-screenshot-review" },
    [pscustomobject]@{ evidenceName = "sourceLawReview"; fileName = "source-law-review-template.md"; requiredReviewerRole = "Named source-law reviewer plus qualified accountant"; signOffGate = "source-law-change-review" },
    [pscustomobject]@{ evidenceName = "externalRosIxbrlValidation"; fileName = "external-ros-ixbrl-validation-template.md"; requiredReviewerRole = "External ROS/iXBRL validation reviewer"; signOffGate = "external-ros-validation-evidence" },
    [pscustomobject]@{ evidenceName = "qualifiedAccountantAcceptance"; fileName = "qualified-accountant-acceptance-template.md"; requiredReviewerRole = "Named qualified accountant"; signOffGate = "qualified-accountant-final-signoff" },
    [pscustomobject]@{ evidenceName = "manualHandoffAcceptance"; fileName = "manual-handoff-acceptance-template.md"; requiredReviewerRole = "Named manual handoff reviewer"; signOffGate = "manual-accountant-acceptance" },
    [pscustomobject]@{ evidenceName = "monitoringProviderConfirmation"; fileName = "monitoring-provider-confirmation-template.md"; requiredReviewerRole = "Named release operator"; signOffGate = "production-monitoring" }
)

$requiredReleaseEvidenceWorkspaceControls = @(
    [pscustomobject]@{ evidenceName = "releaseEvidenceWorkspaceManifest"; fileName = "release-evidence-workspace-manifest.json" },
    [pscustomobject]@{ evidenceName = "releaseEvidenceMachineSummary"; fileName = "release-evidence-machine-summary.json" },
    [pscustomobject]@{ evidenceName = "releaseEvidenceWorkspaceVerificationReport"; fileName = "release-evidence-workspace-verification-report.json" }
)

$requiredReleaseEvidenceReviewerHandoffFiles = @(
    [pscustomobject]@{ evidenceName = "releaseEvidenceReviewerIndex"; fileName = "release-evidence-reviewer-index.md" },
    [pscustomobject]@{ evidenceName = "releaseEvidenceReviewerCompletion"; fileName = "release-evidence-reviewer-completion.json" },
    [pscustomobject]@{ evidenceName = "releaseEvidenceReviewerBlockers"; fileName = "release-evidence-reviewer-blockers.md" },
    [pscustomobject]@{ evidenceName = "releaseEvidenceVerifierOutput"; fileName = "release-evidence-verifier-output.txt" }
)

$allEvidence = [ordered]@{
    "dependency-audit-report.json" = $dependency
    "production-safety-report.json" = $productionSafety
    "monitoring-error-routing-report.json" = $monitoring
    "structured-log-report.json" = $structuredLog
    "restore-drill-report.json" = $restore
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
    Assert-Truthy $productionSafety.seedSafety.bootstrapOwnerPasswordOnlyOnMigrate "production-safety-report.json seedSafety.bootstrapOwnerPasswordOnlyOnMigrate" $failures
    if ($productionSafety.workflowSafety.productionSmokeUsesBuildFlag -ne $false) {
        Add-Failure $failures "production-safety-report.json workflowSafety.productionSmokeUsesBuildFlag must be false."
    }
}

if (-not ($monitoring.PSObject.Properties.Name -contains "__missing")) {
    Assert-NonEmptyString $monitoring.provider "monitoring-error-routing-report.json provider" $failures
    Assert-NonEmptyString $monitoring.eventId "monitoring-error-routing-report.json eventId" $failures
    Assert-NonEmptyString $monitoring.correlationId "monitoring-error-routing-report.json correlationId" $failures
    Assert-NonEmptyString $monitoring.baseUrl "monitoring-error-routing-report.json baseUrl" $failures
}

if (-not ($structuredLog.PSObject.Properties.Name -contains "__missing")) {
    if ([int]$structuredLog.jsonLogLineCount -le 0) {
        Add-Failure $failures "structured-log-report.json jsonLogLineCount must be greater than zero."
    }
    Assert-Truthy $structuredLog.matchedMonitoringSmokeLine "structured-log-report.json matchedMonitoringSmokeLine" $failures
    if (-not [string]::IsNullOrWhiteSpace([string]$monitoring.correlationId) -and
        [string]$structuredLog.monitoringCorrelationId -ne [string]$monitoring.correlationId) {
        Add-Failure $failures "structured-log-report.json monitoringCorrelationId must match monitoring-error-routing-report.json correlationId."
    }
}

if (-not ($restore.PSObject.Properties.Name -contains "__missing")) {
    if ([string]$restore.backupSha256 -notmatch '^[0-9a-f]{64}$') {
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
        if ([int]$productionReadiness.productionScorecard.targetScore -ne 700) {
            Add-Failure $failures "production-readiness-report.json productionScorecard.targetScore must be 700."
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
    if ([int]$productionReadinessVerification.requiredCoverage.expectedVisualScreenshotCount -ne 28) {
        Add-Failure $failures "production-readiness-verification-report.json requiredCoverage.expectedVisualScreenshotCount must be 28."
    }
    if (-not [string]::IsNullOrWhiteSpace([string]$productionReadiness.__path) -and
        -not [string]::IsNullOrWhiteSpace([string]$productionReadinessVerification.reportPath) -and
        [IO.Path]::GetFileName([string]$productionReadinessVerification.reportPath) -ne "production-readiness-report.json") {
        Add-Failure $failures "production-readiness-verification-report.json reportPath must reference production-readiness-report.json."
    }
}

if (-not ($visualSmoke.PSObject.Properties.Name -contains "__missing")) {
    if ([int]$visualSmoke.screenshotCount -ne 28 -or [int]$visualSmoke.expectedScreenshotCount -ne 28) {
        Add-Failure $failures "visual-smoke-evidence-report.json must cover 28 expected screenshots."
    }
    if ([int]$visualSmoke.routeCount -ne 7) {
        Add-Failure $failures "visual-smoke-evidence-report.json routeCount must be 7."
    }
    Assert-VisualSmokeDimensionEvidence $visualSmoke $resolvedDirectory.Path $failures
}

Assert-VisualSmokeManifestEvidence $visualManifest $visualSmoke $failures

if (-not ($accountantWorkbench.PSObject.Properties.Name -contains "__missing")) {
    if ([int]$accountantWorkbench.routeCount -ne 7) {
        Add-Failure $failures "accountant-workbench-evidence-report.json routeCount must be 7."
    }
    if ([int]$accountantWorkbench.screenshotCount -ne 28 -or [int]$accountantWorkbench.expectedScreenshotCount -ne 28) {
        Add-Failure $failures "accountant-workbench-evidence-report.json must cover 28 expected screenshots."
    }
    if ([int]$accountantWorkbench.routeAcceptanceCount -ne 7) {
        Add-Failure $failures "accountant-workbench-evidence-report.json routeAcceptanceCount must be 7."
    }
    foreach ($coverageProperty in @("routeCodes", "routeKeys", "workflowStages", "themes", "viewports", "reviewChecks", "layoutChecks", "expectedTextChecks", "routeAcceptanceEvidence", "evidenceFiles")) {
        if ($null -eq $accountantWorkbench.requiredCoverage.$coverageProperty -or @($accountantWorkbench.requiredCoverage.$coverageProperty).Count -eq 0) {
            Add-Failure $failures "accountant-workbench-evidence-report.json requiredCoverage.$coverageProperty must be present."
        }
    }
    if ([string]$accountantWorkbench.requiredCoverage.routeAcceptanceSignOffGate -ne "qualified-accountant-route-acceptance") {
        Add-Failure $failures "accountant-workbench-evidence-report.json requiredCoverage.routeAcceptanceSignOffGate must be qualified-accountant-route-acceptance."
    }
    Assert-AccountantWorkbenchRequiredCoverage $accountantWorkbench $failures
    foreach ($requiredEvidenceFile in @("visual-smoke-manifest.json", "visual-smoke-evidence-report.json", "accountant-workbench-evidence-report.json")) {
        Assert-ArrayContains @($accountantWorkbench.requiredCoverage.evidenceFiles) $requiredEvidenceFile "accountant-workbench-evidence-report.json requiredCoverage.evidenceFiles" $failures
    }
    Assert-ArrayContains @($accountantWorkbench.requiredCoverage.expectedTextChecks) "visual smoke screenshots carry route expected accountant decision text" "accountant-workbench-evidence-report.json requiredCoverage.expectedTextChecks" $failures
    foreach ($route in @($accountantWorkbench.routeReadiness)) {
        if ([int]$route.expectedTextEvidenceCount -ne 4) {
            Add-Failure $failures "accountant-workbench-evidence-report.json routeReadiness.expectedTextEvidenceCount must be 4 for every route."
        }
    }
    foreach ($route in @($accountantWorkbench.routeAcceptance)) {
        Assert-NonEmptyString $route.routeName "accountant-workbench-evidence-report.json routeAcceptance.routeName" $failures
        Assert-NonEmptyString $route.routeKey "accountant-workbench-evidence-report.json routeAcceptance.routeKey" $failures
        Assert-NonEmptyString $route.expectedText "accountant-workbench-evidence-report.json routeAcceptance.expectedText" $failures
        Assert-Truthy $route.blocksRelease "accountant-workbench-evidence-report.json routeAcceptance.blocksRelease" $failures
        if ([string]$route.signOffGate -ne "qualified-accountant-route-acceptance") {
            Add-Failure $failures "accountant-workbench-evidence-report.json routeAcceptance.signOffGate must be qualified-accountant-route-acceptance."
        }
        if (-not (@($route.requiredAcceptanceEvidence) | Where-Object { [string]$_ -like "*qualified-accountant-route-acceptance" })) {
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
    Assert-ReleaseEvidenceWorkspaceControlManifest $releaseEvidence $resolvedDirectory.Path $requiredReleaseEvidenceWorkspaceControls $failures
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
