import { Button, Chip, Spinner } from "@heroui/react";
import { CheckCircle2, FileText, Shield, Upload } from "lucide-react";
import type { FilingReadinessProfile, FilingWorkflowStatus } from "@/lib/api";
import {
  EvidenceChecklist,
  FilingActionBar,
  ReviewPanel,
  SectionHeader,
  StatusBadge,
} from "@/components/workbench";

type FilingReviewAction = () => void | Promise<void>;
type FilingIssueTone = "bad" | "warn";

interface FilingIssueView {
  message: string;
  tone: FilingIssueTone;
}

interface FilingReviewCentreProps {
  filingStatus: FilingWorkflowStatus | null;
  filingReadinessProfile: FilingReadinessProfile | null;
  croSubmissionReference: string;
  validatingIxbrl: boolean;
  onCroSubmissionReferenceChange: (value: string) => void;
  onRunIxbrlChecks: FilingReviewAction;
  onApproveForFiling: FilingReviewAction;
  onMarkCroSubmitted: (submissionReference: string) => void | Promise<void>;
  onConfirmCroPayment: FilingReviewAction;
  onMarkCroAccepted: FilingReviewAction;
  onRecordCroSendBack: FilingReviewAction;
}

export function FilingReviewCentre({
  filingStatus,
  filingReadinessProfile,
  croSubmissionReference,
  validatingIxbrl,
  onCroSubmissionReferenceChange,
  onRunIxbrlChecks,
  onApproveForFiling,
  onMarkCroSubmitted,
  onConfirmCroPayment,
  onMarkCroAccepted,
  onRecordCroSendBack,
}: FilingReviewCentreProps) {
  const filingIssues = buildFilingIssueGroups(filingStatus, filingReadinessProfile);

  return (
    <ReviewPanel
      title="Filing readiness profile"
      description="Source-backed CRO, Revenue and accountant-review gates for this period."
      actions={
        <div className="flex flex-wrap items-center gap-2">
          <StatusBadge tone={filingReadinessProfile?.supportedPath ? "good" : "bad"}>
            {filingReadinessProfile?.supportedPath ? "Supported path" : "Manual handoff"}
          </StatusBadge>
          <StatusBadge tone={filingStatus?.readyToFile ? "good" : "warn"}>
            {filingStatus?.readyToFile ? "Workflow ready" : "Workflow open"}
          </StatusBadge>
        </div>
      }
    >
      {filingStatus ? (
        <div className="space-y-4">
          <div className="grid gap-3 lg:grid-cols-4">
            <StatusTile label="CRO workflow" value={filingStatus.cro.status} tone={filingStatusTone(filingStatus.cro.status)} />
            <StatusTile label="Revenue workflow" value={filingStatus.revenue.status} tone={filingStatusTone(filingStatus.revenue.status)} />
            <StatusTile label="Accountant review" value={filingReadinessProfile?.accountantReviewState ?? "Required"} />
            <StatusTile label="Taxonomy" value={filingReadinessProfile?.revenueTaxonomy.taxonomyDate ?? "Pending"} />
          </div>

          {filingReadinessProfile && (
            <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_22rem]">
              <div className="space-y-3">
                <SectionHeader
                  eyebrow="Required evidence"
                  title="Professional filing gate"
                  description="Generated outputs remain draft/recorded workflow states until the required evidence and named review are complete."
                />
                <EvidenceChecklist items={filingReadinessProfile.requiredEvidence} />
              </div>
              <div className="space-y-3">
                <div className="rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-4">
                  <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">Submission controls</p>
                  <div className="mt-3 space-y-2 text-sm text-[var(--foreground)]">
                    <SubmissionControl
                      label="Direct CRO submission"
                      supported={filingReadinessProfile.directCroSubmissionSupported}
                      supportedLabel="Enabled"
                      unsupportedLabel="Recorded only"
                    />
                    <SubmissionControl
                      label="Direct ROS submission"
                      supported={filingReadinessProfile.directRosSubmissionSupported}
                      supportedLabel="Enabled"
                      unsupportedLabel="Manual"
                    />
                  </div>
                </div>
                <div className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-4">
                  <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">Legal sources</p>
                  <div className="mt-3 space-y-2">
                    {filingReadinessProfile.sourceReferences.slice(0, 6).map((source) => (
                      <a
                        key={source.sourceId}
                        href={source.url}
                        target="_blank"
                        rel="noreferrer"
                        className="block rounded border border-[var(--border)] px-3 py-2 text-xs font-medium text-[var(--foreground)] hover:bg-[var(--surface-subtle)]"
                      >
                        {source.title}
                      </a>
                    ))}
                  </div>
                </div>
              </div>
            </div>
          )}

          <FilingIssueDigest blockers={filingIssues.blockers} warnings={filingIssues.warnings} />

          <Button
            variant="outline"
            size="sm"
            isDisabled={validatingIxbrl}
            onPress={() => {
              void onRunIxbrlChecks();
            }}
          >
            {validatingIxbrl ? (
              <>
                <Spinner size="sm" className="mr-2" />
                Checking...
              </>
            ) : (
              <>
                <FileText className="w-4 h-4 mr-1" />
                Run iXBRL Checks
              </>
            )}
          </Button>

          {shouldShowCroReference(filingStatus.cro.status) && (
            <div className="max-w-sm">
              <label htmlFor="cro-submission-reference" className="mb-1 block text-xs font-medium uppercase text-[var(--muted-foreground)]">
                CORE submission reference
              </label>
              <input
                id="cro-submission-reference"
                aria-label="CORE submission reference"
                title="CORE submission reference"
                value={croSubmissionReference}
                onChange={(event) => onCroSubmissionReferenceChange(event.target.value)}
                disabled={filingStatus.cro.status === "Accepted"}
                className="h-9 w-full rounded-md border border-[var(--border)] bg-[var(--surface)] px-3 text-sm text-[var(--foreground)] outline-none transition focus:border-[var(--ring)] focus:ring-2 focus:ring-teal-100 disabled:bg-[var(--muted)] disabled:text-[var(--muted-foreground)] dark:focus:ring-teal-900/40"
                placeholder="CORE-2026-0001"
              />
            </div>
          )}

          <FilingActionBar>
            <CroWorkflowActions
              filingStatus={filingStatus}
              filingReadinessProfile={filingReadinessProfile}
              croSubmissionReference={croSubmissionReference}
              onApproveForFiling={onApproveForFiling}
              onMarkCroSubmitted={onMarkCroSubmitted}
              onConfirmCroPayment={onConfirmCroPayment}
              onMarkCroAccepted={onMarkCroAccepted}
              onRecordCroSendBack={onRecordCroSendBack}
            />
          </FilingActionBar>
        </div>
      ) : (
        <div className="text-center py-8">
          <Shield className="w-10 h-10 text-gray-300 dark:text-gray-600 mx-auto mb-3" />
          <p className="text-sm text-gray-500 dark:text-gray-400">Filing status is not available yet.</p>
          <p className="text-xs text-gray-400 dark:text-gray-500 mt-1">Complete the statements and generate documents first.</p>
        </div>
      )}
    </ReviewPanel>
  );
}

function StatusTile({
  label,
  value,
  tone = "default",
}: {
  label: string;
  value: string;
  tone?: "default" | "good" | "warn" | "bad" | "info";
}) {
  return (
    <div className="rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3">
      <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">{label}</p>
      <div className="mt-2">
        {tone === "default" ? (
          <p className="text-sm font-medium text-[var(--foreground)]">{value}</p>
        ) : (
          <StatusBadge tone={tone}>{value}</StatusBadge>
        )}
      </div>
    </div>
  );
}

function SubmissionControl({
  label,
  supported,
  supportedLabel,
  unsupportedLabel,
}: {
  label: string;
  supported: boolean;
  supportedLabel: string;
  unsupportedLabel: string;
}) {
  return (
    <div className="flex items-center justify-between gap-3">
      <span>{label}</span>
      <StatusBadge tone={supported ? "good" : "warn"}>
        {supported ? supportedLabel : unsupportedLabel}
      </StatusBadge>
    </div>
  );
}

function FilingIssueDigest({
  blockers,
  warnings,
}: {
  blockers: FilingIssueView[];
  warnings: FilingIssueView[];
}) {
  if (blockers.length === 0 && warnings.length === 0) return null;

  const priorityBlockers = blockers.slice(0, 3);
  const remainingBlockers = blockers.slice(3);

  return (
    <div className="rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3">
      <div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">Filing issue digest</p>
          <p className="mt-1 text-sm font-medium text-[var(--foreground)]">
            {blockers.length > 0 ? "Resolve priority blockers before approval." : "Warnings remain before external filing evidence is complete."}
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <StatusBadge tone={blockers.length > 0 ? "bad" : "good"}>{formatIssueCount(blockers.length, "blocker")}</StatusBadge>
          <StatusBadge tone={warnings.length > 0 ? "warn" : "good"}>{formatIssueCount(warnings.length, "warning")}</StatusBadge>
        </div>
      </div>

      {priorityBlockers.length > 0 && (
        <div className="mt-3">
          <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">Priority blockers</p>
          <IssueList issues={priorityBlockers} className="mt-2" />
          {remainingBlockers.length > 0 && (
            <details className="mt-2 rounded-md border border-red-200 bg-red-50/60 p-2 dark:border-red-900 dark:bg-red-950/30">
              <summary className="cursor-pointer text-xs font-semibold text-red-800 dark:text-red-100">
                {formatIssueCount(remainingBlockers.length, "more blocker")}
              </summary>
              <IssueList issues={remainingBlockers} className="mt-2" />
            </details>
          )}
        </div>
      )}

      {warnings.length > 0 && (
        <div className="mt-3">
          <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">Warnings</p>
          <IssueList issues={warnings} className="mt-2" />
        </div>
      )}
    </div>
  );
}

function CroWorkflowActions({
  filingStatus,
  filingReadinessProfile,
  croSubmissionReference,
  onApproveForFiling,
  onMarkCroSubmitted,
  onConfirmCroPayment,
  onMarkCroAccepted,
  onRecordCroSendBack,
}: {
  filingStatus: FilingWorkflowStatus;
  filingReadinessProfile: FilingReadinessProfile | null;
  croSubmissionReference: string;
  onApproveForFiling: FilingReviewAction;
  onMarkCroSubmitted: (submissionReference: string) => void | Promise<void>;
  onConfirmCroPayment: FilingReviewAction;
  onMarkCroAccepted: FilingReviewAction;
  onRecordCroSendBack: FilingReviewAction;
}) {
  if (["NotStarted", "InProgress", "PackageGenerated"].includes(filingStatus.cro.status)) {
    return (
      <Button
        variant="primary"
        size="sm"
        isDisabled={!filingStatus.readyToFile || filingReadinessProfile?.manualProfessionalReviewRequired === true}
        onPress={() => {
          void onApproveForFiling();
        }}
      >
        <CheckCircle2 className="w-4 h-4 mr-1" /> Approve for Filing
      </Button>
    );
  }

  if (filingStatus.cro.status === "Approved") {
    return (
      <Button
        variant="primary"
        size="sm"
        onPress={() => {
          void onMarkCroSubmitted(croSubmissionReference.trim());
        }}
      >
        <Upload className="w-4 h-4 mr-1" /> Mark as Submitted
      </Button>
    );
  }

  if (filingStatus.cro.status === "Submitted") {
    return (
      <>
        <Chip size="sm" variant="soft" color={filingStatus.cro.paymentCompleted ? "success" : "warning"}>
          {filingStatus.cro.paymentCompleted ? "CORE payment confirmed" : "Submitted - payment needed"}
        </Chip>
        {!filingStatus.cro.paymentCompleted && (
          <Button
            variant="outline"
            size="sm"
            onPress={() => {
              void onConfirmCroPayment();
            }}
          >
            Confirm CORE Payment
          </Button>
        )}
        <Button
          variant="outline"
          size="sm"
          isDisabled={!filingStatus.cro.paymentCompleted}
          onPress={() => {
            void onMarkCroAccepted();
          }}
        >
          Mark Accepted
        </Button>
        <Button
          variant="outline"
          size="sm"
          onPress={() => {
            void onRecordCroSendBack();
          }}
        >
          Record Send-Back
        </Button>
      </>
    );
  }

  if (filingStatus.cro.status === "Accepted") {
    return <Chip size="sm" variant="soft" color="success">Filing accepted by CRO</Chip>;
  }

  if (filingStatus.cro.status === "CorrectionRequired") {
    return (
      <Chip size="sm" variant="soft" color="danger">
        Correction due {filingStatus.cro.correctionDeadline ? new Date(filingStatus.cro.correctionDeadline).toLocaleDateString("en-IE") : "within 14 days"}
      </Chip>
    );
  }

  return null;
}

function filingStatusTone(status?: string): "default" | "good" | "warn" | "bad" | "info" {
  if (status === "Accepted" || status === "Filed" || status === "Approved") return "good";
  if (status === "Submitted" || status === "PackageGenerated" || status === "ReadyForReview") return "info";
  if (status === "Rejected" || status === "CorrectionRequired") return "bad";
  if (status === "NotStarted" || status === "InProgress") return "warn";
  return "default";
}

function shouldShowCroReference(status: string) {
  return status === "Approved" || status === "Submitted" || status === "Accepted";
}

function IssueList({ issues, className = "" }: { issues: FilingIssueView[]; className?: string }) {
  return (
    <ul className={`space-y-1.5 ${className}`}>
      {issues.map((issue, index) => (
        <li
          key={issueKey(issue.message, index)}
          className={issue.tone === "bad"
            ? "text-sm leading-6 text-red-800 dark:text-red-100"
            : "text-sm leading-6 text-amber-900 dark:text-amber-100"}
        >
          {issue.message}
        </li>
      ))}
    </ul>
  );
}

function buildFilingIssueGroups(
  filingStatus: FilingWorkflowStatus | null,
  filingReadinessProfile: FilingReadinessProfile | null,
) {
  const blockers = uniqueIssueViews([
    ...(filingReadinessProfile?.blockingIssues.map((issue) => ({ message: issue.message, tone: "bad" as const })) ?? []),
    ...(filingStatus?.blockingIssues.map((message) => ({ message, tone: "bad" as const })) ?? []),
  ]);
  const warnings = uniqueIssueViews([
    ...(filingReadinessProfile?.warningIssues.map((issue) => ({ message: issue.message, tone: "warn" as const })) ?? []),
    ...(filingStatus?.warningIssues.map((message) => ({ message, tone: "warn" as const })) ?? []),
  ]);

  return { blockers, warnings };
}

function uniqueIssueViews(issues: FilingIssueView[]) {
  const seen = new Set<string>();
  return issues.filter((issue) => {
    const key = issue.message.trim().toLowerCase();
    if (!key || seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}

function formatIssueCount(count: number, singular: string) {
  return `${count} ${count === 1 ? singular : `${singular}s`}`;
}

function issueKey(message: string, index: number) {
  return `${message.slice(0, 36)}-${index}`;
}
