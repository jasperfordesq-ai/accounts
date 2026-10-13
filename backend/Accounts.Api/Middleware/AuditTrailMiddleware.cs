using Accounts.Api.Data;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Accounts.Api.Middleware;

public class AuditTrailMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        AccountsDbContext db,
        AuditService? audit = null,
        IServiceScopeFactory? scopeFactory = null)
    {
        audit ??= new AuditService(db);
        var shouldAudit = ShouldAudit(context.Request, out var companyId, out var periodId);
        if (shouldAudit && periodId is null)
            periodId = await TryGetVerifiedImportPeriodIdAsync(context.Request, db, companyId);

        if (!shouldAudit)
        {
            if (IsConnectorWorkspaceWrite(context))
            {
                await AuditConnectorWorkspaceWriteAsync(context, audit, scopeFactory);
                return;
            }

            await next(context);
            return;
        }

        await using var transaction = shouldAudit && db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(context.RequestAborted)
            : null;
        var transactionCompleted = false;
        var originalBody = context.Response.Body;
        await using var responseBuffer = new MemoryStream();
        context.Response.Body = responseBuffer;

        try
        {
            await next(context);

            if (!IsSuccess(context.Response.StatusCode))
            {
                if (transaction is not null)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    transactionCompleted = true;
                }

                await LogApiWriteAsync(
                    audit,
                    scopeFactory,
                    context,
                    companyId,
                    periodId,
                    "ApiWriteRejected",
                    useFreshScope: true,
                    payload: new
                    {
                        method = context.Request.Method,
                        path = context.Request.Path.Value,
                        statusCode = context.Response.StatusCode
                    });
            }
            else
            {
                var wasIdempotencyReplay = string.Equals(
                    context.Response.Headers[IdempotencyHttpContract.ReplayedHeader],
                    "true",
                    StringComparison.OrdinalIgnoreCase);
                if (!wasIdempotencyReplay)
                {
                    await LogApiWriteAsync(
                        audit,
                        scopeFactory,
                        context,
                        companyId,
                        periodId,
                        "ApiWriteSucceeded",
                        useFreshScope: false,
                        payload: new
                        {
                            method = context.Request.Method,
                            path = context.Request.Path.Value,
                            statusCode = context.Response.StatusCode
                        });
                }

                if (transaction is not null)
                {
                    await transaction.CommitAsync(CancellationToken.None);
                    transactionCompleted = true;
                }
            }
        }
        catch (Exception ex)
        {
            if (transaction is not null && !transactionCompleted)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                transactionCompleted = true;
            }

            try
            {
                await LogApiWriteAsync(
                    audit,
                    scopeFactory,
                    context,
                    companyId,
                    periodId,
                    "ApiWriteFailed",
                    useFreshScope: true,
                    payload: new
                    {
                        method = context.Request.Method,
                        path = context.Request.Path.Value,
                        exceptionType = ex.GetType().Name
                    });
            }
            catch
            {
                // Preserve the original application exception if audit logging also fails.
            }

            throw;
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        await CopyBufferedResponseAsync(responseBuffer, originalBody, context.RequestAborted);
    }

    private static bool ShouldAudit(HttpRequest request, out int companyId, out int? periodId)
    {
        periodId = null;

        if (!IsUnsafeMethod(request.Method))
        {
            companyId = 0;
            return false;
        }

        if (!TenantAccessMiddleware.TryGetCompanyId(request.Path, out companyId))
            return false;

        if (PeriodOwnershipMiddleware.TryGetCompanyPeriod(request.Path, out _, out var routePeriodId))
            periodId = routePeriodId;

        return true;
    }

    /// <summary>
    /// A connector change outside /api/companies/{id} (creating or onboarding a company, user
    /// administration, privacy and operations routes). Those routes run their own transactions, so
    /// none is opened here; the attributed record is written afterwards in a fresh scope, so a
    /// person can see in the audit log which agent made the change and why.
    /// </summary>
    private static bool IsConnectorWorkspaceWrite(HttpContext context) =>
        IsUnsafeMethod(context.Request.Method)
        && ConnectorSessionContext.Get(context) is not null
        && context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
        && !context.Request.Path.StartsWithSegments("/api/auth", StringComparison.OrdinalIgnoreCase)
        && !context.Request.Path.StartsWithSegments("/api/oauth", StringComparison.OrdinalIgnoreCase);

    private async Task AuditConnectorWorkspaceWriteAsync(HttpContext context, AuditService audit, IServiceScopeFactory? scopeFactory)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            try
            {
                await LogApiWriteAsync(audit, scopeFactory, context, null, null, "ApiWriteFailed", useFreshScope: true,
                    payload: new { method = context.Request.Method, path = context.Request.Path.Value, exceptionType = ex.GetType().Name });
            }
            catch
            {
                // Preserve the original application exception if audit logging also fails.
            }

            throw;
        }

        var replayed = string.Equals(context.Response.Headers[IdempotencyHttpContract.ReplayedHeader], "true", StringComparison.OrdinalIgnoreCase);
        if (replayed) return;
        try
        {
            await LogApiWriteAsync(
                audit,
                scopeFactory,
                context,
                null,
                null,
                IsSuccess(context.Response.StatusCode) ? "ApiWriteSucceeded" : "ApiWriteRejected",
                useFreshScope: true,
                payload: new
                {
                    method = context.Request.Method,
                    path = context.Request.Path.Value,
                    statusCode = context.Response.StatusCode
                });
        }
        catch (Exception ex)
        {
            // The route has already committed its change and may have started its response; an
            // audit failure here must not replace that answer with an error. The connector activity
            // record written by ConnectorPostureMiddleware still attributes the change.
            context.RequestServices?.GetService<ILoggerFactory>()?
                .CreateLogger<AuditTrailMiddleware>()
                .LogError(ex, "Could not write the workspace-level connector audit entry for {Method} {Path} ({StatusCode}).",
                    context.Request.Method, context.Request.Path.Value, context.Response.StatusCode);
        }
    }

    private static async Task<int?> TryGetVerifiedImportPeriodIdAsync(HttpRequest request, AccountsDbContext db, int companyId)
    {
        if (!IsImportRoute(request.Path)
            || !int.TryParse(request.Query["periodId"].FirstOrDefault(), out var queryPeriodId)
            || queryPeriodId <= 0)
        {
            return null;
        }

        var verified = await db.AccountingPeriods
            .AsNoTracking()
            .AnyAsync(p => p.Id == queryPeriodId && p.CompanyId == companyId, request.HttpContext.RequestAborted);

        return verified ? queryPeriodId : null;
    }

    private static bool IsImportRoute(PathString path)
    {
        var segments = path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments is { Length: 6 }
            && segments[0].Equals("api", StringComparison.OrdinalIgnoreCase)
            && segments[1].Equals("companies", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(segments[2], out _)
            && segments[3].Equals("bank-accounts", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(segments[4], out _)
            && segments[5].Equals("import", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task CopyBufferedResponseAsync(MemoryStream responseBuffer, Stream originalBody, CancellationToken cancellationToken)
    {
        responseBuffer.Position = 0;
        await responseBuffer.CopyToAsync(originalBody, cancellationToken);
    }

    private static bool IsUnsafeMethod(string method) =>
        !HttpMethods.IsGet(method)
        && !HttpMethods.IsHead(method)
        && !HttpMethods.IsOptions(method)
        && !HttpMethods.IsTrace(method);

    private static bool IsSuccess(int statusCode) => statusCode is >= 200 and < 400;

    private static async Task LogApiWriteAsync(
        AuditService audit,
        IServiceScopeFactory? scopeFactory,
        HttpContext context,
        int? companyId,
        int? periodId,
        string action,
        bool useFreshScope,
        object payload)
    {
        if (useFreshScope && scopeFactory is not null)
        {
            using var scope = scopeFactory.CreateScope();
            var scopedAudit = scope.ServiceProvider.GetRequiredService<AuditService>();
            await WriteApiAuditAsync(scopedAudit, context, companyId, periodId, action, payload);
            return;
        }

        await WriteApiAuditAsync(audit, context, companyId, periodId, action, payload);
    }

    private static async Task WriteApiAuditAsync(
        AuditService audit,
        HttpContext context,
        int? companyId,
        int? periodId,
        string action,
        object payload)
    {
        var user = AuthContext.GetUser(context);
        await audit.LogAsync(
            companyId,
            periodId,
            "ApiRequest",
            periodId ?? companyId ?? 0,
            action,
            newValue: WithConnectorAttribution(context, payload),
            userId: user is null ? null : AuthenticatedIdentity.AuditUserId(user),
            tenantId: user?.TenantId,
            requestId: GetRequestId(context),
            actorDisplayName: user is null ? null : AuthenticatedIdentity.ReviewerDisplayName(user),
            isolatePendingChanges: true,
            durableAudit: true,
            cancellationToken: CancellationToken.None);
    }

    private static object WithConnectorAttribution(HttpContext context, object payload)
    {
        var client = ConnectorAudit.ClientAttribution(context);
        if (client is null) return payload;
        var node = System.Text.Json.JsonSerializer.SerializeToNode(payload, ConnectorAuditJson) as System.Text.Json.Nodes.JsonObject
            ?? new System.Text.Json.Nodes.JsonObject();
        node["client"] = System.Text.Json.JsonSerializer.SerializeToNode(client, ConnectorAuditJson);
        return node;
    }

    private static readonly System.Text.Json.JsonSerializerOptions ConnectorAuditJson = new(System.Text.Json.JsonSerializerDefaults.Web);

    private static string? GetRequestId(HttpContext context)
    {
        var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(correlationId))
            return correlationId;

        var requestId = context.Request.Headers["X-Request-ID"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(requestId))
            return requestId;

        return context.TraceIdentifier;
    }
}
