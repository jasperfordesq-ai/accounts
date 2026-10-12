# MCP Connector Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give FilingBridge a backend-issued connector credential with per-action approval and a personal-data gate, plus a stdio MCP server (`mcp/`) that exposes every UI capability as MCP tools.

**Architecture:** The API gains bearer connector sessions (`connector_sessions`), a posture middleware (access level, data scope, write budget, activity log) and an approval middleware (428 → human grants with password + TOTP → single-use spend). The TypeScript package mirrors CharityPilot's connector: declarative tool catalogue, field allowlists, keychain credentials, `approve` CLI. Phase 2 (OAuth + in-stack HTTP) and phase 3 (MCPB bundle + VM acceptance) have their own plans.

**Tech Stack:** .NET 10 Minimal API, EF Core 10, PostgreSQL 16 (forced RLS), xUnit; Node 24, TypeScript 5.9, `@modelcontextprotocol/sdk` 1.30.0, `@napi-rs/keyring` 1.3.0, `node:test`.

**Spec:** `Docs/superpowers/specs/2026-10-12-mcp-connector-design.md`

## Global Constraints

- Connector is disabled by default: `Connector:Enabled=false` in `appsettings.json`, compose defaults `false`.
- Header names: `Authorization: Bearer fbc1.…`, `X-FilingBridge-Client: mcp-connector/<semver>`, `X-FilingBridge-Reason`, `X-FilingBridge-Approval`, `X-Correlation-ID`, `Idempotency-Key`, `If-Match`.
- Token prefixes: access `fbc1.`, refresh `fbr1.`. Access TTL 15 min; refresh idle 30 days; family absolute 90 days; approval TTL 5 min; write budget 30/min per family.
- Access levels `Read|Write|Admin`; data scopes `Withheld|Full`; `Full` requires Owner.
- Refusal codes: `CONNECTOR_SESSION_INVALID` 401, `BROWSER_CLIENT_REJECTED` 403, `SESSION_READ_ONLY` 403, `SESSION_LEVEL_TOO_LOW` 403, `DATA_SCOPE_WITHHELD` 403, `CONNECTOR_NOT_PERMITTED` 403, `CONNECTOR_WRITE_LIMIT` 429, `APPROVAL_REQUIRED` 428, `APPROVAL_REFUSED` 401, `MFA_ENROLMENT_REQUIRED` 403, `DATA_SCOPE_FORBIDDEN` 403.
- Approval command text: `filingbridge-mcp approve <id>`.
- Every middleware refusal writes the response and `return`s; tests assert `next` was not invoked.
- Refusal-time persistence (approval mint, activity rows) uses a fresh DI scope.
- New tables carry `TenantId`, a tenant query filter, and forced RLS via `TenantIsolationMigrationSql.ConnectorPolicyInventory`.
- No CRO/ROS submission tool, route, or HTTP client. `mcp/src/instructions.ts` contains: "CRO and ROS final submission remain external actions recorded in workflow state."
- Backend build/test locally: `-p:ArtifactsPath=$env:TEMP/accts-art` (WDAC). npm: `npm ci --prefix mcp` only, never `npm install --prefix mcp` from the root.
- Package: name `filingbridge-mcp`, license `AGPL-3.0-or-later`, Node `>=24 <25`, exact pins.

## Review Focus

1. A connector bearer and a browser cookie arriving together must be refused, never merged into one identity.
2. A browser page reaching `/api/auth/connector/*` through the Next proxy carries `Origin`/`Sec-Fetch-*` and must be refused before any password check.
3. An approval minted for one body must not be spendable with a different body, a different session family, after expiry, or twice (including two concurrent retries).
4. An offboarded, deactivated or password-changed user's connector tokens stop working at once; a browser logout leaves them working.
5. A `Withheld` session must not receive officer addresses, user emails, director-loan counterparty names or any binary download, via any tool, including `fetch` and resource templates.

---

### Task 1: Persistence — config, entities, DbContext, RLS, migration

**Files:**
- Create: `backend/Accounts.Api/Rules/ConnectorConfig.cs`, `backend/Accounts.Api/Entities/ConnectorEntities.cs`, migration `backend/Accounts.Api/Data/Migrations/<ts>_AddMcpConnectorSessions.cs`
- Modify: `Data/AccountsDbContext.cs`, `Data/TenantIsolationPolicyCatalog.cs`, `Data/TenantIsolationMigrationSql.cs`, `Data/PersistenceOwnershipInvariantValidator.cs`
- Test: `Accounts.Tests/DatabaseTenantIsolationSqlTests.cs` (catalog equality includes `ConnectorPolicyInventory`), `Accounts.Tests/PersistenceOwnershipInvariantTests.cs` (connector row with mismatched tenant rejected)

**Interfaces — Produces:**
- `ConnectorConfig { bool Enabled; string SigningKey; int AccessTokenMinutes=15; int RefreshIdleDays=30; int FamilyAbsoluteDays=90; int ApprovalMinutes=5; int WriteBudgetPerMinute=30; int ActivityRetentionDays=400; string? PublicBaseUrl; static bool IsValid(ConnectorConfig) }`
- `ConnectorSession { Guid Id; int TenantId; int UserId; Guid FamilyId; DateTime FamilyCreatedAtUtc; string ClientKind; string AccessLevel; string DataScope; string RefreshTokenHash; long PasswordLastChangedAtTicks; DateTime? MfaVerifiedAtUtc; string? MfaMethod; string? DeviceLabel; string? ClientVersion; DateTime CreatedAtUtc; DateTime ExpiresAtUtc; DateTime? LastUsedAtUtc; DateTime? RevokedAtUtc; string? RevocationReason; Guid? RotatedFromSessionId; UserAccount User }`
- `ConnectorActionApproval { Guid Id; int TenantId; int UserId; Guid SessionFamilyId; string RequestDigest; string Summary; string Method; string RoutePattern; string? ResourceId; int? CompanyId; DateTime CreatedAtUtc; DateTime ExpiresAtUtc; DateTime? ApprovedAtUtc; DateTime? ConsumedAtUtc }`
- `ConnectorActivityEvent { long Id; int TenantId; int UserId; Guid SessionId; Guid SessionFamilyId; string AccessLevel; string DataScope; string Method; string RoutePattern; string? ResourceId; int? CompanyId; int StatusCode; string? RequestId; string? Reason; DateTime OccurredAtUtc }`
- Constants `ConnectorAccessLevels.{Read,Write,Admin}`, `ConnectorDataScopes.{Withheld,Full}`, `ConnectorClientKinds.McpConnector`, `ConnectorRevocationReasons.{Rotated,Logout,ReuseDetected,SecurityStateRevoked,UserRevoked}`.
- DbSets `ConnectorSessions`, `ConnectorActionApprovals`, `ConnectorActivityEvents`; tables `connector_sessions`, `connector_action_approvals`, `connector_activity_events`.

- [ ] Write failing tests: catalog equality (`Version1 + DirectorsApproval + HistoricAnchor + Connector`), ownership validator rejects a `ConnectorSession` whose `TenantId` differs from its user's.
- [ ] Add entities, config, mappings (check constraints on level/scope/kind, unique `RefreshTokenHash`, indexes `(TenantId,UserId,RevokedAtUtc,ExpiresAtUtc)`, `(TenantId,FamilyId)`, approval partial unique `(SessionFamilyId,RequestDigest) WHERE "ConsumedAtUtc" IS NULL`, query filters `CurrentTenantId == null || x.TenantId == CurrentTenantId`).
- [ ] Generate migration with the design-time factory; append posture-immutable trigger (`UPDATE` of `AccessLevel`, `DataScope`, `FamilyId`, `UserId`, `TenantId` raises 23514) and `BuildInstallTablesSql(ConnectorPolicyInventory)`.
- [ ] Run targeted tests; run `dotnet ef migrations has-pending-model-changes`; commit.

### Task 2: Route catalogues

**Files:** Create `Services/PrivilegedRouteCatalog.cs`, `Services/PersonalDataRouteCatalog.cs`; modify `Middleware/RecentAuthenticationMiddleware.cs` (`RequiresRecentMfa` forwards to the catalogue). Test `Accounts.Tests/ConnectorRouteCatalogTests.cs`.

**Produces:** `PrivilegedRouteCatalog.RequiresHumanConfirmation(PathString, string method)`, `PrivilegedRouteCatalog.IsAdminLevel(PathString, string method)`, `PersonalDataRouteCatalog.IsWithheldRoute(PathString, string method)`, `PersonalDataRouteCatalog.WithheldBodyFields(PathString, string method) : IReadOnlySet<string>`, `PersonalDataRouteCatalog.IsPersonalDataRead(PathString, string method)`.

- [ ] Tests: every existing `RequiresRecentMfa` sample is unchanged (trailing slash cases); every human-confirmation route is admin-level; `/api/admin/users` GET is admin-level and withheld; `POST /api/companies/{id}/officers` withholds `address`; `GET …/documents/accounts-package` withheld; `GET /api/companies` not withheld.
- [ ] Implement; run; commit.

### Task 3: Connector sessions

**Files:** Create `Services/ConnectorSessionContext.cs`, `Services/ConnectorTokenCodec.cs`, `Services/ConnectorSessionService.cs`; modify `Services/UserLifecycleService.cs` (`RevokeSecurityStateAsync` revokes connector rows), `Services/AuditEventCodes.cs`. Test `Accounts.Tests/ConnectorSessionTests.cs`.

**Produces:**
- `ConnectorSessionContext(Guid SessionId, Guid FamilyId, string AccessLevel, string DataScope, string? ClientVersion)` with `ItemKey`, `Get(HttpContext)`.
- `ConnectorTokenCodec(byte[] key)`: `string CreateAccessToken(ConnectorAccessPayload)`, `ConnectorAccessPayload? ReadAccessToken(string? token, DateTimeOffset now)`, `static string CreateRefreshToken(int tenantId)`, `static int? ReadRefreshTokenTenant(string?)`.
- `ConnectorSessionService`: `LoginAsync(ConnectorLoginInput, string? clientVersion, CancellationToken) : ConnectorLoginResult`, `RefreshAsync(string refreshToken) : ConnectorTokenResult?`, `LogoutAsync(Guid? familyId, string? refreshToken)`, `AuthenticateAsync(string bearer, DateTimeOffset now) : (AuthenticatedUser, ConnectorSessionContext)?`, `RevokeAllForUserAsync(int userId, string reason)`, `RevokeFamilyAsync(int userId, Guid familyId, string reason) : bool`, `ListSessionsAsync(int userId)`, `GrantApprovalAsync(AuthenticatedUser, Guid approvalId, string? password, string? totpCode) : ConnectorApprovalGrant?`.
- DTOs `ConnectorLoginInput(string? TenantSlug, string? Email, string? Password, string? AccessLevel, string? DataScope, string? TotpCode, string? RecoveryCode, string? DeviceLabel)`, `ConnectorLoginResponse(AuthResponse User, string AccessToken, DateTime AccessTokenExpiresAtUtc, string RefreshToken, DateTime RefreshTokenExpiresAtUtc, ConnectorSessionPosture Session)`, `ConnectorSessionPosture(Guid SessionId, Guid FamilyId, string ClientKind, string AccessLevel, string DataScope, string Role, string TenantSlug, int UserId, bool MfaVerified, DateTime AccessTokenExpiresAtUtc, int WriteBudgetPerMinute, int ApprovalMinutes)`.

- [ ] Tests (InMemory, `IdentityFixture` pattern): Owner login with TOTP issues tokens; privileged login without code → failure; never-enrolled → `MFA_ENROLMENT_REQUIRED`; Accountant `Full` → `DATA_SCOPE_FORBIDDEN` and no row; Client may connect without TOTP; access token authenticates; refused after password change, offboard/revoke (`RevokeSecurityStateAsync`), row revoke, expiry, tampered signature, cookie-key signature; **still valid after `AuthService.RevokeSessionAsync`** (browser logout); refresh rotates and old refresh token refused; reuse revokes family; logout revokes family only.
- [ ] Implement; run; commit.

### Task 4: Bearer path, guard, posture, CSRF

**Files:** Create `Services/ConnectorClientGuard.cs`, `Services/ConnectorWriteBudget.cs`, `Middleware/ConnectorPostureMiddleware.cs`; modify `Middleware/UserSessionMiddleware.cs`, `Middleware/CsrfProtectionMiddleware.cs`, `Middleware/AuditTrailMiddleware.cs` (payload `client`). Test `Accounts.Tests/ConnectorMiddlewareTests.cs`.

**Produces:** `ConnectorClientGuard.Evaluate(HttpRequest, string sessionCookieName, string csrfCookieName) : string? refusalCode`, `ConnectorClientGuard.ClientHeader`, `ConnectorWriteBudget.TryTake(Guid familyId, DateTimeOffset now, out TimeSpan retryAfter)`, `ConnectorAudit.ClientAttribution(HttpContext) : object?`.

- [ ] Tests: missing/invalid client header, each of `Origin`/`Referer`/`Sec-Fetch-Site`/`Sec-Fetch-Dest`, session cookie present → 403 and next not invoked; valid bearer sets both items and no `Set-Cookie`; invalid bearer 401 `CONNECTOR_SESSION_INVALID`; Read+POST 403; Read+`connector/logout` allowed; Write+admin route 403; Withheld+withheld route 403; Withheld+POST officers with `address` 403 and without it passes; browser auth routes refused for connectors; 31st write 429 with `Retry-After`; activity row persisted for success and refusal; CSRF skipped only with connector context; cookie sessions unchanged.
- [ ] Implement; run; commit.

### Task 5: Per-action approval

**Files:** Create `Services/ConnectorActionDigest.cs`, `Services/ConnectorActionSummaryService.cs`, `Middleware/ConnectorActionApprovalMiddleware.cs`; modify `Middleware/RecentAuthenticationMiddleware.cs` (connector branch requires spent marker). Test `Accounts.Tests/ConnectorApprovalTests.cs`; Postgres race in `Accounts.Tests/ConnectorPostgresTests.cs`.

**Produces:** `ConnectorActionDigest.Compute(Guid familyId, string method, string path, ReadOnlySpan<byte> body) : string`, `ConnectorActionApprovalMiddleware.SpentItemKey`, 428 body `{error, code, approvalId, summary, resourceId, expiresAt, command, approvalsUrl}`.

- [ ] Tests: digest ignores key order and query, keeps array order, `{}` == empty body; no header → 428 shape, next not invoked, row persisted even though the request transaction rolls back; repeat ask returns same id; approve with wrong password / wrong TOTP / foreign id / expired → identical refusal; approved + header → next invoked once, marker set, RecentAuthentication passes; second spend → 428; different body → 428; summary never contains address or email; role-denied route mints nothing. Postgres: two parallel spends, exactly one passes.
- [ ] Implement; run; commit.

### Task 6: Endpoints, wiring, safety, contract

**Files:** Create `Endpoints/ConnectorAuthEndpoints.cs`, `Services/SearchService.cs`, `Endpoints/SearchEndpoints.cs`; modify `Program.cs`, `Endpoints/EndpointRegistration.cs`, `Services/RoleAuthorizationService.cs`, `Services/ApiAccessService.cs`, `Services/ProductionSafetyService.cs`, `appsettings.json`, `appsettings.Development.json`, OpenAPI JSON, `frontend/src/lib/generated/accounts-api-v1.ts`, `frontend/src/lib/apiProxyResponse.ts`. Test `Accounts.Tests/ConnectorEndpointTests.cs`, `Accounts.Tests/SearchServiceTests.cs`, `Accounts.Tests/ProductionSafetyTests` additions.

Routes: `POST /api/auth/connector/login|refresh|logout|approve`, `GET /api/auth/connector/session|approvals/{id}|sessions|activity`, `POST /api/auth/connector/sessions/{familyId}/revoke`, `GET /api/search?q=&types=&companyId=&periodId=&limit=`.

- [ ] Tests: role service allows connector self-service routes for Client; search tenant-scoped, Client limited to assigned companies, transactions only with period; production safety refuses weak/duplicate signing key and out-of-range lifetimes when enabled.
- [ ] Wire middleware order `UserSession → ConnectorPosture → Csrf → TenantAccess → AuditTrail → ConnectorActionApproval → RecentAuthentication → RoleAuthorization → Period*`.
- [ ] Build (regenerates OpenAPI), `npm run generate:api-contract`, full backend + frontend tests; commit.

### Task 7: Compose and verifier plumbing

**Files:** `compose.private.yml`, `compose.production.yml`, `scripts/PrivateServer/PrivateServer.psm1`, `scripts/verify-private-compose.ps1`, `scripts/verify-production-compose-images.ps1`, `scripts/PrivateServer/Test-PrivateServer.ps1`, `.github/workflows/ci.yml` (ephemeral connector key), `compose.yml` (dev key + `Connector__Enabled: "true"`).

- [ ] Secret `connector_signing_key` (`CONNECTOR_SIGNING_KEY_FILE`), env `Connector__Enabled`, `Connector__SigningKey_FILE`, `Connector__PublicBaseUrl`; verifier scripts and their tests pass; commit.

### Task 8: `mcp/` package core

**Files:** `mcp/package.json`, `package-lock.json`, `tsconfig.json`, `.gitignore`, `src/{version,config,credentials,refresh-coordination,session,token-source,client,etag-cache,errors,results,redact,tool-input,tool-body,tools,session-level,session-info,instructions,server,cli}.ts`, tests for each.

**Produces:** `ToolDefinition { name; description; method?: 'POST'|'PUT'|'DELETE'; path; params?: ParamSpec[]; body?: FieldSpec[]; level?: 'write'|'admin'; destructive?: boolean; approval?: boolean; model?: string; shape?: string; noRecordsBecause?: string; requiresFullScope?: boolean; binary?: 'pdf'|'xhtml'|'csv'|'zip'|'json'; returnsCredential?: boolean }`, `runTool(tool, args, ctx)`, `ApiClient.request(method, path, {query, body, reason, approvalId, idempotencyKey, ifMatch})`, `ApprovalRequiredError`, `StructuredError {code, retryable, action, status?, retryAfterSeconds?, approvalId?, command?, expiresAt?, correlationId?}`.

- [ ] Port from CharityPilot (`C:\platforms\htdocs\charity-governence\mcp\src`), adapting names, headers, ids (`^[1-9]\d*$`), connector routes and posture shape; tests; `npm test --prefix mcp`; commit.

### Task 9: Tool catalogue, field policy, coverage

**Files:** `mcp/src/{field-policy,enums,tools-companies,tools-banking,tools-year-end,tools-reporting,tools-filing,tools-admin,route-coverage}.ts`, `src/generated/openapi-types.ts`; tests `field-policy`, `schema-drift`, `route-coverage`, `mutating-route-coverage`, `annotations`, `toolsets`, `enums`, `generated-types`.

- [ ] Every contract operation is a tool or in `EXCLUDED_ROUTES` with a reason; every tool declares `model`, `shape` or `noRecordsBecause`; every DELETE and confirmation route is `approval`; drift test reads OpenAPI schemas and `Entities/*.cs`; commit.

### Task 10: Files, search/fetch, resources, prompts

**Files:** `mcp/src/{files,file-tools,search,references,resources,prompts,attribution,legal-framework}.ts` and tests.

- [ ] Downloads write to `--download-dir` and return `{path, bytes, sha256, contentType}`; CSV import reads under `--upload-root`; `search` calls `/api/search`; `fetch {id}` resolves `filingbridge://kind/id` through the normal dispatch; resources and prompts listed; commit.

### Task 11: CI, verifiers, docs

**Files:** `.github/workflows/ci.yml` (job `mcp`), `config/build-input-policy.json`, `scripts/verify-build-inputs.mjs`, `scripts/verify-no-direct-filing-submission.ps1`, `THIRD_PARTY_NOTICES.md`, `Docs/architecture/mcp-connector.md`, `Docs/operations/production-runbook.md`, `LOCAL_SETUP.md`, `SECURITY.md`, `AGENTS.md`, `CLAUDE.md`, `mcp/README.md`, `mcp/HANDOVER.md`, `mcp/scripts/canary.mjs`.

- [ ] Verifiers pass locally; canary turns the suite red for each mutation; commit.

### Task 12: Live end-to-end on the dev stack

**Files:** `mcp/scripts/live-e2e.mjs`.

- [ ] Rebuild dev stack with connector enabled; connect as seeded Owner with TOTP; run the scenario list from the spec's Verification §4; record results in `mcp/HANDOVER.md`; commit; open PR.
