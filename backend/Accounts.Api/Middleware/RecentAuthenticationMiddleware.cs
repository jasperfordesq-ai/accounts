using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.Extensions.Options;

namespace Accounts.Api.Middleware;

/// <summary>
/// Enforces a fresh, TOTP-backed authentication ceremony for irreversible and
/// approval-capable writes. UI state never weakens this server-side gate.
/// </summary>
public sealed class RecentAuthenticationMiddleware(RequestDelegate next)
{
    public const string ErrorCode = "recent_mfa_required";

    public async Task InvokeAsync(HttpContext context, IOptions<AuthSessionConfig> options, TimeProvider timeProvider)
    {
        var user = AuthContext.GetUser(context);
        if (user is not null
            && ConnectorSessionContext.Get(context) is not null
            && RequiresRecentMfa(context.Request.Path, context.Request.Method))
        {
            // Connector sessions never use the browser's recent-TOTP window: only an approval spent
            // for this exact request by ConnectorActionApprovalMiddleware satisfies the gate.
            if (context.Items[ConnectorActionApprovalMiddleware.SpentItemKey] is Guid)
            {
                await next(context);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status428PreconditionRequired;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "This action needs a person to approve it.",
                code = ConnectorErrorCodes.ApprovalRequired
            });
            return;
        }

        if (options.Value.RequirePrivilegedMfa
            && user is not null
            && AuthService.RequiresPrivilegedMfa(user.Role)
            && RequiresRecentMfa(context.Request.Path, context.Request.Method)
            && !HasRecentTotp(user, timeProvider.GetUtcNow(), options.Value.RecentMfaMinutes))
        {
            context.Response.StatusCode = StatusCodes.Status428PreconditionRequired;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Recent authenticator verification is required for this operation.",
                code = ErrorCode
            });
            return;
        }

        await next(context);
    }

    public static bool HasRecentTotp(AuthenticatedUser user, DateTimeOffset now, int recentMinutes)
    {
        if (!string.Equals(user.MfaMethod, "totp", StringComparison.OrdinalIgnoreCase)
            || user.MfaVerifiedAtUtc is not { } verifiedAt
            || verifiedAt > now.AddMinutes(1))
            return false;
        return now - verifiedAt <= TimeSpan.FromMinutes(Math.Clamp(recentMinutes, 1, 60));
    }

    public static bool RequiresRecentMfa(PathString path, string method) =>
        PrivilegedRouteCatalog.RequiresHumanConfirmation(path, method);
}
