using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Services;

public class FinancialStatementsService(AccountsDbContext db)
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
        bool Balances
    );

    public record FixedAssetsSection(List<AssetCategoryLine> Categories, decimal Total);
    public record AssetCategoryLine(string Category, decimal Cost, decimal Depreciation, decimal Nbv);

    public record CurrentAssetsSection(decimal Stock, decimal Debtors, decimal Prepayments, decimal Cash, decimal Total);
    public record CreditorsWithinYearSection(decimal TradeCreditors, decimal Accruals, decimal TaxCreditors, decimal OtherCreditors, decimal Total);
    public record CreditorsAfterYearSection(decimal Loans, decimal Other, decimal Total);
    public record CapitalSection(
        decimal ShareCapital,
        decimal OpeningRetainedEarnings,
        decimal ProfitForYear,
        decimal DividendsPaid,
        decimal RetainedEarnings,
        decimal Total,
        decimal UnexplainedDifference
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

    private sealed class AccountMovement
    {
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
    }

    private sealed class SourceAccumulator(AccountCategory category)
    {
        public AccountCategory Category { get; } = category;
        public decimal OpeningDebit { get; set; }
        public decimal OpeningCredit { get; set; }
        public decimal TransactionDebit { get; set; }
        public decimal TransactionCredit { get; set; }
        public int TransactionCount { get; set; }
        public decimal AdjustmentDebit { get; set; }
        public decimal AdjustmentCredit { get; set; }
        public int AdjustmentCount { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public List<string> SourceNotes { get; } = [];
    }

    private static bool BankOpeningApplies(BankAccount bank, DateOnly periodEnd) =>
        bank.OpeningBalance != 0
        && bank.OpeningBalanceDate is { } openingDate
        && openingDate <= periodEnd;

    private static bool LoanAppliesAtPeriodEnd(Loan loan, DateOnly periodStart, DateOnly periodEnd) =>
        loan.DrawdownDate is { } drawdownDate
        && loan.BalanceAsOfDate is { } balanceAsOfDate
        && drawdownDate <= periodEnd
        && balanceAsOfDate >= periodStart
        && balanceAsOfDate <= periodEnd;

    private static bool ShareCapitalAppliesAt(ShareCapital share, DateOnly date) =>
        share.IssueDate is { } issueDate
        && issueDate <= date
        && (share.CancelledDate is null || share.CancelledDate > date);

    private async Task<List<Loan>> GetLoansAtPeriodEndAsync(int companyId, DateOnly periodStart, DateOnly periodEnd) =>
        (await db.Loans
            .Where(l => l.CompanyId == companyId
                && l.DrawdownDate != null
                && l.BalanceAsOfDate != null
                && l.DrawdownDate <= periodEnd
                && l.BalanceAsOfDate >= periodStart
                && l.BalanceAsOfDate <= periodEnd)
            .ToListAsync())
            .Where(l => LoanAppliesAtPeriodEnd(l, periodStart, periodEnd))
            .ToList();

    private async Task<List<LoanBalanceSnapshot>> GetLoanSnapshotsForPeriodAsync(
        int companyId,
        int periodId,
        DateOnly periodStart,
        DateOnly periodEnd) =>
        (await db.LoanBalanceSnapshots
            .Include(s => s.Loan)
            .Where(s => s.PeriodId == periodId && s.Loan.CompanyId == companyId)
            .ToListAsync())
            .Where(s => LoanAppliesAtPeriodEnd(s.Loan, periodStart, periodEnd))
            .ToList();

    private async Task<decimal> GetShareCapitalAtAsync(int companyId, DateOnly date)
    {
        var shares = await db.ShareCapitals
            .Where(s => s.CompanyId == companyId
                && s.IssueDate != null
                && s.IssueDate <= date
                && (s.CancelledDate == null || s.CancelledDate > date))
            .ToListAsync();

        return shares.Where(s => ShareCapitalAppliesAt(s, date)).Sum(s => s.TotalValue);
    }

    private async Task AssertPeriodBelongsToCompanyAsync(int companyId, int periodId)
    {
        var exists = await db.AccountingPeriods
            .AsNoTracking()
            .AnyAsync(p => p.Id == periodId && p.CompanyId == companyId);
        if (!exists)
            throw new ResourceNotFoundException($"Period {periodId} not found");
    }

    public async Task<List<TrialBalanceLine>> GetTrialBalanceAsync(int companyId, int periodId)
    {
        await AssertPeriodBelongsToCompanyAsync(companyId, periodId);
        return await GetTrialBalanceForPeriodAsync(periodId);
    }

    private async Task<List<TrialBalanceLine>> GetTrialBalanceForPeriodAsync(int periodId)
    {
        var period = await db.AccountingPeriods.Include(p => p.Company).FirstAsync(p => p.Id == periodId);
        var companyId = period.CompanyId;

        var categories = await db.AccountCategories
            .Where(c => c.CompanyId == companyId || (c.IsSystem && c.CompanyId == null))
            .OrderBy(c => c.Code)
            .ToListAsync();
        var movements = await GetAccountMovementsAsync(periodId, categories);

        var lines = new List<TrialBalanceLine>();
        foreach (var cat in categories)
        {
            if (!movements.TryGetValue(cat.Id, out var movement)) continue;
            var netDebit = movement.Debit - movement.Credit;
            if (netDebit == 0) continue;
            var debit = netDebit > 0 ? netDebit : 0;
            var credit = netDebit < 0 ? Math.Abs(netDebit) : 0;

            lines.Add(new TrialBalanceLine(cat.Code, cat.Name, cat.Type.ToString(), debit, credit));
        }

        return lines;
    }

    public virtual async Task<ProfitAndLoss> GetProfitAndLossAsync(int companyId, int periodId)
    {
        await AssertPeriodBelongsToCompanyAsync(companyId, periodId);
        return await GetProfitAndLossForPeriodAsync(periodId);
    }

    // Income earned otherwise than from a trade (categories flagged IsNonTradingIncome),
    // measured with the same posting logic as the P&L, for the 25% corporation tax rate.
    public virtual async Task<decimal> GetNonTradingIncomeAsync(int companyId, int periodId)
    {
        await AssertPeriodBelongsToCompanyAsync(companyId, periodId);
        var categories = await db.AccountCategories
            .Where(c => c.CompanyId == companyId || (c.IsSystem && c.CompanyId == null))
            .ToListAsync();
        var movements = await GetAccountMovementsAsync(periodId, categories);
        return categories
            .Where(c => c.Type == AccountCategoryType.Income && c.IsNonTradingIncome)
            .Sum(c =>
            {
                var movement = movements.GetValueOrDefault(c.Id);
                return movement == null ? 0m : movement.Credit - movement.Debit;
            });
    }

    private async Task<ProfitAndLoss> GetProfitAndLossForPeriodAsync(int periodId)
    {
        var period = await db.AccountingPeriods.Include(p => p.Company).FirstAsync(p => p.Id == periodId);
        var companyId = period.CompanyId;

        var categories = await db.AccountCategories
            .Where(c => c.CompanyId == companyId || (c.IsSystem && c.CompanyId == null))
            .ToListAsync();
        var movements = await GetAccountMovementsAsync(periodId, categories);

        decimal IncomeAmount(AccountCategory category)
        {
            var movement = movements.GetValueOrDefault(category.Id);
            return movement == null ? 0 : movement.Credit - movement.Debit;
        }

        decimal ExpenseAmount(AccountCategory category)
        {
            var movement = movements.GetValueOrDefault(category.Id);
            return movement == null ? 0 : movement.Debit - movement.Credit;
        }

        decimal GetIncomeTotal(string codePrefix) =>
            categories.Where(c => c.Type == AccountCategoryType.Income && c.Code.StartsWith(codePrefix))
                .Sum(IncomeAmount);

        decimal GetExpenseTotal(string codePrefix) =>
            categories.Where(c => c.Type == AccountCategoryType.Expense && c.Code.StartsWith(codePrefix))
                .Sum(ExpenseAmount);

        var turnover = GetIncomeTotal("4");
        var costOfSales = GetExpenseTotal("5");
        var grossProfit = turnover - costOfSales;

        // Income earned outside turnover (non-4xxx income categories, e.g. rent or interest)
        // is reported as other income rather than netted into overheads or dropped, so it is
        // included in profit before tax (and taxed correctly, including the 25% non-trading rate).
        var otherIncome = categories
            .Where(c => c.Type == AccountCategoryType.Income && !c.Code.StartsWith("4"))
            .Sum(IncomeAmount);

        var overheads = categories
            .Where(c => c.Type == AccountCategoryType.Expense
                && ((c.Code.StartsWith("6") && !c.Code.StartsWith("69")) || c.Code.StartsWith("7")))
            .Select(c => new ExpenseLine(c.Code, c.Name, ExpenseAmount(c)))
            .Where(e => e.Amount != 0)
            .ToList();

        var totalOverheads = overheads.Sum(o => o.Amount);
        var operatingProfit = grossProfit + otherIncome - totalOverheads;

        var interestPayable = GetExpenseTotal("69"); // Bank charges & interest

        var unpostedAdjustments = await db.Adjustments
            .Where(a => a.PeriodId == periodId
                && a.DebitCategoryId == null
                && a.CreditCategoryId == null
                && a.ImpactOnProfit != 0)
            .OrderBy(a => a.IsAuto ? 0 : 1)
            .ThenBy(a => a.CreatedAt)
            .ToListAsync();
        var yearEndAdjustments = unpostedAdjustments
            .Where(a => !a.Description.Contains("corporation tax", StringComparison.OrdinalIgnoreCase))
            .Select(a => new AdjustmentLine(a.Description, a.ImpactOnProfit, a.ApprovedAt != null))
            .ToList();
        var totalYearEndAdjustments = yearEndAdjustments.Sum(a => a.Amount);

        var profitBeforeTax = operatingProfit - interestPayable + totalYearEndAdjustments;

        var corpTax = await db.TaxBalances
            .Where(t => t.PeriodId == periodId && t.TaxType == TaxType.CorporationTax)
            .Select(t => t.Liability)
            .FirstOrDefaultAsync();

        var profitAfterTax = profitBeforeTax - corpTax;

        return new ProfitAndLoss(turnover, costOfSales, grossProfit, otherIncome, overheads, totalOverheads,
            operatingProfit, interestPayable, profitBeforeTax, corpTax, profitAfterTax,
            yearEndAdjustments, totalYearEndAdjustments);
    }

    private async Task<Dictionary<int, AccountMovement>> GetAccountMovementsAsync(int periodId, List<AccountCategory> categories)
    {
        var movements = new Dictionary<int, AccountMovement>();
        var bankCategory = categories.FirstOrDefault(c => c.Code == "1400");
        var categoryById = categories.ToDictionary(c => c.Id);

        void Debit(int categoryId, decimal amount)
        {
            if (amount == 0) return;
            if (!movements.TryGetValue(categoryId, out var movement))
                movements[categoryId] = movement = new AccountMovement();
            movement.Debit += Math.Abs(amount);
        }

        void Credit(int categoryId, decimal amount)
        {
            if (amount == 0) return;
            if (!movements.TryGetValue(categoryId, out var movement))
                movements[categoryId] = movement = new AccountMovement();
            movement.Credit += Math.Abs(amount);
        }

        var transactions = await db.ImportedTransactions
            .Where(t => t.PeriodId == periodId && t.CategoryId != null && !t.IsDuplicate)
            .ToListAsync();

        foreach (var transaction in transactions)
        {
            if (!categoryById.ContainsKey(transaction.CategoryId!.Value)) continue;

            if (transaction.Amount >= 0)
            {
                if (bankCategory != null) Debit(bankCategory.Id, transaction.Amount);
                Credit(transaction.CategoryId.Value, transaction.Amount);
            }
            else
            {
                if (bankCategory != null) Credit(bankCategory.Id, transaction.Amount);
                Debit(transaction.CategoryId.Value, transaction.Amount);
            }
        }

        var adjustments = await db.Adjustments
            .Where(a => a.PeriodId == periodId)
            .ToListAsync();

        foreach (var adjustment in adjustments)
        {
            if (adjustment.DebitCategoryId.HasValue && categoryById.ContainsKey(adjustment.DebitCategoryId.Value))
                Debit(adjustment.DebitCategoryId.Value, adjustment.Amount);
            if (adjustment.CreditCategoryId.HasValue && categoryById.ContainsKey(adjustment.CreditCategoryId.Value))
                Credit(adjustment.CreditCategoryId.Value, adjustment.Amount);
        }

        var openingBalances = await db.OpeningBalances
            .Where(o => o.PeriodId == periodId)
            .ToListAsync();

        foreach (var opening in openingBalances)
        {
            if (!categoryById.ContainsKey(opening.AccountCategoryId)) continue;
            Debit(opening.AccountCategoryId, opening.Debit);
            Credit(opening.AccountCategoryId, opening.Credit);
        }

        if (bankCategory != null)
        {
            var period = await db.AccountingPeriods.FirstAsync(p => p.Id == periodId);
            var bankAccounts = await db.BankAccounts
                .Where(b => b.CompanyId == period.CompanyId
                    && b.OpeningBalance != 0
                    && b.OpeningBalanceDate != null
                    && b.OpeningBalanceDate <= period.PeriodEnd)
                .ToListAsync();
            foreach (var bank in bankAccounts)
            {
                if (!BankOpeningApplies(bank, period.PeriodEnd)) continue;
                if (bank.OpeningBalance > 0) Debit(bankCategory.Id, bank.OpeningBalance);
                if (bank.OpeningBalance < 0) Credit(bankCategory.Id, bank.OpeningBalance);
            }
        }

        return movements;
    }

    public async Task<List<StatementSourceSummary>> GetStatementSourcesAsync(int companyId, int periodId)
    {
        await AssertPeriodBelongsToCompanyAsync(companyId, periodId);
        return await GetStatementSourcesForPeriodAsync(periodId);
    }

    private async Task<List<StatementSourceSummary>> GetStatementSourcesForPeriodAsync(int periodId)
    {
        var period = await db.AccountingPeriods.Include(p => p.Company).FirstAsync(p => p.Id == periodId);
        var companyId = period.CompanyId;
        var categories = await db.AccountCategories
            .Where(c => c.CompanyId == companyId || (c.IsSystem && c.CompanyId == null))
            .OrderBy(c => c.Code)
            .ToListAsync();
        var categoryById = categories.ToDictionary(c => c.Id);
        var bankCategory = categories.FirstOrDefault(c => c.Code == "1400");

        var lines = categories.ToDictionary(c => c.Id, c => new SourceAccumulator(c));

        void AddDebit(int categoryId, decimal amount, string note)
        {
            if (amount == 0 || !lines.TryGetValue(categoryId, out var line)) return;
            line.Debit += Math.Abs(amount);
            line.SourceNotes.Add(note);
        }

        void AddCredit(int categoryId, decimal amount, string note)
        {
            if (amount == 0 || !lines.TryGetValue(categoryId, out var line)) return;
            line.Credit += Math.Abs(amount);
            line.SourceNotes.Add(note);
        }

        var openingBalances = await db.OpeningBalances
            .Include(o => o.AccountCategory)
            .Where(o => o.PeriodId == periodId)
            .ToListAsync();
        foreach (var opening in openingBalances)
        {
            if (!lines.TryGetValue(opening.AccountCategoryId, out var line)) continue;
            line.OpeningDebit += opening.Debit;
            line.OpeningCredit += opening.Credit;
            AddDebit(opening.AccountCategoryId, opening.Debit, $"Opening balance: {opening.SourceNote ?? "entered by reviewer"}");
            AddCredit(opening.AccountCategoryId, opening.Credit, $"Opening balance: {opening.SourceNote ?? "entered by reviewer"}");
        }

        if (bankCategory != null)
        {
            var banks = await db.BankAccounts
                .Where(b => b.CompanyId == companyId
                    && b.OpeningBalance != 0
                    && b.OpeningBalanceDate != null
                    && b.OpeningBalanceDate <= period.PeriodEnd)
                .ToListAsync();
            foreach (var bank in banks)
            {
                if (!BankOpeningApplies(bank, period.PeriodEnd)) continue;
                var note = $"Bank opening balance: {bank.Name}";
                if (bank.OpeningBalance > 0)
                {
                    lines[bankCategory.Id].OpeningDebit += bank.OpeningBalance;
                    AddDebit(bankCategory.Id, bank.OpeningBalance, note);
                }
                else
                {
                    lines[bankCategory.Id].OpeningCredit += Math.Abs(bank.OpeningBalance);
                    AddCredit(bankCategory.Id, bank.OpeningBalance, note);
                }
            }
        }

        var transactions = await db.ImportedTransactions
            .Where(t => t.PeriodId == periodId && t.CategoryId != null && !t.IsDuplicate)
            .ToListAsync();
        foreach (var transaction in transactions)
        {
            if (!categoryById.ContainsKey(transaction.CategoryId!.Value)) continue;
            var note = $"Imported transaction: {transaction.Description}";
            if (transaction.Amount >= 0)
            {
                if (bankCategory != null)
                {
                    lines[bankCategory.Id].TransactionDebit += transaction.Amount;
                    AddDebit(bankCategory.Id, transaction.Amount, note);
                }
                lines[transaction.CategoryId.Value].TransactionCredit += transaction.Amount;
                lines[transaction.CategoryId.Value].TransactionCount++;
                AddCredit(transaction.CategoryId.Value, transaction.Amount, note);
            }
            else
            {
                if (bankCategory != null)
                {
                    lines[bankCategory.Id].TransactionCredit += Math.Abs(transaction.Amount);
                    AddCredit(bankCategory.Id, transaction.Amount, note);
                }
                lines[transaction.CategoryId.Value].TransactionDebit += Math.Abs(transaction.Amount);
                lines[transaction.CategoryId.Value].TransactionCount++;
                AddDebit(transaction.CategoryId.Value, transaction.Amount, note);
            }
        }

        var adjustments = await db.Adjustments.Where(a => a.PeriodId == periodId).ToListAsync();
        foreach (var adjustment in adjustments)
        {
            if (adjustment.DebitCategoryId.HasValue && lines.ContainsKey(adjustment.DebitCategoryId.Value))
            {
                lines[adjustment.DebitCategoryId.Value].AdjustmentDebit += adjustment.Amount;
                lines[adjustment.DebitCategoryId.Value].AdjustmentCount++;
                AddDebit(adjustment.DebitCategoryId.Value, adjustment.Amount, $"Adjustment: {adjustment.Description}");
            }
            if (adjustment.CreditCategoryId.HasValue && lines.ContainsKey(adjustment.CreditCategoryId.Value))
            {
                lines[adjustment.CreditCategoryId.Value].AdjustmentCredit += adjustment.Amount;
                lines[adjustment.CreditCategoryId.Value].AdjustmentCount++;
                AddCredit(adjustment.CreditCategoryId.Value, adjustment.Amount, $"Adjustment: {adjustment.Description}");
            }
        }

        return lines.Values
            .Select(l =>
            {
                var netDebit = l.Debit - l.Credit;
                return new StatementSourceSummary(
                    l.Category.Code,
                    l.Category.Name,
                    l.Category.Type.ToString(),
                    l.OpeningDebit,
                    l.OpeningCredit,
                    l.TransactionDebit,
                    l.TransactionCredit,
                    l.TransactionCount,
                    l.AdjustmentDebit,
                    l.AdjustmentCredit,
                    l.AdjustmentCount,
                    netDebit > 0 ? netDebit : 0,
                    netDebit < 0 ? Math.Abs(netDebit) : 0,
                    l.SourceNotes.Distinct().Take(12).ToList()
                );
            })
            .Where(s => s.OpeningDebit != 0 || s.OpeningCredit != 0
                || s.TransactionDebit != 0 || s.TransactionCredit != 0
                || s.AdjustmentDebit != 0 || s.AdjustmentCredit != 0
                || s.ClosingDebit != 0 || s.ClosingCredit != 0)
            .OrderBy(s => s.Code)
            .ToList();
    }

    public async Task<BalanceSheet> GetBalanceSheetAsync(int companyId, int periodId)
    {
        await AssertPeriodBelongsToCompanyAsync(companyId, periodId);
        return await GetBalanceSheetForPeriodAsync(periodId);
    }

    private async Task<BalanceSheet> GetBalanceSheetForPeriodAsync(int periodId)
    {
        var period = await db.AccountingPeriods.Include(p => p.Company).FirstAsync(p => p.Id == periodId);
        var companyId = period.CompanyId;

        // Fixed Assets
        var periodsToDate = (await db.AccountingPeriods
            .Where(p => p.CompanyId == companyId && p.PeriodEnd <= period.PeriodEnd)
            .Select(p => p.Id)
            .ToListAsync())
            .ToHashSet();
        var assets = await db.FixedAssets
            .Include(a => a.DepreciationEntries)
            .Where(a => a.CompanyId == companyId
                && a.AcquisitionDate <= period.PeriodEnd
                && (a.DisposalDate == null || a.DisposalDate > period.PeriodEnd))
            .ToListAsync();

        var assetCategories = assets
            .GroupBy(a => a.Category)
            .Select(g =>
            {
                var cost = g.Sum(a => a.Cost);
                var depTotal = g.Sum(a =>
                {
                    var entry = a.DepreciationEntries.FirstOrDefault(d => d.PeriodId == periodId);
                    return entry != null
                        ? a.Cost - entry.ClosingNbv
                        : a.DepreciationEntries.Where(d => periodsToDate.Contains(d.PeriodId)).Sum(d => d.Charge);
                });
                return new AssetCategoryLine(g.Key, cost, depTotal, cost - depTotal);
            }).ToList();

        var fixedAssetsTotal = assetCategories.Sum(a => a.Nbv);

        // Current Assets
        var stock = await db.Inventories.Where(i => i.PeriodId == periodId).SumAsync(i => i.Value);
        var tradeDebtors = await db.Debtors.Where(d => d.PeriodId == periodId && d.Type == DebtorType.Trade).SumAsync(d => d.Amount);
        var prepayments = await db.Debtors.Where(d => d.PeriodId == periodId && d.Type == DebtorType.Prepayment).SumAsync(d => d.Amount);
        var otherDebtors = await db.Debtors.Where(d => d.PeriodId == periodId && d.Type == DebtorType.Other).SumAsync(d => d.Amount);
        var totalDebtors = tradeDebtors + otherDebtors;

        // Cash at bank -- sum bank account balances (simplified: opening + net transactions)
        var bankAccounts = await db.BankAccounts
            .Where(b => b.CompanyId == companyId)
            .ToListAsync();
        decimal cash = 0;
        foreach (var bank in bankAccounts)
        {
            var netTxns = await db.ImportedTransactions
                .Where(t => t.BankAccountId == bank.Id && t.PeriodId == periodId && !t.IsDuplicate)
                .SumAsync(t => t.Amount);
            cash += (BankOpeningApplies(bank, period.PeriodEnd) ? bank.OpeningBalance : 0) + netTxns;
        }

        var totalCurrentAssets = stock + totalDebtors + prepayments + cash;

        // Creditors within year
        var tradeCreditors = await db.Creditors.Where(c => c.PeriodId == periodId && c.Type == CreditorType.Trade && c.DueWithinYear).SumAsync(c => c.Amount);
        var accruals = await db.Creditors.Where(c => c.PeriodId == periodId && c.Type == CreditorType.Accrual && c.DueWithinYear).SumAsync(c => c.Amount);
        var taxCreditors = await db.Creditors.Where(c => c.PeriodId == periodId && c.Type == CreditorType.Tax && c.DueWithinYear).SumAsync(c => c.Amount);
        // Add tax balances
        var taxLiabilities = await db.TaxBalances.Where(t => t.PeriodId == periodId).SumAsync(t => t.Balance);
        taxCreditors += taxLiabilities;
        var otherCreditorsWithin = await db.Creditors.Where(c => c.PeriodId == periodId && c.Type == CreditorType.Other && c.DueWithinYear).SumAsync(c => c.Amount);
        // Add loan portions due within year
        var loanSnapshots = await GetLoanSnapshotsForPeriodAsync(companyId, periodId, period.PeriodStart, period.PeriodEnd);
        var snapshotLoanIds = loanSnapshots.Select(s => s.LoanId).ToHashSet();
        var loansAtPeriodEnd = (await GetLoansAtPeriodEndAsync(companyId, period.PeriodStart, period.PeriodEnd))
            .Where(l => !snapshotLoanIds.Contains(l.Id))
            .ToList();
        var loansDueWithin = loanSnapshots.Sum(s => s.DueWithinYear) + loansAtPeriodEnd.Sum(l => l.DueWithinYear);
        otherCreditorsWithin += loansDueWithin;
        var totalCreditorsWithin = tradeCreditors + accruals + taxCreditors + otherCreditorsWithin;

        var netCurrentAssets = totalCurrentAssets - totalCreditorsWithin;
        var totalAssetsLessCurrentLiabs = fixedAssetsTotal + netCurrentAssets;

        // Creditors after year
        var loansAfterYear = loanSnapshots.Sum(s => s.DueAfterYear) + loansAtPeriodEnd.Sum(l => l.DueAfterYear);
        var otherCreditorsAfter = await db.Creditors.Where(c => c.PeriodId == periodId && !c.DueWithinYear).SumAsync(c => c.Amount);
        var totalCreditorsAfter = loansAfterYear + otherCreditorsAfter;

        var netAssets = totalAssetsLessCurrentLiabs - totalCreditorsAfter;

        // Capital and reserves are computed independently from equity movements.
        // Any difference is surfaced instead of being hidden inside retained earnings.
        var closingShareCapital = await GetShareCapitalAtAsync(companyId, period.PeriodEnd);
        var openingShareCapital = await GetOpeningEquityBalanceAsync(periodId, "3000");
        var shareCapital = closingShareCapital != 0
            ? closingShareCapital
            : openingShareCapital != 0 ? openingShareCapital : 1m;
        var openingRetainedEarnings = await GetOpeningRetainedEarningsAsync(period);
        var profitForYear = (await GetProfitAndLossForPeriodAsync(periodId)).ProfitAfterTax;
        var dividendsPaid = await db.Dividends.Where(d => d.PeriodId == periodId).SumAsync(d => d.Amount);
        var retainedEarnings = openingRetainedEarnings + profitForYear - dividendsPaid;
        var totalCapital = shareCapital + retainedEarnings;
        var unexplainedDifference = netAssets - totalCapital;

        var balances = Math.Abs(unexplainedDifference) < 0.01m;

        return new BalanceSheet(
            new FixedAssetsSection(assetCategories, fixedAssetsTotal),
            new CurrentAssetsSection(stock, totalDebtors, prepayments, cash, totalCurrentAssets),
            new CreditorsWithinYearSection(tradeCreditors, accruals, taxCreditors, otherCreditorsWithin, totalCreditorsWithin),
            netCurrentAssets,
            totalAssetsLessCurrentLiabs,
            new CreditorsAfterYearSection(loansAfterYear, otherCreditorsAfter, totalCreditorsAfter),
            netAssets,
            new CapitalSection(shareCapital, openingRetainedEarnings, profitForYear, dividendsPaid, retainedEarnings, totalCapital, unexplainedDifference),
            balances
        );
    }

    private async Task<decimal> GetOpeningRetainedEarningsAsync(AccountingPeriod period)
    {
        var explicitOpening = await GetOpeningEquityBalanceAsync(period.Id, "3100");
        if (explicitOpening != 0)
            return explicitOpening;

        var priorPeriod = await db.AccountingPeriods
            .Where(p => p.CompanyId == period.CompanyId && p.PeriodEnd < period.PeriodStart)
            .OrderByDescending(p => p.PeriodEnd)
            .FirstOrDefaultAsync();

        if (priorPeriod == null)
            return 0m;

        var priorProfit = (await GetProfitAndLossForPeriodAsync(priorPeriod.Id)).ProfitAfterTax;
        var priorDividends = await db.Dividends
            .Where(d => d.PeriodId == priorPeriod.Id)
            .SumAsync(d => d.Amount);
        var earlierOpening = await GetOpeningRetainedEarningsAsync(priorPeriod);

        return earlierOpening + priorProfit - priorDividends;
    }

    private async Task<decimal> GetOpeningEquityBalanceAsync(int periodId, string code)
    {
        var balances = await db.OpeningBalances
            .Include(o => o.AccountCategory)
            .Where(o => o.PeriodId == periodId && o.AccountCategory.Code == code)
            .ToListAsync();

        return balances.Sum(o => o.Credit - o.Debit);
    }

    public async Task<ReadinessScore> GetReadinessScoreAsync(int companyId, int periodId)
    {
        await AssertPeriodBelongsToCompanyAsync(companyId, periodId);
        return await GetReadinessScoreForPeriodAsync(periodId);
    }

    private async Task<ReadinessScore> GetReadinessScoreForPeriodAsync(int periodId)
    {
        var period = await db.AccountingPeriods
            .Include(p => p.Company)
            .Include(p => p.SizeClassification)
            .Include(p => p.FilingRegime)
            .FirstAsync(p => p.Id == periodId);

        var companyId = period.CompanyId;
        var missing = new List<string>();
        var warnings = new List<string>();
        int completedChecks = 0;
        int totalChecks = 19;
        var reviewedSections = await db.YearEndReviewConfirmations
            .Where(r => r.PeriodId == periodId && r.Confirmed)
            .Select(r => r.SectionKey)
            .ToListAsync();
        var reviewed = reviewedSections.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 1. Size classification done?
        if (period.SizeClassification != null) completedChecks++; else missing.Add("Size classification not completed");

        // 2. Filing regime determined?
        if (period.FilingRegime != null) completedChecks++; else missing.Add("Filing regime not determined");

        // 3. Transactions imported?
        var txnCount = await db.ImportedTransactions.CountAsync(t => t.PeriodId == periodId && !t.IsDuplicate);
        if (txnCount > 0) completedChecks++; else missing.Add("No transactions imported");

        // 4. All transactions categorised?
        var uncategorised = await db.ImportedTransactions.CountAsync(t => t.PeriodId == periodId && t.CategoryId == null && !t.IsDuplicate);
        if (uncategorised == 0 && txnCount > 0) completedChecks++;
        else if (uncategorised > 0) warnings.Add($"{uncategorised} transactions not yet categorised");

        // 5. Debtors reviewed?
        var hasDebtors = await db.Debtors.AnyAsync(d => d.PeriodId == periodId);
        if (hasDebtors || reviewed.Contains("debtors")) completedChecks++;
        else missing.Add("Debtors and other receivables not reviewed");

        // 6. Creditors reviewed?
        var hasCreditors = await db.Creditors.AnyAsync(c => c.PeriodId == periodId);
        if (hasCreditors || reviewed.Contains("creditors")) completedChecks++;
        else missing.Add("Creditors, accruals and payables not reviewed");

        // 7. Inventory reviewed?
        var hasInventory = await db.Inventories.AnyAsync(i => i.PeriodId == periodId);
        if (!period.Company.HasStock || hasInventory || reviewed.Contains("inventory")) completedChecks++;
        else missing.Add("Stock / inventory not reviewed");

        // 8. Fixed assets up to date?
        if (period.Company.OwnsAssets)
        {
            var assetCount = await db.FixedAssets.CountAsync(a => a.CompanyId == companyId
                && a.AcquisitionDate <= period.PeriodEnd
                && (a.DisposalDate == null || a.DisposalDate > period.PeriodEnd));
            if (assetCount > 0) completedChecks++; else missing.Add("Company owns assets but none registered");
        }
        else completedChecks++;

        // 9. Loans and director loans reviewed?
        var hasLoanSnapshots = await db.LoanBalanceSnapshots.AnyAsync(s => s.PeriodId == periodId && s.Loan.CompanyId == companyId);
        var hasCurrentLoanRows = await db.Loans.AnyAsync(l => l.CompanyId == companyId
            && l.DrawdownDate != null
            && l.BalanceAsOfDate != null
            && l.DrawdownDate <= period.PeriodEnd
            && l.BalanceAsOfDate >= period.PeriodStart
            && l.BalanceAsOfDate <= period.PeriodEnd);
        var hasLoans = hasLoanSnapshots || hasCurrentLoanRows;
        var hasDirectorLoans = await db.DirectorLoans.AnyAsync(d =>
            d.PeriodId == periodId && d.Director.CompanyId == companyId);
        if ((!period.Company.HasBorrowings || hasLoans || reviewed.Contains("loans"))
            && (!period.Company.HasDirectorLoans || hasDirectorLoans || reviewed.Contains("director-loans")))
            completedChecks++;
        else
            missing.Add("Loans or director loans not reviewed");

        // 10. Payroll summary?
        if (period.Company.IsEmployer)
        {
            var hasPayroll = await db.PayrollSummaries.AnyAsync(p => p.PeriodId == periodId);
            if (hasPayroll) completedChecks++; else missing.Add("Payroll summary not entered (company is an employer)");
        }
        else if (reviewed.Contains("payroll")) completedChecks++;
        else missing.Add("Payroll and staff status not confirmed");

        // 11. Tax balances entered?
        var hasTax = await db.TaxBalances.AnyAsync(t => t.PeriodId == periodId);
        if (hasTax || reviewed.Contains("tax")) completedChecks++; else missing.Add("Tax balances not reviewed");

        // 12. Dividends reviewed?
        var hasDividends = await db.Dividends.AnyAsync(d => d.PeriodId == periodId);
        if (hasDividends || reviewed.Contains("dividends")) completedChecks++;
        else missing.Add("Dividends not reviewed");

        // 13. Other statutory disclosures reviewed?
        var hasPostBalanceSheetEvents = await db.PostBalanceSheetEvents.AnyAsync(e => e.PeriodId == periodId);
        var hasRelatedParties = await db.RelatedPartyTransactions.AnyAsync(r => r.PeriodId == periodId);
        var hasContingencies = await db.ContingentLiabilities.AnyAsync(c => c.PeriodId == periodId);
        if ((hasPostBalanceSheetEvents || reviewed.Contains("post-balance-sheet-events"))
            && (hasRelatedParties || reviewed.Contains("related-parties"))
            && (hasContingencies || reviewed.Contains("contingent-liabilities")))
            completedChecks++;
        else
            missing.Add("Post balance sheet events, related parties, or contingencies not reviewed");

        // 14. Going concern assessment completed?
        if (reviewed.Contains("going-concern") && (period.GoingConcernConfirmed || !string.IsNullOrWhiteSpace(period.GoingConcernNote)))
            completedChecks++;
        else
            missing.Add("Going concern assessment not completed");

        // 15. Notes generated and included?
        var hasIncludedNotes = await db.NotesDisclosures.AnyAsync(n => n.PeriodId == periodId && n.IsIncluded);
        if (hasIncludedNotes)
            completedChecks++;
        else
            missing.Add("Notes to the financial statements not generated or reviewed");

        // 16. Adjustments generated?
        var hasAdj = await db.Adjustments.AnyAsync(a => a.PeriodId == periodId);
        if (hasAdj) completedChecks++; else missing.Add("Year-end adjustments not generated");

        // 17. Adjustments approved?
        var unapproved = await db.Adjustments.CountAsync(a => a.PeriodId == periodId && a.ApprovedAt == null);
        if (unapproved == 0 && hasAdj) completedChecks++;
        else if (unapproved > 0) warnings.Add($"{unapproved} adjustments pending approval");

        // 18. Opening balances reviewed and balanced?
        var openingBalances = await db.OpeningBalances.Where(o => o.PeriodId == periodId).ToListAsync();
        var unreviewedOpeningBalances = openingBalances.Count(o => !o.Reviewed);
        var bankOpeningBalances = await db.BankAccounts
            .Where(b => b.CompanyId == companyId
                && b.OpeningBalance != 0
                && b.OpeningBalanceDate != null
                && b.OpeningBalanceDate <= period.PeriodEnd)
            .ToListAsync();
        bankOpeningBalances = bankOpeningBalances.Where(b => BankOpeningApplies(b, period.PeriodEnd)).ToList();
        var openingDebit = openingBalances.Sum(o => o.Debit) + bankOpeningBalances.Where(b => b.OpeningBalance > 0).Sum(b => b.OpeningBalance);
        var openingCredit = openingBalances.Sum(o => o.Credit) + bankOpeningBalances.Where(b => b.OpeningBalance < 0).Sum(b => Math.Abs(b.OpeningBalance));
        if (unreviewedOpeningBalances > 0)
            missing.Add($"{unreviewedOpeningBalances} opening balances not reviewed");
        else if (Math.Abs(openingDebit - openingCredit) > 0.01m)
            warnings.Add($"Opening balances do not agree. Difference: {(openingDebit - openingCredit):C}.");
        else
            completedChecks++;

        // 19. Balance sheet balances?
        try
        {
            var bs = await GetBalanceSheetForPeriodAsync(periodId);
            if (bs.Balances)
                completedChecks++;
            else
                warnings.Add($"Balance sheet does not balance. Unexplained difference: {bs.CapitalAndReserves.UnexplainedDifference:C}.");
        }
        catch { warnings.Add("Could not compute balance sheet"); }

        var completeness = (int)Math.Round((double)completedChecks / totalChecks * 100);

        // Filing readiness is completeness minus critical missing items
        var balanceSheetBalances = false;
        try
        {
            balanceSheetBalances = (await GetBalanceSheetForPeriodAsync(periodId)).Balances;
        }
        catch
        {
            balanceSheetBalances = false;
        }

        var filingReady = missing.Count == 0 && warnings.Count == 0 && balanceSheetBalances
            ? completeness
            : Math.Max(0, completeness - missing.Count * 10 - (balanceSheetBalances ? 0 : 20));

        return new ReadinessScore(completeness, filingReady, balanceSheetBalances, missing, warnings);
    }

    public async Task<List<string>> GetFinalOutputReadinessBlockersAsync(int companyId, int periodId)
    {
        await AssertPeriodBelongsToCompanyAsync(companyId, periodId);
        var readiness = await GetReadinessScoreForPeriodAsync(periodId);
        var blockers = new List<string>();

        if (!readiness.BalanceSheetBalances)
            blockers.Add("balance sheet does not balance");

        blockers.AddRange(readiness.MissingItems);
        blockers.AddRange(readiness.Warnings);

        return blockers.Distinct().Take(10).ToList();
    }

    public async Task AssertFinalOutputReadinessAsync(int companyId, int periodId, string outputName)
    {
        var blockers = await GetFinalOutputReadinessBlockersAsync(companyId, periodId);
        if (blockers.Count > 0)
            throw new BusinessRuleException(
                $"Cannot generate final {outputName} until readiness blockers are resolved: {string.Join("; ", blockers)}");
    }

    public async Task<CashFlowStatement> GetCashFlowStatementAsync(int companyId, int periodId)
    {
        await AssertPeriodBelongsToCompanyAsync(companyId, periodId);
        return await GetCashFlowStatementForPeriodAsync(periodId);
    }

    private async Task<CashFlowStatement> GetCashFlowStatementForPeriodAsync(int periodId)
    {
        var period = await db.AccountingPeriods.Include(p => p.Company).FirstAsync(p => p.Id == periodId);
        var companyId = period.CompanyId;

        // Operating profit from P&L
        var pl = await GetProfitAndLossForPeriodAsync(periodId);
        var operatingProfit = pl.OperatingProfit;

        var adjustments = new List<CashFlowAdjustment>();

        // Add back depreciation
        var depreciation = await db.DepreciationEntries
            .Where(d => d.PeriodId == periodId)
            .SumAsync(d => d.Charge);
        if (depreciation != 0)
            adjustments.Add(new CashFlowAdjustment("Depreciation", depreciation));

        // Working capital: change in debtors (increase = cash outflow, so negate)
        var debtors = await db.Debtors.Where(d => d.PeriodId == periodId).SumAsync(d => d.Amount);
        // Try to get prior period debtors
        var priorPeriod = await db.AccountingPeriods
            .Where(p => p.CompanyId == companyId && p.PeriodEnd < period.PeriodStart)
            .OrderByDescending(p => p.PeriodEnd)
            .FirstOrDefaultAsync();
        decimal priorDebtors = 0, priorCreditors = 0, priorStock = 0;
        if (priorPeriod != null)
        {
            priorDebtors = await db.Debtors.Where(d => d.PeriodId == priorPeriod.Id).SumAsync(d => d.Amount);
            priorCreditors = await db.Creditors.Where(c => c.PeriodId == priorPeriod.Id).SumAsync(c => c.Amount);
            priorStock = await db.Inventories.Where(i => i.PeriodId == priorPeriod.Id).SumAsync(i => i.Value);
        }

        var debtorChange = debtors - priorDebtors;
        if (debtorChange != 0)
            adjustments.Add(new CashFlowAdjustment("(Increase)/decrease in debtors", -debtorChange));

        // Working capital: change in creditors (increase = cash inflow)
        var creditors = await db.Creditors.Where(c => c.PeriodId == periodId).SumAsync(c => c.Amount);
        var creditorChange = creditors - priorCreditors;
        if (creditorChange != 0)
            adjustments.Add(new CashFlowAdjustment("Increase/(decrease) in creditors", creditorChange));

        // Working capital: change in stock (increase = cash outflow, so negate)
        var stock = await db.Inventories.Where(i => i.PeriodId == periodId).SumAsync(i => i.Value);
        var stockChange = stock - priorStock;
        if (stockChange != 0)
            adjustments.Add(new CashFlowAdjustment("(Increase)/decrease in stock", -stockChange));

        var totalAdjustments = adjustments.Sum(a => a.Amount);
        var cashFromOperations = operatingProfit + totalAdjustments;

        // Tax paid
        var taxPaid = await db.TaxBalances
            .Where(t => t.PeriodId == periodId && t.TaxType == TaxType.CorporationTax)
            .Select(t => t.Paid)
            .FirstOrDefaultAsync();
        var netCashFromOperating = cashFromOperations - taxPaid;

        // Investing: asset purchases in this period
        var capexPurchases = await db.FixedAssets
            .Where(a => a.CompanyId == companyId
                && a.AcquisitionDate >= period.PeriodStart
                && a.AcquisitionDate <= period.PeriodEnd)
            .SumAsync(a => a.Cost);

        // Investing: disposal proceeds in this period
        var capexDisposals = await db.FixedAssets
            .Where(a => a.CompanyId == companyId
                && a.DisposalDate != null
                && a.DisposalDate >= period.PeriodStart
                && a.DisposalDate <= period.PeriodEnd)
            .SumAsync(a => a.DisposalProceeds ?? 0);

        var netCashFromInvesting = capexDisposals - capexPurchases;

        // Financing: loans
        var loanSnapshots = await GetLoanSnapshotsForPeriodAsync(companyId, periodId, period.PeriodStart, period.PeriodEnd);
        var snapshotLoanIds = loanSnapshots.Select(s => s.LoanId).ToHashSet();
        var loanDrawdowns = loanSnapshots.Sum(s => s.Drawdowns) + await db.Loans
            .Where(l => l.CompanyId == companyId
                && !snapshotLoanIds.Contains(l.Id)
                && l.DrawdownDate != null
                && l.DrawdownDate >= period.PeriodStart
                && l.DrawdownDate <= period.PeriodEnd)
            .SumAsync(l => l.OriginalAmount);
        var loanRepayments = loanSnapshots.Sum(s => s.Repayments) + await db.Loans
            .Where(l => l.CompanyId == companyId
                && !snapshotLoanIds.Contains(l.Id)
                && l.DrawdownDate != null
                && l.DrawdownDate >= period.PeriodStart
                && l.DrawdownDate <= period.PeriodEnd
                && l.BalanceAsOfDate != null
                && l.BalanceAsOfDate >= period.PeriodStart
                && l.BalanceAsOfDate <= period.PeriodEnd)
            .SumAsync(l => l.OriginalAmount - l.Balance);

        // Financing: dividends paid
        var dividendsPaid = await db.Dividends
            .Where(d => d.PeriodId == periodId && d.DatePaid != null)
            .SumAsync(d => d.Amount);

        var netCashFromFinancing = loanDrawdowns - loanRepayments - dividendsPaid;

        var netIncreaseInCash = netCashFromOperating + netCashFromInvesting + netCashFromFinancing;

        // Opening cash = sum of bank opening balances
        var bankAccounts = await db.BankAccounts
            .Where(b => b.CompanyId == companyId
                && b.OpeningBalance != 0
                && b.OpeningBalanceDate != null
                && b.OpeningBalanceDate <= period.PeriodStart)
            .ToListAsync();
        var openingCash = bankAccounts.Where(b => BankOpeningApplies(b, period.PeriodStart)).Sum(b => b.OpeningBalance);
        var closingCash = openingCash + netIncreaseInCash;

        return new CashFlowStatement(
            operatingProfit, adjustments, cashFromOperations, taxPaid, netCashFromOperating,
            capexPurchases, capexDisposals, netCashFromInvesting,
            loanRepayments, loanDrawdowns, dividendsPaid, netCashFromFinancing,
            netIncreaseInCash, openingCash, closingCash
        );
    }

    public async Task<EquityChanges> GetEquityChangesAsync(int companyId, int periodId)
    {
        await AssertPeriodBelongsToCompanyAsync(companyId, periodId);
        return await GetEquityChangesForPeriodAsync(periodId);
    }

    private async Task<EquityChanges> GetEquityChangesForPeriodAsync(int periodId)
    {
        var period = await db.AccountingPeriods.Include(p => p.Company).FirstAsync(p => p.Id == periodId);
        var companyId = period.CompanyId;

        // Current share capital
        var closingShareCapital = await GetShareCapitalAtAsync(companyId, period.PeriodEnd);
        if (closingShareCapital == 0) closingShareCapital = 1m; // Default nominal

        // Prior period for opening balances
        var priorPeriod = await db.AccountingPeriods
            .Where(p => p.CompanyId == companyId && p.PeriodEnd < period.PeriodStart)
            .OrderByDescending(p => p.PeriodEnd)
            .FirstOrDefaultAsync();

        decimal openingShareCapital = 0;
        decimal openingRetainedEarnings = 0;
        if (priorPeriod != null)
        {
            openingShareCapital = await GetShareCapitalAtAsync(companyId, priorPeriod.PeriodEnd);
            if (openingShareCapital == 0) openingShareCapital = 1m;

            openingRetainedEarnings = await GetOpeningRetainedEarningsAsync(period);
        }
        else
        {
            // First year — opening share capital is the current capital (issued at incorporation)
            openingShareCapital = 0m;
        }

        var openingTotal = openingShareCapital + openingRetainedEarnings;

        // Profit for the year from P&L
        var pl = await GetProfitAndLossForPeriodAsync(periodId);
        var profitForYear = pl.ProfitAfterTax;

        // Dividends paid in period
        var dividendsPaid = await db.Dividends
            .Where(d => d.PeriodId == periodId)
            .SumAsync(d => d.Amount);

        // Shares issued = difference in share capital
        var sharesIssued = closingShareCapital - openingShareCapital;

        var closingRetainedEarnings = openingRetainedEarnings + profitForYear - dividendsPaid;
        var closingTotal = closingShareCapital + closingRetainedEarnings;

        return new EquityChanges(
            openingShareCapital, openingRetainedEarnings, openingTotal,
            profitForYear, dividendsPaid, sharesIssued,
            closingShareCapital, closingRetainedEarnings, closingTotal
        );
    }
}
