using Accounts.Api.Entities;

namespace Accounts.Api.Services;

// One row of a chart of accounts. Rows without a StatementLine are classified from their code by
// CategoryClassification.ClassifyNew (the generic chart, byte-identical to before variants existed);
// rows with one are fully explicit, including a null SystemRole meaning "no role".
public sealed record ChartAccountDefinition(
    string Code,
    string Name,
    AccountCategoryType Type,
    TaxTreatment TaxTreatment,
    AccountStatementLine? StatementLine = null,
    AccountCashFlowClass? CashFlowClass = null,
    AccountSystemRole? SystemRole = null)
{
    public ChartAccountDefaults.DefaultAccount ToDefault() => new(Code, Name, Type, TaxTreatment, StatementLine, CashFlowClass);
}

// A chart variant: the rows seeded for a new company, the rows AdjustmentService creates only when a
// posting needs a role the seeded chart lacks, and the fixed-asset-class and expense-keyword tables
// automatic adjustments use.
public sealed class ChartDefinition(
    ChartOfAccountsVariant variant,
    IReadOnlyList<ChartAccountDefinition> accounts,
    IReadOnlyList<ChartAccountDefinition> lazyRoleAccounts,
    Func<string, ChartAccountDefaults.DefaultAccount> fixedAssetAccount,
    Func<string, ChartAccountDefaults.DefaultAccount> expenseAccountForDescription,
    IReadOnlyDictionary<AccountSystemRole, string>? refusedRoles = null,
    Func<string, ChartAccountDefaults.DefaultAccount?>? depreciationAccountForClass = null)
{
    public ChartOfAccountsVariant Variant { get; } = variant;
    public IReadOnlyList<ChartAccountDefinition> Accounts { get; } = accounts;
    public IReadOnlyList<ChartAccountDefinition> LazyRoleAccounts { get; } = lazyRoleAccounts;

    // Roles a company on this chart can never post to, with the reason given to the user.
    public IReadOnlyDictionary<AccountSystemRole, string> RefusedRoles { get; } =
        refusedRoles ?? new Dictionary<AccountSystemRole, string>();

    // The variant's own account for a role (seeded or lazy), or null when the variant does not define one.
    public ChartAccountDefinition? RoleAccount(AccountSystemRole role) =>
        Accounts.Concat(LazyRoleAccounts).FirstOrDefault(account => account.SystemRole == role);

    public ChartAccountDefaults.DefaultAccount FixedAssetAccount(string assetClass) => fixedAssetAccount(assetClass);

    // A class-specific depreciation expense account, or null for the chart's Depreciation role account.
    public ChartAccountDefaults.DefaultAccount? DepreciationAccount(string assetClass) =>
        depreciationAccountForClass?.Invoke(assetClass);

    public ChartAccountDefaults.DefaultAccount ExpenseAccountForDescription(string description) =>
        expenseAccountForDescription(description.ToLowerInvariant());

    // New, unsaved company rows in chart order. Implicit rows are classified exactly as before (in the
    // same order, so derived roles land on the same rows); explicit rows keep their own attributes.
    public List<AccountCategory> CreateCategories(int companyId)
    {
        var categories = Accounts.Select(account => new AccountCategory
        {
            CompanyId = companyId,
            Code = account.Code,
            Name = account.Name,
            Type = account.Type,
            TaxTreatment = account.TaxTreatment,
            IsSystem = true,
            StatementLine = account.StatementLine,
            CashFlowClass = account.CashFlowClass,
            SystemRole = account.SystemRole
        }).ToList();
        foreach (var (category, account) in categories.Zip(Accounts))
        {
            if (account.StatementLine is null)
                CategoryClassification.ClassifyNew(category, categories);
        }
        return categories;
    }
}

public static class ChartDefinitions
{
    // A new company limited by guarantee that is a charitable organisation starts on the charity chart;
    // every other company starts on the generic trading chart. Existing companies are never changed.
    public static ChartOfAccountsVariant DefaultVariantFor(CompanyType companyType, bool isCharitableOrganisation) =>
        companyType == CompanyType.CompanyLimitedByGuarantee && isCharitableOrganisation
            ? ChartOfAccountsVariant.CharityClg
            : ChartOfAccountsVariant.GenericTrading;

    public const string CharityChartRequiresClg =
        "The charity / CLG chart of accounts is only available to a company limited by guarantee.";

    public const string CharityChartCompanyTypeLocked =
        "This company uses the charity / CLG chart of accounts, which is only available to a company limited by guarantee; keep the company type as company limited by guarantee.";

    // The charity / CLG chart is only for a company limited by guarantee; every type may use the generic chart.
    public static bool IsAvailableTo(ChartOfAccountsVariant variant, CompanyType companyType) =>
        variant != ChartOfAccountsVariant.CharityClg || companyType == CompanyType.CompanyLimitedByGuarantee;

    public static ChartDefinition For(ChartOfAccountsVariant variant) => variant switch
    {
        ChartOfAccountsVariant.GenericTrading => GenericTrading,
        ChartOfAccountsVariant.CharityClg => CharityClg,
        _ => throw new BusinessRuleException($"Chart of accounts variant {variant} is not supported.")
    };

    private static ChartAccountDefinition Generic(string code, string name, AccountCategoryType type, TaxTreatment taxTreatment) =>
        new(code, name, type, taxTreatment);

    // Today's generic Irish trading chart (52 rows), unchanged in content and order.
    public static readonly ChartDefinition GenericTrading = new(
        ChartOfAccountsVariant.GenericTrading,
        [
            // Income
            Generic("4000", "Sales / Revenue", AccountCategoryType.Income, TaxTreatment.Deductible),
            Generic("4100", "Other Income", AccountCategoryType.Income, TaxTreatment.Deductible),
            Generic("4200", "Interest Received", AccountCategoryType.Income, TaxTreatment.Deductible),
            Generic("4300", "Grants Received", AccountCategoryType.Income, TaxTreatment.Deductible),

            // Cost of Sales
            Generic("5000", "Cost of Sales", AccountCategoryType.Expense, TaxTreatment.Deductible),
            Generic("5100", "Direct Materials", AccountCategoryType.Expense, TaxTreatment.Deductible),
            Generic("5200", "Direct Labour", AccountCategoryType.Expense, TaxTreatment.Deductible),

            // Overheads
            Generic("6000", "Wages & Salaries", AccountCategoryType.Expense, TaxTreatment.Deductible),
            Generic("6010", "Employer PRSI", AccountCategoryType.Expense, TaxTreatment.Deductible),
            Generic("6020", "Pension Contributions", AccountCategoryType.Expense, TaxTreatment.Deductible),
            Generic("6100", "Rent", AccountCategoryType.Expense, TaxTreatment.Deductible),
            Generic("6110", "Rates", AccountCategoryType.Expense, TaxTreatment.Deductible),
            Generic("6200", "Insurance", AccountCategoryType.Expense, TaxTreatment.Deductible),
            Generic("6300", "Light & Heat", AccountCategoryType.Expense, TaxTreatment.Deductible),
            Generic("6400", "Telephone & Internet", AccountCategoryType.Expense, TaxTreatment.Deductible),
            Generic("6500", "Office Supplies & Stationery", AccountCategoryType.Expense, TaxTreatment.Deductible),
            Generic("6600", "Motor Expenses", AccountCategoryType.Expense, TaxTreatment.Deductible),
            Generic("6700", "Travel & Subsistence", AccountCategoryType.Expense, TaxTreatment.Deductible),
            Generic("6800", "Professional Fees", AccountCategoryType.Expense, TaxTreatment.Deductible),
            Generic("6810", "Accountancy Fees", AccountCategoryType.Expense, TaxTreatment.Deductible),
            Generic("6820", "Legal Fees", AccountCategoryType.Expense, TaxTreatment.Deductible),
            Generic("6900", "Bank Charges & Interest", AccountCategoryType.Expense, TaxTreatment.Deductible),
            Generic("7000", "Depreciation", AccountCategoryType.Expense, TaxTreatment.NonDeductible),
            Generic("7100", "Repairs & Maintenance", AccountCategoryType.Expense, TaxTreatment.Deductible),
            Generic("7200", "Advertising & Marketing", AccountCategoryType.Expense, TaxTreatment.Deductible),
            Generic("7300", "Software & Subscriptions", AccountCategoryType.Expense, TaxTreatment.Deductible),
            Generic("7400", "Training & Development", AccountCategoryType.Expense, TaxTreatment.Deductible),
            Generic("7500", "Entertainment", AccountCategoryType.Expense, TaxTreatment.NonDeductible),
            Generic("7900", "Sundry Expenses", AccountCategoryType.Expense, TaxTreatment.Deductible),
            Generic("8000", "Corporation Tax Charge", AccountCategoryType.Expense, TaxTreatment.NonDeductible),

            // Fixed Assets
            Generic("0010", "Land & Buildings", AccountCategoryType.Asset, TaxTreatment.CapitalAllowance),
            Generic("0020", "Plant & Machinery", AccountCategoryType.Asset, TaxTreatment.CapitalAllowance),
            Generic("0030", "Motor Vehicles", AccountCategoryType.Asset, TaxTreatment.CapitalAllowance),
            Generic("0040", "Office Equipment", AccountCategoryType.Asset, TaxTreatment.CapitalAllowance),
            Generic("0050", "Computer Equipment", AccountCategoryType.Asset, TaxTreatment.CapitalAllowance),

            // Current Assets
            Generic("1000", "Stock / Inventory", AccountCategoryType.Asset, TaxTreatment.Other),
            Generic("1100", "Trade Debtors", AccountCategoryType.Asset, TaxTreatment.Other),
            Generic("1200", "Prepayments", AccountCategoryType.Asset, TaxTreatment.Other),
            Generic("1300", "VAT Receivable", AccountCategoryType.Asset, TaxTreatment.Other),
            Generic("1400", "Bank Current Account", AccountCategoryType.Asset, TaxTreatment.Other),
            Generic("1410", "Petty Cash", AccountCategoryType.Asset, TaxTreatment.Other),

            // Liabilities
            Generic("2000", "Trade Creditors", AccountCategoryType.Liability, TaxTreatment.Other),
            Generic("2100", "Accruals", AccountCategoryType.Liability, TaxTreatment.Other),
            Generic("2200", "VAT Payable", AccountCategoryType.Liability, TaxTreatment.Other),
            Generic("2300", "PAYE / PRSI Payable", AccountCategoryType.Liability, TaxTreatment.Other),
            Generic("2400", "Corporation Tax Payable", AccountCategoryType.Liability, TaxTreatment.Other),
            Generic("2500", "Director Loan Account", AccountCategoryType.Liability, TaxTreatment.Other),
            Generic("2600", "Bank Loan (< 1 year)", AccountCategoryType.Liability, TaxTreatment.Other),
            Generic("2700", "Bank Loan (> 1 year)", AccountCategoryType.Liability, TaxTreatment.Other),

            // Equity
            Generic("3000", "Share Capital", AccountCategoryType.Equity, TaxTreatment.Other),
            Generic("3100", "Retained Earnings", AccountCategoryType.Equity, TaxTreatment.Other),
            Generic("3200", "Dividends Paid", AccountCategoryType.Equity, TaxTreatment.Other),
        ],
        [],
        GenericFixedAssetAccount,
        GenericExpenseAccountForDescription);

    private static ChartAccountDefaults.DefaultAccount GenericFixedAssetAccount(string assetClass) => new(
        assetClass switch
        {
            "Land & Buildings" or "Property" => "0010",
            "Plant & Machinery" => "0020",
            "Motor Vehicles" or "Vehicles" => "0030",
            "Office Equipment" or "Equipment" or "Furniture" => "0040",
            "Computer Equipment" or "IT" => "0050",
            _ => "0040"
        },
        assetClass,
        AccountCategoryType.Asset,
        TaxTreatment.CapitalAllowance);

    private static ChartAccountDefaults.DefaultAccount GenericExpenseAccountForDescription(string lower)
    {
        var (code, name) = lower switch
        {
            var text when text.Contains("interest") || text.Contains("loan") => ("6900", "Bank Charges & Interest"),
            var text when text.Contains("account") || text.Contains("audit") => ("6810", "Accountancy Fees"),
            var text when text.Contains("legal") || text.Contains("solicitor") => ("6820", "Legal Fees"),
            var text when text.Contains("insurance") => ("6200", "Insurance"),
            var text when text.Contains("electric") || text.Contains("heat") || text.Contains("light") => ("6300", "Light & Heat"),
            var text when text.Contains("phone") || text.Contains("internet") || text.Contains("vodafone") => ("6400", "Telephone & Internet"),
            var text when text.Contains("software") || text.Contains("subscription") => ("7300", "Software & Subscriptions"),
            var text when text.Contains("rent") => ("6100", "Rent"),
            _ => ("7900", "Sundry Expenses")
        };
        return new(code, name, AccountCategoryType.Expense, TaxTreatment.Deductible);
    }

    private static ChartAccountDefinition Asset(string code, string name, AccountStatementLine line, AccountCashFlowClass cashFlow, AccountSystemRole? role = null) =>
        new(code, name, AccountCategoryType.Asset,
            line == AccountStatementLine.TangibleFixedAssets ? TaxTreatment.CapitalAllowance : TaxTreatment.Other,
            line, cashFlow, role);

    private static ChartAccountDefinition Liability(string code, string name, AccountStatementLine line, AccountCashFlowClass cashFlow, AccountSystemRole? role = null) =>
        new(code, name, AccountCategoryType.Liability, TaxTreatment.Other, line, cashFlow, role);

    private static ChartAccountDefinition Fund(string code, string name, AccountStatementLine line, AccountSystemRole? role = null) =>
        new(code, name, AccountCategoryType.Equity, TaxTreatment.Other, line, AccountCashFlowClass.OtherFinancing, role);

    // Income and expense rows are all Operating: an explicit class keeps 8xxx out of "tax paid".
    private static ChartAccountDefinition Income(string code, string name, AccountSystemRole? role = null) =>
        new(code, name, AccountCategoryType.Income, TaxTreatment.Deductible, AccountStatementLine.Turnover, AccountCashFlowClass.Operating, role);

    private static ChartAccountDefinition Expense(string code, string name, AccountStatementLine line = AccountStatementLine.AdministrativeExpenses, AccountSystemRole? role = null) =>
        new(code, name, AccountCategoryType.Expense,
            line == AccountStatementLine.Depreciation ? TaxTreatment.NonDeductible : TaxTreatment.Deductible,
            line, AccountCashFlowClass.Operating, role);

    // The charity / company-limited-by-guarantee chart (spec §4; the owner's chart, sections 1-11).
    // Names are generic: no bank account, contract or counterparty detail ever enters the repository,
    // and a company can rename its own rows.
    public static readonly ChartDefinition CharityClg = new(
        ChartOfAccountsVariant.CharityClg,
        [
            // 1. Fixed assets. Depreciation credits the cost account; the x1 rows are kept for later use.
            Asset("0010", "Equipment held under finance lease — cost", AccountStatementLine.TangibleFixedAssets, AccountCashFlowClass.Investing),
            Asset("0011", "Equipment held under finance lease — accumulated depreciation", AccountStatementLine.TangibleFixedAssets, AccountCashFlowClass.Investing),
            Asset("0020", "Office and IT equipment — cost", AccountStatementLine.TangibleFixedAssets, AccountCashFlowClass.Investing),
            Asset("0021", "Office and IT equipment — accumulated depreciation", AccountStatementLine.TangibleFixedAssets, AccountCashFlowClass.Investing),
            Asset("0030", "Fixtures and fittings — cost", AccountStatementLine.TangibleFixedAssets, AccountCashFlowClass.Investing),
            Asset("0031", "Fixtures and fittings — accumulated depreciation", AccountStatementLine.TangibleFixedAssets, AccountCashFlowClass.Investing),

            // 2. Current assets
            Asset("1000", "Stock — goods held for resale", AccountStatementLine.Stocks, AccountCashFlowClass.Operating, AccountSystemRole.Stock),
            Asset("1100", "Trade debtors", AccountStatementLine.Debtors, AccountCashFlowClass.Operating, AccountSystemRole.TradeDebtors),
            Asset("1110", "Grants receivable", AccountStatementLine.Debtors, AccountCashFlowClass.Operating),
            Asset("1120", "Prepayments", AccountStatementLine.Prepayments, AccountCashFlowClass.Operating, AccountSystemRole.Prepayments),
            Asset("1200", "Bank current account", AccountStatementLine.CashAtBank, AccountCashFlowClass.Cash, AccountSystemRole.BankControl),
            Asset("1230", "Cash in hand", AccountStatementLine.CashAtBank, AccountCashFlowClass.Cash),

            // 3. Creditors: amounts falling due within one year. 2200 is part of cash (an overdraft).
            Liability("2000", "Trade creditors", AccountStatementLine.TradeCreditors, AccountCashFlowClass.Operating, AccountSystemRole.TradeCreditors),
            Liability("2100", "Accruals", AccountStatementLine.Accruals, AccountCashFlowClass.Operating, AccountSystemRole.Accruals),
            Liability("2200", "Bank overdraft", AccountStatementLine.OtherCreditorsWithinYear, AccountCashFlowClass.Cash),
            Liability("2210", "Finance lease — obligations due within one year", AccountStatementLine.OtherCreditorsWithinYear, AccountCashFlowClass.Loan),
            Liability("2215", "Finance lease — arrears", AccountStatementLine.OtherCreditorsWithinYear, AccountCashFlowClass.Loan),
            Liability("2220", "Community loan — balance outstanding", AccountStatementLine.OtherCreditorsWithinYear, AccountCashFlowClass.Loan),
            Liability("2230", "Director's loan account", AccountStatementLine.OtherCreditorsWithinYear, AccountCashFlowClass.OtherFinancing, AccountSystemRole.DirectorLoanLiability),
            Liability("2240", "Other creditors", AccountStatementLine.OtherCreditorsWithinYear, AccountCashFlowClass.Operating),
            Liability("2250", "PAYE / PRSI", AccountStatementLine.TaxCreditors, AccountCashFlowClass.Operating),
            Liability("2260", "VAT", AccountStatementLine.TaxCreditors, AccountCashFlowClass.Operating, AccountSystemRole.VatPayable),

            // 4. Creditors: amounts falling due after more than one year
            Liability("2300", "Finance lease — obligations due after more than one year", AccountStatementLine.OtherCreditorsAfterYear, AccountCashFlowClass.Loan),
            Liability("2310", "Long-term loans", AccountStatementLine.LoansAfterYear, AccountCashFlowClass.Loan),

            // 5. Members' funds. Each year's result closes into 3000 (the RetainedEarnings role).
            Fund("3000", "Members' funds brought forward", AccountStatementLine.Reserves, AccountSystemRole.RetainedEarnings),
            Fund("3100", "Surplus / (deficit) for the financial year", AccountStatementLine.Reserves),
            Fund("3200", "Restricted funds", AccountStatementLine.RestrictedFunds),

            // 6. Income
            Income("4000", "Donations and gifts — unrestricted", AccountSystemRole.TradeDebtorIncome),
            Income("4010", "Regular giving — standing orders and recurring donations"),
            Income("4020", "Grants — restricted"),
            Income("4030", "Grants — unrestricted"),
            Income("4040", "Public funding and service agreements"),
            Income("4100", "Charity shop — sales"),
            Income("4110", "Fundraising events"),
            Income("4200", "Membership and subscription income"),
            Income("4300", "Online payment receipts"),
            Income("4400", "Technology partnership and hosting income"),
            Income("4800", "Other income, including reimbursed expenditure"),

            // 7. Cost of goods sold
            Expense("5000", "Goods purchased for resale", AccountStatementLine.CostOfSales),
            Expense("5010", "Movement in stock", AccountStatementLine.CostOfSales, AccountSystemRole.CostOfSalesStockMovement),

            // 8. Charitable activity costs
            Expense("6000", "Software subscriptions and licences"),
            Expense("6010", "Hosting, domains and infrastructure"),
            Expense("6020", "Platform development"),
            Expense("6100", "Volunteer expenses"),
            Expense("6110", "Member and participant costs"),
            Expense("6200", "Training and development"),
            Expense("6300", "Premises — rent, rates, utilities"),

            // 9. Support and governance costs
            Expense("7000", "Insurance — public liability"),
            Expense("7010", "Insurance — trustee and directors' indemnity"),
            Expense("7100", "Memberships and affiliations"),
            Expense("7200", "Legal and professional fees"),
            Expense("7210", "Accountancy and filing fees"),
            Expense("7300", "CRO, Charities Regulator and other statutory fees"),
            Expense("7400", "Postage, printing and stationery"),
            Expense("7500", "Telephone and internet"),
            Expense("7600", "Bank charges and account fees"),
            Expense("7700", "Sundry expenses"),
            Expense("7800", "Garda vetting and safeguarding costs"),

            // 10. Finance costs and depreciation. 8200 is an administrative expense, not tax.
            Expense("8000", "Depreciation — owned assets", AccountStatementLine.Depreciation, AccountSystemRole.Depreciation),
            Expense("8010", "Depreciation — assets held under finance lease", AccountStatementLine.Depreciation),
            Expense("8100", "Bank interest payable — overdraft", AccountStatementLine.InterestPayable),
            Expense("8110", "Finance lease interest", AccountStatementLine.InterestPayable),
            Expense("8120", "Loan interest", AccountStatementLine.InterestPayable),
            Expense("8200", "Government levies and stamp duty"),

            // 11. Control. The suspense account must be nil before the statements are approved.
            Liability("9998", "Suspense — uncategorised", AccountStatementLine.Suspense, AccountCashFlowClass.Operating, AccountSystemRole.Suspense),
            Fund("9999", "Opening balance control", AccountStatementLine.OpeningBalanceControl, AccountSystemRole.OpeningBalanceControl),
        ],
        [
            // Created only if a posting needs them, on codes the charity chart leaves free.
            Asset("1150", "Director's loan account (debit balance)", AccountStatementLine.Debtors, AccountCashFlowClass.Operating, AccountSystemRole.DirectorLoanReceivable),
            new("4900", "Gain on disposal of fixed assets", AccountCategoryType.Income, TaxTreatment.Other,
                AccountStatementLine.OtherOperatingIncome, AccountCashFlowClass.Operating, AccountSystemRole.DisposalGain),
            new("7050", "Loss on disposal of fixed assets", AccountCategoryType.Expense, TaxTreatment.NonDeductible,
                AccountStatementLine.AdministrativeExpenses, AccountCashFlowClass.Operating, AccountSystemRole.DisposalLoss),
            // Corporation tax for a CLG without a charitable tax exemption (an exempt company posts none
            // unless its entered liability is confirmed as due).
            new("8900", "Corporation tax charge", AccountCategoryType.Expense, TaxTreatment.NonDeductible,
                AccountStatementLine.TaxOnProfit, AccountCashFlowClass.Tax, AccountSystemRole.CorporationTaxCharge),
            Liability("2270", "Corporation tax payable", AccountStatementLine.TaxCreditors, AccountCashFlowClass.Tax, AccountSystemRole.CorporationTaxPayable),
        ],
        CharityFixedAssetAccount,
        CharityExpenseAccountForDescription,
        new Dictionary<AccountSystemRole, string>
        {
            [AccountSystemRole.DividendsPaid] = ClgCannotPayDividends,
            [AccountSystemRole.DividendsPayable] = ClgCannotPayDividends
        },
        CharityDepreciationAccount);

    private const string ClgCannotPayDividends = "A company limited by guarantee cannot pay dividends; remove the dividend entry.";

    private static ChartAccountDefaults.DefaultAccount CharityAccount(string code) =>
        CharityClg.Accounts.Single(account => account.Code == code).ToDefault();

    private static bool IsLeasedClass(string assetClass) => assetClass is LeasedEquipmentClass or "Finance Lease Equipment";

    // The register class the year-end fixed-asset form offers for equipment held under a finance lease.
    public const string LeasedEquipmentClass = "Leased Equipment";

    // Leased equipment is depreciated to 8010; every other class to the Depreciation role (8000).
    private static ChartAccountDefaults.DefaultAccount? CharityDepreciationAccount(string assetClass) =>
        IsLeasedClass(assetClass) ? CharityAccount("8010") : null;

    public const string CharityChartHasNoLandAccount =
        "The charity chart has no land and buildings account. Add one (a 00xx tangible fixed asset account) or choose another asset class.";

    // The register classes a chart has no fixed-asset account for: the charity chart has no land and
    // buildings account, so such an asset is refused rather than presented as another class.
    public static string? FixedAssetClassRefusal(ChartOfAccountsVariant variant, string assetClass) =>
        variant == ChartOfAccountsVariant.CharityClg && IsLandClass(assetClass) ? CharityChartHasNoLandAccount : null;

    private static bool IsLandClass(string assetClass) => assetClass is "Land & Buildings" or "Property";

    // The register's land classes, matched case-insensitively (adopting the charity chart is refused
    // while any asset is in one, since that chart has no land and buildings account).
    public static bool IsLandAssetClass(string? assetClass) =>
        assetClass?.Trim() is { } value
        && (value.Equals("Land & Buildings", StringComparison.OrdinalIgnoreCase) || value.Equals("Property", StringComparison.OrdinalIgnoreCase));

    // Register classes post to the cost accounts: leased equipment 0010, fixtures and fittings 0030,
    // every other class 0020 office and IT equipment. Land and buildings has no account (refused).
    private static ChartAccountDefaults.DefaultAccount CharityFixedAssetAccount(string assetClass) => CharityAccount(
        assetClass switch
        {
            _ when IsLandClass(assetClass) => throw new BusinessRuleException(CharityChartHasNoLandAccount),
            _ when IsLeasedClass(assetClass) => "0010",
            "Furniture" or "Fixtures & Fittings" or "Fixtures and Fittings" => "0030",
            _ => "0020"
        });

    private static ChartAccountDefaults.DefaultAccount CharityExpenseAccountForDescription(string lower) => CharityAccount(
        lower switch
        {
            var text when text.Contains("loan") => "8120",
            var text when text.Contains("lease") && text.Contains("interest") => "8110",
            var text when text.Contains("interest") => "8100",
            var text when text.Contains("bank charge") || text.Contains("bank fee") || text.Contains("account fee") => "7600",
            var text when text.Contains("account") || text.Contains("audit") => "7210",
            var text when text.Contains("legal") || text.Contains("solicitor") => "7200",
            var text when text.Contains("insurance") => "7000",
            var text when text.Contains("electric") || text.Contains("heat") || text.Contains("light") => "6300",
            var text when text.Contains("phone") || text.Contains("internet") => "7500",
            var text when text.Contains("hosting") || text.Contains("domain") => "6010",
            var text when text.Contains("software") || text.Contains("subscription") => "6000",
            var text when text.Contains("rent") => "6300",
            _ => "7700"
        });

    // Default statement line for a row a user adds to a charity chart, from the chart's code ranges.
    // Null when the range has no default (unassigned ranges, the 9xxx control range, a code that is not
    // four digits): the user must then supply the statement line.
    public static AccountStatementLine? CharityCustomLine(string code)
    {
        if (code.Length != 4 || !code.All(char.IsAsciiDigit))
            return null;
        var n = int.Parse(code, System.Globalization.CultureInfo.InvariantCulture);
        return n switch
        {
            <= 99 => AccountStatementLine.TangibleFixedAssets,
            >= 1000 and <= 1099 => AccountStatementLine.Stocks,
            >= 1100 and <= 1119 => AccountStatementLine.Debtors,
            >= 1120 and <= 1199 => AccountStatementLine.Prepayments,
            >= 1200 and <= 1299 => AccountStatementLine.CashAtBank,
            >= 2000 and <= 2099 => AccountStatementLine.TradeCreditors,
            >= 2100 and <= 2199 => AccountStatementLine.Accruals,
            >= 2250 and <= 2269 => AccountStatementLine.TaxCreditors,
            >= 2200 and <= 2299 => AccountStatementLine.OtherCreditorsWithinYear,
            >= 2300 and <= 2399 => AccountStatementLine.OtherCreditorsAfterYear,
            >= 3200 and <= 3299 => AccountStatementLine.RestrictedFunds,
            >= 3000 and <= 3999 => AccountStatementLine.Reserves,
            >= 4000 and <= 4999 => AccountStatementLine.Turnover,
            >= 5000 and <= 5999 => AccountStatementLine.CostOfSales,
            >= 6000 and <= 7999 => AccountStatementLine.AdministrativeExpenses,
            >= 8000 and <= 8099 => AccountStatementLine.Depreciation,
            >= 8100 and <= 8199 => AccountStatementLine.InterestPayable,
            >= 8200 and <= 8899 => AccountStatementLine.AdministrativeExpenses,
            >= 8900 and <= 8999 => AccountStatementLine.TaxOnProfit,
            _ => null
        };
    }

    // Default cash-flow class for a charity-chart row on a statement line.
    public static AccountCashFlowClass CharityDefaultCashFlow(AccountStatementLine line) => line switch
    {
        AccountStatementLine.TangibleFixedAssets => AccountCashFlowClass.Investing,
        AccountStatementLine.CashAtBank => AccountCashFlowClass.Cash,
        AccountStatementLine.LoansAfterYear or AccountStatementLine.OtherCreditorsAfterYear => AccountCashFlowClass.Loan,
        AccountStatementLine.TaxOnProfit => AccountCashFlowClass.Tax,
        AccountStatementLine.DividendsPaid => AccountCashFlowClass.Dividend,
        AccountStatementLine.ShareCapital or AccountStatementLine.Reserves or AccountStatementLine.RestrictedFunds
            or AccountStatementLine.OpeningBalanceControl => AccountCashFlowClass.OtherFinancing,
        _ => AccountCashFlowClass.Operating
    };
}
