import { NextRequest, NextResponse } from "next/server";
import {
  ApiProxyRequestConfigurationError,
  buildApiProxyRequestHeaders,
  configuredApiKeyHeaderName,
  deleteHopByHopHeaders,
} from "@/lib/apiProxyRequest";
import { allowSetCookieForProxyResponse, proxyResponseForUpstream } from "@/lib/apiProxyResponse";
import { withStrictTransportSecurity } from "@/lib/securityHeaders";
import { readServerSecret } from "@/lib/serverSecrets";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

const apiUrl = process.env.API_URL;
const UPSTREAM_TIMEOUT_MS = Number.parseInt(process.env.API_PROXY_TIMEOUT_MS ?? "15000", 10);
const DOCUMENT_TIMEOUT_MS = Number.parseInt(process.env.API_PROXY_DOCUMENT_TIMEOUT_MS ?? "120000", 10);
const MAX_PROXY_BODY_BYTES = Number.parseInt(process.env.API_PROXY_MAX_BODY_BYTES ?? "5242880", 10);
const TRUST_PROXY_HEADERS = process.env.TRUST_PROXY_HEADERS === "true";
const DEFAULT_DEVELOPMENT_API_URL = "http://localhost:5090";
const isDevelopmentRuntime = process.env.NODE_ENV === "development";

type RouteContext = {
  params: Promise<{ path: string[] }>;
};

class ApiProxyConfigurationError extends Error {}

class PayloadTooLargeError extends Error {
  constructor() {
    super("Request body exceeds the frontend API proxy limit.");
    this.name = "PayloadTooLargeError";
  }
}

function getApiBaseUrl() {
  const configuredApiUrl = apiUrl || (isDevelopmentRuntime ? DEFAULT_DEVELOPMENT_API_URL : "");
  if (!configuredApiUrl) {
    throw new ApiProxyConfigurationError("API_URL is required for the frontend API proxy outside development.");
  }

  let parsedApiUrl: URL;
  try {
    parsedApiUrl = new URL(configuredApiUrl);
  } catch {
    throw new ApiProxyConfigurationError("API_URL must be an absolute URL for the frontend API proxy.");
  }

  if (parsedApiUrl.protocol !== "http:" && parsedApiUrl.protocol !== "https:") {
    throw new ApiProxyConfigurationError("API_URL must use http or https for the frontend API proxy.");
  }

  return parsedApiUrl;
}

function requireApiKey() {
  const configuredApiKey = readServerSecret(process.env, "ACCOUNTS_API_KEY").trim();
  if (!isDevelopmentRuntime && !configuredApiKey) {
    throw new ApiProxyConfigurationError("ACCOUNTS_API_KEY is required for the frontend API proxy outside development.");
  }

  return configuredApiKey;
}

function upstreamTimeoutMs() {
  return Number.isFinite(UPSTREAM_TIMEOUT_MS) && UPSTREAM_TIMEOUT_MS > 0 ? UPSTREAM_TIMEOUT_MS : 15000;
}

function documentGenerationTimeoutMs() {
  return Number.isFinite(DOCUMENT_TIMEOUT_MS) && DOCUMENT_TIMEOUT_MS > 0 ? DOCUMENT_TIMEOUT_MS : 120000;
}

function isPositiveIntegerSegment(value: string) {
  return /^[1-9]\d*$/.test(value);
}

function pathMatchesCompanyPeriodPrefix(segments: string[]) {
  return segments.length >= 6
    && segments[0] === "companies"
    && isPositiveIntegerSegment(segments[1])
    && segments[2] === "periods"
    && isPositiveIntegerSegment(segments[3]);
}

function isDocumentGenerationPath(path: string[]) {
  const segments = path.map((segment) => segment.toLowerCase());
  if (!pathMatchesCompanyPeriodPrefix(segments)) return false;

  return segments[4] === "documents"
    || (segments[4] === "revenue" && segments[5] === "ixbrl");
}

function proxyTimeoutMs(path: string[]) {
  return isDocumentGenerationPath(path) ? documentGenerationTimeoutMs() : upstreamTimeoutMs();
}

function maxProxyBodyBytes() {
  return Number.isFinite(MAX_PROXY_BODY_BYTES) && MAX_PROXY_BODY_BYTES > 0 ? MAX_PROXY_BODY_BYTES : 5 * 1024 * 1024;
}

function contentLengthExceedsLimit(request: NextRequest) {
  const contentLength = request.headers.get("content-length");
  if (!contentLength) return false;

  const parsedLength = Number.parseInt(contentLength, 10);
  return Number.isFinite(parsedLength) && parsedLength > maxProxyBodyBytes();
}

function boundedRequestBody(body: ReadableStream<Uint8Array> | null) {
  if (!body) return null;

  let bytesRead = 0;
  return body.pipeThrough(
    new TransformStream<Uint8Array, Uint8Array>({
      transform(chunk, controller) {
        bytesRead += chunk.byteLength;
        if (bytesRead > maxProxyBodyBytes()) {
          throw new PayloadTooLargeError();
        }

        controller.enqueue(chunk);
      },
    }),
  );
}

function upstreamFailureResponse(error: unknown) {
  const message =
    error instanceof Error && error.name === "TimeoutError"
      ? "The Accounts API did not respond before the proxy timeout."
      : "The Accounts API is unavailable.";

  return withStrictTransportSecurity(NextResponse.json(
    {
      error: "upstream_unavailable",
      message,
    },
    { status: 502 },
  ));
}

function payloadTooLargeResponse() {
  return withStrictTransportSecurity(NextResponse.json(
    {
      error: "payload_too_large",
      message: `Request body exceeds the ${maxProxyBodyBytes()} byte frontend API proxy limit.`,
    },
    { status: 413 },
  ));
}

function proxyConfigurationMessage(error: Error) {
  return isDevelopmentRuntime
    ? error.message
    : "The frontend API proxy is unavailable.";
}

function proxyConfigurationResponse(error: Error) {
  console.error("Frontend API proxy configuration error", error);
  return withStrictTransportSecurity(NextResponse.json(
    {
      error: "api_proxy_misconfigured",
      message: proxyConfigurationMessage(error),
    },
    { status: 500 },
  ));
}

async function proxyApiRequest(request: NextRequest, context: RouteContext) {
  try {
    const { path } = await context.params;
    const target = new URL(`/api/${path.map(encodeURIComponent).join("/")}`, getApiBaseUrl());
    const configuredApiKey = requireApiKey();
    const apiKeyHeader = configuredApiKeyHeaderName(process.env.ACCOUNTS_API_KEY_HEADER);
    target.search = request.nextUrl.search;

    const headers = buildApiProxyRequestHeaders(request.headers, {
      apiKey: configuredApiKey,
      apiKeyHeader,
      trustProxyHeaders: TRUST_PROXY_HEADERS,
      requestProtocol: request.nextUrl.protocol,
    });

    const init: RequestInit = {
      method: request.method,
      headers,
      cache: "no-store",
      redirect: "manual",
      signal: AbortSignal.timeout(proxyTimeoutMs(path)),
    };

    if (request.method !== "GET" && request.method !== "HEAD") {
      if (contentLengthExceedsLimit(request)) {
        return payloadTooLargeResponse();
      }

      const body = boundedRequestBody(request.body);
      if (body) {
        Object.assign(init, {
          body,
          duplex: "half",
        } as RequestInit & { duplex: "half" });
      }
    }

    const response = await fetch(target, init);
    const responseHeaders = new Headers(response.headers);
    deleteHopByHopHeaders(responseHeaders);

    return proxyResponseForUpstream(response, responseHeaders, {
      allowSetCookie: allowSetCookieForProxyResponse(request.method, path, response.status),
    });
  } catch (error) {
    if (error instanceof ApiProxyConfigurationError || error instanceof ApiProxyRequestConfigurationError) {
      return proxyConfigurationResponse(error);
    }

    if (error instanceof PayloadTooLargeError || (error instanceof Error && error.cause instanceof PayloadTooLargeError)) {
      return payloadTooLargeResponse();
    }

    console.error("Frontend API proxy request failed", error);
    return upstreamFailureResponse(error);
  }
}

export function GET(request: NextRequest, context: RouteContext) {
  return proxyApiRequest(request, context);
}

export function POST(request: NextRequest, context: RouteContext) {
  return proxyApiRequest(request, context);
}

export function PUT(request: NextRequest, context: RouteContext) {
  return proxyApiRequest(request, context);
}

export function PATCH(request: NextRequest, context: RouteContext) {
  return proxyApiRequest(request, context);
}

export function DELETE(request: NextRequest, context: RouteContext) {
  return proxyApiRequest(request, context);
}
