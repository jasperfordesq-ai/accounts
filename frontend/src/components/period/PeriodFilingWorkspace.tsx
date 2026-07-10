"use client";

import { Card } from "@heroui/react";
import { ArrowRight, ClipboardList } from "lucide-react";
import type {
  AuditExemptionJeopardy,
  AuditLogEntry,
  FilingDeadline,
  FilingReadinessProfile,
  FilingWorkflowStatus,
} from "@/lib/api";
import { FilingDeadlinesPanel } from "@/components/period/FilingDeadlinesPanel";
import { FilingOutputsPanel, type FilingOutputChecklist } from "@/components/period/FilingOutputsPanel";
import { FilingReviewCentre } from "@/components/period/FilingReviewCentre";
import { ExternalFilingHandoffRuntime } from "@/components/period/ExternalFilingHandoffRuntime";
import { PeriodAuditTrailPanel } from "@/components/period/PeriodAuditTrailPanel";
import { StatutoryWarningsPanel } from "@/components/period/StatutoryWarningsPanel";
import { ResourceStateNotice } from "@/components/ResourceStateNotice";
import { ActionLink } from "@/components/workbench";
import type { ResourceState } from "@/lib/resourceState";

type FilingReviewAction = () => void | Promise<void>;

interface PeriodFilingWorkspaceProps {
  companyId: number;
  periodId: number;
  filingStatus: FilingWorkflowStatus | null;
  filingReadinessProfile: FilingReadinessProfile | null;
  croSubmissionReference: string;
  validatingIxbrl: boolean;
  canApprove: boolean;
  canRead: boolean;
  canReview: boolean;
  canWriteWorkingPapers?: boolean;
  canReadExternalHandoff?: boolean;
  deadlines: FilingDeadline[];
  filingReferences: Record<number, string>;
  markingFiledId: number | null;
  jeopardy: AuditExemptionJeopardy | null;
  section307Note: string | null;
  filingRegimeReady: boolean;
  downloadingDocument: string | null;
  checklist: FilingOutputChecklist;
  auditLog: AuditLogEntry[];
  auditTotal: number;
  auditPage: number;
  auditPageSize: number;
  auditPageCount: number;
  loadingAuditLog: boolean;
  auditLogError: string | null;
  filingResourceState?: ResourceState;
  auditResourceState?: ResourceState;
  notesHref: string;
  onCroSubmissionReferenceChange: (value: string) => void;
  onRunIxbrlChecks: FilingReviewAction;
  onApproveForFiling: FilingReviewAction;
  onMarkCroSubmitted: (submissionReference: string) => void | Promise<void>;
  onConfirmCroPayment: FilingReviewAction;
  onMarkCroAccepted: FilingReviewAction;
  onRecordCroSendBack: FilingReviewAction;
  onFilingReferenceChange: (deadlineId: number, value: string) => void;
  onMarkFiled: (deadline: FilingDeadline, filingReference?: string) => void | Promise<void>;
  onReferenceMissing: (message: string) => void;
  onDownloadAgmPack: FilingReviewAction;
  onDownloadCroFilingPack: FilingReviewAction;
  onDownloadSignaturePage: FilingReviewAction;
  onDownloadIxbrl: FilingReviewAction;
  onAuditPageChange: (page: number) => void;
  onAuditPageSizeChange: (pageSize: number) => void;
  onRetryAuditLog: FilingReviewAction;
  onRetryFiling?: FilingReviewAction;
  onExternalHandoffChanged?: FilingReviewAction;
}

export function PeriodFilingWorkspace({
  companyId,
  periodId,
  filingStatus,
  filingReadinessProfile,
  croSubmissionReference,
  validatingIxbrl,
  canApprove,
  canRead,
  canReview,
  canWriteWorkingPapers = false,
  canReadExternalHandoff = false,
  deadlines,
  filingReferences,
  markingFiledId,
  jeopardy,
  section307Note,
  filingRegimeReady,
  downloadingDocument,
  checklist,
  auditLog,
  auditTotal,
  auditPage,
  auditPageSize,
  auditPageCount,
  loadingAuditLog,
  auditLogError,
  filingResourceState,
  auditResourceState,
  notesHref,
  onCroSubmissionReferenceChange,
  onRunIxbrlChecks,
  onApproveForFiling,
  onMarkCroSubmitted,
  onConfirmCroPayment,
  onMarkCroAccepted,
  onRecordCroSendBack,
  onFilingReferenceChange,
  onMarkFiled,
  onReferenceMissing,
  onDownloadAgmPack,
  onDownloadCroFilingPack,
  onDownloadSignaturePage,
  onDownloadIxbrl,
  onAuditPageChange,
  onAuditPageSizeChange,
  onRetryAuditLog,
  onRetryFiling,
  onExternalHandoffChanged,
}: PeriodFilingWorkspaceProps) {
  const filingEvidenceAvailable = !filingResourceState
    || filingResourceState.status === "loaded"
    || filingResourceState.status === "empty";
  return (
    <div id="period-filing-workspace" tabIndex={-1} className="space-y-6 outline-none focus-visible:ring-2 focus-visible:ring-emerald-500">
      {filingResourceState && (
        <ResourceStateNotice state={filingResourceState} label="filing workflow evidence" onRetry={onRetryFiling} />
      )}
      <FilingReviewCentre
        filingStatus={filingStatus}
        filingReadinessProfile={filingReadinessProfile}
        croSubmissionReference={croSubmissionReference}
        validatingIxbrl={validatingIxbrl}
        canApprove={canApprove}
        canReview={canReview}
        canWriteWorkingPapers={canWriteWorkingPapers}
        evidenceAvailable={filingEvidenceAvailable}
        onCroSubmissionReferenceChange={onCroSubmissionReferenceChange}
        onRunIxbrlChecks={onRunIxbrlChecks}
        onApproveForFiling={onApproveForFiling}
        onMarkCroSubmitted={onMarkCroSubmitted}
        onConfirmCroPayment={onConfirmCroPayment}
        onMarkCroAccepted={onMarkCroAccepted}
        onRecordCroSendBack={onRecordCroSendBack}
      />

      <ExternalFilingHandoffRuntime
        companyId={companyId}
        periodId={periodId}
        canRead={canReadExternalHandoff}
        canPrepare={canWriteWorkingPapers}
        canReview={canApprove}
        onEvidenceChanged={onExternalHandoffChanged}
      />

      <FilingDeadlinesPanel
        canReview={canReview}
        deadlines={deadlines}
        filingStatus={filingStatus}
        filingReferences={filingReferences}
        markingFiledId={markingFiledId}
        evidenceAvailable={filingEvidenceAvailable}
        onFilingReferenceChange={onFilingReferenceChange}
        onMarkFiled={onMarkFiled}
        onReferenceMissing={onReferenceMissing}
      />

      <StatutoryWarningsPanel jeopardy={jeopardy} section307Note={section307Note} />

      <FilingOutputsPanel
        canRead={canRead}
        canGenerate={canWriteWorkingPapers}
        filingRegimeReady={filingRegimeReady}
        downloadingDocument={downloadingDocument}
        checklist={checklist}
        onDownloadAgmPack={onDownloadAgmPack}
        onDownloadCroFilingPack={onDownloadCroFilingPack}
        onDownloadSignaturePage={onDownloadSignaturePage}
        onDownloadIxbrl={onDownloadIxbrl}
      />

      <PeriodAuditTrailPanel
        auditLog={auditLog}
        auditTotal={auditTotal}
        page={auditPage}
        pageSize={auditPageSize}
        totalPages={auditPageCount}
        loading={loadingAuditLog}
        error={auditLogError}
        resourceState={auditResourceState}
        onPageChange={onAuditPageChange}
        onPageSizeChange={onAuditPageSizeChange}
        onRetry={onRetryAuditLog}
      />

      <Card className="shadow-sm border border-purple-200 dark:border-purple-800 bg-purple-50/30 dark:bg-purple-900/10">
        <Card.Content className="p-4">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-3">
              <ClipboardList className="w-5 h-5 text-purple-600 dark:text-purple-400" />
              <div>
                <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-100">Review Notes</h3>
                <p className="text-xs text-gray-500 dark:text-gray-400 mt-0.5">
                  Review and finalise notes before filing.
                </p>
              </div>
            </div>
            <ActionLink href={notesHref}>
              Review Notes
              <ArrowRight className="w-4 h-4 ml-1.5" />
            </ActionLink>
          </div>
        </Card.Content>
      </Card>
    </div>
  );
}
