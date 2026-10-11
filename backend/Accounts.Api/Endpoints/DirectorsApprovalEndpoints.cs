using System.Text.Json.Serialization;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Endpoints;

public sealed record DirectorOption(int OfficerId, string Name);

public sealed record DirectorsApprovalPageSummary(
    int Id,
    DateOnly ApprovalDate,
    string FirstDirectorName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? SecondDirectorName,
    int DirectorsInOfficeCount,
    string ResolutionReference,
    string PageSha256,
    string AccountsPdfSha256,
    string RecordSha256,
    DateTime GeneratedAtUtc)
{
    public static DirectorsApprovalPageSummary From(DirectorsApprovalPage page) => new(
        page.Id,
        page.ApprovalDate,
        page.FirstDirectorName,
        page.SecondDirectorName,
        page.DirectorsInOfficeCount,
        page.ResolutionReference,
        page.PageSha256,
        page.AccountsPdfSha256,
        page.RecordSha256,
        page.GeneratedAtUtc);
}

public sealed record DirectorsApprovalSummary(
    int EventId,
    DirectorsApprovalPageSummary Page,
    string SignedPageSha256,
    string ResolutionSha256,
    string RecordedByDisplayName,
    DateTime RecordedAtUtc,
    string RecordSha256)
{
    public static DirectorsApprovalSummary From(CurrentDirectorsApproval current) => new(
        current.Approval.Id,
        DirectorsApprovalPageSummary.From(current.Page),
        current.Approval.SignedPageSha256 ?? string.Empty,
        current.Approval.ResolutionSha256 ?? string.Empty,
        current.Approval.RecordedByDisplayName,
        current.Approval.RecordedAtUtc,
        current.Approval.RecordSha256);
}

public sealed record DirectorsApprovalStateResponse(
    IReadOnlyList<DirectorOption> EligibleDirectors,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DirectorsApprovalPageSummary? LatestPage,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DirectorsApprovalSummary? Current,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DateOnly? PeriodApprovalDate);

public sealed record CroSigningPackInput(
    int DirectorOfficerId,
    int SecretaryOfficerId,
    DateTime SignedAtUtc,
    byte[] SignedArtifact,
    string SignedArtifactSha256);

public sealed record CroSigningPackSummary(
    string SignedByDirector,
    string SignedBySecretary,
    DateTime SignedAtUtc,
    string SignedPdfSha256);

public sealed record AuditorReportInput(
    string FirmName,
    string SignerName,
    string ProfessionalBody,
    string MembershipNumber,
    string ReportReference,
    DateTime SignedAtUtc,
    bool AcceptedByDirectors,
    byte[] ReportArtifact,
    string ReportArtifactSha256);

public sealed record AuditorReportSummary(
    string ReportReference,
    string FirmName,
    string SignerName,
    DateTime SignedAtUtc,
    string ReportSha256);

public sealed record AccountantReviewSummary(
    string ReviewerName,
    string ProfessionalBody,
    string MembershipNumber,
    DateOnly ReviewDate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ReviewLetterSha256,
    string State);

/// <summary>
/// Directors' s.324 approval, the executed CRO signing pack and the signed auditor report. Evidence
/// is recorded by Owner or Reviewer users with recent authentication; generating the approval page
/// decides nothing, so it is also open to Accountants (role routing is the guard).
/// </summary>
public static class DirectorsApprovalEndpoints
{
    private static readonly TimeZoneInfo Ireland = DeadlineService.ResolveIrelandTimeZone();

    public static void MapDirectorsApprovalEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/companies/{companyId:int}/periods/{periodId:int}/filing").WithTags("Directors Approval");

        group.MapGet("/directors-approval", GetStateEndpointAsync)
            .Produces<DirectorsApprovalStateResponse>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/directors-approval-page", GeneratePageEndpointAsync)
            .Produces<DirectorsApprovalPageSummary>();

        group.MapGet("/directors-approval-page/{pageId:int}", GetPagePdfEndpointAsync)
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/directors-approval", RecordApprovalEndpointAsync)
            .Produces<DirectorsApprovalSummary>();

        group.MapPost("/cro-signing-pack", RecordCroSigningPackEndpointAsync)
            .Produces<CroSigningPackSummary>();

        group.MapPost("/auditor-report", RecordAuditorReportEndpointAsync)
            .Produces<AuditorReportSummary>();

        group.MapPost("/accountant-review", RecordAccountantReviewEndpointAsync)
            .Produces<AccountantReviewSummary>();
    }

    public static async Task<IResult> GetStateEndpointAsync(
        int companyId,
        int periodId,
        DateOnly? onDate,
        DirectorsApprovalService service,
        AccountsDbContext db,
        HttpContext context)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        var period = await db.AccountingPeriods.AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId);
        if (period is null)
            return Results.NotFound();

        var eligibilityDate = onDate ?? period.ApprovalDate ?? period.PeriodEnd;
        var directors = (await service.DirectorsInOfficeAsync(companyId, eligibilityDate))
            .Select(d => new DirectorOption(d.Id, d.Name.Trim()))
            .ToList();
        var latestPage = await db.DirectorsApprovalPages.AsNoTracking()
            .Where(p => p.PeriodId == periodId)
            .OrderByDescending(p => p.Id)
            .FirstOrDefaultAsync();
        var current = await service.GetCurrentApprovalAsync(periodId);

        return Results.Ok(new DirectorsApprovalStateResponse(
            directors,
            latestPage is null ? null : DirectorsApprovalPageSummary.From(latestPage),
            current is null ? null : DirectorsApprovalSummary.From(current),
            period.ApprovalDate));
    }

    public static async Task<IResult> GeneratePageEndpointAsync(
        int companyId,
        int periodId,
        DirectorsApprovalPageInput input,
        DirectorsApprovalService service,
        FilingReadinessProfileService readiness,
        AccountsDbContext db,
        HttpContext context,
        ApiAccessService apiAccess)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
            return denied;

        await RequireSupportedPathAsync(readiness, companyId, periodId);
        var page = await service.GeneratePageAsync(companyId, periodId, input, AuthContext.RequireUser(context));
        return Results.Ok(DirectorsApprovalPageSummary.From(page));
    }

    public static async Task<IResult> GetPagePdfEndpointAsync(
        int companyId,
        int periodId,
        int pageId,
        AccountsDbContext db,
        HttpContext context)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        var page = await db.DirectorsApprovalPages.AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == pageId && p.PeriodId == periodId);
        if (page is null)
            return Results.NotFound();
        if (!DirectorsApprovalIntegrity.IsValid(page)
            || !string.Equals(DirectorsApprovalIntegrity.Sha256(page.PageArtifact), page.PageSha256, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("The approval page failed integrity verification; generate a new approval page.");

        return Results.File(page.PageArtifact, "application/pdf", $"directors_approval_page_{pageId}.pdf");
    }

    public static async Task<IResult> RecordApprovalEndpointAsync(
        int companyId,
        int periodId,
        DirectorsApprovalRecordInput input,
        DirectorsApprovalService service,
        FilingReadinessProfileService readiness,
        AccountsDbContext db,
        HttpContext context,
        ApiAccessService apiAccess)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
            return denied;

        await RequireSupportedPathAsync(readiness, companyId, periodId);
        await service.RecordApprovalAsync(companyId, periodId, input, AuthContext.RequireUser(context));
        var current = await service.GetCurrentApprovalAsync(periodId)
            ?? throw new BusinessRuleException("The approval was recorded but did not verify; contact support.");
        return Results.Ok(DirectorsApprovalSummary.From(current));
    }

    public static async Task<IResult> RecordCroSigningPackEndpointAsync(
        int companyId,
        int periodId,
        CroSigningPackInput input,
        FilingReleaseGate gate,
        AuditService audit,
        AccountsDbContext db,
        HttpContext context,
        ApiAccessService apiAccess)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
            return denied;

        if (input.SignedAtUtc.Kind != DateTimeKind.Utc)
            throw new BusinessRuleException("The signing time must be a UTC timestamp.");
        var signedOn = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(input.SignedAtUtc, Ireland));

        // Signatories are resolved from the company's own officer rows; names are never taken from the request.
        var officers = await db.CompanyOfficers.AsNoTracking()
            .Where(o => o.CompanyId == companyId)
            .ToListAsync();
        var director = RequireOfficerInOffice(officers, input.DirectorOfficerId, signedOn, "director",
            o => o.Role == OfficerRole.Director);
        var secretary = RequireOfficerInOffice(officers, input.SecretaryOfficerId, signedOn, "secretary",
            o => o.Role is OfficerRole.Secretary or OfficerRole.CompanySecretary);

        var user = AuthContext.RequireUser(context);
        var auditUserId = AuthenticatedIdentity.AuditUserId(user);
        var package = await gate.RecordVerifiedCroSignatureAsync(
            companyId,
            periodId,
            new CroSignatureEvidence(
                director.Name.Trim(),
                secretary.Name.Trim(),
                input.SignedAtUtc,
                input.SignedArtifact,
                input.SignedArtifactSha256),
            auditUserId);
        await audit.LogAsync(
            companyId,
            periodId,
            nameof(CroFilingPackage),
            package.Id,
            AuditEventCodes.CroSigningPackRecorded,
            null,
            new
            {
                DirectorOfficerId = director.Id,
                SecretaryOfficerId = secretary.Id,
                package.SignedByDirector,
                package.SignedBySecretary,
                package.SignedAt,
                package.SignedPdfSha256
            },
            auditUserId);
        return Results.Ok(new CroSigningPackSummary(
            package.SignedByDirector!,
            package.SignedBySecretary!,
            input.SignedAtUtc,
            package.SignedPdfSha256!));
    }

    public static async Task<IResult> RecordAuditorReportEndpointAsync(
        int companyId,
        int periodId,
        AuditorReportInput input,
        FilingReleaseGate gate,
        AuditService audit,
        AccountsDbContext db,
        HttpContext context,
        ApiAccessService apiAccess,
        TimeProvider time)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
            return denied;

        if (!input.AcceptedByDirectors)
            throw new BusinessRuleException("Confirm that the directors have accepted the auditor's report.");

        // The acceptance is attributed to the authenticated user at the server's time; never to request-supplied values.
        var user = AuthContext.RequireUser(context);
        var auditUserId = AuthenticatedIdentity.AuditUserId(user);
        var now = time.GetUtcNow().UtcDateTime;
        var reviewedAtUtc = new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc);
        var period = await gate.RecordVerifiedAuditorReportAsync(
            companyId,
            periodId,
            new AuditorReportEvidence(
                input.FirmName,
                input.SignerName,
                input.ProfessionalBody,
                input.MembershipNumber,
                input.ReportReference,
                input.SignedAtUtc,
                AuthenticatedIdentity.ReviewerDisplayName(user),
                reviewedAtUtc,
                "accepted",
                input.ReportArtifact,
                input.ReportArtifactSha256),
            auditUserId);
        await audit.LogAsync(
            companyId,
            periodId,
            nameof(AccountingPeriod),
            period.Id,
            AuditEventCodes.AuditorReportRecorded,
            null,
            new
            {
                period.AuditorsReportReference,
                period.AuditorsReportFirmName,
                period.AuditorsReportSignerName,
                period.AuditorsReportSignedAt,
                period.AuditorsReportSha256
            },
            auditUserId);
        return Results.Ok(new AuditorReportSummary(
            period.AuditorsReportReference!,
            period.AuditorsReportFirmName!,
            period.AuditorsReportSignerName!,
            input.SignedAtUtc,
            period.AuditorsReportSha256!));
    }

    public static async Task<IResult> RecordAccountantReviewEndpointAsync(
        int companyId,
        int periodId,
        AccountantReviewAttestationInput input,
        DirectorsApprovalService service,
        AccountsDbContext db,
        HttpContext context,
        ApiAccessService apiAccess)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
            return denied;

        // Optional attestation only: no filing path or gate depends on it, so no supported-path check.
        var package = await service.RecordAccountantReviewAsync(companyId, periodId, input, AuthContext.RequireUser(context));
        return Results.Ok(new AccountantReviewSummary(
            package.OptionalReviewerName!,
            package.ApproverProfessionalBody!,
            package.ApproverMembershipNumber!,
            input.ReviewDate,
            package.ApproverVerificationArtifactSha256,
            FilingReadinessProfileService.OptionalAccountantReviewState(package)));
    }

    private static async Task RequireSupportedPathAsync(FilingReadinessProfileService readiness, int companyId, int periodId)
    {
        var profile = await readiness.GetProfileAsync(companyId, periodId);
        if (!profile.SupportedPath)
            throw new BusinessRuleException("This company is outside the supported filing path.");
    }

    private static CompanyOfficer RequireOfficerInOffice(
        IReadOnlyList<CompanyOfficer> officers,
        int officerId,
        DateOnly onDate,
        string roleLabel,
        Func<CompanyOfficer, bool> hasRole)
    {
        var officer = officers.SingleOrDefault(o => o.Id == officerId)
            ?? throw new ResourceNotFoundException("Officer not found for this company.");
        if (!hasRole(officer)
            || (officer.AppointedDate is not null && officer.AppointedDate > onDate)
            || (officer.ResignedDate is not null && officer.ResignedDate < onDate))
            throw new BusinessRuleException($"{officer.Name.Trim()} was not a {roleLabel} in office on {onDate:yyyy-MM-dd}.");
        return officer;
    }
}
