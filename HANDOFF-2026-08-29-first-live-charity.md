# FilingBridge handoff — findings from the first live charity, 29 August 2026

Paste this to Claude Code with the repo root `C:\platforms\htdocs\accounts` open.

---

## Context you need before touching anything

FilingBridge was used end-to-end for the first time against a **real charity** on 29 August 2026:
hOUR Timebank CLG, CRO 608327, RCN 20162023, financial year end 31 July, micro-entity on FRS 105,
audit-exempt under s.360, and a registered charity holding Revenue Charitable Tax Exemption.

That run is now company id 1, nine accounting periods from incorporation, 61 imported and
categorised FY2026 bank transactions, and a completed charity profile. **Do not delete or reseed
that data** — it is the live record and there is no other copy of the categorisation work.

**The owner's goal is that this app removes the need to engage an accountant for a micro-entity
charity filing.** That goal is legally sound: Irish law does not require an accountant for an
audit-exempt micro entity; the directors prepare and approve the statements. Your job is to make
the output *correct and demonstrably so*, not to delete the checks that ask whether it is. Where a
gate exists, either satisfy it properly or replace it with a stronger mechanism — never just
lower it.

---

## Priority 1 — defects found in live use

### 1.1 AIB CSV import fails on every row

**Symptom:** importing a real AIB internet-banking CSV export returns
`totalRows: 61, importedRows: 0` with `Row N: could not parse date` for every row.

**Cause:** `backend/Accounts.Api/Services/ImportService.cs`. `DetectFormat` (~line 311) selects the
AIB format when the header contains `"posted account"`. `KnownFormats` (~line 34) then applies
`new("AIB", new(0, 1, 3, 4, 2, "dd/MM/yyyy"))` — date in column 0.

The actual AIB export header is:

```
Posted Account, Posted Transactions Date, Description1, Description2, Description3,
Debit Amount, Credit Amount, Balance, Posted Currency, Transaction Type,
Local Currency Amount, Local Currency
```

Account number is column 0; **date is column 1**. The importer parses `"936375 - 16074058"` as a
date and fails. It also assumes a single signed amount column, whereas AIB supplies **separate
Debit and Credit columns**, and three separate description columns.

**Fix:** add a distinct format for this layout — date 1, descriptions 2–4 concatenated, reference 0,
debit 5, credit 6, balance 7 — with debit negated. That needs a `ColumnMapping` that supports
split debit/credit and multi-column descriptions, which the current record shape does not express.
Consider `DebitColumn`/`CreditColumn` as an alternative to `AmountColumn`, and a
`DescriptionColumns` array.

**Do not** just fix the index. Two different AIB export shapes exist and both must import. Detect
on the full header signature, not one substring.

**Regression test:** use `Timebank Documents/Financial/Bank Transactions/hTB Bank Transactions FY2026
(01.08.2025-31.07.2026) export 2026-07-01.csv` in the user's OneDrive project folder as a fixture
(anonymise it). Expected: 61 rows imported, net movement −33.72, closing balance −10,165.13.

### 1.2 Dashboard breaks for any period without calculated deadlines

**Symptom:** with a company present but no deadlines calculated, the dashboard renders
`filing deadlines unavailable — Invalid dashboard deadline response contract: items.0.deadline -
Invalid input: expected object, received undefined`, and every work-queue card shows
"Deadline evidence unavailable".

**Cause:** `/api/dashboard/deadlines` (`Endpoints/DeadlineEndpoints.cs` line ~12) returns items whose
`deadline` is undefined when none exists; the frontend contract in
`frontend/src/lib/apiContracts.ts` requires an object.

**Fix:** make the contract tolerate a company with no calculated deadlines and render a clear
"deadlines not yet calculated — calculate now" affordance instead of an error. A brand-new company
hits this on day one, which is the worst possible first impression.

**Also:** deadlines are only created by an explicit `POST /periods/{id}/deadlines/calculate`.
Calculate them automatically when a period is created, or prompt at the end of onboarding.

### 1.3 Saving charity info silently discards field edits

**Cause:** `Services/CharityReportingService.cs`, `SaveCharityInfoAsync` (~line 262). The
`retainExistingGovernanceEvidence` branch is entered when the governance answer, note and evidence
reference are unchanged and no new artifact is supplied — and that branch **skips the entire block
that updates every other field**. Changing `charityType`, `grossIncome`, `principalActivities` or
`trusteeExpensesDetails` returns `200 OK` and saves nothing.

This cost real time in live use: `charityType` would not change from a descriptive string to `CLG`
until the governance evidence artifact was re-uploaded.

**Fix:** separate the two concerns. Always update the ordinary fields; apply the retain-or-replace
logic only to `GovernanceEvidenceArtifact` and its hash and reviewer stamps.

**Related, and worse:** in the same method the `else` branch sets
`GovernanceEvidenceArtifact = null` whenever no artifact is supplied. A caller updating an
unrelated field can silently destroy retained governance evidence. Make artifact removal explicit
and intentional.

### 1.4 Onboarding cannot accept an existing company

`CompanyOnboardingValidation` requires the first accounting period to begin on the incorporation
date and be flagged as the first year. For a company incorporated in 2017 that is being onboarded
in 2026, the operator must create the first period in 2017 and then add every intervening period
by hand — nine in this case — before touching the year they actually care about.

**Fix:** support onboarding at a stated opening position: an "existing company, first period under
management" path that records the incorporation date as a fact without demanding the full historic
chain, or generates the chain automatically from incorporation date, year end and any year-end
changes. Keep the chronology invariants for periods that do exist.

---

## Priority 2 — charity correctness, and the reason the dashboard cries wolf

### 2.1 Revenue deadlines are generated for charities that are exempt from filing them

`Services/DeadlineService.CalculateDeadlinesAsync` always creates a `Revenue` deadline. It creates a
`Charity` deadline conditionally on `company.IsCharitableOrganisation`, so the pattern already
exists — it is just not applied to Revenue.

A body holding **Charitable Tax Exemption** is exempt from filing a CT1 or financial statements to
Revenue for as long as the exemption is held. Revenue confirmed this in writing for this charity on
16 December 2022, ROS enquiry 2109-57763.

**Effect in live use:** nine false Revenue deadlines, the oldest showing as overdue since 2019, and
the dashboard promoting one of them as the "highest-risk client" headline. That is worse than
useless — it trains the operator to ignore the dashboard.

**Fix:** add a company-level `HoldsCharitableTaxExemption` flag with an evidence reference and date,
alongside the existing `IsCharitableOrganisation`. Suppress Revenue deadline generation when set.
Surface the exemption and its evidence on the company profile so the suppression is visible and
auditable rather than silent.

### 2.2 A Revenue deadline can never be closed, even when it does not apply

`POST /periods/{id}/mark-filed` with `deadlineType: Revenue` returns **409**: *"Revenue filing-ready
iXBRL generation is disabled. The current XHTML is an incomplete accountant-review prototype only."*

So a Revenue row that should never have existed also cannot be dismissed. Once 2.1 is fixed this
mostly disappears, but add a **"not applicable"** disposition for a deadline, distinct from "filed",
carrying a reason and an evidence reference. "Filed" and "not required" are different facts and the
record should not have to lie about which one it is.

### 2.3 SORP 2019 artifacts are unimplemented, which is the whole charity feature

`Services/CharitySorpDecisionService` returns, for a period beginning 1 August 2025:

> "The period falls under SORP 2019. This release has not implemented and source-verified the
> pre-2026 artifact framework, so manual professional handoff is required."

The SoFA, the trustees' report and the fund balances are the charity-specific value of the product,
and for any period beginning before 1 January 2026 none of them can be produced. Every Irish charity
with a year end before that date — which is every one of them, this year — falls into manual handoff.

**This is the single largest gap between the app and its stated purpose.** If the goal is to replace
an accountant for a charity filing, SORP 2019 has to be implemented, not deferred.

Note also `IsSupportedCompanyCharity` requires `charityType` to normalise to exactly `CLG`,
`COMPANYLIMITEDBYGUARANTEE` or `COMPANYCHARITY`. That is an undocumented magic-string contract on a
free-text field. Make it an enum.

---

## Priority 3 — the release gates, honestly

Eight gates block production filing. Sort them and treat them differently.

**Gates that are engineering work — clear these yourself:**

- `automated golden corpus baseline` — `FilingGoldenCorpusScenarioTests` across the five canonical
  scenarios in CI
- `PDF, tax and iXBRL generation baseline` — backend tests and retained evidence manifests
- `frontend accountant workbench` — route-level render tests across the primary journey
- SORP 2019 artifacts (2.3 above)

**A gate that needs the owner, not an accountant:**

- `external-ros-validation-reference` — requires a real validator response, a taxonomy hash and an
  exact artifact hash. Generate a pack, put it through Revenue's iXBRL validator, retain the
  response. This is achievable without engaging anyone. Build the tooling that makes retaining that
  evidence a one-command step.

**Gates that name a human's judgement:**

- `named-accountant-approval-record` — qualified-accountant acceptance across the golden corpus
- `source-law-change-review-note` — a named reviewer confirming the pinned legislation snapshot is
  current
- `HUMAN-001` … `HUMAN-007` — retained reviewer evidence

The owner wrote these rules and may change them; it is his product. **But do not simply delete them.**
The gate is a proxy for the question "has anyone established that the output is right", and deleting
it does not answer that question — it moves the entire risk onto the director who signs the accounts.

If the reviewer role is to be redefined away from "qualified accountant", replace it with something
stronger, not nothing:

1. **An independently derived golden corpus.** Worked examples where the correct FRS 105 output is
   known from a source other than this codebase — published model accounts, prior filed accounts,
   or a hand-computed set — that the app must reproduce exactly. This is the real substitute for
   professional review and it scales; a reviewer's hour does not.
2. **A named competent reviewer**, defined by what they must check rather than by their
   qualification, with the checklist retained as evidence.
3. **A standing source-law review** with the snapshot fingerprint diffed on a schedule, so
   legislative drift is detected rather than assumed away.

Do that and the assurance is genuinely better than a one-hour review by someone unfamiliar with the
entity. Skip it and the app is asserting correctness it has not established.

---

## What to do first

1. **1.1 AIB import** — highest practical value; without it every operator hand-edits a CSV.
2. **2.1 Revenue exemption flag** — removes nine false overdue alerts and restores trust in the dashboard.
3. **1.3 charity info save** — a silent data-loss bug; fix before anyone else uses the app.
4. **1.2 dashboard contract** — first-run experience.
5. **2.3 SORP 2019** — the large one. Scope it properly before starting.
6. **1.4 existing-company onboarding.**
7. **Priority 3 gates**, starting with the golden corpus.

For each: write the failing test first, from the live evidence above.

---

## Verification

- **1.1** the fixture CSV imports 61 of 61 rows, no warnings, net movement −33.72
- **1.2** a fresh company with zero deadlines renders the dashboard with no error banner
- **1.3** changing `charityType` alone, with no artifact supplied, persists — and the stored
  governance artifact survives
- **2.1** a company with the exemption flag set generates CRO and Charity deadlines only
- **2.2** a deadline can be recorded "not applicable" with a reason, without an iXBRL gate
- **2.3** a period beginning 1 August 2025 produces a SoFA and trustees' report that reconcile to
  balance-sheet net assets

## Do not

- Delete or reseed company id 1
- Weaken the MFA step-up on `mark-filed` — recording a statutory filing is exactly where that belongs
- Remove a release gate without replacing the assurance it stands for
- Assume the CRO or Revenue position from documents on disk. Check CORE and the Charities Regulator
  register. A stale letter in a folder caused a wrong finding on this project earlier the same day.
