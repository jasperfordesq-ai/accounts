/**
 * The personal-data gate.
 *
 * A connector session has a data scope, chosen by whoever typed the password
 * and held by FilingBridge: Withheld (the default) or Full (Owner only).
 * While it is Withheld, every record of a model below is reduced to the
 * fields listed as safe. It is an allowlist: a column added to an entity next
 * year is dropped by default rather than released by default, and the drift
 * test (schema-drift.test.ts) fails until somebody classifies it.
 *
 * What counts as personal here was decided by the product owner: officer and
 * registered-office addresses, user emails and display names, director-loan
 * counterparties and their terms and review notes, related-party names and
 * terms, the actors and before/after values in the audit log, CRO officer and
 * shareholder particulars, and the reviewer named on the dashboard.
 *
 * Free text that may interpolate such data is listed as `rewritten`: it is
 * released only after the model's `rewrite` hook has replaced what it would
 * reveal (director-loan blockers and warnings, which begin with the
 * counterparty's name; the generated director-transactions and related-party
 * notes). Responses that are nothing but such prose (the directors' report
 * data, the section 307 note), and every file download, cannot be filtered at
 * all; the tools that return them require a Full session instead (see
 * requiresFullScope in tools.ts).
 */
export type ModelName =
  | 'Company'
  | 'CompanyOfficer'
  | 'CompanyDashboardRow'
  | 'QuarantinedCompanySummary'
  | 'UserAdministrationSummary'
  | 'UserProvisioningResult'
  | 'AuthResponse'
  | 'DirectorLoan'
  | 'DirectorLoanDetail'
  | 'DirectorLoanComplianceResult'
  | 'DirectorLoanSignOffPacket'
  | 'NotesDisclosure'
  | 'RelatedPartyTransaction'
  | 'AuditLog'
  | 'CroOfficerInput'
  | 'B1ShareholderHandoff';

/** A nested value that carries no personal data and is passed through whole. */
export const OPAQUE = 'opaque' as const;

export interface ModelPolicy {
  /** Where the drift test reads the model's real fields from. */
  readonly source:
    | { readonly openapi: string }
    | { readonly entity: string; readonly className: string }
    | { readonly record: string; readonly recordName: string };
  /** Scalar fields released while the gate is closed. */
  readonly safe: readonly string[];
  /** Fields dropped while the gate is closed. Listed so each is a decision. */
  readonly withheld: readonly string[];
  /** Nested records, filtered by their own model or passed through as OPAQUE. */
  readonly nested?: Readonly<Record<string, ModelName | typeof OPAQUE>>;
  /**
   * Free text that may carry withheld data. While the gate is closed it is
   * released only as `rewrite` sets it, and dropped if there is no hook.
   */
  readonly rewritten?: readonly string[];
  readonly rewrite?: RewriteHook;
}

/** What a rewrite hook knows about the whole response it is part of. */
export interface GateContext {
  /**
   * The label FilingBridge prefixes to each director-loan message (the
   * counterparty's name) and the arrangements it labels, collected from the
   * unfiltered response before the names are dropped.
   */
  readonly loanLabels: ReadonlyMap<string, readonly number[]>;
}

/**
 * Sets a record's `rewritten` fields on `out`, from the unfiltered record.
 * A field the hook leaves unset is dropped.
 */
export type RewriteHook = (
  record: Readonly<Record<string, unknown>>,
  out: Record<string, unknown>,
  context: GateContext,
) => void;

/** Said in place of a director-loan message whose subject cannot be separated from a name. */
export const WITHHELD_LOAN_MESSAGE =
  'A director-loan message was withheld because it may name a person. Read it in the web application or '
  + 'with a session that has full data scope.';

/**
 * FilingBridge begins every director-loan blocker and warning with the
 * arrangement's label, which is the counterparty's name. While the gate is
 * closed the label becomes the arrangement's identifier; a message whose
 * label is not recognised is replaced whole.
 */
export function neutraliseLoanMessage(message: unknown, context: GateContext, ownId?: number): unknown {
  if (typeof message !== 'string') return message;
  const labels = [...context.loanLabels.keys()].sort((a, b) => b.length - a.length);
  for (const label of labels) {
    const prefix = `${label}: `;
    if (!message.startsWith(prefix)) continue;
    const ids = ownId !== undefined ? [ownId] : context.loanLabels.get(label)!;
    return `Arrangement ${ids.map((id) => `#${id}`).join(', ')}: ${message.slice(prefix.length)}`;
  }
  return WITHHELD_LOAN_MESSAGE;
}

/** Rewrites the named message fields (a string or a list of strings) of a director-loan record. */
function loanMessages(...fields: string[]): RewriteHook {
  return (record, out, context) => {
    const ownId = typeof record['id'] === 'number' && typeof record['counterpartyName'] === 'string'
      ? record['id']
      : undefined;
    for (const field of fields) {
      if (!(field in record)) continue;
      const value = record[field];
      out[field] = Array.isArray(value)
        ? value.map((message) => neutraliseLoanMessage(message, context, ownId))
        : neutraliseLoanMessage(value, context, ownId);
    }
  };
}

/**
 * Generated notes whose text is built from withheld records: the director
 * transactions note (section 307: each counterparty and the terms) and the
 * related-party note (each party's name and the terms). Matched by their
 * stable code (StatutoryNoteCodes), or by title on a note without one.
 */
export const WITHHELD_NOTE_CODES: ReadonlyMap<string, string> = new Map([
  ['DIRECTOR-TRANSACTIONS', 'Advances, Credits and Guarantees to Directors'],
  ['RELATED-PARTIES', 'Related Party Transactions'],
]);

export const WITHHELD_NOTE_TEXT =
  '[Withheld: this note names the people concerned and states their terms. It is shown in the web '
  + 'application, or to a session with full data scope.]';

function noteIsWithheld(record: Readonly<Record<string, unknown>>): boolean {
  const code = record['code'];
  if (typeof code === 'string' && code !== '') return WITHHELD_NOTE_CODES.has(code);
  const title = typeof record['title'] === 'string' ? record['title'].trim().toLowerCase() : '';
  return [...WITHHELD_NOTE_CODES.values()].some((known) => known.toLowerCase() === title)
    || (/director/.test(title) && /(loan|advance|credit|guarantee)/.test(title))
    || /related part/.test(title);
}

const noteContent: RewriteHook = (record, out) => {
  if (!('content' in record)) return;
  const content = record['content'];
  if (noteIsWithheld(record) && typeof content === 'string' && content.trim() !== '') {
    out['content'] = WITHHELD_NOTE_TEXT;
    out['contentWithheld'] = true;
  } else {
    out['content'] = content;
  }
};

export const POLICIES: Record<ModelName, ModelPolicy> = {
  Company: {
    source: { entity: 'Company.cs', className: 'Company' },
    safe: [
      'id', 'tenantId', 'legalName', 'tradingName', 'croNumber', 'taxReference', 'companyType',
      'incorporationDate', 'financialYearStartMonth', 'annualReturnDate', 'isGroupMember', 'isHolding',
      'isInvestment', 'isSubsidiary', 'isDormant', 'isTrading', 'isVatRegistered', 'isEmployer',
      'hasStock', 'ownsAssets', 'hasBorrowings', 'hasDirectorLoans', 'isListedSecurities',
      'isCreditInstitution', 'isInsuranceUndertaking', 'isPensionFund', 'isFifthScheduleEntity',
      'isOtherIneligibleEntity', 'isFinancialHoldingUndertaking', 'preparesGroupFinancialStatements',
      'includedInHigherConsolidatedFinancialStatements', 'isCharitableOrganisation',
      'holdsCharitableTaxExemption', 'charitableTaxExemptionReference',
      'charitableTaxExemptionConfirmedDate', 'chartOfAccountsVariant', 'isQuarantined',
      'quarantinedAtUtc', 'quarantineEvidenceSha256', 'createdAt', 'updatedAt',
    ],
    // The registered office of a small company is very often a director's
    // home. The quarantine actor and reason name and describe a person.
    // Navigations other than officers and periods are never loaded by the
    // routes that return a Company, and are dropped rather than trusted.
    withheld: [
      'registeredOfficeAddress1', 'registeredOfficeAddress2', 'registeredOfficeCity',
      'registeredOfficeCounty', 'registeredOfficeEircode', 'quarantinedByUserId',
      'quarantinedByDisplayName', 'quarantineReason', 'bankAccounts', 'fixedAssets', 'loans',
      'categories', 'transactionRules', 'shareCapitals', 'filingDeadlines', 'filingHistories',
      'userAccesses', 'charityInfo', 'tenant',
    ],
    nested: { officers: 'CompanyOfficer', periods: OPAQUE },
  },
  CompanyOfficer: {
    source: { entity: 'CompanyOfficer.cs', className: 'CompanyOfficer' },
    // An officer's name and role are on the public CRO register; the address
    // a company holds for them is frequently their home.
    safe: ['id', 'companyId', 'name', 'role', 'appointedDate', 'resignedDate'],
    withheld: ['address'],
  },
  CompanyDashboardRow: {
    source: { openapi: 'CompanyDashboardRow' },
    safe: [
      'id', 'legalName', 'tradingName', 'croNumber', 'taxReference', 'companyType', 'incorporationDate',
      'financialYearStartMonth', 'annualReturnDate', 'isGroupMember', 'isHolding', 'isInvestment',
      'isSubsidiary', 'isDormant', 'isTrading', 'isVatRegistered', 'isEmployer', 'hasStock',
      'ownsAssets', 'hasBorrowings', 'hasDirectorLoans', 'isListedSecurities', 'isCreditInstitution',
      'isInsuranceUndertaking', 'isPensionFund', 'isCharitableOrganisation', 'createdAt', 'periodCount',
    ],
    withheld: ['assignedReviewerName', 'assignedReviewerEmail'],
    nested: { latestPeriod: OPAQUE },
  },
  QuarantinedCompanySummary: {
    source: { record: 'Services/CompanyQuarantineService.cs', recordName: 'QuarantinedCompanySummary' },
    safe: ['companyId', 'legalName', 'quarantinedAtUtc', 'evidenceSha256'],
    withheld: ['quarantinedByDisplayName', 'reason'],
  },
  UserAdministrationSummary: {
    source: { openapi: 'UserAdministrationSummary' },
    safe: [
      'userId', 'role', 'isActive', 'mustChangePassword', 'isLocked', 'lockedUntilUtc', 'mfaEnabled',
      'companyIds', 'inviteAcceptedAtUtc', 'deactivatedAtUtc', 'offboardedAtUtc', 'sessionVersion',
    ],
    withheld: ['email', 'displayName'],
  },
  UserProvisioningResult: {
    source: { openapi: 'UserProvisioningResult' },
    // The action token is a credential, not personal data. It is the reason
    // the provisioning tools exist, and it is handed to the person.
    safe: ['actionToken', 'expiresAtUtc'],
    withheld: [],
    nested: { user: 'UserAdministrationSummary' },
  },
  AuthResponse: {
    source: { openapi: 'AuthResponse' },
    safe: [
      'userId', 'tenantId', 'tenantName', 'tenantSlug', 'role', 'allowedCompanyIds',
      'mustChangePassword', 'mfaVerified', 'mfaMethod',
    ],
    withheld: ['email', 'displayName'],
  },
  DirectorLoan: {
    source: { entity: 'DirectorLoan.cs', className: 'DirectorLoan' },
    safe: [
      'id', 'periodId', 'directorId', 'counterpartyType', 'arrangementType', 'arrangementDate',
      'openingBalance', 'advances', 'repayments', 'closingBalance', 'termsStatus', 'interestRate',
      'interestCharged', 'allowanceMade', 'section236PresumptionEvidenceReference', 'isDocumented',
      'maxBalanceDuringYear', 'complianceBasis', 'relevantAssetsBasis', 'relevantAssetsAmount',
      'relevantAssetsAsOfDate', 'relevantAssetsReference', 'noPriorFinancialStatementsConfirmed',
      'relevantAssetsFallReview', 'relevantAssetsReductionAwarenessDate', 'termsAmendedDate',
      'termsAmendmentEvidenceReference', 'exceptionEvidenceReference', 'sapDeclarationDate',
      'sapResolutionDate', 'sapActivityStartDate', 'sapCroFilingDate', 'sapDeclarationReference',
      'sapResolutionReference', 'sapCroFilingReference', 'sapDeclarationCoversSection203Matters',
      'expenseIncurredDate', 'expenseDischargedDate', 'ordinaryCourseConfirmed',
      'noMoreFavourableTermsConfirmed', 'reviewDecision', 'reviewedAtUtc',
    ],
    withheld: ['counterpartyName', 'loanTerms', 'reviewNote', 'reviewedBy', 'reviewerRole'],
    nested: { balanceMovements: OPAQUE },
  },
  DirectorLoanDetail: {
    source: { record: 'Services/DirectorLoanComplianceService.cs', recordName: 'DirectorLoanDetail' },
    safe: [
      'id', 'relatedDirectorName', 'counterpartyType', 'arrangementType', 'arrangementDate',
      'openingBalance', 'advances', 'repayments', 'allowanceMade', 'maxDuringYear', 'closingBalance',
      'interestRate', 'interestCharged', 'section236PresumedInterest', 'termsStatus',
      'complianceBasis', 'relevantAssets', 'section240Threshold', 'section240StrictlyBelowThreshold',
      'section307DisclosureRequired', 'reviewDecision', 'reviewedAtUtc', 'readyForFinalOutput',
    ],
    // mainConditions is the loan terms text under another name.
    withheld: ['counterpartyName', 'mainConditions', 'reviewedBy', 'reviewerRole'],
    nested: { balanceMovements: OPAQUE },
    // Each message begins with the counterparty's name.
    rewritten: ['blockingIssues', 'warnings'],
    rewrite: loanMessages('blockingIssues', 'warnings'),
  },
  DirectorLoanComplianceResult: {
    source: { record: 'Services/DirectorLoanComplianceService.cs', recordName: 'DirectorLoanComplianceResult' },
    safe: [
      'totalDirectorLoans', 'aggregateMaximumExposure', 'disclosureAggregateMaximumExposure',
      'disclosureOpeningNetAssets', 'disclosureClosingNetAssets', 'section236PresumedInterest',
      'hasUnresolvedComplianceBlockers', 'requiresAlternativeLegalBasis',
    ],
    withheld: [],
    nested: { loans: 'DirectorLoanDetail', signOffPacket: 'DirectorLoanSignOffPacket', legalSources: OPAQUE },
    // Every arrangement's messages again, each beginning with its counterparty's name.
    rewritten: ['blockingIssues', 'warnings', 'warning'],
    rewrite: loanMessages('blockingIssues', 'warnings', 'warning'),
  },
  DirectorLoanSignOffPacket: {
    source: { record: 'Services/DirectorLoanComplianceService.cs', recordName: 'DirectorLoanSignOffPacket' },
    safe: ['state', 'readyForArrangementReview', 'readyForFinalOutput'],
    withheld: [],
    nested: { legalSources: OPAQUE },
    rewritten: ['openBlockers', 'openWarnings'],
    rewrite: loanMessages('openBlockers', 'openWarnings'),
  },
  NotesDisclosure: {
    source: { entity: 'NotesDisclosure.cs', className: 'NotesDisclosure' },
    safe: [
      'id', 'periodId', 'noteNumber', 'code', 'title', 'isRequired', 'isIncluded', 'checklistState',
      'reviewEvidence', 'reviewedAt',
    ],
    // The reviewer is a user's display name.
    withheld: ['reviewedBy'],
    // The director-transactions and related-party notes are built from
    // counterparties, party names and terms; every other note's text passes.
    rewritten: ['content'],
    rewrite: noteContent,
  },
  RelatedPartyTransaction: {
    source: { entity: 'RelatedPartyTransaction.cs', className: 'RelatedPartyTransaction' },
    safe: ['id', 'periodId', 'relationship', 'transactionType', 'amount', 'balanceOwed', 'createdAt'],
    withheld: ['partyName', 'terms'],
  },
  AuditLog: {
    source: { entity: 'AuditLog.cs', className: 'AuditLog' },
    // Who did it and the before/after snapshots (which carry addresses and
    // names whenever the audited record does) are withheld; what happened, to
    // which record and when, and the integrity chain, are not.
    safe: [
      'id', 'companyId', 'periodId', 'entityType', 'entityId', 'action', 'tenantId',
      'previousIntegrityHash', 'integrityHash', 'timestamp',
    ],
    withheld: ['userId', 'actorDisplayName', 'oldValueJson', 'newValueJson', 'requestId'],
  },
  CroOfficerInput: {
    source: { openapi: 'CroOfficerInput' },
    safe: ['officerId', 'firstName', 'lastName', 'otherDirectorshipsEvidenceReference', 'protectedIdentifierEntryConfirmed'],
    withheld: [
      'address', 'identityType', 'identityEvidenceReference', 'identityEvidenceSha256',
      'presenterNotificationEmail',
    ],
  },
  B1ShareholderHandoff: {
    source: { openapi: 'B1ShareholderHandoff' },
    safe: [
      'memberReference', 'shareClass', 'currency', 'openingHolding', 'closingHolding', 'holdingDisplay',
      'evidenceReference',
    ],
    withheld: ['name', 'address'],
  },
};

/** Keys a paged envelope carries beside its items. */
const ENVELOPE_META = new Set([
  'total', 'page', 'pageSize', 'totalPages', 'hasPreviousPage', 'hasNextPage', 'sortBy', 'sortDirection',
]);

function asRecord(value: unknown): Record<string, unknown> | null {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) return null;
  return value as Record<string, unknown>;
}

function filterRecord(model: ModelName, value: unknown, context: GateContext): unknown {
  if (Array.isArray(value)) return value.map((item) => filterRecord(model, item, context));
  const record = asRecord(value);
  if (!record) return value;
  const policy = POLICIES[model];
  const out: Record<string, unknown> = {};
  for (const [key, item] of Object.entries(record)) {
    const nested = policy.nested?.[key];
    if (nested === OPAQUE) {
      out[key] = item;
    } else if (nested !== undefined) {
      out[key] = filterRecord(nested, item, context);
    } else if (policy.safe.includes(key)) {
      out[key] = item;
    }
    // Withheld, rewritten and unclassified keys are dropped alike here.
  }
  policy.rewrite?.(record, out, context);
  return out;
}

/** Collects every director-loan label in a response, from the unfiltered records. */
function gateContextFor(value: unknown): GateContext {
  const loanLabels = new Map<string, number[]>();
  const visit = (node: unknown, depth: number): void => {
    if (depth > 8 || node === null || typeof node !== 'object') return;
    if (Array.isArray(node)) {
      for (const item of node) visit(item, depth + 1);
      return;
    }
    const record = node as Record<string, unknown>;
    const id = record['id'];
    const label = record['counterpartyName'];
    if (typeof id === 'number' && typeof label === 'string' && label !== '') {
      const ids = loanLabels.get(label) ?? [];
      if (!ids.includes(id)) ids.push(id);
      loanLabels.set(label, ids);
    }
    for (const item of Object.values(record)) visit(item, depth + 1);
  };
  visit(value, 0);
  return { loanLabels };
}

/**
 * Applies a model's allowlist to a response: a record, a list of records, or
 * FilingBridge's paged envelope `{ items, total, page, ... }`. The envelope
 * is recognised only at the top, never while recursing into a record.
 */
export function applyFieldPolicy<T>(model: ModelName, value: T, allowPersonalData: boolean): T {
  if (allowPersonalData) return value;
  const context = gateContextFor(value);
  const record = asRecord(value);
  if (record && Array.isArray(record['items'])) {
    const out: Record<string, unknown> = {};
    for (const [key, item] of Object.entries(record)) {
      if (ENVELOPE_META.has(key)) out[key] = item;
    }
    out['items'] = filterRecord(model, record['items'], context);
    return out as T;
  }
  return filterRecord(model, value, context) as T;
}

export function safeFieldsOf(model: ModelName): readonly string[] {
  return POLICIES[model].safe;
}
