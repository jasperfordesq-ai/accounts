import type { ReactNode } from "react";
import { CheckCircle2, Circle, AlertTriangle } from "lucide-react";

type WorkflowState = "done" | "active" | "blocked" | "todo";

export interface WorkflowItem {
  label: string;
  detail: string;
  state: WorkflowState;
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
    <section className="mb-6 border-b border-gray-200 dark:border-neutral-800 pb-5">
      <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
        <div className="min-w-0">
          <h1 className="text-2xl font-semibold text-gray-950 dark:text-gray-50">
            {title}
          </h1>
          {subtitle && (
            <p className="mt-1 text-sm text-gray-500 dark:text-gray-400">
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

export function WorkflowRail({ items }: { items: WorkflowItem[] }) {
  return (
    <div className="mb-6 overflow-x-auto rounded-md border border-gray-200 bg-white dark:border-neutral-800 dark:bg-neutral-950">
      <div className="grid min-w-[760px] grid-cols-6 divide-x divide-gray-200 dark:divide-neutral-800">
        {items.map((item) => (
          <div key={item.label} className="p-3">
            <div className="flex items-center gap-2">
              <WorkflowIcon state={item.state} />
              <span className="text-xs font-semibold uppercase text-gray-700 dark:text-gray-300">
                {item.label}
              </span>
            </div>
            <p className="mt-1 text-xs leading-5 text-gray-500 dark:text-gray-400">
              {item.detail}
            </p>
          </div>
        ))}
      </div>
    </div>
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
    <section className="rounded-md border border-gray-200 bg-white dark:border-neutral-800 dark:bg-neutral-950">
      <header className="flex flex-col gap-3 border-b border-gray-200 px-4 py-3 dark:border-neutral-800 md:flex-row md:items-center md:justify-between">
        <div>
          <h2 className="text-sm font-semibold text-gray-950 dark:text-gray-50">{title}</h2>
          {description && (
            <p className="mt-0.5 text-xs text-gray-500 dark:text-gray-400">{description}</p>
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
  const toneClass = {
    default: "text-gray-950 dark:text-gray-50",
    good: "text-emerald-700 dark:text-emerald-400",
    warn: "text-amber-700 dark:text-amber-400",
    bad: "text-red-700 dark:text-red-400",
  };

  return (
    <div className="grid grid-cols-2 divide-x divide-y divide-gray-200 overflow-hidden rounded-md border border-gray-200 bg-white dark:divide-neutral-800 dark:border-neutral-800 dark:bg-neutral-950 md:grid-cols-4 md:divide-y-0">
      {metrics.map((metric) => (
        <div key={metric.label} className="px-4 py-3">
          <p className="text-xs font-medium text-gray-500 dark:text-gray-400">{metric.label}</p>
          <p className={`mt-1 text-lg font-semibold ${toneClass[metric.tone ?? "default"]}`}>
            {metric.value}
          </p>
        </div>
      ))}
    </div>
  );
}
