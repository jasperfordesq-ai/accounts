using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

public class RevenueFilingPackage
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public string? Ct1DataJson { get; set; }
    public string? IxbrlPath { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public FilingPackageStatus Status { get; set; } = FilingPackageStatus.Draft;
    public FilingStatus FilingStatus { get; set; } = FilingStatus.NotStarted;
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public bool IxbrlGenerated { get; set; }
    public bool IxbrlValidated { get; set; }
    public string? IxbrlValidationErrors { get; set; }
    public string? Ct1Reference { get; set; }

    // Exact retained candidate and approval/validation bindings. The byte payload is deliberately
    // excluded from API JSON responses; final export is available only through FilingReleaseGate.
    [JsonIgnore]
    public byte[]? IxbrlArtifact { get; set; }
    public string? IxbrlSha256 { get; set; }
    public string? ArtifactReleaseCandidate { get; set; }
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
    public string? ExternalValidationArtifactSha256 { get; set; }
    public string? ExternalValidationReference { get; set; }
    public DateTime? ExternalValidatedAt { get; set; }
    public string? ExternalValidatorProvider { get; set; }
    public string? ExternalValidatorVersion { get; set; }
    public string? ExternalTaxonomyPackageSha256 { get; set; }
    public string? ExternalValidationWarningDisposition { get; set; }
    [JsonIgnore]
    public byte[]? ExternalValidationResponseArtifact { get; set; }
    public string? ExternalValidationResponseSha256 { get; set; }

    // Navigation
    [JsonIgnore]
    public AccountingPeriod Period { get; set; } = null!;
}
