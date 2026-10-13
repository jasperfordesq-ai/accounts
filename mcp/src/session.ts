import type { CredentialStore } from './credentials.js';
import type { AccessLevel, DataScope } from './config.js';
import { registerSecret } from './redact.js';
import { CLIENT_HEADER_VALUE } from './version.js';
import { withConnectorRefreshLock } from './refresh-coordination.js';

/**
 * Identifies this as the connector rather than a browser.
 *
 * FilingBridge's connector routes require it, and a web page cannot send it
 * without a CORS preflight that is refused. The connector also never sends an
 * Origin, a Referer or a cookie: their presence is evidence of a browser, and
 * the API refuses connector routes that carry any of them.
 */
export const CLIENT_HEADER = 'x-filingbridge-client';

const PREFIX = '/api/auth/connector';

/** Refresh this long before the access token's stated expiry. */
const EXPIRY_SKEW_MS = 30_000;

export class NotConnectedError extends Error {
  readonly code = 'NOT_CONNECTED';
  constructor(message = 'Not connected. Run: filingbridge-mcp connect') {
    super(message);
    this.name = 'NotConnectedError';
  }
}

/** The account needs its authenticator (or a recovery code) to sign in. */
export class SecondFactorRequiredError extends Error {
  readonly code = 'SECOND_FACTOR_REQUIRED';
  constructor(message: string) {
    super(message);
    this.name = 'SecondFactorRequiredError';
  }
}

export interface ConnectorUser {
  userId: number;
  tenantId: number;
  tenantName: string;
  tenantSlug: string;
  email: string;
  displayName: string;
  role: string;
  allowedCompanyIds: number[];
  mustChangePassword: boolean;
  mfaVerified: boolean;
  mfaMethod: string | null;
}

/** The session posture as FilingBridge reports it. */
export interface ConnectorPosture {
  sessionId?: string;
  familyId?: string;
  clientKind?: string;
  accessLevel: string;
  dataScope: string;
  role: string;
  tenantSlug?: string;
  userId?: number;
  mfaVerified?: boolean;
  accessTokenExpiresAtUtc?: string;
  writeBudgetPerMinute?: number;
  approvalMinutes?: number;
}

export interface LoginResult {
  user: ConnectorUser;
  session: ConnectorPosture;
}

/** One approval as FilingBridge describes it, for a person to read before granting. */
export interface ApprovalPreview {
  approvalId: string;
  summary: string;
  method: string;
  routePattern: string;
  resourceId: string | null;
  createdAtUtc: string | null;
  expiresAtUtc: string;
  approvedAtUtc: string | null;
  consumedAtUtc: string | null;
}

export interface LoginInput {
  tenantSlug: string;
  email: string;
  password: string;
  totpCode?: string | undefined;
  recoveryCode?: string | undefined;
  deviceLabel?: string | undefined;
}

interface SessionOptions {
  baseUrl: string;
  store: CredentialStore;
  accessLevel?: AccessLevel;
  dataScope?: DataScope;
  fetchImpl?: typeof fetch;
  /** Replaced in tests; the real one is the cross-process loopback mutex. */
  lock?: <T>(baseUrl: string, action: () => Promise<T>) => Promise<T>;
  now?: () => number;
}

interface TokenPayload {
  accessToken?: unknown;
  accessTokenExpiresAtUtc?: unknown;
  refreshToken?: unknown;
  user?: unknown;
  session?: unknown;
}

const LEVEL_WIRE: Record<AccessLevel, string> = { read: 'Read', write: 'Write', admin: 'Admin' };
const SCOPE_WIRE: Record<DataScope, string> = { withheld: 'Withheld', full: 'Full' };

async function safeJson(response: Response): Promise<Record<string, unknown>> {
  try {
    const parsed = (await response.json()) as unknown;
    return parsed && typeof parsed === 'object' ? (parsed as Record<string, unknown>) : {};
  } catch {
    return {};
  }
}

function stringOr(value: unknown, fallback: string | null): string | null {
  return typeof value === 'string' ? value : fallback;
}

/**
 * The connector credential: sign-in, rotation and the approval grant.
 *
 * The refresh token lives in the credential store, bound to the origin that
 * issued it. The access token lives only in memory and is renewed under a
 * cross-process lock, because a refresh token is single use and FilingBridge
 * revokes the whole session family when one is presented twice.
 */
export class Session {
  readonly #baseUrl: string;
  readonly #accessLevel: AccessLevel;
  readonly #dataScope: DataScope;
  readonly #store: CredentialStore;
  readonly #fetch: typeof fetch;
  readonly #lock: <T>(baseUrl: string, action: () => Promise<T>) => Promise<T>;
  readonly #now: () => number;
  #accessToken: string | null = null;
  #accessExpiresAt = 0;
  #user: ConnectorUser | null = null;
  #posture: ConnectorPosture | null = null;
  #refreshInFlight: Promise<string> | null = null;

  constructor(options: SessionOptions) {
    this.#baseUrl = options.baseUrl.replace(/\/+$/, '');
    this.#accessLevel = options.accessLevel ?? 'write';
    this.#dataScope = options.dataScope ?? 'withheld';
    this.#store = options.store;
    this.#fetch = options.fetchImpl ?? fetch;
    this.#lock = options.lock ?? withConnectorRefreshLock;
    this.#now = options.now ?? Date.now;
  }

  baseUrl(): string {
    return this.#baseUrl;
  }

  user(): ConnectorUser | null {
    return this.#user;
  }

  /** The posture returned with the last sign-in or rotation, if any. */
  lastPosture(): ConnectorPosture | null {
    return this.#posture;
  }

  async login(input: LoginInput): Promise<LoginResult> {
    const response = await this.#post(`${PREFIX}/login`, {
      tenantSlug: input.tenantSlug,
      email: input.email,
      password: input.password,
      accessLevel: LEVEL_WIRE[this.#accessLevel],
      dataScope: SCOPE_WIRE[this.#dataScope],
      ...(input.totpCode ? { totpCode: input.totpCode } : {}),
      ...(input.recoveryCode ? { recoveryCode: input.recoveryCode } : {}),
      ...(input.deviceLabel ? { deviceLabel: input.deviceLabel } : {}),
    });

    if (!response.ok) {
      const body = await safeJson(response);
      const code = typeof body['code'] === 'string' ? body['code'] : null;
      const error = typeof body['error'] === 'string' ? body['error'] : null;

      if (response.status === 404) {
        throw new Error(
          `${this.#baseUrl} has no connector sign-in route. Either it is not a FilingBridge `
            + 'frontend, it runs a build older than this connector, or the connector is disabled '
            + 'there (Connector:Enabled=false).',
        );
      }
      if (response.status === 429) {
        // The credentials were never looked at, and the route limits attempts.
        // Saying "check the password" would spend the little budget that remains.
        throw new Error(
          'Too many sign-in attempts. Wait a minute and run connect again. The password was not '
            + 'checked, so nothing is wrong with it.',
        );
      }
      if (code === 'BROWSER_CLIENT_REJECTED') {
        throw new Error(
          'FilingBridge refused this request before checking any credential, because it looked '
            + 'like it came from a browser. Something between this connector and FilingBridge '
            + 'is adding an Origin, Referer or cookie header.',
        );
      }
      if (code === 'DATA_SCOPE_FORBIDDEN' || code === 'MFA_ENROLMENT_REQUIRED') {
        throw new Error(error ?? (code === 'MFA_ENROLMENT_REQUIRED'
          ? 'This account must enrol an authenticator in the FilingBridge web application before it can connect an agent.'
          : 'Only an Owner may connect with --data-scope full.'));
      }
      if (code === 'SECOND_FACTOR_REQUIRED') {
        throw new SecondFactorRequiredError(
          error ?? 'This account needs its authenticator code, or a saved recovery code, to connect.',
        );
      }
      if (code === 'SECOND_FACTOR_REJECTED') {
        throw new Error(
          'The authenticator or recovery code was not accepted. Wait for the next code (each one works once) and connect again.',
        );
      }
      if (code === 'PASSWORD_CHANGE_REQUIRED') {
        throw new Error(error ?? 'This account must change its password in the FilingBridge web application before it can connect an agent.');
      }
      if (code === 'INVALID_ARGUMENTS') {
        throw new Error(error ?? 'FilingBridge refused the sign-in request as incomplete.');
      }
      if (response.status === 403) {
        throw new Error(
          error
            ? `${error}${code ? ` (${code})` : ''}`
            : 'FilingBridge refused the connector sign-in. The connector may be disabled on this deployment.',
        );
      }
      // Kept vague on purpose: saying which part was wrong tells an attacker
      // which workspaces and addresses exist.
      throw new Error('Sign-in failed. Check the workspace, the email address, the password and any authenticator code.');
    }

    const payload = (await response.json()) as TokenPayload;
    const stored = this.#absorbTokens(payload);
    if (!stored) {
      throw new Error(
        'Sign-in succeeded but FilingBridge did not return both tokens, so nothing was stored. '
          + 'The connector is not talking to FilingBridge the way it expects to.',
      );
    }
    const user = payload.user as ConnectorUser | undefined;
    if (!user || typeof user !== 'object') {
      throw new Error('Sign-in succeeded but FilingBridge did not say who was signed in.');
    }
    this.#user = user;
    return { user, session: this.#posture ?? { accessLevel: LEVEL_WIRE[this.#accessLevel], dataScope: SCOPE_WIRE[this.#dataScope], role: user.role } };
  }

  /** A current access token, refreshing under the cross-process lock when needed. */
  async accessToken(): Promise<string> {
    if (this.#accessToken && this.#now() < this.#accessExpiresAt - EXPIRY_SKEW_MS) {
      return this.#accessToken;
    }
    if (this.#refreshInFlight) return this.#refreshInFlight;
    this.#refreshInFlight = this.#lock(this.#baseUrl, () => this.#refreshLocked()).finally(() => {
      this.#refreshInFlight = null;
    });
    return this.#refreshInFlight;
  }

  /** Drop the cached access token so the next call renews it. */
  invalidateAccessToken(): void {
    this.#accessToken = null;
    this.#accessExpiresAt = 0;
  }

  async #refreshLocked(): Promise<string> {
    const refreshToken = this.#store.read();
    if (!refreshToken) throw new NotConnectedError();

    const endConsumedRefresh = (message: string): never => {
      this.#accessToken = null;
      this.#user = null;
      try {
        // Another process may already have replaced this credential under the
        // lock. Never remove a successor this request did not use.
        if (this.#store.read() === refreshToken) this.#store.clear();
      } catch (cause) {
        throw new NotConnectedError(
          `Session ended, and the stored credential could not be removed: ${(cause as Error).message}`,
        );
      }
      throw new NotConnectedError(message);
    };

    // A rejected fetch does not prove the server never saw the token: it may
    // have rotated it and lost the response. The old single-use token cannot
    // safely be offered again by anyone.
    const response = await this.#post(`${PREFIX}/refresh`, { refreshToken })
      .catch(() => endConsumedRefresh('Session renewal could not be confirmed. Run: filingbridge-mcp connect'));

    if (!response.ok) {
      // These are refused before the token can rotate, so it is still good.
      if ([400, 403, 404, 429].includes(response.status)) {
        throw new Error(
          `Could not renew the session: FilingBridge returned ${response.status}. The stored `
            + 'credential has been kept; try again shortly.',
        );
      }
      endConsumedRefresh('The connector session has ended (revoked, expired or replaced). Run: filingbridge-mcp connect');
    }

    let payload: TokenPayload;
    try {
      payload = (await response.json()) as TokenPayload;
    } catch {
      return endConsumedRefresh('Session renewal returned an unreadable response. Run: filingbridge-mcp connect');
    }
    try {
      // Persist the rotated credential before exposing its access token.
      if (!this.#absorbTokens(payload)) {
        return endConsumedRefresh('Session renewal did not return both replacement tokens. Run: filingbridge-mcp connect');
      }
    } catch {
      return endConsumedRefresh('Session renewal could not save its replacement credential. Run: filingbridge-mcp connect');
    }
    if (payload.user && typeof payload.user === 'object') this.#user = payload.user as ConnectorUser;
    return this.#accessToken!;
  }

  /**
   * Grants one pending approval with the password and authenticator code of
   * the person at the keyboard, using this session's own bearer.
   */
  async approve(
    approvalId: string,
    password: string,
    totpCode: string,
  ): Promise<{ summary: string | null; expiresAtUtc: string | null }> {
    const token = await this.accessToken();
    const response = await this.#fetch(`${this.#baseUrl}${PREFIX}/approve`, {
      method: 'POST',
      headers: {
        'content-type': 'application/json',
        accept: 'application/json',
        authorization: `Bearer ${token}`,
        [CLIENT_HEADER]: CLIENT_HEADER_VALUE,
      },
      body: JSON.stringify({ approvalId, password, totpCode }),
    });
    if (!response.ok) {
      if (response.status === 429) {
        throw new Error('Too many approval attempts. Wait a minute and try again; nothing was approved.');
      }
      // FilingBridge answers every failure identically, so a caller cannot
      // learn which of the conditions was the one that failed.
      throw new Error(
        'That approval could not be granted. Check the password (and the authenticator code, if two-factor sign-in is on) '
          + 'and that the identifier is the one just printed and has not expired. Nothing was approved.',
      );
    }
    const body = await safeJson(response);
    return {
      summary: stringOr(body['summary'], null),
      expiresAtUtc: stringOr(body['expiresAtUtc'], null),
    };
  }

  /**
   * Reads an approval back so it can be shown before any password is asked
   * for. Refuses, rather than proceeding blind, when FilingBridge cannot
   * describe it: an approval nobody has read is one taken on the agent's word.
   */
  async describeApproval(approvalId: string): Promise<ApprovalPreview> {
    const token = await this.accessToken();
    const response = await this.#fetch(
      `${this.#baseUrl}${PREFIX}/approvals/${encodeURIComponent(approvalId)}`,
      {
        method: 'GET',
        headers: {
          accept: 'application/json',
          authorization: `Bearer ${token}`,
          [CLIENT_HEADER]: CLIENT_HEADER_VALUE,
        },
      },
    );
    if (response.status === 404) {
      throw new Error(
        'No pending approval with that identifier belongs to this session. Check it against what '
          + 'the assistant printed; approvals expire a few minutes after they are asked for. Nothing was approved.',
      );
    }
    if (!response.ok) {
      throw new Error(`Could not read the approval: FilingBridge returned ${response.status}. Nothing was approved.`);
    }
    const body = await safeJson(response);
    if (typeof body['approvalId'] !== 'string' || typeof body['summary'] !== 'string') {
      throw new Error('FilingBridge described the approval in a form this connector does not understand. Nothing was approved.');
    }
    const resource = body['resourceId'];
    return {
      approvalId: body['approvalId'],
      summary: body['summary'],
      method: stringOr(body['method'], '?')!,
      routePattern: stringOr(body['routePattern'], '?')!,
      resourceId: typeof resource === 'string' ? resource : typeof resource === 'number' ? String(resource) : null,
      createdAtUtc: stringOr(body['createdAtUtc'], null),
      expiresAtUtc: stringOr(body['expiresAtUtc'], 'unknown')!,
      approvedAtUtc: stringOr(body['approvedAtUtc'], null),
      consumedAtUtc: stringOr(body['consumedAtUtc'], null),
    };
  }

  /** Revokes the session family on the server (best effort) and clears the store. */
  async logout(): Promise<void> {
    let refreshToken: string | null = null;
    try {
      refreshToken = this.#store.read();
    } catch {
      // A store bound to another origin throws; disconnect must still clear it.
      refreshToken = null;
    }
    if (refreshToken) {
      try {
        await this.#post(`${PREFIX}/logout`, { refreshToken });
      } catch {
        // Revocation is best effort; the local credential is cleared regardless.
      }
    }
    this.#accessToken = null;
    this.#user = null;
    this.#store.clear();
  }

  #absorbTokens(payload: TokenPayload): boolean {
    const access = typeof payload.accessToken === 'string' && payload.accessToken ? payload.accessToken : null;
    const refresh = typeof payload.refreshToken === 'string' && payload.refreshToken ? payload.refreshToken : null;
    if (!access || !refresh) return false;
    this.#store.write(refresh);
    registerSecret(refresh);
    registerSecret(access);
    this.#accessToken = access;
    const expires = typeof payload.accessTokenExpiresAtUtc === 'string'
      ? Date.parse(payload.accessTokenExpiresAtUtc)
      : Number.NaN;
    // Fifteen minutes is FilingBridge's lifetime; a response that does not say
    // is treated as shorter, so a stale token is renewed rather than presented.
    this.#accessExpiresAt = Number.isFinite(expires) ? expires : this.#now() + 10 * 60_000;
    if (payload.session && typeof payload.session === 'object') {
      this.#posture = payload.session as ConnectorPosture;
    }
    return true;
  }

  #post(path: string, body: unknown): Promise<Response> {
    return this.#fetch(`${this.#baseUrl}${path}`, {
      method: 'POST',
      headers: {
        'content-type': 'application/json',
        accept: 'application/json',
        [CLIENT_HEADER]: CLIENT_HEADER_VALUE,
      },
      body: JSON.stringify(body),
    });
  }
}
