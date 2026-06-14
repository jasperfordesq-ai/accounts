using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Endpoints;

public static class ClassificationEndpoints
{
    public static void MapClassificationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/companies/{companyId:int}/periods/{periodId:int}").WithTags("Classification");

        // Save size classification data
        group.MapPut("/size-classification", SaveSizeClassificationEndpointAsync);

        // Run classification engine
        group.MapPost("/classify", async (int companyId, int periodId, SizeClassificationService service, AccountsDbContext db, HttpContext context, ApiAccessService apiAccess) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            var result = await service.ClassifyAsync(companyId, periodId, AuditUserId(context));
            return Results.Ok(result);
        });

        // Determine filing regime
        group.MapPost("/filing-regime", async (int companyId, int periodId, FilingRegimeInput? input, FilingRegimeService service, AccountsDbContext db, HttpContext context, ApiAccessService apiAccess) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            var result = await service.DetermineAsync(companyId, periodId, input?.ElectedRegime, AuditUserId(context));
            return Results.Ok(result);
        });

        // Get current filing regime
        group.MapGet("/filing-regime", async (int companyId, int periodId, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            var fr = await db.FilingRegimes
                .Include(f => f.Period)
                .FirstOrDefaultAsync(f => f.PeriodId == periodId && f.Period.CompanyId == companyId);
            return fr != null ? Results.Ok(fr) : Results.NotFound();
        });

        // Save member audit notice (s.334 Companies Act 2014)
        group.MapPut("/member-audit-notice", async (int companyId, int periodId, MemberAuditNoticeInput input, AccountsDbContext db, AuditService audit, HttpContext context, ApiAccessService apiAccess) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            var period = await db.AccountingPeriods
                .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId);
            if (period == null) return Results.NotFound();

            var oldValue = new
            {
                period.MemberAuditNoticeReceived,
                period.MemberAuditNoticeDate
            };
            period.MemberAuditNoticeReceived = input.Received;
            period.MemberAuditNoticeDate = input.NoticeDate;
            await db.SaveChangesAsync();
            await audit.LogAsync(
                companyId,
                periodId,
                "AccountingPeriod",
                periodId,
                AuditEventCodes.MemberAuditNoticeUpdated,
                oldValue,
                new
                {
                    period.MemberAuditNoticeReceived,
                    period.MemberAuditNoticeDate
                },
                AuditUserId(context));
            return Results.Ok(new { period.MemberAuditNoticeReceived, period.MemberAuditNoticeDate });
        });
    }

    public static async Task<IResult> SaveSizeClassificationEndpointAsync(
        int companyId,
        int periodId,
        SizeClassificationInput input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context,
        ApiAccessService apiAccess)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
            return denied;

        var period = await db.AccountingPeriods
            .Include(p => p.SizeClassification)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId);
        if (period == null) return Results.NotFound();

        var sc = period.SizeClassification;
        object? oldValue = sc is null ? null : SizeClassificationInputSnapshot(sc);
        if (sc == null)
        {
            sc = new SizeClassification { PeriodId = periodId };
            db.SizeClassifications.Add(sc);
        }

        sc.Turnover = input.Turnover;
        sc.BalanceSheetTotal = input.BalanceSheetTotal;
        sc.AvgEmployees = input.AvgEmployees;
        sc.PriorYearClass = input.PriorYearClass;

        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "SizeClassification",
            sc.Id,
            AuditEventCodes.SizeClassificationDataSaved,
            oldValue,
            SizeClassificationInputSnapshot(sc),
            AuditUserId(context));
        return Results.Ok(sc);
    }

    private static string? AuditUserId(HttpContext context)
    {
        var user = AuthContext.GetUser(context);
        return user is null ? null : AuthenticatedIdentity.AuditUserId(user);
    }

    private static object SizeClassificationInputSnapshot(SizeClassification sc) => new
    {
        sc.Turnover,
        sc.BalanceSheetTotal,
        sc.AvgEmployees,
        sc.PriorYearClass
    };
}

public record SizeClassificationInput(decimal Turnover, decimal BalanceSheetTotal, int AvgEmployees, CompanySizeClass? PriorYearClass);
public record FilingRegimeInput(ElectedRegime? ElectedRegime);
public record MemberAuditNoticeInput(bool Received, DateOnly? NoticeDate);
