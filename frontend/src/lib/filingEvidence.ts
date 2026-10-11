import { ApiError, type FilingReadinessProfile } from "@/lib/api";

/**
 * The professional bodies the backend accepts for the optional accountant review, by exact name
 * (AccountantCredentialChecks.RecognisedProfessionalBodies; a backend test keeps the lists equal).
 */
export const ACCOUNTANT_PROFESSIONAL_BODIES = [
  "Chartered Accountants Ireland",
  "Institute of Certified Public Accountants in Ireland",
  "Association of Chartered Certified Accountants",
  "Chartered Institute of Management Accountants",
  "Institute of Chartered Accountants in England and Wales",
  "Institute of Chartered Accountants of Scotland",
] as const;

/** True when the board's s.324 approval is recorded and still current for the generated accounts. */
export function directorsApprovalRecorded(profile: FilingReadinessProfile | null | undefined): boolean {
  return profile?.requiredEvidence.find((item) => item.code === "directors-approval")?.satisfied === true;
}

/** Short label for gate tiles: "Recorded" once the s.324 approval is on file, otherwise "Required". */
export function directorsApprovalGateLabel(profile: FilingReadinessProfile | null | undefined): string {
  return directorsApprovalRecorded(profile) ? "Recorded" : "Required";
}

/** The signed auditor's report is only asked for when the readiness profile still lists it as unsatisfied. */
export function auditReportOutstanding(profile: FilingReadinessProfile | null | undefined): boolean {
  return profile?.requiredEvidence.some((item) => item.code === "audit-report" && !item.satisfied) === true;
}

export function hasNextAction(profile: FilingReadinessProfile | null | undefined, action: string): boolean {
  return profile?.allowedNextActions.includes(action) === true;
}

/** True when the server rejected a privileged write because the last TOTP ceremony is too old. */
export function isRecentMfaRequired(error: unknown): boolean {
  return error instanceof ApiError && error.status === 428;
}
