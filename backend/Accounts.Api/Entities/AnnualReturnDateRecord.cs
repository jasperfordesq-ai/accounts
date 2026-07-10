using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

public enum AnnualReturnDateSource
{
    CroRecord,
    BroughtForward,
    ExtendedB73,
    CourtOrder,
    ManualOverride
}

/// <summary>
/// Immutable evidence for each exact ARD accepted by the platform. The Company row carries the
/// current operational value; these rows retain who changed it, when it took effect and the source
/// evidence so a later ARD cannot rewrite the basis of an earlier deadline calculation.
/// </summary>
public sealed class AnnualReturnDateRecord
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public DateOnly? PreviousAnnualReturnDate { get; set; }
    public DateOnly AnnualReturnDate { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public AnnualReturnDateSource Source { get; set; }
    public required string EvidenceReference { get; set; }
    public string? EvidenceSha256 { get; set; }
    public string? ChangeReason { get; set; }
    public required string RecordedByUserId { get; set; }
    public required string RecordedByDisplayName { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public required string RecordSha256 { get; set; }

    [JsonIgnore]
    public Company Company { get; set; } = null!;
}
