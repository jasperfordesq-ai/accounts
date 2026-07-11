# Production Operations Runbook

This runbook covers the minimum production operating procedures for the Irish statutory accounts platform.

Scheduled reminder delivery, the firm at-risk queue, tenant-safe platform metrics and their operator
procedures are defined in [deadline delivery and platform metrics](deadline-delivery-and-platform-metrics.md).

## HTTPS Ingress Contract

Production traffic must enter through an HTTPS reverse proxy that performs TLS termination before forwarding requests to the Docker Compose frontend. The production compose file publishes the frontend only on `127.0.0.1:${FRONTEND_PORT:-3000}:3000`; do not expose the frontend or API containers directly to the internet.

Use `deploy/caddy/Caddyfile.example` as the checked-in ingress contract. It listens on the public hostname, obtains/serves certificates through Caddy, proxies to `ACCOUNTS_FRONTEND_UPSTREAM` (default `127.0.0.1:3000`), and overwrites X-Forwarded-For, overwrites X-Forwarded-Host, and overwrites X-Forwarded-Proto before the request reaches Next.js. Set `TRUST_PROXY_HEADERS=true` only when the ingress is deployed on the same trusted host or private network and untrusted clients cannot bypass it. The API also validates this flag at startup before it trusts forwarded client IPs for rate limiting. Leave `TRUST_PROXY_HEADERS=false` or unset for any topology where arbitrary clients can connect to the frontend port.

Minimum ingress environment:

```powershell
$env:ACCOUNTS_HOSTNAME = "accounts.example.ie"
$env:FRONTEND_PORT = "3000"
$env:ACCOUNTS_FRONTEND_UPSTREAM = "127.0.0.1:$env:FRONTEND_PORT"
```

Validate the Caddy configuration before a release:

```powershell
caddy validate --config .\deploy\caddy\Caddyfile.example
```

CI also runs the production smoke test through this Caddyfile instead of calling the frontend port directly. The workflow maps `accounts-smoke.local` to `127.0.0.1`, creates the Caddy container on Docker's standard bridge so only runner-loopback ports are published, then attaches it to the internal Compose frontend network before startup with `ACCOUNTS_FRONTEND_UPSTREAM=frontend:3000`. The probe explicitly bypasses proxies and resolves the smoke hostname to loopback; the workflow enables `ACCOUNTS_CADDY_GLOBAL_OPTIONS=local_certs`, trusts the generated local CA for curl, PowerShell and the Node capacity probe, and runs `smoke-production.ps1` against `https://accounts-smoke.local`. This avoids host networking while preserving the private upstream hop and the same ingress contract. Do not set `local_certs` in production unless you intentionally operate with an internally trusted private CA; the normal production path should use the public hostname and Caddy's public certificate automation.

The ingress emits HSTS for the public HTTPS hostname. Keep `ACCOUNTS_ALLOWED_ORIGIN` aligned to the public HTTPS origin used by the reverse proxy, for example `https://accounts.example.ie`. Leave the frontend `ENABLE_HSTS=false` when HSTS is owned by the ingress; set `ENABLE_HSTS=true` only when TLS terminates directly at the Next.js frontend and the domain is ready for the `includeSubDomains; preload` commitment.

The API container is not published to the host, but the frontend reaches it through Docker DNS as `http://api:8080`. `compose.production.yml` appends the internal `api` host to ASP.NET `AllowedHosts` and uses `Host: api` for the API healthcheck; keep the API unexposed and route public traffic through the HTTPS frontend ingress only.

## Frontend API Key

Production requires a shared frontend-to-API key. Store the raw key in a Docker secrets file and point `ACCOUNTS_API_KEY_FILE` at that file for the frontend service; store the lowercase SHA-256 hex digest as `ACCOUNTS_API_KEY_HASH` for the API and migration job.

Generate the pair once, then move both values into the release secret store:

```powershell
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$bytes = New-Object byte[] 32
$rng.GetBytes($bytes)
$rng.Dispose()
$env:ACCOUNTS_API_KEY = [Convert]::ToBase64String($bytes)
$sha = [System.Security.Cryptography.SHA256]::Create()
$env:ACCOUNTS_API_KEY_HASH = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($env:ACCOUNTS_API_KEY))).Replace("-", "").ToLowerInvariant()
$sha.Dispose()
```

The frontend `/health/ready` check validates this key against the protected API before reporting ready, so a mismatched key/hash pair should fail the release smoke test before traffic is promoted.

Backend auth endpoints are proxy-only in production. Do not expose `/api/auth/login` directly without the frontend service key; users should sign in through the HTTPS frontend so the Next.js API proxy reads the key from `ACCOUNTS_API_KEY_FILE`, attaches it to upstream API requests, preserves the browser session and CSRF cookies, and strips any client-supplied API-key headers before forwarding to the private API container.

## Required Production Environment

Set every required variable before running `docker compose -f compose.production.yml config --quiet`, the migration job, or the production stack. CI sample values are not production secrets.

`deploy/production-images.env.example` documents the only accepted image-reference shape. Its
all-zero digests are deliberately non-deployable; replace them only with the two exact references
from the passing release-candidate supply-chain report, never with tags.

Sensitive values are mounted as Docker secrets under `/run/secrets`. Put each raw secret in a file outside the repository, set the matching `*_FILE` variable to that path, and keep those files out of terminal history, logs, and backups that are not approved for secret material.

The application containers run as non-root users and, in non-swarm `docker compose`, the in-container secret keeps the host file's permissions (the `mode:` field is swarm-only). Each mounted secret file must therefore be readable inside its protected parent directory: keep the secrets directory `0700` and the mounted files `0444`. The PostgreSQL container starts its official entrypoint as root only long enough to copy the mounted server key into a bounded in-memory filesystem with mode `0600` and ownership `postgres:postgres`; the official entrypoint then drops privileges before starting PostgreSQL. A `0600` source file owned only by the host user makes the containers fail at startup with `Permission denied` on `/run/secrets/...`.

Do not run plain `docker compose -f compose.production.yml config` with production secrets. The non-quiet render can still print secret file paths into terminal scrollback or CI logs.

| Variable | Purpose |
| --- | --- |
| `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD_FILE` | PostgreSQL database, migration/administration user, and Docker secret file path for that privileged password. The API must never receive this credential. |
| `POSTGRES_APPLICATION_PASSWORD_FILE` | Independent password for the fixed `accounts_api` login. The one-shot `database-role-provision` service creates or rotates that login with `NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE`; the API receives this password only through its application connection string. |
| `POSTGRES_SERVER_CERTIFICATE_FILE`, `POSTGRES_SERVER_KEY_FILE`, `POSTGRES_CA_CERTIFICATE_FILE` | Docker secret paths for the PostgreSQL server certificate (SAN must include `DNS:db`), its private key, and the issuing CA certificate. Keep the CA private key offline; it is never mounted. |
| `ACCOUNTS_API_IMAGE`, `ACCOUNTS_FRONTEND_IMAGE` | Exact CI-promoted lowercase GHCR digest references for the backend/migration image and frontend image, in the form `ghcr.io/owner/image@sha256:<64 lowercase hex>`. Tags are not accepted for production. |
| `ACCOUNTS_MIGRATION_CONNECTION_STRING_FILE` | Docker secret file containing the privileged migration connection string using `POSTGRES_USER`. It must point to `Host=db;Port=5432` and include `SSL Mode=VerifyFull;Root Certificate=/run/secrets/postgres_ca_certificate;Trust Server Certificate=false`. Mount it only on `migrate`. |
| `ACCOUNTS_APPLICATION_CONNECTION_STRING_FILE` | Docker secret file containing the least-privileged API connection string using the fixed `accounts_api` login and the password from `POSTGRES_APPLICATION_PASSWORD_FILE`. It has the same verified-TLS requirements and is mounted only on `api`. |
| `AUTH_SESSION_SIGNING_KEY_FILE` | Docker secret file path containing a Base64 or Base64Url secret of at least 32 bytes for signed browser sessions. |
| `AUDIT_INTEGRITY_ACTIVE_KEY_ID`, `AUDIT_INTEGRITY_SIGNING_KEY_FILE` | Audit hash-chain signing key id and Docker secret file path containing the Base64 signing secret. |
| `DATABASE_TENANT_CONTEXT_KEY_FILE` | Independent Base64 secret of at least 32 bytes used to sign the request-scoped tenant context enforced by PostgreSQL forced RLS. Do not reuse the session, audit, or identity keys. |
| `IDENTITY_HMAC_KEY_FILE` | Independent Base64 secret of at least 32 bytes for identity recovery-code and action-token HMAC operations. |
| `MFA_ENCRYPTION_ACTIVE_KEY_ID`, `MFA_ENCRYPTION_KEY_FILE` | Versioned active key id and independent Base64 key (at least 32 bytes) for MFA-secret envelope encryption. Retain prior configured keys during a controlled rotation until stored secrets have been lazily rewrapped. |
| `DEADLINE_DELIVERY_PROVIDER_ENDPOINT`, `DEADLINE_PROVIDER_TOKEN_FILE` | HTTPS endpoint and Docker secret token for the approved deadline-reminder provider. The durable outbox remains authoritative; never put client names, emails, or accounting values in configuration or metric labels. |
| `BACKUP_ENCRYPTION_CERTIFICATE_FILE` | Public X.509 recipient certificate used by OpenSSL CMS to encrypt every retained backup with AES-256. The backup host needs only this public certificate. |
| `BACKUP_DECRYPTION_CERTIFICATE_FILE`, `BACKUP_DECRYPTION_PRIVATE_KEY_FILE` | Recovery-side certificate/private-key paths. Keep the private key in the approved key-management/recovery service, separate from the application host and backup store; expose it only for an authorized drill or restore. |
| `ACCOUNTS_ALLOWED_HOSTS` | Public API hostnames; Compose appends internal `api` for the private frontend-to-API hop. |
| `ACCOUNTS_ALLOWED_ORIGIN` | Public HTTPS origin used by the browser, for example `https://accounts.example.ie`. |
| `ACCOUNTS_API_KEY_FILE`, `ACCOUNTS_API_KEY_HASH` | Docker secret file path for the frontend service API key and matching lowercase SHA-256 hash. |
| `TRUST_PROXY_HEADERS` | Set to `true` only when the trusted ingress overwrites forwarded headers and clients cannot bypass it; required by the API when `RateLimits__TrustForwardedFor=true`. |
| `MONITORING_ERROR_TRACKING_DSN` | Required HTTPS DSN for the production error-tracking provider. Startup fails outside development when this is missing or not HTTPS. |
| `MONITORING_ERROR_TRACKING_PROVIDER` | Optional; defaults to `Sentry-compatible`. Use a short provider label for the readiness report and operational records. |
| `MONITORING_TRACES_SAMPLE_RATE` | Optional; defaults to `0`. Must be between `0` and `1` when set. |
| `MONITORING_ERROR_SMOKE_ENABLED` | Optional; defaults to `false`. Set to `true` only for an operator-controlled release smoke run that emits a fixed non-PII event through `/api/system/monitoring/error-smoke`, then turn it back off if the endpoint should not remain available between releases. |
| `MONITORING_LOG_RETENTION_DAYS`, `MONITORING_ERROR_RETENTION_DAYS` | Optional; each defaults to `90` and must be between 30 and 3,650 days. Configure the provider/index retention to the same values and retain the provider policy evidence. |
| `MONITORING_ACKNOWLEDGEMENT_MINUTES`, `MONITORING_ESCALATION_MINUTES` | Optional; default to 15 and 30. Escalation must be later than acknowledgement and no later than 240 minutes. Alert policy must use the same targets. |
| `MONITORING_ON_CALL_OWNER`, `MONITORING_ALERT_ROUTE` | Required accountable owner and configured notification-route identifier. Use role/route labels only; never put email addresses, phone numbers, webhook URLs, tokens, or client data in these values. |
| `BOOTSTRAP_TENANT_NAME`, `BOOTSTRAP_TENANT_SLUG` | Initial firm tenant created by the controlled migration/bootstrap job. |
| `BOOTSTRAP_OWNER_EMAIL`, `BOOTSTRAP_OWNER_DISPLAY_NAME`, `BOOTSTRAP_OWNER_PASSWORD_FILE` | Initial owner account and Docker secret file path for the initial password; `BOOTSTRAP_OWNER_PASSWORD_FILE` must contain a password of at least 20 characters and include upper case, lower case, number, and symbol characters. Rotate the password at first login. |
| `BOOTSTRAP_OWNER_MUST_CHANGE_PASSWORD` | Optional; defaults to `true`. When `true` the bootstrap owner must change the password at first sign-in before any other API access is allowed. |

Generate independent session, audit, tenant-context, identity-HMAC, and MFA-encryption secrets. Store each output in its own approved secret file; never reuse one value for another purpose:

```powershell
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$bytes = New-Object byte[] 64
$rng.GetBytes($bytes)
$env:AUTH_SESSION_SIGNING_KEY = [Convert]::ToBase64String($bytes)
$rng.GetBytes($bytes)
$env:AUDIT_INTEGRITY_SIGNING_KEY = [Convert]::ToBase64String($bytes)
$rng.GetBytes($bytes)
$env:DATABASE_TENANT_CONTEXT_KEY = [Convert]::ToBase64String($bytes)
$rng.GetBytes($bytes)
$env:IDENTITY_HMAC_KEY = [Convert]::ToBase64String($bytes)
$rng.GetBytes($bytes)
$env:MFA_ENCRYPTION_KEY = [Convert]::ToBase64String($bytes)
$rng.Dispose()
```

The startup order is deliberate: `db` becomes healthy over verified TLS, `database-role-provision`
creates or rotates the non-bypass API login, `migrate` applies schema changes and the frozen forced-RLS
policy inventory with the privileged connection, and only then does `api` start with its separate
least-privileged connection. The API startup verifier fails closed if the login can bypass RLS, inherits
the administration role, can read the tenant-context signing key, can alter protected tables, or if the
exact forced-policy/function inventory differs from the release migration.

## PostgreSQL Transport TLS

Production startup fails closed unless the API and migration connection string uses `SSL Mode=VerifyFull`, names a root certificate, and leaves `Trust Server Certificate=false`. The database also performs its own health check through the Compose DNS name `db` using `sslmode=verify-full`; an expired, untrusted, or hostname-mismatched certificate therefore prevents the dependent services from starting.

Issue a dedicated server certificate from the deployment database CA with all of the following properties:

- an extended key usage of `serverAuth`;
- a subject alternative name containing `DNS:db` (the Compose service identity);
- a validity period and rotation owner defined by the operator's certificate policy;
- a private key generated and retained outside the repository;
- a CA private key stored offline or in the approved key-management service and never copied to the application host.

Point the three certificate `*_FILE` variables at the public server certificate, server private key, and public CA certificate. Keep their parent directory mode `0700`; Compose copies the server key into an 8 MiB `noexec,nosuid,nodev` tmpfs and changes the runtime copy to mode `0600`. Record only certificate fingerprints, serial numbers, validity dates, issuer, key owner, and rotation ticket in release evidence—never private-key material.

Before expiry, issue a replacement certificate with the same `DNS:db` SAN, update the three files atomically, and recreate `db`, `migrate`, and `api` during the controlled maintenance window. Roll back by restoring the previously approved certificate file set while it remains valid. After rotation, prove hostname and CA verification from the running database container without putting the password on the command line:

```powershell
docker compose -f compose.production.yml exec -T db sh -ec 'PGPASSWORD="$(cat /run/secrets/postgres_password)" psql "host=db port=5432 dbname=$POSTGRES_DB user=$POSTGRES_USER sslmode=verify-full sslrootcert=/run/secrets/postgres_ca_certificate" -v ON_ERROR_STOP=1 -Atqc "SELECT ssl::text || ''|'' || version || ''|'' || cipher FROM pg_stat_ssl WHERE pid = pg_backend_pid();"'
```

The retained output must begin with `true|`. Also retain the certificate SHA-256 fingerprint and `notBefore`/`notAfter` values, the Compose safety report's `databaseTransport` section, the release/run identity, operator, and rotation/recovery result. Database transport TLS does not by itself prove volume or backup encryption at rest; retain separate storage-provider and key-ownership evidence for that control.

CI performs the same proof with `scripts/verify-postgres-tls.ps1` and uploads
`postgres-tls-runtime/postgres-tls-report.json`. The CI machine-evidence and final release-artifact
pack verifiers require that report, its exact commit/run identity, `VerifyFull` policy, live TLS
session/cipher evidence, rejected hostname mismatch, and certificate hashes/validity fields.

## Backup Policy

- RPO: 24 hours for routine operations. Take an extra backup before migrations, releases, and bulk imports.
- RTO: 4 hours for database restore to a prepared host with Docker and the application images available.
- Store encrypted PostgreSQL custom-format backup envelopes outside the application host after each backup.
- Retain daily backups for 14 days, weekly backups for 8 weeks, and monthly backups for 12 months unless client policy requires more.
- The script must encrypt with the recovery public certificate before off-host transfer and delete its plaintext staging dump. Also enable storage-provider encryption at rest as a separate layer.

Assign the recovery private key to the named backup/recovery owner, not the application runtime.
Rotate by issuing a new recipient certificate, switching `BACKUP_ENCRYPTION_CERTIFICATE_FILE`, and
running an encrypted backup/restore drill before retiring the old certificate. Keep each old private
key available only in the recovery key store until every backup encrypted to it has expired under
retention policy; then record destruction/revocation. The manifest certificate SHA-256 identifies
which recovery key is required without exposing that key. A rotation is incomplete until both the
new backup and the oldest still-retained backup have been restored successfully.

## Backup Command

Set the production database environment variables used by `compose.production.yml`, then run:

```powershell
.\scripts\backup-postgres.ps1 -OutputDirectory D:\accounts-backups
```

The script uses `pg_dump --format=custom`, immediately wraps the dump with OpenSSL CMS/AES-256-CBC using `BACKUP_ENCRYPTION_CERTIFICATE_FILE`, removes the plaintext staging dump, and writes only `.dump.cms`, `.dump.cms.sha256`, and `.dump.cms.manifest.json` files. The manifest binds environment/release identity, byte size, encrypted-file SHA-256, public-certificate SHA-256, algorithm, and plaintext-removal state. `-AllowUnencryptedBackupForLocalDryRun` is restricted to non-production local data and must never be used by scheduled or release backup jobs.

`-OutputDirectory` is mandatory and must point outside the repository. The script refuses repository-local output paths by default so production dumps are not left under the application checkout. `-AllowRepositoryOutputForLocalDryRun` exists only for local dry runs with non-production data.

## Restore Drill

Run a restore drill after every material schema change and at least monthly:

```powershell
.\scripts\verify-postgres-backup.ps1 -BackupPath D:\accounts-backups\accounts-20260607-010000.dump.cms
```

The drill decrypts into a bounded operating-system temporary directory, restores into `accounts_restore_verify`, deletes the temporary plaintext, and leaves the source production database untouched. It compares schema migration identity; counts across users, companies, periods, banking/import, adjustments, filing packages/history, audit logs and checkpoints; exact full-row fingerprints for financial, filing and audit data; representative monetary/artifact-byte totals; and malformed audit-hash/checkpoint counts. The report measures backup age (RPO) and end-to-end restore duration (RTO).

In CI, the production smoke job creates an ephemeral production-shape backup, verifies
the restore, writes `restore-drill-report.json`, and uploads the
`postgres-backup-restore-drill` artifact containing only the encrypted `.dump.cms`, checksum,
manifest, and restore evidence report. The decryption private key remains in the ephemeral secrets
directory and is never uploaded. Treat that artifact as the release proof for the CI candidate;
for real production data, retain the same fields in the operations evidence store rather
than uploading client data to CI.

## Migration Compatibility Gate

The supported upgrade floor is `20260621123340_AddCroSignatories`: it is the newest migration
committed to the integration branch before the active production-readiness hardening migration
series. Supporting an older database requires a separately planned, tested data-conversion release;
do not infer support merely because EF can enumerate an older migration. The toolchain is deliberately
split and locked: `global.json` selects .NET SDK `10.0.103`, while `.config/dotnet-tools.json` and the
API design/runtime packages pin EF `10.0.9`; the Npgsql EF provider is `10.0.2`. Restore repository
tools before running the exact drift check:

```powershell
dotnet tool restore
dotnet build backend/Accounts.Api/Accounts.Api.csproj --configuration Release --no-restore
dotnet ef migrations has-pending-model-changes --project backend/Accounts.Api/Accounts.Api.csproj --startup-project backend/Accounts.Api/Accounts.Api.csproj --configuration Release --no-build
$env:ACCOUNTS_POSTGRES_TEST_CONNECTION = "Host=localhost;Port=5432;Database=accounts_test;Username=accounts_test;Password=<secret>"
$env:ACCOUNTS_MIGRATION_EVIDENCE_PATH = "D:\accounts-smoke\migration-upgrade-report.json"
$env:ACCOUNTS_GITHUB_ACTIONS_RUN_URL = "<ci-run-url>"
dotnet test backend/Accounts.Tests/Accounts.Tests.csproj --configuration Release --filter FullyQualifiedName~MigrationUpgradePostgresTests
.\scripts\verify-migration-upgrade-evidence.ps1 -ReportPath D:\accounts-smoke\migration-upgrade-report.json -EvidencePath D:\accounts-smoke\migration-upgrade-verification-report.json -CommitSha <release-commit-sha> -GitHubActionsRunUrl <ci-run-url>
```

The PostgreSQL 16.4 test migrates a fresh schema, restores the supported previous-release schema,
seeds representative users, company/period records, bank/import/opening-balance/journal figures,
CRO and Revenue filing snapshots, and a valid audit chain/checkpoint, then migrates to the current
target. Every required group must retain the same positive row count and canonical SHA-256 before
and after. It also injects a `P0001` failure after partial DDL and data mutation inside a migration
transaction. Passing evidence requires transactional rollback: no marker table, no changed data,
no migration-history residue, and no transaction-suppressed EF SQL operation.

CI uploads `migration-upgrade-report.json`, `migration-upgrade-verification-report.json`, the gate
configuration, and pinned tool manifest as `postgres-migration-upgrade-gate`. Both machine-evidence
pack verifiers require those reports for the exact commit/run and require their declared encrypted
recovery companion, `restore-drill-report.json`, in the same pack. This gate complements rather than
replaces the pre-migration encrypted backup and restore drill.

If a production migration fails, keep application startup stopped. EF/PostgreSQL should roll back the
failed migration transaction; compare `__EFMigrationsHistory`, schema objects, and the representative
data fingerprints with the retained pre-migration evidence. Do not retry blindly. Correct the migration
in a new candidate and rerun the full gate. If a future migration genuinely cannot be transactional,
it must use a documented expand/contract release with an idempotent recovery script, retained phase
checkpoints, a rehearsed backward-compatible application window, and qualified operations approval;
the current gate intentionally rejects transaction-suppressed SQL.

## Production Restore

Restores are destructive when `-Clean` is used. Stop application traffic first and set the explicit confirmation variable. The restore script verifies the adjacent checksum and encrypted manifest, verifies the recovery certificate identity, decrypts only into the operating-system temporary directory, and erases that plaintext in `finally`. Use `-AllowUnverifiedBackupRestore` only for a documented break-glass incident where the checksum file is unavailable but the backup has been independently verified. Plaintext `.dump` restores are rejected unless `-AllowUnencryptedBackupRestore` is explicitly supplied for a documented local/break-glass case. The restore runs `pg_restore` in a single transaction and is configured to exit on the first restore error so a failed restore does not silently leave partially applied objects behind.

```powershell
$env:RESTORE_CONFIRM = "accounts"
.\scripts\restore-postgres.ps1 -BackupPath D:\accounts-backups\accounts-20260607-010000.dump.cms -TargetDatabase accounts -Clean
```

After restore:

1. Run the migration job with `docker compose -f compose.production.yml run --rm migrate`.
2. Check `GET /health/ready`.
3. Verify company counts, latest audit-log entries, and a sample statutory accounts package.
4. Record the restore drill or incident outcome, sha256, operator, start time, finish time, and any exceptions.

## Build Gate

CI is the authoritative build gate for releases. Before promoting an image, confirm the backend tests and build, frontend audit, type-check, lint, readiness regression, production monitoring config gate, and Next production build have all passed. Production deployment must run the exact CI-promoted GHCR digest references and must not rebuild from the release checkout. Copy `ACCOUNTS_API_IMAGE` and `ACCOUNTS_FRONTEND_IMAGE` from the passing `container-supply-chain-report.json`; never substitute a tag. The migration job and API service intentionally use the same `ACCOUNTS_API_IMAGE` digest.

Do not deploy production by rebuilding from the checkout with `docker compose up --build`. Rebuilding on the production host can run code that differs from the CI-promoted immutable image and makes rollback, migration/app parity, and incident reconstruction weaker.

Retain the CI `production-safety-config` artifact for each release candidate. It is produced by:

```powershell
.\scripts\verify-production-compose-images.ps1 -EvidencePath D:\accounts-smoke\production-safety-report.json
```

The report proves the production compose profile uses digest-pinned CI-promoted images, the migration job and API share the exact backend digest, the production smoke workflow contains no secondary Docker build and pulls both exact digests, the migration job runs exactly `--migrate-only`, the API waits for that job to complete, normal API startup has `DatabaseStartup__AutoMigrateOnStartup=false`, demo seeding is disabled for both migration and API services, demo-seed override flags are absent, and the bootstrap owner initial password is available only to the migration job.

Retain the CI `container-supply-chain` artifact for every release candidate. A release-eligible trusted `main` push must contain:

- `container-supply-chain-report.json` and `container-supply-chain-verification-report.json`, both passing for the exact commit and GitHub Actions run;
- the backend and frontend Trivy JSON reports, each scanning the exact promoted digest with zero HIGH or CRITICAL findings;
- backend and frontend SPDX JSON SBOMs; and
- backend and frontend GitHub provenance bundles and attestation URLs.

The workflow builds each application image once. Only a trusted push to `main` may log in to GHCR, push the candidate, or create provenance. Pull requests and forks use local verification images without registry credentials; their supply-chain report is explicitly `blocked`, is not release eligible, and cannot enter the CI machine evidence pack. A missing digest, mutable tag, rebuild, digest mismatch, missing/failed scan, missing SBOM, missing provenance, or verification-only report blocks release.

Retain the no-direct filing submission control report for each release candidate:

```powershell
.\scripts\verify-no-direct-filing-submission.ps1 -EvidencePath D:\accounts-smoke\no-direct-filing-submission-report.json -CommitSha <release-commit-sha> -GitHubActionsRunUrl <ci-run-url>
```

The report proves final CRO and ROS operations remain recorded workflow states only:
the API exposes status, payment, download and internal iXBRL validation endpoints, the
legacy generated marker is blocked with `410 Gone`, and no outbound CRO/ROS submission client or submit route is wired into the release.
The report also records the release candidate commit SHA and GitHub Actions run URL;
the CI machine evidence pack and release artifact pack reject stale no-direct evidence
whose candidate identity does not match the pack being verified.
CI runs the same verifier in the production stack smoke job and uploads the
`no-direct-filing-submission-control` artifact for each candidate.

The smoke script also captures the live production readiness report from the
authenticated production stack and retains it as `production-readiness-report.json`.
CI then runs `scripts\verify-production-readiness-report.ps1` against the captured
JSON and retains `production-readiness-verification-report.json`. CI uploads both
files as the `production-readiness-report` artifact so the release pack contains
the exact scorecard, source-law snapshot, golden corpus, release blockers,
verification manifest, visual QA contract, and machine-checkable coverage report
observed in the candidate stack.

Retain the CI `dependency-audit-release` artifact as the dependency evidence packet. It contains `npm-audit.json` and `dependency-audit-report.json`; the latter records package-lock and package.json hashes, npm audit counts, the backend NuGet audit policy (`NU1901`-`NU1904` as errors), and workflow action-hygiene wiring:

```powershell
.\scripts\write-dependency-evidence.ps1 -NpmAuditJsonPath D:\accounts-smoke\npm-audit.json -EvidencePath D:\accounts-smoke\dependency-audit-report.json
```

The remaining manual release evidence should be recorded with the checked-in templates:

- `Docs/release-evidence/visual-qa-signoff-template.md`
- `Docs/release-evidence/source-law-review-template.md`
- `Docs/release-evidence/external-ros-ixbrl-validation-template.md`
- `Docs/release-evidence/qualified-accountant-acceptance-template.md`
- `Docs/release-evidence/manual-handoff-acceptance-template.md`
- `Docs/release-evidence/monitoring-provider-confirmation-template.md`

After the templates are completed for a release candidate, run the release evidence
verifier and retain its JSON report with the release evidence pack:

```powershell
.\scripts\verify-release-evidence.ps1 -EvidenceDirectory .\Docs\release-evidence -ReportPath D:\accounts-smoke\release-evidence-report.json
```

Then follow `Docs/operations/durable-release-evidence.md`: create all seven
candidate-bound detached-signature sidecars (source-law review requires separate
source-law reviewer and qualified-accountant signatures), verify the independently
pinned certificate/name/capacity/credential trust policy with
`scripts\verify-durable-release-evidence.ps1`, verify the exact no-extra/no-missing
publication manifest with `scripts\verify-durable-release-publication-inventory.ps1`,
and retain both reports outside the immutable evidence input directory. The candidate
completion time is the validated canonical Actions run's `updated_at`; it is read from
GitHub and passed as `CandidateRunCompletedAtUtc`, never chosen by a reviewer.

Completed evidence belongs only in the separately controlled private
`jasperfordesq-ai/accounts-release-evidence` repository. Its protected caller invokes
this public repository's reusable workflow pinned to the exact candidate SHA. The
public workflow intentionally has no `workflow_dispatch`; the private caller must be
the canonical manual workflow on its current signed/protected `main` head and GitHub
OIDC must bind both caller and called-workflow commits before private data is read. The
private repository, its protected-branch-only `durable-release-evidence` environment
with independent reviewer, no self-review and no administrator bypass, read-only
release-governance GitHub App installed only on the application/evidence repositories,
real trust anchors and credentials, completed evidence, and immutable
publication are all currently unprovisioned or unproven. A GitHub plan that cannot
enforce those private-environment rules cannot satisfy this publication control. Treat
the five raw iXBRL files as untrusted active-document evidence and inspect them only in
an offline sandboxed viewer, even after structural/active-content checks pass. These
engineering controls do not create or replace named human acceptance. Real use stays
blocked until the private immutable release is actually published, survives normal
Actions retention, and its release attestation, exact downloaded asset, explicit
provenance and seven signatures are independently verified.

After collecting the release candidate artifact reports into one evidence directory,
run the artifact-pack verifier and retain its JSON report:

```powershell
.\scripts\verify-release-artifact-pack.ps1 -EvidenceDirectory D:\accounts-smoke -ReportPath D:\accounts-smoke\release-artifact-pack-report.json -CommitSha <release-commit-sha> -GitHubActionsRunUrl <ci-run-url>
```

The artifact pack must include `dependency-audit-report.json`,
`production-safety-report.json`, `container-supply-chain-report.json`,
`container-supply-chain-verification-report.json`, the retained backend/frontend
Trivy JSON, SPDX JSON and GitHub provenance bundle files, `monitoring-error-routing-report.json`,
`structured-log-report.json`, `postgres-tls-report.json`, `restore-drill-report.json`,
`capacity-profile-report.json`, `production-failover-report.json`,
`migration-upgrade-report.json`, `migration-upgrade-verification-report.json`,
`no-direct-filing-submission-report.json`, `production-readiness-report.json`,
`production-readiness-verification-report.json`,
`visual-smoke-manifest.json`,
`visual-smoke-evidence-report.json`,
`accountant-workbench-evidence-report.json`, `release-evidence-report.json`,
and the six completed `Docs/release-evidence/*.md` human sign-off templates.
The verifier fails if any required report is missing,
does not have `status: passed`, if the supply-chain evidence is not a strict trusted-main
promotion tied to the exact candidate, if either component was rebuilt, tagged mutably,
not pulled by digest, not scanned at its production digest, has any HIGH/CRITICAL finding,
or lacks its retained SPDX/provenance evidence, if supplied release candidate identity is incomplete
or malformed, if `release-evidence-report.json` does not carry exactly six
`humanEvidenceCompletion` entries with the expected template file, reviewer role,
sign-off gate, release identity, `status: accepted`, and zero blocking failures, if
the completed release-evidence templates are absent from the artifact pack or do not
match the SHA-256/byte-size manifest in
`release-evidence-report.json`, if accountant workbench route acceptance rows are
missing, if visual smoke screenshot summaries omit canonical state/URL/tab/semantic identity or retained PNG image data,
sample counts, distinct color diversity, or luminance range evidence, if the
visual smoke manifest 32-state inventory/audits or 192 screenshot rows do not match the retained
visual smoke evidence report, or if
cross-report checks such as either server/client monitoring correlation id do not match, the
normalized client route drifts, or the synthetic-marker-absence controls are not true. It also
rejects capacity or failover evidence unless it has the exact release commit/run identity, canonical
HTTPS smoke origin, bounded thresholds, successful endpoint/phase measurements, ephemeral
`accounts-production` execution scope, privacy flags, and explicit non-production scope limits. The
generated `release-artifact-pack-report.json` records the release commit, GitHub
Actions run URL, and a SHA-256/byte-size manifest for each required report and
retained human release-evidence template. It also records a
`releaseEvidenceScorecardSummary` copied from
`release-evidence-report.json.productionScorecardCompletion`, including complete
1,000/1,000 independently audited control-ledger status, the exact baseline audited
commit carried through `auditedCommit`, accepted/remaining human evidence counts and the final category
scores, plus a `releaseEvidenceWorkspaceSummary` with the retained workspace
verification status, 21-file prepared workspace count, prepared-human-control
count, six pending human blocker rows, six unassigned reviewer assignment rows and
`reviewerAssignmentPickupFileGuidanceCount` plus the per-gate
`reviewerAssignmentPickupFiles` inventory for retained pickup-file guidance.

CI also retains a machine-evidence pack before human sign-off evidence is complete.
Download the `ci-machine-evidence-pack` artifact and keep
`ci-machine-evidence-pack-report.json` with the candidate evidence. It proves the
exact commit/run identity and SHA-256 inventory for dependency audit, production
safety, monitoring smoke, structured logs, backup/restore, bounded capacity, ephemeral failover, no-direct submission,
PostgreSQL migration drift/upgrade/transactional rollback,
production-readiness and visual/workbench evidence. After the prepared reviewer
workspace is generated, the CI job reruns the verifier with
`-ReviewerWorkspaceDirectory` so the retained report also proves the 21-file
reviewer workspace, passed workspace verification report and six unassigned
reviewer assignment rows with complete per-gate pickup-file guidance recorded as
`reviewerAssignmentPickupFileGuidanceCount = 6`. It does not replace the full
release artifact pack or any named human sign-off template.

Before completing the visual QA sign-off, verify the CI visual smoke manifest and
retain the generated evidence report with the screenshot artifact:

```powershell
cd frontend
node scripts\verify-visual-smoke-artifacts.mjs --manifest=D:\accounts-smoke\visual-smoke\visual-smoke-manifest.json --report-path=D:\accounts-smoke\visual-smoke\visual-smoke-evidence-report.json
node scripts\verify-accountant-workbench-evidence.mjs --visual-report=D:\accounts-smoke\visual-smoke\visual-smoke-evidence-report.json --report-path=D:\accounts-smoke\visual-smoke\accountant-workbench-evidence-report.json
```

The visual smoke evidence report must retain `canonical-material-states-v1`, all 32
canonical states, 18 material routes, 9 named UI states, 192 light/dark
mobile/tablet/desktop captures, canonical URL/tab observations, semantic-content
hashes and distinctness status, screenshot hashes, byte sizes, planned PNG dimensions,
IDAT byte counts, sampled pixel counts, distinct color buckets, and luminance range.
A PNG that is structurally valid but visually blank or semantically duplicates another
intended state is not sufficient
evidence for visual QA sign-off. Copy the minimum PNG IDAT byte size, screenshot
pixel sample count, sampled distinct color count, and luminance range from the
retained report into `Docs/release-evidence/visual-qa-signoff-template.md`; the
release evidence verifier rejects visual QA sign-off if the distinct color count is
below `4` or the luminance range is below `10`.

The accountant workbench evidence report must retain the seven planned route keys,
42 accountant-route captures (six per route), and the linked 192-capture visual-smoke totals,
expected route text, blocking status and required qualified-accountant route
acceptance evidence for each workbench route.

For local Windows or Codex workspaces where Next.js cannot spawn child-process workers or cannot clean a stale `.next` directory, use a clean checkout or temporary copy outside the repository and keep the standard `.next` output directory. The app exposes an opt-in worker-thread fallback for this verification path only:

```powershell
cd frontend
npm run lint
npx tsc --noEmit --incremental false
npm run test:readiness
$env:NEXT_TURBOPACK_USE_WORKER = "0"
$env:NEXT_BUILD_WORKER_THREADS = "1"
npm run build:clean
```

`npm run build:clean` keeps a hashed dependency cache under the operating-system temp directory so repeated local verifications do not spend minutes copying and deleting `node_modules`.

Do not commit a custom `distDir` to work around local filesystem locks; the production image and `npm run start` expect the standalone server under `.next/standalone`.

## Release Checklist

1. Take a pre-release backup and verify the sha256 file.
2. Download the exact trusted-`main` `container-supply-chain` artifact. Confirm both reports have `status: passed`, `promotionMode: promoted`, `releaseEligible: true`, the expected full commit SHA/run URL, zero blockers, and matching report SHA-256. Reject verification-only evidence.
3. Copy `images.backend.exactDigestReference` to `ACCOUNTS_API_IMAGE` and `images.frontend.exactDigestReference` to `ACCOUNTS_FRONTEND_IMAGE`. Both values must match `^ghcr\.io/[a-z0-9._/-]+@sha256:[0-9a-f]{64}$`; tags are forbidden. The migration and API services must retain the same backend value.
4. Verify GitHub provenance, then pull and inspect the exact promoted digests:

```powershell
$env:GITHUB_REPOSITORY = "jasperfordesq-ai/accounts"
gh attestation verify "oci://$env:ACCOUNTS_API_IMAGE" --repo $env:GITHUB_REPOSITORY
gh attestation verify "oci://$env:ACCOUNTS_FRONTEND_IMAGE" --repo $env:GITHUB_REPOSITORY
docker pull $env:ACCOUNTS_API_IMAGE
docker pull $env:ACCOUNTS_FRONTEND_IMAGE
docker compose -f compose.production.yml pull
docker image inspect $env:ACCOUNTS_API_IMAGE
docker image inspect $env:ACCOUNTS_FRONTEND_IMAGE
```

5. Run `scripts\verify-container-supply-chain-report.ps1 -EvidencePath D:\accounts-smoke\container-supply-chain-report.json -CommitSha <release-commit-sha> -GitHubActionsRunUrl <ci-run-url>` and retain the passing verification report with the Trivy, SPDX and provenance files.
6. Run `docker compose -f compose.production.yml config --quiet` and retain `production-safety-report.json`.
7. Confirm the exact-candidate `postgres-migration-upgrade-gate` artifact passes, is tied to the same encrypted `restore-drill-report.json`, and then run the migration job.
8. Start the API and frontend.
9. Run the frontend/proxy/session smoke test:

```powershell
$env:ACCOUNTS_FRONTEND_URL = "https://accounts.example.ie"
$env:SMOKE_LOGIN_EMAIL = "owner@example.ie"
$env:SMOKE_LOGIN_PASSWORD = "<read from the release secret store>"
$env:SMOKE_TOTP_SECRET = "<read from the separate MFA secret store for an enrolled smoke account>"
.\scripts\smoke-production.ps1
```

The smoke completes the privileged-account MFA challenge through the frontend proxy. By default it
rejects an account that still requires enrollment: enrol the production smoke account out of band,
retain its authenticator and recovery credentials through the approved identity process, and provide
`SMOKE_TOTP_SECRET` (or `-TotpSecret`) from the separate authorized MFA secret store. Do not place it
in the repository, command history, logs, or retained smoke artifacts. Only the disposable CI
bootstrap passes `-AllowEphemeralMfaEnrollment`; that path consumes the one-time enrollment secret
without writing the secret or generated recovery codes to evidence and must never be used for a
persistent operator or production account.

To prove production error routing, deploy the stack with `MONITORING_ERROR_SMOKE_ENABLED=true` for the smoke window and run:

```powershell
.\scripts\smoke-production.ps1 -CheckMonitoringErrorRouting -OutputDirectory D:\accounts-smoke
```

The smoke exercises two POST-only, CSRF-protected paths. The Owner-only server smoke emits a fixed
synthetic exception. The authenticated client-event path accepts only a fixed event code, normalizes
the application route through matching browser/server allowlists and deliberately receives
synthetic email/secret markers so evidence can prove they were removed. The explicit reporter sends
only a redacted exception, normalized route shape, method, safe correlation ID, exception type,
stack fingerprint and fixed event code; provider enrichment is configured without request/user
data, automatic failed-request capture, stack attachment, or breadcrumbs. Retain
`monitoring-error-routing-report.json` with both provider event IDs and correlation IDs, normalized
client route and `sensitiveInputAbsent: true`. Confirm both events appear in the configured
error-tracking project before approving real filing use.

Use [the monitoring incident-response runbook](monitoring-incident-response.md) for alert ownership,
severity, containment, privacy review, recovery and retained evidence. A controlled exercise must use
synthetic data, measure provider delivery/acknowledgement/escalation, inspect the provider event and
structured log for PII, and retain the exact candidate/environment identity. It does not replace the
named real-provider confirmation gate.

After the monitoring smoke check, retain a structured API log sample:

```powershell
docker compose -f compose.production.yml logs --no-color --no-log-prefix api > D:\accounts-smoke\api-structured.log
.\scripts\verify-structured-logs.ps1 -LogPath D:\accounts-smoke\api-structured.log -MonitoringEvidencePath D:\accounts-smoke\monitoring-error-routing-report.json -EvidencePath D:\accounts-smoke\structured-log-report.json
```

The verifier parses JSON console lines, requires timestamp, level and category fields, confirms both
server and client correlation IDs appear on their exact sanitized log messages, and fails if the
synthetic email/secret markers occur anywhere in the retained log. The resulting report must carry
`matchedMonitoringSmokeLine`, `matchedClientMonitoringLine` and
`syntheticSensitiveMarkersAbsent` as `true`. CI uploads the same files as the
`structured-json-log-sample` artifact.

To include sample statutory output checks from a non-production tenant in the same deployment, pass an explicit company and period:

```powershell
.\scripts\smoke-production.ps1 -CheckDownloads -CompanyId 1 -PeriodId 1 -OutputDirectory D:\accounts-smoke
```

The smoke script checks `/health/ready`, validates browser security headers including the nonce-based Content Security Policy, signs in through the frontend proxy, verifies `/api/auth/me`, lists companies, optionally emits the controlled monitoring event, performs a CSRF-protected logout, confirms logout clears the authenticated session by requiring GET `/api/auth/me` must return `401 Unauthorized` after logout, and optionally downloads a sample accounts PDF and iXBRL XHTML package. In HTTPS production runs, it rejects `script-src` policies that still allow `unsafe-inline`; `-AllowInsecureHttp` exists only for local dry runs.

The HTTPS smoke path also verifies that the login response sets the `accounts_session` and `accounts_csrf` cookies with the `Secure` attribute, so production cookies stay aligned with the ingress contract.

10. Run `scripts\verify-no-direct-filing-submission.ps1 -CommitSha <release-commit-sha> -GitHubActionsRunUrl <ci-run-url>` and retain `no-direct-filing-submission-report.json`.
11. Run `scripts\verify-production-readiness-report.ps1 -ReportPath D:\accounts-smoke\production-readiness-report.json -EvidencePath D:\accounts-smoke\production-readiness-verification-report.json` and retain `production-readiness-verification-report.json`; it must prove the six human release-evidence gates, each gate's full expected per-gate `reviewerPickupFiles` list, and the five-step `humanReleaseEvidenceCloseout` sequence from prepared reviewer-workspace pickup through final artifact-pack verification.
Before completing the human evidence templates, create a release-specific reviewer workspace with
`scripts\new-release-evidence-workspace.ps1 -OutputDirectory D:\accounts-release-evidence -CommitSha <release-commit-sha> -GitHubActionsRunUrl <ci-run-url> -ProductionReadinessReportPath D:\accounts-smoke\production-readiness-report.json -ProductionReadinessVerificationReportPath D:\accounts-smoke\production-readiness-verification-report.json -VisualSmokeEvidenceReportPath D:\accounts-smoke\visual-smoke-evidence-report.json -MonitoringErrorRoutingReportPath D:\accounts-smoke\monitoring-error-routing-report.json -StructuredLogReportPath D:\accounts-smoke\structured-log-report.json`.
The helper copies the six templates, pre-fills only release identity and machine-derived evidence fields, pre-fills all 32 visual QA state `Notes` cells with exact `visual-smoke-evidence-report.json#routeCoverage.<state-id>` anchors, pre-fills the source-law snapshot fingerprint/content hash and source-law source-row `Notes` cells with exact `source-law-review-ledger#<source-id>` anchors, pre-fills the external ROS/iXBRL scenario `External reference` and `Taxonomy package` cells with exact `external-ros-validation-ledger#<scenario>` and `revenue-taxonomy-package-ledger#<scenario>` anchors, pre-fills the manual handoff scenario and unsupported-path evidence cells with exact `signed-auditor-report-evidence#<scenario>`, `manual-handoff-note#<scenario>`, `filing-readiness-snapshot#<scenario>`, and `unsupported-path-evidence#<path-code>` anchors, pre-fills both server/client monitoring provider IDs, normalized client route, correlation matches and synthetic-marker absence from `monitoring-error-routing-report.json` and `structured-log-report.json`, pre-fills the qualified-accountant scenario and route evidence reference cells with exact `qualified-accountant-walkthrough-ledger#<scenario>`, `accountant-workbench-evidence-report.json#routeAcceptance.<route>`, and `qualified-accountant-route-walkthrough#<route>` anchors, retains the machine evidence JSON inputs (`production-readiness-report.json`, `production-readiness-verification-report.json`, `visual-smoke-manifest.json`, `visual-smoke-evidence-report.json`, `accountant-workbench-evidence-report.json`, `monitoring-error-routing-report.json`, and `structured-log-report.json`), records the source CI artifact name, source artifact file, byte size, and SHA-256 hash for those retained machine inputs in `release-evidence-workspace-manifest.json`, writes `release-evidence-machine-summary.json`, `release-evidence-reviewer-index.md`, `release-evidence-reviewer-completion.json`, and `release-evidence-reviewer-assignments.json`, records each assignment row's `reviewerPickupFiles` so reviewers know which retained files to inspect for that gate, and leaves all reviewer identity, pass/fail/source-review/professional acceptance/manual handoff decisions, signatures, external validation artifact hashes, warnings/errors, decisions, provider-console confirmation checkboxes, both provider event URL/reference fields, operator notes, and human acceptance cells blank.
Run `scripts\verify-release-evidence-workspace.ps1 -WorkspaceDirectory D:\accounts-release-evidence -CommitSha <release-commit-sha> -GitHubActionsRunUrl <ci-run-url>` and retain `release-evidence-workspace-verification-report.json`, `release-evidence-machine-summary.json`, `release-evidence-reviewer-completion.json`, `release-evidence-reviewer-assignments.json`, `release-evidence-reviewer-blockers.md`, plus `release-evidence-verifier-output.txt` before sending the workspace to reviewers; the verifier must prove the workspace is well-formed, the machine evidence JSON inputs are retained, the machine summary agrees with the manifest, the manifest source CI artifact provenance, byte sizes and SHA-256 hashes match the copied machine evidence files, the machine summary carries `productionReadiness.humanReleaseEvidenceReviewerPickupFiles`, the reviewer completion ledger still has all six gates pending, the reviewer assignment ledger still has all six gates unassigned until named reviewer routing, each assignment's pickup-file guidance is retained in `reviewerPickupFiles`, prepared human templates leave top-level reviewer/accountant identity, signature and acceptance checkbox fields blank, release evidence stays blocked until named human sign-off is complete, and the verification report records the release candidate commit/run identity, the `preparedHumanTemplateControls` blank-field/checkbox inventory, the six-entry `pendingHumanEvidenceBlockers` reviewer pickup inventory, the six-row `reviewerAssignmentInventory`, and the exact retained reviewer workspace file set with byte sizes and SHA-256 hashes.
Default CI also uploads the same prepared files as the `release-evidence-reviewer-workspace` artifact after the machine evidence pack is verified, including a failed `release-evidence-report.json`, a reviewer assignment ledger with six unassigned gates, a reviewer-facing `release-evidence-reviewer-blockers.md` summary, and `release-evidence-workspace-verification-report.json` that list the remaining named human evidence blockers. The generated `release-evidence-reviewer-index.md` carries the reviewer closeout sequence: inspect the prepared reviewer workspace, completion ledger, assignment ledger, handoff files and pending human blocker inventory; complete all six Markdown templates with named reviewer identities and accepted decisions; rerun `scripts\verify-release-evidence.ps1`; confirm six accepted `humanEvidenceCompletion` entries, `productionScorecardCompletion` status `complete` at 1,000/1,000 with no open weighted engineering/assurance controls, and no blocking failures; then run `scripts\verify-release-artifact-pack.ps1` for the same commit SHA and GitHub Actions run URL.
12. Complete `Docs/release-evidence/source-law-review-template.md` against the production readiness source-law snapshot, source-law review ledger, and current CRO, Revenue, FRC, and Charities Regulator pages.
13. Complete `Docs/release-evidence/visual-qa-signoff-template.md` using all 192 canonical-state screenshots in the CI `visual-smoke-screenshots` artifact, `visual-smoke-manifest.json`, `visual-smoke-evidence-report.json`, and the retained canonical URL/tab, semantic-distinctness, nonblank-pixel and automated contrast evidence.
14. Complete `Docs/release-evidence/external-ros-ixbrl-validation-template.md` using external ROS/iXBRL validation references for the exact generated artifact hashes.
15. Complete `Docs/release-evidence/monitoring-provider-confirmation-template.md` using the CI monitoring and structured-log artifacts plus the real provider event.
16. Complete `Docs/release-evidence/qualified-accountant-acceptance-template.md` with a named qualified accountant before real filing preparation is used.
17. Complete `Docs/release-evidence/manual-handoff-acceptance-template.md` for `medium-audit-required` and every unsupported path code before any audit-required or unsupported output is relied on.
18. Run `scripts\verify-release-evidence.ps1` and retain `release-evidence-report.json`; real filing use stays blocked if any required checkbox, signature, artifact reference, release identity, UTC timestamp, SHA-256 digest, table row, accepted decision, canonical golden corpus scenario row, source-law source row or per-source note reference, external ROS/iXBRL validation row, manual handoff scenario/path row, route row, visual smoke evidence report reference, visual nonblank metric field, monitoring log confirmation field, required coverage entry, same-candidate identity across all six human evidence templates, retained workspace control file (`release-evidence-workspace-manifest.json`, `release-evidence-machine-summary.json`, `release-evidence-workspace-verification-report.json`), retained workspace manifest or machine-summary machine-evidence provenance row, machine-summary byte-size/SHA-256/source-artifact value matching the workspace manifest, machine-summary completion policy/readiness status/human closeout step list/reviewer queue, workspace verification release-candidate identity, exact prepared-workspace inventory entry, prepared human-template control entry, pending human-evidence blocker entry, or reviewer assignment inventory row is missing or malformed. The report's `humanEvidenceCompletion` section records each template's named reviewer role, sign-off gate, status, and template-specific blocking failures; its `productionScorecardCompletion` section mirrors the exact candidate's independently audited weighted controls and must remain `blocked` below 1,000/1,000 while any engineering, machine-assurance, or human/external control remains open; its `workspaceControlFiles` section records the retained manifest, machine summary, and workspace verification report, and the workspace verification report must retain the same commit/run identity as the human templates plus the canonical prepared workspace inventory, prepared-human-control inventory, pending human-evidence blocker rows and unassigned reviewer assignment rows with reviewer pickup-file guidance and byte-size/SHA-256 evidence, including the pending reviewer assignment ledger, so reviewers can clear the remaining human evidence without losing the machine-evidence provenance chain.
19. Run `scripts\verify-release-artifact-pack.ps1` with the release commit SHA and GitHub Actions run URL, then retain `release-artifact-pack-report.json`; real filing use stays blocked if the exact release artifact pack is missing container supply-chain reports and retained Trivy/SPDX/provenance evidence, dependency, production safety, monitoring, structured log, migration drift/previous-release upgrade/forced-failure rollback, encrypted backup/restore, bounded capacity, ephemeral failover, no-direct-submission, production-readiness report and verification, visual smoke manifest and evidence report, accountant-workbench evidence, completed release-evidence reports, production-readiness verification coverage for the human evidence gates and `humanReleaseEvidenceCloseout` steps, the completed release-evidence Markdown templates with matching SHA-256/byte-size inventory, accepted `humanEvidenceCompletion` rows with zero blocking failures, or a `productionScorecardCompletion` ledger proving complete 1,000/1,000 status with no open weighted controls and the expected 150/350/250/250 category scores. The capacity and failover rows must match the release commit/run exactly and preserve their bounded CI and non-production recovery limitations. The same-candidate workspace controls, retained machine evidence provenance, reviewer handoff files, exact prepared-workspace inventory, pickup-file guidance, and release identity must also pass unchanged.
20. Follow `Docs/operations/durable-release-evidence.md`: in the private evidence repository, retain an exact manifest for every input file plus exactly five distinct raw HTML/XHTML iXBRL artifacts (`micro-ltd`, `small-abridged-ltd`, `dac-small`, `clg-charity`, and `medium-audit-required`); verify seven detached signatures, the out-of-band-pinned trust policy, and `CandidateRunCompletedAtUtc` derived from the validated canonical Actions run `updated_at`; then have the protected private-repository caller invoke the public application's reusable workflow pinned to the candidate SHA. There is no public `workflow_dispatch`. Independently verify the resulting private immutable release, exact downloaded asset and GitHub provenance. The private repository/environment/GitHub App/trust anchors/evidence/publication are not yet provisioned or proven, and every named human/external gate remains open. Do not close `P2-EVID-001` until the real signed bundle remains retrievable and all reviewer identity, professional-capacity and credential checks succeed.
