"use client";

import { Button, Spinner } from "@heroui/react";

import type { TaxBalance } from "@/lib/api";

interface YearEndTaxBalancesSectionProps {
  forms: Record<string, TaxBalance>;
  savingKey: string | null;
  onFormChange: (taxType: string, balance: TaxBalance) => void;
  onSave: (taxType: string) => void;
}

const TAX_ROWS = [
  { key: "CorporationTax", label: "Corporation Tax" },
  { key: "VAT", label: "VAT" },
  { key: "PAYE_PRSI", label: "PAYE / PRSI" },
] as const;

const inputClass =
  "w-full rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 px-3 py-2 text-sm text-gray-900 dark:text-gray-100 outline-none focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 transition-colors";

export function YearEndTaxBalancesSection({
  forms,
  savingKey,
  onFormChange,
  onSave,
}: YearEndTaxBalancesSectionProps) {
  return (
    <div className="space-y-6">
      <p className="text-xs text-gray-500 dark:text-gray-400">
        Capture tax creditor/debtor balances for corporation tax, VAT, and payroll taxes at year-end.
      </p>
      {TAX_ROWS.map(({ key, label }) => {
        const form = forms[key] ?? { taxType: key, liability: 0, paid: 0, balance: 0 };
        const isSaving = savingKey === `tax-${key}`;

        return (
          <div key={key}>
            <h4 className="text-sm font-medium text-gray-800 dark:text-gray-200 mb-3">{label}</h4>
            <div className="grid grid-cols-12 gap-3 items-end">
              <div className="col-span-3">
                <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Liability</label>
                <input
                  type="number"
                  className={inputClass}
                  placeholder="0.00"
                  value={form.liability || ""}
                  onChange={(event) =>
                    onFormChange(key, { ...form, taxType: key, liability: Number(event.target.value) })
                  }
                  aria-label={`${label} liability`}
                />
              </div>
              <div className="col-span-3">
                <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Paid</label>
                <input
                  type="number"
                  className={inputClass}
                  placeholder="0.00"
                  value={form.paid || ""}
                  onChange={(event) =>
                    onFormChange(key, { ...form, taxType: key, paid: Number(event.target.value) })
                  }
                  aria-label={`${label} paid`}
                />
              </div>
              <div className="col-span-3">
                <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Balance</label>
                <input
                  type="number"
                  className={inputClass}
                  placeholder="0.00"
                  value={form.balance || ""}
                  onChange={(event) =>
                    onFormChange(key, { ...form, taxType: key, balance: Number(event.target.value) })
                  }
                  aria-label={`${label} balance`}
                />
              </div>
              <div className="col-span-3">
                <Button
                  variant="outline"
                  size="sm"
                  onPress={() => onSave(key)}
                  isDisabled={isSaving}
                  className="w-full"
                  aria-label={`Save ${label} balance`}
                >
                  {isSaving ? <Spinner size="sm" /> : "Save"}
                </Button>
              </div>
            </div>
          </div>
        );
      })}
    </div>
  );
}
