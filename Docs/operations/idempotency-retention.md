# Tenant-scoped command idempotency

## Contract

The platform requires `Idempotency-Key` on company creation/onboarding, accounting-period creation,
CSV bank imports, CRO document-generation records, period/filing state commands, and recorded filing
deadline completion. A key is 8–128 ASCII letters, digits, dots, colons, underscores, or hyphens.
Keys are unique per tenant across all operations.

For one tenant:

- the same key, operation, and canonical request fingerprint returns the exact retained response,
  resource identity, HTTP status, expiry, and `Idempotency-Replayed: true`;
- the same key with another payload or operation returns `409 idempotency_key_conflict` and never
  runs the command;
- a command failure rolls back its reservation, domain rows, and audit rows together, so a retry is
  not poisoned; no deterministic rejection is retained by the current policy;
- concurrent retries serialize on a PostgreSQL transaction advisory lock before any reservation is
  inserted. The implementation never attempts to recover a uniqueness violation inside an already
  aborted transaction.

The response also exposes `Idempotency-Record-Id`, `Idempotency-Operation`, and
`Idempotency-Expires-At`. Browser automatic retries reuse one generated key; a new user command gets
a new key. Completed-key preflight lets a genuine replay reach its endpoint even when its original
period ETag is now stale or the period was subsequently locked. The endpoint still verifies the
operation and request fingerprint before returning anything.

## Persistence and retention

`idempotency_records` retains the tenant, key, operation, request SHA-256, actor, start/completion
timestamps, expiry, result resource identity, HTTP status, response JSON, and response SHA-256.
Reservation, mutation, explicit domain audit, and completion are one database transaction.

The default retention window is 30 days. `IdempotencyRetentionWorker` checks every six hours and
deletes at most 1,000 expired rows per batch. Configuration is validated at startup and is bounded
to 1–90 days. PostgreSQL rejects completed-row mutation and deletion before expiry; an expired row
can be removed and its key reused as a new command.

Migration `20260711010000_AddTenantScopedIdempotencyLedger` backfills completed rows from the legacy
`company_onboarding_requests` table, preserving its original request canonicalisation and exact
response. That legacy table intentionally remains immutable historical evidence but is no longer a
live idempotency authority or production write path.

## Release checks

Run:

```powershell
dotnet test backend/Accounts.Tests/Accounts.Tests.csproj -c Release --filter "FullyQualifiedName~Idempotency"
dotnet ef migrations has-pending-model-changes --project backend/Accounts.Api/Accounts.Api.csproj --startup-project backend/Accounts.Api/Accounts.Api.csproj --context AccountsDbContext
```

Set `ACCOUNTS_POSTGRES_TEST_CONNECTION` to run the PostgreSQL concurrency, rollback, trigger, expiry,
and legacy-upgrade replay tests. A qualified accountant must still review filing artifacts; the
idempotency ledger records workflow commands only and introduces no CRO/ROS submission client.
