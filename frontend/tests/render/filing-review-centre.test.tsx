import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { FilingReviewCentre } from "@/components/period/FilingReviewCentre";
import type { FilingReadinessProfile, FilingWorkflowStatus } from "@/lib/api";

describe("FilingReviewCentre", () => {
  it("surfaces source-backed evidence and blocks approval for manual handoff paths", () => {
    render(
      <FilingReviewCentre
        filingStatus={sampleWorkflowStatus({ readyToFile: true })}
        filingReadinessProfile={sampleReadinessProfile({
          supportedPath: false,
          manualProfessionalReviewRequired: true,
          blockingIssues: [
            {
              code: "auditor-handoff-required",
              severity: "blocking",
              message: "Signed auditor report must be reviewed manually before final filing.",
              sources: [sourceReference()],
            },
          ],
        })}
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

    expect(screen.getByText("Filing readiness profile")).toBeInTheDocument();
    expect(screen.getByText("Manual handoff")).toBeInTheDocument();
    expect(screen.getByText("Accountant sign-off packet")).toBeInTheDocument();
    expect(screen.getByText("Manual professional handoff")).toBeInTheDocument();
    expect(screen.getByText("Audit report")).toBeInTheDocument();
    expect(screen.getByText("Signed auditor report must be reviewed manually before final filing.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "CRO financial statements requirements" })).toHaveAttribute(
      "href",
      "https://cro.ie/annual-return/financial-statements-requirements/",
    );
    expect(screen.getByRole("button", { name: /Approve for Filing/i })).toBeDisabled();
  });

  it("passes the CORE reference when marking an approved filing as submitted", async () => {
    const user = userEvent.setup();
    const onReferenceChange = vi.fn();
    const onMarkSubmitted = vi.fn();

    render(
      <FilingReviewCentre
        filingStatus={sampleWorkflowStatus({
          croStatus: "Approved",
          readyToFile: true,
        })}
        filingReadinessProfile={sampleReadinessProfile({
          supportedPath: true,
          manualProfessionalReviewRequired: false,
        })}
        croSubmissionReference=" CORE-2026-0007 "
        validatingIxbrl={false}
        onCroSubmissionReferenceChange={onReferenceChange}
        onRunIxbrlChecks={vi.fn()}
        onApproveForFiling={vi.fn()}
        onMarkCroSubmitted={onMarkSubmitted}
        onConfirmCroPayment={vi.fn()}
        onMarkCroAccepted={vi.fn()}
        onRecordCroSendBack={vi.fn()}
      />,
    );

    expect(screen.getByRole("textbox", { name: "CORE submission reference" })).toHaveValue(" CORE-2026-0007 ");

    await user.click(screen.getByRole("button", { name: /Mark as Submitted/i }));

    expect(onReferenceChange).not.toHaveBeenCalled();
    expect(onMarkSubmitted).toHaveBeenCalledWith("CORE-2026-0007");
  });

  it("summarises filing issues before exposing the full blocker list", () => {
    render(
      <FilingReviewCentre
        filingStatus={sampleWorkflowStatus({ readyToFile: false, warningIssues: [] })}
        filingReadinessProfile={sampleReadinessProfile({
          supportedPath: true,
          manualProfessionalReviewRequired: false,
          blockingIssues: [
            readinessIssue("size-classification", "Size classification must be completed before filing readiness can be assessed."),
            readinessIssue("filing-regime", "Filing regime must be determined before statutory outputs are approved."),
            readinessIssue("secretary", "Record an active company secretary before approving or submitting CRO accounts."),
            readinessIssue("cro-pdf", "Generate the CRO accounts PDF before approval or submission."),
            readinessIssue("accountant-review", "A named qualified accountant must approve the filing pack before real CRO/Revenue use."),
          ],
          warningIssues: [
            readinessIssue("external-ros-validation", "External ROS/iXBRL validation remains a manual evidence gate.", "warning"),
          ],
        })}
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

    expect(screen.getByText("Filing issue digest")).toBeInTheDocument();
    expect(screen.getByText("Ready for accountant review")).toBeInTheDocument();
    expect(screen.getByText("External ROS validation evidence pending")).toBeInTheDocument();
    expect(screen.getByText("Approve CRO pack")).toBeInTheDocument();
    expect(screen.getByText("5 blockers")).toBeInTheDocument();
    expect(screen.getByText("1 warning")).toBeInTheDocument();
    expect(screen.getByText("Priority blockers")).toBeInTheDocument();
    expect(screen.getByText("Size classification must be completed before filing readiness can be assessed.")).toBeInTheDocument();
    expect(screen.getByText("2 more blockers")).toBeInTheDocument();
    expect(screen.getByText("External ROS/iXBRL validation remains a manual evidence gate.")).toBeInTheDocument();
  });

  it("surfaces specialist sign-off evidence gates with their legal sources", () => {
    render(
      <FilingReviewCentre
        filingStatus={sampleWorkflowStatus({ readyToFile: false })}
        filingReadinessProfile={sampleReadinessProfile({
          supportedPath: true,
          manualProfessionalReviewRequired: true,
          signOffSteps: [
            {
              code: "charity-reporting",
              label: "Charity reporting evidence",
              state: "complete",
              detail: "Charity number, SoFA and Trustees' Annual Report evidence are present.",
              sources: [
                sourceReference({
                  sourceId: "charities-regulator-annual-report",
                  title: "Charities Regulator annual report guidance",
                  url: "https://www.charitiesregulator.ie/en/information-for-charities/annual-report-how-to-submit",
                }),
              ],
            },
            {
              code: "auditor-handoff",
              label: "Auditor handoff",
              state: "blocked",
              detail: "Signed auditor report evidence is required before final output generation.",
              sources: [
                sourceReference({
                  sourceId: "cro-auditors-report",
                  title: "CRO auditor's report requirements",
                  url: "https://cro.ie/annual-return/financial-statements-requirements/auditors-report/",
                }),
              ],
            },
          ],
        })}
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

    expect(screen.getByText("Specialist evidence gates")).toBeInTheDocument();
    expect(screen.getByText("Charity reporting evidence")).toBeInTheDocument();
    expect(screen.getByText("Auditor handoff")).toBeInTheDocument();
    expect(screen.getByText("Signed auditor report evidence is required before final output generation.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Charities Regulator annual report guidance" })).toHaveAttribute(
      "href",
      "https://www.charitiesregulator.ie/en/information-for-charities/annual-report-how-to-submit",
    );
    expect(screen.getByRole("link", { name: "CRO auditor's report requirements" })).toHaveAttribute(
      "href",
      "https://cro.ie/annual-return/financial-statements-requirements/auditors-report/",
    );
  });
});

function sampleWorkflowStatus({
  croStatus = "NotStarted",
  readyToFile = false,
  warningIssues = ["External ROS validation evidence remains required."],
}: {
  croStatus?: string;
  readyToFile?: boolean;
  warningIssues?: string[];
} = {}): FilingWorkflowStatus {
  return {
    readyToFile,
    blockingIssues: [],
    warningIssues,
    cro: {
      status: croStatus,
      accountsPdfReady: true,
      signaturePageReady: true,
      paymentCompleted: false,
    },
    revenue: {
      status: "ReadyForReview",
      ixbrlReady: true,
      ixbrlInternalChecksPassed: true,
      ixbrlValid: false,
      validationErrors: "Internal checks passed. External ROS/iXBRL validation is still required.",
    },
    charity: {
      status: "NotStarted",
      sofaGenerated: false,
      trusteesReportGenerated: false,
    },
  };
}

function sampleReadinessProfile({
  supportedPath,
  manualProfessionalReviewRequired,
  blockingIssues = [],
  warningIssues = [],
  signOffSteps = [],
}: {
  supportedPath: boolean;
  manualProfessionalReviewRequired: boolean;
  blockingIssues?: FilingReadinessProfile["blockingIssues"];
  warningIssues?: FilingReadinessProfile["warningIssues"];
  signOffSteps?: FilingReadinessProfile["signOffPacket"]["steps"];
}): FilingReadinessProfile {
  return {
    companyId: 7,
    periodId: 3,
    companyType: "Private",
    sizeClass: "Medium",
    electedRegime: "Medium",
    auditExempt: false,
    supportedPath,
    manualProfessionalReviewRequired,
    accountantReviewRequired: true,
    accountantReviewState: "Qualified accountant review required",
    directCroSubmissionSupported: false,
    directRosSubmissionSupported: false,
    revenueTaxonomy: {
      taxonomyKey: "ie-2025-frs-102",
      taxonomyDate: "2025",
      label: "Irish Extension 2025 FRS 102",
      schemaRef: "https://example.test/taxonomy.xsd",
      acceptedByRevenue: true,
      effectiveForPeriodsStartingOnOrAfter: "2024-01-01",
      sources: [sourceReference()],
    },
    requiredEvidence: [
      {
        code: "audit-report",
        label: "Audit report",
        required: true,
        satisfied: false,
        detail: "Signed auditor report required for this path.",
        sources: [sourceReference()],
      },
    ],
    blockingIssues,
    warningIssues,
    sourceReferences: [sourceReference()],
    allowedNextActions: [],
    signOffPacket: {
      state: manualProfessionalReviewRequired ? "manual-handoff" : "ready-for-accountant-review",
      stateLabel: manualProfessionalReviewRequired ? "Manual professional handoff" : "Ready for accountant review",
      readyForAccountantApproval: !manualProfessionalReviewRequired,
      readyForExternalFiling: false,
      approvedBy: undefined,
      approvedAt: undefined,
      steps: [
        {
          code: "supported-path",
          label: "Company path support",
          state: manualProfessionalReviewRequired ? "blocked" : "complete",
          detail: manualProfessionalReviewRequired
            ? "Manual professional handoff is required for this filing path."
            : "Core private company path supported.",
          sources: [sourceReference()],
        },
        {
          code: "external-validation",
          label: "External ROS validation",
          state: "warning",
          detail: "External ROS validation evidence pending",
          sources: [sourceReference()],
        },
        ...signOffSteps,
      ],
      openBlockers: blockingIssues.map((issue) => issue.message),
      openWarnings: warningIssues.map((issue) => issue.message),
      allowedNextActions: manualProfessionalReviewRequired ? [] : ["approve-cro-pack"],
    },
  };
}

function readinessIssue(
  code: string,
  message: string,
  severity: "blocking" | "warning" = "blocking",
): FilingReadinessProfile["blockingIssues"][number] {
  return {
    code,
    severity,
    message,
    sources: [sourceReference()],
  };
}

function sourceReference(overrides: Partial<{ sourceId: string; title: string; effectiveDate: string; url: string }> = {}) {
  return {
    sourceId: "cro-financial-statements-requirements",
    title: "CRO financial statements requirements",
    effectiveDate: "2026-07-03",
    url: "https://cro.ie/annual-return/financial-statements-requirements/",
    ...overrides,
  };
}
