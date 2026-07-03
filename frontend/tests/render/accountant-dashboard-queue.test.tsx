import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { AccountantDashboardQueue } from "@/components/dashboard/AccountantDashboardQueue";
import type { Company, FilingDeadline } from "@/lib/api";

describe("AccountantDashboardQueue", () => {
  it("surfaces deadlines, blockers, reviewer ownership and next actions", () => {
    render(
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
    expect(screen.getByRole("columnheader", { name: "Assigned reviewer" })).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "Next action" })).toBeInTheDocument();

    expect(screen.getByText("Connacht Visual Limited")).toBeInTheDocument();
    expect(screen.getByText("CRO due 10 Jul 2026")).toBeInTheDocument();
    expect(screen.getByText("Due soon")).toBeInTheDocument();
    expect(screen.getAllByText("Unassigned")).toHaveLength(3);
    expect(screen.getByRole("link", { name: "Open filing" })).toHaveAttribute("href", "/companies/7/periods/3");

    expect(screen.getByText("Atlantic Public Limited Company")).toBeInTheDocument();
    expect(screen.getByText("Manual handoff")).toBeInTheDocument();
    expect(screen.getByText("PLC/public-company workflow requires manual review")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Review handoff" })).toHaveAttribute("href", "/companies/8");

    expect(screen.getByText("New Client Limited")).toBeInTheDocument();
    expect(screen.getByText("No period")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Create period" })).toHaveAttribute("href", "/companies/9");
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
    periods: [
      {
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
    ],
  };
}

function unsupportedCompany(): Company {
  return {
    ...sampleCompany(),
    id: 8,
    legalName: "Atlantic Public Limited Company",
    companyType: "PublicLimitedCompany",
    periods: [
      {
        ...sampleCompany().periods![0],
        id: 4,
        companyId: 8,
        status: "Review",
      },
    ],
  };
}

function noPeriodCompany(): Company {
  return {
    ...sampleCompany(),
    id: 9,
    legalName: "New Client Limited",
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
