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
    expect(screen.getByText("AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Golden evidence pack" })).toBeInTheDocument();
    expect(screen.getByText("accounts PDF text")).toBeInTheDocument();
    expect(screen.getByText("director and secretary certification")).toBeInTheDocument();
    expect(screen.getByText("well-formed iXBRL")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "FRC FRS 105 current edition and amendments" })).toHaveAttribute(
      "href",
      "https://www.frc.org.uk/",
    );
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
    expect(screen.getByText("Light/dark visual regression")).toBeInTheDocument();
    expect(screen.getByText(/Sentry production error routing configured and reviewed/)).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Statutory rules matrix" })).toBeInTheDocument();
    expect(screen.getByText("LTD micro")).toBeInTheDocument();
    expect(screen.getByText("CLG charity")).toBeInTheDocument();
    expect(screen.getByText("Medium audit-required")).toBeInTheDocument();
    expect(screen.getByText("Unsupported regulated/group")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "CRO financial statements requirements" })).toHaveAttribute(
      "href",
      "https://cro.ie/",
    );
    expect(screen.getByRole("heading", { name: "Visual QA coverage" })).toBeInTheDocument();
    expect(screen.getByText("20 screenshots")).toBeInTheDocument();
    expect(screen.getByText("Light desktop")).toBeInTheDocument();
    expect(screen.getByText("Dark mobile")).toBeInTheDocument();
    expect(screen.getByText("Filing review")).toBeInTheDocument();
    expect(screen.getByText("visual-smoke-screenshots")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Production auditability" })).toBeInTheDocument();
    expect(screen.getByText("Who changed what")).toBeInTheDocument();
    expect(screen.getByText("Who approved what")).toBeInTheDocument();
    expect(screen.getByText("What was generated")).toBeInTheDocument();
    expect(screen.getByText("Tamper-evident audit chain")).toBeInTheDocument();
    expect(screen.getByText("audit-log-integrity-chain")).toBeInTheDocument();
    expect(screen.getByText("CroDocumentGenerated")).toBeInTheDocument();
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
      sources: [
        {
          sourceId: "revenue-accepted-taxonomies",
          title: "Revenue accepted iXBRL taxonomies",
          effectiveDate: "2025-11-06",
          url: "https://www.revenue.ie/",
        },
      ],
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
        evidenceTestNames: ["AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl"],
        assertions: ["PDF text", "iXBRL parse"],
        evidencePack: {
          outputArtifacts: ["accounts PDF text", "CRO filing pack", "iXBRL XML"],
          decisionGates: ["named qualified-accountant review", "director and secretary certification"],
          expectedValueChecks: ["Micro regime", "100% filing readiness", "well-formed iXBRL"],
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
        status: "required",
        detail: "No real filing pack can be treated as final until a named qualified accountant has approved it.",
        evidenceRequired: "Named accountant approval recorded against the period.",
      },
      {
        code: "light-dark-visual-regression",
        label: "Light/dark visual regression",
        owner: "Engineering",
        priority: "high",
        status: "in-progress",
        detail: "Capture desktop and mobile screenshots for accountant routes in both themes.",
        evidenceRequired: "Screenshot review attached to CI or release checklist.",
      },
      {
        code: "production-monitoring",
        label: "Production monitoring",
        owner: "Operations",
        priority: "high",
        status: "required",
        detail: "Runtime errors must be visible before real filings are processed.",
        evidenceRequired: "Sentry production error routing configured and reviewed.",
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
    visualQaCoverage: {
      artifactName: "visual-smoke-screenshots",
      enforcement: "ci-production-smoke",
      expectedScreenshotCount: 20,
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
          openFilingTab: false,
        },
        {
          code: "filing-review",
          label: "Filing review",
          description: "Period workspace filing tab.",
          requiredText: "Filing readiness profile",
          openFilingTab: true,
        },
      ],
    },
  };
}
