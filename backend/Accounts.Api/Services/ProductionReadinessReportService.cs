using Accounts.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Services;

public sealed record ProductionReadinessArea(
    string Code,
    string Label,
    string Status,
    string Detail);

public sealed record GoldenFilingCorpusScenario(
    string Code,
    string Label,
    string CompanyScope,
    string ExpectedOutcome,
    string CoverageStatus,
    IReadOnlyList<string> EvidenceTestNames,
    IReadOnlyList<string> Assertions);

public sealed record StatutoryRuleMatrixEntry(
    string Code,
    string CompanyScope,
    string SizeOrRegime,
    string SupportLevel,
    IReadOnlyList<string> RequiredEvidence,
    IReadOnlyList<string> RequiredOutputs,
    IReadOnlyList<string> ManualHandoffGates,
    IReadOnlyList<LegalSourceReference> Sources);

public sealed record OperationalGate(
    string Code,
    string Label,
    bool Required,
    string Status,
    string Detail);

public sealed record ProductionReadinessAssuranceAction(
    string Code,
    string Label,
    string Owner,
    string Priority,
    string Status,
    string Detail,
    string EvidenceRequired);

public sealed record ProductionReadinessReport(
    DateTime GeneratedAt,
    string OverallStatus,
    int CompaniesInDatabase,
    int PeriodsInDatabase,
    SourceLawSnapshot SourceLawSnapshot,
    IReadOnlyList<ProductionReadinessArea> Areas,
    IReadOnlyList<GoldenFilingCorpusScenario> GoldenFilingCorpus,
    IReadOnlyList<StatutoryRuleMatrixEntry> StatutoryRuleMatrix,
    IReadOnlyList<string> ManualHandoffPaths,
    IReadOnlyList<OperationalGate> OperationalGates,
    IReadOnlyList<ProductionReadinessAssuranceAction> AssuranceActions);

public class ProductionReadinessReportService(AccountsDbContext db)
{
    public async Task<ProductionReadinessReport> GetReportAsync(CancellationToken cancellationToken = default)
    {
        var companies = await db.Companies.CountAsync(cancellationToken);
        var periods = await db.AccountingPeriods.CountAsync(cancellationToken);

        return new ProductionReadinessReport(
            DateTime.UtcNow,
            "review-required",
            companies,
            periods,
            IrishStatutoryRuleSources.BuildSnapshot(),
            BuildAreas(),
            BuildGoldenCorpus(),
            BuildStatutoryRuleMatrix(),
            BuildManualHandoffPaths(),
            BuildOperationalGates(),
            BuildAssuranceActions());
    }

    private static IReadOnlyList<ProductionReadinessArea> BuildAreas() =>
    [
        new(
            "backend-accounting-engine",
            "Backend accounting engine",
            "hardened",
            "Golden-path coverage exercises classification, regime selection, statements, PDF, iXBRL, readiness and workflow gates. Final statutory use still requires named professional review."),
        new(
            "statutory-source-traceability",
            "Statutory source traceability",
            "hardened",
            "Rule outputs carry LegalSourceReference metadata and the source-law snapshot pins effective dates and URLs."),
        new(
            "frontend-accountant-workbench",
            "Frontend accountant workbench",
            "in-progress",
            "Workbench primitives and filing review surfaces exist; the remaining polish target is a consistent dashboard, route-level states and visual regression coverage."),
        new(
            "operations-security",
            "Operations and security",
            "hardened",
            "CI validates backend, frontend, production compose, production smoke and backup restore. Runtime monitoring and accountant sign-off remain operational gates.")
    ];

    private static IReadOnlyList<GoldenFilingCorpusScenario> BuildGoldenCorpus() =>
    [
        new(
            "micro-ltd",
            "Micro LTD audit-exempt filing",
            "Private company limited by shares, micro regime",
            "generated-pack",
            "covered",
            [
                "AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl"
            ],
            [
                "classification selects Micro",
                "readiness has no missing items",
                "balance sheet balances",
                "PDF text includes company, period and micro statement",
                "iXBRL parses as XML"
            ]),
        new(
            "small-abridged-ltd",
            "Small or small-abridged LTD filing",
            "Private company limited by shares, small/abridged regime",
            "generated-pack",
            "covered",
            [
                "AccountsWorkflowTests.GoldenPath_SmallAuditExemptCompany_MixedAccrualSetBalancesThroughPdfAndIxbrl"
            ],
            [
                "classification selects Small",
                "mixed cash/accrual facts reconcile",
                "PDF text includes legal name and net assets",
                "iXBRL parses as XML"
            ]),
        new(
            "clg-charity",
            "CLG charity annual reporting",
            "Company limited by guarantee with charity evidence",
            "generated-pack-with-charity-gates",
            "covered",
            [
                "FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness"
            ],
            [
                "CLG remains in the supported company scope",
                "charity number evidence is required",
                "SoFA and trustees report evidence are required",
                "Charities Regulator source is attached"
            ]),
        new(
            "medium-audit-required",
            "Medium audit-required handoff",
            "Medium company or non-audit-exempt filing",
            "manual-handoff",
            "covered",
            [
                "FilingGoldenCorpusScenarioTests.GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence"
            ],
            [
                "audit report evidence is mandatory",
                "normal filing approval is blocked until auditor evidence exists",
                "manual professional handoff is exposed in readiness"
            ])
    ];

    private static IReadOnlyList<StatutoryRuleMatrixEntry> BuildStatutoryRuleMatrix() =>
    [
        new(
            "ltd-micro",
            "LTD micro",
            "Micro / FRS 105",
            "supported",
            [
                "Micro size classification completed and no micro exclusions apply",
                "Active director and company secretary recorded",
                "Named qualified-accountant review recorded",
                "External ROS/iXBRL validation evidence recorded before Revenue use"
            ],
            [
                "Micro entity financial statements PDF",
                "CRO certification/signature page",
                "Revenue iXBRL financial statements package",
                "CT1 support and tax computation evidence"
            ],
            [
                "Fail closed if micro exclusions, group context, regulated status or missing signatories are detected",
                "Manual professional review required before real filing use"
            ],
            [
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.FrcFrs105,
                IrishStatutoryRuleSources.RevenueIxbrlOverview,
                IrishStatutoryRuleSources.RevenueAcceptedTaxonomies
            ]),
        new(
            "ltd-small-abridged",
            "LTD small abridged",
            "Small / abridged FRS 102",
            "supported",
            [
                "Small size classification completed under two-of-three rules",
                "Abridgement eligibility and audit exemption confirmed",
                "Director and secretary evidence recorded",
                "Named qualified-accountant review recorded"
            ],
            [
                "Small or abridged financial statements PDF",
                "Required statutory statements and notes",
                "CRO certification/signature page",
                "Revenue iXBRL and CT1 support pack"
            ],
            [
                "Fail closed if audit exemption is lost, member audit notice applies, or abridgement is not available",
                "External ROS/iXBRL validation remains a manual evidence gate"
            ],
            [
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.FrcFrs102,
                IrishStatutoryRuleSources.RevenueIxbrlContents,
                IrishStatutoryRuleSources.RevenueAcceptedTaxonomies
            ]),
        new(
            "dac-small",
            "DAC small",
            "Small / FRS 102",
            "supported",
            [
                "DAC company type and size classification recorded",
                "Audit exemption and filing regime confirmed",
                "Director and secretary evidence recorded",
                "Named qualified-accountant review recorded"
            ],
            [
                "Small company financial statements PDF",
                "Directors' report evidence",
                "CRO certification/signature page",
                "Revenue iXBRL and CT1 support pack"
            ],
            [
                "Manual handoff if audit is required, regulated exclusions apply, or group context is present",
                "No direct CRO/ROS submission automation"
            ],
            [
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.FrcFrs102,
                IrishStatutoryRuleSources.RevenueIxbrlOverview
            ]),
        new(
            "clg-charity",
            "CLG charity",
            "CLG / charity annual return",
            "supported-with-review",
            [
                "CLG company type recorded",
                "Charity number and charity profile completed",
                "SoFA and trustees' annual report evidence generated",
                "Named qualified-accountant or charity reviewer approval recorded"
            ],
            [
                "CLG financial statements PDF",
                "Charity SoFA",
                "Trustees' annual report support",
                "Revenue iXBRL package where corporation tax filing applies"
            ],
            [
                "Charity annual return must be manually reviewed before use",
                "Manual handoff required for complex charity governance, restricted funds or regulator-specific queries"
            ],
            [
                IrishStatutoryRuleSources.CroGuaranteeCompany,
                IrishStatutoryRuleSources.CharitiesRegulatorAnnualReport,
                IrishStatutoryRuleSources.FrcFrs102
            ]),
        new(
            "medium-audit-required",
            "Medium audit-required",
            "Medium / full FRS 102",
            "manual-handoff",
            [
                "Medium size classification completed",
                "Signed auditor report evidence recorded",
                "Full financial statements and directors' report evidence reviewed",
                "Named qualified-accountant and auditor handoff recorded"
            ],
            [
                "Full accounts support pack",
                "Audit report evidence record",
                "Revenue iXBRL and CT1 support pack"
            ],
            [
                "Final outputs remain blocked until signed auditor report evidence is recorded",
                "Manual professional handoff required before approval or submission states"
            ],
            [
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.FrcFrs102,
                IrishStatutoryRuleSources.RevenueIxbrlOverview
            ]),
        new(
            "unsupported-regulated-group",
            "Unsupported regulated/group",
            "PLC, regulated, unlimited variants, group/holding/subsidiary",
            "unsupported",
            [
                "Manual professional ownership recorded",
                "Reason for unsupported path captured",
                "External specialist review evidence retained outside automated filing workflow"
            ],
            [
                "Manual handoff record",
                "Evidence checklist export only; no automated final filing pack approval"
            ],
            [
                "Fail closed before filing workflow approval",
                "Direct CRO/ROS submission remains unsupported",
                "Specialist accountant or auditor must complete the statutory filing outside the automated path"
            ],
            [
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.CroGroupCompany,
                IrishStatutoryRuleSources.CroUnlimitedCompany,
                IrishStatutoryRuleSources.FrcFrs102
            ])
    ];

    private static IReadOnlyList<string> BuildManualHandoffPaths() =>
    [
        "PLC and public-company workflows",
        "Private unlimited company variants not safely modelled",
        "Listed, credit institution, insurance undertaking, pension fund and other excluded entities",
        "Group, holding, subsidiary and consolidation contexts",
        "Audit-required filings without a signed auditor report",
        "Complex corporation tax claims, group relief and loss elections",
        "Direct CRO or ROS submission"
    ];

    private static IReadOnlyList<OperationalGate> BuildOperationalGates() =>
    [
        new(
            "qualified-accountant-review",
            "Named qualified-accountant review",
            true,
            "required",
            "Generated statutory packs cannot be treated as filing-ready until a named qualified accountant has reviewed and approved them."),
        new(
            "external-ros-validation",
            "External ROS/iXBRL validation",
            true,
            "required",
            "The platform records internal iXBRL checks only; external ROS validation remains a manual evidence gate."),
        new(
            "director-secretary-certification",
            "Director and secretary certification",
            true,
            "required",
            "CRO filing workflow requires active director and company secretary evidence."),
        new(
            "no-direct-cro-ros-submission",
            "No direct CRO/ROS submission automation",
            true,
            "enforced",
            "The workflow records generated, reviewed, approved, submitted, paid and accepted/rejected states only."),
        new(
            "production-ci-gates",
            "Production CI gates",
            true,
            "enforced",
            "CI runs backend, frontend, dependency audit, production compose config, production stack smoke and backup restore checks.")
    ];

    private static IReadOnlyList<ProductionReadinessAssuranceAction> BuildAssuranceActions() =>
    [
        new(
            "qualified-accountant-signoff",
            "Qualified accountant sign-off",
            "Qualified accountant",
            "critical",
            "required",
            "No generated filing pack can be treated as final until a named qualified accountant has reviewed the evidence, outputs and wording.",
            "Named qualified-accountant approval recorded against the period and linked to the generated pack."),
        new(
            "external-ros-validation",
            "External ROS/iXBRL validation",
            "Reviewer",
            "critical",
            "required",
            "Internal XML parsing is not a Revenue acceptance check, so real filings need a recorded external ROS validation result.",
            "External ROS validation evidence uploaded or referenced before any Revenue filing state is marked accepted."),
        new(
            "light-dark-visual-regression",
            "Light/dark visual regression",
            "Engineering",
            "high",
            "in-progress",
            "The accountant journey needs desktop and mobile screenshots across light and dark mode before it can be called visually production-ready.",
            "Screenshots for dashboard, company detail, period workspace and filing review in light desktop, dark desktop, light mobile and dark mobile."),
        new(
            "production-monitoring",
            "Production monitoring",
            "Operations",
            "high",
            "required",
            "Runtime failures must be visible to operators before real statutory filing packs are processed.",
            "Sentry production error routing configured and reviewed with structured log correlation."),
        new(
            "accountant-acceptance-walkthrough",
            "Accountant acceptance walkthrough",
            "Qualified accountant",
            "high",
            "required",
            "A qualified accountant must take the golden scenarios through the live workflow and confirm outputs, gates and wording are professionally acceptable.",
            "Signed acceptance note covering micro LTD, small abridged LTD, CLG charity and medium/audit-required manual handoff.")
    ];
}
