import { NextRequest } from "next/server";
import {
  ApiProxyRequestConfigurationError,
  buildApiProxyRequestHeaders,
  configuredApiKeyHeaderName,
  deleteHopByHopHeaders,
} from "@/lib/apiProxyRequest";
import {
  boundedRequestBody,
  contentLengthExceedsLimit,
  isPayloadTooLargeError,
  maxProxyBodyBytes,
  positiveIntegerFromEnv,
} from "@/lib/apiProxyBody";
import {
  ApiProxyConfigurationError,
  apiBaseUrl,
  proxyConfigurationResponse,
  requireApiKey,
  upstreamFailureResponse,
} from "@/lib/apiProxyUpstream";
import { allowSetCookieForProxyResponse, proxyResponseForUpstream } from "@/lib/apiProxyResponse";
import { withStrictTransportSecurity } from "@/lib/securityHeaders";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

const TRUST_PROXY_HEADERS = process.env.TRUST_PROXY_HEADERS === "true";

type RouteContext = {
  params: Promise<{ path: string[] }>;
};

function upstreamTimeoutMs() {
  return positiveIntegerFromEnv(process.env.API_PROXY_TIMEOUT_MS, 15000);
}

function documentGenerationTimeoutMs() {
  return positiveIntegerFromEnv(process.env.API_PROXY_DOCUMENT_TIMEOUT_MS, 120000);
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

function payloadTooLargeResponse() {
  return withStrictTransportSecurity(Response.json(
    {
      error: "payload_too_large",
      message: `Request body exceeds the ${maxProxyBodyBytes()} byte frontend API proxy limit.`,
    },
    { status: 413 },
  ));
}

async function proxyApiRequest(request: NextRequest, context: RouteContext) {
  try {
    const { path } = await context.params;
    const target = new URL(`/api/${path.map(encodeURIComponent).join("/")}`, apiBaseUrl());
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
      const limit = maxProxyBodyBytes();
      if (contentLengthExceedsLimit(request.headers, limit)) {
        return payloadTooLargeResponse();
      }

      const body = boundedRequestBody(request.body, limit);
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

    if (isPayloadTooLargeError(error)) {
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
