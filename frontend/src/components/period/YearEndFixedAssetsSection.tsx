"use client";

import { Button, Chip, Spinner } from "@heroui/react";
import { Plus, Trash2 } from "lucide-react";

import type { FixedAsset } from "@/lib/api";

interface YearEndFixedAssetsSectionProps {
  assets: FixedAsset[];
  draft: FixedAsset;
  saving: boolean;
  onDraftChange: (draft: FixedAsset) => void;
  onAdd: () => void;
  onDelete: (id: number) => void;
}

const inputClass =
  "w-full rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 px-3 py-2 text-sm text-gray-900 dark:text-gray-100 outline-none focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 transition-colors";
const selectClass =
  "w-full rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 px-3 py-2 text-sm text-gray-900 dark:text-gray-100 outline-none focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 transition-colors";

export function YearEndFixedAssetsSection({
  assets,
  draft,
  saving,
  onDraftChange,
  onAdd,
  onDelete,
}: YearEndFixedAssetsSectionProps) {
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
                  <span className="text-xs text-gray-400 dark:text-gray-500">
                    {asset.usefulLifeYears}yr {asset.depreciationMethod}
                  </span>
                  <span className="text-xs text-gray-400 dark:text-gray-500">
                    Acquired {new Date(asset.acquisitionDate).toLocaleDateString("en-IE")}
                  </span>
                </div>
              </div>
              <div className="flex items-center gap-3">
                <span className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                  {formatCurrency(asset.cost)}
                </span>
                <button
                  type="button"
                  onClick={() => asset.id && onDelete(asset.id)}
                  className="text-red-400 hover:text-red-600 dark:text-red-500 dark:hover:text-red-400"
                  aria-label={`Delete asset ${asset.name}`}
                >
                  <Trash2 className="w-4 h-4" />
                </button>
              </div>
            </div>
          ))}
        </div>
      )}

      <div className="space-y-3">
        <div className="grid grid-cols-12 gap-3 items-end">
          <div className="col-span-4">
            <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Asset Name</label>
            <input
              type="text"
              className={inputClass}
              placeholder="e.g. Company Van"
              value={draft.name}
              onChange={(event) => onDraftChange({ ...draft, name: event.target.value })}
              aria-label="Asset name"
            />
          </div>
          <div className="col-span-3">
            <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Category</label>
            <select
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
            <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Cost</label>
            <input
              type="number"
              className={inputClass}
              placeholder="0.00"
              value={draft.cost || ""}
              onChange={(event) => onDraftChange({ ...draft, cost: Number(event.target.value) })}
              aria-label="Asset cost"
            />
          </div>
          <div className="col-span-3">
            <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Acquisition Date</label>
            <input
              type="date"
              className={inputClass}
              value={draft.acquisitionDate}
              onChange={(event) => onDraftChange({ ...draft, acquisitionDate: event.target.value })}
              aria-label="Asset acquisition date"
            />
          </div>
        </div>
        <div className="grid grid-cols-12 gap-3 items-end">
          <div className="col-span-3">
            <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Useful Life (years)</label>
            <input
              type="number"
              className={inputClass}
              value={draft.usefulLifeYears}
              onChange={(event) => onDraftChange({ ...draft, usefulLifeYears: Number(event.target.value) })}
              aria-label="Useful life in years"
            />
          </div>
          <div className="col-span-4">
            <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Depreciation Method</label>
            <select
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
          <div className="col-span-5 flex justify-end">
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
