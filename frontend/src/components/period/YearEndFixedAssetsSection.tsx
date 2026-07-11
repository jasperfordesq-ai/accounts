"use client";

import { Button, Chip, Spinner } from "@heroui/react";
import { Plus, Trash2 } from "lucide-react";

import type { FixedAsset } from "@/lib/api";
import { useDestructiveActionConfirmation } from "@/lib/useDestructiveAction";

interface YearEndFixedAssetsSectionProps {
  canWrite?: boolean;
  assets: FixedAsset[];
  draft: FixedAsset;
  saving: boolean;
  onDraftChange: (draft: FixedAsset) => void;
  onAdd: () => void;
  onDelete: (id: number) => void | Promise<void>;
}

const inputClass =
  "w-full rounded-lg border border-[var(--control-border)] bg-white px-3 py-2 text-sm text-gray-900 outline-none transition-colors focus:border-emerald-500 focus:ring-2 focus:ring-emerald-500 dark:bg-neutral-800 dark:text-gray-100";
const selectClass =
  "w-full rounded-lg border border-[var(--control-border)] bg-white px-3 py-2 text-sm text-gray-900 outline-none transition-colors focus:border-emerald-500 focus:ring-2 focus:ring-emerald-500 dark:bg-neutral-800 dark:text-gray-100";

export function YearEndFixedAssetsSection({
  canWrite = true,
  assets,
  draft,
  saving,
  onDraftChange,
  onAdd,
  onDelete,
}: YearEndFixedAssetsSectionProps) {
  const { requestDestructiveAction, destructiveActionConfirmation } = useDestructiveActionConfirmation();

  return (
    <>
      {assets.length > 0 && (
        <div className="space-y-2 mb-4">
          {assets.map((asset) => (
            <div
              key={asset.id}
              className="flex items-center justify-between rounded-lg border border-gray-200 dark:border-neutral-700 px-4 py-3 dark:bg-neutral-800/50"
            >
              <div>
                <p className="text-sm font-medium text-gray-900 dark:text-gray-100">{asset.name}</p>
                <div className="flex items-center gap-2 mt-0.5">
                  <Chip variant="soft" size="sm" color="default">{asset.category}</Chip>
                  <span className="text-xs text-[var(--muted-foreground)]">
                    {asset.usefulLifeYears}yr {asset.depreciationMethod}
                  </span>
                  <span className="text-xs text-[var(--muted-foreground)]">
                    Acquired {new Date(asset.acquisitionDate).toLocaleDateString("en-IE")}
                  </span>
                  {(asset.residualValue ?? 0) > 0 && (
                    <span className="text-xs text-[var(--muted-foreground)]">
                      Residual {formatCurrency(asset.residualValue ?? 0)}
                    </span>
                  )}
                  {asset.disposalDate && (
                    <span className="text-xs text-amber-600 dark:text-amber-400">
                      Disposed {new Date(asset.disposalDate).toLocaleDateString("en-IE")}
                    </span>
                  )}
                  <Chip
                    variant="soft"
                    size="sm"
                    color={asset.capitalAllowanceTreatment === "PlantAndMachinery12Point5" ? "success" : asset.capitalAllowanceTreatment === "Unreviewed" ? "warning" : "default"}
                  >
                    Tax: {capitalAllowanceLabel(asset.capitalAllowanceTreatment)}
                  </Chip>
                </div>
              </div>
              <div className="flex items-center gap-3">
                <span className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                  {formatCurrency(asset.cost)}
                </span>
                {canWrite && <button
                  type="button"
                  onClick={() => asset.id && requestDestructiveAction({
                    recordLabel: `fixed asset ${asset.name}`,
                    consequence: `This permanently removes the ${formatCurrency(asset.cost)} asset, depreciation and capital-allowance evidence from the year-end records. The removal cannot be undone.`,
                    onConfirm: () => onDelete(asset.id!),
                    successAnnouncement: `Fixed asset ${asset.name} was removed.`,
                  })}
                  className="text-red-600 hover:text-red-700 dark:text-red-400 dark:hover:text-red-300"
                  aria-label={`Delete asset ${asset.name}`}
                >
                  <Trash2 className="w-4 h-4" />
                </button>}
              </div>
            </div>
          ))}
        </div>
      )}

      {canWrite && <div className="space-y-3">
        <div className="mobile-form-grid grid grid-cols-12 gap-3 items-end">
          <div className="col-span-4">
            <label htmlFor="fixed-asset-capital-allowance-treatment" className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Capital-allowance treatment</label>
            <select
              id="fixed-asset-capital-allowance-treatment"
              className={selectClass}
              value={draft.capitalAllowanceTreatment}
              onChange={(event) => onDraftChange({
                ...draft,
                capitalAllowanceTreatment: event.target.value as FixedAsset["capitalAllowanceTreatment"],
              })}
              aria-label="Capital allowance treatment"
            >
              <option value="Unreviewed">Unreviewed - blocks final tax charge</option>
              <option value="NonQualifying">Non-qualifying</option>
              <option value="PlantAndMachinery12Point5">Plant and machinery - 12.5%</option>
              <option value="UnsupportedSpecialScheme">Special scheme - manual tax review</option>
            </select>
          </div>
          <div className="col-span-8">
            <label htmlFor="fixed-asset-capital-allowance-evidence" className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Tax-treatment evidence</label>
            <input
              id="fixed-asset-capital-allowance-evidence"
              type="text"
              className={inputClass}
              value={draft.capitalAllowanceEvidence ?? ""}
              onChange={(event) => onDraftChange({ ...draft, capitalAllowanceEvidence: event.target.value })}
              aria-label="Capital allowance evidence"
              placeholder="Invoice/use evidence or reason non-qualifying (minimum 20 characters)"
            />
          </div>
        </div>
        <div className="mobile-form-grid grid grid-cols-12 gap-3 items-end">
          <div className="col-span-4">
            <label htmlFor="fixed-asset-name" className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Asset Name</label>
            <input
              id="fixed-asset-name"
              type="text"
              className={inputClass}
              placeholder="e.g. Company Van"
              value={draft.name}
              onChange={(event) => onDraftChange({ ...draft, name: event.target.value })}
              aria-label="Asset name"
            />
          </div>
          <div className="col-span-3">
            <label htmlFor="fixed-asset-category" className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Category</label>
            <select
              id="fixed-asset-category"
              className={selectClass}
              value={draft.category}
              onChange={(event) => onDraftChange({ ...draft, category: event.target.value })}
              title="Asset category"
              aria-label="Asset category"
            >
              <option value="Equipment">Equipment</option>
              <option value="Vehicles">Vehicles</option>
              <option value="Property">Property</option>
              <option value="Furniture">Furniture &amp; Fixtures</option>
              <option value="IT">IT Equipment</option>
              <option value="Other">Other</option>
            </select>
          </div>
          <div className="col-span-2">
            <label htmlFor="fixed-asset-cost" className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Cost</label>
            <input
              id="fixed-asset-cost"
              type="number"
              className={inputClass}
              placeholder="0.00"
              value={draft.cost || ""}
              onChange={(event) => onDraftChange({ ...draft, cost: Number(event.target.value) })}
              aria-label="Asset cost"
            />
          </div>
          <div className="col-span-3">
            <label htmlFor="fixed-asset-acquisition-date" className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Acquisition Date</label>
            <input
              id="fixed-asset-acquisition-date"
              type="date"
              className={inputClass}
              value={draft.acquisitionDate}
              onChange={(event) => onDraftChange({ ...draft, acquisitionDate: event.target.value })}
              aria-label="Asset acquisition date"
            />
          </div>
        </div>
        <div className="mobile-form-grid grid grid-cols-12 gap-3 items-end">
          <div className="col-span-3">
            <label htmlFor="fixed-asset-useful-life" className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Useful Life (years)</label>
            <input
              id="fixed-asset-useful-life"
              type="number"
              className={inputClass}
              value={draft.usefulLifeYears}
              onChange={(event) => onDraftChange({ ...draft, usefulLifeYears: Number(event.target.value) })}
              aria-label="Useful life in years"
            />
          </div>
          <div className="col-span-2">
            <label htmlFor="fixed-asset-residual-value" className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Residual Value</label>
            <input
              id="fixed-asset-residual-value"
              type="number"
              min="0"
              step="0.01"
              className={inputClass}
              value={draft.residualValue || ""}
              onChange={(event) => onDraftChange({ ...draft, residualValue: Number(event.target.value) })}
              aria-label="Asset residual value"
            />
          </div>
          <div className="col-span-4">
            <label htmlFor="fixed-asset-depreciation-method" className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Depreciation Method</label>
            <select
              id="fixed-asset-depreciation-method"
              className={selectClass}
              value={draft.depreciationMethod}
              onChange={(event) => onDraftChange({ ...draft, depreciationMethod: event.target.value })}
              title="Depreciation method"
              aria-label="Depreciation method"
            >
              <option value="StraightLine">Straight Line</option>
              <option value="ReducingBalance">Reducing Balance</option>
            </select>
          </div>
          <div className="col-span-3 flex justify-end">
            <Button
              variant="primary"
              size="sm"
              onPress={onAdd}
              isDisabled={saving}
              aria-label="Add asset"
            >
              {saving ? <Spinner size="sm" /> : <><Plus className="w-4 h-4 mr-1" /> Add Asset</>}
            </Button>
          </div>
        </div>
        <div className="mobile-form-grid grid grid-cols-12 gap-3 items-end">
          <div className="col-span-3">
            <label htmlFor="fixed-asset-disposal-date" className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Disposal Date (if disposed)</label>
            <input
              id="fixed-asset-disposal-date"
              type="date"
              className={inputClass}
              value={draft.disposalDate ?? ""}
              onChange={(event) => onDraftChange({ ...draft, disposalDate: event.target.value || undefined })}
              aria-label="Asset disposal date"
            />
          </div>
          <div className="col-span-3">
            <label htmlFor="fixed-asset-disposal-proceeds" className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Disposal Proceeds</label>
            <input
              id="fixed-asset-disposal-proceeds"
              type="number"
              min="0"
              step="0.01"
              className={inputClass}
              value={draft.disposalProceeds || ""}
              onChange={(event) => onDraftChange({ ...draft, disposalProceeds: Number(event.target.value) })}
              aria-label="Asset disposal proceeds"
              disabled={!draft.disposalDate}
            />
          </div>
          <p className="col-span-6 text-xs text-gray-500 dark:text-gray-400">
            Disposal proceeds must also be matched to the posted bank transaction so the asset ledger and cash flow reconcile.
          </p>
        </div>
      </div>}
      {destructiveActionConfirmation}
    </>
  );
}

function capitalAllowanceLabel(treatment: FixedAsset["capitalAllowanceTreatment"]): string {
  switch (treatment) {
    case "PlantAndMachinery12Point5": return "12.5% plant/machinery";
    case "NonQualifying": return "non-qualifying";
    case "UnsupportedSpecialScheme": return "manual special scheme";
    default: return "unreviewed";
  }
}

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat("en-IE", {
    style: "currency",
    currency: "EUR",
  }).format(amount);
}
