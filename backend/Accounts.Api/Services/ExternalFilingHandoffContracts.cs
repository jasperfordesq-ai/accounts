using System.Text.Json.Serialization;

namespace Accounts.Api.Services;

public enum ExternalFilingWorkflow
{
    CroB1,
    RevenueCt1Support
}

public enum ExternalFilingAuthorityKind
{
    CroPresenter,
    CroElectronicFilingAgent,
    RevenueRosAgent
}

public enum ExternalFilingAuthorityStatus
{
    Draft,
    Pending,
    Active,
    Revoked,
    Expired
}

public enum ExternalHandoffFieldStatus
{
    Complete,
    Missing,
    RequiresReview,
    ProtectedManualEntry,
    NotApplicable
}

public enum ExternalFilingOutcomeKind
{
    ReadyForManualHandoff,
    ExternallySubmittedRecorded,
    CorrectionRequired,
    ExternallyRejected,
    ExternallyAcceptedRecorded,
    SupersededByAmendment
}

public sealed record ExternalFilingActor(
    string UserId,
    string DisplayName,
    string Role);

/// <summary>
/// A public-safe projection of retained authority evidence. Protected identifiers and evidence bytes
/// are deliberately absent. AuthorityEvidenceSha256 binds the snapshot to the exact retained private
/// evidence held by the persistence adapter.
/// </summary>
public sealed record ExternalFilingAuthoritySnapshot(
    long AuthorityId,
    int TenantId,
    int CompanyId,
    ExternalFilingWorkflow Workflow,
    ExternalFilingAuthorityKind Kind,
    ExternalFilingAuthorityStatus Status,
    string LegalName,
    string? PracticeName,
    string? MaskedPresenterOrTain,
    string AuthorityScope,
    string EngagementReference,
    string ExternalAuthorityReference,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveUntilUtc,
    DateTime? RevokedAtUtc,
    string AuthorityEvidenceSha256,
    string EvidenceMediaType,
    string EvidenceFileName,
    ExternalFilingActor ReviewedBy,
    DateTime ReviewedAtUtc,
    string ReleaseCandidate);

public sealed record ExternalHandoffField(
    string FieldCode,
    string Section,
    string Label,
    string? Value,
    ExternalHandoffFieldStatus Status,
    string SourceReference,
    string? BlockingReason,
    bool IsProtectedManualEntry = false);

public sealed record ExternalHandoffAddress(
    string? Line1,
    string? Line2,
    string? Line3,
    string? Line4,
    string? Line5,
    string? Line6,
    string? Line7);

/// <summary>
/// No raw PPSN, IPN, RBO number or date of birth is allowed in this public/retained handoff JSON.
/// IdentityEvidenceReference and IdentityEvidenceSha256 bind a protected record; CORE entry remains
/// an explicit protected manual step.
/// </summary>
public sealed record B1OfficerHandoff(
    int OfficerId,
    string FirstName,
    string LastName,
    string Role,
    DateOnly? AppointedDate,
    DateOnly? ResignedDate,
    ExternalHandoffAddress Address,
    string IdentityType,
    string IdentityEvidenceReference,
    string IdentityEvidenceSha256,
    string? PresenterNotificationEmail,
    string OtherDirectorshipsEvidenceReference,
    bool ProtectedIdentifierEntryRequired);

public sealed record B1ShareClassHandoff(
    string ShareClass,
    string Currency,
    decimal NominalValue,
    long NumberIssued,
    decimal TotalNominalValue,
    decimal AmountPaid,
    decimal AmountUnpaid);

public sealed record B1ShareholderHandoff(
    string MemberReference,
    string Name,
    ExternalHandoffAddress Address,
    string ShareClass,
    string Currency,
    decimal OpeningHolding,
    decimal ClosingHolding,
    string HoldingDisplay,
    string EvidenceReference);

public sealed record B1AllotmentHandoff(
    string AllotmentReference,
    DateOnly AllotmentDate,
    string ShareClass,
    string Currency,
    long NumberAllotted,
    decimal NominalValuePerShare,
    decimal Consideration,
    string AllotteeMemberReference,
    string EvidenceReference);

public sealed record B1ManualHandoffFacts(
    string CroNumber,
    string LegalName,
    string CompanyType,
    DateOnly AnnualReturnDate,
    DateOnly MadeUpToDate,
    string AnnualReturnDateElection,
    ExternalHandoffAddress RegisteredOffice,
    DateOnly FinancialYearStart,
    DateOnly FinancialYearEnd,
    bool FinancialStatementsAnnexed,
    bool FirstAnnualReturn,
    bool AuditExemptionClaimed,
    string? AuditorReference,
    string ReportingCurrency,
    bool PoliticalDonationsOverThreshold,
    decimal PoliticalDonationsAmount,
    string PoliticalDonationsEvidenceReference,
    string DirectorSignatory,
    string SecretarySignatory,
    IReadOnlyList<B1OfficerHandoff> Officers,
    IReadOnlyList<B1ShareClassHandoff> ShareClasses,
    IReadOnlyList<B1ShareholderHandoff> Shareholders,
    IReadOnlyList<B1AllotmentHandoff> Allotments,
    string AccountsPdfSha256,
    string SignaturePageSha256,
    string? ShareholdersListPdfSha256);

/// <summary>
/// Bounded corporation-tax support only. IsCompleteCt1Return must remain false and OutputKind must
/// remain the support-only discriminator produced by TaxComputationService.
/// </summary>
public sealed record RevenueCt1SupportHandoffFacts(
    string CompanyName,
    string TaxReference,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string OutputKind,
    bool IsCompleteCt1Return,
    string CalculationSha256,
    string WorksheetArtifactSha256,
    string? IxbrlArtifactSha256,
    string? ExternalValidationEvidenceSha256,
    string? ExternalValidationReference,
    decimal CorporationTaxDue,
    decimal PreliminaryTaxPaid,
    decimal BalanceDue,
    string SupportStatus,
    IReadOnlyList<string> SupportBlockingReasons,
    IReadOnlyList<string> ManualCt1CompletionItems);

public sealed record ExternalFilingAttachment(
    string Code,
    string FileName,
    string MediaType,
    long ByteSize,
    string Sha256,
    string SourceReference);

public sealed record ExternalFilingHandoffBuildRequest(
    Guid SnapshotId,
    int TenantId,
    int CompanyId,
    int PeriodId,
    ExternalFilingWorkflow Workflow,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateTime PreparedAtUtc,
    ExternalFilingActor PreparedBy,
    ExternalFilingAuthoritySnapshot Authority,
    string QualifiedReviewManifestSha256,
    string ReleaseCandidate,
    B1ManualHandoffFacts? CroB1,
    RevenueCt1SupportHandoffFacts? RevenueCt1Support,
    IReadOnlyList<ExternalHandoffField> Fields,
    IReadOnlyList<ExternalFilingAttachment> Attachments);

public sealed record ExternalFilingHandoffDocument(
    string SchemaVersion,
    Guid SnapshotId,
    int Version,
    Guid? SupersedesSnapshotId,
    string? SupersedesArtifactSha256,
    string? AmendmentReason,
    int TenantId,
    int CompanyId,
    int PeriodId,
    ExternalFilingWorkflow Workflow,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateTime PreparedAtUtc,
    ExternalFilingActor PreparedBy,
    ExternalFilingAuthoritySnapshot Authority,
    string QualifiedReviewManifestSha256,
    string ReleaseCandidate,
    bool DirectSubmissionSupported,
    bool IsCompleteExternalReturn,
    bool ReadyForManualHandoff,
    string SourceFingerprintSha256,
    B1ManualHandoffFacts? CroB1,
    RevenueCt1SupportHandoffFacts? RevenueCt1Support,
    IReadOnlyList<ExternalHandoffField> Fields,
    IReadOnlyList<ExternalFilingAttachment> Attachments,
    IReadOnlyList<string> BlockingIssues,
    IReadOnlyList<string> ExternalCompletionWarnings,
    IReadOnlyList<ExternalFilingSourceReference> Sources);

public sealed record ExternalFilingSourceReference(
    string Code,
    string Title,
    string Url,
    string Relevance,
    string EffectiveDate,
    DateTime ReviewedAtUtc);

public sealed record ExternalFilingHandoffBuild(
    ExternalFilingHandoffDocument Document,
    [property: JsonIgnore] byte[] ArtifactBytes,
    string ArtifactSha256);

public sealed record ExternalFilingOutcomeInput(
    ExternalFilingOutcomeKind Outcome,
    Guid SnapshotId,
    string SnapshotArtifactSha256,
    string? ExternalReference,
    DateTime? ExternalOccurredAtUtc,
    string? Reason,
    DateTime? CorrectionDeadlineUtc,
    string? EvidenceReference,
    string? EvidenceSha256,
    Guid? SupersedingSnapshotId = null,
    string? SupersedingSnapshotArtifactSha256 = null);
