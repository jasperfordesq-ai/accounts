# Monitoring and incident response runbook

This runbook governs production alerts for the Irish statutory accounts platform. It does not
authorize a deployment, a CRO/ROS submission, contact with a client or regulator, or use of real
client data in an exercise.

## Ownership and targets

- `Monitoring:OnCallOwner` names the accountable operator for the environment.
- `Monitoring:AlertRoute` identifies the configured notification route; it must not contain a
  personal phone number, email address, webhook secret, or provider token.
- Acknowledge a page within `Monitoring:AlertAcknowledgementMinutes` (production default: 15).
- Escalate to the named secondary operator and incident commander by
  `Monitoring:EscalationMinutes` (production default: 30) if the alert is unacknowledged, active, or
  its customer/statutory impact is uncertain.
- Structured logs and provider error events use the configured retention periods (production
  default: 90 days). Longer retention requires an approved privacy/statutory basis.

## Severity

| Severity | Condition | Initial action |
|---|---|---|
| SEV-1 | Confirmed cross-tenant exposure, audit-chain failure, destructive financial mutation, secret compromise, or filing-artifact integrity loss | Stop affected writes and artifact release; page the incident commander immediately. |
| SEV-2 | Sustained API/database failure, missed deadline delivery, failed backup, or document generation unavailable with no safe workaround | Acknowledge, contain, and escalate within the configured target. |
| SEV-3 | Degraded performance, isolated retryable job failure, or bounded provider error with no integrity impact | Assign an owner and monitor against an explicit resolution time. |

When in doubt, classify at the higher severity until evidence narrows the impact.

## Triage and containment

1. Record incident ID, exact release commit/image digests, environment, detection UTC, provider
   event ID, structured-log correlation ID, alert route, and acknowledging operator.
2. Confirm that provider and log evidence contains no request body, query string, user email, client
   name, secret, raw identifier, or financial value. If it does, stop further export, restrict access,
   preserve the original securely, and open the privacy-incident path.
3. Determine affected tenants and records using tenant-safe internal IDs inside the controlled
   environment. Do not paste client data into chat, tickets, provider notes, or the exercise report.
4. For integrity, isolation, or authorization uncertainty, disable final artifact generation and
   external workflow advancement first. Direct CRO/ROS submission remains unsupported.
5. Preserve database, audit-chain, checkpoint, logs, provider event, and container/image evidence
   before restart or rollback. Record SHA-256 hashes for exported artifacts.
6. Choose the least destructive containment: pause a worker, revoke a session/key, disable a feature
   flag, isolate a service, or route to read-only/manual handoff. Do not delete evidence.

## Client-event privacy boundary

The browser may report only the fixed event codes declared in `frontend/src/lib/clientMonitoring.ts`.
It normalizes the current application URL to an allowlisted route shape and may attach only a
validated correlation ID. Exception messages, stack traces, response bodies, request bodies, form
values, financial amounts, client names/emails, credentials and arbitrary tags are prohibited.
The API repeats the allowlist and normalization before forwarding a generic
`ClientMonitoringException` to the provider; an authenticated caller cannot use the endpoint to
smuggle a free-form event or route value into logs or provider tags.

The production smoke sends synthetic sensitive markers through the controlled client-event input.
`monitoring-error-routing-report.json` must retain the normalized client event and its provider and
correlation identifiers. `structured-log-report.json` must match that correlation to the sanitized
client-event log line and prove the synthetic markers were absent. Any mismatch or marker leak is a
release blocker and a privacy-incident trigger; never copy the leaked content into a ticket or chat.

## Diagnosis, recovery, and verification

- Correlate the client-safe correlation ID across provider and structured logs.
- Verify tenant boundaries and the relevant company audit chain/checkpoint before and after recovery.
- Verify representative financial figures and exact artifact hashes when the incident touches data,
  migrations, document generation, or storage.
- Restore only from a checksum-verified retained backup and follow the production restore runbook.
- A rollback is allowed only when schema compatibility is proven; otherwise use the documented
  expand/contract or forward-fix path.
- Re-enable writes and artifact generation only after the incident commander records containment,
  recovery checks, residual risk, and release/accountant gates.

## Privacy and regulatory decision gate

The incident commander records whether personal data may have been accessed, changed, disclosed, or
made unavailable; the categories and approximate scope; containment time; evidence-preservation
decision; and the named privacy/legal decision owner. This repository does not decide whether an
external notification is legally required. Any notification decision must be made by the authorized
privacy/legal role using current law and retained evidence.

## Closure evidence

Retain the incident timeline, alert and acknowledgement timestamps, provider/log correlation,
release identity, impact and affected-scope classification, containment actions, recovery checks,
audit/checkpoint verification, artifact hashes, privacy decision, root cause, corrective actions,
owners, target dates, and incident-commander acceptance. Secrets and client data must remain outside
the retained report.

## Controlled exercise

Use synthetic data and the fixed monitoring smoke event. Measure provider delivery, acknowledgement
and escalation latency; test the unacknowledged escalation route; inspect event/log redaction; walk
through containment and recovery; then run `node scripts/verify-monitoring-incident-exercise.mjs`
against the retained JSON exercise record. An exercise is evidence for the tested environment and exact
candidate only; it is not a substitute for real monitoring-provider confirmation.
