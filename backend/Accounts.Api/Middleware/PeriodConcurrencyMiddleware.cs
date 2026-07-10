using Accounts.Api.Data;
using Accounts.Api.Services;

namespace Accounts.Api.Middleware;

/// <summary>
/// Exposes a period-wide strong ETag for mutable accounting/workflow resources and rejects a stale
/// If-Match before mutation. The save interceptor remains the authoritative last-line check for
/// clients that do not yet send an ETag and for races that occur after this initial comparison.
/// </summary>
public sealed class PeriodConcurrencyMiddleware(RequestDelegate next)
{
    public const string InitialETagItemKey = "accounts.period.initial-etag";

    public async Task InvokeAsync(
        HttpContext context,
        PeriodConcurrencyTokenService tokens,
        AccountsDbContext db)
    {
        if (!PeriodOwnershipMiddleware.TryGetCompanyPeriod(
                context.Request.Path,
                out var companyId,
                out var periodId))
        {
            await next(context);
            return;
        }

        var current = await tokens.GetAsync(companyId, periodId, context.RequestAborted);
        if (current is null)
        {
            await next(context);
            return;
        }

        context.Items[InitialETagItemKey] = current;
        context.Response.Headers.ETag = current;
        context.Response.OnStarting(async state =>
        {
            var (response, tokenService, company, period, fallback) =
                ((HttpResponse, PeriodConcurrencyTokenService, int, int, string))state;
            try
            {
                response.Headers.ETag = await tokenService.GetAsync(company, period, CancellationToken.None)
                    ?? fallback;
            }
            catch
            {
                // Concurrency metadata must never turn an otherwise valid response into a 500.
                response.Headers.ETag = fallback;
            }
        }, (context.Response, tokens, companyId, periodId, current));

        var unsafeMethod = !HttpMethods.IsGet(context.Request.Method)
            && !HttpMethods.IsHead(context.Request.Method)
            && !HttpMethods.IsOptions(context.Request.Method);
        var supplied = context.Request.Headers.IfMatch.ToString();
        if (unsafeMethod
            && !string.IsNullOrWhiteSpace(supplied)
            && !PeriodConcurrencyTokenService.Matches(supplied, current)
            && !await IdempotencyReplayPreflight.IsCompletedCandidateAsync(context, db, context.RequestAborted))
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(AccountingConflict.Response(context, current));
            return;
        }

        await next(context);
    }
}
