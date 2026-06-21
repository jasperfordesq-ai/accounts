using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Endpoints;

public static class CharityEndpoints
{
    public static void MapCharityEndpoints(this WebApplication app)
    {
        var companyGroup = app.MapGroup("/api/companies/{companyId:int}/charity").WithTags("Charity");
        var periodGroup = app.MapGroup("/api/companies/{companyId:int}/periods/{periodId:int}/charity").WithTags("Charity");

        companyGroup.MapGet("/info", async (int companyId, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
                return Results.NotFound();

            var info = await db.CharityInfos.FirstOrDefaultAsync(c => c.CompanyId == companyId);
            return info != null ? Results.Ok(info) : Results.Ok(new { message = "No charity info configured" });
        });

        companyGroup.MapPut("/info", SaveCharityInfoEndpointAsync);

        periodGroup.MapGet("/sofa", async (int companyId, int periodId, CharityReportingService service, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            var sofa = await service.GenerateSofaAsync(companyId, periodId);
            return Results.Ok(sofa);
        });

        // filing-charity-pdf-and-reconciliation: the SoFA total funds must reconcile to the balance-sheet
        // net assets; this surfaces the difference so the UI can block/warn on a mismatch.
        periodGroup.MapGet("/sofa/reconciliation", async (int companyId, int periodId, CharityReportingService service, FinancialStatementsService statements, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            var balanceSheet = await statements.GetBalanceSheetAsync(companyId, periodId);
            var reconciliation = await service.ReconcileSofaToNetAssetsAsync(companyId, periodId, balanceSheet.NetAssets);
            return Results.Ok(reconciliation);
        });

        periodGroup.MapGet("/trustees-report", async (int companyId, int periodId, CharityReportingService service, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            var tar = await service.GenerateTarAsync(companyId, periodId);
            return Results.Ok(tar);
        });

        periodGroup.MapGet("/funds", ListFundBalancesEndpointAsync);

        periodGroup.MapPost("/funds", CreateFundBalanceEndpointAsync);

        periodGroup.MapPut("/funds/{id:int}", UpdateFundBalanceEndpointAsync);

        periodGroup.MapDelete("/funds/{id:int}", DeleteFundBalanceEndpointAsync);
    }

    public static async Task<IResult> SaveCharityInfoEndpointAsync(
        int companyId,
        CharityInfo input,
        CharityReportingService service,
        AccountingWriteGuard writeGuard,
        AuditService audit,
        AccountsDbContext db,
        HttpContext context,
        ApiAccessService apiAccess)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
            return Results.NotFound();

        if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
            return denied;

        if (await writeGuard.BlockIfCompanyMasterDataLockedAsync(companyId) is { } blocked)
            return blocked;

        var existing = await db.CharityInfos
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CompanyId == companyId);
        var oldValue = existing is null ? null : CharityInfoSnapshot(existing);
        input.Id = 0;
        input.CompanyId = companyId;
        input.CreatedAt = DateTime.UtcNow;
        var result = await service.SaveCharityInfoAsync(companyId, input);
        var user = AuthContext.RequireUser(context);
        await audit.LogAsync(
            companyId,
            null,
            "CharityInfo",
            result.Id,
            existing is null ? AuditEventCodes.CharityInfoCreated : AuditEventCodes.CharityInfoUpdated,
            oldValue,
            CharityInfoSnapshot(result),
            AuthenticatedIdentity.AuditUserId(user));

        return Results.Ok(result);
    }

    public static async Task<IResult> ListFundBalancesEndpointAsync(
        int companyId,
        int periodId,
        AccountsDbContext db,
        HttpContext context)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        var funds = await db.FundBalances
            .Where(f => f.PeriodId == periodId)
            .OrderBy(f => f.FundType)
            .ThenBy(f => f.FundName)
            .ToListAsync();
        return Results.Ok(funds);
    }

    public static async Task<IResult> CreateFundBalanceEndpointAsync(
        int companyId,
        int periodId,
        FundBalance input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context,
        ApiAccessService apiAccess)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
            return denied;

        if (await ValidateFundWritePeriodAsync(db, companyId, periodId) is { } blocked)
            return blocked;

        if (ValidateFundBalanceInput(input) is { } invalid)
            return invalid;

        var user = AuthContext.RequireUser(context);
        PrepareFundBalance(input, periodId);
        db.FundBalances.Add(input);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "FundBalance",
            input.Id,
            AuditEventCodes.FundBalanceCreated,
            null,
            FundBalanceSnapshot(input),
            AuthenticatedIdentity.AuditUserId(user));

        return Results.Created($"/api/companies/{companyId}/periods/{periodId}/charity/funds/{input.Id}", input);
    }

    public static async Task<IResult> UpdateFundBalanceEndpointAsync(
        int companyId,
        int periodId,
        int id,
        FundBalance input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context,
        ApiAccessService apiAccess)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
            return denied;

        if (await ValidateFundWritePeriodAsync(db, companyId, periodId) is { } blocked)
            return blocked;

        if (ValidateFundBalanceInput(input) is { } invalid)
            return invalid;

        var item = await db.FundBalances.FirstOrDefaultAsync(f => f.Id == id && f.PeriodId == periodId);
        if (item == null) return Results.NotFound();

        var user = AuthContext.RequireUser(context);
        var oldValue = FundBalanceSnapshot(item);
        ApplyFundBalance(item, input);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "FundBalance",
            item.Id,
            AuditEventCodes.FundBalanceUpdated,
            oldValue,
            FundBalanceSnapshot(item),
            AuthenticatedIdentity.AuditUserId(user));

        return Results.Ok(item);
    }

    public static async Task<IResult> DeleteFundBalanceEndpointAsync(
        int companyId,
        int periodId,
        int id,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context,
        ApiAccessService apiAccess)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
            return denied;

        if (await ValidateFundWritePeriodAsync(db, companyId, periodId) is { } blocked)
            return blocked;

        var item = await db.FundBalances.FirstOrDefaultAsync(f => f.Id == id && f.PeriodId == periodId);
        if (item == null) return Results.NotFound();

        var user = AuthContext.RequireUser(context);
        var oldValue = FundBalanceSnapshot(item);
        db.FundBalances.Remove(item);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "FundBalance",
            id,
            AuditEventCodes.FundBalanceDeleted,
            oldValue,
            new { Deleted = true },
            AuthenticatedIdentity.AuditUserId(user));

        return Results.NoContent();
    }

    private static async Task<IResult?> ValidateFundWritePeriodAsync(
        AccountsDbContext db,
        int companyId,
        int periodId)
    {
        var period = await db.AccountingPeriods
            .AsNoTracking()
            .Where(p => p.Id == periodId && p.CompanyId == companyId)
            .Select(p => new { p.Status, p.LockedAt })
            .FirstOrDefaultAsync();
        if (period is null)
            return Results.NotFound();

        return period.Status is PeriodStatus.Finalised or PeriodStatus.Filed || period.LockedAt is not null
            ? Results.Conflict(new { error = "Accounting period is locked. Reopen the period before changing charity fund balances." })
            : null;
    }

    private static void PrepareFundBalance(FundBalance fund, int periodId)
    {
        fund.Id = 0;
        fund.PeriodId = periodId;
        fund.ClosingBalance = CalculateClosingBalance(fund);
    }

    private static IResult? ValidateFundBalanceInput(FundBalance fund)
    {
        if (string.IsNullOrWhiteSpace(fund.FundName))
            return Results.BadRequest(new { error = "Fund name is required." });

        if (string.IsNullOrWhiteSpace(fund.FundType))
            return Results.BadRequest(new { error = "Fund type is required." });

        return null;
    }

    private static void ApplyFundBalance(FundBalance target, FundBalance input)
    {
        target.FundName = input.FundName;
        target.FundType = input.FundType;
        target.OpeningBalance = input.OpeningBalance;
        target.IncomingResources = input.IncomingResources;
        target.ResourcesExpended = input.ResourcesExpended;
        target.Transfers = input.Transfers;
        target.GainsLosses = input.GainsLosses;
        target.ClosingBalance = CalculateClosingBalance(input);
        target.Notes = input.Notes;
    }

    private static decimal CalculateClosingBalance(FundBalance fund) =>
        fund.OpeningBalance + fund.IncomingResources - fund.ResourcesExpended + fund.Transfers + fund.GainsLosses;

    private static object CharityInfoSnapshot(CharityInfo info) => new
    {
        info.Id,
        info.CompanyId,
        info.CharityNumber,
        info.CharityType,
        info.GrossIncome,
        info.SorpTier,
        info.CharitableObjectives,
        info.PrincipalActivities,
        info.GovernanceCodeCompliant,
        info.GovernanceCodeNote,
        info.HasInternationalTransfers,
        info.InternationalTransferDetails,
        info.TrusteeRemunerationPaid,
        info.TrusteeRemunerationAmount,
        info.TrusteeExpensesDetails
    };

    private static object FundBalanceSnapshot(FundBalance fund) => new
    {
        fund.Id,
        fund.PeriodId,
        fund.FundName,
        fund.FundType,
        fund.OpeningBalance,
        fund.IncomingResources,
        fund.ResourcesExpended,
        fund.Transfers,
        fund.GainsLosses,
        fund.ClosingBalance,
        fund.Notes
    };
}
