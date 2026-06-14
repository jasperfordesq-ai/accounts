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
        decimal CapitalAllowances
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
            adjustments.Add(new TaxAdjustment("Deduct: Capital allowances (12.5%)", -capitalAllowances, "Wear and tear allowances — s.284 TCA 1997, 8-year straight line"));
        }

        var taxableProfit = accountingProfit;
        foreach (var adj in adjustments)
            taxableProfit += adj.Amount;

        taxableProfit = Math.Max(0, taxableProfit); // Can't be negative for tax calc (losses carried forward separately)

        // Irish corporation tax rates (2024):
        // 12.5% on trading income
        // 25% on non-trading/passive income
        // For simplicity, assume all trading income
        var taxAt125 = Math.Round(taxableProfit * 0.125m, 2);
        var taxAt25 = 0m; // Would apply to investment income
        var totalTax = taxAt125 + taxAt25;

        // Preliminary tax paid
        var prelimTax = await db.TaxBalances
            .Where(t => t.PeriodId == periodId && t.TaxType == TaxType.CorporationTax)
            .Select(t => t.Paid)
            .FirstOrDefaultAsync();

        var balanceDue = totalTax - prelimTax;

        var notes = taxableProfit == 0
            ? "No taxable profit — losses may be available for carry forward under s.396 TCA 1997."
            : $"Corporation tax computed at 12.5% trading rate. Preliminary tax of \u20ac{prelimTax:N2} already paid.";

        return new TaxComputation(accountingProfit, adjustments, taxableProfit, taxAt125, taxAt25, totalTax, prelimTax, balanceDue, notes);
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
            capitalAllowances
        );
    }

    private async Task<decimal> ComputeCapitalAllowancesAsync(int companyId, DateOnly periodStart, DateOnly periodEnd)
    {
        var qualifyingAssets = await db.FixedAssets
            .Where(a => a.CompanyId == companyId
                && a.AcquisitionDate <= periodEnd
                && (a.DisposalDate == null || a.DisposalDate > periodEnd))
            .ToListAsync();

        var capitalAllowances = 0m;
        foreach (var asset in qualifyingAssets)
        {
            // Each prior-period depreciation entry represents a year of capital allowances claimed.
            var priorYearsClaimed = await db.DepreciationEntries
                .Where(d => d.AssetId == asset.Id && d.Period.PeriodEnd < periodStart)
                .CountAsync();

            if (priorYearsClaimed < 8)
                capitalAllowances += asset.Cost * 0.125m;
        }

        return Math.Round(capitalAllowances, 2);
    }
}
