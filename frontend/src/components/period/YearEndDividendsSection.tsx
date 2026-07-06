"use client";

import { Button, Spinner } from "@heroui/react";
import { Plus, Trash2 } from "lucide-react";

import type { Dividend } from "@/lib/api";

interface YearEndDividendsSectionProps {
  dividends: Dividend[];
  draft: Dividend;
  saving: boolean;
  onDraftChange: (draft: Dividend) => void;
  onAdd: () => void;
  onDelete: (id: number) => void;
}

const inputClass =
  "w-full rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 px-3 py-2 text-sm text-gray-900 dark:text-gray-100 outline-none focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 transition-colors";

export function YearEndDividendsSection({
  dividends,
  draft,
  saving,
  onDraftChange,
  onAdd,
  onDelete,
}: YearEndDividendsSectionProps) {
  return (
    <>
      {dividends.length > 0 && (
        <div className="space-y-2 mb-4">
          {dividends.map((dividend) => (
            <div
              key={dividend.id}
              className="flex items-center justify-between rounded-lg border border-gray-200 dark:border-neutral-700 px-4 py-3 dark:bg-neutral-800/50"
            >
              <div>
                <p className="text-sm font-medium text-gray-900 dark:text-gray-100">
                  {formatCurrency(dividend.amount)}
                </p>
                <div className="flex items-center gap-2 mt-0.5">
                  {dividend.dateDeclared && (
                    <span className="text-xs text-gray-400 dark:text-gray-500">
                      Declared: {new Date(dividend.dateDeclared).toLocaleDateString("en-IE")}
                    </span>
                  )}
                  {dividend.datePaid && (
                    <span className="text-xs text-gray-400 dark:text-gray-500">
                      Paid: {new Date(dividend.datePaid).toLocaleDateString("en-IE")}
                    </span>
                  )}
                </div>
              </div>
              <button
                type="button"
                onClick={() => dividend.id && onDelete(dividend.id)}
                className="text-red-400 hover:text-red-600 dark:text-red-500 dark:hover:text-red-400"
                aria-label={`Delete dividend of ${formatCurrency(dividend.amount)}`}
              >
                <Trash2 className="w-4 h-4" />
              </button>
            </div>
          ))}
        </div>
      )}

      <div className="grid grid-cols-12 gap-3 items-end">
        <div className="col-span-3">
          <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Amount</label>
          <input
            type="number"
            className={inputClass}
            placeholder="0.00"
            value={draft.amount || ""}
            onChange={(event) => onDraftChange({ ...draft, amount: Number(event.target.value) })}
            aria-label="Dividend amount"
          />
        </div>
        <div className="col-span-3">
          <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Date Declared</label>
          <input
            type="date"
            className={inputClass}
            value={draft.dateDeclared}
            onChange={(event) => onDraftChange({ ...draft, dateDeclared: event.target.value })}
            aria-label="Date dividend declared"
          />
        </div>
        <div className="col-span-3">
          <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Date Paid</label>
          <input
            type="date"
            className={inputClass}
            value={draft.datePaid}
            onChange={(event) => onDraftChange({ ...draft, datePaid: event.target.value })}
            aria-label="Date dividend paid"
          />
        </div>
        <div className="col-span-3">
          <Button
            variant="primary"
            size="sm"
            onPress={onAdd}
            isDisabled={saving}
            className="w-full"
            aria-label="Add dividend"
          >
            {saving ? <Spinner size="sm" /> : <><Plus className="w-4 h-4 mr-1" /> Add</>}
          </Button>
        </div>
      </div>
    </>
  );
}

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat("en-IE", {
    style: "currency",
    currency: "EUR",
  }).format(amount);
}
