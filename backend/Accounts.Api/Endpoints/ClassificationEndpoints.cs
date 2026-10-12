using System.Text.Json.Serialization;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Mvc;
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

        // Read-only preflight: which earlier periods must be classified before this one (s.280A)
        group.MapGet("/classification/prerequisites", GetClassificationPrerequisitesEndpointAsync)
            .Produces<ClassificationPrerequisites>();

        // Record figures keyed in from a preceding period's filed accounts (provenance-marked root).
        group.MapPut("/classification/historic-anchor", RecordHistoricAnchorEndpointAsync)
            .Produces<SizeClassification>();

        // Retire those figures (Owner only): the evidence is kept, the period is classified normally again.
        group.MapPost("/classification/historic-anchor/retire", RetireHistoricAnchorEndpointAsync)
            .Produces<SizeClassification>();

        // Archived (retired or replaced) historic evidence, and its PDF. Owner, Reviewer and Accountant.
        group.MapGet("/classification/historic-anchor/archive", ListHistoricAnchorEvidenceEndpointAsync)
            .Produces<IReadOnlyList<HistoricAnchorEvidenceSummary>>();
        group.MapGet("/classification/historic-anchor/archive/{recordId:int}/evidence", DownloadHistoricAnchorEvidenceEndpointAsync)
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf");

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
        group.MapPut("/member-audit-notice", SaveMemberAuditNoticeEndpointAsync);
    }

    /// <summary>
    /// Records a members' audit notice (s.334) and re-determines the period's filing regime, which
    /// folds the notice into audit exemption. When the regime loses its exemption, a CRO approval
    /// given on the audit-exempt basis is revoked in the same unit of work, so readiness and the
    /// release gate agree that the signed auditor's report is now required.
    /// </summary>
    public static async Task<IResult> SaveMemberAuditNoticeEndpointAsync(
        int companyId,
        int periodId,
        MemberAuditNoticeInput input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context,
        ApiAccessService apiAccess,
        FilingRegimeService regimes,
        [FromServices] AccountingConcurrencyCoordinator? concurrency = null)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
            return denied;

        concurrency ??= new AccountingConcurrencyCoordinator(db);
        await using var lease = await concurrency.AcquirePeriodAsync(companyId, periodId, context.RequestAborted);
        var period = await db.AccountingPeriods
            .Include(p => p.FilingRegime)
            .Include(p => p.CroFilingPackage)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId);
        if (period == null) return Results.NotFound();

        var oldValue = new
        {
            period.MemberAuditNoticeReceived,
            period.MemberAuditNoticeDate,
            period.FilingRegime?.AuditExempt
        };
        period.MemberAuditNoticeReceived = input.Received;
        period.MemberAuditNoticeDate = input.NoticeDate;

        var wasAuditExempt = period.FilingRegime?.AuditExempt == true;
        if (period.FilingRegime is { } regime)
        {
            try
            {
                // Saves the notice together with the re-determined regime (same tracked period).
                await regimes.DetermineAsync(companyId, periodId, regime.ElectedRegime, AuditUserId(context));
            }
            catch (BusinessRuleException)
            {
                // The regime cannot be re-determined until the size classification is re-run. A
                // members' notice removes the exemption regardless (s.334), so never leave it claimed;
                // a withdrawn notice leaves the regime as it is until it is re-determined.
                if (input.Received)
                    regime.AuditExempt = false;
            }
        }

        var croApprovalRevoked = wasAuditExempt
            && period.FilingRegime?.AuditExempt == false
            && FilingReleaseGate.RevokeCroApprovalForLostAuditExemption(period.CroFilingPackage);
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
                period.MemberAuditNoticeDate,
                period.FilingRegime?.AuditExempt,
                CroApprovalRevoked = croApprovalRevoked
            },
            AuditUserId(context));
        await lease.CommitIfOwnedAsync(context.RequestAborted);
        return Results.Ok(new { period.MemberAuditNoticeReceived, period.MemberAuditNoticeDate });
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

    /// <summary>
    /// Read-only: whether every earlier period has the current size decision a later year's test
    /// needs (Companies Act 2014, s.280A), and which period to classify first if not.
    /// </summary>
    public static async Task<IResult> GetClassificationPrerequisitesEndpointAsync(
        int companyId,
        int periodId,
        SizeClassificationService service,
        AccountsDbContext db,
        HttpContext context)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        return Results.Ok(await service.GetPrerequisitesAsync(companyId, periodId));
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
        // Historic figures are replaced only through the evidence-bound historic-anchor route.
        if (sc is not null && sc.InputBasis == SizeInputBasis.HistoricAnchor)
            throw new BusinessRuleException(SizeClassificationService.HistoricAnchorNotATargetMessage);
        object? oldValue = sc is null ? null : SizeClassificationInputSnapshot(sc);
        if (sc == null)
        {
            sc = new SizeClassification { PeriodId = period.Id };
            db.SizeClassifications.Add(sc);
            period.SizeClassification = sc;
        }

        // The period's own figures replace retired historic figures: the row becomes an ordinary computed
        // row, undecided until classified, and keeps the retired evidence and its retirement record.
        var replacesRetiredFigures = sc.InputBasis == SizeInputBasis.RetiredHistoricAnchor;
        if (replacesRetiredFigures)
            sc.InputBasis = SizeInputBasis.Computed;
        var changed = replacesRetiredFigures
            || sc.Turnover != input.Turnover
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

    /// <summary>
    /// Records figures keyed in from a preceding period's filed accounts (with the PDF evidence) as
    /// the root of the next period's s.280A test. Owner/Reviewer only, with recent authenticator
    /// verification (RecentAuthenticationMiddleware); the attester is the authenticated principal.
    /// </summary>
    public static async Task<IResult> RecordHistoricAnchorEndpointAsync(
        int companyId,
        int periodId,
        HistoricAnchorInput input,
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
        var result = await service.RecordHistoricAnchorAsync(
            companyId,
            periodId,
            new HistoricSizeFiguresRequest(
                input.Turnover,
                input.BalanceSheetTotal,
                input.AvgEmployees,
                input.ThresholdElectionEffectiveFrom,
                input.SourceReference,
                input.EvidenceArtifact,
                input.EvidenceSha256,
                input.AttestedEffectiveClass),
            user.Role,
            user.DisplayName,
            AuthenticatedIdentity.AuditUserId(user));
        return Results.Ok(result);
    }

    /// <summary>
    /// Retires a period's historic figures. Owner only, with recent authenticator verification
    /// (RecentAuthenticationMiddleware); the evidence is kept and the retirer is the principal.
    /// </summary>
    public static async Task<IResult> RetireHistoricAnchorEndpointAsync(
        int companyId,
        int periodId,
        HistoricAnchorRetirementInput input,
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
        if (user.Role is not "Owner")
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        var result = await service.RetireHistoricAnchorAsync(
            companyId,
            periodId,
            input.Reason,
            user.Role,
            user.DisplayName,
            AuthenticatedIdentity.AuditUserId(user));
        return Results.Ok(result);
    }

    /// <summary>
    /// Lists the period's archived historic evidence (retired or replaced), newest first, without the
    /// PDF bytes. Owner, Reviewer and Accountant only.
    /// </summary>
    public static async Task<IResult> ListHistoricAnchorEvidenceEndpointAsync(
        int companyId,
        int periodId,
        AccountsDbContext db,
        HttpContext context)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();
        if (AuthContext.GetUser(context) is { Role: "Client" })
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        IReadOnlyList<HistoricAnchorEvidenceSummary> records = await db.HistoricAnchorEvidenceRecords
            .AsNoTracking()
            .Where(record => record.PeriodId == periodId && record.Period.CompanyId == companyId)
            .OrderByDescending(record => record.ArchivedAt)
            .ThenByDescending(record => record.Id)
            .Select(record => new HistoricAnchorEvidenceSummary(
                record.Id,
                record.Event,
                record.ArchivedAt,
                record.ArchivedBy,
                record.Turnover,
                record.BalanceSheetTotal,
                record.AvgEmployees,
                record.SourceReference,
                record.EvidenceSha256,
                record.AttestedBy,
                record.AttestedRole,
                record.AttestedAt,
                record.AttestedEffectiveClass,
                record.RetiredAt,
                record.RetiredBy,
                record.RetiredReason))
            .ToListAsync();
        return Results.Ok(records);
    }

    /// <summary>Downloads the PDF of an archived historic evidence record. Owner, Reviewer and Accountant only.</summary>
    public static async Task<IResult> DownloadHistoricAnchorEvidenceEndpointAsync(
        int companyId,
        int periodId,
        int recordId,
        AccountsDbContext db,
        HttpContext context)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();
        if (AuthContext.GetUser(context) is { Role: "Client" })
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        var record = await db.HistoricAnchorEvidenceRecords
            .AsNoTracking()
            .Where(candidate => candidate.Id == recordId && candidate.PeriodId == periodId && candidate.Period.CompanyId == companyId)
            .Select(candidate => new { candidate.EvidenceArtifact })
            .FirstOrDefaultAsync();
        return record is null
            ? Results.NotFound()
            : TypedResults.File(record.EvidenceArtifact, "application/pdf", $"historic-figures-period-{periodId}-{recordId}.pdf");
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
public record HistoricAnchorInput(
    decimal Turnover,
    decimal BalanceSheetTotal,
    int AvgEmployees,
    string SourceReference,
    byte[] EvidenceArtifact,
    string EvidenceSha256,
    CompanySizeClass? AttestedEffectiveClass = null,
    DateOnly? ThresholdElectionEffectiveFrom = null);
public record HistoricAnchorRetirementInput(string? Reason);
/// <summary>An archived (retired or replaced) historic evidence record, without its PDF bytes.</summary>
public sealed record HistoricAnchorEvidenceSummary(
    int Id,
    HistoricAnchorEvidenceEvent Event,
    DateTime ArchivedAt,
    string ArchivedBy,
    decimal Turnover,
    decimal BalanceSheetTotal,
    int AvgEmployees,
    string SourceReference,
    string EvidenceSha256,
    string AttestedBy,
    string AttestedRole,
    DateTime AttestedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] CompanySizeClass? AttestedEffectiveClass,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DateTime? RetiredAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? RetiredBy,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? RetiredReason);
public record FilingRegimeInput(ElectedRegime? ElectedRegime);
public record MemberAuditNoticeInput(bool Received, DateOnly? NoticeDate);
