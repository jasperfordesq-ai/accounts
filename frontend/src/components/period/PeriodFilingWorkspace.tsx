"use client";

import { Button, Card } from "@heroui/react";
import Link from "next/link";
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
import { PeriodAuditTrailPanel } from "@/components/period/PeriodAuditTrailPanel";
import { StatutoryWarningsPanel } from "@/components/period/StatutoryWarningsPanel";

type FilingReviewAction = () => void | Promise<void>;

interface PeriodFilingWorkspaceProps {
  filingStatus: FilingWorkflowStatus | null;
  filingReadinessProfile: FilingReadinessProfile | null;
  croSubmissionReference: string;
  validatingIxbrl: boolean;
  canReview: boolean;
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
}

export function PeriodFilingWorkspace({
  filingStatus,
  filingReadinessProfile,
  croSubmissionReference,
  validatingIxbrl,
  canReview,
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
}: PeriodFilingWorkspaceProps) {
  return (
    <div className="space-y-6">
      <FilingReviewCentre
        filingStatus={filingStatus}
        filingReadinessProfile={filingReadinessProfile}
        croSubmissionReference={croSubmissionReference}
        validatingIxbrl={validatingIxbrl}
        canReview={canReview}
        onCroSubmissionReferenceChange={onCroSubmissionReferenceChange}
        onRunIxbrlChecks={onRunIxbrlChecks}
        onApproveForFiling={onApproveForFiling}
        onMarkCroSubmitted={onMarkCroSubmitted}
        onConfirmCroPayment={onConfirmCroPayment}
        onMarkCroAccepted={onMarkCroAccepted}
        onRecordCroSendBack={onRecordCroSendBack}
      />

      <FilingDeadlinesPanel
        deadlines={deadlines}
        filingStatus={filingStatus}
        filingReferences={filingReferences}
        markingFiledId={markingFiledId}
        onFilingReferenceChange={onFilingReferenceChange}
        onMarkFiled={onMarkFiled}
        onReferenceMissing={onReferenceMissing}
      />

      <StatutoryWarningsPanel jeopardy={jeopardy} section307Note={section307Note} />

      <FilingOutputsPanel
        filingRegimeReady={filingRegimeReady}
        downloadingDocument={downloadingDocument}
        checklist={checklist}
        onDownloadAgmPack={onDownloadAgmPack}
        onDownloadCroFilingPack={onDownloadCroFilingPack}
        onDownloadSignaturePage={onDownloadSignaturePage}
        onDownloadIxbrl={onDownloadIxbrl}
      />

      <PeriodAuditTrailPanel auditLog={auditLog} auditTotal={auditTotal} />

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
            <Link href={notesHref}>
              <Button variant="outline" size="sm">
                Review Notes
                <ArrowRight className="w-4 h-4 ml-1.5" />
              </Button>
            </Link>
          </div>
        </Card.Content>
      </Card>
    </div>
  );
}
