import { test } from 'node:test';
import assert from 'node:assert/strict';
import { PassThrough } from 'node:stream';
import { runCli, type CliIo } from '../commands.js';
import { assertApproveAllowed, assertNonInteractiveConnectAllowed, readPasswordFromStdin } from '../connect-input.js';
import { approvalState, formatApprovalPreview } from '../approval-preview.js';
import { createMemoryStore } from '../credentials.js';
import { json, stubFetch } from './helpers.js';

function io(overrides: Partial<CliIo> = {}): CliIo & { out: () => string } {
  const stdout = new PassThrough();
  let text = '';
  stdout.on('data', (chunk) => { text += String(chunk); });
  return {
    stdin: new PassThrough() as unknown as NodeJS.ReadStream,
    stdout,
    stderr: new PassThrough(),
    env: {},
    stdinIsTty: false,
    stdoutIsTty: false,
    makeStore: () => createMemoryStore(),
    out: () => text,
    ...overrides,
  };
}

test('approve refuses to run without a terminal, before any network call', async () => {
  const stub = stubFetch(() => json(200, {}));
  await assert.rejects(
    runCli(['approve', '0f8fad5b-d9cb-469f-a165-70867728950e', '--profile', 'local'], io({ fetchImpl: stub.fetch, stdinIsTty: false, stdoutIsTty: true })),
    /must be run at a terminal/,
  );
  await assert.rejects(
    runCli(['approve', '0f8fad5b-d9cb-469f-a165-70867728950e', '--profile', 'local'], io({ fetchImpl: stub.fetch, stdinIsTty: true, stdoutIsTty: false })),
    /must be run at a terminal/,
  );
  assert.equal(stub.calls.length, 0);
});

test('approve never accepts a piped password, even on the local profile', () => {
  assert.throws(() => assertApproveAllowed({ approvalId: 'x', passwordStdin: true }, { stdin: true, stdout: true }), /piped password/);
  assert.throws(() => assertApproveAllowed({ passwordStdin: false }, { stdin: true, stdout: true }), /needs the identifier/);
  assert.doesNotThrow(() => assertApproveAllowed({ approvalId: 'x', passwordStdin: false }, { stdin: true, stdout: true }));
});

test('a piped password is only for the local profile and only from a pipe', () => {
  assert.throws(() => assertNonInteractiveConnectAllowed({ profile: 'private', passwordStdin: true }, false), /--profile local/);
  assert.throws(() => assertNonInteractiveConnectAllowed({ profile: 'local', passwordStdin: true }, true), /expects a pipe/);
  assert.doesNotThrow(() => assertNonInteractiveConnectAllowed({ profile: 'local', passwordStdin: true }, false));
});

test('a piped password is decoded once, so split multi-byte characters survive', async () => {
  const bytes = Buffer.from('Siobhán-pw\nrest');
  async function* chunks() { yield bytes.subarray(0, 6); yield bytes.subarray(6); }
  assert.equal(await readPasswordFromStdin(chunks()), 'Siobhán-pw');
});

test('connect over a pipe signs in and reports the posture without echoing the password', async () => {
  const stub = stubFetch(() => json(200, {
    user: { userId: 1, tenantId: 1, tenantName: 'Demo Practice', tenantSlug: 'demo', email: 'o@x.ie', displayName: 'Owner', role: 'Owner', allowedCompanyIds: [], mustChangePassword: false, mfaVerified: true, mfaMethod: 'totp' },
    accessToken: 'fbc1.a-aaaaaaaa', accessTokenExpiresAtUtc: new Date(Date.now() + 9e5).toISOString(),
    refreshToken: 'fbr1.1.r-aaaaaaaa', refreshTokenExpiresAtUtc: new Date(Date.now() + 9e7).toISOString(),
    session: { accessLevel: 'Admin', dataScope: 'Withheld', role: 'Owner', tenantSlug: 'demo' },
  }));
  const stdin = new PassThrough();
  stdin.end('very-secret-password\n');
  const cli = io({ stdin: stdin as unknown as NodeJS.ReadStream, fetchImpl: stub.fetch });
  await runCli(['connect', '--profile', 'local', '--password-stdin', '--tenant-slug', 'demo', '--email', 'o@x.ie', '--totp-code', '123456'], cli);
  assert.match(cli.out(), /Connected as Owner/);
  assert.match(cli.out(), /Access level: ADMIN/);
  assert.match(cli.out(), /Personal data: WITHHELD/);
  assert.doesNotMatch(cli.out(), /very-secret-password/);
  assert.equal((stub.calls[0]!.body as { password: string }).password, 'very-secret-password');
});

test('help and version print without touching the network', async () => {
  const cli = io();
  await runCli(['help'], cli);
  assert.match(cli.out(), /approve <id>/);
  assert.match(cli.out(), /serve --http/);
  assert.match(cli.out(), /FILINGBRIDGE_PUBLIC_URL/);
  const v = io();
  await runCli(['--version'], v);
  assert.match(v.out(), /^filingbridge-mcp \d+\.\d+\.\d+/);
});

test('the approval preview names the host and the server\'s summary, and knows its state', () => {
  const preview = {
    approvalId: 'a', summary: 'Delete officer 7 of Acme Ltd', method: 'DELETE', routePattern: '/api/companies/{companyId}/officers/{id}',
    resourceId: '7', createdAtUtc: '2026-10-12T10:00:00Z', expiresAtUtc: '2026-10-12T10:05:00Z', approvedAtUtc: null, consumedAtUtc: null,
  };
  const text = formatApprovalPreview(preview, 'http://127.0.0.1:3500');
  assert.match(text, /127\.0\.0\.1:3500/);
  assert.match(text, /Delete officer 7 of Acme Ltd/);
  assert.equal(approvalState(preview, new Date('2026-10-12T10:01:00Z')), 'pending');
  assert.equal(approvalState(preview, new Date('2026-10-12T10:06:00Z')), 'expired');
  assert.equal(approvalState({ ...preview, consumedAtUtc: 'x' }, new Date()), 'consumed');
});
