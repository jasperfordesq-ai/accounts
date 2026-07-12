# FilingBridge Private Server

> **Operational preview:** the Private Server code and operator contract are independently
> versioned from FilingBridge's statutory acceptance. Do not use generated outputs for real CRO or
> Revenue work until the live audit's named qualified-accountant and external-validation gates are
> complete.

Private Server runs a compiled FilingBridge installation on a trusted personal or small-
organisation computer. It keeps PostgreSQL data across ordinary stops, starts and upgrades and can
share the application with selected people through Tailscale Serve without publishing it to the
internet.

It does not use IIS, Caddy, Apache or Nginx. Windows is the physical host; Docker Desktop runs the
compiled Next.js, ASP.NET Core and PostgreSQL Linux containers. Tailscale, when enabled, runs on the
Windows host.

## Architecture

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
            internal API-key proxy
                          |
                  Kestrel API :8080
                          |
       internal PostgreSQL :5432 + named volume
```

Only the frontend has a host port and it is bound to IPv4 loopback. Kestrel and PostgreSQL are
never published. All `/api` requests pass through Next.js so the browser never receives the
frontend service key.

Docker Desktop does not publish a host port when a container is attached only to `internal:true`
networks, so Next.js also joins a dedicated frontend-only bridge for loopback ingress. The API and
database never join that bridge and remain without host ports or Internet egress. Docker Desktop's
outer VM routing means the bridge option is not claimed as a hard outbound firewall for a
compromised frontend container; the supported profile instead uses exact images and disables its
external provider features. Use an additional tested host/VM egress policy if that stronger threat
boundary is required.

## Supported host and prerequisites

The first release profile supports a Windows x64 host running Linux containers. ARM hosts are not
supported until native multi-architecture images and acceptance evidence exist.

Required:

- Windows 11 x64 23H2 (build 22631) or newer on a currently serviced edition; Windows Server and
  Windows 10 are not certified for this preview;
- hardware virtualisation enabled in BIOS/UEFI and WSL 2.1.5 or newer;
- Docker Desktop or a compatible Docker Engine using Linux containers;
- Docker Compose 2.20.0 or later;
- at least 8 GB RAM, with 16 GB recommended;
- at least 20 GB free disk before installation, plus space for financial data and backups;
- a Windows account permitted to use Docker; and
- Tailscale only when private access from another device is required; and
- the vetted `age` command plus a separately retained age identity/recipient when complete
  encrypted recovery sets are required. `age` is not needed for setup or routine start;
- a separately exported FilingBridge recovery-authentication key, stored apart from both the
  encrypted backup and age identity, before replacement-host recovery can be relied on; and
- a complete recovery payload below the current 1.9 GB `Compress-Archive` safety ceiling. Larger
  databases can still produce authenticated database-only dumps, but this preview has no complete
  host-loss set above that ceiling.

The setup checker also rejects an unsupported CPU architecture, unavailable Docker engine,
Windows-container mode, an occupied frontend port, conflicting FilingBridge projects, insufficient
resources, and unsafe or ambiguous installation state. Paths containing spaces and non-ASCII
characters are supported and tested; do not relocate files to a hard-coded web-server directory.

## Obtain and verify a release

For normal installation, download the versioned `FilingBridge-PrivateServer-<version>.zip` and its
checksum from this repository's GitHub Releases page. **Verify the ZIP before extracting or running
anything from it.** With a current GitHub CLI, require both the immutable Release and the exact
asset attestation to verify:

```powershell
$tag = "private-server-v<version>"
$zip = ".\FilingBridge-PrivateServer-<version>.zip"
gh release verify $tag --repo jasperfordesq-ai/accounts
gh release verify-asset $tag $zip --repo jasperfordesq-ai/accounts
```

Also compare `Get-FileHash -Algorithm SHA256 $zip` with the downloaded `.sha256` sidecar to detect
an incomplete or accidental transfer. Do not proceed if any check fails. The manifest inside the
ZIP checks extracted-file integrity and exact image identities, but it is not an out-of-band trust
anchor by itself.

After verification, extract the ZIP to a new directory. The bundle
contains the launcher, Compose contract, scripts, release manifest, licence/NOTICE files and this
guide. Application images are pulled by immutable digest on first setup.

The generated source archives on GitHub are not the Windows installer. Contributors may instead
clone the repository and use the explicitly documented source-build path, but routine users do not
need Git, Node.js, the .NET SDK or host OpenSSL.

The first image download is network-heavy. Daily startup does not rebuild images, reinstall
dependencies, compile pages, migrate the database or seed data.

## One-time setup

Open PowerShell in the extracted release directory and run:

```powershell
.\FilingBridge.cmd setup
```

Setup prompts for:

- organisation/workspace name;
- Owner display name and email.

The workspace slug is generated from the organisation name. Supply `-TenantSlug <slug>` during
setup if that generated value is unsuitable. Save the printed workspace slug with the Owner email
and password: login requires all three values.

Setup is local-only by default and uses port `3500`; select another free port with `-Port`. Enable
Tailscale later with the dedicated command after local login works.

Do not provide generated secrets on the command line. Setup:

1. verifies the host, manifest shape, extracted-file hashes and exact image repositories/digests;
2. refuses to overwrite an existing installation;
3. creates a unique installation and Compose project identity;
4. writes non-secret configuration and generated secret files below
   `%LOCALAPPDATA%\FilingBridge\server` with restrictive NTFS ACLs;
5. pulls the exact release image digests;
6. starts PostgreSQL and provisions separate migration/application roles;
7. runs migrations once through the migration container;
8. runs the empty-database private initializer once;
9. starts the compiled API and frontend;
10. waits for real loopback application readiness; and
11. prints the workspace slug and generated Owner password only after successful health checks.

Save the Owner password in a password manager. It is not retained in the environment file, release
directory, logs or normal status output. The Owner must replace it at first login. Setup creates one
tenant/workspace and one Owner, but no company, demo user, transaction, accounting period, legal
fact or filing state. Create the real charity/company in the application after signing in.

If setup commits the database initializer but fails before displaying the password, do not delete
the volume or rerun initialization. Use the physical-host `owner-recovery` command.

## Local-only use

Start FilingBridge and open the exact URL printed by `status`, normally:

```text
http://localhost:3500
```

Do not replace `localhost` with a LAN address. The Docker port listens only on `127.0.0.1`; other
devices cannot reach it directly. Private Server retains secure session and CSRF cookies and a
production CSP while allowing the exact loopback origin.

## Private access through Tailscale

Install Tailscale on the server and each selected person's device, sign in with individual
identities, and use a least-privilege tailnet policy. Then run:

```powershell
.\FilingBridge.cmd tailscale enable
.\FilingBridge.cmd tailscale status
```

The helper discovers the machine's tailnet DNS name, configures persistent Tailscale **Serve** for
the loopback frontend, records the exact HTTPS origin and verifies it. It never enables Funnel,
router port forwarding or a public Windows firewall rule.

Each person also needs a FilingBridge account. Tailnet membership does not bypass application
password, MFA, role, company-access or audit controls. FilingBridge ignores Tailscale identity
headers as an authorization source.

To stop sharing while retaining local use:

```powershell
.\FilingBridge.cmd tailscale disable
```

## Accounts and roles

The existing roles remain authoritative:

- `Owner` — installation/workspace administration;
- `Accountant` — accounting and working-paper actions;
- `Reviewer` — review, approval and filing-workflow actions; and
- `Client` — limited/read-only access.

There is no synthetic `Director` role. Choose the least-privileged existing role that matches the
person's duties. From **Settings → User administration**, an Owner can create a manual invitation,
assign companies, change roles, revoke sessions, deactivate, unlock or offboard a user.

Invitation and reset links contain a short-lived one-time token in the URL fragment. The fragment
is removed from browser history immediately and only a keyed hash is stored in PostgreSQL. Share the
link through a private channel and never paste it into a support bundle.

Privileged roles must enrol MFA and retain recovery codes. If the sole Owner loses both the
authenticator and recovery codes, use physical-host recovery rather than direct database editing:

```powershell
.\FilingBridge.cmd owner-recovery
```

The command requires exact installation/tenant/Owner selection and typed confirmation. It revokes
sessions and outstanding action tokens, clears MFA, forces fresh password/MFA enrolment, writes
durable audit evidence and prints one short-lived fragment reset link once.

## Daily operation

```powershell
.\FilingBridge.cmd start
.\FilingBridge.cmd status
.\FilingBridge.cmd logs
.\FilingBridge.cmd stop
```

`start` reuses installed images and data. It never builds, installs dependencies, migrates or
seeds. `stop` stops application services without deleting volumes, configuration or backups. Never
use `docker compose down -v` as troubleshooting.

`status` reports the installation identity, state, release, workspace slug, configured URLs,
container states and loopback readiness without printing secrets. Use `tailscale status`
separately for Serve state.

## Backups

PostgreSQL is the only durable application-data volume. Retained PDFs, iXBRL, auditor reports and
filing evidence are stored in PostgreSQL; there is no separate document volume.

Create a backup outside the release/source directory:

```powershell
.\FilingBridge.cmd backup -BackupRecipient <age-recipient> -OutputDirectory "D:\FilingBridge Backups"
.\FilingBridge.cmd verify-backup -BackupPath <path-to-fbbackup.age> -AgeIdentityFile <path-to-age-key>
.\FilingBridge.cmd export-recovery-key -OutputDirectory "E:\FilingBridge Trust Anchor"
```

The supported workflow records which services were running, quiesces application writers, creates
a PostgreSQL custom-format dump directly in ACL-restricted host staging (not the database's 64 MB
temporary filesystem), records row counts and deterministic content fingerprints for the tenant,
identity, company, accounting-period and audit tables, restores into a disposable database, matches
those selected fingerprints, and proves a non-empty public schema and EF migration history. It
hashes the payload and authenticates the exact ciphertext/dump plus immutable envelope fields with
a dedicated installation-held HMAC key before any privileged `pg_restore`; a hash alone is not
trusted restore evidence. The previous service state is restored and health-checked. If that state
cannot be restored, the artifact is retained but the command fails clearly. Run `verify-backup`
with the age identity to prove that the published encrypted envelope decrypts, matches its exact
inventory, restores again and still matches the retained selected-table evidence.

A complete host-loss recovery set also needs the encrypted configuration/key companion containing
the MFA, identity-HMAC, audit-signing, tenant-context and session key material. Losing those keys can
make an otherwise valid database dump unusable for MFA, recovery or audit verification. The Owner
bootstrap password is never included.

Only restore a set created and authenticated by this exact installation. Anyone can encrypt data to
an age public recipient, so age encryption by itself does not establish who created a backup. The
installation HMAC is verified before a dump is copied into private staging or passed to PostgreSQL.
`export-recovery-key` exports that HMAC trust anchor under a typed, installation-specific
confirmation. Store it separately from both the encrypted recovery set and age identity: possession
of all three is sufficient to recover the installation. The command refuses to overwrite an
existing key file.

An explicitly requested plaintext local database dump is a convenience copy, not a complete
off-host recovery set. Create it only with `-PlaintextDatabaseOnly`; verification/restore requires
the separate `-AllowPlaintextDatabaseOnlyRestore` acknowledgement. Store complete recovery sets on
an encrypted off-host device/location and test them. A backup kept only inside Docker Desktop's
VHDX does not protect against host loss.

If `-OutputDirectory` names a new directory, FilingBridge creates and ACL-restricts it. If it names
an existing ordinary directory, FilingBridge creates a labelled installation-specific child and
changes ACLs only on that managed leaf; it never rewrites the arbitrary parent. Keep the path short
enough for legacy Windows atomic filenames. Monitor both host free space and Docker Desktop's VHDX.
The 1.9 GB complete-payload ceiling is a release limitation, not a storage target; alert and plan
well before the database approaches it.

## Same-installation restore

Restore is deliberately not a one-line destructive shortcut:

```powershell
.\FilingBridge.cmd restore -BackupPath <path-to-fbbackup.age> `
  -AgeIdentityFile <path-to-age-key>
```

It requires a same-installation recovery set, compatible release identity and typed confirmation.
The helper verifies the envelope, exact inventory and a disposable PostgreSQL restore; stops
writers; restores a candidate database inside the existing PostgreSQL volume; preserves the prior
database under a recovery name; applies current forward migrations; rotates the session-signing
key; and starts the app for a loopback health check. It proves selected important-table
fingerprints, not every table or generated-artifact byte; it does not claim an audit-integrity
checkpoint verification or sample PDF/iXBRL generation.

## Replacement-host recovery

The coding path is implemented, but it has not yet passed a real clean/replacement-host drill:

```powershell
.\FilingBridge.cmd recover-host `
  -BackupPath <path-to-fbbackup.age> `
  -AgeIdentityFile <path-to-age-key> `
  -RecoveryAuthenticationKeyFile <path-to-exported-recovery-key> `
  -ReleaseManifest .\release.json
```

The source installation must be offline. The command requires an empty state path, a complete
age-encrypted set, the separate age identity, the separately exported HMAC trust anchor, and the
same or a newer compatible release. It authenticates and re-hashes the ciphertext before
decryption, rejects a recovered key that differs from the external trust anchor, creates a new
installation/Compose identity and empty PostgreSQL volume, independently restore-tests the dump,
selects the recovered database, preserves MFA/audit/identity key continuity, rotates browser
sessions, applies forward migrations, starts the runtime, and compares the live important-table
fingerprints with the authenticated backup evidence. A failed attempt remains explicitly blocked
as `hostRecoveryFailed` for diagnosis or deliberate purge.

Do not delete the source host until this exact workflow has passed on the intended replacement
machine and retained business artifacts have been inspected. Implementation and mock/adversarial
tests are not a substitute for that drill.

## Updates and rollback limits

```powershell
.\FilingBridge.cmd update -ReleaseManifest .\release.json `
  -BackupRecipient <age-recipient> `
  -AgeIdentityFile <path-to-age-key> `
  -OutputDirectory "D:\FilingBridge Backups"
```

Update identifies the current and target versions/digests, downloads the target before downtime,
requires a fresh verified backup, stops writers, runs the migration job separately, starts the new
runtime and verifies loopback health. It does not automate a login or sample statutory-output check.
The age identity is deliberately not saved in server state, so every encrypted update must supply
`-AgeIdentityFile`; the first update must also supply a recipient (later updates may reuse the saved
recipient). Updates are forward-only: older versions, semantic versions reused for different
commits/images, and reviewed-release-to-source-build transitions are rejected before backup or
migration. There is no implicit downgrade command.

Changing an image back does **not** reverse a PostgreSQL migration. A failed migration must roll
back transactionally or leave writers stopped. If migration committed but the new app fails health,
the helper does not claim an automatic downgrade; recovery uses the verified pre-update set and an
explicit restore decision. `status` reports the retained pre-update set; run the exact `restore`
command from the previous installed release directory after reviewing logs. End-user update never
runs `git pull` against a working tree.

## Diagnostics and support bundles

```powershell
.\FilingBridge.cmd diagnose
.\FilingBridge.cmd support-bundle
.\FilingBridge.cmd local-check
```

Diagnostics check state/config shape, required secret-file presence (including backup
authentication), the state drive's free-space floor, Docker/Compose availability, exact configured
container image identities, the applied EF migration identity when PostgreSQL is running, the
resolved Compose configuration, the recorded Tailscale route when enabled, and loopback-port state.
The Docker Desktop VHDX can live on another disk and must also be monitored separately. `status`
supplies container health, and `tailscale status` supplies Serve state. Container JSON logs are
size/rotation bounded.

The support bundle is written outside the repository and excludes database dumps, secret and
environment files, bearer tokens, Owner/workspace identity and unbounded logs. Its bounded
application log sample receives best-effort credential/email/IP/path redaction, but arbitrary
exception messages can still contain client or accounting metadata. Treat the bundle as sensitive,
review every file manually, and share it only through an approved private channel.

`local-check` writes an ACL-restricted JSON acceptance report proving all three owned services are
running, loopback readiness passes, the frontend is published exactly on IPv4 loopback, API and
PostgreSQL have no host ports, and the five authenticated business-data fingerprints are readable.
It does not log in as a human user.

For an authenticated Owner journey, the compiled release includes `scripts\smoke-production.ps1`.
For the first login, set `SMOKE_NEW_PASSWORD` as well as the temporary
`SMOKE_LOGIN_PASSWORD`. The smoke rotates the password through the CSRF-protected frontend proxy
before it accesses accounting data. A successful run writes `owner-workflow-report.json` with
password-rotation, fresh MFA, session/logout and optional download-hash evidence; it never writes
either password or the TOTP secret into that report.
If the Owner has not enrolled MFA, use `-AllowRetainedMfaEnrollment` with a handoff path whose
dedicated parent directory does not yet exist. The smoke creates that directory with a current-user-
only ACL and retains the TOTP seed plus one-time recovery codes there immediately after successful
enrollment. Move both into the Owner's password manager, then securely delete the handoff directory.
Set `SMOKE_TENANT_SLUG`, `SMOKE_LOGIN_EMAIL`, `SMOKE_LOGIN_PASSWORD`, and—after enrolment—
`SMOKE_TOTP_SECRET`, then run it against `http://localhost:3500` with `-AllowInsecureHttp`. Add
`-CompanyId`, `-PeriodId`, and `-CheckDownloads` to exercise PDF and iXBRL downloads for a safe test
period. Do not use `-AllowEphemeralMfaEnrollment` on a retained installation; that switch is only
for disposable CI bootstrap accounts.

## Offline behaviour

The designed provider-free path disables external breached-password lookup, remote monitoring and
deadline delivery; it retains local password checks and structured logs. The compiled runtime has
no intended Internet dependency for local start, login, data entry, statements, PDF/iXBRL
generation or local backup after images are present. That full offline journey has **not** yet been
exercised as live acceptance, so treat it as an implementation property awaiting the acceptance
matrix rather than a certified result. Remote tailnet access and release updates require
connectivity.

## Windows availability and security

- Enable **Start Docker Desktop when you sign in** and verify it after Docker updates.
- Docker Desktop with WSL2 normally becomes available after Windows sign-in, not as a high-
  availability pre-login server.
- No Task Scheduler job is installed automatically. Docker restart policies normally recover the
  existing containers after Docker Desktop starts. If an operator adds a logon-triggered task for
  `FilingBridge.cmd start`, run it only as the installing user, add a Docker-start delay/retry, store
  no credentials in the task, and test the exact task after reboot.
- Configure Windows not to sleep or hibernate while remote availability is required.
- Use BitLocker/device encryption and a strong Windows account with automatic screen locking.
- Do not share a generic Windows login with trustees or accountants.
- Do not add an inbound Windows firewall or router-forwarding rule for port 3500. Loopback is the
  only Docker host binding; selected-device access is through the host Tailscale client.
- Keep Windows, Docker Desktop, Tailscale and FilingBridge releases current.
- Understand where Docker Desktop stores its WSL VHDX and ensure the disk has sufficient space.
- Docker JSON logs retain at most three 10 MB files per container. Choose and document a separate
  encrypted/off-host backup destination and retention schedule; backup files are not pruned by the
  operator.
- Use a UPS where unexpected power loss is material.
- Before a planned acceptance reboot run `.\FilingBridge.cmd reboot-check prepare`. After Windows
  returns and Docker Desktop has started, run `.\FilingBridge.cmd reboot-check verify`. The verifier
  rejects a same-boot run and retains evidence only when all services returned automatically,
  loopback readiness passed, and pre/post-reboot business-data fingerprints match. Also perform a
  second-device login if Tailscale Serve is enabled.

Moving to a replacement Windows host now has the explicit `recover-host` coding path described
above. Preserve encrypted recovery sets, their age identity, and the separately exported
authentication key in distinct protected locations. The path remains operational-preview only
until it passes a clean-host drill. A later Linux VM or public service is a separate Public
Production migration using its strict TLS/provider/ingress contract, not a copy of this Windows
Compose profile.

Docker runtime restart policies and persistent Tailscale Serve configuration help recovery, but the
clean-Windows acceptance test—not a configuration claim—is the evidence that this installation
returns after reboot.

## Stop, uninstall and destructive purge

Normal stop preserves everything. Create and verify a recovery set before uninstalling. Uninstall
removes runtime containers and networks while retaining the PostgreSQL volume and private state; it
does not create a backup automatically:

```powershell
.\FilingBridge.cmd uninstall
```

Deleting data is a different operation:

```powershell
.\FilingBridge.cmd purge-data
```

Purge identifies the exact installation/project/volume, advises a verified backup and requires
typed installation identity confirmation. It must never target development, E2E or Public
Production resources.

## Troubleshooting

Start with:

```powershell
.\FilingBridge.cmd status
.\FilingBridge.cmd diagnose
```

Common causes are Docker Desktop not running, Windows-container mode, insufficient disk space, an
occupied loopback port, a stale Tailscale Serve route, a missing secret file, an image pull failure,
or a migration/health failure after an interrupted update. Do not delete volumes, edit generated
secret files, disable RLS, expose PostgreSQL, switch the API to Development or invent dummy provider
credentials to make a check green.

## Acceptance and known limits

### Current-host engineering drill

On 11 July 2026 a disposable Windows 11 Pro x64 host exercise using Windows PowerShell 5.1 and
Docker Desktop 29.2.1 passed the shipped operator path for source-build setup, loopback health,
stop/start, plaintext database-only backup and verification, same-installation restore,
source-build update, Owner recovery/reset completion, diagnostics, redacted support-bundle review,
purge and exact cleanup. The reset completed with HTTP 204 and the recovered credentials reached
the expected HTTP 202 privileged-MFA gate. Diagnostics passed 25/25. Tailscale was not enabled and
no API, database, LAN or public port was published.

That drill is useful current-host engineering evidence, not clean-machine or filing acceptance. It
used the explicitly incomplete plaintext database-only backup path, not an encrypted complete
recovery set, and it did not exercise a reviewed published bundle, genuine prior-version update,
reboot, offline accountant journey, second-device Serve access or replacement-host recovery. The
operator test suite now forces update failure and recovery and exercises the replacement-host and
reboot evidence contracts through injected deterministic seams; those are coding evidence, not
live-host acceptance.

Private Server is not live-certified until a clean Windows x64 VM or equivalent proves setup,
unique secrets, one tenant/Owner and no demo state, Owner password/MFA lifecycle, two additional
users/roles, only the approved loopback port, restart and reboot persistence, offline routine use,
private HTTPS from a second device, token-log absence, verified backup/restore, prior-version update,
forced update failure, emergency Owner recovery and unchanged Public Production validation.

Even after that operational acceptance, FilingBridge remains early-development statutory
preparation software. It does not directly submit to CRO or ROS. Real-world filing reliance remains
blocked by the current platform audit and named qualified-accountant/external evidence.
