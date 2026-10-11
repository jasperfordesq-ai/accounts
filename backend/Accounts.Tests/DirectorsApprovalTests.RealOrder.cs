using System.Text;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Accounts.Tests;

/// <summary>
/// The directors' approval in the order the product actually runs: the board date is recorded when
/// the period is finalised, the final accounts PDF is rendered afterwards, and every later
/// "Download CRO filing pack" re-renders and re-records it.
/// </summary>
public sealed partial class DirectorsApprovalTests
{
    private static DateOnly IrishToday() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Dublin")));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RealOrder_FinaliseThenGenerateLater_ApprovalIsRecordedAndSurvivesAnIdenticalReDownload(bool auditExempt)
    {
        await using var db = CreateDb();
        var period = await FilingReleaseGateTests.SeedReleaseReadyPeriodAsync(db, auditRequired: !auditExempt);
        await FilingGateFixture.PrepareBoardAsync(db, period);
        // Finalisation recorded the board approval date three days ago; the accounts are rendered today.
        var boardDay = IrishToday().AddDays(-3);
        period.ApprovalDate = boardDay;
        await NotesDisclosureService.RefreshApprovalNoteAsync(db, period);
        await db.SaveChangesAsync();

        var gate = new FilingReleaseGate(db, FilingGateFixture.Candidate, new AuditService(db));
        if (!auditExempt)
        {
            var opinion = FilingGateFixture.Pdf("ACTUAL SIGNED INDEPENDENT AUDITOR OPINION");
            await gate.RecordVerifiedAuditorReportAsync(period.CompanyId, period.Id, new AuditorReportEvidence(
                "Independent Audit Firm", "Named Statutory Auditor", "Chartered Accountants Ireland",
                "auditor-member-456", "AUDITOR-REPORT-2026-001", DateTime.UtcNow.AddMinutes(-10),
                "Firm Owner", DateTime.UtcNow.AddMinutes(-5), "accepted",
                opinion, FilingReleaseGate.ComputeSha256(opinion)));
        }

        var documents = new DocumentGeneratorService(db, new FinancialStatementsService(db));
        var firstRender = await documents.GenerateCroFilingPackAsync(period.CompanyId, period.Id);
        await gate.RecordCroArtifactAsync(period.CompanyId, period.Id, FilingReleaseArtifact.CroAccountsPdf, firstRender, "reviewer@example.ie");
        await gate.RecordCroArtifactAsync(period.CompanyId, period.Id, FilingReleaseArtifact.CroSignaturePage,
            await documents.GenerateSignaturePageAsync(period.CompanyId, period.Id), "reviewer@example.ie");
        var cro = await db.CroFilingPackages.AsNoTracking().SingleAsync(c => c.PeriodId == period.Id);
        var generatedAt = cro.AccountsPdfGeneratedAtUtc!.Value;
        var approvedSha = cro.AccountsPdfSha256!;
        Assert.True(DateOnly.FromDateTime(generatedAt) > boardDay, "the accounts PDF is generated after the board date");

        // Generate the s.324 page (real renderer) and record the approval: the board date precedes
        // the PDF's generation time, which is the normal order and must be accepted.
        var approvals = new DirectorsApprovalService(db, documents, new AuditService(db));
        var directors = await approvals.DirectorsInOfficeAsync(period.CompanyId, boardDay);
        var page = await approvals.GeneratePageAsync(period.CompanyId, period.Id,
            new DirectorsApprovalPageInput(boardDay, directors[0].Id, directors[1].Id, $"WR-{boardDay:yyyy-MM-dd}-accounts"), Owner);
        Assert.Equal(approvedSha, page.AccountsPdfSha256);
        var signed = FilingGateFixture.Pdf("signed by the directors");
        var minute = FilingGateFixture.Pdf("board minute approving the accounts");
        await approvals.RecordApprovalAsync(period.CompanyId, period.Id, RecordInput(page.Id, signed, minute), Owner);
        Assert.NotNull(await approvals.GetCurrentApprovalAsync(period.Id));

        // A later "Download CRO filing pack" re-renders the final PDF (on a later clock second) and
        // re-records it; identical bytes neither revoke the approval nor restamp the generation time.
        await Task.Delay(1100);
        var secondRender = await documents.GenerateCroFilingPackAsync(period.CompanyId, period.Id);
        Assert.Equal(FilingReleaseGate.ComputeSha256(firstRender), FilingReleaseGate.ComputeSha256(secondRender));
        await gate.RecordCroArtifactAsync(period.CompanyId, period.Id, FilingReleaseArtifact.CroAccountsPdf, secondRender, "reviewer@example.ie");

        Assert.NotNull(await approvals.GetCurrentApprovalAsync(period.Id));
        Assert.Empty(await db.DirectorsApprovalEvents.Where(e => e.Kind == DirectorsApprovalEventKind.Revoked).ToListAsync());
        cro = await db.CroFilingPackages.AsNoTracking().SingleAsync(c => c.PeriodId == period.Id);
        Assert.Equal(approvedSha, cro.AccountsPdfSha256);
        Assert.Equal(generatedAt, cro.AccountsPdfGeneratedAtUtc);
    }

    [Fact]
    public async Task FinalPdfs_AreByteDeterministicAcrossRenders()
    {
        await using var db = CreateDb();
        var period = await FilingReleaseGateTests.SeedReleaseReadyPeriodAsync(db);
        await FilingGateFixture.PrepareBoardAsync(db, period);
        var documents = new DocumentGeneratorService(db, new FinancialStatementsService(db));

        async Task<string[]> RenderAllAsync() =>
        [
            FilingReleaseGate.ComputeSha256(await documents.GenerateCroFilingPackAsync(period.CompanyId, period.Id)),
            FilingReleaseGate.ComputeSha256(await documents.GenerateSignaturePageAsync(period.CompanyId, period.Id)),
            FilingReleaseGate.ComputeSha256(await documents.GenerateAccountsPackageAsync(period.CompanyId, period.Id)),
            FilingReleaseGate.ComputeSha256(await documents.RenderAsync(period.CompanyId, period.Id,
                new DirectorsApprovalPageContent(period.ApprovalDate!.Value, "Director One", "Director Two", "WR-1", new string('a', 64)))),
        ];

        var first = await RenderAllAsync();
        await Task.Delay(1100); // a later clock second: a render-time timestamp would change the bytes
        var second = await RenderAllAsync();

        Assert.Equal(first, second);
    }

    [Fact]
    public void ContentDerivedTrailerId_IsStableLengthPreservingAndLeavesEarlierSectionsAlone()
    {
        static byte[] Pdf(string id)
        {
            const string head = "%PDF-1.7\n1 0 obj\n<< /Embedded (trailer << /ID [<11111111><22222222>] >>) >>\nendobj\n";
            return Encoding.Latin1.GetBytes(head
                + "xref\n0 2\n0000000000 65535 f \n0000000009 00000 n \n"
                + $"trailer << /Size 2 /Root 1 0 R /ID [<{id}><{id}>] >>\nstartxref\n{head.Length}\n%%EOF\n");
        }

        var a = DeterministicPdf.WithContentDerivedTrailerId(Pdf("0123456789abcdef0123456789abcdef"));
        var b = DeterministicPdf.WithContentDerivedTrailerId(Pdf("fedcba9876543210fedcba9876543210"));

        Assert.Equal(a, b);
        Assert.Equal(Pdf("0123456789abcdef0123456789abcdef").Length, a.Length);
        var text = Encoding.Latin1.GetString(a);
        Assert.Contains("/ID [<11111111><22222222>]", text); // the earlier (embedded) /ID is untouched
        Assert.DoesNotContain("0123456789abcdef0123456789abcdef", text);

        var withoutId = Encoding.Latin1.GetBytes("%PDF-1.7\nxref\n0 1\ntrailer << /Size 1 >>\nstartxref\n9\n%%EOF\n");
        Assert.Same(withoutId, DeterministicPdf.WithContentDerivedTrailerId(withoutId));
    }
}
