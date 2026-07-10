using Accounts.Api.Data;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Accounts.Api.Endpoints;

/// <summary>
/// Append-only manual CRO/Revenue handoff evidence. These endpoints deliberately expose no submit
/// operation and cannot communicate with CORE or ROS.
/// </summary>
public static class ExternalFilingHandoffEndpoints
{
    private enum AccessKind
    {
        Read,
        Prepare,
        Review
    }

    public sealed record AuthorityRequest(
        ExternalFilingWorkflow Workflow,
        ExternalFilingAuthorityKind Kind,
        string LegalName,
        string? PracticeName,
        string? MaskedPresenterOrTain,
        string AuthorityScope,
        string EngagementReference,
        string ExternalAuthorityReference,
        DateTime EffectiveFromUtc,
        DateTime? EffectiveUntilUtc,
        byte[] EvidenceArtifact,
        string EvidenceSha256,
        string EvidenceMediaType,
        string EvidenceFileName)
    {
        public ExternalFilingHandoffService.AuthorityInput ToServiceInput() => new(
            Workflow,
            Kind,
            LegalName,
            PracticeName,
            MaskedPresenterOrTain,
            AuthorityScope,
            EngagementReference,
            ExternalAuthorityReference,
            EffectiveFromUtc,
            EffectiveUntilUtc,
            EvidenceArtifact,
            EvidenceSha256,
            EvidenceMediaType,
            EvidenceFileName);
    }

    public sealed record AuthorityRevocationRequest(string Reason);

    public static void MapExternalFilingHandoffEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/companies/{companyId:int}/periods/{periodId:int}/external-filing-handoff")
            .WithTags("External Filing Handoff");

        group.MapGet("/workspace", GetWorkspaceEndpointAsync)
            .WithName("GetExternalFilingHandoffWorkspace")
            .WithSummary("Read the scoped immutable CRO/Revenue manual-handoff workspace")
            .Produces<ExternalFilingHandoffService.Workspace>()
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/authorities", RecordAuthorityEndpointAsync)
            .WithName("RecordExternalFilingAuthority")
            .WithSummary("Append reviewed CRO presenter or ROS agent authority evidence")
            .Accepts<AuthorityRequest>("application/json")
            .Produces<ExternalFilingAuthoritySnapshot>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapPost("/authorities/{authorityId:long}/revoke", RevokeAuthorityEndpointAsync)
            .WithName("RevokeExternalFilingAuthority")
            .WithSummary("Append a revocation linked to the current authority record")
            .Accepts<AuthorityRevocationRequest>("application/json")
            .Produces<ExternalFilingAuthoritySnapshot>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapPost("/cro/snapshots", GenerateCroSnapshotEndpointAsync)
            .WithName("GenerateCroB1HandoffSnapshot")
            .WithSummary("Append an exact B1 manual-handoff snapshot or linked amendment")
            .Accepts<ExternalFilingHandoffService.CroSnapshotInput>("application/json")
            .Produces<ExternalFilingHandoffService.SnapshotResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapPost("/revenue/snapshots", GenerateRevenueSnapshotEndpointAsync)
            .WithName("GenerateRevenueCt1SupportHandoffSnapshot")
            .WithSummary("Append an exact support-only CT1/iXBRL handoff snapshot or linked amendment")
            .Accepts<ExternalFilingHandoffService.RevenueSnapshotInput>("application/json")
            .Produces<ExternalFilingHandoffService.SnapshotResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapGet("/snapshots/{snapshotId:guid}/artifact", DownloadArtifactEndpointAsync)
            .WithName("DownloadExternalFilingHandoffArtifact")
            .WithSummary("Download the exact retained immutable handoff JSON bytes")
            .Produces(StatusCodes.Status200OK, contentType: "application/json")
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/snapshots/{snapshotId:guid}/outcomes", RecordOutcomeEndpointAsync)
            .WithName("RecordExternalFilingHandoffOutcome")
            .WithSummary("Append internal readiness or a genuine externally evidenced outcome")
            .Accepts<ExternalFilingHandoffService.OutcomeCommand>("application/json")
            .Produces<ExternalFilingHandoffService.OutcomeResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
    }

    public static async Task<IResult> GetWorkspaceEndpointAsync(
        int companyId,
        int periodId,
        ExternalFilingHandoffService service,
        AccountsDbContext db,
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        if (await RequireAccessAsync(companyId, periodId, db, context, AccessKind.Read) is { } denied)
            return denied;

        SetManualOnlyHeaders(context);
        return Results.Ok(await service.GetWorkspaceAsync(companyId, periodId, cancellationToken));
    }

    public static async Task<IResult> RecordAuthorityEndpointAsync(
        int companyId,
        int periodId,
        AuthorityRequest input,
        ExternalFilingHandoffService service,
        AccountsDbContext db,
        HttpContext context,
        IdempotencyService idempotency,
        CancellationToken cancellationToken = default)
    {
        if (await RequireAccessAsync(companyId, periodId, db, context, AccessKind.Review) is { } denied)
            return denied;

        var actor = Actor(context);
        var command = await IdempotencyHttpContract.ExecuteAsync(
            context,
            idempotency,
            AuthContext.RequireUser(context),
            IdempotencyOperations.ExternalFilingAuthorityRecord,
            new { companyId, periodId, input },
            async token =>
            {
                var result = await service.RecordAuthorityAsync(companyId, periodId, input.ToServiceInput(), actor, token);
                return new IdempotencyOperationOutcome<ExternalFilingAuthoritySnapshot>(
                    result,
                    nameof(ExternalFilingAuthoritySnapshot),
                    result.AuthorityId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    StatusCodes.Status201Created);
            });
        SetManualOnlyHeaders(context);
        return command.Error ?? IdempotencyHttpContract.JsonResult(
            command.Execution!,
            $"/api/companies/{companyId}/periods/{periodId}/external-filing-handoff/workspace");
    }

    public static async Task<IResult> RevokeAuthorityEndpointAsync(
        int companyId,
        int periodId,
        long authorityId,
        AuthorityRevocationRequest input,
        ExternalFilingHandoffService service,
        AccountsDbContext db,
        HttpContext context,
        IdempotencyService idempotency,
        CancellationToken cancellationToken = default)
    {
        if (await RequireAccessAsync(companyId, periodId, db, context, AccessKind.Review) is { } denied)
            return denied;

        var actor = Actor(context);
        var command = await IdempotencyHttpContract.ExecuteAsync(
            context,
            idempotency,
            AuthContext.RequireUser(context),
            IdempotencyOperations.ExternalFilingAuthorityRevoke,
            new { companyId, periodId, authorityId, input.Reason },
            async token =>
            {
                var result = await service.RevokeAuthorityAsync(companyId, periodId, authorityId, input.Reason, actor, token);
                return new IdempotencyOperationOutcome<ExternalFilingAuthoritySnapshot>(
                    result,
                    nameof(ExternalFilingAuthoritySnapshot),
                    result.AuthorityId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    StatusCodes.Status201Created);
            });
        SetManualOnlyHeaders(context);
        return command.Error ?? IdempotencyHttpContract.JsonResult(
            command.Execution!,
            $"/api/companies/{companyId}/periods/{periodId}/external-filing-handoff/workspace");
    }

    public static async Task<IResult> GenerateCroSnapshotEndpointAsync(
        int companyId,
        int periodId,
        ExternalFilingHandoffService.CroSnapshotInput input,
        ExternalFilingHandoffService service,
        AccountsDbContext db,
        HttpContext context,
        IdempotencyService idempotency,
        CancellationToken cancellationToken = default) =>
        await GenerateSnapshotAsync(
            companyId,
            periodId,
            input,
            ExternalFilingWorkflow.CroB1,
            service,
            db,
            context,
            idempotency,
            (actor, token) => service.GenerateCroSnapshotAsync(companyId, periodId, input, actor, token));

    public static async Task<IResult> GenerateRevenueSnapshotEndpointAsync(
        int companyId,
        int periodId,
        ExternalFilingHandoffService.RevenueSnapshotInput input,
        ExternalFilingHandoffService service,
        AccountsDbContext db,
        HttpContext context,
        IdempotencyService idempotency,
        CancellationToken cancellationToken = default) =>
        await GenerateSnapshotAsync(
            companyId,
            periodId,
            input,
            ExternalFilingWorkflow.RevenueCt1Support,
            service,
            db,
            context,
            idempotency,
            (actor, token) => service.GenerateRevenueSnapshotAsync(companyId, periodId, input, actor, token));

    public static async Task<IResult> DownloadArtifactEndpointAsync(
        int companyId,
        int periodId,
        Guid snapshotId,
        ExternalFilingHandoffService service,
        AccountsDbContext db,
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        if (await RequireAccessAsync(companyId, periodId, db, context, AccessKind.Read) is { } denied)
            return denied;

        var artifact = await service.GetArtifactAsync(companyId, periodId, snapshotId, cancellationToken);
        SetManualOnlyHeaders(context);
        context.Response.Headers["X-Artifact-SHA256"] = artifact.Sha256;
        context.Response.Headers["Cache-Control"] = "private, no-store";
        return Results.File(
            artifact.Bytes,
            "application/json",
            $"external-filing-handoff-{snapshotId:D}.json",
            enableRangeProcessing: false);
    }

    public static async Task<IResult> RecordOutcomeEndpointAsync(
        int companyId,
        int periodId,
        Guid snapshotId,
        ExternalFilingHandoffService.OutcomeCommand input,
        ExternalFilingHandoffService service,
        AccountsDbContext db,
        HttpContext context,
        IdempotencyService idempotency,
        CancellationToken cancellationToken = default)
    {
        if (await RequireAccessAsync(companyId, periodId, db, context, AccessKind.Review) is { } denied)
            return denied;

        var actor = Actor(context);
        var command = await IdempotencyHttpContract.ExecuteAsync(
            context,
            idempotency,
            AuthContext.RequireUser(context),
            IdempotencyOperations.ExternalFilingOutcome,
            new { companyId, periodId, snapshotId, input },
            async token =>
            {
                var result = await service.RecordOutcomeAsync(companyId, periodId, snapshotId, input, actor, token);
                return new IdempotencyOperationOutcome<ExternalFilingHandoffService.OutcomeResponse>(
                    result,
                    nameof(ExternalFilingHandoffService.OutcomeResponse),
                    result.EventId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    StatusCodes.Status201Created);
            });
        SetManualOnlyHeaders(context);
        return command.Error ?? IdempotencyHttpContract.JsonResult(
            command.Execution!,
            $"/api/companies/{companyId}/periods/{periodId}/external-filing-handoff/workspace");
    }

    private static async Task<IResult> GenerateSnapshotAsync<TInput>(
        int companyId,
        int periodId,
        TInput input,
        ExternalFilingWorkflow workflow,
        ExternalFilingHandoffService service,
        AccountsDbContext db,
        HttpContext context,
        IdempotencyService idempotency,
        Func<ExternalFilingActor, CancellationToken, Task<ExternalFilingHandoffService.SnapshotResponse>> action)
    {
        if (await RequireAccessAsync(companyId, periodId, db, context, AccessKind.Prepare) is { } denied)
            return denied;

        var operation = workflow == ExternalFilingWorkflow.CroB1
            ? IdempotencyOperations.ExternalFilingCroSnapshot
            : IdempotencyOperations.ExternalFilingRevenueSnapshot;
        var actor = Actor(context);
        var command = await IdempotencyHttpContract.ExecuteAsync(
            context,
            idempotency,
            AuthContext.RequireUser(context),
            operation,
            new { companyId, periodId, input },
            async token =>
            {
                var result = await action(actor, token);
                return new IdempotencyOperationOutcome<ExternalFilingHandoffService.SnapshotResponse>(
                    result,
                    nameof(ExternalFilingHandoffService.SnapshotResponse),
                    result.Document.SnapshotId.ToString("D"),
                    StatusCodes.Status201Created);
            });
        SetManualOnlyHeaders(context);
        return command.Error ?? IdempotencyHttpContract.JsonResult(
            command.Execution!,
            $"/api/companies/{companyId}/periods/{periodId}/external-filing-handoff/snapshots/{command.Execution!.Result.Document.SnapshotId:D}/artifact");
    }

    private static async Task<IResult?> RequireAccessAsync(
        int companyId,
        int periodId,
        AccountsDbContext db,
        HttpContext context,
        AccessKind access)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        var actor = AuthContext.RequireUser(context);
        var role = actor.Role.Trim();
        var allowed = access switch
        {
            AccessKind.Read => role.Equals("Owner", StringComparison.OrdinalIgnoreCase)
                || role.Equals("Accountant", StringComparison.OrdinalIgnoreCase)
                || role.Equals("Reviewer", StringComparison.OrdinalIgnoreCase),
            AccessKind.Prepare => role.Equals("Owner", StringComparison.OrdinalIgnoreCase)
                || role.Equals("Accountant", StringComparison.OrdinalIgnoreCase),
            AccessKind.Review => role.Equals("Owner", StringComparison.OrdinalIgnoreCase)
                || role.Equals("Reviewer", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
        if (allowed)
            return null;

        var message = access switch
        {
            AccessKind.Read => "External filing handoff evidence is limited to Owner, Accountant, and Reviewer users.",
            AccessKind.Prepare => "Only an Owner or Accountant can prepare an immutable handoff snapshot.",
            AccessKind.Review => "Only an Owner or Reviewer can govern authority evidence or record external outcomes.",
            _ => "This external filing handoff action is not authorised."
        };
        return Results.Json(
            new { error = message, correlationId = context.TraceIdentifier },
            statusCode: StatusCodes.Status403Forbidden);
    }

    private static ExternalFilingActor Actor(HttpContext context)
    {
        var actor = AuthContext.RequireUser(context);
        return new ExternalFilingActor(
            AuthenticatedIdentity.AuditUserId(actor),
            AuthenticatedIdentity.ReviewerDisplayName(actor),
            actor.Role);
    }

    private static void SetManualOnlyHeaders(HttpContext context)
    {
        context.Response.Headers["X-External-Filing-Handoff-Only"] = "true";
        context.Response.Headers["X-Direct-CRO-Submission-Supported"] = "false";
        context.Response.Headers["X-Direct-ROS-Submission-Supported"] = "false";
        context.Response.Headers["X-Is-Complete-External-Return"] = "false";
    }
}
