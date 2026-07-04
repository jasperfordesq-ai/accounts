import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { WorkbenchPreview } from "@/components/workbench/WorkbenchPreview";

describe("WorkbenchPreview", () => {
  it("renders the accountant workbench primitives in one QA surface", () => {
    render(<WorkbenchPreview />);

    expect(screen.getByText("Workbench Component Preview")).toBeInTheDocument();
    expect(screen.getByRole("navigation", { name: "Accounting Workflow" })).toBeInTheDocument();
    expect(screen.getByText("Evidence checklist")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Trial balance review" })).toBeInTheDocument();
    expect(screen.getByText("Route states")).toBeInTheDocument();
    expect(screen.getByText("Permission denied")).toBeInTheDocument();
    expect(screen.getByText("Filing action bar")).toBeInTheDocument();
    expect(screen.getByText("EUR 125,000")).toBeInTheDocument();
  });
});
