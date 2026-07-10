import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { PeriodAdjustmentsWorkspace } from "@/components/period/PeriodAdjustmentsWorkspace";

describe("PeriodAdjustmentsWorkspace keyboard review", () => {
  it("approves a retained adjustment with keyboard activation", async () => {
    const user = userEvent.setup();
    const onApproveAdjustment = vi.fn();
    render(
      <PeriodAdjustmentsWorkspace
        canWrite
        canApprove
        adjustments={[{
          id: 91,
          description: "Depreciation review",
          amount: 1_250,
          source: "FixedAssets",
          impactOnProfit: -1_250,
          impactOnAssets: -1_250,
          isAuto: true,
        }]}
        adjSummary={null}
        loadingAdjustments={false}
        generatingAdj={false}
        approvingId={null}
        adjFilterApproved=""
        adjFilterType=""
        onGenerateAdjustments={vi.fn()}
        onRefreshAdjustments={vi.fn()}
        onApproveAdjustment={onApproveAdjustment}
        onFilterApprovedChange={vi.fn()}
        onFilterTypeChange={vi.fn()}
      />,
    );

    const approve = screen.getByRole("button", { name: "Approve Depreciation review adjustment" });
    approve.focus();
    await user.keyboard("{Enter}");

    expect(onApproveAdjustment).toHaveBeenCalledWith(91);
  });
});
