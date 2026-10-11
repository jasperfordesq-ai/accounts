import { Suspense } from "react";
import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

const api = vi.hoisted(() => ({
  getCompany: vi.fn(),
  getPeriod: vi.fn(),
  saveSizeClassification: vi.fn(),
  runClassification: vi.fn(),
  setFilingRegime: vi.fn(),
  saveMemberAuditNotice: vi.fn(),
}));

vi.mock("@/components/AuthProvider", () => ({ useAuth: () => ({ canWriteWorkingPapers: true }) }));
vi.mock("@/lib/api", async (original) => ({ ...(await original<typeof import("@/lib/api")>()), ...api }));

import ClassifyPage from "@/app/companies/[companyId]/periods/[periodId]/classify/page";

const period = {
  id: 9,
  companyId: 1,
  periodStart: "2025-08-01",
  periodEnd: "2026-07-31",
  status: "Draft",
  isFirstYear: false,
  memberAuditNoticeReceived: false,
  goingConcernConfirmed: false,
};

async function renderPage() {
  const params = Promise.resolve({ companyId: "1", periodId: "9" });
  await act(async () => {
    render(
      <Suspense fallback={null}>
        <ClassifyPage params={params} />
      </Suspense>,
    );
  });
}

describe("Classify page (live defects 2026-10-10)", () => {
  beforeEach(() => {
    Object.values(api).forEach((mock) => mock.mockReset());
    api.getCompany.mockResolvedValue({ id: 1, legalName: "Example Charity CLG" });
    api.runClassification.mockResolvedValue({
      calculatedClass: "Micro",
      qualificationNotes: "First financial year",
      canUseMicro: true,
      canFileAbridged: true,
      auditExempt: true,
      availableRegimes: ["Micro"],
      isIneligibleEntity: false,
      rawCurrentClass: "Micro",
      annualisedTurnover: 638,
      periodLengthInYears: 1,
    });
  });

  it("shows a saved zero for average employees instead of an empty box", async () => {
    api.getPeriod.mockResolvedValue({
      ...period,
      sizeClassification: { id: 1, turnover: 1640, balanceSheetTotal: 22358, avgEmployees: 0, thresholdElectionEffectiveFrom: "2024-01-01" },
    });

    await renderPage();

    expect(await screen.findByLabelText("Average Employees")).toHaveValue(0);
    expect(screen.getByLabelText("Turnover")).toHaveValue(1640);
    expect(screen.getByText("Not yet classified")).toBeInTheDocument();
    expect(screen.getByText(/saved but not yet classified/)).toBeInTheDocument();
  });

  it("saves the inputs and classifies in one request, sending zero employees as 0", async () => {
    const user = userEvent.setup();
    api.getPeriod.mockResolvedValue({ ...period });

    await renderPage();

    await user.type(await screen.findByLabelText("Turnover"), "638");
    await user.type(screen.getByLabelText("Balance Sheet Total"), "17430");
    await user.type(screen.getByLabelText("Average Employees"), "0");
    expect(screen.getByLabelText("Average Employees")).toHaveValue(0);
    await user.click(screen.getByRole("button", { name: /Classify Company/ }));

    await waitFor(() => expect(api.runClassification).toHaveBeenCalledTimes(1));
    expect(api.runClassification).toHaveBeenCalledWith(1, 9, {
      turnover: 638,
      balanceSheetTotal: 17430,
      avgEmployees: 0,
      thresholdElectionEffectiveFrom: "2024-01-01",
    });
    expect(api.saveSizeClassification).not.toHaveBeenCalled();
  });

  it("does not send an empty employee count as a silent zero", async () => {
    const user = userEvent.setup();
    api.getPeriod.mockResolvedValue({ ...period });

    await renderPage();

    await user.type(await screen.findByLabelText("Turnover"), "638");
    await user.type(screen.getByLabelText("Balance Sheet Total"), "17430");

    expect(screen.getByRole("button", { name: /Classify Company/ })).toBeDisabled();
    expect(api.runClassification).not.toHaveBeenCalled();
  });
});
