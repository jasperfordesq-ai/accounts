import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

const api = vi.hoisted(() => ({
  getDirectorsApproval: vi.fn(),
  generateDirectorsApprovalPage: vi.fn(),
  recordDirectorsApproval: vi.fn(),
}));
vi.mock("@/lib/api", async (original) => ({ ...(await original<typeof import("@/lib/api")>()), ...api }));
vi.mock("@/components/AuthProvider", () => ({ useAuth: () => ({ reauthenticate: vi.fn() }) }));

import { DirectorsApprovalPanel } from "@/components/period/DirectorsApprovalPanel";
import { ApiError } from "@/lib/api";

const page = {
  id: 4,
  approvalDate: "2026-11-20",
  firstDirectorName: "Director One",
  secondDirectorName: "Director Two",
  directorsInOfficeCount: 3,
  resolutionReference: "WR-2026-11-20",
  pageSha256: "c".repeat(64),
  accountsPdfSha256: "a".repeat(64),
  recordSha256: "d".repeat(64),
  generatedAtUtc: "2026-11-20T10:00:00Z",
};

const threeDirectors = [
  { officerId: 1, name: "Director One" },
  { officerId: 2, name: "Director Two" },
  { officerId: 3, name: "Director Three" },
];

describe("DirectorsApprovalPanel", () => {
  beforeEach(() => Object.values(api).forEach((m) => m.mockReset()));

  it("offers two director selects when two or more directors are in office", async () => {
    api.getDirectorsApproval.mockResolvedValue({
      eligibleDirectors: threeDirectors,
      latestPage: null,
      current: null,
      periodApprovalDate: "2026-11-20",
    });
    render(<DirectorsApprovalPanel companyId={1} periodId={9} canRecord />);
    expect(await screen.findByLabelText("First director")).toBeInTheDocument();
    expect(screen.getByLabelText("Second director")).toBeInTheDocument();
    expect(screen.getByText(/Companies Act 2014, s\.324/)).toBeInTheDocument();
  });

  it("offers one select for a sole director", async () => {
    api.getDirectorsApproval.mockResolvedValue({
      eligibleDirectors: [{ officerId: 1, name: "Director One" }],
      latestPage: null,
      current: null,
      periodApprovalDate: "2026-11-20",
    });
    render(<DirectorsApprovalPanel companyId={1} periodId={9} canRecord />);
    expect(await screen.findByLabelText("Sole director")).toBeInTheDocument();
    expect(screen.queryByLabelText("Second director")).not.toBeInTheDocument();
  });

  it("shows the recorded approval with its record hash", async () => {
    api.getDirectorsApproval.mockResolvedValue({
      eligibleDirectors: [],
      latestPage: page,
      current: {
        eventId: 7,
        page,
        signedPageSha256: "e".repeat(64),
        resolutionSha256: "f".repeat(64),
        recordedByDisplayName: "Firm Owner",
        recordedAtUtc: "2026-11-21T09:00:00Z",
        recordSha256: "9".repeat(64),
      },
      periodApprovalDate: "2026-11-20",
    });
    render(<DirectorsApprovalPanel companyId={1} periodId={9} canRecord />);
    const recorded = await screen.findByRole("region", { name: "Recorded directors' approval" });
    expect(within(recorded).getByText(/Director One and Director Two/)).toBeInTheDocument();
    expect(within(recorded).getByText(/999999999999/)).toBeInTheDocument();
  });

  it("prefills the approval date from the period and makes it read-only", async () => {
    api.getDirectorsApproval.mockResolvedValue({
      eligibleDirectors: threeDirectors,
      latestPage: null,
      current: null,
      periodApprovalDate: "2026-11-20",
    });
    render(<DirectorsApprovalPanel companyId={1} periodId={9} canRecord />);
    const date = await screen.findByLabelText("Approval date");
    expect(date).toHaveValue("2026-11-20");
    expect(date).toHaveAttribute("readonly");
    expect(screen.getByText(/board approval date recorded when the period was finalised/i)).toBeInTheDocument();
    expect(screen.queryByText(/change it there/i)).not.toBeInTheDocument();
  });

  it("explains that the board approval date must be recorded first and offers no generate button", async () => {
    api.getDirectorsApproval.mockResolvedValue({
      eligibleDirectors: threeDirectors,
      latestPage: null,
      current: null,
      periodApprovalDate: null,
    });
    render(<DirectorsApprovalPanel companyId={1} periodId={9} canRecord />);
    expect(await screen.findByText(/board approval date has to be recorded on the period/i)).toBeInTheDocument();
    expect(screen.getByText(/regenerate the accounts/i)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Generate approval page" })).not.toBeInTheDocument();
    expect(screen.queryByLabelText("First director")).not.toBeInTheDocument();
  });

  it("is read-only for a Client: shows state but no generate or record buttons", async () => {
    api.getDirectorsApproval.mockResolvedValue({
      eligibleDirectors: threeDirectors,
      latestPage: page,
      current: null,
      periodApprovalDate: "2026-11-20",
    });
    render(<DirectorsApprovalPanel companyId={1} periodId={9} canRecord={false} canGenerate={false} />);
    expect(await screen.findByText(/Approval page generated for 20 Nov 2026/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Record directors' approval" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Generate approval page" })).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Signed approval page (PDF)")).not.toBeInTheDocument();
  });

  it("generates the approval page for the period date and offers the download", async () => {
    const user = userEvent.setup();
    api.getDirectorsApproval.mockResolvedValue({
      eligibleDirectors: threeDirectors,
      latestPage: null,
      current: null,
      periodApprovalDate: "2026-11-20",
    });
    api.generateDirectorsApprovalPage.mockResolvedValue(page);
    render(<DirectorsApprovalPanel companyId={1} periodId={9} canRecord />);

    await user.selectOptions(await screen.findByLabelText("First director"), "1");
    await user.selectOptions(screen.getByLabelText("Second director"), "2");
    await user.type(screen.getByLabelText("Minute or written-resolution reference"), "WR-2026-11-20");
    await user.click(screen.getByRole("button", { name: "Generate approval page" }));

    await waitFor(() => expect(api.generateDirectorsApprovalPage).toHaveBeenCalledWith(1, 9, {
      approvalDate: "2026-11-20",
      firstDirectorOfficerId: 1,
      secondDirectorOfficerId: 2,
      resolutionReference: "WR-2026-11-20",
    }));
    const download = await screen.findByRole("link", { name: "Download approval page" });
    expect(download).toHaveAttribute("href", expect.stringContaining("/directors-approval-page/4"));
  });

  it("rejects a resolution reference shorter than three characters without calling the API", async () => {
    const user = userEvent.setup();
    api.getDirectorsApproval.mockResolvedValue({
      eligibleDirectors: [{ officerId: 1, name: "Director One" }],
      latestPage: null,
      current: null,
      periodApprovalDate: "2026-11-20",
    });
    render(<DirectorsApprovalPanel companyId={1} periodId={9} canRecord />);
    await user.selectOptions(await screen.findByLabelText("Sole director"), "1");
    await user.type(screen.getByLabelText("Minute or written-resolution reference"), "ab");
    await user.click(screen.getByRole("button", { name: "Generate approval page" }));
    expect(api.generateDirectorsApprovalPage).not.toHaveBeenCalled();
  });

  it("records the approval from the signed page and the minute or resolution", async () => {
    const user = userEvent.setup();
    api.getDirectorsApproval.mockResolvedValue({
      eligibleDirectors: threeDirectors,
      latestPage: page,
      current: null,
      periodApprovalDate: "2026-11-20",
    });
    api.recordDirectorsApproval.mockResolvedValue({});
    render(<DirectorsApprovalPanel companyId={1} periodId={9} canRecord />);

    const signed = new File(["signed"], "signed.pdf", { type: "application/pdf" });
    const resolution = new File(["minute"], "minute.pdf", { type: "application/pdf" });
    await user.upload(await screen.findByLabelText("Signed approval page (PDF)"), signed);
    await user.upload(screen.getByLabelText("Minute or written resolution (PDF)"), resolution);
    await user.click(screen.getByRole("button", { name: "Record directors' approval" }));

    await waitFor(() => expect(api.recordDirectorsApproval).toHaveBeenCalledWith(1, 9, {
      approvalPageId: 4,
      signedPage: signed,
      resolution,
    }));
  });

  it("asks for recent authenticator verification when the server demands it", async () => {
    const user = userEvent.setup();
    api.getDirectorsApproval.mockResolvedValue({
      eligibleDirectors: threeDirectors,
      latestPage: page,
      current: null,
      periodApprovalDate: "2026-11-20",
    });
    api.recordDirectorsApproval.mockRejectedValue(
      new ApiError(428, "Precondition Required", JSON.stringify({ code: "recent_mfa_required", error: "Recent authenticator verification is required for this operation." })),
    );
    render(<DirectorsApprovalPanel companyId={1} periodId={9} canRecord />);
    await user.upload(await screen.findByLabelText("Signed approval page (PDF)"), new File(["a"], "a.pdf", { type: "application/pdf" }));
    await user.upload(screen.getByLabelText("Minute or written resolution (PDF)"), new File(["b"], "b.pdf", { type: "application/pdf" }));
    await user.click(screen.getByRole("button", { name: "Record directors' approval" }));
    expect(await screen.findByText(/Verify with your password and authenticator code/i)).toBeInTheDocument();
  });
});
