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

    public static bool RequiresRecentMfa(PathString path, string method)
    {
        if (HttpMethods.IsDelete(method)) return path.StartsWithSegments("/api");
        if (!(HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method))) return false;

        if (path.StartsWithSegments("/api/privacy", StringComparison.OrdinalIgnoreCase)) return true;
        if (path.StartsWithSegments("/api/admin/users", StringComparison.OrdinalIgnoreCase)) return true;
        if (path.StartsWithSegments("/api/operations/deadline-reminders", StringComparison.OrdinalIgnoreCase)) return true;
        if (path.Equals("/api/auth/mfa/recovery-codes", StringComparison.OrdinalIgnoreCase)) return true;

        var value = path.Value ?? "";
        return value.Contains("/quarantine", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("/recover", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("/status", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("/approve", StringComparison.OrdinalIgnoreCase)
            || value.Contains("/year-end-reviews/", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("/mark-filed", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("/revenue-external-validation", StringComparison.OrdinalIgnoreCase)
            || value.Contains("/external-filing-handoff/", StringComparison.OrdinalIgnoreCase)
            || value.Contains("/release-evidence", StringComparison.OrdinalIgnoreCase)
            || value.Contains("/accept", StringComparison.OrdinalIgnoreCase);
    }
}
