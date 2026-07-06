"use client";

import { Button, Card, Chip, TabsRoot, TabList, Tab, TabPanel } from "@heroui/react";
import {
  AlertTriangle,
  BarChart3,
  Calculator,
  CheckCircle2,
  DollarSign,
  FileText,
  Printer,
  Scale,
  TrendingUp,
  Users,
} from "lucide-react";
import type {
  AccountingPeriod,
  BalanceSheet,
  CashFlowStatement,
  Company,
  DirectorsReportData,
  EquityChanges,
  ProfitAndLoss,
  StatementSourceSummary,
  TaxComputation,
  TrialBalanceLine,
} from "@/lib/api";
import { PageShell, StatusBadge, WorkbenchErrorState } from "@/components/workbench";

interface FinancialStatementsWorkbenchProps {
  company: Company | null;
  period: AccountingPeriod | null;
  companyId: string;
  periodId: string;
  error: string | null;
  trialBalance: TrialBalanceLine[] | null;
  pnl: ProfitAndLoss | null;
  bs: BalanceSheet | null;
  tax: TaxComputation | null;
  cashFlow: CashFlowStatement | null;
  equity: EquityChanges | null;
  directorsReport: DirectorsReportData | null;
  sources: StatementSourceSummary[] | null;
  onRetry: () => void;
}

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

export function FinancialStatementsWorkbench({
  company,
  period,
  companyId,
  periodId,
  error,
  trialBalance,
  pnl,
  bs,
  tax,
  cashFlow,
  equity,
  directorsReport,
  sources,
  onRetry,
}: FinancialStatementsWorkbenchProps) {
  if (error && !company) {
    return (
      <WorkbenchErrorState
        title="Financial statements could not be loaded"
        description={error}
        onRetry={onRetry}
      />
    );
  }

  return (
    <PageShell
      title="Financial Statements"
      subtitle={company && period
        ? `${company.legalName} - ${new Date(period.periodStart).toLocaleDateString("en-IE")} to ${new Date(period.periodEnd).toLocaleDateString("en-IE")}`
        : "Statement preview, tax computation and source trail."}
      backHref={`/companies/${companyId}/periods/${periodId}`}
      backLabel="Back to Period Workspace"
      meta={<StatementsMeta trialBalance={trialBalance} bs={bs} sources={sources} />}
      actions={
        <Button variant="outline" size="sm" className="no-print" onPress={() => window.print()}>
          <Printer className="h-4 w-4" />
          Print
        </Button>
      }
    >
{/* Tabs */}
<TabsRoot>
  <TabList
    aria-label="Financial statements tabs"
    className="flex gap-1 border-b border-gray-200 dark:border-neutral-700 mb-6 no-print overflow-x-auto"
  >
    <Tab
      id="trial-balance"
      className="px-4 py-2.5 text-sm font-medium text-gray-600 dark:text-gray-400 border-b-2 border-transparent data-[selected]:border-emerald-600 data-[selected]:text-emerald-700 dark:data-[selected]:text-emerald-400 cursor-pointer outline-none"
    >
      <Scale className="w-4 h-4 inline mr-1.5 -mt-0.5" />
      Trial Balance
    </Tab>
    <Tab
      id="sources"
      className="px-4 py-2.5 text-sm font-medium text-gray-600 dark:text-gray-400 border-b-2 border-transparent data-[selected]:border-emerald-600 data-[selected]:text-emerald-700 dark:data-[selected]:text-emerald-400 cursor-pointer outline-none"
    >
      <FileText className="w-4 h-4 inline mr-1.5 -mt-0.5" />
      Source Trail
    </Tab>
    <Tab
      id="pnl"
      className="px-4 py-2.5 text-sm font-medium text-gray-600 dark:text-gray-400 border-b-2 border-transparent data-[selected]:border-emerald-600 data-[selected]:text-emerald-700 dark:data-[selected]:text-emerald-400 cursor-pointer outline-none"
    >
      <BarChart3 className="w-4 h-4 inline mr-1.5 -mt-0.5" />
      Profit &amp; Loss
    </Tab>
    <Tab
      id="balance-sheet"
      className="px-4 py-2.5 text-sm font-medium text-gray-600 dark:text-gray-400 border-b-2 border-transparent data-[selected]:border-emerald-600 data-[selected]:text-emerald-700 dark:data-[selected]:text-emerald-400 cursor-pointer outline-none"
    >
      <FileText className="w-4 h-4 inline mr-1.5 -mt-0.5" />
      Balance Sheet
    </Tab>
    <Tab
      id="tax-computation"
      className="px-4 py-2.5 text-sm font-medium text-gray-600 dark:text-gray-400 border-b-2 border-transparent data-[selected]:border-emerald-600 data-[selected]:text-emerald-700 dark:data-[selected]:text-emerald-400 cursor-pointer outline-none"
    >
      <Calculator className="w-4 h-4 inline mr-1.5 -mt-0.5" />
      Tax Computation
    </Tab>
    <Tab
      id="cash-flow"
      className="px-4 py-2.5 text-sm font-medium text-gray-600 dark:text-gray-400 border-b-2 border-transparent data-[selected]:border-emerald-600 data-[selected]:text-emerald-700 dark:data-[selected]:text-emerald-400 cursor-pointer outline-none"
    >
      <DollarSign className="w-4 h-4 inline mr-1.5 -mt-0.5" />
      Cash Flow
    </Tab>
    <Tab
      id="equity-changes"
      className="px-4 py-2.5 text-sm font-medium text-gray-600 dark:text-gray-400 border-b-2 border-transparent data-[selected]:border-emerald-600 data-[selected]:text-emerald-700 dark:data-[selected]:text-emerald-400 cursor-pointer outline-none"
    >
      <TrendingUp className="w-4 h-4 inline mr-1.5 -mt-0.5" />
      Equity Changes
    </Tab>
    <Tab
      id="directors-report"
      className="px-4 py-2.5 text-sm font-medium text-gray-600 dark:text-gray-400 border-b-2 border-transparent data-[selected]:border-emerald-600 data-[selected]:text-emerald-700 dark:data-[selected]:text-emerald-400 cursor-pointer outline-none"
    >
      <Users className="w-4 h-4 inline mr-1.5 -mt-0.5" />
      Directors&apos; Report
    </Tab>
  </TabList>

  {/* Trial Balance Tab */}
  <TabPanel id="trial-balance">
    <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
      <Card.Header>
        <Card.Title>Trial Balance</Card.Title>
        <Card.Description>
          All account balances as at period end
        </Card.Description>
      </Card.Header>
      <Card.Content>
        {trialBalance ? (
          <div className="overflow-x-auto -mx-1">
            <table className="w-full text-sm border border-gray-200 dark:border-neutral-700 rounded-lg overflow-hidden" role="table">
              <caption className="sr-only">Trial Balance</caption>
              <thead>
                <tr className="bg-gray-50 dark:bg-neutral-800 border-b border-gray-200 dark:border-neutral-700 text-xs font-medium text-gray-600 dark:text-gray-400 uppercase tracking-wide">
                  <th scope="col" className="text-left px-4 py-2.5 w-[12%]">Code</th>
                  <th scope="col" className="text-left px-4 py-2.5 w-[36%]">Account Name</th>
                  <th scope="col" className="text-left px-4 py-2.5 w-[16%]">Type</th>
                  <th scope="col" className="text-right px-4 py-2.5 w-[18%]">Debit</th>
                  <th scope="col" className="text-right px-4 py-2.5 w-[18%]">Credit</th>
                </tr>
              </thead>
              <tbody>
                {trialBalance.map((line, idx) => (
                  <tr
                    key={`${line.code}-${idx}`}
                    className={`border-b border-gray-100 dark:border-neutral-800 hover:bg-gray-50/50 dark:hover:bg-neutral-800/50 transition-colors ${
                      idx % 2 === 1 ? "bg-gray-50/50 dark:bg-neutral-800/25" : ""
                    }`}
                  >
                    <td className="px-4 py-2.5 text-gray-600 dark:text-gray-400 font-mono text-xs">
                      {line.code}
                    </td>
                    <td className="px-4 py-2.5 text-gray-900 dark:text-gray-100">
                      {line.name}
                    </td>
                    <td className="px-4 py-2.5">
                      <Chip size="sm" variant="soft" color="default">
                        {line.type}
                      </Chip>
                    </td>
                    <td className="px-4 py-2.5 text-right font-mono text-gray-900 dark:text-gray-100">
                      {line.debit > 0 ? eur(line.debit) : ""}
                    </td>
                    <td className="px-4 py-2.5 text-right font-mono text-gray-900 dark:text-gray-100">
                      {line.credit > 0 ? eur(line.credit) : ""}
                    </td>
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr className="bg-gray-100 dark:bg-neutral-800 border-t-2 border-gray-300 dark:border-neutral-600 font-bold">
                  <td colSpan={3} className="px-4 py-3 text-gray-900 dark:text-gray-100">Totals</td>
                  <td className="px-4 py-3 text-right font-mono text-gray-900 dark:text-gray-100">
                    {eur(trialBalance.reduce((sum, l) => sum + l.debit, 0))}
                  </td>
                  <td className="px-4 py-3 text-right font-mono text-gray-900 dark:text-gray-100">
                    {eur(trialBalance.reduce((sum, l) => sum + l.credit, 0))}
                  </td>
                </tr>
              </tfoot>
            </table>
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
                <div className="px-4 py-2.5 bg-gray-50 dark:bg-neutral-800/50 border-t border-gray-200 dark:border-neutral-700">
                  {diff < 0.01 ? (
                    <div className="flex items-center gap-2 text-sm text-emerald-700 dark:text-emerald-400">
                      <CheckCircle2 className="w-4 h-4" />
                      Trial balance agrees - debits equal credits
                    </div>
                  ) : (
                    <div className="flex items-center gap-2 text-sm text-red-700 dark:text-red-400">
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

  {/* Source Trail Tab */}
  <TabPanel id="sources">
    <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
      <Card.Header>
        <Card.Title>Figure Source Trail</Card.Title>
        <Card.Description>
          Shows how each account balance is built from opening balances, imported transactions, and adjustments.
        </Card.Description>
      </Card.Header>
      <Card.Content>
        {sources && sources.length > 0 ? (
          <div className="overflow-x-auto -mx-1">
            <table className="w-full text-xs border border-gray-200 dark:border-neutral-700 rounded-lg overflow-hidden" role="table">
              <caption className="sr-only">Statement source trail</caption>
              <thead>
                <tr className="bg-gray-50 dark:bg-neutral-800 border-b border-gray-200 dark:border-neutral-700 text-gray-600 dark:text-gray-400 uppercase">
                  <th className="px-3 py-2 text-left">Account</th>
                  <th className="px-3 py-2 text-right">Opening Dr</th>
                  <th className="px-3 py-2 text-right">Opening Cr</th>
                  <th className="px-3 py-2 text-right">Txn Dr</th>
                  <th className="px-3 py-2 text-right">Txn Cr</th>
                  <th className="px-3 py-2 text-right">Adj Dr</th>
                  <th className="px-3 py-2 text-right">Adj Cr</th>
                  <th className="px-3 py-2 text-right">Closing Dr</th>
                  <th className="px-3 py-2 text-right">Closing Cr</th>
                </tr>
              </thead>
              <tbody>
                {sources.map((source) => (
                  <tr key={source.code} className="border-b border-gray-100 align-top dark:border-neutral-800">
                    <td className="px-3 py-2 min-w-72">
                      <div className="font-medium text-gray-900 dark:text-gray-100">
                        <span className="font-mono text-gray-500">{source.code}</span> {source.name}
                      </div>
                      <div className="mt-1 flex flex-wrap gap-1">
                        <Chip size="sm" variant="soft" color="default">{source.type}</Chip>
                        {source.transactionCount > 0 && <Chip size="sm" variant="soft" color="accent">{source.transactionCount} txns</Chip>}
                        {source.adjustmentCount > 0 && <Chip size="sm" variant="soft" color="warning">{source.adjustmentCount} journals</Chip>}
                      </div>
                      {source.sourceNotes.length > 0 && (
                        <ul className="mt-2 space-y-0.5 text-[11px] text-gray-500 dark:text-gray-400">
                          {source.sourceNotes.slice(0, 3).map((note) => (
                            <li key={note}>{note}</li>
                          ))}
                        </ul>
                      )}
                    </td>
                    <td className="px-3 py-2 text-right font-mono">{source.openingDebit ? eur(source.openingDebit) : ""}</td>
                    <td className="px-3 py-2 text-right font-mono">{source.openingCredit ? eur(source.openingCredit) : ""}</td>
                    <td className="px-3 py-2 text-right font-mono">{source.transactionDebit ? eur(source.transactionDebit) : ""}</td>
                    <td className="px-3 py-2 text-right font-mono">{source.transactionCredit ? eur(source.transactionCredit) : ""}</td>
                    <td className="px-3 py-2 text-right font-mono">{source.adjustmentDebit ? eur(source.adjustmentDebit) : ""}</td>
                    <td className="px-3 py-2 text-right font-mono">{source.adjustmentCredit ? eur(source.adjustmentCredit) : ""}</td>
                    <td className="px-3 py-2 text-right font-mono font-semibold">{source.closingDebit ? eur(source.closingDebit) : ""}</td>
                    <td className="px-3 py-2 text-right font-mono font-semibold">{source.closingCredit ? eur(source.closingCredit) : ""}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <EmptyState message="No statement source data is available yet." />
        )}
      </Card.Content>
    </Card>
  </TabPanel>

  {/* P&L Tab */}
  <TabPanel id="pnl">
    <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
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
              <p className="text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wide mb-1">
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
            {pnl.yearEndAdjustments.length > 0 && (
              <div className="pt-2">
                <p className="text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wide mb-1">
                  Unposted Year-End Adjustments
                </p>
                {pnl.yearEndAdjustments.map((adj, idx) => (
                  <StatementRow
                    key={`${adj.description}-${idx}`}
                    label={`  ${adj.description}${adj.approved ? "" : " (pending approval)"}`}
                    amount={adj.amount}
                  />
                ))}
                <StatementRow
                  label="Total unposted adjustment impact"
                  amount={pnl.totalYearEndAdjustments}
                  bold
                />
              </div>
            )}
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
    <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
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
            <p className="text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wide pb-1">
              Fixed Assets
            </p>
            {bs.fixedAssets.categories.length > 0 && (
              <div className="border border-gray-200 dark:border-neutral-700 rounded-lg overflow-hidden mb-2">
                <div className="grid grid-cols-4 gap-2 bg-gray-50 dark:bg-neutral-800 px-3 py-1.5 text-xs font-medium text-gray-500 dark:text-gray-400">
                  <div>Category</div>
                  <div className="text-right">Cost</div>
                  <div className="text-right">Depn</div>
                  <div className="text-right">NBV</div>
                </div>
                {bs.fixedAssets.categories.map((cat) => (
                  <div
                    key={cat.category}
                    className="grid grid-cols-4 gap-2 px-3 py-1.5 text-sm border-t border-gray-100 dark:border-neutral-700"
                  >
                    <div className="text-gray-700 dark:text-gray-300">{cat.category}</div>
                    <div className="text-right font-mono text-gray-900 dark:text-gray-100">
                      {eur(cat.cost)}
                    </div>
                    <div className="text-right font-mono text-gray-900 dark:text-gray-100">
                      {eur(cat.depreciation)}
                    </div>
                    <div className="text-right font-mono text-gray-900 dark:text-gray-100">
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
              <p className="text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wide pb-1">
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
              <p className="text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wide pb-1">
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
              <p className="text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wide pb-1">
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
              <p className="text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wide pb-1">
                Capital and Reserves
              </p>
              <StatementRow
                label="Share Capital"
                amount={bs.capitalAndReserves.shareCapital}
                indent
              />
              <StatementRow
                label="Opening retained earnings"
                amount={bs.capitalAndReserves.openingRetainedEarnings}
                indent
              />
              <StatementRow
                label="Profit after tax"
                amount={bs.capitalAndReserves.profitForYear}
                indent
              />
              <StatementRow
                label="Dividends paid"
                amount={-Math.abs(bs.capitalAndReserves.dividendsPaid)}
                indent
              />
              <StatementRow
                label="Retained earnings (derived)"
                amount={bs.capitalAndReserves.retainedEarnings}
                indent
              />
              {Math.abs(bs.capitalAndReserves.unexplainedDifference) >= 0.01 && (
                <StatementRow
                  label="Unexplained balance sheet difference"
                  amount={bs.capitalAndReserves.unexplainedDifference}
                  indent
                />
              )}
              <Divider double />
              <StatementRow
                label="Total Capital and Reserves"
                amount={bs.capitalAndReserves.total}
                bold
                highlight
              />
            </div>

            {/* Balance check */}
            <div className="mt-4 pt-3 border-t border-gray-200 dark:border-neutral-700">
              {bs.balances ? (
                <div className="flex items-center gap-2 text-sm text-emerald-700 dark:text-emerald-400">
                  <CheckCircle2 className="w-5 h-5" />
                  <span className="font-medium">
                    Balance sheet balances
                  </span>
                </div>
              ) : (
                <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700 dark:border-red-900/60 dark:bg-red-950/30 dark:text-red-300">
                  <div className="flex items-center gap-2">
                  <AlertTriangle className="w-5 h-5" />
                  <span className="font-medium">
                    Balance sheet does not balance
                  </span>
                  </div>
                  <p className="mt-1 text-xs">
                    Unexplained difference: {eur(bs.capitalAndReserves.unexplainedDifference)}. Review source transactions, year-end facts and journals before approving.
                  </p>
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
    <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
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
                <p className="text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wide mb-1">
                  Tax Adjustments
                </p>
                {tax.adjustments.map((adj, idx) => (
                  <div key={idx} className="flex justify-between items-start py-1 pl-4">
                    <div className="flex-1">
                      <span className="text-sm text-gray-700 dark:text-gray-300">
                        {adj.description}
                      </span>
                      {adj.basis && (
                        <span className="text-xs text-gray-400 dark:text-gray-500 ml-2">
                          ({adj.basis})
                        </span>
                      )}
                    </div>
                    <span className="text-sm font-mono text-gray-900 dark:text-gray-100 ml-4">
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
              <p className="text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wide mb-1">
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
              <div className="mt-4 rounded-lg bg-blue-50 dark:bg-blue-900/20 border border-blue-200 dark:border-blue-800 px-4 py-3 text-sm text-blue-800 dark:text-blue-300">
                <p className="font-medium mb-1">Notes</p>
                <p className="text-blue-700 dark:text-blue-400 whitespace-pre-wrap">
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

  {/* Cash Flow Tab */}
  <TabPanel id="cash-flow">
    <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
      <Card.Header>
        <Card.Title>Cash Flow Statement</Card.Title>
        <Card.Description>
          Cash flows for the period
        </Card.Description>
      </Card.Header>
      <Card.Content>
        {cashFlow ? (
          <div className="max-w-lg mx-auto space-y-1">
            <StatementRow label="Operating Profit" amount={cashFlow.operatingProfit} bold />
            {cashFlow.operatingAdjustments.map((adj, idx) => (
              <StatementRow key={idx} label={`  ${adj.description}`} amount={adj.amount} indent />
            ))}
            <Divider />
            <StatementRow label="Cash Generated from Operations" amount={cashFlow.cashFromOperations} bold highlight />
            <StatementRow label="Tax Paid" amount={-Math.abs(cashFlow.taxPaid)} />
            <Divider />
            <StatementRow label="Net Cash from Operating Activities" amount={cashFlow.netCashFromOperating} bold highlight />

            <Divider />
            <p className="text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wide pt-2 pb-1">
              Investing Activities
            </p>
            <StatementRow label="Capital Expenditure (Purchases)" amount={-Math.abs(cashFlow.capitalExpenditurePurchases)} indent />
            <StatementRow label="Disposal Proceeds" amount={cashFlow.capitalExpenditureDisposals} indent />
            <Divider />
            <StatementRow label="Net Cash from Investing Activities" amount={cashFlow.netCashFromInvesting} bold />

            <Divider />
            <p className="text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wide pt-2 pb-1">
              Financing Activities
            </p>
            <StatementRow label="Loan Repayments" amount={-Math.abs(cashFlow.loanRepayments)} indent />
            <StatementRow label="Loan Drawdowns" amount={cashFlow.loanDrawdowns} indent />
            <StatementRow label="Dividends Paid" amount={-Math.abs(cashFlow.dividendsPaid)} indent />
            <Divider />
            <StatementRow label="Net Cash from Financing Activities" amount={cashFlow.netCashFromFinancing} bold />

            <Divider double />
            <StatementRow label="Net Increase / (Decrease) in Cash" amount={cashFlow.netIncreaseInCash} bold highlight />
            <StatementRow label="Opening Cash" amount={cashFlow.openingCash} />
            <Divider double />
            <StatementRow label="Closing Cash" amount={cashFlow.closingCash} bold highlight />
          </div>
        ) : (
          <EmptyState message="Cash flow data is not available yet." />
        )}
      </Card.Content>
    </Card>
  </TabPanel>

  {/* Equity Changes Tab */}
  <TabPanel id="equity-changes">
    <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
      <Card.Header>
        <Card.Title>Statement of Changes in Equity</Card.Title>
        <Card.Description>
          Movements in shareholders&apos; funds for the period
        </Card.Description>
      </Card.Header>
      <Card.Content>
        {equity ? (
          <div className="overflow-x-auto -mx-1">
            <table className="w-full text-sm border border-gray-200 dark:border-neutral-700 rounded-lg overflow-hidden" role="table">
              <caption className="sr-only">Statement of Changes in Equity</caption>
              <thead>
                <tr className="bg-gray-50 dark:bg-neutral-800 border-b border-gray-200 dark:border-neutral-700 text-xs font-medium text-gray-600 dark:text-gray-400 uppercase tracking-wide">
                  <th scope="col" className="text-left px-4 py-2.5 w-[40%]"></th>
                  <th scope="col" className="text-right px-4 py-2.5 w-[20%]">Share Capital</th>
                  <th scope="col" className="text-right px-4 py-2.5 w-[20%]">Retained Earnings</th>
                  <th scope="col" className="text-right px-4 py-2.5 w-[20%]">Total</th>
                </tr>
              </thead>
              <tbody>
                <tr className="border-b border-gray-100 dark:border-neutral-800">
                  <td className="px-4 py-2.5 font-semibold text-gray-900 dark:text-gray-100">Opening Balance</td>
                  <td className="px-4 py-2.5 text-right font-mono text-gray-900 dark:text-gray-100">{eur(equity.openingShareCapital)}</td>
                  <td className="px-4 py-2.5 text-right font-mono text-gray-900 dark:text-gray-100">{eur(equity.openingRetainedEarnings)}</td>
                  <td className="px-4 py-2.5 text-right font-mono font-semibold text-gray-900 dark:text-gray-100">{eur(equity.openingTotal)}</td>
                </tr>
                <tr className="border-b border-gray-100 dark:border-neutral-800">
                  <td className="px-4 py-2.5 text-gray-700 dark:text-gray-300">Profit for the Year</td>
                  <td className="px-4 py-2.5 text-right font-mono text-gray-400 dark:text-gray-500">-</td>
                  <td className="px-4 py-2.5 text-right font-mono text-gray-900 dark:text-gray-100">{eur(equity.profitForYear)}</td>
                  <td className="px-4 py-2.5 text-right font-mono text-gray-900 dark:text-gray-100">{eur(equity.profitForYear)}</td>
                </tr>
                <tr className="border-b border-gray-100 dark:border-neutral-800">
                  <td className="px-4 py-2.5 text-gray-700 dark:text-gray-300">Dividends Paid</td>
                  <td className="px-4 py-2.5 text-right font-mono text-gray-400 dark:text-gray-500">-</td>
                  <td className="px-4 py-2.5 text-right font-mono text-red-700 dark:text-red-400">{equity.dividendsPaid !== 0 ? eur(-Math.abs(equity.dividendsPaid)) : eur(0)}</td>
                  <td className="px-4 py-2.5 text-right font-mono text-red-700 dark:text-red-400">{equity.dividendsPaid !== 0 ? eur(-Math.abs(equity.dividendsPaid)) : eur(0)}</td>
                </tr>
                {equity.sharesIssued !== 0 && (
                  <tr className="border-b border-gray-100 dark:border-neutral-800">
                    <td className="px-4 py-2.5 text-gray-700 dark:text-gray-300">Shares Issued</td>
                    <td className="px-4 py-2.5 text-right font-mono text-gray-900 dark:text-gray-100">{eur(equity.sharesIssued)}</td>
                    <td className="px-4 py-2.5 text-right font-mono text-gray-400 dark:text-gray-500">-</td>
                    <td className="px-4 py-2.5 text-right font-mono text-gray-900 dark:text-gray-100">{eur(equity.sharesIssued)}</td>
                  </tr>
                )}
              </tbody>
              <tfoot>
                <tr className="bg-gray-100 dark:bg-neutral-800 border-t-2 border-gray-300 dark:border-neutral-600 font-bold">
                  <td className="px-4 py-3 text-gray-900 dark:text-gray-100">Closing Balance</td>
                  <td className="px-4 py-3 text-right font-mono text-gray-900 dark:text-gray-100">{eur(equity.closingShareCapital)}</td>
                  <td className="px-4 py-3 text-right font-mono text-gray-900 dark:text-gray-100">{eur(equity.closingRetainedEarnings)}</td>
                  <td className="px-4 py-3 text-right font-mono text-gray-900 dark:text-gray-100">{eur(equity.closingTotal)}</td>
                </tr>
              </tfoot>
            </table>
          </div>
        ) : (
          <EmptyState message="Equity changes data is not available yet." />
        )}
      </Card.Content>
    </Card>
  </TabPanel>

  {/* Directors' Report Tab */}
  <TabPanel id="directors-report">
    <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
      <Card.Header>
        <Card.Title>Directors&apos; Report</Card.Title>
        <Card.Description>
          Report of the directors for the period
        </Card.Description>
      </Card.Header>
      <Card.Content>
        {directorsReport ? (
          <div className="max-w-2xl mx-auto space-y-6">
            {/* Header */}
            <div className="text-center pb-4 border-b border-gray-200 dark:border-neutral-700">
              <h2 className="text-lg font-bold text-gray-900 dark:text-gray-100">
                {directorsReport.companyName}
              </h2>
              <p className="text-sm text-gray-500 dark:text-gray-400 mt-1">
                Directors&apos; Report for the period{" "}
                {new Date(directorsReport.periodStart).toLocaleDateString("en-IE")} to{" "}
                {new Date(directorsReport.periodEnd).toLocaleDateString("en-IE")}
              </p>
            </div>

            {/* Regime indicators */}
            <div className="flex flex-wrap gap-2">
              <Chip size="sm" variant="soft" color="default">{directorsReport.electedRegime}</Chip>
              {directorsReport.isMicroExempt && (
                <Chip size="sm" variant="soft" color="success">Micro Exempt (s.280D)</Chip>
              )}
              {directorsReport.isSmallExemptFromBusinessReview && (
                <Chip size="sm" variant="soft" color="success">Small Company Exempt from Business Review</Chip>
              )}
            </div>

            {/* Directors */}
            <div>
              <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-100 mb-2">Directors</h3>
              <ul className="list-disc list-inside space-y-1">
                {directorsReport.directorNames.map((name, idx) => (
                  <li key={idx} className="text-sm text-gray-700 dark:text-gray-300">{name}</li>
                ))}
              </ul>
              {directorsReport.secretaryName && (
                <p className="text-sm text-gray-500 dark:text-gray-400 mt-2">
                  Company Secretary: {directorsReport.secretaryName}
                </p>
              )}
            </div>

            {/* Principal Activities */}
            <div>
              <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-100 mb-2">Principal Activities</h3>
              <p className="text-sm text-gray-700 dark:text-gray-300 whitespace-pre-wrap">
                {directorsReport.principalActivities}
              </p>
            </div>

            {/* Results and Dividends */}
            <div>
              <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-100 mb-2">Results and Dividends</h3>
              <p className="text-sm text-gray-700 dark:text-gray-300 whitespace-pre-wrap">
                {directorsReport.resultsAndDividends}
              </p>
            </div>

            {/* Accounting Records */}
            <div>
              <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-100 mb-2">Accounting Records</h3>
              <p className="text-sm text-gray-700 dark:text-gray-300 whitespace-pre-wrap">
                {directorsReport.accountingRecordsStatement}
              </p>
            </div>

            {/* Post Balance Sheet Events */}
            {directorsReport.postBalanceSheetEvents && (
              <div>
                <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-100 mb-2">Post Balance Sheet Events</h3>
                <p className="text-sm text-gray-700 dark:text-gray-300 whitespace-pre-wrap">
                  {directorsReport.postBalanceSheetEvents}
                </p>
              </div>
            )}

            {/* Going Concern */}
            {directorsReport.goingConcernStatement && (
              <div className="rounded-lg bg-amber-50 dark:bg-amber-900/20 border border-amber-200 dark:border-amber-800 px-4 py-3">
                <h3 className="text-sm font-semibold text-amber-800 dark:text-amber-300 mb-1">Going Concern</h3>
                <p className="text-sm text-amber-700 dark:text-amber-400 whitespace-pre-wrap">
                  {directorsReport.goingConcernStatement}
                </p>
              </div>
            )}

            {/* Audit Information */}
            {directorsReport.auditInformationStatement && (
              <div>
                <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-100 mb-2">Audit Information</h3>
                <p className="text-sm text-gray-700 dark:text-gray-300 whitespace-pre-wrap">
                  {directorsReport.auditInformationStatement}
                </p>
              </div>
            )}
          </div>
        ) : (
          <EmptyState message="Directors' report data is not available yet." />
        )}
      </Card.Content>
    </Card>
  </TabPanel>
</TabsRoot>
    </PageShell>
  );
}

function StatementsMeta({
  trialBalance,
  bs,
  sources,
}: {
  trialBalance: TrialBalanceLine[] | null;
  bs: BalanceSheet | null;
  sources: StatementSourceSummary[] | null;
}) {
  const trialBalanceDiff = trialBalance
    ? Math.abs(
        trialBalance.reduce((sum, line) => sum + line.debit, 0)
          - trialBalance.reduce((sum, line) => sum + line.credit, 0),
      )
    : null;

  return (
    <>
      <StatusBadge tone={trialBalanceDiff === null ? "warn" : trialBalanceDiff < 0.01 ? "good" : "bad"}>
        {trialBalanceDiff === null ? "Trial balance pending" : trialBalanceDiff < 0.01 ? "Trial balance agrees" : "Trial balance variance"}
      </StatusBadge>
      <StatusBadge tone={bs?.balances ? "good" : bs ? "bad" : "warn"}>
        {bs?.balances ? "Balance sheet agrees" : bs ? "Balance sheet variance" : "Balance sheet pending"}
      </StatusBadge>
      <StatusBadge tone={sources && sources.length > 0 ? "info" : "warn"}>
        {sources?.length ?? 0} source rows
      </StatusBadge>
    </>
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
      } ${highlight ? "bg-emerald-50/50 dark:bg-emerald-900/20 rounded px-2 -mx-2" : ""}`}
    >
      <span
        className={`text-sm ${
          bold ? "font-semibold text-gray-900 dark:text-gray-100" : "text-gray-700 dark:text-gray-300"
        }`}
      >
        {label}
      </span>
      <span
        className={`text-sm font-mono ${
          bold ? "font-semibold text-gray-900 dark:text-gray-100" : "text-gray-900 dark:text-gray-100"
        } ${amount < 0 ? "text-red-700 dark:text-red-400" : ""}`}
      >
        {eur(amount)}
      </span>
    </div>
  );
}

function Divider({ double = false }: { double?: boolean }) {
  return (
    <div className={`${double ? "border-t-2 border-double" : "border-t"} border-gray-300 dark:border-neutral-600 my-1`} />
  );
}

function EmptyState({ message }: { message: string }) {
  return (
    <div className="text-center py-12">
      <FileText className="w-10 h-10 text-gray-300 dark:text-gray-600 mx-auto mb-3" />
      <p className="text-sm text-gray-500 dark:text-gray-400">{message}</p>
      <p className="text-xs text-gray-400 dark:text-gray-500 mt-1">
        Complete the year-end process and generate adjustments first.
      </p>
    </div>
  );
}
