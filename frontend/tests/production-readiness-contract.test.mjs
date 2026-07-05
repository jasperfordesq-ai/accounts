import { test } from "node:test";
import assert from "node:assert/strict";
import { parseProductionReadinessReport } from "../src/lib/api.ts";
import {
  ACCOUNTANT_WORKFLOW_STAGES,
  expectedVisualSmokeArtifacts,
  expectedVisualSmokeScreenshotCount,
  visualSmokeLayoutChecks,
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
  assert.equal(parsed.sourceLawSnapshot.sourceCount, 1);
  assert.equal(parsed.sourceLawSnapshot.contentHash, "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
  assert.equal(parsed.sourceLawTraceability[0].sourceId, "frc-frs-105");
  assert.equal(parsed.sourceLawTraceability[0].inSnapshot, true);
  assert.equal(parsed.sourceLawTraceability[0].usedBy[0], "golden-corpus:micro-ltd");
  assert.equal(parsed.sourceLawTraceability[0].releaseGateCodes[0], "qualified-accountant-review");
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
  assert.equal(parsed.assurancePacket.packetVersion, "production-assurance-packet-v1");
  assert.equal(parsed.assurancePacket.sourceLawSnapshotHash, "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
  assert.equal(parsed.assurancePacket.goldenCorpusCovered, 1);
  assert.ok(parsed.assurancePacket.evidenceItems.includes("source-law-traceability-index"));
  assert.ok(parsed.assurancePacket.evidenceItems.includes("release-review-checklist"));
  assert.ok(parsed.assurancePacket.evidenceItems.includes("golden-verifier-manifest"));
  assert.equal(parsed.assurancePacket.releaseBlockers[0], "Qualified accountant sign-off required");
  assert.equal(parsed.assuranceActions[0].riskRank, 0);
  assert.equal(parsed.assuranceActions[0].evidenceStage, "accountant-review-gate");
  assert.equal(parsed.releaseReviewChecklist[0].code, "accountant-final-signoff");
  assert.equal(parsed.releaseReviewChecklist[0].assuranceActionCode, "qualified-accountant-signoff");
  assert.equal(parsed.releaseReviewChecklist[0].evidenceArtifact, "named-accountant-approval-record");
  assert.equal(parsed.auditEvidenceTimeline[0].code, "data-change-capture");
  assert.equal(parsed.auditEvidenceTimeline[0].capturedWhen, "At every authenticated write before regenerated outputs can be reviewed.");
  assert.equal(parsed.auditEvidenceTimeline[1].blockingGateCodes[0], "generated-output-review");
  assert.equal(parsed.visualQaCoverage.expectedScreenshotCount, expectedVisualSmokeScreenshotCount());
  assert.equal(parsed.visualQaCoverage.artifacts.length, expectedVisualSmokeArtifacts().length);
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

test("parseProductionReadinessReport rejects golden corpus without matching accountant acceptance criteria", () => {
  const payload = sampleReport();
  payload.goldenFilingCorpus.push({
    ...payload.goldenFilingCorpus[0],
    code: "small-abridged-ltd",
    label: "Small abridged LTD",
  });
  payload.assurancePacket.goldenCorpusTotal = 2;
  payload.assurancePacket.goldenCorpusCovered = 2;

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: accountantAcceptanceCriteria - missing acceptance criteria for golden scenarios: small-abridged-ltd/,
  );
});

test("parseProductionReadinessReport rejects inconsistent production assurance counts", () => {
  const payload = sampleReport();
  payload.sourceLawSnapshot.sourceCount = 2;

  assert.throws(
    () => parseProductionReadinessReport(payload),
    /Invalid production readiness report contract: sourceLawSnapshot\.sourceCount - expected 1, received 2/,
  );

  const corpusPayload = sampleReport();
  corpusPayload.assurancePacket.goldenCorpusTotal = 2;

  assert.throws(
    () => parseProductionReadinessReport(corpusPayload),
    /Invalid production readiness report contract: assurancePacket\.goldenCorpusTotal - expected 1, received 2/,
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
      sourceCount: 1,
      sources: [source("frc-frs-105", "FRC FRS 105 current edition and amendments")],
    },
    sourceLawTraceability: [
      {
        ...source("frc-frs-105", "FRC FRS 105 current edition and amendments"),
        inSnapshot: true,
        usedBy: [
          "golden-corpus:micro-ltd",
          "statutory-rule-matrix:ltd-micro",
          "statutory-rules-coverage:size-classification-thresholds",
          "accountant-acceptance:micro-ltd",
        ],
        releaseGateCodes: ["qualified-accountant-review"],
      },
    ],
    assurancePacket: {
      packetId: "assurance-sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
      packetVersion: "production-assurance-packet-v1",
      status: "review-required",
      sourceLawSnapshotHash: "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
      goldenCorpusCovered: 1,
      goldenCorpusTotal: 1,
      statutoryRuleMatrixPaths: 1,
      statutoryRuleCoverageFamilies: 1,
      visualQaExpectedScreenshots: expectedVisualSmokeScreenshotCount(),
      requiredOperationalGates: 1,
      openCriticalActions: 1,
      evidenceItems: ["source-law-snapshot-fingerprint", "source-law-traceability-index", "golden-filing-corpus", "golden-verifier-manifest", "audit-evidence-timeline", "visual-smoke-screenshots", "release-review-checklist"],
      releaseBlockers: ["Qualified accountant sign-off required"],
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
        sources: [source("frc-frs-105", "FRC FRS 105 current edition and amendments")],
      },
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
      expectedScreenshotCount: expectedVisualSmokeScreenshotCount(),
      layoutChecks: visualSmokeLayoutChecks,
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

function source(sourceId, title) {
  return {
    sourceId,
    title,
    effectiveDate: "2026-07-03",
    url: "https://www.frc.org.uk/",
  };
}
