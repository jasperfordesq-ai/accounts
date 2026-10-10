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

        // Run classification engine, optionally saving the inputs in the same write
        group.MapPost("/classify", ClassifySizeEndpointAsync)
            .Produces<SizeClassificationService.ClassificationResult>();

        // Apply or re-review a conservative, evidence-bound statutory classification override.
        group.MapPut("/size-classification/override", ApplySizeClassificationOverrideEndpointAsync);

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
            .Include(p => p.FilingRegime)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId);
        if (period == null) return Results.NotFound();

        var (sc, oldValue) = ApplySizeClassificationInputs(db, period, input);

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

    public static async Task<IResult> ClassifySizeEndpointAsync(
        int companyId,
        int periodId,
        SizeClassificationInput? input,
        SizeClassificationService service,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context,
        ApiAccessService apiAccess)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
            return denied;

        if (input is null)
            return Results.Ok(await service.ClassifyAsync(companyId, periodId, AuditUserId(context)));

        // Inputs and decision are written by one SaveChanges inside ClassifyLoadedAsync. A
        // classification the engine rejects therefore leaves no inputs-only row behind.
        var period = await service.LoadPeriodForClassificationAsync(companyId, periodId);
        var decisionOldValue = period.SizeClassification is { } existing
            ? SizeClassificationService.DecisionAuditSnapshot(existing)
            : null;
        var (sc, inputOldValue) = ApplySizeClassificationInputs(db, period, input);
        var result = await service.ClassifyLoadedAsync(period, decisionOldValue, AuditUserId(context));
        await audit.LogAsync(
            companyId,
            periodId,
            "SizeClassification",
            sc.Id,
            AuditEventCodes.SizeClassificationDataSaved,
            inputOldValue,
            SizeClassificationInputSnapshot(sc),
            AuditUserId(context));
        return Results.Ok(result);
    }

    /// <summary>
    /// Validates and applies caller inputs to the tracked period's classification without saving.
    /// </summary>
    private static (SizeClassification Classification, object? OldValue) ApplySizeClassificationInputs(
        AccountsDbContext db,
        AccountingPeriod period,
        SizeClassificationInput input)
    {
        SizeClassificationService.ValidateInputs(
            input.Turnover,
            input.BalanceSheetTotal,
            input.AvgEmployees,
            input.ThresholdElectionEffectiveFrom);
        if (input.PriorYearClass is not null)
            throw new BusinessRuleException("Prior-year class is derived from retained raw prior-period figures and cannot be supplied by the caller.");

        var sc = period.SizeClassification;
        object? oldValue = sc is null ? null : SizeClassificationInputSnapshot(sc);
        if (sc == null)
        {
            sc = new SizeClassification { PeriodId = period.Id };
            db.SizeClassifications.Add(sc);
            period.SizeClassification = sc;
        }

        var changed = sc.Turnover != input.Turnover
            || sc.BalanceSheetTotal != input.BalanceSheetTotal
            || sc.AvgEmployees != input.AvgEmployees
            || sc.ThresholdElectionEffectiveFrom != input.ThresholdElectionEffectiveFrom;

        sc.Turnover = input.Turnover;
        sc.BalanceSheetTotal = input.BalanceSheetTotal;
        sc.AvgEmployees = input.AvgEmployees;
        sc.ThresholdElectionEffectiveFrom = input.ThresholdElectionEffectiveFrom;
        if (changed)
        {
            sc.DecisionInputFingerprintSha256 = null;
            sc.QualificationNotes = null;
            if (sc.OverrideClass is not null)
                sc.OverrideRequiresRereview = true;
            if (period.FilingRegime is not null)
                db.FilingRegimes.Remove(period.FilingRegime);
        }

        return (sc, oldValue);
    }

    public static async Task<IResult> ApplySizeClassificationOverrideEndpointAsync(
        int companyId,
        int periodId,
        SizeClassificationOverrideInput input,
        SizeClassificationService service,
        AccountsDbContext db,
        HttpContext context,
        ApiAccessService apiAccess)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();
        if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
            return denied;

        var user = AuthContext.RequireUser(context);
        if (user.Role is not "Owner" and not "Reviewer")
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        var result = await service.ApplyOverrideAsync(
            companyId,
            periodId,
            new SizeClassificationOverrideRequest(
                input.OverrideClass,
                input.Reason,
                input.EvidenceArtifact,
                input.EvidenceSha256),
            user.Role,
            user.DisplayName,
            AuthenticatedIdentity.AuditUserId(user));
        return Results.Ok(result);
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
        sc.ThresholdElectionEffectiveFrom,
        sc.PriorYearClass,
        sc.DecisionInputFingerprintSha256,
        sc.OverrideRequiresRereview
    };
}

public record SizeClassificationInput(
    decimal Turnover,
    decimal BalanceSheetTotal,
    int AvgEmployees,
    CompanySizeClass? PriorYearClass,
    DateOnly? ThresholdElectionEffectiveFrom = null);
public record SizeClassificationOverrideInput(
    CompanySizeClass OverrideClass,
    string Reason,
    byte[] EvidenceArtifact,
    string EvidenceSha256);
public record FilingRegimeInput(ElectedRegime? ElectedRegime);
public record MemberAuditNoticeInput(bool Received, DateOnly? NoticeDate);
