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

// Debtors
export interface Debtor { id?: number; periodId?: number; name: string; amount: number; type: string; notes?: string; }
export const getDebtors = (cId: number, pId: number) => apiFetch<Debtor[]>(`/api/companies/${cId}/periods/${pId}/debtors`);
export const createDebtor = (cId: number, pId: number, d: Debtor) => apiFetch<Debtor>(`/api/companies/${cId}/periods/${pId}/debtors`, { method: "POST", body: JSON.stringify(d) });
export const deleteDebtor = (cId: number, pId: number, id: number) => apiFetch<void>(`/api/companies/${cId}/periods/${pId}/debtors/${id}`, { method: "DELETE" });

// Creditors
export interface Creditor { id?: number; periodId?: number; name: string; amount: number; type: string; dueWithinYear: boolean; notes?: string; }
export const getCreditors = (cId: number, pId: number) => apiFetch<Creditor[]>(`/api/companies/${cId}/periods/${pId}/creditors`);
export const createCreditor = (cId: number, pId: number, c: Creditor) => apiFetch<Creditor>(`/api/companies/${cId}/periods/${pId}/creditors`, { method: "POST", body: JSON.stringify(c) });
export const deleteCreditor = (cId: number, pId: number, id: number) => apiFetch<void>(`/api/companies/${cId}/periods/${pId}/creditors/${id}`, { method: "DELETE" });

// Fixed Assets
export interface FixedAsset { id?: number; companyId?: number; name: string; category: string; cost: number; acquisitionDate: string; disposalDate?: string; disposalProceeds?: number; usefulLifeYears: number; depreciationMethod: string; }
export const getFixedAssets = (cId: number) => apiFetch<FixedAsset[]>(`/api/companies/${cId}/fixed-assets`);
export const createFixedAsset = (cId: number, a: FixedAsset) => apiFetch<FixedAsset>(`/api/companies/${cId}/fixed-assets`, { method: "POST", body: JSON.stringify(a) });
export const deleteFixedAsset = (cId: number, id: number) => apiFetch<void>(`/api/companies/${cId}/fixed-assets/${id}`, { method: "DELETE" });

// Payroll
export interface PayrollSummary { id?: number; periodId?: number; grossWages: number; employerPrsi: number; pensionContributions: number; staffCount: number; }
export const getPayroll = (cId: number, pId: number) => apiFetch<PayrollSummary>(`/api/companies/${cId}/periods/${pId}/payroll`).catch(() => null);
export const savePayroll = (cId: number, pId: number, p: PayrollSummary) => apiFetch<PayrollSummary>(`/api/companies/${cId}/periods/${pId}/payroll`, { method: "PUT", body: JSON.stringify(p) });

// Tax Balances
export interface TaxBalance { id?: number; periodId?: number; taxType: string; liability: number; paid: number; balance: number; }
export const getTaxBalances = (cId: number, pId: number) => apiFetch<TaxBalance[]>(`/api/companies/${cId}/periods/${pId}/tax-balances`);
export const saveTaxBalance = (cId: number, pId: number, taxType: string, t: TaxBalance) => apiFetch<TaxBalance>(`/api/companies/${cId}/periods/${pId}/tax-balances/${taxType}`, { method: "PUT", body: JSON.stringify(t) });

// Dividends
export interface Dividend { id?: number; periodId?: number; amount: number; dateDeclared?: string; datePaid?: string; }
export const getDividends = (cId: number, pId: number) => apiFetch<Dividend[]>(`/api/companies/${cId}/periods/${pId}/dividends`);
export const createDividend = (cId: number, pId: number, d: Dividend) => apiFetch<Dividend>(`/api/companies/${cId}/periods/${pId}/dividends`, { method: "POST", body: JSON.stringify(d) });
export const deleteDividend = (cId: number, pId: number, id: number) => apiFetch<void>(`/api/companies/${cId}/periods/${pId}/dividends/${id}`, { method: "DELETE" });

// Inventory
export interface InventoryItem { id?: number; periodId?: number; description: string; value: number; valuationMethod: string; }
export const getInventory = (cId: number, pId: number) => apiFetch<InventoryItem[]>(`/api/companies/${cId}/periods/${pId}/inventory`);
export const createInventory = (cId: number, pId: number, i: InventoryItem) => apiFetch<InventoryItem>(`/api/companies/${cId}/periods/${pId}/inventory`, { method: "POST", body: JSON.stringify(i) });
export const deleteInventory = (cId: number, pId: number, id: number) => apiFetch<void>(`/api/companies/${cId}/periods/${pId}/inventory/${id}`, { method: "DELETE" });

// Size Classification
export const saveSizeClassification = (cId: number, pId: number, data: { turnover: number; balanceSheetTotal: number; avgEmployees: number; priorYearClass?: string }) =>
  apiFetch<unknown>(`/api/companies/${cId}/periods/${pId}/size-classification`, { method: "PUT", body: JSON.stringify(data) });
export const runClassification = (cId: number, pId: number) =>
  apiFetch<{ calculatedClass: string; qualificationNotes: string; canUseMicro: boolean; canFileAbridged: boolean; auditExempt: boolean; availableRegimes: string[] }>(`/api/companies/${cId}/periods/${pId}/classify`, { method: "POST" });
export const setFilingRegime = (cId: number, pId: number, electedRegime?: string) =>
  apiFetch<unknown>(`/api/companies/${cId}/periods/${pId}/filing-regime`, { method: "POST", body: JSON.stringify({ electedRegime }) });

// Notes
export interface NotesDisclosure { id?: number; periodId?: number; noteNumber: number; title: string; content?: string; isRequired: boolean; isIncluded: boolean; }
export const getNotes = (cId: number, pId: number) => apiFetch<NotesDisclosure[]>(`/api/companies/${cId}/periods/${pId}/notes`);
export const generateNotes = (cId: number, pId: number) => apiFetch<NotesDisclosure[]>(`/api/companies/${cId}/periods/${pId}/notes/generate`, { method: "POST" });
export const updateNote = (cId: number, pId: number, id: number, data: Partial<NotesDisclosure>) =>
  apiFetch<NotesDisclosure>(`/api/companies/${cId}/periods/${pId}/notes/${id}`, { method: "PUT", body: JSON.stringify(data) });
export const createNote = (cId: number, pId: number, data: Partial<NotesDisclosure>) =>
  apiFetch<NotesDisclosure>(`/api/companies/${cId}/periods/${pId}/notes`, { method: "POST", body: JSON.stringify(data) });
export const deleteNote = (cId: number, pId: number, id: number) =>
  apiFetch<void>(`/api/companies/${cId}/periods/${pId}/notes/${id}`, { method: "DELETE" });

// Statements
export interface TrialBalanceLine { code: string; name: string; type: string; debit: number; credit: number; }
export interface ProfitAndLoss { turnover: number; costOfSales: number; grossProfit: number; overheads: { code: string; name: string; amount: number }[]; totalOverheads: number; operatingProfit: number; interestPayable: number; profitBeforeTax: number; taxCharge: number; profitAfterTax: number; }
export interface BalanceSheet { fixedAssets: { categories: { category: string; cost: number; depreciation: number; nbv: number }[]; total: number }; currentAssets: { stock: number; debtors: number; prepayments: number; cash: number; total: number }; creditorsWithinYear: { tradeCreditors: number; accruals: number; taxCreditors: number; otherCreditors: number; total: number }; netCurrentAssets: number; totalAssetsLessCurrentLiabilities: number; creditorsAfterYear: { loans: number; other: number; total: number }; netAssets: number; capitalAndReserves: { shareCapital: number; retainedEarnings: number; total: number }; balances: boolean; }
export interface TaxComputation { accountingProfit: number; adjustments: { description: string; amount: number; basis: string }[]; taxableProfit: number; corporationTaxAt125: number; corporationTaxAt25: number; totalCorporationTax: number; preliminaryTaxPaid: number; balanceDue: number; notes: string; }

export const getTrialBalance = (cId: number, pId: number) => apiFetch<TrialBalanceLine[]>(`/api/companies/${cId}/periods/${pId}/statements/trial-balance`);
export const getProfitAndLoss = (cId: number, pId: number) => apiFetch<ProfitAndLoss>(`/api/companies/${cId}/periods/${pId}/statements/profit-and-loss`);
export const getBalanceSheet = (cId: number, pId: number) => apiFetch<BalanceSheet>(`/api/companies/${cId}/periods/${pId}/statements/balance-sheet`);
export const getTaxComputation = (cId: number, pId: number) => apiFetch<TaxComputation>(`/api/companies/${cId}/periods/${pId}/revenue/tax-computation`);
