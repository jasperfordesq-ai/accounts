import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

const api = vi.hoisted(() => ({
  updatePeriodStatus: vi.fn(),
}));
vi.mock("@/lib/api", async (original) => ({ ...(await original<typeof import("@/lib/api")>()), ...api }));
vi.mock("@/components/AuthProvider", () => ({ useAuth: () => ({ reauthenticate: vi.fn() }) }));

import { PeriodFinalisePanel, irishTodayIsoDate } from "@/components/period/PeriodFinalisePanel";
import { ApiError, type AccountingPeriod } from "@/lib/api";

const draft: AccountingPeriod = {
  id: 9,
  companyId: 1,
  periodStart: "2025-01-01",
  periodEnd: "2025-12-31",
  status: "Review",
  isFirstYear: true,
  memberAuditNoticeReceived: false,
  goingConcernConfirmed: true,
};

describe("PeriodFinalisePanel", () => {
  beforeEach(() => Object.values(api).forEach((m) => m.mockReset()));

  it("offers the board approval date and finalise action to an Owner, defaulting to today in Ireland", () => {
    render(<PeriodFinalisePanel companyId={1} period={draft} canFinalise canReopen />);
    const date = screen.getByLabelText("Board approval date (s.324)");
    expect(date).toHaveValue(irishTodayIsoDate());
    expect(date).toHaveAttribute("min", "2025-12-31");
    expect(screen.getByRole("button", { name: "Finalise period" })).toBeInTheDocument();
    expect(screen.getByText(/records the date the board approved the accounts/i)).toBeInTheDocument();
  });

  it("defaults to the stored board approval date when one exists", () => {
    render(<PeriodFinalisePanel companyId={1} period={{ ...draft, approvalDate: "2026-02-10" }} canFinalise canReopen />);
    expect(screen.getByLabelText("Board approval date (s.324)")).toHaveValue("2026-02-10");
  });

  it("is hidden from a Client (no finalise permission) while the period is open", () => {
    render(<PeriodFinalisePanel companyId={1} period={draft} canFinalise={false} canReopen={false} />);
    expect(screen.queryByLabelText("Board approval date (s.324)")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Finalise period" })).not.toBeInTheDocument();
  });

  it("finalises with the chosen board approval date", async () => {
    const user = userEvent.setup();
    const onChanged = vi.fn();
    api.updatePeriodStatus.mockResolvedValue({ ...draft, status: "Finalised", approvalDate: "2026-01-15" });
    render(<PeriodFinalisePanel companyId={1} period={draft} canFinalise canReopen onChanged={onChanged} />);

    fireEvent.change(screen.getByLabelText("Board approval date (s.324)"), { target: { value: "2026-01-15" } });
    await user.click(screen.getByRole("button", { name: "Finalise period" }));

    await waitFor(() => expect(api.updatePeriodStatus).toHaveBeenCalledWith(1, 9, { status: "Finalised", approvalDate: "2026-01-15" }));
    await waitFor(() => expect(onChanged).toHaveBeenCalled());
  });

  it("shows the backend's refusal, such as readiness blockers, in an alert", async () => {
    const user = userEvent.setup();
    api.updatePeriodStatus.mockRejectedValue(new ApiError(400, "Bad Request", JSON.stringify({
      error: "Cannot generate final accounts finalisation while readiness blockers remain: Balance sheet does not balance.",
    })));
    render(<PeriodFinalisePanel companyId={1} period={draft} canFinalise canReopen />);

    await user.click(screen.getByRole("button", { name: "Finalise period" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("readiness blockers remain: Balance sheet does not balance.");
  });

  it("asks for recent verification when the server demands it", async () => {
    const user = userEvent.setup();
    api.updatePeriodStatus.mockRejectedValue(
      new ApiError(428, "Precondition Required", JSON.stringify({ code: "recent_mfa_required", error: "Recent authenticator verification is required for this operation." })),
    );
    render(<PeriodFinalisePanel companyId={1} period={draft} canFinalise canReopen />);
    await user.click(screen.getByRole("button", { name: "Finalise period" }));
    expect(await screen.findByText(/Verify with your password and authenticator code/i)).toBeInTheDocument();
  });

  it("shows the recorded date once finalised and lets an Owner reopen with a reason", async () => {
    const user = userEvent.setup();
    api.updatePeriodStatus.mockResolvedValue({ ...draft, status: "Review" });
    render(<PeriodFinalisePanel companyId={1} period={{ ...draft, status: "Finalised", approvalDate: "2026-01-15" }} canFinalise canReopen />);

    expect(screen.getByText(/Board approval date \(s\.324\):/)).toBeInTheDocument();
    expect(screen.getByText("15 Jan 2026")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Finalise period" })).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Reopen period" }));
    await user.type(screen.getByLabelText(/Reason for reopening/), "Correct the board approval date");
    await user.click(screen.getByRole("button", { name: "Confirm reopen period" }));

    await waitFor(() => expect(api.updatePeriodStatus).toHaveBeenCalledWith(1, 9, {
      status: "Review",
      reopenReason: "Correct the board approval date",
    }));
  });

  it("shows the recorded date read-only to a Client, without a reopen action", () => {
    render(<PeriodFinalisePanel companyId={1} period={{ ...draft, status: "Finalised", approvalDate: "2026-01-15" }} canFinalise={false} canReopen={false} />);
    expect(screen.getByText("15 Jan 2026")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Reopen period" })).not.toBeInTheDocument();
  });
});
