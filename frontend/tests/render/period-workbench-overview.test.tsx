import { render, screen, within } from "@testing-library/react";
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

    const commandCentre = screen.getByRole("heading", { name: "Period command centre" }).closest("section");
    expect(commandCentre).not.toBeNull();
    const command = within(commandCentre!);
    expect(command.getByText("What is wrong?")).toBeInTheDocument();
    expect(command.getAllByText("5 blockers require attention")).toHaveLength(2);
    expect(command.getByText("What is ready?")).toBeInTheDocument();
    expect(command.getByText("3 stages ready")).toBeInTheDocument();
    expect(command.getByText("Setup, Import, Classify")).toBeInTheDocument();
    expect(command.getByText("What must I do next?")).toBeInTheDocument();
    expect(command.getByText("Categorise")).toBeInTheDocument();
    expect(command.getByText("4 uncategorised transactions")).toBeInTheDocument();
    expect(command.getByRole("link", { name: "Open Categorise" })).toHaveAttribute(
      "href",
      "/companies/7/periods/3?tab=categorise",
    );
    const filingGate = screen.getByRole("region", { name: "Filing gate snapshot" });
    expect(within(filingGate).getByText("Filing gate snapshot")).toBeInTheDocument();
    expect(within(filingGate).getAllByText("Manual professional review")).toHaveLength(2);
    expect(within(filingGate).getByText("Not approved")).toBeInTheDocument();
    expect(within(filingGate).getByText("External filing blocked")).toBeInTheDocument();
    expect(within(filingGate).getByText("record-accountant-review")).toBeInTheDocument();
    expect(within(filingGate).getByRole("link", { name: "Open filing review" })).toHaveAttribute(
      "href",
      "/companies/7/periods/3?tab=filing",
    );
    const workflow = screen.getByRole("navigation", { name: "Accountant Workflow" });
    expect(workflow).toBeInTheDocument();
    const workflowNav = within(workflow);
    expect(screen.getByText("8 stages")).toBeInTheDocument();
    expect(workflowNav.getByRole("link", { name: /Setup/ })).toHaveAttribute("href", "/companies/7");
    expect(workflowNav.getByRole("link", { name: /Import/ })).toHaveAttribute("href", "/companies/7/periods/3?tab=categorise");
    expect(workflowNav.getByRole("link", { name: /Import/ })).toHaveAttribute("aria-current", "step");
    expect(workflowNav.getByRole("link", { name: /Classify/ })).toHaveAttribute("href", "/companies/7/periods/3/classify");
    expect(workflowNav.getByRole("link", { name: /Year-End/ })).toHaveAttribute("href", "/companies/7/periods/3/year-end");
    expect(workflowNav.getByRole("link", { name: /Statements/ })).toHaveAttribute("href", "/companies/7/periods/3/statements");
    expect(workflowNav.getByRole("link", { name: /Notes/ })).toHaveAttribute("href", "/companies/7/periods/3/notes");
    expect(workflowNav.getByRole("link", { name: /Review/ })).toHaveAttribute("href", "/companies/7/periods/3?tab=filing");
    expect(workflowNav.getByRole("link", { name: /Filing/ })).toHaveAttribute("href", "/companies/7/periods/3?tab=filing");
    expect(workflow).not.toHaveTextContent("Categorise");
    expect(screen.getByText("Start with company setup, then move period work through evidence, statements, review and filing.")).toBeInTheDocument();
    expect(screen.getByText("4 uncategorised transactions")).toBeInTheDocument();
    expect(screen.getByText("Filing readiness")).toBeInTheDocument();
    expect(screen.getByText("79%")).toBeInTheDocument();
    const sourceBreakdown = screen.getByRole("region", { name: "Issue source breakdown" });
    expect(within(sourceBreakdown).getByText("Issue source breakdown")).toBeInTheDocument();
    expect(within(sourceBreakdown).getByText("Filing workflow")).toBeInTheDocument();
    expect(within(sourceBreakdown).getByText("3 blockers / 1 warning")).toBeInTheDocument();
    expect(within(sourceBreakdown).getByText("Filing readiness profile")).toBeInTheDocument();
    expect(within(sourceBreakdown).getByText("2 blockers / 0 warnings")).toBeInTheDocument();
    expect(within(sourceBreakdown).getByText("Statutory readiness score")).toBeInTheDocument();
    expect(within(sourceBreakdown).getByText("1 blocker / 0 warnings")).toBeInTheDocument();
    expect(screen.getByText("Readiness issue digest")).toBeInTheDocument();
    expect(screen.getByText("5 blockers")).toBeInTheDocument();
    expect(screen.getByText("1 warning")).toBeInTheDocument();
    expect(screen.getByText("2 more blockers")).toBeInTheDocument();
    expect(screen.getAllByText("Balance sheet does not balance")).toHaveLength(2);
    expect(screen.getByText("Named qualified-accountant approval required")).toBeInTheDocument();
    expect(screen.getByText("Revenue deadline has passed and late filing exposure must be reviewed")).toBeInTheDocument();
  }, 20_000);

  it("keeps setup active until director and secretary evidence is recorded", () => {
    render(
      <PeriodWorkbenchOverview
        companyId="7"
        periodId="3"
        company={sampleCompanyWithoutSecretary()}
        period={samplePeriod()}
        yearEnd={sampleYearEnd()}
        readiness={sampleReadiness()}
        filingStatus={sampleFilingStatus()}
        filingReadinessProfile={sampleFilingReadinessProfile()}
        transactionTotal={10}
        categorisedCount={6}
      />,
    );

    const commandCentre = screen.getByRole("heading", { name: "Period command centre" }).closest("section");
    expect(commandCentre).not.toBeNull();
    const command = within(commandCentre!);
    expect(command.getByText("2 stages ready")).toBeInTheDocument();
    expect(command.getByText("Import, Classify")).toBeInTheDocument();

    const workflow = screen.getByRole("navigation", { name: "Accountant Workflow" });
    const setupStep = within(workflow).getByRole("link", { name: /Setup/ });
    expect(setupStep).toHaveTextContent("Director and secretary evidence required");
    expect(setupStep).toHaveAttribute("aria-current", "step");
  }, 20_000);
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
    officers: [
      { id: 1, companyId: 7, name: "Director One", role: "Director" },
      { id: 2, companyId: 7, name: "Secretary One", role: "CompanySecretary" },
    ],
    periods: [],
  };
}

function sampleCompanyWithoutSecretary(): Company {
  return {
    ...sampleCompany(),
    officers: [{ id: 1, companyId: 7, name: "Director One", role: "Director" }],
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
    signOffPacket: {
      state: "blocked",
      stateLabel: "Blocked before accountant review",
      readyForAccountantApproval: false,
      readyForExternalFiling: false,
      approvedBy: undefined,
      approvedAt: undefined,
      steps: [
        {
          code: "generated-outputs",
          label: "Generated statutory outputs",
          state: "blocked",
          detail: "Generate the CRO accounts PDF and complete internal iXBRL checks.",
          sources: [],
        },
      ],
      openBlockers: [
        "Named qualified-accountant approval required",
        "External ROS/iXBRL validation evidence required",
      ],
      openWarnings: [],
      allowedNextActions: ["record-accountant-review"],
    },
  };
}
