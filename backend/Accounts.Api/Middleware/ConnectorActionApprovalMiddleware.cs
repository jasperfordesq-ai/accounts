using Accounts.Api.Data;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.Extensions.Options;

namespace Accounts.Api.Middleware;

/// <summary>
/// Per-action human approval for connector sessions. Any route that a browser user may only call
/// with a fresh TOTP (<see cref="PrivilegedRouteCatalog.RequiresHumanConfirmation"/>) answers a
/// connector with 428 and a single-use approval bound to the exact request. A person grants it with
/// password + TOTP (CLI or web), and the identical retry carrying the approval id spends it.
/// Nothing elevates the agent for a time window.
/// </summary>
public sealed class ConnectorActionApprovalMiddleware(RequestDelegate next)
{
    public const string SpentItemKey = "ConnectorApprovalSpent";

    public async Task InvokeAsync(
        HttpContext context,
        ConnectorSessionService? connectors = null,
        IServiceScopeFactory? scopeFactory = null,
        IOptions<ConnectorConfig>? connectorOptions = null)
    {
        var connector = ConnectorSessionContext.Get(context);
        var user = AuthContext.GetUser(context);
        if (connector is null
            || user is null
            || !PrivilegedRouteCatalog.RequiresHumanConfirmation(context.Request.Path, context.Request.Method))
        {
            await next(context);
            return;
        }

        // A request the role model forbids never mints an approval; let the role gate answer it.
        if (!RoleAuthorizationService.Authorize(user, context.Request.Path, context.Request.Method).IsAllowed)
        {
            await next(context);
            return;
        }

        if (connectors is null)
        {
            await WriteApprovalRequiredAsync(context, null, null);
            return;
        }

        var digest = await ConnectorActionDigest.ComputeAsync(connector.FamilyId, context.Request);
        var presented = context.Request.Headers[ConnectorClientGuard.ApprovalHeader].FirstOrDefault();
        if (Guid.TryParse(presented, out var approvalId)
            && await connectors.TrySpendApprovalAsync(user, connector, approvalId, digest, context.RequestAborted))
        {
            context.Items[SpentItemKey] = approvalId;
            await next(context);
            return;
        }

        if (scopeFactory is null)
        {
            await WriteApprovalRequiredAsync(context, null, connectorOptions?.Value);
            return;
        }

        // The 428 is a non-success response, so the audit transaction around this request is rolled
        // back: the approval must be written through its own scope or it would vanish.
        using var scope = scopeFactory.CreateScope();
        scope.ServiceProvider.GetService<DatabaseTenantBootstrapResolver>()?.UseVerifiedSessionTenant(user.TenantId);
        var scopedConnectors = scope.ServiceProvider.GetRequiredService<ConnectorSessionService>();
        var summaries = scope.ServiceProvider.GetRequiredService<ConnectorActionSummaryService>();
        var routePattern = ConnectorPostureMiddleware.RoutePatternOf(context);
        var summary = await summaries.DescribeAsync(context, routePattern, context.RequestAborted);
        int? companyId = TenantAccessMiddleware.TryGetCompanyId(context.Request.Path, out var parsedCompanyId) ? parsedCompanyId : null;
        var approval = await scopedConnectors.MintApprovalAsync(
            user,
            connector,
            digest,
            context.Request.Method,
            routePattern,
            summary,
            ConnectorPostureMiddleware.ResourceIdOf(context.Request.Path),
            companyId,
            context.RequestAborted);
        await WriteApprovalRequiredAsync(context, approval, connectorOptions?.Value ?? scopedConnectors.Config);
    }

    private static Task WriteApprovalRequiredAsync(
        HttpContext context,
        Entities.ConnectorActionApproval? approval,
        ConnectorConfig? config)
    {
        context.Response.StatusCode = StatusCodes.Status428PreconditionRequired;
        context.Response.ContentType = "application/json";
        if (approval is null)
        {
            return context.Response.WriteAsJsonAsync(new
            {
                error = "This action needs a person to approve it.",
                code = ConnectorErrorCodes.ApprovalRequired
            });
        }

        var publicBase = config?.PublicBaseUrl?.TrimEnd('/');
        return context.Response.WriteAsJsonAsync(new
        {
            error = "This action needs a person to approve it. Ask them to run the command below in their own terminal, then repeat the identical call with approvalId.",
            code = ConnectorErrorCodes.ApprovalRequired,
            approvalId = approval.Id,
            summary = approval.Summary,
            resourceId = approval.ResourceId,
            expiresAt = DateTime.SpecifyKind(approval.ExpiresAtUtc, DateTimeKind.Utc),
            command = $"filingbridge-mcp approve {approval.Id}",
            approvalsUrl = string.IsNullOrWhiteSpace(publicBase) ? null : $"{publicBase}/settings/approvals?id={approval.Id}"
        });
    }
}
