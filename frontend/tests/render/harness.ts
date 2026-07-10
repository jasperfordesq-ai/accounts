// Shared helpers for the money-entry render tests.
//
// Every test installs a fetch mock and (for mutating requests) a CSRF cookie, then asserts that a
// rendered form drives the real `@/lib/api` client — proving the exact request the proxy receives:
// path, method, JSON payload and the `X-CSRF-Token` double-submit header that the real client attaches.
import { vi } from "vitest";

export interface RecordedRequest {
  url: string;
  method: string;
  headers: Headers;
  body: unknown;
  csrf: string | null;
}

export interface RouteResponse {
  status?: number;
  body?: unknown;
  headers?: HeadersInit;
}

type RouteHandler = (request: RecordedRequest) => RouteResponse | undefined;

export interface FetchMock {
  /** Every request the client issued, in order. */
  readonly requests: RecordedRequest[];
  /** Requests filtered to a single HTTP method (case-insensitive). */
  byMethod(method: string): RecordedRequest[];
  /** The single request matching method + url substring (throws if not exactly one). */
  one(method: string, urlIncludes: string): RecordedRequest;
}

const CSRF_COOKIE = "accounts_csrf";

/** Set (or clear) the CSRF double-submit cookie the real client reads from `document.cookie`. */
export function setCsrfCookie(token: string): void {
  document.cookie = `${CSRF_COOKIE}=${encodeURIComponent(token)}`;
}

export function clearCookies(): void {
  for (const part of document.cookie.split(";")) {
    const name = part.split("=")[0]?.trim();
    if (name) document.cookie = `${name}=; expires=Thu, 01 Jan 1970 00:00:00 GMT`;
  }
}

/**
 * Install a `global.fetch` mock that records every request and answers via `handler`. A handler that
 * returns `undefined` falls through to an empty `200 []` (the common "list is empty on mount" case).
 */
export function installFetchMock(handler: RouteHandler = () => undefined): FetchMock {
  const requests: RecordedRequest[] = [];

  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = typeof input === "string" ? input : input.toString();
    const method = (init?.method ?? "GET").toUpperCase();
    const headers = new Headers(init?.headers);
    let body: unknown = undefined;
    if (typeof init?.body === "string") {
      try {
        body = JSON.parse(init.body);
      } catch {
        body = init.body;
      }
    }

    const recorded: RecordedRequest = {
      url,
      method,
      headers,
      body,
      csrf: headers.get("X-CSRF-Token"),
    };
    requests.push(recorded);

    const result = handler(recorded) ?? { status: 200, body: [] };
    const status = result.status ?? 200;
    return {
      ok: status >= 200 && status < 300,
      status,
      statusText: String(status),
      headers: new Headers(result.headers),
      json: async () => result.body ?? {},
      text: async () => (result.body == null ? "" : JSON.stringify(result.body)),
    } as Response;
  });

  global.fetch = fetchMock as unknown as typeof fetch;

  return {
    requests,
    byMethod: (method: string) =>
      requests.filter((r) => r.method === method.toUpperCase()),
    one: (method: string, urlIncludes: string) => {
      const matches = requests.filter(
        (r) => r.method === method.toUpperCase() && r.url.includes(urlIncludes),
      );
      if (matches.length !== 1) {
        throw new Error(
          `Expected exactly one ${method} request including "${urlIncludes}", found ${matches.length}: ` +
            requests.map((r) => `${r.method} ${r.url}`).join(", "),
        );
      }
      return matches[0];
    },
  };
}
