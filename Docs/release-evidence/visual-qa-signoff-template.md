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

## Required Artifact Checks

- [ ] Artifact contains `visual-smoke-manifest.json`.
- [ ] Artifact contains `visual-smoke-evidence-report.json` from `node scripts/verify-visual-smoke-artifacts.mjs`.
- [ ] Artifact contains `accountant-workbench-evidence-report.json` from `node scripts/verify-accountant-workbench-evidence.mjs`.
- [ ] Manifest route count matches the production readiness report.
- [ ] Every route has light and dark screenshots.
- [ ] Every route has desktop and mobile screenshots.
- [ ] Screenshot hashes and byte sizes match the manifest.
- [ ] Evidence report status is `passed` and covers all route/theme/viewport combinations.
- [ ] Evidence report includes screenshot nonblank pixel diversity evidence for every screenshot.
- [ ] Evidence report includes passed automated `theme-contrast` smoke evidence for every screenshot.
- [ ] Every screenshot summary includes `pngIdatByteSize`, `pixelSampleCount`, `sampledDistinctColorCount`, `luminanceRange`, and `themeContrastResult.minimumContrastRatio`.
- [ ] No screenshot is blank, truncated, low-information, or obviously stale.

## Human Visual Review Scope

For each route in the manifest, record pass/fail and notes for:

- Workflow hierarchy is clear.
- Dense tables remain readable and scannable.
- No visible text overlap or clipped controls.
- No horizontal overflow in mobile captures.
- Dark and light themes have acceptable contrast.
- Release blockers and sign-off states are visually obvious.
- Accountant next actions are visible without relying on explanatory copy.

Use exactly `pass` in each light/dark desktop/mobile cell only when that route
capture is visually accepted for this release candidate. The verifier rejects
blank, failed, pending, `accepted`, or other ambiguous route acceptance cells.
Each route `Notes` cell must be the exact retained visual evidence reference
`visual-smoke-evidence-report.json#routeAcceptance.<route>`.

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
