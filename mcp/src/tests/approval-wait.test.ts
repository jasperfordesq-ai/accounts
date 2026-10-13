import assert from 'node:assert/strict';
import test from 'node:test';
import type { ApiClient } from '../client.js';
import { ApprovalRequiredError } from '../client.js';
import { APPROVAL_WAIT_TOOL, DEFAULT_WAIT_SECONDS, MAX_WAIT_SECONDS, parseApprovalWaitArgs, runApprovalWait } from '../approval-wait.js';

const ID = '04c0338f-a98e-4800-9815-327b0fa63dfc';
const T0 = Date.parse('2026-10-13T11:00:00Z');

interface View {
  approvedAtUtc?: string | null;
  consumedAtUtc?: string | null;
  expiresAtUtc?: string;
}

/** A fake client whose approval answers come from a script, and a clock that sleeping advances. */
function harness(views: View[]) {
  let clock = T0;
  const paths: string[] = [];
  let index = 0;
  const client = {
    async get(path: string) {
      paths.push(path);
      const view = views[Math.min(index, views.length - 1)]!;
      index += 1;
      return {
        approvalId: ID,
        summary: 'Delete debtor #1, Trade, EUR 100.00',
        expiresAtUtc: view.expiresAtUtc ?? new Date(T0 + 15 * 60_000).toISOString(),
        approvedAtUtc: view.approvedAtUtc ?? null,
        consumedAtUtc: view.consumedAtUtc ?? null,
      };
    },
  } as unknown as ApiClient;
  const deps = {
    now: () => clock,
    sleep: async (ms: number) => {
      clock += ms;
    },
  };
  return { client, deps, paths, calls: () => index };
}

test('approval_wait returns approved as soon as the person grants it, then tells the agent to repeat the call', async () => {
  const { client, deps, paths, calls } = harness([{}, {}, { approvedAtUtc: '2026-10-13T11:00:07Z' }]);
  const result = await runApprovalWait(client, { approvalId: ID }, deps);
  assert.equal(result.status, 'approved');
  assert.equal(calls(), 3, 'it polls until the grant appears and no longer');
  assert.equal(paths[0], `/api/auth/connector/approvals/${ID}`);
  assert.match(result.next, /exactly the same arguments plus this approvalId/);
  assert.equal(result.summary, 'Delete debtor #1, Trade, EUR 100.00');
});

test('approval_wait gives up after waitSeconds with status waiting, and asks to be called again', async () => {
  const { client, deps, calls } = harness([{}]);
  const result = await runApprovalWait(client, { approvalId: ID, waitSeconds: 10 }, deps);
  assert.equal(result.status, 'waiting');
  assert.match(result.next, /Call approval_wait again/);
  assert.ok(calls() >= 4 && calls() <= 6, `about one look every three seconds, saw ${calls()}`);
});

test('approval_wait reports an expired approval, including one granted too late to be used', async () => {
  const expired = new Date(T0 - 1000).toISOString();
  const lapsed = await runApprovalWait(harness([{ expiresAtUtc: expired }]).client, { approvalId: ID }, harness([]).deps);
  assert.equal(lapsed.status, 'expired');
  assert.match(lapsed.next, /without approvalId to get a fresh approval/);

  const grantedButLapsed = harness([{ expiresAtUtc: expired, approvedAtUtc: '2026-10-13T10:59:00Z' }]);
  const result = await runApprovalWait(grantedButLapsed.client, { approvalId: ID }, grantedButLapsed.deps);
  assert.equal(result.status, 'expired', 'a grant that can no longer be spent must not read as approved');
});

test('approval_wait reports a used approval rather than inviting a replay', async () => {
  const { client, deps } = harness([{ approvedAtUtc: '2026-10-13T11:00:01Z', consumedAtUtc: '2026-10-13T11:00:02Z' }]);
  const result = await runApprovalWait(client, { approvalId: ID }, deps);
  assert.equal(result.status, 'used');
  assert.match(result.next, /cannot be used again/);
});

test('approval_wait stops waiting when the call is cancelled', async () => {
  const controller = new AbortController();
  const { client, deps, calls } = harness([{}]);
  const result = await runApprovalWait(client, { approvalId: ID }, {
    ...deps,
    signal: controller.signal,
    sleep: async (ms) => {
      await deps.sleep(ms);
      controller.abort();
    },
  });
  assert.equal(result.status, 'waiting');
  assert.equal(calls(), 1, 'nothing more is fetched once the call is cancelled');
});

test('approval_wait refuses arguments it does not know and identifiers FilingBridge never issues', () => {
  assert.throws(() => parseApprovalWaitArgs({}), /approvalId must be the identifier/);
  assert.throws(() => parseApprovalWaitArgs({ approvalId: '../../admin' }), /approvalId must be the identifier/);
  assert.throws(() => parseApprovalWaitArgs({ approvalId: ID, password: 'x' }), /Unknown argument "password"/);
  assert.throws(() => parseApprovalWaitArgs({ approvalId: ID, waitSeconds: MAX_WAIT_SECONDS + 1 }), /waitSeconds/);
  assert.throws(() => parseApprovalWaitArgs({ approvalId: ID, waitSeconds: 1.5 }), /waitSeconds/);
  assert.deepEqual(parseApprovalWaitArgs({ approvalId: ID }), { approvalId: ID, waitMs: DEFAULT_WAIT_SECONDS * 1000 });
});

test('approval_wait always answers before the 60-second request timeout MCP clients apply', () => {
  // The answer is one JSON body, so nothing reaches the client until the wait ends: a wait the
  // client gives up on first is reported to the agent as a timeout, not as "waiting".
  const CLIENT_REQUEST_TIMEOUT_SECONDS = 60;
  const MARGIN_SECONDS = 10;
  assert.ok(MAX_WAIT_SECONDS + MARGIN_SECONDS <= CLIENT_REQUEST_TIMEOUT_SECONDS);
  assert.ok(DEFAULT_WAIT_SECONDS <= MAX_WAIT_SECONDS);
  assert.equal(APPROVAL_WAIT_TOOL.inputSchema.properties.waitSeconds.maximum, MAX_WAIT_SECONDS);
  assert.match(APPROVAL_WAIT_TOOL.description, new RegExp(`default ${DEFAULT_WAIT_SECONDS}, at most ${MAX_WAIT_SECONDS}\\)`));
});

test('approval_wait is read-only and cannot grant: it never sends anything but a GET', async () => {
  assert.equal(APPROVAL_WAIT_TOOL.annotations.readOnlyHint, true);
  assert.equal(APPROVAL_WAIT_TOOL.inputSchema.additionalProperties, false);
  assert.deepEqual(Object.keys(APPROVAL_WAIT_TOOL.inputSchema.properties).sort(), ['approvalId', 'waitSeconds']);
});

test('an approval refusal tells the agent to show the link and then wait, not to ask the person to report back', () => {
  const error = new ApprovalRequiredError({
    approvalId: ID,
    summary: 'Delete debtor #1',
    command: `filingbridge-mcp approve ${ID}`,
    expiresAt: '2026-10-13T11:15:00Z',
    resourceId: '1',
    approvalsUrl: `http://localhost:3500/settings/approvals?id=${ID}`,
  });
  assert.match(error.message, /^APPROVAL_REQUIRED: Delete debtor #1/);
  assert.match(error.message, /approve it in their browser:\n\n {4}http:\/\/localhost:3500\/settings\/approvals\?id=/);
  assert.match(error.message, /call approval_wait with approvalId/);
  assert.match(error.message, /never ask for the password or authenticator code/);
  // Two-factor sign-in can be off (Development and Private Server), so the code is not always asked for.
  assert.match(error.message, /They confirm it with their password, and an authenticator code if two-factor sign-in is on\./);
});

test('every agent-facing description of approving says the authenticator code depends on two-factor sign-in', async () => {
  const { INSTRUCTIONS } = await import('../instructions.js');
  const { APPROVAL_NOTE } = await import('../tool-helpers.js');
  for (const text of [INSTRUCTIONS, APPROVAL_NOTE]) {
    assert.doesNotMatch(text, /password and (an )?authenticator code\)?[ .;]/);
    assert.match(text, /authenticator code if two-factor sign-in is on/);
  }
  assert.match(APPROVAL_NOTE, /before it happens; see the approval flow/);
});

/** A client whose lookup never answers until its signal fires, like a stalled FilingBridge. */
function stalledClient(seen: { signal?: AbortSignal | undefined }) {
  return {
    get(_path: string, options: { signal?: AbortSignal } = {}) {
      seen.signal = options.signal;
      return new Promise((_resolve, reject) => {
        options.signal?.addEventListener('abort', () => reject(new Error('aborted')), { once: true });
      });
    },
  } as unknown as ApiClient;
}

test('approval_wait keeps to waitSeconds even when FilingBridge stalls mid-lookup', async () => {
  const seen: { signal?: AbortSignal | undefined } = {};
  const started = Date.now();
  const result = await runApprovalWait(stalledClient(seen), { approvalId: ID, waitSeconds: 1 });
  const elapsed = Date.now() - started;
  assert.equal(result.status, 'waiting');
  assert.ok(seen.signal, 'the lookup carries a signal');
  assert.ok(elapsed >= 900 && elapsed < 2500, `returned after ${elapsed} ms`);
});

test('cancelling approval_wait abandons the pending lookup at once', async () => {
  const seen: { signal?: AbortSignal | undefined } = {};
  const controller = new AbortController();
  const started = Date.now();
  const pending = runApprovalWait(stalledClient(seen), { approvalId: ID, waitSeconds: MAX_WAIT_SECONDS }, { signal: controller.signal });
  setTimeout(() => controller.abort(), 50);
  const result = await pending;
  assert.equal(result.status, 'waiting');
  assert.ok(Date.now() - started < 1000, 'cancellation does not wait for the deadline');
  assert.equal(seen.signal?.aborted, true);
});

test('a lookup error that is not a deadline or a cancellation still reaches the agent', async () => {
  const failing = { get: async () => { throw new Error('FilingBridge said no'); } } as unknown as ApiClient;
  await assert.rejects(runApprovalWait(failing, { approvalId: ID, waitSeconds: 5 }), /FilingBridge said no/);
});
