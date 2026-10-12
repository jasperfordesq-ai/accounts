"use client";

import { useCallback, useEffect, useState, type ReactNode } from "react";
import { Button, Spinner } from "@heroui/react";
import { Download, FileCheck2 } from "lucide-react";
import { toast } from "sonner";
import {
  directorsApprovalPageUrl,
  generateDirectorsApprovalPage,
  getDirectorsApproval,
  recordDirectorsApproval,
  withdrawDirectorsApproval,
  type DirectorsApprovalHistoryItem,
  type DirectorsApprovalPageSummary,
  type DirectorsApprovalState,
  type DirectorsApprovalSummary,
  type DirectorsApprovalWithdrawalReason,
} from "@/lib/api";
import { formatDateIE } from "@/lib/format";
import { ReviewPanel, StatusBadge } from "@/components/workbench";
import { isRecentMfaRequired } from "@/lib/filingEvidence";
import {
  Field,
  RecentVerificationPrompt,
  errorMessage,
  formatDateTime,
  inputClass,
  shortHash,
} from "@/components/period/FilingEvidenceShared";

const REFERENCE_MIN = 3;
const REFERENCE_MAX = 200;
const NOTE_MIN = 3;
const NOTE_MAX = 500;

const WITHDRAWAL_REASONS: ReadonlyArray<{ value: DirectorsApprovalWithdrawalReason; label: string }> = [
  { value: "entered-in-error", label: "Entered in error" },
  { value: "board-rescinded", label: "Board rescinded the approval" },
  { value: "superseded", label: "Superseded by a new approval" },
];

/** Plain-English labels for the reasons stored on a revocation (manual withdrawals carry a "withdrawn-" prefix). */
function revocationLabel(reason: string | null) {
  const manual = WITHDRAWAL_REASONS.find((option) => `withdrawn-${option.value}` === reason);
  if (manual) return `Withdrawn: ${manual.label}`;
  if (reason === "accounts-regenerated") return "Withdrawn automatically: the accounts were regenerated";
  if (reason === "accounts-source-changed") return "Withdrawn automatically: the figures behind the accounts changed";
  return reason ? `Withdrawn: ${reason}` : "Withdrawn";
}

export function DirectorsApprovalPanel({
  companyId,
  periodId,
  canRecord,
  canGenerate = true,
  onChanged,
}: {
  companyId: number;
  periodId: number;
  /** Owner or Reviewer: may record the signed approval. */
  canRecord: boolean;
  /** Owner, Accountant or Reviewer: may generate the approval page. */
  canGenerate?: boolean;
  onChanged?: () => void | Promise<void>;
}) {
  const [state, setState] = useState<DirectorsApprovalState | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [firstId, setFirstId] = useState("");
  const [secondId, setSecondId] = useState("");
  const [reference, setReference] = useState("");
  const [generating, setGenerating] = useState(false);
  const [signedPage, setSignedPage] = useState<File | null>(null);
  const [resolution, setResolution] = useState<File | null>(null);
  const [recording, setRecording] = useState(false);
  const [needsVerification, setNeedsVerification] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setLoadError(null);
    try {
      setState(await getDirectorsApproval(companyId, periodId));
    } catch (error) {
      setLoadError(errorMessage(error, "Failed to load the directors' approval"));
    } finally {
      setLoading(false);
    }
  }, [companyId, periodId]);

  useEffect(() => { void load(); }, [load]);

  const eligible = state?.eligibleDirectors ?? [];
  const soleDirector = eligible.length === 1;
  const approvalDate = state?.periodApprovalDate ?? null;
  const current = state?.current ?? null;
  const latestPage = state?.latestPage ?? null;
  const history = state?.history ?? [];

  async function generate() {
    if (!approvalDate) return;
    const first = soleDirector ? eligible[0]!.officerId : Number(firstId);
    const second = soleDirector ? null : Number(secondId);
    const trimmed = reference.trim();
    if (!first || (!soleDirector && !second)) {
      toast.error(soleDirector ? "Choose the sole director" : "Choose the two directors who will sign");
      return;
    }
    if (second !== null && first === second) {
      toast.error("The two signatories must be different directors");
      return;
    }
    if (trimmed.length < REFERENCE_MIN || trimmed.length > REFERENCE_MAX) {
      toast.error(`The minute or resolution reference must be ${REFERENCE_MIN} to ${REFERENCE_MAX} characters`);
      return;
    }
    setGenerating(true);
    try {
      const generated = await generateDirectorsApprovalPage(companyId, periodId, {
        approvalDate,
        firstDirectorOfficerId: first,
        secondDirectorOfficerId: second,
        resolutionReference: trimmed,
      });
      setState((previous) => previous ? { ...previous, latestPage: generated } : previous);
      toast.success("Approval page generated. Download it, have the directors sign it, then upload it here.");
      await onChanged?.();
    } catch (error) {
      toast.error(errorMessage(error, "Failed to generate the approval page"));
    } finally {
      setGenerating(false);
    }
  }

  async function record() {
    if (!latestPage || !signedPage || !resolution) {
      toast.error("Upload both the signed approval page and the minute or written resolution");
      return;
    }
    setRecording(true);
    try {
      await recordDirectorsApproval(companyId, periodId, {
        approvalPageId: latestPage.id,
        signedPage,
        resolution,
      });
      toast.success("Directors' approval recorded");
      setNeedsVerification(false);
      setSignedPage(null);
      setResolution(null);
      await Promise.all([load(), onChanged?.()]);
    } catch (error) {
      if (isRecentMfaRequired(error)) {
        setNeedsVerification(true);
      } else {
        toast.error(errorMessage(error, "Failed to record the directors' approval"));
      }
    } finally {
      setRecording(false);
    }
  }

  return (
    <ReviewPanel
      title="Directors' approval (s.324)"
      description="The board approves the financial statements under Companies Act 2014, s.324. Two directors (or the sole director) sign on its behalf. No accountant is needed."
      actions={(
        <StatusBadge tone={current ? "good" : "warn"}>
          {current ? "Approval recorded" : "Approval required"}
        </StatusBadge>
      )}
    >
      {loading ? (
        <div className="flex items-center gap-2 text-sm text-[var(--muted-foreground)]"><Spinner size="sm" /> Loading directors&apos; approval</div>
      ) : loadError ? (
        <div className="space-y-2 text-sm text-red-800 dark:text-red-200" role="alert">
          <p>{loadError}</p>
          <Button size="sm" variant="outline" onPress={() => void load()}>Retry</Button>
        </div>
      ) : current ? (
        <div className="space-y-4">
          <RecordedApproval approval={current}>
            {canRecord && (
              <WithdrawApproval
                companyId={companyId}
                periodId={periodId}
                onWithdrawn={async () => { await Promise.all([load(), onChanged?.()]); }}
              />
            )}
          </RecordedApproval>
          <ApprovalHistory history={history} />
        </div>
      ) : (
        <div className="space-y-4">
          {approvalDate === null ? (
            <p className="rounded-md border border-amber-200 bg-amber-50 p-3 text-sm text-amber-950 dark:border-amber-900 dark:bg-amber-950/30 dark:text-amber-100">
              Finalise the period to record the board approval date (s.324), then generate the final accounts.
            </p>
          ) : (
            <>
              {canGenerate && (
                <GenerateStep
                  approvalDate={approvalDate}
                  eligible={eligible}
                  soleDirector={soleDirector}
                  firstId={firstId}
                  secondId={secondId}
                  reference={reference}
                  generating={generating}
                  onFirstChange={setFirstId}
                  onSecondChange={setSecondId}
                  onReferenceChange={setReference}
                  onGenerate={() => void generate()}
                />
              )}
              {latestPage && (
                <LatestPage
                  page={latestPage}
                  downloadHref={directorsApprovalPageUrl(companyId, periodId, latestPage.id)}
                />
              )}
              {latestPage && canRecord && (
                <div className="space-y-3 rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-4">
                  <p className="text-sm font-semibold text-[var(--foreground)]">Step 2: upload the signed evidence</p>
                  <div className="grid gap-4 md:grid-cols-2">
                    <Field label="Signed approval page (PDF)">
                      <input
                        type="file"
                        accept="application/pdf"
                        className={inputClass}
                        onChange={(event) => setSignedPage(event.target.files?.[0] ?? null)}
                      />
                    </Field>
                    <Field label="Minute or written resolution (PDF)">
                      <input
                        type="file"
                        accept="application/pdf"
                        className={inputClass}
                        onChange={(event) => setResolution(event.target.files?.[0] ?? null)}
                      />
                    </Field>
                  </div>
                  {needsVerification && <RecentVerificationPrompt onVerified={() => setNeedsVerification(false)} />}
                  <div className="flex justify-end">
                    <Button size="sm" variant="primary" aria-label="Record directors' approval" isDisabled={recording} onPress={() => void record()}>
                      {recording ? <Spinner size="sm" /> : "Record directors' approval"}
                    </Button>
                  </div>
                </div>
              )}
            </>
          )}
          <p className="text-xs leading-5 text-[var(--muted-foreground)]">
            Charity and Revenue approvals depend on the CRO accounts and on this directors&apos; approval being recorded.
          </p>
          <ApprovalHistory history={history} />
        </div>
      )}
    </ReviewPanel>
  );
}

function GenerateStep({
  approvalDate,
  eligible,
  soleDirector,
  firstId,
  secondId,
  reference,
  generating,
  onFirstChange,
  onSecondChange,
  onReferenceChange,
  onGenerate,
}: {
  approvalDate: string;
  eligible: DirectorsApprovalState["eligibleDirectors"];
  soleDirector: boolean;
  firstId: string;
  secondId: string;
  reference: string;
  generating: boolean;
  onFirstChange: (value: string) => void;
  onSecondChange: (value: string) => void;
  onReferenceChange: (value: string) => void;
  onGenerate: () => void;
}) {
  if (eligible.length === 0) {
    return (
      <p className="rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-900 dark:border-red-900 dark:bg-red-950/30 dark:text-red-100">
        No director was in office on {formatDateIE(approvalDate)}. Record the directors&apos; appointment dates on the company officers panel first.
      </p>
    );
  }
  const options = (
    <>
      <option value="">Select a director</option>
      {eligible.map((director) => <option key={director.officerId} value={director.officerId}>{director.name}</option>)}
    </>
  );
  return (
    <div className="space-y-3 rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-4">
      <p className="text-sm font-semibold text-[var(--foreground)]">Step 1: generate the approval page</p>
      <div className="grid gap-4 md:grid-cols-2">
        <Field
          label="Approval date"
          hint="This is the board approval date recorded when the period was finalised. It is printed in the accounts, so the signed page uses the same date."
        >
          <input type="date" aria-label="Approval date" value={approvalDate} readOnly className={inputClass} />
        </Field>
        <Field label="Minute or written-resolution reference" hint={`${REFERENCE_MIN} to ${REFERENCE_MAX} characters, for example the minute number.`}>
          <input
            value={reference}
            maxLength={REFERENCE_MAX}
            onChange={(event) => onReferenceChange(event.target.value)}
            className={inputClass}
          />
        </Field>
        {soleDirector ? (
          <Field label="Sole director">
            <select value={firstId} onChange={(event) => onFirstChange(event.target.value)} className={inputClass}>{options}</select>
          </Field>
        ) : (
          <>
            <Field label="First director">
              <select value={firstId} onChange={(event) => onFirstChange(event.target.value)} className={inputClass}>{options}</select>
            </Field>
            <Field label="Second director">
              <select value={secondId} onChange={(event) => onSecondChange(event.target.value)} className={inputClass}>{options}</select>
            </Field>
          </>
        )}
      </div>
      <div className="flex justify-end">
        <Button size="sm" variant="primary" aria-label="Generate approval page" isDisabled={generating} onPress={onGenerate}>
          {generating ? <Spinner size="sm" /> : "Generate approval page"}
        </Button>
      </div>
    </div>
  );
}

function LatestPage({ page, downloadHref }: { page: DirectorsApprovalPageSummary; downloadHref: string }) {
  const signatories = page.secondDirectorName ? `${page.firstDirectorName} and ${page.secondDirectorName}` : `${page.firstDirectorName} (sole director)`;
  return (
    <div className="flex flex-col gap-3 rounded-md border border-[var(--border)] bg-[var(--surface)] p-3 text-sm md:flex-row md:items-center md:justify-between">
      <p className="min-w-0 text-[var(--foreground)]">
        Approval page generated for {formatDateIE(page.approvalDate)}, to be signed by {signatories} ({page.resolutionReference}).
      </p>
      <a
        href={downloadHref}
        className="inline-flex min-h-9 shrink-0 items-center justify-center gap-2 rounded-md border border-[var(--control-border)] bg-[var(--surface-subtle)] px-3 text-xs font-semibold text-[var(--foreground)] hover:border-[var(--ring)]"
      >
        <Download className="h-3.5 w-3.5" />
        Download approval page
      </a>
    </div>
  );
}

function RecordedApproval({ approval, children }: { approval: DirectorsApprovalSummary; children?: ReactNode }) {
  const page = approval.page;
  const signatories = page.secondDirectorName ? `${page.firstDirectorName} and ${page.secondDirectorName}` : `${page.firstDirectorName} (sole director)`;
  return (
    <section
      role="region"
      aria-label="Recorded directors' approval"
      className="rounded-md border border-emerald-200 bg-emerald-50 p-3 text-sm text-emerald-950 dark:border-emerald-900 dark:bg-emerald-950/30 dark:text-emerald-100"
    >
      <div className="flex items-start gap-2">
        <FileCheck2 className="mt-0.5 h-4 w-4 shrink-0" />
        <div className="min-w-0 space-y-1">
          <p className="font-semibold">
            Approved by the board on {formatDateIE(page.approvalDate)}, signed by {signatories}.
          </p>
          <p className="text-xs leading-5">Minute or resolution reference {page.resolutionReference}.</p>
          <p className="text-xs leading-5">
            Recorded by {approval.recordedByDisplayName} on {formatDateTime(approval.recordedAtUtc)}.
          </p>
          <p className="break-all font-mono text-xs leading-5">
            Approval page {shortHash(page.pageSha256)} · Signed page {shortHash(approval.signedPageSha256)} · Resolution {shortHash(approval.resolutionSha256)} · Record {shortHash(approval.recordSha256)}
          </p>
        </div>
      </div>
      {children && <div className="mt-3">{children}</div>}
    </section>
  );
}

/**
 * Owner or Reviewer: withdraws a recorded approval (for example one recorded in error). Nothing is
 * deleted; the server appends a withdrawal to the approval's permanent record.
 */
function WithdrawApproval({
  companyId,
  periodId,
  onWithdrawn,
}: {
  companyId: number;
  periodId: number;
  onWithdrawn: () => Promise<void>;
}) {
  const [open, setOpen] = useState(false);
  const [reason, setReason] = useState<DirectorsApprovalWithdrawalReason | "">("");
  const [note, setNote] = useState("");
  const [confirming, setConfirming] = useState(false);
  const [busy, setBusy] = useState(false);
  const [needsVerification, setNeedsVerification] = useState(false);

  function close() {
    setOpen(false);
    setReason("");
    setNote("");
    setConfirming(false);
    setNeedsVerification(false);
  }

  function review() {
    if (!reason) {
      toast.error("Choose why the approval is being withdrawn");
      return;
    }
    const trimmed = note.trim();
    if (trimmed.length < NOTE_MIN || trimmed.length > NOTE_MAX) {
      toast.error(`Explain why the approval is being withdrawn (${NOTE_MIN} to ${NOTE_MAX} characters)`);
      return;
    }
    setConfirming(true);
  }

  async function withdraw() {
    if (!reason) return;
    setBusy(true);
    try {
      await withdrawDirectorsApproval(companyId, periodId, { reason, note: note.trim() });
      toast.success("Directors' approval withdrawn. It stays in the record, marked as withdrawn.");
      close();
      await onWithdrawn();
    } catch (error) {
      if (isRecentMfaRequired(error)) {
        setNeedsVerification(true);
      } else {
        toast.error(errorMessage(error, "Failed to withdraw the directors' approval"));
      }
    } finally {
      setBusy(false);
    }
  }

  if (!open) {
    return (
      <div className="flex justify-end">
        <Button size="sm" variant="outline" aria-label="Withdraw approval" onPress={() => setOpen(true)}>
          Withdraw approval
        </Button>
      </div>
    );
  }

  const reasonLabel = WITHDRAWAL_REASONS.find((option) => option.value === reason)?.label;
  return (
    <div className="space-y-3 rounded-md border border-[var(--border)] bg-[var(--surface)] p-3 text-[var(--foreground)]">
      <p className="text-sm font-semibold">Withdraw this approval</p>
      {confirming ? (
        <div className="space-y-3 text-sm">
          <p>
            Reason: {reasonLabel}. {note.trim()}
          </p>
          <p className="font-medium">The approval stays in the permanent record marked as withdrawn; it can never be deleted.</p>
          <p className="text-xs leading-5 text-[var(--muted-foreground)]">
            Any filing approval that relied on it is withdrawn too. Record a new directors&apos; approval (s.324) before approving the filing again.
          </p>
          <div className="flex flex-wrap justify-end gap-2">
            <Button size="sm" variant="ghost" aria-label="Back" isDisabled={busy} onPress={() => setConfirming(false)}>Back</Button>
            <Button size="sm" variant="danger" aria-label="Confirm withdrawal" isDisabled={busy} onPress={() => void withdraw()}>
              {busy ? <Spinner size="sm" /> : "Confirm withdrawal"}
            </Button>
          </div>
        </div>
      ) : (
        <>
          <div className="grid gap-4 md:grid-cols-2">
            <Field label="Reason for withdrawal">
              <select
                value={reason}
                onChange={(event) => setReason(event.target.value as DirectorsApprovalWithdrawalReason | "")}
                className={inputClass}
              >
                <option value="">Select a reason</option>
                {WITHDRAWAL_REASONS.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
              </select>
            </Field>
            <Field label="Why is it being withdrawn?" hint={`${NOTE_MIN} to ${NOTE_MAX} characters. Kept in the audit trail.`}>
              <textarea
                value={note}
                maxLength={NOTE_MAX}
                rows={3}
                onChange={(event) => setNote(event.target.value)}
                className={inputClass}
              />
            </Field>
          </div>
          <div className="flex flex-wrap justify-end gap-2">
            <Button size="sm" variant="ghost" aria-label="Cancel" onPress={close}>Cancel</Button>
            <Button size="sm" variant="primary" aria-label="Continue" onPress={review}>Continue</Button>
          </div>
        </>
      )}
      {needsVerification && <RecentVerificationPrompt onVerified={() => setNeedsVerification(false)} />}
    </div>
  );
}

function ApprovalHistory({ history }: { history: ReadonlyArray<DirectorsApprovalHistoryItem> }) {
  if (history.length === 0) return null;
  return (
    <section role="region" aria-label="Directors' approval history" className="space-y-2">
      <h3 className="text-sm font-semibold text-[var(--foreground)]">History</h3>
      <ul className="divide-y divide-[var(--border)] rounded-md border border-[var(--border)] bg-[var(--surface)] text-sm">
        {history.map((item) => (
          <li key={item.eventId} className="flex flex-col gap-1 p-3 md:flex-row md:items-center md:justify-between">
            <span className="text-[var(--foreground)]">
              {formatDateIE(item.approvalDate)} · {item.kind === "Approved" ? "Approved" : revocationLabel(item.reason)}
            </span>
            <span className="text-xs text-[var(--muted-foreground)]">
              By {item.recordedByDisplayName} on {formatDateTime(item.recordedAtUtc)}
            </span>
          </li>
        ))}
      </ul>
    </section>
  );
}
