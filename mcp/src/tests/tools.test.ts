import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createHash } from 'node:crypto';
import { TOOLS, findTool } from '../catalogue.js';
import { annotationsFor, runTool, toolInputSchema, personalFieldPaths, type ToolContext } from '../tools.js';
import { TOOL_GROUPS } from '../toolsets.js';
import { ApprovalRequiredError } from '../client.js';
import { ConnectorError } from '../errors.js';
import { clientWith, json } from './helpers.js';

const tool = (name: string) => {
  const found = findTool(name);
  assert.ok(found, name);
  return found;
};

function ctx(responder: Parameters<typeof clientWith>[0], extra: Partial<ToolContext> = {}) {
  const { client, calls } = clientWith(responder);
  return { context: { client, allowPersonalData: false, ...extra } as ToolContext, calls };
}

test('every tool declares a model, a reason it needs no gate, or a full-scope rule', () => {
  for (const t of TOOLS) {
    const declared = [t.model, t.noRecordsBecause, t.requiresFullScope].filter(Boolean).length;
    assert.ok(declared >= 1, `${t.name} says nothing about personal data`);
    assert.ok(!(t.model && t.noRecordsBecause), `${t.name} declares both a model and no records`);
  }
});

test('every binary download needs full scope and a download directory', () => {
  for (const t of TOOLS.filter((x) => x.binary)) {
    assert.ok(t.requiresFullScope, `${t.name} returns a file and must require full data scope`);
  }
  assert.ok(tool('privacy_access_export').requiresFullScope);
});

test('updates that would erase withheld fields need full scope', () => {
  for (const name of ['company_update', 'officer_update', 'director_loan_update']) assert.ok(tool(name).requiresFullScope, name);
});

test('every toolset has tools, and every tool belongs to a known toolset', () => {
  for (const group of TOOL_GROUPS.filter((g) => g !== 'core')) {
    assert.ok(TOOLS.some((t) => t.group === group), `toolset ${group} is empty`);
  }
  for (const t of TOOLS) assert.ok((TOOL_GROUPS as readonly string[]).includes(t.group), t.name);
});

test('annotations follow the definition', () => {
  assert.deepEqual(annotationsFor(tool('debtor_list')), { title: 'Debtor list', readOnlyHint: true, destructiveHint: false, idempotentHint: true, openWorldHint: false });
  assert.equal(annotationsFor(tool('debtor_delete')).destructiveHint, true);
  assert.equal(annotationsFor(tool('debtor_create')).idempotentHint, false);
  assert.equal(annotationsFor(tool('accounts_package_download')).readOnlyHint, false, 'a download writes a file');
  for (const t of TOOLS) {
    assert.equal(annotationsFor(t).destructiveHint, t.approval === true, t.name);
    assert.equal(annotationsFor(t).openWorldHint, false);
  }
});

test('approval tools advertise approvalId and require a reason; reads advertise neither', () => {
  const del = toolInputSchema(tool('officer_delete')) as { properties: Record<string, unknown>; required: string[]; additionalProperties: boolean };
  assert.ok('approvalId' in del.properties);
  assert.ok(del.required.includes('reason'));
  assert.equal(del.additionalProperties, false);
  const read = toolInputSchema(tool('officer_list')) as { properties: Record<string, unknown> };
  assert.ok(!('reason' in read.properties) && !('approvalId' in read.properties));
});

test('no tool name, path or description uses "submit" as a verb (the filing boundary)', () => {
  for (const t of TOOLS) {
    assert.doesNotMatch(t.name, /submit/i, t.name);
    assert.doesNotMatch(t.path, /submit/i, t.name);
    assert.doesNotMatch(t.description, /\bsubmit(s|ting)?\b/i, t.name);
  }
});

test('a read applies the field gate', async () => {
  const { context, calls } = ctx(() => json(200, [{ id: 1, companyId: 2, name: 'Aoife', role: 'Director', address: '1 Main St' }]));
  const out = await runTool(tool('officer_list'), context, { companyId: 2 });
  assert.deepEqual(out, [{ id: 1, companyId: 2, name: 'Aoife', role: 'Director' }]);
  assert.equal(calls[0]!.url, 'http://127.0.0.1:3500/api/companies/2/officers');
  const full = await runTool(tool('officer_list'), { ...context, allowPersonalData: true }, { companyId: 2 });
  assert.equal((full as { address: string }[])[0]!.address, '1 Main St');
});

test('a write setting a withheld field is refused before anything is sent', async () => {
  const { context, calls } = ctx(() => json(200, {}));
  await assert.rejects(
    runTool(tool('officer_create'), context, { companyId: 2, name: 'A', role: 'Director', address: 'home' }),
    (error: unknown) => error instanceof ConnectorError && error.code === 'PERSONAL_DATA_WITHHELD',
  );
  await assert.rejects(
    runTool(tool('company_onboard'), context, { officers: [{ name: 'A', role: 'Director', address: 'home' }] }),
    /officers\[0\]\.address/,
  );
  assert.equal(calls.length, 0);
});

test('a write sends the body, the reason header and an idempotency key', async () => {
  const { context, calls } = ctx(() => json(201, { id: 9, name: 'Acme', amount: 10 }));
  await runTool(tool('debtor_create'), context, { companyId: 1, periodId: 2, name: 'Acme', amount: 10, type: 'Trade', reason: 'year end' });
  const call = calls[0]!;
  assert.equal(call.method, 'POST');
  assert.deepEqual(call.body, { name: 'Acme', amount: 10, type: 'Trade' });
  assert.equal(call.headers['x-filingbridge-reason'], 'year end');
  assert.match(call.headers['idempotency-key'] ?? '', /^[0-9a-f-]{36}$/);
});

test('an approval tool needs a reason and forwards approvalId on the retry', async () => {
  const { context, calls } = ctx(() => new Response(null, { status: 204 }));
  await assert.rejects(runTool(tool('debtor_delete'), context, { companyId: 1, periodId: 2, id: 3 }), /reason/);
  await runTool(tool('debtor_delete'), context, { companyId: 1, periodId: 2, id: 3, reason: 'duplicate', approvalId: '0f8fad5b-d9cb-469f-a165-70867728950e' });
  assert.equal(calls[0]!.headers['x-filingbridge-approval'], '0f8fad5b-d9cb-469f-a165-70867728950e');
  assert.equal(calls[0]!.method, 'DELETE');
});

test('a 428 becomes an ApprovalRequiredError carrying the command', async () => {
  const { context } = ctx(() => json(428, { code: 'APPROVAL_REQUIRED', approvalId: 'a1b2', summary: 'Delete debtor 3', command: 'filingbridge-mcp approve a1b2', expiresAt: '2026-10-12T10:00:00Z', approvalsUrl: 'https://x/settings/approvals?id=a1b2' }));
  await assert.rejects(runTool(tool('debtor_delete'), context, { companyId: 1, periodId: 2, id: 3, reason: 'dup' }), (error: unknown) => {
    assert.ok(error instanceof ApprovalRequiredError);
    assert.equal(error.command, 'filingbridge-mcp approve a1b2');
    assert.match(error.message, /never ask for the password/);
    return true;
  });
});

test('optional bodies are omitted when no field is given', async () => {
  const { context, calls } = ctx(() => json(200, {}));
  await runTool(tool('size_classify'), context, { companyId: 1, periodId: 2 });
  assert.equal(calls[0]!.body, undefined);
});

test('a download is written to the download directory and only its path and digest return', async () => {
  const dir = mkdtempSync(join(tmpdir(), 'fb-dl-'));
  const bytes = Buffer.from('%PDF-1.7 accounts');
  const { context } = ctx(() => new Response(bytes, { status: 200, headers: { 'content-type': 'application/pdf', 'content-disposition': 'attachment; filename="DRAFT_accounts_2.pdf"' } }), { downloadDir: dir, allowPersonalData: true });
  const out = await runTool(tool('accounts_package_download'), context, { companyId: 1, periodId: 2 }) as { saved: { path: string; sha256: string; bytes: number; filename: string } };
  assert.equal(out.saved.sha256, createHash('sha256').update(bytes).digest('hex'));
  assert.equal(out.saved.filename, 'DRAFT_accounts_2.pdf');
  assert.deepEqual(readFileSync(out.saved.path), bytes);
  const again = await runTool(tool('accounts_package_download'), context, { companyId: 1, periodId: 2 }) as { saved: { filename: string } };
  assert.equal(again.saved.filename, 'DRAFT_accounts_2-1.pdf', 'never overwrites');
});

test('the CSV import reads only under the upload root and posts multipart', async () => {
  const root = mkdtempSync(join(tmpdir(), 'fb-csv-'));
  writeFileSync(join(root, 'aib.csv'), 'Date,Description,Amount\n');
  const { context, calls } = ctx(() => json(200, { imported: 0 }), { uploadRoot: root });
  const out = await runTool(tool('bank_statement_import'), context, { companyId: 1, bankAccountId: 4, periodId: 2, file: 'aib.csv' }) as { source: { file: string } };
  assert.equal(out.source.file, 'aib.csv');
  assert.match(calls[0]!.url, /\/api\/companies\/1\/bank-accounts\/4\/import\?periodId=2$/);
  assert.ok(calls[0]!.body instanceof FormData);
  await assert.rejects(runTool(tool('bank_statement_import'), context, { companyId: 1, bankAccountId: 4, periodId: 2, file: '../x.csv' }));
});

test('credential tools hand the credential back with a hand-over note', async () => {
  const { context } = ctx(() => json(201, { user: { userId: 5, email: 'a@b.ie', displayName: 'A', role: 'Client' }, actionToken: 'tok-123', expiresAtUtc: '2026-10-20T00:00:00Z' }), { allowPersonalData: true });
  const out = await runTool(tool('user_password_reset'), context, { userId: 5, reason: 'locked out' }) as { result: { actionToken: string }; credentialHandoff: { note: string } };
  assert.equal(out.result.actionToken, 'tok-123');
  assert.match(out.credentialHandoff.note, /credential for the person/);
  const created = await runTool(tool('user_create'), context, { email: 'a@b.ie', displayName: 'A', role: 'Client', temporaryPassword: 'Temporary-Password-2026!x', companyIds: [1], reason: 'new staff' }) as { credentialHandoff: { temporaryPassword: string } };
  assert.equal(created.credentialHandoff.temporaryPassword, 'Temporary-Password-2026!x');
});

test('personal field paths are known for the tools that write them', () => {
  assert.deepEqual(personalFieldPaths(tool('officer_create')), ['address']);
  assert.ok(personalFieldPaths(tool('company_onboard')).includes('officers.address'));
  assert.ok(personalFieldPaths(tool('company_onboard')).includes('company.registeredOfficeAddress1'));
});
