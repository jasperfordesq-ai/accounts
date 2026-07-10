export const CLIENT_MONITORING_EVENT_CODES = [
  "api-contract-rejection",
  "api-network-failure",
  "api-server-rejection",
  "api-timeout",
  "auth-service-unavailable",
  "render-exception",
  "unhandled-client-exception",
] as const;

export type ClientMonitoringEventCode = (typeof CLIENT_MONITORING_EVENT_CODES)[number];

interface ClientMonitoringPayload {
  eventCode: ClientMonitoringEventCode;
  route: string;
  correlationId?: string;
}

const eventCodes = new Set<string>(CLIENT_MONITORING_EVENT_CODES);
const recentEvents = new Map<string, number>();
const DEDUPE_WINDOW_MS = 60_000;
const CSRF_COOKIE = "accounts_csrf";
const CSRF_HEADER = "X-CSRF-Token";
const SAFE_ROUTE_SEGMENTS = new Set([
  "about",
  "change-password",
  "charity",
  "classify",
  "companies",
  "login",
  "new",
  "notes",
  "periods",
  "production-readiness",
  "statements",
  "workbench-preview",
  "year-end",
]);

export function sanitizeClientRoute(rawRoute: string): string {
  const withoutQuery = String(rawRoute || "/").split(/[?#]/, 1)[0];
  const segments = withoutQuery
    .split("/")
    .filter(Boolean)
    .map((segment) => {
      const decoded = safeDecode(segment);
      if (decoded === "{redacted}") return decoded;
      if (/^\d+$/.test(decoded)) return "{id}";
      if (decoded === "{id}") return decoded;
      if (/^[0-9a-f]{8}-[0-9a-f-]{27,}$/i.test(decoded) || decoded.includes("@") || decoded.length > 64) {
        return "{redacted}";
      }
      const safe = decoded.replace(/[^A-Za-z0-9._~-]/g, "_");
      return SAFE_ROUTE_SEGMENTS.has(safe.toLowerCase()) ? safe.toLowerCase() : "{redacted}";
    });
  return segments.length === 0 ? "/" : `/${segments.join("/")}`;
}

export function buildClientMonitoringPayload(
  eventCode: ClientMonitoringEventCode,
  route: string,
  correlationId?: string,
): ClientMonitoringPayload {
  if (!eventCodes.has(eventCode)) throw new Error("Unsupported client monitoring event code.");
  const safeCorrelationId = correlationId?.trim();
  return {
    eventCode,
    route: sanitizeClientRoute(route),
    ...(safeCorrelationId && /^[A-Za-z0-9._-]{1,128}$/.test(safeCorrelationId)
      ? { correlationId: safeCorrelationId }
      : {}),
  };
}

export async function reportClientMonitoringEvent(
  eventCode: ClientMonitoringEventCode,
  options: { route?: string; correlationId?: string } = {},
): Promise<void> {
  if (typeof window === "undefined" || typeof document === "undefined") return;

  const csrfToken = readCsrfToken();
  if (!csrfToken) return;

  const payload = buildClientMonitoringPayload(
    eventCode,
    options.route ?? window.location.pathname,
    options.correlationId,
  );
  // Correlation IDs are intentionally excluded so a failing request loop cannot grow this
  // in-memory map without bound or bypass route/event deduplication.
  const key = `${payload.eventCode}|${payload.route}`;
  const now = Date.now();
  if (now - (recentEvents.get(key) ?? 0) < DEDUPE_WINDOW_MS) return;
  recentEvents.set(key, now);

  try {
    await fetch("/api/system/monitoring/client-event", {
      method: "POST",
      credentials: "include",
      keepalive: true,
      headers: {
        "Content-Type": "application/json",
        [CSRF_HEADER]: csrfToken,
      },
      body: JSON.stringify(payload),
    });
  } catch {
    // Monitoring must never replace or recursively amplify the user's original error path.
  }
}

function readCsrfToken(): string | undefined {
  const prefix = `${CSRF_COOKIE}=`;
  const cookie = document.cookie
    .split(";")
    .map((part) => part.trim())
    .find((part) => part.startsWith(prefix));
  return cookie ? safeDecode(cookie.slice(prefix.length)) : undefined;
}

function safeDecode(value: string) {
  try {
    return decodeURIComponent(value);
  } catch {
    return "{redacted}";
  }
}
