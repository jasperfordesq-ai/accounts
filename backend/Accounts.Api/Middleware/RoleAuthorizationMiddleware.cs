using Accounts.Api.Services;

namespace Accounts.Api.Middleware;

public class RoleAuthorizationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api")
            || context.Request.Path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var user = AuthContext.GetUser(context);
        if (user is null)
        {
            await next(context);
            return;
        }

        var decision = RoleAuthorizationService.Authorize(
            user,
            context.Request.Path,
            context.Request.Method);

        if (!decision.IsAllowed)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { error = decision.DenialReason });
            return;
        }

        await next(context);
    }
}
