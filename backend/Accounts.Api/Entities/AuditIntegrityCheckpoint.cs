namespace Accounts.Api.Entities;

public class AuditIntegrityCheckpoint
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int? TenantId { get; set; }
    public int LastAuditLogId { get; set; }
    public required string LastIntegrityHash { get; set; }
    public int CheckedEntries { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? CreatedByUserId { get; set; }
    public string? CreatedByDisplayName { get; set; }
    public string? RequestId { get; set; }
    public required string KeyId { get; set; }
    public required string Signature { get; set; }
}
