import type { ApiClient } from './client.js';
import { ConnectorError } from './errors.js';

/** The shortest time a single lookup is given, so a 1-second wait can still read the approval once. */
const MIN_LOOKUP_MS = 250;
import type { ToolAnnotations } from './tools.js';

/** Approval identifiers are GUIDs issued by FilingBridge. */
const APPROVAL_ID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export const DEFAULT_WAIT_SECONDS = 90;
export const MAX_WAIT_SECONDS = 120;
const POLL_MS = 3000;

/**
 * Lets the agent wait for the person to approve, instead of asking them to come
 * back and say so. It only reads the approval's state: it cannot grant anything,
 * and the agent still has to repeat the original call itself, carrying the
 * approvalId, for the action to happen.
 */
export const APPROVAL_WAIT_TOOL = {
  name: 'approval_wait',
  description:
    'Wait while the person approves an action FilingBridge refused with APPROVAL_REQUIRED. First show the person '
    + 'the approvalsUrl (or the approve command) from that refusal, then call this with its approvalId. It returns as '
    + 'soon as the approval is granted, used or expired, or after waitSeconds (default 90, at most 120) with status '
    + '"waiting", in which case call it again. When it returns "approved", repeat the refused call with exactly the '
    + 'same arguments plus approvalId. This tool only reads; it cannot approve anything.',
  inputSchema: {
    type: 'object',
    properties: {
      approvalId: { type: 'string', description: 'The approvalId from the APPROVAL_REQUIRED refusal, exactly as given.' },
      waitSeconds: {
        type: 'integer',
        minimum: 1,
        maximum: MAX_WAIT_SECONDS,
        description: `How long to wait before returning "waiting". Default ${DEFAULT_WAIT_SECONDS}.`,
      },
    },
    required: ['approvalId'],
    additionalProperties: false,
  },
  annotations: {
    title: 'Wait for approval',
    readOnlyHint: true,
    destructiveHint: false,
    idempotentHint: true,
    openWorldHint: false,
  } satisfies ToolAnnotations,
};

export type ApprovalWaitStatus = 'approved' | 'waiting' | 'expired' | 'used';

export interface ApprovalWaitResult {
  status: ApprovalWaitStatus;
  approvalId: string;
  summary: string;
  expiresAt: string;
  next: string;
}

interface ParsedArgs {
  approvalId: string;
  waitMs: number;
}

export function parseApprovalWaitArgs(args: Record<string, unknown>): ParsedArgs {
  for (const key of Object.keys(args)) {
    if (key !== 'approvalId' && key !== 'waitSeconds') {
      throw new ConnectorError('INVALID_ARGUMENTS', `Unknown argument "${key}". approval_wait accepts approvalId and waitSeconds.`, { action: 'fix_arguments' });
    }
  }
  const approvalId = args['approvalId'];
  if (typeof approvalId !== 'string' || !APPROVAL_ID.test(approvalId)) {
    throw new ConnectorError('INVALID_ARGUMENTS', 'approvalId must be the identifier FilingBridge returned, exactly as given.', { action: 'fix_arguments' });
  }
  const raw = args['waitSeconds'];
  let seconds = DEFAULT_WAIT_SECONDS;
  if (raw !== undefined) {
    if (typeof raw !== 'number' || !Number.isInteger(raw) || raw < 1 || raw > MAX_WAIT_SECONDS) {
      throw new ConnectorError('INVALID_ARGUMENTS', `waitSeconds must be a whole number from 1 to ${MAX_WAIT_SECONDS}.`, { action: 'fix_arguments' });
    }
    seconds = raw;
  }
  return { approvalId, waitMs: seconds * 1000 };
}

interface ApprovalView {
  summary: string;
  expiresAtUtc: string;
  approvedAtUtc: string | null;
  consumedAtUtc: string | null;
}

function readView(body: unknown): ApprovalView {
  const record = (typeof body === 'object' && body !== null ? body : {}) as Record<string, unknown>;
  const text = (key: string) => (typeof record[key] === 'string' ? (record[key] as string) : null);
  const summary = text('summary');
  const expiresAtUtc = text('expiresAtUtc');
  if (summary === null || expiresAtUtc === null) {
    throw new ConnectorError('UNEXPECTED_RESPONSE', 'FilingBridge answered the approval lookup in an unexpected shape.', { retryable: true });
  }
  return { summary, expiresAtUtc, approvedAtUtc: text('approvedAtUtc'), consumedAtUtc: text('consumedAtUtc') };
}

const NEXT: Record<ApprovalWaitStatus, string> = {
  approved: 'The person approved it. Repeat the refused call now with exactly the same arguments plus this approvalId.',
  waiting: 'Not approved yet. Call approval_wait again with the same approvalId to keep waiting, or ask the person whether they still want this.',
  expired: 'The approval expired before it was granted. Repeat the refused call without approvalId to get a fresh approval, and show the person its new link.',
  used: 'This approval has already been used and cannot be used again. If the action still needs doing, repeat the call without approvalId to get a fresh approval.',
};

export interface ApprovalWaitDeps {
  sleep?: (ms: number, signal?: AbortSignal) => Promise<void>;
  now?: () => number;
  signal?: AbortSignal | undefined;
}

function defaultSleep(ms: number, signal?: AbortSignal): Promise<void> {
  return new Promise((resolve) => {
    if (signal?.aborted) {
      resolve();
      return;
    }
    const timer = setTimeout(done, ms);
    function done() {
      clearTimeout(timer);
      signal?.removeEventListener('abort', done);
      resolve();
    }
    signal?.addEventListener('abort', done, { once: true });
  });
}

export async function runApprovalWait(
  client: ApiClient,
  args: Record<string, unknown>,
  deps: ApprovalWaitDeps = {},
): Promise<ApprovalWaitResult> {
  const { approvalId, waitMs } = parseApprovalWaitArgs(args);
  const sleep = deps.sleep ?? defaultSleep;
  const now = deps.now ?? Date.now;
  const deadline = now() + waitMs;
  const path = `/api/auth/connector/approvals/${encodeURIComponent(approvalId)}`;

  let last: ApprovalView | null = null;
  const waiting = (): ApprovalWaitResult => ({
    status: 'waiting',
    approvalId,
    summary: last?.summary ?? '',
    expiresAt: last?.expiresAtUtc ?? '',
    next: NEXT.waiting,
  });

  for (;;) {
    if (deps.signal?.aborted) return waiting();
    // Each lookup is bounded by what is left of the wait and by cancellation, so a stalled
    // FilingBridge cannot hold the tool past waitSeconds.
    const timeout = new AbortController();
    const timer = setTimeout(() => timeout.abort(), Math.max(MIN_LOOKUP_MS, deadline - now()));
    const lookup = deps.signal ? AbortSignal.any([timeout.signal, deps.signal]) : timeout.signal;
    let body: unknown;
    try {
      body = await client.get<unknown>(path, { signal: lookup });
    } catch (error) {
      if (lookup.aborted) return waiting();
      throw error;
    } finally {
      clearTimeout(timer);
    }
    const view = readView(body);
    last = view;
    // A granted approval that has since expired can no longer be spent, so it reports expired.
    const status: ApprovalWaitStatus | null = view.consumedAtUtc !== null
      ? 'used'
      : Date.parse(view.expiresAtUtc) <= now()
        ? 'expired'
        : view.approvedAtUtc !== null
          ? 'approved'
          : null;
    const remaining = deadline - now();
    if (status !== null) {
      return { status, approvalId, summary: view.summary, expiresAt: view.expiresAtUtc, next: NEXT[status] };
    }
    if (remaining <= 0 || deps.signal?.aborted) return waiting();
    await sleep(Math.min(POLL_MS, remaining), deps.signal);
  }
}
