# Monitoring Provider Confirmation Template

Use this template to confirm the controlled CI smoke event reached the real configured
error-tracking provider before real filing use.

## Release Candidate

- Commit SHA:
- GitHub Actions run URL:
- Artifact name: `monitoring-error-routing-smoke`
- Artifact name: `structured-json-log-sample`
- Operator name:
- Operator role:
- Confirmation date/time UTC:

Required formats: use the full 40-character commit SHA, the exact
`https://github.com/.../actions/runs/...` run URL, UTC timestamps ending in `Z`
or `+00:00`, a positive integer JSON log line count, and `yes` for both matched
monitoring lines and both sensitive-input-absence fields.
Use real provider, server/client event, correlation, base URL, and provider-event evidence
references. Do not use `accepted`, `none`, `n/a`, `pending`, `todo`, or `tbd`
as stand-ins for provider evidence. Use the accepted decision only after the
both provider events, both structured-log correlations, no-PII review, and alert routing path
are confirmed for the exact release candidate.

## CI Evidence Values

From `monitoring-error-routing-report.json`:

- Provider:
- Event id:
- Correlation id:
- Base URL:
- Checked at UTC:
- Client event code:
- Client event id:
- Client correlation id:
- Client normalized route:
- Client sensitive input absent: yes / no

From `structured-log-report.json`:

- Structured log file:
- JSON log line count:
- Matched monitoring smoke line: yes / no
- Matched client monitoring line: yes / no
- Synthetic sensitive markers absent: yes / no

## Provider Confirmation

- [ ] Event id is visible in the configured provider project.
- [ ] Environment is the expected release/smoke environment.
- [ ] Request path is `/api/system/monitoring/error-smoke`.
- [ ] Controlled client request path is `/api/system/monitoring/client-event`.
- [ ] Correlation id matches the CI smoke artifact.
- [ ] Client event id is visible in the configured provider project.
- [ ] Client correlation id matches the controlled client-event log line.
- [ ] Client normalized route contains no arbitrary identifier or query value.
- [ ] No PII or client filing data is present on the event.
- [ ] The client event contains no exception message, request/response body, form value, financial value, credential, email, or client name.
- [ ] Alert routing / owner notification path is configured.
- [ ] Screenshot or provider permalink is retained in the release evidence store.

Provider event URL or reference:

Client provider event URL or reference:

Operator notes:

## Decision

- [ ] Accepted as monitoring-provider confirmation evidence for this release candidate.
- [ ] Rejected; monitoring-provider confirmation issues below must be remediated and re-reviewed.

Operator signature:
