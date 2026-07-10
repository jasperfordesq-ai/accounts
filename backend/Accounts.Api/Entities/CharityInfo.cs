using System.Text.Json.Serialization;
namespace Accounts.Api.Entities;

public class CharityInfo
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string? CharityNumber { get; set; } // Charities Regulator number
    public string? CharityType { get; set; } // CLG, Trust, Unincorporated
    public decimal GrossIncome { get; set; }
    public int SorpTier { get; set; } // 1 (≤500k), 2 (500k-15m), 3 (>15m)
    public string? CharitableObjectives { get; set; }
    public string? PrincipalActivities { get; set; }
    public bool? GovernanceCodeCompliant { get; set; }
    public string? GovernanceCodeNote { get; set; }
    public string? GovernanceEvidenceReference { get; set; }
    public string? GovernanceReviewedBy { get; set; }
    public DateTime? GovernanceReviewedAtUtc { get; set; }
    [JsonIgnore] public byte[]? GovernanceEvidenceArtifact { get; set; }
    public string? GovernanceEvidenceArtifactSha256 { get; set; }
    public bool HasInternationalTransfers { get; set; }
    public string? InternationalTransferDetails { get; set; }
    public bool TrusteeRemunerationPaid { get; set; }
    public decimal TrusteeRemunerationAmount { get; set; }
    public string? TrusteeExpensesDetails { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [JsonIgnore] public Company Company { get; set; } = null!;
}
