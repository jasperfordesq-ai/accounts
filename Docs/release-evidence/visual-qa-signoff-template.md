# Visual QA Sign-Off Template

Use this template for the named human review of the CI `visual-smoke-screenshots`
artifact before real CRO or Revenue filing use.

## Release Candidate

- Commit SHA:
- GitHub Actions run URL:
- Artifact name: `visual-smoke-screenshots`
- Visual smoke manifest file:
- Visual smoke evidence report file:
- Accountant workbench evidence report file:
- Reviewer name:
- Reviewer role:
- Review date/time UTC:

## Required Artifact Checks

- [ ] Artifact contains `visual-smoke-manifest.json`.
- [ ] Artifact contains `visual-smoke-evidence-report.json` from `node scripts/verify-visual-smoke-artifacts.mjs`.
- [ ] Artifact contains `accountant-workbench-evidence-report.json` from `node scripts/verify-accountant-workbench-evidence.mjs`.
- [ ] Manifest route count matches the production readiness report.
- [ ] Every route has light and dark screenshots.
- [ ] Every route has desktop and mobile screenshots.
- [ ] Screenshot hashes and byte sizes match the manifest.
- [ ] Evidence report status is `passed` and covers all route/theme/viewport combinations.
- [ ] No screenshot is blank, truncated, or obviously stale.

## Human Visual Review Scope

For each route in the manifest, record pass/fail and notes for:

- Workflow hierarchy is clear.
- Dense tables remain readable and scannable.
- No visible text overlap or clipped controls.
- No horizontal overflow in mobile captures.
- Dark and light themes have acceptable contrast.
- Release blockers and sign-off states are visually obvious.
- Accountant next actions are visible without relying on explanatory copy.

| Route | Desktop light | Desktop dark | Mobile light | Mobile dark | Notes |
| --- | --- | --- | --- | --- | --- |
| dashboard |  |  |  |  |  |
| company-detail |  |  |  |  |  |
| period-workspace |  |  |  |  |  |
| filing-review |  |  |  |  |  |
| financial-statements |  |  |  |  |  |
| production-readiness |  |  |  |  |  |
| workbench-preview |  |  |  |  |  |

## Decision

- [ ] Accepted for this release candidate.
- [ ] Rejected; defects listed below must be fixed and re-reviewed.

Defects or follow-up notes:

Reviewer signature:
