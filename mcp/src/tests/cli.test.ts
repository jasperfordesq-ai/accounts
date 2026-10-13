import { test } from 'node:test';
import assert from 'node:assert/strict';
import { PassThrough } from 'node:stream';
import { runCli, type CliIo } from '../commands.js';
import { assertApproveAllowed, assertNonInteractiveConnectAllowed, readPasswordFromStdin } from '../connect-input.js';
import { approvalState, formatApprovalPreview } from '../approval-preview.js';
import { createMemoryStore } from '../credentials.js';
import { json, stubFetch } from './helpers.js';
import { PromptCancelledError, readSecret } from '../terminal-input.js';
import { approveCommandFor, shellArgument } from '../approve-command.js';
import { parseArgs } from '../config.js';

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
  assert.match(cli.out(), /phase 2/);
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

/** A terminal as far as a prompt can tell: raw mode, isTTY, keystrokes as data. */
class FakeTty extends PassThrough {
  isTTY = true;
  isRaw = false;
  rawModes: boolean[] = [];
  setRawMode(mode: boolean): this {
    this.isRaw = mode;
    this.rawModes.push(mode);
    return this;
  }
}

function terminalOutput() {
  const stdout = new PassThrough();
  let text = '';
  const waiters: { needle: string; resolve: () => void }[] = [];
  stdout.on('data', (chunk) => {
    text += String(chunk);
    for (const waiter of [...waiters]) {
      if (text.includes(waiter.needle)) {
        waiters.splice(waiters.indexOf(waiter), 1);
        waiter.resolve();
      }
    }
  });
  return {
    stdout,
    text: () => text,
    when: (needle: string) => new Promise<void>((resolve) => {
      if (text.includes(needle)) resolve();
      else waiters.push({ needle, resolve });
    }),
  };
}

test('a hidden prompt echoes nothing, handles Backspace, restores the terminal and leaves it readable', { timeout: 5000 }, async () => {
  const stdin = new FakeTty();
  const out = terminalOutput();
  const first = readSecret(stdin, out.stdout, 'Password (not shown): ');
  stdin.write('hunter2-Siobh');
  stdin.write(Buffer.from('á', 'utf8').subarray(0, 1));
  stdin.write(Buffer.concat([Buffer.from('á', 'utf8').subarray(1), Buffer.from('nX\x7f!\r')]));
  assert.equal(await first, 'hunter2-Siobhán!');
  assert.equal(out.text(), 'Password (not shown): \n', 'not one typed character reached the screen');
  assert.equal(stdin.isRaw, false, 'raw mode is restored');
  assert.deepEqual(stdin.rawModes, [true, false]);

  const second = readSecret(stdin, out.stdout, 'Authenticator code (not shown): ');
  stdin.write('123456\r');
  assert.equal(await second, '123456', 'the stream still reads after the first prompt');
  assert.doesNotMatch(out.text(), /hunter2|123456|\*/);

  const cancelled = readSecret(stdin, out.stdout, 'Password (not shown): ');
  stdin.write('abc\x03');
  await assert.rejects(cancelled, PromptCancelledError);
  assert.equal(stdin.isRaw, false);
});

test('connect at a terminal asks for the password and then the authenticator code, showing neither', { timeout: 5000 }, async () => {
  const stub = stubFetch((_request, index) => index === 0
    ? json(401, { code: 'SECOND_FACTOR_REQUIRED', error: 'This account needs its authenticator code.' })
    : json(200, {
      user: { userId: 1, tenantId: 1, tenantName: 'Demo Practice', tenantSlug: 'demo', email: 'o@x.ie', displayName: 'Owner', role: 'Owner', allowedCompanyIds: [], mustChangePassword: false, mfaVerified: true, mfaMethod: 'totp' },
      accessToken: 'fbc1.a-aaaaaaaa', accessTokenExpiresAtUtc: new Date(Date.now() + 9e5).toISOString(),
      refreshToken: 'fbr1.1.r-aaaaaaaa', refreshTokenExpiresAtUtc: new Date(Date.now() + 9e7).toISOString(),
      session: { accessLevel: 'Write', dataScope: 'Withheld', role: 'Owner', tenantSlug: 'demo' },
    }));
  const stdin = new FakeTty();
  const out = terminalOutput();
  void out.when('Password (not shown): ').then(() => stdin.write('typed-secret-pw\r'));
  void out.when('Authenticator or recovery code (not shown): ').then(() => stdin.write('654321\r'));
  await runCli(
    ['connect', '--profile', 'local', '--tenant-slug', 'demo', '--email', 'o@x.ie'],
    io({ stdin: stdin as unknown as NodeJS.ReadStream, stdout: out.stdout, stdinIsTty: true, stdoutIsTty: true, fetchImpl: stub.fetch }),
  );
  assert.match(out.text(), /Connected as Owner/);
  assert.doesNotMatch(out.text(), /typed-secret-pw|654321/);
  assert.equal(stub.calls.length, 2);
  assert.equal((stub.calls[1]!.body as { password: string }).password, 'typed-secret-pw');
  assert.equal((stub.calls[1]!.body as { totpCode: string }).totpCode, '654321');
  assert.equal(stdin.isRaw, false);
});

test('the approve command names the profile and origin, quoted only where a shell needs it', () => {
  assert.equal(
    approveCommandFor('0f8fad5b-d9cb-469f-a165-70867728950e', 'private', 'https://filingbridge.tail1234.ts.net'),
    'filingbridge-mcp approve 0f8fad5b-d9cb-469f-a165-70867728950e --profile private --base-url https://filingbridge.tail1234.ts.net',
  );
  assert.equal(approveCommandFor('abc-1', 'local', 'http://[::1]:3000'), 'filingbridge-mcp approve abc-1 --profile local --base-url "http://[::1]:3000"');
  assert.throws(() => approveCommandFor('abc;rm -rf /', 'local', 'http://localhost:3000'), /not one FilingBridge issues/);
  assert.throws(() => shellArgument('a"b'), /safely/);
  assert.throws(() => shellArgument('$(whoami)'), /safely/);
});

test('approve accepts --profile and --base-url on either side of the identifier', () => {
  for (const argv of [
    ['approve', 'abc-1', '--profile', 'private', '--base-url', 'http://127.0.0.1:3500'],
    ['approve', '--profile', 'private', '--base-url', 'http://127.0.0.1:3500', 'abc-1'],
    ['--profile', 'private', 'approve', 'abc-1', '--base-url', 'http://127.0.0.1:3500'],
  ]) {
    const config = parseArgs(argv, {});
    assert.deepEqual([config.command, config.approvalId, config.profile, config.baseUrl], ['approve', 'abc-1', 'private', 'http://127.0.0.1:3500'], argv.join(' '));
  }
});
