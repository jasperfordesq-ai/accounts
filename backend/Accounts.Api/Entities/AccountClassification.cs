namespace Accounts.Api.Entities;

// Explicit per-account presentation attributes. Stored as text like every other enum. A null value
// on an account means "not yet classified": CategoryClassification then derives today's code-based
// rules through LegacyCategoryClassification.Derive.
public enum AccountStatementLine
{
    // Balance sheet
    TangibleFixedAssets,
    Stocks,
    Debtors,
    Prepayments,
    CashAtBank,
    TradeCreditors,
    Accruals,
    TaxCreditors,
    OtherCreditorsWithinYear,
    LoansAfterYear,
    OtherCreditorsAfterYear,
    ShareCapital,
    Reserves,
    RestrictedFunds,
    DividendsPaid,
    Suspense,
    OpeningBalanceControl,

    // Profit and loss
    Turnover,
    OtherOperatingIncome,
    CostOfSales,
    AdministrativeExpenses,
    Depreciation,
    InterestPayable,
    TaxOnProfit
}

public enum AccountCashFlowClass
{
    Operating,
    Investing,
    Tax,
    Dividend,
    Loan,
    ShareIssue,
    OtherFinancing,
    Cash
}

public enum AccountSystemRole
{
    BankControl,
    RetainedEarnings,
    Depreciation,
    Prepayments,
    Stock,
    CostOfSalesStockMovement,
    Accruals,
    TradeDebtors,
    TradeDebtorIncome,
    TradeCreditors,
    DirectorLoanLiability,
    DirectorLoanReceivable,
    CorporationTaxCharge,
    CorporationTaxPayable,
    DividendsPaid,
    DividendsPayable,
    DisposalLoss,
    DisposalGain,
    VatPayable,
    VatReceivable,
    Suspense,
    OpeningBalanceControl
}

public enum ChartOfAccountsVariant
{
    GenericTrading,
    CharityClg
}
