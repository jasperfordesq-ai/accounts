using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;

namespace Accounts.Api.Services;

public enum FilingReleaseWorkflow
{
    Cro,
    Revenue,
    Charity
}

public enum FilingReleaseArtifact
{
    CroAccountsPdf,
    CroSignaturePage,
    RevenueIxbrl,
    CharitySofa,
    CharityTrusteesReport
}

public sealed record ReleasedFilingArtifact(
    byte[] Content,
    string MediaType,
    string FileName,
    string Sha256,
    string ReleaseCandidate);

public sealed record CroSignatureEvidence(
    string DirectorName,
    string SecretaryName,
    DateTime SignedAtUtc,
    byte[] SignedArtifact,
    string SignedArtifactSha256);

public sealed record AuditorReportEvidence(
    string FirmName,
    string SignerName,
    string ProfessionalBody,
    string MembershipNumber,
    string ReportReference,
    DateTime SignedAtUtc,
    string ReviewedByQualifiedAccountant,
    DateTime ReviewedAtUtc,
    string ReviewDecision,
    byte[] ReportArtifact,
    string ReportArtifactSha256);

public sealed record RevenueExternalValidationEvidence(
    string IxbrlArtifactSha256,
    string Provider,
    string ProviderReference,
    string ValidatorVersion,
    string TaxonomyPackageSha256,
    string WarningDisposition,
    byte[] ValidationResponseArtifact,
    string ValidationResponseSha256,
    DateTime ValidatedAtUtc);

/// <summary>
/// One fail-closed boundary for final filing artifacts and final external workflow states. Review
/// rendering happens outside this service; only exact retained clean bytes can pass this gate.
/// </summary>
public sealed class FilingReleaseGate
{
    private readonly AccountsDbContext _db;
    private readonly FinancialStatementsService _statements;
    private readonly FilingReadinessProfileService _readiness;
    private readonly Func<string> _releaseCandidate;
    private readonly AuditService? _audit;
    private readonly DirectorsApprovalService _directorsApproval;

    public FilingReleaseGate(
        AccountsDbContext db,
        FinancialStatementsService statements,
        FilingReadinessProfileService readiness,
        FilingReleaseIdentityProvider identity,
        AuditService audit,
        DirectorsApprovalService directorsApproval)
        : this(db, statements, readiness, identity.GetRequiredCandidate, audit, directorsApproval)
    {
    }

    public FilingReleaseGate(AccountsDbContext db, string releaseCandidate, AuditService? audit = null)
        : this(
            db,
            new FinancialStatementsService(db),
            new FilingReadinessProfileService(db),
            () => NormalizeCandidate(releaseCandidate),
            audit,
            CreateDirectorsApproval(db, audit))
    {
    }

    public FilingReleaseGate(AccountsDbContext db)
        : this(
            db,
            new FinancialStatementsService(db),
            new FilingReadinessProfileService(db),
            () => NormalizeCandidate(
                Environment.GetEnvironmentVariable("ACCOUNTS_RELEASE_CANDIDATE")
                ?? Environment.GetEnvironmentVariable("GITHUB_SHA")
                ?? "local-development"),
            null,
            CreateDirectorsApproval(db, null))
    {
    }

    private FilingReleaseGate(
        AccountsDbContext db,
        FinancialStatementsService statements,
        FilingReadinessProfileService readiness,
        Func<string> releaseCandidate,
        AuditService? audit,
        DirectorsApprovalService directorsApproval)
    {
        _db = db;
        _statements = statements;
        _readiness = readiness;
        _releaseCandidate = releaseCandidate;
        _audit = audit;
        _directorsApproval = directorsApproval;
    }

    /// <summary>
    /// Convenience constructors have no page renderer; revoking an approval never renders a page, so
    /// the revocation path stays fully functional and rendering fails closed if it is ever reached.
    /// </summary>
    private static DirectorsApprovalService CreateDirectorsApproval(AccountsDbContext db, AuditService? audit) =>
        new(db, new RenderingUnavailable(), audit ?? new AuditService(db));

    private sealed class RenderingUnavailable : IDirectorsApprovalPageRenderer
    {
        public Task<byte[]> RenderAsync(int companyId, int periodId, DirectorsApprovalPageContent content) =>
            throw new InvalidOperationException("The filing release gate cannot render a directors' approval page.");
    }

    public const string BoardApprovalCapacity = "board-of-directors-s324";

    public string CurrentReleaseCandidate => _releaseCandidate();

    public static string ComputeSha256(ReadOnlySpan<byte> content) =>
        Convert.ToHexStringLower(SHA256.HashData(content));

    public async Task<CroFilingPackage> RecordCroArtifactAsync(
        int companyId,
        int periodId,
        FilingReleaseArtifact artifactType,
        byte[] content,
        string? auditUserId = null)
    {
        if (artifactType is not FilingReleaseArtifact.CroAccountsPdf and not FilingReleaseArtifact.CroSignaturePage)
            throw new ArgumentOutOfRangeException(nameof(artifactType));
        RequireArtifactBytes(content);

        var period = await LoadPeriodAsync(companyId, periodId);
        var package = period.CroFilingPackage ?? new CroFilingPackage { PeriodId = periodId };
        if (period.CroFilingPackage is null)
            _db.CroFilingPackages.Add(package);

        AssertRegenerationAllowed(package.FilingStatus, "CRO");
        var candidate = CurrentReleaseCandidate;
        var sourceFingerprint = await ComputeCroSourceFingerprintAsync(period);
        var sourceChanged = !string.Equals(
            package.ArtifactSourceFingerprintSha256,
            sourceFingerprint,
            StringComparison.OrdinalIgnoreCase);
        var retainedContent = content.ToArray();
        string? attachedAuditorReportSha256 = null;
        if (artifactType == FilingReleaseArtifact.CroAccountsPdf
            && period.FilingRegime?.AuditExempt == false)
        {
            EnsureAuditorEvidence(period);
            retainedContent = AttachAuditorReport(
                retainedContent,
                period.AuditorsReportArtifact!,
                period.AuditorsReportReference!,
                period.AuditorsReportSignedAt!.Value);
            attachedAuditorReportSha256 = period.AuditorsReportSha256;
        }
        var hash = ComputeSha256(retainedContent);
        var candidateChanged = !string.Equals(package.ArtifactReleaseCandidate, candidate, StringComparison.Ordinal);
        var contentChanged = artifactType == FilingReleaseArtifact.CroAccountsPdf
            ? !string.Equals(package.AccountsPdfSha256, hash, StringComparison.Ordinal)
            : !string.Equals(package.SignaturePageSha256, hash, StringComparison.Ordinal);

        if (candidateChanged || sourceChanged)
        {
            package.AccountsPdfArtifact = null;
            package.AccountsPdfSha256 = null;
            package.SignaturePageArtifact = null;
            package.SignaturePageSha256 = null;
            package.AttachedAuditorReportSha256 = null;
            package.AccountsPdfGenerated = false;
            package.SignaturePageGenerated = false;
        }

        // The directors sign the exact accounts PDF bytes (s.324). A release-candidate-only change
        // that re-records identical bytes against an unchanged source does not change what they
        // approved, so it neither revokes their approval nor refreshes the generation time.
        var isAccountsPdf = artifactType == FilingReleaseArtifact.CroAccountsPdf;
        var approvedBytesRetained = isAccountsPdf
            && !sourceChanged
            && _directorsApproval.IsApprovedAccountsPdf(periodId, hash);
        var directorsApprovalSuperseded = sourceChanged || (isAccountsPdf && !approvedBytesRetained);

        if (isAccountsPdf)
        {
            package.AccountsPdfArtifact = retainedContent;
            package.AccountsPdfSha256 = hash;
            package.AttachedAuditorReportSha256 = attachedAuditorReportSha256;
            package.AccountsPdfGenerated = true;
            if (!approvedBytesRetained && (sourceChanged || contentChanged))
                package.AccountsPdfGeneratedAtUtc = DateTime.UtcNow;
        }
        else
        {
            package.SignaturePageArtifact = retainedContent;
            package.SignaturePageSha256 = hash;
            package.SignaturePageGenerated = true;
        }

        package.ArtifactReleaseCandidate = candidate;
        package.ArtifactSourceFingerprintSha256 = sourceFingerprint;
        package.GeneratedAt = DateTime.UtcNow;
        if (candidateChanged || sourceChanged || contentChanged)
        {
            RevokeCroApproval(package);
            ClearCroSignatureEvidence(package);
        }
        // An accountant's optional review is of specific accounts: regenerated accounts clear it.
        if (sourceChanged || (isAccountsPdf && contentChanged))
            ClearOptionalAccountantReview(package);
        // Same unit of work as the artifact: the directors' approval cannot outlive its accounts.
        var directorsRevocation = directorsApprovalSuperseded
            ? _directorsApproval.AppendRevocationIfCurrent(
                period,
                package,
                sourceChanged ? "accounts-source-changed" : "accounts-regenerated")
            : null;
        package.FilingStatus = package.AccountsPdfGenerated && package.SignaturePageGenerated
            ? FilingStatus.PackageGenerated
            : FilingStatus.InProgress;

        await _db.SaveChangesAsync();
        await LogDirectorsApprovalRevokedAsync(period, directorsRevocation, auditUserId);
        await LogGeneratedAsync(period, artifactType, hash, candidate, auditUserId);
        return package;
    }

    public async Task<RevenueFilingPackage> RecordRevenueIxbrlArtifactAsync(
        int companyId,
        int periodId,
        byte[] content,
        string? auditUserId = null)
    {
        RequireArtifactBytes(content);
        var period = await LoadPeriodAsync(companyId, periodId);
        var package = period.RevenueFilingPackage ?? new RevenueFilingPackage { PeriodId = periodId };
        if (period.RevenueFilingPackage is null)
            _db.RevenueFilingPackages.Add(package);

        AssertRegenerationAllowed(package.FilingStatus, "Revenue");
        var candidate = CurrentReleaseCandidate;
        var hash = ComputeSha256(content);
        var changed = !string.Equals(package.ArtifactReleaseCandidate, candidate, StringComparison.Ordinal)
            || !string.Equals(package.IxbrlSha256, hash, StringComparison.Ordinal);

        package.IxbrlArtifact = content.ToArray();
        package.IxbrlSha256 = hash;
        package.ArtifactReleaseCandidate = candidate;
        package.IxbrlGenerated = true;
        package.GeneratedAt = DateTime.UtcNow;
        if (changed)
        {
            RevokeRevenueApproval(package);
            ClearExternalRevenueValidation(package);
        }
        package.FilingStatus = FilingStatus.InProgress;

        await _db.SaveChangesAsync();
        await LogGeneratedAsync(period, FilingReleaseArtifact.RevenueIxbrl, hash, candidate, auditUserId);
        return package;
    }

    public async Task<CharityFilingPackage> RecordCharityArtifactAsync(
        int companyId,
        int periodId,
        FilingReleaseArtifact artifactType,
        byte[] content,
        string? auditUserId = null,
        CharityArtifactEvidence? evidence = null)
    {
        if (artifactType is not FilingReleaseArtifact.CharitySofa and not FilingReleaseArtifact.CharityTrusteesReport)
            throw new ArgumentOutOfRangeException(nameof(artifactType));
        RequireArtifactBytes(content);

        var period = await LoadPeriodAsync(companyId, periodId);
        var package = period.CharityFilingPackage ?? new CharityFilingPackage { PeriodId = periodId };
        if (period.CharityFilingPackage is null)
            _db.CharityFilingPackages.Add(package);

        AssertRegenerationAllowed(package.FilingStatus, "Charity");
        var candidate = CurrentReleaseCandidate;
        var hash = ComputeSha256(content);
        var candidateChanged = !string.Equals(package.ArtifactReleaseCandidate, candidate, StringComparison.Ordinal);
        var contentChanged = artifactType == FilingReleaseArtifact.CharitySofa
            ? !string.Equals(package.SofaSha256, hash, StringComparison.Ordinal)
            : !string.Equals(package.TrusteesReportSha256, hash, StringComparison.Ordinal);
        var sourceChanged = evidence is not null
            && !string.Equals(
                package.ArtifactSourceFingerprintSha256,
                evidence.SourceFingerprintSha256,
                StringComparison.OrdinalIgnoreCase);

        if (candidateChanged || sourceChanged)
        {
            package.SofaArtifact = null;
            package.SofaSha256 = null;
            package.TrusteesReportArtifact = null;
            package.TrusteesReportSha256 = null;
            package.SofaGenerated = false;
            package.TrusteesReportGenerated = false;
        }

        if (artifactType == FilingReleaseArtifact.CharitySofa)
        {
            package.SofaArtifact = content.ToArray();
            package.SofaSha256 = hash;
            package.SofaGenerated = true;
        }
        else
        {
            package.TrusteesReportArtifact = content.ToArray();
            package.TrusteesReportSha256 = hash;
            package.TrusteesReportGenerated = true;
        }

        package.ArtifactReleaseCandidate = candidate;
        if (evidence is not null)
        {
            package.ArtifactSourceFingerprintSha256 = evidence.SourceFingerprintSha256;
            package.SorpFrameworkCode = evidence.SorpDecision.FrameworkCode;
            package.SorpTier = evidence.SorpDecision.Tier;
            package.SofaBasis = evidence.SorpDecision.SofaBasis;
            package.SorpDecisionSha256 = evidence.SorpDecision.DecisionSha256;
            package.CharityNumberSnapshot = evidence.CharityInfo.CharityNumber;
            package.SofaClosingFunds = evidence.Reconciliation.TotalClosingFunds;
            package.BalanceSheetNetAssets = evidence.Reconciliation.BalanceSheetNetAssets;
            package.ReconciliationDifference = evidence.Reconciliation.Difference;
            package.ReconciledAtUtc = DateTime.UtcNow;
            package.ManualProfessionalHandoffReason = null;
        }
        package.GeneratedAt = DateTime.UtcNow;
        if (candidateChanged || sourceChanged || contentChanged)
            RevokeCharityApproval(package);
        if (package.SofaGenerated && package.TrusteesReportGenerated)
        {
            package.FilingStatus = FilingStatus.PackageGenerated;
            package.Status = FilingPackageStatus.Generated;
        }
        else
        {
            package.FilingStatus = FilingStatus.InProgress;
        }

        await _db.SaveChangesAsync();
        await LogGeneratedAsync(period, artifactType, hash, candidate, auditUserId);
        return package;
    }

    /// <summary>
    /// Retains the executed CRO signing pack. A director/secretary name or file path alone is not
    /// release evidence; the bytes are rehashed at every gated action.
    /// </summary>
    public async Task<CroFilingPackage> RecordVerifiedCroSignatureAsync(
        int companyId,
        int periodId,
        CroSignatureEvidence evidence,
        string? auditUserId = null)
    {
        EnsureCroSignatureEvidence(evidence);
        var period = await LoadPeriodAsync(companyId, periodId);
        var package = period.CroFilingPackage
            ?? throw new FilingReleaseBlockedException("Generate the clean CRO artifacts before retaining the executed signing pack.");
        await EnsureCroArtifactSourceCurrentAsync(period, package);
        EnsureCroArtifacts(period, package, CurrentReleaseCandidate);
        AssertRegenerationAllowed(package.FilingStatus, "CRO signing");

        package.SignedByDirector = evidence.DirectorName.Trim();
        package.SignedBySecretary = evidence.SecretaryName.Trim();
        package.SignedAt = evidence.SignedAtUtc;
        package.SignedPdfArtifact = evidence.SignedArtifact.ToArray();
        package.SignedPdfSha256 = evidence.SignedArtifactSha256.ToLowerInvariant();
        RevokeCroApproval(package);
        await _db.SaveChangesAsync();
        await LogEvidenceAsync(period, "CRO executed signing pack", package.SignedPdfSha256, auditUserId);
        return package;
    }

    /// <summary>
    /// Retains the signed auditor report for an audit-required period. The public year-end flag and
    /// reference remain non-release metadata until this trusted evidence method is used.
    /// </summary>
    public async Task<AccountingPeriod> RecordVerifiedAuditorReportAsync(
        int companyId,
        int periodId,
        AuditorReportEvidence evidence,
        string? auditUserId = null)
    {
        EnsureAuditorReportEvidence(evidence);
        var period = await LoadPeriodAsync(companyId, periodId);
        if (period.FilingRegime?.AuditExempt != false)
            throw new FilingReleaseBlockedException("A retained auditor report is only applicable to an audit-required filing regime.");

        period.AuditorsReportReceived = true;
        period.AuditorsReportReference = evidence.ReportReference.Trim();
        period.AuditorsReportFirmName = evidence.FirmName.Trim();
        period.AuditorsReportSignerName = evidence.SignerName.Trim();
        period.AuditorsReportProfessionalBody = evidence.ProfessionalBody.Trim();
        period.AuditorsReportMembershipNumber = evidence.MembershipNumber.Trim();
        period.AuditorsReportSignedAt = evidence.SignedAtUtc;
        period.AuditorsReportReviewedBy = evidence.ReviewedByQualifiedAccountant.Trim();
        period.AuditorsReportReviewedAt = evidence.ReviewedAtUtc;
        period.AuditorsReportReviewDecision = evidence.ReviewDecision.Trim().ToLowerInvariant();
        period.AuditorsReportArtifact = evidence.ReportArtifact.ToArray();
        period.AuditorsReportSha256 = evidence.ReportArtifactSha256.ToLowerInvariant();
        DirectorsApprovalEvent? directorsRevocation = null;
        if (period.CroFilingPackage is not null)
        {
            RevokeCroApproval(period.CroFilingPackage);
            ClearOptionalAccountantReview(period.CroFilingPackage);
            directorsRevocation = _directorsApproval.AppendRevocationIfCurrent(
                period, period.CroFilingPackage, "accounts-source-changed");
            period.CroFilingPackage.AccountsPdfArtifact = null;
            period.CroFilingPackage.AccountsPdfSha256 = null;
            period.CroFilingPackage.SignaturePageArtifact = null;
            period.CroFilingPackage.SignaturePageSha256 = null;
            period.CroFilingPackage.ArtifactSourceFingerprintSha256 = null;
            period.CroFilingPackage.AttachedAuditorReportSha256 = null;
            period.CroFilingPackage.AccountsPdfGenerated = false;
            period.CroFilingPackage.SignaturePageGenerated = false;
            ClearCroSignatureEvidence(period.CroFilingPackage);
            period.CroFilingPackage.FilingStatus = FilingStatus.InProgress;
        }
        await _db.SaveChangesAsync();
        await LogDirectorsApprovalRevokedAsync(period, directorsRevocation, auditUserId);
        await LogEvidenceAsync(period, "signed auditor report", period.AuditorsReportSha256, auditUserId);
        return period;
    }

    /// <summary>
    /// Binds the filing approval to the board's current Companies Act 2014 s.324 approval of the
    /// exact accounts: the approvers are the directors who signed, never a caller-supplied name.
    /// </summary>
    public Task BindApprovalAsync(
        int companyId,
        int periodId,
        FilingReleaseWorkflow workflow,
        string? auditUserId = null) =>
        GuardAsync(companyId, periodId, workflow, "Approved", auditUserId, async period =>
        {
            if (workflow == FilingReleaseWorkflow.Revenue)
                RevenueIxbrlGenerationPolicy.AssertFilingReadyGenerationEnabled();
            await EnsureBaseReadinessAsync(period);
            var approval = await RequireDirectorsApprovalAsync(period);
            var approvers = approval.Page.SecondDirectorName is null
                ? approval.Page.FirstDirectorName
                : $"{approval.Page.FirstDirectorName} and {approval.Page.SecondDirectorName}";
            var tenantId = RequireTenantId(period.Company);
            var candidate = CurrentReleaseCandidate;
            switch (workflow)
            {
                case FilingReleaseWorkflow.Cro:
                {
                    var package = period.CroFilingPackage ?? Block<CroFilingPackage>("Generate the CRO accounts and signature artifacts before approval.");
                    await EnsureCroArtifactSourceCurrentAsync(period, package);
                    EnsureCroArtifacts(period, package, candidate);
                    EnsureCroSignatures(package);
                    EnsureAuditorEvidence(period);
                    ApplyBoardApproval(package, approvers, tenantId, RequiredApprovalScope(workflow));
                    package.ApprovedArtifactManifestSha256 = CroManifest(period, package, candidate, approval);
                    package.ApprovedReleaseCandidate = candidate;
                    package.ApprovedAt = DateTime.UtcNow;
                    break;
                }
                case FilingReleaseWorkflow.Revenue:
                {
                    var package = period.RevenueFilingPackage ?? Block<RevenueFilingPackage>("Generate the Revenue iXBRL artifact before approval.");
                    EnsureRevenueArtifact(package, candidate);
                    EnsureExternalRevenueValidation(package);
                    ApplyBoardApproval(package, approvers, tenantId, RequiredApprovalScope(workflow));
                    package.ApprovedArtifactManifestSha256 = RevenueManifest(package, candidate, approval);
                    package.ApprovedReleaseCandidate = candidate;
                    package.ApprovedAt = DateTime.UtcNow;
                    break;
                }
                case FilingReleaseWorkflow.Charity:
                {
                    var package = period.CharityFilingPackage ?? Block<CharityFilingPackage>("Generate the charity SoFA and trustees-report artifacts before approval.");
                    await EnsureCharityArtifactSourceCurrentAsync(period, package);
                    EnsureCharityArtifacts(package, candidate);
                    EnsureCharityEvidence(period, package);
                    ApplyBoardApproval(package, approvers, tenantId, RequiredApprovalScope(workflow));
                    package.ApprovedArtifactManifestSha256 = CharityManifest(package, candidate, approval);
                    package.ApprovedReleaseCandidate = candidate;
                    package.ApprovedAt = DateTime.UtcNow;
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(workflow));
            }

            await _db.SaveChangesAsync();
        });

    private async Task<CurrentDirectorsApproval> RequireDirectorsApprovalAsync(AccountingPeriod period) =>
        await _directorsApproval.GetCurrentApprovalAsync(period.Id)
        ?? Block<CurrentDirectorsApproval>("Record the board's directors' approval (s.324), signed by two directors or the sole director, before approving the filing.");

    public Task AssertTransitionAsync(
        int companyId,
        int periodId,
        FilingReleaseWorkflow workflow,
        FilingStatus targetStatus,
        string? externalReference = null,
        string? auditUserId = null)
    {
        if (targetStatus is FilingStatus.Approved)
            throw new ArgumentException("Use BindApprovalAsync for the approval transition.", nameof(targetStatus));
        if (targetStatus is not FilingStatus.Submitted and not FilingStatus.Accepted)
            return Task.CompletedTask;

        return GuardAsync(companyId, periodId, workflow, targetStatus.ToString(), auditUserId, async period =>
        {
            if (workflow == FilingReleaseWorkflow.Revenue)
                RevenueIxbrlGenerationPolicy.AssertFilingReadyGenerationEnabled();
            await EnsureBaseReadinessAsync(period);
            var candidate = CurrentReleaseCandidate;
            switch (workflow)
            {
                case FilingReleaseWorkflow.Cro:
                {
                    var package = period.CroFilingPackage ?? Block<CroFilingPackage>("The CRO filing package has not been generated.");
                    await EnsureCurrentCroApprovalAsync(period, package, candidate);
                    EnsureCroSignatures(package);
                    EnsureAuditorEvidence(period);
                    if (targetStatus == FilingStatus.Submitted)
                    {
                        if (package.FilingStatus != FilingStatus.Approved)
                            Block("Only the currently approved CRO artifact manifest can be recorded as submitted.");
                        RequireReference(externalReference ?? package.CroSubmissionReference, "CORE submission reference");
                    }
                    else
                    {
                        if (package.FilingStatus != FilingStatus.Submitted || !package.PaymentCompleted)
                            Block("CRO acceptance requires the exact submitted manifest and confirmed CORE payment.");
                        RequireReference(package.CroSubmissionReference, "CORE submission reference");
                    }
                    break;
                }
                case FilingReleaseWorkflow.Revenue:
                {
                    var package = period.RevenueFilingPackage ?? Block<RevenueFilingPackage>("The Revenue filing package has not been generated.");
                    await EnsureCurrentRevenueApprovalAsync(period, package, candidate);
                    EnsureExternalRevenueValidation(package);
                    if (targetStatus == FilingStatus.Submitted && package.FilingStatus != FilingStatus.Approved)
                        Block("Only the currently approved Revenue artifact can be recorded as submitted.");
                    if (targetStatus == FilingStatus.Accepted && package.FilingStatus != FilingStatus.Submitted)
                        Block("Only the exact submitted Revenue artifact can be recorded as accepted.");
                    RequireReference(externalReference ?? package.Ct1Reference, "Revenue filing reference");
                    break;
                }
                case FilingReleaseWorkflow.Charity:
                {
                    var package = period.CharityFilingPackage ?? Block<CharityFilingPackage>("The charity filing package has not been generated.");
                    await EnsureCharityArtifactSourceCurrentAsync(period, package);
                    await EnsureCurrentCharityApprovalAsync(period, package, candidate);
                    EnsureCharityEvidence(period, package);
                    if (targetStatus == FilingStatus.Submitted && package.FilingStatus != FilingStatus.Approved)
                        Block("Only the currently approved charity artifact manifest can be recorded as submitted.");
                    if (targetStatus == FilingStatus.Accepted && package.FilingStatus != FilingStatus.Submitted)
                        Block("Only the exact submitted charity artifact manifest can be recorded as accepted.");
                    RequireReference(externalReference ?? package.AnnualReturnReference, "Charity annual return reference");
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(workflow));
            }
        });
    }

    public Task AssertCanRecordFiledAsync(
        int companyId,
        int periodId,
        FilingReleaseWorkflow workflow,
        string? filingReference,
        string? auditUserId = null) =>
        GuardAsync(companyId, periodId, workflow, "Filed", auditUserId, async period =>
        {
            if (workflow == FilingReleaseWorkflow.Revenue)
                RevenueIxbrlGenerationPolicy.AssertFilingReadyGenerationEnabled();
            await EnsureBaseReadinessAsync(period);
            var candidate = CurrentReleaseCandidate;
            switch (workflow)
            {
                case FilingReleaseWorkflow.Cro:
                {
                    var package = period.CroFilingPackage ?? Block<CroFilingPackage>("The CRO filing package is missing.");
                    await EnsureCurrentCroApprovalAsync(period, package, candidate);
                    if (package.FilingStatus != FilingStatus.Accepted)
                        Block("Record CORE acceptance for the exact approved artifact manifest before marking CRO filed.");
                    EnsureMatchingReference(filingReference, package.CroSubmissionReference, "CORE submission reference");
                    break;
                }
                case FilingReleaseWorkflow.Revenue:
                {
                    var package = period.RevenueFilingPackage ?? Block<RevenueFilingPackage>("The Revenue filing package is missing.");
                    await EnsureCurrentRevenueApprovalAsync(period, package, candidate);
                    EnsureExternalRevenueValidation(package);
                    if (package.FilingStatus != FilingStatus.Accepted)
                        Block("Record Revenue acceptance for the exact approved iXBRL artifact before marking Revenue filed.");
                    EnsureMatchingReference(filingReference, package.Ct1Reference, "Revenue filing reference");
                    break;
                }
                case FilingReleaseWorkflow.Charity:
                {
                    var package = period.CharityFilingPackage ?? Block<CharityFilingPackage>("The charity filing package is missing.");
                    await EnsureCharityArtifactSourceCurrentAsync(period, package);
                    await EnsureCurrentCharityApprovalAsync(period, package, candidate);
                    if (package.FilingStatus != FilingStatus.Accepted)
                        Block("Record Charities Regulator acceptance for the exact approved manifest before marking Charity filed.");
                    EnsureMatchingReference(filingReference, package.AnnualReturnReference, "Charity annual return reference");
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(workflow));
            }
        });

    public Task<ReleasedFilingArtifact> GetFinalArtifactAsync(
        int companyId,
        int periodId,
        FilingReleaseArtifact artifactType,
        string? auditUserId = null)
    {
        var workflow = artifactType switch
        {
            FilingReleaseArtifact.CroAccountsPdf or FilingReleaseArtifact.CroSignaturePage => FilingReleaseWorkflow.Cro,
            FilingReleaseArtifact.RevenueIxbrl => FilingReleaseWorkflow.Revenue,
            FilingReleaseArtifact.CharitySofa or FilingReleaseArtifact.CharityTrusteesReport => FilingReleaseWorkflow.Charity,
            _ => throw new ArgumentOutOfRangeException(nameof(artifactType))
        };

        return GuardAsync(companyId, periodId, workflow, $"FinalExport:{artifactType}", auditUserId, async period =>
        {
            if (workflow == FilingReleaseWorkflow.Revenue)
                RevenueIxbrlGenerationPolicy.AssertFilingReadyGenerationEnabled();
            await EnsureBaseReadinessAsync(period);
            var candidate = CurrentReleaseCandidate;
            if (workflow == FilingReleaseWorkflow.Charity)
            {
                var charityPackage = period.CharityFilingPackage
                    ?? Block<CharityFilingPackage>("The charity filing package is missing.");
                await EnsureCharityArtifactSourceCurrentAsync(period, charityPackage);
            }
            return artifactType switch
            {
                FilingReleaseArtifact.CroAccountsPdf => await ReleaseCroArtifactAsync(period, candidate, true),
                FilingReleaseArtifact.CroSignaturePage => await ReleaseCroArtifactAsync(period, candidate, false),
                FilingReleaseArtifact.RevenueIxbrl => await ReleaseRevenueArtifactAsync(period, candidate),
                FilingReleaseArtifact.CharitySofa => await ReleaseCharityArtifactAsync(period, candidate, true),
                FilingReleaseArtifact.CharityTrusteesReport => await ReleaseCharityArtifactAsync(period, candidate, false),
                _ => throw new ArgumentOutOfRangeException(nameof(artifactType))
            };
        });
    }

    public Task<RevenueFilingPackage> RecordExternalRevenueValidationAsync(
        int companyId,
        int periodId,
        string artifactSha256,
        string externalReference,
        string? auditUserId = null) =>
        GuardAsync<RevenueFilingPackage>(companyId, periodId, FilingReleaseWorkflow.Revenue, "ExternalValidation", auditUserId, _ =>
        {
            Block("A free-form external-validation reference cannot release iXBRL. Retained trusted-validator response evidence is required.");
            return Task.FromResult<RevenueFilingPackage>(null!);
        });

    /// <summary>
    /// Trusted validator-ingestion boundary. The response bytes, validator/taxonomy identity and
    /// warning disposition are retained and rechecked against the exact current iXBRL.
    /// </summary>
    public async Task<RevenueFilingPackage> RecordVerifiedExternalRevenueValidationAsync(
        int companyId,
        int periodId,
        RevenueExternalValidationEvidence evidence,
        string? auditUserId = null)
    {
        EnsureExternalValidationEvidence(evidence);
        var period = await LoadPeriodAsync(companyId, periodId);
        var package = period.RevenueFilingPackage
            ?? throw new FilingReleaseBlockedException("Generate and internally validate iXBRL before recording external validation.");
        EnsureRevenueArtifact(package, CurrentReleaseCandidate);
        if (!string.Equals(package.IxbrlSha256, evidence.IxbrlArtifactSha256.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new FilingReleaseBlockedException("External validation must reference the exact current iXBRL SHA-256.");

        package.IxbrlValidated = true;
        package.ExternalValidationArtifactSha256 = package.IxbrlSha256;
        package.ExternalValidationReference = evidence.ProviderReference.Trim();
        package.ExternalValidatedAt = evidence.ValidatedAtUtc;
        package.ExternalValidatorProvider = evidence.Provider.Trim();
        package.ExternalValidatorVersion = evidence.ValidatorVersion.Trim();
        package.ExternalTaxonomyPackageSha256 = evidence.TaxonomyPackageSha256.ToLowerInvariant();
        package.ExternalValidationWarningDisposition = evidence.WarningDisposition.Trim().ToLowerInvariant();
        package.ExternalValidationResponseArtifact = evidence.ValidationResponseArtifact.ToArray();
        package.ExternalValidationResponseSha256 = evidence.ValidationResponseSha256.ToLowerInvariant();
        RevokeRevenueApproval(package);
        await _db.SaveChangesAsync();
        if (_audit is not null)
        {
            await _audit.LogAsync(
                companyId,
                periodId,
                "RevenueFilingPackage",
                package.Id,
                AuditEventCodes.RevenueExternalValidationRecorded,
                null,
                new
                {
                    package.IxbrlSha256,
                    package.ExternalValidationReference,
                    package.ExternalValidatedAt,
                    package.ExternalValidatorProvider,
                    package.ExternalValidatorVersion,
                    package.ExternalTaxonomyPackageSha256,
                    package.ExternalValidationResponseSha256,
                    package.ExternalValidationWarningDisposition
                },
                auditUserId);
        }
        return package;
    }

    private async Task GuardAsync(
        int companyId,
        int periodId,
        FilingReleaseWorkflow workflow,
        string attemptedAction,
        string? auditUserId,
        Func<AccountingPeriod, Task> action)
    {
        await GuardAsync<object?>(companyId, periodId, workflow, attemptedAction, auditUserId, async period =>
        {
            await action(period);
            return null;
        });
    }

    private async Task<T> GuardAsync<T>(
        int companyId,
        int periodId,
        FilingReleaseWorkflow workflow,
        string attemptedAction,
        string? auditUserId,
        Func<AccountingPeriod, Task<T>> action)
    {
        AccountingPeriod? period = null;
        try
        {
            period = await LoadPeriodAsync(companyId, periodId);
            return await action(period);
        }
        catch (FilingReleaseBlockedException ex)
        {
            if (_audit is not null && period is not null)
            {
                await _audit.LogAsync(
                    companyId,
                    periodId,
                    "FilingReleaseGate",
                    periodId,
                    AuditEventCodes.FilingReleaseRejected,
                    null,
                    new { Workflow = workflow.ToString(), AttemptedAction = attemptedAction, Reason = ex.Message },
                    auditUserId,
                    tenantId: period.Company.TenantId,
                    isolatePendingChanges: true);
            }
            throw;
        }
    }

    private async Task<AccountingPeriod> LoadPeriodAsync(int companyId, int periodId) =>
        await _db.AccountingPeriods
            .Include(p => p.Company).ThenInclude(c => c.CharityInfo)
            .Include(p => p.SizeClassification)
            .Include(p => p.FilingRegime)
            .Include(p => p.CroFilingPackage)
            .Include(p => p.RevenueFilingPackage)
            .Include(p => p.CharityFilingPackage)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId)
        ?? throw new ResourceNotFoundException($"Period {periodId} not found");

    private async Task EnsureBaseReadinessAsync(AccountingPeriod period)
    {
        if (period.Status is not PeriodStatus.Finalised and not PeriodStatus.Filed || period.LockedAt is null)
            Block("Final filing release requires a finalised and locked accounting period.");
        if (period.SizeClassification is null)
            Block("Final filing release requires a current size classification.");
        if (period.FilingRegime is null)
            Block("Final filing release requires a determined filing regime.");

        var classification = period.SizeClassification;
        if (classification.ThresholdElectionEffectiveFrom is null
            || string.IsNullOrWhiteSpace(classification.DecisionInputFingerprintSha256))
        {
            Block("Final filing release requires a size decision recalculated against the current period, threshold election and entity flags.");
        }
        try
        {
            await SizeClassificationService.EnsureDecisionChainCurrentAsync(
                _db,
                period,
                classification.ThresholdElectionEffectiveFrom.Value,
                includeTarget: true);
        }
        catch (BusinessRuleException)
        {
            Block("Final filing release requires a size decision chain recalculated against every current/prior input, threshold election and entity flag.");
        }
        if (classification.OverrideClass is not null)
        {
            if (classification.OverrideRequiresRereview
                || classification.OverrideEvidenceArtifact is not { Length: > 0 }
                || !IsSha256(classification.OverrideEvidenceSha256)
                || !string.Equals(
                    ComputeSha256(classification.OverrideEvidenceArtifact),
                    classification.OverrideEvidenceSha256,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    classification.OverrideInputFingerprintSha256,
                    classification.DecisionInputFingerprintSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                Block("Final filing release requires re-review of the retained statutory classification override.");
            }
        }

        var regime = period.FilingRegime;
        var effectiveSizeClass = classification.OverrideClass ?? classification.CalculatedClass;
        if (!FilingRegimeService.IsElectionCompatible(effectiveSizeClass, regime.ElectedRegime))
            Block("The retained filing-regime election is incompatible with the current statutory size decision.");
        if (regime.ElectedRegime == ElectedRegime.Micro && !regime.CanUseMicro)
            Block("The elected Micro regime is not eligible for this period.");
        if (regime.ElectedRegime == ElectedRegime.SmallAbridged && !regime.CanFileAbridged)
            Block("The elected abridged regime is not eligible for this period.");
        var blockers = await _statements.GetFinalOutputReadinessBlockersAsync(period.CompanyId, period.Id);
        if (blockers.Count > 0)
            Block($"Financial readiness is incomplete: {string.Join("; ", blockers)}");

        var profile = await _readiness.GetProfileAsync(period.CompanyId, period.Id);
        if (!profile.SupportedPath || profile.ManualProfessionalReviewRequired)
            Block("This filing path requires a retained manual professional handoff and cannot be released automatically.");
    }

    private static void EnsureCroArtifacts(AccountingPeriod period, CroFilingPackage package, string candidate)
    {
        if (!string.Equals(package.ArtifactReleaseCandidate, candidate, StringComparison.Ordinal))
            Block("CRO artifacts were generated by a different release candidate.");
        if (!IsSha256(package.ArtifactSourceFingerprintSha256))
            Block("CRO artifacts are missing their deterministic legal, tax and accounting source fingerprint.");
        EnsureRetainedArtifact(package.AccountsPdfArtifact, package.AccountsPdfSha256, "CRO accounts PDF");
        EnsureRetainedArtifact(package.SignaturePageArtifact, package.SignaturePageSha256, "CRO signature page");
        if (period.FilingRegime?.AuditExempt == false
            && !string.Equals(
                package.AttachedAuditorReportSha256,
                period.AuditorsReportSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            Block("The exact current signed auditor report is not attached to the retained final accounts PDF.");
        }
    }

    private static void EnsureRevenueArtifact(RevenueFilingPackage package, string candidate)
    {
        if (!string.Equals(package.ArtifactReleaseCandidate, candidate, StringComparison.Ordinal))
            Block("Revenue iXBRL was generated by a different release candidate.");
        EnsureRetainedArtifact(package.IxbrlArtifact, package.IxbrlSha256, "Revenue iXBRL");
        if (!package.IxbrlGenerated || package.IxbrlValidationErrors?.StartsWith("Internal checks passed.", StringComparison.Ordinal) != true)
            Block("Revenue internal iXBRL checks have not passed for the retained artifact.");
    }

    private static void EnsureCharityArtifacts(CharityFilingPackage package, string candidate)
    {
        if (!string.Equals(package.ArtifactReleaseCandidate, candidate, StringComparison.Ordinal))
            Block("Charity artifacts were generated by a different release candidate.");
        EnsureRetainedArtifact(package.SofaArtifact, package.SofaSha256, "Charity SoFA");
        EnsureRetainedArtifact(package.TrusteesReportArtifact, package.TrusteesReportSha256, "Charity trustees report");
        EnsurePdf(package.SofaArtifact!, "Charity SoFA");
        EnsurePdf(package.TrusteesReportArtifact!, "Charity trustees report");
    }

    private static void EnsureRetainedArtifact(byte[]? content, string? expectedHash, string label)
    {
        if (content is null || content.Length == 0 || !IsSha256(expectedHash))
            Block($"The exact retained {label} artifact is missing.");
        if (!string.Equals(ComputeSha256(content), expectedHash, StringComparison.OrdinalIgnoreCase))
            Block($"The retained {label} bytes do not match their SHA-256 evidence.");
    }

    private static void EnsurePdf(byte[] content, string label)
    {
        if (content.Length < 5
            || content[0] != (byte)'%'
            || content[1] != (byte)'P'
            || content[2] != (byte)'D'
            || content[3] != (byte)'F'
            || content[4] != (byte)'-')
            Block($"The retained {label} artifact is not a PDF.");
    }

    private static void EnsureCroSignatures(CroFilingPackage package)
    {
        if (string.IsNullOrWhiteSpace(package.SignedByDirector)
            || string.IsNullOrWhiteSpace(package.SignedBySecretary)
            || package.SignedAt is null
            || package.SignedAt.Value.Kind != DateTimeKind.Utc
            || package.SignedAt > DateTime.UtcNow.AddMinutes(5))
        {
            Block("CRO release requires current director and company-secretary signing authority with a UTC execution time.");
        }
        EnsureRetainedArtifact(package.SignedPdfArtifact, package.SignedPdfSha256, "executed CRO signing pack");
    }

    private static void EnsureAuditorEvidence(AccountingPeriod period)
    {
        if (period.FilingRegime?.AuditExempt != false)
            return;
        if (!HasCompleteAuditorReportEvidence(period))
            Block("Audit-required release needs retained signed-opinion PDF bytes, auditor firm/signer identity and the report accepted by the directors.");
    }

    public static bool HasCompleteAuditorReportEvidence(AccountingPeriod period)
    {
        if (!period.AuditorsReportReceived
            || string.IsNullOrWhiteSpace(period.AuditorsReportReference)
            || string.IsNullOrWhiteSpace(period.AuditorsReportFirmName)
            || string.IsNullOrWhiteSpace(period.AuditorsReportSignerName)
            || string.IsNullOrWhiteSpace(period.AuditorsReportProfessionalBody)
            || string.IsNullOrWhiteSpace(period.AuditorsReportMembershipNumber)
            || period.AuditorsReportSignedAt is null
            || period.AuditorsReportSignedAt.Value.Kind != DateTimeKind.Utc
            || period.AuditorsReportSignedAt > DateTime.UtcNow.AddMinutes(5)
            || string.IsNullOrWhiteSpace(period.AuditorsReportReviewedBy)
            || period.AuditorsReportReviewedAt is null
            || period.AuditorsReportReviewedAt.Value.Kind != DateTimeKind.Utc
            || period.AuditorsReportReviewedAt < period.AuditorsReportSignedAt
            || !string.Equals(period.AuditorsReportReviewDecision, "accepted", StringComparison.OrdinalIgnoreCase)
            || period.AuditorsReportArtifact is not { Length: > 5 }
            || !IsPdfArtifact(period.AuditorsReportArtifact)
            || !IsSha256(period.AuditorsReportSha256)
            || !string.Equals(
                ComputeSha256(period.AuditorsReportArtifact),
                period.AuditorsReportSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static void EnsureExternalRevenueValidation(RevenueFilingPackage package)
    {
        if (!package.IxbrlValidated
            || package.ExternalValidatedAt is null
            || package.ExternalValidatedAt.Value.Kind != DateTimeKind.Utc
            || package.ExternalValidatedAt > DateTime.UtcNow.AddMinutes(5)
            || string.IsNullOrWhiteSpace(package.ExternalValidationReference)
            || string.IsNullOrWhiteSpace(package.ExternalValidatorProvider)
            || string.IsNullOrWhiteSpace(package.ExternalValidatorVersion)
            || !IsAcceptedDisposition(package.ExternalValidationWarningDisposition)
            || !IsSha256(package.ExternalValidationArtifactSha256)
            || !IsSha256(package.ExternalTaxonomyPackageSha256)
            || !string.Equals(package.ExternalValidationArtifactSha256, package.IxbrlSha256, StringComparison.OrdinalIgnoreCase))
        {
            Block("Structured external ROS/iXBRL validation for the exact current artifact, validator and taxonomy is required.");
        }
        EnsureRetainedArtifact(
            package.ExternalValidationResponseArtifact,
            package.ExternalValidationResponseSha256,
            "external ROS/iXBRL validator response");
    }

    private static void EnsureCharityEvidence(AccountingPeriod period, CharityFilingPackage package)
    {
        if (!period.Company.IsCharitableOrganisation || string.IsNullOrWhiteSpace(period.Company.CharityInfo?.CharityNumber))
            Block("Charity filing release requires a configured charity number.");
        if (!package.SofaGenerated || !package.TrusteesReportGenerated)
            Block("Charity filing release requires retained SoFA and trustees-report artifacts.");
        if (!IsSha256(package.ArtifactSourceFingerprintSha256)
            || !IsSha256(package.SorpDecisionSha256)
            || !IsSha256(package.TrusteeReviewArtifactSha256)
            || !IsSha256(package.TrusteePopulationSha256))
            Block("Charity filing release requires source, SORP decision and trustee-review hash evidence.");
        if (string.IsNullOrWhiteSpace(package.SorpFrameworkCode)
            || package.SorpTier is null
            || string.IsNullOrWhiteSpace(package.SofaBasis)
            || string.IsNullOrWhiteSpace(package.CharityNumberSnapshot))
            Block("Charity filing release requires an explicit SORP decision and retained charity-number snapshot.");
        if (!package.TrusteeReviewAccepted
            || string.IsNullOrWhiteSpace(package.TrusteeReviewReference)
            || string.IsNullOrWhiteSpace(package.TrusteeReviewedBy)
            || package.TrusteeReviewedAtUtc is null)
            Block("Charity filing release requires an accepted named trustee-population review.");
        EnsureRetainedArtifact(
            package.TrusteeReviewArtifact,
            package.TrusteeReviewArtifactSha256,
            "trustee-review evidence");
        if (package.SofaClosingFunds is null
            || package.BalanceSheetNetAssets is null
            || package.ReconciliationDifference is null
            || package.ReconciledAtUtc is null
            || Math.Abs(package.ReconciliationDifference.Value) > 0.01m
            || decimal.Round(package.SofaClosingFunds.Value - package.BalanceSheetNetAssets.Value, 2) != package.ReconciliationDifference.Value)
            Block("Charity filing release requires retained proof that SoFA closing funds equal balance-sheet net assets.");
    }

    private async Task EnsureCurrentCroApprovalAsync(AccountingPeriod period, CroFilingPackage package, string candidate)
    {
        await EnsureCroArtifactSourceCurrentAsync(period, package);
        EnsureCroArtifacts(period, package, candidate);
        var approval = await RequireCurrentBoardApprovalAsync(period, package.ApprovalCapacity);
        EnsureApproval(
            package.ApprovedBy,
            package.ApprovedAt,
            package.ApprovedReleaseCandidate,
            package.ApprovedArtifactManifestSha256,
            candidate,
            CroManifest(period, package, candidate, approval),
            "CRO");
    }

    private async Task EnsureCurrentRevenueApprovalAsync(AccountingPeriod period, RevenueFilingPackage package, string candidate)
    {
        EnsureRevenueArtifact(package, candidate);
        var approval = await RequireCurrentBoardApprovalAsync(period, package.ApprovalCapacity);
        EnsureApproval(
            package.ApprovedBy,
            package.ApprovedAt,
            package.ApprovedReleaseCandidate,
            package.ApprovedArtifactManifestSha256,
            candidate,
            RevenueManifest(package, candidate, approval),
            "Revenue");
    }

    private async Task EnsureCurrentCharityApprovalAsync(AccountingPeriod period, CharityFilingPackage package, string candidate)
    {
        EnsureCharityArtifacts(package, candidate);
        var approval = await RequireCurrentBoardApprovalAsync(period, package.ApprovalCapacity);
        EnsureApproval(
            package.ApprovedBy,
            package.ApprovedAt,
            package.ApprovedReleaseCandidate,
            package.ApprovedArtifactManifestSha256,
            candidate,
            CharityManifest(package, candidate, approval),
            "Charity");
    }

    private async Task<CurrentDirectorsApproval> RequireCurrentBoardApprovalAsync(AccountingPeriod period, string? approvalCapacity)
    {
        var approval = await RequireDirectorsApprovalAsync(period);
        if (!string.Equals(approvalCapacity, BoardApprovalCapacity, StringComparison.Ordinal))
            Block("The filing approval was not bound to the directors' approval (s.324); approve it again.");
        return approval;
    }

    private static string RequiredApprovalScope(FilingReleaseWorkflow workflow) => workflow switch
    {
        FilingReleaseWorkflow.Cro => "cro-final-filing",
        FilingReleaseWorkflow.Revenue => "revenue-final-filing",
        FilingReleaseWorkflow.Charity => "charity-final-filing",
        _ => throw new ArgumentOutOfRangeException(nameof(workflow))
    };

    private static int RequireTenantId(Company company) =>
        company.TenantId ?? Block<int>("Final filing release requires an owning tenant.");

    private static void EnsureCroSignatureEvidence(CroSignatureEvidence evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence.DirectorName)
            || string.IsNullOrWhiteSpace(evidence.SecretaryName)
            || evidence.SignedAtUtc.Kind != DateTimeKind.Utc
            || evidence.SignedAtUtc > DateTime.UtcNow.AddMinutes(5))
        {
            Block("The executed CRO signing pack requires director/secretary identities and a valid UTC signing time.");
        }
        EnsureRetainedArtifact(evidence.SignedArtifact, evidence.SignedArtifactSha256, "executed CRO signing pack");
    }

    private static void EnsureAuditorReportEvidence(AuditorReportEvidence evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence.FirmName)
            || string.IsNullOrWhiteSpace(evidence.SignerName)
            || string.IsNullOrWhiteSpace(evidence.ProfessionalBody)
            || string.IsNullOrWhiteSpace(evidence.MembershipNumber)
            || string.IsNullOrWhiteSpace(evidence.ReportReference)
            || evidence.SignedAtUtc.Kind != DateTimeKind.Utc
            || evidence.SignedAtUtc > DateTime.UtcNow.AddMinutes(5)
            || string.IsNullOrWhiteSpace(evidence.ReviewedByQualifiedAccountant)
            || evidence.ReviewedAtUtc.Kind != DateTimeKind.Utc
            || evidence.ReviewedAtUtc < evidence.SignedAtUtc
            || evidence.ReviewedAtUtc > DateTime.UtcNow.AddMinutes(5)
            || !string.Equals(evidence.ReviewDecision?.Trim(), "accepted", StringComparison.OrdinalIgnoreCase))
        {
            Block("The signed auditor report requires firm/signer professional identity, UTC signing evidence and the report accepted by the directors.");
        }
        EnsureRetainedArtifact(evidence.ReportArtifact, evidence.ReportArtifactSha256, "signed auditor report");
        if (!IsPdfArtifact(evidence.ReportArtifact))
            Block("The retained signed auditor report must be a PDF artifact.");
    }

    private static void EnsureExternalValidationEvidence(RevenueExternalValidationEvidence evidence)
    {
        if (!IsSha256(evidence.IxbrlArtifactSha256)
            || string.IsNullOrWhiteSpace(evidence.Provider)
            || string.IsNullOrWhiteSpace(evidence.ProviderReference)
            || string.IsNullOrWhiteSpace(evidence.ValidatorVersion)
            || !IsSha256(evidence.TaxonomyPackageSha256)
            || !IsAcceptedDisposition(evidence.WarningDisposition)
            || evidence.ValidatedAtUtc.Kind != DateTimeKind.Utc
            || evidence.ValidatedAtUtc > DateTime.UtcNow.AddMinutes(5))
        {
            Block("Trusted external validation requires exact artifact/taxonomy hashes, validator identity, warning disposition and a valid UTC timestamp.");
        }
        EnsureRetainedArtifact(
            evidence.ValidationResponseArtifact,
            evidence.ValidationResponseSha256,
            "external ROS/iXBRL validator response");
    }

    private static bool IsAcceptedDisposition(string? value) =>
        string.Equals(value?.Trim(), "accepted", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value?.Trim(), "remediated", StringComparison.OrdinalIgnoreCase);

    private static void EnsureApproval(
        string? approvedBy,
        DateTime? approvedAt,
        string? approvedCandidate,
        string? approvedManifest,
        string candidate,
        string currentManifest,
        string label)
    {
        if (string.IsNullOrWhiteSpace(approvedBy) || approvedAt is null)
            Block($"A current named approval is required for {label} release.");
        if (!string.Equals(approvedCandidate, candidate, StringComparison.Ordinal)
            || !string.Equals(approvedManifest, currentManifest, StringComparison.Ordinal))
        {
            Block($"The {label} approval does not match the current release candidate and exact artifact hashes.");
        }
    }

    private async Task<ReleasedFilingArtifact> ReleaseCroArtifactAsync(AccountingPeriod period, string candidate, bool accounts)
    {
        var package = period.CroFilingPackage ?? Block<CroFilingPackage>("The CRO filing package is missing.");
        await EnsureCurrentCroApprovalAsync(period, package, candidate);
        EnsureCroSignatures(package);
        EnsureAuditorEvidence(period);
        var content = accounts ? package.AccountsPdfArtifact! : package.SignedPdfArtifact!;
        var hash = accounts ? package.AccountsPdfSha256! : package.SignedPdfSha256!;
        return new ReleasedFilingArtifact(
            content.ToArray(),
            "application/pdf",
            accounts ? $"cro_filing_{period.Id}.pdf" : $"executed_signature_pack_{period.Id}.pdf",
            hash,
            candidate);
    }

    private async Task<ReleasedFilingArtifact> ReleaseRevenueArtifactAsync(AccountingPeriod period, string candidate)
    {
        RevenueIxbrlGenerationPolicy.AssertFilingReadyGenerationEnabled();
        var package = period.RevenueFilingPackage ?? Block<RevenueFilingPackage>("The Revenue filing package is missing.");
        await EnsureCurrentRevenueApprovalAsync(period, package, candidate);
        EnsureExternalRevenueValidation(package);
        return new ReleasedFilingArtifact(
            package.IxbrlArtifact!.ToArray(),
            "application/xhtml+xml",
            $"financial_statements_{period.Id}.xhtml",
            package.IxbrlSha256!,
            candidate);
    }

    private async Task<ReleasedFilingArtifact> ReleaseCharityArtifactAsync(AccountingPeriod period, string candidate, bool sofa)
    {
        var package = period.CharityFilingPackage ?? Block<CharityFilingPackage>("The charity filing package is missing.");
        await EnsureCurrentCharityApprovalAsync(period, package, candidate);
        EnsureCharityEvidence(period, package);
        var content = sofa ? package.SofaArtifact! : package.TrusteesReportArtifact!;
        var hash = sofa ? package.SofaSha256! : package.TrusteesReportSha256!;
        return new ReleasedFilingArtifact(
            content.ToArray(),
            "application/pdf",
            sofa ? $"charity_sofa_{period.Id}.pdf" : $"charity_trustees_annual_report_{period.Id}.pdf",
            hash,
            candidate);
    }

    private static void ApplyBoardApproval(CroFilingPackage package, string approvers, int tenantId, string scope)
    {
        package.ApprovedBy = approvers;
        package.ApproverTenantId = tenantId;
        package.ApprovalScope = scope;
        package.ApprovalCapacity = BoardApprovalCapacity;
        package.ApprovalDecision = "approved";
        // The professional-body, membership, review-date and review-letter fields hold the optional
        // accountant review attestation (never a gate); binding the board approval must not erase it.
        package.ApproverVerificationReference = null;
        package.ApproverCredentialValidUntil = null;
    }

    private static void ApplyBoardApproval(RevenueFilingPackage package, string approvers, int tenantId, string scope)
    {
        package.ApprovedBy = approvers;
        package.ApproverTenantId = tenantId;
        package.ApprovalScope = scope;
        package.ApprovalCapacity = BoardApprovalCapacity;
        package.ApprovalDecision = "approved";
        package.ApproverProfessionalBody = null;
        package.ApproverMembershipNumber = null;
        package.ApproverVerificationReference = null;
        package.ApproverVerificationArtifact = null;
        package.ApproverVerificationArtifactSha256 = null;
        package.ApproverVerifiedAt = null;
        package.ApproverCredentialValidUntil = null;
    }

    private static void ApplyBoardApproval(CharityFilingPackage package, string approvers, int tenantId, string scope)
    {
        package.ApprovedBy = approvers;
        package.ApproverTenantId = tenantId;
        package.ApprovalScope = scope;
        package.ApprovalCapacity = BoardApprovalCapacity;
        package.ApprovalDecision = "approved";
        package.ApproverProfessionalBody = null;
        package.ApproverMembershipNumber = null;
        package.ApproverVerificationReference = null;
        package.ApproverVerificationArtifact = null;
        package.ApproverVerificationArtifactSha256 = null;
        package.ApproverVerifiedAt = null;
        package.ApproverCredentialValidUntil = null;
    }

    private static (string Name, string Hash)[] DirectorsApprovalEntries(CurrentDirectorsApproval approval) =>
    [
        ("directors-approval-page", approval.Page.PageSha256),
        ("directors-signed-page", approval.Approval.SignedPageSha256!),
        ("board-resolution", approval.Approval.ResolutionSha256!),
        ("directors-approval-record", approval.Approval.RecordSha256),
    ];

    private static string CroManifest(AccountingPeriod period, CroFilingPackage package, string candidate, CurrentDirectorsApproval approval)
    {
        var artifacts = new List<(string Name, string Hash)>
        {
            ("accounts-pdf", package.AccountsPdfSha256!),
            ("signature-page-template", package.SignaturePageSha256!),
            ("executed-signing-pack", package.SignedPdfSha256!),
            ("legal-tax-accounting-source-fingerprint", package.ArtifactSourceFingerprintSha256!)
        };
        artifacts.AddRange(DirectorsApprovalEntries(approval));
        if (period.FilingRegime?.AuditExempt == false)
        {
            artifacts.Add(("signed-auditor-report", period.AuditorsReportSha256!));
            artifacts.Add(("attached-auditor-report", package.AttachedAuditorReportSha256!));
        }
        return Manifest("CRO", candidate, [.. artifacts]);
    }

    private static string RevenueManifest(RevenueFilingPackage package, string candidate, CurrentDirectorsApproval approval) =>
        Manifest("Revenue", candidate,
        [
            ("ixbrl", package.IxbrlSha256!),
            ("external-validator-response", package.ExternalValidationResponseSha256!),
            ("external-taxonomy-package", package.ExternalTaxonomyPackageSha256!),
            .. DirectorsApprovalEntries(approval)
        ]);

    private static string CharityManifest(CharityFilingPackage package, string candidate, CurrentDirectorsApproval approval) =>
        Manifest("Charity", candidate,
        [
            ("sofa", package.SofaSha256!),
            ("trustees-report", package.TrusteesReportSha256!),
            ("source-fingerprint", package.ArtifactSourceFingerprintSha256!),
            ("sorp-decision", package.SorpDecisionSha256!),
            ("trustee-review", package.TrusteeReviewArtifactSha256!),
            ("trustee-population", package.TrusteePopulationSha256!),
            ("charity-number", HashBinding(package.CharityNumberSnapshot)),
            ("reconciliation", HashBinding(
                package.SofaClosingFunds?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                package.BalanceSheetNetAssets?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                package.ReconciliationDifference?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                package.ReconciledAtUtc?.ToUniversalTime().ToString("O"))),
            .. DirectorsApprovalEntries(approval)
        ]);

    private static string HashBinding(params string?[] values) =>
        ComputeSha256(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(values)));

    private static string Manifest(string workflow, string candidate, params (string Name, string Hash)[] artifacts)
    {
        var canonical = string.Join(
            "\n",
            new[] { $"workflow={workflow}", $"releaseCandidate={candidate}" }
                .Concat(artifacts.OrderBy(a => a.Name, StringComparer.Ordinal).Select(a => $"{a.Name}={a.Hash.ToLowerInvariant()}")));
        return ComputeSha256(Encoding.UTF8.GetBytes(canonical));
    }

    /// <summary>
    /// A members' audit notice (s.334) removed the audit exemption a CRO approval relied on: revoke
    /// that approval in the caller's unit of work (the caller saves). A package already recorded as
    /// externally submitted or accepted is left to its correction workflow. True when revoked.
    /// </summary>
    public static bool RevokeCroApprovalForLostAuditExemption(CroFilingPackage? package)
    {
        if (package?.ApprovedBy is null || package.FilingStatus is FilingStatus.Submitted or FilingStatus.Accepted)
            return false;
        RevokeCroApproval(package);
        // Back to the pre-approval state RecordCroArtifactAsync derives, so the pack no longer reads as Approved.
        package.FilingStatus = package.AccountsPdfGenerated && package.SignaturePageGenerated
            ? FilingStatus.PackageGenerated
            : FilingStatus.InProgress;
        return true;
    }

    private static void RevokeCroApproval(CroFilingPackage package)
    {
        package.ApprovedBy = null;
        package.ApprovedAt = null;
        package.ApprovedArtifactManifestSha256 = null;
        package.ApprovedReleaseCandidate = null;
        package.ApproverTenantId = null;
        package.ApprovalScope = null;
        package.ApprovalCapacity = null;
        package.ApprovalDecision = null;
        package.ApproverVerificationReference = null;
        package.ApproverCredentialValidUntil = null;
        package.SubmittedBy = null;
        package.SubmittedAt = null;
        package.CroSubmissionReference = null;
        package.PaymentCompleted = false;
    }

    /// <summary>
    /// The optional accountant review refers to the accounts actually filed, so any change to the
    /// accounts clears it. It is stored on the package's professional-body verification fields.
    /// </summary>
    private static void ClearOptionalAccountantReview(CroFilingPackage package)
    {
        package.OptionalReviewerName = null;
        package.ApproverProfessionalBody = null;
        package.ApproverMembershipNumber = null;
        package.ApproverVerificationArtifact = null;
        package.ApproverVerificationArtifactSha256 = null;
        package.ApproverVerifiedAt = null;
    }

    private static void RevokeRevenueApproval(RevenueFilingPackage package)
    {
        package.ApprovedBy = null;
        package.ApprovedAt = null;
        package.ApprovedArtifactManifestSha256 = null;
        package.ApprovedReleaseCandidate = null;
        package.ApproverProfessionalBody = null;
        package.ApproverMembershipNumber = null;
        package.ApproverTenantId = null;
        package.ApprovalScope = null;
        package.ApprovalCapacity = null;
        package.ApprovalDecision = null;
        package.ApproverVerificationReference = null;
        package.ApproverVerificationArtifact = null;
        package.ApproverVerificationArtifactSha256 = null;
        package.ApproverVerifiedAt = null;
        package.ApproverCredentialValidUntil = null;
        package.Ct1Reference = null;
    }

    private static void RevokeCharityApproval(CharityFilingPackage package)
    {
        package.ApprovedBy = null;
        package.ApprovedAt = null;
        package.ApprovedArtifactManifestSha256 = null;
        package.ApprovedReleaseCandidate = null;
        package.ApproverProfessionalBody = null;
        package.ApproverMembershipNumber = null;
        package.ApproverTenantId = null;
        package.ApprovalScope = null;
        package.ApprovalCapacity = null;
        package.ApprovalDecision = null;
        package.ApproverVerificationReference = null;
        package.ApproverVerificationArtifact = null;
        package.ApproverVerificationArtifactSha256 = null;
        package.ApproverVerifiedAt = null;
        package.ApproverCredentialValidUntil = null;
        package.SubmittedBy = null;
        package.SubmittedAt = null;
        package.AcceptedBy = null;
        package.AcceptedAt = null;
        package.AnnualReturnReference = null;
    }

    private static void ClearCroSignatureEvidence(CroFilingPackage package)
    {
        package.SignedByDirector = null;
        package.SignedBySecretary = null;
        package.SignedAt = null;
        package.SignedPdfPath = null;
        package.SignedPdfArtifact = null;
        package.SignedPdfSha256 = null;
    }

    private static void ClearExternalRevenueValidation(RevenueFilingPackage package)
    {
        package.IxbrlValidated = false;
        package.ExternalValidationArtifactSha256 = null;
        package.ExternalValidationReference = null;
        package.ExternalValidatedAt = null;
        package.ExternalValidatorProvider = null;
        package.ExternalValidatorVersion = null;
        package.ExternalTaxonomyPackageSha256 = null;
        package.ExternalValidationWarningDisposition = null;
        package.ExternalValidationResponseArtifact = null;
        package.ExternalValidationResponseSha256 = null;
    }

    private async Task EnsureCharityArtifactSourceCurrentAsync(
        AccountingPeriod period,
        CharityFilingPackage package)
    {
        if (!IsSha256(package.ArtifactSourceFingerprintSha256))
            Block("Charity artifacts are missing their deterministic SORP, governance, trustee and reconciled-accounting source fingerprint.");

        CharityArtifactEvidence current;
        try
        {
            var balanceSheet = await _statements.GetBalanceSheetAsync(period.CompanyId, period.Id);
            current = await new CharityReportingService(_db).BuildArtifactEvidenceAsync(
                period.CompanyId,
                period.Id,
                balanceSheet.NetAssets,
                package);
        }
        catch (BusinessRuleException ex)
        {
            CharityReportingService.InvalidateArtifacts(package);
            await _db.SaveChangesAsync();
            Block($"Charity artifacts and their filing approval were revoked because current source evidence is incomplete or changed: {ex.Message}");
            return;
        }

        if (!string.Equals(
                current.SourceFingerprintSha256,
                package.ArtifactSourceFingerprintSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            CharityReportingService.InvalidateArtifacts(package);
            await _db.SaveChangesAsync();
            Block("Charity artifacts and their filing approval were revoked because the charity number, governance evidence, trustee population, SORP decision, funds or reconciled net assets changed. Regenerate the pack, record a new directors' approval (s.324) and approve again.");
        }
    }

    private async Task EnsureCroArtifactSourceCurrentAsync(
        AccountingPeriod period,
        CroFilingPackage package)
    {
        if (!IsSha256(package.ArtifactSourceFingerprintSha256))
            Block("CRO artifacts are missing their deterministic legal, tax and accounting source fingerprint.");

        var current = await ComputeCroSourceFingerprintAsync(period);
        if (!string.Equals(current, package.ArtifactSourceFingerprintSha256, StringComparison.OrdinalIgnoreCase))
        {
            Block("CRO artifacts and the directors' approval (s.324) are stale because legal, tax or accounting source evidence changed. Regenerate the pack, record a new directors' approval and approve again.");
        }
    }

    private async Task<string> ComputeCroSourceFingerprintAsync(AccountingPeriod period)
    {
        var company = await _db.Companies
            .AsNoTracking()
            .Where(item => item.Id == period.CompanyId)
            .Select(item => new
            {
                item.Id,
                item.TenantId,
                item.LegalName,
                item.TradingName,
                item.CroNumber,
                item.TaxReference,
                item.CompanyType,
                item.IncorporationDate,
                item.RegisteredOfficeAddress1,
                item.RegisteredOfficeAddress2,
                item.RegisteredOfficeCity,
                item.RegisteredOfficeCounty,
                item.RegisteredOfficeEircode,
                item.IsTrading,
                item.IsGroupMember,
                item.IsHolding,
                item.IsCharitableOrganisation
            })
            .SingleAsync();
        var officers = await _db.CompanyOfficers
            .AsNoTracking()
            .Where(item => item.CompanyId == period.CompanyId)
            .OrderBy(item => item.Id)
            .Select(item => new
            {
                item.Id,
                item.Name,
                item.Role,
                item.AppointedDate,
                item.ResignedDate,
                item.Address
            })
            .ToListAsync();
        var notes = await _db.NotesDisclosures
            .AsNoTracking()
            .Where(item => item.PeriodId == period.Id)
            .OrderBy(item => item.NoteNumber)
            .ThenBy(item => item.Id)
            .Select(item => new
            {
                item.Id,
                item.NoteNumber,
                item.Title,
                item.Content,
                item.IsRequired,
                item.IsIncluded
            })
            .ToListAsync();
        var taxBalances = await _db.TaxBalances
            .AsNoTracking()
            .Where(item => item.PeriodId == period.Id)
            .OrderBy(item => item.TaxType)
            .ThenBy(item => item.Id)
            .Select(item => new
            {
                item.Id,
                item.TaxType,
                item.Liability,
                item.Paid,
                item.Balance
            })
            .ToListAsync();
        var trialBalance = await _statements.GetTrialBalanceAsync(period.CompanyId, period.Id);
        var profitAndLoss = await _statements.GetProfitAndLossAsync(period.CompanyId, period.Id);
        var balanceSheet = await _statements.GetBalanceSheetAsync(period.CompanyId, period.Id);
        var cashFlow = await _statements.GetCashFlowStatementAsync(period.CompanyId, period.Id);
        var equityChanges = await _statements.GetEquityChangesAsync(period.CompanyId, period.Id);

        var canonical = JsonSerializer.Serialize(new
        {
            Company = company,
            Officers = officers,
            Period = new
            {
                period.Id,
                period.CompanyId,
                period.PeriodStart,
                period.PeriodEnd,
                period.IsFirstYear,
                period.ApprovalDate,
                period.ClosingRetainedEarnings,
                AuditorReportSha256 = period.AuditorsReportSha256
            },
            Classification = period.SizeClassification,
            Regime = period.FilingRegime,
            TrialBalance = trialBalance,
            ProfitAndLoss = profitAndLoss,
            BalanceSheet = balanceSheet,
            CashFlow = cashFlow,
            EquityChanges = equityChanges,
            Notes = notes,
            TaxBalances = taxBalances
        });
        return ComputeSha256(Encoding.UTF8.GetBytes(canonical));
    }

    private static byte[] AttachAuditorReport(
        byte[] accountsPdf,
        byte[] auditorReportPdf,
        string reportReference,
        DateTime signedAtUtc)
    {
        if (!IsPdfArtifact(accountsPdf) || !IsPdfArtifact(auditorReportPdf))
            Block("Both the final accounts and retained signed auditor report must be valid PDF artifacts before attachment.");

        var directory = Path.Combine(Path.GetTempPath(), $"accounts-auditor-attachment-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var accountsPath = Path.Combine(directory, "accounts.pdf");
        var auditorPath = Path.Combine(directory, "signed-auditor-report.pdf");
        var outputPath = Path.Combine(directory, "final-accounts.pdf");
        try
        {
            File.WriteAllBytes(accountsPath, accountsPdf);
            File.WriteAllBytes(auditorPath, auditorReportPdf);
            DocumentOperation
                .LoadFile(accountsPath)
                .AddAttachment(new DocumentOperation.DocumentAttachment
                {
                    Key = "signed-auditor-report.pdf",
                    FilePath = auditorPath,
                    AttachmentName = "signed-auditor-report.pdf",
                    MimeType = "application/pdf",
                    Description = $"Actual signed auditor report {reportReference}",
                    CreationDate = signedAtUtc,
                    ModificationDate = signedAtUtc,
                    Relationship = DocumentOperation.DocumentAttachmentRelationship.Supplement
                })
                .Save(outputPath);
            var attached = File.ReadAllBytes(outputPath);
            if (!IsPdfArtifact(attached) || attached.Length <= accountsPdf.Length)
                Block("The signed auditor report could not be retained inside the final accounts PDF.");
            // qpdf derives the trailer /ID from the clock; pin it to the content so re-recording the
            // same accounts and report yields the same bytes (and keeps the directors' approval).
            return DeterministicPdf.WithContentDerivedTrailerId(attached);
        }
        catch (FilingReleaseBlockedException)
        {
            throw;
        }
        catch
        {
            throw new FilingReleaseBlockedException(
                "The signed auditor report could not be attached to the final accounts PDF. Verify both retained PDFs and regenerate the pack.");
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    private async Task LogGeneratedAsync(
        AccountingPeriod period,
        FilingReleaseArtifact artifact,
        string hash,
        string candidate,
        string? auditUserId)
    {
        if (_audit is null)
            return;
        await _audit.LogAsync(
            period.CompanyId,
            period.Id,
            "FilingArtifact",
            period.Id,
            AuditEventCodes.FilingArtifactGenerated,
            null,
            new { Artifact = artifact.ToString(), Sha256 = hash, ReleaseCandidate = candidate },
            auditUserId);
    }

    private async Task LogDirectorsApprovalRevokedAsync(
        AccountingPeriod period,
        DirectorsApprovalEvent? revocation,
        string? auditUserId)
    {
        if (_audit is null || revocation is null)
            return;
        await _audit.LogAsync(
            period.CompanyId,
            period.Id,
            nameof(DirectorsApprovalEvent),
            revocation.Id,
            AuditEventCodes.DirectorsApprovalRevoked,
            null,
            new { revocation.ApprovalPageId, revocation.ApprovalPageSha256, revocation.RevocationReason, revocation.RecordSha256 },
            auditUserId);
    }

    private async Task LogEvidenceAsync(
        AccountingPeriod period,
        string evidenceType,
        string sha256,
        string? auditUserId)
    {
        if (_audit is null)
            return;
        await _audit.LogAsync(
            period.CompanyId,
            period.Id,
            "FilingReleaseEvidence",
            period.Id,
            AuditEventCodes.FilingEvidenceRecorded,
            null,
            new { EvidenceType = evidenceType, Sha256 = sha256, ReleaseCandidate = CurrentReleaseCandidate },
            auditUserId);
    }

    private static void AssertRegenerationAllowed(FilingStatus status, string label)
    {
        if (status is FilingStatus.Submitted or FilingStatus.Accepted)
            Block($"{label} artifacts cannot be regenerated after external submission or acceptance. Record a correction workflow first.");
    }

    private static void RequireArtifactBytes(byte[] content)
    {
        if (content.Length == 0)
            throw new FilingReleaseBlockedException("A generated filing artifact cannot be empty.");
    }

    private static string RequireReference(string? value, string label)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            Block($"{label} is required.");
        if (trimmed.Length > 200)
            Block($"{label} must be 200 characters or fewer.");
        return trimmed;
    }

    private static void EnsureMatchingReference(string? supplied, string? retained, string label)
    {
        var suppliedValue = RequireReference(supplied ?? retained, label);
        var retainedValue = RequireReference(retained, label);
        if (!string.Equals(suppliedValue, retainedValue, StringComparison.Ordinal))
            Block($"{label} does not match the accepted filing evidence.");
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool IsPdfArtifact(ReadOnlySpan<byte> content) =>
        content.Length >= 5 && content[..5].SequenceEqual("%PDF-"u8);

    private static string NormalizeCandidate(string candidate)
    {
        var trimmed = candidate?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 200)
            throw new FilingReleaseBlockedException("A valid release candidate identity is required.");
        return trimmed;
    }

    [DoesNotReturn]
    private static void Block(string message) => throw new FilingReleaseBlockedException(message);

    [DoesNotReturn]
    private static T Block<T>(string message) => throw new FilingReleaseBlockedException(message);
}

/// <summary>
/// Professional-body credential checks retained from the former mandatory qualified-accountant gate
/// for the optional accountant review attestation. No filing workflow requires them.
/// </summary>
public static class AccountantCredentialChecks
{
    /// <summary>
    /// The professional bodies accepted for the optional accountant review, by exact name. The
    /// frontend offers exactly this list (frontend/src/lib/filingEvidence.ts).
    /// </summary>
    public static readonly IReadOnlyList<string> RecognisedProfessionalBodies =
    [
        "Chartered Accountants Ireland",
        "Institute of Certified Public Accountants in Ireland",
        "Association of Chartered Certified Accountants",
        "Chartered Institute of Management Accountants",
        "Institute of Chartered Accountants in England and Wales",
        "Institute of Chartered Accountants of Scotland"
    ];

    public static bool IsRecognisedProfessionalBody(string? value) =>
        value is not null && RecognisedProfessionalBodies.Contains(value.Trim(), StringComparer.Ordinal);

    public static bool IsHttpsReference(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
        && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    /// <summary>A UTC verification not in the future and a UTC credential expiry after both it and now.</summary>
    public static bool IsCurrentCredential(DateTime? verifiedAtUtc, DateTime? credentialValidUntilUtc) =>
        verifiedAtUtc is not null
        && verifiedAtUtc.Value.Kind == DateTimeKind.Utc
        && verifiedAtUtc <= DateTime.UtcNow.AddMinutes(5)
        && credentialValidUntilUtc is not null
        && credentialValidUntilUtc.Value.Kind == DateTimeKind.Utc
        && credentialValidUntilUtc > DateTime.UtcNow
        && credentialValidUntilUtc > verifiedAtUtc;
}
