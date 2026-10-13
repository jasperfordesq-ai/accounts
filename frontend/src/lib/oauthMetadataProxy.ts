import {
  ApiProxyRequestConfigurationError,
  buildApiProxyRequestHeaders,
  configuredApiKeyHeaderName,
} from "./apiProxyRequest.ts";
import { proxyResponseForUpstream } from "./apiProxyResponse.ts";
import { positiveIntegerFromEnv } from "./apiProxyBody.ts";
import {
  ApiProxyConfigurationError,
  apiBaseUrl,
  proxyConfigurationResponse,
  requireApiKey,
  upstreamFailureResponse,
} from "./apiProxyUpstream.ts";
import { withStrictTransportSecurity } from "./securityHeaders.ts";

/**
 * `/.well-known/oauth-*` discovery documents (RFC 8414 / RFC 9728) live on the frontend origin but
 * are produced by the API. These handlers fetch them server-side with the service API key, exactly
 * like the `/api` proxy, and pass the JSON through with a short public cache lifetime.
 */
export const OAUTH_METADATA_API_PATHS = {
  authorizationServer: "/api/oauth/metadata/authorization-server",
  protectedResource: "/api/oauth/metadata/protected-resource",
} as const;

export const OAUTH_METADATA_CACHE_CONTROL = "public, max-age=300";

type ProxyEnv = Record<string, string | undefined>;

export function oauthMetadataRequestHeaders(source: Headers, env: ProxyEnv, requestProtocol: string) {
  const headers = buildApiProxyRequestHeaders(source, {
    apiKey: requireApiKey(env),
    apiKeyHeader: configuredApiKeyHeaderName(env.ACCOUNTS_API_KEY_HEADER),
    trustProxyHeaders: env.TRUST_PROXY_HEADERS === "true",
    requestProtocol,
  });
  // Discovery is anonymous: no browser session, CSRF state or caller credentials reach the API.
  headers.delete("Cookie");
  headers.delete("Authorization");
  headers.delete("X-CSRF-Token");
  headers.set("Accept", "application/json");
  return headers;
}

export async function proxyOAuthMetadata(request: Request, apiPath: string, env: ProxyEnv = process.env) {
  try {
    const target = new URL(apiPath, apiBaseUrl(env));
    const headers = oauthMetadataRequestHeaders(request.headers, env, new URL(request.url).protocol);
    const response = await fetch(target, {
      method: "GET",
      headers,
      cache: "no-store",
      redirect: "manual",
      signal: AbortSignal.timeout(positiveIntegerFromEnv(env.API_PROXY_TIMEOUT_MS, 15000)),
    });

    const contentType = response.headers.get("content-type") ?? "";
    if (response.ok && contentType.toLowerCase().includes("json")) {
      return withStrictTransportSecurity(new Response(response.body, {
        status: response.status,
        headers: {
          "Content-Type": contentType,
          "Cache-Control": OAUTH_METADATA_CACHE_CONTROL,
        },
      }));
    }

    if (response.ok) {
      await response.body?.cancel().catch(() => undefined);
      return upstreamFailureResponse(new Error("OAuth metadata was not JSON."));
    }

    // Errors (connector disabled, 5xx, redirects) keep the /api proxy's sanitising rules and are
    // never cached publicly.
    const sanitised = proxyResponseForUpstream(response);
    sanitised.headers.set("Cache-Control", "no-store");
    return sanitised;
  } catch (error) {
    if (error instanceof ApiProxyConfigurationError || error instanceof ApiProxyRequestConfigurationError) {
      return proxyConfigurationResponse(error, env);
    }

    console.error("OAuth metadata proxy request failed", error);
    return upstreamFailureResponse(error);
  }
}
