import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { createServer as createHttpServer, type IncomingMessage, type Server as HttpServer, type ServerResponse } from 'node:http';
import {
  WebStandardStreamableHTTPServerTransport,
  type WebStandardStreamableHTTPServerTransportOptions,
} from '@modelcontextprotocol/sdk/server/webStandardStreamableHttp.js';
import type { Transport } from '@modelcontextprotocol/sdk/shared/transport.js';
import { accessTokenExpiresAtMs } from './access-token.js';
import { ApiClient, ApiError } from './client.js';
import { ConnectionError } from './errors.js';
import { CallerETagCaches } from './etag-cache.js';
import { registerSecret, redactSecrets } from './redact.js';
import { createServer } from './server.js';
import { fetchSessionPosture, type SessionPosture } from './session-level.js';
import { StaticBearer } from './token-source.js';
import type { ToolGroup } from './toolsets.js';
import { CLIENT_HEADER_VALUE } from './version.js';
import { LOOPBACK_HOSTS } from './config.js';

/**
 * `filingbridge-mcp serve --http`: the connector as a remote MCP server.
 *
 * Runs inside the stack (the `mcp` service), reached only through the
 * frontend's /mcp route. Claude.ai, Claude Code and Codex authenticate with
 * FilingBridge's OAuth server and send the resulting connector access token
 * (fbc1.) as a bearer on every request. Nothing here signs anyone in, keeps a
 * credential or refreshes a token: the client owns its tokens, and a refused
 * bearer is answered 401 so the client re-authenticates.
 *
 * Each request gets its own MCP Server around a StaticBearer. What outlives a
 * request is keyed by the SHA-256 of its bearer and lives no longer than that
 * bearer: the posture (≤ POSTURE_TTL_MS) and the caller's period ETags, so a
 * write carries the If-Match its caller's earlier read learned. Nothing is
 * shared between bearers, and a bearer the API refuses loses both at once.
 *
 * A bearer past its own expiry, or one the API refuses during a tool call, is
 * answered with the OAuth 401 challenge rather than a tool error, so the
 * client refreshes its token instead of showing the model a failure. The API is called
 * directly with the service key (X-Accounts-Api-Key) and the caller's bearer;
 * no inbound header other than the bearer is ever forwarded, so a browser's
 * Origin, Referer, Cookie or Sec-Fetch-* never reaches the API.
 */

export const MCP_PATH = '/mcp';
export const HEALTH_PATH = '/healthz';
export const PROTECTED_RESOURCE_PATHS: ReadonlySet<string> = new Set([
  '/.well-known/oauth-protected-resource',
  '/.well-known/oauth-protected-resource/mcp',
]);
export const PROTECTED_RESOURCE_API_ROUTE = '/api/oauth/metadata/protected-resource';
export const API_KEY_HEADER = 'x-accounts-api-key';

/** The largest request body accepted; the CSV import's base64 rides inside it. */
export const MAX_BODY_BYTES = 5 * 1024 * 1024;
/** How long a bearer's posture is trusted before FilingBridge is asked again. */
export const POSTURE_TTL_MS = 60_000;
/** How long the protected-resource document is kept. */
export const METADATA_TTL_MS = 5 * 60_000;
/** How long the locally built document stands in before the API is asked again. */
const METADATA_FALLBACK_TTL_MS = 30_000;
const POSTURE_CACHE_MAX = 1000;
const MAX_BEARER_LENGTH = 4096;

export const SCOPES_SUPPORTED = ['fb:read', 'fb:write', 'fb:admin', 'fb:data-full'] as const;

export interface HttpServiceConfig {
  /** Where the API is reached, e.g. http://api:8080. */
  apiUrl: string;
  /** The service key sent as X-Accounts-Api-Key. Never logged. */
  apiKey: string;
  /** The OAuth issuer: the frontend origin people use, no trailing slash. */
  publicUrl: string;
  /** Host header values accepted on /mcp and the metadata routes (DNS-rebinding protection). */
  allowedHosts: readonly string[];
  host: string;
  port: number;
  toolsets?: readonly ToolGroup[] | undefined;
  maxBodyBytes?: number;
  fetchImpl?: typeof fetch;
  /** One structured line per request; stderr by default. */
  log?: (line: string) => void;
  now?: () => number;
}

function parseOrigin(name: string, value: string): URL {
  let url: URL;
  try {
    url = new URL(value);
  } catch {
    throw new Error(`${name} is not a valid URL: ${value}`);
  }
  if (url.protocol !== 'http:' && url.protocol !== 'https:') throw new Error(`${name} must use http or https.`);
  if (url.username || url.password || url.search || url.hash || (url.pathname !== '/' && url.pathname !== '')) {
    throw new Error(`${name} is an origin only (scheme, host and port), with no path, query or credentials. Got: ${value}`);
  }
  return url;
}

/** The Host values accepted when FILINGBRIDGE_ALLOWED_HOSTS is not set. */
export function defaultAllowedHosts(publicUrl: string, port: number): string[] {
  const hosts = new Set<string>([
    new URL(publicUrl).host.toLowerCase(),
    `mcp:${port}`,
    'localhost',
    `localhost:${port}`,
    `127.0.0.1:${port}`,
  ]);
  return [...hosts];
}

/**
 * The HTTP service's configuration, from the environment:
 *
 * - FILINGBRIDGE_API_URL       the API (default http://api:8080)
 * - FILINGBRIDGE_API_KEY_FILE  file holding the service key (required)
 * - FILINGBRIDGE_PUBLIC_URL    the OAuth issuer / frontend origin (required)
 * - FILINGBRIDGE_ALLOWED_HOSTS comma-separated Host values (optional)
 */
export function httpConfigFromEnv(
  env: NodeJS.ProcessEnv,
  listen: { host: string; port: number; toolsets?: readonly ToolGroup[] | undefined },
  readKey: (path: string) => string = (path) => readFileSync(path, 'utf8'),
): HttpServiceConfig {
  const apiUrl = (env['FILINGBRIDGE_API_URL']?.trim() || 'http://api:8080').replace(/\/+$/, '');
  try {
    const parsed = new URL(apiUrl);
    if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') throw new Error('scheme');
  } catch {
    throw new Error(`FILINGBRIDGE_API_URL is not an http(s) URL: ${apiUrl}`);
  }

  const rawPublic = env['FILINGBRIDGE_PUBLIC_URL']?.trim();
  if (!rawPublic) {
    throw new Error('FILINGBRIDGE_PUBLIC_URL must name the FilingBridge origin people use (the OAuth issuer), such as https://fb.example.');
  }
  const publicUrlParsed = parseOrigin('FILINGBRIDGE_PUBLIC_URL', rawPublic);
  if (publicUrlParsed.protocol === 'http:' && !LOOPBACK_HOSTS.has(publicUrlParsed.hostname)) {
    throw new Error('FILINGBRIDGE_PUBLIC_URL must use https unless it is a loopback address (a Private Server reached on this host).');
  }
  const publicUrl = publicUrlParsed.origin;

  const keyFile = env['FILINGBRIDGE_API_KEY_FILE']?.trim();
  if (!keyFile) throw new Error('FILINGBRIDGE_API_KEY_FILE must name the file holding the connector service key.');
  let apiKey: string;
  try {
    apiKey = readKey(keyFile).trim();
  } catch (cause) {
    throw new Error(`The service key file could not be read (${(cause as NodeJS.ErrnoException).code ?? 'error'}).`);
  }
  if (apiKey.length < 16 || /\s/.test(apiKey)) {
    throw new Error('The service key file must hold one key of at least 16 characters, with no whitespace.');
  }

  const allowedRaw = env['FILINGBRIDGE_ALLOWED_HOSTS']?.trim();
  const allowedHosts = allowedRaw
    ? allowedRaw.split(',').map((h) => h.trim().toLowerCase()).filter(Boolean)
    : defaultAllowedHosts(publicUrl, listen.port);
  if (allowedHosts.length === 0) throw new Error('FILINGBRIDGE_ALLOWED_HOSTS names no host.');

  return { apiUrl, apiKey, publicUrl, allowedHosts, host: listen.host, port: listen.port, toolsets: listen.toolsets };
}

export function resourceUrl(publicUrl: string): string {
  return `${publicUrl}${MCP_PATH}`;
}

export function resourceMetadataUrl(publicUrl: string): string {
  return `${publicUrl}/.well-known/oauth-protected-resource${MCP_PATH}`;
}

/** The RFC 9728 document, built from the issuer when the API cannot provide it. */
export function localProtectedResourceMetadata(publicUrl: string): Record<string, unknown> {
  return {
    resource: resourceUrl(publicUrl),
    authorization_servers: [publicUrl],
    scopes_supported: [...SCOPES_SUPPORTED],
    bearer_methods_supported: ['header'],
    resource_name: 'FilingBridge',
  };
}

interface CachedPosture {
  posture: SessionPosture;
  expires: number;
}

type BearerCheck =
  | { ok: true; posture: SessionPosture }
  | { ok: false; status: number; body: Record<string, unknown>; invalidToken?: boolean };

function tokenKey(token: string): string {
  return createHash('sha256').update(token).digest('hex');
}

function sendJson(res: ServerResponse, status: number, body: unknown, headers: Record<string, string> = {}): void {
  if (res.headersSent) return;
  const text = JSON.stringify(body);
  res.writeHead(status, {
    'content-type': 'application/json',
    'cache-control': 'no-store',
    'x-content-type-options': 'nosniff',
    'content-length': String(Buffer.byteLength(text)),
    ...headers,
  });
  res.end(text);
}

class BodyTooLarge extends Error {}

async function readBody(req: IncomingMessage, limit: number): Promise<Buffer> {
  const declared = Number(req.headers['content-length'] ?? 'NaN');
  if (Number.isFinite(declared) && declared > limit) throw new BodyTooLarge();
  const chunks: Buffer[] = [];
  let total = 0;
  for await (const chunk of req as AsyncIterable<Buffer>) {
    total += chunk.length;
    if (total > limit) throw new BodyTooLarge();
    chunks.push(chunk);
  }
  return Buffer.concat(chunks);
}

/**
 * The inbound request as the SDK's web-standard transport reads it. The body
 * is not attached: it has been read and parsed already and is passed as
 * parsedBody. Only the transport sees these headers; none reaches the API.
 */
function toWebRequest(req: IncomingMessage): Request {
  const headers = new Headers();
  for (const [name, value] of Object.entries(req.headers)) {
    if (value === undefined) continue;
    if (Array.isArray(value)) for (const item of value) headers.append(name, item);
    else headers.set(name, value);
  }
  return new Request(`http://${req.headers['host'] ?? 'connector.invalid'}${req.url ?? '/'}`, {
    method: req.method ?? 'POST',
    headers,
  });
}

/** Writes the transport's answer (JSON or empty, never a stream: enableJsonResponse). */
async function writeWebResponse(res: ServerResponse, response: Response): Promise<void> {
  if (res.headersSent) return;
  const body = Buffer.from(await response.arrayBuffer());
  const headers: Record<string, string> = {};
  response.headers.forEach((value, name) => {
    headers[name] = value;
  });
  headers['content-length'] = String(body.length);
  headers['cache-control'] ??= 'no-store';
  res.writeHead(response.status, headers);
  res.end(body);
}

/** JSON-RPC method names in a body, for the request log (never arguments). */
function rpcSummary(body: unknown): string[] {
  const messages = Array.isArray(body) ? body : [body];
  const out: string[] = [];
  for (const message of messages.slice(0, 20)) {
    if (!message || typeof message !== 'object') continue;
    const record = message as Record<string, unknown>;
    if (typeof record['method'] !== 'string') continue;
    const params = record['params'] as Record<string, unknown> | undefined;
    const tool = record['method'] === 'tools/call' && params && typeof params['name'] === 'string' ? `:${params['name']}` : '';
    out.push(`${record['method']}${tool}`.slice(0, 120));
  }
  return out;
}

export interface HttpHandler {
  (req: IncomingMessage, res: ServerResponse): Promise<void>;
  /** For tests: forget cached postures and metadata. */
  reset(): void;
}

export function createHttpHandler(config: HttpServiceConfig): HttpHandler {
  registerSecret(config.apiKey);
  const baseFetch = config.fetchImpl ?? fetch;
  const now = config.now ?? Date.now;
  const log = config.log ?? ((line: string) => {
    process.stderr.write(`${line}\n`);
  });
  const maxBody = config.maxBodyBytes ?? MAX_BODY_BYTES;
  const allowedHosts = new Set(config.allowedHosts.map((h) => h.toLowerCase()));
  const serviceHeaders = { [API_KEY_HEADER]: config.apiKey };
  const metadataUrl = resourceMetadataUrl(config.publicUrl);
  const challenge = `Bearer resource_metadata="${metadataUrl}"`;
  const invalidChallenge = `Bearer error="invalid_token", resource_metadata="${metadataUrl}"`;

  const postures = new Map<string, CachedPosture>();
  const callerEtags = new CallerETagCaches(now);
  let metadata: { document: Record<string, unknown>; expires: number } | null = null;

  function rememberPosture(key: string, posture: SessionPosture, tokenExpiresAt: number): void {
    if (postures.size >= POSTURE_CACHE_MAX) {
      const oldest = postures.keys().next().value;
      if (oldest !== undefined) postures.delete(oldest);
    }
    // Never trusted past the token itself: an expired token must reach the
    // expiry check below, not ride a cached posture into a tool call.
    postures.set(key, { posture, expires: Math.min(now() + POSTURE_TTL_MS, tokenExpiresAt) });
  }

  /**
   * A fetch that forgets everything kept for a bearer as soon as the API
   * refuses it, and tells the request it belongs to.
   */
  function observingFetch(key: string, onRefused: () => void = () => {}): typeof fetch {
    return (async (input: string | URL | Request, init?: RequestInit) => {
      const response = await baseFetch(input, init);
      if (response.status === 401) {
        postures.delete(key);
        callerEtags.drop(key);
        onRefused();
      }
      return response;
    }) as typeof fetch;
  }

  function clientFor(token: string, key: string): ApiClient {
    return new ApiClient({
      tokens: new StaticBearer(token),
      baseUrl: config.apiUrl,
      fetchImpl: observingFetch(key),
      serviceHeaders,
    });
  }

  const expiredToken: BearerCheck = {
    ok: false,
    status: 401,
    invalidToken: true,
    body: { error: 'invalid_token', error_description: 'The access token has expired; refresh it.' },
  };

  async function checkBearer(token: string, key: string, tokenExpiresAt: number | null): Promise<BearerCheck> {
    // Before any cache or API call: the client can only refresh if it is
    // challenged, and a token past its expiry is refused by the API anyway.
    if (tokenExpiresAt === null) {
      return {
        ok: false,
        status: 401,
        invalidToken: true,
        body: { error: 'invalid_token', error_description: 'The access token is not a FilingBridge connector access token.' },
      };
    }
    if (tokenExpiresAt <= now()) {
      postures.delete(key);
      callerEtags.drop(key);
      return expiredToken;
    }
    const cached = postures.get(key);
    if (cached && cached.expires > now()) return { ok: true, posture: cached.posture };
    if (cached) postures.delete(key);
    try {
      const posture = await fetchSessionPosture(clientFor(token, key));
      rememberPosture(key, posture, tokenExpiresAt);
      return { ok: true, posture };
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) {
        return {
          ok: false,
          status: 401,
          invalidToken: true,
          body: { error: 'invalid_token', error_description: 'The access token is not a current FilingBridge connector session.' },
        };
      }
      if (error instanceof ApiError && error.status === 403) {
        return { ok: false, status: 403, body: { error: 'forbidden', code: error.code } };
      }
      if (error instanceof ApiError && error.status === 404) {
        return { ok: false, status: 503, body: { error: 'connector_unavailable', error_description: 'The FilingBridge connector is not enabled.' } };
      }
      const reason = error instanceof ConnectionError ? 'FilingBridge could not be reached.' : 'FilingBridge did not describe the session.';
      return { ok: false, status: 502, body: { error: 'upstream_unavailable', error_description: reason } };
    }
  }

  async function protectedResourceDocument(): Promise<Record<string, unknown>> {
    if (metadata && metadata.expires > now()) return metadata.document;
    try {
      const response = await baseFetch(`${config.apiUrl}${PROTECTED_RESOURCE_API_ROUTE}`, {
        method: 'GET',
        headers: { accept: 'application/json', ...serviceHeaders, 'x-filingbridge-client': CLIENT_HEADER_VALUE },
        signal: AbortSignal.timeout(5000),
      });
      if (response.ok) {
        const parsed = (await response.json()) as unknown;
        if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)
          && typeof (parsed as Record<string, unknown>)['resource'] === 'string') {
          metadata = { document: parsed as Record<string, unknown>, expires: now() + METADATA_TTL_MS };
          return metadata.document;
        }
      }
    } catch {
      // Fall through to the locally built document.
    }
    metadata = { document: localProtectedResourceMetadata(config.publicUrl), expires: now() + METADATA_FALLBACK_TTL_MS };
    return metadata.document;
  }

  async function handleMcp(req: IncomingMessage, res: ServerResponse, note: (fields: Record<string, unknown>) => void): Promise<void> {
    const method = req.method ?? 'GET';
    if (method !== 'POST' && method !== 'GET' && method !== 'DELETE') {
      sendJson(res, 405, { error: 'method_not_allowed' }, { allow: 'POST, GET, DELETE' });
      return;
    }

    const authorization = req.headers['authorization'];
    const match = typeof authorization === 'string' ? /^Bearer[ ]+(\S+)\s*$/i.exec(authorization) : null;
    if (!match) {
      sendJson(res, 401, { error: 'unauthorized', error_description: 'Sign in to FilingBridge: this resource needs an OAuth access token.' }, {
        'www-authenticate': challenge,
      });
      return;
    }
    const token = match[1]!;
    if (!token.startsWith('fbc1.') || token.length > MAX_BEARER_LENGTH) {
      sendJson(res, 401, { error: 'invalid_token', error_description: 'The access token is not a FilingBridge connector access token.' }, {
        'www-authenticate': invalidChallenge,
      });
      return;
    }

    const key = tokenKey(token);
    const tokenExpiresAt = accessTokenExpiresAtMs(token);
    const check = await checkBearer(token, key, tokenExpiresAt);
    if (!check.ok) {
      sendJson(res, check.status, check.body, check.invalidToken ? { 'www-authenticate': invalidChallenge } : {});
      return;
    }

    if (method !== 'POST') {
      // Stateless: there is no session to stream to (GET) or to end (DELETE).
      sendJson(res, 405, {
        jsonrpc: '2.0',
        error: { code: -32000, message: 'This server is stateless: send JSON-RPC requests with POST.' },
        id: null,
      }, { allow: 'POST' });
      return;
    }

    let parsed: unknown;
    try {
      const raw = await readBody(req, maxBody);
      parsed = JSON.parse(raw.toString('utf8'));
    } catch (error) {
      if (error instanceof BodyTooLarge) {
        sendJson(res, 413, {
          jsonrpc: '2.0',
          error: { code: -32000, message: `The request body is larger than ${maxBody / 1024 / 1024} MiB.` },
          id: null,
        }, { connection: 'close' });
        return;
      }
      sendJson(res, 400, { jsonrpc: '2.0', error: { code: -32700, message: 'The request body is not JSON.' }, id: null });
      return;
    }
    note({ rpc: rpcSummary(parsed) });

    let refused = false;
    const server = createServer({
      baseUrl: config.apiUrl,
      publicUrl: config.publicUrl,
      tokens: new StaticBearer(token),
      serviceHeaders,
      fileMode: 'inline',
      toolsets: config.toolsets,
      fetchImpl: observingFetch(key, () => {
        refused = true;
      }),
      // tokenExpiresAt is known here: checkBearer refuses a token without one.
      etags: callerEtags.for(key, tokenExpiresAt!),
      posture: async () => check.posture,
    });
    // Stateless (sessionIdGenerator left undefined): every request carries
    // its own bearer and gets its own server, so there is no session to keep.
    const transportOptions: WebStandardStreamableHTTPServerTransportOptions = {
      // JSON, not SSE: the whole answer exists before anything is written, so
      // a bearer refused mid-call can still be answered with a 401 challenge.
      enableJsonResponse: true,
      // Checked above as well; the SDK's own check is kept as a second line.
      enableDnsRebindingProtection: true,
      allowedHosts: [...allowedHosts],
    };
    const transport = new WebStandardStreamableHTTPServerTransport(transportOptions);
    let gone = false;
    let settleGone: () => void = () => {};
    const disconnected = new Promise<null>((resolve) => {
      settleGone = () => resolve(null);
    });
    res.on('close', () => {
      gone = true;
      settleGone();
      void transport.close();
      void server.close();
    });
    await server.connect(transport as Transport);
    const response = await Promise.race([
      transport.handleRequest(toWebRequest(req), { parsedBody: parsed }),
      disconnected,
    ]);
    if (gone || response === null) return;

    if (refused) {
      // The API refused this bearer during the call (revoked, or expired in
      // flight). A tool error would leave the client holding a dead token;
      // the challenge makes it refresh and retry.
      note({ refused: 'bearer_during_call' });
      await response.body?.cancel();
      sendJson(res, 401, {
        error: 'invalid_token',
        error_description: 'FilingBridge refused the access token during the request; refresh it and retry.',
      }, { 'www-authenticate': invalidChallenge });
      return;
    }
    await writeWebResponse(res, response);
  }

  const handler = (async (req: IncomingMessage, res: ServerResponse) => {
    const started = now();
    const fields: Record<string, unknown> = {};
    const path = new URL(req.url ?? '/', 'http://connector.invalid').pathname;
    res.on('finish', () => {
      log(redactSecrets(JSON.stringify({
        ts: new Date().toISOString(),
        event: 'http_request',
        method: req.method,
        path: path.slice(0, 200),
        status: res.statusCode,
        ms: now() - started,
        ...fields,
      })));
    });

    try {
      if (path === HEALTH_PATH) {
        if (req.method !== 'GET' && req.method !== 'HEAD') {
          sendJson(res, 405, { error: 'method_not_allowed' }, { allow: 'GET, HEAD' });
          return;
        }
        sendJson(res, 200, { ok: true });
        return;
      }

      const hostHeader = (req.headers['host'] ?? '').toLowerCase();
      if (!allowedHosts.has(hostHeader)) {
        fields['refused'] = 'host';
        sendJson(res, 403, { error: 'host_not_allowed', error_description: 'This Host is not served here.' });
        return;
      }

      if (PROTECTED_RESOURCE_PATHS.has(path)) {
        if (req.method !== 'GET' && req.method !== 'HEAD') {
          sendJson(res, 405, { error: 'method_not_allowed' }, { allow: 'GET, HEAD' });
          return;
        }
        sendJson(res, 200, await protectedResourceDocument(), {
          'cache-control': 'public, max-age=300',
          'access-control-allow-origin': '*',
        });
        return;
      }

      if (path === MCP_PATH) {
        await handleMcp(req, res, (more) => Object.assign(fields, more));
        return;
      }

      sendJson(res, 404, { error: 'not_found' });
    } catch (error) {
      fields['error'] = redactSecrets((error as Error).message ?? 'error').slice(0, 300);
      sendJson(res, 500, { error: 'internal_error' });
    }
  }) as HttpHandler;
  handler.reset = () => {
    postures.clear();
    callerEtags.clear();
    metadata = null;
  };
  return handler;
}

/** Starts listening; resolves once the port is bound. */
export async function startHttpServer(config: HttpServiceConfig, options: { handleSignals?: boolean } = {}): Promise<HttpServer> {
  const handler = createHttpHandler(config);
  const server = createHttpServer((req, res) => {
    void handler(req, res);
  });
  server.keepAliveTimeout = 65_000;
  await new Promise<void>((resolve, reject) => {
    server.once('error', reject);
    server.listen(config.port, config.host, () => {
      server.off('error', reject);
      resolve();
    });
  });
  const log = config.log ?? ((line: string) => {
    process.stderr.write(`${line}\n`);
  });
  const address = server.address();
  log(JSON.stringify({
    ts: new Date().toISOString(),
    event: 'listening',
    address: typeof address === 'object' && address ? `${address.address}:${address.port}` : String(address),
    publicUrl: config.publicUrl,
    resource: resourceUrl(config.publicUrl),
    apiUrl: config.apiUrl,
  }));
  if (options.handleSignals) {
    // As PID 1 in a container nothing handles SIGTERM unless this does.
    const stop = () => {
      server.close(() => process.exit(0));
      server.closeAllConnections();
      setTimeout(() => process.exit(0), 5000).unref();
    };
    process.once('SIGTERM', stop);
    process.once('SIGINT', stop);
  }
  return server;
}
