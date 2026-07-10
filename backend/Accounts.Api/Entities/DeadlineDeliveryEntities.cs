using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

public enum DeadlineReminderKind
{
    DueSoon,
    Overdue,
    Corrected
}

public enum DeadlineReminderState
{
    Pending,
    Delivering,
    RetryScheduled,
    Delivered,
    Cancelled,
    Superseded
}

public enum PlatformJobKind
{
    DeadlineReminder,
    Backup,
    Restore
}

public enum PlatformJobStatus
{
    Running,
    Succeeded,
    PartiallySucceeded,
    Failed
}

/// <summary>
/// Durable, tenant-owned notification intent. The row deliberately contains identifiers and a
/// deadline only: client names, email addresses and free-form provider errors are never retained.
/// </summary>
public sealed class DeadlineReminderOutbox
{
    public Guid Id { get; set; }
    public int TenantId { get; set; }
    public int CompanyId { get; set; }
    public int PeriodId { get; set; }
    public int FilingDeadlineId { get; set; }
    public DeadlineType DeadlineType { get; set; }
    public DeadlineReminderKind ReminderKind { get; set; }
    public DeadlineReminderState State { get; set; }
    public DateOnly ObservedDueDate { get; set; }
    public string? ObservedCalculationFingerprintSha256 { get; set; }
    public required string DeduplicationKeySha256 { get; set; }
    public int AttemptCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public DateTime? LastAttemptAtUtc { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? LastFailureCode { get; set; }
    public string? ProviderDeliveryReference { get; set; }
    public int Revision { get; set; } = 1;

    [JsonIgnore]
    public Tenant Tenant { get; set; } = null!;
    [JsonIgnore]
    public Company Company { get; set; } = null!;
    [JsonIgnore]
    public AccountingPeriod Period { get; set; } = null!;
    [JsonIgnore]
    public FilingDeadline FilingDeadline { get; set; } = null!;
}

/// <summary>
/// Durable scheduled/manual job evidence. Counts and fixed failure codes are retained, never
/// exception messages, recipient details, company names or other client data.
/// </summary>
public sealed class PlatformJobRun
{
    public Guid Id { get; set; }
    public int TenantId { get; set; }
    public PlatformJobKind JobKind { get; set; }
    public required string Trigger { get; set; }
    public PlatformJobStatus Status { get; set; }
    public DateTime ScheduledSlotUtc { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int ExaminedCount { get; set; }
    public int EnqueuedCount { get; set; }
    public int DeliveredCount { get; set; }
    public int FailedCount { get; set; }
    public int CancelledCount { get; set; }
    public string? FailureCode { get; set; }
    public string? EvidenceSha256 { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    [JsonIgnore]
    public Tenant Tenant { get; set; } = null!;
}
