namespace Accounts.Api.Rules;

public class BootstrapOwnerConfig
{
    public bool Enabled { get; set; }
    public string TenantName { get; set; } = "";
    public string TenantSlug { get; set; } = "";
    public string OwnerEmail { get; set; } = "";
    public string OwnerDisplayName { get; set; } = "";
    public string OwnerInitialPassword { get; set; } = "";
    public bool OwnerMustChangePassword { get; set; } = true;
}
