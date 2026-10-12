"use client";

import { useState } from "react";
import { Button, Spinner } from "@heroui/react";
import { toast } from "sonner";
import { RecentVerificationPrompt } from "@/components/period/FilingEvidenceShared";
import { confirmCorporationTaxDue, type ConfirmableTaxBalance } from "@/lib/corporationTaxDue";
import { isRecentMfaRequired } from "@/lib/filingEvidence";

const inputClass =
  "w-full rounded-lg border border-[var(--control-border)] bg-white px-3 py-2 text-sm text-gray-900 outline-none transition-colors focus:border-emerald-500 focus:ring-2 focus:ring-emerald-500 dark:bg-neutral-800 dark:text-gray-100";

const REASON_MAX_LENGTH = 1000;

function formatEuro(amount: number): string {
  return new Intl.NumberFormat("en-IE", { style: "currency", currency: "EUR" }).format(amount);
}

/**
 * For a company holding a charitable tax exemption: the saved corporation-tax liability is not
 * provided for until an Owner or Reviewer confirms it is due. The action shows only while that
 * applies; the recent-verification prompt is rendered beside these controls, never inside a form.
 */
export function CorporationTaxDueConfirmation({
  companyId,
  periodId,
  holdsCharitableTaxExemption,
  balance,
  canConfirm,
  onConfirmed,
}: {
  companyId: number;
  periodId: number;
  holdsCharitableTaxExemption: boolean;
  /** The saved corporation-tax balance (not the unsaved form). */
  balance: ConfirmableTaxBalance | undefined;
  canConfirm: boolean;
  onConfirmed: (saved: ConfirmableTaxBalance) => void;
}) {
  const [reason, setReason] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [needsVerification, setNeedsVerification] = useState(false);
  const [taxableProfit, setTaxableProfit] = useState("");
  const trimmedReason = reason.trim();
  // A typed 0 is a real answer; an empty box is not a silent zero.
  const parsedTaxableProfit = taxableProfit.trim() === "" ? undefined : Number(taxableProfit);
  const validTaxableProfit = parsedTaxableProfit !== undefined && Number.isFinite(parsedTaxableProfit) && parsedTaxableProfit >= 0;
  const valid = trimmedReason.length > 0 && trimmedReason.length <= REASON_MAX_LENGTH && validTaxableProfit;

  if (!holdsCharitableTaxExemption || !balance || balance.liability <= 0) return null;

  // A confirmation counts only for the liability and attested profit it was given for.
  if (balance.confirmedDueAt
    && balance.confirmedDueLiability === balance.liability
    && balance.confirmedTaxableProfitOutsideExemption != null) {
    const reasonText = (balance.confirmedDueReason ?? "").trim();
    return (
      <p className="mt-4 rounded-md border border-gray-200 bg-gray-50 px-3 py-2 text-sm text-gray-700 dark:border-neutral-700 dark:bg-neutral-800 dark:text-gray-300">
        The {formatEuro(balance.liability)} corporation tax liability was confirmed as due by {balance.confirmedDueBy ?? "a reviewer"} on{" "}
        {new Date(balance.confirmedDueAt).toLocaleDateString("en-IE")}: {reasonText}{/[.!?]$/.test(reasonText) ? "" : "."} Saving a
        different liability clears this confirmation. Taxable profit outside the charitable exemption, as attested:{" "}
        {formatEuro(balance.confirmedTaxableProfitOutsideExemption)}.
      </p>
    );
  }

  async function confirm() {
    if (!valid) return;
    setSaving(true);
    setError(null);
    try {
      if (parsedTaxableProfit === undefined) return;
      const saved = await confirmCorporationTaxDue(companyId, periodId, trimmedReason, parsedTaxableProfit);
      setNeedsVerification(false);
      setReason("");
      setTaxableProfit("");
      toast.success("Corporation tax liability confirmed as due");
      onConfirmed(saved);
    } catch (failure) {
      if (isRecentMfaRequired(failure)) {
        setNeedsVerification(true);
      } else {
        setError(failure instanceof Error ? failure.message : "The corporation tax liability could not be confirmed");
      }
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="mt-4 space-y-3 rounded-lg border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-900 dark:border-amber-800 dark:bg-amber-900/20 dark:text-amber-200">
      <p id="corporation-tax-due-explanation">
        This company holds a charitable tax exemption, so no corporation tax is provided for automatically. The{" "}
        {formatEuro(balance.liability)} liability entered blocks the final accounts until it is confirmed as due (for example on
        income outside the exemption) or removed.
      </p>
      {canConfirm ? (
        <>
          <div>
            <label htmlFor="corporation-tax-due-reason" className="block text-sm font-medium mb-1.5">
              Why is this liability due?
            </label>
            <textarea
              id="corporation-tax-due-reason"
              className={`${inputClass} min-h-20 resize-y`}
              maxLength={REASON_MAX_LENGTH}
              placeholder="e.g. Income outside the exemption: trading outside the primary purpose"
              value={reason}
              onChange={(event) => setReason(event.target.value)}
            />
          </div>
          <div>
            <label htmlFor="corporation-tax-due-taxable-profit" className="block text-sm font-medium mb-1.5">
              Taxable profit outside the charitable exemption
            </label>
            <input
              id="corporation-tax-due-taxable-profit"
              type="number"
              min={0}
              step="0.01"
              className={inputClass}
              aria-describedby="corporation-tax-due-taxable-profit-hint"
              value={taxableProfit}
              onChange={(event) => setTaxableProfit(event.target.value)}
            />
            <p id="corporation-tax-due-taxable-profit-hint" className="mt-1 text-xs leading-5">
              Taxable profit from activities outside your charitable exemption (for example trading outside your main
              charitable purpose). The tax computation removes the income within the exemption, so this liability must be
              the corporation tax on this profit.
            </p>
          </div>
          <Button
            variant="primary"
            aria-label="Confirm this liability is due"
            aria-describedby="corporation-tax-due-explanation"
            onPress={() => void confirm()}
            isDisabled={saving || !valid}
          >
            {saving ? <Spinner size="sm" /> : "Confirm this liability is due"}
          </Button>
          {error && (
            <div role="alert" className="rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700 dark:border-red-800 dark:bg-red-900/20 dark:text-red-400">
              {error}
            </div>
          )}
          {needsVerification && <RecentVerificationPrompt onVerified={() => setNeedsVerification(false)} />}
        </>
      ) : (
        <p>An Owner or Reviewer must confirm it.</p>
      )}
    </div>
  );
}
