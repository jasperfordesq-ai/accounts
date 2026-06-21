# All-Day Workload Prompt — "Finish the platform"

> Paste the block below into a fresh session (or `/goal`). It is engineered to **not terminate early**:
> the done-state is a *59-item enumerated backlog*, not a short checklist, so the work is bounded by the
> list (it converges) but large enough to fill many days (it won't stop at lunchtime). Re-run it day
> after day until the P0/P1 set is closed. The full backlog with acceptance criteria lives in
> `PLATFORM_AUDIT_2026-06-21.md` — that file is the single source of truth.

---

GOAL: Take the Irish statutory accounts platform from "broad but cents-wrong skeleton" to a platform my
users (firm staff) and my customers (the companies) can fully TRUST — it computes statutory accounts
**correctly**, fails **safely**, produces **filing-grade** CRO + Revenue/iXBRL outputs, and runs with
**minimal support tickets**. Work autonomously and unattended for the FULL session toward that goal. Do
not wind down early — when you finish an item, immediately start the next one on the list.

THIS IS NOT AN OPEN-ENDED AUDIT. The audit is already done. Your scope is the FIXED, ENUMERATED backlog
in `PLATFORM_AUDIT_2026-06-21.md` (59 items across 7 phases, each with a written acceptance criterion).
You are DONE for the whole programme only when **every P0 and P1 item is closed with a passing test you
saw pass, or is genuinely blocked on a human decision and flagged**. P2 items are closed if time
allows, else explicitly deferred. Do not invent new scope; if you discover a new defect, log it to the
backlog file and keep going. Trust comes from closing this list, not from finding more.

## Orient (once, at the start)
- Read in full: `PLATFORM_AUDIT_2026-06-21.md` (the backlog — single source of truth), `DAILY_REVIEW.md`
  and `RUN_LOG.md` (what the last runs already did — do NOT redo), `CLAUDE.md` (build/test commands +
  the WDAC test workaround), `REQUIREMENTS.md` (product intent), and `frontend/AGENTS.md` (Next 16 has
  breaking changes — read `node_modules/next/dist/docs/` before writing frontend code).
- Confirm GREEN first and record the baseline in `RUN_LOG.md`: backend
  `dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art`; frontend `npm test`,
  `npm run lint`, `npx tsc --noEmit`, `npm run build`. Any later regression is then attributable.
- Create a working branch off the current state: `daily/finish-<date>`. Do NOT re-plan or re-audit —
  trust the backlog file and execute it.

## Execution order (top-down — do NOT skip ahead)
Work the phases in order; within a phase, P0 → P1 → P2. Phase 0 first is deliberate: it builds the
harnesses (Postgres golden-path in CI, PDF-content + iXBRL validation, frontend render harness) that
make every later correctness fix provable. The ordered worklist (full detail in the audit file):

- **Phase 0 — Diagnosability first:** frontend-render-harness · tests-ci-filing-path-on-postgres ·
  tests-pdf-content-verified · ops-backend-vuln-scan · import-csv-formula-injection
- **Phase 1 — Money correct & self-consistent:** accounting-opening-balance-pl-accounts ·
  accounting-tax-balance-internal-consistency · accounting-tax-creditor-double-count ·
  accounting-pl-tax-charge-unreconciled · accounting-share-capital-and-dividends-reserves ·
  accounting-multiyear-cash-movement-basis · accounting-cashflow-vs-bs-cash-tie ·
  tests-multiyear-balance-asserted · accounting-vat-paye-reconciliation ·
  validation-pre-filing-consistency-pass · accounting-ixbrl-rounding-subtotals ·
  accounting-retained-earnings-snapshot · accounting-depreciation-regeneration-order
- **Phase 2 — Make all the money enterable:** frontend-loans-no-ui · frontend-share-capital-no-ui ·
  frontend-director-loans-no-entry · frontend-inline-edit-yearend · frontend-role-gating ·
  frontend-unsaved-changes-guard · tests-csv-real-export-fixtures
- **Phase 3 — Regime-correct, legally-dated outputs:** filing-ixbrl-tagging-completeness ·
  filing-ixbrl-regime-taxonomy-branch · filing-ixbrl-namespace-taxonomy-pin ·
  tests-ixbrl-structural-validation · filing-directors-report-from-service ·
  filing-abridged-cro-directors-report · filing-approval-date-persisted ·
  filing-auditor-report-blocks-final · filing-charity-pdf-and-reconciliation · signing-approval-chain
- **Phase 4 — Data safety & tenant backstop:** data-no-optimistic-concurrency · data-company-soft-delete ·
  tenant-ef-query-filter-backstop · data-period-status-state-machine · data-input-validation-breadth ·
  data-list-transactions-pagesize-cap · data-period-lock-toctou · data-idempotency-creates-import
- **Phase 5 — Real filing & agent model:** onboarding-opening-trial-balance-takeon ·
  agent-ros-cro-engagement-model · b1-annual-return-data-object · filing-cro-ros-machine-export ·
  filing-ct1-numbered-field-mapping · filing-preliminary-tax-tracker · filing-amended-filing-and-snapshot
- **Phase 6 — Operate without an engineer:** ops-upgrade-on-populated-db · ops-backup-automated-monitored ·
  filing-deadline-reminders · ops-firm-admin-support-console · privacy-gdpr-data-subject ·
  ops-metrics-tracing · ops-structured-logging · crypto-tls-to-db · auth-login-ratelimit-account-dim

## Working method (the loop that converges — keep it strict)
For EACH backlog item, in order:
1. **Reproduce / understand** from the cited files. For a correctness defect, FIRST write a failing
   test that demonstrates the wrong number (red), so the fix is proven by the test going green.
2. **Implement the smallest correct change** that satisfies the item's acceptance criterion. If it is an
   XL item (multi-year cash, opening-TB take-on, CRO/ROS export), land the smallest safe vertical slice
   that passes a real test, and log the remainder as a new backlog sub-item — never half-apply a refactor.
3. **Prove it:** add/extend a test that asserts the acceptance criterion; run it and SEE it pass.
4. **Verify GREEN:** full backend suite green; frontend `npm test` + `lint` + `tsc` + `build` green.
   After any EF model change, add the migration and confirm `dotnet ef migrations
   has-pending-model-changes` is clean. **After any `ci.yml`/config/workflow-file edit, re-run the
   BACKEND suite** — a backend test asserts on those files (this bit the last run).
5. **Commit that one item** (conventional commit, reference the item id) and append one line to
   `RUN_LOG.md` (item, what changed, test, result). One item per commit.
6. Move to the next item. Do not batch, do not skip the test, do not leave the tree red.

## Convergence rules (so it neither stops early nor loops forever)
- The done-state is the WHOLE P0/P1 list — do not stop because "the important ones" are done. If you
  somehow clear P0/P1, start on P2. Only stop when every P0/P1 is closed-or-blocked.
- Time-box discovery per item. If a fix is bigger than one safe change, ship the smallest safe slice,
  log the remainder, move on. Never rabbit-hole; never re-audit between items.
- Bias to PROVING things work (a failing-then-passing test per defect), not to discovering new defect
  categories. New defects get logged to the backlog, not chased mid-item.
- You MAY use parallel sub-agents / a Workflow to research or verify independent items concurrently, but
  make ALL source edits single-threaded on the ONE branch, committed one item at a time.

## Guardrails
- Branch `daily/finish-<date>` off the current state. NEVER commit to `main`, NEVER merge, NEVER push
  unless I explicitly say so. Leave the branch for my review.
- Keep the build GREEN at all times. If a change can't be completed safely, revert it cleanly, log why,
  and move on. Never leave the tree broken or a refactor half-applied.
- **Human-decision items (`HD ✓` in the audit file): implement the conservative, safe default, ship the
  test/scaffolding you can, and FLAG the decision in `RUN_LOG.md` — do NOT decide tax policy, regime
  scope, taxonomy version, GDPR retention, ROS/CRO export format, hard-vs-soft-delete, etc. yourself.**
- Don't invent correctness. If behaviour depends on a real-world fact you can't verify (e.g. the
  AIB/BOI/Stripe CSV column layout, the exact FRC Irish taxonomy release, a CT1 box mapping), test the
  coded behaviour, FLAG that it needs a real sample/spec, and move on — never guess.
- CI on Linux is the source of truth; note anything you couldn't verify locally (InMemory vs Postgres).

## Stop condition
Work until every P0 and P1 item in `PLATFORM_AUDIT_2026-06-21.md` is closed (each proven by a passing
test you ran) or genuinely blocked on a flagged human decision — then write `DAILY_REVIEW.md`: every
commit (item id + message), every test added and its result, the branch name, every flagged human
decision, and the status of EACH backlog item (done / blocked-on-decision / deferred-with-reason). If
P0/P1 remain at end of session, leave the branch and the updated backlog so the next run continues from
exactly where you stopped. Do not merge.
