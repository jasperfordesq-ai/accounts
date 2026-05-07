const API_BASE = process.env.NEXT_PUBLIC_API_URL || "";

// --- Core fetch with retry, timeout, structured errors ---

class ApiError extends Error {
  constructor(
    public status: number,
    public statusText: string,
    public body: string,
  ) {
    super(ApiError.formatMessage(status, body));
    this.name = "ApiError";
  }

  static formatMessage(status: number, body: string): string {
    // Try to extract a meaningful message from the response body
    try {
      const parsed = JSON.parse(body);
      if (parsed.message) return parsed.message;
      if (parsed.title) return parsed.title;
      if (parsed.detail) return parsed.detail;
      if (typeof parsed === "string") return parsed;
    } catch {
      // Not JSON — use the raw body if short enough
    }
    if (body && body.length < 200) return body;

    // Fallback to status-based messages
    switch (status) {
      case 400: return "Invalid request. Please check your input.";
      case 401: return "Not authenticated. Please sign in.";
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

async function apiFetch<T>(
  path: string,
  options?: RequestInit & { timeout?: number; retries?: number },
): Promise<T> {
  const { timeout = DEFAULT_TIMEOUT, retries = MAX_RETRIES, ...fetchOptions } = options ?? {};

  let lastError: Error | null = null;

  for (let attempt = 0; attempt <= retries; attempt++) {
    try {
      const controller = new AbortController();
      const timeoutId = setTimeout(() => controller.abort(), timeout);

      const res = await fetch(`${API_BASE}${path}`, {
        headers: { "Content-Type": "application/json", ...fetchOptions?.headers },
        signal: controller.signal,
        ...fetchOptions,
      });

      clearTimeout(timeoutId);

      if (!res.ok) {
        const body = await res.text().catch(() => "");
        const error = new ApiError(res.status, res.statusText, body);

        // Only retry on server errors and rate limits (not client errors)
        if (error.isRetryable && attempt < retries) {
          lastError = error;
          await new Promise((r) => setTimeout(r, RETRY_DELAYS[attempt] ?? 1500));
          continue;
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
        if (attempt < retries) {
          await new Promise((r) => setTimeout(r, RETRY_DELAYS[attempt] ?? 1500));
          continue;
        }
        throw lastError;
      }

      // Handle network errors
      if (err instanceof TypeError && err.message.includes("fetch")) {
        lastError = new Error("Network error. Please check your connection.");
        if (attempt < retries) {
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
  data: { debit: number; credit: number; sourceNote?: string; enteredBy?: string; reviewed: boolean },
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
  data: { confirmed: boolean; confirmedBy?: string; note?: string },
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
  approvedBy: string,
) =>
  apiFetch<Adjustment>(
    `/api/companies/${companyId}/periods/${periodId}/adjustments/${id}/approve`,
    { method: "POST", body: JSON.stringify({ approvedBy }) },
  );

// Import
export const uploadBankCsv = async (
  companyId: number,
  bankAccountId: number,
  periodId: number,
  file: File,
  reviewer?: string,
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
        signal: controller.signal,
        headers: reviewer ? { "X-Reviewer": reviewer } : undefined,
      },
    );
    clearTimeout(timeoutId);
    if (!res.ok) {
      const body = await res.text().catch(() => "");
      throw new ApiError(res.status, res.statusText, body);
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
  data: { deadlineType: string; filedDate: string },
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
  blockingIssues: string[];
  warningIssues: string[];
  readyToFile: boolean;
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
  ixbrlValid: boolean;
  validationErrors?: string;
  ct1Reference?: string;
}

export const getFilingWorkflowStatus = (companyId: number, periodId: number) =>
  apiFetch<FilingWorkflowStatus>(`/api/companies/${companyId}/periods/${periodId}/filing/status`);

export const updateCroFilingStatus = (
  companyId: number,
  periodId: number,
  data: { status: string; by?: string; reason?: string; submissionReference?: string }
) =>
  apiFetch<unknown>(`/api/companies/${companyId}/periods/${periodId}/filing/cro-status`, { method: "PUT", body: JSON.stringify(data) });

export const markDocumentGenerated = (companyId: number, periodId: number, documentType: string) =>
  apiFetch<unknown>(`/api/companies/${companyId}/periods/${periodId}/filing/mark-generated`, { method: "POST", body: JSON.stringify({ documentType }) });

export const confirmCroPayment = (companyId: number, periodId: number, by?: string) =>
  apiFetch<unknown>(`/api/companies/${companyId}/periods/${periodId}/filing/cro-payment`, { method: "POST", body: JSON.stringify({ by }) });

export const validateIxbrl = (companyId: number, periodId: number) =>
  apiFetch<RevenueFilingStatus>(`/api/companies/${companyId}/periods/${periodId}/filing/validate-ixbrl`, { method: "POST" });

export const getAuditLog = (companyId: number, periodId?: number, page = 1, pageSize = 20) => {
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (periodId) params.set("periodId", String(periodId));
  return apiFetch<{ total: number; items: AuditLogEntry[] }>(`/api/companies/${companyId}/audit-log?${params}`);
};
