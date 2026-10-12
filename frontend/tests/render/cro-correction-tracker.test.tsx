import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";

const croCorrection = vi.hoisted(() => ({ treatCroAsNotDelivered: vi.fn() }));
vi.mock("@/lib/croCorrection", async (original) => ({ ...(await original<typeof import("@/lib/croCorrection")>()), ...croCorrection }));
vi.mock("@/components/AuthProvider", () => ({ useAuth: () => ({ reauthenticate: vi.fn() }) }));

import { FilingReviewCentre } from "@/components/period/FilingReviewCentre";
import { ApiError, type FilingWorkflowStatus } from "@/lib/api";
import { initialCroSubmissionReference } from "@/lib/croCorrection";

function workflowStatus(croStatus: string, readyToFile = true, correction = true): FilingWorkflowStatus {
  const status = {
    readyToFile,
    blockingIssues: [],
    warningIssues: [],
    cro: {
      status: croStatus,
      accountsPdfReady: true,
      signaturePageReady: true,
      paymentCompleted: false,
      submissionReference: "CORE-EXAMPLE-1",
      rejectionReason: "Example send-back reason.",
      correctionDeadline: "2026-10-20T09:00:00Z",
    },
    revenue: {
      status: "NotStarted",
      ixbrlReady: false,
      ixbrlInternalChecksPassed: false,
      ixbrlValid: false,
      generationSupport: "manual-handoff-only",
      manualHandoffRequired: true,
      reviewPrototypeChecksPassed: false,
    },
    charity: { status: "NotStarted", sofaGenerated: false, trusteesReportGenerated: false },
  } as FilingWorkflowStatus;
  if (!correction) {
    delete status.cro.rejectionReason;
    delete status.cro.correctionDeadline;
  }
  return status;
}

function renderCentre(
  croStatus: string,
  options: { canApprove?: boolean; onApprove?: () => void; onClosed?: () => void; correction?: boolean; croSubmissionReference?: string } = {},
) {
  const canApprove = options.canApprove ?? true;
  return render(
    <FilingReviewCentre
      filingStatus={workflowStatus(croStatus, true, options.correction ?? true)}
      filingReadinessProfile={null}
      croSubmissionReference={options.croSubmissionReference ?? ""}
      validatingIxbrl={false}
      canApprove={canApprove}
      canReview={canApprove}
      onCroSubmissionReferenceChange={vi.fn()}
      onRunIxbrlChecks={vi.fn()}
      onApproveForFiling={options.onApprove ?? vi.fn()}
      onMarkCroSubmitted={vi.fn()}
      onConfirmCroPayment={vi.fn()}
      onMarkCroAccepted={vi.fn()}
      onRecordCroSendBack={vi.fn()}
      companyId={1}
      periodId={9}
      onCroTreatedAsNotDelivered={options.onClosed ?? vi.fn()}
    />,
  );
}

describe("CRO correction tracker", () => {
  it("starts the CORE reference box empty during a correction, and with the recorded reference otherwise", () => {
    expect(initialCroSubmissionReference(workflowStatus("CorrectionRequired").cro)).toBe("");
    expect(initialCroSubmissionReference(workflowStatus("Rejected").cro)).toBe("");
    // Approved during the correction: the send-back's deadline is still set against the old reference.
    expect(initialCroSubmissionReference(workflowStatus("Approved").cro)).toBe("");
    expect(initialCroSubmissionReference(workflowStatus("Approved", true, false).cro)).toBe("CORE-EXAMPLE-1");
    expect(initialCroSubmissionReference(workflowStatus("Submitted", true, false).cro)).toBe("CORE-EXAMPLE-1");
  });

  it("shows the previous submission for context when the corrected pack is approved, with an empty reference box", () => {
    renderCentre("Approved");

    expect(screen.getByText("Previous submission: CORE-EXAMPLE-1")).toBeInTheDocument();
    expect(screen.getByRole("textbox", { name: "CORE submission reference" })).toHaveValue("");
    expect(screen.getByRole("button", { name: /Record external submission/i })).toBeDisabled();
  });

  it("shows the previous submission while the filing is sent back", () => {
    renderCentre("CorrectionRequired");

    expect(screen.getByText("Previous submission: CORE-EXAMPLE-1")).toBeInTheDocument();
  });

  it("shows no previous submission for an ordinary approved filing", () => {
    renderCentre("Approved", { correction: false, croSubmissionReference: "CORE-EXAMPLE-1" });

    expect(screen.queryByText(/Previous submission/)).not.toBeInTheDocument();
    expect(screen.getByRole("textbox", { name: "CORE submission reference" })).toHaveValue("CORE-EXAMPLE-1");
  });

  it("offers treat-as-not-delivered once the corrected pack is approved during the correction", () => {
    renderCentre("Approved");

    expect(screen.getByRole("button", { name: /Record external submission/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Treat as not delivered" })).toBeInTheDocument();
  });

  it("does not offer treat-as-not-delivered for an ordinary approved filing", () => {
    renderCentre("Approved", { correction: false, croSubmissionReference: "CORE-EXAMPLE-1" });

    expect(screen.queryByRole("button", { name: "Treat as not delivered" })).not.toBeInTheDocument();
  });

  it("keeps a rejected filing open through its approval, by its recorded reason", () => {
    const rejectedThenApproved = workflowStatus("Approved");
    delete rejectedThenApproved.cro.correctionDeadline;
    expect(initialCroSubmissionReference(rejectedThenApproved.cro)).toBe("");
  });

  it("says the correction deadline has passed once it has", () => {
    const passed = workflowStatus("CorrectionRequired");
    passed.cro.correctionDeadline = "2026-01-02T09:00:00Z";
    render(
      <FilingReviewCentre
        filingStatus={passed}
        filingReadinessProfile={null}
        croSubmissionReference=""
        validatingIxbrl={false}
        onCroSubmissionReferenceChange={vi.fn()}
        onRunIxbrlChecks={vi.fn()}
        onApproveForFiling={vi.fn()}
        onMarkCroSubmitted={vi.fn()}
        onConfirmCroPayment={vi.fn()}
        onMarkCroAccepted={vi.fn()}
        onRecordCroSendBack={vi.fn()}
      />,
    );

    expect(screen.getByText(/Correction deadline passed/)).toBeInTheDocument();
    expect(screen.queryByText(/Correction due/)).not.toBeInTheDocument();
  });

  it("does not mention a correction deadline for a rejected filing", () => {
    renderCentre("Rejected");

    expect(screen.getByText(/the CORE reference is kept until then/)).toBeInTheDocument();
    expect(screen.queryByText(/correction deadline are kept/)).not.toBeInTheDocument();
  });

  it("offers Approve for Filing with the correction deadline while the CRO filing is sent back", async () => {
    const user = userEvent.setup();
    const onApprove = vi.fn();
    renderCentre("CorrectionRequired", { onApprove });

    expect(screen.getByText(/Correction due/)).toBeInTheDocument();
    expect(screen.getByText(/record the new CORE submission/)).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /Approve for Filing/i }));
    expect(onApprove).toHaveBeenCalledTimes(1);
    expect(screen.getByRole("button", { name: "Treat as not delivered" })).toBeInTheDocument();
  });

  it("offers the same actions for a rejected CRO filing", () => {
    renderCentre("Rejected");

    expect(screen.getAllByText("Rejected by CRO").length).toBeGreaterThan(0);
    expect(screen.getByRole("button", { name: /Approve for Filing/i })).toBeEnabled();
    expect(screen.getByRole("button", { name: "Treat as not delivered" })).toBeInTheDocument();
  });

  it("shows neither action to a user who cannot approve", () => {
    renderCentre("CorrectionRequired", { canApprove: false });

    expect(screen.getByText("Approval permission required")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Approve for Filing/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Treat as not delivered" })).not.toBeInTheDocument();
  });

  it("does not offer treat-as-not-delivered outside a correction", () => {
    renderCentre("PackageGenerated");

    expect(screen.getByRole("button", { name: /Approve for Filing/i })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Treat as not delivered" })).not.toBeInTheDocument();
  });

  it("explains the consequence, requires a reason and closes the correction", async () => {
    const user = userEvent.setup();
    const onClosed = vi.fn();
    croCorrection.treatCroAsNotDelivered.mockResolvedValue(workflowStatus("PackageGenerated"));
    renderCentre("CorrectionRequired", { onClosed });

    await user.click(screen.getByRole("button", { name: "Treat as not delivered" }));
    const panel = screen.getByRole("group", { name: "Treat this CRO filing as not delivered" });
    expect(panel).toHaveTextContent("The filing must then be made afresh");
    expect(panel).toHaveTextContent("late-filing penalties may apply");
    await user.click(within(panel).getByRole("button", { name: "Confirm: treat as not delivered" }));
    expect(within(panel).getByRole("alert")).toHaveTextContent("Explain why the filing is being treated as not delivered");
    expect(croCorrection.treatCroAsNotDelivered).not.toHaveBeenCalled();

    await user.type(within(panel).getByLabelText("Why is the filing being treated as not delivered?"), "The CRO deadline passed before the corrected pack was ready.");
    await user.click(within(panel).getByRole("button", { name: "Confirm: treat as not delivered" }));

    await waitFor(() => expect(onClosed).toHaveBeenCalledTimes(1));
    expect(croCorrection.treatCroAsNotDelivered).toHaveBeenCalledWith(1, 9, "The CRO deadline passed before the corrected pack was ready.");
  });

  it("asks for recent verification beside the controls, never inside them", async () => {
    const user = userEvent.setup();
    croCorrection.treatCroAsNotDelivered.mockImplementation(() => Promise.reject(
      new ApiError(428, "Precondition Required", JSON.stringify({ code: "recent_mfa_required", error: "Recent authenticator verification is required for this operation." })),
    ));
    renderCentre("CorrectionRequired");

    await user.click(screen.getByRole("button", { name: "Treat as not delivered" }));
    const panel = screen.getByRole("group", { name: "Treat this CRO filing as not delivered" });
    await user.type(within(panel).getByLabelText("Why is the filing being treated as not delivered?"), "The CRO deadline passed.");
    await user.click(within(panel).getByRole("button", { name: "Confirm: treat as not delivered" }));

    const prompt = await screen.findByRole("form", { name: "Recent verification" });
    expect(panel.contains(prompt)).toBe(false);
    expect(prompt.parentElement?.closest("form")).toBeNull();
    expect(screen.getByRole("group", { name: "Treat this CRO filing as not delivered" })).toBeInTheDocument();
  });
});
