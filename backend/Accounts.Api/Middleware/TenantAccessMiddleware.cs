using Accounts.Api.Data;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Middleware;

public class TenantAccessMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, AccountsDbContext db)
    {
        if (!TryGetCompanyId(context.Request.Path, out var companyId))
        {
            await next(context);
            return;
        }

        var user = AuthContext.RequireUser(context);
        var allowed = await db.Companies
            .AsNoTracking()
            .AnyAsync(c => c.Id == companyId && c.TenantId == user.TenantId);

        if (!allowed || !UserCompanyAccessPolicy.CanAccessCompany(user, companyId))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { error = "Company not found." });
            return;
        }

        await next(context);
    }

    public static bool TryGetCompanyId(PathString path, out int companyId)
    {
        companyId = 0;
        var segments = path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments is not { Length: >= 3 })
            return false;

        return segments[0].Equals("api", StringComparison.OrdinalIgnoreCase)
            && segments[1].Equals("companies", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(segments[2], out companyId);
    }
}
