import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { WorkbenchPreview } from "@/components/workbench/WorkbenchPreview";

describe("WorkbenchPreview", () => {
  it("renders the accountant workbench primitives in one QA surface", () => {
    render(<WorkbenchPreview />);

    expect(screen.getByText("Workbench Component Preview")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Dashboard" })).toHaveAttribute("href", "/");
    expect(screen.getByText("PageShell primitive")).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Practice command summary" })).toBeInTheDocument();
    expect(screen.getByText("Nearest filing gate")).toBeInTheDocument();
    expect(screen.getAllByText("3 deadline pressure").length).toBeGreaterThanOrEqual(1);
    expect(screen.getByRole("navigation", { name: "Accounting Workflow" })).toBeInTheDocument();
    expect(screen.getByRole("navigation", { name: "Accountant Workflow" })).toBeInTheDocument();
    expect(screen.getByText("Start with company setup, then move period work through evidence, statements, review and filing.")).toBeInTheDocument();
    expect(screen.getByText("Evidence checklist")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Trial balance review" })).toBeInTheDocument();
    expect(screen.getByText("Route states")).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Production release blockers" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Accounting inputs" })).toBeInTheDocument();
    expect(screen.getByLabelText("Balance outstanding")).toHaveAttribute("data-money-input", "true");
    expect(screen.getByText("Permission denied")).toBeInTheDocument();
    expect(screen.getByText("Read-only workflow access")).toBeInTheDocument();
    expect(screen.getByText(/Evidence remains visible; editing requires Owner or Accountant access/i)).toBeInTheDocument();
    expect(screen.getByText("Filing action bar")).toBeInTheDocument();
    expect(screen.getByLabelText("Record external ROS validation preview").tagName).toBe("SPAN");
    expect(screen.getByLabelText("Mark accountant approved preview").tagName).toBe("SPAN");
    expect(screen.queryByRole("button", { name: /Mark accountant approved/i })).not.toBeInTheDocument();
    expect(screen.getByText("EUR 125,000")).toBeInTheDocument();
  });

  it.each([
    ["loading", "Canonical loading state", "Loading canonical accountant workspace"],
    ["empty", "Canonical empty state", "No canonical accounting records"],
    ["maximum-data", "Canonical maximum-data state", "Maximum-data review table"],
    ["error", "Canonical error state", "Canonical workspace could not be loaded"],
    ["partial-error", "Canonical partial-error state", "Filing evidence unavailable"],
    ["permission-denied", "Canonical permission-denied state", "Permission denied"],
    ["read-only", "Canonical read-only state", "Read-only workflow access"],
    ["stale", "Canonical stale state", "Refreshing statement evidence; retained data may be stale."],
    ["conflict", "Canonical conflict state", "Accounting record changed by another reviewer"],
  ])("renders the deterministic %s visual-QA state", (state, title, expectedStateText) => {
    render(<WorkbenchPreview canonicalState={state} />);

    expect(screen.getByRole("heading", { name: title })).toBeInTheDocument();
    expect(screen.getByText(expectedStateText)).toBeInTheDocument();
    expect(screen.getByText("Named human review required")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Workbench preview" })).toHaveAttribute("href", "/workbench-preview");
  });
});
