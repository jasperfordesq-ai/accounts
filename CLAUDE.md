# Irish Statutory Accounts Production Platform

Multi-company web application that replaces the traditional year-end accountant workflow for Irish private companies. Takes imported bank transactions + year-end business facts and produces statutory financial statements, CRO filing outputs, and Revenue CT1/iXBRL support.

## Repository

- GitHub: https://github.com/jasperfordesq-ai/accounts
- Treat this as the canonical repository for the Irish statutory accounts production platform.

## Tech Stack

- **Backend**: ASP.NET Core (.NET 10) Minimal API + EF Core 9 + PostgreSQL 16.4
- **Frontend**: Next.js 16 (App Router) + HeroUI v3 + Tailwind CSS 4
- **PDF Generation**: QuestPDF (server-side)
- **iXBRL**: Custom XHTML generator with FRC Irish FRS taxonomy
- **CSV Import**: CsvHelper (bank statement parsing)
- **Container**: Docker Compose (api + db + frontend)

## Project Structure

```
accounts/
├── backend/
│   ├── Accounts.slnx               # XML solution (Api + Tests); used by `dotnet` and CI
│   ├── Directory.Build.props       # Routes build output to ../.dotnet-artifacts (WDAC workaround)
│   └── Accounts.Api/
│       ├── Program.cs              # Minimal API setup, middleware pipeline, core endpoint mappings
│       ├── Entities/               # 43 entity classes + Enums.cs (incl. Tenant, UserAccount, UserCompanyAccess)
│       ├── Data/
│       │   ├── AccountsDbContext.cs       # 42 DbSets, full OnModelCreating config
│       │   ├── SeedData.cs                # Sample companies + demo tenant/role users (dev only)
│       │   ├── DesignTimeDbContextFactory.cs
│       │   └── Migrations/                # 18 EF migrations
│       ├── Services/               # 35 services (business logic + auth/security)
│       ├── Middleware/             # 10 middleware (security headers, auth, CSRF, tenant, RBAC, audit, locks)
│       ├── Rules/                  # Configurable legal thresholds + auth/session/API-access/audit config
│       └── Endpoints/              # 13 endpoint group files + registration
├── frontend/
│   ├── next.config.ts
│   └── src/
│       ├── app/                    # Next.js App Router pages
│       │   ├── page.tsx            # Dashboard — company listing
│       │   ├── login/page.tsx      # Firm staff login
│       │   ├── change-password/    # Self-service password change
│       │   ├── health/, health/ready/   # Liveness + readiness routes
│       │   ├── api/[...path]/route.ts    # Server-side proxy: injects API key, forwards cookies
│       │   ├── companies/[id], companies/new, .../periods/[periodId]/{year-end,classify,statements,notes,charity}
│       ├── components/             # AppNavbar, AuthProvider (session + route guard), ErrorBoundary, ...
│       └── lib/                    # api.ts (typed client), auth.ts, proxy helpers, validation
├── scripts/                        # PowerShell: backup/restore Postgres, production smoke, image verify
├── deploy/caddy/Caddyfile.example  # Reverse-proxy / HTTPS ingress example
├── .github/workflows/ci.yml        # CI: backend build+test, frontend lint/type/test/build, prod smoke
├── compose.yml                     # Local Docker: api (5090) + postgres (5433) + frontend (3000)
├── compose.production.yml          # Production stack (secret files, hardened config)
├── Dockerfile.backend
├── Dockerfile.frontend
├── LOCAL_SETUP.md                  # Local run + seeded admin instructions
├── REQUIREMENTS.md                 # Full product requirements
└── CLAUDE.md
```

## Development Commands

```bash
# Backend build + test (solution is Accounts.slnx, the XML solution format)
cd backend && dotnet build Accounts.slnx
cd backend && dotnet test Accounts.slnx        # 461 tests (2 Postgres-only tests skip on InMemory)

# Frontend build / lint / unit checks
cd frontend && npm run build
cd frontend && npm run lint
cd frontend && npm run test:readiness && npm run test:proxy && npm run test:auth

# Run backend locally (needs PostgreSQL on port 5433) — see LOCAL_SETUP.md
cd backend/Accounts.Api && dotnet run

# Run frontend dev server
cd frontend && npm run dev

# Docker (full local stack)
docker compose up -d

# EF Core migrations
cd backend/Accounts.Api
dotnet ef migrations add <Name> --output-dir Data/Migrations

# API: port 5090 (Docker) or 5080 (dotnet run)
# Swagger: http://localhost:5090/swagger (dev only)
# Frontend: http://localhost:3000 (Next.js)
# Health: /health (liveness), /health/ready (readiness)
```

> Build output is routed to `.dotnet-artifacts/` by `backend/Directory.Build.props` because
> Windows WDAC blocks running DLLs (incl. QuestPDF) from the repo path. CI uses the same `Accounts.slnx`.
>
> WDAC still blocks a **freshly rebuilt** `Accounts.Api.dll` at test runtime even under `.dotnet-artifacts/`
> (error `0x800711C7`) — so after changing backend code, run tests with artifacts redirected outside the
> repo tree: `dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art`. CI (Linux) is
> unaffected and remains the source of truth.

## Database

- PostgreSQL 16.4 on port 5433 (host) / 5432 (container)
- Database: `accounts`, User: `accounts`, Password: `accounts_dev` (**dev only** — production rejects this password and demo seed)
- Dev auto-migrates + seeds demo data; in production both are gated off by `ProductionSafetyService` and run via a controlled release step
- All enums stored as text (not int) for readability
- snake_case table names, decimal(18,2) for money

## Entities (43 classes, 42 tables)

| Group | Tables |
|-------|--------|
| Tenancy & Users | tenants, user_accounts, user_company_accesses |
| Company | companies (tenant-scoped via `TenantId`), company_officers |
| Periods | accounting_periods, size_classifications, filing_regimes |
| Filing | cro_filing_packages, revenue_filing_packages |
| Banking | bank_accounts, import_batches, imported_transactions, transaction_rules, account_categories |
| Year-End | debtors, creditors, fixed_assets, depreciation_entries, inventories, loans, director_loans, payroll_summaries, tax_balances, dividends |
| Adjustments | adjustments |
| Reports | reports, notes_disclosures |
| Equity | share_capitals |
| Audit | audit_logs |

## Services (35)

Core accounting/statutory services:

| Service | Purpose |
|---------|---------|
| SizeClassificationService | 2-of-3 threshold test, consecutive year rule, micro exclusions |
| FilingRegimeService | Determines filing requirements per regime (Micro/Small/SmallAbridged/Medium/Full) |
| ImportService | Bank CSV import with auto-detection (AIB/BOI/Revolut/Stripe), duplicate detection |
| CategoryService | Default Irish chart of accounts (51 categories), auto-categorisation |
| AdjustmentService | Auto-generates depreciation, accruals, prepayments, stock, tax, retained earnings |
| AuditService | Audit trail logging with JSON snapshots |
| FinancialStatementsService | Trial balance, P&L, balance sheet computation, readiness scoring |
| DocumentGeneratorService | QuestPDF — cover, directors' report, balance sheet, P&L, statutory statement, notes |
| TaxComputationService | Corporation tax bridge (12.5% trading), capital allowances, CT1 support |
| IxbrlService | Inline XBRL financial statements (FRC Irish FRS-102 taxonomy) |
| NotesDisclosureService | Auto-generates regime-aware notes (policies, assets, debtors, creditors, share capital, staff, directors) |
| DeadlineService / FilingWorkflowService | Filing deadlines (Irish public holidays), CRO/Revenue filing workflow + status gates |
| DirectorLoanComplianceService / DirectorsReportService / CharityReportingService | s.236 director-loan checks, directors' report, charity/SORP reporting |

Auth, security & platform services:

| Service | Purpose |
|---------|---------|
| AuthService | Login, PBKDF2-SHA256 (210k) verify, HMAC-signed sessions, lockout, session revocation, password change |
| PasswordVerifier / PasswordHasher | Constant-time password hashing/verification (enumeration- and timing-safe) |
| AuthContext / AuthenticatedIdentity | Reads the authenticated principal; derives reviewer display name + audit user id |
| RoleAuthorizationService | Central role→permission rules (Owner / Accountant / Reviewer / Client) |
| ApiAccessService | Service-to-service API-key guard with per-company scoping + roles |
| BootstrapOwnerService / BootstrapOwnerPasswordPolicy | First-run owner/tenant bootstrap from secret config |
| AuditIntegrityCheckpointService / AuditLogIntegrity | Tamper-evident audit log (signed checkpoints) |
| ProductionSafetyService | Fail-fast validation that blocks unsafe production startup config |

## API Endpoints (~110 total)

### Authentication (4)
| Method | Path | Description |
|--------|------|-------------|
| POST | /api/auth/login | Sign in; sets HTTP-only session + CSRF cookies |
| POST | /api/auth/logout | Clear session |
| GET | /api/auth/me | Current signed-in user + tenant |
| POST | /api/auth/change-password | Self-service password change (rotates session) |

### Core (Program.cs + PeriodStatusEndpoint)

### Core (14 — Program.cs)
| Method | Path | Description |
|--------|------|-------------|
| GET | /health | Health check |
| GET/POST | /api/companies | List / Create company |
| GET/PUT/DELETE | /api/companies/{id} | Get / Update / Delete company |
| GET/POST | /api/companies/{id}/officers | List / Add officer |
| PUT/DELETE | /api/companies/{id}/officers/{oid} | Update / Delete officer |
| GET/POST | /api/companies/{id}/periods | List / Create period |
| GET | /api/companies/{id}/periods/{pid} | Get period |
| PUT | /api/companies/{id}/periods/{pid}/status | Update period status |

### Classification (4)
| PUT | .../periods/{pid}/size-classification | Save size data |
| POST | .../periods/{pid}/classify | Run classification engine |
| POST/GET | .../periods/{pid}/filing-regime | Determine / Get filing regime |

### Banking & Import (14)
| GET/POST/PUT/DELETE | .../bank-accounts | Bank account CRUD |
| POST | .../bank-accounts/{id}/import | CSV import (multipart) |
| GET | .../periods/{pid}/transactions | List transactions (paginated, filterable) |
| PUT | .../transactions/{id}/categorise | Categorise single transaction |
| POST | .../transactions/bulk-categorise | Bulk categorise |
| GET/POST | .../categories | List / Create categories |
| POST | .../categories/seed | Seed default chart of accounts |
| GET/POST/DELETE | .../transaction-rules | Transaction rule CRUD |

### Year-End (42)
Full CRUD for: debtors, creditors, fixed assets, inventory, loans, director loans, payroll (upsert), tax balances (upsert by type), dividends, share capital. Plus year-end summary, notes CRUD (list, generate, update, add, delete).

### Adjustments & Audit (8)
| GET/POST | .../adjustments | List / Create manual adjustment |
| POST | .../adjustments/generate | Auto-generate adjustments |
| PUT | .../adjustments/{id} | Update adjustment |
| POST | .../adjustments/{id}/approve | Approve adjustment |
| DELETE | .../adjustments/{id} | Delete adjustment |
| GET | .../adjustments/summary | Adjustment summary |
| GET | .../audit-log | Audit log (paginated) |

### Financial Statements (4)
| GET | .../statements/trial-balance | Adjusted trial balance |
| GET | .../statements/profit-and-loss | P&L account |
| GET | .../statements/balance-sheet | Balance sheet |
| GET | .../statements/readiness | Completeness + filing readiness score |

### Documents & Revenue (4)
| GET | .../documents/accounts-package | Download accounts PDF |
| GET | .../revenue/tax-computation | Corporation tax computation |
| GET | .../revenue/ct1-support | CT1 form support data |
| GET | .../revenue/ixbrl | Download iXBRL financial statements |

## Frontend Pages (11 routes)

| Route | Purpose |
|-------|---------|
| `/login` | Firm staff login (email + password) |
| `/change-password` | Self-service password change |
| `/health`, `/health/ready` | Liveness + readiness probes |
| `/` | Dashboard — company cards with filing status + quick stats |
| `/companies/new` | 4-step onboarding wizard (legal, structure, address, officers) |
| `/companies/[id]` | Company detail with officers, info cards, period management |
| `/companies/.../periods/[periodId]` | Period workspace (6 tabs: Import, Categorise, Year-End, Adjustments, Statements, Filing) |
| `.../year-end` | Year-end questionnaire — 9 sections of plain-English questions with live CRUD |
| `.../classify` | Size classification interview — input figures, run 2-of-3 test, select regime |
| `.../statements` | Financial statements preview — TB, P&L, BS, Tax Computation in browser |
| `.../notes` | Notes disclosure management — auto-generate, edit, toggle, add custom |
| `.../charity` | Charity / SORP reporting workspace |

## Architecture Patterns

- **Frontend**: Next.js 16 App Router with HeroUI v3 components; `AuthProvider` holds session state and guards routes; a server-side proxy route (`app/api/[...path]/route.ts`) injects the service API key and forwards session cookies
- **Backend**: Minimal API with endpoint groups in Endpoints/ files
- **ORM**: EF Core with primary constructor DbContext
- **DI**: Service + DI pattern (services scoped; `ApiAccessService`/`ProductionSafetyService` singletons)
- **Error handling**: Global ExceptionMiddleware (400 for business rules, 404 for not-found, 500 for server errors)
- **Migrations**: Design-time factory (WDAC blocks QuestPDF.dll at design time)
- **Serialization**: JSON camelCase (ASP.NET Core default), enums as text in DB
- **CORS**: AllowAnyOrigin in dev; explicit `AllowedOrigins` (HTTPS-only) enforced in production

## Authentication, Authorization & Security

The platform enforces firm-user identity and tenant isolation as the production access-control foundation.

- **Two guards**: `ApiAccessMiddleware` (service-to-service API key, per-company scope) **and** signed user sessions (`UserSessionMiddleware`). The frontend proxy injects the API key; the browser carries the session cookie.
- **Sessions**: HTTP-only, `Secure` outside dev, HMAC-SHA256-signed cookie. Payload binds `SessionVersion` + `PasswordLastChangedAt`, so a password change or explicit revoke invalidates all existing sessions. Timing-safe signature comparison.
- **Passwords**: PBKDF2-SHA256 @ 210k iterations; constant-time verify; account lockout after 5 failures in 15 min; ≥20-char policy with mixed character classes.
- **CSRF**: `CsrfProtectionMiddleware` double-submit — a session-bound token must match both the `X-CSRF-Token` header and the CSRF cookie on every mutating `/api` request.
- **Tenancy**: every company is `TenantId`-scoped. `TenantAccessMiddleware` returns **404 (not 403)** for cross-tenant company IDs (no existence disclosure) and also honours per-user `UserCompanyAccess`.
- **Roles** (`RoleAuthorizationService`): `Owner` (all), `Accountant` (working papers), `Reviewer` (approve/review/finalise/filing), `Client` (read-only). Backend is the source of truth; the UI only hides ineligible actions.
- **Audit**: `AuditTrailMiddleware` + tamper-evident `AuditIntegrityCheckpointService` (signed checkpoints). Audit identity comes from the authenticated principal, never caller-supplied names.
- **Fail-fast**: `ProductionSafetyService.ThrowIfUnsafe()` runs at startup and blocks boot on unsafe config (dev DB password, demo seed, non-HTTPS/localhost origins, wildcard hosts, weak/committed session key, insecure cookies, missing bootstrap-owner secrets).

## Production Deployment

- **Stack**: `compose.production.yml` (api + db + frontend) behind a reverse proxy — see `deploy/caddy/Caddyfile.example` for HTTPS ingress.
- **Secrets via files/env** (never committed): `POSTGRES_PASSWORD_FILE`, `ACCOUNTS_CONNECTION_STRING_FILE`, `AUTH_SESSION_SIGNING_KEY_FILE`, `AUDIT_INTEGRITY_SIGNING_KEY_FILE`, `ACCOUNTS_API_KEY_FILE`/`ACCOUNTS_API_KEY_HASH`, `BOOTSTRAP_OWNER_PASSWORD_FILE`; plus `ACCOUNTS_ALLOWED_HOSTS`, `ACCOUNTS_ALLOWED_ORIGIN`, `TRUST_PROXY_HEADERS`, `BOOTSTRAP_TENANT_*`/`BOOTSTRAP_OWNER_*`.
- **Secret file permissions**: containers run as a non-root user and bind-mount the `*_FILE` secrets read-only. In non-swarm `docker compose` the in-container file keeps the host file's mode, so secret files must be readable by that user — keep the secrets directory `0700` and the secret files `0444`.
- **First run**: `BootstrapOwnerService` creates the initial tenant + Owner from secret config; migrations run as a controlled step (not auto-migrate).
- **Ops scripts** (`scripts/`): `backup-postgres.ps1`, `restore-postgres.ps1`, `verify-postgres-backup.ps1`, `smoke-production.ps1`, `verify-production-compose-images.ps1`.
- **CI** (`.github/workflows/ci.yml`): backend build+test, frontend type-check/lint/unit-tests/build, production compose validation, and a full HTTPS production-stack smoke + backup/restore drill.

## Seed Data (3 companies)

1. **Green Valley Community Development CLG** — Micro, CLG, Castlebar Co. Mayo
2. **Connacht Digital Solutions Limited** — Small, LTD, Galway
3. **Atlantic Manufacturing DAC** — Medium, DAC, Shannon Co. Clare

## Legal Framework

- Ireland, Companies Act 2014 (as amended)
- Size thresholds: Micro (€900k/€450k/10), Small (€15m/€7.5m/50), Medium (€50m/€25m/250)
- Configurable via `appsettings.json` SizeThresholds section
- "2 out of 3" threshold test with consecutive year qualification
- Micro exclusions: holding/investment/subsidiary companies
- Filing regimes: Micro (FRS 105), Small, Small Abridged, Medium, Full
- Audit exemption: s.360 Companies Act 2014
- Statutory statements: s.280D (micro), s.352 (abridged), directors' responsibilities
- Corporation tax: 12.5% trading rate, capital allowances s.284 TCA 1997

## Known Issues

- Windows WDAC blocks running DLLs (incl. QuestPDF.dll) from the repo path — build output is routed to `.dotnet-artifacts/` via `backend/Directory.Build.props`; use DesignTimeDbContextFactory for migrations
- NuGet SSL intermittent failures — add packages to .csproj directly and restore
- 2 Postgres-only audit integration tests are skipped under the InMemory provider; they run against a real PostgreSQL service in the CI `Backend` job (`ACCOUNTS_POSTGRES_TEST_CONNECTION`) and in the production-smoke CI job

## Project Stats

| Metric | Count |
|--------|-------|
| Backend .cs files | 148 |
| Entity classes | 43 (+Enums) |
| Services | 35 |
| Middleware | 10 |
| EF migrations | 18 |
| API endpoints | ~110 |
| Backend tests | 463 (461 pass, 2 Postgres-only skipped) |
| Frontend routes | 11 |
| Frontend .tsx/.ts files | 37 |
| Docker services | 3 (api + db + frontend) |
