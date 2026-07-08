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
            // Expected business outcome — log at Warning with the correlation id for traceability.
            logger.LogWarning(ex, "Resource not found handling {Method} {Path} (correlationId {CorrelationId})",
                context.Request.Method, context.Request.Path, context.TraceIdentifier);
            await WriteErrorAsync(context, 404, ex.Message);
        }
        catch (BusinessRuleException ex)
        {
            logger.LogWarning(ex, "Business rule violation handling {Method} {Path} (correlationId {CorrelationId})",
                context.Request.Method, context.Request.Path, context.TraceIdentifier);
            await WriteErrorAsync(context, 400, ex.Message);
        }
        catch (Exception ex)
        {
            // Unexpected — log at Error with the exception (full detail, server-side only), the request
            // method/path, and the correlation id so a support ticket can be triaged without a repro.
            logger.LogError(ex, "Unhandled exception handling {Method} {Path} (correlationId {CorrelationId})",
                context.Request.Method, context.Request.Path, context.TraceIdentifier);
            errorReporter?.CaptureUnexpectedException(
                ex,
                new ErrorReportContext(context.Request.Method, context.Request.Path, context.TraceIdentifier));

            // Outside Development the client message is generic so no exception detail (which may carry
            // connection strings, secrets or PII) leaks; the correlation id is the safe triage handle.
            var env = context.RequestServices.GetService<IHostEnvironment>();
            var message = env?.IsDevelopment() == true ? ex.Message : "An internal error occurred. Please try again.";
            await WriteErrorAsync(context, 500, message);
        }
    }

    private static async Task WriteErrorAsync(HttpContext context, int statusCode, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = message, correlationId = context.TraceIdentifier }));
    }
}
