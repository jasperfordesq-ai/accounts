using Accounts.Api.Services;

namespace Accounts.Api.Middleware;

public class UserSessionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api")
            || HttpMethods.IsOptions(context.Request.Method)
            || IsLoginEndpoint(context))
        {
            await next(context);
            return;
        }

        if (IsLogoutEndpoint(context))
        {
            var optionalAuth = context.RequestServices.GetService<AuthService>();
            if (optionalAuth is not null)
            {
                var optionalUser = await optionalAuth.ReadSessionAsync(
                    context.Request.Cookies[optionalAuth.CookieName],
                    DateTimeOffset.UtcNow);
                if (optionalUser is not null)
                    context.Items[AuthContext.ItemKey] = optionalUser;
            }

            await next(context);
            return;
        }

        var auth = context.RequestServices.GetRequiredService<AuthService>();
        var user = await auth.ReadSessionAsync(
            context.Request.Cookies[auth.CookieName],
            DateTimeOffset.UtcNow);

        if (user is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { error = "Authentication required." });
            return;
        }

        context.Items[AuthContext.ItemKey] = user;
        await next(context);
    }

    private static bool IsLoginEndpoint(HttpContext context) =>
        HttpMethods.IsPost(context.Request.Method)
        && context.Request.Path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase);

    private static bool IsLogoutEndpoint(HttpContext context) =>
        HttpMethods.IsPost(context.Request.Method)
        && context.Request.Path.Equals("/api/auth/logout", StringComparison.OrdinalIgnoreCase);
}
