using Accounts.Api.Services;

namespace Accounts.Api.Middleware;

public class ApiAccessMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ApiAccessService access)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        var presentedKey = context.Request.Headers[access.HeaderName].FirstOrDefault();
        var decision = access.Authorize(presentedKey, context.Request.Path, context.Request.Method);
        if (!decision.IsAllowed)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { error = decision.DenialReason });
            return;
        }

        context.Items["ApiPrincipal"] = decision.PrincipalName;
        context.Items["ApiRole"] = decision.Role;
        context.Items["ApiCompanyId"] = decision.CompanyId;
        context.Items["ApiAllowedCompanyIds"] = decision.AllowedCompanyIds;
        await next(context);
    }
}
