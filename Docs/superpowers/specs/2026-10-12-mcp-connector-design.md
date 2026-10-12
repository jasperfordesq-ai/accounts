# FilingBridge MCP Connector — implementation plan

## Context

FilingBridge has no way for an AI agent (Claude.ai, Claude Code, Codex, Claude Desktop) to drive
it. Every `/api` request today needs the frontend proxy's API key **and** an HMAC-signed browser
session cookie plus a CSRF double-submit; the proxy strips the cookies that keep a session alive,
so emulating a browser is fragile and unauditable. The sibling product CharityPilot already ships
a production-hardened MCP connector (`C:\platforms\htdocs\charity-governence\mcp` + its API side)
and FilingBridge mirrors CharityPilot elsewhere. Goal: a first-class remote MCP server, like the
big-vendor connectors, through which any tenant user can administer everything the UI can do,
with the same audit, approval and personal-data controls, and which stays connected until revoked.

Decisions taken with the product owner (2026-10-12):

| Decision | Choice |
|---|---|
| Audience | Product feature: any tenant user connects their own agent, scoped by their role |
| Transport | Both: Streamable HTTP + OAuth 2.1 inside the stack **and** a stdio CLI (same package) |
| Reach | Claude.ai, Claude Code, Codex, Claude Desktop. Public ingress only in Public Production; on the Private Server VM, Code/Codex over Tailscale and Desktop via stdio bundle |
| Approvals | Per-action: 428 → approve in a terminal (`filingbridge-mcp approve <id>`) **or** on a web approvals page (password + TOTP). No windowed elevation |
| Personal data | Gate mirrored: `dataScope = withheld \| full`; `full` requires Owner |
| Access level | `read \| write \| admin`, chosen at connect/consent, immutable per session family |
| Lifetime | Until revoked; refresh family 90 days absolute, 30 days idle; browser logout does **not** disconnect |
| Credential tools | `user_create` and `user_password_reset` included, admin + approval, credential returned to the agent |
| Build | TypeScript `mcp/` package mirroring CharityPilot; backend changes in C# |
| Targets | Automated tests on the dev stack + CI production-smoke; manual acceptance on the Hyper-V Private Server after a reviewed release |

Hard constraints (`AGENTS.md`): no tool may submit to CRO/ROS (CI greps for it); never weaken RLS,
signed tenant context, cookies, CSRF, auth or audit; deployment modes stay separate; Private Server
never gets Funnel/public ingress; `main` requires signed commits → land each phase via PR squash merge.
Backend commands need `-p:ArtifactsPath=$env:TEMP/accts-art` (WDAC).

## Architecture (one paragraph per piece)

- **Connector session (backend)**: `connector_sessions` row per session family: user, tenant,
  `AccessLevel`, `DataScope`, refresh-token hash, expiry, revocation, `RotatedFromSessionId`. Access
  token = 15-min HMAC-SHA256 envelope `fbc1.<payload>.<sig>` signed with a **separate** key
  `Connector:SigningKey` (same encoding as `AuthService.SignPayload`); payload binds
  `SessionId, FamilyId, UserId, TenantId, ConnectorCredentialVersion, PasswordLastChangedAtTicks`.
  Refresh token = opaque single-use `fbr1.<tenantId>.<random>`, hash stored, rotated per use, reuse
  revokes the family. New `user_accounts.connector_credential_version` is bumped by password change,
  offboard/deactivate, admin revoke-sessions and lockout, **not** by browser logout.
- **Bearer path**: `UserSessionMiddleware` gains a branch for `Authorization: Bearer fbc1.` → non-browser
  guard (`X-FilingBridge-Client: mcp-connector/<semver>` required; `Origin`/`Referer`/`Sec-Fetch-Site`/
  `Sec-Fetch-Dest`/session cookies refused) → `ConnectorSessionService.AuthenticateAsync` → same
  `AuthenticatedUser` in `HttpContext.Items` plus `ConnectorSessionContext`. Tenant RLS context is set
  from the signed payload before any DB read (as the cookie path does). CSRF skipped only when the
  connector context is present. The frontend proxy already forwards `Authorization` and custom headers.
- **Posture middleware** (after UserSession): refuses browser auth routes for connectors; `read` →
  403 on non-GET; `write` → 403 on admin-level routes; `withheld` → 403 on user admin, privacy, audit
  actor data, binary downloads and writes of withheld fields; 30 writes/min per family → 429;
  writes `connector_activity_events` (fresh DI scope) for every write and refusal.
- **Per-action approval**: `RecentAuthenticationMiddleware.RequiresRecentMfa` body moves to
  `PrivilegedRouteCatalog.RequiresHumanConfirmation` (forwarder kept). For connector sessions those
  routes answer `428 {code:"APPROVAL_REQUIRED", approvalId, summary, resourceId, expiresAt, command,
  approvalsUrl}`; digest = SHA-256(familyId, METHOD, path, canonical body); minted in a fresh scope
  (AuditTrail rolls back non-2xx); spent by one conditional `ExecuteUpdate` when the retry carries
  `X-FilingBridge-Approval`. Granting = password + TOTP, constant-time, opaque failure, via
  `POST /api/auth/connector/approve` (CLI) or the web approvals page. The MFA window is never used.
- **OAuth 2.1 (phase 2)**: backend is the authorization server (RFC 8414 metadata, RFC 7591 dynamic
  client registration, PKCE S256, RFC 8707 resource indicator, refresh rotation). The Next app hosts
  the authorize/consent page (tenant slug, email, password, TOTP, access level, data scope) and the
  well-known routes; the `mcp` service hosts `/.well-known/oauth-protected-resource` and validates
  bearers by forwarding them to the API. Scopes `fb:read fb:write fb:admin fb:data-full` map to
  level/scope. The stdio CLI keeps the direct `connector/login` path; both yield identical session rows.
- **Connector package `mcp/`**: CharityPilot layout — tools as data (`ToolDefinition` + `ParamSpec` /
  `FieldSpec` generating `additionalProperties:false` schemas), per-model field allowlists, structured
  errors `{code, retryable, action}`, `session_info` / `search` / `fetch` (Claude.ai shape), resources,
  prompts, keychain credentials, cross-process refresh lock, `approve` TTY-only. 231 tools over 231 of
  the 242 contract operations (+ the undocumented CSV import); 11 excluded with reasons (browser auth,
  health, monitoring). HTTP mode: `serve --http` with `StreamableHTTPServerTransport` (stateless, DNS
  rebinding protection), per-request `Server` using the inbound bearer plus the service API key.

## Phase 1 — backend credential + stdio connector + CI  (PR 1)

### Step 0: spec + plan docs
Write `Docs/superpowers/specs/2026-10-12-mcp-connector-design.md` (this design) and
`Docs/superpowers/plans/2026-10-12-mcp-connector-phase-1.md`; commit first.

### Backend (new files, in order)
1. `Rules/ConnectorConfig.cs` — `Enabled=false`, `SigningKey`, `AccessTokenMinutes=15`,
   `RefreshIdleDays=30`, `FamilyAbsoluteDays=90`, `ApprovalMinutes=5`, `WriteBudgetPerMinute=30`,
   `ActivityRetentionDays=400`, `PublicBaseUrl` (for `approvalsUrl`).
2. `Entities/ConnectorEntities.cs` — `ConnectorSession`, `ConnectorActionApproval`,
   `ConnectorActivityEvent`; `UserAccount.ConnectorCredentialVersion` (int, default 1).
3. `Services/ConnectorSessionContext.cs` — record + `ItemKey`.
4. `Services/PrivilegedRouteCatalog.cs` — `RequiresHumanConfirmation` (moved list), `IsAdminLevel`
   (= confirmation list ∪ `/api/admin/users*` ∪ `/api/privacy*` ∪ `/api/operations*` ∪ company
   create/onboard/delete/recover/quarantined ∪ production-readiness).
5. `Services/PersonalDataRouteCatalog.cs` — withheld routes/fields (officers `address`; company
   `registeredOffice*`, quarantine actor fields; users `email`/`displayName`; director loans
   `counterpartyName`/`loanTerms`/`reviewNote`/`reviewedBy`; related-party `partyName`/`terms`;
   audit actor/values; all binary downloads; privacy; user admin).
6. `Services/ConnectorClientGuard.cs`, `Services/ConnectorActionDigest.cs`,
   `Services/ConnectorActionSummaryService.cs` (route-keyed, public fields only),
   `Services/ConnectorWriteBudget.cs` (singleton sliding window keyed by family, `TimeProvider`).
7. `Services/ConnectorSessionService.cs` — login (reuses `AuthService.LoginAsync` + new
   `IdentityAccessService.VerifySecondFactorAsync` extracted from `CompleteChallengeAsync`),
   refresh rotation with reuse detection, logout, authenticate, revoke-all-for-user, approval
   grant/spend. Conditional updates use `ExecuteUpdateAsync` with the InMemory fallback pattern from
   `AuthService.RevokeSessionAsync`.
8. `Services/SearchService.cs` + `GET /api/search?q=&types=&companyId=&periodId=&limit=` —
   tenant/access-scoped references over companies, periods, officers, categories, notes,
   transactions (period required); returns `{type,id,title,ref}` only.
9. `Middleware/ConnectorPostureMiddleware.cs`, `Middleware/ConnectorActionApprovalMiddleware.cs`.
10. `Endpoints/ConnectorAuthEndpoints.cs` — group `/api/auth/connector`: `login`, `refresh`,
    `logout`, `session`, `approve`, `approvals/{id}`, `GET sessions` + `POST sessions/{familyId}/revoke`
    (own sessions; Owner may pass `userId`), `GET activity`; rate limits `connector-login` 5/min,
    `connector-refresh` 10/min, `connector-approve` 5/min.
11. `Data/Migrations/<ts>_AddMcpConnectorSessions.cs` — 3 tables, user column, posture-immutable
    trigger (pattern `TR_companies_tenant_immutable`), partial unique index on
    `(SessionFamilyId, RequestDigest) WHERE ConsumedAtUtc IS NULL`, RLS via
    `TenantIsolationMigrationSql.BuildInstallTablesSql(ConnectorPolicyInventory)`; add the inventory to
    `TenantIsolationPolicyCatalog` and `TenantIsolationMigrationSql`; `PersistenceOwnershipInvariantValidator`.

### Backend (edits)
`UserSessionMiddleware` (bearer branch; anonymous `connector/login|refresh`), `CsrfProtectionMiddleware`
(skip with context), `RecentAuthenticationMiddleware` (forwarder; connector branch requires spent
marker → 428), `AuditTrailMiddleware` (payload `client{kind,sessionFamilyId,accessLevel,dataScope,
reason}`), `AuditEventCodes` (connector codes), `RoleAuthorizationService` (connector self-service
routes for all roles; `GET /api/search` all roles), `ApiAccessService` (dev-only exemption for
connector login/refresh), `AuthService` (password change bumps credential version; logout untouched),
`UserLifecycleService.RevokeSecurityStateAsync` (bump + revoke connector rows), `MfaSecurityService`
(`HashConnectorRefreshToken`), `ProductionSafetyService` (strong key, distinct from session/identity
keys, bounds on minutes/days/budget), `AccountsDbContext`, `Program.cs` (options, services, rate
limits, middleware order `UserSession → ConnectorPosture → Csrf → TenantAccess → AuditTrail →
ConnectorActionApproval → RecentAuthentication → RoleAuthorization → Period*`), `EndpointRegistration`,
`appsettings*.json`, `compose.private.yml` / `compose.production.yml` (secret `connector_signing_key`,
`Connector__Enabled`, `Connector__PublicBaseUrl`), `scripts/PrivateServer/PrivateServer.psm1`,
`scripts/verify-private-compose.ps1`, `scripts/verify-production-compose-images.ps1`,
`frontend/src/lib/apiProxyResponse.ts` (pass through `retry-after`, `www-authenticate`,
`x-correlation-id`, `idempotency-*`), OpenAPI + generated TS regenerated.

### Backend tests (`Accounts.Tests`)
`ConnectorSessionTests`, `ConnectorClientGuardTests`, `ConnectorPostureMiddlewareTests`,
`PrivilegedRouteCatalogTests`, `ConnectorActionApprovalTests` (incl. "next not invoked" canaries and
mint-survives-AuditTrail-rollback), `SearchServiceTests`, `ConnectorPostgresTests` (spend race, rotation
race, RLS cross-tenant invisibility, posture trigger). Update `DatabaseTenantIsolationSqlTests`,
`PersistenceOwnershipInvariantTests`, `MigrationUpgradePostgresTests`, `PrivateServerModeTests`,
`IdempotencyServiceTests` (bearer replay), `AuditTrailPostgresIntegrationTests`.

### Connector package `mcp/` (port from CharityPilot; reference files named)
- `package.json` (`filingbridge-mcp`, bin `filingbridge-mcp`, AGPL-3.0-or-later, Node `>=24 <25`,
  exact pins `@modelcontextprotocol/sdk 1.30.0`, `@napi-rs/keyring 1.3.0`, dev `typescript`,
  `@types/node`, `openapi-typescript 7.13.0` matching `frontend/package.json`), `tsconfig.json`,
  `.gitignore`, `README.md`, `HANDOVER.md`.
- `src/`: `version`, `config` (commands `serve|connect|status|approve|disconnect|token|help`,
  profiles `local|private|default`, `--toolsets`, `--download-dir`, `--upload-root`, `--http` flags),
  `credentials` + `refresh-coordination` (copy), `session`, `token-source` (`RefreshingSession` /
  `StaticBearer`), `client` (headers, `Idempotency-Key` per POST, `If-Match` from `etag-cache`, 401
  retry once, 428 → `ApprovalRequiredError`, 412 → reread, download/upload), `errors`, `results`,
  `redact`, `tool-input` (+`intId`, `guid`, `date`, `sort`), `tool-body` (+`money`, `decimal`,
  `stringList`, `intIdList`, bounded `object`), `field-policy`, `enums` (tested against
  `components.schemas` enums), `tools` (+`approval`, `requiresFullScope`, `binary`), `tools-read`,
  `tools-write`, `tools-year-end`, `tools-filing`, `tools-admin`, `file-tools` + `files`, `search`,
  `references` + `fetch`, `resources` (`filingbridge://about|legal-framework|filing-boundary`,
  templates company/period/readiness/filing-status), `prompts` (year_end_walkthrough,
  categorise_uncategorised, pre_filing_check, classify_company_size, bank_import_reconcile,
  director_loan_compliance_review, charity_sorp_review, deadline_review), `instructions` (must contain
  "CRO and ROS final submission remain external actions recorded in workflow state."),
  `attribution` (asserted equal to `frontend/src/lib/attribution.ts`), `legal-framework` (asserted
  against `appsettings.json` SizeThresholds), `session-level`, `session-info`, `route-coverage`
  (`EXCLUDED_ROUTES` with reasons; `EXTRA_ROUTES` = CSV import), `server`, `cli`,
  `generated/openapi-types.ts` (committed, freshness-tested).
- Tool catalogue (231): core 3; companies 10; officers 4; periods 4; banking 5; transactions 6;
  categories 8; classification 11; year_end 45; disclosures 20; adjustments 7; audit 4; statements 7;
  working_papers 7; documents 9; revenue 12; deadlines 7; charity 16; filing 19; handoff 7;
  admin_users 10 (incl. `user_create`, `user_password_reset`: admin + approval, result carries the
  credential, `redactAlways` off, never written to activity/diagnostics); privacy 5; operations 3;
  system 2. Every DELETE and irreversible transition is admin + approval; no name/description uses
  "submit" as a verb.
- Tests `src/tests/*.test.ts` (node:test on `dist`): tool-input, tool-body, tools, write-tools,
  annotations, toolsets, server (InMemoryTransport), results, errors, references, resources, prompts,
  instructions, field-policy, schema-drift (DTOs from OpenAPI, entities parsed from `Entities/*.cs`,
  hard-coded `MUST_BE_GATED`), route-coverage, mutating-route-coverage, generated-types, enums,
  client, session, credentials, config, cli, files, search, attribution, legal-framework, packaging.
  `scripts/canary.mjs` (8 mutations that must turn the suite red), `scripts/live-e2e.mjs` against
  `compose.yml` (connect/refresh/revoke, gate open/closed, tenant isolation, read-level refusal,
  write + If-Match conflict, full 428 flow incl. wrong TOTP, PDF sha256 round trip, CSV import).

### CI / repo plumbing
`.github/workflows/ci.yml` job `mcp` (`npm ci --prefix mcp`, audit, `npm test --prefix mcp`,
canary); `config/build-input-policy.json` + `scripts/verify-build-inputs.mjs` (packages list);
`scripts/verify-no-direct-filing-submission.ps1` (scan `mcp/src/instructions.ts`, `tools-filing.ts`;
assert the boundary sentence); `THIRD_PARTY_NOTICES.md`; `Docs/architecture/mcp-connector.md`;
`Docs/operations/production-runbook.md`, `api-key-rotation.md`, `database-tenant-isolation.md`,
`SECURITY.md`, `LOCAL_SETUP.md`; `AGENTS.md` handoff note; `CLAUDE.md` tree + inventory line.

## Phase 2 — remote: OAuth 2.1, in-stack HTTP service, web UI  (PR 2)

Backend: `Endpoints/OAuthEndpoints.cs` — `GET /.well-known/oauth-authorization-server`,
`POST /oauth/register` (DCR, redirect-URI allowlist incl. `https://claude.ai/api/mcp/auth_callback`,
`http://127.0.0.1:*` / `http://localhost:*` for Claude Code & Codex), `GET/POST /oauth/authorize`
(JSON for the Next page; code bound to PKCE challenge, client, resource, chosen level/scope, 60 s TTL,
single use), `POST /oauth/token` (code → connector session + tokens; `refresh_token` → rotation),
`POST /oauth/revoke`; `oauth_clients` + `oauth_authorization_codes` tables (tenant-scoped, RLS);
`ProductionSafetyService` requires HTTPS `Connector:PublicBaseUrl` when OAuth enabled.
Frontend: `app/oauth/authorize/page.tsx` (credentials + TOTP + level/scope + consent, CSRF-free,
no session cookie issued), `app/.well-known/oauth-authorization-server/route.ts` (proxy),
`app/mcp/route.ts` (stream POST/GET/DELETE to `MCP_URL`, forward `authorization`, `mcp-session-id`,
`mcp-protocol-version`, `last-event-id`; body cap via shared `apiProxyBody.ts`), `proxy.ts` matcher
excludes `/mcp` and `/.well-known`; `app/settings/agents/page.tsx` (connected agents list/revoke,
activity), `app/settings/approvals/page.tsx` (pending approvals, grant with password + TOTP);
`lib/permissions.ts` + `FrontendPermissionMatrixTests` for the new routes.
Package: `src/http-server.ts` (`/healthz`, `/mcp`, `/.well-known/oauth-protected-resource`,
401 + `WWW-Authenticate: Bearer resource_metadata=...`), `cli.ts` `token issue|revoke`,
`file-tools` base64/resource-link mode, `tests/http-transport-auth.test.ts`, `tests/oauth-metadata.test.ts`.
Stack: `Dockerfile.mcp` (same `node:24-alpine` digest, stripped runner, uid 1002), service `mcp`
in both compose files (hardened block, `frontend_api` only, no ports, secret `accounts_mcp_api_key`,
API `ApiAccess__Keys__1__*` "MCP service" Admin key), `scripts/verify-private-compose.ps1`,
`test-private-compose.ps1`, `verify-production-compose-images.ps1`, `container-hardening-policy.mjs`,
`PrivateServer.psm1` (+key generation), `scripts/mcp-live-roundtrip.mjs` in production-smoke (stdio
connect via TOTP handoff JSON, tools/list, gated `officer_list`, 428 round trip, `/mcp` 200 with bearer
and 401 without, OAuth metadata discovery), `Docs/deployment/*.md`, `mcp/README.md` client sections
(Claude.ai custom connector URL `https://<host>/mcp`; Claude Code `claude mcp add --transport http
filingbridge https://<host>/mcp` then `/mcp` to sign in; Codex `[mcp_servers.filingbridge] url=...`).

## Phase 3 — Claude Desktop bundle + Private Server acceptance  (PR 3)
`mcp/manifest.json` (MCPB 0.2, `npx @anthropic-ai/mcpb validate` in CI), packaging test, Tailscale
Serve guidance for Code/Codex against the VM, acceptance run on the Hyper-V VM after a reviewed
pre-release (per `filingbridge-live-vm` memory procedure), AGENTS.md evidence note.

## Verification (end to end)
1. Backend: `dotnet build backend/Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art`;
   `dotnet test backend/Accounts.Tests/Accounts.Tests.csproj -c Release -p:ArtifactsPath=$env:TEMP/accts-art`
   with `ACCOUNTS_POSTGRES_TEST_CONNECTION` set (dev Postgres on 5433); report executed-test count.
2. Contract: rebuild regenerates `accounts-api-v1.json`; `cd frontend && npm run generate:api-contract
   && npm run test:api-contract && npm test && npm run build`.
3. Connector: `npm ci --prefix mcp && npm test --prefix mcp && node mcp/scripts/canary.mjs`.
4. Live: `docker compose up -d` (dev stack, `Connector__Enabled=true`), `node mcp/scripts/live-e2e.mjs`;
   then drive the built server with the SDK client over stdio: `session_info`, `company_list`,
   `officer_list` (no `address` under withheld), `debtor_create` → `debtor_update`,
   `officer_delete` → 428 → `filingbridge-mcp approve <id>` → retry succeeds; `audit_log_list` shows
   `client.kind = McpConnector` with the reason.
5. Phase 2: `claude mcp add --transport http filingbridge http://127.0.0.1:3000/mcp` against the dev
   stack, complete OAuth in the browser, call `session_info`; Codex config the same; production-smoke
   job green with the `mcp-live-roundtrip` artifact.
6. Scripts: `pwsh scripts/verify-no-direct-filing-submission.ps1`, `verify-private-compose.ps1`,
   `verify-production-compose-images.ps1` all pass.

## Risks
- Guard/approval middleware must `return` after writing a refusal (CharityPilot's live bug); tests
  assert `next` not invoked. Refusal-time writes need fresh DI scopes (AuditTrail rollback).
- RLS: new tables must be in both catalog and migration inventory or readiness fails.
- OAuth redirect allowlist and DCR need care (open redirect, client impersonation); resource
  indicator must equal the `/mcp` URL; codes single-use and PKCE-bound.
- Claude.ai reach requires Public Production ingress; the Private Server VM cannot serve it.
- Credential-returning tools: results excluded from activity/diagnostic logging and redaction
  registry; description warns the agent to hand the credential to the human immediately.
- `ExecuteUpdateAsync` unsupported on InMemory; race tests are Postgres-only.

## Refinements found while planning phase 1 (2026-10-12)

- **No `connector_credential_version` column.** Connector validation checks the row (not revoked,
  not expired), the user (`IsActive`, not offboarded, not locked) and `PasswordLastChangedAt` against
  the value copied onto the row at login. `UserLifecycleService.RevokeSecurityStateAsync` (revoke
  sessions, offboard, deactivate, role/company changes, resets) explicitly revokes connector rows.
  Browser logout bumps `SessionVersion` only, so it does not disconnect agents. A locked account's
  tokens are refused for the lockout's duration.
- **No MFA extraction.** Connector login runs the existing `IdentityAccessService.BeginLoginAsync`
  and, for privileged roles, immediately `CompleteChallengeAsync` with the supplied TOTP or recovery
  code. A user who has never enrolled MFA gets `MFA_ENROLMENT_REQUIRED` and must enrol in the
  browser first. Approval grants reuse `IdentityAccessService.ReauthenticateAsync`
  (password + TOTP, replay-protected counter, lifecycle event) without issuing any cookie.
