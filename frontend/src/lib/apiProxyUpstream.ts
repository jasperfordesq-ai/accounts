import { readServerSecret } from "./serverSecrets.ts";
import { withStrictTransportSecurity } from "./securityHeaders.ts";

/**
 * Upstream API configuration shared by every frontend route handler that reaches the Accounts API
 * (`app/api/[...path]` and the `/.well-known` OAuth metadata handlers), so the API base URL and the
 * service API key are resolved and validated in exactly one place.
 */
type ProxyEnv = Record<string, string | undefined>;

const DEFAULT_DEVELOPMENT_API_URL = "http://localhost:5090";

export class ApiProxyConfigurationError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "ApiProxyConfigurationError";
  }
}

function isDevelopment(env: ProxyEnv) {
  return env.NODE_ENV === "development";
}

export function apiBaseUrl(env: ProxyEnv = process.env) {
  const configuredApiUrl = env.API_URL || (isDevelopment(env) ? DEFAULT_DEVELOPMENT_API_URL : "");
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

export function requireApiKey(env: ProxyEnv = process.env) {
  const configuredApiKey = readServerSecret(env, "ACCOUNTS_API_KEY").trim();
  if (!isDevelopment(env) && !configuredApiKey) {
    throw new ApiProxyConfigurationError("ACCOUNTS_API_KEY is required for the frontend API proxy outside development.");
  }

  return configuredApiKey;
}

export function upstreamFailureResponse(error: unknown) {
  const message =
    error instanceof Error && error.name === "TimeoutError"
      ? "The Accounts API did not respond before the proxy timeout."
      : "The Accounts API is unavailable.";

  return withStrictTransportSecurity(Response.json(
    {
      error: "upstream_unavailable",
      message,
    },
    { status: 502 },
  ));
}

export function proxyConfigurationResponse(error: Error, env: ProxyEnv = process.env) {
  console.error("Frontend API proxy configuration error", error);
  return withStrictTransportSecurity(Response.json(
    {
      error: "api_proxy_misconfigured",
      message: isDevelopment(env) ? error.message : "The frontend API proxy is unavailable.",
    },
    { status: 500 },
  ));
}
