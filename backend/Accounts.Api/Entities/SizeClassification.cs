using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

public class SizeClassification
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public decimal Turnover { get; set; }
    public decimal BalanceSheetTotal { get; set; }
    public int AvgEmployees { get; set; }
    [JsonIgnore]
    public CompanySizeClass? PriorYearClass { get; set; }
    [JsonIgnore]
    public CompanySizeClass RawCurrentClass { get; set; }
    [JsonIgnore]
    public CompanySizeClass? RawPriorClass { get; set; }
    [JsonIgnore]
    public bool RawCurrentMicroQualified { get; set; }
    [JsonIgnore]
    public bool RawCurrentSmallQualified { get; set; }
    [JsonIgnore]
    public bool RawCurrentMediumQualified { get; set; }
    [JsonIgnore]
    public bool? RawPriorMicroQualified { get; set; }
    [JsonIgnore]
    public bool? RawPriorSmallQualified { get; set; }
    [JsonIgnore]
    public bool? RawPriorMediumQualified { get; set; }
    [JsonIgnore]
    public decimal AnnualisedTurnover { get; set; }
    [JsonIgnore]
    public decimal PeriodLengthInYears { get; set; }
    public DateOnly? ThresholdElectionEffectiveFrom { get; set; }
    [JsonIgnore]
    public DateOnly? ThresholdScheduleEffectiveFrom { get; set; }
    [JsonIgnore]
    public string? ThresholdScheduleCode { get; set; }
    public string? DecisionInputFingerprintSha256 { get; set; }
    [JsonIgnore]
    public CompanySizeClass CalculatedClass { get; set; }

    // Saving inputs creates or edits this row without running the engine, which leaves the
    // decision columns at their CLR defaults (0 years, "Micro") or at a superseded decision.
    // Report them only while a current decision exists; otherwise the API says "not classified"
    // explicitly instead of presenting a default as a statutory result.
    [NotMapped, JsonIgnore]
    public bool HasCurrentDecision => !string.IsNullOrWhiteSpace(DecisionInputFingerprintSha256);

    [NotMapped, JsonPropertyName("priorYearClass")]
    public CompanySizeClass? PriorYearClassDecision => HasCurrentDecision ? PriorYearClass : null;
    [NotMapped, JsonPropertyName("rawCurrentClass")]
    public CompanySizeClass? RawCurrentClassDecision => HasCurrentDecision ? RawCurrentClass : null;
    [NotMapped, JsonPropertyName("rawPriorClass")]
    public CompanySizeClass? RawPriorClassDecision => HasCurrentDecision ? RawPriorClass : null;
    [NotMapped, JsonPropertyName("rawCurrentMicroQualified")]
    public bool? RawCurrentMicroQualifiedDecision => HasCurrentDecision ? RawCurrentMicroQualified : null;
    [NotMapped, JsonPropertyName("rawCurrentSmallQualified")]
    public bool? RawCurrentSmallQualifiedDecision => HasCurrentDecision ? RawCurrentSmallQualified : null;
    [NotMapped, JsonPropertyName("rawCurrentMediumQualified")]
    public bool? RawCurrentMediumQualifiedDecision => HasCurrentDecision ? RawCurrentMediumQualified : null;
    [NotMapped, JsonPropertyName("rawPriorMicroQualified")]
    public bool? RawPriorMicroQualifiedDecision => HasCurrentDecision ? RawPriorMicroQualified : null;
    [NotMapped, JsonPropertyName("rawPriorSmallQualified")]
    public bool? RawPriorSmallQualifiedDecision => HasCurrentDecision ? RawPriorSmallQualified : null;
    [NotMapped, JsonPropertyName("rawPriorMediumQualified")]
    public bool? RawPriorMediumQualifiedDecision => HasCurrentDecision ? RawPriorMediumQualified : null;
    [NotMapped, JsonPropertyName("annualisedTurnover")]
    public decimal? AnnualisedTurnoverDecision => HasCurrentDecision ? AnnualisedTurnover : null;
    [NotMapped, JsonPropertyName("periodLengthInYears")]
    public decimal? PeriodLengthInYearsDecision => HasCurrentDecision ? PeriodLengthInYears : null;
    [NotMapped, JsonPropertyName("thresholdScheduleEffectiveFrom")]
    public DateOnly? ThresholdScheduleEffectiveFromDecision => HasCurrentDecision ? ThresholdScheduleEffectiveFrom : null;
    [NotMapped, JsonPropertyName("thresholdScheduleCode")]
    public string? ThresholdScheduleCodeDecision => HasCurrentDecision ? ThresholdScheduleCode : null;
    [NotMapped, JsonPropertyName("calculatedClass")]
    public CompanySizeClass? CalculatedClassDecision => HasCurrentDecision ? CalculatedClass : null;
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

    // Provenance. A HistoricAnchor row's figures were keyed in from accounts filed elsewhere and
    // attested with retained evidence; the system did not compute them. It roots the next period's
    // decision chain and is never itself a classification or filing target.
    public SizeInputBasis InputBasis { get; set; } = SizeInputBasis.Computed;
    public string? HistoricSourceReference { get; set; }
    [JsonIgnore]
    public byte[]? HistoricEvidenceArtifact { get; set; }
    public string? HistoricEvidenceSha256 { get; set; }
    public string? HistoricAttestedBy { get; set; }
    public string? HistoricAttestedRole { get; set; }
    public DateTime? HistoricAttestedAt { get; set; }
    // Null means no effective class is attested: the next year treats this year as not qualified.
    public CompanySizeClass? HistoricAttestedEffectiveClass { get; set; }
    // Retirement of the anchor by an Owner. The evidence above is kept; once set, these stay with the
    // row (also after the period is classified from fresh figures) until an anchor is recorded again.
    public DateTime? HistoricRetiredAt { get; set; }
    public string? HistoricRetiredBy { get; set; }
    public string? HistoricRetiredReason { get; set; }

    // Navigation
    [JsonIgnore]
    public AccountingPeriod Period { get; set; } = null!;
}
