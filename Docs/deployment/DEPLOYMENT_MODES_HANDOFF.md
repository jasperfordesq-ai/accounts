# Deployment Modes Workstream Handoff

> **Status: Private Server implemented as a Windows x64 operational preview; live acceptance is
> incomplete.**
>
> This document records the implemented three-mode contract, current verification boundary, and
> remaining work. Do not interpret implementation or an automated pass as statutory acceptance,
> live Tailscale certification, clean-machine proof, or production filing readiness.

Last updated: 11 July 2026.

## Canonical deployment documents

- [Choose a deployment mode](README.md)
- [Development setup](../../LOCAL_SETUP.md)
- [Private Server operator guide](private-server.md)
- [Public Production entry guide](public-production.md)
- [Public Production operations runbook](../operations/production-runbook.md)

## Purpose and non-negotiable boundary

FilingBridge supports three deliberately separate operator models:

1. **Development** for contributors changing code on localhost.
2. **Private Server** for a person or small organisation running a compiled installation on a
   trusted Windows x64 computer, with optional access for selected users through Tailscale Serve.
3. **Public Production** for an internet-reachable service behind a reviewed HTTPS ingress and the
   full public operations/evidence contract.

Private Server and Public Production run production builds. Private Server is not Development with
different credentials, and it is not Public Production with arbitrary checks disabled. The exact
deployment marker selects a narrow policy.

This workstream does not change the product's statutory state:

- direct CRO/ROS submission remains unsupported;
- generated outputs remain review artifacts until the central release gates pass;
- the independent audit baseline remains **600/1,000**;
- real filing reliance still requires the named qualified-accountant, source-law, visual,
  external ROS/iXBRL, manual-handoff, monitoring-provider, and operations evidence required by
  `Docs/PLATFORM_AUDIT_2026-07-10.md`; and
- a successful local installation must never be presented as legal, accounting, CRO, or Revenue
  acceptance.

## Supported-mode contract

| Property | Development | Private Server | Public Production |
| --- | --- | --- | --- |
| Marker | `Development` | `PrivateServer` | `PublicProduction` |
| Primary audience | Contributors | Selected users in one small organisation | Internet-facing service operator |
| Runtime | Development or compiled | Compiled production images | Exact promoted production images |
| Source mounts/watchers | Allowed | Forbidden | Forbidden |
| Exposure | Contributor localhost only | IPv4 loopback; optional private tailnet HTTPS | Approved public HTTPS ingress |
| Frontend host port | Development convenience | `127.0.0.1:<configured-port>` only | Loopback or trusted private ingress network |
| API/database host ports | Allowed for development | Forbidden | Forbidden |
| Demo credentials/data | Allowed and labelled | Forbidden | Forbidden |
| Cookies | Development policy | Secure session/CSRF contract retained | Secure session/CSRF contract retained |
| User login | Workspace slug + email + password | Workspace slug + email + password | Workspace slug + email + password |
| External providers | Optional | Disabled/local by default | Required by public contract |
| PostgreSQL transport | Development policy | Plaintext only on isolated same-host bridge | Certificate-verified TLS |
| RLS/database roles | Not a release boundary | Forced RLS, signed context, separate roles | Forced RLS, signed context, separate roles |
| Backup | Disposable data acceptable | Verified encrypted recovery set by default; explicit incomplete plaintext option | Encrypted off-host evidence and named drills |
| Direct filing | Unsupported | Unsupported | Unsupported |

`compose.yml` is Development only. It contains known credentials, automatic migration/seed
allowances, sample state, and published API/database ports. Never expose it through Tailscale, a
LAN, router forwarding, or public ingress.

## Implemented Private Server architecture

```text
local browser                         selected tailnet browser
      |                                      |
      | http://localhost:3500                | private HTTPS :443
      |                                      v
      |                               Tailscale Serve
      |                                      |
      +-------------------+------------------+
                          |
                  127.0.0.1:3500
                          |
              compiled Next.js :3000
                          |
               internal proxy/API key
                          |
                  Kestrel API :8080
                          |
      internal PostgreSQL :5432 + named volume
```

The frontend is the only service with a host port, and Compose binds it to IPv4 loopback. Kestrel
and PostgreSQL have no host ports. Next.js injects the service key server-side and is the only
browser ingress for `/api/*`.

The API/database and frontend/API networks are `internal:true`. Docker Desktop suppresses a
published port for a container attached only to internal networks, so the frontend also joins a
dedicated ingress bridge. API and PostgreSQL never join that bridge. The bridge configuration is
not claimed as a hard outbound firewall for a compromised frontend container on Docker Desktop;
operators needing that stronger boundary must apply and test host/VM egress controls.

Tailscale runs on the Windows host and, when explicitly enabled, serves the loopback frontend. The
repository helper uses Tailscale **Serve**, never Funnel. It does not add router forwarding, a public
Windows firewall rule, or a public reverse proxy. Caddy, IIS, Apache, and Nginx are outside the
Private Server path.

## Implemented mode and security contract

Private Server runs with:

```text
ASPNETCORE_ENVIRONMENT=Production
NODE_ENV=production
Deployment__Mode=PrivateServer
```

The backend mode contract retains production error handling and validates a file-backed
installation identity. It permits only the bounded private exceptions required by this topology:

- local structured JSON logs instead of a remote error-tracking provider;
- deadline delivery disabled with no provider endpoint/token;
- breached-password remote lookup disabled while the local deny-list remains; and
- `sslmode=disable` only for same-host PostgreSQL on the unexposed internal bridge.

It retains generated file-backed secrets, secure cookies, CSRF, service API-key protection,
authentication, authorisation, tenant isolation, forced PostgreSQL RLS, signed connection context,
separate migration/runtime database roles, audit integrity, no startup migration, no demo seed, and
no direct filing client or endpoint.

User identity is tenant-qualified. Email is unique within a tenant rather than globally, and login
requires `workspace slug + email + password`. The database bootstrap function resolves only that
slug/email pair. Generic login errors and keyed rejection telemetry remain in place.

## Implemented repository surface

| File or area | Purpose |
| --- | --- |
| `compose.private.yml` | Compiled private topology, one-shot role/migration/initialisation jobs, health checks, exact image inputs, named data volume |
| `.env.private.example` | Names/placeholders only; generated secrets live outside the checkout |
| `FilingBridge.cmd` | Discoverable Windows launcher |
| `scripts/private-server.ps1` | Command dispatcher |
| `scripts/PrivateServer/PrivateServer.psm1` | Setup, lifecycle, Tailscale, recovery, backup/update, diagnostics, uninstall/purge implementation |
| `scripts/verify-private-compose.ps1` | Static private topology/mode invariant verifier |
| `scripts/test-private-compose.ps1` | Static regression plus loopback publication/network behaviour test |
| `scripts/build-private-server-release.ps1` | Builds a versioned Windows bundle from a clean exact candidate and retained supply-chain evidence |
| `scripts/verify-private-server-release.ps1` | ZIP safety, checksum, exact inventory, manifest, image, statutory-boundary, and file-hash verifier |
| `.github/workflows/private-server-release.yml` | Manual two-stage candidate preparation and protected draft-release publication |
| `deploy/private/release-manifest.schema.json` | Release manifest contract |
| `Docs/deployment/private-server.md` | Authoritative Windows operator guide |
| `Docs/deployment/public-production.md` | Proxy-neutral public entry guide |
| `deploy/{caddy,apache,nginx}/` | Optional Public Production ingress examples |

The Public Production examples are not Private Server dependencies.

## Operator contract

The normal private operator starts from a verified versioned release bundle:

```powershell
.\FilingBridge.cmd setup
.\FilingBridge.cmd start
.\FilingBridge.cmd status
.\FilingBridge.cmd logs
.\FilingBridge.cmd stop
```

Setup is local-only by default. It checks the host and release manifest, creates generated state
under `%LOCALAPPDATA%\FilingBridge\server`, restricts ACLs, pulls exact images, starts PostgreSQL,
provisions the least-privileged application role, runs the migrate-only job, executes the
empty-database private initializer once, starts the runtime, and displays the workspace slug,
Owner email, and one-time Owner password after health succeeds. It refuses to overwrite an existing
installation.

Additional commands are:

```powershell
.\FilingBridge.cmd tailscale enable | disable | status
.\FilingBridge.cmd backup -BackupRecipient <age-recipient> -OutputDirectory <off-repo-directory>
.\FilingBridge.cmd verify-backup -BackupPath <path> -AgeIdentityFile <identity>
.\FilingBridge.cmd restore -BackupPath <path> -AgeIdentityFile <identity>
.\FilingBridge.cmd update -ReleaseManifest <verified-release.json> `
  -BackupRecipient <age-recipient> -AgeIdentityFile <identity> -OutputDirectory <off-repo-directory>
.\FilingBridge.cmd owner-recovery
.\FilingBridge.cmd diagnose
.\FilingBridge.cmd support-bundle
.\FilingBridge.cmd uninstall
.\FilingBridge.cmd purge-data
```

`stop` and `uninstall` preserve data. `purge-data` is separate and confirmation-bound. Do not use
`docker compose down -v` for troubleshooting.

## Backup, restore, and update boundary

The default backup is an age-encrypted recovery set containing a verified PostgreSQL custom dump
and the recovery-critical configuration/key companion. Creation quiesces writers, performs a
disposable restore, and records schema, EF migration-history, and selected important-table
row-count/fingerprint evidence. Dumps are streamed to ACL-restricted host staging rather than the
database's bounded tmpfs. A per-installation HMAC authenticates the exact dump/ciphertext and
immutable envelope before any `pg_restore`; age encryption alone is not treated as sender
authentication. The vetted `age` command and a recipient are required for that complete encrypted
set. Complete payloads currently have a 1.9 GB `Compress-Archive` ceiling. A plaintext database-only
dump is available only through an explicit acknowledgement and is not a complete host-loss set.

Restore is implemented only for the same installation. It verifies the envelope/inventory and a
disposable database restore, preserves the current database, switches to a candidate, reapplies
roles and forward migrations, rotates the session key, and health-checks the loopback app. It does
not prove all-table/all-row equivalence, retained artifact byte continuity, or sample PDF/iXBRL
output.

**Replacement-host/bare-metal restore is not implemented.** The companion is retained for future
key continuity, but no clean-host bootstrap command or drill currently establishes host-loss
recovery.

Update requires a verified pre-update backup, the separately supplied age identity, exact target
manifest/images, a strictly forward semantic release identity, controlled migration, and loopback
health. It rejects downgrades and version reuse before migration. It does not automate an
authenticated login or sample statutory output. An old image cannot reverse a committed database
migration; failed update recovery remains an explicit operator decision.

## Release and trust boundary

Normal users should obtain the versioned ZIP and checksum from GitHub Releases and verify the
immutable release plus exact asset attestation with GitHub CLI before extraction. The checksum
detects transfer corruption. The manifest inside the ZIP proves extracted-file and image identity
only after the release/asset trust check; it is not an independent out-of-band trust anchor.

The release workflow accepts an exact successful `main` run of the canonical CI workflow, exact
application image evidence, and an exact PostgreSQL digest. Its preparation job has read-only
repository permissions. A separate protected-environment job re-resolves the CI run, checks out the
exact candidate SHA without persisted credentials, byte-compares every source payload with that Git
object, independently binds the retained evidence inventory, and validates the downloaded bundle
before obtaining the write/attestation permissions used to create a **draft** release. No Private
Server release has been made generally available merely because this workflow exists.

## Current verification state

Automated coverage now exists for:

- backend Private Server mode safety, file-backed installation identity, private initialization,
  Owner recovery, provider-free constraints, and tenant-qualified PostgreSQL identity resolution;
- frontend workspace-slug login, proxy/header stripping, token fragments, mode-aware CSP and
  rendered identity flows;
- resolved private Compose invariants, exact mode values, secret-file use, lack of API/database
  ports, and loopback-only frontend publication;
- Windows operator path/ACL/state/secret handling, lifecycle command dispatch, backup/restore
  safety, installation-wide lifecycle locking, authenticated-backup tamper rejection, Tailscale
  Serve ownership checks, and destructive confirmation;
- release bundle construction/verification, Windows PowerShell compatibility, malicious archive
  rejection, dirty-source rejection, evidence binding, and CI action policy; and
- preservation of the Public Production Compose contract and proxy-neutral ingress examples.

Focused backend, frontend, operator, release, and Compose checks have passed during implementation.
The frozen integrated frontend gate passed 316 unit tests (one intentional skip), 239 render tests
across 71 files, lint, typecheck, a production build, and generated-contract verification for 177
paths and 183 schemas. The final post-correction backend candidate passed 1,072/1,072 tests with the
PostgreSQL and golden-corpus gates enabled. Private Server operator, release bundle, Compose
mutation/live-loopback, protected release-workflow, CI action-policy, whitespace, and adversarial
publication checks also passed. Green candidate-bound CI evidence is still required before any
release claim.

On 11 July 2026 a disposable current-host drill on Windows 11 Pro x64, Windows PowerShell 5.1, and
Docker Desktop 29.2.1 passed source-build setup, loopback health, stop/start, plaintext database-only
backup and independent verification, same-installation restore, source-build update, physical-host
Owner recovery and reset completion, privileged-MFA enforcement, 25 diagnostics, redacted support
bundle inspection, destructive purge, and exact resource cleanup. The drill found and drove fixes
for benign native stderr/Windows argv handling, restart of existing containers without traversing
removed one-shot dependencies, and the canonical `UserPasswordResetCompleted` host-audit database
constraint. No Tailscale route or public listener was enabled.

The following are **not** proven and must remain open:

- clean Windows x64 machine setup from a real published bundle;
- complete Owner MFA enrolment/authenticated-session journey followed by two separate non-Owner
  role journeys (the current-host drill proved recovery reset and that privileged MFA stayed
  enforced, not the full journey);
- live Tailscale Serve HTTPS from a second device and least-privilege tailnet policy;
- Windows sign-in, Docker Desktop, application, and Serve recovery after reboot;
- full routine workflow without ordinary internet connectivity;
- encrypted complete-recovery-set backup/same-installation restore with artifact-level business
  checks (only the explicitly incomplete plaintext database-only path was drilled);
- update from a real prior version plus forced migration/health failure recovery;
- replacement-host/bare-metal restore; and
- unchanged exact-candidate Public Production and statutory release evidence.

## Acceptance checklist

- [x] Explicit `Development` / `PrivateServer` / `PublicProduction` configuration contract exists.
- [x] `compose.private.yml` contains compiled services, one-shot jobs, named data, and no
      API/database host ports.
- [x] Private setup/lifecycle, diagnostic, backup, restore, update, Owner recovery, Tailscale,
      uninstall, and purge commands exist.
- [x] Private and public guides, mode chooser, proxy-neutral runbook, and optional Caddy/Apache/Nginx
      public examples exist.
- [x] Private Compose, operator, release, frontend, backend, and workflow regression checks are
      wired into repository/CI gates.
- [x] A disposable current Windows host completed source setup, stop/start, plaintext database-only
      backup/verify/restore, source update, Owner recovery/reset, diagnostics/support, purge, and
      exact cleanup without public exposure.
- [x] Exact local post-correction backend/frontend/repository gates passed with executed counts.
- [ ] Green candidate-bound CI evidence retained after publication.
- [ ] Clean Windows x64 release-bundle setup reaches tenant-qualified first login without YAML edits.
- [ ] Rerunning setup refuses to replace an existing database or Owner credentials on a real host.
- [ ] Two non-Owner users complete live role-appropriate journeys.
- [ ] Live loopback and second-device Tailscale Serve HTTPS checks pass without public exposure.
- [ ] Windows reboot and Docker/Tailscale restart preserve service/data (current-host stop/start and
      source-build update passed; reboot and genuine release update remain open).
- [ ] Real encrypted backup and same-installation restore drill passes with retained evidence.
- [ ] Prior-version update and forced-failure recovery drill passes.
- [ ] Offline routine workflow is exercised and documented honestly.
- [ ] Replacement-host recovery is implemented and clean-host drilled before it is claimed.
- [ ] Public Production exact-candidate validation remains green.
- [ ] All audit human/external gates remain blocking until genuine evidence is supplied.

## Continuation instructions

1. Read root `AGENTS.md`, `CLAUDE.md`, this handoff, and `private-server.md` completely.
2. Inspect the worktree and preserve unrelated/user changes.
3. Run the exact final backend test project with PostgreSQL/golden-corpus gates, the full frontend
   gate, all Private Server script tests, Compose verification, release/workflow verification, and
   CI policy verification. Record explicit counts and exact candidate identity.
4. Fix failures without weakening the mode, tenant, statutory, or evidence boundaries.
5. Use a clean Windows x64 VM or equivalent for the remaining live acceptance matrix. Do not infer
   Tailscale, reboot, offline, update, backup, or recovery success from mocked/static tests.
6. Do not expose `compose.yml`, enable Funnel, publish Kestrel/PostgreSQL, switch Private Server to
   Development, or add a private reverse proxy.
7. Do not claim replacement-host restore until a supported bootstrap path and clean-host drill
   exist.
8. Keep `Docs/PLATFORM_AUDIT_2026-07-10.md` authoritative. Private deployment progress does not
   restore the 600/1,000 baseline or satisfy named professional/external acceptance.
