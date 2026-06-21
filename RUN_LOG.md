# Daily Trust Run Log — 2026-06-21

Branch: `daily/trust-2026-06-21` (off `nightly/completion-2026-06-20`)
Goal: the 7 trust guarantees in `DAILY_GOAL_PROMPT.md`, each proven by a test.

## Baseline (green, recorded so any regression is attributable)
- **Backend**: `dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$TEMP/accts-art` →
  **505 passed, 2 skipped, 507 total** (exit 0, 46 s). The 2 skips are the Postgres-only
  audit-durability tests (InMemory provider — expected).
- **Frontend**: `npm run lint` clean, `npx tsc --noEmit` clean, `npm run build` exit 0.

Any later regression below this line is attributable to this session's changes.

---

## Items
<!-- One line per item: guarantee — what changed — test added — result -->
- **G1 (golden paths)** ✅ — Two end-to-end tests drive the full pipeline with the real services
  (onboard → real CSV import via `ImportService` → auto-categorise via `TransactionRules` →
  year-end facts → generate+approve adjustments → notes → statements that BALANCE → accounts PDF
  past the readiness gate → well-formed iXBRL) for the **Micro** and **Small** audit-exempt
  regimes. Tests `GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl`,
  `GoldenPath_SmallAuditExemptCompany_MixedAccrualSetBalancesThroughPdfAndIxbrl`. Backend
  **507 pass / 2 skip** (was 505/2). Commit `d776522`. Also proves G7 in the *ready→emits* direction.
- **G6 (failures diagnosable)** ✅ — `ExceptionMiddleware` now stamps a correlation id (request trace
  id) onto every error response **and** the matching server log, and the 500 log records request
  method+path — so a ticket quoting the id maps to the log line without a repro. Production redaction
  preserved (generic 500 message, no exception detail to client). Test
  `ExceptionMiddleware_LogsCorrelationIdAndDoesNotLeakSecretsInProduction`. **508 pass / 2 skip**.
  Commit `49b43d4`.
- **G7 (refuses to emit when not ready)** ✅ *(already covered + reinforced)* — existing block tests
  (`FinalApprovalPacks_BlockWhenReadinessItemsRemainOpen`, `FinalOutputs_BlockWhenReadinessWarningsRemainOpen`,
  `FinalIxbrlDownload_BlocksUntilReadinessAndInternalChecksPass`, `CroFilingPack/SignaturePage_BlocksWhenReadinessItemsRemainOpen`,
  `PeriodStatusEndpoint_RejectsFinaliseOrFileWhenReadinessBlockersRemain`) prove the gate refuses
  in the *not-ready* direction; the two new golden-path tests prove it *allows* a ready period. No
  gap found — verified, not re-implemented.
- **G3 (customer inputs are safe)** ✅ — `YearEndFigureInputs` guards wired into debtor/creditor/
  inventory/fixed-asset/dividend create+update endpoints (negative amounts, blank names, zero/negative
  useful life, negative cost/proceeds, out-of-order disposal/dividend dates → clean 400, never a 500
  or silent corruption). Create endpoints now reset client-supplied `Id` (over-posting). Tests
  `YearEndFigureInputs_RejectBadFiguresWithCleanBadRequestAndNoCorruption`,
  `YearEndCreate_IgnoresClientSuppliedIdentityToPreventOverPosting`. **510 pass / 2 skip**. Commit `a0c6fc4`.
  Note: CSV-upload bad-input paths already covered (`ImportService` malformed/empty/oversized →
  `BusinessRuleException`); the depreciation div-by-zero was already guarded (asset skipped when
  life ≤ 0 — now also rejected at entry).
- **G2 (money correct, multi-year)** ✅ *(roll-forward figure)* — three-year chain (2023→2024→2025)
  with a dividend proves reserves brought forward accumulate prior profits and subtract prior
  dividends correctly year on year. Test
  `BalanceSheet_MultiYearRetainedEarningsRollForwardAccumulatesProfitsLessDividends`. **511 pass / 2 skip**.
  Commit `9dc0b10`. Single-period money-correctness already proven (BL-01 balance, BL-04 tax direction,
  golden paths). **FLAG/DEFER:** full multi-year balance-sheet *balancing* (carrying prior-year cash) is
  the BL-20/BL-23 movement-basis refactor — the cash side reads only the current period's transactions,
  so years 2+ balance only when brought-forward opening balances are entered. Out of scope this run
  (architecture fork, was deferred in the prior run); roll-forward *figure* is proven correct here.
- **G5 (regressions caught — frontend harness)** ✅ — added a real `node:test` harness
  (`frontend/tests/*.test.mjs`, zero new deps, Next-16-safe pure logic) with 13 named tests covering
  the onboarding-wizard validation (`validation.ts`) and the user-facing formatters (`format.ts`),
  both previously untested. New `npm run test:unit` + an aggregate `npm test` that also runs the
  existing readiness/proxy/auth/api-client verifiers. CI now runs `npm test` (one step) — which also
  brings the previously CI-orphaned `test:api-client` into CI. `npm test`, `lint`, `tsc --noEmit` all
  green.
- **G5 fix** ✅ — the G5 CI edit (replacing 3 frontend steps with `npm test`) broke the backend
  `ContinuousIntegrationWorkflow_RunsBackendFrontendAndProductionConfigGates` guard (it asserted the
  old step names). Updated the guard to assert CI runs `npm test` and that `package.json`'s `test`
  aggregate still chains every sub-suite. **511 pass / 2 skip** (green). Commit `a209aa1`.

## Final state
- Backend **511 pass / 2 skip / 0 fail**; frontend `npm test` + `lint` + `tsc` + `build` green;
  `has-pending-model-changes` clean (no model changes this run).
- Guarantees: **G1, G2, G3, G5, G6, G7 met (each test-proven); G4 met at the trust layer, UI forms
  deferred.** See `DAILY_REVIEW.md`.

---

# Finish Run Log — 2026-06-21 (PLATFORM_AUDIT backlog)

Branch: `daily/finish-2026-06-21` (off `daily/trust-2026-06-21`)
Goal: close the P0/P1 backlog in `PLATFORM_AUDIT_2026-06-21.md`, phase by phase, each item proven by a
test I saw pass. One item per commit. Not merged — left for review.

## Baseline (green, recorded so any regression is attributable)
- **Backend**: `dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art` →
  **511 passed, 2 skipped, 513 total** (exit 0, 40 s). The 2 skips are the Postgres-only audit
  durability tests (InMemory provider — expected; they run on real Postgres in CI).
- **Frontend**: prior run left `npm test` / `lint` / `tsc --noEmit` / `build` green (re-verified before
  any frontend edit this run).

Any later regression below this line is attributable to this session's changes.

## Items
<!-- One line per item: id — what changed — test added — result -->
- **`ops-backend-vuln-scan`** (P1) ✅ — `backend/Directory.Build.props` now enables NuGetAudit
  (mode=all, level=low) and promotes NU1901-NU1904 to errors via `WarningsAsErrors`, so a
  vulnerable backend package (direct/transitive) fails `dotnet restore backend/Accounts.slnx` in CI
  instead of shipping green. No current packages vulnerable (`dotnet list package --vulnerable` →
  none). Test `BackendBuild_FailsCiOnVulnerableNuGetPackages`. Full suite **512 pass / 2 skip**.
  Commit `54eeb2f`.
- **`import-csv-formula-injection`** (P2, done early — small + security-relevant) ✅ — `ImportService`
  neutralises spreadsheet formula-injection in stored bank memo/reference text: a field starting with
  `= + - @` (or leading tab/CR/LF) gets an apostrophe prefix (OWASP CSV-injection mitigation). Numeric/
  date fields keep trim-only `CleanCsvField`, so a legit negative amount `-12.50` still parses. Test
  `ImportCsv_NeutralisesSpreadsheetFormulaInjectionInStoredText` (proves triggers neutralised, ordinary
  text unchanged, negative amount still posts). Commit `166030d`.
- **`tests-pdf-content-verified`** (P1) ✅ — added `PdfPig` (pure-managed) to the test project + an
  `ExtractPdfText` helper, then asserted real PDF *content* (not just `%PDF` bytes): the Micro and Small
  golden-path tests now assert company legal name, period-end date and the computed net-assets total
  (`NetAssets.ToString("N0")` == BalanceSheet) appear in the rendered PDF, plus the micro **s.280D**
  statement; a new `AbridgedSmallCroPack_PdfContainsSection352WordingNameAndPeriodEnd` proves the
  abridged CRO pack carries the **s.352** wording. PdfPig restored cleanly. Tests pass. Commit `6f1a0a2`.
- **`tests-ci-filing-path-on-postgres`** (P1) ✅ — new `FilingGoldenPathPostgresIntegrationTests`: a
  shared `RunGoldenFilingPathAsync` body drives onboard → real-CSV import → categorise → year-end nil
  facts → classify → Micro regime → adjustments → notes → statements → accounts PDF → iXBRL, asserting
  `UnexplainedDifference==0`, NetAssets 600, a `%PDF`-prefixed PDF and well-formed iXBRL. Runs on a real
  PostgreSQL service in CI (`[PostgresFact]`, gated on `ACCOUNTS_POSTGRES_TEST_CONNECTION`) **and** on
  InMemory (`[Fact]`) so the logic is proven locally. ⚠️ No local Postgres available, so the
  Postgres variant is **CI-verified only** (InMemory twin green locally; identical body).
- **`frontend-render-harness`** (P1) ⏸ **DEFERRED (logged, tree kept green).** Requires installing
  Vitest + @testing-library/react + jsdom and rendering the ~1,900-line year-end / ~2,440-line period
  Next-16 client components (HeroUI v3 + React Aria + `next/navigation`) — a large infra lift with real
  risk to the currently-green `npm run build` / `npm test` / CI and little local verifiability under the
  Next-16 breaking-change constraints (`frontend/AGENTS.md`). Deferring it (rather than risk a red tree)
  to invest the session in Phase 1 money correctness, which is the trust core and is fully provable with
  the existing backend test harness. Remaining work: add a Vitest/RTL config + smoke tests (year-end
  sections render; add-debtor form POSTs path/method/payload/CSRF; filing tab per-status button).

### Phase 1 — Make the money correct and self-consistent
- **`accounting-opening-balance-pl-accounts`** (**P0**) ✅ — `UpsertOpeningBalanceEndpointAsync` now
  rejects an opening balance posted to an income/expense account (`AccountCategory.Type` Income/Expense)
  with a clean 400 and stores nothing — a brought-forward figure on a 4xxx/5xxx/6xxx code would fold into
  current-year turnover/expenses. Balance-sheet accounts (e.g. retained earnings) still accepted. Test
  `UpsertOpeningBalance_RejectsIncomeAndExpenseAccountsButAllowsBalanceSheetAccounts`. Full suite
  **516 pass / 3 skip**. Commit `c071708`.
- **`accounting-tax-balance-internal-consistency`** (P1) ✅ — `UpsertTaxBalanceEndpointAsync` now
  validates the triple before storing: `Liability`/`Paid` non-negative and `Balance == Liability − Paid`
  (within €0.005); a negative `Balance` is allowed as a legitimate overpayment/refund. An inconsistent
  triple previously stored verbatim, mis-stating creditors and profit-after-tax. Test
  `UpsertTaxBalance_RejectsInconsistentOrNegativeTriple`. Full suite **517 pass / 3 skip**. Commit `5ea57d6`.
- **`accounting-tax-creditor-double-count`** (P1, **HUMAN DECISION flagged**) ✅ — the balance sheet
  summed the same tax liability twice (`Creditors.Type==Tax` rows **and** `TaxBalances.Balance`).
  **Decision (conservative default, flagged):** `TaxBalances` is the single source of tax owed (it drives
  the P&L tax charge + CT computation; 0 tests/UI use `Creditors.Type==Tax`), so the tax-creditor line is
  now `Σ TaxBalances.Balance` only and the redundant tax-creditor rows are excluded. Test
  `BalanceSheet_DoesNotDoubleCountTaxCreditorAndTaxBalance` (€125 in both sources → tax creditor €125 not
  €250, `UnexplainedDifference==0`). Full suite **518 pass / 3 skip**. Commit `443eb93`.
- **`accounting-pl-tax-charge-unreconciled`** (P1, **HUMAN DECISION flagged**) ✅ — readiness now
  reconciles the entered CorporationTax liability (the P&L tax charge) against `TaxComputationService`:
  when a CT figure is entered and diverges from the computed total by > €1, a warning is added that
  blocks final outputs ("does not match the corporation tax computation"). Clears once the entered
  figure matches. Test `Readiness_WarnsWhenEnteredCorporationTaxDivergesFromComputation`. Golden paths
  (which don't enter CT) unaffected. Full suite **519 pass / 3 skip**. Commit `83b58d1`.
- **`accounting-share-capital-and-dividends-reserves`** (P1, **HUMAN DECISION flagged**) ✅ — removed the
  €1 share-capital plug from the balance sheet AND the statement of changes in equity (share capital now
  reports its actual issued value, 0 if none); the share-capital note states "No share capital has been
  recorded" instead of fabricating a "1 Ordinary share". A non-CLG company with no recorded share
  capital is now a readiness blocker ("Share capital not recorded"); a company limited by guarantee is
  correctly exempt. Proposed (DatePaid==null) dividends no longer reduce reserves anywhere (BS, SOCIE,
  opening-RE roll-forward) — consistent with the financing cash-flow, which already counts only paid
  dividends. Tests `BalanceSheet_NoShareCapital_HasNoPlugAndBlocksReadinessExceptForCLG`,
  `Dividends_ProposedDoesNotReduceReserves_PaidDoes`. Updated 5 block-tests whose "balance sheet does
  not balance" assertion encoded the old plug bug (an empty company now correctly balances at 0; they
  assert another guaranteed-open blocker). Full suite green. Commit `55f6509`.
- **`accounting-multiyear-cash-movement-basis`** (P1, **XL** — prior runs deferred this as an architecture
  fork) ✅ — balance-sheet cash is now on a true movement basis: closing cash = bank opening balance +
  the **cumulative** net transaction movement across this period AND every prior period (using
  `periodsToDate`), not just the current period's transactions. Year-2+ balance sheets now carry forward
  prior years' cash and balance with no manual opening rows. Test
  `BalanceSheet_MultiYearCashOnMovementBasis_CarriesPriorYearsAndBalances` (3-year chain, bank opening 0,
  cash 800 → 1,700 → 2,700 cumulative, `UnexplainedDifference==0` in years 2 & 3). Full suite
  **522 pass / 3 skip**. Commit `0ef00f5`.
- **`accounting-cashflow-vs-bs-cash-tie`** (P1, L — **safe slice**) ✅ — the cash-flow opening cash now
  carries forward prior years' net movement (bank openings + cumulative prior-period transactions, all
  bank accounts), on the same movement basis as the balance sheet. So the cash-flow closing cash
  (`opening + net increase`) ties to the balance-sheet cash for a cash-consistent set across years.
  Test `CashFlow_ClosingCashTiesToBalanceSheetCashAcrossYears` (2-year, 2-account: opening 1,600 → closing
  2,800 == BS cash). ⚠️ **Remaining (logged):** a readiness *divergence warning* when the indirect
  cash-flow does not reconcile to BS cash for **accrual** companies (year-end balances entered as
  standalone figures) — deferred because it is entangled with the deeper "make the indirect cash-flow
  reconcile to the entered accrual balances" model and would otherwise flag the artificially-constructed
  Small golden-path fixture. Added to backlog as `accounting-cashflow-accrual-reconciliation`. Commit `bf87a36`.
- **`tests-multiyear-balance-asserted`** (P1, L) ✅ — the BS-level proof (`UnexplainedDifference==0 &&
  Balances` for years 2 & 3, no manual openings) is the multi-year cash test above; added a complementary
  **readiness-level** proof `Readiness_MultiYearPeriodBalancesWithoutManualOpeningRows` (a year-2 period
  with no manual opening rows → `readiness.BalanceSheetBalances == true` and no "Balance sheet does not
  balance" warning). Test-only; fails on the pre-movement-basis code, passes after it.

