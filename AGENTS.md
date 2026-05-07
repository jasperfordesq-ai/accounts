# Irish Statutory Accounts Production Platform

Multi-company web application that replaces the traditional year-end accountant workflow for Irish private companies. Takes imported bank transactions + year-end business facts and produces statutory financial statements, CRO filing outputs, and Revenue CT1/iXBRL support.

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
│   ├── Accounts.sln
│   └── Accounts.Api/
│       ├── Program.cs              # Minimal API setup + core endpoint mappings
│       ├── Entities/               # 27 entity classes + Enums.cs
│       ├── Data/
│       │   ├── AccountsDbContext.cs       # 26 DbSets, full OnModelCreating config
│       │   ├── SeedData.cs                # 3 sample companies (micro/small/medium)
│       │   ├── DesignTimeDbContextFactory.cs
│       │   └── Migrations/
│       ├── Services/               # 11 business logic services
│       ├── Middleware/              # Global exception handler
│       ├── Rules/                  # Configurable legal threshold config
│       └── Endpoints/              # 7 endpoint group files + registration
├── frontend/
│   ├── next.config.ts
│   └── src/
│       ├── app/                    # Next.js App Router pages
│       │   ├── page.tsx            # Dashboard — company listing
│       │   ├── layout.tsx          # Root layout with HeroUI providers
│       │   ├── providers.tsx       # HeroUI RouterProvider
│       │   ├── companies/
│       │   │   ├── new/page.tsx    # 4-step onboarding wizard
│       │   │   └── [id]/page.tsx   # Company detail + period management
│       │   └── companies/[companyId]/periods/[periodId]/
│       │       ├── page.tsx        # Period workspace (6 tabs)
│       │       ├── year-end/       # Year-end questionnaire (9 sections)
│       │       ├── classify/       # Size classification interview
│       │       ├── statements/     # Financial statements preview (TB, P&L, BS, Tax)
│       │       └── notes/          # Notes disclosure management
│       ├── components/
│       │   └── AppNavbar.tsx       # HeroUI navbar
│       └── lib/
│           └── api.ts              # Typed API layer (native fetch, 70+ functions)
├── compose.yml                     # Docker: api (5090) + postgres (5433) + frontend (3000)
├── Dockerfile.backend
├── Dockerfile.frontend
├── REQUIREMENTS.md                 # Full product requirements
└── AGENTS.md
```

## Development Commands

```bash
# Backend build
cd backend && dotnet build

# Frontend build
cd frontend && npm run build

# Run backend locally (needs PostgreSQL on port 5433)
cd backend/Accounts.Api && dotnet run

# Run frontend dev server
cd frontend && npm run dev

# Docker (full stack)
docker compose up -d

# EF Core migrations
cd backend/Accounts.Api
dotnet ef migrations add <Name> --output-dir Data/Migrations

# API: port 5090 (Docker) or 5080 (dotnet run)
# Swagger: http://localhost:5090/swagger (dev only)
# Frontend: http://localhost:3000 (Next.js)
```

## Database

- PostgreSQL 16.4 on port 5433 (host) / 5432 (container)
- Database: `accounts`, User: `accounts`, Password: `accounts_dev`
- Auto-migrates on startup + seeds 3 sample companies
- All enums stored as text (not int) for readability
- snake_case table names, decimal(18,2) for money

## Entities (27 classes, 29 tables)

| Group | Tables |
|-------|--------|
| Company | companies, company_officers |
| Periods | accounting_periods, size_classifications, filing_regimes |
| Filing | cro_filing_packages, revenue_filing_packages |
| Banking | bank_accounts, import_batches, imported_transactions, transaction_rules, account_categories |
| Year-End | debtors, creditors, fixed_assets, depreciation_entries, inventories, loans, director_loans, payroll_summaries, tax_balances, dividends |
| Adjustments | adjustments |
| Reports | reports, notes_disclosures |
| Equity | share_capitals |
| Audit | audit_logs |

## Services (11)

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

## API Endpoints (~90 total)

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

## Frontend Pages (9 routes)

| Route | Purpose |
|-------|---------|
| `/` | Dashboard — company cards with filing status + quick stats |
| `/companies/new` | 4-step onboarding wizard (legal, structure, address, officers) |
| `/companies/[id]` | Company detail with officers, info cards, period management |
| `/companies/.../periods/[periodId]` | Period workspace (6 tabs: Import, Categorise, Year-End, Adjustments, Statements, Filing) |
| `.../year-end` | Year-end questionnaire — 9 sections of plain-English questions with live CRUD |
| `.../classify` | Size classification interview — input figures, run 2-of-3 test, select regime |
| `.../statements` | Financial statements preview — TB, P&L, BS, Tax Computation in browser |
| `.../notes` | Notes disclosure management — auto-generate, edit, toggle, add custom |

## Architecture Patterns

- **Frontend**: Next.js 16 App Router with HeroUI v3 components (Button, Card, Chip, Tabs, TextField, Checkbox, ProgressBar, Spinner)
- **Backend**: Minimal API with endpoint groups in Endpoints/ files
- **ORM**: EF Core with primary constructor DbContext
- **DI**: Service + DI pattern (all 11 services registered as scoped)
- **Error handling**: Global ExceptionMiddleware (400 for business rules, 500 for server errors)
- **Migrations**: Design-time factory (WDAC blocks QuestPDF.dll at design time)
- **Serialization**: JSON camelCase (ASP.NET Core default), enums as text in DB
- **CORS**: Configured for frontend dev server

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

- Windows WDAC blocks QuestPDF.dll — use DesignTimeDbContextFactory for migrations
- NuGet SSL intermittent failures — add packages to .csproj directly and restore

## Project Stats

| Metric | Count |
|--------|-------|
| Backend .cs files | 58 |
| Entity classes | 27 (+Enums) |
| Services | 11 |
| API endpoints | ~90 |
| Frontend routes | 9 |
| Frontend .tsx/.ts files | 12 |
| API client functions | 70+ |
| Seed companies | 3 (micro/small/medium) |
| EF migrations | 2 |
| Docker services | 3 (api + db + frontend) |
