namespace Accounts.Api.Middleware;

public class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            ApplyTo(context);
            return Task.CompletedTask;
        });

        await next(context);
    }

    public static void ApplyTo(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.TryAdd("X-Content-Type-Options", "nosniff");
        headers.TryAdd("X-Frame-Options", "DENY");
        headers.TryAdd("Referrer-Policy", "no-referrer");
        headers.TryAdd("Permissions-Policy", "camera=(), microphone=(), geolocation=()");

        // HSTS over HTTPS only — never advertise it on plain HTTP/localhost dev (BL-31).
        if (context.Request.IsHttps)
            headers.TryAdd("Strict-Transport-Security", "max-age=63072000; includeSubDomains");

        if (context.Request.Path.StartsWithSegments("/api"))
        {
            headers.TryAdd("Cache-Control", "no-store");
            // API responses are JSON and load no resources; lock the policy down so any
            // injected/served markup cannot fetch scripts, styles or frame the response (BL-31).
            headers.TryAdd("Content-Security-Policy", "default-src 'none'; frame-ancestors 'none'; base-uri 'none'");
        }
    }
}
