# MCP connector — Private Server VM acceptance checklist

Phase 3, task V1 of `Docs/superpowers/plans/2026-10-12-mcp-connector-phase-3.md`. This checklist
turns the plan's acceptance step into an ordered, evidence-producing procedure for the live
Private Server VM (currently `0.1.0-preview.9`). Code, mocks and CI do not count as this
acceptance; only the steps below, run against the published pre-release on the VM, do.

Run it only when **all** of these hold:

- [ ] PRs #87 (phase 1), #88 (phase 2) and #89 (phase 3) are merged to `main`.
- [ ] The owner has given an explicit go-ahead for this acceptance run (date and words recorded).
- [ ] A test workspace/company/period exists on the VM that may receive agent writes. Never point an
      agent with `Full` data scope at real client data during acceptance.

Record every command, its output (redact secrets, tokens, TOTP codes, passwords) and timestamps in
the evidence log used for steps 9 and 10.

## 1. Candidate CI on `main`

- [ ] The CI run for the merge commit on `main` is green (all jobs, including `Production Smoke`
      and the evidence pack). Record the run URL and commit SHA.
- [ ] The run's `container-supply-chain` artifact contains `container-supply-chain-report.json` with
      exactly three images — `backend`, `frontend`, `mcp` — each `promotionMode: promoted`, a zero
      HIGH/CRITICAL Trivy scan, an SPDX SBOM and a provenance bundle (`mcp-trivy.json`,
      `mcp-sbom.spdx.json`, `mcp-provenance.jsonl` are present), and
      `container-supply-chain-verification-report.json` with `status: passed`, three
      `verifiedImageDigests` and nine `retainedEvidenceFiles`.
- [ ] The run's `mcp-connector-evidence` artifact contains a passing `mcp-live-roundtrip-report.json`.

## 2. Prepare the release

- [ ] Dispatch `Prepare Private Server Release` (`.github/workflows/private-server-release.yml`) with:
      `version` = the next pre-release (for example `0.1.0-preview.10`), `candidate_run_id` = the
      run from step 1, `postgres_image` = the reviewed `postgres@sha256:` digest (the same one
      preview.9 pinned unless a reviewed change is intended).
- [ ] Both jobs pass. The prepare job pulls the exact backend, frontend **and MCP connector** digests
      anonymously for `linux/amd64`; the protected job re-verifies the evidence and binds
      `images.mcp` to it.
- [ ] A **draft** release `private-server-v<version>` exists with the ZIP and its `.sha256`.

## 3. Verify the draft before publishing

On the operator workstation (not the VM):

```powershell
gh release download private-server-v<version> --repo jasperfordesq-ai/accounts --dir .\candidate
cd .\candidate
Get-FileHash .\FilingBridge-PrivateServer-<version>.zip -Algorithm SHA256   # compare with the .sha256 line
gh attestation verify .\FilingBridge-PrivateServer-<version>.zip --repo jasperfordesq-ai/accounts
pwsh -File <repo>\scripts\verify-private-server-release.ps1 -ArchivePath .\FilingBridge-PrivateServer-<version>.zip `
  -ExpectedVersion <version> -ExpectedCommitSha <sha> -ExpectedGitHubActionsRunUrl <run-url>
```

- [ ] Checksum matches; `gh attestation verify` passes for the ZIP; the verifier prints
      `"status": "passed"`.
- [ ] `release.json` (inside the ZIP) has `images.mcp.exactDigestReference` =
      `ghcr.io/jasperfordesq-ai/accounts-mcp@sha256:<digest>` equal to the CI report's `mcp` digest.
- [ ] `gh attestation verify oci://ghcr.io/jasperfordesq-ai/accounts-mcp@sha256:<digest> --repo jasperfordesq-ai/accounts`
      passes (likewise for the API and frontend digests).
- [ ] Publish the draft as a **pre-release** (human action in GitHub; keep the notes' statutory and
      no-direct-submission boundary text).

## 4. Update the VM

On the VM, as the operator account. Keep the preview.9 release directory; extract the new ZIP into
a **new** directory.

- [ ] From the preview.9 directory, record `FilingBridge.cmd status` and `FilingBridge.cmd diagnose`
      (baseline).
- [ ] Copy the ZIP and `.sha256` to the VM, recheck the SHA-256, extract to a new directory.
- [ ] From the **new** directory:

```powershell
.\FilingBridge.cmd update -ReleaseManifest .\release.json `
  -BackupRecipient <age-recipient> -AgeIdentityFile <path-to-age-key> `
  -OutputDirectory <backup-directory-outside-the-release>
```

- [ ] The update completes with "Update completed and passed health checks" and names the verified
      pre-update backup. No manual secret or environment edits were made.
- [ ] `FilingBridge.cmd status` shows the new release, an `MCP connector image:` line with the exact
      digest, and `MCP connector: disabled at http://localhost:<port>/mcp`.
- [ ] `FilingBridge.cmd diagnose` passes, including `secret:accounts_mcp_api_key`,
      `secret:accounts_mcp_api_key_hash`, `secret:connector_signing_key`, `mcp-service-key-binding`,
      `image:mcp` and `mcp-connector`.
- [ ] `FilingBridge.cmd local-check` passes; its report lists `db, api, frontend, mcp` running,
      `mcpHostPortPublished: false`, and the five business-data fingerprints.
- [ ] `docker ps` shows no host port for the `mcp`, `api` or `db` containers; only the frontend on
      `127.0.0.1:<port>`.
- [ ] A browser login (workspace + email + password + authenticator) still works and existing
      business data is intact.

## 5. Enable the connector

Choose the origin the agent will use. Loopback (`http://localhost:<port>`) works for a client on the
VM itself or through an SSH/Hyper-V tunnel that forwards the **same** local port; Tailscale requires
`FilingBridge.cmd tailscale enable` first. Never enable Tailscale Funnel.

```powershell
.\FilingBridge.cmd connector enable                      # loopback
.\FilingBridge.cmd connector enable --origin tailscale   # or Tailscale Serve
.\FilingBridge.cmd connector status
```

- [ ] `enable` prints the MCP URL plus the Claude Code and Codex commands; only `api` and `mcp`
      were recreated; `status` shows `MCP connector: enabled`, `MCP service: running|healthy`.
- [ ] Discovery from the client machine:
  - `curl -i <origin>/mcp` (POST or GET without a token) answers **401** with a
    `WWW-Authenticate: Bearer ... resource_metadata="<origin>/.well-known/oauth-protected-resource/mcp"`
    header;
  - `curl <origin>/.well-known/oauth-protected-resource/mcp` names `<origin>/mcp` as the resource;
  - `curl <origin>/.well-known/oauth-authorization-server` names `<origin>` as the issuer.

## 6. Claude Code round trip

```text
claude mcp add --transport http filingbridge <origin>/mcp
```

Then in Claude Code run `/mcp` → *Authenticate*.

- [ ] The browser opens `<origin>/oauth/authorize`; the consent page names the client and a loopback
      redirect host. Sign in with workspace, email, password and authenticator code; choose
      `Write` (or `Admin` for the approval step) and `Withheld` data scope.
- [ ] `session_info`: tenant, user, role, access level and data scope match the consent choices; the
      personal-data gate reports withheld.
- [ ] A read (for example `company_list`, then `period_list` on the test company) returns data with
      withheld fields stripped.
- [ ] A write on the test company/period (for example `debtor_create`, with a reason) succeeds; the
      company audit log shows `client.kind = McpConnector`, the session family and the reason.
- [ ] An approval-required action (for example `debtor_delete` on the debtor just created) returns
      `APPROVAL_REQUIRED` with an `approvalsUrl` pointing at `<origin>/settings/approvals?id=...`.
- [ ] A human opens that link in the browser, sees the server's summary, approves with password +
      authenticator code; the agent repeats the **identical** call and it succeeds; a second replay
      of the same approval is refused with a fresh 428.
- [ ] `/settings/agents` lists the connected agent with its level, scope and recent activity.

## 7. Disconnect and disable

- [ ] Revoke the agent at `/settings/agents`; the next Claude Code tool call is refused (401) until it
      authenticates again. Remove it with `claude mcp remove filingbridge`.
- [ ] `FilingBridge.cmd connector disable`; only `api` was recreated; a new `/mcp` sign-in or tool call
      is refused; `connector status` shows `disabled`.
- [ ] Re-enable only if the owner wants the connector left on after acceptance.

## 8. Not covered — do not claim

- Claude.ai custom connectors (they need public ingress; unsupported on a Private Server).
- Real reboot persistence and clean replacement-host recovery of the new release (separate gates in
  `Docs/deployment/LOCAL_WINDOWS_READINESS.md`).
- Any statutory, qualified-accountant, CRO/ROS or Public Production acceptance.

## 9. Record the evidence

- [ ] `AGENTS.md`, section *MCP connector*: date, release version and commit, CI run URL, release URL,
      the image digests, and a one-line result for each of steps 4–7.
- [ ] `mcp/HANDOVER.md`: a *Phase 3 VM acceptance* entry with the same facts, the client and its
      version, the origin used (loopback/Tailscale), and any deviation or failure with its follow-up.

## 10. If something fails

- Update failure: follow the printed instructions; the verified pre-update backup and an explicit
  `restore` from the preview.9 directory are the recovery path (a migration is never reversed by an
  older image).
- Connector enable failure: the previous connector configuration is re-applied automatically; run
  `diagnose` and `logs`, and record the failure before retrying.
- Stop and report before working around any control (RLS, auth, CSRF, audit, approvals).
