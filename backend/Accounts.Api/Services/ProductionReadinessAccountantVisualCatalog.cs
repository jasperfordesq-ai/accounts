namespace Accounts.Api.Services;

public partial class ProductionReadinessReportService
{
    private static IReadOnlyList<AccountantAcceptanceCriterion> BuildAccountantAcceptanceCriteria(IReadOnlyList<GoldenFilingCorpusScenario> goldenCorpus) =>
    [
        new(
            "micro-ltd",
            "Micro LTD accountant acceptance",
            true,
            "qualified-accountant-review-required",
            [
                "PDF wording and micro statutory statement",
                "iXBRL XML and Revenue taxonomy selection",
                "filing readiness profile at 100%",
                "tax computation and notes disclosure set",
                "director and secretary signatory gates"
            ],
            [
                "Named qualified-accountant approval recorded against the generated pack.",
                "External ROS/iXBRL validation evidence recorded before Revenue use.",
                "Director and secretary certification evidence reviewed."
            ],
            "Named qualified accountant must approve the generated pack before real filing use.",
            [
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.FrcFrs105,
                IrishStatutoryRuleSources.RevenueAcceptedTaxonomies
            ],
            AcceptanceVerifiers(goldenCorpus, "micro-ltd")),
        new(
            "small-abridged-ltd",
            "Small abridged LTD accountant acceptance",
            true,
            "qualified-accountant-review-required",
            [
                "Full accounts PDF wording and abridged CRO pack wording",
                "Section 352 abridgement evidence",
                "iXBRL XML and public profit-and-loss omission checks",
                "tax computation and small-company notes",
                "director and secretary signatory gates"
            ],
            [
                "Named qualified-accountant approval recorded against full and abridged generated packs.",
                "Abridgement eligibility and audit exemption evidence reviewed.",
                "External ROS/iXBRL validation evidence recorded before Revenue use."
            ],
            "Named qualified accountant must approve both the full accounts pack and abridged CRO pack before real filing use.",
            [
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.FrcFrs102,
                IrishStatutoryRuleSources.RevenueIxbrlContents
            ],
            AcceptanceVerifiers(goldenCorpus, "small-abridged-ltd")),
        new(
            "dac-small",
            "Small DAC accountant acceptance",
            true,
            "qualified-accountant-review-required",
            [
                "DAC accounts PDF wording and directors' report evidence",
                "iXBRL XML and Revenue taxonomy selection",
                "filing readiness profile with DAC supported-path evidence",
                "tax computation and small-company notes",
                "director and secretary signatory gates"
            ],
            [
                "Named qualified-accountant approval recorded against the DAC pack.",
                "DAC company type, audit exemption and small-company filing regime evidence reviewed.",
                "External ROS/iXBRL validation evidence recorded before Revenue use."
            ],
            "Named qualified accountant must approve the DAC generated pack before real filing use.",
            [
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.FrcFrs102,
                IrishStatutoryRuleSources.RevenueAcceptedTaxonomies
            ],
            AcceptanceVerifiers(goldenCorpus, "dac-small")),
        new(
            "clg-charity",
            "CLG charity accountant acceptance",
            true,
            "qualified-accountant-review-required",
            [
                "CLG accounts PDF wording",
                "charity number, SoFA and trustees annual report evidence",
                "iXBRL XML and CLG source-backed readiness",
                "tax computation and charity notes",
                "charity annual return review gates"
            ],
            [
                "Named qualified-accountant approval recorded against the CLG charity pack.",
                "Charity annual report evidence reviewed before charity filing state advances.",
                "Charities Regulator source-backed evidence reviewed."
            ],
            "Named qualified accountant must approve the CLG charity pack and charity evidence before real filing use.",
            [
                IrishStatutoryRuleSources.CroGuaranteeCompany,
                IrishStatutoryRuleSources.CharitiesRegulatorAnnualReport,
                IrishStatutoryRuleSources.FrcFrs102
            ],
            AcceptanceVerifiers(goldenCorpus, "clg-charity")),
        new(
            "medium-audit-required",
            "Medium handoff accountant acceptance",
            true,
            "manual-handoff-review-required",
            [
                "auditor handoff and signed auditor report evidence",
                "full accounts PDF with P&L, cash flow and equity statements",
                "iXBRL XML tagged profit-and-loss facts",
                "filing readiness blockers before and after auditor evidence",
                "manual professional handoff note"
            ],
            [
                "Signed auditor report and manual handoff note reviewed by the qualified accountant.",
                "Named qualified-accountant acceptance recorded only after auditor evidence is present.",
                "Unsupported automated filing path remains blocked until manual professional ownership is recorded."
            ],
            "Qualified accountant must record manual handoff acceptance before relying on outputs.",
            [
                IrishStatutoryRuleSources.CroMediumCompany,
                IrishStatutoryRuleSources.CroAuditorsReport,
                IrishStatutoryRuleSources.FrcFrs102
            ],
            AcceptanceVerifiers(goldenCorpus, "medium-audit-required"))
    ];

    private static AccountantAcceptanceSummary BuildAccountantAcceptanceSummary(
        IReadOnlyList<GoldenFilingCorpusScenario> goldenCorpus,
        IReadOnlyList<AccountantAcceptanceCriterion> accountantAcceptanceCriteria)
    {
        var releaseBlockingScenarioCodes = accountantAcceptanceCriteria
            .Where(criterion => criterion.Required && criterion.AcceptanceStatus != "accepted")
            .Select(criterion => criterion.ScenarioCode)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var requiredSignOffGates = accountantAcceptanceCriteria
            .Where(criterion => criterion.Required)
            .Select(criterion => criterion.RequiredSignOffGate)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var automatedVerifierCount = accountantAcceptanceCriteria
            .SelectMany(criterion => criterion.EvidenceVerifiers)
            .Select(verifier => verifier.Name)
            .Distinct(StringComparer.Ordinal)
            .Count();
        var manualHandoffScenarioCount = goldenCorpus.Count(scenario =>
            scenario.ExpectedOutcome.Contains("manual-handoff", StringComparison.OrdinalIgnoreCase)
            || scenario.Fixture.ManualProfessionalReviewRequired);

        return new AccountantAcceptanceSummary(
            goldenCorpus.Count,
            automatedVerifierCount,
            accountantAcceptanceCriteria.Count(criterion => criterion.Required),
            manualHandoffScenarioCount,
            releaseBlockingScenarioCodes,
            requiredSignOffGates,
            releaseBlockingScenarioCodes.Length == 0 ? "accepted" : "qualified-accountant-review-required");
    }

    private static AccountantWorkflowWalkthroughProtocol BuildAccountantWorkflowWalkthroughProtocol(
        IReadOnlyList<GoldenFilingCorpusScenario> goldenCorpus)
    {
        return new AccountantWorkflowWalkthroughProtocol(
            "accountant-workflow-walkthrough-v1",
            "Qualified accountant",
            "required-review",
            "golden-corpus-accountant-acceptance",
            "Block release if a named qualified accountant has not walked the seeded golden corpus through the live accountant workflow and accepted the outputs, gates, wording and evidence.",
            goldenCorpus.Select(scenario => scenario.Code).Order(StringComparer.Ordinal).ToArray(),
            [
                "Dashboard: identify the client, deadline pressure, blockers, reviewer owner and next action.",
                "Company detail: confirm statutory profile, company type, officers, charity flags and period setup.",
                "Period workspace: review import, classification, year-end evidence, statements, notes and workflow rail state.",
                "Financial statements: inspect statement preview, tax computation, source trail and directors' report evidence.",
                "Filing review: inspect readiness profile, legal source links, generated outputs, signatory gates and accountant sign-off packet.",
                "Production readiness: confirm golden corpus, statutory rules coverage, visual QA, release blockers and operational controls."
            ],
            [
                "Micro LTD walkthrough confirms PDF wording, iXBRL review XML, tax computation, notes, machine readiness and the still-open signatory/accountant/external-validation gates.",
                "Small abridged LTD walkthrough confirms full accounts, abridged CRO pack, Section 352 evidence, iXBRL and audit-exemption gates.",
                "CLG charity walkthrough confirms the charity number and reviews the still-pending SoFA, trustees annual report, accountant and Charities Regulator evidence requirements.",
                "Medium/audit-required walkthrough confirms auditor handoff blocks normal approval until signed auditor report evidence and manual acceptance are recorded.",
                "A named qualified accountant states that the generated outputs, gates, wording and evidence are professionally acceptable for the supported scope."
            ],
            [
                "seeded golden corpus walkthrough note",
                "named qualified-accountant approval",
                "visual QA screenshot review",
                "generated PDF and iXBRL evidence",
                "manual handoff acceptance"
            ]);
    }

    private static IReadOnlyList<AccountantJourneyAcceptanceChecklistItem> BuildAccountantJourneyAcceptanceChecklist(
        IReadOnlyList<GoldenFilingCorpusScenario> goldenCorpus,
        VisualQaCoverage visualQaCoverage)
    {
        var scenarioCodes = goldenCorpus.Select(scenario => scenario.Code).Order(StringComparer.Ordinal).ToArray();
        var routeCodes = new HashSet<string>(
            ["dashboard", "company-detail", "period-workspace", "financial-statements", "filing-review", "production-readiness"],
            StringComparer.Ordinal);
        var visualArtifactNamesByRoute = visualQaCoverage.Artifacts
            .GroupBy(artifact => artifact.RouteCode, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(artifact => artifact.FileName).Order(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

        return visualQaCoverage.Routes
            .Where(route => routeCodes.Contains(route.Code))
            .OrderBy(route => JourneyRouteOrder(route.Code))
            .Select(route => new AccountantJourneyAcceptanceChecklistItem(
                route.Code,
                route.Label,
                route.RouteKey,
                route.WorkflowStages,
                scenarioCodes,
                visualArtifactNamesByRoute.TryGetValue(route.Code, out var artifactNames) ? artifactNames : [],
                [
                    "named qualified-accountant route acceptance",
                    "visual smoke screenshots reviewed",
                    "golden corpus evidence accepted"
                ],
                BuildJourneyAcceptanceCriteria(route),
                "golden-corpus-accountant-acceptance",
                "required-review"))
            .ToArray();
    }

    private static IReadOnlyList<string> BuildJourneyAcceptanceCriteria(VisualQaRoute route)
    {
        var criteria = new List<string>
        {
            $"{route.Label} route exposes the relevant accountant workflow state, blockers, next actions and evidence.",
            $"A named qualified accountant accepts the {route.Label} route outputs, gates, wording and evidence for every seeded golden scenario."
        };

        if (route.Code == "filing-review")
        {
            criteria[0] = "Filing review route exposes readiness, source links, generated outputs, signatory gates, accountant sign-off packet, external ROS/iXBRL validation and filing state.";
        }
        else if (route.Code == "financial-statements")
        {
            criteria[0] = "Financial statements route exposes statement preview, tax computation, source trail and directors' report evidence before filing review.";
        }
        else if (route.Code == "production-readiness")
        {
            criteria[0] = "Production readiness route exposes backend checks, filing rules coverage, unsupported paths, security posture, release blockers and accountant review state.";
        }

        return criteria;
    }

    private static IReadOnlyList<AccountantWorkflowEvidencePackItem> BuildAccountantWorkflowEvidencePack(
        IReadOnlyList<AccountantJourneyAcceptanceChecklistItem> checklist) =>
        checklist
            .Select(item => new AccountantWorkflowEvidencePackItem(
                item.RouteCode,
                item.RouteLabel,
                item.WorkflowStages,
                item.SeededScenarioCodes,
                item.VisualArtifactNames,
                $"{item.RouteCode}-accountant-route-acceptance-note",
                BuildAccountantWorkflowDecisionQuestion(item),
                item.RequiredEvidence,
                item.SignOffGate,
                "Block release until a named qualified accountant accepts this route's outputs, gates, wording and evidence against the seeded golden corpus and reviewed visual artifacts."))
            .ToArray();

    private static string BuildAccountantWorkflowDecisionQuestion(AccountantJourneyAcceptanceChecklistItem item)
    {
        if (item.RouteCode == "filing-review")
        {
            return "Does the filing review route let a qualified accountant accept readiness, source links, generated outputs, signatory gates, external ROS/iXBRL validation, filing state, outputs, gates, wording and evidence?";
        }

        if (item.RouteCode == "production-readiness")
        {
            return "Does the production readiness route let a qualified accountant accept backend checks, filing rules coverage, unsupported paths, security posture, release blockers, accountant review state, outputs, gates, wording and evidence?";
        }

        return $"Does the {item.RouteLabel} route let a qualified accountant accept the workflow state, blockers, next action, outputs, gates, wording and evidence for every seeded golden scenario?";
    }

    private static IReadOnlyList<AccountantWalkthroughEvidenceMatrixItem> BuildAccountantWalkthroughEvidenceMatrix(
        IReadOnlyList<GoldenFilingCorpusScenario> goldenCorpus,
        IReadOnlyList<AccountantAcceptanceCriterion> accountantAcceptanceCriteria,
        IReadOnlyList<AccountantJourneyAcceptanceChecklistItem> checklist,
        IReadOnlyList<AccountantWorkflowEvidencePackItem> evidencePack)
    {
        var acceptanceByScenario = accountantAcceptanceCriteria.ToDictionary(
            criterion => criterion.ScenarioCode,
            StringComparer.Ordinal);
        var evidenceByRoute = evidencePack.ToDictionary(
            item => item.RouteCode,
            StringComparer.Ordinal);
        var rows = new List<AccountantWalkthroughEvidenceMatrixItem>();

        foreach (var scenario in goldenCorpus.OrderBy(scenario => scenario.Code, StringComparer.Ordinal))
        {
            var acceptance = acceptanceByScenario[scenario.Code];

            foreach (var route in checklist)
            {
                var routeEvidence = evidenceByRoute[route.RouteCode];
                rows.Add(new AccountantWalkthroughEvidenceMatrixItem(
                    scenario.Code,
                    scenario.Label,
                    scenario.ExpectedOutcome,
                    scenario.EvidencePack.ExpectedOutputs.FilingReadinessState,
                    scenario.EvidencePack.ExpectedOutputs.SignOffPacketState,
                    scenario.Fixture.ManualProfessionalReviewRequired,
                    route.RouteCode,
                    route.RouteLabel,
                    route.RouteKey,
                    route.WorkflowStages,
                    route.VisualArtifactNames,
                    $"{scenario.Code}-{route.RouteCode}-walkthrough-note",
                    routeEvidence.DecisionQuestion,
                    route.RequiredEvidence
                        .Concat(acceptance.RequiredEvidence)
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .ToArray(),
                    route.AcceptanceCriteria
                        .Concat(acceptance.ReviewScope.Select(scope =>
                            $"{scenario.Label}: qualified-accountant review covers {scope}."))
                        .ToArray(),
                    "golden-corpus-accountant-acceptance",
                    route.SignOffGate,
                    "required-review",
                    BlocksRelease: true));
            }
        }

        return rows.ToArray();
    }

    private static IReadOnlyList<WorkbenchVisualAcceptanceRegisterItem> BuildWorkbenchVisualAcceptanceRegister(
        VisualQaCoverage visualQaCoverage)
    {
        var artifactNamesByRoute = visualQaCoverage.Artifacts
            .GroupBy(artifact => artifact.RouteCode, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .Select(artifact => artifact.FileName)
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        return visualQaCoverage.RouteAudits
            .Select(audit => new WorkbenchVisualAcceptanceRegisterItem(
                audit.RouteCode,
                audit.Label,
                audit.WorkflowStages,
                audit.ReviewChecks,
                artifactNamesByRoute.TryGetValue(audit.RouteCode, out var artifactNames) ? artifactNames : [],
                $"{audit.RouteCode}-visual-acceptance-note",
                [
                    "route-state acceptance note",
                    "light/dark mobile/tablet/desktop screenshot review",
                    "named visual QA reviewer sign-off"
                ],
                visualQaCoverage.ReviewProtocol.SignOffGate,
                audit.ReviewStatus,
                "Block release until this accountant workbench route is visually accepted across workflow hierarchy, table scanability, theme contrast, responsive density and route states.",
                BuildWorkbenchVisualAcceptanceNextAction(audit)))
            .ToArray();
    }

    private static string BuildWorkbenchVisualAcceptanceNextAction(VisualQaRouteAudit audit)
    {
        if (audit.RouteCode == "filing-review")
        {
            return "Accept the filing review screen only after its evidence checklist, source links, generated outputs and filing-state actions are visually clear in light/dark mobile/tablet/desktop screenshots.";
        }

        if (audit.RouteCode == "production-readiness")
        {
            return "Accept the production readiness screen only after release blockers, rule coverage, visual QA, operational readiness and accountant review state are visually clear in light/dark mobile/tablet/desktop screenshots.";
        }

        return $"Accept the {audit.Label} route only after its workflow hierarchy, tables, contrast, mobile layout, loading/error/empty states and screenshots are professionally reviewed.";
    }

    private static int JourneyRouteOrder(string routeCode) => routeCode switch
    {
        "dashboard" => 0,
        "company-detail" => 1,
        "period-workspace" => 2,
        "financial-statements" => 3,
        "filing-review" => 4,
        "production-readiness" => 5,
        _ => 99
    };

    private static IReadOnlyList<GoldenFilingCorpusVerifier> AcceptanceVerifiers(
        IReadOnlyList<GoldenFilingCorpusScenario> goldenCorpus,
        string scenarioCode)
    {
        var scenario = goldenCorpus.Single(item => item.Code == scenarioCode);
        return scenario.EvidenceVerifiers;
    }

    private static VisualQaCoverage BuildVisualQaCoverage()
    {
        var accountantWorkflowStages = new[]
        {
            "Setup",
            "Import",
            "Classify",
            "Year-End",
            "Statements",
            "Notes",
            "Review",
            "Filing"
        };
        var themes = new[] { "light", "dark" };
        var layoutChecks = new[]
        {
            "browser-console-errors",
            "page-horizontal-overflow",
            "visible-text-overlap"
        };
        var reviewChecks = new[]
        {
            "accountant-workflow-hierarchy",
            "table-scanability",
            "theme-contrast",
            "axe-wcag-2.2-a-aa",
            "responsive-density",
            "loading-error-empty-states",
            "canonical-url-tab-state",
            "semantic-capture-distinctness",
            "stale-conflict-states"
        };
        var reviewProtocol = new VisualQaReviewProtocol(
            "visual-review-v2-canonical-states",
            "Design reviewer",
            "required-review",
            "visual-qa-screenshot-review",
            "Block release if a canonical material route/state combination is missing, duplicated, semantically identical to another intended state, or has console, overflow, overlap, WCAG accessibility, contrast, responsive-density or human-review defects.",
            [
                "Every canonical material route/state is captured in light and dark themes at 390x844 mobile, 768x1024 tablet and 1440x1000 desktop viewports.",
                "Every capture retains its expected text, exact canonical URL/tab state, console/overflow/overlap/axe/contrast results, responsive workflow acceptance, dimensions, SHA-256 hash and pending human-review status.",
                "No canonical route/theme/viewport combination is missing or duplicated, and no two intended states in the same theme/viewport are semantically identical.",
                "A named visual QA reviewer records screenshot-manifest acceptance before real filing release."
            ],
            [
                "visual-smoke-manifest.json",
                "visual-smoke-evidence-report.json",
                "accountant-workbench-evidence-report.json",
                "192 canonical material-state screenshots",
                "canonical state inventory and exact URL/tab evidence",
                "semantic content SHA-256 distinctness evidence",
                "screenshot SHA-256 checksums",
                "screenshot PNG dimensions",
                "screenshot nonblank pixel diversity evidence",
                "per-screenshot automated theme contrast smoke evidence",
                "per-screenshot axe-core WCAG 2.2 A/AA evidence",
                "per-screenshot responsive workflow acceptance evidence",
                "state audit summary",
                "named visual QA reviewer sign-off"
            ]);
        var viewports = new[]
        {
            new VisualQaViewport("mobile", 390, 844),
            new VisualQaViewport("tablet", 768, 1024),
            new VisualQaViewport("desktop", 1440, 1000)
        };
        var routes = new[]
        {
            new VisualQaRoute(
                "dashboard",
                "dashboard",
                "Dashboard",
                "Accountant queue, blockers, deadlines and production readiness overview.",
                "Firm command centre",
                accountantWorkflowStages,
                OpenFilingTab: false),
            new VisualQaRoute(
                "production-readiness",
                "readiness",
                "Production readiness",
                "Assurance checklist, statutory rules matrix, source snapshot and operational gates.",
                "Production Readiness Checklist",
                ["Review", "Filing"],
                OpenFilingTab: false),
            new VisualQaRoute(
                "company-detail",
                "company",
                "Company detail",
                "Company command centre, statutory profile, officers, charity facts and accounting periods.",
                "Company command centre",
                ["Setup"],
                OpenFilingTab: false),
            new VisualQaRoute(
                "period-workspace",
                "period",
                "Period workspace",
                "Canonical import-tab period workspace with classification, year-end, statements and filing readiness context.",
                "Filing readiness",
                accountantWorkflowStages,
                OpenFilingTab: false),
            new VisualQaRoute(
                "filing-review",
                "filing",
                "Filing review",
                "Canonical filing-tab state with evidence checklist, source links, outputs and recorded filing workflow.",
                "Filing readiness profile",
                ["Review", "Filing"],
                OpenFilingTab: false),
            new VisualQaRoute(
                "financial-statements",
                "financialStatements",
                "Financial statements",
                "Canonical trial-balance tab in the statement preview and source-trail workbench.",
                "Financial Statements",
                ["Statements"],
                OpenFilingTab: false),
            new VisualQaRoute(
                "workbench-preview",
                "workbenchPreview",
                "Workbench preview",
                "Internal deterministic preview for accountant workflow primitives and dense populated content.",
                "Workbench Component Preview",
                accountantWorkflowStages,
                OpenFilingTab: false)
        };

        var requiredMaterialRoutes = new[]
        {
            "login",
            "password-change",
            "onboarding",
            "classification",
            "categorisation",
            "year-end",
            "adjustments",
            "notes",
            "charity",
            "statement-trial-balance",
            "statement-source-trail",
            "statement-profit-and-loss",
            "statement-balance-sheet",
            "statement-tax-computation",
            "statement-cash-flow",
            "statement-equity-changes",
            "statement-directors-report",
            "filing"
        };
        var requiredUiStates = new[]
        {
            "loading",
            "empty",
            "maximum-data",
            "error",
            "partial-error",
            "permission-denied",
            "read-only",
            "stale",
            "conflict"
        };

        static IReadOnlyDictionary<string, string> Query(params (string Key, string Value)[] entries)
        {
            return entries.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        }

        static VisualQaTabState Tab(string kind, string id, string label) => new(kind, id, label);

        static string CanonicalUrl(string path, IReadOnlyDictionary<string, string> query)
        {
            return query.Count == 0
                ? path
                : $"{path}?{string.Join("&", query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"))}";
        }

        static VisualQaStateInventoryItem State(
            string id,
            string label,
            string description,
            string routeKey,
            string canonicalPath,
            string expectedText,
            IReadOnlyList<string> workflowStages,
            string? materialRoute = null,
            string uiState = "populated",
            string authMode = "authenticated",
            IReadOnlyDictionary<string, string>? canonicalQuery = null,
            VisualQaTabState? canonicalTabState = null,
            string? expectedStateText = null)
        {
            canonicalQuery ??= Query();
            canonicalTabState ??= Tab("route", "default", "No tab selection");
            return new VisualQaStateInventoryItem(
                id,
                id,
                routeKey,
                label,
                description,
                materialRoute,
                uiState,
                canonicalPath,
                CanonicalUrl(canonicalPath, canonicalQuery),
                canonicalQuery,
                canonicalTabState,
                expectedText,
                expectedStateText ?? expectedText,
                workflowStages,
                authMode,
                "required-review",
                OpenFilingTab: false);
        }

        VisualQaRoute AccountantRoute(string code) => routes.Single(route => route.Code == code);

        VisualQaStateInventoryItem AccountantState(
            string routeCode,
            string canonicalPath,
            IReadOnlyDictionary<string, string>? canonicalQuery = null,
            VisualQaTabState? canonicalTabState = null,
            string? expectedStateText = null,
            string? materialRoute = null)
        {
            var route = AccountantRoute(routeCode);
            return State(
                route.Code,
                route.Label,
                route.Description,
                route.RouteKey,
                canonicalPath,
                route.RequiredText,
                route.WorkflowStages,
                materialRoute,
                canonicalQuery: canonicalQuery,
                canonicalTabState: canonicalTabState,
                expectedStateText: expectedStateText);
        }

        var stateInventory = new List<VisualQaStateInventoryItem>
        {
            State("login", "Login", "Unauthenticated firm-user sign-in form.", "login", "/login", "Sign in", ["Setup"], "login", authMode: "anonymous"),
            State("password-change", "Password change", "Authenticated password rotation form.", "changePassword", "/change-password", "Set a new password", ["Setup"], "password-change"),
            AccountantState("dashboard", "/", canonicalTabState: Tab("route", "dashboard", "Dashboard root")),
            State("onboarding", "Company onboarding", "Blank company onboarding workflow at its legal-identity step.", "onboarding", "/companies/new", "New Company", ["Setup"], "onboarding", uiState: "blank-form"),
            AccountantState("production-readiness", "/production-readiness", canonicalTabState: Tab("route", "readiness", "Production readiness"), expectedStateText: "Release remains gated"),
            AccountantState("company-detail", "/companies/{companyId}", canonicalTabState: Tab("route", "company", "Company command centre")),
            AccountantState("period-workspace", "/companies/{companyId}/periods/{periodId}", canonicalTabState: Tab("period-tab", "import", "Import"), expectedStateText: "Import Transactions"),
            State("classification", "Company size classification", "Period classification interview and statutory decision evidence.", "classification", "/companies/{companyId}/periods/{periodId}/classify", "Company Size Classification", ["Classify"], "classification"),
            State("categorisation", "Transaction categorisation", "Categorisation filters, rules, progress and transaction evidence table.", "period", "/companies/{companyId}/periods/{periodId}", "Categorisation Overview", ["Import"], "categorisation", canonicalQuery: Query(("tab", "categorise")), canonicalTabState: Tab("period-tab", "categorise", "Categorise")),
            State("year-end", "Year-end questionnaire", "Year-end accounting inputs, statutory representations and completeness review.", "yearEnd", "/companies/{companyId}/periods/{periodId}/year-end", "Year-End Questionnaire", ["Year-End"], "year-end"),
            State("adjustments", "Period adjustments", "Adjustment generation, filters, summaries and approval evidence.", "period", "/companies/{companyId}/periods/{periodId}", "Period Adjustments", ["Year-End", "Review"], "adjustments", canonicalQuery: Query(("tab", "adjustments")), canonicalTabState: Tab("period-tab", "adjustments", "Adjustments")),
            State("notes", "Financial statement notes", "Regime-aware note checklist, generation, inclusion and editing workflow.", "notes", "/companies/{companyId}/periods/{periodId}/notes", "Notes to the Financial Statements", ["Notes"], "notes"),
            State("charity", "Charity reporting", "Charity/SORP reporting decision and statement-of-financial-activities tab.", "charity", "/companies/{companyId}/periods/{periodId}/charity", "Charity Reporting", ["Statements", "Notes", "Review"], "charity", canonicalTabState: Tab("charity-tab", "sofa", "Statement of Financial Activities"), expectedStateText: "Statement of Financial Activities"),
            AccountantState("financial-statements", "/companies/{companyId}/periods/{periodId}/statements", canonicalTabState: Tab("statement-tab", "trial-balance", "Trial Balance"), expectedStateText: "Trial Balance", materialRoute: "statement-trial-balance")
        };

        foreach (var statement in new[]
        {
            (Id: "statement-source-trail", TabId: "sources", TabLabel: "Source Trail", ExpectedStateText: "Figure Source Trail"),
            (Id: "statement-profit-and-loss", TabId: "pnl", TabLabel: "Profit & Loss", ExpectedStateText: "Profit & Loss Account"),
            (Id: "statement-balance-sheet", TabId: "balance-sheet", TabLabel: "Balance Sheet", ExpectedStateText: "Balance Sheet"),
            (Id: "statement-tax-computation", TabId: "tax-computation", TabLabel: "Tax Computation", ExpectedStateText: "Corporation Tax Support Data"),
            (Id: "statement-cash-flow", TabId: "cash-flow", TabLabel: "Cash Flow", ExpectedStateText: "Cash Flow Statement"),
            (Id: "statement-equity-changes", TabId: "equity-changes", TabLabel: "Equity Changes", ExpectedStateText: "Statement of Changes in Equity"),
            (Id: "statement-directors-report", TabId: "directors-report", TabLabel: "Directors' Report", ExpectedStateText: "Directors' Report")
        })
        {
            stateInventory.Add(State(
                statement.Id,
                statement.TabLabel,
                $"Financial statements {statement.TabLabel} tab.",
                "financialStatements",
                "/companies/{companyId}/periods/{periodId}/statements",
                "Financial Statements",
                ["Statements"],
                statement.Id,
                canonicalQuery: Query(("statementTab", statement.TabId)),
                canonicalTabState: Tab("statement-tab", statement.TabId, statement.TabLabel),
                expectedStateText: statement.ExpectedStateText));
        }

        stateInventory.Add(AccountantState(
            "filing-review",
            "/companies/{companyId}/periods/{periodId}",
            Query(("tab", "filing")),
            Tab("period-tab", "filing", "Filing"),
            materialRoute: "filing"));
        stateInventory.Add(AccountantState(
            "workbench-preview",
            "/workbench-preview",
            canonicalTabState: Tab("route", "preview", "Workbench component preview")));

        foreach (var preview in new[]
        {
            (UiState: "loading", Label: "Canonical loading state", ExpectedStateText: "Loading canonical accountant workspace"),
            (UiState: "empty", Label: "Canonical empty state", ExpectedStateText: "No canonical accounting records"),
            (UiState: "maximum-data", Label: "Canonical maximum-data state", ExpectedStateText: "Maximum-data review table"),
            (UiState: "error", Label: "Canonical error state", ExpectedStateText: "Canonical workspace could not be loaded"),
            (UiState: "partial-error", Label: "Canonical partial-error state", ExpectedStateText: "Filing evidence unavailable"),
            (UiState: "permission-denied", Label: "Canonical permission-denied state", ExpectedStateText: "Permission denied"),
            (UiState: "read-only", Label: "Canonical read-only state", ExpectedStateText: "Read-only workflow access"),
            (UiState: "stale", Label: "Canonical stale state", ExpectedStateText: "Refreshing statement evidence; retained data may be stale."),
            (UiState: "conflict", Label: "Canonical conflict state", ExpectedStateText: "Accounting record changed by another reviewer")
        })
        {
            stateInventory.Add(State(
                $"state-{preview.UiState}",
                preview.Label,
                $"Deterministic {preview.UiState} workbench state for visual release review.",
                "workbenchPreview",
                "/workbench-preview",
                preview.Label,
                ["Review"],
                uiState: preview.UiState,
                canonicalQuery: Query(("state", preview.UiState)),
                canonicalTabState: Tab("preview-state", preview.UiState, preview.Label),
                expectedStateText: preview.ExpectedStateText));
        }

        var artifacts = stateInventory
            .SelectMany(state => themes.SelectMany(theme => viewports.Select(viewport =>
            {
                var fileName = $"{state.StateId}-{theme}-{viewport.Name}.png";
                return new VisualQaArtifact(
                    state.StateId,
                    state.RouteName,
                    state.StateId,
                    state.RouteKey,
                    state.MaterialRoute,
                    state.UiState,
                    state.AuthMode,
                    theme,
                    viewport.Name,
                    fileName,
                    $"artifacts/visual-smoke/{fileName}",
                    state.ExpectedText,
                    state.ExpectedStateText,
                    state.CanonicalUrlTemplate,
                    state.CanonicalQuery,
                    state.CanonicalTabState,
                    state.OpenFilingTab,
                    "required-review",
                    layoutChecks);
            })))
            .ToArray();
        var routeAudits = routes
            .Select(route => new VisualQaRouteAudit(
                route.Code,
                route.RouteKey,
                route.Label,
                route.WorkflowStages,
                themes.Length * viewports.Length,
                "required-review",
                reviewChecks))
            .ToArray();

        return new VisualQaCoverage(
            "visual-smoke-screenshots",
            "ci-production-smoke",
            "visual-smoke-manifest.json",
            "canonical-material-states-v1",
            stateInventory.Count,
            stateInventory.Count,
            routes.Length,
            stateInventory.Count * themes.Length * viewports.Length,
            requiredMaterialRoutes,
            requiredUiStates,
            SemanticDistinctnessRequired: true,
            layoutChecks,
            reviewChecks,
            reviewProtocol,
            themes,
            viewports,
            routes,
            stateInventory,
            routeAudits,
            artifacts);
    }
}
