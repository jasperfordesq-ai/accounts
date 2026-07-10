import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { FilingDeadlinesPanel } from "@/components/period/FilingDeadlinesPanel";
import type { FilingDeadline, FilingWorkflowStatus } from "@/lib/api";

describe("FilingDeadlinesPanel", () => {
  it("renders filing deadlines with reference controls and filed-state evidence", () => {
    render(
      <FilingDeadlinesPanel
        canReview
        deadlines={[
          sampleDeadline({ id: 1, deadlineType: "CRO", dueDate: "2026-09-28" }),
          sampleDeadline({ id: 2, deadlineType: "Revenue", dueDate: "2026-09-23" }),
          sampleDeadline({
            id: 3,
            deadlineType: "Charity",
            dueDate: "2026-10-31",
            filedDate: "2026-10-29",
            filingReference: "CR-2026-8842",
          }),
        ]}
        filingStatus={sampleWorkflowStatus()}
        filingReferences={{ 2: "ROS-2026-0042" }}
        markingFiledId={null}
        onFilingReferenceChange={vi.fn()}
        onMarkFiled={vi.fn()}
        onReferenceMissing={vi.fn()}
      />,
    );

    expect(screen.getByText("Filing deadlines")).toBeInTheDocument();
    expect(screen.getByText("3 tracked")).toBeInTheDocument();
    expect(screen.getByText("CRO Filing")).toBeInTheDocument();
    expect(screen.getByText("Revenue Filing")).toBeInTheDocument();
    expect(screen.getByText("Charity Filing")).toBeInTheDocument();
    expect(screen.getByRole("textbox", { name: "Revenue ROS or CT1 filing reference" })).toHaveValue("ROS-2026-0042");
    expect(screen.getByText(/Filed 29 Oct 2026/)).toBeInTheDocument();
    expect(screen.getByText("CR-2026-8842")).toBeInTheDocument();
  });

  it("blocks marking Revenue deadlines as filed until a filing reference is recorded", async () => {
    const user = userEvent.setup();
    const onMarkFiled = vi.fn();
    const onReferenceMissing = vi.fn();

    render(
      <FilingDeadlinesPanel
        canReview
        deadlines={[sampleDeadline({ id: 2, deadlineType: "Revenue", dueDate: "2026-09-23" })]}
        filingStatus={sampleWorkflowStatus({ ct1Reference: "" })}
        filingReferences={{}}
        markingFiledId={null}
        onFilingReferenceChange={vi.fn()}
        onMarkFiled={onMarkFiled}
        onReferenceMissing={onReferenceMissing}
      />,
    );

    await user.click(screen.getByRole("button", { name: "Mark as Filed — Revenue deadline" }));

    expect(onMarkFiled).not.toHaveBeenCalled();
    expect(onReferenceMissing).toHaveBeenCalledWith("Revenue filing reference is required");
  });

  it("passes the selected deadline and normalised reference when marking a referenced filing", async () => {
    const user = userEvent.setup();
    const onMarkFiled = vi.fn();

    render(
      <FilingDeadlinesPanel
        canReview
        deadlines={[sampleDeadline({ id: 3, deadlineType: "Charity", dueDate: "2026-10-31" })]}
        filingStatus={sampleWorkflowStatus()}
        filingReferences={{ 3: " CHARITY-2026-77 " }}
        markingFiledId={null}
        onFilingReferenceChange={vi.fn()}
        onMarkFiled={onMarkFiled}
        onReferenceMissing={vi.fn()}
      />,
    );

    await user.click(screen.getByRole("button", { name: "Mark as Filed — Charity deadline" }));

    expect(onMarkFiled).toHaveBeenCalledWith(
      expect.objectContaining({ id: 3, deadlineType: "Charity" }),
      "CHARITY-2026-77",
    );
  });
});

function sampleDeadline({
  id,
  deadlineType,
  dueDate,
  filedDate,
  filingReference,
}: {
  id: number;
  deadlineType: string;
  dueDate: string;
  filedDate?: string;
  filingReference?: string;
}): FilingDeadline {
  return {
    id,
    companyId: 7,
    periodId: 3,
    deadlineType,
    calculatedDueDate: dueDate,
    dueDate,
    filedDate,
    filingReference,
    isLate: false,
    penaltyAmount: 0,
  };
}

function sampleWorkflowStatus({ ct1Reference = "ROS-2026-0011" }: { ct1Reference?: string } = {}): FilingWorkflowStatus {
  return {
    readyToFile: false,
    blockingIssues: [],
    warningIssues: [],
    cro: {
      status: "NotStarted",
      accountsPdfReady: false,
      signaturePageReady: false,
      paymentCompleted: false,
    },
    revenue: {
      status: "NotStarted",
      ixbrlReady: false,
      ixbrlInternalChecksPassed: false,
      ixbrlValid: false,
      ct1Reference,
      generationSupport: "manual-handoff-only",
      manualHandoffRequired: true,
      reviewPrototypeChecksPassed: false,
    },
    charity: {
      status: "NotStarted",
      sofaGenerated: false,
      trusteesReportGenerated: false,
      annualReturnReference: "CHARITY-2026-0005",
    },
  };
}
