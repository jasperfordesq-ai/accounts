# Irish Statutory Accounts Production Platform

A multi-company web application that replaces the traditional year-end accountant workflow for
Irish private companies. It takes imported bank transactions and year-end business facts and
produces statutory financial statements, CRO filing outputs, and Revenue CT1 / iXBRL support.

> **Status:** firm authentication, tenant isolation, role-based access, CSRF protection, audit
> integrity, and production startup safety checks are in place. Backend test suite (461 tests) and
> the frontend production build both pass; CI additionally runs a full HTTPS production-stack smoke
> test with a backup/restore drill.

## Tech stack

- **Backend** — ASP.NET Core (.NET 10) Minimal API, EF Core 9, PostgreSQL 16.4, QuestPDF, CsvHelper
- **Frontend** — Next.js 16 (App Router), HeroUI v3, Tailwind CSS 4
- **Infra** — Docker Compose (local + production), GitHub Actions CI, Caddy reverse-proxy example

## Quick start (local)

```bash
docker compose up -d --build
# Frontend: http://localhost:3000   API: http://localhost:5090   Swagger: /swagger (dev)
```

Full local instructions, the seeded local admin account, and SDK-only run steps are in
**[LOCAL_SETUP.md](LOCAL_SETUP.md)**.

## Build & test

```bash
# Backend (solution is the XML-format Accounts.slnx)
cd backend && dotnet build Accounts.slnx
cd backend && dotnet test  Accounts.slnx

# Frontend
cd frontend && npm ci
cd frontend && npm run lint && npm run build
```

CI runs all of the above plus production compose validation and a production-stack smoke +
backup/restore drill — see [`.github/workflows/ci.yml`](.github/workflows/ci.yml).

## Production deployment

Use `compose.production.yml` behind a TLS-terminating reverse proxy
(see [`deploy/caddy/Caddyfile.example`](deploy/caddy/Caddyfile.example)). All secrets are supplied
via env/secret files and never committed; `ProductionSafetyService` fails startup fast if the
configuration is unsafe. Operational scripts (backup, restore, smoke, image verification) live in
[`scripts/`](scripts/). The security model, required environment variables, and deployment details
are documented in **[CLAUDE.md](CLAUDE.md)** under *Authentication, Authorization & Security* and
*Production Deployment*.

## Documentation

| Document | Contents |
|----------|----------|
| [CLAUDE.md](CLAUDE.md) | Architecture, entities, services, endpoints, security model, deployment |
| [LOCAL_SETUP.md](LOCAL_SETUP.md) | Running the stack locally + seeded admin |
| [REQUIREMENTS.md](REQUIREMENTS.md) | Product requirements |

## Legal framework

Ireland, Companies Act 2014 (as amended). Size thresholds, the 2-of-3 test, audit exemption
(s.360), statutory statements (s.280D micro / s.352 abridged), and corporation tax (12.5% trading
rate, capital allowances under s.284 TCA 1997) are implemented and configurable via
`appsettings.json`.
