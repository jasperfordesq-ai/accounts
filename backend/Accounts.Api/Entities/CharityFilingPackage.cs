using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

public class CharityFilingPackage
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public FilingPackageStatus Status { get; set; } = FilingPackageStatus.Draft;
    public FilingStatus FilingStatus { get; set; } = FilingStatus.NotStarted;
    public bool SofaGenerated { get; set; }
    public bool TrusteesReportGenerated { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? SubmittedBy { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public string? AcceptedBy { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public string? AnnualReturnReference { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime? CorrectionDeadline { get; set; }

    // Exact clean PDF bytes retained for release. Review copies are rendered separately and are
    // never accepted by FilingReleaseGate as final artifacts.
    [JsonIgnore]
    public byte[]? SofaArtifact { get; set; }
    public string? SofaSha256 { get; set; }
    [JsonIgnore]
    public byte[]? TrusteesReportArtifact { get; set; }
    public string? TrusteesReportSha256 { get; set; }
    public string? ArtifactReleaseCandidate { get; set; }
    public string? ArtifactSourceFingerprintSha256 { get; set; }
    public string? SorpFrameworkCode { get; set; }
    public int? SorpTier { get; set; }
    public string? SofaBasis { get; set; }
    public string? SorpDecisionSha256 { get; set; }
    public string? CharityNumberSnapshot { get; set; }
    public decimal? SofaClosingFunds { get; set; }
    public decimal? BalanceSheetNetAssets { get; set; }
    public decimal? ReconciliationDifference { get; set; }
    public DateTime? ReconciledAtUtc { get; set; }
    public bool TrusteeReviewAccepted { get; set; }
    public string? TrusteeReviewReference { get; set; }
    public string? TrusteeReviewedBy { get; set; }
    public DateTime? TrusteeReviewedAtUtc { get; set; }
    [JsonIgnore]
    public byte[]? TrusteeReviewArtifact { get; set; }
    public string? TrusteeReviewArtifactSha256 { get; set; }
    public string? TrusteePopulationJson { get; set; }
    public string? TrusteePopulationSha256 { get; set; }
    public string? ManualProfessionalHandoffReason { get; set; }
    public string? ApprovedArtifactManifestSha256 { get; set; }
    public string? ApprovedReleaseCandidate { get; set; }
    public string? ApproverProfessionalBody { get; set; }
    public string? ApproverMembershipNumber { get; set; }
    public int? ApproverTenantId { get; set; }
    public string? ApprovalScope { get; set; }
    public string? ApprovalCapacity { get; set; }
    public string? ApprovalDecision { get; set; }
    public string? ApproverVerificationReference { get; set; }
    [JsonIgnore]
    public byte[]? ApproverVerificationArtifact { get; set; }
    public string? ApproverVerificationArtifactSha256 { get; set; }
    public DateTime? ApproverVerifiedAt { get; set; }
    public DateTime? ApproverCredentialValidUntil { get; set; }

    [JsonIgnore]
    public AccountingPeriod Period { get; set; } = null!;
}
