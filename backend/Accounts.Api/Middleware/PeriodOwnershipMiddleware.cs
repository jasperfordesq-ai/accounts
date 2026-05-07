using Accounts.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Middleware;

public class PeriodOwnershipMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, AccountsDbContext db)
    {
        if (!TryGetCompanyPeriod(context.Request.Path, out var companyId, out var periodId))
        {
            await next(context);
            return;
        }

        var periodBelongsToCompany = await db.AccountingPeriods
            .AsNoTracking()
            .AnyAsync(p => p.Id == periodId && p.CompanyId == companyId);

        if (!periodBelongsToCompany)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { error = "Accounting period not found for this company." });
            return;
        }

        await next(context);
    }

    public static bool TryGetCompanyPeriod(PathString path, out int companyId, out int periodId)
    {
        companyId = 0;
        periodId = 0;

        var segments = path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments is not { Length: >= 5 })
            return false;

        if (!segments[0].Equals("api", StringComparison.OrdinalIgnoreCase)
            || !segments[1].Equals("companies", StringComparison.OrdinalIgnoreCase)
            || !segments[3].Equals("periods", StringComparison.OrdinalIgnoreCase))
            return false;

        return int.TryParse(segments[2], out companyId)
            && int.TryParse(segments[4], out periodId);
    }
}
