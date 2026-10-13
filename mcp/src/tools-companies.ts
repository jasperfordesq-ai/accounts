import type { ToolDefinition } from './tools.js';
import { APPROVAL_NOTE, CO, NR, PER, REPLACES } from './tool-helpers.js';
import {
  ACCOUNTING_PERIOD_INPUT_FIELDS,
  ANNUAL_RETURN_DATE_CHANGE_INPUT_FIELDS,
  COMPANY_INPUT_FIELDS,
  COMPANY_OFFICER_INPUT_FIELDS,
  COMPANY_ONBOARDING_INPUT_FIELDS,
  COMPANY_QUARANTINE_REQUEST_FIELDS,
  FILING_REGIME_INPUT_FIELDS,
  HISTORIC_ANCHOR_INPUT_FIELDS,
  HISTORIC_ANCHOR_RETIREMENT_INPUT_FIELDS,
  MEMBER_AUDIT_NOTICE_INPUT_FIELDS,
  PERIOD_STATUS_UPDATE_FIELDS,
  SIZE_CLASSIFICATION_INPUT_FIELDS,
  SIZE_CLASSIFICATION_OVERRIDE_INPUT_FIELDS,
} from './bodies.js';

const COMPANY = '/api/companies/{id}';

const COMPANY_UPDATE_FULL_SCOPE =
  'The update replaces the whole company, including the registered office address, which a Withheld '
  + 'session cannot read; sending the update without it would erase the address.';

const OFFICER_UPDATE_FULL_SCOPE =
  'The update replaces the whole officer, including the address, which a Withheld session cannot read; '
  + 'sending the update without it would erase the address.';

export const COMPANY_TOOLS: readonly ToolDefinition[] = [
  /* --- companies --------------------------------------------------------- */
  {
    name: 'company_list',
    group: 'companies',
    description:
      'Every company this account may access, with its type, size flags, period count and latest period. '
      + 'Start here to find a companyId. Withheld while the data scope is Withheld: the assigned reviewer\'s name and email.',
    path: '/api/companies',
    model: 'CompanyDashboardRow',
  },
  {
    name: 'company_get',
    group: 'companies',
    description:
      'One company in full: legal details, flags that drive size classification and filing obligations, its '
      + 'officers and its accounting periods. Withheld while the data scope is Withheld: the registered office '
      + 'address, officer addresses and the quarantine actor and reason.',
    path: COMPANY,
    model: 'Company',
  },
  {
    name: 'company_create',
    group: 'companies',
    description:
      'Create a company record in this workspace. Prefer company_onboard, which also creates the officers, the '
      + 'first accounting period, the first bank account and the chart of accounts in one step. Owner only.',
    method: 'POST',
    path: '/api/companies',
    level: 'admin',
    body: COMPANY_INPUT_FIELDS,
    bodySchema: 'CompanyInput',
    model: 'Company',
  },
  {
    name: 'company_onboard',
    group: 'companies',
    description:
      'Create a company together with its officers, its first accounting period, its opening bank account and '
      + 'its default chart of accounts, atomically. Retrying the same call is answered from the first result. Owner only.',
    method: 'POST',
    path: '/api/companies/onboard',
    level: 'admin',
    body: COMPANY_ONBOARDING_INPUT_FIELDS,
    bodySchema: 'CompanyOnboardingInput',
    noRecordsBecause: NR.officerNames,
  },
  {
    name: 'company_update',
    group: 'companies',
    description:
      'Change a company\'s legal details and flags. The Annual Return Date cannot change here (use '
      + 'company_annual_return_date_change), and the chart of accounts never changes on an update. Refused '
      + 'while the company\'s master data is locked by a finalised period.' + REPLACES,
    method: 'PUT',
    path: COMPANY,
    level: 'write',
    body: COMPANY_INPUT_FIELDS,
    bodySchema: 'CompanyInput',
    model: 'Company',
    requiresFullScope: COMPANY_UPDATE_FULL_SCOPE,
  },
  {
    name: 'company_delete',
    group: 'companies',
    description:
      'Quarantine a company: it disappears from every list and becomes read-only, with immutable evidence of '
      + 'who did it and why, and can be recovered with company_recover. Nothing is erased. Owner only; the '
      + 'confirmation must be the company\'s legal name exactly.' + APPROVAL_NOTE,
    method: 'DELETE',
    path: COMPANY,
    level: 'admin',
    approval: true,
    body: COMPANY_QUARANTINE_REQUEST_FIELDS,
    bodySchema: 'CompanyQuarantineRequest',
    noRecordsBecause: NR.ack,
  },
  {
    name: 'company_recover',
    group: 'companies',
    description:
      'Recover a quarantined company, restoring it to the lists. Owner only; the confirmation must be the '
      + 'legal name exactly.' + APPROVAL_NOTE,
    method: 'POST',
    path: `${COMPANY}/recover`,
    level: 'admin',
    approval: true,
    body: COMPANY_QUARANTINE_REQUEST_FIELDS,
    bodySchema: 'CompanyQuarantineRequest',
    noRecordsBecause: NR.ack,
  },
  {
    name: 'company_quarantined_list',
    group: 'companies',
    description:
      'Companies in quarantine, with when and the evidence digest. Owner only. Withheld while the data scope '
      + 'is Withheld: who quarantined each one and why.',
    path: '/api/companies/quarantined',
    level: 'admin',
    model: 'QuarantinedCompanySummary',
  },
  {
    name: 'company_annual_return_date_history',
    group: 'companies',
    description: 'The company\'s Annual Return Date history: each ARD, its legal source, evidence and when it took effect.',
    path: `${COMPANY}/annual-return-dates`,
    noRecordsBecause: NR.workflow,
  },
  {
    name: 'company_annual_return_date_change',
    group: 'companies',
    description:
      'Record an evidence-backed change of the Annual Return Date (for example a CRO-acknowledged B73 '
      + 'extension). Deadlines are recalculated from it.',
    method: 'POST',
    path: `${COMPANY}/annual-return-dates`,
    level: 'write',
    body: ANNUAL_RETURN_DATE_CHANGE_INPUT_FIELDS,
    bodySchema: 'AnnualReturnDateChangeInput',
    noRecordsBecause: NR.workflow,
  },

  /* --- officers ---------------------------------------------------------- */
  {
    name: 'officer_list',
    group: 'officers',
    description:
      'The company\'s directors and secretaries with their appointment and resignation dates. Withheld while the '
      + 'data scope is Withheld: addresses.',
    path: `${CO}/officers`,
    model: 'CompanyOfficer',
  },
  {
    name: 'officer_create',
    group: 'officers',
    description:
      'Add a director or secretary. Refused while the company\'s master data is locked. Leave address out '
      + 'unless the session has full data scope.',
    method: 'POST',
    path: `${CO}/officers`,
    level: 'write',
    body: COMPANY_OFFICER_INPUT_FIELDS,
    bodySchema: 'CompanyOfficerInput',
    model: 'CompanyOfficer',
  },
  {
    name: 'officer_update',
    group: 'officers',
    description: 'Change an officer, for example to record a resignation date.' + REPLACES,
    method: 'PUT',
    path: `${CO}/officers/{id}`,
    level: 'write',
    body: COMPANY_OFFICER_INPUT_FIELDS,
    bodySchema: 'CompanyOfficerInput',
    model: 'CompanyOfficer',
    requiresFullScope: OFFICER_UPDATE_FULL_SCOPE,
  },
  {
    name: 'officer_delete',
    group: 'officers',
    description:
      'Remove an officer recorded in error. A director who left should be given a resignation date instead, '
      + 'so the register keeps the history.' + APPROVAL_NOTE,
    method: 'DELETE',
    path: `${CO}/officers/{id}`,
    level: 'admin',
    approval: true,
    noRecordsBecause: NR.ack,
  },

  /* --- periods ----------------------------------------------------------- */
  {
    name: 'period_list',
    group: 'periods',
    description: 'The company\'s accounting periods, newest first, with status, size classification and filing regime. Find a periodId here.',
    path: `${CO}/periods`,
    noRecordsBecause: NR.workflow,
  },
  {
    name: 'period_get',
    group: 'periods',
    description: 'One accounting period with its status, lock, size classification and filing regime.',
    path: `${CO}/periods/{id}`,
    noRecordsBecause: NR.workflow,
  },
  {
    name: 'period_create',
    group: 'periods',
    description: 'Open a new accounting period (at most 18 months). Periods must follow on from each other without gaps or overlaps.',
    method: 'POST',
    path: `${CO}/periods`,
    level: 'write',
    body: ACCOUNTING_PERIOD_INPUT_FIELDS,
    bodySchema: 'AccountingPeriodInput',
    noRecordsBecause: NR.workflow,
  },
  {
    name: 'period_status_set',
    group: 'periods',
    description:
      'Move a period between Draft, Review, Finalised and Filed. Finalising locks it and needs final-output '
      + 'readiness; Filed needs every filing obligation recorded with its external reference; reopening a '
      + 'locked period needs an Owner and a reason.' + APPROVAL_NOTE,
    method: 'PUT',
    path: `${CO}/periods/{id}/status`,
    level: 'admin',
    approval: true,
    body: PERIOD_STATUS_UPDATE_FIELDS,
    bodySchema: 'PeriodStatusUpdate',
    noRecordsBecause: NR.workflow,
  },

  /* --- classification ---------------------------------------------------- */
  {
    name: 'size_figures_save',
    group: 'classification',
    description:
      'Save the period\'s turnover, balance sheet total and average employees for the Companies Act 2014 size '
      + 'test, without running it.',
    method: 'PUT',
    path: `${PER}/size-classification`,
    level: 'write',
    body: SIZE_CLASSIFICATION_INPUT_FIELDS,
    bodySchema: 'SizeClassificationInput',
    noRecordsBecause: NR.figures,
  },
  {
    name: 'size_classify',
    group: 'classification',
    description:
      'Run the size classification: the two-of-three threshold test (micro, small, medium) with the '
      + 'consecutive-year rule and the micro exclusions. Pass figures to save and classify in one step, or none '
      + 'to classify from the saved ones. See filingbridge://legal-framework for the thresholds.',
    method: 'POST',
    path: `${PER}/classify`,
    level: 'write',
    body: SIZE_CLASSIFICATION_INPUT_FIELDS,
    bodySchema: 'SizeClassificationInput',
    optionalBody: true,
    noRecordsBecause: NR.figures,
  },
  {
    name: 'classification_prerequisites',
    group: 'classification',
    description: 'What the size classification still needs: prior-period figures, a historic anchor, or saved figures.',
    path: `${PER}/classification/prerequisites`,
    noRecordsBecause: NR.workflow,
  },
  {
    name: 'historic_anchor_record',
    group: 'classification',
    description:
      'Record evidence-backed prior-year figures (a historic anchor) where FilingBridge holds no prior period, so '
      + 'the consecutive-year rule can be applied. Needs the evidence file under --upload-root.' + APPROVAL_NOTE,
    method: 'PUT',
    path: `${PER}/classification/historic-anchor`,
    level: 'admin',
    approval: true,
    body: HISTORIC_ANCHOR_INPUT_FIELDS,
    bodySchema: 'HistoricAnchorInput',
    noRecordsBecause: NR.figures,
  },
  {
    name: 'historic_anchor_retire',
    group: 'classification',
    description: 'Retire the current historic anchor; its evidence stays in the archive.' + APPROVAL_NOTE,
    method: 'POST',
    path: `${PER}/classification/historic-anchor/retire`,
    level: 'admin',
    approval: true,
    body: HISTORIC_ANCHOR_RETIREMENT_INPUT_FIELDS,
    bodySchema: 'HistoricAnchorRetirementInput',
    noRecordsBecause: NR.figures,
  },
  {
    name: 'historic_anchor_archive_list',
    group: 'classification',
    description: 'Every historic anchor recorded for the period, current and retired, with evidence digests.',
    path: `${PER}/classification/historic-anchor/archive`,
    noRecordsBecause: NR.figures,
  },
  {
    name: 'historic_anchor_evidence_download',
    group: 'classification',
    description: 'Save one historic anchor\'s evidence PDF into --download-dir and report the path and SHA-256.',
    path: `${PER}/classification/historic-anchor/archive/{recordId}/evidence`,
    binary: { kind: 'pdf', defaultName: 'historic-anchor-evidence.pdf' },
    requiresFullScope: 'A stored evidence file cannot be filtered field by field.',
  },
  {
    name: 'size_class_override',
    group: 'classification',
    description:
      'Override the computed size class with an evidenced one. Needs the evidence file under --upload-root. '
      + 'Use only where the computation cannot reflect the legal position.',
    method: 'PUT',
    path: `${PER}/size-classification/override`,
    level: 'write',
    body: SIZE_CLASSIFICATION_OVERRIDE_INPUT_FIELDS,
    bodySchema: 'SizeClassificationOverrideInput',
    noRecordsBecause: NR.figures,
  },
  {
    name: 'filing_regime_determine',
    group: 'classification',
    description:
      'Determine the filing regime (Micro FRS 105, Small, Small Abridged, Medium or Full) from the size class, '
      + 'optionally electing one the class allows.',
    method: 'POST',
    path: `${PER}/filing-regime`,
    level: 'write',
    body: FILING_REGIME_INPUT_FIELDS,
    bodySchema: 'FilingRegimeInput',
    optionalBody: true,
    noRecordsBecause: NR.workflow,
  },
  {
    name: 'filing_regime_get',
    group: 'classification',
    description: 'The period\'s filing regime and what it requires: statements, notes, audit exemption (s.360) and abridgement.',
    path: `${PER}/filing-regime`,
    noRecordsBecause: NR.workflow,
  },
  {
    name: 'member_audit_notice_set',
    group: 'classification',
    description: 'Record whether members served a section 334 notice requiring an audit, which removes the audit exemption.',
    method: 'PUT',
    path: `${PER}/member-audit-notice`,
    level: 'write',
    body: MEMBER_AUDIT_NOTICE_INPUT_FIELDS,
    bodySchema: 'MemberAuditNoticeInput',
    noRecordsBecause: NR.workflow,
  },
];
