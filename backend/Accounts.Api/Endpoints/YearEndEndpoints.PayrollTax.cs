using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Accounts.Api.Endpoints;

public static partial class YearEndEndpoints
{
    public static async Task<IResult> UpsertPayrollSummaryEndpointAsync(
        int companyId,
        int periodId,
        PayrollSummaryInput input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        if (input.GrossWages < 0
            || input.DirectorsFees < 0
            || input.EmployerPrsi < 0
            || input.PensionContributions < 0
            || input.StaffCount < 0)
        {
            return Results.BadRequest(new { error = "Payroll amounts and staff count must not be negative." });
        }

        var item = await db.PayrollSummaries.FirstOrDefaultAsync(p => p.PeriodId == periodId);
        var wasCreated = item is null;
        var oldValue = item is null ? null : PayrollSummarySnapshot(item);

        if (item == null)
        {
            item = input.ToEntity(periodId);
            db.PayrollSummaries.Add(item);
        }
        else
        {
            item.GrossWages = input.GrossWages;
            item.DirectorsFees = input.DirectorsFees;
            item.EmployerPrsi = input.EmployerPrsi;
            item.PensionContributions = input.PensionContributions;
            item.StaffCount = input.StaffCount;
        }

        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "PayrollSummary",
            item.Id,
            AuditEventCodes.PayrollSummaryUpserted,
            oldValue,
            new
            {
                item.GrossWages,
                item.DirectorsFees,
                item.EmployerPrsi,
                item.PensionContributions,
                item.StaffCount,
                WasCreated = wasCreated
            },
            AuditUserId(context));
        return Results.Ok(item);
    }

    public static async Task<IResult> UpsertTaxBalanceEndpointAsync(
        int companyId,
        int periodId,
        TaxType taxType,
        TaxBalanceInput input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        // accounting-tax-balance-internal-consistency: the triple must be self-consistent. A verbatim
        // inconsistent triple (e.g. Balance != Liability - Paid) mis-states creditors and profit-after-
        // tax downstream. Liability and Paid are amounts and cannot be negative; the outstanding Balance
        // must equal Liability - Paid (a negative Balance is a legitimate overpayment / refund due).
        if (input.Liability < 0 || input.Paid < 0)
            return Results.BadRequest(new { error = "Tax liability and amount paid must not be negative." });
        if (Math.Abs(input.Balance - (input.Liability - input.Paid)) > 0.005m)
            return Results.BadRequest(new { error = "Tax balance must equal liability minus amount paid." });

        var item = await db.TaxBalances.FirstOrDefaultAsync(t => t.PeriodId == periodId && t.TaxType == taxType);
        var wasCreated = item is null;
        var oldValue = item is null ? null : TaxBalanceSnapshot(item);
        if (item == null)
        {
            item = input.ToEntity(periodId, taxType);
            db.TaxBalances.Add(item);
        }
        else
        {
            item.Liability = input.Liability;
            item.Paid = input.Paid;
            item.Balance = input.Balance;
        }

        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "TaxBalance",
            item.Id,
            AuditEventCodes.TaxBalanceUpserted,
            oldValue,
            new
            {
                item.TaxType,
                item.Liability,
                item.Paid,
                item.Balance,
                WasCreated = wasCreated
            },
            AuditUserId(context));
        return Results.Ok(item);
    }

}
