import { test } from 'node:test';
import assert from 'node:assert/strict';
import { ApiClient, ApiError, ApprovalRequiredError } from '../client.js';
import { ConnectionError } from '../errors.js';
import { CLIENT_HEADER_VALUE } from '../version.js';
import { registerSecret } from '../redact.js';
import { describeError, errorResult } from '../results.js';
import { FakeTokens, clientWith, json, stubFetch } from './helpers.js';

const PERIOD = '/api/companies/1/periods/2';

test('every request identifies the connector and never looks like a browser', async () => {
  const { client, calls } = clientWith(() => json(200, {}));
  await client.get('/api/companies');
  await client.request('POST', `${PERIOD}/debtors`, { body: { name: 'x' } });
  for (const call of calls) {
    assert.equal(call.headers['x-filingbridge-client'], CLIENT_HEADER_VALUE);
    assert.match(CLIENT_HEADER_VALUE, /^mcp-connector\/\d+\.\d+\.\d+$/);
    assert.equal(call.headers['authorization'], 'Bearer fbc1.token-one-aaaaaaaa');
    assert.match(call.headers['x-correlation-id'] ?? '', /^[0-9a-f-]{36}$/);
    for (const forbidden of ['origin', 'referer', 'cookie', 'sec-fetch-site', 'sec-fetch-dest']) {
      assert.equal(call.headers[forbidden], undefined, forbidden);
    }
  }
  assert.equal(calls[0]!.headers['idempotency-key'], undefined, 'GET carries no idempotency key');
  assert.match(calls[1]!.headers['idempotency-key'] ?? '', /^[0-9a-f-]{36}$/, 'every POST carries one');
});

test('two POSTs carry different idempotency keys; a correlation id is new per call', async () => {
  const { client, calls } = clientWith(() => json(200, {}));
  await client.request('POST', '/api/companies/1/x');
  await client.request('POST', '/api/companies/1/x');
  assert.notEqual(calls[0]!.headers['idempotency-key'], calls[1]!.headers['idempotency-key']);
  assert.notEqual(calls[0]!.headers['x-correlation-id'], calls[1]!.headers['x-correlation-id']);
});

test('a 401 renews the token once and retries; a second 401 asks for a reconnect', async () => {
  const tokens = new FakeTokens(['fbc1.old-token-aaaa', 'fbc1.new-token-bbbb']);
  const { client, calls } = clientWith((_r, i) => (i === 0 ? json(401, { code: 'CONNECTOR_SESSION_INVALID' }) : json(200, { ok: 1 })), tokens);
  assert.deepEqual(await client.get('/api/companies'), { ok: 1 });
  assert.equal(calls[1]!.headers['authorization'], 'Bearer fbc1.new-token-bbbb');
  assert.equal(tokens.invalidations, 1);

  const always = clientWith(() => json(401, { code: 'CONNECTOR_SESSION_INVALID' }));
  await assert.rejects(always.client.get('/api/companies'), (error: unknown) => {
    assert.ok(error instanceof ApiError);
    assert.equal(error.code, 'CONNECTOR_SESSION_INVALID');
    assert.equal(error.action, 'connect');
    return true;
  });
  assert.equal(always.calls.length, 2, 'retried exactly once');
});

test('a 428 approval refusal becomes ApprovalRequiredError', async () => {
  const { client } = clientWith(() => json(428, {
    error: 'needs approval', code: 'APPROVAL_REQUIRED', approvalId: '0f8fad5b-d9cb-469f-a165-70867728950e',
    summary: 'Delete officer 7 of Acme', resourceId: '7', expiresAt: '2026-10-12T10:05:00Z',
    command: 'filingbridge-mcp approve 0f8fad5b-d9cb-469f-a165-70867728950e', approvalsUrl: null,
  }));
  await assert.rejects(client.request('DELETE', '/api/companies/1/officers/7'), (error: unknown) => {
    assert.ok(error instanceof ApprovalRequiredError);
    assert.equal(error.resourceId, '7');
    const described = describeError(error);
    assert.equal(described.action, 'approve');
    assert.equal(described.command, error.command);
    return true;
  });
});

test('the period ETag is remembered and sent as If-Match; a conflict drops it and asks for a reread', async () => {
  const { client, calls } = clientWith((request, index) => {
    if (index === 0) return json(200, [], { etag: '"v1"' });
    if (index === 1) return json(409, { error: 'Accounting data changed', code: 'accounting_concurrency_conflict' });
    return json(200, {});
  });
  await client.get(`${PERIOD}/debtors`);
  await assert.rejects(client.request('POST', `${PERIOD}/debtors`, { body: {} }), (error: unknown) => {
    assert.ok(error instanceof ApiError);
    assert.equal(error.action, 'reread');
    assert.equal(error.retryable, true);
    return true;
  });
  assert.equal(calls[1]!.headers['if-match'], '"v1"');
  await client.request('POST', `${PERIOD}/debtors`, { body: {} });
  assert.equal(calls[2]!.headers['if-match'], undefined, 'the stale token was dropped');
  assert.equal(calls[0]!.headers['if-match'], undefined, 'reads never send If-Match');
});

test('a 412 is also a reread', async () => {
  const { client } = clientWith(() => json(412, { error: 'precondition failed' }));
  await assert.rejects(client.request('PUT', `${PERIOD}/payroll`, { body: {} }), (error: unknown) => error instanceof ApiError && error.action === 'reread');
});

test('posture refusals are explained and coded', async () => {
  for (const [code, action] of [['SESSION_READ_ONLY', 'reconnect'], ['SESSION_LEVEL_TOO_LOW', 'reconnect'], ['DATA_SCOPE_WITHHELD', 'none'], ['CONNECTOR_NOT_PERMITTED', 'none']] as const) {
    const { client } = clientWith(() => json(403, { error: 'x', code }));
    await assert.rejects(client.get('/api/admin/users'), (error: unknown) => error instanceof ApiError && error.code === code && error.action === action);
  }
});

test('the write budget answers wait, with Retry-After', async () => {
  const { client } = clientWith(() => json(429, { code: 'CONNECTOR_WRITE_LIMIT' }, { 'retry-after': '17' }));
  await assert.rejects(client.request('POST', '/api/companies/1/x'), (error: unknown) => {
    assert.ok(error instanceof ApiError);
    assert.equal(error.retryAfterSeconds, 17);
    assert.equal(error.action, 'wait');
    assert.match(error.message, /17 seconds/);
    return true;
  });
});

test('validation problems are passed back field by field', async () => {
  const { client } = clientWith(() => json(400, { title: 'One or more validation errors occurred.', errors: { legalName: ['Legal name is required.'] } }));
  await assert.rejects(client.request('POST', '/api/companies', { body: {} }), (error: unknown) => {
    assert.ok(error instanceof ApiError);
    assert.deepEqual(error.details, [{ field: 'legalName', message: 'Legal name is required.' }]);
    assert.equal(error.action, 'fix_arguments');
    return true;
  });
});

test('a dropped connection is retried once when the request is safe to repeat', async () => {
  let attempts = 0;
  const stub = stubFetch(() => {
    attempts += 1;
    if (attempts === 1) throw new TypeError('socket hang up');
    return json(200, {});
  });
  const client = new ApiClient({ tokens: new FakeTokens(), baseUrl: 'http://127.0.0.1:3500', fetchImpl: stub.fetch });
  await client.request('POST', '/api/companies/1/x');
  assert.equal(stub.calls[0]!.headers['idempotency-key'], stub.calls[1]!.headers['idempotency-key'], 'the retry reuses the key');

  const put = stubFetch(() => { throw new TypeError('down'); });
  const putClient = new ApiClient({ tokens: new FakeTokens(), baseUrl: 'http://127.0.0.1:3500', fetchImpl: put.fetch });
  await assert.rejects(putClient.request('PUT', '/api/companies/1'), ConnectionError);
  assert.equal(put.calls.length, 1, 'a PUT without a key is not blindly repeated');
});

test('secrets never reach error text', () => {
  registerSecret('fbr1.7.super-secret-refresh-token');
  const result = errorResult(new Error('failed with fbr1.7.super-secret-refresh-token and Bearer fbc1.abc.def'));
  assert.doesNotMatch(result.content[0]!.text, /super-secret|fbc1\.abc/);
});

test('a request outside /api is refused before it is sent', async () => {
  const { client, calls } = clientWith(() => json(200, {}));
  await assert.rejects(client.get('/health'), /outside \/api/);
  assert.equal(calls.length, 0);
});
