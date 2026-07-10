# FilingBridge

[![License: AGPL v3](https://img.shields.io/badge/license-AGPL--3.0-blue.svg)](LICENSE)

FilingBridge is an early-development Irish annual-return and statutory filing preparation
workbench. It takes imported bank transactions and year-end business facts and turns them into
accountant-reviewable statutory financial statements, CRO filing packs, Revenue CT1 / iXBRL
support, readiness evidence, and sign-off gates.

It is not bookkeeping software, not a transaction recorder, and not a direct CRO/ROS submission
tool. The product is designed around the year-end preparation path: classify the company, collect
year-end evidence, generate and review statutory outputs, record filing workflow states, and block
real-world use until professional review is complete.

> **Early development status:** FilingBridge is not production filing software yet. Core
> authentication, tenant isolation, role-based access, CSRF protection, audit integrity, statutory
> preparation workflows, and production startup safety checks are in place, but real CRO or Revenue
> use must remain subject to named qualified-accountant review and further production-readiness
> evidence.

## Tech stack

- **Backend** — ASP.NET Core (.NET 10) Minimal API, EF Core 10, PostgreSQL 16.4, QuestPDF, CsvHelper
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
# Backend (restore/build the solution; execute the test project explicitly)
cd backend && dotnet build Accounts.slnx
cd backend && dotnet test Accounts.Tests/Accounts.Tests.csproj --configuration Release --no-restore

# Frontend
cd frontend && npm ci
cd frontend && npm test && npm run lint && npm run build
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
| [LICENSE](LICENSE) | GNU Affero General Public License version 3 text |
| [NOTICE](NOTICE) | Jasper Ford attribution, Section 7 additional terms, and source-code notice |
| [CONTRIBUTORS.md](CONTRIBUTORS.md) | Creator and contributor attribution |
| [CONTRIBUTOR_TERMS.md](CONTRIBUTOR_TERMS.md) | Contributor licence grant and project stewardship terms |
| [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) | Major third-party component notices |

## Credits and Origins

### Creator

- **Jasper Ford** - Creator, main contributor, and primary author

### Contributors

- **Jasper Ford** - Creator, main contributor, and primary author

### Third-party open-source components

Irish Accounts builds on open-source projects including ASP.NET Core, Entity
Framework Core, PostgreSQL tooling, QuestPDF, CsvHelper, Next.js, React,
HeroUI, Tailwind CSS, and related build/test tooling. Each retains its own
licence and copyright; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## License

This software is licensed under the **GNU Affero General Public License version
3** (AGPL-3.0-or-later).

The AGPL requires that if you run a modified version of this software on a
server and let others interact with it, you must make your source code
available to those users.

See the [LICENSE](LICENSE) file for the full license text.
See the [NOTICE](NOTICE) file for attribution requirements.
See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for bundled third-party
components and their licences.

## UI Attribution Requirement

Under AGPL Section 7(b), all public deployments of this software **must**
display visible attribution and a link to the source code repository.

### Required Attribution

**Footer (all pages):**
> "Built on Irish Accounts by Jasper Ford"

This text must be a clickable hyperlink to:
<https://github.com/jasperfordesq-ai/accounts>

**About page:**
> "Powered by Irish Accounts
> Created by Jasper Ford
> Licensed under AGPL v3-or-later"

With a link to: <https://github.com/jasperfordesq-ai/accounts>

### Compliance

- The [NOTICE](NOTICE) file contains the authoritative wording for all
  attribution requirements
- Removing or obscuring required attribution is a licence violation
- This requirement applies to all deployments, including modified versions and
  SaaS offerings

## Source Code

The complete source code for this project is available at:
<https://github.com/jasperfordesq-ai/accounts>

## Legal framework

Ireland, Companies Act 2014 (as amended). Size thresholds, the 2-of-3 test, audit exemption
(s.360), statutory statements (s.280D micro / s.352 abridged), and corporation tax (12.5% trading
rate, capital allowances under s.284 TCA 1997) are implemented and configurable via
`appsettings.json`.
