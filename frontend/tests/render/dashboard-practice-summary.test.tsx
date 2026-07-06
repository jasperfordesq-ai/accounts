import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { DashboardPracticeSummary } from "@/components/dashboard/DashboardPracticeSummary";
import type { Company, FilingDeadline } from "@/lib/api";

describe("DashboardPracticeSummary", () => {
  it("summarises practice workload with workbench metrics", () => {
    render(
      <DashboardPracticeSummary
        companies={[sampleCompany(), dormantCompany(), unassignedCompany()]}
        deadlines={{
          7: sampleDeadline({ companyId: 7, periodId: 3, deadlineType: "CRO", dueDate: "2026-07-10" }),
          8: sampleDeadline({ companyId: 8, periodId: 4, deadlineType: "Revenue", dueDate: "2026-06-20" }),
        }}
        today="2026-07-03"
      />,
    );

    expect(screen.getByRole("region", { name: "Practice command summary" })).toBeInTheDocument();
    expect(screen.getByText("Practice command summary")).toBeInTheDocument();
    expect(screen.getByText("3 companies")).toBeInTheDocument();
    expect(screen.getByText("4 periods")).toBeInTheDocument();
    expect(screen.getByText("1 dormant")).toBeInTheDocument();
    expect(screen.getByText("2 trading")).toBeInTheDocument();
    expect(screen.getAllByText("2 deadline pressure").length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText("2 assigned")).toBeInTheDocument();
    expect(screen.getByText("1 unassigned")).toBeInTheDocument();
    expect(screen.getByText("Nearest filing gate")).toBeInTheDocument();
    expect(screen.getByText("Revenue due 20 Jun 2026")).toBeInTheDocument();
    expect(screen.getByText("Keep this view focused on firm workload, reviewer ownership and immediate filing pressure.")).toBeInTheDocument();
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
    periodCount: 2,
    latestPeriod: {
      id: 3,
      companyId: 7,
      periodStart: "2026-01-01",
      periodEnd: "2026-12-31",
      status: "Review",
      isFirstYear: false,
      memberAuditNoticeReceived: false,
      goingConcernConfirmed: true,
    },
  };
}

function dormantCompany(): Company {
  return {
    ...sampleCompany(),
    id: 8,
    legalName: "Dormant Archive Limited",
    isTrading: false,
    isDormant: true,
    periodCount: 1,
    latestPeriod: {
      ...sampleCompany().latestPeriod!,
      id: 4,
      companyId: 8,
    },
  };
}

function unassignedCompany(): Company {
  return {
    ...sampleCompany(),
    id: 9,
    legalName: "Unassigned Client Limited",
    assignedReviewerName: undefined,
    assignedReviewerEmail: undefined,
    periodCount: 1,
    latestPeriod: {
      ...sampleCompany().latestPeriod!,
      id: 5,
      companyId: 9,
    },
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
