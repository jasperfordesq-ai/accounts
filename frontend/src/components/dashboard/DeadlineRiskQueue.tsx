"use client";

import Link from "next/link";
import { Button, Spinner } from "@heroui/react";
import { AlertTriangle, BellRing, RefreshCw } from "lucide-react";
import type { DeadlineRiskQueueItem } from "@/lib/operations";
import { StatusBadge } from "@/components/workbench";
import type { ResourceState } from "@/lib/resourceState";
import { ResourceStateNotice } from "@/components/ResourceStateNotice";

export function DeadlineRiskQueue({
  items,
  state,
  canRunDelivery,
  busyKey,
  actionMessage,
  onReload,
  onRetry,
  onRunDelivery,
}: {
  items: DeadlineRiskQueueItem[];
  state: ResourceState;
  canRunDelivery: boolean;
  busyKey: string | null;
  actionMessage: string | null;
  onReload: () => void | Promise<void>;
  onRetry: (outboxId: string) => void | Promise<void>;
  onRunDelivery: () => void | Promise<void>;
}) {
  return (
    <section aria-labelledby="deadline-risk-heading" className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-4 shadow-sm shadow-black/[0.03]">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 id="deadline-risk-heading" className="flex items-center gap-2 text-base font-semibold"><BellRing className="h-4 w-4 text-amber-600" /> Reminder delivery at risk</h2>
          <p className="mt-1 text-sm text-[var(--muted-foreground)]">Failed, queued and in-progress deadline reminders that need operational attention. No reminder marks a filing as submitted.</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button size="sm" variant="secondary" onPress={() => void onReload()} isDisabled={busyKey !== null}>
            <RefreshCw className="h-4 w-4" />Refresh queue
          </Button>
          {canRunDelivery && (
            <Button size="sm" variant="primary" onPress={() => void onRunDelivery()} isDisabled={busyKey !== null}>
              {busyKey === "run" ? <Spinner size="sm" /> : <BellRing className="h-4 w-4" />}Run delivery cycle
            </Button>
          )}
        </div>
      </div>
      <div className="mt-3"><ResourceStateNotice state={state} label="deadline reminder risk queue" onRetry={onReload} /></div>
      {actionMessage && <p role="status" aria-live="polite" className="mt-3 rounded-md border border-sky-200 bg-sky-50 px-3 py-2 text-sm text-sky-900 dark:border-sky-900 dark:bg-sky-950/30 dark:text-sky-100">{actionMessage}</p>}
      {(state.status === "loaded" || state.status === "empty" || state.hasRetainedData) && items.length === 0 && (
        <p className="mt-3 text-sm text-[var(--muted-foreground)]">No pending or failed reminder delivery is currently recorded.</p>
      )}
      {items.length > 0 && (
        <ul className="mt-3 divide-y divide-[var(--border)] border-y border-[var(--border)]" aria-label="Reminder delivery risk items">
          {items.map((item) => (
            <li key={item.outboxId} className="grid gap-3 py-3 md:grid-cols-[minmax(0,1fr)_auto] md:items-center">
              <div className="min-w-0">
                <div className="flex flex-wrap items-center gap-2">
                  <Link className="font-semibold text-emerald-700 underline-offset-2 hover:underline dark:text-emerald-300" href={`/companies/${item.companyId}/periods/${item.periodId}?tab=filing`}>{item.companyLegalName}</Link>
                  <StatusBadge tone={item.state === 2 ? "bad" : item.reminderKind === 1 ? "warn" : "info"}>{stateLabel(item.state)}</StatusBadge>
                  <StatusBadge tone="default">{deadlineLabel(item.deadlineType)} / {kindLabel(item.reminderKind)}</StatusBadge>
                </div>
                <p className="mt-1 text-sm text-[var(--muted-foreground)]">Due {formatDate(item.dueDate)} · {item.attemptCount} attempt{item.attemptCount === 1 ? "" : "s"} · next attempt {formatDateTime(item.nextAttemptAtUtc)}</p>
                {item.lastFailureCode && <p className="mt-1 flex items-center gap-1 text-xs font-medium text-red-700 dark:text-red-300"><AlertTriangle className="h-3.5 w-3.5" />Failure code: {item.lastFailureCode}</p>}
              </div>
              {item.state === 2 && (
                <Button size="sm" variant="secondary" onPress={() => void onRetry(item.outboxId)} isDisabled={busyKey !== null}>
                  {busyKey === item.outboxId ? <Spinner size="sm" /> : <RefreshCw className="h-4 w-4" />}Retry now
                </Button>
              )}
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

const deadlineLabels = ["CRO", "Charities Regulator", "Revenue"] as const;
const kindLabels = ["due soon", "overdue", "corrected date"] as const;
const stateLabels = ["Pending", "Delivering", "Retry scheduled", "Delivered", "Cancelled", "Superseded"] as const;
const deadlineLabel = (value: 0 | 1 | 2) => deadlineLabels[value];
const kindLabel = (value: 0 | 1 | 2) => kindLabels[value];
const stateLabel = (value: 0 | 1 | 2 | 3 | 4 | 5) => stateLabels[value];
const formatDate = (value: string) => new Intl.DateTimeFormat("en-IE", { dateStyle: "medium" }).format(new Date(`${value}T00:00:00Z`));
const formatDateTime = (value: string) => new Intl.DateTimeFormat("en-IE", { dateStyle: "medium", timeStyle: "short", timeZone: "UTC" }).format(new Date(value));
