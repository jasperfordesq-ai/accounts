import { z } from "zod";

const API_BASE = "";
const ACCOUNTS_CSRF_COOKIE = "accounts_csrf";
const CSRF_HEADER = "X-CSRF-Token";
export const SESSION_EXPIRED_EVENT = "accounts:session-expired";

export interface SessionExpiredEventDetail {
  returnTo?: string;
}

const SAFE_MESSAGE_STATUSES = new Set([400, 409, 422]);
const RESPONSE_MESSAGE_FIELDS = ["message", "error", "title", "detail"];
const INFRASTRUCTURE_ERROR_MARKERS = [
  "api_proxy_misconfigured",
  "upstream_unavailable",
  "stack trace",
  "exception",
  "System.",
  "Npgsql",
  "SqlException",
  "ECONNREFUSED",
  "ETIMEDOUT",
];

function statusMessage(status: number): string {
  switch (status) {
    case 400: return "Invalid request. Please check your input.";
    case 401: return "Your session has expired. Please sign in again.";
    case 403: return "You do not have permission for this action.";
    case 404: return "The requested resource was not found.";
    case 409: return "A conflict occurred. The data may have been modified.";
    case 422: return "Validation failed. Please check your input.";
    case 429: return "Too many requests. Please wait a moment.";
    case 500: return "Server error. Please try again later.";
    case 502: return "The API server is unavailable. Please try again.";
    case 503: return "Service temporarily unavailable. Please try again.";
    default: return `Request failed (${status})`;
  }
}

function extractResponseMessage(body: string): string | null {
  if (!body) return null;

  try {
    const parsed: unknown = JSON.parse(body);
    if (typeof parsed === "string") return parsed;

    if (parsed && typeof parsed === "object") {
      const record = parsed as Record<string, unknown>;
      for (const field of RESPONSE_MESSAGE_FIELDS) {
        const value = record[field];
        if (typeof value === "string") return value;
      }
    }
  } catch {
    return body.length < 200 ? body : null;
  }

  return null;
}

function shouldExposeResponseMessage(status: number, message: string): boolean {
  if (status >= 500 || !SAFE_MESSAGE_STATUSES.has(status)) return false;

  const trimmed = message.trim();
  if (!trimmed) return false;

  const lowerMessage = trimmed.toLowerCase();
  return !INFRASTRUCTURE_ERROR_MARKERS.some((marker) =>
    lowerMessage.includes(marker.toLowerCase())
  );
}

function currentBrowserPath(): string {
  if (typeof window === "undefined") return "/";

  const path = `${window.location.pathname}${window.location.search}${window.location.hash}`;
  return path || "/";
}

export function dispatchSessionExpired(returnTo = currentBrowserPath()) {
  if (typeof window === "undefined") return;

  window.dispatchEvent(new CustomEvent<SessionExpiredEventDetail>(
    SESSION_EXPIRED_EVENT,
    { detail: { returnTo } },
  ));
}

// --- Core fetch with retry, timeout, structured errors ---

class ApiError extends Error {
  status: number;
  statusText: string;
  body: string;

  constructor(status: number, statusText: string, body: string) {
    super(ApiError.formatMessage(status, body));
    this.status = status;
    this.statusText = statusText;
    this.body = body;
    this.name = "ApiError";
  }

  static formatMessage(status: number, body: string): string {
    const responseMessage = extractResponseMessage(body);
    if (responseMessage && shouldExposeResponseMessage(status, responseMessage)) {
      return responseMessage;
    }

    return statusMessage(status);
  }

  get isRetryable(): boolean {
    return this.status >= 500 || this.status === 429;
  }

  get isNotFound(): boolean {
    return this.status === 404;
  }
}

export { ApiError };

const DEFAULT_TIMEOUT = 30000; // 30 seconds
const MAX_RETRIES = 2;
const RETRY_DELAYS = [500, 1500]; // ms delays between retries

function readCsrfToken(): string | undefined {
  if (typeof document === "undefined") return undefined;

  const prefix = `${ACCOUNTS_CSRF_COOKIE}=`;
  const cookie = document.cookie
    .split(";")
    .map((part) => part.trim())
    .find((part) => part.startsWith(prefix));

  return cookie ? decodeURIComponent(cookie.slice(prefix.length)) : undefined;
}

function isUnsafeMethod(method?: string) {
  const normalized = (method ?? "GET").toUpperCase();
  return method !== "GET" && !["GET", "HEAD", "OPTIONS", "TRACE"].includes(normalized);
}

function withCsrfHeader(method: string | undefined, headers?: HeadersInit): HeadersInit {
  const nextHeaders = new Headers(headers);
  const csrfToken = isUnsafeMethod(method) ? readCsrfToken() : undefined;
  if (csrfToken) nextHeaders.set(CSRF_HEADER, csrfToken);
  return nextHeaders;
}

async function apiFetch<T>(
  path: string,
  options?: RequestInit & { timeout?: number; retries?: number },
): Promise<T> {
  const { timeout = DEFAULT_TIMEOUT, retries = MAX_RETRIES, ...fetchOptions } = options ?? {};
  const effectiveRetries = isUnsafeMethod(fetchOptions.method) ? 0 : retries;

  let lastError: Error | null = null;

  for (let attempt = 0; attempt <= effectiveRetries; attempt++) {
    try {
      const controller = new AbortController();
      const timeoutId = setTimeout(() => controller.abort(), timeout);

      const res = await fetch(`${API_BASE}${path}`, {
        ...fetchOptions,
        headers: withCsrfHeader(fetchOptions.method, {
          "Content-Type": "application/json",
          ...fetchOptions?.headers,
        }),
        credentials: "include",
        signal: controller.signal,
      });

      clearTimeout(timeoutId);

      if (!res.ok) {
        const body = await res.text().catch(() => "");
        const error = new ApiError(res.status, res.statusText, body);

        // Only retry on server errors and rate limits (not client errors)
        if (error.isRetryable && attempt < effectiveRetries) {
          lastError = error;
          await new Promise((r) => setTimeout(r, RETRY_DELAYS[attempt] ?? 1500));
          continue;
        }

        if (error.status === 401) {
          dispatchSessionExpired();
        }

        throw error;
      }

      if (res.status === 204) return undefined as T;
      return res.json();
    } catch (err) {
      if (err instanceof ApiError) throw err;

      // Handle abort (timeout)
      if (err instanceof DOMException && err.name === "AbortError") {
        lastError = new Error("Request timed out. Please try again.");
        if (attempt < effectiveRetries) {
          await new Promise((r) => setTimeout(r, RETRY_DELAYS[attempt] ?? 1500));
          continue;
        }
        throw lastError;
      }

      // Handle network errors
      if (err instanceof TypeError && err.message.includes("fetch")) {
        lastError = new Error("Network error. Please check your connection.");
        if (attempt < effectiveRetries) {
          await new Promise((r) => setTimeout(r, RETRY_DELAYS[attempt] ?? 1500));
          continue;
        }
        throw lastError;
      }

      throw err;
    }
  }

  throw lastError ?? new Error("Request failed after retries");
}

// === Types ===

export interface Company {
  id: number;
  legalName: string;
  tradingName?: string;
  croNumber?: string;
  taxReference?: string;
  companyType: string;
  incorporationDate: string;
  financialYearStartMonth: number;
  ardMonth: number;
  registeredOfficeAddress1?: string;
  registeredOfficeAddress2?: string;
  registeredOfficeCity?: string;
  registeredOfficeCounty?: string;
  registeredOfficeEircode?: string;
  isGroupMember: boolean;
  isHolding: boolean;
  isInvestment: boolean;
  isSubsidiary: boolean;
  isDormant: boolean;
  isTrading: boolean;
  isVatRegistered: boolean;
  isEmployer: boolean;
  hasStock: boolean;
  ownsAssets: boolean;
  hasBorrowings: boolean;
  hasDirectorLoans: boolean;
  // Fifth Schedule ineligibility flags
  isListedSecurities: boolean;
  isCreditInstitution: boolean;
  isInsuranceUndertaking: boolean;
  isPensionFund: boolean;
  isCharitableOrganisation: boolean;
  assignedReviewerName?: string;
  assignedReviewerEmail?: string;
  latestPeriod?: AccountingPeriod;
  officers?: Officer[];
  periods?: AccountingPeriod[];
  periodCount?: number;
}

export interface Officer {
  id?: number;
  companyId?: number;
  name: string;
  role: string;
  appointedDate?: string;
  resignedDate?: string;
  address?: string;
}

export interface AccountingPeriod {
  id: number;
  companyId: number;
  periodStart: string;
  periodEnd: string;
  status: string;
  isFirstYear: boolean;
  memberAuditNoticeReceived: boolean;
  memberAuditNoticeDate?: string;
  goingConcernConfirmed: boolean;
  goingConcernNote?: string;
  sizeClassification?: SizeClassification;
  filingRegime?: FilingRegime;
}

export interface SizeClassification {
  id: number;
  turnover: number;
  balanceSheetTotal: number;
  avgEmployees: number;
  calculatedClass: string;
  qualificationNotes?: string;
}

export interface FilingRegime {
  id: number;
  canUseMicro: boolean;
  canFileAbridged: boolean;
  auditExempt: boolean;
  electedRegime: string;
}

export interface BankAccount {
  id: number;
  companyId: number;
  name: string;
  iban?: string;
  currency: string;
  openingBalance: number;
  openingBalanceDate?: string;
}

export interface ImportedTransaction {
  id: number;
  date: string;
  description: string;
  amount: number;
  balance?: number;
  categoryId?: number;
  confidenceScore?: number;
  manualOverride: boolean;
  category?: AccountCategory;
}

export interface AccountCategory {
  id: number;
  code: string;
  name: string;
  type: string;
}

export interface OpeningBalance {
  id: number;
  periodId: number;
  accountCategoryId: number;
  debit: number;
  credit: number;
  sourceNote?: string;
  enteredBy?: string;
  enteredAt: string;
  reviewed: boolean;
  reviewedBy?: string;
  reviewedAt?: string;
  accountCategory: AccountCategory;
}

export interface TransactionRule {
  id: number;
  companyId: number;
  pattern: string;
  categoryId: number;
  priority: number;
}

export interface YearEndSummary {
  debtors: { count: number; total: number };
  creditors: { count: number; total: number };
  fixedAssets: { count: number; totalCost: number };
  inventory: { count: number; totalValue: number };
  loans: { count: number; totalBalance: number };
  directorLoans: { count: number };
  payroll: { grossWages: number; staffCount: number } | null;
  taxes: { count: number; totalLiability: number; totalBalance: number };
  dividends: { count: number; total: number };
  reviewConfirmations: YearEndReviewConfirmation[];
  completeness: { score: number; completed: number; total: number; incomplete: string[] };
}

export interface YearEndReviewConfirmation {
  id: number;
  periodId: number;
  sectionKey: string;
  confirmed: boolean;
  confirmedBy?: string;
  confirmedAt: string;
  note?: string;
}

export interface ReadinessScore {
  completenessPercent: number;
  filingReadinessPercent: number;
  balanceSheetBalances: boolean;
  missingItems: string[];
  warnings: string[];
}

export interface Adjustment {
  id: number;
  description: string;
  amount: number;
  source: string;
  reason?: string;
  legalBasis?: string;
  impactOnProfit: number;
  impactOnAssets: number;
  isAuto: boolean;
  approvedBy?: string;
  approvedAt?: string;
}

export interface AdjustmentSummary {
  autoGenerated: number;
  manual: number;
  pendingApproval: number;
  approved: number;
  totalImpactOnProfit: number;
  totalImpactOnAssets: number;
}

export interface Debtor {
  id?: number;
  periodId?: number;
  name: string;
  amount: number;
  type: string;
  notes?: string;
}

export interface Creditor {
  id?: number;
  periodId?: number;
  name: string;
  amount: number;
  type: string;
  dueWithinYear: boolean;
  notes?: string;
}

export interface FixedAsset {
  id?: number;
  companyId?: number;
  name: string;
  category: string;
  cost: number;
  acquisitionDate: string;
  disposalDate?: string;
  disposalProceeds?: number;
  usefulLifeYears: number;
  depreciationMethod: string;
}

export interface PayrollSummary {
  id?: number;
  periodId?: number;
  grossWages: number;
  employerPrsi: number;
  pensionContributions: number;
  staffCount: number;
}

export interface TaxBalance {
  id?: number;
  periodId?: number;
  taxType: string;
  liability: number;
  paid: number;
  balance: number;
}

export interface Dividend {
  id?: number;
  periodId?: number;
  amount: number;
  dateDeclared?: string;
  datePaid?: string;
}

export interface InventoryItem {
  id?: number;
  periodId?: number;
  description: string;
  value: number;
  valuationMethod: string;
}

export interface NotesDisclosure {
  id?: number;
  periodId?: number;
  noteNumber: number;
  title: string;
  content?: string;
  isRequired: boolean;
  isIncluded: boolean;
}

export interface TrialBalanceLine {
  code: string;
  name: string;
  type: string;
  debit: number;
  credit: number;
}

export interface StatementSourceSummary {
  code: string;
  name: string;
  type: string;
  openingDebit: number;
  openingCredit: number;
  transactionDebit: number;
  transactionCredit: number;
  transactionCount: number;
  adjustmentDebit: number;
  adjustmentCredit: number;
  adjustmentCount: number;
  closingDebit: number;
  closingCredit: number;
  sourceNotes: string[];
}

export interface ProfitAndLoss {
  turnover: number;
  costOfSales: number;
  grossProfit: number;
  overheads: { code: string; name: string; amount: number }[];
  totalOverheads: number;
  operatingProfit: number;
  interestPayable: number;
  profitBeforeTax: number;
  taxCharge: number;
  profitAfterTax: number;
  yearEndAdjustments: { description: string; amount: number; approved: boolean }[];
  totalYearEndAdjustments: number;
}

export interface BalanceSheet {
  fixedAssets: {
    categories: { category: string; cost: number; depreciation: number; nbv: number }[];
    total: number;
  };
  currentAssets: {
    stock: number;
    debtors: number;
    prepayments: number;
    cash: number;
    total: number;
  };
  creditorsWithinYear: {
    tradeCreditors: number;
    accruals: number;
    taxCreditors: number;
    otherCreditors: number;
    total: number;
  };
  netCurrentAssets: number;
  totalAssetsLessCurrentLiabilities: number;
  creditorsAfterYear: {
    loans: number;
    other: number;
    total: number;
  };
  netAssets: number;
  capitalAndReserves: {
    shareCapital: number;
    openingRetainedEarnings: number;
    profitForYear: number;
    dividendsPaid: number;
    retainedEarnings: number;
    total: number;
    unexplainedDifference: number;
  };
  balances: boolean;
}

export interface TaxComputation {
  accountingProfit: number;
  adjustments: { description: string; amount: number; basis: string }[];
  taxableProfit: number;
  corporationTaxAt125: number;
  corporationTaxAt25: number;
  totalCorporationTax: number;
  preliminaryTaxPaid: number;
  balanceDue: number;
  notes: string;
}

// === API Functions ===

// Companies
export const getCompanies = () => apiFetch<Company[]>("/api/companies");
export const getCompany = (id: number) => apiFetch<Company>(`/api/companies/${id}`);
export const createCompany = (data: Partial<Company>) =>
  apiFetch<Company>("/api/companies", { method: "POST", body: JSON.stringify(data) });
export const updateCompany = (id: number, data: Partial<Company>) =>
  apiFetch<Company>(`/api/companies/${id}`, { method: "PUT", body: JSON.stringify(data) });
export const deleteCompany = (id: number) =>
  apiFetch<void>(`/api/companies/${id}`, { method: "DELETE" });

// Officers
export const getOfficers = (companyId: number) =>
  apiFetch<Officer[]>(`/api/companies/${companyId}/officers`);
export const createOfficer = (companyId: number, data: Officer) =>
  apiFetch<Officer>(`/api/companies/${companyId}/officers`, {
    method: "POST",
    body: JSON.stringify(data),
  });
export const updateOfficer = (companyId: number, officerId: number, data: Partial<Officer>) =>
  apiFetch<Officer>(`/api/companies/${companyId}/officers/${officerId}`, {
    method: "PUT",
    body: JSON.stringify(data),
  });
export const deleteOfficer = (companyId: number, officerId: number) =>
  apiFetch<void>(`/api/companies/${companyId}/officers/${officerId}`, { method: "DELETE" });

// Periods
export const getPeriods = (companyId: number) =>
  apiFetch<AccountingPeriod[]>(`/api/companies/${companyId}/periods`);
export const getPeriod = (companyId: number, id: number) =>
  apiFetch<AccountingPeriod>(`/api/companies/${companyId}/periods/${id}`);
export const createPeriod = (companyId: number, data: Partial<AccountingPeriod>) =>
  apiFetch<AccountingPeriod>(`/api/companies/${companyId}/periods`, {
    method: "POST",
    body: JSON.stringify(data),
  });

// Bank Accounts
export const getBankAccounts = (companyId: number) =>
  apiFetch<BankAccount[]>(`/api/companies/${companyId}/bank-accounts`);
export const createBankAccount = (companyId: number, data: Partial<BankAccount>) =>
  apiFetch<BankAccount>(`/api/companies/${companyId}/bank-accounts`, {
    method: "POST",
    body: JSON.stringify(data),
  });
export const updateBankAccount = (companyId: number, id: number, data: Partial<BankAccount>) =>
  apiFetch<BankAccount>(`/api/companies/${companyId}/bank-accounts/${id}`, {
    method: "PUT",
    body: JSON.stringify(data),
  });
export const deleteBankAccount = (companyId: number, id: number) =>
  apiFetch<void>(`/api/companies/${companyId}/bank-accounts/${id}`, { method: "DELETE" });

// Transactions
export const getTransactions = (
  companyId: number,
  periodId: number,
  page = 1,
  pageSize = 50,
  filters?: { uncategorised?: boolean; categoryId?: number; bankAccountId?: number; search?: string }
) => {
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (filters?.uncategorised) params.set("uncategorised", "true");
  if (filters?.categoryId) params.set("categoryId", String(filters.categoryId));
  if (filters?.bankAccountId) params.set("bankAccountId", String(filters.bankAccountId));
  if (filters?.search) params.set("search", filters.search);
  return apiFetch<{ total: number; items: ImportedTransaction[] }>(
    `/api/companies/${companyId}/periods/${periodId}/transactions?${params}`
  );
};

export const categoriseTransaction = (
  companyId: number,
  periodId: number,
  transactionId: number,
  categoryId: number
) =>
  apiFetch<ImportedTransaction>(
    `/api/companies/${companyId}/periods/${periodId}/transactions/${transactionId}/categorise`,
    {
      method: "PUT",
      body: JSON.stringify({ categoryId }),
    }
  );
export const bulkCategoriseTransactions = (
  companyId: number,
  periodId: number,
  transactionIds: number[],
  categoryId: number,
) =>
  apiFetch<{ updated: number }>(
    `/api/companies/${companyId}/periods/${periodId}/transactions/bulk-categorise`,
    {
      method: "POST",
      body: JSON.stringify({ transactionIds, categoryId }),
    },
  );

// Categories
export const getCategories = (companyId: number) =>
  apiFetch<AccountCategory[]>(`/api/companies/${companyId}/categories`);
export const seedCategories = (companyId: number) =>
  apiFetch<AccountCategory[]>(`/api/companies/${companyId}/categories/seed`, { method: "POST" });

// Transaction Rules
export const getTransactionRules = (companyId: number) =>
  apiFetch<TransactionRule[]>(`/api/companies/${companyId}/transaction-rules`);
export const createTransactionRule = (companyId: number, data: Partial<TransactionRule>) =>
  apiFetch<TransactionRule>(`/api/companies/${companyId}/transaction-rules`, {
    method: "POST",
    body: JSON.stringify(data),
  });
export const deleteTransactionRule = (companyId: number, id: number) =>
  apiFetch<void>(`/api/companies/${companyId}/transaction-rules/${id}`, { method: "DELETE" });

// Year-End
export const getYearEndSummary = (companyId: number, periodId: number) =>
  apiFetch<YearEndSummary>(
    `/api/companies/${companyId}/periods/${periodId}/year-end-summary`
  );
export const getOpeningBalances = (companyId: number, periodId: number) =>
  apiFetch<OpeningBalance[]>(
    `/api/companies/${companyId}/periods/${periodId}/opening-balances`
  );
export const saveOpeningBalance = (
  companyId: number,
  periodId: number,
  categoryId: number,
  data: { debit: number; credit: number; sourceNote?: string; reviewed: boolean },
) =>
  apiFetch<OpeningBalance>(
    `/api/companies/${companyId}/periods/${periodId}/opening-balances/${categoryId}`,
    { method: "PUT", body: JSON.stringify(data) },
  );
export const deleteOpeningBalance = (companyId: number, periodId: number, categoryId: number) =>
  apiFetch<void>(
    `/api/companies/${companyId}/periods/${periodId}/opening-balances/${categoryId}`,
    { method: "DELETE" },
  );
export const getYearEndReviewConfirmations = (companyId: number, periodId: number) =>
  apiFetch<YearEndReviewConfirmation[]>(
    `/api/companies/${companyId}/periods/${periodId}/year-end-reviews`
  );
export const saveYearEndReviewConfirmation = (
  companyId: number,
  periodId: number,
  sectionKey: string,
  data: { confirmed: boolean; note?: string },
) =>
  apiFetch<YearEndReviewConfirmation>(
    `/api/companies/${companyId}/periods/${periodId}/year-end-reviews/${sectionKey}`,
    { method: "PUT", body: JSON.stringify(data) },
  );

// Adjustments
export const getAdjustments = (
  companyId: number,
  periodId: number,
  filters?: { approved?: boolean; isAuto?: boolean }
) => {
  const params = new URLSearchParams();
  if (filters?.approved !== undefined) params.set("approved", String(filters.approved));
  if (filters?.isAuto !== undefined) params.set("isAuto", String(filters.isAuto));
  const qs = params.toString();
  return apiFetch<Adjustment[]>(
    `/api/companies/${companyId}/periods/${periodId}/adjustments${qs ? `?${qs}` : ""}`
  );
};
export const getAdjustmentSummary = (companyId: number, periodId: number) =>
  apiFetch<AdjustmentSummary>(
    `/api/companies/${companyId}/periods/${periodId}/adjustments/summary`
  );
export const generateAdjustments = (companyId: number, periodId: number) =>
  apiFetch<AdjustmentSummary>(
    `/api/companies/${companyId}/periods/${periodId}/adjustments/generate`,
    { method: "POST" },
  );
export const approveAdjustment = (
  companyId: number,
  periodId: number,
  id: number,
) =>
  apiFetch<Adjustment>(
    `/api/companies/${companyId}/periods/${periodId}/adjustments/${id}/approve`,
    { method: "POST", body: JSON.stringify({}) },
  );

// Import
export const uploadBankCsv = async (
  companyId: number,
  bankAccountId: number,
  periodId: number,
  file: File,
) => {
  const controller = new AbortController();
  const timeoutId = setTimeout(() => controller.abort(), 120000); // 2 min for uploads

  try {
    const formData = new FormData();
    formData.append("file", file);
    const res = await fetch(
      `${API_BASE}/api/companies/${companyId}/bank-accounts/${bankAccountId}/import?periodId=${periodId}`,
      {
        method: "POST",
        body: formData,
        credentials: "include",
        headers: withCsrfHeader("POST"),
        signal: controller.signal,
      },
    );
    clearTimeout(timeoutId);
    if (!res.ok) {
      const body = await res.text().catch(() => "");
      const error = new ApiError(res.status, res.statusText, body);
      if (error.status === 401) {
        dispatchSessionExpired();
      }
      throw error;
    }
    return res.json();
  } catch (err) {
    clearTimeout(timeoutId);
    if (err instanceof DOMException && err.name === "AbortError") {
      throw new Error("Upload timed out. The file may be too large.");
    }
    throw err;
  }
};

// Statements
export const getReadiness = (companyId: number, periodId: number) =>
  apiFetch<ReadinessScore>(
    `/api/companies/${companyId}/periods/${periodId}/statements/readiness`
  );
export const getStatementSources = (companyId: number, periodId: number) =>
  apiFetch<StatementSourceSummary[]>(
    `/api/companies/${companyId}/periods/${periodId}/statements/sources`
  );

// Documents
export async function fetchDocumentBlob(url: string, method: "GET" | "POST" = "GET") {
  const response = await fetch(url, {
    method,
    credentials: "include",
    headers: withCsrfHeader(method),
  });

  if (!response.ok) {
    const body = await response.text().catch(() => "");
    const error = new ApiError(response.status, response.statusText, body);
    if (error.status === 401) {
      dispatchSessionExpired();
    }
    throw error;
  }

  return response.blob();
}

export const getAccountsPackageUrl = (companyId: number, periodId: number) =>
  `${API_BASE}/api/companies/${companyId}/periods/${periodId}/documents/accounts-package`;
export const getIxbrlUrl = (companyId: number, periodId: number) =>
  `${API_BASE}/api/companies/${companyId}/periods/${periodId}/revenue/ixbrl`;

// Debtors
export const getDebtors = (companyId: number, periodId: number) =>
  apiFetch<Debtor[]>(`/api/companies/${companyId}/periods/${periodId}/debtors`);
export const createDebtor = (companyId: number, periodId: number, data: Debtor) =>
  apiFetch<Debtor>(`/api/companies/${companyId}/periods/${periodId}/debtors`, {
    method: "POST",
    body: JSON.stringify(data),
  });
export const deleteDebtor = (companyId: number, periodId: number, id: number) =>
  apiFetch<void>(`/api/companies/${companyId}/periods/${periodId}/debtors/${id}`, {
    method: "DELETE",
  });

// Creditors
export const getCreditors = (companyId: number, periodId: number) =>
  apiFetch<Creditor[]>(`/api/companies/${companyId}/periods/${periodId}/creditors`);
export const createCreditor = (companyId: number, periodId: number, data: Creditor) =>
  apiFetch<Creditor>(`/api/companies/${companyId}/periods/${periodId}/creditors`, {
    method: "POST",
    body: JSON.stringify(data),
  });
export const deleteCreditor = (companyId: number, periodId: number, id: number) =>
  apiFetch<void>(`/api/companies/${companyId}/periods/${periodId}/creditors/${id}`, {
    method: "DELETE",
  });

// Fixed Assets
export const getFixedAssets = (companyId: number) =>
  apiFetch<FixedAsset[]>(`/api/companies/${companyId}/fixed-assets`);
export const createFixedAsset = (companyId: number, data: FixedAsset) =>
  apiFetch<FixedAsset>(`/api/companies/${companyId}/fixed-assets`, {
    method: "POST",
    body: JSON.stringify(data),
  });
export const deleteFixedAsset = (companyId: number, id: number) =>
  apiFetch<void>(`/api/companies/${companyId}/fixed-assets/${id}`, { method: "DELETE" });

// Payroll
export const getPayroll = (companyId: number, periodId: number) =>
  apiFetch<PayrollSummary>(`/api/companies/${companyId}/periods/${periodId}/payroll`).catch(
    () => null,
  );
export const savePayroll = (companyId: number, periodId: number, data: PayrollSummary) =>
  apiFetch<PayrollSummary>(`/api/companies/${companyId}/periods/${periodId}/payroll`, {
    method: "PUT",
    body: JSON.stringify(data),
  });

// Tax Balances
export const getTaxBalances = (companyId: number, periodId: number) =>
  apiFetch<TaxBalance[]>(`/api/companies/${companyId}/periods/${periodId}/tax-balances`);
export const saveTaxBalance = (
  companyId: number,
  periodId: number,
  taxType: string,
  data: TaxBalance,
) =>
  apiFetch<TaxBalance>(
    `/api/companies/${companyId}/periods/${periodId}/tax-balances/${taxType}`,
    { method: "PUT", body: JSON.stringify(data) },
  );

// Dividends
export const getDividends = (companyId: number, periodId: number) =>
  apiFetch<Dividend[]>(`/api/companies/${companyId}/periods/${periodId}/dividends`);
export const createDividend = (companyId: number, periodId: number, data: Dividend) =>
  apiFetch<Dividend>(`/api/companies/${companyId}/periods/${periodId}/dividends`, {
    method: "POST",
    body: JSON.stringify(data),
  });
export const deleteDividend = (companyId: number, periodId: number, id: number) =>
  apiFetch<void>(`/api/companies/${companyId}/periods/${periodId}/dividends/${id}`, {
    method: "DELETE",
  });
export const updateDividend = (
  companyId: number,
  periodId: number,
  id: number,
  data: Dividend,
) =>
  apiFetch<Dividend>(`/api/companies/${companyId}/periods/${periodId}/dividends/${id}`, {
    method: "PUT",
    body: JSON.stringify(data),
  });

// === Year-end row updates (BL-25) ===
export const updateDebtor = (companyId: number, periodId: number, id: number, data: Debtor) =>
  apiFetch<Debtor>(`/api/companies/${companyId}/periods/${periodId}/debtors/${id}`, {
    method: "PUT",
    body: JSON.stringify(data),
  });
export const updateCreditor = (companyId: number, periodId: number, id: number, data: Creditor) =>
  apiFetch<Creditor>(`/api/companies/${companyId}/periods/${periodId}/creditors/${id}`, {
    method: "PUT",
    body: JSON.stringify(data),
  });
export const updateFixedAsset = (companyId: number, id: number, data: FixedAsset) =>
  apiFetch<FixedAsset>(`/api/companies/${companyId}/fixed-assets/${id}`, {
    method: "PUT",
    body: JSON.stringify(data),
  });
export const updateInventory = (
  companyId: number,
  periodId: number,
  id: number,
  data: InventoryItem,
) =>
  apiFetch<InventoryItem>(`/api/companies/${companyId}/periods/${periodId}/inventory/${id}`, {
    method: "PUT",
    body: JSON.stringify(data),
  });

// === Loans (company-scoped) — BL-05 ===
export interface Loan {
  id?: number;
  companyId?: number;
  lender: string;
  originalAmount: number;
  balance: number;
  drawdownDate?: string;
  balanceAsOfDate?: string;
  interestRate: number;
  isDirectorLoan: boolean;
  dueWithinYear: number;
  dueAfterYear: number;
}

export const getLoans = (companyId: number) =>
  apiFetch<Loan[]>(`/api/companies/${companyId}/loans`);
export const createLoan = (companyId: number, data: Loan) =>
  apiFetch<Loan>(`/api/companies/${companyId}/loans`, {
    method: "POST",
    body: JSON.stringify(data),
  });
export const updateLoan = (companyId: number, id: number, data: Loan) =>
  apiFetch<Loan>(`/api/companies/${companyId}/loans/${id}`, {
    method: "PUT",
    body: JSON.stringify(data),
  });
export const deleteLoan = (companyId: number, id: number) =>
  apiFetch<void>(`/api/companies/${companyId}/loans/${id}`, { method: "DELETE" });

// === Loan balance snapshots (per period) — BL-05 ===
export interface LoanBalanceSnapshot {
  id?: number;
  loanId: number;
  periodId?: number;
  openingBalance: number;
  drawdowns: number;
  repayments: number;
  closingBalance: number;
  dueWithinYear: number;
  dueAfterYear: number;
}

export const getLoanSnapshots = (companyId: number, periodId: number) =>
  apiFetch<LoanBalanceSnapshot[]>(
    `/api/companies/${companyId}/periods/${periodId}/loan-balance-snapshots`,
  );
export const createLoanSnapshot = (
  companyId: number,
  periodId: number,
  data: LoanBalanceSnapshot,
) =>
  apiFetch<LoanBalanceSnapshot>(
    `/api/companies/${companyId}/periods/${periodId}/loan-balance-snapshots`,
    { method: "POST", body: JSON.stringify(data) },
  );
export const updateLoanSnapshot = (
  companyId: number,
  periodId: number,
  id: number,
  data: LoanBalanceSnapshot,
) =>
  apiFetch<LoanBalanceSnapshot>(
    `/api/companies/${companyId}/periods/${periodId}/loan-balance-snapshots/${id}`,
    { method: "PUT", body: JSON.stringify(data) },
  );
export const deleteLoanSnapshot = (companyId: number, periodId: number, id: number) =>
  apiFetch<void>(
    `/api/companies/${companyId}/periods/${periodId}/loan-balance-snapshots/${id}`,
    { method: "DELETE" },
  );

// === Director loans (per period) — BL-05 ===
export interface DirectorLoanRow {
  id?: number;
  periodId?: number;
  directorId: number;
  openingBalance: number;
  advances: number;
  repayments: number;
  closingBalance: number;
  interestRate: number;
  interestCharged: number;
  isDocumented: boolean;
  loanTerms?: string;
  maxBalanceDuringYear: number;
}

export const getDirectorLoans = (companyId: number, periodId: number) =>
  apiFetch<DirectorLoanRow[]>(
    `/api/companies/${companyId}/periods/${periodId}/director-loans`,
  );
export const createDirectorLoan = (
  companyId: number,
  periodId: number,
  data: DirectorLoanRow,
) =>
  apiFetch<DirectorLoanRow>(`/api/companies/${companyId}/periods/${periodId}/director-loans`, {
    method: "POST",
    body: JSON.stringify(data),
  });
export const updateDirectorLoan = (
  companyId: number,
  periodId: number,
  id: number,
  data: DirectorLoanRow,
) =>
  apiFetch<DirectorLoanRow>(
    `/api/companies/${companyId}/periods/${periodId}/director-loans/${id}`,
    { method: "PUT", body: JSON.stringify(data) },
  );
export const deleteDirectorLoan = (companyId: number, periodId: number, id: number) =>
  apiFetch<void>(`/api/companies/${companyId}/periods/${periodId}/director-loans/${id}`, {
    method: "DELETE",
  });

// === Share capital (company-scoped) — BL-05 ===
export interface ShareCapital {
  id?: number;
  companyId?: number;
  shareClass: string;
  nominalValue: number;
  numberIssued: number;
  totalValue: number;
  isFullyPaid: boolean;
  issueDate?: string;
  cancelledDate?: string;
}

export const getShareCapital = (companyId: number) =>
  apiFetch<ShareCapital[]>(`/api/companies/${companyId}/share-capital`);
export const createShareCapital = (companyId: number, data: ShareCapital) =>
  apiFetch<ShareCapital>(`/api/companies/${companyId}/share-capital`, {
    method: "POST",
    body: JSON.stringify(data),
  });
export const updateShareCapital = (companyId: number, id: number, data: ShareCapital) =>
  apiFetch<ShareCapital>(`/api/companies/${companyId}/share-capital/${id}`, {
    method: "PUT",
    body: JSON.stringify(data),
  });
export const deleteShareCapital = (companyId: number, id: number) =>
  apiFetch<void>(`/api/companies/${companyId}/share-capital/${id}`, { method: "DELETE" });

// Inventory
export const getInventory = (companyId: number, periodId: number) =>
  apiFetch<InventoryItem[]>(`/api/companies/${companyId}/periods/${periodId}/inventory`);
export const createInventory = (companyId: number, periodId: number, data: InventoryItem) =>
  apiFetch<InventoryItem>(`/api/companies/${companyId}/periods/${periodId}/inventory`, {
    method: "POST",
    body: JSON.stringify(data),
  });
export const deleteInventory = (companyId: number, periodId: number, id: number) =>
  apiFetch<void>(`/api/companies/${companyId}/periods/${periodId}/inventory/${id}`, {
    method: "DELETE",
  });

// Size Classification
export const saveSizeClassification = (
  companyId: number,
  periodId: number,
  data: {
    turnover: number;
    balanceSheetTotal: number;
    avgEmployees: number;
    priorYearClass?: string;
  },
) =>
  apiFetch<unknown>(
    `/api/companies/${companyId}/periods/${periodId}/size-classification`,
    { method: "PUT", body: JSON.stringify(data) },
  );

export const runClassification = (companyId: number, periodId: number) =>
  apiFetch<{
    calculatedClass: string;
    qualificationNotes: string;
    canUseMicro: boolean;
    canFileAbridged: boolean;
    auditExempt: boolean;
    availableRegimes: string[];
    isIneligibleEntity: boolean;
    ineligibleReason?: string;
  }>(`/api/companies/${companyId}/periods/${periodId}/classify`, { method: "POST" });

export const setFilingRegime = (companyId: number, periodId: number, electedRegime?: string) =>
  apiFetch<unknown>(`/api/companies/${companyId}/periods/${periodId}/filing-regime`, {
    method: "POST",
    body: JSON.stringify({ electedRegime }),
  });

// Notes
export const getNotes = (companyId: number, periodId: number) =>
  apiFetch<NotesDisclosure[]>(`/api/companies/${companyId}/periods/${periodId}/notes`);
export const generateNotes = (companyId: number, periodId: number) =>
  apiFetch<NotesDisclosure[]>(`/api/companies/${companyId}/periods/${periodId}/notes/generate`, {
    method: "POST",
  });
export const updateNote = (
  companyId: number,
  periodId: number,
  id: number,
  data: Partial<NotesDisclosure>,
) =>
  apiFetch<NotesDisclosure>(`/api/companies/${companyId}/periods/${periodId}/notes/${id}`, {
    method: "PUT",
    body: JSON.stringify(data),
  });
export const createNote = (companyId: number, periodId: number, data: Partial<NotesDisclosure>) =>
  apiFetch<NotesDisclosure>(`/api/companies/${companyId}/periods/${periodId}/notes`, {
    method: "POST",
    body: JSON.stringify(data),
  });
export const deleteNote = (companyId: number, periodId: number, id: number) =>
  apiFetch<void>(`/api/companies/${companyId}/periods/${periodId}/notes/${id}`, {
    method: "DELETE",
  });

// Statements
export const getTrialBalance = (companyId: number, periodId: number) =>
  apiFetch<TrialBalanceLine[]>(
    `/api/companies/${companyId}/periods/${periodId}/statements/trial-balance`
  );
export const getProfitAndLoss = (companyId: number, periodId: number) =>
  apiFetch<ProfitAndLoss>(
    `/api/companies/${companyId}/periods/${periodId}/statements/profit-and-loss`
  );
export const getBalanceSheet = (companyId: number, periodId: number) =>
  apiFetch<BalanceSheet>(
    `/api/companies/${companyId}/periods/${periodId}/statements/balance-sheet`
  );
export const getTaxComputation = (companyId: number, periodId: number) =>
  apiFetch<TaxComputation>(
    `/api/companies/${companyId}/periods/${periodId}/revenue/tax-computation`
  );

// === Member Audit Notice (s.334) ===
export const saveMemberAuditNotice = (
  companyId: number,
  periodId: number,
  data: { received: boolean; noticeDate?: string },
) =>
  apiFetch<{ memberAuditNoticeReceived: boolean; memberAuditNoticeDate?: string }>(
    `/api/companies/${companyId}/periods/${periodId}/member-audit-notice`,
    { method: "PUT", body: JSON.stringify(data) },
  );

// === Filing Deadlines ===
export interface FilingDeadline {
  id: number;
  companyId: number;
  periodId: number;
  deadlineType: string;
  dueDate: string;
  filedDate?: string;
  filingReference?: string;
  isLate: boolean;
  penaltyAmount: number;
  notes?: string;
}

export interface AuditExemptionJeopardy {
  lateFilingCount: number;
  isAtRisk: boolean;
  hasLostExemption: boolean;
  warning?: string;
}

export const getDeadlines = (companyId: number) =>
  apiFetch<FilingDeadline[]>(`/api/companies/${companyId}/deadlines`);

export const getUpcomingDeadline = (companyId: number) =>
  apiFetch<FilingDeadline | { message: string }>(`/api/companies/${companyId}/deadlines/upcoming`);

export const calculateDeadlines = (companyId: number, periodId: number) =>
  apiFetch<FilingDeadline[]>(
    `/api/companies/${companyId}/periods/${periodId}/deadlines/calculate`,
    { method: "POST" },
  );

export const markFiled = (
  companyId: number,
  periodId: number,
  data: { deadlineType: string; filedDate: string; filingReference?: string },
) =>
  apiFetch<FilingDeadline>(
    `/api/companies/${companyId}/periods/${periodId}/mark-filed`,
    { method: "POST", body: JSON.stringify(data) },
  );

export const getAuditExemptionJeopardy = (companyId: number) =>
  apiFetch<AuditExemptionJeopardy>(`/api/companies/${companyId}/deadlines/jeopardy`);

// === Director Loan Compliance ===
export interface DirectorLoanCompliance {
  totalDirectorLoans: number;
  netAssets: number;
  thresholdAmount: number;
  exceedsThreshold: boolean;
  sapRequired: boolean;
  statutoryInterestDue: number;
  loans: DirectorLoanDetail[];
  warning?: string;
}

export interface DirectorLoanDetail {
  id: number;
  directorName: string;
  openingBalance: number;
  maxDuringYear: number;
  closingBalance: number;
  interestCharged: number;
  isDocumented: boolean;
  exceedsThreshold: boolean;
}

export const getDirectorLoanCompliance = (companyId: number, periodId: number) =>
  apiFetch<DirectorLoanCompliance>(
    `/api/companies/${companyId}/periods/${periodId}/director-loans/compliance`
  );

export const getSection307Note = (companyId: number, periodId: number) =>
  apiFetch<{ note: string }>(
    `/api/companies/${companyId}/periods/${periodId}/director-loans/section-307-note`
  );

// === Dual Filing Packs ===
export const getAgmPackUrl = (companyId: number, periodId: number) =>
  `${API_BASE}/api/companies/${companyId}/periods/${periodId}/documents/agm-pack`;
export const getCroFilingPackUrl = (companyId: number, periodId: number) =>
  `${API_BASE}/api/companies/${companyId}/periods/${periodId}/documents/cro-filing-pack`;
export const getSignaturePageUrl = (companyId: number, periodId: number) =>
  `${API_BASE}/api/companies/${companyId}/periods/${periodId}/documents/signature-page`;

// === Phase 2: Interrogation Engine ===

export interface PostBalanceSheetEvent {
  id?: number;
  periodId?: number;
  description: string;
  eventDate: string;
  isAdjusting: boolean;
  financialImpact?: number;
  actionRequired?: string;
}

export interface RelatedPartyTransaction {
  id?: number;
  periodId?: number;
  partyName: string;
  relationship: string;
  transactionType: string;
  amount: number;
  balanceOwed?: number;
  terms?: string;
}

export interface ContingentLiability {
  id?: number;
  periodId?: number;
  description: string;
  nature: string;
  estimatedAmount?: number;
  likelihood: string;
}

export const getPostBalanceSheetEvents = (companyId: number, periodId: number) =>
  apiFetch<PostBalanceSheetEvent[]>(`/api/companies/${companyId}/periods/${periodId}/post-balance-sheet-events`);
export const createPostBalanceSheetEvent = (companyId: number, periodId: number, data: PostBalanceSheetEvent) =>
  apiFetch<PostBalanceSheetEvent>(`/api/companies/${companyId}/periods/${periodId}/post-balance-sheet-events`, { method: "POST", body: JSON.stringify(data) });
export const deletePostBalanceSheetEvent = (companyId: number, periodId: number, id: number) =>
  apiFetch<void>(`/api/companies/${companyId}/periods/${periodId}/post-balance-sheet-events/${id}`, { method: "DELETE" });

export const getRelatedPartyTransactions = (companyId: number, periodId: number) =>
  apiFetch<RelatedPartyTransaction[]>(`/api/companies/${companyId}/periods/${periodId}/related-party-transactions`);
export const createRelatedPartyTransaction = (companyId: number, periodId: number, data: RelatedPartyTransaction) =>
  apiFetch<RelatedPartyTransaction>(`/api/companies/${companyId}/periods/${periodId}/related-party-transactions`, { method: "POST", body: JSON.stringify(data) });
export const deleteRelatedPartyTransaction = (companyId: number, periodId: number, id: number) =>
  apiFetch<void>(`/api/companies/${companyId}/periods/${periodId}/related-party-transactions/${id}`, { method: "DELETE" });

export const getContingentLiabilities = (companyId: number, periodId: number) =>
  apiFetch<ContingentLiability[]>(`/api/companies/${companyId}/periods/${periodId}/contingent-liabilities`);
export const createContingentLiability = (companyId: number, periodId: number, data: ContingentLiability) =>
  apiFetch<ContingentLiability>(`/api/companies/${companyId}/periods/${periodId}/contingent-liabilities`, { method: "POST", body: JSON.stringify(data) });
export const deleteContingentLiability = (companyId: number, periodId: number, id: number) =>
  apiFetch<void>(`/api/companies/${companyId}/periods/${periodId}/contingent-liabilities/${id}`, { method: "DELETE" });

export const getGoingConcern = (companyId: number, periodId: number) =>
  apiFetch<{ goingConcernConfirmed: boolean; goingConcernNote?: string }>(`/api/companies/${companyId}/periods/${periodId}/going-concern`);
export const saveGoingConcern = (companyId: number, periodId: number, data: { confirmed: boolean; note?: string }) =>
  apiFetch<{ goingConcernConfirmed: boolean; goingConcernNote?: string }>(`/api/companies/${companyId}/periods/${periodId}/going-concern`, { method: "PUT", body: JSON.stringify(data) });

// === Phase 3: Cash Flow & Equity Changes ===

export interface CashFlowStatement {
  operatingProfit: number;
  operatingAdjustments: { description: string; amount: number }[];
  cashFromOperations: number;
  taxPaid: number;
  netCashFromOperating: number;
  capitalExpenditurePurchases: number;
  capitalExpenditureDisposals: number;
  netCashFromInvesting: number;
  loanRepayments: number;
  loanDrawdowns: number;
  dividendsPaid: number;
  netCashFromFinancing: number;
  netIncreaseInCash: number;
  openingCash: number;
  closingCash: number;
}

export interface EquityChanges {
  openingShareCapital: number;
  openingRetainedEarnings: number;
  openingTotal: number;
  profitForYear: number;
  dividendsPaid: number;
  sharesIssued: number;
  closingShareCapital: number;
  closingRetainedEarnings: number;
  closingTotal: number;
}

export const getCashFlowStatement = (companyId: number, periodId: number) =>
  apiFetch<CashFlowStatement>(`/api/companies/${companyId}/periods/${periodId}/statements/cash-flow`);
export const getEquityChanges = (companyId: number, periodId: number) =>
  apiFetch<EquityChanges>(`/api/companies/${companyId}/periods/${periodId}/statements/equity-changes`);

// === Phase 4: Directors' Report ===

export interface DirectorsReportData {
  companyName: string;
  periodStart: string;
  periodEnd: string;
  directorNames: string[];
  secretaryName?: string;
  principalActivities: string;
  resultsAndDividends: string;
  accountingRecordsStatement: string;
  postBalanceSheetEvents?: string;
  goingConcernStatement?: string;
  auditInformationStatement?: string;
  isMicroExempt: boolean;
  isSmallExemptFromBusinessReview: boolean;
  electedRegime: string;
}

export const getDirectorsReportData = (companyId: number, periodId: number) =>
  apiFetch<DirectorsReportData>(`/api/companies/${companyId}/periods/${periodId}/documents/directors-report-data`);

// === Phase 5: Charity / SORP ===

export interface CharityInfo {
  id?: number;
  companyId?: number;
  charityNumber?: string;
  charityType?: string;
  grossIncome: number;
  sorpTier: number;
  charitableObjectives?: string;
  principalActivities?: string;
  governanceCodeCompliant: boolean;
  governanceCodeNote?: string;
  hasInternationalTransfers: boolean;
  internationalTransferDetails?: string;
  trusteeRemunerationPaid: boolean;
  trusteeRemunerationAmount: number;
  trusteeExpensesDetails?: string;
}

export interface FundBalance {
  id?: number;
  periodId?: number;
  fundName: string;
  fundType: string;
  openingBalance: number;
  incomingResources: number;
  resourcesExpended: number;
  transfers: number;
  gainsLosses: number;
  closingBalance: number;
  notes?: string;
}

export interface SofaData {
  unrestrictedFunds: FundLine[];
  restrictedFunds: FundLine[];
  endowmentFunds: FundLine[];
  totalIncoming: number;
  totalExpended: number;
  totalTransfers: number;
  totalGainsLosses: number;
  netMovement: number;
  totalOpeningFunds: number;
  totalClosingFunds: number;
}

export interface FundLine {
  fundName: string;
  fundType: string;
  openingBalance: number;
  incomingResources: number;
  resourcesExpended: number;
  transfers: number;
  gainsLosses: number;
  closingBalance: number;
}

export interface TrusteesReportData {
  charityName: string;
  charityNumber: string;
  croNumber: string;
  periodStart: string;
  periodEnd: string;
  trusteeNames: string[];
  charitableObjectives: string;
  principalActivities: string;
  totalIncome: number;
  totalExpenditure: number;
  netMovement: number;
  closingFunds: number;
  governanceCodeCompliant: boolean;
  governanceCodeNote?: string;
  trusteeRemunerationPaid: boolean;
  trusteeRemunerationAmount: number;
  trusteeExpensesDetails?: string;
  hasInternationalTransfers: boolean;
  internationalTransferDetails?: string;
  sorpTier: number;
  filingDeadline: string;
}

export const getCharityInfo = (companyId: number) =>
  apiFetch<CharityInfo>(`/api/companies/${companyId}/charity/info`);
export const saveCharityInfo = (companyId: number, data: CharityInfo) =>
  apiFetch<CharityInfo>(`/api/companies/${companyId}/charity/info`, { method: "PUT", body: JSON.stringify(data) });

export const getSofa = (companyId: number, periodId: number) =>
  apiFetch<SofaData>(`/api/companies/${companyId}/periods/${periodId}/charity/sofa`);
export const getTrusteesReport = (companyId: number, periodId: number) =>
  apiFetch<TrusteesReportData>(`/api/companies/${companyId}/periods/${periodId}/charity/trustees-report`);

export const getFundBalances = (companyId: number, periodId: number) =>
  apiFetch<FundBalance[]>(`/api/companies/${companyId}/periods/${periodId}/charity/funds`);
export const createFundBalance = (companyId: number, periodId: number, data: FundBalance) =>
  apiFetch<FundBalance>(`/api/companies/${companyId}/periods/${periodId}/charity/funds`, { method: "POST", body: JSON.stringify(data) });
export const updateFundBalance = (companyId: number, periodId: number, id: number, data: FundBalance) =>
  apiFetch<FundBalance>(`/api/companies/${companyId}/periods/${periodId}/charity/funds/${id}`, { method: "PUT", body: JSON.stringify(data) });
export const deleteFundBalance = (companyId: number, periodId: number, id: number) =>
  apiFetch<void>(`/api/companies/${companyId}/periods/${periodId}/charity/funds/${id}`, { method: "DELETE" });

// === Phase 6: Filing Workflow ===

export interface FilingWorkflowStatus {
  cro: CroFilingStatus;
  revenue: RevenueFilingStatus;
  charity: CharityFilingStatus;
  blockingIssues: string[];
  warningIssues: string[];
  readyToFile: boolean;
}

export interface LegalSourceReference {
  sourceId: string;
  title: string;
  effectiveDate: string;
  url: string;
}

export interface SourceLawSnapshot {
  snapshotDate: string;
  snapshotVersion: string;
  contentHash: string;
  sourceCount: number;
  sources: LegalSourceReference[];
}

export interface SourceLawTraceabilityEntry {
  sourceId: string;
  title: string;
  effectiveDate: string;
  url: string;
  inSnapshot: boolean;
  usedBy: string[];
  releaseGateCodes: string[];
}

export interface SourceLawMaintenanceProtocol {
  protocolVersion: string;
  ownerRole: string;
  status: string;
  reviewCadence: string;
  nextReviewDue: string;
  signOffGate: string;
  changeDetection: string;
  failurePolicy: string;
  monitoredSourceIds: string[];
  acceptanceCriteria: string[];
  requiredEvidence: string[];
}

export interface SourceLawReviewLedgerEntry {
  sourceId: string;
  title: string;
  url: string;
  pinnedEffectiveDate: string;
  ownerRole: string;
  releaseChecklistCode: string;
  blocksRelease: boolean;
  reviewChecks: string[];
  requiredEvidence: string[];
}

export interface ProductionReadinessArea {
  code: string;
  label: string;
  status: string;
  detail: string;
}

export interface GoldenFilingCorpusEvidencePack {
  outputArtifacts: string[];
  decisionGates: string[];
  expectedValueChecks: string[];
  expectedOutputs: GoldenFilingCorpusExpectedOutputs;
  expectedProofPoints: GoldenFilingCorpusProofPoint[];
  sourceReferences: LegalSourceReference[];
}

export interface GoldenFilingCorpusExpectedOutputs {
  pdfTextMarkers: string[];
  ixbrlRequiredTags: string[];
  filingReadinessState: string;
  expectedCorporationTax: number;
  requiredNotes: string[];
  filingGateStates: string[];
  signOffPacketState: string;
}

export interface GoldenFilingCorpusProofPoint {
  area: string;
  expectedEvidence: string;
  automatedVerifier: string;
  required: boolean;
}

export interface GoldenFilingCorpusFixture {
  legalName: string;
  companyType: string;
  periodStart: string;
  periodEnd: string;
  expectedSizeClass: string;
  expectedRegime: string;
  auditExempt: boolean;
  manualProfessionalReviewRequired: boolean;
}

export interface GoldenFilingCorpusVerifier {
  name: string;
  command: string;
  ciScope: string;
  runsInDefaultCi: boolean;
  environment: string;
  evidenceLevel: string;
}

export interface GoldenFilingCorpusLegalBasisSnapshot {
  scenarioCode: string;
  companyType: string;
  sizeClass: string;
  electedRegime: string;
  auditExempt: boolean;
  manualProfessionalReviewRequired: boolean;
  legalBasis: string;
  requiredOutputs: string[];
  professionalGates: string[];
  sourceIds: string[];
}

export interface GoldenFilingCorpusScenario {
  code: string;
  label: string;
  companyScope: string;
  expectedOutcome: string;
  coverageStatus: string;
  fixture: GoldenFilingCorpusFixture;
  evidenceTestNames: string[];
  evidenceVerifiers: GoldenFilingCorpusVerifier[];
  assertions: string[];
  evidencePack: GoldenFilingCorpusEvidencePack;
  legalBasisSnapshot: GoldenFilingCorpusLegalBasisSnapshot;
}

export interface GoldenEvidenceLedgerEntry {
  scenarioCode: string;
  label: string;
  fixtureLegalName: string;
  companyType: string;
  expectedOutcome: string;
  coverageStatus: string;
  acceptanceStatus: string;
  requiredSignOffGate: string;
  blocksRelease: boolean;
  automatedVerifierNames: string[];
  automatedVerifierCommands: string[];
  ciScopes: string[];
  evidenceLevels: string[];
  outputArtifacts: string[];
  decisionGates: string[];
  expectedValueChecks: string[];
  proofPointAreas: string[];
  sourceIds: string[];
  expectedCorporationTax: number;
  filingReadinessState: string;
  signOffPacketState: string;
}

export interface StatutoryRuleMatrixEntry {
  code: string;
  companyScope: string;
  sizeOrRegime: string;
  supportLevel: string;
  requiredEvidence: string[];
  requiredOutputs: string[];
  manualHandoffGates: string[];
  sources: LegalSourceReference[];
}

export interface StatutoryRulesCoverageItem {
  code: string;
  ruleFamily: string;
  decisionUnderTest: string;
  coverageStatus: string;
  automatedVerifierNames: string[];
  edgeCases: string[];
  sources: LegalSourceReference[];
}

export interface OperationalGate {
  code: string;
  label: string;
  required: boolean;
  status: string;
  detail: string;
}

export interface ProductionReadinessAssuranceAction {
  code: string;
  label: string;
  owner: string;
  priority: string;
  riskRank: number;
  evidenceStage: string;
  status: string;
  detail: string;
  evidenceRequired: string;
}

export interface ProductionReadinessCompletionTrack {
  code: string;
  label: string;
  ownerRole: string;
  status: string;
  completionCriteria: string[];
  currentEvidence: string[];
  nextActions: string[];
  assuranceActionCodes: string[];
}

export interface ProductionReleaseBlocker {
  code: string;
  trackCode: string;
  trackLabel: string;
  ownerRole: string;
  severity: string;
  riskRank: number;
  blockingIssue: string;
  requiredEvidence: string;
  nextAction: string;
  sourceActionCode: string;
  releaseChecklistCode: string;
  operationalGateCode: string;
  evidenceArtifact: string;
  blocksRelease: boolean;
}

export interface ProductionAuditabilityControl {
  code: string;
  label: string;
  required: boolean;
  enforcement: string;
  evidenceCaptured: string;
  verification: string;
  auditEventCodes: string[];
}

export interface AuditEvidenceTimelineEntry {
  code: string;
  stage: string;
  evidenceQuestion: string;
  capturedWhen: string;
  requiredActor: string;
  verification: string;
  auditEventCodes: string[];
  blockingGateCodes: string[];
}

export interface ProductionAuditEvidencePackItem {
  code: string;
  label: string;
  evidenceQuestion: string;
  requiredArtifact: string;
  retainedIn: string;
  requiredActor: string;
  capturedWhen: string;
  verification: string;
  failurePolicy: string;
  auditEventCodes: string[];
  blockingGateCodes: string[];
}

export interface ProductionMonitoringControl {
  code: string;
  label: string;
  provider: string;
  required: boolean;
  productionSafetyGate: string;
  evidenceCaptured: string;
  verification: string;
  alertRoute: string;
  failurePolicy: string;
}

export interface DependencyPolicyControl {
  code: string;
  label: string;
  required: boolean;
  enforcement: string;
  evidenceCaptured: string;
  verification: string;
  failurePolicy: string;
}

export interface DeploymentSafetyControl {
  code: string;
  label: string;
  required: boolean;
  enforcement: string;
  evidenceCaptured: string;
  verification: string;
  failurePolicy: string;
}

export interface OperationsEvidencePackItem {
  code: string;
  label: string;
  category: string;
  ownerRole: string;
  required: boolean;
  command: string;
  requiredArtifact: string;
  releaseGateCode: string;
  verification: string;
  failurePolicy: string;
}

export interface ReleaseReviewChecklistItem {
  code: string;
  label: string;
  ownerRole: string;
  required: boolean;
  status: string;
  blocksRelease: boolean;
  evidenceArtifact: string;
  assuranceActionCode: string;
  operationalGateCode: string;
  auditEventCodes: string[];
  detail: string;
}

export interface ReleaseVerificationManifestItem {
  code: string;
  label: string;
  ownerRole: string;
  command: string;
  ciScope: string;
  runsInDefaultCi: boolean;
  blocksRelease: boolean;
  evidenceArtifact: string;
  releaseChecklistEvidenceArtifact: string;
  manualFallback: string;
}

export interface AccountantAcceptanceCriterion {
  scenarioCode: string;
  label: string;
  required: boolean;
  acceptanceStatus: string;
  reviewScope: string[];
  requiredEvidence: string[];
  requiredSignOffGate: string;
  evidenceVerifiers: GoldenFilingCorpusVerifier[];
  sources: LegalSourceReference[];
}

export interface AccountantAcceptanceSummary {
  scenarioCount: number;
  automatedVerifierCount: number;
  professionalSignOffRequiredCount: number;
  manualHandoffScenarioCount: number;
  releaseBlockingScenarioCodes: string[];
  requiredSignOffGates: string[];
  status: string;
}

export interface AccountantWorkflowWalkthroughProtocol {
  protocolVersion: string;
  reviewerRole: string;
  status: string;
  signOffGate: string;
  failurePolicy: string;
  seededScenarioCodes: string[];
  routeSequence: string[];
  acceptanceCriteria: string[];
  requiredEvidence: string[];
}

export interface AccountantJourneyAcceptanceChecklistItem {
  routeCode: string;
  routeLabel: string;
  routeKey: string;
  workflowStages: string[];
  seededScenarioCodes: string[];
  visualArtifactNames: string[];
  requiredEvidence: string[];
  acceptanceCriteria: string[];
  signOffGate: string;
  status: string;
}

export interface AccountantWorkflowEvidencePackItem {
  routeCode: string;
  routeLabel: string;
  workflowStages: string[];
  seededScenarioCodes: string[];
  visualArtifactNames: string[];
  evidenceArtifact: string;
  decisionQuestion: string;
  requiredEvidence: string[];
  signOffGate: string;
  failurePolicy: string;
}

export interface VisualQaViewport {
  name: string;
  width: number;
  height: number;
}

export interface VisualQaRoute {
  code: string;
  routeKey: string;
  label: string;
  description: string;
  requiredText: string;
  workflowStages: string[];
  openFilingTab: boolean;
}

export interface VisualQaArtifact {
  routeCode: string;
  routeKey: string;
  theme: string;
  viewportName: string;
  fileName: string;
  artifactPath: string;
  requiredText: string;
  openFilingTab: boolean;
  reviewStatus: string;
  layoutChecks: string[];
}

export interface VisualQaRouteAudit {
  routeCode: string;
  routeKey: string;
  label: string;
  workflowStages: string[];
  screenshotCount: number;
  reviewStatus: string;
  reviewChecks: string[];
}

export interface VisualQaReviewProtocol {
  protocolVersion: string;
  reviewerRole: string;
  status: string;
  signOffGate: string;
  failurePolicy: string;
  acceptanceCriteria: string[];
  requiredEvidence: string[];
}

export interface VisualQaCoverage {
  artifactName: string;
  enforcement: string;
  manifestFileName: string;
  expectedScreenshotCount: number;
  layoutChecks: string[];
  reviewChecks: string[];
  reviewProtocol: VisualQaReviewProtocol;
  themes: string[];
  viewports: VisualQaViewport[];
  routes: VisualQaRoute[];
  routeAudits: VisualQaRouteAudit[];
  artifacts: VisualQaArtifact[];
}

export interface ProductionAssurancePacket {
  packetId: string;
  packetVersion: string;
  status: string;
  sourceLawSnapshotHash: string;
  goldenCorpusCovered: number;
  goldenCorpusTotal: number;
  statutoryRuleMatrixPaths: number;
  statutoryRuleCoverageFamilies: number;
  visualQaExpectedScreenshots: number;
  requiredOperationalGates: number;
  openCriticalActions: number;
  evidenceItems: string[];
  releaseBlockers: string[];
}

export interface RevenueTaxonomyRangeEvidence {
  taxonomyKey: string;
  accountingStandard: string;
  taxonomyDate: string;
  label: string;
  schemaRef: string;
  acceptedByRevenue: boolean;
  automatedPlatformSelectionSupported: boolean;
  effectiveForPeriodsStartingOnOrAfter: string;
  effectiveForPeriodsStartingBefore: string;
  sourceIds: string[];
  releaseGateCodes: string[];
}

export interface ProductionReadinessReport {
  generatedAt: string;
  overallStatus: string;
  companiesInDatabase: number;
  periodsInDatabase: number;
  sourceLawSnapshot: SourceLawSnapshot;
  sourceLawTraceability: SourceLawTraceabilityEntry[];
  sourceLawMaintenanceProtocol: SourceLawMaintenanceProtocol;
  sourceLawReviewLedger: SourceLawReviewLedgerEntry[];
  revenueTaxonomyRanges: RevenueTaxonomyRangeEvidence[];
  assurancePacket: ProductionAssurancePacket;
  accountantAcceptanceCriteria: AccountantAcceptanceCriterion[];
  accountantAcceptanceSummary: AccountantAcceptanceSummary;
  accountantWorkflowWalkthroughProtocol: AccountantWorkflowWalkthroughProtocol;
  accountantJourneyAcceptanceChecklist: AccountantJourneyAcceptanceChecklistItem[];
  accountantWorkflowEvidencePack: AccountantWorkflowEvidencePackItem[];
  areas: ProductionReadinessArea[];
  goldenFilingCorpus: GoldenFilingCorpusScenario[];
  goldenEvidenceLedger: GoldenEvidenceLedgerEntry[];
  statutoryRuleMatrix: StatutoryRuleMatrixEntry[];
  statutoryRulesCoverage: StatutoryRulesCoverageItem[];
  manualHandoffPaths: string[];
  operationalGates: OperationalGate[];
  assuranceActions: ProductionReadinessAssuranceAction[];
  completionTracks: ProductionReadinessCompletionTrack[];
  releaseBlockerRegister: ProductionReleaseBlocker[];
  auditabilityControls: ProductionAuditabilityControl[];
  auditEvidenceTimeline: AuditEvidenceTimelineEntry[];
  auditEvidencePack: ProductionAuditEvidencePackItem[];
  monitoringControls: ProductionMonitoringControl[];
  dependencyPolicyControls: DependencyPolicyControl[];
  deploymentSafetyControls: DeploymentSafetyControl[];
  operationsEvidencePack: OperationsEvidencePackItem[];
  releaseReviewChecklist: ReleaseReviewChecklistItem[];
  releaseVerificationManifest: ReleaseVerificationManifestItem[];
  visualQaCoverage: VisualQaCoverage;
}

const legalSourceReferenceSchema = z.object({
  sourceId: z.string().min(1),
  title: z.string().min(1),
  effectiveDate: z.string().min(1),
  url: z.string().url(),
});

const sourceLawSnapshotSchema = z.object({
  snapshotDate: z.string().min(1),
  snapshotVersion: z.string().min(1),
  contentHash: z.string().regex(/^sha256:[0-9a-f]{64}$/),
  sourceCount: z.number().int().nonnegative(),
  sources: z.array(legalSourceReferenceSchema),
});

const sourceLawTraceabilityEntrySchema = z.object({
  sourceId: z.string().min(1),
  title: z.string().min(1),
  effectiveDate: z.string().min(1),
  url: z.string().url(),
  inSnapshot: z.boolean(),
  usedBy: z.array(z.string().min(1)),
  releaseGateCodes: z.array(z.string().min(1)),
});

const sourceLawMaintenanceProtocolSchema = z.object({
  protocolVersion: z.string().min(1),
  ownerRole: z.string().min(1),
  status: z.string().min(1),
  reviewCadence: z.string().min(1),
  nextReviewDue: z.string().min(1),
  signOffGate: z.string().min(1),
  changeDetection: z.string().min(1),
  failurePolicy: z.string().min(1),
  monitoredSourceIds: z.array(z.string().min(1)),
  acceptanceCriteria: z.array(z.string().min(1)),
  requiredEvidence: z.array(z.string().min(1)),
});

const sourceLawReviewLedgerEntrySchema = z.object({
  sourceId: z.string().min(1),
  title: z.string().min(1),
  url: z.string().url(),
  pinnedEffectiveDate: z.string().min(1),
  ownerRole: z.string().min(1),
  releaseChecklistCode: z.string().min(1),
  blocksRelease: z.boolean(),
  reviewChecks: z.array(z.string().min(1)),
  requiredEvidence: z.array(z.string().min(1)),
});

const revenueTaxonomyRangeEvidenceSchema = z.object({
  taxonomyKey: z.string().min(1),
  accountingStandard: z.string().min(1),
  taxonomyDate: z.string().min(1),
  label: z.string().min(1),
  schemaRef: z.string().url(),
  acceptedByRevenue: z.boolean(),
  automatedPlatformSelectionSupported: z.boolean(),
  effectiveForPeriodsStartingOnOrAfter: z.string().min(1),
  effectiveForPeriodsStartingBefore: z.string(),
  sourceIds: z.array(z.string().min(1)),
  releaseGateCodes: z.array(z.string().min(1)),
});

const productionAssurancePacketSchema = z.object({
  packetId: z.string().regex(/^assurance-sha256:[0-9a-f]{64}$/),
  packetVersion: z.string().min(1),
  status: z.string().min(1),
  sourceLawSnapshotHash: z.string().regex(/^sha256:[0-9a-f]{64}$/),
  goldenCorpusCovered: z.number().int().nonnegative(),
  goldenCorpusTotal: z.number().int().nonnegative(),
  statutoryRuleMatrixPaths: z.number().int().nonnegative(),
  statutoryRuleCoverageFamilies: z.number().int().nonnegative(),
  visualQaExpectedScreenshots: z.number().int().nonnegative(),
  requiredOperationalGates: z.number().int().nonnegative(),
  openCriticalActions: z.number().int().nonnegative(),
  evidenceItems: z.array(z.string().min(1)),
  releaseBlockers: z.array(z.string().min(1)),
});

const productionReadinessAreaSchema = z.object({
  code: z.string().min(1),
  label: z.string().min(1),
  status: z.string().min(1),
  detail: z.string().min(1),
});

const goldenFilingCorpusEvidencePackSchema = z.object({
  outputArtifacts: z.array(z.string().min(1)),
  decisionGates: z.array(z.string().min(1)),
  expectedValueChecks: z.array(z.string().min(1)),
  expectedOutputs: z.object({
    pdfTextMarkers: z.array(z.string().min(1)),
    ixbrlRequiredTags: z.array(z.string().min(1)),
    filingReadinessState: z.string().min(1),
    expectedCorporationTax: z.number().nonnegative(),
    requiredNotes: z.array(z.string().min(1)),
    filingGateStates: z.array(z.string().min(1)),
    signOffPacketState: z.string().min(1),
  }),
  expectedProofPoints: z.array(z.object({
    area: z.string().min(1),
    expectedEvidence: z.string().min(1),
    automatedVerifier: z.string().min(1),
    required: z.boolean(),
  })),
  sourceReferences: z.array(legalSourceReferenceSchema),
});

const goldenFilingCorpusFixtureSchema = z.object({
  legalName: z.string().min(1),
  companyType: z.string().min(1),
  periodStart: z.string().min(1),
  periodEnd: z.string().min(1),
  expectedSizeClass: z.string().min(1),
  expectedRegime: z.string().min(1),
  auditExempt: z.boolean(),
  manualProfessionalReviewRequired: z.boolean(),
});

const goldenFilingCorpusVerifierSchema = z.object({
  name: z.string().min(1),
  command: z.string().min(1),
  ciScope: z.string().min(1),
  runsInDefaultCi: z.boolean(),
  environment: z.string().min(1),
  evidenceLevel: z.string().min(1),
});

const goldenFilingCorpusLegalBasisSnapshotSchema = z.object({
  scenarioCode: z.string().min(1),
  companyType: z.string().min(1),
  sizeClass: z.string().min(1),
  electedRegime: z.string().min(1),
  auditExempt: z.boolean(),
  manualProfessionalReviewRequired: z.boolean(),
  legalBasis: z.string().min(1),
  requiredOutputs: z.array(z.string().min(1)),
  professionalGates: z.array(z.string().min(1)),
  sourceIds: z.array(z.string().min(1)),
});

const goldenFilingCorpusScenarioSchema = z.object({
  code: z.string().min(1),
  label: z.string().min(1),
  companyScope: z.string().min(1),
  expectedOutcome: z.string().min(1),
  coverageStatus: z.string().min(1),
  fixture: goldenFilingCorpusFixtureSchema,
  evidenceTestNames: z.array(z.string().min(1)),
  evidenceVerifiers: z.array(goldenFilingCorpusVerifierSchema),
  assertions: z.array(z.string().min(1)),
  evidencePack: goldenFilingCorpusEvidencePackSchema,
  legalBasisSnapshot: goldenFilingCorpusLegalBasisSnapshotSchema,
});

const goldenEvidenceLedgerEntrySchema = z.object({
  scenarioCode: z.string().min(1),
  label: z.string().min(1),
  fixtureLegalName: z.string().min(1),
  companyType: z.string().min(1),
  expectedOutcome: z.string().min(1),
  coverageStatus: z.string().min(1),
  acceptanceStatus: z.string().min(1),
  requiredSignOffGate: z.string().min(1),
  blocksRelease: z.boolean(),
  automatedVerifierNames: z.array(z.string().min(1)),
  automatedVerifierCommands: z.array(z.string().min(1)),
  ciScopes: z.array(z.string().min(1)),
  evidenceLevels: z.array(z.string().min(1)),
  outputArtifacts: z.array(z.string().min(1)),
  decisionGates: z.array(z.string().min(1)),
  expectedValueChecks: z.array(z.string().min(1)),
  proofPointAreas: z.array(z.string().min(1)),
  sourceIds: z.array(z.string().min(1)),
  expectedCorporationTax: z.number().nonnegative(),
  filingReadinessState: z.string().min(1),
  signOffPacketState: z.string().min(1),
});

const statutoryRuleMatrixEntrySchema = z.object({
  code: z.string().min(1),
  companyScope: z.string().min(1),
  sizeOrRegime: z.string().min(1),
  supportLevel: z.string().min(1),
  requiredEvidence: z.array(z.string().min(1)),
  requiredOutputs: z.array(z.string().min(1)),
  manualHandoffGates: z.array(z.string().min(1)),
  sources: z.array(legalSourceReferenceSchema),
});

const statutoryRulesCoverageItemSchema = z.object({
  code: z.string().min(1),
  ruleFamily: z.string().min(1),
  decisionUnderTest: z.string().min(1),
  coverageStatus: z.string().min(1),
  automatedVerifierNames: z.array(z.string().min(1)),
  edgeCases: z.array(z.string().min(1)),
  sources: z.array(legalSourceReferenceSchema),
});

const operationalGateSchema = z.object({
  code: z.string().min(1),
  label: z.string().min(1),
  required: z.boolean(),
  status: z.string().min(1),
  detail: z.string().min(1),
});

const productionReadinessAssuranceActionSchema = z.object({
  code: z.string().min(1),
  label: z.string().min(1),
  owner: z.string().min(1),
  priority: z.string().min(1),
  riskRank: z.number().int().nonnegative(),
  evidenceStage: z.string().min(1),
  status: z.string().min(1),
  detail: z.string().min(1),
  evidenceRequired: z.string().min(1),
});

const productionReadinessCompletionTrackSchema = z.object({
  code: z.string().min(1),
  label: z.string().min(1),
  ownerRole: z.string().min(1),
  status: z.string().min(1),
  completionCriteria: z.array(z.string().min(1)),
  currentEvidence: z.array(z.string().min(1)),
  nextActions: z.array(z.string().min(1)),
  assuranceActionCodes: z.array(z.string().min(1)),
});

const productionReleaseBlockerSchema = z.object({
  code: z.string().min(1),
  trackCode: z.string().min(1),
  trackLabel: z.string().min(1),
  ownerRole: z.string().min(1),
  severity: z.string().min(1),
  riskRank: z.number().int().nonnegative(),
  blockingIssue: z.string().min(1),
  requiredEvidence: z.string().min(1),
  nextAction: z.string().min(1),
  sourceActionCode: z.string().min(1),
  releaseChecklistCode: z.string().min(1),
  operationalGateCode: z.string(),
  evidenceArtifact: z.string().min(1),
  blocksRelease: z.boolean(),
});

const productionAuditabilityControlSchema = z.object({
  code: z.string().min(1),
  label: z.string().min(1),
  required: z.boolean(),
  enforcement: z.string().min(1),
  evidenceCaptured: z.string().min(1),
  verification: z.string().min(1),
  auditEventCodes: z.array(z.string().min(1)),
});

const auditEvidenceTimelineEntrySchema = z.object({
  code: z.string().min(1),
  stage: z.string().min(1),
  evidenceQuestion: z.string().min(1),
  capturedWhen: z.string().min(1),
  requiredActor: z.string().min(1),
  verification: z.string().min(1),
  auditEventCodes: z.array(z.string().min(1)),
  blockingGateCodes: z.array(z.string().min(1)),
});

const productionAuditEvidencePackItemSchema = z.object({
  code: z.string().min(1),
  label: z.string().min(1),
  evidenceQuestion: z.string().min(1),
  requiredArtifact: z.string().min(1),
  retainedIn: z.string().min(1),
  requiredActor: z.string().min(1),
  capturedWhen: z.string().min(1),
  verification: z.string().min(1),
  failurePolicy: z.string().min(1),
  auditEventCodes: z.array(z.string().min(1)),
  blockingGateCodes: z.array(z.string().min(1)),
});

const productionMonitoringControlSchema = z.object({
  code: z.string().min(1),
  label: z.string().min(1),
  provider: z.string().min(1),
  required: z.boolean(),
  productionSafetyGate: z.string().min(1),
  evidenceCaptured: z.string().min(1),
  verification: z.string().min(1),
  alertRoute: z.string().min(1),
  failurePolicy: z.string().min(1),
});

const dependencyPolicyControlSchema = z.object({
  code: z.string().min(1),
  label: z.string().min(1),
  required: z.boolean(),
  enforcement: z.string().min(1),
  evidenceCaptured: z.string().min(1),
  verification: z.string().min(1),
  failurePolicy: z.string().min(1),
});

const deploymentSafetyControlSchema = z.object({
  code: z.string().min(1),
  label: z.string().min(1),
  required: z.boolean(),
  enforcement: z.string().min(1),
  evidenceCaptured: z.string().min(1),
  verification: z.string().min(1),
  failurePolicy: z.string().min(1),
});

const operationsEvidencePackItemSchema = z.object({
  code: z.string().min(1),
  label: z.string().min(1),
  category: z.string().min(1),
  ownerRole: z.string().min(1),
  required: z.boolean(),
  command: z.string().min(1),
  requiredArtifact: z.string().min(1),
  releaseGateCode: z.string().min(1),
  verification: z.string().min(1),
  failurePolicy: z.string().min(1),
});

const releaseReviewChecklistItemSchema = z.object({
  code: z.string().min(1),
  label: z.string().min(1),
  ownerRole: z.string().min(1),
  required: z.boolean(),
  status: z.string().min(1),
  blocksRelease: z.boolean(),
  evidenceArtifact: z.string().min(1),
  assuranceActionCode: z.string().min(1),
  operationalGateCode: z.string(),
  auditEventCodes: z.array(z.string().min(1)),
  detail: z.string().min(1),
});

const releaseVerificationManifestItemSchema = z.object({
  code: z.string().min(1),
  label: z.string().min(1),
  ownerRole: z.string().min(1),
  command: z.string().min(1),
  ciScope: z.string().min(1),
  runsInDefaultCi: z.boolean(),
  blocksRelease: z.boolean(),
  evidenceArtifact: z.string().min(1),
  releaseChecklistEvidenceArtifact: z.string().min(1),
  manualFallback: z.string().min(1),
});

const accountantAcceptanceCriterionSchema = z.object({
  scenarioCode: z.string().min(1),
  label: z.string().min(1),
  required: z.boolean(),
  acceptanceStatus: z.string().min(1),
  reviewScope: z.array(z.string().min(1)),
  requiredEvidence: z.array(z.string().min(1)),
  requiredSignOffGate: z.string().min(1),
  evidenceVerifiers: z.array(goldenFilingCorpusVerifierSchema),
  sources: z.array(legalSourceReferenceSchema),
});

const accountantAcceptanceSummarySchema = z.object({
  scenarioCount: z.number().int().nonnegative(),
  automatedVerifierCount: z.number().int().nonnegative(),
  professionalSignOffRequiredCount: z.number().int().nonnegative(),
  manualHandoffScenarioCount: z.number().int().nonnegative(),
  releaseBlockingScenarioCodes: z.array(z.string().min(1)),
  requiredSignOffGates: z.array(z.string().min(1)),
  status: z.string().min(1),
});

const accountantWorkflowWalkthroughProtocolSchema = z.object({
  protocolVersion: z.string().min(1),
  reviewerRole: z.string().min(1),
  status: z.string().min(1),
  signOffGate: z.string().min(1),
  failurePolicy: z.string().min(1),
  seededScenarioCodes: z.array(z.string().min(1)),
  routeSequence: z.array(z.string().min(1)),
  acceptanceCriteria: z.array(z.string().min(1)),
  requiredEvidence: z.array(z.string().min(1)),
});

const accountantJourneyAcceptanceChecklistItemSchema = z.object({
  routeCode: z.string().min(1),
  routeLabel: z.string().min(1),
  routeKey: z.string().min(1),
  workflowStages: z.array(z.string().min(1)),
  seededScenarioCodes: z.array(z.string().min(1)),
  visualArtifactNames: z.array(z.string().min(1)),
  requiredEvidence: z.array(z.string().min(1)),
  acceptanceCriteria: z.array(z.string().min(1)),
  signOffGate: z.string().min(1),
  status: z.string().min(1),
});

const accountantWorkflowEvidencePackItemSchema = z.object({
  routeCode: z.string().min(1),
  routeLabel: z.string().min(1),
  workflowStages: z.array(z.string().min(1)),
  seededScenarioCodes: z.array(z.string().min(1)),
  visualArtifactNames: z.array(z.string().min(1)),
  evidenceArtifact: z.string().min(1),
  decisionQuestion: z.string().min(1),
  requiredEvidence: z.array(z.string().min(1)),
  signOffGate: z.string().min(1),
  failurePolicy: z.string().min(1),
});

const visualQaViewportSchema = z.object({
  name: z.string().min(1),
  width: z.number(),
  height: z.number(),
});

const visualQaRouteSchema = z.object({
  code: z.string().min(1),
  routeKey: z.string().min(1),
  label: z.string().min(1),
  description: z.string().min(1),
  requiredText: z.string().min(1),
  workflowStages: z.array(z.string().min(1)),
  openFilingTab: z.boolean(),
});

const visualQaArtifactSchema = z.object({
  routeCode: z.string().min(1),
  routeKey: z.string().min(1),
  theme: z.string().min(1),
  viewportName: z.string().min(1),
  fileName: z.string().min(1),
  artifactPath: z.string().min(1),
  requiredText: z.string().min(1),
  openFilingTab: z.boolean(),
  reviewStatus: z.string().min(1),
  layoutChecks: z.array(z.string().min(1)),
});

const visualQaRouteAuditSchema = z.object({
  routeCode: z.string().min(1),
  routeKey: z.string().min(1),
  label: z.string().min(1),
  workflowStages: z.array(z.string().min(1)),
  screenshotCount: z.number().int().nonnegative(),
  reviewStatus: z.string().min(1),
  reviewChecks: z.array(z.string().min(1)),
});

const visualQaReviewProtocolSchema = z.object({
  protocolVersion: z.string().min(1),
  reviewerRole: z.string().min(1),
  status: z.string().min(1),
  signOffGate: z.string().min(1),
  failurePolicy: z.string().min(1),
  acceptanceCriteria: z.array(z.string().min(1)),
  requiredEvidence: z.array(z.string().min(1)),
});

const visualQaCoverageSchema = z.object({
  artifactName: z.string().min(1),
  enforcement: z.string().min(1),
  manifestFileName: z.string().min(1),
  expectedScreenshotCount: z.number(),
  layoutChecks: z.array(z.string().min(1)),
  reviewChecks: z.array(z.string().min(1)),
  reviewProtocol: visualQaReviewProtocolSchema,
  themes: z.array(z.string().min(1)),
  viewports: z.array(visualQaViewportSchema),
  routes: z.array(visualQaRouteSchema),
  routeAudits: z.array(visualQaRouteAuditSchema),
  artifacts: z.array(visualQaArtifactSchema),
});

export const productionReadinessReportSchema = z.object({
  generatedAt: z.string().min(1),
  overallStatus: z.string().min(1),
  companiesInDatabase: z.number(),
  periodsInDatabase: z.number(),
  sourceLawSnapshot: sourceLawSnapshotSchema,
  sourceLawTraceability: z.array(sourceLawTraceabilityEntrySchema),
  sourceLawMaintenanceProtocol: sourceLawMaintenanceProtocolSchema,
  sourceLawReviewLedger: z.array(sourceLawReviewLedgerEntrySchema),
  revenueTaxonomyRanges: z.array(revenueTaxonomyRangeEvidenceSchema),
  assurancePacket: productionAssurancePacketSchema,
  accountantAcceptanceCriteria: z.array(accountantAcceptanceCriterionSchema),
  accountantAcceptanceSummary: accountantAcceptanceSummarySchema,
  accountantWorkflowWalkthroughProtocol: accountantWorkflowWalkthroughProtocolSchema,
  accountantJourneyAcceptanceChecklist: z.array(accountantJourneyAcceptanceChecklistItemSchema),
  accountantWorkflowEvidencePack: z.array(accountantWorkflowEvidencePackItemSchema),
  areas: z.array(productionReadinessAreaSchema),
  goldenFilingCorpus: z.array(goldenFilingCorpusScenarioSchema),
  goldenEvidenceLedger: z.array(goldenEvidenceLedgerEntrySchema),
  statutoryRuleMatrix: z.array(statutoryRuleMatrixEntrySchema),
  statutoryRulesCoverage: z.array(statutoryRulesCoverageItemSchema),
  manualHandoffPaths: z.array(z.string().min(1)),
  operationalGates: z.array(operationalGateSchema),
  assuranceActions: z.array(productionReadinessAssuranceActionSchema),
  completionTracks: z.array(productionReadinessCompletionTrackSchema),
  releaseBlockerRegister: z.array(productionReleaseBlockerSchema),
  auditabilityControls: z.array(productionAuditabilityControlSchema),
  auditEvidenceTimeline: z.array(auditEvidenceTimelineEntrySchema),
  auditEvidencePack: z.array(productionAuditEvidencePackItemSchema),
  monitoringControls: z.array(productionMonitoringControlSchema),
  dependencyPolicyControls: z.array(dependencyPolicyControlSchema),
  deploymentSafetyControls: z.array(deploymentSafetyControlSchema),
  operationsEvidencePack: z.array(operationsEvidencePackItemSchema),
  releaseReviewChecklist: z.array(releaseReviewChecklistItemSchema),
  releaseVerificationManifest: z.array(releaseVerificationManifestItemSchema),
  visualQaCoverage: visualQaCoverageSchema,
});

export function parseProductionReadinessReport(payload: unknown): ProductionReadinessReport {
  const result = productionReadinessReportSchema.safeParse(payload);

  if (!result.success) {
    const issue = result.error.issues[0];
    const path = issue?.path.length ? issue.path.join(".") : "root";
    const message = issue?.message ?? "Invalid payload";
    throw new Error(`Invalid production readiness report contract: ${path} - ${message}`);
  }

  const report: ProductionReadinessReport = result.data;
  assertProductionReadinessInvariants(report);
  return report;
}

function assertProductionReadinessInvariants(report: ProductionReadinessReport) {
  assertExpectedNumber(
    "sourceLawSnapshot.sourceCount",
    report.sourceLawSnapshot.sources.length,
    report.sourceLawSnapshot.sourceCount,
  );
  assertExpectedNumber(
    "sourceLawTraceability.length",
    report.sourceLawSnapshot.sourceCount,
    report.sourceLawTraceability.length,
  );
  assertExpectedNumber(
    "assurancePacket.goldenCorpusTotal",
    report.goldenFilingCorpus.length,
    report.assurancePacket.goldenCorpusTotal,
  );
  assertExpectedNumber(
    "assurancePacket.goldenCorpusCovered",
    report.goldenFilingCorpus.filter((scenario) => scenario.coverageStatus === "covered").length,
    report.assurancePacket.goldenCorpusCovered,
  );
  assertExpectedNumber(
    "visualQaCoverage.expectedScreenshotCount",
    report.visualQaCoverage.themes.length * report.visualQaCoverage.viewports.length * report.visualQaCoverage.routes.length,
    report.visualQaCoverage.expectedScreenshotCount,
  );
  assertExpectedNumber(
    "visualQaCoverage.artifacts.length",
    report.visualQaCoverage.expectedScreenshotCount,
    report.visualQaCoverage.artifacts.length,
  );
  assertExpectedNumber(
    "assurancePacket.visualQaExpectedScreenshots",
    report.visualQaCoverage.expectedScreenshotCount,
    report.assurancePacket.visualQaExpectedScreenshots,
  );
  assertAssuranceActionsRiskOrder(report.assuranceActions);
  assertSourceLawMaintenanceProtocol(report);
  assertVisualQaArtifacts(report);

  const expectedWorkflowStages = [
    "Setup",
    "Import",
    "Classify",
    "Year-End",
    "Statements",
    "Notes",
    "Review",
    "Filing",
  ];
  const coveredWorkflowStages = new Set(report.visualQaCoverage.routes.flatMap((route) => route.workflowStages));
  const missingWorkflowStages = expectedWorkflowStages.filter((stage) => !coveredWorkflowStages.has(stage));

  if (missingWorkflowStages.length > 0) {
    throw new Error(
      `Invalid production readiness report contract: visualQaCoverage.routes.workflowStages - missing accountant workflow stages: ${missingWorkflowStages.join(", ")}`,
    );
  }

  report.visualQaCoverage.routes.forEach((route, routeIndex) => {
    if (route.workflowStages.length === 0) {
      throw new Error(
        `Invalid production readiness report contract: visualQaCoverage.routes.${routeIndex}.workflowStages - every visual QA route must state the workflow stages it proves`,
      );
    }
  });

  const periodWorkspace = report.visualQaCoverage.routes.find((route) => route.code === "period-workspace");
  if (!periodWorkspace || expectedWorkflowStages.some((stage) => !periodWorkspace.workflowStages.includes(stage))) {
    throw new Error(
      "Invalid production readiness report contract: visualQaCoverage.routes.period-workspace.workflowStages - period workspace must prove the full accountant workflow rail",
    );
  }

  const scenarioCodes = new Set(report.goldenFilingCorpus.map((scenario) => scenario.code));
  const acceptanceCodes = new Set(report.accountantAcceptanceCriteria.map((criterion) => criterion.scenarioCode));
  const missingAcceptanceCriteria = [...scenarioCodes]
    .filter((code) => !acceptanceCodes.has(code))
    .sort();

  if (missingAcceptanceCriteria.length > 0) {
    throw new Error(
      `Invalid production readiness report contract: accountantAcceptanceCriteria - missing acceptance criteria for golden scenarios: ${missingAcceptanceCriteria.join(", ")}`,
    );
  }

  const scenariosByCode = new Map(report.goldenFilingCorpus.map((scenario) => [scenario.code, scenario]));
  assertProductionSprintGoldenScenarios(scenariosByCode);
  assertGoldenEvidenceLedger(report, scenariosByCode);
  report.accountantAcceptanceCriteria.forEach((criterion, criterionIndex) => {
    const scenario = scenariosByCode.get(criterion.scenarioCode);

    if (!scenario) {
      throw new Error(
        `Invalid production readiness report contract: accountantAcceptanceCriteria.${criterionIndex}.scenarioCode - must reference a golden scenario`,
      );
    }

    const scenarioVerifierNames = scenario.evidenceVerifiers
      .map((verifier) => verifier.name)
      .sort((left, right) => left.localeCompare(right));
    const acceptanceVerifierNames = criterion.evidenceVerifiers
      .map((verifier) => verifier.name)
      .sort((left, right) => left.localeCompare(right));

    if (
      scenarioVerifierNames.length !== acceptanceVerifierNames.length ||
      scenarioVerifierNames.some((name, index) => name !== acceptanceVerifierNames[index])
    ) {
      throw new Error(
        `Invalid production readiness report contract: accountantAcceptanceCriteria.${criterionIndex}.evidenceVerifiers - must match the golden scenario verifier manifest`,
      );
    }

    criterion.evidenceVerifiers.forEach((verifier, verifierIndex) => {
      if (!verifier.command.includes("dotnet test Accounts.slnx") || !verifier.command.includes(verifier.name)) {
        throw new Error(
          `Invalid production readiness report contract: accountantAcceptanceCriteria.${criterionIndex}.evidenceVerifiers.${verifierIndex}.command - must include the executable backend verifier command`,
        );
      }
    });
  });

  const requiredAcceptanceCriteria = report.accountantAcceptanceCriteria.filter((criterion) => criterion.required);
  const acceptanceSummary = report.accountantAcceptanceSummary;
  assertExpectedNumber(
    "accountantAcceptanceSummary.scenarioCount",
    report.goldenFilingCorpus.length,
    acceptanceSummary.scenarioCount,
  );
  assertExpectedNumber(
    "accountantAcceptanceSummary.professionalSignOffRequiredCount",
    requiredAcceptanceCriteria.length,
    acceptanceSummary.professionalSignOffRequiredCount,
  );
  assertExpectedNumber(
    "accountantAcceptanceSummary.automatedVerifierCount",
    new Set(report.accountantAcceptanceCriteria.flatMap((criterion) => criterion.evidenceVerifiers.map((verifier) => verifier.name))).size,
    acceptanceSummary.automatedVerifierCount,
  );
  assertExpectedNumber(
    "accountantAcceptanceSummary.manualHandoffScenarioCount",
    report.goldenFilingCorpus.filter((scenario) =>
      scenario.expectedOutcome.toLowerCase().includes("manual-handoff")
      || scenario.fixture.manualProfessionalReviewRequired
    ).length,
    acceptanceSummary.manualHandoffScenarioCount,
  );
  const expectedReleaseBlockingScenarioCodes = requiredAcceptanceCriteria
    .filter((criterion) => criterion.acceptanceStatus !== "accepted")
    .map((criterion) => criterion.scenarioCode)
    .sort((left, right) => left.localeCompare(right));
  assertStringArrayEqual(
    "accountantAcceptanceSummary.releaseBlockingScenarioCodes",
    expectedReleaseBlockingScenarioCodes,
    [...acceptanceSummary.releaseBlockingScenarioCodes].sort((left, right) => left.localeCompare(right)),
  );
  assertStringArrayEqual(
    "accountantAcceptanceSummary.requiredSignOffGates",
    [...new Set(requiredAcceptanceCriteria.map((criterion) => criterion.requiredSignOffGate))].sort((left, right) => left.localeCompare(right)),
    [...acceptanceSummary.requiredSignOffGates].sort((left, right) => left.localeCompare(right)),
  );
  const expectedAcceptanceSummaryStatus = expectedReleaseBlockingScenarioCodes.length === 0
    ? "accepted"
    : "qualified-accountant-review-required";
  if (acceptanceSummary.status !== expectedAcceptanceSummaryStatus) {
    throw new Error(
      `Invalid production readiness report contract: accountantAcceptanceSummary.status - expected ${expectedAcceptanceSummaryStatus}, received ${acceptanceSummary.status}`,
    );
  }

  assertAccountantWorkflowWalkthroughProtocol(report);
  assertAccountantJourneyAcceptanceChecklist(report);
  assertAccountantWorkflowEvidencePack(report);

  const snapshotSourceIds = new Set(report.sourceLawSnapshot.sources.map((source) => source.sourceId));
  const traceabilitySourceIds = new Set(report.sourceLawTraceability.map((entry) => entry.sourceId));
  const missingTraceability = [...snapshotSourceIds]
    .filter((sourceId) => !traceabilitySourceIds.has(sourceId))
    .sort();
  const unexpectedTraceability = [...traceabilitySourceIds]
    .filter((sourceId) => !snapshotSourceIds.has(sourceId))
    .sort();

  if (missingTraceability.length > 0 || unexpectedTraceability.length > 0) {
    throw new Error(
      `Invalid production readiness report contract: sourceLawTraceability - expected snapshot source ids only; missing ${missingTraceability.join(", ") || "none"}, unexpected ${unexpectedTraceability.join(", ") || "none"}`,
    );
  }

  report.sourceLawTraceability.forEach((entry, entryIndex) => {
    if (!entry.inSnapshot) {
      throw new Error(
        `Invalid production readiness report contract: sourceLawTraceability.${entryIndex}.inSnapshot - every production source must be pinned in the snapshot`,
      );
    }

    if (entry.usedBy.length === 0) {
      throw new Error(
        `Invalid production readiness report contract: sourceLawTraceability.${entryIndex}.usedBy - every pinned source must have at least one usage`,
      );
    }

    if (entry.releaseGateCodes.length === 0) {
      throw new Error(
        `Invalid production readiness report contract: sourceLawTraceability.${entryIndex}.releaseGateCodes - every pinned source must link to at least one release gate`,
      );
    }
  });

  assertSourceLawReviewLedger(report, snapshotSourceIds);
  assertRevenueTaxonomyRanges(report);

  if (!report.assurancePacket.evidenceItems.includes("source-law-traceability-index")) {
    throw new Error(
      "Invalid production readiness report contract: assurancePacket.evidenceItems - source-law-traceability-index is required",
    );
  }

  if (!report.assurancePacket.evidenceItems.includes("release-review-checklist")) {
    throw new Error(
      "Invalid production readiness report contract: assurancePacket.evidenceItems - release-review-checklist is required",
    );
  }

  if (!report.assurancePacket.evidenceItems.includes("release-blocker-register")) {
    throw new Error(
      "Invalid production readiness report contract: assurancePacket.evidenceItems - release-blocker-register is required",
    );
  }

  if (!report.assurancePacket.evidenceItems.includes("release-verification-manifest")) {
    throw new Error(
      "Invalid production readiness report contract: assurancePacket.evidenceItems - release-verification-manifest is required",
    );
  }

  if (!report.assurancePacket.evidenceItems.includes("audit-evidence-timeline")) {
    throw new Error(
      "Invalid production readiness report contract: assurancePacket.evidenceItems - audit-evidence-timeline is required",
    );
  }

  if (!report.assurancePacket.evidenceItems.includes("production-audit-evidence-pack")) {
    throw new Error(
      "Invalid production readiness report contract: assurancePacket.evidenceItems - production-audit-evidence-pack is required",
    );
  }

  if (!report.assurancePacket.evidenceItems.includes("production-completion-map")) {
    throw new Error(
      "Invalid production readiness report contract: assurancePacket.evidenceItems - production-completion-map is required",
    );
  }

  if (!report.assurancePacket.evidenceItems.includes("operations-evidence-pack")) {
    throw new Error(
      "Invalid production readiness report contract: assurancePacket.evidenceItems - operations-evidence-pack is required",
    );
  }

  if (!report.assurancePacket.evidenceItems.includes("golden-evidence-ledger")) {
    throw new Error(
      "Invalid production readiness report contract: assurancePacket.evidenceItems - golden-evidence-ledger is required",
    );
  }

  if (!report.assurancePacket.evidenceItems.includes("source-law-review-ledger")) {
    throw new Error(
      "Invalid production readiness report contract: assurancePacket.evidenceItems - source-law-review-ledger is required",
    );
  }

  report.auditEvidenceTimeline.forEach((entry, entryIndex) => {
    if (entry.auditEventCodes.length === 0) {
      throw new Error(
        `Invalid production readiness report contract: auditEvidenceTimeline.${entryIndex}.auditEventCodes - at least one audit event code is required`,
      );
    }

    if (entry.blockingGateCodes.length === 0) {
      throw new Error(
        `Invalid production readiness report contract: auditEvidenceTimeline.${entryIndex}.blockingGateCodes - at least one blocking gate code is required`,
      );
    }
  });

  report.auditEvidencePack.forEach((item, itemIndex) => {
    if (item.auditEventCodes.length === 0) {
      throw new Error(
        `Invalid production readiness report contract: auditEvidencePack.${itemIndex}.auditEventCodes - at least one audit event code is required`,
      );
    }

    if (item.blockingGateCodes.length === 0) {
      throw new Error(
        `Invalid production readiness report contract: auditEvidencePack.${itemIndex}.blockingGateCodes - at least one blocking gate code is required`,
      );
    }
  });

  const operationalGateCodes = new Set(report.operationalGates.map((gate) => gate.code));
  const operationsControlGateCodes = new Set([
    "production-monitoring",
    "dependency-policy-controls",
    "deployment-safety-controls",
  ]);

  report.operationsEvidencePack.forEach((item, itemIndex) => {
    if (!item.required) {
      throw new Error(
        `Invalid production readiness report contract: operationsEvidencePack.${itemIndex}.required - operations release evidence must be required`,
      );
    }

    if (!operationalGateCodes.has(item.releaseGateCode) && !operationsControlGateCodes.has(item.releaseGateCode)) {
      throw new Error(
        `Invalid production readiness report contract: operationsEvidencePack.${itemIndex}.releaseGateCode - unknown release gate ${item.releaseGateCode}`,
      );
    }

    if (!item.failurePolicy.toLowerCase().includes("block release")) {
      throw new Error(
        `Invalid production readiness report contract: operationsEvidencePack.${itemIndex}.failurePolicy - must block release when evidence is missing`,
      );
    }
  });

  assertCompletionTracks(report);
  assertReleaseReviewChecklist(report);
  assertReleaseBlockerRegister(report);
  assertReleaseVerificationManifest(report);

  report.goldenFilingCorpus.forEach((scenario, scenarioIndex) => {
    const evidenceTests = new Set(scenario.evidenceTestNames);
    const verifierNames = new Set(scenario.evidenceVerifiers.map((verifier) => verifier.name));
    const legalBasis = scenario.legalBasisSnapshot;

    if (legalBasis.scenarioCode !== scenario.code) {
      throw new Error(
        `Invalid production readiness report contract: goldenFilingCorpus.${scenarioIndex}.legalBasisSnapshot.scenarioCode - must match scenario code`,
      );
    }

    if (legalBasis.companyType !== scenario.fixture.companyType) {
      throw new Error(
        `Invalid production readiness report contract: goldenFilingCorpus.${scenarioIndex}.legalBasisSnapshot.companyType - must match fixture company type`,
      );
    }

    if (legalBasis.sizeClass !== scenario.fixture.expectedSizeClass) {
      throw new Error(
        `Invalid production readiness report contract: goldenFilingCorpus.${scenarioIndex}.legalBasisSnapshot.sizeClass - must match fixture size class`,
      );
    }

    if (legalBasis.electedRegime !== scenario.fixture.expectedRegime) {
      throw new Error(
        `Invalid production readiness report contract: goldenFilingCorpus.${scenarioIndex}.legalBasisSnapshot.electedRegime - must match fixture regime`,
      );
    }

    if (legalBasis.auditExempt !== scenario.fixture.auditExempt) {
      throw new Error(
        `Invalid production readiness report contract: goldenFilingCorpus.${scenarioIndex}.legalBasisSnapshot.auditExempt - must match fixture audit exemption`,
      );
    }

    if (legalBasis.manualProfessionalReviewRequired !== scenario.fixture.manualProfessionalReviewRequired) {
      throw new Error(
        `Invalid production readiness report contract: goldenFilingCorpus.${scenarioIndex}.legalBasisSnapshot.manualProfessionalReviewRequired - must match fixture manual review gate`,
      );
    }

    const requiredOutputs = new Set(legalBasis.requiredOutputs);
    const missingOutputs = scenario.evidencePack.outputArtifacts.filter((artifact) => !requiredOutputs.has(artifact));
    if (missingOutputs.length > 0) {
      throw new Error(
        `Invalid production readiness report contract: goldenFilingCorpus.${scenarioIndex}.legalBasisSnapshot.requiredOutputs - missing evidence-pack outputs: ${missingOutputs.join(", ")}`,
      );
    }

    const professionalGates = new Set(legalBasis.professionalGates);
    const missingGates = scenario.evidencePack.decisionGates.filter((gate) => !professionalGates.has(gate));
    if (missingGates.length > 0) {
      throw new Error(
        `Invalid production readiness report contract: goldenFilingCorpus.${scenarioIndex}.legalBasisSnapshot.professionalGates - missing evidence-pack gates: ${missingGates.join(", ")}`,
      );
    }

    const legalSourceIds = new Set(legalBasis.sourceIds);
    const missingSourceIds = scenario.evidencePack.sourceReferences
      .map((sourceReference) => sourceReference.sourceId)
      .filter((sourceId) => !legalSourceIds.has(sourceId));
    if (missingSourceIds.length > 0) {
      throw new Error(
        `Invalid production readiness report contract: goldenFilingCorpus.${scenarioIndex}.legalBasisSnapshot.sourceIds - missing evidence-pack source IDs: ${missingSourceIds.join(", ")}`,
      );
    }

    scenario.evidenceTestNames.forEach((testName) => {
      if (!verifierNames.has(testName)) {
        throw new Error(
          `Invalid production readiness report contract: goldenFilingCorpus.${scenarioIndex}.evidenceVerifiers - every evidenceTestNames entry must have verifier metadata`,
        );
      }
    });

    scenario.evidenceVerifiers.forEach((verifier, verifierIndex) => {
      if (!evidenceTests.has(verifier.name)) {
        throw new Error(
          `Invalid production readiness report contract: goldenFilingCorpus.${scenarioIndex}.evidenceVerifiers.${verifierIndex}.name - verifier must be listed in evidenceTestNames`,
        );
      }

      if (scenario.coverageStatus === "covered" && !verifier.runsInDefaultCi) {
        throw new Error(
          `Invalid production readiness report contract: goldenFilingCorpus.${scenarioIndex}.evidenceVerifiers.${verifierIndex}.runsInDefaultCi - covered scenarios must run in default CI`,
        );
      }
    });

    scenario.evidencePack.expectedProofPoints.forEach((proofPoint, proofPointIndex) => {
      if (!evidenceTests.has(proofPoint.automatedVerifier)) {
        throw new Error(
          `Invalid production readiness report contract: goldenFilingCorpus.${scenarioIndex}.evidencePack.expectedProofPoints.${proofPointIndex}.automatedVerifier - verifier must be listed in evidenceTestNames`,
        );
      }
    });
  });

  if (!report.assurancePacket.evidenceItems.includes("golden-verifier-manifest")) {
    throw new Error(
      "Invalid production readiness report contract: assurancePacket.evidenceItems - golden-verifier-manifest is required",
    );
  }
}

function assertProductionSprintGoldenScenarios(
  scenariosByCode: Map<string, GoldenFilingCorpusScenario>,
) {
  const requiredScenarioCodes = [
    "micro-ltd",
    "small-abridged-ltd",
    "clg-charity",
    "medium-audit-required",
  ];
  const missingScenarioCodes = requiredScenarioCodes.filter((code) => !scenariosByCode.has(code));

  if (missingScenarioCodes.length > 0) {
    throw new Error(
      `Invalid production readiness report contract: goldenFilingCorpus - missing required production sprint scenarios: ${missingScenarioCodes.join(", ")}`,
    );
  }

  assertSmallAbridgedGoldenScenario(scenariosByCode);
  assertClgCharityGoldenScenario(scenariosByCode);
  assertMediumAuditRequiredGoldenScenario(scenariosByCode);
}

function assertSmallAbridgedGoldenScenario(
  scenariosByCode: Map<string, GoldenFilingCorpusScenario>,
) {
  const scenario = scenariosByCode.get("small-abridged-ltd");
  const requiredVerifier = "FilingGoldenCorpusScenarioTests.GoldenCorpus_SmallAbridgedLtd_EmitsFullAccountsAbridgedCroPackIxbrlAndReadiness";

  if (!scenario) {
    throw new Error(
      "Invalid production readiness report contract: goldenFilingCorpus.small-abridged-ltd - scenario is required",
    );
  }

  if (!scenario.evidenceTestNames.includes(requiredVerifier)) {
    throw new Error(
      "Invalid production readiness report contract: goldenFilingCorpus.small-abridged-ltd - dedicated small abridged verifier is required",
    );
  }

  if (!scenario.evidenceVerifiers.some((verifier) => verifier.name === requiredVerifier)) {
    throw new Error(
      "Invalid production readiness report contract: goldenFilingCorpus.small-abridged-ltd - dedicated small abridged verifier metadata is required",
    );
  }

  const evidencePack = scenario.evidencePack;
  if (!evidencePack.outputArtifacts.some((artifact) => artifact.toLowerCase().includes("abridged cro filing pack"))) {
    throw new Error(
      "Invalid production readiness report contract: goldenFilingCorpus.small-abridged-ltd.evidencePack.outputArtifacts - abridged CRO filing pack evidence is required",
    );
  }

  if (!evidencePack.expectedValueChecks.some((check) => check.toLowerCase().includes("section 352"))) {
    throw new Error(
      "Invalid production readiness report contract: goldenFilingCorpus.small-abridged-ltd.evidencePack.expectedValueChecks - Section 352 evidence is required",
    );
  }

  if (!evidencePack.expectedOutputs.pdfTextMarkers.some((marker) => marker.toLowerCase().includes("section 352"))) {
    throw new Error(
      "Invalid production readiness report contract: goldenFilingCorpus.small-abridged-ltd.evidencePack.expectedOutputs.pdfTextMarkers - Section 352 PDF marker is required",
    );
  }

  if (!evidencePack.expectedOutputs.filingGateStates.some((gate) => gate.toLowerCase().includes("abridgement eligibility"))) {
    throw new Error(
      "Invalid production readiness report contract: goldenFilingCorpus.small-abridged-ltd.evidencePack.expectedOutputs.filingGateStates - abridgement eligibility gate is required",
    );
  }
}

function assertClgCharityGoldenScenario(
  scenariosByCode: Map<string, GoldenFilingCorpusScenario>,
) {
  const scenario = scenariosByCode.get("clg-charity");
  const requiredVerifier = "FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness";

  if (!scenario) return;

  if (!scenario.evidenceTestNames.includes(requiredVerifier)) {
    throw new Error(
      "Invalid production readiness report contract: goldenFilingCorpus.clg-charity - dedicated CLG charity verifier is required",
    );
  }

  if (scenario.fixture.companyType !== "CompanyLimitedByGuarantee") {
    throw new Error(
      "Invalid production readiness report contract: goldenFilingCorpus.clg-charity.fixture.companyType - CLG charity fixture must use CompanyLimitedByGuarantee",
    );
  }

  const evidencePack = scenario.evidencePack;
  if (!evidencePack.outputArtifacts.some((artifact) => artifact.toLowerCase().includes("charity readiness profile"))) {
    throw new Error(
      "Invalid production readiness report contract: goldenFilingCorpus.clg-charity.evidencePack.outputArtifacts - charity readiness profile evidence is required",
    );
  }

  if (!evidencePack.decisionGates.some((gate) => gate.toLowerCase().includes("charity annual return review"))) {
    throw new Error(
      "Invalid production readiness report contract: goldenFilingCorpus.clg-charity.evidencePack.decisionGates - charity annual return review gate is required",
    );
  }

  if (!evidencePack.expectedValueChecks.some((check) => check.toLowerCase().includes("charities regulator"))) {
    throw new Error(
      "Invalid production readiness report contract: goldenFilingCorpus.clg-charity.evidencePack.expectedValueChecks - Charities Regulator source evidence is required",
    );
  }

  if (!evidencePack.expectedOutputs.filingGateStates.some((gate) => gate.toLowerCase().includes("sofa"))) {
    throw new Error(
      "Invalid production readiness report contract: goldenFilingCorpus.clg-charity.evidencePack.expectedOutputs.filingGateStates - SoFA and trustees report evidence gate is required",
    );
  }
}

function assertMediumAuditRequiredGoldenScenario(
  scenariosByCode: Map<string, GoldenFilingCorpusScenario>,
) {
  const scenario = scenariosByCode.get("medium-audit-required");
  const requiredVerifier = "FilingGoldenCorpusScenarioTests.GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence";

  if (!scenario) return;

  if (!scenario.evidenceTestNames.includes(requiredVerifier)) {
    throw new Error(
      "Invalid production readiness report contract: goldenFilingCorpus.medium-audit-required - dedicated medium audit-required verifier is required",
    );
  }

  if (!scenario.expectedOutcome.toLowerCase().includes("manual-handoff")) {
    throw new Error(
      "Invalid production readiness report contract: goldenFilingCorpus.medium-audit-required.expectedOutcome - manual handoff outcome is required",
    );
  }

  if (!scenario.fixture.manualProfessionalReviewRequired || scenario.fixture.auditExempt) {
    throw new Error(
      "Invalid production readiness report contract: goldenFilingCorpus.medium-audit-required.fixture - audit-required manual professional review fixture is required",
    );
  }

  const evidencePack = scenario.evidencePack;
  if (!evidencePack.outputArtifacts.some((artifact) => artifact.toLowerCase().includes("auditor report evidence"))) {
    throw new Error(
      "Invalid production readiness report contract: goldenFilingCorpus.medium-audit-required.evidencePack.outputArtifacts - auditor report evidence is required",
    );
  }

  if (!evidencePack.decisionGates.some((gate) => gate.toLowerCase().includes("auditor handoff"))) {
    throw new Error(
      "Invalid production readiness report contract: goldenFilingCorpus.medium-audit-required.evidencePack.decisionGates - auditor handoff gate is required",
    );
  }

  if (!evidencePack.expectedOutputs.filingGateStates.some((gate) => gate.toLowerCase().includes("normal cro approval blocked"))) {
    throw new Error(
      "Invalid production readiness report contract: goldenFilingCorpus.medium-audit-required.evidencePack.expectedOutputs.filingGateStates - CRO approval blocker is required",
    );
  }

  if (evidencePack.expectedOutputs.signOffPacketState !== "manual-handoff") {
    throw new Error(
      "Invalid production readiness report contract: goldenFilingCorpus.medium-audit-required.evidencePack.expectedOutputs.signOffPacketState - manual handoff sign-off state is required",
    );
  }
}

function assertGoldenEvidenceLedger(
  report: ProductionReadinessReport,
  scenariosByCode: Map<string, GoldenFilingCorpusScenario>,
) {
  const ledgerCodes = report.goldenEvidenceLedger.map((entry) => entry.scenarioCode);
  const duplicateLedgerCodes = ledgerCodes.filter((code, index) => ledgerCodes.indexOf(code) !== index);
  if (duplicateLedgerCodes.length > 0) {
    throw new Error(
      `Invalid production readiness report contract: goldenEvidenceLedger - duplicate scenario codes: ${[...new Set(duplicateLedgerCodes)].join(", ")}`,
    );
  }

  const scenarioCodes = [...scenariosByCode.keys()].sort((left, right) => left.localeCompare(right));
  const sortedLedgerCodes = [...ledgerCodes].sort((left, right) => left.localeCompare(right));
  if (
    scenarioCodes.length !== sortedLedgerCodes.length ||
    scenarioCodes.some((code, index) => code !== sortedLedgerCodes[index])
  ) {
    const missing = scenarioCodes.filter((code) => !sortedLedgerCodes.includes(code));
    const unexpected = sortedLedgerCodes.filter((code) => !scenarioCodes.includes(code));
    throw new Error(
      `Invalid production readiness report contract: goldenEvidenceLedger - expected one ledger entry per golden scenario; missing ${missing.join(", ") || "none"}, unexpected ${unexpected.join(", ") || "none"}`,
    );
  }

  const acceptanceByScenario = new Map(report.accountantAcceptanceCriteria.map((criterion) => [criterion.scenarioCode, criterion]));
  report.goldenEvidenceLedger.forEach((entry, entryIndex) => {
    const scenario = scenariosByCode.get(entry.scenarioCode);
    const acceptance = acceptanceByScenario.get(entry.scenarioCode);
    if (!scenario || !acceptance) return;

    assertLedgerValue(entryIndex, "label", scenario.label, entry.label, "must mirror golden scenario label");
    assertLedgerValue(entryIndex, "fixtureLegalName", scenario.fixture.legalName, entry.fixtureLegalName, "must mirror golden scenario fixture");
    assertLedgerValue(entryIndex, "companyType", scenario.fixture.companyType, entry.companyType, "must mirror golden scenario fixture");
    assertLedgerValue(entryIndex, "expectedOutcome", scenario.expectedOutcome, entry.expectedOutcome, "must mirror golden scenario outcome");
    assertLedgerValue(entryIndex, "coverageStatus", scenario.coverageStatus, entry.coverageStatus, "must mirror golden scenario coverage");
    assertLedgerValue(entryIndex, "acceptanceStatus", acceptance.acceptanceStatus, entry.acceptanceStatus, "must mirror accountant acceptance status");
    assertLedgerValue(entryIndex, "requiredSignOffGate", acceptance.requiredSignOffGate, entry.requiredSignOffGate, "must mirror accountant acceptance sign-off gate");
    assertLedgerValue(
      entryIndex,
      "filingReadinessState",
      scenario.evidencePack.expectedOutputs.filingReadinessState,
      entry.filingReadinessState,
      "must mirror golden scenario expected outputs",
    );
    assertLedgerValue(
      entryIndex,
      "signOffPacketState",
      scenario.evidencePack.expectedOutputs.signOffPacketState,
      entry.signOffPacketState,
      "must mirror golden scenario expected outputs",
    );

    if (scenario.evidencePack.expectedOutputs.expectedCorporationTax !== entry.expectedCorporationTax) {
      throw new Error(
        `Invalid production readiness report contract: goldenEvidenceLedger.${entryIndex}.expectedCorporationTax - must mirror golden scenario expected outputs`,
      );
    }

    if (!entry.blocksRelease) {
      throw new Error(
        `Invalid production readiness report contract: goldenEvidenceLedger.${entryIndex}.blocksRelease - golden scenarios require professional release review`,
      );
    }

    assertLedgerStringArray(
      entryIndex,
      "automatedVerifierNames",
      scenario.evidenceTestNames,
      entry.automatedVerifierNames,
      "must mirror golden scenario verifier manifest",
    );
    assertLedgerStringArray(
      entryIndex,
      "automatedVerifierCommands",
      scenario.evidenceVerifiers.map((verifier) => verifier.command),
      entry.automatedVerifierCommands,
      "must mirror golden scenario verifier commands",
    );
    assertLedgerStringArray(
      entryIndex,
      "ciScopes",
      [...new Set(scenario.evidenceVerifiers.map((verifier) => verifier.ciScope))],
      entry.ciScopes,
      "must mirror golden scenario verifier CI scopes",
    );
    assertLedgerStringArray(
      entryIndex,
      "evidenceLevels",
      [...new Set(scenario.evidenceVerifiers.map((verifier) => verifier.evidenceLevel))],
      entry.evidenceLevels,
      "must mirror golden scenario verifier evidence levels",
    );
    assertLedgerStringArray(
      entryIndex,
      "outputArtifacts",
      scenario.evidencePack.outputArtifacts,
      entry.outputArtifacts,
      "must mirror golden scenario evidence pack",
    );
    assertLedgerStringArray(
      entryIndex,
      "decisionGates",
      scenario.evidencePack.decisionGates,
      entry.decisionGates,
      "must mirror golden scenario evidence pack",
    );
    assertLedgerStringArray(
      entryIndex,
      "expectedValueChecks",
      scenario.evidencePack.expectedValueChecks,
      entry.expectedValueChecks,
      "must mirror golden scenario evidence pack",
    );
    assertLedgerStringArray(
      entryIndex,
      "proofPointAreas",
      scenario.evidencePack.expectedProofPoints.map((proofPoint) => proofPoint.area),
      entry.proofPointAreas,
      "must mirror golden scenario proof points",
    );
    assertLedgerStringArray(
      entryIndex,
      "sourceIds",
      scenario.evidencePack.sourceReferences.map((source) => source.sourceId),
      entry.sourceIds,
      "must mirror golden scenario source references",
    );
  });
}

function assertLedgerValue(
  entryIndex: number,
  field: string,
  expected: string,
  received: string,
  message: string,
) {
  if (expected !== received) {
    throw new Error(`Invalid production readiness report contract: goldenEvidenceLedger.${entryIndex}.${field} - ${message}`);
  }
}

function assertLedgerStringArray(
  entryIndex: number,
  field: string,
  expected: string[],
  received: string[],
  message: string,
) {
  const sortedExpected = [...new Set(expected)].sort((left, right) => left.localeCompare(right));
  const sortedReceived = [...received].sort((left, right) => left.localeCompare(right));
  if (
    sortedExpected.length !== sortedReceived.length ||
    sortedExpected.some((value, index) => value !== sortedReceived[index])
  ) {
    throw new Error(`Invalid production readiness report contract: goldenEvidenceLedger.${entryIndex}.${field} - ${message}`);
  }
}

function assertSourceLawMaintenanceProtocol(report: ProductionReadinessReport) {
  const releaseChecklistCodes = new Set(report.releaseReviewChecklist.map((item) => item.code));
  const snapshotSourceIds = report.sourceLawSnapshot.sources.map((source) => source.sourceId).sort();
  const monitoredSourceIds = [...report.sourceLawMaintenanceProtocol.monitoredSourceIds].sort();

  if (!releaseChecklistCodes.has(report.sourceLawMaintenanceProtocol.signOffGate)) {
    throw new Error(
      "Invalid production readiness report contract: sourceLawMaintenanceProtocol.signOffGate - must reference a release checklist item",
    );
  }

  assertStringArrayEqual(
    "sourceLawMaintenanceProtocol.monitoredSourceIds",
    snapshotSourceIds,
    monitoredSourceIds,
  );

  if (!report.assurancePacket.evidenceItems.includes("source-law-maintenance-protocol")) {
    throw new Error(
      "Invalid production readiness report contract: assurancePacket.evidenceItems - source-law-maintenance-protocol is required",
    );
  }

  for (const evidence of ["source-law-snapshot-fingerprint", "source-law-traceability-index"]) {
    if (!report.sourceLawMaintenanceProtocol.requiredEvidence.includes(evidence)) {
      throw new Error(
        `Invalid production readiness report contract: sourceLawMaintenanceProtocol.requiredEvidence - ${evidence} is required`,
      );
    }
  }

  if (report.sourceLawMaintenanceProtocol.acceptanceCriteria.length === 0) {
    throw new Error(
      "Invalid production readiness report contract: sourceLawMaintenanceProtocol.acceptanceCriteria - at least one criterion is required",
    );
  }
}

function assertSourceLawReviewLedger(report: ProductionReadinessReport, snapshotSourceIds: Set<string>) {
  const releaseChecklistCodes = new Set(report.releaseReviewChecklist.map((item) => item.code));
  const ledgerSourceIds = new Set(report.sourceLawReviewLedger.map((entry) => entry.sourceId));
  const missingLedger = [...snapshotSourceIds]
    .filter((sourceId) => !ledgerSourceIds.has(sourceId))
    .sort();
  const unexpectedLedger = [...ledgerSourceIds]
    .filter((sourceId) => !snapshotSourceIds.has(sourceId))
    .sort();

  if (missingLedger.length > 0 || unexpectedLedger.length > 0) {
    throw new Error(
      `Invalid production readiness report contract: sourceLawReviewLedger - expected snapshot source ids only; missing ${missingLedger.join(", ") || "none"}, unexpected ${unexpectedLedger.join(", ") || "none"}`,
    );
  }

  report.sourceLawReviewLedger.forEach((entry, entryIndex) => {
    if (!releaseChecklistCodes.has(entry.releaseChecklistCode)) {
      throw new Error(
        `Invalid production readiness report contract: sourceLawReviewLedger.${entryIndex}.releaseChecklistCode - must reference a release checklist item`,
      );
    }

    if (!entry.blocksRelease) {
      throw new Error(
        `Invalid production readiness report contract: sourceLawReviewLedger.${entryIndex}.blocksRelease - source-law review must block release`,
      );
    }

    if (entry.reviewChecks.length === 0) {
      throw new Error(
        `Invalid production readiness report contract: sourceLawReviewLedger.${entryIndex}.reviewChecks - at least one source-law review check is required`,
      );
    }

    for (const evidence of ["source-law-change-review-note", "qualified-accountant-source-law-signoff"]) {
      if (!entry.requiredEvidence.includes(evidence)) {
        throw new Error(
          `Invalid production readiness report contract: sourceLawReviewLedger.${entryIndex}.requiredEvidence - ${evidence} is required`,
        );
      }
    }
  });
}

function assertRevenueTaxonomyRanges(report: ProductionReadinessReport) {
  if (report.revenueTaxonomyRanges.length === 0) {
    throw new Error("Invalid production readiness report contract: revenueTaxonomyRanges - at least one accepted taxonomy range is required");
  }

  if (!report.assurancePacket.evidenceItems.includes("revenue-taxonomy-range-evidence")) {
    throw new Error(
      "Invalid production readiness report contract: assurancePacket.evidenceItems - revenue-taxonomy-range-evidence is required",
    );
  }

  report.revenueTaxonomyRanges.forEach((range, index) => {
    if (!range.releaseGateCodes.includes("ixbrl-taxonomy-selection")) {
      throw new Error(
        `Invalid production readiness report contract: revenueTaxonomyRanges.${index}.releaseGateCodes - ixbrl-taxonomy-selection is required`,
      );
    }

    if (!range.releaseGateCodes.includes("source-law-change-review")) {
      throw new Error(
        `Invalid production readiness report contract: revenueTaxonomyRanges.${index}.releaseGateCodes - source-law-change-review is required`,
      );
    }
  });
}

function assertAccountantWorkflowWalkthroughProtocol(report: ProductionReadinessReport) {
  const protocol = report.accountantWorkflowWalkthroughProtocol;
  const releaseChecklistCodes = new Set(report.releaseReviewChecklist.map((item) => item.code));
  const expectedScenarioCodes = report.goldenFilingCorpus
    .map((scenario) => scenario.code)
    .sort((left, right) => left.localeCompare(right));
  const protocolScenarioCodes = [...protocol.seededScenarioCodes].sort((left, right) => left.localeCompare(right));

  assertStringArrayEqual(
    "accountantWorkflowWalkthroughProtocol.seededScenarioCodes",
    expectedScenarioCodes,
    protocolScenarioCodes,
  );

  if (!releaseChecklistCodes.has(protocol.signOffGate)) {
    throw new Error(
      "Invalid production readiness report contract: accountantWorkflowWalkthroughProtocol.signOffGate - must reference a release checklist item",
    );
  }

  if (!report.assurancePacket.evidenceItems.includes("accountant-workflow-walkthrough-protocol")) {
    throw new Error(
      "Invalid production readiness report contract: assurancePacket.evidenceItems - accountant-workflow-walkthrough-protocol is required",
    );
  }

  for (const evidence of ["seeded golden corpus walkthrough note", "named qualified-accountant approval", "visual QA screenshot review"]) {
    if (!protocol.requiredEvidence.includes(evidence)) {
      throw new Error(
        `Invalid production readiness report contract: accountantWorkflowWalkthroughProtocol.requiredEvidence - ${evidence} is required`,
      );
    }
  }

  for (const route of ["Dashboard", "Company detail", "Period workspace", "Financial statements", "Filing review", "Production readiness"]) {
    if (!protocol.routeSequence.some((step) => step.includes(route))) {
      throw new Error(
        `Invalid production readiness report contract: accountantWorkflowWalkthroughProtocol.routeSequence - ${route} is required`,
      );
    }
  }

  for (const criterion of ["Micro LTD", "Small abridged LTD", "CLG charity", "Medium/audit-required", "outputs, gates, wording and evidence"]) {
    if (!protocol.acceptanceCriteria.some((item) => item.includes(criterion))) {
      throw new Error(
        `Invalid production readiness report contract: accountantWorkflowWalkthroughProtocol.acceptanceCriteria - ${criterion} is required`,
      );
    }
  }

  if (!protocol.failurePolicy.includes("Block release")) {
    throw new Error(
      "Invalid production readiness report contract: accountantWorkflowWalkthroughProtocol.failurePolicy - must block release when accountant walkthrough evidence is missing",
    );
  }
}

function assertAccountantJourneyAcceptanceChecklist(report: ProductionReadinessReport) {
  const requiredRouteCodes = [
    "dashboard",
    "company-detail",
    "period-workspace",
    "financial-statements",
    "filing-review",
    "production-readiness",
  ];
  const checklist = report.accountantJourneyAcceptanceChecklist;
  const releaseChecklistCodes = new Set(report.releaseReviewChecklist.map((item) => item.code));
  const routeByCode = new Map(report.visualQaCoverage.routes.map((route) => [route.code, route]));
  const expectedScenarioCodes = report.goldenFilingCorpus
    .map((scenario) => scenario.code)
    .sort((left, right) => left.localeCompare(right));
  const checklistByRoute = new Map(checklist.map((item) => [item.routeCode, item]));
  const missingRoutes = requiredRouteCodes.filter((routeCode) => !checklistByRoute.has(routeCode));
  const unexpectedRoutes = checklist
    .map((item) => item.routeCode)
    .filter((routeCode) => !requiredRouteCodes.includes(routeCode))
    .sort((left, right) => left.localeCompare(right));

  if (missingRoutes.length > 0 || unexpectedRoutes.length > 0) {
    throw new Error(
      `Invalid production readiness report contract: accountantJourneyAcceptanceChecklist - missing route acceptance entries: ${missingRoutes.join(", ") || "none"}; unexpected ${unexpectedRoutes.join(", ") || "none"}`,
    );
  }

  if (!report.assurancePacket.evidenceItems.includes("accountant-journey-acceptance-checklist")) {
    throw new Error(
      "Invalid production readiness report contract: assurancePacket.evidenceItems - accountant-journey-acceptance-checklist is required",
    );
  }

  checklist.forEach((item, itemIndex) => {
    const route = routeByCode.get(item.routeCode);
    if (!route) {
      throw new Error(
        `Invalid production readiness report contract: accountantJourneyAcceptanceChecklist.${itemIndex}.routeCode - must reference a visual QA route`,
      );
    }

    if (item.routeKey !== route.routeKey || item.routeLabel !== route.label) {
      throw new Error(
        `Invalid production readiness report contract: accountantJourneyAcceptanceChecklist.${itemIndex}.routeKey - must mirror visual QA route metadata for ${item.routeCode}`,
      );
    }

    assertStringArrayEqual(
      `accountantJourneyAcceptanceChecklist.${itemIndex}.workflowStages`,
      route.workflowStages,
      item.workflowStages,
    );
    assertStringArrayEqual(
      `accountantJourneyAcceptanceChecklist.${itemIndex}.seededScenarioCodes`,
      expectedScenarioCodes,
      [...item.seededScenarioCodes].sort((left, right) => left.localeCompare(right)),
    );

    const expectedArtifactNames = report.visualQaCoverage.artifacts
      .filter((artifact) => artifact.routeCode === item.routeCode)
      .map((artifact) => artifact.fileName)
      .sort((left, right) => left.localeCompare(right));
    const receivedArtifactNames = [...item.visualArtifactNames].sort((left, right) => left.localeCompare(right));
    if (
      expectedArtifactNames.length !== receivedArtifactNames.length ||
      expectedArtifactNames.some((fileName, index) => fileName !== receivedArtifactNames[index])
    ) {
      throw new Error(
        `Invalid production readiness report contract: accountantJourneyAcceptanceChecklist.${itemIndex}.visualArtifactNames - must mirror visual smoke artifacts for ${item.routeCode}`,
      );
    }

    if (!releaseChecklistCodes.has(item.signOffGate)) {
      throw new Error(
        `Invalid production readiness report contract: accountantJourneyAcceptanceChecklist.${itemIndex}.signOffGate - must reference a release checklist item`,
      );
    }

    for (const evidence of ["named qualified-accountant route acceptance", "visual smoke screenshots reviewed", "golden corpus evidence accepted"]) {
      if (!item.requiredEvidence.includes(evidence)) {
        throw new Error(
          `Invalid production readiness report contract: accountantJourneyAcceptanceChecklist.${itemIndex}.requiredEvidence - ${evidence} is required`,
        );
      }
    }

    if (!item.acceptanceCriteria.some((criterion) => criterion.includes(item.routeLabel))) {
      throw new Error(
        `Invalid production readiness report contract: accountantJourneyAcceptanceChecklist.${itemIndex}.acceptanceCriteria - must mention ${item.routeLabel}`,
      );
    }

    if (!item.acceptanceCriteria.some((criterion) => criterion.includes("outputs, gates, wording and evidence"))) {
      throw new Error(
        `Invalid production readiness report contract: accountantJourneyAcceptanceChecklist.${itemIndex}.acceptanceCriteria - outputs, gates, wording and evidence is required`,
      );
    }
  });
}

function assertAccountantWorkflowEvidencePack(report: ProductionReadinessReport) {
  const pack = report.accountantWorkflowEvidencePack;
  const checklist = report.accountantJourneyAcceptanceChecklist;
  const checklistByRoute = new Map(checklist.map((item) => [item.routeCode, item]));
  const releaseChecklistCodes = new Set(report.releaseReviewChecklist.map((item) => item.code));
  const packRouteCodes = pack.map((item) => item.routeCode).sort((left, right) => left.localeCompare(right));
  const checklistRouteCodes = checklist.map((item) => item.routeCode).sort((left, right) => left.localeCompare(right));

  if (!report.assurancePacket.evidenceItems.includes("accountant-workflow-evidence-pack")) {
    throw new Error(
      "Invalid production readiness report contract: assurancePacket.evidenceItems - accountant-workflow-evidence-pack is required",
    );
  }

  assertStringArrayEqual(
    "accountantWorkflowEvidencePack.routeCodes",
    checklistRouteCodes,
    packRouteCodes,
  );

  pack.forEach((item, itemIndex) => {
    const checklistItem = checklistByRoute.get(item.routeCode);
    if (!checklistItem) {
      throw new Error(
        `Invalid production readiness report contract: accountantWorkflowEvidencePack.${itemIndex}.routeCode - must reference accountant journey acceptance checklist`,
      );
    }

    if (item.routeLabel !== checklistItem.routeLabel) {
      throw new Error(
        `Invalid production readiness report contract: accountantWorkflowEvidencePack.${itemIndex}.routeLabel - must mirror accountant journey acceptance checklist for ${item.routeCode}`,
      );
    }

    assertStringArrayEqual(
      `accountantWorkflowEvidencePack.${itemIndex}.workflowStages`,
      checklistItem.workflowStages,
      item.workflowStages,
    );
    assertStringArrayEqual(
      `accountantWorkflowEvidencePack.${itemIndex}.seededScenarioCodes`,
      [...checklistItem.seededScenarioCodes].sort((left, right) => left.localeCompare(right)),
      [...item.seededScenarioCodes].sort((left, right) => left.localeCompare(right)),
    );
    assertStringArrayEqual(
      `accountantWorkflowEvidencePack.${itemIndex}.visualArtifactNames`,
      [...checklistItem.visualArtifactNames].sort((left, right) => left.localeCompare(right)),
      [...item.visualArtifactNames].sort((left, right) => left.localeCompare(right)),
    );

    if (!releaseChecklistCodes.has(item.signOffGate) || item.signOffGate !== checklistItem.signOffGate) {
      throw new Error(
        `Invalid production readiness report contract: accountantWorkflowEvidencePack.${itemIndex}.signOffGate - must mirror a release checklist-backed accountant journey sign-off gate`,
      );
    }

    for (const evidence of ["named qualified-accountant route acceptance", "visual smoke screenshots reviewed", "golden corpus evidence accepted"]) {
      if (!item.requiredEvidence.includes(evidence)) {
        throw new Error(
          `Invalid production readiness report contract: accountantWorkflowEvidencePack.${itemIndex}.requiredEvidence - ${evidence} is required`,
        );
      }
    }

    if (!item.decisionQuestion.includes("outputs, gates, wording and evidence")) {
      throw new Error(
        `Invalid production readiness report contract: accountantWorkflowEvidencePack.${itemIndex}.decisionQuestion - outputs, gates, wording and evidence is required`,
      );
    }

    if (!item.failurePolicy.includes("Block release")) {
      throw new Error(
        `Invalid production readiness report contract: accountantWorkflowEvidencePack.${itemIndex}.failurePolicy - must block release until accountant route acceptance evidence is complete`,
      );
    }
  });
}

function assertVisualQaArtifacts(report: ProductionReadinessReport) {
  const routeByCode = new Map(report.visualQaCoverage.routes.map((route) => [route.code, route]));
  const themes = new Set(report.visualQaCoverage.themes);
  const viewports = new Set(report.visualQaCoverage.viewports.map((viewport) => viewport.name));
  const artifactsByKey = new Map<string, VisualQaArtifact>();
  const routeAuditsByCode = new Map(report.visualQaCoverage.routeAudits.map((audit) => [audit.routeCode, audit]));
  const releaseChecklistCodes = new Set(report.releaseReviewChecklist.map((item) => item.code));
  const reviewProtocol = report.visualQaCoverage.reviewProtocol;

  if (!releaseChecklistCodes.has(reviewProtocol.signOffGate)) {
    throw new Error(
      "Invalid production readiness report contract: visualQaCoverage.reviewProtocol.signOffGate - must reference a release checklist item",
    );
  }

  if (reviewProtocol.acceptanceCriteria.length === 0) {
    throw new Error(
      "Invalid production readiness report contract: visualQaCoverage.reviewProtocol.acceptanceCriteria - at least one criterion is required",
    );
  }

  if (!reviewProtocol.requiredEvidence.includes(report.visualQaCoverage.manifestFileName)) {
    throw new Error(
      "Invalid production readiness report contract: visualQaCoverage.reviewProtocol.requiredEvidence - must include the visual smoke manifest",
    );
  }

  if (!reviewProtocol.requiredEvidence.includes("screenshot SHA-256 checksums")) {
    throw new Error(
      "Invalid production readiness report contract: visualQaCoverage.reviewProtocol.requiredEvidence - must include screenshot SHA-256 checksums",
    );
  }

  report.visualQaCoverage.artifacts.forEach((artifact, artifactIndex) => {
    const route = routeByCode.get(artifact.routeCode);
    if (!route) {
      throw new Error(
        `Invalid production readiness report contract: visualQaCoverage.artifacts.${artifactIndex}.routeCode - must reference a visual QA route`,
      );
    }

    if (!themes.has(artifact.theme)) {
      throw new Error(
        `Invalid production readiness report contract: visualQaCoverage.artifacts.${artifactIndex}.theme - must reference a configured visual QA theme`,
      );
    }

    if (!viewports.has(artifact.viewportName)) {
      throw new Error(
        `Invalid production readiness report contract: visualQaCoverage.artifacts.${artifactIndex}.viewportName - must reference a configured visual QA viewport`,
      );
    }

    const key = visualQaArtifactKey(artifact.routeCode, artifact.theme, artifact.viewportName);
    if (artifactsByKey.has(key)) {
      throw new Error(
        `Invalid production readiness report contract: visualQaCoverage.artifacts.${artifactIndex} - duplicate artifact for ${key}`,
      );
    }
    artifactsByKey.set(key, artifact);

    const expectedFileName = `${artifact.routeCode}-${artifact.theme}-${artifact.viewportName}.png`;
    if (artifact.fileName !== expectedFileName || artifact.artifactPath !== `artifacts/visual-smoke/${expectedFileName}`) {
      throw new Error(
        `Invalid production readiness report contract: visualQaCoverage.artifacts.${artifactIndex}.artifactPath - must match the visual smoke screenshot naming convention`,
      );
    }

    if (artifact.routeKey !== route.routeKey) {
      throw new Error(
        `Invalid production readiness report contract: visualQaCoverage.artifacts.${artifactIndex}.routeKey - must mirror the visual QA route capture key`,
      );
    }

    if (artifact.requiredText !== route.requiredText || artifact.openFilingTab !== route.openFilingTab) {
      throw new Error(
        `Invalid production readiness report contract: visualQaCoverage.artifacts.${artifactIndex}.requiredText - must mirror the visual QA route capture target`,
      );
    }

    if (artifact.reviewStatus !== "required-review") {
      throw new Error(
        `Invalid production readiness report contract: visualQaCoverage.artifacts.${artifactIndex}.reviewStatus - screenshots must require named review`,
      );
    }

    assertStringArrayEqual(
      `visualQaCoverage.artifacts.${artifactIndex}.layoutChecks`,
      report.visualQaCoverage.layoutChecks,
      artifact.layoutChecks,
    );
  });

  report.visualQaCoverage.routes.forEach((route) => {
    if (!routeAuditsByCode.has(route.code)) {
      throw new Error(
        `Invalid production readiness report contract: visualQaCoverage.routeAudits - missing route audit for ${route.code}`,
      );
    }
  });

  report.visualQaCoverage.routeAudits.forEach((audit, auditIndex) => {
    const route = routeByCode.get(audit.routeCode);

    if (!route) {
      throw new Error(
        `Invalid production readiness report contract: visualQaCoverage.routeAudits.${auditIndex}.routeCode - must reference a visual QA route`,
      );
    }

    if (audit.routeKey !== route.routeKey || audit.label !== route.label) {
      throw new Error(
        `Invalid production readiness report contract: visualQaCoverage.routeAudits.${auditIndex}.routeKey - must mirror the visual QA route metadata`,
      );
    }

    assertExpectedNumber(
      `visualQaCoverage.routeAudits.${auditIndex}.screenshotCount`,
      report.visualQaCoverage.themes.length * report.visualQaCoverage.viewports.length,
      audit.screenshotCount,
    );
    assertStringArrayEqual(
      `visualQaCoverage.routeAudits.${auditIndex}.workflowStages`,
      route.workflowStages,
      audit.workflowStages,
    );
    assertStringArrayEqual(
      `visualQaCoverage.routeAudits.${auditIndex}.reviewChecks`,
      report.visualQaCoverage.reviewChecks,
      audit.reviewChecks,
    );

    if (audit.reviewStatus !== "required-review") {
      throw new Error(
        `Invalid production readiness report contract: visualQaCoverage.routeAudits.${auditIndex}.reviewStatus - route audits must require named review`,
      );
    }
  });

  report.visualQaCoverage.routes.forEach((route) => {
    report.visualQaCoverage.themes.forEach((theme) => {
      report.visualQaCoverage.viewports.forEach((viewport) => {
        const key = visualQaArtifactKey(route.code, theme, viewport.name);
        if (!artifactsByKey.has(key)) {
          throw new Error(
            `Invalid production readiness report contract: visualQaCoverage.artifacts - missing screenshot artifact for ${key}`,
          );
        }
      });
    });
  });
}

function visualQaArtifactKey(routeCode: string, theme: string, viewportName: string) {
  return `${routeCode}/${theme}/${viewportName}`;
}

function assertCompletionTracks(report: ProductionReadinessReport) {
  const expectedCodes = ["backend-code", "frontend-ui-ux", "frontend-code"];
  const actualCodes = report.completionTracks.map((track) => track.code);
  const missingCodes = expectedCodes.filter((code) => !actualCodes.includes(code));
  const duplicateCodes = actualCodes.filter((code, index) => actualCodes.indexOf(code) !== index);
  const assuranceActionCodes = new Set(report.assuranceActions.map((action) => action.code));

  if (missingCodes.length > 0) {
    throw new Error(
      `Invalid production readiness report contract: completionTracks - missing required tracks: ${missingCodes.join(", ")}`,
    );
  }

  if (duplicateCodes.length > 0) {
    throw new Error(
      `Invalid production readiness report contract: completionTracks - duplicate track codes: ${[...new Set(duplicateCodes)].join(", ")}`,
    );
  }

  report.completionTracks.forEach((track, trackIndex) => {
    if (track.completionCriteria.length === 0) {
      throw new Error(
        `Invalid production readiness report contract: completionTracks.${trackIndex}.completionCriteria - at least one completion criterion is required`,
      );
    }

    if (track.currentEvidence.length === 0) {
      throw new Error(
        `Invalid production readiness report contract: completionTracks.${trackIndex}.currentEvidence - at least one current evidence item is required`,
      );
    }

    if (track.nextActions.length === 0) {
      throw new Error(
        `Invalid production readiness report contract: completionTracks.${trackIndex}.nextActions - at least one next action is required`,
      );
    }

    track.assuranceActionCodes.forEach((code) => {
      if (!assuranceActionCodes.has(code)) {
        throw new Error(
          `Invalid production readiness report contract: completionTracks.${trackIndex}.assuranceActionCodes - unknown assurance action ${code}`,
        );
      }
    });
  });
}

function assertReleaseBlockerRegister(report: ProductionReadinessReport) {
  const trackCodes = new Set(report.completionTracks.map((track) => track.code));
  const trackLabelsByCode = new Map(report.completionTracks.map((track) => [track.code, track.label]));
  const trackActionCodesByTrack = new Map(report.completionTracks.map((track) => [track.code, new Set(track.assuranceActionCodes)]));
  const actionCodes = new Set(report.assuranceActions.map((action) => action.code));
  const actionStatusesByCode = new Map(report.assuranceActions.map((action) => [action.code, action.status]));
  const checklistByCode = new Map(report.releaseReviewChecklist.map((item) => [item.code, item]));
  const blockerIssues = new Set<string>();

  report.releaseBlockerRegister.forEach((blocker, blockerIndex) => {
    if (!trackCodes.has(blocker.trackCode)) {
      throw new Error(
        `Invalid production readiness report contract: releaseBlockerRegister.${blockerIndex}.trackCode - must reference a completion track`,
      );
    }

    if (trackLabelsByCode.get(blocker.trackCode) !== blocker.trackLabel) {
      throw new Error(
        `Invalid production readiness report contract: releaseBlockerRegister.${blockerIndex}.trackLabel - must mirror the completion track label`,
      );
    }

    if (!actionCodes.has(blocker.sourceActionCode)) {
      throw new Error(
        `Invalid production readiness report contract: releaseBlockerRegister.${blockerIndex}.sourceActionCode - must reference an assurance action`,
      );
    }

    if (!trackActionCodesByTrack.get(blocker.trackCode)?.has(blocker.sourceActionCode)) {
      throw new Error(
        `Invalid production readiness report contract: releaseBlockerRegister.${blockerIndex}.sourceActionCode - must be owned by the referenced completion track`,
      );
    }

    const actionStatus = actionStatusesByCode.get(blocker.sourceActionCode);
    if (actionStatus === "complete") {
      throw new Error(
        `Invalid production readiness report contract: releaseBlockerRegister.${blockerIndex}.sourceActionCode - completed actions must not remain release blockers`,
      );
    }

    const checklistItem = checklistByCode.get(blocker.releaseChecklistCode);
    if (!checklistItem) {
      throw new Error(
        `Invalid production readiness report contract: releaseBlockerRegister.${blockerIndex}.releaseChecklistCode - must reference a release checklist item`,
      );
    }

    if (
      checklistItem.assuranceActionCode !== blocker.sourceActionCode
      || checklistItem.evidenceArtifact !== blocker.evidenceArtifact
      || checklistItem.operationalGateCode !== blocker.operationalGateCode
    ) {
      throw new Error(
        `Invalid production readiness report contract: releaseBlockerRegister.${blockerIndex}.releaseChecklistCode - must mirror linked release checklist controls`,
      );
    }

    if (checklistItem.blocksRelease !== blocker.blocksRelease) {
      throw new Error(
        `Invalid production readiness report contract: releaseBlockerRegister.${blockerIndex}.blocksRelease - must mirror linked release checklist blocking state`,
      );
    }

    blockerIssues.add(blocker.blockingIssue);
  });

  const missingPacketBlockers = report.assurancePacket.releaseBlockers
    .filter((blocker) => !blockerIssues.has(blocker))
    .sort();
  const unexpectedRegisterBlockers = [...blockerIssues]
    .filter((blocker) => !report.assurancePacket.releaseBlockers.includes(blocker))
    .sort();

  if (missingPacketBlockers.length > 0 || unexpectedRegisterBlockers.length > 0) {
    throw new Error(
      `Invalid production readiness report contract: releaseBlockerRegister - must cover every assurance packet release blocker; missing ${missingPacketBlockers.join(", ") || "none"}, unexpected ${unexpectedRegisterBlockers.join(", ") || "none"}`,
    );
  }
}

function assertReleaseReviewChecklist(report: ProductionReadinessReport) {
  const assuranceActionCodes = new Set(report.assuranceActions.map((action) => action.code));
  const operationalGateCodes = new Set(report.operationalGates.map((gate) => gate.code));
  const checklistActionCodes = new Set<string>();

  report.releaseReviewChecklist.forEach((item, itemIndex) => {
    if (!assuranceActionCodes.has(item.assuranceActionCode)) {
      throw new Error(
        `Invalid production readiness report contract: releaseReviewChecklist.${itemIndex}.assuranceActionCode - must reference a known assurance action`,
      );
    }

    if (item.operationalGateCode.trim() && !operationalGateCodes.has(item.operationalGateCode)) {
      throw new Error(
        `Invalid production readiness report contract: releaseReviewChecklist.${itemIndex}.operationalGateCode - must reference a known operational gate`,
      );
    }

    checklistActionCodes.add(item.assuranceActionCode);
  });

  const missingActions = [...assuranceActionCodes]
    .filter((code) => !checklistActionCodes.has(code))
    .sort();

  if (missingActions.length > 0) {
    throw new Error(
      `Invalid production readiness report contract: releaseReviewChecklist - missing checklist items for assurance actions: ${missingActions.join(", ")}`,
    );
  }
}

function assertReleaseVerificationManifest(report: ProductionReadinessReport) {
  const checklistEvidenceArtifacts = new Set(report.releaseReviewChecklist.map((item) => item.evidenceArtifact));
  const validScopes = new Set(["default-ci", "environment-gated", "manual-release"]);
  const manifestCodes = new Set<string>();
  const manifestChecklistEvidenceArtifacts = new Set<string>();

  report.releaseVerificationManifest.forEach((item, itemIndex) => {
    if (manifestCodes.has(item.code)) {
      throw new Error(
        `Invalid production readiness report contract: releaseVerificationManifest.${itemIndex}.code - duplicate manifest code`,
      );
    }
    manifestCodes.add(item.code);

    if (!validScopes.has(item.ciScope)) {
      throw new Error(
        `Invalid production readiness report contract: releaseVerificationManifest.${itemIndex}.ciScope - must be default-ci, environment-gated or manual-release`,
      );
    }

    if (!checklistEvidenceArtifacts.has(item.releaseChecklistEvidenceArtifact)) {
      throw new Error(
        `Invalid production readiness report contract: releaseVerificationManifest.${itemIndex}.releaseChecklistEvidenceArtifact - must reference release checklist evidence`,
      );
    }
    manifestChecklistEvidenceArtifacts.add(item.releaseChecklistEvidenceArtifact);

    if (item.blocksRelease && !item.manualFallback.trim()) {
      throw new Error(
        `Invalid production readiness report contract: releaseVerificationManifest.${itemIndex}.manualFallback - blocking checks need a manual fallback`,
      );
    }
  });

  if (report.releaseVerificationManifest.length === 0) {
    throw new Error(
      "Invalid production readiness report contract: releaseVerificationManifest - at least one verification command is required",
    );
  }

  const missingBlockingChecklistEvidence = report.releaseReviewChecklist
    .filter((item) => item.blocksRelease)
    .map((item) => item.evidenceArtifact)
    .filter((artifact) => !manifestChecklistEvidenceArtifacts.has(artifact))
    .sort();

  if (missingBlockingChecklistEvidence.length > 0) {
    throw new Error(
      `Invalid production readiness report contract: releaseVerificationManifest - missing verification coverage for blocking checklist evidence: ${missingBlockingChecklistEvidence.join(", ")}`,
    );
  }
}

function assertAssuranceActionsRiskOrder(actions: ProductionReadinessAssuranceAction[]) {
  actions.forEach((action, actionIndex) => {
    if (!action.evidenceStage.trim()) {
      throw new Error(
        `Invalid production readiness report contract: assuranceActions.${actionIndex}.evidenceStage - evidence stage is required`,
      );
    }

    if (actionIndex === 0) return;

    const previous = actions[actionIndex - 1];
    const outOfRiskOrder =
      action.riskRank < previous.riskRank ||
      (action.riskRank === previous.riskRank && action.code.localeCompare(previous.code, "en-IE") < 0);

    if (outOfRiskOrder) {
      throw new Error(
        `Invalid production readiness report contract: assuranceActions.${actionIndex}.riskRank - actions must be sorted by riskRank then code`,
      );
    }
  });
}

function assertStringArrayEqual(path: string, expected: string[], received: string[]) {
  if (expected.length !== received.length || expected.some((value, index) => value !== received[index])) {
    throw new Error(
      `Invalid production readiness report contract: ${path} - expected ${expected.join(", ")}, received ${received.join(", ")}`,
    );
  }
}

function assertExpectedNumber(path: string, expected: number, received: number) {
  if (expected !== received) {
    throw new Error(
      `Invalid production readiness report contract: ${path} - expected ${expected}, received ${received}`,
    );
  }
}

export interface RevenueIxbrlTaxonomySelection {
  taxonomyKey: string;
  taxonomyDate: string;
  label: string;
  schemaRef: string;
  acceptedByRevenue: boolean;
  effectiveForPeriodsStartingOnOrAfter: string;
  sources: LegalSourceReference[];
}

export interface FilingReadinessEvidenceItem {
  code: string;
  label: string;
  required: boolean;
  satisfied: boolean;
  detail?: string;
  sources: LegalSourceReference[];
}

export interface FilingReadinessIssue {
  code: string;
  severity: "blocking" | "warning" | string;
  message: string;
  sources: LegalSourceReference[];
}

export interface FilingReadinessSignOffStep {
  code: string;
  label: string;
  state: string;
  detail: string;
  sources: LegalSourceReference[];
}

export interface FilingReadinessSignOffPacket {
  state: string;
  stateLabel: string;
  readyForAccountantApproval: boolean;
  readyForExternalFiling: boolean;
  approvedBy?: string;
  approvedAt?: string;
  steps: FilingReadinessSignOffStep[];
  openBlockers: string[];
  openWarnings: string[];
  allowedNextActions: string[];
}

export interface FilingReadinessProfile {
  companyId: number;
  periodId: number;
  companyType: string;
  sizeClass?: string;
  electedRegime?: string;
  auditExempt?: boolean;
  supportedPath: boolean;
  manualProfessionalReviewRequired: boolean;
  accountantReviewRequired: boolean;
  accountantReviewState: string;
  directCroSubmissionSupported: boolean;
  directRosSubmissionSupported: boolean;
  revenueTaxonomy: RevenueIxbrlTaxonomySelection;
  signOffPacket: FilingReadinessSignOffPacket;
  requiredEvidence: FilingReadinessEvidenceItem[];
  blockingIssues: FilingReadinessIssue[];
  warningIssues: FilingReadinessIssue[];
  sourceReferences: LegalSourceReference[];
  allowedNextActions: string[];
}

export interface AuditLogEntry {
  id: number;
  companyId?: number;
  periodId?: number;
  entityType: string;
  entityId: number;
  action: string;
  oldValueJson?: string;
  newValueJson?: string;
  userId?: string;
  timestamp: string;
}

export interface CroFilingStatus {
  status: string;
  accountsPdfReady: boolean;
  signaturePageReady: boolean;
  paymentCompleted: boolean;
  submissionReference?: string;
  rejectionReason?: string;
  correctionDeadline?: string;
}

export interface RevenueFilingStatus {
  status: string;
  ixbrlReady: boolean;
  ixbrlInternalChecksPassed: boolean;
  ixbrlValid: boolean;
  validationErrors?: string;
  ct1Reference?: string;
}

export interface CharityFilingStatus {
  status: string;
  sofaGenerated: boolean;
  trusteesReportGenerated: boolean;
  annualReturnReference?: string;
  rejectionReason?: string;
  correctionDeadline?: string;
  submittedBy?: string;
  submittedAt?: string;
  acceptedBy?: string;
  acceptedAt?: string;
}

export const getFilingWorkflowStatus = (companyId: number, periodId: number) =>
  apiFetch<FilingWorkflowStatus>(`/api/companies/${companyId}/periods/${periodId}/filing/status`);

export const getFilingReadinessProfile = (companyId: number, periodId: number) =>
  apiFetch<FilingReadinessProfile>(`/api/companies/${companyId}/periods/${periodId}/filing/readiness-profile`);

export const getProductionReadinessReport = async () =>
  parseProductionReadinessReport(await apiFetch<unknown>("/api/system/production-readiness"));

export const updateCroFilingStatus = (
  companyId: number,
  periodId: number,
  data: { status: string; reason?: string; submissionReference?: string }
) =>
  apiFetch<unknown>(`/api/companies/${companyId}/periods/${periodId}/filing/cro-status`, { method: "PUT", body: JSON.stringify(data) });

export const confirmCroPayment = (companyId: number, periodId: number) =>
  apiFetch<unknown>(`/api/companies/${companyId}/periods/${periodId}/filing/cro-payment`, { method: "POST", body: JSON.stringify({}) });

export const validateIxbrl = (companyId: number, periodId: number) =>
  apiFetch<RevenueFilingStatus>(`/api/companies/${companyId}/periods/${periodId}/filing/validate-ixbrl`, { method: "POST" });

export const recordCharityReportGenerated = (companyId: number, periodId: number, reportType: string) =>
  apiFetch<CharityFilingStatus>(
    `/api/companies/${companyId}/periods/${periodId}/filing/charity-report-generated`,
    { method: "POST", body: JSON.stringify({ reportType }) },
  );

export const updateCharityFilingStatus = (
  companyId: number,
  periodId: number,
  data: { status: string; reason?: string; annualReturnReference?: string },
) =>
  apiFetch<unknown>(
    `/api/companies/${companyId}/periods/${periodId}/filing/charity-status`,
    { method: "PUT", body: JSON.stringify(data) },
  );

export const getAuditLog = (companyId: number, periodId?: number, page = 1, pageSize = 20) => {
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (periodId) params.set("periodId", String(periodId));
  return apiFetch<{ total: number; items: AuditLogEntry[] }>(`/api/companies/${companyId}/audit-log?${params}`);
};
