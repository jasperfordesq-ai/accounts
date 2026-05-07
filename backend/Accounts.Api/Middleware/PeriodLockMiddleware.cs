using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Middleware;

public class PeriodLockMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> WriteAllowedWhenLocked = new(StringComparer.OrdinalIgnoreCase)
    {
        "status",
        "filing",
        "deadlines",
        "mark-filed"
    };

    public async Task InvokeAsync(HttpContext context, AccountsDbContext db)
    {
        if (IsSafeMethod(context.Request.Method)
            || !PeriodOwnershipMiddleware.TryGetCompanyPeriod(context.Request.Path, out var companyId, out var periodId)
            || IsAllowedLockedPeriodWrite(context.Request.Path))
        {
            await next(context);
            return;
        }

        var lockedPeriod = await db.AccountingPeriods
            .AsNoTracking()
            .Where(p => p.Id == periodId && p.CompanyId == companyId)
            .Select(p => new { p.Status, p.LockedAt })
            .FirstOrDefaultAsync();

        if (lockedPeriod is null)
        {
            await next(context);
            return;
        }

        if (lockedPeriod.Status is PeriodStatus.Finalised or PeriodStatus.Filed || lockedPeriod.LockedAt is not null)
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Accounting period is locked. Reopen the period before changing accounting data."
            });
            return;
        }

        await next(context);
    }

    private static bool IsSafeMethod(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);

    private static bool IsAllowedLockedPeriodWrite(PathString path)
    {
        var segments = path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments is not { Length: >= 6 })
            return false;

        return WriteAllowedWhenLocked.Contains(segments[5]);
    }
}
