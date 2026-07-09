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
- `9768a1c Add production readiness evidence gates`
- `baade7e Add release evidence signoff templates`
- `dfa9c28 Record latest release evidence CI status`
- `52b1a5c Add production readiness scorecard`
- `201975c Add release evidence verifier`
- `8e34953 Canonicalize release acceptance coverage`
- `f142912 Add external validation release evidence`
- `ba6b52d Add release artifact pack checksums`
- `1565113 Retain no-direct filing evidence in CI`
- `18f25d0 Retain production readiness report evidence`
- `00bd8d1 Verify production readiness report evidence`
- `0fd3ff1 Require visual smoke PNG dimension evidence`
- `86efd7e Require visual dimensions in evidence packs`
- `660b9ea Harden release evidence formats`
- `4d558bb Verify release evidence candidate identity`
- `a92ecd2 Retain release evidence templates in artifact packs`
- `26b9f75 Record release template retention handoff`
- `3e53b47 Require visual smoke nonblank pixel evidence`
- `a7cc5cc Require visual QA metric signoff evidence`
- `923e163 Require explicit accountant acceptance rows`
- `9c32438 Require accepted external ROS evidence rows`
- `76c9396 Require accepted manual handoff evidence rows`
- `a3fb293 Require accepted source-law review rows`
- `a6fd245 Require accepted monitoring provider evidence`
- `f5dba82 Require visual layout pass evidence`
- `4ef3e63 Require visual contrast smoke evidence`
- `bd5454c Skip unresolved gradient backgrounds in contrast smoke`
- `f51ae46 Ignore gradient backgrounds in contrast sampler`
- `425fc33 Limit contrast smoke to non-interactive text`
- `d018e37 Verify visual route expected text evidence`
- `0591ded Require accountant route evidence references`
- `87ab3d1 Require taxonomy package validation evidence`

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
- Release evidence templates now exist for the remaining named human gates:
  visual QA sign-off, qualified-accountant acceptance, and monitoring-provider
  confirmation. The production runbook links those templates so release evidence is
  captured consistently rather than as ad hoc notes.
- The production readiness report now exposes a first-class production scorecard for
  the active goal categories: architecture/documentation, backend statutory/accounting
  engine, frontend accountant workbench, and security/auth/tenant/platform guardrails.
  Each category carries current/target points, current evidence, remaining gaps,
  completion-track links, and live release-blocker links.
- `scripts/verify-release-evidence.ps1` now makes the remaining manual evidence gates
  machine-checkable. It fails blank or incomplete source-law review, visual QA,
  qualified-accountant, and monitoring-provider confirmation templates; it writes
  `release-evidence-report.json` for both failed and passed checks.
- `scripts/verify-release-evidence.ps1` now rejects completed human evidence when
  release candidate identity, UTC timestamps, SHA-256 digests, external iXBRL
  artifact hashes, or monitoring log confirmation fields are malformed.
- `Docs/release-evidence/visual-qa-signoff-template.md` and
  `scripts/verify-release-evidence.ps1` now require named visual QA reviewers to
  record minimum PNG IDAT byte size, sampled pixel count, sampled distinct color
  count, and luminance range from `visual-smoke-evidence-report.json`; the verifier
  rejects visual QA evidence if the color count is below 4 or luminance range is
  below 10.
- `scripts/verify-release-evidence.ps1` now parses release evidence fields using
  same-line horizontal whitespace only, so blank fields cannot accidentally consume
  the next line as their value under strict mode.
- `scripts/verify-release-evidence.ps1` now emits a single release candidate identity
  across all six human evidence templates, and `scripts/verify-release-artifact-pack.ps1`
  rejects a release pack if `release-evidence-report.json` does not match the pack
  `CommitSha` and `GitHubActionsRunUrl`.
- `scripts/verify-release-evidence.ps1` now emits SHA-256/byte-size manifest entries
  for all six human release-evidence templates, and `scripts/verify-release-artifact-pack.ps1`
  rejects release packs unless those completed Markdown templates are retained beside
  `release-evidence-report.json` with matching hashes.
- Source-law review now has a checked-in release evidence template covering all 12
  monitored CRO, Revenue, FRC, and Charities Regulator source IDs. The release
  verifier reports those IDs under `sourceLawSourceIds`.
- The qualified-accountant acceptance template and verifier now use the canonical
  golden corpus scenario codes (`micro-ltd`, `small-abridged-ltd`, `dac-small`,
  `clg-charity`, and `medium-audit-required`) and the verifier report emits
  required scenario, route, and release-artifact coverage.
- The qualified-accountant acceptance template and verifier now require explicit
  `accepted` scenario decisions plus `yes`/`accepted` route decision and evidence
  acceptance rows, so arbitrary non-empty notes cannot satisfy accountant acceptance.
- External ROS/iXBRL validation now has a checked-in release evidence template and
  verifier coverage for every canonical golden corpus scenario, so internal XML
  checks cannot be mistaken for Revenue acceptance evidence.
- The external ROS/iXBRL validation template and verifier now require every golden
  corpus scenario row to record a real external validation reference, a SHA-256
  artifact hash, accepted/remediated warnings or errors, and an explicit accepted
  scenario decision.
- The manual handoff acceptance template and verifier now require the
  `medium-audit-required` scenario to carry retained auditor evidence, manual
  handoff note, filing readiness snapshot and accepted decision references, and
  every unsupported path row to carry a real blocking-evidence reference plus an
  accepted reviewer decision.
- The source-law review template and verifier now require every monitored source
  row to record URL reachability, effective-date review, guidance wording
  comparison, platform impact classification and an explicit accepted decision.
- The monitoring-provider confirmation template and verifier now require real
  provider, event, correlation and provider-event references, an HTTPS provider
  base URL, a matched structured-log smoke line and an explicit accepted operator
  decision before monitoring evidence can pass.
- `scripts/verify-no-direct-filing-submission.ps1` now emits
  `no-direct-filing-submission-report.json`, proving release candidates still have no
  outbound CRO/ROS submission client or submit route and only record external filing
  workflow states. CI now runs this verifier in the production stack smoke job and
  uploads the `no-direct-filing-submission-control` artifact for each candidate.
- `node scripts/verify-visual-smoke-artifacts.mjs` now emits
  `visual-smoke-evidence-report.json`, proving the visual smoke artifact has the full
  route/theme/viewport matrix plus matching screenshot byte sizes, SHA-256 hashes,
  PNG dimensions, PNG image-data bytes, sampled pixel counts, distinct color buckets,
  luminance range, and per-screenshot passed layout-check results before a named
  human reviewer signs off.
- `node scripts/verify-accountant-workbench-evidence.mjs` now emits
  `accountant-workbench-evidence-report.json`, proving each accountant workbench route
  has workflow-stage, route-key, review-check, theme, viewport, screenshot,
  per-screenshot layout-check pass evidence and qualified-accountant route acceptance
  evidence before named visual QA sign-off.
- `scripts/verify-release-artifact-pack.ps1` now emits a release artifact pack
  manifest with optional release candidate identity plus per-report SHA-256 and
  byte-size evidence, so retained dependency, production safety, monitoring,
  structured log, backup/restore, no-direct-submission, production-readiness
  verification, visual smoke, accountant-workbench, and release-evidence reports
  can be tied to the exact release candidate.
- The release artifact pack verifier and CI machine evidence pack verifier now reject
  visual evidence packs unless `visual-smoke-evidence-report.json` carries the planned
  desktop/mobile PNG viewport dimensions and every screenshot summary records matching
  width, minimum height, retained PNG image data, sampled pixel count, distinct color
  diversity, luminance-range evidence, and passed console-error, horizontal-overflow
  and visible-text-overlap layout-check results.
- `scripts/smoke-production.ps1` now captures `production-readiness-report.json`
  from the live authenticated smoke stack, and CI uploads it as the
  `production-readiness-report` artifact so the exact candidate scorecard,
  source-law snapshot, golden corpus, release blockers and visual QA contract are
  retained with release evidence.
- `scripts/verify-production-readiness-report.ps1` now emits
  `production-readiness-verification-report.json`, proving the captured live
  readiness report carries complete source-law, golden-corpus, scorecard,
  release-blocker, visual-QA, assurance-packet and release-manifest coverage.
- `scripts/verify-ci-machine-evidence-pack.ps1` now emits
  `ci-machine-evidence-pack-report.json`, proving CI retained exact commit/run
  identity plus SHA-256 inventory for dependency, safety, monitoring, structured
  log, backup/restore, no-direct, readiness and visual/workbench evidence before
  the remaining human release evidence is completed.

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
- The frontend parser now rejects visual QA protocols that omit
  `visual-smoke-evidence-report.json` from required evidence.
- The frontend parser now rejects visual QA protocols that omit screenshot nonblank
  pixel diversity evidence, so a release report cannot treat a structurally valid but
  blank PNG as sufficient visual QA proof.
- Visual smoke and accountant-workbench evidence reports now carry per-screenshot
  passed layout-check results for browser console errors, page-level horizontal
  overflow and visible text overlap; release artifact-pack verifiers reject stale
  visual evidence that omits those pass results.
- The frontend parser now requires the release verification manifest to include the
  CI machine evidence pack, production smoke, readiness verification, visual smoke,
  release artifact pack, and named manual review rows before rendering readiness data.
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
- Backend focused release evidence template tests:
  `dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter "FullyQualifiedName~ReleaseEvidenceTemplates_CoverHumanVisualAccountantAndProviderSignoffs|FullyQualifiedName~ProductionSmokeRunbook_ExercisesFrontendProxySessionAndOptionalDownloads"`
  - 2 passed, proving the manual visual QA, source-law review, external ROS/iXBRL
  validation, qualified-accountant acceptance, manual handoff acceptance, and
  monitoring-provider confirmation templates exist and are linked from the production
  runbook
- Backend focused production scorecard test:
  `dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~ProductionReadinessReport_ExposesGoalScorecardMappedToReleaseBlockers`
  - 1 passed, proving the readiness report exposes the four active goal categories,
  current/target scores, current evidence, remaining gaps, completion-track links, and
  live release-blocker links
- Backend focused release evidence verifier tests:
  `dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter "FullyQualifiedName~ReleaseEvidenceVerifier_BlocksIncompleteHumanSignoffEvidence|FullyQualifiedName~ReleaseEvidenceTemplates_CoverHumanVisualAccountantAndProviderSignoffs|FullyQualifiedName~ProductionReadinessReport_ExposesGoalScorecardMappedToReleaseBlockers"`
  - 3 passed, proving the release evidence verifier, templates, runbook linkage, and
  production scorecard evidence are wired together, including canonical
  qualified-accountant golden corpus scenario codes, external ROS/iXBRL validation
  template coverage, source-law source coverage, manual handoff coverage, visual
  smoke and accountant workbench evidence report references, and the 608/700
  scorecard total
- Backend focused release artifact pack verifier and scorecard tests:
  `dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter "FullyQualifiedName~ReleaseArtifactPackVerifier_RequiresExactOperationalEvidenceReports|FullyQualifiedName~ProductionReadinessReport_ExposesGoalScorecardMappedToReleaseBlockers"`
  - 2 passed, proving `scripts/verify-release-artifact-pack.ps1`,
  `scripts/verify-ci-machine-evidence-pack.ps1`, runbook linkage, exact operational
  report names, PNG dimension evidence checks, and the security/platform scorecard
  evidence are wired together
- Backend focused release artifact pack verifier and scorecard tests after adding
  nonblank visual evidence:
  `dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter "FullyQualifiedName~ReleaseArtifactPackVerifier_RequiresExactOperationalEvidenceReports|FullyQualifiedName~ProductionReadinessReport_ExposesGoalScorecardMappedToReleaseBlockers"`
  - 2 passed, proving the release artifact pack verifier, CI machine evidence pack
  verifier, runbook linkage, nonblank visual-smoke evidence failure strings, and
  609/700 production scorecard are wired together
- Synthetic release artifact and CI machine evidence pack dimension checks:
  temporary evidence reports passed both `scripts\verify-release-artifact-pack.ps1`
  and `scripts\verify-ci-machine-evidence-pack.ps1`; removing
  `visual-smoke-evidence-report.json.viewportDimensions` then failed both verifiers as
  expected.
- Backend focused no-direct submission verifier and scorecard tests:
  `dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter "FullyQualifiedName~NoDirectFilingSubmissionVerifier_ProvesRecordedWorkflowStateOnlyControl|FullyQualifiedName~ProductionReadinessReport_ExposesGoalScorecardMappedToReleaseBlockers"`
  - 2 passed, proving the no-direct CRO/ROS submission verifier, runbook linkage,
  recorded-workflow-state evidence, forbidden outbound client patterns, and scorecard
  evidence are wired together
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
- PowerShell parser, expected draft failure, and synthetic completed-template success
  execution for `scripts\verify-release-evidence.ps1` - passed. Blank templates fail
  with a failed JSON report; temporary filled templates pass with
  `release-evidence-report.json`. The report now carries required golden corpus
  scenario, external ROS/iXBRL validation scenario, source-law source, route, and
  release-artifact coverage.
- Release evidence format hardening checks for `scripts\verify-release-evidence.ps1` -
  passed. Temporary completed templates with a 40-character commit SHA, GitHub
  Actions run URL, UTC timestamps, SHA-256 source/iXBRL hashes, monitoring log
  count and matched-smoke confirmation passed; a copied template with malformed
  external iXBRL SHA-256 values failed as expected.
- Release evidence candidate identity checks - passed. Temporary completed templates
  with the same commit SHA and GitHub Actions run URL passed; a copied template set
  with a mismatched monitoring-provider run URL failed; a synthetic release artifact
  pack passed when `release-evidence-report.json` matched `-CommitSha` and
  `-GitHubActionsRunUrl`, then failed when the pack commit SHA was changed.
- Release evidence retained-template checks - passed. A temporary completed release
  evidence directory produced `release-evidence-report.json` with six template
  SHA-256/byte-size manifest entries; a synthetic release artifact pack passed only
  when all six completed Markdown templates were retained with matching hashes, and
  failed when `visual-qa-signoff-template.md` was removed.
- PowerShell parser and synthetic completed-pack execution for
  `scripts\verify-release-artifact-pack.ps1` - passed. Temporary artifact reports pass
  with `release-artifact-pack-report.json`, including release candidate identity,
  per-report and release-evidence-template SHA-256/byte-size manifest, monitoring
  correlation matching, backup sha256/table checks, no-direct route coverage, visual
  smoke coverage, accountant-workbench coverage, and release-evidence required coverage.
- PowerShell parser and execution for `scripts\verify-no-direct-filing-submission.ps1`
  - passed; the generated `no-direct-filing-submission-report.json` records allowed
  filing workflow routes, forbidden outbound submission patterns, forbidden submit
  route patterns, and zero failures.
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
    - 41 passed, including production scorecard schema and invariant checks
  - `node scripts/verify-api-client.mjs` - passed
  - `npm.cmd run test:render -- production-readiness-workbench` - 1 passed
- Frontend visual smoke evidence report checks:
  `node --test tests/accountant-workbench-evidence.test.mjs tests/visual-smoke-plan.test.mjs tests/visual-smoke-artifacts.test.mjs tests/production-readiness-contract.test.mjs`
  - passed, proving the 28-screenshot matrix, verifier report generation,
  PNG dimension checks, accountant workbench evidence report generation, duplicate
  coverage rejection, parser invariants, and scorecard contract
- Frontend focused visual evidence hardening checks after adding PNG dimension
  validation:
  `node --test tests/visual-smoke-artifacts.test.mjs tests/visual-smoke-plan.test.mjs tests/production-readiness-contract.test.mjs`
  - 58 passed, proving the visual smoke verifier rejects non-PNG screenshots, wrong
  viewport widths and missing screenshot PNG dimension evidence
- Frontend focused visual evidence hardening checks after adding nonblank PNG pixel
  evidence:
  `node --test tests/visual-smoke-artifacts.test.mjs tests/visual-smoke-plan.test.mjs`
  - 11 passed, proving the visual smoke verifier inflates PNG image data, samples
  pixels after PNG row filters, emits IDAT/sample/color/luminance evidence, and
  rejects structurally valid but visually blank screenshots
- Frontend production-readiness contract and render checks after adding nonblank PNG
  pixel evidence:
  - `node --test --experimental-strip-types tests/production-readiness-contract.test.mjs`
    - 49 passed
  - `node scripts/verify-api-client.mjs` - passed
  - `npm.cmd run test:render -- production-readiness-workbench production-readiness-panel`
    - 2 passed
  - `npx.cmd tsc --noEmit --incremental false` - passed
- PowerShell parser checks for `scripts\verify-release-artifact-pack.ps1` and
  `scripts\verify-ci-machine-evidence-pack.ps1` after adding visual nonblank evidence
  requirements - passed
- Release evidence verifier visual metric sign-off checks:
  - PowerShell parser check for `scripts\verify-release-evidence.ps1` - passed
  - Temporary completed copies of all six release-evidence templates passed
    `scripts\verify-release-evidence.ps1` after adding the visual metric fields
  - A copied visual QA template with `Minimum screenshot luminance range: 9` failed
    as expected
  - Draft checked-in templates still fail as expected and emit a failed
    `release-evidence-report.json`
- Backend focused release evidence/scorecard tests after adding visual metric
  sign-off evidence:
  `dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter "FullyQualifiedName~ReleaseEvidenceTemplates_CoverHumanVisualAccountantAndProviderSignoffs|FullyQualifiedName~ReleaseEvidenceVerifier_BlocksIncompleteHumanSignoffEvidence|FullyQualifiedName~ProductionReadinessReport_ExposesGoalScorecardMappedToReleaseBlockers"`
  - 3 passed, proving the visual QA template, release evidence verifier, strict
  field parsing, and 610/700 production scorecard are wired together
- Frontend scorecard contract checks after adding visual metric sign-off evidence:
  - `node --test --experimental-strip-types tests/production-readiness-contract.test.mjs`
    - 49 passed
  - `node scripts/verify-api-client.mjs` - passed
  - `npm.cmd run test:render -- production-readiness-workbench production-readiness-panel`
    - 2 passed
  - `npx.cmd tsc --noEmit --incremental false` - passed
- Release evidence verifier qualified-accountant acceptance checks:
  - Temporary completed copies of all six release-evidence templates passed
    `scripts\verify-release-evidence.ps1` with explicit accepted scenario and route
    evidence rows
  - A copied qualified-accountant acceptance template with `dashboard | no |
    accepted | needs rework` failed as expected on the route decision column
- Backend focused release evidence/scorecard tests after adding explicit accountant
  acceptance rows:
  `dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter "FullyQualifiedName~ReleaseEvidenceTemplates_CoverHumanVisualAccountantAndProviderSignoffs|FullyQualifiedName~ReleaseEvidenceVerifier_BlocksIncompleteHumanSignoffEvidence|FullyQualifiedName~ProductionReadinessReport_ExposesGoalScorecardMappedToReleaseBlockers"`
  - 3 passed, proving the qualified-accountant template, release evidence verifier,
  accepted-row checks, and 611/700 production scorecard are wired together
- Frontend scorecard contract checks after adding explicit accountant acceptance rows:
  - `node --test --experimental-strip-types tests/production-readiness-contract.test.mjs`
    - 49 passed
  - `node scripts/verify-api-client.mjs` - passed
  - `npm.cmd run test:render -- production-readiness-workbench production-readiness-panel`
    - 2 passed
  - `npx.cmd tsc --noEmit --incremental false` - passed
- Release evidence verifier external ROS/iXBRL accepted-row checks:
  - PowerShell parser check for `scripts\verify-release-evidence.ps1` - passed
  - Temporary completed copies of all six release-evidence templates passed
    `scripts\verify-release-evidence.ps1` with real external validation references,
    SHA-256 artifact hashes, `none` warnings/errors, and explicit `accepted`
    decisions for every golden corpus scenario
  - A copied external ROS/iXBRL validation template with `unresolved` warnings/errors
    and `pending` decision for `micro-ltd` failed as expected
- Backend focused release evidence/scorecard tests after adding accepted external
  ROS/iXBRL rows:
  `dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter "FullyQualifiedName~ReleaseEvidenceTemplates_CoverHumanVisualAccountantAndProviderSignoffs|FullyQualifiedName~ReleaseEvidenceVerifier_BlocksIncompleteHumanSignoffEvidence|FullyQualifiedName~ProductionReadinessReport_ExposesGoalScorecardMappedToReleaseBlockers"`
  - 3 passed, proving the external ROS/iXBRL template, release evidence verifier,
  accepted-row checks, and 612/700 production scorecard are wired together
- Frontend scorecard contract checks after adding accepted external ROS/iXBRL rows:
  - `node --test tests/production-readiness-contract.test.mjs` - 49 passed
  - `node scripts/verify-api-client.mjs` - passed
  - `npx.cmd vitest run tests/render/production-readiness-panel.test.tsx tests/render/production-readiness-workbench.test.tsx`
    - 2 passed
  - `npx.cmd tsc --noEmit --incremental false` - passed
- Release evidence verifier manual handoff accepted-row checks:
  - PowerShell parser check for `scripts\verify-release-evidence.ps1` - passed
  - Temporary completed copies of all six release-evidence templates passed
    `scripts\verify-release-evidence.ps1` with real auditor-report, manual-handoff
    note, filing-readiness snapshot and unsupported-path blocker references plus
    explicit `accepted` decisions
  - A copied manual handoff template with a `pending` manual handoff note and
    `pending` scenario decision failed as expected
- Backend focused release evidence/scorecard tests after adding accepted manual
  handoff rows:
  `dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter "FullyQualifiedName~ReleaseEvidenceTemplates_CoverHumanVisualAccountantAndProviderSignoffs|FullyQualifiedName~ReleaseEvidenceVerifier_BlocksIncompleteHumanSignoffEvidence|FullyQualifiedName~ProductionReadinessReport_ExposesGoalScorecardMappedToReleaseBlockers"`
  - 3 passed, proving the manual handoff template, release evidence verifier,
  accepted-row checks, and 613/700 production scorecard are wired together
- Frontend scorecard contract checks after adding accepted manual handoff rows:
  - `node --test tests/production-readiness-contract.test.mjs` - 49 passed
  - `node scripts/verify-api-client.mjs` - passed
  - `npx.cmd vitest run tests/render/production-readiness-panel.test.tsx tests/render/production-readiness-workbench.test.tsx`
    - 2 passed
  - `npx.cmd tsc --noEmit --incremental false` - passed
- Release evidence verifier source-law accepted-row checks:
  - PowerShell parser check for `scripts\verify-release-evidence.ps1` - passed
  - Temporary completed copies of all six release-evidence templates passed
    `scripts\verify-release-evidence.ps1` with URL reachability, effective-date
    review, guidance wording comparison, platform impact classification and
    explicit `accepted` decisions for every monitored source
  - A copied source-law review template with `pending` URL/effective-date/decision
    and invalid impact wording for `revenue-accepted-taxonomies` failed as expected
- Backend focused release evidence/scorecard tests after adding accepted source-law
  rows:
  `dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter "FullyQualifiedName~ReleaseEvidenceTemplates_CoverHumanVisualAccountantAndProviderSignoffs|FullyQualifiedName~ReleaseEvidenceVerifier_BlocksIncompleteHumanSignoffEvidence|FullyQualifiedName~ProductionReadinessReport_ExposesGoalScorecardMappedToReleaseBlockers"`
  - 3 passed, proving the source-law review template, release evidence verifier,
  accepted-row checks, and 614/700 production scorecard are wired together
- Frontend scorecard contract checks after adding accepted source-law rows:
  - `node --test tests/production-readiness-contract.test.mjs` - 49 passed
  - `node scripts/verify-api-client.mjs` - passed
  - `npx.cmd vitest run tests/render/production-readiness-panel.test.tsx tests/render/production-readiness-workbench.test.tsx`
    - 2 passed
  - `npx.cmd tsc --noEmit --incremental false` - passed
- Release evidence verifier monitoring-provider accepted-evidence checks:
  - PowerShell parser check for `scripts\verify-release-evidence.ps1` - passed
  - Temporary completed copies of all six release-evidence templates passed
    `scripts\verify-release-evidence.ps1` with real provider/event/correlation
    references, HTTPS provider base URL, matched structured-log smoke line and an
    explicit accepted operator decision
  - A copied monitoring-provider confirmation template with an HTTP base URL,
    placeholder provider-event reference and unchecked accepted decision failed as
    expected
- Backend focused release evidence/scorecard tests after adding accepted monitoring
  provider evidence:
  `dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter "FullyQualifiedName~ReleaseEvidenceTemplates_CoverHumanVisualAccountantAndProviderSignoffs|FullyQualifiedName~ReleaseEvidenceVerifier_BlocksIncompleteHumanSignoffEvidence|FullyQualifiedName~ProductionReadinessReport_ExposesGoalScorecardMappedToReleaseBlockers"`
  - 3 passed, proving the monitoring-provider template, release evidence verifier,
  accepted operator decision, provider reference checks, and 615/700 production
  scorecard are wired together
- Frontend scorecard contract checks after adding accepted monitoring-provider
  evidence:
  - `node --test tests/production-readiness-contract.test.mjs` - 49 passed
  - `node scripts/verify-api-client.mjs` - passed
  - `npx.cmd vitest run tests/render/production-readiness-panel.test.tsx tests/render/production-readiness-workbench.test.tsx`
    - 2 passed
  - `npx.cmd tsc --noEmit --incremental false` - passed
- Frontend visual layout pass evidence checks after `f5dba82`:
  - PowerShell parser checks for `scripts\verify-release-artifact-pack.ps1` and
    `scripts\verify-ci-machine-evidence-pack.ps1` - passed
  - `node --test tests/visual-smoke-artifacts.test.mjs tests/accountant-workbench-evidence.test.mjs tests/visual-smoke-plan.test.mjs`
    - 17 passed, proving visual-smoke manifests and accountant-workbench evidence
      reject screenshots without passed console-error, page-overflow and text-overlap
      layout-check results
  - `node --test tests/production-readiness-contract.test.mjs` - 49 passed
  - `node scripts/verify-api-client.mjs` - passed
  - `npx.cmd vitest run tests/render/production-readiness-panel.test.tsx tests/render/production-readiness-workbench.test.tsx`
    - 2 passed
  - `npx.cmd tsc --noEmit --incremental false` - passed
  - Backend focused scorecard/release-artifact regression:
    `dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter "FullyQualifiedName~ProductionReadinessReport_ExposesGoalScorecardMappedToReleaseBlockers|FullyQualifiedName~ReleaseArtifactPackVerifier_RequiresExactOperationalEvidenceReports"`
    - 2 passed, proving the 617/700 scorecard and release-pack verifier layout-pass
      checks are wired together
- Frontend visual contrast smoke evidence checks:
  - PowerShell parser checks for `scripts\verify-release-evidence.ps1`,
    `scripts\verify-release-artifact-pack.ps1` and
    `scripts\verify-ci-machine-evidence-pack.ps1` - passed
  - `node --test tests/visual-smoke-artifacts.test.mjs tests/accountant-workbench-evidence.test.mjs tests/visual-smoke-plan.test.mjs tests/production-readiness-contract.test.mjs`
    - 69 passed, proving visual-smoke manifests, accountant-workbench evidence,
      production-readiness parser invariants and scorecard fixtures reject missing
      automated theme-contrast smoke evidence
  - `node scripts/verify-api-client.mjs` - passed
  - `npx.cmd vitest run tests/render/production-readiness-panel.test.tsx tests/render/production-readiness-workbench.test.tsx`
    - 2 passed
  - `npx.cmd tsc --noEmit --incremental false` - passed
  - Local refreshed visual evidence:
    `node scripts/verify-visual-smoke-artifacts.mjs` and
    `node scripts/verify-accountant-workbench-evidence.mjs`
    - passed with 28 screenshots, 84 layout-check pass results and 28
      per-screenshot `theme-contrast` pass results
  - Backend focused scorecard/release evidence regression:
    `dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter "FullyQualifiedName~ProductionReadinessReport_ExposesGoalScorecardMappedToReleaseBlockers|FullyQualifiedName~ReleaseArtifactPackVerifier_RequiresExactOperationalEvidenceReports|FullyQualifiedName~ReleaseEvidenceVerifier_BlocksIncompleteHumanSignoffEvidence|FullyQualifiedName~ReleaseEvidenceTemplates_CoverHumanVisualAccountantAndProviderSignoffs"`
    - 4 passed, proving the 619/700 scorecard, visual QA template contrast field,
      release-evidence verifier and artifact-pack contrast checks are wired together
- Frontend visual route expected-text evidence checks:
  - PowerShell parser checks for `scripts\verify-release-artifact-pack.ps1` and
    `scripts\verify-ci-machine-evidence-pack.ps1` - passed
  - `node --test tests/visual-smoke-artifacts.test.mjs tests/accountant-workbench-evidence.test.mjs tests/production-readiness-contract.test.mjs`
    - 69 passed, proving visual-smoke screenshots retain each planned route's
      expected accountant decision text and the accountant-workbench evidence
      report rejects missing or drifted route text proof
  - `node scripts/verify-visual-smoke-artifacts.mjs` and
    `node scripts/verify-accountant-workbench-evidence.mjs`
    - passed locally with 28 screenshots and `expectedTextEvidenceCount: 4`
      for all seven workbench routes
  - `node scripts/verify-api-client.mjs`,
    `npx.cmd vitest run tests/render/production-readiness-panel.test.tsx tests/render/production-readiness-workbench.test.tsx`,
    and `npx.cmd tsc --noEmit --incremental false` - passed
  - Backend focused scorecard/release evidence regression:
    `dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter "FullyQualifiedName~ProductionReadinessReport_ExposesGoalScorecardMappedToReleaseBlockers|FullyQualifiedName~ReleaseArtifactPackVerifier_RequiresExactOperationalEvidenceReports|FullyQualifiedName~ReleaseEvidenceVerifier_BlocksIncompleteHumanSignoffEvidence|FullyQualifiedName~ReleaseEvidenceTemplates_CoverHumanVisualAccountantAndProviderSignoffs"`
    - 4 passed, proving the 621/700 scorecard, frontend 168/200 category,
      route expected-text evidence, and release-pack checks are wired together
- Backend qualified-accountant route evidence reference checks:
  - PowerShell parser check for `scripts\verify-release-evidence.ps1` - passed
  - Backend focused release-evidence/scorecard regression:
    `dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter "FullyQualifiedName~ReleaseEvidenceVerifier_BlocksIncompleteHumanSignoffEvidence|FullyQualifiedName~ReleaseEvidenceTemplates_CoverHumanVisualAccountantAndProviderSignoffs|FullyQualifiedName~ProductionReadinessReport_ExposesGoalScorecardMappedToReleaseBlockers"`
    - 3 passed, proving the qualified-accountant acceptance template and verifier
      now require a real retained workbench evidence reference for every accepted
      route and the backend scorecard is 206/250
  - `node --test tests/production-readiness-contract.test.mjs`,
    `node scripts/verify-api-client.mjs`,
    `npx.cmd vitest run tests/render/production-readiness-panel.test.tsx tests/render/production-readiness-workbench.test.tsx`,
    and `npx.cmd tsc --noEmit --incremental false` - passed
- Backend external ROS/iXBRL taxonomy package evidence checks:
  - PowerShell parser check for `scripts\verify-release-evidence.ps1` - passed
  - Backend focused release-evidence/scorecard regression:
    `dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter "FullyQualifiedName~ReleaseEvidenceVerifier_BlocksIncompleteHumanSignoffEvidence|FullyQualifiedName~ReleaseEvidenceTemplates_CoverHumanVisualAccountantAndProviderSignoffs|FullyQualifiedName~ProductionReadinessReport_ExposesGoalScorecardMappedToReleaseBlockers"`
    - 3 passed, proving the external ROS/iXBRL template and verifier require
      retained taxonomy package references for every golden corpus scenario and
      the backend scorecard is 208/250
  - `node --test tests/production-readiness-contract.test.mjs`,
    `node scripts/verify-api-client.mjs`,
    `npx.cmd vitest run tests/render/production-readiness-panel.test.tsx tests/render/production-readiness-workbench.test.tsx`,
    and `npx.cmd tsc --noEmit --incremental false` - passed
- Frontend visual QA route acceptance decision checks:
  - Commit `aa24207 Require visual QA route acceptance decisions`.
  - PowerShell parser check for `scripts\verify-release-evidence.ps1` - passed.
  - Backend focused release-evidence/scorecard regression:
    `dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter "FullyQualifiedName~ReleaseEvidenceVerifier_BlocksIncompleteHumanSignoffEvidence|FullyQualifiedName~ReleaseEvidenceTemplates_CoverHumanVisualAccountantAndProviderSignoffs|FullyQualifiedName~ProductionReadinessReport_ExposesGoalScorecardMappedToReleaseBlockers"`
    - 3 passed, proving visual QA sign-off now requires explicit `pass` or
      `accepted` route decisions across desktop light, desktop dark, mobile light
      and mobile dark captures, and the frontend scorecard is 170/200.
  - `node --test tests/production-readiness-contract.test.mjs`,
    `node scripts/verify-api-client.mjs`,
    `npx.cmd vitest run tests/render/production-readiness-panel.test.tsx tests/render/production-readiness-workbench.test.tsx`,
    and `npx.cmd tsc --noEmit --incremental false` - passed.
- Backend concrete source-law review row checks:
  - Commit `21b2b3d Require concrete source law review rows`.
  - PowerShell parser check for `scripts\verify-release-evidence.ps1` - passed.
  - Temporary completed release-evidence pack smoke outside the repo:
    - concrete source-law rows passed with `yes`, `YYYY-MM-DD` or `not dated`,
      explicit platform impact and exact `accepted` decisions;
    - copied pack with `revenue-accepted-taxonomies` using generic `accepted` for
      `URL reachable` failed with the expected source-law verifier error.
  - Backend focused release-evidence/scorecard regression:
    `dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter "FullyQualifiedName~ReleaseEvidenceVerifier_BlocksIncompleteHumanSignoffEvidence|FullyQualifiedName~ReleaseEvidenceTemplates_CoverHumanVisualAccountantAndProviderSignoffs|FullyQualifiedName~ProductionReadinessReport_ExposesGoalScorecardMappedToReleaseBlockers"`
    - 3 passed, proving the source-law template/verifier now reject generic
      placeholders in monitored-source review rows and the backend scorecard is
      210/250.
  - `node --test tests/production-readiness-contract.test.mjs`,
    `node scripts/verify-api-client.mjs`,
    `npx.cmd vitest run tests/render/production-readiness-panel.test.tsx tests/render/production-readiness-workbench.test.tsx`,
    and `npx.cmd tsc --noEmit --incremental false` - passed.
- Backend qualified-accountant scenario scope acceptance checks:
  - Commit `760d066 Require accountant scenario scope acceptance`.
  - PowerShell parser check for `scripts\verify-release-evidence.ps1` - passed.
  - Temporary completed release-evidence pack smoke outside the repo:
    - completed qualified-accountant scenario scope rows passed with accepted
      outputs, gates, source-law evidence, wording, workbench journey and decision
      cells;
    - copied pack with `micro-ltd` `Outputs` set to `pending` failed with the
      expected qualified-accountant verifier error.
  - Backend focused release-evidence/scorecard regression:
    `dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter "FullyQualifiedName~ReleaseEvidenceVerifier_BlocksIncompleteHumanSignoffEvidence|FullyQualifiedName~ReleaseEvidenceTemplates_CoverHumanVisualAccountantAndProviderSignoffs|FullyQualifiedName~ProductionReadinessReport_ExposesGoalScorecardMappedToReleaseBlockers"`
    - 3 passed, proving qualified-accountant acceptance now requires explicit
      accepted scenario scope cells for outputs, gates, source-law evidence,
      wording and workbench journey, and the backend scorecard is 212/250.
  - `node --test tests/production-readiness-contract.test.mjs`,
    `node scripts/verify-api-client.mjs`,
    `npx.cmd vitest run tests/render/production-readiness-panel.test.tsx tests/render/production-readiness-workbench.test.tsx`,
    and `npx.cmd tsc --noEmit --incremental false` - passed.
- Backend focused scorecard/visual QA tests after adding PNG dimension evidence:
  `dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter "FullyQualifiedName~ProductionReadinessReport_ExposesGoalScorecardMappedToReleaseBlockers|FullyQualifiedName~ProductionReadinessReport_DeclaresVisualQaCoverageForAccountantWorkbenchRoutes"`
  - 2 passed, proving the readiness report exposes the 608/700 scorecard and visual
  review protocol requires screenshot PNG dimensions
- Frontend scorecard render and type checks for the visual evidence report slice:
  - `node scripts/verify-api-client.mjs` - passed
  - `npm.cmd run test:render -- production-readiness-workbench` - 1 passed
  - `npm.cmd run test:render -- production-readiness-panel` - 1 passed
  - `npx.cmd tsc --noEmit --incremental false` - passed
- Frontend type-check after adding the production scorecard API/UI contract:
  `npx.cmd tsc --noEmit --incremental false` - passed
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

- GitHub Actions run `28956339879` for commit
  `86efd7e Require visual dimensions in evidence packs` completed successfully on
  July 8, 2026.
- GitHub Actions run `28957939046` for commit
  `660b9ea Harden release evidence formats` completed successfully on July 8,
  2026.
- GitHub Actions run `28959364532` for commit
  `4d558bb Verify release evidence candidate identity` completed successfully on
  July 8, 2026.
- GitHub Actions run `28979968332` for commit
  `f66e479 Record visual layout evidence handoff` completed successfully on July 8,
  2026.
- Green jobs: Workflow Hygiene, Production Compose Config, Frontend, Backend,
  Production Stack Smoke, and CI Machine Evidence Pack.
- The scorecard exposed by the candidate is now 669/700, with backend statutory/accounting
  engine at 244/250, frontend accountant workbench at 176/200 and
  security/auth/tenant/platform guardrails at 150/150.
  The typed frontend parser and production-readiness verifier both require CI
  machine evidence, production smoke, readiness verification, visual smoke, release
  artifact pack, no-direct filing control, and named manual review manifest rows;
  the release artifact-pack verifier now also requires the retained readiness
  verification report to prove every required default-CI and manual manifest row,
  and requires all six completed release-evidence Markdown templates to match the
  SHA-256/byte-size manifest emitted by `release-evidence-report.json`.
- Latest retained CI evidence artifacts include:
  - `dependency-audit-release`: `dependency-audit-report.json` passed with 0 npm
    vulnerabilities, frontend lockfile/package hashes, and NuGet audit policy.
  - `production-safety-config`: `production-safety-report.json` passed with
    `--migrate-only`, API dependence on successful migration completion, disabled
    normal-startup migration/seeding, and bootstrap-owner password exposure limited
    to the migration job.
  - `monitoring-error-routing-smoke`: `monitoring-error-routing-report.json`
    passed with provider, event id, and correlation id from the controlled smoke
    event.
  - `structured-json-log-sample`: `structured-log-report.json` passed and matched
    the monitoring-smoke correlation id in JSON console output.
  - `postgres-backup-restore-drill`: `restore-drill-report.json` passed with dump
    sha256 and table-count comparisons.
  - `production-readiness-report`: `production-readiness-report.json` and
    `production-readiness-verification-report.json` were retained from the live
    production smoke stack.
  - `visual-smoke-screenshots`: screenshot artifact, manifest, visual smoke evidence,
    PNG dimension evidence, nonblank pixel diversity evidence, automated
    theme-contrast smoke evidence, and accountant workbench route acceptance
    evidence were retained for human review.
  - `ci-machine-evidence-pack`: `ci-machine-evidence-pack-report.json` passed with
    exact commit/run identity and SHA-256 inventory for the machine-generated
    evidence artifacts; human release evidence is still required separately.

Backend qualified-accountant route walkthrough exact decision checks:

- Commit `0b585d8 Require exact accountant route decisions` tightened
  `scripts/verify-release-evidence.ps1` so qualified-accountant route
  walkthrough rows accept only exact `yes` or `accepted` values for
  `Decision question answered`, and only exact `accepted` for
  `Evidence accepted`.
- The qualified-accountant acceptance template now tells reviewers that ambiguous
  route decision/evidence cells are rejected.
- Verification completed locally:
  - PowerShell parser check for `scripts\verify-release-evidence.ps1` passed.
  - Temporary completed release-evidence pack outside the repo passed; a copied
    qualified-accountant pack with `dashboard` route `Evidence accepted` set to
    `accepted with notes` failed with the expected verifier error.
  - Backend focused regression passed 3 tests:
    `ReleaseEvidenceVerifier_BlocksIncompleteHumanSignoffEvidence`,
    `ReleaseEvidenceTemplates_CoverHumanVisualAccountantAndProviderSignoffs`,
    and
    `ProductionReadinessReport_ExposesGoalScorecardMappedToReleaseBlockers`.
  - Frontend contract/API/render/type checks passed:
    `node --test tests/production-readiness-contract.test.mjs`,
    `node scripts/verify-api-client.mjs`,
    `npx.cmd vitest run tests/render/production-readiness-panel.test.tsx tests/render/production-readiness-workbench.test.tsx`,
    and `npx.cmd tsc --noEmit --incremental false`.

Backend qualified-accountant route evidence anchor checks:

- Commit `07dfe28 Require route-specific accountant evidence anchors` tightened
  `scripts/verify-release-evidence.ps1` so every qualified-accountant route
  walkthrough row must reference its matching
  `accountant-workbench-evidence-report.json#routeAcceptance.<route>` anchor.
- The qualified-accountant acceptance template now tells reviewers the workbench
  evidence reference must match the route key exactly.
- Verification completed locally:
  - PowerShell parser check for `scripts\verify-release-evidence.ps1` passed.
  - Temporary completed release-evidence pack outside the repo passed; a copied
    qualified-accountant pack with the `dashboard` row pointing at
    `accountant-workbench-evidence-report.json#routeAcceptance.company-detail`
    failed with the expected route-specific verifier error.
  - Backend focused regression passed 3 tests:
    `ReleaseEvidenceVerifier_BlocksIncompleteHumanSignoffEvidence`,
    `ReleaseEvidenceTemplates_CoverHumanVisualAccountantAndProviderSignoffs`,
    and
    `ProductionReadinessReport_ExposesGoalScorecardMappedToReleaseBlockers`.
  - Frontend contract/API/render/type checks passed:
    `node --test tests/production-readiness-contract.test.mjs`,
    `node scripts/verify-api-client.mjs`,
    `npx.cmd vitest run tests/render/production-readiness-panel.test.tsx tests/render/production-readiness-workbench.test.tsx`,
    and `npx.cmd tsc --noEmit --incremental false`.

Backend manual handoff row-code evidence checks:

- Commit `305bebf Require manual handoff row evidence codes` tightened
  `scripts/verify-release-evidence.ps1` so manual handoff scenario evidence
  references must include the matching scenario code, and unsupported-path
  evidence references must include the matching path code.
- The manual handoff acceptance template now tells reviewers that retained
  scenario/path evidence references must include the row code, so evidence cannot
  be reused against the wrong manual handoff row.
- Verification completed locally:
  - PowerShell parser check for `scripts\verify-release-evidence.ps1` passed.
  - Temporary completed release-evidence pack outside the repo passed; a copied
    manual handoff pack with `medium-audit-required` removed from the auditor
    evidence reference failed with the expected row-code verifier error.
  - Backend focused regression passed 3 tests:
    `ReleaseEvidenceVerifier_BlocksIncompleteHumanSignoffEvidence`,
    `ReleaseEvidenceTemplates_CoverHumanVisualAccountantAndProviderSignoffs`,
    and
    `ProductionReadinessReport_ExposesGoalScorecardMappedToReleaseBlockers`.
  - Frontend contract/API/render/type checks passed:
    `node --test tests/production-readiness-contract.test.mjs`,
    `node scripts/verify-api-client.mjs`,
    `npx.cmd vitest run tests/render/production-readiness-panel.test.tsx tests/render/production-readiness-workbench.test.tsx`,
    and `npx.cmd tsc --noEmit --incremental false`.

Backend external ROS/iXBRL scenario-code evidence checks:

- Commit `c98dc95 Require scenario-specific external validation evidence` tightened
  `scripts/verify-release-evidence.ps1` so external ROS/iXBRL validation
  references and retained taxonomy package references must include the matching
  golden corpus scenario code.
- The external ROS/iXBRL validation template now tells reviewers scenario
  validation and taxonomy references must include the scenario code so evidence
  cannot be reused against the wrong golden corpus row.
- Verification completed locally:
  - PowerShell parser check for `scripts\verify-release-evidence.ps1` passed.
  - Temporary completed release-evidence pack outside the repo passed; a copied
    external ROS/iXBRL pack with `micro-ltd` removed from the retained taxonomy
    reference failed with the expected row-code verifier error.
  - Backend focused regression passed 3 tests:
    `ReleaseEvidenceVerifier_BlocksIncompleteHumanSignoffEvidence`,
    `ReleaseEvidenceTemplates_CoverHumanVisualAccountantAndProviderSignoffs`,
    and
    `ProductionReadinessReport_ExposesGoalScorecardMappedToReleaseBlockers`.
  - Frontend contract/API/render/type checks passed:
    `node --test tests/production-readiness-contract.test.mjs`,
    `node scripts/verify-api-client.mjs`,
    `npx.cmd vitest run tests/render/production-readiness-panel.test.tsx tests/render/production-readiness-workbench.test.tsx`,
    and `npx.cmd tsc --noEmit --incremental false`.

No-direct release-candidate identity checks:

- This slice tightened `scripts/verify-no-direct-filing-submission.ps1`
  so `no-direct-filing-submission-report.json` must record the release candidate
  commit SHA and GitHub Actions run URL.
- The production smoke CI job now passes `$env:GITHUB_SHA` and the current Actions
  run URL into the no-direct verifier before uploading the
  `no-direct-filing-submission-control` artifact.
- `scripts/verify-release-artifact-pack.ps1` and
  `scripts/verify-ci-machine-evidence-pack.ps1` now reject stale no-direct evidence
  whose release candidate identity is missing or does not match the pack being
  verified.
- Verification completed locally:
  - PowerShell parser checks passed for `scripts\verify-no-direct-filing-submission.ps1`,
    `scripts\verify-release-artifact-pack.ps1`, and
    `scripts\verify-ci-machine-evidence-pack.ps1`.
  - `scripts\verify-no-direct-filing-submission.ps1` passed with a sample commit/run
    identity and wrote `releaseCandidate` fields into the evidence report.
  - The same verifier failed without identity with the expected missing `CommitSha`
    and `GitHubActionsRunUrl` errors.
  - Backend focused regression passed 3 tests:
    `NoDirectFilingSubmissionVerifier_ProvesRecordedWorkflowStateOnlyControl`,
    `ReleaseArtifactPackVerifier_RequiresExactOperationalEvidenceReports`, and
    `ProductionReadinessReport_ExposesGoalScorecardMappedToReleaseBlockers`.
  - Frontend contract/API/render/type checks passed:
    `node --test tests/production-readiness-contract.test.mjs`,
    `node scripts/verify-api-client.mjs`,
    `npx.cmd vitest run tests/render/production-readiness-panel.test.tsx tests/render/production-readiness-workbench.test.tsx`,
    and `npx.cmd tsc --noEmit --incremental false`.
  - `node scripts/verify-ci-actions.mjs` passed after the workflow update.

Backend source-law per-source note evidence checks:

- This slice tightened `scripts/verify-release-evidence.ps1` so every
  source-law review table row must include a real retained `Notes` value and
  that note/reference must include the matching monitored source ID.
- The source-law review template now tells reviewers each `Notes` cell must
  include a retained per-source note or evidence reference containing the
  matching source ID, so source-law review evidence cannot be reused against the
  wrong monitored source row.
- Verification completed locally:
  - PowerShell parser check for `scripts\verify-release-evidence.ps1` passed.
  - Temporary completed release-evidence pack outside the repo passed with
    source-law notes like `source-law-review-ledger#<source-id>` for every
    monitored source.
  - A copied pack with `revenue-accepted-taxonomies` replaced by
    `wrong-source` in that row's Notes cell failed with the expected source-law
    row-code verifier error.
  - Backend focused regression passed 3 tests:
    `ReleaseEvidenceVerifier_BlocksIncompleteHumanSignoffEvidence`,
    `ReleaseEvidenceTemplates_CoverHumanVisualAccountantAndProviderSignoffs`,
    and
    `ProductionReadinessReport_ExposesGoalScorecardMappedToReleaseBlockers`.
  - Frontend contract/API/render/type checks passed:
    `node --test tests/production-readiness-contract.test.mjs`,
    `node scripts/verify-api-client.mjs`,
    `npx.cmd vitest run tests/render/production-readiness-panel.test.tsx tests/render/production-readiness-workbench.test.tsx`,
    and `npx.cmd tsc --noEmit --incremental false`.

Frontend visual QA route-note evidence checks:

- This slice tightened `scripts/verify-release-evidence.ps1` so every visual QA
  route row must include a real retained `Notes` value and that note/reference
  must include the matching route code.
- The visual QA sign-off template now tells reviewers to include a retained
  visual evidence note or reference containing the matching route code in every
  route `Notes` cell, so screenshot review notes cannot be reused against the
  wrong workbench route.
- That slice raised the production scorecard to 643/700, with frontend
  accountant workbench at 172/200.
- Verification completed locally:
  - PowerShell parser check for `scripts\verify-release-evidence.ps1` passed.
  - Temporary completed release-evidence pack outside the repo passed with
    visual route notes like
    `visual-smoke-evidence-report.json#routeAcceptance.<route>` for every
    workbench route.
  - A copied pack with `production-readiness` replaced by `wrong-route` in that
    row's Notes cell failed with the expected visual QA row-code verifier error.
  - Backend focused regression passed 3 tests:
    `ReleaseEvidenceVerifier_BlocksIncompleteHumanSignoffEvidence`,
    `ReleaseEvidenceTemplates_CoverHumanVisualAccountantAndProviderSignoffs`,
    and
    `ProductionReadinessReport_ExposesGoalScorecardMappedToReleaseBlockers`.
  - Frontend contract/API/render/type checks passed:
    `node --test tests/production-readiness-contract.test.mjs`,
    `node scripts/verify-api-client.mjs`,
    `npx.cmd vitest run tests/render/production-readiness-panel.test.tsx tests/render/production-readiness-workbench.test.tsx`,
    and `npx.cmd tsc --noEmit --incremental false`.

Backend qualified-accountant scenario evidence reference checks:

- This slice tightened `scripts/verify-release-evidence.ps1` so every
  qualified-accountant golden corpus scenario row must include a real retained
  `Scenario evidence reference` value and that reference must include the
  matching scenario code.
- The qualified-accountant acceptance template now tells reviewers to include a
  retained scenario walkthrough evidence reference such as
  `qualified-accountant-walkthrough-ledger#micro-ltd` for every accepted
  scenario, so scenario sign-off evidence cannot be reused against the wrong
  golden corpus row.
- That slice raised the production scorecard to 645/700, with backend
  statutory/accounting engine at 224/250.
- Verification completed locally:
  - PowerShell parser check for `scripts\verify-release-evidence.ps1` passed.
  - Temporary completed release-evidence pack outside the repo passed with
    scenario evidence references like
    `qualified-accountant-walkthrough-ledger#<scenario>` for every canonical
    golden corpus scenario.
  - A copied pack with `medium-audit-required` replaced by `wrong-scenario` in
    that scenario's evidence reference failed with the expected
    qualified-accountant row-code verifier error.
  - Backend focused regression passed 3 tests:
    `ReleaseEvidenceVerifier_BlocksIncompleteHumanSignoffEvidence`,
    `ReleaseEvidenceTemplates_CoverHumanVisualAccountantAndProviderSignoffs`,
    and
    `ProductionReadinessReport_ExposesGoalScorecardMappedToReleaseBlockers`.
  - Frontend contract/API/render/type checks passed:
    `node --test tests/production-readiness-contract.test.mjs`,
    `node scripts/verify-api-client.mjs`,
    `npx.cmd vitest run tests/render/production-readiness-panel.test.tsx tests/render/production-readiness-workbench.test.tsx`,
    and `npx.cmd tsc --noEmit --incremental false`.

Backend qualified-accountant route note evidence checks:

- This slice tightened `scripts/verify-release-evidence.ps1` so every
  qualified-accountant route walkthrough row must include a real retained
  `Notes` value and that note/reference must include the matching route code.
- The qualified-accountant acceptance template now tells reviewers to include a
  retained route walkthrough note or reference such as
  `qualified-accountant-route-walkthrough#dashboard` in every route `Notes`
  cell, so route sign-off notes cannot be reused against the wrong workbench
  route.
- That slice raised the production scorecard to 647/700, with backend
  statutory/accounting engine at 226/250.
- Verification completed locally:
  - PowerShell parser check for `scripts\verify-release-evidence.ps1` passed.
  - Temporary completed release-evidence pack outside the repo passed with
    route notes like `qualified-accountant-route-walkthrough#<route>` for every
    workbench route.
  - A copied pack with `production-readiness` replaced by `wrong-route` in that
    route's Notes cell failed with the expected qualified-accountant row-code
    verifier error.
  - Backend focused regression passed 3 tests:
    `ReleaseEvidenceVerifier_BlocksIncompleteHumanSignoffEvidence`,
    `ReleaseEvidenceTemplates_CoverHumanVisualAccountantAndProviderSignoffs`,
    and
    `ProductionReadinessReport_ExposesGoalScorecardMappedToReleaseBlockers`.
  - Frontend contract/API/render/type checks passed:
    `node --test tests/production-readiness-contract.test.mjs`,
    `node scripts/verify-api-client.mjs`,
    `npx.cmd vitest run tests/render/production-readiness-panel.test.tsx tests/render/production-readiness-workbench.test.tsx`,
    and `npx.cmd tsc --noEmit --incremental false`.

Backend manual handoff exact decision checks:

- This slice tightened `scripts/verify-release-evidence.ps1` so manual handoff
  scenario `Decision` cells and unsupported-path `Reviewer decision` cells must
  be exactly `accepted`.
- The manual handoff acceptance template now tells reviewers that ambiguous
  decision text such as `accepted with notes` is rejected, so acceptance
  limitations must live in retained evidence references or notes rather than in
  the decision cell.
- That previous slice moved the production scorecard to 649/700, with backend
  statutory/accounting engine at 228/250.
- Verification completed locally:
  - PowerShell parser check for `scripts\verify-release-evidence.ps1` passed.
  - Temporary completed release-evidence pack outside the repo passed with exact
    `accepted` manual handoff decisions.
  - A copied pack with the `medium-audit-required` decision set to
    `accepted with notes` failed with the expected manual handoff verifier
    error.
  - Backend focused regression passed 3 tests:
    `ReleaseEvidenceVerifier_BlocksIncompleteHumanSignoffEvidence`,
    `ReleaseEvidenceTemplates_CoverHumanVisualAccountantAndProviderSignoffs`,
    and
    `ProductionReadinessReport_ExposesGoalScorecardMappedToReleaseBlockers`.
  - Frontend contract/API/render/type checks passed:
    `node --test tests/production-readiness-contract.test.mjs`,
    `node scripts/verify-api-client.mjs`,
    `npx.cmd vitest run tests/render/production-readiness-panel.test.tsx tests/render/production-readiness-workbench.test.tsx`,
    and `npx.cmd tsc --noEmit --incremental false`.

Backend external ROS/iXBRL exact decision checks:

- This slice tightened `scripts/verify-release-evidence.ps1` so external
  validation `Warnings/errors` cells must be exactly `none`, `accepted`, or
  `remediated`, and scenario `Decision` cells must be exactly `accepted`.
- The external ROS/iXBRL validation template now tells reviewers that ambiguous
  warning/error or decision text such as `accepted with notes` is rejected, so
  limitations must live in retained validation references, reports, or notes.
- That previous slice moved the production scorecard to 651/700, with backend
  statutory/accounting engine at 230/250.
- Verification completed locally:
  - PowerShell parser check for `scripts\verify-release-evidence.ps1` passed.
  - Temporary completed release-evidence pack outside the repo passed with exact
    external validation warning/error statuses and exact accepted decisions.
  - A copied external ROS/iXBRL pack with the `micro-ltd` `Warnings/errors`
    value set to `accepted with notes` failed with the expected exact-value
    verifier error.
  - Backend focused regression passed 3 tests:
    `ReleaseEvidenceVerifier_BlocksIncompleteHumanSignoffEvidence`,
    `ReleaseEvidenceTemplates_CoverHumanVisualAccountantAndProviderSignoffs`,
    and
    `ProductionReadinessReport_ExposesGoalScorecardMappedToReleaseBlockers`.
  - Frontend contract/API/render/type checks passed:
    `node --test tests/production-readiness-contract.test.mjs`,
    `node scripts/verify-api-client.mjs`,
    `npx.cmd vitest run tests/render/production-readiness-panel.test.tsx tests/render/production-readiness-workbench.test.tsx`,
    and `npx.cmd tsc --noEmit --incremental false`.

Frontend visual QA exact route-pass checks:

- This slice tightened `scripts/verify-release-evidence.ps1` so every visual QA
  route capture cell for desktop light, desktop dark, mobile light and mobile
  dark must be exactly `pass`.
- The visual QA sign-off template now tells reviewers that `accepted` or other
  ambiguous route capture text is rejected, so limitations must live in retained
  route notes or visual evidence references.
- That previous slice moved the production scorecard to 653/700, with frontend
  accountant workbench at 174/200.
- Verification completed locally:
  - PowerShell parser check for `scripts\verify-release-evidence.ps1` passed.
  - Temporary completed release-evidence pack outside the repo passed with exact
    `pass` visual QA route capture values.
  - A copied visual QA pack with the `dashboard` `Desktop light` value set to
    `accepted` failed with the expected exact-pass verifier error.
  - Backend focused regression passed 3 tests:
    `ReleaseEvidenceVerifier_BlocksIncompleteHumanSignoffEvidence`,
    `ReleaseEvidenceTemplates_CoverHumanVisualAccountantAndProviderSignoffs`,
    and
    `ProductionReadinessReport_ExposesGoalScorecardMappedToReleaseBlockers`.
  - Frontend contract/API/render/type checks passed:
    `node --test tests/production-readiness-contract.test.mjs`,
    `node scripts/verify-api-client.mjs`,
    `npx.cmd vitest run tests/render/production-readiness-panel.test.tsx tests/render/production-readiness-workbench.test.tsx`,
    and `npx.cmd tsc --noEmit --incremental false`.

Backend source-law exact platform-impact checks:

- This slice tightened `scripts/verify-release-evidence.ps1` so source-law
  `Platform impact` cells must be exactly `no change`, `reflected`, or
  `blocking`.
- The source-law review template now tells reviewers that trailing impact prose
  such as `reflected in notes` is rejected, so detailed rationale must live in
  retained per-source notes or evidence references.
- That previous slice moved the production scorecard to 655/700, with backend
  statutory/accounting engine at 232/250.
- Verification completed locally:
  - PowerShell parser check for `scripts\verify-release-evidence.ps1` passed.
  - Temporary completed release-evidence pack outside the repo passed with exact
    source-law platform-impact values.
  - A copied source-law pack with the
    `cro-financial-statements-requirements` `Platform impact` value set to
    `reflected in notes` failed with the expected exact-impact verifier error.
  - Backend focused regression passed 3 tests:
    `ReleaseEvidenceVerifier_BlocksIncompleteHumanSignoffEvidence`,
    `ReleaseEvidenceTemplates_CoverHumanVisualAccountantAndProviderSignoffs`,
    and
    `ProductionReadinessReport_ExposesGoalScorecardMappedToReleaseBlockers`.
  - Frontend contract/API/render/type checks passed:
    `node --test tests/production-readiness-contract.test.mjs`,
    `node scripts/verify-api-client.mjs`,
    `npx.cmd vitest run tests/render/production-readiness-panel.test.tsx tests/render/production-readiness-workbench.test.tsx`,
    and `npx.cmd tsc --noEmit --incremental false`.

Backend qualified-accountant route exact-question checks:

- This slice tightened `scripts/verify-release-evidence.ps1` so
  qualified-accountant route `Decision question answered` cells must be exactly
  `yes`.
- The qualified-accountant acceptance template now separates route-question
  answer evidence from professional evidence acceptance: `Decision question
  answered` is exactly `yes`, while `Evidence accepted` is exactly `accepted`.
- That previous slice moved the production scorecard to 657/700, with backend
  statutory/accounting engine at 234/250.
- Verification completed locally:
  - PowerShell parser check for `scripts\verify-release-evidence.ps1` passed.
  - Temporary completed release-evidence pack outside the repo passed with exact
    `yes` qualified-accountant route decision-question values.
  - A copied qualified-accountant pack with the `dashboard`
    `Decision question answered` value set to `accepted` failed with the
    expected exact-question verifier error.
  - Backend focused regression passed 3 tests:
    `ReleaseEvidenceVerifier_BlocksIncompleteHumanSignoffEvidence`,
    `ReleaseEvidenceTemplates_CoverHumanVisualAccountantAndProviderSignoffs`,
    and
    `ProductionReadinessReport_ExposesGoalScorecardMappedToReleaseBlockers`.
  - Frontend contract/API/render/type checks passed:
    `node --test tests/production-readiness-contract.test.mjs`,
    `node scripts/verify-api-client.mjs`,
    `npx.cmd vitest run tests/render/production-readiness-panel.test.tsx tests/render/production-readiness-workbench.test.tsx`,
    and `npx.cmd tsc --noEmit --incremental false`.

Frontend visual QA exact route-anchor checks:

- This slice tightened `scripts/verify-release-evidence.ps1` so visual QA route
  `Notes` cells must be exactly
  `visual-smoke-evidence-report.json#routeAcceptance.<route>`.
- The visual QA sign-off template now tells reviewers to retain the exact
  visual-smoke routeAcceptance anchor for every route, so notes cannot be reused
  against another route.
- That previous slice moved the production scorecard to 659/700, with frontend
  accountant workbench at 176/200.
- Verification completed locally:
  - PowerShell parser check for `scripts\verify-release-evidence.ps1` passed.
  - Temporary completed release-evidence pack outside the repo passed with exact
    visual route anchors.
  - A copied visual QA pack with `dashboard` `Notes` set to
    `visual-smoke-evidence-report.json#routeAcceptance.company-detail` failed
    with the expected exact visual QA route-anchor verifier error.
  - Backend focused regression passed 3 tests:
    `ReleaseEvidenceVerifier_BlocksIncompleteHumanSignoffEvidence`,
    `ReleaseEvidenceTemplates_CoverHumanVisualAccountantAndProviderSignoffs`,
    and
    `ProductionReadinessReport_ExposesGoalScorecardMappedToReleaseBlockers`.
  - Frontend contract/API/render/type checks passed:
    `node --test tests/production-readiness-contract.test.mjs`,
    `node scripts/verify-api-client.mjs`,
    `npx.cmd vitest run tests/render/production-readiness-panel.test.tsx tests/render/production-readiness-workbench.test.tsx`,
    and `npx.cmd tsc --noEmit --incremental false`.

Backend qualified-accountant exact route-note anchor checks:

- This slice tightened `scripts/verify-release-evidence.ps1` so
  qualified-accountant route `Notes` cells must be exactly
  `qualified-accountant-route-walkthrough#<route>`.
- The qualified-accountant acceptance template now tells reviewers to retain the
  exact route walkthrough note anchor for every route, so route notes cannot be
  reused against another route.
- That previous slice moved the production scorecard to 661/700, with backend
  statutory/accounting engine at 236/250.
- Verification completed locally:
  - PowerShell parser check for `scripts\verify-release-evidence.ps1` passed.
  - Temporary completed release-evidence pack outside the repo passed with exact
    qualified-accountant route note anchors.
  - A copied qualified-accountant pack with `dashboard` `Notes` set to
    `qualified-accountant-route-walkthrough#company-detail` failed with the
    expected exact route-note verifier error.
  - Backend focused regression passed 3 tests:
    `ReleaseEvidenceVerifier_BlocksIncompleteHumanSignoffEvidence`,
    `ReleaseEvidenceTemplates_CoverHumanVisualAccountantAndProviderSignoffs`,
    and
    `ProductionReadinessReport_ExposesGoalScorecardMappedToReleaseBlockers`.
  - Frontend contract/API/render/type checks passed:
    `node --test tests/production-readiness-contract.test.mjs`,
    `node scripts/verify-api-client.mjs`,
    `npx.cmd vitest run tests/render/production-readiness-panel.test.tsx tests/render/production-readiness-workbench.test.tsx`,
    and `npx.cmd tsc --noEmit --incremental false`.

Backend source-law exact note-anchor checks:

- This slice tightened `scripts/verify-release-evidence.ps1` so source-law
  review `Notes` cells must be exactly
  `source-law-review-ledger#<source-id>`.
- The source-law review template now tells reviewers to retain the exact
  per-source review note anchor for every monitored source, so source-law review
  notes cannot be reused against another source row.
- That previous slice moved the production scorecard to 663/700, with backend
  statutory/accounting engine at 238/250.
- Verification completed locally:
  - PowerShell parser check for `scripts\verify-release-evidence.ps1` passed.
  - Temporary completed release-evidence pack outside the repo passed with exact
    source-law note anchors.
  - A copied source-law pack with `revenue-accepted-taxonomies` `Notes` set to
    `source-law-review-ledger#frc-frs-102` failed with the expected exact
    source-law note-anchor verifier error.
  - Backend focused regression passed 3 tests:
    `ReleaseEvidenceVerifier_BlocksIncompleteHumanSignoffEvidence`,
    `ReleaseEvidenceTemplates_CoverHumanVisualAccountantAndProviderSignoffs`,
    and
    `ProductionReadinessReport_ExposesGoalScorecardMappedToReleaseBlockers`.
  - Frontend contract/API/render/type checks passed:
    `node --test tests/production-readiness-contract.test.mjs`,
    `node scripts/verify-api-client.mjs`,
    `npx.cmd vitest run tests/render/production-readiness-panel.test.tsx tests/render/production-readiness-workbench.test.tsx`,
    and `npx.cmd tsc --noEmit --incremental false`.

Backend qualified-accountant exact scenario-anchor checks:

- This slice tightened `scripts/verify-release-evidence.ps1` so qualified-accountant
  `Scenario evidence reference` cells must be exactly
  `qualified-accountant-walkthrough-ledger#<scenario>`.
- The qualified-accountant acceptance template now tells reviewers to retain the
  exact scenario walkthrough anchor for every canonical golden corpus scenario,
  so scenario evidence cannot be reused against another scenario row.
- That previous slice moved the production scorecard to 665/700, with backend
  statutory/accounting engine at 240/250.
- Verification completed locally:
  - PowerShell parser check for `scripts\verify-release-evidence.ps1` passed.
  - Temporary completed release-evidence pack outside the repo passed with exact
    qualified-accountant scenario anchors.
  - A copied qualified-accountant pack with `micro-ltd`
    `Scenario evidence reference` set to
    `qualified-accountant-walkthrough-ledger#small-abridged-ltd` failed with the
    expected exact scenario-anchor verifier error.
  - Backend focused regression passed 3 tests:
    `ReleaseEvidenceVerifier_BlocksIncompleteHumanSignoffEvidence`,
    `ReleaseEvidenceTemplates_CoverHumanVisualAccountantAndProviderSignoffs`,
    and
    `ProductionReadinessReport_ExposesGoalScorecardMappedToReleaseBlockers`.
  - Frontend contract/API/render/type checks passed:
    `node --test tests/production-readiness-contract.test.mjs`,
    `node scripts/verify-api-client.mjs`,
    `npx.cmd vitest run tests/render/production-readiness-panel.test.tsx tests/render/production-readiness-workbench.test.tsx`,
    and `npx.cmd tsc --noEmit --incremental false`.

Backend external ROS/iXBRL exact scenario-anchor checks:

- This slice tightened `scripts/verify-release-evidence.ps1` so external
  ROS/iXBRL validation `External reference` cells must be exactly
  `external-ros-validation-ledger#<scenario>` and `Taxonomy package` cells must
  be exactly `revenue-taxonomy-package-ledger#<scenario>`.
- The external ROS/iXBRL validation template now tells reviewers to retain those
  exact per-scenario anchors, so validation and taxonomy evidence cannot be
  reused against another golden corpus row.
- That previous slice moved the production scorecard to 667/700, with backend
  statutory/accounting engine at 242/250.
- Verification completed locally:
  - PowerShell parser check for `scripts\verify-release-evidence.ps1` passed.
  - Temporary completed release-evidence pack outside the repo passed with exact
    external ROS/iXBRL validation and taxonomy package anchors.
  - A copied external ROS/iXBRL pack with `micro-ltd` `External reference` set to
    `external-ros-validation-ledger#small-abridged-ltd` failed with the expected
    exact external validation anchor verifier error.
  - Backend focused regression passed 3 tests:
    `ReleaseEvidenceVerifier_BlocksIncompleteHumanSignoffEvidence`,
    `ReleaseEvidenceTemplates_CoverHumanVisualAccountantAndProviderSignoffs`,
    and
    `ProductionReadinessReport_ExposesGoalScorecardMappedToReleaseBlockers`.
  - Frontend contract/API/render/type checks passed:
    `node --test tests/production-readiness-contract.test.mjs`,
    `node scripts/verify-api-client.mjs`,
    `npx.cmd vitest run tests/render/production-readiness-panel.test.tsx tests/render/production-readiness-workbench.test.tsx`,
    and `npx.cmd tsc --noEmit --incremental false`.

Backend manual handoff exact evidence-anchor checks:

- This slice tightened `scripts/verify-release-evidence.ps1` so manual handoff
  scenario `Auditor evidence`, `Manual handoff note`, and `Filing readiness
  snapshot` cells must be exactly
  `signed-auditor-report-evidence#<scenario>`,
  `manual-handoff-note#<scenario>`, and
  `filing-readiness-snapshot#<scenario>`. Unsupported-path
  `Release evidence reference` cells must be exactly
  `unsupported-path-evidence#<path-code>`.
- The manual handoff acceptance template now tells reviewers to retain those
  exact scenario/path anchors, so auditor, handoff, readiness, and unsupported
  path evidence cannot be reused against another manual handoff row.
- The production scorecard is now 669/700, with backend statutory/accounting
  engine at 244/250.
- Verification completed locally:
  - PowerShell parser check for `scripts\verify-release-evidence.ps1` passed.
  - Temporary completed release-evidence pack outside the repo passed with exact
    manual handoff scenario and unsupported-path anchors.
  - A copied manual handoff pack with `medium-audit-required`
    `Auditor evidence` set to
    `signed-auditor-report-evidence#wrong-scenario` failed with the expected
    exact manual handoff anchor verifier error.
  - Backend focused regression passed 3 tests:
    `ReleaseEvidenceVerifier_BlocksIncompleteHumanSignoffEvidence`,
    `ReleaseEvidenceTemplates_CoverHumanVisualAccountantAndProviderSignoffs`,
    and
    `ProductionReadinessReport_ExposesGoalScorecardMappedToReleaseBlockers`.
  - Frontend contract/API/render/type checks passed:
    `node --test tests/production-readiness-contract.test.mjs`,
    `node scripts/verify-api-client.mjs`,
    `npx.cmd vitest run tests/render/production-readiness-panel.test.tsx tests/render/production-readiness-workbench.test.tsx`,
    and `npx.cmd tsc --noEmit --incremental false`.

## What Is Left To Do

Highest-priority next steps:

1. Rerun the full local production gate before release if more code changes land; the
   current local gate is green on July 8, 2026.
2. Perform and record human visual review of the generated light/dark desktop/mobile
   visual smoke artifact set; the screenshot manifest now verifies locally, but
   named visual QA sign-off is still required.
3. Complete and retain the actual named accountant walkthrough across the golden corpus:
   micro LTD, small abridged LTD, DAC small, CLG charity, and medium/audit-required
   manual handoff. The code now emits and verifies the scenario-by-route walkthrough
   evidence matrix and canonical acceptance template rows, but the human walkthrough
   note is still missing.
4. Complete and retain external ROS/iXBRL validation references for the exact generated
   artifact hashes; the template and verifier now require accepted rows with real
   references, hashes, exact warning/error status values, and exact accepted
   decisions, but real external validation evidence is still missing.
5. Complete and retain manual handoff acceptance evidence for the `medium-audit-required`
   scenario and unsupported path codes before relying on audit-required or unsupported
   outputs; the template and verifier now require real retained evidence references
   and accepted reviewer decisions, but real named manual handoff evidence is still
   missing.
6. Record qualified-accountant acceptance evidence for outputs, gates, wording,
   legal/source evidence, visual workflow, and manual handoff behavior.
7. Promote CI monitoring smoke into release-grade evidence by confirming the controlled
   event inside the configured provider and retaining a named operator record before
   real filing use; the template and verifier now require real provider references,
   an HTTPS provider URL and an accepted operator decision, but real named provider
   confirmation evidence is still missing.
8. Continue retaining `no-direct-filing-submission-report.json` for every release
   candidate; the verifier now requires the exact commit/run identity and the pack
   verifiers reject stale no-direct evidence.
9. Run `scripts\verify-release-evidence.ps1` against completed release evidence and
   retain `release-evidence-report.json`; blank templates are intentionally failing
   evidence until real named reviewers complete them.
10. Continue UI polish route by route, especially any surfaces that still feel too
   card-heavy, too sparse, inconsistent in dark mode, or not dense enough for daily
   accountant use.
11. Keep extracting route-heavy frontend code into focused workflow components only when
   it reduces real complexity or improves testable reuse.

## Estimated Completion

As of July 9, 2026:

- Code implementation is roughly 70-75% complete.
- Production assurance is roughly 60-65% complete.
- Overall goal is roughly 63-67% complete, with about one third left.
- The production scorecard is now 669/700: architecture/documentation 99/100,
  backend statutory/accounting engine 244/250, frontend accountant workbench 176/200,
  and security/auth/tenant/platform guardrails 150/150.
- Architecture/documentation is now scored 99/100 in the production scorecard because
  source-law review, release evidence templates, manual handoff evidence, runbook
  links, and verifier coverage are in place, including an exact release artifact-pack
  verifier; the remaining architecture gap is completed named human release evidence.

The remaining third is not just coding. It is proof: human visual QA review,
source-law review sign-off, real-provider monitoring confirmation, manual handoff
acceptance, accountant walkthrough, and named professional sign-off.

## Claude Continuation Instruction

When opening a Claude session, continue from this section:

**[Active Goal Handoff](AGENTS.md#active-goal-handoff)**

Do not restart the project plan. Inspect the current worktree and latest commits, then
continue with the highest-priority unfinished item above.
