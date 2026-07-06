"use client";

import { Button, Chip, Spinner } from "@heroui/react";
import { Plus, Trash2 } from "lucide-react";

import type { ContingentLiability } from "@/lib/api";

interface YearEndContingentLiabilitiesSectionProps {
  contingencies: ContingentLiability[];
  draft: ContingentLiability;
  saving: boolean;
  onDraftChange: (draft: ContingentLiability) => void;
  onAdd: () => void;
  onDelete: (id: number) => void;
}

const inputClass =
  "w-full rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 px-3 py-2 text-sm text-gray-900 dark:text-gray-100 outline-none focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 transition-colors";
const selectClass =
  "w-full rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 px-3 py-2 text-sm text-gray-900 dark:text-gray-100 outline-none focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 transition-colors";

export function YearEndContingentLiabilitiesSection({
  contingencies,
  draft,
  saving,
  onDraftChange,
  onAdd,
  onDelete,
}: YearEndContingentLiabilitiesSectionProps) {
  return (
    <>
      {contingencies.length > 0 && (
        <div className="space-y-2 mb-4">
          {contingencies.map((contingency) => (
            <div
              key={contingency.id}
              className="flex items-center justify-between rounded-lg border border-gray-200 dark:border-neutral-700 px-4 py-3 dark:bg-neutral-800/50"
            >
              <div>
                <p className="text-sm font-medium text-gray-900 dark:text-gray-100">{contingency.description}</p>
                <div className="flex items-center gap-2 mt-0.5">
                  <Chip variant="soft" size="sm" color="default">{contingency.nature}</Chip>
                  <Chip
                    variant="soft"
                    size="sm"
                    color={contingency.likelihood === "Probable" ? "danger" : contingency.likelihood === "Possible" ? "warning" : "success"}
                  >
                    {contingency.likelihood}
                  </Chip>
                </div>
              </div>
              <div className="flex items-center gap-3">
                {contingency.estimatedAmount != null && contingency.estimatedAmount !== 0 && (
                  <span className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                    {formatCurrency(contingency.estimatedAmount)}
                  </span>
                )}
                <button
                  type="button"
                  onClick={() => contingency.id && onDelete(contingency.id)}
                  className="text-red-400 hover:text-red-600 dark:text-red-500 dark:hover:text-red-400"
                  aria-label={`Delete contingency ${contingency.description}`}
                >
                  <Trash2 className="w-4 h-4" />
                </button>
              </div>
            </div>
          ))}
        </div>
      )}

      <div className="grid grid-cols-12 gap-3 items-end">
        <div className="col-span-4">
          <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Description</label>
          <input
            type="text"
            className={inputClass}
            placeholder="e.g. Pending legal claim"
            value={draft.description}
            onChange={(event) => onDraftChange({ ...draft, description: event.target.value })}
            aria-label="Contingency description"
          />
        </div>
        <div className="col-span-2">
          <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Nature</label>
          <select
            className={selectClass}
            value={draft.nature}
            onChange={(event) => onDraftChange({ ...draft, nature: event.target.value })}
            title="Nature"
            aria-label="Contingency nature"
          >
            <option value="Guarantee">Guarantee</option>
            <option value="Legal Claim">Legal Claim</option>
            <option value="Warranty">Warranty</option>
            <option value="Environmental">Environmental</option>
            <option value="Other">Other</option>
          </select>
        </div>
        <div className="col-span-2">
          <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Est. Amount</label>
          <input
            type="number"
            className={inputClass}
            placeholder="0.00"
            value={draft.estimatedAmount || ""}
            onChange={(event) => onDraftChange({ ...draft, estimatedAmount: Number(event.target.value) })}
            aria-label="Estimated amount"
          />
        </div>
        <div className="col-span-2">
          <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Likelihood</label>
          <select
            className={selectClass}
            value={draft.likelihood}
            onChange={(event) => onDraftChange({ ...draft, likelihood: event.target.value })}
            title="Likelihood"
            aria-label="Contingency likelihood"
          >
            <option value="Probable">Probable</option>
            <option value="Possible">Possible</option>
            <option value="Remote">Remote</option>
          </select>
        </div>
        <div className="col-span-2">
          <Button
            variant="primary"
            size="sm"
            onPress={onAdd}
            isDisabled={saving}
            className="w-full"
            aria-label="Add contingent liability"
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
