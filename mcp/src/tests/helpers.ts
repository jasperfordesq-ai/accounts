import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { ApiClient } from '../client.js';
import type { TokenSource } from '../token-source.js';

const here = dirname(fileURLToPath(import.meta.url));

/** mcp/ */
export const PACKAGE_ROOT = resolve(here, '../..');
/** The repository root. */
export const REPO_ROOT = resolve(here, '../../..');
export const API_ROOT = resolve(REPO_ROOT, 'backend/Accounts.Api');
export const CONTRACT_PATH = resolve(API_ROOT, 'OpenApi/accounts-api-v1.json');

export interface Contract {
  paths: Record<string, Record<string, { parameters?: { name: string; in: string; schema?: Record<string, unknown> }[]; requestBody?: unknown }>>;
  components: { schemas: Record<string, { type?: unknown; enum?: unknown[]; properties?: Record<string, Record<string, unknown>>; required?: string[] }> };
}

let cached: Contract | null = null;
export function contract(): Contract {
  cached ??= JSON.parse(readFileSync(CONTRACT_PATH, 'utf8')) as Contract;
  return cached;
}

export const HTTP_METHODS = ['get', 'post', 'put', 'delete', 'patch'] as const;

/** Every operation in the contract as `METHOD /path`. */
export function contractOperations(): string[] {
  const out: string[] = [];
  for (const [path, item] of Object.entries(contract().paths)) {
    for (const method of HTTP_METHODS) {
      if (item[method]) out.push(`${method.toUpperCase()} ${path}`);
    }
  }
  return out.sort();
}

export interface Recorded {
  url: string;
  method: string;
  headers: Record<string, string>;
  body: unknown;
}

export type Responder = (request: Recorded, index: number) => Response | Promise<Response>;

/** A fetch stub that records every request and answers from `responder`. */
export function stubFetch(responder: Responder): { fetch: typeof fetch; calls: Recorded[] } {
  const calls: Recorded[] = [];
  const impl = (async (input: string | URL | Request, init?: RequestInit) => {
    const headers: Record<string, string> = {};
    const raw = init?.headers ?? {};
    if (raw instanceof Headers) raw.forEach((value, key) => { headers[key.toLowerCase()] = value; });
    else for (const [key, value] of Object.entries(raw as Record<string, string>)) headers[key.toLowerCase()] = value;
    let body: unknown = init?.body;
    if (typeof body === 'string') {
      try { body = JSON.parse(body); } catch { /* keep text */ }
    }
    const recorded: Recorded = { url: String(input), method: init?.method ?? 'GET', headers, body };
    calls.push(recorded);
    return responder(recorded, calls.length - 1);
  }) as typeof fetch;
  return { fetch: impl, calls };
}

export function json(status: number, body: unknown, headers: Record<string, string> = {}): Response {
  return new Response(status === 204 ? null : JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json', ...headers },
  });
}

export class FakeTokens implements TokenSource {
  readonly canRenew: boolean;
  tokens: string[];
  invalidations = 0;
  #index = 0;

  constructor(tokens: string[] = ['fbc1.token-one-aaaaaaaa'], canRenew = true) {
    this.tokens = tokens;
    this.canRenew = canRenew;
  }

  async accessToken(): Promise<string> {
    return this.tokens[Math.min(this.#index, this.tokens.length - 1)]!;
  }

  invalidate(): void {
    this.invalidations += 1;
    this.#index += 1;
  }
}

export function clientWith(responder: Responder, tokens: TokenSource = new FakeTokens()) {
  const stub = stubFetch(responder);
  const client = new ApiClient({ tokens, baseUrl: 'http://127.0.0.1:3500', fetchImpl: stub.fetch });
  return { client, calls: stub.calls };
}
