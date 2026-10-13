import { z } from "zod";
import { ApiError, dispatchSessionExpired } from "./api.ts";
import * as apiContracts from "./apiContracts.ts";

/**
 * Browser client for the MCP connector's web surfaces: pending agent approvals, connected agents,
 * agent activity and the public OAuth consent page. Kept out of `api.ts` (module-size budget); it
 * uses the same proxy, cookie, CSRF and response-contract conventions.
 */

const CSRF_COOKIE = "accounts_csrf";
const CSRF_HEADER = "X-CSRF-Token";
const INFRASTRUCTURE_MARKERS = [
  "api_proxy_misconfigured",
  "upstream_unavailable",
  "stack trace",
  "exception",
  "system.",
  "npgsql",
  "sqlexception",
  "econnrefused",
  "etimedout",
];

export type ConnectorAccessLevel = z.infer<typeof apiContracts.connectorAccessLevelSchema>;
export type ConnectorDataScope = z.infer<typeof apiContracts.connectorDataScopeSchema>;
export type ConnectorApproval = z.infer<typeof apiContracts.connectorApprovalViewSchema>;
export type ConnectorApprovalGrant = z.infer<typeof apiContracts.connectorApprovalGrantSchema>;
export type ConnectorSession = z.infer<typeof apiContracts.connectorSessionSummarySchema>;
export type ConnectorActivityRow = z.infer<typeof apiContracts.connectorActivityRowSchema>;
export type ConnectorActivityPage = z.infer<typeof apiContracts.connectorActivityPageSchema>;
export type OAuthAuthorizeDescription = z.infer<typeof apiContracts.oauthAuthorizeDescriptionSchema>;

/** Wire body of `POST /api/oauth/authorize`. */
export interface OAuthAuthorizeRequest {
  clientId: string;
  redirectUri: string;
  state: string | null;
  codeChallenge: string;
  codeChallengeMethod: string;
  resource: string | null;
  tenantSlug: string;
  email: string;
  password: string;
  totpCode: string | null;
  recoveryCode: string | null;
  accessLevel: ConnectorAccessLevel;
  dataScope: ConnectorDataScope;
}

/** Query parameters an OAuth client sends to `/oauth/authorize`, forwarded verbatim to `describe`. */
export const OAUTH_AUTHORIZE_QUERY_PARAMETERS = [
  "client_id",
  "redirect_uri",
  "response_type",
  "code_challenge",
  "code_challenge_method",
  "scope",
  "resource",
  "state",
] as const;

function csrfToken(): string | undefined {
  if (typeof document === "undefined") return undefined;
  const prefix = `${CSRF_COOKIE}=`;
  const item = document.cookie.split(";").map((part) => part.trim()).find((part) => part.startsWith(prefix));
  return item ? decodeURIComponent(item.slice(prefix.length)) : undefined;
}

function jsonHeaders(withCsrf: boolean): HeadersInit {
  const token = withCsrf ? csrfToken() : undefined;
  return {
    "Content-Type": "application/json",
    Accept: "application/json",
    ...(token ? { [CSRF_HEADER]: token } : {}),
  };
}

/**
 * The server's own refusal message and machine code (`{ error, code }`, or OAuth's
 * `{ error, error_description }`), for flows whose 401/403 answers are expected outcomes (wrong
 * password, refused approval) rather than an expired browser session.
 */
export function apiErrorDetail(error: unknown): { message: string | null; code: string | null } {
  if (!(error instanceof ApiError) || !error.body) return { message: null, code: null };
  try {
    const parsed: unknown = JSON.parse(error.body);
    if (!parsed || typeof parsed !== "object") return { message: null, code: null };
    const record = parsed as Record<string, unknown>;
    const text = (value: unknown) => (typeof value === "string" && value.trim() ? value.trim() : null);
    const message = text(record.error_description) ?? text(record.message) ?? text(record.error);
    const safe = message && error.status < 500
      && !INFRASTRUCTURE_MARKERS.some((marker) => message.toLowerCase().includes(marker))
      ? message
      : null;
    return { message: safe, code: text(record.code) };
  } catch {
    return { message: null, code: null };
  }
}

async function failure(response: Response) {
  const body = await response.text().catch(() => "");
  return new ApiError(response.status, response.statusText, body);
}

async function parseJson<T>(response: Response, schema: z.ZodType<T>, contract: string): Promise<T> {
  const payload: unknown = await response.json();
  return apiContracts.parseApiContract(schema, payload, contract);
}

/** Signed-in request: a 401 means the browser session is gone, exactly as in `apiFetch`. */
async function sessionRequest(path: string, init: RequestInit = {}): Promise<Response> {
  const unsafe = (init.method ?? "GET").toUpperCase() !== "GET";
  const response = await fetch(path, {
    ...init,
    credentials: "include",
    headers: { ...jsonHeaders(unsafe), ...init.headers },
  });
  if (!response.ok) {
    if (response.status === 401) dispatchSessionExpired();
    throw await failure(response);
  }
  return response;
}

function userQuery(userId?: number | null, extra: Record<string, number> = {}) {
  const params = new URLSearchParams();
  if (userId) params.set("userId", String(userId));
  for (const [key, value] of Object.entries(extra)) params.set(key, String(value));
  const query = params.toString();
  return query ? `?${query}` : "";
}

export async function getConnectorApprovals(): Promise<ConnectorApproval[]> {
  const response = await sessionRequest("/api/auth/connector/approvals");
  return parseJson(response, z.array(apiContracts.connectorApprovalViewSchema), "connector approvals");
}

/**
 * Grants one agent action with the signed-in person's password and authenticator code. A refused
 * grant answers 401 `APPROVAL_REFUSED`; that is not a lost browser session, so it must not sign the
 * person out. Any other 401 still does.
 */
export async function approveConnectorAction(approvalId: string, password: string, totpCode: string): Promise<ConnectorApprovalGrant> {
  const response = await fetch("/api/auth/connector/approve", {
    method: "POST",
    credentials: "include",
    headers: jsonHeaders(true),
    body: JSON.stringify({ approvalId, password, totpCode }),
  });
  if (!response.ok) {
    const error = await failure(response);
    if (response.status === 401 && apiErrorDetail(error).code !== "APPROVAL_REFUSED") dispatchSessionExpired();
    throw error;
  }
  return parseJson(response, apiContracts.connectorApprovalGrantSchema, "connector approval grant");
}

export async function getConnectorSessions(userId?: number | null): Promise<ConnectorSession[]> {
  const response = await sessionRequest("/api/auth/connector/sessions" + userQuery(userId));
  return parseJson(response, z.array(apiContracts.connectorSessionSummarySchema), "connected agents");
}

export async function revokeConnectorSession(familyId: string, userId?: number | null): Promise<void> {
  await sessionRequest(`/api/auth/connector/sessions/${encodeURIComponent(familyId)}/revoke` + userQuery(userId), {
    method: "POST",
  });
}

export async function getConnectorActivity(
  options: { userId?: number | null; page?: number; pageSize?: number } = {},
): Promise<ConnectorActivityPage> {
  const response = await sessionRequest("/api/auth/connector/activity" + userQuery(options.userId, {
    page: options.page ?? 1,
    pageSize: options.pageSize ?? 25,
  }));
  return parseJson(response, apiContracts.connectorActivityPageSchema, "connector activity");
}

/** Validates an OAuth client's authorization request. Public: no browser session is involved. */
export async function describeOAuthAuthorization(query: URLSearchParams): Promise<OAuthAuthorizeDescription> {
  const forwarded = new URLSearchParams();
  for (const name of OAUTH_AUTHORIZE_QUERY_PARAMETERS) {
    const value = query.get(name);
    if (value !== null) forwarded.set(name, value);
  }
  const response = await fetch("/api/oauth/authorize/describe?" + forwarded.toString(), {
    method: "GET",
    credentials: "same-origin",
    headers: { Accept: "application/json" },
  });
  if (!response.ok) throw await failure(response);
  return parseJson(response, apiContracts.oauthAuthorizeDescriptionSchema, "OAuth authorization request");
}

/**
 * Verifies the person's credentials for one OAuth grant and returns the client redirect carrying
 * the authorization code. No browser session cookie is created (the proxy also never passes one
 * back for this route).
 */
export async function submitOAuthAuthorization(input: OAuthAuthorizeRequest): Promise<string> {
  const response = await fetch("/api/oauth/authorize", {
    method: "POST",
    credentials: "same-origin",
    headers: jsonHeaders(true),
    body: JSON.stringify(input),
  });
  if (!response.ok) throw await failure(response);
  const result = await parseJson(response, apiContracts.oauthAuthorizeResponseSchema, "OAuth authorization grant");
  return result.redirectTo;
}
