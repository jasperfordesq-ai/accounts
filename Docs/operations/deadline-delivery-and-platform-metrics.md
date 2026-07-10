# Deadline delivery and platform metrics

This runbook covers the tenant-scoped deadline-reminder worker, durable delivery outbox, firm
at-risk queue, and restricted platform metrics. It does not submit anything to CRO or Revenue.
Reminder delivery is an internal work-queue notification only; filing remains an external,
qualified-accountant-controlled workflow.

## Production contract

Production startup fails unless `DeadlineDelivery:Enabled=true`, the provider endpoint is absolute
HTTPS, the file-backed provider token contains at least 32 characters, and platform metrics plus the
restricted snapshot are enabled. `compose.production.yml` exposes these as
`DEADLINE_DELIVERY_PROVIDER_ENDPOINT` and `DEADLINE_PROVIDER_TOKEN_FILE` and never embeds the token
in environment-rendered YAML.

The provider receives a fixed payload containing only an event code, pseudonymous tenant scope,
enum deadline/reminder kinds, due date, fixed work-queue path and SHA-256 idempotency key. It never
receives a company/client name, email address, tax/CRO reference, free-form note, exception message,
user identifier, accounting value or source document. Treat any proposed payload expansion as a
privacy and security review requiring tests before release.

## Delivery lifecycle

`DeadlineReminderWorker` enumerates tenant IDs through the restricted database function, then uses a
fresh dependency-injection/database scope and signed RLS context for each tenant. For each scheduled
slot, a serializable planning transaction creates one `PlatformJobRun`, evaluates due-soon, overdue,
filed and corrected deadlines, and appends deduplicated `DeadlineReminderOutbox` rows. Filed rows are
cancelled; changed due-date/fingerprint rows supersede stale intent and create a corrected reminder.

Delivery uses a committed compare-and-set lease before contacting the provider, so no database
transaction is held across network I/O. Provider success records only a bounded safe reference.
Failure records a fixed code, schedules exponential backoff and routes a fixed non-PII operator
event on the configured attempt. Exhausted rows remain in `RetryScheduled` and visible for an
authorized, recent-MFA manual retry. Stale `Delivering` leases are recovered on the next run.

Database triggers make outbox ownership/dedupe identity immutable, constrain valid one-way state
transitions, and prevent deletion. Job identity is immutable and a running job may reach a terminal
state only once. Never edit these rows manually; correct a filing deadline or use the controlled
retry endpoint.

## Operator workflow

- Owner, Accountant and Reviewer: inspect `GET /api/operations/deadline-risk`.
- Owner with recent TOTP: request a controlled run with
  `POST /api/operations/deadline-reminders/run`.
- Owner, Accountant or Reviewer with recent TOTP: retry a visible retryable row with
  `POST /api/operations/deadline-reminders/{outboxId}/retry`.
- Owner or Reviewer: inspect the tenant-safe snapshot at `GET /api/system/platform-metrics`.

Manual runs and retries write durable domain audit events. Do not use manual retry to hide a provider
incident: preserve the first-failure alert/correlation, confirm provider health, then retry. Escalate
missed delivery under the monitoring incident-response runbook.

## Metrics and alerts

The in-process meter records request count/latency by normalized route template, method and status
class; scheduled job outcome/latency; document kind/outcome/latency; reminder kind/outcome/latency;
and database pool checkout/active-connection measurements. Snapshot dimensions are restricted to
the checked-in allowlist and contain no tenant, company, period, user, email, recipient, route value,
file name or client data.

The snapshot also combines durable tenant-scoped reminder backlog/failures, job failures and latest
successful backup evidence. Alert codes cover request latency/error rate, scheduled-job failures,
database-pool pressure, document-generation latency, stale/missing backup evidence and reminder
failure/backlog thresholds. Configure the external monitoring provider to alert on the same targets;
the snapshot is operational evidence, not a replacement for provider confirmation.

## Verification and recovery

Release verification must include time-controlled due-soon, overdue, corrected, filed,
duplicate-suppression, delivery-failure, operator-alert, retry, stale-lease and tenant-isolation
tests. Real PostgreSQL coverage must prove concurrent planners/deliverers create one logical intent,
database triggers reject mutation/deletion, and the application role cannot see another tenant.

If the scheduler is unavailable, restore service, inspect durable running jobs and stale leases, then
allow the next slot to recover them. If the provider is unavailable, preserve the at-risk queue and
alert evidence; do not mark reminders delivered manually. Database recovery follows the encrypted
backup/restore runbook and must retain the outbox/job rows and their evidence hashes.
