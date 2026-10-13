import { test } from 'node:test';
import assert from 'node:assert/strict';
import { WITHHELD_LOAN_MESSAGE, WITHHELD_NOTE_TEXT, applyFieldPolicy } from '../field-policy.js';

test('the gate is an allowlist: unknown fields are dropped', () => {
  const out = applyFieldPolicy('CompanyOfficer', { id: 1, name: 'A', address: 'home', newColumn: 'x' }, false);
  assert.deepEqual(out, { id: 1, name: 'A' });
});

test('an open gate returns the record unchanged', () => {
  const record = { id: 1, address: 'home' };
  assert.equal(applyFieldPolicy('CompanyOfficer', record, true), record);
});

test('nested records are filtered by their own model', () => {
  const company = {
    id: 1, legalName: 'Acme', registeredOfficeAddress1: '1 Main St', quarantineReason: 'x',
    officers: [{ id: 2, name: 'A', address: 'home' }], periods: [{ id: 3, lockedBy: 'reviewer' }], userAccesses: [{ userId: 9 }],
  };
  assert.deepEqual(applyFieldPolicy('Company', company, false), {
    id: 1, legalName: 'Acme', officers: [{ id: 2, name: 'A' }], periods: [{ id: 3, lockedBy: 'reviewer' }],
  });
});

test('the paged envelope keeps its paging and filters its items', () => {
  const page = { total: 1, page: 1, pageSize: 50, totalPages: 1, hasNextPage: false, secret: 'x', items: [{ id: 1, action: 'Update', actorDisplayName: 'A', oldValueJson: '{}' }] };
  assert.deepEqual(applyFieldPolicy('AuditLog', page, false), { total: 1, page: 1, pageSize: 50, totalPages: 1, hasNextPage: false, items: [{ id: 1, action: 'Update' }] });
});

test('provisioning results keep the credential and drop the person', () => {
  const out = applyFieldPolicy('UserProvisioningResult', { user: { userId: 1, email: 'a@b', displayName: 'A', role: 'Client' }, actionToken: 't', expiresAtUtc: 'x' }, false);
  assert.deepEqual(out, { user: { userId: 1, role: 'Client' }, actionToken: 't', expiresAtUtc: 'x' });
});

test('director loan compliance hides counterparties and terms at every level', () => {
  const out = applyFieldPolicy('DirectorLoanComplianceResult', {
    totalDirectorLoans: 5, loans: [{ id: 1, counterpartyName: 'Spouse', mainConditions: 'interest free', relatedDirectorName: 'A', closingBalance: 5 }],
  }, false) as { loans: Record<string, unknown>[] };
  assert.deepEqual(out.loans, [{ id: 1, relatedDirectorName: 'A', closingBalance: 5 }]);
});

const COMPLIANCE = {
  totalDirectorLoans: 5000,
  loans: [
    {
      id: 11, counterpartyName: 'Siobhán Murphy', relatedDirectorName: 'Siobhán Murphy', closingBalance: 5000,
      mainConditions: 'interest free, repayable on demand',
      blockingIssues: ['Siobhán Murphy: Section 236 presumed interest of €120.00 exceeds recorded interest.'],
      warnings: ['Siobhán Murphy: Section 244 discharge remains due by 2026-12-31.'],
    },
    {
      id: 12, counterpartyName: 'Patrick Murphy (spouse)', closingBalance: 0,
      blockingIssues: [], warnings: ['Patrick Murphy (spouse): Terms should be documented in writing.'],
    },
  ],
  blockingIssues: ['Siobhán Murphy: Section 236 presumed interest of €120.00 exceeds recorded interest.'],
  warnings: [
    'Siobhán Murphy: Section 244 discharge remains due by 2026-12-31.',
    'Patrick Murphy (spouse): Terms should be documented in writing.',
    'Someone Else: a message whose label is not one of these arrangements.',
  ],
  warning: 'Siobhán Murphy: Section 236 presumed interest of €120.00 exceeds recorded interest.',
  signOffPacket: {
    state: 'evidence-incomplete', readyForArrangementReview: false, readyForFinalOutput: false,
    openBlockers: ['Siobhán Murphy: Section 236 presumed interest of €120.00 exceeds recorded interest.'],
    openWarnings: ['Patrick Murphy (spouse): Terms should be documented in writing.'],
    legalSources: [{ code: 'companies-act-2014-s236', title: 'Companies Act 2014, section 236', url: 'https://example.ie' }],
  },
  legalSources: [],
};

test('director loan messages lose the counterparty name at every level, keeping the arrangement and the problem', () => {
  const out = applyFieldPolicy('DirectorLoanComplianceResult', COMPLIANCE, false) as Record<string, unknown>;
  const text = JSON.stringify(out);
  assert.doesNotMatch(text, /Patrick|Someone Else|interest free/);
  // The director's name is on the public register and stays where it is a field of its own.
  assert.equal(text.match(/Siobhán/g)?.length, 1, 'only relatedDirectorName keeps the name');
  const loans = out['loans'] as Record<string, unknown>[];
  assert.deepEqual(loans[0]!['blockingIssues'], ['Arrangement #11: Section 236 presumed interest of €120.00 exceeds recorded interest.']);
  assert.deepEqual(loans[1]!['warnings'], ['Arrangement #12: Terms should be documented in writing.']);
  assert.deepEqual(out['warnings'], [
    'Arrangement #11: Section 244 discharge remains due by 2026-12-31.',
    'Arrangement #12: Terms should be documented in writing.',
    WITHHELD_LOAN_MESSAGE,
  ]);
  assert.equal(out['warning'], 'Arrangement #11: Section 236 presumed interest of €120.00 exceeds recorded interest.');
  assert.deepEqual(out['signOffPacket'], {
    state: 'evidence-incomplete', readyForArrangementReview: false, readyForFinalOutput: false,
    openBlockers: ['Arrangement #11: Section 236 presumed interest of €120.00 exceeds recorded interest.'],
    openWarnings: ['Arrangement #12: Terms should be documented in writing.'],
    legalSources: COMPLIANCE.signOffPacket.legalSources,
  });
  assert.equal(applyFieldPolicy('DirectorLoanComplianceResult', COMPLIANCE, true), COMPLIANCE, 'a full session sees it all');
});

const NOTES = [
  { id: 1, periodId: 2, noteNumber: 1, code: 'ACC-POLICIES', title: 'Accounting Policies', content: 'Prepared under FRS 102 Section 1A.', isRequired: true, isIncluded: true, checklistState: 'Required', reviewEvidence: null, reviewedBy: null, reviewedAt: null },
  { id: 2, periodId: 2, noteNumber: 9, code: 'DIRECTOR-TRANSACTIONS', title: 'Advances, Credits and Guarantees to Directors', content: 'Siobhán Murphy: €5,000 advanced interest free, repayable on demand.', isRequired: true, isIncluded: true, checklistState: 'Required', reviewEvidence: null, reviewedBy: 'Reviewer Name', reviewedAt: '2026-10-01T00:00:00Z' },
  { id: 3, periodId: 2, noteNumber: 12, code: 'RELATED-PARTIES', title: 'Related Party Transactions', content: 'Patrick Murphy (spouse) — Rent: €12,000', isRequired: true, isIncluded: true, checklistState: 'Required', reviewEvidence: null, reviewedBy: null, reviewedAt: null },
  { id: 4, periodId: 2, noteNumber: 20, code: null, title: 'Loans to directors', content: 'Legacy uncoded text naming Siobhán Murphy.', isRequired: false, isIncluded: true, checklistState: 'Required', reviewEvidence: null, reviewedBy: null, reviewedAt: null },
  { id: 5, periodId: 2, noteNumber: 10, code: 'DIRECTOR-TRANSACTIONS', title: 'Advances, Credits and Guarantees to Directors', content: 'Not applicable: the company is not recorded as having director loans.', isRequired: true, isIncluded: false, checklistState: 'NotApplicable', reviewEvidence: null, reviewedBy: null, reviewedAt: null },
];

test('the director-transactions and related-party notes are withheld; every other note is kept', () => {
  const out = applyFieldPolicy('NotesDisclosure', NOTES, false) as Record<string, unknown>[];
  assert.doesNotMatch(JSON.stringify(out), /Siobhán|Patrick|Reviewer Name|interest free/);
  assert.equal(out.length, NOTES.length, 'no note disappears');
  assert.equal(out[0]!['content'], 'Prepared under FRS 102 Section 1A.');
  assert.equal(out[0]!['contentWithheld'], undefined);
  for (const index of [1, 2, 3, 4]) {
    assert.equal(out[index]!['content'], WITHHELD_NOTE_TEXT, `note ${index + 1}`);
    assert.equal(out[index]!['contentWithheld'], true);
    assert.equal(out[index]!['title'], NOTES[index]!.title, 'the title stays, so the agent knows the note exists');
  }
  assert.equal(out[1]!['reviewedBy'], undefined);
  assert.equal(out[1]!['reviewedAt'], '2026-10-01T00:00:00Z');
});
