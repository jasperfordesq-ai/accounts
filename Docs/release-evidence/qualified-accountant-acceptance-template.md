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
- [ ] Generated PDF/iXBRL/tax support outputs for each accepted scenario

## Golden Corpus Walkthrough

For each scenario, record whether the generated outputs, gates, source-law evidence,
wording, workbench journey, and manual handoff behavior are accepted.
Use `accepted` in the `Decision` column only when the scenario is professionally
accepted for this release candidate.

| Scenario | Outputs | Gates | Source-law evidence | Wording | Workbench journey | Decision |
| --- | --- | --- | --- | --- | --- | --- |
| micro-ltd |  |  |  |  |  |  |
| small-abridged-ltd |  |  |  |  |  |  |
| dac-small |  |  |  |  |  |  |
| clg-charity |  |  |  |  |  |  |
| medium-audit-required |  |  |  |  |  |  |

## Required Route Walkthrough

For each route, confirm the accountant can answer the route decision question using
the visible evidence and generated artifacts.
Use `yes` or `accepted` for `Decision question answered`; use `accepted` for
`Evidence accepted` only when the route evidence is professionally accepted.

| Route | Decision question answered | Evidence accepted | Notes |
| --- | --- | --- | --- |
| dashboard |  |  |  |
| company-detail |  |  |  |
| period-workspace |  |  |  |
| filing-review |  |  |  |
| financial-statements |  |  |  |
| production-readiness |  |  |  |
| workbench-preview |  |  |  |

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
