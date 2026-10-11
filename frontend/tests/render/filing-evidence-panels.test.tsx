import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

const api = vi.hoisted(() => ({
  getOfficers: vi.fn(),
  recordCroSigningPack: vi.fn(),
  recordAuditorReport: vi.fn(),
  recordAccountantReview: vi.fn(),
}));
const auth = vi.hoisted(() => ({ reauthenticate: vi.fn() }));
vi.mock("@/lib/api", async (original) => ({ ...(await original<typeof import("@/lib/api")>()), ...api }));
vi.mock("@/components/AuthProvider", () => ({ useAuth: () => auth }));

import {
  AuditorReportPanel,
  CroSigningPackPanel,
  OptionalAccountantReviewPanel,
} from "@/components/period/FilingEvidencePanels";
import { ApiError, type FilingReadinessProfile } from "@/lib/api";
import { ACCOUNTANT_PROFESSIONAL_BODIES } from "@/lib/filingEvidence";

function profile(overrides: {
  allowedNextActions?: string[];
  auditSatisfied?: boolean | null;
}): FilingReadinessProfile {
  const evidence = overrides.auditSatisfied === null || overrides.auditSatisfied === undefined
    ? []
    : [{ code: "audit-report", label: "Signed auditor report", required: true, satisfied: overrides.auditSatisfied, sources: [] }];
  return {
    companyId: 1,
    periodId: 9,
    companyType: "Private",
    supportedPath: true,
    manualProfessionalReviewRequired: false,
    accountantReviewRequired: false,
    accountantReviewState: "Not required by law — not recorded",
    approvalAuthority: "board-of-directors-s324",
    requiredEvidence: evidence,
    allowedNextActions: overrides.allowedNextActions ?? [],
  } as unknown as FilingReadinessProfile;
}

describe("filing evidence panels", () => {
  beforeEach(() => {
    Object.values(api).forEach((m) => m.mockReset());
    auth.reauthenticate.mockReset();
  });

  const recentMfaRequired = () => new ApiError(
    428,
    "Precondition Required",
    JSON.stringify({ code: "recent_mfa_required", error: "Recent authenticator verification is required for this operation." }),
  );

  function expectNoNestingWarning(spy: ReturnType<typeof vi.spyOn>) {
    const nesting = spy.mock.calls.filter((call: unknown[]) => call.map(String).join(" ").includes("validateDOMNesting"));
    expect(nesting).toEqual([]);
  }

  async function verifyAndExpectNoResubmit(user: ReturnType<typeof userEvent.setup>, record: ReturnType<typeof vi.fn>) {
    expect(await screen.findByText(/Verify with your password and authenticator code/i)).toBeInTheDocument();
    expect(record).toHaveBeenCalledTimes(1);
    await user.type(screen.getByLabelText("Current password"), "correct horse");
    await user.type(screen.getByLabelText("Authenticator code"), "123456");
    await user.click(screen.getByRole("button", { name: "Verify" }));
    await waitFor(() => expect(auth.reauthenticate).toHaveBeenCalledWith("correct horse", "123456"));
    expect(record).toHaveBeenCalledTimes(1);
  }

  it("re-authenticates the signing pack without nested forms or a second record call", async () => {
    const consoleError = vi.spyOn(console, "error").mockImplementation(() => {});
    const user = userEvent.setup();
    api.getOfficers.mockResolvedValue([
      { id: 1, name: "Director One", role: "Director" },
      { id: 3, name: "Secretary One", role: "Secretary" },
    ]);
    api.recordCroSigningPack.mockRejectedValue(recentMfaRequired());
    auth.reauthenticate.mockResolvedValue({});
    const { container } = render(
      <CroSigningPackPanel companyId={1} periodId={9} filingReadinessProfile={profile({ allowedNextActions: ["record-cro-signing-pack"] })} canRecord />,
    );
    await user.selectOptions(await screen.findByLabelText("Signing director"), "1");
    await user.selectOptions(screen.getByLabelText("Signing secretary"), "3");
    await user.upload(screen.getByLabelText("Executed signing pack (PDF)"), new File(["x"], "pack.pdf", { type: "application/pdf" }));
    await user.click(screen.getByRole("button", { name: "Record executed signing pack" }));

    await verifyAndExpectNoResubmit(user, api.recordCroSigningPack);
    expect(container.querySelector("form form")).toBeNull();
    // Only the explicit Record press submits again.
    await user.click(screen.getByRole("button", { name: "Record executed signing pack" }));
    await waitFor(() => expect(api.recordCroSigningPack).toHaveBeenCalledTimes(2));
    expectNoNestingWarning(consoleError);
    consoleError.mockRestore();
  });

  it("re-authenticates the auditor's report without nested forms or a second record call", async () => {
    const consoleError = vi.spyOn(console, "error").mockImplementation(() => {});
    const user = userEvent.setup();
    api.recordAuditorReport.mockRejectedValue(recentMfaRequired());
    auth.reauthenticate.mockResolvedValue({});
    const { container } = render(
      <AuditorReportPanel companyId={1} periodId={9} filingReadinessProfile={profile({ auditSatisfied: false })} canRecord />,
    );
    await user.type(screen.getByLabelText("Audit firm"), "Example Audit LLP");
    await user.type(screen.getByLabelText("Signing auditor"), "Auditor One");
    await user.type(screen.getByLabelText("Professional body"), "Example Body");
    await user.type(screen.getByLabelText("Membership number"), "M-1");
    await user.type(screen.getByLabelText("Report reference"), "AR-2026");
    await user.upload(screen.getByLabelText("Signed auditor's report (PDF)"), new File(["x"], "audit.pdf", { type: "application/pdf" }));
    await user.click(screen.getByLabelText("The directors have accepted the auditor's report"));
    await user.click(screen.getByRole("button", { name: "Record auditor's report" }));

    await verifyAndExpectNoResubmit(user, api.recordAuditorReport);
    expect(container.querySelector("form form")).toBeNull();
    await user.click(screen.getByRole("button", { name: "Record auditor's report" }));
    await waitFor(() => expect(api.recordAuditorReport).toHaveBeenCalledTimes(2));
    expectNoNestingWarning(consoleError);
    consoleError.mockRestore();
  });

  it("clears the authenticator code after a failed verification", async () => {
    const user = userEvent.setup();
    api.recordAuditorReport.mockRejectedValue(recentMfaRequired());
    auth.reauthenticate.mockRejectedValue(new Error("Invalid code"));
    render(<AuditorReportPanel companyId={1} periodId={9} filingReadinessProfile={profile({ auditSatisfied: false })} canRecord />);
    await user.type(screen.getByLabelText("Audit firm"), "Example Audit LLP");
    await user.type(screen.getByLabelText("Signing auditor"), "Auditor One");
    await user.type(screen.getByLabelText("Professional body"), "Example Body");
    await user.type(screen.getByLabelText("Membership number"), "M-1");
    await user.type(screen.getByLabelText("Report reference"), "AR-2026");
    await user.upload(screen.getByLabelText("Signed auditor's report (PDF)"), new File(["x"], "audit.pdf", { type: "application/pdf" }));
    await user.click(screen.getByLabelText("The directors have accepted the auditor's report"));
    await user.click(screen.getByRole("button", { name: "Record auditor's report" }));
    await user.type(await screen.findByLabelText("Current password"), "pw");
    await user.type(screen.getByLabelText("Authenticator code"), "123456");
    await user.click(screen.getByRole("button", { name: "Verify" }));
    await waitFor(() => expect(auth.reauthenticate).toHaveBeenCalled());
    await waitFor(() => expect(screen.getByLabelText("Authenticator code")).toHaveValue(""));
    expect(screen.getByLabelText("Current password")).toHaveValue("pw");
  });

  it("hides the auditor's report panel unless the audit-report evidence is unsatisfied", () => {
    const { rerender, container } = render(
      <AuditorReportPanel companyId={1} periodId={9} filingReadinessProfile={profile({ auditSatisfied: true })} canRecord />,
    );
    expect(container).toBeEmptyDOMElement();
    rerender(<AuditorReportPanel companyId={1} periodId={9} filingReadinessProfile={profile({})} canRecord />);
    expect(container).toBeEmptyDOMElement();
    rerender(<AuditorReportPanel companyId={1} periodId={9} filingReadinessProfile={profile({ auditSatisfied: false })} canRecord />);
    expect(screen.getByText("Statutory audit: signed auditor's report")).toBeInTheDocument();
    expect(screen.getByText(/members' audit notice/i)).toBeInTheDocument();
  });

  it("records the auditor's report with the directors' acceptance", async () => {
    const user = userEvent.setup();
    api.recordAuditorReport.mockResolvedValue({});
    render(<AuditorReportPanel companyId={1} periodId={9} filingReadinessProfile={profile({ auditSatisfied: false })} canRecord />);
    await user.type(screen.getByLabelText("Audit firm"), "Example Audit LLP");
    await user.type(screen.getByLabelText("Signing auditor"), "Auditor One");
    await user.type(screen.getByLabelText("Professional body"), "Example Body");
    await user.type(screen.getByLabelText("Membership number"), "M-1");
    await user.type(screen.getByLabelText("Report reference"), "AR-2026");
    const pdf = new File(["x"], "audit.pdf", { type: "application/pdf" });
    await user.upload(screen.getByLabelText("Signed auditor's report (PDF)"), pdf);
    await user.click(screen.getByLabelText("The directors have accepted the auditor's report"));
    await user.click(screen.getByRole("button", { name: "Record auditor's report" }));
    await waitFor(() => expect(api.recordAuditorReport).toHaveBeenCalledWith(1, 9, expect.objectContaining({
      firmName: "Example Audit LLP",
      acceptedByDirectors: true,
      report: pdf,
    })));
  });

  it("offers the executed CRO signing pack only when the workflow requests it", async () => {
    const user = userEvent.setup();
    api.getOfficers.mockResolvedValue([
      { id: 1, name: "Director One", role: "Director" },
      { id: 2, name: "Retired Director", role: "Director", resignedDate: "2020-01-01" },
      { id: 3, name: "Secretary One", role: "Secretary" },
    ]);
    api.recordCroSigningPack.mockResolvedValue({});
    const { rerender } = render(
      <CroSigningPackPanel companyId={1} periodId={9} filingReadinessProfile={profile({})} canRecord />,
    );
    expect(screen.queryByLabelText("Signing director")).not.toBeInTheDocument();

    rerender(<CroSigningPackPanel companyId={1} periodId={9} filingReadinessProfile={profile({ allowedNextActions: ["record-cro-signing-pack"] })} canRecord />);
    await user.selectOptions(await screen.findByLabelText("Signing director"), "1");
    expect(screen.queryByRole("option", { name: "Retired Director" })).not.toBeInTheDocument();
    await user.selectOptions(screen.getByLabelText("Signing secretary"), "3");
    const pdf = new File(["x"], "pack.pdf", { type: "application/pdf" });
    await user.upload(screen.getByLabelText("Executed signing pack (PDF)"), pdf);
    await user.click(screen.getByRole("button", { name: "Record executed signing pack" }));
    await waitFor(() => expect(api.recordCroSigningPack).toHaveBeenCalledWith(1, 9, expect.objectContaining({
      directorOfficerId: 1,
      secretaryOfficerId: 3,
      signedPack: pdf,
    })));
  });

  it("keeps the accountant review optional and collapsed with the no-accountant-required copy", () => {
    const { container } = render(
      <OptionalAccountantReviewPanel companyId={1} periodId={9} filingReadinessProfile={profile({})} canRecord />,
    );
    const details = container.querySelector("details");
    expect(details).not.toHaveAttribute("open");
    expect(screen.getByText("Optional: reviewed by an accountant")).toBeInTheDocument();
    expect(screen.getByText("Irish law does not require an accountant. Record a review here only if one was done.")).toBeInTheDocument();
    expect(screen.queryByText(/accountant approval/i)).not.toBeInTheDocument();
  });

  it("offers exactly the backend-accepted professional bodies, including CPA Ireland, and sends the chosen name", async () => {
    const user = userEvent.setup();
    api.recordAccountantReview.mockResolvedValue({});
    render(<OptionalAccountantReviewPanel companyId={1} periodId={9} filingReadinessProfile={profile({})} canRecord />);
    await user.click(screen.getByText("Optional: reviewed by an accountant"));

    const body = screen.getByLabelText("Accountant's professional body");
    const options = Array.from((body as HTMLSelectElement).options).map((option) => option.value).filter(Boolean);
    expect(options).toEqual([...ACCOUNTANT_PROFESSIONAL_BODIES]);
    expect(options).toContain("Institute of Certified Public Accountants in Ireland");

    await user.type(screen.getByLabelText("Accountant's name"), "Reviewer One");
    await user.selectOptions(body, "Institute of Certified Public Accountants in Ireland");
    await user.type(screen.getByLabelText("Accountant's membership number"), "12345");
    await user.click(screen.getByRole("button", { name: "Record accountant review" }));

    await waitFor(() => expect(api.recordAccountantReview).toHaveBeenCalledWith(1, 9, expect.objectContaining({
      professionalBody: "Institute of Certified Public Accountants in Ireland",
    })));
  });
});
