namespace Accounts.Api.Services;

public partial class ProductionReadinessReportService
{
    private static IReadOnlyList<OperationalGate> BuildOperationalGates() =>
    [
        new(
            "qualified-accountant-review",
            "Named qualified-accountant review",
            true,
            "required",
            "Generated statutory packs cannot be treated as filing-ready until a named qualified accountant has reviewed and approved them."),
        new(
            "external-ros-validation",
            "External ROS/iXBRL validation",
            true,
            "required",
            "The platform records internal iXBRL checks only; external ROS validation remains a manual evidence gate."),
        new(
            "director-secretary-certification",
            "Director and secretary certification",
            true,
            "required",
            "CRO filing workflow requires active director and company secretary evidence."),
        new(
            "no-direct-cro-ros-submission",
            "No direct CRO/ROS submission automation",
            true,
            "enforced",
            "The workflow records generated, reviewed, approved, submitted, paid and accepted/rejected states only."),
        new(
            "production-ci-gates",
            "Production CI gates",
            true,
            "enforced",
            "CI runs backend, frontend, dependency audit, production compose config, production stack smoke and backup restore checks.")
    ];

    private static IReadOnlyList<ProductionReadinessAssuranceAction> BuildAssuranceActions() =>
    [
        new(
            "qualified-accountant-signoff",
            "Qualified accountant sign-off",
            "Qualified accountant",
            "critical",
            0,
            "accountant-review-gate",
            "required",
            "No generated filing pack can be treated as final until a named qualified accountant has reviewed the evidence, outputs and wording.",
            "Named qualified-accountant approval recorded against the period and linked to the generated pack."),
        new(
            "source-law-change-review",
            "Source-law change review",
            "Qualified accountant and engineering",
            "critical",
            2,
            "source-law-maintenance",
            "required",
            "Pinned CRO, Revenue, FRC and charity guidance must be reviewed for effective-date or wording changes before release.",
            "Source-law change review note and qualified-accountant sign-off recorded against the snapshot."),
        new(
            "external-ros-validation",
            "External ROS/iXBRL validation",
            "Reviewer",
            "critical",
            5,
            "external-validation-gate",
            "required",
            "Internal XML parsing is not a Revenue acceptance check, so real filings need a recorded external ROS validation result.",
            "External ROS validation evidence uploaded or referenced before any Revenue filing state is marked accepted."),
        new(
            "no-direct-cro-ros-submission",
            "No direct CRO/ROS submission automation",
            "Engineering",
            "critical",
            6,
            "unsupported-path-gate",
            "complete",
            "The platform must never automate final CRO or ROS submission; it records workflow states and external references only.",
            "Release reviewer confirms final filing operations remain recorded workflow states only and no direct submission client is wired."),
        new(
            "accountant-acceptance-walkthrough",
            "Accountant acceptance walkthrough",
            "Qualified accountant",
            "high",
            10,
            "golden-corpus-acceptance",
            "required",
            "A qualified accountant must take the golden scenarios through the live workflow and confirm outputs, gates and wording are professionally acceptable.",
            "Signed acceptance note covering micro LTD, small abridged LTD, CLG charity and medium/audit-required manual handoff."),
        new(
            "production-monitoring",
            "Production monitoring",
            "Operations",
            "high",
            20,
            "operations-evidence",
            "required",
            "Runtime failures must be visible to operators before real statutory filing packs are processed.",
            "Sentry production error routing configured and reviewed with structured log correlation."),
        new(
            "light-dark-visual-regression",
            "Light/dark visual regression",
            "Engineering",
            "high",
            30,
            "visual-qa-evidence",
            "in-progress",
            "The accountant journey needs mobile, tablet and desktop screenshots across light and dark mode before it can be called visually production-ready.",
            "Canonical-state screenshots for all 32 planned states in light and dark themes across mobile, tablet and desktop viewports.")
    ];

    private static IReadOnlyList<ProductionReadinessCompletionTrack> BuildCompletionTracks() =>
    [
        new(
            "backend-code",
            "Backend code",
            "Engineering",
            "review-required",
            [
                "Golden filing corpus proves PDF text, iXBRL XML, tax, notes, readiness and gates.",
                "Source-law snapshot and traceability cover every statutory decision.",
                "Production auditability captures who changed, approved, generated and submitted each pack."
            ],
            [
                "Backend golden corpus scenarios are covered by automated verifiers.",
                "Statutory rules coverage is mapped to executable tests.",
                "Production auditability controls and audit evidence timeline are declared.",
                "No direct CRO/ROS submission automation is enforced as recorded workflow states only."
            ],
            [
                "Run qualified-accountant acceptance on the golden corpus.",
                "Record source-law change review evidence against the pinned snapshot.",
                "Attach external ROS/iXBRL validation evidence for generated iXBRL packs.",
                "Verify Sentry/error routing, structured logs and backup restore evidence.",
                "Record manual handoff acceptance for audit-required paths."
            ],
            [
                "qualified-accountant-signoff",
                "source-law-change-review",
                "external-ros-validation",
                "no-direct-cro-ros-submission",
                "accountant-acceptance-walkthrough",
                "production-monitoring"
            ]),
        new(
            "frontend-ui-ux",
            "Frontend UI/UX",
            "Product design",
            "in-progress",
            [
                "Accountant workflow rail is visually coherent across the core journey.",
                "Light/dark visual regression covers mobile, tablet and desktop.",
                "Dense review workbench surfaces blockers, evidence, sources and next actions without visual clutter."
            ],
            [
                "Visual QA route audit covers the accountant workbench routes.",
                "Dashboard filing deep links send deadline-pressure and manual-handoff work directly to the period filing review tab.",
                "Period filing gate snapshot shows supported/manual path, accountant review state, external filing readiness and allowed next action.",
                "Route-level loading/error states exist for main dynamic routes.",
                "Permission-denied filing action state keeps evidence visible while blocking ineligible review actions.",
                "Workbench primitives are used in the readiness and period review surfaces."
            ],
            [
                "Review each screenshot route-by-route in light and dark mode.",
                "Polish spacing, typography, table density, empty states and mobile flow.",
                "Record named visual acceptance against the smoke manifest."
            ],
            [
                "light-dark-visual-regression",
                "accountant-acceptance-walkthrough"
            ]),
        new(
            "frontend-code",
            "Frontend code",
            "Frontend engineering",
            "in-progress",
            [
                "Shared workbench primitives cover repeated page patterns.",
                "Typed API contract blocks frontend/backend readiness drift.",
                "Route-level states cover loading, error, empty and permission-denied cases."
            ],
            [
                "API client invariants validate production readiness contracts.",
                "Component-preview route exercises shared workbench primitives.",
                "FilingReviewCentre permission gate blocks approval/submission actions behind canReview and renders PermissionDeniedPanel for ineligible roles.",
                "PeriodFilingWorkspace extraction composes review, deadline, warning, output and audit panels behind one focused filing workflow component.",
                "PeriodImportWorkspace extraction composes classification, bank account, opening-balance, CSV upload and import-status panels behind one focused import workflow component.",
                "PeriodCategoriseWorkspace extraction composes metrics, transaction rules, bulk actions, filters and categorisation table behind one focused transaction review component.",
                "PeriodYearEndWorkspace extraction composes questionnaire, completeness, summary metrics and empty-state panels behind one focused year-end workflow component.",
                "PeriodAdjustmentsWorkspace extraction composes generation, summary, filters and approval review cards behind one focused adjustments workflow component.",
                "PeriodStatementsWorkspace extraction composes readiness, statements, notes and charity reporting navigation behind one focused statements workflow component.",
                "Render tests cover accountant dashboards, review panels and workflow routes."
            ],
            [
                "Continue extracting large route files into focused workflow components.",
                "Expand visual regression assertions from screenshot capture into reviewable sign-off.",
                "Keep route fixtures aligned with backend readiness evidence."
            ],
            [
                "light-dark-visual-regression"
            ])
    ];

    private static IReadOnlyList<ProductionReleaseBlocker> BuildReleaseBlockerRegister(
        IReadOnlyList<ProductionReadinessCompletionTrack> completionTracks,
        IReadOnlyList<ProductionReadinessAssuranceAction> assuranceActions,
        IReadOnlyList<ReleaseReviewChecklistItem> releaseReviewChecklist)
    {
        var actionsByCode = assuranceActions.ToDictionary(action => action.Code, StringComparer.Ordinal);
        var checklistByAction = releaseReviewChecklist.ToDictionary(item => item.AssuranceActionCode, StringComparer.Ordinal);
        var blockers = new List<ProductionReleaseBlocker>();

        foreach (var track in completionTracks)
        {
            foreach (var actionCode in track.AssuranceActionCodes.Distinct(StringComparer.Ordinal))
            {
                if (!actionsByCode.TryGetValue(actionCode, out var action))
                    throw new InvalidOperationException($"Completion track {track.Code} references unknown assurance action {actionCode}.");

                if (action.Status == "complete")
                    continue;

                if (!checklistByAction.TryGetValue(action.Code, out var checklist))
                    throw new InvalidOperationException($"Open assurance action {action.Code} does not have a release checklist item.");

                blockers.Add(new ProductionReleaseBlocker(
                    $"{track.Code}:{action.Code}",
                    track.Code,
                    track.Label,
                    checklist.OwnerRole,
                    action.Priority,
                    action.RiskRank,
                    FormatReleaseBlockingIssue(action),
                    action.EvidenceRequired,
                    SelectNextAction(track, action),
                    action.Code,
                    checklist.Code,
                    checklist.OperationalGateCode,
                    checklist.EvidenceArtifact,
                    checklist.BlocksRelease));
            }
        }

        return blockers
            .OrderBy(blocker => blocker.RiskRank)
            .ThenBy(blocker => blocker.TrackCode, StringComparer.Ordinal)
            .ThenBy(blocker => blocker.SourceActionCode, StringComparer.Ordinal)
            .ToArray();
    }

    private static string FormatReleaseBlockingIssue(ProductionReadinessAssuranceAction action) =>
        action.Code == "qualified-accountant-signoff"
            ? "Qualified accountant sign-off required"
            : $"{action.Label} required";

    private static string SelectNextAction(
        ProductionReadinessCompletionTrack track,
        ProductionReadinessAssuranceAction action)
    {
        var match = track.NextActions.FirstOrDefault(nextAction => action.Code switch
        {
            "source-law-change-review" => nextAction.Contains("source-law", StringComparison.OrdinalIgnoreCase),
            "external-ros-validation" => nextAction.Contains("ROS/iXBRL", StringComparison.OrdinalIgnoreCase),
            "production-monitoring" => nextAction.Contains("Sentry", StringComparison.OrdinalIgnoreCase)
                || nextAction.Contains("backup", StringComparison.OrdinalIgnoreCase),
            "light-dark-visual-regression" => nextAction.Contains("screenshot", StringComparison.OrdinalIgnoreCase)
                || nextAction.Contains("visual", StringComparison.OrdinalIgnoreCase),
            "accountant-acceptance-walkthrough" => nextAction.Contains("acceptance", StringComparison.OrdinalIgnoreCase)
                || nextAction.Contains("visual acceptance", StringComparison.OrdinalIgnoreCase),
            "qualified-accountant-signoff" => nextAction.Contains("qualified-accountant", StringComparison.OrdinalIgnoreCase)
                || nextAction.Contains("acceptance", StringComparison.OrdinalIgnoreCase),
            _ => nextAction.Contains(action.Label, StringComparison.OrdinalIgnoreCase)
        });

        return match ?? track.NextActions.FirstOrDefault() ?? action.EvidenceRequired;
    }

    private static IReadOnlyList<ReleaseReviewChecklistItem> BuildReleaseReviewChecklist(
        IReadOnlyList<ProductionReadinessAssuranceAction> assuranceActions,
        IReadOnlyList<OperationalGate> operationalGates)
    {
        var actionStatuses = assuranceActions.ToDictionary(action => action.Code, action => action.Status, StringComparer.Ordinal);
        var gateCodes = operationalGates.Select(gate => gate.Code).ToHashSet(StringComparer.Ordinal);

        ReleaseReviewChecklistItem Item(
            string code,
            string label,
            string ownerRole,
            string evidenceArtifact,
            string assuranceActionCode,
            string operationalGateCode,
            IReadOnlyList<string> auditEventCodes,
            string detail)
        {
            if (!actionStatuses.TryGetValue(assuranceActionCode, out var status))
                throw new InvalidOperationException($"Release checklist item {code} references unknown assurance action {assuranceActionCode}.");

            if (!string.IsNullOrWhiteSpace(operationalGateCode) && !gateCodes.Contains(operationalGateCode))
                throw new InvalidOperationException($"Release checklist item {code} references unknown operational gate {operationalGateCode}.");

            return new ReleaseReviewChecklistItem(
                code,
                label,
                ownerRole,
                Required: true,
                status,
                BlocksRelease: status != "complete",
                evidenceArtifact,
                assuranceActionCode,
                operationalGateCode,
                auditEventCodes,
                detail);
        }

        return
        [
            Item(
                "accountant-final-signoff",
                "Named accountant final sign-off",
                "Qualified accountant",
                "named-accountant-approval-record",
                "qualified-accountant-signoff",
                "qualified-accountant-review",
                [
                    AuditEventCodes.CroFilingStatusChanged,
                    AuditEventCodes.CharityFilingStatusChanged,
                    AuditEventCodes.YearEndReviewConfirmationUpdated
                ],
                "Named professional approval must be recorded against the period before any real filing pack is treated as final."),
            Item(
                "source-law-change-review",
                "Source-law change review",
                "Qualified accountant and engineering",
                "source-law-change-review-note",
                "source-law-change-review",
                "qualified-accountant-review",
                [
                    AuditEventCodes.CroFilingStatusChanged,
                    AuditEventCodes.IxbrlInternalCheckCompleted,
                    AuditEventCodes.CharityFilingStatusChanged
                ],
                "Pinned CRO, Revenue, FRC and charity guidance must be reviewed for effective-date or wording changes before release."),
            Item(
                "external-ros-validation-evidence",
                "External ROS/iXBRL validation evidence",
                "Reviewer",
                "external-ros-validation-reference",
                "external-ros-validation",
                "external-ros-validation",
                [AuditEventCodes.IxbrlInternalCheckCompleted],
                "Internal XML checks are not enough for Revenue acceptance; the reviewer must retain external validation evidence."),
            Item(
                "no-direct-cro-ros-submission",
                "No direct CRO/ROS submission automation",
                "Engineering",
                "no-direct-cro-ros-submission-control",
                "no-direct-cro-ros-submission",
                "no-direct-cro-ros-submission",
                [],
                "Release reviewer confirms final filing operations remain generated, reviewed, approved, marked submitted, payment confirmed, accepted, rejected or corrected recorded workflow states only."),
            Item(
                "golden-corpus-accountant-acceptance",
                "Golden corpus accountant acceptance",
                "Qualified accountant",
                "signed-golden-corpus-acceptance-note",
                "accountant-acceptance-walkthrough",
                "qualified-accountant-review",
                [
                    AuditEventCodes.CroDocumentGenerated,
                    AuditEventCodes.IxbrlInternalCheckCompleted,
                    AuditEventCodes.NotesGenerated
                ],
                "A qualified accountant must walk the golden scenarios through the live workflow and accept outputs, gates and wording."),
            Item(
                "production-smoke-and-backup",
                "Production smoke and backup evidence",
                "Operations",
                "ci-production-stack-smoke-and-backup-restore",
                "production-monitoring",
                "production-ci-gates",
                [],
                "Release evidence must include successful production stack smoke, visual smoke, monitoring configuration and backup restore drill."),
            Item(
                "visual-qa-screenshot-review",
                "Light/dark visual QA screenshot review",
                "Engineering",
                "light-dark-mobile-tablet-desktop-screenshot-review",
                "light-dark-visual-regression",
                "production-ci-gates",
                [],
                "Mobile, tablet and desktop screenshots in light and dark mode must be reviewed for the accountant workflow before release.")
        ];
    }

    private static IReadOnlyList<ReleaseVerificationManifestItem> BuildReleaseVerificationManifest() =>
    [
        new(
            "backend-golden-corpus",
            "Backend golden corpus and statutory rules",
            "Engineering",
            "dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art",
            "default-ci",
            RunsInDefaultCi: true,
            BlocksRelease: true,
            "backend-test-results",
            "signed-golden-corpus-acceptance-note",
            "Run the same command locally from backend/ when GitHub Actions is unavailable, then retain the console output with the release evidence pack."),
        new(
            "frontend-workbench-contract",
            "Frontend workbench contract, render and API checks",
            "Engineering",
            "npm test",
            "default-ci",
            RunsInDefaultCi: true,
            BlocksRelease: true,
            "frontend-test-results",
            "light-dark-mobile-tablet-desktop-screenshot-review",
            "Run from frontend/ and retain the unit, render, readiness, proxy, auth and API-client verifier output."),
        new(
            "frontend-production-build",
            "Frontend lint, type-check and production build",
            "Engineering",
            "npm run lint; npx tsc --noEmit --incremental false; npm run build",
            "default-ci",
            RunsInDefaultCi: true,
            BlocksRelease: true,
            "frontend-build-results",
            "light-dark-mobile-tablet-desktop-screenshot-review",
            "Run from frontend/ and retain lint, TypeScript and Next production build output when CI is unavailable."),
        new(
            "visual-smoke-light-dark",
            "Light/dark mobile/tablet/desktop visual smoke",
            "Engineering",
            "node scripts/visual-smoke.mjs; node scripts/verify-visual-smoke-artifacts.mjs --report-path=artifacts/visual-smoke/visual-smoke-evidence-report.json; node scripts/verify-accountant-workbench-evidence.mjs --visual-report=artifacts/visual-smoke/visual-smoke-evidence-report.json --report-path=artifacts/visual-smoke/accountant-workbench-evidence-report.json",
            "default-ci",
            RunsInDefaultCi: true,
            BlocksRelease: true,
            "artifacts/visual-smoke",
            "light-dark-mobile-tablet-desktop-screenshot-review",
            "Run the visual smoke locally against seeded production-like data if CI cannot capture screenshots, then retain the manifest verification output and review the generated artifacts manually."),
        new(
            "source-law-change-review",
            "Source-law change review note",
            "Qualified accountant and engineering",
            "manual review: compare pinned CRO, Revenue, FRC and charity guidance against source-law snapshot",
            "manual-release",
            RunsInDefaultCi: false,
            BlocksRelease: true,
            "source-law-change-review-note",
            "source-law-change-review-note",
            "Before real filing release, retain a dated review note confirming each pinned source URL, effective date, wording impact and qualified-accountant acceptance."),
        new(
            "qualified-accountant-final-signoff",
            "Named accountant final sign-off evidence",
            "Qualified accountant",
            "manual review: record named qualified-accountant approval against the final generated filing pack",
            "manual-release",
            RunsInDefaultCi: false,
            BlocksRelease: true,
            "named-accountant-approval-record",
            "named-accountant-approval-record",
            "A named qualified accountant must approve the exact generated PDF, iXBRL, tax computation, notes, source-law evidence and filing gate state before real filing use."),
        new(
            "external-ros-validation-evidence",
            "External ROS/iXBRL validation evidence",
            "Reviewer",
            "manual review: retain external ROS/iXBRL validation reference for the exact generated iXBRL pack",
            "manual-release",
            RunsInDefaultCi: false,
            BlocksRelease: true,
            "external-ros-validation-reference",
            "external-ros-validation-reference",
            "Internal XML checks are not sufficient for real Revenue filing use; retain the external validation reference before final approval."),
        new(
            "no-direct-cro-ros-submission-control",
            "No direct CRO/ROS submission automation control",
            "Engineering",
            "manual review: confirm final CRO and ROS operations remain recorded workflow states only with no direct submission client configured",
            "manual-release",
            RunsInDefaultCi: false,
            BlocksRelease: true,
            "no-direct-cro-ros-submission-control",
            "no-direct-cro-ros-submission-control",
            "Confirm final filing operations remain recorded workflow states only: generated, reviewed, approved, marked submitted, payment confirmed, accepted, rejected or corrected."),
        new(
            "production-readiness-report-verification",
            "Production readiness report verification",
            "Engineering",
            "pwsh ./scripts/verify-production-readiness-report.ps1 -ReportPath production-readiness-report.json -EvidencePath production-readiness-verification-report.json",
            "default-ci",
            RunsInDefaultCi: true,
            BlocksRelease: true,
            "production-readiness-report",
            "ci-production-stack-smoke-and-backup-restore",
            "Run after capturing the live /api/system/production-readiness response and retain production-readiness-verification-report.json with the release evidence pack."),
        new(
            "production-stack-smoke",
            "Production compose smoke",
            "Operations",
            "pwsh ./scripts/smoke-production.ps1 -CheckMonitoringErrorRouting; pwsh ./scripts/verify-production-readiness-report.ps1; pwsh ./scripts/verify-structured-logs.ps1",
            "default-ci",
            RunsInDefaultCi: true,
            BlocksRelease: true,
            "ci-production-stack-smoke-and-backup-restore",
            "ci-production-stack-smoke-and-backup-restore",
            "Run the production smoke script against the production compose profile and retain health, login, monitoring-error-routing-report.json, production-readiness-verification-report.json, structured-log-report.json and filing-workflow output."),
        new(
            "postgres-transport-tls",
            "Certificate-verified PostgreSQL transport",
            "Operations",
            "pwsh ./scripts/verify-postgres-tls.ps1 -EvidencePath postgres-tls-report.json -CommitSha <release-commit-sha> -GitHubActionsRunUrl <ci-run-url>",
            "default-ci",
            RunsInDefaultCi: true,
            BlocksRelease: true,
            "postgres-tls-runtime",
            "ci-production-stack-smoke-and-backup-restore",
            "Run against the live production Compose candidate and retain postgres-tls-report.json with VerifyFull policy, protocol/cipher, rejected hostname mismatch, certificate hashes, validity, and exact release identity."),
        new(
            "backup-restore-drill",
            "PostgreSQL backup and restore drill",
            "Operations",
            "pwsh ./scripts/verify-postgres-backup.ps1",
            "default-ci",
            RunsInDefaultCi: true,
            BlocksRelease: true,
            "ci-production-stack-smoke-and-backup-restore",
            "ci-production-stack-smoke-and-backup-restore",
            "Run the backup verification script after creating a fresh production-shape dump and retain the checksum and restore verification output."),
        new(
            "postgres-migration-upgrade-gate",
            "PostgreSQL migration drift, upgrade and rollback gate",
            "Engineering and operations",
            "dotnet ef migrations has-pending-model-changes; dotnet test backend/Accounts.Tests/Accounts.Tests.csproj --configuration Release --filter FullyQualifiedName~MigrationUpgradePostgresTests; pwsh ./scripts/verify-migration-upgrade-evidence.ps1",
            "default-ci",
            RunsInDefaultCi: true,
            BlocksRelease: true,
            "postgres-migration-upgrade-gate",
            "ci-production-stack-smoke-and-backup-restore",
            "Retain migration-upgrade-report.json and migration-upgrade-verification-report.json for the exact candidate alongside restore-drill-report.json; the gate must prove a clean model, fresh PostgreSQL migration, supported previous-release preservation and transactional failure rollback."),
        new(
            "ci-machine-evidence-pack",
            "CI machine evidence pack",
            "Engineering",
            "pwsh ./scripts/verify-ci-machine-evidence-pack.ps1 -EvidenceDirectory <downloaded-ci-artifacts> -ReportPath ci-machine-evidence-pack-report.json -CommitSha <release-commit-sha> -GitHubActionsRunUrl <ci-run-url> -ReviewerWorkspaceDirectory <prepared-reviewer-workspace>",
            "default-ci",
            RunsInDefaultCi: true,
            BlocksRelease: true,
            "ci-machine-evidence-pack",
            "ci-production-stack-smoke-and-backup-restore",
            "Run after CI downloads the dependency, production safety, monitoring, structured log, migration upgrade, backup/restore, no-direct-submission, production-readiness and visual/workbench artifacts for the exact candidate."),
        new(
            "release-artifact-pack",
            "Release artifact pack verification",
            "Engineering",
            "pwsh ./scripts/verify-release-artifact-pack.ps1 -EvidenceDirectory <release-artifacts> -ReportPath release-artifact-pack-report.json -CommitSha <release-commit-sha> -GitHubActionsRunUrl <ci-run-url>",
            "manual-release",
            RunsInDefaultCi: false,
            BlocksRelease: true,
            "release-artifact-pack-report",
            "named-accountant-approval-record",
            "Run against the collected dependency, production safety, monitoring, log, migration upgrade, restore, no-direct, production-readiness, visual, workbench, human release-evidence, workspace verification and reviewer handoff reports for the exact release candidate."),
        new(
            "postgres-gated-audit-tests",
            "PostgreSQL-gated audit durability tests",
            "Engineering",
            "dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~PostgresIntegration",
            "environment-gated",
            RunsInDefaultCi: false,
            BlocksRelease: true,
            "postgres-integration-test-results",
            "ci-production-stack-smoke-and-backup-restore",
            "Set ACCOUNTS_POSTGRES_TEST_CONNECTION to a disposable PostgreSQL database and run the command from backend/ before relying on audit durability evidence."),
        new(
            "manual-accountant-acceptance",
            "Named accountant acceptance walkthrough",
            "Qualified accountant",
            "manual walkthrough: micro LTD, small abridged LTD, CLG charity and medium/audit-required handoff",
            "manual-release",
            RunsInDefaultCi: false,
            BlocksRelease: true,
            "signed-golden-corpus-acceptance-note",
            "signed-golden-corpus-acceptance-note",
            "A named qualified accountant must review the generated outputs, gates, wording and source-law evidence before any real filing pack is treated as final.")
    ];

    private static IReadOnlyList<HumanReleaseEvidenceGate> BuildHumanReleaseEvidence(
        IReadOnlyList<ReleaseReviewChecklistItem> releaseReviewChecklist,
        IReadOnlyList<ReleaseVerificationManifestItem> releaseVerificationManifest)
    {
        var checklistByCode = releaseReviewChecklist.ToDictionary(item => item.Code, StringComparer.Ordinal);
        var manifestByCode = releaseVerificationManifest.ToDictionary(item => item.Code, StringComparer.Ordinal);

        HumanReleaseEvidenceGate Item(
            string code,
            string label,
            string templateFile,
            string requiredReviewerRole,
            string signOffGate,
            string releaseChecklistCode,
            string releaseManifestCode,
            IReadOnlyList<string> reviewerPickupFiles,
            IReadOnlyList<string> requiredEvidence,
            string nextAction)
        {
            if (!checklistByCode.TryGetValue(releaseChecklistCode, out var checklistItem))
                throw new InvalidOperationException($"Human release evidence gate {code} references unknown release checklist item {releaseChecklistCode}.");

            if (!manifestByCode.ContainsKey(releaseManifestCode))
                throw new InvalidOperationException($"Human release evidence gate {code} references unknown release manifest item {releaseManifestCode}.");

            return new HumanReleaseEvidenceGate(
                code,
                label,
                templateFile,
                requiredReviewerRole,
                "pending-human-evidence",
                signOffGate,
                releaseChecklistCode,
                releaseManifestCode,
                checklistItem.EvidenceArtifact,
                BlocksRelease: true,
                reviewerPickupFiles,
                requiredEvidence,
                nextAction);
        }

        return
        [
            Item(
                "visualQa",
                "Visual QA sign-off",
                "visual-qa-signoff-template.md",
                "Named visual QA reviewer",
                "visual-qa-screenshot-review",
                "visual-qa-screenshot-review",
                "visual-smoke-light-dark",
                [
                    "visual-qa-signoff-template.md",
                    "visual-smoke-manifest.json",
                    "visual-smoke-evidence-report.json",
                    "accountant-workbench-evidence-report.json",
                    "release-evidence-reviewer-blockers.md"
                ],
                [
                    "visual-smoke-manifest.json",
                    "visual-smoke-evidence-report.json",
                    "accountant-workbench-evidence-report.json",
                    "Named reviewer pass decisions for every route/theme/viewport capture"
                ],
                "Review all 192 retained light/dark mobile/tablet/desktop canonical state screenshots and complete the visual QA sign-off template."),
            Item(
                "sourceLawReview",
                "Source-law review sign-off",
                "source-law-review-template.md",
                "Named source-law reviewer plus qualified accountant",
                "source-law-change-review",
                "source-law-change-review",
                "source-law-change-review",
                [
                    "source-law-review-template.md",
                    "production-readiness-report.json",
                    "production-readiness-verification-report.json",
                    "release-evidence-reviewer-blockers.md"
                ],
                [
                    "source-law-snapshot-fingerprint",
                    "source-law-review-ledger",
                    "Per-source reachability, effective-date and wording impact rows",
                    "Qualified-accountant source-law sign-off"
                ],
                "Compare pinned CRO, Revenue, FRC and Charities Regulator sources, then retain the signed source-law review."),
            Item(
                "externalRosIxbrlValidation",
                "External ROS/iXBRL validation",
                "external-ros-ixbrl-validation-template.md",
                "External ROS/iXBRL validation reviewer",
                "external-ros-validation-evidence",
                "external-ros-validation-evidence",
                "external-ros-validation-evidence",
                [
                    "external-ros-ixbrl-validation-template.md",
                    "production-readiness-report.json",
                    "release-evidence-reviewer-blockers.md"
                ],
                [
                    "External validation provider/reference",
                    "Generated iXBRL artifact hashes",
                    "Retained taxonomy package references",
                    "Accepted/remediated validation rows for every golden scenario"
                ],
                "Retain external ROS/iXBRL validation references for the exact generated artifacts."),
            Item(
                "qualifiedAccountantAcceptance",
                "Qualified-accountant acceptance",
                "qualified-accountant-acceptance-template.md",
                "Named qualified accountant",
                "qualified-accountant-final-signoff",
                "accountant-final-signoff",
                "qualified-accountant-final-signoff",
                [
                    "qualified-accountant-acceptance-template.md",
                    "production-readiness-report.json",
                    "accountant-workbench-evidence-report.json",
                    "release-evidence-reviewer-blockers.md"
                ],
                [
                    "Named accountant identity and professional body",
                    "Accepted output/gate/source-law/wording/workbench rows",
                    "Scenario walkthrough evidence",
                    "Route acceptance evidence"
                ],
                "Walk the golden corpus through the live workflow and retain named professional acceptance."),
            Item(
                "manualHandoffAcceptance",
                "Manual handoff acceptance",
                "manual-handoff-acceptance-template.md",
                "Named manual handoff reviewer",
                "manual-accountant-acceptance",
                "golden-corpus-accountant-acceptance",
                "manual-accountant-acceptance",
                [
                    "manual-handoff-acceptance-template.md",
                    "production-readiness-report.json",
                    "release-evidence-reviewer-blockers.md"
                ],
                [
                    "Signed auditor-report evidence",
                    "Manual handoff note",
                    "Filing readiness snapshot",
                    "Unsupported-path evidence references"
                ],
                "Retain reviewer acceptance for audit-required and unsupported-path handoff evidence."),
            Item(
                "monitoringProviderConfirmation",
                "Monitoring-provider confirmation",
                "monitoring-provider-confirmation-template.md",
                "Named release operator",
                "production-monitoring",
                "production-smoke-and-backup",
                "production-stack-smoke",
                [
                    "monitoring-provider-confirmation-template.md",
                    "monitoring-error-routing-report.json",
                    "structured-log-report.json",
                    "release-evidence-reviewer-blockers.md"
                ],
                [
                    "monitoring-error-routing-report.json",
                    "structured-log-report.json",
                    "Provider event URL or reference",
                    "Matched monitoring smoke correlation id"
                ],
                "Confirm the controlled monitoring smoke event in the configured provider and retain operator acceptance.")
        ];
    }

    private static IReadOnlyList<HumanReleaseEvidenceCloseoutStep> BuildHumanReleaseEvidenceCloseout(
        IReadOnlyList<HumanReleaseEvidenceGate> humanReleaseEvidence)
    {
        var templateCount = humanReleaseEvidence.Count;
        var pendingCount = humanReleaseEvidence.Count(item => item.BlocksRelease);

        return
        [
            new(
                "pick-up-reviewer-workspace",
                "Pick up reviewer workspace",
                1,
                "Download the release-evidence-reviewer-workspace artifact and inspect release-evidence-reviewer-index.md, release-evidence-reviewer-completion.json and pending human blocker inventory before assigning reviewers.",
                "release-evidence-reviewer-workspace",
                pendingCount > 0),
            new(
                "complete-human-evidence-templates",
                "Complete templates",
                2,
                $"Complete {templateCount} retained Markdown templates with named reviewers, UTC timestamps, retained evidence references, accepted decisions and signatures.",
                "Docs/release-evidence/*.md",
                pendingCount > 0),
            new(
                "run-release-evidence-verifier",
                "Run release evidence verifier",
                3,
                "Generate release-evidence-report.json for the exact candidate after the human templates are complete.",
                "scripts/verify-release-evidence.ps1",
                true),
            new(
                "confirm-human-evidence-completion",
                "Confirm human completion",
                4,
                $"Confirm {templateCount} accepted humanEvidenceCompletion rows and productionScorecardCompletion status complete at 1,000/1,000 with zero open engineering or human/external controls and zero blocking failures in release-evidence-report.json.",
                "release-evidence-report.json",
                true),
            new(
                "verify-release-artifact-pack",
                "Verify final artifact pack",
                5,
                "Run the final pack verifier against the same commit SHA and GitHub Actions run URL.",
                "scripts/verify-release-artifact-pack.ps1",
                true)
        ];
    }

    private static IReadOnlyList<GoldenFilingCorpusProofPoint> ProofPoints(
        string automatedVerifier,
        IReadOnlyList<(string Area, string ExpectedEvidence)> proofPoints) =>
        proofPoints
            .Select(proof => new GoldenFilingCorpusProofPoint(
                proof.Area,
                proof.ExpectedEvidence,
                automatedVerifier,
                Required: true))
            .ToArray();

    private static IReadOnlyList<GoldenFilingCorpusVerifier> Verifiers(params string[] verifierNames) =>
        verifierNames
            .Select(name =>
            {
                var postgres = name.StartsWith("GoldenCorpusPostgresReleaseTests.", StringComparison.Ordinal);
                return new GoldenFilingCorpusVerifier(
                    name,
                    $"dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~{name}",
                    "default-ci",
                    RunsInDefaultCi: true,
                    postgres
                        ? "PostgreSQL 16 with ACCOUNTS_REQUIRE_POSTGRES_GOLDEN_CORPUS=true; missing connection fails the dedicated release gate"
                        : "EF Core InMemory golden fixture; paired with the mandatory PostgreSQL corpus verifier",
                    postgres
                        ? "all-scenario PostgreSQL decision, rendering and fail-closed validation-ingestion workflow"
                        : "scenario-specific machine review-pack workflow");
            })
            .ToArray();
}
