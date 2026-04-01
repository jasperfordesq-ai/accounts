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
        List<ExpenseLine> Overheads,
        decimal TotalOverheads,
        decimal OperatingProfit,
        decimal InterestPayable,
        decimal ProfitBeforeTax,
        decimal TaxCharge,
        decimal ProfitAfterTax
    );
    public record ExpenseLine(string Code, string Name, decimal Amount);

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
    public record CapitalSection(decimal ShareCapital, decimal RetainedEarnings, decimal Total);

    // Scoring
    public record ReadinessScore(
        int CompletenessPercent,
        int FilingReadinessPercent,
        bool BalanceSheetBalances,
        List<string> MissingItems,
        List<string> Warnings
    );

    public async Task<List<TrialBalanceLine>> GetTrialBalanceAsync(int periodId)
    {
        var period = await db.AccountingPeriods.Include(p => p.Company).FirstAsync(p => p.Id == periodId);
        var companyId = period.CompanyId;

        // Get categorised transaction totals
        var txnTotals = await db.ImportedTransactions
            .Where(t => t.PeriodId == periodId && t.CategoryId != null && !t.IsDuplicate)
            .GroupBy(t => t.CategoryId)
            .Select(g => new { CategoryId = g.Key!.Value, Total = g.Sum(t => t.Amount) })
            .ToListAsync();

        // Get adjustment impacts by category
        var debitAdjs = await db.Adjustments
            .Where(a => a.PeriodId == periodId && a.DebitCategoryId != null)
            .GroupBy(a => a.DebitCategoryId)
            .Select(g => new { CategoryId = g.Key!.Value, Total = g.Sum(a => a.Amount) })
            .ToListAsync();

        var creditAdjs = await db.Adjustments
            .Where(a => a.PeriodId == periodId && a.CreditCategoryId != null)
            .GroupBy(a => a.CreditCategoryId)
            .Select(g => new { CategoryId = g.Key!.Value, Total = g.Sum(a => a.Amount) })
            .ToListAsync();

        var categories = await db.AccountCategories
            .Where(c => c.CompanyId == companyId || (c.IsSystem && c.CompanyId == null))
            .OrderBy(c => c.Code)
            .ToListAsync();

        var lines = new List<TrialBalanceLine>();
        foreach (var cat in categories)
        {
            var txnTotal = txnTotals.FirstOrDefault(t => t.CategoryId == cat.Id)?.Total ?? 0;
            var debitAdj = debitAdjs.FirstOrDefault(d => d.CategoryId == cat.Id)?.Total ?? 0;
            var creditAdj = creditAdjs.FirstOrDefault(c => c.CategoryId == cat.Id)?.Total ?? 0;

            var netAmount = txnTotal + debitAdj - creditAdj;
            if (netAmount == 0) continue;

            decimal debit = 0, credit = 0;
            // Income and Liabilities are credit balances; Assets and Expenses are debit
            if (cat.Type == AccountCategoryType.Income || cat.Type == AccountCategoryType.Liability || cat.Type == AccountCategoryType.Equity)
            {
                if (netAmount < 0) debit = Math.Abs(netAmount); else credit = netAmount;
            }
            else
            {
                if (netAmount > 0) debit = netAmount; else credit = Math.Abs(netAmount);
            }

            lines.Add(new TrialBalanceLine(cat.Code, cat.Name, cat.Type.ToString(), debit, credit));
        }

        return lines;
    }

    public async Task<ProfitAndLoss> GetProfitAndLossAsync(int periodId)
    {
        var period = await db.AccountingPeriods.Include(p => p.Company).FirstAsync(p => p.Id == periodId);
        var companyId = period.CompanyId;

        var categories = await db.AccountCategories
            .Where(c => c.CompanyId == companyId || (c.IsSystem && c.CompanyId == null))
            .ToListAsync();

        var txnTotals = await db.ImportedTransactions
            .Where(t => t.PeriodId == periodId && t.CategoryId != null && !t.IsDuplicate)
            .GroupBy(t => t.CategoryId)
            .Select(g => new { CategoryId = g.Key!.Value, Total = g.Sum(t => t.Amount) })
            .ToDictionaryAsync(g => g.CategoryId, g => g.Total);

        decimal GetCategoryTotal(string codePrefix) =>
            categories.Where(c => c.Code.StartsWith(codePrefix))
                .Sum(c => Math.Abs(txnTotals.GetValueOrDefault(c.Id, 0)));

        var turnover = GetCategoryTotal("4");
        var costOfSales = GetCategoryTotal("5");
        var grossProfit = turnover - costOfSales;

        var overheads = categories
            .Where(c => c.Code.StartsWith("6") || c.Code.StartsWith("7"))
            .Select(c => new ExpenseLine(c.Code, c.Name, Math.Abs(txnTotals.GetValueOrDefault(c.Id, 0))))
            .Where(e => e.Amount != 0)
            .ToList();

        var totalOverheads = overheads.Sum(o => o.Amount);
        var operatingProfit = grossProfit - totalOverheads;

        var interestPayable = GetCategoryTotal("69"); // Bank charges & interest
        var profitBeforeTax = operatingProfit - interestPayable;

        var corpTax = await db.TaxBalances
            .Where(t => t.PeriodId == periodId && t.TaxType == TaxType.CorporationTax)
            .Select(t => t.Liability)
            .FirstOrDefaultAsync();

        var profitAfterTax = profitBeforeTax - corpTax;

        return new ProfitAndLoss(turnover, costOfSales, grossProfit, overheads, totalOverheads,
            operatingProfit, interestPayable, profitBeforeTax, corpTax, profitAfterTax);
    }

    public async Task<BalanceSheet> GetBalanceSheetAsync(int periodId)
    {
        var period = await db.AccountingPeriods.Include(p => p.Company).FirstAsync(p => p.Id == periodId);
        var companyId = period.CompanyId;

        // Fixed Assets
        var assets = await db.FixedAssets
            .Include(a => a.DepreciationEntries)
            .Where(a => a.CompanyId == companyId && a.DisposalDate == null)
            .ToListAsync();

        var assetCategories = assets
            .GroupBy(a => a.Category)
            .Select(g =>
            {
                var cost = g.Sum(a => a.Cost);
                var depTotal = g.Sum(a =>
                {
                    var entry = a.DepreciationEntries.FirstOrDefault(d => d.PeriodId == periodId);
                    return entry != null ? cost - entry.ClosingNbv : a.DepreciationEntries.Sum(d => d.Charge);
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
        var bankAccounts = await db.BankAccounts.Where(b => b.CompanyId == companyId).ToListAsync();
        decimal cash = 0;
        foreach (var bank in bankAccounts)
        {
            var netTxns = await db.ImportedTransactions
                .Where(t => t.BankAccountId == bank.Id && t.PeriodId == periodId && !t.IsDuplicate)
                .SumAsync(t => t.Amount);
            cash += bank.OpeningBalance + netTxns;
        }

        var totalCurrentAssets = stock + totalDebtors + prepayments + cash;

        // Creditors within year
        var tradeCreditors = await db.Creditors.Where(c => c.PeriodId == periodId && c.Type == CreditorType.Trade && c.DueWithinYear).SumAsync(c => c.Amount);
        var accruals = await db.Creditors.Where(c => c.PeriodId == periodId && c.Type == CreditorType.Accrual).SumAsync(c => c.Amount);
        var taxCreditors = await db.Creditors.Where(c => c.PeriodId == periodId && c.Type == CreditorType.Tax).SumAsync(c => c.Amount);
        // Add tax balances
        var taxLiabilities = await db.TaxBalances.Where(t => t.PeriodId == periodId).SumAsync(t => t.Balance);
        taxCreditors += taxLiabilities;
        var otherCreditorsWithin = await db.Creditors.Where(c => c.PeriodId == periodId && c.Type == CreditorType.Other && c.DueWithinYear).SumAsync(c => c.Amount);
        // Add loan portions due within year
        var loansDueWithin = await db.Loans.Where(l => l.CompanyId == companyId).SumAsync(l => l.DueWithinYear);
        otherCreditorsWithin += loansDueWithin;
        var totalCreditorsWithin = tradeCreditors + accruals + taxCreditors + otherCreditorsWithin;

        var netCurrentAssets = totalCurrentAssets - totalCreditorsWithin;
        var totalAssetsLessCurrentLiabs = fixedAssetsTotal + netCurrentAssets;

        // Creditors after year
        var loansAfterYear = await db.Loans.Where(l => l.CompanyId == companyId).SumAsync(l => l.DueAfterYear);
        var otherCreditorsAfter = await db.Creditors.Where(c => c.PeriodId == periodId && !c.DueWithinYear).SumAsync(c => c.Amount);
        var totalCreditorsAfter = loansAfterYear + otherCreditorsAfter;

        var netAssets = totalAssetsLessCurrentLiabs - totalCreditorsAfter;

        // Capital and Reserves
        // Share capital - placeholder (from equity categories or manual entry)
        // For now, derive retained earnings as the balancing figure
        var shareCapitals = await db.ShareCapitals.Where(s => s.CompanyId == companyId).ToListAsync();
        var shareCapital = shareCapitals.Count > 0 ? shareCapitals.Sum(s => s.TotalValue) : 1m;
        var dividendsPaid = await db.Dividends.Where(d => d.PeriodId == periodId).SumAsync(d => d.Amount);
        var retainedEarnings = netAssets - shareCapital;
        var totalCapital = shareCapital + retainedEarnings;

        var balances = Math.Abs(netAssets - totalCapital) < 0.01m;

        return new BalanceSheet(
            new FixedAssetsSection(assetCategories, fixedAssetsTotal),
            new CurrentAssetsSection(stock, totalDebtors, prepayments, cash, totalCurrentAssets),
            new CreditorsWithinYearSection(tradeCreditors, accruals, taxCreditors, otherCreditorsWithin, totalCreditorsWithin),
            netCurrentAssets,
            totalAssetsLessCurrentLiabs,
            new CreditorsAfterYearSection(loansAfterYear, otherCreditorsAfter, totalCreditorsAfter),
            netAssets,
            new CapitalSection(shareCapital, retainedEarnings, totalCapital),
            balances
        );
    }

    public async Task<ReadinessScore> GetReadinessScoreAsync(int periodId)
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
        int totalChecks = 12;

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
        completedChecks++; // Reviewed even if none (zero debtors is valid)

        // 6. Creditors reviewed?
        completedChecks++;

        // 7. Fixed assets up to date?
        if (period.Company.OwnsAssets)
        {
            var assetCount = await db.FixedAssets.CountAsync(a => a.CompanyId == companyId);
            if (assetCount > 0) completedChecks++; else missing.Add("Company owns assets but none registered");
        }
        else completedChecks++;

        // 8. Payroll summary?
        if (period.Company.IsEmployer)
        {
            var hasPayroll = await db.PayrollSummaries.AnyAsync(p => p.PeriodId == periodId);
            if (hasPayroll) completedChecks++; else missing.Add("Payroll summary not entered (company is an employer)");
        }
        else completedChecks++;

        // 9. Tax balances entered?
        var hasTax = await db.TaxBalances.AnyAsync(t => t.PeriodId == periodId);
        if (hasTax) completedChecks++; else missing.Add("No tax balances entered");

        // 10. Adjustments generated?
        var hasAdj = await db.Adjustments.AnyAsync(a => a.PeriodId == periodId);
        if (hasAdj) completedChecks++; else missing.Add("Year-end adjustments not generated");

        // 11. Adjustments approved?
        var unapproved = await db.Adjustments.CountAsync(a => a.PeriodId == periodId && a.ApprovedAt == null);
        if (unapproved == 0 && hasAdj) completedChecks++;
        else if (unapproved > 0) warnings.Add($"{unapproved} adjustments pending approval");

        // 12. Balance sheet balances?
        try
        {
            var bs = await GetBalanceSheetAsync(periodId);
            if (bs.Balances) completedChecks++; else warnings.Add("Balance sheet does not balance");
        }
        catch { warnings.Add("Could not compute balance sheet"); }

        var completeness = (int)Math.Round((double)completedChecks / totalChecks * 100);

        // Filing readiness is completeness minus critical missing items
        var filingReady = missing.Count == 0 && warnings.Count == 0 ? completeness : Math.Max(0, completeness - missing.Count * 10);

        return new ReadinessScore(completeness, filingReady, missing.Count == 0, missing, warnings);
    }
}
