import {
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
import type {
  AccountingPeriod,
  Company,
  FilingReadinessProfile,
  FilingWorkflowStatus,
  ReadinessScore,
  YearEndSummary,
} from "@/lib/api";
import { IssueDigest, MetricStrip, WorkflowRail, type WorkflowItem } from "@/components/workbench";

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
      icon: <CheckCircle2 className="h-4 w-4 shrink-0 text-emerald-600 dark:text-emerald-300" />,
    },
    {
      id: "filing",
      label: "Filing",
      detail: readyToFile ? "CRO pack ready" : `${filingStatus?.blockingIssues.length ?? 0} blockers - ${accountantReviewState}`,
      state: readyToFile ? "done" : "blocked",
      icon: <Download className="h-4 w-4 shrink-0 text-emerald-600 dark:text-emerald-300" />,
    },
  ];

  return (
    <div className="mb-6 space-y-6">
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
