import { z } from "zod";

export const externalFilingWorkflowSchema = z.enum(["CroB1", "RevenueCt1Support"]);
export const externalFilingAuthorityKindSchema = z.enum([
  "CroPresenter",
  "CroElectronicFilingAgent",
  "RevenueRosAgent",
]);
export const externalFilingAuthorityStatusSchema = z.enum([
  "Draft",
  "Pending",
  "Active",
  "Revoked",
  "Expired",
]);
export const externalHandoffFieldStatusSchema = z.enum([
  "Complete",
  "Missing",
  "RequiresReview",
  "ProtectedManualEntry",
  "NotApplicable",
]);
export const externalFilingOutcomeKindSchema = z.enum([
  "ReadyForManualHandoff",
  "ExternallySubmittedRecorded",
  "CorrectionRequired",
  "ExternallyRejected",
  "ExternallyAcceptedRecorded",
  "SupersededByAmendment",
]);

export const sha256Schema = z.string().regex(/^[a-f0-9]{64}$/i, "Expected a SHA-256 digest");
const positiveIdSchema = z.number().int().positive();
const utcDateTimeSchema = z.string().datetime({ offset: true });
const obviousPiiReference = /(?:^|[^A-Za-z0-9])\d{7}[A-Za-z]{1,2}(?:$|[^A-Za-z0-9])|\b(?:19|20)\d{2}-\d{1,2}-\d{1,2}\b|\b\d{1,2}[/-]\d{1,2}[/-](?:19|20)\d{2}\b|\b[^\s@]+@[^\s@]+\.[^\s@]+\b/i;
const opaqueReferenceSchema = (minimum = 1, maximum = 1000) => z.string().min(minimum).max(maximum).refine(
  (value) => !obviousPiiReference.test(value),
  "Expected an opaque non-PII reference, not a raw PPSN, date of birth or email address",
);
const actorSchema = z.object({
  userId: z.string().min(1).max(200),
  displayName: z.string().min(2).max(200),
  role: z.string().min(2).max(100),
}).strict();

export const externalFilingAuthoritySchema = z.object({
  authorityId: positiveIdSchema,
  tenantId: positiveIdSchema,
  companyId: positiveIdSchema,
  workflow: externalFilingWorkflowSchema,
  kind: externalFilingAuthorityKindSchema,
  status: externalFilingAuthorityStatusSchema,
  legalName: z.string().min(2).max(300),
  practiceName: z.string().max(300).nullable(),
  maskedPresenterOrTain: z.string().max(100).nullable().refine(
    (value) => value === null || value.length === 0 || value.includes("*") || !/\d/.test(value),
    "Presenter/TAIN identifiers exposed to the UI must be masked",
  ),
  authorityScope: z.string().min(3).max(1000),
  engagementReference: opaqueReferenceSchema(3, 500),
  externalAuthorityReference: opaqueReferenceSchema(3, 500),
  effectiveFromUtc: utcDateTimeSchema,
  effectiveUntilUtc: utcDateTimeSchema.nullable(),
  revokedAtUtc: utcDateTimeSchema.nullable(),
  authorityEvidenceSha256: sha256Schema,
  evidenceMediaType: z.string().min(3).max(100),
  evidenceFileName: z.string().min(1).max(255),
  reviewedBy: actorSchema,
  reviewedAtUtc: utcDateTimeSchema,
  releaseCandidate: z.string().min(3).max(200),
}).strict();

export const externalHandoffAddressSchema = z.object({
  line1: z.string().nullable(),
  line2: z.string().nullable(),
  line3: z.string().nullable(),
  line4: z.string().nullable(),
  line5: z.string().nullable(),
  line6: z.string().nullable(),
  line7: z.string().nullable(),
}).strict();

const b1OfficerSchema = z.object({
  officerId: positiveIdSchema,
  firstName: z.string(),
  lastName: z.string(),
  role: z.string(),
  appointedDate: z.string().nullable(),
  resignedDate: z.string().nullable(),
  address: externalHandoffAddressSchema,
  identityType: z.string(),
  identityEvidenceReference: opaqueReferenceSchema(),
  identityEvidenceSha256: sha256Schema,
  presenterNotificationEmail: z.string().nullable(),
  otherDirectorshipsEvidenceReference: opaqueReferenceSchema(),
  protectedIdentifierEntryRequired: z.literal(true),
}).strict();

const b1FactsSchema = z.object({
  croNumber: z.string(),
  legalName: z.string(),
  companyType: z.string(),
  annualReturnDate: z.string(),
  madeUpToDate: z.string(),
  annualReturnDateElection: z.string(),
  registeredOffice: externalHandoffAddressSchema,
  financialYearStart: z.string(),
  financialYearEnd: z.string(),
  financialStatementsAnnexed: z.boolean(),
  firstAnnualReturn: z.boolean(),
  auditExemptionClaimed: z.boolean(),
  auditorReference: opaqueReferenceSchema().nullable(),
  reportingCurrency: z.string(),
  politicalDonationsOverThreshold: z.boolean(),
  politicalDonationsAmount: z.number().nonnegative(),
  politicalDonationsEvidenceReference: opaqueReferenceSchema(),
  directorSignatory: z.string(),
  secretarySignatory: z.string(),
  officers: z.array(b1OfficerSchema),
  shareClasses: z.array(z.object({
    shareClass: z.string(),
    currency: z.string(),
    nominalValue: z.number().nonnegative(),
    numberIssued: z.number().int().nonnegative(),
    totalNominalValue: z.number().nonnegative(),
    amountPaid: z.number().nonnegative(),
    amountUnpaid: z.number().nonnegative(),
  }).strict()),
  shareholders: z.array(z.object({
    memberReference: opaqueReferenceSchema(),
    name: z.string(),
    address: externalHandoffAddressSchema,
    shareClass: z.string(),
    currency: z.string(),
    openingHolding: z.number().nonnegative(),
    closingHolding: z.number().nonnegative(),
    holdingDisplay: z.string(),
    evidenceReference: opaqueReferenceSchema(),
  }).strict()),
  allotments: z.array(z.object({
    allotmentReference: opaqueReferenceSchema(),
    allotmentDate: z.string(),
    shareClass: z.string(),
    currency: z.string(),
    numberAllotted: z.number().int().positive(),
    nominalValuePerShare: z.number().nonnegative(),
    consideration: z.number().nonnegative(),
    allotteeMemberReference: opaqueReferenceSchema(),
    evidenceReference: opaqueReferenceSchema(),
  }).strict()),
  accountsPdfSha256: sha256Schema,
  signaturePageSha256: sha256Schema,
  shareholdersListPdfSha256: sha256Schema.nullable(),
}).strict();

const revenueSupportFactsSchema = z.object({
  companyName: z.string(),
  taxReference: z.string(),
  periodStart: z.string(),
  periodEnd: z.string(),
  outputKind: z.literal("corporation-tax-support-data-not-ct1-return"),
  isCompleteCt1Return: z.literal(false),
  calculationSha256: sha256Schema,
  worksheetArtifactSha256: sha256Schema,
  ixbrlArtifactSha256: sha256Schema.nullable(),
  externalValidationEvidenceSha256: sha256Schema.nullable(),
  externalValidationReference: opaqueReferenceSchema().nullable(),
  corporationTaxDue: z.number().nonnegative(),
  preliminaryTaxPaid: z.number().nonnegative(),
  balanceDue: z.number(),
  supportStatus: z.string(),
  supportBlockingReasons: z.array(z.string()),
  manualCt1CompletionItems: z.array(z.string()),
}).strict();

const handoffFieldSchema = z.object({
  fieldCode: z.string().min(3).max(150),
  section: z.string().min(2).max(200),
  label: z.string().min(2).max(300),
  value: z.string().max(4000).nullable(),
  status: externalHandoffFieldStatusSchema,
  sourceReference: opaqueReferenceSchema(2, 1000),
  blockingReason: z.string().max(2000).nullable(),
  isProtectedManualEntry: z.boolean(),
}).strict();

const attachmentSchema = z.object({
  code: z.string(),
  fileName: z.string(),
  mediaType: z.string(),
  byteSize: z.number().int().positive(),
  sha256: sha256Schema,
  sourceReference: opaqueReferenceSchema(),
}).strict();

const sourceSchema = z.object({
  code: z.string(),
  title: z.string(),
  url: z.string().url(),
  relevance: z.string(),
  effectiveDate: z.string().min(3),
  reviewedAtUtc: utcDateTimeSchema,
}).strict();

export const externalFilingHandoffDocumentSchema = z.object({
  schemaVersion: z.literal("external-filing-handoff-v1"),
  snapshotId: z.string().uuid(),
  version: z.number().int().positive(),
  supersedesSnapshotId: z.string().uuid().nullable(),
  supersedesArtifactSha256: sha256Schema.nullable(),
  amendmentReason: z.string().nullable(),
  tenantId: positiveIdSchema,
  companyId: positiveIdSchema,
  periodId: positiveIdSchema,
  workflow: externalFilingWorkflowSchema,
  periodStart: z.string(),
  periodEnd: z.string(),
  preparedAtUtc: utcDateTimeSchema,
  preparedBy: actorSchema,
  authority: externalFilingAuthoritySchema,
  qualifiedReviewManifestSha256: sha256Schema,
  releaseCandidate: z.string().min(3),
  directSubmissionSupported: z.literal(false),
  isCompleteExternalReturn: z.literal(false),
  readyForManualHandoff: z.boolean(),
  sourceFingerprintSha256: sha256Schema,
  croB1: b1FactsSchema.nullable(),
  revenueCt1Support: revenueSupportFactsSchema.nullable(),
  fields: z.array(handoffFieldSchema),
  attachments: z.array(attachmentSchema),
  blockingIssues: z.array(z.string()),
  externalCompletionWarnings: z.array(z.string()),
  sources: z.array(sourceSchema),
}).strict().superRefine((document, context) => {
  if (document.authority.tenantId !== document.tenantId
    || document.authority.companyId !== document.companyId
    || document.authority.workflow !== document.workflow) {
    context.addIssue({ code: "custom", path: ["authority"], message: "Authority scope does not match the snapshot" });
  }
  if (document.workflow === "CroB1" && (!document.croB1 || document.revenueCt1Support)) {
    context.addIssue({ code: "custom", path: ["croB1"], message: "CRO snapshots require only B1 facts" });
  }
  if (document.workflow === "RevenueCt1Support" && (!document.revenueCt1Support || document.croB1)) {
    context.addIssue({ code: "custom", path: ["revenueCt1Support"], message: "Revenue snapshots require only support-only CT1 facts" });
  }
  if ((document.supersedesSnapshotId === null) !== (document.supersedesArtifactSha256 === null)) {
    context.addIssue({ code: "custom", path: ["supersedesSnapshotId"], message: "Amendment predecessor ID and hash must be supplied together" });
  }
});

export const externalFilingSnapshotSchema = z.object({
  document: externalFilingHandoffDocumentSchema,
  artifactSha256: sha256Schema,
}).strict();

export const externalFilingOutcomeSchema = z.object({
  eventId: positiveIdSchema,
  snapshotId: z.string().uuid(),
  snapshotArtifactSha256: sha256Schema,
  outcome: externalFilingOutcomeKindSchema,
  externalReference: opaqueReferenceSchema(4, 500).nullable(),
  externalOccurredAtUtc: utcDateTimeSchema.nullable(),
  reason: z.string().nullable(),
  correctionDeadlineUtc: utcDateTimeSchema.nullable(),
  evidenceReference: opaqueReferenceSchema(4, 1000).nullable(),
  evidenceSha256: sha256Schema.nullable(),
  supersedingSnapshotId: z.string().uuid().nullable(),
  supersedingSnapshotArtifactSha256: sha256Schema.nullable(),
  recordedBy: actorSchema,
  recordedAtUtc: utcDateTimeSchema,
}).strict().superRefine((event, context) => {
  const externalFields = [event.externalReference, event.externalOccurredAtUtc, event.evidenceReference, event.evidenceSha256];
  const successorFields = [event.supersedingSnapshotId, event.supersedingSnapshotArtifactSha256];
  if (event.outcome === "ReadyForManualHandoff") {
    if (externalFields.some((value) => value !== null) || successorFields.some((value) => value !== null) || event.correctionDeadlineUtc !== null) {
      context.addIssue({ code: "custom", path: ["outcome"], message: "Internal readiness cannot carry fabricated external or successor evidence" });
    }
    return;
  }
  if (event.outcome === "SupersededByAmendment") {
    if (externalFields.some((value) => value !== null) || event.correctionDeadlineUtc !== null) {
      context.addIssue({ code: "custom", path: ["outcome"], message: "Snapshot supersession is internal and cannot carry fabricated external evidence" });
    }
    if (successorFields.some((value) => value === null)) {
      context.addIssue({ code: "custom", path: ["supersedingSnapshotId"], message: "Snapshot supersession requires the exact new snapshot identity and hash" });
    }
    return;
  }
  if (externalFields.some((value) => value === null)) {
    context.addIssue({ code: "custom", path: ["externalReference"], message: "External outcomes require reference, timestamp and exact evidence hash" });
  }
  if (successorFields.some((value) => value !== null)) {
    context.addIssue({ code: "custom", path: ["supersedingSnapshotId"], message: "Only snapshot supersession may reference a successor" });
  }
  if ((event.outcome === "CorrectionRequired" || event.outcome === "ExternallyRejected") && !event.reason) {
    context.addIssue({ code: "custom", path: ["reason"], message: "Corrections and rejections require a reason" });
  }
  if (event.outcome === "CorrectionRequired" && event.correctionDeadlineUtc === null) {
    context.addIssue({ code: "custom", path: ["correctionDeadlineUtc"], message: "Correction events require a deadline" });
  }
});

export const externalFilingHandoffWorkspaceSchema = z.object({
  tenantId: positiveIdSchema,
  companyId: positiveIdSchema,
  periodId: positiveIdSchema,
  directCroSubmissionSupported: z.literal(false),
  directRosSubmissionSupported: z.literal(false),
  preparation: z.object({
    legalName: z.string().min(1),
    croNumber: z.string().nullable(),
    taxReference: z.string().nullable(),
    periodStart: z.string().regex(/^\d{4}-\d{2}-\d{2}$/),
    periodEnd: z.string().regex(/^\d{4}-\d{2}-\d{2}$/),
    annualReturnDate: z.string().regex(/^\d{4}-\d{2}-\d{2}$/).nullable(),
    registeredOffice: externalHandoffAddressSchema,
    reportingCurrency: z.string().regex(/^[A-Z]{3}$/),
    officers: z.array(z.object({
      officerId: positiveIdSchema,
      name: z.string().min(1),
      role: z.string().min(1),
      appointedDate: z.string().regex(/^\d{4}-\d{2}-\d{2}$/).nullable(),
      resignedDate: z.string().regex(/^\d{4}-\d{2}-\d{2}$/).nullable(),
      sourceAddress: z.string().nullable(),
    }).strict()),
  }).strict(),
  authorities: z.array(externalFilingAuthoritySchema),
  snapshots: z.array(externalFilingSnapshotSchema),
  outcomes: z.array(externalFilingOutcomeSchema),
  sourceGaps: z.array(z.string()),
}).strict().superRefine((workspace, context) => {
  workspace.authorities.forEach((authority, index) => {
    if (authority.tenantId !== workspace.tenantId || authority.companyId !== workspace.companyId) {
      context.addIssue({ code: "custom", path: ["authorities", index], message: "Authority belongs to another tenant or company" });
    }
  });
  workspace.snapshots.forEach((snapshot, index) => {
    const document = snapshot.document;
    if (document.tenantId !== workspace.tenantId
      || document.companyId !== workspace.companyId
      || document.periodId !== workspace.periodId) {
      context.addIssue({ code: "custom", path: ["snapshots", index], message: "Snapshot belongs to another tenant, company or period" });
    }
  });
  const snapshotsById = new Map(workspace.snapshots.map((snapshot) => [snapshot.document.snapshotId, snapshot]));
  if (snapshotsById.size !== workspace.snapshots.length) {
    context.addIssue({ code: "custom", path: ["snapshots"], message: "Snapshot identities must be unique" });
  }
  workspace.snapshots.forEach((snapshot, index) => {
    const document = snapshot.document;
    if (document.version === 1 && (document.supersedesSnapshotId || document.supersedesArtifactSha256)) {
      context.addIssue({ code: "custom", path: ["snapshots", index], message: "Initial snapshots cannot carry a predecessor" });
    }
    if (document.version > 1) {
      const predecessor = document.supersedesSnapshotId ? snapshotsById.get(document.supersedesSnapshotId) : undefined;
      if (!predecessor
        || predecessor.document.workflow !== document.workflow
        || predecessor.document.version !== document.version - 1
        || predecessor.artifactSha256.toLowerCase() !== document.supersedesArtifactSha256?.toLowerCase()) {
        context.addIssue({ code: "custom", path: ["snapshots", index, "document", "supersedesSnapshotId"], message: "Amendment predecessor identity, version, workflow and hash must match exactly" });
      }
    }
  });
  workspace.outcomes.forEach((outcome, index) => {
    const snapshot = snapshotsById.get(outcome.snapshotId);
    if (!snapshot || snapshot.artifactSha256.toLowerCase() !== outcome.snapshotArtifactSha256.toLowerCase()) {
      context.addIssue({ code: "custom", path: ["outcomes", index], message: "External outcome must bind an exact retained snapshot hash" });
    }
    if (outcome.outcome === "SupersededByAmendment") {
      const successor = outcome.supersedingSnapshotId
        ? snapshotsById.get(outcome.supersedingSnapshotId)
        : undefined;
      if (!snapshot
        || !successor
        || successor.artifactSha256.toLowerCase() !== outcome.supersedingSnapshotArtifactSha256?.toLowerCase()
        || successor.document.workflow !== snapshot.document.workflow
        || successor.document.version !== snapshot.document.version + 1
        || successor.document.supersedesSnapshotId !== snapshot.document.snapshotId
        || successor.document.supersedesArtifactSha256?.toLowerCase() !== snapshot.artifactSha256.toLowerCase()) {
        context.addIssue({ code: "custom", path: ["outcomes", index, "supersedingSnapshotId"], message: "Supersession must bind the exact next snapshot and predecessor hash" });
      }
    }
  });
});

const base64EvidenceSchema = z.string()
  .min(4)
  .max(16_000_000)
  .regex(/^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/, "Expected base64 evidence bytes");

export const externalFilingAuthorityRequestSchema = z.object({
  workflow: externalFilingWorkflowSchema,
  kind: externalFilingAuthorityKindSchema,
  legalName: z.string().trim().min(2).max(300),
  practiceName: z.string().trim().max(300).nullable(),
  maskedPresenterOrTain: z.string().trim().max(100).nullable().refine(
    (value) => value === null || value.length === 0 || value.includes("*") || !/\d/.test(value),
    "Presenter/TAIN identifiers sent through the general handoff API must be masked",
  ),
  authorityScope: z.string().trim().min(3).max(1000),
  engagementReference: opaqueReferenceSchema(3, 500),
  externalAuthorityReference: opaqueReferenceSchema(3, 500),
  effectiveFromUtc: utcDateTimeSchema,
  effectiveUntilUtc: utcDateTimeSchema.nullable(),
  evidenceArtifact: base64EvidenceSchema,
  evidenceSha256: sha256Schema,
  evidenceMediaType: z.string().trim().min(3).max(100),
  evidenceFileName: z.string().trim().min(1).max(255),
}).strict().superRefine((input, context) => {
  const validKind = input.workflow === "CroB1"
    ? input.kind === "CroPresenter" || input.kind === "CroElectronicFilingAgent"
    : input.kind === "RevenueRosAgent";
  if (!validKind) {
    context.addIssue({ code: "custom", path: ["kind"], message: "Authority kind must match its workflow" });
  }
  if (input.effectiveUntilUtc && input.effectiveUntilUtc <= input.effectiveFromUtc) {
    context.addIssue({ code: "custom", path: ["effectiveUntilUtc"], message: "Authority end must follow its start" });
  }
});

export const externalFilingAuthorityRevocationRequestSchema = z.object({
  reason: z.string().trim().min(10).max(2000),
}).strict();

const croOfficerInputSchema = z.object({
  officerId: positiveIdSchema,
  firstName: z.string().trim().min(1).max(150),
  lastName: z.string().trim().min(1).max(150),
  address: externalHandoffAddressSchema,
  identityType: z.string().trim().min(2).max(100),
  identityEvidenceReference: opaqueReferenceSchema(3, 500),
  identityEvidenceSha256: sha256Schema,
  presenterNotificationEmail: z.string().email().max(320).nullable(),
  otherDirectorshipsEvidenceReference: opaqueReferenceSchema(3, 500),
  protectedIdentifierEntryConfirmed: z.boolean(),
}).strict();

const b1ShareholderInputSchema = z.object({
  memberReference: opaqueReferenceSchema(3, 500),
  name: z.string().trim().min(2).max(300),
  address: externalHandoffAddressSchema,
  shareClass: z.string().trim().min(1).max(100),
  currency: z.string().trim().regex(/^[A-Z]{3}$/),
  openingHolding: z.number().finite().nonnegative(),
  closingHolding: z.number().finite().nonnegative(),
  holdingDisplay: z.string().trim().min(1).max(500),
  evidenceReference: opaqueReferenceSchema(3, 500),
}).strict();

const b1AllotmentInputSchema = z.object({
  allotmentReference: opaqueReferenceSchema(3, 500),
  allotmentDate: z.string().regex(/^\d{4}-\d{2}-\d{2}$/),
  shareClass: z.string().trim().min(1).max(100),
  currency: z.string().trim().regex(/^[A-Z]{3}$/),
  numberAllotted: z.number().int().positive(),
  nominalValuePerShare: z.number().finite().nonnegative(),
  consideration: z.number().finite().nonnegative(),
  allotteeMemberReference: opaqueReferenceSchema(3, 500),
  evidenceReference: opaqueReferenceSchema(3, 500),
}).strict();

export const croHandoffSnapshotRequestSchema = z.object({
  madeUpToDate: z.string().regex(/^\d{4}-\d{2}-\d{2}$/),
  annualReturnDateElection: z.string().trim().min(2).max(200),
  financialStatementsAnnexed: z.boolean(),
  auditExemptionClaimed: z.boolean(),
  auditorReference: opaqueReferenceSchema(3, 500).nullable(),
  reportingCurrency: z.string().trim().regex(/^[A-Z]{3}$/),
  politicalDonationsOverThreshold: z.boolean(),
  politicalDonationsAmount: z.number().finite().nonnegative(),
  politicalDonationsEvidenceReference: opaqueReferenceSchema(3, 500),
  officers: z.array(croOfficerInputSchema).min(1),
  shareholders: z.array(b1ShareholderInputSchema).min(1),
  allotments: z.array(b1AllotmentInputSchema),
  noAllotmentsInReturnPeriodConfirmed: z.boolean(),
  shareholdersListPdfSha256: sha256Schema.nullable(),
  supersedesSnapshotId: z.string().uuid().nullable(),
  amendmentReason: z.string().trim().min(10).max(2000).nullable(),
}).strict().superRefine((input, context) => {
  if (input.allotments.length === 0 && !input.noAllotmentsInReturnPeriodConfirmed) {
    context.addIssue({ code: "custom", path: ["allotments"], message: "Retain allotments or explicitly confirm none in the return period" });
  }
  if ((input.supersedesSnapshotId === null) !== (input.amendmentReason === null)) {
    context.addIssue({ code: "custom", path: ["amendmentReason"], message: "Linked amendments require both predecessor identity and reason" });
  }
});

export const revenueHandoffSnapshotRequestSchema = z.object({
  asOfDate: z.string().regex(/^\d{4}-\d{2}-\d{2}$/).nullable(),
  unsupportedSectionsReviewed: z.boolean(),
  manualCt1CompletionItems: z.array(z.string().trim().min(2).max(1000)),
  supersedesSnapshotId: z.string().uuid().nullable(),
  amendmentReason: z.string().trim().min(10).max(2000).nullable(),
}).strict().superRefine((input, context) => {
  if ((input.supersedesSnapshotId === null) !== (input.amendmentReason === null)) {
    context.addIssue({ code: "custom", path: ["amendmentReason"], message: "Linked amendments require both predecessor identity and reason" });
  }
});

export const externalFilingOutcomeRequestSchema = z.object({
  outcome: externalFilingOutcomeKindSchema,
  externalReference: opaqueReferenceSchema(4, 500).nullable(),
  externalOccurredAtUtc: utcDateTimeSchema.nullable(),
  reason: z.string().trim().min(5).max(2000).nullable(),
  correctionDeadlineUtc: utcDateTimeSchema.nullable(),
  evidenceReference: opaqueReferenceSchema(4, 1000).nullable(),
  evidenceArtifact: base64EvidenceSchema.nullable(),
  evidenceSha256: sha256Schema.nullable(),
  supersedingSnapshotId: z.string().uuid().nullable(),
}).strict().superRefine((input, context) => {
  const external = input.outcome !== "ReadyForManualHandoff" && input.outcome !== "SupersededByAmendment";
  if (external && (!input.externalReference || !input.externalOccurredAtUtc || !input.evidenceReference || !input.evidenceArtifact || !input.evidenceSha256)) {
    context.addIssue({ code: "custom", path: ["externalReference"], message: "External outcomes require genuine reference, time and exact retained evidence" });
  }
  if (!external && [input.externalReference, input.externalOccurredAtUtc, input.evidenceReference, input.evidenceArtifact, input.evidenceSha256].some((value) => value !== null)) {
    context.addIssue({ code: "custom", path: ["outcome"], message: "Internal chronology events cannot assert fabricated external evidence" });
  }
  if (input.outcome === "SupersededByAmendment" && !input.supersedingSnapshotId) {
    context.addIssue({ code: "custom", path: ["supersedingSnapshotId"], message: "Supersession requires the exact successor snapshot" });
  }
  if (input.outcome !== "SupersededByAmendment" && input.supersedingSnapshotId) {
    context.addIssue({ code: "custom", path: ["supersedingSnapshotId"], message: "Only a supersession event may reference a successor" });
  }
  if (input.outcome === "CorrectionRequired" && !input.correctionDeadlineUtc) {
    context.addIssue({ code: "custom", path: ["correctionDeadlineUtc"], message: "Correction events require a deadline" });
  }
});

export type ExternalFilingWorkflow = z.infer<typeof externalFilingWorkflowSchema>;
export type ExternalFilingAuthority = z.infer<typeof externalFilingAuthoritySchema>;
export type ExternalFilingHandoffDocument = z.infer<typeof externalFilingHandoffDocumentSchema>;
export type ExternalFilingSnapshot = z.infer<typeof externalFilingSnapshotSchema>;
export type ExternalFilingOutcome = z.infer<typeof externalFilingOutcomeSchema>;
export type ExternalFilingHandoffWorkspace = z.infer<typeof externalFilingHandoffWorkspaceSchema>;
export type ExternalFilingAuthorityRequest = z.infer<typeof externalFilingAuthorityRequestSchema>;
export type ExternalFilingAuthorityRevocationRequest = z.infer<typeof externalFilingAuthorityRevocationRequestSchema>;
export type CroHandoffSnapshotRequest = z.infer<typeof croHandoffSnapshotRequestSchema>;
export type RevenueHandoffSnapshotRequest = z.infer<typeof revenueHandoffSnapshotRequestSchema>;
export type ExternalFilingOutcomeRequest = z.infer<typeof externalFilingOutcomeRequestSchema>;
