# Irish Statutory Accounts Production Platform

Canonical engineering guidance for this repository lives in **[CLAUDE.md](CLAUDE.md)**:
architecture, tech stack, development commands, entity/service/endpoint maps,
authentication/authorization/security, and production deployment.

This file is the active agent handoff for the current production-readiness goal. Claude
sessions should read this file first, then continue with the goal below rather than
starting a new plan from scratch.

@CLAUDE.md

Project repository: https://github.com/jasperfordesq-ai/accounts

## Active Goal Handoff

Goal: finish the Irish statutory accounts platform so it is production-ready code-wise,
backend-wise, and visually, with explicit qualified-accountant review gates before any
real CRO/Revenue filing use.

Current working posture:

- Keep `main` as the integration branch.
- Do not mark the goal complete until current evidence proves every production-readiness
  requirement: backend statutory/golden corpus coverage, frontend accountant workbench
  quality, typed API drift protection, visual QA artifacts, operations/security evidence,
  and qualified-accountant sign-off.
- The app may generate CRO/Revenue-ready packs, but final real-world use must remain
  blocked by named qualified-accountant review.
- Direct CRO/ROS submission must stay unsupported; the app records workflow states only.

## What Has Been Achieved In This Session

Committed and pushed work on `main` includes:

- `056ec50 Add period workflow action queue`
- `419290e Add legal basis snapshots to golden corpus`
- `af14cfc Surface legal basis evidence in readiness UI`
- `d585752 Add legal source review board to filing centre`
- `91158b3 Add production audit evidence pack`
- `a73e781 Add operations evidence pack`
- `c44f78e Add accountant workflow evidence pack`
- `d74f105 Add workbench visual acceptance register`
- `4efb2b1 Polish dense workbench table scanning`
- `42bed69 Extract workflow decision summary primitive`
- `1e9ad58 Align company command centre summary`
- `edb59c1 Add golden verifier manifest evidence`

Backend/accounting-engine progress:

- Production readiness reporting now includes source-law snapshots, source-law
  traceability, source-law maintenance protocol, source-law review ledger, Revenue
  taxonomy ranges, statutory rules coverage, golden filing corpus, golden evidence
  ledger, and golden verifier manifest evidence.
- Golden filing scenarios are represented for micro LTD, small abridged LTD, DAC small,
  CLG charity, and medium/audit-required manual handoff.
- Backend tests prove golden corpus behavior including PDF text markers, iXBRL XML,
  tax computation, notes, filing readiness, signatory gates, CLG charity readiness,
  small abridgement, and medium/auditor handoff behavior.
- Production auditability evidence has been surfaced for who changed what, who approved
  what, generated-output evidence, readiness snapshots, audit integrity checkpoints,
  and named-accountant approval records.
- Accountant walkthrough evidence now includes a scenario-by-route matrix tying every
  seeded golden corpus scenario to each required workbench route, screenshot artifact
  set, decision question, required evidence, release checklist code, and blocking
  sign-off status.
- Operations/security evidence has been added for Sentry/error routing, structured logs,
  dependency policy, controlled migrations, production seed blocking, and backup/restore
  drill reporting.
- The CI production-smoke job now retains backup/restore drill evidence as a
  `postgres-backup-restore-drill` artifact. The restore verifier can emit
  `restore-drill-report.json` with the backup filename, sha256, source/verify database
  names, table count comparisons, and completion timestamp, alongside the `.dump` and
  `.dump.sha256` files generated in the ephemeral CI stack.
- Production smoke can now opt into a controlled monitoring error-routing check. The
  `/api/system/monitoring/error-smoke` endpoint is disabled by default, POST-only,
  CSRF-protected, Owner-only, and emits a fixed non-PII `MonitoringSmokeException`
  through `IErrorReporter`; `smoke-production.ps1 -CheckMonitoringErrorRouting`
  records `monitoring-error-routing-report.json`, and CI uploads it as the
  `monitoring-error-routing-smoke` artifact from the ephemeral production stack.
- CI now captures API JSON console output after the monitoring smoke check, runs
  `scripts/verify-structured-logs.ps1` to prove timestamp/level/category fields and
  the monitoring-smoke correlation id, and uploads `api-structured.log` plus
  `structured-log-report.json` as the `structured-json-log-sample` artifact.
- `scripts/verify-production-compose-images.ps1` now accepts `-EvidencePath` and emits
  `production-safety-report.json`, proving CI-promoted images, the dedicated
  `--migrate-only` migration job, API dependence on successful migration completion,
  disabled normal-startup migrations, disabled demo seeding, absent migration/seed
  override flags, and bootstrap-owner initial password exposure only to the migration
  job. CI uploads this as the `production-safety-config` artifact.
- `scripts/write-dependency-evidence.ps1` now writes `dependency-audit-report.json`
  from the release npm audit JSON, frontend lockfile/package hashes, backend NuGet
  audit policy, and CI action hygiene wiring. The frontend CI job runs
  `npm audit --audit-level=moderate --json`, feeds the result to the evidence writer,
  and uploads `npm-audit.json` plus `dependency-audit-report.json` as the
  `dependency-audit-release` artifact.
- Request-scoped EF query filters now backstop tenant isolation across company-owned
  and period-owned child tables, not only the `Companies` set. A metadata regression
  test fails if a required dependent of a filtered principal is left unfiltered.
- Unexpected exceptions caught by `ExceptionMiddleware` are now explicitly passed to
  `IErrorReporter`/`SentryErrorReporter` with HTTP method, request path, and
  correlation id tags before the client receives a safe JSON error response.

Frontend UI/UX progress:

- The accountant workbench has been moved toward a consistent professional product
  experience rather than stitched-together pages.
- Company, period, and filing review command centres now use the shared
  `WorkflowDecisionSummary` primitive for:
  - What is wrong?
  - What is ready?
  - What must I do next?
- Dense workbench tables now have improved scanability, sticky first-column behavior,
  mobile labels, stable scroll affordances, and render coverage.
- Filing review surfaces legal source links, evidence checklist, production decision
  ledger, accountant sign-off packet, external ROS/iXBRL gate, and recorded CRO workflow
  actions.
- Production readiness UI now exposes legal basis evidence, golden corpus evidence,
  release blockers, visual QA coverage, audit evidence, operations evidence, accountant
  workflow evidence, the scenario-route accountant walkthrough evidence matrix, and
  visual acceptance register.

Frontend code/design-system progress:

- Shared workbench primitives now include and are used across routes:
  `PageShell`, `WorkflowRail`, `ReviewPanel`, `EvidenceChecklist`, `DataGrid`,
  `StatusBadge`, `FilingActionBar`, release blocker summary, and
  `WorkflowDecisionSummary`.
- Several route-heavy areas have been extracted into focused workspace components for
  period import, categorise, year-end, adjustments, statements, filing, company detail,
  and production readiness.
- The typed frontend API contract now rejects drift in golden corpus evidence packs,
  legal basis snapshots, golden evidence ledger, golden verifier manifest, source-law
  traceability, accountant acceptance criteria, scenario-route accountant walkthrough
  matrix rows, visual QA coverage, release blockers, release verification manifest,
  audit evidence, and operations evidence.
- A workbench preview route exists and is included in visual QA planning.

## Verification Already Run

Recent successful local verification includes:

- Backend full suite with the pinned .NET 10 SDK installed locally under `.dotnet-sdk/`:
  `dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art` -
  619 passed, 3 skipped
- Backend focused tenant-isolation query-filter tests:
  `dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~TenantIsolation`
  - 3 passed, including child-table filter behavior and required-dependent metadata
  coverage
- Backend focused monitoring/error-routing tests:
  `dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter "FullyQualifiedName~ExceptionMiddleware_LogsCorrelationIdAndDoesNotLeakSecretsInProduction|FullyQualifiedName~ProductionMonitoring_IsWiredIntoApiStartupAndProductionCompose|FullyQualifiedName~ProductionSafety_BlocksMissingMonitoringConfigurationOutsideDevelopment"`
  - 3 passed, proving safe client errors, server log correlation, explicit
  Sentry-backed reporter routing, JSON console scope wiring, and production compose
  monitoring environment variables
- Backend focused monitoring-smoke/CI/runbook tests:
  `dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter "FullyQualifiedName~ProductionMonitoring_ErrorSmokeEndpoint|FullyQualifiedName~ProductionMonitoring_IsWiredIntoApiStartupAndProductionCompose|FullyQualifiedName~ContinuousIntegrationWorkflow_RunsBackendFrontendAndProductionConfigGates|FullyQualifiedName~ProductionSmokeRunbook_ExercisesFrontendProxySessionAndOptionalDownloads"`
  - 8 passed, proving the monitoring smoke endpoint is default-off, Owner-only,
  routes a fixed non-PII event through `IErrorReporter`, and that CI/runbook wiring
  retains `monitoring-error-routing-report.json`
- Backend focused structured-log verifier tests:
  `dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter "FullyQualifiedName~StructuredLogVerifier_ParsesJsonLogsAndMatchesMonitoringSmokeEvidence|FullyQualifiedName~ContinuousIntegrationWorkflow_RunsBackendFrontendAndProductionConfigGates|FullyQualifiedName~ProductionSmokeRunbook_ExercisesFrontendProxySessionAndOptionalDownloads"`
  - included in the 45-test focused backend run with `ProductionReadinessReportTests`;
  passed, proving CI/runbook wiring for `verify-structured-logs.ps1`,
  `api-structured.log`, `structured-log-report.json`, and the
  `structured-json-log-sample` artifact
- Backend focused production safety config tests:
  `dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter "FullyQualifiedName~ProductionComposeVerifier_EmitsMigrationAndSeedSafetyEvidence|FullyQualifiedName~ProductionCompose_UsesImmutableImageReferencesInsteadOfBuildContexts|FullyQualifiedName~ContinuousIntegrationWorkflow_RunsBackendFrontendAndProductionConfigGates|FullyQualifiedName~ProductionReadinessReportTests"`
  - 45 passed, proving the production compose verifier emits migration/seed safety
  evidence and the readiness report points migration safety and production seed
  blocking to the `production-safety-config` artifact
- Backend focused dependency evidence tests:
  `dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter "FullyQualifiedName~DependencyEvidenceWriter_RecordsAuditPolicyAndLockfileHashes|FullyQualifiedName~ContinuousIntegrationWorkflow_RunsBackendFrontendAndProductionConfigGates|FullyQualifiedName~ProductionReadinessReportTests"`
  - 44 passed, proving the dependency evidence writer, CI artifact wiring, and
  readiness report all point to `dependency-audit-release`
- Backend focused opening take-on tests:
  `dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter "FullyQualifiedName~OpeningTrialBalanceTakeOn|FullyQualifiedName~FinalOutputs_BlockWhenOpeningTrialBalanceTakeOnDoesNotBalance|FullyQualifiedName~TrialBalance_IncludesReviewedOpeningBalancesAndBankOpeningSide"`
- Backend focused test:
  `dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~ProductionReadinessReport_ExposesFlattenedGoldenVerifierManifestForReleaseEvidence`
- Backend production readiness report suite after adding the accountant walkthrough
  evidence matrix:
  `dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~ProductionReadinessReportTests`
  - 42 passed
- Backend focused CI/runbook evidence tests after retaining backup/restore drill artifacts:
  `dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter "FullyQualifiedName~ContinuousIntegrationWorkflow_RunsBackendFrontendAndProductionConfigGates|FullyQualifiedName~ProductionBackupRunbook_ProvidesDumpRestoreAndVerificationWorkflow"`
  - 2 passed
- CI action policy verifier after adding the backup artifact upload:
  `node scripts/verify-ci-actions.mjs` - passed
- PowerShell parser and local evidence generation for
  `scripts\verify-production-compose-images.ps1 -EvidencePath ...\production-safety-report.json`
  - passed; the report contained `migrateCommand: ["--migrate-only"]`,
  `apiDependsOnMigrate: service_completed_successfully`,
  `startupMigrationOverridePresent: false`, `demoSeedOverridePresent: false`, and
  `bootstrapOwnerPasswordOnlyOnMigrate: true`
- PowerShell parser and actual local npm audit evidence generation for
  `scripts\write-dependency-evidence.ps1` - passed; `npm audit --audit-level=moderate
  --json` reported 0 low/moderate/high/critical vulnerabilities and the generated
  `dependency-audit-report.json` captured `package-lock.json` SHA-256, NuGet audit
  settings and CI action-hygiene wiring
- PowerShell parser check for `scripts\verify-postgres-backup.ps1` - passed
- PowerShell parser check for `scripts\smoke-production.ps1` after adding the
  optional monitoring check - passed
- PowerShell parser and synthetic sample execution for
  `scripts\verify-structured-logs.ps1` - passed; the sample produced
  `structured-log-report.json` with timestamp, level, category, correlation id, and
  `matchedMonitoringSmokeLine: true`
- Frontend unit tests:
  `npm run test:unit` - 103 passed
- Frontend type-check:
  `npx.cmd tsc --noEmit --incremental false` - passed
- Frontend full test gate after adding the accountant walkthrough evidence matrix:
  `npm.cmd test` - 103 node unit tests and 45 render files / 109 render tests, plus
  readiness, proxy, auth, and API-client verifiers
- Frontend production-readiness contract and render checks after changing the
  dependency artifact:
  - `node --test --experimental-strip-types tests/production-readiness-contract.test.mjs`
    - 38 passed
  - `node scripts/verify-api-client.mjs` - passed
  - `npm.cmd run test:render -- production-readiness-workbench` - 1 passed
- Frontend lint:
  `npm.cmd run lint` - passed
- Frontend production build:
  `npm.cmd run build` - passed; Next.js 16.2.10 compiled successfully and generated
  all app routes including `/production-readiness` and `/workbench-preview`
- Frontend dependency audit:
  `npm.cmd audit --audit-level=moderate` - passed, 0 vulnerabilities
- API client verifier after full local gate:
  `node scripts/verify-api-client.mjs` - passed
- CI action policy verifier after all current workflow artifact changes:
  `node scripts/verify-ci-actions.mjs` - passed
- Production readiness render tests:
  `npm run test:render -- production-readiness-panel production-readiness-workbench`
- Visual smoke screenshot artifact generation against the local production compose
  stack, using an in-memory compose override to keep Postgres internal because
  host port `5433` was already occupied by `nexus-postgres`, and to raise the
  local-only API rate-limit ceiling for the screenshot burst:
  `node scripts/visual-smoke.mjs --base-url=http://127.0.0.1:3000 --email=admin@accounts.local --password=LocalAdmin!Accounts-2026-9Qx --output-dir=artifacts/visual-smoke`
  generated 28 screenshots plus `visual-smoke-manifest.json`
- Visual smoke manifest verification:
  `npm run test:visual:verify` - 28 screenshots, 39,392,238 total bytes
- Local operations evidence recorded in
  `Docs/operations/local-operations-evidence-2026-07-08.md`:
  - `node scripts/verify-ci-actions.mjs` - passed
  - `powershell -ExecutionPolicy Bypass -File scripts\verify-production-compose-images.ps1` - passed
  - `npm audit --audit-level=low` - passed, 0 vulnerabilities
  - `dotnet restore Accounts.slnx` from `backend` with the local .NET 10 SDK - passed
  - `scripts\smoke-production.ps1` against `http://127.0.0.1:3000` with `-AllowInsecureHttp` - passed health, CSP/security headers in local mode, login, authenticated session, company list, CSRF logout, and post-logout 401
  - `scripts\backup-postgres.ps1` against the local compose database - produced `%TEMP%\accounts-backup-drill\accounts-20260708-094342.dump`
  - `scripts\verify-postgres-backup.ps1` restored that dump into `accounts_restore_verify`, checked core table counts, and dropped the verification database
- Broad local production gate after the current evidence changes:
  - backend full suite: 619 passed, 3 skipped
  - frontend lint, type-check, moderate audit, full test aggregate, production build,
    API-client verifier, and CI action policy verifier all passed
- Previous committed slices also passed frontend lint, build, render suites, API client
  verification, and npm audit.

CI status:

- GitHub Actions is now starting jobs again. The latest observed `main` run before this
  handoff update passed Workflow Hygiene, Backend, and Production Compose Config, then
  failed in the Frontend job during `test:readiness` because the API-client production
  readiness fixture did not include `goldenVerifierManifest`.
- The local fix in this working tree derives `goldenVerifierManifest` from the mocked
  golden filing corpus in `frontend/scripts/verify-api-client.mjs`; after the current
  production-evidence changes, the broad local gate is green: backend full suite,
  frontend lint, type-check, moderate audit, full test aggregate, production build,
  API-client verifier, and CI action policy verifier all pass locally.
- The next GitHub Actions run is the source of truth for restoring the production stack
  smoke job, because that job was skipped after the frontend failure.

## What Is Left To Do

Highest-priority next steps:

1. Restore a green GitHub Actions run on `main`; the immediate observed blocker was
   frontend production-readiness contract drift, not billing.
2. Rerun the full local production gate before release if more code changes land; the
   current local gate is green on July 8, 2026.
3. Perform and record human visual review of the generated light/dark desktop/mobile
   visual smoke artifact set; the screenshot manifest now verifies locally, but
   named visual QA sign-off is still required.
4. Complete and retain the actual named accountant walkthrough across the golden corpus:
   micro LTD, small abridged LTD, DAC small, CLG charity, and medium/audit-required
   manual handoff. The code now emits and verifies the scenario-by-route walkthrough
   evidence matrix, but the human walkthrough note is still missing.
5. Record qualified-accountant acceptance evidence for outputs, gates, wording,
   legal/source evidence, visual workflow, and manual handoff behavior.
6. Promote local operations evidence into release-grade evidence:
   real Sentry/provider event confirmation. CI is now configured to retain
   backup/restore drill evidence, monitoring error-routing smoke evidence, a structured
   JSON log sample, production safety config evidence, and dependency audit evidence,
   but the next GitHub Actions run must prove those artifacts on the release commit
   before real filing use.
7. Continue UI polish route by route, especially any surfaces that still feel too
   card-heavy, too sparse, inconsistent in dark mode, or not dense enough for daily
   accountant use.
8. Keep extracting route-heavy frontend code into focused workflow components only when
   it reduces real complexity or improves testable reuse.

## Estimated Completion

As of July 8, 2026:

- Code implementation is roughly 70-75% complete.
- Production assurance is roughly 60-65% complete.
- Overall goal is roughly 63-67% complete, with about one third left.

The remaining third is not just coding. It is proof: full CI, human visual QA
review, operations drills, accountant walkthrough, and named professional sign-off.

## Claude Continuation Instruction

When opening a Claude session, continue from this section:

**[Active Goal Handoff](AGENTS.md#active-goal-handoff)**

Do not restart the project plan. Inspect the current worktree and latest commits, then
continue with the highest-priority unfinished item above.
