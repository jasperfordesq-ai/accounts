using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Accounts.Api.Services;

public sealed record ConnectorAccessPayload(
    Guid SessionId,
    Guid FamilyId,
    int UserId,
    int TenantId,
    long IssuedAtUnixSeconds,
    long ExpiresAtUnixSeconds,
    long PasswordLastChangedAtTicks);

/// <summary>
/// Signs and verifies connector bearer tokens. Access tokens are <c>fbc1.&lt;payload&gt;.&lt;sig&gt;</c>
/// with an HMAC-SHA256 over the prefix and payload, using a key distinct from the browser session
/// key. Refresh tokens are opaque <c>fbr1.&lt;tenant&gt;.&lt;random&gt;</c>; only their keyed hash is stored.
/// The tenant id inside a refresh token is not secret and only selects the RLS tenant before lookup.
/// </summary>
public sealed class ConnectorTokenCodec(byte[] signingKey)
{
    public const string AccessPrefix = "fbc1";
    public const string RefreshPrefix = "fbr1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string CreateAccessToken(ConnectorAccessPayload payload)
    {
        var encoded = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions));
        return $"{AccessPrefix}.{encoded}.{Base64UrlEncode(Sign(encoded))}";
    }

    public ConnectorAccessPayload? ReadAccessToken(string? token, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 2048) return null;
        var parts = token.Trim().Split('.');
        if (parts.Length != 3 || parts[0] != AccessPrefix || parts[1].Length == 0 || parts[2].Length == 0)
            return null;
        if (!TryBase64UrlDecode(parts[2], out var presented)) return null;
        var expected = Sign(parts[1]);
        if (presented.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(presented, expected))
            return null;
        if (!TryBase64UrlDecode(parts[1], out var payloadBytes)) return null;
        try
        {
            var payload = JsonSerializer.Deserialize<ConnectorAccessPayload>(payloadBytes, JsonOptions);
            if (payload is null
                || payload.SessionId == Guid.Empty
                || payload.FamilyId == Guid.Empty
                || payload.UserId <= 0
                || payload.TenantId <= 0
                || payload.PasswordLastChangedAtTicks <= 0
                || payload.ExpiresAtUnixSeconds <= now.ToUnixTimeSeconds()
                || payload.IssuedAtUnixSeconds > now.AddMinutes(1).ToUnixTimeSeconds())
                return null;
            return payload;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string CreateRefreshToken(int tenantId) =>
        $"{RefreshPrefix}.{Base64UrlEncode(Encoding.ASCII.GetBytes(tenantId.ToString(System.Globalization.CultureInfo.InvariantCulture)))}.{Base64UrlEncode(RandomNumberGenerator.GetBytes(32))}";

    public static int? ReadRefreshTokenTenant(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 512) return null;
        var parts = token.Trim().Split('.');
        if (parts.Length != 3 || parts[0] != RefreshPrefix || parts[2].Length < 40) return null;
        if (!TryBase64UrlDecode(parts[1], out var tenantBytes)) return null;
        return int.TryParse(Encoding.ASCII.GetString(tenantBytes), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var tenantId) && tenantId > 0
            ? tenantId
            : null;
    }

    private byte[] Sign(string encodedPayload) =>
        HMACSHA256.HashData(signingKey, Encoding.ASCII.GetBytes($"{AccessPrefix}.{encodedPayload}"));

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryBase64UrlDecode(string value, out byte[] decoded)
    {
        decoded = [];
        if (value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
            return false;
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        try
        {
            decoded = Convert.FromBase64String(padded);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
