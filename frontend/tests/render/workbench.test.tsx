import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { EvidenceChecklist, StatusBadge } from "@/components/workbench";

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
});
