import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import {
  ActionLink,
  DataGrid,
  DataTable,
  EvidenceChecklist,
  FilingActionBar,
  HorizontalScrollRegion,
  IssueDigest,
  LegalSourceList,
  MoneyInput,
  PageShell,
  PermissionDeniedPanel,
  ReleaseBlockerSummary,
  ReviewPanel,
  StatusBadge,
  WorkbenchEmptyState,
  WorkbenchErrorState,
  WorkbenchLoadingState,
  WorkflowDecisionSummary,
  WorkflowRail,
} from "@/components/workbench";

describe("workbench primitives", () => {
  it("renders button-styled navigation as one semantic link", () => {
    render(
      <ActionLink href="/companies/new" variant="primary">
        Create company
      </ActionLink>,
    );

    expect(screen.getByRole("link", { name: "Create company" })).toHaveAttribute("href", "/companies/new");
    expect(screen.queryByRole("button", { name: "Create company" })).not.toBeInTheDocument();
  });

  it("renders a stable page shell with back navigation, metadata, actions and constrained content", () => {
    render(
      <PageShell
        title="Filing readiness review"
        subtitle="Accountant-facing evidence before the generated pack can be trusted."
        backHref="/companies/1"
        backLabel="Company workspace"
        meta={<StatusBadge tone="warn">Review required</StatusBadge>}
        actions={<button type="button">Export evidence</button>}
      >
        <ReviewPanel title="Evidence summary">Golden corpus evidence is linked.</ReviewPanel>
      </PageShell>,
    );

    expect(screen.getByRole("link", { name: "Company workspace" })).toHaveAttribute("href", "/companies/1");
    expect(screen.getByRole("heading", { name: "Filing readiness review" })).toBeInTheDocument();
    expect(screen.getByText("Accountant-facing evidence before the generated pack can be trusted.")).toBeInTheDocument();
    expect(screen.getByText("Review required")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Export evidence" })).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Evidence summary" })).toBeInTheDocument();
  });

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

  it("summarises and groups evidence so filing reviewers can scan open gates first", () => {
    render(
      <EvidenceChecklist
        items={[
          {
            code: "cro-pdf",
            label: "CRO accounts PDF generated",
            required: true,
            satisfied: false,
            detail: "Generate the CRO accounts PDF from the server workflow.",
          },
          {
            code: "external-ros-validation",
            label: "External ROS validation evidence",
            required: false,
            satisfied: false,
            detail: "Manual evidence remains open.",
          },
          {
            code: "cro-signatories",
            label: "Director and secretary certification",
            required: true,
            satisfied: true,
            detail: "Director and secretary present.",
          },
        ]}
      />,
    );

    const checklist = screen.getByRole("region", { name: "Evidence checklist" });
    expect(within(checklist).getByText("Evidence progress")).toBeInTheDocument();
    expect(within(checklist).getByText("1 open required")).toBeInTheDocument();
    expect(within(checklist).getByText("1 advisory")).toBeInTheDocument();
    expect(within(checklist).getByText("1 complete")).toBeInTheDocument();
    expect(within(checklist).getByText("Open evidence")).toBeInTheDocument();
    expect(within(checklist).getByText("Advisory evidence")).toBeInTheDocument();
    expect(within(checklist).getByText("Completed evidence")).toBeInTheDocument();

    const openGroup = within(screen.getByRole("group", { name: "Open evidence" }));
    const advisoryGroup = within(screen.getByRole("group", { name: "Advisory evidence" }));
    const completedGroup = within(screen.getByRole("group", { name: "Completed evidence" }));

    expect(openGroup.getByText("CRO accounts PDF generated")).toBeInTheDocument();
    expect(advisoryGroup.getByText("External ROS validation evidence")).toBeInTheDocument();
    expect(completedGroup.getByText("Director and secretary certification")).toBeInTheDocument();
  });

  it("renders concise status badges", () => {
    render(<StatusBadge tone="warn">Recorded only</StatusBadge>);

    expect(screen.getByText("Recorded only")).toBeInTheDocument();
  });

  it("renders production release blockers as a reusable accountant workbench summary", () => {
    render(
      <ReleaseBlockerSummary
        blockers={[
          {
            code: "accountant-signoff",
            trackCode: "backend",
            trackLabel: "Backend code",
            severity: "critical",
            riskRank: 1,
            blockingIssue: "Qualified accountant sign-off required",
            evidenceArtifact: "named-accountant-approval-record",
            nextAction: "Run qualified-accountant acceptance on the golden corpus.",
            blocksRelease: true,
          },
          {
            code: "visual-qa",
            trackCode: "frontend-ux",
            trackLabel: "Frontend UI/UX",
            severity: "high",
            riskRank: 7,
            blockingIssue: "Light and dark visual QA sign-off required",
            evidenceArtifact: "light-dark-screenshot-review",
            nextAction: "Complete production seeded-data screenshot review.",
            blocksRelease: true,
          },
        ]}
      />,
    );

    const summary = screen.getByRole("region", { name: "Production release blockers" });

    expect(summary).toHaveAttribute("data-workbench-release-blocker-summary", "true");
    expect(within(summary).getByText("2 blockers")).toBeInTheDocument();
    expect(within(summary).getByText("Backend code")).toBeInTheDocument();
    expect(within(summary).getByText("Qualified accountant sign-off required")).toBeInTheDocument();
    expect(within(summary).getByText("named-accountant-approval-record")).toBeInTheDocument();
    expect(within(summary).getByText("Run qualified-accountant acceptance on the golden corpus.")).toBeInTheDocument();
    expect(within(summary).getByRole("link", { name: "Open production readiness" })).toHaveAttribute(
      "href",
      "/production-readiness",
    );
  });

  it("renders filing workflow actions as a labelled normal-flow control", () => {
    render(
      <FilingActionBar
        title="CRO filing controls"
        description="Only record workflow states after the external evidence is present."
        status={<StatusBadge tone="warn">Manual evidence open</StatusBadge>}
      >
        <button type="button" disabled>
          Approve for filing
        </button>
        <button type="button">Record send-back</button>
      </FilingActionBar>,
    );

    const actionBar = screen.getByRole("region", { name: "CRO filing controls" });

    expect(actionBar).toHaveAttribute("data-workbench-filing-action-bar", "true");
    expect(actionBar.className).not.toContain("fixed");
    expect(actionBar.className).not.toContain("sticky");
    expect(screen.getByText("Only record workflow states after the external evidence is present.")).toBeInTheDocument();
    expect(screen.getByText("Manual evidence open")).toBeInTheDocument();
    expect(within(actionBar).getByRole("group", { name: "Available filing actions" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Approve for filing" })).toBeDisabled();
  });

  it("renders the shared what-is-wrong-ready-next workflow decision summary", () => {
    render(
      <WorkflowDecisionSummary
        items={[
          {
            title: "What is wrong?",
            tone: "bad",
            summary: "2 blockers require attention",
            detail: "Balance sheet does not balance",
          },
          {
            title: "What is ready?",
            tone: "good",
            summary: "3 stages ready",
            detail: "Setup, Import, Classify",
          },
          {
            title: "What must I do next?",
            tone: "info",
            summary: "Categorise",
            detail: "4 uncategorised transactions",
            action: { href: "/companies/7/periods/3?tab=categorise", label: "Open Categorise" },
          },
        ]}
      />,
    );

    const summary = screen.getByRole("region", { name: "Workflow decision summary" });
    expect(summary).toHaveAttribute("data-workbench-decision-summary", "true");
    expect(within(summary).getByText("What is wrong?")).toBeInTheDocument();
    expect(within(summary).getByText("2 blockers require attention")).toBeInTheDocument();
    expect(within(summary).getByText("Balance sheet does not balance")).toBeInTheDocument();
    expect(within(summary).getByText("What is ready?")).toBeInTheDocument();
    expect(within(summary).getByText("Setup, Import, Classify")).toBeInTheDocument();
    expect(within(summary).getByRole("link", { name: "Open Categorise" })).toHaveAttribute(
      "href",
      "/companies/7/periods/3?tab=categorise",
    );
  });

  it("renders deduplicated legal source links with effective dates", () => {
    render(
      <LegalSourceList
        sources={[
          {
            sourceId: "cro-financial-statements-requirements",
            title: "CRO financial statements requirements",
            effectiveDate: "2026-07-03",
            url: "https://cro.ie/annual-return/financial-statements-requirements/",
          },
          {
            sourceId: "cro-financial-statements-requirements",
            title: "CRO financial statements requirements",
            effectiveDate: "2026-07-03",
            url: "https://cro.ie/annual-return/financial-statements-requirements/",
          },
          {
            sourceId: "revenue-accepted-taxonomies",
            title: "Revenue accepted iXBRL taxonomies",
            effectiveDate: "2025-11-06",
            url: "https://www.revenue.ie/en/companies-and-charities/corporation-tax-for-companies/submitting-financial-statements/accepted-taxonomies.aspx",
          },
        ]}
      />,
    );

    expect(screen.getAllByRole("link", { name: /CRO financial statements requirements/ })).toHaveLength(1);
    expect(screen.getByRole("link", { name: /Revenue accepted iXBRL taxonomies/ })).toHaveAttribute(
      "href",
      "https://www.revenue.ie/en/companies-and-charities/corporation-tax-for-companies/submitting-financial-statements/accepted-taxonomies.aspx",
    );
    expect(screen.getByText("Effective 03 Jul 2026")).toBeInTheDocument();
    expect(screen.getByText("Effective 06 Nov 2025")).toBeInTheDocument();
  });

  it("renders a dedicated accountant money input without browser number steppers", async () => {
    const user = userEvent.setup();

    function Harness() {
      const [amount, setAmount] = useState(0);
      return (
        <MoneyInput
          label="Balance outstanding"
          value={amount}
          onValueChange={setAmount}
          hint="Feeds creditors due within and after one year."
        />
      );
    }

    render(<Harness />);

    const input = screen.getByLabelText("Balance outstanding");
    expect(input).toHaveAttribute("type", "text");
    expect(input).toHaveAttribute("inputmode", "decimal");
    const prefix = screen.getByText("EUR");
    expect(prefix).toBeInTheDocument();
    expect(prefix).toHaveAttribute("data-money-input-prefix", "true");
    expect(prefix.className).not.toContain("absolute");
    expect(input.parentElement).toHaveClass("grid");
    expect(screen.getByText("Feeds creditors due within and after one year.")).toBeInTheDocument();

    await user.type(input, "40000.25");

    expect(input).toHaveValue("40000.25");
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

  it("exports DataGrid as the canonical accountant table primitive", async () => {
    const user = userEvent.setup();
    const { container } = render(
      <DataGrid
        caption="Golden corpus evidence grid"
        filterPlaceholder="Filter golden corpus"
        columns={["Scenario", "Evidence", "Status"]}
        rows={[
          {
            id: "micro",
            cells: ["Micro LTD", "PDF and iXBRL", "Covered"],
            searchText: "micro pdf ixbrl covered",
            sortValues: ["Micro LTD", "PDF and iXBRL", "covered"],
            tone: "good",
          },
          {
            id: "medium",
            cells: ["Medium audit-required", "Auditor handoff", "Manual"],
            searchText: "medium auditor handoff manual",
            sortValues: ["Medium audit-required", "Auditor handoff", "manual"],
            tone: "bad",
          },
        ]}
        totals={["2 scenarios", "6 proof points", "1 manual handoff"]}
      />,
    );

    expect(screen.getByRole("table", { name: "Golden corpus evidence grid" })).toBeInTheDocument();
    expect(container.querySelector(".workbench-data-grid")).toBeInTheDocument();
    expect(container.querySelector('td[data-label="Evidence"]')).toHaveTextContent("PDF and iXBRL");
    expect(screen.getByText("1 manual handoff")).toBeInTheDocument();

    await user.type(screen.getByRole("searchbox", { name: "Filter Golden corpus evidence grid" }), "auditor");

    expect(screen.getByText("1 of 2 rows")).toBeInTheDocument();
    expect(screen.getByText("Medium audit-required")).toBeInTheDocument();
    expect(screen.queryByText("Micro LTD")).not.toBeInTheDocument();
  });

  it("assigns unique filter controls when two grids share the same caption", () => {
    render(
      <>
        <DataGrid
          caption="Review queue"
          filterPlaceholder="Filter first queue"
          columns={["Company"]}
          rows={[["Alpha Limited"]]}
        />
        <DataGrid
          caption="Review queue"
          filterPlaceholder="Filter second queue"
          columns={["Company"]}
          rows={[["Bravo Limited"]]}
        />
      </>,
    );

    const filters = screen.getAllByRole("searchbox", { name: "Filter Review queue" });
    expect(filters).toHaveLength(2);
    expect(filters[0]).toHaveAttribute("id");
    expect(filters[1]).toHaveAttribute("id");
    expect(filters[0].getAttribute("id")).not.toBe(filters[1].getAttribute("id"));
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

  it("keeps wide accountant tables scan-friendly with a sticky first column and scroll cue", () => {
    const { container } = render(
      <DataGrid
        caption="Production readiness evidence matrix"
        columns={["Route", "Workflow", "Evidence artifact", "Decision question", "Visual evidence", "Failure policy"]}
        rows={[
          {
            id: "filing-review",
            cells: [
              "Filing review",
              "Review, Filing",
              "filing-review-visual-acceptance-note",
              "Are outputs, gates, wording and evidence readable?",
              "light/dark mobile, tablet and desktop",
              "Block release until accepted.",
            ],
            tone: "warn",
          },
        ]}
      />,
    );

    const tableShell = container.querySelector("[data-workbench-table-shell='true']");
    expect(tableShell).toBeInTheDocument();
    expect(tableShell).toHaveAttribute("data-sticky-first-column", "true");
    expect(tableShell).toHaveAttribute("data-scroll-affordance", "true");
    expect(tableShell).toHaveClass("workbench-data-grid");
    expect(container.querySelector("th[data-sticky-column='true']")).toHaveTextContent("Route");
    expect(container.querySelector("td[data-sticky-column='true']")).toHaveTextContent("Filing review");
    expect(screen.getByText("Scroll horizontally to review all evidence columns.")).toBeInTheDocument();
    expect(tableShell).toHaveAttribute("role", "region");
    expect(tableShell).toHaveAttribute("tabindex", "0");
    expect(screen.getByText("Scroll horizontally to review all evidence columns.").closest("p"))
      .toHaveAttribute("data-scroll-instruction", "true");
  });

  it("wraps retained wide tables in a labelled keyboard-scroll region with a persistent instruction", () => {
    render(
      <HorizontalScrollRegion label="Statement values table">
        <table>
          <tbody>
            <tr><td>Turnover</td><td>€120,000</td></tr>
          </tbody>
        </table>
      </HorizontalScrollRegion>,
    );

    const region = screen.getByRole("region", { name: "Statement values table" });
    expect(region).toHaveAttribute("tabindex", "0");
    expect(region).toHaveAttribute("data-horizontal-scroll-region", "true");
    expect(region).toHaveAttribute("data-sticky-first-column", "true");
    expect(screen.getByText(/Swipe horizontally, or focus this table/)).toBeVisible();
  });

  it("filters dense tables and reports visible row counts", async () => {
    const user = userEvent.setup();
    render(
      <DataTable
        caption="Accountant deadline queue"
        filterPlaceholder="Filter companies or blockers"
        columns={["Company", "Status", "Next action"]}
        rows={[
          {
            id: "alpha",
            cells: ["Alpha Accounts Limited", "Blocked", "Resolve iXBRL validation"],
            searchText: "alpha blocked ixbrl",
          },
          {
            id: "bravo",
            cells: ["Bravo Trading DAC", "Ready", "Review statements"],
            searchText: "bravo ready statements",
          },
        ]}
      />,
    );

    const filter = screen.getByRole("searchbox", { name: "Filter Accountant deadline queue" });
    expect(filter).toHaveClass("placeholder:text-[var(--muted-foreground)]");
    await user.type(filter, "ixbrl");

    expect(screen.getByText("1 of 2 rows")).toBeInTheDocument();
    expect(screen.getByText("Alpha Accounts Limited")).toBeInTheDocument();
    expect(screen.queryByText("Bravo Trading DAC")).not.toBeInTheDocument();
  });

  it("sorts accountant tables by header without losing row status cues", async () => {
    const user = userEvent.setup();
    render(
      <DataTable
        caption="Accountant filing queue"
        columns={["Company", "Deadline", "Status"]}
        sortableColumns={[true, true, true]}
        rows={[
          {
            id: "bravo",
            cells: ["Bravo Trading DAC", "23 Sep 2026", "Ready"],
            sortValues: ["Bravo Trading DAC", "2026-09-23", "ready"],
            tone: "good",
          },
          {
            id: "alpha",
            cells: ["Alpha Accounts Limited", "15 Jul 2026", "Blocked"],
            sortValues: ["Alpha Accounts Limited", "2026-07-15", "blocked"],
            tone: "bad",
          },
        ]}
      />,
    );

    await user.click(screen.getByRole("button", { name: "Sort by Deadline" }));

    let rows = screen.getAllByRole("row");
    expect(within(rows[1]).getByText("Alpha Accounts Limited")).toBeInTheDocument();
    expect(rows[1]).toHaveAttribute("data-tone", "bad");

    await user.click(screen.getByRole("button", { name: "Sort by Deadline" }));

    rows = screen.getAllByRole("row");
    expect(within(rows[1]).getByText("Bravo Trading DAC")).toBeInTheDocument();
    expect(rows[1]).toHaveAttribute("data-tone", "good");
  });

  it("opens dense tables with a declared default sort state", () => {
    render(
      <DataTable
        caption="Review urgency queue"
        columns={["Company", "Urgency"]}
        defaultSort={{ columnIndex: 1, direction: "asc" }}
        sortableColumns={[true, true]}
        rows={[
          {
            id: "ready",
            cells: ["Ready Client Limited", "On track"],
            sortValues: ["Ready Client Limited", "2:on-track"],
            tone: "good",
          },
          {
            id: "blocked",
            cells: ["Blocked Client Limited", "Manual handoff"],
            sortValues: ["Blocked Client Limited", "0:manual-handoff"],
            tone: "bad",
          },
        ]}
      />,
    );

    const rows = screen.getAllByRole("row");
    expect(screen.getByRole("columnheader", { name: "Urgency" })).toHaveAttribute("aria-sort", "ascending");
    expect(within(rows[1]).getByText("Blocked Client Limited")).toBeInTheDocument();
    expect(rows[1]).toHaveAttribute("data-tone", "bad");
  });

  it("keeps command columns unsortable while data columns remain sortable", () => {
    render(
      <DataTable
        caption="Filing action queue"
        columns={["Company", "Deadline", "Next action"]}
        sortableColumns={[true, true, false]}
        rows={[
          ["Alpha Limited", "23 Sep 2026", "Open filing"],
          ["Bravo DAC", "15 Jul 2026", "Review handoff"],
        ]}
      />,
    );

    expect(screen.getByRole("button", { name: "Sort by Company" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Sort by Deadline" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Sort by Next action" })).not.toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "Next action" })).not.toHaveAttribute("aria-sort");
  });

  it("keeps columns unsortable by default and rejects JSX columns without primitive sort values", () => {
    const { rerender } = render(
      <DataTable
        caption="Default static evidence"
        columns={["Evidence", "Action"]}
        rows={[["Retained PDF", "Open"]]}
      />,
    );

    expect(screen.queryByRole("button", { name: "Sort by Evidence" })).not.toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "Evidence" })).not.toHaveAttribute("aria-sort");

    rerender(
      <DataTable
        caption="Unsupported JSX evidence"
        columns={["Evidence", "Action"]}
        sortableColumns={[true, false]}
        rows={[[<span key="evidence">Retained PDF</span>, <button key="action">Open</button>]]}
      />,
    );

    expect(screen.queryByRole("button", { name: "Sort by Evidence" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Sort by Action" })).not.toBeInTheDocument();
  });

  it("renders totals, warning cues, and a useful empty state", async () => {
    const user = userEvent.setup();
    render(
      <DataTable
        caption="Trial balance reconciliation"
        filterPlaceholder="Filter trial balance"
        columns={["Account", "Debit", "Credit", "Warning"]}
        rows={[
          {
            id: "bank",
            cells: ["Bank", "EUR 1,250", "EUR 0", "Reconciled"],
            tone: "good",
            searchText: "bank reconciled",
          },
          {
            id: "suspense",
            cells: ["Suspense", "EUR 0", "EUR 125", "Unreconciled balance"],
            tone: "warn",
            searchText: "suspense unreconciled balance",
          },
        ]}
        totals={["Totals", "EUR 1,250", "EUR 125", "Difference EUR 1,125"]}
        emptyState="No matching ledger rows"
      />,
    );

    const rows = screen.getAllByRole("row");
    expect(within(rows[2]).getByText("Unreconciled balance")).toBeInTheDocument();
    expect(rows[2]).toHaveAttribute("data-tone", "warn");
    expect(screen.getByText("Totals")).toBeInTheDocument();
    expect(screen.getByText("Difference EUR 1,125")).toBeInTheDocument();

    await user.type(screen.getByRole("searchbox", { name: "Filter Trial balance reconciliation" }), "nothing");

    expect(screen.getByText("0 of 2 rows")).toBeInTheDocument();
    expect(screen.getByText("No matching ledger rows")).toBeInTheDocument();
  });

  it("keeps generated row keys stable when legacy rows contain JSX cells", () => {
    const consoleError = vi.spyOn(console, "error").mockImplementation(() => undefined);

    try {
      render(
        <DataTable
          columns={["Control", "Status"]}
          rows={[
            [<span key="control">Director approval</span>, <StatusBadge key="status" tone="warn">Required</StatusBadge>],
            [<span key="control">Accountant approval</span>, <StatusBadge key="status" tone="warn">Required</StatusBadge>],
          ]}
        />,
      );

      expect(consoleError).not.toHaveBeenCalledWith(expect.stringContaining("Encountered two children with the same key"));
    } finally {
      consoleError.mockRestore();
    }
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

  it("renders consistent route states for loading, error, empty, and permission denied views", async () => {
    const user = userEvent.setup();
    const retry = vi.fn();

    render(
      <div>
        <WorkbenchLoadingState
          title="Loading period workspace"
          description="Preparing statutory evidence and filing workflow state."
        />
        <WorkbenchErrorState
          title="Period workspace could not be loaded"
          description="The API did not return a readiness profile."
          onRetry={retry}
        />
        <WorkbenchEmptyState
          title="No accounting periods yet"
          description="Create a period before preparing year-end accounts."
        />
        <PermissionDeniedPanel
          title="Manual review access required"
          description="Ask an owner to grant accountant review permissions before approving filing packs."
        />
      </div>,
    );

    expect(screen.getByLabelText("Loading period workspace")).toBeInTheDocument();
    expect(screen.getByText("Preparing statutory evidence and filing workflow state.")).toBeInTheDocument();
    expect(screen.getByText("Period workspace could not be loaded")).toBeInTheDocument();
    expect(screen.getByText("No accounting periods yet")).toBeInTheDocument();
    expect(screen.getByText("Manual review access required")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Retry" }));

    expect(retry).toHaveBeenCalledOnce();
  });
});
