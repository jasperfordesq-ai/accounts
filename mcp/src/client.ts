import { randomUUID } from 'node:crypto';
import type { TokenSource } from './token-source.js';
import { redactSecrets } from './redact.js';
import { CLIENT_HEADER_VALUE } from './version.js';
import { ConnectionError, type ErrorAction } from './errors.js';
import { ETagCache } from './etag-cache.js';

export const CLIENT_HEADER = 'x-filingbridge-client';
export const REASON_HEADER = 'x-filingbridge-reason';
export const APPROVAL_HEADER = 'x-filingbridge-approval';
export const CORRELATION_HEADER = 'x-correlation-id';
export const IDEMPOTENCY_HEADER = 'idempotency-key';

/** The largest file this connector will accept back from FilingBridge. */
export const MAX_DOWNLOAD_BYTES = 64 * 1024 * 1024;

export interface ValidationDetail {
  field: string;
  message: string;
}

interface ApiErrorExtra {
  code: string | null;
  details: readonly ValidationDetail[];
  retryAfterSeconds: number | null;
  retryable: boolean;
  action: ErrorAction;
  correlationId: string | null;
}

export class ApiError extends Error {
  readonly status: number;
  readonly code: string | null;
  readonly details: readonly ValidationDetail[];
  readonly retryAfterSeconds: number | null;
  readonly retryable: boolean;
  readonly action: ErrorAction;
  readonly correlationId: string | null;

  constructor(status: number, message: string, extra: Partial<ApiErrorExtra> = {}) {
    super(redactSecrets(message));
    this.name = 'ApiError';
    this.status = status;
    this.code = extra.code ?? null;
    this.details = extra.details ?? [];
    this.retryAfterSeconds = extra.retryAfterSeconds ?? null;
    this.retryable = extra.retryable ?? status >= 500;
    this.action = extra.action ?? 'none';
    this.correlationId = extra.correlationId ?? null;
  }
}

/**
 * FilingBridge will not perform this action until a person approves it.
 *
 * Carries what the human needs and nothing the agent could use to approve on
 * their behalf: an identifier, the server's own description of the action,
 * the command to run, and the web page that can grant it instead.
 */
export class ApprovalRequiredError extends Error {
  readonly code = 'APPROVAL_REQUIRED';
  readonly approvalId: string;
  readonly summary: string;
  readonly command: string;
  readonly expiresAt: string;
  readonly resourceId: string | null;
  readonly approvalsUrl: string | null;

  constructor(details: {
    approvalId: string;
    summary: string;
    command: string;
    expiresAt: string;
    resourceId: string | null;
    approvalsUrl: string | null;
  }) {
    super(
      `${details.summary}\n\n`
        + 'FilingBridge will not do this until the person you are acting for approves it. Ask them '
        + `to run this in their own terminal:\n\n    ${details.command}\n\n`
        + (details.approvalsUrl
          ? `or to approve it on the approvals page: ${details.approvalsUrl}\n\n`
          : '')
        + 'They will see what they are approving and confirm it with their password and '
        + 'authenticator code. Then call this tool again with exactly the same arguments plus '
        + `approvalId: ${details.approvalId}. The approval expires at ${details.expiresAt} and `
        + 'covers only this one action. Do not run the command yourself and never ask for the password.',
    );
    this.name = 'ApprovalRequiredError';
    this.approvalId = details.approvalId;
    this.summary = details.summary;
    this.command = details.command;
    this.expiresAt = details.expiresAt;
    this.resourceId = details.resourceId;
    this.approvalsUrl = details.approvalsUrl;
  }
}

export interface RequestOptions {
  query?: Record<string, string | number | boolean | undefined> | undefined;
  body?: unknown;
  /** Why the change is being made. Recorded by FilingBridge against the request. */
  reason?: string | undefined;
  /** An approval identifier granted by a human. */
  approvalId?: string | undefined;
  /** Names this POST so a retry is answered rather than repeated. Generated when absent. */
  idempotencyKey?: string | undefined;
  /** Overrides the cached period token. */
  ifMatch?: string | undefined;
}

export type Method = 'GET' | 'POST' | 'PUT' | 'DELETE' | 'PATCH';

export interface DownloadResult {
  bytes: Buffer;
  contentType: string;
  fileName: string | null;
}

export interface UploadFile {
  fieldName: string;
  fileName: string;
  contentType: string;
  bytes: Buffer;
}

interface ApiClientOptions {
  tokens: TokenSource;
  baseUrl: string;
  fetchImpl?: typeof fetch;
  etags?: ETagCache;
  /**
   * Headers added to every request, before the connector's own. serve --http
   * uses it for the service key (X-Accounts-Api-Key), because it calls the API
   * directly rather than through the frontend proxy that adds the key in
   * stdio mode. Never logged.
   */
  serviceHeaders?: Readonly<Record<string, string>>;
}

const CONNECTOR_REFUSALS: Record<string, { text: string; action: ErrorAction }> = {
  SESSION_READ_ONLY: {
    text: 'This connector session is read-only. The person must reconnect with "filingbridge-mcp connect --access-level write" to change records.',
    action: 'reconnect',
  },
  SESSION_LEVEL_TOO_LOW: {
    text: 'This needs an admin-level session. The person must reconnect with "filingbridge-mcp connect --access-level admin".',
    action: 'reconnect',
  },
  DATA_SCOPE_WITHHELD: {
    text: 'This session withholds personal data, and this request would read or write it. Opening the data scope is a data-protection decision for the Owner, made by reconnecting with --data-scope full; it is not something to work around.',
    action: 'none',
  },
  CONNECTOR_NOT_PERMITTED: {
    text: 'FilingBridge does not let a connector reach this route at all. Use the web application for it.',
    action: 'none',
  },
  BROWSER_CLIENT_REJECTED: {
    text: 'FilingBridge refused the request as if it came from a browser. Something is adding an Origin, Referer or cookie header between the connector and FilingBridge.',
    action: 'none',
  },
  CONNECTOR_SESSION_INVALID: {
    text: 'The connector session is no longer valid. The person must run "filingbridge-mcp connect" again.',
    action: 'connect',
  },
};

function retryAfterFrom(response: Response): number | null {
  const raw = response.headers.get('retry-after');
  if (raw === null) return null;
  const seconds = Number(raw);
  return Number.isInteger(seconds) && seconds >= 0 ? seconds : null;
}

/** ASP.NET validation problems: `errors: { field: [messages] }`. */
function validationDetails(raw: unknown): ValidationDetail[] {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return [];
  const out: ValidationDetail[] = [];
  for (const [field, messages] of Object.entries(raw as Record<string, unknown>)) {
    const list = Array.isArray(messages) ? messages : [messages];
    for (const message of list) {
      if (typeof message !== 'string') continue;
      out.push({ field: field.slice(0, 200) || '(body)', message: message.slice(0, 300) });
      if (out.length >= 20) return out;
    }
  }
  return out;
}

function buildQuery(query: RequestOptions['query']): string {
  if (!query) return '';
  const parts: string[] = [];
  for (const [key, value] of Object.entries(query)) {
    if (value === undefined) continue;
    parts.push(`${encodeURIComponent(key)}=${encodeURIComponent(String(value))}`);
  }
  return parts.length > 0 ? `?${parts.join('&')}` : '';
}

function fileNameFrom(disposition: string | null): string | null {
  if (!disposition) return null;
  const star = /filename\*\s*=\s*(?:UTF-8'')?([^;]+)/i.exec(disposition);
  if (star?.[1]) {
    try {
      return decodeURIComponent(star[1].trim().replace(/^"|"$/g, ''));
    } catch {
      // fall through to the plain parameter
    }
  }
  const plain = /filename\s*=\s*"?([^";]+)"?/i.exec(disposition);
  return plain?.[1]?.trim() ?? null;
}

/**
 * Every request the tools make to FilingBridge.
 *
 * Adds the headers the API's connector path requires, renews an expired
 * access token once, turns 428 into an ApprovalRequiredError, keeps the
 * period concurrency token, and turns every other refusal into an ApiError
 * with a code and a next step.
 */
export class ApiClient {
  readonly #tokens: TokenSource;
  readonly #baseUrl: string;
  readonly #fetch: typeof fetch;
  readonly #serviceHeaders: Readonly<Record<string, string>>;
  readonly etags: ETagCache;

  constructor(options: ApiClientOptions) {
    this.#tokens = options.tokens;
    this.#baseUrl = options.baseUrl.replace(/\/+$/, '');
    this.#fetch = options.fetchImpl ?? fetch;
    this.etags = options.etags ?? new ETagCache();
    this.#serviceHeaders = options.serviceHeaders ?? {};
  }

  get<T>(path: string, options: RequestOptions = {}): Promise<T> {
    return this.request<T>('GET', path, options);
  }

  async request<T>(method: Method, path: string, options: RequestOptions = {}): Promise<T> {
    const response = await this.#send(method, path, options, () =>
      options.body === undefined ? undefined : JSON.stringify(options.body));
    return this.#parse<T>(response);
  }

  /** Fetches a file's bytes. The caller writes them to disk; nothing is parsed. */
  async download(path: string, options: RequestOptions = {}, method: Method = 'GET'): Promise<DownloadResult> {
    const response = await this.#send(method, path, options, () => undefined, '*/*');
    const declared = Number(response.headers.get('content-length') ?? '0');
    if (Number.isFinite(declared) && declared > MAX_DOWNLOAD_BYTES) {
      throw new ApiError(response.status, `The file is larger than ${MAX_DOWNLOAD_BYTES / 1024 / 1024} MB and was not downloaded.`);
    }
    const bytes = Buffer.from(await response.arrayBuffer());
    if (bytes.length > MAX_DOWNLOAD_BYTES) {
      throw new ApiError(response.status, `The file is larger than ${MAX_DOWNLOAD_BYTES / 1024 / 1024} MB and was not kept.`);
    }
    return {
      bytes,
      contentType: response.headers.get('content-type') ?? 'application/octet-stream',
      fileName: fileNameFrom(response.headers.get('content-disposition')),
    };
  }

  /** Posts one file as multipart form data. */
  async upload<T>(path: string, file: UploadFile, options: RequestOptions = {}): Promise<T> {
    const response = await this.#send('POST', path, options, () => {
      // Rebuilt for every attempt: a FormData body cannot be replayed once sent.
      const form = new FormData();
      form.append(
        file.fieldName,
        new Blob([new Uint8Array(file.bytes)], { type: file.contentType }),
        file.fileName,
      );
      return form;
    });
    return this.#parse<T>(response);
  }

  async #send(
    method: Method,
    path: string,
    options: RequestOptions,
    makeBody: () => string | FormData | undefined,
    accept = 'application/json',
  ): Promise<Response> {
    if (!path.startsWith('/api/')) throw new Error(`Refusing a request outside /api: ${path}`);
    const url = `${this.#baseUrl}${path}${buildQuery(options.query)}`;
    const idempotencyKey = method === 'POST' ? (options.idempotencyKey ?? randomUUID()) : options.idempotencyKey;
    const unsafe = method !== 'GET';
    const ifMatch = unsafe ? (options.ifMatch ?? this.etags.ifMatchFor(path)) : undefined;

    let renewed = false;
    let reconnected = false;
    for (;;) {
      const token = await this.#tokens.accessToken();
      const correlationId = randomUUID();
      const body = makeBody();
      const headers: Record<string, string> = {
        ...this.#serviceHeaders,
        authorization: `Bearer ${token}`,
        accept,
        [CLIENT_HEADER]: CLIENT_HEADER_VALUE,
        [CORRELATION_HEADER]: correlationId,
      };
      if (typeof body === 'string') headers['content-type'] = 'application/json';
      if (options.reason) headers[REASON_HEADER] = options.reason.replace(/[\r\n]+/g, ' ').slice(0, 500);
      if (options.approvalId) headers[APPROVAL_HEADER] = options.approvalId;
      if (idempotencyKey) headers[IDEMPOTENCY_HEADER] = idempotencyKey;
      if (ifMatch) headers['if-match'] = ifMatch;

      let response: Response;
      try {
        response = await this.#fetch(url, { method, headers, ...(body === undefined ? {} : { body }) });
      } catch (cause) {
        // A change that met a dropped connection may already have happened.
        // With an idempotency key it is safe to ask once more: FilingBridge
        // either performs it, never having seen the first, or replays the
        // answer to the first. Without one, the failure is reported.
        if (!reconnected && (method === 'GET' || idempotencyKey !== undefined)) {
          reconnected = true;
          continue;
        }
        throw new ConnectionError(
          `Cannot reach FilingBridge at ${this.#baseUrl}. Check the address and, for a Private `
            + `Server, that Tailscale is connected. (${redactSecrets((cause as Error).message)})`,
        );
      }

      this.etags.remember(path, response.headers.get('etag'));

      // A fifteen-minute access token expiring mid-session is the ordinary
      // case; renew once rather than sending the person to reconnect.
      if (response.status === 401 && !renewed && this.#tokens.canRenew) {
        renewed = true;
        this.#tokens.invalidate();
        continue;
      }
      if (response.ok) return response;
      throw await this.#refusal(response, path, correlationId);
    }
  }

  async #parse<T>(response: Response): Promise<T> {
    if (response.status === 204) return { ok: true, status: 204 } as T;
    const text = await response.text();
    if (text.trim().length === 0) return { ok: true, status: response.status } as T;
    try {
      return JSON.parse(text) as T;
    } catch {
      throw new ApiError(
        response.status,
        'FilingBridge returned a response that was not JSON. If a proxy or captive portal sits in between, check the connection.',
      );
    }
  }

  async #refusal(response: Response, path: string, correlationId: string): Promise<Error> {
    let body: Record<string, unknown> = {};
    try {
      const parsed = (await response.json()) as unknown;
      if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) body = parsed as Record<string, unknown>;
    } catch {
      body = {};
    }
    const code = typeof body['code'] === 'string' ? body['code'] : null;
    const serverText = typeof body['error'] === 'string'
      ? body['error']
      : typeof body['detail'] === 'string'
        ? body['detail']
        : typeof body['title'] === 'string' && response.status !== 400
          ? body['title']
          : null;
    const retryAfterSeconds = retryAfterFrom(response);
    const extra = { code, retryAfterSeconds, correlationId };

    if (response.status === 428) {
      const approvalId = typeof body['approvalId'] === 'string' ? body['approvalId'] : null;
      const command = typeof body['command'] === 'string' ? body['command'] : null;
      if (code === 'APPROVAL_REQUIRED' && approvalId && command) {
        return new ApprovalRequiredError({
          approvalId,
          summary: typeof body['summary'] === 'string' ? body['summary'] : 'An action that needs a person to approve it',
          command,
          expiresAt: typeof body['expiresAt'] === 'string' ? body['expiresAt'] : 'shortly',
          resourceId: typeof body['resourceId'] === 'string' || typeof body['resourceId'] === 'number'
            ? String(body['resourceId'])
            : null,
          approvalsUrl: typeof body['approvalsUrl'] === 'string' ? body['approvalsUrl'] : null,
        });
      }
      return new ApiError(428, `FilingBridge needs a precondition before it will do this${code ? ` (${code})` : ''}. ${serverText ?? ''}`.trim(), {
        ...extra,
        action: code === 'APPROVAL_REQUIRED' ? 'approve' : 'none',
      });
    }

    if (response.status === 401) {
      const known = CONNECTOR_REFUSALS['CONNECTOR_SESSION_INVALID']!;
      return new ApiError(401, `${known.text}${code && code !== 'CONNECTOR_SESSION_INVALID' ? ` (${code})` : ''}`, {
        ...extra,
        code: code ?? 'CONNECTOR_SESSION_INVALID',
        action: 'connect',
      });
    }

    if (code && CONNECTOR_REFUSALS[code]) {
      const known = CONNECTOR_REFUSALS[code]!;
      return new ApiError(response.status, `${known.text} (${code})`, { ...extra, action: known.action });
    }

    if (response.status === 409 || response.status === 412) {
      // The period moved since it was read. Drop the token so the re-read
      // collects a current one, rather than repeating the stale If-Match.
      this.etags.forget(path);
      return new ApiError(
        response.status,
        `${serverText ?? 'The record or its accounting period changed since it was read.'}`
          + `${code ? ` (${code})` : ''}\nRead it again, reconcile, and retry with the current values.`,
        { ...extra, retryable: true, action: 'reread' },
      );
    }

    if (response.status === 429) {
      return new ApiError(
        429,
        `${code === 'CONNECTOR_WRITE_LIMIT' ? 'This connector session has used its change budget for the minute.' : 'FilingBridge is rate limiting these requests.'}`
          + ` Wait ${retryAfterSeconds ?? 60} seconds and try again.`,
        { ...extra, retryable: true, action: 'wait' },
      );
    }

    if (response.status === 400) {
      const details = validationDetails(body['errors']);
      const lines = [
        serverText ?? (details.length > 0 ? 'FilingBridge refused the request as invalid.' : `FilingBridge refused the request (400).`),
        ...details.map((d) => `- ${d.field}: ${d.message}`),
        'Correct the request and call again.',
      ];
      return new ApiError(400, lines.join('\n'), { ...extra, details, action: 'fix_arguments' });
    }

    if (response.status === 403) {
      return new ApiError(
        403,
        `${serverText ?? 'FilingBridge refused this for your account.'}${code ? ` (${code})` : ''}\n`
          + 'The account\'s role, or its company assignments, do not allow this. Reconnecting at another level will not change that.',
        { ...extra, action: 'none' },
      );
    }

    if (response.status === 404) {
      return new ApiError(
        404,
        `${serverText ?? 'Not found.'} Check the identifiers against the matching list tool; a company or period you cannot access also answers as not found.`,
        { ...extra, action: 'fix_arguments' },
      );
    }

    if (response.status >= 500) {
      return new ApiError(
        response.status,
        `FilingBridge had an internal problem (${response.status}). Nothing about the request needs to change; try again shortly. Correlation id: ${correlationId}.`,
        { ...extra, retryable: true, action: 'wait' },
      );
    }

    return new ApiError(
      response.status,
      `${serverText ?? `FilingBridge returned ${response.status}.`}${code ? ` (${code})` : ''}`,
      { ...extra, action: 'none' },
    );
  }
}
