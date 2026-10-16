using Accounts.Api.Entities;

namespace Accounts.Api.Services;

// The accounts AdjustmentService creates when a posting needs one that the company's chart does not
// yet have, per chart variant (see ChartDefinitions). A variant's own role accounts and tables come
// first; a role the variant does not define falls back to the generic trading chart's definition. The
// engine finds these accounts by system role, never by code.
public static class ChartAccountDefaults
{
    // StatementLine / CashFlowClass are set for an explicit (non-generic) chart's account; null means
    // "classify from the code" exactly as the generic chart always has.
    public readonly record struct DefaultAccount(
        string Code,
        string Name,
        AccountCategoryType Type,
        TaxTreatment TaxTreatment,
        AccountStatementLine? StatementLine = null,
        AccountCashFlowClass? CashFlowClass = null);

    // The generic chart keeps today's behaviour: every adjustment role is resolved (and created if
    // missing) on each regeneration, in the same order, so its auto-created rows are unchanged. Other
    // variants create a role's account only when a posting actually needs it.
    public static bool EnsuresAdjustmentRolesUpFront(ChartOfAccountsVariant variant) =>
        variant == ChartOfAccountsVariant.GenericTrading;

    public static DefaultAccount RoleAccount(ChartOfAccountsVariant variant, AccountSystemRole role)
    {
        if (variant == ChartOfAccountsVariant.GenericTrading)
            return GenericRoleAccount(role);
        var chart = ChartDefinitions.For(variant);
        if (chart.RefusedRoles.TryGetValue(role, out var reason))
            throw new BusinessRuleException(reason);
        return chart.RoleAccount(role)?.ToDefault() ?? GenericRoleAccount(role);
    }

    private static DefaultAccount GenericRoleAccount(AccountSystemRole role) => role switch
    {
        AccountSystemRole.Depreciation => new("7000", "Depreciation", AccountCategoryType.Expense, TaxTreatment.NonDeductible),
        AccountSystemRole.Prepayments => new("1200", "Prepayments", AccountCategoryType.Asset, TaxTreatment.Other),
        AccountSystemRole.Stock => new("1000", "Stock / Inventory", AccountCategoryType.Asset, TaxTreatment.Other),
        AccountSystemRole.CostOfSalesStockMovement => new("5000", "Cost of Sales", AccountCategoryType.Expense, TaxTreatment.Deductible),
        AccountSystemRole.Accruals => new("2100", "Accruals", AccountCategoryType.Liability, TaxTreatment.Other),
        AccountSystemRole.CorporationTaxCharge => new("8000", "Corporation Tax Charge", AccountCategoryType.Expense, TaxTreatment.NonDeductible),
        AccountSystemRole.CorporationTaxPayable => new("2400", "Corporation Tax Payable", AccountCategoryType.Liability, TaxTreatment.Other),
        AccountSystemRole.DividendsPaid => new("3200", "Dividends Paid", AccountCategoryType.Equity, TaxTreatment.Other),
        AccountSystemRole.DividendsPayable => new("2800", "Dividends Payable", AccountCategoryType.Liability, TaxTreatment.Other),
        AccountSystemRole.BankControl => new("1400", "Bank Current Account", AccountCategoryType.Asset, TaxTreatment.Other),
        AccountSystemRole.DisposalLoss => new("7050", "Loss on Disposal of Fixed Assets", AccountCategoryType.Expense, TaxTreatment.NonDeductible),
        AccountSystemRole.DisposalGain => new("9000", "Gain on Disposal of Fixed Assets", AccountCategoryType.Income, TaxTreatment.Other),
        AccountSystemRole.TradeDebtors => new("1100", "Trade Debtors", AccountCategoryType.Asset, TaxTreatment.Other),
        AccountSystemRole.TradeDebtorIncome => new("4000", "Sales / Revenue", AccountCategoryType.Income, TaxTreatment.Deductible),
        AccountSystemRole.TradeCreditors => new("2000", "Trade Creditors", AccountCategoryType.Liability, TaxTreatment.Other),
        AccountSystemRole.DirectorLoanLiability => new("2500", "Director Loan Account", AccountCategoryType.Liability, TaxTreatment.Other),
        AccountSystemRole.DirectorLoanReceivable => new("1150", "Director Loan Account (debit)", AccountCategoryType.Asset, TaxTreatment.Other),
        _ => throw new BusinessRuleException($"No default account is defined for the {role} system role.")
    };

    // Fixed-asset register class -> the asset account its cost, depreciation and disposals post to.
    public static DefaultAccount FixedAssetAccount(ChartOfAccountsVariant variant, string assetClass) =>
        ChartDefinitions.For(variant).FixedAssetAccount(assetClass);

    // Fixed-asset register class -> a class-specific depreciation expense account, or null when the
    // class is depreciated to the chart's Depreciation role account (always null on the generic chart).
    public static DefaultAccount? DepreciationAccount(ChartOfAccountsVariant variant, string assetClass) =>
        ChartDefinitions.For(variant).DepreciationAccount(assetClass);

    public static DefaultAccount? AccumulatedDepreciationAccount(ChartOfAccountsVariant variant, string assetClass) =>
        ChartDefinitions.For(variant).AccumulatedDepreciationAccount(assetClass);

    // Accrual / prepayment / trade-creditor description -> the expense account it adjusts.
    public static DefaultAccount ExpenseAccountForDescription(ChartOfAccountsVariant variant, string description) =>
        ChartDefinitions.For(variant).ExpenseAccountForDescription(description);
}
