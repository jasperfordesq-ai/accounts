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

## CI Evidence Values

From `monitoring-error-routing-report.json`:

- Provider:
- Event id:
- Correlation id:
- Base URL:
- Checked at UTC:

From `structured-log-report.json`:

- Structured log file:
- JSON log line count:
- Matched monitoring smoke line: yes / no

## Provider Confirmation

- [ ] Event id is visible in the configured provider project.
- [ ] Environment is the expected release/smoke environment.
- [ ] Request path is `/api/system/monitoring/error-smoke`.
- [ ] Correlation id matches the CI smoke artifact.
- [ ] No PII or client filing data is present on the event.
- [ ] Alert routing / owner notification path is configured.
- [ ] Screenshot or provider permalink is retained in the release evidence store.

Provider event URL or reference:

Operator notes:

Operator signature:
