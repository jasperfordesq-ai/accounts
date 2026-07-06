"use client";

import { useId, useMemo, useState, type ReactNode } from "react";
import Link from "next/link";
import {
  AlertTriangle,
  ArrowDown,
  ArrowLeft,
  ArrowRight,
  ArrowUp,
  ArrowUpDown,
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
type DataTableSortDirection = "asc" | "desc";
type DataTableSortValue = string | number | null | undefined;

export interface DataTableSortState {
  columnIndex: number;
  direction: DataTableSortDirection;
}

export interface PageShellProps {
  title: string;
  subtitle?: string;
  backHref?: string;
  backLabel?: string;
  meta?: ReactNode;
  actions?: ReactNode;
  children: ReactNode;
}

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

export interface ReleaseBlockerSummaryItem {
  code: string;
  trackCode?: string;
  trackLabel: string;
  severity: string;
  riskRank: number;
  blockingIssue: string;
  evidenceArtifact: string;
  nextAction: string;
  blocksRelease: boolean;
}

export interface ReleaseBlockerSummaryProps {
  blockers: ReleaseBlockerSummaryItem[];
  title?: string;
  description?: string;
  actionHref?: string;
  actionLabel?: string;
  maxVisible?: number;
}

export interface FilingActionBarProps {
  children: ReactNode;
  title?: string;
  description?: ReactNode;
  status?: ReactNode;
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

export interface ReadOnlyNoticeProps {
  subject: string;
  detail?: ReactNode;
  className?: string;
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
  sortValues?: DataTableSortValue[];
}

export type DataTableRow = ReactNode[] | DataTableRichRow;

export interface DataGridProps {
  columns: string[];
  rows: DataTableRow[];
  caption?: string;
  filterPlaceholder?: string;
  emptyState?: ReactNode;
  totals?: ReactNode[];
  defaultSort?: DataTableSortState | null;
  sortableColumns?: boolean[];
}

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

export function PageShell({
  title,
  subtitle,
  backHref,
  backLabel = "Back",
  meta,
  actions,
  children,
}: PageShellProps) {
  return (
    <WorkbenchShell>
      {backHref && (
        <Link
          href={backHref}
          className="inline-flex min-h-8 items-center gap-2 text-sm font-medium text-[var(--muted-foreground)] transition hover:text-[var(--foreground)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-emerald-500"
        >
          <ArrowLeft className="h-4 w-4" aria-hidden="true" />
          {backLabel}
        </Link>
      )}
      <WorkbenchHeader title={title} subtitle={subtitle} meta={meta} actions={actions} />
      {children}
    </WorkbenchShell>
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

export function ReadOnlyNotice({
  subject,
  detail = "Evidence remains visible; editing requires Owner or Accountant access.",
  className = "",
}: ReadOnlyNoticeProps) {
  return (
    <div
      role="status"
      aria-label="Read-only workflow access"
      className={`flex items-start gap-3 rounded-md border border-amber-200 bg-amber-50 px-3 py-2 text-amber-900 dark:border-amber-800 dark:bg-amber-950/50 dark:text-amber-100 ${className}`}
    >
      <LockKeyhole className="mt-0.5 h-4 w-4 shrink-0 text-amber-600 dark:text-amber-300" />
      <div className="min-w-0">
        <p className="text-xs font-semibold">Read-only workflow access</p>
        <p className="mt-0.5 text-xs leading-5">
          Your role has read-only access to {subject}. {detail}
        </p>
      </div>
    </div>
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
  const titleId = useId();

  return (
    <section
      aria-labelledby={titleId}
      className="min-w-0 rounded-md border border-[var(--border)] bg-[var(--surface)] shadow-sm shadow-black/[0.03]"
    >
      <header className="flex flex-col gap-3 border-b border-[var(--border)] px-4 py-3 md:flex-row md:items-center md:justify-between">
        <div>
          <h2 id={titleId} className="text-sm font-semibold text-[var(--foreground)]">{title}</h2>
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

export function ReleaseBlockerSummary({
  blockers,
  title = "Production release blockers",
  description = "Platform-level release gates that must be cleared before real filing packs are trusted.",
  actionHref = "/production-readiness",
  actionLabel = "Open production readiness",
  maxVisible = 3,
}: ReleaseBlockerSummaryProps) {
  const openBlockers = blockers
    .filter((blocker) => blocker.blocksRelease)
    .sort((a, b) => a.riskRank - b.riskRank || a.trackLabel.localeCompare(b.trackLabel))
    .slice(0, maxVisible);
  const openBlockerCount = blockers.filter((blocker) => blocker.blocksRelease).length;

  if (openBlockerCount === 0) {
    return (
      <section
        aria-label={title}
        data-workbench-release-blocker-summary="true"
        className="rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3"
      >
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <h3 className="text-sm font-semibold text-[var(--foreground)]">{title}</h3>
            <p className="mt-1 text-xs leading-5 text-[var(--muted-foreground)]">
              No platform-level release blockers are reported by the production readiness register.
            </p>
          </div>
          <StatusBadge tone="good">Clear</StatusBadge>
        </div>
      </section>
    );
  }

  return (
    <section
      aria-label={title}
      data-workbench-release-blocker-summary="true"
      className="rounded-md border border-red-200 bg-red-50/60 p-3 dark:border-red-900 dark:bg-red-950/25"
    >
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h3 className="text-sm font-semibold text-[var(--foreground)]">{title}</h3>
          <p className="mt-1 text-xs leading-5 text-[var(--muted-foreground)]">{description}</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <StatusBadge tone="bad">{formatIssueCount(openBlockerCount, "blocker")}</StatusBadge>
          {actionHref && (
            <Link
              href={actionHref}
              className="inline-flex min-h-8 items-center gap-2 rounded-md border border-[var(--border)] bg-[var(--surface)] px-3 text-xs font-semibold text-[var(--foreground)] hover:border-[var(--ring)]"
            >
              {actionLabel}
              <ArrowRight className="h-3.5 w-3.5" />
            </Link>
          )}
        </div>
      </div>
      <div className="mt-3 grid gap-3 lg:grid-cols-3">
        {openBlockers.map((blocker) => (
          <div key={blocker.code} className="min-w-0 rounded-md border border-[var(--border)] bg-[var(--surface)] p-3">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <StatusBadge tone={releaseBlockerTone(blocker)}>{blocker.trackLabel}</StatusBadge>
              <StatusBadge tone={blocker.severity === "critical" ? "bad" : "warn"}>Risk {blocker.riskRank}</StatusBadge>
            </div>
            <p className="mt-2 text-sm font-semibold leading-5 text-[var(--foreground)]">{blocker.blockingIssue}</p>
            <code className="mt-2 block break-all text-[11px] text-[var(--muted-foreground)]">{blocker.evidenceArtifact}</code>
            <p className="mt-2 text-xs leading-5 text-[var(--muted-foreground)]">{blocker.nextAction}</p>
          </div>
        ))}
      </div>
    </section>
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

function releaseBlockerTone(blocker: ReleaseBlockerSummaryItem): Tone {
  if (blocker.severity === "critical" || blocker.riskRank <= 5) return "bad";
  if (blocker.severity === "high" || blocker.riskRank <= 30) return "warn";
  return "info";
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
  const openRequired = items.filter((item) => item.required && !item.satisfied);
  const advisory = items.filter((item) => !item.required && !item.satisfied);
  const complete = items.filter((item) => item.satisfied);

  return (
    <section
      aria-label="Evidence checklist"
      className="overflow-hidden rounded-md border border-[var(--border)] bg-[var(--surface)]"
    >
      <div className="border-b border-[var(--border)] bg-[var(--surface-subtle)] px-4 py-3">
        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div className="min-w-0">
            <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">Evidence progress</p>
            <p className="mt-1 text-sm font-medium text-[var(--foreground)]">
              {openRequired.length === 0 ? "Required evidence is complete" : "Open evidence is listed first for review"}
            </p>
          </div>
          <div className="flex flex-wrap gap-2">
            <StatusBadge tone={openRequired.length > 0 ? "bad" : "good"}>
              {formatEvidenceCount(openRequired.length, "open required", "open required")}
            </StatusBadge>
            <StatusBadge tone={advisory.length > 0 ? "warn" : "good"}>
              {formatEvidenceCount(advisory.length, "advisory", "advisories")}
            </StatusBadge>
            <StatusBadge tone="good">
              {formatEvidenceCount(complete.length, "complete", "complete")}
            </StatusBadge>
          </div>
        </div>
      </div>

      {items.length === 0 ? (
        <p className="px-4 py-3 text-sm text-[var(--muted-foreground)]">No evidence items recorded.</p>
      ) : (
        <div className="divide-y divide-[var(--border)]">
          <EvidenceChecklistGroup title="Open evidence" items={openRequired} />
          <EvidenceChecklistGroup title="Advisory evidence" items={advisory} />
          <EvidenceChecklistGroup title="Completed evidence" items={complete} />
        </div>
      )}
    </section>
  );
}

function EvidenceChecklistGroup({
  title,
  items,
}: {
  title: string;
  items: EvidenceItem[];
}) {
  if (items.length === 0) return null;

  return (
    <div role="group" aria-label={title}>
      <div className="border-b border-[var(--border)] bg-[var(--surface-subtle)] px-4 py-2">
        <p className="text-[11px] font-semibold uppercase text-[var(--muted-foreground)]">{title}</p>
      </div>
      <div className="divide-y divide-[var(--border)]">
        {items.map((item) => (
          <EvidenceChecklistRow key={item.code} item={item} />
        ))}
      </div>
    </div>
  );
}

function EvidenceChecklistRow({ item }: { item: EvidenceItem }) {
  const tone: Tone = item.satisfied ? "good" : item.required ? "bad" : "warn";

  return (
    <div className="grid gap-3 px-4 py-3 sm:grid-cols-[minmax(0,1fr)_auto] sm:items-center">
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
}

function formatEvidenceCount(count: number, singular: string, plural: string) {
  return `${count} ${count === 1 ? singular : plural}`;
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
  ...props
}: DataGridProps) {
  return <DataGridBase {...props} surfaceClassName="workbench-data-table" />;
}

export function DataGrid({
  ...props
}: DataGridProps) {
  return <DataGridBase {...props} surfaceClassName="workbench-data-grid" />;
}

function DataGridBase({
  columns,
  rows,
  caption,
  filterPlaceholder,
  emptyState = "No rows to show",
  totals,
  defaultSort = null,
  sortableColumns,
  surfaceClassName,
}: DataGridProps & { surfaceClassName: string }) {
  const isColumnSortable = (columnIndex: number) => sortableColumns?.[columnIndex] ?? true;
  const [filter, setFilter] = useState("");
  const [sortState, setSortState] = useState<DataTableSortState | null>(
    defaultSort && isColumnSortable(defaultSort.columnIndex) ? defaultSort : null,
  );
  const normalizedRows = useMemo(() => rows.map(normalizeDataTableRow), [rows]);
  const normalizedFilter = filter.trim().toLowerCase();
  const visibleRows = useMemo(() => {
    const filteredRows = normalizedFilter
      ? normalizedRows.filter((row) => row.searchText.toLowerCase().includes(normalizedFilter))
      : normalizedRows;

    if (!sortState) return filteredRows;

    return [...filteredRows].sort((left, right) => {
      const comparison = compareDataTableSortValues(
        left.sortValues[sortState.columnIndex],
        right.sortValues[sortState.columnIndex],
      );
      return sortState.direction === "asc" ? comparison : -comparison;
    });
  }, [normalizedFilter, normalizedRows, sortState]);
  const tableLabel = caption ?? "Workbench data table";
  const showFilter = Boolean(filterPlaceholder);
  const toggleSort = (columnIndex: number) => {
    if (!isColumnSortable(columnIndex)) return;

    setSortState((current) => {
      if (current?.columnIndex !== columnIndex) {
        return { columnIndex, direction: "asc" };
      }

      return {
        columnIndex,
        direction: current.direction === "asc" ? "desc" : "asc",
      };
    });
  };

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
        className={`${surfaceClassName} min-w-0 overflow-x-auto rounded-md border border-[var(--border)] bg-[var(--surface)]`}
        data-responsive="card"
      >
      <table className="min-w-full border-collapse text-left text-sm" aria-label={tableLabel}>
        {caption && <caption className="sr-only">{caption}</caption>}
        <thead className="bg-[var(--surface-subtle)] text-xs font-semibold uppercase text-[var(--muted-foreground)]">
          <tr>
            {columns.map((column, columnIndex) => {
              const isSorted = sortState?.columnIndex === columnIndex;
              const isSortable = isColumnSortable(columnIndex);
              return (
                <th
                  key={column}
                  aria-sort={isSortable ? (isSorted ? (sortState.direction === "asc" ? "ascending" : "descending") : "none") : undefined}
                  className="whitespace-nowrap border-b border-[var(--border)] px-4 py-3"
                >
                  {isSortable ? (
                    <button
                      type="button"
                      aria-label={`Sort by ${column}`}
                      onClick={() => toggleSort(columnIndex)}
                      className="inline-flex min-h-7 items-center gap-1.5 rounded-sm text-left font-semibold uppercase text-[var(--muted-foreground)] transition hover:text-[var(--foreground)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-emerald-500"
                    >
                      <span>{column}</span>
                      <SortIcon direction={isSorted ? sortState.direction : null} />
                    </button>
                  ) : (
                    <span className="inline-flex min-h-7 items-center text-left font-semibold uppercase text-[var(--muted-foreground)]">
                      {column}
                    </span>
                  )}
                </th>
              );
            })}
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

function SortIcon({ direction }: { direction: DataTableSortDirection | null }) {
  if (direction === "asc") {
    return <ArrowUp aria-hidden="true" className="h-3.5 w-3.5" />;
  }

  if (direction === "desc") {
    return <ArrowDown aria-hidden="true" className="h-3.5 w-3.5" />;
  }

  return <ArrowUpDown aria-hidden="true" className="h-3.5 w-3.5 opacity-60" />;
}

function normalizeDataTableRow(row: DataTableRow, rowIndex: number): Required<DataTableRichRow> {
  if (Array.isArray(row)) {
    const fallbackId = row.map(cellText).join("|");
    return {
      id: `row-${rowIndex}-${fallbackId || "legacy"}`,
      cells: row,
      searchText: row.map(cellText).join(" "),
      tone: "default",
      sortValues: row.map(cellText),
    };
  }

  const fallbackId = row.cells.map(cellText).join("|");
  return {
    id: row.id ?? `row-${rowIndex}-${fallbackId || "rich"}`,
    cells: row.cells,
    searchText: row.searchText ?? row.cells.map(cellText).join(" "),
    tone: row.tone ?? "default",
    sortValues: row.sortValues ?? row.cells.map(cellText),
  };
}

function compareDataTableSortValues(left: DataTableSortValue, right: DataTableSortValue) {
  if (left === right) return 0;
  if (left === null || left === undefined || left === "") return 1;
  if (right === null || right === undefined || right === "") return -1;
  if (typeof left === "number" && typeof right === "number") return left - right;

  return String(left).localeCompare(String(right), "en-IE", {
    numeric: true,
    sensitivity: "base",
  });
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

export function FilingActionBar({
  children,
  title = "Filing workflow actions",
  description,
  status,
}: FilingActionBarProps) {
  const titleId = useId();

  return (
    <section
      aria-labelledby={titleId}
      data-workbench-filing-action-bar="true"
      className="grid gap-3 rounded-md border border-[var(--border)] bg-[var(--surface)] p-3 sm:grid-cols-[minmax(0,1fr)_auto] sm:items-center"
    >
      <div className="min-w-0">
        <div className="flex min-w-0 flex-wrap items-center gap-2">
          <h3 id={titleId} className="text-sm font-semibold text-[var(--foreground)]">
            {title}
          </h3>
          {status}
        </div>
        {description && (
          <p className="mt-1 text-xs leading-5 text-[var(--muted-foreground)]">
            {description}
          </p>
        )}
      </div>
      <div role="group" aria-label="Available filing actions" className="flex min-w-0 flex-col gap-2 sm:flex-row sm:flex-wrap sm:justify-end">
        {children}
      </div>
    </section>
  );
}
