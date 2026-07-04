import {
  AlertTriangle,
  ArrowRight,
  CheckCircle2,
  ClipboardList,
  Download,
  Eye,
  FileText,
  Scale,
  Settings,
  Shield,
  Upload,
} from "lucide-react";
import Link from "next/link";
import type {
  AccountingPeriod,
  Company,
  FilingReadinessProfile,
  FilingWorkflowStatus,
  ReadinessScore,
  YearEndSummary,
} from "@/lib/api";
import { IssueDigest, MetricStrip, ReviewPanel, StatusBadge, WorkflowRail, type WorkflowItem } from "@/components/workbench";

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
  const setupComplete = Boolean(company?.legalName && company.companyType && (company.officers?.length ?? 0) > 0);
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
  const periodWorkspaceHref = `/companies/${companyId}/periods/${periodId}`;

  const workflowItems: WorkflowItem[] = [
    {
      id: "setup",
      label: "Setup",
      detail: setupComplete ? "Company profile and officers recorded" : "Company profile needs officer evidence",
      state: setupComplete ? "done" : "active",
      href: `/companies/${companyId}`,
      icon: <Shield className="h-4 w-4 shrink-0 text-emerald-600 dark:text-emerald-300" />,
    },
    {
      id: "import",
      label: "Import",
      detail: transactionTotal > 0 ? `${transactionTotal} transactions loaded` : "No transaction data",
      state: transactionTotal > 0 ? "done" : setupComplete ? "active" : "todo",
      href: `${periodWorkspaceHref}?tab=import`,
      icon: <Upload className="h-4 w-4 shrink-0 text-sky-600 dark:text-sky-300" />,
    },
    {
      id: "classify",
      label: "Classify",
      detail: period?.sizeClassification ? period.sizeClassification.calculatedClass : "Size/regime not complete",
      state: period?.sizeClassification ? "done" : transactionTotal > 0 ? "active" : "todo",
      href: `/companies/${companyId}/periods/${periodId}/classify`,
      icon: <Scale className="h-4 w-4 shrink-0 text-blue-600 dark:text-blue-300" />,
    },
    {
      id: "categorise",
      label: "Categorise",
      detail: transactionTotal > 0 ? `${uncategorisedCount} uncategorised` : "Waiting for import",
      state: transactionTotal === 0 ? "todo" : uncategorisedCount === 0 ? "done" : "active",
      href: `${periodWorkspaceHref}?tab=categorise`,
      icon: <Settings className="h-4 w-4 shrink-0 text-amber-600 dark:text-amber-300" />,
    },
    {
      id: "year-end",
      label: "Year-End",
      detail: yearEnd ? `${yearEndScore}% evidence complete` : "Questionnaire not reviewed",
      state: yearEndComplete ? "done" : period?.sizeClassification ? "active" : "todo",
      href: `/companies/${companyId}/periods/${periodId}/year-end`,
      icon: <ClipboardList className="h-4 w-4 shrink-0 text-purple-600 dark:text-purple-300" />,
    },
    {
      id: "statements",
      label: "Statements",
      detail: readiness?.balanceSheetBalances ? "Balance sheet agrees" : "Needs review",
      state: readiness?.balanceSheetBalances ? "done" : yearEndComplete ? "blocked" : "todo",
      href: `/companies/${companyId}/periods/${periodId}/statements`,
      icon: <FileText className="h-4 w-4 shrink-0 text-cyan-600 dark:text-cyan-300" />,
    },
    {
      id: "notes",
      label: "Notes",
      detail: notesEvidence.length > 0 ? `${notesEvidence.filter((item) => !item.satisfied).length} disclosure gates open` : "Required disclosures not generated",
      state: notesComplete ? "done" : readiness?.balanceSheetBalances ? "active" : "todo",
      href: `/companies/${companyId}/periods/${periodId}/notes`,
      icon: <Eye className="h-4 w-4 shrink-0 text-indigo-600 dark:text-indigo-300" />,
    },
    {
      id: "review",
      label: "Review",
      detail: readyToFile ? "Qualified-accountant gate cleared" : `${reviewIssueCount} review issues`,
      state: readyToFile ? "done" : reviewIssueCount > 0 ? "blocked" : "active",
      href: `${periodWorkspaceHref}?tab=filing`,
      icon: <CheckCircle2 className="h-4 w-4 shrink-0 text-emerald-600 dark:text-emerald-300" />,
    },
    {
      id: "filing",
      label: "Filing",
      detail: readyToFile ? "CRO pack ready" : `${filingStatus?.blockingIssues.length ?? 0} blockers - ${accountantReviewState}`,
      state: readyToFile ? "done" : "blocked",
      href: `${periodWorkspaceHref}?tab=filing`,
      icon: <Download className="h-4 w-4 shrink-0 text-emerald-600 dark:text-emerald-300" />,
    },
  ];
  const readyStages = workflowItems.filter((item) => item.state === "done");
  const nextAction = workflowItems.find((item) => item.state === "active")
    ?? workflowItems.find((item) => item.state === "blocked")
    ?? workflowItems.find((item) => item.state === "todo")
    ?? null;
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
  const commandCentreIssueClass = {
    bad: "text-red-800 dark:text-red-100",
    warn: "text-amber-800 dark:text-amber-100",
    good: "text-emerald-800 dark:text-emerald-100",
  }[commandCentreTone];
  const CommandCentreIssueIcon = commandCentreTone === "good" ? CheckCircle2 : AlertTriangle;

  return (
    <div className="mb-6 space-y-6">
      <ReviewPanel
        title="Period command centre"
        description="Current blocker, completed evidence and next workflow action for this accounting period."
        actions={<StatusBadge tone={commandCentreTone}>{commandCentre.blockerSummary}</StatusBadge>}
      >
        <div className="grid overflow-hidden rounded-md border border-[var(--border)] bg-[var(--surface)] md:grid-cols-3 md:divide-x md:divide-y-0 divide-y divide-[var(--border)]">
          <div className="min-w-0 p-4">
            <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">What is wrong?</p>
            <p className="mt-2 text-sm font-semibold text-[var(--foreground)]">{commandCentre.blockerSummary}</p>
            <div className={`mt-3 flex min-w-0 items-start gap-2 text-sm leading-6 ${commandCentreIssueClass}`}>
              <CommandCentreIssueIcon className="mt-1 h-4 w-4 shrink-0" />
              <span>{commandCentre.primaryIssue}</span>
            </div>
          </div>

          <div className="min-w-0 p-4">
            <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">What is ready?</p>
            <p className="mt-2 text-sm font-semibold text-[var(--foreground)]">{commandCentre.readySummary}</p>
            <p className="mt-3 text-sm leading-6 text-[var(--muted-foreground)]">{commandCentre.readyDetail}</p>
          </div>

          <div className="min-w-0 p-4">
            <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">What must I do next?</p>
            <p className="mt-2 text-sm font-semibold text-[var(--foreground)]">{commandCentre.nextActionLabel}</p>
            <p className="mt-3 text-sm leading-6 text-[var(--muted-foreground)]">{commandCentre.nextActionDetail}</p>
            {nextAction?.href && (
              <Link
                href={nextAction.href}
                className="mt-4 inline-flex min-h-8 items-center gap-2 rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] px-3 text-xs font-semibold text-[var(--foreground)] hover:border-[var(--ring)]"
              >
                Open {nextAction.label}
                <ArrowRight className="h-3.5 w-3.5" />
              </Link>
            )}
          </div>
        </div>
      </ReviewPanel>

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

      <WorkflowRail items={workflowItems} />

      <IssueDigest
        title="Readiness issue digest"
        description="Resolve priority blockers before treating the accounts pack as final."
        blockers={blockingIssues}
        warnings={warningIssues}
      />
    </div>
  );
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
