"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Button, Spinner } from "@heroui/react";
import { AlertTriangle, CheckCircle2, RefreshCw, RotateCcw, ShieldQuestion, XCircle } from "lucide-react";
import { toast } from "sonner";
import { ConfirmModal } from "@/components/ConfirmModal";
import {
  decideDuplicateCandidate,
  decideDuplicateBatch,
  getDuplicateReviewQueue,
  type DuplicateCandidateReview,
  type DuplicateExactReimportBatchReview,
  type DuplicateReviewDecision,
  type DuplicateReviewQueue,
} from "@/lib/api";
import { ReviewPanel, StatusBadge } from "@/components/workbench";

export function DuplicateReviewPanel({
  companyId,
  periodId,
  canWrite,
  periodLocked = false,
  refreshToken,
  onDecisionRecorded,
}: {
  companyId: number;
  periodId: number;
  canWrite: boolean;
  periodLocked?: boolean;
  refreshToken?: object | null;
  onDecisionRecorded?: () => void | Promise<void>;
}) {
  const [queue, setQueue] = useState<DuplicateReviewQueue | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [reasons, setReasons] = useState<Record<number, string>>({});
  const [savingId, setSavingId] = useState<number | null>(null);
  const [batchReasons, setBatchReasons] = useState<Record<number, string>>({});
  const [savingBatchId, setSavingBatchId] = useState<number | null>(null);
  const [confirmBatch, setConfirmBatch] = useState<{ batch: DuplicateExactReimportBatchReview; decision: DuplicateReviewDecision } | null>(null);
  const [confirmDiscard, setConfirmDiscard] = useState<DuplicateCandidateReview | null>(null);
  const [page, setPage] = useState(1);
  const [batchPage, setBatchPage] = useState(1);
  const [liveMessage, setLiveMessage] = useState("");
  const previousRefreshToken = useRef(refreshToken);
  const loadGeneration = useRef(0);

  const load = useCallback(async () => {
    const generation = ++loadGeneration.current;
    setLoading(true);
    setError(null);
    try {
      const result = await getDuplicateReviewQueue(companyId, periodId, page, 50, batchPage, 10);
      if (generation !== loadGeneration.current) return;
      setQueue(result);
      if (result.page !== page) setPage(result.page);
      if (result.exactReimportBatchPage !== batchPage) setBatchPage(result.exactReimportBatchPage);
    } catch (loadError) {
      if (generation !== loadGeneration.current) return;
      setError(loadError instanceof Error ? loadError.message : "Failed to load duplicate review evidence");
    } finally {
      if (generation === loadGeneration.current) setLoading(false);
    }
  }, [batchPage, companyId, page, periodId]);

  useEffect(() => { void load(); }, [load]);
  useEffect(() => {
    if (previousRefreshToken.current === refreshToken) return;
    previousRefreshToken.current = refreshToken;
    if (page === 1 && batchPage === 1) void load();
    else {
      setPage(1);
      setBatchPage(1);
    }
  }, [batchPage, load, page, refreshToken]);

  const ordered = useMemo(() => [...(queue?.items ?? [])].sort((left, right) => {
    if (isUnresolved(left) && !isUnresolved(right)) return -1;
    if (!isUnresolved(left) && isUnresolved(right)) return 1;
    return right.date.localeCompare(left.date) || right.transactionId - left.transactionId;
  }), [queue]);

  async function decide(candidate: DuplicateCandidateReview, decision: DuplicateReviewDecision) {
    const reason = (reasons[candidate.transactionId] ?? "").trim();
    if (Array.from(reason).length < 20) {
      toast.error("Give a specific duplicate decision reason of at least 20 characters");
      return;
    }
    setSavingId(candidate.transactionId);
    try {
      await decideDuplicateCandidate(
        companyId,
        periodId,
        candidate.transactionId,
        decision,
        reason,
        candidate.status,
        candidate.decisionVersion,
      );
      toast.success(decision === "Pending" ? "Duplicate decision reopened" : `Transaction ${decision.toLowerCase()} with audit evidence`);
      setLiveMessage(decision === "Pending" ? `Transaction ${candidate.transactionId} decision reopened.` : `Transaction ${candidate.transactionId} ${decision.toLowerCase()}.`);
      setReasons((current) => ({ ...current, [candidate.transactionId]: "" }));
      setConfirmDiscard(null);
      await load();
      await onDecisionRecorded?.();
    } catch (saveError) {
      toast.error(saveError instanceof Error ? saveError.message : "Failed to record duplicate review decision");
      setConfirmDiscard(null);
      await load();
      setLiveMessage("The review queue was reloaded. Check the latest decision before retrying.");
    } finally {
      setSavingId(null);
    }
  }

  async function decideBatch(batch: DuplicateExactReimportBatchReview, decision: DuplicateReviewDecision) {
    const reason = (batchReasons[batch.importBatchId] ?? "").trim();
    if (Array.from(reason).length < 20) {
      toast.error("Give a specific batch decision reason of at least 20 characters");
      return;
    }
    setSavingBatchId(batch.importBatchId);
    try {
      const result = await decideDuplicateBatch(companyId, periodId, batch, decision, reason);
      toast.success(`${result.updatedCount} exact re-import rows ${decision === "Pending" ? "reopened" : decision.toLowerCase()}`);
      setLiveMessage(`Batch ${batch.importBatchId}: ${result.updatedCount} rows ${decision === "Pending" ? "reopened" : decision.toLowerCase()}.`);
      setBatchReasons((current) => ({ ...current, [batch.importBatchId]: "" }));
      setConfirmBatch(null);
      await load();
      await onDecisionRecorded?.();
    } catch (saveError) {
      toast.error(saveError instanceof Error ? saveError.message : "Failed to record exact re-import batch decision");
      setConfirmBatch(null);
      await load();
      setLiveMessage("The exact re-import batch list was reloaded. Check the latest state before retrying.");
    } finally {
      setSavingBatchId(null);
    }
  }

  function requestBatchDecision(batch: DuplicateExactReimportBatchReview, decision: DuplicateReviewDecision) {
    const reason = (batchReasons[batch.importBatchId] ?? "").trim();
    if (Array.from(reason).length < 20) {
      toast.error("Give a specific batch decision reason of at least 20 characters");
      return;
    }
    setConfirmBatch({ batch, decision });
  }

  function requestDecision(candidate: DuplicateCandidateReview, decision: DuplicateReviewDecision) {
    if (decision === "Discarded") {
      const reason = (reasons[candidate.transactionId] ?? "").trim();
      if (Array.from(reason).length < 20) {
        toast.error("Give a specific duplicate decision reason of at least 20 characters");
        return;
      }
      setConfirmDiscard(candidate);
      return;
    }
    void decide(candidate, decision);
  }

  return (
    <ReviewPanel
      title="Duplicate import review"
      description="Candidate matches are retained as source rows until a reviewer explicitly keeps or discards them. Decisions remain reversible before finalisation."
      actions={<div className="flex items-center gap-2"><StatusBadge tone={(queue?.pendingCount ?? 0) > 0 ? "warn" : "good"}>{queue?.pendingCount ?? 0} pending</StatusBadge><Button size="sm" variant="outline" onPress={() => void load()} aria-label="Refresh duplicate review queue"><RefreshCw className="h-4 w-4" /> Refresh</Button></div>}
    >
      <div className="space-y-4">
        <p className="sr-only" aria-live="polite">{liveMessage}</p>
        <div className="rounded-md border border-blue-200 bg-blue-50 p-3 text-xs leading-5 text-blue-900 dark:border-blue-900 dark:bg-blue-950/30 dark:text-blue-100">
          New pending candidates remain provisionally included and block finalisation; they are never silently deleted. A legacy exclusion from an already locked period stays visibly quarantined to preserve signed figures until the period is reopened and explicitly reviewed.
        </div>

        {periodLocked && (
          <div className="rounded-md border border-amber-300 bg-amber-50 p-3 text-xs text-amber-950 dark:border-amber-800 dark:bg-amber-950/30 dark:text-amber-100">
            This period is locked. Reopen it before retaining, discarding, or reopening duplicate decisions. The evidence remains available read-only.
          </div>
        )}

        {loading ? (
          <div className="flex items-center gap-2 py-3 text-sm text-[var(--muted-foreground)]"><Spinner size="sm" /> Loading duplicate candidates</div>
        ) : error ? (
          <div className="flex items-center justify-between gap-3 rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-900 dark:border-red-900 dark:bg-red-950/30 dark:text-red-100">
            <span>{error}</span><Button size="sm" variant="outline" onPress={() => void load()}>Retry</Button>
          </div>
        ) : ordered.length === 0 && (queue?.exactReimportBatches.length ?? 0) === 0 ? (
          <div className="flex items-center gap-2 rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-4 text-sm text-[var(--muted-foreground)]">
            <CheckCircle2 className="h-4 w-4 text-emerald-600" /> No duplicate candidates are waiting or retained in this period.
          </div>
        ) : (
          <div className="space-y-3">
            {(queue?.exactReimportBatches.length ?? 0) > 0 && (
              <section className="space-y-3 rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3" aria-label="Exact re-import batch decisions">
                <div>
                  <h3 className="text-sm font-semibold text-[var(--foreground)]">Exact statement re-imports</h3>
                  <p className="mt-1 text-xs text-[var(--muted-foreground)]">Resolve or reopen an entire byte-identical statement atomically. Every row keeps named decision evidence and the audit log retains the exact row manifest digest.</p>
                </div>
                {queue?.exactReimportBatches.map((batch) => (
                  <ExactReimportBatchCard
                    key={batch.importBatchId}
                    batch={batch}
                    canWrite={canWrite && !periodLocked}
                    reason={batchReasons[batch.importBatchId] ?? ""}
                    saving={savingBatchId === batch.importBatchId}
                    onReasonChange={(value) => setBatchReasons((current) => ({ ...current, [batch.importBatchId]: value }))}
                    onDecide={(decision) => requestBatchDecision(batch, decision)}
                  />
                ))}
                {(queue?.exactReimportBatchTotalPages ?? 1) > 1 && (
                  <div className="flex flex-wrap items-center justify-between gap-3 border-t border-[var(--border)] pt-3">
                    <p className="text-xs text-[var(--muted-foreground)]">Batch page {queue?.exactReimportBatchPage ?? 1} of {queue?.exactReimportBatchTotalPages ?? 1} · {queue?.exactReimportBatchTotal ?? 0} batches</p>
                    <div className="flex gap-2">
                      <Button size="sm" variant="outline" isDisabled={(queue?.exactReimportBatchPage ?? 1) <= 1 || loading} onPress={() => setBatchPage((current) => Math.max(1, current - 1))}>Previous batches</Button>
                      <Button size="sm" variant="outline" isDisabled={(queue?.exactReimportBatchPage ?? 1) >= (queue?.exactReimportBatchTotalPages ?? 1) || loading} onPress={() => setBatchPage((current) => current + 1)}>Next batches</Button>
                    </div>
                  </div>
                )}
              </section>
            )}
            <div className="grid gap-3 sm:grid-cols-3">
              <QueueMetric label="Pending review" value={queue?.pendingCount ?? 0} tone="warn" />
              <QueueMetric label="Explicitly retained" value={queue?.retainedCount ?? 0} tone="good" />
              <QueueMetric label="Explicitly discarded" value={queue?.discardedCount ?? 0} tone="bad" />
            </div>
            {ordered.map((candidate) => (
              <CandidateCard
                key={candidate.transactionId}
                candidate={candidate}
                canWrite={canWrite && !periodLocked}
                reason={reasons[candidate.transactionId] ?? ""}
                saving={savingId === candidate.transactionId}
                onReasonChange={(value) => setReasons((current) => ({ ...current, [candidate.transactionId]: value }))}
                onDecide={(decision) => requestDecision(candidate, decision)}
              />
            ))}
            {(queue?.totalPages ?? 1) > 1 && (
              <div className="flex flex-wrap items-center justify-between gap-3 border-t border-[var(--border)] pt-3">
                <p className="text-xs text-[var(--muted-foreground)]">Page {queue?.page ?? 1} of {queue?.totalPages ?? 1} · {queue?.total ?? 0} candidates</p>
                <div className="flex gap-2">
                  <Button size="sm" variant="outline" isDisabled={(queue?.page ?? 1) <= 1 || loading} onPress={() => setPage((current) => Math.max(1, current - 1))}>Previous</Button>
                  <Button size="sm" variant="outline" isDisabled={(queue?.page ?? 1) >= (queue?.totalPages ?? 1) || loading} onPress={() => setPage((current) => current + 1)}>Next</Button>
                </div>
              </div>
            )}
          </div>
        )}
      </div>
      <ConfirmModal
        open={confirmDiscard !== null}
        title="Discard this row from the ledger?"
        description="The imported source row and its audit evidence will remain retained. This decision only excludes the row from accounting and can be reopened before finalisation."
        confirmLabel="Discard from ledger"
        variant="danger"
        dialogRole="alertdialog"
        loading={confirmDiscard !== null && savingId === confirmDiscard.transactionId}
        onConfirm={() => { if (confirmDiscard) void decide(confirmDiscard, "Discarded"); }}
        onCancel={() => { if (savingId === null) setConfirmDiscard(null); }}
      />
      <ConfirmModal
        open={confirmBatch !== null}
        title={confirmBatch?.decision === "Pending" ? "Reopen this exact re-import batch?" : confirmBatch?.decision === "Discarded" ? "Discard this exact re-import batch?" : "Keep this exact re-import batch?"}
        description={confirmBatch ? `This atomic decision affects all ${confirmBatch.batch.candidateCount.toLocaleString("en-IE")} retained rows in batch #${confirmBatch.batch.importBatchId}. Source evidence remains immutable and the action can be reopened before finalisation.` : ""}
        confirmLabel={confirmBatch?.decision === "Pending" ? "Reopen batch" : confirmBatch?.decision === "Discarded" ? "Discard batch rows" : "Keep batch rows"}
        variant={confirmBatch?.decision === "Discarded" ? "danger" : "default"}
        dialogRole="alertdialog"
        loading={confirmBatch !== null && savingBatchId === confirmBatch.batch.importBatchId}
        onConfirm={() => { if (confirmBatch) void decideBatch(confirmBatch.batch, confirmBatch.decision); }}
        onCancel={() => { if (savingBatchId === null) setConfirmBatch(null); }}
      />
    </ReviewPanel>
  );
}

function CandidateCard({
  candidate,
  canWrite,
  reason,
  saving,
  onReasonChange,
  onDecide,
}: {
  candidate: DuplicateCandidateReview;
  canWrite: boolean;
  reason: string;
  saving: boolean;
  onReasonChange: (value: string) => void;
  onDecide: (decision: DuplicateReviewDecision) => void;
}) {
  const unresolved = isUnresolved(candidate);
  const tone = unresolved ? "warn" : candidate.status === "Retained" ? "good" : "bad";
  return (
    <article className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-4">
      <div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <StatusBadge tone={tone}>{candidate.status === "LegacyLockedUnverified" ? "Legacy locked review" : candidate.status}</StatusBadge>
            <StatusBadge tone={candidate.includedInLedger ? "good" : "bad"}>{candidate.includedInLedger ? "Included in ledger" : "Excluded from ledger"}</StatusBadge>
            <span className="text-xs font-semibold text-[var(--muted-foreground)]">{formatKind(candidate.candidateKind)} · {Math.round(candidate.confidence * 100)}% signal</span>
          </div>
          <p className="mt-2 break-words text-xs text-[var(--muted-foreground)]">
            {candidate.bankAccountName} ({candidate.currency}) · {candidate.importBatchId ? `batch #${candidate.importBatchId} · ` : ""}{candidate.sourceFilename} · {candidate.sourceRowNumber ? `source row ${candidate.sourceRowNumber}` : "legacy row number unavailable"} · transaction #{candidate.transactionId}
          </p>
          {(candidate.sourceFileSha256 || candidate.sourceRowSha256 || candidate.sourceImportedAtUtc) && (
            <p className="mt-1 break-words text-xs text-[var(--muted-foreground)]">
              {candidate.sourceImportedAtUtc ? `Imported ${formatTimestamp(candidate.sourceImportedAtUtc)} · ` : ""}
              {candidate.sourceFileSha256 ? <>file SHA-256 <HashValue value={candidate.sourceFileSha256} /> · </> : null}
              {candidate.sourceRowSha256 ? <>row SHA-256 <HashValue value={candidate.sourceRowSha256} /></> : null}
            </p>
          )}
        </div>
        {candidate.decidedByDisplayName && (
          <p className="text-xs text-[var(--muted-foreground)]">Decision v{candidate.decisionVersion} by {candidate.decidedByDisplayName}</p>
        )}
      </div>

      <div className="mt-3 grid gap-3 lg:grid-cols-2">
        <TransactionFacts title="Incoming retained row" currency={candidate.currency} date={candidate.date} description={candidate.description} amount={candidate.amount} balance={candidate.balance} reference={candidate.reference} />
        <TransactionFacts title={`Possible match${candidate.matchedTransactionId ? ` #${candidate.matchedTransactionId}` : ""}`} currency={candidate.matchedCurrency ?? candidate.currency} date={candidate.matchedDate} description={candidate.matchedDescription} amount={candidate.matchedAmount} balance={candidate.matchedBalance} reference={candidate.matchedReference} evidence={candidate.matchedSourceFilename ? `${candidate.matchedBankAccountName ?? candidate.bankAccountName} · ${candidate.matchedImportBatchId ? `batch #${candidate.matchedImportBatchId} · ` : ""}${candidate.matchedSourceFilename}${candidate.matchedSourceRowNumber ? ` · row ${candidate.matchedSourceRowNumber}` : ""}` : undefined} fileHash={candidate.matchedSourceFileSha256} rowHash={candidate.matchedSourceRowSha256} importedAt={candidate.matchedSourceImportedAtUtc} />
      </div>

      <ul className="mt-3 space-y-1 text-xs text-[var(--muted-foreground)]">
        {candidate.reasons.map((item) => <li key={item}>• {item}</li>)}
      </ul>

      {candidate.status === "LegacyLockedUnverified" && (
        <div className="mt-3 rounded border border-amber-300 bg-amber-50 px-3 py-2 text-xs text-amber-950 dark:border-amber-800 dark:bg-amber-950/30 dark:text-amber-100">
          This unaudited legacy exclusion is preserved only to avoid changing locked accounts. Reopen the period, compare the retained bank evidence, and record a named retain or discard decision.
        </div>
      )}

      {candidate.decisionReason && (
        <div className="mt-3 rounded border border-[var(--border)] bg-[var(--surface-subtle)] px-3 py-2 text-xs text-[var(--foreground)]">
          Current decision reason: {candidate.decisionReason}
        </div>
      )}

      {candidate.batchDecisionAvailable && (
        <div className="mt-3 rounded border border-blue-200 bg-blue-50 px-3 py-2 text-xs text-blue-950 dark:border-blue-800 dark:bg-blue-950/30 dark:text-blue-100">
          This row belongs to an eligible exact re-import batch. Use the atomic batch card above so one row cannot strand the rest of the statement in manual review.
        </div>
      )}

      {canWrite && !candidate.batchDecisionAvailable && (
        <div className="mt-4 space-y-2 border-t border-[var(--border)] pt-3">
          <label className="block text-xs font-semibold text-[var(--foreground)]" htmlFor={`duplicate-reason-${candidate.transactionId}`}>
            {unresolved ? "Reviewer decision reason" : "Reason for changing this decision"}
            <textarea id={`duplicate-reason-${candidate.transactionId}`} aria-label={`${unresolved ? "Reviewer decision reason" : "Reason for changing this decision"} for transaction ${candidate.transactionId}`} aria-describedby={`duplicate-reason-help-${candidate.transactionId}`} value={reason} onChange={(event) => onReasonChange(event.target.value)} rows={2} maxLength={1000} className="mt-1.5 w-full resize-y rounded-md border border-[var(--control-border)] bg-[var(--surface)] px-3 py-2 text-sm font-normal text-[var(--foreground)]" placeholder="Explain the bank-statement evidence and why the incoming row should be retained or excluded." />
          </label>
          <p id={`duplicate-reason-help-${candidate.transactionId}`} className="text-xs text-[var(--muted-foreground)]">{Array.from(reason.trim()).length}/1000 characters · minimum 20</p>
          <div className="flex flex-wrap justify-end gap-2">
            {unresolved ? (
              <>
                <Button size="sm" variant="outline" aria-label={`Retain incoming transaction ${candidate.transactionId}; possible match is unchanged`} isDisabled={saving} onPress={() => onDecide("Retained")}><CheckCircle2 className="h-4 w-4" /> Retain incoming row</Button>
                <Button size="sm" variant="danger" aria-label={`Discard transaction ${candidate.transactionId} from the ledger`} isDisabled={saving} onPress={() => onDecide("Discarded")}><XCircle className="h-4 w-4" /> Discard from ledger</Button>
              </>
            ) : (
              <Button size="sm" variant="outline" aria-label={`Reopen duplicate decision for transaction ${candidate.transactionId}`} isDisabled={saving} onPress={() => onDecide("Pending")}><RotateCcw className="h-4 w-4" /> Reopen decision</Button>
            )}
            {saving && <Spinner size="sm" />}
          </div>
        </div>
      )}
    </article>
  );
}

function ExactReimportBatchCard({
  batch,
  canWrite,
  reason,
  saving,
  onReasonChange,
  onDecide,
}: {
  batch: DuplicateExactReimportBatchReview;
  canWrite: boolean;
  reason: string;
  saving: boolean;
  onReasonChange: (value: string) => void;
  onDecide: (decision: DuplicateReviewDecision) => void;
}) {
  const pending = batch.currentStatus === "Pending";
  return (
    <article className="min-w-0 rounded-md border border-[var(--border)] bg-[var(--surface)] p-3">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2"><StatusBadge tone={pending ? "warn" : batch.currentStatus === "Retained" ? "good" : "bad"}>{batch.currentStatus}</StatusBadge><span className="text-xs font-semibold">{batch.candidateCount.toLocaleString("en-IE")} exact-match rows</span></div>
          <p className="mt-1 break-words text-xs text-[var(--muted-foreground)]">{batch.bankAccountName} ({batch.currency}) · batch #{batch.importBatchId} · {batch.sourceFilename} · {formatTimestamp(batch.importedAtUtc)}</p>
          <p className="mt-1 break-words text-xs text-[var(--muted-foreground)]">File SHA-256 <HashValue value={batch.sourceFileSha256} /> · decision token <HashValue value={batch.decisionToken} /></p>
        </div>
      </div>
      {canWrite && (
        <div className="mt-3 space-y-2 border-t border-[var(--border)] pt-3">
          <label htmlFor={`duplicate-batch-reason-${batch.importBatchId}`} className="block text-xs font-semibold">{pending ? "Batch decision reason" : "Reason for reopening this batch"}</label>
          <textarea id={`duplicate-batch-reason-${batch.importBatchId}`} aria-label={`${pending ? "Batch decision reason" : "Reason for reopening this batch"} for batch ${batch.importBatchId}`} aria-describedby={`duplicate-batch-help-${batch.importBatchId}`} value={reason} onChange={(event) => onReasonChange(event.target.value)} rows={2} maxLength={1000} className="w-full resize-y rounded-md border border-[var(--control-border)] bg-[var(--surface)] px-3 py-2 text-sm" placeholder="Explain why the whole source statement is an exact re-import and how it should be treated." />
          <p id={`duplicate-batch-help-${batch.importBatchId}`} className="text-xs text-[var(--muted-foreground)]">{Array.from(reason.trim()).length}/1000 characters · minimum 20</p>
          <div className="flex flex-wrap justify-end gap-2">
            {pending ? <><Button size="sm" variant="outline" aria-label={`Keep all ${batch.candidateCount} rows in exact re-import batch ${batch.importBatchId}`} isDisabled={saving} onPress={() => onDecide("Retained")}><CheckCircle2 className="h-4 w-4" /> Keep whole batch</Button><Button size="sm" variant="danger" aria-label={`Discard all ${batch.candidateCount} rows in exact re-import batch ${batch.importBatchId}`} isDisabled={saving} onPress={() => onDecide("Discarded")}><XCircle className="h-4 w-4" /> Discard whole batch</Button></> : <Button size="sm" variant="outline" aria-label={`Reopen exact re-import batch ${batch.importBatchId}`} isDisabled={saving} onPress={() => onDecide("Pending")}><RotateCcw className="h-4 w-4" /> Reopen whole batch</Button>}
            {saving && <Spinner size="sm" />}
          </div>
        </div>
      )}
    </article>
  );
}

function TransactionFacts({ title, currency, date, description, amount, balance, reference, evidence, fileHash, rowHash, importedAt }: { title: string; currency: string; date?: string; description?: string; amount?: number; balance?: number; reference?: string; evidence?: string; fileHash?: string; rowHash?: string; importedAt?: string }) {
  return (
    <div className="min-w-0 rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3 text-xs">
      <div className="flex items-center gap-2 font-semibold text-[var(--foreground)]"><ShieldQuestion className="h-3.5 w-3.5" />{title}</div>
      {date && description && amount != null ? <dl className="mt-2 grid grid-cols-[6rem_minmax(0,1fr)] gap-x-2 gap-y-1 text-[var(--muted-foreground)]">
        {evidence && <><dt>Source</dt><dd className="break-words text-[var(--foreground)]">{evidence}</dd></>}
        {importedAt && <><dt>Imported</dt><dd className="text-[var(--foreground)]">{formatTimestamp(importedAt)}</dd></>}
        {fileHash && <><dt>File hash</dt><dd className="min-w-0 break-all text-[var(--foreground)]"><HashValue value={fileHash} /></dd></>}
        {rowHash && <><dt>Row hash</dt><dd className="min-w-0 break-all text-[var(--foreground)]"><HashValue value={rowHash} /></dd></>}
        <dt>Date</dt><dd className="text-[var(--foreground)]">{formatDate(date)}</dd>
        <dt>Description</dt><dd className="break-words text-[var(--foreground)]">{description}</dd>
        <dt>Amount</dt><dd className="font-mono text-[var(--foreground)]">{formatMoney(amount, currency)}</dd>
        <dt>Balance</dt><dd className="font-mono text-[var(--foreground)]">{balance == null ? "Not supplied" : formatMoney(balance, currency)}</dd>
        <dt>Reference</dt><dd className="break-words text-[var(--foreground)]">{reference ?? "Not supplied"}</dd>
      </dl> : <p className="mt-2 text-[var(--muted-foreground)]"><AlertTriangle className="mr-1 inline h-3.5 w-3.5" />The matched source row is unavailable; manual review is required.</p>}
    </div>
  );
}

function HashValue({ value }: { value: string }) {
  return <span className="break-all font-mono">{value}</span>;
}

function QueueMetric({ label, value, tone }: { label: string; value: number; tone: "warn" | "good" | "bad" }) {
  const colour = tone === "warn" ? "text-amber-700 dark:text-amber-200" : tone === "good" ? "text-emerald-700 dark:text-emerald-200" : "text-red-700 dark:text-red-200";
  return <div className="rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3"><p className={`text-xl font-bold ${colour}`}>{value}</p><p className="text-xs text-[var(--muted-foreground)]">{label}</p></div>;
}

function formatKind(value: DuplicateCandidateReview["candidateKind"]) {
  return ({
    ExactSourceReimport: "Exact source re-import",
    ReferenceAndBalanceMatch: "Reference + balance match",
    ReferenceMatch: "Bank reference match",
    BalanceMatch: "Statement balance match",
    SameDateAmountDescription: "Same date, amount + description",
    LegacyUnverified: "Legacy unverified flag",
  } satisfies Record<DuplicateCandidateReview["candidateKind"], string>)[value];
}

function isUnresolved(candidate: DuplicateCandidateReview) {
  return candidate.status === "Pending" || candidate.status === "LegacyLockedUnverified";
}

function formatDate(value: string) { return new Date(value).toLocaleDateString("en-IE", { day: "2-digit", month: "short", year: "numeric" }); }
function formatTimestamp(value: string) { return new Date(value).toLocaleString("en-IE", { dateStyle: "medium", timeStyle: "short" }); }
function formatMoney(value: number, currency: string) { return new Intl.NumberFormat("en-IE", { style: "currency", currency }).format(value); }
