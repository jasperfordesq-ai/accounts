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
        DividendInput input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        if (YearEndFigureInputs.ForDividend(input) is { } invalid)
            return invalid;

        var dividend = input.ToEntity(periodId);
        db.Dividends.Add(dividend);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "Dividend",
            dividend.Id,
            AuditEventCodes.DividendCreated,
            null,
            DividendSnapshot(dividend),
            AuditUserId(context));
        return Results.Created($"/api/companies/{companyId}/periods/{periodId}/dividends/{dividend.Id}", dividend);
    }

    public static async Task<IResult> UpdateDividendEndpointAsync(
        int companyId,
        int periodId,
        int id,
        DividendInput input,
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
        ShareCapitalInput input,
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

        var shareCapital = input.ToEntity(companyId);
        db.ShareCapitals.Add(shareCapital);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            null,
            "ShareCapital",
            shareCapital.Id,
            AuditEventCodes.ShareCapitalCreated,
            null,
            ShareCapitalSnapshot(shareCapital),
            AuditUserId(context));
        return Results.Created($"/api/companies/{companyId}/share-capital/{shareCapital.Id}", shareCapital);
    }

    public static async Task<IResult> UpdateShareCapitalEndpointAsync(
        int companyId,
        int id,
        ShareCapitalInput input,
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
