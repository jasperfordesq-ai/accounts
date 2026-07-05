import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { ProductionReadinessPanel } from "@/components/ProductionReadinessPanel";
import type { ProductionReadinessReport } from "@/lib/api";

describe("ProductionReadinessPanel", () => {
  it("surfaces golden corpus, statutory source and operational gate evidence", () => {
    render(<ProductionReadinessPanel report={sampleReport()} />);

    expect(screen.getByText("Production Readiness")).toBeInTheDocument();
    expect(screen.getByText("Review required")).toBeInTheDocument();
    expect(screen.getByText("Micro LTD")).toBeInTheDocument();
    expect(screen.getByText("CLG charity")).toBeInTheDocument();
    expect(screen.getByText("Example Micro Limited")).toBeInTheDocument();
    expect(screen.getByText("2025-01-01 to 2025-12-31")).toBeInTheDocument();
    expect(screen.getByText("AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl")).toBeInTheDocument();
    expect(screen.getByText("No direct CRO/ROS submission automation")).toBeInTheDocument();
    expect(screen.getByText("Revenue accepted iXBRL taxonomies")).toBeInTheDocument();
    expect(screen.getByText("Qualified accountant sign-off")).toBeInTheDocument();
    expect(screen.getByText("Named accountant approval recorded against the period.")).toBeInTheDocument();
  });
});

function sampleReport(): ProductionReadinessReport {
  return {
    generatedAt: "2026-07-03T12:00:00Z",
    overallStatus: "review-required",
    companiesInDatabase: 3,
    periodsInDatabase: 4,
    sourceLawSnapshot: {
      snapshotDate: "2026-07-03",
      snapshotVersion: "irish-statutory-accounts-sources-2026-07-03",
      contentHash: "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
      sourceCount: 1,
      sources: [
        {
          sourceId: "revenue-accepted-taxonomies",
          title: "Revenue accepted iXBRL taxonomies",
          effectiveDate: "2025-11-06",
          url: "https://www.revenue.ie/",
        },
      ],
    },
    sourceLawTraceability: [
      {
        sourceId: "revenue-accepted-taxonomies",
        title: "Revenue accepted iXBRL taxonomies",
        effectiveDate: "2025-11-06",
        url: "https://www.revenue.ie/",
        inSnapshot: true,
        usedBy: ["statutory-rule-matrix:ltd-micro"],
        releaseGateCodes: ["external-ros-validation", "ixbrl-taxonomy-selection"],
      },
    ],
    assurancePacket: {
      packetId: "assurance-sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
      packetVersion: "production-assurance-packet-v1",
      status: "review-required",
      sourceLawSnapshotHash: "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
      goldenCorpusCovered: 2,
      goldenCorpusTotal: 2,
      statutoryRuleMatrixPaths: 1,
      statutoryRuleCoverageFamilies: 1,
      visualQaExpectedScreenshots: 24,
      requiredOperationalGates: 1,
      openCriticalActions: 1,
      evidenceItems: ["source-law-snapshot-fingerprint", "source-law-traceability-index", "golden-filing-corpus", "golden-verifier-manifest", "audit-evidence-timeline", "visual-smoke-screenshots", "release-review-checklist", "release-verification-manifest", "accountant-acceptance-summary", "production-completion-map"],
      releaseBlockers: ["Qualified accountant sign-off required"],
    },
    accountantAcceptanceCriteria: [
      {
        scenarioCode: "micro-ltd",
        label: "Micro LTD accountant acceptance",
        required: true,
        acceptanceStatus: "qualified-accountant-review-required",
        reviewScope: ["PDF wording", "iXBRL XML", "filing readiness profile"],
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
        sources: [
          {
            sourceId: "frc-frs-105",
            title: "FRC FRS 105 current edition and amendments",
            effectiveDate: "2026-07-03",
            url: "https://www.frc.org.uk/",
          },
        ],
      },
    ],
    accountantAcceptanceSummary: {
      scenarioCount: 2,
      automatedVerifierCount: 1,
      professionalSignOffRequiredCount: 1,
      manualHandoffScenarioCount: 0,
      releaseBlockingScenarioCodes: ["micro-ltd"],
      requiredSignOffGates: ["Named qualified accountant must approve the generated pack before real filing use."],
      status: "qualified-accountant-review-required",
    },
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
          outputArtifacts: ["accounts PDF text", "iXBRL XML"],
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
          sourceReferences: [
            {
              sourceId: "frc-frs-105",
              title: "FRC FRS 105 current edition and amendments",
              effectiveDate: "2026-07-03",
              url: "https://www.frc.org.uk/",
            },
          ],
        },
      },
      {
        code: "clg-charity",
        label: "CLG charity",
        companyScope: "Company limited by guarantee",
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
        evidenceTestNames: ["FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness"],
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
        assertions: ["charity evidence"],
        evidencePack: {
          outputArtifacts: ["CLG accounts PDF text", "charity readiness profile"],
          decisionGates: ["charity number", "charity annual return review"],
          expectedValueChecks: ["charity evidence satisfied"],
          expectedOutputs: {
            pdfTextMarkers: ["Dublin Community Support CLG", "Community support and education."],
            ixbrlRequiredTags: ["core:EntityCurrentLegalOrRegisteredName"],
            filingReadinessState: "ready-for-external-filing",
            expectedCorporationTax: 62.5,
            requiredNotes: ["Accounting Policies", "Charity reporting disclosures"],
            filingGateStates: ["charity number satisfied", "qualified-accountant review recorded"],
            signOffPacketState: "ready-for-external-filing",
          },
          expectedProofPoints: [
            {
              area: "filing-readiness",
              expectedEvidence: "Filing readiness confirms charity number, SoFA and trustees report evidence.",
              automatedVerifier: "FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness",
              required: true,
            },
          ],
          sourceReferences: [
            {
              sourceId: "charities-regulator-annual-report",
              title: "Charities Regulator annual report guidance",
              effectiveDate: "2026-07-03",
              url: "https://www.charitiesregulator.ie/",
            },
          ],
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
        sources: [
          {
            sourceId: "cro-financial-statements-requirements",
            title: "CRO financial statements requirements",
            effectiveDate: "2026-07-03",
            url: "https://cro.ie/",
          },
        ],
      },
    ],
    statutoryRulesCoverage: [
      {
        code: "size-classification-thresholds",
        ruleFamily: "Size classification",
        decisionUnderTest: "Two-of-three thresholds and current/prior movement produce the statutory size class.",
        coverageStatus: "covered",
        automatedVerifierNames: ["AccountsWorkflowTests.SizeClassification_FirstYearMicro_AllowsMicroAndAuditExemption"],
        edgeCases: ["two-of-three threshold rule"],
        sources: [
          {
            sourceId: "cro-financial-statements-requirements",
            title: "CRO financial statements requirements",
            effectiveDate: "2026-07-03",
            url: "https://cro.ie/",
          },
        ],
      },
    ],
    manualHandoffPaths: ["PLC and public-company workflows"],
    operationalGates: [
      {
        code: "no-direct-cro-ros-submission",
        label: "No direct CRO/ROS submission automation",
        required: true,
        status: "enforced",
        detail: "Workflow records states only.",
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
    completionTracks: [
      {
        code: "backend-code",
        label: "Backend code",
        ownerRole: "Engineering",
        status: "review-required",
        completionCriteria: ["Golden filing corpus proves statutory output gates."],
        currentEvidence: ["Backend golden corpus scenarios are covered."],
        nextActions: ["Run qualified-accountant acceptance on the golden corpus."],
        assuranceActionCodes: ["qualified-accountant-signoff"],
      },
      {
        code: "frontend-ui-ux",
        label: "Frontend UI/UX",
        ownerRole: "Product design",
        status: "in-progress",
        completionCriteria: ["Accountant workflow rail is visually coherent."],
        currentEvidence: ["Visual QA route audit covers the main routes."],
        nextActions: ["Review each screenshot route-by-route."],
        assuranceActionCodes: ["qualified-accountant-signoff"],
      },
      {
        code: "frontend-code",
        label: "Frontend code",
        ownerRole: "Frontend engineering",
        status: "in-progress",
        completionCriteria: ["Typed API contract blocks readiness drift."],
        currentEvidence: ["API client invariants validate readiness contracts."],
        nextActions: ["Continue extracting large route files."],
        assuranceActionCodes: ["qualified-accountant-signoff"],
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
        verification: "Audit log snapshots and integrity hash chain must cover each changed entity.",
        auditEventCodes: ["AdjustmentUpdated"],
        blockingGateCodes: ["working-paper-review"],
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
    ],
    visualQaCoverage: {
      artifactName: "visual-smoke-screenshots",
      enforcement: "ci-production-smoke",
      manifestFileName: "visual-smoke-manifest.json",
      expectedScreenshotCount: 24,
      layoutChecks: ["browser-console-errors", "page-horizontal-overflow", "visible-text-overlap"],
      reviewChecks: visualQaReviewChecks(),
      reviewProtocol: visualQaReviewProtocol(),
      themes: ["light", "dark"],
      viewports: [
        { name: "desktop", width: 1440, height: 1000 },
        { name: "mobile", width: 390, height: 844 },
      ],
      routes: visualQaRoutes(),
      routeAudits: visualQaRouteAudits(),
      artifacts: visualQaArtifacts(),
    },
  };
}

function accountantWorkflowStages() {
  return ["Setup", "Import", "Classify", "Year-End", "Statements", "Notes", "Review", "Filing"];
}

function visualQaReviewChecks() {
  return ["accountant-workflow-hierarchy", "table-scanability", "theme-contrast", "mobile-density", "loading-error-empty-states"];
}

function visualQaReviewProtocol(): ProductionReadinessReport["visualQaCoverage"]["reviewProtocol"] {
  return {
    protocolVersion: "visual-review-v1",
    reviewerRole: "Design reviewer",
    status: "required-review",
    signOffGate: "visual-qa-screenshot-review",
    failurePolicy: "Block release if any accountant workbench route has console errors, horizontal overflow, visible text overlap, inaccessible contrast, unreadable table density, or unresolved light/dark/mobile defects.",
    acceptanceCriteria: [
      "Every configured route is captured in light desktop, dark desktop, light mobile and dark mobile.",
      "No browser console errors, horizontal overflow or visible text overlap are present.",
      "Accountant workflow hierarchy, table scanability, theme contrast, mobile density and route states are professionally acceptable.",
      "A named visual QA reviewer records screenshot-manifest acceptance before real filing release.",
    ],
    requiredEvidence: [
      "visual-smoke-manifest.json",
      "24 visual smoke screenshots",
      "route audit summary",
      "named visual QA reviewer sign-off",
    ],
  };
}

function visualQaRoutes(): ProductionReadinessReport["visualQaCoverage"]["routes"] {
  return [
    {
      code: "dashboard",
      routeKey: "dashboard",
      label: "Dashboard",
      description: "Accountant queue and production readiness overview.",
      requiredText: "Production Readiness",
      workflowStages: accountantWorkflowStages(),
      openFilingTab: false,
    },
    {
      code: "production-readiness",
      routeKey: "readiness",
      label: "Production readiness",
      description: "Assurance checklist, statutory rules matrix, source snapshot and operational gates.",
      requiredText: "Production Readiness Checklist",
      workflowStages: ["Review", "Filing"],
      openFilingTab: false,
    },
    {
      code: "company-detail",
      routeKey: "company",
      label: "Company detail",
      description: "Company command centre, statutory profile, officers, charity facts and accounting periods.",
      requiredText: "Company command centre",
      workflowStages: ["Setup"],
      openFilingTab: false,
    },
    {
      code: "period-workspace",
      routeKey: "period",
      label: "Period workspace",
      description: "Import, classification, year-end, statements and filing readiness overview.",
      requiredText: "Filing readiness",
      workflowStages: accountantWorkflowStages(),
      openFilingTab: false,
    },
    {
      code: "filing-review",
      routeKey: "filing",
      label: "Filing review",
      description: "Period workspace filing tab.",
      requiredText: "Filing readiness profile",
      workflowStages: ["Review", "Filing"],
      openFilingTab: true,
    },
    {
      code: "workbench-preview",
      routeKey: "workbenchPreview",
      label: "Workbench preview",
      description: "Internal component preview for accountant workflow primitives and route states.",
      requiredText: "Workbench Component Preview",
      workflowStages: accountantWorkflowStages(),
      openFilingTab: false,
    },
  ];
}

function visualQaArtifacts(): ProductionReadinessReport["visualQaCoverage"]["artifacts"] {
  const layoutChecks = ["browser-console-errors", "page-horizontal-overflow", "visible-text-overlap"];
  return ["light", "dark"].flatMap((theme) =>
    ["desktop", "mobile"].flatMap((viewportName) =>
      visualQaRoutes().map((route) => {
        const fileName = `${route.code}-${theme}-${viewportName}.png`;
        return {
          routeCode: route.code,
          routeKey: route.routeKey,
          theme,
          viewportName,
          fileName,
          artifactPath: `artifacts/visual-smoke/${fileName}`,
          requiredText: route.requiredText,
          openFilingTab: route.openFilingTab,
          reviewStatus: "required-review",
          layoutChecks,
        };
      }),
    ),
  );
}

function visualQaRouteAudits(): ProductionReadinessReport["visualQaCoverage"]["routeAudits"] {
  return visualQaRoutes().map((route) => ({
    routeCode: route.code,
    routeKey: route.routeKey,
    label: route.label,
    workflowStages: route.workflowStages,
    screenshotCount: 4,
    reviewStatus: "required-review",
    reviewChecks: visualQaReviewChecks(),
  }));
}
