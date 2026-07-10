using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

public class SizeClassification
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public decimal Turnover { get; set; }
    public decimal BalanceSheetTotal { get; set; }
    public int AvgEmployees { get; set; }
    public CompanySizeClass? PriorYearClass { get; set; }
    public CompanySizeClass RawCurrentClass { get; set; }
    public CompanySizeClass? RawPriorClass { get; set; }
    public bool RawCurrentMicroQualified { get; set; }
    public bool RawCurrentSmallQualified { get; set; }
    public bool RawCurrentMediumQualified { get; set; }
    public bool? RawPriorMicroQualified { get; set; }
    public bool? RawPriorSmallQualified { get; set; }
    public bool? RawPriorMediumQualified { get; set; }
    public decimal AnnualisedTurnover { get; set; }
    public decimal PeriodLengthInYears { get; set; }
    public DateOnly? ThresholdElectionEffectiveFrom { get; set; }
    public DateOnly? ThresholdScheduleEffectiveFrom { get; set; }
    public string? ThresholdScheduleCode { get; set; }
    public string? DecisionInputFingerprintSha256 { get; set; }
    public CompanySizeClass CalculatedClass { get; set; }
    public CompanySizeClass? OverrideClass { get; set; }
    public string? OverrideReason { get; set; }
    public string? OverrideAuthorityRole { get; set; }
    public string? OverrideApprovedBy { get; set; }
    public DateTime? OverrideApprovedAt { get; set; }
    [JsonIgnore]
    public byte[]? OverrideEvidenceArtifact { get; set; }
    public string? OverrideEvidenceSha256 { get; set; }
    public string? OverrideInputFingerprintSha256 { get; set; }
    public bool OverrideRequiresRereview { get; set; }
    public string? ExclusionFlagsJson { get; set; }
    public string? QualificationNotes { get; set; }
    public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [JsonIgnore]
    public AccountingPeriod Period { get; set; } = null!;
}
