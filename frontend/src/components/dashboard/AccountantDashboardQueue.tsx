import Link from "next/link";
import { AlertTriangle, ArrowRight, CalendarClock, UserRound } from "lucide-react";
import type { Company, FilingDeadline } from "@/lib/api";
import { formatCompanyType, formatDateIE } from "@/lib/format";
import { DataTable, MetricStrip, ReviewPanel, StatusBadge } from "@/components/workbench";
import { AccountantWorkflowRail } from "@/components/workbench/AccountantWorkflowRail";

interface AccountantDashboardQueueProps {
  companies: Company[];
  deadlines: Record<number, FilingDeadline | null>;
  today?: string;
}

type QueueTone = "default" | "good" | "warn" | "bad" | "info";

interface QueueRow {
  company: Company;
  periodId: number | null;
  deadline: FilingDeadline | null;
  deadlineLabel: string;
  deadlineState: string;
  deadlineTone: QueueTone;
  blockerLabel: string;
  blockerDetail: string;
  blockerTone: QueueTone;
  nextActionLabel: string;
  nextActionHref: string;
}

export function AccountantDashboardQueue({
  companies,
  deadlines,
  today,
}: AccountantDashboardQueueProps) {
  const todayDate = parseDate(today) ?? new Date();
  const rows = companies
    .map((company) => buildQueueRow(company, deadlines[company.id] ?? null, todayDate))
    .sort(compareQueueRows);
  const urgentCount = rows.filter((row) => row.deadlineTone === "bad" || row.blockerTone === "bad").length;
  const dueSoonCount = rows.filter((row) => row.deadlineState === "Due soon").length;
  const manualHandoffCount = rows.filter((row) => row.blockerLabel === "Manual handoff").length;
  const unassignedReviewerCount = rows.filter((row) => !row.company.assignedReviewerName?.trim()).length;

  return (
    <ReviewPanel
      title="Accountant Work Queue"
      description="Active production work across the firm."
      actions={<StatusBadge tone={urgentCount > 0 ? "bad" : "good"}>{urgentCount} urgent total</StatusBadge>}
    >
      {rows.length === 0 ? (
        <div className="rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-4 text-sm text-[var(--muted-foreground)]">
          No companies are currently visible to this user.
        </div>
      ) : (
        <div className="space-y-4">
          <AccountantWorkflowRail activeStage={dashboardActiveStage(rows)} />
          <MetricStrip
            metrics={[
              {
                label: "Urgent clients",
                value: formatQueueCount(urgentCount, "urgent", "urgent"),
                tone: urgentCount > 0 ? "bad" : "good",
              },
              {
                label: "Due-soon deadlines",
                value: formatQueueCount(dueSoonCount, "deadline", "deadlines"),
                tone: dueSoonCount > 0 ? "warn" : "good",
              },
              {
                label: "Manual handoffs",
                value: formatQueueCount(manualHandoffCount, "handoff", "handoffs"),
                tone: manualHandoffCount > 0 ? "bad" : "good",
              },
              {
                label: "Unassigned reviewers",
                value: formatQueueCount(unassignedReviewerCount, "unassigned", "unassigned"),
                tone: unassignedReviewerCount > 0 ? "warn" : "good",
              },
            ]}
          />
          <QueueTriage row={rows[0]} />
          <DataTable
            caption="Accountant work queue"
            filterPlaceholder="Filter companies, blockers, reviewers or actions"
            emptyState="No matching companies in the work queue"
            columns={["Company", "Deadline", "Blockers", "Assigned reviewer", "Next action"]}
            defaultSort={{ columnIndex: 1, direction: "asc" }}
            rows={rows.map((row) => ({
              id: row.company.id,
              tone: queueRowTone(row),
              searchText: queueSearchText(row),
              sortValues: queueSortValues(row),
              cells: [
                <div key="company" className="min-w-56">
                  <div className="font-medium text-[var(--foreground)]">{row.company.legalName}</div>
                  <div className="mt-1 flex flex-wrap items-center gap-2 text-xs text-[var(--muted-foreground)]">
                    <span>{formatCompanyType(row.company.companyType)}</span>
                    {row.company.croNumber && <span>CRO {row.company.croNumber}</span>}
                  </div>
                </div>,
                <div key="deadline" className="flex min-w-44 items-start gap-2">
                  <CalendarClock className="mt-0.5 h-4 w-4 shrink-0 text-[var(--muted-foreground)]" />
                  <div>
                    <p className="font-medium text-[var(--foreground)]">{row.deadlineLabel}</p>
                    <div className="mt-1">
                      <StatusBadge tone={row.deadlineTone}>{row.deadlineState}</StatusBadge>
                    </div>
                  </div>
                </div>,
                <div key="blockers" className="flex min-w-56 items-start gap-2">
                  <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-[var(--muted-foreground)]" />
                  <div>
                    <StatusBadge tone={row.blockerTone}>{row.blockerLabel}</StatusBadge>
                    <p className="mt-1 text-xs leading-5 text-[var(--muted-foreground)]">{row.blockerDetail}</p>
                  </div>
                </div>,
                <ReviewerBadge key="reviewer" company={row.company} />,
                <Link
                  key="action"
                  href={row.nextActionHref}
                  className="inline-flex min-h-8 items-center gap-2 rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] px-3 text-xs font-semibold text-[var(--foreground)] hover:border-[var(--ring)]"
                >
                  {row.nextActionLabel}
                  <ArrowRight className="h-3.5 w-3.5" />
                </Link>,
              ],
            }))}
          />
        </div>
      )}
    </ReviewPanel>
  );
}

function QueueTriage({ row }: { row: QueueRow }) {
  const reviewerName = row.company.assignedReviewerName?.trim();
  return (
    <section
      aria-label="Queue triage"
      className="grid gap-3 rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3 md:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_minmax(0,0.8fr)_auto] md:items-center"
    >
      <TriageItem
        label="Highest-risk client"
        value={row.company.legalName}
        detail={row.deadlineLabel}
      />
      <TriageItem
        label="What is wrong"
        value={`${row.blockerLabel}: ${row.blockerDetail}`}
        detail={row.deadlineState}
      />
      <TriageItem
        label="Reviewer ownership"
        value={reviewerName || "Unassigned reviewer"}
        detail={reviewerName ? row.company.assignedReviewerEmail : "Assign before approval"}
      />
      <Link
        href={row.nextActionHref}
        className="inline-flex min-h-10 items-center justify-center gap-2 rounded-md border border-[var(--border)] bg-[var(--surface)] px-3 text-sm font-semibold text-[var(--foreground)] shadow-sm transition hover:border-[var(--ring)]"
      >
        {row.nextActionLabel}
        <ArrowRight className="h-4 w-4" />
      </Link>
    </section>
  );
}

function TriageItem({
  label,
  value,
  detail,
}: {
  label: string;
  value: string;
  detail?: string;
}) {
  return (
    <div className="min-w-0">
      <p className="text-[11px] font-semibold uppercase text-[var(--muted-foreground)]">{label}</p>
      <p className="mt-1 text-sm font-semibold leading-5 text-[var(--foreground)]">{value}</p>
      {detail && <p className="mt-1 text-xs leading-5 text-[var(--muted-foreground)]">{detail}</p>}
    </div>
  );
}

function dashboardActiveStage(rows: QueueRow[]) {
  if (rows.some((row) => row.blockerLabel === "No period")) return "Setup";
  if (rows.some((row) => row.blockerLabel === "Manual handoff")) return "Review";
  if (rows.some((row) => row.deadlineState === "Overdue" || row.deadlineState === "Due soon")) return "Filing";
  return "Review";
}

function ReviewerBadge({ company }: { company: Company }) {
  const reviewerName = company.assignedReviewerName?.trim();
  const reviewerEmail = company.assignedReviewerEmail?.trim();

  if (!reviewerName) {
    return (
      <div className="inline-flex items-center gap-2 rounded-full border border-amber-200 bg-amber-50 px-2.5 py-1 text-xs font-semibold text-amber-900 dark:border-amber-800 dark:bg-amber-950/50 dark:text-amber-100">
        <UserRound className="h-3.5 w-3.5" />
        Unassigned
      </div>
    );
  }

  return (
    <div className="inline-flex max-w-52 items-start gap-2 rounded-md border border-emerald-200 bg-emerald-50 px-2.5 py-1.5 text-xs text-emerald-950 dark:border-emerald-800 dark:bg-emerald-950/50 dark:text-emerald-100">
      <UserRound className="mt-0.5 h-3.5 w-3.5 shrink-0" />
      <div className="min-w-0">
        <div className="truncate font-semibold">{reviewerName}</div>
        {reviewerEmail && (
          <div className="truncate text-[11px] font-medium text-emerald-700 dark:text-emerald-300">
            {reviewerEmail}
          </div>
        )}
      </div>
    </div>
  );
}

function buildQueueRow(company: Company, deadline: FilingDeadline | null, today: Date): QueueRow {
  const period = latestPeriod(company);
  const manualHandoffDetail = manualHandoffReason(company);
  const deadlineState = deadlineStatus(deadline, today);

  if (!period) {
    return {
      company,
      periodId: null,
      deadline,
      deadlineLabel: "No active period",
      deadlineState: "Not scheduled",
      deadlineTone: "warn",
      blockerLabel: "No period",
      blockerDetail: "Create the first accounting period before production work can start.",
      blockerTone: "bad",
      nextActionLabel: "Create period",
      nextActionHref: `/companies/${company.id}`,
    };
  }

  if (manualHandoffDetail) {
    return {
      company,
      periodId: period.id,
      deadline,
      deadlineLabel: formatDeadline(deadline),
      deadlineState: deadlineState.label,
      deadlineTone: deadlineState.tone,
      blockerLabel: "Manual handoff",
      blockerDetail: manualHandoffDetail,
      blockerTone: "bad",
      nextActionLabel: "Review handoff",
      nextActionHref: `/companies/${company.id}`,
    };
  }

  const hasDeadlinePressure = deadlineState.label === "Overdue" || deadlineState.label === "Due soon";
  const blockerLabel = deadlineState.label === "Overdue"
    ? "Deadline overdue"
    : deadline
      ? "No blockers"
      : "Deadline missing";

  return {
    company,
    periodId: period.id,
    deadline,
    deadlineLabel: formatDeadline(deadline),
    deadlineState: deadlineState.label,
    deadlineTone: deadlineState.tone,
    blockerLabel,
    blockerDetail: deadlineState.label === "Overdue"
      ? "Late filing exposure and audit exemption impact must be reviewed."
      : deadline
        ? "No dashboard-level blockers detected from current company and deadline data."
        : "Calculate filing deadlines for the active accounting period.",
    blockerTone: deadlineState.label === "Overdue" || !deadline ? "bad" : "good",
    nextActionLabel: hasDeadlinePressure ? "Open filing" : "Continue workbench",
    nextActionHref: `/companies/${company.id}/periods/${period.id}`,
  };
}

function compareQueueRows(a: QueueRow, b: QueueRow) {
  const priorityDiff = queuePriority(a) - queuePriority(b);
  if (priorityDiff !== 0) return priorityDiff;

  const dateDiff = deadlineSortValue(a) - deadlineSortValue(b);
  if (dateDiff !== 0) return dateDiff;

  return a.company.legalName.localeCompare(b.company.legalName);
}

function queuePriority(row: QueueRow) {
  if (row.blockerTone === "bad" || row.deadlineTone === "bad") return 0;
  if (row.deadlineState === "Due soon" || row.blockerTone === "warn" || row.deadlineTone === "warn") return 1;
  if (row.blockerTone === "good" && row.deadlineTone === "good") return 2;
  return 3;
}

function queueRowTone(row: QueueRow): QueueTone {
  if (row.blockerTone === "bad" || row.deadlineTone === "bad") return "bad";
  if (row.blockerTone === "warn" || row.deadlineTone === "warn") return "warn";
  if (row.blockerTone === "good" && row.deadlineTone === "good") return "good";
  return "default";
}

function queueSearchText(row: QueueRow) {
  return [
    row.company.legalName,
    row.company.tradingName,
    row.company.croNumber,
    row.company.taxReference,
    formatCompanyType(row.company.companyType),
    row.deadlineLabel,
    row.deadlineState,
    row.blockerLabel,
    row.blockerDetail,
    row.company.assignedReviewerName,
    row.company.assignedReviewerEmail,
    row.nextActionLabel,
  ].filter(Boolean).join(" ");
}

function queueSortValues(row: QueueRow) {
  return [
    row.company.legalName,
    queueUrgencySortValue(row),
    `${queuePriority(row)}:${row.blockerLabel}:${row.blockerDetail}`,
    row.company.assignedReviewerName?.trim() || "zz-unassigned",
    row.nextActionLabel,
  ];
}

function queueUrgencySortValue(row: QueueRow) {
  return [
    queuePriority(row),
    row.deadline?.dueDate ?? "9999-12-31",
    row.company.legalName,
  ].join(":");
}

function deadlineSortValue(row: QueueRow) {
  return parseDate(row.deadline?.dueDate)?.getTime() ?? Number.MAX_SAFE_INTEGER;
}

function formatQueueCount(count: number, singular: string, plural: string) {
  return `${count} ${count === 1 ? singular : plural}`;
}

function latestPeriod(company: Company) {
  if (company.latestPeriod) return company.latestPeriod;
  return [...(company.periods ?? [])].sort((a, b) => b.periodEnd.localeCompare(a.periodEnd))[0] ?? null;
}

function manualHandoffReason(company: Company) {
  if (company.companyType === "PublicLimitedCompany" || company.companyType === "PLC") {
    return "PLC/public-company workflow requires manual review";
  }

  if (company.isListedSecurities || company.isCreditInstitution || company.isInsuranceUndertaking || company.isPensionFund) {
    return "Regulated or excluded entity requires manual review";
  }

  if (company.isGroupMember || company.isHolding || company.isSubsidiary) {
    return "Group or consolidation context requires manual review";
  }

  return null;
}

function deadlineStatus(deadline: FilingDeadline | null, today: Date): { label: string; tone: QueueTone } {
  if (!deadline) return { label: "Not scheduled", tone: "warn" };

  const dueDate = parseDate(deadline.dueDate);
  if (!dueDate) return { label: "Review", tone: "warn" };
  if (dueDate < today) return { label: "Overdue", tone: "bad" };

  const daysUntilDue = Math.ceil((dueDate.getTime() - today.getTime()) / 86400000);
  if (daysUntilDue <= 30) return { label: "Due soon", tone: "warn" };
  return { label: "On track", tone: "good" };
}

function formatDeadline(deadline: FilingDeadline | null) {
  if (!deadline) return "No deadline calculated";
  return `${deadline.deadlineType} due ${formatDateIE(deadline.dueDate)}`;
}

function parseDate(value?: string) {
  if (!value) return null;
  const date = new Date(`${value}T00:00:00`);
  return Number.isNaN(date.getTime()) ? null : date;
}
