namespace Accounts.Api.Entities;

public class AuditLog
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public int? PeriodId { get; set; }
    public required string EntityType { get; set; }
    public int EntityId { get; set; }
    public required string Action { get; set; }
    public string? OldValueJson { get; set; }
    public string? NewValueJson { get; set; }
    public string? UserId { get; set; }
    public int? TenantId { get; set; }
    public string? RequestId { get; set; }
    public string? ActorDisplayName { get; set; }
    public string? PreviousIntegrityHash { get; set; }
    public string? IntegrityHash { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
