"use client";

import { useCallback, useEffect, useState } from "react";
import {
  fetchDocumentBlob,
  generateCroHandoffSnapshot,
  generateRevenueHandoffSnapshot,
  getExternalFilingHandoffArtifactUrl,
  getExternalFilingHandoffWorkspace,
  recordExternalFilingAuthority,
  recordExternalFilingOutcome,
  revokeExternalFilingAuthority,
} from "@/lib/api";
import type {
  CroHandoffSnapshotRequest,
  ExternalFilingAuthorityRequest,
  ExternalFilingHandoffWorkspace,
  ExternalFilingOutcomeRequest,
  ExternalFilingSnapshot,
  ExternalFilingWorkflow,
  RevenueHandoffSnapshotRequest,
} from "@/lib/externalFilingHandoff";
import { ExternalFilingHandoffWorkbench } from "@/components/period/ExternalFilingHandoffWorkbench";
import {
  AuthorityEvidenceForm,
  CroSnapshotForm,
  OutcomeEvidenceForm,
  RevenueSnapshotForm,
} from "@/components/period/ExternalFilingHandoffForms";
import { PermissionDeniedPanel, ReviewPanel, WorkbenchLoadingState } from "@/components/workbench";

const commandButtonClass = "inline-flex min-h-10 items-center justify-center rounded-md border border-[var(--control-border)] bg-[var(--surface)] px-4 py-2 text-sm font-semibold text-[var(--foreground)] hover:bg-[var(--surface-subtle)] disabled:cursor-not-allowed disabled:bg-[var(--muted-surface)] disabled:text-[var(--muted-foreground)]";

type Composer =
  | { kind: "authority" }
  | { kind: "cro"; predecessor?: ExternalFilingSnapshot }
  | { kind: "revenue"; predecessor?: ExternalFilingSnapshot }
  | { kind: "outcome"; snapshot: ExternalFilingSnapshot }
  | null;

export function ExternalFilingHandoffRuntime({
  companyId,
  periodId,
  canRead,
  canPrepare,
  canReview,
  onEvidenceChanged,
}: {
  companyId: number;
  periodId: number;
  canRead: boolean;
  canPrepare: boolean;
  canReview: boolean;
  onEvidenceChanged?: () => void | Promise<void>;
}) {
  const [workspace, setWorkspace] = useState<ExternalFilingHandoffWorkspace | null>(null);
  const [composer, setComposer] = useState<Composer>(null);
  const [loading, setLoading] = useState(canRead);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [status, setStatus] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    if (!canRead) return;
    setLoading(true);
    try {
      const next = await getExternalFilingHandoffWorkspace(companyId, periodId);
      setWorkspace(next);
      setError(null);
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "External filing handoff evidence could not be loaded.");
    } finally {
      setLoading(false);
    }
  }, [canRead, companyId, periodId]);

  useEffect(() => { void refresh(); }, [refresh]);

  const commit = useCallback(async (action: () => Promise<unknown>, successMessage: string) => {
    setBusy(true);
    setStatus(null);
    try {
      await action();
      await refresh();
      await onEvidenceChanged?.();
      setComposer(null);
      setStatus(successMessage);
    } catch (requestError) {
      const message = requestError instanceof Error ? requestError.message : "External filing evidence could not be retained.";
      setError(message);
      throw requestError;
    } finally {
      setBusy(false);
    }
  }, [onEvidenceChanged, refresh]);

  async function download(snapshot: ExternalFilingSnapshot) {
    setBusy(true);
    setError(null);
    try {
      const blob = await fetchDocumentBlob(getExternalFilingHandoffArtifactUrl(companyId, periodId, snapshot.document.snapshotId));
      const actual = await sha256(await blob.arrayBuffer());
      if (actual.toLowerCase() !== snapshot.artifactSha256.toLowerCase()) {
        throw new Error("Downloaded handoff bytes do not match the immutable snapshot SHA-256.");
      }
      const href = URL.createObjectURL(blob);
      try {
        const anchor = document.createElement("a");
        anchor.href = href;
        anchor.download = `external-filing-handoff-${snapshot.document.snapshotId}.json`;
        anchor.click();
      } finally {
        URL.revokeObjectURL(href);
      }
      setStatus(`Downloaded and verified ${snapshot.document.workflow} v${snapshot.document.version}.`);
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "The exact handoff artifact could not be downloaded.");
    } finally {
      setBusy(false);
    }
  }

  function prepare(workflow: ExternalFilingWorkflow) {
    setComposer(workflow === "CroB1" ? { kind: "cro" } : { kind: "revenue" });
  }

  function amend(snapshot: ExternalFilingSnapshot) {
    setComposer(snapshot.document.workflow === "CroB1"
      ? { kind: "cro", predecessor: snapshot }
      : { kind: "revenue", predecessor: snapshot });
  }

  if (!canRead) {
    return (
      <PermissionDeniedPanel
        title="Internal filing-handoff permission required"
        description="Authority evidence, immutable snapshots, and external outcome chronology are limited to Owner, Accountant, and Reviewer users."
      />
    );
  }

  if (loading && !workspace) return <WorkbenchLoadingState />;

  if (!workspace) {
    return (
      <ReviewPanel title="External filing handoff unavailable" description={error ?? "No retained handoff workspace was returned."}>
        <button type="button" className={commandButtonClass} onClick={() => void refresh()}>Retry handoff workspace</button>
      </ReviewPanel>
    );
  }

  return (
    <section aria-labelledby="external-filing-handoff-heading" className="space-y-5">
      <div className="flex flex-wrap items-start justify-between gap-3 rounded-lg border border-[var(--border)] bg-[var(--surface)] p-4">
        <div>
          <p className="text-xs font-semibold uppercase tracking-wide text-emerald-700 dark:text-emerald-300">Manual external handoff ledger</p>
          <h2 id="external-filing-handoff-heading" className="mt-1 text-lg font-semibold text-[var(--foreground)]">CRO / Revenue handoff workspace</h2>
          <p className="mt-1 max-w-3xl text-sm text-[var(--muted-foreground)]">Prepare exact append-only evidence for an authorised presenter or ROS agent. This platform cannot submit a return.</p>
        </div>
        <div className="flex flex-wrap gap-2">
          {canReview && <button type="button" className={commandButtonClass} disabled={busy} onClick={() => setComposer({ kind: "authority" })}>Record / revoke authority</button>}
          <button type="button" className={commandButtonClass} disabled={busy || loading} onClick={() => void refresh()}>Refresh ledger</button>
        </div>
      </div>

      <div aria-live="polite" aria-atomic="true">
        {status && <p role="status" className="rounded-md border border-emerald-300 bg-emerald-50 p-3 text-sm text-emerald-900 dark:border-emerald-900 dark:bg-emerald-950/30 dark:text-emerald-100">{status}</p>}
        {error && <p role="alert" className="rounded-md border border-red-300 bg-red-50 p-3 text-sm text-red-900 dark:border-red-900 dark:bg-red-950/30 dark:text-red-100">{error}</p>}
      </div>

      {composer?.kind === "authority" && canReview && (
        <AuthorityEvidenceForm
          workspace={workspace}
          busy={busy}
          onCancel={() => setComposer(null)}
          onSave={(request: ExternalFilingAuthorityRequest) => commit(
            () => recordExternalFilingAuthority(companyId, periodId, request),
            "Authority evidence version retained and hash-bound.",
          )}
          onRevoke={(authorityId, reason) => commit(
            () => revokeExternalFilingAuthority(companyId, periodId, authorityId, { reason }),
            "Authority revocation appended; prior evidence remains immutable.",
          )}
        />
      )}
      {composer?.kind === "cro" && canPrepare && (
        <CroSnapshotForm
          workspace={workspace}
          predecessor={composer.predecessor}
          busy={busy}
          onCancel={() => setComposer(null)}
          onSave={(request: CroHandoffSnapshotRequest) => commit(
            () => generateCroHandoffSnapshot(companyId, periodId, request),
            composer.predecessor ? "Linked CRO amendment retained; its predecessor was not changed." : "Immutable CRO handoff snapshot retained.",
          )}
        />
      )}
      {composer?.kind === "revenue" && canPrepare && (
        <RevenueSnapshotForm
          workspace={workspace}
          predecessor={composer.predecessor}
          busy={busy}
          onCancel={() => setComposer(null)}
          onSave={(request: RevenueHandoffSnapshotRequest) => commit(
            () => generateRevenueHandoffSnapshot(companyId, periodId, request),
            composer.predecessor ? "Linked Revenue support amendment retained; its predecessor was not changed." : "Immutable Revenue support handoff snapshot retained.",
          )}
        />
      )}
      {composer?.kind === "outcome" && canReview && (
        <OutcomeEvidenceForm
          snapshot={composer.snapshot}
          workspace={workspace}
          busy={busy}
          onCancel={() => setComposer(null)}
          onSave={(request: ExternalFilingOutcomeRequest) => commit(
            () => recordExternalFilingOutcome(companyId, periodId, composer.snapshot.document.snapshotId, request),
            "Append-only handoff outcome retained against the exact snapshot hash.",
          )}
        />
      )}

      <ExternalFilingHandoffWorkbench
        workspace={workspace}
        canPrepare={canPrepare}
        canRecordExternalOutcome={canReview}
        isBusy={busy}
        onPrepareSnapshot={prepare}
        onAmendSnapshot={amend}
        onRecordExternalOutcome={(snapshot) => setComposer({ kind: "outcome", snapshot })}
        onDownloadArtifact={download}
      />
    </section>
  );
}

async function sha256(bytes: ArrayBuffer) {
  const digest = await crypto.subtle.digest("SHA-256", bytes);
  return Array.from(new Uint8Array(digest), (byte) => byte.toString(16).padStart(2, "0")).join("");
}
