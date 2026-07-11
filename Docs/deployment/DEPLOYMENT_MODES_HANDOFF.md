# Deployment Modes Workstream Handoff

> **Status: design and implementation planning — not yet available.**
>
> The private-server mode described here has not been implemented. Do not expose the
> current development Compose stack to a LAN, tailnet, or the public internet. This
> document is the canonical handoff for a future session implementing and documenting
> the deployment modes.

Last updated: 11 July 2026.

## Why this workstream exists

The original user need was deliberately modest: make annual accounts and annual-return
preparation easier for one Irish charity, while allowing a small number of directors to
view or review the work. During development, the repository also acquired the controls
needed for a future public, multi-user production service. Those are different deployment
needs and must not be forced through one setup guide.

This workstream will make three supported modes explicit:

1. **Development** for contributors changing the code.
2. **Private Server** for a person, charity, or small organisation sharing a compiled
   installation only with selected users, normally through Tailscale Serve.
3. **Public Production** for an internet-reachable service operated behind an HTTPS
   ingress with the full public-production controls.

Public Production and Private Server use production builds. Private Server is not a
development stack and is not a public-production release with controls silently disabled.
It is a separate, bounded deployment contract.

This workstream complements the independent production-readiness audit. It does not
declare the statutory engine filing-ready, remove qualified-accountant gates, or add
direct CRO/ROS submission.

## Decisions made during the planning session

### Use containers for the private server

The private server will run the compiled frontend, compiled API, and PostgreSQL in
Docker. Tailscale runs on the host and proxies to the frontend's loopback-only port.

The Windows/WSL filesystem performance warning applies primarily to development setups
that bind-mount a Windows source tree into a Linux container and run watchers or hot
reload. The private server will have no source bind mounts, watchers, or development
compilation. Code is copied into images at build time, and PostgreSQL uses a Docker named
volume. An occasional image build may take longer on Windows; normal application requests
do not repeatedly traverse the Windows source directory.

For active development, a hybrid arrangement remains valid: PostgreSQL in Docker with
the .NET API and Next.js development server running natively. That is a development
performance choice, not the private-server architecture.

### Tailscale is the private ingress

The default Private Server topology is:

```text
selected director's browser
        |
        | private HTTPS over the tailnet
        v
Tailscale Serve on the host
        |
        | http://127.0.0.1:<private frontend port>
        v
compiled Next.js frontend container
        |
        | private Docker network
        v
compiled ASP.NET Core API container
        |
        | private Docker network
        v
PostgreSQL container + named volume
```

Private Server documentation must require Tailscale **Serve**, not Funnel. It must not
require router port-forwarding, a public DNS record, IIS, Caddy, Apache, or Nginx. Each
person still receives an individual FilingBridge application account; tailnet access is
an additional network boundary, not a replacement for application authentication and
roles.

The intended persistent Serve configuration is equivalent to:

```powershell
tailscale serve --bg --https=443 localhost:3500
```

The setup helper should discover the actual tailnet DNS name rather than asking the user
to type it into multiple configuration files.

### Caddy is optional in public production

`deploy/caddy/Caddyfile.example` is one ingress example, not an application dependency.
Public Production may use Caddy, Apache, Nginx, an Azure ingress, or another reverse proxy
that satisfies the documented HTTPS and forwarded-header contract. The public-production
guide must be ingress-neutral and link to separate examples.

### Private backups are optional and may be unencrypted

For a private installation using the owner's own charity data, an ordinary local
PostgreSQL dump is sufficient if the owner accepts the risk. Its purpose is recovery from
accidental deletion, disk failure, or a bad upgrade. Encrypted off-host backups and
formal restore evidence remain Public Production requirements, not Private Server
installation blockers.

### Preserve the filing boundary in every mode

All modes may be used to exercise the accounts workflow and generate review artifacts.
No mode directly submits to CRO or ROS. Real-world filing reliance must continue to show
the appropriate professional/external review gates. Deployment convenience must not be
represented as statutory acceptance.

## Reference-environment observations

The planning session inspected both a shared Azure Docker host and the intended local
Windows host read-only. No containers, files, firewall rules, ingress settings, or
databases were changed.

- The Azure host confirmed that the app could coexist with other containers and use an
  Apache/Plesk loopback proxy; it also confirmed that Caddy is unnecessary. The user later
  selected a local-machine/Tailscale deployment as the preferred Private Server route.
- The reference Windows machine has 8 CPU cores / 16 logical processors, 16 GB RAM,
  roughly 120 GB free disk at inspection time, Docker Desktop and Docker Compose working,
  suitable loopback ports free, and mains-powered sleep disabled.
- Tailscale was not installed on the reference Windows machine at inspection time.
- Hostnames, credentials, SSH key paths, tailnet identity, and private infrastructure
  details are intentionally not retained in this public repository.

The reference hardware is sufficient for low-traffic use by a small board. Availability
still depends on the computer being powered, awake, connected, signed into Windows far
enough for Docker Desktop to start, and connected to Tailscale. This is suitable for a
small private service, not high-availability hosting.

## Supported-mode contract

| Property | Development | Private Server | Public Production |
| --- | --- | --- | --- |
| Primary audience | Contributors | Selected users in one small organisation | Internet-facing service operator |
| Runtime code | Development or compiled | Compiled production images | Exact promoted production images |
| Source mounts/watchers | Allowed | Forbidden | Forbidden |
| Intended exposure | Localhost only | Tailnet only | Public HTTPS login surface |
| Public registration | Not a deployment promise | Disabled/unsupported | Disabled unless explicitly implemented later |
| Demo credentials/data | Allowed and clearly labelled | Forbidden | Forbidden |
| Frontend host binding | Developer choice | Loopback only | Loopback or trusted private ingress network |
| API/database host ports | Allowed for development | Forbidden | Forbidden |
| Secure cookies | Optional locally | Required | Required |
| External monitoring | Optional | Optional | Required by the public-production contract |
| Database transport TLS | Optional locally | Optional on the isolated Docker network | Required |
| Backup | Disposable data acceptable | Optional ordinary backup | Encrypted off-host backup and restore evidence |
| Image provenance | Not required | Local build or stable release | Exact CI-promoted digest and provenance |
| Direct CRO/ROS submission | Unsupported | Unsupported | Unsupported |

## Proposed configuration model

Do not overload `ASPNETCORE_ENVIRONMENT=Development` to make Private Server boot. That
would enable development error behaviour, Swagger, known development allowances, and
non-secure cookie behaviour.

Retain production runtime semantics and add an explicit deployment mode, for example:

```text
ASPNETCORE_ENVIRONMENT=Production
NODE_ENV=production
Deployment__Mode=PrivateServer
```

The supported values should be:

```text
Development
PrivateServer
PublicProduction
```

The mode-aware startup validator must retain shared security controls and relax only the
controls that do not fit the bounded private topology.

### Private Server invariants

Private Server must fail startup when any of these are false:

- production frontend and API builds are used;
- the session signing key, audit key, identity HMAC key, MFA key, database password, and
  frontend/API key are generated rather than committed development values;
- demo seeding and sample users/companies are disabled;
- the first Owner bootstrap is explicit, idempotent, and refuses to overwrite an existing
  charity or reset its Owner password;
- secure session and CSRF cookies are enabled for the Tailscale HTTPS origin;
- the allowed origin is the exact Tailscale HTTPS origin;
- only the frontend is published, and only on `127.0.0.1`;
- API and PostgreSQL have no host port mappings;
- application authentication, CSRF, authorisation, tenant scoping, audit logging, and
  destructive-action safeguards remain enabled;
- PostgreSQL data is stored in a named persistent volume;
- no source directory is mounted into a running application container;
- Tailscale Funnel is not configured by repository automation.

The private profile may make external error tracking, delivery-provider integration,
database TLS inside the isolated Docker network, encrypted backup envelopes, immutable
image promotion, and release-evidence publication optional. These relaxations must be
explicit in code and documentation; do not bypass the whole production safety service.

### Public Production invariants

Public Production continues to use the hardened `compose.production.yml` contract:

- exact promoted image digests;
- public HTTPS through a trusted ingress;
- private API and database networks;
- separate migration and least-privileged application database roles;
- verified PostgreSQL TLS;
- generated secret files;
- monitoring and incident routing;
- encrypted off-host backup and restore drills;
- controlled migrations, health checks, smoke tests, and release evidence;
- named professional/external acceptance before real filing reliance.

## Proposed repository layout

```text
compose.yml                              # Development only
compose.private.yml                      # Planned Private Server profile
compose.production.yml                   # Public Production
.env.private.example                     # Names/placeholders only; no secrets

Docs/deployment/README.md                # Choose a mode
Docs/deployment/private-server.md        # Private installation and daily use
Docs/deployment/public-production.md     # Public-production entry guide
Docs/deployment/DEPLOYMENT_MODES_HANDOFF.md
LOCAL_SETUP.md                           # Development guide
Docs/operations/production-runbook.md    # Advanced public operations

scripts/private-server.ps1               # setup/start/stop/status/update dispatcher
scripts/backup-private-server.ps1        # optional ordinary dump
scripts/restore-private-server.ps1       # explicit restore

deploy/caddy/Caddyfile.example           # optional public ingress example
deploy/apache/accounts.conf.example      # planned optional example
deploy/nginx/accounts.conf.example       # planned optional example
```

Prefer one discoverable PowerShell entry point with subcommands over asking private users
to remember Compose and Tailscale commands:

```powershell
.\scripts\private-server.ps1 setup
.\scripts\private-server.ps1 start
.\scripts\private-server.ps1 status
.\scripts\private-server.ps1 stop
.\scripts\private-server.ps1 update
.\scripts\private-server.ps1 backup
```

`stop` must preserve the database volume. Any reset or volume-deletion command must be
separate, explicitly destructive, and require confirmation.

## Documentation plan

### README

Add a deployment-mode chooser near Quick Start. A reader must be able to select the
correct guide in under 30 seconds. The README must label Private Server as in development
until the profile and scripts pass their acceptance tests. It must stop implying that
Caddy is required.

### Deployment index

Create `Docs/deployment/README.md` with:

- a short decision tree;
- the supported-mode matrix;
- links to one canonical guide per mode;
- availability and threat-boundary explanations;
- a migration path from Private Server to Public Production.

### Development guide

Retitle `LOCAL_SETUP.md` visibly as Development Setup and document both full-Docker and
hybrid native development. Warn that the known credentials and demo allowances must
never be shared through Tailscale or exposed publicly.

### Private Server guide

Document, in order:

1. prerequisites on Windows, macOS, and Linux;
2. Docker and Tailscale installation;
3. one-time setup and secret generation;
4. exact first-start behaviour and one-time Owner bootstrap;
5. Tailscale Serve configuration and director access;
6. creating individual application users and choosing roles;
7. daily start, stop, status, logs, and health checks;
8. updates and migration behaviour;
9. optional ordinary backup and restore;
10. moving the database to Public Production;
11. troubleshooting and personal-computer availability limitations.

Do not hard-code Tailscale plan limits or pricing in the repository; link to the current
official pages because those facts can change.

### Public Production guide and runbook

Create a concise public-production entry guide and retain the existing operations runbook
as the detailed reference. Rewrite the runbook's ingress introduction as a generic
contract, with Caddy, Apache, and Nginx as optional examples.

## Implementation work packages

1. **Mode contract and tests**
   - Add the deployment-mode configuration type and fail-fast validation tests.
   - Prove Private Server keeps secure cookies, safe errors, API access control, and no
     development bypasses.
2. **Private Compose profile**
   - Add compiled services, named database volume, loopback-only frontend port, private
     networks, health checks, resource limits, and no source bind mounts.
   - Add CI assertions that API/database ports cannot be published in this mode.
3. **One-time setup**
   - Generate secrets into a gitignored local file.
   - Detect the Tailscale DNS name and set the exact HTTPS origin.
   - Refuse to overwrite an existing configuration or charity database.
4. **Lifecycle helper**
   - Implement setup/start/stop/status/logs/update commands.
   - Make failures readable to non-Docker experts.
5. **User access**
   - Document Tailscale invitation or machine sharing and create separate app accounts.
   - Do not share the Owner password between directors.
6. **Backup and migration**
   - Add an optional unencrypted local dump/restore workflow.
   - Prove the same database can move to Public Production.
7. **Public ingress cleanup**
   - Make the public docs proxy-neutral and add Apache/Nginx examples.
8. **End-to-end verification**
   - Validate a clean clone on Windows.
   - Test first setup, second-start idempotency, login, director access, restart, update,
     backup/restore, and rejection of unsafe port/credential changes.

## Acceptance criteria

Private Server is not complete until all of the following are objectively demonstrated:

- a clean clone can reach the first login without hand-editing YAML;
- setup generates unique values and no secret is committed or printed unnecessarily;
- rerunning setup refuses to replace an existing charity or reset credentials;
- `docker compose config` proves only the frontend has a host port and it is loopback-only;
- no development credential or demo seed appears in the resolved private configuration;
- the application reports healthy through both loopback and Tailscale Serve;
- two separate non-Owner users can sign in and receive their intended application roles;
- a computer restart plus Windows sign-in restores Docker and the persistent Serve route;
- ordinary stop/start and image rebuilds preserve PostgreSQL data;
- backup/restore works when the user opts into it;
- documented commands match the implementation and run successfully;
- Development, Private Server, and Public Production Compose/configuration checks run in
  CI;
- all three mode guides preserve the no-direct-filing and professional-review boundary.

## Instructions for the next agent/session

1. Read repository-root `AGENTS.md` and `CLAUDE.md` completely.
2. Read this handoff before proposing another deployment architecture.
3. Inspect `git status` and preserve all pre-existing user changes; this repository had a
   substantial dirty worktree during the planning session.
4. Do not treat `compose.yml` as shareable: it contains development-only configuration.
5. Do not make Private Server a wrapper around the full 33-variable/15-secret public
   production setup.
6. Do not bypass every production safety check by setting the API environment to
   Development. Implement a narrow, explicit mode contract.
7. Do not add Caddy, IIS, Apache, or Nginx to the Private Server path. Tailscale Serve is
   its ingress.
8. Do not use Tailscale Funnel or publish API/database ports.
9. Add tests with each implementation slice and verify the documented clean-clone path.
10. Update this handoff's status and checklist as work lands; never describe planned files
    or commands as available before they pass verification.
11. Keep the independent production-readiness audit truthful. Private deployment success
    does not close statutory, external-validation, monitoring-provider, backup-drill, or
    qualified-accountant controls for public filing reliance.

## Current status at handoff

- [x] User intent and three-mode model agreed.
- [x] Docker-versus-native decision recorded.
- [x] Tailscale/private-ingress decision recorded.
- [x] Caddy clarified as optional for public production.
- [x] Documentation structure and acceptance criteria planned.
- [ ] Deployment-mode configuration implemented.
- [ ] `compose.private.yml` implemented.
- [ ] Private setup/lifecycle scripts implemented.
- [ ] Private backup/restore helpers implemented.
- [ ] Private Server guide implemented and clean-clone tested.
- [ ] Public Production entry guide implemented.
- [ ] Production runbook made ingress-neutral.
- [ ] Apache and Nginx ingress examples implemented.
- [ ] CI mode-contract and Compose checks implemented.
- [ ] End-to-end Windows/Tailscale acceptance completed.
