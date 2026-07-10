"use client";

import Link from "next/link";
import { Button, Spinner, Tab, TabList, TabPanel, TabsRoot } from "@heroui/react";
import type { ReactNode } from "react";
import {
  AlertTriangle,
  Calculator,
  CheckCircle2,
  FileSearch,
  ListChecks,
  RefreshCw,
  Rows3,
  Scale,
} from "lucide-react";
import type { AccountantWorkingPaperPack, WorkingPaperSourceReference } from "@/lib/api";
import {
  ActionLink,
  DataGrid,
  PageShell,
  StatusBadge,
  WorkflowDecisionSummary,
} from "@/components/workbench";

interface AccountantWorkingPaperWorkbenchProps {
  companyId: string;
  periodId: string;
  pack: AccountantWorkingPaperPack | null;
  canView: boolean;
  canGenerate: boolean;
  generating: boolean;
  error: string | null;
  onGenerate: () => void | Promise<void>;
  onRetry: () => void | Promise<void>;
}

function eur(value: number) {
  const rendered = new Intl.NumberFormat("en-IE", {
    style: "currency",
    currency: "EUR",
    minimumFractionDigits: 2,
  }).format(Math.abs(value));
  return value < 0 ? `(${rendered})` : rendered;
}

function shortHash(value: string) {
  return `${value.slice(0, 12)}…${value.slice(-8)}`;
}

function Sources({ items }: { items: WorkingPaperSourceReference[] }) {
  if (items.length === 0) return <span className="text-xs text-amber-700 dark:text-amber-300">No structured source</span>;
  const first = items[0];
  return (
    <div className="min-w-40 space-y-1 text-xs">
      <Link
        href={first.drillDownRoute}
        className="font-semibold text-emerald-700 underline-offset-2 hover:underline dark:text-emerald-300"
      >
        {items.length} source{items.length === 1 ? "" : "s"}
      </Link>
      <p className="truncate text-[var(--muted-foreground)]" title={`${first.sourceType} #${first.entityId}: ${first.label}`}>
        {first.sourceType} #{first.entityId}: {first.label}
      </p>
      {items.some((item) => item.reviewedBy) && (
        <p className="text-[var(--muted-foreground)]">
          Review evidence retained
        </p>
      )}
    </div>
  );
}

export function AccountantWorkingPaperWorkbench({
  companyId,
  periodId,
  pack,
  canView,
  canGenerate,
  generating,
  error,
  onGenerate,
  onRetry,
}: AccountantWorkingPaperWorkbenchProps) {
  const actions = canGenerate ? (
    <Button
      size="sm"
      variant="primary"
      aria-label={pack ? "Regenerate pack" : "Generate pack"}
      isDisabled={generating}
      onPress={() => void onGenerate()}
    >
      {generating ? <Spinner size="sm" /> : <RefreshCw aria-hidden="true" className="h-4 w-4" />}
      {pack ? "Regenerate pack" : "Generate pack"}
    </Button>
  ) : undefined;

  if (!pack) {
    return (
      <PageShell
        title="Accountant working papers"
        subtitle="Retained, drillable internal schedules for statement and corporation-tax review."
        backHref={`/companies/${companyId}/periods/${periodId}/statements`}
        backLabel="Back to financial statements"
        actions={actions}
      >
        <div className="rounded-md border border-amber-300 bg-amber-50 p-4 text-sm leading-6 text-amber-950 dark:border-amber-800 dark:bg-amber-950/30 dark:text-amber-100">
          <p className="font-semibold">INTERNAL WORKING PAPERS — NOT A CRO OR CT1 RETURN</p>
          <p className="mt-1">Nothing on this route submits data to CRO or Revenue.</p>
        </div>
        <section className="mt-4 rounded-md border border-[var(--border)] bg-[var(--surface)] p-5" aria-label="Working-paper empty state">
          <FileSearch aria-hidden="true" className="h-8 w-8 text-[var(--muted-foreground)]" />
          <h2 className="mt-3 text-base font-semibold">No retained pack yet</h2>
          <p className="mt-1 max-w-2xl text-sm leading-6 text-[var(--muted-foreground)]">
            Generate a candidate-bound snapshot after categorisation and year-end work are current. Later source changes invalidate the retained artifact and require regeneration.
          </p>
          {error && (
            <div role="alert" className="mt-4 rounded-md border border-red-300 bg-red-50 p-3 text-sm text-red-800 dark:border-red-900 dark:bg-red-950/30 dark:text-red-200">
              {error} <button type="button" className="ml-2 font-semibold underline" onClick={() => void onRetry()}>Retry</button>
            </div>
          )}
          {!canView && (
            <p className="mt-4 text-sm font-medium text-amber-800 dark:text-amber-200">
              Client access is not permitted. An Owner, Accountant, or Reviewer must open this internal workspace.
            </p>
          )}
          {canView && !canGenerate && (
            <p className="mt-4 text-sm font-medium text-sky-800 dark:text-sky-200">
              Reviewer access is read-only. An Owner or Accountant must generate the first retained pack.
            </p>
          )}
        </section>
      </PageShell>
    );
  }

  const blockerCount = pack.reviewExceptions.blockingCount + pack.corporationTaxBridge.blockingReasons.length;
  const reconciled = pack.adjustedTrialBalance.reconciliations.every((item) => item.reconciles)
    && pack.leadSchedules.reconciliations.every((item) => item.reconciles)
    && pack.corporationTaxBridge.reconciliations.every((item) => item.reconciles);

  return (
    <PageShell
      title="Accountant working papers"
      subtitle={`${pack.identity.companyName} · ${pack.identity.periodStart} to ${pack.identity.periodEnd}`}
      backHref={`/companies/${companyId}/periods/${periodId}/statements`}
      backLabel="Back to financial statements"
      actions={actions}
      meta={
        <div className="flex flex-wrap gap-2">
          <StatusBadge tone="info">Artifact v{pack.identity.artifactVersion}</StatusBadge>
          <StatusBadge tone={blockerCount === 0 ? "good" : "bad"}>{blockerCount === 0 ? "Review ready" : `${blockerCount} blockers`}</StatusBadge>
          <StatusBadge tone={reconciled ? "good" : "bad"}>{reconciled ? "Figures reconcile" : "Reconciliation failed"}</StatusBadge>
        </div>
      }
    >
      <div className="rounded-md border border-amber-300 bg-amber-50 p-4 text-sm leading-6 text-amber-950 dark:border-amber-800 dark:bg-amber-950/30 dark:text-amber-100" role="status">
        <p className="font-semibold">{pack.warning}</p>
        <p className="mt-1">Qualified-accountant review remains required; this artifact is not a filing approval.</p>
      </div>

      <section className="mt-4 grid gap-3 rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-4 md:grid-cols-2 xl:grid-cols-4" aria-label="Working-paper artifact identity">
        <Identity label="Generated by" value={`${pack.identity.generatedByDisplayName} (${pack.identity.generatedByRole})`} />
        <Identity label="Generated at" value={new Date(pack.identity.generatedAtUtc).toLocaleString("en-IE")} />
        <Identity label="Release candidate" value={pack.identity.releaseCandidate} mono />
        <Identity label="Artifact SHA-256" value={shortHash(pack.identity.artifactSha256)} mono title={pack.identity.artifactSha256} />
        <Identity label="Source snapshot SHA-256" value={shortHash(pack.identity.sourceDataSha256)} mono title={pack.identity.sourceDataSha256} />
        <Identity label="Tenant / company / period" value={`${pack.identity.tenantId} / ${pack.identity.companyId} / ${pack.identity.periodId}`} mono />
        <Identity label="Schema" value={pack.identity.schemaVersion} mono />
        <Identity label="Boundary" value="Internal support only" />
      </section>

      <div className="mt-4">
        <WorkflowDecisionSummary
          ariaLabel="Working-paper decision summary"
          items={[
            {
              title: "What is wrong?",
              tone: blockerCount === 0 ? "good" : "bad",
              summary: blockerCount === 0 ? "No open machine blockers" : `${blockerCount} blocking review items`,
              detail: blockerCount === 0 ? "Human review is still required." : "Open Review exceptions and resolve source-level items.",
            },
            {
              title: "What is ready?",
              tone: reconciled ? "good" : "bad",
              summary: reconciled ? "Ledgers and tax bridge reconcile" : "A reconciliation failed",
              detail: `${pack.leadSchedules.rows.length} ledgers and ${pack.categorizedTransactions.totalCount} transactions are retained.`,
            },
            {
              title: "What must I do next?",
              tone: blockerCount === 0 ? "info" : "warn",
              summary: blockerCount === 0 ? "Perform named accountant review" : "Clear blocking exceptions",
              detail: "Regenerate whenever source facts, transactions, journals, or review evidence change.",
              action: { href: "#review-exceptions-panel", label: "Open exceptions" },
            },
          ]}
        />
      </div>

      {error && (
        <div role="alert" className="mt-4 rounded-md border border-red-300 bg-red-50 p-3 text-sm text-red-800 dark:border-red-900 dark:bg-red-950/30 dark:text-red-200">
          {error} <button type="button" className="ml-2 font-semibold underline" onClick={() => void onRetry()}>Retry</button>
        </div>
      )}

      <section className="mt-4 rounded-md border border-[var(--border)] bg-[var(--surface)] p-4" aria-labelledby="working-paper-index-heading">
        <div className="flex items-start justify-between gap-3">
          <div>
            <h2 id="working-paper-index-heading" className="text-sm font-semibold">Working-paper index</h2>
            <p className="mt-1 text-xs leading-5 text-[var(--muted-foreground)]">Every retained output has an explicit API contract and independent SHA-256.</p>
          </div>
          <ListChecks aria-hidden="true" className="h-5 w-5 text-emerald-600" />
        </div>
        <div className="mt-3 grid gap-2 md:grid-cols-2 xl:grid-cols-5">
          {pack.workingPaperIndex.entries.map((entry) => (
            <article key={entry.code} className="min-w-0 rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3">
              <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">{entry.code}</p>
              <p className="mt-1 text-sm font-semibold">{entry.title}</p>
              <p className="mt-2 text-xs text-[var(--muted-foreground)]">{entry.itemCount} retained rows</p>
              <p className="mt-1 truncate font-mono text-[11px]" title={entry.artifactSha256}>{shortHash(entry.artifactSha256)}</p>
            </article>
          ))}
        </div>
      </section>

      <p id="working-paper-tab-help" role="note" className="mt-4 rounded-md border border-sky-200 bg-sky-50 px-3 py-2 text-xs font-medium text-sky-900 dark:border-sky-800 dark:bg-sky-950/40 dark:text-sky-100">
        Swipe to reveal more output tabs. Use Left and Right Arrow keys to move between tabs.
      </p>
      <TabsRoot>
        <TabList
          aria-label="Accountant working-paper outputs"
          aria-describedby="working-paper-tab-help"
          data-overflow-tablist="true"
          className="mb-4 flex max-w-full gap-1 overflow-x-auto overscroll-x-contain whitespace-nowrap border-b border-[var(--border)]"
        >
          <OutputTab id="lead-schedules" icon={<Rows3 className="h-4 w-4" />} label="Lead schedules" />
          <OutputTab id="categorized-transactions" icon={<ListChecks className="h-4 w-4" />} label="Transactions" />
          <OutputTab id="review-exceptions" icon={<AlertTriangle className="h-4 w-4" />} label="Exceptions" />
          <OutputTab id="adjusted-trial-balance" icon={<Scale className="h-4 w-4" />} label="Adjusted TB" />
          <OutputTab id="corporation-tax-bridge" icon={<Calculator className="h-4 w-4" />} label="CT bridge" />
        </TabList>

        <TabPanel id="lead-schedules">
          <DataGrid
            caption="Retained account lead schedules"
            filterPlaceholder="Filter lead schedules"
            columns={["Account", "Opening", "Transactions", "Journals", "Closing", "Drill-down"]}
            mobilePresentation="cards"
            rows={pack.leadSchedules.rows.map((row) => ({
              id: row.code,
              searchText: `${row.code} ${row.name} ${row.accountType}`,
              cells: [
                <div key="account"><p className="font-mono text-xs font-semibold">{row.code}</p><p className="text-sm">{row.name}</p></div>,
                <MoneyPair key="opening" debit={row.openingDebit} credit={row.openingCredit} />,
                <MoneyPair key="transactions" debit={row.transactionDebit} credit={row.transactionCredit} />,
                <MoneyPair key="journals" debit={row.journalDebit} credit={row.journalCredit} />,
                <MoneyPair key="closing" debit={row.closingDebit} credit={row.closingCredit} />,
                <Sources key="sources" items={row.sources} />,
              ],
            }))}
            totals={[
              <strong key="label">Totals</strong>,
              <MoneyPair key="opening" debit={pack.leadSchedules.rows.reduce((sum, row) => sum + row.openingDebit, 0)} credit={pack.leadSchedules.rows.reduce((sum, row) => sum + row.openingCredit, 0)} />,
              <MoneyPair key="transactions" debit={pack.leadSchedules.rows.reduce((sum, row) => sum + row.transactionDebit, 0)} credit={pack.leadSchedules.rows.reduce((sum, row) => sum + row.transactionCredit, 0)} />,
              <MoneyPair key="journals" debit={pack.leadSchedules.rows.reduce((sum, row) => sum + row.journalDebit, 0)} credit={pack.leadSchedules.rows.reduce((sum, row) => sum + row.journalCredit, 0)} />,
              <MoneyPair key="closing" debit={pack.leadSchedules.rows.reduce((sum, row) => sum + row.closingDebit, 0)} credit={pack.leadSchedules.rows.reduce((sum, row) => sum + row.closingCredit, 0)} />,
              <span key="sources" />,
            ]}
          />
        </TabPanel>

        <TabPanel id="categorized-transactions">
          <DataGrid
            caption="Categorized transaction report"
            filterPlaceholder="Filter categorized transactions"
            columns={["Date", "Description", "Bank", "Amount", "Category", "Review evidence"]}
            mobilePresentation="cards"
            rows={pack.categorizedTransactions.rows.map((row) => ({
              id: row.transactionId,
              tone: row.categoryId === null ? "bad" : row.includedInLedger ? "default" : "warn",
              searchText: `${row.date} ${row.description} ${row.bankAccountName} ${row.categoryCode ?? "uncategorised"}`,
              cells: [
                <time key="date" dateTime={row.date}>{row.date}</time>,
                <div key="description"><p className="font-medium">{row.description}</p><p className="text-xs text-[var(--muted-foreground)]">Transaction #{row.transactionId}</p></div>,
                <span key="bank">{row.bankAccountName}</span>,
                <span key="amount" className="font-mono">{eur(row.amount)}</span>,
                <div key="category"><p>{row.categoryCode ? `${row.categoryCode} · ${row.categoryName}` : "Uncategorised"}</p><p className="text-xs text-[var(--muted-foreground)]">{row.includedInLedger ? "Included" : "Excluded after review"}</p></div>,
                <Sources key="sources" items={row.sources} />,
              ],
            }))}
          />
        </TabPanel>

        <TabPanel id="review-exceptions">
          <section id="review-exceptions-panel" tabIndex={-1} className="space-y-3 outline-none" aria-label="Review exceptions">
            {pack.reviewExceptions.items.length === 0 ? (
              <div className="rounded-md border border-emerald-200 bg-emerald-50 p-4 text-sm text-emerald-800 dark:border-emerald-800 dark:bg-emerald-950/30 dark:text-emerald-100">
                <CheckCircle2 aria-hidden="true" className="mr-2 inline h-4 w-4" /> No machine-detected review exceptions remain.
              </div>
            ) : pack.reviewExceptions.items.map((item, index) => (
              <article key={`${item.code}-${index}`} className={`rounded-md border p-4 ${item.severity === "blocking" ? "border-red-300 bg-red-50 dark:border-red-900 dark:bg-red-950/30" : "border-amber-300 bg-amber-50 dark:border-amber-800 dark:bg-amber-950/30"}`}>
                <div className="flex flex-col justify-between gap-3 sm:flex-row sm:items-start">
                  <div>
                    <p className="text-xs font-semibold uppercase">{item.severity} · {item.code}</p>
                    <p className="mt-1 text-sm font-medium">{item.message}</p>
                    {item.sources.length > 0 && <div className="mt-2"><Sources items={item.sources} /></div>}
                  </div>
                  <ActionLink href={item.resolutionRoute} size="sm" variant="outline">Resolve</ActionLink>
                </div>
              </article>
            ))}
          </section>
        </TabPanel>

        <TabPanel id="adjusted-trial-balance">
          <DataGrid
            caption="Adjusted trial balance"
            filterPlaceholder="Filter adjusted trial balance"
            columns={["Account", "Unadjusted", "Journals", "Adjusted", "Drill-down"]}
            rows={pack.adjustedTrialBalance.rows.map((row) => ({
              id: row.code,
              searchText: `${row.code} ${row.name} ${row.accountType}`,
              cells: [
                <div key="account"><p className="font-mono text-xs font-semibold">{row.code}</p><p>{row.name}</p></div>,
                <MoneyPair key="unadjusted" debit={row.unadjustedDebit} credit={row.unadjustedCredit} />,
                <MoneyPair key="journals" debit={row.journalDebit} credit={row.journalCredit} />,
                <MoneyPair key="adjusted" debit={row.adjustedDebit} credit={row.adjustedCredit} />,
                <Sources key="sources" items={row.sources} />,
              ],
            }))}
            totals={[
              <strong key="label">Totals</strong>,
              <MoneyPair key="unadjusted" debit={pack.adjustedTrialBalance.totalUnadjustedDebits} credit={pack.adjustedTrialBalance.totalUnadjustedCredits} />,
              <MoneyPair key="journals" debit={pack.adjustedTrialBalance.totalJournalDebits} credit={pack.adjustedTrialBalance.totalJournalCredits} />,
              <MoneyPair key="adjusted" debit={pack.adjustedTrialBalance.totalAdjustedDebits} credit={pack.adjustedTrialBalance.totalAdjustedCredits} />,
              <span key="sources" />,
            ]}
          />
        </TabPanel>

        <TabPanel id="corporation-tax-bridge">
          <div className="mb-3 rounded-md border border-amber-300 bg-amber-50 p-3 text-sm text-amber-950 dark:border-amber-800 dark:bg-amber-950/30 dark:text-amber-100">
            Corporation-tax support bridge only — not a CT1 return and no direct ROS submission is supported.
          </div>
          <DataGrid
            caption="Corporation-tax bridge"
            columns={["Bridge item", "Amount", "Basis", "Sources"]}
            mobilePresentation="cards"
            rows={pack.corporationTaxBridge.rows.map((row) => ({
              id: row.code,
              searchText: `${row.code} ${row.description} ${row.basis}`,
              cells: [
                <div key="item"><p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">{row.code}</p><p>{row.description}</p></div>,
                <span key="amount" className="font-mono font-semibold">{eur(row.amount)}</span>,
                <span key="basis" className="text-sm">{row.basis}</span>,
                <Sources key="sources" items={row.sources} />,
              ],
            }))}
          />
        </TabPanel>
      </TabsRoot>
    </PageShell>
  );
}

function OutputTab({ id, icon, label }: { id: string; icon: ReactNode; label: string }) {
  return (
    <Tab id={id} className="cursor-pointer border-b-2 border-transparent px-4 py-2.5 text-sm font-medium text-[var(--muted-foreground)] outline-none data-[selected]:border-emerald-600 data-[selected]:text-emerald-700 dark:data-[selected]:text-emerald-300">
      <span className="mr-1.5 inline-flex align-text-bottom" aria-hidden="true">{icon}</span>{label}
    </Tab>
  );
}

function MoneyPair({ debit, credit }: { debit: number; credit: number }) {
  return (
    <div className="min-w-28 font-mono text-xs">
      <p>Dr {eur(debit)}</p>
      <p>Cr {eur(credit)}</p>
    </div>
  );
}

function Identity({ label, value, mono = false, title }: { label: string; value: string; mono?: boolean; title?: string }) {
  return (
    <div className="min-w-0">
      <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">{label}</p>
      <p className={`mt-1 truncate text-sm ${mono ? "font-mono text-xs" : "font-medium"}`} title={title ?? value}>{value}</p>
    </div>
  );
}
