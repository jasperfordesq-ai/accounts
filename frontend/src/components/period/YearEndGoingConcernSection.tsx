"use client";

import { Button, Spinner } from "@heroui/react";

interface YearEndGoingConcernSectionProps {
  confirmed: boolean;
  note: string;
  saving: boolean;
  onConfirmedChange: (confirmed: boolean) => void;
  onNoteChange: (note: string) => void;
  onSave: () => void;
}

const inputClass =
  "w-full rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 px-3 py-2 text-sm text-gray-900 dark:text-gray-100 outline-none focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 transition-colors";

export function YearEndGoingConcernSection({
  confirmed,
  note,
  saving,
  onConfirmedChange,
  onNoteChange,
  onSave,
}: YearEndGoingConcernSectionProps) {
  return (
    <div className="space-y-4">
      {!confirmed && (
        <div className="rounded-lg bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-700 p-3">
          <p className="text-sm font-medium text-red-800 dark:text-red-300">
            Warning: Going concern is not confirmed. Material uncertainty disclosures will be required in the financial statements.
          </p>
        </div>
      )}

      <div className="flex items-center gap-3">
        <input
          type="checkbox"
          id="going-concern-confirmed"
          checked={confirmed}
          onChange={(event) => onConfirmedChange(event.target.checked)}
          className="rounded border-gray-300 dark:border-neutral-600 text-emerald-600 focus:ring-emerald-500 w-5 h-5"
        />
        <label htmlFor="going-concern-confirmed" className="text-sm font-medium text-gray-900 dark:text-gray-100">
          The directors confirm the company is a going concern
        </label>
      </div>

      {!confirmed && (
        <div>
          <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">
            Material uncertainty / going concern note
          </label>
          <textarea
            className={`${inputClass} min-h-[100px]`}
            placeholder="Describe the material uncertainties that cast significant doubt on the company's ability to continue as a going concern..."
            value={note}
            onChange={(event) => onNoteChange(event.target.value)}
            aria-label="Going concern note"
          />
        </div>
      )}

      <div className="flex justify-end">
        <Button
          variant="primary"
          size="sm"
          onPress={onSave}
          isDisabled={saving}
        >
          {saving ? <Spinner size="sm" /> : "Save Going Concern"}
        </Button>
      </div>
    </div>
  );
}
