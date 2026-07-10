"use client";

import { useState } from "react";
import Link from "next/link";
import {
  DataGrid,
  EvidenceChecklist,
  FilingActionBar,
  IssueDigest,
  MetricStrip,
  MoneyField,
  MoneyInput,
  PageShell,
  PermissionDeniedPanel,
  ReadOnlyNotice,
  ReleaseBlockerSummary,
  ReviewPanel,
  SectionHeader,
  StatusBadge,
  WorkbenchEmptyState,
  WorkbenchErrorState,
  WorkbenchLoadingState,
  WorkflowRail,
} from "@/components/workbench";
import { AccountantWorkflowRail } from "@/components/workbench/AccountantWorkflowRail";
import { DashboardPracticeSummary } from "@/components/dashboard/DashboardPracticeSummary";
import { ResourceStateNotice } from "@/components/ResourceStateNotice";
import type { Company, FilingDeadline } from "@/lib/api";
import type { ResourceState } from "@/lib/resourceState";

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

const releaseBlockers = [
  {
    code: "accountant-signoff",
    trackCode: "backend-code",
    trackLabel: "Backend code",
    severity: "critical",
    riskRank: 1,
    blockingIssue: "Qualified accountant sign-off required",
    evidenceArtifact: "named-accountant-approval-record",
    nextAction: "Run qualified-accountant acceptance on the golden corpus.",
    blocksRelease: true,
  },
  {
    code: "visual-regression-review",
    trackCode: "frontend-ui-ux",
    trackLabel: "Frontend UI/UX",
    severity: "high",
    riskRank: 7,
    blockingIssue: "Light and dark visual regression review required",
    evidenceArtifact: "light-dark-mobile-tablet-desktop-screenshot-review",
    nextAction: "Complete seeded production screenshot review for every main route.",
    blocksRelease: true,
  },
  {
    code: "backup-restore-drill",
    trackCode: "operations",
    trackLabel: "Operations",
    severity: "high",
    riskRank: 11,
    blockingIssue: "Backup restore drill evidence required",
    evidenceArtifact: "production-backup-restore-drill-log",
    nextAction: "Run the restore drill and attach the generated verification log.",
    blocksRelease: true,
  },
];

const dashboardCompanies: Company[] = [
  {
    id: 101,
    legalName: "Preview Micro Limited",
    companyType: "Private",
    incorporationDate: "2023-01-01",
    financialYearStartMonth: 1,
    annualReturnDate: "2026-09-15",
    isGroupMember: false,
    isHolding: false,
    isInvestment: false,
    isSubsidiary: false,
    isDormant: false,
    isTrading: true,
    isVatRegistered: false,
    isEmployer: false,
    hasStock: false,
    ownsAssets: false,
    hasBorrowings: false,
    hasDirectorLoans: false,
    isListedSecurities: false,
    isCreditInstitution: false,
    isInsuranceUndertaking: false,
    isPensionFund: false,
    isCharitableOrganisation: false,
    assignedReviewerName: "Niamh Reviewer",
    assignedReviewerEmail: "niamh.reviewer@example.ie",
    periodCount: 2,
    latestPeriod: {
      id: 201,
      companyId: 101,
      periodStart: "2026-01-01",
      periodEnd: "2026-12-31",
      status: "Review",
      isFirstYear: false,
      memberAuditNoticeReceived: false,
      goingConcernConfirmed: true,
    },
  },
  {
    id: 102,
    legalName: "Preview Charity CLG",
    companyType: "CompanyLimitedByGuarantee",
    incorporationDate: "2022-01-01",
    financialYearStartMonth: 1,
    annualReturnDate: "2026-08-10",
    isGroupMember: false,
    isHolding: false,
    isInvestment: false,
    isSubsidiary: false,
    isDormant: false,
    isTrading: true,
    isVatRegistered: false,
    isEmployer: true,
    hasStock: false,
    ownsAssets: false,
    hasBorrowings: false,
    hasDirectorLoans: false,
    isListedSecurities: false,
    isCreditInstitution: false,
    isInsuranceUndertaking: false,
    isPensionFund: false,
    isCharitableOrganisation: true,
    assignedReviewerName: "Oisin Reviewer",
    assignedReviewerEmail: "oisin.reviewer@example.ie",
    periodCount: 1,
    latestPeriod: {
      id: 202,
      companyId: 102,
      periodStart: "2026-01-01",
      periodEnd: "2026-12-31",
      status: "Review",
      isFirstYear: false,
      memberAuditNoticeReceived: false,
      goingConcernConfirmed: true,
    },
  },
  {
    id: 103,
    legalName: "Preview Dormant Limited",
    companyType: "Private",
    incorporationDate: "2024-01-01",
    financialYearStartMonth: 1,
    annualReturnDate: "2026-10-31",
    isGroupMember: false,
    isHolding: false,
    isInvestment: false,
    isSubsidiary: false,
    isDormant: true,
    isTrading: false,
    isVatRegistered: false,
    isEmployer: false,
    hasStock: false,
    ownsAssets: false,
    hasBorrowings: false,
    hasDirectorLoans: false,
    isListedSecurities: false,
    isCreditInstitution: false,
    isInsuranceUndertaking: false,
    isPensionFund: false,
    isCharitableOrganisation: false,
    assignedReviewerName: undefined,
    assignedReviewerEmail: undefined,
    periodCount: 1,
    latestPeriod: {
      id: 203,
      companyId: 103,
      periodStart: "2026-01-01",
      periodEnd: "2026-12-31",
      status: "Draft",
      isFirstYear: false,
      memberAuditNoticeReceived: false,
      goingConcernConfirmed: false,
    },
  },
];

const dashboardDeadlines: Record<number, FilingDeadline | null> = {
  101: {
    id: 301,
    companyId: 101,
    periodId: 201,
    deadlineType: "CRO",
    calculatedDueDate: "2026-07-10",
    dueDate: "2026-07-10",
    isLate: false,
    penaltyAmount: 0,
  },
  102: {
    id: 302,
    companyId: 102,
    periodId: 202,
    deadlineType: "Revenue",
    calculatedDueDate: "2026-06-20",
    dueDate: "2026-06-20",
    isLate: true,
    penaltyAmount: 100,
  },
  103: null,
};

const canonicalVisualStateIds = new Set([
  "loading",
  "empty",
  "maximum-data",
  "error",
  "partial-error",
  "permission-denied",
  "read-only",
  "stale",
  "conflict",
]);

const maximumDataRows = Array.from({ length: 48 }, (_, index) => ({
  id: `maximum-data-${index + 1}`,
  cells: [
    `Account ${String(index + 1).padStart(3, "0")}`,
    index % 3 === 0 ? "Imported transaction" : index % 3 === 1 ? "Year-end adjustment" : "Opening balance",
    <MoneyField key={`maximum-data-amount-${index + 1}`} value={(index + 1) * 137.25} />,
    index % 4 === 0 ? "Review required" : "Evidence retained",
  ],
  searchText: `account ${index + 1} maximum data evidence review`,
  tone: index % 4 === 0 ? "warn" as const : "good" as const,
}));

const partialErrorState: ResourceState = {
  status: "partial-error",
  error: "The filing readiness profile could not be refreshed.",
  failedResourceKeys: ["filing-readiness-profile"],
  hasRetainedData: true,
};

const staleState: ResourceState = {
  status: "stale/retrying",
  error: null,
  failedResourceKeys: [],
  hasRetainedData: true,
};

export function WorkbenchPreview({ canonicalState }: { canonicalState?: string }) {
  const [balanceOutstanding, setBalanceOutstanding] = useState(40000);
  const [dueWithinYear, setDueWithinYear] = useState(10000);

  if (canonicalState && canonicalVisualStateIds.has(canonicalState)) {
    return <CanonicalVisualState state={canonicalState} />;
  }

  return (
    <PageShell
      title="Workbench Component Preview"
      subtitle="Internal QA surface for accountant workflow primitives, dense review tables, evidence gates, route states and filing actions."
      backHref="/"
      backLabel="Dashboard"
      meta={
        <>
          <StatusBadge tone="good">Light mode ready</StatusBadge>
          <StatusBadge tone="info">Dark mode ready</StatusBadge>
          <StatusBadge tone="warn">Accountant review required</StatusBadge>
          <StatusBadge tone="default">PageShell primitive</StatusBadge>
        </>
      }
    >
      <WorkflowRail items={workflowItems} />
      <AccountantWorkflowRail activeStage="Review" />

      <MetricStrip
        metrics={[
          { label: "Companies queued", value: "18" },
          { label: "Deadline exposure", value: "4", tone: "warn" },
          { label: "Ready packs", value: "7", tone: "good" },
          { label: "Manual handoffs", value: "2", tone: "bad" },
        ]}
      />

      <DashboardPracticeSummary
        companies={dashboardCompanies}
        deadlines={dashboardDeadlines}
        today="2026-07-03"
      />

      <ReleaseBlockerSummary blockers={releaseBlockers} />

      <section className="grid min-w-0 gap-4 xl:grid-cols-[minmax(0,1.15fr)_minmax(20rem,0.85fr)]">
        <ReviewPanel
          title="Trial balance review"
          description="Dense tables keep source, amount, warning and reconciliation status visible."
          actions={<StatusBadge tone="warn">One blocker</StatusBadge>}
        >
          <DataGrid
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
            <ReadOnlyNotice subject="year-end working papers" />
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
            <span
              aria-label="Record external ROS validation preview"
              className="inline-flex min-h-10 items-center rounded-md border border-[var(--border)] bg-[var(--surface)] px-3 text-sm font-semibold text-[var(--foreground)] shadow-sm"
            >
              Record external ROS validation
            </span>
            <span
              aria-label="Mark accountant approved preview"
              className="inline-flex min-h-10 items-center rounded-md border border-emerald-700 bg-emerald-700 px-3 text-sm font-semibold text-white shadow-sm"
            >
              Mark accountant approved
            </span>
          </FilingActionBar>
        </div>
      </section>
    </PageShell>
  );
}

function CanonicalVisualState({ state }: { state: string }) {
  const content = (() => {
    switch (state) {
      case "loading":
        return (
          <WorkbenchLoadingState
            title="Loading canonical accountant workspace"
            description="Preparing deterministic statutory evidence for responsive visual review."
          />
        );
      case "empty":
        return (
          <WorkbenchEmptyState
            title="No canonical accounting records"
            description="The empty state keeps the next safe accountant action visible without implying that filing evidence exists."
            actions={(
              <Link className="text-sm font-semibold text-emerald-700 underline dark:text-emerald-300" href="/companies/new">
                Start company onboarding
              </Link>
            )}
          />
        );
      case "maximum-data":
        return (
          <ReviewPanel
            title="Maximum-data review table"
            description="Forty-eight deterministic rows exercise dense scanning, internal scrolling and responsive labels."
            actions={<StatusBadge tone="warn">48 rows</StatusBadge>}
          >
            <DataGrid
              caption="Maximum-data accounting evidence"
              filterPlaceholder="Filter maximum-data evidence"
              columns={["Account", "Source", "Amount", "Review state"]}
              rows={maximumDataRows}
              totals={["Maximum-data total", "", <MoneyField key="maximum-data-total" value={161406} />, "Human review required"]}
            />
          </ReviewPanel>
        );
      case "error":
        return (
          <WorkbenchErrorState
            title="Canonical workspace could not be loaded"
            description="No retained evidence is available. Retry before making an accounting or filing decision."
            onRetry={() => undefined}
          />
        );
      case "partial-error":
        return (
          <ReviewPanel
            title="Retained evidence with a failed resource"
            description="Previously loaded figures remain visible but cannot support a new professional confirmation."
          >
            <ResourceStateNotice state={partialErrorState} label="Filing evidence" onRetry={() => undefined} />
            <div className="mt-3 rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3 text-sm text-[var(--foreground)]">
              Retained trial-balance total: <MoneyField value={482750} />
            </div>
          </ReviewPanel>
        );
      case "permission-denied":
        return (
          <PermissionDeniedPanel
            title="Permission denied"
            description="This role cannot approve statutory outputs or record an external filing outcome."
            actions={(
              <Link className="text-sm font-semibold text-amber-900 underline dark:text-amber-100" href="/">
                Return to permitted work
              </Link>
            )}
          />
        );
      case "read-only":
        return (
          <ReviewPanel
            title="Reviewer evidence view"
            description="The retained working papers remain visible without presenting mutating controls."
          >
            <ReadOnlyNotice
              subject="year-end working papers"
              detail="A Reviewer can inspect this evidence; editing requires Owner or Accountant access."
            />
          </ReviewPanel>
        );
      case "stale":
        return (
          <ReviewPanel
            title="Retained statement evidence"
            description="The prior result stays visible while the latest request is in flight."
          >
            <ResourceStateNotice state={staleState} label="statement evidence" />
            <div className="mt-3 rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3 text-sm text-[var(--foreground)]">
              Retained balance-sheet total: <MoneyField value={725000} />
            </div>
          </ReviewPanel>
        );
      case "conflict":
        return (
          <WorkbenchErrorState
            title="Accounting record changed by another reviewer"
            description="Reload the latest retained version before retrying; the stale write remains blocked."
            retryLabel="Reload latest version"
            onRetry={() => undefined}
          />
        );
      default:
        return null;
    }
  })();

  return (
    <PageShell
      title={`Canonical ${state} state`}
      subtitle="Deterministic visual-QA fixture. This surface demonstrates presentation only and does not record accounting or filing decisions."
      backHref="/workbench-preview"
      backLabel="Workbench preview"
      meta={<StatusBadge tone="warn">Named human review required</StatusBadge>}
    >
      {content}
    </PageShell>
  );
}
