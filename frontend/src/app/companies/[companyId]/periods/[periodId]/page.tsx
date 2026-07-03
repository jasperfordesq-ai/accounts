"use client";

import { Fragment, use, useState, useEffect, useCallback, useRef } from "react";
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
import {
  Upload,
  Settings,
  HelpCircle,
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
  Trash2,
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
import { FilingReviewCentre } from "@/components/period/FilingReviewCentre";
import { FilingDeadlinesPanel } from "@/components/period/FilingDeadlinesPanel";
import { formatPeriodRange } from "@/lib/format";

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat("en-IE", {
    style: "currency",
    currency: "EUR",
  }).format(amount);
}

export default function PeriodWorkspacePage({
  params,
}: {
  params: Promise<{ companyId: string; periodId: string }>;
}) {
  const { companyId, periodId } = use(params);
  const cId = Number(companyId);
  const pId = Number(periodId);

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

  // Import tab state
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [selectedBankAccountId, setSelectedBankAccountId] = useState<number | "">("");
  const [uploading, setUploading] = useState(false);
  const [uploadResult, setUploadResult] = useState<{
    rowsImported: number;
    duplicatesSkipped: number;
    autoCategorised: number;
  } | null>(null);
  const [uploadError, setUploadError] = useState<string | null>(null);
  const [dragOver, setDragOver] = useState(false);

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

  function handleFileInputChange(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (file) handleFileUpload(file);
    e.target.value = "";
  }

  function handleDropzoneDrop(e: React.DragEvent<HTMLDivElement>) {
    e.preventDefault();
    e.stopPropagation();
    setDragOver(false);
    const file = e.dataTransfer.files?.[0];
    if (file) handleFileUpload(file);
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
      <TabsRoot>
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
          <div className="space-y-6">
            {/* Classify Company Size Link */}
            <Card className="shadow-sm border border-blue-200 dark:border-blue-800 bg-blue-50/30 dark:bg-blue-900/10">
              <Card.Content className="p-4">
                <div className="flex items-center justify-between">
                  <div className="flex items-center gap-3">
                    <Scale className="w-5 h-5 text-blue-600 dark:text-blue-400" />
                    <div>
                      <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-100">Company Size Classification</h3>
                      <p className="text-xs text-gray-500 dark:text-gray-400 mt-0.5">
                        Determine micro/small/medium/large status and filing regime.
                      </p>
                    </div>
                  </div>
                  <Link href={`/companies/${companyId}/periods/${periodId}/classify`}>
                    <Button variant="outline" size="sm">
                      Classify Company Size
                      <ArrowRight className="w-4 h-4 ml-1.5" />
                    </Button>
                  </Link>
                </div>
              </Card.Content>
            </Card>

            {/* Bank Accounts */}
            <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
              <Card.Header>
                <div className="flex w-full flex-col gap-3 md:flex-row md:items-center md:justify-between">
                  <div>
                    <Card.Title className="text-gray-900 dark:text-gray-100">Bank Accounts</Card.Title>
                    <Card.Description>
                      {bankAccounts.length} bank account{bankAccounts.length !== 1 ? "s" : ""} linked for import and reconciliation.
                    </Card.Description>
                  </div>
                  <Button variant="outline" size="sm" onPress={() => setShowBankForm((open) => !open)}>
                    {showBankForm ? "Cancel" : "Add Bank Account"}
                  </Button>
                </div>
              </Card.Header>
              <Card.Content>
                <div className="space-y-4">
                  {showBankForm && (
                    <div className="rounded-md border border-gray-200 bg-gray-50 p-4 dark:border-neutral-700 dark:bg-neutral-800/40">
                      <div className="grid gap-3 md:grid-cols-5">
                        <div>
                          <label className="mb-1 block text-xs font-medium text-gray-500 dark:text-gray-400">Account name</label>
                          <input
                            value={bankForm.name}
                            onChange={(e) => setBankForm((current) => ({ ...current, name: e.target.value }))}
                            className="w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 dark:border-neutral-600 dark:bg-neutral-900 dark:text-gray-100"
                            placeholder="Main current account"
                          />
                        </div>
                        <div>
                          <label className="mb-1 block text-xs font-medium text-gray-500 dark:text-gray-400">IBAN</label>
                          <input
                            value={bankForm.iban}
                            onChange={(e) => setBankForm((current) => ({ ...current, iban: e.target.value }))}
                            className="w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 dark:border-neutral-600 dark:bg-neutral-900 dark:text-gray-100"
                            placeholder="IE00..."
                          />
                        </div>
                        <div>
                          <label className="mb-1 block text-xs font-medium text-gray-500 dark:text-gray-400">Opening balance</label>
                          <input
                            type="number"
                            step="0.01"
                            value={bankForm.openingBalance}
                            onChange={(e) => setBankForm((current) => ({ ...current, openingBalance: e.target.value }))}
                            className="w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 dark:border-neutral-600 dark:bg-neutral-900 dark:text-gray-100"
                          />
                        </div>
                        <div>
                          <label className="mb-1 block text-xs font-medium text-gray-500 dark:text-gray-400">Balance date</label>
                          <input
                            type="date"
                            value={bankForm.openingBalanceDate || period?.periodStart || ""}
                            onChange={(e) => setBankForm((current) => ({ ...current, openingBalanceDate: e.target.value }))}
                            className="w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 dark:border-neutral-600 dark:bg-neutral-900 dark:text-gray-100"
                          />
                        </div>
                        <div>
                          <label className="mb-1 block text-xs font-medium text-gray-500 dark:text-gray-400">Currency</label>
                          <select
                            value={bankForm.currency}
                            onChange={(e) => setBankForm((current) => ({ ...current, currency: e.target.value }))}
                            className="w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 dark:border-neutral-600 dark:bg-neutral-900 dark:text-gray-100"
                          >
                            <option value="EUR">EUR</option>
                            <option value="GBP">GBP</option>
                            <option value="USD">USD</option>
                          </select>
                        </div>
                      </div>
                      <div className="mt-3 flex justify-end">
                        <Button variant="primary" size="sm" onPress={handleCreateBankAccount} isDisabled={savingBankAccount}>
                          {savingBankAccount ? <Spinner size="sm" /> : "Save Bank Account"}
                        </Button>
                      </div>
                    </div>
                  )}

                  {bankAccounts.length === 0 ? (
                    <div className="rounded-md border border-dashed border-gray-300 px-4 py-6 text-sm text-gray-500 dark:border-neutral-700 dark:text-gray-400">
                      No bank accounts linked yet. Add the account that matches the year-end bank statement before importing transactions.
                    </div>
                  ) : (
                    <div className="overflow-hidden rounded-md border border-gray-200 dark:border-neutral-700">
                      <div className="grid grid-cols-12 gap-2 bg-gray-50 px-4 py-2 text-xs font-semibold uppercase text-gray-500 dark:bg-neutral-800 dark:text-gray-400">
                        <div className="col-span-5">Account</div>
                        <div className="col-span-2">Currency</div>
                        <div className="col-span-3 text-right">Opening balance</div>
                        <div className="col-span-2 text-right">Import target</div>
                      </div>
                      <div className="divide-y divide-gray-100 dark:divide-neutral-800">
                        {bankAccounts.map((ba) => (
                          <div
                            key={ba.id}
                            className="grid grid-cols-12 items-center gap-2 px-4 py-3 text-sm"
                          >
                            <div className="col-span-5 min-w-0">
                              <p className="font-medium text-gray-900 dark:text-gray-100">{ba.name}</p>
                              <p className="truncate text-xs text-gray-500 dark:text-gray-400">{ba.iban || "No IBAN recorded"}</p>
                            </div>
                            <div className="col-span-2 text-gray-600 dark:text-gray-300">{ba.currency}</div>
                            <div className="col-span-3 text-right font-mono text-gray-900 dark:text-gray-100">{formatCurrency(ba.openingBalance)}</div>
                            <div className="col-span-2 text-right">
                              {selectedBankAccountId === ba.id ? (
                                <Chip color="success" variant="soft" size="sm">Selected</Chip>
                              ) : (
                                <Button variant="ghost" size="sm" onPress={() => setSelectedBankAccountId(ba.id)}>Use</Button>
                              )}
                            </div>
                          </div>
                        ))}
                      </div>
                    </div>
                  )}
                </div>
              </Card.Content>
            </Card>

            {/* Chart of Accounts Seed */}
            {categories.length === 0 && (
              <Card className="shadow-sm border border-amber-200 dark:border-amber-800 bg-amber-50/30 dark:bg-amber-900/10">
                <Card.Content className="p-4">
                  <div className="flex items-center justify-between">
                    <div className="flex items-center gap-3">
                      <Settings className="w-5 h-5 text-amber-600 dark:text-amber-400" />
                      <div>
                        <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-100">Chart of Accounts</h3>
                        <p className="text-xs text-gray-500 dark:text-gray-400 mt-0.5">No categories configured. Seed the default Irish chart of accounts.</p>
                      </div>
                    </div>
                    <Button variant="outline" size="sm" onPress={async () => {
                      try {
                        const cats = await seedCategories(cId);
                        setCategories(cats);
                        toast.success(`${cats.length} categories seeded`);
                      } catch { toast.error("Failed to seed categories"); }
                    }}>
                      Seed Categories
                    </Button>
                  </div>
                </Card.Content>
              </Card>
            )}

            {/* Opening Balances */}
            <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
              <Card.Header>
                <div className="flex w-full flex-col gap-3 md:flex-row md:items-center md:justify-between">
                  <div>
                    <Card.Title className="text-gray-900 dark:text-gray-100">Opening Balances & Reserves</Card.Title>
                    <Card.Description>
                      Enter reviewed opening reserves, share capital, creditors, and other balance-sheet balances before finalising.
                    </Card.Description>
                  </div>
                  <Chip color={Math.abs(openingDifference) < 0.01 ? "success" : "warning"} variant="soft" size="sm">
                    Difference {formatCurrency(openingDifference)}
                  </Chip>
                </div>
              </Card.Header>
              <Card.Content>
                <div className="grid gap-3 md:grid-cols-5">
                  <div className="md:col-span-2">
                    <label className="mb-1 block text-xs font-medium text-gray-500 dark:text-gray-400">Account</label>
                    <select
                      value={openingBalanceForm.categoryId}
                      onChange={(e) => setOpeningBalanceForm((current) => ({ ...current, categoryId: e.target.value }))}
                      className="w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 dark:border-neutral-600 dark:bg-neutral-900 dark:text-gray-100"
                    >
                      <option value="">Select account...</option>
                      {openingBalanceCategories.map((category) => (
                        <option key={category.id} value={category.id}>
                          {category.code} - {category.name}
                        </option>
                      ))}
                    </select>
                  </div>
                  <div>
                    <label className="mb-1 block text-xs font-medium text-gray-500 dark:text-gray-400">Side</label>
                    <select
                      value={openingBalanceForm.side}
                      onChange={(e) => setOpeningBalanceForm((current) => ({ ...current, side: e.target.value }))}
                      className="w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 dark:border-neutral-600 dark:bg-neutral-900 dark:text-gray-100"
                    >
                      <option value="debit">Debit</option>
                      <option value="credit">Credit</option>
                    </select>
                  </div>
                  <div>
                    <label className="mb-1 block text-xs font-medium text-gray-500 dark:text-gray-400">Amount</label>
                    <input
                      type="number"
                      step="0.01"
                      value={openingBalanceForm.amount}
                      onChange={(e) => setOpeningBalanceForm((current) => ({ ...current, amount: e.target.value }))}
                      className="w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 dark:border-neutral-600 dark:bg-neutral-900 dark:text-gray-100"
                    />
                  </div>
                  <div>
                    <label className="mb-1 block text-xs font-medium text-gray-500 dark:text-gray-400">Evidence note</label>
                    <input
                      value={openingBalanceForm.sourceNote}
                      onChange={(e) => setOpeningBalanceForm((current) => ({ ...current, sourceNote: e.target.value }))}
                      className="w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 dark:border-neutral-600 dark:bg-neutral-900 dark:text-gray-100"
                      placeholder="Prior accounts / TB"
                    />
                  </div>
                </div>
                <div className="mt-3 flex justify-end">
                  <Button variant="primary" size="sm" onPress={handleSaveOpeningBalance} isDisabled={savingOpeningBalance || categories.length === 0}>
                    {savingOpeningBalance ? <Spinner size="sm" /> : "Save Reviewed Balance"}
                  </Button>
                </div>

                <div className="mt-4 overflow-hidden rounded-md border border-gray-200 dark:border-neutral-700">
                  <div className="grid grid-cols-12 gap-2 bg-gray-50 px-4 py-2 text-xs font-semibold uppercase text-gray-500 dark:bg-neutral-800 dark:text-gray-400">
                    <div className="col-span-5">Account</div>
                    <div className="col-span-2 text-right">Debit</div>
                    <div className="col-span-2 text-right">Credit</div>
                    <div className="col-span-2">Evidence</div>
                    <div className="col-span-1 text-right">Action</div>
                  </div>
                  {openingBalances.length === 0 ? (
                    <div className="px-4 py-5 text-sm text-gray-500 dark:text-gray-400">
                      No reviewed opening balances entered yet. Bank account opening balances are included automatically, but reserves/equity need an explicit balancing entry.
                    </div>
                  ) : (
                    openingBalances.map((balance) => (
                      <div key={balance.id} className="grid grid-cols-12 gap-2 border-t border-gray-100 px-4 py-3 text-sm dark:border-neutral-800">
                        <div className="col-span-5">
                          <span className="font-mono text-xs text-gray-500">{balance.accountCategory.code}</span>{" "}
                          <span className="font-medium text-gray-900 dark:text-gray-100">{balance.accountCategory.name}</span>
                        </div>
                        <div className="col-span-2 text-right font-mono">{balance.debit ? formatCurrency(balance.debit) : "-"}</div>
                        <div className="col-span-2 text-right font-mono">{balance.credit ? formatCurrency(balance.credit) : "-"}</div>
                        <div className="col-span-2 truncate text-xs text-gray-500" title={balance.sourceNote ?? ""}>
                          {balance.reviewed ? "Reviewed" : "Unreviewed"}{balance.sourceNote ? ` - ${balance.sourceNote}` : ""}
                        </div>
                        <div className="col-span-1 text-right">
                          <Button variant="ghost" size="sm" onPress={() => handleDeleteOpeningBalance(balance.accountCategoryId)} isDisabled={deletingOpeningCategoryId === balance.accountCategoryId}>
                            {deletingOpeningCategoryId === balance.accountCategoryId ? <Spinner size="sm" /> : <Trash2 className="h-4 w-4" />}
                          </Button>
                        </div>
                      </div>
                    ))
                  )}
                </div>
              </Card.Content>
            </Card>

            {/* Upload Area */}
            <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
              <Card.Header>
                <Card.Title className="text-gray-900 dark:text-gray-100">Import Transactions</Card.Title>
                <Card.Description>Upload bank statements in CSV format (AIB, BOI, Revolut, Stripe)</Card.Description>
              </Card.Header>
              <Card.Content>
                <div className="mb-4">
                  <label htmlFor="bank-account-select" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5">
                    Import into bank account
                  </label>
                  <select
                    id="bank-account-select"
                    value={selectedBankAccountId}
                    onChange={(e) => setSelectedBankAccountId(e.target.value ? Number(e.target.value) : "")}
                    className="w-full rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 px-3 py-2 text-sm text-gray-900 dark:text-gray-100 shadow-sm focus:border-emerald-500 focus:outline-none focus:ring-1 focus:ring-emerald-500 transition-colors"
                    title="Select bank account"
                    aria-label="Select bank account"
                  >
                    <option value="">Select a bank account...</option>
                    {bankAccounts.map((ba) => (
                      <option key={ba.id} value={ba.id}>
                        {ba.name}{ba.iban ? ` (${ba.iban})` : ""} - {ba.currency}
                      </option>
                    ))}
                  </select>
                </div>

                <input
                  ref={fileInputRef}
                  type="file"
                  accept=".csv"
                  className="hidden"
                  onChange={handleFileInputChange}
                  aria-label="Upload CSV file"
                />

                {/* Dropzone with drag feedback */}
                <div
                  className={`border-2 border-dashed rounded-xl p-10 text-center transition-all cursor-pointer ${
                    dragOver
                      ? "border-emerald-500 bg-emerald-50 dark:bg-emerald-900/20 scale-[1.01]"
                      : "border-gray-300 dark:border-neutral-600 hover:border-emerald-400 dark:hover:border-emerald-600"
                  }`}
                  onClick={() => fileInputRef.current?.click()}
                  onDrop={handleDropzoneDrop}
                  onDragOver={(e) => { e.preventDefault(); e.stopPropagation(); setDragOver(true); }}
                  onDragEnter={(e) => { e.preventDefault(); setDragOver(true); }}
                  onDragLeave={(e) => { e.preventDefault(); setDragOver(false); }}
                  role="button"
                  tabIndex={0}
                  aria-label="Upload CSV file by clicking or dragging"
                  onKeyDown={(e) => {
                    if (e.key === "Enter" || e.key === " ") fileInputRef.current?.click();
                  }}
                >
                  {uploading ? (
                    <>
                      <Spinner size="sm" className="mx-auto mb-3" />
                      <p className="text-sm font-medium text-gray-700 dark:text-gray-300">
                        Uploading and processing...
                      </p>
                    </>
                  ) : dragOver ? (
                    <>
                      <Upload className="w-10 h-10 text-emerald-500 mx-auto mb-3" />
                      <p className="text-sm font-medium text-emerald-700 dark:text-emerald-400">
                        Drop your CSV file here
                      </p>
                    </>
                  ) : (
                    <>
                      <Upload className="w-10 h-10 text-gray-400 dark:text-gray-500 mx-auto mb-3" />
                      <p className="text-sm font-medium text-gray-700 dark:text-gray-300">
                        Drag and drop a CSV file here, or click to browse
                      </p>
                      <p className="text-xs text-gray-500 dark:text-gray-400 mt-1">
                        Supports AIB, BOI, Revolut, and Stripe CSV formats
                      </p>
                    </>
                  )}
                </div>

                {uploadError && (
                  <div className="mt-4 rounded-lg bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 px-4 py-3 text-sm text-red-700 dark:text-red-400 animate-fade-in">
                    {uploadError}
                  </div>
                )}
              </Card.Content>
            </Card>

            {/* Import Result */}
            <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
              <Card.Header>
                <Card.Title className="text-gray-900 dark:text-gray-100">Import Status</Card.Title>
              </Card.Header>
              <Card.Content>
                {uploadResult ? (
                  <div className="space-y-3 animate-fade-in">
                    <div className="flex items-center gap-3 text-sm text-emerald-700 dark:text-emerald-400">
                      <CheckCircle2 className="w-5 h-5 text-emerald-600 dark:text-emerald-500" />
                      <span className="font-medium">Import completed successfully</span>
                    </div>
                    <div className="grid grid-cols-3 gap-4">
                      <div className="rounded-lg bg-emerald-50 dark:bg-emerald-900/20 p-4 text-center">
                        <p className="text-2xl font-bold text-emerald-700 dark:text-emerald-400">{uploadResult.rowsImported}</p>
                        <p className="text-xs text-gray-500 dark:text-gray-400 mt-1">Rows Imported</p>
                      </div>
                      <div className="rounded-lg bg-gray-50 dark:bg-neutral-800 p-4 text-center">
                        <p className="text-2xl font-bold text-gray-700 dark:text-gray-300">{uploadResult.duplicatesSkipped}</p>
                        <p className="text-xs text-gray-500 dark:text-gray-400 mt-1">Duplicates Skipped</p>
                      </div>
                      <div className="rounded-lg bg-blue-50 dark:bg-blue-900/20 p-4 text-center">
                        <p className="text-2xl font-bold text-blue-700 dark:text-blue-400">{uploadResult.autoCategorised}</p>
                        <p className="text-xs text-gray-500 dark:text-gray-400 mt-1">Auto-Categorised</p>
                      </div>
                    </div>
                  </div>
                ) : (
                  <div className="flex items-center gap-3 text-sm text-gray-500 dark:text-gray-400">
                    <HelpCircle className="w-5 h-5" />
                    <span>No imports have been processed for this period yet.</span>
                  </div>
                )}
              </Card.Content>
            </Card>
          </div>
        </TabPanel>

        {/* Categorise Tab */}
        <TabPanel id="categorise">
          <div className="space-y-6">
            <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
              <Card.Header>
                <div className="flex items-center justify-between w-full">
                  <div>
                    <Card.Title className="text-gray-900 dark:text-gray-100">Categorisation Overview</Card.Title>
                    <Card.Description>Review and categorise imported transactions</Card.Description>
                  </div>
                  <Button variant="ghost" size="sm" onPress={loadTransactions} isDisabled={loadingTransactions}>
                    <RefreshCw className={`w-4 h-4 mr-1 ${loadingTransactions ? "animate-spin" : ""}`} />
                    Refresh
                  </Button>
                </div>
              </Card.Header>
              <Card.Content>
                <div className="grid grid-cols-3 gap-4 mb-6">
                  <div className="rounded-lg bg-gray-50 dark:bg-neutral-800 p-4 text-center">
                    <p className="text-2xl font-bold text-gray-900 dark:text-gray-100">{transactionTotal}</p>
                    <p className="text-xs text-gray-500 dark:text-gray-400 mt-1">Total Transactions</p>
                  </div>
                  <div className="rounded-lg bg-emerald-50 dark:bg-emerald-900/20 p-4 text-center">
                    <p className="text-2xl font-bold text-emerald-700 dark:text-emerald-400">{categorisedCount}</p>
                    <p className="text-xs text-gray-500 dark:text-gray-400 mt-1">Categorised</p>
                  </div>
                  <div className="rounded-lg bg-amber-50 dark:bg-amber-900/20 p-4 text-center">
                    <p className="text-2xl font-bold text-amber-700 dark:text-amber-400">{uncategorisedCount}</p>
                    <p className="text-xs text-gray-500 dark:text-gray-400 mt-1">Uncategorised</p>
                  </div>
                </div>

                {/* Categorisation progress bar */}
                {transactionTotal > 0 && (
                  <div className="mb-6">
                    <div className="flex items-center justify-between text-sm mb-1.5">
                      <span className="text-gray-600 dark:text-gray-400">Categorisation Progress</span>
                      <span className="font-medium text-gray-900 dark:text-gray-100">
                        {transactionTotal > 0 ? Math.round((categorisedCount / transactionTotal) * 100) : 0}%
                      </span>
                    </div>
                    <ProgressBar
                      value={categorisedCount}
                      minValue={0}
                      maxValue={transactionTotal || 1}
                      aria-label="Categorisation progress"
                      color={categorisedCount === transactionTotal ? "success" : "warning"}
                    >
                      <ProgressBarTrack>
                        <ProgressBarFill />
                      </ProgressBarTrack>
                    </ProgressBar>
                  </div>
                )}

                <div className="mb-6 rounded-md border border-gray-200 bg-gray-50 p-4 dark:border-neutral-700 dark:bg-neutral-800/40">
                  <div className="mb-3 flex flex-col gap-1 md:flex-row md:items-center md:justify-between">
                    <div>
                      <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-100">Transaction Rules</h3>
                      <p className="text-xs text-gray-500 dark:text-gray-400">
                        Match recurring bank descriptions to account categories for future imports.
                      </p>
                    </div>
                    <Chip size="sm" variant="soft" color={transactionRules.length > 0 ? "success" : "default"}>
                      {transactionRules.length} rule{transactionRules.length !== 1 ? "s" : ""}
                    </Chip>
                  </div>
                  <div className="grid gap-3 md:grid-cols-12">
                    <input
                      value={ruleForm.pattern}
                      onChange={(e) => setRuleForm((current) => ({ ...current, pattern: e.target.value }))}
                      placeholder="Description contains..."
                      className="md:col-span-5 rounded-md border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 dark:border-neutral-600 dark:bg-neutral-900 dark:text-gray-100"
                      aria-label="Rule pattern"
                    />
                    <select
                      value={ruleForm.categoryId}
                      onChange={(e) => setRuleForm((current) => ({ ...current, categoryId: e.target.value }))}
                      className="md:col-span-4 rounded-md border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 dark:border-neutral-600 dark:bg-neutral-900 dark:text-gray-100"
                      aria-label="Rule category"
                    >
                      <option value="">Choose category</option>
                      {categories.map((cat) => (
                        <option key={cat.id} value={cat.id}>
                          {cat.code ? `${cat.code} - ${cat.name}` : cat.name}
                        </option>
                      ))}
                    </select>
                    <input
                      type="number"
                      value={ruleForm.priority}
                      onChange={(e) => setRuleForm((current) => ({ ...current, priority: e.target.value }))}
                      className="md:col-span-1 rounded-md border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 dark:border-neutral-600 dark:bg-neutral-900 dark:text-gray-100"
                      aria-label="Rule priority"
                    />
                    <Button variant="outline" size="sm" onPress={handleCreateRule} isDisabled={savingRule || categories.length === 0} className="md:col-span-2">
                      {savingRule ? <Spinner size="sm" /> : "Add Rule"}
                    </Button>
                  </div>
                  {transactionRules.length > 0 && (
                    <div className="mt-4 overflow-hidden rounded-md border border-gray-200 bg-white dark:border-neutral-700 dark:bg-neutral-900">
                      {transactionRules.map((rule) => {
                        const category = categories.find((cat) => cat.id === rule.categoryId);
                        return (
                          <div key={rule.id} className="grid grid-cols-12 items-center gap-2 border-b border-gray-100 px-3 py-2 text-xs last:border-b-0 dark:border-neutral-800">
                            <div className="col-span-5 font-medium text-gray-900 dark:text-gray-100">{rule.pattern}</div>
                            <div className="col-span-5 text-gray-600 dark:text-gray-300">
                              {category ? (category.code ? `${category.code} - ${category.name}` : category.name) : `Category ${rule.categoryId}`}
                            </div>
                            <div className="col-span-1 text-right text-gray-500 dark:text-gray-400">{rule.priority}</div>
                            <div className="col-span-1 text-right">
                              <Button variant="ghost" size="sm" onPress={() => handleDeleteRule(rule.id)} isDisabled={deletingRuleId === rule.id}>
                                {deletingRuleId === rule.id ? <Spinner size="sm" /> : "Delete"}
                              </Button>
                            </div>
                          </div>
                        );
                      })}
                    </div>
                  )}
                </div>

                {transactions.length > 0 && (
                  <div className="mb-4 rounded-md border border-slate-200 bg-slate-50 p-3 dark:border-neutral-700 dark:bg-neutral-800/40">
                    <div className="flex flex-col gap-3 md:flex-row md:items-end md:justify-between">
                      <div>
                        <p className="text-sm font-semibold text-gray-900 dark:text-gray-100">Bulk categorisation</p>
                        <p className="text-xs text-gray-500 dark:text-gray-400">
                          {selectedTransactionIds.length} selected. Use this for reviewed recurring items only.
                        </p>
                      </div>
                      <div className="flex flex-col gap-2 md:flex-row md:items-center">
                        <select
                          value={bulkCategoryId}
                          onChange={(e) => setBulkCategoryId(e.target.value)}
                          className="min-w-64 rounded-md border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 dark:border-neutral-600 dark:bg-neutral-900 dark:text-gray-100"
                          aria-label="Bulk category"
                        >
                          <option value="">Choose category</option>
                          {categories.map((cat) => (
                            <option key={cat.id} value={cat.id}>
                              {cat.code ? `${cat.code} - ${cat.name}` : cat.name}
                            </option>
                          ))}
                        </select>
                        <Button
                          variant="primary"
                          size="sm"
                          onPress={handleBulkCategoriseTransactions}
                          isDisabled={bulkCategorising || selectedTransactionIds.length === 0 || !bulkCategoryId}
                        >
                          {bulkCategorising ? <Spinner size="sm" /> : "Apply to Selected"}
                        </Button>
                      </div>
                    </div>
                  </div>
                )}

                {/* Transaction Filters */}
                <div className="grid grid-cols-4 gap-3 mb-4">
                  <div>
                    <label className="block text-xs font-medium text-gray-500 dark:text-gray-400 mb-1">Status</label>
                    <select
                      className="w-full rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 text-sm text-gray-900 dark:text-gray-100 px-3 py-2"
                      value={txFilterStatus}
                      onChange={(e) => setTxFilterStatus(e.target.value)}
                    >
                      <option value="">All</option>
                      <option value="uncategorised">Uncategorised</option>
                    </select>
                  </div>
                  <div>
                    <label className="block text-xs font-medium text-gray-500 dark:text-gray-400 mb-1">Category</label>
                    <select
                      className="w-full rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 text-sm text-gray-900 dark:text-gray-100 px-3 py-2"
                      value={txFilterCategory}
                      onChange={(e) => setTxFilterCategory(e.target.value)}
                    >
                      <option value="">All Categories</option>
                      {categories.map((cat) => (
                        <option key={cat.id} value={cat.id}>{cat.code ? `${cat.code} - ${cat.name}` : cat.name}</option>
                      ))}
                    </select>
                  </div>
                  <div>
                    <label className="block text-xs font-medium text-gray-500 dark:text-gray-400 mb-1">Bank Account</label>
                    <select
                      className="w-full rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 text-sm text-gray-900 dark:text-gray-100 px-3 py-2"
                      value={txFilterBank}
                      onChange={(e) => setTxFilterBank(e.target.value)}
                    >
                      <option value="">All Accounts</option>
                      {bankAccounts.map((ba) => (
                        <option key={ba.id} value={ba.id}>{ba.name} {ba.iban ? `(${ba.iban})` : ""}</option>
                      ))}
                    </select>
                  </div>
                  <div>
                    <label className="block text-xs font-medium text-gray-500 dark:text-gray-400 mb-1">Search</label>
                    <input
                      type="text"
                      placeholder="Search description..."
                      className="w-full rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 text-sm text-gray-900 dark:text-gray-100 px-3 py-2"
                      defaultValue={txFilterSearch}
                      onChange={(e) => {
                        const val = e.target.value;
                        if (txSearchTimerRef.current) clearTimeout(txSearchTimerRef.current);
                        txSearchTimerRef.current = setTimeout(() => setTxFilterSearch(val), 400);
                      }}
                    />
                  </div>
                </div>

                {loadingTransactions ? (
                  <div className="flex items-center justify-center py-8">
                    <Spinner size="sm" />
                  </div>
                ) : transactions.length === 0 ? (
                  <div className="text-center py-8">
                    <Settings className="w-10 h-10 text-gray-300 dark:text-gray-600 mx-auto mb-3" />
                    <p className="text-sm text-gray-400 dark:text-gray-500 italic">
                      Import transactions to begin categorisation
                    </p>
                  </div>
                ) : (
                  <div className="border border-gray-200 dark:border-neutral-700 rounded-lg overflow-hidden">
                    {/* Header row */}
                    <div className="grid grid-cols-12 gap-2 bg-gray-50 dark:bg-neutral-800 border-b border-gray-200 dark:border-neutral-700 px-4 py-2.5 text-xs font-medium text-gray-600 dark:text-gray-400 uppercase tracking-wide">
                      <div className="col-span-1">
                        <input
                          type="checkbox"
                          checked={allVisibleTransactionsSelected}
                          onChange={(e) => {
                            setSelectedTransactionIds(e.target.checked ? visibleTransactionIds : []);
                          }}
                          aria-label="Select visible transactions"
                        />
                      </div>
                      <div className="col-span-2">Date</div>
                      <div className="col-span-3">Description</div>
                      <div className="col-span-2 text-right">Amount</div>
                      <div className="col-span-3">Category</div>
                      <div className="col-span-1 text-center">Conf.</div>
                    </div>
                    <div className="divide-y divide-gray-100 dark:divide-neutral-700">
                      {transactions.map((tx, idx) => (
                        <div
                          key={tx.id}
                          className={`grid grid-cols-12 gap-2 px-4 py-3 text-sm hover:bg-gray-50 dark:hover:bg-neutral-800/50 items-center transition-colors ${
                            idx % 2 === 1 ? "bg-gray-50/50 dark:bg-neutral-800/25" : ""
                          }`}
                        >
                          <div className="col-span-1">
                            <input
                              type="checkbox"
                              checked={selectedTransactionIds.includes(tx.id)}
                              onChange={(e) => {
                                setSelectedTransactionIds((current) =>
                                  e.target.checked
                                    ? [...current, tx.id]
                                    : current.filter((id) => id !== tx.id)
                                );
                              }}
                              aria-label={`Select ${tx.description}`}
                            />
                          </div>
                          <div className="col-span-2 text-gray-600 dark:text-gray-400">
                            {new Date(tx.date).toLocaleDateString("en-IE")}
                          </div>
                          <div className="col-span-3 text-gray-900 dark:text-gray-100 truncate" title={tx.description}>
                            {tx.description}
                          </div>
                          <div className={`col-span-2 text-right font-medium font-mono ${tx.amount >= 0 ? "text-emerald-700 dark:text-emerald-400" : "text-red-700 dark:text-red-400"}`}>
                            {formatCurrency(tx.amount)}
                          </div>
                          <div className="col-span-3">
                            <div className="flex items-center gap-2">
                              <select
                                value={tx.categoryId ?? ""}
                                onChange={(e) => {
                                  if (e.target.value) {
                                    handleCategoriseTransaction(tx.id, Number(e.target.value));
                                  }
                                }}
                                disabled={categories.length === 0 || categorisingId === tx.id}
                                aria-label={`Categorise ${tx.description}`}
                                className="min-w-0 flex-1 rounded-md border border-gray-300 bg-white px-2 py-1.5 text-xs text-gray-900 dark:border-neutral-600 dark:bg-neutral-900 dark:text-gray-100"
                              >
                                <option value="">Uncategorised</option>
                                {categories.map((cat) => (
                                  <option key={cat.id} value={cat.id}>
                                    {cat.code ? `${cat.code} - ${cat.name}` : cat.name}
                                  </option>
                                ))}
                              </select>
                              {categorisingId === tx.id && <Spinner size="sm" />}
                            </div>
                            {tx.manualOverride && (
                              <p className="mt-1 text-[11px] text-blue-600 dark:text-blue-400">Manual</p>
                            )}
                          </div>
                          <div className="col-span-1 text-center text-xs text-gray-400 dark:text-gray-500">
                            {tx.confidenceScore != null ? `${Math.round(tx.confidenceScore * 100)}%` : "--"}
                          </div>
                        </div>
                      ))}
                    </div>
                    {transactionTotal > transactions.length && (
                      <div className="bg-gray-50 dark:bg-neutral-800 border-t border-gray-200 dark:border-neutral-700 px-4 py-2.5 text-xs text-gray-500 dark:text-gray-400 text-center">
                        Showing {transactions.length} of {transactionTotal} transactions
                      </div>
                    )}
                  </div>
                )}
              </Card.Content>
            </Card>
          </div>
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
            <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
              <Card.Header>
                <Card.Title className="text-gray-900 dark:text-gray-100">Filing Readiness</Card.Title>
                <Card.Description>Assessment of whether the accounts are ready for filing</Card.Description>
              </Card.Header>
              <Card.Content>
                {readiness ? (
                  <div className="space-y-6">
                    <div className="grid grid-cols-2 gap-6">
                      <div>
                        <div className="flex items-center justify-between text-sm mb-2">
                          <span className="text-gray-600 dark:text-gray-400">Completeness</span>
                          <span className="font-medium text-gray-900 dark:text-gray-100">{readiness.completenessPercent}%</span>
                        </div>
                        <ProgressBar value={readiness.completenessPercent} minValue={0} maxValue={100} aria-label="Completeness" color={readiness.completenessPercent >= 80 ? "success" : "warning"}>
                          <ProgressBarTrack><ProgressBarFill /></ProgressBarTrack>
                        </ProgressBar>
                      </div>
                      <div>
                        <div className="flex items-center justify-between text-sm mb-2">
                          <span className="text-gray-600 dark:text-gray-400">Filing Readiness</span>
                          <span className="font-medium text-gray-900 dark:text-gray-100">{readiness.filingReadinessPercent}%</span>
                        </div>
                        <ProgressBar value={readiness.filingReadinessPercent} minValue={0} maxValue={100} aria-label="Filing readiness" color={readiness.filingReadinessPercent >= 80 ? "success" : "warning"}>
                          <ProgressBarTrack><ProgressBarFill /></ProgressBarTrack>
                        </ProgressBar>
                      </div>
                    </div>

                    <div className="flex items-center gap-2 text-sm">
                      {readiness.balanceSheetBalances ? (
                        <><CheckCircle2 className="w-5 h-5 text-emerald-600" /><span className="text-emerald-700 dark:text-emerald-400 font-medium">Balance sheet balances</span></>
                      ) : (
                        <><AlertTriangle className="w-5 h-5 text-amber-500" /><span className="text-amber-700 dark:text-amber-400 font-medium">Balance sheet does not balance</span></>
                      )}
                    </div>

                    {readiness.missingItems.length > 0 && (
                      <div>
                        <h4 className="text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">Missing Items</h4>
                        <ul className="space-y-1.5">
                          {readiness.missingItems.map((item, i) => (
                            <li key={i} className="flex items-start gap-2 text-sm text-red-700 dark:text-red-400">
                              <span className="w-1.5 h-1.5 rounded-full bg-red-400 mt-1.5 shrink-0" />{item}
                            </li>
                          ))}
                        </ul>
                      </div>
                    )}

                    {readiness.warnings.length > 0 && (
                      <div>
                        <h4 className="text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">Warnings</h4>
                        <ul className="space-y-1.5">
                          {readiness.warnings.map((warning, i) => (
                            <li key={i} className="flex items-start gap-2 text-sm text-amber-700 dark:text-amber-400">
                              <AlertTriangle className="w-4 h-4 text-amber-500 mt-0.5 shrink-0" />{warning}
                            </li>
                          ))}
                        </ul>
                      </div>
                    )}
                  </div>
                ) : (
                  <div className="text-center py-8">
                    <FileText className="w-10 h-10 text-gray-300 dark:text-gray-600 mx-auto mb-3" />
                    <p className="text-sm text-gray-500 dark:text-gray-400">Readiness data is not available yet.</p>
                    <p className="text-xs text-gray-400 dark:text-gray-500 mt-1">Complete the year-end process and generate adjustments first.</p>
                  </div>
                )}
              </Card.Content>
            </Card>

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
          <div className="space-y-6">
            <FilingReviewCentre
              filingStatus={filingStatus}
              filingReadinessProfile={filingReadinessProfile}
              croSubmissionReference={croSubmissionReference}
              validatingIxbrl={validatingIxbrl}
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
            />

            <FilingDeadlinesPanel
              deadlines={deadlinesList}
              filingStatus={filingStatus}
              filingReferences={filingReferences}
              markingFiledId={markingFiledId}
              onFilingReferenceChange={(deadlineId, value) => {
                setFilingReferences((current) => ({
                  ...current,
                  [deadlineId]: value,
                }));
              }}
              onMarkFiled={handleMarkDeadlineFiled}
              onReferenceMissing={toast.error}
            />

            {/* Audit Exemption Jeopardy Warning */}
            {jeopardy?.warning && (
              <div className={`rounded-lg px-4 py-3 text-sm ${jeopardy.hasLostExemption ? "bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 text-red-700 dark:text-red-400" : "bg-amber-50 dark:bg-amber-900/20 border border-amber-200 dark:border-amber-800 text-amber-700 dark:text-amber-400"}`}>
                {jeopardy.warning}
              </div>
            )}

            {/* s.307 Director Loan Disclosure */}
            {section307Note && (
              <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
                <Card.Header>
                  <Card.Title className="text-gray-900 dark:text-gray-100">s.307 Director Loan Disclosure</Card.Title>
                </Card.Header>
                <Card.Content>
                  <pre className="text-sm text-gray-700 dark:text-gray-300 whitespace-pre-wrap font-sans">{section307Note}</pre>
                </Card.Content>
              </Card>
            )}

            <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
              <Card.Header>
                <Card.Title className="text-gray-900 dark:text-gray-100">Download Documents</Card.Title>
                <Card.Description>Download the final accounts package and iXBRL filing documents</Card.Description>
              </Card.Header>
              <Card.Content>
                <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
                  <button type="button" onClick={() => downloadDocument(agmPackUrl, "AGM pack")} disabled={downloadingDocument !== null} className="flex flex-col items-center gap-3 rounded-xl border-2 border-gray-200 p-8 text-center transition-all hover:border-emerald-400 hover:bg-emerald-50/30 disabled:cursor-wait disabled:opacity-70 dark:border-neutral-700 dark:hover:border-emerald-600 dark:hover:bg-emerald-900/10 group">
                    <FileText className="w-12 h-12 text-gray-400 dark:text-gray-500 group-hover:text-emerald-600 dark:group-hover:text-emerald-400 transition-colors" />
                    <div className="text-center">
                      <p className="text-sm font-semibold text-gray-900 dark:text-gray-100">AGM Pack</p>
                      <p className="text-xs text-gray-500 dark:text-gray-400 mt-0.5">Full statutory accounts for AGM approval</p>
                    </div>
                    <span className="inline-flex h-9 items-center gap-2 rounded-md border border-gray-300 px-3 text-sm font-medium text-gray-700 dark:border-neutral-600 dark:text-gray-200">
                      {downloadingDocument === "AGM pack" ? <Spinner size="sm" /> : <Download className="w-4 h-4" />}
                      Download PDF
                    </span>
                  </button>
                  <button type="button" onClick={() => downloadDocument(croPackUrl, "CRO filing pack", "accounts", true)} disabled={downloadingDocument !== null} className={`flex flex-col items-center gap-3 rounded-xl border-2 border-gray-200 p-8 text-center transition-all dark:border-neutral-700 group ${period?.filingRegime ? "hover:border-emerald-400 hover:bg-emerald-50/30 dark:hover:border-emerald-600 dark:hover:bg-emerald-900/10 disabled:cursor-wait disabled:opacity-70" : "cursor-not-allowed opacity-60"}`}>
                    <FileText className="w-12 h-12 text-gray-400 dark:text-gray-500 group-hover:text-emerald-600 dark:group-hover:text-emerald-400 transition-colors" />
                    <div className="text-center">
                      <p className="text-sm font-semibold text-gray-900 dark:text-gray-100">CRO Filing Pack</p>
                      <p className="text-xs text-gray-500 dark:text-gray-400 mt-0.5">Abridged accounts for CRO filing</p>
                    </div>
                    <span className="inline-flex h-9 items-center gap-2 rounded-md border border-gray-300 px-3 text-sm font-medium text-gray-700 dark:border-neutral-600 dark:text-gray-200">
                      {downloadingDocument === "CRO filing pack" ? <Spinner size="sm" /> : <Download className="w-4 h-4" />}
                      Download PDF
                    </span>
                  </button>
                  <button type="button" onClick={() => downloadDocument(sigPageUrl, "signature page", "signature", true)} disabled={downloadingDocument !== null} className={`flex flex-col items-center gap-3 rounded-xl border-2 border-gray-200 p-8 text-center transition-all dark:border-neutral-700 group ${period?.filingRegime ? "hover:border-emerald-400 hover:bg-emerald-50/30 dark:hover:border-emerald-600 dark:hover:bg-emerald-900/10 disabled:cursor-wait disabled:opacity-70" : "cursor-not-allowed opacity-60"}`}>
                    <FileText className="w-12 h-12 text-gray-400 dark:text-gray-500 group-hover:text-emerald-600 dark:group-hover:text-emerald-400 transition-colors" />
                    <div className="text-center">
                      <p className="text-sm font-semibold text-gray-900 dark:text-gray-100">Signature Page</p>
                      <p className="text-xs text-gray-500 dark:text-gray-400 mt-0.5">Typeset signatures for CRO (s.347)</p>
                    </div>
                    <span className="inline-flex h-9 items-center gap-2 rounded-md border border-gray-300 px-3 text-sm font-medium text-gray-700 dark:border-neutral-600 dark:text-gray-200">
                      {downloadingDocument === "signature page" ? <Spinner size="sm" /> : <Download className="w-4 h-4" />}
                      Download PDF
                    </span>
                  </button>
                  <button type="button" onClick={() => downloadDocument(ixbrlUrl, "iXBRL filing", undefined, false, "xhtml")} disabled={downloadingDocument !== null} className="flex flex-col items-center gap-3 rounded-xl border-2 border-gray-200 p-8 text-center transition-all hover:border-emerald-400 hover:bg-emerald-50/30 disabled:cursor-wait disabled:opacity-70 dark:border-neutral-700 dark:hover:border-emerald-600 dark:hover:bg-emerald-900/10 group">
                    <FileText className="w-12 h-12 text-gray-400 dark:text-gray-500 group-hover:text-emerald-600 dark:group-hover:text-emerald-400 transition-colors" />
                    <div className="text-center">
                      <p className="text-sm font-semibold text-gray-900 dark:text-gray-100">iXBRL Filing</p>
                      <p className="text-xs text-gray-500 dark:text-gray-400 mt-0.5">For Revenue Online Service (ROS) submission</p>
                    </div>
                    <span className="inline-flex h-9 items-center gap-2 rounded-md border border-gray-300 px-3 text-sm font-medium text-gray-700 dark:border-neutral-600 dark:text-gray-200">
                      {downloadingDocument === "iXBRL filing" ? <Spinner size="sm" /> : <Download className="w-4 h-4" />}
                      Download iXBRL
                    </span>
                  </button>
                </div>
              </Card.Content>
            </Card>

            <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
              <Card.Header>
                <Card.Title className="text-gray-900 dark:text-gray-100">Filing Checklist</Card.Title>
              </Card.Header>
              <Card.Content>
                <div className="space-y-3">
                  <ChecklistItem label="All transactions imported and categorised" done={transactionTotal > 0 && uncategorisedCount === 0} />
                  <ChecklistItem label="Year-end adjustments generated and reviewed" done={adjSummary != null && adjSummary.pendingApproval === 0 && (adjSummary.autoGenerated + adjSummary.manual) > 0} />
                  <ChecklistItem label="Balance sheet balances" done={readiness?.balanceSheetBalances ?? false} />
                  <ChecklistItem label="Filing readiness at 100%" done={(readiness?.filingReadinessPercent ?? 0) >= 100} />
                  <ChecklistItem label="CRO accounts PDF generated" done={filingStatus?.cro.accountsPdfReady ?? false} />
                  <ChecklistItem label="CRO filing pack and signature page generated" done={(filingStatus?.cro.accountsPdfReady ?? false) && (filingStatus?.cro.signaturePageReady ?? false)} />
                </div>
              </Card.Content>
            </Card>

            <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
              <Card.Header>
                <Card.Title className="text-gray-900 dark:text-gray-100">Period Audit Trail</Card.Title>
                <Card.Description>Recent review actions and evidence changes for this period.</Card.Description>
              </Card.Header>
              <Card.Content>
                {auditLog.length === 0 ? (
                  <div className="rounded-lg border border-dashed border-gray-300 dark:border-neutral-700 px-4 py-6 text-sm text-gray-500 dark:text-gray-400">
                    No audit events recorded for this period yet.
                  </div>
                ) : (
                  <div className="overflow-x-auto">
                    <table className="w-full text-sm">
                      <thead>
                        <tr className="border-b border-gray-200 text-left text-xs uppercase text-gray-500 dark:border-neutral-700 dark:text-gray-400">
                          <th className="py-2 pr-4">Time</th>
                          <th className="py-2 pr-4">Action</th>
                          <th className="py-2 pr-4">Record</th>
                          <th className="py-2 pr-4">Reviewer</th>
                        </tr>
                      </thead>
                      <tbody>
                        {auditLog.map((entry) => (
                          <Fragment key={entry.id}>
                            <tr className="border-b border-gray-100 dark:border-neutral-800">
                              <td className="py-2 pr-4 whitespace-nowrap text-gray-600 dark:text-gray-300">
                                {new Date(entry.timestamp).toLocaleString("en-IE", { dateStyle: "medium", timeStyle: "short" })}
                              </td>
                              <td className="py-2 pr-4 font-medium text-gray-900 dark:text-gray-100">{entry.action}</td>
                              <td className="py-2 pr-4 text-gray-600 dark:text-gray-300">{entry.entityType} #{entry.entityId}</td>
                              <td className="py-2 pr-4 text-gray-600 dark:text-gray-300">{entry.userId || "System"}</td>
                            </tr>
                            {(entry.oldValueJson || entry.newValueJson) && (
                              <tr className="border-b border-gray-100 bg-gray-50/60 dark:border-neutral-800 dark:bg-neutral-950/50">
                                <td colSpan={4} className="px-3 py-3">
                                  <div className="space-y-2">
                                    <p className="text-xs font-semibold uppercase text-gray-500 dark:text-gray-400">Audit Details</p>
                                    <div className="grid gap-3 md:grid-cols-2">
                                      {entry.oldValueJson && (
                                        <div>
                                          <p className="mb-1 text-xs font-medium text-gray-600 dark:text-gray-300">Old value</p>
                                          <pre className="max-h-40 overflow-auto whitespace-pre-wrap break-all rounded-md border border-gray-200 bg-white p-3 text-xs text-gray-700 dark:border-neutral-700 dark:bg-neutral-900 dark:text-gray-200">
                                            {formatAuditPayload(entry.oldValueJson)}
                                          </pre>
                                        </div>
                                      )}
                                      {entry.newValueJson && (
                                        <div>
                                          <p className="mb-1 text-xs font-medium text-gray-600 dark:text-gray-300">New value</p>
                                          <pre className="max-h-40 overflow-auto whitespace-pre-wrap break-all rounded-md border border-gray-200 bg-white p-3 text-xs text-gray-700 dark:border-neutral-700 dark:bg-neutral-900 dark:text-gray-200">
                                            {formatAuditPayload(entry.newValueJson)}
                                          </pre>
                                        </div>
                                      )}
                                    </div>
                                  </div>
                                </td>
                              </tr>
                            )}
                          </Fragment>
                        ))}
                      </tbody>
                    </table>
                    {auditTotal > auditLog.length && (
                      <p className="mt-3 text-xs text-gray-500 dark:text-gray-400">
                        Showing latest {auditLog.length} of {auditTotal} audit events.
                      </p>
                    )}
                  </div>
                )}
              </Card.Content>
            </Card>

            <Card className="shadow-sm border border-purple-200 dark:border-purple-800 bg-purple-50/30 dark:bg-purple-900/10">
              <Card.Content className="p-4">
                <div className="flex items-center justify-between">
                  <div className="flex items-center gap-3">
                    <ClipboardList className="w-5 h-5 text-purple-600 dark:text-purple-400" />
                    <div>
                      <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-100">Review Notes</h3>
                      <p className="text-xs text-gray-500 dark:text-gray-400 mt-0.5">Review and finalise notes before filing.</p>
                    </div>
                  </div>
                  <Link href={`/companies/${companyId}/periods/${periodId}/notes`}>
                    <Button variant="outline" size="sm">Review Notes<ArrowRight className="w-4 h-4 ml-1.5" /></Button>
                  </Link>
                </div>
              </Card.Content>
            </Card>
          </div>
        </TabPanel>
      </TabsRoot>
    </div>
  );
}

/* --- Helper Components --- */

function formatAuditPayload(value: string) {
  try {
    return JSON.stringify(JSON.parse(value), null, 2);
  } catch {
    return value;
  }
}

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

function ChecklistItem({ label, done }: { label: string; done: boolean }) {
  return (
    <div className="flex items-center gap-3">
      {done ? (
        <CheckCircle2 className="w-5 h-5 text-emerald-600 dark:text-emerald-400 shrink-0" />
      ) : (
        <div className="w-5 h-5 rounded-full border-2 border-gray-300 dark:border-neutral-600 shrink-0" />
      )}
      <span className={`text-sm ${done ? "text-gray-900 dark:text-gray-100" : "text-gray-500 dark:text-gray-400"}`}>
        {label}
      </span>
    </div>
  );
}
