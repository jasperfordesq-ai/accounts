import { test } from 'node:test';
import assert from 'node:assert/strict';
import { Session, NotConnectedError, SecondFactorRequiredError } from '../session.js';
import { bindCredentialToOrigin, createMemoryStore } from '../credentials.js';
import { withConnectorRefreshLock, connectorRefreshLockPort } from '../refresh-coordination.js';
import { json, stubFetch } from './helpers.js';

const BASE = 'http://127.0.0.1:3500';
const noLock = <T>(_base: string, action: () => Promise<T>) => action();

function tokens(n: number) {
  return {
    user: { userId: 1, tenantId: 1, tenantName: 'Demo', tenantSlug: 'demo', email: 'o@x.ie', displayName: 'Owner', role: 'Owner', allowedCompanyIds: [], mustChangePassword: false, mfaVerified: true, mfaMethod: 'totp' },
    accessToken: `fbc1.access-${n}-aaaaaaaa`,
    accessTokenExpiresAtUtc: new Date(Date.now() + 15 * 60_000).toISOString(),
    refreshToken: `fbr1.1.refresh-${n}-bbbbbbbb`,
    refreshTokenExpiresAtUtc: new Date(Date.now() + 86_400_000).toISOString(),
    session: { accessLevel: 'Write', dataScope: 'Withheld', role: 'Owner', tenantSlug: 'demo' },
  };
}

function session(responder: Parameters<typeof stubFetch>[0], initial: string | null = null) {
  const stub = stubFetch(responder);
  const raw = createMemoryStore(initial);
  const store = bindCredentialToOrigin(raw, BASE);
  return { session: new Session({ baseUrl: BASE, store, accessLevel: 'admin', dataScope: 'full', fetchImpl: stub.fetch, lock: noLock }), store, raw, calls: stub.calls };
}

test('login sends the connector body and stores the refresh token bound to the origin', async () => {
  const s = session(() => json(200, tokens(1)));
  const result = await s.session.login({ tenantSlug: 'demo', email: 'o@x.ie', password: 'pw', totpCode: '123456' });
  assert.equal(result.user.role, 'Owner');
  const call = s.calls[0]!;
  assert.equal(call.url, `${BASE}/api/auth/connector/login`);
  assert.deepEqual(call.body, { tenantSlug: 'demo', email: 'o@x.ie', password: 'pw', accessLevel: 'Admin', dataScope: 'Full', totpCode: '123456' });
  assert.equal(call.headers['x-filingbridge-client']?.startsWith('mcp-connector/'), true);
  assert.equal(call.headers['origin'], undefined);
  assert.deepEqual(JSON.parse(s.raw.read()!), { v: 1, origin: BASE, refreshToken: 'fbr1.1.refresh-1-bbbbbbbb' });
  assert.equal(await s.session.accessToken(), 'fbc1.access-1-aaaaaaaa');
});

test('login refusals are told apart without leaking which credential was wrong', async () => {
  const cases: [number, string, RegExp | typeof SecondFactorRequiredError][] = [
    [401, 'INVALID_CREDENTIALS', /Sign-in failed/],
    [401, 'SECOND_FACTOR_REQUIRED', SecondFactorRequiredError],
    [401, 'SECOND_FACTOR_REJECTED', /not accepted/],
    [403, 'MFA_ENROLMENT_REQUIRED', /enrol/i],
    [403, 'DATA_SCOPE_FORBIDDEN', /Owner/],
    [403, 'BROWSER_CLIENT_REJECTED', /browser/],
    [429, 'X', /Too many/],
    [404, 'CONNECTOR_SESSION_INVALID', /no connector sign-in route/],
  ];
  for (const [status, code, expected] of cases) {
    const s = session(() => json(status, { error: code === 'MFA_ENROLMENT_REQUIRED' ? 'Enrol an authenticator first.' : code === 'DATA_SCOPE_FORBIDDEN' ? 'Only an Owner may connect with full data scope.' : 'x', code }));
    await assert.rejects(s.session.login({ tenantSlug: 'd', email: 'e', password: 'p' }), expected as never, code);
    assert.equal(s.raw.read(), null, 'nothing stored');
  }
});

test('an expired access token is renewed with the stored refresh token, which is rotated', async () => {
  const s = session((_r, i) => json(200, tokens(i + 2)), JSON.stringify({ v: 1, origin: BASE, refreshToken: 'fbr1.1.refresh-1-bbbbbbbb' }));
  const token = await s.session.accessToken();
  assert.equal(token, 'fbc1.access-2-aaaaaaaa');
  assert.deepEqual(s.calls[0]!.body, { refreshToken: 'fbr1.1.refresh-1-bbbbbbbb' });
  assert.equal(JSON.parse(s.raw.read()!).refreshToken, 'fbr1.1.refresh-2-bbbbbbbb');
  // Cached until near expiry.
  assert.equal(await s.session.accessToken(), token);
  assert.equal(s.calls.length, 1);
  s.session.invalidateAccessToken();
  assert.equal(await s.session.accessToken(), 'fbc1.access-3-aaaaaaaa');
});

test('concurrent callers share one refresh', async () => {
  const s = session((_r, i) => json(200, tokens(i + 2)), JSON.stringify({ v: 1, origin: BASE, refreshToken: 'fbr1.1.r-aaaaaaaaa' }));
  const results = await Promise.all([s.session.accessToken(), s.session.accessToken(), s.session.accessToken()]);
  assert.equal(new Set(results).size, 1);
  assert.equal(s.calls.length, 1);
});

test('a refused refresh ends the session and clears only the token it used', async () => {
  const s = session(() => json(401, { code: 'CONNECTOR_SESSION_INVALID' }), JSON.stringify({ v: 1, origin: BASE, refreshToken: 'fbr1.1.r-aaaaaaaaa' }));
  await assert.rejects(s.session.accessToken(), NotConnectedError);
  assert.equal(s.raw.read(), null);
});

test('a rate-limited refresh keeps the credential', async () => {
  const stored = JSON.stringify({ v: 1, origin: BASE, refreshToken: 'fbr1.1.r-aaaaaaaaa' });
  const s = session(() => json(429, {}), stored);
  await assert.rejects(s.session.accessToken(), /kept/);
  assert.equal(s.raw.read(), stored);
});

test('a credential bound to another origin is never sent', async () => {
  const s = session(() => json(200, tokens(2)), JSON.stringify({ v: 1, origin: 'https://evil.example', refreshToken: 'fbr1.1.r-aaaaaaaaa' }));
  await assert.rejects(s.session.accessToken(), /will not be sent/);
  assert.equal(s.calls.length, 0);
});

test('not connected without a stored credential', async () => {
  const s = session(() => json(200, {}));
  await assert.rejects(s.session.accessToken(), NotConnectedError);
});

test('approve sends the approval id, password and code with the bearer; refusals are uniform', async () => {
  const s = session((request) => request.url.endsWith('/refresh') ? json(200, tokens(2)) : json(200, { ok: true, approvalId: 'a', summary: 'Delete officer 7', expiresAtUtc: 'x' }),
    JSON.stringify({ v: 1, origin: BASE, refreshToken: 'fbr1.1.r-aaaaaaaaa' }));
  const result = await s.session.approve('0f8fad5b-d9cb-469f-a165-70867728950e', 'pw', '123456');
  assert.equal(result.summary, 'Delete officer 7');
  const call = s.calls.find((c) => c.url.endsWith('/approve'))!;
  assert.deepEqual(call.body, { approvalId: '0f8fad5b-d9cb-469f-a165-70867728950e', password: 'pw', totpCode: '123456' });
  assert.equal(call.headers['authorization'], 'Bearer fbc1.access-2-aaaaaaaa');

  const refused = session((request) => request.url.endsWith('/refresh') ? json(200, tokens(2)) : json(401, { code: 'APPROVAL_REFUSED' }),
    JSON.stringify({ v: 1, origin: BASE, refreshToken: 'fbr1.1.r-aaaaaaaaa' }));
  await assert.rejects(refused.session.approve('x', 'pw', '000000'), /Nothing was approved/);
});

test('logout revokes the family and clears the store even when revocation fails', async () => {
  const s = session(() => { throw new Error('offline'); }, JSON.stringify({ v: 1, origin: BASE, refreshToken: 'fbr1.1.r-aaaaaaaaa' }));
  await s.session.logout();
  assert.equal(s.raw.read(), null);
});

test('the cross-process lock serialises renewals', async () => {
  const base = `http://127.0.0.1:${40000 + Math.floor(Math.random() * 1000)}`;
  assert.ok(connectorRefreshLockPort(base) >= 20000 && connectorRefreshLockPort(base) < 30000);
  const order: string[] = [];
  const slow = withConnectorRefreshLock(base, async () => {
    order.push('a-start');
    await new Promise((r) => setTimeout(r, 120));
    order.push('a-end');
  });
  await new Promise((r) => setTimeout(r, 20));
  await withConnectorRefreshLock(base, async () => { order.push('b'); });
  await slow;
  assert.deepEqual(order, ['a-start', 'a-end', 'b']);
});
