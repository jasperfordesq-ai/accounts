using Accounts.Api.Services;

namespace Accounts.Api.Endpoints;

public static class OperationsEndpoints
{
    public static void MapOperationsEndpoints(this WebApplication app)
    {
        var operations = app.MapGroup("/api/operations");
        operations.MapGet("/deadline-risk", GetDeadlineRiskAsync)
            .WithName("GetFirmDeadlineRisk")
            .Produces<IReadOnlyList<DeadlineRiskQueueItem>>()
            .Produces(StatusCodes.Status403Forbidden);
        operations.MapPost("/deadline-reminders/run", RunDeadlineRemindersAsync)
            .WithName("RunDeadlineReminders")
            .Produces<DeadlineReminderRunResult>()
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status428PreconditionRequired);
        operations.MapPost("/deadline-reminders/{outboxId:guid}/retry", RetryDeadlineReminderAsync)
            .WithName("RetryDeadlineReminder")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status428PreconditionRequired);

        app.MapGet("/api/system/platform-metrics", GetPlatformMetricsAsync)
            .WithName("GetRestrictedPlatformMetrics")
            .Produces<PlatformMetricsSnapshot>()
            .Produces(StatusCodes.Status403Forbidden);
    }

    internal static async Task<IResult> GetDeadlineRiskAsync(
        DeadlineReminderService service,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var user = AuthContext.GetUser(context);
        if (user is null || !IsPracticeRole(user.Role)) return Forbidden(context);
        return Results.Ok(await service.GetAtRiskQueueAsync(user.TenantId, cancellationToken));
    }

    internal static async Task<IResult> RunDeadlineRemindersAsync(
        DeadlineReminderService service,
        AuditService audit,
        TimeProvider timeProvider,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var user = AuthContext.GetUser(context);
        if (user is null || !RoleIs(user.Role, "Owner")) return Forbidden(context);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        now = new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc);
        var result = await service.RunTenantAsync(user.TenantId, now, "manual", cancellationToken);
        await audit.LogAsync(
            null,
            null,
            nameof(DeadlineReminderRunResult),
            0,
            AuditEventCodes.DeadlineReminderRunRequested,
            newValue: new
            {
                result.JobRunId,
                result.ExaminedCount,
                result.EnqueuedCount,
                result.DeliveredCount,
                result.FailedCount,
                result.CancelledCount,
                result.Status,
                result.EvidenceSha256
            },
            userId: AuthenticatedIdentity.AuditUserId(user),
            tenantId: user.TenantId,
            actorDisplayName: user.DisplayName,
            durableAudit: true,
            cancellationToken: cancellationToken);
        return Results.Ok(result);
    }

    internal static async Task<IResult> RetryDeadlineReminderAsync(
        Guid outboxId,
        DeadlineReminderService service,
        AuditService audit,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var user = AuthContext.GetUser(context);
        if (user is null || !IsPracticeRole(user.Role)) return Forbidden(context);
        if (!await service.RetryNowAsync(user.TenantId, outboxId, cancellationToken))
            return Results.NotFound(new { error = "A retryable reminder was not found.", correlationId = context.TraceIdentifier });
        await audit.LogAsync(
            null,
            null,
            nameof(DeadlineRiskQueueItem),
            0,
            AuditEventCodes.DeadlineReminderRetryRequested,
            newValue: new { retryRequested = true },
            userId: AuthenticatedIdentity.AuditUserId(user),
            tenantId: user.TenantId,
            actorDisplayName: user.DisplayName,
            durableAudit: true,
            cancellationToken: cancellationToken);
        return Results.NoContent();
    }

    internal static async Task<IResult> GetPlatformMetricsAsync(
        DeadlineReminderService service,
        PlatformMetrics metrics,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var user = AuthContext.GetUser(context);
        if (user is null || !(RoleIs(user.Role, "Owner") || RoleIs(user.Role, "Reviewer")))
            return Forbidden(context);
        return Results.Ok(metrics.Snapshot(await service.OperationalMetricsAsync(user.TenantId, cancellationToken)));
    }

    private static bool IsPracticeRole(string role) =>
        RoleIs(role, "Owner") || RoleIs(role, "Accountant") || RoleIs(role, "Reviewer");

    private static bool RoleIs(string actual, string expected) =>
        actual.Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static IResult Forbidden(HttpContext context) => Results.Json(
        new { error = "You do not have permission to access this operational workflow.", correlationId = context.TraceIdentifier },
        statusCode: StatusCodes.Status403Forbidden);
}
