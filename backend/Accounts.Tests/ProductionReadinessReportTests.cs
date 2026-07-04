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

    private static IReadOnlyList<object> ObjectListProperty(object value, string propertyName)
    {
        var property = value.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        return Assert.IsAssignableFrom<System.Collections.IEnumerable>(property!.GetValue(value))
            .Cast<object>()
            .ToArray();
    }
}
