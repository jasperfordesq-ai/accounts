import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { ProductionReadinessWorkbench } from "@/components/readiness/ProductionReadinessWorkbench";
import type { ProductionReadinessReport } from "@/lib/api";

describe("ProductionReadinessWorkbench", () => {
  it("turns backend readiness evidence into a full accountant checklist", () => {
    render(<ProductionReadinessWorkbench report={sampleReport()} />);

    expect(screen.getByRole("heading", { name: "Production Readiness Checklist" })).toBeInTheDocument();
    expect(screen.getByText("Review required")).toBeInTheDocument();
    expect(screen.getByText("3 companies")).toBeInTheDocument();
    expect(screen.getByText("4 periods")).toBeInTheDocument();
    expect(screen.getByRole("searchbox", { name: "Filter Next assurance actions" })).toBeInTheDocument();
    expect(screen.getByRole("searchbox", { name: "Filter Statutory rules matrix" })).toBeInTheDocument();
    expect(screen.getByRole("searchbox", { name: "Filter Golden filing corpus" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Golden filing corpus" })).toBeInTheDocument();
    expect(screen.getAllByText("Micro LTD")).toHaveLength(2);
    expect(screen.getAllByText("AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl").length).toBeGreaterThan(1);
    expect(screen.getByRole("heading", { name: "Golden evidence pack" })).toBeInTheDocument();
    expect(screen.getByText("accounts PDF text")).toBeInTheDocument();
    expect(screen.getByText("accountant sign-off packet")).toBeInTheDocument();
    expect(screen.getByText("accountant-signoff-packet")).toBeInTheDocument();
    expect(screen.getByText("Sign-off packet shows reviewer state, open blockers and allowed next actions.")).toBeInTheDocument();
    expect(screen.getByText("director and secretary certification")).toBeInTheDocument();
    expect(screen.getByText("well-formed iXBRL")).toBeInTheDocument();
    expect(screen.getByText("Expected proof points")).toBeInTheDocument();
    expect(screen.getByText("PDF text contains company name and micro statutory statement.")).toBeInTheDocument();
    expect(screen.getByText("signatory-gates")).toBeInTheDocument();
    expect(screen.getAllByRole("link", { name: "FRC FRS 105 current edition and amendments" }))
      .toEqual(expect.arrayContaining([expect.objectContaining({ href: "https://www.frc.org.uk/" })]));
    expect(screen.getByText("Unsupported/manual handoff")).toBeInTheDocument();
    expect(screen.getByText("PLC and public-company workflows")).toBeInTheDocument();
    expect(screen.getByText("Operations and security")).toBeInTheDocument();
    expect(screen.getByText("Named qualified-accountant review")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Source-backed statutory rules" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Revenue accepted iXBRL taxonomies" })).toHaveAttribute(
      "href",
      "https://www.revenue.ie/",
    );
    expect(screen.getByRole("heading", { name: "Next assurance actions" })).toBeInTheDocument();
    expect(screen.getByText("Qualified accountant sign-off")).toBeInTheDocument();
    expect(screen.getByText("Risk 0")).toBeInTheDocument();
    expect(screen.getByText("accountant-review-gate")).toBeInTheDocument();
    expect(screen.getByText("Light/dark visual regression")).toBeInTheDocument();
    expect(screen.getByText("visual-qa-evidence")).toBeInTheDocument();
    expect(screen.getByText(/Sentry production error routing configured and reviewed/)).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Release review checklist" })).toBeInTheDocument();
    expect(screen.getByText("Named accountant final sign-off")).toBeInTheDocument();
    expect(screen.getByText("named-accountant-approval-record")).toBeInTheDocument();
    expect(screen.getByText("ci-production-stack-smoke-and-backup-restore")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Statutory rules matrix" })).toBeInTheDocument();
    expect(screen.getByText("LTD micro")).toBeInTheDocument();
    expect(screen.getByText("CLG charity")).toBeInTheDocument();
    expect(screen.getByText("Medium audit-required")).toBeInTheDocument();
    expect(screen.getByText("Unsupported regulated/group")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Statutory rules coverage" })).toBeInTheDocument();
    expect(screen.getByRole("searchbox", { name: "Filter Statutory rules coverage" })).toBeInTheDocument();
    expect(screen.getByText("Size classification")).toBeInTheDocument();
    expect(screen.getByText("two-of-three threshold rule")).toBeInTheDocument();
    expect(screen.getByText("AccountsWorkflowTests.SizeClassification_FirstYearMicro_AllowsMicroAndAuditExemption")).toBeInTheDocument();
    expect(screen.getAllByRole("link", { name: "CRO financial statements requirements" }))
      .toEqual(expect.arrayContaining([expect.objectContaining({ href: "https://cro.ie/" })]));
    expect(screen.getByRole("heading", { name: "Visual QA coverage" })).toBeInTheDocument();
    expect(screen.getByText("24 screenshots")).toBeInTheDocument();
    expect(screen.getByText("Light desktop")).toBeInTheDocument();
    expect(screen.getByText("Dark mobile")).toBeInTheDocument();
    expect(screen.getByText("Visible text overlap")).toBeInTheDocument();
    expect(screen.getByText("Filing review")).toBeInTheDocument();
    expect(screen.getAllByText("visual-smoke-screenshots").length).toBeGreaterThan(1);
    expect(screen.getByRole("heading", { name: "Production auditability" })).toBeInTheDocument();
    expect(screen.getByText("Who changed what")).toBeInTheDocument();
    expect(screen.getByText("Who approved what")).toBeInTheDocument();
    expect(screen.getByText("What was generated")).toBeInTheDocument();
    expect(screen.getByText("Tamper-evident audit chain")).toBeInTheDocument();
    expect(screen.getByText("audit-log-integrity-chain")).toBeInTheDocument();
    expect(screen.getByText("CroDocumentGenerated")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Production monitoring" })).toBeInTheDocument();
    expect(screen.getByText("Production error tracking")).toBeInTheDocument();
    expect(screen.getByText("Structured JSON logs")).toBeInTheDocument();
    expect(screen.getByText("Correlation id error responses")).toBeInTheDocument();
    expect(screen.getByText("Sentry-compatible")).toBeInTheDocument();
    expect(screen.getByText("Monitoring:ErrorTrackingDsn")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Dependency policy controls" })).toBeInTheDocument();
    expect(screen.getByText("Frontend dependency vulnerability audit")).toBeInTheDocument();
    expect(screen.getByText("CI action version hygiene")).toBeInTheDocument();
    expect(screen.getByText("Fail the release for moderate, high or critical npm advisories.")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Deployment safety controls" })).toBeInTheDocument();
    expect(screen.getByText("Controlled production migrations")).toBeInTheDocument();
    expect(screen.getByText("Production demo seed blocking")).toBeInTheDocument();
    expect(screen.getByText("Backup restore drill")).toBeInTheDocument();
    expect(screen.getByText("Fail production startup if demo seed data is enabled outside development.")).toBeInTheDocument();
    expect(screen.getByText("sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")).toBeInTheDocument();
    expect(screen.getByText("1 pinned source")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Production assurance packet" })).toBeInTheDocument();
    expect(screen.getByText("assurance-sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")).toBeInTheDocument();
    expect(screen.getByText("Golden corpus 1/1")).toBeInTheDocument();
    expect(screen.getAllByText("Qualified accountant sign-off required").length).toBeGreaterThan(1);
    expect(screen.getByRole("heading", { name: "Release decision summary" })).toBeInTheDocument();
    expect(screen.getByText("Do not use for real filings")).toBeInTheDocument();
    expect(screen.getByText("1 critical blocker")).toBeInTheDocument();
    expect(screen.getByText("Golden corpus covered")).toBeInTheDocument();
    expect(screen.getByText("1 of 1 scenarios")).toBeInTheDocument();
    expect(screen.getByText("Visual QA evidence")).toBeInTheDocument();
    expect(screen.getByText("24 required screenshots")).toBeInTheDocument();
    expect(screen.getByText("Accountant acceptance")).toBeInTheDocument();
    expect(screen.getByText("2 scenarios require sign-off")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Accountant acceptance criteria" })).toBeInTheDocument();
    expect(screen.getByText("Micro LTD accountant acceptance")).toBeInTheDocument();
    expect(screen.getByText("Medium handoff accountant acceptance")).toBeInTheDocument();
    expect(screen.getByText("Named qualified accountant must approve the generated pack before real filing use.")).toBeInTheDocument();
    expect(screen.getByText("Signed auditor report and manual handoff note reviewed by the qualified accountant.")).toBeInTheDocument();
  }, 45000);
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
        usedBy: ["statutory-rule-matrix:ltd-micro", "golden-corpus:micro-ltd"],
      },
    ],
    assurancePacket: {
      packetId: "assurance-sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
      packetVersion: "production-assurance-packet-v1",
      status: "review-required",
      sourceLawSnapshotHash: "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
      goldenCorpusCovered: 1,
      goldenCorpusTotal: 1,
      statutoryRuleMatrixPaths: 2,
      statutoryRuleCoverageFamilies: 1,
      visualQaExpectedScreenshots: 24,
      requiredOperationalGates: 1,
      openCriticalActions: 1,
      evidenceItems: ["source-law-snapshot-fingerprint", "source-law-traceability-index", "golden-filing-corpus", "visual-smoke-screenshots", "release-review-checklist"],
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
        sources: [
          {
            sourceId: "frc-frs-105",
            title: "FRC FRS 105 current edition and amendments",
            effectiveDate: "2026-07-03",
            url: "https://www.frc.org.uk/",
          },
        ],
      },
      {
        scenarioCode: "medium-audit-required",
        label: "Medium handoff accountant acceptance",
        required: true,
        acceptanceStatus: "manual-handoff-review-required",
        reviewScope: ["Auditor handoff", "full accounts PDF", "iXBRL XML", "filing readiness"],
        requiredEvidence: ["Signed auditor report and manual handoff note reviewed by the qualified accountant."],
        requiredSignOffGate: "Qualified accountant must record manual handoff acceptance before relying on outputs.",
        sources: [
          {
            sourceId: "frc-frs-102",
            title: "FRC FRS 102 current edition and amendments",
            effectiveDate: "2026-07-03",
            url: "https://www.frc.org.uk/",
          },
        ],
      },
    ],
    areas: [
      {
        code: "backend-accounting-engine",
        label: "Backend accounting engine",
        status: "hardened",
        detail: "Golden-path coverage exercises outputs and gates.",
      },
      {
        code: "source-backed-statutory-rules",
        label: "Source-backed statutory rules",
        status: "hardened",
        detail: "Classification and filing decisions include legal source references.",
      },
    ],
    goldenFilingCorpus: [
      {
        code: "micro-ltd",
        label: "Micro LTD",
        companyScope: "Private company limited by shares",
        expectedOutcome: "generated-pack",
        coverageStatus: "covered",
        evidenceTestNames: ["AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl"],
        assertions: ["PDF text", "iXBRL parse"],
        evidencePack: {
          outputArtifacts: ["accounts PDF text", "CRO filing pack", "iXBRL XML", "accountant sign-off packet"],
          decisionGates: ["named qualified-accountant review", "director and secretary certification", "accountant sign-off packet state"],
          expectedValueChecks: ["Micro regime", "100% filing readiness", "well-formed iXBRL"],
          expectedProofPoints: [
            {
              area: "pdf-text",
              expectedEvidence: "PDF text contains company name and micro statutory statement.",
              automatedVerifier: "AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl",
              required: true,
            },
            {
              area: "signatory-gates",
              expectedEvidence: "Director and secretary certification gates remain required before filing use.",
              automatedVerifier: "AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl",
              required: true,
            },
            {
              area: "accountant-signoff-packet",
              expectedEvidence: "Sign-off packet shows reviewer state, open blockers and allowed next actions.",
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
    ],
    manualHandoffPaths: ["PLC and public-company workflows"],
    operationalGates: [
      {
        code: "accountant-review",
        label: "Named qualified-accountant review",
        required: true,
        status: "enforced",
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
      {
        code: "production-monitoring",
        label: "Production monitoring",
        owner: "Operations",
        priority: "high",
        riskRank: 20,
        evidenceStage: "operations-evidence",
        status: "required",
        detail: "Runtime errors must be visible before real filings are processed.",
        evidenceRequired: "Sentry production error routing configured and reviewed.",
      },
      {
        code: "light-dark-visual-regression",
        label: "Light/dark visual regression",
        owner: "Engineering",
        priority: "high",
        riskRank: 30,
        evidenceStage: "visual-qa-evidence",
        status: "in-progress",
        detail: "Capture desktop and mobile screenshots for accountant routes in both themes.",
        evidenceRequired: "Screenshot review attached to CI or release checklist.",
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
        operationalGateCode: "accountant-review",
        auditEventCodes: ["CroFilingStatusChanged"],
        detail: "Named professional approval must be recorded against the period.",
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
        operationalGateCode: "",
        auditEventCodes: [],
        detail: "Production stack smoke and backup restore evidence must be attached before release.",
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
        operationalGateCode: "",
        auditEventCodes: [],
        detail: "Desktop and mobile screenshots in light and dark mode must be reviewed.",
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
      {
        code: "who-approved-what",
        label: "Who approved what",
        required: true,
        enforcement: "workflow-gates-plus-audit-log-integrity-chain",
        evidenceCaptured: "Named reviewer identity, approval timestamps and filing status transitions.",
        verification: "Approval endpoints write audit events after readiness gates pass.",
        auditEventCodes: ["AdjustmentApproved", "CroFilingStatusChanged"],
      },
      {
        code: "what-was-generated",
        label: "What was generated",
        required: true,
        enforcement: "server-side-generation-events",
        evidenceCaptured: "Generated accounts documents, iXBRL checks and period linkage.",
        verification: "Generation events are recorded before evidence is marked satisfied.",
        auditEventCodes: ["CroDocumentGenerated"],
      },
      {
        code: "tamper-evident-chain",
        label: "Tamper-evident audit chain",
        required: true,
        enforcement: "audit-log-integrity-chain-and-signed-checkpoint",
        evidenceCaptured: "Previous hash, current hash, checkpoint key id and signed checkpoint anchor.",
        verification: "Signed checkpoint verifies the latest company audit entry.",
        auditEventCodes: ["IxbrlInternalCheckCompleted"],
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
      },
      {
        code: "structured-json-logs",
        label: "Structured JSON logs",
        provider: "ASP.NET Core JSON console",
        required: true,
        productionSafetyGate: "Monitoring:StructuredJsonConsole",
        evidenceCaptured: "Production log lines keep structured fields and scopes for indexing.",
        verification: "Program.cs switches to AddJsonConsole when the production safety gate is enabled.",
      },
      {
        code: "correlation-id-error-responses",
        label: "Correlation id error responses",
        provider: "ExceptionMiddleware",
        required: true,
        productionSafetyGate: "Monitoring:IncludeCorrelationId",
        evidenceCaptured: "Safe client errors include a correlation id that maps to the server exception log.",
        verification: "ExceptionMiddleware logs unhandled errors and returns the trace identifier.",
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
      {
        code: "ci-action-version-hygiene",
        label: "CI action version hygiene",
        required: true,
        enforcement: "Workflow Hygiene job runs node scripts/verify-ci-actions.mjs before downstream jobs.",
        evidenceCaptured: "GitHub Actions used by CI are checked for explicit version hygiene.",
        verification: ".github/workflows/ci.yml Workflow Hygiene job.",
        failurePolicy: "Fail the release if workflow actions are unpinned or bypass the hygiene verifier.",
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
      {
        code: "backup-restore-drill",
        label: "Backup restore drill",
        required: true,
        enforcement: "CI production stack smoke runs scripts/backup-postgres.ps1 and scripts/verify-postgres-backup.ps1.",
        evidenceCaptured: "Production backup dump, sha256 sidecar and restore verification are produced during CI.",
        verification: ".github/workflows/ci.yml Run production backup restore drill step.",
        failurePolicy: "Fail the release if backup creation, checksum verification or restore verification fails.",
      },
    ],
    statutoryRuleMatrix: [
      {
        code: "ltd-micro",
        companyScope: "LTD micro",
        sizeOrRegime: "Micro / FRS 105",
        supportLevel: "supported",
        requiredEvidence: ["size classification", "director and secretary"],
        requiredOutputs: ["micro accounts PDF", "iXBRL"],
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
      {
        code: "clg-charity",
        companyScope: "CLG charity",
        sizeOrRegime: "Small / charity annual return",
        supportLevel: "supported-with-review",
        requiredEvidence: ["charity number", "SoFA"],
        requiredOutputs: ["accounts PDF", "charity annual return support"],
        manualHandoffGates: ["charity annual return review"],
        sources: [
          {
            sourceId: "charities-regulator-annual-report",
            title: "Charities Regulator annual report guidance",
            effectiveDate: "2026-07-03",
            url: "https://www.charitiesregulator.ie/",
          },
        ],
      },
      {
        code: "medium-audit-required",
        companyScope: "Medium audit-required",
        sizeOrRegime: "Medium / full accounts",
        supportLevel: "manual-handoff",
        requiredEvidence: ["signed auditor report"],
        requiredOutputs: ["full accounts pack"],
        manualHandoffGates: ["auditor handoff required"],
        sources: [
          {
            sourceId: "frc-frs-102",
            title: "FRC FRS 102 current edition and amendments",
            effectiveDate: "2026-07-03",
            url: "https://www.frc.org.uk/",
          },
        ],
      },
      {
        code: "unsupported-regulated-group",
        companyScope: "Unsupported regulated/group",
        sizeOrRegime: "Regulated, group or public company",
        supportLevel: "unsupported",
        requiredEvidence: ["manual professional ownership"],
        requiredOutputs: ["manual handoff record"],
        manualHandoffGates: ["fail closed before filing workflow"],
        sources: [
          {
            sourceId: "cro-group-company",
            title: "CRO group company financial statements requirements",
            effectiveDate: "2026-07-03",
            url: "https://cro.ie/group",
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
        edgeCases: ["two-of-three threshold rule", "current and prior year classification rule"],
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
    visualQaCoverage: {
      artifactName: "visual-smoke-screenshots",
      enforcement: "ci-production-smoke",
      expectedScreenshotCount: 24,
      layoutChecks: ["browser-console-errors", "page-horizontal-overflow", "visible-text-overlap"],
      themes: ["light", "dark"],
      viewports: [
        { name: "desktop", width: 1440, height: 1000 },
        { name: "mobile", width: 390, height: 844 },
      ],
      routes: [
        {
          code: "dashboard",
          label: "Dashboard",
          description: "Accountant queue and production readiness overview.",
          requiredText: "Production Readiness",
          workflowStages: accountantWorkflowStages(),
          openFilingTab: false,
        },
        {
          code: "filing-review",
          label: "Filing review",
          description: "Period workspace filing tab.",
          requiredText: "Filing readiness profile",
          workflowStages: ["Review", "Filing"],
          openFilingTab: true,
        },
        {
          code: "workbench-preview",
          label: "Workbench preview",
          description: "Internal component preview for accountant workflow primitives and route states.",
          requiredText: "Workbench Component Preview",
          workflowStages: accountantWorkflowStages(),
          openFilingTab: false,
        },
      ],
    },
  };
}

function accountantWorkflowStages() {
  return ["Setup", "Import", "Classify", "Year-End", "Statements", "Notes", "Review", "Filing"];
}
