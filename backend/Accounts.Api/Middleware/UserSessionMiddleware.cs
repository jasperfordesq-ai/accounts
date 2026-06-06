using Accounts.Api.Services;

namespace Accounts.Api.Middleware;

public class UserSessionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, AuthService auth)
    {
        if (!context.Request.Path.StartsWithSegments("/api") || IsAnonymousAuthEndpoint(context))
        {
            await next(context);
            return;
        }

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

    private static bool IsAnonymousAuthEndpoint(HttpContext context) =>
        HttpMethods.IsPost(context.Request.Method)
        && context.Request.Path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase);
}
