using Accounts.Api.Data;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
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

    [Fact]
    public async Task GoldenCorpusCoverage_IsBackedByConcreteAutomatedEvidenceTests()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();
        var expectedEvidence = new Dictionary<string, string[]>
        {
            ["micro-ltd"] =
            [
                "AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl"
            ],
            ["small-abridged-ltd"] =
            [
                "AccountsWorkflowTests.GoldenPath_SmallAuditExemptCompany_MixedAccrualSetBalancesThroughPdfAndIxbrl"
            ],
            ["clg-charity"] =
            [
                "FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness"
            ],
            ["medium-audit-required"] =
            [
                "FilingGoldenCorpusScenarioTests.GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence"
            ]
        };

        foreach (var (scenarioCode, expectedTests) in expectedEvidence)
        {
            var scenario = Assert.Single(report.GoldenFilingCorpus, s => s.Code == scenarioCode);
            var evidenceProperty = scenario.GetType().GetProperty("EvidenceTestNames");
            Assert.NotNull(evidenceProperty);
            var evidenceTestNames = Assert.IsAssignableFrom<IEnumerable<string>>(evidenceProperty!.GetValue(scenario));

            foreach (var expectedTest in expectedTests)
            {
                Assert.Contains(expectedTest, evidenceTestNames);
                AssertGoldenEvidenceTestExists(expectedTest);
            }
        }
    }

    [Fact]
    public async Task ProductionReadinessReport_ExposesPrioritisedAssuranceActionsForRemainingProductionWork()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();

        Assert.NotEmpty(report.AssuranceActions);
        Assert.Equal("qualified-accountant-signoff", report.AssuranceActions[0].Code);
        Assert.Equal("critical", report.AssuranceActions[0].Priority);
        Assert.Equal("Qualified accountant", report.AssuranceActions[0].Owner);
        Assert.Contains(report.AssuranceActions, action =>
            action.Code == "light-dark-visual-regression"
            && action.Owner == "Engineering"
            && action.Status == "in-progress");
        Assert.Contains(report.AssuranceActions, action =>
            action.Code == "production-monitoring"
            && action.Priority == "high"
            && action.EvidenceRequired.Contains("Sentry", StringComparison.OrdinalIgnoreCase));
        Assert.All(report.AssuranceActions, action =>
        {
            Assert.False(string.IsNullOrWhiteSpace(action.Label));
            Assert.False(string.IsNullOrWhiteSpace(action.Detail));
            Assert.False(string.IsNullOrWhiteSpace(action.EvidenceRequired));
        });
    }

    private static AccountsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AccountsDbContext(options);
    }

    private static void AssertGoldenEvidenceTestExists(string evidenceTestName)
    {
        var separator = evidenceTestName.LastIndexOf('.');
        Assert.True(separator > 0, $"{evidenceTestName} must be formatted as TestClass.TestMethod.");
        var typeName = evidenceTestName[..separator];
        var methodName = evidenceTestName[(separator + 1)..];
        var testType = typeof(ProductionReadinessReportTests).Assembly.GetType($"Accounts.Tests.{typeName}");
        var method = testType?.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(testType);
        Assert.NotNull(method);
        Assert.True(
            method!.GetCustomAttributes(typeof(FactAttribute), inherit: false).Any()
            || method.GetCustomAttributes(typeof(TheoryAttribute), inherit: false).Any(),
            $"{evidenceTestName} must point to an executable xUnit test.");
    }
}
