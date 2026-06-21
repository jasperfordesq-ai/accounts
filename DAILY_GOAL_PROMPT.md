# Daily Goal Prompt — run autonomously, unattended

GOAL: Make the Irish statutory accounts platform something I, my users, and my customers can
trust — software that works correctly, fails safely, and runs like a well-oiled machine with
minimal support tickets. Work autonomously and unattended for the full session toward that goal.

THIS IS NOT AN AUDIT. Do not open-endedly hunt for problems — that never terminates, because
"find problems" has no done-state. Instead, satisfy the FIXED set of trust guarantees below. When
they hold and are proven by tests, you are done for this run, even if a deeper search could find
more. Trust comes from guarantees, not from the absence of every conceivable bug.

## Orient (once, at the start)
- Read in full: `MORNING_REVIEW.md`, `NIGHTLY_LOG.md`, `OVERNIGHT_BACKLOG.md`, `CLAUDE.md`
  (build/test commands + the WDAC test workaround), `REQUIREMENTS.md`. The earlier runs left a
  finite, prioritized backlog with file:line + acceptance criteria, a record of what's done, and
  what's left with reasons. Trust those; do not re-derive from scratch or re-audit.
- Confirm the current state is green first: run the backend suite and the frontend build, record
  the baseline counts in `RUN_LOG.md` so any later regression is attributable.
- Derive ONE prioritized plan for this run (the remaining backlog items + the trust guarantees
  below), write it to `RUN_PLAN.md`, then execute it top-down. Do not re-plan or re-audit between
  items.

## Definition of trustworthy v1 — the finite DONE for this goal
Work until these 7 guarantees hold (each proven by a test) or you are genuinely blocked:
1. **Golden paths proven.** An end-to-end test drives a realistic company through: onboard →
   import bank CSV → categorise → enter year-end facts → generate adjustments → statements that
   BALANCE → accounts PDF → iXBRL — for each regime you ship (at least Micro and Small
   audit-exempt LTD). If a path can't be driven end-to-end, fixing it is the top priority.
2. **Money is correct, by test.** Realistic mixed cash/accrual sets balance
   (`UnexplainedDifference == 0`); corporation tax never under- or over-states in tested
   scenarios; multi-year roll-forward is correct (close BL-20/BL-23/BL-24 or prove them correct).
3. **Customer inputs are safe.** Every customer-facing input (CSV upload, year-end forms, all
   mutating API endpoints) validates and fails with a clear, safe message — never a 500, never
   silent data corruption (BL-28). Add tests for the bad-input cases.
4. **Data is enterable.** Everything the statements depend on can be entered through the UI
   (finish BL-05 loans/director-loans/share-capital UI; BL-25 inline edits), with role gating
   (BL-11).
5. **Regressions are caught before customers see them.** A real frontend test harness exists
   (BL-19) covering the critical UI flows; the backend suite stays green; CI is the source of
   truth.
6. **Failures are diagnosable.** Production errors are logged/surfaced enough to triage a support
   ticket without a repro — structured logging on the error path, preserving existing secret
   redaction.
7. **The platform refuses to emit a filing output when the data isn't ready.** Verify the
   readiness gates are airtight and add boundary tests.

## Working method (keep it strict — this is the loop that converges)
For each plan item: reproduce/understand from the cited files → implement the smallest correct
fix → add or extend a test that proves the acceptance criterion → verify GREEN → commit that one
item (conventional commit, reference the item) → append a line to `RUN_LOG.md`. No item is "done"
without a passing test you ran and saw pass.
- Backend: `cd backend && dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art`
  (the WDAC artifacts redirect is mandatory locally).
- Frontend: `cd frontend && npm run lint && npx tsc --noEmit && npm run build` (plus the frontend
  tests once the harness exists).
- After EF model changes: add the migration, then confirm `has-pending-model-changes` is clean.

## How to NOT loop forever (convergence rules)
- Bias to PROVING things work (happy path + the edge cases that cause support tickets), not to
  discovering new categories of defects.
- Time-box discovery on any one item. If a fix is bigger than one safe change, land the smallest
  safe slice, log the remainder as a follow-up, and move on. Do not rabbit-hole.
- Trust-critical (the 7 guarantees) gets fixed now; everything else gets logged and deferred.
- You may use a Workflow to fan out parallel discovery/verification, but make all source edits
  single-threaded on ONE branch.

## Guardrails
- Create a feature branch first (e.g. `daily/trust-<date>`) off the current state. NEVER commit to
  `main`, NEVER merge, NEVER push unless I explicitly tell you.
- Keep the build green at all times. If a fix can't be completed safely, revert it cleanly, log
  why, and move on — never leave the tree broken or a refactor half-applied.
- Respect the human-decision list in `MORNING_REVIEW.md`/`OVERNIGHT_BACKLOG.md`. Implement the
  conservative default and FLAG each — do NOT decide them yourself (loss-relief election, s.236 vs
  s.239, charity SORP standard, single-source vs balancing-adjustments balance sheet, per-company
  API keys, fabricating an auditor's opinion).
- Don't invent correctness. If a behaviour depends on a real-world fact you can't verify (e.g. the
  AIB/BOI/Stripe CSV column layout), do NOT guess — test the coded behaviour, FLAG that it needs a
  real sample, and move on.
- CI on Linux is the real source of truth; note anything you couldn't verify locally.

## Stop condition
Work until the 7 guarantees hold (each proven by a test) or you are genuinely blocked on every
remaining item. Then write `DAILY_REVIEW.md`: every commit (item + message), every test added and
its result, the current branch name, every flagged human decision, the state of each of the 7
guarantees (met / partially met + what's left), and anything deferred with why. Leave the branch
for me to review — do not merge.
