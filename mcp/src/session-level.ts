import type { ApiClient } from './client.js';
import type { AccessLevel, DataScope } from './config.js';
import { levelOf, type ToolDefinition } from './tools.js';

/**
 * What the session is allowed to do, as FilingBridge holds it.
 *
 * Read from GET /api/auth/connector/session, never from a flag this process
 * was started with: the access level and the data scope were chosen when the
 * person typed their password, and the AI client's configuration file is not
 * a place either may be widened.
 *
 * This decides what is offered, which is convenience. FilingBridge refuses
 * what the session may not do whatever the connector offers, and a client
 * can call a tool it was never shown, so dispatch checks again per call.
 */
const RANK: Record<AccessLevel, number> = { read: 0, write: 1, admin: 2 };

export interface SessionPosture {
  accessLevel: AccessLevel;
  dataScope: DataScope;
  role: string;
  tenantSlug: string | null;
  userId: number | null;
  mfaVerified: boolean | null;
  writeBudgetPerMinute: number | null;
  approvalMinutes: number | null;
  familyId: string | null;
  clientKind: string | null;
}

export const SESSION_ROUTE = '/api/auth/connector/session';

function levelFrom(value: unknown): AccessLevel | null {
  const level = String(value ?? '').toLowerCase();
  return level === 'read' || level === 'write' || level === 'admin' ? level : null;
}

function scopeFrom(value: unknown): DataScope | null {
  const scope = String(value ?? '').toLowerCase();
  return scope === 'full' || scope === 'withheld' ? scope : null;
}

export function parsePosture(body: unknown): SessionPosture | null {
  if (!body || typeof body !== 'object') return null;
  const record = body as Record<string, unknown>;
  const accessLevel = levelFrom(record['accessLevel']);
  const dataScope = scopeFrom(record['dataScope']);
  if (!accessLevel || !dataScope) return null;
  const num = (key: string): number | null => (typeof record[key] === 'number' ? (record[key] as number) : null);
  const str = (key: string): string | null => (typeof record[key] === 'string' ? (record[key] as string) : null);
  return {
    accessLevel,
    dataScope,
    role: str('role') ?? 'unknown',
    tenantSlug: str('tenantSlug'),
    userId: num('userId'),
    mfaVerified: typeof record['mfaVerified'] === 'boolean' ? (record['mfaVerified'] as boolean) : null,
    writeBudgetPerMinute: num('writeBudgetPerMinute'),
    approvalMinutes: num('approvalMinutes'),
    familyId: str('familyId'),
    clientKind: str('clientKind'),
  };
}

export async function fetchSessionPosture(client: ApiClient): Promise<SessionPosture> {
  const body = await client.get<unknown>(SESSION_ROUTE);
  const posture = parsePosture(body);
  if (!posture) {
    throw new Error('FilingBridge described the connector session in a form this connector does not understand.');
  }
  return posture;
}

/**
 * The posture when FilingBridge cannot be asked. Fails closed: read level and
 * withheld data, so an outage never widens what is offered.
 */
export const CLOSED_POSTURE: SessionPosture = {
  accessLevel: 'read',
  dataScope: 'withheld',
  role: 'unknown',
  tenantSlug: null,
  userId: null,
  mfaVerified: null,
  writeBudgetPerMinute: null,
  approvalMinutes: null,
  familyId: null,
  clientKind: null,
};

/**
 * Resolves the posture once and keeps it, but keeps only an answer: a
 * failure stands in (closed) for one call and is asked for again on the next.
 */
export function createPostureResolver(fetchPosture: () => Promise<SessionPosture>): () => Promise<SessionPosture> {
  let held: SessionPosture | null = null;
  let resolving: Promise<SessionPosture> | null = null;
  return async () => {
    if (held) return held;
    if (!resolving) {
      resolving = fetchPosture()
        .then((posture) => {
          held = posture;
          return posture;
        })
        .catch(() => CLOSED_POSTURE)
        .finally(() => {
          resolving = null;
        });
    }
    return resolving;
  };
}

export function permits(held: AccessLevel, tool: ToolDefinition): boolean {
  return RANK[held] >= RANK[levelOf(tool)];
}

export function rank(level: AccessLevel): number {
  return RANK[level];
}

export function refusalFor(held: AccessLevel, tool: ToolDefinition): string {
  const needed = levelOf(tool);
  return (
    `${tool.name} needs a connector session with ${needed} access, and this one has ${held} access. Nothing `
    + `was sent. Only the person can change that, by running "filingbridge-mcp connect --access-level ${needed}" `
    + 'and signing in again.'
  );
}

export function fullScopeRefusal(tool: ToolDefinition): string {
  return (
    `${tool.name} needs a session with full data scope, and this session withholds personal data. `
    + `${tool.requiresFullScope ?? ''} Nothing was sent. Opening the data scope is a data-protection decision `
    + 'for the Owner (filingbridge-mcp connect --data-scope full), not something to work around.'
  );
}
