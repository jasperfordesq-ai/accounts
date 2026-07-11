"use client";

import { Button, Spinner } from "@heroui/react";

import type {
  CorporationTaxScopeReviewInput,
  CorporationTaxScopeReviewResponse,
} from "@/lib/api";

interface CorporationTaxScopeReviewSectionProps {
  canWrite?: boolean;
  form: CorporationTaxScopeReviewInput;
  result: CorporationTaxScopeReviewResponse | null;
  saving: boolean;
  onFormChange: (form: CorporationTaxScopeReviewInput) => void;
  onSave: () => void;
}

const inputClass = "w-full rounded-lg border border-[var(--control-border)] bg-white px-3 py-2 text-sm text-gray-900 outline-none focus:border-emerald-500 focus:ring-2 focus:ring-emerald-500 dark:bg-neutral-800 dark:text-gray-100";

export function CorporationTaxScopeReviewSection({
  canWrite = true,
  form,
  result,
  saving,
  onFormChange,
  onSave,
}: CorporationTaxScopeReviewSectionProps) {
  const computation = result?.computation;
  return (
    <div className="mt-6 border-t border-gray-200 pt-5 dark:border-neutral-700">
      <div className="rounded-lg border border-amber-300 bg-amber-50 px-4 py-3 text-sm text-amber-950 dark:border-amber-700 dark:bg-amber-950/30 dark:text-amber-100">
        <p className="font-semibold">Corporation-tax scope declaration</p>
        <p className="mt-1">
          This controls bounded support data only. It is not a CT1 return, does not replace qualified review,
          and never submits anything to Revenue.
        </p>
      </div>

      {computation && (
        <div className={`mt-3 rounded-lg border px-4 py-3 text-sm ${computation.finalTaxChargeSupported
          ? "border-emerald-300 bg-emerald-50 text-emerald-950 dark:border-emerald-700 dark:bg-emerald-950/30 dark:text-emerald-100"
          : "border-red-300 bg-red-50 text-red-950 dark:border-red-800 dark:bg-red-950/30 dark:text-red-100"}`}>
          <p className="font-semibold">{computation.finalTaxChargeSupported ? "Simple machine scope supported" : "Final tax charge blocked"}</p>
          {computation.blockingReasons.length > 0 && (
            <ul className="mt-2 list-disc space-y-1 pl-5">
              {computation.blockingReasons.map((reason) => <li key={reason}>{reason}</li>)}
            </ul>
          )}
        </div>
      )}
      {result?.computationFailure && (
        <div role="alert" className="mt-3 rounded-lg border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-950 dark:border-red-800 dark:bg-red-950/30 dark:text-red-100">
          <p className="font-semibold">Tax support calculation unavailable</p>
          <p className="mt-1">{result.computationFailure}</p>
        </div>
      )}

      {canWrite ? (
        <div className="mt-4 space-y-4">
          <div className="grid gap-4 md:grid-cols-2">
            <YesNoUnknown
              label="Is the company a close company?"
              value={form.isCloseCompany}
              onChange={(value) => onFormChange({ ...form, isCloseCompany: value, isServiceCompany: value ? form.isServiceCompany : null })}
            />
            <YesNoUnknown
              label="If close, is it a service company?"
              value={form.isServiceCompany}
              disabled={form.isCloseCompany !== true}
              onChange={(value) => onFormChange({ ...form, isServiceCompany: value })}
            />
          </div>

          <div className="grid gap-2 md:grid-cols-2">
            <Flag label="Group or consortium relief/claim" checked={form.hasGroupOrConsortiumRelief} onChange={(value) => onFormChange({ ...form, hasGroupOrConsortiumRelief: value })} />
            <Flag label="Chargeable gains or development land" checked={form.hasChargeableGains} onChange={(value) => onFormChange({ ...form, hasChargeableGains: value })} />
            <Flag label="Foreign income or double-tax credits" checked={form.hasForeignIncomeOrTaxCredits} onChange={(value) => onFormChange({ ...form, hasForeignIncomeOrTaxCredits: value })} />
            <Flag label="Excepted trade / special 25% trade" checked={form.hasExceptedTrade} onChange={(value) => onFormChange({ ...form, hasExceptedTrade: value })} />
            <Flag label="Other reliefs, credits or special regimes" checked={form.hasOtherReliefsOrSpecialRegimes} onChange={(value) => onFormChange({ ...form, hasOtherReliefsOrSpecialRegimes: value })} />
            <Flag label="Passive/non-trading income present" checked={form.declaredPassiveIncomePresent} onChange={(value) => onFormChange({ ...form, declaredPassiveIncomePresent: value })} />
            <Flag label="Passive income classification reviewed" checked={form.passiveIncomeClassificationReviewed} onChange={(value) => onFormChange({ ...form, passiveIncomeClassificationReviewed: value })} />
          </div>

          <div className="grid gap-4 md:grid-cols-2">
            <label className="text-sm font-medium text-gray-700 dark:text-gray-300">
              Loss treatment / election
              <select
                className={`${inputClass} mt-1`}
                value={form.lossTreatment}
                onChange={(event) => onFormChange({ ...form, lossTreatment: event.target.value as CorporationTaxScopeReviewInput["lossTreatment"] })}
              >
                <option value="Unreviewed">Unreviewed - blocks final tax charge</option>
                <option value="NotApplicable">Not applicable</option>
                <option value="CarryForwardSameTrade">Same-trade carry-forward (supported)</option>
                <option value="CurrentPeriodOrCarryBackClaim">Current-period / carry-back claim (manual)</option>
                <option value="GroupRelief">Group relief (manual)</option>
                <option value="TerminalLossRelief">Terminal loss relief (manual)</option>
                <option value="Other">Other election (manual)</option>
              </select>
            </label>
            <label className="text-sm font-medium text-gray-700 dark:text-gray-300">
              Brought-forward trading loss
              <input
                className={`${inputClass} mt-1`}
                type="number"
                min="0"
                step="0.01"
                value={form.broughtForwardTradingLoss || ""}
                onChange={(event) => onFormChange({ ...form, broughtForwardTradingLoss: Number(event.target.value) })}
              />
            </label>
          </div>

          <label className="block text-sm font-medium text-gray-700 dark:text-gray-300">
            Brought-forward loss evidence (required when non-zero)
            <input
              className={`${inputClass} mt-1`}
              value={form.broughtForwardLossEvidence ?? ""}
              onChange={(event) => onFormChange({ ...form, broughtForwardLossEvidence: event.target.value })}
              placeholder="Prior signed computation / retained ledger reference"
            />
          </label>
          <label className="block text-sm font-medium text-gray-700 dark:text-gray-300">
            Scope evidence note
            <textarea
              className={`${inputClass} mt-1 min-h-24`}
              value={form.evidenceNote}
              onChange={(event) => onFormChange({ ...form, evidenceNote: event.target.value })}
              placeholder="Explain the source documents and scope checks completed (minimum 20 characters)."
            />
          </label>
          <div className="flex justify-end">
            <Button
              variant="primary"
              size="sm"
              onPress={onSave}
              isDisabled={saving || form.evidenceNote.trim().length < 20}
              aria-label="Save tax scope and loss ledger"
            >
              {saving ? <Spinner size="sm" /> : "Save tax scope and loss ledger"}
            </Button>
          </div>
        </div>
      ) : result?.review ? (
        <p className="mt-3 text-sm text-gray-700 dark:text-gray-300">
          Prepared by {result.review.preparedBy} at {new Date(result.review.preparedAtUtc).toLocaleString("en-IE")}.
        </p>
      ) : (
        <p className="mt-3 text-sm text-red-700 dark:text-red-300">No corporation-tax scope declaration is retained.</p>
      )}
    </div>
  );
}

function Flag({ label, checked, onChange }: { label: string; checked: boolean; onChange: (value: boolean) => void }) {
  return (
    <label className="flex items-start gap-2 rounded-md border border-gray-200 px-3 py-2 text-sm text-gray-700 dark:border-neutral-700 dark:text-gray-300">
      <input className="workbench-checkbox mt-0.5" type="checkbox" checked={checked} onChange={(event) => onChange(event.target.checked)} />
      <span>{label}</span>
    </label>
  );
}

function YesNoUnknown({
  label,
  value,
  disabled = false,
  onChange,
}: {
  label: string;
  value: boolean | null;
  disabled?: boolean;
  onChange: (value: boolean | null) => void;
}) {
  return (
    <label className="text-sm font-medium text-gray-700 dark:text-gray-300">
      {label}
      <select
        className={`${inputClass} mt-1`}
        disabled={disabled}
        value={value === null ? "unknown" : value ? "yes" : "no"}
        onChange={(event) => onChange(event.target.value === "unknown" ? null : event.target.value === "yes")}
      >
        <option value="unknown">Not answered</option>
        <option value="no">No</option>
        <option value="yes">Yes</option>
      </select>
    </label>
  );
}
