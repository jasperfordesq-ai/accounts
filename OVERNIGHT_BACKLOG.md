# Overnight Completion Backlog — Irish Statutory Accounts Platform

> Generated 2026-06-20 from a 7-agent grounded mapping of the whole stack (accounting
> engine, output generators, data/API, frontend, security/platform, tests). Every item
> below was found by reading source with file:line evidence. This file is the **single
> source of truth** for the overnight session. Work top-down by severity.

## How to use this file
1. Read this whole file first.
2. Work the backlog in the order given in **Overnight plan** (blockers first).
3. For each item: reproduce/understand → implement → add a test → build+test green → commit → tick it off in `NIGHTLY_LOG.md`.
4. Never claim an item done without a passing test. Never merge to `main`. Respect the **Human decisions** list — do not make those calls yourself; flag them.

---

## Architecture mapped? — verdict: MOSTLY MAPPED
The full vertical stack is understood at the service-and-endpoint level; the two
highest-stakes findings (balance-sheet disconnected sources; passive-income loss
shelter) were verified directly in source. Remaining blindspots (do NOT assume these
are fine — confirm if you touch them):
- **No live test run** was done during mapping (Windows WDAC blocks rebuilt DLLs). All correctness findings are static. CI on Linux is the source of truth — validate every fix.
- **iXBRL/FRC FRS-102 conformance** was not validated against a real ROS/FRC schema (only string-contains checks exist). True gap to ROS-acceptance is estimated, not measured.
- **Medium/Full PDF rendering** was read by code path, not generated — confirm by generating.
- **Filing-package + charity service flows** were not runtime-smoked; orphaned service methods behind unmapped routes not fully ruled out.
- **Company-scoped FixedAssets/Loans/ShareCapital** date-window period membership not exercised with boundary data.
- **CSV column-index mappings** for AIB/BOI/Revolut/Stripe not validated against real sample files.

## State of the platform (honest summary)
Advanced build, well past prototype. The double-entry trial-balance core is correct by
construction; recent tax fixes (short-period capital allowances, s.21A 25% non-trading
rate, trading-loss carry-forward, other-income inclusion) are real and regression-tested.
Security/tenancy/audit/production-safety is mature defense-in-depth — no IDOR, CSRF, or
session-fixation holes found. Data layer is clean (no pending migrations, all services
reachable, tenant scoping consistent across 134 routes). Frontend covers almost every
primary workflow end-to-end.

**But it is NOT filing-grade yet.** The gaps concentrate in (a) correctness of the
statutory engine, (b) completeness of filing outputs for half the regimes, and (c) data
that cannot be entered through the UI. Specifically: the balance sheet computes net
assets from entity tables + raw bank cash while computing reserves from the P&L (two
disconnected sources, so correct mixed cash/accrual accounts routinely fail to balance
and are hard-blocked from filing); Medium/Full regimes silently render the Small PDF and
omit Cash Flow, SOCIE and an Auditor's Report (which doesn't exist anywhere); iXBRL is a
single-year skeleton; trading losses silently shelter passive income from the 25% charge
(under-taxes a filing); and loans/director-loans/share-capital have full backend CRUD but
no UI or API client, so any company with borrowings can't populate what the statements
depend on.

## Definition of done (v1 filing-grade)
- A realistic mixed cash/accrual scenario (sales + expenses + debtors + creditors + accruals + depreciation + loans) **balances** with `UnexplainedDifference == 0`, proven by an end-to-end test.
- Every regime's PDF section set matches `FilingRegimeService.GetRequiredStatements/GetRequiredNotes` (incl. Cash Flow + SOCIE for Medium/Full).
- An Independent Auditor's Report section renders when `AuditExempt == false`, suppressed when true (placeholder for a human auditor — see Human decisions).
- CT correct in the high-stakes direction: passive income taxed at 25% even under a trading loss (absent an elected claim); capital allowances from persisted per-asset claims.
- iXBRL emits prior-year comparatives + entity/report metadata, parses as well-formed XML, and tagged values equal the statement figures (ROS sign-off is a human gate).
- Loans (+ snapshots), director loans, and share capital are CRUD-able from the UI and flow into statements/disclosures/adjustments.
- Adjustment engine generates all section-E types (at least loan-interest accrual + director-loan reclassification added).
- Role-based UI gating uses `canWriteWorkingPapers`/`canReview`.
- The 2 Postgres-only audit tests run + pass in CI; CLAUDE.md route/CI claims reconciled.
- Filing-critical paths have figure-level tests (CSV detection, chart of accounts, s.239 boundary, Medium/Full PDFs).

---

## Backlog

### 🔴 Blockers
| ID | Title | Area | Est | Files |
|----|-------|------|-----|-------|
| BL-01 | Balance sheet net-assets vs capital from disconnected sources → correct accounts fail to balance & are hard-blocked | accounting | L | FinancialStatementsService.cs |
| BL-02 | Medium/Full render identical Small PDF — omit Cash Flow + SOCIE | outputs | M | DocumentGeneratorService.cs, FilingRegimeService.cs |
| BL-03 | No Auditor's Report generator — audited companies can't produce a pack | outputs | M | DocumentGeneratorService.cs, FilingRegimeService.cs |
| BL-04 | Trading losses silently shelter non-trading (Case III/V) income from 25% — under-taxes | tax | S | TaxComputationService.cs |
| BL-05 | No UI/API client for loans, director loans, share capital — statements/disclosures unpopulatable | frontend | L | year-end/page.tsx, lib/api.ts, YearEndEndpoints.cs |

### 🟠 Major
| ID | Title | Area | Est | Files |
|----|-------|------|-----|-------|
| BL-06 | Capital allowances re-estimate prior claims from period length, not actual claims | tax | M | TaxComputationService.cs |
| BL-07 | Missing section-E adjustments: loan-interest accrual, director-loan reclassification | accounting | M | AdjustmentService.cs |
| BL-08 | iXBRL omits prior-year comparatives — ROS/CRO rejects without them | outputs | M | IxbrlService.cs |
| BL-09 | iXBRL lacks entity/report metadata + broad FRS-102 tagging; no numeric/schema validation | outputs | M | IxbrlService.cs, tests |
| BL-10 | Tenant isolation has no DB-level backstop (no EF global query filter) | security | M | AccountsDbContext.cs, TenantAccessMiddleware.cs |
| BL-11 | Role-based UI gating not implemented — Reviewer/Client see all mutation controls | frontend | M | AuthProvider.tsx, period/company pages |
| BL-12 | Charity SORP outputs are data-only — no Trustees' Report/SoFA PDF | outputs | M | CharityReportingService.cs, CharityEndpoints.cs |
| BL-13 | No regime-specific notes for Medium/Full beyond Small set | outputs | M | NotesDisclosureService.cs, FilingRegimeService.cs |
| BL-14 | CSV format auto-detection (AIB/BOI/Revolut/Stripe) completely untested | tests | S | ImportService.cs, tests |
| BL-15 | 2 Postgres-only audit-durability tests silently skipped in CI (CLAUDE.md says they run) | tests | S | AuditTrailPostgresIntegrationTests.cs, ci.yml |
| BL-16 | Director-loan s.239 10%-net-assets threshold has no boundary test (and confirm s.236 vs s.239) | tests | S | DirectorLoanComplianceService.cs, tests |
| BL-17 | Default chart of accounts + confidence-scored auto-categorisation essentially untested | tests | S | CategoryService.cs, tests |
| BL-18 | Medium/Full PDF templates have no generation test; PDF content never inspected | tests | S | DocumentGeneratorService.cs, tests |
| BL-19 | No frontend component or e2e tests | tests | M | frontend/scripts, package.json |

### 🟡 Minor
| ID | Title | Area | Est | Files |
|----|-------|------|-----|-------|
| BL-20 | Retained-earnings opening recomputed recursively (O(n²)); roll-forward adjustment is a zero placeholder | accounting | M | FinancialStatementsService.cs, AdjustmentService.cs |
| BL-21 | Reducing-balance depreciation uses 1/UsefulLifeYears, never fully writes asset down | accounting | S | AdjustmentService.cs |
| BL-22 | First-year share-capital opening defaults to 0, mis-states SOCIE & disagrees with BS | accounting | S | FinancialStatementsService.cs |
| BL-23 | Cash-flow working capital mixes prepayments into debtors, ignores creditor classification, unreconciled to BS cash | accounting | M | FinancialStatementsService.cs |
| BL-24 | Approval/signature dates use DateTime.Now at render, not a persisted board-approval date | outputs | S | DocumentGeneratorService.cs, NotesDisclosureService.cs |
| BL-25 | UI create+delete but no update (PUT) for year-end rows; Dividends lacks a PUT endpoint | frontend | M | lib/api.ts, year-end/page.tsx, YearEndEndpoints.cs |
| BL-26 | Statements not click-through drillable to source (data exists via getStatementSources) | frontend | M | statements/page.tsx, lib/api.ts |
| BL-27 | Company-scoped FixedAssets/Loans/ShareCapital infer period membership by date heuristics — no boundary tests | data-api | M | YearEndEndpoints.cs, FinancialStatementsService.cs |
| BL-28 | Uneven input validation + entity over-posting on year-end/banking create endpoints | data-api | M | YearEndEndpoints.cs, BankingEndpoints.cs |
| BL-29 | Orphaned `reports` table, deprecated `mark-generated` route (410), CLAUDE.md route drift | data-api | S | Report.cs, AccountsDbContext.cs, FilingWorkflowEndpoints.cs, CLAUDE.md |
| BL-30 | Production API key is a single unscoped Admin secret, no rotation runbook | security | S | compose.production.yml, ApiAccessService.cs |
| BL-31 | Login rate-limit trusts client XFF; backend emits no CSP/HSTS | security | S | RateLimitClientKey.cs, SecurityHeadersMiddleware.cs |
| BL-32 | Missing figure-level tests for prepayments, RCT/PAYE, capital-vs-revenue, retained-earnings adjustments | tests | M | AdjustmentService.cs, tests |

> Full per-item detail (`why` + verifiable `acceptance` criteria) lives in the mapping
> output the synthesis was built from. Each acceptance criterion is written so a test can
> prove it. Where an item below is terse, derive the test from the Definition of done.

### Acceptance detail for the blockers (most important)
- **BL-01** — Either route current assets/liabilities/cash through the same trial-balance movements that drive the P&L (single source of truth), **or** post every year-end entity row (debtor, creditor, accrual, prepayment, stock, loan) as a balancing trial-balance entry. New end-to-end test seeds a realistic mixed cash/accrual set and asserts `balanceSheet.Balances == true` and `UnexplainedDifference == 0`. **If the single-source refactor is too large to finish safely overnight, take the balancing-adjustments approach and leave a clearly-marked note for human review rather than half-applying a refactor.** (See Human decisions — Jasper should ratify the direction; the conservative, well-tested path is acceptable to land overnight.)
- **BL-02** — For `ElectedRegime` Medium/Full, `GenerateAccountsPackageAsync` renders Cash Flow + SOCIE sections from the existing `FinancialStatementsService` outputs; a test asserts each `GetRequiredStatements` heading appears in the PDF.
- **BL-03** — When `AuditExempt == false`, include an Independent Auditor's Report section (opinion, basis, respective responsibilities, auditor name/signature **placeholder**). Test: present for an audited period, absent for an audit-exempt one.
- **BL-04** — Non-trading income charged at 25% to the extent positive even under a trading loss (absent an elected loss-relief claim); trading loss surfaced for carry-forward. Test: trading loss + rental profit → `CorporationTaxAt25 > 0` and loss carried forward.
- **BL-05** — User can add/edit/delete loans (+ per-period snapshots), director loans, and share capital from the UI via wired `api.ts` functions; data reaches the year-end summary, creditors, equity-changes, director-loan compliance/s.307 note, and the loan-interest adjustment (BL-07).

---

## Overnight plan (execution order)
1. **Baseline**: `cd backend && dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art` and `cd frontend && npm run build && npm run lint`. Record pass counts in `NIGHTLY_LOG.md` so any regression is attributable.
2. **BL-04** (smallest blocker, highest filing risk) → test → commit.
3. **BL-01** (structural blocker; the big one) → end-to-end balance test → commit. Fall back to balancing-adjustments + note if the refactor is too large.
4. **BL-06 → BL-07** (capital-allowance persisted field + EF migration via DesignTimeDbContextFactory; then loan-interest + director-loan reclassification adjustments). Fold in BL-32 figure-level assertions. Migration check, build+test, commit.
5. **BL-02 + BL-03** together, then **BL-18** (Medium/Full generation tests) and **BL-13** (Medium/Full notes). Commit.
6. **BL-08 + BL-09** (iXBRL comparatives + metadata + XML-parsing numeric test). Commit. ROS conformance stays a human gate.
7. **BL-05** (frontend blocker: loans/director-loans/share-capital UI + api.ts), then **BL-11** (role gating) and **BL-25** (inline PUTs + dividends PUT). `npm run build && lint` + new component tests. Commit.
8. **BL-10** (EF global query filter or architecture-guard test). Commit.
9. **CI/test integrity**: BL-15 (Postgres in CI), BL-14 (CSV detection), BL-16 (s.239 boundary), BL-17 (chart + confidence). Commit each cluster.
10. **Correctness minors**: BL-20, BL-21, BL-22, BL-23, BL-24 — each with a test, committed individually.
11. **Hygiene**: BL-28, BL-29, BL-27, BL-30, BL-31, BL-19, BL-26 as time allows. Commit incrementally.
12. **Final pass**: full backend test + frontend build/lint/tests; confirm `dotnet ef migrations has-pending-model-changes` reports no changes; update CLAUDE.md stats; write the handover note. Do **not** merge to main.

---

## Human decisions — DO NOT make these yourself; implement the conservative default and FLAG them in `NIGHTLY_LOG.md`
1. **iXBRL/ROS sign-off** — only Jasper/a registered agent can run the instance through real ROS/CRO validation. Ship v1 iXBRL as "support output, externally validated".
2. **Audit-engagement scope (BL-03)** — render a placeholder/template auditor's report for a human auditor to complete; do not fabricate an opinion. (Or audited companies are out of v1 scope — Jasper decides.)
3. **Loss-relief policy (BL-04)** — default to taxing passive income at 25% unless an elected s.396A/value-basis claim; do not auto-apply loss relief. Expose the election later.
4. **s.236 vs s.239 (BL-16)** — code constant says s.239, docs say s.236. Implement the test against the coded 10%-of-net-assets logic but FLAG the section discrepancy for legal confirmation.
5. **Filing-grade scope for v1** — which regimes/entities ship first (e.g. Micro + Small audit-exempt LTD) decides whether BL-02/03/12/13 are v1 blockers or deferred. Default: fix them (they're cheap once data flows), but flag if scope is narrower.
6. **Balance-sheet model (BL-01)** — single-source refactor vs balancing-adjustments is an architectural fork; land the safe path, flag the long-term decision.
7. **Charity output standard (BL-12)** — confirm exact Charities SORP (FRS-102) tier + Charities Regulator format for the seeded CLG.
8. **Security posture (BL-10/BL-30)** — per-company-scoped API keys + DB query filter now vs convention + architecture-guard test. Land the guard test; flag the bigger investment.
9. **Professional review** — before any output is filed for a real client, a qualified accountant must review a full generated pack. This is a professional-liability gate an agent cannot discharge.
