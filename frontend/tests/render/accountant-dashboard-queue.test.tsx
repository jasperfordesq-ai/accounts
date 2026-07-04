import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { AccountantDashboardQueue } from "@/components/dashboard/AccountantDashboardQueue";
import type { Company, FilingDeadline } from "@/lib/api";

describe("AccountantDashboardQueue", () => {
  it("surfaces deadlines, blockers, reviewer ownership and next actions", async () => {
    const user = userEvent.setup();
    const { container } = render(
      <AccountantDashboardQueue
        companies={[sampleCompany(), unsupportedCompany(), noPeriodCompany()]}
        deadlines={{
          7: sampleDeadline({ companyId: 7, periodId: 3, deadlineType: "CRO", dueDate: "2026-07-10" }),
          8: sampleDeadline({ companyId: 8, periodId: 4, deadlineType: "Revenue", dueDate: "2026-06-20" }),
        }}
        today="2026-07-03"
      />,
    );

    expect(screen.getByText("Accountant Work Queue")).toBeInTheDocument();
    expect(screen.getByText("Active production work across the firm.")).toBeInTheDocument();
    const workflow = screen.getByRole("navigation", { name: "Accountant Workflow" });
    expect(workflow).toBeInTheDocument();
    expect(screen.getByText("8 stages")).toBeInTheDocument();
    expect(workflow).toHaveTextContent("Setup");
    expect(workflow).toHaveTextContent("Import");
    expect(workflow).toHaveTextContent("Classify");
    expect(workflow).toHaveTextContent("Year-End");
    expect(workflow).toHaveTextContent("Statements");
    expect(workflow).toHaveTextContent("Notes");
    expect(workflow).toHaveTextContent("Review");
    expect(workflow).toHaveTextContent("Filing");
    expect(screen.getByText("Start with company setup, then move period work through evidence, statements, review and filing.")).toBeInTheDocument();
    expect(screen.getByText("Urgent clients")).toBeInTheDocument();
    expect(screen.getByText("2 urgent")).toBeInTheDocument();
    expect(screen.getByText("Due-soon deadlines")).toBeInTheDocument();
    expect(screen.getByText("1 deadline")).toBeInTheDocument();
    expect(screen.getByText("Manual handoffs")).toBeInTheDocument();
    expect(screen.getByText("1 handoff")).toBeInTheDocument();
    expect(screen.getByText("Unassigned reviewers")).toBeInTheDocument();
    expect(screen.getByText("2 unassigned")).toBeInTheDocument();
    expect(screen.getByRole("searchbox", { name: "Filter Accountant work queue" })).toBeInTheDocument();
    expect(screen.getByText("3 of 3 rows")).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "Assigned reviewer" })).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "Next action" })).toBeInTheDocument();
    expect(screen.getAllByRole("row")[1]).toHaveTextContent("Atlantic Public Limited Company");
    expect(screen.getAllByRole("row")[2]).toHaveTextContent("New Client Limited");
    expect(screen.getAllByRole("row")[3]).toHaveTextContent("Connacht Visual Limited");
    expect(container.querySelector(".workbench-data-table")).toHaveAttribute("data-responsive", "card");
    const companyCells = container.querySelectorAll('td[data-label="Company"]');
    const deadlineCells = container.querySelectorAll('td[data-label="Deadline"]');
    const blockerCells = container.querySelectorAll('td[data-label="Blockers"]');
    const reviewerCells = container.querySelectorAll('td[data-label="Assigned reviewer"]');
    const actionCells = container.querySelectorAll('td[data-label="Next action"]');
    expect(companyCells[2]).toHaveTextContent("Connacht Visual Limited");
    expect(deadlineCells[2]).toHaveTextContent("CRO due 10 Jul 2026");
    expect(blockerCells[2]).toHaveTextContent("No dashboard-level blockers");
    expect(reviewerCells[2]).toHaveTextContent("Niamh Reviewer");
    expect(actionCells[2]).toHaveTextContent("Open filing");

    expect(screen.getByText("Connacht Visual Limited")).toBeInTheDocument();
    expect(screen.getByText("CRO due 10 Jul 2026")).toBeInTheDocument();
    expect(screen.getByText("Due soon")).toBeInTheDocument();
    expect(screen.getByText("Niamh Reviewer")).toBeInTheDocument();
    expect(screen.getByText("niamh.reviewer@example.ie")).toBeInTheDocument();
    expect(screen.getAllByText("Unassigned")).toHaveLength(2);
    expect(screen.getByRole("link", { name: "Open filing" })).toHaveAttribute("href", "/companies/7/periods/3");

    expect(screen.getByText("Atlantic Public Limited Company")).toBeInTheDocument();
    expect(screen.getByText("Manual handoff")).toBeInTheDocument();
    expect(screen.getByText("PLC/public-company workflow requires manual review")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Review handoff" })).toHaveAttribute("href", "/companies/8");

    expect(screen.getByText("New Client Limited")).toBeInTheDocument();
    expect(screen.getByText("No period")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Create period" })).toHaveAttribute("href", "/companies/9");

    expect(screen.getAllByRole("row")[1]).toHaveAttribute("data-tone", "bad");
    await user.type(screen.getByRole("searchbox", { name: "Filter Accountant work queue" }), "niamh");
    expect(screen.getByText("1 of 3 rows")).toBeInTheDocument();
    expect(screen.getByText("Connacht Visual Limited")).toBeInTheDocument();
    expect(screen.queryByText("Atlantic Public Limited Company")).not.toBeInTheDocument();
  });
});

function sampleCompany(): Company {
  return {
    id: 7,
    legalName: "Connacht Visual Limited",
    companyType: "Private",
    incorporationDate: "2024-01-01",
    financialYearStartMonth: 1,
    ardMonth: 9,
    isGroupMember: false,
    isHolding: false,
    isInvestment: false,
    isSubsidiary: false,
    isDormant: false,
    isTrading: true,
    isVatRegistered: false,
    isEmployer: false,
    hasStock: false,
    ownsAssets: false,
    hasBorrowings: false,
    hasDirectorLoans: false,
    isListedSecurities: false,
    isCreditInstitution: false,
    isInsuranceUndertaking: false,
    isPensionFund: false,
    isCharitableOrganisation: false,
    assignedReviewerName: "Niamh Reviewer",
    assignedReviewerEmail: "niamh.reviewer@example.ie",
    latestPeriod: {
      id: 3,
      companyId: 7,
      periodStart: "2026-01-01",
      periodEnd: "2026-12-31",
      status: "Review",
      isFirstYear: false,
      memberAuditNoticeReceived: false,
      goingConcernConfirmed: true,
      sizeClassification: {
        id: 1,
        turnover: 700000,
        balanceSheetTotal: 300000,
        avgEmployees: 8,
        calculatedClass: "Micro",
      },
    },
  };
}

function unsupportedCompany(): Company {
  return {
    ...sampleCompany(),
    id: 8,
    legalName: "Atlantic Public Limited Company",
    companyType: "PublicLimitedCompany",
    assignedReviewerName: undefined,
    assignedReviewerEmail: undefined,
    latestPeriod: {
      ...sampleCompany().latestPeriod!,
      id: 4,
      companyId: 8,
      status: "Review",
    },
  };
}

function noPeriodCompany(): Company {
  return {
    ...sampleCompany(),
    id: 9,
    legalName: "New Client Limited",
    assignedReviewerName: undefined,
    assignedReviewerEmail: undefined,
    latestPeriod: undefined,
    periods: [],
    periodCount: 0,
  };
}

function sampleDeadline({
  companyId,
  periodId,
  deadlineType,
  dueDate,
}: {
  companyId: number;
  periodId: number;
  deadlineType: string;
  dueDate: string;
}): FilingDeadline {
  return {
    id: companyId,
    companyId,
    periodId,
    deadlineType,
    dueDate,
    isLate: false,
    penaltyAmount: 0,
  };
}
