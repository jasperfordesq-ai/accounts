import { test } from 'node:test';
import assert from 'node:assert/strict';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { InMemoryTransport } from '@modelcontextprotocol/sdk/inMemory.js';
import { createServer, type ServerOptions } from '../server.js';
import { CLOSED_POSTURE, type SessionPosture } from '../session-level.js';
import { TOOLS } from '../catalogue.js';
import { FakeTokens, json, stubFetch, type Responder } from './helpers.js';

const posture = (accessLevel: SessionPosture['accessLevel'], dataScope: SessionPosture['dataScope']): SessionPosture => ({
  ...CLOSED_POSTURE, accessLevel, dataScope, role: 'Owner', tenantSlug: 'demo',
});

async function connect(responder: Responder, options: Partial<ServerOptions> = {}, held = posture('admin', 'withheld')) {
  const stub = stubFetch(responder);
  const server = createServer({
    baseUrl: 'http://127.0.0.1:3500',
    tokens: new FakeTokens(),
    fetchImpl: stub.fetch,
    posture: async () => held,
    ...options,
  });
  const client = new Client({ name: 'test', version: '1.0.0' });
  const [clientSide, serverSide] = InMemoryTransport.createLinkedPair();
  await Promise.all([server.connect(serverSide), client.connect(clientSide)]);
  return { client, calls: stub.calls };
}

test('initialize carries the instructions with the filing boundary sentence', async () => {
  const { client } = await connect(() => json(200, {}));
  assert.match(client.getInstructions() ?? '', /CRO and ROS final submission remain external actions recorded in workflow state\./);
  assert.equal(client.getServerVersion()?.name, 'filingbridge');
});

test('the listing follows the session level, the data scope and the configured directories', async () => {
  const read = await connect(() => json(200, {}), {}, posture('read', 'withheld'));
  const readNames = (await read.client.listTools()).tools.map((t) => t.name);
  assert.ok(readNames.includes('session_info') && readNames.includes('search') && readNames.includes('fetch'));
  assert.ok(readNames.includes('company_list'));
  assert.ok(!readNames.includes('debtor_create'), 'no writes at read level');
  assert.ok(!readNames.includes('accounts_package_download'), 'no downloads while withheld');

  const full = await connect(() => json(200, {}), { downloadDir: '/tmp/x', uploadRoot: '/tmp/y' }, posture('admin', 'full'));
  const fullNames = (await full.client.listTools()).tools.map((t) => t.name);
  assert.equal(fullNames.length, TOOLS.length + 3, 'everything is offered to a full admin session with both directories');

  const narrowed = await connect(() => json(200, {}), { toolsets: ['year_end'] }, posture('admin', 'full'));
  const narrowedNames = (await narrowed.client.listTools()).tools.map((t) => t.name);
  assert.ok(narrowedNames.includes('debtor_list') && narrowedNames.includes('search'));
  assert.ok(!narrowedNames.includes('company_list'));
});

test('a tool never shown is refused when called anyway, with nothing sent', async () => {
  const { client, calls } = await connect(() => json(200, {}), {}, posture('read', 'withheld'));
  const result = await client.callTool({ name: 'debtor_delete', arguments: { companyId: 1, periodId: 2, id: 3, reason: 'x' } });
  assert.equal(result.isError, true);
  assert.equal((result.structuredContent as { code: string }).code, 'SESSION_LEVEL_TOO_LOW');
  const scoped = await client.callTool({ name: 'directors_report_data', arguments: { companyId: 1, periodId: 2 } });
  assert.equal((scoped.structuredContent as { code: string }).code, 'DATA_SCOPE_WITHHELD');
  assert.equal(calls.length, 0);
});

test('session_info reports the posture and withholds the person under a withheld scope', async () => {
  const { client } = await connect(() => json(200, { accessLevel: 'Write', dataScope: 'Withheld', role: 'Accountant', tenantSlug: 'demo', userId: 4, writeBudgetPerMinute: 30, approvalMinutes: 5 }));
  const result = await client.callTool({ name: 'session_info', arguments: {} });
  const content = result.structuredContent as { session: { accessLevel: string; writeBudgetPerMinute: number }; account: { note: string }; filingBoundary: string };
  assert.equal(content.session.accessLevel, 'write');
  assert.equal(content.session.writeBudgetPerMinute, 30);
  assert.match(content.account.note, /withheld/);
  assert.match(content.filingBoundary, /external actions/);
});

test('search returns Claude.ai-shaped references and fetch reads them through the gate', async () => {
  const responder: Responder = (request) => {
    if (request.url.includes('/api/search')) {
      return json(200, { results: [{ type: 'officer', id: 7, title: 'Aoife Byrne', ref: 'filingbridge://officer/7?companyId=1', companyId: 1, periodId: null }], searchedTypes: ['officer'] });
    }
    if (request.url.endsWith('/api/companies/1/officers')) return json(200, [{ id: 7, companyId: 1, name: 'Aoife Byrne', role: 'Director', address: '1 Main St' }]);
    return json(404, {});
  };
  const { client, calls } = await connect(responder);
  const search = await client.callTool({ name: 'search', arguments: { query: 'Aoife', types: ['officer'] } });
  const results = (search.structuredContent as { results: { id: string; url: string }[] }).results;
  assert.deepEqual(results, [{ id: 'filingbridge://officer/7?companyId=1', title: 'Aoife Byrne', url: 'http://127.0.0.1:3500/companies/1' }]);
  assert.match(calls[0]!.url, /q=Aoife&types=officer/);

  const fetched = await client.callTool({ name: 'fetch', arguments: { id: results[0]!.id } });
  const doc = fetched.structuredContent as { text: string; title: string };
  assert.equal(doc.title, 'Aoife Byrne');
  assert.doesNotMatch(doc.text, /Main St/, 'fetch applies the personal-data gate');
});

test('fetch refuses references it did not issue', async () => {
  const { client } = await connect(() => json(200, {}));
  for (const id of ['https://evil.example/x', 'filingbridge://user/1', 'filingbridge://officer/1', 'filingbridge://company/../1']) {
    const result = await client.callTool({ name: 'fetch', arguments: { id } });
    assert.equal(result.isError, true, id);
  }
});

test('resources and templates read through dispatch, so the gate applies', async () => {
  const { client } = await connect((request) => request.url.endsWith('/api/companies/1')
    ? json(200, { id: 1, legalName: 'Acme', registeredOfficeAddress1: 'home', officers: [{ id: 2, name: 'A', address: 'home' }] })
    : json(200, {}));
  const resources = (await client.listResources()).resources.map((r) => r.uri);
  assert.deepEqual(resources, ['filingbridge://about', 'filingbridge://legal-framework', 'filingbridge://filing-boundary']);
  const templates = (await client.listResourceTemplates()).resourceTemplates.map((t) => t.uriTemplate);
  assert.ok(templates.includes('filingbridge://company/{companyId}/period/{periodId}/filing-status'));
  const company = await client.readResource({ uri: 'filingbridge://company/1' });
  const text = (company.contents[0] as { text: string }).text;
  assert.match(text, /Acme/);
  assert.doesNotMatch(text, /home/);
  const about = await client.readResource({ uri: 'filingbridge://about' });
  assert.match((about.contents[0] as { text: string }).text, /AGPL-3\.0-or-later/);
});

test('prompts are listed and expand with their arguments', async () => {
  const { client } = await connect(() => json(200, {}));
  const names = (await client.listPrompts()).prompts.map((p) => p.name).sort();
  assert.deepEqual(names, ['bank_import_reconcile', 'categorise_uncategorised', 'charity_sorp_review', 'classify_company_size', 'deadline_review', 'director_loan_compliance_review', 'pre_filing_check', 'year_end_walkthrough']);
  const prompt = await client.getPrompt({ name: 'pre_filing_check', arguments: { company_id: '12', period_id: '34' } });
  const text = (prompt.messages[0]!.content as { text: string }).text;
  assert.match(text, /company 12, period 34/);
  assert.match(text, /never files/);
});

test('an approval refusal comes back as a structured error the agent can act on', async () => {
  const { client } = await connect(() => json(428, { code: 'APPROVAL_REQUIRED', approvalId: 'abc-1', summary: 'Remove debtor 3', command: 'filingbridge-mcp approve abc-1', expiresAt: 'soon' }));
  const result = await client.callTool({ name: 'debtor_delete', arguments: { companyId: 1, periodId: 2, id: 3, reason: 'duplicate' } });
  assert.equal(result.isError, true);
  assert.deepEqual(
    { code: (result.structuredContent as { code: string }).code, approvalId: (result.structuredContent as { approvalId: string }).approvalId },
    { code: 'APPROVAL_REQUIRED', approvalId: 'abc-1' },
  );
});
