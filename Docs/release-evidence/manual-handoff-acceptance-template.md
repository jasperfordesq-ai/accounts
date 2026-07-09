# Manual Handoff Acceptance Template

Use this template for the named professional acceptance of unsupported or
audit-required paths that must remain outside automated filing approval. This is
mandatory before anyone relies on generated outputs for a manual handoff case.

## Release Candidate

- Commit SHA:
- GitHub Actions run URL:
- Production readiness report timestamp:
- Reviewer name:
- Reviewer role:
- Firm / reviewer capacity:
- Review date/time UTC:

Required formats: use the full 40-character commit SHA, the exact
`https://github.com/.../actions/runs/...` run URL, and UTC timestamps ending in
`Z` or `+00:00`.
Use real retained evidence references in the scenario and unsupported-path
tables; do not use `accepted`, `none`, `n/a`, `pending`, `todo`, or `tbd` as
stand-ins for evidence references. Use `accepted` in the `Decision` and
`Reviewer decision` columns only when the reviewer has accepted the retained
evidence and confirmed the path remains blocked to manual professional ownership.
Scenario evidence references must include the scenario code, and unsupported-path
evidence references must include the path code, so retained evidence cannot be
reused against the wrong manual handoff row.

## Required Evidence Pack

- [ ] `medium-audit-required` golden corpus evidence reviewed.
- [ ] Signed auditor report evidence retained for the audit-required scenario.
- [ ] Manual handoff note retained for the audit-required scenario.
- [ ] Filing readiness profile snapshot retained before handoff acceptance.
- [ ] Generated PDF/iXBRL/tax support outputs are treated as reviewer aids only.
- [ ] Unsupported automated filing paths remain blocked in the application.
- [ ] Qualified accountant acceptance references this manual handoff decision.

## Manual Handoff Scenario Coverage

| Scenario | Auditor evidence | Manual handoff note | Filing readiness snapshot | Decision |
| --- | --- | --- | --- | --- |
| medium-audit-required |  |  |  |  |

## Unsupported Path Coverage

For each unsupported path, record where the release evidence proves the path is
blocked to manual professional ownership rather than automated filing approval.

| Path code | Release evidence reference | Reviewer decision |
| --- | --- | --- |
| plc-public-company |  |  |
| unlimited-company |  |  |
| excluded-regulated-entity |  |  |
| group-consolidation |  |  |
| audit-required-without-auditor-report |  |  |
| complex-corporation-tax |  |  |
| direct-cro-ros-submission |  |  |

## Decision

- [ ] Accepted as manual handoff evidence for this release candidate.
- [ ] Rejected; manual handoff issues below must be remediated and re-reviewed.

Manual handoff issues or limitations:

Reviewer signature:
