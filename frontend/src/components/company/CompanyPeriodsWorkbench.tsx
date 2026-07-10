"use client";

import Link from "next/link";
import { Button } from "@heroui/react";
import { ArrowRight, Calendar, Plus } from "lucide-react";
import type { AccountingPeriod, Company } from "@/lib/api";
import { formatPeriodRange } from "@/lib/format";
import { DataGrid, ReviewPanel, StatusBadge } from "@/components/workbench";

interface CompanyPeriodsWorkbenchProps {
  company: Company;
  showNewPeriod: boolean;
  periodStart: string;
  periodEnd: string;
  isFirstYear: boolean;
  creatingPeriod: boolean;
  canWrite?: boolean;
  onShowNewPeriod: () => void;
  onCancelNewPeriod: () => void;
  onPeriodStartChange: (value: string) => void;
  onPeriodEndChange: (value: string) => void;
  onFirstYearChange: (value: boolean) => void;
  onCreatePeriod: () => void;
}

type Tone = "default" | "good" | "warn" | "bad" | "info";

export function CompanyPeriodsWorkbench({
  company,
  showNewPeriod,
  periodStart,
  periodEnd,
  isFirstYear,
  creatingPeriod,
  canWrite = true,
  onShowNewPeriod,
  onCancelNewPeriod,
  onPeriodStartChange,
  onPeriodEndChange,
  onFirstYearChange,
  onCreatePeriod,
}: CompanyPeriodsWorkbenchProps) {
  const periods = orderedPeriods(company.periods ?? []);

  return (
    <ReviewPanel
      title="Accounting Periods"
      description="Production periods and next workbench action."
      actions={
        <>
          <StatusBadge tone={periods.length > 0 ? "info" : "warn"}>
            {periods.length} {periods.length === 1 ? "period" : "periods"}
          </StatusBadge>
          {canWrite && (
            <Button variant="primary" size="sm" onPress={onShowNewPeriod}>
              <Plus className="h-3.5 w-3.5" />
              New Period
            </Button>
          )}
        </>
      }
    >
      {showNewPeriod && canWrite && (
        <div className="mb-4 grid gap-3 rounded-md border border-emerald-200 bg-emerald-50 p-3 dark:border-emerald-800 dark:bg-emerald-950/30 md:grid-cols-[11rem_11rem_auto_auto_auto] md:items-end">
          <DateField label="Period Start" value={periodStart} onChange={onPeriodStartChange} />
          <DateField label="Period End" value={periodEnd} onChange={onPeriodEndChange} />
          <label className="flex min-h-10 items-center gap-2 text-sm font-medium text-emerald-950 dark:text-emerald-100">
            <input
              type="checkbox"
              checked={isFirstYear}
              onChange={(event) => onFirstYearChange(event.target.checked)}
              className="rounded border-emerald-300 text-emerald-700 focus:ring-emerald-500"
            />
            First year
          </label>
          <Button variant="primary" size="sm" onPress={onCreatePeriod} isDisabled={creatingPeriod}>
            {creatingPeriod ? "Creating..." : "Create"}
          </Button>
          <Button variant="ghost" size="sm" onPress={onCancelNewPeriod} isDisabled={creatingPeriod}>
            Cancel
          </Button>
        </div>
      )}

      {periods.length === 0 ? (
        <div className="rounded-md border border-dashed border-[var(--border)] bg-[var(--surface-subtle)] px-4 py-10 text-center">
          <Calendar className="mx-auto h-9 w-9 text-[var(--muted-foreground)]" />
          <p className="mt-3 text-sm font-medium text-[var(--foreground)]">No accounting periods yet</p>
          <p className="mt-1 text-sm text-[var(--muted-foreground)]">
            Create one to start the statutory accounts workflow.
          </p>
        </div>
      ) : (
        <DataGrid
          columns={["Period", "Status", "Size and regime", "Evidence cues", "Next action"]}
          mobilePresentation="cards"
          sortableColumns={[true, true, true, false, false]}
          rows={periods.map((period, index) => ({
            id: period.id,
            searchText: `${period.periodStart} ${period.periodEnd} ${period.status} ${period.sizeClassification?.calculatedClass ?? "Unclassified"} ${period.filingRegime?.electedRegime ?? "Regime not elected"}`,
            sortValues: [
              period.periodStart,
              period.status,
              `${period.sizeClassification?.calculatedClass ?? "Unclassified"}:${period.filingRegime?.electedRegime ?? "Regime not elected"}`,
              null,
              null,
            ],
            cells: [
              <PeriodLabel key="period" period={period} />,
              <StatusBadge key="status" tone={statusTone(period.status)}>
                {period.status}
              </StatusBadge>,
              <SizeRegime key="size" period={period} />,
              <EvidenceCue key="evidence" period={period} />,
              <Link
                key="action"
                href={`/companies/${company.id}/periods/${period.id}`}
                className="inline-flex min-h-8 items-center gap-2 whitespace-nowrap rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] px-3 text-xs font-semibold text-[var(--foreground)] hover:border-[var(--ring)]"
              >
                {index === 0 ? "Open workbench" : "Open record"}
                <ArrowRight className="h-3.5 w-3.5" />
              </Link>,
            ],
          }))}
        />
      )}
    </ReviewPanel>
  );
}

function DateField({
  label,
  value,
  onChange,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
}) {
  return (
    <label className="block text-xs font-semibold uppercase text-emerald-900 dark:text-emerald-100">
      {label}
      <input
        type="date"
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className="mt-1 min-h-10 w-full rounded-md border border-emerald-200 bg-white px-3 text-sm normal-case text-gray-950 outline-none focus:border-emerald-500 focus:ring-2 focus:ring-emerald-500 dark:border-emerald-800 dark:bg-neutral-900 dark:text-gray-100"
      />
    </label>
  );
}

function PeriodLabel({ period }: { period: AccountingPeriod }) {
  return (
    <div className="min-w-0 sm:min-w-56">
      <p className="font-medium text-[var(--foreground)]">
        {formatPeriodRange(period.periodStart, period.periodEnd)}
      </p>
      {period.isFirstYear && (
        <div className="mt-1">
          <StatusBadge tone="warn">First year</StatusBadge>
        </div>
      )}
    </div>
  );
}

function SizeRegime({ period }: { period: AccountingPeriod }) {
  const size = period.sizeClassification?.calculatedClass ?? "Unclassified";
  const regime = period.filingRegime?.electedRegime ?? "Regime not elected";

  return (
    <div className="min-w-0 sm:min-w-48">
      <p className="font-medium text-[var(--foreground)]">{size}</p>
      <p className="mt-1 text-xs leading-5 text-[var(--muted-foreground)]">Regime: {regime}</p>
    </div>
  );
}

function EvidenceCue({ period }: { period: AccountingPeriod }) {
  const cues = [
    period.goingConcernConfirmed ? "Going concern confirmed" : "Going concern review",
    period.memberAuditNoticeReceived ? "Member audit notice" : "No member audit notice",
  ];

  return (
    <div className="min-w-0 space-y-1 text-xs leading-5 text-[var(--muted-foreground)] sm:min-w-56">
      {cues.map((cue) => (
        <div key={cue}>{cue}</div>
      ))}
    </div>
  );
}

function orderedPeriods(periods: AccountingPeriod[]) {
  return [...periods].sort((a, b) => b.periodEnd.localeCompare(a.periodEnd));
}

function statusTone(status: string): Tone {
  switch (status) {
    case "Filed":
    case "Finalised":
      return "good";
    case "Review":
      return "warn";
    case "Draft":
      return "info";
    default:
      return "default";
  }
}
