using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

/// <summary>
/// Period-specific declaration that bounds the automated corporation-tax support-data scope.
/// Unsupported answers are retained for audit but can never be converted into a plausible final
/// charge by the platform.
/// </summary>
public class CorporationTaxScopeReview
{
    public int Id { get; set; }
    public int PeriodId { get; set; }

    public bool? IsCloseCompany { get; set; }
    public bool? IsServiceCompany { get; set; }
    public bool HasGroupOrConsortiumRelief { get; set; }
    public bool HasChargeableGains { get; set; }
    public bool HasForeignIncomeOrTaxCredits { get; set; }
    public bool HasExceptedTrade { get; set; }
    public bool HasOtherReliefsOrSpecialRegimes { get; set; }
    public bool DeclaredPassiveIncomePresent { get; set; }
    public bool PassiveIncomeClassificationReviewed { get; set; }

    public CorporationTaxLossTreatment LossTreatment { get; set; } = CorporationTaxLossTreatment.Unreviewed;
    public decimal BroughtForwardTradingLoss { get; set; }
    public string? BroughtForwardLossEvidence { get; set; }

    public required string PreparedBy { get; set; }
    public DateTime PreparedAtUtc { get; set; }
    public required string EvidenceNote { get; set; }

    [JsonIgnore]
    public AccountingPeriod Period { get; set; } = null!;
}
