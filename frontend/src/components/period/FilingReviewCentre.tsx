import { Button, Chip, Spinner } from "@heroui/react";
import { AlertTriangle, CheckCircle2, ClipboardCheck, FileText, Shield, Upload } from "lucide-react";
import type {
  FilingReadinessProfile,
  FilingReadinessSignOffPacket,
  FilingReadinessSignOffStep,
  FilingWorkflowStatus,
} from "@/lib/api";
import {
  EvidenceChecklist,
  FilingActionBar,
  IssueDigest,
  LegalSourceList,
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
            <div className="space-y-4">
              <FilingDecisionCentre
                filingStatus={filingStatus}
                filingReadinessProfile={filingReadinessProfile}
                filingIssues={filingIssues}
              />

              <SignOffPacketPanel packet={filingReadinessProfile.signOffPacket} />

              <div className="grid min-w-0 max-w-full gap-4 xl:grid-cols-[minmax(0,1fr)_22rem]">
                <div className="min-w-0 max-w-full space-y-3">
                  <SectionHeader
                    eyebrow="Required evidence"
                    title="Professional filing gate"
                    description="Generated outputs remain draft/recorded workflow states until the required evidence and named review are complete."
                  />
                  <EvidenceChecklist items={filingReadinessProfile.requiredEvidence} />
                </div>
                <div className="min-w-0 max-w-full space-y-3">
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
                    <div className="mt-3">
                      <LegalSourceList sources={filingReadinessProfile.sourceReferences} limit={6} />
                    </div>
                  </div>
                </div>
              </div>
            </div>
          )}

          <IssueDigest
            title="Filing issue digest"
            description={filingIssues.blockers.length > 0
              ? "Resolve priority blockers before approval."
              : "Warnings remain before external filing evidence is complete."}
            blockers={filingIssues.blockers}
            warnings={filingIssues.warnings}
          />

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

function FilingDecisionCentre({
  filingStatus,
  filingReadinessProfile,
  filingIssues,
}: {
  filingStatus: FilingWorkflowStatus;
  filingReadinessProfile: FilingReadinessProfile;
  filingIssues: ReturnType<typeof buildFilingIssueGroups>;
}) {
  const wrongItems = filingIssues.blockers.length > 0
    ? filingIssues.blockers
    : filingIssues.warnings.length > 0
      ? filingIssues.warnings
      : ["No blocking filing issues are currently recorded."];
  const readyItems = filingReadinessProfile.requiredEvidence
    .filter((item) => item.satisfied)
    .map((item) => item.label);
  const nextActions = uniqueIssueMessages([
    ...filingReadinessProfile.signOffPacket.allowedNextActions,
    ...filingReadinessProfile.allowedNextActions,
  ]).map(formatActionLabel);
  const nextItems = nextActions.length > 0
    ? nextActions
    : [filingReadinessProfile.manualProfessionalReviewRequired
      ? "Move this file to manual professional handoff."
      : filingStatus.readyToFile
        ? "Record the named accountant approval and external filing evidence."
        : "Resolve the filing blockers before approval."];
  const warningItems = filingIssues.blockers.length > 0 ? filingIssues.warnings : [];

  return (
    <section
      aria-label="Filing decision centre"
      className="rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-4"
    >
      <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
        <div className="min-w-0">
          <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">Filing decision centre</p>
          <p className="mt-1 max-w-3xl text-sm leading-6 text-[var(--muted-foreground)]">
            Accountant-facing summary of blockers, evidenced outputs and the next allowed workflow step.
          </p>
        </div>
        <StatusBadge tone={filingIssues.blockers.length > 0 ? "bad" : filingIssues.warnings.length > 0 ? "warn" : "good"}>
          {filingIssues.blockers.length > 0
            ? `${filingIssues.blockers.length} blockers`
            : filingIssues.warnings.length > 0
              ? `${filingIssues.warnings.length} warnings`
              : "Clear"}
        </StatusBadge>
      </div>

      <div className="mt-4 grid gap-3 lg:grid-cols-3">
        <DecisionSummaryCard
          title="What is wrong?"
          tone={filingIssues.blockers.length > 0 ? "bad" : filingIssues.warnings.length > 0 ? "warn" : "good"}
          items={wrongItems}
          maxItems={2}
        />
        <DecisionSummaryCard
          title="What is ready?"
          tone={readyItems.length > 0 ? "good" : "warn"}
          items={readyItems.length > 0 ? readyItems : ["No required evidence is complete yet."]}
          maxItems={3}
        />
        <DecisionSummaryCard
          title="What must I do next?"
          tone={nextActions.length > 0 ? "info" : filingReadinessProfile.manualProfessionalReviewRequired ? "bad" : "warn"}
          items={nextItems}
          maxItems={3}
        />
      </div>

      {warningItems.length > 0 && (
        <div className="mt-3 rounded-md border border-amber-200 bg-amber-50/60 p-3 dark:border-amber-900 dark:bg-amber-950/30">
          <p className="text-xs font-semibold uppercase text-amber-900 dark:text-amber-100">Warnings to evidence</p>
          <ul className="mt-2 space-y-1.5">
            {warningItems.slice(0, 2).map((warning) => (
              <li key={warning} className="text-sm leading-6 text-amber-900 dark:text-amber-100">
                {warning}
              </li>
            ))}
          </ul>
        </div>
      )}
    </section>
  );
}

function DecisionSummaryCard({
  title,
  tone,
  items,
  maxItems,
}: {
  title: string;
  tone: "default" | "good" | "warn" | "bad" | "info";
  items: string[];
  maxItems: number;
}) {
  const visibleItems = items.slice(0, maxItems);
  const hiddenCount = Math.max(0, items.length - maxItems);
  const textClass = tone === "bad"
    ? "text-red-800 dark:text-red-100"
    : tone === "warn"
      ? "text-amber-900 dark:text-amber-100"
      : "text-[var(--foreground)]";

  return (
    <article className="min-w-0 rounded-md border border-[var(--border)] bg-[var(--surface)] p-3">
      <div className="flex items-start justify-between gap-3">
        <h3 className="text-sm font-semibold text-[var(--foreground)]">{title}</h3>
        <StatusBadge tone={tone}>{visibleItems.length}</StatusBadge>
      </div>
      <ul className="mt-3 space-y-1.5">
        {visibleItems.map((item) => (
          <li key={item} className={`text-sm leading-6 ${textClass}`}>
            {item}
          </li>
        ))}
      </ul>
      {hiddenCount > 0 && (
        <p className="mt-2 text-xs font-medium text-[var(--muted-foreground)]">
          {hiddenCount} more {hiddenCount === 1 ? "item" : "items"}
        </p>
      )}
    </article>
  );
}

function SignOffPacketPanel({ packet }: { packet: FilingReadinessSignOffPacket }) {
  const openIssueCount = packet.openBlockers.length + packet.openWarnings.length;
  const specialistSteps = packet.steps.filter(isSpecialistSignOffStep);
  const standardSteps = packet.steps.filter((step) => !isSpecialistSignOffStep(step));

  return (
    <section className="rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-4" aria-label="Accountant sign-off packet">
      <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
        <div className="min-w-0">
          <div className="flex min-w-0 items-center gap-2">
            <ClipboardCheck className="h-4 w-4 shrink-0 text-[var(--muted-foreground)]" />
            <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">Accountant sign-off packet</p>
          </div>
          <p className="mt-1 text-sm font-semibold text-[var(--foreground)]">{packet.stateLabel}</p>
          <p className="mt-1 text-xs leading-5 text-[var(--muted-foreground)]">
            {packet.approvedBy
              ? `Approved by ${packet.approvedBy}${packet.approvedAt ? ` on ${formatIrishDateTime(packet.approvedAt)}` : ""}.`
              : "Named qualified-accountant approval is recorded only after every required gate is evidenced."}
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <StatusBadge tone={signOffStateTone(packet.state)}>{formatStateLabel(packet.state)}</StatusBadge>
          <StatusBadge tone={openIssueCount > 0 ? "warn" : "good"}>{openIssueCount} open</StatusBadge>
        </div>
      </div>

      {specialistSteps.length > 0 && <SpecialistEvidenceGates steps={specialistSteps} />}

      <div className="mt-4 grid gap-4 xl:grid-cols-[minmax(0,1fr)_18rem]">
        <ol className="grid gap-2 md:grid-cols-2">
          {standardSteps.map((step) => (
            <SignOffStepItem key={step.code} step={step} />
          ))}
        </ol>
        <div className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-3">
          <p className="text-xs font-semibold uppercase text-[var(--muted-foreground)]">Allowed next actions</p>
          {packet.allowedNextActions.length > 0 ? (
            <div className="mt-3 flex flex-wrap gap-2">
              {packet.allowedNextActions.map((action) => (
                <StatusBadge key={action} tone="info">{formatActionLabel(action)}</StatusBadge>
              ))}
            </div>
          ) : (
            <p className="mt-3 text-sm leading-6 text-[var(--muted-foreground)]">
              No filing workflow action is allowed until blockers or manual handoff are resolved.
            </p>
          )}
        </div>
      </div>
    </section>
  );
}

function SpecialistEvidenceGates({ steps }: { steps: FilingReadinessSignOffStep[] }) {
  return (
    <section
      className="mt-4 rounded-md border border-sky-200 bg-sky-50/70 p-3 dark:border-sky-900 dark:bg-sky-950/30"
      aria-label="Specialist evidence gates"
    >
      <div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
        <div className="min-w-0">
          <div className="flex items-center gap-2">
            <Shield className="h-4 w-4 text-sky-700 dark:text-sky-200" />
            <p className="text-xs font-semibold uppercase text-sky-900 dark:text-sky-100">Specialist evidence gates</p>
          </div>
          <p className="mt-1 text-sm leading-6 text-sky-950 dark:text-sky-100">
            Charity, audit and manual-handoff evidence that affects the filing decision.
          </p>
        </div>
        <StatusBadge tone={steps.some((step) => step.state === "blocked") ? "bad" : "info"}>
          {steps.length} specialist {steps.length === 1 ? "gate" : "gates"}
        </StatusBadge>
      </div>

      <div className="mt-3 grid gap-3 lg:grid-cols-2">
        {steps.map((step) => (
          <SpecialistEvidenceGateCard key={step.code} step={step} />
        ))}
      </div>
    </section>
  );
}

function SpecialistEvidenceGateCard({ step }: { step: FilingReadinessSignOffStep }) {
  const tone = signOffStepTone(step.state);

  return (
    <article className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-3">
      <div className="flex items-start justify-between gap-3">
        <div className="flex min-w-0 items-start gap-2">
          {step.state === "blocked" ? (
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-red-600 dark:text-red-300" />
          ) : (
            <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-emerald-600 dark:text-emerald-300" />
          )}
          <div className="min-w-0">
            <h3 className="text-sm font-semibold text-[var(--foreground)]">{step.label}</h3>
            <p className="mt-1 text-xs leading-5 text-[var(--muted-foreground)]">{step.detail}</p>
          </div>
        </div>
        <StatusBadge tone={tone}>{formatStateLabel(step.state)}</StatusBadge>
      </div>

      {step.sources.length > 0 && (
        <div className="mt-3">
          <LegalSourceList sources={step.sources} />
        </div>
      )}
    </article>
  );
}

function SignOffStepItem({ step }: { step: FilingReadinessSignOffStep }) {
  const tone = signOffStepTone(step.state);

  return (
    <li className="min-w-0 rounded-md border border-[var(--border)] bg-[var(--surface)] p-3">
      <div className="flex items-start justify-between gap-3">
        <p className="min-w-0 text-sm font-medium text-[var(--foreground)]">{step.label}</p>
        <StatusBadge tone={tone}>{formatStateLabel(step.state)}</StatusBadge>
      </div>
      <p className="mt-2 text-xs leading-5 text-[var(--muted-foreground)]">{step.detail}</p>
    </li>
  );
}

function isSpecialistSignOffStep(step: FilingReadinessSignOffStep) {
  return step.code === "charity-reporting" || step.code === "auditor-handoff";
}

function signOffStateTone(state: string): "default" | "good" | "warn" | "bad" | "info" {
  if (state === "ready-for-external-filing") return "good";
  if (state === "ready-for-accountant-review" || state === "approved-external-evidence-open") return "info";
  if (state === "manual-handoff" || state === "blocked") return "bad";
  return "default";
}

function signOffStepTone(state: string): "default" | "good" | "warn" | "bad" | "info" {
  if (state === "complete") return "good";
  if (state === "blocked") return "bad";
  if (state === "warning") return "warn";
  if (state === "pending") return "info";
  return "default";
}

function formatStateLabel(state: string) {
  const labels: Record<string, string> = {
    "approved-external-evidence-open": "Evidence open",
    blocked: "Blocked",
    complete: "Complete",
    "manual-handoff": "Manual gate",
    pending: "Pending",
    "ready-for-accountant-review": "Ready",
    "ready-for-external-filing": "External-ready",
    warning: "Warning",
  };

  return labels[state] ?? actionToTitleCase(state);
}

function formatActionLabel(action: string) {
  const labels: Record<string, string> = {
    "approve-cro-pack": "Approve CRO pack",
    "confirm-core-payment": "Confirm CORE payment",
    "generate-cro-accounts-pdf": "Generate CRO accounts PDF",
    "generate-cro-signature-page": "Generate CRO signature page",
    "mark-cro-accepted": "Mark CRO accepted",
    "mark-cro-submitted": "Mark CRO submitted",
    "run-internal-ixbrl-checks": "Run internal iXBRL checks",
  };

  return labels[action] ?? actionToTitleCase(action);
}

function actionToTitleCase(value: string) {
  return value
    .split(/[-_\s]+/)
    .filter(Boolean)
    .map((part) => `${part.charAt(0).toUpperCase()}${part.slice(1)}`)
    .join(" ");
}

function formatIrishDateTime(value: string) {
  return new Intl.DateTimeFormat("en-IE", {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(new Date(value));
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
    const disabledReason = approvalDisabledReason(filingStatus, filingReadinessProfile);

    return (
      <div className="flex min-w-0 flex-col gap-2">
        <Button
          variant="primary"
          size="sm"
          isDisabled={disabledReason !== null}
          onPress={() => {
            void onApproveForFiling();
          }}
        >
          <CheckCircle2 className="w-4 h-4 mr-1" /> Approve for Filing
        </Button>
        {disabledReason && (
          <p className="max-w-xl text-xs leading-5 text-[var(--muted-foreground)]">
            {disabledReason}
          </p>
        )}
      </div>
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

function approvalDisabledReason(
  filingStatus: FilingWorkflowStatus,
  filingReadinessProfile: FilingReadinessProfile | null,
) {
  if (filingReadinessProfile?.manualProfessionalReviewRequired === true) {
    return "Approval is disabled while manual professional handoff is required.";
  }

  if (!filingStatus.readyToFile) {
    return "Approval is disabled until filing readiness blockers are resolved.";
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

function buildFilingIssueGroups(
  filingStatus: FilingWorkflowStatus | null,
  filingReadinessProfile: FilingReadinessProfile | null,
) {
  const blockers = uniqueIssueMessages([
    ...(filingReadinessProfile?.blockingIssues.map((issue) => issue.message) ?? []),
    ...(filingStatus?.blockingIssues ?? []),
  ]);
  const warnings = uniqueIssueMessages([
    ...(filingReadinessProfile?.warningIssues.map((issue) => issue.message) ?? []),
    ...(filingStatus?.warningIssues ?? []),
  ]);

  return { blockers, warnings };
}

function uniqueIssueMessages(issues: string[]) {
  const seen = new Set<string>();
  return issues.filter((issue) => {
    const key = issue.trim().toLowerCase();
    if (!key || seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}
