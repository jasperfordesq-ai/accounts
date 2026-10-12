# MCP connector

FilingBridge exposes its whole API to AI agents (Claude Code, Codex, Claude Desktop and Claude.ai) through a Model Context Protocol server. An agent acts **as the person who connected it**,
inside that person's tenant, role and company assignments, with an access level and a personal-data
scope chosen at connection time. It can never do more than the person could do in the web app, and
every irreversible step still needs that person to approve it.

Design: `Docs/superpowers/specs/2026-10-12-mcp-connector-design.md`.
Connector package: `mcp/` (see `mcp/README.md` for installation and client configuration).

## Pieces

| Piece | Where | Role |
|---|---|---|
| Connector sessions | `connector_sessions`, `Services/ConnectorSessionService.cs` | One row per refresh token; rows sharing `FamilyId` are one connected agent |
| Token codec | `Services/ConnectorTokenCodec.cs` | `fbc1.` HMAC access tokens (15 min) and opaque `fbr1.` refresh tokens |
| Bearer path | `Middleware/UserSessionMiddleware.cs` | `Authorization: Bearer fbc1.…` authenticates the same `AuthenticatedUser` the browser path does |
| Non-browser guard | `Services/ConnectorClientGuard.cs` | Requires `X-FilingBridge-Client: mcp-connector/<semver>`; refuses `Origin`, `Referer`, `Sec-Fetch-Site`, `Sec-Fetch-Dest` and the session/CSRF cookies |
| Posture | `Middleware/ConnectorPostureMiddleware.cs` | Access level, data scope, 30 writes/min per agent, activity log |
| Approval | `Middleware/ConnectorActionApprovalMiddleware.cs` | 428 + single-use approval for every human-confirmation route |
| Route catalogues | `Services/PrivilegedRouteCatalog.cs`, `Services/PersonalDataRouteCatalog.cs` | One list shared by the browser recent-MFA gate and the connector approval gate |
| Endpoints | `Endpoints/ConnectorAuthEndpoints.cs` | `/api/auth/connector/*` and `GET /api/search` |

Middleware order: `UserSession → ConnectorPosture → Csrf → TenantAccess → AuditTrail →
ConnectorActionApproval → RecentAuthentication → RoleAuthorization → Period*`.

## Signing in

`POST /api/auth/connector/login` takes `tenantSlug`, `email`, `password`, `accessLevel`
(`Read|Write|Admin`), `dataScope` (`Withheld|Full`) and, for Owner/Accountant/Reviewer, a `totpCode`
or `recoveryCode`. It runs the browser's own `IdentityAccessService.BeginLoginAsync` and
`CompleteChallengeAsync`, so lockout, PBKDF2 timing, MFA replay protection and lifecycle evidence are
identical. A privileged user who has never enrolled an authenticator is told to enrol in the web app
first. `Full` data scope requires the Owner role. The response carries the tokens in the body; no
cookie is ever set.

Refresh tokens rotate on every use. Presenting a spent refresh token revokes the whole agent (theft
signal). The family ends after 30 idle days or 90 days in total.

## Remote connection (OAuth 2.1)

Remote MCP clients (Claude.ai custom connectors, Claude Code `--transport http`, Codex) connect to
`{issuer}/mcp`, where the issuer is the origin in `Connector:PublicBaseUrl`. They discover
`{issuer}/.well-known/oauth-protected-resource/mcp` from the 401 `WWW-Authenticate` header, then
`{issuer}/.well-known/oauth-authorization-server`, register themselves, and send the person to the
consent page `{issuer}/oauth/authorize`.

| Step | Where | Notes |
|---|---|---|
| Registration | `POST /api/oauth/register` | Stateless: the `fbcl1.` client id is the signed registration (name + redirect URIs). Public clients only |
| Redirect policy | `OAuthService.IsAllowedRedirect` | Exact match; loopback `http` always; `https` only to `Connector:OAuthRedirectHosts` (default `claude.ai`, `claude.com`) |
| Consent | `/oauth/authorize` page → `GET /api/oauth/authorize/describe`, `POST /api/oauth/authorize` | The person signs in (workspace, email, password, authenticator) and picks level and data scope; the page shows the client name and redirect host. Cross-site POSTs are refused |
| Code | `oauth_authorization_codes` | `fbac1.` codes, 60 seconds, single use, PKCE S256, bound to client, redirect URI and resource |
| Token | `POST /api/oauth/token` | Issues an ordinary connector session (`fbc1.` + `fbr1.`); `refresh_token` grant rotates it |
| Revocation | `POST /api/oauth/revoke`, `/settings/agents` | Ends that agent |

Scopes map onto the connector posture: `fb:read`, `fb:write`, `fb:admin` (highest wins, default
`fb:write`) and `fb:data-full` (Owner only). Because every grant is a connector session, everything in
the rest of this document — approvals, the data gate, the write budget, revocation — applies unchanged.

The in-stack `mcp` service (`Dockerfile.mcp`, compose service `mcp`, reached through the frontend at
`/mcp`) serves Streamable HTTP. It forwards the caller's bearer to the API together with its own
service API key (`ApiAccess:Keys:1`, "MCP service") and never forwards browser headers.

People approve agent actions and disconnect agents in the web app at `/settings/approvals` and
`/settings/agents`; 428 responses link straight to the pending approval.

## What ends an agent session

| Event | Effect |
|---|---|
| Browser logout | None: agents survive it (it only bumps `SessionVersion`) |
| Password change or reset | All agents for that user stop at once (`PasswordLastChangedAt` is bound into every token) |
| Owner revoke-sessions, role/company change, deactivate, offboard | `UserLifecycleService.RevokeSecurityStateAsync` revokes every connector row |
| Account lockout | Tokens refused while the lockout lasts |
| `filingbridge-mcp disconnect`, `POST /api/auth/connector/logout`, `POST …/sessions/{familyId}/revoke` | That agent only |
| `AuthSession:SigningKey` rotation | All refresh tokens (their hashes are keyed from it) |
| `Connector:SigningKey` rotation, Private Server restore or host recovery | All access tokens |

## Access levels and data scope

- **Read**: GET only (plus the connector's own logout/approve routes).
- **Write**: everything the role allows except admin-level routes: every human-confirmation route,
  `/api/admin/*`, `/api/privacy/*`, `/api/operations/*`, company create/onboard/recover/quarantine
  list, production readiness and platform metrics, audit checkpoints.
- **Admin**: everything the role allows; human-confirmation routes still need an approval each time.
- **Withheld** (default): the API refuses user administration, privacy subject work and every
  binary artefact (PDF, iXBRL, CSV, handoff artefacts) and refuses writes that set officer
  addresses, director-loan counterparty names/terms/notes or related-party names/terms. The
  connector additionally strips identifying fields from every response by allowlist.
- **Full**: Owner only; nothing withheld.

Browser sign-in routes (`/api/auth/login`, `/reauthenticate`, `/password`, `/mfa/*`, …) are refused
for agents: an agent can never re-authenticate itself or change credentials.

## Per-action approval

For a connector session, every route in `PrivilegedRouteCatalog.RequiresHumanConfirmation` (every
DELETE, period status changes, approvals, mark-filed, filing-status and handoff writes, user
administration, privacy, chart adoption, historic anchors, …) answers:

```json
{ "code": "APPROVAL_REQUIRED", "approvalId": "…", "summary": "Delete officers, company Example Limited (CRO 123456), officer A Director (Director)",
  "expiresAt": "…", "command": "filingbridge-mcp approve …", "approvalsUrl": "https://…/settings/approvals?id=…" }
```

The approval is bound to SHA-256 of (agent family, method, path, canonical JSON body). The person runs
the command in their own terminal or opens the web approvals page linked from the 428, sees the server's
summary and enters password + authenticator code. The agent then repeats the identical call with
`X-FilingBridge-Approval`; one conditional update spends it. Any mismatch, expiry (5 minutes) or
second use gets a fresh 428. The browser's 10-minute recent-TOTP window is never consulted for
agents, so nothing elevates an agent for a period of time.

## Audit and activity

- API writes under `/api/companies/{id}` keep their `audit_logs` entries; for agents the payload gains
  `client {kind: McpConnector, sessionFamilyId, accessLevel, dataScope, clientVersion, reason}`.
  `reason` comes from `X-FilingBridge-Reason` (control characters stripped, 500 characters).
- `connector_activity_events` records every agent write, every refusal (401/403/428/429) and every
  personal-data read, including on non-company routes. Readable through
  `GET /api/auth/connector/activity` (own; Owner may pass `userId`).
- Session issue/revoke, refresh reuse and approval request/grant/refusal are durable audit events.

## Configuration

| Key | Default | Notes |
|---|---|---|
| `Connector:Enabled` | `false` | Development enables it in `appsettings.Development.json` |
| `Connector:SigningKey` | — | Must be strong and differ from `AuthSession:SigningKey` and `IdentitySecurity:IdentityHmacKey` outside Development |
| `Connector:AccessTokenMinutes` | 15 | 5–30 |
| `Connector:RefreshIdleDays` / `FamilyAbsoluteDays` | 30 / 90 | |
| `Connector:ApprovalMinutes` | 5 | 2–10 |
| `Connector:WriteBudgetPerMinute` | 30 | 5–120 |
| `Connector:PublicBaseUrl` | — | Public origin: OAuth issuer, MCP resource and web approvals link |
| `Connector:OAuthRedirectHosts` | `claude.ai`, `claude.com` | Extra HTTPS redirect hosts for OAuth clients (configuration adds to the defaults) |

Compose: secret `connector_signing_key` (`CONNECTOR_SIGNING_KEY_FILE`) mounted into the API only.
Private Server generates it at setup and on the first environment write after an update, and
rotates it on restore and host recovery; `PRIVATE_CONNECTOR_ENABLED` (from the state file's
`connectorEnabled`) turns the feature on. Public Production uses `CONNECTOR_ENABLED`.

Private Server also runs the in-stack `mcp` service (image `images.mcp` in `release.json`) with its
own service key `accounts_mcp_api_key` (the API mounts only `accounts_mcp_api_key_hash`), generated
at setup and on the first environment write after an update, and regenerated on restore and host
recovery. `filingbridge connector enable [--origin local|tailscale] | disable | status` is the only
supported switch: it sets `connectorEnabled` and `connectorPublicOrigin`, which becomes
`PRIVATE_CONNECTOR_PUBLIC_ORIGIN` (`Connector:PublicBaseUrl` and the service's
`FILINGBRIDGE_PUBLIC_URL`). Claude.ai is not supported there because a Private Server has no public
ingress; see `Docs/deployment/private-server.md#connect-an-ai-agent`.

## Boundaries that do not move

- No tool, route or client submits anything to CRO or ROS. Agents record externally completed steps
  exactly as a person would in the web app.
- The connector reaches the API through the same frontend proxy as the browser; the API is never
  published.
- RLS: all three connector tables are tenant-owned with forced row-level security
  (`TenantIsolationMigrationSql.ConnectorPolicyInventory`).
