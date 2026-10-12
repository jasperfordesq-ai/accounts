import type { ToolDefinition } from './tools.js';
import { APPROVAL_NOTE, NR, PER } from './tool-helpers.js';
import {
  ACCOUNTANT_REVIEW_ATTESTATION_INPUT_FIELDS,
  AUDITOR_REPORT_INPUT_FIELDS,
  AUTHORITY_REQUEST_FIELDS,
  AUTHORITY_REVOCATION_REQUEST_FIELDS,
  CHARITY_FILING_STATUS_INPUT_FIELDS,
  CHARITY_REPORT_GENERATED_INPUT_FIELDS,
  CRO_NOT_DELIVERED_INPUT_FIELDS,
  CRO_PAYMENT_INPUT_FIELDS,
  CRO_SIGNING_PACK_INPUT_FIELDS,
  CRO_SNAPSHOT_INPUT_FIELDS,
  DIRECTORS_APPROVAL_PAGE_INPUT_FIELDS,
  DIRECTORS_APPROVAL_RECORD_INPUT_FIELDS,
  DIRECTORS_APPROVAL_WITHDRAWAL_INPUT_FIELDS,
  FILING_STATUS_INPUT_FIELDS,
  MARK_GENERATED_INPUT_FIELDS,
  OUTCOME_COMMAND_FIELDS,
  REVENUE_EXTERNAL_VALIDATION_INPUT_FIELDS,
  REVENUE_FILING_STATUS_INPUT_FIELDS,
  REVENUE_SNAPSHOT_INPUT_FIELDS,
} from './bodies.js';

/**
 * The filing workflow and the external filing handoff.
 *
 * Nothing here files anything. CRO and ROS final submission remain external
 * actions recorded in workflow state: a person files in CORE or ROS, and
 * these tools record what they did, with its reference and evidence.
 */
const RECORD_ONLY =
  ' This records an external action the person took; FilingBridge never files with the CRO or Revenue itself.';

const HANDOFF = `${PER}/external-filing-handoff`;
const HANDOFF_FULL_SCOPE =
  'Handoff documents carry officer and shareholder names and addresses, identity-evidence references and '
  + 'presenter emails inside nested field values that cannot be filtered reliably.';

export const FILING_TOOLS: readonly ToolDefinition[] = [
  {
    name: 'filing_status',
    group: 'filing',
    description:
      'The filing workflow for the period: CRO, Revenue and charity status, what is generated and approved, and '
      + 'every blocker. Read this before recording any filing step.',
    path: `${PER}/filing/status`,
    noRecordsBecause: NR.workflow,
  },
  {
    name: 'filing_readiness_profile',
    group: 'filing',
    description: 'What this period must produce and file under its regime and company type, and how ready each item is.',
    path: `${PER}/filing/readiness-profile`,
    noRecordsBecause: NR.workflow,
  },
  {
    name: 'cro_filing_status_set',
    group: 'filing',
    description:
      'Move the CRO filing through its workflow (in progress, ready for review, approved, recorded as filed in '
      + 'CORE with its reference, accepted, rejected, correction required). Each step has preconditions; read '
      + 'filing_status first.' + RECORD_ONLY,
    method: 'PUT',
    path: `${PER}/filing/cro-status`,
    level: 'write',
    body: FILING_STATUS_INPUT_FIELDS,
    bodySchema: 'FilingStatusInput',
    noRecordsBecause: NR.workflow,
  },
  {
    name: 'cro_payment_confirm',
    group: 'filing',
    description: 'Record that the CORE filing fee was paid, after the filing is recorded as filed.' + RECORD_ONLY,
    method: 'POST',
    path: `${PER}/filing/cro-payment`,
    level: 'write',
    body: CRO_PAYMENT_INPUT_FIELDS,
    bodySchema: 'CroPaymentInput',
    noRecordsBecause: NR.workflow,
  },
  {
    name: 'cro_not_delivered_record',
    group: 'filing',
    description:
      'Treat a CRO filing that was sent back or rejected as not delivered, so the late-filing position is '
      + 'assessed from the original date. Owner or Reviewer.' + RECORD_ONLY + APPROVAL_NOTE,
    method: 'POST',
    path: `${PER}/filing/cro-not-delivered`,
    level: 'admin',
    approval: true,
    body: CRO_NOT_DELIVERED_INPUT_FIELDS,
    bodySchema: 'CroNotDeliveredInput',
    noRecordsBecause: NR.workflow,
  },
  {
    name: 'charity_report_generated_record',
    group: 'filing',
    description: 'Record that the charity SoFA or Trustees\' Annual Report was generated for the annual return pack.',
    method: 'POST',
    path: `${PER}/filing/charity-report-generated`,
    level: 'write',
    body: CHARITY_REPORT_GENERATED_INPUT_FIELDS,
    bodySchema: 'CharityReportGeneratedInput',
    noRecordsBecause: NR.workflow,
  },
  {
    name: 'charity_filing_status_set',
    group: 'filing',
    description: 'Move the Charities Regulator annual return through its workflow, with its reference.' + RECORD_ONLY,
    method: 'PUT',
    path: `${PER}/filing/charity-status`,
    level: 'write',
    body: CHARITY_FILING_STATUS_INPUT_FIELDS,
    bodySchema: 'CharityFilingStatusInput',
    noRecordsBecause: NR.workflow,
  },
  {
    name: 'revenue_external_validation_record',
    group: 'filing',
    description:
      'Record that the final iXBRL file was validated outside FilingBridge (for example with Revenue\'s test '
      + 'facility), binding the record to the file\'s SHA-256.' + RECORD_ONLY + APPROVAL_NOTE,
    method: 'POST',
    path: `${PER}/filing/revenue-external-validation`,
    level: 'admin',
    approval: true,
    body: REVENUE_EXTERNAL_VALIDATION_INPUT_FIELDS,
    bodySchema: 'RevenueExternalValidationInput',
    noRecordsBecause: NR.workflow,
  },
  {
    name: 'revenue_filing_status_set',
    group: 'filing',
    description: 'Move the Revenue CT1 return through its workflow, with the ROS notice number.' + RECORD_ONLY,
    method: 'PUT',
    path: `${PER}/filing/revenue-status`,
    level: 'write',
    body: REVENUE_FILING_STATUS_INPUT_FIELDS,
    bodySchema: 'RevenueFilingStatusInput',
    noRecordsBecause: NR.workflow,
  },
  {
    name: 'cro_document_generated_mark',
    group: 'filing',
    description:
      'Retired: FilingBridge now records CRO document generation itself when cro_filing_pack_generate or '
      + 'signature_page_generate runs, and refuses this. Kept so the route is accounted for.',
    method: 'POST',
    path: `${PER}/filing/mark-generated`,
    level: 'write',
    body: MARK_GENERATED_INPUT_FIELDS,
    bodySchema: 'MarkGeneratedInput',
    noRecordsBecause: NR.workflow,
  },
  {
    name: 'ixbrl_validate',
    group: 'filing',
    description: 'Run FilingBridge\'s own validation of the generated iXBRL and record the result.',
    method: 'POST',
    path: `${PER}/filing/validate-ixbrl`,
    level: 'write',
    noRecordsBecause: NR.workflow,
  },
  {
    name: 'directors_approval_get',
    group: 'filing',
    description: 'The directors\' approval of the financial statements (s.324): current state, approval pages and history.',
    path: `${PER}/filing/directors-approval`,
    params: [{ kind: 'dateQuery', name: 'onDate', describe: 'Show the state as it stood on this date.' }],
    noRecordsBecause: NR.officerNames,
  },
  {
    name: 'directors_approval_page_generate',
    group: 'filing',
    description: 'Generate the directors\' approval page for signature, naming the signing directors and the approval date.',
    method: 'POST',
    path: `${PER}/filing/directors-approval-page`,
    level: 'write',
    body: DIRECTORS_APPROVAL_PAGE_INPUT_FIELDS,
    bodySchema: 'DirectorsApprovalPageInput',
    noRecordsBecause: NR.officerNames,
  },
  {
    name: 'directors_approval_page_download',
    group: 'filing',
    description: 'Save a generated directors\' approval page PDF into --download-dir, for the directors to sign.',
    path: `${PER}/filing/directors-approval-page/{pageId}`,
    binary: { kind: 'pdf', defaultName: 'directors-approval-page.pdf' },
    requiresFullScope: 'A generated document cannot be filtered field by field.',
  },
  {
    name: 'directors_approval_record',
    group: 'filing',
    description:
      'Record the directors\' signed approval (s.324) with the signed page and the board resolution, both from '
      + '--upload-root.' + APPROVAL_NOTE,
    method: 'POST',
    path: `${PER}/filing/directors-approval`,
    level: 'admin',
    approval: true,
    body: DIRECTORS_APPROVAL_RECORD_INPUT_FIELDS,
    bodySchema: 'DirectorsApprovalRecordInput',
    noRecordsBecause: NR.officerNames,
  },
  {
    name: 'directors_approval_withdraw',
    group: 'filing',
    description: 'Withdraw the recorded directors\' approval, for example before changing the approval date.' + APPROVAL_NOTE,
    method: 'POST',
    path: `${PER}/filing/directors-approval-withdrawal`,
    level: 'admin',
    approval: true,
    body: DIRECTORS_APPROVAL_WITHDRAWAL_INPUT_FIELDS,
    bodySchema: 'DirectorsApprovalWithdrawalInput',
    noRecordsBecause: NR.officerNames,
  },
  {
    name: 'cro_signing_pack_record',
    group: 'filing',
    description: 'Record the signed CRO signature pack (director and secretary), from --upload-root.' + APPROVAL_NOTE,
    method: 'POST',
    path: `${PER}/filing/cro-signing-pack`,
    level: 'admin',
    approval: true,
    body: CRO_SIGNING_PACK_INPUT_FIELDS,
    bodySchema: 'CroSigningPackInput',
    noRecordsBecause: NR.officerNames,
  },
  {
    name: 'auditor_report_record',
    group: 'filing',
    description: 'Record the statutory auditor\'s signed report for an audited company, from --upload-root.' + APPROVAL_NOTE,
    method: 'POST',
    path: `${PER}/filing/auditor-report`,
    level: 'admin',
    approval: true,
    body: AUDITOR_REPORT_INPUT_FIELDS,
    bodySchema: 'AuditorReportInput',
    noRecordsBecause: 'The auditor and firm named on a published audit report.',
  },
  {
    name: 'accountant_review_record',
    group: 'filing',
    description: 'Record the qualified accountant\'s review attestation, optionally with the review letter from --upload-root.',
    method: 'POST',
    path: `${PER}/filing/accountant-review`,
    level: 'write',
    body: ACCOUNTANT_REVIEW_ATTESTATION_INPUT_FIELDS,
    bodySchema: 'AccountantReviewAttestationInput',
    noRecordsBecause: 'The reviewing accountant\'s professional attestation.',
  },
];

export const HANDOFF_TOOLS: readonly ToolDefinition[] = [
  {
    name: 'handoff_workspace',
    group: 'handoff',
    description:
      'The external filing handoff workspace: the prepared CRO B1 and CT1 support facts, recorded filing '
      + 'authorities, snapshots and outcomes, and gaps. Direct CRO and ROS submission are not supported.',
    path: `${HANDOFF}/workspace`,
    requiresFullScope: HANDOFF_FULL_SCOPE,
  },
  {
    name: 'handoff_authority_record',
    group: 'handoff',
    description: 'Record the evidence of a presenter\'s or agent\'s authority to act for the company at the CRO or in ROS.' + APPROVAL_NOTE,
    method: 'POST',
    path: `${HANDOFF}/authorities`,
    level: 'admin',
    approval: true,
    body: AUTHORITY_REQUEST_FIELDS,
    bodySchema: 'AuthorityRequest',
    requiresFullScope: HANDOFF_FULL_SCOPE,
  },
  {
    name: 'handoff_authority_revoke',
    group: 'handoff',
    description: 'Revoke a recorded filing authority.' + APPROVAL_NOTE,
    method: 'POST',
    path: `${HANDOFF}/authorities/{authorityId}/revoke`,
    level: 'admin',
    approval: true,
    body: AUTHORITY_REVOCATION_REQUEST_FIELDS,
    bodySchema: 'AuthorityRevocationRequest',
    requiresFullScope: HANDOFF_FULL_SCOPE,
  },
  {
    name: 'handoff_cro_snapshot_create',
    group: 'handoff',
    description:
      'Freeze a hash-bound CRO B1 handoff snapshot: the facts the person will key into CORE, with officer and '
      + 'shareholder particulars.' + RECORD_ONLY + APPROVAL_NOTE,
    method: 'POST',
    path: `${HANDOFF}/cro/snapshots`,
    level: 'admin',
    approval: true,
    body: CRO_SNAPSHOT_INPUT_FIELDS,
    bodySchema: 'CroSnapshotInput',
    requiresFullScope: HANDOFF_FULL_SCOPE,
  },
  {
    name: 'handoff_revenue_snapshot_create',
    group: 'handoff',
    description: 'Freeze a hash-bound CT1 support handoff snapshot for the person to complete in ROS.' + RECORD_ONLY + APPROVAL_NOTE,
    method: 'POST',
    path: `${HANDOFF}/revenue/snapshots`,
    level: 'admin',
    approval: true,
    body: REVENUE_SNAPSHOT_INPUT_FIELDS,
    bodySchema: 'RevenueSnapshotInput',
    requiresFullScope: HANDOFF_FULL_SCOPE,
  },
  {
    name: 'handoff_snapshot_download',
    group: 'handoff',
    description: 'Save a handoff snapshot\'s artifact (JSON) into --download-dir and report its SHA-256.',
    path: `${HANDOFF}/snapshots/{snapshotId}/artifact`,
    params: [{ kind: 'guid', name: 'snapshotId', describe: 'The snapshot, as handoff_workspace lists it.' }],
    binary: { kind: 'json', defaultName: 'handoff-snapshot.json' },
    requiresFullScope: HANDOFF_FULL_SCOPE,
  },
  {
    name: 'handoff_outcome_record',
    group: 'handoff',
    description:
      'Record what happened to a handoff snapshot outside FilingBridge: ready for manual handoff, filed '
      + 'externally, correction required, rejected, accepted, or superseded.' + RECORD_ONLY + APPROVAL_NOTE,
    method: 'POST',
    path: `${HANDOFF}/snapshots/{snapshotId}/outcomes`,
    params: [{ kind: 'guid', name: 'snapshotId', describe: 'The snapshot, as handoff_workspace lists it.' }],
    level: 'admin',
    approval: true,
    body: OUTCOME_COMMAND_FIELDS,
    bodySchema: 'OutcomeCommand',
    requiresFullScope: HANDOFF_FULL_SCOPE,
  },
];
