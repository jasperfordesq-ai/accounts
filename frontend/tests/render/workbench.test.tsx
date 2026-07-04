import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { DataTable, EvidenceChecklist, IssueDigest, ReviewPanel, StatusBadge, WorkflowRail } from "@/components/workbench";

describe("workbench primitives", () => {
  it("renders evidence checklist completion and required states", () => {
    render(
      <EvidenceChecklist
        items={[
          {
            code: "cro-signatories",
            label: "Active director and company secretary recorded",
            required: true,
            satisfied: true,
            detail: "Director and secretary present.",
          },
          {
            code: "accountant-review",
            label: "Named qualified-accountant review and approval recorded",
            required: true,
            satisfied: false,
            detail: "Required",
          },
        ]}
      />,
    );

    expect(screen.getByText("Active director and company secretary recorded")).toBeInTheDocument();
    expect(screen.getByText("Named qualified-accountant review and approval recorded")).toBeInTheDocument();
    expect(screen.getByText("Complete")).toBeInTheDocument();
    expect(screen.getAllByText("Required").length).toBeGreaterThanOrEqual(2);
  });

  it("renders concise status badges", () => {
    render(<StatusBadge tone="warn">Recorded only</StatusBadge>);

    expect(screen.getByText("Recorded only")).toBeInTheDocument();
  });

  it("renders an eight-stage accountant workflow rail with linked route steps", () => {
    render(
      <WorkflowRail
        items={[
          { id: "setup", label: "Setup", detail: "Company profile ready", state: "done", href: "/companies/1" },
          { id: "import", label: "Import", detail: "10 transactions loaded", state: "done" },
          { id: "classify", label: "Classify", detail: "Micro", state: "done", href: "/companies/1/periods/2/classify" },
          { id: "year-end", label: "Year-End", detail: "Evidence in progress", state: "active", href: "/companies/1/periods/2/year-end" },
          { id: "statements", label: "Statements", detail: "Balance sheet agrees", state: "done", href: "/companies/1/periods/2/statements" },
          { id: "notes", label: "Notes", detail: "Required disclosures", state: "todo", href: "/companies/1/periods/2/notes" },
          { id: "review", label: "Review", detail: "Accountant approval required", state: "blocked" },
          { id: "filing", label: "Filing", detail: "CRO pack blocked", state: "blocked" },
        ]}
      />,
    );

    expect(screen.getByRole("navigation", { name: "Accounting Workflow" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Setup/ })).toHaveAttribute("href", "/companies/1");
    expect(screen.getByRole("link", { name: /Year-End/ })).toHaveAttribute("aria-current", "step");
    expect(screen.getAllByText("Blocked")).toHaveLength(2);
    expect(screen.getByText("8 stages")).toBeInTheDocument();
  }, 15_000);

  it("allows dense tables inside grid panels to shrink on mobile", () => {
    const { container } = render(
      <div className="grid">
        <ReviewPanel title="Evidence table">
          <DataTable
            columns={["Source", "Evidence"]}
            rows={[
              [
                "Revenue accepted iXBRL taxonomies",
                "AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl",
              ],
            ]}
          />
        </ReviewPanel>
      </div>,
    );

    expect(container.querySelector("section.min-w-0")).toBeInTheDocument();
    expect(container.querySelector(".overflow-x-auto.min-w-0")).toBeInTheDocument();
  });

  it("labels each table cell so mobile card rows retain column context", () => {
    const { container } = render(
      <DataTable
        columns={["Company", "Deadline", "Next action"]}
        rows={[
          ["CI Visual Accounts Limited", "Revenue due 23 Sept 2025", "Continue workbench"],
        ]}
      />,
    );

    const table = container.querySelector(".workbench-data-table");
    expect(table).toBeInTheDocument();
    expect(table).toHaveAttribute("data-responsive", "card");
    expect(container.querySelector('td[data-label="Company"]')).toHaveTextContent("CI Visual Accounts Limited");
    expect(container.querySelector('td[data-label="Deadline"]')).toHaveTextContent("Revenue due 23 Sept 2025");
    expect(container.querySelector('td[data-label="Next action"]')).toHaveTextContent("Continue workbench");
  });

  it("renders a compact issue digest with priority items and collapsed overflow", () => {
    render(
      <IssueDigest
        title="Readiness issue digest"
        description="Resolve priority blockers before treating the accounts pack as final."
        blockers={[
          "Size classification not completed",
          "Filing regime not determined",
          "CRO accounts PDF not generated",
          "Named qualified-accountant approval required",
          "Internal iXBRL checks must pass",
        ]}
        warnings={[
          "Revenue deadline passed and late filing exposure must be reviewed.",
        ]}
      />,
    );

    expect(screen.getByText("Readiness issue digest")).toBeInTheDocument();
    expect(screen.getByText("5 blockers")).toBeInTheDocument();
    expect(screen.getByText("1 warning")).toBeInTheDocument();
    expect(screen.getByText("Priority blockers")).toBeInTheDocument();
    expect(screen.getByText("Size classification not completed")).toBeInTheDocument();
    expect(screen.getByText("2 more blockers")).toBeInTheDocument();
    expect(screen.getByText("Revenue deadline passed and late filing exposure must be reviewed.")).toBeInTheDocument();
  });
});
