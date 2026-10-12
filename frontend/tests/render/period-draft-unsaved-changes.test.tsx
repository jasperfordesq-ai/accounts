import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { ReactNode } from "react";

const navigation = vi.hoisted(() => ({ link: vi.fn() }));
vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn(), refresh: vi.fn(), forward: vi.fn(), prefetch: vi.fn() }),
}));
const api = vi.hoisted(() => ({ updatePeriodStatus: vi.fn() }));
vi.mock("@/lib/api", async (original) => ({ ...(await original<typeof import("@/lib/api")>()), ...api }));
const historic = vi.hoisted(() => ({ recordHistoricSizeFigures: vi.fn() }));
vi.mock("@/lib/historicSizeFigures", async (original) => ({
  ...(await original<typeof import("@/lib/historicSizeFigures")>()),
  ...historic,
}));
vi.mock("@/components/AuthProvider", () => ({ useAuth: () => ({ reauthenticate: vi.fn() }) }));

import { UnsavedChangesProvider } from "@/components/UnsavedChangesProvider";
import { HistoricSizeFiguresCard } from "@/components/period/HistoricSizeFiguresCard";
import { PeriodFinalisePanel } from "@/components/period/PeriodFinalisePanel";
import type { AccountingPeriod } from "@/lib/api";

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

function renderGuarded(ui: ReactNode) {
  return render(
    <UnsavedChangesProvider>
      {ui}
      <a href="/elsewhere" onClick={(event) => { event.preventDefault(); navigation.link(); }}>Elsewhere</a>
    </UnsavedChangesProvider>,
  );
}

async function expectGuarded(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole("link", { name: "Elsewhere" }));
  expect(screen.getByRole("alertdialog", { name: "Leave without saving?" })).toBeInTheDocument();
  expect(navigation.link).not.toHaveBeenCalled();
  await user.click(screen.getByRole("button", { name: "Stay and keep editing" }));
}

async function expectNotGuarded(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole("link", { name: "Elsewhere" }));
  expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
  expect(navigation.link).toHaveBeenCalledTimes(1);
}

beforeEach(() => {
  navigation.link.mockReset();
  api.updatePeriodStatus.mockReset();
  historic.recordHistoricSizeFigures.mockReset();
  window.history.replaceState({}, "", "/companies/1/periods/9");
});

describe("Historic size figures: unsaved-change protection", () => {
  it("lets a clean form navigate without a warning", async () => {
    const user = userEvent.setup();
    renderGuarded(<HistoricSizeFiguresCard companyId={1} periodId={8} onCancel={vi.fn()} onRecorded={vi.fn()} />);
    await expectNotGuarded(user);
  });

  it("warns before leaving with figures or a PDF entered, and not once they are recorded", async () => {
    const user = userEvent.setup();
    historic.recordHistoricSizeFigures.mockResolvedValue({ id: 5 });
    const onRecorded = vi.fn().mockResolvedValue(undefined);
    renderGuarded(<HistoricSizeFiguresCard companyId={1} periodId={8} onCancel={vi.fn()} onRecorded={onRecorded} />);

    await user.type(screen.getByLabelText("Turnover from the filed accounts"), "1000000");
    await expectGuarded(user);

    await user.type(screen.getByLabelText("Balance sheet total from the filed accounts"), "500000");
    await user.type(screen.getByLabelText("Average employees from the filed accounts"), "0");
    await user.type(screen.getByLabelText("Which filed accounts"), "Fictitious members' accounts, year ended 31 December 2024");
    await user.upload(screen.getByLabelText("PDF of the filed accounts"), new File(["%PDF-1.4 fictitious"], "accounts.pdf", { type: "application/pdf" }));
    await user.click(screen.getByRole("button", { name: "Record historic figures" }));
    await waitFor(() => expect(onRecorded).toHaveBeenCalledTimes(1));

    await expectNotGuarded(user);
  });

  it("treats a selected PDF alone as unsaved work", async () => {
    const user = userEvent.setup();
    renderGuarded(<HistoricSizeFiguresCard companyId={1} periodId={8} onCancel={vi.fn()} onRecorded={vi.fn()} />);
    await user.upload(screen.getByLabelText("PDF of the filed accounts"), new File(["%PDF-1.4 fictitious"], "accounts.pdf", { type: "application/pdf" }));
    await expectGuarded(user);
  });
});

describe("Finalise period: unsaved-change protection", () => {
  it("lets the untouched panel navigate without a warning", async () => {
    const user = userEvent.setup();
    renderGuarded(<PeriodFinalisePanel companyId={1} period={draft} canFinalise canReopen />);
    await expectNotGuarded(user);
  });

  it("warns before leaving with a changed board approval date, and not once the period is finalised", async () => {
    const user = userEvent.setup();
    api.updatePeriodStatus.mockResolvedValue({ ...draft, status: "Finalised", approvalDate: "2026-01-15" });
    const onChanged = vi.fn();
    renderGuarded(<PeriodFinalisePanel companyId={1} period={draft} canFinalise canReopen onChanged={onChanged} />);

    fireEvent.change(screen.getByLabelText("Board approval date (s.324)"), { target: { value: "2026-01-15" } });
    await expectGuarded(user);

    await user.click(screen.getByRole("button", { name: "Finalise period" }));
    await waitFor(() => expect(onChanged).toHaveBeenCalled());
    await expectNotGuarded(user);
  });

  it("warns before leaving with a reopening reason typed, and not once the period is reopened", async () => {
    const user = userEvent.setup();
    api.updatePeriodStatus.mockResolvedValue({ ...draft, status: "Review" });
    const onChanged = vi.fn();
    renderGuarded(<PeriodFinalisePanel companyId={1} period={{ ...draft, status: "Finalised", approvalDate: "2026-01-15" }} canFinalise canReopen onChanged={onChanged} />);

    await user.click(screen.getByRole("button", { name: "Reopen period" }));
    await user.type(screen.getByLabelText(/Reason for reopening/), "Correct the board approval date");
    await expectGuarded(user);

    await user.click(screen.getByRole("button", { name: "Confirm reopen period" }));
    await waitFor(() => expect(onChanged).toHaveBeenCalled());
    await expectNotGuarded(user);
  });
});
