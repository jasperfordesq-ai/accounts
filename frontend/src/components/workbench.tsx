"use client";

import { useId, useMemo, useState, type ReactNode } from "react";
import Link from "next/link";
import {
  AlertTriangle,
  ArrowRight,
  CheckCircle2,
  Circle,
  CircleAlert,
  CircleCheck,
  CircleDot,
  CircleSlash,
  ExternalLink,
  FileCheck2,
  FileSearch,
  LoaderCircle,
  LockKeyhole,
  RefreshCw,
} from "lucide-react";

type WorkflowState = "done" | "active" | "blocked" | "todo";
type Tone = "default" | "good" | "warn" | "bad" | "info";
type DataTableRowTone = Tone;

export interface WorkflowItem {
  id?: string;
  label: string;
  detail: string;
  state: WorkflowState;
  href?: string;
  icon?: ReactNode;
}

export interface EvidenceItem {
  code: string;
  label: string;
  required?: boolean;
  satisfied: boolean;
  detail?: string | null;
}

export interface LegalSourceItem {
  sourceId: string;
  title: string;
  effectiveDate: string;
  url: string;
}

export interface IssueDigestProps {
  title: string;
  description: string;
  blockers?: string[];
  warnings?: string[];
  maxPriorityItems?: number;
  className?: string;
}

export interface WorkbenchStatePanelProps {
  title: string;
  description?: ReactNode;
  tone?: Tone;
  icon?: ReactNode;
  actions?: ReactNode;
  children?: ReactNode;
  className?: string;
}

export interface WorkbenchErrorStateProps {
  title?: string;
  description?: ReactNode;
  onRetry?: () => void | Promise<void>;
  retryLabel?: string;
}

export interface WorkbenchEmptyStateProps {
  title: string;
  description?: ReactNode;
  actions?: ReactNode;
}

export interface PermissionDeniedPanelProps {
  title?: string;
  description?: ReactNode;
  actions?: ReactNode;
}

export interface MoneyInputProps {
  label: string;
  value: number;
  onValueChange: (value: number) => void;
  ariaLabel?: string;
  placeholder?: string;
  hint?: ReactNode;
  className?: string;
  id?: string;
  isDisabled?: boolean;
  allowNegative?: boolean;
}

export interface DataTableRichRow {
  id?: string | number;
  cells: ReactNode[];
  searchText?: string;
  tone?: DataTableRowTone;
}

type DataTableRow = ReactNode[] | DataTableRichRow;

const toneClasses: Record<Tone, string> = {
  default: "border-[var(--border)] bg-[var(--surface-subtle)] text-[var(--foreground)]",
  good: "border-emerald-200 bg-emerald-50 text-emerald-800 dark:border-emerald-800 dark:bg-emerald-950/50 dark:text-emerald-200",
  warn: "border-amber-200 bg-amber-50 text-amber-900 dark:border-amber-800 dark:bg-amber-950/50 dark:text-amber-100",
  bad: "border-red-200 bg-red-50 text-red-800 dark:border-red-900 dark:bg-red-950/50 dark:text-red-100",
  info: "border-sky-200 bg-sky-50 text-sky-800 dark:border-sky-900 dark:bg-sky-950/50 dark:text-sky-100",
};

const iconToneClasses: Record<Tone, string> = {
  default: "text-[var(--muted-foreground)]",
  good: "text-emerald-600 dark:text-emerald-300",
  warn: "text-amber-600 dark:text-amber-300",
  bad: "text-red-600 dark:text-red-300",
  info: "text-sky-600 dark:text-sky-300",
};

export function WorkbenchShell({ children }: { children: ReactNode }) {
  return <div className="min-w-0 space-y-6 text-[var(--foreground)]">{children}</div>;
}

export function WorkbenchHeader({
  title,
  subtitle,
  meta,
  actions,
}: {
  title: string;
  subtitle?: string;
  meta?: ReactNode;
  actions?: ReactNode;
}) {
  return (
    <section className="mb-6 border-b border-[var(--border)] pb-5">
      <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
        <div className="min-w-0">
          <h1 className="text-2xl font-semibold tracking-normal text-[var(--foreground)]">
            {title}
          </h1>
          {subtitle && (
            <p className="mt-1 max-w-3xl text-sm leading-6 text-[var(--muted-foreground)]">
              {subtitle}
            </p>
          )}
          {meta && <div className="mt-3 flex flex-wrap items-center gap-2">{meta}</div>}
        </div>
        {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
      </div>
    </section>
  );
}

export function WorkbenchStatePanel({
  title,
  description,
  tone = "default",
  icon,
  actions,
  children,
  className = "",
}: WorkbenchStatePanelProps) {
  return (
    <section
      aria-label={title}
      className={`rounded-md border border-[var(--border)] bg-[var(--surface)] p-4 shadow-sm shadow-black/[0.03] ${className}`}
    >
      <div className="flex flex-col gap-4 sm:flex-row sm:items-start">
        <div className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-md border ${toneClasses[tone]}`}>
          {icon ?? <FileSearch className="h-5 w-5" />}
        </div>
        <div className="min-w-0 flex-1">
          <h1 className="text-base font-semibold text-[var(--foreground)]">{title}</h1>
          {description && (
            <div className="mt-1 text-sm leading-6 text-[var(--muted-foreground)]">{description}</div>
          )}
          {children && <div className="mt-4">{children}</div>}
          {actions && <div className="mt-4 flex flex-wrap items-center gap-2">{actions}</div>}
        </div>
      </div>
    </section>
  );
}

export function WorkbenchLoadingState({
  title = "Loading accountant workspace",
  description = "Preparing statutory evidence, deadlines and filing workflow state.",
}: {
  title?: string;
  description?: ReactNode;
}) {
  return (
    <WorkbenchStatePanel
      title={title}
      description={description}
      tone="info"
      icon={<LoaderCircle className="h-5 w-5 animate-spin" />}
    >
      <div className="grid gap-3 md:grid-cols-4">
        {[...Array(4)].map((_, index) => (
          <div key={index} className="skeleton-shimmer h-16 rounded-md" />
        ))}
      </div>
      <div className="mt-3 grid gap-3 lg:grid-cols-[minmax(0,1.4fr)_minmax(18rem,0.6fr)]">
        <div className="skeleton-shimmer h-44 rounded-md" />
        <div className="skeleton-shimmer h-44 rounded-md" />
      </div>
    </WorkbenchStatePanel>
  );
}

export function WorkbenchErrorState({
  title = "Workspace could not be loaded",
  description = "Refresh the workspace and check the API health before continuing filing work.",
  onRetry,
  retryLabel = "Retry",
}: WorkbenchErrorStateProps) {
  return (
    <WorkbenchStatePanel
      title={title}
      description={description}
      tone="bad"
      icon={<AlertTriangle className="h-5 w-5" />}
      actions={onRetry && (
        <button
          type="button"
          onClick={onRetry}
          className="inline-flex min-h-10 items-center gap-2 rounded-md border border-red-300 bg-red-600 px-3 text-sm font-semibold text-white shadow-sm transition hover:bg-red-700 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-red-500 dark:border-red-800"
        >
          <RefreshCw className="h-4 w-4" />
          {retryLabel}
        </button>
      )}
    />
  );
}

export function WorkbenchEmptyState({ title, description, actions }: WorkbenchEmptyStateProps) {
  return (
    <WorkbenchStatePanel
      title={title}
      description={description}
      tone="info"
      icon={<FileSearch className="h-5 w-5" />}
      actions={actions}
    />
  );
}

export function PermissionDeniedPanel({
  title = "Permission denied",
  description = "You do not have permission to approve or submit this accounting workflow.",
  actions,
}: PermissionDeniedPanelProps) {
  return (
    <WorkbenchStatePanel
      title={title}
      description={description}
      tone="warn"
      icon={<LockKeyhole className="h-5 w-5" />}
      actions={actions}
    />
  );
}

const workflowStateLabel: Record<WorkflowState, string> = {
  done: "Complete",
  active: "Next",
  blocked: "Blocked",
  todo: "Pending",
};

const workflowStateClasses: Record<WorkflowState, string> = {
  done: "border-emerald-200 bg-emerald-50 text-emerald-800 dark:border-emerald-800 dark:bg-emerald-950/50 dark:text-emerald-200",
  active: "border-amber-200 bg-amber-50 text-amber-900 dark:border-amber-800 dark:bg-amber-950/50 dark:text-amber-100",
  blocked: "border-red-200 bg-red-50 text-red-800 dark:border-red-900 dark:bg-red-950/50 dark:text-red-100",
  todo: "border-[var(--border)] bg-[var(--surface-subtle)] text-[var(--muted-foreground)]",
};

export function WorkflowRail({ items, title = "Accounting Workflow" }: { items: WorkflowItem[]; title?: string }) {
  return (
    <nav aria-label={title} className="mb-6 min-w-0 max-w-full overflow-hidden rounded-md border border-[var(--border)] bg-[var(--surface)]">
      <div className="flex items-center justify-between gap-3 border-b border-[var(--border)] px-4 py-3">
        <h2 className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">{title}</h2>
        <span className="text-xs text-[var(--muted-foreground)]">{items.length} stages</span>
      </div>
      <div className="min-w-0 max-w-full overflow-x-auto">
        <ol className="grid w-max min-w-full auto-cols-[minmax(9rem,1fr)] grid-flow-col divide-x divide-[var(--border)]">
          {items.map((item) => (
            <li key={item.id ?? item.label} className="min-w-0">
              <WorkflowRailItem item={item} />
            </li>
          ))}
        </ol>
      </div>
    </nav>
  );
}

function WorkflowRailItem({ item }: { item: WorkflowItem }) {
  const content = (
    <>
      <div className="flex items-start justify-between gap-2">
        <div className="flex min-w-0 items-center gap-2">
          {item.icon ?? <WorkflowIcon state={item.state} />}
          <span className="truncate text-xs font-semibold uppercase text-[var(--foreground)]">
            {item.label}
          </span>
        </div>
        {item.href && <ArrowRight className="mt-0.5 h-3.5 w-3.5 shrink-0 text-[var(--muted-foreground)]" />}
      </div>
      <p className="mt-2 min-h-10 text-xs leading-5 text-[var(--muted-foreground)]">
        {item.detail}
      </p>
      <span className={`mt-3 inline-flex min-h-6 items-center rounded-full border px-2 text-[11px] font-semibold ${workflowStateClasses[item.state]}`}>
        {workflowStateLabel[item.state]}
      </span>
    </>
  );

  const className = `block h-full p-3 text-left transition-colors ${item.href ? "hover:bg-[var(--surface-subtle)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-[-2px] focus-visible:outline-emerald-500" : ""}`;

  if (!item.href) {
    return <div className={className}>{content}</div>;
  }

  return (
    <Link href={item.href} aria-current={item.state === "active" ? "step" : undefined} className={className}>
      {content}
    </Link>
  );
}

function WorkflowIcon({ state }: { state: WorkflowState }) {
  if (state === "done") return <CheckCircle2 className="h-4 w-4 text-emerald-600" />;
  if (state === "blocked") return <AlertTriangle className="h-4 w-4 text-red-600" />;
  if (state === "active") return <Circle className="h-4 w-4 fill-amber-400 text-amber-500" />;
  return <Circle className="h-4 w-4 text-gray-300 dark:text-neutral-600" />;
}

export function ReviewPanel({
  title,
  description,
  actions,
  children,
}: {
  title: string;
  description?: string;
  actions?: ReactNode;
  children: ReactNode;
}) {
  return (
    <section className="min-w-0 rounded-md border border-[var(--border)] bg-[var(--surface)] shadow-sm shadow-black/[0.03]">
      <header className="flex flex-col gap-3 border-b border-[var(--border)] px-4 py-3 md:flex-row md:items-center md:justify-between">
        <div>
          <h2 className="text-sm font-semibold text-[var(--foreground)]">{title}</h2>
          {description && (
            <p className="mt-0.5 text-xs leading-5 text-[var(--muted-foreground)]">{description}</p>
          )}
        </div>
        {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
      </header>
      <div className="p-4">{children}</div>
    </section>
  );
}

export function MetricStrip({
  metrics,
}: {
  metrics: { label: string; value: ReactNode; tone?: "default" | "good" | "warn" | "bad" }[];
}) {
  const toneClass: Record<Tone, string> = {
    default: "text-[var(--foreground)]",
    good: "text-emerald-700 dark:text-emerald-300",
    warn: "text-amber-700 dark:text-amber-300",
    bad: "text-red-700 dark:text-red-300",
    info: "text-sky-700 dark:text-sky-300",
  };

  return (
    <div className="grid grid-cols-2 divide-x divide-y divide-[var(--border)] overflow-hidden rounded-md border border-[var(--border)] bg-[var(--surface)] md:grid-cols-4 md:divide-y-0">
      {metrics.map((metric) => (
        <div key={metric.label} className="px-4 py-3">
          <p className="text-xs font-medium text-[var(--muted-foreground)]">{metric.label}</p>
          <p className={`mt-1 text-lg font-semibold ${toneClass[metric.tone ?? "default"]}`}>
            {metric.value}
          </p>
        </div>
      ))}
    </div>
  );
}

export function StatusBadge({
  children,
  tone = "default",
}: {
  children: ReactNode;
  tone?: Tone;
}) {
  return (
    <span className={`inline-flex min-h-7 items-center gap-1.5 rounded-full border px-2.5 text-xs font-semibold ${toneClasses[tone]}`}>
      {tone === "good" && <CircleCheck className="h-3.5 w-3.5" />}
      {tone === "warn" && <CircleAlert className="h-3.5 w-3.5" />}
      {tone === "bad" && <CircleSlash className="h-3.5 w-3.5" />}
      {tone === "info" && <CircleDot className="h-3.5 w-3.5" />}
      {children}
    </span>
  );
}

export function IssueDigest({
  title,
  description,
  blockers = [],
  warnings = [],
  maxPriorityItems = 3,
  className = "",
}: IssueDigestProps) {
  const blockerIssues = uniqueIssueMessages(blockers);
  const warningIssues = uniqueIssueMessages(warnings);

  if (blockerIssues.length === 0 && warningIssues.length === 0) return null;

  const priorityBlockers = blockerIssues.slice(0, maxPriorityItems);
  const remainingBlockers = blockerIssues.slice(maxPriorityItems);

  return (
    <div className={`rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3 ${className}`}>
      <div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">{title}</p>
          <p className="mt-1 text-sm font-medium text-[var(--foreground)]">{description}</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <StatusBadge tone={blockerIssues.length > 0 ? "bad" : "good"}>{formatIssueCount(blockerIssues.length, "blocker")}</StatusBadge>
          <StatusBadge tone={warningIssues.length > 0 ? "warn" : "good"}>{formatIssueCount(warningIssues.length, "warning")}</StatusBadge>
        </div>
      </div>

      {priorityBlockers.length > 0 && (
        <div className="mt-3">
          <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">Priority blockers</p>
          <IssueList issues={priorityBlockers} tone="bad" className="mt-2" />
          {remainingBlockers.length > 0 && (
            <details className="mt-2 rounded-md border border-red-200 bg-red-50/60 p-2 dark:border-red-900 dark:bg-red-950/30">
              <summary className="cursor-pointer text-xs font-semibold text-red-800 dark:text-red-100">
                {formatIssueCount(remainingBlockers.length, "more blocker")}
              </summary>
              <IssueList issues={remainingBlockers} tone="bad" className="mt-2" />
            </details>
          )}
        </div>
      )}

      {warningIssues.length > 0 && (
        <div className="mt-3">
          <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">Warnings</p>
          <IssueList issues={warningIssues} tone="warn" className="mt-2" />
        </div>
      )}
    </div>
  );
}

function IssueList({
  issues,
  tone,
  className = "",
}: {
  issues: string[];
  tone: "bad" | "warn";
  className?: string;
}) {
  const toneClass = tone === "bad"
    ? "text-red-800 dark:text-red-100"
    : "text-amber-900 dark:text-amber-100";

  return (
    <ul className={`space-y-1.5 ${className}`}>
      {issues.map((issue, index) => (
        <li key={issueKey(issue, index)} className={`text-sm leading-6 ${toneClass}`}>
          {issue}
        </li>
      ))}
    </ul>
  );
}

function uniqueIssueMessages(issues: string[]) {
  const seen = new Set<string>();
  return issues.filter((issue) => {
    const normalized = issue.trim();
    const key = normalized.toLowerCase();
    if (!key || seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}

function formatIssueCount(count: number, singular: string) {
  return `${count} ${count === 1 ? singular : `${singular}s`}`;
}

function issueKey(message: string, index: number) {
  return `${message.slice(0, 36)}-${index}`;
}

export function SectionHeader({
  eyebrow,
  title,
  description,
  actions,
}: {
  eyebrow?: string;
  title: string;
  description?: string;
  actions?: ReactNode;
}) {
  return (
    <div className="flex flex-col gap-3 md:flex-row md:items-end md:justify-between">
      <div>
        {eyebrow && <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">{eyebrow}</p>}
        <h2 className="mt-1 text-base font-semibold text-[var(--foreground)]">{title}</h2>
        {description && <p className="mt-1 max-w-3xl text-sm leading-6 text-[var(--muted-foreground)]">{description}</p>}
      </div>
      {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
    </div>
  );
}

export function EvidenceChecklist({ items }: { items: EvidenceItem[] }) {
  return (
    <div className="divide-y divide-[var(--border)] overflow-hidden rounded-md border border-[var(--border)] bg-[var(--surface)]">
      {items.map((item) => {
        const tone: Tone = item.satisfied ? "good" : item.required ? "bad" : "warn";
        return (
          <div key={item.code} className="grid gap-3 px-4 py-3 sm:grid-cols-[minmax(0,1fr)_auto] sm:items-center">
            <div className="flex min-w-0 items-start gap-3">
              <FileCheck2 className={`mt-0.5 h-4 w-4 shrink-0 ${iconToneClasses[tone]}`} />
              <div className="min-w-0">
                <p className="text-sm font-medium text-[var(--foreground)]">{item.label}</p>
                {item.detail && <p className="mt-0.5 text-xs leading-5 text-[var(--muted-foreground)]">{item.detail}</p>}
              </div>
            </div>
            <StatusBadge tone={tone}>{item.satisfied ? "Complete" : item.required ? "Required" : "Review"}</StatusBadge>
          </div>
        );
      })}
    </div>
  );
}

export function LegalSourceList({
  sources,
  limit,
}: {
  sources: LegalSourceItem[];
  limit?: number;
}) {
  const unique = uniqueLegalSources(sources);
  const visible = typeof limit === "number" ? unique.slice(0, limit) : unique;
  const remaining = Math.max(0, unique.length - visible.length);

  if (visible.length === 0) {
    return <p className="text-sm text-[var(--muted-foreground)]">No legal sources attached.</p>;
  }

  return (
    <ul className="flex w-full max-w-full flex-wrap gap-2 whitespace-normal">
      {visible.map((source) => (
        <li key={source.sourceId || source.url} className="min-w-0 max-w-full basis-full sm:basis-auto">
          <a
            href={source.url}
            target="_blank"
            rel="noreferrer"
            className="group inline-flex w-full max-w-full items-start gap-2 rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] px-3 py-2 text-xs font-medium text-[var(--foreground)] transition hover:border-[var(--ring)] hover:bg-[var(--surface)]"
          >
            <span className="min-w-0">
              <span className="block truncate">{source.title}</span>
              <span className="mt-0.5 block text-[11px] font-normal text-[var(--muted-foreground)]">
                Effective {formatLegalSourceDate(source.effectiveDate)}
              </span>
            </span>
            <ExternalLink className="mt-0.5 h-3.5 w-3.5 shrink-0 text-[var(--muted-foreground)] group-hover:text-[var(--foreground)]" />
          </a>
        </li>
      ))}
      {remaining > 0 && (
        <li className="inline-flex min-h-9 max-w-full basis-full items-center rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] px-3 text-xs font-semibold text-[var(--muted-foreground)] sm:basis-auto">
          {remaining} more {remaining === 1 ? "source" : "sources"}
        </li>
      )}
    </ul>
  );
}

function uniqueLegalSources(sources: LegalSourceItem[]) {
  const seen = new Set<string>();
  return sources.filter((source) => {
    const key = source.sourceId || source.url;
    if (!key || seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}

function formatLegalSourceDate(value: string) {
  const calendarDate = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  const date = calendarDate
    ? new Date(
        Date.UTC(
          Number(calendarDate[1]),
          Number(calendarDate[2]) - 1,
          Number(calendarDate[3]),
        ),
      )
    : new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat("en-IE", {
    day: "2-digit",
    month: "short",
    timeZone: "UTC",
    year: "numeric",
  }).format(date);
}

export function DataTable({
  columns,
  rows,
  caption,
  filterPlaceholder,
  emptyState = "No rows to show",
  totals,
}: {
  columns: string[];
  rows: DataTableRow[];
  caption?: string;
  filterPlaceholder?: string;
  emptyState?: ReactNode;
  totals?: ReactNode[];
}) {
  const [filter, setFilter] = useState("");
  const normalizedRows = useMemo(() => rows.map(normalizeDataTableRow), [rows]);
  const normalizedFilter = filter.trim().toLowerCase();
  const visibleRows = useMemo(() => {
    if (!normalizedFilter) return normalizedRows;
    return normalizedRows.filter((row) => row.searchText.toLowerCase().includes(normalizedFilter));
  }, [normalizedFilter, normalizedRows]);
  const tableLabel = caption ?? "Workbench data table";
  const showFilter = Boolean(filterPlaceholder);

  return (
    <div className="min-w-0 space-y-3">
      {showFilter && (
        <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
          <label className="sr-only" htmlFor={filterId(tableLabel)}>
            Filter {tableLabel}
          </label>
          <input
            id={filterId(tableLabel)}
            type="search"
            value={filter}
            onChange={(event) => setFilter(event.target.value)}
            placeholder={filterPlaceholder}
            aria-label={`Filter ${tableLabel}`}
            className="min-h-10 w-full rounded-md border border-[var(--border)] bg-[var(--surface)] px-3 text-sm text-[var(--foreground)] outline-none transition focus:border-emerald-500 focus:ring-2 focus:ring-emerald-500/20 sm:max-w-xs"
          />
          <p className="text-xs font-medium text-[var(--muted-foreground)]">
            {visibleRows.length} of {normalizedRows.length} rows
          </p>
        </div>
      )}
      <div
        className="workbench-data-table min-w-0 overflow-x-auto rounded-md border border-[var(--border)] bg-[var(--surface)]"
        data-responsive="card"
      >
      <table className="min-w-full border-collapse text-left text-sm" aria-label={tableLabel}>
        {caption && <caption className="sr-only">{caption}</caption>}
        <thead className="bg-[var(--surface-subtle)] text-xs font-semibold uppercase text-[var(--muted-foreground)]">
          <tr>
            {columns.map((column) => (
              <th key={column} className="whitespace-nowrap border-b border-[var(--border)] px-4 py-3">
                {column}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y divide-[var(--border)]">
          {visibleRows.map((row, rowIndex) => (
            <tr
              key={row.id ?? rowIndex}
              data-tone={row.tone}
              className={`hover:bg-[var(--surface-subtle)] ${dataTableRowToneClass(row.tone)}`}
            >
              {row.cells.map((cell, cellIndex) => (
                <td
                  key={cellIndex}
                  data-label={columns[cellIndex] ?? ""}
                  className="px-4 py-3 align-top text-[var(--foreground)]"
                >
                  {cell}
                </td>
              ))}
            </tr>
          ))}
          {visibleRows.length === 0 && (
            <tr>
              <td colSpan={columns.length} className="px-4 py-6 text-center text-sm text-[var(--muted-foreground)]">
                {emptyState}
              </td>
            </tr>
          )}
        </tbody>
        {totals && (
          <tfoot className="border-t border-[var(--border)] bg-[var(--surface-subtle)] text-sm font-semibold text-[var(--foreground)]">
            <tr>
              {totals.map((cell, cellIndex) => (
                <td
                  key={cellIndex}
                  data-label={columns[cellIndex] ?? ""}
                  className="px-4 py-3 align-top"
                >
                  {cell}
                </td>
              ))}
            </tr>
          </tfoot>
        )}
      </table>
      </div>
    </div>
  );
}

function normalizeDataTableRow(row: DataTableRow, rowIndex: number): Required<DataTableRichRow> {
  if (Array.isArray(row)) {
    const fallbackId = row.map(cellText).join("|");
    return {
      id: `row-${rowIndex}-${fallbackId || "legacy"}`,
      cells: row,
      searchText: row.map(cellText).join(" "),
      tone: "default",
    };
  }

  const fallbackId = row.cells.map(cellText).join("|");
  return {
    id: row.id ?? `row-${rowIndex}-${fallbackId || "rich"}`,
    cells: row.cells,
    searchText: row.searchText ?? row.cells.map(cellText).join(" "),
    tone: row.tone ?? "default",
  };
}

function cellText(cell: ReactNode): string {
  if (cell === null || cell === undefined || typeof cell === "boolean") return "";
  if (typeof cell === "string" || typeof cell === "number" || typeof cell === "bigint") {
    return String(cell);
  }
  return "";
}

function dataTableRowToneClass(tone: DataTableRowTone) {
  switch (tone) {
    case "good":
      return "border-l-4 border-l-emerald-400";
    case "warn":
      return "border-l-4 border-l-amber-400 bg-amber-50/35 dark:bg-amber-950/20";
    case "bad":
      return "border-l-4 border-l-red-400 bg-red-50/35 dark:bg-red-950/20";
    case "info":
      return "border-l-4 border-l-sky-400";
    default:
      return "";
  }
}

function filterId(label: string) {
  return `filter-${label.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/(^-|-$)/g, "") || "table"}`;
}

export function MoneyField({ value }: { value: number | null | undefined }) {
  if (value === null || value === undefined) return <span className="text-[var(--muted-foreground)]">-</span>;
  return (
    <span className={value < 0 ? "font-medium text-red-700 dark:text-red-300" : "font-medium tabular-nums"}>
      {new Intl.NumberFormat("en-IE", { style: "currency", currency: "EUR", maximumFractionDigits: 0 }).format(value)}
    </span>
  );
}

export function MoneyInput({
  label,
  value,
  onValueChange,
  ariaLabel,
  placeholder = "0.00",
  hint,
  className = "",
  id,
  isDisabled = false,
  allowNegative = false,
}: MoneyInputProps) {
  const generatedId = useId();
  const inputId = id ?? generatedId;
  const hintId = hint ? `${inputId}-hint` : undefined;
  const [isFocused, setIsFocused] = useState(false);
  const [draft, setDraft] = useState(() => formatMoneyInputValue(value));
  const displayValue = isFocused ? draft : formatMoneyInputValue(value);

  return (
    <div className={className}>
      <label htmlFor={inputId} className="block text-xs font-medium text-[var(--muted-foreground)] mb-1">
        {label}
      </label>
      <div
        className={`grid min-h-10 grid-cols-[3.25rem_minmax(0,1fr)] overflow-hidden rounded-md border border-[var(--border)] bg-[var(--surface)] transition focus-within:border-emerald-500 focus-within:ring-2 focus-within:ring-emerald-500/20 ${isDisabled ? "cursor-not-allowed opacity-60" : ""}`}
      >
        <span
          data-money-input-prefix="true"
          className="pointer-events-none flex items-center justify-center border-r border-[var(--border)] bg-[var(--surface-subtle)] px-2 text-[11px] font-semibold text-[var(--muted-foreground)]"
        >
          EUR
        </span>
        <input
          id={inputId}
          type="text"
          inputMode="decimal"
          autoComplete="off"
          value={displayValue}
          placeholder={placeholder}
          aria-label={ariaLabel ?? label}
          aria-describedby={hintId}
          disabled={isDisabled}
          data-money-input="true"
          className="min-w-0 bg-transparent px-3 py-2 text-sm tabular-nums text-[var(--foreground)] outline-none placeholder:text-[var(--muted-foreground)] disabled:cursor-not-allowed"
          onFocus={() => {
            setDraft(formatMoneyInputValue(value));
            setIsFocused(true);
          }}
          onBlur={() => {
            setIsFocused(false);
          }}
          onChange={(event) => {
            const nextDraft = event.target.value;
            const nextValue = parseMoneyInputDraft(nextDraft, allowNegative);
            setDraft(nextDraft);
            if (nextValue !== null) {
              onValueChange(nextValue);
            }
          }}
        />
      </div>
      {hint && (
        <p id={hintId} className="mt-1 text-[11px] leading-4 text-[var(--muted-foreground)]">
          {hint}
        </p>
      )}
    </div>
  );
}

function formatMoneyInputValue(value: number) {
  if (!Number.isFinite(value) || value === 0) return "";
  return String(value);
}

function parseMoneyInputDraft(rawValue: string, allowNegative: boolean) {
  const normalized = rawValue.replace(/[\s,]/g, "");
  if (normalized === "") return 0;

  const pattern = allowNegative ? /^-?\d*(\.\d{0,2})?$/ : /^\d*(\.\d{0,2})?$/;
  if (!pattern.test(normalized) || normalized === "-" || normalized === "." || normalized === "-.") {
    return null;
  }

  const value = Number(normalized);
  return Number.isFinite(value) ? value : null;
}

export function FilingActionBar({ children }: { children: ReactNode }) {
  return (
    <div className="z-20 flex flex-col gap-3 rounded-md border border-[var(--border)] bg-[var(--surface)] p-3 shadow-lg shadow-black/10 sm:sticky sm:bottom-4 sm:flex-row sm:items-center sm:justify-end">
      {children}
    </div>
  );
}
