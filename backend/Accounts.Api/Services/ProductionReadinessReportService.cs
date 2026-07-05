using Accounts.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Accounts.Api.Services;

public sealed record ProductionReadinessArea(
    string Code,
    string Label,
    string Status,
    string Detail);

public sealed record GoldenFilingCorpusEvidencePack(
    IReadOnlyList<string> OutputArtifacts,
    IReadOnlyList<string> DecisionGates,
    IReadOnlyList<string> ExpectedValueChecks,
    GoldenFilingCorpusExpectedOutputs ExpectedOutputs,
    IReadOnlyList<GoldenFilingCorpusProofPoint> ExpectedProofPoints,
    IReadOnlyList<LegalSourceReference> SourceReferences);

public sealed record GoldenFilingCorpusExpectedOutputs(
    IReadOnlyList<string> PdfTextMarkers,
    IReadOnlyList<string> IxbrlRequiredTags,
    string FilingReadinessState,
    decimal ExpectedCorporationTax,
    IReadOnlyList<string> RequiredNotes,
    IReadOnlyList<string> FilingGateStates,
    string SignOffPacketState);

public sealed record GoldenFilingCorpusProofPoint(
    string Area,
    string ExpectedEvidence,
    string AutomatedVerifier,
    bool Required);

public sealed record GoldenFilingCorpusFixture(
    string LegalName,
    string CompanyType,
    string PeriodStart,
    string PeriodEnd,
    string ExpectedSizeClass,
    string ExpectedRegime,
    bool AuditExempt,
    bool ManualProfessionalReviewRequired);

public sealed record GoldenFilingCorpusVerifier(
    string Name,
    string Command,
    string CiScope,
    bool RunsInDefaultCi,
    string Environment,
    string EvidenceLevel);

public sealed record GoldenFilingCorpusScenario(
    string Code,
    string Label,
    string CompanyScope,
    string ExpectedOutcome,
    string CoverageStatus,
    GoldenFilingCorpusFixture Fixture,
    IReadOnlyList<string> EvidenceTestNames,
    IReadOnlyList<GoldenFilingCorpusVerifier> EvidenceVerifiers,
    IReadOnlyList<string> Assertions,
    GoldenFilingCorpusEvidencePack EvidencePack);

public sealed record SourceLawTraceabilityEntry(
    string SourceId,
    string Title,
    string EffectiveDate,
    string Url,
    bool InSnapshot,
    IReadOnlyList<string> UsedBy,
    IReadOnlyList<string> ReleaseGateCodes);

public sealed record SourceLawMaintenanceProtocol(
    string ProtocolVersion,
    string OwnerRole,
    string Status,
    string ReviewCadence,
    string NextReviewDue,
    string SignOffGate,
    string ChangeDetection,
    string FailurePolicy,
    IReadOnlyList<string> MonitoredSourceIds,
    IReadOnlyList<string> AcceptanceCriteria,
    IReadOnlyList<string> RequiredEvidence);

public sealed record StatutoryRuleMatrixEntry(
    string Code,
    string CompanyScope,
    string SizeOrRegime,
    string SupportLevel,
    IReadOnlyList<string> RequiredEvidence,
    IReadOnlyList<string> RequiredOutputs,
    IReadOnlyList<string> ManualHandoffGates,
    IReadOnlyList<LegalSourceReference> Sources);

public sealed record StatutoryRulesCoverageItem(
    string Code,
    string RuleFamily,
    string DecisionUnderTest,
    string CoverageStatus,
    IReadOnlyList<string> AutomatedVerifierNames,
    IReadOnlyList<string> EdgeCases,
    IReadOnlyList<LegalSourceReference> Sources);

public sealed record OperationalGate(
    string Code,
    string Label,
    bool Required,
    string Status,
    string Detail);

public sealed record ProductionReadinessAssuranceAction(
    string Code,
    string Label,
    string Owner,
    string Priority,
    int RiskRank,
    string EvidenceStage,
    string Status,
    string Detail,
    string EvidenceRequired);

public sealed record ProductionReadinessCompletionTrack(
    string Code,
    string Label,
    string OwnerRole,
    string Status,
    IReadOnlyList<string> CompletionCriteria,
    IReadOnlyList<string> CurrentEvidence,
    IReadOnlyList<string> NextActions,
    IReadOnlyList<string> AssuranceActionCodes);

public sealed record ProductionAuditabilityControl(
    string Code,
    string Label,
    bool Required,
    string Enforcement,
    string EvidenceCaptured,
    string Verification,
    IReadOnlyList<string> AuditEventCodes);

public sealed record AuditEvidenceTimelineEntry(
    string Code,
    string Stage,
    string EvidenceQuestion,
    string CapturedWhen,
    string RequiredActor,
    string Verification,
    IReadOnlyList<string> AuditEventCodes,
    IReadOnlyList<string> BlockingGateCodes);

public sealed record ProductionMonitoringControl(
    string Code,
    string Label,
    string Provider,
    bool Required,
    string ProductionSafetyGate,
    string EvidenceCaptured,
    string Verification,
    string AlertRoute,
    string FailurePolicy);

public sealed record DependencyPolicyControl(
    string Code,
    string Label,
    bool Required,
    string Enforcement,
    string EvidenceCaptured,
    string Verification,
    string FailurePolicy);

public sealed record DeploymentSafetyControl(
    string Code,
    string Label,
    bool Required,
    string Enforcement,
    string EvidenceCaptured,
    string Verification,
    string FailurePolicy);

public sealed record ReleaseReviewChecklistItem(
    string Code,
    string Label,
    string OwnerRole,
    bool Required,
    string Status,
    bool BlocksRelease,
    string EvidenceArtifact,
    string AssuranceActionCode,
    string OperationalGateCode,
    IReadOnlyList<string> AuditEventCodes,
    string Detail);

public sealed record ReleaseVerificationManifestItem(
    string Code,
    string Label,
    string OwnerRole,
    string Command,
    string CiScope,
    bool RunsInDefaultCi,
    bool BlocksRelease,
    string EvidenceArtifact,
    string ReleaseChecklistEvidenceArtifact,
    string ManualFallback);

public sealed record AccountantAcceptanceCriterion(
    string ScenarioCode,
    string Label,
    bool Required,
    string AcceptanceStatus,
    IReadOnlyList<string> ReviewScope,
    IReadOnlyList<string> RequiredEvidence,
    string RequiredSignOffGate,
    IReadOnlyList<LegalSourceReference> Sources,
    IReadOnlyList<GoldenFilingCorpusVerifier> EvidenceVerifiers);

public sealed record AccountantAcceptanceSummary(
    int ScenarioCount,
    int AutomatedVerifierCount,
    int ProfessionalSignOffRequiredCount,
    int ManualHandoffScenarioCount,
    IReadOnlyList<string> ReleaseBlockingScenarioCodes,
    IReadOnlyList<string> RequiredSignOffGates,
    string Status);

public sealed record VisualQaViewport(
    string Name,
    int Width,
    int Height);

public sealed record VisualQaRoute(
    string Code,
    string RouteKey,
    string Label,
    string Description,
    string RequiredText,
    IReadOnlyList<string> WorkflowStages,
    bool OpenFilingTab);

public sealed record VisualQaArtifact(
    string RouteCode,
    string RouteKey,
    string Theme,
    string ViewportName,
    string FileName,
    string ArtifactPath,
    string RequiredText,
    bool OpenFilingTab,
    string ReviewStatus,
    IReadOnlyList<string> LayoutChecks);

public sealed record VisualQaRouteAudit(
    string RouteCode,
    string RouteKey,
    string Label,
    IReadOnlyList<string> WorkflowStages,
    int ScreenshotCount,
    string ReviewStatus,
    IReadOnlyList<string> ReviewChecks);

public sealed record VisualQaReviewProtocol(
    string ProtocolVersion,
    string ReviewerRole,
    string Status,
    string SignOffGate,
    string FailurePolicy,
    IReadOnlyList<string> AcceptanceCriteria,
    IReadOnlyList<string> RequiredEvidence);

public sealed record VisualQaCoverage(
    string ArtifactName,
    string Enforcement,
    string ManifestFileName,
    int ExpectedScreenshotCount,
    IReadOnlyList<string> LayoutChecks,
    IReadOnlyList<string> ReviewChecks,
    VisualQaReviewProtocol ReviewProtocol,
    IReadOnlyList<string> Themes,
    IReadOnlyList<VisualQaViewport> Viewports,
    IReadOnlyList<VisualQaRoute> Routes,
    IReadOnlyList<VisualQaRouteAudit> RouteAudits,
    IReadOnlyList<VisualQaArtifact> Artifacts);

public sealed record ProductionAssurancePacket(
    string PacketId,
    string PacketVersion,
    string Status,
    string SourceLawSnapshotHash,
    int GoldenCorpusCovered,
    int GoldenCorpusTotal,
    int StatutoryRuleMatrixPaths,
    int StatutoryRuleCoverageFamilies,
    int VisualQaExpectedScreenshots,
    int RequiredOperationalGates,
    int OpenCriticalActions,
    IReadOnlyList<string> EvidenceItems,
    IReadOnlyList<string> ReleaseBlockers);

public sealed record ProductionReadinessReport(
    DateTime GeneratedAt,
    string OverallStatus,
    int CompaniesInDatabase,
    int PeriodsInDatabase,
    SourceLawSnapshot SourceLawSnapshot,
    IReadOnlyList<SourceLawTraceabilityEntry> SourceLawTraceability,
    SourceLawMaintenanceProtocol SourceLawMaintenanceProtocol,
    ProductionAssurancePacket AssurancePacket,
    IReadOnlyList<AccountantAcceptanceCriterion> AccountantAcceptanceCriteria,
    AccountantAcceptanceSummary AccountantAcceptanceSummary,
    IReadOnlyList<ProductionReadinessArea> Areas,
    IReadOnlyList<GoldenFilingCorpusScenario> GoldenFilingCorpus,
    IReadOnlyList<StatutoryRuleMatrixEntry> StatutoryRuleMatrix,
    IReadOnlyList<StatutoryRulesCoverageItem> StatutoryRulesCoverage,
    IReadOnlyList<string> ManualHandoffPaths,
    IReadOnlyList<OperationalGate> OperationalGates,
    IReadOnlyList<ProductionReadinessAssuranceAction> AssuranceActions,
    IReadOnlyList<ProductionReadinessCompletionTrack> CompletionTracks,
    IReadOnlyList<ProductionAuditabilityControl> AuditabilityControls,
    IReadOnlyList<AuditEvidenceTimelineEntry> AuditEvidenceTimeline,
    IReadOnlyList<ProductionMonitoringControl> MonitoringControls,
    IReadOnlyList<DependencyPolicyControl> DependencyPolicyControls,
    IReadOnlyList<DeploymentSafetyControl> DeploymentSafetyControls,
    IReadOnlyList<ReleaseReviewChecklistItem> ReleaseReviewChecklist,
    IReadOnlyList<ReleaseVerificationManifestItem> ReleaseVerificationManifest,
    VisualQaCoverage VisualQaCoverage);

public class ProductionReadinessReportService(AccountsDbContext db)
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
        var monitoringControls = BuildMonitoringControls();
        var dependencyPolicyControls = BuildDependencyPolicyControls();
        var deploymentSafetyControls = BuildDeploymentSafetyControls();
        var releaseReviewChecklist = BuildReleaseReviewChecklist(assuranceActions, operationalGates);
        var releaseVerificationManifest = BuildReleaseVerificationManifest();
        var accountantAcceptanceCriteria = BuildAccountantAcceptanceCriteria(goldenCorpus);
        var accountantAcceptanceSummary = BuildAccountantAcceptanceSummary(goldenCorpus, accountantAcceptanceCriteria);
        var visualQaCoverage = BuildVisualQaCoverage();
        var sourceLawTraceability = BuildSourceLawTraceability(
            sourceSnapshot,
            goldenCorpus,
            statutoryRuleMatrix,
            statutoryRulesCoverage,
            accountantAcceptanceCriteria);
        var sourceLawMaintenanceProtocol = BuildSourceLawMaintenanceProtocol(sourceSnapshot);
        var assurancePacket = BuildAssurancePacket(
            sourceSnapshot,
            goldenCorpus,
            statutoryRuleMatrix,
            statutoryRulesCoverage,
            operationalGates,
            assuranceActions,
            visualQaCoverage);

        return new ProductionReadinessReport(
            DateTime.UtcNow,
            "review-required",
            companies,
            periods,
            sourceSnapshot,
            sourceLawTraceability,
            sourceLawMaintenanceProtocol,
            assurancePacket,
            accountantAcceptanceCriteria,
            accountantAcceptanceSummary,
            areas,
            goldenCorpus,
            statutoryRuleMatrix,
            statutoryRulesCoverage,
            manualHandoffPaths,
            operationalGates,
            assuranceActions,
            completionTracks,
            auditabilityControls,
            auditEvidenceTimeline,
            monitoringControls,
            dependencyPolicyControls,
            deploymentSafetyControls,
            releaseReviewChecklist,
            releaseVerificationManifest,
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
            "golden-filing-corpus",
            "golden-verifier-manifest",
            "statutory-rules-matrix",
            "statutory-rules-coverage",
            "audit-evidence-timeline",
            "visual-smoke-screenshots",
            "production-operational-gates",
            "dependency-policy-controls",
            "deployment-safety-controls",
            "release-review-checklist",
            "release-verification-manifest",
            "accountant-acceptance-criteria",
            "accountant-acceptance-summary",
            "production-completion-map"
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

    private static IReadOnlyList<ProductionReadinessArea> BuildAreas() =>
    [
        new(
            "backend-accounting-engine",
            "Backend accounting engine",
            "hardened",
            "Golden-path coverage exercises classification, regime selection, statements, PDF, iXBRL, readiness and workflow gates. Final statutory use still requires named professional review."),
        new(
            "statutory-source-traceability",
            "Statutory source traceability",
            "hardened",
            "Rule outputs carry LegalSourceReference metadata and the source-law snapshot pins effective dates and URLs."),
        new(
            "frontend-accountant-workbench",
            "Frontend accountant workbench",
            "in-progress",
            "Workbench primitives and filing review surfaces exist; the remaining polish target is a consistent dashboard, route-level states and visual regression coverage."),
        new(
            "operations-security",
            "Operations and security",
            "hardened",
            "CI validates backend, frontend, production compose, production smoke and backup restore. Runtime monitoring and accountant sign-off remain operational gates.")
    ];

    private static IReadOnlyList<GoldenFilingCorpusScenario> BuildGoldenCorpus() =>
    [
        new(
            "micro-ltd",
            "Micro LTD audit-exempt filing",
            "Private company limited by shares, micro regime",
            "generated-pack",
            "covered",
            new(
                "Example Micro Limited",
                "Private",
                "2025-01-01",
                "2025-12-31",
                "Micro",
                "Micro",
                AuditExempt: true,
                ManualProfessionalReviewRequired: false),
            [
                "AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl"
            ],
            Verifiers("AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl"),
            [
                "classification selects Micro",
                "readiness has no missing items",
                "balance sheet balances",
                "PDF text includes company, period and micro statement",
                "iXBRL parses as XML",
                "accountant sign-off packet exposes approval blockers and allowed next actions"
            ],
            new(
                [
                    "accounts PDF text",
                    "CRO filing pack",
                    "CRO signature page",
                    "iXBRL XML",
                    "tax computation",
                    "notes disclosure set",
                    "filing readiness profile",
                    "accountant sign-off packet"
                ],
                [
                    "named qualified-accountant review",
                    "director and secretary certification",
                    "external ROS/iXBRL validation",
                    "accountant sign-off packet state"
                ],
                [
                    "Micro regime selected",
                    "100% filing readiness",
                    "balance sheet balances",
                    "well-formed iXBRL",
                    "micro statutory statement present in PDF text"
                ],
                new(
                    [
                        "Example Micro Limited",
                        "31 December 2025",
                        "6,250",
                        "280D"
                    ],
                    [
                        "core:EntityCurrentLegalOrRegisteredName"
                    ],
                    "100% filing readiness",
                    718.75m,
                    [
                        "Accounting Policies"
                    ],
                    [
                        "director and secretary certification required",
                        "external ROS/iXBRL validation required",
                        "qualified-accountant review required"
                    ],
                    "review-required"),
                ProofPoints(
                    "AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl",
                    [
                        new("pdf-text", "PDF text contains company name, period and micro statutory statement."),
                        new("ixbrl-xml", "iXBRL XML is well-formed and contains the company legal name."),
                        new("filing-readiness", "Filing readiness reaches 100% with no missing items."),
                        new("tax-computation", "Tax computation is generated and reconciles to the worked micro scenario."),
                        new("notes-disclosure", "Notes disclosure set includes the required accounting policies."),
                        new("signatory-gates", "Director and secretary certification gates remain required before filing use."),
                        new("accountant-signoff-packet", "Accountant sign-off packet shows reviewer state, open blockers and allowed next actions.")
                    ]),
                [
                    IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                    IrishStatutoryRuleSources.FrcFrs105,
                    IrishStatutoryRuleSources.RevenueIxbrlOverview,
                    IrishStatutoryRuleSources.RevenueAcceptedTaxonomies
                ])),
        new(
            "small-abridged-ltd",
            "Small or small-abridged LTD filing",
            "Private company limited by shares, small/abridged regime",
            "generated-pack",
            "covered",
            new(
                "Connacht Digital Solutions Limited",
                "Private",
                "2025-01-01",
                "2025-12-31",
                "Small",
                "SmallAbridged",
                AuditExempt: true,
                ManualProfessionalReviewRequired: false),
            [
                "AccountsWorkflowTests.GoldenPath_SmallAuditExemptCompany_MixedAccrualSetBalancesThroughPdfAndIxbrl"
            ],
            Verifiers("AccountsWorkflowTests.GoldenPath_SmallAuditExemptCompany_MixedAccrualSetBalancesThroughPdfAndIxbrl"),
            [
                "classification selects SmallAbridged",
                "mixed cash/accrual facts reconcile",
                "statutory PDF includes legal name, net assets and P&L",
                "CRO abridged pack omits P&L and cites Section 352",
                "signature page carries director and secretary certification",
                "iXBRL parses as XML and omits public P&L turnover",
                "accountant sign-off packet exposes review state and remaining evidence gates"
            ],
            new(
                [
                    "full accounts PDF text",
                    "abridged CRO filing pack",
                    "CRO signature page",
                    "iXBRL XML",
                    "tax computation",
                    "notes disclosure set",
                    "filing readiness profile",
                    "accountant sign-off packet"
                ],
                [
                    "abridgement eligibility",
                    "director and secretary certification",
                    "named qualified-accountant review",
                    "external ROS/iXBRL validation",
                    "accountant sign-off packet state"
                ],
                [
                    "SmallAbridged regime selected",
                    "Section 352 wording present in CRO pack",
                    "public P&L turnover omitted from iXBRL",
                    "tax computation matches worked scenario",
                    "notes include fixed assets and long-term creditors"
                ],
                new(
                    [
                        "Connacht Digital Solutions Limited",
                        "Section 352",
                        "PROFIT AND LOSS ACCOUNT"
                    ],
                    [
                        "core:EntityCurrentLegalOrRegisteredName"
                    ],
                    "generated-output-evidence-required",
                    950m,
                    [
                        "Accounting Policies",
                        "Tangible Fixed Assets",
                        "Creditors"
                    ],
                    [
                        "abridgement eligibility confirmed",
                        "director and secretary certification required",
                        "qualified-accountant review required"
                    ],
                    "review-required"),
                ProofPoints(
                    "AccountsWorkflowTests.GoldenPath_SmallAuditExemptCompany_MixedAccrualSetBalancesThroughPdfAndIxbrl",
                    [
                        new("pdf-text", "Full accounts PDF text contains legal name, net assets, profit and loss and Section 352 abridgement wording."),
                        new("ixbrl-xml", "iXBRL XML is well-formed and omits public profit-and-loss turnover for the abridged CRO pack."),
                        new("filing-readiness", "Filing readiness confirms generated CRO, Revenue and accountant-review evidence gates."),
                        new("tax-computation", "Tax computation matches the mixed cash/accrual worked scenario."),
                        new("notes-disclosure", "Notes include fixed assets, creditors and the small-company disclosure set."),
                        new("signatory-gates", "CRO signature page carries director and secretary certification evidence."),
                        new("accountant-signoff-packet", "Accountant sign-off packet shows review state, generated-output evidence and allowed next actions.")
                    ]),
                [
                    IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                    IrishStatutoryRuleSources.FrcFrs102,
                    IrishStatutoryRuleSources.RevenueIxbrlContents,
                    IrishStatutoryRuleSources.RevenueAcceptedTaxonomies
                ])),
        new(
            "dac-small",
            "Small DAC filing",
            "Designated activity company, small FRS 102 regime",
            "generated-pack",
            "covered",
            new(
                "Atlantic Manufacturing DAC",
                "DesignatedActivityCompany",
                "2026-01-01",
                "2026-12-31",
                "Small",
                "Small",
                AuditExempt: true,
                ManualProfessionalReviewRequired: false),
            [
                "FilingGoldenCorpusScenarioTests.GoldenCorpus_DacSmall_EmitsAccountsIxbrlAndSourceBackedReadiness"
            ],
            Verifiers("FilingGoldenCorpusScenarioTests.GoldenCorpus_DacSmall_EmitsAccountsIxbrlAndSourceBackedReadiness"),
            [
                "DAC company type remains in the supported path",
                "small FRS 102 regime is selected",
                "directors' report and CRO signature evidence are generated",
                "iXBRL parses as XML against the Revenue-accepted taxonomy gate",
                "accountant sign-off packet reaches ready-for-external-filing only after reviewer, signatory and external validation evidence"
            ],
            new(
                [
                    "DAC accounts PDF text",
                    "directors' report evidence",
                    "CRO signature page",
                    "iXBRL XML",
                    "tax computation",
                    "notes disclosure set",
                    "filing readiness profile",
                    "accountant sign-off packet"
                ],
                [
                    "DAC company type",
                    "director and secretary certification",
                    "named qualified-accountant review",
                    "external ROS/iXBRL validation",
                    "accountant sign-off packet state"
                ],
                [
                    "Small regime selected",
                    "DAC source-backed readiness",
                    "well-formed iXBRL",
                    "tax computation generated"
                ],
                new(
                    [
                        "Atlantic Manufacturing DAC",
                        "DIRECTORS' REPORT"
                    ],
                    [
                        "bus:EntityCurrentLegalOrRegisteredName"
                    ],
                    "ready-for-external-filing",
                    62.50m,
                    [
                        "Accounting Policies"
                    ],
                    [
                        "director and secretary certification satisfied",
                        "qualified-accountant review recorded",
                        "external ROS/iXBRL validation recorded"
                    ],
                    "ready-for-external-filing"),
                ProofPoints(
                    "FilingGoldenCorpusScenarioTests.GoldenCorpus_DacSmall_EmitsAccountsIxbrlAndSourceBackedReadiness",
                    [
                        new("pdf-text", "DAC accounts PDF text contains the legal name and directors' report."),
                        new("ixbrl-xml", "iXBRL XML is well-formed and contains the DAC legal name."),
                        new("filing-readiness", "Filing readiness confirms DAC source-backed support and all review/signatory/validation gates."),
                        new("tax-computation", "Tax computation is generated for the DAC small-company scenario."),
                        new("notes-disclosure", "Notes include the required accounting policies for the small-company path."),
                        new("signatory-gates", "Director and secretary certification evidence is satisfied before filing actions are allowed."),
                        new("accountant-signoff-packet", "Accountant sign-off packet reaches ready-for-external-filing only after reviewer and external validation evidence.")
                    ]),
                [
                    IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                    IrishStatutoryRuleSources.FrcFrs102,
                    IrishStatutoryRuleSources.RevenueIxbrlOverview,
                    IrishStatutoryRuleSources.RevenueAcceptedTaxonomies
                ])),
        new(
            "clg-charity",
            "CLG charity annual reporting",
            "Company limited by guarantee with charity evidence",
            "generated-pack-with-charity-gates",
            "covered",
            new(
                "Dublin Community Support CLG",
                "CompanyLimitedByGuarantee",
                "2026-01-01",
                "2026-12-31",
                "Small",
                "Small",
                AuditExempt: true,
                ManualProfessionalReviewRequired: false),
            [
                "FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness"
            ],
            Verifiers("FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness"),
            [
                "CLG remains in the supported company scope",
                "charity number evidence is required",
                "SoFA and trustees report evidence are required",
                "Charities Regulator source is attached",
                "accountant sign-off packet includes charity evidence and review gates"
            ],
            new(
                [
                    "CLG accounts PDF text",
                    "charity readiness profile",
                    "SoFA evidence",
                    "trustees annual report evidence",
                    "iXBRL XML",
                    "tax computation",
                    "notes disclosure set",
                    "accountant sign-off packet"
                ],
                [
                    "charity number",
                    "charity annual return review",
                    "named qualified-accountant review",
                    "accountant sign-off packet state"
                ],
                [
                    "charity evidence satisfied",
                    "Charities Regulator source attached",
                    "CLG source attached",
                    "well-formed iXBRL"
                ],
                new(
                    [
                        "Dublin Community Support CLG",
                        "Community support and education."
                    ],
                    [
                        "core:EntityCurrentLegalOrRegisteredName"
                    ],
                    "ready-for-external-filing",
                    62.50m,
                    [
                        "Accounting Policies",
                        "Charity reporting disclosures"
                    ],
                    [
                        "charity number satisfied",
                        "SoFA and trustees annual report evidence satisfied",
                        "qualified-accountant review recorded"
                    ],
                    "ready-for-external-filing"),
                ProofPoints(
                    "FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness",
                    [
                        new("pdf-text", "CLG accounts PDF text contains company name and charity objectives."),
                        new("ixbrl-xml", "iXBRL XML is well-formed and contains the CLG legal name."),
                        new("filing-readiness", "Filing readiness confirms charity number, SoFA and trustees report evidence."),
                        new("tax-computation", "Tax computation is generated for the CLG charity scenario."),
                        new("notes-disclosure", "Notes include accounting policies and charity reporting disclosures."),
                        new("signatory-gates", "Charity annual return and named qualified-accountant review gates remain required."),
                        new("accountant-signoff-packet", "Accountant sign-off packet shows charity evidence, review state and allowed next actions.")
                    ]),
                [
                    IrishStatutoryRuleSources.CroGuaranteeCompany,
                    IrishStatutoryRuleSources.CharitiesRegulatorAnnualReport,
                    IrishStatutoryRuleSources.FrcFrs102,
                    IrishStatutoryRuleSources.RevenueIxbrlOverview
                ])),
        new(
            "medium-audit-required",
            "Medium audit-required handoff",
            "Medium company or non-audit-exempt filing",
            "manual-handoff",
            "covered",
            new(
                "Midlands Manufacturing Limited",
                "Private",
                "2026-01-01",
                "2026-12-31",
                "Medium",
                "Medium",
                AuditExempt: false,
                ManualProfessionalReviewRequired: true),
            [
                "FilingGoldenCorpusScenarioTests.GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence"
            ],
            Verifiers("FilingGoldenCorpusScenarioTests.GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence"),
            [
                "audit report evidence is mandatory",
                "normal filing approval is blocked until auditor evidence exists",
                "manual professional handoff is exposed in readiness",
                "CRO medium-company and auditor-report sources are attached",
                "after auditor evidence, the full pack includes auditor report, P&L, cash flow and equity statements",
                "medium iXBRL includes tagged P&L facts",
                "accountant sign-off packet records manual handoff until auditor evidence is present"
            ],
            new(
                [
                    "full accounts PDF text",
                    "auditor report evidence",
                    "cash flow statement",
                    "statement of changes in equity",
                    "iXBRL XML",
                    "filing readiness profile",
                    "tax computation",
                    "accountant sign-off packet"
                ],
                [
                    "auditor handoff",
                    "manual professional review",
                    "normal CRO approval blocked until auditor evidence",
                    "accountant sign-off packet state"
                ],
                [
                    "Medium regime selected",
                    "audit report blocker present before auditor evidence",
                    "tagged P&L facts present after auditor evidence",
                    "auditor reference appears in PDF text"
                ],
                new(
                    [
                        "Midlands Manufacturing Limited",
                        "INDEPENDENT AUDITOR'S REPORT",
                        "AUD-2026-MIDLANDS-001",
                        "CASH FLOW STATEMENT",
                        "STATEMENT OF CHANGES IN EQUITY"
                    ],
                    [
                        "core:TurnoverGrossRevenue",
                        "core:ProfitLossOnOrdinaryActivitiesBeforeTax"
                    ],
                    "manual-handoff-until-auditor-evidence",
                    62.50m,
                    [
                        "Turnover",
                        "Tax on Profit on Ordinary Activities"
                    ],
                    [
                        "auditor handoff blocked until signed auditor report",
                        "normal CRO approval blocked until auditor evidence",
                        "manual professional review required"
                    ],
                    "manual-handoff"),
                ProofPoints(
                    "FilingGoldenCorpusScenarioTests.GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence",
                    [
                        new("pdf-text", "Full accounts PDF text is blocked until auditor evidence exists, then includes auditor report, P&L, cash flow and equity statements."),
                        new("ixbrl-xml", "iXBRL XML is well-formed and contains tagged profit-and-loss facts after auditor evidence."),
                        new("filing-readiness", "Filing readiness blocks approval before signed auditor report evidence and clears that blocker when evidence is recorded."),
                        new("tax-computation", "Tax computation is generated for the medium audit-required scenario."),
                        new("notes-disclosure", "Notes include turnover and tax-on-profit disclosures for the full accounts path."),
                        new("auditor-handoff", "Signed auditor report reference is mandatory before final output generation."),
                        new("accountant-signoff-packet", "Accountant sign-off packet records manual handoff state until signed auditor report evidence is present.")
                    ]),
                [
                    IrishStatutoryRuleSources.CroMediumCompany,
                    IrishStatutoryRuleSources.CroAuditorsReport,
                    IrishStatutoryRuleSources.FrcFrs102,
                    IrishStatutoryRuleSources.RevenueIxbrlOverview
                ]))
    ];

    private static IReadOnlyList<StatutoryRuleMatrixEntry> BuildStatutoryRuleMatrix() =>
    [
        new(
            "ltd-micro",
            "LTD micro",
            "Micro / FRS 105",
            "supported",
            [
                "Micro size classification completed and no micro exclusions apply",
                "Active director and company secretary recorded",
                "Named qualified-accountant review recorded",
                "External ROS/iXBRL validation evidence recorded before Revenue use"
            ],
            [
                "Micro entity financial statements PDF",
                "CRO certification/signature page",
                "Revenue iXBRL financial statements package",
                "CT1 support and tax computation evidence"
            ],
            [
                "Fail closed if micro exclusions, group context, regulated status or missing signatories are detected",
                "Manual professional review required before real filing use"
            ],
            [
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.FrcFrs105,
                IrishStatutoryRuleSources.RevenueIxbrlOverview,
                IrishStatutoryRuleSources.RevenueAcceptedTaxonomies
            ]),
        new(
            "ltd-small-abridged",
            "LTD small abridged",
            "Small / abridged FRS 102",
            "supported",
            [
                "Small size classification completed under two-of-three rules",
                "Abridgement eligibility and audit exemption confirmed",
                "Director and secretary evidence recorded",
                "Named qualified-accountant review recorded"
            ],
            [
                "Abridged small-company financial statements PDF",
                "Required statutory statements and notes",
                "CRO certification/signature page",
                "Revenue iXBRL and CT1 support pack"
            ],
            [
                "Fail closed if audit exemption is lost, member audit notice applies, or abridgement is not available",
                "External ROS/iXBRL validation remains a manual evidence gate"
            ],
            [
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.FrcFrs102,
                IrishStatutoryRuleSources.RevenueIxbrlContents,
                IrishStatutoryRuleSources.RevenueAcceptedTaxonomies
            ]),
        new(
            "ltd-small-full",
            "LTD small full accounts",
            "Small / FRS 102",
            "supported",
            [
                "Small size classification completed under two-of-three rules",
                "Audit exemption confirmed or audit report evidence recorded if exemption is unavailable",
                "Abridgement not elected or not available for this filing package",
                "Director and secretary evidence recorded",
                "Named qualified-accountant review recorded"
            ],
            [
                "Full small-company financial statements PDF",
                "Profit and loss account where required for the selected filing package",
                "Required statutory statements and notes",
                "CRO certification/signature page",
                "Revenue iXBRL and CT1 support pack"
            ],
            [
                "Manual handoff if audit exemption is lost, member audit notice applies, or abridgement/accountant judgement changes the filing path",
                "External ROS/iXBRL validation remains a manual evidence gate"
            ],
            [
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.FrcFrs102,
                IrishStatutoryRuleSources.RevenueIxbrlContents,
                IrishStatutoryRuleSources.RevenueAcceptedTaxonomies
            ]),
        new(
            "dac-small",
            "DAC small",
            "Small / FRS 102",
            "supported",
            [
                "DAC company type and size classification recorded",
                "Audit exemption and filing regime confirmed",
                "Director and secretary evidence recorded",
                "Named qualified-accountant review recorded",
                "External ROS/iXBRL validation evidence recorded before Revenue use"
            ],
            [
                "Small company financial statements PDF",
                "Directors' report evidence",
                "CRO certification/signature page",
                "Revenue iXBRL and CT1 support pack"
            ],
            [
                "Manual handoff if audit is required, regulated exclusions apply, or group context is present",
                "No direct CRO/ROS submission automation"
            ],
            [
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.FrcFrs102,
                IrishStatutoryRuleSources.RevenueIxbrlOverview,
                IrishStatutoryRuleSources.RevenueAcceptedTaxonomies
            ]),
        new(
            "clg-non-charity",
            "CLG non-charity",
            "Company limited by guarantee / FRS 102",
            "supported",
            [
                "Company limited by guarantee status recorded",
                "Non-charity status confirmed so charity annual-return evidence is not expected",
                "Members-guarantee and officers evidence recorded",
                "Named qualified-accountant review recorded"
            ],
            [
                "Guarantee-company financial statements PDF",
                "Directors' report evidence",
                "CRO certification/signature page",
                "Revenue iXBRL and CT1 support pack where corporation tax filing applies"
            ],
            [
                "Manual handoff if charity status, audit requirement, regulated exclusions or group context are detected",
                "External ROS/iXBRL validation remains a manual evidence gate",
                "No direct CRO/ROS submission automation"
            ],
            [
                IrishStatutoryRuleSources.CroGuaranteeCompany,
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.FrcFrs102,
                IrishStatutoryRuleSources.RevenueIxbrlOverview,
                IrishStatutoryRuleSources.RevenueAcceptedTaxonomies
            ]),
        new(
            "clg-charity",
            "CLG charity",
            "CLG / charity annual return",
            "supported-with-review",
            [
                "CLG company type recorded",
                "Charity number and charity profile completed",
                "SoFA and trustees' annual report evidence generated",
                "Named qualified-accountant or charity reviewer approval recorded"
            ],
            [
                "CLG financial statements PDF",
                "Charity SoFA",
                "Trustees' annual report support",
                "Revenue iXBRL package where corporation tax filing applies"
            ],
            [
                "Charity annual return must be manually reviewed before use",
                "External ROS/iXBRL validation remains a manual evidence gate where corporation tax filing applies",
                "Manual handoff required for complex charity governance, restricted funds or regulator-specific queries"
            ],
            [
                IrishStatutoryRuleSources.CroGuaranteeCompany,
                IrishStatutoryRuleSources.CharitiesRegulatorAnnualReport,
                IrishStatutoryRuleSources.FrcFrs102,
                IrishStatutoryRuleSources.RevenueIxbrlOverview,
                IrishStatutoryRuleSources.RevenueAcceptedTaxonomies
            ]),
        new(
            "medium-audit-required",
            "Medium audit-required",
            "Medium / full FRS 102",
            "manual-handoff",
            [
                "Medium size classification completed",
                "Signed auditor report evidence recorded",
                "Full financial statements and directors' report evidence reviewed",
                "Named qualified-accountant and auditor handoff recorded"
            ],
            [
                "Full accounts support pack",
                "Signed statutory auditor's report evidence record",
                "Revenue iXBRL and CT1 support pack"
            ],
            [
                "Final outputs remain blocked until signed auditor report evidence is recorded",
                "External ROS/iXBRL validation remains a manual evidence gate",
                "Manual professional handoff required before approval or submission states"
            ],
            [
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.CroMediumCompany,
                IrishStatutoryRuleSources.CroAuditorsReport,
                IrishStatutoryRuleSources.FrcFrs102,
                IrishStatutoryRuleSources.RevenueIxbrlOverview,
                IrishStatutoryRuleSources.RevenueAcceptedTaxonomies
            ]),
        new(
            "unsupported-regulated-group",
            "Unsupported regulated/group",
            "PLC, regulated, unlimited variants, group/holding/subsidiary",
            "unsupported",
            [
                "Manual professional ownership recorded",
                "Reason for unsupported path captured",
                "External specialist review evidence retained outside automated filing workflow"
            ],
            [
                "Manual handoff record",
                "Evidence checklist export only; no automated final filing pack approval"
            ],
            [
                "Fail closed before filing workflow approval",
                "Direct CRO/ROS submission remains unsupported",
                "Specialist accountant or auditor must complete the statutory filing outside the automated path"
            ],
            [
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.CroGroupCompany,
                IrishStatutoryRuleSources.CroUnlimitedCompany,
                IrishStatutoryRuleSources.FrcFrs102
            ])
    ];

    private static IReadOnlyList<StatutoryRulesCoverageItem> BuildStatutoryRulesCoverage() =>
    [
        new(
            "size-classification-thresholds",
            "Size classification",
            "Two-of-three thresholds, current/prior-year movement and first-year classification must produce the correct statutory size class before regime selection.",
            "covered",
            [
                "AccountsWorkflowTests.SizeClassification_FirstYearMicro_AllowsMicroAndAuditExemption",
                "AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl",
                "AccountsWorkflowTests.GoldenPath_SmallAuditExemptCompany_MixedAccrualSetBalancesThroughPdfAndIxbrl",
                "FilingGoldenCorpusScenarioTests.GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence"
            ],
            [
                "two-of-three threshold rule",
                "current and prior year classification rule",
                "micro, small and medium boundary scenarios",
                "classification must run before filing regime selection"
            ],
            [
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.CroMediumCompany
            ]),
        new(
            "micro-exclusions-and-fifth-schedule",
            "Micro exclusions and excluded entities",
            "Micro and small-company benefits must fail closed where holding, investment, subsidiary, regulated or Fifth Schedule exclusion flags make automation unsafe.",
            "covered",
            [
                "FilingReadinessProfileTests.ReadinessProfile_ForRegulatedOrGroupEntities_FailsClosed",
                "FilingReadinessProfileTests.ReadinessProfile_ForUnsupportedCompanyTypes_FailsClosedToManualHandoff"
            ],
            [
                "group, holding and subsidiary contexts",
                "regulated and Fifth Schedule excluded entities",
                "manual handoff when the model cannot safely apply micro or audit-exemption benefits"
            ],
            [
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.CroGroupCompany,
                IrishStatutoryRuleSources.FrcFrs102
            ]),
        new(
            "audit-exemption-loss",
            "Audit exemption",
            "Audit exemption must be withdrawn or blocked where repeated late CRO filings, member audit notice or medium/full filing conditions require audit evidence.",
            "covered",
            [
                "AccountsWorkflowTests.FilingRegime_RecentRepeatedLateCroFilings_RemoveAuditExemption",
                "AccountsWorkflowTests.ClassificationRoutes_EnforceRuntimePeriodRoleAndApiAuthorization",
                "FilingGoldenCorpusScenarioTests.GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence"
            ],
            [
                "late CRO filings remove audit exemption",
                "member audit notice evidence is recorded through authorised routes",
                "signed auditor report evidence required for medium or non-audit-exempt paths"
            ],
            [
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.CroMediumCompany,
                IrishStatutoryRuleSources.CroAuditorsReport
            ]),
        new(
            "required-outputs-and-filing-gates",
            "Required outputs and filing gates",
            "Generated accounts, CRO pack, iXBRL, CT1/tax support, notes and signatory evidence must be present before a supported path can move toward external filing.",
            "covered",
            [
                "AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl",
                "AccountsWorkflowTests.GoldenPath_SmallAuditExemptCompany_MixedAccrualSetBalancesThroughPdfAndIxbrl",
                "FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness",
                "AccountsWorkflowTests.PeriodStatusEndpoint_RejectsFinaliseOrFileWhenReadinessBlockersRemain"
            ],
            [
                "PDF text assertions",
                "well-formed iXBRL XML assertions",
                "tax computation and notes proof points",
                "director, secretary, accountant-review and external ROS validation gates"
            ],
            [
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.RevenueIxbrlContents,
                IrishStatutoryRuleSources.RevenueAcceptedTaxonomies
            ]),
        new(
            "unsupported-fail-closed",
            "Unsupported paths",
            "PLC, unlimited variants, regulated entities, group contexts, direct CRO/ROS submission and complex claims must stop before normal approval and require manual professional handoff.",
            "covered",
            [
                "FilingReadinessProfileTests.ReadinessProfile_ForUnsupportedCompanyTypes_FailsClosedToManualHandoff",
                "FilingReadinessProfileTests.ApprovalGuard_ForUnsupportedCompanyType_BlocksNormalCroApprovalPath",
                "FilingReadinessProfileTests.ReadinessProfile_ForRegulatedOrGroupEntities_FailsClosed"
            ],
            [
                "PLC and public company paths",
                "regulated and group contexts",
                "direct CRO or ROS submission is not automated",
                "manual professional handoff remains the only allowed external filing path"
            ],
            [
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.CroGroupCompany,
                IrishStatutoryRuleSources.CroUnlimitedCompany,
                IrishStatutoryRuleSources.RevenueIxbrlOverview
            ])
    ];

    private static IReadOnlyList<string> BuildManualHandoffPaths() =>
    [
        "PLC and public-company workflows",
        "Private unlimited company variants not safely modelled",
        "Listed, credit institution, insurance undertaking, pension fund and other excluded entities",
        "Group, holding, subsidiary and consolidation contexts",
        "Audit-required filings without a signed auditor report",
        "Complex corporation tax claims, group relief and loss elections",
        "Direct CRO or ROS submission"
    ];

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
            "The accountant journey needs desktop and mobile screenshots across light and dark mode before it can be called visually production-ready.",
            "Screenshots for dashboard, production readiness, company detail, period workspace, filing review and the workbench component preview in light desktop, dark desktop, light mobile and dark mobile.")
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
                "Production auditability controls and audit evidence timeline are declared."
            ],
            [
                "Run qualified-accountant acceptance on the golden corpus.",
                "Record source-law change review evidence against the pinned snapshot.",
                "Attach external ROS/iXBRL validation evidence for generated iXBRL packs.",
                "Record manual handoff acceptance for audit-required paths."
            ],
            [
                "qualified-accountant-signoff",
                "source-law-change-review",
                "external-ros-validation",
                "accountant-acceptance-walkthrough"
            ]),
        new(
            "frontend-ui-ux",
            "Frontend UI/UX",
            "Product design",
            "in-progress",
            [
                "Accountant workflow rail is visually coherent across the core journey.",
                "Light/dark visual regression covers desktop and mobile.",
                "Dense review workbench surfaces blockers, evidence, sources and next actions without visual clutter."
            ],
            [
                "Visual QA route audit covers the accountant workbench routes.",
                "Route-level loading/error states exist for main dynamic routes.",
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
                "light-dark-desktop-mobile-screenshot-review",
                "light-dark-visual-regression",
                "production-ci-gates",
                [],
                "Desktop and mobile screenshots in light and dark mode must be reviewed for the accountant workflow before release.")
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
            "light-dark-desktop-mobile-screenshot-review",
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
            "light-dark-desktop-mobile-screenshot-review",
            "Run from frontend/ and retain lint, TypeScript and Next production build output when CI is unavailable."),
        new(
            "visual-smoke-light-dark",
            "Light/dark desktop/mobile visual smoke",
            "Engineering",
            "node --experimental-strip-types scripts/visual-smoke.mjs",
            "default-ci",
            RunsInDefaultCi: true,
            BlocksRelease: true,
            "artifacts/visual-smoke",
            "light-dark-desktop-mobile-screenshot-review",
            "Run the visual smoke locally against seeded production-like data if CI cannot capture screenshots, then review the generated artifacts manually."),
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
            "production-stack-smoke",
            "Production compose smoke",
            "Operations",
            "pwsh ./scripts/smoke-production.ps1",
            "default-ci",
            RunsInDefaultCi: true,
            BlocksRelease: true,
            "ci-production-stack-smoke-and-backup-restore",
            "ci-production-stack-smoke-and-backup-restore",
            "Run the production smoke script against the production compose profile and retain health, login and filing-workflow output."),
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
            .Select(name => new GoldenFilingCorpusVerifier(
                name,
                $"dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~{name}",
                "default-ci",
                RunsInDefaultCi: true,
                "EF Core InMemory golden fixture; CI also runs the broader backend suite on Linux",
                "end-to-end golden filing scenario"))
            .ToArray();

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
            "backup-restore-drill",
            "Backup restore drill",
            true,
            "CI production stack smoke runs scripts/backup-postgres.ps1 and scripts/verify-postgres-backup.ps1 against the production compose shape.",
            "The release evidence includes a PostgreSQL custom-format backup dump, sha256 sidecar and backup restore verification before the production smoke job can pass.",
            ".github/workflows/ci.yml Run production backup restore drill step invokes verify-postgres-backup after creating the dump.",
            "Fail the release if backup creation, checksum verification or restore verification fails.")
    ];

    private static IReadOnlyList<AccountantAcceptanceCriterion> BuildAccountantAcceptanceCriteria(IReadOnlyList<GoldenFilingCorpusScenario> goldenCorpus) =>
    [
        new(
            "micro-ltd",
            "Micro LTD accountant acceptance",
            true,
            "qualified-accountant-review-required",
            [
                "PDF wording and micro statutory statement",
                "iXBRL XML and Revenue taxonomy selection",
                "filing readiness profile at 100%",
                "tax computation and notes disclosure set",
                "director and secretary signatory gates"
            ],
            [
                "Named qualified-accountant approval recorded against the generated pack.",
                "External ROS/iXBRL validation evidence recorded before Revenue use.",
                "Director and secretary certification evidence reviewed."
            ],
            "Named qualified accountant must approve the generated pack before real filing use.",
            [
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.FrcFrs105,
                IrishStatutoryRuleSources.RevenueAcceptedTaxonomies
            ],
            AcceptanceVerifiers(goldenCorpus, "micro-ltd")),
        new(
            "small-abridged-ltd",
            "Small abridged LTD accountant acceptance",
            true,
            "qualified-accountant-review-required",
            [
                "Full accounts PDF wording and abridged CRO pack wording",
                "Section 352 abridgement evidence",
                "iXBRL XML and public profit-and-loss omission checks",
                "tax computation and small-company notes",
                "director and secretary signatory gates"
            ],
            [
                "Named qualified-accountant approval recorded against full and abridged generated packs.",
                "Abridgement eligibility and audit exemption evidence reviewed.",
                "External ROS/iXBRL validation evidence recorded before Revenue use."
            ],
            "Named qualified accountant must approve both the full accounts pack and abridged CRO pack before real filing use.",
            [
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.FrcFrs102,
                IrishStatutoryRuleSources.RevenueIxbrlContents
            ],
            AcceptanceVerifiers(goldenCorpus, "small-abridged-ltd")),
        new(
            "dac-small",
            "Small DAC accountant acceptance",
            true,
            "qualified-accountant-review-required",
            [
                "DAC accounts PDF wording and directors' report evidence",
                "iXBRL XML and Revenue taxonomy selection",
                "filing readiness profile with DAC supported-path evidence",
                "tax computation and small-company notes",
                "director and secretary signatory gates"
            ],
            [
                "Named qualified-accountant approval recorded against the DAC pack.",
                "DAC company type, audit exemption and small-company filing regime evidence reviewed.",
                "External ROS/iXBRL validation evidence recorded before Revenue use."
            ],
            "Named qualified accountant must approve the DAC generated pack before real filing use.",
            [
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.FrcFrs102,
                IrishStatutoryRuleSources.RevenueAcceptedTaxonomies
            ],
            AcceptanceVerifiers(goldenCorpus, "dac-small")),
        new(
            "clg-charity",
            "CLG charity accountant acceptance",
            true,
            "qualified-accountant-review-required",
            [
                "CLG accounts PDF wording",
                "charity number, SoFA and trustees annual report evidence",
                "iXBRL XML and CLG source-backed readiness",
                "tax computation and charity notes",
                "charity annual return review gates"
            ],
            [
                "Named qualified-accountant approval recorded against the CLG charity pack.",
                "Charity annual report evidence reviewed before charity filing state advances.",
                "Charities Regulator source-backed evidence reviewed."
            ],
            "Named qualified accountant must approve the CLG charity pack and charity evidence before real filing use.",
            [
                IrishStatutoryRuleSources.CroGuaranteeCompany,
                IrishStatutoryRuleSources.CharitiesRegulatorAnnualReport,
                IrishStatutoryRuleSources.FrcFrs102
            ],
            AcceptanceVerifiers(goldenCorpus, "clg-charity")),
        new(
            "medium-audit-required",
            "Medium handoff accountant acceptance",
            true,
            "manual-handoff-review-required",
            [
                "auditor handoff and signed auditor report evidence",
                "full accounts PDF with P&L, cash flow and equity statements",
                "iXBRL XML tagged profit-and-loss facts",
                "filing readiness blockers before and after auditor evidence",
                "manual professional handoff note"
            ],
            [
                "Signed auditor report and manual handoff note reviewed by the qualified accountant.",
                "Named qualified-accountant acceptance recorded only after auditor evidence is present.",
                "Unsupported automated filing path remains blocked until manual professional ownership is recorded."
            ],
            "Qualified accountant must record manual handoff acceptance before relying on outputs.",
            [
                IrishStatutoryRuleSources.CroMediumCompany,
                IrishStatutoryRuleSources.CroAuditorsReport,
                IrishStatutoryRuleSources.FrcFrs102
            ],
            AcceptanceVerifiers(goldenCorpus, "medium-audit-required"))
    ];

    private static AccountantAcceptanceSummary BuildAccountantAcceptanceSummary(
        IReadOnlyList<GoldenFilingCorpusScenario> goldenCorpus,
        IReadOnlyList<AccountantAcceptanceCriterion> accountantAcceptanceCriteria)
    {
        var releaseBlockingScenarioCodes = accountantAcceptanceCriteria
            .Where(criterion => criterion.Required && criterion.AcceptanceStatus != "accepted")
            .Select(criterion => criterion.ScenarioCode)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var requiredSignOffGates = accountantAcceptanceCriteria
            .Where(criterion => criterion.Required)
            .Select(criterion => criterion.RequiredSignOffGate)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var automatedVerifierCount = accountantAcceptanceCriteria
            .SelectMany(criterion => criterion.EvidenceVerifiers)
            .Select(verifier => verifier.Name)
            .Distinct(StringComparer.Ordinal)
            .Count();
        var manualHandoffScenarioCount = goldenCorpus.Count(scenario =>
            scenario.ExpectedOutcome.Contains("manual-handoff", StringComparison.OrdinalIgnoreCase)
            || scenario.Fixture.ManualProfessionalReviewRequired);

        return new AccountantAcceptanceSummary(
            goldenCorpus.Count,
            automatedVerifierCount,
            accountantAcceptanceCriteria.Count(criterion => criterion.Required),
            manualHandoffScenarioCount,
            releaseBlockingScenarioCodes,
            requiredSignOffGates,
            releaseBlockingScenarioCodes.Length == 0 ? "accepted" : "qualified-accountant-review-required");
    }

    private static IReadOnlyList<GoldenFilingCorpusVerifier> AcceptanceVerifiers(
        IReadOnlyList<GoldenFilingCorpusScenario> goldenCorpus,
        string scenarioCode)
    {
        var scenario = goldenCorpus.Single(item => item.Code == scenarioCode);
        return scenario.EvidenceVerifiers;
    }

    private static VisualQaCoverage BuildVisualQaCoverage()
    {
        var accountantWorkflowStages = new[]
        {
            "Setup",
            "Import",
            "Classify",
            "Year-End",
            "Statements",
            "Notes",
            "Review",
            "Filing"
        };
        var themes = new[] { "light", "dark" };
        var layoutChecks = new[]
        {
            "browser-console-errors",
            "page-horizontal-overflow",
            "visible-text-overlap"
        };
        var reviewChecks = new[]
        {
            "accountant-workflow-hierarchy",
            "table-scanability",
            "theme-contrast",
            "mobile-density",
            "loading-error-empty-states"
        };
        var reviewProtocol = new VisualQaReviewProtocol(
            "visual-review-v1",
            "Design reviewer",
            "required-review",
            "visual-qa-screenshot-review",
            "Block release if any accountant workbench route has console errors, horizontal overflow, visible text overlap, inaccessible contrast, unreadable table density, or unresolved light/dark/mobile defects.",
            [
                "Every configured route is captured in light desktop, dark desktop, light mobile and dark mobile.",
                "No browser console errors, horizontal overflow or visible text overlap are present.",
                "Accountant workflow hierarchy, table scanability, theme contrast, mobile density and route states are professionally acceptable.",
                "A named visual QA reviewer records screenshot-manifest acceptance before real filing release."
            ],
            [
                "visual-smoke-manifest.json",
                "24 visual smoke screenshots",
                "route audit summary",
                "named visual QA reviewer sign-off"
            ]);
        var viewports = new[]
        {
            new VisualQaViewport("desktop", 1440, 1000),
            new VisualQaViewport("mobile", 390, 844)
        };
        var routes = new[]
        {
            new VisualQaRoute(
                "dashboard",
                "dashboard",
                "Dashboard",
                "Accountant queue, blockers, deadlines and production readiness overview.",
                "Production Readiness",
                accountantWorkflowStages,
                OpenFilingTab: false),
            new VisualQaRoute(
                "production-readiness",
                "readiness",
                "Production readiness",
                "Assurance checklist, statutory rules matrix, source snapshot and operational gates.",
                "Production Readiness Checklist",
                ["Review", "Filing"],
                OpenFilingTab: false),
            new VisualQaRoute(
                "company-detail",
                "company",
                "Company detail",
                "Company command centre, statutory profile, officers, charity facts and accounting periods.",
                "Company command centre",
                ["Setup"],
                OpenFilingTab: false),
            new VisualQaRoute(
                "period-workspace",
                "period",
                "Period workspace",
                "Import, classification, year-end, statements and filing readiness overview.",
                "Filing readiness",
                accountantWorkflowStages,
                OpenFilingTab: false),
            new VisualQaRoute(
                "filing-review",
                "filing",
                "Filing review",
                "Period filing tab with evidence checklist, source links, outputs and filing state.",
                "Filing readiness profile",
                ["Review", "Filing"],
                OpenFilingTab: true),
            new VisualQaRoute(
                "workbench-preview",
                "workbenchPreview",
                "Workbench preview",
                "Internal component preview for accountant workflow primitives and route states.",
                "Workbench Component Preview",
                accountantWorkflowStages,
                OpenFilingTab: false)
        };

        var artifacts = routes
            .SelectMany(route => themes.SelectMany(theme => viewports.Select(viewport =>
            {
                var fileName = $"{route.Code}-{theme}-{viewport.Name}.png";
                return new VisualQaArtifact(
                    route.Code,
                    route.RouteKey,
                    theme,
                    viewport.Name,
                    fileName,
                    $"artifacts/visual-smoke/{fileName}",
                    route.RequiredText,
                    route.OpenFilingTab,
                    "required-review",
                    layoutChecks);
            })))
            .ToArray();
        var routeAudits = routes
            .Select(route => new VisualQaRouteAudit(
                route.Code,
                route.RouteKey,
                route.Label,
                route.WorkflowStages,
                themes.Length * viewports.Length,
                "required-review",
                reviewChecks))
            .ToArray();

        return new VisualQaCoverage(
            "visual-smoke-screenshots",
            "ci-production-smoke",
            "visual-smoke-manifest.json",
            routes.Length * themes.Length * viewports.Length,
            layoutChecks,
            reviewChecks,
            reviewProtocol,
            themes,
            viewports,
            routes,
            routeAudits,
            artifacts);
    }
}
