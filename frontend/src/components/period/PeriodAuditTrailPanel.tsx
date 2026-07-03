import { Fragment } from "react";
import { FileClock } from "lucide-react";
import type { AuditLogEntry } from "@/lib/api";
import { ReviewPanel, StatusBadge } from "@/components/workbench";

interface PeriodAuditTrailPanelProps {
  auditLog: AuditLogEntry[];
  auditTotal: number;
}

export function PeriodAuditTrailPanel({ auditLog, auditTotal }: PeriodAuditTrailPanelProps) {
  return (
    <ReviewPanel
      title="Period audit trail"
      description="Recent review actions and evidence changes for this period."
      actions={<StatusBadge tone={auditLog.length > 0 ? "info" : "default"}>{auditTotal} events</StatusBadge>}
    >
      {auditLog.length === 0 ? (
        <div className="rounded-md border border-dashed border-[var(--border)] bg-[var(--surface-subtle)] px-4 py-6 text-sm text-[var(--muted-foreground)]">
          No audit events recorded for this period yet.
        </div>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
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
          {auditTotal > auditLog.length && (
            <p className="mt-3 text-xs text-[var(--muted-foreground)]">
              Showing latest {auditLog.length} of {auditTotal} audit events.
            </p>
          )}
        </div>
      )}
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
