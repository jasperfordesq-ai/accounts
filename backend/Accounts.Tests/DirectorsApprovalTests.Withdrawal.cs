using Accounts.Api.Data;
using Accounts.Api.Endpoints;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Accounts.Tests;

/// <summary>
/// A recorded s.324 approval can be withdrawn by an Owner or Reviewer. The approval is never deleted:
/// a Revoked event is chained after it, and filing approvals bound to it are revoked with it.
/// </summary>
public sealed partial class DirectorsApprovalTests
{
    private static DirectorsApprovalWithdrawalInput Withdrawal(string reason = "entered-in-error", string note = "Recorded against the wrong board minute.") =>
        new(reason, note);

    private static DirectorsApprovalService ServiceAt(AccountsDbContext db, DateTimeOffset now) =>
        new(db, new FakePageRenderer(), new AuditService(db), new FixedClock(now));

    [Theory]
    [InlineData("entered-in-error")]
    [InlineData("board-rescinded")]
    [InlineData("superseded")]
    public async Task Withdraw_AppendsAChainedRevocationAttributedToTheActor(string reason)
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        await ApproveAsync(db, p);
        var approved = await db.DirectorsApprovalEvents.SingleAsync();
        // A sub-microsecond clock proves the stored timestamp is truncated to what PostgreSQL holds.
        var service = ServiceAt(db, new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero).AddTicks(7));
        var reviewer = Owner with { UserId = 5, DisplayName = "Firm Reviewer", Role = "reviewer" };

        var revoked = await service.WithdrawAsync(p.CompanyId, p.Id, Withdrawal(reason, "  The board withdrew it.  "), reviewer);

        Assert.Null(await service.GetCurrentApprovalAsync(p.Id));
        Assert.Equal(DirectorsApprovalEventKind.Revoked, revoked.Kind);
        Assert.Equal(approved.RecordSha256, revoked.PreviousRecordSha256);
        Assert.Equal(approved.ApprovalPageId, revoked.ApprovalPageId);
        Assert.Equal($"withdrawn-{reason}", revoked.RevocationReason);
        Assert.Equal(AuthenticatedIdentity.AuditUserId(reviewer), revoked.RecordedByUserId);
        Assert.Equal(AuthenticatedIdentity.ReviewerDisplayName(reviewer), revoked.RecordedByDisplayName);
        Assert.Equal(0, revoked.RecordedAtUtc.Ticks % 10);
        Assert.True(DirectorsApprovalIntegrity.IsValid(revoked));
        // The approval itself is untouched: append-only.
        var stored = await db.DirectorsApprovalEvents.AsNoTracking().OrderBy(e => e.Id).ToListAsync();
        Assert.Equal(2, stored.Count);
        Assert.Equal(DirectorsApprovalEventKind.Approved, stored[0].Kind);
        Assert.Equal(approved.RecordSha256, stored[0].RecordSha256);

        var log = Assert.Single(db.AuditLogs.Where(a => a.Action == AuditEventCodes.DirectorsApprovalRevoked));
        Assert.Equal(AuthenticatedIdentity.AuditUserId(reviewer), log.UserId);
        Assert.Contains(reason, log.NewValueJson);
        Assert.Contains("The board withdrew it.", log.NewValueJson);
        Assert.DoesNotContain("  The board withdrew it.  ", log.NewValueJson);
        Assert.Contains(approved.RecordSha256, log.OldValueJson);
    }

    [Fact]
    public async Task Withdraw_WithoutACurrentApproval_IsRejected()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var service = Service(db);

        var none = await Assert.ThrowsAsync<BusinessRuleException>(() => service.WithdrawAsync(p.CompanyId, p.Id, Withdrawal(), Owner));
        Assert.Equal("There is no current directors' approval to withdraw.", none.Message);

        await ApproveAsync(db, p);
        await service.WithdrawAsync(p.CompanyId, p.Id, Withdrawal(), Owner);
        var twice = await Assert.ThrowsAsync<BusinessRuleException>(() => service.WithdrawAsync(p.CompanyId, p.Id, Withdrawal(), Owner));
        Assert.Equal("There is no current directors' approval to withdraw.", twice.Message);
        Assert.Single(db.DirectorsApprovalEvents.Where(e => e.Kind == DirectorsApprovalEventKind.Revoked));
    }

    [Theory]
    [InlineData("Client")]
    [InlineData("Accountant")]
    public async Task Withdraw_ByAClientOrAccountant_IsRefusedByTheService(string role)
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        await ApproveAsync(db, p);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Service(db).WithdrawAsync(p.CompanyId, p.Id, Withdrawal(), Owner with { Role = role }));

        Assert.Contains("Only an Owner or Reviewer", error.Message);
        Assert.NotNull(await Service(db).GetCurrentApprovalAsync(p.Id));
        Assert.Empty(db.DirectorsApprovalEvents.Where(e => e.Kind == DirectorsApprovalEventKind.Revoked));
    }

    [Theory]
    [InlineData("entered-in-error", "ab", "Explain why the approval is being withdrawn (3 to 500 characters).")]
    [InlineData("entered-in-error", "   ", "Explain why the approval is being withdrawn (3 to 500 characters).")]
    [InlineData("mistake", "Recorded against the wrong minute.", "Choose why the approval is being withdrawn.")]
    [InlineData("Entered-In-Error", "Recorded against the wrong minute.", "Choose why the approval is being withdrawn.")]
    public async Task Withdraw_RejectsAnUnknownReasonOrABadNote(string reason, string note, string expected)
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        await ApproveAsync(db, p);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Service(db).WithdrawAsync(p.CompanyId, p.Id, new DirectorsApprovalWithdrawalInput(reason, note), Owner));

        Assert.Equal(expected, error.Message);
        Assert.Empty(db.DirectorsApprovalEvents.Where(e => e.Kind == DirectorsApprovalEventKind.Revoked));
    }

    [Fact]
    public async Task Withdraw_RejectsANoteLongerThan500Characters()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        await ApproveAsync(db, p);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Service(db).WithdrawAsync(p.CompanyId, p.Id, Withdrawal(note: new string('n', 501)), Owner));
        var accepted = await Service(db).WithdrawAsync(p.CompanyId, p.Id, Withdrawal(note: new string('n', 500)), Owner);
        Assert.Equal(DirectorsApprovalEventKind.Revoked, accepted.Kind);
    }

    [Fact]
    public async Task Withdraw_RevokesTheCroPackageApprovalBoundToTheBoardAndResetsItsStatus()
    {
        var harness = await FilingGateFixture.ReadyForApprovalAsync();
        var db = harness.Db;
        await harness.RecordDirectorsApprovalAsync();
        await harness.Gate.BindApprovalAsync(harness.CompanyId, harness.PeriodId, FilingReleaseWorkflow.Cro, "user:1");
        var approved = await db.CroFilingPackages.SingleAsync(c => c.PeriodId == harness.PeriodId);
        Assert.Equal(FilingReleaseGate.BoardApprovalCapacity, approved.ApprovalCapacity);
        // FilingWorkflowService moves the package to Approved after binding the approval.
        approved.FilingStatus = FilingStatus.Approved;
        await db.SaveChangesAsync();
        var service = new DirectorsApprovalService(db, new FakePageRenderer(), new AuditService(db));

        await service.WithdrawAsync(harness.CompanyId, harness.PeriodId, Withdrawal("board-rescinded"), Owner);

        var cro = await db.CroFilingPackages.AsNoTracking().SingleAsync(c => c.PeriodId == harness.PeriodId);
        Assert.Null(cro.ApprovedBy);
        Assert.Null(cro.ApprovedAt);
        Assert.Null(cro.ApprovalCapacity);
        Assert.Null(cro.ApprovedArtifactManifestSha256);
        Assert.Equal(FilingStatus.PackageGenerated, cro.FilingStatus);
        // The accounts themselves are unchanged; only the approvals that relied on the board are revoked.
        Assert.Equal(approved.AccountsPdfSha256, cro.AccountsPdfSha256);
        Assert.Null(await service.GetCurrentApprovalAsync(harness.PeriodId));
        var log = Assert.Single(db.AuditLogs.Where(a => a.Action == AuditEventCodes.DirectorsApprovalRevoked));
        Assert.Contains("CRO", log.NewValueJson);
    }

    [Theory]
    [InlineData(true, true, FilingStatus.PackageGenerated)]
    [InlineData(true, false, FilingStatus.InProgress)]
    [InlineData(false, false, FilingStatus.InProgress)]
    public void RevokeBoundApprovals_RevokesTheRevenueAndCharityApprovalsAndResetsTheirStatus(
        bool sofaGenerated, bool trusteesReportGenerated, FilingStatus expectedCharityStatus)
    {
        var approvedAt = new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);
        var revenue = new RevenueFilingPackage
        {
            FilingStatus = FilingStatus.Approved,
            ApprovedBy = "Board of directors",
            ApprovedAt = approvedAt,
            ApprovedArtifactManifestSha256 = new string('a', 64),
            ApprovedReleaseCandidate = "candidate-1",
            ApproverTenantId = 1,
            ApprovalScope = "revenue",
            ApprovalCapacity = FilingReleaseGate.BoardApprovalCapacity,
            ApprovalDecision = "approved",
            ApproverVerificationReference = "board-minute-1",
            ApproverVerifiedAt = approvedAt,
            ApproverCredentialValidUntil = approvedAt.AddYears(1),
            Ct1Reference = "CT1-EXAMPLE-1"
        };
        var charity = new CharityFilingPackage
        {
            FilingStatus = FilingStatus.Approved,
            SofaGenerated = sofaGenerated,
            TrusteesReportGenerated = trusteesReportGenerated,
            ApprovedBy = "Board of directors",
            ApprovedAt = approvedAt,
            ApprovedArtifactManifestSha256 = new string('b', 64),
            ApprovedReleaseCandidate = "candidate-1",
            ApproverTenantId = 1,
            ApprovalScope = "charity",
            ApprovalCapacity = FilingReleaseGate.BoardApprovalCapacity,
            ApprovalDecision = "approved",
            ApproverVerificationReference = "board-minute-1",
            ApproverVerifiedAt = approvedAt,
            ApproverCredentialValidUntil = approvedAt.AddYears(1)
        };
        var period = new AccountingPeriod { RevenueFilingPackage = revenue, CharityFilingPackage = charity };

        var revoked = FilingReleaseGate.RevokeApprovalsBoundToWithdrawnBoardApproval(period);

        Assert.Equal(["Revenue", "Charity"], revoked);
        Assert.Equal(FilingStatus.InProgress, revenue.FilingStatus);
        Assert.Null(revenue.ApprovedBy);
        Assert.Null(revenue.ApprovedAt);
        Assert.Null(revenue.ApprovedArtifactManifestSha256);
        Assert.Null(revenue.ApprovedReleaseCandidate);
        Assert.Null(revenue.ApproverTenantId);
        Assert.Null(revenue.ApprovalScope);
        Assert.Null(revenue.ApprovalCapacity);
        Assert.Null(revenue.ApprovalDecision);
        Assert.Null(revenue.ApproverVerificationReference);
        Assert.Null(revenue.ApproverVerifiedAt);
        Assert.Null(revenue.ApproverCredentialValidUntil);
        Assert.Null(revenue.Ct1Reference);
        Assert.Equal(expectedCharityStatus, charity.FilingStatus);
        Assert.Null(charity.ApprovedBy);
        Assert.Null(charity.ApprovedAt);
        Assert.Null(charity.ApprovedArtifactManifestSha256);
        Assert.Null(charity.ApprovedReleaseCandidate);
        Assert.Null(charity.ApproverTenantId);
        Assert.Null(charity.ApprovalScope);
        Assert.Null(charity.ApprovalCapacity);
        Assert.Null(charity.ApprovalDecision);
        Assert.Null(charity.ApproverVerificationReference);
        Assert.Null(charity.ApproverVerifiedAt);
        Assert.Null(charity.ApproverCredentialValidUntil);
    }

    [Fact]
    public void RevokeBoundApprovals_LeavesRevenueAndCharityApprovalsUnderAnotherCapacity()
    {
        var revenue = new RevenueFilingPackage
        {
            FilingStatus = FilingStatus.Approved,
            ApprovedBy = "Tax agent",
            ApprovalCapacity = "tax-agent"
        };
        var charity = new CharityFilingPackage
        {
            FilingStatus = FilingStatus.Approved,
            ApprovedBy = "Charity trustee",
            ApprovalCapacity = "charity-trustee"
        };
        var period = new AccountingPeriod { RevenueFilingPackage = revenue, CharityFilingPackage = charity };

        Assert.Empty(FilingReleaseGate.RevokeApprovalsBoundToWithdrawnBoardApproval(period));

        Assert.Equal((FilingStatus.Approved, "Tax agent", "tax-agent"), (revenue.FilingStatus, revenue.ApprovedBy, revenue.ApprovalCapacity));
        Assert.Equal((FilingStatus.Approved, "Charity trustee", "charity-trustee"), (charity.FilingStatus, charity.ApprovedBy, charity.ApprovalCapacity));
    }

    [Theory]
    [InlineData("Revenue", FilingStatus.Submitted)]
    [InlineData("Revenue", FilingStatus.Accepted)]
    [InlineData("Charity", FilingStatus.Submitted)]
    [InlineData("Charity", FilingStatus.Accepted)]
    public void RevokeBoundApprovals_IsRefusedOnceARevenueOrCharityFilingIsSubmitted_AndChangesNothing(string filed, FilingStatus status)
    {
        var revenue = new RevenueFilingPackage
        {
            FilingStatus = filed == "Revenue" ? status : FilingStatus.Approved,
            ApprovedBy = "Board of directors",
            ApprovalCapacity = FilingReleaseGate.BoardApprovalCapacity
        };
        var charity = new CharityFilingPackage
        {
            FilingStatus = filed == "Charity" ? status : FilingStatus.Approved,
            ApprovedBy = "Board of directors",
            ApprovalCapacity = FilingReleaseGate.BoardApprovalCapacity
        };
        var period = new AccountingPeriod { RevenueFilingPackage = revenue, CharityFilingPackage = charity };

        var error = Assert.Throws<BusinessRuleException>(() => FilingReleaseGate.RevokeApprovalsBoundToWithdrawnBoardApproval(period));

        Assert.StartsWith($"The {filed} filing is recorded as submitted or accepted", error.Message, StringComparison.Ordinal);
        Assert.Equal(FilingReleaseGate.BoardApprovalCapacity, revenue.ApprovalCapacity);
        Assert.Equal(FilingReleaseGate.BoardApprovalCapacity, charity.ApprovalCapacity);
        Assert.Equal("Board of directors", revenue.ApprovedBy);
        Assert.Equal("Board of directors", charity.ApprovedBy);
    }

    [Fact]
    public async Task Withdraw_AfterTheCroFilingWasRecordedAsSubmitted_IsRefused()
    {
        var harness = await FilingGateFixture.ReadyForApprovalAsync();
        var db = harness.Db;
        await harness.RecordDirectorsApprovalAsync();
        await harness.Gate.BindApprovalAsync(harness.CompanyId, harness.PeriodId, FilingReleaseWorkflow.Cro, "user:1");
        var package = await db.CroFilingPackages.SingleAsync(c => c.PeriodId == harness.PeriodId);
        package.FilingStatus = FilingStatus.Submitted;
        await db.SaveChangesAsync();
        var service = new DirectorsApprovalService(db, new FakePageRenderer(), new AuditService(db));

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.WithdrawAsync(harness.CompanyId, harness.PeriodId, Withdrawal(), Owner));

        Assert.Contains("submitted", error.Message);
        Assert.NotNull(await service.GetCurrentApprovalAsync(harness.PeriodId));
        Assert.Empty(db.DirectorsApprovalEvents.Where(e => e.Kind == DirectorsApprovalEventKind.Revoked));
        Assert.NotNull((await db.CroFilingPackages.AsNoTracking().SingleAsync(c => c.PeriodId == harness.PeriodId)).ApprovedBy);
    }

    [Fact]
    public async Task AfterWithdrawal_ANewPageAndApprovalBecomeCurrent()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var service = Service(db);
        await ApproveAsync(db, p);
        var revoked = await service.WithdrawAsync(p.CompanyId, p.Id, Withdrawal("superseded"), Owner);

        var page = await service.GeneratePageAsync(p.CompanyId, p.Id,
            new(new DateOnly(2026, 11, 20), p.FirstDirectorId, p.ThirdDirectorId, "WR-2026-11-20-b"), Owner);
        var reApproval = await service.RecordApprovalAsync(p.CompanyId, p.Id, RecordInput(page.Id, Pdf("signed again"), Pdf("minute again")), Owner);

        var current = await service.GetCurrentApprovalAsync(p.Id);
        Assert.NotNull(current);
        Assert.Equal(reApproval.Id, current!.Approval.Id);
        Assert.Equal(revoked.RecordSha256, reApproval.PreviousRecordSha256);
    }

    [Fact]
    public async Task StateEndpoint_History_ListsTheVerifiedEventsNewestFirst()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var service = Service(db);
        var context = EndpointContext("Client", "GET", FilingPath(p.CompanyId, p.Id, "directors-approval"), p.CompanyId);

        var empty = Value<DirectorsApprovalStateResponse>(
            await DirectorsApprovalEndpoints.GetStateEndpointAsync(p.CompanyId, p.Id, null, service, db, context));
        Assert.Empty(empty.History);

        var page = await ApproveAsync(db, p);
        await service.WithdrawAsync(p.CompanyId, p.Id, Withdrawal("board-rescinded"), Owner with { DisplayName = "Firm Reviewer" });

        var state = Value<DirectorsApprovalStateResponse>(
            await DirectorsApprovalEndpoints.GetStateEndpointAsync(p.CompanyId, p.Id, null, service, db, context));
        Assert.Null(state.Current);
        Assert.Equal(["Revoked", "Approved"], state.History.Select(h => h.Kind).ToArray());
        var events = await db.DirectorsApprovalEvents.AsNoTracking().OrderByDescending(e => e.Id).ToListAsync();
        Assert.Equal(events.Select(e => e.Id), state.History.Select(h => h.EventId));
        Assert.Equal(events.Select(e => e.RecordSha256), state.History.Select(h => h.RecordSha256));
        Assert.Equal("withdrawn-board-rescinded", state.History[0].Reason);
        Assert.Null(state.History[1].Reason);
        Assert.All(state.History, h => Assert.Equal(page.ApprovalDate, h.ApprovalDate));
        Assert.Equal(AuthenticatedIdentity.ReviewerDisplayName(Owner with { DisplayName = "Firm Reviewer" }), state.History[0].RecordedByDisplayName);
    }

    [Fact]
    public async Task StateEndpoint_History_IsEmptyWhenTheChainIsBroken()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var service = Service(db);
        var page = await ApproveAsync(db, p);
        // A self-consistent event that does not chain to the previous one (a removed or reordered event).
        var forged = new DirectorsApprovalEvent
        {
            PeriodId = p.Id,
            Kind = DirectorsApprovalEventKind.Revoked,
            ApprovalPageId = page.Id,
            ApprovalPageSha256 = page.PageSha256,
            RevocationReason = "withdrawn-entered-in-error",
            RecordedByUserId = "user:1",
            RecordedByDisplayName = "Firm Owner",
            RecordedAtUtc = new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc),
            PreviousRecordSha256 = new string('0', 64),
            RecordSha256 = string.Empty
        };
        forged.RecordSha256 = DirectorsApprovalIntegrity.ComputeEventHash(forged);
        db.DirectorsApprovalEvents.Add(forged);
        await db.SaveChangesAsync();

        var state = Value<DirectorsApprovalStateResponse>(await DirectorsApprovalEndpoints.GetStateEndpointAsync(
            p.CompanyId, p.Id, null, service, db,
            EndpointContext("Owner", "GET", FilingPath(p.CompanyId, p.Id, "directors-approval"), p.CompanyId)));

        Assert.Empty(state.History);
        Assert.Null(state.Current);
    }

    [Fact]
    public void HistoryItem_SerialisesTheNullReasonExplicitly()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(
            new DirectorsApprovalHistoryItem(1, "Approved", new DateOnly(2026, 11, 20), null, "Firm Owner",
                new DateTime(2026, 11, 21, 9, 0, 0, DateTimeKind.Utc), new string('a', 64)),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
        Assert.Contains("\"reason\":null", json);
    }

    [Fact]
    public async Task WithdrawEndpoint_Reviewer_WithdrawsAndReturnsTheRevocation()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        await ApproveAsync(db, p);
        var service = Service(db);
        var context = EndpointContext("Reviewer", "POST", FilingPath(p.CompanyId, p.Id, "directors-approval-withdrawal"), p.CompanyId);

        var result = await DirectorsApprovalEndpoints.WithdrawApprovalEndpointAsync(
            p.CompanyId, p.Id, Withdrawal("entered-in-error"), service, db, context, NoApiKey());

        Assert.Equal(200, Status(result));
        var item = Value<DirectorsApprovalHistoryItem>(result);
        Assert.Equal("Revoked", item.Kind);
        Assert.Equal("withdrawn-entered-in-error", item.Reason);
        Assert.Equal(AuthenticatedIdentity.ReviewerDisplayName(AuthContext.RequireUser(context)), item.RecordedByDisplayName);
        Assert.Null(await service.GetCurrentApprovalAsync(p.Id));
    }

    [Theory]
    [InlineData("Client")]
    [InlineData("Accountant")]
    public async Task WithdrawEndpoint_IsDeniedBeforeAnyRowIsWritten(string role)
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        await ApproveAsync(db, p);
        var service = Service(db);
        var context = EndpointContext(role, "POST", FilingPath(p.CompanyId, p.Id, "directors-approval-withdrawal"), p.CompanyId);

        var result = await DirectorsApprovalEndpoints.WithdrawApprovalEndpointAsync(
            p.CompanyId, p.Id, Withdrawal(), service, db, context, NoApiKey());

        Assert.Equal(403, Status(result));
        Assert.Empty(await db.DirectorsApprovalEvents.Where(e => e.Kind == DirectorsApprovalEventKind.Revoked).ToListAsync());
        Assert.NotNull(await service.GetCurrentApprovalAsync(p.Id));
    }

    [Fact]
    public async Task WithdrawEndpoint_ForAnotherCompanysPeriod_IsNotFound()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        await ApproveAsync(db, p);
        var context = EndpointContext("Owner", "POST", FilingPath(p.CompanyId, p.Id + 99, "directors-approval-withdrawal"), p.CompanyId);

        var result = await DirectorsApprovalEndpoints.WithdrawApprovalEndpointAsync(
            p.CompanyId, p.Id + 99, Withdrawal(), Service(db), db, context, NoApiKey());

        Assert.Equal(404, Status(result));
        Assert.Empty(await db.DirectorsApprovalEvents.Where(e => e.Kind == DirectorsApprovalEventKind.Revoked).ToListAsync());
    }
}
