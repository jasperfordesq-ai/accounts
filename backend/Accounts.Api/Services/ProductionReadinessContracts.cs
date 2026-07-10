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

public sealed record HumanReleaseEvidenceGate(
    string Code,
    string Label,
    string TemplateFile,
    string RequiredReviewerRole,
    string Status,
    string SignOffGate,
    string ReleaseChecklistCode,
    string ReleaseManifestCode,
    string EvidenceArtifact,
    bool BlocksRelease,
    IReadOnlyList<string> ReviewerPickupFiles,
    IReadOnlyList<string> RequiredEvidence,
    string NextAction);

public sealed record HumanReleaseEvidenceCloseoutStep(
    string Code,
    string Label,
    int Sequence,
    string Detail,
    string Artifact,
    bool BlocksRelease);

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

public sealed record VisualQaTabState(
    string Kind,
    string Id,
    string Label);

public sealed record VisualQaStateInventoryItem(
    string StateId,
    string RouteName,
    string RouteKey,
    string Label,
    string Description,
    string? MaterialRoute,
    string UiState,
    string CanonicalPathTemplate,
    string CanonicalUrlTemplate,
    IReadOnlyDictionary<string, string> CanonicalQuery,
    VisualQaTabState CanonicalTabState,
    string ExpectedText,
    string ExpectedStateText,
    IReadOnlyList<string> WorkflowStages,
    string AuthMode,
    string ReviewStatus,
    bool OpenFilingTab);

public sealed record VisualQaArtifact(
    string StateId,
    string RouteName,
    string RouteCode,
    string RouteKey,
    string? MaterialRoute,
    string UiState,
    string AuthMode,
    string Theme,
    string ViewportName,
    string FileName,
    string ArtifactPath,
    string RequiredText,
    string ExpectedStateText,
    string CanonicalUrlTemplate,
    IReadOnlyDictionary<string, string> CanonicalQuery,
    VisualQaTabState CanonicalTabState,
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
    string InventoryVersion,
    int InventoryStateCount,
    int RouteCount,
    int AccountantWorkbenchRouteCount,
    int ExpectedScreenshotCount,
    IReadOnlyList<string> RequiredMaterialRoutes,
    IReadOnlyList<string> RequiredUiStates,
    bool SemanticDistinctnessRequired,
    IReadOnlyList<string> LayoutChecks,
    IReadOnlyList<string> ReviewChecks,
    VisualQaReviewProtocol ReviewProtocol,
    IReadOnlyList<string> Themes,
    IReadOnlyList<VisualQaViewport> Viewports,
    IReadOnlyList<VisualQaRoute> Routes,
    IReadOnlyList<VisualQaStateInventoryItem> StateInventory,
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
    IReadOnlyList<string> ReleaseBlockerCodes,
    IReadOnlyList<ProductionScorecardControl> Controls);

public sealed record ProductionScorecardControl(
    string Code,
    string Label,
    int Weight,
    string AssuranceClass,
    string Status,
    bool Passed,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> BlockingAuditItemIds);

public sealed record ProductionScorecard(
    int CurrentScore,
    int TargetScore,
    string Status,
    string NextGate,
    string ScoreBasis,
    DateOnly AuditBaselineDate,
    string AuditedCommit,
    string EvidencePolicy,
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
    IReadOnlyList<HumanReleaseEvidenceGate> HumanReleaseEvidence,
    IReadOnlyList<HumanReleaseEvidenceCloseoutStep> HumanReleaseEvidenceCloseout,
    VisualQaCoverage VisualQaCoverage);
