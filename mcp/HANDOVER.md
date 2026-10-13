# MCP connector — handover

Written 2026-10-12, at the end of phase 1 (plan `Docs/superpowers/plans/2026-10-12-mcp-connector-phase-1.md`,
Tasks 8–10 and the `mcp/` part of 11). `README.md` beside this file says what the connector is and how to
run it; this says what state it is in and what is deliberately unfinished.

## What was built

A TypeScript stdio MCP server, ported from CharityPilot's production-hardened connector and adapted to
FilingBridge's backend (commit da1df56 and the in-flight PrivilegedRouteCatalog change):

- **Credential** (`session.ts`, `credentials.ts`, `refresh-coordination.ts`, `token-source.ts`):
  `POST /api/auth/connector/login|refresh|logout`, 15-minute `fbc1.` bearer kept in memory, single-use
  `fbr1.` refresh token in the OS keychain (`filingbridge-mcp`, one entry per origin, bound to the origin),
  renewal under a cross-process loopback lock so two AI clients cannot spend the same refresh token.
  `RefreshingSession` and a `StaticBearer` stub implement `TokenSource`; `server.ts`'s `createServer()`
  builds one MCP `Server` around any token source, ready for phase 2's per-request HTTP mode.
- **Client** (`client.ts`, `etag-cache.ts`): `X-FilingBridge-Client: mcp-connector/0.1.0` on every
  request; no Origin, Referer or cookie ever; `X-Correlation-ID` per call; `Idempotency-Key` on every POST
  (reused on the one retry after a dropped connection); `X-FilingBridge-Reason`; `X-FilingBridge-Approval`;
  period `ETag` remembered and sent as `If-Match`, dropped on 409/412 with `action: reread`; one renewal
  on 401; 428 → `ApprovalRequiredError`; every refusal coded with `{code, retryable, action}`.
- **Catalogue** (`tools-*.ts`, `bodies.ts`, `enums.ts`): 233 route tools + 3 core tools. Body field lists
  were generated once from the contract and are now hand-maintained; `bodyFields()` makes every field
  name a compile-time check against `src/generated/openapi-types.ts`, and descriptions come from the C#
  input records and their validation messages.
- **Personal-data gate** (`field-policy.ts`): per-model allowlists with nested models, an envelope rule for
  FilingBridge's `{items, total, page...}` pages, client-side refusal of withheld writes (nested ones
  too), and `requiresFullScope` for unfilterable payloads. Posture comes from
  `GET /api/auth/connector/session`, never a flag; an unreachable API fails closed (read/withheld).
- **Files** (`files.ts`): downloads to `--download-dir` (never overwrite, owner-only, path + sha256 back),
  uploads and evidence attachments from `--upload-root` (realpath containment, no dot-segments, size caps).
- **search/fetch, resources, prompts, instructions, attribution, legal framework** as the README lists.
- **CLI** (`commands.ts`): `serve | connect | status | approve <id> | disconnect | help | version`;
  `approve` refuses unless both stdin and stdout are terminals and shows the host and the server's summary
  before asking for password and TOTP; `--password-stdin` only on `--profile local` and only from a pipe.

### Counts

| Toolset | Tools | Toolset | Tools | Toolset | Tools |
| --- | --- | --- | --- | --- | --- |
| core | 3 | year_end | 45 | revenue | 12 |
| companies | 10 | disclosures | 20 | deadlines | 7 |
| officers | 4 | adjustments | 7 | charity | 16 |
| periods | 4 | audit | 4 | filing | 19 |
| banking | 5 | statements | 7 | handoff | 7 |
| transactions | 6 | working_papers | 7 | admin_users | 10 |
| categories | 8 | documents | 9 | privacy | 5 |
| classification | 11 | operations | 3 | system | 2 |
| | | | | connector | 5 |

236 tools in all. Contract: 253 operations = 233 route tools + 2 core routes (`GET /api/search` →
`search`, `GET /api/auth/connector/session` → `session_info`) + 18 exclusions:

- browser sign-in (cookie/CSRF) routes: `POST /api/auth/login`, `mfa/challenge`, `reauthenticate`,
  `mfa/recovery-codes`, `invitations/accept`, `recovery/complete`, `logout`, `password`, `GET /api/auth/me`
  — the connector has its own credential, and recovery codes, invitation acceptance, password reset
  completion and password change belong to the human;
- `GET /health`, `/health/live`, `/health/ready` — orchestrator probes;
- `POST /api/system/monitoring/error-smoke`, `client-event` — release/browser monitoring hooks;
- `POST /api/auth/connector/login|refresh|logout|approve` — connector credential plumbing, used by the CLI,
  never by the model; `approve` in particular must never be a tool.

Approval tools: every DELETE plus PrivilegedRouteCatalog's confirmation list (56 tools carry
`approval: true`); levels mirror `IsAdminLevel`. Both rule sets are mirrored literal for literal and a test
parses the C# to keep them in step.

### Evidence

- `npm test --prefix mcp`: **124 tests, 123 pass, 0 fail, 1 skipped** (the symlink-escape test skips on
  Windows without symlink privilege; it runs on Linux CI).
- `node mcp/scripts/canary.mjs`: **11/11 mutations killed** (withheld entry dropped, exclusion deleted,
  approval removed from a DELETE tool, boundary sentence removed, undeclared body field, client header
  stripped, SDK unpinned, requiresFullScope dropped from a download, officer address write gate dropped,
  field gate opened, confirmation rule dropped).
- `node mcp/scripts/live-e2e.mjs` against the throwaway API on `http://127.0.0.1:5091`: **44/44 checks**
  — browser MFA enrolment, `connect --password-stdin --totp-code` (withheld admin, full admin, read),
  origin-bound credential file, instructions sentence, tool listing per posture, client-side refusal of a
  registered office and an officer address, `officer_list` without addresses, company/period/debtor
  create, `debtor_update` with If-Match, bank account + CSV import from `--upload-root` and a refused
  `../` escape, search → fetch through the gate, `officer_delete` → 428 → ungranted spend refused → wrong
  TOTP refused → grant → identical retry succeeds → replay refused, size classification + regime + PDF
  download with a matching SHA-256 and no bytes in the result, audit log showing `McpConnector` and the
  reason, read-level listing and refusal, `connector_sessions_list`, `disconnect` and `status`.

## Decisions you may want to reverse

- **The handoff toolset, the directors' report data and the section 307 note need full scope.** Handoff
  documents nest officer/shareholder addresses, identity-evidence references and presenter emails in free
  `value` fields; the other two are prose that interpolates the registered office and counterparties. An
  allowlist cannot filter them, so they fail closed.
- **`company_update`, `officer_update`, `director_loan_update` need full scope.** These PUTs replace the
  whole record; a Withheld agent cannot see the withheld fields, so its update would erase them (the
  server would also refuse the fields if it sent them). Better would be server-side PATCH semantics.
- **User administration and privacy tools need full scope**, matching the server's outright refusal under
  Withheld, so they are not advertised to sessions that would be refused.
- **Registered-office writes are refused client-side under Withheld** although the server's
  `PersonalDataRouteCatalog.WithheldBodyFields` does not cover `CompanyInput` (only officers, onboarding
  addresses, director loans and related parties). The owner's list withholds the registered office on
  read, so the connector withholds it on write; consider adding it server-side.
- **`user_create` echoes the temporary password** the agent supplied into `credentialHandoff`; it is not
  generated by the connector, because an approval is bound to the exact body and a generated password
  would differ on the retry. `user_invite` is treated as a credential tool too (it returns an invitation
  token).
- **Connector management tools** (`connector_approvals_pending`, `connector_approval_get`,
  `connector_sessions_list`, `connector_session_revoke`, `connector_activity_list`) were added at the
  coordinator's request; none can grant anything. `connector_session_revoke` is write level and needs no
  approval (the server exempts `/api/auth/connector/*`).

## Phase 2: connector HTTP service (workstream B)

- `src/http-server.ts`: `serve --http` (`--host`, `--port`; env `FILINGBRIDGE_API_URL`,
  `FILINGBRIDGE_API_KEY_FILE`, `FILINGBRIDGE_PUBLIC_URL`, `FILINGBRIDGE_ALLOWED_HOSTS`). Node `http` server,
  SDK `WebStandardStreamableHTTPServerTransport` stateless (`sessionIdGenerator` left undefined) with JSON
  responses (the service writes the answer itself, so it can still turn it into a 401),
  one `createServer()` per request around a `StaticBearer`. Bearer validated by
  `GET /api/auth/connector/session` (posture cached ≤ 60 s per SHA-256 of the token and never past the
  token's own `expiresAtUnixSeconds`, evicted the moment the API answers 401). A token past its expiry, or
  whose payload expiry cannot be read (`access-token.ts`), is 401 `invalid_token` before any API call; an
  API 401 during a tools/call answers the whole HTTP request with the 401 challenge instead of a tool
  error, so the client refreshes. Period ETags are kept per caller (`CallerETagCaches`: SHA-256 of the
  bearer, ≤ 1000 callers, ≤ token expiry and ≤ 15 min, dropped on a 401) because the API treats `If-Match`
  as optional: without it a later write would overwrite another person's edit. Never shared between
  bearers; a refreshed token starts empty (costs a re-read, never a wrong version). PR #88 review
  (Greptile). 401 / `invalid_token` challenges per the phase 2 contract; 403 for a Host not
  allowed (checked by the service itself, with the SDK's deprecated host check kept as a second line);
  413 over 5 MiB; GET/DELETE `/mcp` 405 after authentication (stateless: no stream, no session).
- Protected-resource metadata: fetched from `GET /api/oauth/metadata/protected-resource` with the
  service key, cached 5 min; if the API does not answer, a document built from `FILINGBRIDGE_PUBLIC_URL`
  stands in for 30 s.
- File mode `inline` (`files.ts`, `tools.ts`, `tool-body.ts`, `results.ts`): downloads return an
  `InlineFile` that becomes a base64 embedded resource (≤ 1 MiB, else `DOWNLOAD_TOO_LARGE`); the CSV
  import takes `contentBase64` + `fileName`; `fileBytes` evidence fields take base64. Schemas and
  descriptions are rewritten for the mode. `privacy_access_export` is `localOnly` (never offered over
  HTTP: the export must never enter a conversation).
- `@napi-rs/keyring` is now loaded lazily (`createRequire`) so the HTTP image never loads the native
  binding.
- `ApiClient` takes `serviceHeaders` (the service key); `ServerOptions` gained `publicUrl` (links and
  `session_info` name the public origin, not `http://api:8080`), `serviceHeaders` and `fileMode`.
- Route coverage: the seven `/api/oauth/*` operations are exclusions ("OAuth authorization-server
  plumbing for remote clients; never a model tool").
- Tests: `http-transport-auth`, `oauth-metadata`, `http-file-mode` (18 tests) — real Node HTTP server,
  stubbed API, a real SDK `StreamableHTTPClientTransport` for initialize/tools/list/tools/call. Canaries
  `resource-metadata-missing` and `service-key-dropped` added.
- Infrastructure: `Dockerfile.mcp` (node:24-alpine digest, three stages, npm/yarn/corepack stripped,
  uid 1002, port 3100); compose `mcp` service in `compose.production.yml` (frontend_api only, no ports,
  secret `accounts_mcp_api_key`; API key 1 "MCP service" Admin by hash; frontend `MCP_URL`); verifiers,
  CI image build/scan/SBOM/attestation and `scripts/mcp-live-roundtrip.mjs` in production-smoke.
- Live evidence (2026-10-12): the `accounts-mcp:dev` image against a throwaway API — OAuth token, then
  initialize, tools/list, `session_info`, `company_create`, `officer_create`, `officer_delete` → 428 with
  the web approvals link → human grant → approved delete: 11/11.

### Phase 2 open items

- **Private Server is deliberately not wired in this phase.** The first draft added the `mcp` service to
  `compose.private.yml`, but the Private Server release chain (release builder, container supply-chain
  report and verifier, `verify-private-server-release.ps1`, the evidence pack and the release workflow)
  only knows the backend and frontend images, so a release built from it would be refused at setup, and
  the launcher's lifecycle commands only start `db`, `api` and `frontend`. Those edits were reverted;
  phase 3 adds the third image to the release evidence, the launcher lifecycle, the MCP service key and
  a connector enable command together. Until then a Private Server uses the stdio connector.
- A full OAuth round trip (register → authorize with password + TOTP → token → `/mcp`) is not in CI; see
  the comment in `scripts/mcp-live-roundtrip.mjs`.

## Known gaps

- **Actor names outside the owner's list are not gated**: period `lockedBy`/`reopenedBy`, ARD history
  `recordedByDisplayName`, charity `governanceReviewedBy`/`trusteeReviewedBy`, handoff/outcome
  `recordedBy`, adjustment descriptions that interpolate a director-loan counterparty ("Director loan
  reclassification — <name>"), and free-text notes. Debtor/creditor/lender names are company records and
  pass. Extend `field-policy.ts` (and `MUST_BE_GATED`) if the owner wants these withheld.
- **SDK pin moved from 1.30.0 to 1.32.1** (2026-10-12) to clear GHSA-6qxp-vccf-f47h in the SDK's OAuth
  *client*. The package never used that client; `npm audit` now reports 0 vulnerabilities.
- **Node**: `engines` says 24; the suite was run on Node 22.23 locally (npm warns, nothing fails).
- **Tools whose evidence is a file** (`historic_anchor_record`, `size_class_override`,
  `directors_approval_record`, `cro_signing_pack_record`, `auditor_report_record`, `handoff_authority_record`,
  optional on others) need `--upload-root`; they are listed regardless and say so when called without it.
- **Bodies with nullable C# value types** are sent as given; omitting a non-nullable field lets the server
  default it (false/0). Update tools say "replaces the whole record".
- The MCPB bundle and Private Server VM acceptance are phase 3.
- CI wiring, `verify-no-direct-filing-submission.ps1` scanning `mcp/`, and the non-`mcp/` docs are Task 11
  items outside this package and were not touched.

## Traps

- Never `npm install --prefix mcp` from the root; use `npm ci --prefix mcp`, or `cd mcp && npm install`.
- After the contract changes: `npm run generate:openapi-types --prefix mcp` (a test fails until you do),
  then route-coverage and body-contract tests tell you which tools to add or exclude.
- The C# confirmation/admin lists are parsed from `Services/PrivilegedRouteCatalog.cs`; if that method is
  restructured the parser test says so before any approval marking can drift.
- A canary that cannot apply its mutation reports `CANARY BROKEN`; update its `find` text rather than
  deleting it.
- `live-e2e.mjs` needs an Owner without an authenticator (it enrols one); recreate the throwaway API
  between runs.
