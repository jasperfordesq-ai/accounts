using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Accounts.Api.Services;

public class AuthService
{
    public const string PasswordAlgorithm = "PBKDF2-SHA256-210000";
    private const int MaxFailedLoginAttempts = 5;
    private const long TicksPerMicrosecond = 10;
    private static readonly TimeSpan FailedLoginWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan LoginLockoutDuration = TimeSpan.FromMinutes(15);

    private static readonly JsonSerializerOptions SessionJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AccountsDbContext db;
    private readonly AuthSessionConfig config;
    private readonly IHostEnvironment environment;
    private readonly IPasswordVerifier passwordVerifier;
    private readonly TimeProvider timeProvider;
    private readonly byte[] signingKey;

    public AuthService(
        AccountsDbContext db,
        IOptions<AuthSessionConfig> config,
        IHostEnvironment environment,
        IPasswordVerifier passwordVerifier,
        TimeProvider? timeProvider = null)
    {
        this.db = db;
        this.config = config.Value;
        this.environment = environment;
        this.passwordVerifier = passwordVerifier;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        signingKey = AuthSessionKey.DecodeRequired(this.config.SigningKey);
    }

    public string CookieName =>
        string.IsNullOrWhiteSpace(config.CookieName) ? "accounts_session" : config.CookieName.Trim();

    public string CsrfCookieName =>
        string.IsNullOrWhiteSpace(config.CsrfCookieName) ? "accounts_csrf" : config.CsrfCookieName.Trim();

    public async Task<LoginResult> LoginAsync(string? email, string? password)
    {
        var normalizedEmail = NormalizeEmail(email);
        if (normalizedEmail is null || string.IsNullOrWhiteSpace(password))
            return LoginResult.Failed("Email and password are required.", attemptedEmail: normalizedEmail);

        var user = await db.UserAccounts
            .Include(u => u.Tenant)
            .Include(u => u.CompanyAccesses)
            .SingleOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);

        var now = DatabaseTimestampUtc();
        if (IsLocked(user, now))
        {
            return LoginResult.Failed(
                "Invalid email or password.",
                attemptedEmail: normalizedEmail,
                user: user,
                accountLocked: true,
                failedLoginCount: user!.FailedLoginCount,
                lockedUntilUtc: user.LockedUntilUtc);
        }

        var passwordMatches = passwordVerifier.Verify(password, user);

        if (user is null
            || user.Tenant is null
            || user.PasswordAlgorithm != PasswordAlgorithm
            || !passwordMatches)
        {
            var failureState = await RecordFailedLoginAsync(user, now);
            return LoginResult.Failed(
                "Invalid email or password.",
                attemptedEmail: normalizedEmail,
                user: user,
                accountLocked: failureState.LockedUntilUtc > now,
                lockoutStarted: failureState.LockoutStarted,
                failedLoginCount: failureState.FailedLoginCount,
                lockedUntilUtc: failureState.LockedUntilUtc);
        }

        if (!user.IsActive)
            return LoginResult.Failed("User account is inactive.", attemptedEmail: normalizedEmail, user: user);

        ResetLoginFailureState(user);
        user.LastLoginAt = now;
        user.UpdatedAt = now;
        await db.SaveChangesAsync();

        return LoginResult.Success(ToPrincipal(user));
    }

    public string CreateSessionCookieValue(AuthenticatedUser user, DateTimeOffset now)
    {
        var csrfToken = string.IsNullOrWhiteSpace(user.CsrfToken) ? CreateCsrfToken() : user.CsrfToken;
        var payload = new SessionPayload(
            UserId: user.UserId,
            TenantId: user.TenantId,
            ExpiresAtUnixSeconds: now.AddMinutes(ClampedExpiryMinutes).ToUnixTimeSeconds(),
            PasswordLastChangedAtTicks: user.PasswordLastChangedAt.Ticks,
            SessionVersion: user.SessionVersion,
            CsrfToken: csrfToken);
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, SessionJsonOptions);
        var encodedPayload = Base64UrlEncode(payloadBytes);
        var signature = Base64UrlEncode(ComputeSignature(encodedPayload));

        return $"{encodedPayload}.{signature}";
    }

    public string CreateCsrfToken() => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public async Task<AuthenticatedUser?> ReadSessionAsync(string? cookieValue, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(cookieValue))
            return null;

        var parts = cookieValue.Split('.');
        if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace))
            return null;

        if (!SignatureMatches(parts[0], parts[1]))
            return null;

        try
        {
            if (!TryBase64UrlDecode(parts[0], out var payloadBytes))
                return null;

            var payload = JsonSerializer.Deserialize<SessionPayload>(payloadBytes, SessionJsonOptions);
            if (payload is null
                || payload.UserId <= 0
                || payload.TenantId <= 0
                || payload.SessionVersion <= 0
                || string.IsNullOrWhiteSpace(payload.CsrfToken)
                || payload.ExpiresAtUnixSeconds <= now.ToUnixTimeSeconds())
                return null;

            var user = await db.UserAccounts
                .Include(u => u.Tenant)
                .Include(u => u.CompanyAccesses)
                .SingleOrDefaultAsync(u => u.Id == payload.UserId);

            if (!CanAuthenticate(user) || user!.TenantId != payload.TenantId)
                return null;

            if (payload.PasswordLastChangedAtTicks <= 0)
                return null;

            if (user.PasswordLastChangedAt.Ticks != payload.PasswordLastChangedAtTicks)
                return null;

            if (user.SessionVersion != payload.SessionVersion)
                return null;

            return ToPrincipal(user) with { CsrfToken = payload.CsrfToken };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<PasswordChangeResult> ChangePasswordAsync(int userId, string? currentPassword, string? newPassword)
    {
        if (string.IsNullOrWhiteSpace(currentPassword) || string.IsNullOrWhiteSpace(newPassword))
            return PasswordChangeResult.Failed("Current password and new password are required.");

        var user = await db.UserAccounts
            .Include(u => u.Tenant)
            .Include(u => u.CompanyAccesses)
            .SingleOrDefaultAsync(u => u.Id == userId);

        if (!CanAuthenticate(user) || !passwordVerifier.Verify(currentPassword, user))
            return PasswordChangeResult.Failed("Current password is incorrect.");

        var passwordValidation = ValidateNewPassword(currentPassword, newPassword);
        if (passwordValidation is not null)
            return PasswordChangeResult.Failed(passwordValidation);

        var (hash, salt) = PasswordHasher.HashPassword(newPassword);
        var passwordLastChangedAt = DatabaseTimestampUtc();
        var nextSessionVersion = user!.SessionVersion + 1;

        if (db.Database.IsRelational())
        {
            var rowsUpdated = await db.UserAccounts
                .Where(u =>
                    u.Id == userId
                    && u.PasswordHash == user.PasswordHash
                    && u.PasswordSalt == user.PasswordSalt
                    && u.PasswordLastChangedAt == user.PasswordLastChangedAt
                    && u.SessionVersion == user.SessionVersion)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(u => u.PasswordHash, hash)
                    .SetProperty(u => u.PasswordSalt, salt)
                    .SetProperty(u => u.PasswordAlgorithm, PasswordAlgorithm)
                    .SetProperty(u => u.PasswordStrengthScore, 5)
                    .SetProperty(u => u.MustChangePassword, false)
                    .SetProperty(u => u.PasswordLastChangedAt, passwordLastChangedAt)
                    .SetProperty(u => u.SessionVersion, nextSessionVersion)
                    .SetProperty(u => u.FailedLoginCount, 0)
                    .SetProperty(u => u.LastFailedLoginAt, (DateTime?)null)
                    .SetProperty(u => u.LockedUntilUtc, (DateTime?)null)
                    .SetProperty(u => u.UpdatedAt, passwordLastChangedAt));

            if (rowsUpdated == 0)
                return PasswordChangeResult.Failed("Current password is incorrect.");

            user.PasswordHash = hash;
            user.PasswordSalt = salt;
            user.PasswordAlgorithm = PasswordAlgorithm;
            user.PasswordStrengthScore = 5;
            user.MustChangePassword = false;
            user.PasswordLastChangedAt = passwordLastChangedAt;
            user.SessionVersion = nextSessionVersion;
            ResetLoginFailureState(user);
            user.UpdatedAt = passwordLastChangedAt;
            return PasswordChangeResult.Success(ToPrincipal(user));
        }

        user.PasswordHash = hash;
        user.PasswordSalt = salt;
        user.PasswordAlgorithm = PasswordAlgorithm;
        user.PasswordStrengthScore = 5;
        user.MustChangePassword = false;
        user.PasswordLastChangedAt = passwordLastChangedAt;
        user.SessionVersion = nextSessionVersion;
        ResetLoginFailureState(user);
        user.UpdatedAt = user.PasswordLastChangedAt;
        await db.SaveChangesAsync();

        return PasswordChangeResult.Success(ToPrincipal(user));
    }

    public async Task RevokeSessionAsync(int userId)
    {
        var now = DatabaseTimestampUtc();
        if (db.Database.IsRelational())
        {
            await db.UserAccounts
                .Where(u => u.Id == userId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(u => u.SessionVersion, u => u.SessionVersion + 1)
                    .SetProperty(u => u.UpdatedAt, now));
            return;
        }

        var user = await db.UserAccounts.SingleOrDefaultAsync(u => u.Id == userId);
        if (user is null)
            return;

        user.SessionVersion = user.SessionVersion + 1;
        user.UpdatedAt = now;
        await db.SaveChangesAsync();
    }

    public CookieOptions CreateCookieOptions(DateTimeOffset now) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = now.AddMinutes(ClampedExpiryMinutes),
        Secure = ShouldUseSecureCookies
    };

    public CookieOptions CreateCsrfCookieOptions(DateTimeOffset now) => new()
    {
        HttpOnly = false,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = now.AddMinutes(ClampedExpiryMinutes),
        Secure = ShouldUseSecureCookies
    };

    public CookieOptions ClearCookieOptions() => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = DateTimeOffset.UnixEpoch,
        Secure = ShouldUseSecureCookies
    };

    public CookieOptions ClearCsrfCookieOptions() => new()
    {
        HttpOnly = false,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = DateTimeOffset.UnixEpoch,
        Secure = ShouldUseSecureCookies
    };

    private bool ShouldUseSecureCookies =>
        !environment.IsDevelopment();

    private int ClampedExpiryMinutes => Math.Clamp(config.ExpiryMinutes, 15, 1_440);

    private DateTime DatabaseTimestampUtc()
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return new DateTime(now.Ticks - (now.Ticks % TicksPerMicrosecond), DateTimeKind.Utc);
    }

    private static bool CanAuthenticate(UserAccount? user) =>
        user is not null
        && user.IsActive
        && user.Tenant is not null
        && user.PasswordAlgorithm == PasswordAlgorithm;

    private static bool CanTrackLoginFailures(UserAccount? user) =>
        user is not null
        && user.IsActive
        && user.Tenant is not null
        && user.PasswordAlgorithm == PasswordAlgorithm;

    private static bool IsLocked(UserAccount? user, DateTime now) =>
        CanTrackLoginFailures(user)
        && user!.LockedUntilUtc is { } lockedUntil
        && lockedUntil > now;

    private async Task<LoginFailureState> RecordFailedLoginAsync(UserAccount? user, DateTime now)
    {
        if (!CanTrackLoginFailures(user))
            return new LoginFailureState();

        if (db.Database.IsRelational())
            return await RecordFailedLoginWithSetBasedUpdateAsync(user!, now);

        return await RecordFailedLoginWithTrackedEntityAsync(user!, now);
    }

    private async Task<LoginFailureState> RecordFailedLoginWithSetBasedUpdateAsync(UserAccount user, DateTime now)
    {
        var previousFailedLoginCount = user.FailedLoginCount;
        var staleFailureCutoff = now - FailedLoginWindow;
        var lockedUntil = now.Add(LoginLockoutDuration);
        await db.UserAccounts
            .Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(u => u.FailedLoginCount, u =>
                    u.LastFailedLoginAt == null || u.LastFailedLoginAt < staleFailureCutoff
                        ? 1
                        : u.FailedLoginCount + 1)
                .SetProperty(u => u.LastFailedLoginAt, now)
                .SetProperty(u => u.LockedUntilUtc, u =>
                    (u.LastFailedLoginAt == null || u.LastFailedLoginAt < staleFailureCutoff
                        ? 1
                        : u.FailedLoginCount + 1) >= MaxFailedLoginAttempts
                    && (u.LockedUntilUtc == null || u.LockedUntilUtc <= now)
                        ? lockedUntil
                        : u.LockedUntilUtc)
                .SetProperty(u => u.UpdatedAt, now));

        var updated = await db.UserAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == user.Id);
        if (updated is null)
            return new LoginFailureState();

        return new LoginFailureState(
            updated.FailedLoginCount,
            updated.LockedUntilUtc,
            previousFailedLoginCount < MaxFailedLoginAttempts
                && updated.FailedLoginCount >= MaxFailedLoginAttempts
                && updated.LockedUntilUtc == lockedUntil);
    }

    private async Task<LoginFailureState> RecordFailedLoginWithTrackedEntityAsync(UserAccount user, DateTime now)
    {
        if (user!.LastFailedLoginAt is null || now - user.LastFailedLoginAt.Value > FailedLoginWindow)
            user.FailedLoginCount = 0;

        var previousFailedLoginCount = user.FailedLoginCount;
        user.FailedLoginCount = user.FailedLoginCount + 1;
        user.LastFailedLoginAt = now;
        var lockoutStarted = previousFailedLoginCount < MaxFailedLoginAttempts
            && user.FailedLoginCount >= MaxFailedLoginAttempts;
        if (user.FailedLoginCount >= MaxFailedLoginAttempts)
            user.LockedUntilUtc = now.Add(LoginLockoutDuration);

        user.UpdatedAt = now;
        await db.SaveChangesAsync();
        return new LoginFailureState(
            user.FailedLoginCount,
            user.LockedUntilUtc,
            lockoutStarted);
    }

    private static void ResetLoginFailureState(UserAccount user)
    {
        user.FailedLoginCount = 0;
        user.LastFailedLoginAt = null;
        user.LockedUntilUtc = null;
    }

    private byte[] ComputeSignature(string encodedPayload)
    {
        using var hmac = new HMACSHA256(signingKey);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(encodedPayload));
    }

    private bool SignatureMatches(string encodedPayload, string encodedSignature)
    {
        if (!TryBase64UrlDecode(encodedSignature, out var providedSignature))
            return false;

        var expectedSignature = ComputeSignature(encodedPayload);
        return providedSignature.Length == expectedSignature.Length
            && CryptographicOperations.FixedTimeEquals(providedSignature, expectedSignature);
    }

    private static AuthenticatedUser ToPrincipal(UserAccount user) => new(
        user.Id,
        user.TenantId,
        user.Tenant.Name,
        user.Email,
        user.DisplayName,
        user.Role,
        user.CompanyAccesses
            .Select(a => a.CompanyId)
            .Distinct()
            .ToHashSet(),
        SessionVersion: user.SessionVersion,
        MustChangePassword: user.MustChangePassword,
        PasswordLastChangedAt: user.PasswordLastChangedAt);

    private static string? ValidateNewPassword(string currentPassword, string newPassword)
    {
        if (newPassword == currentPassword)
            return "New password must be different from the current password.";

        if (newPassword.Length < 20
            || !newPassword.Any(char.IsUpper)
            || !newPassword.Any(char.IsLower)
            || !newPassword.Any(char.IsDigit)
            || !newPassword.Any(ch => !char.IsLetterOrDigit(ch)))
        {
            return "New password must be at least 20 characters and include upper case, lower case, number, and symbol characters.";
        }

        return null;
    }

    private static string? NormalizeEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryBase64UrlDecode(string value, out byte[] decoded)
    {
        decoded = [];
        if (value.Any(c => !(char.IsLetterOrDigit(c) || c is '-' or '_')))
            return false;

        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized += (normalized.Length % 4) switch
        {
            0 => "",
            2 => "==",
            3 => "=",
            _ => ""
        };

        if (normalized.Length % 4 != 0)
            return false;

        try
        {
            decoded = Convert.FromBase64String(normalized);
            return true;
        }
        catch (FormatException)
        {
            decoded = [];
            return false;
        }
    }

    private sealed record SessionPayload(
        int UserId,
        int TenantId,
        long ExpiresAtUnixSeconds,
        long PasswordLastChangedAtTicks,
        int SessionVersion = 0,
        string CsrfToken = "");

    private sealed record LoginFailureState(
        int FailedLoginCount = 0,
        DateTime? LockedUntilUtc = null,
        bool LockoutStarted = false);
}

public record LoginResult(
    bool Succeeded,
    AuthenticatedUser? User,
    string? FailureReason,
    int? AuditUserId = null,
    int? AuditTenantId = null,
    string? AuditEmail = null,
    string? AuditDisplayName = null,
    bool AccountLocked = false,
    bool LockoutStarted = false,
    int FailedLoginCount = 0,
    DateTime? LockedUntilUtc = null)
{
    public static LoginResult Success(AuthenticatedUser user) =>
        new(
            true,
            user,
            null,
            user.UserId,
            user.TenantId,
            user.Email,
            user.DisplayName);

    public static LoginResult Failed(
        string failureReason,
        string? attemptedEmail = null,
        UserAccount? user = null,
        bool accountLocked = false,
        bool lockoutStarted = false,
        int failedLoginCount = 0,
        DateTime? lockedUntilUtc = null) =>
        new(
            false,
            null,
            failureReason,
            user?.Id,
            user?.TenantId,
            user?.Email ?? attemptedEmail,
            user?.DisplayName,
            accountLocked,
            lockoutStarted,
            failedLoginCount,
            lockedUntilUtc);
}

public record PasswordChangeResult(bool Succeeded, AuthenticatedUser? User, string? FailureReason)
{
    public static PasswordChangeResult Success(AuthenticatedUser user) =>
        new(true, user, null);

    public static PasswordChangeResult Failed(string failureReason) =>
        new(false, null, failureReason);
}
