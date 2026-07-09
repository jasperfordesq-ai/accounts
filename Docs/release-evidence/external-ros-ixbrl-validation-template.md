# External ROS/iXBRL Validation Template

Use this template to retain the external ROS/iXBRL validation reference for the
exact generated iXBRL pack before any real Revenue filing use.

Internal XML checks are not Revenue acceptance evidence.

## Release Candidate

- Commit SHA:
- GitHub Actions run URL:
- Production readiness report timestamp:
- Reviewer name:
- Reviewer role:
- Review date/time UTC:

## Validation Evidence

- External validation provider:
- Validation environment:
- Validation run/reference id:
- Validation report file or URL:
- Generated iXBRL artifact name:
- Generated iXBRL SHA-256:
- Taxonomy package:
- Company/period reference:

Required formats: use the full 40-character commit SHA, the exact
`https://github.com/.../actions/runs/...` run URL, UTC timestamps ending in `Z`
or `+00:00`, and 64-character SHA-256 digests for every generated iXBRL hash.
Use a real external validation reference for each scenario. Use `none`,
`accepted`, or `remediated` in the `Warnings/errors` column only when every
warning or error has been accepted by the reviewer or remediated. Use `accepted`
in the `Decision` column only when the external reference, artifact hash,
taxonomy package, and warnings/errors status are accepted for the exact release
candidate. Record the actual taxonomy package or retained package reference for
each scenario; do not use `accepted`, `none`, `n/a`, `pending`, `todo`, or `tbd`
as the taxonomy package.
Scenario external validation references and retained taxonomy package references
must include the scenario code, so evidence cannot be reused against the wrong
golden corpus row.

## Required Evidence Pack

- [ ] Generated iXBRL XHTML package for each accepted scenario.
- [ ] External ROS/iXBRL validation reference for each accepted scenario.
- [ ] Validation references are for the exact release candidate commit.
- [ ] Validation references are for the exact generated iXBRL artifact hashes.
- [ ] Validation environment and provider are recorded.
- [ ] Validation warnings/errors are recorded and accepted or remediated.
- [ ] Internal XML checks are not treated as Revenue acceptance evidence.

## Golden Corpus Validation Coverage

For each scenario, record the external validation reference, artifact hash, taxonomy
package, warnings/errors, and decision.

| Scenario | External reference | Artifact hash | Taxonomy package | Warnings/errors | Decision |
| --- | --- | --- | --- | --- | --- |
| micro-ltd |  |  |  |  |  |
| small-abridged-ltd |  |  |  |  |  |
| dac-small |  |  |  |  |  |
| clg-charity |  |  |  |  |  |
| medium-audit-required |  |  |  |  |  |

## Decision

- [ ] Accepted as external ROS/iXBRL validation evidence for this release candidate.
- [ ] Rejected; validation issues below must be remediated and re-reviewed.

Validation issues or limitations:

Reviewer signature:
