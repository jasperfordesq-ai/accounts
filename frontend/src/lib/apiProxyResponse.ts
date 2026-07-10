import { withStrictTransportSecurity } from "./securityHeaders.ts";

const passThroughHeaders = [
  "cache-control",
  "content-disposition",
  "content-language",
  "content-type",
  "etag",
  "expires",
  "last-modified",
  "pragma",
];

type ProxyResponseOptions = {
  allowSetCookie?: boolean;
};

type HeadersWithSetCookie = Headers & {
  getSetCookie?: () => string[];
};

const authSetCookieEndpoints = new Set(["login", "logout", "password"]);

export function allowSetCookieForProxyResponse(method: string, path: string[], status: number) {
  if (method.toUpperCase() !== "POST") return false;
  if (status < 200 || status >= 300) return false;

  const [area, operation, ...extraSegments] = path.map((segment) => segment.toLowerCase());
  return area === "auth"
    && extraSegments.length === 0
    && authSetCookieEndpoints.has(operation ?? "");
}

function appendSetCookieHeaders(source: Headers, target: Headers) {
  const setCookieHeaders = (source as HeadersWithSetCookie).getSetCookie?.();
  if (setCookieHeaders?.length) {
    for (const cookie of setCookieHeaders) target.append("Set-Cookie", cookie);
    return;
  }

  const setCookie = source.get("Set-Cookie");
  if (setCookie) target.set("Set-Cookie", setCookie);
}

function safePassThroughHeaders(headers: Headers, options: ProxyResponseOptions = {}) {
  const safeHeaders = new Headers();
  for (const header of passThroughHeaders) {
    const value = headers.get(header);
    if (value) safeHeaders.set(header, value);
  }

  if (options.allowSetCookie) appendSetCookieHeaders(headers, safeHeaders);

  return safeHeaders;
}

export function proxyResponseForUpstream(
  response: Response,
  headers: Headers = response.headers,
  options: ProxyResponseOptions = {},
) {
  if (response.status >= 300 && response.status < 400) {
    return withStrictTransportSecurity(Response.json(
      {
        error: "upstream_unavailable",
        message: "The Accounts API returned an invalid proxy response.",
      },
      { status: 502 },
    ));
  }

  if (response.status >= 500) {
    return withStrictTransportSecurity(Response.json(
      {
        error: "upstream_unavailable",
        message: "The Accounts API is unavailable.",
      },
      { status: 502 },
    ));
  }

  const responseForUpstream = new Response(response.body, {
    status: response.status,
    statusText: response.statusText,
    headers: safePassThroughHeaders(headers, options),
  });
  return withStrictTransportSecurity(responseForUpstream);
}
