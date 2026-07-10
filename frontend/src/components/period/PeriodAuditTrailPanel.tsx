"use client";

import { Button, Spinner } from "@heroui/react";
import { Fragment } from "react";
import { FileClock } from "lucide-react";
import type { AuditLogEntry } from "@/lib/api";
import { HorizontalScrollRegion, ReviewPanel, StatusBadge } from "@/components/workbench";
import { ResourceStateNotice } from "@/components/ResourceStateNotice";
import type { ResourceState } from "@/lib/resourceState";

interface PeriodAuditTrailPanelProps {
  auditLog: AuditLogEntry[];
  auditTotal: number;
  page: number;
  pageSize: number;
  totalPages: number;
  loading: boolean;
  error: string | null;
  resourceState?: ResourceState;
  onPageChange: (page: number) => void;
  onPageSizeChange: (pageSize: number) => void;
  onRetry: () => void | Promise<void>;
}

export function PeriodAuditTrailPanel({
  auditLog,
  auditTotal,
  page,
  pageSize,
  totalPages,
  loading,
  error,
  resourceState,
  onPageChange,
  onPageSizeChange,
  onRetry,
}: PeriodAuditTrailPanelProps) {
  const firstVisibleEvent = auditTotal === 0 ? 0 : ((page - 1) * pageSize) + 1;
  const lastVisibleEvent = Math.min(page * pageSize, auditTotal);

  return (
    <ReviewPanel
      title="Period audit trail"
      description="Review actions and evidence changes for this period. Every retained event is reachable."
      actions={<StatusBadge tone={auditLog.length > 0 ? "info" : "default"}>{auditTotal} events</StatusBadge>}
    >
      <div className="space-y-3" aria-busy={loading}>
        {resourceState && (
          <ResourceStateNotice state={resourceState} label="period audit events" onRetry={onRetry} compact />
        )}
        {error && !resourceState && (
          <div className="flex flex-col gap-3 rounded-md border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-800 dark:border-red-800 dark:bg-red-950/30 dark:text-red-200 sm:flex-row sm:items-center sm:justify-between" role="alert">
            <span>{error}</span>
            <Button variant="outline" size="sm" onPress={onRetry} isDisabled={loading}>
              Retry audit events
            </Button>
          </div>
        )}

        {loading && auditLog.length === 0 && !resourceState ? (
          <div className="flex items-center justify-center gap-2 rounded-md border border-dashed border-[var(--border)] bg-[var(--surface-subtle)] px-4 py-6 text-sm text-[var(--muted-foreground)]" role="status">
            <Spinner size="sm" />
            Loading audit events…
          </div>
        ) : auditLog.length === 0 && !error && (!resourceState || resourceState.status === "empty") ? (
          <div className="rounded-md border border-dashed border-[var(--border)] bg-[var(--surface-subtle)] px-4 py-6 text-sm text-[var(--muted-foreground)]">
            No audit events recorded for this period yet.
          </div>
        ) : auditLog.length > 0 ? (
          <HorizontalScrollRegion label="Period audit events table">
            <table className="min-w-[46rem] w-full text-sm">
            <thead>
              <tr className="border-b border-[var(--border)] text-left text-xs uppercase text-[var(--muted-foreground)]">
                <th className="py-2 pr-4">Time</th>
                <th className="py-2 pr-4">Action</th>
                <th className="py-2 pr-4">Record</th>
                <th className="py-2 pr-4">Reviewer</th>
              </tr>
            </thead>
            <tbody>
              {auditLog.map((entry) => (
                <Fragment key={entry.id}>
                  <tr className="border-b border-[var(--border)]">
                    <td className="whitespace-nowrap py-2 pr-4 text-[var(--muted-foreground)]">
                      {formatAuditTimestamp(entry.timestamp)}
                    </td>
                    <td className="py-2 pr-4 font-medium text-[var(--foreground)]">{entry.action}</td>
                    <td className="py-2 pr-4 text-[var(--muted-foreground)]">
                      {entry.entityType} #{entry.entityId}
                    </td>
                    <td className="py-2 pr-4 text-[var(--muted-foreground)]">{entry.userId || "System"}</td>
                  </tr>
                  {(entry.oldValueJson || entry.newValueJson) && (
                    <tr className="border-b border-[var(--border)] bg-[var(--surface-subtle)]">
                      <td colSpan={4} className="px-3 py-3">
                        <div className="space-y-2">
                          <p className="flex items-center gap-2 text-xs font-semibold uppercase text-[var(--muted-foreground)]">
                            <FileClock className="h-3.5 w-3.5" />
                            Audit details
                          </p>
                          <div className="grid gap-3 md:grid-cols-2">
                            {entry.oldValueJson && (
                              <AuditPayload label="Old value" payload={entry.oldValueJson} />
                            )}
                            {entry.newValueJson && (
                              <AuditPayload label="New value" payload={entry.newValueJson} />
                            )}
                          </div>
                        </div>
                      </td>
                    </tr>
                  )}
                </Fragment>
              ))}
            </tbody>
            </table>
          </HorizontalScrollRegion>
        ) : null}

        {loading && auditLog.length > 0 && !resourceState && (
          <p className="flex items-center gap-2 text-xs text-[var(--muted-foreground)]" role="status">
            <Spinner size="sm" />
            Refreshing audit events…
          </p>
        )}

        {auditTotal > 0 && (
          <div
            className="flex flex-col gap-3 rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] px-3 py-3 text-xs text-[var(--muted-foreground)] md:flex-row md:items-center md:justify-between"
            role="navigation"
            aria-label="Audit log pagination"
          >
            <p aria-live="polite">
              Showing {firstVisibleEvent}–{lastVisibleEvent} of {auditTotal} audit events
            </p>
            <div className="flex flex-wrap items-center gap-3">
              <label className="flex items-center gap-2">
                <span>Events per page</span>
                <select
                  aria-label="Events per page"
                  className="rounded-md border border-[var(--border)] bg-[var(--surface)] px-2 py-1 text-xs text-[var(--foreground)]"
                  value={pageSize}
                  onChange={(event) => onPageSizeChange(Number(event.target.value))}
                  disabled={loading}
                >
                  <option value="25">25</option>
                  <option value="50">50</option>
                  <option value="100">100</option>
                </select>
              </label>
              <span>Page {page} of {totalPages}</span>
              <Button
                variant="outline"
                size="sm"
                aria-label="Previous audit page"
                onPress={() => onPageChange(page - 1)}
                isDisabled={loading || page <= 1}
              >
                Previous
              </Button>
              <Button
                variant="outline"
                size="sm"
                aria-label="Next audit page"
                onPress={() => onPageChange(page + 1)}
                isDisabled={loading || page >= totalPages}
              >
                Next
              </Button>
            </div>
          </div>
        )}
      </div>
    </ReviewPanel>
  );
}

function AuditPayload({ label, payload }: { label: string; payload: string }) {
  return (
    <div>
      <p className="mb-1 text-xs font-medium text-[var(--muted-foreground)]">{label}</p>
      <pre className="max-h-40 overflow-auto whitespace-pre-wrap break-all rounded-md border border-[var(--border)] bg-[var(--surface)] p-3 text-xs text-[var(--foreground)]">
        {formatAuditPayload(payload)}
      </pre>
    </div>
  );
}

function formatAuditTimestamp(value: string) {
  return new Date(value).toLocaleString("en-IE", {
    dateStyle: "medium",
    timeStyle: "short",
  });
}

function formatAuditPayload(value: string) {
  try {
    return JSON.stringify(JSON.parse(value), null, 2);
  } catch {
    return value;
  }
}
