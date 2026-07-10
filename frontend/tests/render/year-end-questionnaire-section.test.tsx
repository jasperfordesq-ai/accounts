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

  it("does not render failed required evidence as a completed professional confirmation", async () => {
    const user = userEvent.setup();
    const onConfirmReview = vi.fn();
    render(
      <YearEndQuestionnaireSection
        title="Tax balances"
        subtitle="Corporation tax, VAT and payroll liabilities"
        icon={Landmark}
        completed={false}
        review={{
          id: 4,
          periodId: 3,
          sectionKey: "tax",
          confirmed: true,
          confirmedBy: "Prior Reviewer",
          confirmedAt: "2026-07-10T10:00:00Z",
        }}
        reviewDisabled
        reviewDisabledReason="Required tax evidence failed to load."
        onConfirmReview={onConfirmReview}
      >
        <p>Retained tax working papers.</p>
      </YearEndQuestionnaireSection>,
    );

    expect(screen.getByText("Evidence unavailable")).toBeInTheDocument();
    expect(screen.queryByText("Reviewed")).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Expand Tax balances section" }));
    expect(screen.getByText("Confirmation blocked until evidence is available")).toBeInTheDocument();
    expect(screen.getByText("Required tax evidence failed to load.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Refresh confirmation" })).toBeDisabled();
    expect(onConfirmReview).not.toHaveBeenCalled();
  });

  it("retains an in-progress editor when its accordion is collapsed and reopened", async () => {
    const user = userEvent.setup();
    render(
      <YearEndQuestionnaireSection
        title="Loans"
        subtitle="Loan and borrowing evidence"
        icon={Landmark}
        completed={false}
      >
        <label htmlFor="loan-lender">Lender</label>
        <input id="loan-lender" defaultValue="" />
      </YearEndQuestionnaireSection>,
    );

    await user.click(screen.getByRole("button", { name: "Expand Loans section" }));
    const lender = screen.getByRole("textbox", { name: "Lender" });
    await user.type(lender, "Bank of Ireland");
    await user.click(screen.getByRole("button", { name: "Collapse Loans section" }));

    expect(screen.queryByRole("textbox", { name: "Lender" })).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Expand Loans section" }));
    expect(screen.getByRole("textbox", { name: "Lender" })).toHaveValue("Bank of Ireland");
  });
});
