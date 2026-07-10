using Accounts.Api.Entities;

namespace Accounts.Api.Services;

/// <summary>
/// Stable identifiers for generated statutory-note checklist rows. Titles are presentation text and
/// may change; these codes are the release/readiness contract and must remain stable.
/// </summary>
public static class StatutoryNoteCodes
{
    public const string AccountingPolicies = "ACC-POLICIES";
    public const string FixedAssets = "FIXED-ASSETS";
    public const string Inventories = "INVENTORIES";
    public const string Debtors = "DEBTORS";
    public const string CurrentCreditors = "CREDITORS-CURRENT";
    public const string LongTermCreditors = "CREDITORS-LONG-TERM";
    public const string ShareCapital = "SHARE-CAPITAL";
    public const string Reserves = "RESERVES";
    public const string Employees = "EMPLOYEES";
    public const string DirectorTransactions = "DIRECTOR-TRANSACTIONS";
    public const string DirectorRemuneration = "DIRECTOR-REMUNERATION";
    public const string PostBalanceSheetEvents = "POST-BALANCE-SHEET-EVENTS";
    public const string RelatedParties = "RELATED-PARTIES";
    public const string UltimateControllingParty = "ULTIMATE-CONTROLLING-PARTY";
    public const string ContingentLiabilities = "CONTINGENT-LIABILITIES";
    public const string GoingConcern = "GOING-CONCERN";
    public const string Turnover = "TURNOVER";
    public const string TaxOnProfit = "TAX-ON-PROFIT";
    public const string Dividends = "DIVIDENDS";
    public const string FinancialInstruments = "FINANCIAL-INSTRUMENTS";
    public const string CapitalCommitments = "CAPITAL-COMMITMENTS";
    public const string DeferredTax = "DEFERRED-TAX";
    public const string Approval = "APPROVAL";

    public static readonly IReadOnlyDictionary<string, string> Titles =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AccountingPolicies] = "Accounting Policies",
            [FixedAssets] = "Tangible Fixed Assets",
            [Inventories] = "Inventories",
            [Debtors] = "Debtors",
            [CurrentCreditors] = "Creditors: Amounts Falling Due Within One Year",
            [LongTermCreditors] = "Creditors: Amounts Falling Due After More Than One Year",
            [ShareCapital] = "Share Capital",
            [Reserves] = "Capital and Reserves",
            [Employees] = "Employees and Remuneration",
            [DirectorTransactions] = "Advances, Credits and Guarantees to Directors",
            [DirectorRemuneration] = "Directors' Remuneration",
            [PostBalanceSheetEvents] = "Post Balance Sheet Events",
            [RelatedParties] = "Related Party Transactions",
            [UltimateControllingParty] = "Ultimate Controlling Party",
            [ContingentLiabilities] = "Contingent Liabilities",
            [GoingConcern] = "Going Concern",
            [Turnover] = "Turnover",
            [TaxOnProfit] = "Tax on Profit on Ordinary Activities",
            [Dividends] = "Dividends",
            [FinancialInstruments] = "Financial Instruments",
            [CapitalCommitments] = "Capital Commitments",
            [DeferredTax] = "Deferred Tax",
            [Approval] = "Approval of Financial Statements"
        };

    public static bool IsStableCode(string? code) =>
        code is not null && Titles.ContainsKey(code);

    public static bool IsProfitAndLossNote(string? code) =>
        code is Turnover or TaxOnProfit or Dividends;

    public static bool ContainsUnsupportedRepresentation(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        return System.Text.RegularExpressions.Regex.IsMatch(
            content,
            @"\bnone\b|deferred\s+tax|derivative|financial\s+instrument|capital\s+commitment",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
            | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    public static IReadOnlyList<string> RegimeRequiredCodes(
        ElectedRegime regime,
        CompanySizeClass sizeClass,
        Company company)
    {
        var codes = new List<string> { AccountingPolicies, GoingConcern, Approval };
        if (regime == ElectedRegime.Micro)
        {
            codes.Add(DirectorTransactions);
            return codes;
        }

        codes.AddRange([
            FixedAssets,
            Inventories,
            Debtors,
            CurrentCreditors,
            LongTermCreditors,
            ShareCapital,
            Reserves,
            Employees,
            DirectorTransactions,
            DirectorRemuneration,
            PostBalanceSheetEvents,
            RelatedParties,
            ContingentLiabilities
        ]);

        if (regime != ElectedRegime.SmallAbridged)
            codes.AddRange([Turnover, TaxOnProfit, Dividends]);

        if (company.IsGroupMember)
            codes.Add(UltimateControllingParty);

        if (regime is ElectedRegime.Medium or ElectedRegime.Full || sizeClass >= CompanySizeClass.Medium)
            codes.AddRange([FinancialInstruments, CapitalCommitments, DeferredTax]);

        return codes.Distinct(StringComparer.Ordinal).ToList();
    }
}
