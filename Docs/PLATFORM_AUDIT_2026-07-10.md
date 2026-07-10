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
  or the six missing human/external evidence gates.

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

### [ ] P0-REL-001 — Introduce one server-side final filing release gate

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

### [ ] P0-REL-003 — Separate review artifacts from final export and handoff

Before the exact-hash release gate passes, users may obtain only conspicuously marked review
artifacts. Unwatermarked/final export and external handoff must use the same central release gate as
workflow transitions.

Acceptance:

- Pre-approval PDFs and iXBRL carry an unmistakable `DRAFT — NOT FOR FILING` status appropriate to
  the format and cannot be downloaded through a final-export endpoint.
- Final export and handoff fail while any exact-hash approval, signature, external validation, or
  manual-handoff requirement is missing.
- Regeneration changes the hash and immediately revokes prior final-export eligibility.

### [ ] P0-REL-002 — Replace the hard-coded readiness score with derived risk state

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

## Phase 1 — Critical security and data-integrity remediation

### [ ] P0-SEC-001 — Eliminate writable EF entity graph binding

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

### [ ] P0-SEC-002 — Enforce tenant/company/period invariants at persistence boundaries

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

### [ ] P0-SEC-003 — Stop rejected cross-tenant probes mutating victim audit chains

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

### [ ] P0-DATA-001 — Serialize finalisation and accounting writes

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

### [ ] P0-DATA-002 — Make company deletion authoritative, recoverable, and auditable

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

- `backend/Accounts.Api/Endpoints/CompanyDeletionEndpoint.cs:52`

## Phase 2 — Statutory and accounting correctness

### [ ] P0-ACC-001 — Correct period, threshold, size, and regime decisions

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

### [ ] P0-ACC-002 — Enforce valid period chronology

Store and enforce incorporation-aware period chronology: no overlap, controlled gaps, one first
year, correct maximum length, and deterministic prior-period/comparative selection.

Acceptance:

- API and PostgreSQL constraint tests reject periods before incorporation, overlaps, duplicate
  first years, and concurrent overlapping creates.
- Comparative and cumulative calculations select the intended prior period.

### [ ] P0-ACC-003 — Make posted double-entry data the statement source of truth

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

- `backend/Accounts.Api/Endpoints/AdjustmentEndpoints.cs:437`
- `backend/Accounts.Api/Services/AdjustmentService.cs:81`
- `backend/Accounts.Api/Services/AdjustmentService.cs:314`
- `backend/Accounts.Api/Services/FinancialStatementsService.cs:464`

### [ ] P0-STAT-001 — Separate full statutory accounts from reduced CRO copies

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

### [ ] P0-STAT-002 — Replace or disable incomplete Revenue iXBRL generation

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

### [ ] P0-STAT-003 — Retain and bind real qualified-accountant approval

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

### [ ] P0-STAT-004 — Retain the actual signed auditor report

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

### [ ] P1-STAT-005 — Implement a complete regime/fact-based notes checklist

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

- `backend/Accounts.Api/Services/FilingRegimeService.cs:174`
- `backend/Accounts.Api/Services/FinancialStatementsService.cs:818`
- `backend/Accounts.Api/Services/NotesDisclosureService.cs:79`
- `backend/Accounts.Api/Services/NotesDisclosureService.cs:153`

### [ ] P1-STAT-006 — Produce retained charity artifacts from reconciled data

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

- `backend/Accounts.Api/Services/CharityReportingService.cs:25`
- `backend/Accounts.Api/Services/CharityReportingService.cs:98`
- `backend/Accounts.Api/Services/FilingWorkflowService.cs:402`

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

### [ ] P1-STAT-008 — Correct Directors' Report population and wording

Use directors who served during the accounting period, accurate appointment/resignation dates,
profit/loss wording, paid versus proposed dividends, evidenced audit-information representations,
and reviewed principal activities.

Acceptance:

- Timeline fixtures exclude future directors and include relevant resigned directors.
- Profit/loss and dividend wording follows actual facts.
- Audit-information statements require explicit director evidence.

### [ ] P1-STAT-009 — Store the exact ARD date and correct deadline modelling

Replace `ArdMonth` with an exact date/effective history. Distinguish the B1 made-up-to/delivery
deadline from the maximum financial-statement age rule.

Acceptance:

- Official CRO examples, changed ARDs, leap years, weekends, and Irish public holidays match
  expected results.
- No last-day-of-month assumption remains.

Evidence:

- `backend/Accounts.Api/Entities/Company.cs:14`
- `backend/Accounts.Api/Services/DeadlineService.cs:31`
- CRO filing guidance: `https://cro.ie/Annual-Return/Filing-an-Annual-Return/`

### [ ] P1-ACC-004 — Make duplicate imports reviewable rather than destructive

Use available reference, statement balance, batch, account, and source metadata to identify
candidate duplicates. Allow an explicit retain/discard decision instead of silently dropping a
same-date/amount/description transaction.

Acceptance:

- Two genuine identical same-day transactions remain reviewable.
- A re-imported statement is identified without silently losing data.
- Duplicate decisions are audited and reversible before finalisation.
- No source row is discarded before a retained, audited reviewer decision.

### [ ] P1-ACC-005 — Deliver the required internal accountant outputs

Implement retained, drillable lead schedules, categorized-transaction reports, review-exception
reports, adjusted trial balance, working-paper indexes, and corporation-tax bridge outputs. Every
figure must link to transactions, opening balances, year-end facts, journals, and reviewer evidence.

Acceptance:

- Each required internal output has an explicit endpoint/artifact contract and route.
- Totals reconcile to the final statements and tax computation.
- Drill-down tests prove that a final figure resolves to its complete source set.
- Generated working papers carry exact period, reviewer, version, and artifact-hash identity.

### [ ] P1-TAX-002 — Complete the filing-support worksheet and preliminary-tax tracker

Add a clearly scoped field-by-field CT1 support worksheet, numbered panel/field mapping where
officially defined, preliminary-tax due dates and safe-harbour calculations, payment tracking, and
late-filing/surcharge exposure. Unsupported elections or complex cases must remain manual handoff.

Acceptance:

- Official worked examples and independently reviewed fixtures match due dates and calculations.
- The UI and exports never imply that the support worksheet is a directly submittable CT1.

### [ ] P1-FILE-001 — Complete the external filing handoff data model

Represent the B1 annual-return worksheet, shareholder/allotment information needed for the handoff,
CRO presenter and ROS agent/TAIN authority records, CT1 support, external references, correction/
send-back state, and amended/superseding filing chains. Preserve immutable as-filed snapshots and
exact artifact hashes. Direct submission remains unsupported.

Acceptance:

- A complete field-by-field manual handoff can be produced without scraping PDFs or querying the
  database directly.
- Authority/engagement records are required before an external filing state can advance.
- Amendment creates a new linked snapshot; it never rewrites the prior as-filed record.

### [ ] P1-DATA-003 — Add idempotency to create, import, and workflow commands

Use tenant-scoped idempotency keys for company/period creation, imports, document generation records,
and filing-state commands where retries could duplicate data or state changes.

Acceptance:

- Retrying the same key and payload returns the original result without a second mutation.
- Reusing a key with a different payload fails safely.
- Concurrent duplicate requests create exactly one logical result and one domain audit event.

## Phase 3 — Frontend workflow correctness

### [ ] P0-FE-001 — Implement real transaction and audit pagination

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

### [ ] P0-FE-002 — Replace failure-as-empty behavior with explicit resource states

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

### [ ] P0-FE-003 — Correct deadline semantics and dashboard scalability

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

### [ ] P0-FE-004 — Apply one canonical permission matrix to all UI actions

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

### [ ] P0-FE-005 — Make onboarding transactional and idempotent

Use one backend transaction that creates a company and officers, protected by an idempotency key.
Require an explicit incorporation date; never substitute the current date.

Acceptance:

- Failure on any officer rolls back the whole transaction. A retry with the same key creates or
  returns the same single durable company identity; no partial/draft duplicate remains.
- Double submission creates exactly one company.
- Missing incorporation date blocks with an accessible inline error.

Evidence:

- `frontend/src/app/companies/new/page.tsx:154`

### [ ] P0-FE-006 — Runtime-validate all critical API responses

Add shared runtime schemas for company, period, transaction, year-end, adjustment, statement,
notes, charity/SORP, dashboard/deadline batches, filing readiness/workflow status, audit, auth,
pagination, and aggregate envelopes. Do not rely only on TypeScript generics around `res.json()`.

Acceptance:

- Missing fields, wrong types, unknown enums, malformed money/dates, and inconsistent aggregates
  or cross-field invariants fail closed into explicit error states.
- Backend/frontend contract drift fails CI.

Evidence:

- `frontend/src/lib/api.ts:198`

### [ ] P1-FE-007 — Guard unsaved changes across Next.js navigation

Cover links, breadcrumbs, tabs, browser back, refresh, close, and onboarding—not only
`beforeunload`.

Acceptance:

- Cancel retains drafts and location.
- Confirm navigates.
- Saved/clean forms do not prompt.
- Tests cover all editable routes.

Evidence:

- `frontend/src/lib/useUnsavedChanges.ts:9`

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

### [ ] P1-UX-002 — Restructure production readiness using progressive disclosure

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

- `frontend/src/components/AppNavbar.tsx:38`
- `frontend/src/app/companies/[companyId]/periods/[periodId]/classify/page.tsx:254`
- `frontend/src/app/companies/[companyId]/periods/[periodId]/charity/page.tsx:282`
- `frontend/src/components/ConfirmModal.tsx:25`

### [ ] P1-UX-004 — Remove inert sorting

Make columns unsortable by default unless a valid primitive sort accessor/value is supplied. Remove
sort controls from action and unsupported JSX columns.

Acceptance:

- Every visible sort control changes order both directions and updates `aria-sort`.
- Action columns have no sort control.
- Regression tests cover dashboard, officers, periods, transactions, and readiness tables.

Evidence:

- `frontend/src/components/workbench.tsx:988`
- `frontend/src/components/workbench.tsx:1188`

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

### [ ] P1-VIS-002 — Raise contrast and motion checks to WCAG AA

Check 4.5:1 normal text, 3:1 large text and UI components, including links, buttons, focus rings,
placeholders, disabled states, and resolvable gradients. Respect reduced-motion preferences.

Acceptance:

- Automated and human checks cover text and interactive controls.
- Focus is visibly discernible.
- `prefers-reduced-motion: reduce` disables shimmer, spin, slide, fade, and smooth scrolling.

### [ ] P1-UX-005 — Protect destructive actions

Add an accessible confirmation or undo pattern for notes, year-end rows, opening balances, officers,
rules, and similar destructive actions.

Acceptance:

- A single accidental click cannot irreversibly delete material data.
- Confirmation names the record and consequence.
- Cancel preserves the record; failure restores it; success is announced.

### [ ] P2-FE-008 — Improve authentication revalidation UX

Reserve the blocking app spinner for initial authentication. Revalidate sessions in the background
without blanking the app on every pathname change. Distinguish infrastructure failure from 401.

Acceptance:

- Client navigation does not blank the workbench or make redundant `/auth/me` requests.
- Auth service 500 shows service unavailable, not login.
- Genuine 401 still redirects safely.

### [ ] P2-FE-009 — Add frontend monitoring with PII controls

Capture controlled client exceptions and rejected requests with release, environment, route, and
correlation context, while excluding form values, financial payloads, secrets, and client PII.

Acceptance:

- A controlled client error appears in the real provider and matches structured-log correlation.
- Tests prove sensitive values are absent.

### [ ] P2-FE-010 — Split oversized frontend modules after behavior stabilizes

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

## Phase 5 — Operations, supply chain, identity, and governance

### [ ] P0-OPS-001 — Build once and promote exact tested image digests

CI must build one backend image and one frontend image, scan them, create SBOM/provenance, sign or
attest them, push them to the approved registry, and run production smoke by pulling those exact
digests. The migration and API services must use the same application digest.

Acceptance:

- Release evidence contains exact digests, SBOMs, provenance, scan results, and registry references.
- Rebuilt tags, mutable tags, digest mismatches, or unscanned images fail.
- The final deployment instructions consume digests, not locally rebuilt tags.

Evidence:

- `.github/workflows/ci.yml:239`
- `.github/workflows/ci.yml:340`

### [ ] P1-OPS-002 — Implement production user lifecycle and privileged MFA

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

### [ ] P1-OPS-003 — Protect repository governance

Configure protected `main`, required PR review and CI, admin enforcement, force-push/deletion
blocking, required signed commits where supported, secret scanning, push protection, Dependabot
alerts/updates, and CodeQL. Pin third-party Actions to immutable commit SHAs.

Acceptance:

- GitHub API reports all required controls enabled.
- Required checks and review cannot be bypassed by normal pushes.
- Workflow policy rejects floating action tags.
- GitHub API and commit verification prove the configured signed-commit requirement and the release
  candidate's verified signature state.

### [ ] P1-OPS-004 — Make dependency and build inputs reproducible

Align Docker with the declared Node 24 runtime, pin base images by digest, add NuGet lock files and
locked restore, scan containers, generate SBOMs, and run scheduled audits.

Acceptance:

- CI rejects action/base digest drift, lockfile drift, high/critical OS vulnerabilities, Node engine
  mismatch, and unlocked NuGet resolution.
- A scheduled dependency/container audit runs without a source commit and retains its result; a
  deliberately introduced vulnerable test fixture fails the appropriate policy check.

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

### [ ] P1-OPS-006 — Complete real monitoring and incident response

Add provider delivery, log aggregation/retention, redaction, alert routing, escalation ownership, and
an incident runbook.

Acceptance:

- A controlled event appears in the configured real provider, matches the structured-log
  correlation ID, triggers the expected notification, contains no client PII, and is acknowledged by
  a named operator.
- Evidence records monitoring/log retention, measured alert latency, escalation behavior, redaction
  tests, and a completed incident-runbook exercise.

### [ ] P1-OPS-007 — Add migration drift and previous-release upgrade gates

Run pending-model checks, migrate a fresh database and a restored previous-release database, verify
preserved rows/figures, and document rollback or expand/contract strategy for material migrations.

Acceptance:

- CI runs `dotnet ef migrations has-pending-model-changes` and fails on model drift.
- Fresh and previous-release upgrades preserve representative users, financial rows, calculated
  figures, filing snapshots, and audit-chain/checkpoint integrity.
- A forced migration failure proves rollback or safe expand/contract recovery without partial state.

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

### [ ] P1-OPS-009 — Restrict internal readiness/security evidence

Limit `/api/system/production-readiness` and comparable internal evidence to Owner and explicitly
authorized release-review roles.

Acceptance:

- Client and ordinary Accountant requests receive 403.
- Authorized release reviewers retain access.

### [ ] P1-AUD-001 — Complete domain-level audit coverage

Ensure company, officer, period, bank, category, rule, deletion, professional approval, artifact,
and workflow changes record the domain action, tenant/company/period, actor, correlation ID, and
meaningful old/new values in the correct transaction/security chain.

Acceptance:

- An endpoint/domain operation matrix proves every material write records atomic old/new evidence
  in the correct tenant/company chain.
- Failed domain writes do not retain success events; unauthorized/cross-tenant attempts never touch
  the guessed victim chain.
- Audit-chain and checkpoint verification passes before and after every operation family.

### [ ] P2-SEC-004 — Add database-enforced tenant isolation

Evaluate and implement PostgreSQL row-level security or an equivalent database-enforced boundary
for the application role.

Acceptance:

- Deliberately defective application queries still cannot read or write another tenant.

### [ ] P2-AUTH-001 — Add privileged reauthentication and recovery hardening

Add idle/absolute session expiry tests, recent-auth requirements for finalisation/deletion/role
changes/evidence acceptance, breached-password checks, and secure recovery controls.

Acceptance:

- Time-controlled tests prove idle and absolute expiry and immediate session revocation.
- Finalisation, deletion, role changes, and evidence acceptance reject stale authentication and
  require recent MFA-backed reauthentication.
- Recovery cannot bypass tenant, role, audit, MFA, or lockout controls.

### [ ] P2-EVID-001 — Make release evidence durable and authentic

Retain release evidence outside ephemeral Actions retention and use authenticated approvals or
digital signatures that permit independent identity/professional-capacity verification.

Acceptance:

- Evidence remains retrievable after the CI artifact-retention window.
- Altered artifacts, manifests, timestamps, identities, or signatures fail verification.
- Reviewer identity, qualification/capacity, and signature validity can be independently verified.

### [ ] P2-PRIV-001 — Define privacy, retention, and incident handling

Enforce retention and approved deletion/anonymisation rules for login-attempt and audit PII while
preserving statutory audit requirements.

Acceptance:

- Automated retention tests expire data according to the approved schedule.
- Subject access/export and approved erasure/anonymisation workflows locate all applicable data and
  preserve legally required financial/audit evidence with a recorded statutory-retention override.
- A privacy-incident exercise proves notification, containment, evidence preservation, and review.

### [ ] P2-OPS-010 — Add capacity, failover, and recovery tests

Test rate limiting, audit buffering, DB pooling, document generation, large practices, high-volume
transactions, host/database failure, and measured RPO/RTO recovery.

Acceptance:

- Published load profiles meet defined latency, throughput, error-rate, and resource thresholds.
- Rate limits do not corrupt audit or financial writes under concurrency.
- Host/database failure drills restore within target RTO and lose no more than target RPO.
- Document generation and large-practice dashboards remain bounded and observable under load.

### [ ] P2-OPS-011 — Add scheduled deadline delivery and platform metrics

Implement scheduled, monitored deadline reminders and a firm-wide at-risk queue without relying on
staff opening the dashboard. Add request latency/error-rate, job, database-pool, document-generation,
backup, and reminder-delivery metrics with alert thresholds.

Acceptance:

- Time-controlled tests cover due-soon, overdue, corrected, filed, duplicate-suppression, and failed
  delivery cases.
- Delivery failures alert an operator and remain visible for retry.
- Metrics contain tenant-safe dimensions and no client PII.

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

### [ ] P1-QA-002 — Prefer behavioral tests over source-string assertions

Replace file-presence and source-text assertions with endpoint, component, integration, migration,
rendered output, and external validator behavior wherever feasible.

Acceptance:

- Every P0 regression has a failing-before/passing-after behavioral test.
- Contract tests detect actual schema/behavior drift, not only expected prose.

### [ ] P1-QA-003 — Generate and enforce typed API contracts

Adopt OpenAPI-generated or otherwise single-source request/response contracts, combined with
frontend runtime parsing for critical financial payloads.

### [ ] P2-ARCH-001 — Split oversized backend/test/readiness modules

After behavior stabilizes, split the approximately 3,900-line readiness service, 18,000-line test
file, and other oversized modules into domain-focused components with explicit contracts.

### [ ] P2-DOC-001 — Reconcile canonical documentation with implementation

Update stale EF Core version, test counts, entity/service counts, commands, route inventory, current
limitations, release state, and score semantics in `README.md`, `CLAUDE.md`, and `AGENTS.md`.

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
..\.dotnet-sdk\dotnet.exe test Accounts.slnx -c Release `
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
