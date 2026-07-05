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
        Assert.Equal(snapshot.Sources.Count, snapshot.SourceCount);
        Assert.Matches("^sha256:[0-9a-f]{64}$", snapshot.ContentHash);
        Assert.Equal(
            IrishStatutoryRuleSources.ComputeContentHash(snapshot.Sources),
            snapshot.ContentHash);
    }

    [Fact]
    public void SourceLawSnapshot_FingerprintIsDeterministicAndChangesWhenPinnedSourceChanges()
    {
        var first = new LegalSourceReference(
            "cro-financial-statements-requirements",
            "CRO financial statements requirements",
            new DateOnly(2026, 7, 3),
            "https://cro.ie/annual-return/financial-statements-requirements/");
        var second = new LegalSourceReference(
            "revenue-accepted-taxonomies",
            "Revenue accepted iXBRL taxonomies",
            new DateOnly(2025, 11, 6),
            "https://www.revenue.ie/en/companies-and-charities/corporation-tax-for-companies/submitting-financial-statements/accepted-taxonomies.aspx");

        var originalHash = IrishStatutoryRuleSources.ComputeContentHash([first, second]);
        var reorderedHash = IrishStatutoryRuleSources.ComputeContentHash([second, first]);
        var changedHash = IrishStatutoryRuleSources.ComputeContentHash([
            first,
            second with { EffectiveDate = new DateOnly(2026, 1, 1) }
        ]);

        Assert.Equal(originalHash, reorderedHash);
        Assert.NotEqual(originalHash, changedHash);
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
    public async Task ProductionReadinessReport_ExposesDeterministicAssurancePacketForReleaseEvidence()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();

        Assert.NotNull(report.AssurancePacket);
        Assert.Equal("production-assurance-packet-v1", report.AssurancePacket.PacketVersion);
        Assert.Matches("^assurance-sha256:[0-9a-f]{64}$", report.AssurancePacket.PacketId);
        Assert.Equal("review-required", report.AssurancePacket.Status);
        Assert.Equal(report.SourceLawSnapshot.ContentHash, report.AssurancePacket.SourceLawSnapshotHash);
        Assert.Equal(report.GoldenFilingCorpus.Count, report.AssurancePacket.GoldenCorpusTotal);
        Assert.Equal(report.GoldenFilingCorpus.Count(scenario => scenario.CoverageStatus == "covered"), report.AssurancePacket.GoldenCorpusCovered);
        Assert.Equal(report.StatutoryRuleMatrix.Count, report.AssurancePacket.StatutoryRuleMatrixPaths);
        Assert.Equal(report.StatutoryRulesCoverage.Count, report.AssurancePacket.StatutoryRuleCoverageFamilies);
        Assert.Equal(report.VisualQaCoverage.ExpectedScreenshotCount, report.AssurancePacket.VisualQaExpectedScreenshots);
        Assert.Equal(report.OperationalGates.Count(gate => gate.Required), report.AssurancePacket.RequiredOperationalGates);
        Assert.Equal(report.AssuranceActions.Count(action => action.Priority == "critical" && action.Status != "complete"), report.AssurancePacket.OpenCriticalActions);
        Assert.Contains("source-law-snapshot-fingerprint", report.AssurancePacket.EvidenceItems);
        Assert.Contains("golden-filing-corpus", report.AssurancePacket.EvidenceItems);
        Assert.Contains("visual-smoke-screenshots", report.AssurancePacket.EvidenceItems);
        Assert.Contains(report.AssurancePacket.ReleaseBlockers, blocker =>
            blocker.Contains("qualified accountant", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            report.AssurancePacket.PacketId,
            ProductionReadinessReportService.ComputeAssurancePacketId(report.AssurancePacket));
    }

    [Fact]
    public async Task ProductionReadinessReport_ExposesSourceLawTraceabilityIndex()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();
        var traceabilityProperty = report.GetType().GetProperty("SourceLawTraceability");

        Assert.NotNull(traceabilityProperty);
        var traceability = Assert.IsAssignableFrom<System.Collections.IEnumerable>(traceabilityProperty!.GetValue(report))
            .Cast<object>()
            .ToArray();
        var snapshotSources = report.SourceLawSnapshot.Sources.ToDictionary(source => source.SourceId);

        Assert.Equal(
            snapshotSources.Keys.Order(StringComparer.Ordinal),
            traceability.Select(entry => StringProperty(entry, "SourceId")).Order(StringComparer.Ordinal));
        Assert.All(traceability, entry =>
        {
            var source = snapshotSources[StringProperty(entry, "SourceId")];
            Assert.True(BooleanProperty(entry, "InSnapshot"));
            Assert.Equal(source.Title, StringProperty(entry, "Title"));
            Assert.Equal(source.Url, StringProperty(entry, "Url"));
            Assert.Equal(source.EffectiveDate.ToString("yyyy-MM-dd"), StringProperty(entry, "EffectiveDate"));
            Assert.NotEmpty(StringListProperty(entry, "UsedBy"));
            Assert.NotEmpty(StringListProperty(entry, "ReleaseGateCodes"));
        });

        Assert.Contains(traceability, entry =>
            StringProperty(entry, "SourceId") == IrishStatutoryRuleSources.CroAuditorsReport.SourceId
            && StringListProperty(entry, "UsedBy").Any(usage => usage == "golden-corpus:medium-audit-required")
            && StringListProperty(entry, "UsedBy").Any(usage => usage == "statutory-rule-matrix:medium-audit-required")
            && StringListProperty(entry, "ReleaseGateCodes").Contains("auditor-handoff"));
        Assert.Contains(traceability, entry =>
            StringProperty(entry, "SourceId") == IrishStatutoryRuleSources.CroUnlimitedCompany.SourceId
            && StringListProperty(entry, "UsedBy").Any(usage => usage.Contains("unsupported", StringComparison.OrdinalIgnoreCase))
            && StringListProperty(entry, "ReleaseGateCodes").Contains("manual-professional-handoff"));
        Assert.Contains(traceability, entry =>
            StringProperty(entry, "SourceId") == IrishStatutoryRuleSources.RevenueAcceptedTaxonomies.SourceId
            && StringListProperty(entry, "ReleaseGateCodes").Contains("external-ros-validation"));
        Assert.Contains(traceability, entry =>
            StringProperty(entry, "SourceId") == IrishStatutoryRuleSources.CharitiesRegulatorAnnualReport.SourceId
            && StringListProperty(entry, "ReleaseGateCodes").Contains("charity-annual-return-review"));
        Assert.Contains(report.AssurancePacket.EvidenceItems, item => item == "source-law-traceability-index");
    }

    [Fact]
    public async Task ProductionReadinessReport_ExposesAuditEvidenceTimelineForWhoWhatWhenReview()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();
        var timelineProperty = report.GetType().GetProperty("AuditEvidenceTimeline");

        Assert.NotNull(timelineProperty);
        var timeline = Assert.IsAssignableFrom<System.Collections.IEnumerable>(timelineProperty!.GetValue(report))
            .Cast<object>()
            .ToArray();

        Assert.Contains(report.AssurancePacket.EvidenceItems, item => item == "audit-evidence-timeline");
        Assert.Contains(timeline, entry =>
            StringProperty(entry, "Code") == "data-change-capture"
            && StringProperty(entry, "EvidenceQuestion").Contains("who changed what", StringComparison.OrdinalIgnoreCase)
            && StringProperty(entry, "CapturedWhen").Contains("write", StringComparison.OrdinalIgnoreCase)
            && StringListProperty(entry, "AuditEventCodes").Contains(AuditEventCodes.SizeClassificationDataSaved));
        Assert.Contains(timeline, entry =>
            StringProperty(entry, "Code") == "generated-output-capture"
            && StringProperty(entry, "EvidenceQuestion").Contains("what was generated", StringComparison.OrdinalIgnoreCase)
            && StringListProperty(entry, "AuditEventCodes").Contains(AuditEventCodes.CroDocumentGenerated));
        Assert.Contains(timeline, entry =>
            StringProperty(entry, "Code") == "accountant-approval-capture"
            && StringProperty(entry, "RequiredActor").Contains("qualified accountant", StringComparison.OrdinalIgnoreCase)
            && StringListProperty(entry, "BlockingGateCodes").Contains("qualified-accountant-review"));
        Assert.Contains(timeline, entry =>
            StringProperty(entry, "Code") == "external-validation-capture"
            && StringProperty(entry, "EvidenceQuestion").Contains("external ROS", StringComparison.OrdinalIgnoreCase)
            && StringListProperty(entry, "BlockingGateCodes").Contains("external-ros-validation"));

        Assert.All(timeline, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(entry, "Code")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(entry, "Stage")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(entry, "EvidenceQuestion")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(entry, "CapturedWhen")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(entry, "RequiredActor")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(entry, "Verification")));
            Assert.NotEmpty(StringListProperty(entry, "AuditEventCodes"));
            Assert.NotEmpty(StringListProperty(entry, "BlockingGateCodes"));
        });
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
    public async Task GoldenCorpusCoverage_ExposesVerifierManifestWithCiScope()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();

        foreach (var scenario in report.GoldenFilingCorpus)
        {
            var verifiers = ObjectListProperty(scenario, "EvidenceVerifiers");

            Assert.Equal(scenario.EvidenceTestNames.Order(StringComparer.Ordinal), verifiers.Select(verifier => StringProperty(verifier, "Name")).Order(StringComparer.Ordinal));
            Assert.NotEmpty(verifiers);
            Assert.All(verifiers, verifier =>
            {
                Assert.False(string.IsNullOrWhiteSpace(StringProperty(verifier, "Name")));
                Assert.Contains(StringProperty(verifier, "Name"), scenario.EvidenceTestNames);
                Assert.Contains("dotnet test Accounts.slnx", StringProperty(verifier, "Command"), StringComparison.Ordinal);
                Assert.Contains(StringProperty(verifier, "Name"), StringProperty(verifier, "Command"), StringComparison.Ordinal);
                Assert.Contains(StringProperty(verifier, "CiScope"), new[] { "default-ci", "environment-gated" });
                Assert.False(string.IsNullOrWhiteSpace(StringProperty(verifier, "Environment")));
                Assert.False(string.IsNullOrWhiteSpace(StringProperty(verifier, "EvidenceLevel")));
            });

            if (scenario.CoverageStatus == "covered")
                Assert.All(verifiers, verifier => Assert.True(BooleanProperty(verifier, "RunsInDefaultCi")));
        }

        Assert.Contains(report.AssurancePacket.EvidenceItems, item => item == "golden-verifier-manifest");
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
    public async Task GoldenCorpusEvidencePacks_ExposeStructuredProofPointsForKnownExpectedOutputs()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();

        foreach (var scenario in report.GoldenFilingCorpus)
        {
            var proofPoints = ObjectListProperty(scenario.EvidencePack, "ExpectedProofPoints");

            Assert.Contains(proofPoints, proof =>
                StringProperty(proof, "Area") == "pdf-text"
                && StringProperty(proof, "ExpectedEvidence").Contains("PDF", StringComparison.OrdinalIgnoreCase)
                && StringProperty(proof, "AutomatedVerifier").Contains(scenario.EvidenceTestNames[0], StringComparison.Ordinal));
            Assert.Contains(proofPoints, proof =>
                StringProperty(proof, "Area") == "ixbrl-xml"
                && StringProperty(proof, "ExpectedEvidence").Contains("well-formed", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(proofPoints, proof =>
                StringProperty(proof, "Area") == "filing-readiness"
                && StringProperty(proof, "ExpectedEvidence").Contains("readiness", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(proofPoints, proof =>
                StringProperty(proof, "Area") == "tax-computation"
                && StringProperty(proof, "ExpectedEvidence").Contains("tax", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(proofPoints, proof =>
                StringProperty(proof, "Area") == "notes-disclosure"
                && StringProperty(proof, "ExpectedEvidence").Contains("note", StringComparison.OrdinalIgnoreCase));

            Assert.All(proofPoints, proof =>
            {
                Assert.False(string.IsNullOrWhiteSpace(StringProperty(proof, "Area")));
                Assert.False(string.IsNullOrWhiteSpace(StringProperty(proof, "ExpectedEvidence")));
                Assert.False(string.IsNullOrWhiteSpace(StringProperty(proof, "AutomatedVerifier")));
                Assert.True(BooleanProperty(proof, "Required"));
            });
        }

        var micro = Assert.Single(report.GoldenFilingCorpus, scenario => scenario.Code == "micro-ltd");
        Assert.Contains(ObjectListProperty(micro.EvidencePack, "ExpectedProofPoints"), proof =>
            StringProperty(proof, "Area") == "signatory-gates"
            && StringProperty(proof, "ExpectedEvidence").Contains("director and secretary", StringComparison.OrdinalIgnoreCase));

        var medium = Assert.Single(report.GoldenFilingCorpus, scenario => scenario.Code == "medium-audit-required");
        Assert.Contains(ObjectListProperty(medium.EvidencePack, "ExpectedProofPoints"), proof =>
            StringProperty(proof, "Area") == "auditor-handoff"
            && StringProperty(proof, "ExpectedEvidence").Contains("signed auditor", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GoldenCorpusEvidencePacks_ProveAccountantSignOffPacketAcrossEveryScenario()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();

        foreach (var scenario in report.GoldenFilingCorpus)
        {
            Assert.Contains(
                scenario.EvidencePack.OutputArtifacts,
                artifact => artifact.Contains("accountant sign-off packet", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(
                scenario.EvidencePack.DecisionGates,
                gate => gate.Contains("sign-off packet", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(scenario.Assertions, assertion =>
                assertion.Contains("sign-off packet", StringComparison.OrdinalIgnoreCase));

            var proofPoints = ObjectListProperty(scenario.EvidencePack, "ExpectedProofPoints");
            Assert.Contains(proofPoints, proof =>
                StringProperty(proof, "Area") == "accountant-signoff-packet"
                && StringProperty(proof, "ExpectedEvidence").Contains("sign-off packet", StringComparison.OrdinalIgnoreCase)
                && StringProperty(proof, "AutomatedVerifier").Contains(scenario.EvidenceTestNames[0], StringComparison.Ordinal));
        }

        var medium = Assert.Single(report.GoldenFilingCorpus, scenario => scenario.Code == "medium-audit-required");
        Assert.Contains(ObjectListProperty(medium.EvidencePack, "ExpectedProofPoints"), proof =>
            StringProperty(proof, "Area") == "accountant-signoff-packet"
            && StringProperty(proof, "ExpectedEvidence").Contains("manual handoff", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GoldenCorpusScenarios_ExposeConcreteFixtureIdentityForAccountantAcceptance()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();

        foreach (var scenario in report.GoldenFilingCorpus)
        {
            var fixtureProperty = scenario.GetType().GetProperty("Fixture");
            Assert.NotNull(fixtureProperty);
            var fixture = fixtureProperty!.GetValue(scenario);
            Assert.NotNull(fixture);

            Assert.False(string.IsNullOrWhiteSpace(StringProperty(fixture!, "LegalName")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(fixture!, "CompanyType")));
            Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", StringProperty(fixture!, "PeriodStart"));
            Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", StringProperty(fixture!, "PeriodEnd"));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(fixture!, "ExpectedSizeClass")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(fixture!, "ExpectedRegime")));
        }

        AssertGoldenFixture(report, "micro-ltd", "Example Micro Limited", "Private", "2025-01-01", "2025-12-31", "Micro", "Micro", auditExempt: true, manualReviewRequired: false);
        AssertGoldenFixture(report, "small-abridged-ltd", "Connacht Digital Solutions Limited", "Private", "2025-01-01", "2025-12-31", "Small", "SmallAbridged", auditExempt: true, manualReviewRequired: false);
        AssertGoldenFixture(report, "clg-charity", "Dublin Community Support CLG", "CompanyLimitedByGuarantee", "2026-01-01", "2026-12-31", "Small", "Small", auditExempt: true, manualReviewRequired: false);
        AssertGoldenFixture(report, "medium-audit-required", "Midlands Manufacturing Limited", "Private", "2026-01-01", "2026-12-31", "Medium", "Medium", auditExempt: false, manualReviewRequired: true);
    }

    [Fact]
    public async Task GoldenCorpusEvidencePacks_ExposeStructuredExpectedOutputValues()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();

        foreach (var scenario in report.GoldenFilingCorpus)
        {
            var expectedOutputs = ObjectProperty(scenario.EvidencePack, "ExpectedOutputs");

            Assert.NotEmpty(StringListProperty(expectedOutputs, "PdfTextMarkers"));
            Assert.NotEmpty(StringListProperty(expectedOutputs, "IxbrlRequiredTags"));
            Assert.NotEmpty(StringListProperty(expectedOutputs, "RequiredNotes"));
            Assert.NotEmpty(StringListProperty(expectedOutputs, "FilingGateStates"));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(expectedOutputs, "FilingReadinessState")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(expectedOutputs, "SignOffPacketState")));
            Assert.True(DecimalProperty(expectedOutputs, "ExpectedCorporationTax") >= 0m);
        }

        AssertGoldenExpectedOutputs(
            report,
            "micro-ltd",
            expectedPdfMarker: "280D",
            expectedIxbrlTag: "core:EntityCurrentLegalOrRegisteredName",
            expectedTax: 718.75m,
            readinessState: "100% filing readiness",
            signOffState: "review-required");
        AssertGoldenExpectedOutputs(
            report,
            "small-abridged-ltd",
            expectedPdfMarker: "Section 352",
            expectedIxbrlTag: "core:EntityCurrentLegalOrRegisteredName",
            expectedTax: 950m,
            readinessState: "generated-output-evidence-required",
            signOffState: "review-required");
        AssertGoldenExpectedOutputs(
            report,
            "clg-charity",
            expectedPdfMarker: "Community support and education.",
            expectedIxbrlTag: "core:EntityCurrentLegalOrRegisteredName",
            expectedTax: 62.50m,
            readinessState: "ready-for-external-filing",
            signOffState: "ready-for-external-filing");
        AssertGoldenExpectedOutputs(
            report,
            "medium-audit-required",
            expectedPdfMarker: "INDEPENDENT AUDITOR'S REPORT",
            expectedIxbrlTag: "core:TurnoverGrossRevenue",
            expectedTax: 62.50m,
            readinessState: "manual-handoff-until-auditor-evidence",
            signOffState: "manual-handoff");
    }

    [Fact]
    public async Task ProductionReadinessReport_ExposesAccountantAcceptanceCriteriaForEveryGoldenScenario()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();
        var acceptanceProperty = report.GetType().GetProperty("AccountantAcceptanceCriteria");

        Assert.NotNull(acceptanceProperty);
        var criteria = Assert.IsAssignableFrom<System.Collections.IEnumerable>(acceptanceProperty!.GetValue(report))
            .Cast<object>()
            .ToArray();

        Assert.Equal(
            report.GoldenFilingCorpus.Select(scenario => scenario.Code).Order(StringComparer.Ordinal),
            criteria.Select(item => StringProperty(item, "ScenarioCode")).Order(StringComparer.Ordinal));
        Assert.Contains(criteria, criterion =>
            StringProperty(criterion, "ScenarioCode") == "micro-ltd"
            && StringListProperty(criterion, "ReviewScope").Any(item => item.Contains("PDF", StringComparison.OrdinalIgnoreCase))
            && StringProperty(criterion, "RequiredSignOffGate").Contains("qualified accountant", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(criteria, criterion =>
            StringProperty(criterion, "ScenarioCode") == "medium-audit-required"
            && StringProperty(criterion, "AcceptanceStatus") == "manual-handoff-review-required"
            && StringListProperty(criterion, "RequiredEvidence").Any(item => item.Contains("auditor", StringComparison.OrdinalIgnoreCase)));
        Assert.All(criteria, criterion =>
        {
            Assert.True(BooleanProperty(criterion, "Required"));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(criterion, "Label")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(criterion, "AcceptanceStatus")));
            Assert.NotEmpty(StringListProperty(criterion, "ReviewScope"));
            Assert.NotEmpty(StringListProperty(criterion, "RequiredEvidence"));
            Assert.NotEmpty(ObjectListProperty(criterion, "Sources"));

            var scenarioCode = StringProperty(criterion, "ScenarioCode");
            var scenario = Assert.Single(report.GoldenFilingCorpus, item => item.Code == scenarioCode);
            var acceptanceVerifiers = ObjectListProperty(criterion, "EvidenceVerifiers");
            Assert.Equal(
                scenario.EvidenceVerifiers.Select(verifier => verifier.Name).Order(StringComparer.Ordinal),
                acceptanceVerifiers.Select(verifier => StringProperty(verifier, "Name")).Order(StringComparer.Ordinal));
            Assert.All(acceptanceVerifiers, verifier =>
            {
                Assert.Contains(StringProperty(verifier, "Name"), scenario.EvidenceTestNames);
                Assert.Contains("dotnet test Accounts.slnx", StringProperty(verifier, "Command"), StringComparison.Ordinal);
                Assert.Contains(StringProperty(verifier, "Name"), StringProperty(verifier, "Command"), StringComparison.Ordinal);
                Assert.True(BooleanProperty(verifier, "RunsInDefaultCi") || StringProperty(verifier, "CiScope") == "environment-gated");
            });
        });
        Assert.Contains(report.AssurancePacket.EvidenceItems, item => item == "accountant-acceptance-criteria");
    }

    [Fact]
    public async Task ProductionReadinessReport_ExposesPrioritisedAssuranceActionsForRemainingProductionWork()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();

        Assert.NotEmpty(report.AssuranceActions);
        Assert.Equal("qualified-accountant-signoff", report.AssuranceActions[0].Code);
        Assert.Equal("critical", report.AssuranceActions[0].Priority);
        Assert.Equal(0, report.AssuranceActions[0].RiskRank);
        Assert.Equal("accountant-review-gate", report.AssuranceActions[0].EvidenceStage);
        Assert.Equal("Qualified accountant", report.AssuranceActions[0].Owner);
        Assert.Contains(report.AssuranceActions, action =>
            action.Code == "light-dark-visual-regression"
            && action.Owner == "Engineering"
            && action.Status == "in-progress"
            && action.RiskRank > report.AssuranceActions[0].RiskRank
            && action.EvidenceStage == "visual-qa-evidence");
        Assert.Contains(report.AssuranceActions, action =>
            action.Code == "production-monitoring"
            && action.Priority == "high"
            && action.RiskRank == 20
            && action.EvidenceStage == "operations-evidence"
            && action.EvidenceRequired.Contains("Sentry", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            report.AssuranceActions.OrderBy(action => action.RiskRank).ThenBy(action => action.Code).Select(action => action.Code),
            report.AssuranceActions.Select(action => action.Code));
        Assert.All(report.AssuranceActions, action =>
        {
            Assert.False(string.IsNullOrWhiteSpace(action.Label));
            Assert.False(string.IsNullOrWhiteSpace(action.Detail));
            Assert.False(string.IsNullOrWhiteSpace(action.EvidenceRequired));
            Assert.False(string.IsNullOrWhiteSpace(action.EvidenceStage));
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
    public async Task ProductionReadinessReport_DeclaresProductionMonitoringControls()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();

        Assert.Contains(report.MonitoringControls, control =>
            control.Code == "error-tracking"
            && control.Provider == "Sentry-compatible"
            && control.Required
            && control.ProductionSafetyGate.Contains("Monitoring:ErrorTrackingDsn", StringComparison.Ordinal)
            && control.EvidenceCaptured.Contains("unhandled exceptions", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.MonitoringControls, control =>
            control.Code == "structured-json-logs"
            && control.Required
            && control.ProductionSafetyGate.Contains("Monitoring:StructuredJsonConsole", StringComparison.Ordinal)
            && control.EvidenceCaptured.Contains("correlation", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.MonitoringControls, control =>
            control.Code == "correlation-id-error-responses"
            && control.Required
            && control.Verification.Contains("ExceptionMiddleware", StringComparison.Ordinal));
        Assert.All(report.MonitoringControls, control =>
        {
            Assert.False(string.IsNullOrWhiteSpace(control.Label));
            Assert.False(string.IsNullOrWhiteSpace(control.EvidenceCaptured));
            Assert.False(string.IsNullOrWhiteSpace(control.Verification));
        });

        var alertRouteProperty = typeof(ProductionMonitoringControl).GetProperty("AlertRoute");
        var failurePolicyProperty = typeof(ProductionMonitoringControl).GetProperty("FailurePolicy");
        Assert.NotNull(alertRouteProperty);
        Assert.NotNull(failurePolicyProperty);

        Assert.All(report.MonitoringControls.Cast<object>(), control =>
        {
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(control, "AlertRoute")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(control, "FailurePolicy")));
        });
        Assert.Contains(report.MonitoringControls.Cast<object>(), control =>
            StringProperty(control, "Code") == "error-tracking"
            && StringProperty(control, "AlertRoute").Contains("on-call", StringComparison.OrdinalIgnoreCase)
            && StringProperty(control, "FailurePolicy").Contains("block", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.MonitoringControls.Cast<object>(), control =>
            StringProperty(control, "Code") == "correlation-id-error-responses"
            && StringProperty(control, "AlertRoute").Contains("support", StringComparison.OrdinalIgnoreCase)
            && StringProperty(control, "FailurePolicy").Contains("correlation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ProductionReadinessReport_DeclaresDependencyPolicyControls()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();
        var dependencyPolicyProperty = report.GetType().GetProperty("DependencyPolicyControls");

        Assert.NotNull(dependencyPolicyProperty);
        var controls = Assert.IsAssignableFrom<System.Collections.IEnumerable>(dependencyPolicyProperty!.GetValue(report))
            .Cast<object>()
            .ToArray();

        Assert.Contains(controls, control =>
            StringProperty(control, "Code") == "frontend-npm-audit"
            && BooleanProperty(control, "Required")
            && StringProperty(control, "Enforcement").Contains("npm audit --audit-level=moderate", StringComparison.Ordinal)
            && StringProperty(control, "FailurePolicy").Contains("moderate", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(controls, control =>
            StringProperty(control, "Code") == "frontend-lockfile-reproducibility"
            && StringProperty(control, "Enforcement").Contains("npm ci", StringComparison.Ordinal)
            && StringProperty(control, "EvidenceCaptured").Contains("package-lock.json", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(controls, control =>
            StringProperty(control, "Code") == "ci-action-version-hygiene"
            && StringProperty(control, "Enforcement").Contains("verify-ci-actions", StringComparison.Ordinal)
            && StringProperty(control, "FailurePolicy").Contains("unpinned", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(controls, control =>
            StringProperty(control, "Code") == "backend-restore-build"
            && StringProperty(control, "Enforcement").Contains("dotnet restore", StringComparison.Ordinal)
            && StringProperty(control, "Enforcement").Contains("dotnet build", StringComparison.Ordinal)
            && StringProperty(control, "EvidenceCaptured").Contains("NuGet", StringComparison.OrdinalIgnoreCase));
        Assert.All(controls, control =>
        {
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(control, "Label")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(control, "EvidenceCaptured")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(control, "Verification")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(control, "FailurePolicy")));
        });
        Assert.Contains(report.AssurancePacket.EvidenceItems, item => item == "dependency-policy-controls");
    }

    [Fact]
    public async Task ProductionReadinessReport_DeclaresDeploymentSafetyControls()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();
        var deploymentSafetyProperty = report.GetType().GetProperty("DeploymentSafetyControls");

        Assert.NotNull(deploymentSafetyProperty);
        var controls = Assert.IsAssignableFrom<System.Collections.IEnumerable>(deploymentSafetyProperty!.GetValue(report))
            .Cast<object>()
            .ToArray();

        Assert.Contains(controls, control =>
            StringProperty(control, "Code") == "controlled-production-migrations"
            && BooleanProperty(control, "Required")
            && StringProperty(control, "Enforcement").Contains("--migrate-only", StringComparison.Ordinal)
            && StringProperty(control, "FailurePolicy").Contains("AutoMigrateOnStartup", StringComparison.Ordinal));
        Assert.Contains(controls, control =>
            StringProperty(control, "Code") == "production-demo-seed-block"
            && StringProperty(control, "Enforcement").Contains("SeedDemoData", StringComparison.Ordinal)
            && StringProperty(control, "FailurePolicy").Contains("demo", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(controls, control =>
            StringProperty(control, "Code") == "backup-restore-drill"
            && StringProperty(control, "EvidenceCaptured").Contains("backup restore", StringComparison.OrdinalIgnoreCase)
            && StringProperty(control, "Verification").Contains("verify-postgres-backup", StringComparison.Ordinal));
        Assert.All(controls, control =>
        {
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(control, "Label")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(control, "EvidenceCaptured")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(control, "Verification")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(control, "FailurePolicy")));
        });
        Assert.Contains(report.AssurancePacket.EvidenceItems, item => item == "deployment-safety-controls");
    }

    [Fact]
    public async Task ProductionReadinessReport_ExposesReleaseReviewChecklistTiedToAssuranceActionsAndAuditEvidence()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();
        var checklistProperty = report.GetType().GetProperty("ReleaseReviewChecklist");

        Assert.NotNull(checklistProperty);
        var checklist = Assert.IsAssignableFrom<System.Collections.IEnumerable>(checklistProperty!.GetValue(report))
            .Cast<object>()
            .ToArray();

        Assert.NotEmpty(checklist);
        Assert.Contains(report.AssurancePacket.EvidenceItems, item => item == "release-review-checklist");
        Assert.Contains(checklist, item =>
            StringProperty(item, "Code") == "accountant-final-signoff"
            && StringProperty(item, "OwnerRole") == "Qualified accountant"
            && StringProperty(item, "AssuranceActionCode") == "qualified-accountant-signoff"
            && StringProperty(item, "OperationalGateCode") == "qualified-accountant-review"
            && StringProperty(item, "EvidenceArtifact") == "named-accountant-approval-record"
            && BooleanProperty(item, "BlocksRelease")
            && StringListProperty(item, "AuditEventCodes").Contains(AuditEventCodes.CroFilingStatusChanged));
        Assert.Contains(checklist, item =>
            StringProperty(item, "Code") == "production-smoke-and-backup"
            && StringProperty(item, "OwnerRole") == "Operations"
            && StringProperty(item, "AssuranceActionCode") == "production-monitoring"
            && StringProperty(item, "EvidenceArtifact") == "ci-production-stack-smoke-and-backup-restore"
            && StringProperty(item, "Status") == "required");

        var assuranceActionCodes = report.AssuranceActions.Select(action => action.Code).ToHashSet(StringComparer.Ordinal);
        var operationalGateCodes = report.OperationalGates.Select(gate => gate.Code).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            report.AssuranceActions.Select(action => action.Code).Order(StringComparer.Ordinal),
            checklist.Select(item => StringProperty(item, "AssuranceActionCode")).Order(StringComparer.Ordinal));
        Assert.All(checklist, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(item, "Code")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(item, "Label")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(item, "OwnerRole")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(item, "EvidenceArtifact")));
            Assert.Contains(StringProperty(item, "AssuranceActionCode"), assuranceActionCodes);

            var operationalGateCode = StringProperty(item, "OperationalGateCode");
            if (!string.IsNullOrWhiteSpace(operationalGateCode))
                Assert.Contains(operationalGateCode, operationalGateCodes);

            var ownerRole = StringProperty(item, "OwnerRole");
            if (ownerRole is "Qualified accountant" or "Reviewer")
                Assert.NotEmpty(StringListProperty(item, "AuditEventCodes"));
        });
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

        var ixbrlRows = report.StatutoryRuleMatrix
            .Where(row => row.RequiredOutputs.Any(output => output.Contains("iXBRL", StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        Assert.NotEmpty(ixbrlRows);
        Assert.All(ixbrlRows, row =>
        {
            Assert.Contains(row.Sources, source => source.SourceId == IrishStatutoryRuleSources.RevenueAcceptedTaxonomies.SourceId);
            Assert.Contains(
                row.RequiredEvidence.Concat(row.ManualHandoffGates),
                evidence => evidence.Contains("external ROS/iXBRL validation", StringComparison.OrdinalIgnoreCase));
        });

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
    public async Task ProductionReadinessReport_ExposesGranularStatutoryRulesCoverageMappedToExecutableTests()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();

        Assert.Contains(report.StatutoryRulesCoverage, coverage =>
            coverage.Code == "size-classification-thresholds"
            && coverage.RuleFamily == "Size classification"
            && coverage.CoverageStatus == "covered"
            && coverage.EdgeCases.Any(edge => edge.Contains("two-of-three", StringComparison.OrdinalIgnoreCase))
            && coverage.EdgeCases.Any(edge => edge.Contains("current and prior", StringComparison.OrdinalIgnoreCase))
            && coverage.Sources.Any(source => source.SourceId == IrishStatutoryRuleSources.CroFinancialStatementsRequirements.SourceId)
            && coverage.AutomatedVerifierNames.Contains("AccountsWorkflowTests.SizeClassification_FirstYearMicro_AllowsMicroAndAuditExemption"));

        Assert.Contains(report.StatutoryRulesCoverage, coverage =>
            coverage.Code == "audit-exemption-loss"
            && coverage.RuleFamily == "Audit exemption"
            && coverage.CoverageStatus == "covered"
            && coverage.EdgeCases.Any(edge => edge.Contains("late CRO filings", StringComparison.OrdinalIgnoreCase))
            && coverage.EdgeCases.Any(edge => edge.Contains("member audit notice", StringComparison.OrdinalIgnoreCase))
            && coverage.AutomatedVerifierNames.Contains("AccountsWorkflowTests.FilingRegime_RecentRepeatedLateCroFilings_RemoveAuditExemption"));

        Assert.Contains(report.StatutoryRulesCoverage, coverage =>
            coverage.Code == "unsupported-fail-closed"
            && coverage.RuleFamily == "Unsupported paths"
            && coverage.CoverageStatus == "covered"
            && coverage.EdgeCases.Any(edge => edge.Contains("PLC", StringComparison.OrdinalIgnoreCase))
            && coverage.EdgeCases.Any(edge => edge.Contains("regulated", StringComparison.OrdinalIgnoreCase))
            && coverage.AutomatedVerifierNames.Contains("FilingReadinessProfileTests.ReadinessProfile_ForUnsupportedCompanyTypes_FailsClosedToManualHandoff"));

        Assert.All(report.StatutoryRulesCoverage, coverage =>
        {
            Assert.False(string.IsNullOrWhiteSpace(coverage.Code));
            Assert.False(string.IsNullOrWhiteSpace(coverage.RuleFamily));
            Assert.False(string.IsNullOrWhiteSpace(coverage.DecisionUnderTest));
            Assert.False(string.IsNullOrWhiteSpace(coverage.CoverageStatus));
            Assert.NotEmpty(coverage.AutomatedVerifierNames);
            Assert.NotEmpty(coverage.EdgeCases);
            Assert.NotEmpty(coverage.Sources);
            foreach (var verifierName in coverage.AutomatedVerifierNames)
                AssertGoldenEvidenceTestExists(verifierName);
        });
    }

    [Fact]
    public async Task ProductionReadinessReport_DeclaresVisualQaCoverageForAccountantWorkbenchRoutes()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();

        Assert.Equal("visual-smoke-screenshots", report.VisualQaCoverage.ArtifactName);
        Assert.Equal("ci-production-smoke", report.VisualQaCoverage.Enforcement);
        Assert.Equal(24, report.VisualQaCoverage.ExpectedScreenshotCount);
        var artifacts = ObjectListProperty(report.VisualQaCoverage, "Artifacts");
        Assert.Equal(report.VisualQaCoverage.ExpectedScreenshotCount, artifacts.Count);
        var layoutChecksProperty = report.VisualQaCoverage.GetType().GetProperty("LayoutChecks");
        Assert.NotNull(layoutChecksProperty);
        var layoutChecks = Assert.IsAssignableFrom<IEnumerable<string>>(layoutChecksProperty!.GetValue(report.VisualQaCoverage)).ToArray();
        Assert.Contains("browser-console-errors", layoutChecks);
        Assert.Contains("page-horizontal-overflow", layoutChecks);
        Assert.Contains("visible-text-overlap", layoutChecks);
        Assert.Equal(["light", "dark"], report.VisualQaCoverage.Themes);
        Assert.Contains(report.VisualQaCoverage.Viewports, viewport =>
            viewport.Name == "desktop" && viewport.Width == 1440 && viewport.Height == 1000);
        Assert.Contains(report.VisualQaCoverage.Viewports, viewport =>
            viewport.Name == "mobile" && viewport.Width == 390 && viewport.Height == 844);
        Assert.Contains(report.VisualQaCoverage.Routes, route =>
            route.Code == "dashboard" && route.RequiredText == "Production Readiness");
        Assert.Contains(report.VisualQaCoverage.Routes, route =>
            route.Code == "company-detail" && route.RequiredText == "Company command centre");
        Assert.Contains(report.VisualQaCoverage.Routes, route =>
            route.Code == "period-workspace" && route.RequiredText == "Filing readiness");
        Assert.Contains(report.VisualQaCoverage.Routes, route =>
            route.Code == "filing-review" && route.OpenFilingTab && route.RequiredText == "Filing readiness profile");
        Assert.Contains(report.VisualQaCoverage.Routes, route =>
            route.Code == "workbench-preview" && route.RequiredText == "Workbench Component Preview");
        foreach (var route in report.VisualQaCoverage.Routes)
        {
            foreach (var theme in report.VisualQaCoverage.Themes)
            {
                foreach (var viewport in report.VisualQaCoverage.Viewports)
                {
                    var artifact = Assert.Single(artifacts, item =>
                        StringProperty(item, "RouteCode") == route.Code
                        && StringProperty(item, "Theme") == theme
                        && StringProperty(item, "ViewportName") == viewport.Name);
                    var fileName = $"{route.Code}-{theme}-{viewport.Name}.png";

                    Assert.Equal(fileName, StringProperty(artifact, "FileName"));
                    Assert.Equal($"artifacts/visual-smoke/{fileName}", StringProperty(artifact, "ArtifactPath"));
                    Assert.Equal(route.RequiredText, StringProperty(artifact, "RequiredText"));
                    Assert.Equal("required-review", StringProperty(artifact, "ReviewStatus"));
                    Assert.Equal(route.OpenFilingTab, BooleanProperty(artifact, "OpenFilingTab"));
                    Assert.Equal(report.VisualQaCoverage.LayoutChecks, StringListProperty(artifact, "LayoutChecks"));
                }
            }
        }
        var expectedWorkflowStages = new[]
        {
            "Setup",
            "Import",
            "Classify",
            "Year-End",
            "Statements",
            "Notes",
            "Review",
            "Filing"
        };
        var periodWorkspace = Assert.Single(report.VisualQaCoverage.Routes, route => route.Code == "period-workspace");
        Assert.Equal(expectedWorkflowStages, periodWorkspace.WorkflowStages);
        var coveredWorkflowStages = report.VisualQaCoverage.Routes
            .SelectMany(route => route.WorkflowStages)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            expectedWorkflowStages.Order(StringComparer.Ordinal),
            coveredWorkflowStages);
        Assert.All(report.VisualQaCoverage.Routes, route =>
        {
            Assert.False(string.IsNullOrWhiteSpace(route.Label));
            Assert.False(string.IsNullOrWhiteSpace(route.Description));
            Assert.NotEmpty(route.WorkflowStages);
        });
    }

    [Fact]
    public async Task VisualQaCoverage_ExposesCaptureRouteKeysUsedByTheBrowserSmokeRunner()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();
        var expectedRouteKeys = new Dictionary<string, string>
        {
            ["dashboard"] = "dashboard",
            ["production-readiness"] = "readiness",
            ["company-detail"] = "company",
            ["period-workspace"] = "period",
            ["filing-review"] = "filing",
            ["workbench-preview"] = "workbenchPreview"
        };

        foreach (var route in report.VisualQaCoverage.Routes)
        {
            var routeKeyProperty = route.GetType().GetProperty("RouteKey");
            Assert.NotNull(routeKeyProperty);
            Assert.Equal(expectedRouteKeys[route.Code], routeKeyProperty!.GetValue(route));
        }

        var artifacts = ObjectListProperty(report.VisualQaCoverage, "Artifacts");
        foreach (var artifact in artifacts)
        {
            var routeCode = StringProperty(artifact, "RouteCode");
            var routeKeyProperty = artifact.GetType().GetProperty("RouteKey");
            Assert.NotNull(routeKeyProperty);
            Assert.Equal(expectedRouteKeys[routeCode], routeKeyProperty!.GetValue(artifact));
        }
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

    private static void AssertGoldenFixture(
        ProductionReadinessReport report,
        string scenarioCode,
        string legalName,
        string companyType,
        string periodStart,
        string periodEnd,
        string expectedSizeClass,
        string expectedRegime,
        bool auditExempt,
        bool manualReviewRequired)
    {
        var scenario = Assert.Single(report.GoldenFilingCorpus, s => s.Code == scenarioCode);
        var fixtureProperty = scenario.GetType().GetProperty("Fixture");
        Assert.NotNull(fixtureProperty);
        var fixture = fixtureProperty!.GetValue(scenario);
        Assert.NotNull(fixture);

        Assert.Equal(legalName, StringProperty(fixture!, "LegalName"));
        Assert.Equal(companyType, StringProperty(fixture!, "CompanyType"));
        Assert.Equal(periodStart, StringProperty(fixture!, "PeriodStart"));
        Assert.Equal(periodEnd, StringProperty(fixture!, "PeriodEnd"));
        Assert.Equal(expectedSizeClass, StringProperty(fixture!, "ExpectedSizeClass"));
        Assert.Equal(expectedRegime, StringProperty(fixture!, "ExpectedRegime"));
        Assert.Equal(auditExempt, BooleanProperty(fixture!, "AuditExempt"));
        Assert.Equal(manualReviewRequired, BooleanProperty(fixture!, "ManualProfessionalReviewRequired"));
    }

    private static void AssertGoldenExpectedOutputs(
        ProductionReadinessReport report,
        string scenarioCode,
        string expectedPdfMarker,
        string expectedIxbrlTag,
        decimal expectedTax,
        string readinessState,
        string signOffState)
    {
        var scenario = Assert.Single(report.GoldenFilingCorpus, s => s.Code == scenarioCode);
        var expectedOutputs = ObjectProperty(scenario.EvidencePack, "ExpectedOutputs");

        Assert.Contains(StringListProperty(expectedOutputs, "PdfTextMarkers"), marker =>
            marker.Contains(expectedPdfMarker, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(StringListProperty(expectedOutputs, "IxbrlRequiredTags"), tag =>
            tag.Contains(expectedIxbrlTag, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(expectedTax, DecimalProperty(expectedOutputs, "ExpectedCorporationTax"));
        Assert.Equal(readinessState, StringProperty(expectedOutputs, "FilingReadinessState"));
        Assert.Equal(signOffState, StringProperty(expectedOutputs, "SignOffPacketState"));
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

    private static IReadOnlyList<object> ObjectListProperty(object value, string propertyName)
    {
        var property = value.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        return Assert.IsAssignableFrom<System.Collections.IEnumerable>(property!.GetValue(value))
            .Cast<object>()
            .ToArray();
    }

    private static object ObjectProperty(object value, string propertyName)
    {
        var property = value.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        var propertyValue = property!.GetValue(value);
        Assert.NotNull(propertyValue);
        return propertyValue!;
    }

    private static decimal DecimalProperty(object value, string propertyName)
    {
        var property = value.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        return Assert.IsType<decimal>(property!.GetValue(value));
    }
}
