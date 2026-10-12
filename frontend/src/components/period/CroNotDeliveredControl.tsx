"use client";

import { useState } from "react";
import { Button, Spinner } from "@heroui/react";
import { toast } from "sonner";
import { RecentVerificationPrompt } from "@/components/period/FilingEvidenceShared";
import {
  CRO_NOT_DELIVERED_REASON_MAX,
  CRO_NOT_DELIVERED_REASON_MIN,
  treatCroAsNotDelivered,
} from "@/lib/croCorrection";
import { isRecentMfaRequired } from "@/lib/filingEvidence";

const inputClass =
  "w-full rounded-lg border border-[var(--control-border)] bg-white px-3 py-2 text-sm text-gray-900 outline-none transition-colors focus:border-emerald-500 focus:ring-2 focus:ring-emerald-500 dark:bg-neutral-800 dark:text-gray-100";

/**
 * Owner/Reviewer only: closes a CRO correction that the CRO treats as not delivered. The controls are
 * not a form, and the recent-verification prompt is rendered beside them, never inside.
 */
export function CroNotDeliveredControl({
  companyId,
  periodId,
  onClosed,
}: {
  companyId: number;
  periodId: number;
  onClosed: () => void | Promise<void>;
}) {
  const [open, setOpen] = useState(false);
  const [reason, setReason] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [needsVerification, setNeedsVerification] = useState(false);
  const trimmedReason = reason.trim();
  const valid = trimmedReason.length >= CRO_NOT_DELIVERED_REASON_MIN && trimmedReason.length <= CRO_NOT_DELIVERED_REASON_MAX;

  async function confirm() {
    if (!valid) {
      setError(`Explain why the filing is being treated as not delivered (${CRO_NOT_DELIVERED_REASON_MIN} to 1,000 characters).`);
      return;
    }
    setSaving(true);
    setError(null);
    try {
      await treatCroAsNotDelivered(companyId, periodId, trimmedReason);
      setNeedsVerification(false);
      setOpen(false);
      setReason("");
      toast.warning("CRO filing treated as not delivered");
      await onClosed();
    } catch (failure) {
      if (isRecentMfaRequired(failure)) {
        setNeedsVerification(true);
      } else {
        setError(failure instanceof Error ? failure.message : "The CRO filing could not be treated as not delivered");
      }
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="flex min-w-0 flex-col gap-2" data-cro-not-delivered>
      {!open ? (
        <Button variant="outline" size="sm" onPress={() => setOpen(true)}>
          Treat as not delivered
        </Button>
      ) : (
        <div
          role="group"
          aria-labelledby="cro-not-delivered-title"
          className="max-w-xl space-y-3 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-900 dark:border-red-800 dark:bg-red-900/20 dark:text-red-200"
        >
          <p id="cro-not-delivered-title" className="font-semibold">Treat this CRO filing as not delivered</p>
          <p id="cro-not-delivered-explanation">
            Use this when the CRO treats the filing as not delivered, usually because the correction was not made within
            the 14-day deadline. The filing must then be made afresh: the CORE reference is cleared, the pack must be
            approved and submitted again, and late-filing penalties may apply. The original reference, deadline and
            reason stay in the audit trail.
          </p>
          <div>
            <label htmlFor="cro-not-delivered-reason" className="mb-1.5 block text-sm font-medium">
              Why is the filing being treated as not delivered?
            </label>
            <textarea
              id="cro-not-delivered-reason"
              className={`${inputClass} min-h-20 resize-y`}
              maxLength={CRO_NOT_DELIVERED_REASON_MAX}
              value={reason}
              onChange={(event) => setReason(event.target.value)}
            />
          </div>
          <div className="flex flex-wrap gap-2">
            <Button
              variant="danger"
              size="sm"
              aria-label="Confirm: treat as not delivered"
              aria-describedby="cro-not-delivered-explanation"
              isDisabled={saving}
              onPress={() => void confirm()}
            >
              {saving ? <Spinner size="sm" /> : "Confirm: treat as not delivered"}
            </Button>
            <Button variant="outline" size="sm" isDisabled={saving} onPress={() => { setOpen(false); setError(null); }}>
              Cancel
            </Button>
          </div>
          {error && (
            <div role="alert" className="rounded-lg border border-red-300 bg-white px-3 py-2 text-sm text-red-700 dark:bg-neutral-900 dark:text-red-300">
              {error}
            </div>
          )}
        </div>
      )}
      {needsVerification && <RecentVerificationPrompt onVerified={() => setNeedsVerification(false)} />}
    </div>
  );
}
