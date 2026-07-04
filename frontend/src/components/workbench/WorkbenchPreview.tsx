"use client";

import { useState } from "react";
import {
  DataTable,
  EvidenceChecklist,
  FilingActionBar,
  IssueDigest,
  MetricStrip,
  MoneyField,
  MoneyInput,
  PermissionDeniedPanel,
  ReviewPanel,
  SectionHeader,
  StatusBadge,
  WorkbenchEmptyState,
  WorkbenchErrorState,
  WorkbenchHeader,
  WorkbenchLoadingState,
  WorkbenchShell,
  WorkflowRail,
} from "@/components/workbench";

const workflowItems = [
  { id: "setup", label: "Setup", detail: "Company identity, officers and filing profile recorded.", state: "done" as const },
  { id: "import", label: "Import", detail: "Bank accounts, opening balances and transactions loaded.", state: "done" as const },
  { id: "classify", label: "Classify", detail: "Size class and filing regime supported by source law.", state: "done" as const },
  { id: "year-end", label: "Year-End", detail: "Accruals, loans, tax, payroll and signatory evidence checked.", state: "active" as const },
  { id: "statements", label: "Statements", detail: "Primary statements agree to the trial balance.", state: "todo" as const },
  { id: "notes", label: "Notes", detail: "Required disclosures and legal source links reviewed.", state: "todo" as const },
  { id: "review", label: "Review", detail: "Named qualified-accountant approval required.", state: "blocked" as const },
  { id: "filing", label: "Filing", detail: "CRO and Revenue workflow remains recorded, not automated.", state: "blocked" as const },
];

const evidenceItems = [
  {
    code: "source-law",
    label: "Rule decision has source-law reference",
    required: true,
    satisfied: true,
    detail: "CRO, Revenue and FRC guidance linked with effective dates.",
  },
  {
    code: "ixbrl-validation",
    label: "Internal iXBRL checks passed",
    required: true,
    satisfied: true,
    detail: "Well-formed XHTML and expected taxonomy metadata present.",
  },
  {
    code: "accountant-signoff",
    label: "Named qualified-accountant sign-off",
    required: true,
    satisfied: false,
    detail: "Required before any filing pack is treated as final.",
  },
];

const reviewRows = [
  {
    id: "cash",
    cells: ["Cash at bank", "Bank statement", "EUR 125,000", "Reconciled"],
    searchText: "cash bank statement reconciled",
    tone: "good" as const,
  },
  {
    id: "debtors",
    cells: ["Trade debtors", "Aged debtors listing", <MoneyField key="debtors" value={18400} />, "Review ageing"],
    searchText: "trade debtors aged debtors review ageing",
    tone: "warn" as const,
  },
  {
    id: "approval",
    cells: ["Accountant approval", "Sign-off packet", "Required", "Blocked"],
    searchText: "accountant approval sign-off blocked",
    tone: "bad" as const,
  },
];

export function WorkbenchPreview() {
  const [balanceOutstanding, setBalanceOutstanding] = useState(40000);
  const [dueWithinYear, setDueWithinYear] = useState(10000);

  return (
    <WorkbenchShell>
      <WorkbenchHeader
        title="Workbench Component Preview"
        subtitle="Internal QA surface for accountant workflow primitives, dense review tables, evidence gates, route states and filing actions."
        meta={
          <>
            <StatusBadge tone="good">Light mode ready</StatusBadge>
            <StatusBadge tone="info">Dark mode ready</StatusBadge>
            <StatusBadge tone="warn">Accountant review required</StatusBadge>
          </>
        }
      />

      <WorkflowRail items={workflowItems} />

      <MetricStrip
        metrics={[
          { label: "Companies queued", value: "18" },
          { label: "Deadline exposure", value: "4", tone: "warn" },
          { label: "Ready packs", value: "7", tone: "good" },
          { label: "Manual handoffs", value: "2", tone: "bad" },
        ]}
      />

      <section className="grid min-w-0 gap-4 xl:grid-cols-[minmax(0,1.15fr)_minmax(20rem,0.85fr)]">
        <ReviewPanel
          title="Trial balance review"
          description="Dense tables keep source, amount, warning and reconciliation status visible."
          actions={<StatusBadge tone="warn">One blocker</StatusBadge>}
        >
          <DataTable
            caption="Trial balance review"
            filterPlaceholder="Filter review evidence"
            columns={["Area", "Evidence", "Amount", "Status"]}
            rows={reviewRows}
            totals={["Reviewed total", "", <MoneyField key="total" value={143400} />, "Difference EUR 0"]}
          />
        </ReviewPanel>

        <ReviewPanel
          title="Evidence checklist"
          description="Every statutory decision should show required evidence and completion state."
        >
          <EvidenceChecklist items={evidenceItems} />
        </ReviewPanel>
      </section>

      <ReviewPanel
        title="Accounting inputs"
        description="Money entry uses the same currency prefix, decimal keyboard hint and dense spacing across year-end forms."
        actions={<StatusBadge tone="info">MoneyInput primitive</StatusBadge>}
      >
        <div className="grid gap-3 md:grid-cols-3">
          <MoneyInput
            label="Balance outstanding"
            value={balanceOutstanding}
            onValueChange={setBalanceOutstanding}
            hint="Feeds creditors due within and after one year."
          />
          <MoneyInput
            label="Due within one year"
            value={dueWithinYear}
            onValueChange={setDueWithinYear}
          />
          <div className="rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3">
            <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">Derived long-term balance</p>
            <p className="mt-1 text-lg font-semibold text-[var(--foreground)]">
              <MoneyField value={Math.max(0, balanceOutstanding - dueWithinYear)} />
            </p>
          </div>
        </div>
      </ReviewPanel>

      <section className="grid min-w-0 gap-4 lg:grid-cols-2">
        <ReviewPanel
          title="Issue digest"
          description="Priority blockers should be compact, plain, and actionable."
        >
          <IssueDigest
            title="Readiness issue digest"
            description="Resolve before treating the generated accounts pack as final."
            blockers={[
              "Named qualified-accountant approval required.",
              "External ROS validation must be recorded.",
              "Director and secretary certification pending.",
              "Audit report handoff required for this path.",
            ]}
            warnings={[
              "Revenue filing deadline is inside the next 14 days.",
            ]}
          />
        </ReviewPanel>

        <ReviewPanel
          title="Route states"
          description="Loading, error, empty and permission states use one workbench language."
        >
          <div className="space-y-3">
            <WorkbenchLoadingState
              title="Loading review evidence"
              description="Preparing filing outputs and statutory evidence."
            />
            <WorkbenchErrorState
              title="Readiness profile unavailable"
              description="The API did not return a filing readiness profile."
            />
            <WorkbenchEmptyState
              title="No outputs generated"
              description="Generate the accounts PDF and iXBRL file before review."
            />
            <PermissionDeniedPanel />
          </div>
        </ReviewPanel>
      </section>

      <section className="space-y-3 pb-24">
        <SectionHeader
          eyebrow="Filing controls"
          title="Filing action bar"
          description="Final filing actions stay explicit, gated and visible without automating CRO or ROS submission."
        />
        <div className="[&>div]:static [&>div]:shadow-none">
          <FilingActionBar>
            <StatusBadge tone="bad">Approval blocked</StatusBadge>
            <button
              type="button"
              className="inline-flex min-h-10 items-center rounded-md border border-[var(--border)] bg-[var(--surface)] px-3 text-sm font-semibold text-[var(--foreground)] shadow-sm transition hover:bg-[var(--surface-subtle)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-emerald-500"
            >
              Record external ROS validation
            </button>
            <button
              type="button"
              className="inline-flex min-h-10 items-center rounded-md border border-emerald-700 bg-emerald-700 px-3 text-sm font-semibold text-white shadow-sm transition hover:bg-emerald-800 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-emerald-500"
            >
              Mark accountant approved
            </button>
          </FilingActionBar>
        </div>
      </section>
    </WorkbenchShell>
  );
}
