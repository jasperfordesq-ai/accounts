import { filingWorkflowStatusSchema } from "./apiContracts.ts";
import { postApiJson, type FilingWorkflowStatus } from "./api.ts";

// The CRO correction tracker. While a CRO filing is sent back for correction (or rejected), it keeps its
// CORE reference and correction deadline until the corrected pack is approved and the new submission is
// recorded. If the CRO treats the filing as not delivered (typically once the deadline has passed), an
// Owner or Reviewer closes the tracker with a reason: the filing must then be made afresh.

/** The statuses in which the CRO correction tracker is open. */
export const croCorrectionStatuses = ["CorrectionRequired", "Rejected"] as const;

export function isCroInCorrection(status: string | undefined): boolean {
  return (croCorrectionStatuses as readonly string[]).includes(status ?? "");
}

export const CRO_NOT_DELIVERED_REASON_MIN = 5;
export const CRO_NOT_DELIVERED_REASON_MAX = 1000;

/** Owner/Reviewer, with recent verification: closes the CRO correction as not delivered. */
export function treatCroAsNotDelivered(companyId: number, periodId: number, reason: string) {
  return postApiJson<FilingWorkflowStatus>(
    `/api/companies/${companyId}/periods/${periodId}/filing/cro-not-delivered`,
    { reason },
    filingWorkflowStatusSchema,
    "CRO filing treated as not delivered",
  );
}

/**
 * The correction stays open after the corrected pack is approved, until the new CORE submission is
 * recorded: the send-back's correction deadline (or a rejection's recorded reason) is still set against
 * the original CORE reference. The backend also requires the send-back to post-date the recorded
 * submission; this status view carries no submission time, so it cannot check that.
 */
export function isCroCorrectionOpen(cro: FilingWorkflowStatus["cro"]): boolean {
  return isCroInCorrection(cro.status)
    || ((Boolean(cro.correctionDeadline) || Boolean(cro.rejectionReason))
      && Boolean(cro.submissionReference?.trim())
      && cro.status !== "Submitted"
      && cro.status !== "Accepted");
}

const CORRECTION_WINDOW_MS = 14 * 24 * 60 * 60 * 1000;

/** The charity annual return's equivalent of {@link isCroCorrectionOpen}. */
export function isCharityCorrectionOpen(charity: FilingWorkflowStatus["charity"]): boolean {
  if (isCroInCorrection(charity.status)) return true;
  if (!charity.correctionDeadline || !charity.annualReturnReference?.trim()) return false;
  if (charity.status === "Submitted" || charity.status === "Accepted") return false;
  const sentBack = Date.parse(charity.correctionDeadline) - CORRECTION_WINDOW_MS;
  return !charity.submittedAt || sentBack >= Date.parse(charity.submittedAt);
}

/** The annual return reference box: empty during a correction (the backend needs it entered), else the recorded one. */
export function initialAnnualReturnReference(charity: FilingWorkflowStatus["charity"]): string {
  return isCharityCorrectionOpen(charity) ? "" : charity.annualReturnReference ?? "";
}

/** The CORE reference box: empty during a correction (a new reference is required), else the recorded one. */
export function initialCroSubmissionReference(cro: FilingWorkflowStatus["cro"]): string {
  return isCroCorrectionOpen(cro) ? "" : cro.submissionReference ?? "";
}
