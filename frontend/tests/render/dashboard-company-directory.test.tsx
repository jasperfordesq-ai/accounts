import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { DashboardCompanyDirectory } from "@/components/dashboard/DashboardCompanyDirectory";
import type { Company, FilingDeadline } from "@/lib/api";

describe("DashboardCompanyDirectory", () => {
  it("renders a dense filterable workbench table for company navigation", async () => {
    const user = userEvent.setup();
    const { container } = render(
      <DashboardCompanyDirectory
        companies={[sampleCompany(), dormantCompany()]}
        deadlines={{
          7: sampleDeadline({ companyId: 7, periodId: 3, deadlineType: "CRO", dueDate: "2026-07-10" }),
          8: null,
        }}
        isOwner
        today="2026-07-03"
      />,
    );

    expect(screen.getByRole("heading", { name: "Company directory" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Add Company" })).toHaveAttribute("href", "/companies/new");
    expect(screen.getByRole("searchbox", { name: "Filter Company directory" })).toBeInTheDocument();
    expect(screen.getByText("2 of 2 rows")).toBeInTheDocument();
    expect(container.querySelector(".workbench-data-table")).toHaveAttribute("data-responsive", "card");
    expect(screen.getByRole("columnheader", { name: "Company" })).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "Deadline" })).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "Reviewer" })).toBeInTheDocument();
    expect(screen.getByText("Connacht Visual Limited")).toBeInTheDocument();
    expect(screen.getByText("CRO due 10 Jul 2026")).toBeInTheDocument();
    expect(screen.getByText("Due soon")).toBeInTheDocument();
    expect(screen.getByText("Niamh Reviewer")).toBeInTheDocument();
    expect(screen.getByText("Dormant")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Open workspace/ })).toHaveAttribute("href", "/companies/7/periods/3");

    await user.type(screen.getByRole("searchbox", { name: "Filter Company directory" }), "dormant");

    expect(screen.getByText("1 of 2 rows")).toBeInTheDocument();
    expect(screen.queryByText("Connacht Visual Limited")).not.toBeInTheDocument();
    expect(screen.getByText("Munster Dormant Limited")).toBeInTheDocument();
  });

  it("renders a workbench empty state when no companies are available", () => {
    render(<DashboardCompanyDirectory companies={[]} deadlines={{}} isOwner />);

    expect(screen.getByText("No companies available")).toBeInTheDocument();
    expect(screen.getByText("Add the first company before preparing year-end accounts.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Add Company" })).toHaveAttribute("href", "/companies/new");
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
    periodCount: 1,
  };
}

function dormantCompany(): Company {
  return {
    ...sampleCompany(),
    id: 8,
    legalName: "Munster Dormant Limited",
    isTrading: false,
    isDormant: true,
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
