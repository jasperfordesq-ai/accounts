using Accounts.Api.Data;
using Accounts.Api.Middleware;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Endpoints;

public static class CompanyDeletionEndpoint
{
    /// <summary>
    /// data-company-soft-delete: a company DELETE cascade-wipes every period, transaction, year-end
    /// figure and filing irreversibly. When any period holds financial data the delete is blocked unless
    /// the caller supplies a typed confirmation equal to the exact legal name, so a populated company
    /// cannot be wiped by accident.
    /// </summary>
    public static async Task<IResult> DeleteAsync(
        int id,
        string? confirmName,
        HttpContext context,
        ApiAccessService apiAccess,
        AccountsDbContext db,
        AccountingWriteGuard writeGuard)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, id))
            return Results.NotFound();

        if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
            return denied;

        var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == id);
        if (company is null) return Results.NotFound();

        if (await writeGuard.BlockIfCompanyMasterDataLockedAsync(id) is { } blocked)
            return blocked;

        if (await CompanyHasFinancialDataAsync(db, id)
            && !string.Equals(confirmName?.Trim(), company.LegalName, StringComparison.Ordinal))
        {
            return Results.BadRequest(new
            {
                error = $"This company holds financial data and deleting it is irreversible. To confirm, "
                    + $"resend the delete with confirmName equal to the exact legal name \"{company.LegalName}\"."
            });
        }

        db.Companies.Remove(company);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    /// <summary>True when any of the company's periods holds transactions, year-end figures or a filing.</summary>
    public static async Task<bool> CompanyHasFinancialDataAsync(AccountsDbContext db, int companyId)
    {
        var periodIds = await db.AccountingPeriods
            .Where(p => p.CompanyId == companyId)
            .Select(p => p.Id)
            .ToListAsync();
        if (periodIds.Count == 0) return false;

        return await db.ImportedTransactions.AnyAsync(t => t.PeriodId != null && periodIds.Contains(t.PeriodId.Value))
            || await db.Debtors.AnyAsync(d => periodIds.Contains(d.PeriodId))
            || await db.Creditors.AnyAsync(c => periodIds.Contains(c.PeriodId))
            || await db.TaxBalances.AnyAsync(t => periodIds.Contains(t.PeriodId))
            || await db.CroFilingPackages.AnyAsync(f => periodIds.Contains(f.PeriodId))
            || await db.RevenueFilingPackages.AnyAsync(f => periodIds.Contains(f.PeriodId));
    }
}
