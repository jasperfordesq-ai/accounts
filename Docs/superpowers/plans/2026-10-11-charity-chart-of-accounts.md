# Charity/CLG Chart of Accounts — Implementation Plan

**Goal:** a selectable charity/CLG chart of accounts whose balances present correctly, with the
statements engine reading explicit per-account attributes instead of code numbers, and a safe way
for an existing company to adopt the chart without moving a transaction.

**Spec:** `Docs/superpowers/specs/2026-10-11-charity-chart-of-accounts-design.md`
**Research inventory (file:line for every code-coupled site):**
`C:\Users\jaspe\AppData\Local\Temp\claude\C--platforms-htdocs-accounts\a9ae11ac-339f-4394-af4a-95570ea5211b\scratchpad\round2\research-chart.md`
(§1 sites, §2 table/endpoints, §3 categorisation, §4 presentation, §5 pinned tests, §6 design).

## Global constraints

- Owner's ground rules: diagnose before editing; never weaken a check; minimal diffs (no
  refactors/reformatting beyond the task); regression tests; run the suite and linter.
- **Do not commit, push, stash, reset or checkout.** Work stays uncommitted on branch
  `fix/e2e-findings`; the controller stages each accepted task.
- Never touch the live server. The bank account number in the owner's chart must never appear in
  code, tests, fixtures or docs.
- Build/test commands and EOL rules: `scratchpad/env-notes-copy.md`.
- Migrations are append-only; nullable columns + backfill; regenerate OpenAPI and the frontend
  generated contract properly when shapes change (never hand-edit generated files).
- Generic companies must be byte-for-byte unchanged in statements, PDFs and CRO fingerprints.

## Tasks

### C1 — Attributes, classification function, migration
Add to `account_categories`: nullable `StatementLine`, `CashFlowClass`, `SystemRole` (text enums),
`IsArchived` (bool default false); to `companies`: `ChartOfAccountsVariant` (text, default
`GenericTrading`). One pure `LegacyCategoryClassification.Derive(type, code)` reproducing today's
rules exactly (research §6(a) rules), plus `CategoryClassification` helpers returning
`category.StatementLine ?? Derive(...)` etc. Migration: pre-flight for duplicate (CompanyId, Code) /
duplicate roles (fail with a clear message), SQL backfill mirroring `Derive`, roles per research
§6(a), partial unique index on (CompanyId, SystemRole). Create paths fill explicit values. Audit
snapshot, `AccountCategoryInput` optional fields, FE type + zod schema, OpenAPI regen.
Tests: exhaustive SQL-vs-C# parity over all seed, auto-created and edge codes; migration drift;
RLS/ownership catalog unaffected (no new tables).

### C2 — Engine switch (no behaviour change)
Replace every code predicate (research §1.1–1.5, §1.10) with the C1 helpers:
FinancialStatementsService (P&L, BS, CF by CashFlowClass, SOCIE, VAT readiness by role,
share-capital readiness by line), AccountingLedgerService (bank control and retained earnings by
role, persistence, IsCash by line), AdjustmentService (find-or-create by role, lazy, Type-safe —
never resolve a code whose existing row has another Type), TaxComputationService,
AccountantWorkingPaperService. **Gate: full backend + frontend suites pass with zero test edits;
golden-corpus CRO fingerprints unchanged.** Dead legacy methods: leave untouched.

### C3 — Chart variants and seeding
`ChartDefinitions.GenericTrading` (today's 52 rows, unchanged) and `CharityClg` (spec §4, generic
bank names). `CategoryService.SeedDefaultCategoriesAsync` and `/categories/seed` read the company's
variant. `CompanyInput`/onboarding accept an optional variant, defaulting to `CharityClg` for a CLG
that is a charitable organisation. FE onboarding: a "Chart of accounts" select pre-filled from type.
Tests: charity-seeded company statements — bank 1200 = cash, 2300 = creditors > 1 yr, 2210 < 1 yr,
3000/3100/3200 members' funds, 8000 depreciation (admin + CF add-back), 8100 interest payable, 8200
admin, 4xxx turnover; prior-year result closes into 3000; ledger needs no 1400/3100 codes;
adjustments post depreciation/prepayments/stock to the right roles.

### C4 — Suspense, hidden accounts, exemption
Readiness blocker when any Suspense-line account is non-nil ("Suspense account {code} has a balance
of €X; clear it before the statements are approved."), only evaluated when such an account exists
(don't change totalChecks). Server-side rejection (400) of archived and BankControl targets for
categorise, bulk-categorise, rule create and opening-balance upsert; `GET /categories` hides
archived rows by default (`?include=all` for TB/admin) but never hides a row with a balance; FE
pickers follow. For a company holding a charitable tax exemption, AdjustmentService posts no CT
provision. Tests for each, incl. re-categorising a categorised transaction and back again (audited,
reversible) and rejection in a finalised period.

### C5 — Charity-variant presentation
Gated on `ChartOfAccountsVariant == CharityClg`: members' funds labels in the PDF and the frontend
statements workbench (no share-capital row; "Members' funds"), an overdrawn bank-control balance
presented within creditors due within one year (never netted), finance-lease split via lines.
Generic companies unchanged (tests prove it). iXBRL untouched.

### C6 — Adopt the charity chart (existing company)
`POST /api/companies/{id}/categories/chart-adoption/plan` (dry run → plan + `planSha256`) and
`POST .../chart-adoption` (execute with `{variant, planSha256, acknowledgeStaleFilingArtifacts}`),
Owner only, recent re-verification. Plan per spec §2.4: rename-in-place only 1400→1200 (BankControl)
and 3100→3000 (RetainedEarnings); clashing rows with postings → `G-<code>` "(legacy)", active;
clashing/unused rows without postings → archived (re-coded only if needed); insert missing charity
rows; set the variant. One transaction under the company lock; two-phase codes; guards: refuse if an
affected account has postings in a Finalised/Filed/locked period; list periods whose CRO artefacts
would go stale and require acknowledgement; never move transactions or create opening balances;
audit `AccountCategoriesChartAdopted` with before/after snapshots + plan hash. FE: company page
"Chart of accounts" card with the plan preview (renames, legacy re-codes, archives, inserts) and a
confirm step. Postgres tests: ids preserved, transactions untouched, permutation, locked refusal,
stale-plan-hash refusal, audit row; and a **live-shape regression fixture** (a fixture mirroring the
live company's shape: six categorised accounts and their transactions; counts, uncategorised and net
movement unchanged after adoption), with all six still selectable as legacy and the charity codes
present after adoption.

### C7 — Runbook
`Docs/operations/charity-chart-adoption.md`: back up; dry-run plan; review; apply; re-categorise and
split in the app (owner's mapping table); verify that counts, uncategorised and net movement are
unchanged and suspense is nil; then enter take-on balances.
