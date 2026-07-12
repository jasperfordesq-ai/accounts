namespace Accounts.Api.Rules;

public sealed class PrivateInitializationConfig
{
    public string TenantName { get; set; } = "";
    public string TenantSlug { get; set; } = "";
    public string OwnerEmail { get; set; } = "";
    public string OwnerDisplayName { get; set; } = "";
    public string OwnerInitialPassword { get; set; } = "";
}

public sealed class PrivateOwnerRecoveryConfig
{
    public const string RequiredConfirmationPhrase = "RECOVER PRIVATE SERVER OWNER";

    public string ConfirmInstallationId { get; set; } = "";
    public string TenantSlug { get; set; } = "";
    public string OwnerEmail { get; set; } = "";
    public string ConfirmOwnerEmail { get; set; } = "";
    public string ConfirmationPhrase { get; set; } = "";
}
