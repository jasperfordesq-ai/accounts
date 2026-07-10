using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Accounts.Api.Endpoints;

public static partial class YearEndEndpoints
{
    public static async Task<IResult> CreateDebtorEndpointAsync(
        int companyId,
        int periodId,
        DebtorInput input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        if (YearEndFigureInputs.ForDebtor(input) is { } invalid)
            return invalid;

        var debtor = input.ToEntity(periodId);
        db.Debtors.Add(debtor);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "Debtor",
            debtor.Id,
            AuditEventCodes.DebtorCreated,
            null,
            DebtorSnapshot(debtor),
            AuditUserId(context));
        return Results.Created($"/api/companies/{companyId}/periods/{periodId}/debtors/{debtor.Id}", debtor);
    }

    public static async Task<IResult> UpdateDebtorEndpointAsync(
        int companyId,
        int periodId,
        int id,
        DebtorInput input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        var item = await db.Debtors.FirstOrDefaultAsync(d => d.Id == id && d.PeriodId == periodId);
        if (item == null) return Results.NotFound();

        if (YearEndFigureInputs.ForDebtor(input) is { } invalid)
            return invalid;

        var oldValue = DebtorSnapshot(item);
        item.Name = input.Name!;
        item.Amount = input.Amount;
        item.Type = input.Type;
        item.Notes = input.Notes;
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "Debtor",
            item.Id,
            AuditEventCodes.DebtorUpdated,
            oldValue,
            DebtorSnapshot(item),
            AuditUserId(context));
        return Results.Ok(item);
    }

    public static async Task<IResult> DeleteDebtorEndpointAsync(
        int companyId,
        int periodId,
        int id,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        var item = await db.Debtors.FirstOrDefaultAsync(d => d.Id == id && d.PeriodId == periodId);
        if (item == null) return Results.NotFound();

        var oldValue = DebtorSnapshot(item);
        db.Debtors.Remove(item);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "Debtor",
            id,
            AuditEventCodes.DebtorDeleted,
            oldValue,
            new { Deleted = true },
            AuditUserId(context));
        return Results.NoContent();
    }

    public static async Task<IResult> CreateCreditorEndpointAsync(
        int companyId,
        int periodId,
        CreditorInput input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        if (YearEndFigureInputs.ForCreditor(input) is { } invalid)
            return invalid;

        var creditor = input.ToEntity(periodId);
        db.Creditors.Add(creditor);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "Creditor",
            creditor.Id,
            AuditEventCodes.CreditorCreated,
            null,
            CreditorSnapshot(creditor),
            AuditUserId(context));
        return Results.Created($"/api/companies/{companyId}/periods/{periodId}/creditors/{creditor.Id}", creditor);
    }

    public static async Task<IResult> UpdateCreditorEndpointAsync(
        int companyId,
        int periodId,
        int id,
        CreditorInput input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        var item = await db.Creditors.FirstOrDefaultAsync(c => c.Id == id && c.PeriodId == periodId);
        if (item == null) return Results.NotFound();

        if (YearEndFigureInputs.ForCreditor(input) is { } invalid)
            return invalid;

        var oldValue = CreditorSnapshot(item);
        item.Name = input.Name!;
        item.Amount = input.Amount;
        item.Type = input.Type;
        item.DueWithinYear = input.DueWithinYear;
        item.Notes = input.Notes;
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "Creditor",
            item.Id,
            AuditEventCodes.CreditorUpdated,
            oldValue,
            CreditorSnapshot(item),
            AuditUserId(context));
        return Results.Ok(item);
    }

    public static async Task<IResult> DeleteCreditorEndpointAsync(
        int companyId,
        int periodId,
        int id,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        var item = await db.Creditors.FirstOrDefaultAsync(c => c.Id == id && c.PeriodId == periodId);
        if (item == null) return Results.NotFound();

        var oldValue = CreditorSnapshot(item);
        db.Creditors.Remove(item);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "Creditor",
            id,
            AuditEventCodes.CreditorDeleted,
            oldValue,
            new { Deleted = true },
            AuditUserId(context));
        return Results.NoContent();
    }

    public static async Task<IResult> CreateInventoryEndpointAsync(
        int companyId,
        int periodId,
        InventoryInput input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        if (YearEndFigureInputs.ForInventory(input) is { } invalid)
            return invalid;

        var inventory = input.ToEntity(periodId);
        db.Inventories.Add(inventory);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "Inventory",
            inventory.Id,
            AuditEventCodes.InventoryCreated,
            null,
            InventorySnapshot(inventory),
            AuditUserId(context));
        return Results.Created($"/api/companies/{companyId}/periods/{periodId}/inventory/{inventory.Id}", inventory);
    }

    public static async Task<IResult> UpdateInventoryEndpointAsync(
        int companyId,
        int periodId,
        int id,
        InventoryInput input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        var item = await db.Inventories.FirstOrDefaultAsync(i => i.Id == id && i.PeriodId == periodId);
        if (item == null) return Results.NotFound();

        if (YearEndFigureInputs.ForInventory(input) is { } invalid)
            return invalid;

        var oldValue = InventorySnapshot(item);
        item.Description = input.Description!;
        item.Value = input.Value;
        item.ValuationMethod = input.ValuationMethod;
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "Inventory",
            item.Id,
            AuditEventCodes.InventoryUpdated,
            oldValue,
            InventorySnapshot(item),
            AuditUserId(context));
        return Results.Ok(item);
    }

    public static async Task<IResult> DeleteInventoryEndpointAsync(
        int companyId,
        int periodId,
        int id,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        var item = await db.Inventories.FirstOrDefaultAsync(i => i.Id == id && i.PeriodId == periodId);
        if (item == null) return Results.NotFound();

        var oldValue = InventorySnapshot(item);
        db.Inventories.Remove(item);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "Inventory",
            id,
            AuditEventCodes.InventoryDeleted,
            oldValue,
            new { Deleted = true },
            AuditUserId(context));
        return Results.NoContent();
    }

}
