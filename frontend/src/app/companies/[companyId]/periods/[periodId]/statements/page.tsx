"use client";

import { use, useState, useEffect, useCallback } from "react";
import Link from "next/link";
import {
  Button,
  Card,
  Chip,
  Spinner,
  TabsRoot,
  TabList,
  Tab,
  TabPanel,
} from "@heroui/react";
import {
  ArrowLeft,
  RefreshCw,
  AlertTriangle,
  CheckCircle2,
  FileText,
  Calculator,
  BarChart3,
  Scale,
} from "lucide-react";
import {
  getCompany,
  getPeriod,
  getTrialBalance,
  getProfitAndLoss,
  getBalanceSheet,
  getTaxComputation,
  type Company,
  type AccountingPeriod,
  type TrialBalanceLine,
  type ProfitAndLoss,
  type BalanceSheet,
  type TaxComputation,
} from "@/lib/api";

/** Format a number as EUR accounting style: negative in parentheses */
function eur(amount: number): string {
  const abs = Math.abs(amount);
  const formatted = new Intl.NumberFormat("en-IE", {
    style: "currency",
    currency: "EUR",
    minimumFractionDigits: 2,
  }).format(abs);
  return amount < 0 ? `(${formatted})` : formatted;
}

export default function StatementsPage({
  params,
}: {
  params: Promise<{ companyId: string; periodId: string }>;
}) {
  const { companyId, periodId } = use(params);
  const cId = Number(companyId);
  const pId = Number(periodId);

  const [company, setCompany] = useState<Company | null>(null);
  const [period, setPeriod] = useState<AccountingPeriod | null>(null);
  const [trialBalance, setTrialBalance] = useState<TrialBalanceLine[] | null>(null);
  const [pnl, setPnl] = useState<ProfitAndLoss | null>(null);
  const [bs, setBs] = useState<BalanceSheet | null>(null);
  const [tax, setTax] = useState<TaxComputation | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const loadData = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [companyData, periodData] = await Promise.all([
        getCompany(cId),
        getPeriod(cId, pId),
      ]);
      setCompany(companyData);
      setPeriod(periodData);

      // Load statements in parallel - each may fail independently
      const results = await Promise.allSettled([
        getTrialBalance(cId, pId),
        getProfitAndLoss(cId, pId),
        getBalanceSheet(cId, pId),
        getTaxComputation(cId, pId),
      ]);

      if (results[0].status === "fulfilled") setTrialBalance(results[0].value);
      if (results[1].status === "fulfilled") setPnl(results[1].value);
      if (results[2].status === "fulfilled") setBs(results[2].value);
      if (results[3].status === "fulfilled") setTax(results[3].value);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load data");
    } finally {
      setLoading(false);
    }
  }, [cId, pId]);

  useEffect(() => {
    loadData();
  }, [loadData]);

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

  return (
    <div>
      {/* Header */}
      <div className="mb-6">
        <Link
          href={`/companies/${companyId}/periods/${periodId}`}
          className="inline-flex items-center gap-1.5 text-sm text-gray-500 hover:text-emerald-600 mb-3"
        >
          <ArrowLeft className="w-4 h-4" />
          Back to Period Workspace
        </Link>
        <h1 className="text-2xl font-bold text-gray-900">
          Financial Statements
        </h1>
        {company && period && (
          <p className="text-sm text-gray-500 mt-1">
            {company.legalName} &mdash;{" "}
            {new Date(period.periodStart).toLocaleDateString("en-IE")} to{" "}
            {new Date(period.periodEnd).toLocaleDateString("en-IE")}
          </p>
        )}
      </div>

      {/* Tabs */}
      <TabsRoot>
        <TabList
          aria-label="Financial statements tabs"
          className="flex gap-1 border-b border-gray-200 mb-6"
        >
          <Tab
            id="trial-balance"
            className="px-4 py-2.5 text-sm font-medium text-gray-600 border-b-2 border-transparent data-[selected]:border-emerald-600 data-[selected]:text-emerald-700 cursor-pointer outline-none"
          >
            <Scale className="w-4 h-4 inline mr-1.5 -mt-0.5" />
            Trial Balance
          </Tab>
          <Tab
            id="pnl"
            className="px-4 py-2.5 text-sm font-medium text-gray-600 border-b-2 border-transparent data-[selected]:border-emerald-600 data-[selected]:text-emerald-700 cursor-pointer outline-none"
          >
            <BarChart3 className="w-4 h-4 inline mr-1.5 -mt-0.5" />
            Profit &amp; Loss
          </Tab>
          <Tab
            id="balance-sheet"
            className="px-4 py-2.5 text-sm font-medium text-gray-600 border-b-2 border-transparent data-[selected]:border-emerald-600 data-[selected]:text-emerald-700 cursor-pointer outline-none"
          >
            <FileText className="w-4 h-4 inline mr-1.5 -mt-0.5" />
            Balance Sheet
          </Tab>
          <Tab
            id="tax-computation"
            className="px-4 py-2.5 text-sm font-medium text-gray-600 border-b-2 border-transparent data-[selected]:border-emerald-600 data-[selected]:text-emerald-700 cursor-pointer outline-none"
          >
            <Calculator className="w-4 h-4 inline mr-1.5 -mt-0.5" />
            Tax Computation
          </Tab>
        </TabList>

        {/* Trial Balance Tab */}
        <TabPanel id="trial-balance">
          <Card className="shadow-sm border border-gray-200">
            <Card.Header>
              <Card.Title>Trial Balance</Card.Title>
              <Card.Description>
                All account balances as at period end
              </Card.Description>
            </Card.Header>
            <Card.Content>
              {trialBalance ? (
                <div className="border border-gray-200 rounded-lg overflow-hidden">
                  {/* Header */}
                  <div className="grid grid-cols-12 gap-2 bg-gray-50 border-b border-gray-200 px-4 py-2.5 text-xs font-medium text-gray-600 uppercase tracking-wide">
                    <div className="col-span-2">Code</div>
                    <div className="col-span-4">Account Name</div>
                    <div className="col-span-2">Type</div>
                    <div className="col-span-2 text-right">Debit</div>
                    <div className="col-span-2 text-right">Credit</div>
                  </div>
                  {/* Rows */}
                  <div className="divide-y divide-gray-100">
                    {trialBalance.map((line, idx) => (
                      <div
                        key={`${line.code}-${idx}`}
                        className="grid grid-cols-12 gap-2 px-4 py-2.5 text-sm hover:bg-gray-50/50 items-center"
                      >
                        <div className="col-span-2 text-gray-600 font-mono text-xs">
                          {line.code}
                        </div>
                        <div className="col-span-4 text-gray-900">
                          {line.name}
                        </div>
                        <div className="col-span-2">
                          <Chip size="sm" variant="soft" color="default">
                            {line.type}
                          </Chip>
                        </div>
                        <div className="col-span-2 text-right font-mono text-gray-900">
                          {line.debit > 0 ? eur(line.debit) : ""}
                        </div>
                        <div className="col-span-2 text-right font-mono text-gray-900">
                          {line.credit > 0 ? eur(line.credit) : ""}
                        </div>
                      </div>
                    ))}
                  </div>
                  {/* Totals */}
                  <div className="grid grid-cols-12 gap-2 bg-gray-100 border-t-2 border-gray-300 px-4 py-3 text-sm font-bold">
                    <div className="col-span-8 text-gray-900">Totals</div>
                    <div className="col-span-2 text-right font-mono text-gray-900">
                      {eur(
                        trialBalance.reduce((sum, l) => sum + l.debit, 0)
                      )}
                    </div>
                    <div className="col-span-2 text-right font-mono text-gray-900">
                      {eur(
                        trialBalance.reduce((sum, l) => sum + l.credit, 0)
                      )}
                    </div>
                  </div>
                  {/* Balance check */}
                  {(() => {
                    const totalDebit = trialBalance.reduce(
                      (sum, l) => sum + l.debit,
                      0
                    );
                    const totalCredit = trialBalance.reduce(
                      (sum, l) => sum + l.credit,
                      0
                    );
                    const diff = Math.abs(totalDebit - totalCredit);
                    return (
                      <div className="px-4 py-2.5 bg-gray-50 border-t border-gray-200">
                        {diff < 0.01 ? (
                          <div className="flex items-center gap-2 text-sm text-emerald-700">
                            <CheckCircle2 className="w-4 h-4" />
                            Trial balance agrees - debits equal credits
                          </div>
                        ) : (
                          <div className="flex items-center gap-2 text-sm text-red-700">
                            <AlertTriangle className="w-4 h-4" />
                            Trial balance does not agree - difference of{" "}
                            {eur(diff)}
                          </div>
                        )}
                      </div>
                    );
                  })()}
                </div>
              ) : (
                <EmptyState message="Trial balance data is not available yet." />
              )}
            </Card.Content>
          </Card>
        </TabPanel>

        {/* P&L Tab */}
        <TabPanel id="pnl">
          <Card className="shadow-sm border border-gray-200">
            <Card.Header>
              <Card.Title>Profit &amp; Loss Account</Card.Title>
              <Card.Description>
                Income statement for the period
              </Card.Description>
            </Card.Header>
            <Card.Content>
              {pnl ? (
                <div className="max-w-lg mx-auto space-y-1">
                  <StatementRow label="Turnover" amount={pnl.turnover} bold />
                  <StatementRow
                    label="Less: Cost of Sales"
                    amount={-pnl.costOfSales}
                  />
                  <Divider />
                  <StatementRow
                    label="Gross Profit"
                    amount={pnl.grossProfit}
                    bold
                    highlight
                  />
                  <div className="pt-2">
                    <p className="text-xs font-medium text-gray-500 uppercase tracking-wide mb-1">
                      Overheads
                    </p>
                    {pnl.overheads.map((oh) => (
                      <StatementRow
                        key={oh.code}
                        label={`  ${oh.name}`}
                        amount={-oh.amount}
                        indent
                      />
                    ))}
                  </div>
                  <StatementRow
                    label="Total Overheads"
                    amount={-pnl.totalOverheads}
                    bold
                  />
                  <Divider />
                  <StatementRow
                    label="Operating Profit"
                    amount={pnl.operatingProfit}
                    bold
                    highlight
                  />
                  <StatementRow
                    label="Less: Interest Payable"
                    amount={-pnl.interestPayable}
                  />
                  <Divider />
                  <StatementRow
                    label="Profit Before Tax"
                    amount={pnl.profitBeforeTax}
                    bold
                    highlight
                  />
                  <StatementRow
                    label="Less: Corporation Tax"
                    amount={-pnl.taxCharge}
                  />
                  <Divider double />
                  <StatementRow
                    label="Profit After Tax"
                    amount={pnl.profitAfterTax}
                    bold
                    highlight
                  />
                </div>
              ) : (
                <EmptyState message="Profit and loss data is not available yet." />
              )}
            </Card.Content>
          </Card>
        </TabPanel>

        {/* Balance Sheet Tab */}
        <TabPanel id="balance-sheet">
          <Card className="shadow-sm border border-gray-200">
            <Card.Header>
              <Card.Title>Balance Sheet</Card.Title>
              <Card.Description>
                Statement of financial position as at period end
              </Card.Description>
            </Card.Header>
            <Card.Content>
              {bs ? (
                <div className="max-w-lg mx-auto space-y-1">
                  {/* Fixed Assets */}
                  <p className="text-xs font-medium text-gray-500 uppercase tracking-wide pb-1">
                    Fixed Assets
                  </p>
                  {bs.fixedAssets.categories.length > 0 && (
                    <div className="border border-gray-200 rounded-lg overflow-hidden mb-2">
                      <div className="grid grid-cols-4 gap-2 bg-gray-50 px-3 py-1.5 text-xs font-medium text-gray-500">
                        <div>Category</div>
                        <div className="text-right">Cost</div>
                        <div className="text-right">Depn</div>
                        <div className="text-right">NBV</div>
                      </div>
                      {bs.fixedAssets.categories.map((cat) => (
                        <div
                          key={cat.category}
                          className="grid grid-cols-4 gap-2 px-3 py-1.5 text-sm border-t border-gray-100"
                        >
                          <div className="text-gray-700">{cat.category}</div>
                          <div className="text-right font-mono text-gray-900">
                            {eur(cat.cost)}
                          </div>
                          <div className="text-right font-mono text-gray-900">
                            {eur(cat.depreciation)}
                          </div>
                          <div className="text-right font-mono text-gray-900">
                            {eur(cat.nbv)}
                          </div>
                        </div>
                      ))}
                    </div>
                  )}
                  <StatementRow
                    label="Total Fixed Assets"
                    amount={bs.fixedAssets.total}
                    bold
                  />

                  {/* Current Assets */}
                  <div className="pt-3">
                    <p className="text-xs font-medium text-gray-500 uppercase tracking-wide pb-1">
                      Current Assets
                    </p>
                    <StatementRow label="Stock" amount={bs.currentAssets.stock} indent />
                    <StatementRow
                      label="Debtors"
                      amount={bs.currentAssets.debtors}
                      indent
                    />
                    <StatementRow
                      label="Prepayments"
                      amount={bs.currentAssets.prepayments}
                      indent
                    />
                    <StatementRow label="Cash at Bank" amount={bs.currentAssets.cash} indent />
                    <Divider />
                    <StatementRow
                      label="Total Current Assets"
                      amount={bs.currentAssets.total}
                      bold
                    />
                  </div>

                  {/* Creditors: amounts falling due within one year */}
                  <div className="pt-3">
                    <p className="text-xs font-medium text-gray-500 uppercase tracking-wide pb-1">
                      Creditors: amounts falling due within one year
                    </p>
                    <StatementRow
                      label="Trade Creditors"
                      amount={bs.creditorsWithinYear.tradeCreditors}
                      indent
                    />
                    <StatementRow
                      label="Accruals"
                      amount={bs.creditorsWithinYear.accruals}
                      indent
                    />
                    <StatementRow
                      label="Tax Creditors"
                      amount={bs.creditorsWithinYear.taxCreditors}
                      indent
                    />
                    <StatementRow
                      label="Other Creditors"
                      amount={bs.creditorsWithinYear.otherCreditors}
                      indent
                    />
                    <Divider />
                    <StatementRow
                      label="Total Creditors (within year)"
                      amount={-bs.creditorsWithinYear.total}
                      bold
                    />
                  </div>

                  <Divider />
                  <StatementRow
                    label="Net Current Assets"
                    amount={bs.netCurrentAssets}
                    bold
                    highlight
                  />

                  <Divider />
                  <StatementRow
                    label="Total Assets Less Current Liabilities"
                    amount={bs.totalAssetsLessCurrentLiabilities}
                    bold
                  />

                  {/* Creditors after year */}
                  <div className="pt-3">
                    <p className="text-xs font-medium text-gray-500 uppercase tracking-wide pb-1">
                      Creditors: amounts falling due after more than one year
                    </p>
                    <StatementRow label="Loans" amount={bs.creditorsAfterYear.loans} indent />
                    <StatementRow label="Other" amount={bs.creditorsAfterYear.other} indent />
                    <StatementRow
                      label="Total Creditors (after year)"
                      amount={-bs.creditorsAfterYear.total}
                      bold
                    />
                  </div>

                  <Divider double />
                  <StatementRow
                    label="Net Assets"
                    amount={bs.netAssets}
                    bold
                    highlight
                  />

                  {/* Capital and Reserves */}
                  <div className="pt-3">
                    <p className="text-xs font-medium text-gray-500 uppercase tracking-wide pb-1">
                      Capital and Reserves
                    </p>
                    <StatementRow
                      label="Share Capital"
                      amount={bs.capitalAndReserves.shareCapital}
                      indent
                    />
                    <StatementRow
                      label="Retained Earnings"
                      amount={bs.capitalAndReserves.retainedEarnings}
                      indent
                    />
                    <Divider double />
                    <StatementRow
                      label="Total Capital and Reserves"
                      amount={bs.capitalAndReserves.total}
                      bold
                      highlight
                    />
                  </div>

                  {/* Balance check */}
                  <div className="mt-4 pt-3 border-t border-gray-200">
                    {bs.balances ? (
                      <div className="flex items-center gap-2 text-sm text-emerald-700">
                        <CheckCircle2 className="w-5 h-5" />
                        <span className="font-medium">
                          Balance sheet balances
                        </span>
                      </div>
                    ) : (
                      <div className="flex items-center gap-2 text-sm text-red-700">
                        <AlertTriangle className="w-5 h-5" />
                        <span className="font-medium">
                          Balance sheet does not balance
                        </span>
                      </div>
                    )}
                  </div>
                </div>
              ) : (
                <EmptyState message="Balance sheet data is not available yet." />
              )}
            </Card.Content>
          </Card>
        </TabPanel>

        {/* Tax Computation Tab */}
        <TabPanel id="tax-computation">
          <Card className="shadow-sm border border-gray-200">
            <Card.Header>
              <Card.Title>Corporation Tax Computation</Card.Title>
              <Card.Description>
                Irish corporation tax calculation for the period
              </Card.Description>
            </Card.Header>
            <Card.Content>
              {tax ? (
                <div className="max-w-lg mx-auto space-y-1">
                  <StatementRow
                    label="Accounting Profit"
                    amount={tax.accountingProfit}
                    bold
                  />

                  {tax.adjustments.length > 0 && (
                    <div className="pt-2">
                      <p className="text-xs font-medium text-gray-500 uppercase tracking-wide mb-1">
                        Tax Adjustments
                      </p>
                      {tax.adjustments.map((adj, idx) => (
                        <div key={idx} className="flex justify-between items-start py-1 pl-4">
                          <div className="flex-1">
                            <span className="text-sm text-gray-700">
                              {adj.description}
                            </span>
                            {adj.basis && (
                              <span className="text-xs text-gray-400 ml-2">
                                ({adj.basis})
                              </span>
                            )}
                          </div>
                          <span className="text-sm font-mono text-gray-900 ml-4">
                            {eur(adj.amount)}
                          </span>
                        </div>
                      ))}
                    </div>
                  )}

                  <Divider />
                  <StatementRow
                    label="Taxable Profit"
                    amount={tax.taxableProfit}
                    bold
                    highlight
                  />

                  <div className="pt-2">
                    <p className="text-xs font-medium text-gray-500 uppercase tracking-wide mb-1">
                      Corporation Tax
                    </p>
                    <StatementRow
                      label="Tax at 12.5% (trading)"
                      amount={tax.corporationTaxAt125}
                      indent
                    />
                    <StatementRow
                      label="Tax at 25% (non-trading)"
                      amount={tax.corporationTaxAt25}
                      indent
                    />
                  </div>
                  <Divider />
                  <StatementRow
                    label="Total Corporation Tax"
                    amount={tax.totalCorporationTax}
                    bold
                  />
                  <StatementRow
                    label="Less: Preliminary Tax Paid"
                    amount={-tax.preliminaryTaxPaid}
                  />
                  <Divider double />
                  <StatementRow
                    label="Balance Due / (Refundable)"
                    amount={tax.balanceDue}
                    bold
                    highlight
                  />

                  {tax.notes && (
                    <div className="mt-4 rounded-lg bg-blue-50 border border-blue-200 px-4 py-3 text-sm text-blue-800">
                      <p className="font-medium mb-1">Notes</p>
                      <p className="text-blue-700 whitespace-pre-wrap">
                        {tax.notes}
                      </p>
                    </div>
                  )}
                </div>
              ) : (
                <EmptyState message="Tax computation data is not available yet." />
              )}
            </Card.Content>
          </Card>
        </TabPanel>
      </TabsRoot>
    </div>
  );
}

/* --- Helper Components --- */

function StatementRow({
  label,
  amount,
  bold = false,
  highlight = false,
  indent = false,
}: {
  label: string;
  amount: number;
  bold?: boolean;
  highlight?: boolean;
  indent?: boolean;
}) {
  return (
    <div
      className={`flex justify-between items-center py-1 ${
        indent ? "pl-4" : ""
      } ${highlight ? "bg-emerald-50/50 rounded px-2 -mx-2" : ""}`}
    >
      <span
        className={`text-sm ${
          bold ? "font-semibold text-gray-900" : "text-gray-700"
        }`}
      >
        {label}
      </span>
      <span
        className={`text-sm font-mono ${
          bold ? "font-semibold text-gray-900" : "text-gray-900"
        } ${amount < 0 ? "text-red-700" : ""}`}
      >
        {eur(amount)}
      </span>
    </div>
  );
}

function Divider({ double = false }: { double?: boolean }) {
  return (
    <div className={`${double ? "border-t-2 border-double" : "border-t"} border-gray-300 my-1`} />
  );
}

function EmptyState({ message }: { message: string }) {
  return (
    <div className="text-center py-12">
      <FileText className="w-10 h-10 text-gray-300 mx-auto mb-3" />
      <p className="text-sm text-gray-500">{message}</p>
      <p className="text-xs text-gray-400 mt-1">
        Complete the year-end process and generate adjustments first.
      </p>
    </div>
  );
}
