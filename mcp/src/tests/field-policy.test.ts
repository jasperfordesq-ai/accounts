import { test } from 'node:test';
import assert from 'node:assert/strict';
import { applyFieldPolicy } from '../field-policy.js';

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
