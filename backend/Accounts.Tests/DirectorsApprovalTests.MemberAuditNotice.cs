using Accounts.Api.Endpoints;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Accounts.Tests;

/// <summary>A members' audit notice (s.334) after the CRO pack was approved on an audit-exempt basis.</summary>
public sealed partial class DirectorsApprovalTests
{
    [Theory]
    [InlineData(true)]  // the regime is re-determined by FilingRegimeService
    [InlineData(false)] // the regime cannot be re-determined (no classification fingerprint): the notice still removes the exemption
    public async Task MemberAuditNotice_AfterCroApproval_RequiresAuditRevokesTheApprovalAndAcceptsTheAuditorsReport(bool classified)
    {
        var harness = await FilingGateFixture.ReadyForApprovalAsync(classified: classified);
        var db = harness.Db;
        await harness.RecordDirectorsApprovalAsync();
        await harness.Gate.BindApprovalAsync(harness.CompanyId, harness.PeriodId, FilingReleaseWorkflow.Cro, "user:1");
        Assert.NotNull((await db.CroFilingPackages.SingleAsync(c => c.PeriodId == harness.PeriodId)).ApprovedBy);
        var tenantId = (await db.Companies.SingleAsync(c => c.Id == harness.CompanyId)).TenantId!.Value;
        var audit = new AuditService(db);

        var result = await ClassificationEndpoints.SaveMemberAuditNoticeEndpointAsync(
            harness.CompanyId, harness.PeriodId, new MemberAuditNoticeInput(true, IrishToday()),
            db, audit,
            EndpointContext("Owner", "PUT", $"/api/companies/{harness.CompanyId}/periods/{harness.PeriodId}/member-audit-notice", harness.CompanyId, tenantId),
            NoApiKey(), new FilingRegimeService(db, new DeadlineService(db), audit));

        Assert.Equal(200, Status(result));
        var period = await db.AccountingPeriods.Include(p => p.FilingRegime).Include(p => p.CroFilingPackage)
            .SingleAsync(p => p.Id == harness.PeriodId);
        Assert.True(period.MemberAuditNoticeReceived);
        Assert.False(period.FilingRegime!.AuditExempt);
        Assert.Null(period.CroFilingPackage!.ApprovedBy);
        Assert.Null(period.CroFilingPackage.ApprovedArtifactManifestSha256);
        if (classified)
            Assert.Contains(db.AuditLogs, a => a.Action == AuditEventCodes.FilingRegimeDetermined);

        // The release gate and the auditor-report endpoint now agree that audit is required.
        await Assert.ThrowsAsync<FilingReleaseBlockedException>(() =>
            harness.Gate.BindApprovalAsync(harness.CompanyId, harness.PeriodId, FilingReleaseWorkflow.Cro, "user:1"));
        var recorded = await DirectorsApprovalEndpoints.RecordAuditorReportEndpointAsync(
            harness.CompanyId, harness.PeriodId, AuditorReport(Pdf("signed auditor report")),
            harness.Gate, audit, db,
            EndpointContext("Owner", "POST", FilingPath(harness.CompanyId, harness.PeriodId, "auditor-report"), harness.CompanyId, tenantId),
            NoApiKey(), TimeProvider.System);
        Assert.Equal(200, Status(recorded));
        Assert.True((await db.AccountingPeriods.SingleAsync(p => p.Id == harness.PeriodId)).AuditorsReportReceived);
    }

    [Fact]
    public async Task MemberAuditNotice_WithoutAnApprovedPack_LeavesTheCroPackageUntouched()
    {
        var harness = await FilingGateFixture.ReadyForApprovalAsync(classified: true);
        var db = harness.Db;
        var tenantId = (await db.Companies.SingleAsync(c => c.Id == harness.CompanyId)).TenantId!.Value;
        var before = await db.CroFilingPackages.AsNoTracking().SingleAsync(c => c.PeriodId == harness.PeriodId);

        var result = await ClassificationEndpoints.SaveMemberAuditNoticeEndpointAsync(
            harness.CompanyId, harness.PeriodId, new MemberAuditNoticeInput(true, IrishToday()),
            db, new AuditService(db),
            EndpointContext("Owner", "PUT", $"/api/companies/{harness.CompanyId}/periods/{harness.PeriodId}/member-audit-notice", harness.CompanyId, tenantId),
            NoApiKey(), new FilingRegimeService(db));

        Assert.Equal(200, Status(result));
        var after = await db.CroFilingPackages.AsNoTracking().SingleAsync(c => c.PeriodId == harness.PeriodId);
        Assert.Equal(before.SignedPdfSha256, after.SignedPdfSha256);
        Assert.Equal(before.AccountsPdfSha256, after.AccountsPdfSha256);
        Assert.False((await db.FilingRegimes.SingleAsync(f => f.PeriodId == harness.PeriodId)).AuditExempt);
    }
}
