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
      },
      {
        code: "clg-charity",
        label: "CLG charity",
        companyScope: "Company limited by guarantee",
        expectedOutcome: "generated-pack-with-charity-gates",
        coverageStatus: "covered",
        evidenceTestNames: ["FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness"],
        assertions: ["charity evidence"],
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
  };
}
