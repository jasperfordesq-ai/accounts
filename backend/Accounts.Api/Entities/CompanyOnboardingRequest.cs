namespace Accounts.Api.Entities;

/// <summary>
/// Read-only historical evidence from the pre-generic atomic onboarding implementation. Migration
/// 20260711010000 backfills every completed key into <see cref="IdempotencyRecord"/>. Production
/// commands no longer read or write this table; it remains protected by its immutable PostgreSQL
/// trigger solely so old release evidence is not discarded during the retention transition.
/// </summary>
public sealed class CompanyOnboardingRequest
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public required string IdempotencyKey { get; set; }
    public required string RequestSha256 { get; set; }
    public required string Status { get; set; }
    public int? CompanyId { get; set; }
    public int? PeriodId { get; set; }
    public int? BankAccountId { get; set; }
    public int CategoryCount { get; set; }
    public required string CreatedByUserId { get; set; }
    public required string CreatedByDisplayName { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? ResponseJson { get; set; }
    public string? ResponseSha256 { get; set; }
}
