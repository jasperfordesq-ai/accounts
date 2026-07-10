import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";

import { AccountantWorkingPaperWorkbench } from "@/components/period/AccountantWorkingPaperWorkbench";
import { accountantWorkingPaperPackFixture } from "../fixtures/accountant-working-paper-pack";

describe("AccountantWorkingPaperWorkbench", () => {
  it("renders candidate identity, all internal outputs, drill-down, and no filing action", async () => {
    const user = userEvent.setup();
    const regenerate = vi.fn();
    render(
      <AccountantWorkingPaperWorkbench
        companyId="1"
        periodId="3"
        pack={accountantWorkingPaperPackFixture}
        canView
        canGenerate
        generating={false}
        error={null}
        onGenerate={regenerate}
        onRetry={vi.fn()}
      />,
    );

    expect(screen.getByRole("heading", { name: "Accountant working papers" })).toBeInTheDocument();
    expect(screen.getByText(/NOT A CRO OR CT1 RETURN/)).toBeInTheDocument();
    expect(screen.getByText("Fixture Accountant (Accountant)")).toBeInTheDocument();
    expect(screen.getByText("fixture-candidate")).toBeInTheDocument();
    expect(screen.getByText("7 / 1 / 3")).toBeInTheDocument();
    expect(screen.getByText("Ledgers and tax bridge reconcile")).toBeInTheDocument();
    expect(screen.getByRole("tablist", { name: "Accountant working-paper outputs" })).toHaveAttribute("data-overflow-tablist", "true");
    expect(screen.getByText(/Swipe to reveal more output tabs/)).toBeVisible();
    expect(screen.getByText("Retained account lead schedules")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /2 sources/ })).toHaveAttribute("href", "/companies/1/periods/3?tab=opening-balances");

    await user.click(screen.getByRole("tab", { name: "Transactions" }));
    expect(screen.getByText("Categorized transaction report")).toBeInTheDocument();
    expect(screen.getByText("Customer receipt")).toBeInTheDocument();

    await user.click(screen.getByRole("tab", { name: "Exceptions" }));
    expect(screen.getByText(/No machine-detected review exceptions remain/)).toBeInTheDocument();

    await user.click(screen.getByRole("tab", { name: "Adjusted TB" }));
    expect(screen.getByRole("table", { name: "Adjusted trial balance" })).toBeInTheDocument();

    await user.click(screen.getByRole("tab", { name: "CT bridge" }));
    expect(screen.getByText(/Corporation-tax support bridge only/)).toBeInTheDocument();
    expect(screen.getByText("Accounting profit before tax")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Regenerate pack" }));
    expect(regenerate).toHaveBeenCalledOnce();
    expect(screen.queryByRole("button", { name: /submit|file with|send to revenue|send to cro/i })).not.toBeInTheDocument();
  });

  it("shows a truthful empty state and denies Client access without a generate control", () => {
    render(
      <AccountantWorkingPaperWorkbench
        companyId="1"
        periodId="3"
        pack={null}
        canView={false}
        canGenerate={false}
        generating={false}
        error={null}
        onGenerate={vi.fn()}
        onRetry={vi.fn()}
      />,
    );

    expect(screen.getByRole("heading", { name: "No retained pack yet" })).toBeInTheDocument();
    expect(screen.getByText(/Client access is not permitted/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Generate pack" })).not.toBeInTheDocument();
  });
});
