import { NextRequest, NextResponse } from "next/server";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

const apiUrl = process.env.API_URL || process.env.NEXT_PUBLIC_API_URL || "http://localhost:5090";
const apiKey = process.env.ACCOUNTS_API_KEY || "";
const apiKeyHeader = process.env.ACCOUNTS_API_KEY_HEADER || "X-Accounts-Api-Key";

const hopByHopHeaders = [
  "connection",
  "content-length",
  "expect",
  "host",
  "keep-alive",
  "proxy-authenticate",
  "proxy-authorization",
  "te",
  "trailer",
  "transfer-encoding",
  "upgrade",
];

type RouteContext = {
  params: Promise<{ path: string[] }>;
};

async function proxyApiRequest(request: NextRequest, context: RouteContext) {
  const { path } = await context.params;
  const target = new URL(`/api/${path.map(encodeURIComponent).join("/")}`, apiUrl);
  target.search = request.nextUrl.search;

  const headers = new Headers(request.headers);
  for (const header of hopByHopHeaders) headers.delete(header);

  headers.delete(apiKeyHeader);
  headers.delete("X-Accounts-Api-Key");
  if (apiKey) headers.set(apiKeyHeader, apiKey);

  const init: RequestInit = {
    method: request.method,
    headers,
    cache: "no-store",
    redirect: "manual",
  };

  if (request.method !== "GET" && request.method !== "HEAD") {
    const body = await request.arrayBuffer();
    if (body.byteLength > 0) init.body = body;
  }

  const response = await fetch(target, init);
  const responseHeaders = new Headers(response.headers);
  for (const header of hopByHopHeaders) responseHeaders.delete(header);

  return new NextResponse(response.body, {
    status: response.status,
    statusText: response.statusText,
    headers: responseHeaders,
  });
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
