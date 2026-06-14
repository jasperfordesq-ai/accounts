using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Middleware;

public class PeriodLockMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, AccountsDbContext db)
    {
        if (IsSafeMethod(context.Request.Method)
            || !PeriodOwnershipMiddleware.TryGetCompanyPeriod(context.Request.Path, out var companyId, out var periodId)
            || IsAllowedLockedPeriodWrite(context.Request.Method, context.Request.Path))
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

    private static bool IsAllowedLockedPeriodWrite(string method, PathString path)
    {
        var segments = path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments is not { Length: >= 6 })
            return false;

        if (HttpMethods.IsPut(method) && IsPeriodAction(segments, "status"))
            return true;

        if (HttpMethods.IsPut(method) && IsPeriodFilingAction(segments, "cro-status"))
            return true;

        if (HttpMethods.IsPost(method) && IsPeriodFilingAction(segments, "cro-payment"))
            return true;

        if (HttpMethods.IsPost(method)
            && (IsPeriodDocumentAction(segments, "cro-filing-pack")
                || IsPeriodDocumentAction(segments, "signature-page")))
        {
            return true;
        }

        if (HttpMethods.IsPost(method) && IsPeriodAction(segments, "mark-filed"))
            return true;

        if (HttpMethods.IsPost(method)
            && segments is { Length: 7 }
            && IsCompanyPeriodPrefix(segments)
            && segments[5].Equals("deadlines", StringComparison.OrdinalIgnoreCase)
            && segments[6].Equals("calculate", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool IsPeriodFilingAction(string[] segments, string action) =>
        segments is { Length: 7 }
        && IsCompanyPeriodPrefix(segments)
        && segments[5].Equals("filing", StringComparison.OrdinalIgnoreCase)
        && segments[6].Equals(action, StringComparison.OrdinalIgnoreCase);

    private static bool IsPeriodDocumentAction(string[] segments, string action) =>
        segments is { Length: 7 }
        && IsCompanyPeriodPrefix(segments)
        && segments[5].Equals("documents", StringComparison.OrdinalIgnoreCase)
        && segments[6].Equals(action, StringComparison.OrdinalIgnoreCase);

    private static bool IsPeriodAction(string[] segments, string action) =>
        segments is { Length: 6 }
        && IsCompanyPeriodPrefix(segments)
        && segments[5].Equals(action, StringComparison.OrdinalIgnoreCase);

    private static bool IsCompanyPeriodPrefix(string[] segments) =>
        segments.Length >= 5
        && segments[0].Equals("api", StringComparison.OrdinalIgnoreCase)
        && segments[1].Equals("companies", StringComparison.OrdinalIgnoreCase)
        && int.TryParse(segments[2], out _)
        && segments[3].Equals("periods", StringComparison.OrdinalIgnoreCase)
        && int.TryParse(segments[4], out _);
}
