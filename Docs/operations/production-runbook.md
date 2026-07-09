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

After collecting the release candidate artifact reports into one evidence directory,
run the artifact-pack verifier and retain its JSON report:

```powershell
.\scripts\verify-release-artifact-pack.ps1 -EvidenceDirectory D:\accounts-smoke -ReportPath D:\accounts-smoke\release-artifact-pack-report.json -CommitSha <release-commit-sha> -GitHubActionsRunUrl <ci-run-url>
```

The artifact pack must include `dependency-audit-report.json`,
`production-safety-report.json`, `monitoring-error-routing-report.json`,
`structured-log-report.json`, `restore-drill-report.json`,
`no-direct-filing-submission-report.json`, `production-readiness-report.json`,
`production-readiness-verification-report.json`,
`visual-smoke-manifest.json`,
`visual-smoke-evidence-report.json`,
`accountant-workbench-evidence-report.json`, `release-evidence-report.json`,
and the six completed `Docs/release-evidence/*.md` human sign-off templates.
The verifier fails if any required report is missing,
does not have `status: passed`, if supplied release candidate identity is incomplete
or malformed, if `release-evidence-report.json` does not carry exactly six
`humanEvidenceCompletion` entries with the expected template file, reviewer role,
sign-off gate, release identity, `status: accepted`, and zero blocking failures, if
the completed release-evidence templates are absent from the artifact pack or do not
match the SHA-256/byte-size manifest in
`release-evidence-report.json`, if accountant workbench route acceptance rows are
missing, if visual smoke screenshot summaries omit retained PNG image data,
sample counts, distinct color diversity, or luminance range evidence, if the
visual smoke manifest route audits or screenshot rows do not match the retained
visual smoke evidence report, or if
cross-report checks such as the monitoring smoke correlation id do not match. The
generated `release-artifact-pack-report.json` records the release commit, GitHub
Actions run URL, and a SHA-256/byte-size manifest for each required report and
retained human release-evidence template.

CI also retains a machine-evidence pack before human sign-off evidence is complete.
Download the `ci-machine-evidence-pack` artifact and keep
`ci-machine-evidence-pack-report.json` with the candidate evidence. It proves the
exact commit/run identity and SHA-256 inventory for dependency audit, production
safety, monitoring smoke, structured logs, backup/restore, no-direct submission,
production-readiness and visual/workbench evidence. It does not replace the full
release artifact pack or any named human sign-off template.

Before completing the visual QA sign-off, verify the CI visual smoke manifest and
retain the generated evidence report with the screenshot artifact:

```powershell
cd frontend
node scripts\verify-visual-smoke-artifacts.mjs --manifest=D:\accounts-smoke\visual-smoke\visual-smoke-manifest.json --report-path=D:\accounts-smoke\visual-smoke\visual-smoke-evidence-report.json
node scripts\verify-accountant-workbench-evidence.mjs --visual-report=D:\accounts-smoke\visual-smoke\visual-smoke-evidence-report.json --report-path=D:\accounts-smoke\visual-smoke\accountant-workbench-evidence-report.json
```

The visual smoke evidence report must retain screenshot hashes, byte sizes, planned
PNG dimensions, IDAT byte counts, sampled pixel counts, distinct color buckets, and
luminance range. A PNG that is structurally valid but visually blank is not sufficient
evidence for visual QA sign-off. Copy the minimum PNG IDAT byte size, screenshot
pixel sample count, sampled distinct color count, and luminance range from the
retained report into `Docs/release-evidence/visual-qa-signoff-template.md`; the
release evidence verifier rejects visual QA sign-off if the distinct color count is
below `4` or the luminance range is below `10`.

The accountant workbench evidence report must retain the seven planned route keys,
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

8. Run `scripts\verify-no-direct-filing-submission.ps1 -CommitSha <release-commit-sha> -GitHubActionsRunUrl <ci-run-url>` and retain `no-direct-filing-submission-report.json`.
9. Run `scripts\verify-production-readiness-report.ps1 -ReportPath D:\accounts-smoke\production-readiness-report.json -EvidencePath D:\accounts-smoke\production-readiness-verification-report.json` and retain `production-readiness-verification-report.json`; it must prove the six human release-evidence gates and the four-step `humanReleaseEvidenceCloseout` sequence from template completion through final artifact-pack verification.
Before completing the human evidence templates, create a release-specific reviewer workspace with
`scripts\new-release-evidence-workspace.ps1 -OutputDirectory D:\accounts-release-evidence -CommitSha <release-commit-sha> -GitHubActionsRunUrl <ci-run-url> -ProductionReadinessReportPath D:\accounts-smoke\production-readiness-report.json -ProductionReadinessVerificationReportPath D:\accounts-smoke\production-readiness-verification-report.json -VisualSmokeEvidenceReportPath D:\accounts-smoke\visual-smoke-evidence-report.json -MonitoringErrorRoutingReportPath D:\accounts-smoke\monitoring-error-routing-report.json -StructuredLogReportPath D:\accounts-smoke\structured-log-report.json`.
The helper copies the six templates, pre-fills only release identity and machine-derived evidence fields, pre-fills the visual QA route `Notes` cells with exact `visual-smoke-evidence-report.json#routeAcceptance.<route>` anchors, pre-fills the source-law snapshot fingerprint/content hash and source-law source-row `Notes` cells with exact `source-law-review-ledger#<source-id>` anchors, pre-fills the external ROS/iXBRL scenario `External reference` and `Taxonomy package` cells with exact `external-ros-validation-ledger#<scenario>` and `revenue-taxonomy-package-ledger#<scenario>` anchors, pre-fills the manual handoff scenario and unsupported-path evidence cells with exact `signed-auditor-report-evidence#<scenario>`, `manual-handoff-note#<scenario>`, `filing-readiness-snapshot#<scenario>`, and `unsupported-path-evidence#<path-code>` anchors, pre-fills the monitoring provider CI evidence fields from `monitoring-error-routing-report.json` and `structured-log-report.json`, pre-fills the qualified-accountant scenario and route evidence reference cells with exact `qualified-accountant-walkthrough-ledger#<scenario>`, `accountant-workbench-evidence-report.json#routeAcceptance.<route>`, and `qualified-accountant-route-walkthrough#<route>` anchors, retains the machine evidence JSON inputs (`production-readiness-report.json`, `production-readiness-verification-report.json`, `visual-smoke-manifest.json`, `visual-smoke-evidence-report.json`, `accountant-workbench-evidence-report.json`, `monitoring-error-routing-report.json`, and `structured-log-report.json`), records the source CI artifact name, source artifact file, byte size, and SHA-256 hash for those retained machine inputs in `release-evidence-workspace-manifest.json`, writes `release-evidence-machine-summary.json`, `release-evidence-reviewer-index.md`, and `release-evidence-reviewer-completion.json`, and leaves all reviewer identity, pass/fail/source-review/professional acceptance/manual handoff decisions, signatures, external validation artifact hashes, warnings/errors, decisions, provider-console confirmation checkboxes, provider event URL/reference, operator notes, and human acceptance cells blank.
Run `scripts\verify-release-evidence-workspace.ps1 -WorkspaceDirectory D:\accounts-release-evidence -CommitSha <release-commit-sha> -GitHubActionsRunUrl <ci-run-url>` and retain `release-evidence-workspace-verification-report.json`, `release-evidence-machine-summary.json`, `release-evidence-reviewer-completion.json`, `release-evidence-reviewer-blockers.md`, plus `release-evidence-verifier-output.txt` before sending the workspace to reviewers; the verifier must prove the workspace is well-formed, the machine evidence JSON inputs are retained, the machine summary agrees with the manifest, the manifest source CI artifact provenance, byte sizes and SHA-256 hashes match the copied machine evidence files, the reviewer completion ledger still has all six gates pending, prepared human templates leave top-level reviewer/accountant identity, signature and acceptance checkbox fields blank, release evidence stays blocked until named human sign-off is complete, and the verification report records the release candidate commit/run identity, the `preparedHumanTemplateControls` blank-field/checkbox inventory, and the exact retained reviewer workspace file set with byte sizes and SHA-256 hashes.
Default CI also uploads the same prepared files as the `release-evidence-reviewer-workspace` artifact after the machine evidence pack is verified, including a failed `release-evidence-report.json`, a reviewer-facing `release-evidence-reviewer-blockers.md` summary, and `release-evidence-workspace-verification-report.json` that list the remaining named human evidence blockers. The generated `release-evidence-reviewer-index.md` carries the reviewer closeout sequence: complete all six Markdown templates with named reviewer identities and accepted decisions, rerun `scripts\verify-release-evidence.ps1`, confirm six accepted `humanEvidenceCompletion` entries with no blocking failures, then run `scripts\verify-release-artifact-pack.ps1` for the same commit SHA and GitHub Actions run URL.
10. Complete `Docs/release-evidence/source-law-review-template.md` against the production readiness source-law snapshot, source-law review ledger, and current CRO, Revenue, FRC, and Charities Regulator pages.
11. Complete `Docs/release-evidence/visual-qa-signoff-template.md` using the CI `visual-smoke-screenshots` artifact, `visual-smoke-manifest.json`, `visual-smoke-evidence-report.json`, and the retained nonblank pixel plus automated contrast metric minima.
12. Complete `Docs/release-evidence/external-ros-ixbrl-validation-template.md` using external ROS/iXBRL validation references for the exact generated artifact hashes.
13. Complete `Docs/release-evidence/monitoring-provider-confirmation-template.md` using the CI monitoring and structured-log artifacts plus the real provider event.
14. Complete `Docs/release-evidence/qualified-accountant-acceptance-template.md` with a named qualified accountant before real filing preparation is used.
15. Complete `Docs/release-evidence/manual-handoff-acceptance-template.md` for `medium-audit-required` and every unsupported path code before any audit-required or unsupported output is relied on.
16. Run `scripts\verify-release-evidence.ps1` and retain `release-evidence-report.json`; real filing use stays blocked if any required checkbox, signature, artifact reference, release identity, UTC timestamp, SHA-256 digest, table row, accepted decision, canonical golden corpus scenario row, source-law source row or per-source note reference, external ROS/iXBRL validation row, manual handoff scenario/path row, route row, visual smoke evidence report reference, visual nonblank metric field, monitoring log confirmation field, required coverage entry, same-candidate identity across all six human evidence templates, retained workspace control file (`release-evidence-workspace-manifest.json`, `release-evidence-machine-summary.json`, `release-evidence-workspace-verification-report.json`), workspace verification release-candidate identity, or exact prepared-workspace inventory entry is missing or malformed. The report's `humanEvidenceCompletion` section records each template's named reviewer role, sign-off gate, status, and template-specific blocking failures; its `workspaceControlFiles` section records the retained manifest, machine summary, and workspace verification report, and the workspace verification report must retain the same commit/run identity as the human templates plus the canonical 20-file prepared workspace inventory with byte-size/SHA-256 evidence, so reviewers can clear the remaining human evidence without losing the machine-evidence provenance chain.
17. Run `scripts\verify-release-artifact-pack.ps1` with the release commit SHA and GitHub Actions run URL, then retain `release-artifact-pack-report.json`; real filing use stays blocked if the exact release artifact pack is missing dependency, production safety, monitoring, structured log, backup/restore, no-direct-submission, production-readiness report and verification, visual smoke manifest and evidence report, accountant-workbench evidence, completed release-evidence reports, production-readiness verification coverage for the human evidence gates and `humanReleaseEvidenceCloseout` steps, the six completed release-evidence Markdown templates with matching SHA-256/byte-size inventory, a six-entry `humanEvidenceCompletion` ledger accepted for the expected reviewer roles and sign-off gates with zero blocking failures, retained release evidence workspace control files (`release-evidence-workspace-manifest.json`, `release-evidence-machine-summary.json`, `release-evidence-workspace-verification-report.json`) with matching SHA-256/byte-size inventory, retained reviewer handoff files (`release-evidence-reviewer-index.md`, `release-evidence-reviewer-completion.json`, `release-evidence-reviewer-blockers.md`, and `release-evidence-verifier-output.txt`) matching the workspace verification report inventory, a retained workspace verification report that independently parses as passed with zero failures, same release-candidate identity and exact 20-file workspace inventory, or a release-evidence candidate identity matching the pack `CommitSha` and `GitHubActionsRunUrl`.
