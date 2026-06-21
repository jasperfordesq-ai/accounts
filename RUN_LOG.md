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
