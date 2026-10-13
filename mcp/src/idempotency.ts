/**
 * The routes on which FilingBridge honours Idempotency-Key.
 *
 * Only these record the key with the outcome and answer a repeat by replaying
 * that outcome (IdempotencyHttpContract.ExecuteAsync, and the onboarding
 * service, in backend/Accounts.Api). Every other route ignores the header: a
 * repeated POST there is a second record. So a change whose answer was lost
 * to a dropped connection is asked again only on a route listed here; on any
 * other route the outcome is unknown and the agent is told to read the record
 * before trying again.
 *
 * These routes also refuse a request that carries no key at all, PUTs
 * included, so the client sends one on every call to them.
 *
 * Mirrored from the backend source; idempotency.test.ts parses the endpoint
 * files and fails when the two disagree in either direction.
 */
export const REPLAY_SAFE_ROUTES: readonly string[] = [
  'POST /api/companies',
  'POST /api/companies/onboard',
  'POST /api/companies/{companyId}/periods',
  'PUT /api/companies/{companyId}/periods/{id}/status',
  'POST /api/companies/{companyId}/bank-accounts/{bankAccountId}/import',
  'POST /api/companies/{companyId}/periods/{periodId}/documents/cro-filing-pack',
  'POST /api/companies/{companyId}/periods/{periodId}/documents/signature-page',
  'POST /api/companies/{companyId}/periods/{periodId}/mark-filed',
  'PUT /api/companies/{companyId}/periods/{periodId}/filing/cro-status',
  'POST /api/companies/{companyId}/periods/{periodId}/filing/cro-payment',
  'POST /api/companies/{companyId}/periods/{periodId}/filing/charity-report-generated',
  'PUT /api/companies/{companyId}/periods/{periodId}/filing/charity-status',
  'POST /api/companies/{companyId}/periods/{periodId}/filing/revenue-external-validation',
  'PUT /api/companies/{companyId}/periods/{periodId}/filing/revenue-status',
  'POST /api/companies/{companyId}/periods/{periodId}/filing/validate-ixbrl',
  'POST /api/companies/{companyId}/periods/{periodId}/external-filing-handoff/authorities',
  'POST /api/companies/{companyId}/periods/{periodId}/external-filing-handoff/authorities/{authorityId}/revoke',
  'POST /api/companies/{companyId}/periods/{periodId}/external-filing-handoff/cro/snapshots',
  'POST /api/companies/{companyId}/periods/{periodId}/external-filing-handoff/revenue/snapshots',
  'POST /api/companies/{companyId}/periods/{periodId}/external-filing-handoff/snapshots/{snapshotId}/outcomes',
];

const LISTED = new Set(REPLAY_SAFE_ROUTES);

/** Whether FilingBridge answers a repeat of this route template by replaying the first outcome. */
export function honoursIdempotencyKey(method: string, template: string): boolean {
  return LISTED.has(`${method.toUpperCase()} ${template}`);
}
