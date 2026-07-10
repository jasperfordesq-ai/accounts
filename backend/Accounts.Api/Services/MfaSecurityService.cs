using Accounts.Api.Rules;
using Microsoft.Extensions.Options;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Accounts.Api.Services;

/// <summary>
/// RFC 6238 TOTP and one-time recovery primitives. Credential secrets are
/// encrypted with AES-256-GCM using a purpose-separated key derived from the
/// deployment session secret. Opaque workflow tokens and recovery codes are
/// retained only as purpose-separated HMAC-SHA256 digests.
/// </summary>
public sealed class MfaSecurityService
{
    public const int TotpDigits = 6;
    public const int TotpPeriodSeconds = 30;
    private const int RecoveryCodeCount = 10;
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private readonly byte[] legacyEncryptionKey;
    private readonly IReadOnlyDictionary<string, byte[]> encryptionKeys;
    private readonly string? activeEncryptionKeyId;
    private readonly byte[] tokenHashKey;
    private readonly byte[] recoveryHashKey;

    public MfaSecurityService(
        IOptions<AuthSessionConfig> options,
        IOptions<IdentitySecurityConfig>? identityOptions = null)
    {
        var rootKey = AuthSessionKey.DecodeRequired(options.Value.SigningKey);
        legacyEncryptionKey = DeriveKey(rootKey, "accounts:mfa:secret-encryption:v1");
        var identityConfig = identityOptions?.Value ?? new IdentitySecurityConfig();
        var configuredKeyMaterial = identityConfig.MfaEncryptionKeys.Count > 0
            || !string.IsNullOrWhiteSpace(identityConfig.ActiveMfaEncryptionKeyId);
        if (configuredKeyMaterial && !IdentitySecurityConfig.HasValidProductionKeys(identityConfig))
            throw new InvalidOperationException("Identity MFA encryption key-ring configuration is invalid.");
        encryptionKeys = identityConfig.MfaEncryptionKeys.ToDictionary(
            item => item.KeyId,
            item => DeriveConfiguredKey(item.EncryptionKey, $"accounts:mfa:secret-encryption:v2:{item.KeyId}"),
            StringComparer.Ordinal);
        activeEncryptionKeyId = encryptionKeys.Count == 0 ? null : identityConfig.ActiveMfaEncryptionKeyId;
        var identityHmacRoot = IdentitySecurityConfig.DecodeKey(identityConfig.IdentityHmacKey);
        var separateIdentityHmac = identityHmacRoot.Length >= 32;
        if (!separateIdentityHmac) identityHmacRoot = rootKey;
        tokenHashKey = DeriveKey(identityHmacRoot, separateIdentityHmac
            ? "accounts:identity:opaque-token:v2"
            : "accounts:identity:opaque-token:v1");
        recoveryHashKey = DeriveKey(identityHmacRoot, separateIdentityHmac
            ? "accounts:mfa:recovery-code:v2"
            : "accounts:mfa:recovery-code:v1");
        if (!ReferenceEquals(identityHmacRoot, rootKey)) CryptographicOperations.ZeroMemory(identityHmacRoot);
        CryptographicOperations.ZeroMemory(rootKey);
    }

    public int ActiveSecretVersion => activeEncryptionKeyId is null ? 1 : 2;

    public string GenerateSecret() => Base32Encode(RandomNumberGenerator.GetBytes(20));

    public string EncryptSecret(string base32Secret)
    {
        _ = Base32Decode(base32Secret);
        if (activeEncryptionKeyId is { } keyId)
            return EncryptV2(base32Secret, keyId, encryptionKeys[keyId]);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintext = Encoding.ASCII.GetBytes(base32Secret);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(legacyEncryptionKey, tag.Length);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, Encoding.UTF8.GetBytes("accounts-totp-v1"));
        CryptographicOperations.ZeroMemory(plaintext);
        return $"v1.{Base64UrlEncode(nonce)}.{Base64UrlEncode(ciphertext)}.{Base64UrlEncode(tag)}";
    }

    public string DecryptSecret(string encryptedSecret)
        => DecryptSecretWithRotation(encryptedSecret).Secret;

    public DecryptedMfaSecret DecryptSecretWithRotation(string encryptedSecret)
    {
        var parts = encryptedSecret.Split('.');
        if (parts is { Length: 5 } && parts[0] == "v2")
        {
            var keyId = parts[1];
            if (!encryptionKeys.TryGetValue(keyId, out var key))
                throw new InvalidOperationException("The MFA credential references an unavailable encryption key.");
            var secret = DecryptEnvelope(
                parts[2],
                parts[3],
                parts[4],
                key,
                $"accounts-totp-v2:{keyId}");
            return new DecryptedMfaSecret(secret, keyId != activeEncryptionKeyId, 2, keyId);
        }
        if (parts is not { Length: 4 } || parts[0] != "v1")
            throw new InvalidOperationException("Unsupported MFA credential secret envelope.");
        return new DecryptedMfaSecret(
            DecryptEnvelope(parts[1], parts[2], parts[3], legacyEncryptionKey, "accounts-totp-v1"),
            activeEncryptionKeyId is not null,
            1,
            "legacy-session-v1");
    }

    private static string EncryptV2(string base32Secret, string keyId, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintext = Encoding.ASCII.GetBytes(base32Secret);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        try
        {
            using var aes = new AesGcm(key, tag.Length);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, Encoding.UTF8.GetBytes($"accounts-totp-v2:{keyId}"));
            return $"v2.{keyId}.{Base64UrlEncode(nonce)}.{Base64UrlEncode(ciphertext)}.{Base64UrlEncode(tag)}";
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static string DecryptEnvelope(
        string encodedNonce,
        string encodedCiphertext,
        string encodedTag,
        byte[] key,
        string associatedData)
    {
        var nonce = Base64UrlDecode(encodedNonce);
        var ciphertext = Base64UrlDecode(encodedCiphertext);
        var tag = Base64UrlDecode(encodedTag);
        if (nonce.Length != 12 || tag.Length != 16 || ciphertext.Length is < 16 or > 128)
            throw new InvalidOperationException("Invalid MFA credential secret envelope.");
        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(key, tag.Length);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, Encoding.UTF8.GetBytes(associatedData));
            var secret = Encoding.ASCII.GetString(plaintext);
            _ = Base32Decode(secret);
            return secret;
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException("MFA credential secret integrity check failed.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public bool VerifyTotp(string base32Secret, string? suppliedCode, DateTimeOffset now)
        => TryVerifyTotp(base32Secret, suppliedCode, now, -1, out _);

    public bool TryVerifyTotp(
        string base32Secret,
        string? suppliedCode,
        DateTimeOffset now,
        long lastAcceptedCounter,
        out long acceptedCounter)
    {
        acceptedCounter = -1;
        var normalized = suppliedCode?.Trim();
        if (normalized is not { Length: TotpDigits } || normalized.Any(character => character is < '0' or > '9'))
            return false;

        var supplied = Encoding.ASCII.GetBytes(normalized);
        var counter = now.ToUnixTimeSeconds() / TotpPeriodSeconds;
        for (var offset = -1; offset <= 1; offset++)
        {
            var candidateCounter = counter + offset;
            var expected = Encoding.ASCII.GetBytes(ComputeTotp(base32Secret, candidateCounter));
            var matches = CryptographicOperations.FixedTimeEquals(expected, supplied);
            CryptographicOperations.ZeroMemory(expected);
            if (matches && candidateCounter > lastAcceptedCounter)
            {
                CryptographicOperations.ZeroMemory(supplied);
                acceptedCounter = candidateCounter;
                return true;
            }
        }

        CryptographicOperations.ZeroMemory(supplied);
        return false;
    }

    public string ComputeTotpForTesting(string base32Secret, DateTimeOffset now) =>
        ComputeTotp(base32Secret, now.ToUnixTimeSeconds() / TotpPeriodSeconds);

    public string CreateOpaqueToken() => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public string HashOpaqueToken(string rawToken) =>
        HexHmac(tokenHashKey, Encoding.UTF8.GetBytes(rawToken.Trim()));

    public IReadOnlyList<string> GenerateRecoveryCodes() => Enumerable.Range(0, RecoveryCodeCount)
        .Select(_ =>
        {
            var value = Base32Encode(RandomNumberGenerator.GetBytes(10))[..12];
            return $"{value[..4]}-{value[4..8]}-{value[8..12]}";
        })
        .ToArray();

    public string HashRecoveryCode(int userId, string rawCode)
    {
        var normalized = NormalizeRecoveryCode(rawCode);
        return HexHmac(recoveryHashKey, Encoding.UTF8.GetBytes($"{userId}:{normalized}"));
    }

    public static string NormalizeRecoveryCode(string? rawCode) =>
        new((rawCode ?? "")
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());

    public static string BuildOtpAuthUri(string issuer, string accountName, string secret) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(accountName)}" +
        $"?secret={Uri.EscapeDataString(secret)}&issuer={Uri.EscapeDataString(issuer)}" +
        $"&algorithm=SHA1&digits={TotpDigits}&period={TotpPeriodSeconds}";

    private static string ComputeTotp(string base32Secret, long counter)
    {
        var secret = Base32Decode(base32Secret);
        Span<byte> counterBytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counterBytes, counter);
        using var hmac = new HMACSHA1(secret);
        var digest = hmac.ComputeHash(counterBytes.ToArray());
        CryptographicOperations.ZeroMemory(secret);
        var offset = digest[^1] & 0x0f;
        var binary = ((digest[offset] & 0x7f) << 24)
            | (digest[offset + 1] << 16)
            | (digest[offset + 2] << 8)
            | digest[offset + 3];
        CryptographicOperations.ZeroMemory(digest);
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static byte[] DeriveKey(byte[] rootKey, string purpose)
    {
        using var hmac = new HMACSHA256(rootKey);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(purpose));
    }

    private static byte[] DeriveConfiguredKey(string encodedKey, string purpose)
    {
        var material = IdentitySecurityConfig.DecodeKey(encodedKey);
        try { return DeriveKey(material, purpose); }
        finally { CryptographicOperations.ZeroMemory(material); }
    }

    private static string HexHmac(byte[] key, byte[] value)
    {
        using var hmac = new HMACSHA256(key);
        return Convert.ToHexStringLower(hmac.ComputeHash(value));
    }

    private static string Base32Encode(byte[] bytes)
    {
        var output = new StringBuilder((bytes.Length * 8 + 4) / 5);
        var buffer = 0;
        var bitsLeft = 0;
        foreach (var value in bytes)
        {
            buffer = (buffer << 8) | value;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                output.Append(Base32Alphabet[(buffer >> (bitsLeft - 5)) & 31]);
                bitsLeft -= 5;
            }
        }

        if (bitsLeft > 0)
            output.Append(Base32Alphabet[(buffer << (5 - bitsLeft)) & 31]);
        return output.ToString();
    }

    private static byte[] Base32Decode(string value)
    {
        var normalized = value.Trim().Replace(" ", "", StringComparison.Ordinal).TrimEnd('=').ToUpperInvariant();
        if (normalized.Length is < 16 or > 128)
            throw new FormatException("Invalid Base32 MFA secret.");

        var output = new List<byte>(normalized.Length * 5 / 8);
        var buffer = 0;
        var bitsLeft = 0;
        foreach (var character in normalized)
        {
            var index = Base32Alphabet.IndexOf(character);
            if (index < 0)
                throw new FormatException("Invalid Base32 MFA secret.");
            buffer = (buffer << 5) | index;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                output.Add((byte)((buffer >> (bitsLeft - 8)) & 255));
                bitsLeft -= 8;
            }
        }

        return output.ToArray();
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized += (normalized.Length % 4) switch { 0 => "", 2 => "==", 3 => "=", _ => throw new FormatException("Invalid Base64Url value.") };
        return Convert.FromBase64String(normalized);
    }
}

public sealed record DecryptedMfaSecret(
    string Secret,
    bool NeedsRewrap,
    int EnvelopeVersion,
    string KeyId);
