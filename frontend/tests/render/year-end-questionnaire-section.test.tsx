import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Landmark } from "lucide-react";
import { describe, expect, it, vi } from "vitest";
import { YearEndQuestionnaireSection } from "@/components/period/YearEndQuestionnaireSection";

describe("YearEndQuestionnaireSection", () => {
  it("links the section trigger to an accessible review panel and records accountant review actions", async () => {
    const user = userEvent.setup();
    const onConfirmReview = vi.fn();

    render(
      <YearEndQuestionnaireSection
        title="Tax balances"
        subtitle="Corporation tax, VAT and payroll liabilities"
        icon={Landmark}
        completed={false}
        onConfirmReview={onConfirmReview}
      >
        <p>Tax working papers go here.</p>
      </YearEndQuestionnaireSection>,
    );

    const trigger = screen.getByRole("button", { name: "Expand Tax balances section" });
    const panelId = trigger.getAttribute("aria-controls");

    expect(trigger).toHaveAttribute("aria-expanded", "false");
    expect(panelId).not.toBeNull();
    if (panelId === null) {
      throw new Error("section trigger should declare aria-controls");
    }
    expect(panelId).toMatch(/^year-end-section-/);
    expect(screen.queryByRole("region", { name: "Tax balances review evidence" })).not.toBeInTheDocument();

    await user.click(trigger);

    expect(trigger).toHaveAttribute("aria-expanded", "true");
    expect(trigger).toHaveAttribute("aria-controls", panelId ?? "");
    const panel = screen.getByRole("region", { name: "Tax balances review evidence" });
    expect(panel).toHaveAttribute("id", panelId ?? "");
    expect(panel).toHaveTextContent("Tax working papers go here.");
    expect(panel).toHaveTextContent("Confirm reviewed");

    await user.click(screen.getByRole("button", { name: "Confirm reviewed" }));

    expect(onConfirmReview).toHaveBeenCalledTimes(1);
  });
});
