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
- Visual inventory version:
- Canonical state count:
- Canonical material route count:
- Canonical UI state count:
- Retained screenshot count:
- Semantic content hash count:
- Minimum PNG IDAT byte size:
- Minimum screenshot pixel sample count:
- Minimum sampled distinct color count:
- Minimum screenshot luminance range:
- Minimum automated contrast ratio:
- Reviewer name:
- Reviewer role:
- Review date/time UTC:

Required formats: use the full 40-character commit SHA, the exact
`https://github.com/.../actions/runs/...` run URL, and UTC timestamps ending in
`Z` or `+00:00`. The first four minimum visual evidence fields must be positive
integers copied from the retained `visual-smoke-evidence-report.json`; sampled
distinct color count must be at least `4`, luminance range must be at least
`10`, and minimum automated contrast ratio must be at least `3.0`. The visual
artifact file fields must be exactly `visual-smoke-manifest.json`,
`visual-smoke-evidence-report.json`, and
`accountant-workbench-evidence-report.json`. Reviewer name, reviewer role and
reviewer signature fields must be real retained evidence values, not
placeholders such as `accepted`, `none`, `n/a`, `pending`, `todo`, or `tbd`.
The visual inventory version must be `canonical-material-states-v1`; the
retained machine evidence must prove 32 canonical states, 18 material routes,
9 named UI states and 192 screenshots. Semantic content hash count must be at
least 32, with `semanticDistinctnessPassed` set to `true` in the evidence report.

## Required Artifact Checks

- [ ] Artifact contains `visual-smoke-manifest.json`.
- [ ] Artifact contains `visual-smoke-evidence-report.json` from `node scripts/verify-visual-smoke-artifacts.mjs`.
- [ ] Artifact contains `accountant-workbench-evidence-report.json` from `node scripts/verify-accountant-workbench-evidence.mjs`.
- [ ] Manifest inventory version and 32-state inventory match the production readiness report.
- [ ] Every canonical state has light and dark screenshots.
- [ ] Every canonical state has mobile, tablet and desktop screenshots.
- [ ] The 18 required material routes and 9 required UI states are present exactly once in the canonical inventory.
- [ ] Semantic content and PNG captures are distinct across intended states for each theme/viewport pair.
- [ ] Screenshot hashes and byte sizes match the manifest.
- [ ] Evidence report status is `passed` and covers all route/theme/viewport combinations.
- [ ] Evidence report includes screenshot nonblank pixel diversity evidence for every screenshot.
- [ ] Evidence report includes passed automated `theme-contrast` smoke evidence for every screenshot.
- [ ] Every screenshot summary includes `pngIdatByteSize`, `pixelSampleCount`, `sampledDistinctColorCount`, `luminanceRange`, and `themeContrastResult.minimumContrastRatio`.
- [ ] No screenshot is blank, truncated, low-information, or obviously stale.

## Human Visual Review Scope

For each canonical state in the manifest, record pass/fail and notes for:

- Workflow hierarchy is clear.
- Dense tables remain readable and scannable.
- No visible text overlap or clipped controls.
- No horizontal overflow in mobile, tablet or desktop captures.
- Dark and light themes have acceptable contrast.
- Release blockers and sign-off states are visually obvious.
- Accountant next actions are visible without relying on explanatory copy.

Use exactly `pass` in each light/dark mobile/tablet/desktop cell only when that state
capture is visually accepted for this release candidate. The verifier rejects
blank, failed, pending, `accepted`, or other ambiguous state acceptance cells.
Each state `Notes` cell must be the exact retained visual evidence reference
`visual-smoke-evidence-report.json#routeCoverage.<state-id>`.

| State | Mobile light | Mobile dark | Tablet light | Tablet dark | Desktop light | Desktop dark | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- |
| login |  |  |  |  |  |  |  |
| password-change |  |  |  |  |  |  |  |
| dashboard |  |  |  |  |  |  |  |
| onboarding |  |  |  |  |  |  |  |
| production-readiness |  |  |  |  |  |  |  |
| company-detail |  |  |  |  |  |  |  |
| period-workspace |  |  |  |  |  |  |  |
| classification |  |  |  |  |  |  |  |
| categorisation |  |  |  |  |  |  |  |
| year-end |  |  |  |  |  |  |  |
| adjustments |  |  |  |  |  |  |  |
| notes |  |  |  |  |  |  |  |
| charity |  |  |  |  |  |  |  |
| financial-statements |  |  |  |  |  |  |  |
| statement-source-trail |  |  |  |  |  |  |  |
| statement-profit-and-loss |  |  |  |  |  |  |  |
| statement-balance-sheet |  |  |  |  |  |  |  |
| statement-tax-computation |  |  |  |  |  |  |  |
| statement-cash-flow |  |  |  |  |  |  |  |
| statement-equity-changes |  |  |  |  |  |  |  |
| statement-directors-report |  |  |  |  |  |  |  |
| filing-review |  |  |  |  |  |  |  |
| workbench-preview |  |  |  |  |  |  |  |
| state-loading |  |  |  |  |  |  |  |
| state-empty |  |  |  |  |  |  |  |
| state-maximum-data |  |  |  |  |  |  |  |
| state-error |  |  |  |  |  |  |  |
| state-partial-error |  |  |  |  |  |  |  |
| state-permission-denied |  |  |  |  |  |  |  |
| state-read-only |  |  |  |  |  |  |  |
| state-stale |  |  |  |  |  |  |  |
| state-conflict |  |  |  |  |  |  |  |

## Decision

- [ ] Accepted for this release candidate.
- [ ] Rejected; defects listed below must be fixed and re-reviewed.

Defects or follow-up notes:

Reviewer signature:
