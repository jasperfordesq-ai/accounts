import assert from 'node:assert/strict';
import test from 'node:test';
import type { ApiClient } from '../client.js';
import { ApprovalRequiredError } from '../client.js';
import { APPROVAL_WAIT_TOOL, MAX_WAIT_SECONDS, parseApprovalWaitArgs, runApprovalWait } from '../approval-wait.js';

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
  assert.equal(calls(), 2);
});

test('approval_wait refuses arguments it does not know and identifiers FilingBridge never issues', () => {
  assert.throws(() => parseApprovalWaitArgs({}), /approvalId must be the identifier/);
  assert.throws(() => parseApprovalWaitArgs({ approvalId: '../../admin' }), /approvalId must be the identifier/);
  assert.throws(() => parseApprovalWaitArgs({ approvalId: ID, password: 'x' }), /Unknown argument "password"/);
  assert.throws(() => parseApprovalWaitArgs({ approvalId: ID, waitSeconds: MAX_WAIT_SECONDS + 1 }), /waitSeconds/);
  assert.throws(() => parseApprovalWaitArgs({ approvalId: ID, waitSeconds: 1.5 }), /waitSeconds/);
  assert.deepEqual(parseApprovalWaitArgs({ approvalId: ID }), { approvalId: ID, waitMs: 90_000 });
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
});
