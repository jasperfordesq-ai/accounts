"use client";

import { Button, Spinner } from "@heroui/react";

import type { PayrollSummary } from "@/lib/api";

interface YearEndPayrollSectionProps {
  canWrite?: boolean;
  form: PayrollSummary;
  saving: boolean;
  onFormChange: (form: PayrollSummary) => void;
  onSave: () => void;
}

const inputClass =
  "w-full rounded-lg border border-[var(--control-border)] bg-white px-3 py-2 text-sm text-gray-900 outline-none transition-colors focus:border-emerald-500 focus:ring-2 focus:ring-emerald-500 dark:bg-neutral-800 dark:text-gray-100";

export function YearEndPayrollSection({
  canWrite = true,
  form,
  saving,
  onFormChange,
  onSave,
}: YearEndPayrollSectionProps) {
  return (
    <>
      <p className="mb-4 text-xs text-[var(--muted-foreground)]">
        Record payroll and staff costs for the statutory accounts, notes, and corporation tax working papers.
      </p>
      {canWrite ? <><div className="grid grid-cols-2 gap-4">
        <div>
          <label htmlFor="payroll-staff-count" className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Number of Staff</label>
          <input
            id="payroll-staff-count"
            type="number"
            className={inputClass}
            placeholder="0"
            value={form.staffCount || ""}
            onChange={(event) => onFormChange({ ...form, staffCount: Number(event.target.value) })}
            aria-label="Number of staff"
          />
        </div>
        <div>
          <label htmlFor="payroll-gross-wages" className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Employee gross wages (excluding director fees)</label>
          <input
            id="payroll-gross-wages"
            type="number"
            className={inputClass}
            placeholder="0.00"
            value={form.grossWages || ""}
            onChange={(event) => onFormChange({ ...form, grossWages: Number(event.target.value) })}
            aria-label="Employee gross wages excluding director fees"
          />
        </div>
        <div>
          <label htmlFor="payroll-directors-fees" className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Directors&apos; salaries and fees</label>
          <input
            id="payroll-directors-fees"
            type="number"
            className={inputClass}
            placeholder="0.00"
            value={form.directorsFees || ""}
            onChange={(event) => onFormChange({ ...form, directorsFees: Number(event.target.value) })}
            aria-label="Directors salaries and fees"
          />
        </div>
        <div>
          <label htmlFor="payroll-employer-prsi" className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Employer PRSI</label>
          <input
            id="payroll-employer-prsi"
            type="number"
            className={inputClass}
            placeholder="0.00"
            value={form.employerPrsi || ""}
            onChange={(event) => onFormChange({ ...form, employerPrsi: Number(event.target.value) })}
            aria-label="Employer PRSI"
          />
        </div>
        <div>
          <label htmlFor="payroll-pension-contributions" className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Pension Contributions</label>
          <input
            id="payroll-pension-contributions"
            type="number"
            className={inputClass}
            placeholder="0.00"
            value={form.pensionContributions || ""}
            onChange={(event) => onFormChange({ ...form, pensionContributions: Number(event.target.value) })}
            aria-label="Pension contributions"
          />
        </div>
      </div>
      <div className="mt-4 flex justify-end">
        <Button
          variant="primary"
          size="sm"
          onPress={onSave}
          isDisabled={saving}
          aria-label="Save payroll"
        >
          {saving ? <Spinner size="sm" /> : "Save Payroll"}
        </Button>
      </div></> : (
        <dl className="grid grid-cols-2 gap-3 text-sm text-gray-700 dark:text-gray-300">
          <div><dt className="text-xs text-[var(--muted-foreground)]">Staff</dt><dd>{form.staffCount}</dd></div>
          <div><dt className="text-xs text-[var(--muted-foreground)]">Employee gross wages</dt><dd>{form.grossWages}</dd></div>
          <div><dt className="text-xs text-[var(--muted-foreground)]">Directors&apos; salaries and fees</dt><dd>{form.directorsFees}</dd></div>
          <div><dt className="text-xs text-[var(--muted-foreground)]">Employer PRSI</dt><dd>{form.employerPrsi}</dd></div>
          <div><dt className="text-xs text-[var(--muted-foreground)]">Pension contributions</dt><dd>{form.pensionContributions}</dd></div>
        </dl>
      )}
    </>
  );
}
