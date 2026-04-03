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

    public async Task<TaxComputation> ComputeAsync(int periodId)
    {
        var period = await db.AccountingPeriods
            .Include(p => p.Company)
            .FirstAsync(p => p.Id == periodId);

        var pl = await statementsService.GetProfitAndLossAsync(periodId);
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
        var qualifyingAssets = await db.FixedAssets
            .Where(a => a.CompanyId == period.CompanyId && a.DisposalDate == null)
            .ToListAsync();

        var capitalAllowances = 0m;
        foreach (var asset in qualifyingAssets)
        {
            // Count how many prior periods have depreciation entries for this asset
            // (each entry represents one year of capital allowances claimed)
            var priorYearsClaimed = await db.DepreciationEntries
                .Where(d => d.AssetId == asset.Id && d.PeriodId != periodId)
                .CountAsync();

            // Only claim 12.5% if fewer than 8 years of allowances have been claimed
            if (priorYearsClaimed < 8)
            {
                capitalAllowances += asset.Cost * 0.125m;
            }
        }
        capitalAllowances = Math.Round(capitalAllowances, 2);

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

    public async Task<Ct1SupportData> GetCt1SupportDataAsync(int periodId)
    {
        var period = await db.AccountingPeriods
            .Include(p => p.Company)
            .FirstAsync(p => p.Id == periodId);

        var computation = await ComputeAsync(periodId);
        var pl = await statementsService.GetProfitAndLossAsync(periodId);

        var payroll = await db.PayrollSummaries.FirstOrDefaultAsync(p => p.PeriodId == periodId);
        var depCharged = await db.DepreciationEntries.Where(d => d.PeriodId == periodId).SumAsync(d => d.Charge);
        var ct1QualifyingAssets = await db.FixedAssets.Where(a => a.CompanyId == period.CompanyId && a.DisposalDate == null).ToListAsync();
        var capitalAllowances = 0m;
        foreach (var asset in ct1QualifyingAssets)
        {
            var priorYearsClaimed = await db.DepreciationEntries
                .Where(d => d.AssetId == asset.Id && d.PeriodId != periodId)
                .CountAsync();
            if (priorYearsClaimed < 8)
                capitalAllowances += asset.Cost * 0.125m;
        }
        capitalAllowances = Math.Round(capitalAllowances, 2);

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
}
