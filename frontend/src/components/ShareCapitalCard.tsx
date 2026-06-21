"use client";

import { useCallback, useEffect, useState } from "react";
import {
  Card, CardContent, CardHeader, CardTitle, Button, Chip, Spinner,
} from "@heroui/react";
import { Coins, Pencil, Plus, Trash2, X } from "lucide-react";
import { toast } from "sonner";
import {
  getShareCapital, createShareCapital, updateShareCapital, deleteShareCapital, type ShareCapital,
} from "@/lib/api";

const inputClass =
  "w-full rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 px-3 py-2 text-sm text-gray-900 dark:text-gray-100 outline-none focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 transition-colors";

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat("en-IE", { style: "currency", currency: "EUR" }).format(amount);
}

const emptyForm: ShareCapital = {
  shareClass: "Ordinary",
  nominalValue: 1,
  numberIssued: 0,
  totalValue: 0,
  isFullyPaid: true,
  issueDate: "",
};

/**
 * Company-scoped share-capital entry (frontend-share-capital-no-ui). Issued shares feed the Share
 * Capital note, the SOCIE `sharesIssued`, and `BalanceSheet.capitalAndReserves.shareCapital` — with no
 * recorded share capital a non-CLG company is a readiness blocker, so this is the only way firm staff
 * can reach a correct balance sheet for a company with real equity.
 */
export function ShareCapitalCard({
  companyId,
  canWrite = true,
}: {
  companyId: number;
  canWrite?: boolean;
}) {
  const [shares, setShares] = useState<ShareCapital[]>([]);
  const [loading, setLoading] = useState(true);
  const [form, setForm] = useState<ShareCapital>(emptyForm);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [saving, setSaving] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setShares(await getShareCapital(companyId));
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to load share capital");
    } finally {
      setLoading(false);
    }
  }, [companyId]);

  useEffect(() => { load(); }, [load]);

  const totalIssued = shares.reduce((sum, s) => sum + s.totalValue, 0);
  const previewTotal = form.nominalValue * form.numberIssued;

  function startEdit(s: ShareCapital) {
    setEditingId(s.id ?? null);
    setForm({ ...s });
  }

  function cancelEdit() {
    setEditingId(null);
    setForm(emptyForm);
  }

  async function handleSubmit() {
    if (!form.shareClass.trim()) { toast.error("Share class is required"); return; }
    if (!form.issueDate) { toast.error("Issue date is required"); return; }
    if (form.numberIssued <= 0) { toast.error("Number of shares issued must be greater than zero"); return; }
    if (form.nominalValue < 0) { toast.error("Nominal value cannot be negative"); return; }
    setSaving(true);
    try {
      const payload = { ...form, totalValue: previewTotal };
      if (editingId != null) {
        // PUT preserves the row id and audit continuity (no delete + re-add).
        const updated = await updateShareCapital(companyId, editingId, payload);
        setShares((prev) => prev.map((s) => (s.id === editingId ? updated : s)));
        toast.success("Share capital updated");
      } else {
        const created = await createShareCapital(companyId, payload);
        setShares((prev) => [...prev, created]);
        toast.success("Share capital recorded");
      }
      setEditingId(null);
      setForm(emptyForm);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to save share capital");
    } finally {
      setSaving(false);
    }
  }

  async function handleDelete(id: number) {
    setSaving(true);
    try {
      await deleteShareCapital(companyId, id);
      setShares((prev) => prev.filter((s) => s.id !== id));
      toast.success("Share capital removed");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to remove share capital");
    } finally {
      setSaving(false);
    }
  }

  return (
    <Card className="bg-white dark:bg-neutral-900 shadow-sm border border-gray-200 dark:border-neutral-700 mb-8">
      <CardHeader className="flex flex-row items-center justify-between">
        <CardTitle className="flex items-center gap-2 text-gray-900 dark:text-gray-100">
          <Coins className="w-4 h-4 text-emerald-600 dark:text-emerald-400" />
          Share Capital
        </CardTitle>
        {shares.length > 0 && (
          <Chip size="sm" color="success" variant="soft">
            {formatCurrency(totalIssued)} issued
          </Chip>
        )}
      </CardHeader>
      <CardContent>
        {loading ? (
          <div className="py-6 flex justify-center"><Spinner size="sm" /></div>
        ) : (
          <>
            {shares.length > 0 ? (
              <div className="space-y-2 mb-4">
                {shares.map((s) => (
                  <div
                    key={s.id}
                    className="flex items-center justify-between rounded-lg border border-gray-200 dark:border-neutral-700 px-4 py-3 dark:bg-neutral-800/50"
                  >
                    <div>
                      <p className="text-sm font-medium text-gray-900 dark:text-gray-100">
                        {s.numberIssued.toLocaleString("en-IE")} {s.shareClass} shares
                        @ {formatCurrency(s.nominalValue)}
                      </p>
                      <div className="flex items-center gap-2 mt-0.5">
                        <Chip variant="soft" size="sm" color={s.isFullyPaid ? "success" : "warning"}>
                          {s.isFullyPaid ? "Fully paid" : "Partly paid"}
                        </Chip>
                        {s.issueDate && (
                          <span className="text-xs text-gray-400 dark:text-gray-500">
                            Issued {new Date(s.issueDate).toLocaleDateString("en-IE")}
                          </span>
                        )}
                      </div>
                    </div>
                    <div className="flex items-center gap-3">
                      <span className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                        {formatCurrency(s.totalValue)}
                      </span>
                      {canWrite && (
                        <>
                          <button
                            type="button"
                            onClick={() => startEdit(s)}
                            className="text-gray-400 hover:text-emerald-600 dark:text-gray-500 dark:hover:text-emerald-400"
                            aria-label={`Edit ${s.shareClass} share capital`}
                          >
                            <Pencil className="w-4 h-4" />
                          </button>
                          <button
                            type="button"
                            onClick={() => s.id && handleDelete(s.id)}
                            className="text-red-400 hover:text-red-600 dark:text-red-500 dark:hover:text-red-400"
                            aria-label={`Delete ${s.shareClass} share capital`}
                          >
                            <Trash2 className="w-4 h-4" />
                          </button>
                        </>
                      )}
                    </div>
                  </div>
                ))}
              </div>
            ) : (
              <p className="text-sm text-gray-400 dark:text-gray-500 italic mb-4">
                No share capital recorded yet. Record issued shares so they appear in the balance
                sheet and the statement of changes in equity.
              </p>
            )}

            {!canWrite ? (
              <p className="text-xs text-gray-400 dark:text-gray-500 italic">
                Your role has read-only access to share capital.
              </p>
            ) : (
            <>
            <div className="grid grid-cols-12 gap-3 items-end">
              <div className="col-span-3">
                <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Share Class</label>
                <select
                  className={inputClass}
                  value={form.shareClass}
                  onChange={(e) => setForm({ ...form, shareClass: e.target.value })}
                  aria-label="Share class"
                  title="Share class"
                >
                  <option value="Ordinary">Ordinary</option>
                  <option value="Preference">Preference</option>
                  <option value="Redeemable">Redeemable</option>
                  <option value="Deferred">Deferred</option>
                </select>
              </div>
              <div className="col-span-2">
                <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Number Issued</label>
                <input
                  type="number"
                  className={inputClass}
                  placeholder="0"
                  value={form.numberIssued || ""}
                  onChange={(e) => setForm({ ...form, numberIssued: Number(e.target.value) })}
                  aria-label="Number of shares issued"
                />
              </div>
              <div className="col-span-2">
                <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Nominal Value</label>
                <input
                  type="number"
                  className={inputClass}
                  placeholder="0.00"
                  value={form.nominalValue || ""}
                  onChange={(e) => setForm({ ...form, nominalValue: Number(e.target.value) })}
                  aria-label="Nominal value per share"
                />
              </div>
              <div className="col-span-2">
                <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Issue Date</label>
                <input
                  type="date"
                  className={inputClass}
                  value={form.issueDate ?? ""}
                  onChange={(e) => setForm({ ...form, issueDate: e.target.value })}
                  aria-label="Share issue date"
                />
              </div>
              <div className="col-span-3 flex items-center gap-2 pb-2">
                <input
                  type="checkbox"
                  id="share-fully-paid"
                  checked={form.isFullyPaid}
                  onChange={(e) => setForm({ ...form, isFullyPaid: e.target.checked })}
                  className="rounded border-gray-300 dark:border-neutral-600 text-emerald-600 focus:ring-emerald-500"
                />
                <label htmlFor="share-fully-paid" className="text-xs font-medium text-gray-600 dark:text-gray-400">
                  Fully paid
                </label>
              </div>
            </div>
            <div className="mt-3 flex items-center justify-between">
              <span className="text-xs text-gray-500 dark:text-gray-400">
                Total value: <span className="font-semibold text-gray-900 dark:text-gray-100">{formatCurrency(previewTotal)}</span>
              </span>
              <div className="flex items-center gap-2">
                {editingId != null && (
                  <Button variant="ghost" size="sm" onPress={cancelEdit} isDisabled={saving}>
                    <X className="w-4 h-4 mr-1" /> Cancel
                  </Button>
                )}
                <Button
                  variant="primary"
                  size="sm"
                  onPress={handleSubmit}
                  isDisabled={saving}
                >
                  {saving ? <Spinner size="sm" /> : editingId != null
                    ? <>Save changes</>
                    : <><Plus className="w-4 h-4 mr-1" /> Issue Shares</>}
                </Button>
              </div>
            </div>
            </>
            )}
          </>
        )}
      </CardContent>
    </Card>
  );
}
