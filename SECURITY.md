# Security Policy

## Supported versions

FilingBridge is early-development statutory preparation software. The only
published releases are versioned `0.1.0-preview.*` Private Server operational
previews. The latest `private-server-v0.1.0-preview.*` release that has passed
its release workflow and acceptance evidence is the supported self-hosted
version. Older previews may be asked to upgrade before a fix can be provided
safely, because Private Server updates are forward-only. A clean `main`
checkout with successful repository CI may be evaluated only as a supervised
testing snapshot; it is not a published release.

Use only the named `FilingBridge-PrivateServer-<version>.zip` release asset
with its checksum and manifest, after verifying the immutable GitHub Release and
the exact asset attestation as described in
[`Docs/deployment/private-server.md`](Docs/deployment/private-server.md).
GitHub's repository-level **Code > Download ZIP** archive and the generated
source archives are not release bundles and are unsupported for installation.

The Windows Private Server host baseline is Windows 11 x64 23H2 (build 22631)
or newer on a currently serviced edition, running Docker Desktop with Linux
containers. The separate Ubuntu profile supports Ubuntu 24.04 LTS x86-64 with
Docker Engine and a dedicated non-root operator account; it remains an
operational preview until its own live acceptance matrix passes (see
[`Docs/deployment/private-server-linux.md`](Docs/deployment/private-server-linux.md)).
ARM hosts, rootless Docker, Podman, Kubernetes and public ingress are outside
the Private Server preview.

This policy does not turn Public Production into a launched service. The
Development stack, the Private Server profiles and the Public Production
profile have separate deployment and evidence requirements; see
[`Docs/deployment/README.md`](Docs/deployment/README.md) and
[`Docs/deployment/public-production.md`](Docs/deployment/public-production.md).
FilingBridge does not submit directly to CRO or Revenue, and a passing security
or operational check is not statutory, source-law or qualified-accountant
acceptance.

## Report a vulnerability privately

Do not open a public issue containing an exploitable vulnerability, personal
data, credentials, access tokens, private URLs, database contents, recovery
keys, or backup material.

Use the repository's **Security** tab and choose **Report a vulnerability** to
open a private GitHub Security Advisory:

<https://github.com/jasperfordesq-ai/accounts/security/advisories/new>

Include, where safe:

- the affected commit or release tag;
- the affected deployment profile (`Development`, `PrivateServer`, or
  `PublicProduction`);
- a minimal reproduction using disposable data;
- the impact and preconditions;
- whether exploitation has been observed; and
- suggested remediation, if known.

Never attach a real FilingBridge backup, environment or secret file, database
dump, bank statement, accounting record, private invitation/reset URL, or
screenshot containing company or personal data. Use redacted or synthetic
evidence.

## Response and disclosure

The maintainer will acknowledge a complete private report when it is reviewed,
triage severity and supported versions, coordinate a fix and verification, and
agree a responsible disclosure point with the reporter where practical. No
specific response or remediation time is guaranteed by this volunteer/open
source project.

Security fixes should be bound to a commit or release, pass the applicable
tests, and avoid disclosing exploit detail before supported users can update.

## Private Server trust boundary

The supported private-access choices are the exact loopback HTTP origin for use
on the host itself (port `3500` by default), or the host's exact private
`.ts.net` HTTPS origin through host-level Tailscale **Serve**. Tailscale
Funnel, router port forwarding, public firewall rules, Cloudflare Tunnel and
other public or third-party tunnels are not supported. Do not add a reverse
proxy or public listener to work around this boundary.

Only the frontend publishes a host port, and only on IPv4 loopback. The API and
PostgreSQL have no host ports and must never be published. Treat any other
published port, or any attachment of the API or database to the frontend's
ingress bridge, as a security failure. All browser `/api` requests pass through
the Next.js proxy, which injects the service API key so that the browser never
receives it.

Tailscale is a network boundary, not FilingBridge authorization. FilingBridge
ignores Tailscale identity headers as an authorization source. Every person
needs an individual application account with the least-privileged existing role
(`Owner`, `Accountant`, `Reviewer` or `Client`), MFA where required, company
access, and audit trail; do not share a generic Windows, Linux or application
login. Tenant isolation is enforced by forced PostgreSQL row-level security
with signed tenant context, and cross-tenant company identifiers return 404.
Never disable RLS, expose PostgreSQL, switch the API to Development, or invent
dummy provider credentials to make a check pass.

Private Server trusts the host, its Docker administrator and the installation
owner. It does not defend against a hostile host administrator. On Ubuntu, the
Docker-capable operator account is root-equivalent and must be restricted to
the dedicated operator; do not run FilingBridge commands with `sudo`, do not
expose the Docker daemon over TCP, and do not use `compose.yml` on a server.
Remote Docker endpoints and ambient Docker overrides are not supported.

Run lifecycle operations only through the supported launcher (`FilingBridge.cmd`
on Windows, `filingbridge` on Ubuntu) from an extracted, verified release
bundle. Running raw Docker Compose, ad-hoc database commands, manual volume
deletion, `docker compose down -v`, or hand-edits to generated secret files
bypasses the launcher's installation lock, manifest and image-digest checks,
ACL enforcement and recovery gates; those are not supported installation,
update or recovery procedures. If the sole Owner loses both authenticator and
recovery codes, use `owner-recovery` from the physical host rather than editing
the database. Updates are forward-only; changing an image back does not reverse
a PostgreSQL migration.

Invitation and reset links carry a short-lived one-time token in the URL
fragment and only a keyed hash is stored. Share them through a private channel
and never paste them into a support bundle or public issue. The support bundle
applies best-effort redaction only: review every file manually and share it only
through an approved private channel.

## Protected state and recovery keys

The launcher keeps durable private state outside the release directory. On
Windows the default is `%LOCALAPPDATA%\FilingBridge\server`, protected with
restrictive NTFS ACLs. On Ubuntu it is
`${XDG_STATE_HOME:-$HOME/.local/state}/filingbridge/server`, with directories
mode `0700` and files mode `0600`. This state holds the generated environment
and secret files (including the MFA encryption, identity HMAC, audit-signing,
tenant-context, session-signing and backup-authentication keys), installation
state and diagnostics. These controls do not protect against the installing
operator, a Docker administrator, malware running as that operator, or loss of
the host. Never commit, email or attach any of these files to a public issue.

Complete recovery sets are age-encrypted and authenticated by an
installation-held HMAC before any privileged restore. Keep these three items in
separate protected locations: the encrypted recovery set, its age identity, and
the recovery-authentication key produced by `export-recovery-key`. Possession of
all three is sufficient to recover the installation, so do not store them
together. A complete recovery set carries the MFA, identity-HMAC, audit-signing,
tenant-context and session key material in its encrypted configuration
companion, so losing the host's copies of those keys does not by itself prevent
recovery: an intact complete set, its age identity and the separately saved
recovery-authentication key still allow `recover-host` to restore them. Recovery
is lost only when no usable combination of those three remains, for example if
the age identity is lost or the only complete sets are missing or damaged. In
that case do not delete the remaining host state, volume or recovery sets, and
do not improvise a plaintext backup,
`docker volume` operation or manual database edit as a workaround. Preserve the
host and seek supervised recovery. If an age identity, the recovery key or the
operator account is compromised, treat every recovery set it protects as
disclosed, remove private access where safe, preserve incident evidence, revoke
affected sessions and credentials, and create a new independently protected
recovery generation through a reviewed procedure. A stolen key cannot be made
unable to decrypt copies an attacker already obtained.

Encryption and authentication do not make a recovery set safe to publish:
names, paths, non-secret operational metadata and encrypted accounting and
personal data still require restricted handling. A plaintext database-only dump
requested with `-PlaintextDatabaseOnly` is a local convenience copy, not a
complete or secure recovery set.

`purge-data` is destructive and terminal for the identified installation. Create
and verify a recovery set first and never run it against development or Public
Production resources. Moving to a replacement host uses `recover-host` with the
separately held age identity and recovery-authentication key; do not delete the
source installation until the recovered data and sample outputs have been
inspected.

## Self-hosting responsibilities

The software cannot protect data from an administrator who controls the host,
the Docker daemon, the operating-system account, the age identity, the recovery
key or an unencrypted backup. Self-hosting operators remain responsible for host
and Docker updates, disk encryption (BitLocker/device encryption on Windows),
individual accounts and automatic screen locking, tailnet access policy,
verified encrypted off-host backups, restore rehearsals, monitoring free disk
space (including Docker Desktop's VHDX), and physical security. See
[`Docs/deployment/private-server.md`](Docs/deployment/private-server.md) and
[`Docs/deployment/private-server-linux.md`](Docs/deployment/private-server-linux.md).

Do not expose the Private Server frontend port through a router, public firewall
rule, Tailscale Funnel, Cloudflare Tunnel, or another unsupported tunnel.
Operating FilingBridge for unrelated organisations or over the public internet
requires the separate Public Production contract and its own acceptance
evidence.
