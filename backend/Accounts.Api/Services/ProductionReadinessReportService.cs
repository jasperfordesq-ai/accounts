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

public sealed record OperationalGate(
    string Code,
    string Label,
    bool Required,
    string Status,
    string Detail);

public sealed record ProductionReadinessReport(
    DateTime GeneratedAt,
    string OverallStatus,
    int CompaniesInDatabase,
    int PeriodsInDatabase,
    SourceLawSnapshot SourceLawSnapshot,
    IReadOnlyList<ProductionReadinessArea> Areas,
    IReadOnlyList<GoldenFilingCorpusScenario> GoldenFilingCorpus,
    IReadOnlyList<string> ManualHandoffPaths,
    IReadOnlyList<OperationalGate> OperationalGates);

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
            BuildManualHandoffPaths(),
            BuildOperationalGates());
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
}
