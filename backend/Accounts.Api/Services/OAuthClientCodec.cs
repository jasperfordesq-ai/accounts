using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Accounts.Api.Services;

public sealed record OAuthClientRegistration(
    string ClientName,
    IReadOnlyList<string> RedirectUris,
    long IssuedAtUnixSeconds);

/// <summary>
/// Stateless dynamic client registration. The client id is the registration itself
/// (<c>fbcl1.&lt;payload&gt;.&lt;sig&gt;</c>), HMAC-signed with a key derived from the connector signing
/// key, so no global (tenant-less) table is needed and a client id cannot be forged or edited.
/// </summary>
public sealed class OAuthClientCodec
{
    public const string Prefix = "fbcl1";
    public const int MaxClientIdLength = 4096;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly byte[] key;

    public OAuthClientCodec(byte[] connectorSigningKey)
    {
        key = HMACSHA256.HashData(connectorSigningKey, Encoding.ASCII.GetBytes("accounts:connector:oauth-client:v1"));
    }

    public string Encode(OAuthClientRegistration registration)
    {
        var payload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(
            new ClientPayload(registration.ClientName, registration.RedirectUris.ToArray(), registration.IssuedAtUnixSeconds), JsonOptions));
        return $"{Prefix}.{payload}.{Base64UrlEncode(Sign(payload))}";
    }

    public OAuthClientRegistration? Decode(string? clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId) || clientId.Length > MaxClientIdLength) return null;
        var parts = clientId.Split('.');
        if (parts.Length != 3 || parts[0] != Prefix) return null;
        if (!TryBase64UrlDecode(parts[2], out var presented)) return null;
        var expected = Sign(parts[1]);
        if (presented.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(presented, expected)) return null;
        if (!TryBase64UrlDecode(parts[1], out var bytes)) return null;
        try
        {
            var payload = JsonSerializer.Deserialize<ClientPayload>(bytes, JsonOptions);
            if (payload is null || payload.RedirectUris is not { Length: > 0 } || string.IsNullOrWhiteSpace(payload.ClientName))
                return null;
            return new OAuthClientRegistration(payload.ClientName, payload.RedirectUris, payload.IssuedAt);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string Hash(string clientId) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(clientId)));

    private byte[] Sign(string payload) => HMACSHA256.HashData(key, Encoding.ASCII.GetBytes($"{Prefix}.{payload}"));

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryBase64UrlDecode(string value, out byte[] decoded)
    {
        decoded = [];
        if (value.Length == 0 || value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
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

    private sealed record ClientPayload(string ClientName, string[] RedirectUris, long IssuedAt);
}
