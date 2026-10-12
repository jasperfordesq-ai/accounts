import { createServer as createHttpServer, request as httpRequest, type Server } from 'node:http';
import type { AddressInfo } from 'node:net';
import { createHttpHandler, type HttpHandler, type HttpServiceConfig } from '../http-server.js';
import { json, stubFetch, type Recorded, type Responder } from './helpers.js';

export const SERVICE_KEY = 'svc-key-0123456789abcdef0123456789abcdef';
export const PUBLIC_URL = 'https://fb.example';
export const GOOD_TOKEN = 'fbc1.7.good-access-token-aaaaaaaaaaaa';
export const REVOKED_TOKEN = 'fbc1.7.revoked-access-token-bbbbbbbbbb';

export const POSTURE_BODY = {
  accessLevel: 'Write',
  dataScope: 'Withheld',
  role: 'Owner',
  tenantSlug: 'demo',
  userId: 4,
  writeBudgetPerMinute: 30,
  approvalMinutes: 5,
};

/** The API as the HTTP service sees it: the connector session route and a few reads. */
export const defaultApi: Responder = (request) => {
  const auth = request.headers['authorization'];
  if (request.url.endsWith('/api/oauth/metadata/protected-resource')) return json(404, {});
  if (auth === `Bearer ${REVOKED_TOKEN}`) return json(401, { code: 'CONNECTOR_SESSION_INVALID' });
  if (request.url.endsWith('/api/auth/connector/session')) return json(200, POSTURE_BODY);
  if (request.url.endsWith('/api/companies')) return json(200, [{ id: 1, legalName: 'Green Valley CLG', companyType: 'CLG' }]);
  return json(404, { error: 'not found' });
};

export interface RunningService {
  base: string;
  port: number;
  calls: Recorded[];
  logs: string[];
  handler: HttpHandler;
  close(): Promise<void>;
}

/**
 * The HTTP service on an ephemeral loopback port, its API stubbed. The
 * allowed hosts are the loopback address and port it was given, unless the
 * test names others.
 */
export async function startService(
  responder: Responder = defaultApi,
  overrides: Partial<HttpServiceConfig> = {},
): Promise<RunningService> {
  const stub = stubFetch(responder);
  const logs: string[] = [];
  let handler: HttpHandler | null = null;
  const server: Server = createHttpServer((req, res) => {
    void handler!(req, res);
  });
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  const port = (server.address() as AddressInfo).port;
  handler = createHttpHandler({
    apiUrl: 'http://api.test:8080',
    apiKey: SERVICE_KEY,
    publicUrl: PUBLIC_URL,
    allowedHosts: [`127.0.0.1:${port}`],
    host: '127.0.0.1',
    port,
    fetchImpl: stub.fetch,
    log: (line) => logs.push(line),
    ...overrides,
  });
  return {
    base: `http://127.0.0.1:${port}`,
    port,
    calls: stub.calls,
    logs,
    handler,
    close: () => new Promise<void>((resolve) => {
      server.closeAllConnections();
      server.close(() => resolve());
    }),
  };
}

export interface RawResponse {
  status: number;
  headers: Record<string, string | string[] | undefined>;
  text: string;
  json(): unknown;
}

/** A request with full control of every header, Host included. */
export function raw(
  port: number,
  method: string,
  path: string,
  headers: Record<string, string> = {},
  body?: string | Buffer,
): Promise<RawResponse> {
  return new Promise((resolve, reject) => {
    const req = httpRequest({ host: '127.0.0.1', port, method, path, headers }, (res) => {
      const chunks: Buffer[] = [];
      res.on('data', (chunk: Buffer) => chunks.push(chunk));
      res.on('end', () => {
        const text = Buffer.concat(chunks).toString('utf8');
        resolve({ status: res.statusCode ?? 0, headers: res.headers, text, json: () => JSON.parse(text) as unknown });
      });
    });
    req.on('error', reject);
    if (body !== undefined) req.write(body);
    req.end();
  });
}

export const MCP_ACCEPT = 'application/json, text/event-stream';

export function rpc(method: string, params: Record<string, unknown> = {}, id = 1): string {
  return JSON.stringify({ jsonrpc: '2.0', id, method, params });
}
