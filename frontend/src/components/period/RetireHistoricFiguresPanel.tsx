"use client";

import { useState } from "react";
import { Button, Spinner } from "@heroui/react";
import { toast } from "sonner";
import { RecentVerificationPrompt } from "@/components/period/FilingEvidenceShared";
import { isRecentMfaRequired } from "@/lib/filingEvidence";
import { retireHistoricSizeFigures } from "@/lib/historicSizeFigures";

const inputClass =
  "w-full rounded-lg border border-[var(--control-border)] bg-white px-3 py-2 text-sm text-gray-900 outline-none transition-colors focus:border-emerald-500 focus:ring-2 focus:ring-emerald-500 dark:bg-neutral-800 dark:text-gray-100";

const REASON_MAX_LENGTH = 1000;

/**
 * Owner only: retires this period's historic figures, with a required reason. The consequences are
 * stated before the button, and the recent-verification prompt is rendered beside these controls,
 * never inside a form.
 */
export function RetireHistoricFiguresPanel({
  companyId,
  periodId,
  onRetired,
}: {
  companyId: number;
  periodId: number;
  onRetired: () => Promise<void>;
}) {
  const [reason, setReason] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [needsVerification, setNeedsVerification] = useState(false);
  const trimmedReason = reason.trim();
  const valid = trimmedReason.length > 0 && trimmedReason.length <= REASON_MAX_LENGTH;

  async function retire() {
    if (!valid) return;
    setSaving(true);
    setError(null);
    try {
      await retireHistoricSizeFigures(companyId, periodId, trimmedReason);
      setNeedsVerification(false);
      toast.success("Historic figures retired; they are kept as evidence");
      await onRetired();
    } catch (failure) {
      if (isRecentMfaRequired(failure)) {
        setNeedsVerification(true);
      } else {
        setError(failure instanceof Error ? failure.message : "The historic figures could not be retired");
      }
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="mt-2 space-y-3 rounded-lg border border-red-200 bg-red-50/40 p-4 dark:border-red-900 dark:bg-red-900/10">
      <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-100">Retire historic figures</h3>
      <p id="retire-historic-figures-consequences" className="text-sm leading-6 text-gray-700 dark:text-gray-300">
        Retiring keeps these figures and the PDF as evidence, but they are no longer used as the previous year&apos;s
        figures in the next period&apos;s size test. The following periods&apos; classifications become out of date and must
        be re-run, so figures that a finalised, filed or locked later period relies on cannot be retired. This
        period can then be classified normally from its own figures, or historic figures recorded again if its earlier
        history is still missing.
      </p>
      <div>
        <label htmlFor="retire-historic-figures-reason" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5">
          Why are these figures being retired?
        </label>
        <textarea
          id="retire-historic-figures-reason"
          className={`${inputClass} min-h-20 resize-y`}
          maxLength={REASON_MAX_LENGTH}
          value={reason}
          onChange={(event) => setReason(event.target.value)}
        />
      </div>
      <Button
        variant="danger"
        aria-label="Retire historic figures"
        aria-describedby="retire-historic-figures-consequences"
        onPress={() => void retire()}
        isDisabled={saving || !valid}
      >
        {saving ? <Spinner size="sm" /> : "Retire historic figures"}
      </Button>
      {error && (
        <div role="alert" className="rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700 dark:border-red-800 dark:bg-red-900/20 dark:text-red-400">
          {error}
        </div>
      )}
      {needsVerification && <RecentVerificationPrompt onVerified={() => setNeedsVerification(false)} />}
    </div>
  );
}
