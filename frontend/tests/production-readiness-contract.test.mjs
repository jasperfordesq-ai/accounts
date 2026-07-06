import { test } from "node:test";
import assert from "node:assert/strict";
import { parseProductionReadinessReport } from "../src/lib/api.ts";
import {
  ACCOUNTANT_WORKFLOW_STAGES,
  expectedVisualSmokeArtifacts,
  expectedVisualSmokeRouteAudits,
  expectedVisualSmokeScreenshotCount,
  visualSmokeLayoutChecks,
  visualSmokeReviewProtocol,
  visualSmokeReviewChecks,
  visualSmokeRoutes,
  visualSmokeThemes,
  visualSmokeViewports,
} from "../scripts/visual-smoke-plan.mjs";

test("parseProductionReadinessReport accepts the golden corpus evidence-pack contract", () => {
  const parsed = parseProductionReadinessReport(sampleReport());

  assert.equal(parsed.goldenFilingCorpus[0].fixture.legalName, "Example Micro Limited");
  assert.equal(parsed.goldenFilingCorpus[0].fixture.expectedRegime, "Micro");
  assert.equal(parsed.goldenFilingCorpus[0].fixture.auditExempt, true);
  assert.equal(parsed.goldenFilingCorpus[0].fixture.manualProfessionalReviewRequired, false);
  assert.equal(parsed.goldenFilingCorpus[0].evidenceVerifiers[0].name, "AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl");
  assert.equal(parsed.goldenFilingCorpus[0].evidenceVerifiers[0].ciScope, "default-ci");
  assert.equal(parsed.goldenFilingCorpus[0].evidenceVerifiers[0].runsInDefaultCi, true);
  assert.equal(parsed.goldenFilingCorpus[0].evidencePack.outputArtifacts[0], "accounts PDF text");
  assert.equal(parsed.goldenFilingCorpus[0].evidencePack.decisionGates[0], "named qualified-accountant review");
  assert.equal(parsed.goldenFilingCorpus[0].evidencePack.expectedValueChecks[0], "well-formed iXBRL");
  assert.equal(parsed.goldenFilingCorpus[0].evidencePack.expectedOutputs.pdfTextMarkers[0], "Example Micro Limited");
  assert.equal(parsed.goldenFilingCorpus[0].evidencePack.expectedOutputs.ixbrlRequiredTags[0], "core:EntityCurrentLegalOrRegisteredName");
  assert.equal(parsed.goldenFilingCorpus[0].evidencePack.expectedOutputs.expectedCorporationTax, 718.75);
  assert.equal(parsed.goldenFilingCorpus[0].evidencePack.expectedOutputs.signOffPacketState, "review-required");
  assert.equal(parsed.goldenFilingCorpus[0].evidencePack.expectedProofPoints[0].area, "pdf-text");
  assert.equal(parsed.goldenFilingCorpus[0].evidencePack.expectedProofPoints[0].required, true);
  assert.equal(parsed.goldenFilingCorpus[0].evidencePack.sourceReferences[0].sourceId, "frc-frs-105");
  assert.equal(parsed.goldenEvidenceLedger[0].scenarioCode, "micro-ltd");
  assert.equal(parsed.goldenEvidenceLedger[0].fixtureLegalName, "Example Micro Limited");
  assert.equal(parsed.goldenEvidenceLedger[0].expectedCorporationTax, 718.75);
  assert.deepEqual(parsed.goldenEvidenceLedger[0].automatedVerifierNames, parsed.goldenFilingCorpus[0].evidenceTestNames);
  assert.ok(parsed.goldenEvidenceLedger[0].blocksRelease);
  const dacScenario = parsed.goldenFilingCorpus.find((scenario) => scenario.code === "dac-small");
  assert.equal(dacScenario?.fixture.legalName, "Atlantic Manufacturing DAC");
  assert.equal(dacScenario?.fixture.companyType, "DesignatedActivityCompany");
  assert.equal(dacScenario?.evidenceVerifiers[0].name, "FilingGoldenCorpusScenarioTests.GoldenCorpus_DacSmall_EmitsAccountsIxbrlAndSourceBackedReadiness");
  assert.equal(dacScenario?.evidencePack.expectedOutputs.ixbrlRequiredTags[0], "bus:EntityCurrentLegalOrRegisteredName");
  assert.equal(parsed.sourceLawSnapshot.sourceCount, 2);
  assert.equal(parsed.sourceLawSnapshot.contentHash, "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
  assert.equal(parsed.sourceLawTraceability[0].sourceId, "frc-frs-105");
  assert.equal(parsed.sourceLawTraceability[0].inSnapshot, true);
  assert.equal(parsed.sourceLawTraceability[0].usedBy[0], "golden-corpus:micro-ltd");
  assert.equal(parsed.sourceLawTraceability[0].releaseGateCodes[0], "qualified-accountant-review");
  assert.equal(parsed.sourceLawMaintenanceProtocol.protocolVersion, "source-law-maintenance-v1");
  assert.equal(parsed.sourceLawMaintenanceProtocol.ownerRole, "Qualified accountant and engineering");
  assert.equal(parsed.sourceLawMaintenanceProtocol.signOffGate, "source-law-change-review");
  assert.deepEqual(parsed.sourceLawMaintenanceProtocol.monitoredSourceIds, ["frc-frs-105", "frc-frs-102"]);
  assert.match(parsed.sourceLawMaintenanceProtocol.failurePolicy, /Block release/);
  assert.match(parsed.sourceLawMaintenanceProtocol.acceptanceCriteria[0], /CRO/);
  assert.ok(parsed.sourceLawMaintenanceProtocol.requiredEvidence.includes("source-law-change-review-note"));
  assert.equal(parsed.statutoryRulesCoverage[0].code, "size-classification-thresholds");
  assert.equal(parsed.statutoryRulesCoverage[0].automatedVerifierNames[0], "AccountsWorkflowTests.SizeClassification_FirstYearMicro_AllowsMicroAndAuditExemption");
  assert.equal(parsed.statutoryRulesCoverage[0].edgeCases[0], "two-of-three threshold rule");
  assert.equal(parsed.monitoringControls[0].code, "error-tracking");
  assert.equal(parsed.monitoringControls[0].productionSafetyGate, "Monitoring:ErrorTrackingDsn");
  assert.equal(parsed.monitoringControls[0].alertRoute, "Primary on-call accountant and platform owner");
  assert.equal(parsed.monitoringControls[0].failurePolicy, "Block release if error events cannot be routed to the on-call owner.");
  assert.equal(parsed.dependencyPolicyControls[0].code, "frontend-npm-audit");
  assert.equal(parsed.dependencyPolicyControls[0].failurePolicy, "Fail the release for moderate, high or critical npm advisories.");
  assert.equal(parsed.deploymentSafetyControls[0].code, "controlled-production-migrations");
  assert.match(parsed.deploymentSafetyControls[1].failurePolicy, /demo/i);
  assert.equal(parsed.accountantAcceptanceCriteria[0].scenarioCode, "micro-ltd");
  assert.equal(parsed.accountantAcceptanceCriteria[0].required, true);
  assert.match(parsed.accountantAcceptanceCriteria[0].requiredSignOffGate, /qualified accountant/i);
  assert.equal(parsed.accountantAcceptanceCriteria[0].evidenceVerifiers[0].name, parsed.goldenFilingCorpus[0].evidenceVerifiers[0].name);
  assert.equal(parsed.accountantAcceptanceCriteria[0].evidenceVerifiers[0].command, parsed.goldenFilingCorpus[0].evidenceVerifiers[0].command);
  assert.equal(parsed.accountantAcceptanceSummary.scenarioCount, 2);
  assert.equal(parsed.accountantAcceptanceSummary.professionalSignOffRequiredCount, 2);
  assert.equal(parsed.accountantAcceptanceSummary.automatedVerifierCount, 2);
  assert.equal(parsed.accountantAcceptanceSummary.manualHandoffScenarioCount, 0);
  assert.deepEqual(parsed.accountantAcceptanceSummary.releaseBlockingScenarioCodes, ["dac-small", "micro-ltd"]);
  assert.deepEqual(parsed.accountantAcceptanceSummary.requiredSignOffGates, [
    "Named qualified accountant must approve the DAC generated pack before real filing use.",
    "Named qualified accountant must approve the generated pack before real filing use.",
  ]);
  assert.equal(parsed.accountantAcceptanceSummary.status, "qualified-accountant-review-required");
  assert.equal(parsed.accountantWorkflowWalkthroughProtocol.protocolVersion, "accountant-workflow-walkthrough-v1");
  assert.equal(parsed.accountantWorkflowWalkthroughProtocol.signOffGate, "golden-corpus-accountant-acceptance");
  assert.deepEqual(parsed.accountantWorkflowWalkthroughProtocol.seededScenarioCodes, ["dac-small", "micro-ltd"]);
  assert.match(parsed.accountantWorkflowWalkthroughProtocol.routeSequence[0], /Dashboard/);
  assert.match(parsed.accountantWorkflowWalkthroughProtocol.acceptanceCriteria.at(-1), /outputs, gates, wording and evidence/);
  assert.ok(parsed.accountantWorkflowWalkthroughProtocol.requiredEvidence.includes("seeded golden corpus walkthrough note"));
  assert.equal(parsed.accountantJourneyAcceptanceChecklist.length, 5);
  assert.deepEqual(
    parsed.accountantJourneyAcceptanceChecklist.map((item) => item.routeCode).sort(),
    ["company-detail", "dashboard", "filing-review", "period-workspace", "production-readiness"],
  );
  assert.equal(parsed.accountantJourneyAcceptanceChecklist[0].signOffGate, "golden-corpus-accountant-acceptance");
  assert.deepEqual(parsed.accountantJourneyAcceptanceChecklist[0].seededScenarioCodes, ["dac-small", "micro-ltd"]);
  assert.equal(parsed.accountantJourneyAcceptanceChecklist[0].visualArtifactNames.length, 4);
  assert.match(parsed.accountantJourneyAcceptanceChecklist[2].acceptanceCriteria[0], /Period workspace/);
  assert.equal(parsed.assurancePacket.packetVersion, "production-assurance-packet-v1");
  assert.equal(parsed.assurancePacket.sourceLawSnapshotHash, "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
  assert.equal(parsed.assurancePacket.goldenCorpusCovered, 2);
  assert.ok(parsed.assurancePacket.evidenceItems.includes("source-law-traceability-index"));
  assert.ok(parsed.assurancePacket.evidenceItems.includes("release-review-checklist"));
  assert.ok(parsed.assurancePacket.evidenceItems.includes("golden-evidence-ledger"));
  assert.ok(parsed.assurancePacket.evidenceItems.includes("golden-verifier-manifest"));
  assert.ok(parsed.assurancePacket.evidenceItems.includes("release-blocker-register"));
  assert.equal(parsed.assurancePacket.releaseBlockers[0], "Qualified accountant sign-off required");
  assert.equal(parsed.assuranceActions[0].riskRank, 0);
  assert.equal(parsed.assuranceActions[0].evidenceStage, "accountant-review-gate");
  assert.equal(parsed.completionTracks.length, 3);
  assert.equal(parsed.completionTracks[0].code, "backend-code");
  assert.equal(parsed.completionTracks[0].label, "Backend code");
  assert.deepEqual(parsed.completionTracks[0].assuranceActionCodes, [
    "qualified-accountant-signoff",
    "source-law-change-review",
    "external-ros-validation",
    "accountant-acceptance-walkthrough",
  ]);
  assert.match(parsed.completionTracks[1].completionCriteria[0], /accountant workflow rail/i);
  assert.match(parsed.completionTracks[2].currentEvidence[0], /API client invariants/i);
  assert.equal(parsed.releaseBlockerRegister[0].code, "backend-code:qualified-accountant-signoff");
  assert.equal(parsed.releaseBlockerRegister[0].blockingIssue, "Qualified accountant sign-off required");
  assert.equal(parsed.releaseBlockerRegister[0].releaseChecklistCode, "accountant-final-signoff");
  assert.equal(parsed.releaseBlockerRegister[0].evidenceArtifact, "named-accountant-approval-record");
  assert.equal(parsed.releaseReviewChecklist[0].code, "accountant-final-signoff");
  assert.equal(parsed.releaseReviewChecklist[0].assuranceActionCode, "qualified-accountant-signoff");
  assert.equal(parsed.releaseReviewChecklist[0].evidenceArtifact, "named-accountant-approval-record");
  assert.equal(parsed.releaseVerificationManifest[0].code, "backend-golden-corpus");
  assert.equal(parsed.releaseVerificationManifest[0].command, "dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art");
  assert.equal(parsed.releaseVerificationManifest[0].releaseChecklistEvidenceArtifact, "named-accountant-approval-record");
  assert.equal(parsed.releaseVerificationManifest[1].ciScope, "environment-gated");
  assert.equal(parsed.releaseVerificationManifest[1].runsInDefaultCi, false);
  assert.match(parsed.releaseVerificationManifest[1].manualFallback, /ACCOUNTS_POSTGRES_TEST_CONNECTION/);
  const visualSmokeVerification = parsed.releaseVerificationManifest.find((item) => item.code === "visual-smoke-light-dark");
  assert.match(visualSmokeVerification?.command ?? "", /verify-visual-smoke-artifacts/);
  assert.equal(visualSmokeVerification?.evidenceArtifact, "artifacts/visual-smoke");
  assert.equal(parsed.auditEvidenceTimeline[0].code, "data-change-capture");
  assert.equal(parsed.auditEvidenceTimeline[0].capturedWhen, "At every authenticated write before regenerated outputs can be reviewed.");
  assert.equal(parsed.auditEvidenceTimeline[1].blockingGateCodes[0], "generated-output-review");
  assert.equal(parsed.visualQaCoverage.expectedScreenshotCount, expectedVisualSmokeScreenshotCount());
  assert.equal(parsed.visualQaCoverage.manifestFileName, "visual-smoke-manifest.json");
  assert.equal(parsed.visualQaCoverage.artifacts.length, expectedVisualSmokeArtifacts().length);
  assert.deepEqual(parsed.visualQaCoverage.reviewChecks, visualSmokeReviewChecks);
  assert.equal(parsed.visualQaCoverage.reviewProtocol.protocolVersion, "visual-review-v1");
  assert.equal(parsed.visualQaCoverage.reviewProtocol.reviewerRole, "Design reviewer");
  assert.equal(parsed.visualQaCoverage.reviewProtocol.signOffGate, "visual-qa-screenshot-review");
  assert.match(parsed.visualQaCoverage.reviewProtocol.failurePolicy, /Block release/);
  assert.match(parsed.visualQaCoverage.reviewProtocol.acceptanceCriteria[0], /light desktop/);
  assert.deepEqual(parsed.visualQaCoverage.reviewProtocol.requiredEvidence, [
    "visual-smoke-manifest.json",
    "24 visual smoke screenshots",
    "screenshot SHA-256 checksums",
    "route audit summary",
    "named visual QA reviewer sign-off",
  ]);
  assert.deepEqual(parsed.visualQaCoverage.routeAudits, expectedVisualSmokeRouteAudits().map((audit) => ({
    routeCode: audit.routeName,
    routeKey: audit.routeKey,
    label: audit.label,
    workflowStages: audit.workflowStages,
    screenshotCount: audit.screenshotCount,
    reviewStatus: audit.reviewStatus,
    reviewChecks: audit.reviewChecks,
  })));
  assert.deepEqual(parsed.visualQaCoverage.themes, visualSmokeThemes);
  assert.deepEqual(parsed.visualQaCoverage.viewports, visualSmokeViewports);
  assert.deepEqual(parsed.visualQaCoverage.routes.find((route) => route.code === "period-workspace")?.workflowStages, ACCOUNTANT_WORKFLOW_STAGES);
  assert.deepEqual(
    parsed.visualQaCoverage.routes.map(({ code, routeKey, label, description, requiredText, workflowStages, openFilingTab }) => ({
      code,
      routeKey,
      label,
      description,
      requiredText,
      workflowStages,
      openFilingTab,
    })),
    visualSmokeRoutes.map(({ name, routeKey, label, description, expectedText, workflowStages, openFilingTab }) => ({
      code: name,
      routeKey,
      label,
      description,
      requiredText: expectedText,
      workflowStages,
      openFilingTab,
    })),
  );
  assert.deepEqual(parsed.visualQaCoverage.layoutChecks, visualSmokeLayoutChecks);
  assert.equal(parsed.visualQaCoverage.artifacts[0].artifactPath, "artifacts/visual-smoke/dashboard-light-desktop.png");
  assert.equal(parsed.visualQaCoverage.artifacts[0].routeKey, "dashboard");
  assert.equal(parsed.visualQaCoverage.artifacts[0].requiredText, "Production Readiness");
  assert.deepEqual(parsed.visualQaCoverage.artifacts[0].layoutChecks, ["browser-console-errors", "page-horizontal-overflow", "visible-text-overlap"]);
});

test("parseProductionReadinessReport rejects missing golden corpus evidence packs", () => {
  const payload = sampleReport();
  delete payload.goldenFilingCorpus[0].evidencePack;

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: goldenFilingCorpus\.0\.evidencePack/,
  );
});

test("parseProductionReadinessReport rejects missing golden evidence ledger", () => {
  const payload = sampleReport();
  delete payload.goldenEvidenceLedger;

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: goldenEvidenceLedger/,
  );
});

test("parseProductionReadinessReport rejects golden evidence ledger drift", () => {
  const payload = sampleReport();
  payload.goldenEvidenceLedger[0] = {
    ...payload.goldenEvidenceLedger[0],
    outputArtifacts: ["spreadsheet export only"],
  };

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: goldenEvidenceLedger\.0\.outputArtifacts - must mirror golden scenario evidence pack/,
  );
});

test("parseProductionReadinessReport rejects golden corpus without matching accountant acceptance criteria", () => {
  const payload = sampleReport();
  payload.goldenFilingCorpus.push({
    ...payload.goldenFilingCorpus[0],
    code: "small-abridged-ltd",
    label: "Small abridged LTD",
  });
  payload.assurancePacket.goldenCorpusTotal = 3;
  payload.assurancePacket.goldenCorpusCovered = 3;

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: accountantAcceptanceCriteria - missing acceptance criteria for golden scenarios: small-abridged-ltd/,
  );
});

test("parseProductionReadinessReport rejects accountant acceptance verifiers that do not match the golden scenario", () => {
  const payload = sampleReport();
  payload.accountantAcceptanceCriteria[0].evidenceVerifiers[0].name =
    "AccountsWorkflowTests.NotTheGoldenScenarioVerifier";

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: accountantAcceptanceCriteria\.0\.evidenceVerifiers - must match the golden scenario verifier manifest/,
  );
});

test("parseProductionReadinessReport rejects inconsistent production assurance counts", () => {
  const payload = sampleReport();
  payload.sourceLawSnapshot.sourceCount = 3;

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: sourceLawSnapshot\.sourceCount - expected 2, received 3/,
  );

  const corpusPayload = sampleReport();
  corpusPayload.assurancePacket.goldenCorpusTotal = 3;

  assert.throws(
    () => parseProductionReadinessReport(corpusPayload),
    /Invalid production readiness report contract: assurancePacket\.goldenCorpusTotal - expected 2, received 3/,
  );

  const visualPayload = sampleReport();
  visualPayload.visualQaCoverage.expectedScreenshotCount = 7;

  assert.throws(
    () => parseProductionReadinessReport(visualPayload),
    /Invalid production readiness report contract: visualQaCoverage\.expectedScreenshotCount - expected 24, received 7/,
  );

  const visualAssurancePayload = sampleReport();
  visualAssurancePayload.assurancePacket.visualQaExpectedScreenshots = 99;

  assert.throws(
    () => parseProductionReadinessReport(visualAssurancePayload),
    /Invalid production readiness report contract: assurancePacket\.visualQaExpectedScreenshots - expected 24, received 99/,
  );
});

test("parseProductionReadinessReport rejects stale visual route audit counts", () => {
  const payload = sampleReport();
  payload.visualQaCoverage.routeAudits[0].screenshotCount = 3;

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: visualQaCoverage\.routeAudits\.0\.screenshotCount - expected 4, received 3/,
  );
});

test("parseProductionReadinessReport rejects visual review protocols without a release checklist sign-off gate", () => {
  const payload = sampleReport();
  payload.visualQaCoverage.reviewProtocol.signOffGate = "missing-visual-review-gate";

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: visualQaCoverage\.reviewProtocol\.signOffGate - must reference a release checklist item/,
  );
});

test("parseProductionReadinessReport rejects visual review protocols without checksum evidence", () => {
  const payload = sampleReport();
  payload.visualQaCoverage.reviewProtocol.requiredEvidence =
    payload.visualQaCoverage.reviewProtocol.requiredEvidence.filter((item) => item !== "screenshot SHA-256 checksums");

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: visualQaCoverage\.reviewProtocol\.requiredEvidence - must include screenshot SHA-256 checksums/,
  );
});

test("parseProductionReadinessReport rejects source-law maintenance protocols without a release checklist sign-off gate", () => {
  const payload = sampleReport();
  payload.sourceLawMaintenanceProtocol.signOffGate = "missing-source-law-review-gate";

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: sourceLawMaintenanceProtocol\.signOffGate - must reference a release checklist item/,
  );
});

test("parseProductionReadinessReport rejects accountant walkthrough protocols without a release checklist sign-off gate", () => {
  const payload = sampleReport();
  payload.accountantWorkflowWalkthroughProtocol.signOffGate = "missing-accountant-walkthrough-gate";

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: accountantWorkflowWalkthroughProtocol\.signOffGate - must reference a release checklist item/,
  );
});

test("parseProductionReadinessReport rejects accountant walkthrough protocols that drift from the golden corpus", () => {
  const payload = sampleReport();
  payload.accountantWorkflowWalkthroughProtocol.seededScenarioCodes = ["micro-ltd"];

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: accountantWorkflowWalkthroughProtocol\.seededScenarioCodes - expected dac-small, micro-ltd, received micro-ltd/,
  );
});

test("parseProductionReadinessReport rejects accountant walkthrough protocols without assurance packet evidence", () => {
  const payload = sampleReport();
  payload.assurancePacket.evidenceItems = payload.assurancePacket.evidenceItems.filter(
    (item) => item !== "accountant-workflow-walkthrough-protocol",
  );

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: assurancePacket\.evidenceItems - accountant-workflow-walkthrough-protocol is required/,
  );
});

test("parseProductionReadinessReport rejects accountant journey acceptance route drift", () => {
  const payload = sampleReport();
  payload.accountantJourneyAcceptanceChecklist = payload.accountantJourneyAcceptanceChecklist.filter(
    (item) => item.routeCode !== "filing-review",
  );

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: accountantJourneyAcceptanceChecklist - missing route acceptance entries: filing-review/,
  );
});

test("parseProductionReadinessReport rejects accountant journey acceptance without screenshot evidence", () => {
  const payload = sampleReport();
  payload.accountantJourneyAcceptanceChecklist[0].visualArtifactNames = ["dashboard-light-desktop.png"];

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: accountantJourneyAcceptanceChecklist\.0\.visualArtifactNames - must mirror visual smoke artifacts for dashboard/,
  );
});

test("parseProductionReadinessReport rejects accountant journey acceptance without assurance packet evidence", () => {
  const payload = sampleReport();
  payload.assurancePacket.evidenceItems = payload.assurancePacket.evidenceItems.filter(
    (item) => item !== "accountant-journey-acceptance-checklist",
  );

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: assurancePacket\.evidenceItems - accountant-journey-acceptance-checklist is required/,
  );
});

test("parseProductionReadinessReport rejects inconsistent accountant acceptance summaries", () => {
  const payload = sampleReport();
  payload.accountantAcceptanceSummary.scenarioCount = 3;

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: accountantAcceptanceSummary\.scenarioCount - expected 2, received 3/,
  );
});

test("parseProductionReadinessReport rejects incomplete source-law traceability", () => {
  const missingUsagePayload = sampleReport();
  missingUsagePayload.sourceLawTraceability[0].usedBy = [];

  assert.throws(
    () => parseProductionReadinessReport(missingUsagePayload),
    /Invalid production readiness report contract: sourceLawTraceability\.0\.usedBy - every pinned source must have at least one usage/,
  );

  const missingEvidencePayload = sampleReport();
  missingEvidencePayload.assurancePacket.evidenceItems =
    missingEvidencePayload.assurancePacket.evidenceItems.filter((item) => item !== "source-law-traceability-index");

  assert.throws(
    () => parseProductionReadinessReport(missingEvidencePayload),
    /Invalid production readiness report contract: assurancePacket\.evidenceItems - source-law-traceability-index is required/,
  );

  const missingReleaseGatePayload = sampleReport();
  missingReleaseGatePayload.sourceLawTraceability[0].releaseGateCodes = [];

  assert.throws(
    () => parseProductionReadinessReport(missingReleaseGatePayload),
    /Invalid production readiness report contract: sourceLawTraceability\.0\.releaseGateCodes - every pinned source must link to at least one release gate/,
  );
});

test("parseProductionReadinessReport rejects proof points whose verifier is not listed on the scenario", () => {
  const payload = sampleReport();
  payload.goldenFilingCorpus[0].evidencePack.expectedProofPoints[0].automatedVerifier =
    "AccountsWorkflowTests.NotActuallyPartOfThisScenario";

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: goldenFilingCorpus\.0\.evidencePack\.expectedProofPoints\.0\.automatedVerifier - verifier must be listed in evidenceTestNames/,
  );
});

test("parseProductionReadinessReport rejects assurance actions that are not risk ordered", () => {
  const payload = sampleReport();
  payload.assuranceActions = [
    {
      ...payload.assuranceActions[0],
      code: "visual-regression",
      riskRank: 30,
      evidenceStage: "visual-qa-evidence",
    },
    {
      ...payload.assuranceActions[0],
      code: "external-validation",
      riskRank: 5,
      evidenceStage: "external-validation-gate",
    },
  ];

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: assuranceActions\.1\.riskRank - actions must be sorted by riskRank then code/,
  );
});

test("parseProductionReadinessReport rejects completion tracks with unknown assurance actions", () => {
  const payload = sampleReport();
  payload.completionTracks[0].assuranceActionCodes.push("unknown-action");

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: completionTracks\.0\.assuranceActionCodes - unknown assurance action unknown-action/,
  );
});

test("parseProductionReadinessReport rejects a missing release blocker register", () => {
  const payload = sampleReport();
  delete payload.releaseBlockerRegister;

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: releaseBlockerRegister/,
  );
});

test("parseProductionReadinessReport rejects release blockers that drift from tracks and actions", () => {
  const payload = sampleReport();
  payload.releaseBlockerRegister[0].trackCode = "unknown-track";

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: releaseBlockerRegister\.0\.trackCode - must reference a completion track/,
  );

  const actionPayload = sampleReport();
  actionPayload.releaseBlockerRegister[0].sourceActionCode = "unknown-action";

  assert.throws(
    () => parseProductionReadinessReport(actionPayload),
    /Invalid production readiness report contract: releaseBlockerRegister\.0\.sourceActionCode - must reference an assurance action/,
  );

  const blockerPayload = sampleReport();
  blockerPayload.releaseBlockerRegister = blockerPayload.releaseBlockerRegister.filter(
    (blocker) => blocker.blockingIssue !== "Qualified accountant sign-off required",
  );

  assert.throws(
    () => parseProductionReadinessReport(blockerPayload),
    /Invalid production readiness report contract: releaseBlockerRegister - must cover every assurance packet release blocker/,
  );
});

test("parseProductionReadinessReport rejects release checklist items for unknown assurance actions", () => {
  const payload = sampleReport();
  payload.releaseReviewChecklist[0].assuranceActionCode = "missing-assurance-action";

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: releaseReviewChecklist\.0\.assuranceActionCode - must reference a known assurance action/,
  );
});

test("parseProductionReadinessReport rejects missing audit evidence timeline", () => {
  const payload = sampleReport();
  delete payload.auditEvidenceTimeline;

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: auditEvidenceTimeline/,
  );
});

test("parseProductionReadinessReport rejects missing release verification manifest", () => {
  const payload = sampleReport();
  delete payload.releaseVerificationManifest;

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: releaseVerificationManifest/,
  );
});

test("parseProductionReadinessReport rejects release verification manifest that misses blocking checklist evidence", () => {
  const payload = sampleReport();
  payload.releaseVerificationManifest = payload.releaseVerificationManifest.filter(
    (item) => item.releaseChecklistEvidenceArtifact !== "named-accountant-approval-record",
  );

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: releaseVerificationManifest - missing verification coverage for blocking checklist evidence: named-accountant-approval-record/,
  );
});

function sampleReport() {
  return {
    generatedAt: "2026-07-04T12:00:00Z",
    overallStatus: "review-required",
    companiesInDatabase: 1,
    periodsInDatabase: 1,
    sourceLawSnapshot: {
      snapshotDate: "2026-07-03",
      snapshotVersion: "irish-statutory-accounts-sources-2026-07-03",
      contentHash: "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
      sourceCount: 2,
      sources: [
        source("frc-frs-105", "FRC FRS 105 current edition and amendments"),
        source("frc-frs-102", "FRC FRS 102 current edition and amendments"),
      ],
    },
    sourceLawTraceability: [
      {
        ...source("frc-frs-105", "FRC FRS 105 current edition and amendments"),
        inSnapshot: true,
        usedBy: [
          "golden-corpus:micro-ltd",
          "golden-corpus:dac-small",
          "statutory-rule-matrix:ltd-micro",
          "statutory-rules-coverage:size-classification-thresholds",
          "accountant-acceptance:micro-ltd",
          "accountant-acceptance:dac-small",
        ],
        releaseGateCodes: ["qualified-accountant-review"],
      },
      {
        ...source("frc-frs-102", "FRC FRS 102 current edition and amendments"),
        inSnapshot: true,
        usedBy: [
          "golden-corpus:dac-small",
          "accountant-acceptance:dac-small",
        ],
        releaseGateCodes: ["qualified-accountant-review"],
      },
    ],
    sourceLawMaintenanceProtocol: {
      protocolVersion: "source-law-maintenance-v1",
      ownerRole: "Qualified accountant and engineering",
      status: "required-review",
      reviewCadence: "Before every production release and at least monthly while source-backed filing logic is active.",
      nextReviewDue: "2026-08-03",
      signOffGate: "source-law-change-review",
      changeDetection: "Compare CRO, Revenue, FRC and Charities Regulator guidance pages against the pinned source-law snapshot before release.",
      failurePolicy: "Block release if any pinned source changes, becomes unreachable, gains a newer effective date, or lacks qualified-accountant review.",
      monitoredSourceIds: ["frc-frs-105", "frc-frs-102"],
      acceptanceCriteria: [
        "CRO, Revenue, FRC and Charities Regulator source pages are reachable and reviewed for changes.",
        "Every changed effective date or guidance wording is reflected in source-law snapshot metadata before release.",
        "A qualified accountant accepts the source-law review note before generated filing packs are used for real filings.",
      ],
      requiredEvidence: [
        "source-law-snapshot-fingerprint",
        "source-law-traceability-index",
        "source-law-change-review-note",
        "qualified-accountant-source-law-signoff",
      ],
    },
    assurancePacket: {
      packetId: "assurance-sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
      packetVersion: "production-assurance-packet-v1",
      status: "review-required",
      sourceLawSnapshotHash: "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
      goldenCorpusCovered: 2,
      goldenCorpusTotal: 2,
      statutoryRuleMatrixPaths: 1,
      statutoryRuleCoverageFamilies: 1,
      visualQaExpectedScreenshots: expectedVisualSmokeScreenshotCount(),
      requiredOperationalGates: 1,
      openCriticalActions: 3,
      evidenceItems: ["source-law-snapshot-fingerprint", "source-law-traceability-index", "source-law-maintenance-protocol", "golden-filing-corpus", "golden-evidence-ledger", "golden-verifier-manifest", "audit-evidence-timeline", "visual-smoke-screenshots", "release-blocker-register", "release-review-checklist", "release-verification-manifest", "accountant-acceptance-summary", "accountant-workflow-walkthrough-protocol", "accountant-journey-acceptance-checklist", "production-completion-map"],
      releaseBlockers: [
        "Qualified accountant sign-off required",
        "Source-law change review required",
        "External ROS/iXBRL validation required",
        "Accountant acceptance walkthrough required",
        "Light/dark visual regression required",
      ],
    },
    accountantAcceptanceCriteria: [
      {
        scenarioCode: "micro-ltd",
        label: "Micro LTD accountant acceptance",
        required: true,
        acceptanceStatus: "qualified-accountant-review-required",
        reviewScope: ["PDF wording", "iXBRL XML", "filing readiness", "tax computation", "notes", "signatory gates"],
        requiredEvidence: ["Named qualified-accountant approval recorded against the generated pack."],
        requiredSignOffGate: "Named qualified accountant must approve the generated pack before real filing use.",
        evidenceVerifiers: [
          {
            name: "AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl",
            command: "dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl",
            ciScope: "default-ci",
            runsInDefaultCi: true,
            environment: "EF Core InMemory golden fixture; CI also runs the broader backend suite on Linux",
            evidenceLevel: "end-to-end golden filing scenario",
          },
        ],
        sources: [source("frc-frs-105", "FRC FRS 105 current edition and amendments")],
      },
      {
        scenarioCode: "dac-small",
        label: "Small DAC accountant acceptance",
        required: true,
        acceptanceStatus: "qualified-accountant-review-required",
        reviewScope: ["DAC accounts PDF", "iXBRL XML", "filing readiness", "tax computation", "notes", "signatory gates"],
        requiredEvidence: ["Named qualified-accountant approval recorded against the DAC pack."],
        requiredSignOffGate: "Named qualified accountant must approve the DAC generated pack before real filing use.",
        evidenceVerifiers: [
          {
            name: "FilingGoldenCorpusScenarioTests.GoldenCorpus_DacSmall_EmitsAccountsIxbrlAndSourceBackedReadiness",
            command: "dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~FilingGoldenCorpusScenarioTests.GoldenCorpus_DacSmall_EmitsAccountsIxbrlAndSourceBackedReadiness",
            ciScope: "default-ci",
            runsInDefaultCi: true,
            environment: "EF Core InMemory golden fixture; CI also runs the broader backend suite on Linux",
            evidenceLevel: "end-to-end golden filing scenario",
          },
        ],
        sources: [source("frc-frs-102", "FRC FRS 102 current edition and amendments")],
      },
    ],
    accountantAcceptanceSummary: {
      scenarioCount: 2,
      automatedVerifierCount: 2,
      professionalSignOffRequiredCount: 2,
      manualHandoffScenarioCount: 0,
      releaseBlockingScenarioCodes: ["dac-small", "micro-ltd"],
      requiredSignOffGates: [
        "Named qualified accountant must approve the DAC generated pack before real filing use.",
        "Named qualified accountant must approve the generated pack before real filing use.",
      ],
      status: "qualified-accountant-review-required",
    },
    accountantWorkflowWalkthroughProtocol: {
      protocolVersion: "accountant-workflow-walkthrough-v1",
      reviewerRole: "Qualified accountant",
      status: "required-review",
      signOffGate: "golden-corpus-accountant-acceptance",
      failurePolicy: "Block release if a named qualified accountant has not walked the seeded golden corpus through the live accountant workflow and accepted the outputs, gates, wording and evidence.",
      seededScenarioCodes: ["dac-small", "micro-ltd"],
      routeSequence: [
        "Dashboard: identify the client, deadline pressure, blockers, reviewer owner and next action.",
        "Company detail: confirm statutory profile, company type, officers, charity flags and period setup.",
        "Period workspace: review import, classification, year-end evidence, statements, notes and workflow rail state.",
        "Filing review: inspect readiness profile, legal source links, generated outputs, signatory gates and accountant sign-off packet.",
        "Production readiness: confirm golden corpus, statutory rules coverage, visual QA, release blockers and operational controls.",
      ],
      acceptanceCriteria: [
        "Micro LTD walkthrough confirms PDF wording, iXBRL XML, tax computation, notes, signatory gates and 100% filing readiness.",
        "Small abridged LTD walkthrough confirms full accounts, abridged CRO pack, Section 352 evidence, iXBRL and audit-exemption gates.",
        "CLG charity walkthrough confirms charity number, SoFA, trustees annual report, charity notes and Charities Regulator evidence.",
        "Medium/audit-required walkthrough confirms auditor handoff blocks normal approval until signed auditor report evidence and manual acceptance are recorded.",
        "A named qualified accountant states that the generated outputs, gates, wording and evidence are professionally acceptable for the supported scope.",
      ],
      requiredEvidence: [
        "seeded golden corpus walkthrough note",
        "named qualified-accountant approval",
        "visual QA screenshot review",
        "generated PDF and iXBRL evidence",
        "manual handoff acceptance",
      ],
    },
    accountantJourneyAcceptanceChecklist: [
      journeyAcceptance("dashboard", "Dashboard", "dashboard", ["Setup", "Import", "Classify", "Year-End", "Statements", "Notes", "Review", "Filing"]),
      journeyAcceptance("company-detail", "Company detail", "company", ["Setup"]),
      journeyAcceptance("period-workspace", "Period workspace", "period", ["Setup", "Import", "Classify", "Year-End", "Statements", "Notes", "Review", "Filing"]),
      journeyAcceptance("filing-review", "Filing review", "filing", ["Review", "Filing"], [
        "Filing review route exposes readiness, source links, generated outputs, signatory gates, accountant sign-off packet, external ROS/iXBRL validation and filing state.",
        "A named qualified accountant accepts the Filing review route outputs, gates, wording and evidence for every seeded golden scenario.",
      ]),
      journeyAcceptance("production-readiness", "Production readiness", "readiness", ["Review", "Filing"], [
        "Production readiness route exposes backend checks, filing rules coverage, unsupported paths, security posture, release blockers and accountant review state.",
        "A named qualified accountant accepts the Production readiness route outputs, gates, wording and evidence for every seeded golden scenario.",
      ]),
    ],
    areas: [
      {
        code: "backend-accounting-engine",
        label: "Backend accounting engine",
        status: "hardened",
        detail: "Golden-path coverage exercises outputs and gates.",
      },
    ],
    goldenFilingCorpus: [
      {
        code: "micro-ltd",
        label: "Micro LTD",
        companyScope: "Private company limited by shares",
        expectedOutcome: "generated-pack",
        coverageStatus: "covered",
        fixture: {
          legalName: "Example Micro Limited",
          companyType: "Private",
          periodStart: "2025-01-01",
          periodEnd: "2025-12-31",
          expectedSizeClass: "Micro",
          expectedRegime: "Micro",
          auditExempt: true,
          manualProfessionalReviewRequired: false,
        },
        evidenceTestNames: ["AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl"],
        evidenceVerifiers: [
          {
            name: "AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl",
            command: "dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl",
            ciScope: "default-ci",
            runsInDefaultCi: true,
            environment: "EF Core InMemory golden fixture; CI also runs the broader backend suite on Linux",
            evidenceLevel: "end-to-end golden filing scenario",
          },
        ],
        assertions: ["PDF text", "iXBRL parse"],
        evidencePack: {
          outputArtifacts: ["accounts PDF text"],
          decisionGates: ["named qualified-accountant review"],
          expectedValueChecks: ["well-formed iXBRL"],
          expectedOutputs: {
            pdfTextMarkers: ["Example Micro Limited", "280D"],
            ixbrlRequiredTags: ["core:EntityCurrentLegalOrRegisteredName"],
            filingReadinessState: "100% filing readiness",
            expectedCorporationTax: 718.75,
            requiredNotes: ["Accounting Policies"],
            filingGateStates: ["director and secretary certification required", "qualified-accountant review required"],
            signOffPacketState: "review-required",
          },
          expectedProofPoints: [
            {
              area: "pdf-text",
              expectedEvidence: "PDF text contains company name and micro statutory statement.",
              automatedVerifier: "AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl",
              required: true,
            },
          ],
          sourceReferences: [source("frc-frs-105", "FRC FRS 105 current edition and amendments")],
        },
      },
      {
        code: "dac-small",
        label: "Small DAC",
        companyScope: "Designated activity company",
        expectedOutcome: "generated-pack",
        coverageStatus: "covered",
        fixture: {
          legalName: "Atlantic Manufacturing DAC",
          companyType: "DesignatedActivityCompany",
          periodStart: "2026-01-01",
          periodEnd: "2026-12-31",
          expectedSizeClass: "Small",
          expectedRegime: "Small",
          auditExempt: true,
          manualProfessionalReviewRequired: false,
        },
        evidenceTestNames: ["FilingGoldenCorpusScenarioTests.GoldenCorpus_DacSmall_EmitsAccountsIxbrlAndSourceBackedReadiness"],
        evidenceVerifiers: [
          {
            name: "FilingGoldenCorpusScenarioTests.GoldenCorpus_DacSmall_EmitsAccountsIxbrlAndSourceBackedReadiness",
            command: "dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~FilingGoldenCorpusScenarioTests.GoldenCorpus_DacSmall_EmitsAccountsIxbrlAndSourceBackedReadiness",
            ciScope: "default-ci",
            runsInDefaultCi: true,
            environment: "EF Core InMemory golden fixture; CI also runs the broader backend suite on Linux",
            evidenceLevel: "end-to-end golden filing scenario",
          },
        ],
        assertions: ["PDF text", "iXBRL parse", "source-backed readiness"],
        evidencePack: {
          outputArtifacts: ["DAC accounts PDF text"],
          decisionGates: ["named qualified-accountant review"],
          expectedValueChecks: ["well-formed iXBRL"],
          expectedOutputs: {
            pdfTextMarkers: ["Atlantic Manufacturing DAC", "DIRECTORS' REPORT"],
            ixbrlRequiredTags: ["bus:EntityCurrentLegalOrRegisteredName"],
            filingReadinessState: "ready-for-external-filing",
            expectedCorporationTax: 62.5,
            requiredNotes: ["Accounting Policies"],
            filingGateStates: ["director and secretary certification satisfied", "qualified-accountant review recorded"],
            signOffPacketState: "ready-for-external-filing",
          },
          expectedProofPoints: [
            {
              area: "pdf-text",
              expectedEvidence: "DAC accounts PDF text contains the legal name and directors' report.",
              automatedVerifier: "FilingGoldenCorpusScenarioTests.GoldenCorpus_DacSmall_EmitsAccountsIxbrlAndSourceBackedReadiness",
              required: true,
            },
          ],
          sourceReferences: [source("frc-frs-102", "FRC FRS 102 current edition and amendments")],
        },
      },
    ],
    goldenEvidenceLedger: [
      {
        scenarioCode: "micro-ltd",
        label: "Micro LTD",
        fixtureLegalName: "Example Micro Limited",
        companyType: "Private",
        expectedOutcome: "generated-pack",
        coverageStatus: "covered",
        acceptanceStatus: "qualified-accountant-review-required",
        requiredSignOffGate: "Named qualified accountant must approve the generated pack before real filing use.",
        blocksRelease: true,
        automatedVerifierNames: ["AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl"],
        outputArtifacts: ["accounts PDF text"],
        decisionGates: ["named qualified-accountant review"],
        expectedValueChecks: ["well-formed iXBRL"],
        proofPointAreas: ["pdf-text"],
        sourceIds: ["frc-frs-105"],
        expectedCorporationTax: 718.75,
        filingReadinessState: "100% filing readiness",
        signOffPacketState: "review-required",
      },
      {
        scenarioCode: "dac-small",
        label: "Small DAC",
        fixtureLegalName: "Atlantic Manufacturing DAC",
        companyType: "DesignatedActivityCompany",
        expectedOutcome: "generated-pack",
        coverageStatus: "covered",
        acceptanceStatus: "qualified-accountant-review-required",
        requiredSignOffGate: "Named qualified accountant must approve the DAC generated pack before real filing use.",
        blocksRelease: true,
        automatedVerifierNames: ["FilingGoldenCorpusScenarioTests.GoldenCorpus_DacSmall_EmitsAccountsIxbrlAndSourceBackedReadiness"],
        outputArtifacts: ["DAC accounts PDF text"],
        decisionGates: ["named qualified-accountant review"],
        expectedValueChecks: ["well-formed iXBRL"],
        proofPointAreas: ["pdf-text"],
        sourceIds: ["frc-frs-102"],
        expectedCorporationTax: 62.5,
        filingReadinessState: "ready-for-external-filing",
        signOffPacketState: "ready-for-external-filing",
      },
    ],
    statutoryRuleMatrix: [
      {
        code: "ltd-micro",
        companyScope: "LTD micro",
        sizeOrRegime: "Micro / FRS 105",
        supportLevel: "supported",
        requiredEvidence: ["size classification"],
        requiredOutputs: ["micro accounts PDF"],
        manualHandoffGates: ["qualified accountant review"],
        sources: [source("frc-frs-105", "FRC FRS 105 current edition and amendments")],
      },
    ],
    statutoryRulesCoverage: [
      {
        code: "size-classification-thresholds",
        ruleFamily: "Size classification",
        decisionUnderTest: "Two-of-three thresholds and current/prior movement produce the statutory size class.",
        coverageStatus: "covered",
        automatedVerifierNames: ["AccountsWorkflowTests.SizeClassification_FirstYearMicro_AllowsMicroAndAuditExemption"],
        edgeCases: ["two-of-three threshold rule", "current and prior year classification rule"],
        sources: [source("frc-frs-105", "FRC FRS 105 current edition and amendments")],
      },
    ],
    manualHandoffPaths: ["PLC and public-company workflows"],
    operationalGates: [
      {
        code: "qualified-accountant-review",
        label: "Named qualified-accountant review",
        required: true,
        status: "required",
        detail: "Real filing packs require named professional approval before use.",
      },
      {
        code: "external-ros-validation",
        label: "External ROS/iXBRL validation",
        required: true,
        status: "required",
        detail: "External validation evidence must be retained before Revenue filing use.",
      },
    ],
    assuranceActions: [
      {
        code: "qualified-accountant-signoff",
        label: "Qualified accountant sign-off",
        owner: "Qualified accountant",
        priority: "critical",
        riskRank: 0,
        evidenceStage: "accountant-review-gate",
        status: "required",
        detail: "No real filing pack can be treated as final until a named qualified accountant has approved it.",
        evidenceRequired: "Named accountant approval recorded against the period.",
      },
      {
        code: "source-law-change-review",
        label: "Source-law change review",
        owner: "Qualified accountant and engineering",
        priority: "critical",
        riskRank: 2,
        evidenceStage: "source-law-maintenance",
        status: "required",
        detail: "Pinned CRO, Revenue, FRC and charity guidance must be reviewed for effective-date or wording changes before release.",
        evidenceRequired: "Source-law change review note and qualified-accountant sign-off recorded against the snapshot.",
      },
      {
        code: "external-ros-validation",
        label: "External ROS/iXBRL validation",
        owner: "Reviewer",
        priority: "critical",
        riskRank: 5,
        evidenceStage: "external-validation-gate",
        status: "required",
        detail: "Internal XML checks are not a Revenue acceptance check.",
        evidenceRequired: "External ROS validation evidence uploaded or referenced.",
      },
      {
        code: "accountant-acceptance-walkthrough",
        label: "Accountant acceptance walkthrough",
        owner: "Qualified accountant",
        priority: "high",
        riskRank: 10,
        evidenceStage: "golden-corpus-acceptance",
        status: "required",
        detail: "A qualified accountant must accept outputs, gates and wording.",
        evidenceRequired: "Signed acceptance note for the golden corpus.",
      },
      {
        code: "light-dark-visual-regression",
        label: "Light/dark visual regression",
        owner: "Engineering",
        priority: "high",
        riskRank: 30,
        evidenceStage: "visual-qa-evidence",
        status: "in-progress",
        detail: "The accountant journey needs screenshot evidence across light and dark mode.",
        evidenceRequired: "Light/dark desktop/mobile screenshots for the main workflow routes.",
      },
    ],
    releaseBlockerRegister: [
      {
        code: "backend-code:qualified-accountant-signoff",
        trackCode: "backend-code",
        trackLabel: "Backend code",
        ownerRole: "Qualified accountant",
        severity: "critical",
        riskRank: 0,
        blockingIssue: "Qualified accountant sign-off required",
        requiredEvidence: "Named accountant approval recorded against the period.",
        nextAction: "Run qualified-accountant acceptance on the golden corpus.",
        sourceActionCode: "qualified-accountant-signoff",
        releaseChecklistCode: "accountant-final-signoff",
        operationalGateCode: "qualified-accountant-review",
        evidenceArtifact: "named-accountant-approval-record",
        blocksRelease: true,
      },
      {
        code: "backend-code:source-law-change-review",
        trackCode: "backend-code",
        trackLabel: "Backend code",
        ownerRole: "Qualified accountant and engineering",
        severity: "critical",
        riskRank: 2,
        blockingIssue: "Source-law change review required",
        requiredEvidence: "Source-law change review note and qualified-accountant sign-off recorded against the snapshot.",
        nextAction: "Run qualified-accountant acceptance on the golden corpus.",
        sourceActionCode: "source-law-change-review",
        releaseChecklistCode: "source-law-change-review",
        operationalGateCode: "qualified-accountant-review",
        evidenceArtifact: "source-law-change-review-note",
        blocksRelease: true,
      },
      {
        code: "backend-code:external-ros-validation",
        trackCode: "backend-code",
        trackLabel: "Backend code",
        ownerRole: "Reviewer",
        severity: "critical",
        riskRank: 5,
        blockingIssue: "External ROS/iXBRL validation required",
        requiredEvidence: "External ROS validation evidence uploaded or referenced.",
        nextAction: "Attach external ROS/iXBRL validation evidence for generated iXBRL packs.",
        sourceActionCode: "external-ros-validation",
        releaseChecklistCode: "external-ros-validation-evidence",
        operationalGateCode: "external-ros-validation",
        evidenceArtifact: "external-ros-validation-reference",
        blocksRelease: true,
      },
      {
        code: "backend-code:accountant-acceptance-walkthrough",
        trackCode: "backend-code",
        trackLabel: "Backend code",
        ownerRole: "Qualified accountant",
        severity: "high",
        riskRank: 10,
        blockingIssue: "Accountant acceptance walkthrough required",
        requiredEvidence: "Signed acceptance note for the golden corpus.",
        nextAction: "Run qualified-accountant acceptance on the golden corpus.",
        sourceActionCode: "accountant-acceptance-walkthrough",
        releaseChecklistCode: "golden-corpus-accountant-acceptance",
        operationalGateCode: "qualified-accountant-review",
        evidenceArtifact: "signed-golden-corpus-acceptance-note",
        blocksRelease: true,
      },
      {
        code: "frontend-ui-ux:light-dark-visual-regression",
        trackCode: "frontend-ui-ux",
        trackLabel: "Frontend UI/UX",
        ownerRole: "Engineering",
        severity: "high",
        riskRank: 30,
        blockingIssue: "Light/dark visual regression required",
        requiredEvidence: "Light/dark desktop/mobile screenshots for the main workflow routes.",
        nextAction: "Review each screenshot route-by-route in light and dark mode.",
        sourceActionCode: "light-dark-visual-regression",
        releaseChecklistCode: "visual-qa-screenshot-review",
        operationalGateCode: "",
        evidenceArtifact: "visual-smoke-screenshots",
        blocksRelease: true,
      },
      {
        code: "frontend-ui-ux:accountant-acceptance-walkthrough",
        trackCode: "frontend-ui-ux",
        trackLabel: "Frontend UI/UX",
        ownerRole: "Qualified accountant",
        severity: "high",
        riskRank: 10,
        blockingIssue: "Accountant acceptance walkthrough required",
        requiredEvidence: "Signed acceptance note for the golden corpus.",
        nextAction: "Record named visual acceptance against the smoke manifest.",
        sourceActionCode: "accountant-acceptance-walkthrough",
        releaseChecklistCode: "golden-corpus-accountant-acceptance",
        operationalGateCode: "qualified-accountant-review",
        evidenceArtifact: "signed-golden-corpus-acceptance-note",
        blocksRelease: true,
      },
      {
        code: "frontend-code:light-dark-visual-regression",
        trackCode: "frontend-code",
        trackLabel: "Frontend code",
        ownerRole: "Engineering",
        severity: "high",
        riskRank: 30,
        blockingIssue: "Light/dark visual regression required",
        requiredEvidence: "Light/dark desktop/mobile screenshots for the main workflow routes.",
        nextAction: "Expand visual regression assertions from screenshot capture into reviewable sign-off.",
        sourceActionCode: "light-dark-visual-regression",
        releaseChecklistCode: "visual-qa-screenshot-review",
        operationalGateCode: "",
        evidenceArtifact: "visual-smoke-screenshots",
        blocksRelease: true,
      },
    ],
    completionTracks: [
      {
        code: "backend-code",
        label: "Backend code",
        ownerRole: "Engineering",
        status: "review-required",
        completionCriteria: [
          "Golden filing corpus proves PDF text, iXBRL XML, tax, notes, readiness and gates.",
          "Source-law snapshot and traceability cover every statutory decision.",
          "Production auditability captures who changed, approved, generated and submitted each pack.",
        ],
        currentEvidence: [
          "Backend golden corpus scenarios are covered by automated verifiers.",
          "Statutory rules coverage is mapped to executable tests.",
          "Production auditability controls and audit evidence timeline are declared.",
        ],
        nextActions: [
          "Run qualified-accountant acceptance on the golden corpus.",
          "Attach external ROS/iXBRL validation evidence for generated iXBRL packs.",
          "Record manual handoff acceptance for audit-required paths.",
        ],
        assuranceActionCodes: [
          "qualified-accountant-signoff",
          "source-law-change-review",
          "external-ros-validation",
          "accountant-acceptance-walkthrough",
        ],
      },
      {
        code: "frontend-ui-ux",
        label: "Frontend UI/UX",
        ownerRole: "Product design",
        status: "in-progress",
        completionCriteria: [
          "Accountant workflow rail is visually coherent across the core journey.",
          "Light/dark visual regression covers desktop and mobile.",
          "Dense review workbench surfaces blockers, evidence, sources and next actions without visual clutter.",
        ],
        currentEvidence: [
          "Visual QA route audit covers the accountant workbench routes.",
          "Dashboard filing deep links send deadline-pressure and manual-handoff work directly to the period filing review tab.",
          "Period filing gate snapshot shows supported/manual path, accountant review state, external filing readiness and allowed next action.",
          "Route-level loading/error states exist for main dynamic routes.",
          "Permission-denied filing action state keeps evidence visible while blocking ineligible review actions.",
          "Workbench primitives are used in the readiness and period review surfaces.",
        ],
        nextActions: [
          "Review each screenshot route-by-route in light and dark mode.",
          "Polish spacing, typography, table density, empty states and mobile flow.",
          "Record named visual acceptance against the smoke manifest.",
        ],
        assuranceActionCodes: [
          "light-dark-visual-regression",
          "accountant-acceptance-walkthrough",
        ],
      },
      {
        code: "frontend-code",
        label: "Frontend code",
        ownerRole: "Frontend engineering",
        status: "in-progress",
        completionCriteria: [
          "Shared workbench primitives cover repeated page patterns.",
          "Typed API contract blocks frontend/backend readiness drift.",
          "Route-level states cover loading, error, empty and permission-denied cases.",
        ],
        currentEvidence: [
          "API client invariants validate production readiness contracts.",
          "Component-preview route exercises shared workbench primitives.",
          "FilingReviewCentre permission gate blocks approval/submission actions behind canReview and renders PermissionDeniedPanel for ineligible roles.",
          "PeriodFilingWorkspace extraction composes review, deadline, warning, output and audit panels behind one focused filing workflow component.",
          "PeriodImportWorkspace extraction composes classification, bank account, opening-balance, CSV upload and import-status panels behind one focused import workflow component.",
          "PeriodCategoriseWorkspace extraction composes metrics, transaction rules, bulk actions, filters and categorisation table behind one focused transaction review component.",
          "Render tests cover accountant dashboards, review panels and workflow routes.",
        ],
        nextActions: [
          "Continue extracting large route files into focused workflow components.",
          "Expand visual regression assertions from screenshot capture into reviewable sign-off.",
          "Keep route fixtures aligned with backend readiness evidence.",
        ],
        assuranceActionCodes: ["light-dark-visual-regression"],
      },
    ],
    releaseReviewChecklist: [
      {
        code: "accountant-final-signoff",
        label: "Named accountant final sign-off",
        ownerRole: "Qualified accountant",
        required: true,
        status: "required",
        blocksRelease: true,
        evidenceArtifact: "named-accountant-approval-record",
        assuranceActionCode: "qualified-accountant-signoff",
        operationalGateCode: "qualified-accountant-review",
        auditEventCodes: ["CroFilingStatusChanged"],
        detail: "Named professional approval must be recorded against the period.",
      },
      {
        code: "source-law-change-review",
        label: "Source-law change review",
        ownerRole: "Qualified accountant and engineering",
        required: true,
        status: "required",
        blocksRelease: true,
        evidenceArtifact: "source-law-change-review-note",
        assuranceActionCode: "source-law-change-review",
        operationalGateCode: "qualified-accountant-review",
        auditEventCodes: ["CroFilingStatusChanged"],
        detail: "Pinned CRO, Revenue, FRC and charity guidance must be reviewed for effective-date or wording changes before release.",
      },
      {
        code: "external-ros-validation-evidence",
        label: "External ROS/iXBRL validation evidence",
        ownerRole: "Reviewer",
        required: true,
        status: "required",
        blocksRelease: true,
        evidenceArtifact: "external-ros-validation-reference",
        assuranceActionCode: "external-ros-validation",
        operationalGateCode: "external-ros-validation",
        auditEventCodes: ["IxbrlInternalCheckCompleted"],
        detail: "External ROS validation evidence must be retained before real Revenue filing use.",
      },
      {
        code: "golden-corpus-accountant-acceptance",
        label: "Golden corpus accountant acceptance",
        ownerRole: "Qualified accountant",
        required: true,
        status: "required",
        blocksRelease: true,
        evidenceArtifact: "signed-golden-corpus-acceptance-note",
        assuranceActionCode: "accountant-acceptance-walkthrough",
        operationalGateCode: "qualified-accountant-review",
        auditEventCodes: ["CroDocumentGenerated", "IxbrlInternalCheckCompleted", "NotesGenerated"],
        detail: "A qualified accountant must walk the golden scenarios and accept outputs, gates and wording.",
      },
      {
        code: "visual-qa-screenshot-review",
        label: "Visual QA screenshot review",
        ownerRole: "Engineering",
        required: true,
        status: "in-progress",
        blocksRelease: true,
        evidenceArtifact: "visual-smoke-screenshots",
        assuranceActionCode: "light-dark-visual-regression",
        operationalGateCode: "",
        auditEventCodes: [],
        detail: "Visual smoke screenshots must be reviewed in light and dark mode before release.",
      },
    ],
    releaseVerificationManifest: [
      {
        code: "backend-golden-corpus",
        label: "Backend golden corpus and statutory rules",
        ownerRole: "Engineering",
        command: "dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art",
        ciScope: "default-ci",
        runsInDefaultCi: true,
        blocksRelease: true,
        evidenceArtifact: "backend-test-results",
        releaseChecklistEvidenceArtifact: "named-accountant-approval-record",
        manualFallback: "Run the same command locally from backend/ when GitHub Actions is unavailable.",
      },
      {
        code: "postgres-gated-audit-tests",
        label: "PostgreSQL-gated audit durability tests",
        ownerRole: "Engineering",
        command: "dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~PostgresIntegration",
        ciScope: "environment-gated",
        runsInDefaultCi: false,
        blocksRelease: true,
        evidenceArtifact: "postgres-integration-test-results",
        releaseChecklistEvidenceArtifact: "named-accountant-approval-record",
        manualFallback: "Set ACCOUNTS_POSTGRES_TEST_CONNECTION to a disposable PostgreSQL database before relying on audit durability evidence.",
      },
      {
        code: "visual-smoke-light-dark",
        label: "Light/dark desktop/mobile visual smoke",
        ownerRole: "Engineering",
        command: "node scripts/visual-smoke.mjs; node scripts/verify-visual-smoke-artifacts.mjs",
        ciScope: "default-ci",
        runsInDefaultCi: true,
        blocksRelease: true,
        evidenceArtifact: "artifacts/visual-smoke",
        releaseChecklistEvidenceArtifact: "visual-smoke-screenshots",
        manualFallback: "Run visual smoke locally, then retain the manifest verification output and review the generated artifacts manually.",
      },
      {
        code: "source-law-change-review",
        label: "Source-law change review note",
        ownerRole: "Qualified accountant and engineering",
        command: "manual review: compare pinned CRO, Revenue, FRC and charity guidance against source-law snapshot",
        ciScope: "manual-release",
        runsInDefaultCi: false,
        blocksRelease: true,
        evidenceArtifact: "source-law-change-review-note",
        releaseChecklistEvidenceArtifact: "source-law-change-review-note",
        manualFallback: "Retain a dated source-law review note before relying on the generated packs for real filing use.",
      },
      {
        code: "external-ros-validation-evidence",
        label: "External ROS/iXBRL validation evidence",
        ownerRole: "Reviewer",
        command: "manual review: retain external ROS/iXBRL validation reference for the exact generated iXBRL pack",
        ciScope: "manual-release",
        runsInDefaultCi: false,
        blocksRelease: true,
        evidenceArtifact: "external-ros-validation-reference",
        releaseChecklistEvidenceArtifact: "external-ros-validation-reference",
        manualFallback: "Retain the external ROS validation reference before final approval.",
      },
      {
        code: "manual-accountant-acceptance",
        label: "Named accountant acceptance walkthrough",
        ownerRole: "Qualified accountant",
        command: "manual walkthrough: micro LTD, small abridged LTD, CLG charity and medium/audit-required handoff",
        ciScope: "manual-release",
        runsInDefaultCi: false,
        blocksRelease: true,
        evidenceArtifact: "signed-golden-corpus-acceptance-note",
        releaseChecklistEvidenceArtifact: "signed-golden-corpus-acceptance-note",
        manualFallback: "A named qualified accountant must accept the generated outputs, gates, wording and source-law evidence.",
      },
    ],
    auditabilityControls: [
      {
        code: "who-changed-what",
        label: "Who changed what",
        required: true,
        enforcement: "audit-log-integrity-chain",
        evidenceCaptured: "Authenticated user id, timestamp, entity, action and old/new value snapshots.",
        verification: "Hash chain verification covers each company-scoped audit row.",
        auditEventCodes: ["AdjustmentUpdated"],
      },
    ],
    auditEvidenceTimeline: [
      {
        code: "data-change-capture",
        stage: "Working papers",
        evidenceQuestion: "Who changed what and when?",
        capturedWhen: "At every authenticated write before regenerated outputs can be reviewed.",
        requiredActor: "Authenticated firm user",
        verification: "Audit log snapshots and integrity hash chain must cover the changed entity.",
        auditEventCodes: ["AdjustmentUpdated"],
        blockingGateCodes: ["working-paper-review"],
      },
      {
        code: "generated-output-capture",
        stage: "Generated outputs",
        evidenceQuestion: "What was generated and when?",
        capturedWhen: "Immediately after server-side PDF, notes or iXBRL generation completes.",
        requiredActor: "System generation service",
        verification: "Generated output audit event must exist before accountant approval can rely on the pack.",
        auditEventCodes: ["CroDocumentGenerated"],
        blockingGateCodes: ["generated-output-review"],
      },
    ],
    monitoringControls: [
      {
        code: "error-tracking",
        label: "Production error tracking",
        provider: "Sentry-compatible",
        required: true,
        productionSafetyGate: "Monitoring:ErrorTrackingDsn",
        evidenceCaptured: "Unhandled exceptions are routed to the configured production error-tracking provider.",
        verification: "Program.cs wires UseSentry and ProductionSafetyService blocks a missing DSN.",
        alertRoute: "Primary on-call accountant and platform owner",
        failurePolicy: "Block release if error events cannot be routed to the on-call owner.",
      },
    ],
    dependencyPolicyControls: [
      {
        code: "frontend-npm-audit",
        label: "Frontend dependency vulnerability audit",
        required: true,
        enforcement: "CI frontend job runs npm audit --audit-level=moderate after npm ci.",
        evidenceCaptured: "npm audit report for dependencies resolved from frontend/package-lock.json.",
        verification: ".github/workflows/ci.yml Audit frontend dependencies step.",
        failurePolicy: "Fail the release for moderate, high or critical npm advisories.",
      },
    ],
    deploymentSafetyControls: [
      {
        code: "controlled-production-migrations",
        label: "Controlled production migrations",
        required: true,
        enforcement: "Production migrations run through dotnet Accounts.Api.dll --migrate-only before app startup.",
        evidenceCaptured: "CI production image contract and release runbook prove migrations are a separate controlled step.",
        verification: "Program.cs handles --migrate-only and ProductionSafetyService blocks unsafe AutoMigrateOnStartup.",
        failurePolicy: "Fail production startup when AutoMigrateOnStartup is enabled without explicit production approval.",
      },
      {
        code: "production-demo-seed-block",
        label: "Production demo seed blocking",
        required: true,
        enforcement: "ProductionSafetyService rejects DatabaseStartup:SeedDemoData outside development.",
        evidenceCaptured: "Startup safety validation blocks known sample companies and demo users in production.",
        verification: "ProductionSafetyService validates SeedDemoData before database startup tasks execute.",
        failurePolicy: "Fail production startup if demo seed data is enabled outside development.",
      },
    ],
    visualQaCoverage: {
      artifactName: "visual-smoke-screenshots",
      enforcement: "ci-production-smoke",
      manifestFileName: "visual-smoke-manifest.json",
      expectedScreenshotCount: expectedVisualSmokeScreenshotCount(),
      layoutChecks: visualSmokeLayoutChecks,
      reviewChecks: visualSmokeReviewChecks,
      reviewProtocol: structuredClone(visualSmokeReviewProtocol),
      themes: visualSmokeThemes,
      viewports: visualSmokeViewports,
      routes: visualSmokeRoutes.map(({ name, routeKey, label, description, expectedText, workflowStages, openFilingTab }) => ({
        code: name,
        routeKey,
        label,
        description,
        requiredText: expectedText,
        workflowStages,
        openFilingTab,
      })),
      routeAudits: expectedVisualSmokeRouteAudits().map((audit) => ({
        routeCode: audit.routeName,
        routeKey: audit.routeKey,
        label: audit.label,
        workflowStages: audit.workflowStages,
        screenshotCount: audit.screenshotCount,
        reviewStatus: audit.reviewStatus,
        reviewChecks: audit.reviewChecks,
      })),
      artifacts: expectedVisualSmokeArtifacts().map(({ routeName, routeKey, theme, viewportName, fileName, artifactPath, expectedText, openFilingTab, reviewStatus, layoutChecks }) => ({
        routeCode: routeName,
        routeKey,
        theme,
        viewportName,
        fileName,
        artifactPath,
        requiredText: expectedText,
        openFilingTab,
        reviewStatus,
        layoutChecks,
      })),
    },
  };
}

function journeyAcceptance(routeCode, routeLabel, routeKey, workflowStages, acceptanceCriteria) {
  return {
    routeCode,
    routeLabel,
    routeKey,
    workflowStages,
    seededScenarioCodes: ["dac-small", "micro-ltd"],
    visualArtifactNames: ["dark-desktop", "dark-mobile", "light-desktop", "light-mobile"].map(
      (suffix) => `${routeCode}-${suffix}.png`,
    ),
    requiredEvidence: [
      "named qualified-accountant route acceptance",
      "visual smoke screenshots reviewed",
      "golden corpus evidence accepted",
    ],
    acceptanceCriteria: acceptanceCriteria ?? [
      `${routeLabel} route exposes the relevant accountant workflow state, blockers, next actions and evidence.`,
      `A named qualified accountant accepts the ${routeLabel} route outputs, gates, wording and evidence for every seeded golden scenario.`,
    ],
    signOffGate: "golden-corpus-accountant-acceptance",
    status: "required-review",
  };
}

function source(sourceId, title) {
  return {
    sourceId,
    title,
    effectiveDate: "2026-07-03",
    url: "https://www.frc.org.uk/",
  };
}
