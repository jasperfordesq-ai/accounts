using Accounts.Api.Services;

namespace Accounts.Api.Endpoints;

public static class AuthEndpoints
{
    public const string LoginRateLimitPolicy = "auth-login";

    public static void MapAuthEndpoints(this WebApplication app)
    {
        var auth = app.MapGroup("/api/auth").WithTags("Auth");

        auth.MapPost("/login", async (LoginInput input, AuthService authService, AuditService audit, HttpContext context) =>
        {
            var result = await authService.LoginAsync(input.Email, input.Password);
            await LogLoginAuditAsync(audit, context, result);
            if (result.LockoutStarted)
                await LogAccountLockoutAuditAsync(audit, context, result);

            if (!result.Succeeded || result.User is null)
                return Results.Unauthorized();

            var now = DateTimeOffset.UtcNow;
            var csrfToken = authService.CreateCsrfToken();
            var user = result.User with { CsrfToken = csrfToken };
            context.Response.Cookies.Append(
                authService.CookieName,
                authService.CreateSessionCookieValue(user, now),
                authService.CreateCookieOptions(now));
            context.Response.Cookies.Append(
                authService.CsrfCookieName,
                csrfToken,
                authService.CreateCsrfCookieOptions(now));

            return Results.Ok(AuthResponse.From(user));
        }).RequireRateLimiting(LoginRateLimitPolicy);

        auth.MapPost("/logout", async (AuthService authService, AuditService audit, HttpContext context) =>
        {
            var user = AuthContext.GetUser(context);
            if (user is not null)
            {
                await authService.RevokeSessionAsync(user.UserId);
                await LogLogoutAuditAsync(audit, context, user);
            }

            context.Response.Cookies.Delete(authService.CookieName, authService.ClearCookieOptions());
            context.Response.Cookies.Delete(authService.CsrfCookieName, authService.ClearCsrfCookieOptions());
            return Results.NoContent();
        });

        auth.MapPost("/password", async (PasswordChangeInput input, AuthService authService, AuditService audit, HttpContext context) =>
        {
            var currentUser = AuthContext.RequireUser(context);
            var result = await authService.ChangePasswordAsync(
                currentUser.UserId,
                input.CurrentPassword,
                input.NewPassword);
            if (!result.Succeeded || result.User is null)
            {
                await LogPasswordChangeAuditAsync(audit, context, currentUser, result);
                return Results.BadRequest(new { error = result.FailureReason });
            }

            await LogPasswordChangeAuditAsync(audit, context, currentUser, result);

            var now = DateTimeOffset.UtcNow;
            var csrfToken = authService.CreateCsrfToken();
            var user = result.User with { CsrfToken = csrfToken };
            context.Response.Cookies.Append(
                authService.CookieName,
                authService.CreateSessionCookieValue(user, now),
                authService.CreateCookieOptions(now));
            context.Response.Cookies.Append(
                authService.CsrfCookieName,
                csrfToken,
                authService.CreateCsrfCookieOptions(now));

            return Results.Ok(AuthResponse.From(user));
        });

        auth.MapGet("/me", (HttpContext context) =>
            Results.Ok(AuthResponse.From(AuthContext.RequireUser(context))));
    }

    private static Task LogLoginAuditAsync(AuditService audit, HttpContext context, LoginResult result) =>
        audit.LogAsync(
            companyId: null,
            periodId: null,
            entityType: result.AuditUserId is null ? "AuthAttempt" : "AuthSession",
            entityId: result.AuditUserId ?? 0,
            action: result.Succeeded ? AuditEventCodes.AuthLoginSucceeded : AuditEventCodes.AuthLoginFailed,
            newValue: new
            {
                Email = result.AuditEmail,
                Succeeded = result.Succeeded,
                ReasonCode = result.Succeeded ? "Authenticated" : LoginFailureReasonCode(result),
                result.AccountLocked,
                FailedLoginCount = result.FailedLoginCount > 0 ? result.FailedLoginCount : (int?)null
            },
            userId: AuditUserId(result.AuditEmail),
            tenantId: result.AuditTenantId,
            requestId: RequestId(context),
            actorDisplayName: result.AuditDisplayName,
            durableAudit: true);

    private static Task LogAccountLockoutAuditAsync(AuditService audit, HttpContext context, LoginResult result)
    {
        if (result.AuditUserId is null)
            return Task.CompletedTask;

        return audit.LogAsync(
            companyId: null,
            periodId: null,
            entityType: "UserAccount",
            entityId: result.AuditUserId.Value,
            action: AuditEventCodes.AuthAccountLocked,
            newValue: new
            {
                Email = result.AuditEmail,
                ReasonCode = "LockoutThresholdReached",
                result.FailedLoginCount,
                result.LockedUntilUtc
            },
            userId: AuditUserId(result.AuditEmail),
            tenantId: result.AuditTenantId,
            requestId: RequestId(context),
            actorDisplayName: result.AuditDisplayName,
            durableAudit: true);
    }

    private static Task LogPasswordChangeAuditAsync(
        AuditService audit,
        HttpContext context,
        AuthenticatedUser currentUser,
        PasswordChangeResult result)
    {
        var auditUser = result.User ?? currentUser;
        return audit.LogAsync(
            companyId: null,
            periodId: null,
            entityType: "UserAccount",
            entityId: currentUser.UserId,
            action: result.Succeeded ? AuditEventCodes.AuthPasswordChanged : AuditEventCodes.AuthPasswordChangeFailed,
            newValue: new
            {
                Email = currentUser.Email,
                result.Succeeded,
                ReasonCode = result.Succeeded ? "PasswordChanged" : PasswordChangeFailureReasonCode(result.FailureReason),
                auditUser.SessionVersion,
                auditUser.MustChangePassword
            },
            userId: AuthenticatedIdentity.AuditUserId(currentUser),
            tenantId: currentUser.TenantId,
            requestId: RequestId(context),
            actorDisplayName: AuthenticatedIdentity.ReviewerDisplayName(currentUser),
            durableAudit: true);
    }

    private static Task LogLogoutAuditAsync(AuditService audit, HttpContext context, AuthenticatedUser user) =>
        audit.LogAsync(
            companyId: null,
            periodId: null,
            entityType: "AuthSession",
            entityId: user.UserId,
            action: AuditEventCodes.AuthLogoutSucceeded,
            newValue: new
            {
                user.Email,
                Revoked = true,
                SessionVersionRevoked = user.SessionVersion
            },
            userId: AuthenticatedIdentity.AuditUserId(user),
            tenantId: user.TenantId,
            requestId: RequestId(context),
            actorDisplayName: AuthenticatedIdentity.ReviewerDisplayName(user),
            durableAudit: true);

    private static string LoginFailureReasonCode(LoginResult result)
    {
        if (result.LockoutStarted)
            return "LockoutThresholdReached";

        if (result.AccountLocked)
            return "LockedOut";

        return result.FailureReason?.Contains("required", StringComparison.OrdinalIgnoreCase) == true
            ? "MissingCredentials"
            : "InvalidCredentials";
    }

    private static string PasswordChangeFailureReasonCode(string? failureReason)
    {
        if (string.IsNullOrWhiteSpace(failureReason))
            return "Rejected";

        if (failureReason.Contains("required", StringComparison.OrdinalIgnoreCase))
            return "MissingFields";

        if (failureReason.Contains("incorrect", StringComparison.OrdinalIgnoreCase))
            return "CurrentPasswordIncorrect";

        if (failureReason.Contains("different", StringComparison.OrdinalIgnoreCase))
            return "PasswordReuseRejected";

        if (failureReason.Contains("least", StringComparison.OrdinalIgnoreCase))
            return "WeakPasswordRejected";

        return "Rejected";
    }

    private static string? AuditUserId(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    private static string? RequestId(HttpContext context)
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

public record LoginInput(string? Email, string? Password);
public record PasswordChangeInput(string? CurrentPassword, string? NewPassword);

public record AuthResponse(
    int UserId,
    int TenantId,
    string TenantName,
    string Email,
    string DisplayName,
    string Role,
    IReadOnlySet<int> AllowedCompanyIds,
    bool MustChangePassword)
{
    public static AuthResponse From(AuthenticatedUser user) => new(
        user.UserId,
        user.TenantId,
        user.TenantName,
        user.Email,
        user.DisplayName,
        user.Role,
        user.AllowedCompanyIds,
        user.MustChangePassword);
}
