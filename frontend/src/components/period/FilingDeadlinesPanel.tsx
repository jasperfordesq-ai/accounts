import { Button, Spinner } from "@heroui/react";
import { CalendarClock } from "lucide-react";
import type { FilingDeadline, FilingWorkflowStatus } from "@/lib/api";
import { ReviewPanel, StatusBadge } from "@/components/workbench";

interface FilingDeadlinesPanelProps {
  deadlines: FilingDeadline[];
  filingStatus: FilingWorkflowStatus | null;
  filingReferences: Record<number, string>;
  markingFiledId: number | null;
  onFilingReferenceChange: (deadlineId: number, value: string) => void;
  onMarkFiled: (deadline: FilingDeadline, filingReference?: string) => void | Promise<void>;
  onReferenceMissing: (message: string) => void;
}

export function FilingDeadlinesPanel({
  deadlines,
  filingStatus,
  filingReferences,
  markingFiledId,
  onFilingReferenceChange,
  onMarkFiled,
  onReferenceMissing,
}: FilingDeadlinesPanelProps) {
  if (deadlines.length === 0) return null;

  return (
    <ReviewPanel
      title="Filing deadlines"
      description="Recorded CRO, Revenue and charity filing states for this period."
      actions={<StatusBadge tone="info">{deadlines.length} tracked</StatusBadge>}
    >
      <div className="space-y-3">
        {deadlines.map((deadline) => {
          const reference = deadlineReference(deadline, filingReferences, filingStatus);
          const requiresReference = referenceRequired(deadline);

          return (
            <div
              key={deadline.id}
              className="grid gap-3 rounded-md border border-[var(--border)] bg-[var(--surface)] px-4 py-3 md:grid-cols-[minmax(0,1fr)_auto] md:items-center"
            >
              <div className="min-w-0">
                <div className="flex min-w-0 items-center gap-2">
                  <CalendarClock className="h-4 w-4 shrink-0 text-[var(--muted-foreground)]" />
                  <p className="truncate text-sm font-semibold text-[var(--foreground)]">
                    {deadline.deadlineType} Filing
                  </p>
                </div>
                <p className="mt-1 text-xs text-[var(--muted-foreground)]">
                  Due {formatDate(deadline.dueDate)}
                </p>
                {deadline.filingReference && (
                  <p className="mt-1 text-xs text-[var(--muted-foreground)]">
                    Reference: <span className="font-medium text-[var(--foreground)]">{deadline.filingReference}</span>
                  </p>
                )}
              </div>

              <div className="flex min-w-0 flex-col gap-2 sm:flex-row sm:items-center sm:justify-end">
                {deadline.filedDate ? (
                  <StatusBadge tone={deadline.isLate ? "bad" : "good"}>
                    Filed {formatDate(deadline.filedDate)}
                    {deadline.isLate ? ` (${deadline.penaltyAmount > 0 ? `${formatMoney(deadline.penaltyAmount)} penalty` : "Late"})` : ""}
                  </StatusBadge>
                ) : (
                  <>
                    {requiresReference && (
                      <input
                        aria-label={deadline.deadlineType === "Revenue" ? "Revenue ROS or CT1 filing reference" : "Charities Regulator annual return reference"}
                        title={deadline.deadlineType === "Revenue" ? "Revenue ROS or CT1 filing reference" : "Charities Regulator annual return reference"}
                        value={reference}
                        onChange={(event) => onFilingReferenceChange(deadline.id, event.target.value)}
                        className="h-9 w-full rounded-md border border-[var(--border)] bg-[var(--surface)] px-3 text-sm text-[var(--foreground)] outline-none transition focus:border-[var(--ring)] focus:ring-2 focus:ring-teal-100 dark:focus:ring-teal-900/40 sm:w-56"
                        placeholder={deadline.deadlineType === "Revenue" ? "ROS/CT1 reference" : "Annual return reference"}
                      />
                    )}
                    <Button
                      variant="outline"
                      size="sm"
                      isDisabled={markingFiledId === deadline.id}
                      onPress={() => {
                        const normalisedReference = reference.trim();
                        if (deadline.deadlineType === "Revenue" && !normalisedReference) {
                          onReferenceMissing("Revenue filing reference is required");
                          return;
                        }
                        if (deadline.deadlineType === "Charity" && !normalisedReference) {
                          onReferenceMissing("Charity annual return reference is required");
                          return;
                        }
                        void onMarkFiled(deadline, normalisedReference || undefined);
                      }}
                    >
                      {markingFiledId === deadline.id ? <Spinner size="sm" /> : "Mark as Filed"}
                    </Button>
                  </>
                )}
              </div>
            </div>
          );
        })}
      </div>
    </ReviewPanel>
  );
}

function deadlineReference(
  deadline: FilingDeadline,
  filingReferences: Record<number, string>,
  filingStatus: FilingWorkflowStatus | null,
) {
  if (filingReferences[deadline.id] !== undefined) return filingReferences[deadline.id];
  if (deadline.deadlineType === "Revenue") return filingStatus?.revenue.ct1Reference ?? "";
  if (deadline.deadlineType === "Charity") {
    return filingStatus?.charity.annualReturnReference ?? deadline.filingReference ?? "";
  }
  return deadline.filingReference ?? "";
}

function referenceRequired(deadline: FilingDeadline) {
  return deadline.deadlineType === "Revenue" || deadline.deadlineType === "Charity";
}

function formatDate(date: string) {
  return new Date(date).toLocaleDateString("en-IE", {
    day: "2-digit",
    month: "short",
    year: "numeric",
  });
}

function formatMoney(amount: number) {
  return new Intl.NumberFormat("en-IE", {
    style: "currency",
    currency: "EUR",
  }).format(amount);
}
