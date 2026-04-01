# Irish Statutory Accounts Production Platform

Multi-company web application that replaces the traditional year-end accountant workflow for Irish private companies. Takes imported bank transactions + year-end business facts and produces statutory financial statements, CRO filing outputs, and Revenue CT1/iXBRL support.

## Tech Stack

- **Backend**: ASP.NET Core (.NET 10) Minimal API + EF Core 9 + PostgreSQL 16.4
- **Frontend**: React 18 + Vite + TypeScript + Tailwind CSS 4
- **PDF Generation**: QuestPDF (server-side)
- **iXBRL**: Custom XHTML generator with FRC Irish FRS taxonomy
- **CSV Import**: CsvHelper (bank statement parsing)
- **Container**: Docker Compose (api + db)

## Project Structure

```
accounts/
├── backend/
│   ├── Accounts.sln
│   └── Accounts.Api/
│       ├── Program.cs              # Minimal API setup + core endpoint mappings
│       ├── Entities/               # 26 entity classes + Enums.cs
│       ├── Data/
│       │   ├── AccountsDbContext.cs       # 25 DbSets, full OnModelCreating config
│       │   ├── SeedData.cs                # 3 sample companies (micro/small/medium)
│       │   ├── DesignTimeDbContextFactory.cs
│       │   └── Migrations/
│       ├── Services/               # 10 business logic services
│       ├── Rules/                  # Configurable legal threshold config
│       └── Endpoints/              # 7 endpoint group files + registration
├── frontend/
│   ├── src/
│   │   ├── pages/                  # Dashboard, CompanyOnboarding, CompanyDetail, PeriodWorkspace
│   │   ├── components/             # Layout
│   │   └── api/                    # Axios client + companies API
│   └── vite.config.ts
├── compose.yml                     # Docker: api (5090) + postgres (5433)
├── Dockerfile.backend
├── REQUIREMENTS.md                 # Full product requirements
└── CLAUDE.md
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
# Frontend: http://localhost:5173 (proxies /api to backend)
```

## Database

- PostgreSQL 16.4 on port 5433 (host) / 5432 (container)
- Database: `accounts`, User: `accounts`, Password: `accounts_dev`
- Auto-migrates on startup + seeds 3 sample companies
- All enums stored as text (not int) for readability
- snake_case table names, decimal(18,2) for money

## Entities (26 classes, 28 tables)

| Group | Tables |
|-------|--------|
| Company | companies, company_officers |
| Periods | accounting_periods, size_classifications, filing_regimes |
| Filing | cro_filing_packages, revenue_filing_packages |
| Banking | bank_accounts, import_batches, imported_transactions, transaction_rules, account_categories |
| Year-End | debtors, creditors, fixed_assets, depreciation_entries, inventories, loans, director_loans, payroll_summaries, tax_balances, dividends |
| Adjustments | adjustments |
| Reports | reports, notes_disclosures |
| Audit | audit_logs |

## Services (10)

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

## API Endpoints (80 total)

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

### Year-End (32)
Full CRUD for: debtors, creditors, fixed assets, inventory, loans, director loans, payroll (upsert), tax balances (upsert by type), dividends. Plus year-end summary endpoint.

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

## Architecture Patterns

- Minimal API with endpoint groups in Endpoints/ files
- EF Core with primary constructor DbContext
- Service + DI pattern (all 10 services registered as scoped)
- Design-time factory for migrations (WDAC blocks QuestPDF.dll)
- JSON: camelCase (ASP.NET Core default), enums as text in DB
- CORS configured for frontend dev server (localhost:5173)

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
- PeriodWorkspace frontend tabs are placeholder stubs — backend endpoints are fully functional
