namespace Accounts.Api.Data;

public sealed class DatabaseTenantIsolationConfig
{
    public const int MinimumSigningKeyBytes = 32;
    public const string TenantSignatureSettingName = "accounts.tenant_signature";

    public bool Required { get; set; }
    public string ApplicationGroupRole { get; set; } = TenantIsolationPolicyCatalog.ApplicationGroupRole;
    public string ApplicationLoginRole { get; set; } = "accounts_api";
    public string ContextSigningKey { get; set; } = "";

    public static bool IsValid(DatabaseTenantIsolationConfig config)
    {
        if (!config.Required && string.IsNullOrWhiteSpace(config.ContextSigningKey)) return true;
        return string.Equals(
                config.ApplicationGroupRole,
                TenantIsolationPolicyCatalog.ApplicationGroupRole,
                StringComparison.Ordinal)
            && IsSafeRoleName(config.ApplicationGroupRole)
            && IsSafeRoleName(config.ApplicationLoginRole)
            && !string.Equals(config.ApplicationLoginRole, config.ApplicationGroupRole, StringComparison.Ordinal)
            && !string.Equals(
                config.ApplicationLoginRole,
                TenantIsolationPolicyCatalog.AdministratorGroupRole,
                StringComparison.Ordinal)
            && DecodeContextSigningKey(config.ContextSigningKey).Length >= MinimumSigningKeyBytes;
    }

    public static byte[] DecodeContextSigningKey(string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded)) return [];
        try
        {
            return Convert.FromBase64String(encoded.Trim());
        }
        catch (FormatException)
        {
            return [];
        }
    }

    public static bool IsSafeRoleName(string? roleName) =>
        roleName is { Length: >= 3 and <= 63 }
        && roleName[0] is >= 'a' and <= 'z'
        && roleName.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_');
}
