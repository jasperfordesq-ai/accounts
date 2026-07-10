"use client";

import { Button, Chip, Spinner } from "@heroui/react";
import { Plus, Trash2 } from "lucide-react";

import type { Creditor, Debtor } from "@/lib/api";
import { useDestructiveActionConfirmation } from "@/lib/useDestructiveAction";

type MoneyListItem = Debtor | Creditor;

interface YearEndMoneyListSectionProps<T extends MoneyListItem> {
  canWrite?: boolean;
  mode: "debtors" | "creditors";
  items: T[];
  draft: T;
  typeOptions: string[];
  namePlaceholder: string;
  saving: boolean;
  onDraftChange: (draft: T) => void;
  onAdd: () => void;
  onDelete: (id: number) => void | Promise<void>;
}

const inputClass =
  "w-full rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 px-3 py-2 text-sm text-gray-900 dark:text-gray-100 outline-none focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 transition-colors";
const selectClass =
  "w-full rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 px-3 py-2 text-sm text-gray-900 dark:text-gray-100 outline-none focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 transition-colors";

export function YearEndMoneyListSection<T extends MoneyListItem>({
  canWrite = true,
  mode,
  items,
  draft,
  typeOptions,
  namePlaceholder,
  saving,
  onDraftChange,
  onAdd,
  onDelete,
}: YearEndMoneyListSectionProps<T>) {
  const noun = mode === "debtors" ? "Debtor" : "Creditor";
  const lowerNoun = noun.toLowerCase();
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
                <p className="text-sm font-medium text-gray-900 dark:text-gray-100">{item.name}</p>
                <div className="flex items-center gap-2 mt-0.5">
                  <Chip variant="soft" size="sm" color="default">{item.type}</Chip>
                  {mode === "creditors" && "dueWithinYear" in item && (
                    <Chip variant="soft" size="sm" color={item.dueWithinYear ? "warning" : "default"}>
                      {item.dueWithinYear ? "Due < 1 year" : "Due > 1 year"}
                    </Chip>
                  )}
                  {mode === "debtors" && item.notes && (
                    <span className="text-xs text-gray-400 dark:text-gray-500">{item.notes}</span>
                  )}
                </div>
              </div>
              <div className="flex items-center gap-3">
                <span className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                  {formatCurrency(item.amount)}
                </span>
                {canWrite && (
                  <button
                    type="button"
                    onClick={() => item.id && requestDestructiveAction({
                      recordLabel: `${lowerNoun} ${item.name}`,
                      consequence: `This permanently removes the ${formatCurrency(item.amount)} ${lowerNoun} balance and its year-end classification evidence. The removal cannot be undone.`,
                      onConfirm: () => onDelete(item.id!),
                      successAnnouncement: `${noun} ${item.name} was removed.`,
                    })}
                    className="text-red-400 hover:text-red-600 dark:text-red-500 dark:hover:text-red-400"
                    aria-label={`Delete ${lowerNoun} ${item.name}`}
                  >
                    <Trash2 className="w-4 h-4" />
                  </button>
                )}
              </div>
            </div>
          ))}
        </div>
      )}

      {canWrite && <div className="mobile-form-grid grid grid-cols-12 gap-3 items-end">
        <div className={mode === "debtors" ? "col-span-4" : "col-span-3"}>
          <label htmlFor={`${mode}-entry-name`} className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Name</label>
          <input
            id={`${mode}-entry-name`}
            type="text"
            className={inputClass}
            placeholder={namePlaceholder}
            value={draft.name}
            onChange={(event) => onDraftChange({ ...draft, name: event.target.value })}
            aria-label={`${noun} name`}
          />
        </div>
        <div className={mode === "debtors" ? "col-span-3" : "col-span-2"}>
          <label htmlFor={`${mode}-entry-amount`} className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Amount</label>
          <input
            id={`${mode}-entry-amount`}
            type="number"
            className={inputClass}
            placeholder="0.00"
            value={draft.amount || ""}
            onChange={(event) => onDraftChange({ ...draft, amount: Number(event.target.value) })}
            aria-label={`${noun} amount`}
          />
        </div>
        <div className={mode === "debtors" ? "col-span-3" : "col-span-2"}>
          <label htmlFor={`${mode}-entry-type`} className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Type</label>
          <select
            id={`${mode}-entry-type`}
            className={selectClass}
            value={draft.type}
            onChange={(event) => onDraftChange({ ...draft, type: event.target.value })}
            title={`${noun} type`}
            aria-label={`${noun} type`}
          >
            {typeOptions.map((option) => (
              <option key={option} value={option}>{option}</option>
            ))}
          </select>
        </div>
        {mode === "creditors" && "dueWithinYear" in draft && (
          <div className="col-span-3">
            <label htmlFor={`${mode}-entry-due-within-year`} className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Due within year?</label>
            <select
              id={`${mode}-entry-due-within-year`}
              className={selectClass}
              value={draft.dueWithinYear ? "yes" : "no"}
              onChange={(event) => onDraftChange({ ...draft, dueWithinYear: event.target.value === "yes" })}
              title="Due within one year"
              aria-label="Due within one year"
            >
              <option value="yes">Yes</option>
              <option value="no">No</option>
            </select>
          </div>
        )}
        <div className="col-span-2">
          <Button
            variant="primary"
            size="sm"
            onPress={onAdd}
            isDisabled={saving}
            className="w-full"
            aria-label={`Add ${lowerNoun}`}
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
