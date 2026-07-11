"use client";

import { Button, Chip, Spinner } from "@heroui/react";
import { Plus, Trash2 } from "lucide-react";

import type { PostBalanceSheetEvent } from "@/lib/api";
import { useDestructiveActionConfirmation } from "@/lib/useDestructiveAction";

interface YearEndPostBalanceSheetEventsSectionProps {
  canWrite?: boolean;
  events: PostBalanceSheetEvent[];
  draft: PostBalanceSheetEvent;
  saving: boolean;
  onDraftChange: (draft: PostBalanceSheetEvent) => void;
  onAdd: () => void;
  onDelete: (id: number) => void | Promise<void>;
}

const inputClass =
  "w-full rounded-lg border border-[var(--control-border)] bg-white px-3 py-2 text-sm text-gray-900 outline-none transition-colors focus:border-emerald-500 focus:ring-2 focus:ring-emerald-500 dark:bg-neutral-800 dark:text-gray-100";

export function YearEndPostBalanceSheetEventsSection({
  canWrite = true,
  events,
  draft,
  saving,
  onDraftChange,
  onAdd,
  onDelete,
}: YearEndPostBalanceSheetEventsSectionProps) {
  const { requestDestructiveAction, destructiveActionConfirmation } = useDestructiveActionConfirmation();

  return (
    <>
      {events.length > 0 && (
        <div className="space-y-2 mb-4">
          {events.map((event) => (
            <div
              key={event.id}
              className="flex items-center justify-between rounded-lg border border-gray-200 dark:border-neutral-700 px-4 py-3 dark:bg-neutral-800/50"
            >
              <div>
                <p className="text-sm font-medium text-gray-900 dark:text-gray-100">{event.description}</p>
                <div className="flex items-center gap-2 mt-0.5">
                  <span className="text-xs text-[var(--muted-foreground)]">
                    {new Date(event.eventDate).toLocaleDateString("en-IE")}
                  </span>
                  <Chip variant="soft" size="sm" color={event.isAdjusting ? "warning" : "default"}>
                    {event.isAdjusting ? "Adjusting" : "Non-adjusting"}
                  </Chip>
                  {event.financialImpact != null && event.financialImpact !== 0 && (
                    <span className="text-xs text-gray-500 dark:text-gray-400">
                      Impact: {formatCurrency(event.financialImpact)}
                    </span>
                  )}
                </div>
              </div>
              {canWrite && <button
                type="button"
                onClick={() => event.id && requestDestructiveAction({
                  recordLabel: `post-balance-sheet event ${event.description}`,
                  consequence: `This permanently removes the ${event.isAdjusting ? "adjusting" : "non-adjusting"} event dated ${new Date(event.eventDate).toLocaleDateString("en-IE")} and its financial-impact evidence. The removal cannot be undone.`,
                  onConfirm: () => onDelete(event.id!),
                  successAnnouncement: `Post-balance-sheet event ${event.description} was removed.`,
                })}
                className="text-red-600 hover:text-red-700 dark:text-red-400 dark:hover:text-red-300"
                aria-label={`Delete event ${event.description}`}
              >
                <Trash2 className="w-4 h-4" />
              </button>}
            </div>
          ))}
        </div>
      )}

      {canWrite && <div className="mobile-form-grid grid grid-cols-12 gap-3 items-end">
        <div className="col-span-4">
          <label htmlFor="post-balance-sheet-event-description" className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Description</label>
          <input
            id="post-balance-sheet-event-description"
            type="text"
            className={inputClass}
            placeholder="e.g. Major contract signed"
            value={draft.description}
            onChange={(event) => onDraftChange({ ...draft, description: event.target.value })}
            aria-label="Event description"
          />
        </div>
        <div className="col-span-2">
          <label htmlFor="post-balance-sheet-event-date" className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Date</label>
          <input
            id="post-balance-sheet-event-date"
            type="date"
            className={inputClass}
            value={draft.eventDate}
            onChange={(event) => onDraftChange({ ...draft, eventDate: event.target.value })}
            aria-label="Event date"
          />
        </div>
        <div className="col-span-2">
          <label htmlFor="post-balance-sheet-event-impact" className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Financial Impact</label>
          <input
            id="post-balance-sheet-event-impact"
            type="number"
            className={inputClass}
            placeholder="0.00"
            value={draft.financialImpact || ""}
            onChange={(event) => onDraftChange({ ...draft, financialImpact: Number(event.target.value) })}
            aria-label="Financial impact"
          />
        </div>
        <div className="col-span-2 flex items-center gap-2 pb-2">
          <input
            type="checkbox"
            id="pbse-adjusting"
            checked={draft.isAdjusting}
            onChange={(event) => onDraftChange({ ...draft, isAdjusting: event.target.checked })}
            className="rounded border-[var(--control-border)] text-emerald-600 focus:ring-emerald-500"
          />
          <label htmlFor="pbse-adjusting" className="text-xs font-medium text-gray-600 dark:text-gray-400">
            Adjusting
          </label>
        </div>
        <div className="col-span-2">
          <Button
            variant="primary"
            size="sm"
            onPress={onAdd}
            isDisabled={saving}
            className="w-full"
            aria-label="Add post-balance sheet event"
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
