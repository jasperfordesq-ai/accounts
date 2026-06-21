# Platform Audit & Finish-Line Roadmap — 2026-06-21

> Produced by an 83-agent grounded audit (8 domain auditors → adversarial verification of every
> P0/P1 → completeness critics → synthesis). 96 raw findings → **67 confirmed P0/P1**, 5 dropped as
> false-positive/overstated, 24 P2, 17 missing categories. This file is the **single source of truth**
> for the backlog. Work it top-down by phase; within a phase, by severity.

## Honest state of the platform
An impressively broad, security-conscious **skeleton** of an Irish statutory-accounts engine — auth,
tenancy, RBAC, audit-integrity, a full year-end data model, statement computation, QuestPDF and a
custom iXBRL generator are all present and mostly wired. **But it is not filing-grade, and several
"green" signals are false confidence.**

The money paths have **confirmed correctness defects**:
- **Tax is double-counted on the balance sheet** — `Creditors.Type==Tax` rows *and* every
  `TaxBalances.Balance` are both summed (`FinancialStatementsService` ~637–642).
- The **P&L tax charge is an unreconciled free-text figure** never compared to the CT computation, so
  equity/SOCIE/iXBRL/CT1 can silently disagree.
- **Year-2+ cash is computed from current-period transactions only** (~line 629), so multi-year
  accounts silently mis-balance unless a human re-keys opening balances.
- **Share capital is plugged to €1** to force a balance (~line 670) and fabricates a "1 Ordinary
  share" note.
- **Proposed (unpaid) dividends wrongly reduce reserves.**
- **iXBRL is hardcoded to FRS-102 with a full public P&L for every regime** (illegal for Micro/Abridged)
  and **rounds each fact independently** so subtotals need not cross-add (a ROS/CRO calc-check reject).

Worse, the **tests give false confidence**: the suite runs almost entirely on EF **InMemory**, PDFs
are checked only for `%PDF` magic bytes, iXBRL is never taxonomy-validated, and CI never produces a
filed output on real Postgres — so these defects ship green.

On the **product** side, whole categories required to actually file are missing: no UI to enter
**loans, director loans or share capital** (so any company with borrowings or real equity cannot reach
a correct balance sheet through the app); no **opening-trial-balance / take-on** for existing companies;
no **B1 annual-return** object or shareholder register; no **ROS-agent/TAIN / CRO-presenter / engagement**
model (no recorded authority to file); no **machine-readable CRO/CT1 export** (the journey stops at a
PDF + JSON blob); no **deadline delivery**, **preliminary-tax tracker**, **amended-filing** concept,
**immutable as-filed snapshot**, **firm-admin/support console**, or **GDPR data-subject** machinery.

**Bottom line:** the computation core is ~70% mapped but cents-wrong in known ways, the filing endgame
is a demo, and the trust scaffolding (verification, observability, recoverability, agent/engagement
model) is thin. Strategy: make the numbers **provably correct and the balance sheet provably balance
across years** (tested on real Postgres), **then** make the data fully enterable, **then** make the
statutory outputs regime-correct and legally dated/snapshotted, **then** build the real filing/agent/ops
layer.

## Themes
1. **Make the money correct & self-consistent** — de-double-count tax, reconcile the tax charge to the
   CT computation, fix multi-year cash on a movement basis, tie cash-flow to the balance sheet, stop
   fabricating €1 share capital and proposed-dividend reserves; every primary statement must internally
   cross-add or block.
2. **Prove correctness where it ships** — run the golden path on real Postgres in CI, assert
   `UnexplainedDifference==0` in years 2+, parse PDF/iXBRL content, validate iXBRL against the FRC
   taxonomy, stand up a frontend render harness.
3. **Make all the money enterable** — loans, director-loans, share-capital UI; inline edit; opening-TB
   take-on; role-gated controls.
4. **Regime-correct, legally-dated outputs** — branch iXBRL/PDF by regime; drive the directors' report
   from its service; persist a real board-approval date + immutable as-filed snapshot; gate audited
   entities on a real auditor's report.
5. **Data safety at multi-user scale** — optimistic concurrency, soft-delete/recoverability, period
   state-machine, validation breadth, EF tenant query-filter backstop.
6. **Real filing & agent operating model** — ROS-agent/TAIN + CRO-presenter + engagement, B1 + share
   register, CT1 numbered-field mapping, preliminary-tax/surcharge tracker, amended-filing chain,
   machine-readable exports.
7. **Operate it without an engineer** — structured logging + metrics/alerting, monitored backups +
   upgrade-on-populated-DB CI, deadline reminders, firm-admin/support console, GDPR tooling.

---

## Backlog by phase
Legend: **Sev** P0 (blocks trust) / P1 (important) / P2 (polish) · **Eff** S/M/L/XL · **HD** = needs a
human/business/legal decision (implement the conservative default + flag — do not decide).

### Phase 0 — Diagnosability first (make CI tell the truth)
*Stand up the harnesses that will catch the correctness work, so every later fix is provable.*

| id | Sev | Eff | HD | Title / acceptance |
|----|-----|-----|----|--------------------|
| `frontend-render-harness` | P1 | L | – | **React render/component harness for money-entry surfaces.** Vitest + @testing-library/react in CI; smoke tests assert year-end sections render, an add-debtor form issues the expected POST (path/method/payload/CSRF), and the filing tab renders the correct per-status button. The ~1,900-line year-end + ~2,440-line period components have zero render coverage today. |
| `tests-ci-filing-path-on-postgres` | P1 | L | – | **Run the golden filing path on real Postgres in CI.** onboard→categorise→year-end→balance→PDF→iXBRL on a real PostgreSQL service asserting `UnexplainedDifference==0`, a `%PDF`-prefixed PDF and well-formed iXBRL. Today every statement/PDF/iXBRL is exercised only on InMemory. |
| `tests-pdf-content-verified` | P1 | M | – | **Parse PDF text and assert figures/names/wording.** Micro + Medium parse the accounts-package PDF and assert company legal name, period-end, BS total/net assets == computed `BalanceSheet`, and required wording (s.280D micro / s.352 abridged). Today PDFs are checked only for `%PDF` + length. |
| `ops-backend-vuln-scan` | P1 | S | – | **Fail CI on vulnerable backend NuGet.** `NuGetAudit warnaserror` (NU1901-1904) or `dotnet list package --vulnerable`. CI audits npm but not NuGet, yet the backend parses untrusted CSV and handles auth/crypto. |
| `import-csv-formula-injection` | P2 | S | – | **Neutralise CSV formula-injection on imported text.** `CleanCsvField` only trims; a `=HYPERLINK(...)` memo executes when a user later exports to Excel. Neutralise leading `= + - @` and tab/CR/LF. |

### Phase 1 — Make the money correct and self-consistent
*Eliminate the confirmed wrong-money defects; every primary statement must internally cross-add or block.*

| id | Sev | Eff | HD | Title / acceptance |
|----|-----|-----|----|--------------------|
| `accounting-opening-balance-pl-accounts` | **P0** | S | – | **Reject opening balances posted to Income/Expense accounts.** Opening balances on a 4xxx/5xxx/6xxx code fold into current-year turnover/expenses (what a mid-year migration does). `UpsertOpeningBalanceEndpoint` → 400 when `AccountCategory.Type` is Income/Expense; test: €10k opening credit to code 4000 rejected, turnover unchanged. |
| `accounting-tax-balance-internal-consistency` | P1 | S | – | **Validate TaxBalance triple** (non-negative, `Balance == Liability − Paid`). `UpsertTaxBalance` stores verbatim; an inconsistent triple mis-states creditors and profit-after-tax. |
| `accounting-tax-creditor-double-count` | P1 | M | ✓ | **Stop double-counting tax** — `Creditors.Type==Tax` rows AND `TaxBalances.Balance` both summed (~637–642). Decide the single source; test: a €X tax creditor + €X TaxBalance → `taxCreditors == X` not 2X, `UnexplainedDifference` stays 0. |
| `accounting-pl-tax-charge-unreconciled` | P1 | M | ✓ | **Block readiness when entered CT diverges from the CT computation.** P&L tax charge reads free-text `TaxBalances.Liability` never compared to `TaxComputationService`. Readiness blocker when they differ by >€1. Depends: `accounting-tax-creditor-double-count`. |
| `accounting-share-capital-and-dividends-reserves` | P1 | M | ✓ | **Remove the €1 share-capital plug; exclude proposed (unpaid) dividends from reserves.** No share capital → readiness blocker + explicit "not recorded" note (no €1 plug); `DatePaid==null` dividend must NOT reduce BS/SOCIE reserves; paid dividend reduces reserves + financing cash-flow consistently. |
| `accounting-multiyear-cash-movement-basis` | P1 | XL | ✓ | **Year-2+ balance-sheet cash on a true movement basis** (carry prior closing cash). Cash = `bank.OpeningBalance + Σ(this-period txns)` (~629) omits prior years. Test: 3-year fixture, bank opening 0, no manual rows → year-2/3 cash == cumulative net movement AND `UnexplainedDifference==0`. Depends: opening-balance fix. |
| `accounting-cashflow-vs-bs-cash-tie` | P1 | L | – | **Cash-flow closing cash must tie to BS cash (or block).** Different routes/cutoffs today. Test: multi-account period → `CashFlow.ClosingCash == BalanceSheet.Cash`; divergence → readiness warning. Depends: movement basis. |
| `tests-multiyear-balance-asserted` | P1 | L | – | **Assert a year-2+ balance sheet balances with no manual openings.** ≥2-year chain, no manual opening rows → `UnexplainedDifference==0 && Balances`. Must fail on current code, pass after the refactor. Depends: movement basis. |
| `accounting-vat-paye-reconciliation` | P1 | L | ✓ | **Reconcile entered VAT/PAYE to source** (VAT-coded txns / payroll) with a readiness warning when they diverge. Depends: tax double-count. |
| `validation-pre-filing-consistency-pass` | P1 | M | – | **One pre-filing internal-consistency gate before approval** — BS balances (incl. year-2+), cash-flow ties to BS cash, reserves tie across BS/SOCIE/cash-flow, tax provision ties to CT computation, iXBRL subtotals cross-add — else block with specifics. Depends: cash-flow tie. |
| `accounting-ixbrl-rounding-subtotals` | P2 | M | – | **Derive iXBRL subtotals from rounded components so they cross-add** (round children first, then total). Each fact is `Math.Round(amount,0)` independently (~193–195). |
| `accounting-retained-earnings-snapshot` | P2 | M | ✓ | **Persist prior-period closing reserves** instead of recursively recomputing prior-year P&L (O(n²) + retroactive drift). Finalising writes a fixed opening-reserves figure. Depends: movement basis. |
| `accounting-depreciation-regeneration-order` | P2 | L | – | **Recompute-forward (or block) depreciation/CA roll-forward when a prior period is regenerated.** Stale `ClosingNbv`/claim counts can push cumulative depreciation over cost; Finalised/Filed → 409. Depends: movement basis. |

### Phase 2 — Make all the money enterable
*Give firm staff a UI for every figure the balance sheet needs, with safe editing and role gating.*

| id | Sev | Eff | HD | Title / acceptance |
|----|-----|-----|----|--------------------|
| `frontend-loans-no-ui` | **P0** | L | – | **Loans entry/edit UI** wired to loan + snapshot endpoints; year-end page misdirects to a non-existent "Company Setup" screen today. Test: create a loan → appears in year-end Loans total and `creditorsAfterYear`. |
| `frontend-share-capital-no-ui` | **P0** | M | – | **Share-capital entry UI** (company-scoped, `/share-capital`). Test: issue shares → Share Capital note + SOCIE `sharesIssued` populate, `BS.capitalAndReserves.shareCapital` reflects issued total. |
| `frontend-director-loans-no-entry` | P1 | L | – | **Director-loan create/edit UI.** Today display-only → `directorLoanCompliance` always null, s.236 warnings never fire, overdrawn-DLA reclassification never generated. Depends: loans UI. |
| `frontend-inline-edit-yearend` | P2 | M | – | **Inline edit of year-end rows** via existing `update*` helpers (PUT, not delete+re-add) preserving id/notes/audit continuity. |
| `frontend-role-gating` | P2 | M | – | **UI role gating** via `canWriteWorkingPapers`/`canReview` — hide/disable mutating + approve/file controls for ineligible roles (Client sees neither Approve nor mutating buttons). |
| `frontend-unsaved-changes-guard` | P2 | M | – | **Shared unsaved-changes guard** across notes/year-end/classify/charity (today only on notes). |
| `tests-csv-real-export-fixtures` | P1 | M | ✓ | **Import fixtures from real anonymised AIB/BOI/Revolut/Stripe exports** (current tests are the parser agreeing with itself). Assert exact row count/signs/amounts (AIB money-in must post) + a malformed/BOM variant that fails loudly. *(Needs real sample files — flag.)* |

### Phase 3 — Regime-correct, legally-dated statutory outputs
| id | Sev | Eff | HD | Title / acceptance |
|----|-----|-----|----|--------------------|
| `filing-ixbrl-regime-taxonomy-branch` | **P0** | L | ✓ | **Branch iXBRL by regime** — FRS-105 + no P&L for Micro, balance-sheet-only for Abridged; FRS-102 + full P&L for Small/Medium/Full. Today every regime publishes a full P&L (illegal for Micro/Abridged). Depends: tagging set. |
| `filing-ixbrl-tagging-completeness` | P1 | L | ✓ | **Tag the FRS mandatory-minimum concept set** (avg employees, directors' remuneration, audit-exemption, going-concern/dormant, policies, BS date). Today ~12 headline numbers only. |
| `filing-ixbrl-namespace-taxonomy-pin` | P1 | M | ✓ | **Pin a real FRC Irish taxonomy date; reconcile `core:`/`ie-FRS-102` prefixes** (UK `core:` concepts under an Irish schemaRef; `TaxonomyDate 2026-01-01` is a placeholder). Validate offline in CI. Depends: tagging set. |
| `tests-ixbrl-structural-validation` | P2 | L | ✓ | **Validate iXBRL structure offline** — every `contextRef`/`unitRef` resolves, every concept prefix declared, concepts on a curated FRS-102 allow-list. Depends: tagging set. |
| `filing-directors-report-from-service` | P1 | M | – | **Render the PDF directors' report from `DirectorsReportService`** (dormant wording when not trading, dividend disclosure, audit-info statement only when not audit-exempt). Today hardcoded boilerplate that falsely states a dormant company traded. |
| `filing-abridged-cro-directors-report` | P1 | S | – | **Include the Directors' Report in the SmallAbridged CRO pack** (its own doc-comment says it does; it's omitted). Depends: directors'-report-from-service. |
| `filing-approval-date-persisted` | P1 | M | ✓ | **Persist a board-approval date and stamp it everywhere** (PDF, notes, iXBRL, signature page) instead of `DateTime.Now` at render (BL-24). Approve on date X, regenerate later → still X. |
| `filing-auditor-report-blocks-final` | P1 | M | ✓ | **Block final outputs for non-audit-exempt entities until a signed auditor's report is attached.** Today audited entities get only a template yet readiness lets the pack + iXBRL generate. |
| `filing-charity-pdf-and-reconciliation` | P1 | L | ✓ | **Charity Trustees' Report + fund-column SoFA PDF**, with SoFA total funds reconciled to BS net assets (block/warn on mismatch). Today JSON only (BL-12). |
| `signing-approval-chain` | P1 | M | ✓ | **Capture director/secretary signatories + signed-PDF/e-signature retention**; submission blocked until signatories present. Depends: persisted approval date. |

### Phase 4 — Data safety, integrity and tenant backstop
| id | Sev | Eff | HD | Title / acceptance |
|----|-----|-----|----|--------------------|
| `data-no-optimistic-concurrency` | P1 | L | – | **Optimistic-concurrency tokens** (`xmin`/RowVersion) on mutable accounting entities; a stale-snapshot save → 409 "record changed", not last-write-wins clobber. |
| `data-company-soft-delete` | P1 | M | ✓ | **Soft-delete + recoverability for companies** (or block hard-delete when any period holds financial data + typed confirmation). Today one DELETE cascade-wipes everything irreversibly. |
| `tenant-ef-query-filter-backstop` | P1 | L | – | **EF global query filter on `TenantId`** so cross-tenant queries return zero rows even if `CompanyEndpointAccess` is missed; guard/test fails CI if a new sub-resource endpoint omits scoping. |
| `data-period-status-state-machine` | P2 | M | ✓ | **Legal `PeriodStatus` transition table** — reject illegal jumps (Draft→Filed must pass Finalised; reopen only via Owner+reason). |
| `data-input-validation-breadth` | P2 | M | – | **DTO/validation for category, note, related-party, post-balance-event, contingent-liability creates** (ignore client `Id`/`IsSystem`/`CompanyId`; required text + length; non-negative; same-company `ParentId`). |
| `data-list-transactions-pagesize-cap` | P2 | S | – | **Cap list-transactions page size** (e.g. 200); `Take(pageSize ?? 50)` is unclamped → memory/DoS. |
| `data-period-lock-toctou` | P2 | L | – | **Re-validate period lock under a row lock inside the write transaction** (`SELECT…FOR UPDATE`/concurrency token); a finalise mid-request must not commit into a now-locked period. Depends: concurrency. |
| `data-idempotency-creates-import` | P2 | L | ✓ | **`Idempotency-Key` on create endpoints + import**; a retried POST must not double-insert. |

### Phase 5 — The real filing & agent operating model
| id | Sev | Eff | HD | Title / acceptance |
|----|-----|-----|----|--------------------|
| `onboarding-opening-trial-balance-takeon` | **P0** | XL | ✓ | **Opening trial balance / take-on for existing companies + carried comparatives.** Without it the platform is trustworthy only for a company's first year. Take-on reconciles (Dr==Cr), feeds comparatives in statements + iXBRL. Depends: movement basis. |
| `agent-ros-cro-engagement-model` | P1 | L | ✓ | **ROS-agent/TAIN + CRO-presenter + per-client engagement records gating filing** (no recorded authority to act today). |
| `b1-annual-return-data-object` | P1 | L | ✓ | **B1 annual return + shareholder register** as a data object distinct from the statements; allotments/transfers reflected in B1 + SOCIE/share note. Depends: share-capital UI. |
| `filing-cro-ros-machine-export` | P1 | XL | ✓ | **Machine-readable CRO B1 + ROS CT1 exports** (or an explicit field-by-field CORE/ROS worksheet). The journey stops at PDF + JSON today — the single biggest filing-grade gap. |
| `filing-ct1-numbered-field-mapping` | P1 | M | ✓ | **Map CT1 figures to numbered panels/fields** (Trading Results, Extracts From Accounts, …). Depends: machine export. |
| `filing-preliminary-tax-tracker` | P1 | M | ✓ | **Preliminary-tax tracker** — PT due date, 90/100% safe-harbour, small/large rules, late-CT1 surcharge exposure. |
| `filing-amended-filing-and-snapshot` | P1 | L | ✓ | **Amended/superseding filing chain + immutable as-filed snapshot** (freeze exact PDF/iXBRL/CT1/figures on submit; refile creates a distinct amended record referencing the original). |

### Phase 6 — Operate it without an engineer
| id | Sev | Eff | HD | Title / acceptance |
|----|-----|-----|----|--------------------|
| `ops-upgrade-on-populated-db` | P1 | L | – | **CI applies new migrations on a populated prior-release DB** (seed prior tag → migrate → assert success, preserved rows/figures, `/health/ready==200`). Today CI only migrates a fresh DB. |
| `ops-backup-automated-monitored` | P1 | M | ✓ | **Scheduled, monitored, off-host backups + success/failure alerting** (today a manual PowerShell dump, no scheduler/alert). |
| `filing-deadline-reminders` | P1 | L | ✓ | **Scheduled deadline reminder delivery (email) + firm-wide at-risk dashboard.** Deadlines are computed but only shown on login — a missed CRO/CT deadline is the costly trust-destroyer. |
| `ops-firm-admin-support-console` | P1 | L | ✓ | **Firm-admin/support console** (users, lockouts, access, restore soft-deleted company) + figure drill-down — so incidents don't escalate to the developer. Depends: soft-delete. |
| `privacy-gdpr-data-subject` | P1 | L | ✓ | **GDPR data-subject export + erasure** with statutory-retention override (tombstone, keep audit-chain valid). |
| `ops-metrics-tracing` | P1 | L | ✓ | **OpenTelemetry/Prometheus metrics** (request rate, latency, 5xx, DB pool) on an internal endpoint. Depends: structured logging. |
| `ops-structured-logging` | P2 | M | – | **Structured JSON request logging** with correlation id + tenant/company/period in Production. |
| `crypto-tls-to-db` | P2 | M | ✓ | **Require `sslmode` on the DB connection outside Development** (fail boot otherwise). |
| `auth-login-ratelimit-account-dim` | P2 | M | ✓ | **Account-dimension login throttle / global ceiling** (today IP-keyed only → credential-stuffing across IPs; per-account lockout weaponisable as DoS). |

---

## Discovered during the finish run (logged, not in the original 59)
| id | Sev | Eff | HD | Title / acceptance |
|----|-----|-----|----|--------------------|
| `accounting-paye-payroll-source-reconciliation` | P2 | M | ✓ | **Reconcile entered PAYE/PRSI to payroll source.** `accounting-vat-paye-reconciliation` delivered the VAT side (entered VAT vs the 1300/2200 control accounts). The PAYE side is not implemented because `PayrollSummary` records only GrossWages/EmployerPrsi/PensionContributions/StaffCount — there is no employee PAYE/PRSI-withheld field, so an entered PAYE `TaxBalance` cannot be reconciled precisely to payroll. Extend the payroll model (employee PAYE + PRSI withheld) then add the reconciliation warning. |
| `accounting-cashflow-accrual-reconciliation` | P2 | L | ✓ | **Reconcile the indirect cash-flow to the balance-sheet cash for accrual companies.** The cash-flow opening cash now carries forward prior-period movement and the closing cash ties to BS cash for a *cash-consistent* set (`accounting-cashflow-vs-bs-cash-tie`). But when year-end debtors/creditors/stock are entered as standalone balances that are not reflected in bank transactions, the indirect `netIncreaseInCash` does not equal the actual cash movement, so a strict readiness "closing cash != BS cash" warning would (correctly) fire on those sets — including the artificially-constructed Small golden-path fixture. Resolve the accrual model (or make the fixture cash-consistent) then add the readiness divergence warning. |

## Counts
- Raw findings: **96** · Confirmed P0/P1: **67** · Dropped (false-positive/overstated): **5** · P2: **24** · Missing categories: **17**
- Backlog after dedupe: **59 items** — ~5 P0, ~38 P1, ~16 P2.

## Human-decision items (implement the conservative default + FLAG — do not decide)
The single source of tax truth (creditor vs TaxBalance); loss-relief / CT reconciliation policy;
which regimes/entities ship in v1; the FRC Irish taxonomy version + ROS/CRO acceptance (external gate);
the auditor's-report engagement scope; charity SORP tier/format; hard-delete vs soft-delete policy;
period reopen policy; concurrency UX; GDPR retention schedule; ROS-agent/TAIN + CRO-presenter model;
CT1 field mapping + CRO/ROS export format; PT rules; backup schedule/retention; DB TLS posture;
login-throttle policy; and any behaviour depending on a real-world fact you cannot verify (e.g.
AIB/BOI/Stripe CSV column layouts — flag, test coded behaviour only, don't guess).

> Full evidence (file:line) for each finding is in the workflow transcript under
> `…/subagents/workflows/wf_ce47d445-e76`. The "why" lines above embed the key references.
