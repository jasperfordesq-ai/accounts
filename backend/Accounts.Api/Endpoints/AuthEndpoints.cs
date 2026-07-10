using Accounts.Api.Services;

namespace Accounts.Api.Endpoints;

public static class AuthEndpoints
{
    public const string LoginRateLimitPolicy = "auth-login";
    public const string MfaRateLimitPolicy = "auth-mfa";

    public static void MapAuthEndpoints(this WebApplication app)
    {
        var auth = app.MapGroup("/api/auth").WithTags("Auth");

        auth.MapPost("/login", async (LoginInput input, IdentityAccessService identityAccess, AuthService authService, AuditService audit, PrivacyGovernanceService privacy, HttpContext context, CancellationToken cancellationToken) =>
        {
            var outcome = await identityAccess.BeginLoginAsync(input.Email, input.Password, cancellationToken);
            var result = outcome.FirstFactor;
            await privacy.RecordLoginAttemptAsync(
                result.AuditEmail,
                result.AuditTenantId,
                result.AuditUserId,
                result.Succeeded ? "accepted" : "rejected",
                LoginSecurityReasonCode(result, outcome.MfaChallenge is not null),
                RequestId(context),
                cancellationToken);
            await LogLoginAuditAsync(audit, context, result, outcome.MfaChallenge is not null);
            if (result.LockoutStarted) await LogAccountLockoutAuditAsync(audit, context, result);

            if (!outcome.Succeeded)
                return Results.Unauthorized();

            if (outcome.MfaChallenge is not null)
                return Results.Json(outcome.MfaChallenge, statusCode: StatusCodes.Status202Accepted);

            var user = outcome.User!;
            var now = DateTimeOffset.UtcNow;
            var csrfToken = authService.CreateCsrfToken();
            user = user with { CsrfToken = csrfToken };
            SetSessionCookies(context, authService, user, now, preserveIssuedAt: false);

            return Results.Ok(AuthResponse.From(user));
        })
            .Produces<AuthResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .RequireRateLimiting(LoginRateLimitPolicy);

        auth.MapPost("/mfa/challenge", async (MfaChallengeInput input, IdentityAccessService identityAccess, AuthService authService, HttpContext context, CancellationToken cancellationToken) =>
        {
            var result = await identityAccess.CompleteChallengeAsync(input.ChallengeToken, input.TotpCode, input.RecoveryCode, cancellationToken);
            if (!result.Succeeded || result.User is null)
                return Results.Json(new { error = result.FailureReason }, statusCode: StatusCodes.Status401Unauthorized);
            var now = DateTimeOffset.UtcNow;
            var user = result.User with { CsrfToken = authService.CreateCsrfToken() };
            SetSessionCookies(context, authService, user, now, preserveIssuedAt: false);
            return Results.Ok(new MfaCompletionResponse(AuthResponse.From(user), result.RecoveryCodes));
        })
            .Produces<MfaCompletionResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .RequireRateLimiting(MfaRateLimitPolicy);

        auth.MapPost("/reauthenticate", async (ReauthenticationInput input, IdentityAccessService identityAccess, AuthService authService, HttpContext context, CancellationToken cancellationToken) =>
        {
            var currentUser = AuthContext.RequireUser(context);
            var result = await identityAccess.ReauthenticateAsync(currentUser, input.Password, input.TotpCode, cancellationToken);
            if (!result.Succeeded || result.User is null)
                return Results.Json(new { error = result.FailureReason }, statusCode: StatusCodes.Status401Unauthorized);
            var now = DateTimeOffset.UtcNow;
            SetSessionCookies(context, authService, result.User, now, preserveIssuedAt: true);
            return Results.Ok(AuthResponse.From(result.User));
        })
            .Produces<AuthResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .RequireRateLimiting(MfaRateLimitPolicy);

        auth.MapPost("/mfa/recovery-codes", async (IdentityAccessService identityAccess, HttpContext context, CancellationToken cancellationToken) =>
        {
            var codes = await identityAccess.RegenerateRecoveryCodesAsync(AuthContext.RequireUser(context), cancellationToken);
            return Results.Ok(new RecoveryCodesResponse(codes));
        }).Produces<RecoveryCodesResponse>();

        auth.MapPost("/invitations/accept", async (AcceptActionTokenInput input, UserLifecycleService lifecycle, CancellationToken cancellationToken) =>
        {
            await lifecycle.AcceptInvitationAsync(input, cancellationToken);
            return Results.NoContent();
        }).Produces(StatusCodes.Status204NoContent).ProducesValidationProblem();

        auth.MapPost("/recovery/complete", async (AcceptActionTokenInput input, UserLifecycleService lifecycle, CancellationToken cancellationToken) =>
        {
            await lifecycle.CompletePasswordResetAsync(input, cancellationToken);
            return Results.NoContent();
        }).Produces(StatusCodes.Status204NoContent).ProducesValidationProblem();

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

        auth.MapPost("/password", async (PasswordChangeInput input, AuthService authService, AuditService audit, HttpContext context, CancellationToken cancellationToken) =>
        {
            var currentUser = AuthContext.RequireUser(context);
            var result = await authService.ChangePasswordAsync(
                currentUser.UserId,
                input.CurrentPassword,
                input.NewPassword,
                cancellationToken);
            if (!result.Succeeded || result.User is null)
            {
                await LogPasswordChangeAuditAsync(audit, context, currentUser, result);
                return Results.BadRequest(new { error = result.FailureReason });
            }

            await LogPasswordChangeAuditAsync(audit, context, currentUser, result);

            var now = DateTimeOffset.UtcNow;
            var csrfToken = authService.CreateCsrfToken();
            var user = result.User with
            {
                CsrfToken = csrfToken,
                SessionIssuedAtUtc = currentUser.SessionIssuedAtUtc,
                MfaVerifiedAtUtc = currentUser.MfaVerifiedAtUtc,
                MfaMethod = currentUser.MfaMethod
            };
            SetSessionCookies(context, authService, user, now, preserveIssuedAt: true);

            return Results.Ok(AuthResponse.From(user));
        })
            .Produces<AuthResponse>()
            .ProducesValidationProblem();

        auth.MapGet("/me", (HttpContext context) =>
            Results.Ok(AuthResponse.From(AuthContext.RequireUser(context))))
            .Produces<AuthResponse>();
    }

    private static Task LogLoginAuditAsync(AuditService audit, HttpContext context, LoginResult result, bool awaitingMfa) =>
        audit.LogAsync(
            companyId: null,
            periodId: null,
            entityType: result.AuditUserId is null ? "AuthAttempt" : "AuthSession",
            entityId: result.AuditUserId ?? 0,
            action: result.Succeeded
                ? awaitingMfa ? AuditEventCodes.AuthFirstFactorSucceeded : AuditEventCodes.AuthLoginSucceeded
                : AuditEventCodes.AuthLoginFailed,
            newValue: new
            {
                SubjectUserId = result.AuditUserId,
                UserKnown = result.AuditUserId is not null,
                Succeeded = result.Succeeded,
                ReasonCode = result.Succeeded ? awaitingMfa ? "AwaitingMfa" : "Authenticated" : LoginFailureReasonCode(result),
                result.AccountLocked,
                FailedLoginCount = result.FailedLoginCount > 0 ? result.FailedLoginCount : (int?)null
            },
            userId: AuditUserKey(result.AuditUserId),
            tenantId: result.AuditTenantId,
            requestId: RequestId(context),
            actorDisplayName: null,
            durableAudit: true);

    private static void SetSessionCookies(HttpContext context, AuthService authService, AuthenticatedUser user, DateTimeOffset now, bool preserveIssuedAt)
    {
        context.Response.Cookies.Append(
            authService.CookieName,
            preserveIssuedAt
                ? authService.CreateRefreshedSessionCookieValue(user, now)
                : authService.CreateSessionCookieValue(user, now),
            authService.CreateCookieOptions(now));
        context.Response.Cookies.Append(
            authService.CsrfCookieName,
            user.CsrfToken,
            authService.CreateCsrfCookieOptions(now));
    }

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
                SubjectUserId = result.AuditUserId,
                ReasonCode = "LockoutThresholdReached",
                result.FailedLoginCount,
                result.LockedUntilUtc
            },
            userId: AuditUserKey(result.AuditUserId),
            tenantId: result.AuditTenantId,
            requestId: RequestId(context),
            actorDisplayName: null,
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
                SubjectUserId = currentUser.UserId,
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
                SubjectUserId = user.UserId,
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

    private static string? AuditUserKey(int? userId) => userId is > 0 ? $"user:{userId.Value}" : null;

    private static string LoginSecurityReasonCode(LoginResult result, bool awaitingMfa)
    {
        if (result.Succeeded)
            return awaitingMfa ? "awaiting-mfa" : "authenticated";
        if (result.LockoutStarted)
            return "lockout-started";
        if (result.AccountLocked)
            return "locked-out";
        return result.FailureReason?.Contains("required", StringComparison.OrdinalIgnoreCase) == true
            ? "missing-credentials"
            : "invalid-credentials";
    }

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
public record MfaChallengeInput(string? ChallengeToken, string? TotpCode, string? RecoveryCode);
public record ReauthenticationInput(string? Password, string? TotpCode);
public record MfaCompletionResponse(AuthResponse User, IReadOnlyList<string> RecoveryCodes);
public record RecoveryCodesResponse(IReadOnlyList<string> RecoveryCodes);

public record AuthResponse(
    int UserId,
    int TenantId,
    string TenantName,
    string Email,
    string DisplayName,
    string Role,
    IReadOnlySet<int> AllowedCompanyIds,
    bool MustChangePassword,
    bool MfaVerified,
    string? MfaMethod)
{
    public static AuthResponse From(AuthenticatedUser user) => new(
        user.UserId,
        user.TenantId,
        user.TenantName,
        user.Email,
        user.DisplayName,
        user.Role,
        user.AllowedCompanyIds,
        user.MustChangePassword,
        user.MfaVerifiedAtUtc is not null,
        user.MfaMethod);
}
