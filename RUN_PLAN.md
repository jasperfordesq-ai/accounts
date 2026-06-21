# Run Plan — Daily Trust 2026-06-21

One prioritized plan for this run. Execute top-down. Do not re-plan or re-audit between items.
Each item: reproduce/understand → smallest correct change → test that proves the criterion →
verify GREEN (full backend suite) → commit that one item → append to `RUN_LOG.md`.

Branch: `daily/trust-2026-06-21`. Never merge/push. Keep green always.

## The 7 guarantees → concrete, provable work (priority order)

### P1 — G1: Golden paths proven end-to-end  *(keystone)*
No single test drives the whole pipeline with the real services. Write end-to-end tests that,
for each shipped regime (**Micro audit-exempt CLG/LTD** and **Small audit-exempt LTD**), drive:
onboard company+period → import a real CSV via `ImportService.ImportCsvAsync` → categorise via
`CategoryService` → enter year-end facts → `AdjustmentService.GenerateAutoAdjustmentsAsync` →
assert TB + P&L + BS computed and **BS BALANCES** (`UnexplainedDifference == 0`) → generate the
accounts **PDF** (passes the readiness gate, non-empty) → generate **iXBRL** (well-formed XML).
This single test proves the product works as a whole.

### P2 — G7: Refuses to emit a filing output when data isn't ready
Boundary tests around the readiness gate (`AssertFinalOutputReadinessAsync`): a period that does
NOT balance / is missing required data → accounts PDF, final iXBRL, and CRO submission each throw
`BusinessRuleException` naming the blockers; the same period once made ready succeeds. Prove the
gate is airtight in both directions.

### P3 — G6: Failures are diagnosable
Verify the global `ExceptionMiddleware` logs server-error (500) paths with enough structured
context to triage a ticket without a repro, while preserving secret redaction. Add a test that an
unhandled exception is logged (level/category/message) and the HTTP response leaks no secret or
stack trace.

### P4 — G3: Customer inputs are safe (BL-28, sliced)
Bad-input tests on the highest-traffic customer inputs: malformed/empty/oversized CSV upload, and
year-end create endpoints with invalid amounts / missing required fields / wrong types → clean
400 (`BusinessRuleException`), never a 500, never silent corruption. Slice to the inputs a real
customer hits first; log the rest as deferred.

### P5 — G2: Money correct across multiple years
Multi-year roll-forward test: period 1 closes with a profit → period 2 opening retained earnings
carries it correctly and both years' balance sheets balance. Closes the roll-forward concern from
BL-20/BL-32 in the high-stakes direction (proves correct or fixes).

### P6 — G5: Regressions caught — real frontend test harness (BL-19)
Establish a real frontend test runner (node:test, zero new heavy deps, in the existing
`verify-*.mjs` idiom) wired into `npm test`, covering the critical pure-logic flows (validation,
api client, auth/session, proxy). This is the prerequisite for safely verifying G4 UI work.

### P7 — G4: Data enterable via UI (BL-05 / BL-25 / BL-11)  *(time-boxed, gated on P6)*
Wire the year-end page UI for loans / director-loans / share-capital (api.ts client already done),
inline edits (BL-25), and role gating (BL-11). Verify with P6 harness + lint/tsc/build. Data is
already enterable via the API today, so this is the least trust-critical gap — attempt as time
allows; otherwise log precisely what remains.

## Human-decision items — implement conservative default, FLAG, never decide
loss-relief election (s.396A); s.236 vs s.239; charity SORP standard; single-source vs
balancing-adjustments balance sheet; per-company API keys; fabricating an auditor's opinion;
AIB/BOI/Stripe CSV column layouts (do not guess — flag, test coded behaviour only).

## Convergence
Bias to proving things work. Time-box each item; land the smallest safe slice and defer the rest
with a logged reason. Trust-critical (the 7) gets fixed now; everything else is logged. One branch,
single-threaded edits. Stop when the 7 hold (each proven) or genuinely blocked → write
`DAILY_REVIEW.md`.
