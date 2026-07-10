using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

/// <summary>
/// Short-lived, minimised login telemetry. The submitted identifier, password, IP address, user
/// agent and HTTP payload are deliberately absent. IdentifierFingerprint is a keyed HMAC.
/// </summary>
public sealed class LoginSecurityEvent
{
    public long Id { get; set; }
    public int? TenantId { get; set; }
    public int? UserId { get; set; }
    public required string IdentifierFingerprint { get; set; }
    public required string OutcomeCode { get; set; }
    public required string ReasonCode { get; set; }
    public string? CorrelationId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}

/// <summary>
/// Durable decision metadata for a subject access or erasure review. Export bytes are streamed and
/// never retained here; only their digest, size and inventory counts remain.
/// </summary>
public sealed class PrivacySubjectRequest
{
    public Guid Id { get; set; }
    public int TenantId { get; set; }
    public int SubjectUserId { get; set; }
    public required string RequestKind { get; set; }
    public required string State { get; set; }
    public int RequestedByUserId { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public int? DecidedByUserId { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public string? DecisionReason { get; set; }
    public string? ExportSha256 { get; set; }
    public long? ExportByteCount { get; set; }
    public string? LocatedRecordCountsJson { get; set; }
    public bool StatutoryRetentionOverrideApplied { get; set; }
    public string? StatutoryRetentionLegalBasis { get; set; }
    public DateTime? StatutoryRetainUntilUtc { get; set; }
    public string? StatutoryRetentionInventoryJson { get; set; }
    public DateTime MetadataExpiresAtUtc { get; set; }
}

/// <summary>A synthetic or real privacy-incident exercise/decision ledger with no client payload.</summary>
public sealed class PrivacyIncidentExercise
{
    public Guid Id { get; set; }
    public int TenantId { get; set; }
    public required string ExerciseKind { get; set; }
    public required string ReleaseCandidate { get; set; }
    public required string EnvironmentName { get; set; }
    public required string ScenarioSha256 { get; set; }
    public DateTime DetectedAtUtc { get; set; }
    public DateTime NotificationRoutedAtUtc { get; set; }
    public DateTime ContainedAtUtc { get; set; }
    public DateTime EvidencePreservedAtUtc { get; set; }
    public DateTime RecoveryVerifiedAtUtc { get; set; }
    public DateTime ReviewedAtUtc { get; set; }
    public int ReviewedByUserId { get; set; }
    public required string NotificationDecision { get; set; }
    public required string EvidenceManifestSha256 { get; set; }
    public required string ReviewDecision { get; set; }
    public bool UsedSyntheticDataOnly { get; set; }
    public bool TenantIsolationVerified { get; set; }
    public bool AuditIntegrityVerified { get; set; }
    public bool FinancialIntegrityVerified { get; set; }
}
