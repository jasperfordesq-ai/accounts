using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Accounts.Api.Endpoints;

public static class PrivacyEndpoints
{
    public sealed record ErasureRequest(string DecisionReason);

    public sealed record IncidentExerciseInput(
        string ExerciseKind,
        string ReleaseCandidate,
        string EnvironmentName,
        string ScenarioSha256,
        DateTime DetectedAtUtc,
        DateTime NotificationRoutedAtUtc,
        DateTime ContainedAtUtc,
        DateTime EvidencePreservedAtUtc,
        DateTime RecoveryVerifiedAtUtc,
        DateTime ReviewedAtUtc,
        string NotificationDecision,
        string EvidenceManifestSha256,
        string ReviewDecision,
        bool UsedSyntheticDataOnly,
        bool TenantIsolationVerified,
        bool AuditIntegrityVerified,
        bool FinancialIntegrityVerified);

    public static void MapPrivacyEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/privacy").WithTags("Privacy Governance");

        group.MapGet("/policy", GetPolicyEndpoint)
            .WithName("GetPrivacyRetentionPolicy")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden);
        group.MapPost("/subjects/{subjectUserId:int}/access-export", BuildAccessExportEndpointAsync)
            .WithName("BuildPrivacySubjectAccessExport")
            .WithSummary("Build a same-tenant subject inventory for controller review")
            .Produces(StatusCodes.Status200OK, contentType: "application/json")
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status428PreconditionRequired);
        group.MapPost("/subjects/{subjectUserId:int}/approved-erasure", ExecuteErasureEndpointAsync)
            .WithName("ExecuteApprovedPrivacyErasure")
            .WithSummary("Erase non-statutory identity data and retain explicit statutory overrides")
            .Accepts<ErasureRequest>("application/json")
            .Produces<PrivacyGovernanceService.ErasureDecision>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status428PreconditionRequired);
        group.MapPost("/retention/run", RunRetentionEndpointAsync)
            .WithName("RunPrivacyRetention")
            .Produces<PrivacyGovernanceService.RetentionRunResult>()
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status428PreconditionRequired);
        group.MapPost("/incident-exercises", RecordIncidentExerciseEndpointAsync)
            .WithName("RecordPrivacyIncidentExercise")
            .Accepts<IncidentExerciseInput>("application/json")
            .Produces<PrivacyIncidentExercise>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status428PreconditionRequired);
    }

    public static IResult GetPolicyEndpoint(
        HttpContext context,
        IOptions<PrivacyGovernanceConfig> options)
    {
        if (!CanReview(context))
            return Forbidden(context);
        var policy = options.Value;
        return Results.Ok(new
        {
            schemaVersion = "privacy-retention-policy-v1",
            policy.LoginSecurityEventRetentionDays,
            policy.TerminalIdentityArtifactRetentionHours,
            policy.UsedRecoveryCodeRetentionDays,
            policy.SubjectRequestMetadataRetentionYears,
            policy.StatutoryRecordMinimumYears,
            policy.RetentionWorkerIntervalHours,
            statutoryBasis = PrivacyGovernanceService.CompaniesActRetentionBasis,
            sourceUrls = new[]
            {
                "https://www.dataprotection.ie/en/faqs/responsibilities-data-controllers/how-long-should-personal-data-be-held-meet-obligations-imposed-gdpr",
                "https://www.dataprotection.ie/en/organisations/data-protection-basics/principles-data-protection",
                "https://www.irishstatutebook.ie/eli/2014/act/38/section/285/enacted/",
                "https://www.revenue.ie/en/starting-a-business/starting-a-business/keeping-records.aspx"
            },
            directRegulatorNotificationSupported = false,
            controllerDecisionRequired = true
        });
    }

    public static async Task<IResult> BuildAccessExportEndpointAsync(
        int subjectUserId,
        PrivacyGovernanceService service,
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        if (!IsOwner(context))
            return Forbidden(context);
        var actor = AuthContext.RequireUser(context);
        try
        {
            var artifact = await service.BuildSubjectAccessExportAsync(
                actor.TenantId,
                subjectUserId,
                actor.UserId,
                cancellationToken);
            context.Response.Headers["X-Privacy-Request-ID"] = artifact.RequestId.ToString("D");
            context.Response.Headers["X-Artifact-SHA256"] = artifact.Sha256;
            context.Response.Headers.CacheControl = "private, no-store";
            return Results.File(
                artifact.Bytes,
                "application/json",
                $"subject-access-{artifact.RequestId:D}.json",
                enableRangeProcessing: false);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
    }

    public static async Task<IResult> ExecuteErasureEndpointAsync(
        int subjectUserId,
        [FromBody] ErasureRequest input,
        PrivacyGovernanceService service,
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        if (!IsOwner(context))
            return Forbidden(context);
        var actor = AuthContext.RequireUser(context);
        try
        {
            var decision = await service.ExecuteApprovedErasureAsync(
                actor.TenantId,
                subjectUserId,
                actor.UserId,
                input.DecisionReason,
                cancellationToken);
            return Results.Ok(decision);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (BusinessRuleException error)
        {
            return Results.Conflict(new { error = error.Message, correlationId = context.TraceIdentifier });
        }
    }

    public static async Task<IResult> RunRetentionEndpointAsync(
        PrivacyGovernanceService service,
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        if (!IsOwner(context))
            return Forbidden(context);
        var user = AuthContext.GetUser(context)!;
        return Results.Ok(await service.RunRetentionForTenantAsync(user.TenantId, cancellationToken));
    }

    public static async Task<IResult> RecordIncidentExerciseEndpointAsync(
        [FromBody] IncidentExerciseInput input,
        PrivacyGovernanceService service,
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        if (!CanReview(context))
            return Forbidden(context);
        var actor = AuthContext.RequireUser(context);
        try
        {
            var exercise = new PrivacyIncidentExercise
            {
                TenantId = actor.TenantId,
                ExerciseKind = input.ExerciseKind,
                ReleaseCandidate = input.ReleaseCandidate,
                EnvironmentName = input.EnvironmentName,
                ScenarioSha256 = input.ScenarioSha256,
                DetectedAtUtc = input.DetectedAtUtc,
                NotificationRoutedAtUtc = input.NotificationRoutedAtUtc,
                ContainedAtUtc = input.ContainedAtUtc,
                EvidencePreservedAtUtc = input.EvidencePreservedAtUtc,
                RecoveryVerifiedAtUtc = input.RecoveryVerifiedAtUtc,
                ReviewedAtUtc = input.ReviewedAtUtc,
                ReviewedByUserId = actor.UserId,
                NotificationDecision = input.NotificationDecision,
                EvidenceManifestSha256 = input.EvidenceManifestSha256,
                ReviewDecision = input.ReviewDecision,
                UsedSyntheticDataOnly = input.UsedSyntheticDataOnly,
                TenantIsolationVerified = input.TenantIsolationVerified,
                AuditIntegrityVerified = input.AuditIntegrityVerified,
                FinancialIntegrityVerified = input.FinancialIntegrityVerified
            };
            var result = await service.RecordIncidentExerciseAsync(
                exercise,
                actor.TenantId,
                actor.UserId,
                cancellationToken);
            return Results.Created($"/api/privacy/incident-exercises/{result.Id:D}", result);
        }
        catch (BusinessRuleException error)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["exercise"] = [error.Message]
            });
        }
    }

    private static bool IsOwner(HttpContext context) =>
        string.Equals(AuthContext.GetUser(context)?.Role, "Owner", StringComparison.OrdinalIgnoreCase);

    private static bool CanReview(HttpContext context)
    {
        var role = AuthContext.GetUser(context)?.Role;
        return string.Equals(role, "Owner", StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, "Reviewer", StringComparison.OrdinalIgnoreCase);
    }

    private static IResult Forbidden(HttpContext context) => Results.Json(
        new
        {
            error = "This privacy-governance operation requires a same-tenant Owner or authorized Reviewer.",
            correlationId = context.TraceIdentifier
        },
        statusCode: StatusCodes.Status403Forbidden);
}
