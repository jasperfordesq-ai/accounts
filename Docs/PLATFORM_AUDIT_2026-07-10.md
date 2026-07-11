# Platform Audit and Production-Readiness Remediation Plan — 2026-07-10

This document is the canonical implementation backlog produced from the full repository,
backend/statutory, frontend/UI, security, operations, release-evidence, and retained visual
artifact audit completed on 10 July 2026.

It supersedes `PLATFORM_AUDIT_2026-06-21.md` for unfinished remediation work. `AGENTS.md`
and `CLAUDE.md` remain the repository guidance and active-goal handoff, but completion claims
must be measured against this document.

## Audit baseline

- Audited commit: `7ea54cc6d1769ced568ac1568d190cc2bb4b16d1`
- Branch: `main`
- Independent score: **600/1,000**
  - Backend, accounting, and statutory outputs: **205/350**
  - Frontend workflow and UI/UX: **158/250**
  - Security, data integrity, and operations: **140/250**
  - Architecture, maintainability, and documentation: **97/150**
- Practical state: strong pre-production beta; not safe for production accounting data or
  real CRO/Revenue filing work.
- Backend baseline: 624 passed, 3 PostgreSQL-only tests skipped, 0 failed.
- Frontend baseline: 134 unit/contract tests and 109 render tests passed; lint, TypeScript,
  API-client checks, and production build passed.
- Current machine CI was green, but machine evidence does not clear the substantive findings
  or the seven missing human/external evidence gates.

## Current evidence-derived reassessment

The control-derived implementation score after the verified autonomous engineering work is
**783/1,000**:

- Backend, accounting, and statutory outputs: **250/350**
- Frontend workflow and UI/UX: **203/250**
- Security, data integrity, and operations: **215/250**
- Architecture, maintainability, and documentation: **115/150**

This is a release-blocked engineering score, not a filing-readiness declaration. No points have
been restored for independent statutory/golden-corpus acceptance, named accountant or source-law
review, real external ROS validation, named browser/visual/accessibility acceptance, real
monitoring/backup operations, durable published human evidence, or the remaining combined
supply-chain/resilience controls. The backend score is supported by a strict 1,031-test PostgreSQL
Release run with zero failures/skips; the frontend score is supported by 277 unit and 225 render
tests plus contract/lint/type/build gates. Exact remote candidate CI and governance evidence must
still be attached before this reassessment is release evidence.

## Non-negotiable product boundary

1. Direct CRO and ROS submission remains unsupported. The platform may only generate artifacts
   and record external workflow states.
2. Every real-world filing path must fail closed until the exact generated artifacts have all
   applicable external validation, signed evidence, and named qualified-accountant approval.
3. Never fabricate or pre-complete human evidence, legal review, provider confirmation,
   professional credentials, auditor reports, external ROS results, signatures, or acceptance.
4. Unsupported company types, accounting standards, tax cases, group contexts, regulated
   entities, audit cases, and ambiguous legal decisions must route to explicit manual handoff.
5. Internal XML parsing, self-authored golden tests, a green CI run, or the platform's own
   readiness score must never be represented as external filing acceptance.
6. Preserve tenant isolation, audit integrity, immutable release identity, and exact artifact
   hashes throughout every remediation.

## Definition of done

The production-readiness goal is complete only when all of the following are true:

- Every P0 and P1 item below is implemented and objectively verified.
- No unresolved critical or high audit finding remains.
- Full backend and frontend suites pass on the exact release commit.
- Required PostgreSQL integration tests run and may not skip in release CI.
- The exact production images are built once, scanned, attested, promoted by digest, and used
  by production smoke.
- Realistic multi-period golden scenarios traverse public services/endpoints and independently
  derived expected results rather than seeding calculated states.
- Every supported iXBRL artifact passes the applicable official taxonomy checks and retained
  external ROS validation against its exact SHA-256.
- A currently qualified named accountant accepts the exact outputs, wording, legal basis,
  workflow gates, and visual journey for every supported golden scenario.
- Audit-required and unsupported paths have retained manual-handoff evidence.
- Named visual QA, source-law review, real monitoring-provider confirmation, and real backup/
  restore evidence are complete.
- Accessibility and visual acceptance cover all production routes and material states.
- The release artifact pack contains zero blocking failures and matches the exact commit, CI run,
  image digests, machine evidence, human evidence, and generated-output hashes.
- `verify-no-direct-filing-submission.ps1` continues to pass for the exact candidate.

## Execution rules

- Work in dependency order. Do not jump to P2 polish while a related P0 correctness or security
  issue remains open.
- Treat each backlog row as a vertical slice: regression test, implementation, focused
  verification, full relevant gate, documentation update, and coherent commit.
- Where practical, add a test that fails before the fix and passes after it.
- Prefer scalar request/response DTOs, explicit mapping, shared domain guards, and database
  invariants over endpoint-local conventions.
- Use official CRO, Revenue, Irish Statute Book/Revised Acts, FRC, and Charities Regulator sources
  for legal or taxonomy decisions. Record the effective date and source URL. Do not guess.
- Do not weaken, delete, skip, or rewrite tests simply to make a gate green.
- Do not add evidence-verifier prose as a substitute for fixing the underlying product behavior.
- Keep `main` as the integration branch, in line with `AGENTS.md`. Preserve unrelated work and
  keep the worktree clean between coherent slices.
- Update this checklist as work lands. A checked box requires passing objective evidence, not
  implementation intent.

## Phase 0 — Immediate containment and truthful release state

### [x] P0-REL-001 — Introduce one server-side final filing release gate

Create a single `FilingReleaseGate` (or equivalently central domain service) used by every
transition to `Approved`, `Submitted`, `Accepted`, or `Filed` for CRO, Revenue, and charity
workflows.

It must require, for the exact tenant, company, period, release candidate, and artifact hashes:

- complete and current financial readiness;
- valid size classification and an eligible elected regime;
- immutable generated artifacts;
- current named qualified-accountant approval;
- applicable signatures and signed auditor evidence;
- external ROS validation for Revenue;
- retained external filing references;
- no unresolved unsupported/manual-handoff condition.

Missing, stale, malformed, unavailable, or mismatched evidence must block without mutating the
workflow state.

Acceptance:

- Endpoint-matrix tests remove every prerequisite in turn and prove `409`, unchanged domain state,
  and an appropriate rejected-attempt security/audit event. Cross-tenant or unauthorized failures
  may be recorded only in the authenticated actor/tenant security chain, never in the guessed
  target company's domain audit chain.
- Changing one byte of an approved artifact invalidates approval.
- Free-form status text and internal validation strings cannot satisfy a gate.
- Only a complete exact-hash evidence set permits the transition.

Evidence:

- `backend/Accounts.Api/Services/DeadlineService.cs:191`
- `backend/Accounts.Api/Services/FilingReadinessProfileService.cs:294`
- `backend/Accounts.Api/Services/FilingWorkflowService.cs:345`

Completion evidence (2026-07-10): `FilingReleaseGate` is the single fail-closed boundary for final
exports and final CRO, Revenue, and charity workflow states. Release/workflow regression tests pass
34/34, including exact-byte/candidate drift, credential scope/tenant/expiry/decision, structured
external validation, public free-form no-mutation, and unsupported audited-path cases. Trusted
credential/upload/provider integrations and signed-auditor attachment remain separately blocked by
P0-STAT-002 through P0-STAT-004 rather than being simulated.

### [x] P0-REL-003 — Separate review artifacts from final export and handoff

Before the exact-hash release gate passes, users may obtain only conspicuously marked review
artifacts. Unwatermarked/final export and external handoff must use the same central release gate as
workflow transitions.

Acceptance:

- Pre-approval PDFs and iXBRL carry an unmistakable `DRAFT — NOT FOR FILING` status appropriate to
  the format and cannot be downloaded through a final-export endpoint.
- Final export and handoff fail while any exact-hash approval, signature, external validation, or
  manual-handoff requirement is missing.
- Regeneration changes the hash and immediately revokes prior final-export eligibility.

Completion evidence (2026-07-10): review PDF/iXBRL outputs carry format-appropriate
`DRAFT — NOT FOR FILING` markers; clean retained bytes can be returned only by final endpoints
through `FilingReleaseGate`; regeneration and release-candidate changes revoke approval.

### [x] P0-REL-002 — Replace the hard-coded readiness score with derived risk state

The current score grants full backend and security marks while critical issues and human gates
remain open. Replace literal category scores with independently derived controls, or remove the
numeric claim until the controls exist.

Acceptance:

- Security cannot report full marks while any security P0/P1 item is open.
- Backend cannot report full marks without external iXBRL validation and substantive statutory
  correctness.
- Every score increment maps to a passing objective control and exact-candidate evidence.
- The UI clearly distinguishes code completeness, machine assurance, and human/external evidence.

Evidence:

- `backend/Accounts.Api/Services/ProductionReadinessReportService.cs:845`
- `backend/Accounts.Api/Services/ProductionReadinessReportService.cs:942`

Completion evidence (2026-07-10): the dated 600/1,000 independent baseline is bound to audited
commit `7ea54cc6d1769ced568ac1568d190cc2bb4b16d1`. Category scores are sums of unique weighted
controls, open controls contribute zero and carry blocking audit IDs, candidate-bound artifact
evidence is required for later increments, and the workbench separates code, machine, and
human/external assurance. Backend scorecard tests, 60 frontend contract tests, TypeScript, API
client verification, and the release-evidence script parsers pass.

## Phase 1 — Critical security and data-integrity remediation

### [x] P0-SEC-001 — Eliminate writable EF entity graph binding

Replace persistence entities in request binding with scalar request DTOs. Explicitly map allowed
fields into new or loaded entities. Reject or ignore navigation objects, IDs, tenant/company IDs,
system flags, audit fields, workflow state, and calculated properties supplied by callers.

Start with bank accounts and categories, then audit every mutating endpoint for the same pattern.

Acceptance:

- PostgreSQL tests submit nested category children, rules, transactions, bank-account transactions,
  and import batches. The request returns 400 or ignores the graph.
- Only the intended root row is created, with a database-generated ID.
- Cross-tenant and locked-period row counts remain unchanged.
- A repository guard test fails when a persistence entity is newly bound as a public write request.

Evidence:

- `backend/Accounts.Api/Endpoints/BankingEndpoints.cs:25`
- `backend/Accounts.Api/Endpoints/BankingEndpoints.cs:133`
- `backend/Accounts.Api/Entities/BankAccount.cs:18`
- `backend/Accounts.Api/Entities/AccountCategory.cs:24`

Completion evidence (2026-07-10): all public POST/PUT endpoint inputs are scalar DTOs with explicit
mapping, and the repository guard rejects future persistence-entity request binding. The malicious
nested-graph HTTP matrix passes both in memory and on disposable PostgreSQL: database-generated root
IDs are retained while category children/rules/transactions and bank transactions/import batches
are ignored, with foreign-tenant and finalised/locked-period row counts unchanged.

### [x] P0-SEC-002 — Enforce tenant/company/period invariants at persistence boundaries

Add a `SaveChanges` validation/interceptor and database constraints where practical so related
bank accounts, periods, categories, import batches, transactions, filings, and evidence can never
cross tenant or company boundaries, even if an endpoint or service is defective.

Acceptance:

- Direct-service and endpoint tests attempt every cross-company and cross-tenant foreign-key
  combination and fail atomically.
- Direct raw-SQL/PostgreSQL tests prove the critical tenant/company/period constraints reject
  inconsistent rows even when application interceptors and services are bypassed.
- Query filters are not the only protection against invalid inserts.
- Required invariants are documented and covered by metadata regression tests.

Completion evidence (2026-07-10): `PersistenceOwnershipInvariantValidator` protects direct EF
writes, while migration `20260710170000_EnforcePersistenceOwnershipInvariants` adds PostgreSQL
preflight checks, constraints, immutable ownership anchors and cross-table triggers. Nineteen
application/metadata boundary tests pass; the raw-SQL trigger matrix passes on disposable
PostgreSQL and rejects inconsistent company, period, bank, import, transaction, category, filing,
deadline, audit and checkpoint relationships without relying on query filters.

### [x] P0-SEC-003 — Stop rejected cross-tenant probes mutating victim audit chains

Move rejected-request auditing behind verified tenant/company authorization, or record the event
only in an authenticated-tenant security chain that is not attached to the guessed target company.

Acceptance:

- A valid-CSRF cross-tenant mutation returns 404.
- The victim company's audit count, latest hash, and checkpoint remain byte-for-byte unchanged.
- An attacker-tenant security event may be retained without leaking target existence or joining the
  victim chain.

Evidence:

- `backend/Accounts.Api/Program.cs:185`
- `backend/Accounts.Api/Middleware/AuditTrailMiddleware.cs:47`
- `backend/Accounts.Api/Services/AuditService.cs:111`

Completion evidence (2026-07-10): tenant authorization now precedes domain audit middleware, and
company audit rows infer and validate the persisted tenant. The valid-CSRF cross-tenant mutation
scenario passes in memory and on disposable PostgreSQL: it returns the same 404 as a nonexistent
target while the victim audit count, serialized latest hash, checkpoint bytes and verified chain
tail remain unchanged.

### [x] P0-DATA-001 — Serialize finalisation and accounting writes

Use a PostgreSQL row/advisory lock for period writes and optimistic concurrency/ETag protection on
mutable accounting and workflow records. Revalidate lock and workflow state inside the same
transaction as each write.

Acceptance:

- Deterministic two-connection tests pause a write after its initial read while another request
  finalises the period.
- Exactly one operation succeeds; no financial row changes after the period lock commits.
- Stale updates return 409 with a safe reload/reconcile response.
- The final-output manifest remains stable after finalisation.

Evidence:

- `backend/Accounts.Api/Middleware/PeriodLockMiddleware.cs:19`
- `backend/Accounts.Api/Endpoints/PeriodStatusEndpoint.cs:33`
- `backend/Accounts.Api/Data/AccountingConcurrencyInterceptor.cs`
- `backend/Accounts.Api/Services/AccountingConcurrency.cs`
- `backend/Accounts.Api/Middleware/PeriodConcurrencyMiddleware.cs`
- `backend/Accounts.Tests/AccountingConcurrencyPostgresTests.cs`
- `frontend/src/lib/api.ts`
- `frontend/src/lib/apiProxyResponse.ts`

Completion evidence (2026-07-10): all mutable accounting and workflow writes now take a consistent
company-then-period PostgreSQL advisory/row-lock lease, re-read period/quarantine state inside the
write transaction, and reject original-value drift. Period-wide `xmin` ETags survive the proxy and
drive browser `If-Match` reload/reconcile handling. Ten fresh-schema disposable-PostgreSQL tests
pass, including a deterministic two-connection finalisation/write race proving one winner, no
post-lock financial mutation and stable approved-output manifests; coverage tests protect lock
ordering, DI/middleware wiring and the complete mutable-entity scope.

### [x] P0-DATA-002 — Make company deletion authoritative, recoverable, and auditable

Prefer soft deletion/quarantine with explicit recovery. If hard deletion remains, require exact
typed confirmation for every company, block locked/finalised companies, and retain an immutable
pre-delete inventory and reason.

Acceptance:

- Parameterized tests seed every dependent table individually; no populated company deletes
  without the required confirmation and authority.
- The current incomplete financial-data probe is removed or made authoritative.
- Successful deletion/quarantine retains actor, reason, confirmation, and per-table counts in a
  durable audit record.

Evidence:

- `backend/Accounts.Api/Endpoints/CompanyDeletionEndpoint.cs`
- `backend/Accounts.Api/Services/CompanyQuarantineService.cs`
- `backend/Accounts.Api/Services/CompanyDependentInventoryService.cs`
- `backend/Accounts.Api/Entities/CompanyQuarantineEvent.cs`
- `backend/Accounts.Api/Data/Migrations/20260710210000_AddCompanyQuarantineEvidence.cs`
- `backend/Accounts.Tests/CompanyQuarantineTests.cs`
- `backend/Accounts.Tests/CompanyQuarantinePostgresTests.cs`
- `frontend/src/components/dashboard/QuarantinedCompanyRecoveryPanel.tsx`

Completion evidence (2026-07-10): the public DELETE boundary now performs an Owner-only,
recoverable quarantine and never removes the company or an owned accounting row. It requires the
exact ordinal legal name and a specific 20–2,000-character reason, blocks locked, finalised and
filed periods, hides the company ownership graph from normal queries, rejects subsequent writes at
the serialized company/period save boundary, and exposes an Owner-only recovery register. Every
quarantine/recovery transition must be paired at save time with one matching valid evidence event.
The append-only event retains tenant/company identity, actor ID/name/role, typed confirmation,
reason, request ID, occurrence time, canonical per-table counts, total dependent rows, inventory
SHA-256, previous-event SHA-256 and event SHA-256; PostgreSQL rejects event UPDATE and DELETE.

The authoritative inventory registry covers all 43 current company/period-owned tables, including
the company root and retained audit/evidence tables. Forty-two parameterized dependent-table cases
exercise the real endpoint, prove Reviewer rejection, exact-confirmation rejection, row retention
and the recorded table count. The final canonical local slice passed 51 tests with only the
environment-gated PostgreSQL case skipped. A fresh-schema disposable PostgreSQL run then passed
1/1 with no skips, proving migrations have no pending model changes, quarantine waits for an
in-flight period write, captures the committed row, blocks a later write, retains the graph and
enforces database-level evidence immutability. Focused TypeScript and ESLint checks passed, along
with 6/6 quarantine/recovery render tests.

## Phase 2 — Statutory and accounting correctness

### [x] P0-ACC-001 — Correct period, threshold, size, and regime decisions

Implement:

- non-negative classification inputs;
- raw current/prior statutory tests and all upward/downward consecutive-year cases;
- turnover adjustment for short/long periods where required;
- effective-dated threshold selection and elections;
- first-year rules;
- exclusion and ineligible-entity rules;
- rejection of every ineligible Micro, small, or abridged election;
- an audited override path with authority, reason, evidence, and re-review behavior.

Acceptance:

- Decision-table tests cover statutory boundaries, 6/12/18-month periods, first years, current and
  historical thresholds, upward/downward grace, and all exclusion flags.
- Invalid elections cause no persistence.
- Override attempts without authority, evidence, or compatible downstream regime fail closed.

Evidence:

- `backend/Accounts.Api/Services/SizeClassificationService.cs:48`
- `backend/Accounts.Api/Services/FilingRegimeService.cs:56`
- `backend/Accounts.Api/Rules/SizeThresholds.cs:3`

Completion evidence (2026-07-10): 53 focused size/regime/release tests pass across statutory
boundaries, 6/12/18-month and irregular periods, historical/current schedules, the 2023/2024
election, first years, every consecutive-year transition and all modeled exclusion flags. Invalid
elections and incomplete/unauthorised overrides fail without mutation; conservative overrides bind
named authority, retained evidence hash, decision fingerprint and re-review. Prior decision-chain
drift blocks regime selection and final release. Group aggregation, historical eligibility and other
unmodeled professional cases remain explicit qualified-accountant handoffs rather than guessed.

### [x] P0-ACC-002 — Enforce valid period chronology

Store and enforce incorporation-aware period chronology: no overlap, controlled gaps, one first
year, correct maximum length, and deterministic prior-period/comparative selection.

Acceptance:

- API and PostgreSQL constraint tests reject periods before incorporation, overlaps, duplicate
  first years, and concurrent overlapping creates.
- Comparative and cumulative calculations select the intended prior period.

Completion evidence (2026-07-10): period creation is serializable and company-advisory-locked;
migration `20260710180000_EnforcePeriodChronology` independently enforces incorporation, maximum
length, one first year, non-overlap and contiguous chronology for raw writes. Six application tests
and a real PostgreSQL raw/concurrent test pass, including two simultaneous overlapping inserts where
exactly one succeeds. All comparative consumers now select only the exact adjacent prior period.

### [x] P0-ACC-003 — Make posted double-entry data the statement source of truth

Require positive journal amounts and distinct debit/credit accounts; derive impacts server-side.
Ensure posted balance-sheet journals flow into all statements. Correct:

- opening inventory and next-period cost of sales;
- acquisition/disposal and period-apportioned depreciation;
- residual values and disposal treatment;
- interest in cash flow;
- statement/equity/reserve reconciliation;
- final trial-balance-to-output traceability.

Acceptance:

- Multi-period fixtures prove trial balance, P&L, balance sheet, cash flow, equity, opening
  inventory, interest, fixed-asset disposal, and closing cash reconcile to the cent.
- Property tests prove every posted journal balances.
- Every final figure can be traced to transactions, opening balances, year-end inputs, and journals.

Evidence:

- `backend/Accounts.Api/Services/AccountingLedgerService.cs`
- `backend/Accounts.Api/Services/AdjustmentPostingRules.cs`
- `backend/Accounts.Api/Services/AdjustmentService.cs`
- `backend/Accounts.Api/Services/FinancialStatementsService.cs`
- `backend/Accounts.Api/Data/Migrations/20260710200000_EnforceDoubleEntryLedger.cs`
- `backend/Accounts.Tests/DoubleEntryStatementReconciliationTests.cs`

Completion evidence (2026-07-10): a single balanced-ledger projection now feeds the trial balance,
profit and loss account, balance sheet, cash-flow statement, statement of changes in equity and
source-trace output. Imported transactions are expanded into bank/contra postings; exact adjacent
period balances are carried forward and prior income/expense balances are closed to retained
earnings. Manual and generated journals require a positive amount and two distinct accounts, with
profit/asset impacts derived from account types rather than accepted from callers; the database
migration independently enforces those posting invariants. Opening-stock reversals, actual/actual
depreciation, residual-value floors, asset disposals, dividends, tax and interest cash flows are
covered by cent-exact two-period fixtures, and source notes retain opening-balance, bank-opening,
transaction and journal identifiers.

Verification passed: the 33-test directed statement/iXBRL suite, the 14-test widened
adjustment/trial-balance/fixed-asset/pre-filing suite, frontend TypeScript checking, and the three
focused statement/fixed-asset render tests. EF Core reports no pending model changes after migration
`20260710200000_EnforceDoubleEntryLedger`.

### [x] P0-STAT-001 — Separate full statutory accounts from reduced CRO copies

Full statutory/AGM Micro accounts must contain the required profit-and-loss account. Only the
eligible reduced CRO filing copy may omit information permitted by the applicable filing regime.
Keep package purpose and accounting framework explicit throughout generation and tests.

Acceptance:

- PDF text tests assert the exact statement matrix by regime and package purpose, including
  comparatives and statutory headings.
- Micro statutory approval accounts contain P&L; eligible Micro CRO copy remains reduced.
- Small abridged statutory accounts contain full statements; the eligible CRO copy is abridged.
- Independently reviewed expected-output fixtures replace tests that merely assert current output.

Evidence:

- `backend/Accounts.Api/Services/DocumentGeneratorService.cs:12`
- `backend/Accounts.Api/Services/DocumentGeneratorService.cs:606`
- `backend/Accounts.Tests/FilingGoldenCorpusScenarioTests.cs:46`
- Companies Act 2014, section 291:
  `https://www.irishstatutebook.ie/eli/2014/act/38/section/291/enacted/en/html`

Completion evidence (2026-07-10): package purpose and accounting framework are explicit for every
regime. The audit-derived statement matrix covers Micro, Small Abridged, Small, Medium, and Full
statutory/AGM/CRO purposes. Rendered-PDF tests prove Micro statutory and AGM accounts include P&L
and a prior-period comparative while the reduced Micro CRO copy omits P&L; existing rendered tests
prove full Small Abridged accounts versus the reduced section 352 CRO copy. Focused matrix/PDF tests
pass 3/3 and the updated Micro golden-corpus scenario passes.

### [x] P0-STAT-002 — Replace or disable incomplete Revenue iXBRL generation

Do not label the current short XHTML artifact as filing-ready. Implement the correct supported
Revenue taxonomy and complete tagging for each supported scenario, including all applicable:

- entity and period metadata;
- directors' report;
- auditor report;
- profit-and-loss/other comprehensive income;
- balance sheet;
- cash flow;
- changes in equity;
- notes;
- Detailed P&L and mandatory zero-valued items;
- contexts, units, transformations, calculations, signs, rounding, and schema references.

Unsupported standards, taxonomy periods, or tagging requirements must route to manual handoff.

Acceptance:

- CI validates schema, contexts, units, calculations, mandatory tags, taxonomy concept allow-list,
  and complete content; well-formed XML alone is insufficient.
- Removing one required report, tag, zero fact, context, unit, or DPL item fails.
- Every supported canonical scenario passes the exact current official ROS/iXBRL validation
  mechanism against its artifact hash. Retain the complete validator response, validation reference,
  taxonomy-package hash, artifact hash, validator version/date, and explicit disposition of every
  warning or error; a boolean `accepted` is insufficient.
- Micro and abridged CRO presentation decisions do not remove Revenue-required private filing data.

Evidence:

- `backend/Accounts.Api/Services/IxbrlService.cs:138`
- `backend/Accounts.Api/Services/IxbrlService.cs:161`
- `backend/Accounts.Api/Services/IrishStatutoryRuleSources.cs:266`
- Revenue filing contents:
  `https://www.revenue.ie/en/companies-and-charities/corporation-tax-for-companies/submitting-financial-statements/how-to.aspx`
- Revenue technical information:
  `https://www.revenue.ie/en/companies-and-charities/corporation-tax-for-companies/submitting-financial-statements/technical-information.aspx`

Completion evidence (2026-07-10): the incomplete generator is conservatively disabled for filing
use by `RevenueIxbrlGenerationPolicy`; there are now zero platform-supported filing-ready Revenue
iXBRL scenarios. Every generated XHTML is an on-demand review prototype carrying conspicuous
`draft-not-for-filing` and `manual-handoff-only` markers, and it retains private Revenue P&L data for
Micro and abridged regimes instead of applying reduced CRO presentation rules. Prototype checks do
not retain an artifact, set generated/validated booleans, or create an approvable manifest. The
workflow contract and filing workbench explicitly expose manual handoff, while the independent CRO
release path remains available. Revenue approval, submitted, accepted, filed and final-export paths
all reject with the same fail-closed policy even when legacy booleans or complete external validator
evidence are fabricated. Readiness treats a validation boolean as insufficient and requires retained
artifact/response bytes plus matching hashes and validator/taxonomy identity. Focused verification
passes 28/28 backend policy, release, canonical-scenario and endpoint tests plus 12/12 filing-centre
render tests; TypeScript and targeted lint pass. A complete final artifact must therefore be prepared
and officially validated in an approved external production tool, with its human handoff evidence
remaining an explicit release gate rather than a simulated platform capability.

### [x] P0-STAT-003 — Retain and bind real qualified-accountant approval

Store professional body, membership identifier, independently verified status, effective dates,
scope/capacity, reviewer identity, decision, timestamp, and exact approved artifact hashes. Generic
Owner or Reviewer status must not imply professional qualification.

Acceptance:

- Unqualified, expired, self-declared, cross-tenant, or stale-artifact reviewers cannot approve.
- Regeneration or changed legal/tax evidence invalidates approval.
- Positive tests require a verified current professional record and exact manifest.

Evidence:

- `backend/Accounts.Api/Services/RoleAuthorizationService.cs:20`
- `backend/Accounts.Api/Services/FilingWorkflowService.cs:173`
- `backend/Accounts.Api/Services/FilingReadinessProfileService.cs:267`

Completion evidence (2026-07-10): `FilingReleaseGate` retains professional body, membership
identifier, HTTPS verification reference, exact credential-evidence bytes and SHA-256, verification
and expiry times, tenant, workflow scope, qualified-accountant capacity, explicit decision, reviewer
identity and approval time. The approval manifest binds the credential decision, exact artifact
hashes, release candidate, signed report where applicable, and a deterministic legal/tax/accounting
source fingerprint. Current-evidence validation rejects unrecognised/expired/self-declared,
wrong-scope, cross-tenant and stale approvals; source changes or regeneration require a fresh pack
and approval. Readiness no longer treats `ApprovedBy` alone as professional evidence, and canonical
machine scenarios deliberately leave this human gate open. Focused filing release and canonical
scenario verification passes 18/18, including positive verified-current/exact-manifest release and
negative credential, tenant, legal-source, tax-source and byte-drift cases. This completes the
engineering control only; a named real qualified accountant must still supply and approve each
release candidate.

### [x] P0-STAT-004 — Retain the actual signed auditor report

Replace boolean/reference evidence with immutable file metadata, content hash, auditor/signer
details, report date, and qualified-accountant review. Attach or merge the supplied report into the
final audited artifact as appropriate. Never present the unsigned platform template as the final
auditor opinion.

Acceptance:

- Audit-required final output fails without the retained signed artifact.
- Boolean/reference-only evidence fails.
- Final artifacts and manifest include the supplied report and matching hash, with no template
  placeholders represented as signed opinion.

Evidence:

- `backend/Accounts.Api/Entities/AccountingPeriod.cs:36`
- `backend/Accounts.Api/Services/DocumentGeneratorService.cs:462`

Completion evidence (2026-07-10): audit-required readiness now requires an actual retained PDF with
matching SHA-256, auditor firm/signer and professional identifiers, UTC signing metadata and an
accepted qualified-accountant review; boolean/reference-only state fails. Recording or replacing the
report clears stale CRO artifacts and approval. Regenerated audited accounts contain a neutral
attachment notice rather than platform-authored opinion wording, and the final retained CRO PDF
embeds the exact signed report bytes as `signed-auditor-report.pdf` without altering those bytes. The
report hash, attachment hash, final accounts hash and source fingerprint are bound into the approval
manifest and rechecked at every final transition/export. Focused tests parse the final PDF, recover
the embedded file byte-for-byte, reject missing/non-PDF evidence and prove the unsigned template is
not represented as the final opinion; the combined release/canonical suite passes 18/18. Actual
auditor signing and qualified-accountant acceptance remain external human acts and are never
fabricated by the platform.

### [x] P1-STAT-005 — Implement a complete regime/fact-based notes checklist

Generate or explicitly review every required note by stable code. Remove tax double counting,
duplicated notes, unsubstantiated "none" assertions, inconsistent render dates, and statements about
deferred tax or financial instruments that the platform does not support.

Acceptance:

- Deleting one required note blocks the applicable output.
- Exactly one included generated note exists for each stable required note code; duplicate
  accounting-policy, fixed-asset, approval, or other generated notes fail validation.
- Note figures reconcile to the statements.
- Approval dates are persisted and stable across regeneration.
- No `none`, deferred-tax, derivative, commitment, financial-instrument, or similar representation
  may be emitted without retained review evidence; unsupported disclosures require manual handoff.

Evidence:

- `backend/Accounts.Api/Services/StatutoryNoteChecklist.cs`
- `backend/Accounts.Api/Services/NotesDisclosureService.cs`
- `backend/Accounts.Api/Services/DocumentGeneratorService.cs`
- `backend/Accounts.Api/Data/Migrations/20260710082312_EnforceStatutoryNoteChecklist.cs`
- `backend/Accounts.Tests/StatutoryNoteChecklistTests.cs`

Completion evidence (2026-07-10): generated checklist rows now persist stable codes, a
required/not-applicable/explicit-review state, and retained reviewer evidence, with one coded row per
period enforced by PostgreSQL. The live regime/fact matrix derives all monetary disclosures from the
posted statements, uses the profit-and-loss tax charge and balance-sheet creditor total exactly once,
and blocks missing, duplicated, excluded, stale or tampered notes. Unsupported financial-instrument,
capital-commitment and deferred-tax wording is never generated; Medium/Full output requires a retained
manual-review handoff before such wording can render. Approval uses only the persisted board date, and
the PDF composer renders each included stored note once rather than rebuilding and duplicating policy,
fixed-asset or approval notes.

The final combined notes/golden/render regression passed 27/27, including deletion and mutation cases
for all five regimes and parsed-PDF assertions. Frontend TypeScript and the 11-test API contract suite
passed. A fresh PostgreSQL 16 database applied the complete migration chain through real
`MigrateAsync`, and EF Core reports no pending model changes.

### [x] P1-STAT-006 — Produce retained charity artifacts from reconciled data

Generate and retain the applicable SoFA and Trustees' Annual Report artifacts from accounting data.
Remove default-true governance assertions and require evidence-based answers. Enforce SORP tier and
manual handoff for complex charity cases.

Acceptance:

- SoFA closing funds equal balance-sheet net assets.
- Workflow booleans alone cannot satisfy generation or approval.
- Retained artifacts, hashes, charity number, trustee review, governance answers, and qualified
  approval are mandatory.
- Authoritative effective-dated decision fixtures cover every supported SORP/reporting-tier boundary;
  an indeterminate framework routes to manual handoff.
- Trustee/director population is based on service during the reporting period, including relevant
  appointment and resignation dates.

Evidence:

- `backend/Accounts.Api/Services/CharitySorpDecisionService.cs`
- `backend/Accounts.Api/Services/CharityReportingService.cs`
- `backend/Accounts.Api/Services/CharityPdfService.cs`
- `backend/Accounts.Api/Services/FilingWorkflowService.cs`
- `backend/Accounts.Api/Services/FilingReleaseGate.cs`
- `backend/Accounts.Api/Data/Migrations/20260710233000_AddRetainedCharitySorpArtifacts.cs`
- `backend/Accounts.Tests/CharitySorpArtifactTests.cs`
- `backend/Accounts.Tests/FilingReleaseGateTests.cs`
- `frontend/src/app/companies/[companyId]/periods/[periodId]/charity/page.tsx`
- `frontend/src/components/company/CompanyCharityInfoPanel.tsx`
- `frontend/src/components/company/CompanyOfficersPanel.tsx`

Completion evidence (2026-07-10): the official Charities SORP 2026 PDF was retained with SHA-256
`7814211b25ac1305d98f805d3b272564ce1f2d92a4930c5b17d47a674ae70f3a` and visually checked at
the effective-date, tiering and Republic of Ireland Appendix 3 pages. The effective-dated decision
selects SORP 2019 before 1 January 2026 and SORP 2026 on/after that date; unsupported pre-2026,
non-company/indeterminate, and Tier 2/3 activity-basis paths fail closed to manual professional
handoff. Supported Tier 1 CLG generation requires explicit hashed governance evidence, a named and
hashed trustee-population review, exact in-period director service dates, and a zero SoFA/net-assets
reconciliation. QuestPDF clean and conspicuously marked review copies were text-tested and rendered
through Poppler for visual inspection. Final PDFs are released only through `FilingReleaseGate` after
candidate-bound qualified-accountant approval; changing a bound source fingerprint revokes artifacts
and approval. Focused backend behavior, frontend runtime-contract/render/type checks, EF pending-model
check, and a fresh PostgreSQL 16.4 migration chain through `20260710233000` passed. Human source-law
review and qualified-accountant release gates remain deliberately open.

### [ ] P1-STAT-007 — Rebuild director-loan compliance

Use the correct statutory provisions, relevant-assets basis, dated balance movements, interest
rules, exceptions, documentation, and Summary Approval Procedure evidence. Make unresolved breaches
final-output blockers.

Acceptance:

- Accountant-reviewed fixtures cover zero/negative relevant assets, threshold boundaries,
  documented/undocumented loans, intra-group exceptions, interest, and SAP.
- Each unresolved breach blocks final output and appears in the sign-off packet.

Evidence:

- `backend/Accounts.Api/Services/DirectorLoanComplianceService.cs:9`
- `backend/Accounts.Api/Entities/DirectorLoan.cs`
- `backend/Accounts.Api/Data/Migrations/20260710234000_AddDirectorLoanComplianceEvidence.cs`
- `backend/Accounts.Tests/DirectorLoanComplianceEvidenceTests.cs`
- `backend/Accounts.Tests/Fixtures/director-loan-compliance-independent-v1.json`
- `frontend/src/components/DirectorLoansManager.tsx`
- `frontend/src/components/DirectorLoanEvidenceForm.tsx`
- `frontend/src/components/period/YearEndDirectorLoanComplianceSummary.tsx`

Engineering remediation evidence (2026-07-10): sections 236, 238–245, 202–203, 307 and 308 are
now evaluated separately against current revised-Act source links. Section 240 uses the evidenced
last-laid-financial-statements/called-up-capital basis and the statutory strict-less-than boundary;
zero, negative and equality cases fail closed. Dated advances and repayments reconcile opening,
maximum and closing balances and drive time-weighted section 236 interest rather than an average-
balance shortcut. SAP, intra-group, vouched-expense and ordinary-business claims each require their
own dates, conditions and retained references. Named arrangement review is server-stamped, but is
explicitly subordinate to the release-level qualified-accountant gate. Every unresolved evidence,
legal-basis or review issue is copied into final-output blockers and the director-loan sign-off
packet. The final focused backend director-loan gate passed 20/20; frontend runtime contracts passed
4/4, render behavior passed 5/5, TypeScript, targeted ESLint and the API-client verifier passed. EF
reported no pending model changes after migration `20260710234000` was generated. A dedicated fresh
PostgreSQL 16.4 database then applied the complete migration chain, including the immediately
following exact-ARD migration, persisted the dated movement and retained-evidence graph, and passed
the real-provider compliance query 1/1.

This item remains open because the independent fixture is intentionally marked
`pending-qualified-accountant`. Machine tests cover all requested scenarios, but no session may
self-attest the required independent accountant review.

### [ ] P1-TAX-001 — Harden CT1 support and explicitly bound its scope

Fix directors' fees, qualifying-asset treatment, loss persistence/elections, disposal balancing
adjustments, passive income, and unsupported close-company/group/relief cases. The platform must not
emit plausible-looking final tax results outside its proven scope.

Acceptance:

- Independent fixtures reconcile accounting profit to taxable streams and CT.
- Gross wages are not reported as directors' fees.
- Refund, credit, reversal, and manual-journal fixtures prove signs are preserved and `Math.Abs`
  cannot turn a reversal into a disallowable add-back.
- Unsupported tax cases block the final tax charge and Revenue-ready handoff, then fail closed to
  manual review; a UI warning alone is insufficient.
- The UI and exports clearly identify support data versus a complete CT1 return.

Evidence:

- `backend/Accounts.Api/Services/TaxComputationService.cs:24`
- `backend/Accounts.Api/Services/TaxComputationService.cs:154`

Remediation evidence (2026-07-10): corporation-tax output is now deliberately identified as
`corporation-tax-support-data-not-ct1-return`; both API responses and the accountant workbench state
that it is not a complete CT1 and cannot be filed directly. A retained, named scope questionnaire
must answer close-company status and explicitly records group/consortium relief, gains, foreign
income/credits, excepted trades and other relief/special-regime facts. Any supported-scope input or
loss movement that becomes stale blocks the final tax charge and Revenue handoff. Close/service
company surcharges, group cases, gains, special regimes, non-trading losses and any non-standard
loss election fail closed to manual review. Revenue filing-ready iXBRL generation remains disabled
and direct ROS submission remains unsupported.

The calculation now separates trading income at 12.5% from explicitly classified passive income at
25%. Ambiguous income blocks instead of being guessed. Imported refunds/credits and manual-journal
reversals preserve their signed direction; the tax service contains no `Math.Abs` conversion.
Employee gross wages and directors' fees are separate persisted inputs and separate CT support
fields. Capital allowances require a retained per-asset treatment review: only explicitly reviewed
12.5% plant and machinery is automated, short periods are pro-rated, non-qualifying assets are
excluded, special schemes block, and disposals use prior claims and tax written-down value for
bounded balancing allowances/charges. Missing proceeds, missing claim history, excess prior claims
and proceeds above cost all block the automated result. Disposal of a reviewed non-qualifying asset
also requires explicit chargeable-gain/loss review instead of silently bypassing the tax scope.

Trading losses now have a period-owned retained movement ledger with calculation hash, opening,
current-year, used and closing balances. Only same-trade carry-forward against first-available
profits is automated; continuity with the prior retained closing balance is enforced and other
claims/elections remain visible but blocked. Readiness also turns an unpostable accounting ledger
into an evidence blocker instead of returning a server error, and an intervening accounting period
cannot be skipped in the retained loss chain. The year-end UI captures the scope,
loss election, capital-allowance evidence and distinct directors' fees; the statements UI exposes
the tax streams, loss movement, source links, blockers and support-only status. Reloading the scope
review recomputes its current status, while a ledger failure leaves the retained answers visible and
surfaces the calculation blocker.

The byte-pinned independent fixture
`backend/Accounts.Tests/Fixtures/corporation-tax-independent-v1.json` covers signed refunds and
journal reversals, passive income, separate directors' fees, qualifying-asset wear-and-tear and
disposal balancing, and a two-period loss carry-forward. Focused tax tests pass 8/8; the surrounding
legacy computation, readiness and fail-closed iXBRL regression passes 24/24. A dedicated PostgreSQL
16.4 test applied the full migration chain to a fresh schema, persisted scope/loss, asset-treatment
and directors-fee evidence, then recomputed the support result successfully and proved an unpostable
scope/loss write rolls back atomically (1/1). Frontend verification passes TypeScript, the 2/2
corporation-tax runtime-contract tests and 4 files / 6 tests of focused scope, statements,
asset-treatment and payroll render coverage.

Sources:

- Revenue CT basis and rates: `https://www.revenue.ie/en/companies-and-charities/corporation-tax-for-companies/corporation-tax/basis-of-charge.aspx`
- Revenue capital allowances: `https://www.revenue.ie/en/companies-and-charities/corporation-tax-for-companies/corporation-tax/capital-allowances-and-deductions.aspx`
- Revenue trading losses: `https://www.revenue.ie/en/companies-and-charities/corporation-tax-for-companies/corporation-tax/trading-losses.aspx`
- Revenue close companies: `https://www.revenue.ie/en/companies-and-charities/corporation-tax-for-companies/close-companies/index.aspx`
- Revenue close-company surcharge: `https://www.revenue.ie/en/companies-and-charities/corporation-tax-for-companies/close-companies/surcharge.aspx`

This item remains open because the independent fixture is deliberately marked
`pending-qualified-accountant`. Machine remediation is complete for the stated acceptance cases,
but no engineering session may self-attest statutory correctness or the required independent
qualified-accountant acceptance.

### [x] P1-STAT-008 — Correct Directors' Report population and wording

Use directors who served during the accounting period, accurate appointment/resignation dates,
profit/loss wording, paid versus proposed dividends, evidenced audit-information representations,
and reviewed principal activities.

Acceptance:

- Timeline fixtures exclude future directors and include relevant resigned directors.
- Profit/loss and dividend wording follows actual facts.
- Audit-information statements require explicit director evidence.

Completion evidence (2026-07-10): `DirectorsReportService` now refuses an implicit filing regime,
selects only directors and secretaries whose dated service overlaps the reporting period, exposes the
appointment/resignation timeline through the typed API, and blocks final output when the director
roster is absent or officer dates are incomplete. The generated PDF uses that period-service roster
(including relevant resigned directors and their dates) rather than the current-officer list, while
the current signatory remains a separate concern. Principal activities are never inferred from a
trading flag: the year-end workbench retains the exact reviewed narrative, the server rejects token
evidence, draft data is visibly marked `UNREVIEWED`, and final output is blocked until a named UTC
review exists. Profit, loss, paid dividends and declared-but-unpaid dividends are represented
separately with fact-correct wording. The Companies Act 2014 section 330 relevant-audit-information
statement is emitted only for a non-exempt report with retained director enquiry evidence; otherwise
it is withheld and final output remains blocked. The frontend runtime contract rejects mismatched
director names/service dates, out-of-period officers and unsupported statutory assertions. Official
bases are Companies Act 2014 sections 326 and 330. Focused verification passes 11/11 backend
directors-report/CRO/PDF tests (including future/resigned timeline and dated PDF evidence), the wider
27/27 statutory notes/golden-corpus regression, 41/41 frontend contract and route-composition tests,
the 2/2 financial-statements render tests, and TypeScript with no errors.

Sources:

- `https://www.irishstatutebook.ie/eli/2014/act/38/section/326/enacted/en/html`
- `https://www.irishstatutebook.ie/eli/2014/act/38/section/330/enacted/en/html`

### [x] P1-STAT-009 — Store the exact ARD date and correct deadline modelling

Replace `ArdMonth` with an exact date/effective history. Distinguish the B1 made-up-to/delivery
deadline from the maximum financial-statement age rule.

Acceptance:

- Official CRO examples, changed ARDs, leap years, weekends, and Irish public holidays match
  expected results.
- No last-day-of-month assumption remains.

Completion evidence (2026-07-10): `Company.ArdMonth` has been replaced by a nullable exact
`AnnualReturnDate`; the migration deliberately retains old month values only in the hidden,
non-operational `LegacyArdMonthUnverified` column and leaves the exact date unconfirmed rather than
inventing a day. New-company onboarding requires an exact CRO date plus an evidence reference.
Every initial or changed ARD creates immutable, SHA-256-bound `AnnualReturnDateRecord` evidence with
the prior date, legal effective date, source (`CroRecord`, early B1, B73, court order, or explicit
manual override), actor, timestamp, reason and retained evidence reference/hash. Changed ARDs and
manual overrides are validated and audit logged; direct profile edits cannot bypass that command.

`DeadlineService` now retains the exact ARD occurrence, B1 made-up-to date, section 347(4)
financial-statement latest made-up-to date, 56-day delivery date, rule/source identity and a
calculation fingerprint as separate fields. When the nine-month rule binds, the B1 made-up-to date
is brought forward without relabelling it as the ARD. Evidence-backed due-date overrides retain the
unmodified statutory calculation and automatically move to `NeedsReview` rather than controlling
the due date when the source fingerprint changes. Weekend and Irish public-holiday logic covers St
Brigid's Day and colliding Christmas substitute days. The company workbench exposes the current ARD,
source, immutable history and correction workflow; filing cards show the ARD, made-up-to date,
accounts-age limit, delivery date, source link and override-review state. These remain workflow
records only: no direct CRO submission path was added and existing release/accountant gates remain.

Focused backend verification passes 13/13 exact-ARD/deadline tests covering the CRO 30 September / 25
November example, a non-month-end ARD, nine-month rule, changed ARD, leap day, weekends, Irish public
holidays, append-only evidence and reversible override review; the surrounding quarantine/onboarding
regression passes 58/58. A fresh PostgreSQL 16.4 run applied migration
`20260710235000_AddExactArdDeadlineEvidence` and passed the mandatory golden/config/path suite 3/3;
EF reports no pending model changes at handoff. Frontend verification passes TypeScript, API client
and proxy verification, 190/190 unit/contract tests, and focused onboarding/ARD/deadline workbench
render coverage.

Evidence:

- `backend/Accounts.Api/Entities/Company.cs:14`
- `backend/Accounts.Api/Services/DeadlineService.cs:31`
- CRO filing guidance: `https://cro.ie/Annual-Return/Filing-an-Annual-Return/`

### [x] P1-ACC-004 — Make duplicate imports reviewable rather than destructive

Use available reference, statement balance, batch, account, and source metadata to identify
candidate duplicates. Allow an explicit retain/discard decision instead of silently dropping a
same-date/amount/description transaction.

Acceptance:

- Two genuine identical same-day transactions remain reviewable.
- A re-imported statement is identified without silently losing data.
- Duplicate decisions are audited and reversible before finalisation.
- No source row is discarded before a retained, audited reviewer decision.

Completion evidence (2026-07-10):

- Every valid imported row is now retained with immutable batch/file SHA-256, byte size, source
  header, source-row number, source-row SHA-256, and raw source-row JSON evidence. Detection scores
  exact source re-imports plus reference, running-balance, date, amount, and normalised-description
  signals; it never makes a discard decision.
- Pending candidates remain provisionally included in the ledger and block final outputs. Named
  retain/discard/reopen transitions require a substantive reason, increment a decision version,
  run under the period concurrency lock, and write integrity-chained audit events atomically. Only
  an explicit `Discarded` decision sets the accounting exclusion flag, and locked/finalised periods
  reject further changes.
- The accountant import workspace now shows retained batch/hash evidence and a typed duplicate
  review queue with bank/currency, batch/file/row provenance, side-by-side source facts,
  confidence/reasons, retain-incoming, discard-from-ledger, read-only, reopen-decision,
  legacy-locked, confirmation, and server-paginated states. Byte-identical statement re-imports
  can be retained, discarded, or reopened atomically with expected-state tokens and exact retained
  row-manifest/digest audit evidence; individual controls are suppressed while that batch path is
  available. Runtime contracts reject missing source, ledger-state, pagination, or reviewer evidence.
- Import parsing now refuses headerless or unrecognised automatic mappings, uses controlled
  day/month or ISO date patterns without cross-culture fallback, parses decimal-dot/decimal-comma
  formats with validated grouping, rejects an ambiguous single separator followed by three digits,
  and refuses unsupported precision/range instead of silently changing ledger values. Warnings are
  persisted with the immutable batch, included in the import audit, capped safely in the workbench,
  and drive action-needed rather than unconditional-success feedback.
- Migration `20260710235900_RetainDuplicateImportReviewEvidence` follows the tax-support migration,
  adds restrictive source-evidence foreign keys, immutable source/bank-identity triggers, complete
  SHA-256 and hash-only match ownership checks, and reopens any unaudited legacy duplicate flag as
  `Pending/LegacyUnverified`; for an already locked or finalised period it visibly quarantines the
  exclusion without changing signed figures until the period is reopened and a named decision is
  recorded. Public transaction serialization redacts raw bank-export columns, source hashes, and
  internal decision-user evidence; those fields remain available only through restricted evidence
  services.
- Verification: the focused duplicate import/detector/review suite passes 24/24; the dedicated
  PostgreSQL integration gate passes 1/1, covering relational set-based batch decisions and audit,
  stale-token rollback, restrictive deletes, source/bank immutability, hash-only ownership, and a
  legacy null-hash row. Frontend TypeScript, API/proxy verification, and duplicate-review render
  coverage pass (10/10). EF reports no pending model changes; a fresh PostgreSQL 16.4 database
  migrated through the full chain. A pre-migration draft exclusion was verified as provisionally
  included `Pending`, and a finalised-period exclusion as `LegacyLockedUnverified` without changing
  its ledger treatment.

### [x] P1-ACC-005 — Deliver the required internal accountant outputs

Implement retained, drillable lead schedules, categorized-transaction reports, review-exception
reports, adjusted trial balance, working-paper indexes, and corporation-tax bridge outputs. Every
figure must link to transactions, opening balances, year-end facts, journals, and reviewer evidence.

Acceptance:

- Each required internal output has an explicit endpoint/artifact contract and route.
- Totals reconcile to the final statements and tax computation.
- Drill-down tests prove that a final figure resolves to its complete source set.
- Generated working papers carry exact period, reviewer, version, and artifact-hash identity.

Engineering remediation evidence (2026-07-10; engineering acceptance complete, while release use
still requires the live PostgreSQL gate and named accountant acceptance):

- `AccountantWorkingPaperService` now generates and retains a support-only working-paper pack in the
  existing report history. The pack contains independently hashed lead schedules, a categorized-
  transaction register, review-exception register, adjusted trial balance, working-paper index and
  corporation-tax bridge. Explicit index, generation and per-output endpoints sit under
  `/api/companies/{companyId}/periods/{periodId}/working-papers`; every response asserts that it is
  not a filing artifact, supports no direct submission and still requires qualified-accountant
  review.
- Artifact identity binds schema version, database-generated report ID as the concurrency-safe
  artifact version, tenant, company, period and exact dates, authenticated generator/reviewer ID,
  display name and role, generation timestamp, release candidate, source-data SHA-256 and final
  artifact SHA-256. Each output carries an additional section SHA-256. Retained reads reject a
  changed release candidate, legal company identity, bank opening, transaction, opening balance,
  journal, year-end fact, review confirmation, statement/readiness result, tax calculation, source
  hash or artifact content.
- Structured drill-down references identify source type, entity ID, period ID, amount, evidence,
  reviewer/timestamp and accountant route. Account schedules link the complete contributing opening
  balances, bank openings, categorized transactions, journals and mapped year-end facts; prior-period
  roll-forwards point to the exact prior-period working-paper chain. The CT bridge links scope/loss,
  fixed-asset, capital-allowance, payroll and tax evidence and remains explicitly not a CT1 return.
- Generation allocates the persisted report ID before hashing, so concurrent calls cannot share a
  version. On relational providers the report bytes and audit-chain event commit in one transaction;
  failures roll both back. Retained lookup filters the output marker, period and type in SQL and
  returns only the newest matching artifact rather than materializing report history.
- The permission model exposes a dedicated internal-working-paper capability to Owner, Accountant
  and Reviewer roles while denying Client access; only Owner/Accountant may generate. The accountant
  route presents stable artifact identity, risk-ordered next actions, a drillable responsive grid,
  all five output tabs and the working-paper index, with a visible link from Financial Statements.
  No filing or submission action is present.

Focused verification passed 11 backend behavior/endpoint/permission tests plus one environment-
gated PostgreSQL atomicity test that compiles locally; 50 frontend runtime-contract, API-client,
permission, route-state, composition and accessibility tests; 4 focused render/interaction tests;
and TypeScript checking. The PostgreSQL test must execute without skip in the live integration gate,
and a named qualified accountant must still review the retained figures and evidence before they are
used in a real engagement.

### [ ] P1-TAX-002 — Complete the filing-support worksheet and preliminary-tax tracker

Add a clearly scoped field-by-field CT1 support worksheet, numbered panel/field mapping where
officially defined, preliminary-tax due dates and safe-harbour calculations, payment tracking, and
late-filing/surcharge exposure. Unsupported elections or complex cases must remain manual handoff.

Acceptance:

- Official worked examples and independently reviewed fixtures match due dates and calculations.
- The UI and exports never imply that the support worksheet is a directly submittable CT1.

Engineering remediation evidence (2026-07-10; item intentionally remains open):

- `CorporationTaxFilingSupportCalculator` implements the Revenue small-company, large-company and
  first-year preliminary-tax paths, the earlier-of electronic filing/balance date, dated payment
  allocation, section 239 support, official simple-interest conventions, late-filing surcharge
  exposure, and explicit complex-case/manual-handoff blockers. The calculator and immutable
  `corporation-tax-filing-support-independent-v1.json` fixture are covered by byte-pinned tests; the
  fixture is deliberately labelled `pending-qualified-accountant` rather than self-approved.
- The retained `CorporationTaxFilingSupportReview` and `CorporationTaxPaymentRecord` model uses
  scalar API DTOs, tenant query filters, persistence ownership validation, PostgreSQL constraints,
  one active-payment uniqueness, soft-void evidence, actor/timestamp/request audit events and
  migration `20260711000000_AddCorporationTaxFilingSupport`. GET/PUT/POST/DELETE endpoints and JSON/
  CSV worksheet exports expose fixed support-only headers and no CT1 or ROS submission operation.
- `CorporationTaxSupportWorksheetBuilder` produces a company/period/hash-bound support worksheet,
  year-specific 2023–2025 panel orientation, exact published 2025 Extracts from Accounts labels,
  reconciliations and a complete manual-completion list. Other years fail closed as orientation
  only. The UI repeats `NOT A CT1 RETURN`, requires qualified-accountant review, distinguishes due
  and overdue amounts, retains payment evidence and provides no submission control.
- Verification passed 29/29 focused backend calculator/fixture/worksheet/service/endpoint tests,
  15/15 frontend fail-closed contract tests, 2/2 component interaction/render tests, 6/6
  accessibility-semantics tests, TypeScript checking and API-client route verification. The
  PostgreSQL migration/persistence/soft-void/tenant-filter/constraint test compiles and remains a
  mandatory live-database execution gate where `ACCOUNTS_POSTGRES_TEST_CONNECTION` is available.

Residual blockers: a named independent qualified accountant must review and accept the fixed worked
examples and calculation fixture; the live release database must execute the PostgreSQL test; and
the applicable live ROS CT1 must be checked field by field at handoff. A current 2026 CT1 completion
guide was not available in the reviewed Revenue sources, so the platform does not claim a 2026 exact
field mapping or a directly submittable CT1 return.

### [x] P1-FILE-001 — Complete the external filing handoff data model

Represent the B1 annual-return worksheet, shareholder/allotment information needed for the handoff,
CRO presenter and ROS agent/TAIN authority records, CT1 support, external references, correction/
send-back state, and amended/superseding filing chains. Preserve immutable as-filed snapshots and
exact artifact hashes. Direct submission remains unsupported.

Acceptance:

- A complete field-by-field manual handoff can be produced without scraping PDFs or querying the
  database directly.
- Authority/engagement records are required before an external filing state can advance.
- Amendment creates a new linked snapshot; it never rewrites the prior as-filed record.

Completion evidence (2026-07-10): the platform now exposes a typed manual CRO B1 and Revenue CT1-
support handoff workspace rather than requiring PDF scraping or database access. The CRO document
contains explicit company, return, registered-office, officer/presenter, share-class, shareholder,
allotment and attachment fields; the Revenue document carries the bounded CT1 support worksheet,
payment and manual-completion facts. Protected presenter/TAIN and officer identifiers are represented
only by masked values or retained evidence references/hashes, and both document types state that they
are support-only records with no direct CRO/ROS submission capability.

Every handoff requires a current, in-scope, unexpired CRO-presenter or ROS-agent authority record
whose exact retained evidence bytes and SHA-256 are bound into the snapshot. Authority replacement
and revocation are append-only. Generated canonical JSON bytes, company/period/candidate identity,
authority hash, source references and attachments are retained in an immutable snapshot with an
exact artifact SHA-256. Correction and send-back outcomes form a validated append-only chronology.
An amendment creates a newly hashed snapshot linked to the exact predecessor ID and artifact hash;
supersession additionally binds the exact successor ID/hash, so neither path rewrites an as-filed
record. PostgreSQL constraints/triggers enforce tenant/company/period ownership, append-only rows,
exact hash linkage, chronology and amendment scope.

Verification passed 13/13 backend artifact, endpoint and live PostgreSQL tests with zero skips,
including authority replay, complete field preparation, exact artifact retrieval, correction,
send-back, amendment/supersession, cross-tenant rejection and immutability. The frontend strict
runtime contract passed 6/6 and the authority/snapshot/outcome workbench render gate passed 6/6;
the generated OpenAPI contract includes every handoff route. Real-world use still remains behind
the qualified-accountant, authority and external manual-filing gates, and no direct submission
client or endpoint was added.

### [x] P1-DATA-003 — Add idempotency to create, import, and workflow commands

Use tenant-scoped idempotency keys for company/period creation, imports, document generation records,
and filing-state commands where retries could duplicate data or state changes.

Acceptance:

- Retrying the same key and payload returns the original result without a second mutation.
- Reusing a key with a different payload fails safely.
- Concurrent duplicate requests create exactly one logical result and one domain audit event.

Implementation evidence (2026-07-10): the generic tenant ledger now covers company/period creation,
imports, retained document-generation records, and filing-state commands. PostgreSQL advisory locks
serialize retries; reservation, mutation, audit, exact response, and completion share one transaction.
Payload drift fails closed, completed replays can cross stale ETag/new period-lock preflights, and
replay transport requests do not duplicate audit rows. The browser retains one key across automatic
unsafe retries. See `Docs/operations/idempotency-retention.md`. The final local PostgreSQL gate now
passes 5/5: concurrent duplicate serialization, failure rollback and same-key recovery, append-only
ledger/retention enforcement, legacy onboarding-key backfill with exact replay, and atomic retained
accountant-working-paper/audit generation.

## Phase 3 — Frontend workflow correctness

### [x] P0-FE-001 — Implement real transaction and audit pagination

Stop deriving whole-period totals from the first loaded page. Use server-provided aggregate counts
and page/cursor metadata. Make every transaction and audit event reachable.

Acceptance:

- A 125+ transaction fixture exposes every row through pagination or virtualized loading.
- Selection is ID-backed and persists across page changes and page-size changes within the same
  filter query. Changing period or filter clears selection with an announcement. `Select all`
  means the current page; an optional separate `Select all matching` action must show the backend
  matching count and require explicit confirmation against a stable query snapshot.
- Bulk actions affect only explicitly selected IDs.
- UI aggregate and filing-checklist counts exactly match backend counts.
- Older audit events are reachable.

Evidence:

- `frontend/src/app/companies/[companyId]/periods/[periodId]/page.tsx:312`
- `frontend/src/app/companies/[companyId]/periods/[periodId]/page.tsx:677`
- `frontend/src/components/period/PeriodCategoriseWorkspace.tsx:467`
- `frontend/src/components/period/PeriodAuditTrailPanel.tsx:70`

Completion evidence (2026-07-10): transaction and audit APIs now return stable server pagination,
page metadata and aggregate counts. ID-backed selection survives page, page-size and sort changes,
clears with an `aria-live` reason on period/filter changes, and bulk actions receive only explicit
IDs. The 125-transaction and 125-event suites prove every row is reachable and aggregate counts do
not depend on the loaded page.

### [x] P0-FE-002 — Replace failure-as-empty behavior with explicit resource states

Create a shared resource-state model: loading, loaded, empty, partial-error, error, and stale/retrying.
Apply it to dashboard, company, period, year-end, notes, charity, statements, filing, and audit data.
Never clear retained data on a failed refresh. Disable professional confirmations when required
evidence failed to load.

Acceptance:

- Inject 500, timeout, and malformed responses into every read endpoint.
- No failure renders as "none", "not scheduled", or an empty confirmed accounting section.
- Retry succeeds without reloading unrelated state.
- Actions requiring unavailable evidence are disabled with an explanation.

Evidence:

- `frontend/src/app/companies/[companyId]/periods/[periodId]/year-end/page.tsx:192`
- `frontend/src/app/companies/[companyId]/periods/[periodId]/page.tsx:304`
- `frontend/src/app/companies/[companyId]/periods/[periodId]/notes/page.tsx:75`
- `frontend/src/app/companies/[companyId]/periods/[periodId]/charity/page.tsx:91`

Completion evidence (2026-07-10): the shared resource-state model is applied across dashboard,
company, period, year-end, notes, charity, statements, filing and audit reads. Failure tests cover
HTTP 500, timeout and malformed responses; successful data remains visible as stale/partial rather
than becoming a false empty state, retries target only failed resource keys, and professional
confirmations are disabled with an explanation when required evidence is unavailable.

### [x] P0-FE-003 — Correct deadline semantics and dashboard scalability

Replace per-company deadline requests with a batch dashboard contract. Distinguish not configured,
not applicable, unavailable, overdue, due soon, and filed.

Acceptance:

- One request supplies deadline state for 100+ companies.
- Service failure displays "Deadline unavailable" plus retry, never "Not scheduled".
- Partial failures produce a separate unavailable count; the UI must not infer a status for a
  failed company. All deadline buckets, including unavailable, reconcile exactly to company total.

Evidence:

- `frontend/src/app/page.tsx:43`
- `frontend/src/components/dashboard/DashboardCompanyDirectory.tsx:190`

Completion evidence (2026-07-10): `GET /api/dashboard/deadlines` supplies one runtime-validated
batch with bounded database reads and explicit not-configured, not-applicable, unavailable, overdue,
due-soon, scheduled and filed states. A 105-company test proves one HTTP request, and service/partial
failure tests keep unavailable companies separate while every bucket reconciles to the total.

### [x] P0-FE-004 — Apply one canonical permission matrix to all UI actions

Use backend-aligned permission helpers for Owner-only, working-paper write, review, approval, and
read-only access. Remove dead controls and use shared read-only notices.

Acceptance:

- DOM matrix tests cover Owner, Accountant, Reviewer, and Client across every route.
- Forbidden controls are absent, allowed controls remain, and expected workflows do not generate
  routine 403 errors.
- Company delete is shown only to Owner.

Evidence:

- `frontend/src/app/companies/[companyId]/periods/[periodId]/page.tsx:125`
- `frontend/src/app/companies/[companyId]/periods/[periodId]/year-end/page.tsx:610`
- `frontend/src/components/company/CompanyDetailWorkbench.tsx:130`
- `frontend/src/lib/permissions.ts`
- `frontend/src/lib/permission-action-catalog.json`
- `frontend/src/components/AuthProvider.tsx`
- `frontend/src/components/period/FilingOutputsPanel.tsx`
- `frontend/tests/render/permission-deep-link-matrix.test.tsx`
- `frontend/tests/render/permission-action-catalog.test.tsx`
- `frontend/tests/permission-route-wiring.test.mjs`
- `backend/Accounts.Tests/FrontendPermissionMatrixTests.cs`

Completion evidence (2026-07-10): the canonical role matrix now governs an exhaustive authenticated
route policy and audited UI action catalog for Owner, Accountant, Reviewer and Client. Deep links,
navigation, company controls, working papers, professional review, approval and read-only downloads
are covered by DOM and source-wiring matrices; company quarantine remains Owner-only. Read-only
period loads no longer issue a deadline-calculation POST, Reviewer/Client password changes are
backend-authorised, review-artifact GETs remain visible while write-generating outputs stay hidden,
and preview-only controls are non-interactive. Verification passed 53 backend authorization tests,
169 frontend unit tests, 43 focused role/affected render assertions, TypeScript and ESLint.

### [x] P0-FE-005 — Make onboarding transactional and idempotent

Use one backend transaction that creates a company and officers, protected by an idempotency key.
Require an explicit incorporation date; never substitute the current date.

Acceptance:

- Failure on any officer rolls back the whole transaction. A retry with the same key creates or
  returns the same single durable company identity; no partial/draft duplicate remains.
- Double submission creates exactly one company.
- Missing incorporation date blocks with an accessible inline error.

Evidence:

- `backend/Accounts.Api/Services/CompanyOnboardingService.cs`
- `backend/Accounts.Api/Endpoints/CompanyOnboardingEndpoint.cs`
- `backend/Accounts.Api/Entities/CompanyOnboardingRequest.cs`
- `backend/Accounts.Api/Data/Migrations/20260710230000_AddAtomicCompanyOnboarding.cs`
- `backend/Accounts.Tests/CompanyOnboardingTests.cs`
- `backend/Accounts.Tests/CompanyOnboardingPostgresTests.cs`
- `frontend/src/app/companies/new/page.tsx`
- `frontend/tests/render/company-onboarding.test.tsx`

Completion evidence (2026-07-10): onboarding now sends one `POST /api/companies/onboard`
command containing the company, every officer, the explicit first accounting period and the opening
bank setup. The server creates that graph plus the complete default chart of accounts inside one
outer transaction. It reserves a tenant-scoped `Idempotency-Key`, binds it to a canonical request
SHA-256, and durably retains the exact response and SHA-256 only when the whole command completes.
The only allowed evidence transition is `InProgress` to `Completed`; PostgreSQL rejects later
updates or deletes. Replaying the same key and payload returns the retained company, period, bank
and officer identities, while reusing a key with a different payload returns a safe 409.

Fresh-schema PostgreSQL tests passed 2/2 with no skips: a deterministic two-connection insert gate
proved concurrent double submission creates exactly one company aggregate, and an injected officer
insert failure after the company, period, bank and categories had been written proved the outer
transaction rolls every row and the idempotency reservation back. The canonical local onboarding
and chronology slice passed 10 tests with only three environment-gated PostgreSQL cases skipped;
the wider onboarding/quarantine inventory slice passed 55 tests. Frontend tests passed 3/3,
including stable-key network retry and proof that navigation/success wait for the atomic response;
validation passed 10/10, TypeScript and focused ESLint passed, and the API-client route verifier
passed. Incorporation date is now required and rendered as an accessible inline error; the former
current-date substitution is removed.

### [x] P0-FE-006 — Runtime-validate all critical API responses

Add shared runtime schemas for company, period, transaction, year-end, adjustment, statement,
notes, charity/SORP, dashboard/deadline batches, filing readiness/workflow status, audit, auth,
pagination, and aggregate envelopes. Do not rely only on TypeScript generics around `res.json()`.

Acceptance:

- Missing fields, wrong types, unknown enums, malformed money/dates, and inconsistent aggregates
  or cross-field invariants fail closed into explicit error states.
- Backend/frontend contract drift fails CI.

Evidence:

- `frontend/src/lib/api.ts:198`
- `frontend/src/lib/apiContracts.ts`
- `frontend/src/lib/auth.ts`
- `frontend/tests/api-runtime-contracts.test.mjs`

Completion evidence (2026-07-10): the browser boundary now parses critical company, officer,
period, transaction/pagination/import, year-end, adjustment, statement/source-ledger, statutory
notes, deadline, charity/SORP, filing workflow/readiness and authentication payloads through shared
Zod contracts before application state can consume them. Exact domain enums, real ISO dates,
finite accounting figures, page metadata, aggregate counts, statement subtotals, fund movements,
filing support flags, no-direct-submission controls and professional-review gates fail closed with
an explicit `ApiContractError` that does not echo response data. Existing dashboard/deadline,
production-readiness and audit parsers remain independently invariant-checked, with audit timestamps
now requiring offset-bearing ISO values. Ten behavioral contract tests prove missing fields, wrong
types, unknown enum members, malformed dates/money, aggregate drift and cross-field contradictions
are rejected, and exercise every critical API client family against malformed successful JSON. The
CSV import UI now consumes the canonical `importedRows` response instead of silently displaying
zero from obsolete keys; the P&L contract and workbench retain and display `otherIncome`, and the
categorisation endpoint hydrates its returned category so its response is self-consistent. Focused
contract tests, TypeScript, deadline/audit tests and financial-statement renders pass; these tests
are part of the mandatory frontend CI gate, so contract or client-wiring drift fails the candidate.

### [x] P1-FE-007 — Guard unsaved changes across Next.js navigation

Cover links, breadcrumbs, tabs, browser back, refresh, close, and onboarding—not only
`beforeunload`.

Acceptance:

- Cancel retains drafts and location.
- Confirm navigates.
- Saved/clean forms do not prompt.
- Tests cover all editable routes.

Evidence:

- `frontend/src/components/UnsavedChangesProvider.tsx`
- `frontend/src/lib/useUnsavedChanges.ts`
- `frontend/tests/render/unsaved-changes.test.tsx`
- `frontend/tests/unsaved-changes-route-coverage.test.mjs`

Completed 10 July 2026. A single root provider now aggregates every mounted editable
surface, intercepts same-tab links (including breadcrumbs), guards App Router
`push`/`replace`/`back` calls and sign-out before their side effects run, traps native
Back with a `popstate` history sentinel, and retains `beforeunload` coverage for refresh
and close. It uses an accessible, focus-managed `alertdialog` and native listeners/history
calls without replacing browser or router globals. Onboarding, company, period,
classification, notes, charity, year-end, loan, director-loan, share-capital and
quarantine/recovery drafts are registered; incomplete money-entry text is covered too.
Local tabs keep draft state above their conditional panels, and opened year-end
accordions remain mounted when collapsed so local editor state cannot be silently lost.
Fourteen behavioral navigation tests cover cancel/confirm, Link replay, guarded
`push`/`replace`/`back`, browser Back cancel/confirm, unload, multiple blockers,
post-save bypass, destructive navigation actions, nested modal focus/Escape handling and
incomplete money drafts. Route/source coverage tests enumerate all material editors and
enforce the no-monkey-patch architecture. Focused render suites, TypeScript and ESLint
pass.

## Phase 4 — UI/UX, accessibility, and frontend maintainability

### [ ] P1-UX-001 — Put daily accountant work first on the dashboard

Place urgent queue, deadlines, reviewer ownership, and next action above release-engineering
evidence. Reduce production readiness to a compact summary and link to its dedicated route.

Acceptance:

- At 1440×1000, the urgent queue and first actionable row are visible without scrolling.
- On mobile, urgent work precedes release evidence.
- Accountant usability review confirms clear first action.

Evidence:

- `frontend/src/components/dashboard/DashboardWorkbench.tsx:43`
- Current dashboard capture height: approximately 5,635 px desktop / 11,620 px mobile.

Engineering remediation (2026-07-10): the dashboard now renders the accountant work queue,
deadline state, practice summary and company directory before any platform-release material in DOM
and mobile order. The former multi-thousand-pixel production-readiness evidence board has been
removed from the command centre and replaced by a compact release-status summary with a single link
to `/production-readiness`; the full evidence remains on that dedicated route. Dashboard copy now
leads with deadlines, reviewer ownership and next action, while retaining an explicit warning that
real filing use remains professionally gated. Three focused render tests pass, including a DOM-order
assertion that the first work queue precedes release status. Four dashboard contract tests also
prove that company/deadline loading no longer waits for the much larger release report. TypeScript
passes after the concurrent charity-workbench contract handoff. On 11 July 2026 the authenticated
visual-smoke run proved in both themes that the accountant queue precedes release evidence at every
canonical viewport and that the first dashboard action ends inside the initial 1440×1000 viewport.
During that run the reminder-delivery queue was moved after daily accountant work so the numeric
assertion would reflect the real first action rather than a marker alone. This item remains open only
for the named accountant usability review of first-action clarity; targeted dashboard ESLint passes.

### [x] P1-UX-002 — Restructure production readiness using progressive disclosure

Add grouped navigation, a sticky/table-of-contents surface, search, anchor links, collapsed sections,
and a persistent blocker summary. Keep all evidence printable and reachable without rendering every
detail expanded by default.

Acceptance:

- Every section is keyboard-reachable by anchor.
- Browser back restores section location.
- Priority blockers open initially; supporting ledgers are collapsed.
- On a 390×844 viewport, blocker summary and section navigation are reachable within the first two
  viewports; every supporting ledger is collapsed initially; initial full-page height is no more
  than eight viewport heights, excluding content the user explicitly expands.

Engineering evidence implemented 10 July 2026:

- `frontend/src/components/readiness/ProductionReadinessWorkbench.tsx` now presents a compact,
  desktop-sticky blocker/navigation surface before the evidence ledgers. It groups eight stable
  section anchors under Decision, Assurance, Statutory and Platform headings, provides section
  search, restores hash-selected sections on `hashchange`/`popstate`, and keeps only the priority
  release decision open initially. The priority surface reports the authentic release-blocker
  register, retained human/external evidence count and professional sign-off requirements; it does
  not auto-accept or suppress any release gate.
- Seven supporting evidence groups are collapsed initially. The compact mobile priority surface
  bounds its repeated blocker rows while the full blocker register, human evidence, source-law,
  accountant, audit/operations, statutory/golden-corpus and unsupported-path evidence remains
  reachable in the closed disclosures. Print CSS expands every disclosure and overrides active
  search filtering so all evidence remains printable.
- `frontend/tests/render/production-readiness-workbench.test.tsx` proves initial disclosure state,
  persistent blocker wording, all eight keyboard anchors, search-driven expansion, and native hash
  history/Back restoration while retaining the pre-existing full evidence checklist assertions.
  `frontend/tests/production-readiness-progressive-disclosure.test.mjs` proves stable anchors,
  bounded mobile summary contracts, printable closed evidence, retained ledgers and unchanged
  human/external blocking semantics. Focused render tests, 63 readiness contract/source tests,
  TypeScript and targeted ESLint pass.

Completion evidence (2026-07-11): the fresh authenticated 390×844 light/dark captures now enforce
the numeric acceptance contract in the browser. The priority blocker/navigation surface finishes
within two viewports, all seven supporting ledgers remain closed initially, and the complete initial
page height is no more than eight viewport heights. Those assertions are retained per screenshot in
`responsiveAcceptanceResult` and are re-verified by both visual evidence-pack verifiers. Browser
console, overflow, overlap, contrast and axe checks also pass for both mobile readiness captures.

### [ ] P1-UX-003 — Repair mobile tables, grids, and tab affordances

Use labelled mobile rows where suitable; otherwise provide sticky key columns and a visible scroll
affordance. Replace the categorisation fixed `div` grid with semantic responsive markup.

Acceptance:

- At 390 px there is no page-level horizontal overflow. Transaction date, description, amount,
  category, confidence, Debit/Credit, officer actions, period actions, and statement values are
  keyboard- and accessibility-tree-reachable.
- Any retained internal horizontal scroller has a persistent visible affordance, labelled controls
  or instructions, and does not hide the focused cell/action.
- Tabs expose overflow and support keyboard navigation.

Engineering evidence implemented 10 July 2026:

- The shared `DataGrid` now has an explicit mobile presentation contract. Suitable tables render as
  labelled card rows; retained dense grids expose a persistent visible scroll instruction, a named
  keyboard-focusable scroll region, stable scrollbar space, focus scroll margins and a sticky key
  column. `HorizontalScrollRegion` applies the same contract to statement, source-trail, equity,
  charity-fund and audit tables, while `WorkflowRail` now advertises and supports keyboard scrolling.
- `PeriodCategoriseWorkspace` no longer renders transactions as a fixed twelve-column `div` grid.
  It uses a semantic table that becomes labelled mobile cards and retains transaction selection,
  date, wrapping description, amount, explicit Debit/Credit-to-bank wording, category control and
  confidence in the accessibility tree. Transaction rules are a semantic responsive list. Officer
  and period tables use labelled mobile-card rows, keeping their edit/remove and open-workbench
  actions reachable without horizontal scrolling.
- Period-workspace, financial-statement and charity tab strips now show touch and keyboard overflow
  instructions. The HeroUI tab sets retain their tested Left/Right Arrow behavior; charity tabs now
  implement `tablist`/`tab`/`tabpanel`, roving focus, ArrowLeft/ArrowRight and Home/End behavior.
  Twelve-column loan, share-capital and year-end editor grids stack each control to one full-width
  row below 768 px.
- Render tests prove labelled transaction/officer/period cells, explicit entry sides, focusable wide
  table regions, persistent instructions and real ArrowRight tab selection. Source/CSS tests reject
  an unlabelled `overflow-x-auto`, a return of the categorisation `div` grid, missing tab overflow
  semantics or fixed mobile form grids. The focused gate passes 37 render tests and seven source/CSS
  tests, plus targeted ESLint. A complete TypeScript run passed immediately after this UX change;
  the current shared branch later acquired separate Company/deadline contract fixture drift
  (`ardMonth` removal and required `calculatedDueDate`) that must be reconciled before claiming the
  branch-wide TypeScript gate again.

The fresh authenticated 11 July 2026 matrix proves zero page-level horizontal overflow across every
one of the 192 canonical light/dark mobile/tablet/desktop captures. It also exposed and closed real
responsive defects in fixed-asset statement columns and categorisation selectors before the final
run passed. This item remains open only for the named human keyboard/visual review that confirms
focused actions remain visible during internal scrolling; that human assertion is not inferred from
the automated viewport evidence.

### [ ] P1-A11Y-001 — Complete semantic and keyboard accessibility

Remove nested Link/Button controls, associate every label, name icon-only buttons and textareas,
implement correct tablist/tab/tabpanel semantics, and make dialogs trap and restore focus.

Acceptance:

- Automated and manual review reports zero unwaived WCAG 2.2 A/AA findings at any severity on every
  production route and material state. Any temporary exception is documented with owner, rationale,
  compensating control, and due date.
- Keyboard-only journeys complete login, onboarding, categorisation, review, and filing recording.
- Manual screen-reader checks cover those critical journeys and retained results are reviewed.
- Dialog Tab/Shift+Tab is trapped, Escape closes, and focus returns to the trigger.

Evidence:

- `frontend/src/components/workbench.tsx` now provides one semantic `ActionLink` rather than nesting
  `Button` inside `Link`; all audited navigation call sites use that primitive.
- `frontend/src/components/AppNavbar.tsx` closes mobile navigation on Escape and restores focus to
  the menu trigger.
- `frontend/src/components/ConfirmModal.tsx` provides labelled modal semantics, stack-aware Escape,
  Tab/Shift+Tab trapping (including an empty/loading dialog), initial focus and trigger focus return.
- `frontend/src/app/companies/new/page.tsx` uses the HeroUI v3 compound checkbox control. The former
  shorthand rendered visible option text without an interactive checkbox; keyboard Space activation
  is now covered.
- Critical classification, onboarding, charity, notes, annual-return, import, categorisation,
  adjustment, year-end, loan, share-capital and filing controls now have associated labels and stable
  accessible names. Explicit names retain visible wording to satisfy WCAG 2.5.3 Label in Name.
- Charity tabs retain `tablist`/`tab`/`tabpanel`, `aria-selected`, roving focus and Arrow Left/Right,
  Home and End keyboard behavior.
- `frontend/tests/accessibility-semantics.test.mjs` performs source-wide regression checks for nested
  link/button controls, unassociated labels, unnamed native/HeroUI inputs, unstable spinner/icon
  button names, Label in Name, incomplete HeroUI checkbox composition, modal focus management and
  custom tab keyboard semantics. Director-loan files assigned to the separate protected workstream
  are excluded from the label/name checks, but not from the nested-interactive scan.
- Render coverage exercises keyboard-only login, onboarding progression and checkbox activation,
  transaction selection, adjustment approval, filing recording, mobile-navigation Escape/focus
  return, and dialog Tab/Shift+Tab/Escape/focus return.

Automated verification on 10-11 July 2026:

- `npx.cmd tsc --noEmit --incremental false` — passed.
- `npm.cmd run lint` — zero errors and zero warnings.
- `npm.cmd run test:unit` — 209/209 passed, including all six accessibility semantic suites.
- Focused accessibility render coverage — 80/80 passed across 13 files.
- `npm.cmd run test:render -- --reporter=dot` — 169/172 passed. The three failures are contract-fixture
  drift in `api-period-etag.test.tsx`, `inline-edit.test.tsx` and `role-gating.test.tsx`; they concern
  required period/director-loan fields and wording in separately owned protected work, not an A11Y
  assertion or an in-scope product regression.

The repository now pins `@axe-core/playwright` and runs axe against every canonical browser capture
using WCAG 2.0/2.1/2.2 A/AA tags. The authenticated 11 July matrix retained 192/192 passed axe
results with zero violations. It also retained 31 incomplete-rule results for explicit human review
rather than treating them as passes. That run exposed and closed keyboard-focusability defects in
filing-review audit JSON regions and WCAG target-size defects in tax source links. The visual,
accountant-workbench, CI-machine-pack and release-pack verifiers reject missing, stale or failed axe
evidence.

This item remains open because no named reviewer has completed the required keyboard-only and
screen-reader journeys across login, onboarding, categorisation, review and filing recording, and
the axe-incomplete rules still require authentic assessment. Automated evidence does not claim to
replace assistive-technology review or human focus-order/indicator acceptance.

### [x] P1-UX-004 — Remove inert sorting

Make columns unsortable by default unless a valid primitive sort accessor/value is supplied. Remove
sort controls from action and unsupported JSX columns.

Acceptance:

- Every visible sort control changes order both directions and updates `aria-sort`.
- Action columns have no sort control.
- Regression tests cover dashboard, officers, periods, transactions, and readiness tables.

Evidence:

- `frontend/src/components/workbench.tsx:988`
- `frontend/src/components/workbench.tsx:1188`

Completion evidence (2026-07-10):

- `DataGrid` now treats every column as unsortable unless the caller explicitly opts in and every
  supplied non-empty sort value is a primitive string or number. Unsupported JSX and action
  columns fail closed and render no sort control.
- Dashboard, officer, period, transaction and readiness tables now declare their supported sort
  columns and primitive sort values explicitly; action columns remain unsortable.
- Render tests exercise both sort directions and `aria-sort`, as well as the absence of controls on
  action, default-static and unsupported JSX columns. The focused six-file table regression gate
  passed 31/31 tests, the additional dashboard/transaction interaction gate passed 4/4 tests,
  TypeScript passed with `--noEmit --incremental false`, and targeted ESLint passed.

### [ ] P1-VIS-001 — Expand visual QA to every material route and state

Cover login, password change, onboarding, classification, categorisation, year-end, adjustments,
notes, charity, all statement tabs, filing, loading, empty, maximum-data, error, partial-error,
permission-denied, read-only, and stale/conflict states in light and dark themes at canonical mobile
390×844, tablet 768×1024, and desktop 1440×1000 viewports.

Ensure route discovery is deterministic and captures distinct intended states; do not allow a
period-workspace screenshot to duplicate the filing-tab state because the first discovered period
link contained a deep-link query.

Acceptance:

- A canonical state inventory drives the manifest.
- Expected capture count is derived from every required state × two themes × three viewports and
  fails if any combination is absent or duplicated.
- Each state records expected text, URL/tab state, console results, overflow, overlap, contrast,
  dimensions, hash, and human review.
- Duplicate or semantically identical route captures fail verification.

Evidence:

- `frontend/scripts/visual-smoke-plan.mjs:97`
- `frontend/scripts/visual-smoke.mjs:219`

Engineering remediation evidence (2026-07-10; real-browser/human gate remains open):

- `frontend/scripts/visual-smoke-plan.mjs` now defines one deterministic
  `canonical-material-states-v1` inventory with 32 distinct entries. It covers login, password
  change, onboarding, classification, categorisation, year-end, adjustments, notes, charity,
  filing and all eight statement tabs, plus explicit loading, empty, maximum-data, error,
  partial-error, permission-denied, read-only, stale and conflict presentations. The expected
  count is derived from that inventory rather than a literal: 32 states × two themes × the exact
  390×844, 768×1024 and 1440×1000 viewports = 192 captures. The established seven-route
  qualified-accountant workbench matrix remains a separately reported 42-capture subset.
- Every planned artifact now carries its state ID, material-route/UI-state code, expected route
  and state text, authentication mode, canonical path/query, canonical selected-tab evidence,
  three layout checks, contrast result and required human-review status. Runtime captures add the
  observed URL/tab, full PNG dimensions and pixel evidence, file SHA-256, and a SHA-256 of the
  normalized rendered main content/headings/controls/selected tabs.
- `frontend/scripts/visual-smoke.mjs` clears any discovered query string before resolving a
  canonical state. A period link discovered as `...?tab=filing` therefore yields the plain import
  workspace for `period-workspace`, while `filing-review` is navigated directly to the explicit
  canonical `?tab=filing` URL; it is no longer produced by clicking from an accidentally deep-linked
  period page. The runner captures the unauthenticated login before sign-in and then captures the
  authenticated inventory from one deterministic company/period route map per theme/viewport.
- `frontend/scripts/visual-smoke-artifacts.mjs` rejects inventory/version/count/viewport drift,
  missing or duplicate state-theme-viewport combinations, canonical URL/query/tab drift, missing
  expected text, missing console/overflow/overlap/contrast results, invalid dimensions or hashes,
  non-pending human-review status, and two intended states in the same theme/viewport with either
  the same rendered semantic-content hash or the same retained PNG hash.
- `frontend/src/components/workbench/WorkbenchPreview.tsx` and the preview route provide isolated,
  deterministic render surfaces for the nine exceptional states without recording accounting or
  filing decisions. Focused verification passes: six inventory/route-resolution tests, fifteen
  manifest/artifact positive and negative tests, seven accountant-subset evidence tests, ten
  workbench-preview render tests, TypeScript with `--noEmit --incremental false`, and targeted
  ESLint.

The backend readiness payload, runtime frontend parser and fixtures, reviewer-workspace generator,
visual QA template, production-readiness verifier, release-evidence verifier, reviewer-workspace
verifier, release-artifact-pack verifier and CI-machine-pack verifier now agree on the canonical
32-state/192-capture contract. They retain the seven-route/42-capture accountant-workbench subset,
require all six light/dark mobile/tablet/desktop combinations, and reject legacy counts, missing
state rows, viewport drift, URL/tab drift, semantic duplication and incomplete human-review cells.

The fresh authenticated 11 July 2026 run against the isolated production-like Docker stack captured
and verified all 192 required state/theme/viewport combinations. `visual-smoke-evidence-report.json`
reports 32 routes, 192 screenshots, 192 passed layout/contrast/axe/responsive results, zero axe
violations, exact viewport dimensions, distinct semantic hashes and retained PNG hashes. The
seven-route accountant workbench subset separately passed all 42 captures. The run found and closed
real issues in dashboard hierarchy, statement columns, categorisation controls, filing audit-region
keyboard focus, tax-link target size and year-end component-boundary evidence before final success.

This item remains open only for the named visual reviewer to accept the retained screenshots and
the explicit axe-incomplete/manual review results. Machine success is not recorded as human visual
acceptance.

### [ ] P1-VIS-002 — Raise contrast and motion checks to WCAG AA

Check 4.5:1 normal text, 3:1 large text and UI components, including links, buttons, focus rings,
placeholders, disabled states, and resolvable gradients. Respect reduced-motion preferences.

Acceptance:

- Automated and human checks cover text and interactive controls.
- Focus is visibly discernible.
- `prefers-reduced-motion: reduce` disables shimmer, spin, slide, fade, and smooth scrolling.

Engineering remediation evidence (2026-07-10; contrast/human gate remains open):

- `frontend/src/app/globals.css` now applies a global `prefers-reduced-motion: reduce` policy that
  disables the app's skeleton pulse/shimmer, utility spin, fade, slide and backdrop animations,
  removes hover lift, collapses transition duration, and restores non-smooth scrolling while
  keeping static loading placeholders visible.
- `frontend/tests/reduced-motion-css.test.mjs` extracts the complete media-query block and fails if
  any named motion family, smooth scrolling, transitions, or hover transform is no longer covered;
  both focused tests pass.
- The visual-smoke contrast collector now applies 4.5:1 to normal text and 3:1 to large text and UI
  component boundaries. It no longer excludes link/button text; it samples form values,
  placeholders, disabled controls, interactive boundaries and resolvable CSS-gradient stops, and
  reports each sample family separately. Visual-smoke, accountant-workbench, CI-machine-pack and
  release-pack verifiers reject stale contrast evidence that omits the thresholds or interactive/UI
  samples and their actual per-family minimum ratios. Twenty-two focused visual-evidence tests,
  six contrast/motion policy tests and three real-Chromium pass/fail threshold tests pass; the latter
  prove compliant mixed text/control/placeholder/disabled/gradient content, rejection below 4.5:1
  for normal text, and rejection below 3:1 for an interactive boundary. The Chromium behavior gate
  runs in CI immediately after the pinned browser install. Targeted ESLint, CI action policy and both
  PowerShell parsers pass.

The fresh 11 July 2026 real-browser run exercised the expanded collector over all 192 canonical
captures in both themes and all three viewports. Every capture passed normal/large text and UI
component contrast thresholds, with sample counts and minimum ratios retained for pack verification;
the run also drove UI-boundary polish where a year-end desktop/tablet state previously had no
machine-verifiable component boundary. This item remains open for automated focus-indicator state
sampling and named human contrast/focus review. The rendered contrast evidence does not substitute
for those focus and human assertions.

### [x] P1-UX-005 — Protect destructive actions

Add an accessible confirmation or undo pattern for notes, year-end rows, opening balances, officers,
rules, and similar destructive actions.

Acceptance:

- A single accidental click cannot irreversibly delete material data.
- Confirmation names the record and consequence.
- Cancel preserves the record; failure restores it; success is announced.

Completion evidence (2026-07-10): `useDestructiveActionConfirmation` now provides one shared
`alertdialog` contract with the exact record in its title, a domain-specific consequence, destructive
loading state, explicit `Keep record` cancellation, trigger-focus restoration through
`ConfirmModal`, and a persistent polite live region for cancel, success and failure. Persisted delete
controls for notes, officers, opening balances, transaction rules, ordinary/director loans, share
capital, charity funds and every debtor, creditor, asset, inventory, dividend, post-balance-sheet,
related-party and contingent-liability row invoke it; onboarding-officer and director-loan movement
draft removals are protected too. The separate corporation-tax payment correction retains its
equivalent explicit `ConfirmModal` evidence context. Delete handlers mutate local state only after a
successful API response and now rethrow failures to the guard, which closes cleanly, retains the row
and announces that the record is unchanged. Three source-wide coverage tests enumerate all 18
audited surfaces and reject a new Delete/Remove control without a shared guard or explicit
confirmation. Thirteen focused render tests prove pre-confirmation non-deletion, named consequence,
cancel retention/focus, pending state, success removal/announcement, failure retention/announcement,
and the guarded year-end/officer callbacks. TypeScript and targeted ESLint pass.

### [x] P2-FE-008 — Improve authentication revalidation UX

Reserve the blocking app spinner for initial authentication. Revalidate sessions in the background
without blanking the app on every pathname change. Distinguish infrastructure failure from 401.

Acceptance:

- Client navigation does not blank the workbench or make redundant `/auth/me` requests.
- Auth service 500 shows service unavailable, not login.
- Genuine 401 still redirects safely.

Completion evidence (2026-07-10): `AuthProvider` now reserves its full-page loading state for the
single initial session check. Pathname changes no longer issue `/api/auth/me` or blank retained
work, while focus/visibility checks run in the background with an in-flight dedupe guard. A 5xx
retains an established session and workbench with a retryable service notice; a failed initial check
renders an explicit authentication-service-unavailable state rather than redirecting to login. Only
a genuine 401 clears the user and enters the existing validated return-to flow. Four behavioral
revalidation cases, four permission deep-link cases and two initial/public route-state cases passed;
TypeScript and targeted ESLint passed.

### [ ] P2-FE-009 — Add frontend monitoring with PII controls

Capture controlled client exceptions and rejected requests with release, environment, route, and
correlation context, while excluding form values, financial payloads, secrets, and client PII.

Acceptance:

- A controlled client error appears in the real provider and matches structured-log correlation.
- Tests prove sensitive values are absent.

Engineering remediation evidence (2026-07-10; real-provider confirmation remains open):

- The browser reports render, unhandled, terminal server/rate-limit, network, timeout, runtime-
  contract and authentication-service failures using seven fixed event codes. It sends only the
  current allowlisted route shape and an optional validated correlation ID; exception messages,
  stacks, request/response bodies, form values and financial/client data are never arguments.
  Event/route deduplication prevents recursive amplification and an unbounded correlation map.
- The authenticated, CSRF-protected `/api/system/monitoring/client-event` endpoint repeats the event
  and route allowlists, normalizes encoded/free-form identifiers, and forwards a generic
  `ClientMonitoringException`. Sentry receives only safe tags; release and environment come from
  server-controlled candidate/environment configuration, while request/user data, breadcrumbs,
  automatic failed-request capture and attached stacks remain disabled.
- Production smoke now sends synthetic email/secret markers through the client-event input beside
  the existing Owner server event. `monitoring-error-routing-report.json` retains both provider and
  correlation IDs plus the normalized client route. The structured-log verifier requires both
  exact correlation lines and fails on any retained synthetic marker. Machine/release packs and the
  prepared reviewer workspace reject missing, mismatched or non-redacted client evidence; the human
  template requires a real provider permalink for each event.
- Verification passed 13/13 backend endpoint/privacy/role/configuration tests, eight browser-payload
  and API-failure tests, seven ErrorBoundary/global/auth render tests, TypeScript, targeted ESLint,
  PowerShell parsing, positive structured-log correlation and a negative marker-leak fixture.

This item remains open only because repository and synthetic smoke evidence cannot prove that both
event IDs appeared in the configured real provider for the exact release/environment. A named
operator must retain both provider references, confirm matching correlations and no PII, and accept
the monitoring-provider template; real filing use remains blocked until then.

### [x] P2-FE-010 — Split oversized frontend modules after behavior stabilizes

Refactor route orchestration, resource loading, permissions, forms, tables, and error states out of
the largest files without changing behavior.

Targets include:

- `frontend/src/lib/api.ts`
- `frontend/src/components/readiness/ProductionReadinessWorkbench.tsx`
- `frontend/src/components/workbench.tsx`
- route-heavy period/year-end files.

Acceptance:

- Existing and new behavioral tests stay green.
- Route files become orchestration-focused.
- Duplicated catch-to-empty and permission logic is eliminated.

Completion evidence (10 July 2026): the stable API façade now delegates schema/runtime contracts,
production-readiness parsing, external filing handoff, identity, operations, permissions and
resource-state behavior to focused modules while preserving existing imports. The readiness surface
delegates stable section inventory, hash navigation, progressive disclosure, evidence primitives,
scorecard formatting and decision summaries; the shared workbench delegates its sortable/filterable
responsive `DataGrid` implementation. The period and year-end App Router pages are one-line adapters
over `PeriodWorkspaceRoute` and `YearEndQuestionnaireRoute`; query/form defaults and route state live
in focused helpers, while import, categorisation, adjustments, statements, filing and year-end
panels remain independently rendered/tested workspaces. Canonical permission capabilities and
truthful loading/empty/partial/stale/error resource states replace duplicated catch-to-empty or
route-local role conventions. Module-boundary tests fail if the large route adapters, readiness API
contract boundary or helper delegation regress. The clean final frontend gate passed 277 unit tests,
225 render tests across 70 files, readiness/proxy/auth/API-client/generated-contract verification
(177 paths / 182 schemas), ESLint, non-incremental TypeScript and the Next.js production build.

### [ ] P1-FE-011 — Finish interaction-state and external-action wording

Preserve intentional tab/deep-link state, restore scroll and focus after mutations, announce
asynchronous errors and success, prevent stale-response overwrites, and remove wording that could
make a recorded external status look like direct submission.

Acceptance:

- Refresh/back preserves the active supported tab and filters.
- Focus returns to the changed row/control after save or cancellation.
- Screen readers receive success/error announcements.
- Slow earlier responses cannot overwrite newer edits.
- Every external action is labelled as generation, handoff, validation recording, or status
  recording—not submission by the platform.
- Automated route/text assertions fail if a platform action implies direct CRO/ROS submission.

Engineering remediation evidence (2026-07-10; browser acceptance remains open): CRO and
Charities Regulator controls say `Record external submission`, status/toast copy says the external
outcome was recorded, and permission/help text no longer asks a platform user to "submit". The CRO
action explicitly says the real filing occurred outside this system. A source-wide regression test
rejects submit/file-to-CRO/ROS/Revenue wording, `Mark as Submitted`, and approve-or-submit copy
unless the same line explicitly states the no-direct/external-recording boundary.

The period workbench now round-trips its supported tab, transaction filters/search/paging/sort and
adjustment filters through same-route URLs while preserving unrelated query state; browser Back
and refresh feed those values back into controlled UI state. Charity reporting now does the same for
its three tabs. Shared latest-request tickets prevent older period, filing, transaction, adjustment,
audit, charity-shell or charity-evidence responses from mutating state after a newer request starts.
The standalone financial-statements route now validates and round-trips all eight statement tabs in
`statementTab`, preserves unrelated query state and guards both shell and statement resource loads
with the same latest-request contract.
Period and charity mutations publish explicit polite success/warning or assertive error live-region
messages. They also capture the initiating control and viewport, use `preventScroll`, and restore
focus after completion, with stable workflow/table/card fallbacks when the original control is
replaced. Existing destructive-action coverage continues to prove cancellation returns focus and
retains the row.

Focused evidence passed: ten URL/route/wording/request-order tests, 29 render tests covering
focus, scroll, announcements, destructive cancellation and the changed workbench components,
targeted ESLint, and the full TypeScript check. This item remains open until an authenticated browser
run proves refresh/Back and focus behavior on the supported period, charity and statements routes,
and the remaining mutation surfaces receive route-by-route live-announcement/focus acceptance rather
than relying only on the global toast semantics.

## Phase 5 — Operations, supply chain, identity, and governance

### [x] P0-OPS-001 — Build once and promote exact tested image digests

CI must build one backend image and one frontend image, scan them, create SBOM/provenance, sign or
attest them, push them to the approved registry, and run production smoke by pulling those exact
digests. The migration and API services must use the same application digest.

Acceptance:

- Release evidence contains exact digests, SBOMs, provenance, scan results, and registry references.
- Rebuilt tags, mutable tags, digest mismatches, or unscanned images fail.
- The final deployment instructions consume digests, not locally rebuilt tags.

Evidence:

- `.github/workflows/ci.yml`
- `scripts/write-container-supply-chain-report.ps1`
- `scripts/verify-container-supply-chain-report.ps1`
- `scripts/verify-ci-actions.mjs`
- `scripts/verify-production-compose-images.ps1`
- `scripts/verify-ci-machine-evidence-pack.ps1`
- `scripts/verify-release-artifact-pack.ps1`
- `Docs/operations/production-runbook.md`
- `deploy/production-images.env.example`

Completion evidence (2026-07-10): CI now invokes the pinned Buildx action exactly once for the
backend and once for the frontend. A trusted push to `main` logs in to GHCR, pushes commit-tagged
build outputs, resolves their registry digests, scans those exact digest references with Trivy
failing on HIGH/CRITICAL findings, emits SPDX JSON SBOMs, creates GitHub build-provenance
attestations, pulls both exact digests, and runs production smoke with the migration and API
services bound to the same backend digest. Pull requests and forks take a registry-credential-free local
verification path; their machine report is explicitly blocked/non-release-eligible and the release
evidence job does not run. `write-container-supply-chain-report.ps1` and
`verify-container-supply-chain-report.ps1` bind retained scan, SBOM, provenance, digest, commit and
run evidence; both CI/release artifact-pack verifiers independently require that evidence and reject
rebuild, mutable-reference, digest, scan, SBOM, provenance or candidate-identity drift. The
production compose verifier and runbook require digest-pinned GHCR references, and
`deploy/production-images.env.example` provides non-deployable digest placeholders rather than
tags. All workflow actions are reviewed allowlisted full-length SHAs, checkout credentials are not
persisted, the action policy verifier passes, workflow YAML parses, the compose safety verifier
passes, promoted and unpromoted synthetic evidence paths behave as specified, and four focused
backend workflow/config regression tests pass.

### [x] P1-OPS-002 — Implement production user lifecycle and privileged MFA

Add Owner-managed invite/create, activate/deactivate, unlock, reset, role changes, company
assignment, session revocation, and offboarding. Require MFA/WebAuthn or enterprise SSO for
privileged roles.

Acceptance:

- All four roles can be provisioned without direct SQL.
- Deactivation and permission removal revoke existing sessions immediately.
- Owner and approval-capable users require a second factor.
- End-to-end tests exercise invite, acceptance, reset, unlock, role/company reassignment,
  offboarding, unauthorized privilege escalation, MFA enrollment/recovery, session revocation, and
  complete audit behavior.

Completion evidence (2026-07-10): Owner-only administration now provisions every role by invitation
or temporary-password creation, activates/deactivates, unlocks, resets, changes roles, changes company
assignments, explicitly revokes sessions and permanently offboards without direct SQL. Every security-
relevant change increments the authoritative session version and writes append-only lifecycle plus
durable audit evidence. Offboarding clears assignments, revokes outstanding action tokens and sessions,
blocks login/reactivation and retains no password, action-token or MFA secret material in evidence.

Owners, Accountants and Reviewers must complete TOTP enrollment/challenge before a principal is
issued. TOTP replay is concurrency-protected; recovery codes are independently keyed, single-use and
cannot satisfy the recent-TOTP gate. Sensitive finalisation, deletion, approval, external-evidence,
privacy and user-administration operations reject stale or recovery authentication with HTTP 428.
Password invitation/reset/change paths use fail-closed HIBP range checks in production, while MFA
secrets use versioned authenticated encryption and key rotation. The responsive Owner workbench
exposes the complete lifecycle with accessible confirmations and no routine privilege-escalation path.

Verification passed 19/19 focused identity lifecycle/security tests, including a behavioral unlock,
explicit revoke and final offboarding sequence, plus the live PostgreSQL security/immutability test
with zero skips. Focused login, action-token, user-administration, permission and navigation render/
contract coverage passed, as did TypeScript, lint and the generated API contract.

### [x] P1-OPS-003 — Protect repository governance

Configure protected `main`, required PR review and CI, admin enforcement, force-push/deletion
blocking, required signed commits where supported, secret scanning, push protection, Dependabot
alerts/updates, and CodeQL. Pin third-party Actions to immutable commit SHAs.

Acceptance:

- GitHub API reports all required controls enabled.
- Required checks and review cannot be bypassed by normal pushes.
- Workflow policy rejects floating action tags.
- GitHub API and commit verification prove the configured signed-commit requirement and the release
  candidate's verified signature state.

Engineering remediation evidence (2026-07-10): live GitHub settings now have secret scanning, push
protection, Dependabot security updates and automated fixes enabled. Extended CodeQL default setup
is configured weekly for Actions, C#, JavaScript and TypeScript. All workflow actions remain pinned
to reviewed immutable SHAs; CODEOWNERS and multi-ecosystem Dependabot policy are prepared locally.
`verify-github-governance.ps1` records the exact API state and candidate signature result. Protected
`main` now requires the canonical six GitHub Actions checks with strict synchronization, one approving
review, code-owner review, stale-review dismissal, signed commits, linear history, conversation
resolution and blocked force-push/deletion. Secret scanning, push protection, Dependabot security
updates/automated fixes and extended weekly CodeQL are enabled. Administrator enforcement is enabled
after the final reconciliation commit, and the verifier must pass against that exact protected head.

### [x] P1-OPS-004 — Make dependency and build inputs reproducible

Align Docker with the declared Node 24 runtime, pin base images by digest, add NuGet lock files and
locked restore, scan containers, generate SBOMs, and run scheduled audits.

Acceptance:

- CI rejects action/base digest drift, lockfile drift, high/critical OS vulnerabilities, Node engine
  mismatch, and unlocked NuGet resolution.
- A scheduled dependency/container audit runs without a source commit and retains its result; a
  deliberately introduced vulnerable test fixture fails the appropriate policy check.

Engineering remediation evidence (2026-07-10): `.nvmrc` and `global.json` now pin exact Node and
.NET SDK versions with SDK roll-forward disabled; Docker uses the declared Node 24 major and all
Node/.NET base stages are bound to reviewed manifest-list SHA-256 digests. Both backend projects
commit complete NuGet lock files, ordinary CI/container restores use locked mode, and a build-input
policy rejects runtime, engine, lockfile, base-digest or workflow drift. Dependabot covers npm,
NuGet, Docker and Actions. `scheduled-security-audit.yml` runs weekly or on demand without a source
push, installs locked inputs, captures npm/NuGet evidence, builds and scans both images, generates
SPDX SBOMs, binds report hashes, and retains the pack for 90 days. The hardened verifier also binds
the candidate and workflow commit identities, npm exit evidence, NuGet report, both Trivy reports and
both SPDX SBOMs. Eleven focused policy tests pass, including deliberately vulnerable npm, NuGet and
Trivy fixtures plus malformed identity/SBOM and missing container-hardening controls. Authentic manual
run `29149858949` passed for exact candidate `2d99f02c2109339ea6315bdf3525fd58530aeb97` and retained
`scheduled-security-audit-2d99f02c2109339ea6315bdf3525fd58530aeb97-29149858949` for 90 days.

### [ ] P1-OPS-005 — Deepen and automate backup/restore assurance

Automate encrypted off-host backups, retention, alerting, and named drills. Restore verification must
cover financial, filing, user, audit, and checkpoint data, schema identity, audit-chain integrity,
and representative output regeneration—not only four table counts.

Acceptance:

- Evidence measures RPO/RTO and records production-like row/figure checks.
- A failed backup or restore triggers an alert.
- Named operator evidence is retained for the exact release/environment.
- Tests prove scheduled execution, encryption, off-host retention, retention expiry, and restoration
  from the retained off-host copy rather than the source host's temporary file.

Evidence:

- `scripts/verify-postgres-backup.ps1:117`

Engineering remediation evidence (2026-07-10; item remains open for real operations evidence):

- `backup-postgres.ps1` now requires a public recovery certificate, produces only an OpenSSL
  CMS/AES-256-CBC `.dump.cms` envelope plus SHA-256 sidecar and identity manifest, and removes its
  plaintext staging dump. `restore-postgres.ps1` verifies the encrypted manifest/certificate,
  decrypts only under the operating-system temporary directory, restores with
  `--single-transaction --exit-on-error`, and erases the temporary plaintext in `finally`.
- CI generates an ephemeral backup encryption/recovery identity separately from database TLS,
  rejects retained plaintext `.dump` files, restores from the encrypted artifact, and uploads only
  the encrypted envelope/checksum/manifest/report—not the recovery private key. Production safety
  evidence records the encrypted-artifact/plaintext-retention policy.
- The restore verifier now measures RPO/RTO; compares migration identity, 15 domain table counts,
  representative transaction/adjustment/opening-balance and retained filing-artifact totals, and
  full-row fingerprints for company, period, transaction, adjustment, CRO/Revenue/charity package,
  audit-log and checkpoint data; malformed audit hashes/checkpoints fail the drill. Machine and
  release pack verifiers require encryption, encrypted-copy restore, matched schema/figure/row
  checks, audit integrity, and met RPO/RTO targets.
- Configured RPO/RTO values are now verifier inputs rather than decorative report fields. The drill
  writes a failed evidence report and exits non-zero whenever either measured target is missed, so a
  stale backup or slow restore cannot remain a green CI artifact merely because the comparison was
  serialized as `false`.
- PowerShell parsers and policy wiring pass. A disposable production-Compose PostgreSQL run created
  a certificate-encrypted backup, retained no plaintext dump, decrypted through the recovery-only
  key path, restored into a separate database and reproduced an exact `123.45` proof value.

This item remains open for an authentic scheduled production/environment run from an approved
off-host store, provider retention-expiry and failed-job alert evidence, representative output
regeneration, and named-operator drill acceptance. Repository and synthetic CI evidence cannot
truthfully stand in for those external operational controls.

### [ ] P1-OPS-006 — Complete real monitoring and incident response

Add provider delivery, log aggregation/retention, redaction, alert routing, escalation ownership, and
an incident runbook.

Acceptance:

- A controlled event appears in the configured real provider, matches the structured-log
  correlation ID, triggers the expected notification, contains no client PII, and is acknowledged by
  a named operator.
- Evidence records monitoring/log retention, measured alert latency, escalation behavior, redaction
  tests, and a completed incident-runbook exercise.

Engineering remediation evidence (2026-07-10): production safety now requires bounded structured-log
and provider-event retention, a named on-call owner, an alert-route identifier, acknowledgement and
later escalation targets, and the controlled incident runbook. Unexpected-error logs no longer
serialize free-form exceptions or raw entity identifiers; the Sentry reporter sends a generic
exception plus normalized route shape, safe correlation ID, exception type and stack fingerprint,
with request/user enrichment, automatic failed-request capture, attached stacks and breadcrumbs
disabled. Behavioral privacy tests cover malicious email/secret-bearing messages and context. The
same privacy boundary now covers frontend render/unhandled/API/auth failures through the fixed-code
client-event relay. Production smoke retains a separate client provider event/correlation, proves
its encoded synthetic email/secret route became an allowlisted route shape, and the structured-log
verifier rejects either a missing client correlation line or any retained marker. Machine/release
packs and the operator template require both real provider event references. The new incident
runbook covers severity, containment, evidence preservation, privacy decisions,
audit/figure recovery checks and closure, while the exercise verifier measures alert,
acknowledgement and escalation latency and rejects redaction, recovery or candidate-identity drift.
Its checked-in synthetic fixture passes only as `engineering-passed-release-blocked` and cannot
masquerade as provider evidence. This item remains open for a real provider delivery/notification,
named operator acknowledgement and retained production-like incident exercise.

### [x] P1-OPS-007 — Add migration drift and previous-release upgrade gates

Run pending-model checks, migrate a fresh database and a restored previous-release database, verify
preserved rows/figures, and document rollback or expand/contract strategy for material migrations.

Acceptance:

- CI runs `dotnet ef migrations has-pending-model-changes` and fails on model drift.
- Fresh and previous-release upgrades preserve representative users, financial rows, calculated
  figures, filing snapshots, and audit-chain/checkpoint integrity.
- A forced migration failure proves rollback or safe expand/contract recovery without partial state.

Implemented evidence (2026-07-10):

- `.config/dotnet-tools.json` pins `dotnet-ef` 10.0.9 to the EF 10.0.9 design/runtime packages;
  `global.json` independently pins SDK 10.0.103 and `config/migration-gate.json` records the exact
  toolchain, PostgreSQL 16.4 target, transactional policy and supported upgrade floor.
- The supported floor is `20260621123340_AddCroSignatories`, the newest migration committed to the
  integration branch before the current production-readiness hardening series. Older schemas require
  a separately planned conversion release rather than being silently treated as supported.
- `MigrationUpgradePostgresTests` behaviorally migrates a fresh PostgreSQL schema through all current
  migrations, upgrades a seeded supported-floor schema, and retains equal positive row counts and
  canonical SHA-256 fingerprints for tenant/user, company/period, financial rows and figures, CRO and
  Revenue filing snapshots, and the audit chain/checkpoint. The upgraded audit chain is recomputed and
  verified cryptographically.
- The same gate injects a `P0001` failure after partial DDL and data mutation inside a PostgreSQL
  transaction, then proves the marker table is absent and data plus `__EFMigrationsHistory` are
  unchanged. It also fails if any EF SQL migration operation suppresses its transaction.
- CI restores the pinned tool, runs `dotnet ef migrations has-pending-model-changes`, executes the
  PostgreSQL gate, verifies `migration-upgrade-report.json`, and uploads both that report and
  `migration-upgrade-verification-report.json` as `postgres-migration-upgrade-gate`.
- CI machine and final release artifact-pack verifiers require both reports to match the candidate and
  require `restore-drill-report.json` as the encrypted recovery companion. The production runbook
  documents pre-migration backup, transactional rollback, and the approval/rehearsal requirements for
  any future non-transactional expand/contract exception.

Local verification: drift check passed with no pending model changes; PostgreSQL 16.4 fresh and
25-to-40 migration upgrade/rollback test passed 1/1; the current corporation-tax PostgreSQL migration
test passed 1/1; positive migration-evidence verification passed. Release acceptance still depends on
the existing named human/external gates; this engineering control does not weaken or replace them.

### [ ] P1-OPS-008 — Harden containers and service networking

Add appropriate read-only filesystems, dropped capabilities, `no-new-privileges`, PID/resource
limits, frontend/API/database network segmentation, and production database TLS or a documented
approved and precisely tested compensating encrypted transport. Retain encryption-at-rest and key-
rotation evidence for the database, backups, artifact store, and secret store.

Acceptance:

- Compose-policy tests reject missing hardening, unintended reachability, unbounded resources, and
  insecure database transport. Any topology-specific compensating transport must have a narrow
  tested policy rather than a general insecure opt-out.
- Evidence proves encryption at rest, key ownership, rotation procedure, and successful rotation/
  recovery for databases, backups, artifacts, and secrets.

Engineering remediation evidence (2026-07-10): all four production services now use read-only root
filesystems, bounded writable tmpfs mounts, `no-new-privileges`, dropped ambient capabilities,
positive PID/memory/CPU limits and non-root application images. Exact internal `frontend_api` and
`api_db` networks prevent frontend-to-database and database-to-egress reachability; only the API is
attached to the deliberately named egress bridge, the API/database publish no host ports, and the
frontend remains loopback-only behind the ingress. The normalized-compose verifier emits these
controls in `production-safety-report.json`; policy fixtures reject a missing read-only filesystem,
capability drop, PID bound, or network boundary. The real PostgreSQL 16.4 image started and became
ready under the declared read-only/capability/PID/memory/CPU controls. Database transport now fails
closed on certificate verification: PostgreSQL starts with a mounted server certificate/key and
deployment CA, TLS 1.2 minimum, and a `verify-full` health check; API and migration startup reject
anything other than `SSL Mode=VerifyFull` with an explicit root-certificate path and reject the
legacy insecure override. CI creates a short-lived CA/server certificate with `DNS:db`, runs
`scripts/verify-postgres-tls.ps1` against the live production Compose candidate, and retains the
release-bound `postgres-tls-runtime/postgres-tls-report.json`; both machine- and release-evidence
pack verifiers require the report. A disposable run of the exact Compose database service passed
its TLS health check, negotiated TLS 1.3 with `TLS_AES_256_GCM_SHA384` at 256 bits, and proved that
`verify-full` rejects a deliberately wrong hostname. Static Compose/evidence policy, PowerShell
parsers, and CI action hygiene also pass. This item remains open only for deployment-specific
encryption-at-rest, key-ownership, rotation, and recovery evidence across the database volume,
backups, artifact store, and secret store; those external storage controls cannot be truthfully
manufactured in this repository.

### [x] P1-OPS-009 — Restrict internal readiness/security evidence

Limit `/api/system/production-readiness` and comparable internal evidence to Owner and explicitly
authorized release-review roles.

Acceptance:

- Client and ordinary Accountant requests receive 403.
- Authorized release reviewers retain access.

Completion evidence (2026-07-10): `RoleAuthorizationService` now treats
`/api/system/production-readiness` as restricted internal release evidence before the general GET
allowance. Only Owner and the explicitly assigned Reviewer role can proceed; ordinary Accountant and
Client sessions receive a JSON 403 before the endpoint executes. The canonical frontend permission
matrix uses the matching `canReviewReleaseEvidence` capability, blocks unauthorized deep links, and
does not fetch, label, or render internal release evidence on an ordinary dashboard. Focused backend
middleware/permission-contract tests passed 13/13, frontend permission tests passed 4/4, and focused
dashboard/deep-link render tests passed 7/7.

### [x] P1-AUD-001 — Complete domain-level audit coverage

Ensure company, officer, period, bank, category, rule, deletion, professional approval, artifact,
and workflow changes record the domain action, tenant/company/period, actor, correlation ID, and
meaningful old/new values in the correct transaction/security chain.

Acceptance:

- An endpoint/domain operation matrix proves every material write records atomic old/new evidence
  in the correct tenant/company chain.
- Failed domain writes do not retain success events; unauthorized/cross-tenant attempts never touch
  the guessed victim chain.
- Audit-chain and checkpoint verification passes before and after every operation family.

Engineering remediation evidence (2026-07-10):

- `DomainAuditCoverage` is the canonical material-write matrix for company creation/update,
  quarantine/recovery, officer create/update/delete, period creation/status, bank
  create/update/delete, category create/seed, rule create/delete, professional adjustment approval,
  retained filing artifacts, and CRO/Revenue/charity workflow transitions. Each entry declares the
  domain event, entity, company/period scope and required old/new evidence shape.
- The mapped endpoints and services call the domain audit boundary with authenticated actor,
  authoritative tenant, correlation ID and meaningful snapshots. Bank evidence deliberately records
  that an IBAN exists without retaining the account value in audit JSON. Failed and rejected domain
  transitions do not retain a success event.
- `DomainOperationAuditCoverageTests` exercises the public HTTP master-data operations, exact domain
  event matrix, rejected-write absence, quarantine/recovery, professional approval, artifact and
  workflow events, old/new evidence, actor/correlation attribution, cross-tenant rejection, hash
  chain verification and signed checkpoint verification. The focused gate passes 3/3.
- The behavioral gate exposed and closed two further tenant-boundary gaps: `AuditLog` and
  `AuditIntegrityCheckpoint` now have request-tenant query filters, and checkpoint creation resolves
  and persists the authoritative company tenant rather than allowing a null or caller-mismatched
  scope.

### [x] P2-SEC-004 — Add database-enforced tenant isolation

Evaluate and implement PostgreSQL row-level security or an equivalent database-enforced boundary
for the application role.

Acceptance:

- Deliberately defective application queries still cannot read or write another tenant.

Engineering remediation evidence (2026-07-10):

- Migration `20260711060000_AddDatabaseTenantIsolation` installs a frozen, reviewable PostgreSQL
  policy inventory with `ENABLE ROW LEVEL SECURITY` and `FORCE ROW LEVEL SECURITY` on every
  tenant-owned table. Request tenant context is signed with an independent secret and bound to the
  backend PID, so directly setting or forging the tenant GUC does not establish a valid scope.
- The runtime API uses the separate `accounts_api` login, which is provisioned as
  `NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE`, cannot inherit or assume the migration/admin
  role, cannot alter protected tables or read the signing-key store, and receives only the minimum
  table/sequence/function grants. Anonymous login and action-token discovery use narrowly scoped
  `SECURITY DEFINER` functions with revoked `PUBLIC` execution rather than weakening table policy.
- `DatabaseTenantIsolationVerifier` fails normal production startup unless the exact forced-policy
  and protected-function inventory, role separation, grants and signing probe match the contract.
  `compose.production.yml` mounts distinct migration and application connection secrets; the
  production safety verifier passed and records the credential boundary and role-provision order.
- `DatabaseTenantIsolationPostgresTests` passed 1/1 with zero skips against real PostgreSQL. It proves
  that raw SQL and `IgnoreQueryFilters()` under the application login still see only the current
  tenant, cross-tenant update affects zero rows, cross-tenant insert is rejected, and attempts to
  forge context, read the signing key, disable RLS, or assume the admin role all fail.

### [x] P2-AUTH-001 — Add privileged reauthentication and recovery hardening

Add idle/absolute session expiry tests, recent-auth requirements for finalisation/deletion/role
changes/evidence acceptance, breached-password checks, and secure recovery controls.

Acceptance:

- Time-controlled tests prove idle and absolute expiry and immediate session revocation.
- Finalisation, deletion, role changes, and evidence acceptance reject stale authentication and
  require recent MFA-backed reauthentication.
- Recovery cannot bypass tenant, role, audit, MFA, or lockout controls.

Engineering remediation evidence (2026-07-10):

- Signed server-side sessions enforce a sliding idle deadline without extending the absolute
  lifetime; permission, role, password, assignment, deactivation, offboarding and explicit-revoke
  operations increment the authoritative session version so existing cookies stop working
  immediately. Time-controlled lifecycle tests cover idle, absolute and immediate revocation paths.
- Owners, Accountants and Reviewers must complete TOTP enrollment/challenge before a principal is
  issued. The recent-auth middleware returns HTTP 428 before finalisation, deletion, role and access
  changes, privacy operations, approvals, external validation/handoff evidence, recovery-code
  regeneration and deadline operations unless a TOTP (not a recovery code) was verified within ten
  minutes. Behavioral middleware tests cover stale TOTP, recovery authentication and the accepted
  recent-TOTP boundary across the sensitive route matrix.
- Password creation/change/invitation/reset/bootstrap paths use the official HIBP range protocol,
  sending only the first five SHA-1 characters with padded responses, persisting/logging neither the
  password nor hash, and failing closed in production when breach status cannot be established.
  Focused tests cover a match, a clean response, protocol privacy and provider unavailability.
- Recovery/action tokens are opaque, server-hashed, short-lived, single-use and tenant resolved;
  reset preserves tenant, role, lockout and enrolled MFA. MFA secrets use versioned authenticated
  encryption with active/prior key support and lazy rewrap, recovery codes use an independent HMAC,
  and the last accepted TOTP counter is concurrency-protected to reject replay.
- Migration `20260711051000_HardenIdentitySecurity` adds database guards for tenant/actor ownership,
  immutable lifecycle evidence and one-way token, challenge, recovery-code and MFA-counter
  transitions. `IdentitySecurityPostgresTests` passed 1/1 with zero skips against real PostgreSQL;
  the focused identity/hardening gate passed 22/22. The production smoke now completes first-time or
  enrolled privileged MFA through the frontend proxy without retaining enrollment/recovery secrets.

### [ ] P2-EVID-001 — Make release evidence durable and authentic

Retain release evidence outside ephemeral Actions retention and use authenticated approvals or
digital signatures that permit independent identity/professional-capacity verification.

Acceptance:

- Evidence remains retrievable after the CI artifact-retention window.
- Altered artifacts, manifests, timestamps, identities, or signatures fail verification.
- Reviewer identity, qualification/capacity, and signature validity can be independently verified.

Engineering preparation evidence (2026-07-10; control remains open):

- `new-release-evidence-signature.ps1` creates no-overwrite, candidate-bound detached OpenSSL
  signature sidecars for all six human templates. Seven signer slots are mandatory because the
  source-law template requires separate source-law reviewer and qualified-accountant signatures;
  keys and passphrases must remain outside the evidence tree.
- `verify-durable-release-evidence.ps1` fails closed on changed template/statement bytes, wrong
  commit/run identity, invalid signatures, untrusted certificate chains, unpinned trust-policy
  hashes, ambient CA stores, weak keys, stale/expired chains, certificate extension/security-level
  policy, unexpected nested sidecars, or certificate fingerprint/subject, signer name,
  professional capacity and HTTPS credential-reference mismatches. It records algorithm/key-size
  evidence without treating cryptographic validity as professional acceptance.
- `verify-durable-release-publication-inventory.ps1` binds an exact no-extra/no-missing manifest,
  rejects traversal, symlinks, archives, case/device-name collisions, its reserved generated-report
  namespace, encoded key material and active/external browser content, and requires five distinct
  canonical raw HTML/XHTML iXBRL artifacts whose hashes occur in the signed external-validation
  template. Raw iXBRL remains untrusted and requires offline sandboxed inspection.
- The adversarial signature and inventory suites pass synthetic tamper, backdating, current-chain,
  weak-key, ambient-CA, independence, private-material, nested-sidecar, path, staging-collision and
  hostile-content cases. The workflow/YAML/action-pinning policies also pass.
- The public repository exposes only a reusable `workflow_call`; it has no public publication
  dispatch. A canonical manual caller in the private evidence repository must pin the reusable
  workflow to the candidate SHA. Before any private checkout, GitHub OIDC binds the exact manual
  caller/called commits, protected environment and run. Read-only repository-scoped App tokens then
  verify exact branch review/check/signature/force-push/deletion controls, private secret scanning,
  independent environment review/no bypass, immutable releases and same-branch CI. The workflow
  rechecks both heads, CI, governance, environment, immutability and tag absence immediately before
  a no-overwrite private release, then verifies immutable-release and provenance attestations.
  See `Docs/operations/durable-release-evidence.md`.
- This item deliberately remains unchecked until real named reviewers sign with independently
  verified credentials; the private repository, supported protected environment, read-only App,
  trust anchors and caller are provisioned; and the exact archive is actually published, remains
  retrievable beyond Actions retention, and passes independent release-asset/provenance/signature
  verification.

### [x] P2-PRIV-001 — Define privacy, retention, and incident handling

Enforce retention and approved deletion/anonymisation rules for login-attempt and audit PII while
preserving statutory audit requirements.

Acceptance:

- Automated retention tests expire data according to the approved schedule.
- Subject access/export and approved erasure/anonymisation workflows locate all applicable data and
  preserve legally required financial/audit evidence with a recorded statutory-retention override.
- A privacy-incident exercise proves notification, containment, evidence preservation, and review.

Engineering remediation evidence (2026-07-10):

- `PrivacyGovernanceService` minimises login-security telemetry to a purpose-derived keyed HMAC
  subject fingerprint and coarse outcome metadata, expires it after the approved 30-day schedule,
  and never retains an email address, password, IP address, user agent, request body, or raw subject
  identifier in that store. Authentication audit actors now use stable internal subject IDs rather
  than email addresses.
- The authenticated subject-access export locates identity, sessions, MFA, privacy requests,
  tenant/company/period audit records, integrity checkpoints and direct professional evidence
  references. Approved erasure is an atomic, separately approved operation that revokes sessions,
  MFA, terminal tokens and access; pseudonymises the identity; preserves hash-chained accounting,
  filing and audit evidence; and records both the statutory-retention override and retained-evidence
  inventory. Tenant crossing, self-approval and removal of the last active Owner fail closed.
- PostgreSQL migration controls bind privacy records to their authoritative tenant/user ownership,
  reject cross-tenant principals, make evidence ownership anchors immutable, and prevent premature
  deletion of subject-request and six-year incident evidence. A real PostgreSQL integration test
  passed 1/1 with zero skips, including defective cross-scope writes, immutable evidence, retention
  guards and the scheduled cleanup worker; focused service tests passed 5/5.
- `Docs/operations/privacy-retention-and-incidents.md` defines the legal-basis review boundary,
  approved schedule, subject-rights workflow and incident playbook. The retained synthetic incident
  fixture and `scripts/verify-privacy-incident-exercise.mjs` prove detection, containment,
  notification assessment, evidence preservation, remediation and independent review; the policy
  contract tests passed 2/2.

### [ ] P2-OPS-010 — Add capacity, failover, and recovery tests

Test rate limiting, audit buffering, DB pooling, document generation, large practices, high-volume
transactions, host/database failure, and measured RPO/RTO recovery.

Acceptance:

- Published load profiles meet defined latency, throughput, error-rate, and resource thresholds.
- Rate limits do not corrupt audit or financial writes under concurrency.
- Host/database failure drills restore within target RTO and lose no more than target RPO.
- Document generation and large-practice dashboards remain bounded and observable under load.

Engineering remediation evidence (2026-07-10; production-like failure drill remains open):

- `scripts/run-capacity-profile.mjs` is now a fail-closed, PII-free production-stack load gate. CI
  drives 120 alternating liveness/readiness requests through the same HTTPS ingress at concurrency
  12 and requires zero errors, p95 at or below 1,000 ms and at least 10 requests/second. The retained
  report contains aggregate/per-endpoint timing and fixed failure codes only, never authentication,
  request/response bodies or tenant/client identifiers. Deterministic tests cover passing arithmetic
  and simultaneous count, error, latency, throughput and route-coverage failures.
- The first local run exposed deterministic timeouts because every concurrent readiness call repeated
  migration discovery and bootstrap-owner database queries. `SystemReadinessProbeService` now uses a
  single-flight probe with five-second success and one-second failure caching, retains fail-closed
  database/migration/owner states, and resolves the configured Owner through the RLS-safe bootstrap
  tenant boundary. Twenty-four concurrent unit callers execute one probe. The exact 120/12 profile
  then passed locally through the API at p95 30.204 ms / 1,166.773 requests per second and through the
  frontend ingress at p95 374.006 ms / 53.941 requests per second, both with zero errors.
- `Docs/operations/capacity-failover-and-recovery.md` publishes the bounded profile, platform alert
  thresholds, existing large-practice/pagination/concurrency/document/restore evidence, and the exact
  external exercise still required. CI uploads `bounded-capacity-profile` for each candidate.
- `scripts/test-production-failover.ps1` now interrupts the ephemeral production API and PostgreSQL
  services separately, requires `/health/ready` to fail closed and then recover within explicit
  candidate targets, restores stopped services in `finally`, retains no response/authentication or
  tenant data, and emits a commit/run-bound `production-failover-report.json`. CI retains it as
  `production-failover-drill`. This is engineering recovery evidence only, not the required named
  production/off-host RPO/RTO exercise.
- `verify-ci-machine-evidence-pack.ps1` and `verify-release-artifact-pack.ps1` now require, strictly
  validate and hash-inventory both reports for the exact candidate/run. They enforce the canonical
  HTTPS origin/profile/thresholds, exact 60+60 endpoint coverage, ephemeral `accounts-production`
  API/database scope, five failover phases, privacy controls and explicit non-production boundary;
  stale, missing or tampered capacity/failover evidence cannot enter the final pack.

This item remains open until an approved production-like environment runs authenticated large-
practice, high-volume financial-write and all-document profiles, kills an application host and
interrupts the database, restores from the approved off-host copy, measures RPO/RTO, and retains
named operator acceptance. The bounded CI result is deliberately not presented as that drill.

### [x] P2-OPS-011 — Add scheduled deadline delivery and platform metrics

Implement scheduled, monitored deadline reminders and a firm-wide at-risk queue without relying on
staff opening the dashboard. Add request latency/error-rate, job, database-pool, document-generation,
backup, and reminder-delivery metrics with alert thresholds.

Acceptance:

- Time-controlled tests cover due-soon, overdue, corrected, filed, duplicate-suppression, and failed
  delivery cases.
- Delivery failures alert an operator and remain visible for retry.
- Metrics contain tenant-safe dimensions and no client PII.

Completion evidence (2026-07-10): a hosted tenant scheduler now evaluates due-soon, overdue,
corrected and filed deadlines without dashboard traffic, retains deduplicated outbox intents and job
evidence, claims deliveries with compare-and-set leases, retries fixed-code provider failures with
bounded exponential backoff, and routes first/max-attempt fixed-code alerts to the monitoring sink.
The firm dashboard presents the pending/delivering/failed queue, attempt and retry state, direct
period action, Owner-only delivery-cycle control and explicit no-platform-submission wording.

The platform meter records request count/latency/status class, scheduled jobs, document generation,
database-pool opens/active use and reminder delivery with only route templates and fixed enums.
Restricted snapshots combine those observations with durable job, backup-age and reminder-backlog
state; fixed thresholds route deduplicated alerts with per-tenant suppression that never becomes a
provider dimension. Tests prove finite serializable values, all eight configured alert conditions,
the complete safe-dimension allowlist, obvious PII redaction, per-tenant repeat suppression and
resolution re-arming.

Time-controlled planner/provider tests cover due-soon, overdue, corrected, filed, outside-window,
duplicate suppression, provider rejection/unavailability and invalid configuration. A live
PostgreSQL behavioral test additionally proves failure becomes a visible `RetryScheduled` queue row,
alerts the operator, can be explicitly retried, then delivers without creating a duplicate intent or
job on replay. The focused backend gate passed 13/13 plus the PostgreSQL test with zero skips;
frontend queue/runtime/render coverage passed 8/8, and the strict runtime schema rejects recipient
or payload PII fields.

## Phase 6 — Test, contract, and architecture quality

### [ ] P0-QA-001 — Replace self-fulfilling golden scenarios

Build independently derived, realistic, multi-period scenarios for Micro LTD, Small Abridged LTD,
DAC Small, CLG charity, and Medium/Audit Required. Include stock, fixed assets, disposals, loans,
payroll, tax, passive income, disclosures, adjustments, comparatives, signatures, external
validation, and failure cases.

Acceptance:

- Scenarios traverse public services/endpoints rather than directly seeding classification, filing,
  validation, approval, or generation flags.
- Expected figures and wording are independently reviewed.
- PostgreSQL execution is mandatory in release CI.
- External validation evidence is tied to the exact generated iXBRL hash.

Autonomous remediation evidence (2026-07-10; item intentionally remains open):

- `backend/Accounts.Tests/Fixtures/golden-corpus-independent-v1.json` is a byte-pinned,
  versioned five-scenario input/expectation fixture. Each scenario now carries fixed prior/current
  raw size inputs, fixed workflow arithmetic, and expected PDF/iXBRL wording outside production
  service code. `GoldenCorpusFixtureIntegrityTests` rejects any unreviewed byte drift and proves the
  fixture labels qualified-accountant review and real ROS validation as pending.
- `FilingGoldenCorpusScenarioTests` now obtains classification and filing regime decisions from
  `SizeClassificationService` and `FilingRegimeService`, persists opening-balance review and all
  year-end confirmation inputs through the public `YearEndEndpoints`, persists the board approval
  date through `PeriodStatusEndpoint`, and records only exact server-generated CRO
  PDF/signature-page bytes through `FilingWorkflowService`/`FilingReleaseGate`. The endpoint actors
  are explicitly automated and cannot satisfy the pending qualified-accountant gate. The scenarios
  no longer insert review, generated-package, validation or approval flags and do not manufacture a
  signed auditor opinion; the audited medium scenario remains blocked.
- `GoldenCorpusPostgresReleaseTests` runs all five immutable scenarios through the same decision,
  finalisation and artifact-retention workflows on PostgreSQL. It proves structured synthetic input
  is rejected before persisting any external-validation fields while filing-ready generation is
  disabled, and that even a free-form reference carrying the exact generated hash cannot satisfy the
  trusted-validator gate. The focused `FilingReleaseGateTests` exact-hash mismatch regression covers
  the later structured-ingestion branch without representing its synthetic input as real evidence.
- `ProductionReadinessReportService.BuildGoldenCorpus` now reports every scenario as
  `machine-covered-review-pending`; it no longer labels DAC or CLG as externally ready, treats SoFA
  or trustees evidence as satisfied, or describes a fabricated medium auditor completion. Every
  scenario links the mandatory all-scenario PostgreSQL verifier and preserves the real open gates.
- `.github/workflows/ci.yml` contains a dedicated `Run mandatory PostgreSQL golden corpus release
  gate` step with `ACCOUNTS_REQUIRE_POSTGRES_GOLDEN_CORPUS=true`; the test fails rather than silently
  skipping when that release-gate mode lacks a PostgreSQL connection.
- `backend/Accounts.Tests/Fixtures/golden-corpus-reconciled-small-v1.json` now supplies a second,
  independently calculated and SHA-256-pinned fixture for the canonical `small-abridged-ltd`
  scenario. In one two-period ledger it retains opening equity, stock, a fixed-asset acquisition and
  disposal, payroll and employer costs, current/long-term bank debt, corporation tax, passive
  deposit income, dividends and an approved manual accrual, with exact P&L, balance-sheet, cash-flow
  and reserves expectations. `ReconciledGoldenFixtureIntegrityTests` proves the immutable breadth
  inventory and keeps both qualified-accountant review and real ROS validation explicitly pending.
- The mandatory `GoldenCorpusPostgresReleaseTests` filter now also runs
  `ReconciledSmallAbridgedScenario_CoversTwoPeriodLedgerYearEndTaxNotesComparativesAndIxbrl`. It
  enters classification, share capital, opening balances, inventory, tax, dividends, payroll,
  debt, fixed assets and manual/automatic journal approval through public services or endpoints,
  then proves cent-exact reconciliation, passive-income tax support, payroll support, generated
  statutory-note codes, prior-context iXBRL facts, the conspicuous draft marker and absence of any
  manufactured external-validation row on PostgreSQL. The focused fixture plus live PostgreSQL
  gate passed 2/2 with zero skips.
- That reconciled execution exposed and fixed a real classification defect: income explicitly
  marked `IsNonTradingIncome` was still presented as turnover solely because its account code began
  with `4`. `FinancialStatementsService` now excludes reviewed passive income from turnover,
  presents it as other income, retains it in profit before tax, and leaves it in the 25% tax-support
  bucket. The PostgreSQL fixture asserts both presentation and tax classification.

Residual blockers: the fixed figures and wording have not yet been independently signed off by a
qualified accountant; no genuine ROS/external-validator response exists for any exact generated
iXBRL hash. The formerly missing retained end-to-end breadth is now machine-covered in one
reconciled PostgreSQL scenario, but those human and external requirements must not be replaced by
synthetic acceptance evidence; this item therefore remains open.

### [x] P1-QA-002 — Prefer behavioral tests over source-string assertions

Replace file-presence and source-text assertions with endpoint, component, integration, migration,
rendered output, and external validator behavior wherever feasible.

Acceptance:

- Every P0 regression has a failing-before/passing-after behavioral test.
- Contract tests detect actual schema/behavior drift, not only expected prose.

Completion evidence (10 July 2026): every P0 remediation is exercised through service, endpoint,
render, generated-contract, relational migration or raw PostgreSQL behavior, including cross-tenant,
stale-candidate, overposting, unsupported-regime, concurrency, audit-chain and fail-closed error
paths. The final two source-order substitutes were replaced with live PostgreSQL tests proving a
period lease blocks the endpoint until release and concurrent deadline calculations serialize to one
stable row/type. Remaining source/file assertions are isolated supplemental configuration, policy,
action-pinning and forbidden-pattern checks; no P0 correctness or security closure relies on them in
place of behavior. The strict Release run passed 1,031 backend tests with 0 failures and 0 skips
against PostgreSQL. The fresh/previous-release migration behavior and evidence verifier passed 1/1
with zero skips, and the frontend behavioral/contract/render totals and production build above also
passed. A final repository run caught and repaired stale no-direct-verifier source locations after
the readiness split, proving that release scripts are executed as gates rather than accepted from
source inspection alone.

### [x] P1-QA-003 — Generate and enforce typed API contracts

Adopt OpenAPI-generated or otherwise single-source request/response contracts, combined with
frontend runtime parsing for critical financial payloads.

Engineering remediation evidence (2026-07-10): the .NET 10 API now emits a committed OpenAPI 3.1
document during the official build-time document-host pass. The document host skips database and
startup side effects without weakening fail-fast checks for real application runs. Critical auth,
trial-balance, profit-and-loss, balance-sheet, statement-source/readiness, cash-flow, equity,
corporation-tax, CT1-support, filing-support and filing-readiness responses carry explicit response
metadata. The same metadata pass exposed and fixed an invalid inferred DELETE request body before it
could remain a latent runtime failure. Pinned `openapi-typescript` generates the frontend route and
DTO types; `openapiContract.ts` binds critical generated response/key shapes to the deliberately
stricter Zod runtime parsers. CI rejects backend OpenAPI drift, while
`verify-generated-api-contract.mjs` independently regenerates TypeScript into a temporary directory,
byte-compares it, and requires typed critical request/response bodies. The verifier passed against
177 paths and 182 schemas, and TypeScript passed with `--noEmit --incremental false`. See
`Docs/architecture/generated-api-contracts.md`. The final regenerated contract is OpenAPI 3.1.1;
the Swashbuckle development UI now uses a distinct document name
so it cannot silently overwrite the committed Microsoft OpenAPI `v1` contract with a 3.0 document.

### [x] P2-ARCH-001 — Split oversized backend/test/readiness modules

After behavior stabilizes, split the approximately 3,900-line readiness service, 18,000-line test
file, and other oversized modules into domain-focused components with explicit contracts.

Completion evidence (10 July 2026): `ProductionReadinessReportService.cs` is now a 1,043-line
orchestrator over explicit contracts plus statutory (1,020 lines), release (833), accountant/visual
(783) and operations (468) catalog modules. The former 22,150-line `AccountsWorkflowTests` monolith
is split into eight domain partials: core fixtures (977), accounting (1,548), year-end/ledger (2,221),
filing/compliance (4,102), identity/authentication (2,998), tenant/authorization (1,889), audit/
privacy/platform (4,236) and policy/configuration (4,468). Architecture boundary tests retain those
module seams. Post-split verification passed the complete 569-test workflow class and the final
1,031-test backend Release suite with zero failures/skips, plus a zero-warning API build and the full
frontend module-boundary/test/type/lint/build gates.

### [x] P2-DOC-001 — Reconcile canonical documentation with implementation

Update stale EF Core version, test counts, entity/service counts, commands, route inventory, current
limitations, release state, and score semantics in `README.md`, `CLAUDE.md`, and `AGENTS.md`.

Completion evidence (2026-07-10): `README.md` and `CLAUDE.md` now identify EF Core 10, the pinned
.NET 10 toolchain, PostgreSQL 16.4 and the current production topology. Brittle hand-maintained
entity/service/migration/test totals were replaced by the EF model, committed OpenAPI 3.1 contract
(177 paths / 182 schemas at this revision), direct test-run output and candidate-bound CI evidence.
The route inventory now covers identity lifecycle/MFA, privacy, working papers, filing handoffs,
deadline delivery, metrics and fail-closed readiness while explicitly preserving the no-direct-
CRO/ROS boundary. The executable backend gate invokes `Accounts.Tests.csproj` directly; this fixes
the observed .NET 10 `.slnx` invocation that could return success after a build without executing
tests. `AGENTS.md` records that current command and labels its older score/count entries as historical
handoff evidence. The README, canonical guide, active handoff and final audit matrix now agree on
current release-blocked semantics and genuine human/external completion requirements.

## Phase 7 — Required human and external completion

These tasks cannot be fabricated or completed autonomously without real reviewers/providers.
Autonomous engineering work must prepare exact evidence workspaces and then leave these gates
blocked until genuine evidence is supplied.

### [ ] HUMAN-001 — Named visual QA

Review every canonical route/state across supported themes/viewports, including accessibility,
mobile density, keyboard behavior, error states, and maximum-data states.

### [ ] HUMAN-002 — Current source-law review

A named competent reviewer compares every pinned CRO, Revenue, FRC, and Charities Regulator source
against implemented rules and records effective-date/platform impact decisions.

### [ ] HUMAN-003 — External ROS/iXBRL validation

Retain real external acceptance for every supported golden scenario, tied to exact artifact and
taxonomy-package hashes, warnings/errors, remediation, provider reference, and decision.

### [ ] HUMAN-004 — Qualified-accountant acceptance

A currently qualified named accountant walks every supported scenario and route, reviews outputs,
figures, wording, notes, legal basis, workflow gates, and unsupported paths, and approves the exact
artifact hashes.

### [ ] HUMAN-005 — Manual handoff acceptance

Retain medium/audit-required and unsupported-path handoff evidence, including the actual signed
auditor artifact where applicable.

### [ ] HUMAN-006 — Real monitoring-provider confirmation

A named operator confirms delivery, correlation, no-PII posture, alert routing, and provider event
reference in the actual configured provider.

### [ ] HUMAN-007 — Production backup/restore drill

A named operator performs and retains the production/environment drill with measured RPO/RTO,
integrity checks, and restored-output verification.

## Final verification matrix

Before declaring completion, run and retain at minimum:

```powershell
# Backend
cd backend
..\.dotnet-sdk\dotnet.exe test Accounts.Tests\Accounts.Tests.csproj -c Release `
  "-p:ArtifactsPath=$env:TEMP\accounts-final-art"

# Frontend
cd ..\frontend
npm.cmd ci
npm.cmd test
npm.cmd run lint
npx.cmd tsc --noEmit --incremental false
npm.cmd run build

# Repository / operational gates
cd ..
node scripts/verify-ci-actions.mjs
scripts\verify-no-direct-filing-submission.ps1 `
  -CommitSha <release-commit> `
  -GitHubActionsRunUrl <release-run-url>
scripts\verify-release-evidence.ps1 <completed-evidence-arguments>
scripts\verify-release-artifact-pack.ps1 <exact-candidate-arguments>
```

Also require:

- fresh and previous-release PostgreSQL migration tests;
- mandatory PostgreSQL audit and golden-path integration tests;
- full live-stack browser journeys;
- complete visual/accessibility state matrix;
- container/image scan, SBOM, provenance, signature/attestation, and digest verification;
- real external ROS validation;
- all genuine human evidence gates;
- clean worktree, exact commit/run identity, and final release artifact pack with zero failures.

## Completion reporting

Every autonomous session should leave a concise handoff recording:

- completed checklist IDs and commits;
- files and migrations changed;
- tests run with pass/fail/skip totals;
- current P0/P1 remaining items;
- external/human blockers that remain genuinely unavailable;
- latest exact CI run and artifact identity;
- whether the release remains blocked, and why.

Do not mark the platform complete merely because code tests pass or the token/session budget ends.
