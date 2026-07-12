using Microsoft.Extensions.Options;
using Accounts.Api.Rules;
using Accounts.Api.Services;

namespace Accounts.Api.Endpoints;

public static class SystemEndpoints
{
    private static readonly HashSet<string> AllowedClientMonitoringEventCodes = new(StringComparer.Ordinal)
    {
        "api-contract-rejection",
        "api-network-failure",
        "api-server-rejection",
        "api-timeout",
        "auth-service-unavailable",
        "render-exception",
        "unhandled-client-exception"
    };

    public static void MapSystemEndpoints(this WebApplication app)
    {
        // Health checks
        app.MapGet("/health/live", () => Results.Ok(new { status = "alive", timestamp = DateTime.UtcNow }))
           .WithTags("System");

        app.MapGet("/health/ready", async (SystemReadinessProbeService readiness, CancellationToken cancellationToken) =>
        {
            var probe = await readiness.GetAsync(cancellationToken);
            if (!probe.Ready)
            {
                return Results.Json(
                    new
                    {
                        status = "unready",
                        checks = new
                        {
                            database = probe.Database,
                            migrations = probe.Migrations,
                            owner = probe.Owner
                        },
                        timestamp = DateTime.UtcNow
                    },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return Results.Ok(new
            {
                status = "ready",
                checks = new
                {
                    database = probe.Database,
                    migrations = probe.Migrations,
                    owner = probe.Owner
                },
                timestamp = DateTime.UtcNow
            });
        })
           .WithTags("System");

        app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
           .WithTags("System");

        app.MapGet("/api/system/production-readiness", async (
            ProductionReadinessReportService service,
            CancellationToken cancellationToken) =>
        {
            var report = await service.GetReportAsync(cancellationToken);
            return Results.Ok(report);
        })
           .WithTags("System");

        app.MapPost("/api/system/monitoring/error-smoke", (
            HttpContext context,
            IOptions<MonitoringConfig> monitoringOptions,
            IErrorReporter errorReporter,
            ILoggerFactory loggerFactory) =>
            EmitMonitoringSmokeError(
                context,
                monitoringOptions,
                errorReporter,
                loggerFactory.CreateLogger("Accounts.Api.Endpoints.SystemEndpoints")))
           .WithTags("System");

        app.MapPost("/api/system/monitoring/client-event", (
            HttpContext context,
            ClientMonitoringEventInput input,
            IErrorReporter errorReporter,
            ILoggerFactory loggerFactory) =>
            EmitClientMonitoringEvent(
                context,
                input,
                errorReporter,
                loggerFactory.CreateLogger("Accounts.Api.Endpoints.SystemEndpoints")))
           .WithTags("System");
    }

    public static IResult EmitMonitoringSmokeError(
        HttpContext context,
        IOptions<MonitoringConfig> monitoringOptions,
        IErrorReporter errorReporter,
        ILogger logger)
    {
        var monitoring = monitoringOptions.Value;
        if (!monitoring.ErrorSmokeEnabled)
            return Results.NotFound();

        var user = AuthContext.GetUser(context);
        if (user is null)
            return Results.Json(new { error = "Authentication is required." }, statusCode: StatusCodes.Status401Unauthorized);

        if (!user.Role.Trim().Equals("Owner", StringComparison.OrdinalIgnoreCase))
            return Results.Json(new { error = "Owner role is required to emit a monitoring smoke event." }, statusCode: StatusCodes.Status403Forbidden);

        var eventId = errorReporter.CaptureUnexpectedException(
            new MonitoringSmokeException(),
            new ErrorReportContext(
                context.Request.Method,
                "/api/system/monitoring/error-smoke",
                context.TraceIdentifier));

        logger.LogWarning(
            "Controlled monitoring smoke event emitted by owner user {UserId} with provider {Provider} and event id {EventId} (correlationId {CorrelationId})",
            user.UserId,
            monitoring.ErrorTrackingProvider,
            eventId,
            context.TraceIdentifier);

        return Results.Ok(new
        {
            status = "reported",
            provider = monitoring.ErrorTrackingProvider,
            eventId,
            correlationId = context.TraceIdentifier,
            timestamp = DateTime.UtcNow
        });
    }

    public static IResult EmitClientMonitoringEvent(
        HttpContext context,
        ClientMonitoringEventInput input,
        IErrorReporter errorReporter,
        ILogger logger)
    {
        if (AuthContext.GetUser(context) is null)
            return Results.Json(new { error = "Authentication is required." }, statusCode: StatusCodes.Status401Unauthorized);

        var eventCode = input.EventCode?.Trim().ToLowerInvariant() ?? "";
        if (!AllowedClientMonitoringEventCodes.Contains(eventCode))
            return Results.BadRequest(new { error = "Unsupported client monitoring event code." });

        var rawRoute = input.Route?.Trim() ?? "";
        if (!rawRoute.StartsWith('/') || rawRoute.Length > 512)
            return Results.BadRequest(new { error = "Client route is required and must be at most 512 characters." });
        var route = MonitoringEventSanitizer.SafeClientPath(rawRoute);

        var exception = new ClientMonitoringException();
        var correlationId = MonitoringEventSanitizer.SafeClientCorrelationId(
            input.CorrelationId,
            context.TraceIdentifier);
        var rawContext = new ErrorReportContext(
            "CLIENT",
            route,
            correlationId,
            eventCode);
        var safe = MonitoringEventSanitizer.Sanitize(exception, rawContext);
        var safeContext = new ErrorReportContext(
            safe.Method,
            safe.Path,
            safe.CorrelationId,
            safe.EventCode);
        var eventId = errorReporter.CaptureUnexpectedException(exception, safeContext);

        logger.LogWarning(
            "Sanitized client monitoring event {EventCode} reported for route {Route} with provider event id {EventId} (correlationId {CorrelationId})",
            safe.EventCode,
            safe.Path,
            eventId,
            safe.CorrelationId);

        return Results.Json(
            new
            {
                status = "reported",
                eventCode = safe.EventCode,
                eventId,
                correlationId = safe.CorrelationId,
                route = safe.Path,
                timestamp = DateTime.UtcNow
            },
            statusCode: StatusCodes.Status202Accepted);
    }
}

public sealed record ClientMonitoringEventInput(string EventCode, string Route, string? CorrelationId);
