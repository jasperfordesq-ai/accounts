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

    private const int PasswordIterations = 210_000;
    private const int MinimumSaltBytes = 16;
    private const int MinimumStoredHashBytes = 16;
    private static readonly JsonSerializerOptions SessionJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AccountsDbContext db;
    private readonly AuthSessionConfig config;
    private readonly IHostEnvironment environment;
    private readonly byte[] signingKey;

    public AuthService(
        AccountsDbContext db,
        IOptions<AuthSessionConfig> config,
        IHostEnvironment environment)
    {
        this.db = db;
        this.config = config.Value;
        this.environment = environment;
        signingKey = AuthSessionKey.DecodeRequired(this.config.SigningKey);
    }

    public string CookieName =>
        string.IsNullOrWhiteSpace(config.CookieName) ? "accounts_session" : config.CookieName.Trim();

    public async Task<LoginResult> LoginAsync(string? email, string? password)
    {
        var normalizedEmail = NormalizeEmail(email);
        if (normalizedEmail is null || string.IsNullOrWhiteSpace(password))
            return LoginResult.Failed("Email and password are required.");

        var user = await db.UserAccounts
            .Include(u => u.Tenant)
            .SingleOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);

        if (user is null || user.Tenant is null)
            return LoginResult.Failed("Invalid email or password.");

        if (user.PasswordAlgorithm != PasswordAlgorithm)
            return LoginResult.Failed("Invalid email or password.");

        if (!VerifyPassword(user, password))
            return LoginResult.Failed("Invalid email or password.");

        if (!user.IsActive)
            return LoginResult.Failed("User account is inactive.");

        user.LastLoginAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return LoginResult.Success(ToPrincipal(user));
    }

    public string CreateSessionCookieValue(AuthenticatedUser user, DateTimeOffset now)
    {
        var payload = new SessionPayload(
            user.UserId,
            user.TenantId,
            now.AddMinutes(ClampedExpiryMinutes).ToUnixTimeSeconds());
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, SessionJsonOptions);
        var encodedPayload = Base64UrlEncode(payloadBytes);
        var signature = Base64UrlEncode(ComputeSignature(encodedPayload));

        return $"{encodedPayload}.{signature}";
    }

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
                || payload.ExpiresAtUnixSeconds <= now.ToUnixTimeSeconds())
                return null;

            var user = await db.UserAccounts
                .Include(u => u.Tenant)
                .SingleOrDefaultAsync(u => u.Id == payload.UserId);

            if (!CanAuthenticate(user) || user!.TenantId != payload.TenantId)
                return null;

            return ToPrincipal(user);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public CookieOptions CreateCookieOptions(DateTimeOffset now) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = now.AddMinutes(ClampedExpiryMinutes),
        Secure = environment.IsProduction()
    };

    public CookieOptions ClearCookieOptions() => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = DateTimeOffset.UnixEpoch,
        Secure = environment.IsProduction()
    };

    private int ClampedExpiryMinutes => Math.Clamp(config.ExpiryMinutes, 15, 1_440);

    private static bool CanAuthenticate(UserAccount? user) =>
        user is not null
        && user.IsActive
        && user.Tenant is not null
        && user.PasswordAlgorithm == PasswordAlgorithm;

    private static bool VerifyPassword(UserAccount user, string password)
    {
        if (!TryDecodeStoredPassword(user, out var salt, out var storedHash))
            return false;

        var candidateHash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            PasswordIterations,
            HashAlgorithmName.SHA256,
            storedHash.Length);

        return CryptographicOperations.FixedTimeEquals(candidateHash, storedHash);
    }

    private static bool TryDecodeStoredPassword(UserAccount user, out byte[] salt, out byte[] storedHash)
    {
        salt = [];
        storedHash = [];

        try
        {
            salt = Convert.FromBase64String(user.PasswordSalt);
            storedHash = Convert.FromBase64String(user.PasswordHash);
        }
        catch (FormatException)
        {
            return false;
        }

        return salt.Length >= MinimumSaltBytes && storedHash.Length >= MinimumStoredHashBytes;
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
        user.Role);

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
        long ExpiresAtUnixSeconds);
}

public record LoginResult(bool Succeeded, AuthenticatedUser? User, string? FailureReason)
{
    public static LoginResult Success(AuthenticatedUser user) =>
        new(true, user, null);

    public static LoginResult Failed(string failureReason) =>
        new(false, null, failureReason);
}
