using Accounts.Api.Services;
using System.Text.Json;

namespace Accounts.Api.Middleware;

public class ExceptionMiddleware(
    RequestDelegate next,
    ILogger<ExceptionMiddleware> logger,
    IErrorReporter? errorReporter = null)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ResourceNotFoundException ex)
        {
            LogSafeWarning(logger, "ResourceNotFound", ex, context);
            await WriteErrorAsync(context, 404, ex.Message);
        }
        catch (FilingReleaseBlockedException ex)
        {
            LogSafeWarning(logger, "FilingReleaseBlocked", ex, context);
            await WriteErrorAsync(context, StatusCodes.Status409Conflict, ex.Message);
        }
        catch (AccountingConcurrencyException ex)
        {
            LogSafeWarning(logger, "AccountingConcurrencyConflict", ex, context);
            if (ex.CurrentETag is not null)
                context.Response.Headers.ETag = ex.CurrentETag;
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(AccountingConflict.Response(context, ex.CurrentETag));
        }
        catch (BusinessRuleException ex)
        {
            LogSafeWarning(logger, "BusinessRuleViolation", ex, context);
            await WriteErrorAsync(context, 400, ex.Message);
        }
        catch (Exception ex)
        {
            // Exported logs must never carry free-form exception messages, request data, client
            // identifiers, or secrets. Safe dimensions retain grouping and correlation value.
            var safe = MonitoringEventSanitizer.Sanitize(
                ex,
                new ErrorReportContext(
                    context.Request.Method,
                    MonitoringEventSanitizer.SafeServerRoute(context.GetEndpoint()),
                    context.TraceIdentifier));
            logger.LogError(
                "Unhandled {ExceptionType} handling {Method} {Path} (correlationId {CorrelationId}, stackFingerprint {StackFingerprint})",
                safe.ExceptionType,
                safe.Method,
                safe.Path,
                safe.CorrelationId,
                safe.StackFingerprint);
            errorReporter?.CaptureUnexpectedException(
                ex,
                new ErrorReportContext(
                    context.Request.Method,
                    MonitoringEventSanitizer.SafeServerRoute(context.GetEndpoint()),
                    context.TraceIdentifier));

            // Outside Development the client message is generic so no exception detail (which may carry
            // connection strings, secrets or PII) leaks; the correlation id is the safe triage handle.
            var env = context.RequestServices.GetService<IHostEnvironment>();
            var message = env?.IsDevelopment() == true ? ex.Message : "An internal error occurred. Please try again.";
            await WriteErrorAsync(context, 500, message);
        }
    }

    private static void LogSafeWarning(ILogger logger, string outcome, Exception exception, HttpContext context)
    {
        var safe = MonitoringEventSanitizer.Sanitize(
            exception,
            new ErrorReportContext(
                context.Request.Method,
                MonitoringEventSanitizer.SafeServerRoute(context.GetEndpoint()),
                context.TraceIdentifier));
        logger.LogWarning(
            "{Outcome} {ExceptionType} handling {Method} {Path} (correlationId {CorrelationId}, stackFingerprint {StackFingerprint})",
            outcome,
            safe.ExceptionType,
            safe.Method,
            safe.Path,
            safe.CorrelationId,
            safe.StackFingerprint);
    }

    private static async Task WriteErrorAsync(HttpContext context, int statusCode, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = message, correlationId = context.TraceIdentifier }));
    }
}
