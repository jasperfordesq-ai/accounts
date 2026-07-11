"use client";

import Link from "next/link";
import { ArrowRight, Building2, CalendarClock, Plus, UserRound } from "lucide-react";
import type { Company, DashboardDeadlineState, FilingDeadline } from "@/lib/api";
import { formatCompanyType, formatDateIE } from "@/lib/format";
import { DataGrid, ReviewPanel, StatusBadge, WorkbenchEmptyState } from "@/components/workbench";

interface DashboardCompanyDirectoryProps {
  companies: Company[];
  deadlines: Record<number, FilingDeadline | null>;
  deadlineUnavailableCompanyIds?: number[];
  deadlineStates?: Record<number, DashboardDeadlineState>;
  canCreateCompany: boolean;
  today?: string;
}

type DirectoryTone = "default" | "good" | "warn" | "bad" | "info";

interface DirectoryRow {
  company: Company;
  deadline: FilingDeadline | null;
  deadlineLabel: string;
  deadlineState: string;
  deadlineTone: DirectoryTone;
  activityLabel: string;
  activityTone: DirectoryTone;
  reviewerLabel: string;
  nextActionLabel: string;
  nextActionHref: string;
  rowTone: DirectoryTone;
}

export function DashboardCompanyDirectory({
  companies,
  deadlines,
  deadlineUnavailableCompanyIds = [],
  deadlineStates = {},
  canCreateCompany,
  today,
}: DashboardCompanyDirectoryProps) {
  const todayDate = parseDate(today) ?? new Date();
  const unavailableDeadlineIds = new Set(deadlineUnavailableCompanyIds);
  const rows = companies
    .map((company) => buildDirectoryRow(
      company,
      deadlines[company.id] ?? null,
      todayDate,
      unavailableDeadlineIds.has(company.id),
      deadlineStates[company.id],
    ))
    .sort(compareDirectoryRows);

  if (rows.length === 0) {
    return (
      <WorkbenchEmptyState
        title="No companies available"
        description="Add the first company before preparing year-end accounts."
        actions={canCreateCompany ? <AddCompanyLink /> : undefined}
      />
    );
  }

  return (
    <ReviewPanel
      title="Company directory"
      description="Dense company navigation with statutory status, filing pressure, reviewer ownership and the next workspace action."
      actions={canCreateCompany ? <AddCompanyLink /> : undefined}
    >
      <DataGrid
        caption="Company directory"
        mobilePresentation="cards"
        filterPlaceholder="Filter companies, deadlines, reviewers or status"
        emptyState="No matching companies"
        columns={["Company", "Activity", "Periods", "Deadline", "Reviewer", "Next action"]}
        sortableColumns={[true, true, true, true, true, false]}
        rows={rows.map((row) => ({
          id: row.company.id,
          tone: row.rowTone,
          searchText: directorySearchText(row),
          cells: [
            <div key="company" className="min-w-56">
              <div className="flex min-w-0 items-start gap-2">
                <Building2 className="mt-0.5 h-4 w-4 shrink-0 text-[var(--muted-foreground)]" />
                <div className="min-w-0">
                  <p className="font-medium text-[var(--foreground)]">{row.company.legalName}</p>
                  <p className="mt-1 text-xs text-[var(--muted-foreground)]">
                    {formatCompanyType(row.company.companyType)}
                    {row.company.croNumber ? ` · CRO ${row.company.croNumber}` : ""}
                  </p>
                </div>
              </div>
            </div>,
            <StatusBadge key="activity" tone={row.activityTone}>{row.activityLabel}</StatusBadge>,
            <span key="periods" className="text-[var(--muted-foreground)]">
              {formatPeriodCount(row.company)}
            </span>,
            <div key="deadline" className="flex min-w-44 items-start gap-2">
              <CalendarClock className="mt-0.5 h-4 w-4 shrink-0 text-[var(--muted-foreground)]" />
              <div>
                <p className="font-medium text-[var(--foreground)]">{row.deadlineLabel}</p>
                <div className="mt-1">
                  <StatusBadge tone={row.deadlineTone}>{row.deadlineState}</StatusBadge>
                </div>
              </div>
            </div>,
            <ReviewerCell key="reviewer" label={row.reviewerLabel} />,
            <Link
              key="action"
              href={row.nextActionHref}
              className="inline-flex min-h-8 items-center gap-2 rounded-md border border-[var(--control-border)] bg-[var(--surface-subtle)] px-3 text-xs font-semibold text-[var(--foreground)] hover:border-[var(--ring)]"
            >
              {row.nextActionLabel}
              <ArrowRight className="h-3.5 w-3.5" />
            </Link>,
          ],
        }))}
      />
    </ReviewPanel>
  );
}

function AddCompanyLink() {
  return (
    <Link
      href="/companies/new"
      className="inline-flex min-h-9 items-center gap-2 rounded-md border border-emerald-700 bg-emerald-700 px-3 text-sm font-semibold text-white shadow-sm transition hover:bg-emerald-800 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-emerald-500"
    >
      <Plus className="h-4 w-4" />
      Add Company
    </Link>
  );
}

function ReviewerCell({ label }: { label: string }) {
  const assigned = label !== "Unassigned";
  return (
    <div className={`inline-flex max-w-52 items-center gap-2 rounded-md border px-2.5 py-1.5 text-xs ${
      assigned
        ? "border-emerald-200 bg-emerald-50 text-emerald-950 dark:border-emerald-800 dark:bg-emerald-950/50 dark:text-emerald-100"
        : "border-amber-200 bg-amber-50 font-semibold text-amber-900 dark:border-amber-800 dark:bg-amber-950/50 dark:text-amber-100"
    }`}
    >
      <UserRound className="h-3.5 w-3.5 shrink-0" />
      <span className="truncate">{label}</span>
    </div>
  );
}

function buildDirectoryRow(
  company: Company,
  deadline: FilingDeadline | null,
  today: Date,
  deadlineUnavailable = false,
  authoritativeState?: DashboardDeadlineState,
): DirectoryRow {
  const period = latestPeriod(company);
  const deadlineState = deadlineStatus(deadline, today, deadlineUnavailable, authoritativeState);
  const activity = activityStatus(company);

  return {
    company,
    deadline,
    deadlineLabel: formatDeadline(deadline, deadlineUnavailable, authoritativeState),
    deadlineState: deadlineState.label,
    deadlineTone: deadlineState.tone,
    activityLabel: activity.label,
    activityTone: activity.tone,
    reviewerLabel: company.assignedReviewerName?.trim() || "Unassigned",
    nextActionLabel: period ? "Open workspace" : "Open company",
    nextActionHref: period ? `/companies/${company.id}/periods/${period.id}` : `/companies/${company.id}`,
    rowTone: rowTone(activity.tone, deadlineState.tone),
  };
}

function compareDirectoryRows(a: DirectoryRow, b: DirectoryRow) {
  const priorityDiff = priority(a) - priority(b);
  if (priorityDiff !== 0) return priorityDiff;

  const dateDiff = (parseDate(a.deadline?.dueDate)?.getTime() ?? Number.MAX_SAFE_INTEGER)
    - (parseDate(b.deadline?.dueDate)?.getTime() ?? Number.MAX_SAFE_INTEGER);
  if (dateDiff !== 0) return dateDiff;

  return a.company.legalName.localeCompare(b.company.legalName);
}

function priority(row: DirectoryRow) {
  if (row.deadlineTone === "bad") return 0;
  if (row.deadlineTone === "warn" || row.activityTone === "warn") return 1;
  if (row.activityTone === "good" && row.deadlineTone === "good") return 2;
  return 3;
}

function rowTone(activityTone: DirectoryTone, deadlineTone: DirectoryTone): DirectoryTone {
  if (deadlineTone === "bad") return "bad";
  if (deadlineTone === "warn" || activityTone === "warn") return "warn";
  if (activityTone === "good" && deadlineTone === "good") return "good";
  return "default";
}

function activityStatus(company: Company): { label: string; tone: DirectoryTone } {
  if (company.isDormant) return { label: "Dormant", tone: "warn" };
  if (company.isTrading) return { label: "Trading", tone: "good" };
  return { label: "Review activity", tone: "warn" };
}

function deadlineStatus(
  deadline: FilingDeadline | null,
  today: Date,
  unavailable = false,
  authoritativeState?: DashboardDeadlineState,
): { label: string; tone: DirectoryTone } {
  if (unavailable || authoritativeState === "unavailable") return { label: "Unavailable", tone: "bad" };
  if (authoritativeState === "not-applicable") return { label: "Not applicable", tone: "default" };
  if (authoritativeState === "not-configured") return { label: "Not configured", tone: "warn" };
  if (authoritativeState === "overdue") return { label: "Overdue", tone: "bad" };
  if (authoritativeState === "due-soon") return { label: "Due soon", tone: "warn" };
  if (authoritativeState === "scheduled") return { label: "On track", tone: "good" };
  if (authoritativeState === "filed") return { label: "Filed", tone: "good" };
  if (!deadline) return { label: "Not scheduled", tone: "warn" };

  const dueDate = parseDate(deadline.dueDate);
  if (!dueDate) return { label: "Review", tone: "warn" };
  if (dueDate < today) return { label: "Overdue", tone: "bad" };

  const daysUntilDue = Math.ceil((dueDate.getTime() - today.getTime()) / 86400000);
  if (daysUntilDue <= 30) return { label: "Due soon", tone: "warn" };
  return { label: "On track", tone: "good" };
}

function formatDeadline(deadline: FilingDeadline | null, unavailable = false, authoritativeState?: DashboardDeadlineState) {
  if (unavailable || authoritativeState === "unavailable") return "Deadline evidence unavailable";
  if (authoritativeState === "not-applicable") return "No accounting period exists";
  if (authoritativeState === "not-configured") return "Deadline calculation required";
  if (authoritativeState === "filed" && deadline?.filedDate) {
    return `${deadline.deadlineType} filed ${formatDateIE(deadline.filedDate)}`;
  }
  if (!deadline) return "No deadline calculated";
  return `${deadline.deadlineType} due ${formatDateIE(deadline.dueDate)}`;
}

function formatPeriodCount(company: Company) {
  const count = company.periodCount ?? company.periods?.length ?? (company.latestPeriod ? 1 : 0);
  return `${count} period${count === 1 ? "" : "s"}`;
}

function directorySearchText(row: DirectoryRow) {
  return [
    row.company.legalName,
    row.company.tradingName,
    row.company.croNumber,
    row.company.taxReference,
    formatCompanyType(row.company.companyType),
    row.activityLabel,
    row.deadlineLabel,
    row.deadlineState,
    row.reviewerLabel,
    row.nextActionLabel,
  ].filter(Boolean).join(" ");
}

function latestPeriod(company: Company) {
  if (company.latestPeriod) return company.latestPeriod;
  return [...(company.periods ?? [])].sort((a, b) => b.periodEnd.localeCompare(a.periodEnd))[0] ?? null;
}

function parseDate(value?: string) {
  if (!value) return null;
  const date = new Date(`${value}T00:00:00`);
  return Number.isNaN(date.getTime()) ? null : date;
}
