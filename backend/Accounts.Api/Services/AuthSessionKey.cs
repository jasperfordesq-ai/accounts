using System.Security.Cryptography;

namespace Accounts.Api.Services;

public static class AuthSessionKey
{
    private const int MinimumSigningKeyBytes = 32;
    private const int MinimumSigningKeyDistinctEncodedChars = 8;
    private const int MinimumSigningKeyDistinctBytes = 16;
    private const string DevelopmentSigningKeyBase64 = "IyRih0V4m+9WHp+buroLFov10a+LGRhgg8g4J7vm3uTnRtUU6t1JenYYfZTaqT9Gl9H7FmYhGwNORssVZ/BPkg==";
    private static readonly byte[] DevelopmentSigningKeyBytes = Convert.FromBase64String(DevelopmentSigningKeyBase64);

    public static bool HasStrongKey(string? signingKey)
    {
        if (string.IsNullOrWhiteSpace(signingKey))
            return false;

        var trimmed = signingKey.Trim();
        if (!TryDecode(trimmed, out var decoded))
            return false;

        return decoded.Length >= MinimumSigningKeyBytes
            && trimmed.TrimEnd('=').Distinct().Count() >= MinimumSigningKeyDistinctEncodedChars
            && decoded.Distinct().Count() >= MinimumSigningKeyDistinctBytes;
    }

    public static byte[] DecodeRequired(string? signingKey)
    {
        if (!HasStrongKey(signingKey))
            throw new InvalidOperationException("AuthSession:SigningKey must be a generated Base64 or Base64Url-encoded secret of at least 32 bytes.");

        TryDecode(signingKey!.Trim(), out var decoded);
        return decoded;
    }

    public static bool IsKnownDevelopmentKey(string? signingKey)
    {
        if (string.IsNullOrWhiteSpace(signingKey))
            return false;

        return TryDecode(signingKey.Trim(), out var decoded)
            && decoded.Length == DevelopmentSigningKeyBytes.Length
            && CryptographicOperations.FixedTimeEquals(decoded, DevelopmentSigningKeyBytes);
    }

    public static bool TryDecode(string signingKey, out byte[] decoded)
    {
        if (TryDecodeBase64(signingKey, out decoded))
            return true;

        return TryDecodeBase64Url(signingKey, out decoded);
    }

    private static bool TryDecodeBase64(string value, out byte[] decoded)
    {
        try
        {
            decoded = Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException)
        {
            decoded = [];
            return false;
        }
    }

    private static bool TryDecodeBase64Url(string value, out byte[] decoded)
    {
        if (value.Any(c => !(char.IsLetterOrDigit(c) || c is '-' or '_' or '=')))
        {
            decoded = [];
            return false;
        }

        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized += (normalized.Length % 4) switch
        {
            0 => "",
            2 => "==",
            3 => "=",
            _ => ""
        };

        if (normalized.Length % 4 != 0)
        {
            decoded = [];
            return false;
        }

        return TryDecodeBase64(normalized, out decoded);
    }
}
