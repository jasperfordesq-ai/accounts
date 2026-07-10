import type { TransactionSortDirection, TransactionSortField } from "@/lib/api";
import {
  enumSearchParam,
  numericIdentifierSearchParam,
  positiveIntegerSearchParam,
} from "@/lib/interactionState";

export type WorkspaceTabId = "import" | "categorise" | "yearend" | "adjustments" | "statements" | "filing";

const WORKSPACE_TAB_IDS = new Set<WorkspaceTabId>([
  "import",
  "categorise",
  "yearend",
  "adjustments",
  "statements",
  "filing",
]);
const TRANSACTION_STATUS_FILTERS = new Set(["", "uncategorised"] as const);
const TRANSACTION_SORT_FIELDS = new Set<TransactionSortField>(["date", "description", "amount", "confidence"]);
const TRANSACTION_SORT_DIRECTIONS = new Set<TransactionSortDirection>(["asc", "desc"]);
const TRANSACTION_PAGE_SIZES = new Set([25, 50, 100]);
const ADJUSTMENT_APPROVAL_FILTERS = new Set(["", "pending", "approved"] as const);
const ADJUSTMENT_SOURCE_FILTERS = new Set(["", "auto", "manual"] as const);

type SearchParamsReader = Pick<URLSearchParams, "get">;

export interface PeriodWorkspaceQueryState {
  selectedWorkspaceTab: WorkspaceTabId;
  transactionPage: number;
  transactionPageSize: number;
  transactionSortBy: TransactionSortField;
  transactionSortDirection: TransactionSortDirection;
  txFilterCategory: string;
  txFilterBank: string;
  txFilterStatus: string;
  txFilterSearch: string;
  adjFilterApproved: string;
  adjFilterType: string;
}

export function normaliseWorkspaceTab(value: string | null): WorkspaceTabId {
  if (value === "year-end") return "yearend";
  if (value === "review") return "filing";
  if (value && WORKSPACE_TAB_IDS.has(value as WorkspaceTabId)) return value as WorkspaceTabId;

  return "import";
}

export function parsePeriodWorkspaceQuery(searchParams: SearchParamsReader): PeriodWorkspaceQueryState {
  return {
    selectedWorkspaceTab: normaliseWorkspaceTab(searchParams.get("tab")),
    transactionPage: positiveIntegerSearchParam(searchParams, "txPage", 1),
    transactionPageSize: positiveIntegerSearchParam(searchParams, "txPageSize", 50, TRANSACTION_PAGE_SIZES),
    transactionSortBy: enumSearchParam(searchParams, "txSort", TRANSACTION_SORT_FIELDS, "date"),
    transactionSortDirection: enumSearchParam(searchParams, "txDirection", TRANSACTION_SORT_DIRECTIONS, "desc"),
    txFilterCategory: numericIdentifierSearchParam(searchParams, "txCategory"),
    txFilterBank: numericIdentifierSearchParam(searchParams, "txBank"),
    txFilterStatus: enumSearchParam(searchParams, "txStatus", TRANSACTION_STATUS_FILTERS, ""),
    txFilterSearch: searchParams.get("txSearch") ?? "",
    adjFilterApproved: enumSearchParam(searchParams, "adjApproval", ADJUSTMENT_APPROVAL_FILTERS, ""),
    adjFilterType: enumSearchParam(searchParams, "adjSource", ADJUSTMENT_SOURCE_FILTERS, ""),
  };
}
