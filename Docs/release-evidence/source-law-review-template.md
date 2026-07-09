# Source-Law Review Template

Use this template for the named review of the pinned source-law snapshot before real
CRO or Revenue filing use. Internal source links and tests are not a substitute for
checking the current CRO, Revenue, FRC, and Charities Regulator guidance pages.

## Release Candidate

- Commit SHA:
- GitHub Actions run URL:
- Production readiness report timestamp:
- Source-law snapshot fingerprint:
- Source-law snapshot content hash:
- Reviewer name:
- Reviewer role:
- Review date/time UTC:
- Qualified accountant name:
- Qualification / professional body:

Required formats: use the full 40-character commit SHA, the exact
`https://github.com/.../actions/runs/...` run URL, UTC timestamps ending in `Z`
or `+00:00`, and a 64-character SHA-256 digest for the source-law snapshot
content hash.
Use `yes` for `URL reachable` and `Guidance wording compared` only after the
current source page has been checked. Use the checked effective date in
`YYYY-MM-DD` format, or `not dated` when the source has no dated update. Use
`no change`, `reflected...`, or `blocking...` in `Platform impact`; use
`accepted` in `Decision` only when the source row is accepted for this release
candidate. The verifier rejects generic `accepted` placeholders in the URL,
effective-date, guidance-comparison and platform-impact cells.

## Required Evidence Pack

- [ ] `source-law-snapshot-fingerprint`
- [ ] `source-law-traceability-index`
- [ ] `source-law-maintenance-protocol`
- [ ] `source-law-review-ledger`
- [ ] `source-law-change-review-note`
- [ ] `qualified-accountant-source-law-signoff`
- [ ] Production readiness report export or screenshot

## Required Source Review

For each source in the production readiness report source-law snapshot, record the
current-source check outcome and any platform impact.

| Source ID | URL reachable | Effective date checked | Guidance wording compared | Platform impact | Decision | Notes |
| --- | --- | --- | --- | --- | --- | --- |
| cro-financial-statements-requirements |  |  |  |  |  |  |
| cro-guarantee-company |  |  |  |  |  |  |
| cro-unlimited-company |  |  |  |  |  |  |
| cro-group-company |  |  |  |  |  |  |
| cro-medium-company |  |  |  |  |  |  |
| cro-auditors-report |  |  |  |  |  |  |
| revenue-ixbrl-overview |  |  |  |  |  |  |
| revenue-ixbrl-contents |  |  |  |  |  |  |
| revenue-accepted-taxonomies |  |  |  |  |  |  |
| frc-frs-102 |  |  |  |  |  |  |
| frc-frs-105 |  |  |  |  |  |  |
| charities-regulator-annual-report |  |  |  |  |  |  |

## Explicit Review Assertions

- [ ] CRO source changes have been reflected in filing readiness, generated accounts wording, and manual-handoff gates where applicable.
- [ ] Revenue iXBRL and accepted-taxonomy source changes have been reflected in generated iXBRL assumptions or blocked for external validation.
- [ ] FRC FRS 102/105 source changes have been reflected in accounting-standard selection, disclosures, and notes wording where applicable.
- [ ] Charities Regulator source changes have been reflected in charity annual-return evidence and manual review gates where applicable.
- [ ] Any changed source effective date or guidance wording is either reflected in code/docs/tests or listed below as a blocking release defect.

## Decision

- [ ] Accepted as source-law review evidence for this release candidate.
- [ ] Rejected; source-law issues below must be remediated and re-reviewed.

Changed-source notes, defects, or no-change rationale:

Reviewer signature:

Qualified accountant source-law sign-off:
