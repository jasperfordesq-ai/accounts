import Link from "next/link";
import { AlertTriangle, ArrowRight, CalendarClock, UserRound } from "lucide-react";
import type { Company, FilingDeadline } from "@/lib/api";
import { formatCompanyType, formatDateIE } from "@/lib/format";
import { ReviewPanel, StatusBadge } from "@/components/workbench";

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
  const rows = companies.map((company) => buildQueueRow(company, deadlines[company.id] ?? null, todayDate));
  const urgentCount = rows.filter((row) => row.deadlineTone === "bad" || row.blockerTone === "bad").length;

  return (
    <ReviewPanel
      title="Accountant Work Queue"
      description="Active production work across the firm."
      actions={<StatusBadge tone={urgentCount > 0 ? "bad" : "good"}>{urgentCount} urgent</StatusBadge>}
    >
      {rows.length === 0 ? (
        <div className="rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-4 text-sm text-[var(--muted-foreground)]">
          No companies are currently visible to this user.
        </div>
      ) : (
        <div className="overflow-x-auto rounded-md border border-[var(--border)] bg-[var(--surface)]">
          <table className="min-w-full border-collapse text-left text-sm">
            <thead className="bg-[var(--surface-subtle)] text-xs font-semibold uppercase text-[var(--muted-foreground)]">
              <tr>
                <th className="whitespace-nowrap border-b border-[var(--border)] px-4 py-3">Company</th>
                <th className="whitespace-nowrap border-b border-[var(--border)] px-4 py-3">Deadline</th>
                <th className="whitespace-nowrap border-b border-[var(--border)] px-4 py-3">Blockers</th>
                <th className="whitespace-nowrap border-b border-[var(--border)] px-4 py-3">Assigned reviewer</th>
                <th className="whitespace-nowrap border-b border-[var(--border)] px-4 py-3">Next action</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-[var(--border)]">
              {rows.map((row) => (
                <tr key={row.company.id} className="hover:bg-[var(--surface-subtle)]">
                  <td className="min-w-64 px-4 py-3 align-top">
                    <div className="font-medium text-[var(--foreground)]">{row.company.legalName}</div>
                    <div className="mt-1 flex flex-wrap items-center gap-2 text-xs text-[var(--muted-foreground)]">
                      <span>{formatCompanyType(row.company.companyType)}</span>
                      {row.company.croNumber && <span>CRO {row.company.croNumber}</span>}
                    </div>
                  </td>
                  <td className="min-w-52 px-4 py-3 align-top">
                    <div className="flex items-start gap-2">
                      <CalendarClock className="mt-0.5 h-4 w-4 shrink-0 text-[var(--muted-foreground)]" />
                      <div>
                        <p className="font-medium text-[var(--foreground)]">{row.deadlineLabel}</p>
                        <div className="mt-1">
                          <StatusBadge tone={row.deadlineTone}>{row.deadlineState}</StatusBadge>
                        </div>
                      </div>
                    </div>
                  </td>
                  <td className="min-w-64 px-4 py-3 align-top">
                    <div className="flex items-start gap-2">
                      <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-[var(--muted-foreground)]" />
                      <div>
                        <StatusBadge tone={row.blockerTone}>{row.blockerLabel}</StatusBadge>
                        <p className="mt-1 text-xs leading-5 text-[var(--muted-foreground)]">{row.blockerDetail}</p>
                      </div>
                    </div>
                  </td>
                  <td className="min-w-44 px-4 py-3 align-top">
                    <div className="inline-flex items-center gap-2 rounded-full border border-amber-200 bg-amber-50 px-2.5 py-1 text-xs font-semibold text-amber-900 dark:border-amber-800 dark:bg-amber-950/50 dark:text-amber-100">
                      <UserRound className="h-3.5 w-3.5" />
                      Unassigned
                    </div>
                  </td>
                  <td className="whitespace-nowrap px-4 py-3 align-top">
                    <Link
                      href={row.nextActionHref}
                      className="inline-flex min-h-8 items-center gap-2 rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] px-3 text-xs font-semibold text-[var(--foreground)] hover:border-[var(--ring)]"
                    >
                      {row.nextActionLabel}
                      <ArrowRight className="h-3.5 w-3.5" />
                    </Link>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </ReviewPanel>
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

function latestPeriod(company: Company) {
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
