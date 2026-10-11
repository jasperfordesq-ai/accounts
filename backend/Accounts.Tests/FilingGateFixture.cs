using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Accounts.Tests;

/// <summary>
/// A release-ready period whose CRO accounts, signature page and executed signing pack are retained,
/// with a two-director board whose s.324 approval can be recorded against those exact accounts.
/// </summary>
internal sealed class FilingGateFixture
{
    internal const string Candidate = "candidate-a";

    private static readonly AuthenticatedUser Recorder = new(
        UserId: 1, TenantId: 1, TenantName: "Firm", Email: "owner@example.ie",
        DisplayName: "Firm Owner", Role: "Owner");

    static FilingGateFixture()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private FilingGateFixture(AccountsDbContext db, FilingReleaseGate gate, AccountingPeriod period, byte[] accountsPdf)
    {
        Db = db;
        Gate = gate;
        Period = period;
        AccountsPdf = accountsPdf;
    }

    public AccountsDbContext Db { get; }
    public FilingReleaseGate Gate { get; }
    public AccountingPeriod Period { get; }
    public byte[] AccountsPdf { get; }
    public int CompanyId => Period.CompanyId;
    public int PeriodId => Period.Id;

    /// <param name="auditExempt">False seeds an audit-required regime and retains a signed auditor report.</param>
    /// <param name="withAuditorReport">
    /// False (audit-required only) withdraws the auditor report evidence after the accounts were
    /// generated with it attached, so the accounts the directors approve are unchanged but the
    /// release no longer has an accepted auditor report.
    /// </param>
    /// <param name="classified">
    /// True runs the statutory size classification and filing-regime determination first, so the
    /// regime can be re-determined later (the bare seed has no classification fingerprint).
    /// </param>
    public static async Task<FilingGateFixture> ReadyForApprovalAsync(bool auditExempt = true, bool withAuditorReport = true, bool classified = false)
    {
        var db = DirectorsApprovalTests.CreateDb();
        var period = await FilingReleaseGateTests.SeedReleaseReadyPeriodAsync(db, auditRequired: !auditExempt);
        var gate = new FilingReleaseGate(db, Candidate, new AuditService(db));
        await PrepareBoardAsync(db, period);
        if (classified)
        {
            await new SizeClassificationService(db, Microsoft.Extensions.Options.Options.Create(new Accounts.Api.Rules.SizeThresholdConfig()), new AuditService(db))
                .ClassifyAsync(period.CompanyId, period.Id);
            await new FilingRegimeService(db).DetermineAsync(period.CompanyId, period.Id, ElectedRegime.Micro);
        }

        if (!auditExempt)
        {
            var opinion = Pdf("ACTUAL SIGNED INDEPENDENT AUDITOR OPINION");
            await gate.RecordVerifiedAuditorReportAsync(period.CompanyId, period.Id, new AuditorReportEvidence(
                "Independent Audit Firm", "Named Statutory Auditor", "Chartered Accountants Ireland",
                "auditor-member-456", "AUDITOR-REPORT-2026-001", DateTime.UtcNow.AddMinutes(-10),
                "Firm Owner", DateTime.UtcNow.AddMinutes(-5), "accepted",
                opinion, FilingReleaseGate.ComputeSha256(opinion)));
        }

        var accounts = Pdf("clean-cro-accounts-v1");
        await gate.RecordCroArtifactAsync(period.CompanyId, period.Id, FilingReleaseArtifact.CroAccountsPdf, accounts, "reviewer@example.ie");
        await gate.RecordCroArtifactAsync(period.CompanyId, period.Id, FilingReleaseArtifact.CroSignaturePage, Pdf("clean-cro-signature-v1"), "reviewer@example.ie");
        var signed = Pdf("executed-cro-signing-pack");
        await gate.RecordVerifiedCroSignatureAsync(period.CompanyId, period.Id, new CroSignatureEvidence(
            "Director One", "B Secretary", DateTime.UtcNow.AddMinutes(-2), signed, FilingReleaseGate.ComputeSha256(signed)),
            "reviewer@example.ie");

        if (!auditExempt && !withAuditorReport)
        {
            period.AuditorsReportReceived = false;
            period.AuditorsReportReviewDecision = null;
            await db.SaveChangesAsync();
        }

        var retained = (await db.CroFilingPackages.SingleAsync(c => c.PeriodId == period.Id)).AccountsPdfArtifact!;
        return new FilingGateFixture(db, gate, period, retained);
    }

    public Task RecordDirectorsApprovalAsync() => RecordDirectorsApprovalAsync(Db, CompanyId, PeriodId);

    /// <summary>
    /// Gives the company a two-director board ("Director One", "Director Two") and sets the period's
    /// board approval date (and its approval note). Call before the CRO accounts are generated: both
    /// are inputs to the accounts' source fingerprint.
    /// </summary>
    internal static async Task PrepareBoardAsync(AccountsDbContext db, AccountingPeriod period)
    {
        var directors = await db.CompanyOfficers
            .Where(o => o.CompanyId == period.CompanyId && o.Role == OfficerRole.Director)
            .OrderBy(o => o.Id)
            .ToListAsync();
        if (directors.Count == 0)
        {
            db.CompanyOfficers.Add(new CompanyOfficer { CompanyId = period.CompanyId, Name = "Director One", Role = OfficerRole.Director });
        }
        else
        {
            directors[0].Name = "Director One";
        }
        if (directors.Count < 2)
            db.CompanyOfficers.Add(new CompanyOfficer { CompanyId = period.CompanyId, Name = "Director Two", Role = OfficerRole.Director, AppointedDate = directors.FirstOrDefault()?.AppointedDate });

        period.ApprovalDate = BoardApprovalDate(period);
        await NotesDisclosureService.RefreshApprovalNoteAsync(db, period);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Runs the s.324 page + approval flow for the period's current accounts on the period's approval
    /// date, signed by the first two directors in office (or the sole director).
    /// </summary>
    internal static async Task RecordDirectorsApprovalAsync(AccountsDbContext db, int companyId, int periodId)
    {
        var period = await db.AccountingPeriods.AsNoTracking().SingleAsync(p => p.Id == periodId && p.CompanyId == companyId);
        var approvalDate = period.ApprovalDate
            ?? throw new InvalidOperationException("Call PrepareBoardAsync before generating the accounts.");
        var service = new DirectorsApprovalService(
            db,
            new DirectorsApprovalTests.FakePageRenderer(),
            new AuditService(db),
            new BoardDayClock(approvalDate));
        var directors = await service.DirectorsInOfficeAsync(companyId, approvalDate);
        if (directors.Count == 0)
            throw new InvalidOperationException("No director is in office on the approval date; call PrepareBoardAsync before generating the accounts.");
        var page = await service.GeneratePageAsync(companyId, periodId, new DirectorsApprovalPageInput(
            approvalDate,
            directors[0].Id,
            directors.Count > 1 ? directors[1].Id : null,
            $"WR-{approvalDate:yyyy-MM-dd}-accounts"), Recorder);
        var signed = Pdf("signed by the directors");
        var resolution = Pdf("board minute approving the accounts");
        await service.RecordApprovalAsync(companyId, periodId, new DirectorsApprovalRecordInput(
            page.Id, signed, DirectorsApprovalIntegrity.Sha256(signed), resolution, DirectorsApprovalIntegrity.Sha256(resolution)), Recorder);
    }

    /// <summary>
    /// The board meets on or after the period end and not before the accounts are generated (today);
    /// the approval service's clock is pinned to that day.
    /// </summary>
    private static DateOnly BoardApprovalDate(AccountingPeriod period)
    {
        var irishToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Dublin")));
        return period.PeriodEnd > irishToday ? period.PeriodEnd : irishToday;
    }

    private sealed class BoardDayClock(DateOnly day) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            var noon = new DateTimeOffset(day.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
            var now = DateTimeOffset.UtcNow;
            return now > noon ? now : noon;
        }
    }

    internal static byte[] Pdf(string text) =>
        Document.Create(container => container.Page(page => page.Content().Text(text))).GeneratePdf();
}
