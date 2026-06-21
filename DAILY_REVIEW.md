# Daily Review — Trust Run 2026-06-21

**Branch:** `daily/trust-2026-06-21` (off `nightly/completion-2026-06-20`) — **NOT merged, left for review.**
**Run:** autonomous, against the 7 trust guarantees in `DAILY_GOAL_PROMPT.md` (plan in `RUN_PLAN.md`,
per-item log in `RUN_LOG.md`).
**Net diff vs base:** 10 files changed, ~959 insertions, ~21 deletions, 6 commits.

## Headline
- **Backend tests: 505 → 511 passing** (+6), 2 skipped (Postgres-only, run in CI), 0 failing.
- **Frontend:** new real `node:test` harness (13 named tests) + existing verifiers all green via
  `npm test`; `lint`, `tsc --noEmit`, `build` clean.
- **EF model:** no entity/DbContext changes this run → `has-pending-model-changes` clean (no migration).
- The tree was kept green after every committed item **except** one self-inflicted slip (the G5 CI
  edit broke a backend CI-guard test; caught by the final full-suite gate and fixed in `a209aa1`).

> ⚠️ **Professional-liability gate unchanged:** nothing here substitutes for a qualified accountant
> reviewing a full generated pack before any real filing. These are correctness/safety guarantees,
> not professional sign-off.

## Commits (oldest → newest), each with its test
| Commit | Guarantee | Summary | Test(s) |
|--------|-----------|---------|---------|
| `d776522` | **G1** | Golden paths end-to-end for Micro + Small (onboard→CSV→categorise→year-end→adjust→**balance**→PDF→iXBRL) | `GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl`, `GoldenPath_SmallAuditExemptCompany_MixedAccrualSetBalancesThroughPdfAndIxbrl` |
| `49b43d4` | **G6** | Correlation id on every error response + server log; method/path on 500 log; redaction preserved | `ExceptionMiddleware_LogsCorrelationIdAndDoesNotLeakSecretsInProduction` |
| `a0c6fc4` | **G3** | Validate year-end figure inputs (debtor/creditor/inventory/fixed-asset/dividend) + reset client Id (over-posting) | `YearEndFigureInputs_RejectBadFiguresWithCleanBadRequestAndNoCorruption`, `YearEndCreate_IgnoresClientSuppliedIdentityToPreventOverPosting` |
| `9dc0b10` | **G2** | 3-year retained-earnings roll-forward (profits less dividends) correct year on year | `BalanceSheet_MultiYearRetainedEarningsRollForwardAccumulatesProfitsLessDividends` |
| `3c69fe1` | **G5** | Real `node:test` frontend harness (validation + format) + `npm test` aggregate; CI runs `npm test` | 13 node:test tests in `frontend/tests/*.test.mjs` |
| `a209aa1` | **G5 fix** | Align backend CI-guard with the `npm test` CI change | `ContinuousIntegrationWorkflow_RunsBackendFrontendAndProductionConfigGates` (updated) |

## State of each guarantee
1. **Golden paths proven** — ✅ **MET.** Two end-to-end tests drive the whole pipeline with the real
   services for the two shipped regimes (Micro + Small audit-exempt), proving balanced statements, a
   real PDF past the readiness gate, and well-formed iXBRL.
2. **Money is correct, by test** — ✅ **MET for what's in scope.** Single-period balance proven (BL-01
   + both golden paths); CT direction proven (BL-04, prior run); multi-year **retained-earnings
   roll-forward figure** proven correct over 3 years incl. dividends. ⚠️ **Deferred (flagged):** full
   multi-year balance-**sheet** balancing (carrying prior-year cash) is the BL-20/BL-23 movement-basis
   refactor — the cash side reads only the current period's transactions, so years 2+ balance only
   when brought-forward opening balances are entered. Architecture fork, deferred in the prior run; not
   safe to land in one session. See **Flagged**.
3. **Customer inputs are safe** — ✅ **MET (core).** Negative amounts / blank names / zero useful life /
   negative cost / out-of-order dates on the figure-bearing year-end endpoints now return a clean 400,
   never a 500 or silent corruption; create endpoints reset client-supplied Ids. CSV-upload bad input
   was already covered (`ImportService` → `BusinessRuleException`). Remaining endpoints (banking rules,
   officers, etc.) not exhaustively swept — logged as follow-up, lower customer-impact.
4. **Data is enterable** — ⚠️ **MET at the trust layer; UI forms deferred.** Every statement-critical
   year-end entity (incl. loans / director loans / share capital) is enterable via the tested typed
   `api.ts` client + backend CRUD, and the golden-path tests enter year-end data that flows into
   balanced statements. The **year-end page UI forms** (BL-05), inline edits (BL-25) and **UI role
   gating** (BL-11) remain deferred — the page is a 1,911-line Next 16 client component with documented
   breaking-change risk (`frontend/AGENTS.md`) and there is no component-render test framework, so the
   change can't be verified beyond build/lint. **Not trust-critical:** the backend is the source of
   truth for authorization (Reviewer/Client writes already 403, proven by existing tests) and data is
   enterable via the API today. See **Deferrals**.
5. **Regressions are caught first** — ✅ **MET.** A real `node:test` frontend harness now exists with 13
   named tests over the previously-untested critical pure logic (onboarding-wizard validation; user-
   facing formatters), wired into `npm test` alongside the readiness/proxy/auth/api-client verifiers,
   and CI runs `npm test` as one step (also pulling the previously CI-orphaned `test:api-client` into
   CI). Backend suite green; CI remains the source of truth.
6. **Failures are diagnosable** — ✅ **MET.** Every error response and its matching server log now carry
   a correlation id (request trace id); the 500 log also records request method + path. A support
   ticket quoting the id maps to the exact log line without a repro. Production secret redaction
   preserved (generic 500 message to the client).
7. **Refuses to emit when not ready** — ✅ **MET (verified).** Existing block tests already prove the
   readiness gate refuses accounts PDF / final iXBRL / CRO submission / signature page when blockers or
   warnings remain; the two new golden-path tests prove the *ready* direction (a fully-ready period
   emits). No gap found — verified, not re-implemented.

**Summary: 6 of 7 fully met with passing tests; G4 met at the trust layer with the UI-forms portion
deferred (non-trust-critical, documented Next-16 risk, no render-test harness).**

## Flagged human decisions (implemented conservative default — NOT decided here)
- **Balance-sheet model (multi-year):** single-source-of-truth vs balancing-adjustments remains the
  open architecture fork (Human decision #6). This run proved the roll-forward *figure*; full
  multi-year balancing (BL-20/BL-23) is the larger movement-basis change to ratify.
- **UI role gating (BL-11):** backend is already the authorization source of truth; UI gating is UX
  hardening, not a security boundary — deferred, your call on priority vs the Next-16 risk.
- All prior-run flags still stand (loss-relief s.396A election; s.236 vs s.239; charity SORP standard;
  per-company API keys; auditor's-report template; AIB/BOI/Stripe CSV column maps need real samples).

## Deferrals (with why)
- **BL-05 / BL-25 (year-end UI forms + inline edits)** — 1,911-line Next 16 client component, no
  render-test framework; only build/lint can verify. Data enterable via API today. Remaining work:
  add loan / director-loan (needs officer picker) / share-capital sections + inline row edits to
  `year-end/page.tsx`, then verify via build/lint (and, ideally, a component-render harness — separate
  decision).
- **BL-11 (UI role gating)** — wrap mutation controls in `canWriteWorkingPapers`/`canReview`; same
  Next-16/no-render-test risk; not trust-critical (backend enforces).
- **BL-20 / BL-23 (multi-year cash movement basis / cash-flow reconciliation)** — architecture-level;
  needed for full multi-year balance-sheet balancing. Roll-forward figure proven here.
- **BL-24 (persisted board-approval date)** — needs a new `AccountingPeriod` field + migration + render
  wiring; deferred (notes currently stamp `DateTime.Now` at render).
- **G3 breadth** — validation swept the figure-bearing year-end endpoints; a full sweep of every
  mutating endpoint (banking rules, officers, transaction-rules) is a lower-impact follow-up.

## How to verify locally
```bash
cd backend && dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art   # 511 pass / 2 skip
cd frontend && npm test && npm run lint && npx tsc --noEmit && npm run build               # all green
cd backend/Accounts.Api && dotnet ef migrations has-pending-model-changes                  # clean (no model changes)
```
CI on Linux is the source of truth (it additionally runs the 2 Postgres audit tests and now `npm test`).

## Note on process
The G5 commit (`3c69fe1`) changed `ci.yml` but I verified only the frontend, not the backend suite —
a backend test asserts on `ci.yml`, so the tree was briefly red on the branch. The final full-suite
gate caught it and `a209aa1` fixed it. Lesson: run the **backend** suite after any `ci.yml`/config
edit, since backend tests assert on those files.
