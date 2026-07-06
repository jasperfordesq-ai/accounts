"use client";

import { Button, Chip, Spinner } from "@heroui/react";
import { Plus, Trash2 } from "lucide-react";

import type { InventoryItem } from "@/lib/api";

interface YearEndInventorySectionProps {
  items: InventoryItem[];
  draft: InventoryItem;
  saving: boolean;
  onDraftChange: (draft: InventoryItem) => void;
  onAdd: () => void;
  onDelete: (id: number) => void;
}

const inputClass =
  "w-full rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 px-3 py-2 text-sm text-gray-900 dark:text-gray-100 outline-none focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 transition-colors";
const selectClass =
  "w-full rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 px-3 py-2 text-sm text-gray-900 dark:text-gray-100 outline-none focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 transition-colors";

export function YearEndInventorySection({
  items,
  draft,
  saving,
  onDraftChange,
  onAdd,
  onDelete,
}: YearEndInventorySectionProps) {
  return (
    <>
      {items.length > 0 && (
        <div className="space-y-2 mb-4">
          {items.map((item) => (
            <div
              key={item.id}
              className="flex items-center justify-between rounded-lg border border-gray-200 dark:border-neutral-700 px-4 py-3 dark:bg-neutral-800/50"
            >
              <div>
                <p className="text-sm font-medium text-gray-900 dark:text-gray-100">{item.description}</p>
                <Chip variant="soft" size="sm" color="default">{item.valuationMethod}</Chip>
              </div>
              <div className="flex items-center gap-3">
                <span className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                  {formatCurrency(item.value)}
                </span>
                <button
                  type="button"
                  onClick={() => item.id && onDelete(item.id)}
                  className="text-red-400 hover:text-red-600 dark:text-red-500 dark:hover:text-red-400"
                  aria-label={`Delete inventory item ${item.description}`}
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
            placeholder="e.g. Finished goods or work in progress"
            value={draft.description}
            onChange={(event) => onDraftChange({ ...draft, description: event.target.value })}
            aria-label="Inventory item description"
          />
        </div>
        <div className="col-span-3">
          <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Value</label>
          <input
            type="number"
            className={inputClass}
            placeholder="0.00"
            value={draft.value || ""}
            onChange={(event) => onDraftChange({ ...draft, value: Number(event.target.value) })}
            aria-label="Inventory item value"
          />
        </div>
        <div className="col-span-3">
          <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Valuation Method</label>
          <select
            className={selectClass}
            value={draft.valuationMethod}
            onChange={(event) => onDraftChange({ ...draft, valuationMethod: event.target.value })}
            title="Valuation method"
            aria-label="Valuation method"
          >
            <option value="FIFO">FIFO</option>
            <option value="WeightedAverage">Weighted Average</option>
            <option value="LIFO">LIFO</option>
          </select>
        </div>
        <div className="col-span-2">
          <Button
            variant="primary"
            size="sm"
            onPress={onAdd}
            isDisabled={saving}
            className="w-full"
            aria-label="Add inventory item"
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
