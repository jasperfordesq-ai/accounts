namespace Accounts.Api.Entities;

/// <summary>
/// Tenant-scoped command replay evidence. The reservation and business mutation are committed in
/// one transaction, so an InProgress row is never durable. Completed rows retain the exact response
/// contract until expiry and can then be deleted by the bounded retention worker.
/// </summary>
public sealed class IdempotencyRecord
{
    public long Id { get; set; }
    public int TenantId { get; set; }
    public required string IdempotencyKey { get; set; }
    public required string Operation { get; set; }
    public required string RequestFingerprintSha256 { get; set; }
    public required string Status { get; set; }
    public required string CreatedByUserId { get; set; }
    public required string CreatedByDisplayName { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public string? ResultResourceType { get; set; }
    public string? ResultResourceId { get; set; }
    public int? ResultHttpStatusCode { get; set; }
    public string? ResponseJson { get; set; }
    public string? ResponseSha256 { get; set; }
}
