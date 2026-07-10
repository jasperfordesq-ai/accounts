import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { expect, it, vi } from "vitest";
import { DeadlineRiskQueue } from "@/components/dashboard/DeadlineRiskQueue";

const item = {
  outboxId: "11111111-1111-4111-8111-111111111111",
  companyId: 7,
  companyLegalName: "Synthetic Limited",
  periodId: 9,
  deadlineType: 0 as const,
  reminderKind: 1 as const,
  state: 2 as const,
  dueDate: "2026-07-01",
  attemptCount: 2,
  nextAttemptAtUtc: "2026-07-10T12:00:00Z",
  lastFailureCode: "provider-unavailable",
};

it("keeps failed reminder delivery visible and exposes bounded retry and filing-review actions", async () => {
  const user = userEvent.setup();
  const retry = vi.fn();
  const run = vi.fn();
  render(<DeadlineRiskQueue
    items={[item]}
    state={{ status: "loaded", error: null, failedResourceKeys: [], hasRetainedData: true }}
    canRunDelivery
    busyKey={null}
    actionMessage={null}
    onReload={vi.fn()}
    onRetry={retry}
    onRunDelivery={run}
  />);

  expect(screen.getByRole("link", { name: "Synthetic Limited" })).toHaveAttribute("href", "/companies/7/periods/9?tab=filing");
  expect(screen.getByText("Failure code: provider-unavailable")).toBeInTheDocument();
  expect(screen.getByText("Retry scheduled")).toBeInTheDocument();
  expect(screen.getByText(/No reminder marks a filing as submitted/)).toBeInTheDocument();
  await user.click(screen.getByRole("button", { name: "Retry now" }));
  expect(retry).toHaveBeenCalledWith(item.outboxId);
  await user.click(screen.getByRole("button", { name: "Run delivery cycle" }));
  expect(run).toHaveBeenCalledOnce();
});

it("shows an honest empty state and hides the privileged manual run for non-Owners", () => {
  render(<DeadlineRiskQueue
    items={[]}
    state={{ status: "empty", error: null, failedResourceKeys: [], hasRetainedData: false }}
    canRunDelivery={false}
    busyKey={null}
    actionMessage={null}
    onReload={vi.fn()}
    onRetry={vi.fn()}
    onRunDelivery={vi.fn()}
  />);
  expect(screen.getByText(/No pending or failed reminder delivery/)).toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "Run delivery cycle" })).not.toBeInTheDocument();
});
