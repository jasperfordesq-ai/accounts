export const FINANCIAL_STATEMENT_TAB_IDS = [
  "trial-balance",
  "sources",
  "pnl",
  "balance-sheet",
  "tax-computation",
  "cash-flow",
  "equity-changes",
  "directors-report",
] as const;

export type FinancialStatementTabId = (typeof FINANCIAL_STATEMENT_TAB_IDS)[number];

export const FINANCIAL_STATEMENT_TAB_ID_SET: ReadonlySet<FinancialStatementTabId> =
  new Set(FINANCIAL_STATEMENT_TAB_IDS);
