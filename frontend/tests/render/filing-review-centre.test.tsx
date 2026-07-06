import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { FilingReviewCentre } from "@/components/period/FilingReviewCentre";
import type { FilingReadinessProfile, FilingWorkflowStatus } from "@/lib/api";

describe("FilingReviewCentre", () => {
  it("opens with a filing decision centre for blockers, ready evidence and next action", () => {
    render(
      <FilingReviewCentre
        filingStatus={sampleWorkflowStatus({ readyToFile: false, warningIssues: [] })}
        filingReadinessProfile={sampleReadinessProfile({
          supportedPath: true,
          manualProfessionalReviewRequired: false,
          extraEvidence: [
            {
              code: "ixbrl-internal-checks",
              label: "Internal iXBRL checks completed",
              required: true,
              satisfied: true,
              detail: "Internal checks passed.",
              sources: [sourceReference()],
            },
          ],
          blockingIssues: [
            readinessIssue("cro-pdf", "Generate the CRO accounts PDF before approval or submission."),
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

    const decisionCentre = screen.getByLabelText("Filing decision centre");
    expect(within(decisionCentre).getByText("Filing decision centre")).toBeInTheDocument();
    expect(within(decisionCentre).getByText("What is wrong?")).toBeInTheDocument();
    expect(within(decisionCentre).getByText("Generate the CRO accounts PDF before approval or submission.")).toBeInTheDocument();
    expect(within(decisionCentre).getByText("What is ready?")).toBeInTheDocument();
    expect(within(decisionCentre).getByText("Internal iXBRL checks completed")).toBeInTheDocument();
    expect(within(decisionCentre).getByText("What must I do next?")).toBeInTheDocument();
    expect(within(decisionCentre).getByText("Approve CRO pack")).toBeInTheDocument();
    expect(within(decisionCentre).getByText("External ROS/iXBRL validation remains a manual evidence gate.")).toBeInTheDocument();

    const approvalDocket = screen.getByRole("region", { name: "Accountant approval docket" });
    expect(within(approvalDocket).getByText("Reviewer state")).toBeInTheDocument();
    expect(within(approvalDocket).getByText("Ready for accountant review")).toBeInTheDocument();
    expect(within(approvalDocket).getByText("Open evidence")).toBeInTheDocument();
    expect(within(approvalDocket).getByText("1 blocker / 1 warning")).toBeInTheDocument();
    expect(within(approvalDocket).getByText("Next workflow action")).toBeInTheDocument();
    expect(within(approvalDocket).getByText("Approve CRO pack")).toBeInTheDocument();
  });

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
    expect(screen.getAllByText("Manual professional handoff").length).toBeGreaterThan(0);
    expect(screen.getByText("Audit report")).toBeInTheDocument();
    expect(screen.getAllByText("Signed auditor report must be reviewed manually before final filing.").length).toBeGreaterThan(0);
    expect(screen.getByRole("link", { name: /CRO financial statements requirements/ })).toHaveAttribute(
      "href",
      "https://cro.ie/annual-return/financial-statements-requirements/",
    );
    expect(screen.getByText("Effective 03 Jul 2026")).toBeInTheDocument();
    expect(screen.getByText("Approval is disabled while manual professional handoff is required.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Approve for Filing/i })).toBeDisabled();
  });

  it("shows a visible manual-review taxonomy state when Revenue has not accepted the selected taxonomy", () => {
    render(
      <FilingReviewCentre
        filingStatus={sampleWorkflowStatus({ readyToFile: false })}
        filingReadinessProfile={sampleReadinessProfile({
          supportedPath: false,
          manualProfessionalReviewRequired: true,
          revenueTaxonomy: {
            taxonomyKey: "manual-revenue-taxonomy-review-required",
            taxonomyDate: "",
            label: "Manual Revenue taxonomy review required for FRS 102 periods before 2019-01-01",
            schemaRef: "",
            acceptedByRevenue: false,
            effectiveForPeriodsStartingOnOrAfter: "2019-01-01",
            sources: [
              sourceReference({
                sourceId: "revenue-accepted-taxonomies",
                title: "Revenue accepted iXBRL taxonomies",
                effectiveDate: "2025-11-06",
                url: "https://www.revenue.ie/en/companies-and-charities/corporation-tax-for-companies/submitting-financial-statements/accepted-taxonomies.aspx",
              }),
            ],
          },
          blockingIssues: [
            readinessIssue(
              "taxonomy-not-revenue-accepted",
              "No Revenue-accepted iXBRL taxonomy is pinned for period start 2018-01-01.",
            ),
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

    expect(screen.getByText("Taxonomy")).toBeInTheDocument();
    expect(screen.getByText("Manual taxonomy review")).toBeInTheDocument();
    expect(screen.getAllByText("No Revenue-accepted iXBRL taxonomy is pinned for period start 2018-01-01.").length).toBeGreaterThan(0);
  });

  it("orders filing evidence by risk before completed evidence", () => {
    render(
      <FilingReviewCentre
        filingStatus={sampleWorkflowStatus({ readyToFile: false, warningIssues: [] })}
        filingReadinessProfile={sampleReadinessProfile({
          supportedPath: true,
          manualProfessionalReviewRequired: false,
          requiredEvidence: [
            {
              code: "cro-pdf",
              label: "CRO accounts PDF generated",
              required: true,
              satisfied: true,
              detail: "Draft CRO PDF exists.",
              sources: [sourceReference()],
            },
            {
              code: "external-ros-validation",
              label: "External ROS validation evidence",
              required: false,
              satisfied: false,
              detail: "Manual evidence remains open.",
              sources: [sourceReference()],
            },
            {
              code: "accountant-review",
              label: "Named accountant approval",
              required: true,
              satisfied: false,
              detail: "Approval must be recorded before filing.",
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

    const blockingEvidence = evidenceChecklistLabel("Named accountant approval");
    const advisoryEvidence = evidenceChecklistLabel("External ROS validation evidence");
    const completedEvidence = evidenceChecklistLabel("CRO accounts PDF generated");

    expect(blockingEvidence.compareDocumentPosition(advisoryEvidence) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(advisoryEvidence.compareDocumentPosition(completedEvidence) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
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

  it("blocks marking a CRO filing as submitted until a CORE reference is recorded", () => {
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
        croSubmissionReference="   "
        validatingIxbrl={false}
        onCroSubmissionReferenceChange={vi.fn()}
        onRunIxbrlChecks={vi.fn()}
        onApproveForFiling={vi.fn()}
        onMarkCroSubmitted={onMarkSubmitted}
        onConfirmCroPayment={vi.fn()}
        onMarkCroAccepted={vi.fn()}
        onRecordCroSendBack={vi.fn()}
      />,
    );

    expect(screen.getByRole("textbox", { name: "CORE submission reference" })).toHaveValue("   ");
    expect(screen.getByRole("button", { name: /Mark as Submitted/i })).toBeDisabled();
    expect(screen.getByText("CORE submission reference is required before recording CRO submission.")).toBeInTheDocument();
    expect(onMarkSubmitted).not.toHaveBeenCalled();
  });

  it("shows a permission-denied filing action state for users who cannot review", () => {
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
        croSubmissionReference="CORE-2026-0007"
        validatingIxbrl={false}
        canReview={false}
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
    expect(screen.getByText("Professional filing gate")).toBeInTheDocument();
    expect(screen.getByText("Review permission required")).toBeInTheDocument();
    expect(screen.getByText(/Evidence remains visible/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Mark as Submitted/i })).toBeNull();
    expect(screen.queryByRole("button", { name: /Approve for Filing/i })).toBeNull();
  });

  it("shows the exact open sign-off gates beside a disabled approval action", () => {
    render(
      <FilingReviewCentre
        filingStatus={sampleWorkflowStatus({ readyToFile: false, warningIssues: [] })}
        filingReadinessProfile={sampleReadinessProfile({
          supportedPath: true,
          manualProfessionalReviewRequired: false,
          blockingIssues: [
            readinessIssue("cro-pdf", "Generate the CRO accounts PDF before approval or submission."),
            readinessIssue("accountant-review", "A named qualified accountant must approve the filing pack before real CRO/Revenue use."),
            readinessIssue("director-signatory", "Director certification must be recorded before CRO filing."),
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

    const approvalGates = screen.getByRole("region", { name: "Approval evidence gates" });
    const gates = within(approvalGates);

    expect(gates.getByText("Resolve before approval")).toBeInTheDocument();
    expect(gates.getByText("Generate the CRO accounts PDF before approval or submission.")).toBeInTheDocument();
    expect(gates.getByText("A named qualified accountant must approve the filing pack before real CRO/Revenue use.")).toBeInTheDocument();
    expect(gates.getByText("1 more approval blocker")).toBeInTheDocument();
    expect(gates.getByText("External ROS/iXBRL validation remains a manual evidence gate.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Approve for Filing/i })).toBeDisabled();
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
    expect(screen.getAllByText("Ready for accountant review").length).toBeGreaterThan(0);
    expect(screen.getByText("External ROS validation evidence pending")).toBeInTheDocument();
    expect(screen.getAllByText("Approve CRO pack").length).toBeGreaterThan(0);
    expect(screen.getAllByText("5 blockers").length).toBeGreaterThan(0);
    expect(screen.getAllByText("1 warning").length).toBeGreaterThan(0);
    expect(screen.getByText("Priority blockers")).toBeInTheDocument();
    expect(screen.getAllByText("Size classification must be completed before filing readiness can be assessed.").length).toBeGreaterThan(0);
    expect(screen.getByText("2 more blockers")).toBeInTheDocument();
    expect(screen.getAllByText("External ROS/iXBRL validation remains a manual evidence gate.").length).toBeGreaterThan(0);
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
    expect(screen.getByRole("link", { name: /Charities Regulator annual report guidance/ })).toHaveAttribute(
      "href",
      "https://www.charitiesregulator.ie/en/information-for-charities/annual-report-how-to-submit",
    );
    expect(screen.getByRole("link", { name: /CRO auditor's report requirements/ })).toHaveAttribute(
      "href",
      "https://cro.ie/annual-return/financial-statements-requirements/auditors-report/",
    );
    expect(screen.getAllByText("Effective 03 Jul 2026").length).toBeGreaterThan(0);
  });

  it("shows a production decision ledger for final-use filing gates", () => {
    render(
      <FilingReviewCentre
        filingStatus={sampleWorkflowStatus({
          croStatus: "Approved",
          readyToFile: true,
          warningIssues: ["External ROS validation evidence remains required."],
        })}
        filingReadinessProfile={sampleReadinessProfile({
          supportedPath: true,
          manualProfessionalReviewRequired: false,
          warningIssues: [
            readinessIssue("external-ros-validation", "External ROS/iXBRL validation remains a manual evidence gate.", "warning"),
          ],
        })}
        croSubmissionReference="CORE-2026-0007"
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

    const ledger = screen.getByRole("region", { name: "Production decision ledger" });
    const ledgerScope = within(ledger);

    expect(ledgerScope.getByText("Production decision ledger")).toBeInTheDocument();
    expect(ledgerScope.getByText("Supported company path")).toBeInTheDocument();
    expect(ledgerScope.getByText("Supported")).toBeInTheDocument();
    expect(ledgerScope.getByText("Accountant approval")).toBeInTheDocument();
    expect(ledgerScope.getByText("Ready for accountant review")).toBeInTheDocument();
    expect(ledgerScope.getByText("External ROS/iXBRL validation")).toBeInTheDocument();
    expect(ledgerScope.getByText("Evidence open")).toBeInTheDocument();
    expect(ledgerScope.getByText("Direct submission automation")).toBeInTheDocument();
    expect(ledgerScope.getByText("Recorded workflow only")).toBeInTheDocument();
    expect(ledgerScope.getByText("CRO filing evidence")).toBeInTheDocument();
    expect(ledgerScope.getByText("CORE-2026-0007")).toBeInTheDocument();
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
  requiredEvidence,
  blockingIssues = [],
  warningIssues = [],
  signOffSteps = [],
  extraEvidence = [],
  revenueTaxonomy,
}: {
  supportedPath: boolean;
  manualProfessionalReviewRequired: boolean;
  requiredEvidence?: FilingReadinessProfile["requiredEvidence"];
  blockingIssues?: FilingReadinessProfile["blockingIssues"];
  warningIssues?: FilingReadinessProfile["warningIssues"];
  signOffSteps?: FilingReadinessProfile["signOffPacket"]["steps"];
  extraEvidence?: FilingReadinessProfile["requiredEvidence"];
  revenueTaxonomy?: FilingReadinessProfile["revenueTaxonomy"];
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
    revenueTaxonomy: revenueTaxonomy ?? {
      taxonomyKey: "ie-2025-frs-102",
      taxonomyDate: "2025",
      label: "Irish Extension 2025 FRS 102",
      schemaRef: "https://example.test/taxonomy.xsd",
      acceptedByRevenue: true,
      effectiveForPeriodsStartingOnOrAfter: "2024-01-01",
      sources: [sourceReference()],
    },
    requiredEvidence: requiredEvidence ?? [
      {
        code: "audit-report",
        label: "Audit report",
        required: true,
        satisfied: false,
        detail: "Signed auditor report required for this path.",
        sources: [sourceReference()],
      },
      ...extraEvidence,
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

function evidenceChecklistLabel(label: string) {
  const match = screen.getAllByText(label).find((element) =>
    element.tagName === "P" && element.className.includes("font-medium"));

  if (!match) throw new Error(`Could not find evidence checklist label: ${label}`);
  return match;
}
