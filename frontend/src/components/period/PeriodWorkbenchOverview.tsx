import { ArrowRight } from "lucide-react";
import Link from "next/link";
import type {
  AccountingPeriod,
  Company,
  FilingReadinessProfile,
  FilingWorkflowStatus,
  ReadinessScore,
  YearEndSummary,
} from "@/lib/api";
import { IssueDigest, MetricStrip, ReviewPanel, StatusBadge, WorkflowDecisionSummary, type WorkflowItem } from "@/components/workbench";
import { AccountantWorkflowRail, type AccountantWorkflowStage } from "@/components/workbench/AccountantWorkflowRail";

interface PeriodWorkbenchOverviewProps {
  companyId: number | string;
  periodId: number | string;
  company: Company | null;
  period: AccountingPeriod | null;
  yearEnd: YearEndSummary | null;
  readiness: ReadinessScore | null;
  filingStatus: FilingWorkflowStatus | null;
  filingReadinessProfile: FilingReadinessProfile | null;
  transactionTotal: number;
  categorisedCount: number;
  pendingAdjustments?: number;
}

export function PeriodWorkbenchOverview({
  companyId,
  periodId,
  company,
  period,
  yearEnd,
  readiness,
  filingStatus,
  filingReadinessProfile,
  transactionTotal,
  categorisedCount,
  pendingAdjustments = 0,
}: PeriodWorkbenchOverviewProps) {
  const uncategorisedCount = Math.max(transactionTotal - categorisedCount, 0);
  const readyToFile = filingStatus?.readyToFile ?? false;
  const activeOfficers = company?.officers?.filter((officer) => !officer.resignedDate) ?? [];
  const hasDirector = activeOfficers.some((officer) => officerRole(officer.role).includes("director"));
  const hasSecretary = activeOfficers.some((officer) => officerRole(officer.role).includes("secretary"));
  const setupComplete = Boolean(company?.legalName && company.companyType && hasDirector && hasSecretary);
  const yearEndComplete = Boolean(yearEnd && yearEnd.completeness.incomplete.length === 0);
  const yearEndScore = yearEnd?.completeness.score ?? 0;
  const notesEvidence = filingReadinessProfile?.requiredEvidence.filter((item) => {
    const haystack = `${item.code} ${item.label}`.toLowerCase();
    return haystack.includes("note") || haystack.includes("disclosure");
  }) ?? [];
  const notesComplete = notesEvidence.length > 0 && notesEvidence.every((item) => item.satisfied);
  const reviewIssueCount = filingReadinessProfile
    ? filingReadinessProfile.blockingIssues.length + filingReadinessProfile.warningIssues.length
    : filingStatus?.blockingIssues.length ?? 0;
  const accountantReviewState = filingReadinessProfile?.accountantReviewState ?? "Not started";
  const blockingIssues = uniqueIssues([
    ...(filingStatus?.blockingIssues ?? []),
    ...(filingReadinessProfile?.blockingIssues.map((issue) => issue.message) ?? []),
  ]);
  const warningIssues = uniqueIssues([
    ...(filingStatus?.warningIssues ?? []),
    ...(filingReadinessProfile?.warningIssues.map((issue) => issue.message) ?? []),
    ...(readiness?.warnings ?? []),
  ]);
  const issueSources = [
    {
      label: "Filing workflow",
      blockers: filingStatus?.blockingIssues.length ?? 0,
      warnings: filingStatus?.warningIssues.length ?? 0,
      detail: "Generated output and workflow-state blockers from CRO, Revenue and charity filing status.",
    },
    {
      label: "Filing readiness profile",
      blockers: filingReadinessProfile?.blockingIssues.length ?? 0,
      warnings: filingReadinessProfile?.warningIssues.length ?? 0,
      detail: "Source-backed professional review, sign-off packet and allowed-action gates.",
    },
    {
      label: "Statutory readiness score",
      blockers: readiness?.missingItems.length ?? 0,
      warnings: readiness?.warnings.length ?? 0,
      detail: "Accounts readiness checks from statements, balances and statutory warning signals.",
    },
  ];
  const periodWorkspaceHref = `/companies/${companyId}/periods/${periodId}`;

  const workflowItems: WorkflowItem[] = [
    {
      id: "setup",
      label: "Setup",
      detail: setupComplete ? "Company profile and officers recorded" : "Director and secretary evidence required",
      state: setupComplete ? "done" : "active",
      href: `/companies/${companyId}`,
    },
    {
      id: "import",
      label: "Import",
      detail: transactionTotal > 0 ? `${transactionTotal} transactions loaded` : "No transaction data",
      state: transactionTotal > 0 ? "done" : setupComplete ? "active" : "todo",
      href: `${periodWorkspaceHref}?tab=import`,
    },
    {
      id: "classify",
      label: "Classify",
      detail: period?.sizeClassification ? period.sizeClassification.calculatedClass : "Size/regime not complete",
      state: period?.sizeClassification ? "done" : transactionTotal > 0 ? "active" : "todo",
      href: `/companies/${companyId}/periods/${periodId}/classify`,
    },
    {
      id: "categorise",
      label: "Categorise",
      detail: transactionTotal > 0 ? `${uncategorisedCount} uncategorised` : "Waiting for import",
      state: transactionTotal === 0 ? "todo" : uncategorisedCount === 0 ? "done" : "active",
      href: `${periodWorkspaceHref}?tab=categorise`,
    },
    {
      id: "year-end",
      label: "Year-End",
      detail: yearEnd ? `${yearEndScore}% evidence complete` : "Questionnaire not reviewed",
      state: yearEndComplete ? "done" : period?.sizeClassification ? "active" : "todo",
      href: `/companies/${companyId}/periods/${periodId}/year-end`,
    },
    {
      id: "statements",
      label: "Statements",
      detail: readiness?.balanceSheetBalances ? "Balance sheet agrees" : "Needs review",
      state: readiness?.balanceSheetBalances ? "done" : yearEndComplete ? "blocked" : "todo",
      href: `/companies/${companyId}/periods/${periodId}/statements`,
    },
    {
      id: "notes",
      label: "Notes",
      detail: notesEvidence.length > 0 ? `${notesEvidence.filter((item) => !item.satisfied).length} disclosure gates open` : "Required disclosures not generated",
      state: notesComplete ? "done" : readiness?.balanceSheetBalances ? "active" : "todo",
      href: `/companies/${companyId}/periods/${periodId}/notes`,
    },
    {
      id: "review",
      label: "Review",
      detail: readyToFile ? "Qualified-accountant gate cleared" : `${reviewIssueCount} review issues`,
      state: readyToFile ? "done" : reviewIssueCount > 0 ? "blocked" : "active",
      href: `${periodWorkspaceHref}?tab=filing`,
    },
    {
      id: "filing",
      label: "Filing",
      detail: readyToFile ? "CRO pack ready" : `${filingStatus?.blockingIssues.length ?? 0} blockers - ${accountantReviewState}`,
      state: readyToFile ? "done" : "blocked",
      href: `${periodWorkspaceHref}?tab=filing`,
    },
  ];
  const workflowById = new Map(workflowItems.map((item) => [item.id, item]));
  const readyStages = workflowItems.filter((item) => item.state === "done");
  const unresolvedWorkflowItems = workflowItems.filter((item) => item.state !== "done");
  const nextAction = workflowItems.find((item) => item.state === "active")
    ?? workflowItems.find((item) => item.state === "blocked")
    ?? workflowItems.find((item) => item.state === "todo")
    ?? null;
  const activeWorkflowStage = activeStageForNextAction(nextAction);
  const commandCentre = {
    blockerSummary: blockingIssues.length > 0
      ? `${blockingIssues.length} ${blockingIssues.length === 1 ? "blocker requires" : "blockers require"} attention`
      : warningIssues.length > 0
        ? `${warningIssues.length} ${warningIssues.length === 1 ? "warning needs" : "warnings need"} review`
        : "No priority blockers",
    primaryIssue: blockingIssues[0] ?? warningIssues[0] ?? "Ready for final review",
    readySummary: `${readyStages.length} ${readyStages.length === 1 ? "stage" : "stages"} ready`,
    readyDetail: readyStages.length > 0 ? readyStages.map((item) => item.label).join(", ") : "No workflow stages are complete yet",
    nextActionLabel: nextAction?.label ?? "Final review",
    nextActionDetail: nextActionDetail(nextAction, uncategorisedCount),
  };
  const commandCentreTone = blockingIssues.length > 0 ? "bad" : warningIssues.length > 0 ? "warn" : "good";
  const filingReviewHref = `${periodWorkspaceHref}?tab=filing`;
  const filingGatePathLabel = filingReadinessProfile?.manualProfessionalReviewRequired
    ? "Manual professional review"
    : filingReadinessProfile?.supportedPath
      ? "Supported filing path"
      : "Manual handoff";
  const filingGateTone = filingReadinessProfile?.signOffPacket.readyForExternalFiling
    ? "good"
    : filingReadinessProfile?.manualProfessionalReviewRequired
      ? "bad"
      : "warn";
  const externalFilingState = filingReadinessProfile?.signOffPacket.readyForExternalFiling
    ? "External filing ready"
    : "External filing blocked";
  const allowedNextActions = filingReadinessProfile?.allowedNextActions.length
    ? filingReadinessProfile.allowedNextActions.join(", ")
    : "No allowed filing action";

  return (
    <div className="mb-6 min-w-0 max-w-full space-y-6 overflow-x-clip">
      <ReviewPanel
        title="Period command centre"
        description="Current blocker, completed evidence and next workflow action for this accounting period."
        actions={<StatusBadge tone={commandCentreTone}>{commandCentre.blockerSummary}</StatusBadge>}
      >
        <WorkflowDecisionSummary
          items={[
            {
              title: "What is wrong?",
              tone: commandCentreTone,
              summary: commandCentre.blockerSummary,
              detail: commandCentre.primaryIssue,
            },
            {
              title: "What is ready?",
              tone: readyStages.length > 0 ? "good" : "warn",
              summary: commandCentre.readySummary,
              detail: commandCentre.readyDetail,
            },
            {
              title: "What must I do next?",
              tone: nextAction?.state === "blocked" ? "bad" : nextAction?.state === "active" ? "info" : "warn",
              summary: commandCentre.nextActionLabel,
              detail: commandCentre.nextActionDetail,
              action: nextAction?.href ? { href: nextAction.href, label: `Open ${nextAction.label}` } : undefined,
            },
          ]}
        />
      </ReviewPanel>

      <ReviewPanel
        title="Filing gate snapshot"
        description="Compact review state for accountant sign-off, external filing evidence and the next allowed filing workflow action."
        actions={<StatusBadge tone={filingGateTone}>{filingGatePathLabel}</StatusBadge>}
      >
        <div className="grid gap-3 rounded-md border border-[var(--border)] bg-[var(--surface)] p-3 md:grid-cols-[minmax(0,0.8fr)_minmax(0,0.8fr)_minmax(0,0.8fr)_minmax(0,1fr)_auto] md:items-center">
          <GateSnapshotItem label="Path" value={filingGatePathLabel} />
          <GateSnapshotItem label="Accountant review" value={accountantReviewState} />
          <GateSnapshotItem label="External filing" value={externalFilingState} />
          <GateSnapshotItem label="Allowed next action" value={allowedNextActions} />
          <Link
            href={filingReviewHref}
            className="inline-flex min-h-9 items-center justify-center gap-2 rounded-md border border-[var(--control-border)] bg-[var(--surface-subtle)] px-3 text-xs font-semibold text-[var(--foreground)] hover:border-[var(--ring)]"
          >
            Open filing review
            <ArrowRight className="h-3.5 w-3.5" />
          </Link>
        </div>
      </ReviewPanel>

      <WorkflowActionQueue items={unresolvedWorkflowItems} uncategorisedCount={uncategorisedCount} />

      <MetricStrip
        metrics={[
          {
            label: "Filing readiness",
            value: `${readiness?.filingReadinessPercent ?? 0}%`,
            tone: (readiness?.filingReadinessPercent ?? 0) >= 90 ? "good" : "warn",
          },
          {
            label: "Transactions",
            value: transactionTotal,
            tone: transactionTotal > 0 ? "good" : "warn",
          },
          {
            label: "Uncategorised",
            value: uncategorisedCount,
            tone: uncategorisedCount === 0 ? "good" : "bad",
          },
          {
            label: "Adjustments pending",
            value: pendingAdjustments,
            tone: pendingAdjustments === 0 ? "good" : "warn",
          },
        ]}
      />

      <ReviewPanel
        title="Issue source breakdown"
        description="Where the current blockers are coming from, so reviewers know which workbench surface to fix first."
        actions={<StatusBadge tone={blockingIssues.length > 0 ? "bad" : warningIssues.length > 0 ? "warn" : "good"}>{blockingIssues.length + warningIssues.length} total issues</StatusBadge>}
      >
        <div className="grid gap-3 md:grid-cols-3">
          {issueSources.map((source) => (
            <div key={source.label} className="rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3">
              <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">{source.label}</p>
              <p className="mt-2 text-sm font-semibold text-[var(--foreground)]">
                {formatIssueSourceCount(source.blockers, "blocker")} / {formatIssueSourceCount(source.warnings, "warning")}
              </p>
              <p className="mt-2 text-xs leading-5 text-[var(--muted-foreground)]">{source.detail}</p>
            </div>
          ))}
        </div>
      </ReviewPanel>

      <AccountantWorkflowRail
        activeStage={activeWorkflowStage}
        stageOverrides={{
          setup: pickWorkflowRailFields(workflowById.get("setup")),
          import: nextAction?.id === "categorise"
            ? pickWorkflowRailFields(nextAction, "active")
            : pickWorkflowRailFields(workflowById.get("import")),
          classify: pickWorkflowRailFields(workflowById.get("classify")),
          "year-end": pickWorkflowRailFields(workflowById.get("year-end")),
          statements: pickWorkflowRailFields(workflowById.get("statements")),
          notes: pickWorkflowRailFields(workflowById.get("notes")),
          review: pickWorkflowRailFields(workflowById.get("review")),
          filing: pickWorkflowRailFields(workflowById.get("filing")),
        }}
      />

      <IssueDigest
        title="Readiness issue digest"
        description="Resolve priority blockers before treating the accounts pack as final."
        blockers={blockingIssues}
        warnings={warningIssues}
      />
    </div>
  );
}

function WorkflowActionQueue({
  items,
  uncategorisedCount,
}: {
  items: WorkflowItem[];
  uncategorisedCount: number;
}) {
  const visibleItems = items.slice(0, 3);

  return (
    <section
      aria-label="Period workflow action queue"
      className="rounded-md border border-[var(--border)] bg-[var(--surface)]"
    >
      <div className="flex flex-col gap-2 border-b border-[var(--border)] px-3 py-3 md:flex-row md:items-center md:justify-between">
        <div className="min-w-0">
          <h3 className="text-sm font-semibold text-[var(--foreground)]">Period workflow action queue</h3>
          <p className="mt-1 text-xs leading-5 text-[var(--muted-foreground)]">
            First unresolved steps for this period.
          </p>
        </div>
        <StatusBadge tone={items.length > 0 ? "warn" : "good"}>
          {formatWorkflowActionCount(items.length)}
        </StatusBadge>
      </div>
      <div className="divide-y divide-[var(--border)]">
        {visibleItems.map((item) => (
          <div
            key={item.id}
            className="grid gap-3 px-3 py-3 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.6fr)_auto] lg:items-center"
          >
            <div className="min-w-0">
              <div className="flex min-w-0 flex-wrap items-center gap-2">
                <p className="truncate text-sm font-semibold text-[var(--foreground)]">{item.label}</p>
                <StatusBadge tone={workflowItemTone(item.state)}>{workflowStateLabel(item.state)}</StatusBadge>
              </div>
              <p className="mt-1 text-xs leading-5 text-[var(--muted-foreground)]">
                {workflowQueueDetail(item, uncategorisedCount)}
              </p>
            </div>
            <div className="min-w-0">
              <p className="text-[11px] font-semibold uppercase text-[var(--muted-foreground)]">Review cue</p>
              <p className="mt-1 text-sm font-medium leading-5 text-[var(--foreground)]">
                {workflowReviewCue(item)}
              </p>
            </div>
            {item.href && (
              <Link
                href={item.href}
                className="inline-flex min-h-9 items-center justify-center gap-2 rounded-md border border-[var(--control-border)] bg-[var(--surface-subtle)] px-3 text-xs font-semibold text-[var(--foreground)] hover:border-[var(--ring)]"
              >
                Open {item.label}
                <ArrowRight className="h-3.5 w-3.5" />
              </Link>
            )}
          </div>
        ))}
      </div>
    </section>
  );
}

function GateSnapshotItem({ label, value }: { label: string; value: string }) {
  return (
    <div className="min-w-0">
      <p className="text-[11px] font-semibold uppercase text-[var(--muted-foreground)]">{label}</p>
      <p className="mt-1 break-words text-sm font-semibold leading-5 text-[var(--foreground)]">{value}</p>
    </div>
  );
}

function formatWorkflowActionCount(count: number) {
  return `${count} ${count === 1 ? "open action" : "open actions"}`;
}

function workflowItemTone(state: WorkflowItem["state"]) {
  if (state === "blocked") return "bad";
  if (state === "active") return "info";
  if (state === "todo") return "warn";
  return "good";
}

function workflowStateLabel(state: WorkflowItem["state"]) {
  if (state === "active") return "Active";
  if (state === "blocked") return "Blocked";
  if (state === "todo") return "Pending";
  return "Complete";
}

function workflowReviewCue(item: WorkflowItem) {
  if (item.state === "blocked") return "Resolve blockers before approval.";
  if (item.state === "active") return "This is the next workbench step.";
  if (item.state === "todo") return "Waiting for earlier evidence.";
  return "Evidence complete.";
}

function workflowQueueDetail(item: WorkflowItem, uncategorisedCount: number) {
  if (item.id === "categorise" && uncategorisedCount > 0) {
    return `${uncategorisedCount} uncategorised ${uncategorisedCount === 1 ? "transaction" : "transactions"}`;
  }

  return item.detail;
}

function pickWorkflowRailFields(item?: WorkflowItem, stateOverride?: WorkflowItem["state"]) {
  return item ? { detail: item.detail, href: item.href, state: stateOverride ?? item.state } : undefined;
}

function uniqueIssues(issues: string[]) {
  return Array.from(new Set(issues.filter(Boolean)));
}

function nextActionDetail(nextAction: WorkflowItem | null, uncategorisedCount: number) {
  if (!nextAction) return "No workflow action is currently available.";
  if (nextAction.id === "categorise" && uncategorisedCount > 0) {
    return `${uncategorisedCount} uncategorised ${uncategorisedCount === 1 ? "transaction" : "transactions"}`;
  }

  return nextAction.detail;
}

function formatIssueSourceCount(count: number, label: "blocker" | "warning") {
  return `${count} ${count === 1 ? label : `${label}s`}`;
}

function activeStageForNextAction(nextAction: WorkflowItem | null): AccountantWorkflowStage {
  switch (nextAction?.id) {
    case "setup":
      return "Setup";
    case "import":
    case "categorise":
      return "Import";
    case "classify":
      return "Classify";
    case "year-end":
      return "Year-End";
    case "statements":
      return "Statements";
    case "notes":
      return "Notes";
    case "review":
      return "Review";
    case "filing":
      return "Filing";
    default:
      return "Review";
  }
}

function officerRole(role: string) {
  return role.toLowerCase().replace(/[\s_-]/g, "");
}
