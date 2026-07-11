"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { Chip, Spinner } from "@heroui/react";
import { Pencil, Trash2 } from "lucide-react";
import { toast } from "sonner";
import {
  createDirectorLoan,
  deleteDirectorLoan,
  getDirectorLoans,
  updateDirectorLoan,
  type DirectorLoanRow,
} from "@/lib/api";
import { ReadOnlyNotice } from "@/components/workbench";
import { DirectorLoanEvidenceForm, deriveDirectorLoanBalances } from "@/components/DirectorLoanEvidenceForm";
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

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat("en-IE", { style: "currency", currency: "EUR" }).format(amount);
}

function humanise(value: string): string {
  return value.replace(/([a-z])([A-Z0-9])/g, "$1 $2").replace(/Section(\d+)/, "Section $1");
}

export interface DirectorOption {
  id: number;
  name: string;
}

function emptyForm(directorId?: number): DirectorLoanRow {
  return {
    directorId,
    counterpartyType: "Director",
    counterpartyName: undefined,
    arrangementType: "Loan",
    arrangementDate: undefined,
    openingBalance: 0,
    advances: 0,
    repayments: 0,
    closingBalance: 0,
    termsStatus: "Unassessed",
    interestRate: 0,
    interestCharged: 0,
    allowanceMade: 0,
    section236PresumptionEvidenceReference: undefined,
    isDocumented: false,
    loanTerms: undefined,
    maxBalanceDuringYear: 0,
    complianceBasis: "Unassessed",
    relevantAssetsBasis: "Unassessed",
    relevantAssetsAmount: undefined,
    relevantAssetsAsOfDate: undefined,
    relevantAssetsReference: undefined,
    noPriorFinancialStatementsConfirmed: false,
    relevantAssetsFallReview: "Unassessed",
    relevantAssetsReductionAwarenessDate: undefined,
    termsAmendedDate: undefined,
    termsAmendmentEvidenceReference: undefined,
    exceptionEvidenceReference: undefined,
    sapDeclarationDate: undefined,
    sapResolutionDate: undefined,
    sapActivityStartDate: undefined,
    sapCroFilingDate: undefined,
    sapDeclarationReference: undefined,
    sapResolutionReference: undefined,
    sapCroFilingReference: undefined,
    sapDeclarationCoversSection203Matters: false,
    expenseIncurredDate: undefined,
    expenseDischargedDate: undefined,
    ordinaryCourseConfirmed: false,
    noMoreFavourableTermsConfirmed: false,
    reviewDecision: "Unreviewed",
    reviewNote: undefined,
    reviewedBy: undefined,
    reviewerRole: undefined,
    reviewedAtUtc: undefined,
    balanceMovements: [],
  };
}

export function DirectorLoansManager({
  companyId,
  periodId,
  directors,
  canWrite = true,
  onCountChange,
  onSaved,
  onResourceStateChange,
}: {
  companyId: number;
  periodId: number;
  directors: DirectorOption[];
  canWrite?: boolean;
  onCountChange?: (count: number) => void;
  onSaved?: () => void;
  onResourceStateChange?: (state: ResourceState) => void;
}) {
  const [rows, setRows] = useState<DirectorLoanRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [form, setForm] = useState<DirectorLoanRow>(() => emptyForm(directors[0]?.id));
  const [editingId, setEditingId] = useState<number | null>(null);
  const [saving, setSaving] = useState(false);
  const [resourceState, setResourceState] = useState<ResourceState>(INITIAL_RESOURCE_STATE);
  const { requestDestructiveAction, destructiveActionConfirmation } = useDestructiveActionConfirmation();

  const directorLoanFormDirty = useMemo(() => {
    const baseline = editingId == null
      ? emptyForm(directors[0]?.id)
      : rows.find((row) => row.id === editingId) ?? emptyForm(directors[0]?.id);
    return JSON.stringify(form) !== JSON.stringify(baseline);
  }, [directors, editingId, form, rows]);
  useUnsavedChanges(directorLoanFormDirty);

  useEffect(() => {
    onResourceStateChange?.(resourceState);
  }, [onResourceStateChange, resourceState]);

  const publishCount = useCallback(
    (next: DirectorLoanRow[]) => onCountChange?.(next.length),
    [onCountChange],
  );

  const load = useCallback(async () => {
    setLoading(true);
    setResourceState((current) => beginResourceLoad(current, current.hasRetainedData));
    try {
      const data = await getDirectorLoans(companyId, periodId);
      setRows(data);
      publishCount(data);
      setResourceState(completeResourceLoad(data.length === 0));
    } catch (error) {
      const message = error instanceof Error ? error.message : "Failed to load director-loan evidence";
      setResourceState((current) => failResourceLoad({
        failedResourceKeys: ["director-loans"],
        errors: { "director-loans": message },
      }, current.hasRetainedData));
      toast.error(message);
    } finally {
      setLoading(false);
    }
  }, [companyId, periodId, publishCount]);

  useEffect(() => { void load(); }, [load]);

  const directorName = (id?: number) => directors.find((director) => director.id === id)?.name ?? (id ? `Director #${id}` : "No director");
  const counterpartyName = (row: DirectorLoanRow) => row.counterpartyName || directorName(row.directorId);

  function startEdit(row: DirectorLoanRow) {
    setEditingId(row.id ?? null);
    setForm({ ...row, balanceMovements: row.balanceMovements.map((movement) => ({ ...movement })) });
  }

  function resetForm() {
    setEditingId(null);
    setForm(emptyForm(directors[0]?.id));
  }

  async function handleSubmit() {
    if (form.counterpartyType !== "GroupCompany" && !form.directorId) {
      toast.error("Select the related director");
      return;
    }
    if (form.counterpartyType !== "Director" && !form.counterpartyName?.trim()) {
      toast.error("Enter the counterparty name");
      return;
    }
    if (form.balanceMovements.some((movement) => !movement.movementDate || movement.amount <= 0)) {
      toast.error("Every dated movement needs a date and positive amount");
      return;
    }

    const derived = deriveDirectorLoanBalances(form.openingBalance, form.balanceMovements);
    if (derived.closingBalance < 0) {
      toast.error("Repayments cannot reduce the director-loan balance below zero");
      return;
    }
    const payload: DirectorLoanRow = {
      ...form,
      directorId: form.counterpartyType === "GroupCompany" ? undefined : form.directorId,
      counterpartyName: form.counterpartyName?.trim() || undefined,
      advances: derived.advances,
      repayments: derived.repayments,
      closingBalance: derived.closingBalance,
      maxBalanceDuringYear: derived.maximumBalance,
      isDocumented: form.termsStatus.startsWith("Written"),
      loanTerms: form.loanTerms?.trim() || undefined,
      reviewNote: form.reviewNote?.trim() || undefined,
      balanceMovements: form.balanceMovements.map((movement) => ({
        ...movement,
        evidenceReference: movement.evidenceReference?.trim() || undefined,
      })),
    };

    setSaving(true);
    try {
      if (editingId != null) {
        const updated = await updateDirectorLoan(companyId, periodId, editingId, payload);
        setRows((current) => current.map((row) => (row.id === editingId ? updated : row)));
        toast.success("Director-loan evidence updated");
      } else {
        const created = await createDirectorLoan(companyId, periodId, payload);
        const next = [...rows, created];
        setRows(next);
        setResourceState(completeResourceLoad(false));
        publishCount(next);
        toast.success("Director-loan evidence recorded");
      }
      resetForm();
      onSaved?.();
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "Failed to save director-loan evidence");
    } finally {
      setSaving(false);
    }
  }

  async function handleDelete(id: number) {
    setSaving(true);
    try {
      await deleteDirectorLoan(companyId, periodId, id);
      const next = rows.filter((row) => row.id !== id);
      setRows(next);
      setResourceState(completeResourceLoad(next.length === 0));
      publishCount(next);
      toast.success("Director-loan evidence removed");
      if (editingId === id) resetForm();
      onSaved?.();
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "Failed to remove director-loan evidence");
      throw error;
    } finally {
      setSaving(false);
    }
  }

  if (loading && !resourceState.hasRetainedData) {
    return <div className="flex justify-center py-6"><Spinner size="sm" /></div>;
  }
  if (resourceState.status === "error" && !resourceState.hasRetainedData) {
    return <ResourceStateNotice state={resourceState} label="director-loan evidence" onRetry={load} compact />;
  }

  return (
    <div className="space-y-4">
      <ResourceStateNotice state={resourceState} label="director-loan evidence" onRetry={load} compact />

      {rows.length > 0 && (
        <div className="space-y-2" aria-label="Retained director-loan arrangements">
          {rows.map((row) => (
            <article key={row.id} className="rounded-lg border border-gray-200 p-4 dark:border-neutral-700 dark:bg-neutral-800/50">
              <div className="flex flex-col justify-between gap-3 sm:flex-row sm:items-center">
                <div className="min-w-0">
                  <p className="font-medium text-gray-900 dark:text-gray-100">{counterpartyName(row)}</p>
                  <div className="mt-1 flex flex-wrap items-center gap-2 text-xs text-gray-500 dark:text-gray-400">
                    <span>{humanise(row.arrangementType)}</span>
                    <span>Maximum {formatCurrency(row.maxBalanceDuringYear)}</span>
                    <span>Closing {formatCurrency(row.closingBalance)}</span>
                    <Chip variant="soft" size="sm" color={row.reviewDecision === "Accepted" ? "success" : row.reviewDecision === "RemediationRequired" ? "danger" : "warning"}>
                      {humanise(row.reviewDecision)}
                    </Chip>
                    <Chip variant="soft" size="sm">{humanise(row.complianceBasis)}</Chip>
                  </div>
                </div>
                {canWrite && canUseResourceAsEvidence(resourceState) && row.id && (
                  <div className="flex shrink-0 items-center gap-3">
                    <button type="button" onClick={() => startEdit(row)} className="text-[var(--muted-foreground)] hover:text-[var(--accent)]" aria-label={`Edit director-loan evidence for ${counterpartyName(row)}`}>
                      <Pencil className="h-4 w-4" />
                    </button>
                    <button type="button" onClick={() => requestDestructiveAction({
                      recordLabel: `director-loan evidence for ${counterpartyName(row)}`,
                      consequence: "This permanently removes the retained arrangement, balance movements, compliance review and linked evidence from this period. The removal cannot be undone.",
                      onConfirm: () => handleDelete(row.id!),
                      successAnnouncement: `Director-loan evidence for ${counterpartyName(row)} was removed.`,
                    })} className="text-red-500 hover:text-red-700" aria-label={`Delete director-loan evidence for ${counterpartyName(row)}`}>
                      <Trash2 className="h-4 w-4" />
                    </button>
                  </div>
                )}
              </div>
            </article>
          ))}
        </div>
      )}

      {!canWrite ? (
        <ReadOnlyNotice subject="director-loan evidence" />
      ) : !canUseResourceAsEvidence(resourceState) ? (
        <p className="text-sm text-amber-700 dark:text-amber-300">Editing is disabled until the failed evidence refresh succeeds.</p>
      ) : (
        <>
          {directors.length === 0 && form.counterpartyType !== "GroupCompany" && (
            <p className="rounded-lg border border-amber-200 bg-amber-50 p-4 text-sm text-amber-800 dark:border-amber-700 dark:bg-amber-900/20 dark:text-amber-300">
              Add a director with a verified appointment date before recording an individual or connected-person arrangement, or select “Group company” for a section 243 arrangement.
            </p>
          )}
          <DirectorLoanEvidenceForm
            form={form}
            directors={directors}
            editing={editingId != null}
            saving={saving}
            onChange={setForm}
            onCancel={editingId != null ? resetForm : undefined}
            onSubmit={() => void handleSubmit()}
          />
        </>
      )}
      {destructiveActionConfirmation}
    </div>
  );
}
