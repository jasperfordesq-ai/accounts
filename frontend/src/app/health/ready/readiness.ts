import { ApiProxyRequestConfigurationError, configuredApiKeyHeaderName } from "../../../lib/apiProxyRequest.ts";
import { readServerSecret } from "../../../lib/serverSecrets.ts";

const Status503 = 503;
const DefaultReadyTimeoutMs = 5000;
const ApiReadinessFailureMessage = "The Accounts API readiness check failed.";

type ReadinessEnv = Record<string, string | undefined>;

type ReadinessOptions = {
  env?: ReadinessEnv;
  fetcher?: typeof fetch;
  now?: () => string;
};

type JsonRecord = Record<string, unknown>;

class ReadinessConfigurationError extends Error {}

function configuredEnv(options?: ReadinessOptions): ReadinessEnv {
  return options?.env ?? process.env;
}

function readinessTimeoutMs(env: ReadinessEnv) {
  const configured = Number.parseInt(env.FRONTEND_READY_TIMEOUT_MS ?? "5000", 10);
  return Number.isFinite(configured) && configured > 0 ? configured : DefaultReadyTimeoutMs;
}

function apiBaseUrl(env: ReadinessEnv) {
  const configuredApiUrl = env.API_URL ?? (env === process.env ? process.env.API_URL : undefined);
  if (!configuredApiUrl) {
    throw new ReadinessConfigurationError("API_URL is required for frontend readiness checks.");
  }

  let apiUrl: URL;
  try {
    apiUrl = new URL(configuredApiUrl);
  } catch {
    throw new ReadinessConfigurationError("API_URL must be an absolute URL for frontend readiness checks.");
  }

  if (apiUrl.protocol !== "http:" && apiUrl.protocol !== "https:") {
    throw new ReadinessConfigurationError("API_URL must use http or https for frontend readiness checks.");
  }

  return apiUrl;
}

function apiReadyUrl(baseUrl: URL) {
  return new URL("/health/ready", baseUrl);
}

function apiProxyAuthUrl(baseUrl: URL) {
  return new URL("/api/companies", baseUrl);
}

function configuredApiKey(env: ReadinessEnv) {
  return readServerSecret(env, "ACCOUNTS_API_KEY").trim();
}

function isDevelopmentRuntime(env: ReadinessEnv) {
  const nodeEnv = env.NODE_ENV ?? (env === process.env ? process.env.NODE_ENV : undefined);
  return nodeEnv === "development";
}

function requireApiProxyKey(env: ReadinessEnv) {
  if (!isDevelopmentRuntime(env) && !configuredApiKey(env)) {
    throw new ReadinessConfigurationError("Frontend API proxy is missing its service API key.");
  }
}

function responseError(body: unknown) {
  return body && typeof body === "object" && "error" in body
    ? String((body as { error?: unknown }).error ?? "")
    : "";
}

function acceptsProxyAuthentication(response: Response, body: unknown) {
  if (response.ok) return true;

  return response.status === 401 && responseError(body) === "Authentication required.";
}

async function readJson(response: Response) {
  return response.json().catch(() => null);
}

function timestamp(options?: ReadinessOptions) {
  return options?.now?.() ?? new Date().toISOString();
}

export async function getFrontendReadiness(options?: ReadinessOptions): Promise<{ status: number; body: JsonRecord }> {
  const env = configuredEnv(options);
  const fetcher = options?.fetcher ?? fetch;

  function apiProxyAuthHeaders() {
    const headers = new Headers();
    const apiKey = configuredApiKey(env);
    const apiKeyHeader = configuredApiKeyHeaderName(
      env.ACCOUNTS_API_KEY_HEADER || (env === process.env ? process.env.ACCOUNTS_API_KEY_HEADER : undefined),
    );
    if (apiKey) headers.set(apiKeyHeader, apiKey);
    return headers;
  }

  try {
    const baseUrl = apiBaseUrl(env);
    requireApiProxyKey(env);

    const proxyAuthResponse = await fetcher(apiProxyAuthUrl(baseUrl), {
      cache: "no-store",
      headers: apiProxyAuthHeaders(),
      signal: AbortSignal.timeout(readinessTimeoutMs(env)),
    });
    const proxyAuth = await readJson(proxyAuthResponse);

    if (!acceptsProxyAuthentication(proxyAuthResponse, proxyAuth)) {
      return {
        status: Status503,
        body: {
          status: "unready",
          error: "api_proxy_misconfigured",
          message: "Frontend API proxy credentials were rejected by the Accounts API.",
          checks: {
            api: "unconfigured",
            proxyAuth: "rejected",
            upstreamStatus: proxyAuthResponse.status,
          },
          timestamp: timestamp(options),
        },
      };
    }

    const response = await fetcher(apiReadyUrl(baseUrl), {
      cache: "no-store",
      signal: AbortSignal.timeout(readinessTimeoutMs(env)),
    });

    if (!response.ok) {
      return {
        status: Status503,
        body: {
          status: "unready",
          error: "api_unavailable",
          message: ApiReadinessFailureMessage,
          checks: {
            api: "unready",
            proxyAuth: "accepted",
            upstreamStatus: response.status,
          },
          timestamp: timestamp(options),
        },
      };
    }

    return {
      status: 200,
      body: {
        status: "ready",
        checks: {
          api: "ready",
          proxyAuth: "accepted",
        },
        timestamp: timestamp(options),
      },
    };
  } catch (error) {
    if (error instanceof ReadinessConfigurationError || error instanceof ApiProxyRequestConfigurationError) {
      return {
        status: Status503,
        body: {
          status: "unready",
          error: "api_proxy_misconfigured",
          message: error.message,
          checks: {
            api: "unconfigured",
          },
          timestamp: timestamp(options),
        },
      };
    }

    return {
      status: Status503,
      body: {
        status: "unready",
        error: "api_unavailable",
        message: ApiReadinessFailureMessage,
        checks: {
          api: "unavailable",
        },
        timestamp: timestamp(options),
      },
    };
  }
}
