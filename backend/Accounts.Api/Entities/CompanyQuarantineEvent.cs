namespace Accounts.Api.Entities;

/// <summary>
/// Append-only evidence for company quarantine and recovery. This intentionally has no foreign key
/// to Company so the evidence survives any future retention-policy disposal of the company row.
/// </summary>
public sealed class CompanyQuarantineEvent
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int TenantId { get; set; }
    public required string CompanyLegalName { get; set; }
    public required string EventType { get; set; }
    public required string ActorUserId { get; set; }
    public required string ActorDisplayName { get; set; }
    public required string ActorRole { get; set; }
    public required string Reason { get; set; }
    public required string TypedConfirmation { get; set; }
    public required string InventoryJson { get; set; }
    public required string InventorySha256 { get; set; }
    public long TotalDependentRows { get; set; }
    public string? PreviousEvidenceSha256 { get; set; }
    public required string EvidenceSha256 { get; set; }
    public string? RequestId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}
