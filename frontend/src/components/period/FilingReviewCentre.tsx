import { Button, Chip, Spinner } from "@heroui/react";
import { AlertTriangle, CheckCircle2, FileText, Shield, Upload } from "lucide-react";
import type { FilingReadinessProfile, FilingWorkflowStatus } from "@/lib/api";
import {
  EvidenceChecklist,
  FilingActionBar,
  ReviewPanel,
  SectionHeader,
  StatusBadge,
} from "@/components/workbench";

type FilingReviewAction = () => void | Promise<void>;

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

          <FilingIssues filingStatus={filingStatus} filingReadinessProfile={filingReadinessProfile} />

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

function FilingIssues({
  filingStatus,
  filingReadinessProfile,
}: {
  filingStatus: FilingWorkflowStatus;
  filingReadinessProfile: FilingReadinessProfile | null;
}) {
  if (filingReadinessProfile?.blockingIssues.length) {
    return (
      <div className="rounded-md border border-red-200 bg-red-50 p-4 dark:border-red-900 dark:bg-red-950/40">
        <h4 className="flex items-center gap-2 text-sm font-semibold text-red-800 dark:text-red-100">
          <AlertTriangle className="h-4 w-4" /> Statutory blockers
        </h4>
        <ul className="mt-2 space-y-1.5">
          {filingReadinessProfile.blockingIssues.map((issue, index) => (
            <li key={issueKey(issue.message, index)} className="text-sm leading-6 text-red-800 dark:text-red-100">
              {issue.message}
            </li>
          ))}
        </ul>
      </div>
    );
  }

  return (
    <>
      {filingStatus.blockingIssues.length > 0 && (
        <div className="rounded-md border border-red-200 bg-red-50 p-4 dark:border-red-900 dark:bg-red-950/40">
          <h4 className="text-sm font-semibold text-red-800 dark:text-red-100">Workflow blockers</h4>
          <ul className="mt-2 space-y-1.5">
            {filingStatus.blockingIssues.map((issue, index) => (
              <li key={issueKey(issue, index)} className="text-sm leading-6 text-red-800 dark:text-red-100">
                {issue}
              </li>
            ))}
          </ul>
        </div>
      )}

      {filingStatus.warningIssues.length > 0 && (
        <div className="rounded-md border border-amber-200 bg-amber-50 p-4 dark:border-amber-800 dark:bg-amber-950/40">
          <h4 className="text-sm font-semibold text-amber-900 dark:text-amber-100">Filing warnings</h4>
          <ul className="mt-2 space-y-1.5">
            {filingStatus.warningIssues.map((issue, index) => (
              <li key={issueKey(issue, index)} className="text-sm leading-6 text-amber-900 dark:text-amber-100">
                {issue}
              </li>
            ))}
          </ul>
        </div>
      )}
    </>
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

function issueKey(message: string, index: number) {
  return `${message.slice(0, 36)}-${index}`;
}
