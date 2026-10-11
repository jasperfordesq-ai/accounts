using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

public enum DirectorsApprovalEventKind
{
    Approved,
    Revoked
}

/// <summary>
/// A generated Companies Act 2014 s.324 approval page. Immutable: correcting a detail generates a
/// new page. The page binds the exact accounts PDF it approves by SHA-256.
/// </summary>
public sealed class DirectorsApprovalPage
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public DateOnly ApprovalDate { get; set; }
    public int FirstDirectorOfficerId { get; set; }
    public int? SecondDirectorOfficerId { get; set; }
    public required string FirstDirectorName { get; set; }
    public string? SecondDirectorName { get; set; }
    public int DirectorsInOfficeCount { get; set; }
    public required string ResolutionReference { get; set; }
    public required string AccountsPdfSha256 { get; set; }
    public required string ArtifactSourceFingerprintSha256 { get; set; }
    [JsonIgnore]
    public byte[] PageArtifact { get; set; } = [];
    public required string PageSha256 { get; set; }
    public required string GeneratedByUserId { get; set; }
    public required string GeneratedByDisplayName { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
    public required string RecordSha256 { get; set; }

    [JsonIgnore]
    public AccountingPeriod Period { get; set; } = null!;
}

/// <summary>
/// Append-only approval / revocation events. Each event chains the previous event's hash for the
/// period so a removed or reordered event is detectable.
/// </summary>
public sealed class DirectorsApprovalEvent
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public DirectorsApprovalEventKind Kind { get; set; }
    public int ApprovalPageId { get; set; }
    public required string ApprovalPageSha256 { get; set; }
    [JsonIgnore]
    public byte[]? SignedPageArtifact { get; set; }
    public string? SignedPageSha256 { get; set; }
    [JsonIgnore]
    public byte[]? ResolutionArtifact { get; set; }
    public string? ResolutionSha256 { get; set; }
    public string? RevocationReason { get; set; }
    public required string RecordedByUserId { get; set; }
    public required string RecordedByDisplayName { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public string? PreviousRecordSha256 { get; set; }
    public required string RecordSha256 { get; set; }

    [JsonIgnore]
    public AccountingPeriod Period { get; set; } = null!;
    [JsonIgnore]
    public DirectorsApprovalPage ApprovalPage { get; set; } = null!;
}
