# Qualified-Accountant Acceptance Template

Use this template for the named qualified-accountant walkthrough across the seeded
golden filing corpus and live accountant workbench journey. This is mandatory before
real CRO or Revenue filing use.

## Release Candidate

- Commit SHA:
- GitHub Actions run URL:
- Production readiness report timestamp:
- Accountant name:
- Qualification / professional body:
- Firm / reviewer capacity:
- Review date/time UTC:

Required formats: use the full 40-character commit SHA, the exact
`https://github.com/.../actions/runs/...` run URL, and UTC timestamps ending in
`Z` or `+00:00`.

## Required Evidence Pack

- [ ] `dependency-audit-release`
- [ ] `production-safety-config`
- [ ] `monitoring-error-routing-smoke`
- [ ] `structured-json-log-sample`
- [ ] `postgres-backup-restore-drill`
- [ ] `production-readiness-report`
- [ ] `production-readiness-verification-report.json`
- [ ] `visual-smoke-screenshots`
- [ ] `accountant-workbench-evidence-report.json`
- [ ] Generated PDF/iXBRL/tax support outputs for each accepted scenario

## Golden Corpus Walkthrough

For each scenario, record whether the generated outputs, gates, source-law evidence,
wording, workbench journey, and manual handoff behavior are accepted.
Use `accepted` in each scenario review cell only when that review scope is
professionally accepted for the release candidate. Use `accepted` in the
`Decision` column only when the whole scenario is professionally accepted for
this release candidate. The verifier rejects blank, pending, failed, or
ambiguous scenario scope acceptance cells.
Record a real retained scenario walkthrough evidence reference for every
accepted scenario, such as
`qualified-accountant-walkthrough-ledger#micro-ltd`; do not use `accepted`,
`none`, `n/a`, `pending`, `todo`, or `tbd` as the evidence reference. Each
scenario evidence reference must include the matching scenario code.

| Scenario | Outputs | Gates | Source-law evidence | Wording | Workbench journey | Decision | Scenario evidence reference |
| --- | --- | --- | --- | --- | --- | --- | --- |
| micro-ltd |  |  |  |  |  |  |  |
| small-abridged-ltd |  |  |  |  |  |  |  |
| dac-small |  |  |  |  |  |  |  |
| clg-charity |  |  |  |  |  |  |  |
| medium-audit-required |  |  |  |  |  |  |  |

## Required Route Walkthrough

For each route, confirm the accountant can answer the route decision question using
the visible evidence and generated artifacts.
Use exactly `yes` for `Decision question answered`; use exactly `accepted` for
`Evidence accepted` only when the route evidence is professionally accepted.
Record a real retained workbench evidence reference for every accepted route, such as
`accountant-workbench-evidence-report.json#routeAcceptance.dashboard`; do not use
`accepted`, `none`, `n/a`, `pending`, `todo`, or `tbd` as the evidence reference.
The verifier rejects ambiguous route decision/evidence cells; use exact `yes` in
`Decision question answered` and exact `accepted` in `Evidence accepted`.
The workbench evidence reference must match the route key exactly:
`accountant-workbench-evidence-report.json#routeAcceptance.<route>`.
Each route `Notes` cell must include a retained route walkthrough note or
reference containing the matching route code, for example
`qualified-accountant-route-walkthrough#dashboard`.

| Route | Decision question answered | Evidence accepted | Workbench evidence reference | Notes |
| --- | --- | --- | --- | --- |
| dashboard |  |  |  |  |
| company-detail |  |  |  |  |
| period-workspace |  |  |  |  |
| filing-review |  |  |  |  |
| financial-statements |  |  |  |  |
| production-readiness |  |  |  |  |
| workbench-preview |  |  |  |  |

## Explicit Non-Automation Acceptance

- [ ] Direct CRO submission remains unsupported.
- [ ] Direct ROS submission remains unsupported.
- [ ] The app records workflow states only.
- [ ] Final real-world filing use remains blocked without named professional approval.
- [ ] Medium/audit-required scenario remains manual-handoff until auditor evidence is present.

## Decision

- [ ] Accepted for real filing preparation subject to external CRO/ROS processes.
- [ ] Rejected; issues below must be remediated and re-reviewed.

Issues or limitations:

Qualified accountant signature:
