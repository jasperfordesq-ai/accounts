import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { ProductionReadinessWorkbench } from "@/components/readiness/ProductionReadinessWorkbench";
import type { ProductionReadinessReport } from "@/lib/api";

describe("ProductionReadinessWorkbench", () => {
  it("turns backend readiness evidence into a full accountant checklist", () => {
    render(<ProductionReadinessWorkbench report={sampleReport()} />);

    expect(screen.getByRole("heading", { name: "Production Readiness Checklist" })).toBeInTheDocument();
    expect(screen.getAllByText("Review required").length).toBeGreaterThan(1);
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
    expect(screen.getByText("Expected CT:")).toBeInTheDocument();
    expect(screen.getByText("€718.75")).toBeInTheDocument();
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
    expect(screen.getAllByText("external-ros-validation").length).toBeGreaterThan(0);
    expect(screen.getAllByText("ixbrl-taxonomy-selection").length).toBeGreaterThan(0);
    expect(screen.getByRole("heading", { name: "Next assurance actions" })).toBeInTheDocument();
    expect(screen.getByText("Qualified accountant sign-off")).toBeInTheDocument();
    expect(screen.getByText("Risk 0")).toBeInTheDocument();
    expect(screen.getByText("accountant-review-gate")).toBeInTheDocument();
    expect(screen.getByText("Light/dark visual regression")).toBeInTheDocument();
    expect(screen.getByText("visual-qa-evidence")).toBeInTheDocument();
    expect(screen.getByText(/Sentry production error routing configured and reviewed/)).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Production completion map" })).toBeInTheDocument();
    expect(screen.getByRole("searchbox", { name: "Filter Production completion map" })).toBeInTheDocument();
    expect(screen.getByText("Backend code")).toBeInTheDocument();
    expect(screen.getByText("Frontend UI/UX")).toBeInTheDocument();
    expect(screen.getByText("Frontend code")).toBeInTheDocument();
    expect(screen.getByText(/Golden filing corpus proves PDF text/)).toBeInTheDocument();
    expect(screen.getByText(/Accountant workflow rail is visually coherent/)).toBeInTheDocument();
    expect(screen.getByText(/Typed API contract blocks frontend\/backend readiness drift/)).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Release review checklist" })).toBeInTheDocument();
    expect(screen.getByText("Named accountant final sign-off")).toBeInTheDocument();
    expect(screen.getByText("named-accountant-approval-record")).toBeInTheDocument();
    expect(screen.getByText("ci-production-stack-smoke-and-backup-restore")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Release verification manifest" })).toBeInTheDocument();
    expect(screen.getByRole("searchbox", { name: "Filter Release verification manifest" })).toBeInTheDocument();
    expect(screen.getByText("Backend golden corpus and statutory rules")).toBeInTheDocument();
    expect(screen.getByText("dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art")).toBeInTheDocument();
    expect(screen.getByText("PostgreSQL-gated audit durability tests")).toBeInTheDocument();
    expect(screen.getByText("environment-gated")).toBeInTheDocument();
    expect(screen.getByText(/ACCOUNTS_POSTGRES_TEST_CONNECTION/)).toBeInTheDocument();
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
    expect(screen.getByText("visual-smoke-manifest.json")).toBeInTheDocument();
    expect(screen.getByText("Light desktop")).toBeInTheDocument();
    expect(screen.getByText("Dark mobile")).toBeInTheDocument();
    expect(screen.getByText("Visible text overlap")).toBeInTheDocument();
    expect(screen.getByText("Route audit summary")).toBeInTheDocument();
    expect(screen.getAllByText("Table scanability").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Theme contrast").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Mobile density").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Filing review").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Capture key filing").length).toBeGreaterThan(0);
    expect(screen.getByText("workbenchPreview")).toBeInTheDocument();
    expect(screen.getAllByText("visual-smoke-screenshots").length).toBeGreaterThan(1);
    expect(screen.getByRole("heading", { name: "Production auditability" })).toBeInTheDocument();
    expect(screen.getByText("Who changed what")).toBeInTheDocument();
    expect(screen.getByText("Who approved what")).toBeInTheDocument();
    expect(screen.getByText("What was generated")).toBeInTheDocument();
    expect(screen.getByText("Tamper-evident audit chain")).toBeInTheDocument();
    expect(screen.getByText("audit-log-integrity-chain")).toBeInTheDocument();
    expect(screen.getAllByText("CroDocumentGenerated").length).toBeGreaterThan(1);
    expect(screen.getByRole("heading", { name: "Audit evidence timeline" })).toBeInTheDocument();
    expect(screen.getByText("Who changed what and when?")).toBeInTheDocument();
    expect(screen.getByText("At every authenticated write before regenerated outputs can be reviewed.")).toBeInTheDocument();
    expect(screen.getByText("Generated output audit event must exist before accountant approval can rely on the pack.")).toBeInTheDocument();
    expect(screen.getAllByText("generated-output-review").length).toBeGreaterThan(0);
    expect(screen.getByRole("heading", { name: "Production monitoring" })).toBeInTheDocument();
    expect(screen.getByText("Production error tracking")).toBeInTheDocument();
    expect(screen.getByText("Structured JSON logs")).toBeInTheDocument();
    expect(screen.getByText("Correlation id error responses")).toBeInTheDocument();
    expect(screen.getByText("Sentry-compatible")).toBeInTheDocument();
    expect(screen.getByText("Monitoring:ErrorTrackingDsn")).toBeInTheDocument();
    expect(screen.getByText("Primary on-call accountant and platform owner")).toBeInTheDocument();
    expect(screen.getByText("Block release if error events cannot be routed to the on-call owner.")).toBeInTheDocument();
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
    expect(screen.getByText("2 professional sign-offs")).toBeInTheDocument();
    expect(screen.getByText("1 manual handoff scenario")).toBeInTheDocument();
    expect(screen.getByText("2 automated verifiers")).toBeInTheDocument();
    expect(screen.getByText("micro-ltd, medium-audit-required")).toBeInTheDocument();
    expect(screen.getByText("Do not use for real filings")).toBeInTheDocument();
    expect(screen.getByText("1 critical blocker")).toBeInTheDocument();
    expect(screen.getByText("Golden corpus covered")).toBeInTheDocument();
    expect(screen.getByText("1 of 1 scenarios")).toBeInTheDocument();
    expect(screen.getByText("Visual QA evidence")).toBeInTheDocument();
    expect(screen.getByText("24 required screenshots")).toBeInTheDocument();
    expect(screen.getByText("Accountant acceptance")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Accountant acceptance criteria" })).toBeInTheDocument();
    expect(screen.getByText("Micro LTD accountant acceptance")).toBeInTheDocument();
    expect(screen.getByText("Medium handoff accountant acceptance")).toBeInTheDocument();
    expect(screen.getAllByText("Example Micro Limited").length).toBeGreaterThan(1);
    expect(screen.getByText("2025-01-01 to 2025-12-31")).toBeInTheDocument();
    expect(screen.getByText("Named qualified accountant must approve the generated pack before real filing use.")).toBeInTheDocument();
    expect(screen.getByText("Signed auditor report and manual handoff note reviewed by the qualified accountant.")).toBeInTheDocument();
    expect(screen.getAllByText("Acceptance verifier").length).toBeGreaterThan(0);
    expect(screen.getAllByText(/dotnet test Accounts\.slnx/).length).toBeGreaterThan(1);
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
        releaseGateCodes: ["external-ros-validation", "ixbrl-taxonomy-selection"],
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
      evidenceItems: ["source-law-snapshot-fingerprint", "source-law-traceability-index", "golden-filing-corpus", "golden-verifier-manifest", "audit-evidence-timeline", "visual-smoke-screenshots", "release-review-checklist", "release-verification-manifest", "accountant-acceptance-summary", "production-completion-map"],
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
      {
        scenarioCode: "medium-audit-required",
        label: "Medium handoff accountant acceptance",
        required: true,
        acceptanceStatus: "manual-handoff-review-required",
        reviewScope: ["Auditor handoff", "full accounts PDF", "iXBRL XML", "filing readiness"],
        requiredEvidence: ["Signed auditor report and manual handoff note reviewed by the qualified accountant."],
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
          {
            sourceId: "frc-frs-102",
            title: "FRC FRS 102 current edition and amendments",
            effectiveDate: "2026-07-03",
            url: "https://www.frc.org.uk/",
          },
        ],
      },
    ],
    accountantAcceptanceSummary: {
      scenarioCount: 2,
      automatedVerifierCount: 2,
      professionalSignOffRequiredCount: 2,
      manualHandoffScenarioCount: 1,
      releaseBlockingScenarioCodes: ["micro-ltd", "medium-audit-required"],
      requiredSignOffGates: [
        "Named qualified accountant must approve the generated pack before real filing use.",
        "Qualified accountant must record manual handoff acceptance before relying on outputs.",
      ],
      status: "qualified-accountant-review-required",
    },
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
          outputArtifacts: ["accounts PDF text", "CRO filing pack", "iXBRL XML", "accountant sign-off packet"],
          decisionGates: ["named qualified-accountant review", "director and secretary certification", "accountant sign-off packet state"],
          expectedValueChecks: ["Micro regime", "100% filing readiness", "well-formed iXBRL"],
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
          "Route-level loading/error states exist for main dynamic routes.",
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
        releaseChecklistEvidenceArtifact: "ci-production-stack-smoke-and-backup-restore",
        manualFallback: "Set ACCOUNTS_POSTGRES_TEST_CONNECTION to a disposable PostgreSQL database before relying on audit durability evidence.",
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
      {
        code: "structured-json-logs",
        label: "Structured JSON logs",
        provider: "ASP.NET Core JSON console",
        required: true,
        productionSafetyGate: "Monitoring:StructuredJsonConsole",
        evidenceCaptured: "Production log lines keep structured fields and scopes for indexing.",
        verification: "Program.cs switches to AddJsonConsole when the production safety gate is enabled.",
        alertRoute: "Platform operations log stream and release reviewer",
        failurePolicy: "Block release if production logs cannot be parsed by timestamp, level, category and correlation id.",
      },
      {
        code: "correlation-id-error-responses",
        label: "Correlation id error responses",
        provider: "ExceptionMiddleware",
        required: true,
        productionSafetyGate: "Monitoring:IncludeCorrelationId",
        evidenceCaptured: "Safe client errors include a correlation id that maps to the server exception log.",
        verification: "ExceptionMiddleware logs unhandled errors and returns the trace identifier.",
        alertRoute: "Support triage queue and platform owner",
        failurePolicy: "Block release if safe error responses omit correlation ids or server logs cannot be matched to the support ticket.",
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
      manifestFileName: "visual-smoke-manifest.json",
      expectedScreenshotCount: 24,
      layoutChecks: ["browser-console-errors", "page-horizontal-overflow", "visible-text-overlap"],
      reviewChecks: visualQaReviewChecks(),
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
