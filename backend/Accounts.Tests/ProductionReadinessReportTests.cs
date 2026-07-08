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
    public async Task ProductionReadinessReport_DeclaresSourceLawMaintenanceProtocolForLegalChangeControl()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();
        var protocolProperty = report.GetType().GetProperty("SourceLawMaintenanceProtocol");

        Assert.NotNull(protocolProperty);
        var protocol = protocolProperty!.GetValue(report)!;
        var monitoredSources = StringListProperty(protocol, "MonitoredSourceIds");
        var signOffGate = StringProperty(protocol, "SignOffGate");

        Assert.Equal("source-law-maintenance-v1", StringProperty(protocol, "ProtocolVersion"));
        Assert.Equal("Qualified accountant and engineering", StringProperty(protocol, "OwnerRole"));
        Assert.Equal("required-review", StringProperty(protocol, "Status"));
        Assert.Contains("monthly", StringProperty(protocol, "ReviewCadence"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Block release", StringProperty(protocol, "FailurePolicy"));
        Assert.Equal("source-law-change-review", signOffGate);
        Assert.Equal(
            report.SourceLawSnapshot.Sources.Select(source => source.SourceId).Order(StringComparer.Ordinal),
            monitoredSources.Order(StringComparer.Ordinal));
        AssertListContainsAll(
            StringListProperty(protocol, "AcceptanceCriteria"),
            [
                "CRO",
                "Revenue",
                "FRC",
                "effective date",
                "qualified accountant"
            ],
            "source-law",
            "acceptance criteria");
        AssertListContainsAll(
            StringListProperty(protocol, "RequiredEvidence"),
            [
                "source-law-snapshot-fingerprint",
                "source-law-traceability-index",
                "source-law-change-review-note",
                "qualified-accountant-source-law-signoff"
            ],
            "source-law",
            "required evidence");
        Assert.Contains(report.AssurancePacket.EvidenceItems, item => item == "source-law-maintenance-protocol");
        Assert.Contains(report.AssuranceActions, action =>
            action.Code == "source-law-change-review"
            && action.Priority == "critical"
            && action.RiskRank < 5);
        Assert.Contains(report.ReleaseReviewChecklist, item =>
            item.Code == signOffGate
            && item.AssuranceActionCode == "source-law-change-review"
            && item.EvidenceArtifact == "source-law-change-review-note"
            && item.BlocksRelease);
    }

    [Fact]
    public async Task ProductionReadinessReport_ExposesPerSourceLawReviewLedgerForReleaseEvidence()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();
        var ledgerProperty = report.GetType().GetProperty("SourceLawReviewLedger");

        Assert.NotNull(ledgerProperty);
        var ledger = Assert.IsAssignableFrom<System.Collections.IEnumerable>(ledgerProperty!.GetValue(report))
            .Cast<object>()
            .ToArray();

        Assert.Equal(report.SourceLawSnapshot.SourceCount, ledger.Length);
        Assert.Contains(report.AssurancePacket.EvidenceItems, item => item == "source-law-review-ledger");

        var snapshotSources = report.SourceLawSnapshot.Sources.ToDictionary(source => source.SourceId);
        Assert.All(ledger, entry =>
        {
            var sourceId = StringProperty(entry, "SourceId");
            Assert.True(snapshotSources.ContainsKey(sourceId));
            Assert.Equal(snapshotSources[sourceId].EffectiveDate.ToString("yyyy-MM-dd"), StringProperty(entry, "PinnedEffectiveDate"));
            Assert.Equal("source-law-change-review", StringProperty(entry, "ReleaseChecklistCode"));
            Assert.True(BooleanProperty(entry, "BlocksRelease"));
            AssertListContainsAll(
                StringListProperty(entry, "ReviewChecks"),
                ["reachable", "effective date", "guidance wording", "qualified accountant"],
                sourceId,
                "source-law review checks");
            AssertListContainsAll(
                StringListProperty(entry, "RequiredEvidence"),
                ["source-law-change-review-note", "qualified-accountant-source-law-signoff"],
                sourceId,
                "source-law review evidence");
        });

        Assert.Contains(ledger, entry =>
            StringProperty(entry, "SourceId") == IrishStatutoryRuleSources.RevenueAcceptedTaxonomies.SourceId
            && StringProperty(entry, "OwnerRole").Contains("Taxonomy", StringComparison.OrdinalIgnoreCase)
            && StringListProperty(entry, "ReviewChecks").Any(check => check.Contains("Revenue-accepted taxonomy", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(ledger, entry =>
            StringProperty(entry, "SourceId") == IrishStatutoryRuleSources.CroAuditorsReport.SourceId
            && StringListProperty(entry, "ReviewChecks").Any(check => check.Contains("auditor", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task ProductionReadinessReport_ExposesRevenueAcceptedTaxonomyRangesForSourceLawEvidence()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();
        var rangesProperty = report.GetType().GetProperty("RevenueTaxonomyRanges");

        Assert.NotNull(rangesProperty);
        var ranges = Assert.IsAssignableFrom<System.Collections.IEnumerable>(rangesProperty!.GetValue(report))
            .Cast<object>()
            .ToArray();

        Assert.Equal(9, ranges.Length);
        Assert.Contains(report.AssurancePacket.EvidenceItems, item => item == "revenue-taxonomy-range-evidence");
        Assert.Equal(3, ranges.Count(range => StringProperty(range, "AccountingStandard") == "FRS 101"));
        Assert.Equal(3, ranges.Count(range => StringProperty(range, "AccountingStandard") == "FRS 102"));
        Assert.Equal(3, ranges.Count(range => StringProperty(range, "AccountingStandard") == "EU IFRS"));
        AssertRevenueTaxonomyRange(
            ranges,
            "irish-extension-2025-frs-102",
            "FRS 102",
            "2025-01-01",
            "2024-01-01",
            "",
            "/FRS-102/2025-01-01/");
        AssertRevenueTaxonomyRange(
            ranges,
            "irish-extension-2023-frs-102",
            "FRS 102",
            "2023-01-01",
            "2023-01-01",
            "2024-01-01",
            "/FRS-102/2023-01-01/");
        AssertRevenueTaxonomyRange(
            ranges,
            "irish-extension-2022-frs-102",
            "FRS 102",
            "2022-01-01",
            "2019-01-01",
            "2023-01-01",
            "/FRS-102/2022-01-01/");
        AssertRevenueTaxonomyRange(
            ranges,
            "irish-extension-2025-frs-101",
            "FRS 101",
            "2025-01-01",
            "2024-01-01",
            "",
            "/FRS-101/2025-01-01/");
        AssertRevenueTaxonomyRange(
            ranges,
            "irish-extension-2023-ifrs",
            "EU IFRS",
            "2023-01-01",
            "2023-01-01",
            "2024-01-01",
            "/IFRS/2023-01-01/");
        AssertRevenueTaxonomyRange(
            ranges,
            "irish-extension-2022-frs-101",
            "FRS 101",
            "2022-01-01",
            "2018-01-01",
            "2023-01-01",
            "/FRS-101/2022-01-01/");
        AssertRevenueTaxonomyRange(
            ranges,
            "irish-extension-2022-ifrs",
            "EU IFRS",
            "2022-01-01",
            "2018-01-01",
            "2023-01-01",
            "/IFRS/2022-01-01/");

        Assert.All(ranges, range =>
        {
            Assert.True(BooleanProperty(range, "AcceptedByRevenue"));
            Assert.Contains(IrishStatutoryRuleSources.RevenueAcceptedTaxonomies.SourceId, StringListProperty(range, "SourceIds"));
            Assert.Contains("ixbrl-taxonomy-selection", StringListProperty(range, "ReleaseGateCodes"));
            Assert.Contains("source-law-change-review", StringListProperty(range, "ReleaseGateCodes"));
        });
        Assert.All(
            ranges.Where(range => StringProperty(range, "AccountingStandard") == "FRS 102"),
            range =>
            {
                Assert.True(BooleanProperty(range, "AutomatedPlatformSelectionSupported"));
                Assert.Contains(IrishStatutoryRuleSources.FrcFrs102.SourceId, StringListProperty(range, "SourceIds"));
                Assert.DoesNotContain("manual-professional-handoff", StringListProperty(range, "ReleaseGateCodes"));
            });
        Assert.All(
            ranges.Where(range => StringProperty(range, "AccountingStandard") is "FRS 101" or "EU IFRS"),
            range =>
            {
                Assert.False(BooleanProperty(range, "AutomatedPlatformSelectionSupported"));
                Assert.Contains("manual-professional-handoff", StringListProperty(range, "ReleaseGateCodes"));
            });
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
    public async Task ProductionReadinessReport_ExposesProductionAuditEvidencePackForReleaseReview()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();
        var packProperty = report.GetType().GetProperty("AuditEvidencePack");

        Assert.NotNull(packProperty);
        var pack = Assert.IsAssignableFrom<System.Collections.IEnumerable>(packProperty!.GetValue(report))
            .Cast<object>()
            .ToArray();

        Assert.Contains(report.AssurancePacket.EvidenceItems, item => item == "production-audit-evidence-pack");
        Assert.Contains(pack, item =>
            StringProperty(item, "Code") == "who-changed-what"
            && StringProperty(item, "RequiredArtifact") == "tamper-evident-audit-log-entry"
            && StringProperty(item, "RetainedIn") == "audit_logs"
            && StringProperty(item, "FailurePolicy").Contains("Block release", StringComparison.OrdinalIgnoreCase)
            && StringListProperty(item, "AuditEventCodes").Contains(AuditEventCodes.AdjustmentUpdated)
            && StringListProperty(item, "BlockingGateCodes").Contains("working-paper-review"));
        Assert.Contains(pack, item =>
            StringProperty(item, "Code") == "who-approved-what"
            && StringProperty(item, "RequiredActor").Contains("qualified accountant", StringComparison.OrdinalIgnoreCase)
            && StringProperty(item, "RequiredArtifact") == "named-accountant-approval-record"
            && StringListProperty(item, "BlockingGateCodes").Contains("qualified-accountant-review"));
        Assert.Contains(pack, item =>
            StringProperty(item, "Code") == "evidence-present-at-approval"
            && StringProperty(item, "EvidenceQuestion").Contains("What evidence was present", StringComparison.OrdinalIgnoreCase)
            && StringProperty(item, "RetainedIn") == "filing-readiness-profile-snapshot"
            && StringListProperty(item, "BlockingGateCodes").Contains("generated-output-review"));
        Assert.Contains(pack, item =>
            StringProperty(item, "Code") == "generated-output-fingerprint"
            && StringProperty(item, "RequiredArtifact") == "generated-output-fingerprint"
            && StringListProperty(item, "AuditEventCodes").Contains(AuditEventCodes.CroDocumentGenerated));
        Assert.Contains(pack, item =>
            StringProperty(item, "Code") == "integrity-chain-checkpoint"
            && StringProperty(item, "RequiredArtifact") == "signed-audit-integrity-checkpoint"
            && StringProperty(item, "Verification").Contains("previous hash", StringComparison.OrdinalIgnoreCase));

        Assert.All(pack, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(item, "Code")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(item, "Label")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(item, "EvidenceQuestion")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(item, "RequiredArtifact")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(item, "RetainedIn")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(item, "RequiredActor")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(item, "CapturedWhen")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(item, "Verification")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(item, "FailurePolicy")));
            Assert.NotEmpty(StringListProperty(item, "AuditEventCodes"));
            Assert.NotEmpty(StringListProperty(item, "BlockingGateCodes"));
        });
    }

    [Fact]
    public async Task ProductionReadinessReport_ExposesAccountantAcceptanceSummaryForReleaseDecision()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();
        var summaryProperty = report.GetType().GetProperty("AccountantAcceptanceSummary");

        Assert.NotNull(summaryProperty);
        var summary = summaryProperty!.GetValue(report)!;

        Assert.Equal(report.GoldenFilingCorpus.Count, IntProperty(summary, "ScenarioCount"));
        Assert.Equal(
            report.AccountantAcceptanceCriteria.Count(criterion => criterion.Required),
            IntProperty(summary, "ProfessionalSignOffRequiredCount"));
        Assert.Equal(
            report.AccountantAcceptanceCriteria
                .SelectMany(criterion => criterion.EvidenceVerifiers)
                .Select(verifier => verifier.Name)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            IntProperty(summary, "AutomatedVerifierCount"));
        Assert.Equal(
            report.GoldenFilingCorpus.Count(scenario =>
                scenario.ExpectedOutcome.Contains("manual-handoff", StringComparison.OrdinalIgnoreCase)
                || scenario.Fixture.ManualProfessionalReviewRequired),
            IntProperty(summary, "ManualHandoffScenarioCount"));
        Assert.Equal(
            report.AccountantAcceptanceCriteria
                .Where(criterion => criterion.Required && criterion.AcceptanceStatus != "accepted")
                .Select(criterion => criterion.ScenarioCode)
                .Order(StringComparer.Ordinal),
            StringListProperty(summary, "ReleaseBlockingScenarioCodes").Order(StringComparer.Ordinal));
        Assert.Contains(
            report.AccountantAcceptanceCriteria[0].RequiredSignOffGate,
            StringListProperty(summary, "RequiredSignOffGates"));
        Assert.Equal("qualified-accountant-review-required", StringProperty(summary, "Status"));
    }

    [Fact]
    public async Task ProductionReadinessReport_ExposesAccountantWorkflowWalkthroughProtocolForSeededSignOff()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();
        var protocolProperty = report.GetType().GetProperty("AccountantWorkflowWalkthroughProtocol");

        Assert.NotNull(protocolProperty);
        var protocol = protocolProperty!.GetValue(report)!;

        Assert.Equal("accountant-workflow-walkthrough-v1", StringProperty(protocol, "ProtocolVersion"));
        Assert.Equal("Qualified accountant", StringProperty(protocol, "ReviewerRole"));
        Assert.Equal("required-review", StringProperty(protocol, "Status"));
        Assert.Equal("golden-corpus-accountant-acceptance", StringProperty(protocol, "SignOffGate"));
        Assert.Contains("Block release", StringProperty(protocol, "FailurePolicy"), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            report.GoldenFilingCorpus.Select(scenario => scenario.Code).Order(StringComparer.Ordinal),
            StringListProperty(protocol, "SeededScenarioCodes").Order(StringComparer.Ordinal));
        AssertListContainsAll(
            StringListProperty(protocol, "RouteSequence"),
            [
                "Dashboard",
                "Company detail",
                "Period workspace",
                "Financial statements",
                "Filing review",
                "Production readiness"
            ],
            "accountant-walkthrough",
            "route sequence");
        AssertListContainsAll(
            StringListProperty(protocol, "AcceptanceCriteria"),
            [
                "micro LTD",
                "small abridged LTD",
                "CLG charity",
                "medium/audit-required",
                "outputs, gates, wording and evidence"
            ],
            "accountant-walkthrough",
            "acceptance criteria");
        AssertListContainsAll(
            StringListProperty(protocol, "RequiredEvidence"),
            [
                "seeded golden corpus walkthrough note",
                "named qualified-accountant approval",
                "visual QA screenshot review",
                "generated PDF and iXBRL evidence",
                "manual handoff acceptance"
            ],
            "accountant-walkthrough",
            "required evidence");
        Assert.Contains(report.AssurancePacket.EvidenceItems, item => item == "accountant-workflow-walkthrough-protocol");
        Assert.Contains(report.ReleaseReviewChecklist, item =>
            item.Code == "golden-corpus-accountant-acceptance"
            && item.EvidenceArtifact == "signed-golden-corpus-acceptance-note"
            && item.BlocksRelease);
    }

    [Fact]
    public async Task ProductionReadinessReport_ExposesAccountantJourneyAcceptanceChecklistAcrossWorkbenchRoutes()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();
        var checklistProperty = report.GetType().GetProperty("AccountantJourneyAcceptanceChecklist");

        Assert.NotNull(checklistProperty);
        var checklist = Assert.IsAssignableFrom<System.Collections.IEnumerable>(checklistProperty!.GetValue(report))
            .Cast<object>()
            .ToArray();

        Assert.Contains(report.AssurancePacket.EvidenceItems, item => item == "accountant-journey-acceptance-checklist");
        Assert.Equal(
            new[] { "company-detail", "dashboard", "filing-review", "financial-statements", "period-workspace", "production-readiness" },
            checklist.Select(item => StringProperty(item, "RouteCode")).Order(StringComparer.Ordinal));

        var scenarioCodes = report.GoldenFilingCorpus.Select(scenario => scenario.Code).Order(StringComparer.Ordinal).ToArray();
        var visualArtifacts = ObjectListProperty(report.VisualQaCoverage, "Artifacts");

        foreach (var item in checklist)
        {
            var routeCode = StringProperty(item, "RouteCode");
            var route = Assert.Single(report.VisualQaCoverage.Routes, route => route.Code == routeCode);

            Assert.Equal(route.RouteKey, StringProperty(item, "RouteKey"));
            Assert.Equal(route.Label, StringProperty(item, "RouteLabel"));
            Assert.Equal("required-review", StringProperty(item, "Status"));
            Assert.Equal("golden-corpus-accountant-acceptance", StringProperty(item, "SignOffGate"));
            Assert.Equal(route.WorkflowStages, StringListProperty(item, "WorkflowStages"));
            Assert.Equal(scenarioCodes, StringListProperty(item, "SeededScenarioCodes").Order(StringComparer.Ordinal));
            Assert.Contains("named qualified-accountant route acceptance", StringListProperty(item, "RequiredEvidence"));
            Assert.Contains("visual smoke screenshots reviewed", StringListProperty(item, "RequiredEvidence"));
            Assert.Contains("golden corpus evidence accepted", StringListProperty(item, "RequiredEvidence"));
            Assert.Contains(StringListProperty(item, "AcceptanceCriteria"), criterion =>
                criterion.Contains(route.Label, StringComparison.OrdinalIgnoreCase));
            Assert.Contains(StringListProperty(item, "AcceptanceCriteria"), criterion =>
                criterion.Contains("outputs, gates, wording and evidence", StringComparison.OrdinalIgnoreCase));

            var expectedArtifactNames = visualArtifacts
                .Where(artifact => StringProperty(artifact, "RouteCode") == routeCode)
                .Select(artifact => StringProperty(artifact, "FileName"))
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(4, expectedArtifactNames.Length);
            Assert.Equal(expectedArtifactNames, StringListProperty(item, "VisualArtifactNames").Order(StringComparer.Ordinal));
        }

        var filingReview = Assert.Single(checklist, item => StringProperty(item, "RouteCode") == "filing-review");
        Assert.Contains(StringListProperty(filingReview, "WorkflowStages"), stage => stage == "Review");
        Assert.Contains(StringListProperty(filingReview, "WorkflowStages"), stage => stage == "Filing");
        Assert.Contains(StringListProperty(filingReview, "AcceptanceCriteria"), criterion =>
            criterion.Contains("external ROS/iXBRL validation", StringComparison.OrdinalIgnoreCase));

        var financialStatements = Assert.Single(checklist, item => StringProperty(item, "RouteCode") == "financial-statements");
        Assert.Equal(["Statements"], StringListProperty(financialStatements, "WorkflowStages"));
        Assert.Contains(StringListProperty(financialStatements, "AcceptanceCriteria"), criterion =>
            criterion.Contains("statement preview, tax computation, source trail and directors' report", StringComparison.OrdinalIgnoreCase));

        var productionReadiness = Assert.Single(checklist, item => StringProperty(item, "RouteCode") == "production-readiness");
        Assert.Contains(StringListProperty(productionReadiness, "AcceptanceCriteria"), criterion =>
            criterion.Contains("release blockers", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ProductionReadinessReport_ExposesAccountantWorkflowEvidencePackForRouteAcceptance()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();
        var packProperty = report.GetType().GetProperty("AccountantWorkflowEvidencePack");

        Assert.NotNull(packProperty);
        var pack = Assert.IsAssignableFrom<System.Collections.IEnumerable>(packProperty!.GetValue(report))
            .Cast<object>()
            .ToArray();

        Assert.Contains(report.AssurancePacket.EvidenceItems, item => item == "accountant-workflow-evidence-pack");
        Assert.Equal(
            report.AccountantJourneyAcceptanceChecklist.Select(item => item.RouteCode).Order(StringComparer.Ordinal),
            pack.Select(item => StringProperty(item, "RouteCode")).Order(StringComparer.Ordinal));

        var scenarioCodes = report.GoldenFilingCorpus.Select(scenario => scenario.Code).Order(StringComparer.Ordinal).ToArray();
        foreach (var item in pack)
        {
            var routeCode = StringProperty(item, "RouteCode");
            var checklistItem = Assert.Single(report.AccountantJourneyAcceptanceChecklist, route => route.RouteCode == routeCode);

            Assert.Equal(checklistItem.RouteLabel, StringProperty(item, "RouteLabel"));
            Assert.Equal(checklistItem.WorkflowStages, StringListProperty(item, "WorkflowStages"));
            Assert.Equal(scenarioCodes, StringListProperty(item, "SeededScenarioCodes").Order(StringComparer.Ordinal));
            Assert.Equal(checklistItem.VisualArtifactNames.Order(StringComparer.Ordinal), StringListProperty(item, "VisualArtifactNames").Order(StringComparer.Ordinal));
            Assert.Equal("golden-corpus-accountant-acceptance", StringProperty(item, "SignOffGate"));
            Assert.Contains("Block release", StringProperty(item, "FailurePolicy"), StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(item, "EvidenceArtifact")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(item, "DecisionQuestion")));
            Assert.Contains("outputs, gates, wording and evidence", StringProperty(item, "DecisionQuestion"), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("named qualified-accountant route acceptance", StringListProperty(item, "RequiredEvidence"));
            Assert.Contains("visual smoke screenshots reviewed", StringListProperty(item, "RequiredEvidence"));
            Assert.Contains("golden corpus evidence accepted", StringListProperty(item, "RequiredEvidence"));
        }

        Assert.Contains(pack, item =>
            StringProperty(item, "RouteCode") == "dashboard"
            && StringProperty(item, "EvidenceArtifact") == "dashboard-accountant-route-acceptance-note"
            && StringListProperty(item, "WorkflowStages").Contains("Filing"));
        Assert.Contains(pack, item =>
            StringProperty(item, "RouteCode") == "filing-review"
            && StringProperty(item, "DecisionQuestion").Contains("external ROS/iXBRL validation", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(pack, item =>
            StringProperty(item, "RouteCode") == "production-readiness"
            && StringProperty(item, "DecisionQuestion").Contains("release blockers", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ProductionReadinessReport_ExposesScenarioRouteAccountantWalkthroughEvidenceMatrix()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();
        var matrixProperty = report.GetType().GetProperty("AccountantWalkthroughEvidenceMatrix");

        Assert.NotNull(matrixProperty);
        var matrix = Assert.IsAssignableFrom<System.Collections.IEnumerable>(matrixProperty!.GetValue(report))
            .Cast<object>()
            .ToArray();

        Assert.Contains(report.AssurancePacket.EvidenceItems, item => item == "accountant-walkthrough-evidence-matrix");
        Assert.Equal(
            report.GoldenFilingCorpus.Count * report.AccountantJourneyAcceptanceChecklist.Count,
            matrix.Length);

        foreach (var scenario in report.GoldenFilingCorpus)
        {
            var acceptance = Assert.Single(report.AccountantAcceptanceCriteria, criterion => criterion.ScenarioCode == scenario.Code);
            var scenarioRows = matrix.Where(item => StringProperty(item, "ScenarioCode") == scenario.Code).ToArray();

            Assert.Equal(report.AccountantJourneyAcceptanceChecklist.Count, scenarioRows.Length);
            Assert.All(report.AccountantJourneyAcceptanceChecklist, route =>
                Assert.Contains(scenarioRows, item => StringProperty(item, "RouteCode") == route.RouteCode));

            foreach (var row in scenarioRows)
            {
                var route = Assert.Single(
                    report.AccountantJourneyAcceptanceChecklist,
                    item => item.RouteCode == StringProperty(row, "RouteCode"));
                var routeEvidence = Assert.Single(
                    report.AccountantWorkflowEvidencePack,
                    item => item.RouteCode == route.RouteCode);

                Assert.Equal(scenario.Label, StringProperty(row, "ScenarioLabel"));
                Assert.Equal(scenario.ExpectedOutcome, StringProperty(row, "ExpectedOutcome"));
                Assert.Equal(scenario.EvidencePack.ExpectedOutputs.FilingReadinessState, StringProperty(row, "FilingReadinessState"));
                Assert.Equal(scenario.EvidencePack.ExpectedOutputs.SignOffPacketState, StringProperty(row, "SignOffPacketState"));
                Assert.Equal(scenario.Fixture.ManualProfessionalReviewRequired, BooleanProperty(row, "ManualProfessionalReviewRequired"));
                Assert.Equal(route.RouteLabel, StringProperty(row, "RouteLabel"));
                Assert.Equal(route.RouteKey, StringProperty(row, "RouteKey"));
                Assert.Equal(route.WorkflowStages, StringListProperty(row, "WorkflowStages"));
                Assert.Equal(route.VisualArtifactNames.Order(StringComparer.Ordinal), StringListProperty(row, "VisualArtifactNames").Order(StringComparer.Ordinal));
                Assert.Equal($"{scenario.Code}-{route.RouteCode}-walkthrough-note", StringProperty(row, "EvidenceArtifact"));
                Assert.Equal(routeEvidence.DecisionQuestion, StringProperty(row, "DecisionQuestion"));
                Assert.Equal("golden-corpus-accountant-acceptance", StringProperty(row, "ReleaseChecklistCode"));
                Assert.Equal(route.SignOffGate, StringProperty(row, "SignOffGate"));
                Assert.Equal("required-review", StringProperty(row, "Status"));
                Assert.True(BooleanProperty(row, "BlocksRelease"));

                Assert.Contains("named qualified-accountant route acceptance", StringListProperty(row, "RequiredEvidence"));
                Assert.Contains("visual smoke screenshots reviewed", StringListProperty(row, "RequiredEvidence"));
                Assert.Contains("golden corpus evidence accepted", StringListProperty(row, "RequiredEvidence"));
                Assert.All(acceptance.RequiredEvidence, requiredEvidence =>
                    Assert.Contains(requiredEvidence, StringListProperty(row, "RequiredEvidence")));
                Assert.Contains(StringListProperty(row, "AcceptanceCriteria"), criterion =>
                    criterion.Contains(route.RouteLabel, StringComparison.OrdinalIgnoreCase));
                Assert.Contains(StringListProperty(row, "AcceptanceCriteria"), criterion =>
                    criterion.Contains(scenario.Label, StringComparison.OrdinalIgnoreCase));
            }
        }

        var mediumFiling = Assert.Single(matrix, item =>
            StringProperty(item, "ScenarioCode") == "medium-audit-required"
            && StringProperty(item, "RouteCode") == "filing-review");
        Assert.True(BooleanProperty(mediumFiling, "ManualProfessionalReviewRequired"));
        Assert.Equal("manual-handoff", StringProperty(mediumFiling, "SignOffPacketState"));
        Assert.Contains(StringListProperty(mediumFiling, "RequiredEvidence"), evidence =>
            evidence.Contains("manual handoff", StringComparison.OrdinalIgnoreCase)
            || evidence.Contains("auditor", StringComparison.OrdinalIgnoreCase));

        var productionReadinessRows = matrix
            .Where(item => StringProperty(item, "RouteCode") == "production-readiness")
            .ToArray();
        Assert.Equal(report.GoldenFilingCorpus.Count, productionReadinessRows.Length);
        Assert.All(productionReadinessRows, item =>
            Assert.Contains("release blockers", StringProperty(item, "DecisionQuestion"), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ProductionReadinessReport_ExposesWorkbenchVisualAcceptanceRegisterForUiPolish()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();
        var registerProperty = report.GetType().GetProperty("WorkbenchVisualAcceptanceRegister");

        Assert.NotNull(registerProperty);
        var register = Assert.IsAssignableFrom<System.Collections.IEnumerable>(registerProperty!.GetValue(report))
            .Cast<object>()
            .ToArray();

        Assert.Contains(report.AssurancePacket.EvidenceItems, item => item == "workbench-visual-acceptance-register");
        Assert.Equal(
            report.VisualQaCoverage.RouteAudits.Select(audit => audit.RouteCode).Order(StringComparer.Ordinal),
            register.Select(item => StringProperty(item, "RouteCode")).Order(StringComparer.Ordinal));

        foreach (var item in register)
        {
            var routeCode = StringProperty(item, "RouteCode");
            var routeAudit = Assert.Single(report.VisualQaCoverage.RouteAudits, audit => audit.RouteCode == routeCode);
            var artifactNames = report.VisualQaCoverage.Artifacts
                .Where(artifact => artifact.RouteCode == routeCode)
                .Select(artifact => artifact.FileName)
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(routeAudit.Label, StringProperty(item, "RouteLabel"));
            Assert.Equal(routeAudit.WorkflowStages, StringListProperty(item, "WorkflowStages"));
            Assert.Equal(routeAudit.ReviewChecks.Order(StringComparer.Ordinal), StringListProperty(item, "AcceptanceAreas").Order(StringComparer.Ordinal));
            Assert.Equal(artifactNames, StringListProperty(item, "ScreenshotArtifactNames").Order(StringComparer.Ordinal));
            Assert.Equal("visual-qa-screenshot-review", StringProperty(item, "ReleaseGateCode"));
            Assert.Equal("required-review", StringProperty(item, "Status"));
            Assert.Contains("Block release", StringProperty(item, "FailurePolicy"), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("route-state acceptance note", StringListProperty(item, "RequiredEvidence"));
            Assert.Contains("light/dark desktop/mobile screenshot review", StringListProperty(item, "RequiredEvidence"));
            Assert.Contains("named visual QA reviewer sign-off", StringListProperty(item, "RequiredEvidence"));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(item, "EvidenceArtifact")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(item, "NextAction")));
        }

        Assert.Contains(register, item =>
            StringProperty(item, "RouteCode") == "dashboard"
            && StringListProperty(item, "AcceptanceAreas").Contains("accountant-workflow-hierarchy"));
        Assert.Contains(register, item =>
            StringProperty(item, "RouteCode") == "filing-review"
            && StringProperty(item, "NextAction").Contains("evidence checklist", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(register, item =>
            StringProperty(item, "RouteCode") == "production-readiness"
            && StringProperty(item, "NextAction").Contains("release blockers", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ProductionReadinessReport_ExposesCompletionTracksForBackendUiAndFrontendCode()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();
        var tracksProperty = report.GetType().GetProperty("CompletionTracks");

        Assert.NotNull(tracksProperty);
        var tracks = Assert.IsAssignableFrom<System.Collections.IEnumerable>(tracksProperty!.GetValue(report))
            .Cast<object>()
            .ToArray();

        Assert.Equal(
            new[] { "backend-code", "frontend-code", "frontend-ui-ux" },
            tracks.Select(track => StringProperty(track, "Code")).Order(StringComparer.Ordinal));

        var assuranceActionCodes = report.AssuranceActions
            .Select(action => action.Code)
            .ToHashSet(StringComparer.Ordinal);

        AssertCompletionTrack(
            tracks,
            "backend-code",
            "Backend code",
            "Engineering",
            ["golden filing corpus", "source-law snapshot", "auditability"],
            ["backend golden corpus", "statutory rules coverage", "production auditability controls"],
            ["qualified-accountant-signoff", "external-ros-validation", "accountant-acceptance-walkthrough"]);

        AssertCompletionTrack(
            tracks,
            "frontend-ui-ux",
            "Frontend UI/UX",
            "Product design",
            ["accountant workflow rail", "light/dark visual regression", "dense review workbench"],
            ["visual QA route audit", "dashboard filing deep links", "period filing gate snapshot", "route-level loading/error states", "permission-denied filing action state", "workbench primitives"],
            ["light-dark-visual-regression", "accountant-acceptance-walkthrough"]);

        AssertCompletionTrack(
            tracks,
            "frontend-code",
            "Frontend code",
            "Frontend engineering",
            ["shared workbench primitives", "typed API contract", "route-level states"],
            ["API client invariants", "component-preview route", "render tests", "FilingReviewCentre permission gate", "PeriodFilingWorkspace extraction", "PeriodImportWorkspace extraction", "PeriodCategoriseWorkspace extraction", "PeriodYearEndWorkspace extraction", "PeriodAdjustmentsWorkspace extraction", "PeriodStatementsWorkspace extraction"],
            ["light-dark-visual-regression"]);

        Assert.All(tracks, track =>
        {
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(track, "Label")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(track, "OwnerRole")));
            Assert.Contains(StringProperty(track, "Status"), new[] { "complete", "in-progress", "blocked", "review-required" });
            Assert.NotEmpty(StringListProperty(track, "CompletionCriteria"));
            Assert.NotEmpty(StringListProperty(track, "CurrentEvidence"));
            Assert.NotEmpty(StringListProperty(track, "NextActions"));
            Assert.NotEmpty(StringListProperty(track, "AssuranceActionCodes"));
            Assert.All(StringListProperty(track, "AssuranceActionCodes"), code => Assert.Contains(code, assuranceActionCodes));
        });

        Assert.Contains(report.AssurancePacket.EvidenceItems, item => item == "production-completion-map");
    }

    [Fact]
    public async Task ProductionReadinessReport_ExposesGoalScorecardMappedToReleaseBlockers()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();

        Assert.NotNull(report.ProductionScorecard);
        Assert.Equal(522, report.ProductionScorecard.CurrentScore);
        Assert.Equal(700, report.ProductionScorecard.TargetScore);
        Assert.Equal("review-required", report.ProductionScorecard.Status);
        Assert.Contains("source-law", report.ProductionScorecard.NextGate, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("visual QA", report.ProductionScorecard.NextGate, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("qualified-accountant", report.ProductionScorecard.NextGate, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("production-scorecard", report.AssurancePacket.EvidenceItems);

        var categories = report.ProductionScorecard.Categories;
        Assert.Equal(
            new[]
            {
                "architecture-documentation",
                "backend-statutory-accounting-engine",
                "frontend-accountant-workbench",
                "security-auth-tenant-platform-guardrails"
            },
            categories.Select(category => category.Code));

        var scores = categories.ToDictionary(category => category.Code);
        Assert.Equal((97, 100), (scores["architecture-documentation"].CurrentScore, scores["architecture-documentation"].TargetScore));
        Assert.Equal((185, 250), (scores["backend-statutory-accounting-engine"].CurrentScore, scores["backend-statutory-accounting-engine"].TargetScore));
        Assert.Equal((135, 200), (scores["frontend-accountant-workbench"].CurrentScore, scores["frontend-accountant-workbench"].TargetScore));
        Assert.Equal((105, 150), (scores["security-auth-tenant-platform-guardrails"].CurrentScore, scores["security-auth-tenant-platform-guardrails"].TargetScore));
        Assert.Contains(scores["architecture-documentation"].CurrentEvidence, evidence =>
            evidence.Contains("verify-release-evidence.ps1", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(scores["architecture-documentation"].CurrentEvidence, evidence =>
            evidence.Contains("source-law", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(scores["architecture-documentation"].RemainingGaps, gap =>
            gap.Contains("release-evidence-report.json", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(scores["architecture-documentation"].RemainingGaps, gap =>
            gap.Contains("source-law-review-template.md", StringComparison.OrdinalIgnoreCase));

        var trackCodes = report.CompletionTracks.Select(track => track.Code).ToHashSet(StringComparer.Ordinal);
        var blockerCodes = report.ReleaseBlockerRegister.Select(blocker => blocker.Code).ToHashSet(StringComparer.Ordinal);

        Assert.All(categories, category =>
        {
            Assert.InRange(category.CurrentScore, 0, category.TargetScore);
            Assert.NotEmpty(category.CurrentEvidence);
            Assert.NotEmpty(category.RemainingGaps);
            Assert.All(category.CompletionTrackCodes, trackCode => Assert.Contains(trackCode, trackCodes));
            Assert.All(category.ReleaseBlockerCodes, blockerCode => Assert.Contains(blockerCode, blockerCodes));
        });

        Assert.Contains("backend-code:production-monitoring", scores["security-auth-tenant-platform-guardrails"].ReleaseBlockerCodes);
        Assert.Contains(scores["security-auth-tenant-platform-guardrails"].CurrentEvidence, evidence =>
            evidence.Contains("verify-no-direct-filing-submission.ps1", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(scores["security-auth-tenant-platform-guardrails"].RemainingGaps, gap =>
            gap.Contains("no-direct-filing-submission-report.json", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(scores["frontend-accountant-workbench"].RemainingGaps, gap =>
            gap.Contains("visual QA", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(scores["frontend-accountant-workbench"].CurrentEvidence, evidence =>
            evidence.Contains("visual-smoke-evidence-report.json", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(scores["backend-statutory-accounting-engine"].RemainingGaps, gap =>
            gap.Contains("qualified-accountant", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(scores["backend-statutory-accounting-engine"].CurrentEvidence, evidence =>
            evidence.Contains("canonical golden corpus scenario codes", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(scores["backend-statutory-accounting-engine"].CurrentEvidence, evidence =>
            evidence.Contains("External ROS/iXBRL validation evidence", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(scores["backend-statutory-accounting-engine"].CurrentEvidence, evidence =>
            evidence.Contains("Source-law review evidence", StringComparison.OrdinalIgnoreCase));
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
                "AccountsWorkflowTests.GoldenPath_SmallAuditExemptCompany_MixedAccrualSetBalancesThroughPdfAndIxbrl",
                "FilingGoldenCorpusScenarioTests.GoldenCorpus_SmallAbridgedLtd_EmitsFullAccountsAbridgedCroPackIxbrlAndReadiness"
            ],
            ["dac-small"] =
            [
                "FilingGoldenCorpusScenarioTests.GoldenCorpus_DacSmall_EmitsAccountsIxbrlAndSourceBackedReadiness"
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
    public async Task ProductionReadinessReport_ExposesFlattenedGoldenVerifierManifestForReleaseEvidence()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();
        var manifestProperty = report.GetType().GetProperty("GoldenVerifierManifest");

        Assert.NotNull(manifestProperty);
        var manifest = Assert.IsAssignableFrom<System.Collections.IEnumerable>(manifestProperty!.GetValue(report))
            .Cast<object>()
            .ToArray();
        var expectedEntries = report.GoldenFilingCorpus.Sum(scenario => scenario.EvidenceVerifiers.Count);

        Assert.Equal(expectedEntries, manifest.Length);
        Assert.Contains(report.AssurancePacket.EvidenceItems, item => item == "golden-verifier-manifest");

        foreach (var scenario in report.GoldenFilingCorpus)
        {
            var scenarioEntries = manifest
                .Where(entry => StringProperty(entry, "ScenarioCode") == scenario.Code)
                .ToArray();
            var ledgerEntry = Assert.Single(report.GoldenEvidenceLedger, entry => entry.ScenarioCode == scenario.Code);

            Assert.Equal(scenario.EvidenceVerifiers.Count, scenarioEntries.Length);
            Assert.Equal(
                scenario.EvidenceTestNames.Order(StringComparer.Ordinal),
                scenarioEntries.Select(entry => StringProperty(entry, "VerifierName")).Order(StringComparer.Ordinal));
            Assert.All(scenarioEntries, entry =>
            {
                var verifier = Assert.Single(scenario.EvidenceVerifiers, item => item.Name == StringProperty(entry, "VerifierName"));

                Assert.Equal(scenario.Label, StringProperty(entry, "ScenarioLabel"));
                Assert.Equal(scenario.ExpectedOutcome, StringProperty(entry, "ExpectedOutcome"));
                Assert.Equal(scenario.CoverageStatus, StringProperty(entry, "CoverageStatus"));
                Assert.Equal(verifier.Command, StringProperty(entry, "Command"));
                Assert.Equal(verifier.CiScope, StringProperty(entry, "CiScope"));
                Assert.Equal(verifier.RunsInDefaultCi, BooleanProperty(entry, "RunsInDefaultCi"));
                Assert.Equal(verifier.EvidenceLevel, StringProperty(entry, "EvidenceLevel"));
                Assert.True(BooleanProperty(entry, "BlocksRelease"));
                Assert.Equal(scenario.EvidencePack.OutputArtifacts.Order(StringComparer.Ordinal), StringListProperty(entry, "OutputArtifacts").Order(StringComparer.Ordinal));
                Assert.Equal(scenario.EvidencePack.DecisionGates.Order(StringComparer.Ordinal), StringListProperty(entry, "DecisionGates").Order(StringComparer.Ordinal));
                Assert.Equal(scenario.EvidencePack.ExpectedProofPoints.Select(proof => proof.Area).Order(StringComparer.Ordinal), StringListProperty(entry, "ProofPointAreas").Order(StringComparer.Ordinal));
                Assert.Contains(StringProperty(entry, "Command"), ledgerEntry.AutomatedVerifierCommands);
                AssertGoldenEvidenceTestExists(StringProperty(entry, "VerifierName"));
            });
        }
    }

    [Fact]
    public async Task ProductionReadinessReport_ExposesReleaseVerificationManifestForEveryProductionGate()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();
        var manifestProperty = report.GetType().GetProperty("ReleaseVerificationManifest");

        Assert.NotNull(manifestProperty);
        var manifest = Assert.IsAssignableFrom<System.Collections.IEnumerable>(manifestProperty!.GetValue(report))
            .Cast<object>()
            .ToArray();

        Assert.Contains(report.AssurancePacket.EvidenceItems, item => item == "release-verification-manifest");
        Assert.Contains(manifest, item =>
            StringProperty(item, "Code") == "backend-golden-corpus"
            && StringProperty(item, "OwnerRole") == "Engineering"
            && StringProperty(item, "Command").Contains("dotnet test Accounts.slnx", StringComparison.Ordinal)
            && StringProperty(item, "EvidenceArtifact") == "backend-test-results"
            && BooleanProperty(item, "RunsInDefaultCi")
            && BooleanProperty(item, "BlocksRelease"));
        Assert.Contains(manifest, item =>
            StringProperty(item, "Code") == "frontend-workbench-contract"
            && StringProperty(item, "Command") == "npm test"
            && BooleanProperty(item, "RunsInDefaultCi"));
        Assert.Contains(manifest, item =>
            StringProperty(item, "Code") == "visual-smoke-light-dark"
            && StringProperty(item, "Command").Contains("visual-smoke", StringComparison.OrdinalIgnoreCase)
            && StringProperty(item, "Command").Contains("verify-visual-smoke-artifacts", StringComparison.OrdinalIgnoreCase)
            && StringProperty(item, "Command").Contains("visual-smoke-evidence-report.json", StringComparison.OrdinalIgnoreCase)
            && StringProperty(item, "EvidenceArtifact") == "artifacts/visual-smoke"
            && BooleanProperty(item, "BlocksRelease"));
        Assert.Contains(manifest, item =>
            StringProperty(item, "Code") == "qualified-accountant-final-signoff"
            && StringProperty(item, "CiScope") == "manual-release"
            && StringProperty(item, "ReleaseChecklistEvidenceArtifact") == "named-accountant-approval-record"
            && StringProperty(item, "ManualFallback").Contains("named qualified accountant", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(manifest, item =>
            StringProperty(item, "Code") == "external-ros-validation-evidence"
            && StringProperty(item, "CiScope") == "manual-release"
            && StringProperty(item, "ReleaseChecklistEvidenceArtifact") == "external-ros-validation-reference"
            && StringProperty(item, "ManualFallback").Contains("external validation", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(manifest, item =>
            StringProperty(item, "Code") == "no-direct-cro-ros-submission-control"
            && StringProperty(item, "CiScope") == "manual-release"
            && StringProperty(item, "ReleaseChecklistEvidenceArtifact") == "no-direct-cro-ros-submission-control"
            && StringProperty(item, "ManualFallback").Contains("recorded workflow states only", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(manifest, item =>
            StringProperty(item, "Code") == "production-stack-smoke"
            && StringProperty(item, "Command").Contains("smoke-production", StringComparison.OrdinalIgnoreCase)
            && BooleanProperty(item, "RunsInDefaultCi"));
        Assert.Contains(manifest, item =>
            StringProperty(item, "Code") == "backup-restore-drill"
            && StringProperty(item, "Command").Contains("verify-postgres-backup", StringComparison.OrdinalIgnoreCase)
            && StringProperty(item, "CiScope") == "default-ci");
        Assert.Contains(manifest, item =>
            StringProperty(item, "Code") == "postgres-gated-audit-tests"
            && StringProperty(item, "CiScope") == "environment-gated"
            && !BooleanProperty(item, "RunsInDefaultCi")
            && StringProperty(item, "ManualFallback").Contains("ACCOUNTS_POSTGRES_TEST_CONNECTION", StringComparison.Ordinal));

        var checklistArtifactCodes = report.ReleaseReviewChecklist
            .Select(item => item.EvidenceArtifact)
            .ToHashSet(StringComparer.Ordinal);
        var manifestChecklistArtifacts = manifest
            .Select(item => StringProperty(item, "ReleaseChecklistEvidenceArtifact"))
            .ToHashSet(StringComparer.Ordinal);
        var missingBlockingChecklistArtifacts = report.ReleaseReviewChecklist
            .Where(item => item.BlocksRelease)
            .Select(item => item.EvidenceArtifact)
            .Where(artifact => !manifestChecklistArtifacts.Contains(artifact))
            .ToArray();

        Assert.Empty(missingBlockingChecklistArtifacts);
        Assert.All(manifest, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(item, "Code")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(item, "Label")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(item, "OwnerRole")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(item, "Command")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(item, "CiScope")));
            Assert.Contains(StringProperty(item, "CiScope"), new[] { "default-ci", "environment-gated", "manual-release" });
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(item, "EvidenceArtifact")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(item, "ManualFallback")));
            Assert.Contains(StringProperty(item, "ReleaseChecklistEvidenceArtifact"), checklistArtifactCodes);
        });
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
        var microScenario = Assert.Single(report.GoldenFilingCorpus, scenario => scenario.Code == "micro-ltd");
        Assert.Contains(
            "FilingGoldenCorpusScenarioTests.GoldenCorpus_MicroLtd_EmitsAccountsIxbrlTaxNotesReadinessAndSignatoryGates",
            microScenario.EvidenceTestNames);

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
            "dac-small",
            expectedArtifacts:
            [
                "DAC accounts PDF text",
                "directors' report evidence",
                "CRO signature page",
                "iXBRL XML",
                "tax computation",
                "notes disclosure set",
                "filing readiness profile",
                "accountant sign-off packet"
            ],
            expectedGates:
            [
                "DAC company type",
                "director and secretary certification",
                "named qualified-accountant review",
                "external ROS/iXBRL validation"
            ],
            expectedValueChecks:
            [
                "Small regime",
                "DAC source-backed readiness",
                "well-formed iXBRL"
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
    public async Task ProductionReadinessReport_ExposesGoldenEvidenceLedgerForAccountantReview()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();
        var ledgerProperty = report.GetType().GetProperty("GoldenEvidenceLedger");

        Assert.NotNull(ledgerProperty);
        var ledger = Assert.IsAssignableFrom<System.Collections.IEnumerable>(ledgerProperty!.GetValue(report))
            .Cast<object>()
            .ToArray();

        Assert.Equal(
            report.GoldenFilingCorpus.Select(scenario => scenario.Code).Order(StringComparer.Ordinal),
            ledger.Select(entry => StringProperty(entry, "ScenarioCode")).Order(StringComparer.Ordinal));
        Assert.Contains(report.AssurancePacket.EvidenceItems, item => item == "golden-evidence-ledger");

        foreach (var scenario in report.GoldenFilingCorpus)
        {
            var entry = Assert.Single(ledger, item => StringProperty(item, "ScenarioCode") == scenario.Code);
            var acceptance = Assert.Single(report.AccountantAcceptanceCriteria, item => item.ScenarioCode == scenario.Code);

            Assert.Equal(scenario.Label, StringProperty(entry, "Label"));
            Assert.Equal(scenario.Fixture.LegalName, StringProperty(entry, "FixtureLegalName"));
            Assert.Equal(scenario.Fixture.CompanyType, StringProperty(entry, "CompanyType"));
            Assert.Equal(scenario.ExpectedOutcome, StringProperty(entry, "ExpectedOutcome"));
            Assert.Equal(scenario.CoverageStatus, StringProperty(entry, "CoverageStatus"));
            Assert.Equal(acceptance.AcceptanceStatus, StringProperty(entry, "AcceptanceStatus"));
            Assert.Equal(acceptance.RequiredSignOffGate, StringProperty(entry, "RequiredSignOffGate"));
            Assert.True(BooleanProperty(entry, "BlocksRelease"));
            Assert.Equal(scenario.EvidenceTestNames.Order(StringComparer.Ordinal), StringListProperty(entry, "AutomatedVerifierNames").Order(StringComparer.Ordinal));
            Assert.Equal(scenario.EvidenceVerifiers.Select(verifier => verifier.Command).Order(StringComparer.Ordinal), StringListProperty(entry, "AutomatedVerifierCommands").Order(StringComparer.Ordinal));
            Assert.Equal(scenario.EvidenceVerifiers.Select(verifier => verifier.CiScope).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal), StringListProperty(entry, "CiScopes").Order(StringComparer.Ordinal));
            Assert.Equal(scenario.EvidenceVerifiers.Select(verifier => verifier.EvidenceLevel).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal), StringListProperty(entry, "EvidenceLevels").Order(StringComparer.Ordinal));
            Assert.Equal(scenario.EvidencePack.OutputArtifacts.Order(StringComparer.Ordinal), StringListProperty(entry, "OutputArtifacts").Order(StringComparer.Ordinal));
            Assert.Equal(scenario.EvidencePack.DecisionGates.Order(StringComparer.Ordinal), StringListProperty(entry, "DecisionGates").Order(StringComparer.Ordinal));
            Assert.Equal(scenario.EvidencePack.ExpectedValueChecks.Order(StringComparer.Ordinal), StringListProperty(entry, "ExpectedValueChecks").Order(StringComparer.Ordinal));
            Assert.Equal(
                scenario.EvidencePack.ExpectedProofPoints.Select(proof => proof.Area).Order(StringComparer.Ordinal),
                StringListProperty(entry, "ProofPointAreas").Order(StringComparer.Ordinal));
            Assert.Equal(
                scenario.EvidencePack.SourceReferences.Select(source => source.SourceId).Order(StringComparer.Ordinal),
                StringListProperty(entry, "SourceIds").Order(StringComparer.Ordinal));
            Assert.Equal(scenario.EvidencePack.ExpectedOutputs.ExpectedCorporationTax, DecimalProperty(entry, "ExpectedCorporationTax"));
            Assert.Equal(scenario.EvidencePack.ExpectedOutputs.FilingReadinessState, StringProperty(entry, "FilingReadinessState"));
            Assert.Equal(scenario.EvidencePack.ExpectedOutputs.SignOffPacketState, StringProperty(entry, "SignOffPacketState"));
        }

        Assert.Contains(ledger, entry =>
            StringProperty(entry, "ScenarioCode") == "medium-audit-required"
            && StringProperty(entry, "AcceptanceStatus") == "manual-handoff-review-required"
            && StringListProperty(entry, "DecisionGates").Any(gate => gate.Contains("auditor", StringComparison.OrdinalIgnoreCase)));
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
        AssertGoldenFixture(report, "dac-small", "Atlantic Manufacturing DAC", "DesignatedActivityCompany", "2026-01-01", "2026-12-31", "Small", "Small", auditExempt: true, manualReviewRequired: false);
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
            expectedTax: 62.50m,
            readinessState: "100% filing readiness",
            signOffState: "review-required");
        AssertGoldenExpectedOutputs(
            report,
            "small-abridged-ltd",
            expectedPdfMarker: "Section 352",
            expectedIxbrlTag: "core:EntityCurrentLegalOrRegisteredName",
            expectedTax: 62.50m,
            readinessState: "generated-output-evidence-required",
            signOffState: "review-required");
        AssertGoldenExpectedOutputs(
            report,
            "dac-small",
            expectedPdfMarker: "Atlantic Manufacturing DAC",
            expectedIxbrlTag: "bus:EntityCurrentLegalOrRegisteredName",
            expectedTax: 62.50m,
            readinessState: "ready-for-external-filing",
            signOffState: "ready-for-external-filing");
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
    public async Task GoldenCorpusExpectedTaxValues_MirrorTheFormalFilingCorpusScenarioFixtures()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();
        var expectedTaxByScenario = new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            ["micro-ltd"] = 62.50m,
            ["small-abridged-ltd"] = 62.50m,
            ["dac-small"] = 62.50m,
            ["clg-charity"] = 62.50m,
            ["medium-audit-required"] = 62.50m
        };

        foreach (var scenario in report.GoldenFilingCorpus)
        {
            var expectedOutputs = ObjectProperty(scenario.EvidencePack, "ExpectedOutputs");

            Assert.True(expectedTaxByScenario.ContainsKey(scenario.Code), $"{scenario.Code} must have a pinned expected tax value.");
            Assert.Equal(expectedTaxByScenario[scenario.Code], DecimalProperty(expectedOutputs, "ExpectedCorporationTax"));
        }
    }

    [Fact]
    public async Task GoldenCorpusScenarios_ExposeLegalBasisSnapshotsForAccountantReview()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();

        foreach (var scenario in report.GoldenFilingCorpus)
        {
            var legalBasisProperty = scenario.GetType().GetProperty("LegalBasisSnapshot");
            Assert.NotNull(legalBasisProperty);
            var snapshot = legalBasisProperty!.GetValue(scenario)!;

            Assert.Equal(scenario.Code, StringProperty(snapshot, "ScenarioCode"));
            Assert.Equal(scenario.Fixture.CompanyType, StringProperty(snapshot, "CompanyType"));
            Assert.Equal(scenario.Fixture.ExpectedSizeClass, StringProperty(snapshot, "SizeClass"));
            Assert.Equal(scenario.Fixture.ExpectedRegime, StringProperty(snapshot, "ElectedRegime"));
            Assert.Equal(scenario.Fixture.AuditExempt, BooleanProperty(snapshot, "AuditExempt"));
            Assert.Equal(scenario.Fixture.ManualProfessionalReviewRequired, BooleanProperty(snapshot, "ManualProfessionalReviewRequired"));
            Assert.NotEmpty(StringListProperty(snapshot, "RequiredOutputs"));
            Assert.NotEmpty(StringListProperty(snapshot, "ProfessionalGates"));
            Assert.NotEmpty(StringListProperty(snapshot, "SourceIds"));
            Assert.Equal(
                scenario.EvidencePack.OutputArtifacts.Order(StringComparer.Ordinal),
                StringListProperty(snapshot, "RequiredOutputs").Order(StringComparer.Ordinal));
            Assert.Equal(
                scenario.EvidencePack.DecisionGates.Order(StringComparer.Ordinal),
                StringListProperty(snapshot, "ProfessionalGates").Order(StringComparer.Ordinal));
            Assert.Equal(
                scenario.EvidencePack.SourceReferences.Select(source => source.SourceId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal),
                StringListProperty(snapshot, "SourceIds").Order(StringComparer.Ordinal));
        }

        AssertGoldenLegalBasis(
            report,
            "micro-ltd",
            expectedBasis: "FRS 105 micro-entities regime with CRO financial-statement and Revenue iXBRL filing evidence.",
            expectedSourceIds:
            [
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements.SourceId,
                IrishStatutoryRuleSources.FrcFrs105.SourceId,
                IrishStatutoryRuleSources.RevenueAcceptedTaxonomies.SourceId
            ]);
        AssertGoldenLegalBasis(
            report,
            "small-abridged-ltd",
            expectedBasis: "FRS 102 small-company abridgement with Section 352 CRO filing evidence and Revenue iXBRL evidence.",
            expectedSourceIds:
            [
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements.SourceId,
                IrishStatutoryRuleSources.FrcFrs102.SourceId,
                IrishStatutoryRuleSources.RevenueAcceptedTaxonomies.SourceId
            ]);
        AssertGoldenLegalBasis(
            report,
            "clg-charity",
            expectedBasis: "CLG charity reporting path with CRO guarantee-company, Charities Regulator annual-report and FRS 102 evidence.",
            expectedSourceIds:
            [
                IrishStatutoryRuleSources.CroGuaranteeCompany.SourceId,
                IrishStatutoryRuleSources.CharitiesRegulatorAnnualReport.SourceId,
                IrishStatutoryRuleSources.FrcFrs102.SourceId
            ]);
        AssertGoldenLegalBasis(
            report,
            "medium-audit-required",
            expectedBasis: "Medium-company audit-required path blocked to manual handoff until auditor report and professional review evidence are present.",
            expectedSourceIds:
            [
                IrishStatutoryRuleSources.CroMediumCompany.SourceId,
                IrishStatutoryRuleSources.CroAuditorsReport.SourceId,
                IrishStatutoryRuleSources.FrcFrs102.SourceId
            ]);
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
    public async Task ProductionReadinessReport_ExposesOperationsEvidencePackForReleaseReview()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();
        var packProperty = report.GetType().GetProperty("OperationsEvidencePack");

        Assert.NotNull(packProperty);
        var pack = Assert.IsAssignableFrom<System.Collections.IEnumerable>(packProperty!.GetValue(report))
            .Cast<object>()
            .ToArray();

        Assert.Contains(report.AssurancePacket.EvidenceItems, item => item == "operations-evidence-pack");
        Assert.Contains(pack, item =>
            StringProperty(item, "Code") == "sentry-error-routing"
            && StringProperty(item, "Category") == "Monitoring"
            && StringProperty(item, "RequiredArtifact") == "sentry-production-error-routing-check"
            && StringProperty(item, "Command").Contains("Monitoring:ErrorTrackingDsn", StringComparison.Ordinal)
            && StringProperty(item, "FailurePolicy").Contains("Block release", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(pack, item =>
            StringProperty(item, "Code") == "structured-log-correlation"
            && StringProperty(item, "RequiredArtifact") == "structured-json-log-sample"
            && StringProperty(item, "Verification").Contains("correlation", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(pack, item =>
            StringProperty(item, "Code") == "dependency-audit"
            && StringProperty(item, "Command").Contains("npm audit", StringComparison.OrdinalIgnoreCase)
            && StringProperty(item, "Command").Contains("write-dependency-evidence", StringComparison.OrdinalIgnoreCase)
            && StringProperty(item, "RequiredArtifact") == "dependency-audit-release"
            && StringProperty(item, "Verification").Contains("dependency-audit-report.json", StringComparison.OrdinalIgnoreCase)
            && StringProperty(item, "ReleaseGateCode") == "dependency-policy-controls");
        Assert.Contains(pack, item =>
            StringProperty(item, "Code") == "migration-safety"
            && StringProperty(item, "Command").Contains("verify-production-compose-images", StringComparison.Ordinal)
            && StringProperty(item, "Command").Contains("production-safety-report.json", StringComparison.Ordinal)
            && StringProperty(item, "RequiredArtifact") == "production-safety-config"
            && StringProperty(item, "Verification").Contains("--migrate-only", StringComparison.Ordinal)
            && StringProperty(item, "Verification").Contains("AutoMigrateOnStartup", StringComparison.Ordinal));
        Assert.Contains(pack, item =>
            StringProperty(item, "Code") == "production-seed-block"
            && StringProperty(item, "Command").Contains("production-safety-report.json", StringComparison.Ordinal)
            && StringProperty(item, "RequiredArtifact") == "production-safety-config"
            && StringProperty(item, "Verification").Contains("SeedDemoData", StringComparison.Ordinal)
            && StringProperty(item, "Verification").Contains("bootstrap owner initial password", StringComparison.OrdinalIgnoreCase)
            && StringProperty(item, "FailurePolicy").Contains("demo seed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(pack, item =>
            StringProperty(item, "Code") == "backup-restore-drill"
            && StringProperty(item, "Command").Contains("verify-postgres-backup", StringComparison.Ordinal)
            && StringProperty(item, "RequiredArtifact") == "postgres-backup-restore-drill-report");

        Assert.All(pack, item =>
        {
            Assert.True(BooleanProperty(item, "Required"));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(item, "Code")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(item, "Label")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(item, "Category")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(item, "OwnerRole")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(item, "Command")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(item, "RequiredArtifact")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(item, "ReleaseGateCode")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(item, "Verification")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(item, "FailurePolicy")));
        });
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
        Assert.Contains(checklist, item =>
            StringProperty(item, "Code") == "no-direct-cro-ros-submission"
            && StringProperty(item, "OwnerRole") == "Engineering"
            && StringProperty(item, "AssuranceActionCode") == "no-direct-cro-ros-submission"
            && StringProperty(item, "OperationalGateCode") == "no-direct-cro-ros-submission"
            && StringProperty(item, "EvidenceArtifact") == "no-direct-cro-ros-submission-control"
            && StringProperty(item, "Status") == "complete");

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
    public async Task ProductionReadinessReport_ExposesReleaseBlockerRegisterMappedToCompletionTracks()
    {
        await using var db = CreateDbContext();
        var report = await new ProductionReadinessReportService(db).GetReportAsync();
        var blockerRegisterProperty = report.GetType().GetProperty("ReleaseBlockerRegister");

        Assert.NotNull(blockerRegisterProperty);
        var blockerRegister = Assert.IsAssignableFrom<System.Collections.IEnumerable>(blockerRegisterProperty!.GetValue(report))
            .Cast<object>()
            .ToArray();

        Assert.NotEmpty(blockerRegister);
        Assert.Contains(report.AssurancePacket.EvidenceItems, item => item == "release-blocker-register");
        Assert.Equal(
            report.AssurancePacket.ReleaseBlockers.Order(StringComparer.Ordinal),
            blockerRegister
                .Select(item => StringProperty(item, "BlockingIssue"))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal));

        var trackCodes = report.CompletionTracks.Select(track => track.Code).ToHashSet(StringComparer.Ordinal);
        var assuranceActionCodes = report.AssuranceActions.Select(action => action.Code).ToHashSet(StringComparer.Ordinal);
        var checklistCodes = report.ReleaseReviewChecklist.Select(item => item.Code).ToHashSet(StringComparer.Ordinal);

        Assert.Contains(blockerRegister, blocker =>
            StringProperty(blocker, "TrackCode") == "backend-code"
            && StringProperty(blocker, "SourceActionCode") == "qualified-accountant-signoff"
            && StringProperty(blocker, "ReleaseChecklistCode") == "accountant-final-signoff"
            && StringProperty(blocker, "EvidenceArtifact") == "named-accountant-approval-record"
            && StringProperty(blocker, "BlockingIssue") == "Qualified accountant sign-off required"
            && BooleanProperty(blocker, "BlocksRelease"));
        Assert.Contains(blockerRegister, blocker =>
            StringProperty(blocker, "TrackCode") == "frontend-ui-ux"
            && StringProperty(blocker, "SourceActionCode") == "light-dark-visual-regression"
            && StringProperty(blocker, "ReleaseChecklistCode") == "visual-qa-screenshot-review"
            && StringProperty(blocker, "EvidenceArtifact") == "light-dark-desktop-mobile-screenshot-review"
            && StringProperty(blocker, "RequiredEvidence").Contains("screenshot", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(blockerRegister, blocker =>
            StringProperty(blocker, "TrackCode") == "frontend-code"
            && StringProperty(blocker, "SourceActionCode") == "light-dark-visual-regression");

        Assert.All(blockerRegister, blocker =>
        {
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(blocker, "Code")));
            Assert.Contains(StringProperty(blocker, "TrackCode"), trackCodes);
            Assert.Contains(StringProperty(blocker, "SourceActionCode"), assuranceActionCodes);
            Assert.Contains(StringProperty(blocker, "ReleaseChecklistCode"), checklistCodes);
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(blocker, "OwnerRole")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(blocker, "Severity")));
            Assert.True(IntProperty(blocker, "RiskRank") >= 0);
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(blocker, "BlockingIssue")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(blocker, "RequiredEvidence")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(blocker, "EvidenceArtifact")));
            Assert.False(string.IsNullOrWhiteSpace(StringProperty(blocker, "NextAction")));
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
        Assert.Equal("visual-smoke-manifest.json", StringProperty(report.VisualQaCoverage, "ManifestFileName"));
        Assert.Equal(28, report.VisualQaCoverage.ExpectedScreenshotCount);
        var artifacts = ObjectListProperty(report.VisualQaCoverage, "Artifacts");
        var routeAudits = ObjectListProperty(report.VisualQaCoverage, "RouteAudits");
        var reviewProtocol = ObjectProperty(report.VisualQaCoverage, "ReviewProtocol");
        Assert.Equal(report.VisualQaCoverage.ExpectedScreenshotCount, artifacts.Count);
        Assert.Equal(report.VisualQaCoverage.Routes.Count, routeAudits.Count);
        Assert.Equal("visual-review-v1", StringProperty(reviewProtocol, "ProtocolVersion"));
        Assert.Equal("Design reviewer", StringProperty(reviewProtocol, "ReviewerRole"));
        Assert.Equal("required-review", StringProperty(reviewProtocol, "Status"));
        Assert.Equal("visual-qa-screenshot-review", StringProperty(reviewProtocol, "SignOffGate"));
        Assert.Contains("Block release", StringProperty(reviewProtocol, "FailurePolicy"));
        AssertListContainsAll(
            StringListProperty(reviewProtocol, "AcceptanceCriteria"),
            [
                "light desktop",
                "horizontal overflow",
                "table scanability",
                "named visual QA reviewer"
            ],
            "visual-qa",
            "acceptance criteria");
        AssertListContainsAll(
            StringListProperty(reviewProtocol, "RequiredEvidence"),
            [
                "visual-smoke-manifest.json",
                "visual-smoke-evidence-report.json",
                "28 visual smoke screenshots",
                "screenshot SHA-256 checksums",
                "route audit summary",
                "named visual QA reviewer sign-off"
            ],
            "visual-qa",
            "required evidence");
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
            route.Code == "financial-statements" && route.RequiredText == "Financial Statements");
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
            var routeAudit = Assert.Single(routeAudits, audit =>
                StringProperty(audit, "RouteCode") == route.Code
                && StringProperty(audit, "RouteKey") == route.RouteKey);
            Assert.Equal(route.Label, StringProperty(routeAudit, "Label"));
            Assert.Equal(report.VisualQaCoverage.Themes.Count * report.VisualQaCoverage.Viewports.Count, IntProperty(routeAudit, "ScreenshotCount"));
            Assert.Equal("required-review", StringProperty(routeAudit, "ReviewStatus"));
            Assert.Equal(route.WorkflowStages, StringListProperty(routeAudit, "WorkflowStages"));
            Assert.Contains("accountant-workflow-hierarchy", StringListProperty(routeAudit, "ReviewChecks"));
            Assert.Contains("table-scanability", StringListProperty(routeAudit, "ReviewChecks"));
            Assert.Contains("theme-contrast", StringListProperty(routeAudit, "ReviewChecks"));
            Assert.Contains("mobile-density", StringListProperty(routeAudit, "ReviewChecks"));
            Assert.Contains("loading-error-empty-states", StringListProperty(routeAudit, "ReviewChecks"));
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
            ["financial-statements"] = "financialStatements",
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

    private static void AssertGoldenLegalBasis(
        ProductionReadinessReport report,
        string scenarioCode,
        string expectedBasis,
        IReadOnlyList<string> expectedSourceIds)
    {
        var scenario = Assert.Single(report.GoldenFilingCorpus, s => s.Code == scenarioCode);
        var legalBasisProperty = scenario.GetType().GetProperty("LegalBasisSnapshot");
        Assert.NotNull(legalBasisProperty);
        var snapshot = legalBasisProperty!.GetValue(scenario)!;

        Assert.Equal(expectedBasis, StringProperty(snapshot, "LegalBasis"));
        Assert.Contains(StringListProperty(snapshot, "SourceIds"), sourceId => expectedSourceIds.Contains(sourceId));
        Assert.All(expectedSourceIds, sourceId =>
            Assert.Contains(sourceId, StringListProperty(snapshot, "SourceIds")));
    }

    private static void AssertCompletionTrack(
        IReadOnlyList<object> tracks,
        string code,
        string label,
        string ownerRole,
        IReadOnlyList<string> expectedCriteria,
        IReadOnlyList<string> expectedEvidence,
        IReadOnlyList<string> expectedAssuranceActions)
    {
        var track = Assert.Single(tracks, item => StringProperty(item, "Code") == code);

        Assert.Equal(label, StringProperty(track, "Label"));
        Assert.Equal(ownerRole, StringProperty(track, "OwnerRole"));
        AssertListContainsAll(StringListProperty(track, "CompletionCriteria"), expectedCriteria, code, "completion criteria");
        AssertListContainsAll(StringListProperty(track, "CurrentEvidence"), expectedEvidence, code, "current evidence");
        AssertListContainsAll(StringListProperty(track, "AssuranceActionCodes"), expectedAssuranceActions, code, "assurance actions");
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

    private static void AssertRevenueTaxonomyRange(
        IReadOnlyList<object> ranges,
        string taxonomyKey,
        string accountingStandard,
        string taxonomyDate,
        string effectiveFrom,
        string effectiveBefore,
        string schemaRefFragment)
    {
        var range = Assert.Single(ranges, item => StringProperty(item, "TaxonomyKey") == taxonomyKey);
        Assert.Equal(accountingStandard, StringProperty(range, "AccountingStandard"));
        Assert.Equal(taxonomyDate, StringProperty(range, "TaxonomyDate"));
        Assert.Equal(effectiveFrom, StringProperty(range, "EffectiveForPeriodsStartingOnOrAfter"));
        Assert.Equal(effectiveBefore, StringProperty(range, "EffectiveForPeriodsStartingBefore"));
        Assert.Contains(schemaRefFragment, StringProperty(range, "SchemaRef"), StringComparison.Ordinal);
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

    private static int IntProperty(object value, string propertyName)
    {
        var property = value.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        return Assert.IsType<int>(property!.GetValue(value));
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
