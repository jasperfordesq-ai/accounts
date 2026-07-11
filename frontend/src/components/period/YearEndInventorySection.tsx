"use client";

import { Button, Chip, Spinner } from "@heroui/react";
import { Plus, Trash2 } from "lucide-react";

import type { InventoryItem } from "@/lib/api";
import { useDestructiveActionConfirmation } from "@/lib/useDestructiveAction";

interface YearEndInventorySectionProps {
  canWrite?: boolean;
  items: InventoryItem[];
  draft: InventoryItem;
  saving: boolean;
  onDraftChange: (draft: InventoryItem) => void;
  onAdd: () => void;
  onDelete: (id: number) => void | Promise<void>;
}

const inputClass =
  "w-full rounded-lg border border-[var(--control-border)] bg-white px-3 py-2 text-sm text-gray-900 outline-none transition-colors focus:border-emerald-500 focus:ring-2 focus:ring-emerald-500 dark:bg-neutral-800 dark:text-gray-100";
const selectClass =
  "w-full rounded-lg border border-[var(--control-border)] bg-white px-3 py-2 text-sm text-gray-900 outline-none transition-colors focus:border-emerald-500 focus:ring-2 focus:ring-emerald-500 dark:bg-neutral-800 dark:text-gray-100";

export function YearEndInventorySection({
  canWrite = true,
  items,
  draft,
  saving,
  onDraftChange,
  onAdd,
  onDelete,
}: YearEndInventorySectionProps) {
  const { requestDestructiveAction, destructiveActionConfirmation } = useDestructiveActionConfirmation();

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
                {canWrite && <button
                  type="button"
                  onClick={() => item.id && requestDestructiveAction({
                    recordLabel: `inventory item ${item.description}`,
                    consequence: `This permanently removes the ${formatCurrency(item.value)} inventory valuation and ${item.valuationMethod} evidence from the year-end record. The removal cannot be undone.`,
                    onConfirm: () => onDelete(item.id!),
                    successAnnouncement: `Inventory item ${item.description} was removed.`,
                  })}
                  className="text-red-600 hover:text-red-700 dark:text-red-400 dark:hover:text-red-300"
                  aria-label={`Delete inventory item ${item.description}`}
                >
                  <Trash2 className="w-4 h-4" />
                </button>}
              </div>
            </div>
          ))}
        </div>
      )}

      {canWrite && <div className="mobile-form-grid grid grid-cols-12 gap-3 items-end">
        <div className="col-span-4">
          <label htmlFor="inventory-description" className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Description</label>
          <input
            id="inventory-description"
            type="text"
            className={inputClass}
            placeholder="e.g. Finished goods or work in progress"
            value={draft.description}
            onChange={(event) => onDraftChange({ ...draft, description: event.target.value })}
            aria-label="Inventory item description"
          />
        </div>
        <div className="col-span-3">
          <label htmlFor="inventory-value" className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Value</label>
          <input
            id="inventory-value"
            type="number"
            className={inputClass}
            placeholder="0.00"
            value={draft.value || ""}
            onChange={(event) => onDraftChange({ ...draft, value: Number(event.target.value) })}
            aria-label="Inventory item value"
          />
        </div>
        <div className="col-span-3">
          <label htmlFor="inventory-valuation-method" className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Valuation Method</label>
          <select
            id="inventory-valuation-method"
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
      </div>}
      {destructiveActionConfirmation}
    </>
  );
}

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat("en-IE", {
    style: "currency",
    currency: "EUR",
  }).format(amount);
}
