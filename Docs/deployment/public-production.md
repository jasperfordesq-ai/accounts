# Public Production deployment entry guide

Public Production is the hardened, internet-reachable FilingBridge profile. It is not required for
a small Private Server installation. This page is an entry point; the authoritative procedures are
in the [Production Operations Runbook](../operations/production-runbook.md).

## Topology

```text
public HTTPS browser
        |
approved TLS ingress
        |
loopback/private-network Next.js frontend
        |
internal Kestrel API
        |
certificate-verified PostgreSQL
```

Every browser route, including `/api/*`, must go to Next.js. The frontend proxy injects the
file-backed service key and forwards authenticated session/CSRF cookies to Kestrel. Direct ingress
routing to Kestrel is unsupported.

The ingress may be Caddy, Apache, Nginx, a managed cloud ingress, or another reviewed proxy that:

- terminates approved HTTPS;
- prevents clients bypassing the frontend;
- preserves the external Host;
- overwrites forwarded client, host and protocol headers;
- applies the documented security-header and request-size policy; and
- forwards all application routes to the loopback/private frontend port.

Checked-in examples:

- [Caddy](../../deploy/caddy/Caddyfile.example)
- [Apache](../../deploy/apache/accounts.conf.example)
- [Nginx](../../deploy/nginx/accounts.conf.example)

## Non-negotiable production controls

Use `compose.production.yml` and exact CI-promoted digest references. Retain:

- a dedicated migration job and least-privileged API database login;
- forced PostgreSQL RLS with signed tenant context;
- certificate-verified PostgreSQL TLS;
- generated, file-backed and independently scoped secrets;
- no startup migration or demo seeding;
- monitoring, alert routing and deadline delivery providers;
- encrypted off-host backups and named restore drills;
- image scanning, SBOM, provenance and exact-candidate smoke evidence; and
- the no-direct-filing and professional/external acceptance gates.

Do not copy Private Server's provider-free or internal-plaintext-database allowances into Public
Production. Those allowances are selected only by the exact private deployment mode and its proven
same-host topology.

## Release state

The hardened stack exists, but the current platform audit remains authoritative. A passing Compose
render, production smoke or private installation does not close statutory, external ROS/iXBRL,
qualified-accountant, visual, monitoring-provider or backup-operator acceptance.
