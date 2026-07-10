using Accounts.Api.Services;
using Accounts.Api.Data;

namespace Accounts.Api.Middleware;

public class UserSessionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api")
            || HttpMethods.IsOptions(context.Request.Method)
            || IsAnonymousAuthEndpoint(context))
        {
            await next(context);
            return;
        }

        if (IsLogoutEndpoint(context))
        {
            var optionalAuth = context.RequestServices.GetService<AuthService>();
            if (optionalAuth is not null)
            {
                ApplyVerifiedTenantScope(context, optionalAuth, DateTimeOffset.UtcNow);
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
        var now = DateTimeOffset.UtcNow;
        if (!ApplyVerifiedTenantScope(context, auth, now))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { error = "Authentication required." });
            return;
        }
        var user = await auth.ReadSessionAsync(
            context.Request.Cookies[auth.CookieName],
            now);

        if (user is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { error = "Authentication required." });
            return;
        }

        context.Items[AuthContext.ItemKey] = user;
        context.Response.Cookies.Append(
            auth.CookieName,
            auth.CreateRefreshedSessionCookieValue(user, now),
            auth.CreateCookieOptions(now));
        await next(context);
    }

    private static bool ApplyVerifiedTenantScope(
        HttpContext context,
        AuthService auth,
        DateTimeOffset now)
    {
        var verified = auth.ReadVerifiedSessionScope(context.Request.Cookies[auth.CookieName], now);
        if (verified is null) return false;
        context.RequestServices.GetService<DatabaseTenantBootstrapResolver>()
            ?.UseVerifiedSessionTenant(verified.TenantId);
        return true;
    }

    private static bool IsAnonymousAuthEndpoint(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method)) return false;
        return context.Request.Path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase)
            || context.Request.Path.Equals("/api/auth/mfa/challenge", StringComparison.OrdinalIgnoreCase)
            || context.Request.Path.Equals("/api/auth/invitations/accept", StringComparison.OrdinalIgnoreCase)
            || context.Request.Path.Equals("/api/auth/recovery/complete", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLogoutEndpoint(HttpContext context) =>
        HttpMethods.IsPost(context.Request.Method)
        && context.Request.Path.Equals("/api/auth/logout", StringComparison.OrdinalIgnoreCase);
}
