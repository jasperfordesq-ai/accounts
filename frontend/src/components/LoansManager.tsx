"use client";

import { useCallback, useEffect, useState } from "react";
import { Button, Chip, Spinner } from "@heroui/react";
import { Pencil, Plus, Trash2, X } from "lucide-react";
import { toast } from "sonner";
import { getLoans, createLoan, updateLoan, deleteLoan, type Loan } from "@/lib/api";

const inputClass =
  "w-full rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 px-3 py-2 text-sm text-gray-900 dark:text-gray-100 outline-none focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 transition-colors";

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat("en-IE", { style: "currency", currency: "EUR" }).format(amount);
}

function emptyForm(periodEnd?: string): Loan {
  return {
    lender: "",
    originalAmount: 0,
    balance: 0,
    drawdownDate: "",
    balanceAsOfDate: periodEnd ?? "",
    interestRate: 0,
    isDirectorLoan: false,
    dueWithinYear: 0,
    dueAfterYear: 0,
  };
}

/**
 * Loans & borrowings entry for the year-end Loans section (frontend-loans-no-ui). Replaces the old
 * "managed in Company Setup" dead-end. A loan's DueWithinYear / DueAfterYear split feeds the balance
 * sheet (creditors due within / after one year); the split is derived from balance minus the amount the
 * user marks as due within a year so it always cross-adds. balanceAsOfDate defaults to the period end so
 * the loan lands in this period's balance sheet.
 */
export function LoansManager({
  companyId,
  periodEnd,
  canWrite = true,
  onCountChange,
}: {
  companyId: number;
  periodEnd?: string;
  canWrite?: boolean;
  onCountChange?: (count: number) => void;
}) {
  const [loans, setLoans] = useState<Loan[]>([]);
  const [loading, setLoading] = useState(true);
  const [form, setForm] = useState<Loan>(() => emptyForm(periodEnd));
  const [editingId, setEditingId] = useState<number | null>(null);
  const [saving, setSaving] = useState(false);

  const publishCount = useCallback(
    (next: Loan[]) => onCountChange?.(next.length),
    [onCountChange],
  );

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const data = await getLoans(companyId);
      setLoans(data);
      publishCount(data);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to load loans");
    } finally {
      setLoading(false);
    }
  }, [companyId, publishCount]);

  useEffect(() => { load(); }, [load]);

  const dueAfterYear = Math.max(0, form.balance - form.dueWithinYear);

  function startEdit(l: Loan) {
    setEditingId(l.id ?? null);
    setForm({ ...l });
  }

  function cancelEdit() {
    setEditingId(null);
    setForm(emptyForm(periodEnd));
  }

  async function handleSubmit() {
    if (!form.lender.trim()) { toast.error("Lender is required"); return; }
    if (!form.drawdownDate) { toast.error("Drawdown date is required"); return; }
    if (!form.balanceAsOfDate) { toast.error("Balance as-of date is required"); return; }
    if (form.balanceAsOfDate < form.drawdownDate) {
      toast.error("Balance as-of date cannot be before the drawdown date");
      return;
    }
    if (form.dueWithinYear > form.balance) {
      toast.error("Amount due within one year cannot exceed the balance");
      return;
    }
    setSaving(true);
    try {
      const payload = { ...form, dueAfterYear };
      if (editingId != null) {
        // PUT preserves the loan id and audit continuity (no delete + re-add).
        const updated = await updateLoan(companyId, editingId, payload);
        setLoans((prev) => prev.map((l) => (l.id === editingId ? updated : l)));
        toast.success("Loan updated");
      } else {
        const created = await createLoan(companyId, payload);
        const next = [...loans, created];
        setLoans(next);
        publishCount(next);
        toast.success("Loan recorded");
      }
      setEditingId(null);
      setForm(emptyForm(periodEnd));
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to save loan");
    } finally {
      setSaving(false);
    }
  }

  async function handleDelete(id: number) {
    setSaving(true);
    try {
      await deleteLoan(companyId, id);
      const next = loans.filter((l) => l.id !== id);
      setLoans(next);
      publishCount(next);
      toast.success("Loan removed");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to remove loan");
    } finally {
      setSaving(false);
    }
  }

  if (loading) {
    return <div className="py-6 flex justify-center"><Spinner size="sm" /></div>;
  }

  return (
    <div>
      {loans.length > 0 && (
        <div className="space-y-2 mb-4">
          {loans.map((l) => (
            <div
              key={l.id}
              className="flex items-center justify-between rounded-lg border border-gray-200 dark:border-neutral-700 px-4 py-3 dark:bg-neutral-800/50"
            >
              <div>
                <p className="text-sm font-medium text-gray-900 dark:text-gray-100">{l.lender}</p>
                <div className="flex items-center gap-2 mt-0.5 flex-wrap">
                  <Chip variant="soft" size="sm" color="default">
                    {l.interestRate}% interest
                  </Chip>
                  <span className="text-xs text-gray-400 dark:text-gray-500">
                    Due &lt; 1yr {formatCurrency(l.dueWithinYear)} · &gt; 1yr {formatCurrency(l.dueAfterYear)}
                  </span>
                  {l.isDirectorLoan && (
                    <Chip variant="soft" size="sm" color="warning">Director loan</Chip>
                  )}
                </div>
              </div>
              <div className="flex items-center gap-3">
                <span className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                  {formatCurrency(l.balance)}
                </span>
                {canWrite && (
                  <>
                    <button
                      type="button"
                      onClick={() => startEdit(l)}
                      className="text-gray-400 hover:text-emerald-600 dark:text-gray-500 dark:hover:text-emerald-400"
                      aria-label={`Edit loan from ${l.lender}`}
                    >
                      <Pencil className="w-4 h-4" />
                    </button>
                    <button
                      type="button"
                      onClick={() => l.id && handleDelete(l.id)}
                      className="text-red-400 hover:text-red-600 dark:text-red-500 dark:hover:text-red-400"
                      aria-label={`Delete loan from ${l.lender}`}
                    >
                      <Trash2 className="w-4 h-4" />
                    </button>
                  </>
                )}
              </div>
            </div>
          ))}
        </div>
      )}

      {!canWrite ? (
        <p className="text-xs text-gray-400 dark:text-gray-500 italic">
          Your role has read-only access to loans.
        </p>
      ) : (
      <div className="space-y-3">
        <div className="grid grid-cols-12 gap-3 items-end">
          <div className="col-span-4">
            <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Lender</label>
            <input
              type="text"
              className={inputClass}
              placeholder="e.g. Bank of Ireland"
              value={form.lender}
              onChange={(e) => setForm({ ...form, lender: e.target.value })}
              aria-label="Lender"
            />
          </div>
          <div className="col-span-3">
            <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Original Amount</label>
            <input
              type="number"
              className={inputClass}
              placeholder="0.00"
              value={form.originalAmount || ""}
              onChange={(e) => setForm({ ...form, originalAmount: Number(e.target.value) })}
              aria-label="Original amount"
            />
          </div>
          <div className="col-span-3">
            <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Balance Outstanding</label>
            <input
              type="number"
              className={inputClass}
              placeholder="0.00"
              value={form.balance || ""}
              onChange={(e) => setForm({ ...form, balance: Number(e.target.value) })}
              aria-label="Balance outstanding"
            />
          </div>
          <div className="col-span-2">
            <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Interest %</label>
            <input
              type="number"
              className={inputClass}
              placeholder="0.0"
              value={form.interestRate || ""}
              onChange={(e) => setForm({ ...form, interestRate: Number(e.target.value) })}
              aria-label="Interest rate"
            />
          </div>
        </div>
        <div className="grid grid-cols-12 gap-3 items-end">
          <div className="col-span-3">
            <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Drawdown Date</label>
            <input
              type="date"
              className={inputClass}
              value={form.drawdownDate ?? ""}
              onChange={(e) => setForm({ ...form, drawdownDate: e.target.value })}
              aria-label="Drawdown date"
            />
          </div>
          <div className="col-span-3">
            <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Balance As-Of Date</label>
            <input
              type="date"
              className={inputClass}
              value={form.balanceAsOfDate ?? ""}
              onChange={(e) => setForm({ ...form, balanceAsOfDate: e.target.value })}
              aria-label="Balance as-of date"
            />
          </div>
          <div className="col-span-3">
            <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Due Within 1 Year</label>
            <input
              type="number"
              className={inputClass}
              placeholder="0.00"
              value={form.dueWithinYear || ""}
              onChange={(e) => setForm({ ...form, dueWithinYear: Number(e.target.value) })}
              aria-label="Amount due within one year"
            />
          </div>
          <div className="col-span-3 flex items-center justify-between pb-2">
            <span className="text-xs text-gray-500 dark:text-gray-400">
              Due &gt; 1yr:{" "}
              <span className="font-semibold text-gray-900 dark:text-gray-100">{formatCurrency(dueAfterYear)}</span>
            </span>
          </div>
        </div>
        <div className="flex items-center justify-between">
          <label className="flex items-center gap-2 text-xs font-medium text-gray-600 dark:text-gray-400">
            <input
              type="checkbox"
              checked={form.isDirectorLoan}
              onChange={(e) => setForm({ ...form, isDirectorLoan: e.target.checked })}
              className="rounded border-gray-300 dark:border-neutral-600 text-emerald-600 focus:ring-emerald-500"
              aria-label="This is a director loan"
            />
            Loan from a director
          </label>
          <div className="flex items-center gap-2">
            {editingId != null && (
              <Button variant="ghost" size="sm" onPress={cancelEdit} isDisabled={saving}>
                <X className="w-4 h-4 mr-1" /> Cancel
              </Button>
            )}
            <Button variant="primary" size="sm" onPress={handleSubmit} isDisabled={saving}>
              {saving ? <Spinner size="sm" /> : editingId != null
                ? <>Save changes</>
                : <><Plus className="w-4 h-4 mr-1" /> Add Loan</>}
            </Button>
          </div>
        </div>
      </div>
      )}
    </div>
  );
}
