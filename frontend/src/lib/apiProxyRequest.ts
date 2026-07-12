const defaultApiKeyHeader = "X-Accounts-Api-Key";
const httpHeaderNamePattern = /^[!#$%&'*+\-.^_`|~0-9A-Za-z]+$/;

export const hopByHopHeaders = [
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

type BuildApiProxyRequestHeadersOptions = {
  apiKey: string;
  apiKeyHeader?: string;
  trustProxyHeaders: boolean;
  requestProtocol: string;
};

export class ApiProxyRequestConfigurationError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "ApiProxyRequestConfigurationError";
  }
}

function isHttpHeaderName(value: string) {
  return httpHeaderNamePattern.test(value);
}

function isForbiddenApiKeyHeaderName(header: string) {
  const normalized = header.toLowerCase();
  return hopByHopHeaders.includes(normalized)
    || normalized === "forwarded"
    || normalized === "x-real-ip"
    || normalized.startsWith("x-forwarded-")
    || normalized.startsWith("tailscale-")
    || normalized.startsWith("x-tailscale-");
}

export function configuredApiKeyHeaderName(configuredHeader: string | undefined) {
  const header = configuredHeader?.trim();
  if (!header) return defaultApiKeyHeader;
  if (!isHttpHeaderName(header)) {
    throw new ApiProxyRequestConfigurationError("ACCOUNTS_API_KEY_HEADER must be a valid HTTP header name.");
  }
  if (isForbiddenApiKeyHeaderName(header)) {
    throw new ApiProxyRequestConfigurationError("ACCOUNTS_API_KEY_HEADER must be an end-to-end HTTP header name.");
  }

  return header;
}

export function deleteHopByHopHeaders(headers: Headers) {
  const connectionHeaders = headers.get("Connection") ?? "";
  const connectionHeaderNames = connectionHeaders
    .split(",")
    .map((header) => header.trim())
    .filter((header) => header && isHttpHeaderName(header));

  for (const header of connectionHeaderNames) headers.delete(header);
  for (const header of hopByHopHeaders) headers.delete(header);
}

function deleteForwardedHeaders(headers: Headers) {
  const forwardedHeaders = [...headers.keys()].filter((header) =>
    header.toLowerCase().startsWith("x-forwarded-"));
  for (const header of forwardedHeaders) headers.delete(header);

  headers.delete("Forwarded");
  headers.delete("X-Real-IP");
}

export function deleteUntrustedTailscaleIdentityHeaders(headers: Headers) {
  const identityHeaders = [...headers.keys()].filter((header) => {
    const normalized = header.toLowerCase();
    return normalized.startsWith("tailscale-") || normalized.startsWith("x-tailscale-");
  });

  for (const header of identityHeaders) headers.delete(header);
}

export function buildApiProxyRequestHeaders(
  sourceHeaders: Headers,
  options: BuildApiProxyRequestHeadersOptions,
) {
  const headers = new Headers(sourceHeaders);
  deleteHopByHopHeaders(headers);
  deleteUntrustedTailscaleIdentityHeaders(headers);

  const apiKeyHeader = configuredApiKeyHeaderName(options.apiKeyHeader);
  headers.delete(defaultApiKeyHeader);
  headers.delete(apiKeyHeader);
  const apiKey = options.apiKey.trim();
  if (apiKey) headers.set(apiKeyHeader, apiKey);

  deleteForwardedHeaders(headers);
  if (options.trustProxyHeaders) {
    const forwardedFor = sourceHeaders.get("x-forwarded-for") ?? sourceHeaders.get("x-real-ip");
    if (forwardedFor) headers.set("X-Forwarded-For", forwardedFor);

    const forwardedHost = sourceHeaders.get("x-forwarded-host") ?? sourceHeaders.get("host");
    if (forwardedHost) headers.set("X-Forwarded-Host", forwardedHost);

    const forwardedProto = sourceHeaders.get("x-forwarded-proto") ?? options.requestProtocol.replace(":", "");
    headers.set("X-Forwarded-Proto", forwardedProto);
  }

  return headers;
}
