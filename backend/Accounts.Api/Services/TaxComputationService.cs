using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Services;

public class TaxComputationService(AccountsDbContext db, FinancialStatementsService statementsService)
{
    public record TaxComputation(
        decimal AccountingProfit,
        List<TaxAdjustment> Adjustments,
        decimal TaxableProfit,
        decimal TradingLossAvailable,
        decimal CorporationTaxAt125,
        decimal CorporationTaxAt25,
        decimal TotalCorporationTax,
        decimal PreliminaryTaxPaid,
        decimal BalanceDue,
        string Notes
    );

    public record TaxAdjustment(string Description, decimal Amount, string Basis);

    public record Ct1SupportData(
        string CompanyName,
        string TaxReference,
        string PeriodStart,
        string PeriodEnd,
        decimal Turnover,
        decimal GrossProfit,
        decimal NetProfit,
        decimal TaxableProfit,
        decimal TaxDue,
        decimal PreliminaryTaxPaid,
        decimal BalanceDue,
        List<TaxAdjustment> Adjustments,
        decimal TotalDirectorsFees,
        decimal TotalEmployeeCosts,
        decimal DepreciationCharged,
        decimal CapitalAllowances,
        decimal TradingLossAvailable
    );

    public async Task<TaxComputation> ComputeAsync(int companyId, int periodId)
    {
        var period = await db.AccountingPeriods
            .Include(p => p.Company)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");

        var pl = await statementsService.GetProfitAndLossAsync(companyId, periodId);
        var accountingProfit = pl.ProfitBeforeTax;

        var adjustments = new List<TaxAdjustment>();

        // 1. Add back depreciation (not tax-deductible)
        var depreciationCharged = await db.DepreciationEntries
            .Where(d => d.PeriodId == periodId)
            .SumAsync(d => d.Charge);

        if (depreciationCharged > 0)
        {
            adjustments.Add(new TaxAdjustment("Add back: Depreciation", depreciationCharged, "Not deductible for tax — replaced by capital allowances"));
        }

        // 2. Add back entertainment expenses (disallowable)
        var entertainmentCats = await db.AccountCategories
            .Where(c => (c.CompanyId == period.CompanyId || c.IsSystem) && c.TaxTreatment == TaxTreatment.NonDeductible)
            .Select(c => c.Id)
            .ToListAsync();

        var nonDeductible = await db.ImportedTransactions
            .Where(t => t.PeriodId == periodId && t.CategoryId != null && entertainmentCats.Contains(t.CategoryId.Value) && !t.IsDuplicate)
            .SumAsync(t => Math.Abs(t.Amount));

        if (nonDeductible > 0)
        {
            adjustments.Add(new TaxAdjustment("Add back: Non-deductible expenses", nonDeductible, "Entertainment and other disallowable expenses per Schedule D Case I"));
        }

        // 3. Deduct capital allowances (12.5% per year over 8 years, tracking prior claims)
        var capitalAllowances = await ComputeCapitalAllowancesAsync(period.CompanyId, period.PeriodStart, period.PeriodEnd);

        if (capitalAllowances > 0)
        {
            adjustments.Add(new TaxAdjustment("Deduct: Capital allowances", -capitalAllowances, "Wear and tear allowances — s.284 TCA 1997, 12.5% straight line over 8 years, pro-rated for short accounting periods"));
        }

        // Irish corporation tax runs two separate computations: trading income (Case I) at 12.5%
        // and non-trading/passive income (Case III/IV/V — rent, deposit interest) at 25% under
        // s.21A TCA 1997. They must be kept apart: a trading loss is set against trading profits
        // only, and absent an elected claim it does NOT shelter passive income from the 25% charge.
        var nonTradingIncome = await statementsService.GetNonTradingIncomeAsync(companyId, periodId);

        // Case I trading result: accounting profit excluding the non-trading income, after the
        // trading adjustments (depreciation add-back, disallowables, capital allowances).
        var tradingProfitBeforeRelief = accountingProfit - nonTradingIncome;
        foreach (var adj in adjustments)
            tradingProfitBeforeRelief += adj.Amount;

        // A negative trading result is a loss, not a negative tax charge. It is carried forward
        // against future trading profits (s.396(1) TCA 1997). It is NOT automatically set against
        // this period's passive income — auto-applying loss relief (s.396A) would under-tax the
        // filing, so the passive income is charged in full and the loss is surfaced for carry-forward.
        var tradingLossAvailable = Math.Max(0m, -tradingProfitBeforeRelief);
        var tradingTaxable = Math.Max(0m, tradingProfitBeforeRelief);
        var nonTradingTaxable = Math.Max(0m, nonTradingIncome);
        var taxableProfit = tradingTaxable + nonTradingTaxable;

        var taxAt125 = Math.Round(tradingTaxable * 0.125m, 2);
        var taxAt25 = Math.Round(nonTradingTaxable * 0.25m, 2);
        var totalTax = taxAt125 + taxAt25;

        // Preliminary tax paid
        var prelimTax = await db.TaxBalances
            .Where(t => t.PeriodId == periodId && t.TaxType == TaxType.CorporationTax)
            .Select(t => t.Paid)
            .FirstOrDefaultAsync();

        var balanceDue = totalTax - prelimTax;

        // A trading loss and a 25% charge on passive income can co-exist, so describe each stream
        // that applies rather than assuming a single outcome.
        var noteParts = new List<string>();
        if (nonTradingTaxable > 0)
            noteParts.Add($"Non-trading income of €{nonTradingTaxable:N2} charged at 25% (s.21A TCA 1997).");
        if (tradingTaxable > 0)
            noteParts.Add($"Trading profit of €{tradingTaxable:N2} charged at the 12.5% trading rate.");
        if (tradingLossAvailable > 0)
            noteParts.Add($"Trading loss of €{tradingLossAvailable:N2} available to carry forward against future trading profits (s.396(1) TCA 1997).");
        if (noteParts.Count == 0)
            noteParts.Add("No taxable profit and no trading loss for the period.");
        if (prelimTax > 0)
            noteParts.Add($"Preliminary tax of €{prelimTax:N2} already paid.");
        var notes = string.Join(" ", noteParts);

        return new TaxComputation(accountingProfit, adjustments, taxableProfit, tradingLossAvailable, taxAt125, taxAt25, totalTax, prelimTax, balanceDue, notes);
    }

    public async Task<Ct1SupportData> GetCt1SupportDataAsync(int companyId, int periodId)
    {
        var period = await db.AccountingPeriods
            .Include(p => p.Company)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");

        var computation = await ComputeAsync(companyId, periodId);
        var pl = await statementsService.GetProfitAndLossAsync(companyId, periodId);

        var payroll = await db.PayrollSummaries.FirstOrDefaultAsync(p => p.PeriodId == periodId);
        var depCharged = await db.DepreciationEntries.Where(d => d.PeriodId == periodId).SumAsync(d => d.Charge);
        var capitalAllowances = await ComputeCapitalAllowancesAsync(period.CompanyId, period.PeriodStart, period.PeriodEnd);

        return new Ct1SupportData(
            period.Company.LegalName,
            period.Company.TaxReference ?? "",
            period.PeriodStart.ToString("yyyy-MM-dd"),
            period.PeriodEnd.ToString("yyyy-MM-dd"),
            pl.Turnover,
            pl.GrossProfit,
            pl.ProfitBeforeTax,
            computation.TaxableProfit,
            computation.TotalCorporationTax,
            computation.PreliminaryTaxPaid,
            computation.BalanceDue,
            computation.Adjustments,
            payroll?.GrossWages ?? 0,
            payroll != null ? payroll.GrossWages + payroll.EmployerPrsi + payroll.PensionContributions : 0,
            depCharged,
            capitalAllowances,
            computation.TradingLossAvailable
        );
    }

    public record CapitalAllowanceClaimResult(int AssetId, decimal Cost, decimal Claim);

    private async Task<decimal> ComputeCapitalAllowancesAsync(int companyId, DateOnly periodStart, DateOnly periodEnd) =>
        (await ComputeCapitalAllowanceClaimsAsync(companyId, periodStart, periodEnd)).Sum(c => c.Claim);

    // Per-asset wear and tear allowance for the period, capped so the cumulative claim never
    // exceeds 100% of cost. Crucially, the "already allowed" figure is read from the persisted
    // per-asset claims of prior periods (CapitalAllowanceClaims), NOT re-estimated from period
    // length or from depreciation entries — capital allowances run for eight years regardless of
    // the accounting depreciation life, so the actual cumulative claim cannot be re-derived (BL-06).
    public async Task<List<CapitalAllowanceClaimResult>> ComputeCapitalAllowanceClaimsAsync(int companyId, DateOnly periodStart, DateOnly periodEnd)
    {
        var qualifyingAssets = await db.FixedAssets
            .Where(a => a.CompanyId == companyId
                && a.AcquisitionDate <= periodEnd
                && (a.DisposalDate == null || a.DisposalDate > periodEnd))
            .ToListAsync();

        // Wear and tear is 12.5% of cost for a 12-month accounting period, reduced
        // proportionately for a period of less than 12 months (s.284(3A) TCA 1997).
        // A single accounting period never attracts more than one full year's allowance.
        var periodFraction = PeriodYearFraction(periodStart, periodEnd);

        var results = new List<CapitalAllowanceClaimResult>();
        foreach (var asset in qualifyingAssets)
        {
            // Allowance actually claimed against this asset in prior periods.
            var priorClaims = await db.CapitalAllowanceClaims
                .Where(c => c.AssetId == asset.Id && c.Period.PeriodEnd < periodStart)
                .SumAsync(c => c.Claim);

            var remainingCost = Math.Max(0m, asset.Cost - priorClaims);
            var thisPeriodClaim = Math.Min(Math.Round(asset.Cost * 0.125m * periodFraction, 2), remainingCost);

            if (thisPeriodClaim > 0)
                results.Add(new CapitalAllowanceClaimResult(asset.Id, asset.Cost, thisPeriodClaim));
        }

        return results;
    }

    // Records the wear-and-tear allowance claimed against each qualifying asset this period, so
    // future periods can read the actual cumulative claim instead of re-estimating it (BL-06).
    // Called when a period's accounts/adjustments are generated.
    public async Task PersistCapitalAllowanceClaimsAsync(int companyId, int periodId)
    {
        var period = await db.AccountingPeriods
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");

        var existing = await db.CapitalAllowanceClaims
            .Where(c => c.PeriodId == periodId)
            .ToListAsync();
        db.CapitalAllowanceClaims.RemoveRange(existing);

        var claims = await ComputeCapitalAllowanceClaimsAsync(companyId, period.PeriodStart, period.PeriodEnd);
        foreach (var claim in claims)
        {
            db.CapitalAllowanceClaims.Add(new CapitalAllowanceClaim
            {
                AssetId = claim.AssetId,
                PeriodId = periodId,
                Cost = claim.Cost,
                Claim = claim.Claim
            });
        }

        await db.SaveChangesAsync();
    }

    // Length of an accounting period as a fraction of a 12-month year (capped at 1.0),
    // used to pro-rate wear and tear allowances for short accounting periods.
    private static decimal PeriodYearFraction(DateOnly start, DateOnly end)
    {
        var days = end.DayNumber - start.DayNumber + 1;
        if (days <= 0) return 0m;
        return Math.Min(1m, days / 365m);
    }
}
