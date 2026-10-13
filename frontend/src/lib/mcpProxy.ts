import {
  boundedRequestBody,
  contentLengthExceedsLimit,
  isPayloadTooLargeError,
  positiveIntegerFromEnv,
} from "./apiProxyBody.ts";
import { withStrictTransportSecurity } from "./securityHeaders.ts";

/**
 * Remote MCP endpoint (`/mcp` on the frontend origin) streamed to the in-stack connector HTTP
 * service at `MCP_URL`. The connector authenticates the caller's own bearer token and adds its own
 * service API key, so this proxy forwards an explicit allowlist of MCP transport headers only:
 * never cookies, the frontend's API key, Origin/Referer or forwarding headers.
 */
type ProxyEnv = Record<string, string | undefined>;

export const MCP_FORWARDED_REQUEST_HEADERS = [
  "authorization",
  "accept",
  "content-type",
  "mcp-session-id",
  "mcp-protocol-version",
  "last-event-id",
] as const;

export const MCP_PASSED_BACK_RESPONSE_HEADERS = [
  "content-type",
  "mcp-session-id",
  "mcp-protocol-version",
  "www-authenticate",
  "cache-control",
  "retry-after",
] as const;

/** Connect timeout: how long to wait for the connector's response headers. The body is not timed. */
export const DEFAULT_MCP_CONNECT_TIMEOUT_MS = 300000;
/** Large enough for the HTTP connector's 5 MiB base64 CSV import plus the JSON-RPC envelope. */
export const DEFAULT_MCP_MAX_BODY_BYTES = 8 * 1024 * 1024;

export class McpUpstreamTimeoutError extends Error {
  constructor() {
    super("The MCP connector service did not respond before the proxy timeout.");
    this.name = "McpUpstreamTimeoutError";
  }
}

export function mcpUpstreamUrl(env: ProxyEnv = process.env): URL | null {
  const configured = env.MCP_URL?.trim();
  if (!configured) return null;
  try {
    const url = new URL(configured);
    return url.protocol === "http:" || url.protocol === "https:" ? url : null;
  } catch {
    return null;
  }
}

export function mcpConnectTimeoutMs(env: ProxyEnv = process.env) {
  return positiveIntegerFromEnv(env.API_PROXY_MCP_TIMEOUT_MS, DEFAULT_MCP_CONNECT_TIMEOUT_MS);
}

export function mcpMaxBodyBytes(env: ProxyEnv = process.env) {
  return positiveIntegerFromEnv(env.API_PROXY_MCP_MAX_BODY_BYTES, DEFAULT_MCP_MAX_BODY_BYTES);
}

export function buildMcpUpstreamHeaders(source: Headers) {
  const headers = new Headers();
  for (const name of MCP_FORWARDED_REQUEST_HEADERS) {
    const value = source.get(name);
    if (value !== null) headers.set(name, value);
  }
  return headers;
}

export function mcpResponseHeaders(upstream: Headers) {
  const headers = new Headers();
  for (const name of MCP_PASSED_BACK_RESPONSE_HEADERS) {
    const value = upstream.get(name);
    if (value !== null) headers.set(name, value);
  }
  return headers;
}

function jsonError(status: number, error: string, message: string) {
  return withStrictTransportSecurity(Response.json({ error, message }, {
    status,
    headers: { "Cache-Control": "no-store" },
  }));
}

/**
 * Starts the upstream request and resolves once response headers arrive. The connect timer is
 * cleared at that point so a long-lived SSE body is never cut off; a client disconnect still
 * aborts the upstream request through the inbound request's signal.
 */
async function fetchWithConnectTimeout(url: URL, init: RequestInit, timeoutMs: number, clientSignal?: AbortSignal) {
  const connectController = new AbortController();
  const timer = setTimeout(() => connectController.abort(new McpUpstreamTimeoutError()), timeoutMs);
  const signal = clientSignal ? AbortSignal.any([clientSignal, connectController.signal]) : connectController.signal;
  try {
    return await fetch(url, { ...init, signal });
  } catch (error) {
    if (connectController.signal.aborted) throw new McpUpstreamTimeoutError();
    throw error;
  } finally {
    clearTimeout(timer);
  }
}

export async function proxyMcpRequest(request: Request, env: ProxyEnv = process.env) {
  const target = mcpUpstreamUrl(env);
  if (!target) {
    return jsonError(404, "not_found", "The remote MCP connector is not enabled on this FilingBridge server.");
  }

  const init: RequestInit & { duplex?: "half" } = {
    method: request.method,
    headers: buildMcpUpstreamHeaders(request.headers),
    cache: "no-store",
    redirect: "manual",
  };

  if (request.method !== "GET" && request.method !== "HEAD" && request.body) {
    const limit = mcpMaxBodyBytes(env);
    if (contentLengthExceedsLimit(request.headers, limit)) {
      return jsonError(413, "payload_too_large", `Request body exceeds the ${limit} byte MCP proxy limit.`);
    }
    init.body = boundedRequestBody(request.body, limit);
    init.duplex = "half";
  }

  try {
    const upstream = await fetchWithConnectTimeout(target, init, mcpConnectTimeoutMs(env), request.signal);
    if (upstream.status >= 300 && upstream.status < 400) {
      await upstream.body?.cancel().catch(() => undefined);
      return jsonError(502, "upstream_unavailable", "The MCP connector service returned an invalid proxy response.");
    }

    // The body stream is handed back untouched (SSE or JSON) with only the allowlisted headers.
    return withStrictTransportSecurity(new Response(upstream.body, {
      status: upstream.status,
      statusText: upstream.statusText,
      headers: mcpResponseHeaders(upstream.headers),
    }));
  } catch (error) {
    if (isPayloadTooLargeError(error)) {
      return jsonError(413, "payload_too_large", `Request body exceeds the ${mcpMaxBodyBytes(env)} byte MCP proxy limit.`);
    }
    if (error instanceof McpUpstreamTimeoutError) {
      return jsonError(504, "upstream_timeout", error.message);
    }
    if (request.signal?.aborted) {
      return jsonError(499, "client_closed_request", "The client closed the request.");
    }
    console.error("MCP proxy request failed", error);
    return jsonError(502, "upstream_unavailable", "The MCP connector service is unavailable.");
  }
}
