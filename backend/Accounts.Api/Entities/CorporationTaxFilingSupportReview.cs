using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

public sealed class CorporationTaxFilingSupportReview
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public DateOnly? PriorPeriodStart { get; set; }
    public DateOnly? PriorPeriodEnd { get; set; }
    public decimal? PriorPeriodCorporationTaxLiabilityExcludingSurchargeAndS239 { get; set; }
    public decimal? PriorPeriodSection239IncomeTax { get; set; }
    public decimal CurrentPeriodSection239IncomeTax { get; set; }
    public string? PriorLiabilityEvidenceReference { get; set; }
    public bool HasInterestLimitationRule { get; set; }
    public bool UsesNotionalGroupPaymentAllocation { get; set; }
    public bool HasDirtOrOtherWithholdingCredits { get; set; }
    public bool HasOtherPreliminaryTaxAdjustments { get; set; }
    public bool HasMandatoryElectronicFilingExemption { get; set; }
    public string PreparedBy { get; set; } = string.Empty;
    public DateTime PreparedAtUtc { get; set; }
    public string EvidenceNote { get; set; } = string.Empty;

    [JsonIgnore]
    public AccountingPeriod Period { get; set; } = null!;
}
