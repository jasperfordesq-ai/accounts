import { z } from "zod";
import * as apiContracts from "./apiContracts.ts";
import {
  reportClientMonitoringEvent,
  type ClientMonitoringEventCode,
} from "./clientMonitoring.ts";
import {
  croHandoffSnapshotRequestSchema,
  externalFilingAuthorityRequestSchema,
  externalFilingAuthorityRevocationRequestSchema,
  externalFilingAuthoritySchema,
  externalFilingHandoffWorkspaceSchema,
  externalFilingOutcomeRequestSchema,
  externalFilingOutcomeSchema,
  externalFilingSnapshotSchema,
  revenueHandoffSnapshotRequestSchema,
  type CroHandoffSnapshotRequest,
  type ExternalFilingAuthority,
  type ExternalFilingAuthorityRequest,
  type ExternalFilingAuthorityRevocationRequest,
  type ExternalFilingHandoffWorkspace,
  type ExternalFilingOutcome,
  type ExternalFilingOutcomeRequest,
  type ExternalFilingSnapshot,
  type RevenueHandoffSnapshotRequest,
} from "./externalFilingHandoff.ts";
import {
  parseProductionReadinessReport,
  type LegalSourceReference,
} from "./productionReadinessContract.ts";

export * from "./productionReadinessContract.ts";

export { ApiContractError } from "./apiContracts.ts";

const API_BASE = "";
const ACCOUNTS_CSRF_COOKIE = "accounts_csrf";
const CSRF_HEADER = "X-CSRF-Token";
const IDEMPOTENCY_HEADER = "Idempotency-Key";
export const SESSION_EXPIRED_EVENT = "accounts:session-expired";

let idempotencyFallbackCounter = 0;

export function createIdempotencyKey(scope = "command"): string {
  const normalizedScope = scope.replace(/[^A-Za-z0-9._:-]/g, "-").slice(0, 40) || "command";
  const randomPart = globalThis.crypto?.randomUUID?.()
    ?? `${Date.now().toString(36)}-${(++idempotencyFallbackCounter).toString(36)}-${Math.random().toString(36).slice(2)}`;
  return `${normalizedScope}:${randomPart}`.slice(0, 128);
}

function idempotentMutation(scope: string, key = createIdempotencyKey(scope)) {
  return {
    headers: { [IDEMPOTENCY_HEADER]: key },
    retries: 2,
    retryUnsafe: true,
  } as const;
}

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
const periodETags = new Map<string, string>();

function periodResourceKey(path: string): string | undefined {
  const match = path.match(/^\/api\/companies\/(\d+)\/periods\/(\d+)(?:[/?]|$)/);
  return match ? `${match[1]}:${match[2]}` : undefined;
}

function safeResponseCorrelationId(body: string): string | undefined {
  if (!body) return undefined;
  try {
    const parsed: unknown = JSON.parse(body);
    if (!parsed || typeof parsed !== "object") return undefined;
    const candidate = (parsed as Record<string, unknown>).correlationId;
    return typeof candidate === "string" && /^[A-Za-z0-9._-]{1,128}$/.test(candidate)
      ? candidate
      : undefined;
  } catch {
    return undefined;
  }
}

function monitoredFailureCode(
  path: string,
  fallback: Exclude<ClientMonitoringEventCode, "auth-service-unavailable">,
): ClientMonitoringEventCode {
  return path.startsWith("/api/auth/") ? "auth-service-unavailable" : fallback;
}

function reportApiFailure(
  eventCode: ClientMonitoringEventCode,
  correlationId?: string,
) {
  void reportClientMonitoringEvent(eventCode, {
    route: currentBrowserPath(),
    correlationId,
  });
}

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

async function fetchRawWithRetry(
  url: string,
  init: RequestInit,
  timeout: number,
  retries = MAX_RETRIES,
): Promise<Response> {
  let lastError: unknown;
  for (let attempt = 0; attempt <= retries; attempt++) {
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), timeout);
    try {
      const response = await fetch(url, { ...init, signal: controller.signal });
      if ((response.status >= 500 || response.status === 429) && attempt < retries) {
        await response.body?.cancel().catch(() => undefined);
        await new Promise((resolve) => setTimeout(resolve, RETRY_DELAYS[attempt] ?? 1500));
        continue;
      }
      return response;
    } catch (error) {
      lastError = error;
      const retryable = error instanceof TypeError
        || (error instanceof DOMException && error.name === "AbortError");
      if (!retryable || attempt >= retries) throw error;
      await new Promise((resolve) => setTimeout(resolve, RETRY_DELAYS[attempt] ?? 1500));
    } finally {
      clearTimeout(timeoutId);
    }
  }
  throw lastError ?? new Error("Request failed after retries");
}

async function apiFetch<T>(
  path: string,
  options?: RequestInit & {
    timeout?: number;
    retries?: number;
    retryUnsafe?: boolean;
    responseSchema?: z.ZodType<unknown>;
    responseContract?: string;
  },
): Promise<T> {
  const {
    timeout = DEFAULT_TIMEOUT,
    retries = MAX_RETRIES,
    retryUnsafe = false,
    responseSchema,
    responseContract,
    ...fetchOptions
  } = options ?? {};
  const effectiveRetries = isUnsafeMethod(fetchOptions.method) && !retryUnsafe ? 0 : retries;

  let lastError: Error | null = null;

  for (let attempt = 0; attempt <= effectiveRetries; attempt++) {
    try {
      const controller = new AbortController();
      const timeoutId = setTimeout(() => controller.abort(), timeout);
      const periodKey = periodResourceKey(path);
      const requestHeaders = new Headers(withCsrfHeader(fetchOptions.method, {
        "Content-Type": "application/json",
        ...fetchOptions?.headers,
      }));
      if (periodKey && isUnsafeMethod(fetchOptions.method) && !requestHeaders.has("If-Match")) {
        const knownETag = periodETags.get(periodKey);
        if (knownETag) requestHeaders.set("If-Match", knownETag);
      }

      let res: Response;
      try {
        res = await fetch(`${API_BASE}${path}`, {
          ...fetchOptions,
          headers: requestHeaders,
          credentials: "include",
          signal: controller.signal,
        });
      } finally {
        clearTimeout(timeoutId);
      }

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

        if (error.status >= 500 || error.status === 429) {
          reportApiFailure(
            monitoredFailureCode(path, "api-server-rejection"),
            safeResponseCorrelationId(body),
          );
        }

        throw error;
      }

      const responseETag = res.headers?.get("ETag");
      if (periodKey && responseETag) periodETags.set(periodKey, responseETag);

      if (res.status === 204) return undefined as T;
      const payload: unknown = await res.json();
      return responseSchema
        ? apiContracts.parseApiContract(responseSchema, payload, responseContract ?? path) as T
        : payload as T;
    } catch (err) {
      if (err instanceof ApiError) throw err;

      if (err instanceof apiContracts.ApiContractError) {
        reportApiFailure(monitoredFailureCode(path, "api-contract-rejection"));
        throw err;
      }

      // Handle abort (timeout)
      if (err instanceof DOMException && err.name === "AbortError") {
        lastError = new Error("Request timed out. Please try again.");
        if (attempt < effectiveRetries) {
          await new Promise((r) => setTimeout(r, RETRY_DELAYS[attempt] ?? 1500));
          continue;
        }
        reportApiFailure(monitoredFailureCode(path, "api-timeout"));
        throw lastError;
      }

      // Handle network errors
      if (err instanceof TypeError && err.message.includes("fetch")) {
        lastError = new Error("Network error. Please check your connection.");
        if (attempt < effectiveRetries) {
          await new Promise((r) => setTimeout(r, RETRY_DELAYS[attempt] ?? 1500));
          continue;
        }
        reportApiFailure(monitoredFailureCode(path, "api-network-failure"));
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
  annualReturnDate?: string;
  annualReturnDateEffectiveFrom?: string;
  annualReturnDateSource?: AnnualReturnDateSource;
  annualReturnDateEvidenceReference?: string;
  annualReturnDateEvidenceSha256?: string;
  annualReturnDateChangeReason?: string;
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
  lockedAt?: string;
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
  priorYearClass?: string;
  rawCurrentClass?: string;
  rawPriorClass?: string;
  rawCurrentMicroQualified?: boolean;
  rawCurrentSmallQualified?: boolean;
  rawCurrentMediumQualified?: boolean;
  rawPriorMicroQualified?: boolean;
  rawPriorSmallQualified?: boolean;
  rawPriorMediumQualified?: boolean;
  annualisedTurnover?: number;
  periodLengthInYears?: number;
  thresholdElectionEffectiveFrom?: string;
  thresholdScheduleEffectiveFrom?: string;
  thresholdScheduleCode?: string;
  decisionInputFingerprintSha256?: string;
  calculatedClass: string;
  overrideRequiresRereview?: boolean;
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

export type TransactionSortField = "date" | "description" | "amount" | "confidence";
export type TransactionSortDirection = "asc" | "desc";

export interface TransactionListFilters {
  uncategorised?: boolean;
  categoryId?: number;
  bankAccountId?: number;
  search?: string;
  sortBy?: TransactionSortField;
  sortDirection?: TransactionSortDirection;
}

export interface TransactionPage {
  total: number;
  items: ImportedTransaction[];
  page: number;
  pageSize: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
  sortBy: TransactionSortField;
  sortDirection: TransactionSortDirection;
  aggregates: {
    total: number;
    categorised: number;
    uncategorised: number;
  };
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
  residualValue?: number;
  acquisitionDate: string;
  disposalDate?: string;
  disposalProceeds?: number;
  usefulLifeYears: number;
  depreciationMethod: string;
  capitalAllowanceTreatment: "Unreviewed" | "NonQualifying" | "PlantAndMachinery12Point5" | "UnsupportedSpecialScheme";
  capitalAllowanceEvidence?: string;
  capitalAllowanceReviewedBy?: string;
  capitalAllowanceReviewedAtUtc?: string;
}

export interface PayrollSummary {
  id?: number;
  periodId?: number;
  grossWages: number;
  directorsFees: number;
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
  code?: string;
  title: string;
  content?: string;
  isRequired: boolean;
  isIncluded: boolean;
  checklistState?: "Required" | "NotApplicable" | "ExplicitReview";
  reviewEvidence?: string;
  reviewedBy?: string;
  reviewedAt?: string;
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
  otherIncome: number;
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
    otherReserveMovements: number;
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
  tradingProfitBeforeLossRelief: number;
  tradingProfitAfterLossRelief: number;
  passiveNonTradingIncome: number;
  broughtForwardTradingLoss: number;
  tradingLossAvailable: number;
  tradingLossUsed: number;
  tradingLossCarriedForward: number;
  capitalAllowances: number;
  balancingAllowances: number;
  balancingCharges: number;
  supportStatus: "machine-supported-simple-scope" | "manual-review-required";
  finalTaxChargeSupported: boolean;
  manualReviewRequired: true;
  outputKind: "corporation-tax-support-data-not-ct1-return";
  isCompleteCt1Return: false;
  blockingReasons: string[];
  sources: { code: string; title: string; url: string }[];
  calculationSha256: string;
}

export type CorporationTaxLossTreatment =
  | "Unreviewed"
  | "NotApplicable"
  | "CarryForwardSameTrade"
  | "CurrentPeriodOrCarryBackClaim"
  | "GroupRelief"
  | "TerminalLossRelief"
  | "Other";

export interface CorporationTaxScopeReviewInput {
  isCloseCompany: boolean | null;
  isServiceCompany: boolean | null;
  hasGroupOrConsortiumRelief: boolean;
  hasChargeableGains: boolean;
  hasForeignIncomeOrTaxCredits: boolean;
  hasExceptedTrade: boolean;
  hasOtherReliefsOrSpecialRegimes: boolean;
  declaredPassiveIncomePresent: boolean;
  passiveIncomeClassificationReviewed: boolean;
  lossTreatment: CorporationTaxLossTreatment;
  broughtForwardTradingLoss: number;
  broughtForwardLossEvidence?: string;
  evidenceNote: string;
}

export interface CorporationTaxScopeReview extends CorporationTaxScopeReviewInput {
  id: number;
  periodId: number;
  preparedBy: string;
  preparedAtUtc: string;
}

export interface CorporationTaxLossRecord {
  id: number;
  periodId: number;
  openingTradingLoss: number;
  currentPeriodTradingLoss: number;
  tradingLossUsed: number;
  closingTradingLoss: number;
  treatment: CorporationTaxLossTreatment;
  calculationSha256: string;
  recordedBy: string;
  recordedAtUtc: string;
}

export interface CorporationTaxScopeReviewResponse {
  review: CorporationTaxScopeReview | null;
  lossRecord?: CorporationTaxLossRecord | null;
  computation?: TaxComputation;
  computationFailure?: string | null;
}

export type CorporationTaxPaymentKind = z.infer<typeof apiContracts.corporationTaxPaymentKindSchema>;
export type CorporationTaxPaymentClass = z.infer<typeof apiContracts.corporationTaxPaymentClassSchema>;
export type CorporationTaxPaymentRecord = z.infer<typeof apiContracts.corporationTaxPaymentRecordSchema>;
export type CorporationTaxFilingSupportReview = z.infer<typeof apiContracts.corporationTaxFilingSupportReviewSchema>;
export type CorporationTaxFilingSupport = z.infer<typeof apiContracts.corporationTaxFilingSupportSchema>;
export type CorporationTaxSupportWorksheet = z.infer<typeof apiContracts.corporationTaxSupportWorksheetSchema>;
export type CorporationTaxFilingSupportResponse = z.infer<typeof apiContracts.corporationTaxFilingSupportResponseSchema>;

export interface CorporationTaxFilingSupportReviewInput {
  priorPeriodStart?: string;
  priorPeriodEnd?: string;
  priorPeriodCorporationTaxLiabilityExcludingSurchargeAndS239?: number;
  priorPeriodSection239IncomeTax?: number;
  currentPeriodSection239IncomeTax: number;
  priorLiabilityEvidenceReference?: string;
  hasInterestLimitationRule: boolean;
  usesNotionalGroupPaymentAllocation: boolean;
  hasDirtOrOtherWithholdingCredits: boolean;
  hasOtherPreliminaryTaxAdjustments: boolean;
  hasMandatoryElectronicFilingExemption: boolean;
  evidenceNote: string;
}

export interface CorporationTaxPaymentInput {
  paymentDate: string;
  amount: number;
  kind: CorporationTaxPaymentKind;
  evidenceReference: string;
  externalPaymentReference?: string;
}

export interface BankImportResult {
  totalRows: number;
  importedRows: number;
  duplicateCandidates: number;
  autoCategorised: number;
  warnings: string[];
  importBatchId?: number;
  sourceFilename: string;
  sourceFileSha256: string;
  sourceFileBytes: number;
}

export type DuplicateReviewStatus = "Pending" | "LegacyLockedUnverified" | "Retained" | "Discarded";
export type DuplicateReviewDecision = "Pending" | "Retained" | "Discarded";

export interface DuplicateCandidateReview {
  transactionId: number;
  bankAccountId: number;
  bankAccountName: string;
  currency: string;
  importBatchId?: number;
  sourceFilename: string;
  sourceFileSha256?: string;
  sourceImportedAtUtc?: string;
  sourceRowNumber?: number;
  sourceRowSha256?: string;
  date: string;
  description: string;
  amount: number;
  balance?: number;
  reference?: string;
  status: DuplicateReviewStatus;
  includedInLedger: boolean;
  candidateKind: "ExactSourceReimport" | "ReferenceAndBalanceMatch" | "ReferenceMatch" | "BalanceMatch" | "SameDateAmountDescription" | "LegacyUnverified";
  confidence: number;
  reasons: string[];
  matchedTransactionId?: number;
  matchedBankAccountId?: number;
  matchedBankAccountName?: string;
  matchedCurrency?: string;
  matchedImportBatchId?: number;
  matchedSourceFilename?: string;
  matchedSourceFileSha256?: string;
  matchedSourceImportedAtUtc?: string;
  matchedSourceRowNumber?: number;
  matchedSourceRowSha256?: string;
  matchedDate?: string;
  matchedDescription?: string;
  matchedAmount?: number;
  matchedBalance?: number;
  matchedReference?: string;
  decidedByDisplayName?: string;
  decidedAtUtc?: string;
  decisionReason?: string;
  decisionVersion: number;
  batchDecisionAvailable: boolean;
}

export interface DuplicateExactReimportBatchReview {
  importBatchId: number;
  bankAccountId: number;
  bankAccountName: string;
  currency: string;
  sourceFilename: string;
  sourceFileSha256: string;
  importedAtUtc: string;
  currentStatus: "Pending" | "Retained" | "Discarded";
  candidateCount: number;
  decisionToken: string;
}

export interface DuplicateReviewQueue {
  pendingCount: number;
  retainedCount: number;
  discardedCount: number;
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
  exactReimportBatchTotal: number;
  exactReimportBatchPage: number;
  exactReimportBatchPageSize: number;
  exactReimportBatchTotalPages: number;
  exactReimportBatches: DuplicateExactReimportBatchReview[];
  items: DuplicateCandidateReview[];
}

// === API Functions ===

// Companies
export const getCompanies = () => apiFetch<Company[]>("/api/companies", {
  responseSchema: z.array(apiContracts.companySchema),
  responseContract: "company list",
});
export const getCompany = (id: number) => apiFetch<Company>(`/api/companies/${id}`, {
  responseSchema: apiContracts.companySchema,
  responseContract: "company",
});
export const createCompany = (data: Partial<Company>, idempotencyKey?: string) =>
  apiFetch<Company>("/api/companies", {
    method: "POST",
    ...idempotentMutation("company-create", idempotencyKey),
    body: JSON.stringify(data),
    responseSchema: apiContracts.companySchema,
    responseContract: "created company",
  });
export interface CompanyOnboardingInput {
  company: Partial<Company>;
  officers: Array<Pick<Officer, "name" | "role">>;
  firstPeriod: {
    periodStart: string;
    periodEnd: string;
    isFirstYear: true;
    memberAuditNoticeReceived: false;
    goingConcernConfirmed: true;
  };
  openingBankAccount: {
    name: string;
    iban?: string;
    currency: "EUR";
    openingBalance: number;
    openingBalanceDate?: string;
  };
}

export interface CompanyOnboardingOutcome {
  companyId: number;
  companyLegalName: string;
  firstPeriodId: number;
  firstPeriodStart: string;
  firstPeriodEnd: string;
  openingBankAccountId: number;
  openingBankAccountName: string;
  categoryCount: number;
  officers: Array<{ id: number; name: string; role: string }>;
}

const companyOnboardingOutcomeSchema = z.object({
  companyId: z.number().int().positive(),
  companyLegalName: z.string().min(1),
  firstPeriodId: z.number().int().positive(),
  firstPeriodStart: z.string().regex(/^\d{4}-\d{2}-\d{2}$/),
  firstPeriodEnd: z.string().regex(/^\d{4}-\d{2}-\d{2}$/),
  openingBankAccountId: z.number().int().positive(),
  openingBankAccountName: z.string().min(1),
  categoryCount: z.number().int().positive(),
  officers: z.array(z.object({
    id: z.number().int().positive(),
    name: z.string().min(1),
    role: z.string().min(1),
  })).min(1),
});

export const onboardCompany = (data: CompanyOnboardingInput, idempotencyKey: string) =>
  apiFetch<CompanyOnboardingOutcome>("/api/companies/onboard", {
    method: "POST",
    headers: { "Idempotency-Key": idempotencyKey },
    body: JSON.stringify(data),
    retries: 2,
    retryUnsafe: true,
    responseSchema: companyOnboardingOutcomeSchema,
    responseContract: "atomic company onboarding",
  });
export const updateCompany = (id: number, data: Partial<Company>) =>
  apiFetch<Company>(`/api/companies/${id}`, {
    method: "PUT",
    body: JSON.stringify(data),
    responseSchema: apiContracts.companySchema,
    responseContract: "updated company",
  });

export type AnnualReturnDateSource = "CroRecord" | "BroughtForward" | "ExtendedB73" | "CourtOrder" | "ManualOverride";

export interface AnnualReturnDateRecord {
  id: number;
  companyId: number;
  previousAnnualReturnDate?: string;
  annualReturnDate: string;
  effectiveFrom: string;
  source: AnnualReturnDateSource;
  evidenceReference: string;
  evidenceSha256?: string;
  changeReason?: string;
  recordedByUserId: string;
  recordedByDisplayName: string;
  recordedAtUtc: string;
  recordSha256: string;
}

export interface AnnualReturnDateChangeInput {
  annualReturnDate: string;
  effectiveFrom: string;
  source: AnnualReturnDateSource;
  evidenceReference: string;
  evidenceSha256?: string;
  changeReason?: string;
}

const annualReturnDateRecordSchema = z.object({
  id: z.number().int().positive(),
  companyId: z.number().int().positive(),
  previousAnnualReturnDate: z.string().regex(/^\d{4}-\d{2}-\d{2}$/).nullable().optional().transform((value) => value ?? undefined),
  annualReturnDate: z.string().regex(/^\d{4}-\d{2}-\d{2}$/),
  effectiveFrom: z.string().regex(/^\d{4}-\d{2}-\d{2}$/),
  source: z.enum(["CroRecord", "BroughtForward", "ExtendedB73", "CourtOrder", "ManualOverride"]),
  evidenceReference: z.string().trim().min(1).max(300),
  evidenceSha256: z.string().regex(/^[a-f0-9]{64}$/i).nullable().optional().transform((value) => value ?? undefined),
  changeReason: z.string().trim().min(1).nullable().optional().transform((value) => value ?? undefined),
  recordedByUserId: z.string().trim().min(1),
  recordedByDisplayName: z.string().trim().min(1),
  recordedAtUtc: z.string().datetime({ offset: true }),
  recordSha256: z.string().regex(/^[a-f0-9]{64}$/i),
});

export const getAnnualReturnDateHistory = (companyId: number) =>
  apiFetch<AnnualReturnDateRecord[]>(`/api/companies/${companyId}/annual-return-dates`, {
    responseSchema: z.array(annualReturnDateRecordSchema),
    responseContract: "Annual Return Date evidence history",
  });

export const recordAnnualReturnDate = (companyId: number, data: AnnualReturnDateChangeInput) =>
  apiFetch<AnnualReturnDateRecord>(`/api/companies/${companyId}/annual-return-dates`, {
    method: "POST",
    body: JSON.stringify(data),
    responseSchema: annualReturnDateRecordSchema,
    responseContract: "recorded Annual Return Date evidence",
  });
export interface CompanyQuarantineRequest {
  confirmation: string;
  reason: string;
}

export interface CompanyQuarantineOutcome {
  companyId: number;
  companyLegalName: string;
  status: "Quarantined" | "Recovered";
  evidenceId: number;
  evidenceSha256: string;
  occurredAtUtc: string;
  inventory: Record<string, number>;
  totalDependentRows: number;
}

export interface QuarantinedCompanySummary {
  companyId: number;
  legalName: string;
  quarantinedAtUtc: string;
  quarantinedByDisplayName: string;
  reason: string;
  evidenceSha256: string;
}

export const quarantineCompany = (id: number, data: CompanyQuarantineRequest) =>
  apiFetch<CompanyQuarantineOutcome>(`/api/companies/${id}`, {
    method: "DELETE",
    body: JSON.stringify(data),
  });
export const getQuarantinedCompanies = () =>
  apiFetch<QuarantinedCompanySummary[]>("/api/companies/quarantined");
export const recoverCompany = (id: number, data: CompanyQuarantineRequest) =>
  apiFetch<CompanyQuarantineOutcome>(`/api/companies/${id}/recover`, {
    method: "POST",
    body: JSON.stringify(data),
  });

// Officers
export const getOfficers = (companyId: number) =>
  apiFetch<Officer[]>(`/api/companies/${companyId}/officers`, {
    responseSchema: z.array(apiContracts.officerSchema),
    responseContract: "company officer list",
  });
export const createOfficer = (companyId: number, data: Officer) =>
  apiFetch<Officer>(`/api/companies/${companyId}/officers`, {
    method: "POST",
    body: JSON.stringify(data),
    responseSchema: apiContracts.officerSchema,
    responseContract: "created company officer",
  });
export const updateOfficer = (companyId: number, officerId: number, data: Partial<Officer>) =>
  apiFetch<Officer>(`/api/companies/${companyId}/officers/${officerId}`, {
    method: "PUT",
    body: JSON.stringify(data),
    responseSchema: apiContracts.officerSchema,
    responseContract: "updated company officer",
  });
export const deleteOfficer = (companyId: number, officerId: number) =>
  apiFetch<void>(`/api/companies/${companyId}/officers/${officerId}`, { method: "DELETE" });

// Periods
export const getPeriods = (companyId: number) =>
  apiFetch<AccountingPeriod[]>(`/api/companies/${companyId}/periods`, {
    responseSchema: z.array(apiContracts.accountingPeriodSchema),
    responseContract: "accounting period list",
  });
export const getPeriod = (companyId: number, id: number) =>
  apiFetch<AccountingPeriod>(`/api/companies/${companyId}/periods/${id}`, {
    responseSchema: apiContracts.accountingPeriodSchema,
    responseContract: "accounting period",
  });
export const createPeriod = (companyId: number, data: Partial<AccountingPeriod>, idempotencyKey?: string) =>
  apiFetch<AccountingPeriod>(`/api/companies/${companyId}/periods`, {
    method: "POST",
    ...idempotentMutation("period-create", idempotencyKey),
    body: JSON.stringify(data),
    responseSchema: apiContracts.accountingPeriodSchema,
    responseContract: "created accounting period",
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
  filters?: TransactionListFilters,
) => {
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (filters?.uncategorised) params.set("uncategorised", "true");
  if (filters?.categoryId != null) params.set("categoryId", String(filters.categoryId));
  if (filters?.bankAccountId != null) params.set("bankAccountId", String(filters.bankAccountId));
  if (filters?.search) params.set("search", filters.search);
  if (filters?.sortBy) params.set("sortBy", filters.sortBy);
  if (filters?.sortDirection) params.set("sortDirection", filters.sortDirection);
  return apiFetch<TransactionPage>(
    `/api/companies/${companyId}/periods/${periodId}/transactions?${params}`,
    {
      responseSchema: apiContracts.transactionPageSchema,
      responseContract: "transaction page",
    },
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
      responseSchema: apiContracts.importedTransactionSchema,
      responseContract: "categorised transaction",
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
      responseSchema: z.object({ updated: z.number().int().nonnegative() }),
      responseContract: "bulk transaction categorisation result",
    },
  );

const duplicateCandidateReviewSchema = z.object({
  transactionId: z.number().int().positive(),
  bankAccountId: z.number().int().positive(),
  bankAccountName: z.string().trim().min(1),
  currency: z.string().trim().regex(/^[A-Z]{3}$/),
  importBatchId: z.number().int().positive().nullable().optional().transform((value) => value ?? undefined),
  sourceFilename: z.string().trim().min(1),
  sourceFileSha256: z.string().regex(/^[a-f0-9]{64}$/i).nullable().optional().transform((value) => value ?? undefined),
  sourceImportedAtUtc: z.string().datetime({ offset: true }).nullable().optional().transform((value) => value ?? undefined),
  sourceRowNumber: z.number().int().positive().nullable().optional().transform((value) => value ?? undefined),
  sourceRowSha256: z.string().regex(/^[a-f0-9]{64}$/i).nullable().optional().transform((value) => value ?? undefined),
  date: z.string().regex(/^\d{4}-\d{2}-\d{2}$/),
  description: z.string().trim().min(1),
  amount: z.number().finite(),
  balance: z.number().finite().nullable().optional().transform((value) => value ?? undefined),
  reference: z.string().trim().min(1).nullable().optional().transform((value) => value ?? undefined),
  status: z.enum(["Pending", "LegacyLockedUnverified", "Retained", "Discarded"]),
  includedInLedger: z.boolean(),
  candidateKind: z.enum(["ExactSourceReimport", "ReferenceAndBalanceMatch", "ReferenceMatch", "BalanceMatch", "SameDateAmountDescription", "LegacyUnverified"]),
  confidence: z.number().min(0).max(1),
  reasons: z.array(z.string().trim().min(1)).min(1),
  matchedTransactionId: z.number().int().positive().nullable().optional().transform((value) => value ?? undefined),
  matchedBankAccountId: z.number().int().positive().nullable().optional().transform((value) => value ?? undefined),
  matchedBankAccountName: z.string().trim().min(1).nullable().optional().transform((value) => value ?? undefined),
  matchedCurrency: z.string().trim().regex(/^[A-Z]{3}$/).nullable().optional().transform((value) => value ?? undefined),
  matchedImportBatchId: z.number().int().positive().nullable().optional().transform((value) => value ?? undefined),
  matchedSourceFilename: z.string().trim().min(1).nullable().optional().transform((value) => value ?? undefined),
  matchedSourceFileSha256: z.string().regex(/^[a-f0-9]{64}$/i).nullable().optional().transform((value) => value ?? undefined),
  matchedSourceImportedAtUtc: z.string().datetime({ offset: true }).nullable().optional().transform((value) => value ?? undefined),
  matchedSourceRowNumber: z.number().int().positive().nullable().optional().transform((value) => value ?? undefined),
  matchedSourceRowSha256: z.string().regex(/^[a-f0-9]{64}$/i).nullable().optional().transform((value) => value ?? undefined),
  matchedDate: z.string().regex(/^\d{4}-\d{2}-\d{2}$/).nullable().optional().transform((value) => value ?? undefined),
  matchedDescription: z.string().trim().min(1).nullable().optional().transform((value) => value ?? undefined),
  matchedAmount: z.number().finite().nullable().optional().transform((value) => value ?? undefined),
  matchedBalance: z.number().finite().nullable().optional().transform((value) => value ?? undefined),
  matchedReference: z.string().trim().min(1).nullable().optional().transform((value) => value ?? undefined),
  decidedByDisplayName: z.string().trim().min(1).nullable().optional().transform((value) => value ?? undefined),
  decidedAtUtc: z.string().datetime({ offset: true }).nullable().optional().transform((value) => value ?? undefined),
  decisionReason: z.string().trim().min(1).nullable().optional().transform((value) => value ?? undefined),
  decisionVersion: z.number().int().nonnegative(),
  batchDecisionAvailable: z.boolean(),
}).superRefine((candidate, context) => {
  const reviewerEvidence = [
    candidate.decidedByDisplayName,
    candidate.decidedAtUtc,
    candidate.decisionReason,
  ];
  const reviewerEvidenceCount = reviewerEvidence.filter(Boolean).length;
  if (reviewerEvidenceCount !== 0 && reviewerEvidenceCount !== reviewerEvidence.length) {
    context.addIssue({ code: "custom", path: ["status"], message: "Duplicate review evidence must be complete when present" });
  }
  const unresolved = candidate.status === "Pending" || candidate.status === "LegacyLockedUnverified";
  if (!unresolved && reviewerEvidenceCount !== reviewerEvidence.length) {
    context.addIssue({ code: "custom", path: ["status"], message: "Resolved duplicate review requires retained reviewer evidence" });
  }
  if (candidate.decisionVersion === 0 && reviewerEvidenceCount !== 0) {
    context.addIssue({ code: "custom", path: ["decisionVersion"], message: "An undecided candidate cannot carry current decision evidence" });
  }
  if (candidate.decisionVersion > 0 && reviewerEvidenceCount !== reviewerEvidence.length) {
    context.addIssue({ code: "custom", path: ["decisionVersion"], message: "A versioned duplicate transition requires complete reviewer evidence" });
  }
  if ((!unresolved && candidate.decisionVersion === 0) || (candidate.status === "LegacyLockedUnverified" && candidate.decisionVersion !== 0)) {
    context.addIssue({ code: "custom", path: ["decisionVersion"], message: "Duplicate status and decision version are inconsistent" });
  }
  const expectedLedgerInclusion = candidate.status !== "Discarded" && candidate.status !== "LegacyLockedUnverified";
  if (candidate.includedInLedger !== expectedLedgerInclusion) {
    context.addIssue({ code: "custom", path: ["includedInLedger"], message: "Ledger inclusion does not match the retained decision state" });
  }
  if (candidate.candidateKind !== "LegacyUnverified" && (!candidate.importBatchId || !candidate.sourceRowNumber || !candidate.sourceRowSha256)) {
    context.addIssue({ code: "custom", path: ["sourceRowSha256"], message: "Current imports require complete retained source-row evidence" });
  }
  if (candidate.candidateKind !== "LegacyUnverified" && (!candidate.sourceFileSha256 || !candidate.sourceImportedAtUtc)) {
    context.addIssue({ code: "custom", path: ["sourceFileSha256"], message: "Current imports require complete retained source-file evidence" });
  }
  const matchFields = [candidate.matchedTransactionId, candidate.matchedBankAccountId, candidate.matchedBankAccountName, candidate.matchedCurrency];
  if (matchFields.filter(Boolean).length !== 0 && matchFields.filter(Boolean).length !== matchFields.length) {
    context.addIssue({ code: "custom", path: ["matchedTransactionId"], message: "Matched transaction ownership evidence must be complete" });
  }
  if (candidate.matchedBankAccountId && candidate.matchedBankAccountId !== candidate.bankAccountId) {
    context.addIssue({ code: "custom", path: ["matchedBankAccountId"], message: "A duplicate match must belong to the same bank account" });
  }
});

const duplicateExactReimportBatchSchema = z.object({
  importBatchId: z.number().int().positive(),
  bankAccountId: z.number().int().positive(),
  bankAccountName: z.string().trim().min(1),
  currency: z.string().trim().regex(/^[A-Z]{3}$/),
  sourceFilename: z.string().trim().min(1),
  sourceFileSha256: z.string().regex(/^[a-f0-9]{64}$/i),
  importedAtUtc: z.string().datetime({ offset: true }),
  currentStatus: z.enum(["Pending", "Retained", "Discarded"]),
  candidateCount: z.number().int().positive(),
  decisionToken: z.string().regex(/^[a-f0-9]{64}$/i),
});

const duplicateReviewQueueSchema = z.object({
  pendingCount: z.number().int().nonnegative(),
  retainedCount: z.number().int().nonnegative(),
  discardedCount: z.number().int().nonnegative(),
  total: z.number().int().nonnegative(),
  page: z.number().int().positive(),
  pageSize: z.number().int().min(10).max(100),
  totalPages: z.number().int().positive(),
  exactReimportBatchTotal: z.number().int().nonnegative(),
  exactReimportBatchPage: z.number().int().positive(),
  exactReimportBatchPageSize: z.number().int().min(5).max(25),
  exactReimportBatchTotalPages: z.number().int().positive(),
  exactReimportBatches: z.array(duplicateExactReimportBatchSchema),
  items: z.array(duplicateCandidateReviewSchema),
}).superRefine((queue, context) => {
  const count = (status: DuplicateReviewStatus) => queue.items.filter((item) => item.status === status).length;
  const unresolvedOnPage = count("Pending") + count("LegacyLockedUnverified");
  if (unresolvedOnPage > queue.pendingCount) context.addIssue({ code: "custom", path: ["pendingCount"], message: "Page count exceeds queue count" });
  if (count("Retained") > queue.retainedCount) context.addIssue({ code: "custom", path: ["retainedCount"], message: "Page count exceeds queue count" });
  if (count("Discarded") > queue.discardedCount) context.addIssue({ code: "custom", path: ["discardedCount"], message: "Page count exceeds queue count" });
  if (queue.total !== queue.pendingCount + queue.retainedCount + queue.discardedCount) context.addIssue({ code: "custom", path: ["total"], message: "Queue counts do not reconcile" });
  const expectedPages = Math.max(1, Math.ceil(queue.total / queue.pageSize));
  if (queue.totalPages !== expectedPages || queue.page > queue.totalPages) context.addIssue({ code: "custom", path: ["totalPages"], message: "Pagination does not reconcile" });
  if (queue.items.length > queue.pageSize || queue.items.length > queue.total) context.addIssue({ code: "custom", path: ["items"], message: "Page item count exceeds its envelope" });
  const expectedBatchPages = Math.max(1, Math.ceil(queue.exactReimportBatchTotal / queue.exactReimportBatchPageSize));
  if (queue.exactReimportBatchTotalPages !== expectedBatchPages || queue.exactReimportBatchPage > queue.exactReimportBatchTotalPages) {
    context.addIssue({ code: "custom", path: ["exactReimportBatchTotalPages"], message: "Exact re-import batch pagination does not reconcile" });
  }
  if (queue.exactReimportBatches.length > queue.exactReimportBatchPageSize || queue.exactReimportBatches.length > queue.exactReimportBatchTotal) {
    context.addIssue({ code: "custom", path: ["exactReimportBatches"], message: "Exact re-import batch page exceeds its envelope" });
  }
});

export const getDuplicateReviewQueue = (companyId: number, periodId: number, page = 1, pageSize = 50, batchPage = 1, batchPageSize = 10) =>
  apiFetch<DuplicateReviewQueue>(`/api/companies/${companyId}/periods/${periodId}/transactions/duplicate-review?page=${page}&pageSize=${pageSize}&batchPage=${batchPage}&batchPageSize=${batchPageSize}`, {
    responseSchema: duplicateReviewQueueSchema,
    responseContract: "duplicate transaction review queue",
  });

export const decideDuplicateCandidate = (
  companyId: number,
  periodId: number,
  transactionId: number,
  decision: DuplicateReviewDecision,
  reason: string,
  expectedStatus: DuplicateReviewStatus,
  expectedDecisionVersion: number,
) => apiFetch<DuplicateCandidateReview>(
  `/api/companies/${companyId}/periods/${periodId}/transactions/${transactionId}/duplicate-review`,
  {
    method: "POST",
    body: JSON.stringify({ decision, reason, expectedStatus, expectedDecisionVersion }),
    responseSchema: duplicateCandidateReviewSchema,
    responseContract: "duplicate transaction reviewer decision",
  },
);

export const decideDuplicateBatch = (
  companyId: number,
  periodId: number,
  batch: DuplicateExactReimportBatchReview,
  decision: DuplicateReviewDecision,
  reason: string,
) => apiFetch<{ importBatchId: number; decision: DuplicateReviewDecision; updatedCount: number; rowEvidenceSha256: string }>(
  `/api/companies/${companyId}/periods/${periodId}/transactions/duplicate-review/batches/${batch.importBatchId}`,
  {
    method: "POST",
    body: JSON.stringify({
      decision,
      reason,
      expectedStatus: batch.currentStatus,
      expectedCandidateCount: batch.candidateCount,
      expectedDecisionToken: batch.decisionToken,
    }),
    responseSchema: z.object({
      importBatchId: z.number().int().positive(),
      decision: z.enum(["Pending", "Retained", "Discarded"]),
      updatedCount: z.number().int().positive(),
      rowEvidenceSha256: z.string().regex(/^[a-f0-9]{64}$/i),
    }),
    responseContract: "exact re-import batch decision",
  },
);

// Categories
export const getCategories = (companyId: number) =>
  apiFetch<AccountCategory[]>(`/api/companies/${companyId}/categories`, {
    responseSchema: z.array(apiContracts.accountCategorySchema),
    responseContract: "account category list",
  });
export const seedCategories = (companyId: number) =>
  apiFetch<AccountCategory[]>(`/api/companies/${companyId}/categories/seed`, {
    method: "POST",
    responseSchema: z.array(apiContracts.accountCategorySchema),
    responseContract: "seeded account category list",
  });

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
    `/api/companies/${companyId}/periods/${periodId}/year-end-summary`,
    {
      responseSchema: apiContracts.yearEndSummarySchema,
      responseContract: "year-end summary",
    },
  );
export const getOpeningBalances = (companyId: number, periodId: number) =>
  apiFetch<OpeningBalance[]>(
    `/api/companies/${companyId}/periods/${periodId}/opening-balances`,
    {
      responseSchema: z.array(apiContracts.openingBalanceSchema),
      responseContract: "opening balance list",
    },
  );
export const saveOpeningBalance = (
  companyId: number,
  periodId: number,
  categoryId: number,
  data: { debit: number; credit: number; sourceNote?: string; reviewed: boolean },
) =>
  apiFetch<OpeningBalance>(
    `/api/companies/${companyId}/periods/${periodId}/opening-balances/${categoryId}`,
    {
      method: "PUT",
      body: JSON.stringify(data),
      responseSchema: apiContracts.openingBalanceSchema,
      responseContract: "saved opening balance",
    },
  );
export const deleteOpeningBalance = (companyId: number, periodId: number, categoryId: number) =>
  apiFetch<void>(
    `/api/companies/${companyId}/periods/${periodId}/opening-balances/${categoryId}`,
    { method: "DELETE" },
  );
export const getYearEndReviewConfirmations = (companyId: number, periodId: number) =>
  apiFetch<YearEndReviewConfirmation[]>(
    `/api/companies/${companyId}/periods/${periodId}/year-end-reviews`,
    {
      responseSchema: z.array(apiContracts.yearEndReviewConfirmationSchema),
      responseContract: "year-end review confirmation list",
    },
  );
export const saveYearEndReviewConfirmation = (
  companyId: number,
  periodId: number,
  sectionKey: string,
  data: { confirmed: boolean; note?: string },
) =>
  apiFetch<YearEndReviewConfirmation>(
    `/api/companies/${companyId}/periods/${periodId}/year-end-reviews/${sectionKey}`,
    {
      method: "PUT",
      body: JSON.stringify(data),
      responseSchema: apiContracts.yearEndReviewConfirmationSchema,
      responseContract: "saved year-end review confirmation",
    },
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
    `/api/companies/${companyId}/periods/${periodId}/adjustments${qs ? `?${qs}` : ""}`,
    {
      responseSchema: z.array(apiContracts.adjustmentSchema),
      responseContract: "adjustment list",
    },
  );
};
export const getAdjustmentSummary = (companyId: number, periodId: number) =>
  apiFetch<AdjustmentSummary>(
    `/api/companies/${companyId}/periods/${periodId}/adjustments/summary`,
    {
      responseSchema: apiContracts.adjustmentSummarySchema,
      responseContract: "adjustment summary",
    },
  );
export const generateAdjustments = (companyId: number, periodId: number) =>
  apiFetch<AdjustmentSummary>(
    `/api/companies/${companyId}/periods/${periodId}/adjustments/generate`,
    {
      method: "POST",
      responseSchema: apiContracts.adjustmentSummarySchema,
      responseContract: "generated adjustment summary",
    },
  );
export const approveAdjustment = (
  companyId: number,
  periodId: number,
  id: number,
) =>
  apiFetch<Adjustment>(
    `/api/companies/${companyId}/periods/${periodId}/adjustments/${id}/approve`,
    {
      method: "POST",
      body: JSON.stringify({}),
      responseSchema: apiContracts.adjustmentSchema,
      responseContract: "approved adjustment",
    },
  );

// Import
export const uploadBankCsv = async (
  companyId: number,
  bankAccountId: number,
  periodId: number,
  file: File,
  idempotencyKey = createIdempotencyKey("bank-import"),
): Promise<BankImportResult> => {
  try {
    const formData = new FormData();
    formData.append("file", file);
    const requestHeaders = new Headers(withCsrfHeader("POST"));
    requestHeaders.set(IDEMPOTENCY_HEADER, idempotencyKey);
    const res = await fetchRawWithRetry(
      `${API_BASE}/api/companies/${companyId}/bank-accounts/${bankAccountId}/import?periodId=${periodId}`,
      {
        method: "POST",
        body: formData,
        credentials: "include",
        headers: requestHeaders,
      },
      120000,
    );
    if (!res.ok) {
      const body = await res.text().catch(() => "");
      const error = new ApiError(res.status, res.statusText, body);
      if (error.status === 401) {
        dispatchSessionExpired();
      }
      throw error;
    }
    const payload: unknown = await res.json();
    return apiContracts.parseApiContract(
      apiContracts.importResultSchema,
      payload,
      "bank CSV import result",
    );
  } catch (err) {
    if (err instanceof DOMException && err.name === "AbortError") {
      throw new Error("Upload timed out. The file may be too large.");
    }
    throw err;
  }
};

// Statements
export const getReadiness = (companyId: number, periodId: number) =>
  apiFetch<ReadinessScore>(
    `/api/companies/${companyId}/periods/${periodId}/statements/readiness`,
    {
      responseSchema: apiContracts.readinessScoreSchema,
      responseContract: "statement readiness",
    },
  );
export const getStatementSources = (companyId: number, periodId: number) =>
  apiFetch<StatementSourceSummary[]>(
    `/api/companies/${companyId}/periods/${periodId}/statements/sources`,
    {
      responseSchema: z.array(apiContracts.statementSourceSummarySchema),
      responseContract: "statement source ledger",
    },
  );

// Documents
export async function fetchDocumentBlob(
  url: string,
  method: "GET" | "POST" = "GET",
  idempotencyKey = method === "POST" ? createIdempotencyKey("document-generate") : undefined,
) {
  const headers = new Headers(withCsrfHeader(method));
  if (idempotencyKey) headers.set(IDEMPOTENCY_HEADER, idempotencyKey);
  const response = await fetchRawWithRetry(url, {
    method,
    credentials: "include",
    headers,
  }, 120000);

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
  apiFetch<PayrollSummary | null>(`/api/companies/${companyId}/periods/${periodId}/payroll`, {
    responseSchema: apiContracts.payrollSummarySchema.nullable(),
    responseContract: "payroll summary",
  });
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
export type DirectorLoanRow = z.infer<typeof apiContracts.directorLoanRowSchema>;
export type DirectorLoanMovement = z.infer<typeof apiContracts.directorLoanMovementSchema>;
export type DirectorLoanCounterpartyType = z.infer<typeof apiContracts.directorLoanCounterpartyTypeSchema>;
export type DirectorLoanArrangementType = z.infer<typeof apiContracts.directorLoanArrangementTypeSchema>;
export type DirectorLoanTermsStatus = z.infer<typeof apiContracts.directorLoanTermsStatusSchema>;
export type DirectorLoanComplianceBasis = z.infer<typeof apiContracts.directorLoanComplianceBasisSchema>;
export type DirectorLoanRelevantAssetsBasis = z.infer<typeof apiContracts.directorLoanRelevantAssetsBasisSchema>;
export type DirectorLoanRelevantAssetsFallReview = z.infer<typeof apiContracts.directorLoanRelevantAssetsFallReviewSchema>;
export type DirectorLoanReviewDecision = z.infer<typeof apiContracts.directorLoanReviewDecisionSchema>;

export const getDirectorLoans = (companyId: number, periodId: number) =>
  apiFetch<DirectorLoanRow[]>(
    `/api/companies/${companyId}/periods/${periodId}/director-loans`,
    {
      responseSchema: z.array(apiContracts.directorLoanRowSchema),
      responseContract: "director-loan evidence list",
    },
  );
export const createDirectorLoan = (
  companyId: number,
  periodId: number,
  data: DirectorLoanRow,
) =>
  apiFetch<DirectorLoanRow>(`/api/companies/${companyId}/periods/${periodId}/director-loans`, {
    method: "POST",
    body: JSON.stringify(data),
    responseSchema: apiContracts.directorLoanRowSchema,
    responseContract: "created director-loan evidence",
  });
export const updateDirectorLoan = (
  companyId: number,
  periodId: number,
  id: number,
  data: DirectorLoanRow,
) =>
  apiFetch<DirectorLoanRow>(
    `/api/companies/${companyId}/periods/${periodId}/director-loans/${id}`,
    {
      method: "PUT",
      body: JSON.stringify(data),
      responseSchema: apiContracts.directorLoanRowSchema,
      responseContract: "updated director-loan evidence",
    },
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
    thresholdElectionEffectiveFrom: "2023-01-01" | "2024-01-01";
  },
) =>
  apiFetch<unknown>(
    `/api/companies/${companyId}/periods/${periodId}/size-classification`,
    { method: "PUT", body: JSON.stringify(data) },
  );

export interface ClassificationResult {
  calculatedClass: string;
  qualificationNotes: string;
  canUseMicro: boolean;
  canFileAbridged: boolean;
  auditExempt: boolean;
  availableRegimes: string[];
  isIneligibleEntity: boolean;
  ineligibleReason?: string;
  rawCurrentClass: string;
  rawPriorClass?: string;
  annualisedTurnover: number;
  periodLengthInYears: number;
  thresholdScheduleCode?: string;
  thresholdScheduleEffectiveFrom?: string;
}

export const runClassification = (companyId: number, periodId: number) =>
  apiFetch<ClassificationResult>(`/api/companies/${companyId}/periods/${periodId}/classify`, { method: "POST" });

export const setFilingRegime = (companyId: number, periodId: number, electedRegime?: string) =>
  apiFetch<unknown>(`/api/companies/${companyId}/periods/${periodId}/filing-regime`, {
    method: "POST",
    body: JSON.stringify({ electedRegime }),
  });

// Notes
export const getNotes = (companyId: number, periodId: number) =>
  apiFetch<NotesDisclosure[]>(`/api/companies/${companyId}/periods/${periodId}/notes`, {
    responseSchema: z.array(apiContracts.notesDisclosureSchema),
    responseContract: "statutory notes list",
  });
export const generateNotes = (companyId: number, periodId: number) =>
  apiFetch<NotesDisclosure[]>(`/api/companies/${companyId}/periods/${periodId}/notes/generate`, {
    method: "POST",
    responseSchema: z.array(apiContracts.notesDisclosureSchema),
    responseContract: "generated statutory notes",
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
    responseSchema: apiContracts.notesDisclosureSchema,
    responseContract: "updated statutory note",
  });
export const createNote = (companyId: number, periodId: number, data: Partial<NotesDisclosure>) =>
  apiFetch<NotesDisclosure>(`/api/companies/${companyId}/periods/${periodId}/notes`, {
    method: "POST",
    body: JSON.stringify(data),
    responseSchema: apiContracts.notesDisclosureSchema,
    responseContract: "created statutory note",
  });
export const deleteNote = (companyId: number, periodId: number, id: number) =>
  apiFetch<void>(`/api/companies/${companyId}/periods/${periodId}/notes/${id}`, {
    method: "DELETE",
  });

// Statements
export const getTrialBalance = (companyId: number, periodId: number) =>
  apiFetch<TrialBalanceLine[]>(
    `/api/companies/${companyId}/periods/${periodId}/statements/trial-balance`,
    {
      responseSchema: z.array(apiContracts.trialBalanceLineSchema),
      responseContract: "trial balance",
    },
  );
export const getProfitAndLoss = (companyId: number, periodId: number) =>
  apiFetch<ProfitAndLoss>(
    `/api/companies/${companyId}/periods/${periodId}/statements/profit-and-loss`,
    {
      responseSchema: apiContracts.profitAndLossSchema,
      responseContract: "profit and loss statement",
    },
  );
export const getBalanceSheet = (companyId: number, periodId: number) =>
  apiFetch<BalanceSheet>(
    `/api/companies/${companyId}/periods/${periodId}/statements/balance-sheet`,
    {
      responseSchema: apiContracts.balanceSheetSchema,
      responseContract: "balance sheet",
    },
  );
export const getTaxComputation = (companyId: number, periodId: number) =>
  apiFetch<TaxComputation>(
    `/api/companies/${companyId}/periods/${periodId}/revenue/tax-computation`,
    {
      responseSchema: apiContracts.taxComputationSchema,
      responseContract: "tax computation",
    },
  );
export const getCorporationTaxScopeReview = (companyId: number, periodId: number) =>
  apiFetch<CorporationTaxScopeReviewResponse>(
    `/api/companies/${companyId}/periods/${periodId}/revenue/scope-review`,
  );
export const saveCorporationTaxScopeReview = (
  companyId: number,
  periodId: number,
  data: CorporationTaxScopeReviewInput,
) => apiFetch<CorporationTaxScopeReviewResponse>(
  `/api/companies/${companyId}/periods/${periodId}/revenue/scope-review`,
  { method: "PUT", body: JSON.stringify(data) },
);
export const getCorporationTaxFilingSupport = (
  companyId: number,
  periodId: number,
  asOfDate?: string,
) => apiFetch<CorporationTaxFilingSupportResponse>(
  `/api/companies/${companyId}/periods/${periodId}/revenue/filing-support${asOfDate ? `?asOf=${encodeURIComponent(asOfDate)}` : ""}`,
  {
    responseSchema: apiContracts.corporationTaxFilingSupportResponseSchema,
    responseContract: "Corporation Tax filing-support worksheet and payment tracker",
  },
);
export const saveCorporationTaxFilingSupportReview = (
  companyId: number,
  periodId: number,
  data: CorporationTaxFilingSupportReviewInput,
) => apiFetch<CorporationTaxFilingSupportResponse>(
  `/api/companies/${companyId}/periods/${periodId}/revenue/filing-support`,
  {
    method: "PUT",
    body: JSON.stringify(data),
    responseSchema: apiContracts.corporationTaxFilingSupportResponseSchema,
    responseContract: "Corporation Tax filing-support review",
  },
);
export const recordCorporationTaxPayment = (
  companyId: number,
  periodId: number,
  data: CorporationTaxPaymentInput,
) => apiFetch<CorporationTaxFilingSupportResponse>(
  `/api/companies/${companyId}/periods/${periodId}/revenue/filing-support/payments`,
  {
    method: "POST",
    body: JSON.stringify(data),
    responseSchema: apiContracts.corporationTaxFilingSupportResponseSchema,
    responseContract: "Corporation Tax payment tracker",
  },
);
export const deleteCorporationTaxPayment = (
  companyId: number,
  periodId: number,
  paymentId: number,
) => apiFetch<CorporationTaxFilingSupportResponse>(
  `/api/companies/${companyId}/periods/${periodId}/revenue/filing-support/payments/${paymentId}`,
  {
    method: "DELETE",
    responseSchema: apiContracts.corporationTaxFilingSupportResponseSchema,
    responseContract: "Corporation Tax payment correction",
  },
);

export const corporationTaxSupportWorksheetCsvUrl = (companyId: number, periodId: number) =>
  `/api/companies/${companyId}/periods/${periodId}/revenue/ct1-support/worksheet.csv`;

export const corporationTaxSupportWorksheetJsonUrl = (companyId: number, periodId: number) =>
  `/api/companies/${companyId}/periods/${periodId}/revenue/ct1-support/worksheet`;

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
  calculatedDueDate: string;
  dueDate: string;
  annualReturnDate?: string;
  annualReturnDateRecordId?: number;
  returnMadeUpToDate?: string;
  financialStatementsLatestMadeUpToDate?: string;
  deliveryDueDate?: string;
  madeUpToDateBroughtForwardForAccountsAge?: boolean;
  calculationRuleVersion?: string;
  calculationSourceUrl?: string;
  calculationFingerprintSha256?: string;
  manualOverrideStatus?: "Active" | "NeedsReview";
  manualOverrideDueDate?: string;
  manualOverrideReason?: string;
  manualOverrideEvidenceReference?: string;
  manualOverrideEvidenceSha256?: string;
  manualOverrideByUserId?: string;
  manualOverrideByDisplayName?: string;
  manualOverrideAtUtc?: string;
  manualOverrideCalculationFingerprintSha256?: string;
  filedDate?: string;
  filingReference?: string;
  isLate: boolean;
  penaltyAmount: number;
  notes?: string;
}

export const DASHBOARD_DEADLINE_STATES = [
  "not-configured",
  "not-applicable",
  "unavailable",
  "overdue",
  "due-soon",
  "scheduled",
  "filed",
] as const;

export type DashboardDeadlineState = typeof DASHBOARD_DEADLINE_STATES[number];

export interface DashboardDeadlineItem {
  companyId: number;
  companyName: string;
  state: DashboardDeadlineState;
  deadline: FilingDeadline | null;
  message: string;
}

export interface DashboardDeadlineBatch {
  totalCompanies: number;
  unavailableCount: number;
  counts: Record<DashboardDeadlineState, number>;
  items: DashboardDeadlineItem[];
}

const filingDeadlineSchema = apiContracts.filingDeadlineSchema;

const dashboardDeadlineStateSchema = z.enum(DASHBOARD_DEADLINE_STATES);
const dashboardDeadlineBatchSchema = z.object({
  totalCompanies: z.number().int().nonnegative(),
  unavailableCount: z.number().int().nonnegative(),
  counts: z.object({
    "not-configured": z.number().int().nonnegative(),
    "not-applicable": z.number().int().nonnegative(),
    unavailable: z.number().int().nonnegative(),
    overdue: z.number().int().nonnegative(),
    "due-soon": z.number().int().nonnegative(),
    scheduled: z.number().int().nonnegative(),
    filed: z.number().int().nonnegative(),
  }),
  items: z.array(z.object({
    companyId: z.number().int().positive(),
    companyName: z.string().min(1),
    state: dashboardDeadlineStateSchema,
    deadline: filingDeadlineSchema.nullable(),
    message: z.string().min(1),
  })),
});

export function parseDashboardDeadlineBatch(payload: unknown): DashboardDeadlineBatch {
  const result = dashboardDeadlineBatchSchema.safeParse(payload);
  if (!result.success) {
    const issue = result.error.issues[0];
    const path = issue?.path.length ? issue.path.join(".") : "root";
    throw new Error(`Invalid dashboard deadline response contract: ${path} - ${issue?.message ?? "Invalid payload"}`);
  }

  const batch = result.data as DashboardDeadlineBatch;
  if (batch.totalCompanies !== batch.items.length) {
    throw new Error("Invalid dashboard deadline response contract: totalCompanies must equal items.length");
  }
  if (new Set(batch.items.map((item) => item.companyId)).size !== batch.items.length) {
    throw new Error("Invalid dashboard deadline response contract: companyId values must be unique");
  }
  for (const state of DASHBOARD_DEADLINE_STATES) {
    const actual = batch.items.filter((item) => item.state === state).length;
    if (batch.counts[state] !== actual) {
      throw new Error(`Invalid dashboard deadline response contract: counts.${state} must equal ${actual}`);
    }
  }
  if (batch.unavailableCount !== batch.counts.unavailable) {
    throw new Error("Invalid dashboard deadline response contract: unavailableCount must equal counts.unavailable");
  }
  for (const item of batch.items) {
    const requiresDeadline = ["overdue", "due-soon", "scheduled", "filed"].includes(item.state);
    if (requiresDeadline !== (item.deadline != null)) {
      throw new Error(`Invalid dashboard deadline response contract: items.${item.companyId}.deadline does not match state ${item.state}`);
    }
    if (item.deadline && item.deadline.companyId !== item.companyId) {
      throw new Error(`Invalid dashboard deadline response contract: items.${item.companyId}.deadline.companyId mismatch`);
    }
  }
  return batch;
}

export interface AuditExemptionJeopardy {
  lateFilingCount: number;
  isAtRisk: boolean;
  hasLostExemption: boolean;
  warning?: string;
}

export const getDeadlines = (companyId: number) =>
  apiFetch<FilingDeadline[]>(`/api/companies/${companyId}/deadlines`, {
    responseSchema: z.array(apiContracts.filingDeadlineSchema),
    responseContract: "filing deadline list",
  });

export const getUpcomingDeadline = (companyId: number) =>
  apiFetch<FilingDeadline | { message: string }>(`/api/companies/${companyId}/deadlines/upcoming`, {
    responseSchema: z.union([
      apiContracts.filingDeadlineSchema,
      z.object({ message: z.literal("No upcoming deadlines") }),
    ]),
    responseContract: "upcoming filing deadline",
  });

export async function getDashboardDeadlines(): Promise<DashboardDeadlineBatch> {
  return parseDashboardDeadlineBatch(await apiFetch<unknown>("/api/dashboard/deadlines"));
}

export const calculateDeadlines = (companyId: number, periodId: number) =>
  apiFetch<FilingDeadline[]>(
    `/api/companies/${companyId}/periods/${periodId}/deadlines/calculate`,
    {
      method: "POST",
      responseSchema: z.array(apiContracts.filingDeadlineSchema),
      responseContract: "calculated filing deadline list",
    },
  );

export const markFiled = (
  companyId: number,
  periodId: number,
  data: { deadlineType: string; filedDate: string; filingReference?: string },
  idempotencyKey?: string,
) =>
  apiFetch<FilingDeadline>(
    `/api/companies/${companyId}/periods/${periodId}/mark-filed`,
    {
      method: "POST",
      ...idempotentMutation("deadline-mark-filed", idempotencyKey),
      body: JSON.stringify(data),
      responseSchema: apiContracts.filingDeadlineSchema,
      responseContract: "filed deadline",
    },
  );

export const getAuditExemptionJeopardy = (companyId: number) =>
  apiFetch<AuditExemptionJeopardy>(`/api/companies/${companyId}/deadlines/jeopardy`);

// === Director Loan Compliance ===
export type DirectorLoanCompliance = z.infer<typeof apiContracts.directorLoanComplianceSchema>;
export type DirectorLoanDetail = z.infer<typeof apiContracts.directorLoanDetailSchema>;

export const getDirectorLoanCompliance = (companyId: number, periodId: number) =>
  apiFetch<DirectorLoanCompliance>(
    `/api/companies/${companyId}/periods/${periodId}/director-loans/compliance`,
    {
      responseSchema: apiContracts.directorLoanComplianceSchema,
      responseContract: "director-loan compliance",
    },
  );

export const recordDeadlineOverride = (
  companyId: number,
  periodId: number,
  data: {
    deadlineType: string;
    overrideDueDate: string;
    reason: string;
    evidenceReference: string;
    evidenceSha256: string;
  },
) => apiFetch<FilingDeadline>(
  `/api/companies/${companyId}/periods/${periodId}/deadlines/override`,
  {
    method: "POST",
    body: JSON.stringify(data),
    responseSchema: apiContracts.filingDeadlineSchema,
    responseContract: "manual deadline override",
  },
);

export const getSection307Note = (companyId: number, periodId: number) =>
  apiFetch<{ note: string }>(
    `/api/companies/${companyId}/periods/${periodId}/director-loans/section-307-note`,
    {
      responseSchema: z.object({ note: z.string().trim().min(1) }),
      responseContract: "section 307 director-loan note",
    },
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
  shareIssues: number;
  otherFinancing: number;
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
  otherReserveMovements: number;
  sharesIssued: number;
  closingShareCapital: number;
  closingRetainedEarnings: number;
  closingTotal: number;
}

export const getCashFlowStatement = (companyId: number, periodId: number) =>
  apiFetch<CashFlowStatement>(`/api/companies/${companyId}/periods/${periodId}/statements/cash-flow`, {
    responseSchema: apiContracts.cashFlowStatementSchema,
    responseContract: "cash-flow statement",
  });
export const getEquityChanges = (companyId: number, periodId: number) =>
  apiFetch<EquityChanges>(`/api/companies/${companyId}/periods/${periodId}/statements/equity-changes`, {
    responseSchema: apiContracts.equityChangesSchema,
    responseContract: "statement of changes in equity",
  });

// === Phase 4: Directors' Report ===

export interface DirectorsReportData {
  companyName: string;
  periodStart: string;
  periodEnd: string;
  directorNames: string[];
  directorServicePeriods: {
    name: string;
    role: "Director" | "Secretary" | "CompanySecretary";
    appointedDate: string;
    resignedDate?: string;
  }[];
  secretaryName?: string;
  secretaryServicePeriod?: {
    name: string;
    role: "Director" | "Secretary" | "CompanySecretary";
    appointedDate: string;
    resignedDate?: string;
  };
  principalActivities: string;
  principalActivitiesReviewed: boolean;
  principalActivitiesReviewedBy?: string;
  principalActivitiesReviewedAt?: string;
  resultsAndDividends: string;
  profitOrLossAfterTax: number;
  dividendsPaid: number;
  dividendsDeclaredNotPaid: number;
  accountingRecordsStatement: string;
  postBalanceSheetEvents?: string;
  postBalanceSheetEventsReviewed: boolean;
  goingConcernStatement?: string;
  auditInformationStatement?: string;
  auditInformationEvidenceRequired: boolean;
  auditInformationEvidenceRecorded: boolean;
  auditInformationConfirmedBy?: string;
  auditInformationConfirmedAt?: string;
  officerTimelineComplete: boolean;
  isMicroExempt: boolean;
  isSmallExemptFromBusinessReview: boolean;
  electedRegime: "Micro" | "Small" | "SmallAbridged" | "Medium" | "Full";
}

export const getDirectorsReportData = (companyId: number, periodId: number) =>
  apiFetch<DirectorsReportData>(`/api/companies/${companyId}/periods/${periodId}/documents/directors-report-data`, {
    responseSchema: apiContracts.directorsReportSchema,
    responseContract: "directors report",
  });

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
  governanceCodeCompliant: boolean | null;
  governanceCodeNote?: string;
  governanceEvidenceReference?: string;
  governanceReviewedBy?: string;
  governanceReviewedAtUtc?: string;
  governanceEvidenceArtifactSha256?: string;
  governanceEvidenceArtifact?: string;
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
  trustees: Array<{
    officerId: number;
    name: string;
    appointedDate: string;
    resignedDate?: string;
  }>;
  charitableObjectives: string;
  principalActivities: string;
  totalIncome: number;
  totalExpenditure: number;
  netMovement: number;
  closingFunds: number;
  governanceCodeCompliant: boolean;
  governanceCodeNote?: string;
  governanceEvidenceReference: string;
  governanceReviewedBy: string;
  governanceReviewedAtUtc: string;
  trusteeRemunerationPaid: boolean;
  trusteeRemunerationAmount: number;
  trusteeExpensesDetails?: string;
  hasInternationalTransfers: boolean;
  internationalTransferDetails?: string;
  filingDeadline: string;
}

export interface CharitySorpDecision {
  frameworkCode: "SORP-2019-FRS102" | "SORP-2026-FRS102";
  frameworkTitle: string;
  effectiveFrom: string;
  tier: number | null;
  sofaBasis: "natural-or-activity" | "activity" | "undetermined";
  automatedArtifactsSupported: boolean;
  manualProfessionalHandoffRequired: boolean;
  decisionReason: string;
  sources: Array<{
    sourceId: string;
    title: string;
    url: string;
    documentSha256: string | null;
    basis: string;
  }>;
  decisionSha256: string;
}

export interface CharityArtifactStatus {
  decision: CharitySorpDecision;
  package: null | {
    filingStatus: string;
    sofaGenerated: boolean;
    trusteesReportGenerated: boolean;
    sofaSha256: string | null;
    trusteesReportSha256: string | null;
    artifactReleaseCandidate: string | null;
    artifactSourceFingerprintSha256: string | null;
    sorpFrameworkCode: string | null;
    sorpTier: number | null;
    sofaBasis: string | null;
    charityNumberSnapshot: string | null;
    sofaClosingFunds: number | null;
    balanceSheetNetAssets: number | null;
    reconciliationDifference: number | null;
    reconciledAtUtc: string | null;
    trusteeReviewAccepted: boolean;
    trusteeReviewReference: string | null;
    trusteeReviewedBy: string | null;
    trusteeReviewedAtUtc: string | null;
    trusteeReviewArtifactSha256: string | null;
    trusteePopulationSha256: string | null;
    manualProfessionalHandoffReason: string | null;
    approvedBy: string | null;
    approvedAt: string | null;
    approvedArtifactManifestSha256: string | null;
    approvedReleaseCandidate: string | null;
  };
}

export const getCharityInfo = (companyId: number) =>
  apiFetch<CharityInfo | { message: "No charity info configured" }>(
    `/api/companies/${companyId}/charity/info`,
    {
      responseSchema: z.union([apiContracts.charityInfoSchema, apiContracts.absentCharityInfoSchema]),
      responseContract: "charity information",
    },
  );
export const saveCharityInfo = (companyId: number, data: CharityInfo) =>
  apiFetch<CharityInfo>(`/api/companies/${companyId}/charity/info`, {
    method: "PUT",
    body: JSON.stringify(data),
    responseSchema: apiContracts.charityInfoSchema,
    responseContract: "saved charity information",
  });

export const getSofa = (companyId: number, periodId: number) =>
  apiFetch<SofaData>(`/api/companies/${companyId}/periods/${periodId}/charity/sofa`, {
    responseSchema: apiContracts.sofaSchema,
    responseContract: "charity statement of financial activities",
  });
export const getTrusteesReport = (companyId: number, periodId: number) =>
  apiFetch<TrusteesReportData>(`/api/companies/${companyId}/periods/${periodId}/charity/trustees-report`, {
    responseSchema: apiContracts.trusteesReportSchema,
    responseContract: "trustees annual report",
  });

export const getCharitySorpDecision = (companyId: number, periodId: number) =>
  apiFetch<CharitySorpDecision>(`/api/companies/${companyId}/periods/${periodId}/charity/sorp-decision`, {
    responseSchema: apiContracts.charitySorpDecisionSchema,
    responseContract: "charity SORP decision",
  });

export const getCharityArtifactStatus = (companyId: number, periodId: number) =>
  apiFetch<CharityArtifactStatus>(`/api/companies/${companyId}/periods/${periodId}/charity/artifacts/status`, {
    responseSchema: apiContracts.charityArtifactStatusSchema,
    responseContract: "charity artifact status",
  });

export const recordCharityTrusteeReview = (
  companyId: number,
  periodId: number,
  data: { accepted: boolean; evidenceReference: string; evidenceArtifact: string },
) => apiFetch<unknown>(`/api/companies/${companyId}/periods/${periodId}/charity/trustee-review`, {
  method: "PUT",
  body: JSON.stringify(data),
});

export const getCharitySofaReviewUrl = (companyId: number, periodId: number) =>
  `${API_BASE}/api/companies/${companyId}/periods/${periodId}/charity/sofa/review-pdf`;
export const getCharityTrusteesReportReviewUrl = (companyId: number, periodId: number) =>
  `${API_BASE}/api/companies/${companyId}/periods/${periodId}/charity/trustees-report/review-pdf`;
export const getCharitySofaFinalUrl = (companyId: number, periodId: number) =>
  `${API_BASE}/api/companies/${companyId}/periods/${periodId}/charity/sofa/final`;
export const getCharityTrusteesReportFinalUrl = (companyId: number, periodId: number) =>
  `${API_BASE}/api/companies/${companyId}/periods/${periodId}/charity/trustees-report/final`;

export const getFundBalances = (companyId: number, periodId: number) =>
  apiFetch<FundBalance[]>(`/api/companies/${companyId}/periods/${periodId}/charity/funds`, {
    responseSchema: z.array(apiContracts.fundBalanceSchema),
    responseContract: "charity fund balance list",
  });
export const createFundBalance = (companyId: number, periodId: number, data: FundBalance) =>
  apiFetch<FundBalance>(`/api/companies/${companyId}/periods/${periodId}/charity/funds`, {
    method: "POST",
    body: JSON.stringify(data),
    responseSchema: apiContracts.fundBalanceSchema,
    responseContract: "created charity fund balance",
  });
export const updateFundBalance = (companyId: number, periodId: number, id: number, data: FundBalance) =>
  apiFetch<FundBalance>(`/api/companies/${companyId}/periods/${periodId}/charity/funds/${id}`, {
    method: "PUT",
    body: JSON.stringify(data),
    responseSchema: apiContracts.fundBalanceSchema,
    responseContract: "updated charity fund balance",
  });
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

// The large release-evidence runtime contract and invariant set lives in a focused module.

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
  revenueIxbrlGenerationSupported: boolean;
  revenueManualHandoffRequired: boolean;
  revenueGenerationSupportReason: string;
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
  companyId: number;
  periodId?: number | null;
  entityType: string;
  entityId: number;
  action: string;
  oldValueJson?: string | null;
  newValueJson?: string | null;
  userId?: string | null;
  timestamp: string;
}

export interface AuditLogPage {
  total: number;
  items: AuditLogEntry[];
  page: number;
  pageSize: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}

const auditLogEntrySchema = z.object({
  id: z.number().int().positive(),
  companyId: z.number().int().positive(),
  periodId: z.number().int().positive().nullable().optional(),
  entityType: z.string().min(1),
  entityId: z.number().int().nonnegative(),
  action: z.string().min(1),
  oldValueJson: z.string().nullable().optional(),
  newValueJson: z.string().nullable().optional(),
  userId: z.string().nullable().optional(),
  timestamp: apiContracts.isoDateTimeSchema,
});

const auditLogPageSchema = z.object({
  total: z.number().int().nonnegative(),
  items: z.array(auditLogEntrySchema),
  page: z.number().int().positive(),
  pageSize: z.number().int().positive().max(100),
  totalPages: z.number().int().positive(),
  hasPreviousPage: z.boolean(),
  hasNextPage: z.boolean(),
});

export function parseAuditLogPage(
  payload: unknown,
  expected?: { companyId: number; periodId?: number },
): AuditLogPage {
  const result = auditLogPageSchema.safeParse(payload);
  if (!result.success) {
    const issue = result.error.issues[0];
    const path = issue?.path.length ? issue.path.join(".") : "root";
    const message = issue?.message ?? "Invalid payload";
    throw new Error(`Invalid audit log response contract: ${path} - ${message}`);
  }

  const page: AuditLogPage = result.data;
  const expectedTotalPages = Math.max(1, Math.ceil(page.total / page.pageSize));
  if (page.totalPages !== expectedTotalPages) {
    throw new Error(
      `Invalid audit log response contract: totalPages - expected ${expectedTotalPages}, received ${page.totalPages}`,
    );
  }
  if (page.page > page.totalPages) {
    throw new Error("Invalid audit log response contract: page - cannot exceed totalPages");
  }
  if (page.hasPreviousPage !== (page.page > 1)) {
    throw new Error("Invalid audit log response contract: hasPreviousPage - inconsistent with page");
  }
  if (page.hasNextPage !== (page.page < page.totalPages)) {
    throw new Error("Invalid audit log response contract: hasNextPage - inconsistent with page");
  }
  if (page.items.length > page.pageSize) {
    throw new Error("Invalid audit log response contract: items - cannot exceed pageSize");
  }

  const ids = page.items.map((entry) => entry.id);
  if (new Set(ids).size !== ids.length) {
    throw new Error("Invalid audit log response contract: items - duplicate audit event IDs");
  }

  page.items.forEach((entry, index) => {
    const timestamp = Date.parse(entry.timestamp);
    if (Number.isNaN(timestamp)) {
      throw new Error(`Invalid audit log response contract: items.${index}.timestamp - expected an ISO timestamp`);
    }
    if (expected?.companyId != null && entry.companyId !== expected.companyId) {
      throw new Error(`Invalid audit log response contract: items.${index}.companyId - unexpected company`);
    }
    if (expected?.periodId != null && entry.periodId !== expected.periodId) {
      throw new Error(`Invalid audit log response contract: items.${index}.periodId - unexpected period`);
    }

    const previous = page.items[index - 1];
    if (!previous) return;
    const previousTimestamp = Date.parse(previous.timestamp);
    if (previousTimestamp < timestamp || (previousTimestamp === timestamp && previous.id < entry.id)) {
      throw new Error(
        `Invalid audit log response contract: items.${index} - events must use timestamp-descending, ID-descending order`,
      );
    }
  });

  return page;
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
  generationSupport: "filing-ready" | "manual-handoff-only";
  manualHandoffRequired: boolean;
  reviewPrototypeChecksPassed: boolean;
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
  apiFetch<FilingWorkflowStatus>(`/api/companies/${companyId}/periods/${periodId}/filing/status`, {
    responseSchema: apiContracts.filingWorkflowStatusSchema,
    responseContract: "filing workflow status",
  });

export const getFilingReadinessProfile = (companyId: number, periodId: number) =>
  apiFetch<FilingReadinessProfile>(`/api/companies/${companyId}/periods/${periodId}/filing/readiness-profile`, {
    responseSchema: apiContracts.filingReadinessProfileSchema,
    responseContract: "filing readiness profile",
  });

export const getProductionReadinessReport = async () =>
  parseProductionReadinessReport(await apiFetch<unknown>("/api/system/production-readiness"));

export const updateCroFilingStatus = (
  companyId: number,
  periodId: number,
  data: { status: string; reason?: string; submissionReference?: string },
  idempotencyKey?: string,
) =>
  apiFetch<unknown>(`/api/companies/${companyId}/periods/${periodId}/filing/cro-status`, {
    method: "PUT",
    ...idempotentMutation("filing-cro-status", idempotencyKey),
    body: JSON.stringify(data),
  });

export const confirmCroPayment = (companyId: number, periodId: number, idempotencyKey?: string) =>
  apiFetch<unknown>(`/api/companies/${companyId}/periods/${periodId}/filing/cro-payment`, {
    method: "POST",
    ...idempotentMutation("filing-cro-payment", idempotencyKey),
    body: JSON.stringify({}),
  });

export const validateIxbrl = (companyId: number, periodId: number, idempotencyKey?: string) =>
  apiFetch<RevenueFilingStatus>(`/api/companies/${companyId}/periods/${periodId}/filing/validate-ixbrl`, {
    method: "POST",
    ...idempotentMutation("filing-revenue-validate", idempotencyKey),
    responseSchema: apiContracts.revenueFilingStatusSchema,
    responseContract: "Revenue iXBRL review-prototype status",
  });

export const recordCharityReportGenerated = (companyId: number, periodId: number, reportType: string, idempotencyKey?: string) =>
  apiFetch<CharityFilingStatus>(
    `/api/companies/${companyId}/periods/${periodId}/filing/charity-report-generated`,
    {
      method: "POST",
      ...idempotentMutation("filing-charity-report", idempotencyKey),
      body: JSON.stringify({ reportType }),
      responseSchema: apiContracts.charityFilingStatusSchema,
      responseContract: "charity filing report status",
    },
  );

export const updateCharityFilingStatus = (
  companyId: number,
  periodId: number,
  data: { status: string; reason?: string; annualReturnReference?: string },
  idempotencyKey?: string,
) =>
  apiFetch<unknown>(
    `/api/companies/${companyId}/periods/${periodId}/filing/charity-status`,
    {
      method: "PUT",
      ...idempotentMutation("filing-charity-status", idempotencyKey),
      body: JSON.stringify(data),
    },
  );

export const getAuditLog = async (companyId: number, periodId?: number, page = 1, pageSize = 20) => {
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (periodId != null) params.set("periodId", String(periodId));
  return parseAuditLogPage(
    await apiFetch<unknown>(`/api/companies/${companyId}/audit-log?${params}`),
    { companyId, periodId },
  );
};

// --- Retained internal accountant outputs (support-only; never filing artifacts) ---

export type AccountantWorkingPaperPack = z.infer<typeof apiContracts.accountantWorkingPaperPackSchema>;
export type WorkingPaperSourceReference = AccountantWorkingPaperPack["leadSchedules"]["rows"][number]["sources"][number];
export type LeadScheduleRow = AccountantWorkingPaperPack["leadSchedules"]["rows"][number];
export type CategorizedWorkingPaperTransaction = AccountantWorkingPaperPack["categorizedTransactions"]["rows"][number];
export type WorkingPaperReviewException = AccountantWorkingPaperPack["reviewExceptions"]["items"][number];
export type AdjustedTrialBalanceWorkingPaperRow = AccountantWorkingPaperPack["adjustedTrialBalance"]["rows"][number];
export type CorporationTaxBridgeWorkingPaperRow = AccountantWorkingPaperPack["corporationTaxBridge"]["rows"][number];

export const getAccountantWorkingPaperPack = (companyId: number, periodId: number) =>
  apiFetch<AccountantWorkingPaperPack>(
    `/api/companies/${companyId}/periods/${periodId}/working-papers`,
    {
      responseSchema: apiContracts.accountantWorkingPaperPackSchema,
      responseContract: "retained internal accountant working-paper pack",
    },
  );

export const generateAccountantWorkingPaperPack = (companyId: number, periodId: number) =>
  apiFetch<AccountantWorkingPaperPack>(
    `/api/companies/${companyId}/periods/${periodId}/working-papers/generate`,
    {
      method: "POST",
      body: JSON.stringify({}),
      responseSchema: apiContracts.accountantWorkingPaperPackSchema,
      responseContract: "generated internal accountant working-paper pack",
    },
  );

// --- Immutable external filing handoff (manual CORE/ROS workflow only) ---

const externalHandoffBase = (companyId: number, periodId: number) =>
  `/api/companies/${companyId}/periods/${periodId}/external-filing-handoff`;

export const getExternalFilingHandoffWorkspace = (companyId: number, periodId: number) =>
  apiFetch<ExternalFilingHandoffWorkspace>(
    `${externalHandoffBase(companyId, periodId)}/workspace`,
    {
      responseSchema: externalFilingHandoffWorkspaceSchema,
      responseContract: "immutable external filing handoff workspace",
    },
  );

export const recordExternalFilingAuthority = (
  companyId: number,
  periodId: number,
  data: ExternalFilingAuthorityRequest,
) => {
  const input = externalFilingAuthorityRequestSchema.parse(data);
  return apiFetch<ExternalFilingAuthority>(
    `${externalHandoffBase(companyId, periodId)}/authorities`,
    {
      method: "POST",
      body: JSON.stringify(input),
      ...idempotentMutation("external-filing-authority"),
      responseSchema: externalFilingAuthoritySchema,
      responseContract: "retained external filing authority",
    },
  );
};

export const revokeExternalFilingAuthority = (
  companyId: number,
  periodId: number,
  authorityId: number,
  data: ExternalFilingAuthorityRevocationRequest,
) => {
  const input = externalFilingAuthorityRevocationRequestSchema.parse(data);
  return apiFetch<ExternalFilingAuthority>(
    `${externalHandoffBase(companyId, periodId)}/authorities/${authorityId}/revoke`,
    {
      method: "POST",
      body: JSON.stringify(input),
      ...idempotentMutation("external-filing-authority-revoke"),
      responseSchema: externalFilingAuthoritySchema,
      responseContract: "revoked external filing authority version",
    },
  );
};

export const generateCroHandoffSnapshot = (
  companyId: number,
  periodId: number,
  data: CroHandoffSnapshotRequest,
) => {
  const input = croHandoffSnapshotRequestSchema.parse(data);
  return apiFetch<ExternalFilingSnapshot>(
    `${externalHandoffBase(companyId, periodId)}/cro/snapshots`,
    {
      method: "POST",
      body: JSON.stringify(input),
      ...idempotentMutation("external-filing-cro-snapshot"),
      responseSchema: externalFilingSnapshotSchema,
      responseContract: "immutable CRO B1 handoff snapshot",
    },
  );
};

export const generateRevenueHandoffSnapshot = (
  companyId: number,
  periodId: number,
  data: RevenueHandoffSnapshotRequest,
) => {
  const input = revenueHandoffSnapshotRequestSchema.parse(data);
  return apiFetch<ExternalFilingSnapshot>(
    `${externalHandoffBase(companyId, periodId)}/revenue/snapshots`,
    {
      method: "POST",
      body: JSON.stringify(input),
      ...idempotentMutation("external-filing-revenue-snapshot"),
      responseSchema: externalFilingSnapshotSchema,
      responseContract: "immutable Revenue CT1 support handoff snapshot",
    },
  );
};

export const recordExternalFilingOutcome = (
  companyId: number,
  periodId: number,
  snapshotId: string,
  data: ExternalFilingOutcomeRequest,
) => {
  const input = externalFilingOutcomeRequestSchema.parse(data);
  return apiFetch<ExternalFilingOutcome>(
    `${externalHandoffBase(companyId, periodId)}/snapshots/${encodeURIComponent(snapshotId)}/outcomes`,
    {
      method: "POST",
      body: JSON.stringify(input),
      ...idempotentMutation("external-filing-outcome"),
      responseSchema: externalFilingOutcomeSchema,
      responseContract: "external filing outcome chronology event",
    },
  );
};

export const getExternalFilingHandoffArtifactUrl = (
  companyId: number,
  periodId: number,
  snapshotId: string,
) => `${externalHandoffBase(companyId, periodId)}/snapshots/${encodeURIComponent(snapshotId)}/artifact`;

export type {
  CroHandoffSnapshotRequest,
  ExternalFilingAuthority,
  ExternalFilingAuthorityRequest,
  ExternalFilingAuthorityRevocationRequest,
  ExternalFilingHandoffWorkspace,
  ExternalFilingOutcome,
  ExternalFilingOutcomeRequest,
  ExternalFilingSnapshot,
  RevenueHandoffSnapshotRequest,
};
