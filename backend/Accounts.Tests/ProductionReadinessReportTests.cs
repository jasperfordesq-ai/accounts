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
        Assert.Contains(snapshot.Sources, s => s.SourceId == "cro-medium-company" && s.EffectiveDate == new DateOnly(2026, 7, 4));
        Assert.Contains(snapshot.Sources, s => s.SourceId == "cro-auditors-report" && s.EffectiveDate == new DateOnly(2026, 7, 4));
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
    public async Task GoldenCorpusScenarios_ExposeFormalEvidencePacksForArtifactsGatesValuesAndSources()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();

        AssertGoldenEvidencePack(
            report,
            "micro-ltd",
            expectedArtifacts:
            [
                "accounts PDF text",
                "CRO filing pack",
                "CRO signature page",
                "iXBRL XML",
                "tax computation",
                "notes disclosure set",
                "filing readiness profile"
            ],
            expectedGates:
            [
                "named qualified-accountant review",
                "director and secretary certification",
                "external ROS/iXBRL validation"
            ],
            expectedValueChecks:
            [
                "Micro regime",
                "100% filing readiness",
                "well-formed iXBRL"
            ],
            expectedSourceIds:
            [
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements.SourceId,
                IrishStatutoryRuleSources.FrcFrs105.SourceId,
                IrishStatutoryRuleSources.RevenueAcceptedTaxonomies.SourceId
            ]);

        AssertGoldenEvidencePack(
            report,
            "small-abridged-ltd",
            expectedArtifacts:
            [
                "full accounts PDF text",
                "abridged CRO filing pack",
                "CRO signature page",
                "iXBRL XML",
                "tax computation",
                "notes disclosure set",
                "filing readiness profile"
            ],
            expectedGates:
            [
                "abridgement eligibility",
                "director and secretary certification",
                "external ROS/iXBRL validation"
            ],
            expectedValueChecks:
            [
                "SmallAbridged regime",
                "Section 352 wording",
                "public P&L turnover omitted from iXBRL"
            ],
            expectedSourceIds:
            [
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements.SourceId,
                IrishStatutoryRuleSources.FrcFrs102.SourceId,
                IrishStatutoryRuleSources.RevenueAcceptedTaxonomies.SourceId
            ]);

        AssertGoldenEvidencePack(
            report,
            "clg-charity",
            expectedArtifacts:
            [
                "CLG accounts PDF text",
                "charity readiness profile",
                "SoFA evidence",
                "trustees annual report evidence",
                "iXBRL XML"
            ],
            expectedGates:
            [
                "charity number",
                "charity annual return review",
                "named qualified-accountant review"
            ],
            expectedValueChecks:
            [
                "charity evidence satisfied",
                "Charities Regulator source attached"
            ],
            expectedSourceIds:
            [
                IrishStatutoryRuleSources.CroGuaranteeCompany.SourceId,
                IrishStatutoryRuleSources.CharitiesRegulatorAnnualReport.SourceId,
                IrishStatutoryRuleSources.FrcFrs102.SourceId
            ]);

        AssertGoldenEvidencePack(
            report,
            "medium-audit-required",
            expectedArtifacts:
            [
                "full accounts PDF text",
                "auditor report evidence",
                "cash flow statement",
                "statement of changes in equity",
                "iXBRL XML",
                "filing readiness profile"
            ],
            expectedGates:
            [
                "auditor handoff",
                "manual professional review",
                "normal CRO approval blocked until auditor evidence"
            ],
            expectedValueChecks:
            [
                "Medium regime",
                "audit report blocker",
                "tagged P&L facts"
            ],
            expectedSourceIds:
            [
                IrishStatutoryRuleSources.CroMediumCompany.SourceId,
                IrishStatutoryRuleSources.CroAuditorsReport.SourceId,
                IrishStatutoryRuleSources.FrcFrs102.SourceId
            ]);
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

    [Fact]
    public async Task ProductionReadinessReport_DeclaresProductionAuditabilityControls()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();
        var auditabilityProperty = report.GetType().GetProperty("AuditabilityControls");

        Assert.NotNull(auditabilityProperty);
        var controls = Assert.IsAssignableFrom<System.Collections.IEnumerable>(auditabilityProperty!.GetValue(report))
            .Cast<object>()
            .ToArray();

        Assert.Contains(controls, control =>
            StringProperty(control, "Code") == "who-changed-what"
            && StringProperty(control, "Enforcement") == "audit-log-integrity-chain"
            && StringListProperty(control, "AuditEventCodes").Contains(AuditEventCodes.AdjustmentUpdated)
            && StringProperty(control, "EvidenceCaptured").Contains("old/new value snapshots", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(controls, control =>
            StringProperty(control, "Code") == "who-approved-what"
            && StringListProperty(control, "AuditEventCodes").Contains(AuditEventCodes.AdjustmentApproved)
            && StringListProperty(control, "AuditEventCodes").Contains(AuditEventCodes.CroFilingStatusChanged)
            && StringProperty(control, "EvidenceCaptured").Contains("named reviewer", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(controls, control =>
            StringProperty(control, "Code") == "what-was-generated"
            && StringListProperty(control, "AuditEventCodes").Contains(AuditEventCodes.CroDocumentGenerated)
            && StringListProperty(control, "AuditEventCodes").Contains(AuditEventCodes.IxbrlInternalCheckCompleted)
            && StringListProperty(control, "AuditEventCodes").Contains(AuditEventCodes.NotesGenerated));
        Assert.Contains(controls, control =>
            StringProperty(control, "Code") == "tamper-evident-chain"
            && BooleanProperty(control, "Required")
            && StringProperty(control, "Enforcement").Contains("checkpoint", StringComparison.OrdinalIgnoreCase));
        Assert.All(controls, control =>
        {
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(control, "Label")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(control, "EvidenceCaptured")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(control, "Verification")));
        });
        Assert.Equal(
            controls.Length,
            controls.Select(control => StringProperty(control, "Code")).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task ProductionReadinessReport_IncludesSourceBackedStatutoryRulesMatrix()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();

        Assert.Contains(report.StatutoryRuleMatrix, row =>
            row.Code == "ltd-micro"
            && row.CompanyScope.Contains("LTD", StringComparison.OrdinalIgnoreCase)
            && row.SizeOrRegime.Contains("Micro", StringComparison.OrdinalIgnoreCase)
            && row.SupportLevel == "supported");
        Assert.Contains(report.StatutoryRuleMatrix, row =>
            row.Code == "ltd-small-abridged"
            && row.RequiredOutputs.Any(output => output.Contains("abridged", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(report.StatutoryRuleMatrix, row =>
            row.Code == "ltd-small-full"
            && row.CompanyScope.Contains("LTD", StringComparison.OrdinalIgnoreCase)
            && row.SizeOrRegime.Contains("Small", StringComparison.OrdinalIgnoreCase)
            && !row.SizeOrRegime.Contains("abridged", StringComparison.OrdinalIgnoreCase)
            && row.RequiredOutputs.Any(output => output.Contains("full small-company financial statements", StringComparison.OrdinalIgnoreCase))
            && row.ManualHandoffGates.Any(gate => gate.Contains("abridgement", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(report.StatutoryRuleMatrix, row =>
            row.Code == "dac-small"
            && row.CompanyScope.Contains("DAC", StringComparison.OrdinalIgnoreCase)
            && row.SupportLevel == "supported");
        Assert.Contains(report.StatutoryRuleMatrix, row =>
            row.Code == "clg-non-charity"
            && row.CompanyScope.Contains("CLG non-charity", StringComparison.OrdinalIgnoreCase)
            && row.SupportLevel == "supported"
            && row.RequiredEvidence.Any(evidence => evidence.Contains("guarantee", StringComparison.OrdinalIgnoreCase))
            && row.Sources.Any(source => source.SourceId == "cro-guarantee-company"));
        Assert.Contains(report.StatutoryRuleMatrix, row =>
            row.Code == "clg-charity"
            && row.CompanyScope.Contains("CLG charity", StringComparison.OrdinalIgnoreCase)
            && row.ManualHandoffGates.Any(gate => gate.Contains("charity annual return", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(report.StatutoryRuleMatrix, row =>
            row.Code == "medium-audit-required"
            && row.SupportLevel == "manual-handoff"
            && row.RequiredEvidence.Any(evidence => evidence.Contains("auditor", StringComparison.OrdinalIgnoreCase)));
        var medium = Assert.Single(report.StatutoryRuleMatrix, row => row.Code == "medium-audit-required");
        Assert.Contains(medium.Sources, source => source.SourceId == "cro-medium-company");
        Assert.Contains(medium.Sources, source => source.SourceId == "cro-auditors-report");
        Assert.Contains(medium.RequiredOutputs, output => output.Contains("auditor", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.StatutoryRuleMatrix, row =>
            row.Code == "unsupported-regulated-group"
            && row.SupportLevel == "unsupported"
            && row.ManualHandoffGates.Any(gate => gate.Contains("fail closed", StringComparison.OrdinalIgnoreCase)));

        Assert.All(report.StatutoryRuleMatrix, row =>
        {
            Assert.NotEmpty(row.RequiredEvidence);
            Assert.NotEmpty(row.RequiredOutputs);
            Assert.NotEmpty(row.ManualHandoffGates);
            Assert.NotEmpty(row.Sources);
            Assert.All(row.Sources, source => Assert.StartsWith("https://", source.Url));
        });
    }

    [Fact]
    public async Task ProductionReadinessReport_DeclaresVisualQaCoverageForAccountantWorkbenchRoutes()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();

        Assert.Equal("visual-smoke-screenshots", report.VisualQaCoverage.ArtifactName);
        Assert.Equal("ci-production-smoke", report.VisualQaCoverage.Enforcement);
        Assert.Equal(20, report.VisualQaCoverage.ExpectedScreenshotCount);
        Assert.Equal(["light", "dark"], report.VisualQaCoverage.Themes);
        Assert.Contains(report.VisualQaCoverage.Viewports, viewport =>
            viewport.Name == "desktop" && viewport.Width == 1440 && viewport.Height == 1000);
        Assert.Contains(report.VisualQaCoverage.Viewports, viewport =>
            viewport.Name == "mobile" && viewport.Width == 390 && viewport.Height == 844);
        Assert.Contains(report.VisualQaCoverage.Routes, route =>
            route.Code == "dashboard" && route.RequiredText == "Production Readiness");
        Assert.Contains(report.VisualQaCoverage.Routes, route =>
            route.Code == "company-detail" && route.RequiredText == "Accounting Periods");
        Assert.Contains(report.VisualQaCoverage.Routes, route =>
            route.Code == "period-workspace" && route.RequiredText == "Filing readiness");
        Assert.Contains(report.VisualQaCoverage.Routes, route =>
            route.Code == "filing-review" && route.OpenFilingTab && route.RequiredText == "Filing readiness profile");
        Assert.All(report.VisualQaCoverage.Routes, route =>
        {
            Assert.False(string.IsNullOrWhiteSpace(route.Label));
            Assert.False(string.IsNullOrWhiteSpace(route.Description));
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

    private static void AssertGoldenEvidencePack(
        ProductionReadinessReport report,
        string scenarioCode,
        IReadOnlyList<string> expectedArtifacts,
        IReadOnlyList<string> expectedGates,
        IReadOnlyList<string> expectedValueChecks,
        IReadOnlyList<string> expectedSourceIds)
    {
        var scenario = Assert.Single(report.GoldenFilingCorpus, s => s.Code == scenarioCode);
        var evidencePackProperty = scenario.GetType().GetProperty("EvidencePack");
        Assert.NotNull(evidencePackProperty);
        var evidencePack = evidencePackProperty!.GetValue(scenario);
        Assert.NotNull(evidencePack);

        AssertListContainsAll(StringListProperty(evidencePack!, "OutputArtifacts"), expectedArtifacts, scenarioCode, "output artifacts");
        AssertListContainsAll(StringListProperty(evidencePack!, "DecisionGates"), expectedGates, scenarioCode, "decision gates");
        AssertListContainsAll(StringListProperty(evidencePack!, "ExpectedValueChecks"), expectedValueChecks, scenarioCode, "expected value checks");

        var sourceReferencesProperty = evidencePack!.GetType().GetProperty("SourceReferences");
        Assert.NotNull(sourceReferencesProperty);
        var sources = Assert.IsAssignableFrom<IEnumerable<LegalSourceReference>>(sourceReferencesProperty!.GetValue(evidencePack)).ToArray();
        foreach (var sourceId in expectedSourceIds)
            Assert.Contains(sources, source => source.SourceId == sourceId);
        Assert.All(sources, source => Assert.StartsWith("https://", source.Url));
    }

    private static void AssertListContainsAll(
        IReadOnlyList<string> actual,
        IReadOnlyList<string> expected,
        string scenarioCode,
        string label)
    {
        foreach (var expectedItem in expected)
        {
            Assert.True(
                actual.Any(item => item.Contains(expectedItem, StringComparison.OrdinalIgnoreCase)),
                $"{scenarioCode} evidence pack {label} should include '{expectedItem}'.");
        }

        Assert.Equal(
            actual.Count,
            actual.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.NotEmpty(actual);
    }

    private static string StringProperty(object value, string propertyName)
    {
        var property = value.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        return Assert.IsType<string>(property!.GetValue(value));
    }

    private static bool BooleanProperty(object value, string propertyName)
    {
        var property = value.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        return Assert.IsType<bool>(property!.GetValue(value));
    }

    private static IReadOnlyList<string> StringListProperty(object value, string propertyName)
    {
        var property = value.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        return Assert.IsAssignableFrom<IEnumerable<string>>(property!.GetValue(value)).ToArray();
    }
}
