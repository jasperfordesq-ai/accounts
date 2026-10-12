using System.Net;
using System.Net.Http.Json;
using Accounts.Api.Endpoints;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Accounts.Tests;

/// <summary>
/// Post-finalisation filing writes against a finalised, locked period, through the production middleware
/// order and route groups. A write passes the period lock only when its handler or service is designed
/// to act on a finalised period: the s.324 approval page and record, the executed CRO signing pack and
/// the optional accountant review (all bound to the CRO accounts generated after finalisation), and the
/// Revenue and charity filing status (approval and submission need a finalised, locked period). The
/// auditor report, the external Revenue validation and charity report generation stay blocked.
/// </summary>
public sealed partial class DirectorsApprovalTests
{
    public static TheoryData<string, string, bool> PostFinalisationFilingWrites => new()
    {
        { "POST", "directors-approval-page", true },
        { "POST", "directors-approval", true },
        { "POST", "cro-signing-pack", true },
        { "POST", "accountant-review", true },
        { "PUT", "revenue-status", true },
        { "PUT", "charity-status", true },
        { "POST", "auditor-report", false },
        { "POST", "revenue-external-validation", false },
        { "POST", "charity-report-generated", false },
    };

    [Theory]
    [MemberData(nameof(PostFinalisationFilingWrites))]
    public async Task LockedPeriod_PostFinalisationFilingWrite_PassesTheLockOnlyWhenDesignedForAFinalisedPeriod(string method, string action, bool passesLock)
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        await LockAsync(db, p.Id);
        var auditRowsBefore = await db.AuditLogs.CountAsync();

        await using var host = await LockedPeriodHost.StartAsync(db, VerifiedOwner() with { AllowedCompanyIds = new HashSet<int> { p.CompanyId } });
        var response = await SendFilingAsync(host.Client, method, p, action, LockedPeriodBody(action, p));

        Assert.Equal(!passesLock, await IsPeriodLockConflictAsync(response));
        if (!passesLock)
            Assert.Equal(auditRowsBefore, await db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task LockedPeriod_DirectorsApprovalPageRecordAndAccountantReview_ReachTheirHandlers()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        await LockAsync(db, p.Id);
        await using var host = await LockedPeriodHost.StartAsync(db, VerifiedOwner() with { AllowedCompanyIds = new HashSet<int> { p.CompanyId } });

        var page = await SendFilingAsync(host.Client, "POST", p, "directors-approval-page", LockedPeriodBody("directors-approval-page", p));
        Assert.True(page.StatusCode == HttpStatusCode.OK, await page.Content.ReadAsStringAsync());
        var pageId = (await page.Content.ReadFromJsonAsync<DirectorsApprovalPageSummary>())!.Id;

        var approval = await SendFilingAsync(host.Client, "POST", p, "directors-approval", RecordInput(pageId, Pdf("signed"), Pdf("minute")));
        Assert.True(approval.StatusCode == HttpStatusCode.OK, await approval.Content.ReadAsStringAsync());
        Assert.NotNull(await Service(db).GetCurrentApprovalAsync(p.Id));

        var review = await SendFilingAsync(host.Client, "POST", p, "accountant-review", Review());
        Assert.True(review.StatusCode == HttpStatusCode.OK, await review.Content.ReadAsStringAsync());
        Assert.Equal("A. Accountant", (await db.CroFilingPackages.AsNoTracking().SingleAsync(c => c.PeriodId == p.Id)).OptionalReviewerName);
    }

    [Fact]
    public async Task LockedPeriod_CroSigningPack_ReachesItsHandler()
    {
        await using var db = CreateDb();
        var (period, _, directorId, secretaryId, tenantId) = await ReleaseReadyAsync(db);
        Assert.NotNull(period.LockedAt);
        await using var host = await LockedPeriodHost.StartAsync(db,
            VerifiedOwner() with { TenantId = tenantId, AllowedCompanyIds = new HashSet<int> { period.CompanyId } });

        var response = await host.Client.PostAsJsonAsync(FilingPath(period.CompanyId, period.Id, "cro-signing-pack"),
            SigningPack(directorId, secretaryId, DateTime.UtcNow.AddMinutes(-2)));

        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        Assert.NotNull((await db.CroFilingPackages.AsNoTracking().SingleAsync(c => c.PeriodId == period.Id)).SignedPdfSha256);
    }

    [Fact]
    public async Task LockedPeriod_RevenueAndCharityStatus_ReachTheirHandlers_AndTheReleaseGateStillDecides()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        (await db.Companies.SingleAsync(c => c.Id == p.CompanyId)).IsCharitableOrganisation = true;
        db.RevenueFilingPackages.Add(new RevenueFilingPackage { PeriodId = p.Id });
        // The regulator sends back only a return recorded as submitted.
        db.CharityFilingPackages.Add(new CharityFilingPackage { PeriodId = p.Id, FilingStatus = FilingStatus.Submitted, AnnualReturnReference = "AR-EXAMPLE-1" });
        await db.SaveChangesAsync();
        await LockAsync(db, p.Id);
        await using var host = await LockedPeriodHost.StartAsync(db, VerifiedOwner() with { AllowedCompanyIds = new HashSet<int> { p.CompanyId } });

        var revenue = await SendFilingAsync(host.Client, "PUT", p, "revenue-status", LockedPeriodBody("revenue-status", p));
        Assert.True(revenue.StatusCode == HttpStatusCode.OK, await revenue.Content.ReadAsStringAsync());
        Assert.Equal(FilingStatus.CorrectionRequired, (await db.RevenueFilingPackages.AsNoTracking().SingleAsync(r => r.PeriodId == p.Id)).FilingStatus);

        var charity = await SendFilingAsync(host.Client, "PUT", p, "charity-status", LockedPeriodBody("charity-status", p));
        Assert.True(charity.StatusCode == HttpStatusCode.OK, await charity.Content.ReadAsStringAsync());
        Assert.Equal(FilingStatus.CorrectionRequired, (await db.CharityFilingPackages.AsNoTracking().SingleAsync(c => c.PeriodId == p.Id)).FilingStatus);

        // Approval still goes through the release gate's own checks: without the Revenue iXBRL artifact
        // the gate refuses (a release block, not the period lock).
        var approve = await SendFilingAsync(host.Client, "PUT", p, "revenue-status", new RevenueFilingStatusInput(FilingStatus.Approved, null, null));
        Assert.Equal(HttpStatusCode.Conflict, approve.StatusCode);
        Assert.False(await IsPeriodLockConflictAsync(approve));
        Assert.Equal(FilingStatus.CorrectionRequired, (await db.RevenueFilingPackages.AsNoTracking().SingleAsync(r => r.PeriodId == p.Id)).FilingStatus);
    }

    // Passing the lock never bypasses role routing or the recent-MFA gate, which run before it.
    [Theory]
    [InlineData("POST", "directors-approval-page", "Client", HttpStatusCode.Forbidden)]
    [InlineData("POST", "directors-approval", "Accountant", HttpStatusCode.Forbidden)]
    [InlineData("POST", "directors-approval", "Client", HttpStatusCode.Forbidden)]
    [InlineData("POST", "directors-approval", "Owner-without-recent-mfa", (HttpStatusCode)428)]
    [InlineData("POST", "cro-signing-pack", "Accountant", HttpStatusCode.Forbidden)]
    [InlineData("POST", "cro-signing-pack", "Owner-without-recent-mfa", (HttpStatusCode)428)]
    [InlineData("POST", "accountant-review", "Client", HttpStatusCode.Forbidden)]
    [InlineData("PUT", "revenue-status", "Accountant", HttpStatusCode.Forbidden)]
    [InlineData("PUT", "revenue-status", "Client", HttpStatusCode.Forbidden)]
    [InlineData("PUT", "charity-status", "Accountant", HttpStatusCode.Forbidden)]
    [InlineData("PUT", "charity-status", "Client", HttpStatusCode.Forbidden)]
    public async Task LockedPeriod_ThePassedWrites_KeepTheirRoleAndRecentMfaChecks(string method, string action, string actor, HttpStatusCode expected)
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        await LockAsync(db, p.Id);
        var user = (actor == "Owner-without-recent-mfa"
                ? VerifiedOwner() with { MfaVerifiedAtUtc = DateTimeOffset.UtcNow.AddHours(-2) }
                : VerifiedOwner() with { Role = actor })
            with { AllowedCompanyIds = new HashSet<int> { p.CompanyId } };
        var auditRowsBefore = await db.AuditLogs.CountAsync();

        await using var host = await LockedPeriodHost.StartAsync(db, user);
        var response = await SendFilingAsync(host.Client, method, p, action, LockedPeriodBody(action, p));

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(auditRowsBefore, await db.AuditLogs.CountAsync());
    }

    private static object LockedPeriodBody(string action, SeededPeriod p) => action switch
    {
        "directors-approval-page" => new DirectorsApprovalPageInput(new DateOnly(2026, 11, 20), p.FirstDirectorId, p.SecondDirectorId, "WR-2026-11-20"),
        "directors-approval" => RecordInput(0, Pdf("signed"), Pdf("minute")),
        "cro-signing-pack" => SigningPack(p.FirstDirectorId, p.SecretaryId, DateTime.UtcNow.AddMinutes(-2)),
        "accountant-review" => Review(),
        "revenue-status" => new RevenueFilingStatusInput(FilingStatus.CorrectionRequired, "Example correction requested.", null),
        "charity-status" => new CharityFilingStatusInput(FilingStatus.CorrectionRequired, "Example correction requested.", null),
        "auditor-report" => new AuditorReportInput(
            "Example Audit Firm", "Example Auditor", "Chartered Accountants Ireland", "example-member-1",
            "EXAMPLE-AUDIT-REPORT-1", DateTime.UtcNow.AddMinutes(-10), true, Pdf("opinion"), DirectorsApprovalIntegrity.Sha256(Pdf("opinion"))),
        "revenue-external-validation" => new RevenueExternalValidationInput(new string('a', 64), "EXAMPLE-VALIDATION-1"),
        "charity-report-generated" => new CharityReportGeneratedInput("sofa"),
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    private static Task<HttpResponseMessage> SendFilingAsync(HttpClient client, string method, SeededPeriod p, string action, object body) =>
        SendFilingAsync(client, method, FilingPath(p.CompanyId, p.Id, action), body);

    // Each request carries its own idempotency key, as the frontend sends for status commands.
    private static Task<HttpResponseMessage> SendFilingAsync(HttpClient client, string method, string path, object body)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(body, body.GetType()) };
        request.Headers.Add(IdempotencyHttpContract.RequestHeader, $"locked-period-{Guid.NewGuid():N}");
        return client.SendAsync(request);
    }

    /// <summary>The period lock's own refusal (PeriodLockMiddleware writes the accounting-conflict body).</summary>
    private static async Task<bool> IsPeriodLockConflictAsync(HttpResponseMessage response) =>
        response.StatusCode == HttpStatusCode.Conflict
        && (await response.Content.ReadAsStringAsync()).Contains("\"code\":\"accounting_concurrency_conflict\"", StringComparison.Ordinal);
}
