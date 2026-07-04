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
      evidenceItems: ["source-law-snapshot-fingerprint", "golden-filing-corpus", "visual-smoke-screenshots"],
      releaseBlockers: ["Qualified accountant sign-off required"],
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
        evidenceTestNames: ["AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl"],
        assertions: ["PDF text", "iXBRL parse"],
        evidencePack: {
          outputArtifacts: ["accounts PDF text", "iXBRL XML"],
          decisionGates: ["named qualified-accountant review"],
          expectedValueChecks: ["well-formed iXBRL"],
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
        evidenceTestNames: ["FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness"],
        assertions: ["charity evidence"],
        evidencePack: {
          outputArtifacts: ["CLG accounts PDF text", "charity readiness profile"],
          decisionGates: ["charity number", "charity annual return review"],
          expectedValueChecks: ["charity evidence satisfied"],
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
        status: "required",
        detail: "No real filing pack can be treated as final until a named qualified accountant has approved it.",
        evidenceRequired: "Named accountant approval recorded against the period.",
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
          openFilingTab: false,
        },
        {
          code: "workbench-preview",
          label: "Workbench preview",
          description: "Internal component preview for accountant workflow primitives and route states.",
          requiredText: "Workbench Component Preview",
          openFilingTab: false,
        },
      ],
    },
  };
}
