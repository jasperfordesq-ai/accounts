"use client";

import { Button, Spinner } from "@heroui/react";

import type { PayrollSummary } from "@/lib/api";

interface YearEndPayrollSectionProps {
  form: PayrollSummary;
  saving: boolean;
  onFormChange: (form: PayrollSummary) => void;
  onSave: () => void;
}

const inputClass =
  "w-full rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 px-3 py-2 text-sm text-gray-900 dark:text-gray-100 outline-none focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 transition-colors";

export function YearEndPayrollSection({
  form,
  saving,
  onFormChange,
  onSave,
}: YearEndPayrollSectionProps) {
  return (
    <>
      <p className="mb-4 text-xs text-gray-500 dark:text-gray-400">
        Record payroll and staff costs for the statutory accounts, notes, and corporation tax working papers.
      </p>
      <div className="grid grid-cols-2 gap-4">
        <div>
          <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Number of Staff</label>
          <input
            type="number"
            className={inputClass}
            placeholder="0"
            value={form.staffCount || ""}
            onChange={(event) => onFormChange({ ...form, staffCount: Number(event.target.value) })}
            aria-label="Number of staff"
          />
        </div>
        <div>
          <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Gross Wages</label>
          <input
            type="number"
            className={inputClass}
            placeholder="0.00"
            value={form.grossWages || ""}
            onChange={(event) => onFormChange({ ...form, grossWages: Number(event.target.value) })}
            aria-label="Gross wages"
          />
        </div>
        <div>
          <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Employer PRSI</label>
          <input
            type="number"
            className={inputClass}
            placeholder="0.00"
            value={form.employerPrsi || ""}
            onChange={(event) => onFormChange({ ...form, employerPrsi: Number(event.target.value) })}
            aria-label="Employer PRSI"
          />
        </div>
        <div>
          <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Pension Contributions</label>
          <input
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
      </div>
    </>
  );
}
