import { Button, Spinner } from "@heroui/react";
import { CalendarClock } from "lucide-react";
import type { FilingDeadline, FilingWorkflowStatus } from "@/lib/api";
import { ReviewPanel, StatusBadge } from "@/components/workbench";

interface FilingDeadlinesPanelProps {
  canReview?: boolean;
  deadlines: FilingDeadline[];
  filingStatus: FilingWorkflowStatus | null;
  filingReferences: Record<number, string>;
  markingFiledId: number | null;
  evidenceAvailable?: boolean;
  onFilingReferenceChange: (deadlineId: number, value: string) => void;
  onMarkFiled: (deadline: FilingDeadline, filingReference?: string) => void | Promise<void>;
  onReferenceMissing: (message: string) => void;
}

export function FilingDeadlinesPanel({
  canReview = false,
  deadlines,
  filingStatus,
  filingReferences,
  markingFiledId,
  evidenceAvailable = true,
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
                  {deadline.manualOverrideStatus === "Active" ? "Effective override due" : "Due"} {formatDate(deadline.dueDate)}
                </p>
                {deadline.deadlineType === "CRO" && deadline.annualReturnDate && (
                  <div className="mt-2 grid gap-x-4 gap-y-1 text-xs text-[var(--muted-foreground)] sm:grid-cols-2">
                    <span>Exact ARD: <strong className="font-medium text-[var(--foreground)]">{formatDate(deadline.annualReturnDate)}</strong></span>
                    <span>B1 made up to: <strong className="font-medium text-[var(--foreground)]">{formatDate(deadline.returnMadeUpToDate ?? deadline.annualReturnDate)}</strong></span>
                    {deadline.financialStatementsLatestMadeUpToDate && (
                      <span>Accounts age limit: <strong className="font-medium text-[var(--foreground)]">{formatDate(deadline.financialStatementsLatestMadeUpToDate)}</strong></span>
                    )}
                    {deadline.deliveryDueDate && (
                      <span>56-day delivery date: <strong className="font-medium text-[var(--foreground)]">{formatDate(deadline.deliveryDueDate)}</strong></span>
                    )}
                  </div>
                )}
                {deadline.madeUpToDateBroughtForwardForAccountsAge && (
                  <p className="mt-2 text-xs font-medium text-amber-800 dark:text-amber-200">
                    B1 made-up-to date is earlier than the ARD to satisfy the nine-month financial-statement age rule.
                  </p>
                )}
                {deadline.manualOverrideStatus && (
                  <div className={`mt-2 rounded border px-2.5 py-2 text-xs ${deadline.manualOverrideStatus === "Active" ? "border-blue-200 bg-blue-50 text-blue-900 dark:border-blue-900 dark:bg-blue-950/30 dark:text-blue-100" : "border-red-200 bg-red-50 text-red-900 dark:border-red-900 dark:bg-red-950/30 dark:text-red-100"}`}>
                    <strong>{deadline.manualOverrideStatus === "Active" ? "Reviewed due-date override active" : "Due-date override needs review"}</strong>
                    {deadline.manualOverrideEvidenceReference ? ` — ${deadline.manualOverrideEvidenceReference}` : ""}
                  </div>
                )}
                {deadline.calculationSourceUrl && (
                  <a href={deadline.calculationSourceUrl} target="_blank" rel="noreferrer" className="mt-2 inline-block text-xs font-medium text-teal-700 underline underline-offset-2 dark:text-teal-300">
                    CRO calculation guidance
                  </a>
                )}
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
                ) : canReview ? (
                  <>
                    {requiresReference && (
                      <input
                        aria-label={deadline.deadlineType === "Revenue" ? "Revenue ROS or CT1 filing reference" : "Charities Regulator annual return reference"}
                        title={deadline.deadlineType === "Revenue" ? "Revenue ROS or CT1 filing reference" : "Charities Regulator annual return reference"}
                        value={reference}
                        disabled={!evidenceAvailable}
                        onChange={(event) => onFilingReferenceChange(deadline.id, event.target.value)}
                        className="h-9 w-full rounded-md border border-[var(--control-border)] bg-[var(--surface)] px-3 text-sm text-[var(--foreground)] outline-none transition focus:border-[var(--ring)] focus:ring-2 focus:ring-teal-100 dark:focus:ring-teal-900/40 sm:w-56"
                        placeholder={deadline.deadlineType === "Revenue" ? "ROS/CT1 reference" : "Annual return reference"}
                      />
                    )}
                    <Button
                      variant="outline"
                      size="sm"
                      aria-label={`Mark as Filed — ${deadline.deadlineType} deadline`}
                      isDisabled={!evidenceAvailable || markingFiledId === deadline.id}
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
                ) : (
                  <StatusBadge tone="info">Reviewer access required</StatusBadge>
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
