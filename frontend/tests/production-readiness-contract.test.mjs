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
  assert.equal(parsed.goldenFilingCorpus[0].evidencePack.expectedOutputs.expectedCorporationTax, 62.5);
  assert.equal(parsed.goldenFilingCorpus[0].evidencePack.expectedOutputs.signOffPacketState, "review-required");
  assert.equal(parsed.goldenFilingCorpus[0].evidencePack.expectedProofPoints[0].area, "pdf-text");
  assert.equal(parsed.goldenFilingCorpus[0].evidencePack.expectedProofPoints[0].required, true);
  assert.equal(parsed.goldenFilingCorpus[0].evidencePack.sourceReferences[0].sourceId, "frc-frs-105");
  assert.equal(parsed.goldenFilingCorpus[0].legalBasisSnapshot.scenarioCode, "micro-ltd");
  assert.equal(parsed.goldenFilingCorpus[0].legalBasisSnapshot.companyType, "Private");
  assert.equal(parsed.goldenFilingCorpus[0].legalBasisSnapshot.electedRegime, "Micro");
  assert.equal(parsed.goldenFilingCorpus[0].legalBasisSnapshot.auditExempt, true);
  assert.match(parsed.goldenFilingCorpus[0].legalBasisSnapshot.legalBasis, /FRS 105 micro-entities/);
  assert.ok(parsed.goldenFilingCorpus[0].legalBasisSnapshot.sourceIds.includes("revenue-accepted-taxonomies"));
  assert.equal(parsed.goldenEvidenceLedger[0].scenarioCode, "micro-ltd");
  assert.equal(parsed.goldenEvidenceLedger[0].fixtureLegalName, "Example Micro Limited");
  assert.equal(parsed.goldenEvidenceLedger[0].expectedCorporationTax, 62.5);
  assert.deepEqual(parsed.goldenEvidenceLedger[0].automatedVerifierNames, parsed.goldenFilingCorpus[0].evidenceTestNames);
  assert.deepEqual(parsed.goldenEvidenceLedger[0].automatedVerifierCommands, parsed.goldenFilingCorpus[0].evidenceVerifiers.map((verifier) => verifier.command));
  assert.deepEqual(parsed.goldenEvidenceLedger[0].ciScopes, ["default-ci"]);
  assert.deepEqual(parsed.goldenEvidenceLedger[0].evidenceLevels, ["end-to-end golden filing scenario"]);
  assert.ok(parsed.goldenEvidenceLedger[0].blocksRelease);
  assert.equal(parsed.goldenVerifierManifest[0].scenarioCode, "micro-ltd");
  assert.equal(parsed.goldenVerifierManifest[0].scenarioLabel, "Micro LTD");
  assert.equal(parsed.goldenVerifierManifest[0].verifierName, parsed.goldenFilingCorpus[0].evidenceVerifiers[0].name);
  assert.equal(parsed.goldenVerifierManifest[0].command, parsed.goldenFilingCorpus[0].evidenceVerifiers[0].command);
  assert.ok(parsed.goldenVerifierManifest[0].blocksRelease);
  assert.deepEqual(parsed.goldenVerifierManifest[0].outputArtifacts, parsed.goldenFilingCorpus[0].evidencePack.outputArtifacts);
  const dacScenario = parsed.goldenFilingCorpus.find((scenario) => scenario.code === "dac-small");
  assert.equal(dacScenario?.fixture.legalName, "Atlantic Manufacturing DAC");
  assert.equal(dacScenario?.fixture.companyType, "DesignatedActivityCompany");
  assert.equal(dacScenario?.evidenceVerifiers[0].name, "FilingGoldenCorpusScenarioTests.GoldenCorpus_DacSmall_EmitsAccountsIxbrlAndSourceBackedReadiness");
  assert.equal(dacScenario?.evidencePack.expectedOutputs.ixbrlRequiredTags[0], "bus:EntityCurrentLegalOrRegisteredName");
  const smallAbridgedScenario = parsed.goldenFilingCorpus.find((scenario) => scenario.code === "small-abridged-ltd");
  assert.equal(smallAbridgedScenario?.fixture.legalName, "Connacht Digital Solutions Limited");
  assert.equal(smallAbridgedScenario?.fixture.expectedRegime, "SmallAbridged");
  assert.ok(smallAbridgedScenario?.evidenceTestNames.includes("FilingGoldenCorpusScenarioTests.GoldenCorpus_SmallAbridgedLtd_EmitsFullAccountsAbridgedCroPackIxbrlAndReadiness"));
  assert.ok(smallAbridgedScenario?.evidencePack.outputArtifacts.includes("abridged CRO filing pack"));
  assert.ok(smallAbridgedScenario?.evidencePack.expectedValueChecks.includes("Section 352 wording"));
  assert.ok(smallAbridgedScenario?.evidencePack.expectedOutputs.pdfTextMarkers.includes("Section 352"));
  assert.ok(smallAbridgedScenario?.evidencePack.expectedOutputs.filingGateStates.includes("abridgement eligibility confirmed"));
  const clgCharityScenario = parsed.goldenFilingCorpus.find((scenario) => scenario.code === "clg-charity");
  assert.equal(clgCharityScenario?.fixture.legalName, "Dublin Community Support CLG");
  assert.equal(clgCharityScenario?.fixture.companyType, "CompanyLimitedByGuarantee");
  assert.ok(clgCharityScenario?.evidenceTestNames.includes("FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness"));
  assert.ok(clgCharityScenario?.evidencePack.outputArtifacts.includes("charity readiness profile"));
  assert.ok(clgCharityScenario?.evidencePack.decisionGates.includes("charity annual return review"));
  assert.ok(clgCharityScenario?.evidencePack.expectedOutputs.filingGateStates.includes("SoFA and trustees annual report evidence satisfied"));
  const mediumScenario = parsed.goldenFilingCorpus.find((scenario) => scenario.code === "medium-audit-required");
  assert.equal(mediumScenario?.fixture.legalName, "Midlands Manufacturing Limited");
  assert.equal(mediumScenario?.expectedOutcome, "manual-handoff");
  assert.equal(mediumScenario?.fixture.auditExempt, false);
  assert.equal(mediumScenario?.fixture.manualProfessionalReviewRequired, true);
  assert.ok(mediumScenario?.evidenceTestNames.includes("FilingGoldenCorpusScenarioTests.GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence"));
  assert.ok(mediumScenario?.evidencePack.outputArtifacts.includes("auditor report evidence"));
  assert.ok(mediumScenario?.evidencePack.decisionGates.includes("auditor handoff"));
  assert.ok(mediumScenario?.evidencePack.expectedOutputs.filingGateStates.includes("normal CRO approval blocked until auditor evidence"));
  assert.equal(mediumScenario?.evidencePack.expectedOutputs.signOffPacketState, "manual-handoff");
  assert.equal(parsed.sourceLawSnapshot.sourceCount, 6);
  assert.equal(parsed.sourceLawSnapshot.contentHash, "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
  assert.equal(parsed.sourceLawTraceability[0].sourceId, "frc-frs-105");
  assert.equal(parsed.sourceLawTraceability[0].inSnapshot, true);
  assert.equal(parsed.sourceLawTraceability[0].usedBy[0], "golden-corpus:micro-ltd");
  assert.equal(parsed.sourceLawTraceability[0].releaseGateCodes[0], "qualified-accountant-review");
  assert.equal(parsed.sourceLawMaintenanceProtocol.protocolVersion, "source-law-maintenance-v1");
  assert.equal(parsed.sourceLawMaintenanceProtocol.ownerRole, "Qualified accountant and engineering");
  assert.equal(parsed.sourceLawMaintenanceProtocol.signOffGate, "source-law-change-review");
  assert.deepEqual(parsed.sourceLawMaintenanceProtocol.monitoredSourceIds, ["frc-frs-105", "frc-frs-102", "cro-guarantee-company", "charities-regulator-annual-report", "cro-medium-company", "cro-auditors-report"]);
  assert.match(parsed.sourceLawMaintenanceProtocol.failurePolicy, /Block release/);
  assert.match(parsed.sourceLawMaintenanceProtocol.acceptanceCriteria[0], /CRO/);
  assert.ok(parsed.sourceLawMaintenanceProtocol.requiredEvidence.includes("source-law-change-review-note"));
  assert.equal(parsed.sourceLawReviewLedger[0].sourceId, "frc-frs-105");
  assert.equal(parsed.sourceLawReviewLedger[0].releaseChecklistCode, "source-law-change-review");
  assert.ok(parsed.sourceLawReviewLedger[0].blocksRelease);
  assert.match(parsed.sourceLawReviewLedger[0].reviewChecks[0], /reachable/);
  assert.ok(parsed.sourceLawReviewLedger[0].requiredEvidence.includes("qualified-accountant-source-law-signoff"));
  assert.equal(parsed.revenueTaxonomyRanges[0].taxonomyKey, "irish-extension-2025-frs-102");
  assert.equal(parsed.revenueTaxonomyRanges[0].effectiveForPeriodsStartingOnOrAfter, "2024-01-01");
  assert.equal(parsed.revenueTaxonomyRanges[0].effectiveForPeriodsStartingBefore, "");
  assert.ok(parsed.revenueTaxonomyRanges[0].releaseGateCodes.includes("source-law-change-review"));
  assert.ok(parsed.revenueTaxonomyRanges[0].automatedPlatformSelectionSupported);
  assert.equal(parsed.revenueTaxonomyRanges.filter((range) => range.accountingStandard === "FRS 101").length, 1);
  assert.equal(parsed.revenueTaxonomyRanges.filter((range) => range.accountingStandard === "EU IFRS").length, 1);
  assert.ok(parsed.revenueTaxonomyRanges.find((range) => range.accountingStandard === "EU IFRS")?.releaseGateCodes.includes("manual-professional-handoff"));
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
  assert.equal(parsed.accountantAcceptanceSummary.scenarioCount, 5);
  assert.equal(parsed.accountantAcceptanceSummary.professionalSignOffRequiredCount, 5);
  assert.equal(parsed.accountantAcceptanceSummary.automatedVerifierCount, 6);
  assert.equal(parsed.accountantAcceptanceSummary.manualHandoffScenarioCount, 1);
  assert.deepEqual(parsed.accountantAcceptanceSummary.releaseBlockingScenarioCodes, ["clg-charity", "dac-small", "medium-audit-required", "micro-ltd", "small-abridged-ltd"]);
  assert.deepEqual(parsed.accountantAcceptanceSummary.requiredSignOffGates, [
    "Named qualified accountant must approve the CLG charity pack and charity evidence before real filing use.",
    "Named qualified accountant must approve the DAC generated pack before real filing use.",
    "Named qualified accountant must approve both the full accounts pack and abridged CRO pack before real filing use.",
    "Named qualified accountant must approve the generated pack before real filing use.",
    "Qualified accountant must record manual handoff acceptance before relying on outputs.",
  ]);
  assert.equal(parsed.accountantAcceptanceSummary.status, "qualified-accountant-review-required");
  assert.equal(parsed.accountantWorkflowWalkthroughProtocol.protocolVersion, "accountant-workflow-walkthrough-v1");
  assert.equal(parsed.accountantWorkflowWalkthroughProtocol.signOffGate, "golden-corpus-accountant-acceptance");
  assert.deepEqual(parsed.accountantWorkflowWalkthroughProtocol.seededScenarioCodes, ["clg-charity", "dac-small", "medium-audit-required", "micro-ltd", "small-abridged-ltd"]);
  assert.match(parsed.accountantWorkflowWalkthroughProtocol.routeSequence[0], /Dashboard/);
  assert.match(parsed.accountantWorkflowWalkthroughProtocol.acceptanceCriteria.at(-1), /outputs, gates, wording and evidence/);
  assert.ok(parsed.accountantWorkflowWalkthroughProtocol.requiredEvidence.includes("seeded golden corpus walkthrough note"));
  assert.equal(parsed.accountantJourneyAcceptanceChecklist.length, 6);
  assert.deepEqual(
    parsed.accountantJourneyAcceptanceChecklist.map((item) => item.routeCode).sort(),
    ["company-detail", "dashboard", "filing-review", "financial-statements", "period-workspace", "production-readiness"],
  );
  assert.equal(parsed.accountantJourneyAcceptanceChecklist[0].signOffGate, "golden-corpus-accountant-acceptance");
  assert.deepEqual(parsed.accountantJourneyAcceptanceChecklist[0].seededScenarioCodes, ["clg-charity", "dac-small", "medium-audit-required", "micro-ltd", "small-abridged-ltd"]);
  assert.equal(parsed.accountantJourneyAcceptanceChecklist[0].visualArtifactNames.length, 4);
  assert.match(parsed.accountantJourneyAcceptanceChecklist[2].acceptanceCriteria[0], /Period workspace/);
  assert.match(
    parsed.accountantJourneyAcceptanceChecklist.find((item) => item.routeCode === "financial-statements")?.acceptanceCriteria[0] ?? "",
    /statement preview, tax computation, source trail and directors' report/i,
  );
  assert.equal(parsed.accountantWorkflowEvidencePack[0].routeCode, "dashboard");
  assert.equal(parsed.accountantWorkflowEvidencePack[0].evidenceArtifact, "dashboard-accountant-route-acceptance-note");
  assert.deepEqual(parsed.accountantWorkflowEvidencePack[0].visualArtifactNames, parsed.accountantJourneyAcceptanceChecklist[0].visualArtifactNames);
  assert.match(parsed.accountantWorkflowEvidencePack.find((item) => item.routeCode === "filing-review")?.decisionQuestion ?? "", /external ROS\/iXBRL validation/);
  assert.match(parsed.accountantWorkflowEvidencePack.find((item) => item.routeCode === "production-readiness")?.decisionQuestion ?? "", /release blockers/);
  assert.equal(parsed.accountantWalkthroughEvidenceMatrix.length, 30);
  assert.equal(parsed.accountantWalkthroughEvidenceMatrix[0].scenarioCode, "clg-charity");
  assert.equal(parsed.accountantWalkthroughEvidenceMatrix[0].routeCode, "dashboard");
  assert.equal(parsed.accountantWalkthroughEvidenceMatrix[0].evidenceArtifact, "clg-charity-dashboard-walkthrough-note");
  assert.deepEqual(parsed.accountantWalkthroughEvidenceMatrix[0].visualArtifactNames, parsed.accountantJourneyAcceptanceChecklist[0].visualArtifactNames);
  assert.ok(parsed.accountantWalkthroughEvidenceMatrix[0].blocksRelease);
  assert.match(parsed.accountantWalkthroughEvidenceMatrix[0].acceptanceCriteria.at(-1) ?? "", /CLG charity annual reporting/);
  assert.equal(
    parsed.accountantWalkthroughEvidenceMatrix.find((item) => item.scenarioCode === "medium-audit-required" && item.routeCode === "filing-review")?.signOffPacketState,
    "manual-handoff",
  );
  assert.equal(parsed.workbenchVisualAcceptanceRegister[0].routeCode, "dashboard");
  assert.equal(parsed.workbenchVisualAcceptanceRegister[0].evidenceArtifact, "dashboard-visual-acceptance-note");
  assert.deepEqual(parsed.workbenchVisualAcceptanceRegister[0].screenshotArtifactNames, parsed.accountantJourneyAcceptanceChecklist[0].visualArtifactNames);
  assert.match(parsed.workbenchVisualAcceptanceRegister.find((item) => item.routeCode === "filing-review")?.nextAction ?? "", /evidence checklist/);
  assert.match(parsed.workbenchVisualAcceptanceRegister.find((item) => item.routeCode === "production-readiness")?.nextAction ?? "", /release blockers/);
  assert.equal(parsed.assurancePacket.packetVersion, "production-assurance-packet-v1");
  assert.equal(parsed.assurancePacket.sourceLawSnapshotHash, "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
  assert.equal(parsed.assurancePacket.goldenCorpusCovered, 5);
  assert.ok(parsed.assurancePacket.evidenceItems.includes("source-law-traceability-index"));
  assert.ok(parsed.assurancePacket.evidenceItems.includes("revenue-taxonomy-range-evidence"));
  assert.ok(parsed.assurancePacket.evidenceItems.includes("release-review-checklist"));
  assert.ok(parsed.assurancePacket.evidenceItems.includes("golden-evidence-ledger"));
  assert.ok(parsed.assurancePacket.evidenceItems.includes("golden-verifier-manifest"));
  assert.ok(parsed.assurancePacket.evidenceItems.includes("release-blocker-register"));
  assert.ok(parsed.assurancePacket.evidenceItems.includes("production-readiness-report"));
  assert.ok(parsed.assurancePacket.evidenceItems.includes("production-readiness-verification-report"));
  assert.equal(parsed.assurancePacket.releaseBlockers[0], "Qualified accountant sign-off required");
  assert.equal(parsed.productionScorecard.currentScore, 698);
  assert.equal(parsed.productionScorecard.targetScore, 700);
  assert.deepEqual(parsed.productionScorecard.categories.map((category) => category.code), [
    "architecture-documentation",
    "backend-statutory-accounting-engine",
    "frontend-accountant-workbench",
    "security-auth-tenant-platform-guardrails",
  ]);
  assert.equal(parsed.productionScorecard.categories[1].currentScore, 250);
  assert.equal(parsed.productionScorecard.categories[1].targetScore, 250);
  assert.ok(parsed.productionScorecard.categories[2].remainingGaps[0].includes("visual QA"));
  assert.ok(parsed.productionScorecard.categories[3].completionTrackCodes.includes("backend-code"));
  assert.equal(parsed.assuranceActions[0].riskRank, 0);
  assert.equal(parsed.assuranceActions[0].evidenceStage, "accountant-review-gate");
  assert.equal(parsed.completionTracks.length, 3);
  assert.equal(parsed.completionTracks[0].code, "backend-code");
  assert.equal(parsed.completionTracks[0].label, "Backend code");
  assert.deepEqual(parsed.completionTracks[0].assuranceActionCodes, [
    "qualified-accountant-signoff",
    "source-law-change-review",
    "external-ros-validation",
    "no-direct-cro-ros-submission",
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
  const frontendContractVerification = parsed.releaseVerificationManifest.find(
    (item) => item.code === "frontend-workbench-contract",
  );
  assert.equal(frontendContractVerification?.ciScope, "default-ci");
  assert.equal(frontendContractVerification?.runsInDefaultCi, true);
  assert.match(frontendContractVerification?.manualFallback ?? "", /unit, render, readiness, proxy, auth and API-client verifier output/);
  const visualSmokeVerification = parsed.releaseVerificationManifest.find((item) => item.code === "visual-smoke-light-dark");
  assert.match(visualSmokeVerification?.command ?? "", /verify-visual-smoke-artifacts/);
  assert.match(visualSmokeVerification?.command ?? "", /verify-accountant-workbench-evidence/);
  assert.equal(visualSmokeVerification?.evidenceArtifact, "artifacts/visual-smoke");
  const noDirectSubmissionVerification = parsed.releaseVerificationManifest.find((item) => item.code === "no-direct-cro-ros-submission-control");
  assert.equal(noDirectSubmissionVerification?.releaseChecklistEvidenceArtifact, "no-direct-cro-ros-submission-control");
  assert.match(noDirectSubmissionVerification?.manualFallback ?? "", /recorded workflow states only/);
  const noDirectSubmissionChecklist = parsed.releaseReviewChecklist.find((item) => item.code === "no-direct-cro-ros-submission");
  assert.equal(noDirectSubmissionChecklist?.assuranceActionCode, "no-direct-cro-ros-submission");
  assert.equal(noDirectSubmissionChecklist?.operationalGateCode, "no-direct-cro-ros-submission");
  assert.equal(noDirectSubmissionChecklist?.evidenceArtifact, "no-direct-cro-ros-submission-control");
  assert.equal(parsed.humanReleaseEvidence.length, 6);
  assert.deepEqual(parsed.humanReleaseEvidence.map((item) => item.code), [
    "visualQa",
    "sourceLawReview",
    "externalRosIxbrlValidation",
    "qualifiedAccountantAcceptance",
    "manualHandoffAcceptance",
    "monitoringProviderConfirmation",
  ]);
  const visualHumanEvidence = parsed.humanReleaseEvidence.find((item) => item.code === "visualQa");
  assert.equal(visualHumanEvidence?.templateFile, "visual-qa-signoff-template.md");
  assert.equal(visualHumanEvidence?.releaseManifestCode, "visual-smoke-light-dark");
  assert.equal(visualHumanEvidence?.status, "pending-human-evidence");
  assert.equal(visualHumanEvidence?.blocksRelease, true);
  assert.ok(visualHumanEvidence?.reviewerPickupFiles.includes("visual-smoke-evidence-report.json"));
  assert.ok(visualHumanEvidence?.reviewerPickupFiles.includes("release-evidence-reviewer-blockers.md"));
  assert.ok(visualHumanEvidence?.requiredEvidence.some((item) => item.includes("visual-qa-signoff-template.md")));
  assert.deepEqual(parsed.humanReleaseEvidenceCloseout.map((item) => item.code), [
    "pick-up-reviewer-workspace",
    "complete-human-evidence-templates",
    "run-release-evidence-verifier",
    "confirm-human-evidence-completion",
    "verify-release-artifact-pack",
  ]);
  assert.equal(parsed.humanReleaseEvidenceCloseout[0].artifact, "release-evidence-reviewer-workspace");
  assert.match(parsed.humanReleaseEvidenceCloseout[0].detail, /pending human blocker inventory/);
  assert.equal(parsed.humanReleaseEvidenceCloseout[1].artifact, "Docs/release-evidence/*.md");
  assert.match(parsed.humanReleaseEvidenceCloseout[3].detail, /6 accepted humanEvidenceCompletion rows/);
  assert.equal(parsed.humanReleaseEvidenceCloseout[4].artifact, "scripts/verify-release-artifact-pack.ps1");
  assert.equal(parsed.auditEvidenceTimeline[0].code, "data-change-capture");
  assert.equal(parsed.auditEvidenceTimeline[0].capturedWhen, "At every authenticated write before regenerated outputs can be reviewed.");
  assert.equal(parsed.auditEvidenceTimeline[1].blockingGateCodes[0], "generated-output-review");
  assert.equal(parsed.auditEvidencePack[0].code, "who-changed-what");
  assert.equal(parsed.auditEvidencePack[0].requiredArtifact, "tamper-evident-audit-log-entry");
  assert.equal(parsed.auditEvidencePack[0].retainedIn, "audit_logs");
  assert.equal(parsed.auditEvidencePack[0].failurePolicy, "Block release when a supported write path can alter filing evidence without an audit row linked into the integrity chain.");
  assert.equal(parsed.auditEvidencePack[1].requiredArtifact, "named-accountant-approval-record");
  assert.equal(parsed.operationsEvidencePack[0].code, "sentry-error-routing");
  assert.equal(parsed.operationsEvidencePack[0].requiredArtifact, "sentry-production-error-routing-check");
  assert.equal(parsed.operationsEvidencePack[2].releaseGateCode, "dependency-policy-controls");
  assert.match(parsed.operationsEvidencePack[5].command, /verify-postgres-backup/);
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
    "visual-smoke-evidence-report.json",
    "accountant-workbench-evidence-report.json",
    "28 visual smoke screenshots",
    "screenshot SHA-256 checksums",
    "screenshot PNG dimensions",
    "screenshot nonblank pixel diversity evidence",
    "per-screenshot automated theme contrast smoke evidence",
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
  assert.equal(parsed.visualQaCoverage.artifacts[0].requiredText, "Firm command centre");
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

test("parseProductionReadinessReport rejects golden legal basis source drift", () => {
  const payload = sampleReport();
  payload.goldenFilingCorpus[0].legalBasisSnapshot.sourceIds = [];

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: goldenFilingCorpus\.0\.legalBasisSnapshot\.sourceIds - missing evidence-pack source IDs: frc-frs-105/,
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

test("parseProductionReadinessReport rejects golden verifier manifest drift", () => {
  const payload = sampleReport();
  payload.goldenVerifierManifest[0] = {
    ...payload.goldenVerifierManifest[0],
    command: "dotnet test Accounts.slnx --filter WrongVerifier",
  };

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: goldenVerifierManifest\.0\.command - must mirror golden scenario verifier command/,
  );
});

test("parseProductionReadinessReport rejects golden corpus without matching accountant acceptance criteria", () => {
  const payload = sampleReport();
  payload.goldenFilingCorpus.push({
    ...payload.goldenFilingCorpus[0],
    code: "unmodelled-plc",
    label: "Unmodelled PLC",
  });
  payload.assurancePacket.goldenCorpusTotal = 6;
  payload.assurancePacket.goldenCorpusCovered = 6;

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: accountantAcceptanceCriteria - missing acceptance criteria for golden scenarios: unmodelled-plc/,
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
    /Invalid production readiness report contract: sourceLawSnapshot\.sourceCount - expected 6, received 3/,
  );

  const corpusPayload = sampleReport();
  corpusPayload.assurancePacket.goldenCorpusTotal = 6;

  assert.throws(
    () => parseProductionReadinessReport(corpusPayload),
    /Invalid production readiness report contract: assurancePacket\.goldenCorpusTotal - expected 5, received 6/,
  );

  const visualPayload = sampleReport();
  visualPayload.visualQaCoverage.expectedScreenshotCount = 7;

  assert.throws(
    () => parseProductionReadinessReport(visualPayload),
    /Invalid production readiness report contract: visualQaCoverage\.expectedScreenshotCount - expected 28, received 7/,
  );

  const visualAssurancePayload = sampleReport();
  visualAssurancePayload.assurancePacket.visualQaExpectedScreenshots = 99;

  assert.throws(
    () => parseProductionReadinessReport(visualAssurancePayload),
    /Invalid production readiness report contract: assurancePacket\.visualQaExpectedScreenshots - expected 28, received 99/,
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

test("parseProductionReadinessReport rejects visual review protocols without PNG dimension evidence", () => {
  const payload = sampleReport();
  payload.visualQaCoverage.reviewProtocol.requiredEvidence =
    payload.visualQaCoverage.reviewProtocol.requiredEvidence.filter((item) => item !== "screenshot PNG dimensions");

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: visualQaCoverage\.reviewProtocol\.requiredEvidence - must include screenshot PNG dimensions/,
  );
});

test("parseProductionReadinessReport rejects visual review protocols without nonblank pixel diversity evidence", () => {
  const payload = sampleReport();
  payload.visualQaCoverage.reviewProtocol.requiredEvidence =
    payload.visualQaCoverage.reviewProtocol.requiredEvidence.filter((item) => item !== "screenshot nonblank pixel diversity evidence");

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: visualQaCoverage\.reviewProtocol\.requiredEvidence - must include screenshot nonblank pixel diversity evidence/,
  );
});

test("parseProductionReadinessReport rejects visual review protocols without automated contrast smoke evidence", () => {
  const payload = sampleReport();
  payload.visualQaCoverage.reviewProtocol.requiredEvidence =
    payload.visualQaCoverage.reviewProtocol.requiredEvidence.filter((item) => item !== "per-screenshot automated theme contrast smoke evidence");

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: visualQaCoverage\.reviewProtocol\.requiredEvidence - must include per-screenshot automated theme contrast smoke evidence/,
  );
});

test("parseProductionReadinessReport rejects visual review protocols without verifier report evidence", () => {
  const payload = sampleReport();
  payload.visualQaCoverage.reviewProtocol.requiredEvidence =
    payload.visualQaCoverage.reviewProtocol.requiredEvidence.filter((item) => item !== "visual-smoke-evidence-report.json");

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: visualQaCoverage\.reviewProtocol\.requiredEvidence - must include visual-smoke-evidence-report\.json/,
  );
});

test("parseProductionReadinessReport rejects visual review protocols without accountant workbench evidence report", () => {
  const payload = sampleReport();
  payload.visualQaCoverage.reviewProtocol.requiredEvidence =
    payload.visualQaCoverage.reviewProtocol.requiredEvidence.filter((item) => item !== "accountant-workbench-evidence-report.json");

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: visualQaCoverage\.reviewProtocol\.requiredEvidence - must include accountant-workbench-evidence-report\.json/,
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
    /Invalid production readiness report contract: accountantWorkflowWalkthroughProtocol\.seededScenarioCodes - expected clg-charity, dac-small, medium-audit-required, micro-ltd, small-abridged-ltd, received micro-ltd/,
  );
});

test("parseProductionReadinessReport rejects accountant walkthrough protocols without financial statements review", () => {
  const payload = sampleReport();
  payload.accountantWorkflowWalkthroughProtocol.routeSequence = payload.accountantWorkflowWalkthroughProtocol.routeSequence.filter(
    (step) => !step.includes("Financial statements"),
  );

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: accountantWorkflowWalkthroughProtocol\.routeSequence - Financial statements is required/,
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

test("parseProductionReadinessReport rejects missing accountant workbench evidence report coverage", () => {
  const payload = sampleReport();
  payload.assurancePacket.evidenceItems = payload.assurancePacket.evidenceItems.filter(
    (item) => item !== "accountant-workbench-evidence-report",
  );

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: assurancePacket\.evidenceItems - accountant-workbench-evidence-report is required/,
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

test("parseProductionReadinessReport rejects accountant walkthrough matrix row drift", () => {
  const payload = sampleReport();
  payload.accountantWalkthroughEvidenceMatrix = payload.accountantWalkthroughEvidenceMatrix.filter(
    (item) => !(item.scenarioCode === "micro-ltd" && item.routeCode === "filing-review"),
  );

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: accountantWalkthroughEvidenceMatrix\.length - expected 30, received 29/,
  );
});

test("parseProductionReadinessReport rejects accountant walkthrough matrix without route screenshots", () => {
  const payload = sampleReport();
  payload.accountantWalkthroughEvidenceMatrix[0].visualArtifactNames = ["dashboard-light-desktop.png"];

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: accountantWalkthroughEvidenceMatrix\.0\.visualArtifactNames - expected dashboard-dark-desktop\.png/,
  );
});

test("parseProductionReadinessReport rejects accountant walkthrough matrix without release-blocking status", () => {
  const payload = sampleReport();
  payload.accountantWalkthroughEvidenceMatrix[0].blocksRelease = false;

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: accountantWalkthroughEvidenceMatrix\.0\.status - walkthrough rows must block release until accepted/,
  );
});

test("parseProductionReadinessReport rejects inconsistent accountant acceptance summaries", () => {
  const payload = sampleReport();
  payload.accountantAcceptanceSummary.scenarioCount = 6;

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: accountantAcceptanceSummary\.scenarioCount - expected 5, received 6/,
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

test("parseProductionReadinessReport rejects missing source-law review ledger", () => {
  const payload = sampleReport();
  delete payload.sourceLawReviewLedger;

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: sourceLawReviewLedger/,
  );
});

test("parseProductionReadinessReport rejects source-law review ledger drift", () => {
  const payload = sampleReport();
  payload.sourceLawReviewLedger = payload.sourceLawReviewLedger.slice(0, 1);

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: sourceLawReviewLedger - expected snapshot source ids only/,
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

test("parseProductionReadinessReport rejects small abridged corpus without Section 352 verifier evidence", () => {
  const payload = sampleReport();
  const scenario = payload.goldenFilingCorpus.find((item) => item.code === "small-abridged-ltd");
  assert.ok(scenario);
  scenario.evidenceTestNames = scenario.evidenceTestNames.filter(
    (name) => name !== "FilingGoldenCorpusScenarioTests.GoldenCorpus_SmallAbridgedLtd_EmitsFullAccountsAbridgedCroPackIxbrlAndReadiness",
  );
  scenario.evidenceVerifiers = scenario.evidenceVerifiers.filter(
    (verifier) => verifier.name !== "FilingGoldenCorpusScenarioTests.GoldenCorpus_SmallAbridgedLtd_EmitsFullAccountsAbridgedCroPackIxbrlAndReadiness",
  );

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: goldenFilingCorpus\.small-abridged-ltd - dedicated small abridged verifier is required/,
  );
});

test("parseProductionReadinessReport rejects production sprint corpus without CLG charity and medium audit-required evidence", () => {
  const payload = sampleReport();
  payload.goldenFilingCorpus = payload.goldenFilingCorpus.filter(
    (scenario) => !["clg-charity", "medium-audit-required"].includes(scenario.code),
  );
  payload.accountantAcceptanceCriteria = payload.accountantAcceptanceCriteria.filter(
    (criterion) => !["clg-charity", "medium-audit-required"].includes(criterion.scenarioCode),
  );
  payload.goldenEvidenceLedger = payload.goldenEvidenceLedger.filter(
    (entry) => !["clg-charity", "medium-audit-required"].includes(entry.scenarioCode),
  );
  payload.assurancePacket.goldenCorpusCovered = 3;
  payload.assurancePacket.goldenCorpusTotal = 3;

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: goldenFilingCorpus - missing required production sprint scenarios: clg-charity, medium-audit-required/,
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

test("parseProductionReadinessReport rejects a missing production scorecard", () => {
  const payload = sampleReport();
  delete payload.productionScorecard;

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: productionScorecard - Invalid input: expected object, received undefined/,
  );
});

test("parseProductionReadinessReport rejects scorecard totals that do not match categories", () => {
  const payload = sampleReport();
  payload.productionScorecard.currentScore = 491;

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: productionScorecard\.currentScore - expected 698, received 491/,
  );
});

test("parseProductionReadinessReport rejects scorecard categories with unknown release blockers", () => {
  const payload = sampleReport();
  payload.productionScorecard.categories[0].releaseBlockerCodes.push("unknown-track:unknown-action");

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: productionScorecard\.categories\.0\.releaseBlockerCodes - unknown release blocker unknown-track:unknown-action/,
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

test("parseProductionReadinessReport rejects release verification manifests without CI machine evidence coverage", () => {
  const payload = sampleReport();
  payload.releaseVerificationManifest = payload.releaseVerificationManifest.filter(
    (item) => item.code !== "ci-machine-evidence-pack",
  );

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: releaseVerificationManifest - missing default CI verification commands: ci-machine-evidence-pack/,
  );
});

test("parseProductionReadinessReport rejects release verification manifests without no-direct filing control coverage", () => {
  const payload = sampleReport();
  payload.releaseVerificationManifest = payload.releaseVerificationManifest.filter(
    (item) => item.code !== "no-direct-cro-ros-submission-control",
  );

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: releaseVerificationManifest - missing manual release verification commands: no-direct-cro-ros-submission-control/,
  );
});

test("parseProductionReadinessReport rejects malformed CI machine evidence manifest rows", () => {
  const payload = sampleReport();
  const ciMachineEvidencePack = payload.releaseVerificationManifest.find(
    (item) => item.code === "ci-machine-evidence-pack",
  );
  ciMachineEvidencePack.evidenceArtifact = "wrong-artifact";

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: releaseVerificationManifest\.ci-machine-evidence-pack - default CI verifier and artifact are required/,
  );
});

test("parseProductionReadinessReport rejects release verification manifest that misses blocking checklist evidence", () => {
  const payload = sampleReport();
  payload.releaseVerificationManifest = payload.releaseVerificationManifest.map((item) =>
    item.releaseChecklistEvidenceArtifact === "named-accountant-approval-record"
      ? { ...item, releaseChecklistEvidenceArtifact: "source-law-change-review-note" }
      : item,
  );

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: releaseVerificationManifest - missing verification coverage for blocking checklist evidence: named-accountant-approval-record/,
  );
});

test("parseProductionReadinessReport rejects missing human release evidence gates", () => {
  const payload = sampleReport();
  payload.humanReleaseEvidence = payload.humanReleaseEvidence.filter((item) => item.code !== "visualQa");

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: humanReleaseEvidence - missing required gates: visualQa/,
  );
});

test("parseProductionReadinessReport rejects human release evidence manifest drift", () => {
  const payload = sampleReport();
  payload.humanReleaseEvidence[0].releaseManifestCode = "missing-manifest-gate";

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: humanReleaseEvidence\.0\.releaseManifestCode - must reference a release verification manifest item/,
  );
});

test("parseProductionReadinessReport rejects human release evidence without reviewer pickup files", () => {
  const payload = sampleReport();
  payload.humanReleaseEvidence[0].reviewerPickupFiles = payload.humanReleaseEvidence[0].reviewerPickupFiles.filter((item) => item !== "release-evidence-reviewer-blockers.md");

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: humanReleaseEvidence\.0\.reviewerPickupFiles - must include release-evidence-reviewer-blockers\.md/,
  );
});

test("parseProductionReadinessReport rejects human evidence closeout sequence drift", () => {
  const payload = sampleReport();
  payload.humanReleaseEvidenceCloseout = [
    payload.humanReleaseEvidenceCloseout[1],
    payload.humanReleaseEvidenceCloseout[0],
    ...payload.humanReleaseEvidenceCloseout.slice(2),
  ];

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: humanReleaseEvidenceCloseout\.0\.code - steps must remain in release-operator sequence/,
  );
});

test("parseProductionReadinessReport rejects human evidence closeout artifact drift", () => {
  const payload = sampleReport();
  payload.humanReleaseEvidenceCloseout[4].artifact = "scripts/manual-final-pack-check.ps1";

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: humanReleaseEvidenceCloseout\.4\.artifact - must reference scripts\/verify-release-artifact-pack\.ps1/,
  );
});

function productionScorecard() {
  return {
    currentScore: 698,
    targetScore: 700,
    status: "review-required",
    nextGate: "Complete source-law review, named visual QA, monitoring-provider confirmation, manual handoff and qualified-accountant acceptance evidence.",
    categories: [
      {
        code: "architecture-documentation",
        label: "Architecture and documentation",
        currentScore: 99,
        targetScore: 100,
        status: "release-evidence-required",
        currentEvidence: [
          "CLAUDE.md is canonical.",
          "AGENTS.md carries the active handoff.",
          "source-law-review-template.md is checked in and release-verifier covered.",
          "verify-release-artifact-pack.ps1 is documented for exact release evidence packs with checksum inventory.",
          "Release evidence reviewer workspace verification inventories pending human-evidence blockers and rejects prepared human templates with prefilled top-level reviewer/accountant identity, signature or acceptance checkbox fields.",
        ],
        remainingGaps: ["Complete checked-in release evidence templates with named reviewers, including source-law-review-template.md."],
        completionTrackCodes: ["backend-code", "frontend-ui-ux", "frontend-code"],
        releaseBlockerCodes: ["backend-code:source-law-change-review", "frontend-ui-ux:light-dark-visual-regression"],
      },
      {
        code: "backend-statutory-accounting-engine",
        label: "Backend statutory/accounting engine",
        currentScore: 250,
        targetScore: 250,
        status: "qualified-accountant-review-required",
        currentEvidence: [
          "Golden filing corpus covers the production scenarios.",
          "Qualified-accountant acceptance evidence uses canonical golden corpus scenario codes.",
          "verify-release-evidence.ps1 rejects qualified-accountant acceptance unless every golden scenario decision and route evidence acceptance row is explicitly accepted.",
          "Qualified-accountant acceptance requires explicit accepted scenario scope cells for outputs, gates, source-law evidence, wording and workbench journey before a scenario decision can pass.",
          "Qualified-accountant scenario walkthrough rows must match the exact qualified-accountant-walkthrough-ledger anchor for every canonical golden corpus scenario.",
          "Qualified-accountant route walkthrough rows require exact yes decision-question cells and exact accepted evidence cells before route acceptance can pass.",
          "Qualified-accountant route decision-question cells reject accepted-style ambiguous text so professional evidence acceptance stays in the dedicated evidence column.",
          "Qualified-accountant route walkthrough rows require route-specific accountant-workbench evidence anchors for every accepted route.",
          "Qualified-accountant route acceptance requires a real retained workbench evidence reference for every accepted route.",
          "Qualified-accountant route walkthrough notes must match the exact qualified-accountant-route-walkthrough anchor for every accepted route.",
          "Release evidence reviewer workspaces now prefill qualified-accountant scenario walkthrough, route workbench evidence and route walkthrough anchors while leaving all professional acceptance cells blank.",
          "Qualified-accountant acceptance top-level evidence rejects placeholder accountant name, qualification, reviewer capacity and signature fields before professional sign-off evidence can pass.",
          "External ROS/iXBRL validation evidence has template and verifier checks for real references, retained taxonomy package references, accepted/remediated warnings and accepted decisions.",
          "External ROS/iXBRL validation and taxonomy references require the matching golden corpus scenario code before acceptance can pass.",
          "External ROS/iXBRL validation rows must match exact retained external validation and taxonomy package ledger anchors for every canonical golden corpus scenario.",
          "External ROS/iXBRL validation warnings/errors require exact none, accepted or remediated values, and scenario decisions require exact accepted values before evidence can pass.",
          "External ROS/iXBRL validation top-level evidence rejects placeholder provider, environment, run/reference, report, taxonomy and company/period fields, and requires a retained XHTML, HTML or ZIP iXBRL artifact name.",
          "Release evidence reviewer workspaces now prefill external ROS/iXBRL validation and taxonomy package anchors for every golden corpus scenario while leaving artifact hash, warnings/errors and decision cells blank.",
          "Source-law review evidence has template and verifier checks for concrete URL reachability, dated or not-dated effective-date review, guidance comparison, platform impact classification and exact accepted decisions.",
          "Source-law review platform impact cells require exact no change, reflected or blocking values before review evidence can pass.",
          "Source-law review notes must match the exact source-law-review-ledger anchor for every monitored source before acceptance can pass.",
          "Release evidence reviewer workspaces now prefill source-law snapshot fingerprint, content hash and per-source review ledger note anchors from the retained production-readiness report while leaving source review decision cells blank.",
          "Source-law review top-level evidence requires an exact source-law-snapshot-fingerprint retained evidence anchor plus real reviewer and qualified-accountant identity, signature and sign-off fields before review evidence can pass.",
          "Manual handoff acceptance evidence has template and verifier checks for real evidence references and accepted reviewer decisions.",
          "Manual handoff scenario and unsupported-path decisions require exact accepted reviewer decisions before acceptance evidence can pass.",
          "Manual handoff evidence references require the matching scenario or unsupported-path code before acceptance can pass.",
          "Manual handoff evidence rows must match exact retained auditor-report, handoff-note, readiness-snapshot and unsupported-path anchors before acceptance can pass.",
          "Release evidence reviewer workspaces now prefill manual handoff scenario and unsupported-path evidence anchors while leaving scenario and reviewer decision cells blank.",
          "CI retains production-readiness-report.json from the live smoke stack with source-law, golden corpus, scorecard and release blocker evidence.",
        ],
        remainingGaps: ["Run and retain verified source-law, qualified-accountant acceptance, external ROS/iXBRL validation, and manual handoff evidence across every canonical golden corpus scenario."],
        completionTrackCodes: ["backend-code"],
        releaseBlockerCodes: [
          "backend-code:qualified-accountant-signoff",
          "backend-code:external-ros-validation",
          "backend-code:accountant-acceptance-walkthrough",
        ],
      },
      {
        code: "frontend-accountant-workbench",
        label: "Frontend accountant workbench",
        currentScore: 199,
        targetScore: 200,
        status: "visual-acceptance-required",
        currentEvidence: [
          "Visual smoke plan covers the accountant journey.",
          "visual-smoke-evidence-report.json proves screenshot hash, byte-size, PNG dimension, nonblank pixel diversity, per-screenshot layout-check pass results, automated theme-contrast smoke results and route/theme/viewport coverage.",
          "visual-qa-signoff-template.md and verify-release-evidence.ps1 require reviewers to record visual smoke nonblank pixel and contrast metrics before visual QA evidence can pass.",
          "Visual QA sign-off requires exact pass decisions for every route across desktop light, desktop dark, mobile light and mobile dark captures.",
          "Visual QA route capture cells reject accepted-style ambiguous text so reviewer limitations must stay in retained route notes or references.",
          "Visual QA route notes must match the exact visual-smoke-evidence-report.json routeAcceptance anchor for every route before sign-off evidence can pass.",
          "Release evidence reviewer workspaces now prefill visual QA route note anchors from visual-smoke-evidence-report.json while leaving all route pass/fail cells blank for named human review.",
          "Visual QA release evidence requires exact visual-smoke manifest, visual evidence report and accountant workbench evidence report filenames before sign-off evidence can pass.",
          "Visual QA top-level evidence rejects placeholder reviewer name, reviewer role and reviewer signature fields before human visual sign-off evidence can pass.",
          "accountant-workbench-evidence-report.json proves route workflow-stage, review-check, expected accountant decision text and qualified-accountant route acceptance coverage.",
          "Release artifact and CI machine evidence pack verifiers require exact visual-smoke top-level themes, viewports, planned viewport dimensions, layout/contrast result counts, minimum contrast ratio, retained screenshot bytes and per-route coverage before retained visual evidence can pass.",
          "Release artifact and CI machine evidence pack verifiers require the exact visual-smoke screenshot matrix for every route, theme and viewport, including route keys, file names, expected accountant decision text and required-review status.",
          "Release artifact and CI machine evidence pack verifiers cross-check visual-smoke-manifest.json route audits and screenshot rows against visual-smoke-evidence-report.json before retained visual evidence can pass.",
          "Release artifact and CI machine evidence pack verifiers require every visual-smoke screenshot row to retain a positive byte size and canonical sha256:<64 lowercase hex> checksum before retained visual evidence can pass.",
          "Release artifact and CI machine evidence pack verifiers require every visual-smoke screenshot row to match a retained PNG file by file name, byte size and sha256 checksum before retained visual evidence can pass.",
          "Release artifact and CI machine evidence pack verifiers require exact accountant-workbench route acceptance names, route keys, expected decision text and per-route acceptance evidence ids for every workbench route.",
          "Release artifact and CI machine evidence pack verifiers require exact accountant-workbench route acceptance labels, screenshot-review evidence anchors and required-review status for every workbench route.",
          "Release artifact and CI machine evidence pack verifiers require exact accountant-workbench route readiness screenshot counts, layout-check counts, contrast counts, minimum contrast ratios, required-review status and required review checks for every workbench route.",
          "Release artifact and CI machine evidence pack verifiers require exact accountant-workbench route workflow-stage coverage and light/dark desktop/mobile theme-viewport coverage before retained route readiness evidence can pass.",
          "Release artifact and CI machine evidence pack verifiers require exact accountant-workbench required coverage for workflow stages, themes, viewports, review checks, layout checks, expected-text checks, layout/contrast evidence and retained visual evidence files.",
          "Frontend parser invariants now require the CI machine evidence pack, production smoke, readiness verification, visual smoke and manual release-verification rows before rendering readiness data.",
        ],
        remainingGaps: ["Complete named visual QA review against the light/dark desktop/mobile screenshot manifest and visual-smoke-evidence-report.json."],
        completionTrackCodes: ["frontend-ui-ux", "frontend-code"],
        releaseBlockerCodes: [
          "frontend-ui-ux:light-dark-visual-regression",
          "frontend-ui-ux:accountant-acceptance-walkthrough",
          "frontend-code:light-dark-visual-regression",
        ],
      },
      {
        code: "security-auth-tenant-platform-guardrails",
        label: "Security/auth/tenant/platform guardrails",
        currentScore: 150,
        targetScore: 150,
        status: "operator-confirmation-required",
        currentEvidence: [
          "Session, CSRF, tenant and production safety evidence exists.",
          "CI retains no-direct-filing-submission-report.json for the no-direct CRO/ROS submission verifier.",
          "no-direct-filing-submission-report.json records release candidate commit/run identity and pack verifiers reject stale no-direct evidence.",
          "Release artifact pack verifier validates operational, workspace verification and reviewer handoff reports together.",
          "release-artifact-pack-report.json records release candidate identity, per-report and reviewer-handoff SHA-256/byte-size evidence, plus a release-evidence workspace summary with the 21-file prepared workspace, six pending human blockers, six unassigned reviewer assignment rows, reviewerAssignmentPickupFileGuidanceCount and the per-gate reviewerAssignmentPickupFiles inventory.",
          "ci-machine-evidence-pack-report.json records exact commit/run identity, machine-evidence SHA-256 inventory and a prepared reviewer-workspace summary with the 21-file workspace plus six unassigned reviewer assignment rows with complete per-gate pickup-file guidance.",
          "verify-production-readiness-report.ps1 now requires default-CI and manual release manifest rows, including the no-direct CRO/ROS control, CI machine evidence pack and release artifact pack.",
          "verify-release-artifact-pack.ps1 now rejects release packs unless production-readiness-verification-report.json proves every required default-CI and manual release manifest row.",
          "verify-release-artifact-pack.ps1 and verify-ci-machine-evidence-pack.ps1 now reject visual evidence packs unless visual-smoke-evidence-report.json carries planned PNG viewport dimensions, passed layout-check results and automated theme-contrast smoke results for every screenshot.",
          "verify-release-evidence.ps1 now rejects completed human evidence when release candidate identity, UTC timestamps, SHA-256 digests, external iXBRL artifact hashes, or monitoring log confirmation fields are malformed.",
          "verify-release-evidence.ps1 emits a consistent releaseCandidate identity for all six human evidence templates, and verify-release-artifact-pack.ps1 rejects packs whose release-evidence-report.json identity does not match the pack CommitSha and GitHubActionsRunUrl.",
          "verify-release-evidence.ps1 emits SHA-256/byte-size manifest entries for all six human release-evidence templates, and verify-release-artifact-pack.ps1 requires those completed templates to be retained in the pack with matching hashes.",
          "verify-release-artifact-pack.ps1 independently parses release-evidence-workspace-verification-report.json, requires the same release candidate and exact 21-file prepared workspace inventory including production-readiness-verification-report.json, the pending reviewer assignment ledger and its six-row reviewer assignment inventory, and manifests retained reviewer handoff files with SHA-256/byte-size evidence.",
          "Monitoring-provider confirmation evidence requires real provider/event/correlation references, an HTTPS provider base URL, a matched structured-log smoke line and an explicit accepted operator decision.",
          "Release evidence reviewer workspaces now prefill monitoring provider machine evidence from retained CI smoke/log reports while leaving provider confirmation, operator identity, decision and signature fields blank.",
        ],
        remainingGaps: ["Confirm the controlled monitoring smoke event inside the configured provider and retain the full release-artifact-pack-report.json after release-evidence-report.json is completed with named human sign-offs."],
        completionTrackCodes: ["backend-code"],
        releaseBlockerCodes: ["backend-code:source-law-change-review", "backend-code:external-ros-validation"],
      },
    ],
  };
}

function humanReleaseEvidence() {
  return [
    humanEvidenceGate("visualQa", "Visual QA sign-off", "visual-qa-signoff-template.md", "Named visual QA reviewer", "visual-qa-screenshot-review", "visual-qa-screenshot-review", "visual-smoke-light-dark", "light-dark-desktop-mobile-screenshot-review"),
    humanEvidenceGate("sourceLawReview", "Source-law review sign-off", "source-law-review-template.md", "Named source-law reviewer plus qualified accountant", "source-law-change-review", "source-law-change-review", "source-law-change-review", "source-law-change-review-note"),
    humanEvidenceGate("externalRosIxbrlValidation", "External ROS/iXBRL validation", "external-ros-ixbrl-validation-template.md", "External ROS/iXBRL validation reviewer", "external-ros-validation-evidence", "external-ros-validation-evidence", "external-ros-validation-evidence", "external-ros-validation-reference"),
    humanEvidenceGate("qualifiedAccountantAcceptance", "Qualified-accountant acceptance", "qualified-accountant-acceptance-template.md", "Named qualified accountant", "qualified-accountant-final-signoff", "accountant-final-signoff", "qualified-accountant-final-signoff", "named-accountant-approval-record"),
    humanEvidenceGate("manualHandoffAcceptance", "Manual handoff acceptance", "manual-handoff-acceptance-template.md", "Named manual handoff reviewer", "manual-accountant-acceptance", "golden-corpus-accountant-acceptance", "manual-accountant-acceptance", "signed-golden-corpus-acceptance-note"),
    humanEvidenceGate("monitoringProviderConfirmation", "Monitoring-provider confirmation", "monitoring-provider-confirmation-template.md", "Named release operator", "production-monitoring", "production-smoke-and-backup", "production-stack-smoke", "ci-production-stack-smoke-and-backup-restore"),
  ];
}

function humanReleaseEvidenceCloseout() {
  return [
    {
      code: "pick-up-reviewer-workspace",
      label: "Pick up reviewer workspace",
      sequence: 1,
      detail: "Download the release-evidence-reviewer-workspace artifact and inspect release-evidence-reviewer-index.md, release-evidence-reviewer-completion.json and pending human blocker inventory before assigning reviewers.",
      artifact: "release-evidence-reviewer-workspace",
      blocksRelease: true,
    },
    {
      code: "complete-human-evidence-templates",
      label: "Complete templates",
      sequence: 2,
      detail: "Complete 6 retained Markdown templates with named reviewers, UTC timestamps, retained evidence references, accepted decisions and signatures.",
      artifact: "Docs/release-evidence/*.md",
      blocksRelease: true,
    },
    {
      code: "run-release-evidence-verifier",
      label: "Run release evidence verifier",
      sequence: 3,
      detail: "Generate release-evidence-report.json for the exact candidate after the human templates are complete.",
      artifact: "scripts/verify-release-evidence.ps1",
      blocksRelease: true,
    },
    {
      code: "confirm-human-evidence-completion",
      label: "Confirm human completion",
      sequence: 4,
      detail: "Confirm 6 accepted humanEvidenceCompletion rows with zero blocking failures in release-evidence-report.json.",
      artifact: "release-evidence-report.json",
      blocksRelease: true,
    },
    {
      code: "verify-release-artifact-pack",
      label: "Verify final artifact pack",
      sequence: 5,
      detail: "Run the final pack verifier against the same commit SHA and GitHub Actions run URL.",
      artifact: "scripts/verify-release-artifact-pack.ps1",
      blocksRelease: true,
    },
  ];
}

function humanEvidenceGate(code, label, templateFile, requiredReviewerRole, signOffGate, releaseChecklistCode, releaseManifestCode, evidenceArtifact) {
  return {
    code,
    label,
    templateFile,
    requiredReviewerRole,
    status: "pending-human-evidence",
    signOffGate,
    releaseChecklistCode,
    releaseManifestCode,
    evidenceArtifact,
    blocksRelease: true,
    reviewerPickupFiles: reviewerPickupFilesForGate(code, templateFile),
    requiredEvidence: [`${templateFile} completed by a named reviewer`, `${evidenceArtifact} retained in the release pack`],
    nextAction: `Complete and verify ${templateFile} for the exact release candidate.`,
  };
}

function reviewerPickupFilesForGate(code, templateFile) {
  const commonBlocker = "release-evidence-reviewer-blockers.md";
  const filesByCode = {
    visualQa: [templateFile, "visual-smoke-manifest.json", "visual-smoke-evidence-report.json", "accountant-workbench-evidence-report.json", commonBlocker],
    sourceLawReview: [templateFile, "production-readiness-report.json", "production-readiness-verification-report.json", commonBlocker],
    externalRosIxbrlValidation: [templateFile, "production-readiness-report.json", commonBlocker],
    qualifiedAccountantAcceptance: [templateFile, "production-readiness-report.json", "accountant-workbench-evidence-report.json", commonBlocker],
    manualHandoffAcceptance: [templateFile, "production-readiness-report.json", commonBlocker],
    monitoringProviderConfirmation: [templateFile, "monitoring-error-routing-report.json", "structured-log-report.json", commonBlocker],
  };

  return filesByCode[code] ?? [templateFile, commonBlocker];
}

function sampleReport() {
  return withGoldenLegalBasisSnapshots({
    generatedAt: "2026-07-04T12:00:00Z",
    overallStatus: "review-required",
    companiesInDatabase: 1,
    periodsInDatabase: 1,
    sourceLawSnapshot: {
      snapshotDate: "2026-07-03",
      snapshotVersion: "irish-statutory-accounts-sources-2026-07-03",
      contentHash: "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
      sourceCount: 6,
      sources: [
        source("frc-frs-105", "FRC FRS 105 current edition and amendments"),
        source("frc-frs-102", "FRC FRS 102 current edition and amendments"),
        source("cro-guarantee-company", "CRO guarantee company financial statements requirements"),
        source("charities-regulator-annual-report", "Charities Regulator annual report guidance"),
        source("cro-medium-company", "CRO medium company financial statements requirements"),
        source("cro-auditors-report", "CRO auditors report requirements"),
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
          "golden-corpus:small-abridged-ltd",
          "golden-corpus:clg-charity",
          "golden-corpus:medium-audit-required",
          "accountant-acceptance:dac-small",
          "accountant-acceptance:small-abridged-ltd",
          "accountant-acceptance:clg-charity",
          "accountant-acceptance:medium-audit-required",
        ],
        releaseGateCodes: ["qualified-accountant-review"],
      },
      {
        ...source("cro-guarantee-company", "CRO guarantee company financial statements requirements"),
        inSnapshot: true,
        usedBy: [
          "golden-corpus:clg-charity",
          "statutory-rule-matrix:clg-charity",
          "accountant-acceptance:clg-charity",
        ],
        releaseGateCodes: ["qualified-accountant-review", "charity-annual-return-review"],
      },
      {
        ...source("charities-regulator-annual-report", "Charities Regulator annual report guidance"),
        inSnapshot: true,
        usedBy: [
          "golden-corpus:clg-charity",
          "statutory-rule-matrix:clg-charity",
          "accountant-acceptance:clg-charity",
        ],
        releaseGateCodes: ["charity-annual-return-review"],
      },
      {
        ...source("cro-medium-company", "CRO medium company financial statements requirements"),
        inSnapshot: true,
        usedBy: [
          "golden-corpus:medium-audit-required",
          "statutory-rule-matrix:medium-audit-required",
          "accountant-acceptance:medium-audit-required",
        ],
        releaseGateCodes: ["auditor-handoff", "manual-professional-handoff"],
      },
      {
        ...source("cro-auditors-report", "CRO auditors report requirements"),
        inSnapshot: true,
        usedBy: [
          "golden-corpus:medium-audit-required",
          "statutory-rule-matrix:medium-audit-required",
          "accountant-acceptance:medium-audit-required",
        ],
        releaseGateCodes: ["auditor-handoff"],
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
      monitoredSourceIds: ["frc-frs-105", "frc-frs-102", "cro-guarantee-company", "charities-regulator-annual-report", "cro-medium-company", "cro-auditors-report"],
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
    sourceLawReviewLedger: [
      sourceLawReviewEntry("frc-frs-105", "FRC FRS 105 current edition and amendments"),
      sourceLawReviewEntry("frc-frs-102", "FRC FRS 102 current edition and amendments"),
      sourceLawReviewEntry("cro-guarantee-company", "CRO guarantee company financial statements requirements"),
      sourceLawReviewEntry("charities-regulator-annual-report", "Charities Regulator annual report guidance"),
      sourceLawReviewEntry("cro-medium-company", "CRO medium company financial statements requirements"),
      sourceLawReviewEntry("cro-auditors-report", "CRO auditors report requirements"),
    ],
    revenueTaxonomyRanges: [
      {
        taxonomyKey: "irish-extension-2025-frs-102",
        accountingStandard: "FRS 102",
        taxonomyDate: "2025-01-01",
        label: "Irish Extension 2025 FRS 102 taxonomy accepted by Revenue",
        schemaRef: "https://xbrl.frc.org.uk/ireland/FRS-102/2025-01-01/ie-FRS-102-2025-01-01.xsd",
        acceptedByRevenue: true,
        automatedPlatformSelectionSupported: true,
        effectiveForPeriodsStartingOnOrAfter: "2024-01-01",
        effectiveForPeriodsStartingBefore: "",
        sourceIds: ["frc-frs-102"],
        releaseGateCodes: ["external-ros-validation", "ixbrl-taxonomy-selection", "source-law-change-review"],
      },
      {
        taxonomyKey: "irish-extension-2023-frs-102",
        accountingStandard: "FRS 102",
        taxonomyDate: "2023-01-01",
        label: "Irish Extension 2023 FRS 102 taxonomy accepted by Revenue",
        schemaRef: "https://xbrl.frc.org.uk/ireland/FRS-102/2023-01-01/ie-FRS-102-2023-01-01.xsd",
        acceptedByRevenue: true,
        automatedPlatformSelectionSupported: true,
        effectiveForPeriodsStartingOnOrAfter: "2023-01-01",
        effectiveForPeriodsStartingBefore: "2024-01-01",
        sourceIds: ["frc-frs-102"],
        releaseGateCodes: ["external-ros-validation", "ixbrl-taxonomy-selection", "source-law-change-review"],
      },
      {
        taxonomyKey: "irish-extension-2022-frs-102",
        accountingStandard: "FRS 102",
        taxonomyDate: "2022-01-01",
        label: "Irish Extension 2022 FRS 102 taxonomy accepted by Revenue",
        schemaRef: "https://xbrl.frc.org.uk/ireland/FRS-102/2022-01-01/ie-FRS-102-2022-01-01.xsd",
        acceptedByRevenue: true,
        automatedPlatformSelectionSupported: true,
        effectiveForPeriodsStartingOnOrAfter: "2019-01-01",
        effectiveForPeriodsStartingBefore: "2023-01-01",
        sourceIds: ["frc-frs-102"],
        releaseGateCodes: ["external-ros-validation", "ixbrl-taxonomy-selection", "source-law-change-review"],
      },
      {
        taxonomyKey: "irish-extension-2025-frs-101",
        accountingStandard: "FRS 101",
        taxonomyDate: "2025-01-01",
        label: "Irish Extension 2025 FRS 101 taxonomy accepted by Revenue",
        schemaRef: "https://xbrl.frc.org.uk/ireland/FRS-101/2025-01-01/ie-FRS-101-2025-01-01.xsd",
        acceptedByRevenue: true,
        automatedPlatformSelectionSupported: false,
        effectiveForPeriodsStartingOnOrAfter: "2024-01-01",
        effectiveForPeriodsStartingBefore: "",
        sourceIds: ["frc-frs-102"],
        releaseGateCodes: ["external-ros-validation", "ixbrl-taxonomy-selection", "source-law-change-review", "manual-professional-handoff"],
      },
      {
        taxonomyKey: "irish-extension-2025-ifrs",
        accountingStandard: "EU IFRS",
        taxonomyDate: "2025-01-01",
        label: "EU IFRS Irish Extension 2025 taxonomy accepted by Revenue",
        schemaRef: "https://xbrl.frc.org.uk/ireland/IFRS/2025-01-01/ie-IFRS-2025-01-01.xsd",
        acceptedByRevenue: true,
        automatedPlatformSelectionSupported: false,
        effectiveForPeriodsStartingOnOrAfter: "2024-01-01",
        effectiveForPeriodsStartingBefore: "",
        sourceIds: ["frc-frs-102"],
        releaseGateCodes: ["external-ros-validation", "ixbrl-taxonomy-selection", "source-law-change-review", "manual-professional-handoff"],
      },
    ],
    assurancePacket: {
      packetId: "assurance-sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
      packetVersion: "production-assurance-packet-v1",
      status: "review-required",
      sourceLawSnapshotHash: "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
      goldenCorpusCovered: 5,
      goldenCorpusTotal: 5,
      statutoryRuleMatrixPaths: 1,
      statutoryRuleCoverageFamilies: 1,
      visualQaExpectedScreenshots: expectedVisualSmokeScreenshotCount(),
      requiredOperationalGates: 1,
      openCriticalActions: 3,
      evidenceItems: ["source-law-snapshot-fingerprint", "source-law-traceability-index", "source-law-maintenance-protocol", "source-law-review-ledger", "revenue-taxonomy-range-evidence", "golden-filing-corpus", "golden-evidence-ledger", "golden-verifier-manifest", "audit-evidence-timeline", "production-audit-evidence-pack", "operations-evidence-pack", "production-readiness-report", "production-readiness-verification-report", "visual-smoke-screenshots", "accountant-workbench-evidence-report", "release-blocker-register", "release-review-checklist", "release-verification-manifest", "human-release-evidence", "accountant-acceptance-summary", "accountant-workflow-walkthrough-protocol", "accountant-journey-acceptance-checklist", "accountant-workflow-evidence-pack", "accountant-walkthrough-evidence-matrix", "workbench-visual-acceptance-register", "production-completion-map", "production-scorecard"],
      releaseBlockers: [
        "Qualified accountant sign-off required",
        "Source-law change review required",
        "External ROS/iXBRL validation required",
        "Accountant acceptance walkthrough required",
        "Light/dark visual regression required",
      ],
    },
    productionScorecard: productionScorecard(),
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
        scenarioCode: "small-abridged-ltd",
        label: "Small abridged LTD accountant acceptance",
        required: true,
        acceptanceStatus: "qualified-accountant-review-required",
        reviewScope: ["Full accounts PDF", "Abridged CRO pack", "Section 352 evidence", "iXBRL XML", "tax computation", "notes", "signatory gates"],
        requiredEvidence: ["Named qualified-accountant approval recorded against full and abridged generated packs."],
        requiredSignOffGate: "Named qualified accountant must approve both the full accounts pack and abridged CRO pack before real filing use.",
        evidenceVerifiers: [
          {
            name: "AccountsWorkflowTests.GoldenPath_SmallAuditExemptCompany_MixedAccrualSetBalancesThroughPdfAndIxbrl",
            command: "dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~AccountsWorkflowTests.GoldenPath_SmallAuditExemptCompany_MixedAccrualSetBalancesThroughPdfAndIxbrl",
            ciScope: "default-ci",
            runsInDefaultCi: true,
            environment: "EF Core InMemory golden fixture; CI also runs the broader backend suite on Linux",
            evidenceLevel: "end-to-end golden filing scenario",
          },
          {
            name: "FilingGoldenCorpusScenarioTests.GoldenCorpus_SmallAbridgedLtd_EmitsFullAccountsAbridgedCroPackIxbrlAndReadiness",
            command: "dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~FilingGoldenCorpusScenarioTests.GoldenCorpus_SmallAbridgedLtd_EmitsFullAccountsAbridgedCroPackIxbrlAndReadiness",
            ciScope: "default-ci",
            runsInDefaultCi: true,
            environment: "EF Core InMemory golden fixture; CI also runs the broader backend suite on Linux",
            evidenceLevel: "end-to-end golden filing scenario",
          },
        ],
        sources: [source("frc-frs-102", "FRC FRS 102 current edition and amendments")],
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
      {
        scenarioCode: "clg-charity",
        label: "CLG charity accountant acceptance",
        required: true,
        acceptanceStatus: "qualified-accountant-review-required",
        reviewScope: ["CLG accounts PDF", "charity number", "SoFA and trustees annual report evidence", "iXBRL XML", "tax computation", "charity notes", "charity annual return review gates"],
        requiredEvidence: [
          "Named qualified-accountant approval recorded against the CLG charity pack.",
          "Charity annual report evidence reviewed before charity filing state advances.",
        ],
        requiredSignOffGate: "Named qualified accountant must approve the CLG charity pack and charity evidence before real filing use.",
        evidenceVerifiers: [
          {
            name: "FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness",
            command: "dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness",
            ciScope: "default-ci",
            runsInDefaultCi: true,
            environment: "EF Core InMemory golden fixture; CI also runs the broader backend suite on Linux",
            evidenceLevel: "end-to-end golden filing scenario",
          },
        ],
        sources: [
          source("cro-guarantee-company", "CRO guarantee company financial statements requirements"),
          source("charities-regulator-annual-report", "Charities Regulator annual report guidance"),
          source("frc-frs-102", "FRC FRS 102 current edition and amendments"),
        ],
      },
      {
        scenarioCode: "medium-audit-required",
        label: "Medium audit-required accountant acceptance",
        required: true,
        acceptanceStatus: "qualified-accountant-review-required",
        reviewScope: ["audit report blocker", "manual handoff state", "full accounts PDF", "iXBRL XML", "tax computation", "notes", "auditor evidence"],
        requiredEvidence: [
          "Signed auditor report and manual handoff note reviewed by the qualified accountant.",
          "Normal filing approval remains blocked until auditor evidence is present.",
        ],
        requiredSignOffGate: "Qualified accountant must record manual handoff acceptance before relying on outputs.",
        evidenceVerifiers: [
          {
            name: "FilingGoldenCorpusScenarioTests.GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence",
            command: "dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~FilingGoldenCorpusScenarioTests.GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence",
            ciScope: "default-ci",
            runsInDefaultCi: true,
            environment: "EF Core InMemory golden fixture; CI also runs the broader backend suite on Linux",
            evidenceLevel: "end-to-end golden filing scenario",
          },
        ],
        sources: [
          source("cro-medium-company", "CRO medium company financial statements requirements"),
          source("cro-auditors-report", "CRO auditors report requirements"),
          source("frc-frs-102", "FRC FRS 102 current edition and amendments"),
        ],
      },
    ],
    accountantAcceptanceSummary: {
      scenarioCount: 5,
      automatedVerifierCount: 6,
      professionalSignOffRequiredCount: 5,
      manualHandoffScenarioCount: 1,
      releaseBlockingScenarioCodes: ["clg-charity", "dac-small", "medium-audit-required", "micro-ltd", "small-abridged-ltd"],
      requiredSignOffGates: [
        "Named qualified accountant must approve the CLG charity pack and charity evidence before real filing use.",
        "Named qualified accountant must approve the DAC generated pack before real filing use.",
        "Named qualified accountant must approve both the full accounts pack and abridged CRO pack before real filing use.",
        "Named qualified accountant must approve the generated pack before real filing use.",
        "Qualified accountant must record manual handoff acceptance before relying on outputs.",
      ],
      status: "qualified-accountant-review-required",
    },
    accountantWorkflowWalkthroughProtocol: {
      protocolVersion: "accountant-workflow-walkthrough-v1",
      reviewerRole: "Qualified accountant",
      status: "required-review",
      signOffGate: "golden-corpus-accountant-acceptance",
      failurePolicy: "Block release if a named qualified accountant has not walked the seeded golden corpus through the live accountant workflow and accepted the outputs, gates, wording and evidence.",
      seededScenarioCodes: ["clg-charity", "dac-small", "medium-audit-required", "micro-ltd", "small-abridged-ltd"],
      routeSequence: [
        "Dashboard: identify the client, deadline pressure, blockers, reviewer owner and next action.",
        "Company detail: confirm statutory profile, company type, officers, charity flags and period setup.",
        "Period workspace: review import, classification, year-end evidence, statements, notes and workflow rail state.",
        "Financial statements: inspect statement preview, tax computation, source trail and directors' report evidence.",
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
      journeyAcceptance("financial-statements", "Financial statements", "financialStatements", ["Statements"], [
        "Financial statements route exposes statement preview, tax computation, source trail and directors' report evidence before filing review.",
        "A named qualified accountant accepts the Financial statements route outputs, gates, wording and evidence for every seeded golden scenario.",
      ]),
      journeyAcceptance("filing-review", "Filing review", "filing", ["Review", "Filing"], [
        "Filing review route exposes readiness, source links, generated outputs, signatory gates, accountant sign-off packet, external ROS/iXBRL validation and filing state.",
        "A named qualified accountant accepts the Filing review route outputs, gates, wording and evidence for every seeded golden scenario.",
      ]),
      journeyAcceptance("production-readiness", "Production readiness", "readiness", ["Review", "Filing"], [
        "Production readiness route exposes backend checks, filing rules coverage, unsupported paths, security posture, release blockers and accountant review state.",
        "A named qualified accountant accepts the Production readiness route outputs, gates, wording and evidence for every seeded golden scenario.",
      ]),
    ],
    accountantWorkflowEvidencePack: accountantWorkflowEvidencePack(),
    workbenchVisualAcceptanceRegister: workbenchVisualAcceptanceRegister(),
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
            expectedCorporationTax: 62.5,
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
      {
        code: "small-abridged-ltd",
        label: "Small abridged LTD",
        companyScope: "Private company limited by shares",
        expectedOutcome: "generated-pack",
        coverageStatus: "covered",
        fixture: {
          legalName: "Connacht Digital Solutions Limited",
          companyType: "Private",
          periodStart: "2025-01-01",
          periodEnd: "2025-12-31",
          expectedSizeClass: "Small",
          expectedRegime: "SmallAbridged",
          auditExempt: true,
          manualProfessionalReviewRequired: false,
        },
        evidenceTestNames: [
          "AccountsWorkflowTests.GoldenPath_SmallAuditExemptCompany_MixedAccrualSetBalancesThroughPdfAndIxbrl",
          "FilingGoldenCorpusScenarioTests.GoldenCorpus_SmallAbridgedLtd_EmitsFullAccountsAbridgedCroPackIxbrlAndReadiness",
        ],
        evidenceVerifiers: [
          {
            name: "AccountsWorkflowTests.GoldenPath_SmallAuditExemptCompany_MixedAccrualSetBalancesThroughPdfAndIxbrl",
            command: "dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~AccountsWorkflowTests.GoldenPath_SmallAuditExemptCompany_MixedAccrualSetBalancesThroughPdfAndIxbrl",
            ciScope: "default-ci",
            runsInDefaultCi: true,
            environment: "EF Core InMemory golden fixture; CI also runs the broader backend suite on Linux",
            evidenceLevel: "end-to-end golden filing scenario",
          },
          {
            name: "FilingGoldenCorpusScenarioTests.GoldenCorpus_SmallAbridgedLtd_EmitsFullAccountsAbridgedCroPackIxbrlAndReadiness",
            command: "dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~FilingGoldenCorpusScenarioTests.GoldenCorpus_SmallAbridgedLtd_EmitsFullAccountsAbridgedCroPackIxbrlAndReadiness",
            ciScope: "default-ci",
            runsInDefaultCi: true,
            environment: "EF Core InMemory golden fixture; CI also runs the broader backend suite on Linux",
            evidenceLevel: "end-to-end golden filing scenario",
          },
        ],
        assertions: ["full accounts PDF", "abridged CRO pack", "Section 352 evidence", "iXBRL parse", "source-backed readiness"],
        evidencePack: {
          outputArtifacts: ["full accounts PDF text", "abridged CRO filing pack", "CRO signature page", "iXBRL XML", "tax computation", "notes disclosure set", "filing readiness profile"],
          decisionGates: ["abridgement eligibility", "director and secretary certification", "named qualified-accountant review", "external ROS/iXBRL validation"],
          expectedValueChecks: ["SmallAbridged regime", "Section 352 wording", "public P&L turnover omitted from iXBRL"],
          expectedOutputs: {
            pdfTextMarkers: ["Connacht Digital Solutions Limited", "Section 352", "PROFIT AND LOSS ACCOUNT"],
            ixbrlRequiredTags: ["core:EntityCurrentLegalOrRegisteredName"],
            filingReadinessState: "ready-for-external-filing",
            expectedCorporationTax: 62.5,
            requiredNotes: ["Accounting Policies"],
            filingGateStates: ["abridgement eligibility confirmed", "director and secretary certification satisfied", "qualified-accountant review recorded"],
            signOffPacketState: "ready-for-external-filing",
          },
          expectedProofPoints: [
            {
              area: "pdf-text",
              expectedEvidence: "Full accounts PDF and abridged CRO filing pack contain the required Section 352 evidence.",
              automatedVerifier: "FilingGoldenCorpusScenarioTests.GoldenCorpus_SmallAbridgedLtd_EmitsFullAccountsAbridgedCroPackIxbrlAndReadiness",
              required: true,
            },
            {
              area: "ixbrl-xml",
              expectedEvidence: "iXBRL XML is well-formed and omits public profit-and-loss turnover for the abridged pack.",
              automatedVerifier: "FilingGoldenCorpusScenarioTests.GoldenCorpus_SmallAbridgedLtd_EmitsFullAccountsAbridgedCroPackIxbrlAndReadiness",
              required: true,
            },
          ],
          sourceReferences: [source("frc-frs-102", "FRC FRS 102 current edition and amendments")],
        },
      },
      {
        code: "clg-charity",
        label: "CLG charity annual reporting",
        companyScope: "Company limited by guarantee with charity evidence",
        expectedOutcome: "generated-pack-with-charity-gates",
        coverageStatus: "covered",
        fixture: {
          legalName: "Dublin Community Support CLG",
          companyType: "CompanyLimitedByGuarantee",
          periodStart: "2026-01-01",
          periodEnd: "2026-12-31",
          expectedSizeClass: "Small",
          expectedRegime: "Small",
          auditExempt: true,
          manualProfessionalReviewRequired: false,
        },
        evidenceTestNames: [
          "FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness",
        ],
        evidenceVerifiers: [
          {
            name: "FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness",
            command: "dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness",
            ciScope: "default-ci",
            runsInDefaultCi: true,
            environment: "EF Core InMemory golden fixture; CI also runs the broader backend suite on Linux",
            evidenceLevel: "end-to-end golden filing scenario",
          },
        ],
        assertions: ["CLG support", "charity number evidence", "SoFA evidence", "trustees report evidence", "source-backed readiness"],
        evidencePack: {
          outputArtifacts: ["CLG accounts PDF text", "charity readiness profile", "SoFA evidence", "trustees annual report evidence", "iXBRL XML", "tax computation", "notes disclosure set", "accountant sign-off packet"],
          decisionGates: ["charity number", "charity annual return review", "named qualified-accountant review", "accountant sign-off packet state"],
          expectedValueChecks: ["charity evidence satisfied", "Charities Regulator source attached", "CLG source attached", "well-formed iXBRL"],
          expectedOutputs: {
            pdfTextMarkers: ["Dublin Community Support CLG", "Community support and education."],
            ixbrlRequiredTags: ["core:EntityCurrentLegalOrRegisteredName"],
            filingReadinessState: "ready-for-external-filing",
            expectedCorporationTax: 62.5,
            requiredNotes: ["Accounting Policies", "Charity reporting disclosures"],
            filingGateStates: ["charity number satisfied", "SoFA and trustees annual report evidence satisfied", "qualified-accountant review recorded"],
            signOffPacketState: "ready-for-external-filing",
          },
          expectedProofPoints: [
            {
              area: "pdf-text",
              expectedEvidence: "CLG accounts PDF text contains company name and charity objectives.",
              automatedVerifier: "FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness",
              required: true,
            },
            {
              area: "filing-readiness",
              expectedEvidence: "Filing readiness confirms charity number, SoFA and trustees report evidence.",
              automatedVerifier: "FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness",
              required: true,
            },
          ],
          sourceReferences: [
            source("cro-guarantee-company", "CRO guarantee company financial statements requirements"),
            source("charities-regulator-annual-report", "Charities Regulator annual report guidance"),
            source("frc-frs-102", "FRC FRS 102 current edition and amendments"),
          ],
        },
      },
      {
        code: "medium-audit-required",
        label: "Medium audit-required handoff",
        companyScope: "Medium company or non-audit-exempt filing",
        expectedOutcome: "manual-handoff",
        coverageStatus: "covered",
        fixture: {
          legalName: "Midlands Manufacturing Limited",
          companyType: "Private",
          periodStart: "2026-01-01",
          periodEnd: "2026-12-31",
          expectedSizeClass: "Medium",
          expectedRegime: "Medium",
          auditExempt: false,
          manualProfessionalReviewRequired: true,
        },
        evidenceTestNames: [
          "FilingGoldenCorpusScenarioTests.GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence",
        ],
        evidenceVerifiers: [
          {
            name: "FilingGoldenCorpusScenarioTests.GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence",
            command: "dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~FilingGoldenCorpusScenarioTests.GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence",
            ciScope: "default-ci",
            runsInDefaultCi: true,
            environment: "EF Core InMemory golden fixture; CI also runs the broader backend suite on Linux",
            evidenceLevel: "end-to-end golden filing scenario",
          },
        ],
        assertions: ["audit report blocker", "manual handoff", "auditor evidence", "full accounts PDF", "medium iXBRL facts"],
        evidencePack: {
          outputArtifacts: ["full accounts PDF text", "auditor report evidence", "cash flow statement", "statement of changes in equity", "iXBRL XML", "filing readiness profile", "tax computation", "accountant sign-off packet"],
          decisionGates: ["auditor handoff", "manual professional review", "normal CRO approval blocked until auditor evidence", "accountant sign-off packet state"],
          expectedValueChecks: ["Medium regime selected", "audit report blocker present before auditor evidence", "tagged P&L facts present after auditor evidence", "auditor reference appears in PDF text"],
          expectedOutputs: {
            pdfTextMarkers: ["Midlands Manufacturing Limited", "INDEPENDENT AUDITOR'S REPORT", "AUD-2026-MIDLANDS-001", "CASH FLOW STATEMENT", "STATEMENT OF CHANGES IN EQUITY"],
            ixbrlRequiredTags: ["core:TurnoverGrossRevenue", "core:ProfitLossOnOrdinaryActivitiesBeforeTax"],
            filingReadinessState: "manual-handoff-until-auditor-evidence",
            expectedCorporationTax: 62.5,
            requiredNotes: ["Turnover", "Tax on Profit on Ordinary Activities"],
            filingGateStates: ["auditor handoff blocked until signed auditor report", "normal CRO approval blocked until auditor evidence", "manual professional review required"],
            signOffPacketState: "manual-handoff",
          },
          expectedProofPoints: [
            {
              area: "auditor-handoff",
              expectedEvidence: "Signed auditor report reference is mandatory before final output generation.",
              automatedVerifier: "FilingGoldenCorpusScenarioTests.GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence",
              required: true,
            },
            {
              area: "filing-readiness",
              expectedEvidence: "Filing readiness blocks approval before signed auditor report evidence and clears that blocker when evidence is recorded.",
              automatedVerifier: "FilingGoldenCorpusScenarioTests.GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence",
              required: true,
            },
          ],
          sourceReferences: [
            source("cro-medium-company", "CRO medium company financial statements requirements"),
            source("cro-auditors-report", "CRO auditors report requirements"),
            source("frc-frs-102", "FRC FRS 102 current edition and amendments"),
          ],
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
        automatedVerifierCommands: ["dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl"],
        ciScopes: ["default-ci"],
        evidenceLevels: ["end-to-end golden filing scenario"],
        outputArtifacts: ["accounts PDF text"],
        decisionGates: ["named qualified-accountant review"],
        expectedValueChecks: ["well-formed iXBRL"],
        proofPointAreas: ["pdf-text"],
        sourceIds: ["frc-frs-105"],
        expectedCorporationTax: 62.5,
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
        automatedVerifierCommands: ["dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~FilingGoldenCorpusScenarioTests.GoldenCorpus_DacSmall_EmitsAccountsIxbrlAndSourceBackedReadiness"],
        ciScopes: ["default-ci"],
        evidenceLevels: ["end-to-end golden filing scenario"],
        outputArtifacts: ["DAC accounts PDF text"],
        decisionGates: ["named qualified-accountant review"],
        expectedValueChecks: ["well-formed iXBRL"],
        proofPointAreas: ["pdf-text"],
        sourceIds: ["frc-frs-102"],
        expectedCorporationTax: 62.5,
        filingReadinessState: "ready-for-external-filing",
        signOffPacketState: "ready-for-external-filing",
      },
      {
        scenarioCode: "small-abridged-ltd",
        label: "Small abridged LTD",
        fixtureLegalName: "Connacht Digital Solutions Limited",
        companyType: "Private",
        expectedOutcome: "generated-pack",
        coverageStatus: "covered",
        acceptanceStatus: "qualified-accountant-review-required",
        requiredSignOffGate: "Named qualified accountant must approve both the full accounts pack and abridged CRO pack before real filing use.",
        blocksRelease: true,
        automatedVerifierNames: [
          "AccountsWorkflowTests.GoldenPath_SmallAuditExemptCompany_MixedAccrualSetBalancesThroughPdfAndIxbrl",
          "FilingGoldenCorpusScenarioTests.GoldenCorpus_SmallAbridgedLtd_EmitsFullAccountsAbridgedCroPackIxbrlAndReadiness",
        ],
        automatedVerifierCommands: [
          "dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~AccountsWorkflowTests.GoldenPath_SmallAuditExemptCompany_MixedAccrualSetBalancesThroughPdfAndIxbrl",
          "dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~FilingGoldenCorpusScenarioTests.GoldenCorpus_SmallAbridgedLtd_EmitsFullAccountsAbridgedCroPackIxbrlAndReadiness",
        ],
        ciScopes: ["default-ci"],
        evidenceLevels: ["end-to-end golden filing scenario"],
        outputArtifacts: ["full accounts PDF text", "abridged CRO filing pack", "CRO signature page", "iXBRL XML", "tax computation", "notes disclosure set", "filing readiness profile"],
        decisionGates: ["abridgement eligibility", "director and secretary certification", "named qualified-accountant review", "external ROS/iXBRL validation"],
        expectedValueChecks: ["SmallAbridged regime", "Section 352 wording", "public P&L turnover omitted from iXBRL"],
        proofPointAreas: ["pdf-text", "ixbrl-xml"],
        sourceIds: ["frc-frs-102"],
        expectedCorporationTax: 62.5,
        filingReadinessState: "ready-for-external-filing",
        signOffPacketState: "ready-for-external-filing",
      },
      {
        scenarioCode: "clg-charity",
        label: "CLG charity annual reporting",
        fixtureLegalName: "Dublin Community Support CLG",
        companyType: "CompanyLimitedByGuarantee",
        expectedOutcome: "generated-pack-with-charity-gates",
        coverageStatus: "covered",
        acceptanceStatus: "qualified-accountant-review-required",
        requiredSignOffGate: "Named qualified accountant must approve the CLG charity pack and charity evidence before real filing use.",
        blocksRelease: true,
        automatedVerifierNames: [
          "FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness",
        ],
        automatedVerifierCommands: [
          "dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness",
        ],
        ciScopes: ["default-ci"],
        evidenceLevels: ["end-to-end golden filing scenario"],
        outputArtifacts: ["CLG accounts PDF text", "charity readiness profile", "SoFA evidence", "trustees annual report evidence", "iXBRL XML", "tax computation", "notes disclosure set", "accountant sign-off packet"],
        decisionGates: ["charity number", "charity annual return review", "named qualified-accountant review", "accountant sign-off packet state"],
        expectedValueChecks: ["charity evidence satisfied", "Charities Regulator source attached", "CLG source attached", "well-formed iXBRL"],
        proofPointAreas: ["pdf-text", "filing-readiness"],
        sourceIds: ["cro-guarantee-company", "charities-regulator-annual-report", "frc-frs-102"],
        expectedCorporationTax: 62.5,
        filingReadinessState: "ready-for-external-filing",
        signOffPacketState: "ready-for-external-filing",
      },
      {
        scenarioCode: "medium-audit-required",
        label: "Medium audit-required handoff",
        fixtureLegalName: "Midlands Manufacturing Limited",
        companyType: "Private",
        expectedOutcome: "manual-handoff",
        coverageStatus: "covered",
        acceptanceStatus: "qualified-accountant-review-required",
        requiredSignOffGate: "Qualified accountant must record manual handoff acceptance before relying on outputs.",
        blocksRelease: true,
        automatedVerifierNames: [
          "FilingGoldenCorpusScenarioTests.GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence",
        ],
        automatedVerifierCommands: [
          "dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~FilingGoldenCorpusScenarioTests.GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence",
        ],
        ciScopes: ["default-ci"],
        evidenceLevels: ["end-to-end golden filing scenario"],
        outputArtifacts: ["full accounts PDF text", "auditor report evidence", "cash flow statement", "statement of changes in equity", "iXBRL XML", "filing readiness profile", "tax computation", "accountant sign-off packet"],
        decisionGates: ["auditor handoff", "manual professional review", "normal CRO approval blocked until auditor evidence", "accountant sign-off packet state"],
        expectedValueChecks: ["Medium regime selected", "audit report blocker present before auditor evidence", "tagged P&L facts present after auditor evidence", "auditor reference appears in PDF text"],
        proofPointAreas: ["auditor-handoff", "filing-readiness"],
        sourceIds: ["cro-medium-company", "cro-auditors-report", "frc-frs-102"],
        expectedCorporationTax: 62.5,
        filingReadinessState: "manual-handoff-until-auditor-evidence",
        signOffPacketState: "manual-handoff",
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
      {
        code: "no-direct-cro-ros-submission",
        label: "No direct CRO/ROS submission automation",
        required: true,
        status: "enforced",
        detail: "Final filing operations remain recorded workflow states only.",
      },
      {
        code: "production-ci-gates",
        label: "Production CI evidence gates",
        required: true,
        status: "required",
        detail: "Production smoke, backup restore and visual evidence must be retained for the exact release candidate.",
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
        code: "no-direct-cro-ros-submission",
        label: "No direct CRO/ROS submission automation",
        owner: "Engineering",
        priority: "critical",
        riskRank: 6,
        evidenceStage: "unsupported-path-gate",
        status: "complete",
        detail: "The platform never automates final CRO or ROS submission.",
        evidenceRequired: "Release reviewer confirms final filing operations remain recorded workflow states only.",
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
        code: "production-monitoring",
        label: "Production monitoring and backup evidence",
        owner: "Operations",
        priority: "high",
        riskRank: 20,
        evidenceStage: "production-operations-evidence",
        status: "required",
        detail: "Production smoke, monitoring routing and backup restore evidence must be retained for the exact release candidate.",
        evidenceRequired: "CI production stack smoke and backup restore evidence.",
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
        operationalGateCode: "production-ci-gates",
        evidenceArtifact: "light-dark-desktop-mobile-screenshot-review",
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
        operationalGateCode: "production-ci-gates",
        evidenceArtifact: "light-dark-desktop-mobile-screenshot-review",
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
          "No direct CRO/ROS submission automation is enforced as recorded workflow states only.",
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
          "no-direct-cro-ros-submission",
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
          "PeriodYearEndWorkspace extraction composes questionnaire, completeness, summary metrics and empty-state panels behind one focused year-end workflow component.",
          "PeriodAdjustmentsWorkspace extraction composes generation, summary, filters and approval review cards behind one focused adjustments workflow component.",
          "PeriodStatementsWorkspace extraction composes readiness, statements, notes and charity reporting navigation behind one focused statements workflow component.",
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
        detail: "Named professional approval must be recorded against the period before any real filing pack is treated as final.",
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
        auditEventCodes: ["CroFilingStatusChanged", "IxbrlInternalCheckCompleted"],
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
        detail: "Internal XML checks are not enough for Revenue acceptance; the reviewer must retain external validation evidence.",
      },
      {
        code: "no-direct-cro-ros-submission",
        label: "No direct CRO/ROS submission automation",
        ownerRole: "Engineering",
        required: true,
        status: "complete",
        blocksRelease: false,
        evidenceArtifact: "no-direct-cro-ros-submission-control",
        assuranceActionCode: "no-direct-cro-ros-submission",
        operationalGateCode: "no-direct-cro-ros-submission",
        auditEventCodes: [],
        detail: "Release reviewer confirms final filing operations remain recorded workflow states only.",
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
        detail: "A qualified accountant must walk the golden scenarios through the live workflow and accept outputs, gates and wording.",
      },
      {
        code: "production-smoke-and-backup",
        label: "Production smoke and backup evidence",
        ownerRole: "Operations",
        required: true,
        status: "required",
        blocksRelease: true,
        evidenceArtifact: "ci-production-stack-smoke-and-backup-restore",
        assuranceActionCode: "production-monitoring",
        operationalGateCode: "production-ci-gates",
        auditEventCodes: [],
        detail: "Release evidence must include successful production stack smoke, visual smoke, monitoring configuration and backup restore drill.",
      },
      {
        code: "visual-qa-screenshot-review",
        label: "Light/dark visual QA screenshot review",
        ownerRole: "Engineering",
        required: true,
        status: "in-progress",
        blocksRelease: true,
        evidenceArtifact: "light-dark-desktop-mobile-screenshot-review",
        assuranceActionCode: "light-dark-visual-regression",
        operationalGateCode: "production-ci-gates",
        auditEventCodes: [],
        detail: "Desktop and mobile screenshots in light and dark mode must be reviewed for the accountant workflow before release.",
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
        code: "frontend-workbench-contract",
        label: "Frontend workbench contract, render and API checks",
        ownerRole: "Engineering",
        command: "npm test",
        ciScope: "default-ci",
        runsInDefaultCi: true,
        blocksRelease: true,
        evidenceArtifact: "frontend-test-results",
        releaseChecklistEvidenceArtifact: "light-dark-desktop-mobile-screenshot-review",
        manualFallback: "Run from frontend/ and retain the unit, render, readiness, proxy, auth and API-client verifier output.",
      },
      {
        code: "frontend-production-build",
        label: "Frontend lint, type-check and production build",
        ownerRole: "Engineering",
        command: "npm run lint; npx tsc --noEmit --incremental false; npm run build",
        ciScope: "default-ci",
        runsInDefaultCi: true,
        blocksRelease: true,
        evidenceArtifact: "frontend-build-results",
        releaseChecklistEvidenceArtifact: "light-dark-desktop-mobile-screenshot-review",
        manualFallback: "Run from frontend/ and retain lint, TypeScript and Next production build output when CI is unavailable.",
      },
      {
        code: "visual-smoke-light-dark",
        label: "Light/dark desktop/mobile visual smoke",
        ownerRole: "Engineering",
        command: "node scripts/visual-smoke.mjs; node scripts/verify-visual-smoke-artifacts.mjs --report-path=artifacts/visual-smoke/visual-smoke-evidence-report.json; node scripts/verify-accountant-workbench-evidence.mjs --visual-report=artifacts/visual-smoke/visual-smoke-evidence-report.json --report-path=artifacts/visual-smoke/accountant-workbench-evidence-report.json",
        ciScope: "default-ci",
        runsInDefaultCi: true,
        blocksRelease: true,
        evidenceArtifact: "artifacts/visual-smoke",
        releaseChecklistEvidenceArtifact: "light-dark-desktop-mobile-screenshot-review",
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
        code: "qualified-accountant-final-signoff",
        label: "Named accountant final sign-off evidence",
        ownerRole: "Qualified accountant",
        command: "manual review: record named qualified-accountant approval against the final generated filing pack",
        ciScope: "manual-release",
        runsInDefaultCi: false,
        blocksRelease: true,
        evidenceArtifact: "named-accountant-approval-record",
        releaseChecklistEvidenceArtifact: "named-accountant-approval-record",
        manualFallback: "A named qualified accountant must approve the exact generated filing pack before real filing use.",
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
        code: "no-direct-cro-ros-submission-control",
        label: "No direct CRO/ROS submission automation control",
        ownerRole: "Engineering",
        command: "manual review: confirm final CRO and ROS operations remain recorded workflow states only with no direct submission client configured",
        ciScope: "manual-release",
        runsInDefaultCi: false,
        blocksRelease: true,
        evidenceArtifact: "no-direct-cro-ros-submission-control",
        releaseChecklistEvidenceArtifact: "no-direct-cro-ros-submission-control",
        manualFallback: "Confirm final filing operations remain recorded workflow states only: generated, reviewed, approved, marked submitted, payment confirmed, accepted, rejected or corrected.",
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
      {
        code: "production-readiness-report-verification",
        label: "Production readiness report verification",
        ownerRole: "Engineering",
        command: "pwsh ./scripts/verify-production-readiness-report.ps1 -ReportPath production-readiness-report.json -EvidencePath production-readiness-verification-report.json",
        ciScope: "default-ci",
        runsInDefaultCi: true,
        blocksRelease: true,
        evidenceArtifact: "production-readiness-report",
        releaseChecklistEvidenceArtifact: "ci-production-stack-smoke-and-backup-restore",
        manualFallback: "Run after capturing the live production-readiness response and retain production-readiness-verification-report.json.",
      },
      {
        code: "production-stack-smoke",
        label: "Production compose smoke",
        ownerRole: "Operations",
        command: "pwsh ./scripts/smoke-production.ps1 -CheckMonitoringErrorRouting; pwsh ./scripts/verify-production-readiness-report.ps1; pwsh ./scripts/verify-structured-logs.ps1",
        ciScope: "default-ci",
        runsInDefaultCi: true,
        blocksRelease: true,
        evidenceArtifact: "ci-production-stack-smoke-and-backup-restore",
        releaseChecklistEvidenceArtifact: "ci-production-stack-smoke-and-backup-restore",
        manualFallback: "Run the production smoke script against the production compose profile and retain monitoring, structured log and filing-workflow output.",
      },
      {
        code: "backup-restore-drill",
        label: "PostgreSQL backup and restore drill",
        ownerRole: "Operations",
        command: "pwsh ./scripts/verify-postgres-backup.ps1",
        ciScope: "default-ci",
        runsInDefaultCi: true,
        blocksRelease: true,
        evidenceArtifact: "ci-production-stack-smoke-and-backup-restore",
        releaseChecklistEvidenceArtifact: "ci-production-stack-smoke-and-backup-restore",
        manualFallback: "Run the backup verification script after creating a fresh production-shape dump and retain the checksum and restore verification output.",
      },
      {
        code: "ci-machine-evidence-pack",
        label: "CI machine evidence pack",
        ownerRole: "Engineering",
        command: "pwsh ./scripts/verify-ci-machine-evidence-pack.ps1 -EvidenceDirectory <downloaded-ci-artifacts> -ReportPath ci-machine-evidence-pack-report.json -CommitSha <release-commit-sha> -GitHubActionsRunUrl <ci-run-url> -ReviewerWorkspaceDirectory <prepared-reviewer-workspace>",
        ciScope: "default-ci",
        runsInDefaultCi: true,
        blocksRelease: true,
        evidenceArtifact: "ci-machine-evidence-pack",
        releaseChecklistEvidenceArtifact: "ci-production-stack-smoke-and-backup-restore",
        manualFallback: "Run after downloading CI machine artifacts for the exact candidate.",
      },
      {
        code: "release-artifact-pack",
        label: "Release artifact pack verification",
        ownerRole: "Engineering",
        command: "pwsh ./scripts/verify-release-artifact-pack.ps1 -EvidenceDirectory <release-artifacts> -ReportPath release-artifact-pack-report.json -CommitSha <release-commit-sha> -GitHubActionsRunUrl <ci-run-url>",
        ciScope: "manual-release",
        runsInDefaultCi: false,
        blocksRelease: true,
        evidenceArtifact: "release-artifact-pack-report",
        releaseChecklistEvidenceArtifact: "named-accountant-approval-record",
        manualFallback: "Run against the collected machine, human release evidence, workspace verification and reviewer handoff reports for the exact release candidate.",
      },
    ],
    humanReleaseEvidence: humanReleaseEvidence(),
    humanReleaseEvidenceCloseout: humanReleaseEvidenceCloseout(),
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
    auditEvidencePack: [
      {
        code: "who-changed-what",
        label: "Who changed what",
        evidenceQuestion: "Which authenticated user changed statutory, accounting or filing evidence, and what old/new values were captured?",
        requiredArtifact: "tamper-evident-audit-log-entry",
        retainedIn: "audit_logs",
        requiredActor: "Authenticated firm user",
        capturedWhen: "At the same transaction boundary as each supported write.",
        verification: "Audit entry must include entity, action, request correlation, redacted before/after snapshots, integrity hash and previous hash.",
        failurePolicy: "Block release when a supported write path can alter filing evidence without an audit row linked into the integrity chain.",
        auditEventCodes: ["AdjustmentUpdated"],
        blockingGateCodes: ["working-paper-review"],
      },
      {
        code: "who-approved-what",
        label: "Who approved what",
        evidenceQuestion: "Which named reviewer or qualified accountant approved the pack, and which period/output state was approved?",
        requiredArtifact: "named-accountant-approval-record",
        retainedIn: "filing_workflow_status_history",
        requiredActor: "Named qualified accountant",
        capturedWhen: "After required evidence is present and before any filing status can be marked approved or submitted.",
        verification: "Approval transition must carry reviewer identity, timestamp, period id, open blocker summary and the allowed next action set.",
        failurePolicy: "Block release when approval can be recorded without a named qualified-accountant identity and linked readiness evidence.",
        auditEventCodes: ["CroFilingStatusChanged"],
        blockingGateCodes: ["qualified-accountant-review"],
      },
    ],
    operationsEvidencePack: [
      {
        code: "sentry-error-routing",
        label: "Sentry production error routing",
        category: "Monitoring",
        ownerRole: "Platform owner",
        required: true,
        command: "Verify Monitoring:ErrorTrackingDsn is configured with an HTTPS DSN and send a controlled non-PII smoke error through the production error pipeline.",
        requiredArtifact: "sentry-production-error-routing-check",
        releaseGateCode: "production-monitoring",
        verification: "Evidence must show the event reached the production error-tracking project with environment, request path and correlation id while default PII capture remains disabled.",
        failurePolicy: "Block release if production exceptions cannot be routed to the on-call owner with a usable correlation id.",
      },
      {
        code: "structured-log-correlation",
        label: "Structured log correlation sample",
        category: "Monitoring",
        ownerRole: "Platform owner",
        required: true,
        command: "Run production stack smoke and retain a structured JSON log sample containing timestamp, level, category, request id and correlation id.",
        requiredArtifact: "structured-json-log-sample",
        releaseGateCode: "production-monitoring",
        verification: "Evidence must prove safe error responses can be matched to server logs through the trace identifier/correlation id.",
        failurePolicy: "Block release if logs cannot be parsed or support tickets cannot be correlated to server evidence.",
      },
      {
        code: "dependency-audit",
        label: "Dependency and lockfile audit",
        category: "Dependency policy",
        ownerRole: "Engineering",
        required: true,
        command: "Run npm ci, npm audit --audit-level=moderate --json and scripts/write-dependency-evidence.ps1 against the release commit.",
        requiredArtifact: "dependency-audit-release",
        releaseGateCode: "dependency-policy-controls",
        verification: "Evidence must include npm-audit.json and dependency-audit-report.json with package-lock hash, npm audit counts, NuGet audit policy, and GitHub Actions version-hygiene wiring.",
        failurePolicy: "Block release for moderate/high/critical advisories, unreproducible lockfiles, failed restore/build, or unverified CI action versions.",
      },
      {
        code: "migration-safety",
        label: "Controlled migration safety",
        category: "Deployment safety",
        ownerRole: "Platform owner",
        required: true,
        command: "Run dotnet Accounts.Api.dll --migrate-only with production startup flags proving AutoMigrateOnStartup is disabled for normal web startup.",
        requiredArtifact: "controlled-migration-release-note",
        releaseGateCode: "deployment-safety-controls",
        verification: "Evidence must show migrations run as a separate release step and normal production startup remains guarded by ProductionSafetyService.",
        failurePolicy: "Block release if production startup can auto-migrate without explicit release approval.",
      },
      {
        code: "production-seed-block",
        label: "Production seed blocking",
        category: "Deployment safety",
        ownerRole: "Platform owner",
        required: true,
        command: "Review production configuration and run startup validation proving DatabaseStartup:SeedDemoData is false outside development.",
        requiredArtifact: "production-seed-block-validation",
        releaseGateCode: "deployment-safety-controls",
        verification: "Evidence must show demo users, sample companies and preview-only accounting records cannot be inserted into a production database.",
        failurePolicy: "Block release if demo seed data can run outside development.",
      },
      {
        code: "backup-restore-drill",
        label: "Backup restore drill",
        category: "Deployment safety",
        ownerRole: "Platform owner",
        required: true,
        command: "Run scripts/backup-postgres.ps1 and scripts/verify-postgres-backup.ps1 against the production compose shape before approving the release.",
        requiredArtifact: "postgres-backup-restore-drill-report",
        releaseGateCode: "deployment-safety-controls",
        verification: "Evidence must include the PostgreSQL custom-format dump, sha256 sidecar and successful restore verification report.",
        failurePolicy: "Block release if backup creation, checksum verification or restore verification fails.",
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
  });
}

function withGoldenLegalBasisSnapshots(report) {
  const goldenFilingCorpus = report.goldenFilingCorpus.map((scenario) => ({
    ...scenario,
    legalBasisSnapshot: goldenLegalBasisSnapshot(scenario),
  }));

  return {
    ...report,
    goldenFilingCorpus,
    goldenVerifierManifest: buildGoldenVerifierManifest(goldenFilingCorpus),
    accountantWalkthroughEvidenceMatrix: buildAccountantWalkthroughEvidenceMatrix({
      ...report,
      goldenFilingCorpus,
    }),
  };
}

function buildAccountantWalkthroughEvidenceMatrix(report) {
  const acceptanceByScenario = new Map(
    report.accountantAcceptanceCriteria.map((criterion) => [criterion.scenarioCode, criterion]),
  );
  const routeEvidenceByCode = new Map(
    report.accountantWorkflowEvidencePack.map((item) => [item.routeCode, item]),
  );

  return [...report.goldenFilingCorpus]
    .sort((left, right) => left.code.localeCompare(right.code))
    .flatMap((scenario) => {
      const acceptance = acceptanceByScenario.get(scenario.code);
      if (!acceptance) return [];

      return report.accountantJourneyAcceptanceChecklist.map((route) => {
        const routeEvidence = routeEvidenceByCode.get(route.routeCode);
        return {
          scenarioCode: scenario.code,
          scenarioLabel: scenario.label,
          expectedOutcome: scenario.expectedOutcome,
          filingReadinessState: scenario.evidencePack.expectedOutputs.filingReadinessState,
          signOffPacketState: scenario.evidencePack.expectedOutputs.signOffPacketState,
          manualProfessionalReviewRequired: scenario.fixture.manualProfessionalReviewRequired,
          routeCode: route.routeCode,
          routeLabel: route.routeLabel,
          routeKey: route.routeKey,
          workflowStages: route.workflowStages,
          visualArtifactNames: route.visualArtifactNames,
          evidenceArtifact: `${scenario.code}-${route.routeCode}-walkthrough-note`,
          decisionQuestion: routeEvidence?.decisionQuestion ?? "",
          requiredEvidence: [...new Set([...route.requiredEvidence, ...acceptance.requiredEvidence])].sort(),
          acceptanceCriteria: [
            ...route.acceptanceCriteria,
            ...acceptance.reviewScope.map((scope) =>
              `${scenario.label}: qualified-accountant review covers ${scope}.`,
            ),
          ],
          releaseChecklistCode: "golden-corpus-accountant-acceptance",
          signOffGate: route.signOffGate,
          status: "required-review",
          blocksRelease: true,
        };
      });
    });
}

function buildGoldenVerifierManifest(goldenFilingCorpus) {
  return goldenFilingCorpus.flatMap((scenario) =>
    scenario.evidenceVerifiers.map((verifier) => ({
      scenarioCode: scenario.code,
      scenarioLabel: scenario.label,
      expectedOutcome: scenario.expectedOutcome,
      coverageStatus: scenario.coverageStatus,
      verifierName: verifier.name,
      command: verifier.command,
      ciScope: verifier.ciScope,
      runsInDefaultCi: verifier.runsInDefaultCi,
      evidenceLevel: verifier.evidenceLevel,
      blocksRelease: true,
      outputArtifacts: scenario.evidencePack.outputArtifacts,
      decisionGates: scenario.evidencePack.decisionGates,
      proofPointAreas: scenario.evidencePack.expectedProofPoints.map((proofPoint) => proofPoint.area),
    })),
  );
}

function goldenLegalBasisSnapshot(scenario) {
  const basisByCode = {
    "micro-ltd": {
      legalBasis: "FRS 105 micro-entities regime with CRO financial-statement and Revenue iXBRL filing evidence.",
      sourceIds: [
        "cro-financial-statements-requirements",
        "frc-frs-105",
        "revenue-ixbrl-overview",
        "revenue-accepted-taxonomies",
      ],
    },
    "small-abridged-ltd": {
      legalBasis: "FRS 102 small-company abridgement with Section 352 CRO filing evidence and Revenue iXBRL evidence.",
      sourceIds: [
        "cro-financial-statements-requirements",
        "frc-frs-102",
        "revenue-ixbrl-contents",
        "revenue-accepted-taxonomies",
      ],
    },
    "dac-small": {
      legalBasis: "FRS 102 small-company DAC path with directors' report, CRO certification and Revenue iXBRL evidence.",
      sourceIds: [
        "cro-financial-statements-requirements",
        "frc-frs-102",
        "revenue-ixbrl-overview",
        "revenue-accepted-taxonomies",
      ],
    },
    "clg-charity": {
      legalBasis: "CLG charity reporting path with CRO guarantee-company, Charities Regulator annual-report and FRS 102 evidence.",
      sourceIds: [
        "cro-guarantee-company",
        "charities-regulator-annual-report",
        "frc-frs-102",
        "revenue-ixbrl-overview",
      ],
    },
    "medium-audit-required": {
      legalBasis: "Medium-company audit-required path blocked to manual handoff until auditor report and professional review evidence are present.",
      sourceIds: [
        "cro-medium-company",
        "cro-auditors-report",
        "frc-frs-102",
        "revenue-ixbrl-overview",
      ],
    },
  };
  const basis = basisByCode[scenario.code] ?? {
    legalBasis: `${scenario.label} source-backed statutory filing basis.`,
    sourceIds: scenario.evidencePack.sourceReferences.map((sourceReference) => sourceReference.sourceId),
  };
  const sourceIds = Array.from(
    new Set([
      ...basis.sourceIds,
      ...scenario.evidencePack.sourceReferences.map((sourceReference) => sourceReference.sourceId),
    ]),
  ).sort();

  return {
    scenarioCode: scenario.code,
    companyType: scenario.fixture.companyType,
    sizeClass: scenario.fixture.expectedSizeClass,
    electedRegime: scenario.fixture.expectedRegime,
    auditExempt: scenario.fixture.auditExempt,
    manualProfessionalReviewRequired: scenario.fixture.manualProfessionalReviewRequired,
    legalBasis: basis.legalBasis,
    requiredOutputs: scenario.evidencePack.outputArtifacts,
    professionalGates: scenario.evidencePack.decisionGates,
    sourceIds,
  };
}

function journeyAcceptance(routeCode, routeLabel, routeKey, workflowStages, acceptanceCriteria) {
  return {
    routeCode,
    routeLabel,
    routeKey,
    workflowStages,
    seededScenarioCodes: ["clg-charity", "dac-small", "medium-audit-required", "micro-ltd", "small-abridged-ltd"],
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

function accountantWorkflowEvidencePack() {
  return [
    accountantRouteEvidence("dashboard", "Dashboard", ["Setup", "Import", "Classify", "Year-End", "Statements", "Notes", "Review", "Filing"]),
    accountantRouteEvidence("company-detail", "Company detail", ["Setup"]),
    accountantRouteEvidence("period-workspace", "Period workspace", ["Setup", "Import", "Classify", "Year-End", "Statements", "Notes", "Review", "Filing"]),
    accountantRouteEvidence("financial-statements", "Financial statements", ["Statements"]),
    accountantRouteEvidence(
      "filing-review",
      "Filing review",
      ["Review", "Filing"],
      "Does the filing review route let a qualified accountant accept readiness, source links, generated outputs, signatory gates, external ROS/iXBRL validation, filing state, outputs, gates, wording and evidence?",
    ),
    accountantRouteEvidence(
      "production-readiness",
      "Production readiness",
      ["Review", "Filing"],
      "Does the production readiness route let a qualified accountant accept backend checks, filing rules coverage, unsupported paths, security posture, release blockers, accountant review state, outputs, gates, wording and evidence?",
    ),
  ];
}

function accountantRouteEvidence(routeCode, routeLabel, workflowStages, decisionQuestion) {
  return {
    routeCode,
    routeLabel,
    workflowStages,
    seededScenarioCodes: ["clg-charity", "dac-small", "medium-audit-required", "micro-ltd", "small-abridged-ltd"],
    visualArtifactNames: visualSmokeArtifactsForRoute(routeCode),
    evidenceArtifact: `${routeCode}-accountant-route-acceptance-note`,
    decisionQuestion: decisionQuestion ?? `Does the ${routeLabel} route let a qualified accountant accept the workflow state, blockers, next action, outputs, gates, wording and evidence for every seeded golden scenario?`,
    requiredEvidence: [
      "named qualified-accountant route acceptance",
      "visual smoke screenshots reviewed",
      "golden corpus evidence accepted",
    ],
    signOffGate: "golden-corpus-accountant-acceptance",
    failurePolicy: "Block release until a named qualified accountant accepts this route's outputs, gates, wording and evidence against the seeded golden corpus and reviewed visual artifacts.",
  };
}

function workbenchVisualAcceptanceRegister() {
  return [
    workbenchVisualAcceptance("dashboard", "Dashboard", ["Setup", "Import", "Classify", "Year-End", "Statements", "Notes", "Review", "Filing"]),
    workbenchVisualAcceptance("company-detail", "Company detail", ["Setup"]),
    workbenchVisualAcceptance("period-workspace", "Period workspace", ["Setup", "Import", "Classify", "Year-End", "Statements", "Notes", "Review", "Filing"]),
    workbenchVisualAcceptance("financial-statements", "Financial statements", ["Statements"]),
    workbenchVisualAcceptance(
      "filing-review",
      "Filing review",
      ["Review", "Filing"],
      "Accept the filing review screen only after its evidence checklist, source links, generated outputs and filing-state actions are visually clear in light/dark desktop/mobile screenshots.",
    ),
    workbenchVisualAcceptance(
      "production-readiness",
      "Production readiness",
      ["Review", "Filing"],
      "Accept the production readiness screen only after release blockers, rule coverage, visual QA, operational readiness and accountant review state are visually clear in light/dark desktop/mobile screenshots.",
    ),
    workbenchVisualAcceptance("workbench-preview", "Workbench preview", ["Setup", "Import", "Classify", "Year-End", "Statements", "Notes", "Review", "Filing"]),
  ];
}

function workbenchVisualAcceptance(routeCode, routeLabel, workflowStages, nextAction) {
  return {
    routeCode,
    routeLabel,
    workflowStages,
    acceptanceAreas: [
      "accountant-workflow-hierarchy",
      "table-scanability",
      "theme-contrast",
      "mobile-density",
      "loading-error-empty-states",
    ],
    screenshotArtifactNames: visualSmokeArtifactsForRoute(routeCode),
    evidenceArtifact: `${routeCode}-visual-acceptance-note`,
    requiredEvidence: [
      "route-state acceptance note",
      "light/dark desktop/mobile screenshot review",
      "named visual QA reviewer sign-off",
    ],
    releaseGateCode: "visual-qa-screenshot-review",
    status: "required-review",
    failurePolicy: "Block release until this accountant workbench route is visually accepted across workflow hierarchy, table scanability, theme contrast, mobile density and route states.",
    nextAction: nextAction ?? `Accept the ${routeLabel} route only after its workflow hierarchy, tables, contrast, mobile layout, loading/error/empty states and screenshots are professionally reviewed.`,
  };
}

function visualSmokeArtifactsForRoute(routeCode) {
  return expectedVisualSmokeArtifacts()
    .filter((artifact) => artifact.routeName === routeCode)
    .map((artifact) => artifact.fileName)
    .sort((left, right) => left.localeCompare(right));
}

function source(sourceId, title) {
  return {
    sourceId,
    title,
    effectiveDate: "2026-07-03",
    url: "https://www.frc.org.uk/",
  };
}

function sourceLawReviewEntry(sourceId, title) {
  const legalSource = source(sourceId, title);

  return {
    sourceId: legalSource.sourceId,
    title: legalSource.title,
    url: legalSource.url,
    pinnedEffectiveDate: legalSource.effectiveDate,
    ownerRole: "Accounting standards reviewer",
    releaseChecklistCode: "source-law-change-review",
    blocksRelease: true,
    reviewChecks: [
      "Confirm source page is reachable at the pinned URL.",
      "Compare pinned effective date against the current source page.",
      "Review guidance wording for statutory filing, exemption, note or taxonomy changes.",
      "Record qualified accountant acceptance before generated packs are used for real filings.",
    ],
    requiredEvidence: [
      "source-law-change-review-note",
      "qualified-accountant-source-law-signoff",
    ],
  };
}
