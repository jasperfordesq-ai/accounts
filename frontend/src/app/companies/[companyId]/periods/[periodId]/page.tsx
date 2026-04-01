"use client";

import { use, useState, useEffect, useCallback } from "react";
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
  generateAdjustments,
  type Company,
  type AccountingPeriod,
  type YearEndSummary,
  type ReadinessScore,
  type BankAccount,
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
  }, [cId, pId]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  async function handleGenerateAdjustments() {
    setGeneratingAdj(true);
    try {
      await generateAdjustments(cId, pId);
      await loadData();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to generate adjustments");
    } finally {
      setGeneratingAdj(false);
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
                <Card.Description>Upload bank statements in CSV or OFX format</Card.Description>
              </Card.Header>
              <Card.Content>
                <div className="border-2 border-dashed border-gray-300 rounded-xl p-10 text-center hover:border-emerald-400 transition-colors">
                  <Upload className="w-10 h-10 text-gray-400 mx-auto mb-3" />
                  <p className="text-sm font-medium text-gray-700">
                    Drag and drop files here, or click to browse
                  </p>
                  <p className="text-xs text-gray-500 mt-1">
                    Supports CSV, OFX, QIF formats
                  </p>
                </div>
              </Card.Content>
            </Card>

            {/* Import Status */}
            <Card className="shadow-sm border border-gray-200">
              <Card.Header>
                <Card.Title>Import Status</Card.Title>
              </Card.Header>
              <Card.Content>
                <div className="flex items-center gap-3 text-sm text-gray-500">
                  <HelpCircle className="w-5 h-5" />
                  <span>No imports have been processed for this period yet.</span>
                </div>
              </Card.Content>
            </Card>
          </div>
        </TabPanel>

        {/* Categorise Tab */}
        <TabPanel id="categorise">
          <div className="space-y-6">
            <Card className="shadow-sm border border-gray-200">
              <Card.Header>
                <Card.Title>Categorisation Overview</Card.Title>
                <Card.Description>Review and categorise imported transactions</Card.Description>
              </Card.Header>
              <Card.Content>
                <div className="grid grid-cols-3 gap-4 mb-6">
                  <div className="rounded-lg bg-gray-50 p-4 text-center">
                    <p className="text-2xl font-bold text-gray-900">0</p>
                    <p className="text-xs text-gray-500 mt-1">Total Transactions</p>
                  </div>
                  <div className="rounded-lg bg-emerald-50 p-4 text-center">
                    <p className="text-2xl font-bold text-emerald-700">0</p>
                    <p className="text-xs text-gray-500 mt-1">Categorised</p>
                  </div>
                  <div className="rounded-lg bg-amber-50 p-4 text-center">
                    <p className="text-2xl font-bold text-amber-700">0</p>
                    <p className="text-xs text-gray-500 mt-1">Uncategorised</p>
                  </div>
                </div>

                <div className="border border-gray-200 rounded-lg overflow-hidden">
                  <table className="w-full text-sm">
                    <thead>
                      <tr className="bg-gray-50 border-b border-gray-200">
                        <th className="text-left px-4 py-2.5 font-medium text-gray-600">Date</th>
                        <th className="text-left px-4 py-2.5 font-medium text-gray-600">Description</th>
                        <th className="text-right px-4 py-2.5 font-medium text-gray-600">Amount</th>
                        <th className="text-left px-4 py-2.5 font-medium text-gray-600">Category</th>
                      </tr>
                    </thead>
                    <tbody>
                      <tr>
                        <td colSpan={4} className="px-4 py-8 text-center text-gray-400 italic">
                          Import transactions to begin categorisation
                        </td>
                      </tr>
                    </tbody>
                  </table>
                </div>
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
                </div>
              </Card.Content>
            </Card>

            <Card className="shadow-sm border border-gray-200">
              <Card.Header>
                <Card.Title>Adjustment Summary</Card.Title>
              </Card.Header>
              <Card.Content>
                <div className="grid grid-cols-4 gap-4">
                  <div className="rounded-lg bg-gray-50 p-4 text-center">
                    <p className="text-2xl font-bold text-gray-900">0</p>
                    <p className="text-xs text-gray-500 mt-1">Total Adjustments</p>
                  </div>
                  <div className="rounded-lg bg-blue-50 p-4 text-center">
                    <p className="text-2xl font-bold text-blue-700">0</p>
                    <p className="text-xs text-gray-500 mt-1">Depreciation</p>
                  </div>
                  <div className="rounded-lg bg-purple-50 p-4 text-center">
                    <p className="text-2xl font-bold text-purple-700">0</p>
                    <p className="text-xs text-gray-500 mt-1">Prepayments</p>
                  </div>
                  <div className="rounded-lg bg-orange-50 p-4 text-center">
                    <p className="text-2xl font-bold text-orange-700">0</p>
                    <p className="text-xs text-gray-500 mt-1">Accruals</p>
                  </div>
                </div>
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
                    done={false}
                  />
                  <ChecklistItem
                    label="Year-end adjustments generated and reviewed"
                    done={false}
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
