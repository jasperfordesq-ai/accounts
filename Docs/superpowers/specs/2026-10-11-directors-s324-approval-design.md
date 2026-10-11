# Directors' s.324 Approval Record Design

Status: approved 11 October 2026, revised the same day after the legal check below widened the scope · Item 5 of the 10 October live-use blocker list

## Context

FilingBridge currently gates every CRO filing on a **verified named qualified-accountant approval**
recorded on `CroFilingPackage` (`FilingReadinessProfileService` evidence `accountant-review`,
blocker `accountant-review-required`; `FilingReleaseGate.EnsureStoredQualifiedAccountantEvidence`
and the CRO manifest's `accountant-verification` / `accountant-decision-binding` entries). The
public `BindApprovalAsync` path refuses free-form approvals, so on a live installation the gate
cannot be cleared at all, and the live micro-entity charity (hOUR Timebank CLG, FY2026) shows
"Accountant review: Required" indefinitely.

Irish law does not require an accountant to prepare, review or approve any company's financial
statements. It requires the **directors** to prepare them and the **board** to approve them, signed
on its behalf by two directors, or by the sole director, on the face of the balance sheet. It
requires a **statutory audit** only where the company is not entitled to, or does not use, the
audit exemption. The board-approval control is represented nowhere in the application today, and
the accountant gate stands in for a requirement the law does not make.

## Legal basis (checked 11 October 2026 against the Irish Statute Book)

| Provision | Effect |
|---|---|
| Companies Act 2014 s.290(1) | "The directors of a company shall prepare entity financial statements" |
| s.324(1), (2), (4), (5) | Board approves; signed on its behalf by 2 directors where there are 2 or more, or by the sole director; signatures on the face of the balance sheet; copies state the signatories' names |
| s.333 | Statutory audit by statutory auditors **unless** the company is entitled to and chooses the audit exemption |
| ss.358, 360, 362, 363 | Exemption for small companies; lost by Schedule 5 / securitisation categories (s.362) and unless the annual return is filed on time for this and the preceding year (s.363) |
| s.334, as modified for a CLG by s.1218 | Members (any one member of a CLG) can serve notice requiring an audit |
| Charities Act 2009 s.50(13)(a) | The charity audit / independent-examination requirement does not apply to a charitable organisation that is a company. The Charities (Amendment) Act 2024 would delete this exception but is **not commenced** at the date of revision |

Nothing in these provisions requires an accountant. Accountant review is therefore modelled as an
**optional** attestation, never as a gate.

## Goal

For every company on the supported filing path, replace the qualified-accountant approval gate with
the directors' s.324 approval record, which is at least as hard to satisfy as the gate it replaces
and cannot be cleared by typing a name into a box. Where the company is not audit-exempt, the
statutory auditor's report remains required in addition, exactly as today. Accountant review
becomes an optional, recorded attestation.

## Non-goals

- No change to how audit is required: `auditRequired` and the retained signed auditor's report
  evidence (`AuditorsReportReference` / `AuditorsReportSha256`, the `signed-auditor-report` and
  `attached-auditor-report` manifest entries) are unchanged.
- No change for unsupported or manual-handoff paths (PLC, regulated, group contexts): they stay
  outside the supported filing path.
- No change to `external-ros-validation-reference` or `source-law-change-review-note`.
- The platform-level release-evidence item `named-accountant-approval-record` / assurance action
  `qualified-accountant-signoff` (`ProductionReadinessReleaseCatalog`) certifies the product, not a
  company's filing. Consistent with the legal basis it stops being a **release blocker** and becomes
  an optional assurance item (retained evidence slot, `BlocksRelease = false`). This is a separate,
  small change in the same slice.
- No change to the Revenue-side blockers (iXBRL generation policy, external ROS validation).
  "External filing" may still show blocked for those reasons; that is correct and separate.
- No electronic-signature verification. The application retains and hash-binds the signed copies;
  it does not attempt to validate handwriting or certificates.
- No change to the CRO B1 certification page (director + secretary) or the existing
  `executed-signing-pack` evidence.

## Scope

The directors' approval gate applies to **every company on the supported filing path**, of any size
and regime. Whether an audit is **also** required is the existing `auditRequired` decision, computed
live (the stored `FilingRegime.AuditExempt` can be stale because the s.334 notice endpoint does not
re-run the regime): an audit is required unless the regime is audit-exempt **and** no s.334 / s.1218
member notice has been received for the period. When an audit is required, the signed auditor's
report evidence stays a blocker exactly as today; the directors' approval is required as well,
because s.324 applies to audited companies too.

## What the record proves

A clear directors' approval for a period exists only when **all** of these hold:

| # | Requirement | Validation |
|---|---|---|
| 1 | Board approval date | On or after the period end; not in the future (Ireland date); on or after the date the current CRO accounts PDF was generated, taken from a new `cro_filing_packages.AccountsPdfGeneratedAtUtc` column set only when the accounts PDF changes (the existing `GeneratedAt` also moves when the signature page is regenerated, so it cannot be used). A board cannot approve a pack that did not yet exist |
| 2 | Approving directors | Count the company's directors in office on the approval date (role `Director`, appointed on or before it, not resigned before it). If there are 2 or more: two **distinct** such directors are required. If there is exactly one: that sole director alone (s.324(2)). If there are none: rejected. Officers are chosen by id; names are copied from the officer records, never entered as free text |
| 3 | Minute or written-resolution reference | Required, trimmed, 3–200 characters |
| 4 | The minute or written resolution itself | Uploaded PDF, retained with SHA-256; client-declared digest must match the bytes |
| 5 | The generated s.324 approval page | Generated by FilingBridge for exactly items 1–3 and the current accounts PDF SHA-256; retained with SHA-256 |
| 6 | That page, signed by both directors | Uploaded PDF, retained with SHA-256; client-declared digest must match the bytes |
| 7 | Binding to the accounts | The record stores the `AccountsPdfSha256` and `ArtifactSourceFingerprintSha256` current at recording; any later regeneration of the accounts PDF revokes the approval |
| 8 | Who recorded it | The authenticated user (Owner or Reviewer), with a recent MFA re-authentication; display name and audit id come from the session, never from the request |
| 9 | Integrity hash | SHA-256 over a canonical form of every field above (ARD-record style), recomputed and verified on every read that clears the gate |

## Data model

Two new append-only tables, following the ARD evidence pattern
(`AnnualReturnDateRecord` / `AnnualReturnDateEvidenceIntegrity` / DbContext change validation).

### `directors_approval_pages` — a generated s.324 page

| Column | Notes |
|---|---|
| `Id`, `PeriodId`, `TenantId` | tenant-scoped; RLS like every period-owned table |
| `ApprovalDate` | date |
| `FirstDirectorOfficerId`, `SecondDirectorOfficerId` | FK to `company_officers`; distinct; second is null only for a sole-director company |
| `FirstDirectorName`, `SecondDirectorName` | snapshotted from the officer rows at generation |
| `DirectorsInOfficeCount` | snapshotted, so a later officer edit cannot silently change whether one signature sufficed |
| `ResolutionReference` | text |
| `AccountsPdfSha256`, `ArtifactSourceFingerprintSha256` | snapshot of the CRO package at generation |
| `PageArtifact` (bytea), `PageSha256` | the generated PDF |
| `GeneratedByUserId`, `GeneratedByDisplayName`, `GeneratedAtUtc` | from the session |
| `RecordSha256` | canonical integrity hash |

Generating again (for example after correcting the date) adds a new row; rows are never edited.

### `directors_approval_events` — approvals and revocations

| Column | Notes |
|---|---|
| `Id`, `PeriodId`, `TenantId` | |
| `Kind` | `Approved` or `Revoked` (text enum) |
| `ApprovalPageId`, `ApprovalPageSha256` | the page this event concerns |
| `SignedPageArtifact`, `SignedPageSha256` | `Approved` only |
| `ResolutionArtifact`, `ResolutionSha256` | `Approved` only |
| `RevocationReason` | `Revoked` only (e.g. `accounts-regenerated`) |
| `RecordedByUserId`, `RecordedByDisplayName`, `RecordedAtUtc` | from the session, or `system` for automatic revocation |
| `RecordSha256` | canonical integrity hash, chained by including the prior event's `RecordSha256` for the period (empty for the first) |

Database check constraints enforce kind-specific required columns and 64-hex digests.
`AccountsDbContext` rejects `Modified` / `Deleted` entries for both tables and recomputes the hash on
`Added`, exactly as it does for ARD records. Migration is additive: two new tables and one nullable column
(`cro_filing_packages.AccountsPdfGeneratedAtUtc`, backfilled from `GeneratedAt` where an accounts PDF exists).

**Current approval** for a period = the latest `Approved` event whose page is not followed by a
`Revoked` event, whose page's `AccountsPdfSha256` and `ArtifactSourceFingerprintSha256` equal the CRO
package's current values, and whose event and page integrity hashes both verify.

## Behaviour

### Generate the approval page

`POST /api/companies/{c}/periods/{p}/filing/directors-approval-page` (download: `GET …/filing/directors-approval-page/{id}`)
Body: `approvalDate`, `firstDirectorOfficerId`, `secondDirectorOfficerId` (null only when the company
has a sole director in office on the approval date), `resolutionReference`.

- Requires the supported filing path, the CRO accounts PDF to be generated, and validation rows 1–3.
- Generates a one-page PDF (QuestPDF, `DocumentGeneratorService`) titled "Approval of the financial
  statements — Companies Act 2014, section 324", stating the company, CRO number, period, that the
  financial statements for the period were approved by the board on the approval date under the
  stated minute/resolution reference, the SHA-256 of the accounts PDF approved, and one signature
  block per approving director ("Signed on behalf of the board") with the director's name and the
  date. A sole-director page states that the company has a sole director (s.324(2)).
- Stores a `directors_approval_pages` row; returns the page id, page SHA-256 and a download link.
- Allowed roles: Owner, Accountant, Reviewer (it records no decision).

### Record the approval

`POST /api/companies/{c}/periods/{p}/filing/directors-approval`
(added explicitly to the recent-authentication route list; the `/filing/{action}` shape is what `RoleAuthorizationService` already uses for filing actions)
Body: `approvalPageId`, `signedPageArtifact` + `signedPageSha256`,
`resolutionArtifact` + `resolutionSha256` (base64 bytes + declared digest, the existing evidence
pattern).

- Owner or Reviewer only; recent MFA re-authentication required.
- Rejects unless: the period is on the supported filing path; the page exists for this period and its
  integrity hash verifies; the page's accounts PDF and source fingerprint still equal the CRO
  package's current values; both uploads are non-empty PDFs (`%PDF-` header) whose SHA-256 equals the
  declared digest; neither upload is byte-identical to the unsigned generated page; the officer rules
  (row 2) still hold on the approval date; there is no current approval already.
- Appends an `Approved` event, sets `AccountingPeriod.ApprovalDate` to the page's approval date if not
  already set (consistent with the existing finalisation rule), and writes audit event
  `DirectorsApprovalRecorded` with an evidence snapshot (hashes, officer ids, reference, date).

### Revocation

- Regenerating the CRO accounts PDF (`FilingReleaseGate.RecordCroArtifactAsync`, which already calls
  `RevokeCroApproval` when the accounts change) also appends a `Revoked` event with reason
  `accounts-regenerated` for any current directors' approval, in the same unit of work.
- No user-initiated delete. A director-initiated withdrawal is out of scope for this slice.

### Gate re-pointing (every supported company)

- `FilingReadinessProfileService`: evidence `accountant-review` and blocker
  `accountant-review-required` are removed and replaced by evidence `directors-approval`
  ("Board approval of the financial statements, s.324") and blocker `directors-approval-required`.
  `AccountantReviewRequired` is `false` for every company. `AccountantReviewState` reports the
  optional attestation (below): "Not required by law — not recorded" or "Optional review recorded:
  {name} ({body}, {membership})". The sign-off packet's step `accountant-approval` becomes
  `directors-approval` with the same complete / pending / blocked semantics, and
  `ReadyForAccountantApproval` is renamed `ReadyForDirectorsApproval`. A statutory source reference
  for Companies Act 2014 s.324 is added to `IrishStatutoryRuleSources`.
- The audit evidence is untouched: where `auditRequired` is true the signed auditor's report remains a
  blocker, and the directors' approval is required as well.
- `AssertCanApproveCroPackAsync` / `DetermineAllowedNextActions` ignore `directors-approval-required`
  in place of `accountant-review-required` and offer `record-directors-approval` when the accounts PDF
  exists and no current approval does.
- `FilingReleaseGate`: CRO approval is bound from the current directors' approval (a new internal
  `BindDirectorsApprovalAsync`, invoked by the existing CRO approve workflow), setting `ApprovedBy` to
  the approving directors' names, `ApprovalCapacity` = `board-of-directors-s324`, `ApprovalScope` =
  `cro-final-filing`. `EnsureStoredQualifiedAccountantEvidence` becomes `EnsureStoredApprovalEvidence`,
  which verifies the directors' approval (integrity, page/accounts binding, uploads, officer rules).
  `CroManifest` binds `directors-approval-page`, `directors-signed-page`, `board-resolution` and
  `directors-approval-record` hashes in place of `accountant-verification` /
  `accountant-decision-binding`; the auditor entries are still added when the company is audited.
- `BindVerifiedApprovalAsync` (qualified-accountant binding) no longer approves a CRO pack. Its
  verification logic is reused for the optional attestation.
- `ExternalFilingHandoffService`: unchanged contract; the B1 presenter remains `ApprovedBy`.

### Optional accountant review (attestation, never a gate)

- A period may carry an optional "Reviewed by an accountant" record: reviewer name, professional
  body, membership number, review date, and an optional uploaded review letter (SHA-256). It is
  stored on the existing `CroFilingPackage` verification fields only when a review is supplied, and
  **no blocker, release gate or manifest entry depends on it**. When present it is listed in the
  readiness evidence as satisfied and shown in the gate snapshot as "Accountant review: optional —
  recorded".
- Endpoint: `POST /api/companies/{c}/periods/{p}/filing/accountant-review` (Owner, Accountant or Reviewer).
- Regenerating the accounts PDF clears it, so it always refers to the accounts actually filed.

### Platform release catalog

- `ProductionReadinessReleaseCatalog`: assurance action `qualified-accountant-signoff` and checklist
  item `accountant-final-signoff` / evidence `named-accountant-approval-record` become optional
  assurance (`BlocksRelease = false`, status `optional`); the verification manifest entry
  `qualified-accountant-final-signoff` is no longer required for `release-artifact-pack`.
  `external-ros-validation-reference` and `source-law-change-review-note` are unchanged.
- The Charities (Amendment) Act 2024 change to s.50(13)(a) is recorded as a watch item in the
  statutory rule sources, so its commencement triggers the existing source-law-change review.

## Frontend

- `apiContracts.ts`: the filing readiness profile contract today rejects
  `accountantReviewRequired: false` outright. It is changed to require `false` (accountant review is
  never mandatory) and to require the new field `approvalAuthority: "board-of-directors-s324"`.
  The CT worksheet, CT bridge and working-paper schemas that pin `qualifiedAccountantReviewRequired:
  z.literal(true)` describe tax and working-paper review prompts, not filing gates; they are left
  unchanged in this slice and listed as a follow-up.
- Filing tab: a "Directors' approval (s.324)" panel for every supported period. Step 1: approval
  date, director selects populated from officers in office on that date (one select when the company
  has a sole director; resigned or non-director officers are not selectable), reference → "Generate
  approval page" → download. Step 2: upload the signed page and the minute/resolution → "Record
  approval" (triggers re-authentication). Once recorded: date, directors, reference, the three short
  hashes and the record hash, in the style of the ARD panel.
- A collapsed "Optional: reviewed by an accountant" section beneath it.
- Gate snapshot and period workbench show "Directors' approval: Recorded / Required" and
  "Accountant review: Optional"; "Statutory audit: Required / Exempt" where relevant.

## Error handling

All validation failures are `BusinessRuleException` (400) with a specific message naming the rule
(e.g. "Director Two was not a director in office on 2026-11-20"; "The company had 3 directors in
office on 2026-11-20, so two directors must sign"). Unsupported or manual-handoff path → 400 "This
company is outside the supported filing path." Missing period/officer/page in another tenant → 404
(existing tenant rule). Integrity failure on read → the gate treats the approval as absent and the
readiness profile reports "Directors' approval record failed integrity verification".

## Testing

- Scope: micro exempt, small exempt, small-abridged, medium, large and micro with a member notice all
  require the directors' approval; those that are not audit-exempt additionally still require the
  signed auditor's report; manual-handoff paths are rejected.
- Officers: two-plus directors require two distinct ones; a sole director signs alone; the same
  director twice; a secretary-only officer; a director appointed after the date; a director resigned
  before the date; an officer from another company; the in-office count snapshotted on the page.
- Dates: before period end; future; before the accounts PDF generation.
- Uploads: digest mismatch; empty; non-PDF; signed upload identical to the unsigned page.
- Append-only: modifying or deleting either table is rejected by the DbContext; hash tamper is
  detected; event hash chain.
- Revocation: regenerating the accounts PDF revokes the directors' approval and clears the optional
  accountant review; a stale page cannot be approved.
- Gate: the readiness blocker clears only with a current approval; `approve-cro-pack` allowed only
  then; the release manifest contains the directors' entries (and the auditor entries when audited);
  `accountantReviewRequired` is false for every company; the optional review never blocks.
- Release catalog: the accountant sign-off item no longer blocks release; the two named items are
  unchanged.
- Contracts: backend serialisation of the profile; the frontend contract rejects
  `accountantReviewRequired: true` and a missing `approvalAuthority`.
- Golden corpus: the scenario expectations that assert accountant review is unsatisfied and required
  are updated to assert the directors' approval instead (an intentional, reviewed change).
- Postgres: RLS isolation of both tables; migration up on a copy of a populated schema.
- Render: the panel for every supported period; sole-director single select; directors list
  excludes ineligible officers; optional accountant section.

## Rollout

Additive migration (two tables, one nullable column, check constraints, RLS policies via the
existing provisioner). Ships in the next Private Server release. Existing periods show "Directors'
approval: Required" until one is recorded. Any CRO pack already approved through the old accountant
binding keeps its approval (none exist on the live installation; the public path never allowed one).

## Additions found while planning

- **Revenue and Charity approvals.** `FilingReleaseGate` binds Revenue and Charity package approvals
  through the same qualified-accountant evidence as CRO. They are re-pointed to the same current
  directors' approval (same manifest entries, `ApprovalCapacity` = `board-of-directors-s324`);
  otherwise the accountant would still be a hidden gate on those two workflows.
- **Executed CRO signing pack and auditor's report endpoints.** `RecordVerifiedCroSignatureAsync` and
  `RecordVerifiedAuditorReportAsync` exist but have no endpoint, so no live installation could ever
  satisfy those gates. Two Owner/Reviewer endpoints are added (`POST …/filing/cro-signing-pack`,
  `POST …/filing/auditor-report`), with recent re-authentication; the signing pack takes the director
  and secretary by officer id, never by typed name. The checks themselves are unchanged.
- **Member audit notice.** The readiness `auditRequired` flag also becomes true when
  `MemberAuditNoticeReceived` is set (s.334 / s.1218), so a notice served after approval brings the
  auditor's report back as a blocker.

## Open questions

None blocking. Recorded for the owner: whether a later slice should let the directors withdraw an
approval before filing (currently only regeneration revokes); whether the s.324 page should also be
appended to the accounts PDF itself; and the follow-up on the tax / working-paper
`qualifiedAccountantReviewRequired` prompts.
