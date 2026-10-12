using Accounts.Api.Entities;

namespace Accounts.Api.Services;

public readonly record struct DerivedCategoryClassification(
    AccountStatementLine StatementLine,
    AccountCashFlowClass CashFlowClass,
    AccountSystemRole? SystemRole);

// Today's code-number rules, in one pure function. It must reproduce exactly what the statements
// engine (FinancialStatementsService, AccountingLedgerService) and AdjustmentService decide from a
// category's Type and Code. The migration that back-fills explicit attributes mirrors it in SQL
// (AddExplicitAccountCategoryClassification.BackfillSql) and a parity test pins both.
public static class LegacyCategoryClassification
{
    public static DerivedCategoryClassification Derive(
        AccountCategoryType type,
        string code,
        bool isNonTradingIncome = false) =>
        new(DeriveStatementLine(type, code, isNonTradingIncome), DeriveCashFlowClass(type, code), DeriveSystemRole(code));

    private static bool Starts(string code, string prefix) => code.StartsWith(prefix, StringComparison.Ordinal);

    private static AccountStatementLine DeriveStatementLine(AccountCategoryType type, string code, bool isNonTradingIncome) =>
        type switch
        {
            AccountCategoryType.Income => Starts(code, "4") && !isNonTradingIncome
                ? AccountStatementLine.Turnover
                : AccountStatementLine.OtherOperatingIncome,
            AccountCategoryType.Expense => Starts(code, "5") ? AccountStatementLine.CostOfSales
                : Starts(code, "69") ? AccountStatementLine.InterestPayable
                : Starts(code, "8") ? AccountStatementLine.TaxOnProfit
                : code == "7000" ? AccountStatementLine.Depreciation
                : AccountStatementLine.AdministrativeExpenses,
            AccountCategoryType.Asset => Starts(code, "00") ? AccountStatementLine.TangibleFixedAssets
                : Starts(code, "14") ? AccountStatementLine.CashAtBank
                : code == "1000" ? AccountStatementLine.Stocks
                : code == "1200" ? AccountStatementLine.Prepayments
                : AccountStatementLine.Debtors,
            AccountCategoryType.Liability => code == "2700" ? AccountStatementLine.LoansAfterYear
                : Starts(code, "27") || Starts(code, "29") ? AccountStatementLine.OtherCreditorsAfterYear
                : code == "2000" ? AccountStatementLine.TradeCreditors
                : code == "2100" ? AccountStatementLine.Accruals
                : Starts(code, "22") || Starts(code, "23") || Starts(code, "24") ? AccountStatementLine.TaxCreditors
                : AccountStatementLine.OtherCreditorsWithinYear,
            AccountCategoryType.Equity => code == "3000" ? AccountStatementLine.ShareCapital
                : code == "3200" ? AccountStatementLine.DividendsPaid
                : AccountStatementLine.Reserves,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown account category type.")
        };

    // The cash-flow statement classifies the contra category of each cash movement. Cash accounts are
    // never a contra. Precedence below matters only for Equity codes that today's predicates place in
    // two buckets at once (24xx/8xxx, 26xx/27xx, 2800 typed as Equity); see the parity tests.
    private static AccountCashFlowClass DeriveCashFlowClass(AccountCategoryType type, string code)
    {
        if (type == AccountCategoryType.Asset && Starts(code, "14")) return AccountCashFlowClass.Cash;
        if (type == AccountCategoryType.Asset && Starts(code, "00")) return AccountCashFlowClass.Investing;
        if (Starts(code, "24") || Starts(code, "8")) return AccountCashFlowClass.Tax;
        if (code is "2800" or "3200") return AccountCashFlowClass.Dividend;
        if (Starts(code, "26") || Starts(code, "27")) return AccountCashFlowClass.Loan;
        if (code == "3000") return AccountCashFlowClass.ShareIssue;
        if (Starts(code, "25") || type == AccountCategoryType.Equity) return AccountCashFlowClass.OtherFinancing;
        return AccountCashFlowClass.Operating;
    }

    // Roles are found by code alone today (AccountingLedgerService.PreferredCategory,
    // AdjustmentService.GetOrCreateCategoryIdAsync, the VAT reconciliation), regardless of Type.
    private static AccountSystemRole? DeriveSystemRole(string code) => code switch
    {
        "1400" => AccountSystemRole.BankControl,
        "3100" => AccountSystemRole.RetainedEarnings,
        "7000" => AccountSystemRole.Depreciation,
        "1200" => AccountSystemRole.Prepayments,
        "1000" => AccountSystemRole.Stock,
        "5000" => AccountSystemRole.CostOfSalesStockMovement,
        "2100" => AccountSystemRole.Accruals,
        "1100" => AccountSystemRole.TradeDebtors,
        "4000" => AccountSystemRole.TradeDebtorIncome,
        "2000" => AccountSystemRole.TradeCreditors,
        "2500" => AccountSystemRole.DirectorLoanLiability,
        "1150" => AccountSystemRole.DirectorLoanReceivable,
        "8000" => AccountSystemRole.CorporationTaxCharge,
        "2400" => AccountSystemRole.CorporationTaxPayable,
        "3200" => AccountSystemRole.DividendsPaid,
        "2800" => AccountSystemRole.DividendsPayable,
        "7050" => AccountSystemRole.DisposalLoss,
        "9000" => AccountSystemRole.DisposalGain,
        "2200" => AccountSystemRole.VatPayable,
        "1300" => AccountSystemRole.VatReceivable,
        _ => null
    };
}

// Runtime view of a category's presentation attributes: the explicit value when set, otherwise
// today's code rules. A row is "classified" once its StatementLine is set; for a classified row a
// null SystemRole means "no role", so only an unclassified row falls back to the derived role.
public static class CategoryClassification
{
    private static DerivedCategoryClassification Derived(AccountCategory category) =>
        LegacyCategoryClassification.Derive(category.Type, category.Code, category.IsNonTradingIncome);

    public static AccountStatementLine Line(AccountCategory category)
    {
        var line = category.StatementLine ?? Derived(category).StatementLine;
        // A reviewed non-trading flag always keeps income out of turnover, even when it was set after
        // the row was classified (the engine checks the flag directly today).
        return line == AccountStatementLine.Turnover
            && category.Type == AccountCategoryType.Income
            && category.IsNonTradingIncome
            ? AccountStatementLine.OtherOperatingIncome
            : line;
    }

    public static AccountCashFlowClass CashFlow(AccountCategory category) =>
        category.CashFlowClass ?? Derived(category).CashFlowClass;

    public static AccountSystemRole? Role(AccountCategory category) =>
        category.SystemRole ?? (category.StatementLine is null ? Derived(category).SystemRole : null);

    // The one definition of cash: an account presented on the CashAtBank line.
    public static bool IsCash(AccountCategory category) => Line(category) == AccountStatementLine.CashAtBank;

    // A bank overdraft account: a Liability whose cash-flow class is Cash (spec §4 row 2200; the only
    // non-cash line IsCashFlowClassAllowed permits Cash on). The charity presentation treats it, with
    // the cash accounts, as cash and cash equivalents.
    public static bool IsCashOrBankOverdraft(AccountCategory category) =>
        IsCash(category)
        || (category.Type == AccountCategoryType.Liability && CashFlow(category) == AccountCashFlowClass.Cash);

    // The account holding a system role: a company row before a global system row, then the lowest
    // Id (the same preference the ledger applied to code 1400 / 3100 before roles existed).
    public static AccountCategory? PreferredByRole(
        IEnumerable<AccountCategory> categories,
        int companyId,
        AccountSystemRole role) =>
        categories
            .Where(category => Role(category) == role)
            .OrderByDescending(category => category.CompanyId == companyId)
            .ThenBy(category => category.Id)
            .FirstOrDefault();

    // Cash-flow class Cash means "part of cash": an account on the CashAtBank line, or a bank
    // overdraft (a Liability presented within other creditors due within one year, spec §4 row 2200).
    // Any other line with class Cash, or a CashAtBank account with a non-cash class, is rejected so
    // the statements have a single, consistent definition of cash.
    public static bool IsCashFlowClassAllowed(
        AccountCategoryType type,
        AccountStatementLine line,
        AccountCashFlowClass cashFlowClass) =>
        line == AccountStatementLine.CashAtBank
            ? cashFlowClass == AccountCashFlowClass.Cash
            : cashFlowClass != AccountCashFlowClass.Cash
                || (type == AccountCategoryType.Liability && line == AccountStatementLine.OtherCreditorsWithinYear);

    public static bool IsLineAllowedForType(AccountCategoryType type, AccountStatementLine line) => type switch
    {
        AccountCategoryType.Income => line is AccountStatementLine.Turnover or AccountStatementLine.OtherOperatingIncome,
        AccountCategoryType.Expense => line is AccountStatementLine.CostOfSales or AccountStatementLine.AdministrativeExpenses
            or AccountStatementLine.Depreciation or AccountStatementLine.InterestPayable or AccountStatementLine.TaxOnProfit,
        AccountCategoryType.Asset => line is AccountStatementLine.TangibleFixedAssets or AccountStatementLine.Stocks
            or AccountStatementLine.Debtors or AccountStatementLine.Prepayments or AccountStatementLine.CashAtBank,
        AccountCategoryType.Liability => line is AccountStatementLine.TradeCreditors or AccountStatementLine.Accruals
            or AccountStatementLine.TaxCreditors or AccountStatementLine.OtherCreditorsWithinYear
            or AccountStatementLine.LoansAfterYear or AccountStatementLine.OtherCreditorsAfterYear
            or AccountStatementLine.Suspense,
        AccountCategoryType.Equity => line is AccountStatementLine.ShareCapital or AccountStatementLine.Reserves
            or AccountStatementLine.RestrictedFunds or AccountStatementLine.DividendsPaid
            or AccountStatementLine.OpeningBalanceControl,
        _ => false
    };

    // Makes a row that is being created explicit: missing StatementLine / CashFlowClass are filled
    // from today's rules, and the derived role is assigned only when no other row of the same
    // company already resolves to it (one role per company; the partial unique index enforces it).
    // A caller-given line that differs from the derived line means the code's meaning does not apply
    // (e.g. a 1200 presented as cash at bank), so no role is derived from the code in that case.
    public static void ClassifyNew(AccountCategory category, IEnumerable<AccountCategory> otherCompanyRows)
    {
        var derived = Derived(category);
        var explicitLineDiffers = category.StatementLine is { } given && given != derived.StatementLine;
        category.StatementLine ??= derived.StatementLine;
        // Keep the class consistent with the single definition of cash (IsCashFlowClassAllowed).
        category.CashFlowClass ??= category.StatementLine == AccountStatementLine.CashAtBank
            ? AccountCashFlowClass.Cash
            : derived.CashFlowClass == AccountCashFlowClass.Cash ? AccountCashFlowClass.Operating : derived.CashFlowClass;
        if (category.SystemRole is null && !explicitLineDiffers && derived.SystemRole is { } candidate
            && !otherCompanyRows.Any(other => !ReferenceEquals(other, category)
                && other.CompanyId == category.CompanyId
                && Role(other) == candidate))
        {
            category.SystemRole = candidate;
        }
    }
}
