"use client";

import { type Key, use, useState, useEffect, useCallback, useMemo, useRef } from "react";
import {
  Card,
  Chip,
  TabsRoot,
  TabList,
  Tab,
  TabPanel,
} from "@heroui/react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import {
  Upload,
  Settings,
  Calculator,
  FileText,
  Download,
  BarChart3,
  Scale,
  ClipboardList,
} from "lucide-react";
import { toast as sonnerToast } from "sonner";
import {
  fetchDocumentBlob,
  getCompany,
  getPeriod,
  getYearEndSummary,
  getReadiness,
  getIxbrlUrl,
  getAgmPackUrl,
  getCroFilingPackUrl,
  getSignaturePageUrl,
  getBankAccounts,
  createBankAccount,
  getTransactions,
  categoriseTransaction,
  bulkCategoriseTransactions,
  getAdjustments,
  getAdjustmentSummary,
  generateAdjustments,
  approveAdjustment,
  uploadBankCsv,
  getFilingWorkflowStatus,
  getFilingReadinessProfile,
  validateIxbrl,
  calculateDeadlines,
  updateCroFilingStatus,
  confirmCroPayment,
  getDeadlines,
  markFiled,
  getAuditExemptionJeopardy,
  getSection307Note,
  getCategories,
  seedCategories,
  getOpeningBalances,
  saveOpeningBalance,
  deleteOpeningBalance,
  getTransactionRules,
  createTransactionRule,
  deleteTransactionRule,
  getAuditLog,
  type Company,
  type AccountingPeriod,
  type YearEndSummary,
  type ReadinessScore,
  type BankAccount,
  type ImportedTransaction,
  type TransactionListFilters,
  type TransactionSortDirection,
  type TransactionSortField,
  type Adjustment,
  type AdjustmentSummary,
  type FilingWorkflowStatus,
  type FilingReadinessProfile,
  type FilingDeadline,
  type AuditExemptionJeopardy,
  type AccountCategory,
  type OpeningBalance,
  type TransactionRule,
  type AuditLogEntry,
} from "@/lib/api";
import { Breadcrumbs } from "@/components/Breadcrumbs";
import { PeriodWorkspaceSkeleton } from "@/components/Skeleton";
import { ActionLink, WorkbenchHeader } from "@/components/workbench";
import { PeriodAdjustmentsWorkspace } from "@/components/period/PeriodAdjustmentsWorkspace";
import { PeriodWorkbenchOverview } from "@/components/period/PeriodWorkbenchOverview";
import { PeriodCategoriseWorkspace } from "@/components/period/PeriodCategoriseWorkspace";
import { PeriodFilingWorkspace } from "@/components/period/PeriodFilingWorkspace";
import { PeriodImportWorkspace } from "@/components/period/PeriodImportWorkspace";
import { PeriodStatementsWorkspace } from "@/components/period/PeriodStatementsWorkspace";
import { PeriodYearEndWorkspace } from "@/components/period/PeriodYearEndWorkspace";
import { useAuth } from "@/components/AuthProvider";
import { formatPeriodRange } from "@/lib/format";
import {
  selectionAfterTransactionScopeChange,
  setCurrentPageTransactionSelection,
  toggleTransactionSelection,
} from "@/lib/transactionSelection";
import { ResourceStateNotice } from "@/components/ResourceStateNotice";
import {
  INITIAL_RESOURCE_STATE,
  beginResourceLoad,
  completeResourceLoad,
  failResourceLoad,
  loadResourceGroup,
  type ResourceState,
} from "@/lib/resourceState";
import { useUnsavedChanges } from "@/lib/useUnsavedChanges";
import { InteractionAnnouncement } from "@/components/InteractionAnnouncement";
import {
  captureInteractionFocus,
  patchSearchHref,
  useInteractionAnnouncements,
  useLatestRequestSequence,
} from "@/lib/interactionState";
import {
  normaliseWorkspaceTab,
  parsePeriodWorkspaceQuery,
  type WorkspaceTabId,
} from "@/lib/periodWorkspaceQuery";

export function PeriodWorkspaceRoute({
  params,
}: {
  params: Promise<{ companyId: string; periodId: string }>;
}) {
  const { companyId, periodId } = use(params);
  const cId = Number(companyId);
  const pId = Number(periodId);
  const searchParams = useSearchParams();
  const pathname = usePathname();
  const router = useRouter();
  const { canApprove, canRead, canReadInternalWorkingPapers, canReview, canWriteWorkingPapers } = useAuth();
  const { announcement, announce } = useInteractionAnnouncements();
  const toast = useMemo(() => ({
    success(message: string) {
      sonnerToast.success(message);
      announce("success", message);
    },
    error(message: string) {
      sonnerToast.error(message);
      announce("error", message);
    },
    warning(message: string) {
      sonnerToast.warning(message);
      announce("warning", message);
    },
  }), [announce]);
  const pageLoadRequestSequence = useLatestRequestSequence();
  const periodRequestSequence = useLatestRequestSequence();
  const filingRequestSequence = useLatestRequestSequence();
  const transactionRequestSequence = useLatestRequestSequence();
  const adjustmentRequestSequence = useLatestRequestSequence();
  const auditRequestSequence = useLatestRequestSequence();
  const [selectedWorkspaceTab, setSelectedWorkspaceTab] = useState<WorkspaceTabId>(() =>
    parsePeriodWorkspaceQuery(searchParams).selectedWorkspaceTab,
  );

  const [company, setCompany] = useState<Company | null>(null);
  const [period, setPeriod] = useState<AccountingPeriod | null>(null);
  const [yearEnd, setYearEnd] = useState<YearEndSummary | null>(null);
  const [readiness, setReadiness] = useState<ReadinessScore | null>(null);
  const [bankAccounts, setBankAccounts] = useState<BankAccount[]>([]);
  const [showBankForm, setShowBankForm] = useState(false);
  const [savingBankAccount, setSavingBankAccount] = useState(false);
  const [bankForm, setBankForm] = useState({
    name: "",
    iban: "",
    openingBalance: "0",
    openingBalanceDate: "",
    currency: "EUR",
  });
  const [filingStatus, setFilingStatus] = useState<FilingWorkflowStatus | null>(null);
  const [filingReadinessProfile, setFilingReadinessProfile] = useState<FilingReadinessProfile | null>(null);
  const [deadlinesList, setDeadlinesList] = useState<FilingDeadline[]>([]);
  const [filingReferences, setFilingReferences] = useState<Record<number, string>>({});
  const [croSubmissionReference, setCroSubmissionReference] = useState("");
  const [markingFiledId, setMarkingFiledId] = useState<number | null>(null);
  const [jeopardy, setJeopardy] = useState<AuditExemptionJeopardy | null>(null);
  const [section307Note, setSection307Note] = useState<string | null>(null);
  const [categories, setCategories] = useState<AccountCategory[]>([]);
  const [openingBalances, setOpeningBalances] = useState<OpeningBalance[]>([]);
  const [openingBalanceForm, setOpeningBalanceForm] = useState({
    categoryId: "",
    side: "debit",
    amount: "",
    sourceNote: "",
  });
  const [savingOpeningBalance, setSavingOpeningBalance] = useState(false);
  const [deletingOpeningCategoryId, setDeletingOpeningCategoryId] = useState<number | null>(null);
  const [transactionRules, setTransactionRules] = useState<TransactionRule[]>([]);
  const [auditLog, setAuditLog] = useState<AuditLogEntry[]>([]);
  const [auditTotal, setAuditTotal] = useState(0);
  const [auditPage, setAuditPage] = useState(1);
  const [auditPageSize, setAuditPageSize] = useState(50);
  const [auditLoadedPage, setAuditLoadedPage] = useState(1);
  const [auditLoadedPageSize, setAuditLoadedPageSize] = useState(50);
  const [auditPageCount, setAuditPageCount] = useState(1);
  const [loadingAuditLog, setLoadingAuditLog] = useState(false);
  const [auditState, setAuditState] = useState<ResourceState>(INITIAL_RESOURCE_STATE);
  const [periodState, setPeriodState] = useState<ResourceState>(INITIAL_RESOURCE_STATE);
  const [filingState, setFilingState] = useState<ResourceState>(INITIAL_RESOURCE_STATE);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [generatingAdj, setGeneratingAdj] = useState(false);
  const [validatingIxbrl, setValidatingIxbrl] = useState(false);
  const [downloadingDocument, setDownloadingDocument] = useState<string | null>(null);

  useEffect(() => {
    const query = parsePeriodWorkspaceQuery(searchParams);
    setSelectedWorkspaceTab(query.selectedWorkspaceTab);
    setTxFilterStatus(query.txFilterStatus);
    setTxFilterCategory(query.txFilterCategory);
    setTxFilterBank(query.txFilterBank);
    setTxFilterSearch(query.txFilterSearch);
    setTxFilterSearchInput(query.txFilterSearch);
    setTransactionPage(query.transactionPage);
    setTransactionPageSize(query.transactionPageSize);
    setTransactionSortBy(query.transactionSortBy);
    setTransactionSortDirection(query.transactionSortDirection);
    setAdjFilterApproved(query.adjFilterApproved);
    setAdjFilterType(query.adjFilterType);
  }, [searchParams]);

  const updateWorkspaceQuery = useCallback((patch: Record<string, string | number | null>) => {
    const currentSearch = typeof window === "undefined" ? searchParams.toString() : window.location.search;
    router.push(patchSearchHref(pathname, currentSearch, patch), { scroll: false });
  }, [pathname, router, searchParams]);

  const handleWorkspaceTabChange = useCallback((key: Key) => {
    const nextTab = normaliseWorkspaceTab(String(key));
    setSelectedWorkspaceTab(nextTab);
    updateWorkspaceQuery({ tab: nextTab === "import" ? null : nextTab });
  }, [updateWorkspaceQuery]);

  // Import tab state
  const [selectedBankAccountId, setSelectedBankAccountId] = useState<number | "">("");
  const [uploading, setUploading] = useState(false);
  const [uploadResult, setUploadResult] = useState<{
    rowsImported: number;
    duplicateCandidates: number;
    autoCategorised: number;
    importBatchId?: number;
    sourceFilename: string;
    sourceFileSha256: string;
    sourceFileBytes: number;
    warnings: string[];
  } | null>(null);
  const [uploadError, setUploadError] = useState<string | null>(null);

  // Categorise tab state
  const [transactions, setTransactions] = useState<ImportedTransaction[]>([]);
  const [transactionTotal, setTransactionTotal] = useState(0);
  const [transactionFilteredTotal, setTransactionFilteredTotal] = useState(0);
  const [categorisedCount, setCategorisedCount] = useState(0);
  const [uncategorisedCount, setUncategorisedCount] = useState(0);
  const [transactionPage, setTransactionPage] = useState(() => parsePeriodWorkspaceQuery(searchParams).transactionPage);
  const [transactionPageSize, setTransactionPageSize] = useState(() =>
    parsePeriodWorkspaceQuery(searchParams).transactionPageSize);
  const [transactionPageCount, setTransactionPageCount] = useState(1);
  const [transactionSortBy, setTransactionSortBy] = useState<TransactionSortField>(() =>
    parsePeriodWorkspaceQuery(searchParams).transactionSortBy);
  const [transactionSortDirection, setTransactionSortDirection] = useState<TransactionSortDirection>(() =>
    parsePeriodWorkspaceQuery(searchParams).transactionSortDirection);
  const [loadingTransactions, setLoadingTransactions] = useState(false);
  const [categorisingId, setCategorisingId] = useState<number | null>(null);
  const [txFilterCategory, setTxFilterCategory] = useState<string>(() => parsePeriodWorkspaceQuery(searchParams).txFilterCategory);
  const [txFilterBank, setTxFilterBank] = useState<string>(() => parsePeriodWorkspaceQuery(searchParams).txFilterBank);
  const [txFilterStatus, setTxFilterStatus] = useState<string>(() =>
    parsePeriodWorkspaceQuery(searchParams).txFilterStatus);
  const [txFilterSearch, setTxFilterSearch] = useState(() => parsePeriodWorkspaceQuery(searchParams).txFilterSearch);
  const [txFilterSearchInput, setTxFilterSearchInput] = useState(() => parsePeriodWorkspaceQuery(searchParams).txFilterSearch);
  const txSearchTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const [selectedTransactionIds, setSelectedTransactionIds] = useState<number[]>([]);
  const selectedTransactionIdsRef = useRef<number[]>([]);
  const [selectionAnnouncement, setSelectionAnnouncement] = useState("");
  const [transactionState, setTransactionState] = useState<ResourceState>(INITIAL_RESOURCE_STATE);
  const [bulkCategoryId, setBulkCategoryId] = useState("");
  const [bulkCategorising, setBulkCategorising] = useState(false);
  const [ruleForm, setRuleForm] = useState({ pattern: "", categoryId: "", priority: "100" });
  const [savingRule, setSavingRule] = useState(false);
  const [deletingRuleId, setDeletingRuleId] = useState<number | null>(null);

  const hasPeriodWorkspaceDraft = useMemo(() => {
    const bankDraft = showBankForm && (
      bankForm.name !== ""
      || bankForm.iban !== ""
      || bankForm.openingBalance !== "0"
      || bankForm.openingBalanceDate !== ""
      || bankForm.currency !== "EUR"
    );
    const openingBalanceDraft = openingBalanceForm.categoryId !== ""
      || openingBalanceForm.side !== "debit"
      || openingBalanceForm.amount !== ""
      || openingBalanceForm.sourceNote !== "";
    const ruleDraft = ruleForm.pattern !== ""
      || ruleForm.categoryId !== ""
      || ruleForm.priority !== "100";
    const filingReferenceDraft = Object.values(filingReferences).some((value) => value.trim() !== "")
      || croSubmissionReference !== (filingStatus?.cro.submissionReference ?? "");
    return bankDraft || openingBalanceDraft || ruleDraft || bulkCategoryId !== "" || filingReferenceDraft;
  }, [
    bankForm, bulkCategoryId, croSubmissionReference, filingReferences, filingStatus,
    openingBalanceForm, ruleForm, showBankForm,
  ]);
  useUnsavedChanges(hasPeriodWorkspaceDraft);

  // Adjustments tab state
  const [adjustments, setAdjustments] = useState<Adjustment[]>([]);
  const [adjSummary, setAdjSummary] = useState<AdjustmentSummary | null>(null);
  const [loadingAdjustments, setLoadingAdjustments] = useState(false);
  const [adjustmentState, setAdjustmentState] = useState<ResourceState>(INITIAL_RESOURCE_STATE);
  const [approvingId, setApprovingId] = useState<number | null>(null);
  const [adjFilterApproved, setAdjFilterApproved] = useState<string>(() =>
    parsePeriodWorkspaceQuery(searchParams).adjFilterApproved);
  const [adjFilterType, setAdjFilterType] = useState<string>(() =>
    parsePeriodWorkspaceQuery(searchParams).adjFilterType);

  useEffect(() => {
    if (txSearchTimerRef.current) clearTimeout(txSearchTimerRef.current);
    pageLoadRequestSequence.invalidate();
    periodRequestSequence.invalidate();
    filingRequestSequence.invalidate();
    transactionRequestSequence.invalidate();
    adjustmentRequestSequence.invalidate();
    auditRequestSequence.invalidate();
    const currentSelection = selectedTransactionIdsRef.current;
    const nextSelection = selectionAfterTransactionScopeChange(currentSelection, "period");
    selectedTransactionIdsRef.current = nextSelection;
    setSelectedTransactionIds(nextSelection);
    if (currentSelection.length > 0 && nextSelection.length === 0) {
      setSelectionAnnouncement("Selection cleared because the accounting period changed.");
    }
    setAuditLog([]);
    setAuditTotal(0);
    setAuditPage(1);
    setAuditLoadedPage(1);
    setAuditLoadedPageSize(50);
    setAuditPageCount(1);
    setAuditState(INITIAL_RESOURCE_STATE);
    setTransactionState(INITIAL_RESOURCE_STATE);
    setAdjustmentState(INITIAL_RESOURCE_STATE);
    setPeriodState(INITIAL_RESOURCE_STATE);
    setFilingState(INITIAL_RESOURCE_STATE);
  }, [
    adjustmentRequestSequence,
    auditRequestSequence,
    cId,
    filingRequestSequence,
    pageLoadRequestSequence,
    pId,
    periodRequestSequence,
    transactionRequestSequence,
  ]);

  useEffect(() => {
    selectedTransactionIdsRef.current = selectedTransactionIds;
  }, [selectedTransactionIds]);

  useEffect(() => () => {
    if (txSearchTimerRef.current) clearTimeout(txSearchTimerRef.current);
  }, []);

  const refreshFilingState = useCallback(async (onlyKeys?: string[]) => {
    const request = filingRequestSequence.begin();
    const loaders = {
      "filing-status": () => getFilingWorkflowStatus(cId, pId),
      "filing-readiness-profile": () => getFilingReadinessProfile(cId, pId),
    };
    const keys = (onlyKeys ?? Object.keys(loaders)) as Array<keyof typeof loaders>;
    setFilingState((current) => beginResourceLoad(current, current.hasRetainedData));
    const result = await loadResourceGroup(loaders, keys);
    if (!request.isLatest()) return;
    if (result.values["filing-status"] !== undefined) {
      setFilingStatus(result.values["filing-status"]);
      setCroSubmissionReference(result.values["filing-status"].cro.submissionReference ?? "");
    }
    if (result.values["filing-readiness-profile"] !== undefined) {
      setFilingReadinessProfile(result.values["filing-readiness-profile"]);
    }
    if (result.failedResourceKeys.length === 0) setFilingState(completeResourceLoad(false));
    else setFilingState((current) => failResourceLoad({
      failedResourceKeys: result.failedResourceKeys,
      errors: result.errors,
    }, current.hasRetainedData || Object.keys(result.values).length > 0));
  }, [cId, filingRequestSequence, pId]);

  const loadPeriodResources = useCallback(async (onlyKeys?: string[]) => {
    const request = periodRequestSequence.begin();
    const loaders = {
      company: () => getCompany(cId),
      period: () => getPeriod(cId, pId),
      "bank-accounts": () => getBankAccounts(cId),
      "year-end-summary": () => getYearEndSummary(cId, pId),
      readiness: () => getReadiness(cId, pId),
      deadlines: () => getDeadlines(cId),
      "audit-exemption-jeopardy": () => getAuditExemptionJeopardy(cId),
      "section-307-note": () => getSection307Note(cId, pId),
      categories: () => getCategories(cId),
      "opening-balances": () => getOpeningBalances(cId, pId),
      "transaction-rules": () => getTransactionRules(cId),
    };
    const keys = (onlyKeys ?? Object.keys(loaders)) as Array<keyof typeof loaders>;
    setPeriodState((current) => beginResourceLoad(current, current.hasRetainedData));
    const result = await loadResourceGroup(loaders, keys);
    if (!request.isLatest()) return;
    if (result.values.company !== undefined) setCompany(result.values.company);
    if (result.values.period !== undefined) setPeriod(result.values.period);
    if (result.values["bank-accounts"] !== undefined) {
      const bankData = result.values["bank-accounts"];
      setBankAccounts(bankData);
      setSelectedBankAccountId((current) => bankData.length > 0 && current === "" ? bankData[0].id : current);
    }
    if (result.values["year-end-summary"] !== undefined) setYearEnd(result.values["year-end-summary"]);
    if (result.values.readiness !== undefined) setReadiness(result.values.readiness);
    if (result.values.deadlines !== undefined) {
      setDeadlinesList(result.values.deadlines.filter((deadline) => deadline.periodId === pId));
    }
    if (result.values["audit-exemption-jeopardy"] !== undefined) setJeopardy(result.values["audit-exemption-jeopardy"]);
    if (result.values["section-307-note"] !== undefined) setSection307Note(result.values["section-307-note"].note);
    if (result.values.categories !== undefined) setCategories(result.values.categories);
    if (result.values["opening-balances"] !== undefined) setOpeningBalances(result.values["opening-balances"]);
    if (result.values["transaction-rules"] !== undefined) setTransactionRules(result.values["transaction-rules"]);

    const shellError = result.errors.company ?? result.errors.period;
    setError(shellError ?? null);
    if (result.failedResourceKeys.length === 0) setPeriodState(completeResourceLoad(false));
    else setPeriodState((current) => failResourceLoad({
      failedResourceKeys: result.failedResourceKeys,
      errors: result.errors,
    }, current.hasRetainedData || Object.keys(result.values).length > 0));
  }, [cId, pId, periodRequestSequence]);

  const loadData = useCallback(async () => {
    const request = pageLoadRequestSequence.begin();
    setLoading(true);
    try {
      if (canWriteWorkingPapers) {
        try {
          await calculateDeadlines(cId, pId);
        } catch {
          // The deadlines read below remains authoritative and reports unavailable evidence explicitly.
        }
      }
      await Promise.all([loadPeriodResources(), refreshFilingState()]);
    } finally {
      if (request.isLatest()) setLoading(false);
    }
  }, [cId, pId, canWriteWorkingPapers, loadPeriodResources, pageLoadRequestSequence, refreshFilingState]);

  const loadTransactions = useCallback(async () => {
    const request = transactionRequestSequence.begin();
    setLoadingTransactions(true);
    setTransactionState((current) => beginResourceLoad(current, current.hasRetainedData));
    try {
      const filters: TransactionListFilters = {
        sortBy: transactionSortBy,
        sortDirection: transactionSortDirection,
      };
      if (txFilterStatus === "uncategorised") filters.uncategorised = true;
      if (txFilterCategory) filters.categoryId = Number(txFilterCategory);
      if (txFilterBank) filters.bankAccountId = Number(txFilterBank);
      if (txFilterSearch.trim()) filters.search = txFilterSearch.trim();
      const data = await getTransactions(cId, pId, transactionPage, transactionPageSize, filters);
      if (!request.isLatest()) return;
      setTransactions(data.items);
      setTransactionFilteredTotal(data.total);
      setTransactionTotal(data.aggregates.total);
      setCategorisedCount(data.aggregates.categorised);
      setUncategorisedCount(data.aggregates.uncategorised);
      setTransactionPage(data.page);
      setTransactionPageSize(data.pageSize);
      setTransactionPageCount(data.totalPages);
      setTransactionState(completeResourceLoad(data.total === 0));
    } catch (loadError) {
      if (!request.isLatest()) return;
      const message = loadError instanceof Error ? loadError.message : "Failed to load transactions";
      setTransactionState((current) => failResourceLoad({
        failedResourceKeys: ["transactions"],
        errors: { transactions: message },
      }, current.hasRetainedData));
    } finally {
      if (request.isLatest()) setLoadingTransactions(false);
    }
  }, [
    cId,
    pId,
    transactionPage,
    transactionPageSize,
    transactionSortBy,
    transactionSortDirection,
    txFilterCategory,
    txFilterBank,
    txFilterStatus,
    txFilterSearch,
    transactionRequestSequence,
  ]);

  const loadAuditLog = useCallback(async () => {
    const request = auditRequestSequence.begin();
    setLoadingAuditLog(true);
    setAuditState((current) => beginResourceLoad(current, current.hasRetainedData));
    try {
      const audit = await getAuditLog(cId, pId, auditPage, auditPageSize);
      if (!request.isLatest()) return;
      setAuditLog(audit.items);
      setAuditTotal(audit.total);
      setAuditPage(audit.page);
      setAuditPageSize(audit.pageSize);
      setAuditLoadedPage(audit.page);
      setAuditLoadedPageSize(audit.pageSize);
      setAuditPageCount(audit.totalPages);
      setAuditState(completeResourceLoad(audit.total === 0));
    } catch (err) {
      if (!request.isLatest()) return;
      const message = err instanceof Error ? err.message : "Failed to load audit events";
      setAuditState((current) => failResourceLoad({
        failedResourceKeys: ["audit-log"],
        errors: { "audit-log": message },
      }, current.hasRetainedData));
    } finally {
      if (request.isLatest()) setLoadingAuditLog(false);
    }
  }, [auditPage, auditPageSize, auditRequestSequence, cId, pId]);

  const loadAdjustments = useCallback(async () => {
    const request = adjustmentRequestSequence.begin();
    setLoadingAdjustments(true);
    setAdjustmentState((current) => beginResourceLoad(current, current.hasRetainedData));
    try {
      const filters: { approved?: boolean; isAuto?: boolean } = {};
      if (adjFilterApproved === "approved") filters.approved = true;
      else if (adjFilterApproved === "pending") filters.approved = false;
      if (adjFilterType === "auto") filters.isAuto = true;
      else if (adjFilterType === "manual") filters.isAuto = false;
      const [adjData, summaryData] = await Promise.all([
        getAdjustments(cId, pId, filters),
        getAdjustmentSummary(cId, pId),
      ]);
      if (!request.isLatest()) return;
      setAdjustments(adjData);
      setAdjSummary(summaryData);
      setAdjustmentState(completeResourceLoad(adjData.length === 0));
    } catch (loadError) {
      if (!request.isLatest()) return;
      const message = loadError instanceof Error ? loadError.message : "Failed to load adjustments";
      setAdjustmentState((current) => failResourceLoad({
        failedResourceKeys: ["adjustments"],
        errors: { adjustments: message },
      }, current.hasRetainedData));
    } finally {
      if (request.isLatest()) setLoadingAdjustments(false);
    }
  }, [adjustmentRequestSequence, cId, pId, adjFilterApproved, adjFilterType]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  useEffect(() => {
    if (!loading) {
      loadTransactions();
      loadAdjustments();
    }
  }, [loading, loadTransactions, loadAdjustments]);

  useEffect(() => {
    if (!loading) loadAuditLog();
  }, [loading, loadAuditLog]);

  async function handleFileUpload(file: File) {
    if (!selectedBankAccountId) {
      toast.error("Please select a bank account before uploading.");
      return;
    }
    if (!file.name.endsWith(".csv")) {
      toast.error("Only CSV files are supported.");
      return;
    }
    const focus = captureInteractionFocus();
    setUploading(true);
    setUploadError(null);
    setUploadResult(null);
    try {
      const result = await uploadBankCsv(cId, Number(selectedBankAccountId), pId, file);
      setUploadResult({
        rowsImported: result.importedRows,
        duplicateCandidates: result.duplicateCandidates,
        autoCategorised: result.autoCategorised,
        importBatchId: result.importBatchId,
        sourceFilename: result.sourceFilename,
        sourceFileSha256: result.sourceFileSha256,
        sourceFileBytes: result.sourceFileBytes,
        warnings: result.warnings,
      });
      if (result.importedRows === 0) toast.error(`No transactions imported · ${result.warnings.length} warning${result.warnings.length === 1 ? "" : "s"}`);
      else if (result.warnings.length > 0 || result.duplicateCandidates > 0) toast.warning(`Imported ${result.importedRows} transactions · ${result.warnings.length} warnings · ${result.duplicateCandidates} duplicate candidates`);
      else toast.success(`Imported ${result.importedRows} transactions`);
      await loadTransactions();
      await loadData();
    } catch (err) {
      const msg = err instanceof Error ? err.message : "Upload failed";
      setUploadError(msg);
      toast.error(msg);
    } finally {
      setUploading(false);
      focus.restore();
    }
  }

  async function handleCreateBankAccount() {
    if (!bankForm.name.trim()) {
      toast.error("Bank account name is required");
      return;
    }

    const openingBalance = Number(bankForm.openingBalance || 0);
    const openingBalanceDate = bankForm.openingBalanceDate || period?.periodStart || "";
    if (openingBalance !== 0 && !openingBalanceDate) {
      toast.error("Opening balance date is required");
      return;
    }

    const focus = captureInteractionFocus("period-add-bank-account-toggle");
    setSavingBankAccount(true);
    try {
      const account = await createBankAccount(cId, {
        name: bankForm.name.trim(),
        iban: bankForm.iban.trim() || undefined,
        currency: bankForm.currency || "EUR",
        openingBalance,
        openingBalanceDate: openingBalance !== 0 ? openingBalanceDate : undefined,
      });
      setBankAccounts((current) => [...current, account]);
      setSelectedBankAccountId(account.id);
      setBankForm({ name: "", iban: "", openingBalance: "0", openingBalanceDate: period?.periodStart ?? "", currency: "EUR" });
      setShowBankForm(false);
      toast.success("Bank account linked");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to create bank account");
    } finally {
      setSavingBankAccount(false);
      focus.restore();
    }
  }

  async function handleSaveOpeningBalance() {
    if (!openingBalanceForm.categoryId) {
      toast.error("Choose an account category for the opening balance");
      return;
    }

    const amount = Number(openingBalanceForm.amount || 0);
    if (amount <= 0) {
      toast.error("Enter a positive opening balance amount");
      return;
    }

    const focus = captureInteractionFocus();
    setSavingOpeningBalance(true);
    try {
      const categoryId = Number(openingBalanceForm.categoryId);
      const saved = await saveOpeningBalance(cId, pId, categoryId, {
        debit: openingBalanceForm.side === "debit" ? amount : 0,
        credit: openingBalanceForm.side === "credit" ? amount : 0,
        sourceNote: openingBalanceForm.sourceNote.trim() || undefined,
        reviewed: true,
      });
      setOpeningBalances((current) => [
        ...current.filter((balance) => balance.accountCategoryId !== categoryId),
        saved,
      ].sort((a, b) => a.accountCategory.code.localeCompare(b.accountCategory.code)));
      setOpeningBalanceForm({ categoryId: "", side: "debit", amount: "", sourceNote: "" });
      toast.success("Opening balance saved and reviewed");
      await loadData();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to save opening balance");
    } finally {
      setSavingOpeningBalance(false);
      focus.restore();
    }
  }

  async function handleDeleteOpeningBalance(categoryId: number) {
    setDeletingOpeningCategoryId(categoryId);
    try {
      await deleteOpeningBalance(cId, pId, categoryId);
      setOpeningBalances((current) => current.filter((balance) => balance.accountCategoryId !== categoryId));
      toast.success("Opening balance removed");
      await loadData();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to remove opening balance");
      throw err;
    } finally {
      setDeletingOpeningCategoryId(null);
    }
  }

  async function handleSeedCategories() {
    const focus = captureInteractionFocus();
    try {
      const cats = await seedCategories(cId);
      setCategories(cats);
      toast.success(`${cats.length} categories seeded`);
    } catch {
      toast.error("Failed to seed categories");
    } finally {
      focus.restore();
    }
  }

  async function handleCategoriseTransaction(transactionId: number, categoryId: number) {
    const focus = captureInteractionFocus("transaction-register");
    setCategorisingId(transactionId);
    try {
      const updated = await categoriseTransaction(cId, pId, transactionId, categoryId);
      setTransactions((current) =>
        current.map((tx) => (tx.id === transactionId ? { ...tx, ...updated } : tx))
      );
      setSelectedTransactionIds((current) => toggleTransactionSelection(current, transactionId, false));
      await loadTransactions();
      toast.success("Transaction categorised");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to categorise transaction");
    } finally {
      setCategorisingId(null);
      focus.restore();
    }
  }

  async function handleBulkCategoriseTransactions() {
    if (selectedTransactionIds.length === 0 || !bulkCategoryId) {
      toast.error("Select transactions and a category first");
      return;
    }

    const focus = captureInteractionFocus("transaction-register");
    setBulkCategorising(true);
    try {
      const result = await bulkCategoriseTransactions(cId, pId, selectedTransactionIds, Number(bulkCategoryId));
      toast.success(`${result.updated} transactions categorised`);
      setSelectedTransactionIds([]);
      setBulkCategoryId("");
      await loadTransactions();
      await loadData();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to bulk categorise transactions");
    } finally {
      setBulkCategorising(false);
      focus.restore();
    }
  }

  async function handleCreateRule() {
    if (!ruleForm.pattern.trim() || !ruleForm.categoryId) {
      toast.error("Rule pattern and category are required");
      return;
    }

    const focus = captureInteractionFocus();
    setSavingRule(true);
    try {
      const created = await createTransactionRule(cId, {
        pattern: ruleForm.pattern.trim(),
        categoryId: Number(ruleForm.categoryId),
        priority: Number(ruleForm.priority || 100),
      });
      setTransactionRules((current) => [...current, created].sort((a, b) => a.priority - b.priority));
      setRuleForm({ pattern: "", categoryId: "", priority: "100" });
      toast.success("Transaction rule created");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to create transaction rule");
    } finally {
      setSavingRule(false);
      focus.restore();
    }
  }

  async function handleDeleteRule(ruleId: number) {
    setDeletingRuleId(ruleId);
    try {
      await deleteTransactionRule(cId, ruleId);
      setTransactionRules((current) => current.filter((rule) => rule.id !== ruleId));
      toast.success("Transaction rule deleted");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to delete transaction rule");
      throw err;
    } finally {
      setDeletingRuleId(null);
    }
  }

  function handleTransactionSearchInput(value: string) {
    setTxFilterSearchInput(value);
    if (txSearchTimerRef.current) clearTimeout(txSearchTimerRef.current);
    txSearchTimerRef.current = setTimeout(() => {
      clearTransactionSelection("search filter");
      setTransactionPage(1);
      setTxFilterSearch(value);
      updateWorkspaceQuery({ txSearch: value.trim() || null, txPage: null });
    }, 400);
  }

  function clearTransactionSelection(changedScope: string) {
    const current = selectedTransactionIdsRef.current;
    const next = selectionAfterTransactionScopeChange(current, "filter");
    selectedTransactionIdsRef.current = next;
    setSelectedTransactionIds(next);
    if (current.length > 0 && next.length === 0) {
      setSelectionAnnouncement(`Selection cleared because the ${changedScope} changed.`);
    }
  }

  function handleTransactionStatusFilter(value: string) {
    clearTransactionSelection("status filter");
    setTransactionPage(1);
    setTxFilterStatus(value);
    updateWorkspaceQuery({ txStatus: value || null, txPage: null });
  }

  function handleTransactionCategoryFilter(value: string) {
    clearTransactionSelection("category filter");
    setTransactionPage(1);
    setTxFilterCategory(value);
    updateWorkspaceQuery({ txCategory: value || null, txPage: null });
  }

  function handleTransactionBankFilter(value: string) {
    clearTransactionSelection("bank filter");
    setTransactionPage(1);
    setTxFilterBank(value);
    updateWorkspaceQuery({ txBank: value || null, txPage: null });
  }

  function handleTransactionPageChange(page: number) {
    setTransactionPage(page);
    updateWorkspaceQuery({ txPage: page <= 1 ? null : page });
  }

  function handleTransactionPageSizeChange(pageSize: number) {
    setTransactionPage(1);
    setTransactionPageSize(pageSize);
    updateWorkspaceQuery({ txPage: null, txPageSize: pageSize === 50 ? null : pageSize });
  }

  function handleTransactionSortByChange(sortBy: TransactionSortField) {
    setTransactionPage(1);
    setTransactionSortBy(sortBy);
    updateWorkspaceQuery({ txPage: null, txSort: sortBy === "date" ? null : sortBy });
  }

  function handleTransactionSortDirectionChange(sortDirection: TransactionSortDirection) {
    setTransactionPage(1);
    setTransactionSortDirection(sortDirection);
    updateWorkspaceQuery({ txPage: null, txDirection: sortDirection === "desc" ? null : sortDirection });
  }

  function handleAdjustmentApprovalFilter(value: string) {
    setAdjFilterApproved(value);
    updateWorkspaceQuery({ adjApproval: value || null });
  }

  function handleAdjustmentSourceFilter(value: string) {
    setAdjFilterType(value);
    updateWorkspaceQuery({ adjSource: value || null });
  }

  async function handleMarkDeadlineFiled(deadline: FilingDeadline, filingReference?: string) {
    const focus = captureInteractionFocus("period-filing-workspace");
    setMarkingFiledId(deadline.id);
    try {
      await markFiled(cId, pId, {
        deadlineType: deadline.deadlineType,
        filedDate: new Date().toISOString().split("T")[0],
        ...(filingReference ? { filingReference } : {}),
      });
      setFilingReferences((current) => {
        const next = { ...current };
        delete next[deadline.id];
        return next;
      });
      toast.success(`${deadline.deadlineType} filing marked as complete`);
      await loadData();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to mark as filed");
    } finally {
      setMarkingFiledId(null);
      focus.restore();
    }
  }

  async function handleGenerateAdjustments() {
    const focus = captureInteractionFocus();
    setGeneratingAdj(true);
    try {
      const summary = await generateAdjustments(cId, pId);
      setAdjSummary(summary);
      toast.success("Adjustments generated successfully");
      await loadAdjustments();
      await loadData();
    } catch (err) {
      const msg = err instanceof Error ? err.message : "Failed to generate adjustments";
      toast.error(msg);
      setError(msg);
    } finally {
      setGeneratingAdj(false);
      focus.restore();
    }
  }

  async function handleApproveAdjustment(id: number) {
    const focus = captureInteractionFocus(`adjustment-review-${id}`);
    setApprovingId(id);
    try {
      await approveAdjustment(cId, pId, id);
      toast.success("Adjustment approved");
      await loadAdjustments();
      await loadData();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to approve adjustment");
    } finally {
      setApprovingId(null);
      focus.restore();
    }
  }

  function documentDownloadName(label: string, extension = "pdf") {
    const safeLabel = label
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, "-")
      .replace(/^-+|-+$/g, "");

    return `${safeLabel || "accounts-document"}.${extension}`;
  }

  async function downloadDocument(url: string, label: string, documentType?: "accounts" | "signature", requiresRegime = false, extension = "pdf") {
    if (requiresRegime && !period?.filingRegime) {
      toast.error("Confirm the filing regime before generating CRO documents.");
      return;
    }

    const focus = captureInteractionFocus();
    setDownloadingDocument(label);
    try {
      const blob = await fetchDocumentBlob(url, documentType ? "POST" : "GET");
      const objectUrl = URL.createObjectURL(blob);
      const anchor = document.createElement("a");
      let downloadDelivered = false;

      try {
        anchor.href = objectUrl;
        anchor.download = documentDownloadName(label, extension);
        anchor.rel = "noopener noreferrer";
        document.body.appendChild(anchor);
        anchor.click();
        downloadDelivered = true;
      } finally {
        anchor.remove();
        window.setTimeout(() => URL.revokeObjectURL(objectUrl), 60000);
      }

      if (documentType && downloadDelivered) {
        try {
          await refreshFilingState();
        } catch {
          // Download still opens; the readiness panel will refresh on the next status load.
        }
      }
    } catch (err) {
      toast.error(err instanceof Error ? err.message : `Failed to generate ${label}`);
    } finally {
      setDownloadingDocument(null);
      focus.restore();
    }
  }

  if (loading && !periodState.hasRetainedData) return <PeriodWorkspaceSkeleton />;

  if (!company || !period) {
    return (
      <div className="max-w-2xl mx-auto animate-fade-in">
        <Card className="border border-red-200 dark:border-red-800 bg-white dark:bg-neutral-900">
          <Card.Content className="text-center py-8">
            <ResourceStateNotice
              state={periodState}
              label="period workspace"
              onRetry={() => loadPeriodResources(periodState.failedResourceKeys)}
            />
          </Card.Content>
        </Card>
      </div>
    );
  }

  const ixbrlUrl = getIxbrlUrl(cId, pId);
  const agmPackUrl = getAgmPackUrl(cId, pId);
  const croPackUrl = getCroFilingPackUrl(cId, pId);
  const sigPageUrl = getSignaturePageUrl(cId, pId);
  const readyToFile = filingStatus?.readyToFile ?? false;
  const periodDateRange = period ? formatPeriodRange(period.periodStart, period.periodEnd) : "Period loading";
  const classificationLabel = period?.sizeClassification?.calculatedClass ?? "Unclassified";
  const openingDebitTotal = openingBalances.reduce((sum, balance) => sum + balance.debit, 0);
  const openingCreditTotal = openingBalances.reduce((sum, balance) => sum + balance.credit, 0);
  const bankOpeningDebit = bankAccounts.filter((bank) => bank.openingBalance > 0).reduce((sum, bank) => sum + bank.openingBalance, 0);
  const bankOpeningCredit = bankAccounts.filter((bank) => bank.openingBalance < 0).reduce((sum, bank) => sum + Math.abs(bank.openingBalance), 0);
  const openingDifference = openingDebitTotal + bankOpeningDebit - openingCreditTotal - bankOpeningCredit;
  const openingBalanceCategories = categories.filter((category) => category.type !== "Income" && category.type !== "Expense");
  const visibleTransactionIds = transactions.map((tx) => tx.id);
  const allVisibleTransactionsSelected = visibleTransactionIds.length > 0
    && visibleTransactionIds.every((id) => selectedTransactionIds.includes(id));

  const tabClass = "px-4 py-2.5 text-sm font-medium text-gray-600 dark:text-gray-400 border-b-2 border-transparent data-[selected]:border-emerald-600 data-[selected]:text-emerald-700 dark:data-[selected]:text-emerald-400 cursor-pointer outline-none transition-colors";

  return (
    <div className="animate-fade-in">
      <InteractionAnnouncement announcement={announcement} />
      {/* Breadcrumbs */}
      <Breadcrumbs
        items={[
          { label: company?.legalName ?? "Company", href: `/companies/${companyId}` },
          {
            label: period
              ? formatPeriodRange(period.periodStart, period.periodEnd)
              : "Period",
          },
        ]}
      />

      <div className="mb-4">
        <ResourceStateNotice
          state={periodState}
          label="period workspace evidence"
          onRetry={() => loadPeriodResources(periodState.failedResourceKeys)}
        />
      </div>

      <WorkbenchHeader
        title={company?.legalName ?? "Company"}
        subtitle={`${periodDateRange} - annual accounts production file`}
        meta={
          <>
            <Chip color="accent" variant="soft" size="sm">{period?.status ?? "Loading"}</Chip>
            <Chip color={classificationLabel === "Micro" ? "success" : "warning"} variant="soft" size="sm">
              {classificationLabel}
            </Chip>
            {period?.isFirstYear && <Chip color="warning" variant="soft" size="sm">First year</Chip>}
            {readyToFile ? (
              <Chip color="success" variant="soft" size="sm">Ready to file</Chip>
            ) : (
              <Chip color="danger" variant="soft" size="sm">
                {filingStatus?.blockingIssues.length ?? 0} blockers
              </Chip>
            )}
          </>
        }
        actions={
          <>
            <ActionLink href={`/companies/${companyId}/periods/${periodId}/classify`}>
              <Scale className="w-4 h-4 mr-1" />Classify
            </ActionLink>
            <ActionLink href={`/companies/${companyId}/periods/${periodId}/year-end`}>
              <ClipboardList className="w-4 h-4 mr-1" />Year-end review
            </ActionLink>
            <ActionLink href={`/companies/${companyId}/periods/${periodId}/statements`} variant="primary">
              <FileText className="w-4 h-4 mr-1" />Working papers
            </ActionLink>
          </>
        }
      />

      <PeriodWorkbenchOverview
        companyId={companyId}
        periodId={periodId}
        company={company}
        period={period}
        yearEnd={yearEnd}
        readiness={readiness}
        filingStatus={filingStatus}
        filingReadinessProfile={filingReadinessProfile}
        transactionTotal={transactionTotal}
        categorisedCount={categorisedCount}
        pendingAdjustments={adjSummary?.pendingApproval ?? 0}
      />

      {error && periodState.status !== "partial-error" && (
        <div className="rounded-lg bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 px-4 py-3 text-sm text-red-700 dark:text-red-400 mb-4 animate-fade-in">
          {error}
        </div>
      )}

      {/* Tabs */}
      <p
        id="period-workspace-tab-help"
        role="note"
        aria-label="Period workspace tab navigation instructions"
        className="no-print mb-2 rounded-md border border-sky-200 bg-sky-50 px-3 py-2 text-xs font-medium text-sky-900 dark:border-sky-800 dark:bg-sky-950/40 dark:text-sky-100"
      >
        Swipe to reveal more workflow tabs. Use Left and Right Arrow keys to move between tabs.
      </p>
      <TabsRoot selectedKey={selectedWorkspaceTab} onSelectionChange={handleWorkspaceTabChange}>
        <TabList
          aria-label="Period workspace tabs"
          aria-describedby="period-workspace-tab-help"
          data-overflow-tablist="true"
          className="mb-6 flex max-w-full gap-1 overflow-x-auto overscroll-x-contain whitespace-nowrap border-b border-gray-200 no-print dark:border-neutral-700"
        >
          <Tab id="import" className={tabClass}>
            <Upload className="w-4 h-4 inline mr-1.5 -mt-0.5" />
            Import
          </Tab>
          <Tab id="categorise" className={tabClass}>
            <Settings className="w-4 h-4 inline mr-1.5 -mt-0.5" />
            Categorise
          </Tab>
          <Tab id="yearend" className={tabClass}>
            <BarChart3 className="w-4 h-4 inline mr-1.5 -mt-0.5" />
            Year-End
          </Tab>
          <Tab id="adjustments" className={tabClass}>
            <Calculator className="w-4 h-4 inline mr-1.5 -mt-0.5" />
            Adjustments
          </Tab>
          <Tab id="statements" className={tabClass}>
            <FileText className="w-4 h-4 inline mr-1.5 -mt-0.5" />
            Statements
          </Tab>
          <Tab id="filing" className={tabClass}>
            <Download className="w-4 h-4 inline mr-1.5 -mt-0.5" />
            Filing
          </Tab>
        </TabList>

        {/* Import Tab */}
        <TabPanel id="import">
          <PeriodImportWorkspace
            companyId={Number(companyId)}
            periodId={Number(periodId)}
            canWrite={canWriteWorkingPapers}
            classificationHref={`/companies/${companyId}/periods/${periodId}/classify`}
            period={period}
            bankAccounts={bankAccounts}
            showBankForm={showBankForm}
            savingBankAccount={savingBankAccount}
            bankForm={bankForm}
            selectedBankAccountId={selectedBankAccountId}
            categories={categories}
            openingBalances={openingBalances}
            openingBalanceCategories={openingBalanceCategories}
            openingBalanceForm={openingBalanceForm}
            openingDifference={openingDifference}
            savingOpeningBalance={savingOpeningBalance}
            deletingOpeningCategoryId={deletingOpeningCategoryId}
            uploading={uploading}
            uploadResult={uploadResult}
            uploadError={uploadError}
            onToggleBankForm={() => setShowBankForm((open) => !open)}
            onBankFormChange={setBankForm}
            onCreateBankAccount={handleCreateBankAccount}
            onSeedCategories={handleSeedCategories}
            onOpeningBalanceFormChange={setOpeningBalanceForm}
            onSaveOpeningBalance={handleSaveOpeningBalance}
            onDeleteOpeningBalance={handleDeleteOpeningBalance}
            onSelectBankAccount={setSelectedBankAccountId}
            onUploadFile={handleFileUpload}
            onDuplicateDecisionRecorded={async () => { await Promise.all([loadTransactions(), loadData()]); }}
          />
        </TabPanel>

        {/* Categorise Tab */}
        <TabPanel id="categorise">
          <div className="space-y-4">
            <ResourceStateNotice state={transactionState} label="transaction register" onRetry={loadTransactions} />
            {(transactionState.status === "loaded" || transactionState.status === "empty" || transactionState.hasRetainedData) && (
              <PeriodCategoriseWorkspace
            canWrite={canWriteWorkingPapers}
            transactions={transactions}
            transactionTotal={transactionTotal}
            filteredTransactionTotal={transactionFilteredTotal}
            categorisedCount={categorisedCount}
            uncategorisedCount={uncategorisedCount}
            transactionPage={transactionPage}
            transactionPageSize={transactionPageSize}
            transactionPageCount={transactionPageCount}
            transactionSortBy={transactionSortBy}
            transactionSortDirection={transactionSortDirection}
            loadingTransactions={loadingTransactions}
            categorisingId={categorisingId}
            categories={categories}
            bankAccounts={bankAccounts}
            transactionRules={transactionRules}
            selectedTransactionIds={selectedTransactionIds}
            visibleTransactionIds={visibleTransactionIds}
            allVisibleTransactionsSelected={allVisibleTransactionsSelected}
            bulkCategoryId={bulkCategoryId}
            bulkCategorising={bulkCategorising}
            ruleForm={ruleForm}
            savingRule={savingRule}
            deletingRuleId={deletingRuleId}
            txFilterStatus={txFilterStatus}
            txFilterCategory={txFilterCategory}
            txFilterBank={txFilterBank}
            txFilterSearch={txFilterSearchInput}
            selectionAnnouncement={selectionAnnouncement}
            onRefresh={loadTransactions}
            onRuleFormChange={setRuleForm}
            onCreateRule={handleCreateRule}
            onDeleteRule={handleDeleteRule}
            onBulkCategoryChange={setBulkCategoryId}
            onBulkCategorise={handleBulkCategoriseTransactions}
            onFilterStatusChange={handleTransactionStatusFilter}
            onFilterCategoryChange={handleTransactionCategoryFilter}
            onFilterBankChange={handleTransactionBankFilter}
            onSearchInputChange={handleTransactionSearchInput}
            onPageChange={handleTransactionPageChange}
            onPageSizeChange={handleTransactionPageSizeChange}
            onSortByChange={handleTransactionSortByChange}
            onSortDirectionChange={handleTransactionSortDirectionChange}
            onSelectVisibleTransactions={(selected) => {
              setSelectedTransactionIds((current) =>
                setCurrentPageTransactionSelection(current, visibleTransactionIds, selected),
              );
            }}
            onToggleTransactionSelection={(transactionId, selected) => {
              setSelectedTransactionIds((current) => toggleTransactionSelection(current, transactionId, selected));
            }}
            onCategoriseTransaction={handleCategoriseTransaction}
              />
            )}
          </div>
        </TabPanel>

        {/* Year-End Tab */}
        <TabPanel id="yearend">
          <PeriodYearEndWorkspace
            yearEnd={yearEnd}
            questionnaireHref={`/companies/${companyId}/periods/${periodId}/year-end`}
          />
        </TabPanel>

        {/* Adjustments Tab */}
        <TabPanel id="adjustments">
          <div className="space-y-4">
            <ResourceStateNotice state={adjustmentState} label="adjustment evidence" onRetry={loadAdjustments} />
            {(adjustmentState.status === "loaded" || adjustmentState.status === "empty" || adjustmentState.hasRetainedData) && (
              <PeriodAdjustmentsWorkspace
            canWrite={canWriteWorkingPapers}
            canApprove={canApprove}
            adjustments={adjustments}
            adjSummary={adjSummary}
            loadingAdjustments={loadingAdjustments}
            generatingAdj={generatingAdj}
            approvingId={approvingId}
            adjFilterApproved={adjFilterApproved}
            adjFilterType={adjFilterType}
            onGenerateAdjustments={handleGenerateAdjustments}
            onRefreshAdjustments={loadAdjustments}
            onApproveAdjustment={handleApproveAdjustment}
            onFilterApprovedChange={handleAdjustmentApprovalFilter}
            onFilterTypeChange={handleAdjustmentSourceFilter}
              />
            )}
          </div>
        </TabPanel>

        {/* Statements Tab */}
        <TabPanel id="statements">
          <PeriodStatementsWorkspace
            readiness={readiness}
            statementsHref={`/companies/${companyId}/periods/${periodId}/statements`}
            notesHref={`/companies/${companyId}/periods/${periodId}/notes`}
            charityHref={`/companies/${companyId}/periods/${periodId}/charity`}
            isCharity={Boolean(company?.isCharitableOrganisation)}
          />
        </TabPanel>

        {/* Filing Tab */}
        <TabPanel id="filing">
          <PeriodFilingWorkspace
            companyId={cId}
            periodId={pId}
            filingStatus={filingStatus}
            filingReadinessProfile={filingReadinessProfile}
            croSubmissionReference={croSubmissionReference}
            validatingIxbrl={validatingIxbrl}
            canApprove={canApprove}
            canRead={canRead}
            canReview={canReview}
            canWriteWorkingPapers={canWriteWorkingPapers}
            canReadExternalHandoff={canReadInternalWorkingPapers}
            deadlines={deadlinesList}
            filingReferences={filingReferences}
            markingFiledId={markingFiledId}
            jeopardy={jeopardy}
            section307Note={section307Note}
            filingRegimeReady={Boolean(period?.filingRegime)}
            downloadingDocument={downloadingDocument}
            checklist={{
              transactionsCategorised: transactionTotal > 0 && uncategorisedCount === 0,
              adjustmentsReviewed: adjSummary != null && adjSummary.pendingApproval === 0 && (adjSummary.autoGenerated + adjSummary.manual) > 0,
              balanceSheetBalances: readiness?.balanceSheetBalances ?? false,
              filingReadinessComplete: (readiness?.filingReadinessPercent ?? 0) >= 100,
              accountsPdfGenerated: filingStatus?.cro.accountsPdfReady ?? false,
              croPackAndSignatureGenerated: (filingStatus?.cro.accountsPdfReady ?? false) && (filingStatus?.cro.signaturePageReady ?? false),
            }}
            auditLog={auditLog}
            auditTotal={auditTotal}
            auditPage={auditLoadedPage}
            auditPageSize={auditLoadedPageSize}
            auditPageCount={auditPageCount}
            loadingAuditLog={loadingAuditLog}
            auditLogError={auditState.error}
            filingResourceState={filingState}
            auditResourceState={auditState}
            onAuditPageChange={setAuditPage}
            onAuditPageSizeChange={(pageSize) => {
              setAuditPage(1);
              setAuditPageSize(pageSize);
            }}
            onRetryAuditLog={loadAuditLog}
            onRetryFiling={() => refreshFilingState(filingState.failedResourceKeys)}
            onExternalHandoffChanged={async () => {
              await Promise.all([refreshFilingState(), loadAuditLog()]);
            }}
            notesHref={`/companies/${companyId}/periods/${periodId}/notes`}
            onCroSubmissionReferenceChange={setCroSubmissionReference}
            onRunIxbrlChecks={async () => {
              const focus = captureInteractionFocus("period-filing-workspace");
              setValidatingIxbrl(true);
              try {
                const result = await validateIxbrl(cId, pId);
                if (result.manualHandoffRequired && result.reviewPrototypeChecksPassed) {
                  toast.warning("Draft iXBRL structure checked; filing-ready generation remains disabled and requires manual handoff");
                } else if (result.ixbrlInternalChecksPassed) {
                  toast.success("Internal iXBRL checks passed; external ROS validation is still required");
                } else {
                  toast.error(result.validationErrors || "Internal iXBRL checks need attention");
                }
                await refreshFilingState();
              } catch (err) {
                toast.error(err instanceof Error ? err.message : "Internal iXBRL checks need attention");
              } finally {
                setValidatingIxbrl(false);
                focus.restore();
              }
            }}
            onApproveForFiling={async () => {
              const focus = captureInteractionFocus("period-filing-workspace");
              try {
                await updateCroFilingStatus(cId, pId, { status: "Approved" });
                toast.success("Filing approved");
                await refreshFilingState();
              } catch (err) {
                toast.error(err instanceof Error ? err.message : "Failed to approve");
              } finally {
                focus.restore();
              }
            }}
            onMarkCroSubmitted={async (submissionReference) => {
              const focus = captureInteractionFocus("period-filing-workspace");
              try {
                if (!submissionReference) {
                  toast.error("CORE submission reference is required");
                  return;
                }
                await updateCroFilingStatus(cId, pId, { status: "Submitted", submissionReference });
                toast.success("External CRO submission recorded");
                await refreshFilingState();
              } catch (err) {
                toast.error(err instanceof Error ? err.message : "Failed to update status");
              } finally {
                focus.restore();
              }
            }}
            onConfirmCroPayment={async () => {
              const focus = captureInteractionFocus("period-filing-workspace");
              try {
                await confirmCroPayment(cId, pId);
                toast.success("CORE payment confirmed");
                await refreshFilingState();
              } catch (err) {
                toast.error(err instanceof Error ? err.message : "Failed to confirm payment");
              } finally {
                focus.restore();
              }
            }}
            onMarkCroAccepted={async () => {
              const focus = captureInteractionFocus("period-filing-workspace");
              try {
                await updateCroFilingStatus(cId, pId, { status: "Accepted" });
                toast.success("CRO acceptance recorded");
                await refreshFilingState();
              } catch (err) {
                toast.error(err instanceof Error ? err.message : "Failed to mark accepted");
              } finally {
                focus.restore();
              }
            }}
            onRecordCroSendBack={async () => {
              const focus = captureInteractionFocus("period-filing-workspace");
              try {
                await updateCroFilingStatus(cId, pId, {
                  status: "CorrectionRequired",
                  reason: "CRO send-back/correction required. Correct and redeliver within 14 days.",
                });
                toast.warning("Correction deadline opened");
                await refreshFilingState();
              } catch (err) {
                toast.error(err instanceof Error ? err.message : "Failed to record correction");
              } finally {
                focus.restore();
              }
            }}
            onFilingReferenceChange={(deadlineId, value) => {
              setFilingReferences((current) => ({
                ...current,
                [deadlineId]: value,
              }));
            }}
            onMarkFiled={handleMarkDeadlineFiled}
            onReferenceMissing={toast.error}
            onDownloadAgmPack={() => downloadDocument(agmPackUrl, "AGM pack")}
            onDownloadCroFilingPack={() => downloadDocument(croPackUrl, "CRO filing pack", "accounts", true)}
            onDownloadSignaturePage={() => downloadDocument(sigPageUrl, "signature page", "signature", true)}
            onDownloadIxbrl={() => downloadDocument(ixbrlUrl, "draft iXBRL review prototype", undefined, false, "xhtml")}
          />
        </TabPanel>
      </TabsRoot>
    </div>
  );
}
