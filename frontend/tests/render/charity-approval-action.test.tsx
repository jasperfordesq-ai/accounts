import { Suspense } from "react";
import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

const api = vi.hoisted(() => ({
  getCompany: vi.fn(),
  getPeriod: vi.fn(),
  getSofa: vi.fn(),
  getTrusteesReport: vi.fn(),
  getFundBalances: vi.fn(),
  getFilingWorkflowStatus: vi.fn(),
  getCharityArtifactStatus: vi.fn(),
  getDirectorsApproval: vi.fn(),
  updateCharityFilingStatus: vi.fn(),
}));

const auth = vi.hoisted(() => ({ canApprove: true, canReview: true, canWriteWorkingPapers: false }));

vi.mock("next/navigation", () => ({
  usePathname: () => "/companies/1/periods/9/charity",
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn(), refresh: vi.fn(), forward: vi.fn(), prefetch: vi.fn() }),
  useSearchParams: () => new URLSearchParams(),
}));
vi.mock("@/components/AuthProvider", () => ({ useAuth: () => auth }));
vi.mock("@/lib/api", async (original) => ({ ...(await original<typeof import("@/lib/api")>()), ...api }));

import CharityReportingPage from "@/app/companies/[companyId]/periods/[periodId]/charity/page";

const charityStatus = { status: "PackageGenerated", sofaGenerated: true, trusteesReportGenerated: true };

function directorsApproval(current: boolean) {
  return {
    eligibleDirectors: [],
    latestPage: null,
    periodApprovalDate: "2026-11-20",
    current: current ? { page: { id: 4 }, approval: { id: 7 } } : null,
  };
}

async function renderPage() {
  const params = Promise.resolve({ companyId: "1", periodId: "9" });
  await act(async () => {
    render(
      <Suspense fallback={null}>
        <CharityReportingPage params={params} />
      </Suspense>,
    );
  });
}

describe("Charity page approval action", () => {
  beforeEach(() => {
    Object.values(api).forEach((mock) => mock.mockReset());
    Object.assign(auth, { canApprove: true, canReview: true, canWriteWorkingPapers: false });
    api.getCompany.mockResolvedValue({ id: 1, legalName: "Example Charity CLG", isCharitableOrganisation: true });
    api.getPeriod.mockResolvedValue({ id: 9, companyId: 1, periodStart: "2025-08-01", periodEnd: "2026-07-31", status: "Finalised" });
    api.getSofa.mockResolvedValue(null);
    api.getTrusteesReport.mockResolvedValue(null);
    api.getFundBalances.mockResolvedValue([]);
    api.getFilingWorkflowStatus.mockResolvedValue({ charity: charityStatus });
    api.getCharityArtifactStatus.mockResolvedValue({
      decision: {
        frameworkCode: "charities-sorp-frs-102",
        tier: 1,
        decisionReason: "Tier 1 charity",
        manualProfessionalHandoffRequired: false,
        decisionSha256: "a".repeat(64),
      },
      package: null,
      reviewPrototypeChecksPassed: true,
    });
  });

  it("offers Approve charity pack once the directors' approval is current and approves through the charity status endpoint", async () => {
    const user = userEvent.setup();
    api.getDirectorsApproval.mockResolvedValue(directorsApproval(true));
    api.updateCharityFilingStatus.mockResolvedValue({});

    await renderPage();

    const approve = await screen.findByRole("button", { name: "Approve charity pack" });
    await waitFor(() => expect(approve).toBeEnabled());
    expect(screen.queryByText(/qualified-accountant/i)).not.toBeInTheDocument();
    expect(screen.getByText(/approved against the board's current directors' approval \(Companies Act 2014, s\.324\)/)).toBeInTheDocument();

    await user.click(approve);

    await waitFor(() => expect(api.updateCharityFilingStatus).toHaveBeenCalledWith(1, 9, { status: "Approved" }));
  });

  it("surfaces the backend's refusal message", async () => {
    const user = userEvent.setup();
    api.getDirectorsApproval.mockResolvedValue(directorsApproval(true));
    api.updateCharityFilingStatus.mockRejectedValue(new Error("Generate the charity SoFA and trustees-report artifacts before approval."));

    await renderPage();
    const approve = await screen.findByRole("button", { name: "Approve charity pack" });
    await waitFor(() => expect(approve).toBeEnabled());
    await user.click(approve);

    expect(await screen.findByRole("alert")).toHaveTextContent("Generate the charity SoFA and trustees-report artifacts before approval.");
  });

  it("asks for the directors' approval instead when none is current", async () => {
    api.getDirectorsApproval.mockResolvedValue(directorsApproval(false));

    await renderPage();

    expect(await screen.findByText("Directors' approval (s.324) required")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Approve charity pack" })).not.toBeInTheDocument();
  });

  it("is not offered to a role that cannot approve", async () => {
    api.getDirectorsApproval.mockResolvedValue(directorsApproval(true));
    Object.assign(auth, { canApprove: false, canReview: false });
    await renderPage();
    await screen.findByText("Annual Return Workflow");
    expect(screen.queryByRole("button", { name: "Approve charity pack" })).not.toBeInTheDocument();
  });

  it("starts the annual return reference empty during a correction and shows the previous one", async () => {
    api.getDirectorsApproval.mockResolvedValue(directorsApproval(true));
    api.getFilingWorkflowStatus.mockResolvedValue({
      charity: {
        ...charityStatus,
        status: "Approved",
        annualReturnReference: "AR-EXAMPLE-1",
        submittedBy: "Firm Reviewer",
        submittedAt: "2026-09-01T10:00:00Z",
        rejectionReason: "Example charity correction.",
        correctionDeadline: "2026-10-20T09:00:00Z",
      },
    });
    await renderPage();

    await screen.findByRole("button", { name: /Record external submission/ });
    expect(screen.getByLabelText("Charities Regulator annual return reference")).toHaveValue("");
    expect(screen.getByText("Previous submission: AR-EXAMPLE-1")).toBeInTheDocument();
  });

  it("keeps the recorded annual return reference outside a correction", async () => {
    api.getDirectorsApproval.mockResolvedValue(directorsApproval(true));
    api.getFilingWorkflowStatus.mockResolvedValue({ charity: { ...charityStatus, status: "Approved", annualReturnReference: "AR-EXAMPLE-1" } });
    await renderPage();

    await screen.findByRole("button", { name: /Record external submission/ });
    expect(screen.getByLabelText("Charities Regulator annual return reference")).toHaveValue("AR-EXAMPLE-1");
    expect(screen.queryByText(/Previous submission/)).not.toBeInTheDocument();
  });

  it("is not offered once the pack is already approved", async () => {
    api.getDirectorsApproval.mockResolvedValue(directorsApproval(true));
    api.getFilingWorkflowStatus.mockResolvedValue({ charity: { ...charityStatus, status: "Approved" } });
    await renderPage();
    await screen.findByRole("button", { name: /Record external submission/ });
    expect(screen.queryByRole("button", { name: "Approve charity pack" })).not.toBeInTheDocument();
  });
});
