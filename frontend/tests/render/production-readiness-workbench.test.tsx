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
    expect(screen.getByText("Golden filing corpus")).toBeInTheDocument();
    expect(screen.getByText("Micro LTD")).toBeInTheDocument();
    expect(screen.getByText("AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl")).toBeInTheDocument();
    expect(screen.getByText("Unsupported/manual handoff")).toBeInTheDocument();
    expect(screen.getByText("PLC and public-company workflows")).toBeInTheDocument();
    expect(screen.getByText("Operations and security")).toBeInTheDocument();
    expect(screen.getByText("Named qualified-accountant review")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Source-backed statutory rules" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Revenue accepted iXBRL taxonomies" })).toHaveAttribute(
      "href",
      "https://www.revenue.ie/",
    );
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
  };
}
