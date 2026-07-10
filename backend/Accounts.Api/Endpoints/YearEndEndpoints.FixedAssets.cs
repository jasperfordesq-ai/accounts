using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Accounts.Api.Endpoints;

public static partial class YearEndEndpoints
{
    public static async Task<IResult> CreateFixedAssetEndpointAsync(
        int companyId,
        FixedAssetInput input,
        AccountsDbContext db,
        AccountingWriteGuard writeGuard,
        AuditService audit,
        HttpContext context)
    {
        if (await RequireCompanyWriteAccessAsync(db, companyId, context) is { } denied)
            return denied;

        if (YearEndFigureInputs.ForFixedAsset(input) is { } invalid)
            return invalid;

        if (await writeGuard.BlockIfCompanyAccountingLockedAsync(companyId, input.AcquisitionDate) is { } blocked)
            return blocked;

        var asset = input.ToEntity(companyId);
        StampCapitalAllowanceReview(asset, input, AuthContext.RequireUser(context));
        db.FixedAssets.Add(asset);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            null,
            "FixedAsset",
            asset.Id,
            AuditEventCodes.FixedAssetCreated,
            null,
            FixedAssetSnapshot(asset),
            AuditUserId(context));
        return Results.Created($"/api/companies/{companyId}/fixed-assets/{asset.Id}", asset);
    }

    public static async Task<IResult> UpdateFixedAssetEndpointAsync(
        int companyId,
        int id,
        FixedAssetInput input,
        AccountsDbContext db,
        AccountingWriteGuard writeGuard,
        AuditService audit,
        HttpContext context)
    {
        if (await RequireCompanyWriteAccessAsync(db, companyId, context) is { } denied)
            return denied;

        var item = await db.FixedAssets.FirstOrDefaultAsync(a => a.Id == id && a.CompanyId == companyId);
        if (item == null) return Results.NotFound();

        if (YearEndFigureInputs.ForFixedAsset(input) is { } invalid)
            return invalid;

        var oldValue = FixedAssetSnapshot(item);
        var effectiveDate = BankingEndpointInputs.Earliest(item.AcquisitionDate, input.AcquisitionDate);
        if (await writeGuard.BlockIfCompanyAccountingLockedAsync(companyId, effectiveDate) is { } blocked)
            return blocked;

        item.Name = input.Name!;
        item.Category = input.Category!;
        item.Cost = input.Cost;
        item.ResidualValue = input.ResidualValue;
        item.AcquisitionDate = input.AcquisitionDate;
        item.DisposalDate = input.DisposalDate;
        item.DisposalProceeds = input.DisposalProceeds;
        item.UsefulLifeYears = input.UsefulLifeYears;
        item.DepreciationMethod = input.DepreciationMethod;
        item.CapitalAllowanceTreatment = input.CapitalAllowanceTreatment;
        item.CapitalAllowanceEvidence = input.CapitalAllowanceEvidence;
        StampCapitalAllowanceReview(item, input, AuthContext.RequireUser(context));
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            null,
            "FixedAsset",
            item.Id,
            AuditEventCodes.FixedAssetUpdated,
            oldValue,
            FixedAssetSnapshot(item),
            AuditUserId(context));
        return Results.Ok(item);
    }

    public static async Task<IResult> DeleteFixedAssetEndpointAsync(
        int companyId,
        int id,
        AccountsDbContext db,
        AccountingWriteGuard writeGuard,
        AuditService audit,
        HttpContext context)
    {
        if (await RequireCompanyWriteAccessAsync(db, companyId, context) is { } denied)
            return denied;

        var item = await db.FixedAssets.FirstOrDefaultAsync(a => a.Id == id && a.CompanyId == companyId);
        if (item == null) return Results.NotFound();

        var oldValue = FixedAssetSnapshot(item);
        if (await writeGuard.BlockIfCompanyAccountingLockedAsync(companyId, item.AcquisitionDate) is { } blocked)
            return blocked;

        db.FixedAssets.Remove(item);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            null,
            "FixedAsset",
            id,
            AuditEventCodes.FixedAssetDeleted,
            oldValue,
            new { Deleted = true },
            AuditUserId(context));
        return Results.NoContent();
    }

    private static void StampCapitalAllowanceReview(
        FixedAsset asset,
        FixedAssetInput input,
        AuthenticatedUser user)
    {
        asset.CapitalAllowanceTreatment = input.CapitalAllowanceTreatment;
        asset.CapitalAllowanceEvidence = string.IsNullOrWhiteSpace(input.CapitalAllowanceEvidence)
            ? null
            : input.CapitalAllowanceEvidence.Trim();
        if (input.CapitalAllowanceTreatment == CapitalAllowanceTreatment.Unreviewed)
        {
            asset.CapitalAllowanceReviewedBy = null;
            asset.CapitalAllowanceReviewedAtUtc = null;
            return;
        }

        asset.CapitalAllowanceReviewedBy = AuthenticatedIdentity.ReviewerDisplayName(user);
        asset.CapitalAllowanceReviewedAtUtc = DateTime.UtcNow;
    }

}
