"use client";

import { Button, Chip, Spinner } from "@heroui/react";
import { Plus, Trash2 } from "lucide-react";

import type { RelatedPartyTransaction } from "@/lib/api";

interface YearEndRelatedPartyTransactionsSectionProps {
  transactions: RelatedPartyTransaction[];
  draft: RelatedPartyTransaction;
  saving: boolean;
  onDraftChange: (draft: RelatedPartyTransaction) => void;
  onAdd: () => void;
  onDelete: (id: number) => void;
}

const inputClass =
  "w-full rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 px-3 py-2 text-sm text-gray-900 dark:text-gray-100 outline-none focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 transition-colors";
const selectClass =
  "w-full rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 px-3 py-2 text-sm text-gray-900 dark:text-gray-100 outline-none focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 transition-colors";

export function YearEndRelatedPartyTransactionsSection({
  transactions,
  draft,
  saving,
  onDraftChange,
  onAdd,
  onDelete,
}: YearEndRelatedPartyTransactionsSectionProps) {
  return (
    <>
      {transactions.length > 0 && (
        <div className="space-y-2 mb-4">
          {transactions.map((transaction) => (
            <div
              key={transaction.id}
              className="flex items-center justify-between rounded-lg border border-gray-200 dark:border-neutral-700 px-4 py-3 dark:bg-neutral-800/50"
            >
              <div>
                <p className="text-sm font-medium text-gray-900 dark:text-gray-100">{transaction.partyName}</p>
                <div className="flex items-center gap-2 mt-0.5">
                  <Chip variant="soft" size="sm" color="default">{transaction.relationship}</Chip>
                  <Chip variant="soft" size="sm" color="default">{transaction.transactionType}</Chip>
                  {transaction.balanceOwed != null && transaction.balanceOwed !== 0 && (
                    <span className="text-xs text-gray-500 dark:text-gray-400">
                      Balance owed: {formatCurrency(transaction.balanceOwed)}
                    </span>
                  )}
                </div>
              </div>
              <div className="flex items-center gap-3">
                <span className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                  {formatCurrency(transaction.amount)}
                </span>
                <button
                  type="button"
                  onClick={() => transaction.id && onDelete(transaction.id)}
                  className="text-red-400 hover:text-red-600 dark:text-red-500 dark:hover:text-red-400"
                  aria-label={`Delete transaction with ${transaction.partyName}`}
                >
                  <Trash2 className="w-4 h-4" />
                </button>
              </div>
            </div>
          ))}
        </div>
      )}

      <div className="grid grid-cols-12 gap-3 items-end">
        <div className="col-span-3">
          <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Party Name</label>
          <input
            type="text"
            className={inputClass}
            placeholder="e.g. John Smith"
            value={draft.partyName}
            onChange={(event) => onDraftChange({ ...draft, partyName: event.target.value })}
            aria-label="Party name"
          />
        </div>
        <div className="col-span-3">
          <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Relationship</label>
          <select
            className={selectClass}
            value={draft.relationship}
            onChange={(event) => onDraftChange({ ...draft, relationship: event.target.value })}
            title="Relationship"
            aria-label="Relationship"
          >
            <option value="Director">Director</option>
            <option value="Connected Person">Connected Person</option>
            <option value="Group Company">Group Company</option>
            <option value="Key Management">Key Management</option>
          </select>
        </div>
        <div className="col-span-2">
          <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Type</label>
          <select
            className={selectClass}
            value={draft.transactionType}
            onChange={(event) => onDraftChange({ ...draft, transactionType: event.target.value })}
            title="Transaction type"
            aria-label="Transaction type"
          >
            <option value="Sale">Sale</option>
            <option value="Purchase">Purchase</option>
            <option value="Loan">Loan</option>
            <option value="Management Fee">Management Fee</option>
            <option value="Other">Other</option>
          </select>
        </div>
        <div className="col-span-2">
          <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Amount</label>
          <input
            type="number"
            className={inputClass}
            placeholder="0.00"
            value={draft.amount || ""}
            onChange={(event) => onDraftChange({ ...draft, amount: Number(event.target.value) })}
            aria-label="Transaction amount"
          />
        </div>
        <div className="col-span-2">
          <Button
            variant="primary"
            size="sm"
            onPress={onAdd}
            isDisabled={saving}
            className="w-full"
            aria-label="Add related party transaction"
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
