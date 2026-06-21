using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Accounts.Api.Endpoints;

public static partial class YearEndEndpoints
{
    public static async Task<IResult> CreateDividendEndpointAsync(
        int companyId,
        int periodId,
        Dividend input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        if (YearEndFigureInputs.ForDividend(input) is { } invalid)
            return invalid;

        input.Id = 0;
        input.PeriodId = periodId;
        db.Dividends.Add(input);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "Dividend",
            input.Id,
            AuditEventCodes.DividendCreated,
            null,
            DividendSnapshot(input),
            AuditUserId(context));
        return Results.Created($"/api/companies/{companyId}/periods/{periodId}/dividends/{input.Id}", input);
    }

    public static async Task<IResult> UpdateDividendEndpointAsync(
        int companyId,
        int periodId,
        int id,
        Dividend input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        var item = await db.Dividends.FirstOrDefaultAsync(d => d.Id == id && d.PeriodId == periodId);
        if (item == null) return Results.NotFound();

        if (YearEndFigureInputs.ForDividend(input) is { } invalid)
            return invalid;

        var oldValue = DividendSnapshot(item);
        item.Amount = input.Amount;
        item.DateDeclared = input.DateDeclared;
        item.DatePaid = input.DatePaid;
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "Dividend",
            item.Id,
            AuditEventCodes.DividendUpdated,
            oldValue,
            DividendSnapshot(item),
            AuditUserId(context));
        return Results.Ok(item);
    }

    public static async Task<IResult> DeleteDividendEndpointAsync(
        int companyId,
        int periodId,
        int id,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        var item = await db.Dividends.FirstOrDefaultAsync(d => d.Id == id && d.PeriodId == periodId);
        if (item == null) return Results.NotFound();

        var oldValue = DividendSnapshot(item);
        db.Dividends.Remove(item);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "Dividend",
            id,
            AuditEventCodes.DividendDeleted,
            oldValue,
            new { Deleted = true },
            AuditUserId(context));
        return Results.NoContent();
    }

    public static async Task<IResult> CreateShareCapitalEndpointAsync(
        int companyId,
        ShareCapital input,
        AccountsDbContext db,
        AccountingWriteGuard writeGuard,
        AuditService audit,
        HttpContext context)
    {
        if (await RequireCompanyWriteAccessAsync(db, companyId, context) is { } denied)
            return denied;

        if (ShareCapitalInputs.Validate(input) is { } validationProblem)
            return validationProblem;

        var effectiveDate = input.IssueDate;
        if (await writeGuard.BlockIfCompanyAccountingLockedAsync(companyId, effectiveDate) is { } blocked)
            return blocked;

        input.CompanyId = companyId;
        input.TotalValue = input.NominalValue * input.NumberIssued;
        db.ShareCapitals.Add(input);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            null,
            "ShareCapital",
            input.Id,
            AuditEventCodes.ShareCapitalCreated,
            null,
            ShareCapitalSnapshot(input),
            AuditUserId(context));
        return Results.Created($"/api/companies/{companyId}/share-capital/{input.Id}", input);
    }

    public static async Task<IResult> UpdateShareCapitalEndpointAsync(
        int companyId,
        int id,
        ShareCapital input,
        AccountsDbContext db,
        AccountingWriteGuard writeGuard,
        AuditService audit,
        HttpContext context)
    {
        if (await RequireCompanyWriteAccessAsync(db, companyId, context) is { } denied)
            return denied;

        var item = await db.ShareCapitals.FirstOrDefaultAsync(s => s.Id == id && s.CompanyId == companyId);
        if (item == null) return Results.NotFound();
        if (ShareCapitalInputs.Validate(input) is { } validationProblem)
            return validationProblem;

        var oldValue = ShareCapitalSnapshot(item);
        var existingEffectiveDate = BankingEndpointInputs.Earliest(item.IssueDate, item.CancelledDate);
        var inputEffectiveDate = BankingEndpointInputs.Earliest(input.IssueDate, input.CancelledDate);
        var effectiveDate = BankingEndpointInputs.Earliest(existingEffectiveDate, inputEffectiveDate);
        if (await writeGuard.BlockIfCompanyAccountingLockedAsync(companyId, effectiveDate) is { } blocked)
            return blocked;

        item.ShareClass = input.ShareClass;
        item.NominalValue = input.NominalValue;
        item.NumberIssued = input.NumberIssued;
        item.TotalValue = input.NominalValue * input.NumberIssued;
        item.IsFullyPaid = input.IsFullyPaid;
        item.IssueDate = input.IssueDate;
        item.CancelledDate = input.CancelledDate;
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            null,
            "ShareCapital",
            item.Id,
            AuditEventCodes.ShareCapitalUpdated,
            oldValue,
            ShareCapitalSnapshot(item),
            AuditUserId(context));
        return Results.Ok(item);
    }

    public static async Task<IResult> DeleteShareCapitalEndpointAsync(
        int companyId,
        int id,
        AccountsDbContext db,
        AccountingWriteGuard writeGuard,
        AuditService audit,
        HttpContext context)
    {
        if (await RequireCompanyWriteAccessAsync(db, companyId, context) is { } denied)
            return denied;

        var item = await db.ShareCapitals.FirstOrDefaultAsync(s => s.Id == id && s.CompanyId == companyId);
        if (item == null) return Results.NotFound();

        var oldValue = ShareCapitalSnapshot(item);
        var effectiveDate = BankingEndpointInputs.Earliest(item.IssueDate, item.CancelledDate);
        if (await writeGuard.BlockIfCompanyAccountingLockedAsync(companyId, effectiveDate) is { } blocked)
            return blocked;

        db.ShareCapitals.Remove(item);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            null,
            "ShareCapital",
            id,
            AuditEventCodes.ShareCapitalDeleted,
            oldValue,
            new { Deleted = true },
            AuditUserId(context));
        return Results.NoContent();
    }

}
