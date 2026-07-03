using Accounts.Api.Data;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Accounts.Tests;

public class ProductionReadinessReportTests
{
    [Fact]
    public void SourceLawSnapshot_IncludesPinnedEffectiveSourcesAndNoBlankUrls()
    {
        var snapshot = IrishStatutoryRuleSources.BuildSnapshot();

        Assert.Equal(new DateOnly(2026, 7, 3), snapshot.SnapshotDate);
        Assert.Contains(snapshot.Sources, s => s.SourceId == "cro-financial-statements-requirements");
        Assert.Contains(snapshot.Sources, s => s.SourceId == "revenue-accepted-taxonomies" && s.EffectiveDate == new DateOnly(2025, 11, 6));
        Assert.Contains(snapshot.Sources, s => s.SourceId == "frc-frs-102");
        Assert.Contains(snapshot.Sources, s => s.SourceId == "charities-regulator-annual-report");
        Assert.All(snapshot.Sources, source =>
        {
            Assert.StartsWith("https://", source.Url);
            Assert.False(string.IsNullOrWhiteSpace(source.Title));
        });
    }

    [Fact]
    public async Task ProductionReadinessReport_DeclaresGoldenCorpusManualHandoffsAndOperationalGates()
    {
        await using var db = CreateDbContext();
        var service = new ProductionReadinessReportService(db);

        var report = await service.GetReportAsync();

        Assert.Equal("review-required", report.OverallStatus);
        Assert.Contains(report.GoldenFilingCorpus, s => s.Code == "micro-ltd" && s.CoverageStatus == "covered");
        Assert.Contains(report.GoldenFilingCorpus, s => s.Code == "small-abridged-ltd" && s.CoverageStatus == "covered");
        Assert.Contains(report.GoldenFilingCorpus, s => s.Code == "clg-charity" && s.CoverageStatus == "covered");
        Assert.Contains(report.GoldenFilingCorpus, s => s.Code == "medium-audit-required" && s.ExpectedOutcome == "manual-handoff");
        Assert.Contains(report.ManualHandoffPaths, p => p.Contains("PLC", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.OperationalGates, g => g.Code == "qualified-accountant-review" && g.Required);
        Assert.Contains(report.OperationalGates, g => g.Code == "no-direct-cro-ros-submission" && g.Required);
        Assert.Contains(report.Areas, a => a.Code == "backend-accounting-engine" && a.Status == "hardened");
        Assert.Contains(report.SourceLawSnapshot.Sources, s => s.SourceId == IrishStatutoryRuleSources.RevenueAcceptedTaxonomies.SourceId);
    }

    private static AccountsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AccountsDbContext(options);
    }
}
