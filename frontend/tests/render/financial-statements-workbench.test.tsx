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
    expect(screen.getByText("Simple-scope machine checks passed - the directors' review is still required")).toBeInTheDocument();
    expect(screen.queryByText(/qualified review/i)).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Revenue corporation tax" })).toHaveClass("min-h-6");
    await user.click(screen.getByRole("tab", { name: /Source Trail/ }));
    expect(onStatementTabChange).toHaveBeenCalledWith("sources");
  });

  it("reserves non-overlapping amount columns for the mobile fixed-assets schedule", () => {
    render(<FinancialStatementsWorkbench {...props()} selectedStatementTab="balance-sheet" />);

    const category = screen.getByText("Computer equipment");
    expect(category.parentElement).toHaveClass(
      "grid-cols-[minmax(0,1fr)_repeat(3,minmax(4.75rem,auto))]",
      "text-xs",
    );
    expect(screen.getByText("€10,000.00")).toHaveClass("whitespace-nowrap");
  });

  it("keeps the capital and reserves presentation for a company on the generic chart", () => {
    render(<FinancialStatementsWorkbench {...props()} selectedStatementTab="balance-sheet" />);

    expect(screen.getByText("Share Capital")).toBeInTheDocument();
    expect(screen.getByText("Total Capital and Reserves")).toBeInTheDocument();
    expect(screen.queryByText("Bank Overdraft")).not.toBeInTheDocument();
    expect(screen.queryByText("Total Members' Funds")).not.toBeInTheDocument();
  });

  it("presents members' funds and the bank overdraft for a company on the charity chart", () => {
    render(
      <FinancialStatementsWorkbench
        {...props()}
        bs={sampleCharityBalanceSheet()}
        selectedStatementTab="balance-sheet"
      />,
    );

    expect(screen.getByText("Bank Overdraft")).toBeInTheDocument();
    expect(screen.getByText("€1,620.00")).toBeInTheDocument();
    expect(screen.getByText("Members' Funds")).toBeInTheDocument();
    expect(screen.getByText("Members' funds brought forward")).toBeInTheDocument();
    expect(screen.getByText("Surplus/(deficit) for the financial year")).toBeInTheDocument();
    expect(screen.getByText("Restricted funds")).toBeInTheDocument();
    expect(screen.getByText("Total Members' Funds")).toBeInTheDocument();
    expect(screen.queryByText("Share Capital")).not.toBeInTheDocument();
    expect(screen.queryByText("Total Capital and Reserves")).not.toBeInTheDocument();
    expect(screen.queryByText("Other movements in members' funds")).not.toBeInTheDocument();
  });

  it("hides a nil bank overdraft for a company on the charity chart", () => {
    const bs = sampleCharityBalanceSheet();
    bs.currentAssets = { ...bs.currentAssets, cash: 1_820, total: 1_820 };
    bs.creditorsWithinYear = { ...bs.creditorsWithinYear, bankOverdraft: 0, total: 250 };
    bs.netCurrentAssets = 1_570;
    render(<FinancialStatementsWorkbench {...props()} bs={bs} selectedStatementTab="balance-sheet" />);

    expect(screen.getByText("Total Members' Funds")).toBeInTheDocument();
    expect(screen.queryByText("Bank Overdraft")).not.toBeInTheDocument();
  });

  it("labels the statement of changes in members' funds for a company on the charity chart", () => {
    const { unmount } = render(
      <FinancialStatementsWorkbench
        {...props()}
        bs={sampleCharityBalanceSheet()}
        equity={{ ...sampleEquity(), openingShareCapital: 0, openingTotal: 0, closingShareCapital: 0, closingTotal: 78_750 }}
        selectedStatementTab="equity-changes"
      />,
    );

    expect(screen.getByRole("table", { name: "Statement of Changes in Members' Funds" })).toBeInTheDocument();
    expect(screen.getByText("Movements in members' funds for the period")).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "Members' Funds" })).toBeInTheDocument();
    expect(screen.getByText("Surplus/(deficit) for the Year")).toBeInTheDocument();
    expect(screen.queryByRole("columnheader", { name: "Retained Earnings" })).not.toBeInTheDocument();
    // No nil share capital column and no nil dividends row for a company limited by guarantee.
    expect(screen.queryByRole("columnheader", { name: "Share Capital" })).not.toBeInTheDocument();
    expect(screen.queryByText("Dividends Paid")).not.toBeInTheDocument();
    unmount();

    render(<FinancialStatementsWorkbench {...props()} selectedStatementTab="equity-changes" />);
    expect(screen.getByRole("columnheader", { name: "Retained Earnings" })).toBeInTheDocument();
    expect(screen.getByText("Profit for the Year")).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "Share Capital" })).toBeInTheDocument();
    expect(screen.getByText("Dividends Paid")).toBeInTheDocument();
  });

  it("keeps the profit and loss wording for a company on the generic chart", () => {
    const { unmount } = render(<FinancialStatementsWorkbench {...props()} selectedStatementTab="pnl" />);

    expect(screen.getByRole("tab", { name: /Profit & Loss/ })).toBeInTheDocument();
    expect(screen.getByText("Profit & Loss Account")).toBeInTheDocument();
    expect(screen.getByText("Turnover")).toBeInTheDocument();
    expect(screen.getByText("Gross Profit")).toBeInTheDocument();
    expect(screen.getByText("Profit Before Tax")).toBeInTheDocument();
    expect(screen.getByText("Less: Corporation Tax")).toBeInTheDocument();
    expect(screen.getByText("Profit After Tax")).toBeInTheDocument();
    expect(screen.queryByText(/Surplus/)).not.toBeInTheDocument();
    unmount();

    // The generic layout shows the tax line even when it is nil.
    render(<FinancialStatementsWorkbench {...props()} pnl={{ ...samplePnl(), taxCharge: 0 }} selectedStatementTab="pnl" />);
    expect(screen.getByText("Less: Corporation Tax")).toBeInTheDocument();
  });

  it("presents the income and expenditure account for a company on the charity chart", () => {
    const { unmount } = render(
      <FinancialStatementsWorkbench {...props()} bs={sampleCharityBalanceSheet()} selectedStatementTab="pnl" />,
    );

    expect(screen.getByRole("tab", { name: /Income & Expenditure/ })).toBeInTheDocument();
    expect(screen.getByText("Income and Expenditure Account")).toBeInTheDocument();
    expect(screen.getByText("Income")).toBeInTheDocument();
    expect(screen.getByText("Gross Surplus/(deficit)")).toBeInTheDocument();
    expect(screen.getByText("Operating Surplus/(deficit)")).toBeInTheDocument();
    expect(screen.getByText("Surplus/(deficit) before taxation")).toBeInTheDocument();
    expect(screen.getByText("Surplus/(deficit) for the financial year")).toBeInTheDocument();
    expect(screen.getByText("Less: Corporation Tax")).toBeInTheDocument();
    expect(screen.queryByText(/Turnover|Profit/)).not.toBeInTheDocument();
    unmount();

    // The tax line is kept only when it is non-zero.
    render(
      <FinancialStatementsWorkbench
        {...props()}
        bs={sampleCharityBalanceSheet()}
        pnl={{ ...samplePnl(), taxCharge: 0 }}
        selectedStatementTab="pnl"
      />,
    );
    expect(screen.queryByText("Less: Corporation Tax")).not.toBeInTheDocument();
    expect(screen.getByText("Surplus/(deficit) for the financial year")).toBeInTheDocument();
  });

  it("titles the directors' report results section by chart", () => {
    const { unmount } = render(
      <FinancialStatementsWorkbench {...props()} bs={sampleCharityBalanceSheet()} selectedStatementTab="directors-report" />,
    );
    expect(screen.getByRole("heading", { name: "Results" })).toBeInTheDocument();
    expect(screen.queryByText("Results and Dividends")).not.toBeInTheDocument();
    unmount();

    render(<FinancialStatementsWorkbench {...props()} selectedStatementTab="directors-report" />);
    expect(screen.getByRole("heading", { name: "Results and Dividends" })).toBeInTheDocument();
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

// Charity chart: the overdrawn bank (1,620) is within creditors, not negative cash; members' funds
// replace share capital.
function sampleCharityBalanceSheet(): BalanceSheet {
  return {
    fixedAssets: { categories: [], total: 1_200 },
    currentAssets: { stock: 0, debtors: 0, prepayments: 0, cash: 200, total: 200 },
    creditorsWithinYear: {
      tradeCreditors: 0,
      accruals: 0,
      taxCreditors: 0,
      otherCreditors: 250,
      total: 1_870,
      bankOverdraft: 1_620,
    },
    netCurrentAssets: -1_670,
    totalAssetsLessCurrentLiabilities: -470,
    creditorsAfterYear: { loans: 0, other: 800, total: 800 },
    netAssets: -1_270,
    capitalAndReserves: {
      shareCapital: 0,
      openingRetainedEarnings: 1_000,
      profitForYear: -2_520,
      dividendsPaid: 0,
      otherReserveMovements: 250,
      retainedEarnings: -1_270,
      total: -1_270,
      unexplainedDifference: 0,
    },
    balances: true,
    membersFunds: {
      fundsBroughtForward: 1_000,
      surplusForYear: -2_520,
      otherMovements: 0,
      restrictedFunds: 250,
      total: -1_270,
    },
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
