using Accounts.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Accounts.Api.Services;

// Transport contracts are defined separately so this service remains focused on report assembly.

public partial class ProductionReadinessReportService(AccountsDbContext db)
{
    public async Task<ProductionReadinessReport> GetReportAsync(CancellationToken cancellationToken = default)
    {
        var companies = await db.Companies.CountAsync(cancellationToken);
        var periods = await db.AccountingPeriods.CountAsync(cancellationToken);
        var sourceSnapshot = IrishStatutoryRuleSources.BuildSnapshot();
        var areas = BuildAreas();
        var goldenCorpus = BuildGoldenCorpus();
        var statutoryRuleMatrix = BuildStatutoryRuleMatrix();
        var statutoryRulesCoverage = BuildStatutoryRulesCoverage();
        var manualHandoffPaths = BuildManualHandoffPaths();
        var operationalGates = BuildOperationalGates();
        var assuranceActions = BuildAssuranceActions();
        var completionTracks = BuildCompletionTracks();
        var auditabilityControls = BuildAuditabilityControls();
        var auditEvidenceTimeline = BuildAuditEvidenceTimeline();
        var auditEvidencePack = BuildAuditEvidencePack();
        var monitoringControls = BuildMonitoringControls();
        var dependencyPolicyControls = BuildDependencyPolicyControls();
        var deploymentSafetyControls = BuildDeploymentSafetyControls();
        var operationsEvidencePack = BuildOperationsEvidencePack();
        var releaseReviewChecklist = BuildReleaseReviewChecklist(assuranceActions, operationalGates);
        var releaseVerificationManifest = BuildReleaseVerificationManifest();
        var humanReleaseEvidence = BuildHumanReleaseEvidence(releaseReviewChecklist, releaseVerificationManifest);
        var humanReleaseEvidenceCloseout = BuildHumanReleaseEvidenceCloseout(humanReleaseEvidence);
        var releaseBlockerRegister = BuildReleaseBlockerRegister(
            completionTracks,
            assuranceActions,
            releaseReviewChecklist);
        var accountantAcceptanceCriteria = BuildAccountantAcceptanceCriteria(goldenCorpus);
        var goldenEvidenceLedger = BuildGoldenEvidenceLedger(goldenCorpus, accountantAcceptanceCriteria);
        var goldenVerifierManifest = BuildGoldenVerifierManifest(goldenCorpus);
        var accountantAcceptanceSummary = BuildAccountantAcceptanceSummary(goldenCorpus, accountantAcceptanceCriteria);
        var accountantWorkflowWalkthroughProtocol = BuildAccountantWorkflowWalkthroughProtocol(goldenCorpus);
        var visualQaCoverage = BuildVisualQaCoverage();
        var accountantJourneyAcceptanceChecklist = BuildAccountantJourneyAcceptanceChecklist(goldenCorpus, visualQaCoverage);
        var accountantWorkflowEvidencePack = BuildAccountantWorkflowEvidencePack(accountantJourneyAcceptanceChecklist);
        var accountantWalkthroughEvidenceMatrix = BuildAccountantWalkthroughEvidenceMatrix(
            goldenCorpus,
            accountantAcceptanceCriteria,
            accountantJourneyAcceptanceChecklist,
            accountantWorkflowEvidencePack);
        var workbenchVisualAcceptanceRegister = BuildWorkbenchVisualAcceptanceRegister(visualQaCoverage);
        var sourceLawTraceability = BuildSourceLawTraceability(
            sourceSnapshot,
            goldenCorpus,
            statutoryRuleMatrix,
            statutoryRulesCoverage,
            accountantAcceptanceCriteria);
        var sourceLawMaintenanceProtocol = BuildSourceLawMaintenanceProtocol(sourceSnapshot);
        var sourceLawReviewLedger = BuildSourceLawReviewLedger(sourceSnapshot, releaseReviewChecklist);
        var revenueTaxonomyRanges = BuildRevenueTaxonomyRanges();
        var assurancePacket = BuildAssurancePacket(
            sourceSnapshot,
            goldenCorpus,
            statutoryRuleMatrix,
            statutoryRulesCoverage,
            operationalGates,
            assuranceActions,
            visualQaCoverage);
        var productionScorecard = BuildProductionScorecard(
            completionTracks,
            releaseBlockerRegister);

        return new ProductionReadinessReport(
            DateTime.UtcNow,
            "review-required",
            companies,
            periods,
            sourceSnapshot,
            sourceLawTraceability,
            sourceLawMaintenanceProtocol,
            sourceLawReviewLedger,
            revenueTaxonomyRanges,
            assurancePacket,
            productionScorecard,
            accountantAcceptanceCriteria,
            accountantAcceptanceSummary,
            accountantWorkflowWalkthroughProtocol,
            accountantJourneyAcceptanceChecklist,
            accountantWorkflowEvidencePack,
            accountantWalkthroughEvidenceMatrix,
            workbenchVisualAcceptanceRegister,
            areas,
            goldenCorpus,
            goldenEvidenceLedger,
            goldenVerifierManifest,
            statutoryRuleMatrix,
            statutoryRulesCoverage,
            manualHandoffPaths,
            operationalGates,
            assuranceActions,
            completionTracks,
            releaseBlockerRegister,
            auditabilityControls,
            auditEvidenceTimeline,
            auditEvidencePack,
            monitoringControls,
            dependencyPolicyControls,
            deploymentSafetyControls,
            operationsEvidencePack,
            releaseReviewChecklist,
            releaseVerificationManifest,
            humanReleaseEvidence,
            humanReleaseEvidenceCloseout,
            visualQaCoverage);
    }

    public static string ComputeAssurancePacketId(ProductionAssurancePacket packet)
    {
        var canonical = string.Join(
            "\n",
            [
                packet.PacketVersion,
                packet.Status,
                packet.SourceLawSnapshotHash,
                packet.GoldenCorpusCovered.ToString(CultureInfo.InvariantCulture),
                packet.GoldenCorpusTotal.ToString(CultureInfo.InvariantCulture),
                packet.StatutoryRuleMatrixPaths.ToString(CultureInfo.InvariantCulture),
                packet.StatutoryRuleCoverageFamilies.ToString(CultureInfo.InvariantCulture),
                packet.VisualQaExpectedScreenshots.ToString(CultureInfo.InvariantCulture),
                packet.RequiredOperationalGates.ToString(CultureInfo.InvariantCulture),
                packet.OpenCriticalActions.ToString(CultureInfo.InvariantCulture),
                string.Join("|", packet.EvidenceItems.Order(StringComparer.Ordinal)),
                string.Join("|", packet.ReleaseBlockers.Order(StringComparer.Ordinal))
            ]);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return $"assurance-sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static ProductionAssurancePacket BuildAssurancePacket(
        SourceLawSnapshot sourceSnapshot,
        IReadOnlyList<GoldenFilingCorpusScenario> goldenCorpus,
        IReadOnlyList<StatutoryRuleMatrixEntry> statutoryRuleMatrix,
        IReadOnlyList<StatutoryRulesCoverageItem> statutoryRulesCoverage,
        IReadOnlyList<OperationalGate> operationalGates,
        IReadOnlyList<ProductionReadinessAssuranceAction> assuranceActions,
        VisualQaCoverage visualQaCoverage)
    {
        var evidenceItems = new[]
        {
            "source-law-snapshot-fingerprint",
            "source-law-traceability-index",
            "source-law-maintenance-protocol",
            "source-law-review-ledger",
            "revenue-taxonomy-range-evidence",
            "golden-filing-corpus",
            "golden-evidence-ledger",
            "golden-verifier-manifest",
            "statutory-rules-matrix",
            "statutory-rules-coverage",
            "audit-evidence-timeline",
            "production-audit-evidence-pack",
            "visual-smoke-screenshots",
            "accountant-workbench-evidence-report",
            "production-operational-gates",
            "production-readiness-report",
            "production-readiness-verification-report",
            "postgres-migration-upgrade-gate",
            "dependency-policy-controls",
            "deployment-safety-controls",
            "operations-evidence-pack",
            "release-blocker-register",
            "release-review-checklist",
            "release-verification-manifest",
            "human-release-evidence",
            "accountant-acceptance-criteria",
            "accountant-acceptance-summary",
            "accountant-workflow-walkthrough-protocol",
            "accountant-journey-acceptance-checklist",
            "accountant-workflow-evidence-pack",
            "accountant-walkthrough-evidence-matrix",
            "workbench-visual-acceptance-register",
            "production-completion-map",
            "production-scorecard"
        };
        var releaseBlockers = assuranceActions
            .Where(action => action.Status != "complete")
            .OrderBy(action => action.Priority == "critical" ? 0 : action.Priority == "high" ? 1 : 2)
            .ThenBy(action => action.Code, StringComparer.Ordinal)
            .Select(action => action.Code == "qualified-accountant-signoff"
                ? "Qualified accountant sign-off required"
                : $"{action.Label} required")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var packetWithoutId = new ProductionAssurancePacket(
            "",
            "production-assurance-packet-v1",
            releaseBlockers.Length == 0 ? "ready" : "review-required",
            sourceSnapshot.ContentHash,
            goldenCorpus.Count(scenario => scenario.CoverageStatus == "covered"),
            goldenCorpus.Count,
            statutoryRuleMatrix.Count,
            statutoryRulesCoverage.Count,
            visualQaCoverage.ExpectedScreenshotCount,
            operationalGates.Count(gate => gate.Required),
            assuranceActions.Count(action => action.Priority == "critical" && action.Status != "complete"),
            evidenceItems,
            releaseBlockers);

        return packetWithoutId with { PacketId = ComputeAssurancePacketId(packetWithoutId) };
    }

    private static ProductionScorecard BuildProductionScorecard(
        IReadOnlyList<ProductionReadinessCompletionTrack> completionTracks,
        IReadOnlyList<ProductionReleaseBlocker> releaseBlockers)
    {
        var trackCodes = completionTracks.Select(track => track.Code).ToHashSet(StringComparer.Ordinal);
        var blockerCodes = releaseBlockers.Select(blocker => blocker.Code).ToHashSet(StringComparer.Ordinal);

        ProductionScorecardCategory Category(
            string code,
            string label,
            string status,
            IReadOnlyList<string> currentEvidence,
            IReadOnlyList<string> remainingGaps,
            IReadOnlyList<string> completionTrackCodes,
            IReadOnlyList<string> releaseBlockerCodes,
            IReadOnlyList<ProductionScorecardControl> controls)
        {
            foreach (var trackCode in completionTrackCodes)
            {
                if (!trackCodes.Contains(trackCode))
                    throw new InvalidOperationException($"Production scorecard category {code} references unknown completion track {trackCode}.");
            }

            foreach (var blockerCode in releaseBlockerCodes)
            {
                if (!blockerCodes.Contains(blockerCode))
                    throw new InvalidOperationException($"Production scorecard category {code} references unknown release blocker {blockerCode}.");
            }

            if (controls.Count == 0)
                throw new InvalidOperationException($"Production scorecard category {code} must define weighted controls.");

            if (controls.Select(control => control.Code).Distinct(StringComparer.Ordinal).Count() != controls.Count)
                throw new InvalidOperationException($"Production scorecard category {code} contains duplicate control codes.");

            foreach (var control in controls)
            {
                if (control.Weight <= 0)
                    throw new InvalidOperationException($"Production scorecard control {control.Code} must have a positive weight.");
                if (control.Evidence.Count == 0)
                    throw new InvalidOperationException($"Production scorecard control {control.Code} must identify objective evidence.");
                if (!control.Passed && control.BlockingAuditItemIds.Count == 0)
                    throw new InvalidOperationException($"Open production scorecard control {control.Code} must identify blocking audit items.");
            }

            var currentScore = controls.Where(control => control.Passed).Sum(control => control.Weight);
            var targetScore = controls.Sum(control => control.Weight);

            return new ProductionScorecardCategory(
                code,
                label,
                currentScore,
                targetScore,
                status,
                currentEvidence,
                remainingGaps,
                completionTrackCodes,
                releaseBlockerCodes,
                controls);
        }

        static ProductionScorecardControl Control(
            string code,
            string label,
            int weight,
            string assuranceClass,
            bool passed,
            IReadOnlyList<string> evidence,
            IReadOnlyList<string>? blockingAuditItemIds = null) =>
            new(
                code,
                label,
                weight,
                assuranceClass,
                passed ? "passed" : "open",
                passed,
                evidence,
                blockingAuditItemIds ?? []);

        var categories = new[]
        {
            Category(
                "architecture-documentation",
                "Architecture and documentation",
                "remediation-required",
                [
                    "CLAUDE.md is the canonical architecture/development guide.",
                    "AGENTS.md carries the active production-readiness handoff.",
                    "Production runbook links release evidence templates for source-law review, visual QA, monitoring provider confirmation and qualified-accountant acceptance.",
                    "scripts/verify-release-evidence.ps1 validates completed release evidence templates before real filing use, including source-law source coverage.",
                    "scripts/verify-release-artifact-pack.ps1 validates the collected release artifact reports and the retained human release-evidence templates as one exact evidence pack with release candidate identity and SHA-256 inventory.",
                    "Production readiness report exposes the six human release-evidence gates with template files, reviewer roles, sign-off gates, required retained evidence and full per-gate reviewerPickupFiles.",
                    "Release evidence reviewer workspace verification now inventories pending human-evidence blockers and rejects prepared human templates whose top-level reviewer/accountant identity, signature or acceptance checkbox fields are filled before named human sign-off.",
                    "Human release-evidence closeout now starts from the prepared release-evidence-reviewer-workspace artifact, its blocker inventory, its retained reviewer handoff files and assignment-ledger pickup files before reviewers complete the six templates.",
                    "release-evidence-report.json records productionScorecardCompletion against the independently audited 1,000-point control ledger and stays blocked while any engineering or human/external control remains open.",
                    "CI artifacts now prove production safety, dependency audit, monitoring smoke, structured logs, visual smoke and backup restore drill."
                ],
                [
                    "Keep AGENTS.md aligned with the latest green CI run after every release-evidence commit.",
                    "Complete the checked-in release evidence templates with named human reviewers and retain release-evidence-report.json.",
                    "Complete source-law-review-template.md with a named reviewer and qualified-accountant source-law sign-off."
                ],
                ["backend-code", "frontend-ui-ux", "frontend-code"],
                ["backend-code:source-law-change-review", "frontend-ui-ux:light-dark-visual-regression"],
                [
                    Control(
                        "canonical-engineering-guidance",
                        "Canonical engineering guidance",
                        35,
                        "code",
                        true,
                        ["CLAUDE.md and AGENTS.md define architecture, operating boundaries and the active release handoff."]),
                    Control(
                        "release-runbook-contract",
                        "Release runbook and evidence contract",
                        32,
                        "machine",
                        true,
                        ["Docs/operations/production-runbook.md and scripts/verify-release-evidence.ps1 provide machine-verifiable release instructions."]),
                    Control(
                        "typed-readiness-evidence-contract",
                        "Control-derived typed readiness assessment",
                        30,
                        "machine",
                        true,
                        ["P0-REL-002 is enforced by weighted backend controls, exact-candidate release verifiers, frontend runtime invariants and readiness render tests."]),
                    Control(
                        "canonical-documentation-reconciliation",
                        "Canonical documentation matches the implementation",
                        18,
                        "code",
                        true,
                        ["README.md, CLAUDE.md, AGENTS.md and the audit verification matrix identify the current EF/OpenAPI/runtime inventory, executable direct-project test gate and release-blocked semantics."]),
                    Control(
                        "independent-release-review",
                        "Independent legal, professional and operational review",
                        35,
                        "human-external",
                        false,
                        ["Release evidence templates and HUMAN-001 through HUMAN-007 require genuine retained reviewer evidence."],
                        ["HUMAN-001", "HUMAN-002", "HUMAN-003", "HUMAN-004", "HUMAN-005", "HUMAN-006", "HUMAN-007"])
                ]),
            Category(
                "backend-statutory-accounting-engine",
                "Backend statutory/accounting engine",
                "remediation-required",
                [
                    "Golden filing corpus covers micro LTD, small abridged LTD, DAC small, CLG charity and medium audit-required manual handoff.",
                    "Source-law snapshot, traceability, review ledger and Revenue taxonomy ranges are exposed in the readiness report.",
                    "Filing readiness profiles, generated PDF/iXBRL evidence and audit snapshots are backed by automated tests.",
                    "Qualified-accountant acceptance evidence now uses canonical golden corpus scenario codes and the release verifier reports required scenario, route and artifact coverage.",
                    "scripts/verify-release-evidence.ps1 now rejects qualified-accountant acceptance unless every golden scenario decision and every route evidence acceptance row is explicitly accepted.",
                    "Qualified-accountant acceptance now requires explicit accepted scenario scope cells for outputs, gates, source-law evidence, wording and workbench journey before a scenario decision can pass.",
                    "Qualified-accountant scenario walkthrough rows now must match the exact qualified-accountant-walkthrough-ledger anchor for every canonical golden corpus scenario.",
                    "Qualified-accountant route walkthrough rows now require exact yes decision-question cells and exact accepted evidence cells before route acceptance can pass.",
                    "Qualified-accountant route decision-question cells now reject accepted-style ambiguous text so professional evidence acceptance stays in the dedicated evidence column.",
                    "Qualified-accountant route walkthrough rows now require route-specific accountant-workbench evidence anchors for every accepted route.",
                    "Qualified-accountant route acceptance now requires a real retained workbench evidence reference for every accepted route, tied to accountant-workbench-evidence-report.json.",
                    "Qualified-accountant route walkthrough notes now must match the exact qualified-accountant-route-walkthrough anchor for every accepted route.",
                    "Release evidence reviewer workspaces now prefill qualified-accountant scenario walkthrough, route workbench evidence and route walkthrough anchors while leaving all professional acceptance cells blank.",
                    "Qualified-accountant acceptance top-level evidence now rejects placeholder accountant name, qualification, reviewer capacity and signature fields before professional sign-off evidence can pass.",
                    "External ROS/iXBRL validation evidence now has a checked-in template and the release verifier rejects rows without real references, retained taxonomy package references, accepted/remediated warning status and accepted scenario decisions.",
                    "External ROS/iXBRL validation references and retained taxonomy package references now must include the matching golden corpus scenario code before acceptance can pass.",
                    "External ROS/iXBRL validation rows now must match exact retained external validation and taxonomy package ledger anchors for every canonical golden corpus scenario.",
                    "External ROS/iXBRL validation warnings/errors now require exact none, accepted or remediated values, and scenario decisions require exact accepted values before evidence can pass.",
                    "External ROS/iXBRL validation top-level evidence now rejects placeholder provider, environment, run/reference, report, taxonomy and company/period fields, and requires a retained XHTML, HTML or ZIP iXBRL artifact name.",
                    "Release evidence reviewer workspaces now prefill external ROS/iXBRL validation and taxonomy package anchors for every golden corpus scenario while leaving artifact hash, warnings/errors and decision cells blank.",
                    "Source-law review evidence now has a checked-in template and the release verifier rejects monitored-source rows without concrete URL reachability, dated or not-dated effective-date review, guidance comparison, platform impact classification and exact accepted decisions.",
                    "Source-law review platform impact cells now require exact no change, reflected or blocking values before review evidence can pass.",
                    "Source-law review notes now must match the exact source-law-review-ledger anchor for every monitored source before acceptance can pass.",
                    "Release evidence reviewer workspaces now prefill source-law snapshot fingerprint, content hash and per-source review ledger note anchors from the retained production-readiness report while leaving source review decision cells blank.",
                    "Source-law review top-level evidence now requires an exact source-law-snapshot-fingerprint retained evidence anchor plus real reviewer and qualified-accountant identity, signature and sign-off fields before review evidence can pass.",
                    "Manual handoff acceptance now has a checked-in template and the release verifier rejects audit-required or unsupported-path rows without real evidence references and accepted reviewer decisions.",
                    "Manual handoff scenario and unsupported-path decisions now require exact accepted reviewer decisions before acceptance evidence can pass.",
                    "Manual handoff evidence references now must include the matching scenario or unsupported-path code before acceptance can pass.",
                    "Manual handoff evidence rows now must match exact retained auditor-report, handoff-note, readiness-snapshot and unsupported-path anchors before acceptance can pass.",
                    "Release evidence reviewer workspaces now prefill manual handoff scenario and unsupported-path evidence anchors while leaving scenario and reviewer decision cells blank.",
                    "CI now retains production-readiness-report.json from the live smoke stack, proving the exact source-law snapshot, golden corpus, scorecard and release blockers exposed by the candidate.",
                    "scripts/verify-production-readiness-report.ps1 emits production-readiness-verification-report.json and proves the captured live report has complete source-law, golden-corpus, scorecard, blocker, visual-QA and release-manifest coverage."
                ],
                [
                    "Complete and retain verified source-law review evidence for every monitored source before relying on generated packs.",
                    "Run and retain verified qualified-accountant acceptance across every canonical golden corpus scenario.",
                    "Complete and retain verified external ROS/iXBRL validation evidence for generated packs.",
                    "Complete and retain verified manual handoff acceptance for audit-required and unsupported paths before relying on outputs."
                ],
                ["backend-code"],
                [
                    "backend-code:qualified-accountant-signoff",
                    "backend-code:external-ros-validation",
                    "backend-code:accountant-acceptance-walkthrough"
                ],
                [
                    Control(
                        "accounting-engine-baseline",
                        "Accounting engine and statement baseline",
                        65,
                        "code",
                        true,
                        ["Accounts workflow tests cover trial balance, statements, adjustments and multi-period calculations."]),
                    Control(
                        "statutory-workflow-baseline",
                        "Statutory workflow baseline",
                        45,
                        "code",
                        true,
                        ["Filing regime, deadline, readiness and workflow services have automated behavioral coverage."]),
                    Control(
                        "document-generation-baseline",
                        "PDF, tax and iXBRL generation baseline",
                        35,
                        "code",
                        true,
                        ["Generated PDF, tax support and iXBRL artifacts are exercised by backend tests and retained evidence manifests."]),
                    Control(
                        "golden-corpus-baseline",
                        "Automated golden corpus baseline",
                        35,
                        "machine",
                        true,
                        ["FilingGoldenCorpusScenarioTests cover the five canonical scenario codes in machine CI."]),
                    Control(
                        "filing-evidence-baseline",
                        "Filing evidence and handoff baseline",
                        25,
                        "machine",
                        true,
                        ["Production readiness and release-evidence verifiers retain artifact identities and block missing reviewer evidence."]),
                    Control(
                        "final-release-containment",
                        "Central exact-hash final release containment",
                        10,
                        "code",
                        true,
                        ["FilingReleaseGate and the bound external-handoff services enforce exact candidate/artifact hashes and retain the no-direct-submission boundary across final outputs and workflow transitions."]),
                    Control(
                        "accounting-correctness-remediation",
                        "Accounting and classification correctness",
                        35,
                        "code",
                        true,
                        ["Behavioral double-entry, classification, retained duplicate-review and accountant working-paper gates close P0-ACC-001 through P1-ACC-005."]),
                    Control(
                        "statutory-output-correctness",
                        "Statutory output and disclosure correctness",
                        30,
                        "code",
                        false,
                        ["The implemented statutory-output controls are covered by retained machine evidence; the remaining director-loan, tax-calculation and filing-acceptance findings require genuine qualified-accountant and external evidence."],
                        ["P1-STAT-007", "P1-TAX-001", "P1-TAX-002"]),
                    Control(
                        "external-ixbrl-acceptance",
                        "Complete externally validated Revenue iXBRL",
                        35,
                        "human-external",
                        false,
                        ["The external ROS/iXBRL template requires validator response, taxonomy hash and exact artifact hash."],
                        ["HUMAN-003"]),
                    Control(
                        "professional-artifact-approval",
                        "Verified professional approval and signed auditor evidence",
                        20,
                        "human-external",
                        false,
                        ["Qualified-accountant and manual-handoff templates require genuine professional identities and signed evidence."],
                        ["HUMAN-004", "HUMAN-005"]),
                    Control(
                        "independent-golden-corpus",
                        "Independently derived golden corpus",
                        15,
                        "human-external",
                        false,
                        ["P0-QA-001 requires independently reviewed expected figures, public workflows and mandatory PostgreSQL execution."],
                        ["P0-QA-001"])
                ]),
            Category(
                "frontend-accountant-workbench",
                "Frontend accountant workbench",
                "remediation-required",
                [
                    "Production readiness, dashboard, company, period, filing review, financial statements and workbench preview routes are in the visual smoke plan.",
                    "Shared workbench primitives and route-level render tests cover the main accountant journey.",
                    "Dense tables, workflow rails, blocker summaries and permission-denied states are surfaced in the workbench.",
                    "node scripts/verify-visual-smoke-artifacts.mjs now writes visual-smoke-evidence-report.json covering screenshot hashes, byte sizes, PNG dimensions, nonblank pixel diversity, per-screenshot layout, axe-core WCAG 2.2 A/AA, theme-contrast and responsive-workflow results, and route/theme/viewport completeness before human review.",
                    "Docs/release-evidence/visual-qa-signoff-template.md and scripts/verify-release-evidence.ps1 now require named reviewers to record the visual smoke nonblank pixel and contrast metrics before visual QA evidence can pass.",
                    "Visual QA sign-off now requires exact pass decisions for every canonical state across light and dark themes at mobile, tablet and desktop viewports.",
                    "Visual QA route capture cells now reject accepted-style ambiguous text so reviewer limitations must stay in retained route notes or references.",
                    "Visual QA state notes now must match the exact visual-smoke-evidence-report.json routeCoverage anchor for every canonical state before sign-off evidence can pass.",
                    "Release evidence reviewer workspaces now prefill visual QA route note anchors from visual-smoke-evidence-report.json while leaving all route pass/fail cells blank for named human review.",
                    "Visual QA release evidence now requires exact visual-smoke manifest, visual evidence report and accountant workbench evidence report filenames before sign-off evidence can pass.",
                    "Visual QA top-level evidence now rejects placeholder reviewer name, reviewer role and reviewer signature fields before human visual sign-off evidence can pass.",
                    "node scripts/verify-accountant-workbench-evidence.mjs now writes accountant-workbench-evidence-report.json proving route, workflow-stage, theme, viewport, layout-check and review-check coverage.",
                    "scripts/verify-release-artifact-pack.ps1 and scripts/verify-ci-machine-evidence-pack.ps1 reject visual evidence unless every screenshot reports passed console-error, horizontal-overflow, visible-text-overlap, axe-core WCAG 2.2 A/AA, automated theme-contrast and responsive-workflow checks.",
                    "Visual smoke and accountant-workbench evidence now retain and re-verify each route's expected accountant decision text across light/dark mobile/tablet/desktop screenshots.",
                    "Release artifact and CI machine evidence pack verifiers now require exact visual-smoke top-level themes, viewports, planned viewport dimensions, layout/contrast result counts, minimum contrast ratio, retained screenshot bytes and per-route coverage before retained visual evidence can pass.",
                    "Release artifact and CI machine evidence pack verifiers now require the exact visual-smoke screenshot matrix for every route, theme and viewport, including route keys, file names, expected accountant decision text and required-review status.",
                    "Release artifact and CI machine evidence pack verifiers now cross-check visual-smoke-manifest.json route audits and screenshot rows against visual-smoke-evidence-report.json before retained visual evidence can pass.",
                    "Release artifact and CI machine evidence pack verifiers now require every visual-smoke screenshot row to retain a positive byte size and canonical sha256:<64 lowercase hex> checksum before retained visual evidence can pass.",
                    "Release artifact and CI machine evidence pack verifiers now require every visual-smoke screenshot row to match a retained PNG file by file name, byte size and sha256 checksum before retained visual evidence can pass.",
                    "accountant-workbench-evidence-report.json now includes route acceptance rows with stable route keys, expected decision text, blocking status and qualified-accountant route acceptance evidence for every workbench route.",
                    "Release artifact and CI machine evidence pack verifiers now require exact accountant-workbench route acceptance names, route keys, expected decision text and per-route acceptance evidence ids for every workbench route.",
                    "Release artifact and CI machine evidence pack verifiers now require exact accountant-workbench route acceptance labels, screenshot-review evidence anchors and required-review status for every workbench route.",
                    "Release artifact and CI machine evidence pack verifiers now require exact accountant-workbench route readiness screenshot counts, layout-check counts, contrast counts, minimum contrast ratios, required-review status and required review checks for every workbench route.",
                    "Release artifact and CI machine evidence pack verifiers now require exact accountant-workbench route workflow-stage coverage and light/dark mobile/tablet/desktop theme-viewport coverage before retained route readiness evidence can pass.",
                    "Release artifact and CI machine evidence pack verifiers now require exact accountant-workbench required coverage for workflow stages, themes, viewports, review checks, layout checks, expected-text checks, layout/contrast evidence and retained visual evidence files.",
                    "Frontend parser invariants now require the CI machine evidence pack, production smoke, readiness verification, visual smoke and manual release-verification rows before rendering readiness data.",
                    "Production readiness workbench now renders the pending human release-evidence reviewer queue with template files, reviewer roles, sign-off gates, next actions and retained reviewer pickup files."
                ],
                [
                    "Complete named visual QA review against the 192-capture light/dark mobile/tablet/desktop canonical state manifest and visual-smoke-evidence-report.json.",
                    "Complete manual keyboard, screen-reader, focus-indicator, contrast and responsive usability review without waiving axe-incomplete rules.",
                    "Record qualified-accountant route acceptance for outputs, gates, wording and evidence."
                ],
                ["frontend-ui-ux", "frontend-code"],
                [
                    "frontend-ui-ux:light-dark-visual-regression",
                    "frontend-ui-ux:accountant-acceptance-walkthrough",
                    "frontend-code:light-dark-visual-regression"
                ],
                [
                    Control(
                        "workbench-primitives",
                        "Shared accountant workbench primitives",
                        35,
                        "code",
                        true,
                        ["PageShell, WorkflowRail, ReviewPanel, DataGrid and decision-summary primitives are used across primary routes."]),
                    Control(
                        "primary-route-baseline",
                        "Primary accountant route baseline",
                        40,
                        "code",
                        true,
                        ["Dashboard, company, period, statements, filing and readiness routes have render coverage."]),
                    Control(
                        "frontend-readiness-contract",
                        "Frontend readiness API contract",
                        30,
                        "machine",
                        true,
                        ["Runtime production-readiness parsing and API-client verification reject readiness evidence drift."]),
                    Control(
                        "visual-smoke-baseline",
                        "Visual smoke evidence baseline",
                        28,
                        "machine",
                        true,
                        ["Visual smoke artifacts retain screenshots, dimensions, hashes, layout checks, axe-core WCAG 2.2 A/AA results, contrast checks and responsive workflow assertions for all 192 canonical captures."]),
                    Control(
                        "role-aware-ui-baseline",
                        "Role-aware UI baseline",
                        25,
                        "code",
                        true,
                        ["Shared permission helpers and permission-denied render states cover core routes."]),
                    Control(
                        "pagination-and-resource-state",
                        "Complete pagination and truthful resource states",
                        25,
                        "code",
                        true,
                        ["Server pagination, ID-backed selection and explicit loading/empty/partial/stale/error resource states are behaviorally covered for material workbench data."]),
                    Control(
                        "workflow-correctness",
                        "Dashboard, permissions, onboarding and response-contract correctness",
                        20,
                        "code",
                        true,
                        ["Batched deadlines, canonical permission capabilities, atomic onboarding and fail-closed runtime response contracts close P0-FE-003 through P0-FE-006."]),
                    Control(
                        "accessible-responsive-workbench",
                        "Accessible and responsive accountant workbench",
                        25,
                        "machine",
                        false,
                        ["All 192 canonical captures pass automated axe-core WCAG 2.2 A/AA and responsive workflow checks; genuine manual keyboard, screen-reader, focus/contrast and qualified-accountant acceptance remain open."],
                        ["P1-UX-001", "P1-UX-003", "P1-A11Y-001", "P1-VIS-002", "P1-FE-011"]),
                    Control(
                        "complete-visual-acceptance",
                        "Complete visual-state matrix and named review",
                        22,
                        "human-external",
                        false,
                        ["P1-VIS-001 and HUMAN-001 require every route/state at mobile, tablet and desktop plus named review."],
                        ["P1-VIS-001", "HUMAN-001"])
                ]),
            Category(
                "security-auth-tenant-platform-guardrails",
                "Security/auth/tenant/platform guardrails",
                "remediation-required",
                [
                    "Authenticated sessions, CSRF, secure cookie checks and post-logout 401 are covered by production smoke.",
                    "Request-scoped EF query filters backstop tenant isolation across company-owned and period-owned child tables.",
                    "Production compose gates enforce immutable images, migrate-only job ordering, demo seed blocking and structured monitoring evidence.",
                    "CI runs scripts/verify-no-direct-filing-submission.ps1 and retains no-direct-filing-submission-report.json, proving final CRO/ROS operations remain recorded workflow states with no outbound submission client wired.",
                    "no-direct-filing-submission-report.json now records release candidate commit/run identity, and the CI machine evidence pack plus release artifact pack reject stale no-direct evidence whose identity does not match the verified candidate.",
                    "scripts/verify-release-artifact-pack.ps1 validates dependency, production safety, monitoring, structured log, backup/restore, no-direct-submission, production-readiness verification, visual smoke, release-evidence, workspace verification and reviewer handoff reports together.",
                    "release-artifact-pack-report.json now records release candidate identity, per-report and reviewer-handoff SHA-256/byte-size evidence, plus a release-evidence workspace summary with the 21-file prepared workspace, six pending human blockers, six unassigned reviewer assignment rows, reviewerAssignmentPickupFileGuidanceCount and the per-gate reviewerAssignmentPickupFiles inventory.",
                    "CI runs scripts/verify-ci-machine-evidence-pack.ps1 and retains ci-machine-evidence-pack-report.json with exact commit/run identity, SHA-256 inventory for dependency, safety, certificate-verified PostgreSQL transport, monitoring, structured log, backup/restore, no-direct, readiness and visual/workbench evidence, plus a prepared reviewer-workspace summary proving the 21-file workspace and six unassigned reviewer assignment rows with complete per-gate pickup-file guidance.",
                    "CI now restores the repository-pinned EF tool, rejects pending model changes, migrates fresh PostgreSQL 16.4 and the supported previous-release floor, preserves five representative data/evidence groups, proves transactional failure rollback, and retains candidate-bound migration reports beside encrypted restore evidence.",
                    "Repository policy retains signed candidates, strict synchronized CI checks, pull requests, administrator enforcement, resolved conversations and blocked force-push/deletion. The current solo-owner local/private posture does not require a second-person or code-owner approval and therefore does not satisfy independent repository review; verify-github-governance.ps1 remains the stricter Public Production evidence gate.",
                    "Runtime images use locked and digest-pinned inputs, remove npm/Corepack/Yarn/pnpm tooling from the final frontend runtime, and are covered by retained scheduled vulnerability scans and SPDX SBOM evidence.",
                    "scripts/verify-production-readiness-report.ps1 now requires default-CI and manual release manifest rows, including the no-direct CRO/ROS control, CI machine evidence pack and release artifact pack, before accepting a captured readiness report.",
                    "scripts/verify-release-artifact-pack.ps1 now rejects release packs unless the retained production-readiness-verification-report.json proves every required default-CI and manual release manifest row.",
                    "scripts/verify-release-artifact-pack.ps1 and scripts/verify-ci-machine-evidence-pack.ps1 now reject visual evidence packs unless visual-smoke-evidence-report.json carries planned PNG viewport dimensions, passed layout-check results and automated theme-contrast smoke results for every screenshot.",
                    "scripts/verify-release-evidence.ps1 now rejects completed human evidence when release candidate identity, UTC timestamps, SHA-256 digests, external iXBRL artifact hashes, or monitoring log confirmation fields are malformed.",
                    "scripts/verify-release-evidence.ps1 emits a consistent releaseCandidate identity for all six human evidence templates, and scripts/verify-release-artifact-pack.ps1 rejects packs whose release-evidence-report.json identity does not match the pack CommitSha and GitHubActionsRunUrl.",
                    "scripts/verify-release-evidence.ps1 now emits SHA-256/byte-size manifest entries for all six human release-evidence templates, and scripts/verify-release-artifact-pack.ps1 requires those completed templates to be retained in the pack with matching hashes.",
                    "scripts/verify-release-artifact-pack.ps1 independently parses release-evidence-workspace-verification-report.json, release-evidence-machine-summary.json and release-evidence-report.json productionScorecardCompletion, requiring the same release candidate, exact prepared workspace inventory, retained machine-evidence provenance and hashes, humanReleaseEvidenceCloseoutStepCodes, reviewer pickup-file maps, the pending reviewer assignment ledger, retained reviewer handoff files and final 1,000/1,000 control-ledger proof.",
                    "Monitoring-provider confirmation evidence now requires real provider/event/correlation references, an HTTPS provider base URL, a matched structured-log smoke line and an explicit accepted operator decision.",
                    "Release evidence reviewer workspaces now prefill monitoring provider machine evidence from retained CI smoke/log reports while leaving provider confirmation, operator identity, decision and signature fields blank."
                ],
                [
                    "Confirm the controlled monitoring smoke event inside the configured provider and retain operator evidence.",
                    "Run and retain the full release-artifact-pack-report.json after release-evidence-report.json is completed with named human sign-offs.",
                    "Retain provider-console monitoring confirmation for the exact release candidate.",
                    "Retain production backup/restore, deployment ingress and encryption-at-rest/key-ownership evidence for the exact environment."
                ],
                ["backend-code"],
                ["backend-code:production-monitoring"],
                [
                    Control(
                        "session-csrf-password-baseline",
                        "Session, CSRF and password baseline",
                        35,
                        "code",
                        true,
                        ["Authentication tests and production smoke cover signed sessions, CSRF, lockout and password rotation."]),
                    Control(
                        "tenant-access-baseline",
                        "Tenant access-control baseline",
                        30,
                        "code",
                        true,
                        ["Tenant middleware, company access checks and EF query-filter metadata tests provide application-layer isolation."]),
                    Control(
                        "production-startup-safety",
                        "Production startup safety",
                        25,
                        "machine",
                        true,
                        ["ProductionSafetyService and compose verification reject unsafe production configuration."]),
                    Control(
                        "audit-monitoring-baseline",
                        "Audit-integrity and monitoring baseline",
                        25,
                        "machine",
                        true,
                        ["Audit hash/checkpoint tests, structured logs and controlled monitoring smoke are retained in CI."]),
                    Control(
                        "no-direct-filing-evidence",
                        "No-direct-filing and evidence baseline",
                        25,
                        "machine",
                        true,
                        ["verify-no-direct-filing-submission.ps1 and candidate-bound evidence-pack verifiers fail on outbound submission behavior."]),
                    Control(
                        "persistence-boundaries",
                        "Overposting and persistence-boundary enforcement",
                        30,
                        "code",
                        true,
                        ["Scalar command DTOs, ownership validation, restrictive keys/triggers and PostgreSQL negative cross-scope tests close P0-SEC-001 through P0-SEC-003."]),
                    Control(
                        "concurrency-and-deletion",
                        "Concurrent finalisation and recoverable deletion",
                        20,
                        "code",
                        true,
                        ["Period concurrency tokens/advisory locks and the quarantine/recovery deletion workflow have deterministic relational and endpoint coverage."]),
                    Control(
                        "privileged-identity-lifecycle",
                        "Privileged identity lifecycle, MFA and recent authentication",
                        25,
                        "code",
                        true,
                        ["Owner-managed invite/reset/unlock/offboard, encrypted MFA/recovery, session revocation, breached-password and recent-TOTP gates have behavioral plus PostgreSQL coverage."]),
                    Control(
                        "supply-chain-and-operations",
                        "Supply-chain, governance and production operations",
                        25,
                        "machine",
                        false,
                        ["Build-once digest promotion, reproducible locked inputs, retained scheduled scan/SBOM evidence, restricted readiness evidence and domain audit coverage are implemented. Independent protected-branch review is deliberately open under the current solo-owner local/private posture, alongside production backup, live monitoring confirmation and deployment encryption evidence."],
                        ["P1-OPS-003", "P1-OPS-005", "P1-OPS-006", "P1-OPS-008", "P2-FE-009"]),
                    Control(
                        "defence-in-depth-and-resilience",
                        "Database isolation, privacy and resilience",
                        10,
                        "machine",
                        false,
                        ["Database-enforced tenant isolation, privacy/retention/incident workflows and scheduled delivery/platform metrics are implemented; durable authentic release evidence and production-like capacity/failover/recovery acceptance remain open."],
                        ["P2-EVID-001", "P2-OPS-010"])
                ])
        };

        return new ProductionScorecard(
            categories.Sum(category => category.CurrentScore),
            categories.Sum(category => category.TargetScore),
            "remediation-required",
            "Close the remaining statutory/tax, visual/accessibility, operations/deployment, resilience, maintainability and authentic human/external evidence findings before release.",
            "independent-audit-control-ledger-v1",
            new DateOnly(2026, 7, 10),
            "7ea54cc6d1769ced568ac1568d190cc2bb4b16d1",
            "Baseline points are the passed weighted controls independently accepted for the exact audited commit; every post-baseline increment requires exact live candidate report evidence, and machine or human/external controls require candidate-bound artifact hashes and accepted evidence.",
            categories);
    }

    private static IReadOnlyList<SourceLawTraceabilityEntry> BuildSourceLawTraceability(
        SourceLawSnapshot sourceSnapshot,
        IReadOnlyList<GoldenFilingCorpusScenario> goldenCorpus,
        IReadOnlyList<StatutoryRuleMatrixEntry> statutoryRuleMatrix,
        IReadOnlyList<StatutoryRulesCoverageItem> statutoryRulesCoverage,
        IReadOnlyList<AccountantAcceptanceCriterion> accountantAcceptanceCriteria)
    {
        var sourceUsages = sourceSnapshot.Sources.ToDictionary(
            source => source.SourceId,
            _ => new SortedSet<string>(StringComparer.Ordinal),
            StringComparer.Ordinal);
        var releaseGateCodes = BuildSourceReleaseGateCodes();

        void AddUsage(LegalSourceReference source, string usage)
        {
            if (!sourceUsages.TryGetValue(source.SourceId, out var usages))
            {
                usages = new SortedSet<string>(StringComparer.Ordinal);
                sourceUsages[source.SourceId] = usages;
            }

            usages.Add(usage);
        }

        foreach (var scenario in goldenCorpus)
        {
            foreach (var source in scenario.EvidencePack.SourceReferences)
                AddUsage(source, $"golden-corpus:{scenario.Code}");
        }

        foreach (var row in statutoryRuleMatrix)
        {
            foreach (var source in row.Sources)
                AddUsage(source, $"statutory-rule-matrix:{row.Code}");
        }

        foreach (var coverage in statutoryRulesCoverage)
        {
            foreach (var source in coverage.Sources)
                AddUsage(source, $"statutory-rules-coverage:{coverage.Code}");
        }

        foreach (var criterion in accountantAcceptanceCriteria)
        {
            foreach (var source in criterion.Sources)
                AddUsage(source, $"accountant-acceptance:{criterion.ScenarioCode}");
        }

        var snapshotBySourceId = sourceSnapshot.Sources.ToDictionary(source => source.SourceId, StringComparer.Ordinal);
        return sourceUsages
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair =>
            {
                var inSnapshot = snapshotBySourceId.TryGetValue(pair.Key, out var source);
                var reference = source ?? new LegalSourceReference(pair.Key, pair.Key, sourceSnapshot.SnapshotDate, "");
                return new SourceLawTraceabilityEntry(
                    reference.SourceId,
                    reference.Title,
                    reference.EffectiveDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    reference.Url,
                    inSnapshot,
                    pair.Value.ToArray(),
                    releaseGateCodes.TryGetValue(pair.Key, out var gates) ? gates : []);
            })
            .ToArray();
    }

    private static IReadOnlyList<GoldenEvidenceLedgerEntry> BuildGoldenEvidenceLedger(
        IReadOnlyList<GoldenFilingCorpusScenario> goldenCorpus,
        IReadOnlyList<AccountantAcceptanceCriterion> accountantAcceptanceCriteria)
    {
        var acceptanceByScenario = accountantAcceptanceCriteria.ToDictionary(
            criterion => criterion.ScenarioCode,
            StringComparer.Ordinal);

        return goldenCorpus
            .OrderBy(scenario => scenario.Code, StringComparer.Ordinal)
            .Select(scenario =>
            {
                var acceptance = acceptanceByScenario[scenario.Code];
                return new GoldenEvidenceLedgerEntry(
                    scenario.Code,
                    scenario.Label,
                    scenario.Fixture.LegalName,
                    scenario.Fixture.CompanyType,
                    scenario.ExpectedOutcome,
                    scenario.CoverageStatus,
                    acceptance.AcceptanceStatus,
                    acceptance.RequiredSignOffGate,
                    acceptance.Required || scenario.Fixture.ManualProfessionalReviewRequired || scenario.ExpectedOutcome == "manual-handoff",
                    scenario.EvidenceTestNames.Order(StringComparer.Ordinal).ToArray(),
                    scenario.EvidenceVerifiers.Select(verifier => verifier.Command).Order(StringComparer.Ordinal).ToArray(),
                    scenario.EvidenceVerifiers.Select(verifier => verifier.CiScope).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                    scenario.EvidenceVerifiers.Select(verifier => verifier.EvidenceLevel).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                    scenario.EvidencePack.OutputArtifacts.Order(StringComparer.Ordinal).ToArray(),
                    scenario.EvidencePack.DecisionGates.Order(StringComparer.Ordinal).ToArray(),
                    scenario.EvidencePack.ExpectedValueChecks.Order(StringComparer.Ordinal).ToArray(),
                    scenario.EvidencePack.ExpectedProofPoints.Select(proof => proof.Area).Order(StringComparer.Ordinal).ToArray(),
                    scenario.EvidencePack.SourceReferences.Select(source => source.SourceId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                    scenario.EvidencePack.ExpectedOutputs.ExpectedCorporationTax,
                    scenario.EvidencePack.ExpectedOutputs.FilingReadinessState,
                    scenario.EvidencePack.ExpectedOutputs.SignOffPacketState);
            })
            .ToArray();
    }

    private static IReadOnlyList<GoldenVerifierManifestEntry> BuildGoldenVerifierManifest(
        IReadOnlyList<GoldenFilingCorpusScenario> goldenCorpus) =>
        goldenCorpus
            .OrderBy(scenario => scenario.Code, StringComparer.Ordinal)
            .SelectMany(scenario => scenario.EvidenceVerifiers
                .OrderBy(verifier => verifier.Name, StringComparer.Ordinal)
                .Select(verifier => new GoldenVerifierManifestEntry(
                    scenario.Code,
                    scenario.Label,
                    scenario.ExpectedOutcome,
                    scenario.CoverageStatus,
                    verifier.Name,
                    verifier.Command,
                    verifier.CiScope,
                    verifier.RunsInDefaultCi,
                    verifier.EvidenceLevel,
                    BlocksRelease: true,
                    scenario.EvidencePack.OutputArtifacts.Order(StringComparer.Ordinal).ToArray(),
                    scenario.EvidencePack.DecisionGates.Order(StringComparer.Ordinal).ToArray(),
                    scenario.EvidencePack.ExpectedProofPoints.Select(proof => proof.Area).Order(StringComparer.Ordinal).ToArray())))
            .ToArray();

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildSourceReleaseGateCodes()
    {
        return new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [IrishStatutoryRuleSources.CharitiesRegulatorAnnualReport.SourceId] =
            [
                "charity-annual-return-review",
                "qualified-accountant-review"
            ],
            [IrishStatutoryRuleSources.CroAuditorsReport.SourceId] =
            [
                "auditor-handoff",
                "manual-professional-handoff"
            ],
            [IrishStatutoryRuleSources.CroFinancialStatementsRequirements.SourceId] =
            [
                "cro-filing-readiness",
                "director-secretary-certification",
                "qualified-accountant-review"
            ],
            [IrishStatutoryRuleSources.CroGroupCompany.SourceId] =
            [
                "group-manual-handoff",
                "manual-professional-handoff"
            ],
            [IrishStatutoryRuleSources.CroGuaranteeCompany.SourceId] =
            [
                "clg-filing-review",
                "qualified-accountant-review"
            ],
            [IrishStatutoryRuleSources.CroMediumCompany.SourceId] =
            [
                "auditor-handoff",
                "manual-professional-handoff"
            ],
            [IrishStatutoryRuleSources.CroUnlimitedCompany.SourceId] =
            [
                "manual-professional-handoff"
            ],
            [IrishStatutoryRuleSources.FrcFrs102.SourceId] =
            [
                "statutory-basis-review",
                "qualified-accountant-review"
            ],
            [IrishStatutoryRuleSources.FrcFrs105.SourceId] =
            [
                "micro-statutory-review",
                "qualified-accountant-review"
            ],
            [IrishStatutoryRuleSources.RevenueAcceptedTaxonomies.SourceId] =
            [
                "external-ros-validation",
                "ixbrl-taxonomy-selection"
            ],
            [IrishStatutoryRuleSources.RevenueIxbrlContents.SourceId] =
            [
                "external-ros-validation",
                "ixbrl-content-review"
            ],
            [IrishStatutoryRuleSources.RevenueIxbrlOverview.SourceId] =
            [
                "external-ros-validation",
                "revenue-filing-readiness"
            ]
        };
    }

    private static IReadOnlyList<RevenueTaxonomyRangeEvidence> BuildRevenueTaxonomyRanges()
    {
        var ranges = RevenueIxbrlTaxonomySelector.AcceptedTaxonomyRanges()
            .OrderByDescending(range => range.EffectiveForPeriodsStartingOnOrAfter)
            .ThenBy(range => range.AccountingStandard, StringComparer.Ordinal)
            .ToArray();

        return ranges
            .Select(range =>
            {
                var nextEffectiveStart = ranges
                    .Where(candidate =>
                        candidate.AccountingStandard == range.AccountingStandard
                        && candidate.EffectiveForPeriodsStartingOnOrAfter > range.EffectiveForPeriodsStartingOnOrAfter)
                    .OrderBy(candidate => candidate.EffectiveForPeriodsStartingOnOrAfter)
                    .FirstOrDefault();
                var gates = new List<string>
                {
                    "external-ros-validation",
                    "ixbrl-taxonomy-selection",
                    "source-law-change-review"
                };
                if (!range.AutomatedPlatformSelectionSupported)
                    gates.Add("manual-professional-handoff");

                return new RevenueTaxonomyRangeEvidence(
                    range.TaxonomyKey,
                    range.AccountingStandard,
                    range.TaxonomyDate,
                    range.Label,
                    range.SchemaRef,
                    AcceptedByRevenue: true,
                    range.AutomatedPlatformSelectionSupported,
                    range.EffectiveForPeriodsStartingOnOrAfter.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    nextEffectiveStart is null
                        ? ""
                        : nextEffectiveStart.EffectiveForPeriodsStartingOnOrAfter.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    range.Sources.Select(source => source.SourceId).Order(StringComparer.Ordinal).ToArray(),
                    gates.ToArray());
            })
            .ToArray();
    }

    private static SourceLawMaintenanceProtocol BuildSourceLawMaintenanceProtocol(SourceLawSnapshot sourceSnapshot)
    {
        var nextReviewDue = sourceSnapshot.SnapshotDate.AddMonths(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return new SourceLawMaintenanceProtocol(
            "source-law-maintenance-v1",
            "Qualified accountant and engineering",
            "required-review",
            "Before every production release and at least monthly while source-backed filing logic is active.",
            nextReviewDue,
            "source-law-change-review",
            "Compare CRO, Revenue, FRC and Charities Regulator guidance pages against the pinned source-law snapshot before release.",
            "Block release if any pinned source changes, becomes unreachable, gains a newer effective date, or lacks qualified-accountant review.",
            sourceSnapshot.Sources.Select(source => source.SourceId).Order(StringComparer.Ordinal).ToArray(),
            [
                "CRO, Revenue, FRC and Charities Regulator source pages are reachable and reviewed for changes.",
                "Every changed effective date or guidance wording is reflected in source-law snapshot metadata before release.",
                "A qualified accountant accepts the source-law review note before generated filing packs are used for real filings."
            ],
            [
                "source-law-snapshot-fingerprint",
                "source-law-traceability-index",
                "source-law-change-review-note",
                "qualified-accountant-source-law-signoff"
            ]);
    }

    private static IReadOnlyList<SourceLawReviewLedgerEntry> BuildSourceLawReviewLedger(
        SourceLawSnapshot sourceSnapshot,
        IReadOnlyList<ReleaseReviewChecklistItem> releaseReviewChecklist)
    {
        var checklistItem = releaseReviewChecklist.Single(item => item.Code == "source-law-change-review");

        return sourceSnapshot.Sources
            .OrderBy(source => source.SourceId, StringComparer.Ordinal)
            .Select(source => new SourceLawReviewLedgerEntry(
                source.SourceId,
                source.Title,
                source.Url,
                source.EffectiveDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                SelectSourceLawOwnerRole(source.SourceId),
                checklistItem.Code,
                checklistItem.BlocksRelease,
                BuildSourceLawReviewChecks(source.SourceId),
                new[]
                {
                    "source-law-change-review-note",
                    "qualified-accountant-source-law-signoff",
                    checklistItem.EvidenceArtifact
                }.Distinct(StringComparer.Ordinal).ToArray()))
            .ToArray();
    }

    private static string SelectSourceLawOwnerRole(string sourceId) =>
        sourceId switch
        {
            var id when id.StartsWith("revenue-", StringComparison.Ordinal) => "Taxonomy and corporation tax reviewer",
            var id when id.StartsWith("frc-", StringComparison.Ordinal) => "Accounting standards reviewer",
            var id when id.StartsWith("charities-", StringComparison.Ordinal) => "Charity reporting reviewer",
            _ => "Qualified accountant and engineering"
        };

    private static IReadOnlyList<string> BuildSourceLawReviewChecks(string sourceId)
    {
        var checks = new List<string>
        {
            "Confirm source page is reachable at the pinned URL.",
            "Compare pinned effective date against the current source page.",
            "Review guidance wording for statutory filing, exemption, note or taxonomy changes.",
            "Record qualified accountant acceptance before generated packs are used for real filings."
        };

        if (sourceId.StartsWith("revenue-", StringComparison.Ordinal))
            checks.Add("Confirm Revenue-accepted taxonomy and iXBRL content guidance still match generated output assumptions.");
        if (sourceId == IrishStatutoryRuleSources.CroAuditorsReport.SourceId)
            checks.Add("Confirm auditor report requirements still support audit-required manual handoff gates.");
        if (sourceId == IrishStatutoryRuleSources.CroUnlimitedCompany.SourceId)
            checks.Add("Confirm unlimited-company variants remain manual professional handoff unless explicitly modelled.");
        if (sourceId == IrishStatutoryRuleSources.CharitiesRegulatorAnnualReport.SourceId)
            checks.Add("Confirm charity annual-report deadlines and SoFA/TAR evidence expectations remain current.");

        return checks;
    }

    // Static statutory, release, operations, accountant and visual catalogs live in focused partials.
}
