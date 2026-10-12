using Accounts.Api.Data;
using Accounts.Api.Endpoints;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Accounts.Tests;

/// <summary>
/// The CRO correction tracker. While a filing is sent back for correction (or rejected), every path that
/// revokes a filing approval revokes only the approval: the status, the external submission reference
/// and the correction deadline stay until the corrected pack is resubmitted or an Owner/Reviewer treats
/// the filing as not delivered. An approval still never outlives the bytes it approved.
/// </summary>
public sealed partial class DirectorsApprovalTests
{
    private const string OriginalCoreReference = "CORE-EXAMPLE-1";
    private const string SendBackReason = "Example send-back: the board approval date is wrong.";
    private static readonly DateTime OriginalSubmittedAt = new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);

    private static async Task<CroFilingPackage> SendCroBackAsync(AccountsDbContext db, int periodId, FilingStatus status, DateTime deadline)
    {
        var package = await db.CroFilingPackages.SingleAsync(c => c.PeriodId == periodId);
        package.FilingStatus = status;
        package.CroSubmissionReference = OriginalCoreReference;
        package.SubmittedBy = "Firm Reviewer";
        package.SubmittedAt = OriginalSubmittedAt;
        package.PaymentCompleted = true;
        package.RejectionReason = SendBackReason;
        package.CorrectionDeadline = deadline;
        await db.SaveChangesAsync();
        return package;
    }

    private static void AssertTrackerKept(CroFilingPackage package, FilingStatus status, DateTime deadline)
    {
        Assert.Equal(status, package.FilingStatus);
        Assert.Equal(OriginalCoreReference, package.CroSubmissionReference);
        Assert.Equal("Firm Reviewer", package.SubmittedBy);
        Assert.Equal(OriginalSubmittedAt, package.SubmittedAt);
        Assert.Equal(SendBackReason, package.RejectionReason);
        Assert.Equal(deadline, package.CorrectionDeadline);
    }

    private static FilingWorkflowService Workflow(FilingGateFixture harness)
    {
        var statements = new FinancialStatementsService(harness.Db);
        return new FilingWorkflowService(
            harness.Db,
            statements,
            new IxbrlService(harness.Db, statements),
            new AuditService(harness.Db),
            releaseGate: harness.Gate);
    }

    private static async Task<FilingGateFixture> ApprovedCroAsync()
    {
        var harness = await FilingGateFixture.ReadyForApprovalAsync();
        await harness.RecordDirectorsApprovalAsync();
        await harness.Gate.BindApprovalAsync(harness.CompanyId, harness.PeriodId, FilingReleaseWorkflow.Cro, "user:1");
        return harness;
    }

    public static TheoryData<FilingStatus, string> CorrectionRevokePaths => new()
    {
        { FilingStatus.CorrectionRequired, "regenerate" },
        { FilingStatus.CorrectionRequired, "signing-pack" },
        { FilingStatus.CorrectionRequired, "withdraw" },
        { FilingStatus.CorrectionRequired, "audit-notice" },
        { FilingStatus.CorrectionRequired, "auditor-report" },
        { FilingStatus.Rejected, "regenerate" },
        { FilingStatus.Rejected, "withdraw" },
        { FilingStatus.Rejected, "audit-notice" }
    };

    [Theory]
    [MemberData(nameof(CorrectionRevokePaths))]
    public async Task InCorrection_EveryRevokePath_RevokesOnlyTheApproval_AndKeepsTheTracker(FilingStatus status, string path)
    {
        var harness = await ApprovedCroAsync();
        var db = harness.Db;
        var deadline = DateTime.UtcNow.AddDays(10);
        await SendCroBackAsync(db, harness.PeriodId, status, deadline);

        switch (path)
        {
            case "regenerate":
                await harness.Gate.RecordCroArtifactAsync(harness.CompanyId, harness.PeriodId, FilingReleaseArtifact.CroAccountsPdf,
                    FilingGateFixture.Pdf("corrected-cro-accounts-v2"), "reviewer@example.ie");
                break;
            case "signing-pack":
                var signed = FilingGateFixture.Pdf("re-executed-cro-signing-pack");
                await harness.Gate.RecordVerifiedCroSignatureAsync(harness.CompanyId, harness.PeriodId, new CroSignatureEvidence(
                    "Director One", "B Secretary", DateTime.UtcNow.AddMinutes(-1), signed, FilingReleaseGate.ComputeSha256(signed)));
                break;
            case "withdraw":
                await new DirectorsApprovalService(db, new FakePageRenderer(), new AuditService(db))
                    .WithdrawAsync(harness.CompanyId, harness.PeriodId, Withdrawal(), Owner);
                break;
            case "audit-notice":
                var package = await db.CroFilingPackages.SingleAsync(c => c.PeriodId == harness.PeriodId);
                Assert.True(FilingReleaseGate.RevokeCroApprovalForLostAuditExemption(package));
                await db.SaveChangesAsync();
                break;
            case "auditor-report":
                db.FilingRegimes.Single(r => r.PeriodId == harness.PeriodId).AuditExempt = false;
                await db.SaveChangesAsync();
                var opinion = FilingGateFixture.Pdf("SIGNED INDEPENDENT AUDITOR OPINION");
                await harness.Gate.RecordVerifiedAuditorReportAsync(harness.CompanyId, harness.PeriodId, new AuditorReportEvidence(
                    "Independent Audit Firm", "Named Statutory Auditor", "Chartered Accountants Ireland",
                    "auditor-member-456", "AUDITOR-REPORT-2026-001", DateTime.UtcNow.AddMinutes(-10),
                    "Firm Owner", DateTime.UtcNow.AddMinutes(-5), "accepted",
                    opinion, FilingReleaseGate.ComputeSha256(opinion)));
                break;
        }

        var stored = await db.CroFilingPackages.AsNoTracking().SingleAsync(c => c.PeriodId == harness.PeriodId);
        AssertTrackerKept(stored, status, deadline);
        Assert.Null(stored.ApprovedBy);
        Assert.Null(stored.ApprovedArtifactManifestSha256);
        Assert.Null(stored.ApprovalCapacity);
    }

    [Fact]
    public async Task InCorrection_ReSavingTheSameSignaturePage_KeepsTheTrackerAndTheApproval()
    {
        var harness = await ApprovedCroAsync();
        var deadline = DateTime.UtcNow.AddDays(10);
        var package = await SendCroBackAsync(harness.Db, harness.PeriodId, FilingStatus.CorrectionRequired, deadline);
        // The exact retained bytes: a freshly rendered PDF can carry a different creation timestamp.
        var sameBytes = package.SignaturePageArtifact!.ToArray();

        await harness.Gate.RecordCroArtifactAsync(harness.CompanyId, harness.PeriodId, FilingReleaseArtifact.CroSignaturePage,
            sameBytes, "reviewer@example.ie");

        var stored = await harness.Db.CroFilingPackages.AsNoTracking().SingleAsync(c => c.PeriodId == harness.PeriodId);
        AssertTrackerKept(stored, FilingStatus.CorrectionRequired, deadline);
        Assert.Equal(FilingReleaseGate.BoardApprovalCapacity, stored.ApprovalCapacity);
    }

    public static TheoryData<string> AfterApprovalRevokePaths => new() { "regenerate", "withdraw" };

    [Theory]
    [MemberData(nameof(AfterApprovalRevokePaths))]
    public async Task ApprovedDuringACorrection_ThenRevoked_ReturnsToTheCorrectionAndKeepsTheTracker(string path)
    {
        var harness = await ApprovedCroAsync();
        var db = harness.Db;
        var workflow = Workflow(harness);
        var deadline = DateTime.UtcNow.AddDays(10);
        await SendCroBackAsync(db, harness.PeriodId, FilingStatus.CorrectionRequired, deadline);
        var approved = await workflow.UpdateCroStatusAsync(harness.CompanyId, harness.PeriodId, FilingStatus.Approved, "Firm Reviewer", auditUserId: "user:1");
        Assert.Equal(FilingStatus.Approved, approved.FilingStatus);

        if (path == "regenerate")
            await harness.Gate.RecordCroArtifactAsync(harness.CompanyId, harness.PeriodId, FilingReleaseArtifact.CroAccountsPdf,
                FilingGateFixture.Pdf("corrected-cro-accounts-v3"), "reviewer@example.ie");
        else
            await new DirectorsApprovalService(db, new FakePageRenderer(), new AuditService(db))
                .WithdrawAsync(harness.CompanyId, harness.PeriodId, Withdrawal(), Owner);

        var stored = await db.CroFilingPackages.AsNoTracking().SingleAsync(c => c.PeriodId == harness.PeriodId);
        AssertTrackerKept(stored, FilingStatus.CorrectionRequired, deadline);
        Assert.Null(stored.ApprovedBy);
        Assert.Null(stored.ApprovalCapacity);
    }

    [Theory]
    [InlineData(OriginalCoreReference)]
    [InlineData(" core-example-1 ")]
    [InlineData(null)]
    public async Task ResubmittingDuringACorrection_RefusesThePreviousCoreReference(string? reference)
    {
        var harness = await ApprovedCroAsync();
        var workflow = Workflow(harness);
        var deadline = DateTime.UtcNow.AddDays(10);
        await SendCroBackAsync(harness.Db, harness.PeriodId, FilingStatus.CorrectionRequired, deadline);
        await workflow.UpdateCroStatusAsync(harness.CompanyId, harness.PeriodId, FilingStatus.Approved, "Firm Reviewer", auditUserId: "user:1");

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => workflow.UpdateCroStatusAsync(
            harness.CompanyId, harness.PeriodId, FilingStatus.Submitted, "Firm Reviewer", submissionReference: reference, auditUserId: "user:1"));

        Assert.Equal(
            "Record the new CORE submission reference for the corrected filing; CORE-EXAMPLE-1 is the reference of the filing the CRO sent back.",
            error.Message);
        var stored = await harness.Db.CroFilingPackages.AsNoTracking().SingleAsync(c => c.PeriodId == harness.PeriodId);
        Assert.Equal(FilingStatus.Approved, stored.FilingStatus);
        Assert.Equal(OriginalCoreReference, stored.CroSubmissionReference);
        Assert.Equal(deadline, stored.CorrectionDeadline);
    }

    [Fact]
    public async Task ResubmittingDuringACorrection_ClearsTheCorrectionMarker_AndTheAuditKeepsIt()
    {
        var harness = await ApprovedCroAsync();
        var workflow = Workflow(harness);
        var deadline = DateTime.UtcNow.AddDays(10);
        await SendCroBackAsync(harness.Db, harness.PeriodId, FilingStatus.CorrectionRequired, deadline);
        await workflow.UpdateCroStatusAsync(harness.CompanyId, harness.PeriodId, FilingStatus.Approved, "Firm Reviewer", auditUserId: "user:1");

        var submitted = await workflow.UpdateCroStatusAsync(harness.CompanyId, harness.PeriodId, FilingStatus.Submitted, "Firm Reviewer",
            submissionReference: "CORE-EXAMPLE-2", auditUserId: "user:1");

        Assert.Equal((FilingStatus.Submitted, "CORE-EXAMPLE-2"), (submitted.FilingStatus, submitted.CroSubmissionReference));
        Assert.Null(submitted.CorrectionDeadline);
        Assert.Null(submitted.RejectionReason);
        var log = harness.Db.AuditLogs.Where(a => a.Action == AuditEventCodes.CroFilingStatusChanged).OrderBy(a => a.Id).AsEnumerable().Last();
        Assert.Contains(OriginalCoreReference, log.OldValueJson);
        Assert.Contains("Example send-back", log.OldValueJson);
    }

    [Fact]
    public async Task ApprovedOutsideACorrection_ThenRegenerated_BehavesAsBefore()
    {
        var harness = await ApprovedCroAsync();
        var workflow = Workflow(harness);
        await workflow.UpdateCroStatusAsync(harness.CompanyId, harness.PeriodId, FilingStatus.Approved, "Firm Reviewer", auditUserId: "user:1");

        await harness.Gate.RecordCroArtifactAsync(harness.CompanyId, harness.PeriodId, FilingReleaseArtifact.CroAccountsPdf,
            FilingGateFixture.Pdf("regenerated-cro-accounts-v2"), "reviewer@example.ie");

        var stored = await harness.Db.CroFilingPackages.AsNoTracking().SingleAsync(c => c.PeriodId == harness.PeriodId);
        Assert.Equal(FilingStatus.PackageGenerated, stored.FilingStatus);
        Assert.Null(stored.ApprovedBy);
        Assert.Null(stored.CroSubmissionReference);
        Assert.Null(stored.CorrectionDeadline);
    }

    [Fact]
    public void WithdrawingTheBoardApproval_AfterACharityCorrectionWasApproved_ReturnsItToTheCorrection()
    {
        var deadline = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);
        var charity = new CharityFilingPackage
        {
            FilingStatus = FilingStatus.Approved, Status = FilingPackageStatus.Submitted, ApprovedBy = "Board of directors",
            ApprovalCapacity = FilingReleaseGate.BoardApprovalCapacity, SofaGenerated = true, TrusteesReportGenerated = true,
            AnnualReturnReference = "AR-EXAMPLE-1", SubmittedBy = "Firm Reviewer", SubmittedAt = OriginalSubmittedAt,
            RejectionReason = "Example charity correction.", CorrectionDeadline = deadline
        };

        FilingReleaseGate.RevokeApprovalsBoundToWithdrawnBoardApproval(new AccountingPeriod { CharityFilingPackage = charity });

        Assert.Equal((FilingStatus.CorrectionRequired, "AR-EXAMPLE-1", OriginalSubmittedAt, deadline, (string?)null),
            (charity.FilingStatus, charity.AnnualReturnReference, charity.SubmittedAt, charity.CorrectionDeadline, charity.ApprovedBy));
    }

    [Fact]
    public async Task OutsideCorrection_RegenerationStillResetsTheStatusAndClearsTheReference()
    {
        var harness = await ApprovedCroAsync();
        var package = await harness.Db.CroFilingPackages.SingleAsync(c => c.PeriodId == harness.PeriodId);
        package.FilingStatus = FilingStatus.Approved;
        package.CroSubmissionReference = OriginalCoreReference;
        await harness.Db.SaveChangesAsync();

        await harness.Gate.RecordCroArtifactAsync(harness.CompanyId, harness.PeriodId, FilingReleaseArtifact.CroAccountsPdf,
            FilingGateFixture.Pdf("regenerated-cro-accounts-v2"), "reviewer@example.ie");

        var stored = await harness.Db.CroFilingPackages.AsNoTracking().SingleAsync(c => c.PeriodId == harness.PeriodId);
        Assert.Equal(FilingStatus.PackageGenerated, stored.FilingStatus);
        Assert.Null(stored.CroSubmissionReference);
        Assert.Null(stored.ApprovedBy);
    }

    [Fact]
    public async Task InCorrection_ApprovingAndRecordingTheNewSubmission_EndsTheTracker()
    {
        var harness = await ApprovedCroAsync();
        var workflow = Workflow(harness);
        await SendCroBackAsync(harness.Db, harness.PeriodId, FilingStatus.CorrectionRequired, DateTime.UtcNow.AddDays(10));

        var approved = await workflow.UpdateCroStatusAsync(harness.CompanyId, harness.PeriodId, FilingStatus.Approved, "Firm Reviewer", auditUserId: "user:1");
        Assert.Equal(FilingStatus.Approved, approved.FilingStatus);
        Assert.Equal(FilingReleaseGate.BoardApprovalCapacity, approved.ApprovalCapacity);

        var submitted = await workflow.UpdateCroStatusAsync(harness.CompanyId, harness.PeriodId, FilingStatus.Submitted, "Firm Reviewer",
            submissionReference: "CORE-EXAMPLE-2", auditUserId: "user:1");

        Assert.Equal(FilingStatus.Submitted, submitted.FilingStatus);
        Assert.Equal("CORE-EXAMPLE-2", submitted.CroSubmissionReference);
        var status = await workflow.GetStatusAsync(harness.CompanyId, harness.PeriodId);
        Assert.Equal(FilingStatus.Submitted, status.Cro.Status);
    }

    [Fact]
    public async Task WrongBoardDate_SendBack_IsCorrectedEndToEnd_WithTheTrackerKeptUntilResubmission()
    {
        var harness = await ApprovedCroAsync();
        var db = harness.Db;
        var workflow = Workflow(harness);
        await workflow.UpdateCroStatusAsync(harness.CompanyId, harness.PeriodId, FilingStatus.Approved, "Firm Reviewer", auditUserId: "user:1");
        await workflow.UpdateCroStatusAsync(harness.CompanyId, harness.PeriodId, FilingStatus.Submitted, "Firm Reviewer",
            submissionReference: OriginalCoreReference, auditUserId: "user:1");
        await workflow.UpdateCroStatusAsync(harness.CompanyId, harness.PeriodId, FilingStatus.CorrectionRequired, "Firm Reviewer",
            reason: SendBackReason, auditUserId: "user:1");
        var sentBack = await db.CroFilingPackages.AsNoTracking().SingleAsync(c => c.PeriodId == harness.PeriodId);
        var deadline = sentBack.CorrectionDeadline!.Value;

        // 1. Withdraw the directors' approval: allowed during the correction, and the tracker stays.
        var approvals = new DirectorsApprovalService(db, new FakePageRenderer(), new AuditService(db));
        await approvals.WithdrawAsync(harness.CompanyId, harness.PeriodId, Withdrawal("entered-in-error", "The board met on a different date."), Owner);
        var afterWithdrawal = await db.CroFilingPackages.AsNoTracking().SingleAsync(c => c.PeriodId == harness.PeriodId);
        Assert.Equal(FilingStatus.CorrectionRequired, afterWithdrawal.FilingStatus);
        Assert.Equal(OriginalCoreReference, afterWithdrawal.CroSubmissionReference);
        Assert.Equal(deadline, afterWithdrawal.CorrectionDeadline);
        Assert.Null(afterWithdrawal.ApprovedBy);

        // 2. Correct the board approval date.
        var period = await db.AccountingPeriods.SingleAsync(p => p.Id == harness.PeriodId);
        var irishToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Dublin")));
        period.ApprovalDate = irishToday.AddDays(-1);
        await NotesDisclosureService.RefreshApprovalNoteAsync(db, period);
        await db.SaveChangesAsync();

        // 3. Regenerate the corrected pack and re-execute the signing pack.
        await harness.Gate.RecordCroArtifactAsync(harness.CompanyId, harness.PeriodId, FilingReleaseArtifact.CroAccountsPdf,
            FilingGateFixture.Pdf("corrected-cro-accounts-v2"), "reviewer@example.ie");
        await harness.Gate.RecordCroArtifactAsync(harness.CompanyId, harness.PeriodId, FilingReleaseArtifact.CroSignaturePage,
            FilingGateFixture.Pdf("corrected-cro-signature-v2"), "reviewer@example.ie");
        var signed = FilingGateFixture.Pdf("re-executed-cro-signing-pack");
        await harness.Gate.RecordVerifiedCroSignatureAsync(harness.CompanyId, harness.PeriodId, new CroSignatureEvidence(
            "Director One", "B Secretary", DateTime.UtcNow.AddMinutes(-1), signed, FilingReleaseGate.ComputeSha256(signed)));
        var regenerated = await db.CroFilingPackages.AsNoTracking().SingleAsync(c => c.PeriodId == harness.PeriodId);
        Assert.Equal(FilingStatus.CorrectionRequired, regenerated.FilingStatus);
        Assert.Equal(OriginalCoreReference, regenerated.CroSubmissionReference);
        Assert.Equal(deadline, regenerated.CorrectionDeadline);

        // 4. The board approves the corrected accounts on the corrected date.
        await harness.RecordDirectorsApprovalAsync();
        Assert.Equal(irishToday.AddDays(-1), (await approvals.GetCurrentApprovalAsync(harness.PeriodId))!.Page.ApprovalDate);

        // 5. Approve the corrected pack and record the new CORE submission: the tracker ends.
        await workflow.UpdateCroStatusAsync(harness.CompanyId, harness.PeriodId, FilingStatus.Approved, "Firm Reviewer", auditUserId: "user:1");
        var resubmitted = await workflow.UpdateCroStatusAsync(harness.CompanyId, harness.PeriodId, FilingStatus.Submitted, "Firm Reviewer",
            submissionReference: "CORE-EXAMPLE-2", auditUserId: "user:1");
        Assert.Equal(FilingStatus.Submitted, resubmitted.FilingStatus);
        Assert.Equal("CORE-EXAMPLE-2", resubmitted.CroSubmissionReference);
        Assert.Null(resubmitted.CorrectionDeadline);
        Assert.Null(resubmitted.RejectionReason);
    }

    // ---- Treat as not delivered --------------------------------------------------------------------

    [Fact]
    public async Task DeadlinePassed_ApprovalStaysBlocked_UntilTheFilingIsTreatedAsNotDelivered()
    {
        var harness = await ApprovedCroAsync();
        var db = harness.Db;
        var workflow = Workflow(harness);
        var passed = DateTime.UtcNow.AddDays(-1);
        await SendCroBackAsync(db, harness.PeriodId, FilingStatus.CorrectionRequired, passed);
        var reviewer = Owner with { UserId = 5, DisplayName = "Firm Reviewer", Role = "Reviewer" };

        var blocked = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            workflow.UpdateCroStatusAsync(harness.CompanyId, harness.PeriodId, FilingStatus.Approved, "Firm Reviewer", auditUserId: "user:1"));
        Assert.Contains("CRO correction deadline has passed", blocked.Message);

        var closed = await workflow.TreatCroAsNotDeliveredAsync(harness.CompanyId, harness.PeriodId,
            "  The CRO deadline passed before the corrected pack was ready.  ", reviewer);

        Assert.Equal(FilingStatus.PackageGenerated, closed.FilingStatus);
        Assert.Null(closed.CroSubmissionReference);
        Assert.Null(closed.SubmittedBy);
        Assert.Null(closed.SubmittedAt);
        Assert.False(closed.PaymentCompleted);
        Assert.Null(closed.ApprovedBy);
        Assert.Null(closed.ApprovalCapacity);
        // The correction marker is cleared too; the old deadline and reason stay in the audit record.
        Assert.Null(closed.CorrectionDeadline);
        Assert.Null(closed.RejectionReason);
        var log = Assert.Single(db.AuditLogs.Where(a => a.Action == AuditEventCodes.CroFilingTreatedAsNotDelivered));
        Assert.Equal(AuthenticatedIdentity.AuditUserId(reviewer), log.UserId);
        Assert.Contains(OriginalCoreReference, log.OldValueJson);
        Assert.Contains("Example send-back", log.OldValueJson);
        Assert.Contains(passed.ToString("yyyy-MM-dd"), log.OldValueJson);
        Assert.Contains("The CRO deadline passed before the corrected pack was ready.", log.NewValueJson);
        Assert.DoesNotContain("  The CRO deadline", log.NewValueJson);
        // The deadlines are recalculated: the CRO delivery deadline is open (not filed) again.
        var croDeadline = await db.FilingDeadlines.AsNoTracking()
            .SingleAsync(d => d.PeriodId == harness.PeriodId && d.DeadlineType == DeadlineType.CRO);
        Assert.Null(croDeadline.FiledDate);

        // The closed filing can be approved afresh.
        var approved = await workflow.UpdateCroStatusAsync(harness.CompanyId, harness.PeriodId, FilingStatus.Approved, "Firm Reviewer", auditUserId: "user:1");
        Assert.Equal(FilingStatus.Approved, approved.FilingStatus);
    }

    [Fact]
    public async Task TreatAsNotDelivered_ReopensAFiledCroDeadline_SoLateFilingExposureShows()
    {
        var harness = await ApprovedCroAsync();
        var db = harness.Db;
        await new DeadlineService(db).CalculateDeadlinesAsync(harness.CompanyId, harness.PeriodId);
        var deadline = await db.FilingDeadlines.SingleAsync(d => d.PeriodId == harness.PeriodId && d.DeadlineType == DeadlineType.CRO);
        deadline.FiledDate = new DateOnly(2026, 9, 1);
        deadline.FilingReference = OriginalCoreReference;
        await SendCroBackAsync(db, harness.PeriodId, FilingStatus.Rejected, DateTime.UtcNow.AddDays(5));

        await Workflow(harness).TreatCroAsNotDeliveredAsync(harness.CompanyId, harness.PeriodId, "Rejected by the CRO; to be filed afresh.", Owner);

        var reopened = await db.FilingDeadlines.AsNoTracking().SingleAsync(d => d.Id == deadline.Id);
        Assert.Null(reopened.FiledDate);
        Assert.Null(reopened.FilingReference);
        Assert.False(reopened.IsLate);
        var log = Assert.Single(db.AuditLogs.Where(a => a.Action == AuditEventCodes.CroFilingTreatedAsNotDelivered));
        Assert.Contains("2026-09-01", log.OldValueJson);
    }

    [Theory]
    [InlineData(FilingStatus.PackageGenerated)]
    [InlineData(FilingStatus.Approved)]
    [InlineData(FilingStatus.Submitted)]
    [InlineData(FilingStatus.Accepted)]
    public async Task TreatAsNotDelivered_OutsideACorrection_IsRefused(FilingStatus status)
    {
        var harness = await ApprovedCroAsync();
        var package = await harness.Db.CroFilingPackages.SingleAsync(c => c.PeriodId == harness.PeriodId);
        package.FilingStatus = status;
        package.CroSubmissionReference = OriginalCoreReference;
        await harness.Db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Workflow(harness).TreatCroAsNotDeliveredAsync(harness.CompanyId, harness.PeriodId, "Example reason for the record.", Owner));

        Assert.Equal("Only a CRO filing that was sent back for correction or rejected can be treated as not delivered.", error.Message);
        Assert.Equal(OriginalCoreReference, (await harness.Db.CroFilingPackages.AsNoTracking().SingleAsync(c => c.PeriodId == harness.PeriodId)).CroSubmissionReference);
    }

    [Theory]
    [InlineData("Owner", "", "Explain why the CRO filing is being treated as not delivered (5 to 1,000 characters).")]
    [InlineData("Owner", "  abc ", "Explain why the CRO filing is being treated as not delivered (5 to 1,000 characters).")]
    [InlineData("Accountant", "Example reason for the record.", "Only an Owner or Reviewer can treat a CRO filing as not delivered.")]
    [InlineData("Client", "Example reason for the record.", "Only an Owner or Reviewer can treat a CRO filing as not delivered.")]
    public async Task TreatAsNotDelivered_NeedsAReasonAndAnOwnerOrReviewer(string role, string reason, string expected)
    {
        var harness = await ApprovedCroAsync();
        var deadline = DateTime.UtcNow.AddDays(-1);
        await SendCroBackAsync(harness.Db, harness.PeriodId, FilingStatus.CorrectionRequired, deadline);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Workflow(harness).TreatCroAsNotDeliveredAsync(harness.CompanyId, harness.PeriodId, reason, Owner with { Role = role }));

        Assert.Equal(expected, error.Message);
        AssertTrackerKept(await harness.Db.CroFilingPackages.AsNoTracking().SingleAsync(c => c.PeriodId == harness.PeriodId),
            FilingStatus.CorrectionRequired, deadline);
        Assert.False(harness.Db.AuditLogs.Any(a => a.Action == AuditEventCodes.CroFilingTreatedAsNotDelivered));
    }

    [Theory]
    [InlineData("Reviewer", 200)]
    [InlineData("Owner", 200)]
    [InlineData("Accountant", 403)]
    [InlineData("Client", 403)]
    public async Task TreatAsNotDeliveredEndpoint_IsForOwnersAndReviewersOnly(string role, int expected)
    {
        var harness = await ApprovedCroAsync();
        var deadline = DateTime.UtcNow.AddDays(-1);
        await SendCroBackAsync(harness.Db, harness.PeriodId, FilingStatus.CorrectionRequired, deadline);
        var tenantId = (await harness.Db.Companies.SingleAsync(c => c.Id == harness.CompanyId)).TenantId!.Value;
        var context = EndpointContext(role, "POST", FilingPath(harness.CompanyId, harness.PeriodId, "cro-not-delivered"), harness.CompanyId, tenantId);

        var result = await FilingWorkflowEndpoints.TreatCroAsNotDeliveredEndpointAsync(
            harness.CompanyId, harness.PeriodId, new CroNotDeliveredInput("Treated as not delivered after the CRO deadline."),
            Workflow(harness), harness.Db, context, NoApiKey());

        Assert.Equal(expected, Status(result));
        var stored = await harness.Db.CroFilingPackages.AsNoTracking().SingleAsync(c => c.PeriodId == harness.PeriodId);
        if (expected == 200)
        {
            Assert.Equal(FilingStatus.PackageGenerated, Value<FilingWorkflowService.FilingWorkflowStatus>(result).Cro.Status);
            Assert.Null(stored.CroSubmissionReference);
            var log = Assert.Single(harness.Db.AuditLogs.Where(a => a.Action == AuditEventCodes.CroFilingTreatedAsNotDelivered));
            Assert.Equal(AuthenticatedIdentity.AuditUserId(AuthContext.RequireUser(context)), log.UserId);
        }
        else
        {
            AssertTrackerKept(stored, FilingStatus.CorrectionRequired, deadline);
        }
    }

    [Fact]
    public async Task TreatAsNotDeliveredEndpoint_ForAnotherTenantsCompany_IsNotFound()
    {
        var harness = await ApprovedCroAsync();
        await SendCroBackAsync(harness.Db, harness.PeriodId, FilingStatus.CorrectionRequired, DateTime.UtcNow.AddDays(-1));
        var context = EndpointContext("Owner", "POST", FilingPath(harness.CompanyId, harness.PeriodId, "cro-not-delivered"), harness.CompanyId, tenantId: 999);

        var result = await FilingWorkflowEndpoints.TreatCroAsNotDeliveredEndpointAsync(
            harness.CompanyId, harness.PeriodId, new CroNotDeliveredInput("Treated as not delivered after the CRO deadline."),
            Workflow(harness), harness.Db, context, NoApiKey());

        Assert.Equal(404, Status(result));
        Assert.Equal(OriginalCoreReference, (await harness.Db.CroFilingPackages.AsNoTracking().SingleAsync(c => c.PeriodId == harness.PeriodId)).CroSubmissionReference);
    }

    // ---- Fix round 2: the correction is open whatever the status ---------------------------------

    [Fact]
    public async Task ApprovedBeforeTheDeadline_IsBlockedFromSubmissionOnceTheDeadlinePasses()
    {
        var harness = await ApprovedCroAsync();
        var db = harness.Db;
        var workflow = Workflow(harness);
        await SendCroBackAsync(db, harness.PeriodId, FilingStatus.CorrectionRequired, DateTime.UtcNow.AddDays(10));
        await workflow.UpdateCroStatusAsync(harness.CompanyId, harness.PeriodId, FilingStatus.Approved, "Firm Reviewer", auditUserId: "user:1");
        // The deadline passes while the corrected pack waits to be submitted.
        var passed = DateTime.UtcNow.AddDays(-1);
        (await db.CroFilingPackages.SingleAsync(c => c.PeriodId == harness.PeriodId)).CorrectionDeadline = passed;
        await db.SaveChangesAsync();

        Assert.Contains("CRO correction deadline has passed. Treat the filing as not delivered and recalculate late filing exposure.",
            (await workflow.GetStatusAsync(harness.CompanyId, harness.PeriodId)).BlockingIssues);
        var blocked = await Assert.ThrowsAsync<BusinessRuleException>(() => workflow.UpdateCroStatusAsync(
            harness.CompanyId, harness.PeriodId, FilingStatus.Submitted, "Firm Reviewer", submissionReference: "CORE-EXAMPLE-2", auditUserId: "user:1"));

        Assert.Contains("CRO correction deadline has passed", blocked.Message);
        var stored = await db.CroFilingPackages.AsNoTracking().SingleAsync(c => c.PeriodId == harness.PeriodId);
        Assert.Equal((FilingStatus.Approved, OriginalCoreReference, passed), (stored.FilingStatus, stored.CroSubmissionReference, stored.CorrectionDeadline!.Value));
    }

    [Fact]
    public async Task TreatAsNotDelivered_AfterTheCorrectedPackWasApproved_ClosesTheCorrection()
    {
        var harness = await ApprovedCroAsync();
        var db = harness.Db;
        var workflow = Workflow(harness);
        await SendCroBackAsync(db, harness.PeriodId, FilingStatus.CorrectionRequired, DateTime.UtcNow.AddDays(-1));
        // Approved directly (as an approval given before the deadline would have left it).
        var package = await db.CroFilingPackages.SingleAsync(c => c.PeriodId == harness.PeriodId);
        package.FilingStatus = FilingStatus.Approved;
        await db.SaveChangesAsync();
        Assert.True(FilingReleaseGate.HasOpenCorrection(package));

        var closed = await workflow.TreatCroAsNotDeliveredAsync(harness.CompanyId, harness.PeriodId, "The CRO deadline passed before submission.", Owner);

        Assert.Equal(FilingStatus.PackageGenerated, closed.FilingStatus);
        Assert.Null(closed.CroSubmissionReference);
        Assert.Null(closed.ApprovedBy);
        Assert.Null(closed.CorrectionDeadline);
        Assert.Single(db.AuditLogs.Where(a => a.Action == AuditEventCodes.CroFilingTreatedAsNotDelivered));
    }

    public static TheoryData<FilingStatus, FilingStatus> SendBackFromAnotherState => new()
    {
        { FilingStatus.CorrectionRequired, FilingStatus.CorrectionRequired },
        { FilingStatus.CorrectionRequired, FilingStatus.Rejected },
        { FilingStatus.Rejected, FilingStatus.CorrectionRequired },
        { FilingStatus.Approved, FilingStatus.CorrectionRequired },
        { FilingStatus.PackageGenerated, FilingStatus.Rejected },
        { FilingStatus.Accepted, FilingStatus.CorrectionRequired }
    };

    [Theory]
    [MemberData(nameof(SendBackFromAnotherState))]
    public async Task ASendBackOrRejection_IsOnlyRecordedForASubmittedFiling(FilingStatus from, FilingStatus to)
    {
        var harness = await ApprovedCroAsync();
        var passed = DateTime.UtcNow.AddDays(-1);
        await SendCroBackAsync(harness.Db, harness.PeriodId, from, passed);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Workflow(harness).UpdateCroStatusAsync(
            harness.CompanyId, harness.PeriodId, to, "Firm Reviewer", reason: "Example reason for the record.", auditUserId: "user:1"));

        Assert.Equal("A CRO send-back or rejection can only be recorded for a filing recorded as submitted to CORE.", error.Message);
        var stored = await harness.Db.CroFilingPackages.AsNoTracking().SingleAsync(c => c.PeriodId == harness.PeriodId);
        Assert.Equal((from, passed, SendBackReason), (stored.FilingStatus, stored.CorrectionDeadline!.Value, stored.RejectionReason));
    }

    [Theory]
    [InlineData("regenerate")]
    [InlineData("resubmit-old-reference")]
    [InlineData("treat-as-not-delivered")]
    public async Task ARejectedFiling_RecordsItsReason_AndStaysOpenThroughTheCorrectedApproval(string path)
    {
        var harness = await ApprovedCroAsync();
        var db = harness.Db;
        var workflow = Workflow(harness);
        await workflow.UpdateCroStatusAsync(harness.CompanyId, harness.PeriodId, FilingStatus.Approved, "Firm Reviewer", auditUserId: "user:1");
        await workflow.UpdateCroStatusAsync(harness.CompanyId, harness.PeriodId, FilingStatus.Submitted, "Firm Reviewer",
            submissionReference: OriginalCoreReference, auditUserId: "user:1");

        var rejected = await workflow.UpdateCroStatusAsync(harness.CompanyId, harness.PeriodId, FilingStatus.Rejected, "Firm Reviewer",
            reason: "  Example rejection by the CRO.  ", auditUserId: "user:1");
        Assert.Equal(("Example rejection by the CRO.", (DateTime?)null), (rejected.RejectionReason, rejected.CorrectionDeadline));
        await workflow.UpdateCroStatusAsync(harness.CompanyId, harness.PeriodId, FilingStatus.Approved, "Firm Reviewer", auditUserId: "user:1");
        Assert.True(FilingReleaseGate.HasOpenCorrection(await db.CroFilingPackages.SingleAsync(c => c.PeriodId == harness.PeriodId)));

        switch (path)
        {
            case "regenerate":
                await harness.Gate.RecordCroArtifactAsync(harness.CompanyId, harness.PeriodId, FilingReleaseArtifact.CroAccountsPdf,
                    FilingGateFixture.Pdf("corrected-cro-accounts-v2"), "reviewer@example.ie");
                var stored = await db.CroFilingPackages.AsNoTracking().SingleAsync(c => c.PeriodId == harness.PeriodId);
                Assert.Equal((FilingStatus.Rejected, OriginalCoreReference, "Example rejection by the CRO."),
                    (stored.FilingStatus, stored.CroSubmissionReference, stored.RejectionReason));
                Assert.Null(stored.ApprovedBy);
                break;
            case "resubmit-old-reference":
                var error = await Assert.ThrowsAsync<BusinessRuleException>(() => workflow.UpdateCroStatusAsync(
                    harness.CompanyId, harness.PeriodId, FilingStatus.Submitted, "Firm Reviewer", auditUserId: "user:1"));
                Assert.StartsWith("Record the new CORE submission reference for the corrected filing", error.Message, StringComparison.Ordinal);
                var resubmitted = await workflow.UpdateCroStatusAsync(harness.CompanyId, harness.PeriodId, FilingStatus.Submitted, "Firm Reviewer",
                    submissionReference: "CORE-EXAMPLE-2", auditUserId: "user:1");
                Assert.Equal(("CORE-EXAMPLE-2", (string?)null), (resubmitted.CroSubmissionReference, resubmitted.RejectionReason));
                break;
            case "treat-as-not-delivered":
                var closed = await workflow.TreatCroAsNotDeliveredAsync(harness.CompanyId, harness.PeriodId, "Rejected; to be filed afresh.", Owner);
                Assert.Equal((FilingStatus.PackageGenerated, (string?)null, (string?)null),
                    (closed.FilingStatus, closed.CroSubmissionReference, closed.RejectionReason));
                break;
        }
    }

    [Fact]
    public async Task ASendBackOlderThanTheRecordedSubmission_IsNotAnOpenCorrection()
    {
        var harness = await ApprovedCroAsync();
        var package = await harness.Db.CroFilingPackages.SingleAsync(c => c.PeriodId == harness.PeriodId);
        // A legacy row: resubmitted under the old code (the send-back fields were never cleared), then
        // rewound through the API. Its send-back (deadline less 14 days) predates the recorded submission.
        package.FilingStatus = FilingStatus.PackageGenerated;
        package.CroSubmissionReference = "CORE-EXAMPLE-2";
        package.SubmittedAt = DateTime.UtcNow.AddDays(-1);
        package.CorrectionDeadline = DateTime.UtcNow.AddDays(-5);
        package.RejectionReason = SendBackReason;
        await harness.Db.SaveChangesAsync();
        Assert.False(FilingReleaseGate.HasOpenCorrection(package));
        package.SubmittedAt = DateTime.UtcNow.AddDays(-30);
        Assert.True(FilingReleaseGate.HasOpenCorrection(package));
        package.SubmittedAt = DateTime.UtcNow.AddDays(-1);
        await harness.Db.SaveChangesAsync();

        await harness.Gate.RecordCroArtifactAsync(harness.CompanyId, harness.PeriodId, FilingReleaseArtifact.CroAccountsPdf,
            FilingGateFixture.Pdf("regenerated-cro-accounts-v2"), "reviewer@example.ie");

        var stored = await harness.Db.CroFilingPackages.AsNoTracking().SingleAsync(c => c.PeriodId == harness.PeriodId);
        Assert.Equal(FilingStatus.PackageGenerated, stored.FilingStatus);
        Assert.Null(stored.CroSubmissionReference);
    }

    [Fact]
    public async Task ASigningPackRecordedAfterTheCorrectedPackWasApproved_ReturnsItToTheCorrection()
    {
        var harness = await ApprovedCroAsync();
        var deadline = DateTime.UtcNow.AddDays(10);
        await SendCroBackAsync(harness.Db, harness.PeriodId, FilingStatus.CorrectionRequired, deadline);
        await Workflow(harness).UpdateCroStatusAsync(harness.CompanyId, harness.PeriodId, FilingStatus.Approved, "Firm Reviewer", auditUserId: "user:1");

        var signed = FilingGateFixture.Pdf("re-executed-cro-signing-pack");
        await harness.Gate.RecordVerifiedCroSignatureAsync(harness.CompanyId, harness.PeriodId, new CroSignatureEvidence(
            "Director One", "B Secretary", DateTime.UtcNow.AddMinutes(-1), signed, FilingReleaseGate.ComputeSha256(signed)));

        var stored = await harness.Db.CroFilingPackages.AsNoTracking().SingleAsync(c => c.PeriodId == harness.PeriodId);
        AssertTrackerKept(stored, FilingStatus.CorrectionRequired, deadline);
        Assert.Null(stored.ApprovedBy);
    }

    private static async Task<AccountingPeriod> CharityCorrectionApprovedAsync(AccountsDbContext db, DateTime deadline)
    {
        var period = await FilingReleaseGateTests.SeedReleaseReadyPeriodAsync(db, charity: true);
        db.CharityFilingPackages.Add(new CharityFilingPackage
        {
            PeriodId = period.Id, FilingStatus = FilingStatus.Approved, Status = FilingPackageStatus.Submitted,
            ApprovedBy = "Board of directors", ApprovalCapacity = FilingReleaseGate.BoardApprovalCapacity,
            AnnualReturnReference = "AR-EXAMPLE-1", SubmittedBy = "Firm Reviewer", SubmittedAt = OriginalSubmittedAt,
            RejectionReason = "Example charity correction.", CorrectionDeadline = deadline
        });
        await db.SaveChangesAsync();
        return period;
    }

    [Fact]
    public async Task CharityResubmissionDuringACorrection_NeedsAnExplicitReference()
    {
        await using var db = CreateDb();
        var deadline = DateTime.UtcNow.AddDays(10);
        var period = await CharityCorrectionApprovedAsync(db, deadline);
        var statements = new FinancialStatementsService(db);
        var workflow = new FilingWorkflowService(db, statements, new IxbrlService(db, statements));

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => workflow.UpdateCharityStatusAsync(
            period.CompanyId, period.Id, FilingStatus.Submitted, "Firm Reviewer", annualReturnReference: "  ", auditUserId: "user:1"));

        Assert.Equal("Enter the annual return reference of the corrected return; the reference of the return that was sent back is not reused automatically.", error.Message);
        var stored = await db.CharityFilingPackages.AsNoTracking().SingleAsync(c => c.PeriodId == period.Id);
        Assert.Equal((FilingStatus.Approved, "AR-EXAMPLE-1", deadline), (stored.FilingStatus, stored.AnnualReturnReference, stored.CorrectionDeadline!.Value));
    }

    [Fact]
    public async Task CharityTrusteeReview_DuringACorrection_KeepsTheTracker()
    {
        await using var db = CreateDb();
        var deadline = DateTime.UtcNow.AddDays(10);
        var period = await CharityCorrectionApprovedAsync(db, deadline);

        await new CharityReportingService(db).RecordTrusteeReviewAsync(
            period.CompanyId, period.Id, true, "TRUSTEE-REVIEW-EXAMPLE-1", System.Text.Encoding.UTF8.GetBytes("signed trustee review"), "Firm Reviewer");

        var stored = await db.CharityFilingPackages.AsNoTracking().SingleAsync(c => c.PeriodId == period.Id);
        Assert.Equal((FilingStatus.CorrectionRequired, "AR-EXAMPLE-1", OriginalSubmittedAt, deadline, (string?)null),
            (stored.FilingStatus, stored.AnnualReturnReference, stored.SubmittedAt, stored.CorrectionDeadline, stored.ApprovedBy));
    }

    // ---- Revenue and Charity equivalents -----------------------------------------------------------

    [Theory]
    [InlineData(FilingStatus.CorrectionRequired)]
    [InlineData(FilingStatus.Rejected)]
    public void WithdrawingTheBoardApproval_InCorrection_KeepsEveryPackagesStatusAndExternalReference(FilingStatus status)
    {
        var deadline = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);
        var cro = new CroFilingPackage
        {
            FilingStatus = status, ApprovedBy = "Board of directors", ApprovalCapacity = FilingReleaseGate.BoardApprovalCapacity,
            AccountsPdfGenerated = true, SignaturePageGenerated = true,
            CroSubmissionReference = OriginalCoreReference, SubmittedAt = OriginalSubmittedAt, RejectionReason = SendBackReason, CorrectionDeadline = deadline
        };
        var revenue = new RevenueFilingPackage
        {
            FilingStatus = status, ApprovedBy = "Board of directors", ApprovalCapacity = FilingReleaseGate.BoardApprovalCapacity,
            Ct1Reference = "CT1-EXAMPLE-1", IxbrlValidationErrors = "Example Revenue correction."
        };
        var charity = new CharityFilingPackage
        {
            FilingStatus = status, Status = FilingPackageStatus.Submitted, ApprovedBy = "Board of directors",
            ApprovalCapacity = FilingReleaseGate.BoardApprovalCapacity, SofaGenerated = true, TrusteesReportGenerated = true,
            AnnualReturnReference = "AR-EXAMPLE-1", SubmittedBy = "Firm Reviewer", SubmittedAt = OriginalSubmittedAt,
            RejectionReason = "Example charity correction.", CorrectionDeadline = deadline
        };
        var period = new AccountingPeriod { CroFilingPackage = cro, RevenueFilingPackage = revenue, CharityFilingPackage = charity };

        var revoked = FilingReleaseGate.RevokeApprovalsBoundToWithdrawnBoardApproval(period);

        Assert.Equal(["CRO", "Revenue", "Charity"], revoked);
        Assert.Equal((status, OriginalCoreReference, OriginalSubmittedAt, deadline, (string?)null),
            (cro.FilingStatus, cro.CroSubmissionReference, cro.SubmittedAt, cro.CorrectionDeadline, cro.ApprovedBy));
        Assert.Equal((status, "CT1-EXAMPLE-1", "Example Revenue correction.", (string?)null),
            (revenue.FilingStatus, revenue.Ct1Reference, revenue.IxbrlValidationErrors, revenue.ApprovedBy));
        Assert.Equal((status, FilingPackageStatus.Submitted, "AR-EXAMPLE-1", "Firm Reviewer", OriginalSubmittedAt, deadline, (string?)null),
            (charity.FilingStatus, charity.Status, charity.AnnualReturnReference, charity.SubmittedBy, charity.SubmittedAt, charity.CorrectionDeadline, charity.ApprovedBy));
    }

    [Fact]
    public async Task RegeneratingRevenueIxbrl_InCorrection_RevokesTheApprovalAndKeepsTheReference()
    {
        var harness = await FilingGateFixture.ReadyForApprovalAsync();
        harness.Db.RevenueFilingPackages.Add(new RevenueFilingPackage
        {
            PeriodId = harness.PeriodId, FilingStatus = FilingStatus.CorrectionRequired, IxbrlGenerated = true,
            IxbrlArtifact = [1, 2, 3], IxbrlSha256 = FilingReleaseGate.ComputeSha256([1, 2, 3]), ArtifactReleaseCandidate = FilingGateFixture.Candidate,
            ApprovedBy = "Board of directors", ApprovalCapacity = FilingReleaseGate.BoardApprovalCapacity,
            Ct1Reference = "CT1-EXAMPLE-1", IxbrlValidationErrors = "Example Revenue correction."
        });
        await harness.Db.SaveChangesAsync();

        await harness.Gate.RecordRevenueIxbrlArtifactAsync(harness.CompanyId, harness.PeriodId, [4, 5, 6]);

        var stored = await harness.Db.RevenueFilingPackages.AsNoTracking().SingleAsync(r => r.PeriodId == harness.PeriodId);
        Assert.Equal(FilingStatus.CorrectionRequired, stored.FilingStatus);
        Assert.Equal("CT1-EXAMPLE-1", stored.Ct1Reference);
        Assert.Null(stored.ApprovedBy);
        Assert.Equal(FilingReleaseGate.ComputeSha256([4, 5, 6]), stored.IxbrlSha256);
    }

    [Fact]
    public async Task RegeneratingOrInvalidatingCharityArtifacts_InCorrection_RevokesTheApprovalAndKeepsTheTracker()
    {
        var harness = await FilingGateFixture.ReadyForApprovalAsync();
        var deadline = DateTime.UtcNow.AddDays(10);
        var charity = new CharityFilingPackage
        {
            PeriodId = harness.PeriodId, FilingStatus = FilingStatus.CorrectionRequired, Status = FilingPackageStatus.Submitted,
            ArtifactReleaseCandidate = FilingGateFixture.Candidate, SofaGenerated = true, TrusteesReportGenerated = true,
            SofaArtifact = [1], SofaSha256 = FilingReleaseGate.ComputeSha256([1]),
            ApprovedBy = "Board of directors", ApprovalCapacity = FilingReleaseGate.BoardApprovalCapacity,
            AnnualReturnReference = "AR-EXAMPLE-1", SubmittedBy = "Firm Reviewer", SubmittedAt = OriginalSubmittedAt,
            RejectionReason = "Example charity correction.", CorrectionDeadline = deadline
        };
        harness.Db.CharityFilingPackages.Add(charity);
        await harness.Db.SaveChangesAsync();

        await harness.Gate.RecordCharityArtifactAsync(harness.CompanyId, harness.PeriodId, FilingReleaseArtifact.CharitySofa, [2]);
        var regenerated = await harness.Db.CharityFilingPackages.AsNoTracking().SingleAsync(c => c.PeriodId == harness.PeriodId);
        CharityReportingService.InvalidateArtifacts(charity);
        await harness.Db.SaveChangesAsync();
        var invalidated = await harness.Db.CharityFilingPackages.AsNoTracking().SingleAsync(c => c.PeriodId == harness.PeriodId);

        foreach (var stored in new[] { regenerated, invalidated })
        {
            Assert.Equal(FilingStatus.CorrectionRequired, stored.FilingStatus);
            Assert.Equal(FilingPackageStatus.Submitted, stored.Status);
            Assert.Equal("AR-EXAMPLE-1", stored.AnnualReturnReference);
            Assert.Equal(("Firm Reviewer", OriginalSubmittedAt), (stored.SubmittedBy, stored.SubmittedAt));
            Assert.Equal(deadline, stored.CorrectionDeadline);
            Assert.Null(stored.ApprovedBy);
        }
        Assert.False(invalidated.SofaGenerated);
    }
}
