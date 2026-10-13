import { test } from 'node:test';
import assert from 'node:assert/strict';
import { accessTokenExpiresAtMs } from '../access-token.js';
import { CALLER_ETAG_TTL_MS, CallerETagCaches } from '../etag-cache.js';
import { POSTURE_TTL_MS } from '../http-server.js';
import { json, type Recorded } from './helpers.js';
import {
  MCP_ACCEPT,
  PUBLIC_URL,
  accessToken,
  defaultApi,
  raw,
  rpc,
  startService,
} from './http-helpers.js';

/**
 * State the HTTP service keeps for one caller across that caller's requests,
 * and the bearer's own expiry: Greptile's findings on PR #88.
 */

const METADATA_URL = `${PUBLIC_URL}/.well-known/oauth-protected-resource/mcp`;
const INVALID_CHALLENGE = `Bearer error="invalid_token", resource_metadata="${METADATA_URL}"`;
const DEBTORS = '/api/companies/1/periods/2/debtors';

const ALICE = accessToken({ userId: 4, tag: 'alice' });
const BOB = accessToken({ userId: 5, tag: 'bob' });

function headersFor(port: number, token: string): Record<string, string> {
  return { host: `127.0.0.1:${port}`, authorization: `Bearer ${token}`, accept: MCP_ACCEPT, 'content-type': 'application/json' };
}

/** The API with one period whose reads hand out a version; writes are recorded. */
function periodApi(version: () => string) {
  return (request: Recorded) => {
    if (request.url.endsWith(DEBTORS) && request.method === 'GET') {
      return json(200, [], { etag: version() });
    }
    if (request.url.endsWith(DEBTORS) && request.method === 'POST') {
      return json(201, { id: 9, name: 'Acme', amount: 10, type: 'Trade' }, { etag: '"after-write"' });
    }
    return defaultApi(request, 0);
  };
}

const LIST = rpc('tools/call', { name: 'debtor_list', arguments: { companyId: 1, periodId: 2 } });
const CREATE = rpc('tools/call', {
  name: 'debtor_create',
  arguments: { companyId: 1, periodId: 2, name: 'Acme', amount: 10, type: 'Trade' },
});

function writes(calls: Recorded[]): Recorded[] {
  return calls.filter((c) => c.method === 'POST' && c.url.endsWith(DEBTORS));
}

test('a write in a later request carries the If-Match its caller learned on an earlier read', async () => {
  const service = await startService(periodApi(() => '"v1"'));
  try {
    const read = await raw(service.port, 'POST', '/mcp', headersFor(service.port, ALICE), LIST);
    assert.equal(read.status, 200, read.text);
    const write = await raw(service.port, 'POST', '/mcp', headersFor(service.port, ALICE), CREATE);
    assert.equal(write.status, 200, write.text);
    assert.notEqual((write.json() as { result: { isError?: boolean } }).result.isError, true, write.text);

    const sent = writes(service.calls);
    assert.equal(sent.length, 1);
    assert.equal(sent[0]!.headers['if-match'], '"v1"', 'the read\'s version guards the later write');
  } finally {
    await service.close();
  }
});

test('another caller never gets the first caller\'s If-Match', async () => {
  const service = await startService(periodApi(() => '"v1"'));
  try {
    assert.equal((await raw(service.port, 'POST', '/mcp', headersFor(service.port, ALICE), LIST)).status, 200);
    const write = await raw(service.port, 'POST', '/mcp', headersFor(service.port, BOB), CREATE);
    assert.equal(write.status, 200, write.text);

    const sent = writes(service.calls);
    assert.equal(sent.length, 1);
    assert.equal(sent[0]!.headers['authorization'], `Bearer ${BOB}`);
    assert.equal(sent[0]!.headers['if-match'], undefined, 'Bob has read nothing, so his write claims no version');
  } finally {
    await service.close();
  }
});

test('a caller\'s versions are forgotten once the API refuses the bearer', async () => {
  let refuse = false;
  const api = periodApi(() => '"v1"');
  const service = await startService((request) => {
    if (refuse && request.headers['authorization'] === `Bearer ${ALICE}`) return json(401, { code: 'CONNECTOR_SESSION_INVALID' });
    return api(request);
  });
  try {
    assert.equal((await raw(service.port, 'POST', '/mcp', headersFor(service.port, ALICE), LIST)).status, 200);
    refuse = true;
    const refused = await raw(service.port, 'POST', '/mcp', headersFor(service.port, ALICE), LIST);
    assert.equal(refused.status, 401);
    refuse = false;
    // The same bearer accepted again (say, a transient refusal): its old versions are gone.
    const write = await raw(service.port, 'POST', '/mcp', headersFor(service.port, ALICE), CREATE);
    assert.equal(write.status, 200, write.text);
    assert.equal(writes(service.calls)[0]!.headers['if-match'], undefined);
  } finally {
    await service.close();
  }
});

test('an expired access token is answered 401 invalid_token before anything is asked of the API', async () => {
  const clock = 2_000_000_000_000; // ms
  const service = await startService(defaultApi, { now: () => clock });
  try {
    const expired = accessToken({ expiresAtUnixSeconds: clock / 1000 - 1 });
    const atTheSecond = accessToken({ expiresAtUnixSeconds: clock / 1000 });
    for (const token of [expired, atTheSecond]) {
      const response = await raw(service.port, 'POST', '/mcp', headersFor(service.port, token), rpc('tools/list'));
      assert.equal(response.status, 401);
      assert.equal(response.headers['www-authenticate'], INVALID_CHALLENGE);
      assert.equal((response.json() as { error: string }).error, 'invalid_token');
    }
    assert.equal(service.calls.length, 0);
  } finally {
    await service.close();
  }
});

test('a token whose expiry cannot be read is 401 invalid_token without asking the API', async () => {
  const service = await startService();
  try {
    const unreadable = [
      'fbc1.7.not-a-payload-aaaaaaaaaaaa',
      `fbc1.${Buffer.from('not json').toString('base64url')}.c2ln`,
      accessToken({ expiresAtUnixSeconds: undefined }),
      accessToken({ expiresAtUnixSeconds: '4102444800' }),
      accessToken({ expiresAtUnixSeconds: 1.5e300 }),
      `fbc1.${Buffer.from(JSON.stringify({ expiresAtUnixSeconds: 4102444800 })).toString('base64url')}`,
    ];
    for (const token of unreadable) {
      const response = await raw(service.port, 'POST', '/mcp', headersFor(service.port, token), rpc('tools/list'));
      assert.equal(response.status, 401, token);
      assert.equal(response.headers['www-authenticate'], INVALID_CHALLENGE);
    }
    assert.equal(service.calls.length, 0);
  } finally {
    await service.close();
  }
});

test('a cached posture does not outlive its token: the client is challenged so it can refresh', async () => {
  let clock = 2_000_000_000_000;
  const service = await startService(defaultApi, { now: () => clock });
  try {
    const shortLived = accessToken({ expiresAtUnixSeconds: clock / 1000 + 10 });
    assert.equal((await raw(service.port, 'POST', '/mcp', headersFor(service.port, shortLived), rpc('tools/list'))).status, 200);
    const asked = service.calls.length;

    clock += 11_000; // past the token, well inside the posture TTL
    assert.ok(11_000 < POSTURE_TTL_MS);
    const response = await raw(service.port, 'POST', '/mcp', headersFor(service.port, shortLived), rpc('tools/call', { name: 'company_list', arguments: {} }));
    assert.equal(response.status, 401);
    assert.equal(response.headers['www-authenticate'], INVALID_CHALLENGE);
    assert.equal(service.calls.length, asked, 'nothing more was sent to the API');
  } finally {
    await service.close();
  }
});

test('a tool call the API refuses with 401 is answered with the HTTP 401 challenge, not a tool error', async () => {
  let revoked = false;
  const service = await startService((request) => {
    if (revoked) return json(401, { code: 'CONNECTOR_SESSION_INVALID' });
    return defaultApi(request, 0);
  });
  try {
    const headers = headersFor(service.port, ALICE);
    assert.equal((await raw(service.port, 'POST', '/mcp', headers, rpc('tools/list'))).status, 200, 'posture now cached');
    revoked = true;
    const call = await raw(service.port, 'POST', '/mcp', headers, rpc('tools/call', { name: 'company_list', arguments: {} }));
    assert.equal(call.status, 401, call.text);
    assert.equal(call.headers['www-authenticate'], INVALID_CHALLENGE);
    assert.equal((call.json() as { error: string }).error, 'invalid_token');
  } finally {
    await service.close();
  }
});

test('caller ETag caches are bounded, expire with the token (at most the access-token lifetime) and never cross keys', () => {
  let clock = 1_000_000;
  const caches = new CallerETagCaches(() => clock, 2);
  const a = caches.for('a', clock + 5_000);
  a.remember(DEBTORS, '"va"');
  assert.equal(caches.for('a', clock + 5_000), a, 'the same caller gets the same cache');
  assert.equal(caches.for('b', clock + 5_000).ifMatchFor(DEBTORS), undefined, 'another caller starts empty');

  clock += 5_000;
  assert.notEqual(caches.for('a', clock + 5_000), a, 'gone once the token has expired');

  clock = 0;
  const longLived = caches.for('c', Number.MAX_SAFE_INTEGER);
  clock += CALLER_ETAG_TTL_MS;
  assert.notEqual(caches.for('c', Number.MAX_SAFE_INTEGER), longLived, 'capped at the access-token lifetime');

  caches.for('d', Number.MAX_SAFE_INTEGER);
  caches.for('e', Number.MAX_SAFE_INTEGER);
  assert.ok(caches.size() <= 2, 'bounded');
  const e = caches.for('e', Number.MAX_SAFE_INTEGER);
  caches.drop('e');
  assert.notEqual(caches.for('e', Number.MAX_SAFE_INTEGER), e, 'dropped on refusal');
});

test('the access-token expiry is read from FilingBridge\'s payload shape only', () => {
  assert.equal(accessTokenExpiresAtMs(accessToken({ expiresAtUnixSeconds: 1_900_000_000 })), 1_900_000_000_000);
  assert.equal(accessTokenExpiresAtMs('fbc1.7.x'), null);
  assert.equal(accessTokenExpiresAtMs('fbr1.' + accessToken().slice(5)), null);
  assert.equal(accessTokenExpiresAtMs(accessToken({ expiresAtUnixSeconds: -5 })), null);
  assert.equal(accessTokenExpiresAtMs(`fbc1.${Buffer.from('[1]').toString('base64url')}.c2ln`), null);
});
