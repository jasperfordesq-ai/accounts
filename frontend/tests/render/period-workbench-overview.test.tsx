import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { PeriodWorkbenchOverview } from "@/components/period/PeriodWorkbenchOverview";
import type {
  AccountingPeriod,
  Company,
  FilingReadinessProfile,
  FilingWorkflowStatus,
  ReadinessScore,
  YearEndSummary,
} from "@/lib/api";

describe("PeriodWorkbenchOverview", () => {
  it("surfaces workflow state, readiness metrics and filing blockers for an accountant", () => {
    render(
      <PeriodWorkbenchOverview
        companyId="7"
        periodId="3"
        company={sampleCompany()}
        period={samplePeriod()}
        yearEnd={sampleYearEnd()}
        readiness={sampleReadiness()}
        filingStatus={sampleFilingStatus()}
        filingReadinessProfile={sampleFilingReadinessProfile()}
        transactionTotal={10}
        categorisedCount={6}
      />,
    );

    expect(screen.getByRole("navigation", { name: "Accounting Workflow" })).toBeInTheDocument();
    expect(screen.getByText("9 stages")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Setup/ })).toHaveAttribute("href", "/companies/7");
    expect(screen.getByRole("link", { name: /Import/ })).toHaveAttribute("href", "/companies/7/periods/3?tab=import");
    expect(screen.getByRole("link", { name: /Categorise/ })).toHaveAttribute("href", "/companies/7/periods/3?tab=categorise");
    expect(screen.getByRole("link", { name: /Year-End/ })).toHaveAttribute("href", "/companies/7/periods/3/year-end");
    expect(screen.getByRole("link", { name: /Review/ })).toHaveAttribute("href", "/companies/7/periods/3?tab=filing");
    expect(screen.getByRole("link", { name: /Filing/ })).toHaveAttribute("href", "/companies/7/periods/3?tab=filing");
    expect(screen.getByText("4 uncategorised")).toBeInTheDocument();
    expect(screen.getByText("Filing readiness")).toBeInTheDocument();
    expect(screen.getByText("79%")).toBeInTheDocument();
    expect(screen.getByText("Readiness issue digest")).toBeInTheDocument();
    expect(screen.getByText("5 blockers")).toBeInTheDocument();
    expect(screen.getByText("1 warning")).toBeInTheDocument();
    expect(screen.getByText("2 more blockers")).toBeInTheDocument();
    expect(screen.getByText("Balance sheet does not balance")).toBeInTheDocument();
    expect(screen.getByText("Named qualified-accountant approval required")).toBeInTheDocument();
    expect(screen.getByText("Revenue deadline has passed and late filing exposure must be reviewed")).toBeInTheDocument();
  }, 15_000);
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
    officers: [{ id: 1, companyId: 7, name: "Director One", role: "Director" }],
    periods: [],
  };
}

function samplePeriod(): AccountingPeriod {
  return {
    id: 3,
    companyId: 7,
    periodStart: "2024-01-01",
    periodEnd: "2024-12-31",
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
  };
}

function sampleYearEnd(): YearEndSummary {
  return {
    debtors: { count: 0, total: 0 },
    creditors: { count: 0, total: 0 },
    fixedAssets: { count: 0, totalCost: 0 },
    inventory: { count: 0, totalValue: 0 },
    loans: { count: 0, totalBalance: 0 },
    directorLoans: { count: 0 },
    payroll: null,
    taxes: { count: 0, totalLiability: 0, totalBalance: 0 },
    dividends: { count: 0, total: 0 },
    reviewConfirmations: [],
    completeness: {
      score: 75,
      completed: 6,
      total: 8,
      incomplete: ["Dividends reviewed", "Going concern assessment"],
    },
  };
}

function sampleReadiness(): ReadinessScore {
  return {
    completenessPercent: 80,
    filingReadinessPercent: 79,
    balanceSheetBalances: false,
    missingItems: ["Balance sheet does not balance"],
    warnings: [],
  };
}

function sampleFilingStatus(): FilingWorkflowStatus {
  return {
    readyToFile: false,
    blockingIssues: [
      "Balance sheet does not balance",
      "CRO accounts PDF not generated",
      "CRO signature page not generated",
    ],
    warningIssues: ["Revenue deadline has passed and late filing exposure must be reviewed"],
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
    },
    charity: {
      status: "NotStarted",
      sofaGenerated: false,
      trusteesReportGenerated: false,
    },
  };
}

function sampleFilingReadinessProfile(): FilingReadinessProfile {
  return {
    companyId: 7,
    periodId: 3,
    companyType: "Private",
    sizeClass: "Micro",
    electedRegime: "Micro",
    auditExempt: true,
    supportedPath: true,
    manualProfessionalReviewRequired: true,
    accountantReviewRequired: true,
    accountantReviewState: "Not approved",
    directCroSubmissionSupported: false,
    directRosSubmissionSupported: false,
    revenueTaxonomy: {
      taxonomyKey: "IE-FRS-102-2025",
      taxonomyDate: "2025",
      label: "Irish Extension 2025 FRS 102",
      schemaRef: "https://www.revenue.ie/",
      acceptedByRevenue: true,
      effectiveForPeriodsStartingOnOrAfter: "2024-01-01",
      sources: [],
    },
    requiredEvidence: [
      {
        code: "notes-disclosures",
        label: "Notes disclosures generated",
        required: true,
        satisfied: false,
        sources: [],
      },
    ],
    blockingIssues: [
      {
        code: "accountant-review",
        severity: "blocking",
        message: "Named qualified-accountant approval required",
        sources: [],
      },
      {
        code: "external-ros-validation",
        severity: "blocking",
        message: "External ROS/iXBRL validation evidence required",
        sources: [],
      },
    ],
    warningIssues: [],
    sourceReferences: [],
    allowedNextActions: ["record-accountant-review"],
  };
}
