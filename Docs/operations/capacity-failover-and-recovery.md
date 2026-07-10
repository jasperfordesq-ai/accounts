# Capacity, failover, and recovery profile

This document defines the bounded machine profile and the larger operational evidence still required
before production acceptance. It does not claim that CI health traffic represents accountant load or
that an ephemeral database restore is a production disaster-recovery exercise.

## Automated candidate profile

The production-stack job runs `scripts/run-capacity-profile.mjs` through the same HTTPS ingress used
by smoke tests. The candidate must complete 120 alternating `/health` and `/health/ready`
requests at concurrency 12 with zero errors, p95 latency no greater than 1,000 ms, and throughput of
at least 10 requests/second. The report retains aggregate and per-endpoint counts and latency only;
it sends no request body or authentication and retains no response body, tenant, company, user, or
client identifier. Threshold misses write a failed report and fail CI.

The bounded profile complements, but does not replace, the existing behavioral gates:

- fixed-window rate-limit policy and proxy-aware partitioning;
- 105-company deadline batching and server pagination for 125+ transactions;
- atomic concurrent financial/audit writes and PostgreSQL advisory locks;
- bounded working-paper/document generation with duration and outcome metrics;
- database-pool active/checkout metrics and fixed-code alerts;
- migration rollback, encrypted backup restoration, full-row fingerprints, audit-chain checks, and
  enforced RPO/RTO target failures.

The same ephemeral production-stack job runs `scripts/test-production-failover.ps1`. It stops the
API container, requires the ingress readiness route to fail closed, restarts it within the candidate
target, then repeats the exercise for PostgreSQL. The report retains only phase, status code and
timing aggregates, is bound to the commit/run identity, and fails if either outage is not detected,
either service does not recover, or cleanup cannot restore the stack. This catches cached-readiness,
restart and dependency-recovery regressions; it deliberately records that it is not a production
host/off-host recovery drill.

Both `capacity-profile-report.json` and `production-failover-report.json` are mandatory inputs to
the CI machine evidence pack and the final release artifact pack. The final pack verifier repeats
the candidate commit/run, canonical HTTPS smoke origin, threshold, endpoint/phase, ephemeral
Compose scope, privacy, and scope-boundary checks and records both files in its SHA-256/byte-size
manifest; a stale, broadened, incomplete, or missing report blocks release.
Neither exercise sends accounting data or performs a filing action, and the same final pack must
independently retain the passing no-direct CRO/ROS submission control for that candidate.

## Published operational targets

The release target is p95 API latency at or below 1,000 ms for routine workbench reads, less than 2%
server-error rate, document-generation p95 at or below 10 seconds for supported seeded scenarios,
backup age below 26 hours, recovery point no older than 24 hours, and recovery within four hours.
Configured alert thresholds in `PlatformMetrics` must remain equal to or stricter than those targets.

## Evidence that remains external

P2-OPS-010 remains open until an approved production-like environment executes a representative
large-practice profile including authenticated dashboard reads, high-volume transaction paging,
concurrent audited financial writes, and all supported document types. The exercise must also kill
an application host and interrupt the database, prove writes are not corrupted or silently lost,
restore from the approved off-host encrypted copy, measure RPO/RTO, and retain named operator review.
CI health load, synthetic provider failures, and ephemeral Compose restores must not be presented as
that evidence.
