# Production Operations Runbook

This runbook covers the minimum production operating procedures for the Irish statutory accounts platform.

## HTTPS Ingress Contract

Production traffic must enter through an HTTPS reverse proxy that performs TLS termination before forwarding requests to the Docker Compose frontend. The production compose file publishes the frontend only on `127.0.0.1:${FRONTEND_PORT:-3000}:3000`; do not expose the frontend or API containers directly to the internet.

Use `deploy/caddy/Caddyfile.example` as the checked-in ingress contract. It listens on the public hostname, obtains/serves certificates through Caddy, proxies to the local frontend port, and overwrites X-Forwarded-For, overwrites X-Forwarded-Host, and overwrites X-Forwarded-Proto before the request reaches Next.js. Set `TRUST_PROXY_HEADERS=true` only when the ingress is deployed on the same trusted host or private network and untrusted clients cannot bypass it. The API also validates this flag at startup before it trusts forwarded client IPs for rate limiting. Leave `TRUST_PROXY_HEADERS=false` or unset for any topology where arbitrary clients can connect to the frontend port.

Minimum ingress environment:

```powershell
$env:ACCOUNTS_HOSTNAME = "accounts.example.ie"
$env:FRONTEND_PORT = "3000"
```

Validate the Caddy configuration before a release:

```powershell
caddy validate --config .\deploy\caddy\Caddyfile.example
```

CI also runs the production smoke test through this Caddyfile instead of calling the frontend port directly. The workflow maps `accounts-smoke.local` to `127.0.0.1`, starts Caddy with `ACCOUNTS_CADDY_GLOBAL_OPTIONS=local_certs`, trusts the generated local CA on the runner, and runs `smoke-production.ps1` against `https://accounts-smoke.local`. Do not set `local_certs` in production unless you intentionally operate with an internally trusted private CA; the normal production path should use the public hostname and Caddy's public certificate automation.

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

Sensitive values are mounted as Docker secrets under `/run/secrets`. Put each raw secret in a file outside the repository, set the matching `*_FILE` variable to that path, and keep those files out of terminal history, logs, and backups that are not approved for secret material.

The containers run as a non-root user and, in non-swarm `docker compose`, the in-container secret keeps the host file's permissions (the `mode:` field is swarm-only). Each secret file must therefore be readable by that user: keep the secrets directory `0700` and the secret files `0444`. A `0600` file owned by the host user makes the migration, API, and frontend containers fail at startup with `Permission denied` on `/run/secrets/...`.

Do not run plain `docker compose -f compose.production.yml config` with production secrets. The non-quiet render can still print secret file paths into terminal scrollback or CI logs.

| Variable | Purpose |
| --- | --- |
| `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD_FILE` | PostgreSQL database, user, and Docker secret file path for the database password. |
| `ACCOUNTS_API_IMAGE`, `ACCOUNTS_FRONTEND_IMAGE` | CI-promoted immutable image references for the backend/migration image and frontend image. Prefer digest-pinned refs such as `registry.example/accounts-api@sha256:...`. |
| `ACCOUNTS_CONNECTION_STRING_FILE` | Docker secret file path containing the API connection string, normally pointing to `Host=db;Port=5432` inside Compose. |
| `AUTH_SESSION_SIGNING_KEY_FILE` | Docker secret file path containing a Base64 or Base64Url secret of at least 32 bytes for signed browser sessions. |
| `AUDIT_INTEGRITY_ACTIVE_KEY_ID`, `AUDIT_INTEGRITY_SIGNING_KEY_FILE` | Audit hash-chain signing key id and Docker secret file path containing the Base64 signing secret. |
| `ACCOUNTS_ALLOWED_HOSTS` | Public API hostnames; Compose appends internal `api` for the private frontend-to-API hop. |
| `ACCOUNTS_ALLOWED_ORIGIN` | Public HTTPS origin used by the browser, for example `https://accounts.example.ie`. |
| `ACCOUNTS_API_KEY_FILE`, `ACCOUNTS_API_KEY_HASH` | Docker secret file path for the frontend service API key and matching lowercase SHA-256 hash. |
| `TRUST_PROXY_HEADERS` | Set to `true` only when the trusted ingress overwrites forwarded headers and clients cannot bypass it; required by the API when `RateLimits__TrustForwardedFor=true`. |
| `MONITORING_ERROR_TRACKING_DSN` | Required HTTPS DSN for the production error-tracking provider. Startup fails outside development when this is missing or not HTTPS. |
| `MONITORING_ERROR_TRACKING_PROVIDER` | Optional; defaults to `Sentry-compatible`. Use a short provider label for the readiness report and operational records. |
| `MONITORING_TRACES_SAMPLE_RATE` | Optional; defaults to `0`. Must be between `0` and `1` when set. |
| `MONITORING_ERROR_SMOKE_ENABLED` | Optional; defaults to `false`. Set to `true` only for an operator-controlled release smoke run that emits a fixed non-PII event through `/api/system/monitoring/error-smoke`, then turn it back off if the endpoint should not remain available between releases. |
| `BOOTSTRAP_TENANT_NAME`, `BOOTSTRAP_TENANT_SLUG` | Initial firm tenant created by the controlled migration/bootstrap job. |
| `BOOTSTRAP_OWNER_EMAIL`, `BOOTSTRAP_OWNER_DISPLAY_NAME`, `BOOTSTRAP_OWNER_PASSWORD_FILE` | Initial owner account and Docker secret file path for the initial password; `BOOTSTRAP_OWNER_PASSWORD_FILE` must contain a password of at least 20 characters and include upper case, lower case, number, and symbol characters. Rotate the password at first login. |
| `BOOTSTRAP_OWNER_MUST_CHANGE_PASSWORD` | Optional; defaults to `true`. When `true` the bootstrap owner must change the password at first sign-in before any other API access is allowed. |

Generate independent session and audit secrets:

```powershell
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$bytes = New-Object byte[] 64
$rng.GetBytes($bytes)
$env:AUTH_SESSION_SIGNING_KEY = [Convert]::ToBase64String($bytes)
$rng.GetBytes($bytes)
$env:AUDIT_INTEGRITY_SIGNING_KEY = [Convert]::ToBase64String($bytes)
$rng.Dispose()
```

## Backup Policy

- RPO: 24 hours for routine operations. Take an extra backup before migrations, releases, and bulk imports.
- RTO: 4 hours for database restore to a prepared host with Docker and the application images available.
- Store PostgreSQL custom-format dumps outside the application host after each backup.
- Retain daily backups for 14 days, weekly backups for 8 weeks, and monthly backups for 12 months unless client policy requires more.
- Encrypt backups at rest using the storage provider or a separate encryption layer before off-host transfer.

## Backup Command

Set the production database environment variables used by `compose.production.yml`, then run:

```powershell
.\scripts\backup-postgres.ps1 -OutputDirectory D:\accounts-backups
```

The script uses `pg_dump --format=custom`, writes a `.dump` file, and writes a `.sha256` file beside it. Record the backup filename, sha256 value, application release, and migration version in the operations log.

`-OutputDirectory` is mandatory and must point outside the repository. The script refuses repository-local output paths by default so production dumps are not left under the application checkout. `-AllowRepositoryOutputForLocalDryRun` exists only for local dry runs with non-production data.

## Restore Drill

Run a restore drill after every material schema change and at least monthly:

```powershell
.\scripts\verify-postgres-backup.ps1 -BackupPath D:\accounts-backups\accounts-20260607-010000.dump
```

The drill restores into `accounts_restore_verify`, queries core tables, and leaves the production database untouched.

In CI, the production smoke job creates an ephemeral production-shape backup, verifies
the restore, writes `restore-drill-report.json`, and uploads the
`postgres-backup-restore-drill` artifact containing the `.dump`, `.dump.sha256`, and
restore evidence report. Treat that artifact as the release proof for the CI candidate;
for real production data, retain the same fields in the operations evidence store rather
than uploading client data to CI.

## Production Restore

Restores are destructive when `-Clean` is used. Stop application traffic first and set the explicit confirmation variable. The restore script verifies the adjacent `.sha256` file before restoring; use `-AllowUnverifiedBackupRestore` only for a documented break-glass incident where the checksum file is unavailable but the backup has been independently verified. The restore runs `pg_restore` in a single transaction and is configured to exit on the first restore error so a failed restore does not silently leave partially applied objects behind.

```powershell
$env:RESTORE_CONFIRM = "accounts"
.\scripts\restore-postgres.ps1 -BackupPath D:\accounts-backups\accounts-20260607-010000.dump -TargetDatabase accounts -Clean
```

After restore:

1. Run the migration job with `docker compose -f compose.production.yml run --rm migrate`.
2. Check `GET /health/ready`.
3. Verify company counts, latest audit-log entries, and a sample statutory accounts package.
4. Record the restore drill or incident outcome, sha256, operator, start time, finish time, and any exceptions.

## Build Gate

CI is the authoritative build gate for releases. Before promoting an image, confirm the backend tests and build, frontend audit, type-check, lint, readiness regression, production monitoring config gate, and Next production build have all passed. Production deployment must run CI-promoted immutable image references, not rebuild from the release checkout. Set `ACCOUNTS_API_IMAGE` to the tested backend image tag or digest and `ACCOUNTS_FRONTEND_IMAGE` to the tested frontend image tag or digest; the migration job and API service intentionally use the same `ACCOUNTS_API_IMAGE`.

Do not deploy production by rebuilding from the checkout with `docker compose up --build`. Rebuilding on the production host can run code that differs from the CI-promoted immutable image and makes rollback, migration/app parity, and incident reconstruction weaker.

Retain the CI `production-safety-config` artifact for each release candidate. It is produced by:

```powershell
.\scripts\verify-production-compose-images.ps1 -EvidencePath D:\accounts-smoke\production-safety-report.json
```

The report proves the production compose profile uses CI-promoted images, the migration job runs exactly `--migrate-only`, the API waits for that job to complete, normal API startup has `DatabaseStartup__AutoMigrateOnStartup=false`, demo seeding is disabled for both migration and API services, demo-seed override flags are absent, and the bootstrap owner initial password is available only to the migration job.

Retain the no-direct filing submission control report for each release candidate:

```powershell
.\scripts\verify-no-direct-filing-submission.ps1 -EvidencePath D:\accounts-smoke\no-direct-filing-submission-report.json
```

The report proves final CRO and ROS operations remain recorded workflow states only:
the API exposes status, payment, download and internal iXBRL validation endpoints, the
legacy generated marker is blocked with `410 Gone`, and no outbound CRO/ROS submission client or submit route is wired into the release.

Retain the CI `dependency-audit-release` artifact as the dependency evidence packet. It contains `npm-audit.json` and `dependency-audit-report.json`; the latter records package-lock and package.json hashes, npm audit counts, the backend NuGet audit policy (`NU1901`-`NU1904` as errors), and workflow action-hygiene wiring:

```powershell
.\scripts\write-dependency-evidence.ps1 -NpmAuditJsonPath D:\accounts-smoke\npm-audit.json -EvidencePath D:\accounts-smoke\dependency-audit-report.json
```

The remaining manual release evidence should be recorded with the checked-in templates:

- `Docs/release-evidence/visual-qa-signoff-template.md`
- `Docs/release-evidence/external-ros-ixbrl-validation-template.md`
- `Docs/release-evidence/qualified-accountant-acceptance-template.md`
- `Docs/release-evidence/monitoring-provider-confirmation-template.md`

After the templates are completed for a release candidate, run the release evidence
verifier and retain its JSON report with the release evidence pack:

```powershell
.\scripts\verify-release-evidence.ps1 -EvidenceDirectory .\Docs\release-evidence -ReportPath D:\accounts-smoke\release-evidence-report.json
```

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
2. Set `ACCOUNTS_API_IMAGE` and `ACCOUNTS_FRONTEND_IMAGE` to the CI-promoted immutable image references.
3. Pull and inspect the promoted images:

```powershell
docker compose -f compose.production.yml pull
docker image inspect $env:ACCOUNTS_API_IMAGE
docker image inspect $env:ACCOUNTS_FRONTEND_IMAGE
```

4. Run `docker compose -f compose.production.yml config --quiet` and retain `production-safety-report.json`.
5. Run the migration job.
6. Start the API and frontend.
7. Run the frontend/proxy/session smoke test:

```powershell
$env:ACCOUNTS_FRONTEND_URL = "https://accounts.example.ie"
$env:SMOKE_LOGIN_EMAIL = "owner@example.ie"
$env:SMOKE_LOGIN_PASSWORD = "<read from the release secret store>"
.\scripts\smoke-production.ps1
```

To prove production error routing, deploy the stack with `MONITORING_ERROR_SMOKE_ENABLED=true` for the smoke window and run:

```powershell
.\scripts\smoke-production.ps1 -CheckMonitoringErrorRouting -OutputDirectory D:\accounts-smoke
```

The monitoring check is POST-only, CSRF-protected, Owner-only, and emits a fixed synthetic exception with no client data. Retain `monitoring-error-routing-report.json` with the provider event id and correlation id, then confirm the same event appears in the configured error-tracking project before approving real filing use.

After the monitoring smoke check, retain a structured API log sample:

```powershell
docker compose -f compose.production.yml logs --no-color --no-log-prefix api > D:\accounts-smoke\api-structured.log
.\scripts\verify-structured-logs.ps1 -LogPath D:\accounts-smoke\api-structured.log -MonitoringEvidencePath D:\accounts-smoke\monitoring-error-routing-report.json -EvidencePath D:\accounts-smoke\structured-log-report.json
```

The verifier parses JSON console lines, requires timestamp, level and category fields, and confirms the monitoring smoke correlation id appears in the structured log stream. CI uploads the same files as the `structured-json-log-sample` artifact.

To include sample statutory output checks from a non-production tenant in the same deployment, pass an explicit company and period:

```powershell
.\scripts\smoke-production.ps1 -CheckDownloads -CompanyId 1 -PeriodId 1 -OutputDirectory D:\accounts-smoke
```

The smoke script checks `/health/ready`, validates browser security headers including the nonce-based Content Security Policy, signs in through the frontend proxy, verifies `/api/auth/me`, lists companies, optionally emits the controlled monitoring event, performs a CSRF-protected logout, confirms logout clears the authenticated session by requiring GET `/api/auth/me` must return `401 Unauthorized` after logout, and optionally downloads a sample accounts PDF and iXBRL XHTML package. In HTTPS production runs, it rejects `script-src` policies that still allow `unsafe-inline`; `-AllowInsecureHttp` exists only for local dry runs.

The HTTPS smoke path also verifies that the login response sets the `accounts_session` and `accounts_csrf` cookies with the `Secure` attribute, so production cookies stay aligned with the ingress contract.

8. Run `scripts\verify-no-direct-filing-submission.ps1` and retain `no-direct-filing-submission-report.json`.
9. Complete `Docs/release-evidence/visual-qa-signoff-template.md` using the CI `visual-smoke-screenshots` artifact.
10. Complete `Docs/release-evidence/external-ros-ixbrl-validation-template.md` using external ROS/iXBRL validation references for the exact generated artifact hashes.
11. Complete `Docs/release-evidence/monitoring-provider-confirmation-template.md` using the CI monitoring and structured-log artifacts plus the real provider event.
12. Complete `Docs/release-evidence/qualified-accountant-acceptance-template.md` with a named qualified accountant before real filing preparation is used.
13. Run `scripts\verify-release-evidence.ps1` and retain `release-evidence-report.json`; real filing use stays blocked if any required checkbox, signature, artifact reference, table row, accepted decision, canonical golden corpus scenario row, external ROS/iXBRL validation row, route row, or required coverage entry is missing.
