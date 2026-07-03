import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { CompanyPeriodsWorkbench } from "@/components/company/CompanyPeriodsWorkbench";
import type { Company } from "@/lib/api";

describe("CompanyPeriodsWorkbench", () => {
  it("surfaces period status, statutory size/regime and next accountant actions", () => {
    render(
      <CompanyPeriodsWorkbench
        company={sampleCompany()}
        showNewPeriod={false}
        periodStart=""
        periodEnd=""
        isFirstYear={false}
        creatingPeriod={false}
        canWrite
        onShowNewPeriod={vi.fn()}
        onCancelNewPeriod={vi.fn()}
        onPeriodStartChange={vi.fn()}
        onPeriodEndChange={vi.fn()}
        onFirstYearChange={vi.fn()}
        onCreatePeriod={vi.fn()}
      />,
    );

    expect(screen.getByText("Accounting Periods")).toBeInTheDocument();
    expect(screen.getByText("Production periods and next workbench action.")).toBeInTheDocument();
    expect(screen.getByText("2 periods")).toBeInTheDocument();
    expect(screen.getByText("01 Jan 2026 to 31 Dec 2026")).toBeInTheDocument();
    expect(screen.getByText("Review")).toBeInTheDocument();
    expect(screen.getByText("Micro")).toBeInTheDocument();
    expect(screen.getByText("First year")).toBeInTheDocument();
    expect(screen.getByText("Regime: Small abridged")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open workbench" })).toHaveAttribute(
      "href",
      "/companies/7/periods/3",
    );
    expect(screen.getByRole("button", { name: "New Period" })).toBeInTheDocument();
  });
});

function sampleCompany(): Company {
  return {
    id: 7,
    legalName: "Connacht Workbench Limited",
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
        id: 2,
        companyId: 7,
        periodStart: "2025-01-01",
        periodEnd: "2025-12-31",
        status: "Filed",
        isFirstYear: false,
        memberAuditNoticeReceived: false,
        goingConcernConfirmed: true,
        sizeClassification: {
          id: 12,
          turnover: 1_200_000,
          balanceSheetTotal: 500_000,
          avgEmployees: 12,
          calculatedClass: "Small",
        },
        filingRegime: {
          id: 22,
          canUseMicro: false,
          canFileAbridged: true,
          auditExempt: true,
          electedRegime: "Small abridged",
        },
      },
      {
        id: 3,
        companyId: 7,
        periodStart: "2026-01-01",
        periodEnd: "2026-12-31",
        status: "Review",
        isFirstYear: true,
        memberAuditNoticeReceived: false,
        goingConcernConfirmed: true,
        sizeClassification: {
          id: 13,
          turnover: 700_000,
          balanceSheetTotal: 250_000,
          avgEmployees: 6,
          calculatedClass: "Micro",
        },
        filingRegime: {
          id: 23,
          canUseMicro: true,
          canFileAbridged: true,
          auditExempt: true,
          electedRegime: "Micro",
        },
      },
    ],
  };
}
