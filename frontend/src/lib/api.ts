const API_BASE = process.env.NEXT_PUBLIC_API_URL || "http://localhost:5090";

async function apiFetch<T>(path: string, options?: RequestInit): Promise<T> {
  const res = await fetch(`${API_BASE}${path}`, {
    headers: { "Content-Type": "application/json", ...options?.headers },
    ...options,
  });
  if (!res.ok) {
    const text = await res.text().catch(() => "Unknown error");
    throw new Error(`API error ${res.status}: ${text}`);
  }
  if (res.status === 204) return undefined as T;
  return res.json();
}

// === Companies ===
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
  completeness: { score: number; completed: number; total: number; incomplete: string[] };
}

export interface ReadinessScore {
  completenessPercent: number;
  filingReadinessPercent: number;
  balanceSheetBalances: boolean;
  missingItems: string[];
  warnings: string[];
}

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
  apiFetch<Officer>(`/api/companies/${companyId}/officers`, { method: "POST", body: JSON.stringify(data) });

// Periods
export const getPeriods = (companyId: number) =>
  apiFetch<AccountingPeriod[]>(`/api/companies/${companyId}/periods`);
export const getPeriod = (companyId: number, id: number) =>
  apiFetch<AccountingPeriod>(`/api/companies/${companyId}/periods/${id}`);
export const createPeriod = (companyId: number, data: Partial<AccountingPeriod>) =>
  apiFetch<AccountingPeriod>(`/api/companies/${companyId}/periods`, { method: "POST", body: JSON.stringify(data) });

// Bank Accounts
export const getBankAccounts = (companyId: number) =>
  apiFetch<BankAccount[]>(`/api/companies/${companyId}/bank-accounts`);

// Transactions
export const getTransactions = (companyId: number, periodId: number, page = 1) =>
  apiFetch<{ total: number; items: ImportedTransaction[] }>(
    `/api/companies/${companyId}/periods/${periodId}/transactions?page=${page}&pageSize=50`
  );

// Categories
export const getCategories = (companyId: number) =>
  apiFetch<AccountCategory[]>(`/api/companies/${companyId}/categories`);
export const seedCategories = (companyId: number) =>
  apiFetch<AccountCategory[]>(`/api/companies/${companyId}/categories/seed`, { method: "POST" });

// Year-End
export const getYearEndSummary = (companyId: number, periodId: number) =>
  apiFetch<YearEndSummary>(`/api/companies/${companyId}/periods/${periodId}/year-end-summary`);

// Adjustments
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

export const getAdjustments = (companyId: number, periodId: number) =>
  apiFetch<Adjustment[]>(`/api/companies/${companyId}/periods/${periodId}/adjustments`);
export const getAdjustmentSummary = (companyId: number, periodId: number) =>
  apiFetch<AdjustmentSummary>(`/api/companies/${companyId}/periods/${periodId}/adjustments/summary`);
export const generateAdjustments = (companyId: number, periodId: number) =>
  apiFetch<AdjustmentSummary>(`/api/companies/${companyId}/periods/${periodId}/adjustments/generate`, { method: "POST" });
export const approveAdjustment = (companyId: number, periodId: number, id: number, approvedBy: string) =>
  apiFetch<Adjustment>(`/api/companies/${companyId}/periods/${periodId}/adjustments/${id}/approve`, { method: "POST", body: JSON.stringify({ approvedBy }) });

// Import
export const uploadBankCsv = async (companyId: number, bankAccountId: number, periodId: number, file: File) => {
  const formData = new FormData();
  formData.append("file", file);
  const res = await fetch(
    `${API_BASE}/api/companies/${companyId}/bank-accounts/${bankAccountId}/import?periodId=${periodId}`,
    { method: "POST", body: formData }
  );
  if (!res.ok) throw new Error(`Import failed: ${res.status}`);
  return res.json();
};

// Statements
export const getReadiness = (companyId: number, periodId: number) =>
  apiFetch<ReadinessScore>(`/api/companies/${companyId}/periods/${periodId}/statements/readiness`);

// Documents
export const getAccountsPackageUrl = (companyId: number, periodId: number) =>
  `${API_BASE}/api/companies/${companyId}/periods/${periodId}/documents/accounts-package`;
export const getIxbrlUrl = (companyId: number, periodId: number) =>
  `${API_BASE}/api/companies/${companyId}/periods/${periodId}/revenue/ixbrl`;
