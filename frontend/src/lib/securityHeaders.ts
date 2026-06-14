type SecurityHeaderEnv = Record<string, string | undefined>;

export const strictTransportSecurity = "max-age=31536000; includeSubDomains; preload";

export function enableStrictTransportSecurity(env?: SecurityHeaderEnv) {
  if (!env) return process.env.ENABLE_HSTS === "true";
  return env.ENABLE_HSTS === "true";
}

export function withStrictTransportSecurity<T extends Response>(response: T, env?: SecurityHeaderEnv) {
  if (enableStrictTransportSecurity(env)) {
    response.headers.set("Strict-Transport-Security", strictTransportSecurity);
  }

  return response;
}
