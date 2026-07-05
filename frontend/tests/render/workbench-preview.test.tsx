import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { WorkbenchPreview } from "@/components/workbench/WorkbenchPreview";

describe("WorkbenchPreview", () => {
  it("renders the accountant workbench primitives in one QA surface", () => {
    render(<WorkbenchPreview />);

    expect(screen.getByText("Workbench Component Preview")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Dashboard" })).toHaveAttribute("href", "/");
    expect(screen.getByText("PageShell primitive")).toBeInTheDocument();
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
    expect(screen.getByText("EUR 125,000")).toBeInTheDocument();
  });
});
