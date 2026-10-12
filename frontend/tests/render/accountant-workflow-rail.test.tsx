import { render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { AccountantWorkflowRail, accountantWorkflowStages } from "@/components/workbench/AccountantWorkflowRail";

const stateBadges = ["Complete", "Next", "Blocked", "Pending"];

describe("AccountantWorkflowRail guide mode", () => {
  it("lists the stages without inventing any completion state", () => {
    const { container } = render(<AccountantWorkflowRail mode="guide" />);

    const workflow = screen.getByRole("navigation", { name: "Accounts workflow" });
    expect(screen.getByText("8 stages")).toBeInTheDocument();
    for (const stage of accountantWorkflowStages) {
      expect(workflow).toHaveTextContent(stage);
    }
    expect(screen.getByText("Open a period to see its progress through these stages.")).toBeInTheDocument();
    for (const badge of stateBadges) {
      expect(within(workflow).queryByText(badge)).not.toBeInTheDocument();
    }
    expect(container.querySelector("[aria-current]")).toBeNull();
  });

  it("ignores any active stage that is passed in guide mode", () => {
    render(<AccountantWorkflowRail mode="guide" activeStage="Filing" />);

    expect(screen.queryByText("Complete")).not.toBeInTheDocument();
  });

  it("keeps the evidence-driven progress states by default", () => {
    render(
      <AccountantWorkflowRail
        activeStage="Classify"
        stageOverrides={{ setup: { state: "done" }, import: { state: "done" }, classify: { state: "active", href: "/classify" } }}
      />,
    );

    const workflow = screen.getByRole("navigation", { name: "Accounts workflow" });
    expect(within(workflow).getAllByText("Complete")).toHaveLength(2);
    expect(within(workflow).getByText("Next")).toBeInTheDocument();
    expect(within(workflow).getByRole("link", { name: /Classify/ })).toHaveAttribute("aria-current", "step");
  });

  it("does not describe the review stage as an accountant sign-off", () => {
    render(<AccountantWorkflowRail mode="guide" />);

    expect(screen.getByText("Evidence, sources and directors' approval")).toBeInTheDocument();
    expect(screen.queryByText(/accountant sign-off/i)).not.toBeInTheDocument();
  });
});
