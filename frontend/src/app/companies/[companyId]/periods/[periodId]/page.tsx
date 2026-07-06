"use client";

import { type Key, use, useState, useEffect, useCallback, useRef } from "react";
import {
  Button,
  Card,
  Chip,
  Spinner,
  TabsRoot,
  TabList,
  Tab,
  TabPanel,
  ProgressBar,
  ProgressBarTrack,
  ProgressBarFill,
} from "@heroui/react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import {
  Upload,
  Settings,
  Calculator,
  FileText,
  Download,
  RefreshCw,
  CheckCircle2,
  AlertTriangle,
  BarChart3,
  ArrowRight,
  Scale,
  ClipboardList,
  Eye,
  Heart,
} from "lucide-react";
import { toast } from "sonner";
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
import { WorkbenchHeader } from "@/components/workbench";
import { PeriodWorkbenchOverview } from "@/components/period/PeriodWorkbenchOverview";
import { PeriodCategoriseWorkspace } from "@/components/period/PeriodCategoriseWorkspace";
import { PeriodFilingWorkspace } from "@/components/period/PeriodFilingWorkspace";
import { PeriodImportWorkspace } from "@/components/period/PeriodImportWorkspace";
import { StatementsReadinessPanel } from "@/components/period/StatementsReadinessPanel";
import { useAuth } from "@/components/AuthProvider";
import { formatPeriodRange } from "@/lib/format";

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat("en-IE", {
    style: "currency",
    currency: "EUR",
  }).format(amount);
}

type WorkspaceTabId = "import" | "categorise" | "yearend" | "adjustments" | "statements" | "filing";

const WORKSPACE_TAB_IDS = new Set<WorkspaceTabId>([
  "import",
  "categorise",
  "yearend",
  "adjustments",
  "statements",
  "filing",
]);

function normaliseWorkspaceTab(value: string | null): WorkspaceTabId {
  if (value === "year-end") return "yearend";
  if (value === "review") return "filing";
  if (value && WORKSPACE_TAB_IDS.has(value as WorkspaceTabId)) return value as WorkspaceTabId;

  return "import";
}

export default function PeriodWorkspacePage({
  params,
}: {
  params: Promise<{ companyId: string; periodId: string }>;
}) {
  const { companyId, periodId } = use(params);
  const cId = Number(companyId);
  const pId = Number(periodId);
  const searchParams = useSearchParams();
  const { canReview } = useAuth();
  const [selectedWorkspaceTab, setSelectedWorkspaceTab] = useState<WorkspaceTabId>(() =>
    normaliseWorkspaceTab(searchParams.get("tab")),
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
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [generatingAdj, setGeneratingAdj] = useState(false);
  const [validatingIxbrl, setValidatingIxbrl] = useState(false);
  const [downloadingDocument, setDownloadingDocument] = useState<string | null>(null);

  useEffect(() => {
    setSelectedWorkspaceTab(normaliseWorkspaceTab(searchParams.get("tab")));
  }, [searchParams]);

  const handleWorkspaceTabChange = useCallback((key: Key) => {
    setSelectedWorkspaceTab(normaliseWorkspaceTab(String(key)));
  }, []);

  // Import tab state
  const [selectedBankAccountId, setSelectedBankAccountId] = useState<number | "">("");
  const [uploading, setUploading] = useState(false);
  const [uploadResult, setUploadResult] = useState<{
    rowsImported: number;
    duplicatesSkipped: number;
    autoCategorised: number;
  } | null>(null);
  const [uploadError, setUploadError] = useState<string | null>(null);

  // Categorise tab state
  const [transactions, setTransactions] = useState<ImportedTransaction[]>([]);
  const [transactionTotal, setTransactionTotal] = useState(0);
  const [loadingTransactions, setLoadingTransactions] = useState(false);
  const [categorisingId, setCategorisingId] = useState<number | null>(null);
  const [txFilterCategory, setTxFilterCategory] = useState<string>("");
  const [txFilterBank, setTxFilterBank] = useState<string>("");
  const [txFilterStatus, setTxFilterStatus] = useState<string>("");
  const [txFilterSearch, setTxFilterSearch] = useState("");
  const txSearchTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const [selectedTransactionIds, setSelectedTransactionIds] = useState<number[]>([]);
  const [bulkCategoryId, setBulkCategoryId] = useState("");
  const [bulkCategorising, setBulkCategorising] = useState(false);
  const [ruleForm, setRuleForm] = useState({ pattern: "", categoryId: "", priority: "100" });
  const [savingRule, setSavingRule] = useState(false);
  const [deletingRuleId, setDeletingRuleId] = useState<number | null>(null);

  // Adjustments tab state
  const [adjustments, setAdjustments] = useState<Adjustment[]>([]);
  const [adjSummary, setAdjSummary] = useState<AdjustmentSummary | null>(null);
  const [loadingAdjustments, setLoadingAdjustments] = useState(false);
  const [approvingId, setApprovingId] = useState<number | null>(null);
  const [adjFilterApproved, setAdjFilterApproved] = useState<string>("");
  const [adjFilterType, setAdjFilterType] = useState<string>("");

  const refreshFilingState = useCallback(async () => {
    const [statusResult, profileResult] = await Promise.allSettled([
      getFilingWorkflowStatus(cId, pId),
      getFilingReadinessProfile(cId, pId),
    ]);

    if (statusResult.status === "fulfilled") {
      setFilingStatus(statusResult.value);
      setCroSubmissionReference(statusResult.value.cro.submissionReference ?? "");
    }
    if (profileResult.status === "fulfilled") {
      setFilingReadinessProfile(profileResult.value);
    }
  }, [cId, pId]);

  const loadData = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [companyData, periodData, bankData] = await Promise.all([
        getCompany(cId),
        getPeriod(cId, pId),
        getBankAccounts(cId),
      ]);
      setCompany(companyData);
      setPeriod(periodData);
      setBankAccounts(bankData);

      if (bankData.length > 0 && selectedBankAccountId === "") {
        setSelectedBankAccountId(bankData[0].id);
      }

      try {
        const yeSummary = await getYearEndSummary(cId, pId);
        setYearEnd(yeSummary);
      } catch {
        // Year-end data may not be available yet
      }
      try {
        const readinessData = await getReadiness(cId, pId);
        setReadiness(readinessData);
      } catch {
        // Readiness data may not be available yet
      }
      try {
        await refreshFilingState();
      } catch {
        // Filing status may not be available yet
      }
      try {
        await calculateDeadlines(cId, pId);
      } catch {
        // Deadline calculation may not be available yet
      }
      try {
        const dl = await getDeadlines(cId);
        setDeadlinesList(dl.filter((d: FilingDeadline) => d.periodId === pId));
      } catch {}
      try {
        const j = await getAuditExemptionJeopardy(cId);
        setJeopardy(j);
      } catch {}
      try {
        const n = await getSection307Note(cId, pId);
        setSection307Note(n.note);
      } catch {}
      try {
        const cats = await getCategories(cId);
        setCategories(cats);
      } catch {}
      try {
        const opening = await getOpeningBalances(cId, pId);
        setOpeningBalances(opening);
      } catch {}
      try {
        const rules = await getTransactionRules(cId);
        setTransactionRules(rules);
      } catch {}
      try {
        const audit = await getAuditLog(cId, pId, 1, 50);
        setAuditLog(audit.items);
        setAuditTotal(audit.total);
      } catch {}
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load data");
    } finally {
      setLoading(false);
    }
  }, [cId, pId, refreshFilingState, selectedBankAccountId]);

  const loadTransactions = useCallback(async () => {
    setLoadingTransactions(true);
    try {
      const filters: { uncategorised?: boolean; categoryId?: number; bankAccountId?: number; search?: string } = {};
      if (txFilterStatus === "uncategorised") filters.uncategorised = true;
      if (txFilterCategory) filters.categoryId = Number(txFilterCategory);
      if (txFilterBank) filters.bankAccountId = Number(txFilterBank);
      if (txFilterSearch.trim()) filters.search = txFilterSearch.trim();
      const data = await getTransactions(cId, pId, 1, 50, filters);
      setTransactions(data.items);
      setTransactionTotal(data.total);
      setSelectedTransactionIds((current) => current.filter((id) => data.items.some((tx) => tx.id === id)));
    } catch {
      setTransactions([]);
      setTransactionTotal(0);
    } finally {
      setLoadingTransactions(false);
    }
  }, [cId, pId, txFilterCategory, txFilterBank, txFilterStatus, txFilterSearch]);

  const loadAdjustments = useCallback(async () => {
    setLoadingAdjustments(true);
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
      setAdjustments(adjData);
      setAdjSummary(summaryData);
    } catch {
      setAdjustments([]);
      setAdjSummary(null);
    } finally {
      setLoadingAdjustments(false);
    }
  }, [cId, pId, adjFilterApproved, adjFilterType]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  useEffect(() => {
    if (!loading) {
      loadTransactions();
      loadAdjustments();
    }
  }, [loading, loadTransactions, loadAdjustments]);

  async function handleFileUpload(file: File) {
    if (!selectedBankAccountId) {
      toast.error("Please select a bank account before uploading.");
      return;
    }
    if (!file.name.endsWith(".csv")) {
      toast.error("Only CSV files are supported.");
      return;
    }
    setUploading(true);
    setUploadError(null);
    setUploadResult(null);
    try {
      const result = await uploadBankCsv(cId, Number(selectedBankAccountId), pId, file);
      setUploadResult({
        rowsImported: result.rowsImported ?? result.imported ?? 0,
        duplicatesSkipped: result.duplicatesSkipped ?? result.duplicates ?? 0,
        autoCategorised: result.autoCategorised ?? result.categorised ?? 0,
      });
      toast.success(`Imported ${result.rowsImported ?? result.imported ?? 0} transactions`);
      await loadTransactions();
      await loadData();
    } catch (err) {
      const msg = err instanceof Error ? err.message : "Upload failed";
      setUploadError(msg);
      toast.error(msg);
    } finally {
      setUploading(false);
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
    } finally {
      setDeletingOpeningCategoryId(null);
    }
  }

  async function handleSeedCategories() {
    try {
      const cats = await seedCategories(cId);
      setCategories(cats);
      toast.success(`${cats.length} categories seeded`);
    } catch {
      toast.error("Failed to seed categories");
    }
  }

  async function handleCategoriseTransaction(transactionId: number, categoryId: number) {
    setCategorisingId(transactionId);
    try {
      const updated = await categoriseTransaction(cId, pId, transactionId, categoryId);
      setTransactions((current) =>
        current.map((tx) => (tx.id === transactionId ? { ...tx, ...updated } : tx))
      );
      toast.success("Transaction categorised");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to categorise transaction");
    } finally {
      setCategorisingId(null);
    }
  }

  async function handleBulkCategoriseTransactions() {
    if (selectedTransactionIds.length === 0 || !bulkCategoryId) {
      toast.error("Select transactions and a category first");
      return;
    }

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
    }
  }

  async function handleCreateRule() {
    if (!ruleForm.pattern.trim() || !ruleForm.categoryId) {
      toast.error("Rule pattern and category are required");
      return;
    }

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
    } finally {
      setDeletingRuleId(null);
    }
  }

  function handleTransactionSearchInput(value: string) {
    if (txSearchTimerRef.current) clearTimeout(txSearchTimerRef.current);
    txSearchTimerRef.current = setTimeout(() => setTxFilterSearch(value), 400);
  }

  async function handleMarkDeadlineFiled(deadline: FilingDeadline, filingReference?: string) {
    setMarkingFiledId(deadline.id);
    try {
      await markFiled(cId, pId, {
        deadlineType: deadline.deadlineType,
        filedDate: new Date().toISOString().split("T")[0],
        ...(filingReference ? { filingReference } : {}),
      });
      toast.success(`${deadline.deadlineType} filing marked as complete`);
      await loadData();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to mark as filed");
    } finally {
      setMarkingFiledId(null);
    }
  }

  async function handleGenerateAdjustments() {
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
    }
  }

  async function handleApproveAdjustment(id: number) {
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
    }
  }

  if (loading) return <PeriodWorkspaceSkeleton />;

  if (error && !company) {
    return (
      <div className="max-w-2xl mx-auto animate-fade-in">
        <Card className="border border-red-200 dark:border-red-800 bg-white dark:bg-neutral-900">
          <Card.Content className="text-center py-8">
            <AlertTriangle className="w-10 h-10 text-red-500 mx-auto mb-3" />
            <p className="text-red-700 dark:text-red-400 font-medium">{error}</p>
            <Button variant="outline" className="mt-4" onPress={loadData}>
              <RefreshCw className="w-4 h-4 mr-1" />
              Retry
            </Button>
          </Card.Content>
        </Card>
      </div>
    );
  }

  const ixbrlUrl = getIxbrlUrl(cId, pId);
  const agmPackUrl = getAgmPackUrl(cId, pId);
  const croPackUrl = getCroFilingPackUrl(cId, pId);
  const sigPageUrl = getSignaturePageUrl(cId, pId);
  const categorisedCount = transactions.filter((t) => t.categoryId != null).length;
  const uncategorisedCount = transactionTotal - categorisedCount;
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
            <Link href={`/companies/${companyId}/periods/${periodId}/classify`}>
              <Button variant="outline" size="sm"><Scale className="w-4 h-4 mr-1" />Classify</Button>
            </Link>
            <Link href={`/companies/${companyId}/periods/${periodId}/year-end`}>
              <Button variant="outline" size="sm"><ClipboardList className="w-4 h-4 mr-1" />Year-end review</Button>
            </Link>
            <Link href={`/companies/${companyId}/periods/${periodId}/statements`}>
              <Button variant="primary" size="sm"><FileText className="w-4 h-4 mr-1" />Working papers</Button>
            </Link>
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

      {error && (
        <div className="rounded-lg bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 px-4 py-3 text-sm text-red-700 dark:text-red-400 mb-4 animate-fade-in">
          {error}
        </div>
      )}

      {/* Tabs */}
      <TabsRoot selectedKey={selectedWorkspaceTab} onSelectionChange={handleWorkspaceTabChange}>
        <TabList aria-label="Period workspace tabs" className="flex gap-1 border-b border-gray-200 dark:border-neutral-700 mb-6 overflow-x-auto no-print">
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
          />
        </TabPanel>

        {/* Categorise Tab */}
        <TabPanel id="categorise">
          <PeriodCategoriseWorkspace
            transactions={transactions}
            transactionTotal={transactionTotal}
            categorisedCount={categorisedCount}
            uncategorisedCount={uncategorisedCount}
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
            txFilterSearch={txFilterSearch}
            onRefresh={loadTransactions}
            onRuleFormChange={setRuleForm}
            onCreateRule={handleCreateRule}
            onDeleteRule={handleDeleteRule}
            onBulkCategoryChange={setBulkCategoryId}
            onBulkCategorise={handleBulkCategoriseTransactions}
            onFilterStatusChange={setTxFilterStatus}
            onFilterCategoryChange={setTxFilterCategory}
            onFilterBankChange={setTxFilterBank}
            onSearchInputChange={handleTransactionSearchInput}
            onSelectVisibleTransactions={(selected) => {
              setSelectedTransactionIds(selected ? visibleTransactionIds : []);
            }}
            onToggleTransactionSelection={(transactionId, selected) => {
              setSelectedTransactionIds((current) =>
                selected
                  ? [...current, transactionId]
                  : current.filter((id) => id !== transactionId),
              );
            }}
            onCategoriseTransaction={handleCategoriseTransaction}
          />
        </TabPanel>

        {/* Year-End Tab */}
        <TabPanel id="yearend">
          <div className="space-y-6">
            <Card className="shadow-sm border border-emerald-200 dark:border-emerald-800 bg-emerald-50/30 dark:bg-emerald-900/10">
              <Card.Content className="p-5">
                <div className="flex items-center justify-between">
                  <div>
                    <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-100">Year-End Questionnaire</h3>
                    <p className="text-xs text-gray-500 dark:text-gray-400 mt-0.5">
                      Walk through all 9 sections to capture debtors, creditors, assets, payroll, tax, and more.
                    </p>
                  </div>
                  <Link href={`/companies/${companyId}/periods/${periodId}/year-end`}>
                    <Button variant="primary">
                      Open Year-End Questionnaire
                      <ArrowRight className="w-4 h-4 ml-1.5" />
                    </Button>
                  </Link>
                </div>
              </Card.Content>
            </Card>

            {yearEnd && (
              <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
                <Card.Header>
                  <Card.Title className="text-gray-900 dark:text-gray-100">Year-End Completeness</Card.Title>
                  <Card.Description>
                    {yearEnd.completeness.completed} of {yearEnd.completeness.total} items completed
                  </Card.Description>
                </Card.Header>
                <Card.Content>
                  <div className="mb-2 flex items-center justify-between text-sm">
                    <span className="text-gray-600 dark:text-gray-400">Progress</span>
                    <span className="font-medium text-gray-900 dark:text-gray-100">{yearEnd.completeness.score}%</span>
                  </div>
                  <ProgressBar
                    value={yearEnd.completeness.score}
                    minValue={0}
                    maxValue={100}
                    aria-label="Year-end completeness"
                    color={yearEnd.completeness.score >= 80 ? "success" : yearEnd.completeness.score >= 50 ? "warning" : "danger"}
                  >
                    <ProgressBarTrack>
                      <ProgressBarFill />
                    </ProgressBarTrack>
                  </ProgressBar>
                  {yearEnd.completeness.incomplete.length > 0 && (
                    <div className="mt-4">
                      <p className="text-xs font-medium text-gray-500 dark:text-gray-400 mb-2">Incomplete items:</p>
                      <div className="flex flex-wrap gap-1.5">
                        {yearEnd.completeness.incomplete.map((item) => (
                          <Chip key={item} color="warning" variant="soft" size="sm">{item}</Chip>
                        ))}
                      </div>
                    </div>
                  )}
                </Card.Content>
              </Card>
            )}

            <div className="grid grid-cols-2 gap-4">
              <SummaryCard title="Debtors" value={yearEnd ? formatCurrency(yearEnd.debtors.total) : "--"} subtitle={yearEnd ? `${yearEnd.debtors.count} entries` : "No data"} />
              <SummaryCard title="Creditors" value={yearEnd ? formatCurrency(yearEnd.creditors.total) : "--"} subtitle={yearEnd ? `${yearEnd.creditors.count} entries` : "No data"} />
              <SummaryCard title="Fixed Assets" value={yearEnd ? formatCurrency(yearEnd.fixedAssets.totalCost) : "--"} subtitle={yearEnd ? `${yearEnd.fixedAssets.count} assets` : "No data"} />
              <SummaryCard title="Inventory" value={yearEnd ? formatCurrency(yearEnd.inventory.totalValue) : "--"} subtitle={yearEnd ? `${yearEnd.inventory.count} items` : "No data"} />
              <SummaryCard title="Loans" value={yearEnd ? formatCurrency(yearEnd.loans.totalBalance) : "--"} subtitle={yearEnd ? `${yearEnd.loans.count} loans` : "No data"} />
              <SummaryCard title="Payroll" value={yearEnd?.payroll ? formatCurrency(yearEnd.payroll.grossWages) : "--"} subtitle={yearEnd?.payroll ? `${yearEnd.payroll.staffCount} staff` : "Not applicable"} />
              <SummaryCard title="Tax Liabilities" value={yearEnd ? formatCurrency(yearEnd.taxes.totalLiability) : "--"} subtitle={yearEnd ? `${yearEnd.taxes.count} items` : "No data"} />
              <SummaryCard title="Dividends" value={yearEnd ? formatCurrency(yearEnd.dividends.total) : "--"} subtitle={yearEnd ? `${yearEnd.dividends.count} distributions` : "No data"} />
            </div>

            {!yearEnd && (
              <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
                <Card.Content className="text-center py-8">
                  <BarChart3 className="w-10 h-10 text-gray-300 dark:text-gray-600 mx-auto mb-3" />
                  <p className="text-sm text-gray-500 dark:text-gray-400">
                    Year-end summary data is not yet available for this period.
                  </p>
                  <p className="text-xs text-gray-400 dark:text-gray-500 mt-1">
                    Complete the import and categorisation steps first.
                  </p>
                </Card.Content>
              </Card>
            )}
          </div>
        </TabPanel>

        {/* Adjustments Tab */}
        <TabPanel id="adjustments">
          <div className="space-y-6">
            <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
              <Card.Header>
                <Card.Title className="text-gray-900 dark:text-gray-100">Period Adjustments</Card.Title>
                <Card.Description>
                  Generate and review adjustments for depreciation, prepayments, accruals, and other year-end entries
                </Card.Description>
              </Card.Header>
              <Card.Content>
                <div className="flex items-center gap-4">
                  <Button variant="primary" onPress={handleGenerateAdjustments} isDisabled={generatingAdj}>
                    {generatingAdj ? (
                      <><Spinner size="sm" className="mr-2" />Generating...</>
                    ) : (
                      <><Calculator className="w-4 h-4 mr-1" />Generate Adjustments</>
                    )}
                  </Button>
                  <Button variant="ghost" size="sm" onPress={loadAdjustments} isDisabled={loadingAdjustments}>
                    <RefreshCw className={`w-4 h-4 mr-1 ${loadingAdjustments ? "animate-spin" : ""}`} />
                    Refresh
                  </Button>
                </div>
              </Card.Content>
            </Card>

            {/* Adjustment Summary */}
            <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
              <Card.Header>
                <Card.Title className="text-gray-900 dark:text-gray-100">Adjustment Summary</Card.Title>
              </Card.Header>
              <Card.Content>
                {adjSummary ? (
                  <div className="space-y-4">
                    <div className="grid grid-cols-4 gap-4">
                      <div className="rounded-lg bg-blue-50 dark:bg-blue-900/20 p-4 text-center">
                        <p className="text-2xl font-bold text-blue-700 dark:text-blue-400">{adjSummary.autoGenerated}</p>
                        <p className="text-xs text-gray-500 dark:text-gray-400 mt-1">Auto-Generated</p>
                      </div>
                      <div className="rounded-lg bg-purple-50 dark:bg-purple-900/20 p-4 text-center">
                        <p className="text-2xl font-bold text-purple-700 dark:text-purple-400">{adjSummary.manual}</p>
                        <p className="text-xs text-gray-500 dark:text-gray-400 mt-1">Manual</p>
                      </div>
                      <div className="rounded-lg bg-amber-50 dark:bg-amber-900/20 p-4 text-center">
                        <p className="text-2xl font-bold text-amber-700 dark:text-amber-400">{adjSummary.pendingApproval}</p>
                        <p className="text-xs text-gray-500 dark:text-gray-400 mt-1">Pending Approval</p>
                      </div>
                      <div className="rounded-lg bg-emerald-50 dark:bg-emerald-900/20 p-4 text-center">
                        <p className="text-2xl font-bold text-emerald-700 dark:text-emerald-400">{adjSummary.approved}</p>
                        <p className="text-xs text-gray-500 dark:text-gray-400 mt-1">Approved</p>
                      </div>
                    </div>
                    <div className="grid grid-cols-2 gap-4">
                      <div className="rounded-lg bg-gray-50 dark:bg-neutral-800 p-4">
                        <p className="text-xs text-gray-500 dark:text-gray-400">Total Impact on Profit</p>
                        <p className={`text-lg font-bold mt-1 ${adjSummary.totalImpactOnProfit >= 0 ? "text-emerald-700 dark:text-emerald-400" : "text-red-700 dark:text-red-400"}`}>
                          {formatCurrency(adjSummary.totalImpactOnProfit)}
                        </p>
                      </div>
                      <div className="rounded-lg bg-gray-50 dark:bg-neutral-800 p-4">
                        <p className="text-xs text-gray-500 dark:text-gray-400">Total Impact on Assets</p>
                        <p className={`text-lg font-bold mt-1 ${adjSummary.totalImpactOnAssets >= 0 ? "text-emerald-700 dark:text-emerald-400" : "text-red-700 dark:text-red-400"}`}>
                          {formatCurrency(adjSummary.totalImpactOnAssets)}
                        </p>
                      </div>
                    </div>
                  </div>
                ) : (
                  <div className="grid grid-cols-4 gap-4">
                    {["Auto-Generated", "Manual", "Pending Approval", "Approved"].map((label) => (
                      <div key={label} className="rounded-lg bg-gray-50 dark:bg-neutral-800 p-4 text-center">
                        <p className="text-2xl font-bold text-gray-900 dark:text-gray-100">0</p>
                        <p className="text-xs text-gray-500 dark:text-gray-400 mt-1">{label}</p>
                      </div>
                    ))}
                  </div>
                )}
              </Card.Content>
            </Card>

            {/* Adjustments List */}
            <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
              <Card.Header>
                <Card.Title className="text-gray-900 dark:text-gray-100">Adjustment Details</Card.Title>
                <Card.Description>
                  {adjustments.length} adjustment{adjustments.length !== 1 ? "s" : ""} found
                </Card.Description>
              </Card.Header>
              <Card.Content>
                <div className="mb-4 grid gap-3 md:grid-cols-3">
                  <div>
                    <label className="mb-1 block text-xs font-medium text-gray-500 dark:text-gray-400">Approval status</label>
                    <select
                      value={adjFilterApproved}
                      onChange={(e) => setAdjFilterApproved(e.target.value)}
                      className="w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 dark:border-neutral-600 dark:bg-neutral-900 dark:text-gray-100"
                    >
                      <option value="">All adjustments</option>
                      <option value="pending">Pending approval</option>
                      <option value="approved">Approved</option>
                    </select>
                  </div>
                  <div>
                    <label className="mb-1 block text-xs font-medium text-gray-500 dark:text-gray-400">Source</label>
                    <select
                      value={adjFilterType}
                      onChange={(e) => setAdjFilterType(e.target.value)}
                      className="w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 dark:border-neutral-600 dark:bg-neutral-900 dark:text-gray-100"
                    >
                      <option value="">Auto and manual</option>
                      <option value="auto">Auto-generated</option>
                      <option value="manual">Manual only</option>
                    </select>
                  </div>
                  <div className="flex items-end">
                    <Button variant="outline" size="sm" onPress={loadAdjustments} isDisabled={loadingAdjustments}>
                      <RefreshCw className={`w-4 h-4 mr-1 ${loadingAdjustments ? "animate-spin" : ""}`} />
                      Apply Filters
                    </Button>
                  </div>
                </div>
                {loadingAdjustments ? (
                  <div className="flex items-center justify-center py-8"><Spinner size="sm" /></div>
                ) : adjustments.length === 0 ? (
                  <div className="text-center py-8">
                    <Calculator className="w-10 h-10 text-gray-300 dark:text-gray-600 mx-auto mb-3" />
                    <p className="text-sm text-gray-500 dark:text-gray-400">
                      No adjustments yet. Click &ldquo;Generate Adjustments&rdquo; to create them.
                    </p>
                  </div>
                ) : (
                  <div className="space-y-3">
                    {adjustments.map((adj) => (
                      <Card key={adj.id} className="border border-gray-100 dark:border-neutral-700 bg-white dark:bg-neutral-900">
                        <Card.Content className="p-4">
                          <div className="flex items-start justify-between gap-4">
                            <div className="flex-1 min-w-0">
                              <div className="flex items-center gap-2 mb-1">
                                <p className="text-sm font-medium text-gray-900 dark:text-gray-100 truncate">
                                  {adj.description}
                                </p>
                                <Chip color={adj.isAuto ? "default" : "accent"} variant="soft" size="sm">
                                  {adj.isAuto ? "Auto" : "Manual"}
                                </Chip>
                                <Chip color={adj.approvedBy ? "success" : "warning"} variant="soft" size="sm">
                                  {adj.approvedBy ? "Approved" : "Pending"}
                                </Chip>
                              </div>
                              {adj.reason && (
                                <p className="text-xs text-gray-500 dark:text-gray-400 mb-1">{adj.reason}</p>
                              )}
                              <div className="flex items-center gap-4 text-xs text-gray-500 dark:text-gray-400">
                                <span>Amount: <span className="font-medium text-gray-700 dark:text-gray-300">{formatCurrency(adj.amount)}</span></span>
                                <span>Profit: <span className={`font-medium ${adj.impactOnProfit >= 0 ? "text-emerald-700 dark:text-emerald-400" : "text-red-700 dark:text-red-400"}`}>{formatCurrency(adj.impactOnProfit)}</span></span>
                                <span>Assets: <span className={`font-medium ${adj.impactOnAssets >= 0 ? "text-emerald-700 dark:text-emerald-400" : "text-red-700 dark:text-red-400"}`}>{formatCurrency(adj.impactOnAssets)}</span></span>
                              </div>
                              {adj.approvedBy && (
                                <p className="text-xs text-gray-400 dark:text-gray-500 mt-1">
                                  Approved by {adj.approvedBy}
                                  {adj.approvedAt && ` on ${new Date(adj.approvedAt).toLocaleDateString("en-IE")}`}
                                </p>
                              )}
                            </div>
                            <div className="shrink-0">
                              {!adj.approvedBy && (
                                <Button variant="outline" size="sm" onPress={() => handleApproveAdjustment(adj.id)} isDisabled={approvingId === adj.id}>
                                  {approvingId === adj.id ? <Spinner size="sm" /> : <><CheckCircle2 className="w-4 h-4 mr-1" />Approve</>}
                                </Button>
                              )}
                            </div>
                          </div>
                        </Card.Content>
                      </Card>
                    ))}
                  </div>
                )}
              </Card.Content>
            </Card>
          </div>
        </TabPanel>

        {/* Statements Tab */}
        <TabPanel id="statements">
          <div className="space-y-6">
            <StatementsReadinessPanel readiness={readiness} />

            <Card className="shadow-sm border border-blue-200 dark:border-blue-800 bg-blue-50/30 dark:bg-blue-900/10">
              <Card.Content className="p-4">
                <div className="flex items-center justify-between">
                  <div className="flex items-center gap-3">
                    <Eye className="w-5 h-5 text-blue-600 dark:text-blue-400" />
                    <div>
                      <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-100">View Financial Statements</h3>
                      <p className="text-xs text-gray-500 dark:text-gray-400 mt-0.5">Preview trial balance, P&amp;L, balance sheet, and tax computation.</p>
                    </div>
                  </div>
                  <Link href={`/companies/${companyId}/periods/${periodId}/statements`}>
                    <Button variant="outline" size="sm">View Statements<ArrowRight className="w-4 h-4 ml-1.5" /></Button>
                  </Link>
                </div>
              </Card.Content>
            </Card>

            <Card className="shadow-sm border border-purple-200 dark:border-purple-800 bg-purple-50/30 dark:bg-purple-900/10">
              <Card.Content className="p-4">
                <div className="flex items-center justify-between">
                  <div className="flex items-center gap-3">
                    <ClipboardList className="w-5 h-5 text-purple-600 dark:text-purple-400" />
                    <div>
                      <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-100">Manage Notes</h3>
                      <p className="text-xs text-gray-500 dark:text-gray-400 mt-0.5">Generate, edit, and manage notes to the financial statements.</p>
                    </div>
                  </div>
                  <Link href={`/companies/${companyId}/periods/${periodId}/notes`}>
                    <Button variant="outline" size="sm">Manage Notes<ArrowRight className="w-4 h-4 ml-1.5" /></Button>
                  </Link>
                </div>
              </Card.Content>
            </Card>

            {/* Charity SoFA */}
            {company?.isCharitableOrganisation && (
              <Card className="shadow-sm border border-green-200 dark:border-green-800 bg-green-50/30 dark:bg-green-900/10">
                <Card.Content className="p-4">
                  <div className="flex items-center justify-between">
                    <div className="flex items-center gap-3">
                      <Heart className="w-5 h-5 text-green-600 dark:text-green-400" />
                      <div>
                        <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-100">Charity Reporting (SoFA)</h3>
                        <p className="text-xs text-gray-500 dark:text-gray-400 mt-0.5">Statement of Financial Activities, fund accounting, and Trustees&apos; Annual Report.</p>
                      </div>
                    </div>
                    <Link href={`/companies/${companyId}/periods/${periodId}/charity`}>
                      <Button variant="outline" size="sm">Open Charity Reporting<ArrowRight className="w-4 h-4 ml-1.5" /></Button>
                    </Link>
                  </div>
                </Card.Content>
              </Card>
            )}
          </div>
        </TabPanel>

        {/* Filing Tab */}
        <TabPanel id="filing">
          <PeriodFilingWorkspace
            filingStatus={filingStatus}
            filingReadinessProfile={filingReadinessProfile}
            croSubmissionReference={croSubmissionReference}
            validatingIxbrl={validatingIxbrl}
            canReview={canReview}
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
            notesHref={`/companies/${companyId}/periods/${periodId}/notes`}
            onCroSubmissionReferenceChange={setCroSubmissionReference}
            onRunIxbrlChecks={async () => {
              setValidatingIxbrl(true);
              try {
                const result = await validateIxbrl(cId, pId);
                if (result.ixbrlInternalChecksPassed) {
                  toast.success("Internal iXBRL checks passed; external ROS validation is still required");
                } else {
                  toast.error(result.validationErrors || "Internal iXBRL checks need attention");
                }
                await refreshFilingState();
              } catch (err) {
                toast.error(err instanceof Error ? err.message : "Internal iXBRL checks need attention");
              } finally {
                setValidatingIxbrl(false);
              }
            }}
            onApproveForFiling={async () => {
              try {
                await updateCroFilingStatus(cId, pId, { status: "Approved" });
                toast.success("Filing approved");
                await refreshFilingState();
              } catch (err) {
                toast.error(err instanceof Error ? err.message : "Failed to approve");
              }
            }}
            onMarkCroSubmitted={async (submissionReference) => {
              try {
                if (!submissionReference) {
                  toast.error("CORE submission reference is required");
                  return;
                }
                await updateCroFilingStatus(cId, pId, { status: "Submitted", submissionReference });
                toast.success("Marked as submitted to CRO");
                await refreshFilingState();
              } catch (err) {
                toast.error(err instanceof Error ? err.message : "Failed to update status");
              }
            }}
            onConfirmCroPayment={async () => {
              try {
                await confirmCroPayment(cId, pId);
                toast.success("CORE payment confirmed");
                await refreshFilingState();
              } catch (err) {
                toast.error(err instanceof Error ? err.message : "Failed to confirm payment");
              }
            }}
            onMarkCroAccepted={async () => {
              try {
                await updateCroFilingStatus(cId, pId, { status: "Accepted" });
                toast.success("CRO acceptance recorded");
                await refreshFilingState();
              } catch (err) {
                toast.error(err instanceof Error ? err.message : "Failed to mark accepted");
              }
            }}
            onRecordCroSendBack={async () => {
              try {
                await updateCroFilingStatus(cId, pId, {
                  status: "CorrectionRequired",
                  reason: "CRO send-back/correction required. Correct and redeliver within 14 days.",
                });
                toast.warning("Correction deadline opened");
                await refreshFilingState();
              } catch (err) {
                toast.error(err instanceof Error ? err.message : "Failed to record correction");
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
            onDownloadIxbrl={() => downloadDocument(ixbrlUrl, "iXBRL filing", undefined, false, "xhtml")}
          />
        </TabPanel>
      </TabsRoot>
    </div>
  );
}

/* --- Helper Components --- */

function SummaryCard({ title, value, subtitle }: { title: string; value: string; subtitle: string }) {
  return (
    <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900 card-hover">
      <Card.Content className="p-5">
        <p className="text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wide">{title}</p>
        <p className="text-xl font-bold text-gray-900 dark:text-gray-100 mt-1">{value}</p>
        <p className="text-xs text-gray-500 dark:text-gray-400 mt-0.5">{subtitle}</p>
      </Card.Content>
    </Card>
  );
}
