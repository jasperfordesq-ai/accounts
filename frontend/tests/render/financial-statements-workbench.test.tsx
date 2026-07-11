import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { FinancialStatementsWorkbench } from "@/components/statements/FinancialStatementsWorkbench";
import type {
  AccountingPeriod,
  BalanceSheet,
  CashFlowStatement,
  Company,
  DirectorsReportData,
  EquityChanges,
  ProfitAndLoss,
  StatementSourceSummary,
  TaxComputation,
  TrialBalanceLine,
} from "@/lib/api";

describe("FinancialStatementsWorkbench", () => {
  it("renders statement preview context, health badges, tabs and print action", async () => {
    const user = userEvent.setup();
    const print = vi.fn();
    vi.stubGlobal("print", print);

    render(<FinancialStatementsWorkbench {...props()} canViewWorkingPapers />);

    expect(screen.getByRole("link", { name: "Back to Period Workspace" })).toHaveAttribute(
      "href",
      "/companies/7/periods/3",
    );
    expect(screen.getByRole("heading", { name: "Financial Statements", level: 1 })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Accountant working papers" })).toHaveAttribute(
      "href",
      "/companies/7/periods/3/working-papers",
    );
    expect(screen.getByText("Connacht Workbench Limited - 01 Jan 2026 to 31 Dec 2026")).toBeInTheDocument();
    expect(screen.getByText("Trial balance agrees")).toBeInTheDocument();
    expect(screen.getByText("Balance sheet agrees")).toBeInTheDocument();
    expect(screen.getByText("2 source rows")).toBeInTheDocument();

    expect(screen.getByRole("tab", { name: /Trial Balance/ })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /Source Trail/ })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /Directors' Report/ })).toBeInTheDocument();
    expect(screen.getByText("Sales / Revenue")).toBeInTheDocument();
    expect(screen.getByText("Trial balance agrees - debits equal credits")).toBeInTheDocument();
    expect(screen.getByRole("tablist", { name: "Financial statements tabs" }))
      .toHaveAttribute("data-overflow-tablist", "true");
    expect(screen.getByText(/Swipe to reveal more statement tabs/)).toBeVisible();
    expect(screen.getByRole("region", { name: "Trial balance table" })).toHaveAttribute("tabindex", "0");

    const trialBalanceTab = screen.getByRole("tab", { name: /Trial Balance/ });
    const sourceTrailTab = screen.getByRole("tab", { name: /Source Trail/ });
    trialBalanceTab.focus();
    await user.keyboard("{ArrowRight}");
    expect(sourceTrailTab).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("region", { name: "Statement source trail table" })).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /Print/ }));

    expect(print).toHaveBeenCalledTimes(1);
  });

  it("renders a failed statement read as an error, never as an empty statement", () => {
    render(
      <FinancialStatementsWorkbench
        {...props()}
        trialBalance={null}
        statementState={{
          status: "partial-error",
          error: "Malformed trial balance response",
          failedResourceKeys: ["trial-balance"],
          hasRetainedData: true,
        }}
        onRetryStatements={vi.fn()}
      />,
    );

    expect(screen.getAllByText("trial balance evidence unavailable").length).toBeGreaterThan(0);
    expect(screen.getAllByText(/Malformed trial balance response/).length).toBeGreaterThan(0);
    expect(screen.queryByText("Trial balance data is not available yet.")).not.toBeInTheDocument();
  });

  it("renders a URL-controlled statement tab and reports supported tab changes", async () => {
    const user = userEvent.setup();
    const onStatementTabChange = vi.fn();
    render(
      <FinancialStatementsWorkbench
        {...props()}
        selectedStatementTab="tax-computation"
        onStatementTabChange={onStatementTabChange}
      />,
    );

    expect(screen.getByRole("tab", { name: /Tax Computation/ })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByText("Corporation Tax Support Data")).toBeInTheDocument();
    await user.click(screen.getByRole("tab", { name: /Source Trail/ }));
    expect(onStatementTabChange).toHaveBeenCalledWith("sources");
  });

  it("explains the filing-regime prerequisite without presenting it as a failed request", () => {
    render(
      <FinancialStatementsWorkbench
        {...props()}
        directorsReport={null}
        selectedStatementTab="directors-report"
      />,
    );

    expect(screen.getByText(
      "Complete the filing-regime classification before reviewing the directors' report.",
    )).toBeInTheDocument();
    expect(screen.getByText(
      "Choose the statutory size and filing regime for this period first.",
    )).toBeInTheDocument();
    expect(screen.queryByText(
      "Complete the year-end process and generate adjustments first.",
    )).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open classification" })).toHaveAttribute(
      "href",
      "/companies/7/periods/3/classify",
    );
    expect(screen.queryByRole("button", { name: /retry directors-report/i })).not.toBeInTheDocument();
  });
});

function props(): Parameters<typeof FinancialStatementsWorkbench>[0] {
  return {
    company: sampleCompany(),
    period: samplePeriod(),
    companyId: "7",
    periodId: "3",
    error: null,
    trialBalance: sampleTrialBalance(),
    pnl: samplePnl(),
    bs: sampleBalanceSheet(),
    tax: sampleTaxComputation(),
    cashFlow: sampleCashFlow(),
    equity: sampleEquity(),
    directorsReport: sampleDirectorsReport(),
    sources: sampleSources(),
    onRetry: vi.fn(),
  };
}

function sampleCompany(): Company {
  return {
    id: 7,
    legalName: "Connacht Workbench Limited",
    companyType: "Private",
    incorporationDate: "2024-01-01",
    financialYearStartMonth: 1,
    annualReturnDate: "2026-09-15",
    isGroupMember: false,
    isHolding: false,
    isInvestment: false,
    isSubsidiary: false,
    isDormant: false,
    isTrading: true,
    isVatRegistered: true,
    isEmployer: false,
    hasStock: false,
    ownsAssets: true,
    hasBorrowings: false,
    hasDirectorLoans: false,
    isListedSecurities: false,
    isCreditInstitution: false,
    isInsuranceUndertaking: false,
    isPensionFund: false,
    isCharitableOrganisation: false,
  };
}

function samplePeriod(): AccountingPeriod {
  return {
    id: 3,
    companyId: 7,
    periodStart: "2026-01-01",
    periodEnd: "2026-12-31",
    status: "Review",
    isFirstYear: false,
    memberAuditNoticeReceived: false,
    goingConcernConfirmed: true,
  };
}

function sampleTrialBalance(): TrialBalanceLine[] {
  return [
    { code: "4000", name: "Sales / Revenue", type: "Income", debit: 0, credit: 120_000 },
    { code: "7900", name: "Retained earnings", type: "Equity", debit: 120_000, credit: 0 },
  ];
}

function sampleSources(): StatementSourceSummary[] {
  return [
    {
      code: "4000",
      name: "Sales / Revenue",
      type: "Income",
      openingDebit: 0,
      openingCredit: 0,
      transactionDebit: 0,
      transactionCredit: 120_000,
      transactionCount: 12,
      adjustmentDebit: 0,
      adjustmentCredit: 0,
      adjustmentCount: 0,
      closingDebit: 0,
      closingCredit: 120_000,
      sourceNotes: ["Imported sales receipts"],
    },
    {
      code: "7900",
      name: "Retained earnings",
      type: "Equity",
      openingDebit: 120_000,
      openingCredit: 0,
      transactionDebit: 0,
      transactionCredit: 0,
      transactionCount: 0,
      adjustmentDebit: 0,
      adjustmentCredit: 0,
      adjustmentCount: 1,
      closingDebit: 120_000,
      closingCredit: 0,
      sourceNotes: ["Year-end close"],
    },
  ];
}

function samplePnl(): ProfitAndLoss {
  return {
    turnover: 120_000,
    costOfSales: 20_000,
    grossProfit: 100_000,
    otherIncome: 0,
    overheads: [{ code: "7000", name: "Administration", amount: 10_000 }],
    totalOverheads: 10_000,
    operatingProfit: 90_000,
    interestPayable: 0,
    profitBeforeTax: 90_000,
    taxCharge: 11_250,
    profitAfterTax: 78_750,
    yearEndAdjustments: [],
    totalYearEndAdjustments: 0,
  };
}

function sampleBalanceSheet(): BalanceSheet {
  return {
    fixedAssets: {
      categories: [{ category: "Computer equipment", cost: 10_000, depreciation: 2_000, nbv: 8_000 }],
      total: 8_000,
    },
    currentAssets: {
      stock: 0,
      debtors: 30_000,
      prepayments: 0,
      cash: 100_000,
      total: 130_000,
    },
    creditorsWithinYear: {
      tradeCreditors: 5_000,
      accruals: 1_000,
      taxCreditors: 11_250,
      otherCreditors: 0,
      total: 17_250,
    },
    netCurrentAssets: 112_750,
    totalAssetsLessCurrentLiabilities: 120_750,
    creditorsAfterYear: { loans: 0, other: 0, total: 0 },
    netAssets: 120_750,
    capitalAndReserves: {
      shareCapital: 42_000,
      openingRetainedEarnings: 0,
      profitForYear: 78_750,
      dividendsPaid: 0,
      otherReserveMovements: 0,
      retainedEarnings: 78_750,
      total: 120_750,
      unexplainedDifference: 0,
    },
    balances: true,
  };
}

function sampleTaxComputation(): TaxComputation {
  return {
    accountingProfit: 90_000,
    adjustments: [],
    taxableProfit: 90_000,
    tradingLossAvailable: 0,
    corporationTaxAt125: 11_250,
    corporationTaxAt25: 0,
    totalCorporationTax: 11_250,
    preliminaryTaxPaid: 0,
    balanceDue: 11_250,
    notes: "Trading income taxed at 12.5%.",
    tradingProfitBeforeLossRelief: 90_000,
    tradingProfitAfterLossRelief: 90_000,
    passiveNonTradingIncome: 0,
    broughtForwardTradingLoss: 0,
    tradingLossUsed: 0,
    tradingLossCarriedForward: 0,
    capitalAllowances: 0,
    balancingAllowances: 0,
    balancingCharges: 0,
    supportStatus: "machine-supported-simple-scope",
    finalTaxChargeSupported: true,
    manualReviewRequired: true,
    outputKind: "corporation-tax-support-data-not-ct1-return",
    isCompleteCt1Return: false,
    blockingReasons: [],
    sources: [{ code: "revenue-ct", title: "Revenue corporation tax", url: "https://www.revenue.ie/" }],
    calculationSha256: "a".repeat(64),
  };
}

function sampleCashFlow(): CashFlowStatement {
  return {
    operatingProfit: 90_000,
    operatingAdjustments: [],
    cashFromOperations: 90_000,
    taxPaid: 0,
    netCashFromOperating: 90_000,
    capitalExpenditurePurchases: 10_000,
    capitalExpenditureDisposals: 0,
    netCashFromInvesting: -10_000,
    loanRepayments: 0,
    loanDrawdowns: 0,
    dividendsPaid: 0,
    shareIssues: 0,
    otherFinancing: 0,
    netCashFromFinancing: 0,
    netIncreaseInCash: 80_000,
    openingCash: 20_000,
    closingCash: 100_000,
  };
}

function sampleEquity(): EquityChanges {
  return {
    openingShareCapital: 42_000,
    openingRetainedEarnings: 0,
    openingTotal: 42_000,
    profitForYear: 78_750,
    dividendsPaid: 0,
    otherReserveMovements: 0,
    sharesIssued: 0,
    closingShareCapital: 42_000,
    closingRetainedEarnings: 78_750,
    closingTotal: 120_750,
  };
}

function sampleDirectorsReport(): DirectorsReportData {
  return {
    companyName: "Connacht Workbench Limited",
    periodStart: "2026-01-01",
    periodEnd: "2026-12-31",
    directorNames: ["Aisling Director"],
    directorServicePeriods: [{
      name: "Aisling Director",
      role: "Director",
      appointedDate: "2023-04-01",
    }],
    secretaryName: "Brian Secretary",
    secretaryServicePeriod: {
      name: "Brian Secretary",
      role: "Secretary",
      appointedDate: "2023-04-01",
    },
    principalActivities: "Cloud accounting services.",
    principalActivitiesReviewed: true,
    principalActivitiesReviewedBy: "Roisin Reviewer",
    principalActivitiesReviewedAt: "2027-01-15T10:00:00Z",
    resultsAndDividends: "The company made a profit after tax.",
    profitOrLossAfterTax: 10_000,
    dividendsPaid: 0,
    dividendsDeclaredNotPaid: 0,
    accountingRecordsStatement: "Accounting records are maintained in Ireland.",
    postBalanceSheetEventsReviewed: true,
    auditInformationEvidenceRequired: false,
    auditInformationEvidenceRecorded: true,
    officerTimelineComplete: true,
    isMicroExempt: false,
    isSmallExemptFromBusinessReview: true,
    electedRegime: "SmallAbridged",
  };
}
