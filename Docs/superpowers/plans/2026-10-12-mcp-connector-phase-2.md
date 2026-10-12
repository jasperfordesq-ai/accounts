# MCP Connector Phase 2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Make the connector a remote MCP server reachable like big-vendor connectors: OAuth 2.1 (DCR + PKCE) so Claude.ai, Claude Code and Codex can add `https://<origin>/mcp`, an in-stack Streamable HTTP service, and web pages for approving agent actions and managing connected agents.

**Spec:** `Docs/superpowers/specs/2026-10-12-mcp-connector-design.md` (Phase 2 section). Builds on phase 1 (PR #87).

**Workstreams (parallel, contracts below are binding):**
- **A Backend** (`backend/`): OAuth authorization server, metadata, stateless client registration, authorization codes, registered-office gate, re-enabled `approvalsUrl`.
- **B Connector HTTP service** (`mcp/`, `Dockerfile.mcp`, compose files, compose verifiers, Private Server module, CI).
- **C Frontend** (`frontend/`): `/oauth/authorize` page, `/settings/approvals`, `/settings/agents`, `/mcp` and `/.well-known/*` proxy routes.

## Global Constraints

- Issuer = `Connector:PublicBaseUrl` with no trailing slash (e.g. `https://fb.example`). Every OAuth URL is on the frontend origin; the API is never published.
- MCP resource URL = `{issuer}/mcp`.
- Scopes: `fb:read`, `fb:write`, `fb:admin` (access level, highest wins; default `fb:write`), `fb:data-full` (Full scope, Owner only). Absent scope = `fb:write`, Withheld.
- PKCE `S256` only. Public clients only (`token_endpoint_auth_method: "none"`). Authorization codes live 60 s, single use.
- Tokens issued through OAuth are ordinary connector sessions (same `fbc1`/`fbr1` tokens, same posture, approval, gate, revocation rules as phase 1).
- Redirect URIs: exact match. Allowed: `http://127.0.0.1:<port>/…`, `http://localhost:<port>/…`, `http://[::1]:<port>/…`, and `https://` URIs whose host is in `Connector:OAuthRedirectHosts` (default `["claude.ai", "claude.com"]`). No fragments, no userinfo.
- No new required secret: client ids are HMAC-signed with the existing `Connector:SigningKey` (domain-separated).
- Every frontend route handler reuses the existing proxy helpers; nothing bypasses the API key injection.

## Binding contracts

### API routes (all under `/api/oauth`, reached through the Next proxy)

| Method & path | Body / query | Response |
|---|---|---|
| `GET /api/oauth/metadata/authorization-server` | — | RFC 8414 JSON: `issuer`, `authorization_endpoint` = `{issuer}/oauth/authorize`, `token_endpoint` = `{issuer}/api/oauth/token`, `registration_endpoint` = `{issuer}/api/oauth/register`, `revocation_endpoint` = `{issuer}/api/oauth/revoke`, `response_types_supported` `["code"]`, `grant_types_supported` `["authorization_code","refresh_token"]`, `code_challenge_methods_supported` `["S256"]`, `token_endpoint_auth_methods_supported` `["none"]`, `scopes_supported` |
| `GET /api/oauth/metadata/protected-resource` | — | RFC 9728 JSON: `resource` = `{issuer}/mcp`, `authorization_servers` `[issuer]`, `scopes_supported`, `bearer_methods_supported` `["header"]`, `resource_name` `"FilingBridge"` |
| `POST /api/oauth/register` | JSON `{redirect_uris[], client_name?, grant_types?, response_types?, token_endpoint_auth_method?}` | 201 `{client_id, client_id_issued_at, client_name, redirect_uris, grant_types, response_types, token_endpoint_auth_method:"none"}`; 400 `{error:"invalid_redirect_uri"\|"invalid_client_metadata", error_description}` |
| `GET /api/oauth/authorize/describe` | query `client_id, redirect_uri, response_type, code_challenge, code_challenge_method, scope?, resource?, state?` | 200 `{clientName, redirectUri, redirectHost, requestedAccessLevel, requestedDataScope, resource}`; 400 `{error, error_description}` (page must NOT redirect on these) |
| `POST /api/oauth/authorize` | JSON `{clientId, redirectUri, state?, codeChallenge, codeChallengeMethod, resource?, tenantSlug, email, password, totpCode?, recoveryCode?, accessLevel, dataScope}` | 200 `{redirectTo}` where `redirectTo` = `redirectUri?code=…&state=…&iss=…`; 400/401/403 `{error, code}` with phase-1 login codes (`INVALID_CREDENTIALS`, `SECOND_FACTOR_REQUIRED`, `SECOND_FACTOR_REJECTED`, `MFA_ENROLMENT_REQUIRED`, `PASSWORD_CHANGE_REQUIRED`, `DATA_SCOPE_FORBIDDEN`, `INVALID_ARGUMENTS`) or `INVALID_CLIENT`. Refused (403 `CROSS_SITE_REJECTED`) when an `Origin` header is present and differs from the issuer origin. |
| `POST /api/oauth/token` | `application/x-www-form-urlencoded`: `grant_type=authorization_code` + `code, redirect_uri, client_id, code_verifier, resource?`; or `grant_type=refresh_token` + `refresh_token, client_id` | 200 `{access_token, token_type:"Bearer", expires_in, refresh_token, scope}` with `Cache-Control: no-store`; 400 `{error:"invalid_grant"\|"invalid_request"\|"unsupported_grant_type"\|"invalid_client", error_description}` |
| `POST /api/oauth/revoke` | form `token` (refresh or access) | 200 always |

Rate limits: register 10/min, authorize 5/min, token 30/min (per client IP).

### Connector HTTP service (`mcp serve --http`)

- Listens on `0.0.0.0:3100` in the container. Routes: `GET /healthz` (no auth, `{ok:true}`); `POST|GET|DELETE /mcp` (Streamable HTTP, stateless, JSON responses allowed); `GET /.well-known/oauth-protected-resource` and `/.well-known/oauth-protected-resource/mcp` (fetched from the API and cached 5 min).
- Requires `Authorization: Bearer fbc1.…`; otherwise 401 with `WWW-Authenticate: Bearer resource_metadata="{issuer}/.well-known/oauth-protected-resource/mcp"`. When the API answers 401 for the bearer the service answers 401 with `WWW-Authenticate: Bearer error="invalid_token", resource_metadata="…"`.
- Calls the API at `FILINGBRIDGE_API_URL` (`http://api:8080`) with header `X-Accounts-Api-Key` from `FILINGBRIDGE_API_KEY_FILE`, plus the inbound bearer and its own `X-FilingBridge-Client`. Never forwards inbound `Origin`, `Referer`, `Cookie` or `Sec-Fetch-*`.
- Issuer from `FILINGBRIDGE_PUBLIC_URL`. Binary downloads in HTTP mode: base64 inline up to 1 MiB, else an error telling the agent to use the stdio connector or a smaller artefact. CSV import in HTTP mode: `contentBase64` ≤ 5 MiB.

### Frontend

- `GET /.well-known/oauth-authorization-server` → proxies `GET /api/oauth/metadata/authorization-server`.
- `GET /.well-known/oauth-protected-resource` and `/.well-known/oauth-protected-resource/mcp` → proxy `/api/oauth/metadata/protected-resource`.
- `GET|POST|DELETE /mcp` → streams to `MCP_URL` (`http://mcp:3100/mcp`); 404 when `MCP_URL` unset. Forwards `authorization, accept, content-type, mcp-session-id, mcp-protocol-version, last-event-id`; passes back `content-type, mcp-session-id, mcp-protocol-version, www-authenticate, cache-control, retry-after`; no response timeout on the SSE body.
- `/oauth/authorize` page: public (no session), calls `describe`, shows client name and **redirect host** prominently, collects workspace slug, email, password, authenticator code (or recovery code), access level and data scope (pre-filled from `scope`), consent button; on success `window.location.assign(redirectTo)`; "Deny" redirects `redirectUri?error=access_denied&state=…` only after `describe` validated it.
- `/settings/approvals`: signed-in users; lists `GET /api/auth/connector/approvals`, deep-links `?id=`, grants with password + authenticator code via `POST /api/auth/connector/approve` (browser session + CSRF via `apiFetch`).
- `/settings/agents`: lists `GET /api/auth/connector/sessions`, revoke via `POST /api/auth/connector/sessions/{familyId}/revoke`, activity via `GET /api/auth/connector/activity` (Owner may choose a user from `/api/admin/users`).
- The 428 response's `approvalsUrl` = `{issuer}/settings/approvals?id={approvalId}` again once C lands.

## Tasks

### A1 Backend OAuth
- [ ] Refactor `ConnectorSessionService.LoginAsync` into `VerifyCredentialsAsync` (password + second factor + posture checks → principal) and `IssueAsync(principal, level, scope, deviceLabel, clientVersion)`; `LoginAsync` composes them.
- [ ] `Services/OAuthClientCodec.cs` (stateless signed client ids `fbcl1.`), `Services/OAuthService.cs`, `Entities` `OAuthAuthorizationCode` (`oauth_authorization_codes`, DirectTenantId RLS, hashed code `fbac1.<tenant>.<random>`), migration, `Endpoints/OAuthEndpoints.cs`, `ConnectorConfig.OAuthRedirectHosts`, rate-limit policies, production-safety (HTTPS issuer outside loopback when enabled).
- [ ] Server-side Withheld gate for `registeredOffice*` on company create/update/onboard.
- [ ] Tests: client codec tamper/expiry, redirect policy matrix, PKCE, code single use + expiry + client/redirect mismatch, scope mapping, Full scope Owner-only, refresh via OAuth, revoke, cross-site refusal, metadata shape; Postgres code-spend race; RLS catalogue.
- [ ] Regenerate OpenAPI + frontend types.

### B1 Connector HTTP service
- [ ] `mcp/src/http-server.ts`, `serve --http`, per-request server with `StaticBearer`, protected-resource metadata, 401 semantics, base64 file mode, tests (`http-transport-auth`, `oauth-metadata`).
- [ ] `Dockerfile.mcp`; `mcp` service in `compose.private.yml` and `compose.production.yml` (hardened like `frontend`, `frontend_api` network only, no ports, secret `accounts_mcp_api_key`, API `ApiAccess__Keys__1__*` "MCP service" Admin with hash secret `accounts_mcp_api_key_hash`); frontend env `MCP_URL`.
- [ ] Verifiers (`verify-private-compose.ps1`, `verify-production-compose-images.ps1`, `container-hardening-policy.mjs`, `verify-build-inputs.mjs` node image count), Private Server module key generation, CI image build + `scripts/mcp-live-roundtrip.mjs` in production-smoke.

### C1 Frontend
- [ ] Routes and pages above, `AuthProvider` public-route allowance for `/oauth/authorize`, `proxy.ts` matcher exclusions for `/mcp` and `/.well-known`, nav links, `permissions.ts` + backend `FrontendPermissionMatrixTests` if routes change, unit/render tests, proxy tests.

### D Integration
- [ ] Live: dev stack with `mcp` service; Claude Code `claude mcp add --transport http filingbridge http://localhost:3000/mcp` OAuth flow; Codex config; docs (`mcp/README.md`, `Docs/architecture/mcp-connector.md`, deployment docs); PR.
