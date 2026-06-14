namespace Accounts.Api.Rules;

public class AuditIntegrityConfig
{
    public string ActiveKeyId { get; set; } = "";
    public List<AuditIntegritySigningKeyConfig> SigningKeys { get; set; } = [];
}

public class AuditIntegritySigningKeyConfig
{
    public string KeyId { get; set; } = "";
    public string SigningKey { get; set; } = "";
}
