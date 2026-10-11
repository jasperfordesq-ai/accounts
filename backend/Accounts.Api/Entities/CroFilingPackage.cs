using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

public class CroFilingPackage
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public string? PdfPath { get; set; }
    public FilingPackageStatus Status { get; set; } = FilingPackageStatus.Draft;
    public FilingStatus FilingStatus { get; set; } = FilingStatus.NotStarted;
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? SubmittedBy { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public string? CroSubmissionReference { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime? CorrectionDeadline { get; set; } // 14 days from rejection
    public bool AccountsPdfGenerated { get; set; }
    public bool SignaturePageGenerated { get; set; }
    public bool PaymentCompleted { get; set; }

    // P0-REL-001/P0-REL-003: retain the exact clean artifacts server-side. Review downloads are
    // rendered separately with a DRAFT watermark; these bytes are the only bytes that may later be
    // released through a gated final-export endpoint.
    [JsonIgnore]
    public byte[]? AccountsPdfArtifact { get; set; }
    public string? AccountsPdfSha256 { get; set; }
    /// <summary>Set only when the accounts PDF bytes change (GeneratedAt also moves for the signature page).</summary>
    public DateTime? AccountsPdfGeneratedAtUtc { get; set; }
    [JsonIgnore]
    public byte[]? SignaturePageArtifact { get; set; }
    public string? SignaturePageSha256 { get; set; }
    public string? ArtifactReleaseCandidate { get; set; }
    public string? ArtifactSourceFingerprintSha256 { get; set; }
    public string? AttachedAuditorReportSha256 { get; set; }
    public string? ApprovedArtifactManifestSha256 { get; set; }
    public string? ApprovedReleaseCandidate { get; set; }

    // Approval is not satisfied by ApprovedBy alone. These fields retain the exact credential
    // verification evidence that was current when the artifact manifest was approved.
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

    // Optional "reviewed by an accountant" attestation (never a gate). The reviewer's body, membership
    // number, review date and optional letter live in the ApproverProfessionalBody/MembershipNumber/
    // VerifiedAt/VerificationArtifact fields above; this holds the reviewer's name. Cleared when the
    // accounts PDF is regenerated, so it always refers to the accounts actually filed.
    public string? OptionalReviewerName { get; set; }

    // signing-approval-chain: the director/secretary who approved/signed the statements, captured at
    // approval. Submission is blocked until both are present, so there is always a recorded signing
    // authority behind a filing. SignedPdfPath retains the executed/e-signed pack when available.
    public string? SignedByDirector { get; set; }
    public string? SignedBySecretary { get; set; }
    public DateTime? SignedAt { get; set; }
    public string? SignedPdfPath { get; set; }
    [JsonIgnore]
    public byte[]? SignedPdfArtifact { get; set; }
    public string? SignedPdfSha256 { get; set; }

    // Navigation
    [JsonIgnore]
    public AccountingPeriod Period { get; set; } = null!;
}
