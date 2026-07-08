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

## Required Evidence Pack

- [ ] `dependency-audit-release`
- [ ] `production-safety-config`
- [ ] `monitoring-error-routing-smoke`
- [ ] `structured-json-log-sample`
- [ ] `postgres-backup-restore-drill`
- [ ] `visual-smoke-screenshots`
- [ ] Production readiness report export or screenshot
- [ ] Generated PDF/iXBRL/tax support outputs for each accepted scenario

## Golden Corpus Walkthrough

For each scenario, record whether the generated outputs, gates, source-law evidence,
wording, workbench journey, and manual handoff behavior are accepted.

| Scenario | Outputs | Gates | Source-law evidence | Wording | Workbench journey | Decision |
| --- | --- | --- | --- | --- | --- | --- |
| micro-ltd-standard |  |  |  |  |  |  |
| small-ltd-abridged |  |  |  |  |  |  |
| dac-small |  |  |  |  |  |  |
| clg-charity |  |  |  |  |  |  |
| medium-audit-required |  |  |  |  |  |  |

## Required Route Walkthrough

For each route, confirm the accountant can answer the route decision question using
the visible evidence and generated artifacts.

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
