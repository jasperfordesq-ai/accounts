using System.Diagnostics;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Routing;

namespace Accounts.Api.Middleware;

public sealed class PlatformMetricsMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, PlatformMetrics metrics)
    {
        var started = Stopwatch.GetTimestamp();
        var statusCode = StatusCodes.Status500InternalServerError;
        try
        {
            await next(context);
            statusCode = context.Response.StatusCode;
        }
        finally
        {
            var registeredPattern = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
            metrics.RecordRequest(
                registeredPattern,
                context.Request.Method,
                statusCode,
                Stopwatch.GetElapsedTime(started));
        }
    }
}
