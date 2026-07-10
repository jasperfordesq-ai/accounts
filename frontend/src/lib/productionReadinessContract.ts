import { z } from "zod";

export interface LegalSourceReference {
  sourceId: string;
  title: string;
  effectiveDate: string;
  url: string;
}

export interface SourceLawSnapshot {
  snapshotDate: string;
  snapshotVersion: string;
  contentHash: string;
  sourceCount: number;
  sources: LegalSourceReference[];
}

export interface SourceLawTraceabilityEntry {
  sourceId: string;
  title: string;
  effectiveDate: string;
  url: string;
  inSnapshot: boolean;
  usedBy: string[];
  releaseGateCodes: string[];
}

export interface SourceLawMaintenanceProtocol {
  protocolVersion: string;
  ownerRole: string;
  status: string;
  reviewCadence: string;
  nextReviewDue: string;
  signOffGate: string;
  changeDetection: string;
  failurePolicy: string;
  monitoredSourceIds: string[];
  acceptanceCriteria: string[];
  requiredEvidence: string[];
}

export interface SourceLawReviewLedgerEntry {
  sourceId: string;
  title: string;
  url: string;
  pinnedEffectiveDate: string;
  ownerRole: string;
  releaseChecklistCode: string;
  blocksRelease: boolean;
  reviewChecks: string[];
  requiredEvidence: string[];
}

export interface ProductionReadinessArea {
  code: string;
  label: string;
  status: string;
  detail: string;
}

export interface GoldenFilingCorpusEvidencePack {
  outputArtifacts: string[];
  decisionGates: string[];
  expectedValueChecks: string[];
  expectedOutputs: GoldenFilingCorpusExpectedOutputs;
  expectedProofPoints: GoldenFilingCorpusProofPoint[];
  sourceReferences: LegalSourceReference[];
}

export interface GoldenFilingCorpusExpectedOutputs {
  pdfTextMarkers: string[];
  ixbrlRequiredTags: string[];
  filingReadinessState: string;
  expectedCorporationTax: number;
  requiredNotes: string[];
  filingGateStates: string[];
  signOffPacketState: string;
}

export interface GoldenFilingCorpusProofPoint {
  area: string;
  expectedEvidence: string;
  automatedVerifier: string;
  required: boolean;
}

export interface GoldenFilingCorpusFixture {
  legalName: string;
  companyType: string;
  periodStart: string;
  periodEnd: string;
  expectedSizeClass: string;
  expectedRegime: string;
  auditExempt: boolean;
  manualProfessionalReviewRequired: boolean;
}

export interface GoldenFilingCorpusVerifier {
  name: string;
  command: string;
  ciScope: string;
  runsInDefaultCi: boolean;
  environment: string;
  evidenceLevel: string;
}

export interface GoldenFilingCorpusLegalBasisSnapshot {
  scenarioCode: string;
  companyType: string;
  sizeClass: string;
  electedRegime: string;
  auditExempt: boolean;
  manualProfessionalReviewRequired: boolean;
  legalBasis: string;
  requiredOutputs: string[];
  professionalGates: string[];
  sourceIds: string[];
}

export interface GoldenFilingCorpusScenario {
  code: string;
  label: string;
  companyScope: string;
  expectedOutcome: string;
  coverageStatus: string;
  fixture: GoldenFilingCorpusFixture;
  evidenceTestNames: string[];
  evidenceVerifiers: GoldenFilingCorpusVerifier[];
  assertions: string[];
  evidencePack: GoldenFilingCorpusEvidencePack;
  legalBasisSnapshot: GoldenFilingCorpusLegalBasisSnapshot;
}

export interface GoldenEvidenceLedgerEntry {
  scenarioCode: string;
  label: string;
  fixtureLegalName: string;
  companyType: string;
  expectedOutcome: string;
  coverageStatus: string;
  acceptanceStatus: string;
  requiredSignOffGate: string;
  blocksRelease: boolean;
  automatedVerifierNames: string[];
  automatedVerifierCommands: string[];
  ciScopes: string[];
  evidenceLevels: string[];
  outputArtifacts: string[];
  decisionGates: string[];
  expectedValueChecks: string[];
  proofPointAreas: string[];
  sourceIds: string[];
  expectedCorporationTax: number;
  filingReadinessState: string;
  signOffPacketState: string;
}

export interface GoldenVerifierManifestEntry {
  scenarioCode: string;
  scenarioLabel: string;
  expectedOutcome: string;
  coverageStatus: string;
  verifierName: string;
  command: string;
  ciScope: string;
  runsInDefaultCi: boolean;
  evidenceLevel: string;
  blocksRelease: boolean;
  outputArtifacts: string[];
  decisionGates: string[];
  proofPointAreas: string[];
}

export interface StatutoryRuleMatrixEntry {
  code: string;
  companyScope: string;
  sizeOrRegime: string;
  supportLevel: string;
  requiredEvidence: string[];
  requiredOutputs: string[];
  manualHandoffGates: string[];
  sources: LegalSourceReference[];
}

export interface StatutoryRulesCoverageItem {
  code: string;
  ruleFamily: string;
  decisionUnderTest: string;
  coverageStatus: string;
  automatedVerifierNames: string[];
  edgeCases: string[];
  sources: LegalSourceReference[];
}

export interface OperationalGate {
  code: string;
  label: string;
  required: boolean;
  status: string;
  detail: string;
}

export interface ProductionReadinessAssuranceAction {
  code: string;
  label: string;
  owner: string;
  priority: string;
  riskRank: number;
  evidenceStage: string;
  status: string;
  detail: string;
  evidenceRequired: string;
}

export interface ProductionReadinessCompletionTrack {
  code: string;
  label: string;
  ownerRole: string;
  status: string;
  completionCriteria: string[];
  currentEvidence: string[];
  nextActions: string[];
  assuranceActionCodes: string[];
}

export interface ProductionReleaseBlocker {
  code: string;
  trackCode: string;
  trackLabel: string;
  ownerRole: string;
  severity: string;
  riskRank: number;
  blockingIssue: string;
  requiredEvidence: string;
  nextAction: string;
  sourceActionCode: string;
  releaseChecklistCode: string;
  operationalGateCode: string;
  evidenceArtifact: string;
  blocksRelease: boolean;
}

export interface ProductionAuditabilityControl {
  code: string;
  label: string;
  required: boolean;
  enforcement: string;
  evidenceCaptured: string;
  verification: string;
  auditEventCodes: string[];
}

export interface AuditEvidenceTimelineEntry {
  code: string;
  stage: string;
  evidenceQuestion: string;
  capturedWhen: string;
  requiredActor: string;
  verification: string;
  auditEventCodes: string[];
  blockingGateCodes: string[];
}

export interface ProductionAuditEvidencePackItem {
  code: string;
  label: string;
  evidenceQuestion: string;
  requiredArtifact: string;
  retainedIn: string;
  requiredActor: string;
  capturedWhen: string;
  verification: string;
  failurePolicy: string;
  auditEventCodes: string[];
  blockingGateCodes: string[];
}

export interface ProductionMonitoringControl {
  code: string;
  label: string;
  provider: string;
  required: boolean;
  productionSafetyGate: string;
  evidenceCaptured: string;
  verification: string;
  alertRoute: string;
  failurePolicy: string;
}

export interface DependencyPolicyControl {
  code: string;
  label: string;
  required: boolean;
  enforcement: string;
  evidenceCaptured: string;
  verification: string;
  failurePolicy: string;
}

export interface DeploymentSafetyControl {
  code: string;
  label: string;
  required: boolean;
  enforcement: string;
  evidenceCaptured: string;
  verification: string;
  failurePolicy: string;
}

export interface OperationsEvidencePackItem {
  code: string;
  label: string;
  category: string;
  ownerRole: string;
  required: boolean;
  command: string;
  requiredArtifact: string;
  releaseGateCode: string;
  verification: string;
  failurePolicy: string;
}

export interface ReleaseReviewChecklistItem {
  code: string;
  label: string;
  ownerRole: string;
  required: boolean;
  status: string;
  blocksRelease: boolean;
  evidenceArtifact: string;
  assuranceActionCode: string;
  operationalGateCode: string;
  auditEventCodes: string[];
  detail: string;
}

export interface ReleaseVerificationManifestItem {
  code: string;
  label: string;
  ownerRole: string;
  command: string;
  ciScope: string;
  runsInDefaultCi: boolean;
  blocksRelease: boolean;
  evidenceArtifact: string;
  releaseChecklistEvidenceArtifact: string;
  manualFallback: string;
}

export interface HumanReleaseEvidenceGate {
  code: string;
  label: string;
  templateFile: string;
  requiredReviewerRole: string;
  status: string;
  signOffGate: string;
  releaseChecklistCode: string;
  releaseManifestCode: string;
  evidenceArtifact: string;
  blocksRelease: boolean;
  reviewerPickupFiles: string[];
  requiredEvidence: string[];
  nextAction: string;
}

export interface HumanReleaseEvidenceCloseoutStep {
  code: string;
  label: string;
  sequence: number;
  detail: string;
  artifact: string;
  blocksRelease: boolean;
}

export interface AccountantAcceptanceCriterion {
  scenarioCode: string;
  label: string;
  required: boolean;
  acceptanceStatus: string;
  reviewScope: string[];
  requiredEvidence: string[];
  requiredSignOffGate: string;
  evidenceVerifiers: GoldenFilingCorpusVerifier[];
  sources: LegalSourceReference[];
}

export interface AccountantAcceptanceSummary {
  scenarioCount: number;
  automatedVerifierCount: number;
  professionalSignOffRequiredCount: number;
  manualHandoffScenarioCount: number;
  releaseBlockingScenarioCodes: string[];
  requiredSignOffGates: string[];
  status: string;
}

export interface AccountantWorkflowWalkthroughProtocol {
  protocolVersion: string;
  reviewerRole: string;
  status: string;
  signOffGate: string;
  failurePolicy: string;
  seededScenarioCodes: string[];
  routeSequence: string[];
  acceptanceCriteria: string[];
  requiredEvidence: string[];
}

export interface AccountantJourneyAcceptanceChecklistItem {
  routeCode: string;
  routeLabel: string;
  routeKey: string;
  workflowStages: string[];
  seededScenarioCodes: string[];
  visualArtifactNames: string[];
  requiredEvidence: string[];
  acceptanceCriteria: string[];
  signOffGate: string;
  status: string;
}

export interface AccountantWorkflowEvidencePackItem {
  routeCode: string;
  routeLabel: string;
  workflowStages: string[];
  seededScenarioCodes: string[];
  visualArtifactNames: string[];
  evidenceArtifact: string;
  decisionQuestion: string;
  requiredEvidence: string[];
  signOffGate: string;
  failurePolicy: string;
}

export interface AccountantWalkthroughEvidenceMatrixItem {
  scenarioCode: string;
  scenarioLabel: string;
  expectedOutcome: string;
  filingReadinessState: string;
  signOffPacketState: string;
  manualProfessionalReviewRequired: boolean;
  routeCode: string;
  routeLabel: string;
  routeKey: string;
  workflowStages: string[];
  visualArtifactNames: string[];
  evidenceArtifact: string;
  decisionQuestion: string;
  requiredEvidence: string[];
  acceptanceCriteria: string[];
  releaseChecklistCode: string;
  signOffGate: string;
  status: string;
  blocksRelease: boolean;
}

export interface WorkbenchVisualAcceptanceRegisterItem {
  routeCode: string;
  routeLabel: string;
  workflowStages: string[];
  acceptanceAreas: string[];
  screenshotArtifactNames: string[];
  evidenceArtifact: string;
  requiredEvidence: string[];
  releaseGateCode: string;
  status: string;
  failurePolicy: string;
  nextAction: string;
}

export interface VisualQaViewport {
  name: string;
  width: number;
  height: number;
}

export interface VisualQaRoute {
  code: string;
  routeKey: string;
  label: string;
  description: string;
  requiredText: string;
  workflowStages: string[];
  openFilingTab: boolean;
}

export interface VisualQaTabState {
  kind: string;
  id: string;
  label: string;
}

export interface VisualQaStateInventoryItem {
  stateId: string;
  routeName: string;
  routeKey: string;
  label: string;
  description: string;
  materialRoute: string | null;
  uiState: string;
  canonicalPathTemplate: string;
  canonicalUrlTemplate: string;
  canonicalQuery: Record<string, string>;
  canonicalTabState: VisualQaTabState;
  expectedText: string;
  expectedStateText: string;
  workflowStages: string[];
  authMode: string;
  reviewStatus: string;
  openFilingTab: boolean;
}

export interface VisualQaArtifact {
  stateId: string;
  routeName: string;
  routeCode: string;
  routeKey: string;
  materialRoute: string | null;
  uiState: string;
  authMode: string;
  theme: string;
  viewportName: string;
  fileName: string;
  artifactPath: string;
  requiredText: string;
  expectedStateText: string;
  canonicalUrlTemplate: string;
  canonicalQuery: Record<string, string>;
  canonicalTabState: VisualQaTabState;
  openFilingTab: boolean;
  reviewStatus: string;
  layoutChecks: string[];
}

export interface VisualQaRouteAudit {
  routeCode: string;
  routeKey: string;
  label: string;
  workflowStages: string[];
  screenshotCount: number;
  reviewStatus: string;
  reviewChecks: string[];
}

export interface VisualQaReviewProtocol {
  protocolVersion: string;
  reviewerRole: string;
  status: string;
  signOffGate: string;
  failurePolicy: string;
  acceptanceCriteria: string[];
  requiredEvidence: string[];
}

export interface VisualQaCoverage {
  artifactName: string;
  enforcement: string;
  manifestFileName: string;
  inventoryVersion: "canonical-material-states-v1";
  inventoryStateCount: number;
  routeCount: number;
  accountantWorkbenchRouteCount: number;
  expectedScreenshotCount: number;
  requiredMaterialRoutes: string[];
  requiredUiStates: string[];
  semanticDistinctnessRequired: boolean;
  layoutChecks: string[];
  reviewChecks: string[];
  reviewProtocol: VisualQaReviewProtocol;
  themes: string[];
  viewports: VisualQaViewport[];
  routes: VisualQaRoute[];
  stateInventory: VisualQaStateInventoryItem[];
  routeAudits: VisualQaRouteAudit[];
  artifacts: VisualQaArtifact[];
}

export interface ProductionAssurancePacket {
  packetId: string;
  packetVersion: string;
  status: string;
  sourceLawSnapshotHash: string;
  goldenCorpusCovered: number;
  goldenCorpusTotal: number;
  statutoryRuleMatrixPaths: number;
  statutoryRuleCoverageFamilies: number;
  visualQaExpectedScreenshots: number;
  requiredOperationalGates: number;
  openCriticalActions: number;
  evidenceItems: string[];
  releaseBlockers: string[];
}

export interface ProductionScorecardCategory {
  code: string;
  label: string;
  currentScore: number;
  targetScore: number;
  status: string;
  currentEvidence: string[];
  remainingGaps: string[];
  completionTrackCodes: string[];
  releaseBlockerCodes: string[];
  controls: ProductionScorecardControl[];
}

export type ProductionScorecardAssuranceClass = "code" | "machine" | "human-external";

export interface ProductionScorecardControl {
  code: string;
  label: string;
  weight: number;
  assuranceClass: ProductionScorecardAssuranceClass;
  status: "passed" | "open";
  passed: boolean;
  evidence: string[];
  blockingAuditItemIds: string[];
}

export interface ProductionScorecard {
  currentScore: number;
  targetScore: number;
  status: string;
  nextGate: string;
  scoreBasis: "independent-audit-control-ledger-v1";
  auditBaselineDate: string;
  auditedCommit: string;
  evidencePolicy: string;
  categories: ProductionScorecardCategory[];
}

export interface RevenueTaxonomyRangeEvidence {
  taxonomyKey: string;
  accountingStandard: string;
  taxonomyDate: string;
  label: string;
  schemaRef: string;
  acceptedByRevenue: boolean;
  automatedPlatformSelectionSupported: boolean;
  effectiveForPeriodsStartingOnOrAfter: string;
  effectiveForPeriodsStartingBefore: string;
  sourceIds: string[];
  releaseGateCodes: string[];
}

export interface ProductionReadinessReport {
  generatedAt: string;
  overallStatus: string;
  companiesInDatabase: number;
  periodsInDatabase: number;
  sourceLawSnapshot: SourceLawSnapshot;
  sourceLawTraceability: SourceLawTraceabilityEntry[];
  sourceLawMaintenanceProtocol: SourceLawMaintenanceProtocol;
  sourceLawReviewLedger: SourceLawReviewLedgerEntry[];
  revenueTaxonomyRanges: RevenueTaxonomyRangeEvidence[];
  assurancePacket: ProductionAssurancePacket;
  productionScorecard: ProductionScorecard;
  accountantAcceptanceCriteria: AccountantAcceptanceCriterion[];
  accountantAcceptanceSummary: AccountantAcceptanceSummary;
  accountantWorkflowWalkthroughProtocol: AccountantWorkflowWalkthroughProtocol;
  accountantJourneyAcceptanceChecklist: AccountantJourneyAcceptanceChecklistItem[];
  accountantWorkflowEvidencePack: AccountantWorkflowEvidencePackItem[];
  accountantWalkthroughEvidenceMatrix: AccountantWalkthroughEvidenceMatrixItem[];
  workbenchVisualAcceptanceRegister: WorkbenchVisualAcceptanceRegisterItem[];
  areas: ProductionReadinessArea[];
  goldenFilingCorpus: GoldenFilingCorpusScenario[];
  goldenEvidenceLedger: GoldenEvidenceLedgerEntry[];
  goldenVerifierManifest: GoldenVerifierManifestEntry[];
  statutoryRuleMatrix: StatutoryRuleMatrixEntry[];
  statutoryRulesCoverage: StatutoryRulesCoverageItem[];
  manualHandoffPaths: string[];
  operationalGates: OperationalGate[];
  assuranceActions: ProductionReadinessAssuranceAction[];
  completionTracks: ProductionReadinessCompletionTrack[];
  releaseBlockerRegister: ProductionReleaseBlocker[];
  auditabilityControls: ProductionAuditabilityControl[];
  auditEvidenceTimeline: AuditEvidenceTimelineEntry[];
  auditEvidencePack: ProductionAuditEvidencePackItem[];
  monitoringControls: ProductionMonitoringControl[];
  dependencyPolicyControls: DependencyPolicyControl[];
  deploymentSafetyControls: DeploymentSafetyControl[];
  operationsEvidencePack: OperationsEvidencePackItem[];
  releaseReviewChecklist: ReleaseReviewChecklistItem[];
  releaseVerificationManifest: ReleaseVerificationManifestItem[];
  humanReleaseEvidence: HumanReleaseEvidenceGate[];
  humanReleaseEvidenceCloseout: HumanReleaseEvidenceCloseoutStep[];
  visualQaCoverage: VisualQaCoverage;
}

const legalSourceReferenceSchema = z.object({
  sourceId: z.string().min(1),
  title: z.string().min(1),
  effectiveDate: z.string().min(1),
  url: z.string().url(),
});

const sourceLawSnapshotSchema = z.object({
  snapshotDate: z.string().min(1),
  snapshotVersion: z.string().min(1),
  contentHash: z.string().regex(/^sha256:[0-9a-f]{64}$/),
  sourceCount: z.number().int().nonnegative(),
  sources: z.array(legalSourceReferenceSchema),
});

const sourceLawTraceabilityEntrySchema = z.object({
  sourceId: z.string().min(1),
  title: z.string().min(1),
  effectiveDate: z.string().min(1),
  url: z.string().url(),
  inSnapshot: z.boolean(),
  usedBy: z.array(z.string().min(1)),
  releaseGateCodes: z.array(z.string().min(1)),
});

const sourceLawMaintenanceProtocolSchema = z.object({
  protocolVersion: z.string().min(1),
  ownerRole: z.string().min(1),
  status: z.string().min(1),
  reviewCadence: z.string().min(1),
  nextReviewDue: z.string().min(1),
  signOffGate: z.string().min(1),
  changeDetection: z.string().min(1),
  failurePolicy: z.string().min(1),
  monitoredSourceIds: z.array(z.string().min(1)),
  acceptanceCriteria: z.array(z.string().min(1)),
  requiredEvidence: z.array(z.string().min(1)),
});

const sourceLawReviewLedgerEntrySchema = z.object({
  sourceId: z.string().min(1),
  title: z.string().min(1),
  url: z.string().url(),
  pinnedEffectiveDate: z.string().min(1),
  ownerRole: z.string().min(1),
  releaseChecklistCode: z.string().min(1),
  blocksRelease: z.boolean(),
  reviewChecks: z.array(z.string().min(1)),
  requiredEvidence: z.array(z.string().min(1)),
});

const revenueTaxonomyRangeEvidenceSchema = z.object({
  taxonomyKey: z.string().min(1),
  accountingStandard: z.string().min(1),
  taxonomyDate: z.string().min(1),
  label: z.string().min(1),
  schemaRef: z.string().url(),
  acceptedByRevenue: z.boolean(),
  automatedPlatformSelectionSupported: z.boolean(),
  effectiveForPeriodsStartingOnOrAfter: z.string().min(1),
  effectiveForPeriodsStartingBefore: z.string(),
  sourceIds: z.array(z.string().min(1)),
  releaseGateCodes: z.array(z.string().min(1)),
});

const productionAssurancePacketSchema = z.object({
  packetId: z.string().regex(/^assurance-sha256:[0-9a-f]{64}$/),
  packetVersion: z.string().min(1),
  status: z.string().min(1),
  sourceLawSnapshotHash: z.string().regex(/^sha256:[0-9a-f]{64}$/),
  goldenCorpusCovered: z.number().int().nonnegative(),
  goldenCorpusTotal: z.number().int().nonnegative(),
  statutoryRuleMatrixPaths: z.number().int().nonnegative(),
  statutoryRuleCoverageFamilies: z.number().int().nonnegative(),
  visualQaExpectedScreenshots: z.number().int().nonnegative(),
  requiredOperationalGates: z.number().int().nonnegative(),
  openCriticalActions: z.number().int().nonnegative(),
  evidenceItems: z.array(z.string().min(1)),
  releaseBlockers: z.array(z.string().min(1)),
});

const productionScorecardControlSchema = z.object({
  code: z.string().min(1),
  label: z.string().min(1),
  weight: z.number().int().positive(),
  assuranceClass: z.enum(["code", "machine", "human-external"]),
  status: z.enum(["passed", "open"]),
  passed: z.boolean(),
  evidence: z.array(z.string().min(1)).min(1),
  blockingAuditItemIds: z.array(z.string().min(1)),
});

const productionScorecardCategorySchema = z.object({
  code: z.string().min(1),
  label: z.string().min(1),
  currentScore: z.number().int().nonnegative(),
  targetScore: z.number().int().positive(),
  status: z.string().min(1),
  currentEvidence: z.array(z.string().min(1)),
  remainingGaps: z.array(z.string().min(1)),
  completionTrackCodes: z.array(z.string().min(1)),
  releaseBlockerCodes: z.array(z.string().min(1)),
  controls: z.array(productionScorecardControlSchema).min(1),
});

const productionScorecardSchema = z.object({
  currentScore: z.number().int().nonnegative(),
  targetScore: z.number().int().positive(),
  status: z.string().min(1),
  nextGate: z.string().min(1),
  scoreBasis: z.literal("independent-audit-control-ledger-v1"),
  auditBaselineDate: z.string().regex(/^\d{4}-\d{2}-\d{2}$/),
  auditedCommit: z.literal("7ea54cc6d1769ced568ac1568d190cc2bb4b16d1"),
  evidencePolicy: z.string().min(1),
  categories: z.array(productionScorecardCategorySchema),
});

const humanReleaseEvidenceGateSchema = z.object({
  code: z.string().min(1),
  label: z.string().min(1),
  templateFile: z.string().regex(/^[a-z0-9-]+-template\.md$/),
  requiredReviewerRole: z.string().min(1),
  status: z.string().min(1),
  signOffGate: z.string().min(1),
  releaseChecklistCode: z.string().min(1),
  releaseManifestCode: z.string().min(1),
  evidenceArtifact: z.string().min(1),
  blocksRelease: z.boolean(),
  reviewerPickupFiles: z.array(z.string().min(1)),
  requiredEvidence: z.array(z.string().min(1)),
  nextAction: z.string().min(1),
});

const humanReleaseEvidenceCloseoutStepSchema = z.object({
  code: z.string().min(1),
  label: z.string().min(1),
  sequence: z.number().int().positive(),
  detail: z.string().min(1),
  artifact: z.string().min(1),
  blocksRelease: z.boolean(),
});

const productionReadinessAreaSchema = z.object({
  code: z.string().min(1),
  label: z.string().min(1),
  status: z.string().min(1),
  detail: z.string().min(1),
});

const goldenFilingCorpusEvidencePackSchema = z.object({
  outputArtifacts: z.array(z.string().min(1)),
  decisionGates: z.array(z.string().min(1)),
  expectedValueChecks: z.array(z.string().min(1)),
  expectedOutputs: z.object({
    pdfTextMarkers: z.array(z.string().min(1)),
    ixbrlRequiredTags: z.array(z.string().min(1)),
    filingReadinessState: z.string().min(1),
    expectedCorporationTax: z.number().nonnegative(),
    requiredNotes: z.array(z.string().min(1)),
    filingGateStates: z.array(z.string().min(1)),
    signOffPacketState: z.string().min(1),
  }),
  expectedProofPoints: z.array(z.object({
    area: z.string().min(1),
    expectedEvidence: z.string().min(1),
    automatedVerifier: z.string().min(1),
    required: z.boolean(),
  })),
  sourceReferences: z.array(legalSourceReferenceSchema),
});

const goldenFilingCorpusFixtureSchema = z.object({
  legalName: z.string().min(1),
  companyType: z.string().min(1),
  periodStart: z.string().min(1),
  periodEnd: z.string().min(1),
  expectedSizeClass: z.string().min(1),
  expectedRegime: z.string().min(1),
  auditExempt: z.boolean(),
  manualProfessionalReviewRequired: z.boolean(),
});

const goldenFilingCorpusVerifierSchema = z.object({
  name: z.string().min(1),
  command: z.string().min(1),
  ciScope: z.string().min(1),
  runsInDefaultCi: z.boolean(),
  environment: z.string().min(1),
  evidenceLevel: z.string().min(1),
});

const goldenFilingCorpusLegalBasisSnapshotSchema = z.object({
  scenarioCode: z.string().min(1),
  companyType: z.string().min(1),
  sizeClass: z.string().min(1),
  electedRegime: z.string().min(1),
  auditExempt: z.boolean(),
  manualProfessionalReviewRequired: z.boolean(),
  legalBasis: z.string().min(1),
  requiredOutputs: z.array(z.string().min(1)),
  professionalGates: z.array(z.string().min(1)),
  sourceIds: z.array(z.string().min(1)),
});

const goldenFilingCorpusScenarioSchema = z.object({
  code: z.string().min(1),
  label: z.string().min(1),
  companyScope: z.string().min(1),
  expectedOutcome: z.string().min(1),
  coverageStatus: z.string().min(1),
  fixture: goldenFilingCorpusFixtureSchema,
  evidenceTestNames: z.array(z.string().min(1)),
  evidenceVerifiers: z.array(goldenFilingCorpusVerifierSchema),
  assertions: z.array(z.string().min(1)),
  evidencePack: goldenFilingCorpusEvidencePackSchema,
  legalBasisSnapshot: goldenFilingCorpusLegalBasisSnapshotSchema,
});

const goldenEvidenceLedgerEntrySchema = z.object({
  scenarioCode: z.string().min(1),
  label: z.string().min(1),
  fixtureLegalName: z.string().min(1),
  companyType: z.string().min(1),
  expectedOutcome: z.string().min(1),
  coverageStatus: z.string().min(1),
  acceptanceStatus: z.string().min(1),
  requiredSignOffGate: z.string().min(1),
  blocksRelease: z.boolean(),
  automatedVerifierNames: z.array(z.string().min(1)),
  automatedVerifierCommands: z.array(z.string().min(1)),
  ciScopes: z.array(z.string().min(1)),
  evidenceLevels: z.array(z.string().min(1)),
  outputArtifacts: z.array(z.string().min(1)),
  decisionGates: z.array(z.string().min(1)),
  expectedValueChecks: z.array(z.string().min(1)),
  proofPointAreas: z.array(z.string().min(1)),
  sourceIds: z.array(z.string().min(1)),
  expectedCorporationTax: z.number().nonnegative(),
  filingReadinessState: z.string().min(1),
  signOffPacketState: z.string().min(1),
});

const goldenVerifierManifestEntrySchema = z.object({
  scenarioCode: z.string().min(1),
  scenarioLabel: z.string().min(1),
  expectedOutcome: z.string().min(1),
  coverageStatus: z.string().min(1),
  verifierName: z.string().min(1),
  command: z.string().min(1),
  ciScope: z.string().min(1),
  runsInDefaultCi: z.boolean(),
  evidenceLevel: z.string().min(1),
  blocksRelease: z.boolean(),
  outputArtifacts: z.array(z.string().min(1)),
  decisionGates: z.array(z.string().min(1)),
  proofPointAreas: z.array(z.string().min(1)),
});

const statutoryRuleMatrixEntrySchema = z.object({
  code: z.string().min(1),
  companyScope: z.string().min(1),
  sizeOrRegime: z.string().min(1),
  supportLevel: z.string().min(1),
  requiredEvidence: z.array(z.string().min(1)),
  requiredOutputs: z.array(z.string().min(1)),
  manualHandoffGates: z.array(z.string().min(1)),
  sources: z.array(legalSourceReferenceSchema),
});

const statutoryRulesCoverageItemSchema = z.object({
  code: z.string().min(1),
  ruleFamily: z.string().min(1),
  decisionUnderTest: z.string().min(1),
  coverageStatus: z.string().min(1),
  automatedVerifierNames: z.array(z.string().min(1)),
  edgeCases: z.array(z.string().min(1)),
  sources: z.array(legalSourceReferenceSchema),
});

const operationalGateSchema = z.object({
  code: z.string().min(1),
  label: z.string().min(1),
  required: z.boolean(),
  status: z.string().min(1),
  detail: z.string().min(1),
});

const productionReadinessAssuranceActionSchema = z.object({
  code: z.string().min(1),
  label: z.string().min(1),
  owner: z.string().min(1),
  priority: z.string().min(1),
  riskRank: z.number().int().nonnegative(),
  evidenceStage: z.string().min(1),
  status: z.string().min(1),
  detail: z.string().min(1),
  evidenceRequired: z.string().min(1),
});

const productionReadinessCompletionTrackSchema = z.object({
  code: z.string().min(1),
  label: z.string().min(1),
  ownerRole: z.string().min(1),
  status: z.string().min(1),
  completionCriteria: z.array(z.string().min(1)),
  currentEvidence: z.array(z.string().min(1)),
  nextActions: z.array(z.string().min(1)),
  assuranceActionCodes: z.array(z.string().min(1)),
});

const productionReleaseBlockerSchema = z.object({
  code: z.string().min(1),
  trackCode: z.string().min(1),
  trackLabel: z.string().min(1),
  ownerRole: z.string().min(1),
  severity: z.string().min(1),
  riskRank: z.number().int().nonnegative(),
  blockingIssue: z.string().min(1),
  requiredEvidence: z.string().min(1),
  nextAction: z.string().min(1),
  sourceActionCode: z.string().min(1),
  releaseChecklistCode: z.string().min(1),
  operationalGateCode: z.string(),
  evidenceArtifact: z.string().min(1),
  blocksRelease: z.boolean(),
});

const productionAuditabilityControlSchema = z.object({
  code: z.string().min(1),
  label: z.string().min(1),
  required: z.boolean(),
  enforcement: z.string().min(1),
  evidenceCaptured: z.string().min(1),
  verification: z.string().min(1),
  auditEventCodes: z.array(z.string().min(1)),
});

const auditEvidenceTimelineEntrySchema = z.object({
  code: z.string().min(1),
  stage: z.string().min(1),
  evidenceQuestion: z.string().min(1),
  capturedWhen: z.string().min(1),
  requiredActor: z.string().min(1),
  verification: z.string().min(1),
  auditEventCodes: z.array(z.string().min(1)),
  blockingGateCodes: z.array(z.string().min(1)),
});

const productionAuditEvidencePackItemSchema = z.object({
  code: z.string().min(1),
  label: z.string().min(1),
  evidenceQuestion: z.string().min(1),
  requiredArtifact: z.string().min(1),
  retainedIn: z.string().min(1),
  requiredActor: z.string().min(1),
  capturedWhen: z.string().min(1),
  verification: z.string().min(1),
  failurePolicy: z.string().min(1),
  auditEventCodes: z.array(z.string().min(1)),
  blockingGateCodes: z.array(z.string().min(1)),
});

const productionMonitoringControlSchema = z.object({
  code: z.string().min(1),
  label: z.string().min(1),
  provider: z.string().min(1),
  required: z.boolean(),
  productionSafetyGate: z.string().min(1),
  evidenceCaptured: z.string().min(1),
  verification: z.string().min(1),
  alertRoute: z.string().min(1),
  failurePolicy: z.string().min(1),
});

const dependencyPolicyControlSchema = z.object({
  code: z.string().min(1),
  label: z.string().min(1),
  required: z.boolean(),
  enforcement: z.string().min(1),
  evidenceCaptured: z.string().min(1),
  verification: z.string().min(1),
  failurePolicy: z.string().min(1),
});

const deploymentSafetyControlSchema = z.object({
  code: z.string().min(1),
  label: z.string().min(1),
  required: z.boolean(),
  enforcement: z.string().min(1),
  evidenceCaptured: z.string().min(1),
  verification: z.string().min(1),
  failurePolicy: z.string().min(1),
});

const operationsEvidencePackItemSchema = z.object({
  code: z.string().min(1),
  label: z.string().min(1),
  category: z.string().min(1),
  ownerRole: z.string().min(1),
  required: z.boolean(),
  command: z.string().min(1),
  requiredArtifact: z.string().min(1),
  releaseGateCode: z.string().min(1),
  verification: z.string().min(1),
  failurePolicy: z.string().min(1),
});

const releaseReviewChecklistItemSchema = z.object({
  code: z.string().min(1),
  label: z.string().min(1),
  ownerRole: z.string().min(1),
  required: z.boolean(),
  status: z.string().min(1),
  blocksRelease: z.boolean(),
  evidenceArtifact: z.string().min(1),
  assuranceActionCode: z.string().min(1),
  operationalGateCode: z.string(),
  auditEventCodes: z.array(z.string().min(1)),
  detail: z.string().min(1),
});

const releaseVerificationManifestItemSchema = z.object({
  code: z.string().min(1),
  label: z.string().min(1),
  ownerRole: z.string().min(1),
  command: z.string().min(1),
  ciScope: z.string().min(1),
  runsInDefaultCi: z.boolean(),
  blocksRelease: z.boolean(),
  evidenceArtifact: z.string().min(1),
  releaseChecklistEvidenceArtifact: z.string().min(1),
  manualFallback: z.string().min(1),
});

const accountantAcceptanceCriterionSchema = z.object({
  scenarioCode: z.string().min(1),
  label: z.string().min(1),
  required: z.boolean(),
  acceptanceStatus: z.string().min(1),
  reviewScope: z.array(z.string().min(1)),
  requiredEvidence: z.array(z.string().min(1)),
  requiredSignOffGate: z.string().min(1),
  evidenceVerifiers: z.array(goldenFilingCorpusVerifierSchema),
  sources: z.array(legalSourceReferenceSchema),
});

const accountantAcceptanceSummarySchema = z.object({
  scenarioCount: z.number().int().nonnegative(),
  automatedVerifierCount: z.number().int().nonnegative(),
  professionalSignOffRequiredCount: z.number().int().nonnegative(),
  manualHandoffScenarioCount: z.number().int().nonnegative(),
  releaseBlockingScenarioCodes: z.array(z.string().min(1)),
  requiredSignOffGates: z.array(z.string().min(1)),
  status: z.string().min(1),
});

const accountantWorkflowWalkthroughProtocolSchema = z.object({
  protocolVersion: z.string().min(1),
  reviewerRole: z.string().min(1),
  status: z.string().min(1),
  signOffGate: z.string().min(1),
  failurePolicy: z.string().min(1),
  seededScenarioCodes: z.array(z.string().min(1)),
  routeSequence: z.array(z.string().min(1)),
  acceptanceCriteria: z.array(z.string().min(1)),
  requiredEvidence: z.array(z.string().min(1)),
});

const accountantJourneyAcceptanceChecklistItemSchema = z.object({
  routeCode: z.string().min(1),
  routeLabel: z.string().min(1),
  routeKey: z.string().min(1),
  workflowStages: z.array(z.string().min(1)),
  seededScenarioCodes: z.array(z.string().min(1)),
  visualArtifactNames: z.array(z.string().min(1)),
  requiredEvidence: z.array(z.string().min(1)),
  acceptanceCriteria: z.array(z.string().min(1)),
  signOffGate: z.string().min(1),
  status: z.string().min(1),
});

const accountantWorkflowEvidencePackItemSchema = z.object({
  routeCode: z.string().min(1),
  routeLabel: z.string().min(1),
  workflowStages: z.array(z.string().min(1)),
  seededScenarioCodes: z.array(z.string().min(1)),
  visualArtifactNames: z.array(z.string().min(1)),
  evidenceArtifact: z.string().min(1),
  decisionQuestion: z.string().min(1),
  requiredEvidence: z.array(z.string().min(1)),
  signOffGate: z.string().min(1),
  failurePolicy: z.string().min(1),
});

const accountantWalkthroughEvidenceMatrixItemSchema = z.object({
  scenarioCode: z.string().min(1),
  scenarioLabel: z.string().min(1),
  expectedOutcome: z.string().min(1),
  filingReadinessState: z.string().min(1),
  signOffPacketState: z.string().min(1),
  manualProfessionalReviewRequired: z.boolean(),
  routeCode: z.string().min(1),
  routeLabel: z.string().min(1),
  routeKey: z.string().min(1),
  workflowStages: z.array(z.string().min(1)),
  visualArtifactNames: z.array(z.string().min(1)),
  evidenceArtifact: z.string().min(1),
  decisionQuestion: z.string().min(1),
  requiredEvidence: z.array(z.string().min(1)),
  acceptanceCriteria: z.array(z.string().min(1)),
  releaseChecklistCode: z.string().min(1),
  signOffGate: z.string().min(1),
  status: z.string().min(1),
  blocksRelease: z.boolean(),
});

const workbenchVisualAcceptanceRegisterItemSchema = z.object({
  routeCode: z.string().min(1),
  routeLabel: z.string().min(1),
  workflowStages: z.array(z.string().min(1)),
  acceptanceAreas: z.array(z.string().min(1)),
  screenshotArtifactNames: z.array(z.string().min(1)),
  evidenceArtifact: z.string().min(1),
  requiredEvidence: z.array(z.string().min(1)),
  releaseGateCode: z.string().min(1),
  status: z.string().min(1),
  failurePolicy: z.string().min(1),
  nextAction: z.string().min(1),
});

const visualQaViewportSchema = z.object({
  name: z.string().min(1),
  width: z.number(),
  height: z.number(),
});

const visualQaRouteSchema = z.object({
  code: z.string().min(1),
  routeKey: z.string().min(1),
  label: z.string().min(1),
  description: z.string().min(1),
  requiredText: z.string().min(1),
  workflowStages: z.array(z.string().min(1)),
  openFilingTab: z.boolean(),
});

const visualQaTabStateSchema = z.object({
  kind: z.string().min(1),
  id: z.string().min(1),
  label: z.string().min(1),
});

const visualQaStateInventoryItemSchema = z.object({
  stateId: z.string().min(1),
  routeName: z.string().min(1),
  routeKey: z.string().min(1),
  label: z.string().min(1),
  description: z.string().min(1),
  materialRoute: z.string().min(1).nullable(),
  uiState: z.string().min(1),
  canonicalPathTemplate: z.string().startsWith("/"),
  canonicalUrlTemplate: z.string().startsWith("/"),
  canonicalQuery: z.record(z.string(), z.string()),
  canonicalTabState: visualQaTabStateSchema,
  expectedText: z.string().min(1),
  expectedStateText: z.string().min(1),
  workflowStages: z.array(z.string().min(1)).min(1),
  authMode: z.enum(["anonymous", "authenticated"]),
  reviewStatus: z.literal("required-review"),
  openFilingTab: z.literal(false),
});

const visualQaArtifactSchema = z.object({
  stateId: z.string().min(1),
  routeName: z.string().min(1),
  routeCode: z.string().min(1),
  routeKey: z.string().min(1),
  materialRoute: z.string().min(1).nullable(),
  uiState: z.string().min(1),
  authMode: z.enum(["anonymous", "authenticated"]),
  theme: z.string().min(1),
  viewportName: z.string().min(1),
  fileName: z.string().min(1),
  artifactPath: z.string().min(1),
  requiredText: z.string().min(1),
  expectedStateText: z.string().min(1),
  canonicalUrlTemplate: z.string().startsWith("/"),
  canonicalQuery: z.record(z.string(), z.string()),
  canonicalTabState: visualQaTabStateSchema,
  openFilingTab: z.literal(false),
  reviewStatus: z.literal("required-review"),
  layoutChecks: z.array(z.string().min(1)),
});

const visualQaRouteAuditSchema = z.object({
  routeCode: z.string().min(1),
  routeKey: z.string().min(1),
  label: z.string().min(1),
  workflowStages: z.array(z.string().min(1)),
  screenshotCount: z.number().int().nonnegative(),
  reviewStatus: z.string().min(1),
  reviewChecks: z.array(z.string().min(1)),
});

const visualQaReviewProtocolSchema = z.object({
  protocolVersion: z.string().min(1),
  reviewerRole: z.string().min(1),
  status: z.string().min(1),
  signOffGate: z.string().min(1),
  failurePolicy: z.string().min(1),
  acceptanceCriteria: z.array(z.string().min(1)),
  requiredEvidence: z.array(z.string().min(1)),
});

const visualQaCoverageSchema = z.object({
  artifactName: z.string().min(1),
  enforcement: z.string().min(1),
  manifestFileName: z.string().min(1),
  inventoryVersion: z.literal("canonical-material-states-v1"),
  inventoryStateCount: z.number().int().positive(),
  routeCount: z.number().int().positive(),
  accountantWorkbenchRouteCount: z.number().int().positive(),
  expectedScreenshotCount: z.number().int().positive(),
  requiredMaterialRoutes: z.array(z.string().min(1)),
  requiredUiStates: z.array(z.string().min(1)),
  semanticDistinctnessRequired: z.literal(true),
  layoutChecks: z.array(z.string().min(1)),
  reviewChecks: z.array(z.string().min(1)),
  reviewProtocol: visualQaReviewProtocolSchema,
  themes: z.array(z.string().min(1)),
  viewports: z.array(visualQaViewportSchema),
  routes: z.array(visualQaRouteSchema),
  stateInventory: z.array(visualQaStateInventoryItemSchema),
  routeAudits: z.array(visualQaRouteAuditSchema),
  artifacts: z.array(visualQaArtifactSchema),
});

export const productionReadinessReportSchema = z.object({
  generatedAt: z.string().min(1),
  overallStatus: z.string().min(1),
  companiesInDatabase: z.number(),
  periodsInDatabase: z.number(),
  sourceLawSnapshot: sourceLawSnapshotSchema,
  sourceLawTraceability: z.array(sourceLawTraceabilityEntrySchema),
  sourceLawMaintenanceProtocol: sourceLawMaintenanceProtocolSchema,
  sourceLawReviewLedger: z.array(sourceLawReviewLedgerEntrySchema),
  revenueTaxonomyRanges: z.array(revenueTaxonomyRangeEvidenceSchema),
  assurancePacket: productionAssurancePacketSchema,
  productionScorecard: productionScorecardSchema,
  accountantAcceptanceCriteria: z.array(accountantAcceptanceCriterionSchema),
  accountantAcceptanceSummary: accountantAcceptanceSummarySchema,
  accountantWorkflowWalkthroughProtocol: accountantWorkflowWalkthroughProtocolSchema,
  accountantJourneyAcceptanceChecklist: z.array(accountantJourneyAcceptanceChecklistItemSchema),
  accountantWorkflowEvidencePack: z.array(accountantWorkflowEvidencePackItemSchema),
  accountantWalkthroughEvidenceMatrix: z.array(accountantWalkthroughEvidenceMatrixItemSchema),
  workbenchVisualAcceptanceRegister: z.array(workbenchVisualAcceptanceRegisterItemSchema),
  areas: z.array(productionReadinessAreaSchema),
  goldenFilingCorpus: z.array(goldenFilingCorpusScenarioSchema),
  goldenEvidenceLedger: z.array(goldenEvidenceLedgerEntrySchema),
  goldenVerifierManifest: z.array(goldenVerifierManifestEntrySchema),
  statutoryRuleMatrix: z.array(statutoryRuleMatrixEntrySchema),
  statutoryRulesCoverage: z.array(statutoryRulesCoverageItemSchema),
  manualHandoffPaths: z.array(z.string().min(1)),
  operationalGates: z.array(operationalGateSchema),
  assuranceActions: z.array(productionReadinessAssuranceActionSchema),
  completionTracks: z.array(productionReadinessCompletionTrackSchema),
  releaseBlockerRegister: z.array(productionReleaseBlockerSchema),
  auditabilityControls: z.array(productionAuditabilityControlSchema),
  auditEvidenceTimeline: z.array(auditEvidenceTimelineEntrySchema),
  auditEvidencePack: z.array(productionAuditEvidencePackItemSchema),
  monitoringControls: z.array(productionMonitoringControlSchema),
  dependencyPolicyControls: z.array(dependencyPolicyControlSchema),
  deploymentSafetyControls: z.array(deploymentSafetyControlSchema),
  operationsEvidencePack: z.array(operationsEvidencePackItemSchema),
  releaseReviewChecklist: z.array(releaseReviewChecklistItemSchema),
  releaseVerificationManifest: z.array(releaseVerificationManifestItemSchema),
  humanReleaseEvidence: z.array(humanReleaseEvidenceGateSchema),
  humanReleaseEvidenceCloseout: z.array(humanReleaseEvidenceCloseoutStepSchema),
  visualQaCoverage: visualQaCoverageSchema,
});

export function parseProductionReadinessReport(payload: unknown): ProductionReadinessReport {
  const result = productionReadinessReportSchema.safeParse(payload);

  if (!result.success) {
    const issue = result.error.issues[0];
    const path = issue?.path.length ? issue.path.join(".") : "root";
    const message = issue?.message ?? "Invalid payload";
    throw new Error(`Invalid production readiness report contract: ${path} - ${message}`);
  }

  const report: ProductionReadinessReport = result.data;
  assertProductionReadinessInvariants(report);
  return report;
}

const CANONICAL_VISUAL_STATE_IDS = [
  "login", "password-change", "dashboard", "onboarding", "production-readiness", "company-detail",
  "period-workspace", "classification", "categorisation", "year-end", "adjustments", "notes", "charity",
  "financial-statements", "statement-source-trail", "statement-profit-and-loss", "statement-balance-sheet",
  "statement-tax-computation", "statement-cash-flow", "statement-equity-changes", "statement-directors-report",
  "filing-review", "workbench-preview", "state-loading", "state-empty", "state-maximum-data", "state-error",
  "state-partial-error", "state-permission-denied", "state-read-only", "state-stale", "state-conflict",
];

const CANONICAL_VISUAL_MATERIAL_ROUTES = [
  "login", "password-change", "onboarding", "classification", "categorisation", "year-end", "adjustments",
  "notes", "charity", "statement-trial-balance", "statement-source-trail", "statement-profit-and-loss",
  "statement-balance-sheet", "statement-tax-computation", "statement-cash-flow", "statement-equity-changes",
  "statement-directors-report", "filing",
];

const CANONICAL_VISUAL_UI_STATES = [
  "loading", "empty", "maximum-data", "error", "partial-error", "permission-denied", "read-only", "stale", "conflict",
];

const CANONICAL_VISUAL_VIEWPORTS = [
  { name: "mobile", width: 390, height: 844 },
  { name: "tablet", width: 768, height: 1024 },
  { name: "desktop", width: 1440, height: 1000 },
];

function assertProductionReadinessInvariants(report: ProductionReadinessReport) {
  assertExpectedNumber(
    "sourceLawSnapshot.sourceCount",
    report.sourceLawSnapshot.sources.length,
    report.sourceLawSnapshot.sourceCount,
  );
  assertExpectedNumber(
    "sourceLawTraceability.length",
    report.sourceLawSnapshot.sourceCount,
    report.sourceLawTraceability.length,
  );
  assertExpectedNumber(
    "assurancePacket.goldenCorpusTotal",
    report.goldenFilingCorpus.length,
    report.assurancePacket.goldenCorpusTotal,
  );
  assertExpectedNumber(
    "assurancePacket.goldenCorpusCovered",
    report.goldenFilingCorpus.filter((scenario) => scenario.coverageStatus === "covered").length,
    report.assurancePacket.goldenCorpusCovered,
  );
  assertExpectedNumber(
    "visualQaCoverage.expectedScreenshotCount",
    report.visualQaCoverage.themes.length * report.visualQaCoverage.viewports.length * report.visualQaCoverage.stateInventory.length,
    report.visualQaCoverage.expectedScreenshotCount,
  );
  assertExpectedNumber(
    "visualQaCoverage.inventoryStateCount",
    CANONICAL_VISUAL_STATE_IDS.length,
    report.visualQaCoverage.inventoryStateCount,
  );
  assertExpectedNumber(
    "visualQaCoverage.routeCount",
    CANONICAL_VISUAL_STATE_IDS.length,
    report.visualQaCoverage.routeCount,
  );
  assertExpectedNumber(
    "visualQaCoverage.accountantWorkbenchRouteCount",
    7,
    report.visualQaCoverage.accountantWorkbenchRouteCount,
  );
  assertExpectedNumber(
    "visualQaCoverage.routes.length",
    report.visualQaCoverage.accountantWorkbenchRouteCount,
    report.visualQaCoverage.routes.length,
  );
  assertExpectedNumber(
    "visualQaCoverage.stateInventory.length",
    report.visualQaCoverage.inventoryStateCount,
    report.visualQaCoverage.stateInventory.length,
  );
  assertStringArrayEqual(
    "visualQaCoverage.stateInventory.stateIds",
    CANONICAL_VISUAL_STATE_IDS,
    report.visualQaCoverage.stateInventory.map((state) => state.stateId),
  );
  assertStringArrayEqual(
    "visualQaCoverage.requiredMaterialRoutes",
    CANONICAL_VISUAL_MATERIAL_ROUTES,
    report.visualQaCoverage.requiredMaterialRoutes,
  );
  assertStringArrayEqual(
    "visualQaCoverage.requiredUiStates",
    CANONICAL_VISUAL_UI_STATES,
    report.visualQaCoverage.requiredUiStates,
  );
  assertStringArrayEqual("visualQaCoverage.themes", ["light", "dark"], report.visualQaCoverage.themes);
  assertVisualQaViewports(report.visualQaCoverage.viewports);
  assertStringArrayEqual(
    "visualQaCoverage.layoutChecks",
    ["browser-console-errors", "page-horizontal-overflow", "visible-text-overlap"],
    report.visualQaCoverage.layoutChecks,
  );
  assertStringArrayEqual(
    "visualQaCoverage.reviewChecks",
    [
      "accountant-workflow-hierarchy",
      "table-scanability",
      "theme-contrast",
      "responsive-density",
      "loading-error-empty-states",
      "canonical-url-tab-state",
      "semantic-capture-distinctness",
      "stale-conflict-states",
    ],
    report.visualQaCoverage.reviewChecks,
  );
  assertExpectedNumber(
    "visualQaCoverage.artifacts.length",
    report.visualQaCoverage.expectedScreenshotCount,
    report.visualQaCoverage.artifacts.length,
  );
  assertExpectedNumber(
    "assurancePacket.visualQaExpectedScreenshots",
    report.visualQaCoverage.expectedScreenshotCount,
    report.assurancePacket.visualQaExpectedScreenshots,
  );
  assertAssuranceActionsRiskOrder(report.assuranceActions);
  assertSourceLawMaintenanceProtocol(report);
  assertVisualQaArtifacts(report);

  const expectedWorkflowStages = [
    "Setup",
    "Import",
    "Classify",
    "Year-End",
    "Statements",
    "Notes",
    "Review",
    "Filing",
  ];
  const coveredWorkflowStages = new Set(report.visualQaCoverage.routes.flatMap((route) => route.workflowStages));
  const missingWorkflowStages = expectedWorkflowStages.filter((stage) => !coveredWorkflowStages.has(stage));

  if (missingWorkflowStages.length > 0) {
    throw new Error(
      `Invalid production readiness report contract: visualQaCoverage.routes.workflowStages - missing accountant workflow stages: ${missingWorkflowStages.join(", ")}`,
    );
  }

  report.visualQaCoverage.routes.forEach((route, routeIndex) => {
    if (route.workflowStages.length === 0) {
      throw new Error(
        `Invalid production readiness report contract: visualQaCoverage.routes.${routeIndex}.workflowStages - every visual QA route must state the workflow stages it proves`,
      );
    }
  });

  const periodWorkspace = report.visualQaCoverage.routes.find((route) => route.code === "period-workspace");
  if (!periodWorkspace || expectedWorkflowStages.some((stage) => !periodWorkspace.workflowStages.includes(stage))) {
    throw new Error(
      "Invalid production readiness report contract: visualQaCoverage.routes.period-workspace.workflowStages - period workspace must prove the full accountant workflow rail",
    );
  }

  const scenarioCodes = new Set(report.goldenFilingCorpus.map((scenario) => scenario.code));
  const acceptanceCodes = new Set(report.accountantAcceptanceCriteria.map((criterion) => criterion.scenarioCode));
  const missingAcceptanceCriteria = [...scenarioCodes]
    .filter((code) => !acceptanceCodes.has(code))
    .sort();

  if (missingAcceptanceCriteria.length > 0) {
    throw new Error(
      `Invalid production readiness report contract: accountantAcceptanceCriteria - missing acceptance criteria for golden scenarios: ${missingAcceptanceCriteria.join(", ")}`,
    );
  }

  const scenariosByCode = new Map(report.goldenFilingCorpus.map((scenario) => [scenario.code, scenario]));
  assertProductionSprintGoldenScenarios(scenariosByCode);
  assertGoldenEvidenceLedger(report, scenariosByCode);
  assertGoldenVerifierManifest(report, scenariosByCode);
  report.accountantAcceptanceCriteria.forEach((criterion, criterionIndex) => {
    const scenario = scenariosByCode.get(criterion.scenarioCode);

    if (!scenario) {
      throw new Error(
        `Invalid production readiness report contract: accountantAcceptanceCriteria.${criterionIndex}.scenarioCode - must reference a golden scenario`,
      );
    }

    const scenarioVerifierNames = scenario.evidenceVerifiers
      .map((verifier) => verifier.name)
      .sort((left, right) => left.localeCompare(right));
    const acceptanceVerifierNames = criterion.evidenceVerifiers
      .map((verifier) => verifier.name)
      .sort((left, right) => left.localeCompare(right));

    if (
      scenarioVerifierNames.length !== acceptanceVerifierNames.length ||
      scenarioVerifierNames.some((name, index) => name !== acceptanceVerifierNames[index])
    ) {
      throw new Error(
        `Invalid production readiness report contract: accountantAcceptanceCriteria.${criterionIndex}.evidenceVerifiers - must match the golden scenario verifier manifest`,
      );
    }

    criterion.evidenceVerifiers.forEach((verifier, verifierIndex) => {
      if (!verifier.command.includes("dotnet test Accounts.slnx") || !verifier.command.includes(verifier.name)) {
        throw new Error(
          `Invalid production readiness report contract: accountantAcceptanceCriteria.${criterionIndex}.evidenceVerifiers.${verifierIndex}.command - must include the executable backend verifier command`,
        );
      }
    });
  });

  const requiredAcceptanceCriteria = report.accountantAcceptanceCriteria.filter((criterion) => criterion.required);
  const acceptanceSummary = report.accountantAcceptanceSummary;
  assertExpectedNumber(
    "accountantAcceptanceSummary.scenarioCount",
    report.goldenFilingCorpus.length,
    acceptanceSummary.scenarioCount,
  );
  assertExpectedNumber(
    "accountantAcceptanceSummary.professionalSignOffRequiredCount",
    requiredAcceptanceCriteria.length,
    acceptanceSummary.professionalSignOffRequiredCount,
  );
  assertExpectedNumber(
    "accountantAcceptanceSummary.automatedVerifierCount",
    new Set(report.accountantAcceptanceCriteria.flatMap((criterion) => criterion.evidenceVerifiers.map((verifier) => verifier.name))).size,
    acceptanceSummary.automatedVerifierCount,
  );
  assertExpectedNumber(
    "accountantAcceptanceSummary.manualHandoffScenarioCount",
    report.goldenFilingCorpus.filter((scenario) =>
      scenario.expectedOutcome.toLowerCase().includes("manual-handoff")
      || scenario.fixture.manualProfessionalReviewRequired
    ).length,
    acceptanceSummary.manualHandoffScenarioCount,
  );
  const expectedReleaseBlockingScenarioCodes = requiredAcceptanceCriteria
    .filter((criterion) => criterion.acceptanceStatus !== "accepted")
    .map((criterion) => criterion.scenarioCode)
    .sort((left, right) => left.localeCompare(right));
  assertStringArrayEqual(
    "accountantAcceptanceSummary.releaseBlockingScenarioCodes",
    expectedReleaseBlockingScenarioCodes,
    [...acceptanceSummary.releaseBlockingScenarioCodes].sort((left, right) => left.localeCompare(right)),
  );
  assertStringArrayEqual(
    "accountantAcceptanceSummary.requiredSignOffGates",
    [...new Set(requiredAcceptanceCriteria.map((criterion) => criterion.requiredSignOffGate))].sort((left, right) => left.localeCompare(right)),
    [...acceptanceSummary.requiredSignOffGates].sort((left, right) => left.localeCompare(right)),
  );
  const expectedAcceptanceSummaryStatus = expectedReleaseBlockingScenarioCodes.length === 0
    ? "accepted"
    : "qualified-accountant-review-required";
  if (acceptanceSummary.status !== expectedAcceptanceSummaryStatus) {
    throw new Error(
      `Invalid production readiness report contract: accountantAcceptanceSummary.status - expected ${expectedAcceptanceSummaryStatus}, received ${acceptanceSummary.status}`,
    );
  }

  assertAccountantWorkflowWalkthroughProtocol(report);
  assertAccountantJourneyAcceptanceChecklist(report);
  assertAccountantWorkflowEvidencePack(report);
  assertAccountantWalkthroughEvidenceMatrix(report);
  assertWorkbenchVisualAcceptanceRegister(report);

  const snapshotSourceIds = new Set(report.sourceLawSnapshot.sources.map((source) => source.sourceId));
  const traceabilitySourceIds = new Set(report.sourceLawTraceability.map((entry) => entry.sourceId));
  const missingTraceability = [...snapshotSourceIds]
    .filter((sourceId) => !traceabilitySourceIds.has(sourceId))
    .sort();
  const unexpectedTraceability = [...traceabilitySourceIds]
    .filter((sourceId) => !snapshotSourceIds.has(sourceId))
    .sort();

  if (missingTraceability.length > 0 || unexpectedTraceability.length > 0) {
    throw new Error(
      `Invalid production readiness report contract: sourceLawTraceability - expected snapshot source ids only; missing ${missingTraceability.join(", ") || "none"}, unexpected ${unexpectedTraceability.join(", ") || "none"}`,
    );
  }

  report.sourceLawTraceability.forEach((entry, entryIndex) => {
    if (!entry.inSnapshot) {
      throw new Error(
        `Invalid production readiness report contract: sourceLawTraceability.${entryIndex}.inSnapshot - every production source must be pinned in the snapshot`,
      );
    }

    if (entry.usedBy.length === 0) {
      throw new Error(
        `Invalid production readiness report contract: sourceLawTraceability.${entryIndex}.usedBy - every pinned source must have at least one usage`,
      );
    }

    if (entry.releaseGateCodes.length === 0) {
      throw new Error(
        `Invalid production readiness report contract: sourceLawTraceability.${entryIndex}.releaseGateCodes - every pinned source must link to at least one release gate`,
      );
    }
  });

  assertSourceLawReviewLedger(report, snapshotSourceIds);
  assertRevenueTaxonomyRanges(report);

  if (!report.assurancePacket.evidenceItems.includes("source-law-traceability-index")) {
    throw new Error(
      "Invalid production readiness report contract: assurancePacket.evidenceItems - source-law-traceability-index is required",
    );
  }

  if (!report.assurancePacket.evidenceItems.includes("release-review-checklist")) {
    throw new Error(
      "Invalid production readiness report contract: assurancePacket.evidenceItems - release-review-checklist is required",
    );
  }

  if (!report.assurancePacket.evidenceItems.includes("release-blocker-register")) {
    throw new Error(
      "Invalid production readiness report contract: assurancePacket.evidenceItems - release-blocker-register is required",
    );
  }

  if (!report.assurancePacket.evidenceItems.includes("release-verification-manifest")) {
    throw new Error(
      "Invalid production readiness report contract: assurancePacket.evidenceItems - release-verification-manifest is required",
    );
  }

  if (!report.assurancePacket.evidenceItems.includes("audit-evidence-timeline")) {
    throw new Error(
      "Invalid production readiness report contract: assurancePacket.evidenceItems - audit-evidence-timeline is required",
    );
  }

  if (!report.assurancePacket.evidenceItems.includes("production-audit-evidence-pack")) {
    throw new Error(
      "Invalid production readiness report contract: assurancePacket.evidenceItems - production-audit-evidence-pack is required",
    );
  }

  if (!report.assurancePacket.evidenceItems.includes("production-completion-map")) {
    throw new Error(
      "Invalid production readiness report contract: assurancePacket.evidenceItems - production-completion-map is required",
    );
  }

  if (!report.assurancePacket.evidenceItems.includes("operations-evidence-pack")) {
    throw new Error(
      "Invalid production readiness report contract: assurancePacket.evidenceItems - operations-evidence-pack is required",
    );
  }

  if (!report.assurancePacket.evidenceItems.includes("golden-evidence-ledger")) {
    throw new Error(
      "Invalid production readiness report contract: assurancePacket.evidenceItems - golden-evidence-ledger is required",
    );
  }

  if (!report.assurancePacket.evidenceItems.includes("source-law-review-ledger")) {
    throw new Error(
      "Invalid production readiness report contract: assurancePacket.evidenceItems - source-law-review-ledger is required",
    );
  }

  report.auditEvidenceTimeline.forEach((entry, entryIndex) => {
    if (entry.auditEventCodes.length === 0) {
      throw new Error(
        `Invalid production readiness report contract: auditEvidenceTimeline.${entryIndex}.auditEventCodes - at least one audit event code is required`,
      );
    }

    if (entry.blockingGateCodes.length === 0) {
      throw new Error(
        `Invalid production readiness report contract: auditEvidenceTimeline.${entryIndex}.blockingGateCodes - at least one blocking gate code is required`,
      );
    }
  });

  report.auditEvidencePack.forEach((item, itemIndex) => {
    if (item.auditEventCodes.length === 0) {
      throw new Error(
        `Invalid production readiness report contract: auditEvidencePack.${itemIndex}.auditEventCodes - at least one audit event code is required`,
      );
    }

    if (item.blockingGateCodes.length === 0) {
      throw new Error(
        `Invalid production readiness report contract: auditEvidencePack.${itemIndex}.blockingGateCodes - at least one blocking gate code is required`,
      );
    }
  });

  const operationalGateCodes = new Set(report.operationalGates.map((gate) => gate.code));
  const operationsControlGateCodes = new Set([
    "production-monitoring",
    "dependency-policy-controls",
    "deployment-safety-controls",
  ]);

  report.operationsEvidencePack.forEach((item, itemIndex) => {
    if (!item.required) {
      throw new Error(
        `Invalid production readiness report contract: operationsEvidencePack.${itemIndex}.required - operations release evidence must be required`,
      );
    }

    if (!operationalGateCodes.has(item.releaseGateCode) && !operationsControlGateCodes.has(item.releaseGateCode)) {
      throw new Error(
        `Invalid production readiness report contract: operationsEvidencePack.${itemIndex}.releaseGateCode - unknown release gate ${item.releaseGateCode}`,
      );
    }

    if (!item.failurePolicy.toLowerCase().includes("block release")) {
      throw new Error(
        `Invalid production readiness report contract: operationsEvidencePack.${itemIndex}.failurePolicy - must block release when evidence is missing`,
      );
    }
  });

  assertCompletionTracks(report);
  assertReleaseReviewChecklist(report);
  assertReleaseBlockerRegister(report);
  assertProductionScorecard(report);
  assertReleaseVerificationManifest(report);
  assertHumanReleaseEvidence(report);
  assertHumanReleaseEvidenceCloseout(report);

  report.goldenFilingCorpus.forEach((scenario, scenarioIndex) => {
    const evidenceTests = new Set(scenario.evidenceTestNames);
    const verifierNames = new Set(scenario.evidenceVerifiers.map((verifier) => verifier.name));
    const legalBasis = scenario.legalBasisSnapshot;

    if (legalBasis.scenarioCode !== scenario.code) {
      throw new Error(
        `Invalid production readiness report contract: goldenFilingCorpus.${scenarioIndex}.legalBasisSnapshot.scenarioCode - must match scenario code`,
      );
    }

    if (legalBasis.companyType !== scenario.fixture.companyType) {
      throw new Error(
        `Invalid production readiness report contract: goldenFilingCorpus.${scenarioIndex}.legalBasisSnapshot.companyType - must match fixture company type`,
      );
    }

    if (legalBasis.sizeClass !== scenario.fixture.expectedSizeClass) {
      throw new Error(
        `Invalid production readiness report contract: goldenFilingCorpus.${scenarioIndex}.legalBasisSnapshot.sizeClass - must match fixture size class`,
      );
    }

    if (legalBasis.electedRegime !== scenario.fixture.expectedRegime) {
      throw new Error(
        `Invalid production readiness report contract: goldenFilingCorpus.${scenarioIndex}.legalBasisSnapshot.electedRegime - must match fixture regime`,
      );
    }

    if (legalBasis.auditExempt !== scenario.fixture.auditExempt) {
      throw new Error(
        `Invalid production readiness report contract: goldenFilingCorpus.${scenarioIndex}.legalBasisSnapshot.auditExempt - must match fixture audit exemption`,
      );
    }

    if (legalBasis.manualProfessionalReviewRequired !== scenario.fixture.manualProfessionalReviewRequired) {
      throw new Error(
        `Invalid production readiness report contract: goldenFilingCorpus.${scenarioIndex}.legalBasisSnapshot.manualProfessionalReviewRequired - must match fixture manual review gate`,
      );
    }

    const requiredOutputs = new Set(legalBasis.requiredOutputs);
    const missingOutputs = scenario.evidencePack.outputArtifacts.filter((artifact) => !requiredOutputs.has(artifact));
    if (missingOutputs.length > 0) {
      throw new Error(
        `Invalid production readiness report contract: goldenFilingCorpus.${scenarioIndex}.legalBasisSnapshot.requiredOutputs - missing evidence-pack outputs: ${missingOutputs.join(", ")}`,
      );
    }

    const professionalGates = new Set(legalBasis.professionalGates);
    const missingGates = scenario.evidencePack.decisionGates.filter((gate) => !professionalGates.has(gate));
    if (missingGates.length > 0) {
      throw new Error(
        `Invalid production readiness report contract: goldenFilingCorpus.${scenarioIndex}.legalBasisSnapshot.professionalGates - missing evidence-pack gates: ${missingGates.join(", ")}`,
      );
    }

    const legalSourceIds = new Set(legalBasis.sourceIds);
    const missingSourceIds = scenario.evidencePack.sourceReferences
      .map((sourceReference) => sourceReference.sourceId)
      .filter((sourceId) => !legalSourceIds.has(sourceId));
    if (missingSourceIds.length > 0) {
      throw new Error(
        `Invalid production readiness report contract: goldenFilingCorpus.${scenarioIndex}.legalBasisSnapshot.sourceIds - missing evidence-pack source IDs: ${missingSourceIds.join(", ")}`,
      );
    }

    scenario.evidenceTestNames.forEach((testName) => {
      if (!verifierNames.has(testName)) {
        throw new Error(
          `Invalid production readiness report contract: goldenFilingCorpus.${scenarioIndex}.evidenceVerifiers - every evidenceTestNames entry must have verifier metadata`,
        );
      }
    });

    scenario.evidenceVerifiers.forEach((verifier, verifierIndex) => {
      if (!evidenceTests.has(verifier.name)) {
        throw new Error(
          `Invalid production readiness report contract: goldenFilingCorpus.${scenarioIndex}.evidenceVerifiers.${verifierIndex}.name - verifier must be listed in evidenceTestNames`,
        );
      }

      if (
        (scenario.coverageStatus === "covered" || scenario.coverageStatus === "machine-covered-review-pending")
        && !verifier.runsInDefaultCi
      ) {
        throw new Error(
          `Invalid production readiness report contract: goldenFilingCorpus.${scenarioIndex}.evidenceVerifiers.${verifierIndex}.runsInDefaultCi - covered scenarios must run in default CI`,
        );
      }
    });

    scenario.evidencePack.expectedProofPoints.forEach((proofPoint, proofPointIndex) => {
      if (!evidenceTests.has(proofPoint.automatedVerifier)) {
        throw new Error(
          `Invalid production readiness report contract: goldenFilingCorpus.${scenarioIndex}.evidencePack.expectedProofPoints.${proofPointIndex}.automatedVerifier - verifier must be listed in evidenceTestNames`,
        );
      }
    });
  });

  if (!report.assurancePacket.evidenceItems.includes("golden-verifier-manifest")) {
    throw new Error(
      "Invalid production readiness report contract: assurancePacket.evidenceItems - golden-verifier-manifest is required",
    );
  }
}

function assertProductionSprintGoldenScenarios(
  scenariosByCode: Map<string, GoldenFilingCorpusScenario>,
) {
  const requiredScenarioCodes = [
    "micro-ltd",
    "small-abridged-ltd",
    "clg-charity",
    "medium-audit-required",
  ];
  const missingScenarioCodes = requiredScenarioCodes.filter((code) => !scenariosByCode.has(code));

  if (missingScenarioCodes.length > 0) {
    throw new Error(
      `Invalid production readiness report contract: goldenFilingCorpus - missing required production sprint scenarios: ${missingScenarioCodes.join(", ")}`,
    );
  }

  assertSmallAbridgedGoldenScenario(scenariosByCode);
  assertClgCharityGoldenScenario(scenariosByCode);
  assertMediumAuditRequiredGoldenScenario(scenariosByCode);
}

function assertSmallAbridgedGoldenScenario(
  scenariosByCode: Map<string, GoldenFilingCorpusScenario>,
) {
  const scenario = scenariosByCode.get("small-abridged-ltd");
  const requiredVerifier = "FilingGoldenCorpusScenarioTests.GoldenCorpus_SmallAbridgedLtd_EmitsFullAccountsAbridgedCroPackIxbrlAndReadiness";

  if (!scenario) {
    throw new Error(
      "Invalid production readiness report contract: goldenFilingCorpus.small-abridged-ltd - scenario is required",
    );
  }

  if (!scenario.evidenceTestNames.includes(requiredVerifier)) {
    throw new Error(
      "Invalid production readiness report contract: goldenFilingCorpus.small-abridged-ltd - dedicated small abridged verifier is required",
    );
  }

  if (!scenario.evidenceVerifiers.some((verifier) => verifier.name === requiredVerifier)) {
    throw new Error(
      "Invalid production readiness report contract: goldenFilingCorpus.small-abridged-ltd - dedicated small abridged verifier metadata is required",
    );
  }

  const evidencePack = scenario.evidencePack;
  if (!evidencePack.outputArtifacts.some((artifact) => artifact.toLowerCase().includes("abridged cro filing pack"))) {
    throw new Error(
      "Invalid production readiness report contract: goldenFilingCorpus.small-abridged-ltd.evidencePack.outputArtifacts - abridged CRO filing pack evidence is required",
    );
  }

  if (!evidencePack.expectedValueChecks.some((check) => check.toLowerCase().includes("section 352"))) {
    throw new Error(
      "Invalid production readiness report contract: goldenFilingCorpus.small-abridged-ltd.evidencePack.expectedValueChecks - Section 352 evidence is required",
    );
  }

  if (!evidencePack.expectedOutputs.pdfTextMarkers.some((marker) => marker.toLowerCase().includes("section 352"))) {
    throw new Error(
      "Invalid production readiness report contract: goldenFilingCorpus.small-abridged-ltd.evidencePack.expectedOutputs.pdfTextMarkers - Section 352 PDF marker is required",
    );
  }

  if (!evidencePack.expectedOutputs.filingGateStates.some((gate) => gate.toLowerCase().includes("abridgement eligibility"))) {
    throw new Error(
      "Invalid production readiness report contract: goldenFilingCorpus.small-abridged-ltd.evidencePack.expectedOutputs.filingGateStates - abridgement eligibility gate is required",
    );
  }
}

function assertClgCharityGoldenScenario(
  scenariosByCode: Map<string, GoldenFilingCorpusScenario>,
) {
  const scenario = scenariosByCode.get("clg-charity");
  const requiredVerifier = "FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness";

  if (!scenario) return;

  if (!scenario.evidenceTestNames.includes(requiredVerifier)) {
    throw new Error(
      "Invalid production readiness report contract: goldenFilingCorpus.clg-charity - dedicated CLG charity verifier is required",
    );
  }

  if (scenario.fixture.companyType !== "CompanyLimitedByGuarantee") {
    throw new Error(
      "Invalid production readiness report contract: goldenFilingCorpus.clg-charity.fixture.companyType - CLG charity fixture must use CompanyLimitedByGuarantee",
    );
  }

  const evidencePack = scenario.evidencePack;
  if (!evidencePack.outputArtifacts.some((artifact) => artifact.toLowerCase().includes("charity readiness profile"))) {
    throw new Error(
      "Invalid production readiness report contract: goldenFilingCorpus.clg-charity.evidencePack.outputArtifacts - charity readiness profile evidence is required",
    );
  }

  if (!evidencePack.decisionGates.some((gate) => gate.toLowerCase().includes("charity annual return review"))) {
    throw new Error(
      "Invalid production readiness report contract: goldenFilingCorpus.clg-charity.evidencePack.decisionGates - charity annual return review gate is required",
    );
  }

  if (!evidencePack.expectedValueChecks.some((check) => check.toLowerCase().includes("charities regulator"))) {
    throw new Error(
      "Invalid production readiness report contract: goldenFilingCorpus.clg-charity.evidencePack.expectedValueChecks - Charities Regulator source evidence is required",
    );
  }

  if (!evidencePack.expectedOutputs.filingGateStates.some((gate) => gate.toLowerCase().includes("sofa"))) {
    throw new Error(
      "Invalid production readiness report contract: goldenFilingCorpus.clg-charity.evidencePack.expectedOutputs.filingGateStates - SoFA and trustees report evidence gate is required",
    );
  }
}

function assertMediumAuditRequiredGoldenScenario(
  scenariosByCode: Map<string, GoldenFilingCorpusScenario>,
) {
  const scenario = scenariosByCode.get("medium-audit-required");
  const requiredVerifier = "FilingGoldenCorpusScenarioTests.GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence";

  if (!scenario) return;

  if (!scenario.evidenceTestNames.includes(requiredVerifier)) {
    throw new Error(
      "Invalid production readiness report contract: goldenFilingCorpus.medium-audit-required - dedicated medium audit-required verifier is required",
    );
  }

  if (!scenario.expectedOutcome.toLowerCase().includes("manual-handoff")) {
    throw new Error(
      "Invalid production readiness report contract: goldenFilingCorpus.medium-audit-required.expectedOutcome - manual handoff outcome is required",
    );
  }

  if (!scenario.fixture.manualProfessionalReviewRequired || scenario.fixture.auditExempt) {
    throw new Error(
      "Invalid production readiness report contract: goldenFilingCorpus.medium-audit-required.fixture - audit-required manual professional review fixture is required",
    );
  }

  const evidencePack = scenario.evidencePack;
  if (!evidencePack.outputArtifacts.some((artifact) => artifact.toLowerCase().includes("auditor report evidence"))) {
    throw new Error(
      "Invalid production readiness report contract: goldenFilingCorpus.medium-audit-required.evidencePack.outputArtifacts - auditor report evidence is required",
    );
  }

  if (!evidencePack.decisionGates.some((gate) => gate.toLowerCase().includes("auditor handoff"))) {
    throw new Error(
      "Invalid production readiness report contract: goldenFilingCorpus.medium-audit-required.evidencePack.decisionGates - auditor handoff gate is required",
    );
  }

  if (!evidencePack.expectedOutputs.filingGateStates.some((gate) => gate.toLowerCase().includes("normal cro approval blocked"))) {
    throw new Error(
      "Invalid production readiness report contract: goldenFilingCorpus.medium-audit-required.evidencePack.expectedOutputs.filingGateStates - CRO approval blocker is required",
    );
  }

  if (evidencePack.expectedOutputs.signOffPacketState !== "manual-handoff") {
    throw new Error(
      "Invalid production readiness report contract: goldenFilingCorpus.medium-audit-required.evidencePack.expectedOutputs.signOffPacketState - manual handoff sign-off state is required",
    );
  }
}

function assertGoldenEvidenceLedger(
  report: ProductionReadinessReport,
  scenariosByCode: Map<string, GoldenFilingCorpusScenario>,
) {
  const ledgerCodes = report.goldenEvidenceLedger.map((entry) => entry.scenarioCode);
  const duplicateLedgerCodes = ledgerCodes.filter((code, index) => ledgerCodes.indexOf(code) !== index);
  if (duplicateLedgerCodes.length > 0) {
    throw new Error(
      `Invalid production readiness report contract: goldenEvidenceLedger - duplicate scenario codes: ${[...new Set(duplicateLedgerCodes)].join(", ")}`,
    );
  }

  const scenarioCodes = [...scenariosByCode.keys()].sort((left, right) => left.localeCompare(right));
  const sortedLedgerCodes = [...ledgerCodes].sort((left, right) => left.localeCompare(right));
  if (
    scenarioCodes.length !== sortedLedgerCodes.length ||
    scenarioCodes.some((code, index) => code !== sortedLedgerCodes[index])
  ) {
    const missing = scenarioCodes.filter((code) => !sortedLedgerCodes.includes(code));
    const unexpected = sortedLedgerCodes.filter((code) => !scenarioCodes.includes(code));
    throw new Error(
      `Invalid production readiness report contract: goldenEvidenceLedger - expected one ledger entry per golden scenario; missing ${missing.join(", ") || "none"}, unexpected ${unexpected.join(", ") || "none"}`,
    );
  }

  const acceptanceByScenario = new Map(report.accountantAcceptanceCriteria.map((criterion) => [criterion.scenarioCode, criterion]));
  report.goldenEvidenceLedger.forEach((entry, entryIndex) => {
    const scenario = scenariosByCode.get(entry.scenarioCode);
    const acceptance = acceptanceByScenario.get(entry.scenarioCode);
    if (!scenario || !acceptance) return;

    assertLedgerValue(entryIndex, "label", scenario.label, entry.label, "must mirror golden scenario label");
    assertLedgerValue(entryIndex, "fixtureLegalName", scenario.fixture.legalName, entry.fixtureLegalName, "must mirror golden scenario fixture");
    assertLedgerValue(entryIndex, "companyType", scenario.fixture.companyType, entry.companyType, "must mirror golden scenario fixture");
    assertLedgerValue(entryIndex, "expectedOutcome", scenario.expectedOutcome, entry.expectedOutcome, "must mirror golden scenario outcome");
    assertLedgerValue(entryIndex, "coverageStatus", scenario.coverageStatus, entry.coverageStatus, "must mirror golden scenario coverage");
    assertLedgerValue(entryIndex, "acceptanceStatus", acceptance.acceptanceStatus, entry.acceptanceStatus, "must mirror accountant acceptance status");
    assertLedgerValue(entryIndex, "requiredSignOffGate", acceptance.requiredSignOffGate, entry.requiredSignOffGate, "must mirror accountant acceptance sign-off gate");
    assertLedgerValue(
      entryIndex,
      "filingReadinessState",
      scenario.evidencePack.expectedOutputs.filingReadinessState,
      entry.filingReadinessState,
      "must mirror golden scenario expected outputs",
    );
    assertLedgerValue(
      entryIndex,
      "signOffPacketState",
      scenario.evidencePack.expectedOutputs.signOffPacketState,
      entry.signOffPacketState,
      "must mirror golden scenario expected outputs",
    );

    if (scenario.evidencePack.expectedOutputs.expectedCorporationTax !== entry.expectedCorporationTax) {
      throw new Error(
        `Invalid production readiness report contract: goldenEvidenceLedger.${entryIndex}.expectedCorporationTax - must mirror golden scenario expected outputs`,
      );
    }

    if (!entry.blocksRelease) {
      throw new Error(
        `Invalid production readiness report contract: goldenEvidenceLedger.${entryIndex}.blocksRelease - golden scenarios require professional release review`,
      );
    }

    assertLedgerStringArray(
      entryIndex,
      "automatedVerifierNames",
      scenario.evidenceTestNames,
      entry.automatedVerifierNames,
      "must mirror golden scenario verifier manifest",
    );
    assertLedgerStringArray(
      entryIndex,
      "automatedVerifierCommands",
      scenario.evidenceVerifiers.map((verifier) => verifier.command),
      entry.automatedVerifierCommands,
      "must mirror golden scenario verifier commands",
    );
    assertLedgerStringArray(
      entryIndex,
      "ciScopes",
      [...new Set(scenario.evidenceVerifiers.map((verifier) => verifier.ciScope))],
      entry.ciScopes,
      "must mirror golden scenario verifier CI scopes",
    );
    assertLedgerStringArray(
      entryIndex,
      "evidenceLevels",
      [...new Set(scenario.evidenceVerifiers.map((verifier) => verifier.evidenceLevel))],
      entry.evidenceLevels,
      "must mirror golden scenario verifier evidence levels",
    );
    assertLedgerStringArray(
      entryIndex,
      "outputArtifacts",
      scenario.evidencePack.outputArtifacts,
      entry.outputArtifacts,
      "must mirror golden scenario evidence pack",
    );
    assertLedgerStringArray(
      entryIndex,
      "decisionGates",
      scenario.evidencePack.decisionGates,
      entry.decisionGates,
      "must mirror golden scenario evidence pack",
    );
    assertLedgerStringArray(
      entryIndex,
      "expectedValueChecks",
      scenario.evidencePack.expectedValueChecks,
      entry.expectedValueChecks,
      "must mirror golden scenario evidence pack",
    );
    assertLedgerStringArray(
      entryIndex,
      "proofPointAreas",
      scenario.evidencePack.expectedProofPoints.map((proofPoint) => proofPoint.area),
      entry.proofPointAreas,
      "must mirror golden scenario proof points",
    );
    assertLedgerStringArray(
      entryIndex,
      "sourceIds",
      scenario.evidencePack.sourceReferences.map((source) => source.sourceId),
      entry.sourceIds,
      "must mirror golden scenario source references",
    );
  });
}

function assertGoldenVerifierManifest(
  report: ProductionReadinessReport,
  scenariosByCode: Map<string, GoldenFilingCorpusScenario>,
) {
  const expectedEntryCount = report.goldenFilingCorpus.reduce(
    (count, scenario) => count + scenario.evidenceVerifiers.length,
    0,
  );

  if (report.goldenVerifierManifest.length !== expectedEntryCount) {
    throw new Error(
      `Invalid production readiness report contract: goldenVerifierManifest - expected ${expectedEntryCount} verifier entries, received ${report.goldenVerifierManifest.length}`,
    );
  }

  const ledgerByScenario = new Map(report.goldenEvidenceLedger.map((entry) => [entry.scenarioCode, entry]));
  const seenKeys = new Set<string>();

  report.goldenVerifierManifest.forEach((entry, entryIndex) => {
    const scenario = scenariosByCode.get(entry.scenarioCode);
    const ledger = ledgerByScenario.get(entry.scenarioCode);
    if (!scenario || !ledger) {
      throw new Error(
        `Invalid production readiness report contract: goldenVerifierManifest.${entryIndex}.scenarioCode - must reference a golden scenario and ledger entry`,
      );
    }

    const key = `${entry.scenarioCode}:${entry.verifierName}`;
    if (seenKeys.has(key)) {
      throw new Error(
        `Invalid production readiness report contract: goldenVerifierManifest.${entryIndex}.verifierName - duplicate verifier for scenario`,
      );
    }
    seenKeys.add(key);

    const verifier = scenario.evidenceVerifiers.find((item) => item.name === entry.verifierName);
    if (!verifier) {
      throw new Error(
        `Invalid production readiness report contract: goldenVerifierManifest.${entryIndex}.verifierName - must be listed on the golden scenario`,
      );
    }

    assertVerifierManifestValue(entryIndex, "scenarioLabel", scenario.label, entry.scenarioLabel, "must mirror golden scenario label");
    assertVerifierManifestValue(entryIndex, "expectedOutcome", scenario.expectedOutcome, entry.expectedOutcome, "must mirror golden scenario outcome");
    assertVerifierManifestValue(entryIndex, "coverageStatus", scenario.coverageStatus, entry.coverageStatus, "must mirror golden scenario coverage");
    assertVerifierManifestValue(entryIndex, "command", verifier.command, entry.command, "must mirror golden scenario verifier command");
    assertVerifierManifestValue(entryIndex, "ciScope", verifier.ciScope, entry.ciScope, "must mirror golden scenario verifier CI scope");
    assertVerifierManifestValue(entryIndex, "evidenceLevel", verifier.evidenceLevel, entry.evidenceLevel, "must mirror golden scenario verifier evidence level");

    if (entry.runsInDefaultCi !== verifier.runsInDefaultCi) {
      throw new Error(
        `Invalid production readiness report contract: goldenVerifierManifest.${entryIndex}.runsInDefaultCi - must mirror golden scenario verifier CI behavior`,
      );
    }

    if (!entry.blocksRelease) {
      throw new Error(
        `Invalid production readiness report contract: goldenVerifierManifest.${entryIndex}.blocksRelease - golden verifiers require release blocking evidence`,
      );
    }

    if (!ledger.automatedVerifierCommands.includes(entry.command)) {
      throw new Error(
        `Invalid production readiness report contract: goldenVerifierManifest.${entryIndex}.command - must mirror golden evidence ledger commands`,
      );
    }

    assertVerifierManifestStringArray(
      entryIndex,
      "outputArtifacts",
      scenario.evidencePack.outputArtifacts,
      entry.outputArtifacts,
      "must mirror golden scenario evidence pack",
    );
    assertVerifierManifestStringArray(
      entryIndex,
      "decisionGates",
      scenario.evidencePack.decisionGates,
      entry.decisionGates,
      "must mirror golden scenario evidence pack",
    );
    assertVerifierManifestStringArray(
      entryIndex,
      "proofPointAreas",
      scenario.evidencePack.expectedProofPoints.map((proofPoint) => proofPoint.area),
      entry.proofPointAreas,
      "must mirror golden scenario proof points",
    );
  });
}

function assertVerifierManifestValue(
  entryIndex: number,
  field: string,
  expected: string,
  received: string,
  message: string,
) {
  if (expected !== received) {
    throw new Error(`Invalid production readiness report contract: goldenVerifierManifest.${entryIndex}.${field} - ${message}`);
  }
}

function assertVerifierManifestStringArray(
  entryIndex: number,
  field: string,
  expected: string[],
  received: string[],
  message: string,
) {
  const sortedExpected = [...new Set(expected)].sort((left, right) => left.localeCompare(right));
  const sortedReceived = [...received].sort((left, right) => left.localeCompare(right));
  if (
    sortedExpected.length !== sortedReceived.length ||
    sortedExpected.some((value, index) => value !== sortedReceived[index])
  ) {
    throw new Error(`Invalid production readiness report contract: goldenVerifierManifest.${entryIndex}.${field} - ${message}`);
  }
}

function assertLedgerValue(
  entryIndex: number,
  field: string,
  expected: string,
  received: string,
  message: string,
) {
  if (expected !== received) {
    throw new Error(`Invalid production readiness report contract: goldenEvidenceLedger.${entryIndex}.${field} - ${message}`);
  }
}

function assertLedgerStringArray(
  entryIndex: number,
  field: string,
  expected: string[],
  received: string[],
  message: string,
) {
  const sortedExpected = [...new Set(expected)].sort((left, right) => left.localeCompare(right));
  const sortedReceived = [...received].sort((left, right) => left.localeCompare(right));
  if (
    sortedExpected.length !== sortedReceived.length ||
    sortedExpected.some((value, index) => value !== sortedReceived[index])
  ) {
    throw new Error(`Invalid production readiness report contract: goldenEvidenceLedger.${entryIndex}.${field} - ${message}`);
  }
}

function assertSourceLawMaintenanceProtocol(report: ProductionReadinessReport) {
  const releaseChecklistCodes = new Set(report.releaseReviewChecklist.map((item) => item.code));
  const snapshotSourceIds = report.sourceLawSnapshot.sources.map((source) => source.sourceId).sort();
  const monitoredSourceIds = [...report.sourceLawMaintenanceProtocol.monitoredSourceIds].sort();

  if (!releaseChecklistCodes.has(report.sourceLawMaintenanceProtocol.signOffGate)) {
    throw new Error(
      "Invalid production readiness report contract: sourceLawMaintenanceProtocol.signOffGate - must reference a release checklist item",
    );
  }

  assertStringArrayEqual(
    "sourceLawMaintenanceProtocol.monitoredSourceIds",
    snapshotSourceIds,
    monitoredSourceIds,
  );

  if (!report.assurancePacket.evidenceItems.includes("source-law-maintenance-protocol")) {
    throw new Error(
      "Invalid production readiness report contract: assurancePacket.evidenceItems - source-law-maintenance-protocol is required",
    );
  }

  for (const evidence of ["source-law-snapshot-fingerprint", "source-law-traceability-index"]) {
    if (!report.sourceLawMaintenanceProtocol.requiredEvidence.includes(evidence)) {
      throw new Error(
        `Invalid production readiness report contract: sourceLawMaintenanceProtocol.requiredEvidence - ${evidence} is required`,
      );
    }
  }

  if (report.sourceLawMaintenanceProtocol.acceptanceCriteria.length === 0) {
    throw new Error(
      "Invalid production readiness report contract: sourceLawMaintenanceProtocol.acceptanceCriteria - at least one criterion is required",
    );
  }
}

function assertSourceLawReviewLedger(report: ProductionReadinessReport, snapshotSourceIds: Set<string>) {
  const releaseChecklistCodes = new Set(report.releaseReviewChecklist.map((item) => item.code));
  const ledgerSourceIds = new Set(report.sourceLawReviewLedger.map((entry) => entry.sourceId));
  const missingLedger = [...snapshotSourceIds]
    .filter((sourceId) => !ledgerSourceIds.has(sourceId))
    .sort();
  const unexpectedLedger = [...ledgerSourceIds]
    .filter((sourceId) => !snapshotSourceIds.has(sourceId))
    .sort();

  if (missingLedger.length > 0 || unexpectedLedger.length > 0) {
    throw new Error(
      `Invalid production readiness report contract: sourceLawReviewLedger - expected snapshot source ids only; missing ${missingLedger.join(", ") || "none"}, unexpected ${unexpectedLedger.join(", ") || "none"}`,
    );
  }

  report.sourceLawReviewLedger.forEach((entry, entryIndex) => {
    if (!releaseChecklistCodes.has(entry.releaseChecklistCode)) {
      throw new Error(
        `Invalid production readiness report contract: sourceLawReviewLedger.${entryIndex}.releaseChecklistCode - must reference a release checklist item`,
      );
    }

    if (!entry.blocksRelease) {
      throw new Error(
        `Invalid production readiness report contract: sourceLawReviewLedger.${entryIndex}.blocksRelease - source-law review must block release`,
      );
    }

    if (entry.reviewChecks.length === 0) {
      throw new Error(
        `Invalid production readiness report contract: sourceLawReviewLedger.${entryIndex}.reviewChecks - at least one source-law review check is required`,
      );
    }

    for (const evidence of ["source-law-change-review-note", "qualified-accountant-source-law-signoff"]) {
      if (!entry.requiredEvidence.includes(evidence)) {
        throw new Error(
          `Invalid production readiness report contract: sourceLawReviewLedger.${entryIndex}.requiredEvidence - ${evidence} is required`,
        );
      }
    }
  });
}

function assertRevenueTaxonomyRanges(report: ProductionReadinessReport) {
  if (report.revenueTaxonomyRanges.length === 0) {
    throw new Error("Invalid production readiness report contract: revenueTaxonomyRanges - at least one accepted taxonomy range is required");
  }

  if (!report.assurancePacket.evidenceItems.includes("revenue-taxonomy-range-evidence")) {
    throw new Error(
      "Invalid production readiness report contract: assurancePacket.evidenceItems - revenue-taxonomy-range-evidence is required",
    );
  }

  report.revenueTaxonomyRanges.forEach((range, index) => {
    if (!range.releaseGateCodes.includes("ixbrl-taxonomy-selection")) {
      throw new Error(
        `Invalid production readiness report contract: revenueTaxonomyRanges.${index}.releaseGateCodes - ixbrl-taxonomy-selection is required`,
      );
    }

    if (!range.releaseGateCodes.includes("source-law-change-review")) {
      throw new Error(
        `Invalid production readiness report contract: revenueTaxonomyRanges.${index}.releaseGateCodes - source-law-change-review is required`,
      );
    }
  });
}

function assertAccountantWorkflowWalkthroughProtocol(report: ProductionReadinessReport) {
  const protocol = report.accountantWorkflowWalkthroughProtocol;
  const releaseChecklistCodes = new Set(report.releaseReviewChecklist.map((item) => item.code));
  const expectedScenarioCodes = report.goldenFilingCorpus
    .map((scenario) => scenario.code)
    .sort((left, right) => left.localeCompare(right));
  const protocolScenarioCodes = [...protocol.seededScenarioCodes].sort((left, right) => left.localeCompare(right));

  assertStringArrayEqual(
    "accountantWorkflowWalkthroughProtocol.seededScenarioCodes",
    expectedScenarioCodes,
    protocolScenarioCodes,
  );

  if (!releaseChecklistCodes.has(protocol.signOffGate)) {
    throw new Error(
      "Invalid production readiness report contract: accountantWorkflowWalkthroughProtocol.signOffGate - must reference a release checklist item",
    );
  }

  if (!report.assurancePacket.evidenceItems.includes("accountant-workflow-walkthrough-protocol")) {
    throw new Error(
      "Invalid production readiness report contract: assurancePacket.evidenceItems - accountant-workflow-walkthrough-protocol is required",
    );
  }

  if (!report.assurancePacket.evidenceItems.includes("accountant-workbench-evidence-report")) {
    throw new Error(
      "Invalid production readiness report contract: assurancePacket.evidenceItems - accountant-workbench-evidence-report is required",
    );
  }

  for (const evidence of ["seeded golden corpus walkthrough note", "named qualified-accountant approval", "visual QA screenshot review"]) {
    if (!protocol.requiredEvidence.includes(evidence)) {
      throw new Error(
        `Invalid production readiness report contract: accountantWorkflowWalkthroughProtocol.requiredEvidence - ${evidence} is required`,
      );
    }
  }

  for (const route of ["Dashboard", "Company detail", "Period workspace", "Financial statements", "Filing review", "Production readiness"]) {
    if (!protocol.routeSequence.some((step) => step.includes(route))) {
      throw new Error(
        `Invalid production readiness report contract: accountantWorkflowWalkthroughProtocol.routeSequence - ${route} is required`,
      );
    }
  }

  for (const criterion of ["Micro LTD", "Small abridged LTD", "CLG charity", "Medium/audit-required", "outputs, gates, wording and evidence"]) {
    if (!protocol.acceptanceCriteria.some((item) => item.includes(criterion))) {
      throw new Error(
        `Invalid production readiness report contract: accountantWorkflowWalkthroughProtocol.acceptanceCriteria - ${criterion} is required`,
      );
    }
  }

  if (!protocol.failurePolicy.includes("Block release")) {
    throw new Error(
      "Invalid production readiness report contract: accountantWorkflowWalkthroughProtocol.failurePolicy - must block release when accountant walkthrough evidence is missing",
    );
  }
}

function assertAccountantJourneyAcceptanceChecklist(report: ProductionReadinessReport) {
  const requiredRouteCodes = [
    "dashboard",
    "company-detail",
    "period-workspace",
    "financial-statements",
    "filing-review",
    "production-readiness",
  ];
  const checklist = report.accountantJourneyAcceptanceChecklist;
  const releaseChecklistCodes = new Set(report.releaseReviewChecklist.map((item) => item.code));
  const routeByCode = new Map(report.visualQaCoverage.routes.map((route) => [route.code, route]));
  const expectedScenarioCodes = report.goldenFilingCorpus
    .map((scenario) => scenario.code)
    .sort((left, right) => left.localeCompare(right));
  const checklistByRoute = new Map(checklist.map((item) => [item.routeCode, item]));
  const missingRoutes = requiredRouteCodes.filter((routeCode) => !checklistByRoute.has(routeCode));
  const unexpectedRoutes = checklist
    .map((item) => item.routeCode)
    .filter((routeCode) => !requiredRouteCodes.includes(routeCode))
    .sort((left, right) => left.localeCompare(right));

  if (missingRoutes.length > 0 || unexpectedRoutes.length > 0) {
    throw new Error(
      `Invalid production readiness report contract: accountantJourneyAcceptanceChecklist - missing route acceptance entries: ${missingRoutes.join(", ") || "none"}; unexpected ${unexpectedRoutes.join(", ") || "none"}`,
    );
  }

  if (!report.assurancePacket.evidenceItems.includes("accountant-journey-acceptance-checklist")) {
    throw new Error(
      "Invalid production readiness report contract: assurancePacket.evidenceItems - accountant-journey-acceptance-checklist is required",
    );
  }

  checklist.forEach((item, itemIndex) => {
    const route = routeByCode.get(item.routeCode);
    if (!route) {
      throw new Error(
        `Invalid production readiness report contract: accountantJourneyAcceptanceChecklist.${itemIndex}.routeCode - must reference a visual QA route`,
      );
    }

    if (item.routeKey !== route.routeKey || item.routeLabel !== route.label) {
      throw new Error(
        `Invalid production readiness report contract: accountantJourneyAcceptanceChecklist.${itemIndex}.routeKey - must mirror visual QA route metadata for ${item.routeCode}`,
      );
    }

    assertStringArrayEqual(
      `accountantJourneyAcceptanceChecklist.${itemIndex}.workflowStages`,
      route.workflowStages,
      item.workflowStages,
    );
    assertStringArrayEqual(
      `accountantJourneyAcceptanceChecklist.${itemIndex}.seededScenarioCodes`,
      expectedScenarioCodes,
      [...item.seededScenarioCodes].sort((left, right) => left.localeCompare(right)),
    );

    const expectedArtifactNames = report.visualQaCoverage.artifacts
      .filter((artifact) => artifact.routeCode === item.routeCode)
      .map((artifact) => artifact.fileName)
      .sort((left, right) => left.localeCompare(right));
    const receivedArtifactNames = [...item.visualArtifactNames].sort((left, right) => left.localeCompare(right));
    if (
      expectedArtifactNames.length !== receivedArtifactNames.length ||
      expectedArtifactNames.some((fileName, index) => fileName !== receivedArtifactNames[index])
    ) {
      throw new Error(
        `Invalid production readiness report contract: accountantJourneyAcceptanceChecklist.${itemIndex}.visualArtifactNames - must mirror visual smoke artifacts for ${item.routeCode}`,
      );
    }

    if (!releaseChecklistCodes.has(item.signOffGate)) {
      throw new Error(
        `Invalid production readiness report contract: accountantJourneyAcceptanceChecklist.${itemIndex}.signOffGate - must reference a release checklist item`,
      );
    }

    for (const evidence of ["named qualified-accountant route acceptance", "visual smoke screenshots reviewed", "golden corpus evidence accepted"]) {
      if (!item.requiredEvidence.includes(evidence)) {
        throw new Error(
          `Invalid production readiness report contract: accountantJourneyAcceptanceChecklist.${itemIndex}.requiredEvidence - ${evidence} is required`,
        );
      }
    }

    if (!item.acceptanceCriteria.some((criterion) => criterion.includes(item.routeLabel))) {
      throw new Error(
        `Invalid production readiness report contract: accountantJourneyAcceptanceChecklist.${itemIndex}.acceptanceCriteria - must mention ${item.routeLabel}`,
      );
    }

    if (!item.acceptanceCriteria.some((criterion) => criterion.includes("outputs, gates, wording and evidence"))) {
      throw new Error(
        `Invalid production readiness report contract: accountantJourneyAcceptanceChecklist.${itemIndex}.acceptanceCriteria - outputs, gates, wording and evidence is required`,
      );
    }
  });
}

function assertAccountantWorkflowEvidencePack(report: ProductionReadinessReport) {
  const pack = report.accountantWorkflowEvidencePack;
  const checklist = report.accountantJourneyAcceptanceChecklist;
  const checklistByRoute = new Map(checklist.map((item) => [item.routeCode, item]));
  const releaseChecklistCodes = new Set(report.releaseReviewChecklist.map((item) => item.code));
  const packRouteCodes = pack.map((item) => item.routeCode).sort((left, right) => left.localeCompare(right));
  const checklistRouteCodes = checklist.map((item) => item.routeCode).sort((left, right) => left.localeCompare(right));

  if (!report.assurancePacket.evidenceItems.includes("accountant-workflow-evidence-pack")) {
    throw new Error(
      "Invalid production readiness report contract: assurancePacket.evidenceItems - accountant-workflow-evidence-pack is required",
    );
  }

  assertStringArrayEqual(
    "accountantWorkflowEvidencePack.routeCodes",
    checklistRouteCodes,
    packRouteCodes,
  );

  pack.forEach((item, itemIndex) => {
    const checklistItem = checklistByRoute.get(item.routeCode);
    if (!checklistItem) {
      throw new Error(
        `Invalid production readiness report contract: accountantWorkflowEvidencePack.${itemIndex}.routeCode - must reference accountant journey acceptance checklist`,
      );
    }

    if (item.routeLabel !== checklistItem.routeLabel) {
      throw new Error(
        `Invalid production readiness report contract: accountantWorkflowEvidencePack.${itemIndex}.routeLabel - must mirror accountant journey acceptance checklist for ${item.routeCode}`,
      );
    }

    assertStringArrayEqual(
      `accountantWorkflowEvidencePack.${itemIndex}.workflowStages`,
      checklistItem.workflowStages,
      item.workflowStages,
    );
    assertStringArrayEqual(
      `accountantWorkflowEvidencePack.${itemIndex}.seededScenarioCodes`,
      [...checklistItem.seededScenarioCodes].sort((left, right) => left.localeCompare(right)),
      [...item.seededScenarioCodes].sort((left, right) => left.localeCompare(right)),
    );
    assertStringArrayEqual(
      `accountantWorkflowEvidencePack.${itemIndex}.visualArtifactNames`,
      [...checklistItem.visualArtifactNames].sort((left, right) => left.localeCompare(right)),
      [...item.visualArtifactNames].sort((left, right) => left.localeCompare(right)),
    );

    if (!releaseChecklistCodes.has(item.signOffGate) || item.signOffGate !== checklistItem.signOffGate) {
      throw new Error(
        `Invalid production readiness report contract: accountantWorkflowEvidencePack.${itemIndex}.signOffGate - must mirror a release checklist-backed accountant journey sign-off gate`,
      );
    }

    for (const evidence of ["named qualified-accountant route acceptance", "visual smoke screenshots reviewed", "golden corpus evidence accepted"]) {
      if (!item.requiredEvidence.includes(evidence)) {
        throw new Error(
          `Invalid production readiness report contract: accountantWorkflowEvidencePack.${itemIndex}.requiredEvidence - ${evidence} is required`,
        );
      }
    }

    if (!item.decisionQuestion.includes("outputs, gates, wording and evidence")) {
      throw new Error(
        `Invalid production readiness report contract: accountantWorkflowEvidencePack.${itemIndex}.decisionQuestion - outputs, gates, wording and evidence is required`,
      );
    }

    if (!item.failurePolicy.includes("Block release")) {
      throw new Error(
        `Invalid production readiness report contract: accountantWorkflowEvidencePack.${itemIndex}.failurePolicy - must block release until accountant route acceptance evidence is complete`,
      );
    }
  });
}

function assertAccountantWalkthroughEvidenceMatrix(report: ProductionReadinessReport) {
  const matrix = report.accountantWalkthroughEvidenceMatrix;
  const expectedRowCount = report.goldenFilingCorpus.length * report.accountantJourneyAcceptanceChecklist.length;
  const scenariosByCode = new Map(report.goldenFilingCorpus.map((scenario) => [scenario.code, scenario]));
  const acceptanceByScenarioCode = new Map(report.accountantAcceptanceCriteria.map((criterion) => [criterion.scenarioCode, criterion]));
  const checklistByRouteCode = new Map(report.accountantJourneyAcceptanceChecklist.map((item) => [item.routeCode, item]));
  const evidenceByRouteCode = new Map(report.accountantWorkflowEvidencePack.map((item) => [item.routeCode, item]));
  const releaseChecklistCodes = new Set(report.releaseReviewChecklist.map((item) => item.code));
  const seenKeys = new Set<string>();

  if (!report.assurancePacket.evidenceItems.includes("accountant-walkthrough-evidence-matrix")) {
    throw new Error(
      "Invalid production readiness report contract: assurancePacket.evidenceItems - accountant-walkthrough-evidence-matrix is required",
    );
  }

  assertExpectedNumber(
    "accountantWalkthroughEvidenceMatrix.length",
    expectedRowCount,
    matrix.length,
  );

  matrix.forEach((item, itemIndex) => {
    const scenario = scenariosByCode.get(item.scenarioCode);
    const acceptance = acceptanceByScenarioCode.get(item.scenarioCode);
    const checklistItem = checklistByRouteCode.get(item.routeCode);
    const routeEvidence = evidenceByRouteCode.get(item.routeCode);
    const rowKey = `${item.scenarioCode}:${item.routeCode}`;

    if (seenKeys.has(rowKey)) {
      throw new Error(
        `Invalid production readiness report contract: accountantWalkthroughEvidenceMatrix.${itemIndex} - duplicate scenario/route walkthrough row ${rowKey}`,
      );
    }
    seenKeys.add(rowKey);

    if (!scenario || !acceptance) {
      throw new Error(
        `Invalid production readiness report contract: accountantWalkthroughEvidenceMatrix.${itemIndex}.scenarioCode - must reference a golden scenario and accountant acceptance criterion`,
      );
    }

    if (!checklistItem || !routeEvidence) {
      throw new Error(
        `Invalid production readiness report contract: accountantWalkthroughEvidenceMatrix.${itemIndex}.routeCode - must reference the accountant journey checklist and route evidence pack`,
      );
    }

    if (item.scenarioLabel !== scenario.label || item.expectedOutcome !== scenario.expectedOutcome) {
      throw new Error(
        `Invalid production readiness report contract: accountantWalkthroughEvidenceMatrix.${itemIndex}.scenarioLabel - must mirror golden scenario metadata for ${item.scenarioCode}`,
      );
    }

    if (
      item.filingReadinessState !== scenario.evidencePack.expectedOutputs.filingReadinessState ||
      item.signOffPacketState !== scenario.evidencePack.expectedOutputs.signOffPacketState ||
      item.manualProfessionalReviewRequired !== scenario.fixture.manualProfessionalReviewRequired
    ) {
      throw new Error(
        `Invalid production readiness report contract: accountantWalkthroughEvidenceMatrix.${itemIndex}.filingReadinessState - must mirror golden scenario output readiness state`,
      );
    }

    if (item.routeLabel !== checklistItem.routeLabel || item.routeKey !== checklistItem.routeKey) {
      throw new Error(
        `Invalid production readiness report contract: accountantWalkthroughEvidenceMatrix.${itemIndex}.routeLabel - must mirror accountant journey route metadata for ${item.routeCode}`,
      );
    }

    assertStringArrayEqual(
      `accountantWalkthroughEvidenceMatrix.${itemIndex}.workflowStages`,
      checklistItem.workflowStages,
      item.workflowStages,
    );
    assertStringArrayEqual(
      `accountantWalkthroughEvidenceMatrix.${itemIndex}.visualArtifactNames`,
      [...checklistItem.visualArtifactNames].sort((left, right) => left.localeCompare(right)),
      [...item.visualArtifactNames].sort((left, right) => left.localeCompare(right)),
    );

    if (item.evidenceArtifact !== `${item.scenarioCode}-${item.routeCode}-walkthrough-note`) {
      throw new Error(
        `Invalid production readiness report contract: accountantWalkthroughEvidenceMatrix.${itemIndex}.evidenceArtifact - must be scenario-route specific`,
      );
    }

    if (item.decisionQuestion !== routeEvidence.decisionQuestion) {
      throw new Error(
        `Invalid production readiness report contract: accountantWalkthroughEvidenceMatrix.${itemIndex}.decisionQuestion - must mirror the route evidence decision question`,
      );
    }

    if (!releaseChecklistCodes.has(item.releaseChecklistCode) || item.releaseChecklistCode !== "golden-corpus-accountant-acceptance") {
      throw new Error(
        `Invalid production readiness report contract: accountantWalkthroughEvidenceMatrix.${itemIndex}.releaseChecklistCode - must reference golden-corpus-accountant-acceptance`,
      );
    }

    if (item.signOffGate !== checklistItem.signOffGate || !releaseChecklistCodes.has(item.signOffGate)) {
      throw new Error(
        `Invalid production readiness report contract: accountantWalkthroughEvidenceMatrix.${itemIndex}.signOffGate - must mirror the release checklist-backed route sign-off gate`,
      );
    }

    if (!item.blocksRelease || item.status !== "required-review") {
      throw new Error(
        `Invalid production readiness report contract: accountantWalkthroughEvidenceMatrix.${itemIndex}.status - walkthrough rows must block release until accepted`,
      );
    }

    for (const evidence of [
      "named qualified-accountant route acceptance",
      "visual smoke screenshots reviewed",
      "golden corpus evidence accepted",
      ...acceptance.requiredEvidence,
    ]) {
      if (!item.requiredEvidence.includes(evidence)) {
        throw new Error(
          `Invalid production readiness report contract: accountantWalkthroughEvidenceMatrix.${itemIndex}.requiredEvidence - ${evidence} is required`,
        );
      }
    }

    if (!item.acceptanceCriteria.some((criterion) => criterion.includes(item.routeLabel))) {
      throw new Error(
        `Invalid production readiness report contract: accountantWalkthroughEvidenceMatrix.${itemIndex}.acceptanceCriteria - must mention ${item.routeLabel}`,
      );
    }

    if (!item.acceptanceCriteria.some((criterion) => criterion.includes(item.scenarioLabel))) {
      throw new Error(
        `Invalid production readiness report contract: accountantWalkthroughEvidenceMatrix.${itemIndex}.acceptanceCriteria - must mention ${item.scenarioLabel}`,
      );
    }
  });
}

function assertWorkbenchVisualAcceptanceRegister(report: ProductionReadinessReport) {
  const register = report.workbenchVisualAcceptanceRegister;
  const routeAudits = report.visualQaCoverage.routeAudits;
  const routeAuditByCode = new Map(routeAudits.map((audit) => [audit.routeCode, audit]));
  const releaseChecklistCodes = new Set(report.releaseReviewChecklist.map((item) => item.code));
  const registerRouteCodes = register.map((item) => item.routeCode).sort((left, right) => left.localeCompare(right));
  const auditRouteCodes = routeAudits.map((item) => item.routeCode).sort((left, right) => left.localeCompare(right));

  if (!report.assurancePacket.evidenceItems.includes("workbench-visual-acceptance-register")) {
    throw new Error(
      "Invalid production readiness report contract: assurancePacket.evidenceItems - workbench-visual-acceptance-register is required",
    );
  }

  assertStringArrayEqual(
    "workbenchVisualAcceptanceRegister.routeCodes",
    auditRouteCodes,
    registerRouteCodes,
  );

  register.forEach((item, itemIndex) => {
    const routeAudit = routeAuditByCode.get(item.routeCode);
    if (!routeAudit) {
      throw new Error(
        `Invalid production readiness report contract: workbenchVisualAcceptanceRegister.${itemIndex}.routeCode - must reference a visual QA route audit`,
      );
    }

    if (item.routeLabel !== routeAudit.label) {
      throw new Error(
        `Invalid production readiness report contract: workbenchVisualAcceptanceRegister.${itemIndex}.routeLabel - must mirror visual QA route audit label for ${item.routeCode}`,
      );
    }

    assertStringArrayEqual(
      `workbenchVisualAcceptanceRegister.${itemIndex}.workflowStages`,
      routeAudit.workflowStages,
      item.workflowStages,
    );
    assertStringArrayEqual(
      `workbenchVisualAcceptanceRegister.${itemIndex}.acceptanceAreas`,
      [...routeAudit.reviewChecks].sort((left, right) => left.localeCompare(right)),
      [...item.acceptanceAreas].sort((left, right) => left.localeCompare(right)),
    );

    const expectedArtifactNames = report.visualQaCoverage.artifacts
      .filter((artifact) => artifact.routeCode === item.routeCode)
      .map((artifact) => artifact.fileName)
      .sort((left, right) => left.localeCompare(right));
    assertStringArrayEqual(
      `workbenchVisualAcceptanceRegister.${itemIndex}.screenshotArtifactNames`,
      expectedArtifactNames,
      [...item.screenshotArtifactNames].sort((left, right) => left.localeCompare(right)),
    );

    if (!releaseChecklistCodes.has(item.releaseGateCode) || item.releaseGateCode !== report.visualQaCoverage.reviewProtocol.signOffGate) {
      throw new Error(
        `Invalid production readiness report contract: workbenchVisualAcceptanceRegister.${itemIndex}.releaseGateCode - must mirror the visual QA review sign-off gate`,
      );
    }

    for (const evidence of ["route-state acceptance note", "light/dark mobile/tablet/desktop screenshot review", "named visual QA reviewer sign-off"]) {
      if (!item.requiredEvidence.includes(evidence)) {
        throw new Error(
          `Invalid production readiness report contract: workbenchVisualAcceptanceRegister.${itemIndex}.requiredEvidence - ${evidence} is required`,
        );
      }
    }

    if (!item.failurePolicy.includes("Block release")) {
      throw new Error(
        `Invalid production readiness report contract: workbenchVisualAcceptanceRegister.${itemIndex}.failurePolicy - must block release until visual acceptance is complete`,
      );
    }
  });
}

function assertVisualQaViewports(viewports: VisualQaViewport[]) {
  assertExpectedNumber("visualQaCoverage.viewports.length", CANONICAL_VISUAL_VIEWPORTS.length, viewports.length);
  CANONICAL_VISUAL_VIEWPORTS.forEach((expected, index) => {
    const actual = viewports[index];
    if (!actual || actual.name !== expected.name || actual.width !== expected.width || actual.height !== expected.height) {
      throw new Error(
        `Invalid production readiness report contract: visualQaCoverage.viewports.${index} - expected ${expected.name} ${expected.width}x${expected.height}`,
      );
    }
  });
}

function assertVisualQaArtifacts(report: ProductionReadinessReport) {
  const routeByCode = new Map(report.visualQaCoverage.routes.map((route) => [route.code, route]));
  const stateById = new Map(report.visualQaCoverage.stateInventory.map((state) => [state.stateId, state]));
  const themes = new Set(report.visualQaCoverage.themes);
  const viewports = new Set(report.visualQaCoverage.viewports.map((viewport) => viewport.name));
  const artifactsByKey = new Map<string, VisualQaArtifact>();
  const routeAuditsByCode = new Map(report.visualQaCoverage.routeAudits.map((audit) => [audit.routeCode, audit]));
  const releaseChecklistCodes = new Set(report.releaseReviewChecklist.map((item) => item.code));
  const reviewProtocol = report.visualQaCoverage.reviewProtocol;

  if (!releaseChecklistCodes.has(reviewProtocol.signOffGate)) {
    throw new Error(
      "Invalid production readiness report contract: visualQaCoverage.reviewProtocol.signOffGate - must reference a release checklist item",
    );
  }

  if (reviewProtocol.acceptanceCriteria.length === 0) {
    throw new Error(
      "Invalid production readiness report contract: visualQaCoverage.reviewProtocol.acceptanceCriteria - at least one criterion is required",
    );
  }

  if (!reviewProtocol.requiredEvidence.includes(report.visualQaCoverage.manifestFileName)) {
    throw new Error(
      "Invalid production readiness report contract: visualQaCoverage.reviewProtocol.requiredEvidence - must include the visual smoke manifest",
    );
  }

  if (!reviewProtocol.requiredEvidence.includes("screenshot SHA-256 checksums")) {
    throw new Error(
      "Invalid production readiness report contract: visualQaCoverage.reviewProtocol.requiredEvidence - must include screenshot SHA-256 checksums",
    );
  }

  if (!reviewProtocol.requiredEvidence.includes("screenshot PNG dimensions")) {
    throw new Error(
      "Invalid production readiness report contract: visualQaCoverage.reviewProtocol.requiredEvidence - must include screenshot PNG dimensions",
    );
  }

  if (!reviewProtocol.requiredEvidence.includes("screenshot nonblank pixel diversity evidence")) {
    throw new Error(
      "Invalid production readiness report contract: visualQaCoverage.reviewProtocol.requiredEvidence - must include screenshot nonblank pixel diversity evidence",
    );
  }

  if (!reviewProtocol.requiredEvidence.includes("per-screenshot automated theme contrast smoke evidence")) {
    throw new Error(
      "Invalid production readiness report contract: visualQaCoverage.reviewProtocol.requiredEvidence - must include per-screenshot automated theme contrast smoke evidence",
    );
  }

  if (!reviewProtocol.requiredEvidence.includes("visual-smoke-evidence-report.json")) {
    throw new Error(
      "Invalid production readiness report contract: visualQaCoverage.reviewProtocol.requiredEvidence - must include visual-smoke-evidence-report.json",
    );
  }

  if (!reviewProtocol.requiredEvidence.includes("accountant-workbench-evidence-report.json")) {
    throw new Error(
      "Invalid production readiness report contract: visualQaCoverage.reviewProtocol.requiredEvidence - must include accountant-workbench-evidence-report.json",
    );
  }

  if (!reviewProtocol.requiredEvidence.includes("192 canonical material-state screenshots")) {
    throw new Error(
      "Invalid production readiness report contract: visualQaCoverage.reviewProtocol.requiredEvidence - must include 192 canonical material-state screenshots",
    );
  }

  if (!reviewProtocol.requiredEvidence.includes("canonical state inventory and exact URL/tab evidence")) {
    throw new Error(
      "Invalid production readiness report contract: visualQaCoverage.reviewProtocol.requiredEvidence - must include canonical state inventory and exact URL/tab evidence",
    );
  }

  if (!reviewProtocol.requiredEvidence.includes("semantic content SHA-256 distinctness evidence")) {
    throw new Error(
      "Invalid production readiness report contract: visualQaCoverage.reviewProtocol.requiredEvidence - must include semantic content SHA-256 distinctness evidence",
    );
  }

  const materialRoutes = report.visualQaCoverage.stateInventory
    .flatMap((state) => state.materialRoute ? [state.materialRoute] : []);
  assertStringArrayEqual(
    "visualQaCoverage.stateInventory.materialRoutes",
    report.visualQaCoverage.requiredMaterialRoutes,
    materialRoutes,
  );
  assertStringArrayEqual(
    "visualQaCoverage.stateInventory.requiredUiStates",
    report.visualQaCoverage.requiredUiStates,
    report.visualQaCoverage.stateInventory
      .filter((state) => state.stateId.startsWith("state-"))
      .map((state) => state.uiState),
  );

  report.visualQaCoverage.stateInventory.forEach((state, stateIndex) => {
    if (state.routeName !== state.stateId) {
      throw new Error(
        `Invalid production readiness report contract: visualQaCoverage.stateInventory.${stateIndex}.routeName - must match stateId`,
      );
    }
    const canonicalQuery = new URLSearchParams(state.canonicalQuery).toString();
    const expectedCanonicalUrl = canonicalQuery
      ? `${state.canonicalPathTemplate}?${canonicalQuery}`
      : state.canonicalPathTemplate;
    if (state.canonicalUrlTemplate !== expectedCanonicalUrl) {
      throw new Error(
        `Invalid production readiness report contract: visualQaCoverage.stateInventory.${stateIndex}.canonicalUrlTemplate - must match canonical path and query`,
      );
    }
    if (!state.canonicalTabState.kind || !state.canonicalTabState.id || !state.canonicalTabState.label) {
      throw new Error(
        `Invalid production readiness report contract: visualQaCoverage.stateInventory.${stateIndex}.canonicalTabState - exact tab state is required`,
      );
    }
  });

  report.visualQaCoverage.routes.forEach((route, routeIndex) => {
    const state = stateById.get(route.code);
    if (!state || state.routeKey !== route.routeKey || state.label !== route.label || state.expectedText !== route.requiredText) {
      throw new Error(
        `Invalid production readiness report contract: visualQaCoverage.routes.${routeIndex} - accountant route must mirror its canonical state inventory row`,
      );
    }
  });

  report.visualQaCoverage.artifacts.forEach((artifact, artifactIndex) => {
    const state = stateById.get(artifact.stateId);
    if (!state) {
      throw new Error(
        `Invalid production readiness report contract: visualQaCoverage.artifacts.${artifactIndex}.stateId - must reference the canonical state inventory`,
      );
    }

    if (!themes.has(artifact.theme)) {
      throw new Error(
        `Invalid production readiness report contract: visualQaCoverage.artifacts.${artifactIndex}.theme - must reference a configured visual QA theme`,
      );
    }

    if (!viewports.has(artifact.viewportName)) {
      throw new Error(
        `Invalid production readiness report contract: visualQaCoverage.artifacts.${artifactIndex}.viewportName - must reference a configured visual QA viewport`,
      );
    }

    const key = visualQaArtifactKey(artifact.stateId, artifact.theme, artifact.viewportName);
    if (artifactsByKey.has(key)) {
      throw new Error(
        `Invalid production readiness report contract: visualQaCoverage.artifacts.${artifactIndex} - duplicate artifact for ${key}`,
      );
    }
    artifactsByKey.set(key, artifact);

    const expectedFileName = `${artifact.stateId}-${artifact.theme}-${artifact.viewportName}.png`;
    if (artifact.fileName !== expectedFileName || artifact.artifactPath !== `artifacts/visual-smoke/${expectedFileName}`) {
      throw new Error(
        `Invalid production readiness report contract: visualQaCoverage.artifacts.${artifactIndex}.artifactPath - must match the visual smoke screenshot naming convention`,
      );
    }

    if (artifact.routeCode !== state.stateId
      || artifact.routeName !== state.routeName
      || artifact.routeKey !== state.routeKey
      || artifact.materialRoute !== state.materialRoute
      || artifact.uiState !== state.uiState
      || artifact.authMode !== state.authMode) {
      throw new Error(
        `Invalid production readiness report contract: visualQaCoverage.artifacts.${artifactIndex}.routeKey - must mirror canonical state identity`,
      );
    }

    if (artifact.requiredText !== state.expectedText
      || artifact.expectedStateText !== state.expectedStateText
      || artifact.canonicalUrlTemplate !== state.canonicalUrlTemplate
      || JSON.stringify(artifact.canonicalQuery) !== JSON.stringify(state.canonicalQuery)
      || JSON.stringify(artifact.canonicalTabState) !== JSON.stringify(state.canonicalTabState)
      || artifact.openFilingTab !== state.openFilingTab) {
      throw new Error(
        `Invalid production readiness report contract: visualQaCoverage.artifacts.${artifactIndex}.requiredText - must mirror the canonical state capture target`,
      );
    }

    if (artifact.reviewStatus !== "required-review") {
      throw new Error(
        `Invalid production readiness report contract: visualQaCoverage.artifacts.${artifactIndex}.reviewStatus - screenshots must require named review`,
      );
    }

    assertStringArrayEqual(
      `visualQaCoverage.artifacts.${artifactIndex}.layoutChecks`,
      report.visualQaCoverage.layoutChecks,
      artifact.layoutChecks,
    );
  });

  report.visualQaCoverage.routes.forEach((route) => {
    if (!routeAuditsByCode.has(route.code)) {
      throw new Error(
        `Invalid production readiness report contract: visualQaCoverage.routeAudits - missing route audit for ${route.code}`,
      );
    }
  });

  report.visualQaCoverage.routeAudits.forEach((audit, auditIndex) => {
    const route = routeByCode.get(audit.routeCode);

    if (!route) {
      throw new Error(
        `Invalid production readiness report contract: visualQaCoverage.routeAudits.${auditIndex}.routeCode - must reference a visual QA route`,
      );
    }

    if (audit.routeKey !== route.routeKey || audit.label !== route.label) {
      throw new Error(
        `Invalid production readiness report contract: visualQaCoverage.routeAudits.${auditIndex}.routeKey - must mirror the visual QA route metadata`,
      );
    }

    assertExpectedNumber(
      `visualQaCoverage.routeAudits.${auditIndex}.screenshotCount`,
      report.visualQaCoverage.themes.length * report.visualQaCoverage.viewports.length,
      audit.screenshotCount,
    );
    assertStringArrayEqual(
      `visualQaCoverage.routeAudits.${auditIndex}.workflowStages`,
      route.workflowStages,
      audit.workflowStages,
    );
    assertStringArrayEqual(
      `visualQaCoverage.routeAudits.${auditIndex}.reviewChecks`,
      report.visualQaCoverage.reviewChecks,
      audit.reviewChecks,
    );

    if (audit.reviewStatus !== "required-review") {
      throw new Error(
        `Invalid production readiness report contract: visualQaCoverage.routeAudits.${auditIndex}.reviewStatus - route audits must require named review`,
      );
    }
  });

  report.visualQaCoverage.stateInventory.forEach((state) => {
    report.visualQaCoverage.themes.forEach((theme) => {
      report.visualQaCoverage.viewports.forEach((viewport) => {
        const key = visualQaArtifactKey(state.stateId, theme, viewport.name);
        if (!artifactsByKey.has(key)) {
          throw new Error(
            `Invalid production readiness report contract: visualQaCoverage.artifacts - missing screenshot artifact for ${key}`,
          );
        }
      });
    });
  });
}

function visualQaArtifactKey(routeCode: string, theme: string, viewportName: string) {
  return `${routeCode}/${theme}/${viewportName}`;
}

function assertCompletionTracks(report: ProductionReadinessReport) {
  const expectedCodes = ["backend-code", "frontend-ui-ux", "frontend-code"];
  const actualCodes = report.completionTracks.map((track) => track.code);
  const missingCodes = expectedCodes.filter((code) => !actualCodes.includes(code));
  const duplicateCodes = actualCodes.filter((code, index) => actualCodes.indexOf(code) !== index);
  const assuranceActionCodes = new Set(report.assuranceActions.map((action) => action.code));

  if (missingCodes.length > 0) {
    throw new Error(
      `Invalid production readiness report contract: completionTracks - missing required tracks: ${missingCodes.join(", ")}`,
    );
  }

  if (duplicateCodes.length > 0) {
    throw new Error(
      `Invalid production readiness report contract: completionTracks - duplicate track codes: ${[...new Set(duplicateCodes)].join(", ")}`,
    );
  }

  report.completionTracks.forEach((track, trackIndex) => {
    if (track.completionCriteria.length === 0) {
      throw new Error(
        `Invalid production readiness report contract: completionTracks.${trackIndex}.completionCriteria - at least one completion criterion is required`,
      );
    }

    if (track.currentEvidence.length === 0) {
      throw new Error(
        `Invalid production readiness report contract: completionTracks.${trackIndex}.currentEvidence - at least one current evidence item is required`,
      );
    }

    if (track.nextActions.length === 0) {
      throw new Error(
        `Invalid production readiness report contract: completionTracks.${trackIndex}.nextActions - at least one next action is required`,
      );
    }

    track.assuranceActionCodes.forEach((code) => {
      if (!assuranceActionCodes.has(code)) {
        throw new Error(
          `Invalid production readiness report contract: completionTracks.${trackIndex}.assuranceActionCodes - unknown assurance action ${code}`,
        );
      }
    });
  });
}

function assertReleaseBlockerRegister(report: ProductionReadinessReport) {
  const trackCodes = new Set(report.completionTracks.map((track) => track.code));
  const trackLabelsByCode = new Map(report.completionTracks.map((track) => [track.code, track.label]));
  const trackActionCodesByTrack = new Map(report.completionTracks.map((track) => [track.code, new Set(track.assuranceActionCodes)]));
  const actionCodes = new Set(report.assuranceActions.map((action) => action.code));
  const actionStatusesByCode = new Map(report.assuranceActions.map((action) => [action.code, action.status]));
  const checklistByCode = new Map(report.releaseReviewChecklist.map((item) => [item.code, item]));
  const blockerIssues = new Set<string>();

  report.releaseBlockerRegister.forEach((blocker, blockerIndex) => {
    if (!trackCodes.has(blocker.trackCode)) {
      throw new Error(
        `Invalid production readiness report contract: releaseBlockerRegister.${blockerIndex}.trackCode - must reference a completion track`,
      );
    }

    if (trackLabelsByCode.get(blocker.trackCode) !== blocker.trackLabel) {
      throw new Error(
        `Invalid production readiness report contract: releaseBlockerRegister.${blockerIndex}.trackLabel - must mirror the completion track label`,
      );
    }

    if (!actionCodes.has(blocker.sourceActionCode)) {
      throw new Error(
        `Invalid production readiness report contract: releaseBlockerRegister.${blockerIndex}.sourceActionCode - must reference an assurance action`,
      );
    }

    if (!trackActionCodesByTrack.get(blocker.trackCode)?.has(blocker.sourceActionCode)) {
      throw new Error(
        `Invalid production readiness report contract: releaseBlockerRegister.${blockerIndex}.sourceActionCode - must be owned by the referenced completion track`,
      );
    }

    const actionStatus = actionStatusesByCode.get(blocker.sourceActionCode);
    if (actionStatus === "complete") {
      throw new Error(
        `Invalid production readiness report contract: releaseBlockerRegister.${blockerIndex}.sourceActionCode - completed actions must not remain release blockers`,
      );
    }

    const checklistItem = checklistByCode.get(blocker.releaseChecklistCode);
    if (!checklistItem) {
      throw new Error(
        `Invalid production readiness report contract: releaseBlockerRegister.${blockerIndex}.releaseChecklistCode - must reference a release checklist item`,
      );
    }

    if (
      checklistItem.assuranceActionCode !== blocker.sourceActionCode
      || checklistItem.evidenceArtifact !== blocker.evidenceArtifact
      || checklistItem.operationalGateCode !== blocker.operationalGateCode
    ) {
      throw new Error(
        `Invalid production readiness report contract: releaseBlockerRegister.${blockerIndex}.releaseChecklistCode - must mirror linked release checklist controls`,
      );
    }

    if (checklistItem.blocksRelease !== blocker.blocksRelease) {
      throw new Error(
        `Invalid production readiness report contract: releaseBlockerRegister.${blockerIndex}.blocksRelease - must mirror linked release checklist blocking state`,
      );
    }

    blockerIssues.add(blocker.blockingIssue);
  });

  const missingPacketBlockers = report.assurancePacket.releaseBlockers
    .filter((blocker) => !blockerIssues.has(blocker))
    .sort();
  const unexpectedRegisterBlockers = [...blockerIssues]
    .filter((blocker) => !report.assurancePacket.releaseBlockers.includes(blocker))
    .sort();

  if (missingPacketBlockers.length > 0 || unexpectedRegisterBlockers.length > 0) {
    throw new Error(
      `Invalid production readiness report contract: releaseBlockerRegister - must cover every assurance packet release blocker; missing ${missingPacketBlockers.join(", ") || "none"}, unexpected ${unexpectedRegisterBlockers.join(", ") || "none"}`,
    );
  }
}

function assertProductionScorecard(report: ProductionReadinessReport) {
  const requiredCodes = [
    "architecture-documentation",
    "backend-statutory-accounting-engine",
    "frontend-accountant-workbench",
    "security-auth-tenant-platform-guardrails",
  ];
  const actualCodes = report.productionScorecard.categories.map((category) => category.code);
  const missingCodes = requiredCodes.filter((code) => !actualCodes.includes(code));
  const duplicateCodes = actualCodes.filter((code, index) => actualCodes.indexOf(code) !== index);
  const completionTrackCodes = new Set(report.completionTracks.map((track) => track.code));
  const releaseBlockerCodes = new Set(report.releaseBlockerRegister.map((blocker) => blocker.code));
  const auditBaselineDate = new Date(`${report.productionScorecard.auditBaselineDate}T00:00:00Z`);

  if (
    Number.isNaN(auditBaselineDate.getTime())
    || auditBaselineDate.toISOString().slice(0, 10) !== report.productionScorecard.auditBaselineDate
  ) {
    throw new Error(
      "Invalid production readiness report contract: productionScorecard.auditBaselineDate - expected a real calendar date",
    );
  }

  const evidencePolicy = report.productionScorecard.evidencePolicy.toLowerCase();
  if (
    !evidencePolicy.includes("passed weighted controls")
    || !evidencePolicy.includes("exact live candidate report")
    || !evidencePolicy.includes("machine")
    || !evidencePolicy.includes("human/external")
    || !evidencePolicy.includes("candidate-bound artifact hashes")
  ) {
    throw new Error(
      "Invalid production readiness report contract: productionScorecard.evidencePolicy - must tie passed weighted controls and candidate-bound artifact hashes to the exact live candidate report",
    );
  }

  if (!report.assurancePacket.evidenceItems.includes("production-scorecard")) {
    throw new Error(
      "Invalid production readiness report contract: assurancePacket.evidenceItems - production-scorecard is required",
    );
  }

  if (!report.assurancePacket.evidenceItems.includes("production-readiness-report")) {
    throw new Error(
      "Invalid production readiness report contract: assurancePacket.evidenceItems - production-readiness-report is required",
    );
  }

  if (!report.assurancePacket.evidenceItems.includes("production-readiness-verification-report")) {
    throw new Error(
      "Invalid production readiness report contract: assurancePacket.evidenceItems - production-readiness-verification-report is required",
    );
  }

  if (missingCodes.length > 0) {
    throw new Error(
      `Invalid production readiness report contract: productionScorecard.categories - missing required categories: ${missingCodes.join(", ")}`,
    );
  }

  if (duplicateCodes.length > 0) {
    throw new Error(
      `Invalid production readiness report contract: productionScorecard.categories - duplicate category codes: ${[...new Set(duplicateCodes)].join(", ")}`,
    );
  }

  const currentTotal = report.productionScorecard.categories.reduce((total, category) => total + category.currentScore, 0);
  const targetTotal = report.productionScorecard.categories.reduce((total, category) => total + category.targetScore, 0);

  assertExpectedNumber("productionScorecard.currentScore", currentTotal, report.productionScorecard.currentScore);
  assertExpectedNumber("productionScorecard.targetScore", targetTotal, report.productionScorecard.targetScore);
  assertExpectedNumber("productionScorecard.targetScore", 1000, report.productionScorecard.targetScore);

  report.productionScorecard.categories.forEach((category, categoryIndex) => {
    if (category.currentScore > category.targetScore) {
      throw new Error(
        `Invalid production readiness report contract: productionScorecard.categories.${categoryIndex}.currentScore - cannot exceed target score`,
      );
    }

    const controlCodes = category.controls.map((control) => control.code);
    const duplicateControlCodes = controlCodes.filter((code, index) => controlCodes.indexOf(code) !== index);
    if (duplicateControlCodes.length > 0) {
      throw new Error(
        `Invalid production readiness report contract: productionScorecard.categories.${categoryIndex}.controls - duplicate control codes: ${[...new Set(duplicateControlCodes)].join(", ")}`,
      );
    }

    const weightedTarget = category.controls.reduce((total, control) => total + control.weight, 0);
    const weightedCurrent = category.controls
      .filter((control) => control.passed)
      .reduce((total, control) => total + control.weight, 0);
    assertExpectedNumber(
      `productionScorecard.categories.${categoryIndex}.targetScore`,
      weightedTarget,
      category.targetScore,
    );
    assertExpectedNumber(
      `productionScorecard.categories.${categoryIndex}.currentScore`,
      weightedCurrent,
      category.currentScore,
    );

    category.controls.forEach((control, controlIndex) => {
      const expectedStatus = control.passed ? "passed" : "open";
      if (control.status !== expectedStatus) {
        throw new Error(
          `Invalid production readiness report contract: productionScorecard.categories.${categoryIndex}.controls.${controlIndex}.status - expected ${expectedStatus}, received ${control.status}`,
        );
      }

      if (!control.passed && control.blockingAuditItemIds.length === 0) {
        throw new Error(
          `Invalid production readiness report contract: productionScorecard.categories.${categoryIndex}.controls.${controlIndex}.blockingAuditItemIds - open controls require at least one blocking audit item`,
        );
      }
    });

    if (category.currentEvidence.length === 0) {
      throw new Error(
        `Invalid production readiness report contract: productionScorecard.categories.${categoryIndex}.currentEvidence - at least one evidence item is required`,
      );
    }

    if (category.remainingGaps.length === 0) {
      throw new Error(
        `Invalid production readiness report contract: productionScorecard.categories.${categoryIndex}.remainingGaps - at least one remaining gap is required until the score reaches target`,
      );
    }

    category.completionTrackCodes.forEach((trackCode) => {
      if (!completionTrackCodes.has(trackCode)) {
        throw new Error(
          `Invalid production readiness report contract: productionScorecard.categories.${categoryIndex}.completionTrackCodes - unknown completion track ${trackCode}`,
        );
      }
    });

    category.releaseBlockerCodes.forEach((blockerCode) => {
      if (!releaseBlockerCodes.has(blockerCode)) {
        throw new Error(
          `Invalid production readiness report contract: productionScorecard.categories.${categoryIndex}.releaseBlockerCodes - unknown release blocker ${blockerCode}`,
        );
      }
    });
  });
}

function assertReleaseReviewChecklist(report: ProductionReadinessReport) {
  const assuranceActionCodes = new Set(report.assuranceActions.map((action) => action.code));
  const operationalGateCodes = new Set(report.operationalGates.map((gate) => gate.code));
  const checklistActionCodes = new Set<string>();

  report.releaseReviewChecklist.forEach((item, itemIndex) => {
    if (!assuranceActionCodes.has(item.assuranceActionCode)) {
      throw new Error(
        `Invalid production readiness report contract: releaseReviewChecklist.${itemIndex}.assuranceActionCode - must reference a known assurance action`,
      );
    }

    if (item.operationalGateCode.trim() && !operationalGateCodes.has(item.operationalGateCode)) {
      throw new Error(
        `Invalid production readiness report contract: releaseReviewChecklist.${itemIndex}.operationalGateCode - must reference a known operational gate`,
      );
    }

    checklistActionCodes.add(item.assuranceActionCode);
  });

  const missingActions = [...assuranceActionCodes]
    .filter((code) => !checklistActionCodes.has(code))
    .sort();

  if (missingActions.length > 0) {
    throw new Error(
      `Invalid production readiness report contract: releaseReviewChecklist - missing checklist items for assurance actions: ${missingActions.join(", ")}`,
    );
  }
}

function assertReleaseVerificationManifest(report: ProductionReadinessReport) {
  const checklistEvidenceArtifacts = new Set(report.releaseReviewChecklist.map((item) => item.evidenceArtifact));
  const validScopes = new Set(["default-ci", "environment-gated", "manual-release"]);
  const requiredDefaultCiManifestCodes = [
    "backend-golden-corpus",
    "frontend-workbench-contract",
    "frontend-production-build",
    "visual-smoke-light-dark",
    "production-readiness-report-verification",
    "production-stack-smoke",
    "backup-restore-drill",
    "postgres-migration-upgrade-gate",
    "ci-machine-evidence-pack",
  ];
  const requiredManualManifestCodes = [
    "release-artifact-pack",
    "qualified-accountant-final-signoff",
    "source-law-change-review",
    "external-ros-validation-evidence",
    "no-direct-cro-ros-submission-control",
    "manual-accountant-acceptance",
  ];
  const manifestCodes = new Set<string>();
  const manifestChecklistEvidenceArtifacts = new Set<string>();

  report.releaseVerificationManifest.forEach((item, itemIndex) => {
    if (manifestCodes.has(item.code)) {
      throw new Error(
        `Invalid production readiness report contract: releaseVerificationManifest.${itemIndex}.code - duplicate manifest code`,
      );
    }
    manifestCodes.add(item.code);

    if (!validScopes.has(item.ciScope)) {
      throw new Error(
        `Invalid production readiness report contract: releaseVerificationManifest.${itemIndex}.ciScope - must be default-ci, environment-gated or manual-release`,
      );
    }

    if (!checklistEvidenceArtifacts.has(item.releaseChecklistEvidenceArtifact)) {
      throw new Error(
        `Invalid production readiness report contract: releaseVerificationManifest.${itemIndex}.releaseChecklistEvidenceArtifact - must reference release checklist evidence`,
      );
    }
    manifestChecklistEvidenceArtifacts.add(item.releaseChecklistEvidenceArtifact);

    if (item.blocksRelease && !item.manualFallback.trim()) {
      throw new Error(
        `Invalid production readiness report contract: releaseVerificationManifest.${itemIndex}.manualFallback - blocking checks need a manual fallback`,
      );
    }
  });

  if (report.releaseVerificationManifest.length === 0) {
    throw new Error(
      "Invalid production readiness report contract: releaseVerificationManifest - at least one verification command is required",
    );
  }

  const missingDefaultCiManifestCodes = requiredDefaultCiManifestCodes
    .filter((code) => !manifestCodes.has(code))
    .sort();
  if (missingDefaultCiManifestCodes.length > 0) {
    throw new Error(
      `Invalid production readiness report contract: releaseVerificationManifest - missing default CI verification commands: ${missingDefaultCiManifestCodes.join(", ")}`,
    );
  }

  const missingManualManifestCodes = requiredManualManifestCodes
    .filter((code) => !manifestCodes.has(code))
    .sort();
  if (missingManualManifestCodes.length > 0) {
    throw new Error(
      `Invalid production readiness report contract: releaseVerificationManifest - missing manual release verification commands: ${missingManualManifestCodes.join(", ")}`,
    );
  }

  const ciMachineEvidencePack = report.releaseVerificationManifest.find(
    (item) => item.code === "ci-machine-evidence-pack",
  );
  if (
    !ciMachineEvidencePack ||
    ciMachineEvidencePack.ciScope !== "default-ci" ||
    !ciMachineEvidencePack.runsInDefaultCi ||
    !ciMachineEvidencePack.command.includes("verify-ci-machine-evidence-pack.ps1") ||
    ciMachineEvidencePack.evidenceArtifact !== "ci-machine-evidence-pack"
  ) {
    throw new Error(
      "Invalid production readiness report contract: releaseVerificationManifest.ci-machine-evidence-pack - default CI verifier and artifact are required",
    );
  }

  const migrationUpgradeGate = report.releaseVerificationManifest.find(
    (item) => item.code === "postgres-migration-upgrade-gate",
  );
  if (
    !migrationUpgradeGate ||
    migrationUpgradeGate.ciScope !== "default-ci" ||
    !migrationUpgradeGate.runsInDefaultCi ||
    !migrationUpgradeGate.command.includes("has-pending-model-changes") ||
    !migrationUpgradeGate.command.includes("MigrationUpgradePostgresTests") ||
    !migrationUpgradeGate.command.includes("verify-migration-upgrade-evidence.ps1") ||
    migrationUpgradeGate.evidenceArtifact !== "postgres-migration-upgrade-gate"
  ) {
    throw new Error(
      "Invalid production readiness report contract: releaseVerificationManifest.postgres-migration-upgrade-gate - drift, previous-release upgrade, rollback verifier and artifact are required",
    );
  }

  const missingBlockingChecklistEvidence = report.releaseReviewChecklist
    .filter((item) => item.blocksRelease)
    .map((item) => item.evidenceArtifact)
    .filter((artifact) => !manifestChecklistEvidenceArtifacts.has(artifact))
    .sort();

  if (missingBlockingChecklistEvidence.length > 0) {
    throw new Error(
      `Invalid production readiness report contract: releaseVerificationManifest - missing verification coverage for blocking checklist evidence: ${missingBlockingChecklistEvidence.join(", ")}`,
    );
  }
}

function assertHumanReleaseEvidence(report: ProductionReadinessReport) {
  const requiredCodes = [
    "visualQa",
    "sourceLawReview",
    "externalRosIxbrlValidation",
    "qualifiedAccountantAcceptance",
    "manualHandoffAcceptance",
    "monitoringProviderConfirmation",
  ];
  const requiredReviewerPickupFilesByCode: Record<string, string[]> = {
    visualQa: ["visual-qa-signoff-template.md", "visual-smoke-manifest.json", "visual-smoke-evidence-report.json", "accountant-workbench-evidence-report.json", "release-evidence-reviewer-blockers.md"],
    sourceLawReview: ["source-law-review-template.md", "production-readiness-report.json", "production-readiness-verification-report.json", "release-evidence-reviewer-blockers.md"],
    externalRosIxbrlValidation: ["external-ros-ixbrl-validation-template.md", "production-readiness-report.json", "release-evidence-reviewer-blockers.md"],
    qualifiedAccountantAcceptance: ["qualified-accountant-acceptance-template.md", "production-readiness-report.json", "accountant-workbench-evidence-report.json", "release-evidence-reviewer-blockers.md"],
    manualHandoffAcceptance: ["manual-handoff-acceptance-template.md", "production-readiness-report.json", "release-evidence-reviewer-blockers.md"],
    monitoringProviderConfirmation: ["monitoring-provider-confirmation-template.md", "monitoring-error-routing-report.json", "structured-log-report.json", "release-evidence-reviewer-blockers.md"],
  };
  const checklistCodes = new Set(report.releaseReviewChecklist.map((item) => item.code));
  const manifestCodes = new Set(report.releaseVerificationManifest.map((item) => item.code));
  const checklistByCode = new Map(report.releaseReviewChecklist.map((item) => [item.code, item]));
  const actualCodes = report.humanReleaseEvidence.map((item) => item.code);
  const missingCodes = requiredCodes.filter((code) => !actualCodes.includes(code));
  const duplicateCodes = actualCodes.filter((code, index) => actualCodes.indexOf(code) !== index);

  if (!report.assurancePacket.evidenceItems.includes("human-release-evidence")) {
    throw new Error(
      "Invalid production readiness report contract: assurancePacket.evidenceItems - human-release-evidence is required",
    );
  }

  if (missingCodes.length > 0) {
    throw new Error(
      `Invalid production readiness report contract: humanReleaseEvidence - missing required gates: ${missingCodes.join(", ")}`,
    );
  }

  if (duplicateCodes.length > 0) {
    throw new Error(
      `Invalid production readiness report contract: humanReleaseEvidence - duplicate gate codes: ${[...new Set(duplicateCodes)].join(", ")}`,
    );
  }

  report.humanReleaseEvidence.forEach((item, itemIndex) => {
    const checklistItem = checklistByCode.get(item.releaseChecklistCode);

    if (!checklistCodes.has(item.releaseChecklistCode)) {
      throw new Error(
        `Invalid production readiness report contract: humanReleaseEvidence.${itemIndex}.releaseChecklistCode - must reference a release checklist item`,
      );
    }

    if (!manifestCodes.has(item.releaseManifestCode)) {
      throw new Error(
        `Invalid production readiness report contract: humanReleaseEvidence.${itemIndex}.releaseManifestCode - must reference a release verification manifest item`,
      );
    }

    if (checklistItem && checklistItem.evidenceArtifact !== item.evidenceArtifact) {
      throw new Error(
        `Invalid production readiness report contract: humanReleaseEvidence.${itemIndex}.evidenceArtifact - must mirror release checklist evidence`,
      );
    }

    if (item.status !== "pending-human-evidence" && item.status !== "accepted") {
      throw new Error(
        `Invalid production readiness report contract: humanReleaseEvidence.${itemIndex}.status - must be pending-human-evidence or accepted`,
      );
    }

    if (item.blocksRelease && item.status === "accepted") {
      throw new Error(
        `Invalid production readiness report contract: humanReleaseEvidence.${itemIndex}.blocksRelease - accepted human evidence cannot still block release`,
      );
    }

    if (!item.blocksRelease && item.status !== "accepted") {
      throw new Error(
        `Invalid production readiness report contract: humanReleaseEvidence.${itemIndex}.status - non-blocking human evidence must be accepted`,
      );
    }

    if (item.requiredEvidence.length < 2) {
      throw new Error(
        `Invalid production readiness report contract: humanReleaseEvidence.${itemIndex}.requiredEvidence - at least two retained evidence references are required`,
      );
    }

    const requiredReviewerPickupFiles = requiredReviewerPickupFilesByCode[item.code] ?? [item.templateFile, "release-evidence-reviewer-blockers.md"];
    const missingReviewerPickupFile = requiredReviewerPickupFiles.find((fileName) => !item.reviewerPickupFiles.includes(fileName));
    if (missingReviewerPickupFile) {
      throw new Error(
        `Invalid production readiness report contract: humanReleaseEvidence.${itemIndex}.reviewerPickupFiles - must include expected pickup file ${missingReviewerPickupFile}`,
      );
    }
  });
}

function assertHumanReleaseEvidenceCloseout(report: ProductionReadinessReport) {
  const requiredSteps = [
    {
      code: "pick-up-reviewer-workspace",
      artifact: "release-evidence-reviewer-workspace",
      detailTerms: ["release-evidence-reviewer-index.md", "pending human blocker inventory"],
    },
    {
      code: "complete-human-evidence-templates",
      artifact: "Docs/release-evidence/*.md",
      detailTerms: ["retained Markdown templates", "named reviewers"],
    },
    {
      code: "run-release-evidence-verifier",
      artifact: "scripts/verify-release-evidence.ps1",
      detailTerms: ["release-evidence-report.json", "exact candidate"],
    },
    {
      code: "confirm-human-evidence-completion",
      artifact: "release-evidence-report.json",
      detailTerms: ["humanEvidenceCompletion", "zero blocking failures"],
    },
    {
      code: "verify-release-artifact-pack",
      artifact: "scripts/verify-release-artifact-pack.ps1",
      detailTerms: ["same commit SHA", "GitHub Actions run URL"],
    },
  ];
  const actualCodes = report.humanReleaseEvidenceCloseout.map((item) => item.code);
  const missingCodes = requiredSteps.map((step) => step.code).filter((code) => !actualCodes.includes(code));
  const duplicateCodes = actualCodes.filter((code, index) => actualCodes.indexOf(code) !== index);
  const templateCount = report.humanReleaseEvidence.length.toString();

  if (missingCodes.length > 0) {
    throw new Error(
      `Invalid production readiness report contract: humanReleaseEvidenceCloseout - missing required steps: ${missingCodes.join(", ")}`,
    );
  }

  if (duplicateCodes.length > 0) {
    throw new Error(
      `Invalid production readiness report contract: humanReleaseEvidenceCloseout - duplicate step codes: ${[...new Set(duplicateCodes)].join(", ")}`,
    );
  }

  report.humanReleaseEvidenceCloseout.forEach((step, stepIndex) => {
    const expected = requiredSteps[stepIndex];

    if (!expected) {
      throw new Error(
        `Invalid production readiness report contract: humanReleaseEvidenceCloseout.${stepIndex}.code - unexpected closeout step`,
      );
    }

    if (step.code !== expected.code) {
      throw new Error(
        `Invalid production readiness report contract: humanReleaseEvidenceCloseout.${stepIndex}.code - steps must remain in release-operator sequence`,
      );
    }

    if (step.sequence !== stepIndex + 1) {
      throw new Error(
        `Invalid production readiness report contract: humanReleaseEvidenceCloseout.${stepIndex}.sequence - must match release-operator order`,
      );
    }

    if (step.artifact !== expected.artifact) {
      throw new Error(
        `Invalid production readiness report contract: humanReleaseEvidenceCloseout.${stepIndex}.artifact - must reference ${expected.artifact}`,
      );
    }

    if (step.code === "complete-human-evidence-templates" && !step.detail.includes(templateCount)) {
      throw new Error(
        `Invalid production readiness report contract: humanReleaseEvidenceCloseout.${stepIndex}.detail - must include the human evidence template count`,
      );
    }

    if (step.code === "confirm-human-evidence-completion" && !step.detail.includes(templateCount)) {
      throw new Error(
        `Invalid production readiness report contract: humanReleaseEvidenceCloseout.${stepIndex}.detail - must include the humanEvidenceCompletion row count`,
      );
    }

    expected.detailTerms.forEach((term) => {
      if (!step.detail.includes(term)) {
        throw new Error(
          `Invalid production readiness report contract: humanReleaseEvidenceCloseout.${stepIndex}.detail - must mention ${term}`,
        );
      }
    });
  });
}

function assertAssuranceActionsRiskOrder(actions: ProductionReadinessAssuranceAction[]) {
  actions.forEach((action, actionIndex) => {
    if (!action.evidenceStage.trim()) {
      throw new Error(
        `Invalid production readiness report contract: assuranceActions.${actionIndex}.evidenceStage - evidence stage is required`,
      );
    }

    if (actionIndex === 0) return;

    const previous = actions[actionIndex - 1];
    const outOfRiskOrder =
      action.riskRank < previous.riskRank ||
      (action.riskRank === previous.riskRank && action.code.localeCompare(previous.code, "en-IE") < 0);

    if (outOfRiskOrder) {
      throw new Error(
        `Invalid production readiness report contract: assuranceActions.${actionIndex}.riskRank - actions must be sorted by riskRank then code`,
      );
    }
  });
}

function assertStringArrayEqual(path: string, expected: string[], received: string[]) {
  if (expected.length !== received.length || expected.some((value, index) => value !== received[index])) {
    throw new Error(
      `Invalid production readiness report contract: ${path} - expected ${expected.join(", ")}, received ${received.join(", ")}`,
    );
  }
}

function assertExpectedNumber(path: string, expected: number, received: number) {
  if (expected !== received) {
    throw new Error(
      `Invalid production readiness report contract: ${path} - expected ${expected}, received ${received}`,
    );
  }
}
