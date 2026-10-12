using System.Text.Json.Serialization;

namespace Accounts.Api.Services;

// Public statement contracts (DTOs) returned by FinancialStatementsService — separated from the
// computation engine. Nested in the same partial class, so references stay FinancialStatementsService.X.
public partial class FinancialStatementsService
{
    // Trial Balance line
    public record TrialBalanceLine(string Code, string Name, string Type, decimal Debit, decimal Credit);

    // P&L
    public record ProfitAndLoss(
        decimal Turnover,
        decimal CostOfSales,
        decimal GrossProfit,
        decimal OtherIncome,
        List<ExpenseLine> Overheads,
        decimal TotalOverheads,
        decimal OperatingProfit,
        decimal InterestPayable,
        decimal ProfitBeforeTax,
        decimal TaxCharge,
        decimal ProfitAfterTax,
        List<AdjustmentLine> YearEndAdjustments,
        decimal TotalYearEndAdjustments
    );
    public record ExpenseLine(string Code, string Name, decimal Amount);
    public record AdjustmentLine(string Description, decimal Amount, bool Approved);

    // Balance Sheet
    public record BalanceSheet(
        FixedAssetsSection FixedAssets,
        CurrentAssetsSection CurrentAssets,
        CreditorsWithinYearSection CreditorsWithinYear,
        decimal NetCurrentAssets,
        decimal TotalAssetsLessCurrentLiabilities,
        CreditorsAfterYearSection CreditorsAfterYear,
        decimal NetAssets,
        CapitalSection CapitalAndReserves,
        bool Balances,
        // Charity chart (ChartOfAccountsVariant.CharityClg) only: the members' funds presentation of
        // capital and reserves. Omitted (never written as null) for every other company, so generic
        // API responses and the CRO source fingerprint are unchanged.
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] MembersFundsSection? MembersFunds = null
    );

    public record FixedAssetsSection(List<AssetCategoryLine> Categories, decimal Total);
    public record AssetCategoryLine(string Category, decimal Cost, decimal Depreciation, decimal Nbv);

    public record CurrentAssetsSection(decimal Stock, decimal Debtors, decimal Prepayments, decimal Cash, decimal Total);
    // BankOverdraft is set only on the charity chart, where a credit balance on a cash account is a bank
    // overdraft within creditors (included in Total) rather than negative cash; omitted otherwise.
    public record CreditorsWithinYearSection(
        decimal TradeCreditors,
        decimal Accruals,
        decimal TaxCreditors,
        decimal OtherCreditors,
        decimal Total,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? BankOverdraft = null);
    public record CreditorsAfterYearSection(decimal Loans, decimal Other, decimal Total);
    public record CapitalSection(
        decimal ShareCapital,
        decimal OpeningRetainedEarnings,
        decimal ProfitForYear,
        decimal DividendsPaid,
        decimal OtherReserveMovements,
        decimal RetainedEarnings,
        decimal Total,
        decimal UnexplainedDifference
    );

    // Members' funds of a company limited by guarantee on the charity chart. Total equals
    // CapitalAndReserves.Total = FundsBroughtForward + SurplusForYear + OtherMovements + RestrictedFunds
    // (+ any share capital, which a company limited by guarantee should not have). FundsBroughtForward is
    // the opening unrestricted funds; OtherMovements are posted movements on unrestricted funds;
    // RestrictedFunds is the closing balance on the restricted-funds line.
    public record MembersFundsSection(
        decimal FundsBroughtForward,
        decimal SurplusForYear,
        decimal OtherMovements,
        decimal RestrictedFunds,
        decimal Total
    );

    // Cash Flow Statement
    public record CashFlowStatement(
        decimal OperatingProfit,
        List<CashFlowAdjustment> OperatingAdjustments,
        decimal CashFromOperations,
        decimal TaxPaid,
        decimal NetCashFromOperating,
        decimal CapitalExpenditurePurchases,
        decimal CapitalExpenditureDisposals,
        decimal NetCashFromInvesting,
        decimal LoanRepayments,
        decimal LoanDrawdowns,
        decimal DividendsPaid,
        decimal ShareIssues,
        decimal OtherFinancing,
        decimal NetCashFromFinancing,
        decimal NetIncreaseInCash,
        decimal OpeningCash,
        decimal ClosingCash
    );

    public record CashFlowAdjustment(string Description, decimal Amount);

    // Statement of Changes in Equity
    public record EquityChanges(
        decimal OpeningShareCapital,
        decimal OpeningRetainedEarnings,
        decimal OpeningTotal,
        decimal ProfitForYear,
        decimal DividendsPaid,
        decimal OtherReserveMovements,
        decimal SharesIssued,
        decimal ClosingShareCapital,
        decimal ClosingRetainedEarnings,
        decimal ClosingTotal
    );

    // Scoring
    public record ReadinessScore(
        int CompletenessPercent,
        int FilingReadinessPercent,
        bool BalanceSheetBalances,
        List<string> MissingItems,
        List<string> Warnings
    );
    public record StatementSourceSummary(
        string Code,
        string Name,
        string Type,
        decimal OpeningDebit,
        decimal OpeningCredit,
        decimal TransactionDebit,
        decimal TransactionCredit,
        int TransactionCount,
        decimal AdjustmentDebit,
        decimal AdjustmentCredit,
        int AdjustmentCount,
        decimal ClosingDebit,
        decimal ClosingCredit,
        List<string> SourceNotes
    );
}
