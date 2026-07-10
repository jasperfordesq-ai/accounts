using System.Text;
using System.Text.Json;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;
using UglyToad.PdfPig;
using Xunit;

namespace Accounts.Tests;

public sealed class CharitySorpArtifactTests
{
    static CharitySorpArtifactTests() => QuestPDF.Settings.License = LicenseType.Community;

    [Fact]
    public void EffectiveDatedDecision_UsesExact2026Boundaries_AndFailsClosedForUnsupportedPaths()
    {
        var pre2026 = CharitySorpDecisionService.Decide(
            new DateOnly(2025, 12, 31),
            CompanyType.CompanyLimitedByGuarantee,
            "CLG",
            100_000m);
        Assert.Equal(CharitySorpDecisionService.Sorp2019Framework, pre2026.FrameworkCode);
        Assert.Null(pre2026.Tier);
        Assert.True(pre2026.ManualProfessionalHandoffRequired);

        var tier1 = CharitySorpDecisionService.Decide(
            new DateOnly(2026, 1, 1),
            CompanyType.CompanyLimitedByGuarantee,
            "CLG",
            500_000m);
        Assert.Equal(CharitySorpDecisionService.Sorp2026Framework, tier1.FrameworkCode);
        Assert.Equal(1, tier1.Tier);
        Assert.Equal("natural-or-activity", tier1.SofaBasis);
        Assert.True(tier1.AutomatedArtifactsSupported);
        Assert.Equal(CharitySorpDecisionService.Sorp2026DocumentSha256, tier1.Sources.Single().DocumentSha256);

        var tier2Lower = CharitySorpDecisionService.Decide(
            new DateOnly(2026, 1, 1),
            CompanyType.CompanyLimitedByGuarantee,
            "CLG",
            500_000.01m);
        Assert.Equal(2, tier2Lower.Tier);
        Assert.Equal("activity", tier2Lower.SofaBasis);
        Assert.True(tier2Lower.ManualProfessionalHandoffRequired);

        Assert.Equal(2, CharitySorpDecisionService.Determine2026Tier(15_000_000m));
        Assert.Equal(3, CharitySorpDecisionService.Determine2026Tier(15_000_000.01m));

        var nonCompany = CharitySorpDecisionService.Decide(
            new DateOnly(2026, 1, 1),
            CompanyType.Private,
            "Trust",
            10_000m);
        Assert.True(nonCompany.ManualProfessionalHandoffRequired);
        Assert.Contains("Non-company or indeterminate", nonCompany.DecisionReason);
    }

    [Fact]
    public async Task TrusteeReview_IncludesEveryDirectorServingDuringPeriod_AndExcludesFutureOrPastDirectors()
    {
        await using var db = CreateDbContext();
        var period = await SeedCharityPeriodAsync(db, completeGovernanceEvidence: true);
        db.CompanyOfficers.AddRange(
            Director(period.CompanyId, "Whole period", new DateOnly(2024, 1, 1), null),
            Director(period.CompanyId, "Appointed on period end", period.PeriodEnd, null),
            Director(period.CompanyId, "Resigned on period start", new DateOnly(2020, 1, 1), period.PeriodStart),
            Director(period.CompanyId, "Future director", period.PeriodEnd.AddDays(1), null),
            Director(period.CompanyId, "Past director", new DateOnly(2020, 1, 1), period.PeriodStart.AddDays(-1)));
        await db.SaveChangesAsync();

        var service = new CharityReportingService(db);
        var package = await service.RecordTrusteeReviewAsync(
            period.CompanyId,
            period.Id,
            true,
            "BOARD-MINUTE-TRUSTEES-001",
            Encoding.UTF8.GetBytes("signed trustee roster"),
            "Niamh Reviewer");

        using var json = JsonDocument.Parse(package.TrusteePopulationJson!);
        var names = json.RootElement.EnumerateArray()
            .Select(item => item.GetProperty("Name").GetString())
            .ToList();
        Assert.Equal(3, names.Count);
        Assert.Contains("Whole period", names);
        Assert.Contains("Appointed on period end", names);
        Assert.Contains("Resigned on period start", names);
        Assert.DoesNotContain("Future director", names);
        Assert.DoesNotContain("Past director", names);
        Assert.True(package.TrusteeReviewAccepted);
        Assert.Equal(FilingReleaseGate.ComputeSha256(package.TrusteeReviewArtifact!), package.TrusteeReviewArtifactSha256);
    }

    [Fact]
    public async Task ArtifactEvidence_RequiresGovernanceAndExactSofaNetAssetsReconciliation()
    {
        await using var db = CreateDbContext();
        var incomplete = await SeedCharityPeriodAsync(db, completeGovernanceEvidence: false);
        db.CompanyOfficers.Add(Director(incomplete.CompanyId, "Director", incomplete.PeriodStart, null));
        await db.SaveChangesAsync();
        var service = new CharityReportingService(db);
        var package = await service.RecordTrusteeReviewAsync(
            incomplete.CompanyId,
            incomplete.Id,
            true,
            "TRUSTEE-REVIEW",
            Encoding.UTF8.GetBytes("review"),
            "Reviewer");

        var governanceError = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.BuildArtifactEvidenceAsync(incomplete.CompanyId, incomplete.Id, 100m, package));
        Assert.Contains("Governance Code question explicitly", governanceError.Message);

        var info = await db.CharityInfos.SingleAsync(item => item.CompanyId == incomplete.CompanyId);
        var artifact = Encoding.UTF8.GetBytes("governance evidence");
        info.GovernanceCodeCompliant = true;
        info.GovernanceCodeNote = "Reviewed by the board.";
        info.GovernanceEvidenceReference = "GOV-REVIEW";
        info.GovernanceReviewedBy = "Reviewer";
        info.GovernanceReviewedAtUtc = DateTime.UtcNow;
        info.GovernanceEvidenceArtifact = artifact;
        info.GovernanceEvidenceArtifactSha256 = FilingReleaseGate.ComputeSha256(artifact);
        await db.SaveChangesAsync();

        var mismatch = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.BuildArtifactEvidenceAsync(incomplete.CompanyId, incomplete.Id, 99m, package));
        Assert.Contains("must equal balance-sheet net assets", mismatch.Message);

        var evidence = await service.BuildArtifactEvidenceAsync(incomplete.CompanyId, incomplete.Id, 100m, package);
        Assert.True(evidence.Reconciliation.Reconciles);
        Assert.Equal(0m, evidence.Reconciliation.Difference);
    }

    [Fact]
    public async Task RetainedPdfArtifacts_CarryReviewMarkingAndSourceFingerprintChanges()
    {
        await using var db = CreateDbContext();
        var period = await SeedCharityPeriodAsync(db, completeGovernanceEvidence: true);
        db.CompanyOfficers.Add(Director(period.CompanyId, "Serving Trustee", period.PeriodStart, null));
        await db.SaveChangesAsync();
        var service = new CharityReportingService(db);
        var package = await service.RecordTrusteeReviewAsync(
            period.CompanyId,
            period.Id,
            true,
            "BOARD-REVIEW-2026",
            Encoding.UTF8.GetBytes("signed board review"),
            "Qualified Reviewer");
        var evidence = await service.BuildArtifactEvidenceAsync(period.CompanyId, period.Id, 100m, package);
        var pdf = new CharityPdfService();

        var cleanSofa = pdf.GenerateSofa(evidence, reviewCopy: false);
        var reviewSofa = pdf.GenerateSofa(evidence, reviewCopy: true);
        var cleanTar = pdf.GenerateTrusteesAnnualReport(evidence, reviewCopy: false);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(cleanSofa, 0, 5));
        Assert.Contains("STATEMENT OF FINANCIAL ACTIVITIES", ExtractPdfText(cleanSofa));
        Assert.DoesNotContain("NOT APPROVED FOR FINAL USE", ExtractPdfText(cleanSofa));
        Assert.Contains("REVIEW COPY - NOT APPROVED FOR FINAL USE", ExtractPdfText(reviewSofa));
        Assert.Contains("TRUSTEES' ANNUAL REPORT", ExtractPdfText(cleanTar));
        Assert.Contains("Serving Trustee", ExtractPdfText(cleanTar));

        var qaOutput = Environment.GetEnvironmentVariable("CHARITY_PDF_QA_OUTPUT");
        if (!string.IsNullOrWhiteSpace(qaOutput))
        {
            Directory.CreateDirectory(qaOutput);
            await File.WriteAllBytesAsync(Path.Combine(qaOutput, "charity-sofa-clean.pdf"), cleanSofa);
            await File.WriteAllBytesAsync(Path.Combine(qaOutput, "charity-sofa-review.pdf"), reviewSofa);
            await File.WriteAllBytesAsync(Path.Combine(qaOutput, "trustees-annual-report-clean.pdf"), cleanTar);
        }

        var oldFingerprint = evidence.SourceFingerprintSha256;
        var info = await db.CharityInfos.SingleAsync(item => item.CompanyId == period.CompanyId);
        info.CharityNumber = "CHY-CHANGED";
        await db.SaveChangesAsync();
        var changed = await service.BuildArtifactEvidenceAsync(period.CompanyId, period.Id, 100m, package);
        Assert.NotEqual(oldFingerprint, changed.SourceFingerprintSha256);
    }

    private static AccountsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AccountsDbContext(options);
    }

    private static async Task<AccountingPeriod> SeedCharityPeriodAsync(
        AccountsDbContext db,
        bool completeGovernanceEvidence)
    {
        var company = new Company
        {
            LegalName = "Evidence Charity CLG",
            CroNumber = "765431",
            CompanyType = CompanyType.CompanyLimitedByGuarantee,
            IncorporationDate = new DateOnly(2020, 1, 1),
            AnnualReturnDate = new DateOnly(2024, 9, 15),
            IsCharitableOrganisation = true
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var governanceArtifact = completeGovernanceEvidence
            ? Encoding.UTF8.GetBytes("signed governance review")
            : null;
        db.CharityInfos.Add(new CharityInfo
        {
            CompanyId = company.Id,
            CharityNumber = "CHY-765431",
            CharityType = "CLG",
            GrossIncome = 100_000m,
            SorpTier = 1,
            CharitableObjectives = "Community benefit and education.",
            PrincipalActivities = "Training and community services.",
            GovernanceCodeCompliant = completeGovernanceEvidence ? true : null,
            GovernanceCodeNote = completeGovernanceEvidence ? "Board review complete." : null,
            GovernanceEvidenceReference = completeGovernanceEvidence ? "GOV-BOARD-001" : null,
            GovernanceReviewedBy = completeGovernanceEvidence ? "Qualified Reviewer" : null,
            GovernanceReviewedAtUtc = completeGovernanceEvidence ? DateTime.UtcNow : null,
            GovernanceEvidenceArtifact = governanceArtifact,
            GovernanceEvidenceArtifactSha256 = governanceArtifact is null
                ? null
                : FilingReleaseGate.ComputeSha256(governanceArtifact)
        });
        var period = new AccountingPeriod
        {
            CompanyId = company.Id,
            PeriodStart = new DateOnly(2026, 1, 1),
            PeriodEnd = new DateOnly(2026, 12, 31),
            Status = PeriodStatus.Review
        };
        db.AccountingPeriods.Add(period);
        await db.SaveChangesAsync();
        db.FundBalances.Add(new FundBalance
        {
            PeriodId = period.Id,
            FundName = "General fund",
            FundType = "Unrestricted",
            OpeningBalance = 20m,
            IncomingResources = 120m,
            ResourcesExpended = 40m,
            ClosingBalance = 100m
        });
        await db.SaveChangesAsync();
        return period;
    }

    private static CompanyOfficer Director(
        int companyId,
        string name,
        DateOnly appointed,
        DateOnly? resigned) => new()
        {
            CompanyId = companyId,
            Name = name,
            Role = OfficerRole.Director,
            AppointedDate = appointed,
            ResignedDate = resigned
        };

    private static string ExtractPdfText(byte[] content)
    {
        using var stream = new MemoryStream(content);
        using var document = PdfDocument.Open(stream);
        return string.Join("\n", document.GetPages().Select(page => page.Text));
    }
}
