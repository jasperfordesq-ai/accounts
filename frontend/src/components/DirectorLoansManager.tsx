"use client";

import { useCallback, useEffect, useState } from "react";
import { Button, Chip, Spinner } from "@heroui/react";
import { Pencil, Plus, Trash2, X } from "lucide-react";
import { toast } from "sonner";
import {
  getDirectorLoans, createDirectorLoan, updateDirectorLoan, deleteDirectorLoan, type DirectorLoanRow,
} from "@/lib/api";
import { MoneyInput } from "@/components/workbench";

const inputClass =
  "w-full rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 px-3 py-2 text-sm text-gray-900 dark:text-gray-100 outline-none focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 transition-colors";

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat("en-IE", { style: "currency", currency: "EUR" }).format(amount);
}

export interface DirectorOption {
  id: number;
  name: string;
}

function emptyForm(directorId = 0): DirectorLoanRow {
  return {
    directorId,
    openingBalance: 0,
    advances: 0,
    repayments: 0,
    closingBalance: 0,
    interestRate: 0,
    interestCharged: 0,
    isDocumented: false,
    loanTerms: "",
    maxBalanceDuringYear: 0,
  };
}

/**
 * Director-loan create/edit for the year-end Director Loans section (frontend-director-loans-no-entry).
 * The section used to be display-only, so directorLoanCompliance was always null and the s.236 /
 * overdrawn-DLA checks never fired. Closing balance is derived (opening + advances - repayments) and the
 * max-during-year defaults to the larger of opening/closing (the figure the 10%-of-net-assets test uses),
 * both overridable. On save it asks the page to refresh the compliance summary.
 */
export function DirectorLoansManager({
  companyId,
  periodId,
  directors,
  canWrite = true,
  onCountChange,
  onSaved,
}: {
  companyId: number;
  periodId: number;
  directors: DirectorOption[];
  canWrite?: boolean;
  onCountChange?: (count: number) => void;
  onSaved?: () => void;
}) {
  const [rows, setRows] = useState<DirectorLoanRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [form, setForm] = useState<DirectorLoanRow>(() => emptyForm(directors[0]?.id ?? 0));
  const [editingId, setEditingId] = useState<number | null>(null);
  const [saving, setSaving] = useState(false);

  const publishCount = useCallback(
    (next: DirectorLoanRow[]) => onCountChange?.(next.length),
    [onCountChange],
  );

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const data = await getDirectorLoans(companyId, periodId);
      setRows(data);
      publishCount(data);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to load director loans");
    } finally {
      setLoading(false);
    }
  }, [companyId, periodId, publishCount]);

  useEffect(() => { load(); }, [load]);

  const directorName = (id: number) => directors.find((d) => d.id === id)?.name ?? `Director #${id}`;
  const closingBalance = form.openingBalance + form.advances - form.repayments;
  const maxDuringYear = form.maxBalanceDuringYear || Math.max(form.openingBalance, closingBalance);

  function startEdit(r: DirectorLoanRow) {
    setEditingId(r.id ?? null);
    setForm({ ...r });
  }

  function cancelEdit() {
    setEditingId(null);
    setForm(emptyForm(directors[0]?.id ?? 0));
  }

  async function handleSubmit() {
    if (!form.directorId) { toast.error("Select the director"); return; }
    setSaving(true);
    try {
      const payload = {
        ...form,
        closingBalance,
        maxBalanceDuringYear: maxDuringYear,
        loanTerms: form.loanTerms?.trim() ? form.loanTerms.trim() : undefined,
      };
      if (editingId != null) {
        // PUT preserves the row id and audit continuity (no delete + re-add).
        const updated = await updateDirectorLoan(companyId, periodId, editingId, payload);
        setRows((prev) => prev.map((r) => (r.id === editingId ? updated : r)));
        toast.success("Director loan updated");
      } else {
        const created = await createDirectorLoan(companyId, periodId, payload);
        const next = [...rows, created];
        setRows(next);
        publishCount(next);
        toast.success("Director loan recorded");
      }
      setEditingId(null);
      setForm(emptyForm(directors[0]?.id ?? 0));
      onSaved?.();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to save director loan");
    } finally {
      setSaving(false);
    }
  }

  async function handleDelete(id: number) {
    setSaving(true);
    try {
      await deleteDirectorLoan(companyId, periodId, id);
      const next = rows.filter((r) => r.id !== id);
      setRows(next);
      publishCount(next);
      toast.success("Director loan removed");
      onSaved?.();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to remove director loan");
    } finally {
      setSaving(false);
    }
  }

  if (loading) {
    return <div className="py-6 flex justify-center"><Spinner size="sm" /></div>;
  }

  if (directors.length === 0) {
    return (
      <div className="rounded-lg bg-amber-50 dark:bg-amber-900/20 border border-amber-200 dark:border-amber-700 p-4 text-sm text-amber-800 dark:text-amber-300">
        Add a director to this company first (Officers, on the company page) — a director loan must be
        attributed to a named director.
      </div>
    );
  }

  return (
    <div>
      {rows.length > 0 && (
        <div className="space-y-2 mb-4">
          {rows.map((r) => (
            <div
              key={r.id}
              className="flex items-center justify-between rounded-lg border border-gray-200 dark:border-neutral-700 px-4 py-3 dark:bg-neutral-800/50"
            >
              <div>
                <p className="text-sm font-medium text-gray-900 dark:text-gray-100">{directorName(r.directorId)}</p>
                <div className="flex items-center gap-2 mt-0.5 flex-wrap">
                  <span className="text-xs text-gray-400 dark:text-gray-500">
                    Opening {formatCurrency(r.openingBalance)} · Max {formatCurrency(r.maxBalanceDuringYear)}
                  </span>
                  <Chip variant="soft" size="sm" color={r.isDocumented ? "success" : "warning"}>
                    {r.isDocumented ? "Documented" : "Undocumented"}
                  </Chip>
                </div>
              </div>
              <div className="flex items-center gap-3">
                <span className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                  {formatCurrency(r.closingBalance)}
                </span>
                {canWrite && (
                  <>
                    <button
                      type="button"
                      onClick={() => startEdit(r)}
                      className="text-gray-400 hover:text-emerald-600 dark:text-gray-500 dark:hover:text-emerald-400"
                      aria-label={`Edit director loan for ${directorName(r.directorId)}`}
                    >
                      <Pencil className="w-4 h-4" />
                    </button>
                    <button
                      type="button"
                      onClick={() => r.id && handleDelete(r.id)}
                      className="text-red-400 hover:text-red-600 dark:text-red-500 dark:hover:text-red-400"
                      aria-label={`Delete director loan for ${directorName(r.directorId)}`}
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
          Your role has read-only access to director loans.
        </p>
      ) : (
      <div className="space-y-3">
        <div className="grid grid-cols-12 gap-3 items-end">
          <div className="col-span-4">
            <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Director</label>
            <select
              className={inputClass}
              value={form.directorId || ""}
              onChange={(e) => setForm({ ...form, directorId: Number(e.target.value) })}
              aria-label="Director"
              title="Director"
            >
              {directors.map((d) => (
                <option key={d.id} value={d.id}>{d.name}</option>
              ))}
            </select>
          </div>
          <MoneyInput
            className="col-span-2"
            label="Opening Balance"
            ariaLabel="Opening balance"
            value={form.openingBalance}
            onValueChange={(value) => setForm({ ...form, openingBalance: value })}
          />
          <MoneyInput
            className="col-span-2"
            label="Advances"
            ariaLabel="Advances to director"
            value={form.advances}
            onValueChange={(value) => setForm({ ...form, advances: value })}
          />
          <MoneyInput
            className="col-span-2"
            label="Repayments"
            ariaLabel="Repayments by director"
            value={form.repayments}
            onValueChange={(value) => setForm({ ...form, repayments: value })}
          />
          <MoneyInput
            className="col-span-2"
            label="Max During Year"
            ariaLabel="Maximum balance during year"
            placeholder="auto"
            value={form.maxBalanceDuringYear}
            onValueChange={(value) => setForm({ ...form, maxBalanceDuringYear: value })}
          />
        </div>
        <div className="grid grid-cols-12 gap-3 items-end">
          <div className="col-span-3">
            <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Interest Rate %</label>
            <input
              type="number"
              className={inputClass}
              placeholder="0.0"
              value={form.interestRate || ""}
              onChange={(e) => setForm({ ...form, interestRate: Number(e.target.value) })}
              aria-label="Interest rate"
            />
          </div>
          <MoneyInput
            className="col-span-3"
            label="Interest Charged"
            ariaLabel="Interest charged"
            value={form.interestCharged}
            onValueChange={(value) => setForm({ ...form, interestCharged: value })}
          />
          <div className="col-span-3 flex items-center gap-2 pb-2">
            <input
              type="checkbox"
              id="director-loan-documented"
              checked={form.isDocumented}
              onChange={(e) => setForm({ ...form, isDocumented: e.target.checked })}
              className="rounded border-gray-300 dark:border-neutral-600 text-emerald-600 focus:ring-emerald-500"
            />
            <label htmlFor="director-loan-documented" className="text-xs font-medium text-gray-600 dark:text-gray-400">
              Documented terms
            </label>
          </div>
          <div className="col-span-3 flex items-center justify-end pb-1">
            <span className="text-xs text-gray-500 dark:text-gray-400">
              Closing:{" "}
              <span className="font-semibold text-gray-900 dark:text-gray-100">{formatCurrency(closingBalance)}</span>
            </span>
          </div>
        </div>
        <div className="flex items-center justify-end gap-2">
          {editingId != null && (
            <Button variant="ghost" size="sm" onPress={cancelEdit} isDisabled={saving}>
              <X className="w-4 h-4 mr-1" /> Cancel
            </Button>
          )}
          <Button variant="primary" size="sm" onPress={handleSubmit} isDisabled={saving}>
            {saving ? <Spinner size="sm" /> : editingId != null
              ? <>Save changes</>
              : <><Plus className="w-4 h-4 mr-1" /> Add Director Loan</>}
          </Button>
        </div>
      </div>
      )}
    </div>
  );
}
