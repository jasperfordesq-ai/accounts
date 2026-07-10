using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Services;

public partial class FinancialStatementsService(AccountsDbContext db)
{

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

    private static string FixedAssetCode(string assetCategory) => assetCategory switch
    {
        "Land & Buildings" or "Property" => "0010",
        "Plant & Machinery" => "0020",
        "Motor Vehicles" or "Vehicles" => "0030",
        "Office Equipment" or "Equipment" or "Furniture" => "0040",
        "Computer Equipment" or "IT" => "0050",
        _ => "0040"
    };

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
        var companyId = await db.AccountingPeriods
            .Where(p => p.Id == periodId)
            .Select(p => p.CompanyId)
            .FirstAsync();
        var ledger = await new AccountingLedgerService(db).BuildAsync(companyId, periodId);
        return ledger.Lines.Values
            .Where(line => line.NetDebit != 0)
            .OrderBy(line => line.Category.Code)
            .ThenBy(line => line.Category.Id)
            .Select(line => new TrialBalanceLine(
                line.Category.Code,
                line.Category.Name,
                line.Category.Type.ToString(),
                line.NetDebit > 0 ? line.NetDebit : 0m,
                line.NetDebit < 0 ? Math.Abs(line.NetDebit) : 0m))
            .ToList();
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
        var ledger = await new AccountingLedgerService(db).BuildAsync(companyId, periodId);
        return ledger.Lines.Values
            .Where(line => line.Category.Type == AccountCategoryType.Income && line.Category.IsNonTradingIncome)
            .Sum(line => line.Credit - line.Debit);
    }

    private async Task<ProfitAndLoss> GetProfitAndLossForPeriodAsync(int periodId)
    {
        var companyId = await db.AccountingPeriods
            .Where(p => p.Id == periodId)
            .Select(p => p.CompanyId)
            .FirstAsync();
        var ledger = await new AccountingLedgerService(db).BuildAsync(companyId, periodId);
        var lines = ledger.Lines.Values.ToList();

        static decimal IncomeAmount(AccountingLedgerService.LedgerLine line) => line.Credit - line.Debit;

        static decimal ExpenseAmount(AccountingLedgerService.LedgerLine line) => line.Debit - line.Credit;

        decimal GetExpenseTotal(string codePrefix) =>
            lines.Where(line => line.Category.Type == AccountCategoryType.Expense && line.Category.Code.StartsWith(codePrefix))
                .Sum(ExpenseAmount);

        // A 4xxx bookkeeping code is not, by itself, evidence that income arose from the trade.
        // Explicitly reviewed passive/non-trading income must stay out of turnover and be presented
        // as other income, while remaining in profit before tax and the 25% tax-rate support bucket.
        var turnover = lines
            .Where(line => line.Category.Type == AccountCategoryType.Income
                && line.Category.Code.StartsWith("4", StringComparison.Ordinal)
                && !line.Category.IsNonTradingIncome)
            .Sum(IncomeAmount);
        var costOfSales = GetExpenseTotal("5");
        var grossProfit = turnover - costOfSales;

        // Income earned outside turnover, including any category explicitly classified as
        // non-trading, is reported as other income rather than netted into overheads or dropped.
        var otherIncome = lines
            .Where(line => line.Category.Type == AccountCategoryType.Income
                && (line.Category.IsNonTradingIncome
                    || !line.Category.Code.StartsWith("4", StringComparison.Ordinal)))
            .Sum(IncomeAmount);

        var overheads = lines
            .Where(line => line.Category.Type == AccountCategoryType.Expense
                && !line.Category.Code.StartsWith("5")
                && !line.Category.Code.StartsWith("69")
                && !line.Category.Code.StartsWith("8"))
            .Select(line => new ExpenseLine(line.Category.Code, line.Category.Name, ExpenseAmount(line)))
            .Where(e => e.Amount != 0)
            .ToList();

        var totalOverheads = overheads.Sum(o => o.Amount);
        var operatingProfit = grossProfit + otherIncome - totalOverheads;

        var interestPayable = GetExpenseTotal("69"); // Bank charges & interest

        var postedAdjustments = await db.Adjustments
            .Where(a => a.PeriodId == periodId && a.ImpactOnProfit != 0)
            .OrderBy(a => a.IsAuto ? 0 : 1)
            .ThenBy(a => a.CreatedAt)
            .ToListAsync();
        var yearEndAdjustments = postedAdjustments
            .Where(a => !a.Description.Contains("corporation tax", StringComparison.OrdinalIgnoreCase))
            .Select(a => new AdjustmentLine(a.Description, a.ImpactOnProfit, a.ApprovedAt != null))
            .ToList();
        var totalYearEndAdjustments = yearEndAdjustments.Sum(a => a.Amount);

        // Posted journals are already in the ledger-derived income and expense totals; this list is
        // explanatory only and must never be added a second time.
        var profitBeforeTax = operatingProfit - interestPayable;
        var corpTax = GetExpenseTotal("8");

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
        var companyId = await db.AccountingPeriods
            .Where(p => p.Id == periodId)
            .Select(p => p.CompanyId)
            .FirstAsync();
        var ledger = await new AccountingLedgerService(db).BuildAsync(companyId, periodId);
        return ledger.Lines.Values
            .Where(line => line.Debit != 0 || line.Credit != 0)
            .OrderBy(line => line.Category.Code)
            .ThenBy(line => line.Category.Id)
            .Select(line => new StatementSourceSummary(
                line.Category.Code,
                line.Category.Name,
                line.Category.Type.ToString(),
                line.OpeningDebit,
                line.OpeningCredit,
                line.TransactionDebit,
                line.TransactionCredit,
                line.TransactionCount,
                line.AdjustmentDebit,
                line.AdjustmentCredit,
                line.AdjustmentCount,
                line.NetDebit > 0 ? line.NetDebit : 0m,
                line.NetDebit < 0 ? Math.Abs(line.NetDebit) : 0m,
                line.SourceNotes.Distinct().ToList()))
            .ToList();
    }

    private async Task<List<StatementSourceSummary>> GetLegacyStatementSourcesForPeriodAsync(int periodId)
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
        var companyId = await db.AccountingPeriods
            .Where(p => p.Id == periodId)
            .Select(p => p.CompanyId)
            .FirstAsync();
        var ledger = await new AccountingLedgerService(db).BuildAsync(companyId, periodId);
        var lines = ledger.Lines.Values.ToList();

        static bool FixedAsset(AccountCategory category) =>
            category.Type == AccountCategoryType.Asset && category.Code.StartsWith("00", StringComparison.Ordinal);
        static bool Cash(AccountCategory category) => AccountingLedgerService.IsCash(category);
        static decimal AssetBalance(AccountingLedgerService.LedgerLine line) => line.NetDebit;
        static decimal LiabilityBalance(AccountingLedgerService.LedgerLine line) => -line.NetDebit;

        var activeAssets = await db.FixedAssets
            .AsNoTracking()
            .Where(asset => asset.CompanyId == companyId
                && asset.AcquisitionDate <= ledger.Period.PeriodEnd
                && (asset.DisposalDate == null || asset.DisposalDate > ledger.Period.PeriodEnd))
            .ToListAsync();
        var fixedAssetLines = lines.Where(line => FixedAsset(line.Category)).ToList();
        var assetCategories = fixedAssetLines
            .Select(line =>
            {
                var netBookValue = AssetBalance(line);
                var registerCost = activeAssets
                    .Where(asset => FixedAssetCode(asset.Category) == line.Category.Code)
                    .Sum(asset => asset.Cost);
                var cost = registerCost == 0m ? Math.Max(0m, netBookValue) : registerCost;
                return new AssetCategoryLine(line.Category.Name, cost, cost - netBookValue, netBookValue);
            })
            .Where(line => line.Cost != 0 || line.Depreciation != 0 || line.Nbv != 0)
            .ToList();
        var fixedAssetsTotal = fixedAssetLines.Sum(AssetBalance);

        var currentAssetLines = lines
            .Where(line => line.Category.Type == AccountCategoryType.Asset && !FixedAsset(line.Category))
            .ToList();
        var stock = currentAssetLines.Where(line => line.Category.Code == "1000").Sum(AssetBalance);
        var prepayments = currentAssetLines.Where(line => line.Category.Code == "1200").Sum(AssetBalance);
        var cash = currentAssetLines.Where(line => Cash(line.Category)).Sum(AssetBalance);
        var debtors = currentAssetLines
            .Where(line => line.Category.Code != "1000" && line.Category.Code != "1200" && !Cash(line.Category))
            .Sum(AssetBalance);
        var totalCurrentAssets = currentAssetLines.Sum(AssetBalance);

        var liabilityLines = lines.Where(line => line.Category.Type == AccountCategoryType.Liability).ToList();
        var longTermLines = liabilityLines.Where(line =>
            line.Category.Code.StartsWith("27", StringComparison.Ordinal)
            || line.Category.Code.StartsWith("29", StringComparison.Ordinal)).ToList();
        var currentLiabilityLines = liabilityLines.Except(longTermLines).ToList();
        var tradeCreditors = currentLiabilityLines.Where(line => line.Category.Code == "2000").Sum(LiabilityBalance);
        var accruals = currentLiabilityLines.Where(line => line.Category.Code == "2100").Sum(LiabilityBalance);
        var taxCreditors = currentLiabilityLines
            .Where(line => line.Category.Code.StartsWith("22", StringComparison.Ordinal)
                || line.Category.Code.StartsWith("23", StringComparison.Ordinal)
                || line.Category.Code.StartsWith("24", StringComparison.Ordinal))
            .Sum(LiabilityBalance);
        var otherCreditorsWithin = currentLiabilityLines
            .Where(line => line.Category.Code != "2000"
                && line.Category.Code != "2100"
                && !line.Category.Code.StartsWith("22", StringComparison.Ordinal)
                && !line.Category.Code.StartsWith("23", StringComparison.Ordinal)
                && !line.Category.Code.StartsWith("24", StringComparison.Ordinal))
            .Sum(LiabilityBalance);
        var totalCreditorsWithin = currentLiabilityLines.Sum(LiabilityBalance);
        var loansAfterYear = longTermLines.Where(line => line.Category.Code == "2700").Sum(LiabilityBalance);
        var otherCreditorsAfter = longTermLines.Where(line => line.Category.Code != "2700").Sum(LiabilityBalance);
        var totalCreditorsAfter = longTermLines.Sum(LiabilityBalance);

        var netCurrentAssets = totalCurrentAssets - totalCreditorsWithin;
        var totalAssetsLessCurrentLiabilities = fixedAssetsTotal + netCurrentAssets;
        var netAssets = totalAssetsLessCurrentLiabilities - totalCreditorsAfter;

        var profitForYear = (await GetProfitAndLossForPeriodAsync(periodId)).ProfitAfterTax;
        var equityLines = lines.Where(line => line.Category.Type == AccountCategoryType.Equity).ToList();
        var shareCapital = -equityLines.Where(line => line.Category.Code == "3000").Sum(line => line.NetDebit);
        var dividendsPaid = equityLines.Where(line => line.Category.Code == "3200").Sum(line => line.CurrentNetDebit);
        var otherReserveMovements = -equityLines
            .Where(line => line.Category.Code is not "3000" and not "3200")
            .Sum(line => line.CurrentNetDebit);
        var equityExcludingShare = -equityLines.Where(line => line.Category.Code != "3000").Sum(line => line.NetDebit);
        var retainedEarnings = equityExcludingShare + profitForYear;
        var openingRetainedEarnings = retainedEarnings - profitForYear + dividendsPaid - otherReserveMovements;
        var totalCapital = shareCapital + retainedEarnings;
        var unexplainedDifference = netAssets - totalCapital;

        return new BalanceSheet(
            new FixedAssetsSection(assetCategories, fixedAssetsTotal),
            new CurrentAssetsSection(stock, debtors, prepayments, cash, totalCurrentAssets),
            new CreditorsWithinYearSection(tradeCreditors, accruals, taxCreditors, otherCreditorsWithin, totalCreditorsWithin),
            netCurrentAssets,
            totalAssetsLessCurrentLiabilities,
            new CreditorsAfterYearSection(loansAfterYear, otherCreditorsAfter, totalCreditorsAfter),
            netAssets,
            new CapitalSection(
                shareCapital,
                openingRetainedEarnings,
                profitForYear,
                dividendsPaid,
                otherReserveMovements,
                retainedEarnings,
                totalCapital,
                unexplainedDifference),
            Math.Abs(unexplainedDifference) < 0.01m);
    }

    private async Task<BalanceSheet> GetLegacyBalanceSheetForPeriodAsync(int periodId)
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

        // Cash at bank, on a true movement basis (accounting-multiyear-cash-movement-basis):
        // closing cash = bank opening balance + the CUMULATIVE net transaction movement across this
        // period AND every prior period (not just this period's transactions). Summing only the current
        // period dropped prior years' cash, so year-2+ balance sheets silently mis-balanced unless a
        // human re-keyed opening balances.
        var periodIdsToDate = periodsToDate.ToList();
        var bankAccounts = await db.BankAccounts
            .Where(b => b.CompanyId == companyId)
            .ToListAsync();
        decimal cash = 0;
        foreach (var bank in bankAccounts)
        {
            var netTxns = await db.ImportedTransactions
                .Where(t => t.BankAccountId == bank.Id && t.PeriodId != null && periodIdsToDate.Contains(t.PeriodId.Value) && !t.IsDuplicate)
                .SumAsync(t => t.Amount);
            cash += (BankOpeningApplies(bank, period.PeriodEnd) ? bank.OpeningBalance : 0) + netTxns;
        }

        var totalCurrentAssets = stock + totalDebtors + prepayments + cash;

        // Creditors within year
        var tradeCreditors = await db.Creditors.Where(c => c.PeriodId == periodId && c.Type == CreditorType.Trade && c.DueWithinYear).SumAsync(c => c.Amount);
        var accruals = await db.Creditors.Where(c => c.PeriodId == periodId && c.Type == CreditorType.Accrual && c.DueWithinYear).SumAsync(c => c.Amount);
        // accounting-tax-creditor-double-count [HUMAN DECISION flagged: single source of tax truth].
        // TaxBalances is the single source of tax owed — it drives the P&L tax charge and the CT
        // computation. Previously the same liability was summed twice: once from Creditors.Type==Tax
        // rows AND once from TaxBalances.Balance. Tax owed is taken from TaxBalances only; tax recorded
        // by a reviewer belongs in the year-end Tax section, not as a free-text creditor row.
        var taxCreditors = await db.TaxBalances.Where(t => t.PeriodId == periodId).SumAsync(t => t.Balance);
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
        // accounting-share-capital-and-dividends-reserves: report the actual issued share capital with
        // no €1 plug. A missing figure is surfaced as 0 (and as a readiness blocker / "not recorded"
        // note) rather than fabricated, so the unexplained difference stays honest.
        var closingShareCapital = await GetShareCapitalAtAsync(companyId, period.PeriodEnd);
        var openingShareCapital = await GetOpeningEquityBalanceAsync(periodId, "3000");
        var shareCapital = closingShareCapital != 0 ? closingShareCapital : openingShareCapital;
        var openingRetainedEarnings = await GetOpeningRetainedEarningsAsync(period);
        var profitForYear = (await GetProfitAndLossForPeriodAsync(periodId)).ProfitAfterTax;
        // Only dividends actually paid reduce reserves; a proposed (DatePaid == null) dividend must not.
        // This keeps reserves consistent with the financing cash-flow, which also counts paid dividends.
        var dividendsPaid = await db.Dividends.Where(d => d.PeriodId == periodId && d.DatePaid != null).SumAsync(d => d.Amount);
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
            new CapitalSection(shareCapital, openingRetainedEarnings, profitForYear, dividendsPaid, 0m, retainedEarnings, totalCapital, unexplainedDifference),
            balances
        );
    }

    private async Task<decimal> GetOpeningRetainedEarningsAsync(AccountingPeriod period)
    {
        var explicitOpening = await GetOpeningEquityBalanceAsync(period.Id, "3100");
        if (explicitOpening != 0)
            return explicitOpening;

        var priorPeriod = await PeriodChronologyService
            .PriorPeriodQuery(db, period.CompanyId, period.PeriodStart)
            .FirstOrDefaultAsync();

        if (priorPeriod == null)
            return 0m;

        // accounting-retained-earnings-snapshot: prefer the fixed closing-reserves figure captured when
        // the prior period was finalised, instead of recursively recomputing its (and every earlier
        // year's) P&L — which is O(n^2) and drifts if an earlier year is later edited.
        if (priorPeriod.ClosingRetainedEarnings is { } snapshot)
            return snapshot;

        var priorProfit = (await GetProfitAndLossForPeriodAsync(priorPeriod.Id)).ProfitAfterTax;
        // Only paid dividends reduce prior-year reserves carried forward (see reserves note above).
        var priorDividends = await db.Dividends
            .Where(d => d.PeriodId == priorPeriod.Id && d.DatePaid != null)
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

        // Candidate rows remain provisionally included. Final outputs fail closed until a reviewer
        // explicitly keeps or discards each one, so a re-import can never be silently lost.
        var pendingDuplicateReviews = await db.ImportedTransactions.CountAsync(t =>
            t.PeriodId == periodId
            && (t.DuplicateReviewStatus == DuplicateReviewStatus.Pending
                || t.DuplicateReviewStatus == DuplicateReviewStatus.LegacyLockedUnverified));
        if (pendingDuplicateReviews > 0)
            missing.Add($"{pendingDuplicateReviews} imported duplicate candidates require an explicit retain or discard decision");

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
            d.PeriodId == periodId && d.Period.CompanyId == companyId);
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

        // accounting-pl-tax-charge-unreconciled [HUMAN DECISION flagged]: the P&L tax charge reads the
        // entered CorporationTax liability and is never reconciled to the CT computation, so equity /
        // SOCIE / iXBRL / CT1 can silently disagree. When a CT figure has been entered, compare it to
        // TaxComputationService and warn (which blocks final outputs) if they diverge by more than €1.
        var enteredCorporationTax = await db.TaxBalances
            .Where(t => t.PeriodId == periodId && t.TaxType == TaxType.CorporationTax)
            .Select(t => (decimal?)t.Liability)
            .FirstOrDefaultAsync();
        if (enteredCorporationTax is { } enteredCt)
        {
            try
            {
                var taxSupport = await new TaxComputationService(db, this).ComputeAsync(companyId, periodId);
                if (!taxSupport.FinalTaxChargeSupported)
                    warnings.Add("Final corporation-tax charge is blocked pending manual scope review: " + string.Join("; ", taxSupport.BlockingReasons));
                else if (Math.Abs(enteredCt - taxSupport.TotalCorporationTax) > 1m)
                    warnings.Add(
                        $"Entered corporation tax ({enteredCt:C}) does not match the supported corporation tax computation ({taxSupport.TotalCorporationTax:C}). Reconcile the tax charge before filing.");
            }
            catch (Exception)
            {
                warnings.Add("Final corporation-tax charge is blocked because tax support data could not be validated.");
                // Computation unavailable (e.g. incomplete data) — the tax-balances checks already nudge.
            }
        }

        // accounting-vat-paye-reconciliation [HUMAN DECISION flagged — assumes VAT is posted to the VAT
        // control accounts (1300 VAT Receivable / 2200 VAT Payable); needs a real VAT-return spec to
        // confirm the convention]. When a VAT figure is entered, reconcile it to the source: net output
        // VAT (credit movement on VAT Payable) less input VAT (debit movement on VAT Receivable). Warn
        // (blocks final outputs) when they diverge, so an unreconciled VAT figure cannot be filed.
        var enteredVat = await db.TaxBalances
            .Where(t => t.PeriodId == periodId && t.TaxType == TaxType.Vat)
            .Select(t => (decimal?)t.Liability)
            .FirstOrDefaultAsync();
        if (enteredVat is { } vat)
        {
            var vatCategories = await db.AccountCategories
                .Where(c => c.CompanyId == companyId || (c.IsSystem && c.CompanyId == null))
                .ToListAsync();
            var vatMovements = await GetAccountMovementsAsync(periodId, vatCategories);
            decimal CategoryNetCredit(string code)
            {
                var cat = vatCategories.FirstOrDefault(c => c.Code == code);
                return cat != null && vatMovements.TryGetValue(cat.Id, out var m) ? m.Credit - m.Debit : 0m;
            }
            // Output VAT = net credit on VAT Payable; input VAT = net debit on VAT Receivable.
            var vatFromSource = CategoryNetCredit("2200") + CategoryNetCredit("1300");
            if (Math.Abs(vat - vatFromSource) > 1m)
                warnings.Add(
                    $"Entered VAT ({vat:C}) does not reconcile to the VAT control accounts ({vatFromSource:C}). Reconcile VAT to the underlying transactions before filing.");
        }

        // PAYE/PRSI reconciliation is FLAGGED, not implemented: PayrollSummary records only GrossWages,
        // EmployerPrsi, PensionContributions and StaffCount — there is no employee PAYE/PRSI withheld
        // field, so an entered PAYE TaxBalance cannot be reconciled precisely to payroll source data
        // without extending the payroll model. Tracked as accounting-paye-payroll-source-reconciliation.

        // 12. Dividends reviewed?
        var hasDividends = await db.Dividends.AnyAsync(d => d.PeriodId == periodId);
        if (hasDividends || reviewed.Contains("dividends")) completedChecks++;
        else missing.Add("Dividends not reviewed");

        // accounting-share-capital-and-dividends-reserves: a share-capital company must have its issued
        // share capital recorded (no €1 plug). Block final outputs when none is recorded — except a
        // company limited by guarantee, which legitimately has no share capital.
        if (period.Company.CompanyType != CompanyType.CompanyLimitedByGuarantee)
        {
            var recordedShareCapital = await GetShareCapitalAtAsync(companyId, period.PeriodEnd);
            if (recordedShareCapital == 0)
                recordedShareCapital = await GetOpeningEquityBalanceAsync(periodId, "3000");
            if (recordedShareCapital == 0)
                missing.Add("Share capital not recorded");
        }

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

        // 15. Statutory-note checklist complete? A single included row is not sufficient: every live
        // regime/fact code must exist exactly once, reconcile to its source, and carry retained review
        // evidence where the platform cannot substantiate the representation itself.
        var noteIssues = await new NotesDisclosureService(db).GetChecklistIssuesAsync(companyId, periodId);
        if (noteIssues.Count == 0)
            completedChecks++;
        else
            missing.AddRange(noteIssues);

        // 16. Adjustments generated?
        var hasAdj = await db.Adjustments.AnyAsync(a => a.PeriodId == periodId);
        var nilAdjustmentsReviewed = reviewed.Contains("adjustments");
        if (hasAdj || nilAdjustmentsReviewed) completedChecks++; else missing.Add("Year-end adjustments not generated or nil-adjustment review not confirmed");

        // 17. Adjustments approved?
        var unapproved = await db.Adjustments.CountAsync(a => a.PeriodId == periodId && a.ApprovedAt == null);
        if (unapproved == 0 && (hasAdj || nilAdjustmentsReviewed)) completedChecks++;
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

        // filing-auditor-report-blocks-final: a non-audit-exempt entity must have a signed auditor's
        // report attached before any final statutory output (PDF / iXBRL / CRO pack / signature page)
        // generates. Audit-exempt entities (most micro/small companies) are unaffected.
        var period = await db.AccountingPeriods
            .Include(p => p.FilingRegime)
            .Include(p => p.Company).ThenInclude(company => company.Officers)
            .Include(p => p.YearEndReviewConfirmations)
            .Include(p => p.Dividends)
            .AsNoTracking()
            .FirstAsync(p => p.Id == periodId);
        if (period.FilingRegime is { AuditExempt: false }
            && !FilingReleaseGate.HasCompleteAuditorReportEvidence(period))
            blockers.Add("a signed auditor's report has not been attached (the company is not audit-exempt)");

        var directorsReportRequired = period.FilingRegime is { ElectedRegime: not ElectedRegime.Micro };
        if (directorsReportRequired)
        {
            var reportingDirectors = period.Company.Officers
                .Where(officer => officer.Role == OfficerRole.Director
                    && DirectorsReportService.ServedDuring(officer, period.PeriodStart, period.PeriodEnd))
                .ToList();
            if (reportingDirectors.Count == 0)
                blockers.Add("the directors' report has no director with verified service during the reporting period");
            if (period.Company.Officers.Any(officer =>
                    (officer.Role is OfficerRole.Director or OfficerRole.Secretary or OfficerRole.CompanySecretary)
                    && officer.AppointedDate is null))
            {
                blockers.Add("director and secretary appointment dates must be recorded before the directors' report can be finalised");
            }

            var principalActivitiesReview = period.YearEndReviewConfirmations.FirstOrDefault(review =>
                DirectorsReportService.IsCompleteReview(
                    review,
                    DirectorsReportService.PrincipalActivitiesReviewKey));
            if (principalActivitiesReview is null)
                blockers.Add("the directors' principal-activities narrative has not been retained and reviewed");

            if (period.FilingRegime?.AuditExempt == false)
            {
                var auditInformationReview = period.YearEndReviewConfirmations.FirstOrDefault(review =>
                    DirectorsReportService.IsCompleteReview(
                        review,
                        DirectorsReportService.AuditInformationReviewKey));
                if (auditInformationReview is null)
                    blockers.Add("the directors have not retained explicit evidence for the relevant-audit-information statement");
            }

            if (period.Dividends.Any(dividend =>
                    dividend.Amount > 0
                    && dividend.DateDeclared is null
                    && dividend.DatePaid is null))
            {
                blockers.Add("each dividend requires a declaration date or payment date before directors' report finalisation");
            }
        }

        var directorLoanRowsExist = await db.DirectorLoans.AnyAsync(loan =>
            loan.PeriodId == periodId
            && loan.Period.CompanyId == companyId
            && (loan.CounterpartyType == DirectorLoanCounterpartyType.GroupCompany && loan.DirectorId == null
                || loan.DirectorId != null && loan.Director!.CompanyId == companyId));
        if (period.Company.HasDirectorLoans && !directorLoanRowsExist)
        {
            blockers.Add("the company is marked as having director arrangements but no director-loan compliance evidence is retained");
        }
        else if (directorLoanRowsExist)
        {
            var directorLoanCompliance = await new DirectorLoanComplianceService(db, this)
                .GetComplianceStatusAsync(companyId, periodId);
            blockers.AddRange(directorLoanCompliance.BlockingIssues.Select(issue => $"director-loan compliance: {issue}"));
        }

        return blockers.Distinct().ToList();
    }

    // validation-pre-filing-consistency-pass: one explicit internal-consistency pass over the primary
    // statements, returning specific issues (empty == consistent). Aggregates the cross-statement ties
    // that must hold before a set is filed: the balance sheet balances; reserves and share capital agree
    // between the balance sheet and the statement of changes in equity; and the entered corporation tax
    // reconciles to the CT computation. (The balance-sheet-balance and CT-tie checks already block final
    // outputs via readiness; the reserves/share-capital cross-ties are surfaced here explicitly.)
    public async Task<List<string>> GetPreFilingConsistencyIssuesAsync(int companyId, int periodId)
    {
        await AssertPeriodBelongsToCompanyAsync(companyId, periodId);
        var issues = new List<string>();

        var bs = await GetBalanceSheetForPeriodAsync(periodId);
        if (!bs.Balances)
            issues.Add($"Balance sheet does not balance. Unexplained difference: {bs.CapitalAndReserves.UnexplainedDifference:C}.");

        var equity = await GetEquityChangesForPeriodAsync(periodId);
        if (Math.Abs(bs.CapitalAndReserves.RetainedEarnings - equity.ClosingRetainedEarnings) > 0.01m)
            issues.Add($"Reserves disagree between the balance sheet ({bs.CapitalAndReserves.RetainedEarnings:C}) and the statement of changes in equity ({equity.ClosingRetainedEarnings:C}).");
        if (Math.Abs(bs.CapitalAndReserves.ShareCapital - equity.ClosingShareCapital) > 0.01m)
            issues.Add($"Share capital disagrees between the balance sheet ({bs.CapitalAndReserves.ShareCapital:C}) and the statement of changes in equity ({equity.ClosingShareCapital:C}).");

        var enteredCorporationTax = await db.TaxBalances
            .Where(t => t.PeriodId == periodId && t.TaxType == TaxType.CorporationTax)
            .Select(t => (decimal?)t.Liability)
            .FirstOrDefaultAsync();
        if (enteredCorporationTax is { } enteredCt)
        {
            try
            {
                var taxSupport = await new TaxComputationService(db, this).ComputeAsync(companyId, periodId);
                if (!taxSupport.FinalTaxChargeSupported)
                    issues.Add("Final corporation-tax charge is blocked: " + string.Join("; ", taxSupport.BlockingReasons));
                else if (Math.Abs(enteredCt - taxSupport.TotalCorporationTax) > 1m)
                    issues.Add($"Entered corporation tax ({enteredCt:C}) does not match the supported corporation tax computation ({taxSupport.TotalCorporationTax:C}).");
            }
            catch (Exception)
            {
                issues.Add("Final corporation-tax charge is blocked because tax support data could not be validated.");
                // Computation unavailable — other checks cover incomplete data.
            }
        }

        return issues;
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
        var companyId = await db.AccountingPeriods
            .Where(p => p.Id == periodId)
            .Select(p => p.CompanyId)
            .FirstAsync();
        var ledger = await new AccountingLedgerService(db).BuildAsync(companyId, periodId);
        var profitAndLoss = await GetProfitAndLossForPeriodAsync(periodId);
        var operatingProfit = profitAndLoss.OperatingProfit;

        static bool IsFixedAsset(AccountCategory category) =>
            category.Type == AccountCategoryType.Asset && category.Code.StartsWith("00", StringComparison.Ordinal);
        static bool IsTax(AccountCategory category) =>
            category.Code.StartsWith("24", StringComparison.Ordinal)
            || category.Code.StartsWith("8", StringComparison.Ordinal);
        static bool IsDividend(AccountCategory category) => category.Code is "2800" or "3200";
        static bool IsLoan(AccountCategory category) =>
            category.Code.StartsWith("26", StringComparison.Ordinal)
            || category.Code.StartsWith("27", StringComparison.Ordinal);
        static bool IsShareIssue(AccountCategory category) => category.Code == "3000";
        static bool IsOtherFinancing(AccountCategory category) =>
            category.Code.StartsWith("25", StringComparison.Ordinal)
            || (category.Type == AccountCategoryType.Equity && category.Code is not "3000" and not "3200");

        var investing = ledger.CashMovements.Where(movement => IsFixedAsset(movement.ContraCategory)).ToList();
        var tax = ledger.CashMovements.Where(movement => IsTax(movement.ContraCategory)).ToList();
        var dividends = ledger.CashMovements.Where(movement => IsDividend(movement.ContraCategory)).ToList();
        var loans = ledger.CashMovements.Where(movement => IsLoan(movement.ContraCategory)).ToList();
        var shareIssues = ledger.CashMovements.Where(movement => IsShareIssue(movement.ContraCategory)).ToList();
        var otherFinancing = ledger.CashMovements.Where(movement => IsOtherFinancing(movement.ContraCategory)).ToList();
        var excluded = investing.Concat(tax).Concat(dividends).Concat(loans).Concat(shareIssues).Concat(otherFinancing)
            .Select(movement => movement.SourceReference)
            .ToHashSet(StringComparer.Ordinal);
        var operating = ledger.CashMovements.Where(movement => !excluded.Contains(movement.SourceReference)).ToList();

        var operatingCashBeforeTax = operating.Sum(movement => movement.Amount);
        var interestCashMovement = operating
            .Where(movement => movement.ContraCategory.Code.StartsWith("69", StringComparison.Ordinal))
            .Sum(movement => movement.Amount);
        var depreciation = ledger.Lines.Values
            .Where(line => line.Category.Code == "7000")
            .Sum(line => line.CurrentNetDebit);
        var operatingAdjustments = new List<CashFlowAdjustment>();
        if (depreciation != 0)
            operatingAdjustments.Add(new CashFlowAdjustment("Depreciation", depreciation));
        if (interestCashMovement != 0)
            operatingAdjustments.Add(new CashFlowAdjustment("Interest paid", interestCashMovement));
        var remainingOperatingBridge = operatingCashBeforeTax - operatingProfit - depreciation - interestCashMovement;
        if (remainingOperatingBridge != 0)
            operatingAdjustments.Add(new CashFlowAdjustment("Non-cash items and working-capital movements", remainingOperatingBridge));

        var cashFromOperations = operatingProfit + operatingAdjustments.Sum(adjustment => adjustment.Amount);
        var taxPaid = -tax.Sum(movement => movement.Amount);
        var netCashFromOperating = cashFromOperations - taxPaid;
        var capitalExpenditurePurchases = -investing.Where(movement => movement.Amount < 0).Sum(movement => movement.Amount);
        var capitalExpenditureDisposals = investing.Where(movement => movement.Amount > 0).Sum(movement => movement.Amount);
        var netCashFromInvesting = capitalExpenditureDisposals - capitalExpenditurePurchases;
        var loanRepayments = -loans.Where(movement => movement.Amount < 0).Sum(movement => movement.Amount);
        var loanDrawdowns = loans.Where(movement => movement.Amount > 0).Sum(movement => movement.Amount);
        var dividendsPaid = -dividends.Sum(movement => movement.Amount);
        var shareIssueCash = shareIssues.Sum(movement => movement.Amount);
        var otherFinancingCash = otherFinancing.Sum(movement => movement.Amount);
        var netCashFromFinancing = loanDrawdowns - loanRepayments - dividendsPaid + shareIssueCash + otherFinancingCash;
        var netIncreaseInCash = netCashFromOperating + netCashFromInvesting + netCashFromFinancing;
        var openingCash = ledger.OpeningNetDebit(AccountingLedgerService.IsCash);
        var closingCash = ledger.NetDebit(AccountingLedgerService.IsCash);

        // All cash-flow sections are classifications of the same posted cash movements. Any future
        // category added to the ledger is therefore included exactly once and closing cash ties to the
        // balance sheet to the cent.
        if (Math.Abs((closingCash - openingCash) - netIncreaseInCash) >= 0.01m)
            throw new BusinessRuleException("Cash-flow classifications do not reconcile to the posted bank ledger.");

        return new CashFlowStatement(
            operatingProfit,
            operatingAdjustments,
            cashFromOperations,
            taxPaid,
            netCashFromOperating,
            capitalExpenditurePurchases,
            capitalExpenditureDisposals,
            netCashFromInvesting,
            loanRepayments,
            loanDrawdowns,
            dividendsPaid,
            shareIssueCash,
            otherFinancingCash,
            netCashFromFinancing,
            netIncreaseInCash,
            openingCash,
            closingCash);
    }

    private async Task<CashFlowStatement> GetLegacyCashFlowStatementForPeriodAsync(int periodId)
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
        var priorPeriod = await PeriodChronologyService
            .PriorPeriodQuery(db, companyId, period.PeriodStart)
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

        // Opening cash on the same movement basis as the balance sheet (accounting-cashflow-vs-bs-cash-
        // tie): the bank opening balances that apply at the period start PLUS the cumulative net movement
        // of every PRIOR period. Previously this used only the bank opening balances, so for year-2+ the
        // cash-flow's closing cash (opening + net increase) drifted from the balance-sheet cash by all
        // the prior years' movement. Now they tie for a cash-consistent set.
        var priorPeriodIds = await db.AccountingPeriods
            .Where(p => p.CompanyId == companyId && p.PeriodEnd < period.PeriodStart)
            .Select(p => p.Id)
            .ToListAsync();
        var allBankAccounts = await db.BankAccounts.Where(b => b.CompanyId == companyId).ToListAsync();
        decimal openingCash = 0;
        foreach (var bank in allBankAccounts)
        {
            var priorMovement = priorPeriodIds.Count == 0
                ? 0m
                : await db.ImportedTransactions
                    .Where(t => t.BankAccountId == bank.Id && t.PeriodId != null
                        && priorPeriodIds.Contains(t.PeriodId.Value) && !t.IsDuplicate)
                    .SumAsync(t => t.Amount);
            openingCash += (BankOpeningApplies(bank, period.PeriodStart) ? bank.OpeningBalance : 0) + priorMovement;
        }
        var closingCash = openingCash + netIncreaseInCash;

        return new CashFlowStatement(
            operatingProfit, adjustments, cashFromOperations, taxPaid, netCashFromOperating,
            capexPurchases, capexDisposals, netCashFromInvesting,
            loanRepayments, loanDrawdowns, dividendsPaid, 0m, 0m, netCashFromFinancing,
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
        var companyId = await db.AccountingPeriods
            .Where(p => p.Id == periodId)
            .Select(p => p.CompanyId)
            .FirstAsync();
        var ledger = await new AccountingLedgerService(db).BuildAsync(companyId, periodId);
        var equityLines = ledger.Lines.Values
            .Where(line => line.Category.Type == AccountCategoryType.Equity)
            .ToList();
        var shareLines = equityLines.Where(line => line.Category.Code == "3000").ToList();
        var dividendLines = equityLines.Where(line => line.Category.Code == "3200").ToList();
        var reserveLines = equityLines.Where(line => line.Category.Code is not "3000" and not "3200").ToList();

        var openingShareCapital = -shareLines.Sum(line => line.OpeningNetDebit);
        var sharesIssued = -shareLines.Sum(line => line.CurrentNetDebit);
        var closingShareCapital = openingShareCapital + sharesIssued;
        var openingRetainedEarnings = -reserveLines.Sum(line => line.OpeningNetDebit);
        var dividendsPaid = dividendLines.Sum(line => line.CurrentNetDebit);
        var otherReserveMovements = -reserveLines.Sum(line => line.CurrentNetDebit);
        var profitForYear = (await GetProfitAndLossForPeriodAsync(periodId)).ProfitAfterTax;
        var closingRetainedEarnings = openingRetainedEarnings
            + profitForYear
            - dividendsPaid
            + otherReserveMovements;

        return new EquityChanges(
            openingShareCapital,
            openingRetainedEarnings,
            openingShareCapital + openingRetainedEarnings,
            profitForYear,
            dividendsPaid,
            otherReserveMovements,
            sharesIssued,
            closingShareCapital,
            closingRetainedEarnings,
            closingShareCapital + closingRetainedEarnings);
    }

    private async Task<EquityChanges> GetLegacyEquityChangesForPeriodAsync(int periodId)
    {
        var period = await db.AccountingPeriods.Include(p => p.Company).FirstAsync(p => p.Id == periodId);
        var companyId = period.CompanyId;

        // Current share capital — actual issued value, no €1 plug
        // (accounting-share-capital-and-dividends-reserves).
        var closingShareCapital = await GetShareCapitalAtAsync(companyId, period.PeriodEnd);

        // Prior period for opening balances
        var priorPeriod = await PeriodChronologyService
            .PriorPeriodQuery(db, companyId, period.PeriodStart)
            .FirstOrDefaultAsync();

        decimal openingShareCapital = 0;
        decimal openingRetainedEarnings = 0;
        if (priorPeriod != null)
        {
            openingShareCapital = await GetShareCapitalAtAsync(companyId, priorPeriod.PeriodEnd);
            openingRetainedEarnings = await GetOpeningRetainedEarningsAsync(period);
        }
        else
        {
            // First year — capital subscribed at incorporation (issued on or before the period start)
            // is the opening balance, so it is not mis-stated as "issued during the year" and the
            // statement of changes in equity agrees with the balance sheet (BL-22).
            openingShareCapital = await GetShareCapitalAtAsync(companyId, period.PeriodStart);
        }

        var openingTotal = openingShareCapital + openingRetainedEarnings;

        // Profit for the year from P&L
        var pl = await GetProfitAndLossForPeriodAsync(periodId);
        var profitForYear = pl.ProfitAfterTax;

        // Dividends paid in period — only paid dividends move reserves (a proposed dividend does not).
        var dividendsPaid = await db.Dividends
            .Where(d => d.PeriodId == periodId && d.DatePaid != null)
            .SumAsync(d => d.Amount);

        // Shares issued = difference in share capital
        var sharesIssued = closingShareCapital - openingShareCapital;

        var closingRetainedEarnings = openingRetainedEarnings + profitForYear - dividendsPaid;
        var closingTotal = closingShareCapital + closingRetainedEarnings;

        return new EquityChanges(
            openingShareCapital, openingRetainedEarnings, openingTotal,
            profitForYear, dividendsPaid, 0m, sharesIssued,
            closingShareCapital, closingRetainedEarnings, closingTotal
        );
    }
}
