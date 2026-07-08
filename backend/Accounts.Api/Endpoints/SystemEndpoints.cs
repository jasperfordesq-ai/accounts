using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Accounts.Api.Data;
using Accounts.Api.Rules;
using Accounts.Api.Services;

namespace Accounts.Api.Endpoints;

public static class SystemEndpoints
{
    public static void MapSystemEndpoints(this WebApplication app)
    {
        // Health checks
        app.MapGet("/health/live", () => Results.Ok(new { status = "alive", timestamp = DateTime.UtcNow }))
           .WithTags("System");

        app.MapGet("/health/ready", async (AccountsDbContext db, IOptions<BootstrapOwnerConfig> bootstrapOptions, CancellationToken cancellationToken) =>
        {
            try
            {
                var databaseReady = await db.Database.CanConnectAsync(cancellationToken);
                if (!databaseReady)
                {
                    return Results.Json(
                        new
                        {
                            status = "unready",
                            checks = new { database = "unavailable" },
                            timestamp = DateTime.UtcNow
                        },
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                var pendingMigrations = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
                var bootstrap = bootstrapOptions.Value;
                var bootstrapOwnerEmail = bootstrap.OwnerEmail.Trim().ToLowerInvariant();
                var bootstrapTenantSlug = bootstrap.TenantSlug.Trim().ToLowerInvariant();
                var hasConfiguredBootstrapOwner = !string.IsNullOrWhiteSpace(bootstrapOwnerEmail)
                    && !string.IsNullOrWhiteSpace(bootstrapTenantSlug);
                var ownerReady = hasConfiguredBootstrapOwner
                    ? await db.UserAccounts.AnyAsync(
                        u => u.IsActive
                            && u.Role.Trim().ToLower() == "owner"
                            && u.Email.ToLower() == bootstrapOwnerEmail
                            && u.Tenant.Slug == bootstrapTenantSlug,
                        cancellationToken)
                    : await db.UserAccounts.AnyAsync(u => u.IsActive && u.Role.Trim().ToLower() == "owner", cancellationToken);
                if (pendingMigrations.Length > 0 || !ownerReady)
                {
                    return Results.Json(
                        new
                        {
                            status = "unready",
                            checks = new
                            {
                                database = "reachable",
                                migrations = pendingMigrations.Length == 0 ? "current" : "pending",
                                owner = ownerReady ? "configured" : "missing"
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
                        database = "reachable",
                        migrations = "current",
                        owner = "configured"
                    },
                    timestamp = DateTime.UtcNow
                });
            }
            catch
            {
                return Results.Json(
                    new
                    {
                        status = "unready",
                        checks = new { database = "schema_unavailable" },
                        timestamp = DateTime.UtcNow
                    },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
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
            new ErrorReportContext(context.Request.Method, context.Request.Path, context.TraceIdentifier));

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
}
