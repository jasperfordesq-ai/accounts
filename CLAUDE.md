# Irish Statutory Accounts Production Platform

Multi-company web application that replaces the traditional year-end accountant workflow for Irish private companies. Takes imported bank transactions + year-end business facts and produces statutory financial statements, CRO filing outputs, and Revenue CT1/iXBRL support.

> Active production-readiness handoff for Claude/agent sessions:
> **[AGENTS.md - Active Goal Handoff](AGENTS.md#active-goal-handoff)**.

## Repository

- GitHub: https://github.com/jasperfordesq-ai/accounts
- Treat this as the canonical repository for the Irish statutory accounts production platform.

## Tech Stack

- **Backend**: ASP.NET Core (.NET 10) Minimal API + EF Core 10 + PostgreSQL 16.4
- **Frontend**: Next.js 16 (App Router) + HeroUI v3 + Tailwind CSS 4
- **PDF Generation**: QuestPDF (server-side)
- **iXBRL**: Custom XHTML generator with FRC Irish FRS taxonomy
- **CSV Import**: CsvHelper (bank statement parsing)
- **Container**: Docker Compose (api + db + frontend)

## Project Structure

```
accounts/
├── backend/
│   ├── Accounts.slnx               # XML solution used for restore/build
│   ├── Directory.Build.props       # Routes build output to ../.dotnet-artifacts (WDAC workaround)
│   └── Accounts.Api/
│       ├── Program.cs              # Minimal API setup, middleware pipeline, core endpoint mappings
│       ├── Entities/               # Accounting, filing, identity, privacy, operations and audit entities
│       ├── Data/
│       │   ├── AccountsDbContext.cs       # DbSets, query filters, interceptors and model composition
│       │   ├── SeedData.cs                # Sample companies + demo tenant/role users (dev only)
│       │   ├── DesignTimeDbContextFactory.cs
│       │   └── Migrations/                # Append-only EF migration history
│       ├── Services/               # Domain, filing, identity, privacy and operations services
│       ├── Middleware/             # Security, auth, tenant, CSRF, audit, concurrency and metrics
│       ├── Rules/                  # Configurable legal thresholds + auth/session/API-access/audit config
│       └── Endpoints/              # Domain-focused endpoint groups + registration
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
# Backend restore/build + executable test gate
cd backend && dotnet build Accounts.slnx
cd backend && dotnet test Accounts.Tests/Accounts.Tests.csproj --configuration Release --no-restore

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
> Windows WDAC blocks running DLLs (incl. QuestPDF) from the repo path. CI restores the solution,
> builds the API, and invokes `Accounts.Tests.csproj` explicitly so a successful command always
> represents an executed test run.
>
> WDAC still blocks a **freshly rebuilt** `Accounts.Api.dll` at test runtime even under `.dotnet-artifacts/`
> (error `0x800711C7`) — so after changing backend code, run tests with artifacts redirected outside the
> repo tree: `dotnet test Accounts.Tests/Accounts.Tests.csproj -c Release -p:ArtifactsPath=$env:TEMP/accts-art`. CI (Linux) is
> unaffected and remains the source of truth.

## Database

- PostgreSQL 16.4 on port 5433 (host) / 5432 (container)
- Database: `accounts`, User: `accounts`, Password: `accounts_dev` (**dev only** — production rejects this password and demo seed)
- Dev auto-migrates + seeds demo data; in production both are gated off by `ProductionSafetyService` and run via a controlled release step
- All enums stored as text (not int) for readability
- snake_case table names, decimal(18,2) for money

## Domain inventory

| Group | Tables |
|-------|--------|
| Tenancy & Users | tenants, user_accounts, user_company_accesses |
| Company | companies (tenant-scoped via `TenantId`), company_officers |
| Periods | accounting_periods, size_classifications, filing_regimes |
| Filing | cro_filing_packages, revenue_filing_packages |
| Banking | bank_accounts, import_batches, imported_transactions, transaction_rules, account_categories |
| Year-End | debtors, creditors, fixed_assets, depreciation_entries, capital_allowance_claims, inventories, loans, director_loans, payroll_summaries, tax_balances, dividends |
| Adjustments | adjustments |
| Reports | reports, notes_disclosures |
| Equity | share_capitals |
| Audit | audit_logs |

## Services

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
| CorporationTaxFilingSupportService / AccountantWorkingPaperService | Bounded CT filing-support evidence and versioned accountant working papers |
| ExternalFilingHandoffService | Append-only, hash-bound external handoff preparation/review/revocation; never direct submission |

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
| IdentityLifecycleService / MfaService | Invitations, resets, unlock/offboard, encrypted TOTP, recovery codes and replay-resistant terminal tokens |
| PrivacyGovernanceService | Subject access, erasure/legal-hold workflow and minimised retention evidence |
| DeadlineReminderService / PlatformMetricsService | Tenant-scoped scheduled delivery, retry/operator queue and PII-safe operational signals |
| SystemReadinessProbeService | Single-flight cached readiness checks for database, migrations and owner bootstrap |

## API endpoints

The committed OpenAPI 3.1 contract is the canonical route and transport-schema inventory:
`backend/Accounts.Api/OpenApi/accounts-api-v1.json`. It currently contains 177 paths and 182
schemas. `Docs/architecture/generated-api-contracts.md` documents regeneration and frontend drift
checks; avoid maintaining a second exhaustive hand-written route count here.

### Authentication (representative routes)
| Method | Path | Description |
|--------|------|-------------|
| POST | /api/auth/login | Sign in; sets HTTP-only session + CSRF cookies |
| POST | /api/auth/logout | Clear session |
| GET | /api/auth/me | Current signed-in user + tenant |
| POST | /api/auth/change-password | Self-service password change (rotates session) |

### Core (Program.cs + PeriodStatusEndpoint)

### Core (representative routes)
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

### Classification (representative routes)
| PUT | .../periods/{pid}/size-classification | Save size data |
| POST | .../periods/{pid}/classify | Run classification engine |
| POST/GET | .../periods/{pid}/filing-regime | Determine / Get filing regime |

### Banking & Import (representative routes)
| GET/POST/PUT/DELETE | .../bank-accounts | Bank account CRUD |
| POST | .../bank-accounts/{id}/import | CSV import (multipart) |
| GET | .../periods/{pid}/transactions | List transactions (paginated, filterable) |
| PUT | .../transactions/{id}/categorise | Categorise single transaction |
| POST | .../transactions/bulk-categorise | Bulk categorise |
| GET/POST | .../categories | List / Create categories |
| POST | .../categories/seed | Seed default chart of accounts |
| GET/POST/DELETE | .../transaction-rules | Transaction rule CRUD |

### Year-End (representative routes)
Full CRUD for: debtors, creditors, fixed assets, inventory, loans, director loans, payroll (upsert), tax balances (upsert by type), dividends, share capital. Plus year-end summary, notes CRUD (list, generate, update, add, delete).

### Adjustments & Audit (representative routes)
| GET/POST | .../adjustments | List / Create manual adjustment |
| POST | .../adjustments/generate | Auto-generate adjustments |
| PUT | .../adjustments/{id} | Update adjustment |
| POST | .../adjustments/{id}/approve | Approve adjustment |
| DELETE | .../adjustments/{id} | Delete adjustment |
| GET | .../adjustments/summary | Adjustment summary |
| GET | .../audit-log | Audit log (paginated) |

### Financial Statements (representative routes)
| GET | .../statements/trial-balance | Adjusted trial balance |
| GET | .../statements/profit-and-loss | P&L account |
| GET | .../statements/balance-sheet | Balance sheet |
| GET | .../statements/readiness | Completeness + filing readiness score |

### Documents & Revenue (representative routes)
| GET | .../documents/accounts-package | Download accounts PDF |
| GET | .../revenue/tax-computation | Corporation tax computation |
| GET | .../revenue/ct1-support | CT1 form support data |
| GET | .../revenue/ixbrl | Download iXBRL financial statements |

Additional endpoint groups cover invitation/reset and MFA workflows, Owner identity lifecycle,
privacy governance, accountant working papers, atomic onboarding, scheduled deadline delivery,
platform metrics, filing-release evidence and recorded external filing handoffs. `/health/ready`
uses a cached, fail-closed database/migration/bootstrap probe. Direct CRO/ROS submission endpoints
and outbound submission clients are intentionally absent.

## Frontend pages

| Route | Purpose |
|-------|---------|
| `/login` | Firm staff login (email + password) |
| `/change-password` | Self-service password change |
| `/accept-invite`, `/reset-password` | Terminal invitation and password-reset flows |
| `/settings/users` | Owner-only identity lifecycle and MFA/session administration |
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
| `.../working-papers` | Versioned accountant working-paper generation and review |
| `/production-readiness` | Restricted machine/human release-evidence workbench |
| `/workbench-preview` | Design-system and visual-QA preview route |

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
- **Sessions**: HTTP-only, `Secure` outside dev, HMAC-SHA256-signed cookie with idle and absolute expiry. Payload binds `SessionVersion` + `PasswordLastChangedAt`; password change, unlock/offboard actions and explicit revocation invalidate existing sessions. Timing-safe signature comparison and replay-resistant terminal tokens are enforced.
- **Passwords and MFA**: PBKDF2-SHA256 @ 210k iterations; constant-time verify; compromised-password rejection; account lockout; ≥20-char policy with mixed character classes; encrypted TOTP enrollment, recovery codes and recent-authentication gates for privileged actions.
- **CSRF**: `CsrfProtectionMiddleware` double-submit — a session-bound token must match both the `X-CSRF-Token` header and the CSRF cookie on every mutating `/api` request.
- **Tenancy**: every company is `TenantId`-scoped. `TenantAccessMiddleware` returns **404 (not 403)** for cross-tenant company IDs and honours per-user `UserCompanyAccess`; forced PostgreSQL RLS is the persistence boundary for the least-privileged application login. See `Docs/architecture/database-tenant-isolation.md`.
- **Roles** (`RoleAuthorizationService`): `Owner` (all), `Accountant` (working papers), `Reviewer` (approve/review/finalise/filing), `Client` (read-only). Backend is the source of truth; the UI only hides ineligible actions.
- **Audit**: `AuditTrailMiddleware` + tamper-evident `AuditIntegrityCheckpointService` (signed checkpoints). Audit identity comes from the authenticated principal, never caller-supplied names.
- **Fail-fast**: `ProductionSafetyService.ThrowIfUnsafe()` runs at startup and blocks boot on unsafe config (dev DB password, demo seed, non-HTTPS/localhost origins, wildcard hosts, weak/committed session key, insecure cookies, missing bootstrap-owner secrets, missing production monitoring).

## Production Deployment

- **Stack**: `compose.production.yml` (api + db + frontend) behind a reverse proxy — see `deploy/caddy/Caddyfile.example` for HTTPS ingress.
- **Secrets via files/env** (never committed): separate `POSTGRES_PASSWORD_FILE`/`POSTGRES_APPLICATION_PASSWORD_FILE` credentials, `ACCOUNTS_MIGRATION_CONNECTION_STRING_FILE` (migration job only), `ACCOUNTS_APPLICATION_CONNECTION_STRING_FILE` (least-privileged API only), `DATABASE_TENANT_CONTEXT_KEY_FILE`, `AUTH_SESSION_SIGNING_KEY_FILE`, `AUDIT_INTEGRITY_SIGNING_KEY_FILE`, `IDENTITY_HMAC_KEY_FILE`, `MFA_ENCRYPTION_KEY_FILE`, `DEADLINE_PROVIDER_TOKEN_FILE`, `ACCOUNTS_API_KEY_FILE`/`ACCOUNTS_API_KEY_HASH`, and `BOOTSTRAP_OWNER_PASSWORD_FILE`; plus `ACCOUNTS_ALLOWED_HOSTS`, `ACCOUNTS_ALLOWED_ORIGIN`, `TRUST_PROXY_HEADERS`, `MONITORING_ERROR_TRACKING_DSN`, `MFA_ENCRYPTION_ACTIVE_KEY_ID`, `DEADLINE_DELIVERY_PROVIDER_ENDPOINT`, and `BOOTSTRAP_TENANT_*`/`BOOTSTRAP_OWNER_*`.
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
- PostgreSQL-required integration suites are release gates, not optional evidence. Set
  `ACCOUNTS_POSTGRES_TEST_CONNECTION` for the direct test-project command; CI supplies it and also
  sets the golden-corpus requirement so an environment skip cannot masquerade as a release pass.

## Generated/current inventory

- Transport inventory: committed OpenAPI 3.1 contract (177 paths, 182 schemas at this revision).
- Database inventory: EF model plus append-only migrations; migration/model drift is tested rather
  than represented by a manually maintained count.
- Test inventory: use test-run output and retained CI evidence for the exact candidate; historical
  counts in handoff logs are not a current release assertion.
- Runtime topology: PostgreSQL, migrate-only job, least-privileged API, frontend and TLS ingress as
  defined by `compose.production.yml`.
