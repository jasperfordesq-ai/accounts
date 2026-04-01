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
} from "lucide-react";
import {
  getCompany,
  getPeriod,
  getYearEndSummary,
  getReadiness,
  getAccountsPackageUrl,
  getIxbrlUrl,
  getBankAccounts,
  getTransactions,
  getAdjustments,
  getAdjustmentSummary,
  generateAdjustments,
  approveAdjustment,
  uploadBankCsv,
  type Company,
  type AccountingPeriod,
  type YearEndSummary,
  type ReadinessScore,
  type BankAccount,
  type ImportedTransaction,
  type Adjustment,
  type AdjustmentSummary,
} from "@/lib/api";

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
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [generatingAdj, setGeneratingAdj] = useState(false);

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

      // Auto-select first bank account if available
      if (bankData.length > 0 && selectedBankAccountId === "") {
        setSelectedBankAccountId(bankData[0].id);
      }

      // Load year-end and readiness separately (may not exist yet)
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
      // Transactions may not exist yet
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
      // Adjustments may not exist yet
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
      setUploadError("Please select a bank account before uploading.");
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
      // Reload transactions after import
      await loadTransactions();
    } catch (err) {
      setUploadError(err instanceof Error ? err.message : "Upload failed");
    } finally {
      setUploading(false);
    }
  }

  function handleFileInputChange(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (file) {
      handleFileUpload(file);
    }
    // Reset the input so the same file can be re-uploaded
    e.target.value = "";
  }

  function handleDropzoneDrop(e: React.DragEvent<HTMLDivElement>) {
    e.preventDefault();
    e.stopPropagation();
    const file = e.dataTransfer.files?.[0];
    if (file) {
      handleFileUpload(file);
    }
  }

  function handleDropzoneDragOver(e: React.DragEvent<HTMLDivElement>) {
    e.preventDefault();
    e.stopPropagation();
  }

  async function handleGenerateAdjustments() {
    setGeneratingAdj(true);
    try {
      const summary = await generateAdjustments(cId, pId);
      setAdjSummary(summary);
      // Reload adjustments list
      await loadAdjustments();
      await loadData();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to generate adjustments");
    } finally {
      setGeneratingAdj(false);
    }
  }

  async function handleApproveAdjustment(id: number) {
    setApprovingId(id);
    try {
      await approveAdjustment(cId, pId, id, "Current User");
      await loadAdjustments();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to approve adjustment");
    } finally {
      setApprovingId(null);
    }
  }

  if (loading) {
    return (
      <div className="flex items-center justify-center py-20">
        <Spinner size="lg" />
      </div>
    );
  }

  if (error && !company) {
    return (
      <div className="max-w-2xl mx-auto">
        <Card className="border border-red-200">
          <Card.Content className="text-center py-8">
            <AlertTriangle className="w-10 h-10 text-red-500 mx-auto mb-3" />
            <p className="text-red-700 font-medium">{error}</p>
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

  const categorisedCount = transactions.filter((t) => t.categoryId != null).length;
  const uncategorisedCount = transactionTotal - categorisedCount;

  return (
    <div>
      {/* Header */}
      <div className="mb-6">
        <h1 className="text-2xl font-bold text-gray-900">
          {company?.legalName ?? "Company"}
        </h1>
        {period && (
          <div className="flex items-center gap-3 mt-2">
            <Chip color="accent" variant="soft" size="sm">
              {period.status}
            </Chip>
            <span className="text-sm text-gray-500">
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
        <div className="rounded-lg bg-red-50 border border-red-200 px-4 py-3 text-sm text-red-700 mb-4">
          {error}
        </div>
      )}

      {/* Tabs */}
      <TabsRoot>
        <TabList aria-label="Period workspace tabs" className="flex gap-1 border-b border-gray-200 mb-6">
          <Tab id="import" className="px-4 py-2.5 text-sm font-medium text-gray-600 border-b-2 border-transparent data-[selected]:border-emerald-600 data-[selected]:text-emerald-700 cursor-pointer outline-none">
            <Upload className="w-4 h-4 inline mr-1.5 -mt-0.5" />
            Import
          </Tab>
          <Tab id="categorise" className="px-4 py-2.5 text-sm font-medium text-gray-600 border-b-2 border-transparent data-[selected]:border-emerald-600 data-[selected]:text-emerald-700 cursor-pointer outline-none">
            <Settings className="w-4 h-4 inline mr-1.5 -mt-0.5" />
            Categorise
          </Tab>
          <Tab id="yearend" className="px-4 py-2.5 text-sm font-medium text-gray-600 border-b-2 border-transparent data-[selected]:border-emerald-600 data-[selected]:text-emerald-700 cursor-pointer outline-none">
            <BarChart3 className="w-4 h-4 inline mr-1.5 -mt-0.5" />
            Year-End
          </Tab>
          <Tab id="adjustments" className="px-4 py-2.5 text-sm font-medium text-gray-600 border-b-2 border-transparent data-[selected]:border-emerald-600 data-[selected]:text-emerald-700 cursor-pointer outline-none">
            <Calculator className="w-4 h-4 inline mr-1.5 -mt-0.5" />
            Adjustments
          </Tab>
          <Tab id="statements" className="px-4 py-2.5 text-sm font-medium text-gray-600 border-b-2 border-transparent data-[selected]:border-emerald-600 data-[selected]:text-emerald-700 cursor-pointer outline-none">
            <FileText className="w-4 h-4 inline mr-1.5 -mt-0.5" />
            Statements
          </Tab>
          <Tab id="filing" className="px-4 py-2.5 text-sm font-medium text-gray-600 border-b-2 border-transparent data-[selected]:border-emerald-600 data-[selected]:text-emerald-700 cursor-pointer outline-none">
            <Download className="w-4 h-4 inline mr-1.5 -mt-0.5" />
            Filing
          </Tab>
        </TabList>

        {/* Import Tab */}
        <TabPanel id="import">
          <div className="space-y-6">
            {/* Bank Accounts */}
            <Card className="shadow-sm border border-gray-200">
              <Card.Header>
                <Card.Title>Bank Accounts</Card.Title>
                <Card.Description>
                  {bankAccounts.length} bank account{bankAccounts.length !== 1 ? "s" : ""} linked
                </Card.Description>
              </Card.Header>
              <Card.Content>
                {bankAccounts.length === 0 ? (
                  <p className="text-sm text-gray-500 italic">No bank accounts linked yet.</p>
                ) : (
                  <div className="space-y-2">
                    {bankAccounts.map((ba) => (
                      <div
                        key={ba.id}
                        className="flex items-center justify-between rounded-lg border border-gray-200 px-4 py-3"
                      >
                        <div>
                          <p className="text-sm font-medium text-gray-900">{ba.name}</p>
                          {ba.iban && (
                            <p className="text-xs text-gray-500">{ba.iban}</p>
                          )}
                        </div>
                        <div className="text-right">
                          <p className="text-sm font-medium text-gray-900">
                            {formatCurrency(ba.openingBalance)}
                          </p>
                          <p className="text-xs text-gray-500">{ba.currency}</p>
                        </div>
                      </div>
                    ))}
                  </div>
                )}
              </Card.Content>
            </Card>

            {/* Upload Area */}
            <Card className="shadow-sm border border-gray-200">
              <Card.Header>
                <Card.Title>Import Transactions</Card.Title>
                <Card.Description>Upload bank statements in CSV format</Card.Description>
              </Card.Header>
              <Card.Content>
                {/* Bank Account Selector */}
                <div className="mb-4">
                  <label htmlFor="bank-account-select" className="block text-sm font-medium text-gray-700 mb-1.5">
                    Import into bank account
                  </label>
                  <select
                    id="bank-account-select"
                    value={selectedBankAccountId}
                    onChange={(e) => setSelectedBankAccountId(e.target.value ? Number(e.target.value) : "")}
                    className="w-full rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 shadow-sm focus:border-emerald-500 focus:outline-none focus:ring-1 focus:ring-emerald-500"
                  >
                    <option value="">Select a bank account...</option>
                    {bankAccounts.map((ba) => (
                      <option key={ba.id} value={ba.id}>
                        {ba.name}{ba.iban ? ` (${ba.iban})` : ""} - {ba.currency}
                      </option>
                    ))}
                  </select>
                </div>

                {/* Hidden file input */}
                <input
                  ref={fileInputRef}
                  type="file"
                  accept=".csv"
                  className="hidden"
                  onChange={handleFileInputChange}
                />

                {/* Dropzone */}
                <div
                  className="border-2 border-dashed border-gray-300 rounded-xl p-10 text-center hover:border-emerald-400 transition-colors cursor-pointer"
                  onClick={() => fileInputRef.current?.click()}
                  onDrop={handleDropzoneDrop}
                  onDragOver={handleDropzoneDragOver}
                  role="button"
                  tabIndex={0}
                  onKeyDown={(e) => {
                    if (e.key === "Enter" || e.key === " ") {
                      fileInputRef.current?.click();
                    }
                  }}
                >
                  {uploading ? (
                    <>
                      <Spinner size="sm" className="mx-auto mb-3" />
                      <p className="text-sm font-medium text-gray-700">
                        Uploading and processing...
                      </p>
                    </>
                  ) : (
                    <>
                      <Upload className="w-10 h-10 text-gray-400 mx-auto mb-3" />
                      <p className="text-sm font-medium text-gray-700">
                        Drag and drop a CSV file here, or click to browse
                      </p>
                      <p className="text-xs text-gray-500 mt-1">
                        Supports CSV format
                      </p>
                    </>
                  )}
                </div>

                {/* Upload Error */}
                {uploadError && (
                  <div className="mt-4 rounded-lg bg-red-50 border border-red-200 px-4 py-3 text-sm text-red-700">
                    {uploadError}
                  </div>
                )}
              </Card.Content>
            </Card>

            {/* Import Result / Status */}
            <Card className="shadow-sm border border-gray-200">
              <Card.Header>
                <Card.Title>Import Status</Card.Title>
              </Card.Header>
              <Card.Content>
                {uploadResult ? (
                  <div className="space-y-3">
                    <div className="flex items-center gap-3 text-sm text-emerald-700">
                      <CheckCircle2 className="w-5 h-5 text-emerald-600" />
                      <span className="font-medium">Import completed successfully</span>
                    </div>
                    <div className="grid grid-cols-3 gap-4">
                      <div className="rounded-lg bg-emerald-50 p-4 text-center">
                        <p className="text-2xl font-bold text-emerald-700">{uploadResult.rowsImported}</p>
                        <p className="text-xs text-gray-500 mt-1">Rows Imported</p>
                      </div>
                      <div className="rounded-lg bg-gray-50 p-4 text-center">
                        <p className="text-2xl font-bold text-gray-700">{uploadResult.duplicatesSkipped}</p>
                        <p className="text-xs text-gray-500 mt-1">Duplicates Skipped</p>
                      </div>
                      <div className="rounded-lg bg-blue-50 p-4 text-center">
                        <p className="text-2xl font-bold text-blue-700">{uploadResult.autoCategorised}</p>
                        <p className="text-xs text-gray-500 mt-1">Auto-Categorised</p>
                      </div>
                    </div>
                  </div>
                ) : (
                  <div className="flex items-center gap-3 text-sm text-gray-500">
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
            <Card className="shadow-sm border border-gray-200">
              <Card.Header>
                <div className="flex items-center justify-between w-full">
                  <div>
                    <Card.Title>Categorisation Overview</Card.Title>
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
                  <div className="rounded-lg bg-gray-50 p-4 text-center">
                    <p className="text-2xl font-bold text-gray-900">{transactionTotal}</p>
                    <p className="text-xs text-gray-500 mt-1">Total Transactions</p>
                  </div>
                  <div className="rounded-lg bg-emerald-50 p-4 text-center">
                    <p className="text-2xl font-bold text-emerald-700">{categorisedCount}</p>
                    <p className="text-xs text-gray-500 mt-1">Categorised</p>
                  </div>
                  <div className="rounded-lg bg-amber-50 p-4 text-center">
                    <p className="text-2xl font-bold text-amber-700">{uncategorisedCount}</p>
                    <p className="text-xs text-gray-500 mt-1">Uncategorised</p>
                  </div>
                </div>

                {loadingTransactions ? (
                  <div className="flex items-center justify-center py-8">
                    <Spinner size="sm" />
                  </div>
                ) : transactions.length === 0 ? (
                  <Card className="border border-gray-100">
                    <Card.Content className="text-center py-8">
                      <p className="text-sm text-gray-400 italic">
                        Import transactions to begin categorisation
                      </p>
                    </Card.Content>
                  </Card>
                ) : (
                  <div className="border border-gray-200 rounded-lg overflow-hidden">
                    {/* Header row */}
                    <div className="grid grid-cols-12 gap-2 bg-gray-50 border-b border-gray-200 px-4 py-2.5 text-xs font-medium text-gray-600 uppercase tracking-wide">
                      <div className="col-span-2">Date</div>
                      <div className="col-span-4">Description</div>
                      <div className="col-span-2 text-right">Amount</div>
                      <div className="col-span-3">Category</div>
                      <div className="col-span-1 text-center">Conf.</div>
                    </div>
                    {/* Transaction rows */}
                    <div className="divide-y divide-gray-100">
                      {transactions.map((tx) => (
                        <div
                          key={tx.id}
                          className="grid grid-cols-12 gap-2 px-4 py-3 text-sm hover:bg-gray-50/50 items-center"
                        >
                          <div className="col-span-2 text-gray-600">
                            {new Date(tx.date).toLocaleDateString("en-IE")}
                          </div>
                          <div className="col-span-4 text-gray-900 truncate" title={tx.description}>
                            {tx.description}
                          </div>
                          <div className={`col-span-2 text-right font-medium ${tx.amount >= 0 ? "text-emerald-700" : "text-red-700"}`}>
                            {formatCurrency(tx.amount)}
                          </div>
                          <div className="col-span-3">
                            {tx.category ? (
                              <Chip
                                color={tx.manualOverride ? "accent" : "success"}
                                variant="soft"
                                size="sm"
                              >
                                {tx.category.name}
                              </Chip>
                            ) : (
                              <Chip color="warning" variant="soft" size="sm">
                                Uncategorised
                              </Chip>
                            )}
                          </div>
                          <div className="col-span-1 text-center text-xs text-gray-400">
                            {tx.confidenceScore != null
                              ? `${Math.round(tx.confidenceScore * 100)}%`
                              : "--"}
                          </div>
                        </div>
                      ))}
                    </div>
                    {/* Footer with total */}
                    {transactionTotal > transactions.length && (
                      <div className="bg-gray-50 border-t border-gray-200 px-4 py-2.5 text-xs text-gray-500 text-center">
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
            {/* Completeness Score */}
            {yearEnd && (
              <Card className="shadow-sm border border-gray-200">
                <Card.Header>
                  <Card.Title>Year-End Completeness</Card.Title>
                  <Card.Description>
                    {yearEnd.completeness.completed} of {yearEnd.completeness.total} items completed
                  </Card.Description>
                </Card.Header>
                <Card.Content>
                  <div className="mb-2 flex items-center justify-between text-sm">
                    <span className="text-gray-600">Progress</span>
                    <span className="font-medium text-gray-900">{yearEnd.completeness.score}%</span>
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
                      <p className="text-xs font-medium text-gray-500 mb-2">Incomplete items:</p>
                      <div className="flex flex-wrap gap-1.5">
                        {yearEnd.completeness.incomplete.map((item) => (
                          <Chip key={item} color="warning" variant="soft" size="sm">
                            {item}
                          </Chip>
                        ))}
                      </div>
                    </div>
                  )}
                </Card.Content>
              </Card>
            )}

            {/* Summary Cards */}
            <div className="grid grid-cols-2 gap-4">
              <SummaryCard
                title="Debtors"
                value={yearEnd ? formatCurrency(yearEnd.debtors.total) : "--"}
                subtitle={yearEnd ? `${yearEnd.debtors.count} entries` : "No data"}
              />
              <SummaryCard
                title="Creditors"
                value={yearEnd ? formatCurrency(yearEnd.creditors.total) : "--"}
                subtitle={yearEnd ? `${yearEnd.creditors.count} entries` : "No data"}
              />
              <SummaryCard
                title="Fixed Assets"
                value={yearEnd ? formatCurrency(yearEnd.fixedAssets.totalCost) : "--"}
                subtitle={yearEnd ? `${yearEnd.fixedAssets.count} assets` : "No data"}
              />
              <SummaryCard
                title="Inventory"
                value={yearEnd ? formatCurrency(yearEnd.inventory.totalValue) : "--"}
                subtitle={yearEnd ? `${yearEnd.inventory.count} items` : "No data"}
              />
              <SummaryCard
                title="Loans"
                value={yearEnd ? formatCurrency(yearEnd.loans.totalBalance) : "--"}
                subtitle={yearEnd ? `${yearEnd.loans.count} loans` : "No data"}
              />
              <SummaryCard
                title="Payroll"
                value={
                  yearEnd?.payroll
                    ? formatCurrency(yearEnd.payroll.grossWages)
                    : "--"
                }
                subtitle={
                  yearEnd?.payroll
                    ? `${yearEnd.payroll.staffCount} staff`
                    : "Not applicable"
                }
              />
              <SummaryCard
                title="Tax Liabilities"
                value={yearEnd ? formatCurrency(yearEnd.taxes.totalLiability) : "--"}
                subtitle={yearEnd ? `${yearEnd.taxes.count} items` : "No data"}
              />
              <SummaryCard
                title="Dividends"
                value={yearEnd ? formatCurrency(yearEnd.dividends.total) : "--"}
                subtitle={yearEnd ? `${yearEnd.dividends.count} distributions` : "No data"}
              />
            </div>

            {!yearEnd && (
              <Card className="shadow-sm border border-gray-200">
                <Card.Content className="text-center py-8">
                  <BarChart3 className="w-10 h-10 text-gray-300 mx-auto mb-3" />
                  <p className="text-sm text-gray-500">
                    Year-end summary data is not yet available for this period.
                  </p>
                  <p className="text-xs text-gray-400 mt-1">
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
            <Card className="shadow-sm border border-gray-200">
              <Card.Header>
                <Card.Title>Period Adjustments</Card.Title>
                <Card.Description>
                  Generate and review adjustments for depreciation, prepayments, accruals, and other year-end entries
                </Card.Description>
              </Card.Header>
              <Card.Content>
                <div className="flex items-center gap-4">
                  <Button
                    variant="primary"
                    onPress={handleGenerateAdjustments}
                    isDisabled={generatingAdj}
                  >
                    {generatingAdj ? (
                      <>
                        <Spinner size="sm" className="mr-2" />
                        Generating...
                      </>
                    ) : (
                      <>
                        <Calculator className="w-4 h-4 mr-1" />
                        Generate Adjustments
                      </>
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
            <Card className="shadow-sm border border-gray-200">
              <Card.Header>
                <Card.Title>Adjustment Summary</Card.Title>
              </Card.Header>
              <Card.Content>
                {adjSummary ? (
                  <div className="space-y-4">
                    <div className="grid grid-cols-4 gap-4">
                      <div className="rounded-lg bg-blue-50 p-4 text-center">
                        <p className="text-2xl font-bold text-blue-700">{adjSummary.autoGenerated}</p>
                        <p className="text-xs text-gray-500 mt-1">Auto-Generated</p>
                      </div>
                      <div className="rounded-lg bg-purple-50 p-4 text-center">
                        <p className="text-2xl font-bold text-purple-700">{adjSummary.manual}</p>
                        <p className="text-xs text-gray-500 mt-1">Manual</p>
                      </div>
                      <div className="rounded-lg bg-amber-50 p-4 text-center">
                        <p className="text-2xl font-bold text-amber-700">{adjSummary.pendingApproval}</p>
                        <p className="text-xs text-gray-500 mt-1">Pending Approval</p>
                      </div>
                      <div className="rounded-lg bg-emerald-50 p-4 text-center">
                        <p className="text-2xl font-bold text-emerald-700">{adjSummary.approved}</p>
                        <p className="text-xs text-gray-500 mt-1">Approved</p>
                      </div>
                    </div>
                    <div className="grid grid-cols-2 gap-4">
                      <div className="rounded-lg bg-gray-50 p-4">
                        <p className="text-xs text-gray-500">Total Impact on Profit</p>
                        <p className={`text-lg font-bold mt-1 ${adjSummary.totalImpactOnProfit >= 0 ? "text-emerald-700" : "text-red-700"}`}>
                          {formatCurrency(adjSummary.totalImpactOnProfit)}
                        </p>
                      </div>
                      <div className="rounded-lg bg-gray-50 p-4">
                        <p className="text-xs text-gray-500">Total Impact on Assets</p>
                        <p className={`text-lg font-bold mt-1 ${adjSummary.totalImpactOnAssets >= 0 ? "text-emerald-700" : "text-red-700"}`}>
                          {formatCurrency(adjSummary.totalImpactOnAssets)}
                        </p>
                      </div>
                    </div>
                  </div>
                ) : (
                  <div className="grid grid-cols-4 gap-4">
                    <div className="rounded-lg bg-gray-50 p-4 text-center">
                      <p className="text-2xl font-bold text-gray-900">0</p>
                      <p className="text-xs text-gray-500 mt-1">Auto-Generated</p>
                    </div>
                    <div className="rounded-lg bg-gray-50 p-4 text-center">
                      <p className="text-2xl font-bold text-gray-900">0</p>
                      <p className="text-xs text-gray-500 mt-1">Manual</p>
                    </div>
                    <div className="rounded-lg bg-gray-50 p-4 text-center">
                      <p className="text-2xl font-bold text-gray-900">0</p>
                      <p className="text-xs text-gray-500 mt-1">Pending Approval</p>
                    </div>
                    <div className="rounded-lg bg-gray-50 p-4 text-center">
                      <p className="text-2xl font-bold text-gray-900">0</p>
                      <p className="text-xs text-gray-500 mt-1">Approved</p>
                    </div>
                  </div>
                )}
              </Card.Content>
            </Card>

            {/* Adjustments List */}
            <Card className="shadow-sm border border-gray-200">
              <Card.Header>
                <Card.Title>Adjustment Details</Card.Title>
                <Card.Description>
                  {adjustments.length} adjustment{adjustments.length !== 1 ? "s" : ""} found
                </Card.Description>
              </Card.Header>
              <Card.Content>
                {loadingAdjustments ? (
                  <div className="flex items-center justify-center py-8">
                    <Spinner size="sm" />
                  </div>
                ) : adjustments.length === 0 ? (
                  <div className="text-center py-8">
                    <Calculator className="w-10 h-10 text-gray-300 mx-auto mb-3" />
                    <p className="text-sm text-gray-500">
                      No adjustments yet. Click &ldquo;Generate Adjustments&rdquo; to create them.
                    </p>
                  </div>
                ) : (
                  <div className="space-y-3">
                    {adjustments.map((adj) => (
                      <Card key={adj.id} className="border border-gray-100">
                        <Card.Content className="p-4">
                          <div className="flex items-start justify-between gap-4">
                            <div className="flex-1 min-w-0">
                              <div className="flex items-center gap-2 mb-1">
                                <p className="text-sm font-medium text-gray-900 truncate">
                                  {adj.description}
                                </p>
                                <Chip
                                  color={adj.isAuto ? "default" : "accent"}
                                  variant="soft"
                                  size="sm"
                                >
                                  {adj.isAuto ? "Auto" : "Manual"}
                                </Chip>
                                {adj.approvedBy ? (
                                  <Chip color="success" variant="soft" size="sm">
                                    Approved
                                  </Chip>
                                ) : (
                                  <Chip color="warning" variant="soft" size="sm">
                                    Pending
                                  </Chip>
                                )}
                              </div>
                              {adj.reason && (
                                <p className="text-xs text-gray-500 mb-1">{adj.reason}</p>
                              )}
                              <div className="flex items-center gap-4 text-xs text-gray-500">
                                <span>
                                  Amount: <span className="font-medium text-gray-700">{formatCurrency(adj.amount)}</span>
                                </span>
                                <span>
                                  Profit impact:{" "}
                                  <span className={`font-medium ${adj.impactOnProfit >= 0 ? "text-emerald-700" : "text-red-700"}`}>
                                    {formatCurrency(adj.impactOnProfit)}
                                  </span>
                                </span>
                                <span>
                                  Asset impact:{" "}
                                  <span className={`font-medium ${adj.impactOnAssets >= 0 ? "text-emerald-700" : "text-red-700"}`}>
                                    {formatCurrency(adj.impactOnAssets)}
                                  </span>
                                </span>
                              </div>
                              {adj.approvedBy && (
                                <p className="text-xs text-gray-400 mt-1">
                                  Approved by {adj.approvedBy}
                                  {adj.approvedAt && ` on ${new Date(adj.approvedAt).toLocaleDateString("en-IE")}`}
                                </p>
                              )}
                            </div>
                            <div className="shrink-0">
                              {!adj.approvedBy && (
                                <Button
                                  variant="outline"
                                  size="sm"
                                  onPress={() => handleApproveAdjustment(adj.id)}
                                  isDisabled={approvingId === adj.id}
                                >
                                  {approvingId === adj.id ? (
                                    <Spinner size="sm" />
                                  ) : (
                                    <>
                                      <CheckCircle2 className="w-4 h-4 mr-1" />
                                      Approve
                                    </>
                                  )}
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
            {/* Readiness Score */}
            <Card className="shadow-sm border border-gray-200">
              <Card.Header>
                <Card.Title>Filing Readiness</Card.Title>
                <Card.Description>
                  Assessment of whether the accounts are ready for filing
                </Card.Description>
              </Card.Header>
              <Card.Content>
                {readiness ? (
                  <div className="space-y-6">
                    <div className="grid grid-cols-2 gap-6">
                      <div>
                        <div className="flex items-center justify-between text-sm mb-2">
                          <span className="text-gray-600">Completeness</span>
                          <span className="font-medium text-gray-900">
                            {readiness.completenessPercent}%
                          </span>
                        </div>
                        <ProgressBar
                          value={readiness.completenessPercent}
                          minValue={0}
                          maxValue={100}
                          aria-label="Completeness"
                          color={readiness.completenessPercent >= 80 ? "success" : "warning"}
                        >
                          <ProgressBarTrack>
                            <ProgressBarFill />
                          </ProgressBarTrack>
                        </ProgressBar>
                      </div>
                      <div>
                        <div className="flex items-center justify-between text-sm mb-2">
                          <span className="text-gray-600">Filing Readiness</span>
                          <span className="font-medium text-gray-900">
                            {readiness.filingReadinessPercent}%
                          </span>
                        </div>
                        <ProgressBar
                          value={readiness.filingReadinessPercent}
                          minValue={0}
                          maxValue={100}
                          aria-label="Filing readiness"
                          color={readiness.filingReadinessPercent >= 80 ? "success" : "warning"}
                        >
                          <ProgressBarTrack>
                            <ProgressBarFill />
                          </ProgressBarTrack>
                        </ProgressBar>
                      </div>
                    </div>

                    <div className="flex items-center gap-2 text-sm">
                      {readiness.balanceSheetBalances ? (
                        <>
                          <CheckCircle2 className="w-5 h-5 text-emerald-600" />
                          <span className="text-emerald-700 font-medium">Balance sheet balances</span>
                        </>
                      ) : (
                        <>
                          <AlertTriangle className="w-5 h-5 text-amber-500" />
                          <span className="text-amber-700 font-medium">Balance sheet does not balance</span>
                        </>
                      )}
                    </div>

                    {readiness.missingItems.length > 0 && (
                      <div>
                        <h4 className="text-sm font-medium text-gray-700 mb-2">Missing Items</h4>
                        <ul className="space-y-1.5">
                          {readiness.missingItems.map((item, i) => (
                            <li key={i} className="flex items-start gap-2 text-sm text-red-700">
                              <span className="w-1.5 h-1.5 rounded-full bg-red-400 mt-1.5 shrink-0" />
                              {item}
                            </li>
                          ))}
                        </ul>
                      </div>
                    )}

                    {readiness.warnings.length > 0 && (
                      <div>
                        <h4 className="text-sm font-medium text-gray-700 mb-2">Warnings</h4>
                        <ul className="space-y-1.5">
                          {readiness.warnings.map((warning, i) => (
                            <li key={i} className="flex items-start gap-2 text-sm text-amber-700">
                              <AlertTriangle className="w-4 h-4 text-amber-500 mt-0.5 shrink-0" />
                              {warning}
                            </li>
                          ))}
                        </ul>
                      </div>
                    )}
                  </div>
                ) : (
                  <div className="text-center py-8">
                    <FileText className="w-10 h-10 text-gray-300 mx-auto mb-3" />
                    <p className="text-sm text-gray-500">
                      Readiness data is not available yet.
                    </p>
                    <p className="text-xs text-gray-400 mt-1">
                      Complete the year-end process and generate adjustments first.
                    </p>
                  </div>
                )}
              </Card.Content>
            </Card>
          </div>
        </TabPanel>

        {/* Filing Tab */}
        <TabPanel id="filing">
          <div className="space-y-6">
            <Card className="shadow-sm border border-gray-200">
              <Card.Header>
                <Card.Title>Download Documents</Card.Title>
                <Card.Description>
                  Download the final accounts package and iXBRL filing documents
                </Card.Description>
              </Card.Header>
              <Card.Content>
                <div className="grid grid-cols-2 gap-4">
                  <a
                    href={pdfUrl}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="flex flex-col items-center gap-3 rounded-xl border-2 border-gray-200 p-8 hover:border-emerald-400 hover:bg-emerald-50/30 transition-all group"
                  >
                    <FileText className="w-12 h-12 text-gray-400 group-hover:text-emerald-600 transition-colors" />
                    <div className="text-center">
                      <p className="text-sm font-semibold text-gray-900">Accounts Package</p>
                      <p className="text-xs text-gray-500 mt-0.5">PDF format for printing and review</p>
                    </div>
                    <Button variant="outline" size="sm">
                      <Download className="w-4 h-4 mr-1" />
                      Download PDF
                    </Button>
                  </a>

                  <a
                    href={ixbrlUrl}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="flex flex-col items-center gap-3 rounded-xl border-2 border-gray-200 p-8 hover:border-emerald-400 hover:bg-emerald-50/30 transition-all group"
                  >
                    <FileText className="w-12 h-12 text-gray-400 group-hover:text-emerald-600 transition-colors" />
                    <div className="text-center">
                      <p className="text-sm font-semibold text-gray-900">iXBRL Filing</p>
                      <p className="text-xs text-gray-500 mt-0.5">For Revenue Online Service (ROS) submission</p>
                    </div>
                    <Button variant="outline" size="sm">
                      <Download className="w-4 h-4 mr-1" />
                      Download iXBRL
                    </Button>
                  </a>
                </div>
              </Card.Content>
            </Card>

            <Card className="shadow-sm border border-gray-200">
              <Card.Header>
                <Card.Title>Filing Checklist</Card.Title>
              </Card.Header>
              <Card.Content>
                <div className="space-y-3">
                  <ChecklistItem
                    label="All transactions imported and categorised"
                    done={transactionTotal > 0 && uncategorisedCount === 0}
                  />
                  <ChecklistItem
                    label="Year-end adjustments generated and reviewed"
                    done={adjSummary != null && adjSummary.pendingApproval === 0 && (adjSummary.autoGenerated + adjSummary.manual) > 0}
                  />
                  <ChecklistItem
                    label="Balance sheet balances"
                    done={readiness?.balanceSheetBalances ?? false}
                  />
                  <ChecklistItem
                    label="Filing readiness at 100%"
                    done={(readiness?.filingReadinessPercent ?? 0) >= 100}
                  />
                  <ChecklistItem
                    label="Accounts package downloaded and reviewed"
                    done={false}
                  />
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

function SummaryCard({
  title,
  value,
  subtitle,
}: {
  title: string;
  value: string;
  subtitle: string;
}) {
  return (
    <Card className="shadow-sm border border-gray-200">
      <Card.Content className="p-5">
        <p className="text-xs font-medium text-gray-500 uppercase tracking-wide">{title}</p>
        <p className="text-xl font-bold text-gray-900 mt-1">{value}</p>
        <p className="text-xs text-gray-500 mt-0.5">{subtitle}</p>
      </Card.Content>
    </Card>
  );
}

function ChecklistItem({ label, done }: { label: string; done: boolean }) {
  return (
    <div className="flex items-center gap-3">
      {done ? (
        <CheckCircle2 className="w-5 h-5 text-emerald-600 shrink-0" />
      ) : (
        <div className="w-5 h-5 rounded-full border-2 border-gray-300 shrink-0" />
      )}
      <span className={`text-sm ${done ? "text-gray-900" : "text-gray-500"}`}>{label}</span>
    </div>
  );
}
