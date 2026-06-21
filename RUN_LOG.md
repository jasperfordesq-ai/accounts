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
  **516 pass / 3 skip**.

