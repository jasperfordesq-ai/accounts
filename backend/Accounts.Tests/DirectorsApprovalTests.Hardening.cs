using System.Text;
using System.Text.RegularExpressions;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Accounts.Tests;

/// <summary>The real s.324 page render and the smaller hardening rules around the directors' approval.</summary>
public sealed partial class DirectorsApprovalTests
{
    private static string PdfText(byte[] pdf)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(pdf);
        return string.Join(' ', document.GetPages().SelectMany(page => page.GetWords()).Select(word => word.Text));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RealRenderer_ProducesTheSection324Page(bool twoDirectors)
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var renderer = new DocumentGeneratorService(db, new FinancialStatementsService(db));

        var pdf = await renderer.RenderAsync(p.CompanyId, p.Id, new DirectorsApprovalPageContent(
            new DateOnly(2026, 11, 20),
            "Director One",
            twoDirectors ? "Director Two" : null,
            "WR-2026-11-20-accounts",
            new string('a', 64)));

        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5));
        Assert.True(pdf.Length > 1000, $"a rendered page, not a stub ({pdf.Length} bytes)");
        var text = PdfText(pdf);
        Assert.Contains("section 324", text);
        Assert.Contains("Director One, Director", text);
        Assert.Contains("WR-2026-11-20-accounts", text);
        Assert.Contains("Example Charity CLG", text);
        if (twoDirectors)
        {
            Assert.Contains("Director Two, Director", text);
            Assert.DoesNotContain("sole director", text);
        }
        else
        {
            Assert.DoesNotContain("Director Two", text);
            Assert.Contains("sole director", text);
        }
    }

    [Fact]
    public async Task GeneratePage_RejectsADirectorNameLongerThan200Characters()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        (await db.CompanyOfficers.SingleAsync(o => o.Id == p.FirstDirectorId)).Name = new string('N', 201);
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service(db).GeneratePageAsync(
            p.CompanyId, p.Id, new(new DateOnly(2026, 11, 20), p.FirstDirectorId, p.SecondDirectorId, "WR-2026-11-20"), Owner));

        Assert.Contains("longer than 200 characters", error.Message);
        Assert.Empty(await db.DirectorsApprovalPages.ToListAsync());
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("REVIEWER")]
    public async Task RecordApproval_AcceptsRolesCaseInsensitively(string role)
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var service = Service(db);
        var page = await service.GeneratePageAsync(p.CompanyId, p.Id, new(new DateOnly(2026, 11, 20), p.FirstDirectorId, p.SecondDirectorId, "WR-2026-11-20"), Owner);
        var actor = Owner with { Role = role };

        var approval = await service.RecordApprovalAsync(p.CompanyId, p.Id, RecordInput(page.Id, Pdf("signed"), Pdf("minute")), actor);

        Assert.Equal(DirectorsApprovalEventKind.Approved, approval.Kind);
    }

    [Fact]
    public async Task ResolveCurrent_RejectsAPageWhoseStoredBytesNoLongerMatchItsHash()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        await ApproveAsync(db, p);
        var events = await db.DirectorsApprovalEvents.AsNoTracking().ToListAsync();
        var pages = await db.DirectorsApprovalPages.AsNoTracking().ToListAsync();
        var cro = await db.CroFilingPackages.AsNoTracking().SingleAsync(c => c.PeriodId == p.Id);
        Assert.NotNull(DirectorsApprovalService.ResolveCurrent(events, pages, cro));

        // The page record hash covers PageSha256 but not the bytes; the bytes must be re-hashed.
        pages[0].PageArtifact = [.. pages[0].PageArtifact, (byte)'!'];

        Assert.Null(DirectorsApprovalService.ResolveCurrent(events, pages, cro));
    }

    [Fact]
    public async Task ApprovingTheCroPack_ForAnAuditRequiredCompanyWithoutTheReport_SaysTheReportIsMissing()
    {
        var harness = await FilingGateFixture.ReadyForApprovalAsync(auditExempt: false, withAuditorReport: false);
        await harness.RecordDirectorsApprovalAsync();

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            new FilingReadinessProfileService(harness.Db).AssertCanApproveCroPackAsync(harness.CompanyId, harness.PeriodId));

        Assert.Contains("Record the signed auditor's report before approving the CRO pack.", error.Message);
        Assert.DoesNotContain("outside the supported filing path", error.Message);
    }

    [Fact]
    public async Task OptionalAccountantReview_AcceptsCpaIreland()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);

        var package = await Service(db).RecordAccountantReviewAsync(p.CompanyId, p.Id,
            Review(body: "Institute of Certified Public Accountants in Ireland"), Owner);

        Assert.Equal("Institute of Certified Public Accountants in Ireland", package.ApproverProfessionalBody);
    }

    [Fact]
    public void ProfessionalBodies_TheFrontendOffersExactlyTheAcceptedList()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "frontend", "src", "lib", "filingEvidence.ts"));
        var block = Regex.Match(source, @"ACCOUNTANT_PROFESSIONAL_BODIES = \[(?<items>[^\]]*)\]").Groups["items"].Value;
        var offered = Regex.Matches(block, "\"(?<name>[^\"]+)\"").Select(m => m.Groups["name"].Value).ToArray();

        Assert.Equal(AccountantCredentialChecks.RecognisedProfessionalBodies, offered);
        Assert.All(offered, name => Assert.True(AccountantCredentialChecks.IsRecognisedProfessionalBody(name)));
    }

    [Fact]
    public void CharityPackageApprovedBy_IsUnboundedLikeTheCroAndRevenuePackages()
    {
        using var db = CreateDb();
        foreach (var entity in new[] { typeof(CharityFilingPackage), typeof(CroFilingPackage), typeof(RevenueFilingPackage) })
            Assert.Null(db.Model.FindEntityType(entity)!.FindProperty("ApprovedBy")!.GetMaxLength());
    }

    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
