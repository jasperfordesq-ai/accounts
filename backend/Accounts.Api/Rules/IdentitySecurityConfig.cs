namespace Accounts.Api.Rules;

public sealed class IdentitySecurityConfig
{
    public bool RequireInProduction { get; set; } = true;
    public bool BreachedPasswordCheckEnabled { get; set; } = true;
    public bool BreachedPasswordFailClosed { get; set; } = true;
    public string PwnedPasswordsRangeBaseUrl { get; set; } = "https://api.pwnedpasswords.com/range/";
    public int PwnedPasswordsTimeoutSeconds { get; set; } = 5;
    public int PwnedPasswordsMaximumResponseBytes { get; set; } = 2_000_000;
    public string IdentityHmacKey { get; set; } = "";
    public string ActiveMfaEncryptionKeyId { get; set; } = "";
    public List<MfaEncryptionKeyConfig> MfaEncryptionKeys { get; set; } = [];

    public static bool HasValidProductionKeys(IdentitySecurityConfig config) =>
        DecodeKey(config.IdentityHmacKey).Length >= 32
        && config.ActiveMfaEncryptionKeyId is { Length: >= 3 and <= 40 }
        && config.MfaEncryptionKeys.Count is >= 1 and <= 8
        && config.MfaEncryptionKeys.Select(item => item.KeyId).Distinct(StringComparer.Ordinal).Count()
            == config.MfaEncryptionKeys.Count
        && config.MfaEncryptionKeys.Any(item => item.KeyId == config.ActiveMfaEncryptionKeyId)
        && config.MfaEncryptionKeys.All(item =>
            item.KeyId is { Length: >= 3 and <= 40 }
            && item.KeyId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            && DecodeKey(item.EncryptionKey).Length >= 32);

    public static byte[] DecodeKey(string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded)) return [];
        try { return Convert.FromBase64String(encoded.Trim()); }
        catch (FormatException) { return []; }
    }
}

public sealed class MfaEncryptionKeyConfig
{
    public string KeyId { get; set; } = "";
    public string EncryptionKey { get; set; } = "";
}
