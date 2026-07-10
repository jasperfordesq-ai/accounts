namespace Accounts.Api.Services;

public partial class ProductionReadinessReportService
{
    private static IReadOnlyList<ProductionAuditabilityControl> BuildAuditabilityControls() =>
    [
        new(
            "who-changed-what",
            "Who changed what",
            true,
            "audit-log-integrity-chain",
            "Authenticated user id, reviewer display name, request id, timestamp, entity, action, and old/new value snapshots with sensitive fields redacted.",
            "AuditLog integrity hashes link each company-scoped entry to the previous entry; audit durability tests verify failed business writes still preserve audit rows where required.",
            [
                AuditEventCodes.SizeClassificationDataSaved,
                AuditEventCodes.FilingRegimeDetermined,
                AuditEventCodes.TransactionCategorised,
                AuditEventCodes.AdjustmentUpdated,
                AuditEventCodes.NoteDisclosureUpdated
            ]),
        new(
            "who-approved-what",
            "Who approved what",
            true,
            "workflow-gates-plus-audit-log-integrity-chain",
            "Named reviewer/accountant identity, approval timestamps, filing status transitions, adjustment approvals, signatory evidence and the affected period.",
            "Approval and filing-state endpoints write audit events after readiness gates have passed; final filing paths remain blocked when required evidence is missing.",
            [
                AuditEventCodes.AdjustmentApproved,
                AuditEventCodes.CroFilingStatusChanged,
                AuditEventCodes.CroPaymentConfirmed,
                AuditEventCodes.DeadlineMarkedFiled,
                AuditEventCodes.CharityFilingStatusChanged
            ]),
        new(
            "what-was-generated",
            "What was generated",
            true,
            "server-side-generation-events",
            "Generated accounts documents, CRO signature pages, notes, charity reports, iXBRL internal checks, validation status and period linkage.",
            "Document generation and iXBRL checks are recorded as workflow/audit events before readiness profiles expose generated outputs as satisfied evidence.",
            [
                AuditEventCodes.CroDocumentGenerated,
                AuditEventCodes.IxbrlInternalCheckCompleted,
                AuditEventCodes.NotesGenerated,
                AuditEventCodes.CharityReportGenerated
            ]),
        new(
            "what-evidence-was-present",
            "What evidence was present",
            true,
            "readiness-profile-plus-audit-snapshots",
            "Required evidence checklist state, source references, legal-gate decisions, old/new value snapshots and generated output flags at the point of review.",
            "FilingReadinessProfile exposes blocking evidence and LegalSourceReference metadata; audit snapshots preserve the data changes that led to the generated pack.",
            [
                AuditEventCodes.YearEndReviewConfirmationUpdated,
                AuditEventCodes.OpeningBalanceUpserted,
                AuditEventCodes.ShareCapitalUpdated,
                AuditEventCodes.TaxBalanceUpserted,
                AuditEventCodes.CharityInfoUpdated
            ]),
        new(
            "tamper-evident-chain",
            "Tamper-evident audit chain",
            true,
            "audit-log-integrity-chain-and-signed-checkpoint",
            "Previous integrity hash, current integrity hash, checkpoint key id, signed checkpoint anchor, checked-entry count and checkpoint creator identity.",
            "AuditIntegrityService verifies hash chaining; AuditIntegrityCheckpointService signs a checkpoint over the latest company audit entry with deployment-managed signing keys.",
            [
                AuditEventCodes.CroFilingStatusChanged,
                AuditEventCodes.CroDocumentGenerated,
                AuditEventCodes.IxbrlInternalCheckCompleted
            ])
    ];

    private static IReadOnlyList<AuditEvidenceTimelineEntry> BuildAuditEvidenceTimeline() =>
    [
        new(
            "data-change-capture",
            "Working papers",
            "Who changed what and when?",
            "At every authenticated write before regenerated outputs can be reviewed.",
            "Authenticated firm user",
            "Audit log snapshots and integrity hash chain must cover the changed entity before a reviewer relies on the updated evidence.",
            [
                AuditEventCodes.SizeClassificationDataSaved,
                AuditEventCodes.FilingRegimeDetermined,
                AuditEventCodes.AdjustmentUpdated,
                AuditEventCodes.NoteDisclosureUpdated,
                AuditEventCodes.YearEndReviewConfirmationUpdated
            ],
            [
                "working-paper-review",
                "qualified-accountant-review"
            ]),
        new(
            "generated-output-capture",
            "Generated outputs",
            "What was generated and when?",
            "Immediately after server-side PDF, notes, charity or iXBRL generation completes.",
            "System generation service",
            "Generated output audit event must exist before accountant approval can rely on the pack.",
            [
                AuditEventCodes.CroDocumentGenerated,
                AuditEventCodes.IxbrlInternalCheckCompleted,
                AuditEventCodes.NotesGenerated,
                AuditEventCodes.CharityReportGenerated
            ],
            [
                "generated-output-review",
                "qualified-accountant-review"
            ]),
        new(
            "accountant-approval-capture",
            "Professional review",
            "Who approved the pack and what evidence was open at approval?",
            "At named qualified-accountant approval, after generated outputs and required evidence are present.",
            "Named qualified accountant",
            "Filing workflow transitions must record reviewer identity, approval timestamp, open blockers, warnings and allowed next actions.",
            [
                AuditEventCodes.AdjustmentApproved,
                AuditEventCodes.CroFilingStatusChanged,
                AuditEventCodes.CroPaymentConfirmed,
                AuditEventCodes.CharityFilingStatusChanged
            ],
            [
                "qualified-accountant-review",
                "director-secretary-certification"
            ]),
        new(
            "external-validation-capture",
            "External validation",
            "When was external ROS/iXBRL validation evidence present?",
            "After internal iXBRL checks pass and before Revenue filing status can be marked externally usable.",
            "Reviewer or qualified accountant",
            "External validation evidence remains a recorded workflow state only; the platform must not perform direct ROS submission.",
            [
                AuditEventCodes.IxbrlInternalCheckCompleted,
                AuditEventCodes.CroFilingStatusChanged
            ],
            [
                "external-ros-validation",
                "no-direct-cro-ros-submission"
            ])
    ];

    private static IReadOnlyList<ProductionAuditEvidencePackItem> BuildAuditEvidencePack() =>
    [
        new(
            "who-changed-what",
            "Who changed what",
            "Which authenticated user changed statutory, accounting or filing evidence, and what old/new values were captured?",
            "tamper-evident-audit-log-entry",
            "audit_logs",
            "Authenticated firm user",
            "At the same transaction boundary as each supported write.",
            "Audit entry must include entity, action, request correlation, redacted before/after snapshots, integrity hash and previous hash.",
            "Block release when a supported write path can alter filing evidence without an audit row linked into the integrity chain.",
            [
                AuditEventCodes.SizeClassificationDataSaved,
                AuditEventCodes.FilingRegimeDetermined,
                AuditEventCodes.AdjustmentUpdated,
                AuditEventCodes.NoteDisclosureUpdated,
                AuditEventCodes.YearEndReviewConfirmationUpdated
            ],
            [
                "working-paper-review",
                "qualified-accountant-review"
            ]),
        new(
            "who-approved-what",
            "Who approved what",
            "Which named reviewer or qualified accountant approved the pack, and which period/output state was approved?",
            "named-accountant-approval-record",
            "filing_workflow_status_history",
            "Named qualified accountant",
            "After required evidence is present and before any filing status can be marked approved or submitted.",
            "Approval transition must carry reviewer identity, timestamp, period id, open blocker summary and the allowed next action set.",
            "Block release when approval can be recorded without a named qualified-accountant identity and linked readiness evidence.",
            [
                AuditEventCodes.AdjustmentApproved,
                AuditEventCodes.CroFilingStatusChanged,
                AuditEventCodes.CroPaymentConfirmed,
                AuditEventCodes.CharityFilingStatusChanged
            ],
            [
                "qualified-accountant-review",
                "director-secretary-certification"
            ]),
        new(
            "evidence-present-at-approval",
            "Evidence present at approval",
            "What evidence was present, blocked, warned or manually handed off when the accountant approval decision was made?",
            "readiness-profile-decision-snapshot",
            "filing-readiness-profile-snapshot",
            "Named qualified accountant",
            "At professional review, immediately before final approval or manual handoff recording.",
            "Snapshot must include required evidence, blocking issues, warning issues, legal source references, generated output flags and allowed next actions.",
            "Block release when accountant approval can proceed without a retained readiness-profile snapshot for the approved period.",
            [
                AuditEventCodes.YearEndReviewConfirmationUpdated,
                AuditEventCodes.OpeningBalanceUpserted,
                AuditEventCodes.ShareCapitalUpdated,
                AuditEventCodes.TaxBalanceUpserted,
                AuditEventCodes.CharityInfoUpdated
            ],
            [
                "generated-output-review",
                "qualified-accountant-review",
                "manual-professional-handoff"
            ]),
        new(
            "generated-output-fingerprint",
            "Generated output fingerprint",
            "Which PDF, iXBRL, notes, CRO or charity output was generated, and how can the exact generated artifact be recognised later?",
            "generated-output-fingerprint",
            "generated_filing_output_manifest",
            "System generation service",
            "Immediately after server-side output generation and before the output is exposed for review.",
            "Manifest must retain output type, period id, generator version, source-law snapshot hash, generated timestamp and artifact fingerprint.",
            "Block release when generated filing artifacts are reviewable without a retained manifest or audit event.",
            [
                AuditEventCodes.CroDocumentGenerated,
                AuditEventCodes.IxbrlInternalCheckCompleted,
                AuditEventCodes.NotesGenerated,
                AuditEventCodes.CharityReportGenerated
            ],
            [
                "generated-output-review",
                "external-ros-validation"
            ]),
        new(
            "integrity-chain-checkpoint",
            "Integrity chain checkpoint",
            "Can the release reviewer prove audit entries have not been removed or rewritten since the evidence was captured?",
            "signed-audit-integrity-checkpoint",
            "audit_integrity_checkpoints",
            "Platform owner",
            "At release review and after the seeded accountant walkthrough evidence has been generated.",
            "Checkpoint must cover latest audit id, previous hash, current hash, checked-entry count, signing key id and signature verification result.",
            "Block release when audit hash verification or signed checkpoint creation cannot be demonstrated for the production candidate.",
            [
                AuditEventCodes.CroFilingStatusChanged,
                AuditEventCodes.CroDocumentGenerated,
                AuditEventCodes.IxbrlInternalCheckCompleted
            ],
            [
                "audit-integrity-checkpoint",
                "release-review-checklist"
            ])
    ];

    private static IReadOnlyList<ProductionMonitoringControl> BuildMonitoringControls() =>
    [
        new(
            "error-tracking",
            "Production error tracking",
            "Sentry-compatible",
            true,
            "Monitoring:ErrorTrackingDsn",
            "Unhandled exceptions are captured server-side with request path, HTTP method, environment and correlation id while default PII capture is disabled.",
            "Program.cs wires UseSentry from Monitoring:ErrorTrackingDsn; ProductionSafetyService blocks non-development startup when the DSN is missing or not HTTPS.",
            "Primary on-call accountant and platform owner",
            "Block release if error events cannot be routed to the on-call owner."),
        new(
            "structured-json-logs",
            "Structured JSON logs",
            "ASP.NET Core JSON console",
            true,
            "Monitoring:StructuredJsonConsole",
            "Production logs are emitted as structured JSON with scopes so log processors can index timestamps, categories, levels and correlation fields.",
            "Program.cs switches to AddJsonConsole when Monitoring:StructuredJsonConsole is true; production compose sets the flag explicitly.",
            "Platform operations log stream and release reviewer",
            "Block release if production logs cannot be parsed by timestamp, level, category and correlation id."),
        new(
            "sanitized-client-error-telemetry",
            "Sanitized frontend error telemetry",
            "Authenticated first-party relay to Sentry-compatible provider",
            true,
            "Monitoring:ErrorTrackingDsn and /api/system/monitoring/client-event",
            "Render, unhandled, terminal API, timeout, network, runtime-contract and authentication-service failures use fixed event codes, an allowlisted route shape and an optional safe correlation ID only.",
            "Production smoke retains both provider event IDs; verify-structured-logs.ps1 matches the client correlation line and rejects the synthetic email/secret markers; release evidence requires the real provider permalink.",
            "Frontend engineering, platform owner and named release operator",
            "Block release if client failures cannot reach the provider without request/response bodies, form values, financial values, credentials or client PII."),
        new(
            "correlation-id-error-responses",
            "Correlation id error responses",
            "ExceptionMiddleware",
            true,
            "Monitoring:IncludeCorrelationId",
            "Unexpected errors return a safe generic response with the ASP.NET trace identifier; server logs carry the same identifier for triage.",
            "ExceptionMiddleware logs ResourceNotFoundException, BusinessRuleException and unhandled exceptions with context.TraceIdentifier and writes correlationId to the JSON error response.",
            "Support triage queue and platform owner",
            "Block release if safe error responses omit correlation ids or server logs cannot be matched to the support ticket.")
    ];

    private static IReadOnlyList<DependencyPolicyControl> BuildDependencyPolicyControls() =>
    [
        new(
            "frontend-npm-audit",
            "Frontend dependency vulnerability audit",
            true,
            "CI frontend job runs npm audit --audit-level=moderate after npm ci.",
            "npm audit report for dependencies resolved from frontend/package-lock.json, with low-severity advisories tolerated only when they do not affect production build/runtime paths.",
            ".github/workflows/ci.yml Audit frontend dependencies step plus package-lock.json review in release evidence.",
            "Fail the release for moderate, high or critical npm advisories; record any accepted low-severity dev-tool advisory with owner and review date."),
        new(
            "frontend-lockfile-reproducibility",
            "Frontend lockfile reproducibility",
            true,
            "CI installs with npm ci using frontend/package-lock.json and the Node version pinned by .nvmrc/package engines.",
            "The package-lock.json resolved dependency graph is the release input for test, lint, build and production smoke images.",
            ".github/workflows/ci.yml Set up Node cache-dependency-path and Install frontend dependencies steps.",
            "Fail the release if package.json and package-lock.json drift or if npm ci cannot reproduce the dependency tree."),
        new(
            "ci-action-version-hygiene",
            "CI action version hygiene",
            true,
            "Workflow Hygiene job runs node scripts/verify-ci-actions.mjs before backend/frontend/production jobs.",
            "GitHub Actions used by CI are checked for explicit version hygiene before any production assurance job can pass.",
            ".github/workflows/ci.yml Workflow Hygiene job blocks downstream jobs through needs dependencies.",
            "Fail the release if workflow actions are unpinned, downgraded below policy, or bypass the hygiene verifier."),
        new(
            "backend-restore-build",
            "Backend NuGet restore and release build",
            true,
            "CI backend job runs dotnet restore, dotnet test --configuration Release and dotnet build --configuration Release.",
            "NuGet restore, Release test output and Release API build output prove the backend dependency graph resolves and compiles before production images are accepted.",
            ".github/workflows/ci.yml Backend job and production image build jobs.",
            "Fail the release if NuGet restore fails, Release tests fail, or the API cannot be built from the restored dependency graph.")
    ];

    private static IReadOnlyList<DeploymentSafetyControl> BuildDeploymentSafetyControls() =>
    [
        new(
            "controlled-production-migrations",
            "Controlled production migrations",
            true,
            "Production migrations run through dotnet Accounts.Api.dll --migrate-only before app startup; ProductionSafetyService blocks unsafe automatic startup migrations outside development.",
            "A controlled migration-only command path applies EF migrations and bootstrap-owner setup without starting the web host, while normal production startup remains guarded by DatabaseStartup safety flags.",
            "Program.cs handles --migrate-only; ProductionSafetyService rejects DatabaseStartup:AutoMigrateOnStartup unless AllowStartupMigrationInProduction is deliberately enabled.",
            "Fail production startup when AutoMigrateOnStartup is enabled without explicit production approval; run migrations as a separate release step instead."),
        new(
            "production-demo-seed-block",
            "Production demo seed blocking",
            true,
            "ProductionSafetyService rejects DatabaseStartup:SeedDemoData outside development before any database startup tasks execute.",
            "Known sample companies, seeded demo users and preview-only accounting records cannot be inserted into a non-development database unless the process is running in development.",
            "ProductionSafetyService validates SeedDemoData before Program.cs can call SeedData.SeedAsync.",
            "Fail production startup if demo seed data is enabled outside development."),
        new(
            "migration-upgrade-compatibility",
            "Fresh and previous-release migration compatibility",
            true,
            "CI restores the repository-pinned dotnet-ef 10.0.9 tool, rejects pending model changes, migrates a fresh PostgreSQL 16.4 schema, upgrades the supported 20260621123340_AddCroSignatories release floor and injects a transactional DDL/data failure.",
            "migration-upgrade-report.json fingerprints retained tenant/user, company/period, financial rows and figures, filing snapshots, audit chain and checkpoints before and after upgrade; migration-upgrade-verification-report.json binds that evidence to the candidate.",
            "MigrationUpgradePostgresTests plus scripts/verify-migration-upgrade-evidence.ps1; both reports must be retained with the encrypted restore-drill-report.json in CI machine and release artifact packs.",
            "Fail the release for EF model drift, a fresh or previous-release migration failure, changed preservation fingerprints, invalid audit chain/checkpoint evidence, transaction-suppressed SQL, partial schema/data after forced failure, or missing encrypted restore evidence."),
        new(
            "backup-restore-drill",
            "Backup restore drill",
            true,
            "CI production stack smoke encrypts a custom-format PostgreSQL dump with a recovery public certificate, removes plaintext, and runs scripts/verify-postgres-backup.ps1 against the encrypted copy.",
            "The release evidence includes only the CMS/AES-256-CBC envelope, sha256 sidecar, encryption manifest and backup restore report with schema, financial, filing, audit/checkpoint fingerprints and measured RPO/RTO.",
            ".github/workflows/ci.yml Run production backup restore drill step invokes verify-postgres-backup after creating the dump.",
            "Fail the release if backup creation, checksum verification or restore verification fails."),
        new(
            "certificate-verified-database-transport",
            "Certificate-verified PostgreSQL transport",
            true,
            "Production Compose mounts a server certificate/key and deployment CA, starts PostgreSQL with TLS 1.2 or later, and requires VerifyFull for its health check, migration job and API.",
            "The retained runtime report proves the negotiated TLS protocol/cipher and that a deliberately wrong server hostname is rejected.",
            "ProductionSafetyService, scripts/verify-production-compose-images.ps1 and scripts/verify-postgres-tls.ps1 fail closed on missing CA validation, insecure overrides, expired certificates or hostname mismatch.",
            "Fail the release if postgres-tls-report.json is missing, unbound to the candidate, or does not prove an authenticated encrypted database session.")
    ];

    private static IReadOnlyList<OperationsEvidencePackItem> BuildOperationsEvidencePack() =>
    [
        new(
            "sentry-error-routing",
            "Sentry production error routing",
            "Monitoring",
            "Platform owner",
            true,
            "Verify Monitoring:ErrorTrackingDsn is configured with an HTTPS DSN and send a controlled non-PII smoke error through the production error pipeline.",
            "sentry-production-error-routing-check",
            "production-monitoring",
            "Evidence must show both the server smoke and sanitized client event reached the production error-tracking project with environment, normalized path and matching correlation ids while default PII capture remains disabled.",
            "Block release if production exceptions cannot be routed to the on-call owner with a usable correlation id."),
        new(
            "structured-log-correlation",
            "Structured log correlation sample",
            "Monitoring",
            "Platform owner",
            true,
            "Run production stack smoke and retain a structured JSON log sample containing timestamp, level, category, request id and correlation id.",
            "structured-json-log-sample",
            "production-monitoring",
            "Evidence must include api-structured.log plus structured-log-report.json proving timestamp, level, category, both monitoring correlation ids and synthetic-sensitive-marker absence.",
            "Block release if logs cannot be parsed or support tickets cannot be correlated to server evidence."),
        new(
            "dependency-audit",
            "Dependency and lockfile audit",
            "Dependency policy",
            "Engineering",
            true,
            "Run npm ci, npm audit --audit-level=moderate --json and scripts/write-dependency-evidence.ps1 against the release commit.",
            "dependency-audit-release",
            "dependency-policy-controls",
            "Evidence must include npm-audit.json and dependency-audit-report.json with package-lock hash, npm audit counts, NuGet audit policy, and GitHub Actions version-hygiene wiring.",
            "Block release for moderate/high/critical advisories, unreproducible lockfiles, failed restore/build, or unverified CI action versions."),
        new(
            "migration-safety",
            "Controlled migration safety",
            "Deployment safety",
            "Platform owner",
            true,
            "Run scripts/verify-production-compose-images.ps1 -EvidencePath production-safety-report.json against the release compose profile.",
            "production-safety-config",
            "deployment-safety-controls",
            "Evidence must show the migrate service runs exactly --migrate-only, the API depends on successful migration completion, and AutoMigrateOnStartup remains false for normal web startup.",
            "Block release if production startup can auto-migrate without explicit release approval."),
        new(
            "migration-upgrade-compatibility",
            "PostgreSQL migration upgrade compatibility",
            "Deployment safety",
            "Engineering and platform owner",
            true,
            "Run dotnet ef migrations has-pending-model-changes, the PostgreSQL MigrationUpgradePostgresTests gate and scripts/verify-migration-upgrade-evidence.ps1 for the release commit.",
            "postgres-migration-upgrade-gate",
            "deployment-safety-controls",
            "Evidence must retain migration-upgrade-report.json and migration-upgrade-verification-report.json proving 0 pending model changes, fresh migration coverage, the supported previous-release floor, exact row/figure/artifact/audit preservation, and forced-failure rollback, with restore-drill-report.json as its encrypted recovery companion.",
            "Block release if migration evidence is missing, stale, not candidate-bound, does not preserve every required group, or is not retained beside encrypted recovery evidence."),
        new(
            "production-seed-block",
            "Production seed blocking",
            "Deployment safety",
            "Platform owner",
            true,
            "Run scripts/verify-production-compose-images.ps1 -EvidencePath production-safety-report.json and retain the CI production-safety-config artifact.",
            "production-safety-config",
            "deployment-safety-controls",
            "Evidence must show SeedDemoData is false for migrate and API services, demo-seed override flags are absent, and the bootstrap owner initial password is available only to the migration job.",
            "Block release if demo seed data can run outside development."),
        new(
            "backup-restore-drill",
            "Backup restore drill",
            "Deployment safety",
            "Platform owner",
            true,
            "Run scripts/backup-postgres.ps1 and scripts/verify-postgres-backup.ps1 from the retained encrypted off-host copy against the production compose shape before approving the release.",
            "postgres-backup-restore-drill-report",
            "deployment-safety-controls",
            "Evidence must include the encrypted PostgreSQL CMS envelope, sha256 sidecar, encryption manifest, successful encrypted-copy restore, matched schema/row/figure/audit checks and measured RPO/RTO.",
            "Block release if encryption, plaintext cleanup, off-host retention, checksum verification, restore integrity or RPO/RTO verification fails."),
        new(
            "postgres-transport-tls",
            "Certificate-verified PostgreSQL transport",
            "Deployment safety",
            "Platform owner",
            true,
            "Run scripts/verify-postgres-tls.ps1 against the live production Compose candidate after its TLS-authenticated database health check passes.",
            "postgres-tls-runtime",
            "deployment-safety-controls",
            "Evidence must include postgres-tls-report.json with VerifyFull policy, mounted CA path, TLS protocol/cipher, rejected hostname mismatch, certificate hashes, validity, and exact release identity.",
            "Block release if the API/database hop is unencrypted, trusts an unverified server identity, or uses an expired or hostname-mismatched certificate.")
    ];
}
