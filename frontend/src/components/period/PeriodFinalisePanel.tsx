"use client";

import { useState } from "react";
import { Button, Spinner } from "@heroui/react";
import { toast } from "sonner";
import { updatePeriodStatus, type AccountingPeriod } from "@/lib/api";
import { formatDateIE } from "@/lib/format";
import { isRecentMfaRequired } from "@/lib/filingEvidence";
import { useUnsavedChanges } from "@/lib/useUnsavedChanges";
import { ReviewPanel, StatusBadge } from "@/components/workbench";
import { Field, RecentVerificationPrompt, errorMessage, inputClass } from "@/components/period/FilingEvidenceShared";

const REOPEN_REASON_MIN = 10;

/** Today's calendar date in Ireland (Europe/Dublin) as YYYY-MM-DD, matching the backend default. */
export function irishTodayIsoDate(now = new Date()) {
  return new Intl.DateTimeFormat("en-CA", {
    timeZone: "Europe/Dublin",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).format(now);
}

/**
 * Finalising the period records the date the board approved the accounts (Companies Act 2014,
 * s.324). The final accounts and the s.324 approval page print that date.
 */
export function PeriodFinalisePanel({
  companyId,
  period,
  canFinalise,
  canReopen,
  onChanged,
}: {
  companyId: number;
  period: AccountingPeriod | null;
  /** Owner or Reviewer. */
  canFinalise: boolean;
  /** Owner only: the backend refuses reopening for every other role. */
  canReopen: boolean;
  onChanged?: () => void | Promise<void>;
}) {
  const [approvalDate, setApprovalDate] = useState<string | null>(null);
  const [reopenReason, setReopenReason] = useState("");
  const [reopening, setReopening] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [needsVerification, setNeedsVerification] = useState(false);
  // A changed board approval date or a reopening reason not yet submitted is unsaved work; a
  // successful submit clears both.
  const hasFinaliseDraft = period !== null
    && ((approvalDate !== null && approvalDate !== (period.approvalDate ?? irishTodayIsoDate()))
      || (reopening && reopenReason.trim() !== ""));
  useUnsavedChanges(hasFinaliseDraft);

  if (!period) return null;
  const locked = period.status === "Finalised" || period.status === "Filed";
  if (!locked && !canFinalise) return null;

  const today = irishTodayIsoDate();
  const chosenDate = approvalDate ?? period.approvalDate ?? today;

  async function submit(update: Parameters<typeof updatePeriodStatus>[2], success: string) {
    if (!period) return;
    setBusy(true);
    setError(null);
    try {
      await updatePeriodStatus(companyId, period.id, update);
      setNeedsVerification(false);
      setReopening(false);
      setReopenReason("");
      setApprovalDate(null);
      toast.success(success);
      await onChanged?.();
    } catch (failure) {
      if (isRecentMfaRequired(failure)) {
        setNeedsVerification(true);
      } else {
        setError(errorMessage(failure, "The period status could not be updated"));
      }
    } finally {
      setBusy(false);
    }
  }

  function finalise() {
    if (!chosenDate) {
      setError("Enter the board approval date.");
      return;
    }
    void submit({ status: "Finalised", approvalDate: chosenDate }, "Period finalised and board approval date recorded");
  }

  function reopen() {
    const reason = reopenReason.trim();
    if (reason.length < REOPEN_REASON_MIN) {
      setError(`Give a reason of at least ${REOPEN_REASON_MIN} characters for reopening the period.`);
      return;
    }
    void submit({ status: "Review", reopenReason: reason }, "Period reopened");
  }

  return (
    <div className="mb-6">
      <ReviewPanel
        title="Finalise period"
        description="Finalising records the date the board approved the accounts (Companies Act 2014, s.324); the final accounts and the s.324 approval page print it."
        actions={(
          <StatusBadge tone={locked ? "good" : "warn"}>{locked ? `Period ${period.status.toLowerCase()}` : "Not finalised"}</StatusBadge>
        )}
      >
        <div className="space-y-3">
          {locked ? (
            <>
              <p className="text-sm text-[var(--foreground)]">
                Board approval date (s.324): <span className="font-semibold">{period.approvalDate ? formatDateIE(period.approvalDate) : "not recorded"}</span>
              </p>
              {canReopen && !reopening && (
                <div className="flex justify-end">
                  <Button size="sm" variant="outline" aria-label="Reopen period" isDisabled={busy} onPress={() => { setError(null); setReopening(true); }}>
                    Reopen period
                  </Button>
                </div>
              )}
              {canReopen && reopening && (
                <div className="space-y-3 rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-4">
                  <Field label="Reason for reopening" hint={`At least ${REOPEN_REASON_MIN} characters. It is recorded in the audit trail.`}>
                    <textarea
                      value={reopenReason}
                      onChange={(event) => setReopenReason(event.target.value)}
                      rows={2}
                      className={inputClass}
                    />
                  </Field>
                  <div className="flex justify-end gap-2">
                    <Button size="sm" variant="outline" isDisabled={busy} onPress={() => { setReopening(false); setError(null); }}>
                      Cancel
                    </Button>
                    <Button size="sm" variant="primary" aria-label="Confirm reopen period" isDisabled={busy} onPress={reopen}>
                      {busy ? <Spinner size="sm" /> : "Confirm reopen"}
                    </Button>
                  </div>
                </div>
              )}
            </>
          ) : (
            <div className="grid gap-4 md:grid-cols-[minmax(0,16rem)_auto] md:items-end">
              <Field label="Board approval date (s.324)" hint={`On or after the period end (${formatDateIE(period.periodEnd)}) and not in the future.`}>
                <input
                  type="date"
                  value={chosenDate}
                  min={period.periodEnd}
                  max={today}
                  onChange={(event) => setApprovalDate(event.target.value)}
                  className={inputClass}
                />
              </Field>
              <div className="flex justify-end">
                <Button size="sm" variant="primary" aria-label="Finalise period" isDisabled={busy} onPress={finalise}>
                  {busy ? <Spinner size="sm" /> : "Finalise period"}
                </Button>
              </div>
            </div>
          )}
          {needsVerification && <RecentVerificationPrompt onVerified={() => setNeedsVerification(false)} />}
          {error && (
            <p role="alert" className="rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-900 dark:border-red-900 dark:bg-red-950/30 dark:text-red-100">
              {error}
            </p>
          )}
        </div>
      </ReviewPanel>
    </div>
  );
}
