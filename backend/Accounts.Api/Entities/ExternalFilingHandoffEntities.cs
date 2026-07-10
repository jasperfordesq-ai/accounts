using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

/// <summary>
/// Append-only presenter/agent engagement evidence. Revocation or replacement creates a new row
/// linked through SupersedesAuthorityId; retained authority evidence is never rewritten.
/// </summary>
public sealed class FilingAuthorityEngagement
{
    public long Id { get; set; }
    public int TenantId { get; set; }
    public int CompanyId { get; set; }
    public int Version { get; set; }
    public long? SupersedesAuthorityId { get; set; }
    public required string Workflow { get; set; }
    public required string Kind { get; set; }
    public required string Status { get; set; }
    public required string LegalName { get; set; }
    public string? PracticeName { get; set; }
    public string? MaskedPresenterOrTain { get; set; }
    public required string AuthorityScope { get; set; }
    public required string EngagementReference { get; set; }
    public required string ExternalAuthorityReference { get; set; }
    public DateTime EffectiveFromUtc { get; set; }
    public DateTime? EffectiveUntilUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    [JsonIgnore]
    public required byte[] AuthorityEvidenceArtifact { get; set; }
    public required string AuthorityEvidenceSha256 { get; set; }
    public required string EvidenceMediaType { get; set; }
    public required string EvidenceFileName { get; set; }
    public required string ReviewedByUserId { get; set; }
    public required string ReviewedByDisplayName { get; set; }
    public required string ReviewedByRole { get; set; }
    public DateTime ReviewedAtUtc { get; set; }
    public required string ReleaseCandidate { get; set; }
    public required string RecordSha256 { get; set; }
    public required string CreatedByUserId { get; set; }
    public required string CreatedByDisplayName { get; set; }
    public required string CreatedByRole { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    [JsonIgnore]
    public Tenant Tenant { get; set; } = null!;
    [JsonIgnore]
    public Company Company { get; set; } = null!;
    [JsonIgnore]
    public FilingAuthorityEngagement? SupersedesAuthority { get; set; }
}

/// <summary>
/// Exact immutable manual-handoff artifact. The byte payload and its SHA-256 are the source of
/// truth; scalar columns make tenant scope, chronology and release gates independently queryable.
/// </summary>
public sealed class ExternalFilingHandoffSnapshot
{
    public long Id { get; set; }
    public Guid SnapshotId { get; set; }
    public int TenantId { get; set; }
    public int CompanyId { get; set; }
    public int PeriodId { get; set; }
    public required string Workflow { get; set; }
    public int Version { get; set; }
    public long? SupersedesSnapshotRecordId { get; set; }
    public Guid? SupersedesSnapshotId { get; set; }
    public string? SupersedesArtifactSha256 { get; set; }
    public string? AmendmentReason { get; set; }
    public required string SchemaVersion { get; set; }
    [JsonIgnore]
    public required byte[] ArtifactBytes { get; set; }
    public required string ArtifactSha256 { get; set; }
    public required string SourceFingerprintSha256 { get; set; }
    public long AuthorityId { get; set; }
    public required string AuthorityEvidenceSha256 { get; set; }
    public required string QualifiedReviewManifestSha256 { get; set; }
    public required string ReleaseCandidate { get; set; }
    public bool DirectSubmissionSupported { get; set; }
    public bool IsCompleteExternalReturn { get; set; }
    public bool ReadyForManualHandoff { get; set; }
    public required string PreparedByUserId { get; set; }
    public required string PreparedByDisplayName { get; set; }
    public required string PreparedByRole { get; set; }
    public DateTime PreparedAtUtc { get; set; }

    [JsonIgnore]
    public Tenant Tenant { get; set; } = null!;
    [JsonIgnore]
    public Company Company { get; set; } = null!;
    [JsonIgnore]
    public AccountingPeriod Period { get; set; } = null!;
    [JsonIgnore]
    public FilingAuthorityEngagement Authority { get; set; } = null!;
    [JsonIgnore]
    public ExternalFilingHandoffSnapshot? SupersedesSnapshot { get; set; }
}

/// <summary>
/// Append-only chronology over one exact snapshot. Internal readiness and supersession rows carry
/// no fabricated external reference; genuine external outcomes carry a reference, timestamp and
/// retained evidence hash.
/// </summary>
public sealed class ExternalFilingOutcomeEvent
{
    public long Id { get; set; }
    public int TenantId { get; set; }
    public int CompanyId { get; set; }
    public int PeriodId { get; set; }
    public long SnapshotRecordId { get; set; }
    public Guid SnapshotId { get; set; }
    public required string SnapshotArtifactSha256 { get; set; }
    public int Sequence { get; set; }
    public required string Outcome { get; set; }
    public string? ExternalReference { get; set; }
    public DateTime? ExternalOccurredAtUtc { get; set; }
    public string? Reason { get; set; }
    public DateTime? CorrectionDeadlineUtc { get; set; }
    public string? EvidenceReference { get; set; }
    [JsonIgnore]
    public byte[]? EvidenceArtifact { get; set; }
    public string? EvidenceSha256 { get; set; }
    public long? SupersedingSnapshotRecordId { get; set; }
    public Guid? SupersedingSnapshotId { get; set; }
    public string? SupersedingSnapshotArtifactSha256 { get; set; }
    public required string RecordedByUserId { get; set; }
    public required string RecordedByDisplayName { get; set; }
    public required string RecordedByRole { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public required string EventSha256 { get; set; }

    [JsonIgnore]
    public Tenant Tenant { get; set; } = null!;
    [JsonIgnore]
    public Company Company { get; set; } = null!;
    [JsonIgnore]
    public AccountingPeriod Period { get; set; } = null!;
    [JsonIgnore]
    public ExternalFilingHandoffSnapshot Snapshot { get; set; } = null!;
    [JsonIgnore]
    public ExternalFilingHandoffSnapshot? SupersedingSnapshot { get; set; }
}
