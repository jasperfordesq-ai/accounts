"use client";

import { Button } from "@heroui/react";
import {
  AlertTriangle,
  CheckCircle2,
  FileClock,
  FileKey2,
  History,
  Link2,
  LockKeyhole,
  RefreshCw,
  ShieldAlert,
} from "lucide-react";
import type {
  ExternalFilingHandoffDocument,
  ExternalFilingHandoffWorkspace,
  ExternalFilingSnapshot,
  ExternalFilingWorkflow,
} from "@/lib/externalFilingHandoff";
import {
  PermissionDeniedPanel,
  ReviewPanel,
  SectionHeader,
  StatusBadge,
  WorkflowDecisionSummary,
} from "@/components/workbench";

interface ExternalFilingHandoffWorkbenchProps {
  workspace: ExternalFilingHandoffWorkspace;
  canPrepare?: boolean;
  canRecordExternalOutcome?: boolean;
  isBusy?: boolean;
  onPrepareSnapshot?: (workflow: ExternalFilingWorkflow) => void | Promise<void>;
  onAmendSnapshot?: (snapshot: ExternalFilingSnapshot) => void | Promise<void>;
  onRecordExternalOutcome?: (snapshot: ExternalFilingSnapshot) => void | Promise<void>;
  onDownloadArtifact?: (snapshot: ExternalFilingSnapshot) => void | Promise<void>;
}

const WORKFLOWS: ReadonlyArray<{ workflow: ExternalFilingWorkflow; title: string; description: string }> = [
  {
    workflow: "CroB1",
    title: "CRO B1 manual handoff",
    description: "Field worksheet, presenter authority, member/allotment facts and exact attachment hashes.",
  },
  {
    workflow: "RevenueCt1Support",
    title: "Revenue CT1 support handoff",
    description: "Bounded tax support, ROS agent authority, iXBRL evidence and explicit manual CT1 completion.",
  },
];

export function ExternalFilingHandoffWorkbench({
  workspace,
  canPrepare = false,
  canRecordExternalOutcome = false,
  isBusy = false,
  onPrepareSnapshot,
  onAmendSnapshot,
  onRecordExternalOutcome,
  onDownloadArtifact,
}: ExternalFilingHandoffWorkbenchProps) {
  const latest = new Map<ExternalFilingWorkflow, ExternalFilingSnapshot>();
  for (const snapshot of workspace.snapshots) {
    const current = latest.get(snapshot.document.workflow);
    if (!current || snapshot.document.version > current.document.version) {
      latest.set(snapshot.document.workflow, snapshot);
    }
  }

  const allBlockers = [...latest.values()].flatMap((snapshot) => snapshot.document.blockingIssues);
  const ready = [...latest.values()].filter((snapshot) => snapshot.document.readyForManualHandoff);
  const nextActions = workspace.sourceGaps.length > 0
    ? workspace.sourceGaps
    : allBlockers.length > 0
      ? allBlockers
      : ["Retain the external acknowledgement against the exact immutable snapshot hash."];

  return (
    <div className="space-y-5" data-external-filing-handoff-workbench="true">
      <div
        role="status"
        className="rounded-md border border-amber-300 bg-amber-50 p-4 text-sm leading-6 text-amber-950 dark:border-amber-800 dark:bg-amber-950/30 dark:text-amber-100"
      >
        <div className="flex items-start gap-3">
          <ShieldAlert className="mt-0.5 h-5 w-5 shrink-0" aria-hidden="true" />
          <div>
            <p className="font-semibold">External handoff only — no CRO or ROS submission</p>
            <p className="mt-1">
              This workspace prepares and retains manual handoff evidence. An authorised presenter or ROS agent completes the
              filing outside this platform, then a reviewer records the external reference against the exact snapshot hash.
            </p>
          </div>
        </div>
      </div>

      <WorkflowDecisionSummary
        ariaLabel="External filing handoff decision summary"
        items={[
          {
            title: "What is wrong?",
            tone: allBlockers.length > 0 || workspace.sourceGaps.length > 0 ? "bad" : "good",
            summary: allBlockers.length > 0 || workspace.sourceGaps.length > 0 ? "Handoff evidence is incomplete" : "No current handoff blocker",
            detail: (allBlockers.length > 0 ? allBlockers : workspace.sourceGaps).slice(0, 2).join(" ") || "Current source and authority checks have no recorded gap.",
          },
          {
            title: "What is ready?",
            tone: ready.length > 0 ? "good" : "warn",
            summary: ready.length > 0 ? `${ready.length} immutable handoff snapshot${ready.length === 1 ? "" : "s"}` : "No snapshot is ready",
            detail: ready.map((snapshot) => `${workflowLabel(snapshot.document.workflow)} v${snapshot.document.version}`).join(" · ") || "Complete the required source rows and authority evidence.",
          },
          {
            title: "What must I do next?",
            tone: "info",
            summary: nextActions[0] || "Retain external evidence",
            detail: nextActions.slice(1, 3).join(" ") || "Record any external outcome against the exact artifact SHA-256.",
          },
        ]}
      />

      <div className="grid gap-4 xl:grid-cols-2">
        {WORKFLOWS.map(({ workflow, title, description }) => (
          <WorkflowCard
            key={workflow}
            workflow={workflow}
            title={title}
            description={description}
            workspace={workspace}
            snapshot={latest.get(workflow) ?? null}
            canPrepare={canPrepare}
            canRecordExternalOutcome={canRecordExternalOutcome}
            isBusy={isBusy}
            onPrepareSnapshot={onPrepareSnapshot}
            onAmendSnapshot={onAmendSnapshot}
            onRecordExternalOutcome={onRecordExternalOutcome}
          />
        ))}
      </div>

      <SnapshotChain workspace={workspace} onDownloadArtifact={onDownloadArtifact} isBusy={isBusy} />
      <OutcomeHistory workspace={workspace} />

      <ReviewPanel
        title="Genuine external source gaps"
        description="These facts are unavailable in the current platform model and must never be guessed from accounting data."
      >
        {workspace.sourceGaps.length > 0 ? (
          <ul className="space-y-2 text-sm text-[var(--foreground)]">
            {workspace.sourceGaps.map((gap) => (
              <li key={gap} className="flex items-start gap-2 rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3">
                <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-amber-600" aria-hidden="true" />
                <span>{gap}</span>
              </li>
            ))}
          </ul>
        ) : (
          <p className="flex items-center gap-2 text-sm text-emerald-700 dark:text-emerald-300">
            <CheckCircle2 className="h-4 w-4" aria-hidden="true" />
            No unresolved source gaps are recorded for the current snapshots.
          </p>
        )}
      </ReviewPanel>
    </div>
  );
}

interface WorkflowCardProps extends Omit<ExternalFilingHandoffWorkbenchProps, "workspace"> {
  workflow: ExternalFilingWorkflow;
  title: string;
  description: string;
  workspace: ExternalFilingHandoffWorkspace;
  snapshot: ExternalFilingSnapshot | null;
}

function WorkflowCard({
  workflow,
  title,
  description,
  workspace,
  snapshot,
  canPrepare,
  canRecordExternalOutcome,
  isBusy,
  onPrepareSnapshot,
  onAmendSnapshot,
  onRecordExternalOutcome,
}: WorkflowCardProps) {
  const authority = [...workspace.authorities]
    .filter((item) => item.workflow === workflow)
    .sort((left, right) => right.reviewedAtUtc.localeCompare(left.reviewedAtUtc))[0];
  const outcomes = snapshot
    ? workspace.outcomes.filter((event) => event.snapshotId === snapshot.document.snapshotId)
    : [];
  const lastOutcome = [...outcomes].sort((left, right) => right.recordedAtUtc.localeCompare(left.recordedAtUtc))[0];

  return (
    <ReviewPanel
      title={title}
      description={description}
      actions={(
        <div className="flex flex-wrap gap-2">
          <StatusBadge tone={authority?.status === "Active" ? "good" : "bad"}>
            {authority?.status === "Active" ? "Authority active" : "Authority blocked"}
          </StatusBadge>
          <StatusBadge tone={snapshot?.document.readyForManualHandoff ? "good" : "warn"}>
            {snapshot?.document.readyForManualHandoff ? "Handoff ready" : "Preparation open"}
          </StatusBadge>
        </div>
      )}
    >
      <div className="space-y-4">
        <AuthoritySummary authority={authority} />
        {snapshot ? (
          <>
            <SnapshotIntegrity snapshot={snapshot} lastOutcome={lastOutcome?.outcome ?? null} />
            <FieldBoard document={snapshot.document} />
          </>
        ) : (
          <div className="rounded-md border border-dashed border-[var(--border)] bg-[var(--surface-subtle)] p-4 text-sm text-[var(--muted-foreground)]">
            No immutable {workflow === "CroB1" ? "B1" : "CT1 support"} snapshot has been retained for this period.
          </div>
        )}

        {canPrepare || canRecordExternalOutcome ? (
          <div className="flex flex-wrap gap-2 border-t border-[var(--border)] pt-4">
            {canPrepare && <Button
                size="sm"
                isDisabled={isBusy || !onPrepareSnapshot}
                onPress={() => { void onPrepareSnapshot?.(workflow); }}
              >
                <FileKey2 className="mr-1 h-4 w-4" aria-hidden="true" />
                Generate immutable snapshot
              </Button>}
            {canPrepare && snapshot && (
              <Button
                size="sm"
                variant="outline"
                isDisabled={isBusy || !onAmendSnapshot}
                onPress={() => { void onAmendSnapshot?.(snapshot); }}
              >
                <RefreshCw className="mr-1 h-4 w-4" aria-hidden="true" />
                Create linked amendment
              </Button>
            )}
            {snapshot && canRecordExternalOutcome && (
              <Button
                size="sm"
                variant="outline"
                isDisabled={isBusy || !onRecordExternalOutcome}
                onPress={() => { void onRecordExternalOutcome?.(snapshot); }}
              >
                <Link2 className="mr-1 h-4 w-4" aria-hidden="true" />
                Record external outcome
              </Button>
            )}
          </div>
        ) : (
          <PermissionDeniedPanel
            title="Preparation permission required"
            description="Evidence remains visible. Ask an Owner or Accountant to prepare a snapshot, or an Owner or Reviewer to record an external outcome."
          />
        )}
      </div>
    </ReviewPanel>
  );
}

function AuthoritySummary({
  authority,
}: {
  authority: ExternalFilingHandoffWorkspace["authorities"][number] | undefined;
}) {
  if (!authority) {
    return (
      <div className="rounded-md border border-red-300 bg-red-50 p-3 text-sm text-red-900 dark:border-red-900 dark:bg-red-950/30 dark:text-red-100">
        Current presenter/agent engagement evidence is missing. External workflow advancement must remain blocked.
      </div>
    );
  }
  return (
    <div className="grid gap-3 rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-4 sm:grid-cols-2">
      <Fact label="Authorised party" value={authority.practiceName || authority.legalName} />
      <Fact label="Masked presenter / TAIN" value={authority.maskedPresenterOrTain || "Protected — not exposed"} />
      <Fact label="Engagement" value={authority.engagementReference} />
      <Fact label="Authority reference" value={authority.externalAuthorityReference} />
      <Fact label="Reviewed by" value={`${authority.reviewedBy.displayName} · ${authority.reviewedBy.role}`} />
      <Fact label="Effective until" value={authority.effectiveUntilUtc ? formatUtc(authority.effectiveUntilUtc) : "No recorded end date"} />
      <div className="sm:col-span-2">
        <Fact label="Authority evidence SHA-256" value={authority.authorityEvidenceSha256} mono />
      </div>
    </div>
  );
}

function SnapshotIntegrity({ snapshot, lastOutcome }: { snapshot: ExternalFilingSnapshot; lastOutcome: string | null }) {
  const document = snapshot.document;
  return (
    <div className="grid gap-3 sm:grid-cols-2">
      <IntegrityFact label="Snapshot" value={`v${document.version} · ${document.snapshotId}`} />
      <IntegrityFact label="Latest outcome" value={lastOutcome ? outcomeLabel(lastOutcome) : "No outcome recorded"} />
      <IntegrityFact label="Exact artifact SHA-256" value={snapshot.artifactSha256} mono />
      <IntegrityFact label="Source fingerprint SHA-256" value={document.sourceFingerprintSha256} mono />
      <IntegrityFact label="Release candidate" value={document.releaseCandidate} />
      <IntegrityFact
        label="Predecessor binding"
        value={document.supersedesArtifactSha256
          ? `v${document.version - 1} · ${shortHash(document.supersedesArtifactSha256)}`
          : "Initial snapshot"}
      />
    </div>
  );
}

function FieldBoard({ document }: { document: ExternalFilingHandoffDocument }) {
  const blockers = new Set(document.blockingIssues);
  return (
    <div className="space-y-3">
      <SectionHeader
        eyebrow="Field-by-field worksheet"
        title={document.workflow === "CroB1" ? "CORE B1 handoff fields" : "ROS CT1 support fields"}
        description={document.workflow === "CroB1"
          ? "Values are copied into the immutable artifact; protected officer identifiers are never displayed or retained here."
          : "This remains bounded support data and never represents a complete CT1 return."}
      />
      {document.externalCompletionWarnings.length > 0 && (
        <div className="rounded-md border border-amber-300 bg-amber-50 p-3 text-sm text-amber-950 dark:border-amber-800 dark:bg-amber-950/30 dark:text-amber-100">
          <ul className="space-y-1">
            {document.externalCompletionWarnings.map((warning) => <li key={warning}>{warning}</li>)}
          </ul>
        </div>
      )}
      <div className="grid gap-2 sm:grid-cols-2">
        {document.sources.map((source) => (
          <div key={source.code} className="rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3 text-xs text-[var(--muted-foreground)]">
            <p className="font-semibold text-[var(--foreground)]">{source.title}</p>
            <p className="mt-1">Effective basis: {source.effectiveDate}</p>
            <p className="mt-1">Reviewed: {formatUtc(source.reviewedAtUtc)}</p>
          </div>
        ))}
      </div>
      <div className="max-h-96 overflow-auto rounded-md border border-[var(--border)]">
        <table className="w-full min-w-[42rem] border-collapse text-left text-sm">
          <thead className="sticky top-0 bg-[var(--surface-subtle)] text-xs uppercase text-[var(--muted-foreground)]">
            <tr>
              <th className="px-3 py-2">Field</th>
              <th className="px-3 py-2">Value</th>
              <th className="px-3 py-2">Status</th>
              <th className="px-3 py-2">Evidence source</th>
            </tr>
          </thead>
          <tbody>
            {document.fields.map((field) => (
              <tr key={field.fieldCode} className="border-t border-[var(--border)] align-top">
                <td className="px-3 py-3">
                  <p className="font-medium text-[var(--foreground)]">{field.label}</p>
                  <p className="mt-1 font-mono text-[11px] text-[var(--muted-foreground)]">{field.fieldCode}</p>
                </td>
                <td className="max-w-xs px-3 py-3 text-[var(--foreground)]">
                  {field.isProtectedManualEntry ? "Protected entry not retained" : field.value || "—"}
                </td>
                <td className="px-3 py-3">
                  <StatusBadge tone={field.status === "Complete" || field.status === "NotApplicable" ? "good" : "warn"}>
                    {fieldStatusLabel(field.status)}
                  </StatusBadge>
                  {field.blockingReason && (
                    <p className={`mt-1 max-w-xs text-xs ${blockers.has(field.blockingReason) ? "text-red-700 dark:text-red-300" : "text-[var(--muted-foreground)]"}`}>
                      {field.blockingReason}
                    </p>
                  )}
                </td>
                <td className="max-w-xs px-3 py-3 text-xs text-[var(--muted-foreground)]">{field.sourceReference}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function SnapshotChain({ workspace, onDownloadArtifact, isBusy }: {
  workspace: ExternalFilingHandoffWorkspace;
  onDownloadArtifact?: (snapshot: ExternalFilingSnapshot) => void | Promise<void>;
  isBusy?: boolean;
}) {
  return (
    <ReviewPanel
      title="Immutable amendment chains"
      description="Each amendment has a new identity and exact predecessor hash; prior as-filed evidence is never rewritten."
    >
      {workspace.snapshots.length > 0 ? (
        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
          {[...workspace.snapshots]
            .sort((left, right) => left.document.workflow.localeCompare(right.document.workflow) || left.document.version - right.document.version)
            .map((snapshot) => (
              <div key={snapshot.document.snapshotId} className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-4">
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <p className="font-semibold text-[var(--foreground)]">{workflowLabel(snapshot.document.workflow)} v{snapshot.document.version}</p>
                    <p className="mt-1 text-xs text-[var(--muted-foreground)]">Prepared {formatUtc(snapshot.document.preparedAtUtc)}</p>
                  </div>
                  <LockKeyhole className="h-5 w-5 text-teal-600" aria-hidden="true" />
                </div>
                <p className="mt-3 font-mono text-xs text-[var(--muted-foreground)]" title={snapshot.artifactSha256}>
                  {shortHash(snapshot.artifactSha256)}
                </p>
                {snapshot.document.supersedesArtifactSha256 && (
                  <p className="mt-2 text-xs text-[var(--muted-foreground)]">
                    Supersedes {shortHash(snapshot.document.supersedesArtifactSha256)}
                  </p>
                )}
                {snapshot.document.amendmentReason && (
                  <p className="mt-2 text-sm text-[var(--foreground)]">{snapshot.document.amendmentReason}</p>
                )}
                <Button
                  size="sm"
                  variant="outline"
                  className="mt-3"
                  isDisabled={isBusy || !onDownloadArtifact}
                  onPress={() => { void onDownloadArtifact?.(snapshot); }}
                >
                  Download verified artifact
                </Button>
              </div>
            ))}
        </div>
      ) : (
        <p className="text-sm text-[var(--muted-foreground)]">No snapshot chain exists yet.</p>
      )}
    </ReviewPanel>
  );
}

function OutcomeHistory({ workspace }: { workspace: ExternalFilingHandoffWorkspace }) {
  return (
    <ReviewPanel
      title="External reference and correction history"
      description="Append-only events bind every external acknowledgement, rejection, correction and acceptance to one exact snapshot hash."
    >
      {workspace.outcomes.length > 0 ? (
        <ol className="space-y-3">
          {[...workspace.outcomes].sort((left, right) => left.recordedAtUtc.localeCompare(right.recordedAtUtc)).map((event) => (
            <li key={event.eventId} className="flex gap-3 rounded-md border border-[var(--border)] bg-[var(--surface)] p-4">
              <History className="mt-0.5 h-5 w-5 shrink-0 text-teal-600" aria-hidden="true" />
              <div className="min-w-0 flex-1">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <p className="font-semibold text-[var(--foreground)]">{outcomeLabel(event.outcome)}</p>
                  <p className="text-xs text-[var(--muted-foreground)]">
                    {event.externalOccurredAtUtc
                      ? `External: ${formatUtc(event.externalOccurredAtUtc)}`
                      : `Recorded: ${formatUtc(event.recordedAtUtc)}`}
                  </p>
                </div>
                {event.externalReference ? (
                  <p className="mt-1 text-sm text-[var(--foreground)]">External reference: {event.externalReference}</p>
                ) : (
                  <p className="mt-1 text-sm text-[var(--muted-foreground)]">Internal chronology event — no external reference asserted.</p>
                )}
                <p className="mt-1 font-mono text-xs text-[var(--muted-foreground)]">Snapshot {shortHash(event.snapshotArtifactSha256)}</p>
                {event.supersedingSnapshotId && event.supersedingSnapshotArtifactSha256 && (
                  <p className="mt-1 font-mono text-xs text-[var(--muted-foreground)]">
                    Successor {event.supersedingSnapshotId} · {shortHash(event.supersedingSnapshotArtifactSha256)}
                  </p>
                )}
                {event.reason && <p className="mt-2 text-sm text-amber-800 dark:text-amber-200">{event.reason}</p>}
                {event.correctionDeadlineUtc && (
                  <p className="mt-1 text-sm font-medium text-red-700 dark:text-red-300">
                    Correction deadline: {formatUtc(event.correctionDeadlineUtc)}
                  </p>
                )}
                <p className="mt-2 text-xs text-[var(--muted-foreground)]">
                  Recorded by {event.recordedBy.displayName}
                  {event.evidenceSha256 ? ` · external evidence ${shortHash(event.evidenceSha256)}` : " · snapshot integrity is the internal evidence"}
                </p>
              </div>
            </li>
          ))}
        </ol>
      ) : (
        <div className="flex items-center gap-2 text-sm text-[var(--muted-foreground)]">
          <FileClock className="h-4 w-4" aria-hidden="true" />
          No external outcomes have been recorded. This is not evidence that a filing occurred.
        </div>
      )}
    </ReviewPanel>
  );
}

function Fact({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="min-w-0">
      <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">{label}</p>
      <p className={`mt-1 break-all text-sm text-[var(--foreground)] ${mono ? "font-mono text-xs" : ""}`}>{value}</p>
    </div>
  );
}

function IntegrityFact({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3">
      <Fact label={label} value={value} mono={mono} />
    </div>
  );
}

function shortHash(value: string) {
  return `${value.slice(0, 12)}…${value.slice(-8)}`;
}

function formatUtc(value: string) {
  return value.replace("T", " ").replace(/\.\d{3,7}(?=Z$)/, "").replace("Z", " UTC");
}

function workflowLabel(workflow: ExternalFilingWorkflow) {
  return workflow === "CroB1" ? "CRO B1" : "Revenue CT1 support";
}

function fieldStatusLabel(status: ExternalFilingHandoffDocument["fields"][number]["status"]) {
  return ({
    Complete: "Complete",
    Missing: "Missing",
    RequiresReview: "Review required",
    ProtectedManualEntry: "Protected manual entry",
    NotApplicable: "Not applicable",
  } as const)[status];
}

function outcomeLabel(outcome: string) {
  return ({
    ReadyForManualHandoff: "Ready for manual handoff",
    ExternallySubmittedRecorded: "External submission recorded",
    CorrectionRequired: "Correction required",
    ExternallyRejected: "Externally rejected",
    ExternallyAcceptedRecorded: "External acceptance recorded",
    SupersededByAmendment: "Superseded by linked amendment",
  } as Record<string, string>)[outcome] ?? outcome;
}
