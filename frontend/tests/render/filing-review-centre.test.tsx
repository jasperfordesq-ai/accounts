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
});

function sampleWorkflowStatus({
  croStatus = "NotStarted",
  readyToFile = false,
}: {
  croStatus?: string;
  readyToFile?: boolean;
} = {}): FilingWorkflowStatus {
  return {
    readyToFile,
    blockingIssues: [],
    warningIssues: ["External ROS validation evidence remains required."],
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
}: {
  supportedPath: boolean;
  manualProfessionalReviewRequired: boolean;
  blockingIssues?: FilingReadinessProfile["blockingIssues"];
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
    warningIssues: [],
    sourceReferences: [sourceReference()],
    allowedNextActions: [],
  };
}

function sourceReference() {
  return {
    sourceId: "cro-financial-statements-requirements",
    title: "CRO financial statements requirements",
    effectiveDate: "2026-07-03",
    url: "https://cro.ie/annual-return/financial-statements-requirements/",
  };
}
