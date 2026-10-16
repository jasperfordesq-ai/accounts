import type { ToolDefinition } from './tools.js';
import { APPROVAL_NOTE, CO, NR, PER, crud } from './tool-helpers.js';
import { ENUMS } from './enums.js';
import {
  CONTINGENT_LIABILITY_INPUT_FIELDS,
  CORPORATION_TAX_DUE_CONFIRMATION_INPUT_FIELDS,
  CREDITOR_INPUT_FIELDS,
  DEBTOR_INPUT_FIELDS,
  DIRECTOR_LOAN_INPUT_FIELDS,
  DIVIDEND_INPUT_FIELDS,
  FIXED_ASSET_INPUT_FIELDS,
  GOING_CONCERN_INPUT_FIELDS,
  INVENTORY_INPUT_FIELDS,
  LOAN_BALANCE_SNAPSHOT_INPUT_FIELDS,
  LOAN_INPUT_FIELDS,
  NOTES_DISCLOSURE_INPUT_FIELDS,
  OPENING_BALANCE_INPUT_FIELDS,
  PAYROLL_SUMMARY_INPUT_FIELDS,
  POST_BALANCE_SHEET_EVENT_INPUT_FIELDS,
  RELATED_PARTY_TRANSACTION_INPUT_FIELDS,
  SHARE_CAPITAL_INPUT_FIELDS,
  TAX_BALANCE_INPUT_FIELDS,
  YEAR_END_REVIEW_INPUT_FIELDS,
} from './bodies.js';

const REVIEW_SECTIONS = [
  'debtors', 'creditors', 'fixed-assets', 'inventory', 'loans', 'director-loans', 'payroll', 'tax',
  'dividends', 'post-balance-sheet-events', 'related-parties', 'contingent-liabilities', 'going-concern',
  'adjustments', 'directors-report-principal-activities', 'directors-report-audit-information',
  'note-directors-remuneration', 'note-ultimate-controlling-party', 'note-financial-instruments',
  'note-capital-commitments', 'note-deferred-tax',
] as const;

const LOCKED = ' Refused while the period is finalised or filed.';

export const YEAR_END_TOOLS: readonly ToolDefinition[] = [
  ...crud({
    entity: 'debtor', noun: 'debtor', group: 'year_end', collection: `${PER}/debtors`,
    body: DEBTOR_INPUT_FIELDS, bodySchema: 'DebtorInput',
    listDescription: 'Amounts owed to the company at the period end: trade debtors, other debtors and prepayments.',
    createDescription: `Record an amount owed to the company at the period end.${LOCKED}`,
    deleteDescription: 'Remove a debtor recorded in error.',
  }),
  ...crud({
    entity: 'creditor', noun: 'creditor', group: 'year_end', collection: `${PER}/creditors`,
    body: CREDITOR_INPUT_FIELDS, bodySchema: 'CreditorInput',
    listDescription: 'Amounts the company owes at the period end: trade creditors, accruals, tax and others, split by when due.',
    createDescription: `Record an amount the company owes at the period end.${LOCKED}`,
    deleteDescription: 'Remove a creditor recorded in error.',
  }),
  ...crud({
    entity: 'fixed_asset', noun: 'fixed asset', group: 'year_end', collection: `${CO}/fixed-assets`,
    body: FIXED_ASSET_INPUT_FIELDS, bodySchema: 'FixedAssetInput',
    listDescription: 'The company\'s fixed asset register: cost, depreciation method, useful life, disposals and capital-allowance treatment.',
    createDescription: 'Add a fixed asset. Depreciation and capital allowances are generated from it.',
    deleteDescription: 'Remove a fixed asset recorded in error. A disposed asset should be given a disposal date instead.',
  }),
  ...crud({
    entity: 'inventory', noun: 'stock line', group: 'year_end', collection: `${PER}/inventory`,
    body: INVENTORY_INPUT_FIELDS, bodySchema: 'InventoryInput',
    listDescription: 'Stock held at the period end and its valuation basis.',
    createDescription: `Record stock held at the period end.${LOCKED}`,
    deleteDescription: 'Remove a stock line recorded in error.',
  }),
  ...crud({
    entity: 'loan', noun: 'loan', group: 'year_end', collection: `${CO}/loans`,
    body: LOAN_INPUT_FIELDS, bodySchema: 'LoanInput',
    listDescription: 'Loans and borrowings of the company: lender, balance, rate and the within/after one year split.',
    createDescription: 'Record a loan to the company.',
    deleteDescription: 'Remove a loan recorded in error.',
  }),
  ...crud({
    entity: 'loan_snapshot', noun: 'loan balance snapshot', group: 'year_end', collection: `${PER}/loan-balance-snapshots`,
    body: LOAN_BALANCE_SNAPSHOT_INPUT_FIELDS, bodySchema: 'LoanBalanceSnapshotInput',
    listDescription: 'Each loan\'s movement for the period: opening, drawdowns, repayments, closing and the due split.',
    createDescription: `Record a loan\'s movement for the period; it must reconcile.${LOCKED}`,
    deleteDescription: 'Remove a loan balance snapshot recorded in error.',
  }),
  ...crud({
    entity: 'director_loan', noun: 'director loan arrangement', group: 'year_end', collection: `${PER}/director-loans`,
    body: DIRECTOR_LOAN_INPUT_FIELDS, bodySchema: 'DirectorLoanInput', model: 'DirectorLoan',
    listDescription:
      'Loans, quasi-loans, credit transactions and guarantees between the company and its directors or connected '
      + 'persons (sections 236 to 245, disclosure under section 307). Withheld while the data scope is Withheld: '
      + 'the counterparty name, the loan terms and the review note and reviewer.',
    createDescription:
      'Record a director-loan arrangement with its balances, legal basis and evidence. Leave counterpartyName, '
      + `loanTerms and reviewNote out unless the session has full data scope.${LOCKED}`,
    updateDescription: 'Change a director-loan arrangement, for example to record its compliance review.',
    updateNeedsFullScope:
      'The update replaces the whole arrangement, including the counterparty, terms and review note a Withheld '
      + 'session cannot read; sending it without them would erase them.',
    deleteDescription: 'Remove a director-loan arrangement recorded in error.',
  }),
  {
    name: 'payroll_get',
    group: 'year_end',
    description: 'The period\'s payroll summary: wages, directors\' fees, employer PRSI, pensions and staff numbers.',
    path: `${PER}/payroll`,
    noRecordsBecause: NR.figures,
  },
  {
    name: 'payroll_set',
    group: 'year_end',
    description: `Record or replace the period\'s payroll summary (aggregate figures only).${LOCKED}`,
    method: 'PUT',
    path: `${PER}/payroll`,
    level: 'write',
    body: PAYROLL_SUMMARY_INPUT_FIELDS,
    bodySchema: 'PayrollSummaryInput',
    noRecordsBecause: NR.figures,
  },
  {
    name: 'tax_balance_list',
    group: 'year_end',
    description: 'Tax balances at the period end by type: corporation tax, VAT, PAYE and RCT.',
    path: `${PER}/tax-balances`,
    noRecordsBecause: NR.figures,
  },
  {
    name: 'tax_balance_set',
    group: 'year_end',
    description: `Record or replace one tax balance (liability, paid, balance).${LOCKED}`,
    method: 'PUT',
    path: `${PER}/tax-balances/{taxType}`,
    params: [{ kind: 'pathEnum', name: 'taxType', values: ENUMS.TaxType, describe: 'Which tax: CorporationTax, Vat, Paye or Rct.' }],
    level: 'write',
    body: TAX_BALANCE_INPUT_FIELDS,
    bodySchema: 'TaxBalanceInput',
    noRecordsBecause: NR.figures,
  },
  {
    name: 'corporation_tax_due_confirm',
    group: 'year_end',
    description:
      'Confirm that corporation tax is due despite a charitable tax exemption, giving the profits outside the '
      + 'exemption.' + APPROVAL_NOTE,
    method: 'POST',
    path: `${PER}/tax-balances/CorporationTax/confirm-due`,
    level: 'admin',
    approval: true,
    body: CORPORATION_TAX_DUE_CONFIRMATION_INPUT_FIELDS,
    bodySchema: 'CorporationTaxDueConfirmationInput',
    noRecordsBecause: NR.figures,
  },
  ...crud({
    entity: 'dividend', noun: 'dividend', group: 'year_end', collection: `${PER}/dividends`,
    body: DIVIDEND_INPUT_FIELDS, bodySchema: 'DividendInput',
    listDescription: 'Dividends declared and paid in the period.',
    createDescription: `Record a dividend.${LOCKED}`,
    deleteDescription: 'Remove a dividend recorded in error.',
  }),
  ...crud({
    entity: 'share_capital', noun: 'share class', group: 'year_end', collection: `${CO}/share-capital`,
    body: SHARE_CAPITAL_INPUT_FIELDS, bodySchema: 'ShareCapitalInput',
    listDescription: 'The company\'s share capital by class: nominal value, number issued, paid status, issue and cancellation dates.',
    createDescription: 'Record a class of issued share capital.',
    deleteDescription: 'Remove a share class recorded in error.',
  }),
  {
    name: 'opening_balance_list',
    group: 'year_end',
    description: 'Opening balances by account category for the period, with their source and review state.',
    path: `${PER}/opening-balances`,
    noRecordsBecause: NR.figures,
  },
  {
    name: 'opening_balance_set',
    group: 'year_end',
    description: `Record or replace the opening balance of one account category.${LOCKED}`,
    method: 'PUT',
    path: `${PER}/opening-balances/{categoryId}`,
    level: 'write',
    body: OPENING_BALANCE_INPUT_FIELDS,
    bodySchema: 'OpeningBalanceInput',
    noRecordsBecause: NR.figures,
  },
  {
    name: 'opening_balance_delete',
    group: 'year_end',
    description: 'Remove one category\'s opening balance.' + APPROVAL_NOTE,
    method: 'DELETE',
    path: `${PER}/opening-balances/{categoryId}`,
    level: 'admin',
    approval: true,
    noRecordsBecause: NR.ack,
  },
  {
    name: 'year_end_summary',
    group: 'year_end',
    description: 'A summary of the year-end questionnaire: what has been recorded in each section and what is still outstanding.',
    path: `${PER}/year-end-summary`,
    noRecordsBecause: NR.workflow,
  },
];

export const DISCLOSURE_TOOLS: readonly ToolDefinition[] = [
  {
    name: 'note_list',
    group: 'disclosures',
    description:
      'The notes to the financial statements for the period, in order, with whether each is included and required. '
      + 'Withheld while the data scope is Withheld: the text of the director-transactions (section 307) and '
      + 'related-party notes, which name the people concerned, and the name of whoever reviewed a note.',
    path: `${PER}/notes`,
    model: 'NotesDisclosure',
  },
  {
    name: 'note_generate',
    group: 'disclosures',
    description:
      'Generate the regime-aware notes (accounting policies, fixed assets, debtors, creditors, share capital, staff, '
      + 'directors and the rest) from the period\'s records. Edited notes are kept.',
    method: 'POST',
    path: `${PER}/notes/generate`,
    level: 'write',
    model: 'NotesDisclosure',
  },
  {
    name: 'note_create',
    group: 'disclosures',
    description: `Add a custom note.${LOCKED}`,
    method: 'POST',
    path: `${PER}/notes`,
    level: 'write',
    body: NOTES_DISCLOSURE_INPUT_FIELDS,
    bodySchema: 'NotesDisclosureInput',
    model: 'NotesDisclosure',
  },
  {
    name: 'note_update',
    group: 'disclosures',
    description: `Edit a note's title, text or inclusion.${LOCKED}`,
    method: 'PUT',
    path: `${PER}/notes/{id}`,
    level: 'write',
    body: NOTES_DISCLOSURE_INPUT_FIELDS,
    bodySchema: 'NotesDisclosureInput',
    model: 'NotesDisclosure',
  },
  {
    name: 'note_delete',
    group: 'disclosures',
    description: 'Remove a custom note. A required note should be excluded with note_update instead.' + APPROVAL_NOTE,
    method: 'DELETE',
    path: `${PER}/notes/{id}`,
    level: 'admin',
    approval: true,
    noRecordsBecause: NR.ack,
  },
  ...crud({
    entity: 'post_balance_sheet_event', noun: 'post balance sheet event', group: 'disclosures',
    collection: `${PER}/post-balance-sheet-events`,
    body: POST_BALANCE_SHEET_EVENT_INPUT_FIELDS, bodySchema: 'PostBalanceSheetEventInput',
    listDescription: 'Events after the balance sheet date, adjusting and non-adjusting (FRS 102 section 32).',
    createDescription: `Record an event after the balance sheet date.${LOCKED}`,
    updateDescription: `Correct a post balance sheet event in place (description, date, adjusting flag, impact) instead of deleting and re-adding it.${LOCKED}`,
    deleteDescription: 'Remove a post balance sheet event recorded in error.',
  }),
  ...crud({
    entity: 'related_party_transaction', noun: 'related party transaction', group: 'disclosures',
    collection: `${PER}/related-party-transactions`,
    body: RELATED_PARTY_TRANSACTION_INPUT_FIELDS, bodySchema: 'RelatedPartyTransactionInput',
    model: 'RelatedPartyTransaction', noUpdate: true,
    listDescription:
      'Transactions with related parties for disclosure (FRS 102 section 33). Withheld while the data scope is '
      + 'Withheld: the party name and the terms.',
    createDescription:
      'Record a related party transaction. partyName is required by FilingBridge and is personal data, so this '
      + `needs a session with full data scope.${LOCKED}`,
    deleteDescription: 'Remove a related party transaction recorded in error.',
  }),
  ...crud({
    entity: 'contingent_liability', noun: 'contingent liability', group: 'disclosures',
    collection: `${PER}/contingent-liabilities`,
    body: CONTINGENT_LIABILITY_INPUT_FIELDS, bodySchema: 'ContingentLiabilityInput',
    listDescription: 'Contingent liabilities (guarantees, litigation, warranties) and their likelihood.',
    createDescription: `Record a contingent liability.${LOCKED}`,
    updateDescription: `Correct a contingent liability in place (description, nature, amount, likelihood) instead of deleting and re-adding it.${LOCKED}`,
    deleteDescription: 'Remove a contingent liability recorded in error.',
  }),
  {
    name: 'going_concern_get',
    group: 'disclosures',
    description: 'The directors\' going-concern confirmation and note for the period.',
    path: `${PER}/going-concern`,
    noRecordsBecause: NR.workflow,
  },
  {
    name: 'going_concern_set',
    group: 'disclosures',
    description: `Record the directors\' going-concern confirmation and note.${LOCKED}`,
    method: 'PUT',
    path: `${PER}/going-concern`,
    level: 'write',
    body: GOING_CONCERN_INPUT_FIELDS,
    bodySchema: 'GoingConcernInput',
    noRecordsBecause: NR.workflow,
  },
  {
    name: 'director_loan_compliance',
    group: 'disclosures',
    description:
      'The director-loan compliance review: each arrangement against sections 236 to 245 and 307, the 10% '
      + 'relevant-assets test, blockers and the sign-off packet. Withheld while the data scope is Withheld: '
      + 'counterparty names, the main conditions and the reviewer.',
    path: `${PER}/director-loans/compliance`,
    model: 'DirectorLoanComplianceResult',
  },
  {
    name: 'director_loan_section_307_note',
    group: 'disclosures',
    description: 'The generated section 307 director-loan disclosure note, as it will appear in the financial statements.',
    path: `${PER}/director-loans/section-307-note`,
    requiresFullScope: 'The note is prose that names each counterparty and states the terms; it cannot be filtered.',
  },
  {
    name: 'year_end_review_list',
    group: 'disclosures',
    description: 'Which year-end sections have been reviewed and confirmed, by whom and when.',
    path: `${PER}/year-end-reviews`,
    noRecordsBecause: NR.workflow,
  },
  {
    name: 'year_end_review_confirm',
    group: 'disclosures',
    description:
      'Confirm (or unconfirm) the review of one year-end section, such as debtors or the directors\' report '
      + 'principal activities.' + APPROVAL_NOTE,
    method: 'PUT',
    path: `${PER}/year-end-reviews/{sectionKey}`,
    params: [{ kind: 'pathEnum', name: 'sectionKey', values: REVIEW_SECTIONS, describe: 'The section being confirmed.' }],
    level: 'admin',
    approval: true,
    body: YEAR_END_REVIEW_INPUT_FIELDS,
    bodySchema: 'YearEndReviewInput',
    noRecordsBecause: NR.workflow,
  },
];
