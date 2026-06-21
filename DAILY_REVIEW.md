# Daily Review — Frontend Trust Run 2026-06-21 (PLATFORM_AUDIT Phase 2)

**Branch:** `daily/frontend-trust-2026-06-21` (off `main`) — **NOT merged, NOT pushed. Left for review.**
**Scope:** the FIXED Phase 2 worklist in `PLATFORM_AUDIT_2026-06-21.md` ("Make all the money enterable"),
enabled by the deferred Phase 0 render harness. Per-item detail in `RUN_LOG.md` (the "Frontend Trust Run"
section); one item per commit, each proven by a render/integration test I ran.

> ⚠️ **Professional-liability gate unchanged:** nothing here substitutes for a qualified accountant
> reviewing a full generated pack before any real filing. These are correctness/reachability guarantees.

## Headline
- **The money the balance sheet needs is now enterable, role-gated and editable through the UI, and
  every entry surface is proven to issue the exact backend request (path / method / payload / CSRF).**
  A company with share capital, loans or director loans can now reach a correct, balanced balance sheet
  entirely through the front end — closing the gap the programme was set up to fix.
- **Render harness stood up** (the prior runs' blocker): Vitest + @testing-library/react + jsdom, wired
  into `npm test` and therefore CI, with HeroUI v3 / React Aria rendering and CSRF-aware fetch assertions.
- **22 render tests across 7 files**, all green; `tsc`, `lint`, `build` green; **9 commits**, tree green
  after each.
- **Pre-flight also fixed CI red-on-main** (a real-Postgres FK violation in the golden filing-path test).

## ⚠️ CI status on `main` (read this first)
CI on `main` was **RED** before this run: the Postgres-gated golden filing-path test hard-coded
`TenantId = 1` on the company; EF InMemory ignores FKs so the local `[Fact]` twin passed, but the
real-Postgres `[PostgresFact]` in CI violated `FK_companies_tenants_TenantId`. The fix (seed a tenant) is
the **first commit on this branch**. Because the guardrail forbids committing to `main`, **`main` stays
red until this branch is merged** — merging it is what makes CI green again. Verified locally against a
real PostgreSQL 16.4 (2/2).

## Pre-flight outcomes
- **`refactor/architecture-2026-06-21`**: already fast-forward-merged into `main` (main HEAD `e33649a` ==
  the refactor tip). Nothing to rebase; the branch can be safely deleted. Decision resolved.
- **Green baseline recorded** (`RUN_LOG.md`): backend InMemory **541 pass / 3 skip / 0 fail**; frontend
  `npm test` / `lint` / `tsc` / `build` all green.

## Status of every Phase 2 item
| id | Sev | Status | Proof |
|----|-----|--------|-------|
| `frontend-render-harness` | P1 | ✅ done | smoke 3/3 (RTL renders, HeroUI onPress fires, real client POST+CSRF) — `d442d61` |
| `frontend-share-capital-no-ui` | **P0** | ✅ done | 3/3 — `POST /share-capital` payload+CSRF; missing-fields no POST — `1f5bd3a` |
| `frontend-loans-no-ui` | **P0** | ✅ done | 3/3 — `POST /loans` payload + derived due-split + CSRF; bad dates no POST — `dd675c4` |
| `frontend-director-loans-no-entry` | P1 | ✅ done | 3/3 — `POST /director-loans` payload+CSRF; no-directors blocked — `add057d` |
| `frontend-role-gating` | P2 | ✅ done | 4/4 — `canWrite=false` hides add form + delete; default keeps them — `d75f109` |
| `frontend-inline-edit-yearend` | P2 | ✅ done | 3/3 — edit issues `PUT .../{id}` (id preserved), no POST — `4e47a2c` |
| `frontend-unsaved-changes-guard` | P2 | ✅ done | 3/3 — shared hook adds/removes beforeunload; handler cancels unload — `e2119ed` |
| `tests-csv-real-export-fixtures` | P1 (HD) | ⏸ BLOCKED | needs real anonymised bank CSV exports — flagged, not fabricated |

**Every P0/P1/P2 entry-UI item is closed and render-proven. The one remaining Phase 2 item is blocked on
a real-world artefact (bank CSV samples) and is flagged, not guessed.**

## Commits (oldest → newest) — each = one item + its test
| Commit | Item | Test(s) |
|--------|------|---------|
| `6986b09` | tests-ci-filing-path-on-postgres (CI fix) | golden path 2/2 on real Postgres + InMemory |
| `d442d61` | frontend-render-harness | harness.smoke 3/3 |
| `1f5bd3a` | frontend-share-capital-no-ui (**P0**) | share-capital 3/3 |
| `dd675c4` | frontend-loans-no-ui (**P0**) | loans 3/3 |
| `add057d` | frontend-director-loans-no-entry (P1) | director-loans 3/3 |
| `66c9ccd` | RUN_LOG baseline + P0/P1 (docs) | — |
| `d75f109` | frontend-role-gating (P2) | role-gating 4/4 |
| `4e47a2c` | frontend-inline-edit-yearend (P2) | inline-edit 3/3 |
| `e2119ed` | frontend-unsaved-changes-guard (P2) | unsaved-changes 3/3 |

## What was built (and where the money now flows)
- **Share capital** — `ShareCapitalCard` (company-scoped) on the company detail page. Issued shares feed
  `BalanceSheet.capitalAndReserves.shareCapital`, the Share Capital note and SOCIE `sharesIssued`;
  `TotalValue` is recomputed server-side. A non-CLG company with no share capital is a readiness blocker,
  so this is the only way to clear it through the UI.
- **Loans** — `LoansManager` replaces the year-end Loans section's dead-end ("managed in Company Setup").
  The `DueWithinYear`/`DueAfterYear` split (which feeds creditors due within/after one year on the BS) is
  derived from the balance so it always cross-adds; `balanceAsOfDate` defaults to period end so the loan
  lands in this period.
- **Director loans** — `DirectorLoansManager` (period-scoped) makes director loans enterable, so
  `directorLoanCompliance` is no longer always null: the s.236 / overdrawn-DLA threshold test and the SAP
  warning now actually fire. Director dropdown is sourced from the company's director officers; the
  backend still validates the director served during the period.
- **Role gating** — all three surfaces honour `canWriteWorkingPapers` (Owner/Accountant); a read-only
  Client sees figures but no add/delete controls. The backend remains the authority.
- **Inline edit** — all three support PUT-based editing (id + audit preserved), not delete + re-add.
- **Unsaved-changes guard** — the shared `useUnsavedChanges` hook now covers notes/classify/charity/year-end.

## Flagged decisions / scope notes (no behaviour invented)
1. **Share-capital home** — placed on the company detail page (company-scoped, alongside Officers), matching
   the `/share-capital` company-scoped endpoint. Loans/director-loans live in their year-end sections.
2. **Loan due-split** — derived as `balance − dueWithinYear` so it cross-adds (backend does not enforce
   this for a bare `Loan` row); `balanceAsOfDate` defaulted to period end. No new API surface.
3. **Director-loan closing/max** — closing = opening + advances − repayments; max-during-year defaults to
   the larger of opening/closing (the figure the 10%-of-net-assets test uses), both overridable.
4. **Role gating is UX hardening, not a security boundary** — only the three new money surfaces are gated;
   exhaustive gating of every legacy period/year-end/filing control (approve-adjustment, finalise/file) is
   a recommended follow-up. The backend `RoleAuthorizationService` already rejects those for ineligible roles.
5. **Unsaved-changes for year-end** — limited to the explicit-Save payroll panel; row sections auto-persist
   on add so they are intentionally excluded from the dirty signal.
6. **`tests-csv-real-export-fixtures`** — needs real anonymised AIB/BOI/Revolut/Stripe exports; flagged, not
   fabricated (per guardrails).

## How to verify locally
```bash
cd frontend && npm test            # 13 unit + 22 render (7 files) + node verifiers — all green
cd frontend && npm run lint && npx tsc --noEmit && npm run build   # all green
# backend (only the pre-flight CI fix touches it):
cd backend && dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art   # 541 pass / 3 skip
# the golden-path Postgres test, against a real DB:
#   ACCOUNTS_POSTGRES_TEST_CONNECTION=... dotnet test ... --filter FilingGoldenPathPostgres
```

## Where the next run could go (Phase 2 is complete; these are beyond it)
1. **Live end-to-end smoke** — the render tests prove each form issues the exact request; a browser-driven
   pass (dev server + API + real login/CSRF) would additionally prove the round-trip visually. Optional;
   the stated acceptance is render-test-based and is met.
2. **`tests-csv-real-export-fixtures`** — unblock with real anonymised bank CSV samples.
3. **Exhaustive role gating** of the legacy period/year-end/filing controls (follow-up to `frontend-role-gating`).
4. Remaining backlog phases (4/5/6 infra, FRC taxonomy, opening-TB take-on) — out of this run's scope.

**Do not merge.** Branch left for review. Merging restores CI-green on `main`.
