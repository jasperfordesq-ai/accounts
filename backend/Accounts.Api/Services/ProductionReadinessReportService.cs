using Accounts.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Accounts.Api.Services;

public sealed record ProductionReadinessArea(
    string Code,
    string Label,
    string Status,
    string Detail);

public sealed record GoldenFilingCorpusEvidencePack(
    IReadOnlyList<string> OutputArtifacts,
    IReadOnlyList<string> DecisionGates,
    IReadOnlyList<string> ExpectedValueChecks,
    GoldenFilingCorpusExpectedOutputs ExpectedOutputs,
    IReadOnlyList<GoldenFilingCorpusProofPoint> ExpectedProofPoints,
    IReadOnlyList<LegalSourceReference> SourceReferences);

public sealed record GoldenFilingCorpusExpectedOutputs(
    IReadOnlyList<string> PdfTextMarkers,
    IReadOnlyList<string> IxbrlRequiredTags,
    string FilingReadinessState,
    decimal ExpectedCorporationTax,
    IReadOnlyList<string> RequiredNotes,
    IReadOnlyList<string> FilingGateStates,
    string SignOffPacketState);

public sealed record GoldenFilingCorpusProofPoint(
    string Area,
    string ExpectedEvidence,
    string AutomatedVerifier,
    bool Required);

public sealed record GoldenFilingCorpusFixture(
    string LegalName,
    string CompanyType,
    string PeriodStart,
    string PeriodEnd,
    string ExpectedSizeClass,
    string ExpectedRegime,
    bool AuditExempt,
    bool ManualProfessionalReviewRequired);

public sealed record GoldenFilingCorpusVerifier(
    string Name,
    string Command,
    string CiScope,
    bool RunsInDefaultCi,
    string Environment,
    string EvidenceLevel);

public sealed record GoldenFilingCorpusLegalBasisSnapshot(
    string ScenarioCode,
    string CompanyType,
    string SizeClass,
    string ElectedRegime,
    bool AuditExempt,
    bool ManualProfessionalReviewRequired,
    string LegalBasis,
    IReadOnlyList<string> RequiredOutputs,
    IReadOnlyList<string> ProfessionalGates,
    IReadOnlyList<string> SourceIds);

public sealed record GoldenFilingCorpusScenario(
    string Code,
    string Label,
    string CompanyScope,
    string ExpectedOutcome,
    string CoverageStatus,
    GoldenFilingCorpusFixture Fixture,
    IReadOnlyList<string> EvidenceTestNames,
    IReadOnlyList<GoldenFilingCorpusVerifier> EvidenceVerifiers,
    IReadOnlyList<string> Assertions,
    GoldenFilingCorpusEvidencePack EvidencePack,
    GoldenFilingCorpusLegalBasisSnapshot LegalBasisSnapshot);

public sealed record GoldenEvidenceLedgerEntry(
    string ScenarioCode,
    string Label,
    string FixtureLegalName,
    string CompanyType,
    string ExpectedOutcome,
    string CoverageStatus,
    string AcceptanceStatus,
    string RequiredSignOffGate,
    bool BlocksRelease,
    IReadOnlyList<string> AutomatedVerifierNames,
    IReadOnlyList<string> AutomatedVerifierCommands,
    IReadOnlyList<string> CiScopes,
    IReadOnlyList<string> EvidenceLevels,
    IReadOnlyList<string> OutputArtifacts,
    IReadOnlyList<string> DecisionGates,
    IReadOnlyList<string> ExpectedValueChecks,
    IReadOnlyList<string> ProofPointAreas,
    IReadOnlyList<string> SourceIds,
    decimal ExpectedCorporationTax,
    string FilingReadinessState,
    string SignOffPacketState);

public sealed record GoldenVerifierManifestEntry(
    string ScenarioCode,
    string ScenarioLabel,
    string ExpectedOutcome,
    string CoverageStatus,
    string VerifierName,
    string Command,
    string CiScope,
    bool RunsInDefaultCi,
    string EvidenceLevel,
    bool BlocksRelease,
    IReadOnlyList<string> OutputArtifacts,
    IReadOnlyList<string> DecisionGates,
    IReadOnlyList<string> ProofPointAreas);

public sealed record SourceLawTraceabilityEntry(
    string SourceId,
    string Title,
    string EffectiveDate,
    string Url,
    bool InSnapshot,
    IReadOnlyList<string> UsedBy,
    IReadOnlyList<string> ReleaseGateCodes);

public sealed record SourceLawReviewLedgerEntry(
    string SourceId,
    string Title,
    string Url,
    string PinnedEffectiveDate,
    string OwnerRole,
    string ReleaseChecklistCode,
    bool BlocksRelease,
    IReadOnlyList<string> ReviewChecks,
    IReadOnlyList<string> RequiredEvidence);

public sealed record SourceLawMaintenanceProtocol(
    string ProtocolVersion,
    string OwnerRole,
    string Status,
    string ReviewCadence,
    string NextReviewDue,
    string SignOffGate,
    string ChangeDetection,
    string FailurePolicy,
    IReadOnlyList<string> MonitoredSourceIds,
    IReadOnlyList<string> AcceptanceCriteria,
    IReadOnlyList<string> RequiredEvidence);

public sealed record RevenueTaxonomyRangeEvidence(
    string TaxonomyKey,
    string AccountingStandard,
    string TaxonomyDate,
    string Label,
    string SchemaRef,
    bool AcceptedByRevenue,
    bool AutomatedPlatformSelectionSupported,
    string EffectiveForPeriodsStartingOnOrAfter,
    string EffectiveForPeriodsStartingBefore,
    IReadOnlyList<string> SourceIds,
    IReadOnlyList<string> ReleaseGateCodes);

public sealed record StatutoryRuleMatrixEntry(
    string Code,
    string CompanyScope,
    string SizeOrRegime,
    string SupportLevel,
    IReadOnlyList<string> RequiredEvidence,
    IReadOnlyList<string> RequiredOutputs,
    IReadOnlyList<string> ManualHandoffGates,
    IReadOnlyList<LegalSourceReference> Sources);

public sealed record StatutoryRulesCoverageItem(
    string Code,
    string RuleFamily,
    string DecisionUnderTest,
    string CoverageStatus,
    IReadOnlyList<string> AutomatedVerifierNames,
    IReadOnlyList<string> EdgeCases,
    IReadOnlyList<LegalSourceReference> Sources);

public sealed record OperationalGate(
    string Code,
    string Label,
    bool Required,
    string Status,
    string Detail);

public sealed record ProductionReadinessAssuranceAction(
    string Code,
    string Label,
    string Owner,
    string Priority,
    int RiskRank,
    string EvidenceStage,
    string Status,
    string Detail,
    string EvidenceRequired);

public sealed record ProductionReadinessCompletionTrack(
    string Code,
    string Label,
    string OwnerRole,
    string Status,
    IReadOnlyList<string> CompletionCriteria,
    IReadOnlyList<string> CurrentEvidence,
    IReadOnlyList<string> NextActions,
    IReadOnlyList<string> AssuranceActionCodes);

public sealed record ProductionReleaseBlocker(
    string Code,
    string TrackCode,
    string TrackLabel,
    string OwnerRole,
    string Severity,
    int RiskRank,
    string BlockingIssue,
    string RequiredEvidence,
    string NextAction,
    string SourceActionCode,
    string ReleaseChecklistCode,
    string OperationalGateCode,
    string EvidenceArtifact,
    bool BlocksRelease);

public sealed record ProductionAuditabilityControl(
    string Code,
    string Label,
    bool Required,
    string Enforcement,
    string EvidenceCaptured,
    string Verification,
    IReadOnlyList<string> AuditEventCodes);

public sealed record AuditEvidenceTimelineEntry(
    string Code,
    string Stage,
    string EvidenceQuestion,
    string CapturedWhen,
    string RequiredActor,
    string Verification,
    IReadOnlyList<string> AuditEventCodes,
    IReadOnlyList<string> BlockingGateCodes);

public sealed record ProductionAuditEvidencePackItem(
    string Code,
    string Label,
    string EvidenceQuestion,
    string RequiredArtifact,
    string RetainedIn,
    string RequiredActor,
    string CapturedWhen,
    string Verification,
    string FailurePolicy,
    IReadOnlyList<string> AuditEventCodes,
    IReadOnlyList<string> BlockingGateCodes);

public sealed record ProductionMonitoringControl(
    string Code,
    string Label,
    string Provider,
    bool Required,
    string ProductionSafetyGate,
    string EvidenceCaptured,
    string Verification,
    string AlertRoute,
    string FailurePolicy);

public sealed record DependencyPolicyControl(
    string Code,
    string Label,
    bool Required,
    string Enforcement,
    string EvidenceCaptured,
    string Verification,
    string FailurePolicy);

public sealed record DeploymentSafetyControl(
    string Code,
    string Label,
    bool Required,
    string Enforcement,
    string EvidenceCaptured,
    string Verification,
    string FailurePolicy);

public sealed record OperationsEvidencePackItem(
    string Code,
    string Label,
    string Category,
    string OwnerRole,
    bool Required,
    string Command,
    string RequiredArtifact,
    string ReleaseGateCode,
    string Verification,
    string FailurePolicy);

public sealed record ReleaseReviewChecklistItem(
    string Code,
    string Label,
    string OwnerRole,
    bool Required,
    string Status,
    bool BlocksRelease,
    string EvidenceArtifact,
    string AssuranceActionCode,
    string OperationalGateCode,
    IReadOnlyList<string> AuditEventCodes,
    string Detail);

public sealed record ReleaseVerificationManifestItem(
    string Code,
    string Label,
    string OwnerRole,
    string Command,
    string CiScope,
    bool RunsInDefaultCi,
    bool BlocksRelease,
    string EvidenceArtifact,
    string ReleaseChecklistEvidenceArtifact,
    string ManualFallback);

public sealed record AccountantAcceptanceCriterion(
    string ScenarioCode,
    string Label,
    bool Required,
    string AcceptanceStatus,
    IReadOnlyList<string> ReviewScope,
    IReadOnlyList<string> RequiredEvidence,
    string RequiredSignOffGate,
    IReadOnlyList<LegalSourceReference> Sources,
    IReadOnlyList<GoldenFilingCorpusVerifier> EvidenceVerifiers);

public sealed record AccountantAcceptanceSummary(
    int ScenarioCount,
    int AutomatedVerifierCount,
    int ProfessionalSignOffRequiredCount,
    int ManualHandoffScenarioCount,
    IReadOnlyList<string> ReleaseBlockingScenarioCodes,
    IReadOnlyList<string> RequiredSignOffGates,
    string Status);

public sealed record AccountantWorkflowWalkthroughProtocol(
    string ProtocolVersion,
    string ReviewerRole,
    string Status,
    string SignOffGate,
    string FailurePolicy,
    IReadOnlyList<string> SeededScenarioCodes,
    IReadOnlyList<string> RouteSequence,
    IReadOnlyList<string> AcceptanceCriteria,
    IReadOnlyList<string> RequiredEvidence);

public sealed record AccountantJourneyAcceptanceChecklistItem(
    string RouteCode,
    string RouteLabel,
    string RouteKey,
    IReadOnlyList<string> WorkflowStages,
    IReadOnlyList<string> SeededScenarioCodes,
    IReadOnlyList<string> VisualArtifactNames,
    IReadOnlyList<string> RequiredEvidence,
    IReadOnlyList<string> AcceptanceCriteria,
    string SignOffGate,
    string Status);

public sealed record AccountantWorkflowEvidencePackItem(
    string RouteCode,
    string RouteLabel,
    IReadOnlyList<string> WorkflowStages,
    IReadOnlyList<string> SeededScenarioCodes,
    IReadOnlyList<string> VisualArtifactNames,
    string EvidenceArtifact,
    string DecisionQuestion,
    IReadOnlyList<string> RequiredEvidence,
    string SignOffGate,
    string FailurePolicy);

public sealed record AccountantWalkthroughEvidenceMatrixItem(
    string ScenarioCode,
    string ScenarioLabel,
    string ExpectedOutcome,
    string FilingReadinessState,
    string SignOffPacketState,
    bool ManualProfessionalReviewRequired,
    string RouteCode,
    string RouteLabel,
    string RouteKey,
    IReadOnlyList<string> WorkflowStages,
    IReadOnlyList<string> VisualArtifactNames,
    string EvidenceArtifact,
    string DecisionQuestion,
    IReadOnlyList<string> RequiredEvidence,
    IReadOnlyList<string> AcceptanceCriteria,
    string ReleaseChecklistCode,
    string SignOffGate,
    string Status,
    bool BlocksRelease);

public sealed record WorkbenchVisualAcceptanceRegisterItem(
    string RouteCode,
    string RouteLabel,
    IReadOnlyList<string> WorkflowStages,
    IReadOnlyList<string> AcceptanceAreas,
    IReadOnlyList<string> ScreenshotArtifactNames,
    string EvidenceArtifact,
    IReadOnlyList<string> RequiredEvidence,
    string ReleaseGateCode,
    string Status,
    string FailurePolicy,
    string NextAction);

public sealed record VisualQaViewport(
    string Name,
    int Width,
    int Height);

public sealed record VisualQaRoute(
    string Code,
    string RouteKey,
    string Label,
    string Description,
    string RequiredText,
    IReadOnlyList<string> WorkflowStages,
    bool OpenFilingTab);

public sealed record VisualQaArtifact(
    string RouteCode,
    string RouteKey,
    string Theme,
    string ViewportName,
    string FileName,
    string ArtifactPath,
    string RequiredText,
    bool OpenFilingTab,
    string ReviewStatus,
    IReadOnlyList<string> LayoutChecks);

public sealed record VisualQaRouteAudit(
    string RouteCode,
    string RouteKey,
    string Label,
    IReadOnlyList<string> WorkflowStages,
    int ScreenshotCount,
    string ReviewStatus,
    IReadOnlyList<string> ReviewChecks);

public sealed record VisualQaReviewProtocol(
    string ProtocolVersion,
    string ReviewerRole,
    string Status,
    string SignOffGate,
    string FailurePolicy,
    IReadOnlyList<string> AcceptanceCriteria,
    IReadOnlyList<string> RequiredEvidence);

public sealed record VisualQaCoverage(
    string ArtifactName,
    string Enforcement,
    string ManifestFileName,
    int ExpectedScreenshotCount,
    IReadOnlyList<string> LayoutChecks,
    IReadOnlyList<string> ReviewChecks,
    VisualQaReviewProtocol ReviewProtocol,
    IReadOnlyList<string> Themes,
    IReadOnlyList<VisualQaViewport> Viewports,
    IReadOnlyList<VisualQaRoute> Routes,
    IReadOnlyList<VisualQaRouteAudit> RouteAudits,
    IReadOnlyList<VisualQaArtifact> Artifacts);

public sealed record ProductionAssurancePacket(
    string PacketId,
    string PacketVersion,
    string Status,
    string SourceLawSnapshotHash,
    int GoldenCorpusCovered,
    int GoldenCorpusTotal,
    int StatutoryRuleMatrixPaths,
    int StatutoryRuleCoverageFamilies,
    int VisualQaExpectedScreenshots,
    int RequiredOperationalGates,
    int OpenCriticalActions,
    IReadOnlyList<string> EvidenceItems,
    IReadOnlyList<string> ReleaseBlockers);

public sealed record ProductionScorecardCategory(
    string Code,
    string Label,
    int CurrentScore,
    int TargetScore,
    string Status,
    IReadOnlyList<string> CurrentEvidence,
    IReadOnlyList<string> RemainingGaps,
    IReadOnlyList<string> CompletionTrackCodes,
    IReadOnlyList<string> ReleaseBlockerCodes);

public sealed record ProductionScorecard(
    int CurrentScore,
    int TargetScore,
    string Status,
    string NextGate,
    IReadOnlyList<ProductionScorecardCategory> Categories);

public sealed record ProductionReadinessReport(
    DateTime GeneratedAt,
    string OverallStatus,
    int CompaniesInDatabase,
    int PeriodsInDatabase,
    SourceLawSnapshot SourceLawSnapshot,
    IReadOnlyList<SourceLawTraceabilityEntry> SourceLawTraceability,
    SourceLawMaintenanceProtocol SourceLawMaintenanceProtocol,
    IReadOnlyList<SourceLawReviewLedgerEntry> SourceLawReviewLedger,
    IReadOnlyList<RevenueTaxonomyRangeEvidence> RevenueTaxonomyRanges,
    ProductionAssurancePacket AssurancePacket,
    ProductionScorecard ProductionScorecard,
    IReadOnlyList<AccountantAcceptanceCriterion> AccountantAcceptanceCriteria,
    AccountantAcceptanceSummary AccountantAcceptanceSummary,
    AccountantWorkflowWalkthroughProtocol AccountantWorkflowWalkthroughProtocol,
    IReadOnlyList<AccountantJourneyAcceptanceChecklistItem> AccountantJourneyAcceptanceChecklist,
    IReadOnlyList<AccountantWorkflowEvidencePackItem> AccountantWorkflowEvidencePack,
    IReadOnlyList<AccountantWalkthroughEvidenceMatrixItem> AccountantWalkthroughEvidenceMatrix,
    IReadOnlyList<WorkbenchVisualAcceptanceRegisterItem> WorkbenchVisualAcceptanceRegister,
    IReadOnlyList<ProductionReadinessArea> Areas,
    IReadOnlyList<GoldenFilingCorpusScenario> GoldenFilingCorpus,
    IReadOnlyList<GoldenEvidenceLedgerEntry> GoldenEvidenceLedger,
    IReadOnlyList<GoldenVerifierManifestEntry> GoldenVerifierManifest,
    IReadOnlyList<StatutoryRuleMatrixEntry> StatutoryRuleMatrix,
    IReadOnlyList<StatutoryRulesCoverageItem> StatutoryRulesCoverage,
    IReadOnlyList<string> ManualHandoffPaths,
    IReadOnlyList<OperationalGate> OperationalGates,
    IReadOnlyList<ProductionReadinessAssuranceAction> AssuranceActions,
    IReadOnlyList<ProductionReadinessCompletionTrack> CompletionTracks,
    IReadOnlyList<ProductionReleaseBlocker> ReleaseBlockerRegister,
    IReadOnlyList<ProductionAuditabilityControl> AuditabilityControls,
    IReadOnlyList<AuditEvidenceTimelineEntry> AuditEvidenceTimeline,
    IReadOnlyList<ProductionAuditEvidencePackItem> AuditEvidencePack,
    IReadOnlyList<ProductionMonitoringControl> MonitoringControls,
    IReadOnlyList<DependencyPolicyControl> DependencyPolicyControls,
    IReadOnlyList<DeploymentSafetyControl> DeploymentSafetyControls,
    IReadOnlyList<OperationsEvidencePackItem> OperationsEvidencePack,
    IReadOnlyList<ReleaseReviewChecklistItem> ReleaseReviewChecklist,
    IReadOnlyList<ReleaseVerificationManifestItem> ReleaseVerificationManifest,
    VisualQaCoverage VisualQaCoverage);

public class ProductionReadinessReportService(AccountsDbContext db)
{
    public async Task<ProductionReadinessReport> GetReportAsync(CancellationToken cancellationToken = default)
    {
        var companies = await db.Companies.CountAsync(cancellationToken);
        var periods = await db.AccountingPeriods.CountAsync(cancellationToken);
        var sourceSnapshot = IrishStatutoryRuleSources.BuildSnapshot();
        var areas = BuildAreas();
        var goldenCorpus = BuildGoldenCorpus();
        var statutoryRuleMatrix = BuildStatutoryRuleMatrix();
        var statutoryRulesCoverage = BuildStatutoryRulesCoverage();
        var manualHandoffPaths = BuildManualHandoffPaths();
        var operationalGates = BuildOperationalGates();
        var assuranceActions = BuildAssuranceActions();
        var completionTracks = BuildCompletionTracks();
        var auditabilityControls = BuildAuditabilityControls();
        var auditEvidenceTimeline = BuildAuditEvidenceTimeline();
        var auditEvidencePack = BuildAuditEvidencePack();
        var monitoringControls = BuildMonitoringControls();
        var dependencyPolicyControls = BuildDependencyPolicyControls();
        var deploymentSafetyControls = BuildDeploymentSafetyControls();
        var operationsEvidencePack = BuildOperationsEvidencePack();
        var releaseReviewChecklist = BuildReleaseReviewChecklist(assuranceActions, operationalGates);
        var releaseVerificationManifest = BuildReleaseVerificationManifest();
        var releaseBlockerRegister = BuildReleaseBlockerRegister(
            completionTracks,
            assuranceActions,
            releaseReviewChecklist);
        var accountantAcceptanceCriteria = BuildAccountantAcceptanceCriteria(goldenCorpus);
        var goldenEvidenceLedger = BuildGoldenEvidenceLedger(goldenCorpus, accountantAcceptanceCriteria);
        var goldenVerifierManifest = BuildGoldenVerifierManifest(goldenCorpus);
        var accountantAcceptanceSummary = BuildAccountantAcceptanceSummary(goldenCorpus, accountantAcceptanceCriteria);
        var accountantWorkflowWalkthroughProtocol = BuildAccountantWorkflowWalkthroughProtocol(goldenCorpus);
        var visualQaCoverage = BuildVisualQaCoverage();
        var accountantJourneyAcceptanceChecklist = BuildAccountantJourneyAcceptanceChecklist(goldenCorpus, visualQaCoverage);
        var accountantWorkflowEvidencePack = BuildAccountantWorkflowEvidencePack(accountantJourneyAcceptanceChecklist);
        var accountantWalkthroughEvidenceMatrix = BuildAccountantWalkthroughEvidenceMatrix(
            goldenCorpus,
            accountantAcceptanceCriteria,
            accountantJourneyAcceptanceChecklist,
            accountantWorkflowEvidencePack);
        var workbenchVisualAcceptanceRegister = BuildWorkbenchVisualAcceptanceRegister(visualQaCoverage);
        var sourceLawTraceability = BuildSourceLawTraceability(
            sourceSnapshot,
            goldenCorpus,
            statutoryRuleMatrix,
            statutoryRulesCoverage,
            accountantAcceptanceCriteria);
        var sourceLawMaintenanceProtocol = BuildSourceLawMaintenanceProtocol(sourceSnapshot);
        var sourceLawReviewLedger = BuildSourceLawReviewLedger(sourceSnapshot, releaseReviewChecklist);
        var revenueTaxonomyRanges = BuildRevenueTaxonomyRanges();
        var assurancePacket = BuildAssurancePacket(
            sourceSnapshot,
            goldenCorpus,
            statutoryRuleMatrix,
            statutoryRulesCoverage,
            operationalGates,
            assuranceActions,
            visualQaCoverage);
        var productionScorecard = BuildProductionScorecard(
            completionTracks,
            releaseBlockerRegister);

        return new ProductionReadinessReport(
            DateTime.UtcNow,
            "review-required",
            companies,
            periods,
            sourceSnapshot,
            sourceLawTraceability,
            sourceLawMaintenanceProtocol,
            sourceLawReviewLedger,
            revenueTaxonomyRanges,
            assurancePacket,
            productionScorecard,
            accountantAcceptanceCriteria,
            accountantAcceptanceSummary,
            accountantWorkflowWalkthroughProtocol,
            accountantJourneyAcceptanceChecklist,
            accountantWorkflowEvidencePack,
            accountantWalkthroughEvidenceMatrix,
            workbenchVisualAcceptanceRegister,
            areas,
            goldenCorpus,
            goldenEvidenceLedger,
            goldenVerifierManifest,
            statutoryRuleMatrix,
            statutoryRulesCoverage,
            manualHandoffPaths,
            operationalGates,
            assuranceActions,
            completionTracks,
            releaseBlockerRegister,
            auditabilityControls,
            auditEvidenceTimeline,
            auditEvidencePack,
            monitoringControls,
            dependencyPolicyControls,
            deploymentSafetyControls,
            operationsEvidencePack,
            releaseReviewChecklist,
            releaseVerificationManifest,
            visualQaCoverage);
    }

    public static string ComputeAssurancePacketId(ProductionAssurancePacket packet)
    {
        var canonical = string.Join(
            "\n",
            [
                packet.PacketVersion,
                packet.Status,
                packet.SourceLawSnapshotHash,
                packet.GoldenCorpusCovered.ToString(CultureInfo.InvariantCulture),
                packet.GoldenCorpusTotal.ToString(CultureInfo.InvariantCulture),
                packet.StatutoryRuleMatrixPaths.ToString(CultureInfo.InvariantCulture),
                packet.StatutoryRuleCoverageFamilies.ToString(CultureInfo.InvariantCulture),
                packet.VisualQaExpectedScreenshots.ToString(CultureInfo.InvariantCulture),
                packet.RequiredOperationalGates.ToString(CultureInfo.InvariantCulture),
                packet.OpenCriticalActions.ToString(CultureInfo.InvariantCulture),
                string.Join("|", packet.EvidenceItems.Order(StringComparer.Ordinal)),
                string.Join("|", packet.ReleaseBlockers.Order(StringComparer.Ordinal))
            ]);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return $"assurance-sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static ProductionAssurancePacket BuildAssurancePacket(
        SourceLawSnapshot sourceSnapshot,
        IReadOnlyList<GoldenFilingCorpusScenario> goldenCorpus,
        IReadOnlyList<StatutoryRuleMatrixEntry> statutoryRuleMatrix,
        IReadOnlyList<StatutoryRulesCoverageItem> statutoryRulesCoverage,
        IReadOnlyList<OperationalGate> operationalGates,
        IReadOnlyList<ProductionReadinessAssuranceAction> assuranceActions,
        VisualQaCoverage visualQaCoverage)
    {
        var evidenceItems = new[]
        {
            "source-law-snapshot-fingerprint",
            "source-law-traceability-index",
            "source-law-maintenance-protocol",
            "source-law-review-ledger",
            "revenue-taxonomy-range-evidence",
            "golden-filing-corpus",
            "golden-evidence-ledger",
            "golden-verifier-manifest",
            "statutory-rules-matrix",
            "statutory-rules-coverage",
            "audit-evidence-timeline",
            "production-audit-evidence-pack",
            "visual-smoke-screenshots",
            "accountant-workbench-evidence-report",
            "production-operational-gates",
            "production-readiness-report",
            "production-readiness-verification-report",
            "dependency-policy-controls",
            "deployment-safety-controls",
            "operations-evidence-pack",
            "release-blocker-register",
            "release-review-checklist",
            "release-verification-manifest",
            "accountant-acceptance-criteria",
            "accountant-acceptance-summary",
            "accountant-workflow-walkthrough-protocol",
            "accountant-journey-acceptance-checklist",
            "accountant-workflow-evidence-pack",
            "accountant-walkthrough-evidence-matrix",
            "workbench-visual-acceptance-register",
            "production-completion-map",
            "production-scorecard"
        };
        var releaseBlockers = assuranceActions
            .Where(action => action.Status != "complete")
            .OrderBy(action => action.Priority == "critical" ? 0 : action.Priority == "high" ? 1 : 2)
            .ThenBy(action => action.Code, StringComparer.Ordinal)
            .Select(action => action.Code == "qualified-accountant-signoff"
                ? "Qualified accountant sign-off required"
                : $"{action.Label} required")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var packetWithoutId = new ProductionAssurancePacket(
            "",
            "production-assurance-packet-v1",
            releaseBlockers.Length == 0 ? "ready" : "review-required",
            sourceSnapshot.ContentHash,
            goldenCorpus.Count(scenario => scenario.CoverageStatus == "covered"),
            goldenCorpus.Count,
            statutoryRuleMatrix.Count,
            statutoryRulesCoverage.Count,
            visualQaCoverage.ExpectedScreenshotCount,
            operationalGates.Count(gate => gate.Required),
            assuranceActions.Count(action => action.Priority == "critical" && action.Status != "complete"),
            evidenceItems,
            releaseBlockers);

        return packetWithoutId with { PacketId = ComputeAssurancePacketId(packetWithoutId) };
    }

    private static ProductionScorecard BuildProductionScorecard(
        IReadOnlyList<ProductionReadinessCompletionTrack> completionTracks,
        IReadOnlyList<ProductionReleaseBlocker> releaseBlockers)
    {
        var trackCodes = completionTracks.Select(track => track.Code).ToHashSet(StringComparer.Ordinal);
        var blockerCodes = releaseBlockers.Select(blocker => blocker.Code).ToHashSet(StringComparer.Ordinal);

        ProductionScorecardCategory Category(
            string code,
            string label,
            int currentScore,
            int targetScore,
            string status,
            IReadOnlyList<string> currentEvidence,
            IReadOnlyList<string> remainingGaps,
            IReadOnlyList<string> completionTrackCodes,
            IReadOnlyList<string> releaseBlockerCodes)
        {
            foreach (var trackCode in completionTrackCodes)
            {
                if (!trackCodes.Contains(trackCode))
                    throw new InvalidOperationException($"Production scorecard category {code} references unknown completion track {trackCode}.");
            }

            foreach (var blockerCode in releaseBlockerCodes)
            {
                if (!blockerCodes.Contains(blockerCode))
                    throw new InvalidOperationException($"Production scorecard category {code} references unknown release blocker {blockerCode}.");
            }

            return new ProductionScorecardCategory(
                code,
                label,
                currentScore,
                targetScore,
                status,
                currentEvidence,
                remainingGaps,
                completionTrackCodes,
                releaseBlockerCodes);
        }

        var categories = new[]
        {
            Category(
                "architecture-documentation",
                "Architecture and documentation",
                99,
                100,
                "release-evidence-required",
                [
                    "CLAUDE.md is the canonical architecture/development guide.",
                    "AGENTS.md carries the active production-readiness handoff.",
                    "Production runbook links release evidence templates for source-law review, visual QA, monitoring provider confirmation and qualified-accountant acceptance.",
                    "scripts/verify-release-evidence.ps1 validates completed release evidence templates before real filing use, including source-law source coverage.",
                    "scripts/verify-release-artifact-pack.ps1 validates the collected release artifact reports and the retained human release-evidence templates as one exact evidence pack with release candidate identity and SHA-256 inventory.",
                    "CI artifacts now prove production safety, dependency audit, monitoring smoke, structured logs, visual smoke and backup restore drill."
                ],
                [
                    "Keep AGENTS.md aligned with the latest green CI run after every release-evidence commit.",
                    "Complete the checked-in release evidence templates with named human reviewers and retain release-evidence-report.json.",
                    "Complete source-law-review-template.md with a named reviewer and qualified-accountant source-law sign-off."
                ],
                ["backend-code", "frontend-ui-ux", "frontend-code"],
                ["backend-code:source-law-change-review", "frontend-ui-ux:light-dark-visual-regression"]),
            Category(
                "backend-statutory-accounting-engine",
                "Backend statutory/accounting engine",
                250,
                250,
                "qualified-accountant-review-required",
                [
                    "Golden filing corpus covers micro LTD, small abridged LTD, DAC small, CLG charity and medium audit-required manual handoff.",
                    "Source-law snapshot, traceability, review ledger and Revenue taxonomy ranges are exposed in the readiness report.",
                    "Filing readiness profiles, generated PDF/iXBRL evidence and audit snapshots are backed by automated tests.",
                    "Qualified-accountant acceptance evidence now uses canonical golden corpus scenario codes and the release verifier reports required scenario, route and artifact coverage.",
                    "scripts/verify-release-evidence.ps1 now rejects qualified-accountant acceptance unless every golden scenario decision and every route evidence acceptance row is explicitly accepted.",
                    "Qualified-accountant acceptance now requires explicit accepted scenario scope cells for outputs, gates, source-law evidence, wording and workbench journey before a scenario decision can pass.",
                    "Qualified-accountant scenario walkthrough rows now must match the exact qualified-accountant-walkthrough-ledger anchor for every canonical golden corpus scenario.",
                    "Qualified-accountant route walkthrough rows now require exact yes decision-question cells and exact accepted evidence cells before route acceptance can pass.",
                    "Qualified-accountant route decision-question cells now reject accepted-style ambiguous text so professional evidence acceptance stays in the dedicated evidence column.",
                    "Qualified-accountant route walkthrough rows now require route-specific accountant-workbench evidence anchors for every accepted route.",
                    "Qualified-accountant route acceptance now requires a real retained workbench evidence reference for every accepted route, tied to accountant-workbench-evidence-report.json.",
                    "Qualified-accountant route walkthrough notes now must match the exact qualified-accountant-route-walkthrough anchor for every accepted route.",
                    "Qualified-accountant acceptance top-level evidence now rejects placeholder accountant name, qualification, reviewer capacity and signature fields before professional sign-off evidence can pass.",
                    "External ROS/iXBRL validation evidence now has a checked-in template and the release verifier rejects rows without real references, retained taxonomy package references, accepted/remediated warning status and accepted scenario decisions.",
                    "External ROS/iXBRL validation references and retained taxonomy package references now must include the matching golden corpus scenario code before acceptance can pass.",
                    "External ROS/iXBRL validation rows now must match exact retained external validation and taxonomy package ledger anchors for every canonical golden corpus scenario.",
                    "External ROS/iXBRL validation warnings/errors now require exact none, accepted or remediated values, and scenario decisions require exact accepted values before evidence can pass.",
                    "External ROS/iXBRL validation top-level evidence now rejects placeholder provider, environment, run/reference, report, taxonomy and company/period fields, and requires a retained XHTML, HTML or ZIP iXBRL artifact name.",
                    "Source-law review evidence now has a checked-in template and the release verifier rejects monitored-source rows without concrete URL reachability, dated or not-dated effective-date review, guidance comparison, platform impact classification and exact accepted decisions.",
                    "Source-law review platform impact cells now require exact no change, reflected or blocking values before review evidence can pass.",
                    "Source-law review notes now must match the exact source-law-review-ledger anchor for every monitored source before acceptance can pass.",
                    "Source-law review top-level evidence now requires an exact source-law-snapshot-fingerprint retained evidence anchor plus real reviewer and qualified-accountant identity, signature and sign-off fields before review evidence can pass.",
                    "Manual handoff acceptance now has a checked-in template and the release verifier rejects audit-required or unsupported-path rows without real evidence references and accepted reviewer decisions.",
                    "Manual handoff scenario and unsupported-path decisions now require exact accepted reviewer decisions before acceptance evidence can pass.",
                    "Manual handoff evidence references now must include the matching scenario or unsupported-path code before acceptance can pass.",
                    "Manual handoff evidence rows now must match exact retained auditor-report, handoff-note, readiness-snapshot and unsupported-path anchors before acceptance can pass.",
                    "CI now retains production-readiness-report.json from the live smoke stack, proving the exact source-law snapshot, golden corpus, scorecard and release blockers exposed by the candidate.",
                    "scripts/verify-production-readiness-report.ps1 emits production-readiness-verification-report.json and proves the captured live report has complete source-law, golden-corpus, scorecard, blocker, visual-QA and release-manifest coverage."
                ],
                [
                    "Complete and retain verified source-law review evidence for every monitored source before relying on generated packs.",
                    "Run and retain verified qualified-accountant acceptance across every canonical golden corpus scenario.",
                    "Complete and retain verified external ROS/iXBRL validation evidence for generated packs.",
                    "Complete and retain verified manual handoff acceptance for audit-required and unsupported paths before relying on outputs."
                ],
                ["backend-code"],
                [
                    "backend-code:qualified-accountant-signoff",
                    "backend-code:external-ros-validation",
                    "backend-code:accountant-acceptance-walkthrough"
                ]),
            Category(
                "frontend-accountant-workbench",
                "Frontend accountant workbench",
                186,
                200,
                "visual-acceptance-required",
                [
                    "Production readiness, dashboard, company, period, filing review, financial statements and workbench preview routes are in the visual smoke plan.",
                    "Shared workbench primitives and route-level render tests cover the main accountant journey.",
                    "Dense tables, workflow rails, blocker summaries and permission-denied states are surfaced in the workbench.",
                    "node scripts/verify-visual-smoke-artifacts.mjs now writes visual-smoke-evidence-report.json covering screenshot hashes, byte sizes, PNG dimensions, nonblank pixel diversity, per-screenshot layout-check pass results, automated theme-contrast smoke results and route/theme/viewport completeness before human review.",
                    "Docs/release-evidence/visual-qa-signoff-template.md and scripts/verify-release-evidence.ps1 now require named reviewers to record the visual smoke nonblank pixel and contrast metrics before visual QA evidence can pass.",
                    "Visual QA sign-off now requires exact pass decisions for every route across desktop light, desktop dark, mobile light and mobile dark captures.",
                    "Visual QA route capture cells now reject accepted-style ambiguous text so reviewer limitations must stay in retained route notes or references.",
                    "Visual QA route notes now must match the exact visual-smoke-evidence-report.json routeAcceptance anchor for every route before sign-off evidence can pass.",
                    "Visual QA release evidence now requires exact visual-smoke manifest, visual evidence report and accountant workbench evidence report filenames before sign-off evidence can pass.",
                    "Visual QA top-level evidence now rejects placeholder reviewer name, reviewer role and reviewer signature fields before human visual sign-off evidence can pass.",
                    "node scripts/verify-accountant-workbench-evidence.mjs now writes accountant-workbench-evidence-report.json proving route, workflow-stage, theme, viewport, layout-check and review-check coverage.",
                    "scripts/verify-release-artifact-pack.ps1 and scripts/verify-ci-machine-evidence-pack.ps1 reject visual evidence unless every screenshot reports passed console-error, horizontal-overflow, visible-text-overlap and automated theme-contrast smoke checks.",
                    "Visual smoke and accountant-workbench evidence now retain and re-verify each route's expected accountant decision text across light/dark desktop/mobile screenshots.",
                    "accountant-workbench-evidence-report.json now includes route acceptance rows with stable route keys, expected decision text, blocking status and qualified-accountant route acceptance evidence for every workbench route.",
                    "Release artifact and CI machine evidence pack verifiers now require exact accountant-workbench route acceptance names, route keys, expected decision text and per-route acceptance evidence ids for every workbench route.",
                    "Release artifact and CI machine evidence pack verifiers now require exact accountant-workbench route acceptance labels, screenshot-review evidence anchors and required-review status for every workbench route.",
                    "Release artifact and CI machine evidence pack verifiers now require exact accountant-workbench route readiness screenshot counts, layout-check counts, contrast counts, minimum contrast ratios, required-review status and required review checks for every workbench route.",
                    "Frontend parser invariants now require the CI machine evidence pack, production smoke, readiness verification, visual smoke and manual release-verification rows before rendering readiness data."
                ],
                [
                    "Complete named visual QA review against the light/dark desktop/mobile screenshot manifest and visual-smoke-evidence-report.json.",
                    "Continue route-by-route polish for density, dark mode, mobile flow and table scanability.",
                    "Record qualified-accountant route acceptance for outputs, gates, wording and evidence."
                ],
                ["frontend-ui-ux", "frontend-code"],
                [
                    "frontend-ui-ux:light-dark-visual-regression",
                    "frontend-ui-ux:accountant-acceptance-walkthrough",
                    "frontend-code:light-dark-visual-regression"
                ]),
            Category(
                "security-auth-tenant-platform-guardrails",
                "Security/auth/tenant/platform guardrails",
                150,
                150,
                "operator-confirmation-required",
                [
                    "Authenticated sessions, CSRF, secure cookie checks and post-logout 401 are covered by production smoke.",
                    "Request-scoped EF query filters backstop tenant isolation across company-owned and period-owned child tables.",
                    "Production compose gates enforce immutable images, migrate-only job ordering, demo seed blocking and structured monitoring evidence.",
                    "CI runs scripts/verify-no-direct-filing-submission.ps1 and retains no-direct-filing-submission-report.json, proving final CRO/ROS operations remain recorded workflow states with no outbound submission client wired.",
                    "no-direct-filing-submission-report.json now records release candidate commit/run identity, and the CI machine evidence pack plus release artifact pack reject stale no-direct evidence whose identity does not match the verified candidate.",
                    "scripts/verify-release-artifact-pack.ps1 validates dependency, production safety, monitoring, structured log, backup/restore, no-direct-submission, production-readiness verification, visual smoke and release-evidence reports together.",
                    "release-artifact-pack-report.json now records release candidate identity plus per-report SHA-256 and byte-size evidence.",
                    "CI runs scripts/verify-ci-machine-evidence-pack.ps1 and retains ci-machine-evidence-pack-report.json with exact commit/run identity plus SHA-256 inventory for dependency, safety, monitoring, structured log, backup/restore, no-direct, readiness and visual/workbench evidence.",
                    "scripts/verify-production-readiness-report.ps1 now requires default-CI and manual release manifest rows, including the no-direct CRO/ROS control, CI machine evidence pack and release artifact pack, before accepting a captured readiness report.",
                    "scripts/verify-release-artifact-pack.ps1 now rejects release packs unless the retained production-readiness-verification-report.json proves every required default-CI and manual release manifest row.",
                    "scripts/verify-release-artifact-pack.ps1 and scripts/verify-ci-machine-evidence-pack.ps1 now reject visual evidence packs unless visual-smoke-evidence-report.json carries planned PNG viewport dimensions, passed layout-check results and automated theme-contrast smoke results for every screenshot.",
                    "scripts/verify-release-evidence.ps1 now rejects completed human evidence when release candidate identity, UTC timestamps, SHA-256 digests, external iXBRL artifact hashes, or monitoring log confirmation fields are malformed.",
                    "scripts/verify-release-evidence.ps1 emits a consistent releaseCandidate identity for all six human evidence templates, and scripts/verify-release-artifact-pack.ps1 rejects packs whose release-evidence-report.json identity does not match the pack CommitSha and GitHubActionsRunUrl.",
                    "scripts/verify-release-evidence.ps1 now emits SHA-256/byte-size manifest entries for all six human release-evidence templates, and scripts/verify-release-artifact-pack.ps1 requires those completed templates to be retained in the pack with matching hashes.",
                    "Monitoring-provider confirmation evidence now requires real provider/event/correlation references, an HTTPS provider base URL, a matched structured-log smoke line and an explicit accepted operator decision."
                ],
                [
                    "Confirm the controlled monitoring smoke event inside the configured provider and retain operator evidence.",
                    "Run and retain the full release-artifact-pack-report.json after release-evidence-report.json is completed with named human sign-offs.",
                    "Retain provider-console monitoring confirmation for the exact release candidate."
                ],
                ["backend-code"],
                ["backend-code:production-monitoring"])
        };

        return new ProductionScorecard(
            categories.Sum(category => category.CurrentScore),
            categories.Sum(category => category.TargetScore),
            "review-required",
            "Complete source-law review, named visual QA, monitoring-provider confirmation, manual handoff and qualified-accountant acceptance evidence.",
            categories);
    }

    private static IReadOnlyList<SourceLawTraceabilityEntry> BuildSourceLawTraceability(
        SourceLawSnapshot sourceSnapshot,
        IReadOnlyList<GoldenFilingCorpusScenario> goldenCorpus,
        IReadOnlyList<StatutoryRuleMatrixEntry> statutoryRuleMatrix,
        IReadOnlyList<StatutoryRulesCoverageItem> statutoryRulesCoverage,
        IReadOnlyList<AccountantAcceptanceCriterion> accountantAcceptanceCriteria)
    {
        var sourceUsages = sourceSnapshot.Sources.ToDictionary(
            source => source.SourceId,
            _ => new SortedSet<string>(StringComparer.Ordinal),
            StringComparer.Ordinal);
        var releaseGateCodes = BuildSourceReleaseGateCodes();

        void AddUsage(LegalSourceReference source, string usage)
        {
            if (!sourceUsages.TryGetValue(source.SourceId, out var usages))
            {
                usages = new SortedSet<string>(StringComparer.Ordinal);
                sourceUsages[source.SourceId] = usages;
            }

            usages.Add(usage);
        }

        foreach (var scenario in goldenCorpus)
        {
            foreach (var source in scenario.EvidencePack.SourceReferences)
                AddUsage(source, $"golden-corpus:{scenario.Code}");
        }

        foreach (var row in statutoryRuleMatrix)
        {
            foreach (var source in row.Sources)
                AddUsage(source, $"statutory-rule-matrix:{row.Code}");
        }

        foreach (var coverage in statutoryRulesCoverage)
        {
            foreach (var source in coverage.Sources)
                AddUsage(source, $"statutory-rules-coverage:{coverage.Code}");
        }

        foreach (var criterion in accountantAcceptanceCriteria)
        {
            foreach (var source in criterion.Sources)
                AddUsage(source, $"accountant-acceptance:{criterion.ScenarioCode}");
        }

        var snapshotBySourceId = sourceSnapshot.Sources.ToDictionary(source => source.SourceId, StringComparer.Ordinal);
        return sourceUsages
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair =>
            {
                var inSnapshot = snapshotBySourceId.TryGetValue(pair.Key, out var source);
                var reference = source ?? new LegalSourceReference(pair.Key, pair.Key, sourceSnapshot.SnapshotDate, "");
                return new SourceLawTraceabilityEntry(
                    reference.SourceId,
                    reference.Title,
                    reference.EffectiveDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    reference.Url,
                    inSnapshot,
                    pair.Value.ToArray(),
                    releaseGateCodes.TryGetValue(pair.Key, out var gates) ? gates : []);
            })
            .ToArray();
    }

    private static IReadOnlyList<GoldenEvidenceLedgerEntry> BuildGoldenEvidenceLedger(
        IReadOnlyList<GoldenFilingCorpusScenario> goldenCorpus,
        IReadOnlyList<AccountantAcceptanceCriterion> accountantAcceptanceCriteria)
    {
        var acceptanceByScenario = accountantAcceptanceCriteria.ToDictionary(
            criterion => criterion.ScenarioCode,
            StringComparer.Ordinal);

        return goldenCorpus
            .OrderBy(scenario => scenario.Code, StringComparer.Ordinal)
            .Select(scenario =>
            {
                var acceptance = acceptanceByScenario[scenario.Code];
                return new GoldenEvidenceLedgerEntry(
                    scenario.Code,
                    scenario.Label,
                    scenario.Fixture.LegalName,
                    scenario.Fixture.CompanyType,
                    scenario.ExpectedOutcome,
                    scenario.CoverageStatus,
                    acceptance.AcceptanceStatus,
                    acceptance.RequiredSignOffGate,
                    acceptance.Required || scenario.Fixture.ManualProfessionalReviewRequired || scenario.ExpectedOutcome == "manual-handoff",
                    scenario.EvidenceTestNames.Order(StringComparer.Ordinal).ToArray(),
                    scenario.EvidenceVerifiers.Select(verifier => verifier.Command).Order(StringComparer.Ordinal).ToArray(),
                    scenario.EvidenceVerifiers.Select(verifier => verifier.CiScope).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                    scenario.EvidenceVerifiers.Select(verifier => verifier.EvidenceLevel).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                    scenario.EvidencePack.OutputArtifacts.Order(StringComparer.Ordinal).ToArray(),
                    scenario.EvidencePack.DecisionGates.Order(StringComparer.Ordinal).ToArray(),
                    scenario.EvidencePack.ExpectedValueChecks.Order(StringComparer.Ordinal).ToArray(),
                    scenario.EvidencePack.ExpectedProofPoints.Select(proof => proof.Area).Order(StringComparer.Ordinal).ToArray(),
                    scenario.EvidencePack.SourceReferences.Select(source => source.SourceId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                    scenario.EvidencePack.ExpectedOutputs.ExpectedCorporationTax,
                    scenario.EvidencePack.ExpectedOutputs.FilingReadinessState,
                    scenario.EvidencePack.ExpectedOutputs.SignOffPacketState);
            })
            .ToArray();
    }

    private static IReadOnlyList<GoldenVerifierManifestEntry> BuildGoldenVerifierManifest(
        IReadOnlyList<GoldenFilingCorpusScenario> goldenCorpus) =>
        goldenCorpus
            .OrderBy(scenario => scenario.Code, StringComparer.Ordinal)
            .SelectMany(scenario => scenario.EvidenceVerifiers
                .OrderBy(verifier => verifier.Name, StringComparer.Ordinal)
                .Select(verifier => new GoldenVerifierManifestEntry(
                    scenario.Code,
                    scenario.Label,
                    scenario.ExpectedOutcome,
                    scenario.CoverageStatus,
                    verifier.Name,
                    verifier.Command,
                    verifier.CiScope,
                    verifier.RunsInDefaultCi,
                    verifier.EvidenceLevel,
                    BlocksRelease: true,
                    scenario.EvidencePack.OutputArtifacts.Order(StringComparer.Ordinal).ToArray(),
                    scenario.EvidencePack.DecisionGates.Order(StringComparer.Ordinal).ToArray(),
                    scenario.EvidencePack.ExpectedProofPoints.Select(proof => proof.Area).Order(StringComparer.Ordinal).ToArray())))
            .ToArray();

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildSourceReleaseGateCodes()
    {
        return new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [IrishStatutoryRuleSources.CharitiesRegulatorAnnualReport.SourceId] =
            [
                "charity-annual-return-review",
                "qualified-accountant-review"
            ],
            [IrishStatutoryRuleSources.CroAuditorsReport.SourceId] =
            [
                "auditor-handoff",
                "manual-professional-handoff"
            ],
            [IrishStatutoryRuleSources.CroFinancialStatementsRequirements.SourceId] =
            [
                "cro-filing-readiness",
                "director-secretary-certification",
                "qualified-accountant-review"
            ],
            [IrishStatutoryRuleSources.CroGroupCompany.SourceId] =
            [
                "group-manual-handoff",
                "manual-professional-handoff"
            ],
            [IrishStatutoryRuleSources.CroGuaranteeCompany.SourceId] =
            [
                "clg-filing-review",
                "qualified-accountant-review"
            ],
            [IrishStatutoryRuleSources.CroMediumCompany.SourceId] =
            [
                "auditor-handoff",
                "manual-professional-handoff"
            ],
            [IrishStatutoryRuleSources.CroUnlimitedCompany.SourceId] =
            [
                "manual-professional-handoff"
            ],
            [IrishStatutoryRuleSources.FrcFrs102.SourceId] =
            [
                "statutory-basis-review",
                "qualified-accountant-review"
            ],
            [IrishStatutoryRuleSources.FrcFrs105.SourceId] =
            [
                "micro-statutory-review",
                "qualified-accountant-review"
            ],
            [IrishStatutoryRuleSources.RevenueAcceptedTaxonomies.SourceId] =
            [
                "external-ros-validation",
                "ixbrl-taxonomy-selection"
            ],
            [IrishStatutoryRuleSources.RevenueIxbrlContents.SourceId] =
            [
                "external-ros-validation",
                "ixbrl-content-review"
            ],
            [IrishStatutoryRuleSources.RevenueIxbrlOverview.SourceId] =
            [
                "external-ros-validation",
                "revenue-filing-readiness"
            ]
        };
    }

    private static IReadOnlyList<RevenueTaxonomyRangeEvidence> BuildRevenueTaxonomyRanges()
    {
        var ranges = RevenueIxbrlTaxonomySelector.AcceptedTaxonomyRanges()
            .OrderByDescending(range => range.EffectiveForPeriodsStartingOnOrAfter)
            .ThenBy(range => range.AccountingStandard, StringComparer.Ordinal)
            .ToArray();

        return ranges
            .Select(range =>
            {
                var nextEffectiveStart = ranges
                    .Where(candidate =>
                        candidate.AccountingStandard == range.AccountingStandard
                        && candidate.EffectiveForPeriodsStartingOnOrAfter > range.EffectiveForPeriodsStartingOnOrAfter)
                    .OrderBy(candidate => candidate.EffectiveForPeriodsStartingOnOrAfter)
                    .FirstOrDefault();
                var gates = new List<string>
                {
                    "external-ros-validation",
                    "ixbrl-taxonomy-selection",
                    "source-law-change-review"
                };
                if (!range.AutomatedPlatformSelectionSupported)
                    gates.Add("manual-professional-handoff");

                return new RevenueTaxonomyRangeEvidence(
                    range.TaxonomyKey,
                    range.AccountingStandard,
                    range.TaxonomyDate,
                    range.Label,
                    range.SchemaRef,
                    AcceptedByRevenue: true,
                    range.AutomatedPlatformSelectionSupported,
                    range.EffectiveForPeriodsStartingOnOrAfter.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    nextEffectiveStart is null
                        ? ""
                        : nextEffectiveStart.EffectiveForPeriodsStartingOnOrAfter.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    range.Sources.Select(source => source.SourceId).Order(StringComparer.Ordinal).ToArray(),
                    gates.ToArray());
            })
            .ToArray();
    }

    private static SourceLawMaintenanceProtocol BuildSourceLawMaintenanceProtocol(SourceLawSnapshot sourceSnapshot)
    {
        var nextReviewDue = sourceSnapshot.SnapshotDate.AddMonths(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return new SourceLawMaintenanceProtocol(
            "source-law-maintenance-v1",
            "Qualified accountant and engineering",
            "required-review",
            "Before every production release and at least monthly while source-backed filing logic is active.",
            nextReviewDue,
            "source-law-change-review",
            "Compare CRO, Revenue, FRC and Charities Regulator guidance pages against the pinned source-law snapshot before release.",
            "Block release if any pinned source changes, becomes unreachable, gains a newer effective date, or lacks qualified-accountant review.",
            sourceSnapshot.Sources.Select(source => source.SourceId).Order(StringComparer.Ordinal).ToArray(),
            [
                "CRO, Revenue, FRC and Charities Regulator source pages are reachable and reviewed for changes.",
                "Every changed effective date or guidance wording is reflected in source-law snapshot metadata before release.",
                "A qualified accountant accepts the source-law review note before generated filing packs are used for real filings."
            ],
            [
                "source-law-snapshot-fingerprint",
                "source-law-traceability-index",
                "source-law-change-review-note",
                "qualified-accountant-source-law-signoff"
            ]);
    }

    private static IReadOnlyList<SourceLawReviewLedgerEntry> BuildSourceLawReviewLedger(
        SourceLawSnapshot sourceSnapshot,
        IReadOnlyList<ReleaseReviewChecklistItem> releaseReviewChecklist)
    {
        var checklistItem = releaseReviewChecklist.Single(item => item.Code == "source-law-change-review");

        return sourceSnapshot.Sources
            .OrderBy(source => source.SourceId, StringComparer.Ordinal)
            .Select(source => new SourceLawReviewLedgerEntry(
                source.SourceId,
                source.Title,
                source.Url,
                source.EffectiveDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                SelectSourceLawOwnerRole(source.SourceId),
                checklistItem.Code,
                checklistItem.BlocksRelease,
                BuildSourceLawReviewChecks(source.SourceId),
                new[]
                {
                    "source-law-change-review-note",
                    "qualified-accountant-source-law-signoff",
                    checklistItem.EvidenceArtifact
                }.Distinct(StringComparer.Ordinal).ToArray()))
            .ToArray();
    }

    private static string SelectSourceLawOwnerRole(string sourceId) =>
        sourceId switch
        {
            var id when id.StartsWith("revenue-", StringComparison.Ordinal) => "Taxonomy and corporation tax reviewer",
            var id when id.StartsWith("frc-", StringComparison.Ordinal) => "Accounting standards reviewer",
            var id when id.StartsWith("charities-", StringComparison.Ordinal) => "Charity reporting reviewer",
            _ => "Qualified accountant and engineering"
        };

    private static IReadOnlyList<string> BuildSourceLawReviewChecks(string sourceId)
    {
        var checks = new List<string>
        {
            "Confirm source page is reachable at the pinned URL.",
            "Compare pinned effective date against the current source page.",
            "Review guidance wording for statutory filing, exemption, note or taxonomy changes.",
            "Record qualified accountant acceptance before generated packs are used for real filings."
        };

        if (sourceId.StartsWith("revenue-", StringComparison.Ordinal))
            checks.Add("Confirm Revenue-accepted taxonomy and iXBRL content guidance still match generated output assumptions.");
        if (sourceId == IrishStatutoryRuleSources.CroAuditorsReport.SourceId)
            checks.Add("Confirm auditor report requirements still support audit-required manual handoff gates.");
        if (sourceId == IrishStatutoryRuleSources.CroUnlimitedCompany.SourceId)
            checks.Add("Confirm unlimited-company variants remain manual professional handoff unless explicitly modelled.");
        if (sourceId == IrishStatutoryRuleSources.CharitiesRegulatorAnnualReport.SourceId)
            checks.Add("Confirm charity annual-report deadlines and SoFA/TAR evidence expectations remain current.");

        return checks;
    }

    private static IReadOnlyList<ProductionReadinessArea> BuildAreas() =>
    [
        new(
            "backend-accounting-engine",
            "Backend accounting engine",
            "hardened",
            "Golden-path coverage exercises classification, regime selection, statements, PDF, iXBRL, readiness and workflow gates. Final statutory use still requires named professional review."),
        new(
            "statutory-source-traceability",
            "Statutory source traceability",
            "hardened",
            "Rule outputs carry LegalSourceReference metadata and the source-law snapshot pins effective dates and URLs."),
        new(
            "frontend-accountant-workbench",
            "Frontend accountant workbench",
            "in-progress",
            "Workbench primitives and filing review surfaces exist; the remaining polish target is a consistent dashboard, route-level states and visual regression coverage."),
        new(
            "operations-security",
            "Operations and security",
            "hardened",
            "CI validates backend, frontend, production compose, production smoke and backup restore. Runtime monitoring and accountant sign-off remain operational gates.")
    ];

    private static IReadOnlyList<GoldenFilingCorpusScenario> BuildGoldenCorpus() =>
    [
        new(
            "micro-ltd",
            "Micro LTD audit-exempt filing",
            "Private company limited by shares, micro regime",
            "generated-pack",
            "covered",
            new(
                "Example Micro Limited",
                "Private",
                "2025-01-01",
                "2025-12-31",
                "Micro",
                "Micro",
                AuditExempt: true,
                ManualProfessionalReviewRequired: false),
            [
                "FilingGoldenCorpusScenarioTests.GoldenCorpus_MicroLtd_EmitsAccountsIxbrlTaxNotesReadinessAndSignatoryGates",
                "AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl"
            ],
            Verifiers(
                "FilingGoldenCorpusScenarioTests.GoldenCorpus_MicroLtd_EmitsAccountsIxbrlTaxNotesReadinessAndSignatoryGates",
                "AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl"),
            [
                "classification selects Micro",
                "readiness has no missing items",
                "balance sheet balances",
                "PDF text includes company, period and micro statement",
                "iXBRL parses as XML",
                "accountant sign-off packet exposes approval blockers and allowed next actions"
            ],
            new(
                [
                    "accounts PDF text",
                    "CRO filing pack",
                    "CRO signature page",
                    "iXBRL XML",
                    "tax computation",
                    "notes disclosure set",
                    "filing readiness profile",
                    "accountant sign-off packet"
                ],
                [
                    "named qualified-accountant review",
                    "director and secretary certification",
                    "external ROS/iXBRL validation",
                    "accountant sign-off packet state"
                ],
                [
                    "Micro regime selected",
                    "100% filing readiness",
                    "balance sheet balances",
                    "well-formed iXBRL",
                    "micro statutory statement present in PDF text"
                ],
                new(
                    [
                        "Example Micro Limited",
                        "31 December 2025",
                        "6,250",
                        "280D"
                    ],
                    [
                        "core:EntityCurrentLegalOrRegisteredName"
                    ],
                    "100% filing readiness",
                    62.50m,
                    [
                        "Accounting Policies"
                    ],
                    [
                        "director and secretary certification required",
                        "external ROS/iXBRL validation required",
                        "qualified-accountant review required"
                    ],
                    "review-required"),
                ProofPoints(
                    "FilingGoldenCorpusScenarioTests.GoldenCorpus_MicroLtd_EmitsAccountsIxbrlTaxNotesReadinessAndSignatoryGates",
                    [
                        new("pdf-text", "PDF text contains company name, period and micro statutory statement."),
                        new("ixbrl-xml", "iXBRL XML is well-formed and contains the company legal name."),
                        new("filing-readiness", "Filing readiness reaches 100% with no missing items."),
                        new("tax-computation", "Tax computation is generated and reconciles to the worked micro scenario."),
                        new("notes-disclosure", "Notes disclosure set includes the required accounting policies."),
                        new("signatory-gates", "Director and secretary certification gates remain required before filing use."),
                        new("accountant-signoff-packet", "Accountant sign-off packet shows reviewer state, open blockers and allowed next actions.")
                    ]),
                [
                    IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                    IrishStatutoryRuleSources.FrcFrs105,
                    IrishStatutoryRuleSources.RevenueIxbrlOverview,
                    IrishStatutoryRuleSources.RevenueAcceptedTaxonomies
                ]),
            LegalBasis(
                "micro-ltd",
                "Private",
                "Micro",
                "Micro",
                auditExempt: true,
                manualProfessionalReviewRequired: false,
                "FRS 105 micro-entities regime with CRO financial-statement and Revenue iXBRL filing evidence.",
                [
                    "accounts PDF text",
                    "CRO filing pack",
                    "CRO signature page",
                    "iXBRL XML",
                    "tax computation",
                    "notes disclosure set",
                    "filing readiness profile",
                    "accountant sign-off packet"
                ],
                [
                    "named qualified-accountant review",
                    "director and secretary certification",
                    "external ROS/iXBRL validation",
                    "accountant sign-off packet state"
                ],
                [
                    IrishStatutoryRuleSources.CroFinancialStatementsRequirements.SourceId,
                    IrishStatutoryRuleSources.FrcFrs105.SourceId,
                    IrishStatutoryRuleSources.RevenueIxbrlOverview.SourceId,
                    IrishStatutoryRuleSources.RevenueAcceptedTaxonomies.SourceId
                ])),
        new(
            "small-abridged-ltd",
            "Small or small-abridged LTD filing",
            "Private company limited by shares, small/abridged regime",
            "generated-pack",
            "covered",
            new(
                "Connacht Digital Solutions Limited",
                "Private",
                "2025-01-01",
                "2025-12-31",
                "Small",
                "SmallAbridged",
                AuditExempt: true,
                ManualProfessionalReviewRequired: false),
            [
                "AccountsWorkflowTests.GoldenPath_SmallAuditExemptCompany_MixedAccrualSetBalancesThroughPdfAndIxbrl",
                "FilingGoldenCorpusScenarioTests.GoldenCorpus_SmallAbridgedLtd_EmitsFullAccountsAbridgedCroPackIxbrlAndReadiness"
            ],
            Verifiers(
                "AccountsWorkflowTests.GoldenPath_SmallAuditExemptCompany_MixedAccrualSetBalancesThroughPdfAndIxbrl",
                "FilingGoldenCorpusScenarioTests.GoldenCorpus_SmallAbridgedLtd_EmitsFullAccountsAbridgedCroPackIxbrlAndReadiness"),
            [
                "classification selects SmallAbridged",
                "mixed cash/accrual facts reconcile",
                "statutory PDF includes legal name, net assets and P&L",
                "CRO abridged pack omits P&L and cites Section 352",
                "signature page carries director and secretary certification",
                "iXBRL parses as XML and omits public P&L turnover",
                "accountant sign-off packet exposes review state and remaining evidence gates"
            ],
            new(
                [
                    "full accounts PDF text",
                    "abridged CRO filing pack",
                    "CRO signature page",
                    "iXBRL XML",
                    "tax computation",
                    "notes disclosure set",
                    "filing readiness profile",
                    "accountant sign-off packet"
                ],
                [
                    "abridgement eligibility",
                    "director and secretary certification",
                    "named qualified-accountant review",
                    "external ROS/iXBRL validation",
                    "accountant sign-off packet state"
                ],
                [
                    "SmallAbridged regime selected",
                    "Section 352 wording present in CRO pack",
                    "public P&L turnover omitted from iXBRL",
                    "tax computation matches worked scenario",
                    "notes include fixed assets and long-term creditors"
                ],
                new(
                    [
                        "Connacht Digital Solutions Limited",
                        "Section 352",
                        "PROFIT AND LOSS ACCOUNT"
                    ],
                    [
                        "core:EntityCurrentLegalOrRegisteredName"
                    ],
                    "generated-output-evidence-required",
                    62.50m,
                    [
                        "Accounting Policies",
                        "Tangible Fixed Assets",
                        "Creditors"
                    ],
                    [
                        "abridgement eligibility confirmed",
                        "director and secretary certification required",
                        "qualified-accountant review required"
                    ],
                    "review-required"),
                ProofPoints(
                    "AccountsWorkflowTests.GoldenPath_SmallAuditExemptCompany_MixedAccrualSetBalancesThroughPdfAndIxbrl",
                    [
                        new("pdf-text", "Full accounts PDF text contains legal name, net assets, profit and loss and Section 352 abridgement wording."),
                        new("ixbrl-xml", "iXBRL XML is well-formed and omits public profit-and-loss turnover for the abridged CRO pack."),
                        new("filing-readiness", "Filing readiness confirms generated CRO, Revenue and accountant-review evidence gates."),
                        new("tax-computation", "Tax computation matches the mixed cash/accrual worked scenario."),
                        new("notes-disclosure", "Notes include fixed assets, creditors and the small-company disclosure set."),
                        new("signatory-gates", "CRO signature page carries director and secretary certification evidence."),
                        new("accountant-signoff-packet", "Accountant sign-off packet shows review state, generated-output evidence and allowed next actions.")
                    ]),
                [
                    IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                    IrishStatutoryRuleSources.FrcFrs102,
                    IrishStatutoryRuleSources.RevenueIxbrlContents,
                    IrishStatutoryRuleSources.RevenueAcceptedTaxonomies
                ]),
            LegalBasis(
                "small-abridged-ltd",
                "Private",
                "Small",
                "SmallAbridged",
                auditExempt: true,
                manualProfessionalReviewRequired: false,
                "FRS 102 small-company abridgement with Section 352 CRO filing evidence and Revenue iXBRL evidence.",
                [
                    "full accounts PDF text",
                    "abridged CRO filing pack",
                    "CRO signature page",
                    "iXBRL XML",
                    "tax computation",
                    "notes disclosure set",
                    "filing readiness profile",
                    "accountant sign-off packet"
                ],
                [
                    "abridgement eligibility",
                    "director and secretary certification",
                    "named qualified-accountant review",
                    "external ROS/iXBRL validation",
                    "accountant sign-off packet state"
                ],
                [
                    IrishStatutoryRuleSources.CroFinancialStatementsRequirements.SourceId,
                    IrishStatutoryRuleSources.FrcFrs102.SourceId,
                    IrishStatutoryRuleSources.RevenueIxbrlContents.SourceId,
                    IrishStatutoryRuleSources.RevenueAcceptedTaxonomies.SourceId
                ])),
        new(
            "dac-small",
            "Small DAC filing",
            "Designated activity company, small FRS 102 regime",
            "generated-pack",
            "covered",
            new(
                "Atlantic Manufacturing DAC",
                "DesignatedActivityCompany",
                "2026-01-01",
                "2026-12-31",
                "Small",
                "Small",
                AuditExempt: true,
                ManualProfessionalReviewRequired: false),
            [
                "FilingGoldenCorpusScenarioTests.GoldenCorpus_DacSmall_EmitsAccountsIxbrlAndSourceBackedReadiness"
            ],
            Verifiers("FilingGoldenCorpusScenarioTests.GoldenCorpus_DacSmall_EmitsAccountsIxbrlAndSourceBackedReadiness"),
            [
                "DAC company type remains in the supported path",
                "small FRS 102 regime is selected",
                "directors' report and CRO signature evidence are generated",
                "iXBRL parses as XML against the Revenue-accepted taxonomy gate",
                "accountant sign-off packet reaches ready-for-external-filing only after reviewer, signatory and external validation evidence"
            ],
            new(
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
                [
                    "DAC company type",
                    "director and secretary certification",
                    "named qualified-accountant review",
                    "external ROS/iXBRL validation",
                    "accountant sign-off packet state"
                ],
                [
                    "Small regime selected",
                    "DAC source-backed readiness",
                    "well-formed iXBRL",
                    "tax computation generated"
                ],
                new(
                    [
                        "Atlantic Manufacturing DAC",
                        "DIRECTORS' REPORT"
                    ],
                    [
                        "bus:EntityCurrentLegalOrRegisteredName"
                    ],
                    "ready-for-external-filing",
                    62.50m,
                    [
                        "Accounting Policies"
                    ],
                    [
                        "director and secretary certification satisfied",
                        "qualified-accountant review recorded",
                        "external ROS/iXBRL validation recorded"
                    ],
                    "ready-for-external-filing"),
                ProofPoints(
                    "FilingGoldenCorpusScenarioTests.GoldenCorpus_DacSmall_EmitsAccountsIxbrlAndSourceBackedReadiness",
                    [
                        new("pdf-text", "DAC accounts PDF text contains the legal name and directors' report."),
                        new("ixbrl-xml", "iXBRL XML is well-formed and contains the DAC legal name."),
                        new("filing-readiness", "Filing readiness confirms DAC source-backed support and all review/signatory/validation gates."),
                        new("tax-computation", "Tax computation is generated for the DAC small-company scenario."),
                        new("notes-disclosure", "Notes include the required accounting policies for the small-company path."),
                        new("signatory-gates", "Director and secretary certification evidence is satisfied before filing actions are allowed."),
                        new("accountant-signoff-packet", "Accountant sign-off packet reaches ready-for-external-filing only after reviewer and external validation evidence.")
                    ]),
                [
                    IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                    IrishStatutoryRuleSources.FrcFrs102,
                    IrishStatutoryRuleSources.RevenueIxbrlOverview,
                    IrishStatutoryRuleSources.RevenueAcceptedTaxonomies
                ]),
            LegalBasis(
                "dac-small",
                "DesignatedActivityCompany",
                "Small",
                "Small",
                auditExempt: true,
                manualProfessionalReviewRequired: false,
                "FRS 102 small-company DAC path with directors' report, CRO certification and Revenue iXBRL evidence.",
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
                [
                    "DAC company type",
                    "director and secretary certification",
                    "named qualified-accountant review",
                    "external ROS/iXBRL validation",
                    "accountant sign-off packet state"
                ],
                [
                    IrishStatutoryRuleSources.CroFinancialStatementsRequirements.SourceId,
                    IrishStatutoryRuleSources.FrcFrs102.SourceId,
                    IrishStatutoryRuleSources.RevenueIxbrlOverview.SourceId,
                    IrishStatutoryRuleSources.RevenueAcceptedTaxonomies.SourceId
                ])),
        new(
            "clg-charity",
            "CLG charity annual reporting",
            "Company limited by guarantee with charity evidence",
            "generated-pack-with-charity-gates",
            "covered",
            new(
                "Dublin Community Support CLG",
                "CompanyLimitedByGuarantee",
                "2026-01-01",
                "2026-12-31",
                "Small",
                "Small",
                AuditExempt: true,
                ManualProfessionalReviewRequired: false),
            [
                "FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness"
            ],
            Verifiers("FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness"),
            [
                "CLG remains in the supported company scope",
                "charity number evidence is required",
                "SoFA and trustees report evidence are required",
                "Charities Regulator source is attached",
                "accountant sign-off packet includes charity evidence and review gates"
            ],
            new(
                [
                    "CLG accounts PDF text",
                    "charity readiness profile",
                    "SoFA evidence",
                    "trustees annual report evidence",
                    "iXBRL XML",
                    "tax computation",
                    "notes disclosure set",
                    "accountant sign-off packet"
                ],
                [
                    "charity number",
                    "charity annual return review",
                    "named qualified-accountant review",
                    "accountant sign-off packet state"
                ],
                [
                    "charity evidence satisfied",
                    "Charities Regulator source attached",
                    "CLG source attached",
                    "well-formed iXBRL"
                ],
                new(
                    [
                        "Dublin Community Support CLG",
                        "Community support and education."
                    ],
                    [
                        "core:EntityCurrentLegalOrRegisteredName"
                    ],
                    "ready-for-external-filing",
                    62.50m,
                    [
                        "Accounting Policies",
                        "Charity reporting disclosures"
                    ],
                    [
                        "charity number satisfied",
                        "SoFA and trustees annual report evidence satisfied",
                        "qualified-accountant review recorded"
                    ],
                    "ready-for-external-filing"),
                ProofPoints(
                    "FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness",
                    [
                        new("pdf-text", "CLG accounts PDF text contains company name and charity objectives."),
                        new("ixbrl-xml", "iXBRL XML is well-formed and contains the CLG legal name."),
                        new("filing-readiness", "Filing readiness confirms charity number, SoFA and trustees report evidence."),
                        new("tax-computation", "Tax computation is generated for the CLG charity scenario."),
                        new("notes-disclosure", "Notes include accounting policies and charity reporting disclosures."),
                        new("signatory-gates", "Charity annual return and named qualified-accountant review gates remain required."),
                        new("accountant-signoff-packet", "Accountant sign-off packet shows charity evidence, review state and allowed next actions.")
                    ]),
                [
                    IrishStatutoryRuleSources.CroGuaranteeCompany,
                    IrishStatutoryRuleSources.CharitiesRegulatorAnnualReport,
                    IrishStatutoryRuleSources.FrcFrs102,
                    IrishStatutoryRuleSources.RevenueIxbrlOverview
                ]),
            LegalBasis(
                "clg-charity",
                "CompanyLimitedByGuarantee",
                "Small",
                "Small",
                auditExempt: true,
                manualProfessionalReviewRequired: false,
                "CLG charity reporting path with CRO guarantee-company, Charities Regulator annual-report and FRS 102 evidence.",
                [
                    "CLG accounts PDF text",
                    "charity readiness profile",
                    "SoFA evidence",
                    "trustees annual report evidence",
                    "iXBRL XML",
                    "tax computation",
                    "notes disclosure set",
                    "accountant sign-off packet"
                ],
                [
                    "charity number",
                    "charity annual return review",
                    "named qualified-accountant review",
                    "accountant sign-off packet state"
                ],
                [
                    IrishStatutoryRuleSources.CroGuaranteeCompany.SourceId,
                    IrishStatutoryRuleSources.CharitiesRegulatorAnnualReport.SourceId,
                    IrishStatutoryRuleSources.FrcFrs102.SourceId,
                    IrishStatutoryRuleSources.RevenueIxbrlOverview.SourceId
                ])),
        new(
            "medium-audit-required",
            "Medium audit-required handoff",
            "Medium company or non-audit-exempt filing",
            "manual-handoff",
            "covered",
            new(
                "Midlands Manufacturing Limited",
                "Private",
                "2026-01-01",
                "2026-12-31",
                "Medium",
                "Medium",
                AuditExempt: false,
                ManualProfessionalReviewRequired: true),
            [
                "FilingGoldenCorpusScenarioTests.GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence"
            ],
            Verifiers("FilingGoldenCorpusScenarioTests.GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence"),
            [
                "audit report evidence is mandatory",
                "normal filing approval is blocked until auditor evidence exists",
                "manual professional handoff is exposed in readiness",
                "CRO medium-company and auditor-report sources are attached",
                "after auditor evidence, the full pack includes auditor report, P&L, cash flow and equity statements",
                "medium iXBRL includes tagged P&L facts",
                "accountant sign-off packet records manual handoff until auditor evidence is present"
            ],
            new(
                [
                    "full accounts PDF text",
                    "auditor report evidence",
                    "cash flow statement",
                    "statement of changes in equity",
                    "iXBRL XML",
                    "filing readiness profile",
                    "tax computation",
                    "accountant sign-off packet"
                ],
                [
                    "auditor handoff",
                    "manual professional review",
                    "normal CRO approval blocked until auditor evidence",
                    "accountant sign-off packet state"
                ],
                [
                    "Medium regime selected",
                    "audit report blocker present before auditor evidence",
                    "tagged P&L facts present after auditor evidence",
                    "auditor reference appears in PDF text"
                ],
                new(
                    [
                        "Midlands Manufacturing Limited",
                        "INDEPENDENT AUDITOR'S REPORT",
                        "AUD-2026-MIDLANDS-001",
                        "CASH FLOW STATEMENT",
                        "STATEMENT OF CHANGES IN EQUITY"
                    ],
                    [
                        "core:TurnoverGrossRevenue",
                        "core:ProfitLossOnOrdinaryActivitiesBeforeTax"
                    ],
                    "manual-handoff-until-auditor-evidence",
                    62.50m,
                    [
                        "Turnover",
                        "Tax on Profit on Ordinary Activities"
                    ],
                    [
                        "auditor handoff blocked until signed auditor report",
                        "normal CRO approval blocked until auditor evidence",
                        "manual professional review required"
                    ],
                    "manual-handoff"),
                ProofPoints(
                    "FilingGoldenCorpusScenarioTests.GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence",
                    [
                        new("pdf-text", "Full accounts PDF text is blocked until auditor evidence exists, then includes auditor report, P&L, cash flow and equity statements."),
                        new("ixbrl-xml", "iXBRL XML is well-formed and contains tagged profit-and-loss facts after auditor evidence."),
                        new("filing-readiness", "Filing readiness blocks approval before signed auditor report evidence and clears that blocker when evidence is recorded."),
                        new("tax-computation", "Tax computation is generated for the medium audit-required scenario."),
                        new("notes-disclosure", "Notes include turnover and tax-on-profit disclosures for the full accounts path."),
                        new("auditor-handoff", "Signed auditor report reference is mandatory before final output generation."),
                        new("accountant-signoff-packet", "Accountant sign-off packet records manual handoff state until signed auditor report evidence is present.")
                    ]),
                [
                    IrishStatutoryRuleSources.CroMediumCompany,
                    IrishStatutoryRuleSources.CroAuditorsReport,
                    IrishStatutoryRuleSources.FrcFrs102,
                    IrishStatutoryRuleSources.RevenueIxbrlOverview
                ]),
            LegalBasis(
                "medium-audit-required",
                "Private",
                "Medium",
                "Medium",
                auditExempt: false,
                manualProfessionalReviewRequired: true,
                "Medium-company audit-required path blocked to manual handoff until auditor report and professional review evidence are present.",
                [
                    "full accounts PDF text",
                    "auditor report evidence",
                    "cash flow statement",
                    "statement of changes in equity",
                    "iXBRL XML",
                    "filing readiness profile",
                    "tax computation",
                    "accountant sign-off packet"
                ],
                [
                    "auditor handoff",
                    "manual professional review",
                    "normal CRO approval blocked until auditor evidence",
                    "accountant sign-off packet state"
                ],
                [
                    IrishStatutoryRuleSources.CroMediumCompany.SourceId,
                    IrishStatutoryRuleSources.CroAuditorsReport.SourceId,
                    IrishStatutoryRuleSources.FrcFrs102.SourceId,
                    IrishStatutoryRuleSources.RevenueIxbrlOverview.SourceId
                ]))
    ];

    private static GoldenFilingCorpusLegalBasisSnapshot LegalBasis(
        string scenarioCode,
        string companyType,
        string sizeClass,
        string electedRegime,
        bool auditExempt,
        bool manualProfessionalReviewRequired,
        string legalBasis,
        IReadOnlyList<string> requiredOutputs,
        IReadOnlyList<string> professionalGates,
        IReadOnlyList<string> sourceIds) =>
        new(
            scenarioCode,
            companyType,
            sizeClass,
            electedRegime,
            auditExempt,
            manualProfessionalReviewRequired,
            legalBasis,
            requiredOutputs,
            professionalGates,
            sourceIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());

    private static IReadOnlyList<StatutoryRuleMatrixEntry> BuildStatutoryRuleMatrix() =>
    [
        new(
            "ltd-micro",
            "LTD micro",
            "Micro / FRS 105",
            "supported",
            [
                "Micro size classification completed and no micro exclusions apply",
                "Active director and company secretary recorded",
                "Named qualified-accountant review recorded",
                "External ROS/iXBRL validation evidence recorded before Revenue use"
            ],
            [
                "Micro entity financial statements PDF",
                "CRO certification/signature page",
                "Revenue iXBRL financial statements package",
                "CT1 support and tax computation evidence"
            ],
            [
                "Fail closed if micro exclusions, group context, regulated status or missing signatories are detected",
                "Manual professional review required before real filing use"
            ],
            [
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.FrcFrs105,
                IrishStatutoryRuleSources.RevenueIxbrlOverview,
                IrishStatutoryRuleSources.RevenueAcceptedTaxonomies
            ]),
        new(
            "ltd-small-abridged",
            "LTD small abridged",
            "Small / abridged FRS 102",
            "supported",
            [
                "Small size classification completed under two-of-three rules",
                "Abridgement eligibility and audit exemption confirmed",
                "Director and secretary evidence recorded",
                "Named qualified-accountant review recorded"
            ],
            [
                "Abridged small-company financial statements PDF",
                "Required statutory statements and notes",
                "CRO certification/signature page",
                "Revenue iXBRL and CT1 support pack"
            ],
            [
                "Fail closed if audit exemption is lost, member audit notice applies, or abridgement is not available",
                "External ROS/iXBRL validation remains a manual evidence gate"
            ],
            [
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.FrcFrs102,
                IrishStatutoryRuleSources.RevenueIxbrlContents,
                IrishStatutoryRuleSources.RevenueAcceptedTaxonomies
            ]),
        new(
            "ltd-small-full",
            "LTD small full accounts",
            "Small / FRS 102",
            "supported",
            [
                "Small size classification completed under two-of-three rules",
                "Audit exemption confirmed or audit report evidence recorded if exemption is unavailable",
                "Abridgement not elected or not available for this filing package",
                "Director and secretary evidence recorded",
                "Named qualified-accountant review recorded"
            ],
            [
                "Full small-company financial statements PDF",
                "Profit and loss account where required for the selected filing package",
                "Required statutory statements and notes",
                "CRO certification/signature page",
                "Revenue iXBRL and CT1 support pack"
            ],
            [
                "Manual handoff if audit exemption is lost, member audit notice applies, or abridgement/accountant judgement changes the filing path",
                "External ROS/iXBRL validation remains a manual evidence gate"
            ],
            [
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.FrcFrs102,
                IrishStatutoryRuleSources.RevenueIxbrlContents,
                IrishStatutoryRuleSources.RevenueAcceptedTaxonomies
            ]),
        new(
            "dac-small",
            "DAC small",
            "Small / FRS 102",
            "supported",
            [
                "DAC company type and size classification recorded",
                "Audit exemption and filing regime confirmed",
                "Director and secretary evidence recorded",
                "Named qualified-accountant review recorded",
                "External ROS/iXBRL validation evidence recorded before Revenue use"
            ],
            [
                "Small company financial statements PDF",
                "Directors' report evidence",
                "CRO certification/signature page",
                "Revenue iXBRL and CT1 support pack"
            ],
            [
                "Manual handoff if audit is required, regulated exclusions apply, or group context is present",
                "No direct CRO/ROS submission automation"
            ],
            [
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.FrcFrs102,
                IrishStatutoryRuleSources.RevenueIxbrlOverview,
                IrishStatutoryRuleSources.RevenueAcceptedTaxonomies
            ]),
        new(
            "clg-non-charity",
            "CLG non-charity",
            "Company limited by guarantee / FRS 102",
            "supported",
            [
                "Company limited by guarantee status recorded",
                "Non-charity status confirmed so charity annual-return evidence is not expected",
                "Members-guarantee and officers evidence recorded",
                "Named qualified-accountant review recorded"
            ],
            [
                "Guarantee-company financial statements PDF",
                "Directors' report evidence",
                "CRO certification/signature page",
                "Revenue iXBRL and CT1 support pack where corporation tax filing applies"
            ],
            [
                "Manual handoff if charity status, audit requirement, regulated exclusions or group context are detected",
                "External ROS/iXBRL validation remains a manual evidence gate",
                "No direct CRO/ROS submission automation"
            ],
            [
                IrishStatutoryRuleSources.CroGuaranteeCompany,
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.FrcFrs102,
                IrishStatutoryRuleSources.RevenueIxbrlOverview,
                IrishStatutoryRuleSources.RevenueAcceptedTaxonomies
            ]),
        new(
            "clg-charity",
            "CLG charity",
            "CLG / charity annual return",
            "supported-with-review",
            [
                "CLG company type recorded",
                "Charity number and charity profile completed",
                "SoFA and trustees' annual report evidence generated",
                "Named qualified-accountant or charity reviewer approval recorded"
            ],
            [
                "CLG financial statements PDF",
                "Charity SoFA",
                "Trustees' annual report support",
                "Revenue iXBRL package where corporation tax filing applies"
            ],
            [
                "Charity annual return must be manually reviewed before use",
                "External ROS/iXBRL validation remains a manual evidence gate where corporation tax filing applies",
                "Manual handoff required for complex charity governance, restricted funds or regulator-specific queries"
            ],
            [
                IrishStatutoryRuleSources.CroGuaranteeCompany,
                IrishStatutoryRuleSources.CharitiesRegulatorAnnualReport,
                IrishStatutoryRuleSources.FrcFrs102,
                IrishStatutoryRuleSources.RevenueIxbrlOverview,
                IrishStatutoryRuleSources.RevenueAcceptedTaxonomies
            ]),
        new(
            "medium-audit-required",
            "Medium audit-required",
            "Medium / full FRS 102",
            "manual-handoff",
            [
                "Medium size classification completed",
                "Signed auditor report evidence recorded",
                "Full financial statements and directors' report evidence reviewed",
                "Named qualified-accountant and auditor handoff recorded"
            ],
            [
                "Full accounts support pack",
                "Signed statutory auditor's report evidence record",
                "Revenue iXBRL and CT1 support pack"
            ],
            [
                "Final outputs remain blocked until signed auditor report evidence is recorded",
                "External ROS/iXBRL validation remains a manual evidence gate",
                "Manual professional handoff required before approval or submission states"
            ],
            [
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.CroMediumCompany,
                IrishStatutoryRuleSources.CroAuditorsReport,
                IrishStatutoryRuleSources.FrcFrs102,
                IrishStatutoryRuleSources.RevenueIxbrlOverview,
                IrishStatutoryRuleSources.RevenueAcceptedTaxonomies
            ]),
        new(
            "unsupported-regulated-group",
            "Unsupported regulated/group",
            "PLC, regulated, unlimited variants, group/holding/subsidiary",
            "unsupported",
            [
                "Manual professional ownership recorded",
                "Reason for unsupported path captured",
                "External specialist review evidence retained outside automated filing workflow"
            ],
            [
                "Manual handoff record",
                "Evidence checklist export only; no automated final filing pack approval"
            ],
            [
                "Fail closed before filing workflow approval",
                "Direct CRO/ROS submission remains unsupported",
                "Specialist accountant or auditor must complete the statutory filing outside the automated path"
            ],
            [
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.CroGroupCompany,
                IrishStatutoryRuleSources.CroUnlimitedCompany,
                IrishStatutoryRuleSources.FrcFrs102
            ])
    ];

    private static IReadOnlyList<StatutoryRulesCoverageItem> BuildStatutoryRulesCoverage() =>
    [
        new(
            "size-classification-thresholds",
            "Size classification",
            "Two-of-three thresholds, current/prior-year movement and first-year classification must produce the correct statutory size class before regime selection.",
            "covered",
            [
                "AccountsWorkflowTests.SizeClassification_FirstYearMicro_AllowsMicroAndAuditExemption",
                "AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl",
                "AccountsWorkflowTests.GoldenPath_SmallAuditExemptCompany_MixedAccrualSetBalancesThroughPdfAndIxbrl",
                "FilingGoldenCorpusScenarioTests.GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence"
            ],
            [
                "two-of-three threshold rule",
                "current and prior year classification rule",
                "micro, small and medium boundary scenarios",
                "classification must run before filing regime selection"
            ],
            [
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.CroMediumCompany
            ]),
        new(
            "micro-exclusions-and-fifth-schedule",
            "Micro exclusions and excluded entities",
            "Micro and small-company benefits must fail closed where holding, investment, subsidiary, regulated or Fifth Schedule exclusion flags make automation unsafe.",
            "covered",
            [
                "FilingReadinessProfileTests.ReadinessProfile_ForRegulatedOrGroupEntities_FailsClosed",
                "FilingReadinessProfileTests.ReadinessProfile_ForUnsupportedCompanyTypes_FailsClosedToManualHandoff"
            ],
            [
                "group, holding and subsidiary contexts",
                "regulated and Fifth Schedule excluded entities",
                "manual handoff when the model cannot safely apply micro or audit-exemption benefits"
            ],
            [
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.CroGroupCompany,
                IrishStatutoryRuleSources.FrcFrs102
            ]),
        new(
            "audit-exemption-loss",
            "Audit exemption",
            "Audit exemption must be withdrawn or blocked where repeated late CRO filings, member audit notice or medium/full filing conditions require audit evidence.",
            "covered",
            [
                "AccountsWorkflowTests.FilingRegime_RecentRepeatedLateCroFilings_RemoveAuditExemption",
                "AccountsWorkflowTests.ClassificationRoutes_EnforceRuntimePeriodRoleAndApiAuthorization",
                "FilingGoldenCorpusScenarioTests.GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence"
            ],
            [
                "late CRO filings remove audit exemption",
                "member audit notice evidence is recorded through authorised routes",
                "signed auditor report evidence required for medium or non-audit-exempt paths"
            ],
            [
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.CroMediumCompany,
                IrishStatutoryRuleSources.CroAuditorsReport
            ]),
        new(
            "required-outputs-and-filing-gates",
            "Required outputs and filing gates",
            "Generated accounts, CRO pack, iXBRL, CT1/tax support, notes and signatory evidence must be present before a supported path can move toward external filing.",
            "covered",
            [
                "AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl",
                "AccountsWorkflowTests.GoldenPath_SmallAuditExemptCompany_MixedAccrualSetBalancesThroughPdfAndIxbrl",
                "FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness",
                "AccountsWorkflowTests.PeriodStatusEndpoint_RejectsFinaliseOrFileWhenReadinessBlockersRemain"
            ],
            [
                "PDF text assertions",
                "well-formed iXBRL XML assertions",
                "tax computation and notes proof points",
                "director, secretary, accountant-review and external ROS validation gates"
            ],
            [
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.RevenueIxbrlContents,
                IrishStatutoryRuleSources.RevenueAcceptedTaxonomies
            ]),
        new(
            "unsupported-fail-closed",
            "Unsupported paths",
            "PLC, unlimited variants, regulated entities, group contexts, direct CRO/ROS submission and complex claims must stop before normal approval and require manual professional handoff.",
            "covered",
            [
                "FilingReadinessProfileTests.ReadinessProfile_ForUnsupportedCompanyTypes_FailsClosedToManualHandoff",
                "FilingReadinessProfileTests.ApprovalGuard_ForUnsupportedCompanyType_BlocksNormalCroApprovalPath",
                "FilingReadinessProfileTests.ReadinessProfile_ForRegulatedOrGroupEntities_FailsClosed"
            ],
            [
                "PLC and public company paths",
                "regulated and group contexts",
                "direct CRO or ROS submission is not automated",
                "manual professional handoff remains the only allowed external filing path"
            ],
            [
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.CroGroupCompany,
                IrishStatutoryRuleSources.CroUnlimitedCompany,
                IrishStatutoryRuleSources.RevenueIxbrlOverview
            ])
    ];

    private static IReadOnlyList<string> BuildManualHandoffPaths() =>
    [
        "PLC and public-company workflows",
        "Private unlimited company variants not safely modelled",
        "Listed, credit institution, insurance undertaking, pension fund and other excluded entities",
        "Group, holding, subsidiary and consolidation contexts",
        "Audit-required filings without a signed auditor report",
        "Complex corporation tax claims, group relief and loss elections",
        "Direct CRO or ROS submission"
    ];

    private static IReadOnlyList<OperationalGate> BuildOperationalGates() =>
    [
        new(
            "qualified-accountant-review",
            "Named qualified-accountant review",
            true,
            "required",
            "Generated statutory packs cannot be treated as filing-ready until a named qualified accountant has reviewed and approved them."),
        new(
            "external-ros-validation",
            "External ROS/iXBRL validation",
            true,
            "required",
            "The platform records internal iXBRL checks only; external ROS validation remains a manual evidence gate."),
        new(
            "director-secretary-certification",
            "Director and secretary certification",
            true,
            "required",
            "CRO filing workflow requires active director and company secretary evidence."),
        new(
            "no-direct-cro-ros-submission",
            "No direct CRO/ROS submission automation",
            true,
            "enforced",
            "The workflow records generated, reviewed, approved, submitted, paid and accepted/rejected states only."),
        new(
            "production-ci-gates",
            "Production CI gates",
            true,
            "enforced",
            "CI runs backend, frontend, dependency audit, production compose config, production stack smoke and backup restore checks.")
    ];

    private static IReadOnlyList<ProductionReadinessAssuranceAction> BuildAssuranceActions() =>
    [
        new(
            "qualified-accountant-signoff",
            "Qualified accountant sign-off",
            "Qualified accountant",
            "critical",
            0,
            "accountant-review-gate",
            "required",
            "No generated filing pack can be treated as final until a named qualified accountant has reviewed the evidence, outputs and wording.",
            "Named qualified-accountant approval recorded against the period and linked to the generated pack."),
        new(
            "source-law-change-review",
            "Source-law change review",
            "Qualified accountant and engineering",
            "critical",
            2,
            "source-law-maintenance",
            "required",
            "Pinned CRO, Revenue, FRC and charity guidance must be reviewed for effective-date or wording changes before release.",
            "Source-law change review note and qualified-accountant sign-off recorded against the snapshot."),
        new(
            "external-ros-validation",
            "External ROS/iXBRL validation",
            "Reviewer",
            "critical",
            5,
            "external-validation-gate",
            "required",
            "Internal XML parsing is not a Revenue acceptance check, so real filings need a recorded external ROS validation result.",
            "External ROS validation evidence uploaded or referenced before any Revenue filing state is marked accepted."),
        new(
            "no-direct-cro-ros-submission",
            "No direct CRO/ROS submission automation",
            "Engineering",
            "critical",
            6,
            "unsupported-path-gate",
            "complete",
            "The platform must never automate final CRO or ROS submission; it records workflow states and external references only.",
            "Release reviewer confirms final filing operations remain recorded workflow states only and no direct submission client is wired."),
        new(
            "accountant-acceptance-walkthrough",
            "Accountant acceptance walkthrough",
            "Qualified accountant",
            "high",
            10,
            "golden-corpus-acceptance",
            "required",
            "A qualified accountant must take the golden scenarios through the live workflow and confirm outputs, gates and wording are professionally acceptable.",
            "Signed acceptance note covering micro LTD, small abridged LTD, CLG charity and medium/audit-required manual handoff."),
        new(
            "production-monitoring",
            "Production monitoring",
            "Operations",
            "high",
            20,
            "operations-evidence",
            "required",
            "Runtime failures must be visible to operators before real statutory filing packs are processed.",
            "Sentry production error routing configured and reviewed with structured log correlation."),
        new(
            "light-dark-visual-regression",
            "Light/dark visual regression",
            "Engineering",
            "high",
            30,
            "visual-qa-evidence",
            "in-progress",
            "The accountant journey needs desktop and mobile screenshots across light and dark mode before it can be called visually production-ready.",
            "Screenshots for dashboard, production readiness, company detail, period workspace, filing review and the workbench component preview in light desktop, dark desktop, light mobile and dark mobile.")
    ];

    private static IReadOnlyList<ProductionReadinessCompletionTrack> BuildCompletionTracks() =>
    [
        new(
            "backend-code",
            "Backend code",
            "Engineering",
            "review-required",
            [
                "Golden filing corpus proves PDF text, iXBRL XML, tax, notes, readiness and gates.",
                "Source-law snapshot and traceability cover every statutory decision.",
                "Production auditability captures who changed, approved, generated and submitted each pack."
            ],
            [
                "Backend golden corpus scenarios are covered by automated verifiers.",
                "Statutory rules coverage is mapped to executable tests.",
                "Production auditability controls and audit evidence timeline are declared.",
                "No direct CRO/ROS submission automation is enforced as recorded workflow states only."
            ],
            [
                "Run qualified-accountant acceptance on the golden corpus.",
                "Record source-law change review evidence against the pinned snapshot.",
                "Attach external ROS/iXBRL validation evidence for generated iXBRL packs.",
                "Verify Sentry/error routing, structured logs and backup restore evidence.",
                "Record manual handoff acceptance for audit-required paths."
            ],
            [
                "qualified-accountant-signoff",
                "source-law-change-review",
                "external-ros-validation",
                "no-direct-cro-ros-submission",
                "accountant-acceptance-walkthrough",
                "production-monitoring"
            ]),
        new(
            "frontend-ui-ux",
            "Frontend UI/UX",
            "Product design",
            "in-progress",
            [
                "Accountant workflow rail is visually coherent across the core journey.",
                "Light/dark visual regression covers desktop and mobile.",
                "Dense review workbench surfaces blockers, evidence, sources and next actions without visual clutter."
            ],
            [
                "Visual QA route audit covers the accountant workbench routes.",
                "Dashboard filing deep links send deadline-pressure and manual-handoff work directly to the period filing review tab.",
                "Period filing gate snapshot shows supported/manual path, accountant review state, external filing readiness and allowed next action.",
                "Route-level loading/error states exist for main dynamic routes.",
                "Permission-denied filing action state keeps evidence visible while blocking ineligible review actions.",
                "Workbench primitives are used in the readiness and period review surfaces."
            ],
            [
                "Review each screenshot route-by-route in light and dark mode.",
                "Polish spacing, typography, table density, empty states and mobile flow.",
                "Record named visual acceptance against the smoke manifest."
            ],
            [
                "light-dark-visual-regression",
                "accountant-acceptance-walkthrough"
            ]),
        new(
            "frontend-code",
            "Frontend code",
            "Frontend engineering",
            "in-progress",
            [
                "Shared workbench primitives cover repeated page patterns.",
                "Typed API contract blocks frontend/backend readiness drift.",
                "Route-level states cover loading, error, empty and permission-denied cases."
            ],
            [
                "API client invariants validate production readiness contracts.",
                "Component-preview route exercises shared workbench primitives.",
                "FilingReviewCentre permission gate blocks approval/submission actions behind canReview and renders PermissionDeniedPanel for ineligible roles.",
                "PeriodFilingWorkspace extraction composes review, deadline, warning, output and audit panels behind one focused filing workflow component.",
                "PeriodImportWorkspace extraction composes classification, bank account, opening-balance, CSV upload and import-status panels behind one focused import workflow component.",
                "PeriodCategoriseWorkspace extraction composes metrics, transaction rules, bulk actions, filters and categorisation table behind one focused transaction review component.",
                "PeriodYearEndWorkspace extraction composes questionnaire, completeness, summary metrics and empty-state panels behind one focused year-end workflow component.",
                "PeriodAdjustmentsWorkspace extraction composes generation, summary, filters and approval review cards behind one focused adjustments workflow component.",
                "PeriodStatementsWorkspace extraction composes readiness, statements, notes and charity reporting navigation behind one focused statements workflow component.",
                "Render tests cover accountant dashboards, review panels and workflow routes."
            ],
            [
                "Continue extracting large route files into focused workflow components.",
                "Expand visual regression assertions from screenshot capture into reviewable sign-off.",
                "Keep route fixtures aligned with backend readiness evidence."
            ],
            [
                "light-dark-visual-regression"
            ])
    ];

    private static IReadOnlyList<ProductionReleaseBlocker> BuildReleaseBlockerRegister(
        IReadOnlyList<ProductionReadinessCompletionTrack> completionTracks,
        IReadOnlyList<ProductionReadinessAssuranceAction> assuranceActions,
        IReadOnlyList<ReleaseReviewChecklistItem> releaseReviewChecklist)
    {
        var actionsByCode = assuranceActions.ToDictionary(action => action.Code, StringComparer.Ordinal);
        var checklistByAction = releaseReviewChecklist.ToDictionary(item => item.AssuranceActionCode, StringComparer.Ordinal);
        var blockers = new List<ProductionReleaseBlocker>();

        foreach (var track in completionTracks)
        {
            foreach (var actionCode in track.AssuranceActionCodes.Distinct(StringComparer.Ordinal))
            {
                if (!actionsByCode.TryGetValue(actionCode, out var action))
                    throw new InvalidOperationException($"Completion track {track.Code} references unknown assurance action {actionCode}.");

                if (action.Status == "complete")
                    continue;

                if (!checklistByAction.TryGetValue(action.Code, out var checklist))
                    throw new InvalidOperationException($"Open assurance action {action.Code} does not have a release checklist item.");

                blockers.Add(new ProductionReleaseBlocker(
                    $"{track.Code}:{action.Code}",
                    track.Code,
                    track.Label,
                    checklist.OwnerRole,
                    action.Priority,
                    action.RiskRank,
                    FormatReleaseBlockingIssue(action),
                    action.EvidenceRequired,
                    SelectNextAction(track, action),
                    action.Code,
                    checklist.Code,
                    checklist.OperationalGateCode,
                    checklist.EvidenceArtifact,
                    checklist.BlocksRelease));
            }
        }

        return blockers
            .OrderBy(blocker => blocker.RiskRank)
            .ThenBy(blocker => blocker.TrackCode, StringComparer.Ordinal)
            .ThenBy(blocker => blocker.SourceActionCode, StringComparer.Ordinal)
            .ToArray();
    }

    private static string FormatReleaseBlockingIssue(ProductionReadinessAssuranceAction action) =>
        action.Code == "qualified-accountant-signoff"
            ? "Qualified accountant sign-off required"
            : $"{action.Label} required";

    private static string SelectNextAction(
        ProductionReadinessCompletionTrack track,
        ProductionReadinessAssuranceAction action)
    {
        var match = track.NextActions.FirstOrDefault(nextAction => action.Code switch
        {
            "source-law-change-review" => nextAction.Contains("source-law", StringComparison.OrdinalIgnoreCase),
            "external-ros-validation" => nextAction.Contains("ROS/iXBRL", StringComparison.OrdinalIgnoreCase),
            "production-monitoring" => nextAction.Contains("Sentry", StringComparison.OrdinalIgnoreCase)
                || nextAction.Contains("backup", StringComparison.OrdinalIgnoreCase),
            "light-dark-visual-regression" => nextAction.Contains("screenshot", StringComparison.OrdinalIgnoreCase)
                || nextAction.Contains("visual", StringComparison.OrdinalIgnoreCase),
            "accountant-acceptance-walkthrough" => nextAction.Contains("acceptance", StringComparison.OrdinalIgnoreCase)
                || nextAction.Contains("visual acceptance", StringComparison.OrdinalIgnoreCase),
            "qualified-accountant-signoff" => nextAction.Contains("qualified-accountant", StringComparison.OrdinalIgnoreCase)
                || nextAction.Contains("acceptance", StringComparison.OrdinalIgnoreCase),
            _ => nextAction.Contains(action.Label, StringComparison.OrdinalIgnoreCase)
        });

        return match ?? track.NextActions.FirstOrDefault() ?? action.EvidenceRequired;
    }

    private static IReadOnlyList<ReleaseReviewChecklistItem> BuildReleaseReviewChecklist(
        IReadOnlyList<ProductionReadinessAssuranceAction> assuranceActions,
        IReadOnlyList<OperationalGate> operationalGates)
    {
        var actionStatuses = assuranceActions.ToDictionary(action => action.Code, action => action.Status, StringComparer.Ordinal);
        var gateCodes = operationalGates.Select(gate => gate.Code).ToHashSet(StringComparer.Ordinal);

        ReleaseReviewChecklistItem Item(
            string code,
            string label,
            string ownerRole,
            string evidenceArtifact,
            string assuranceActionCode,
            string operationalGateCode,
            IReadOnlyList<string> auditEventCodes,
            string detail)
        {
            if (!actionStatuses.TryGetValue(assuranceActionCode, out var status))
                throw new InvalidOperationException($"Release checklist item {code} references unknown assurance action {assuranceActionCode}.");

            if (!string.IsNullOrWhiteSpace(operationalGateCode) && !gateCodes.Contains(operationalGateCode))
                throw new InvalidOperationException($"Release checklist item {code} references unknown operational gate {operationalGateCode}.");

            return new ReleaseReviewChecklistItem(
                code,
                label,
                ownerRole,
                Required: true,
                status,
                BlocksRelease: status != "complete",
                evidenceArtifact,
                assuranceActionCode,
                operationalGateCode,
                auditEventCodes,
                detail);
        }

        return
        [
            Item(
                "accountant-final-signoff",
                "Named accountant final sign-off",
                "Qualified accountant",
                "named-accountant-approval-record",
                "qualified-accountant-signoff",
                "qualified-accountant-review",
                [
                    AuditEventCodes.CroFilingStatusChanged,
                    AuditEventCodes.CharityFilingStatusChanged,
                    AuditEventCodes.YearEndReviewConfirmationUpdated
                ],
                "Named professional approval must be recorded against the period before any real filing pack is treated as final."),
            Item(
                "source-law-change-review",
                "Source-law change review",
                "Qualified accountant and engineering",
                "source-law-change-review-note",
                "source-law-change-review",
                "qualified-accountant-review",
                [
                    AuditEventCodes.CroFilingStatusChanged,
                    AuditEventCodes.IxbrlInternalCheckCompleted,
                    AuditEventCodes.CharityFilingStatusChanged
                ],
                "Pinned CRO, Revenue, FRC and charity guidance must be reviewed for effective-date or wording changes before release."),
            Item(
                "external-ros-validation-evidence",
                "External ROS/iXBRL validation evidence",
                "Reviewer",
                "external-ros-validation-reference",
                "external-ros-validation",
                "external-ros-validation",
                [AuditEventCodes.IxbrlInternalCheckCompleted],
                "Internal XML checks are not enough for Revenue acceptance; the reviewer must retain external validation evidence."),
            Item(
                "no-direct-cro-ros-submission",
                "No direct CRO/ROS submission automation",
                "Engineering",
                "no-direct-cro-ros-submission-control",
                "no-direct-cro-ros-submission",
                "no-direct-cro-ros-submission",
                [],
                "Release reviewer confirms final filing operations remain generated, reviewed, approved, marked submitted, payment confirmed, accepted, rejected or corrected recorded workflow states only."),
            Item(
                "golden-corpus-accountant-acceptance",
                "Golden corpus accountant acceptance",
                "Qualified accountant",
                "signed-golden-corpus-acceptance-note",
                "accountant-acceptance-walkthrough",
                "qualified-accountant-review",
                [
                    AuditEventCodes.CroDocumentGenerated,
                    AuditEventCodes.IxbrlInternalCheckCompleted,
                    AuditEventCodes.NotesGenerated
                ],
                "A qualified accountant must walk the golden scenarios through the live workflow and accept outputs, gates and wording."),
            Item(
                "production-smoke-and-backup",
                "Production smoke and backup evidence",
                "Operations",
                "ci-production-stack-smoke-and-backup-restore",
                "production-monitoring",
                "production-ci-gates",
                [],
                "Release evidence must include successful production stack smoke, visual smoke, monitoring configuration and backup restore drill."),
            Item(
                "visual-qa-screenshot-review",
                "Light/dark visual QA screenshot review",
                "Engineering",
                "light-dark-desktop-mobile-screenshot-review",
                "light-dark-visual-regression",
                "production-ci-gates",
                [],
                "Desktop and mobile screenshots in light and dark mode must be reviewed for the accountant workflow before release.")
        ];
    }

    private static IReadOnlyList<ReleaseVerificationManifestItem> BuildReleaseVerificationManifest() =>
    [
        new(
            "backend-golden-corpus",
            "Backend golden corpus and statutory rules",
            "Engineering",
            "dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art",
            "default-ci",
            RunsInDefaultCi: true,
            BlocksRelease: true,
            "backend-test-results",
            "signed-golden-corpus-acceptance-note",
            "Run the same command locally from backend/ when GitHub Actions is unavailable, then retain the console output with the release evidence pack."),
        new(
            "frontend-workbench-contract",
            "Frontend workbench contract, render and API checks",
            "Engineering",
            "npm test",
            "default-ci",
            RunsInDefaultCi: true,
            BlocksRelease: true,
            "frontend-test-results",
            "light-dark-desktop-mobile-screenshot-review",
            "Run from frontend/ and retain the unit, render, readiness, proxy, auth and API-client verifier output."),
        new(
            "frontend-production-build",
            "Frontend lint, type-check and production build",
            "Engineering",
            "npm run lint; npx tsc --noEmit --incremental false; npm run build",
            "default-ci",
            RunsInDefaultCi: true,
            BlocksRelease: true,
            "frontend-build-results",
            "light-dark-desktop-mobile-screenshot-review",
            "Run from frontend/ and retain lint, TypeScript and Next production build output when CI is unavailable."),
        new(
            "visual-smoke-light-dark",
            "Light/dark desktop/mobile visual smoke",
            "Engineering",
            "node scripts/visual-smoke.mjs; node scripts/verify-visual-smoke-artifacts.mjs --report-path=artifacts/visual-smoke/visual-smoke-evidence-report.json; node scripts/verify-accountant-workbench-evidence.mjs --visual-report=artifacts/visual-smoke/visual-smoke-evidence-report.json --report-path=artifacts/visual-smoke/accountant-workbench-evidence-report.json",
            "default-ci",
            RunsInDefaultCi: true,
            BlocksRelease: true,
            "artifacts/visual-smoke",
            "light-dark-desktop-mobile-screenshot-review",
            "Run the visual smoke locally against seeded production-like data if CI cannot capture screenshots, then retain the manifest verification output and review the generated artifacts manually."),
        new(
            "source-law-change-review",
            "Source-law change review note",
            "Qualified accountant and engineering",
            "manual review: compare pinned CRO, Revenue, FRC and charity guidance against source-law snapshot",
            "manual-release",
            RunsInDefaultCi: false,
            BlocksRelease: true,
            "source-law-change-review-note",
            "source-law-change-review-note",
            "Before real filing release, retain a dated review note confirming each pinned source URL, effective date, wording impact and qualified-accountant acceptance."),
        new(
            "qualified-accountant-final-signoff",
            "Named accountant final sign-off evidence",
            "Qualified accountant",
            "manual review: record named qualified-accountant approval against the final generated filing pack",
            "manual-release",
            RunsInDefaultCi: false,
            BlocksRelease: true,
            "named-accountant-approval-record",
            "named-accountant-approval-record",
            "A named qualified accountant must approve the exact generated PDF, iXBRL, tax computation, notes, source-law evidence and filing gate state before real filing use."),
        new(
            "external-ros-validation-evidence",
            "External ROS/iXBRL validation evidence",
            "Reviewer",
            "manual review: retain external ROS/iXBRL validation reference for the exact generated iXBRL pack",
            "manual-release",
            RunsInDefaultCi: false,
            BlocksRelease: true,
            "external-ros-validation-reference",
            "external-ros-validation-reference",
            "Internal XML checks are not sufficient for real Revenue filing use; retain the external validation reference before final approval."),
        new(
            "no-direct-cro-ros-submission-control",
            "No direct CRO/ROS submission automation control",
            "Engineering",
            "manual review: confirm final CRO and ROS operations remain recorded workflow states only with no direct submission client configured",
            "manual-release",
            RunsInDefaultCi: false,
            BlocksRelease: true,
            "no-direct-cro-ros-submission-control",
            "no-direct-cro-ros-submission-control",
            "Confirm final filing operations remain recorded workflow states only: generated, reviewed, approved, marked submitted, payment confirmed, accepted, rejected or corrected."),
        new(
            "production-readiness-report-verification",
            "Production readiness report verification",
            "Engineering",
            "pwsh ./scripts/verify-production-readiness-report.ps1 -ReportPath production-readiness-report.json -EvidencePath production-readiness-verification-report.json",
            "default-ci",
            RunsInDefaultCi: true,
            BlocksRelease: true,
            "production-readiness-report",
            "ci-production-stack-smoke-and-backup-restore",
            "Run after capturing the live /api/system/production-readiness response and retain production-readiness-verification-report.json with the release evidence pack."),
        new(
            "production-stack-smoke",
            "Production compose smoke",
            "Operations",
            "pwsh ./scripts/smoke-production.ps1 -CheckMonitoringErrorRouting; pwsh ./scripts/verify-production-readiness-report.ps1; pwsh ./scripts/verify-structured-logs.ps1",
            "default-ci",
            RunsInDefaultCi: true,
            BlocksRelease: true,
            "ci-production-stack-smoke-and-backup-restore",
            "ci-production-stack-smoke-and-backup-restore",
            "Run the production smoke script against the production compose profile and retain health, login, monitoring-error-routing-report.json, production-readiness-verification-report.json, structured-log-report.json and filing-workflow output."),
        new(
            "backup-restore-drill",
            "PostgreSQL backup and restore drill",
            "Operations",
            "pwsh ./scripts/verify-postgres-backup.ps1",
            "default-ci",
            RunsInDefaultCi: true,
            BlocksRelease: true,
            "ci-production-stack-smoke-and-backup-restore",
            "ci-production-stack-smoke-and-backup-restore",
            "Run the backup verification script after creating a fresh production-shape dump and retain the checksum and restore verification output."),
        new(
            "ci-machine-evidence-pack",
            "CI machine evidence pack",
            "Engineering",
            "pwsh ./scripts/verify-ci-machine-evidence-pack.ps1 -EvidenceDirectory <downloaded-ci-artifacts> -ReportPath ci-machine-evidence-pack-report.json -CommitSha <release-commit-sha> -GitHubActionsRunUrl <ci-run-url>",
            "default-ci",
            RunsInDefaultCi: true,
            BlocksRelease: true,
            "ci-machine-evidence-pack",
            "ci-production-stack-smoke-and-backup-restore",
            "Run after CI downloads the dependency, production safety, monitoring, structured log, backup/restore, no-direct-submission, production-readiness and visual/workbench artifacts for the exact candidate."),
        new(
            "release-artifact-pack",
            "Release artifact pack verification",
            "Engineering",
            "pwsh ./scripts/verify-release-artifact-pack.ps1 -EvidenceDirectory <release-artifacts> -ReportPath release-artifact-pack-report.json -CommitSha <release-commit-sha> -GitHubActionsRunUrl <ci-run-url>",
            "manual-release",
            RunsInDefaultCi: false,
            BlocksRelease: true,
            "release-artifact-pack-report",
            "named-accountant-approval-record",
            "Run against the collected dependency, production safety, monitoring, log, restore, no-direct, production-readiness, visual, workbench and human release-evidence reports for the exact release candidate."),
        new(
            "postgres-gated-audit-tests",
            "PostgreSQL-gated audit durability tests",
            "Engineering",
            "dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~PostgresIntegration",
            "environment-gated",
            RunsInDefaultCi: false,
            BlocksRelease: true,
            "postgres-integration-test-results",
            "ci-production-stack-smoke-and-backup-restore",
            "Set ACCOUNTS_POSTGRES_TEST_CONNECTION to a disposable PostgreSQL database and run the command from backend/ before relying on audit durability evidence."),
        new(
            "manual-accountant-acceptance",
            "Named accountant acceptance walkthrough",
            "Qualified accountant",
            "manual walkthrough: micro LTD, small abridged LTD, CLG charity and medium/audit-required handoff",
            "manual-release",
            RunsInDefaultCi: false,
            BlocksRelease: true,
            "signed-golden-corpus-acceptance-note",
            "signed-golden-corpus-acceptance-note",
            "A named qualified accountant must review the generated outputs, gates, wording and source-law evidence before any real filing pack is treated as final.")
    ];

    private static IReadOnlyList<GoldenFilingCorpusProofPoint> ProofPoints(
        string automatedVerifier,
        IReadOnlyList<(string Area, string ExpectedEvidence)> proofPoints) =>
        proofPoints
            .Select(proof => new GoldenFilingCorpusProofPoint(
                proof.Area,
                proof.ExpectedEvidence,
                automatedVerifier,
                Required: true))
            .ToArray();

    private static IReadOnlyList<GoldenFilingCorpusVerifier> Verifiers(params string[] verifierNames) =>
        verifierNames
            .Select(name => new GoldenFilingCorpusVerifier(
                name,
                $"dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~{name}",
                "default-ci",
                RunsInDefaultCi: true,
                "EF Core InMemory golden fixture; CI also runs the broader backend suite on Linux",
                "end-to-end golden filing scenario"))
            .ToArray();

    private static IReadOnlyList<ProductionAuditabilityControl> BuildAuditabilityControls() =>
    [
        new(
            "who-changed-what",
            "Who changed what",
            true,
            "audit-log-integrity-chain",
            "Authenticated user id, reviewer display name, request id, timestamp, entity, action, and old/new value snapshots with sensitive fields redacted.",
            "AuditLog integrity hashes link each company-scoped entry to the previous entry; audit durability tests verify failed business writes still preserve audit rows where required.",
            [
                AuditEventCodes.SizeClassificationDataSaved,
                AuditEventCodes.FilingRegimeDetermined,
                AuditEventCodes.TransactionCategorised,
                AuditEventCodes.AdjustmentUpdated,
                AuditEventCodes.NoteDisclosureUpdated
            ]),
        new(
            "who-approved-what",
            "Who approved what",
            true,
            "workflow-gates-plus-audit-log-integrity-chain",
            "Named reviewer/accountant identity, approval timestamps, filing status transitions, adjustment approvals, signatory evidence and the affected period.",
            "Approval and filing-state endpoints write audit events after readiness gates have passed; final filing paths remain blocked when required evidence is missing.",
            [
                AuditEventCodes.AdjustmentApproved,
                AuditEventCodes.CroFilingStatusChanged,
                AuditEventCodes.CroPaymentConfirmed,
                AuditEventCodes.DeadlineMarkedFiled,
                AuditEventCodes.CharityFilingStatusChanged
            ]),
        new(
            "what-was-generated",
            "What was generated",
            true,
            "server-side-generation-events",
            "Generated accounts documents, CRO signature pages, notes, charity reports, iXBRL internal checks, validation status and period linkage.",
            "Document generation and iXBRL checks are recorded as workflow/audit events before readiness profiles expose generated outputs as satisfied evidence.",
            [
                AuditEventCodes.CroDocumentGenerated,
                AuditEventCodes.IxbrlInternalCheckCompleted,
                AuditEventCodes.NotesGenerated,
                AuditEventCodes.CharityReportGenerated
            ]),
        new(
            "what-evidence-was-present",
            "What evidence was present",
            true,
            "readiness-profile-plus-audit-snapshots",
            "Required evidence checklist state, source references, legal-gate decisions, old/new value snapshots and generated output flags at the point of review.",
            "FilingReadinessProfile exposes blocking evidence and LegalSourceReference metadata; audit snapshots preserve the data changes that led to the generated pack.",
            [
                AuditEventCodes.YearEndReviewConfirmationUpdated,
                AuditEventCodes.OpeningBalanceUpserted,
                AuditEventCodes.ShareCapitalUpdated,
                AuditEventCodes.TaxBalanceUpserted,
                AuditEventCodes.CharityInfoUpdated
            ]),
        new(
            "tamper-evident-chain",
            "Tamper-evident audit chain",
            true,
            "audit-log-integrity-chain-and-signed-checkpoint",
            "Previous integrity hash, current integrity hash, checkpoint key id, signed checkpoint anchor, checked-entry count and checkpoint creator identity.",
            "AuditIntegrityService verifies hash chaining; AuditIntegrityCheckpointService signs a checkpoint over the latest company audit entry with deployment-managed signing keys.",
            [
                AuditEventCodes.CroFilingStatusChanged,
                AuditEventCodes.CroDocumentGenerated,
                AuditEventCodes.IxbrlInternalCheckCompleted
            ])
    ];

    private static IReadOnlyList<AuditEvidenceTimelineEntry> BuildAuditEvidenceTimeline() =>
    [
        new(
            "data-change-capture",
            "Working papers",
            "Who changed what and when?",
            "At every authenticated write before regenerated outputs can be reviewed.",
            "Authenticated firm user",
            "Audit log snapshots and integrity hash chain must cover the changed entity before a reviewer relies on the updated evidence.",
            [
                AuditEventCodes.SizeClassificationDataSaved,
                AuditEventCodes.FilingRegimeDetermined,
                AuditEventCodes.AdjustmentUpdated,
                AuditEventCodes.NoteDisclosureUpdated,
                AuditEventCodes.YearEndReviewConfirmationUpdated
            ],
            [
                "working-paper-review",
                "qualified-accountant-review"
            ]),
        new(
            "generated-output-capture",
            "Generated outputs",
            "What was generated and when?",
            "Immediately after server-side PDF, notes, charity or iXBRL generation completes.",
            "System generation service",
            "Generated output audit event must exist before accountant approval can rely on the pack.",
            [
                AuditEventCodes.CroDocumentGenerated,
                AuditEventCodes.IxbrlInternalCheckCompleted,
                AuditEventCodes.NotesGenerated,
                AuditEventCodes.CharityReportGenerated
            ],
            [
                "generated-output-review",
                "qualified-accountant-review"
            ]),
        new(
            "accountant-approval-capture",
            "Professional review",
            "Who approved the pack and what evidence was open at approval?",
            "At named qualified-accountant approval, after generated outputs and required evidence are present.",
            "Named qualified accountant",
            "Filing workflow transitions must record reviewer identity, approval timestamp, open blockers, warnings and allowed next actions.",
            [
                AuditEventCodes.AdjustmentApproved,
                AuditEventCodes.CroFilingStatusChanged,
                AuditEventCodes.CroPaymentConfirmed,
                AuditEventCodes.CharityFilingStatusChanged
            ],
            [
                "qualified-accountant-review",
                "director-secretary-certification"
            ]),
        new(
            "external-validation-capture",
            "External validation",
            "When was external ROS/iXBRL validation evidence present?",
            "After internal iXBRL checks pass and before Revenue filing status can be marked externally usable.",
            "Reviewer or qualified accountant",
            "External validation evidence remains a recorded workflow state only; the platform must not perform direct ROS submission.",
            [
                AuditEventCodes.IxbrlInternalCheckCompleted,
                AuditEventCodes.CroFilingStatusChanged
            ],
            [
                "external-ros-validation",
                "no-direct-cro-ros-submission"
            ])
    ];

    private static IReadOnlyList<ProductionAuditEvidencePackItem> BuildAuditEvidencePack() =>
    [
        new(
            "who-changed-what",
            "Who changed what",
            "Which authenticated user changed statutory, accounting or filing evidence, and what old/new values were captured?",
            "tamper-evident-audit-log-entry",
            "audit_logs",
            "Authenticated firm user",
            "At the same transaction boundary as each supported write.",
            "Audit entry must include entity, action, request correlation, redacted before/after snapshots, integrity hash and previous hash.",
            "Block release when a supported write path can alter filing evidence without an audit row linked into the integrity chain.",
            [
                AuditEventCodes.SizeClassificationDataSaved,
                AuditEventCodes.FilingRegimeDetermined,
                AuditEventCodes.AdjustmentUpdated,
                AuditEventCodes.NoteDisclosureUpdated,
                AuditEventCodes.YearEndReviewConfirmationUpdated
            ],
            [
                "working-paper-review",
                "qualified-accountant-review"
            ]),
        new(
            "who-approved-what",
            "Who approved what",
            "Which named reviewer or qualified accountant approved the pack, and which period/output state was approved?",
            "named-accountant-approval-record",
            "filing_workflow_status_history",
            "Named qualified accountant",
            "After required evidence is present and before any filing status can be marked approved or submitted.",
            "Approval transition must carry reviewer identity, timestamp, period id, open blocker summary and the allowed next action set.",
            "Block release when approval can be recorded without a named qualified-accountant identity and linked readiness evidence.",
            [
                AuditEventCodes.AdjustmentApproved,
                AuditEventCodes.CroFilingStatusChanged,
                AuditEventCodes.CroPaymentConfirmed,
                AuditEventCodes.CharityFilingStatusChanged
            ],
            [
                "qualified-accountant-review",
                "director-secretary-certification"
            ]),
        new(
            "evidence-present-at-approval",
            "Evidence present at approval",
            "What evidence was present, blocked, warned or manually handed off when the accountant approval decision was made?",
            "readiness-profile-decision-snapshot",
            "filing-readiness-profile-snapshot",
            "Named qualified accountant",
            "At professional review, immediately before final approval or manual handoff recording.",
            "Snapshot must include required evidence, blocking issues, warning issues, legal source references, generated output flags and allowed next actions.",
            "Block release when accountant approval can proceed without a retained readiness-profile snapshot for the approved period.",
            [
                AuditEventCodes.YearEndReviewConfirmationUpdated,
                AuditEventCodes.OpeningBalanceUpserted,
                AuditEventCodes.ShareCapitalUpdated,
                AuditEventCodes.TaxBalanceUpserted,
                AuditEventCodes.CharityInfoUpdated
            ],
            [
                "generated-output-review",
                "qualified-accountant-review",
                "manual-professional-handoff"
            ]),
        new(
            "generated-output-fingerprint",
            "Generated output fingerprint",
            "Which PDF, iXBRL, notes, CRO or charity output was generated, and how can the exact generated artifact be recognised later?",
            "generated-output-fingerprint",
            "generated_filing_output_manifest",
            "System generation service",
            "Immediately after server-side output generation and before the output is exposed for review.",
            "Manifest must retain output type, period id, generator version, source-law snapshot hash, generated timestamp and artifact fingerprint.",
            "Block release when generated filing artifacts are reviewable without a retained manifest or audit event.",
            [
                AuditEventCodes.CroDocumentGenerated,
                AuditEventCodes.IxbrlInternalCheckCompleted,
                AuditEventCodes.NotesGenerated,
                AuditEventCodes.CharityReportGenerated
            ],
            [
                "generated-output-review",
                "external-ros-validation"
            ]),
        new(
            "integrity-chain-checkpoint",
            "Integrity chain checkpoint",
            "Can the release reviewer prove audit entries have not been removed or rewritten since the evidence was captured?",
            "signed-audit-integrity-checkpoint",
            "audit_integrity_checkpoints",
            "Platform owner",
            "At release review and after the seeded accountant walkthrough evidence has been generated.",
            "Checkpoint must cover latest audit id, previous hash, current hash, checked-entry count, signing key id and signature verification result.",
            "Block release when audit hash verification or signed checkpoint creation cannot be demonstrated for the production candidate.",
            [
                AuditEventCodes.CroFilingStatusChanged,
                AuditEventCodes.CroDocumentGenerated,
                AuditEventCodes.IxbrlInternalCheckCompleted
            ],
            [
                "audit-integrity-checkpoint",
                "release-review-checklist"
            ])
    ];

    private static IReadOnlyList<ProductionMonitoringControl> BuildMonitoringControls() =>
    [
        new(
            "error-tracking",
            "Production error tracking",
            "Sentry-compatible",
            true,
            "Monitoring:ErrorTrackingDsn",
            "Unhandled exceptions are captured server-side with request path, HTTP method, environment and correlation id while default PII capture is disabled.",
            "Program.cs wires UseSentry from Monitoring:ErrorTrackingDsn; ProductionSafetyService blocks non-development startup when the DSN is missing or not HTTPS.",
            "Primary on-call accountant and platform owner",
            "Block release if error events cannot be routed to the on-call owner."),
        new(
            "structured-json-logs",
            "Structured JSON logs",
            "ASP.NET Core JSON console",
            true,
            "Monitoring:StructuredJsonConsole",
            "Production logs are emitted as structured JSON with scopes so log processors can index timestamps, categories, levels and correlation fields.",
            "Program.cs switches to AddJsonConsole when Monitoring:StructuredJsonConsole is true; production compose sets the flag explicitly.",
            "Platform operations log stream and release reviewer",
            "Block release if production logs cannot be parsed by timestamp, level, category and correlation id."),
        new(
            "correlation-id-error-responses",
            "Correlation id error responses",
            "ExceptionMiddleware",
            true,
            "Monitoring:IncludeCorrelationId",
            "Unexpected errors return a safe generic response with the ASP.NET trace identifier; server logs carry the same identifier for triage.",
            "ExceptionMiddleware logs ResourceNotFoundException, BusinessRuleException and unhandled exceptions with context.TraceIdentifier and writes correlationId to the JSON error response.",
            "Support triage queue and platform owner",
            "Block release if safe error responses omit correlation ids or server logs cannot be matched to the support ticket.")
    ];

    private static IReadOnlyList<DependencyPolicyControl> BuildDependencyPolicyControls() =>
    [
        new(
            "frontend-npm-audit",
            "Frontend dependency vulnerability audit",
            true,
            "CI frontend job runs npm audit --audit-level=moderate after npm ci.",
            "npm audit report for dependencies resolved from frontend/package-lock.json, with low-severity advisories tolerated only when they do not affect production build/runtime paths.",
            ".github/workflows/ci.yml Audit frontend dependencies step plus package-lock.json review in release evidence.",
            "Fail the release for moderate, high or critical npm advisories; record any accepted low-severity dev-tool advisory with owner and review date."),
        new(
            "frontend-lockfile-reproducibility",
            "Frontend lockfile reproducibility",
            true,
            "CI installs with npm ci using frontend/package-lock.json and the Node version pinned by .nvmrc/package engines.",
            "The package-lock.json resolved dependency graph is the release input for test, lint, build and production smoke images.",
            ".github/workflows/ci.yml Set up Node cache-dependency-path and Install frontend dependencies steps.",
            "Fail the release if package.json and package-lock.json drift or if npm ci cannot reproduce the dependency tree."),
        new(
            "ci-action-version-hygiene",
            "CI action version hygiene",
            true,
            "Workflow Hygiene job runs node scripts/verify-ci-actions.mjs before backend/frontend/production jobs.",
            "GitHub Actions used by CI are checked for explicit version hygiene before any production assurance job can pass.",
            ".github/workflows/ci.yml Workflow Hygiene job blocks downstream jobs through needs dependencies.",
            "Fail the release if workflow actions are unpinned, downgraded below policy, or bypass the hygiene verifier."),
        new(
            "backend-restore-build",
            "Backend NuGet restore and release build",
            true,
            "CI backend job runs dotnet restore, dotnet test --configuration Release and dotnet build --configuration Release.",
            "NuGet restore, Release test output and Release API build output prove the backend dependency graph resolves and compiles before production images are accepted.",
            ".github/workflows/ci.yml Backend job and production image build jobs.",
            "Fail the release if NuGet restore fails, Release tests fail, or the API cannot be built from the restored dependency graph.")
    ];

    private static IReadOnlyList<DeploymentSafetyControl> BuildDeploymentSafetyControls() =>
    [
        new(
            "controlled-production-migrations",
            "Controlled production migrations",
            true,
            "Production migrations run through dotnet Accounts.Api.dll --migrate-only before app startup; ProductionSafetyService blocks unsafe automatic startup migrations outside development.",
            "A controlled migration-only command path applies EF migrations and bootstrap-owner setup without starting the web host, while normal production startup remains guarded by DatabaseStartup safety flags.",
            "Program.cs handles --migrate-only; ProductionSafetyService rejects DatabaseStartup:AutoMigrateOnStartup unless AllowStartupMigrationInProduction is deliberately enabled.",
            "Fail production startup when AutoMigrateOnStartup is enabled without explicit production approval; run migrations as a separate release step instead."),
        new(
            "production-demo-seed-block",
            "Production demo seed blocking",
            true,
            "ProductionSafetyService rejects DatabaseStartup:SeedDemoData outside development before any database startup tasks execute.",
            "Known sample companies, seeded demo users and preview-only accounting records cannot be inserted into a non-development database unless the process is running in development.",
            "ProductionSafetyService validates SeedDemoData before Program.cs can call SeedData.SeedAsync.",
            "Fail production startup if demo seed data is enabled outside development."),
        new(
            "backup-restore-drill",
            "Backup restore drill",
            true,
            "CI production stack smoke runs scripts/backup-postgres.ps1 and scripts/verify-postgres-backup.ps1 against the production compose shape.",
            "The release evidence includes a PostgreSQL custom-format backup dump, sha256 sidecar and backup restore verification before the production smoke job can pass.",
            ".github/workflows/ci.yml Run production backup restore drill step invokes verify-postgres-backup after creating the dump.",
            "Fail the release if backup creation, checksum verification or restore verification fails.")
    ];

    private static IReadOnlyList<OperationsEvidencePackItem> BuildOperationsEvidencePack() =>
    [
        new(
            "sentry-error-routing",
            "Sentry production error routing",
            "Monitoring",
            "Platform owner",
            true,
            "Verify Monitoring:ErrorTrackingDsn is configured with an HTTPS DSN and send a controlled non-PII smoke error through the production error pipeline.",
            "sentry-production-error-routing-check",
            "production-monitoring",
            "Evidence must show the event reached the production error-tracking project with environment, request path and correlation id while default PII capture remains disabled.",
            "Block release if production exceptions cannot be routed to the on-call owner with a usable correlation id."),
        new(
            "structured-log-correlation",
            "Structured log correlation sample",
            "Monitoring",
            "Platform owner",
            true,
            "Run production stack smoke and retain a structured JSON log sample containing timestamp, level, category, request id and correlation id.",
            "structured-json-log-sample",
            "production-monitoring",
            "Evidence must include api-structured.log plus structured-log-report.json proving timestamp, level, category and the monitoring smoke correlation id are present in JSON logs.",
            "Block release if logs cannot be parsed or support tickets cannot be correlated to server evidence."),
        new(
            "dependency-audit",
            "Dependency and lockfile audit",
            "Dependency policy",
            "Engineering",
            true,
            "Run npm ci, npm audit --audit-level=moderate --json and scripts/write-dependency-evidence.ps1 against the release commit.",
            "dependency-audit-release",
            "dependency-policy-controls",
            "Evidence must include npm-audit.json and dependency-audit-report.json with package-lock hash, npm audit counts, NuGet audit policy, and GitHub Actions version-hygiene wiring.",
            "Block release for moderate/high/critical advisories, unreproducible lockfiles, failed restore/build, or unverified CI action versions."),
        new(
            "migration-safety",
            "Controlled migration safety",
            "Deployment safety",
            "Platform owner",
            true,
            "Run scripts/verify-production-compose-images.ps1 -EvidencePath production-safety-report.json against the release compose profile.",
            "production-safety-config",
            "deployment-safety-controls",
            "Evidence must show the migrate service runs exactly --migrate-only, the API depends on successful migration completion, and AutoMigrateOnStartup remains false for normal web startup.",
            "Block release if production startup can auto-migrate without explicit release approval."),
        new(
            "production-seed-block",
            "Production seed blocking",
            "Deployment safety",
            "Platform owner",
            true,
            "Run scripts/verify-production-compose-images.ps1 -EvidencePath production-safety-report.json and retain the CI production-safety-config artifact.",
            "production-safety-config",
            "deployment-safety-controls",
            "Evidence must show SeedDemoData is false for migrate and API services, demo-seed override flags are absent, and the bootstrap owner initial password is available only to the migration job.",
            "Block release if demo seed data can run outside development."),
        new(
            "backup-restore-drill",
            "Backup restore drill",
            "Deployment safety",
            "Platform owner",
            true,
            "Run scripts/backup-postgres.ps1 and scripts/verify-postgres-backup.ps1 against the production compose shape before approving the release.",
            "postgres-backup-restore-drill-report",
            "deployment-safety-controls",
            "Evidence must include the PostgreSQL custom-format dump, sha256 sidecar and successful restore verification report.",
            "Block release if backup creation, checksum verification or restore verification fails.")
    ];

    private static IReadOnlyList<AccountantAcceptanceCriterion> BuildAccountantAcceptanceCriteria(IReadOnlyList<GoldenFilingCorpusScenario> goldenCorpus) =>
    [
        new(
            "micro-ltd",
            "Micro LTD accountant acceptance",
            true,
            "qualified-accountant-review-required",
            [
                "PDF wording and micro statutory statement",
                "iXBRL XML and Revenue taxonomy selection",
                "filing readiness profile at 100%",
                "tax computation and notes disclosure set",
                "director and secretary signatory gates"
            ],
            [
                "Named qualified-accountant approval recorded against the generated pack.",
                "External ROS/iXBRL validation evidence recorded before Revenue use.",
                "Director and secretary certification evidence reviewed."
            ],
            "Named qualified accountant must approve the generated pack before real filing use.",
            [
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.FrcFrs105,
                IrishStatutoryRuleSources.RevenueAcceptedTaxonomies
            ],
            AcceptanceVerifiers(goldenCorpus, "micro-ltd")),
        new(
            "small-abridged-ltd",
            "Small abridged LTD accountant acceptance",
            true,
            "qualified-accountant-review-required",
            [
                "Full accounts PDF wording and abridged CRO pack wording",
                "Section 352 abridgement evidence",
                "iXBRL XML and public profit-and-loss omission checks",
                "tax computation and small-company notes",
                "director and secretary signatory gates"
            ],
            [
                "Named qualified-accountant approval recorded against full and abridged generated packs.",
                "Abridgement eligibility and audit exemption evidence reviewed.",
                "External ROS/iXBRL validation evidence recorded before Revenue use."
            ],
            "Named qualified accountant must approve both the full accounts pack and abridged CRO pack before real filing use.",
            [
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.FrcFrs102,
                IrishStatutoryRuleSources.RevenueIxbrlContents
            ],
            AcceptanceVerifiers(goldenCorpus, "small-abridged-ltd")),
        new(
            "dac-small",
            "Small DAC accountant acceptance",
            true,
            "qualified-accountant-review-required",
            [
                "DAC accounts PDF wording and directors' report evidence",
                "iXBRL XML and Revenue taxonomy selection",
                "filing readiness profile with DAC supported-path evidence",
                "tax computation and small-company notes",
                "director and secretary signatory gates"
            ],
            [
                "Named qualified-accountant approval recorded against the DAC pack.",
                "DAC company type, audit exemption and small-company filing regime evidence reviewed.",
                "External ROS/iXBRL validation evidence recorded before Revenue use."
            ],
            "Named qualified accountant must approve the DAC generated pack before real filing use.",
            [
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.FrcFrs102,
                IrishStatutoryRuleSources.RevenueAcceptedTaxonomies
            ],
            AcceptanceVerifiers(goldenCorpus, "dac-small")),
        new(
            "clg-charity",
            "CLG charity accountant acceptance",
            true,
            "qualified-accountant-review-required",
            [
                "CLG accounts PDF wording",
                "charity number, SoFA and trustees annual report evidence",
                "iXBRL XML and CLG source-backed readiness",
                "tax computation and charity notes",
                "charity annual return review gates"
            ],
            [
                "Named qualified-accountant approval recorded against the CLG charity pack.",
                "Charity annual report evidence reviewed before charity filing state advances.",
                "Charities Regulator source-backed evidence reviewed."
            ],
            "Named qualified accountant must approve the CLG charity pack and charity evidence before real filing use.",
            [
                IrishStatutoryRuleSources.CroGuaranteeCompany,
                IrishStatutoryRuleSources.CharitiesRegulatorAnnualReport,
                IrishStatutoryRuleSources.FrcFrs102
            ],
            AcceptanceVerifiers(goldenCorpus, "clg-charity")),
        new(
            "medium-audit-required",
            "Medium handoff accountant acceptance",
            true,
            "manual-handoff-review-required",
            [
                "auditor handoff and signed auditor report evidence",
                "full accounts PDF with P&L, cash flow and equity statements",
                "iXBRL XML tagged profit-and-loss facts",
                "filing readiness blockers before and after auditor evidence",
                "manual professional handoff note"
            ],
            [
                "Signed auditor report and manual handoff note reviewed by the qualified accountant.",
                "Named qualified-accountant acceptance recorded only after auditor evidence is present.",
                "Unsupported automated filing path remains blocked until manual professional ownership is recorded."
            ],
            "Qualified accountant must record manual handoff acceptance before relying on outputs.",
            [
                IrishStatutoryRuleSources.CroMediumCompany,
                IrishStatutoryRuleSources.CroAuditorsReport,
                IrishStatutoryRuleSources.FrcFrs102
            ],
            AcceptanceVerifiers(goldenCorpus, "medium-audit-required"))
    ];

    private static AccountantAcceptanceSummary BuildAccountantAcceptanceSummary(
        IReadOnlyList<GoldenFilingCorpusScenario> goldenCorpus,
        IReadOnlyList<AccountantAcceptanceCriterion> accountantAcceptanceCriteria)
    {
        var releaseBlockingScenarioCodes = accountantAcceptanceCriteria
            .Where(criterion => criterion.Required && criterion.AcceptanceStatus != "accepted")
            .Select(criterion => criterion.ScenarioCode)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var requiredSignOffGates = accountantAcceptanceCriteria
            .Where(criterion => criterion.Required)
            .Select(criterion => criterion.RequiredSignOffGate)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var automatedVerifierCount = accountantAcceptanceCriteria
            .SelectMany(criterion => criterion.EvidenceVerifiers)
            .Select(verifier => verifier.Name)
            .Distinct(StringComparer.Ordinal)
            .Count();
        var manualHandoffScenarioCount = goldenCorpus.Count(scenario =>
            scenario.ExpectedOutcome.Contains("manual-handoff", StringComparison.OrdinalIgnoreCase)
            || scenario.Fixture.ManualProfessionalReviewRequired);

        return new AccountantAcceptanceSummary(
            goldenCorpus.Count,
            automatedVerifierCount,
            accountantAcceptanceCriteria.Count(criterion => criterion.Required),
            manualHandoffScenarioCount,
            releaseBlockingScenarioCodes,
            requiredSignOffGates,
            releaseBlockingScenarioCodes.Length == 0 ? "accepted" : "qualified-accountant-review-required");
    }

    private static AccountantWorkflowWalkthroughProtocol BuildAccountantWorkflowWalkthroughProtocol(
        IReadOnlyList<GoldenFilingCorpusScenario> goldenCorpus)
    {
        return new AccountantWorkflowWalkthroughProtocol(
            "accountant-workflow-walkthrough-v1",
            "Qualified accountant",
            "required-review",
            "golden-corpus-accountant-acceptance",
            "Block release if a named qualified accountant has not walked the seeded golden corpus through the live accountant workflow and accepted the outputs, gates, wording and evidence.",
            goldenCorpus.Select(scenario => scenario.Code).Order(StringComparer.Ordinal).ToArray(),
            [
                "Dashboard: identify the client, deadline pressure, blockers, reviewer owner and next action.",
                "Company detail: confirm statutory profile, company type, officers, charity flags and period setup.",
                "Period workspace: review import, classification, year-end evidence, statements, notes and workflow rail state.",
                "Financial statements: inspect statement preview, tax computation, source trail and directors' report evidence.",
                "Filing review: inspect readiness profile, legal source links, generated outputs, signatory gates and accountant sign-off packet.",
                "Production readiness: confirm golden corpus, statutory rules coverage, visual QA, release blockers and operational controls."
            ],
            [
                "Micro LTD walkthrough confirms PDF wording, iXBRL XML, tax computation, notes, signatory gates and 100% filing readiness.",
                "Small abridged LTD walkthrough confirms full accounts, abridged CRO pack, Section 352 evidence, iXBRL and audit-exemption gates.",
                "CLG charity walkthrough confirms charity number, SoFA, trustees annual report, charity notes and Charities Regulator evidence.",
                "Medium/audit-required walkthrough confirms auditor handoff blocks normal approval until signed auditor report evidence and manual acceptance are recorded.",
                "A named qualified accountant states that the generated outputs, gates, wording and evidence are professionally acceptable for the supported scope."
            ],
            [
                "seeded golden corpus walkthrough note",
                "named qualified-accountant approval",
                "visual QA screenshot review",
                "generated PDF and iXBRL evidence",
                "manual handoff acceptance"
            ]);
    }

    private static IReadOnlyList<AccountantJourneyAcceptanceChecklistItem> BuildAccountantJourneyAcceptanceChecklist(
        IReadOnlyList<GoldenFilingCorpusScenario> goldenCorpus,
        VisualQaCoverage visualQaCoverage)
    {
        var scenarioCodes = goldenCorpus.Select(scenario => scenario.Code).Order(StringComparer.Ordinal).ToArray();
        var routeCodes = new HashSet<string>(
            ["dashboard", "company-detail", "period-workspace", "financial-statements", "filing-review", "production-readiness"],
            StringComparer.Ordinal);
        var visualArtifactNamesByRoute = visualQaCoverage.Artifacts
            .GroupBy(artifact => artifact.RouteCode, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(artifact => artifact.FileName).Order(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

        return visualQaCoverage.Routes
            .Where(route => routeCodes.Contains(route.Code))
            .OrderBy(route => JourneyRouteOrder(route.Code))
            .Select(route => new AccountantJourneyAcceptanceChecklistItem(
                route.Code,
                route.Label,
                route.RouteKey,
                route.WorkflowStages,
                scenarioCodes,
                visualArtifactNamesByRoute.TryGetValue(route.Code, out var artifactNames) ? artifactNames : [],
                [
                    "named qualified-accountant route acceptance",
                    "visual smoke screenshots reviewed",
                    "golden corpus evidence accepted"
                ],
                BuildJourneyAcceptanceCriteria(route),
                "golden-corpus-accountant-acceptance",
                "required-review"))
            .ToArray();
    }

    private static IReadOnlyList<string> BuildJourneyAcceptanceCriteria(VisualQaRoute route)
    {
        var criteria = new List<string>
        {
            $"{route.Label} route exposes the relevant accountant workflow state, blockers, next actions and evidence.",
            $"A named qualified accountant accepts the {route.Label} route outputs, gates, wording and evidence for every seeded golden scenario."
        };

        if (route.Code == "filing-review")
        {
            criteria[0] = "Filing review route exposes readiness, source links, generated outputs, signatory gates, accountant sign-off packet, external ROS/iXBRL validation and filing state.";
        }
        else if (route.Code == "financial-statements")
        {
            criteria[0] = "Financial statements route exposes statement preview, tax computation, source trail and directors' report evidence before filing review.";
        }
        else if (route.Code == "production-readiness")
        {
            criteria[0] = "Production readiness route exposes backend checks, filing rules coverage, unsupported paths, security posture, release blockers and accountant review state.";
        }

        return criteria;
    }

    private static IReadOnlyList<AccountantWorkflowEvidencePackItem> BuildAccountantWorkflowEvidencePack(
        IReadOnlyList<AccountantJourneyAcceptanceChecklistItem> checklist) =>
        checklist
            .Select(item => new AccountantWorkflowEvidencePackItem(
                item.RouteCode,
                item.RouteLabel,
                item.WorkflowStages,
                item.SeededScenarioCodes,
                item.VisualArtifactNames,
                $"{item.RouteCode}-accountant-route-acceptance-note",
                BuildAccountantWorkflowDecisionQuestion(item),
                item.RequiredEvidence,
                item.SignOffGate,
                "Block release until a named qualified accountant accepts this route's outputs, gates, wording and evidence against the seeded golden corpus and reviewed visual artifacts."))
            .ToArray();

    private static string BuildAccountantWorkflowDecisionQuestion(AccountantJourneyAcceptanceChecklistItem item)
    {
        if (item.RouteCode == "filing-review")
        {
            return "Does the filing review route let a qualified accountant accept readiness, source links, generated outputs, signatory gates, external ROS/iXBRL validation, filing state, outputs, gates, wording and evidence?";
        }

        if (item.RouteCode == "production-readiness")
        {
            return "Does the production readiness route let a qualified accountant accept backend checks, filing rules coverage, unsupported paths, security posture, release blockers, accountant review state, outputs, gates, wording and evidence?";
        }

        return $"Does the {item.RouteLabel} route let a qualified accountant accept the workflow state, blockers, next action, outputs, gates, wording and evidence for every seeded golden scenario?";
    }

    private static IReadOnlyList<AccountantWalkthroughEvidenceMatrixItem> BuildAccountantWalkthroughEvidenceMatrix(
        IReadOnlyList<GoldenFilingCorpusScenario> goldenCorpus,
        IReadOnlyList<AccountantAcceptanceCriterion> accountantAcceptanceCriteria,
        IReadOnlyList<AccountantJourneyAcceptanceChecklistItem> checklist,
        IReadOnlyList<AccountantWorkflowEvidencePackItem> evidencePack)
    {
        var acceptanceByScenario = accountantAcceptanceCriteria.ToDictionary(
            criterion => criterion.ScenarioCode,
            StringComparer.Ordinal);
        var evidenceByRoute = evidencePack.ToDictionary(
            item => item.RouteCode,
            StringComparer.Ordinal);
        var rows = new List<AccountantWalkthroughEvidenceMatrixItem>();

        foreach (var scenario in goldenCorpus.OrderBy(scenario => scenario.Code, StringComparer.Ordinal))
        {
            var acceptance = acceptanceByScenario[scenario.Code];

            foreach (var route in checklist)
            {
                var routeEvidence = evidenceByRoute[route.RouteCode];
                rows.Add(new AccountantWalkthroughEvidenceMatrixItem(
                    scenario.Code,
                    scenario.Label,
                    scenario.ExpectedOutcome,
                    scenario.EvidencePack.ExpectedOutputs.FilingReadinessState,
                    scenario.EvidencePack.ExpectedOutputs.SignOffPacketState,
                    scenario.Fixture.ManualProfessionalReviewRequired,
                    route.RouteCode,
                    route.RouteLabel,
                    route.RouteKey,
                    route.WorkflowStages,
                    route.VisualArtifactNames,
                    $"{scenario.Code}-{route.RouteCode}-walkthrough-note",
                    routeEvidence.DecisionQuestion,
                    route.RequiredEvidence
                        .Concat(acceptance.RequiredEvidence)
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .ToArray(),
                    route.AcceptanceCriteria
                        .Concat(acceptance.ReviewScope.Select(scope =>
                            $"{scenario.Label}: qualified-accountant review covers {scope}."))
                        .ToArray(),
                    "golden-corpus-accountant-acceptance",
                    route.SignOffGate,
                    "required-review",
                    BlocksRelease: true));
            }
        }

        return rows.ToArray();
    }

    private static IReadOnlyList<WorkbenchVisualAcceptanceRegisterItem> BuildWorkbenchVisualAcceptanceRegister(
        VisualQaCoverage visualQaCoverage)
    {
        var artifactNamesByRoute = visualQaCoverage.Artifacts
            .GroupBy(artifact => artifact.RouteCode, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .Select(artifact => artifact.FileName)
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        return visualQaCoverage.RouteAudits
            .Select(audit => new WorkbenchVisualAcceptanceRegisterItem(
                audit.RouteCode,
                audit.Label,
                audit.WorkflowStages,
                audit.ReviewChecks,
                artifactNamesByRoute.TryGetValue(audit.RouteCode, out var artifactNames) ? artifactNames : [],
                $"{audit.RouteCode}-visual-acceptance-note",
                [
                    "route-state acceptance note",
                    "light/dark desktop/mobile screenshot review",
                    "named visual QA reviewer sign-off"
                ],
                visualQaCoverage.ReviewProtocol.SignOffGate,
                audit.ReviewStatus,
                "Block release until this accountant workbench route is visually accepted across workflow hierarchy, table scanability, theme contrast, mobile density and route states.",
                BuildWorkbenchVisualAcceptanceNextAction(audit)))
            .ToArray();
    }

    private static string BuildWorkbenchVisualAcceptanceNextAction(VisualQaRouteAudit audit)
    {
        if (audit.RouteCode == "filing-review")
        {
            return "Accept the filing review screen only after its evidence checklist, source links, generated outputs and filing-state actions are visually clear in light/dark desktop/mobile screenshots.";
        }

        if (audit.RouteCode == "production-readiness")
        {
            return "Accept the production readiness screen only after release blockers, rule coverage, visual QA, operational readiness and accountant review state are visually clear in light/dark desktop/mobile screenshots.";
        }

        return $"Accept the {audit.Label} route only after its workflow hierarchy, tables, contrast, mobile layout, loading/error/empty states and screenshots are professionally reviewed.";
    }

    private static int JourneyRouteOrder(string routeCode) => routeCode switch
    {
        "dashboard" => 0,
        "company-detail" => 1,
        "period-workspace" => 2,
        "financial-statements" => 3,
        "filing-review" => 4,
        "production-readiness" => 5,
        _ => 99
    };

    private static IReadOnlyList<GoldenFilingCorpusVerifier> AcceptanceVerifiers(
        IReadOnlyList<GoldenFilingCorpusScenario> goldenCorpus,
        string scenarioCode)
    {
        var scenario = goldenCorpus.Single(item => item.Code == scenarioCode);
        return scenario.EvidenceVerifiers;
    }

    private static VisualQaCoverage BuildVisualQaCoverage()
    {
        var accountantWorkflowStages = new[]
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
        var themes = new[] { "light", "dark" };
        var layoutChecks = new[]
        {
            "browser-console-errors",
            "page-horizontal-overflow",
            "visible-text-overlap"
        };
        var reviewChecks = new[]
        {
            "accountant-workflow-hierarchy",
            "table-scanability",
            "theme-contrast",
            "mobile-density",
            "loading-error-empty-states"
        };
        var reviewProtocol = new VisualQaReviewProtocol(
            "visual-review-v1",
            "Design reviewer",
            "required-review",
            "visual-qa-screenshot-review",
            "Block release if any accountant workbench route has console errors, horizontal overflow, visible text overlap, inaccessible contrast, unreadable table density, or unresolved light/dark/mobile defects.",
            [
                "Every configured route is captured in light desktop, dark desktop, light mobile and dark mobile.",
                "No browser console errors, horizontal overflow or visible text overlap are present.",
                "Accountant workflow hierarchy, table scanability, theme contrast, mobile density and route states are professionally acceptable.",
                "A named visual QA reviewer records screenshot-manifest acceptance before real filing release."
            ],
            [
                "visual-smoke-manifest.json",
                "visual-smoke-evidence-report.json",
                "accountant-workbench-evidence-report.json",
                "28 visual smoke screenshots",
                "screenshot SHA-256 checksums",
                "screenshot PNG dimensions",
                "screenshot nonblank pixel diversity evidence",
                "per-screenshot automated theme contrast smoke evidence",
                "route audit summary",
                "named visual QA reviewer sign-off"
            ]);
        var viewports = new[]
        {
            new VisualQaViewport("desktop", 1440, 1000),
            new VisualQaViewport("mobile", 390, 844)
        };
        var routes = new[]
        {
            new VisualQaRoute(
                "dashboard",
                "dashboard",
                "Dashboard",
                "Accountant queue, blockers, deadlines and production readiness overview.",
                "Production Readiness",
                accountantWorkflowStages,
                OpenFilingTab: false),
            new VisualQaRoute(
                "production-readiness",
                "readiness",
                "Production readiness",
                "Assurance checklist, statutory rules matrix, source snapshot and operational gates.",
                "Production Readiness Checklist",
                ["Review", "Filing"],
                OpenFilingTab: false),
            new VisualQaRoute(
                "company-detail",
                "company",
                "Company detail",
                "Company command centre, statutory profile, officers, charity facts and accounting periods.",
                "Company command centre",
                ["Setup"],
                OpenFilingTab: false),
            new VisualQaRoute(
                "period-workspace",
                "period",
                "Period workspace",
                "Import, classification, year-end, statements and filing readiness overview.",
                "Filing readiness",
                accountantWorkflowStages,
                OpenFilingTab: false),
            new VisualQaRoute(
                "filing-review",
                "filing",
                "Filing review",
                "Period filing tab with evidence checklist, source links, outputs and filing state.",
                "Filing readiness profile",
                ["Review", "Filing"],
                OpenFilingTab: true),
            new VisualQaRoute(
                "financial-statements",
                "financialStatements",
                "Financial statements",
                "Statement preview, tax computation, source trail and directors' report workbench.",
                "Financial Statements",
                ["Statements"],
                OpenFilingTab: false),
            new VisualQaRoute(
                "workbench-preview",
                "workbenchPreview",
                "Workbench preview",
                "Internal component preview for accountant workflow primitives and route states.",
                "Workbench Component Preview",
                accountantWorkflowStages,
                OpenFilingTab: false)
        };

        var artifacts = routes
            .SelectMany(route => themes.SelectMany(theme => viewports.Select(viewport =>
            {
                var fileName = $"{route.Code}-{theme}-{viewport.Name}.png";
                return new VisualQaArtifact(
                    route.Code,
                    route.RouteKey,
                    theme,
                    viewport.Name,
                    fileName,
                    $"artifacts/visual-smoke/{fileName}",
                    route.RequiredText,
                    route.OpenFilingTab,
                    "required-review",
                    layoutChecks);
            })))
            .ToArray();
        var routeAudits = routes
            .Select(route => new VisualQaRouteAudit(
                route.Code,
                route.RouteKey,
                route.Label,
                route.WorkflowStages,
                themes.Length * viewports.Length,
                "required-review",
                reviewChecks))
            .ToArray();

        return new VisualQaCoverage(
            "visual-smoke-screenshots",
            "ci-production-smoke",
            "visual-smoke-manifest.json",
            routes.Length * themes.Length * viewports.Length,
            layoutChecks,
            reviewChecks,
            reviewProtocol,
            themes,
            viewports,
            routes,
            routeAudits,
            artifacts);
    }
}
