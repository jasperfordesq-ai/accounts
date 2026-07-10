"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { Button, Chip, Spinner } from "@heroui/react";
import { Pencil, Plus, Trash2, X } from "lucide-react";
import { toast } from "sonner";
import { getLoans, createLoan, updateLoan, deleteLoan, type Loan } from "@/lib/api";
import { MoneyInput, ReadOnlyNotice } from "@/components/workbench";
import { ResourceStateNotice } from "@/components/ResourceStateNotice";
import {
  INITIAL_RESOURCE_STATE,
  beginResourceLoad,
  canUseResourceAsEvidence,
  completeResourceLoad,
  failResourceLoad,
  type ResourceState,
} from "@/lib/resourceState";
import { useUnsavedChanges } from "@/lib/useUnsavedChanges";
import { useDestructiveActionConfirmation } from "@/lib/useDestructiveAction";

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
  onResourceStateChange,
}: {
  companyId: number;
  periodEnd?: string;
  canWrite?: boolean;
  onCountChange?: (count: number) => void;
  onResourceStateChange?: (state: ResourceState) => void;
}) {
  const [loans, setLoans] = useState<Loan[]>([]);
  const [loading, setLoading] = useState(true);
  const [form, setForm] = useState<Loan>(() => emptyForm(periodEnd));
  const [editingId, setEditingId] = useState<number | null>(null);
  const [saving, setSaving] = useState(false);
  const [resourceState, setResourceState] = useState<ResourceState>(INITIAL_RESOURCE_STATE);
  const { requestDestructiveAction, destructiveActionConfirmation } = useDestructiveActionConfirmation();

  const loanFormDirty = useMemo(() => {
    const baseline = editingId == null
      ? emptyForm(periodEnd)
      : loans.find((loan) => loan.id === editingId) ?? emptyForm(periodEnd);
    return form.lender !== baseline.lender
      || form.originalAmount !== baseline.originalAmount
      || form.balance !== baseline.balance
      || (form.drawdownDate ?? "") !== (baseline.drawdownDate ?? "")
      || (form.balanceAsOfDate ?? "") !== (baseline.balanceAsOfDate ?? "")
      || form.interestRate !== baseline.interestRate
      || form.isDirectorLoan !== baseline.isDirectorLoan
      || form.dueWithinYear !== baseline.dueWithinYear;
  }, [editingId, form, loans, periodEnd]);
  useUnsavedChanges(loanFormDirty);

  useEffect(() => {
    onResourceStateChange?.(resourceState);
  }, [onResourceStateChange, resourceState]);

  const publishCount = useCallback(
    (next: Loan[]) => onCountChange?.(next.length),
    [onCountChange],
  );

  const load = useCallback(async () => {
    setLoading(true);
    setResourceState((current) => beginResourceLoad(current, current.hasRetainedData));
    try {
      const data = await getLoans(companyId);
      setLoans(data);
      publishCount(data);
      setResourceState(completeResourceLoad(data.length === 0));
    } catch (err) {
      const message = err instanceof Error ? err.message : "Failed to load loans";
      setResourceState((current) => failResourceLoad({
        failedResourceKeys: ["loans"],
        errors: { loans: message },
      }, current.hasRetainedData));
      toast.error(message);
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
        setResourceState(completeResourceLoad(false));
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
      setResourceState(completeResourceLoad(next.length === 0));
      publishCount(next);
      toast.success("Loan removed");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to remove loan");
      throw err;
    } finally {
      setSaving(false);
    }
  }

  if (loading && !resourceState.hasRetainedData) {
    return <div className="py-6 flex justify-center"><Spinner size="sm" /></div>;
  }

  if (resourceState.status === "error" && !resourceState.hasRetainedData) {
    return <ResourceStateNotice state={resourceState} label="loan evidence" onRetry={load} compact />;
  }

  return (
    <div className="space-y-3">
      <ResourceStateNotice state={resourceState} label="loan evidence" onRetry={load} compact />
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
                {canWrite && canUseResourceAsEvidence(resourceState) && (
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
                      onClick={() => l.id && requestDestructiveAction({
                        recordLabel: `loan from ${l.lender}`,
                        consequence: `This permanently removes the ${formatCurrency(l.balance)} closing loan balance and its retained terms from the company record. The removal cannot be undone.`,
                        onConfirm: () => handleDelete(l.id!),
                        successAnnouncement: `Loan from ${l.lender} was removed.`,
                      })}
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
        <ReadOnlyNotice subject="loans" />
      ) : !canUseResourceAsEvidence(resourceState) ? (
        <p className="text-sm text-amber-700 dark:text-amber-300">
          Loan editing is disabled until the failed evidence refresh succeeds.
        </p>
      ) : (
      <div className="space-y-3">
        <div className="mobile-form-grid grid grid-cols-12 gap-3 items-end">
          <div className="col-span-4">
            <label htmlFor="loan-lender" className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Lender</label>
            <input
              id="loan-lender"
              type="text"
              className={inputClass}
              placeholder="e.g. Bank of Ireland"
              value={form.lender}
              onChange={(e) => setForm({ ...form, lender: e.target.value })}
              aria-label="Lender"
            />
          </div>
          <MoneyInput
            className="col-span-3"
            label="Original Amount"
            ariaLabel="Original amount"
            value={form.originalAmount}
            onValueChange={(value) => setForm({ ...form, originalAmount: value })}
          />
          <MoneyInput
            className="col-span-3"
            label="Balance Outstanding"
            ariaLabel="Balance outstanding"
            value={form.balance}
            onValueChange={(value) => setForm({ ...form, balance: value })}
          />
          <div className="col-span-2">
            <label htmlFor="loan-interest-rate" className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Interest %</label>
            <input
              id="loan-interest-rate"
              type="number"
              className={inputClass}
              placeholder="0.0"
              value={form.interestRate || ""}
              onChange={(e) => setForm({ ...form, interestRate: Number(e.target.value) })}
              aria-label="Interest rate"
            />
          </div>
        </div>
        <div className="mobile-form-grid grid grid-cols-12 gap-3 items-end">
          <div className="col-span-3">
            <label htmlFor="loan-drawdown-date" className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Drawdown Date</label>
            <input
              id="loan-drawdown-date"
              type="date"
              className={inputClass}
              value={form.drawdownDate ?? ""}
              onChange={(e) => setForm({ ...form, drawdownDate: e.target.value })}
              aria-label="Drawdown date"
            />
          </div>
          <div className="col-span-3">
            <label htmlFor="loan-balance-as-of-date" className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Balance As-Of Date</label>
            <input
              id="loan-balance-as-of-date"
              type="date"
              className={inputClass}
              value={form.balanceAsOfDate ?? ""}
              onChange={(e) => setForm({ ...form, balanceAsOfDate: e.target.value })}
              aria-label="Balance as-of date"
            />
          </div>
          <MoneyInput
            className="col-span-3"
            label="Due Within 1 Year"
            ariaLabel="Amount due within one year"
            value={form.dueWithinYear}
            onValueChange={(value) => setForm({ ...form, dueWithinYear: value })}
          />
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
            <Button variant="primary" size="sm" aria-label={editingId != null ? "Save changes to loan" : "Add Loan"} onPress={handleSubmit} isDisabled={saving}>
              {saving ? <Spinner size="sm" /> : editingId != null
                ? <>Save changes</>
                : <><Plus className="w-4 h-4 mr-1" /> Add Loan</>}
            </Button>
          </div>
        </div>
      </div>
      )}
      {destructiveActionConfirmation}
    </div>
  );
}
