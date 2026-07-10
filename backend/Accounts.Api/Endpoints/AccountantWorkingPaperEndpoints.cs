using Accounts.Api.Data;
using Accounts.Api.Services;

namespace Accounts.Api.Endpoints;

public static class AccountantWorkingPaperEndpoints
{
    public sealed record SectionResponse<T>(
        string OutputKind,
        bool IsFilingArtifact,
        bool DirectSubmissionSupported,
        bool QualifiedAccountantReviewRequired,
        string Warning,
        AccountantWorkingPaperService.ArtifactIdentity Identity,
        T Artifact);

    public static void MapAccountantWorkingPaperEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/companies/{companyId:int}/periods/{periodId:int}/working-papers")
            .WithTags("Internal Accountant Working Papers");

        group.MapPost("/generate", GenerateEndpointAsync);
        group.MapGet("", GetIndexEndpointAsync);
        group.MapGet("/lead-schedules", GetLeadSchedulesEndpointAsync);
        group.MapGet("/categorized-transactions", GetCategorizedTransactionsEndpointAsync);
        group.MapGet("/review-exceptions", GetReviewExceptionsEndpointAsync);
        group.MapGet("/adjusted-trial-balance", GetAdjustedTrialBalanceEndpointAsync);
        group.MapGet("/corporation-tax-bridge", GetCorporationTaxBridgeEndpointAsync);
    }

    public static async Task<IResult> GenerateEndpointAsync(
        int companyId,
        int periodId,
        AccountantWorkingPaperService service,
        AccountsDbContext db,
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        var access = await RequireAccessAsync(companyId, periodId, db, context);
        if (access is not null)
            return access;
        var actor = AuthContext.RequireUser(context);
        try
        {
            AccountantWorkingPaperService.EnsureCanGenerate(actor);
        }
        catch (WorkingPaperAccessDeniedException exception)
        {
            return Results.Json(
                new { error = exception.Message, correlationId = context.TraceIdentifier },
                statusCode: StatusCodes.Status403Forbidden);
        }
        var pack = await service.GenerateAndRetainAsync(
            companyId,
            periodId,
            actor,
            context.TraceIdentifier,
            cancellationToken);
        SetSupportOnlyHeaders(context, pack.Identity);
        return Results.Ok(pack);
    }

    public static async Task<IResult> GetIndexEndpointAsync(
        int companyId,
        int periodId,
        AccountantWorkingPaperService service,
        AccountsDbContext db,
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        var pack = await GetPackAsync(companyId, periodId, service, db, context, cancellationToken);
        if (pack.Result is not null)
            return pack.Result;
        SetSupportOnlyHeaders(context, pack.Pack!.Identity);
        return Results.Ok(pack.Pack);
    }

    public static async Task<IResult> GetLeadSchedulesEndpointAsync(
        int companyId,
        int periodId,
        AccountantWorkingPaperService service,
        AccountsDbContext db,
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        var pack = await GetPackAsync(companyId, periodId, service, db, context, cancellationToken);
        return pack.Result ?? Section(context, pack.Pack!, pack.Pack!.LeadSchedules);
    }

    public static async Task<IResult> GetCategorizedTransactionsEndpointAsync(
        int companyId,
        int periodId,
        AccountantWorkingPaperService service,
        AccountsDbContext db,
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        var pack = await GetPackAsync(companyId, periodId, service, db, context, cancellationToken);
        return pack.Result ?? Section(context, pack.Pack!, pack.Pack!.CategorizedTransactions);
    }

    public static async Task<IResult> GetReviewExceptionsEndpointAsync(
        int companyId,
        int periodId,
        AccountantWorkingPaperService service,
        AccountsDbContext db,
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        var pack = await GetPackAsync(companyId, periodId, service, db, context, cancellationToken);
        return pack.Result ?? Section(context, pack.Pack!, pack.Pack!.ReviewExceptions);
    }

    public static async Task<IResult> GetAdjustedTrialBalanceEndpointAsync(
        int companyId,
        int periodId,
        AccountantWorkingPaperService service,
        AccountsDbContext db,
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        var pack = await GetPackAsync(companyId, periodId, service, db, context, cancellationToken);
        return pack.Result ?? Section(context, pack.Pack!, pack.Pack!.AdjustedTrialBalance);
    }

    public static async Task<IResult> GetCorporationTaxBridgeEndpointAsync(
        int companyId,
        int periodId,
        AccountantWorkingPaperService service,
        AccountsDbContext db,
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        var pack = await GetPackAsync(companyId, periodId, service, db, context, cancellationToken);
        return pack.Result ?? Section(context, pack.Pack!, pack.Pack!.CorporationTaxBridge);
    }

    private static async Task<(AccountantWorkingPaperService.Pack? Pack, IResult? Result)> GetPackAsync(
        int companyId,
        int periodId,
        AccountantWorkingPaperService service,
        AccountsDbContext db,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var access = await RequireAccessAsync(companyId, periodId, db, context);
        if (access is not null)
            return (null, access);
        var pack = await service.GetLatestRetainedAsync(
            companyId,
            periodId,
            AuthContext.RequireUser(context),
            cancellationToken);
        return pack is null
            ? (null, Results.NotFound(new
            {
                error = "No retained working-paper pack exists for this period. Generate it before review.",
                outputKind = AccountantWorkingPaperService.OutputKind
            }))
            : (pack, null);
    }

    private static async Task<IResult?> RequireAccessAsync(
        int companyId,
        int periodId,
        AccountsDbContext db,
        HttpContext context)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();
        var actor = AuthContext.RequireUser(context);
        try
        {
            AccountantWorkingPaperService.EnsureInternalRole(actor);
        }
        catch (WorkingPaperAccessDeniedException exception)
        {
            return Results.Json(
                new { error = exception.Message, correlationId = context.TraceIdentifier },
                statusCode: StatusCodes.Status403Forbidden);
        }
        return null;
    }

    private static IResult Section<T>(
        HttpContext context,
        AccountantWorkingPaperService.Pack pack,
        T artifact)
    {
        SetSupportOnlyHeaders(context, pack.Identity);
        return Results.Ok(new SectionResponse<T>(
            pack.OutputKind,
            pack.IsFilingArtifact,
            pack.DirectSubmissionSupported,
            pack.QualifiedAccountantReviewRequired,
            pack.Warning,
            pack.Identity,
            artifact));
    }

    private static void SetSupportOnlyHeaders(
        HttpContext context,
        AccountantWorkingPaperService.ArtifactIdentity identity)
    {
        context.Response.Headers["X-Working-Paper-Output-Kind"] = AccountantWorkingPaperService.OutputKind;
        context.Response.Headers["X-Filing-Artifact"] = "false";
        context.Response.Headers["X-Direct-Submission-Supported"] = "false";
        context.Response.Headers["X-Qualified-Accountant-Review-Required"] = "true";
        context.Response.Headers["X-Working-Paper-Artifact-SHA256"] = identity.ArtifactSha256;
        context.Response.Headers["X-Working-Paper-Source-SHA256"] = identity.SourceDataSha256;
    }
}
