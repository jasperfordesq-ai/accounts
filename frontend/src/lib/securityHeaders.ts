type SecurityHeaderEnv = Record<string, string | undefined>;

export const strictTransportSecurity = "max-age=31536000; includeSubDomains; preload";

export function enableStrictTransportSecurity(env?: SecurityHeaderEnv) {
  if (!env) return process.env.ENABLE_HSTS === "true";
  return env.ENABLE_HSTS === "true";
}

function exactPrivateLocalhostOrigin(env: SecurityHeaderEnv) {
  if (env.DEPLOYMENT_MODE !== "PrivateServer") return null;

  const configuredOrigin = env.PRIVATE_LOCAL_ORIGIN?.trim();
  if (!configuredOrigin) return null;

  try {
    const url = new URL(configuredOrigin);
    if (
      url.protocol !== "http:"
      || url.hostname !== "localhost"
      || url.username !== ""
      || url.password !== ""
      || url.pathname !== "/"
      || url.search !== ""
      || url.hash !== ""
    ) {
      return null;
    }

    return url.origin;
  } catch {
    return null;
  }
}

export function shouldUpgradeInsecureRequests(
  requestUrl: string,
  env: SecurityHeaderEnv = process.env,
  requestHost?: string | null,
) {
  if (env.NODE_ENV === "development") return false;

  const localOrigin = exactPrivateLocalhostOrigin(env);
  if (!localOrigin) return true;

  try {
    const local = new URL(localOrigin);
    const requestOrigin = requestHost?.trim()
      ? new URL(`${local.protocol}//${requestHost.trim()}`).origin
      : new URL(requestUrl).origin;
    return requestOrigin !== localOrigin;
  } catch {
    return true;
  }
}

export function withStrictTransportSecurity<T extends Response>(response: T, env?: SecurityHeaderEnv) {
  if (enableStrictTransportSecurity(env)) {
    response.headers.set("Strict-Transport-Security", strictTransportSecurity);
  }

  return response;
}
