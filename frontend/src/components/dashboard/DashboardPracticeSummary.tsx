import { CalendarClock, UserRound } from "lucide-react";
import type { ReactNode } from "react";
import type { Company, FilingDeadline } from "@/lib/api";
import { formatDateIE } from "@/lib/format";
import { MetricStrip, ReviewPanel, StatusBadge } from "@/components/workbench";

interface DashboardPracticeSummaryProps {
  companies: Company[];
  deadlines: Record<number, FilingDeadline | null>;
  today?: string;
}

type SummaryTone = "default" | "good" | "warn" | "bad";

export function DashboardPracticeSummary({
  companies,
  deadlines,
  today,
}: DashboardPracticeSummaryProps) {
  const todayDate = parseDate(today) ?? new Date();
  const totalCompanies = companies.length;
  const totalPeriods = companies.reduce(
    (sum, company) => sum + (company.periodCount ?? company.periods?.length ?? (company.latestPeriod ? 1 : 0)),
    0,
  );
  const tradingCompanies = companies.filter((company) => company.isTrading).length;
  const dormantCompanies = companies.filter((company) => company.isDormant).length;
  const deadlinePressure = Object.values(deadlines).filter((deadline): deadline is FilingDeadline =>
    deadlineStatus(deadline, todayDate).tone !== "good",
  ).length;
  const assignedReviewers = companies.filter((company) => company.assignedReviewerName?.trim()).length;
  const unassignedReviewers = totalCompanies - assignedReviewers;
  const nearestDeadline = Object.values(deadlines)
    .filter((deadline): deadline is FilingDeadline => Boolean(deadline?.dueDate))
    .sort((a, b) => a.dueDate.localeCompare(b.dueDate))[0] ?? null;

  return (
    <ReviewPanel
      title="Practice command summary"
      description="One firm-level snapshot before drilling into the accountant work queue."
      actions={
        <StatusBadge tone={deadlinePressure > 0 || unassignedReviewers > 0 ? "warn" : "good"}>
          {deadlinePressure > 0 ? `${deadlinePressure} deadline pressure` : "No deadline pressure"}
        </StatusBadge>
      }
    >
      <div className="space-y-3">
        <MetricStrip
          metrics={[
            {
              label: "Companies",
              value: `${totalCompanies} ${totalCompanies === 1 ? "company" : "companies"}`,
              tone: "default",
            },
            {
              label: "Periods",
              value: `${totalPeriods} ${totalPeriods === 1 ? "period" : "periods"}`,
              tone: "default",
            },
            {
              label: "Trading",
              value: `${tradingCompanies} trading`,
              tone: tradingCompanies > 0 ? "good" : "warn",
            },
            {
              label: "Dormant",
              value: `${dormantCompanies} dormant`,
              tone: dormantCompanies > 0 ? "warn" : "good",
            },
          ]}
        />

        <div className="grid gap-3 rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3 md:grid-cols-3">
          <SummaryItem
            label="Deadline pressure"
            value={`${deadlinePressure} deadline pressure`}
            detail="Overdue or due-soon filing dates across visible companies."
            tone={deadlinePressure > 0 ? "warn" : "good"}
          />
          <SummaryItem
            label="Reviewer ownership"
            value={`${assignedReviewers} assigned`}
            detail={`${unassignedReviewers} unassigned`}
            tone={unassignedReviewers > 0 ? "warn" : "good"}
            icon={<UserRound className="h-4 w-4" />}
          />
          <SummaryItem
            label="Nearest filing gate"
            value={nearestDeadline ? formatDeadline(nearestDeadline) : "No deadline calculated"}
            detail="Next statutory filing date visible on the dashboard."
            tone={nearestDeadline ? deadlineStatus(nearestDeadline, todayDate).tone : "warn"}
            icon={<CalendarClock className="h-4 w-4" />}
          />
        </div>

        <p className="text-xs leading-5 text-[var(--muted-foreground)]">
          Keep this view focused on firm workload, reviewer ownership and immediate filing pressure.
        </p>
      </div>
    </ReviewPanel>
  );
}

function SummaryItem({
  label,
  value,
  detail,
  tone,
  icon,
}: {
  label: string;
  value: string;
  detail: string;
  tone: SummaryTone;
  icon?: ReactNode;
}) {
  return (
    <div className="min-w-0">
      <div className="flex items-center justify-between gap-2">
        <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">{label}</p>
        <span className="text-[var(--muted-foreground)]">{icon}</span>
      </div>
      <p className="mt-2 text-sm font-semibold text-[var(--foreground)]">{value}</p>
      <div className="mt-2">
        <StatusBadge tone={tone}>{tone === "good" ? "Clear" : tone === "bad" ? "Blocked" : "Review"}</StatusBadge>
      </div>
      <p className="mt-2 text-xs leading-5 text-[var(--muted-foreground)]">{detail}</p>
    </div>
  );
}

function deadlineStatus(deadline: FilingDeadline | null, today: Date): { tone: SummaryTone } {
  if (!deadline) return { tone: "warn" };

  const dueDate = parseDate(deadline.dueDate);
  if (!dueDate) return { tone: "warn" };
  if (dueDate < today) return { tone: "bad" };

  const daysUntilDue = Math.ceil((dueDate.getTime() - today.getTime()) / 86400000);
  if (daysUntilDue <= 30) return { tone: "warn" };
  return { tone: "good" };
}

function formatDeadline(deadline: FilingDeadline) {
  return `${deadline.deadlineType} due ${formatDateIE(deadline.dueDate)}`;
}

function parseDate(value?: string) {
  if (!value) return null;
  const date = new Date(`${value}T00:00:00`);
  return Number.isNaN(date.getTime()) ? null : date;
}
