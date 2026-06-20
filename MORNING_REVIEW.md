# Morning Review — Overnight Completion Run

**Branch:** `nightly/completion-2026-06-20` (NOT merged — left for your review)
**Run:** 2026-06-20 → 2026-06-21, autonomous, against `OVERNIGHT_BACKLOG.md`
**Net diff vs `main`:** 24 files changed, ~5,022 insertions, ~94 deletions, 17 commits.

## Headline result
- **Backend tests: 468 → 505 passing (+37), 2 skipped, 0 failing.** The 2 skips are the
  Postgres-only audit-durability tests; they now run against a real PostgreSQL service in
  the CI `Backend` job (BL-15) — locally they still skip on the InMemory provider.
- **Frontend:** `lint`, `tsc --noEmit`, `build`, and all verify scripts (incl. the new
  `test:api-client`) are clean.
- **EF model:** `dotnet ef migrations has-pending-model-changes` is clean (one new migration,
  `AddCapitalAllowanceClaims`).
- The tree was kept green after **every** committed item.

> ⚠️ **Professional-liability gate (Human decision #9):** none of this discharges the
> requirement that a qualified accountant review a full generated pack before anything is
> filed for a real client. These changes improve correctness and completeness; they are not a
> substitute for professional sign-off.

## Definition-of-done scorecard (the 5 blockers + key DoD items)
- ✅ Mixed cash/accrual scenario **balances** (`UnexplainedDifference == 0`) — BL-01.
- ✅ Medium/Full PDFs render Cash Flow + SOCIE; Auditor's Report renders when not audit-exempt — BL-02/03/18.
- ✅ Passive income taxed at 25% even under a trading loss — BL-04.
- ✅ Capital allowances from persisted per-asset claims — BL-06.
- ✅ iXBRL: prior-year comparatives + entity/report metadata, parses as well-formed XML, tagged values == statement figures — BL-08/09.
- ✅ Loan-interest accrual + director-loan reclassification adjustments — BL-07.
- ⚠️ Loans/director-loans/share-capital CRUD: **API client + backend done & tested; year-end page UI deferred** — BL-05 (see Undone).
- ✅ Role-gating helpers exist in the client; **UI wiring of role gating deferred** — BL-11 (see Undone).
- ✅ The 2 Postgres tests run in CI; CLAUDE.md reconciled — BL-15/BL-29.

---

## Commits (oldest → newest), each with its test

| Commit | BL | Summary | Test(s) added |
|--------|----|---------|---------------|
| `b2ac31b` | — | add overnight backlog (source of truth) | n/a |
| `d0e2f16` | BL-04 | tax passive income at 25% even under a trading loss | `TaxComputation_TradingLossDoesNotShelterNonTradingIncomeFrom25Percent` |
| `de9e552` | BL-01 | post trade debtors/creditors so mixed accounts balance | `BalanceSheet_MixedCashAccrualScenario_BalancesWithZeroUnexplainedDifference` |
| `ad7bb57` | BL-06 | capital allowances from persisted per-asset claims (+migration) | `CapitalAllowances_ClaimPersistedWhenAdjustmentsGenerated`, `CapitalAllowances_CapCumulativeClaimUsingPersistedPriorClaims` |
| `7ada0cc` | BL-07 | loan-interest accrual + director-loan reclassification | `Adjustments_AccrueLoanInterestAndKeepBalanceSheetBalanced`, `Adjustments_ReclassifyOverdrawnDirectorLoanAsReceivable` |
| `afb4537` | BL-02/03/18 | Cash Flow, SOCIE, Auditor's Report for Medium/Full | `AccountsPackage_MediumAndFullIncludeEveryRequiredPrimaryStatement`, `AccountsPackage_SmallAuditExemptOmits…`, `AccountsPackage_AuditorsReportTogglesWithAuditExemption`, `MediumAccountsPdf_RendersExtraStatementsAndIsLargerThanSmall` |
| `ca31277` | BL-13 | Medium/Full regime disclosure notes | `Notes_MediumRegimeAddsFullerDisclosureSetBeyondSmall` |
| `2b15ae5` | BL-08/09 | iXBRL prior-year comparatives + metadata + well-formed XML | `Ixbrl_EmitsPriorYearComparativesEntityMetadataAndWellFormedXml` |
| `6ecbaef` | BL-05(client)/25 | loan/director-loan/share-capital client + dividend PUT | `UpdateDividend_PersistsChangesAndLogsAudit` + frontend `verify-api-client.mjs` |
| `7929cea` | BL-14/15/16/17 | CSV detection, chart of accounts, s.239 boundary; Postgres in CI | `ImportService_DetectsBankFormatFromHeader`(×5), `ImportService_AutoDetectsRevolut…`, `CategoryService_SeedsDefaultIrishChartOfAccounts`, `CategoryService_AutoCategorisesByRuleThenFuzzyNameWithConfidence`, `DirectorLoanCompliance_TenPercentNetAssetsThresholdBoundary`(×3) |
| `67e1ff6` | BL-21 | reducing-balance assets fully write down | `Depreciation_ReducingBalanceFullyWritesDownByEndOfUsefulLife` |
| `c4c4e91` | BL-22 | first-year SOCIE opening uses incorporation capital | `EquityChanges_FirstYearShowsIncorporationCapitalAsOpeningNotIssuedInYear` |
| `ea3bd50` | BL-31 | backend CSP/HSTS + rate-limit XFF guard | `SecurityHeaders_EmitCspOnApiAndHstsOverHttps`, `RateLimitClientKey_IgnoresForwardedForUnlessExplicitlyTrusted` |
| `b809449` | BL-32 | figure-level: prepayments, RCT/PAYE, capex, retained earnings | `Adjustments_PrepaymentIncreases…`, `BalanceSheet_IncludesPayeAndRct…`, `ProfitAndLoss_TreatsCapexAsCapital…`, `BalanceSheet_RollsPriorPeriodProfit…` |
| `30a209e` | BL-10/27 | tenant-isolation guard + period-membership boundaries | `TenantIsolation_CompanyAndPeriodAccessIsScopedToCallersTenant`, `FixedAssets_PeriodMembership…`, `ShareCapital_PeriodMembership…` |
| `f5704ee` | BL-30 | service API key rotation runbook | `ApiKeyRotationRunbook_DocumentsHashAndZeroDowntimeRotation` |
| `57d3ace` | BL-29 | reconcile CLAUDE.md stats + nightly log | n/a (doc) |

All listed tests were run and observed passing; the full suite was green (505/507) after the
final commit.

---

## Human-decision items flagged (NOT decided autonomously)
These were implemented with the conservative default and are flagged for your ratification:

1. **#1 iXBRL / ROS sign-off** — v1 iXBRL ships as a *support output*. Real ROS/CRO schema
   validation is a human gate; only well-formedness + value-equality are machine-verified.
2. **#2 Auditor's report** — rendered as a clearly-marked **TEMPLATE** for a Registered Auditor
   to complete; no opinion is fabricated. (Audited companies in v1 scope is your call.)
3. **#3 Loss-relief policy** — passive income taxed at 25% by default; **no auto-applied
   s.396A/value-basis claim**. The election is not yet exposed.
4. **#4 s.236 vs s.239** — the director-loan code constant/warning say **s.239**; docs say
   **s.236**. Test asserts the coded s.239 behaviour. **Needs legal confirmation of the section.**
5. **#6 Balance-sheet model** — landed the **conservative balancing-adjustments path** (trade
   debtors→turnover, trade creditors→expense), NOT the single-source-of-truth refactor. The
   architectural fork is yours to ratify.
6. **#8 Security posture** — landed the tenant-isolation **guard test**; a DB-level **EF global
   query filter** and **per-company-scoped API keys** are the bigger investments, deferred.
7. **CSV column-index blindspot (BL-14)** — detection-by-keyword is tested, but the **AIB/BOI/
   Stripe column index maps are unvalidated against real exports** (the coded AIB `DateColumn=0`
   looks wrong — real AIB col 0 is the account number). Left as-is; needs a real sample to fix.
8. **#7 Charity output standard** — untouched (BL-12 deferred, see below).

---

## Items completed (22 fully + BL-05 client + BL-29)
BL-01, BL-02, BL-03, BL-04, BL-06, BL-07, BL-08, BL-09, BL-10, BL-13, BL-14, BL-15, BL-16,
BL-17, BL-18, BL-21, BL-22, BL-25, BL-27, BL-30, BL-31, BL-32 — each with a passing test.
BL-05 client+backend landed & tested. BL-29 was mostly already satisfied (410 route + reports
table used only by dev seed; CLAUDE.md drift reconciled).

## Items left undone (with why)
- **BL-05 (UI portion)** — year-end page forms for loans/director-loans/share-capital. The
  api.ts client + backend are done & tested; the **UI wiring is deferred** because the page is a
  ~1,900-line Next.js 16 client component (repo `frontend/AGENTS.md` warns Next 16 has breaking
  changes beyond training data), there is **no component-test framework** (only build/lint can
  verify UI — that's BL-19), and director-loan entry needs an officer picker. Data is enterable
  via the API today.
- **BL-11 (role-based UI gating)** — frontend; same Next-16/no-test-framework risk. Backend is
  already the source of truth for authorization.
- **BL-12 (charity SORP Trustees'/SoFA PDF)** — output work; also gated on Human decision #7
  (exact Charities SORP tier/format). Not started.
- **BL-19 (frontend component/e2e tests)** — needs a test framework decision; only the lightweight
  `verify-*.mjs` style exists. I added one (`verify-api-client`) in that style.
- **BL-20 (retained-earnings O(n²) recursion + zero-placeholder adjustment)** — correctness
  refactor; deferred as a careful multi-period change (the single-period figures are correct and
  tested; BL-32 covers the roll-forward figure).
- **BL-23 (cash-flow working-capital classification / reconcile to BS cash)** — correctness
  refactor; deferred.
- **BL-24 (persisted board-approval date instead of `DateTime.Now` at render)** — needs a new
  `AccountingPeriod` field + migration + wiring across render sites; deferred.
- **BL-26 (statements drill-through UI)** — frontend; `getStatementSources` already exists on the
  backend.
- **BL-28 (uneven input validation / entity over-posting on create endpoints)** — spans many
  endpoints; deferred as a broad change.

## How to verify locally
```bash
cd backend && dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art   # 505 pass / 2 skip
cd frontend && npm run lint && npx tsc --noEmit && npm run build && npm run test:api-client
cd backend/Accounts.Api && dotnet ef migrations has-pending-model-changes                  # clean
```
CI on Linux is the source of truth — it additionally runs the 2 Postgres tests (BL-15).

Full per-item detail is in `NIGHTLY_LOG.md`.
