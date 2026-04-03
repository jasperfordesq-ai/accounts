"use client";

import { use, useState, useEffect, useCallback, useRef } from "react";
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
  Shield,
} from "lucide-react";
import { toast } from "sonner";
import {
  getCompany,
  getPeriod,
  getYearEndSummary,
  getReadiness,
  getAccountsPackageUrl,
  getIxbrlUrl,
  getAgmPackUrl,
  getCroFilingPackUrl,
  getSignaturePageUrl,
  getBankAccounts,
  getTransactions,
  getAdjustments,
  getAdjustmentSummary,
  generateAdjustments,
  approveAdjustment,
  uploadBankCsv,
  getFilingWorkflowStatus,
  validateIxbrl,
  calculateDeadlines,
  type Company,
  type AccountingPeriod,
  type YearEndSummary,
  type ReadinessScore,
  type BankAccount,
  type ImportedTransaction,
  type Adjustment,
  type AdjustmentSummary,
  type FilingWorkflowStatus,
} from "@/lib/api";
import { Breadcrumbs } from "@/components/Breadcrumbs";
import { PeriodWorkspaceSkeleton } from "@/components/Skeleton";

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
  const [filingStatus, setFilingStatus] = useState<FilingWorkflowStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [generatingAdj, setGeneratingAdj] = useState(false);
  const [validatingIxbrl, setValidatingIxbrl] = useState(false);

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

  // Adjustments tab state
  const [adjustments, setAdjustments] = useState<Adjustment[]>([]);
  const [adjSummary, setAdjSummary] = useState<AdjustmentSummary | null>(null);
  const [loadingAdjustments, setLoadingAdjustments] = useState(false);
  const [approvingId, setApprovingId] = useState<number | null>(null);

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
        const fs = await getFilingWorkflowStatus(cId, pId);
        setFilingStatus(fs);
      } catch {
        // Filing status may not be available yet
      }
      try {
        await calculateDeadlines(cId, pId);
      } catch {
        // Deadline calculation may not be available yet
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load data");
    } finally {
      setLoading(false);
    }
  }, [cId, pId, selectedBankAccountId]);

  const loadTransactions = useCallback(async () => {
    setLoadingTransactions(true);
    try {
      const data = await getTransactions(cId, pId);
      setTransactions(data.items);
      setTransactionTotal(data.total);
    } catch {
      setTransactions([]);
      setTransactionTotal(0);
    } finally {
      setLoadingTransactions(false);
    }
  }, [cId, pId]);

  const loadAdjustments = useCallback(async () => {
    setLoadingAdjustments(true);
    try {
      const [adjData, summaryData] = await Promise.all([
        getAdjustments(cId, pId),
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
  }, [cId, pId]);

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
      await approveAdjustment(cId, pId, id, "Current User");
      toast.success("Adjustment approved");
      await loadAdjustments();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to approve adjustment");
    } finally {
      setApprovingId(null);
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

  const pdfUrl = getAccountsPackageUrl(cId, pId);
  const ixbrlUrl = getIxbrlUrl(cId, pId);
  const agmPackUrl = getAgmPackUrl(cId, pId);
  const croPackUrl = getCroFilingPackUrl(cId, pId);
  const sigPageUrl = getSignaturePageUrl(cId, pId);
  const categorisedCount = transactions.filter((t) => t.categoryId != null).length;
  const uncategorisedCount = transactionTotal - categorisedCount;

  const tabClass = "px-4 py-2.5 text-sm font-medium text-gray-600 dark:text-gray-400 border-b-2 border-transparent data-[selected]:border-emerald-600 data-[selected]:text-emerald-700 dark:data-[selected]:text-emerald-400 cursor-pointer outline-none transition-colors";

  return (
    <div className="animate-fade-in">
      {/* Breadcrumbs */}
      <Breadcrumbs
        items={[
          { label: company?.legalName ?? "Company", href: `/companies/${companyId}` },
          {
            label: period
              ? `${new Date(period.periodStart).toLocaleDateString("en-IE")} \u2013 ${new Date(period.periodEnd).toLocaleDateString("en-IE")}`
              : "Period",
          },
        ]}
      />

      {/* Header */}
      <div className="mb-6">
        <h1 className="text-2xl font-bold text-gray-900 dark:text-gray-100">
          {company?.legalName ?? "Company"}
        </h1>
        {period && (
          <div className="flex items-center gap-3 mt-2">
            <Chip color="accent" variant="soft" size="sm">
              {period.status}
            </Chip>
            <span className="text-sm text-gray-500 dark:text-gray-400">
              {new Date(period.periodStart).toLocaleDateString("en-IE")} &mdash;{" "}
              {new Date(period.periodEnd).toLocaleDateString("en-IE")}
            </span>
            {period.isFirstYear && (
              <Chip color="warning" variant="soft" size="sm">
                First Year
              </Chip>
            )}
          </div>
        )}
      </div>

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
                <Card.Title className="text-gray-900 dark:text-gray-100">Bank Accounts</Card.Title>
                <Card.Description>
                  {bankAccounts.length} bank account{bankAccounts.length !== 1 ? "s" : ""} linked
                </Card.Description>
              </Card.Header>
              <Card.Content>
                {bankAccounts.length === 0 ? (
                  <p className="text-sm text-gray-500 dark:text-gray-400 italic">No bank accounts linked yet.</p>
                ) : (
                  <div className="space-y-2">
                    {bankAccounts.map((ba) => (
                      <div
                        key={ba.id}
                        className="flex items-center justify-between rounded-lg border border-gray-200 dark:border-neutral-700 px-4 py-3 hover:bg-gray-50 dark:hover:bg-neutral-800/50 transition-colors"
                      >
                        <div>
                          <p className="text-sm font-medium text-gray-900 dark:text-gray-100">{ba.name}</p>
                          {ba.iban && <p className="text-xs text-gray-500 dark:text-gray-400">{ba.iban}</p>}
                        </div>
                        <div className="text-right">
                          <p className="text-sm font-medium text-gray-900 dark:text-gray-100">
                            {formatCurrency(ba.openingBalance)}
                          </p>
                          <p className="text-xs text-gray-500 dark:text-gray-400">{ba.currency}</p>
                        </div>
                      </div>
                    ))}
                  </div>
                )}
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
                      <div className="col-span-2">Date</div>
                      <div className="col-span-4">Description</div>
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
                          <div className="col-span-2 text-gray-600 dark:text-gray-400">
                            {new Date(tx.date).toLocaleDateString("en-IE")}
                          </div>
                          <div className="col-span-4 text-gray-900 dark:text-gray-100 truncate" title={tx.description}>
                            {tx.description}
                          </div>
                          <div className={`col-span-2 text-right font-medium font-mono ${tx.amount >= 0 ? "text-emerald-700 dark:text-emerald-400" : "text-red-700 dark:text-red-400"}`}>
                            {formatCurrency(tx.amount)}
                          </div>
                          <div className="col-span-3">
                            {tx.category ? (
                              <Chip color={tx.manualOverride ? "accent" : "success"} variant="soft" size="sm">
                                {tx.category.name}
                              </Chip>
                            ) : (
                              <Chip color="warning" variant="soft" size="sm">
                                Uncategorised
                              </Chip>
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
          </div>
        </TabPanel>

        {/* Filing Tab */}
        <TabPanel id="filing">
          <div className="space-y-6">
            {/* Filing Workflow Status */}
            <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
              <Card.Header>
                <Card.Title className="text-gray-900 dark:text-gray-100">
                  <Shield className="w-4 h-4 inline mr-1.5 -mt-0.5" />
                  Filing Workflow Status
                </Card.Title>
                <Card.Description>CRO and Revenue filing readiness</Card.Description>
              </Card.Header>
              <Card.Content>
                {filingStatus ? (
                  <div className="space-y-4">
                    {/* Ready / Not Ready Banner */}
                    {filingStatus.readyToFile ? (
                      <div className="rounded-lg bg-emerald-50 dark:bg-emerald-900/20 border border-emerald-200 dark:border-emerald-800 px-4 py-3 flex items-center gap-2">
                        <CheckCircle2 className="w-5 h-5 text-emerald-600 dark:text-emerald-400" />
                        <span className="text-sm font-medium text-emerald-700 dark:text-emerald-400">Ready to File</span>
                      </div>
                    ) : (
                      <div className="rounded-lg bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 px-4 py-3 flex items-center gap-2">
                        <AlertTriangle className="w-5 h-5 text-red-500" />
                        <span className="text-sm font-medium text-red-700 dark:text-red-400">Not Ready to File</span>
                      </div>
                    )}

                    {/* Status Badges */}
                    <div className="flex items-center gap-4">
                      <div className="flex items-center gap-2">
                        <span className="text-xs font-medium text-gray-500 dark:text-gray-400 uppercase">CRO:</span>
                        <Chip size="sm" variant="soft" color={
                          filingStatus.cro.status === "Filed" ? "success" :
                          filingStatus.cro.status === "Rejected" ? "danger" :
                          filingStatus.cro.status === "Submitted" ? "accent" : "warning"
                        }>
                          {filingStatus.cro.status}
                        </Chip>
                      </div>
                      <div className="flex items-center gap-2">
                        <span className="text-xs font-medium text-gray-500 dark:text-gray-400 uppercase">Revenue:</span>
                        <Chip size="sm" variant="soft" color={
                          filingStatus.revenue.status === "Filed" ? "success" :
                          filingStatus.revenue.status === "Rejected" ? "danger" :
                          filingStatus.revenue.status === "Submitted" ? "accent" : "warning"
                        }>
                          {filingStatus.revenue.status}
                        </Chip>
                      </div>
                    </div>

                    {/* Blocking Issues */}
                    {filingStatus.blockingIssues.length > 0 && (
                      <div>
                        <h4 className="text-sm font-medium text-red-700 dark:text-red-400 mb-2">Blocking Issues</h4>
                        <ul className="space-y-1.5">
                          {filingStatus.blockingIssues.map((issue, i) => (
                            <li key={i} className="flex items-start gap-2 text-sm text-red-700 dark:text-red-400">
                              <span className="w-1.5 h-1.5 rounded-full bg-red-400 mt-1.5 shrink-0" />{issue}
                            </li>
                          ))}
                        </ul>
                      </div>
                    )}

                    {/* Validate iXBRL Button */}
                    <Button
                      variant="outline"
                      size="sm"
                      isDisabled={validatingIxbrl}
                      onPress={async () => {
                        setValidatingIxbrl(true);
                        try {
                          const result = await validateIxbrl(cId, pId);
                          if (result.ixbrlValid) {
                            toast.success("iXBRL validation passed");
                          } else {
                            toast.error(result.validationErrors || "iXBRL validation failed");
                          }
                          // Refresh filing status
                          try {
                            const fs = await getFilingWorkflowStatus(cId, pId);
                            setFilingStatus(fs);
                          } catch {}
                        } catch (err) {
                          toast.error(err instanceof Error ? err.message : "iXBRL validation failed");
                        } finally {
                          setValidatingIxbrl(false);
                        }
                      }}
                    >
                      {validatingIxbrl ? (
                        <><Spinner size="sm" className="mr-2" />Validating...</>
                      ) : (
                        <><FileText className="w-4 h-4 mr-1" />Validate iXBRL</>
                      )}
                    </Button>
                  </div>
                ) : (
                  <div className="text-center py-8">
                    <Shield className="w-10 h-10 text-gray-300 dark:text-gray-600 mx-auto mb-3" />
                    <p className="text-sm text-gray-500 dark:text-gray-400">Filing status is not available yet.</p>
                    <p className="text-xs text-gray-400 dark:text-gray-500 mt-1">Complete the statements and generate documents first.</p>
                  </div>
                )}
              </Card.Content>
            </Card>

            <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
              <Card.Header>
                <Card.Title className="text-gray-900 dark:text-gray-100">Download Documents</Card.Title>
                <Card.Description>Download the final accounts package and iXBRL filing documents</Card.Description>
              </Card.Header>
              <Card.Content>
                <div className="grid grid-cols-2 gap-4">
                  <a href={agmPackUrl} target="_blank" rel="noopener noreferrer" className="flex flex-col items-center gap-3 rounded-xl border-2 border-gray-200 dark:border-neutral-700 p-8 hover:border-emerald-400 dark:hover:border-emerald-600 hover:bg-emerald-50/30 dark:hover:bg-emerald-900/10 transition-all group">
                    <FileText className="w-12 h-12 text-gray-400 dark:text-gray-500 group-hover:text-emerald-600 dark:group-hover:text-emerald-400 transition-colors" />
                    <div className="text-center">
                      <p className="text-sm font-semibold text-gray-900 dark:text-gray-100">AGM Pack</p>
                      <p className="text-xs text-gray-500 dark:text-gray-400 mt-0.5">Full statutory accounts for AGM approval</p>
                    </div>
                    <Button variant="outline" size="sm"><Download className="w-4 h-4 mr-1" />Download PDF</Button>
                  </a>
                  <a href={croPackUrl} target="_blank" rel="noopener noreferrer" className="flex flex-col items-center gap-3 rounded-xl border-2 border-gray-200 dark:border-neutral-700 p-8 hover:border-emerald-400 dark:hover:border-emerald-600 hover:bg-emerald-50/30 dark:hover:bg-emerald-900/10 transition-all group">
                    <FileText className="w-12 h-12 text-gray-400 dark:text-gray-500 group-hover:text-emerald-600 dark:group-hover:text-emerald-400 transition-colors" />
                    <div className="text-center">
                      <p className="text-sm font-semibold text-gray-900 dark:text-gray-100">CRO Filing Pack</p>
                      <p className="text-xs text-gray-500 dark:text-gray-400 mt-0.5">Abridged accounts for CRO filing</p>
                    </div>
                    <Button variant="outline" size="sm"><Download className="w-4 h-4 mr-1" />Download PDF</Button>
                  </a>
                  <a href={sigPageUrl} target="_blank" rel="noopener noreferrer" className="flex flex-col items-center gap-3 rounded-xl border-2 border-gray-200 dark:border-neutral-700 p-8 hover:border-emerald-400 dark:hover:border-emerald-600 hover:bg-emerald-50/30 dark:hover:bg-emerald-900/10 transition-all group">
                    <FileText className="w-12 h-12 text-gray-400 dark:text-gray-500 group-hover:text-emerald-600 dark:group-hover:text-emerald-400 transition-colors" />
                    <div className="text-center">
                      <p className="text-sm font-semibold text-gray-900 dark:text-gray-100">Signature Page</p>
                      <p className="text-xs text-gray-500 dark:text-gray-400 mt-0.5">Typeset signatures for CRO (s.347)</p>
                    </div>
                    <Button variant="outline" size="sm"><Download className="w-4 h-4 mr-1" />Download PDF</Button>
                  </a>
                  <a href={ixbrlUrl} target="_blank" rel="noopener noreferrer" className="flex flex-col items-center gap-3 rounded-xl border-2 border-gray-200 dark:border-neutral-700 p-8 hover:border-emerald-400 dark:hover:border-emerald-600 hover:bg-emerald-50/30 dark:hover:bg-emerald-900/10 transition-all group">
                    <FileText className="w-12 h-12 text-gray-400 dark:text-gray-500 group-hover:text-emerald-600 dark:group-hover:text-emerald-400 transition-colors" />
                    <div className="text-center">
                      <p className="text-sm font-semibold text-gray-900 dark:text-gray-100">iXBRL Filing</p>
                      <p className="text-xs text-gray-500 dark:text-gray-400 mt-0.5">For Revenue Online Service (ROS) submission</p>
                    </div>
                    <Button variant="outline" size="sm"><Download className="w-4 h-4 mr-1" />Download iXBRL</Button>
                  </a>
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
                  <ChecklistItem label="Accounts package downloaded and reviewed" done={false} />
                  <ChecklistItem label="CRO filing pack and signature page generated" done={false} />
                </div>
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
