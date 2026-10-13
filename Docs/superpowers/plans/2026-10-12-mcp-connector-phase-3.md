# MCP Connector Phase 3 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Bring the connector to the Private Server (remote MCP service, release evidence, launcher lifecycle, enable command) and to Claude Desktop (MCPB bundle), then accept it on the live Hyper-V Private Server after a reviewed pre-release.

**Spec:** `Docs/superpowers/specs/2026-10-12-mcp-connector-design.md` (Phase 3). Builds on PR #87 (phase 1) and #88 (phase 2).

## Global Constraints

- Private Server never gets public ingress or Tailscale Funnel; the MCP service is reached only through the frontend (`127.0.0.1:<port>/mcp` or Tailscale Serve HTTPS).
- The connector stays **off by default**. `filingbridge connector enable` is the only supported way to turn it on.
- Releases stay forward-only and evidence-bound: every image a release runs is digest-pinned in `release.json`, scanned, SBOM'd and attested, and verified before setup/update.
- Existing installations (the live VM runs `0.1.0-preview.9`) must update to the new release without manual secret work: new secrets are generated on the first environment write.
- No CRO/ROS submission tool; no weakening of RLS, auth, CSRF, audit or recovery.
- LF line endings in edited files.

## Binding contracts

### Release manifest
- `release.json` gains required `images.mcp` = `ghcr.io/jasperfordesq-ai/accounts-mcp@sha256:<64 hex>` for any release whose `compose.private.yml` references `ACCOUNTS_MCP_IMAGE`. `deploy/private/release-manifest.schema.json` documents it. A release without it is refused at setup/update with a clear message.
- The container supply-chain report covers three images (backend, frontend, mcp): digest, Trivy result, SBOM, attestation.

### Compose (`compose.private.yml`)
- Service `mcp`: hardened exactly like `frontend`, networks `[frontend_api]` only, no ports, secret `accounts_mcp_api_key`, env `FILINGBRIDGE_API_URL=http://api:8080`, `FILINGBRIDGE_API_KEY_FILE=/run/secrets/accounts_mcp_api_key`, `FILINGBRIDGE_PUBLIC_URL=${PRIVATE_CONNECTOR_PUBLIC_ORIGIN}`, `FILINGBRIDGE_ALLOWED_HOSTS=mcp:3100,<public host>`, healthcheck `/healthz`.
- API: `ApiAccess__Keys__1__{Name: MCP service, Role: Admin, KeyHash_FILE: /run/secrets/accounts_mcp_api_key_hash}`, `Connector__PublicBaseUrl: ${PRIVATE_CONNECTOR_PUBLIC_ORIGIN}`.
- Frontend: `MCP_URL: http://mcp:3100/mcp`.

### Launcher
- Lifecycle commands (setup, start, stop, status, update, restore, recover-host, reboot-check, local-check, diagnose) treat `mcp` like `frontend`.
- New command `filingbridge connector enable [--origin local|tailscale]`, `connector disable`, `connector status`.
  - `enable` sets `state.connectorEnabled=true` and `state.connectorPublicOrigin` (`local` → `PRIVATE_LOCAL_ORIGIN`; `tailscale` → the configured Tailscale HTTPS origin, refused if Tailscale Serve is not configured), rewrites the environment, recreates `api` and `mcp`, waits for health, prints the MCP URL plus the Claude Code / Codex commands.
  - `disable` sets it false and recreates `api` (agents are refused immediately; existing sessions remain revocable from `/settings/agents`).
  - `status` prints enabled/origin/MCP URL/health.
- `accounts_mcp_api_key` + `accounts_mcp_api_key_hash` generated at setup and on the first environment write for existing installs; not recovery-critical; regenerated on restore/recover-host.

### Claude Desktop bundle
- `mcp/manifest.json` (MCPB manifest 0.2): server type node, entry `dist/cli.js`, args `serve --profile ${user_config.profile} --base-url ${user_config.base_url}`, `user_config` for `base_url` (string, required), `profile` (`private`/`default`/`local`), `download_dir` and `upload_root` (directories, optional). Connecting stays a terminal step (`connect`), documented in the bundle's long description.
- `npm run pack:mcpb --prefix mcp` builds `filingbridge.mcpb` with `npx @anthropic-ai/mcpb@<pinned> pack`; CI validates the manifest with `npx @anthropic-ai/mcpb@<pinned> validate mcp/manifest.json`; a packaging test asserts manifest/package/version agreement.

## Tasks

### P1 Private Server (agent)
- [ ] Compose, verifiers, launcher lifecycle, MCP key, `connector` command, release builder, supply-chain report writer/verifier/tests, `verify-private-server-release.ps1`, evidence pack, release workflow + policy verifier, Test-PrivateServer/test-private-compose fixtures, `Docs/deployment/private-server.md` + `private-server-linux.md`.

### D1 Desktop bundle (agent)
- [ ] Manifest, pack script, CI validation step, packaging test, README section, THIRD_PARTY_NOTICES if needed.

### V1 VM acceptance (needs #87/#88/#89 merged and an explicit go-ahead)
- [ ] Green CI on `main` → dispatch `private-server-release.yml` → publish pre-release → verify attestation and checksums → `filingbridge update` on the VM → `filingbridge connector enable` → Claude Code over Tailscale/loopback tunnel: OAuth sign-in, `session_info`, a read, a write, an approval through `/settings/approvals`, disconnect → record evidence in `AGENTS.md` and `mcp/HANDOVER.md`.
- [ ] Acceptance checklist: `Docs/operations/mcp-connector-acceptance.md`.
