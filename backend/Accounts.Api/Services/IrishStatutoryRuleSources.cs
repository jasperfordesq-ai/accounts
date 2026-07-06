using Accounts.Api.Entities;
using System.Security.Cryptography;
using System.Text;

namespace Accounts.Api.Services;

public sealed record LegalSourceReference(
    string SourceId,
    string Title,
    DateOnly EffectiveDate,
    string Url);

public sealed record SourceLawSnapshot(
    DateOnly SnapshotDate,
    string SnapshotVersion,
    string ContentHash,
    int SourceCount,
    IReadOnlyList<LegalSourceReference> Sources);

public sealed record RevenueIxbrlTaxonomySelection(
    string TaxonomyKey,
    string TaxonomyDate,
    string Label,
    string SchemaRef,
    bool AcceptedByRevenue,
    DateOnly EffectiveForPeriodsStartingOnOrAfter,
    IReadOnlyList<LegalSourceReference> Sources);

public static class IrishStatutoryRuleSources
{
    public static readonly LegalSourceReference CroFinancialStatementsRequirements = new(
        "cro-financial-statements-requirements",
        "CRO financial statements requirements",
        new DateOnly(2026, 7, 3),
        "https://cro.ie/annual-return/financial-statements-requirements/");

    public static readonly LegalSourceReference CroGuaranteeCompany = new(
        "cro-guarantee-company",
        "CRO guarantee company financial statements requirements",
        new DateOnly(2026, 7, 3),
        "https://cro.ie/annual-return/financial-statements-requirements/guarantee-company/");

    public static readonly LegalSourceReference CroUnlimitedCompany = new(
        "cro-unlimited-company",
        "CRO unlimited company financial statements requirements",
        new DateOnly(2026, 7, 3),
        "https://cro.ie/annual-return/financial-statements-requirements/unlimited-company/");

    public static readonly LegalSourceReference CroGroupCompany = new(
        "cro-group-company",
        "CRO group company financial statements requirements",
        new DateOnly(2026, 7, 3),
        "https://cro.ie/annual-return/financial-statements-requirements/group-company/");

    public static readonly LegalSourceReference CroMediumCompany = new(
        "cro-medium-company",
        "CRO medium company financial statements requirements",
        new DateOnly(2026, 7, 4),
        "https://cro.ie/annual-return/financial-statements-requirements/medium-company/");

    public static readonly LegalSourceReference CroAuditorsReport = new(
        "cro-auditors-report",
        "CRO auditor's report requirements",
        new DateOnly(2026, 7, 4),
        "https://cro.ie/annual-return/financial-statements-requirements/auditors-report/");

    public static readonly LegalSourceReference RevenueIxbrlOverview = new(
        "revenue-ixbrl-overview",
        "Revenue iXBRL filing overview",
        new DateOnly(2026, 7, 3),
        "https://www.revenue.ie/en/companies-and-charities/corporation-tax-for-companies/submitting-financial-statements/index.aspx");

    public static readonly LegalSourceReference RevenueIxbrlContents = new(
        "revenue-ixbrl-contents",
        "Revenue iXBRL financial statement contents",
        new DateOnly(2026, 7, 3),
        "https://www.revenue.ie/en/companies-and-charities/corporation-tax-for-companies/submitting-financial-statements/how-to.aspx");

    public static readonly LegalSourceReference RevenueAcceptedTaxonomies = new(
        "revenue-accepted-taxonomies",
        "Revenue accepted iXBRL taxonomies",
        new DateOnly(2025, 11, 6),
        "https://www.revenue.ie/en/companies-and-charities/corporation-tax-for-companies/submitting-financial-statements/accepted-taxonomies.aspx");

    public static readonly LegalSourceReference FrcFrs102 = new(
        "frc-frs-102",
        "FRC FRS 102 current edition and amendments",
        new DateOnly(2026, 7, 3),
        "https://www.frc.org.uk/library/standards-codes-policy/accounting-and-reporting/uk-accounting-standards/frs-102/");

    public static readonly LegalSourceReference FrcFrs105 = new(
        "frc-frs-105",
        "FRC FRS 105 current edition and amendments",
        new DateOnly(2026, 7, 3),
        "https://www.frc.org.uk/library/standards-codes-policy/accounting-and-reporting/uk-accounting-standards/frs-105/");

    public static readonly LegalSourceReference CharitiesRegulatorAnnualReport = new(
        "charities-regulator-annual-report",
        "Charities Regulator annual report guidance",
        new DateOnly(2026, 7, 3),
        "https://www.charitiesregulator.ie/en/information-for-charities/annual-report-how-to-submit");

    public static SourceLawSnapshot BuildSnapshot()
    {
        var sources = All;
        return new SourceLawSnapshot(
            new DateOnly(2026, 7, 3),
            "irish-statutory-accounts-sources-2026-07-03",
            ComputeContentHash(sources),
            sources.Count,
            sources);
    }

    public static string ComputeContentHash(IEnumerable<LegalSourceReference> sources)
    {
        var canonicalSourceSet = string.Join(
            "\n",
            sources
                .OrderBy(source => source.SourceId, StringComparer.Ordinal)
                .Select(source =>
                    $"{CanonicalField(source.SourceId)}|{CanonicalField(source.Title)}|{source.EffectiveDate:yyyy-MM-dd}|{CanonicalField(source.Url)}"));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalSourceSet));
        return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static string CanonicalField(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

    public static readonly IReadOnlyList<LegalSourceReference> All =
    [
        CroFinancialStatementsRequirements,
        CroGuaranteeCompany,
        CroUnlimitedCompany,
        CroGroupCompany,
        CroMediumCompany,
        CroAuditorsReport,
        RevenueIxbrlOverview,
        RevenueIxbrlContents,
        RevenueAcceptedTaxonomies,
        FrcFrs102,
        FrcFrs105,
        CharitiesRegulatorAnnualReport
    ];
}

public static class RevenueIxbrlTaxonomySelector
{
    private sealed record RevenueTaxonomyOption(
        string TaxonomyKey,
        string TaxonomyDate,
        string Label,
        string SchemaRef,
        DateOnly EffectiveForPeriodsStartingOnOrAfter);

    private static readonly RevenueTaxonomyOption[] Frs102Options =
    [
        new(
            "irish-extension-2025-frs-102",
            "2025-01-01",
            "Irish Extension 2025 FRS 102 taxonomy accepted by Revenue",
            "https://xbrl.frc.org.uk/ireland/FRS-102/2025-01-01/ie-FRS-102-2025-01-01.xsd",
            new DateOnly(2024, 1, 1)),
        new(
            "irish-extension-2023-frs-102",
            "2023-01-01",
            "Irish Extension 2023 FRS 102 taxonomy accepted by Revenue",
            "https://xbrl.frc.org.uk/ireland/FRS-102/2023-01-01/ie-FRS-102-2023-01-01.xsd",
            new DateOnly(2023, 1, 1)),
        new(
            "irish-extension-2022-frs-102",
            "2022-01-01",
            "Irish Extension 2022 FRS 102 taxonomy accepted by Revenue",
            "https://xbrl.frc.org.uk/ireland/FRS-102/2022-01-01/ie-FRS-102-2022-01-01.xsd",
            new DateOnly(2019, 1, 1))
    ];

    public static RevenueIxbrlTaxonomySelection Select(DateOnly periodStart, ElectedRegime? regime)
    {
        var selected = Frs102Options
            .FirstOrDefault(option => periodStart >= option.EffectiveForPeriodsStartingOnOrAfter);

        if (selected is null)
        {
            return new RevenueIxbrlTaxonomySelection(
                "manual-revenue-taxonomy-review-required",
                "",
                "Manual Revenue taxonomy review required for FRS 102 periods before 2019-01-01",
                "",
                false,
                Frs102Options[^1].EffectiveForPeriodsStartingOnOrAfter,
                [
                    IrishStatutoryRuleSources.RevenueAcceptedTaxonomies,
                    IrishStatutoryRuleSources.RevenueIxbrlOverview
                ]);
        }

        var sources = new[]
        {
            IrishStatutoryRuleSources.RevenueAcceptedTaxonomies,
            IrishStatutoryRuleSources.FrcFrs102
        };

        return new RevenueIxbrlTaxonomySelection(
            selected.TaxonomyKey,
            selected.TaxonomyDate,
            selected.Label,
            selected.SchemaRef,
            true,
            selected.EffectiveForPeriodsStartingOnOrAfter,
            sources);
    }
}
