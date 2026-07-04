using Accounts.Api.Data;
using Microsoft.EntityFrameworkCore;

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
    IReadOnlyList<GoldenFilingCorpusProofPoint> ExpectedProofPoints,
    IReadOnlyList<LegalSourceReference> SourceReferences);

public sealed record GoldenFilingCorpusProofPoint(
    string Area,
    string ExpectedEvidence,
    string AutomatedVerifier,
    bool Required);

public sealed record GoldenFilingCorpusScenario(
    string Code,
    string Label,
    string CompanyScope,
    string ExpectedOutcome,
    string CoverageStatus,
    IReadOnlyList<string> EvidenceTestNames,
    IReadOnlyList<string> Assertions,
    GoldenFilingCorpusEvidencePack EvidencePack);

public sealed record StatutoryRuleMatrixEntry(
    string Code,
    string CompanyScope,
    string SizeOrRegime,
    string SupportLevel,
    IReadOnlyList<string> RequiredEvidence,
    IReadOnlyList<string> RequiredOutputs,
    IReadOnlyList<string> ManualHandoffGates,
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
    string Status,
    string Detail,
    string EvidenceRequired);

public sealed record ProductionAuditabilityControl(
    string Code,
    string Label,
    bool Required,
    string Enforcement,
    string EvidenceCaptured,
    string Verification,
    IReadOnlyList<string> AuditEventCodes);

public sealed record VisualQaViewport(
    string Name,
    int Width,
    int Height);

public sealed record VisualQaRoute(
    string Code,
    string Label,
    string Description,
    string RequiredText,
    bool OpenFilingTab);

public sealed record VisualQaCoverage(
    string ArtifactName,
    string Enforcement,
    int ExpectedScreenshotCount,
    IReadOnlyList<string> LayoutChecks,
    IReadOnlyList<string> Themes,
    IReadOnlyList<VisualQaViewport> Viewports,
    IReadOnlyList<VisualQaRoute> Routes);

public sealed record ProductionReadinessReport(
    DateTime GeneratedAt,
    string OverallStatus,
    int CompaniesInDatabase,
    int PeriodsInDatabase,
    SourceLawSnapshot SourceLawSnapshot,
    IReadOnlyList<ProductionReadinessArea> Areas,
    IReadOnlyList<GoldenFilingCorpusScenario> GoldenFilingCorpus,
    IReadOnlyList<StatutoryRuleMatrixEntry> StatutoryRuleMatrix,
    IReadOnlyList<string> ManualHandoffPaths,
    IReadOnlyList<OperationalGate> OperationalGates,
    IReadOnlyList<ProductionReadinessAssuranceAction> AssuranceActions,
    IReadOnlyList<ProductionAuditabilityControl> AuditabilityControls,
    VisualQaCoverage VisualQaCoverage);

public class ProductionReadinessReportService(AccountsDbContext db)
{
    public async Task<ProductionReadinessReport> GetReportAsync(CancellationToken cancellationToken = default)
    {
        var companies = await db.Companies.CountAsync(cancellationToken);
        var periods = await db.AccountingPeriods.CountAsync(cancellationToken);

        return new ProductionReadinessReport(
            DateTime.UtcNow,
            "review-required",
            companies,
            periods,
            IrishStatutoryRuleSources.BuildSnapshot(),
            BuildAreas(),
            BuildGoldenCorpus(),
            BuildStatutoryRuleMatrix(),
            BuildManualHandoffPaths(),
            BuildOperationalGates(),
            BuildAssuranceActions(),
            BuildAuditabilityControls(),
            BuildVisualQaCoverage());
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
            [
                "AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl"
            ],
            [
                "classification selects Micro",
                "readiness has no missing items",
                "balance sheet balances",
                "PDF text includes company, period and micro statement",
                "iXBRL parses as XML"
            ],
            new(
                [
                    "accounts PDF text",
                    "CRO filing pack",
                    "CRO signature page",
                    "iXBRL XML",
                    "tax computation",
                    "notes disclosure set",
                    "filing readiness profile"
                ],
                [
                    "named qualified-accountant review",
                    "director and secretary certification",
                    "external ROS/iXBRL validation"
                ],
                [
                    "Micro regime selected",
                    "100% filing readiness",
                    "balance sheet balances",
                    "well-formed iXBRL",
                    "micro statutory statement present in PDF text"
                ],
                ProofPoints(
                    "AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl",
                    [
                        new("pdf-text", "PDF text contains company name, period and micro statutory statement."),
                        new("ixbrl-xml", "iXBRL XML is well-formed and contains the company legal name."),
                        new("filing-readiness", "Filing readiness reaches 100% with no missing items."),
                        new("tax-computation", "Tax computation is generated and reconciles to the worked micro scenario."),
                        new("notes-disclosure", "Notes disclosure set includes the required accounting policies."),
                        new("signatory-gates", "Director and secretary certification gates remain required before filing use.")
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
            [
                "AccountsWorkflowTests.GoldenPath_SmallAuditExemptCompany_MixedAccrualSetBalancesThroughPdfAndIxbrl"
            ],
            [
                "classification selects SmallAbridged",
                "mixed cash/accrual facts reconcile",
                "statutory PDF includes legal name, net assets and P&L",
                "CRO abridged pack omits P&L and cites Section 352",
                "signature page carries director and secretary certification",
                "iXBRL parses as XML and omits public P&L turnover"
            ],
            new(
                [
                    "full accounts PDF text",
                    "abridged CRO filing pack",
                    "CRO signature page",
                    "iXBRL XML",
                    "tax computation",
                    "notes disclosure set",
                    "filing readiness profile"
                ],
                [
                    "abridgement eligibility",
                    "director and secretary certification",
                    "named qualified-accountant review",
                    "external ROS/iXBRL validation"
                ],
                [
                    "SmallAbridged regime selected",
                    "Section 352 wording present in CRO pack",
                    "public P&L turnover omitted from iXBRL",
                    "tax computation matches worked scenario",
                    "notes include fixed assets and long-term creditors"
                ],
                ProofPoints(
                    "AccountsWorkflowTests.GoldenPath_SmallAuditExemptCompany_MixedAccrualSetBalancesThroughPdfAndIxbrl",
                    [
                        new("pdf-text", "Full accounts PDF text contains legal name, net assets, profit and loss and Section 352 abridgement wording."),
                        new("ixbrl-xml", "iXBRL XML is well-formed and omits public profit-and-loss turnover for the abridged CRO pack."),
                        new("filing-readiness", "Filing readiness confirms generated CRO, Revenue and accountant-review evidence gates."),
                        new("tax-computation", "Tax computation matches the mixed cash/accrual worked scenario."),
                        new("notes-disclosure", "Notes include fixed assets, creditors and the small-company disclosure set."),
                        new("signatory-gates", "CRO signature page carries director and secretary certification evidence.")
                    ]),
                [
                    IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                    IrishStatutoryRuleSources.FrcFrs102,
                    IrishStatutoryRuleSources.RevenueIxbrlContents,
                    IrishStatutoryRuleSources.RevenueAcceptedTaxonomies
                ])),
        new(
            "clg-charity",
            "CLG charity annual reporting",
            "Company limited by guarantee with charity evidence",
            "generated-pack-with-charity-gates",
            "covered",
            [
                "FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness"
            ],
            [
                "CLG remains in the supported company scope",
                "charity number evidence is required",
                "SoFA and trustees report evidence are required",
                "Charities Regulator source is attached"
            ],
            new(
                [
                    "CLG accounts PDF text",
                    "charity readiness profile",
                    "SoFA evidence",
                    "trustees annual report evidence",
                    "iXBRL XML",
                    "tax computation",
                    "notes disclosure set"
                ],
                [
                    "charity number",
                    "charity annual return review",
                    "named qualified-accountant review"
                ],
                [
                    "charity evidence satisfied",
                    "Charities Regulator source attached",
                    "CLG source attached",
                    "well-formed iXBRL"
                ],
                ProofPoints(
                    "FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness",
                    [
                        new("pdf-text", "CLG accounts PDF text contains company name and charity objectives."),
                        new("ixbrl-xml", "iXBRL XML is well-formed and contains the CLG legal name."),
                        new("filing-readiness", "Filing readiness confirms charity number, SoFA and trustees report evidence."),
                        new("tax-computation", "Tax computation is generated for the CLG charity scenario."),
                        new("notes-disclosure", "Notes include accounting policies and charity reporting disclosures."),
                        new("signatory-gates", "Charity annual return and named qualified-accountant review gates remain required.")
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
            [
                "FilingGoldenCorpusScenarioTests.GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence"
            ],
            [
                "audit report evidence is mandatory",
                "normal filing approval is blocked until auditor evidence exists",
                "manual professional handoff is exposed in readiness",
                "CRO medium-company and auditor-report sources are attached",
                "after auditor evidence, the full pack includes auditor report, P&L, cash flow and equity statements",
                "medium iXBRL includes tagged P&L facts"
            ],
            new(
                [
                    "full accounts PDF text",
                    "auditor report evidence",
                    "cash flow statement",
                    "statement of changes in equity",
                    "iXBRL XML",
                    "filing readiness profile",
                    "tax computation"
                ],
                [
                    "auditor handoff",
                    "manual professional review",
                    "normal CRO approval blocked until auditor evidence"
                ],
                [
                    "Medium regime selected",
                    "audit report blocker present before auditor evidence",
                    "tagged P&L facts present after auditor evidence",
                    "auditor reference appears in PDF text"
                ],
                ProofPoints(
                    "FilingGoldenCorpusScenarioTests.GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence",
                    [
                        new("pdf-text", "Full accounts PDF text is blocked until auditor evidence exists, then includes auditor report, P&L, cash flow and equity statements."),
                        new("ixbrl-xml", "iXBRL XML is well-formed and contains tagged profit-and-loss facts after auditor evidence."),
                        new("filing-readiness", "Filing readiness blocks approval before signed auditor report evidence and clears that blocker when evidence is recorded."),
                        new("tax-computation", "Tax computation is generated for the medium audit-required scenario."),
                        new("notes-disclosure", "Notes include turnover and tax-on-profit disclosures for the full accounts path."),
                        new("auditor-handoff", "Signed auditor report reference is mandatory before final output generation.")
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
                "Named qualified-accountant review recorded"
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
                IrishStatutoryRuleSources.RevenueIxbrlOverview
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
                "No direct CRO/ROS submission automation"
            ],
            [
                IrishStatutoryRuleSources.CroGuaranteeCompany,
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.FrcFrs102,
                IrishStatutoryRuleSources.RevenueIxbrlOverview
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
                "Manual handoff required for complex charity governance, restricted funds or regulator-specific queries"
            ],
            [
                IrishStatutoryRuleSources.CroGuaranteeCompany,
                IrishStatutoryRuleSources.CharitiesRegulatorAnnualReport,
                IrishStatutoryRuleSources.FrcFrs102
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
                "Manual professional handoff required before approval or submission states"
            ],
            [
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.CroMediumCompany,
                IrishStatutoryRuleSources.CroAuditorsReport,
                IrishStatutoryRuleSources.FrcFrs102,
                IrishStatutoryRuleSources.RevenueIxbrlOverview
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
            "required",
            "No generated filing pack can be treated as final until a named qualified accountant has reviewed the evidence, outputs and wording.",
            "Named qualified-accountant approval recorded against the period and linked to the generated pack."),
        new(
            "external-ros-validation",
            "External ROS/iXBRL validation",
            "Reviewer",
            "critical",
            "required",
            "Internal XML parsing is not a Revenue acceptance check, so real filings need a recorded external ROS validation result.",
            "External ROS validation evidence uploaded or referenced before any Revenue filing state is marked accepted."),
        new(
            "light-dark-visual-regression",
            "Light/dark visual regression",
            "Engineering",
            "high",
            "in-progress",
            "The accountant journey needs desktop and mobile screenshots across light and dark mode before it can be called visually production-ready.",
            "Screenshots for dashboard, company detail, period workspace and filing review in light desktop, dark desktop, light mobile and dark mobile."),
        new(
            "production-monitoring",
            "Production monitoring",
            "Operations",
            "high",
            "required",
            "Runtime failures must be visible to operators before real statutory filing packs are processed.",
            "Sentry production error routing configured and reviewed with structured log correlation."),
        new(
            "accountant-acceptance-walkthrough",
            "Accountant acceptance walkthrough",
            "Qualified accountant",
            "high",
            "required",
            "A qualified accountant must take the golden scenarios through the live workflow and confirm outputs, gates and wording are professionally acceptable.",
            "Signed acceptance note covering micro LTD, small abridged LTD, CLG charity and medium/audit-required manual handoff.")
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

    private static VisualQaCoverage BuildVisualQaCoverage()
    {
        var themes = new[] { "light", "dark" };
        var layoutChecks = new[]
        {
            "browser-console-errors",
            "page-horizontal-overflow",
            "visible-text-overlap"
        };
        var viewports = new[]
        {
            new VisualQaViewport("desktop", 1440, 1000),
            new VisualQaViewport("mobile", 390, 844)
        };
        var routes = new[]
        {
            new VisualQaRoute(
                "dashboard",
                "Dashboard",
                "Accountant queue, blockers, deadlines and production readiness overview.",
                "Production Readiness",
                OpenFilingTab: false),
            new VisualQaRoute(
                "production-readiness",
                "Production readiness",
                "Assurance checklist, statutory rules matrix, source snapshot and operational gates.",
                "Production Readiness Checklist",
                OpenFilingTab: false),
            new VisualQaRoute(
                "company-detail",
                "Company detail",
                "Company statutory profile, officers, charity facts and accounting periods.",
                "Accounting Periods",
                OpenFilingTab: false),
            new VisualQaRoute(
                "period-workspace",
                "Period workspace",
                "Import, classification, year-end, statements and filing readiness overview.",
                "Filing readiness",
                OpenFilingTab: false),
            new VisualQaRoute(
                "filing-review",
                "Filing review",
                "Period filing tab with evidence checklist, source links, outputs and filing state.",
                "Filing readiness profile",
                OpenFilingTab: true)
        };

        return new VisualQaCoverage(
            "visual-smoke-screenshots",
            "ci-production-smoke",
            routes.Length * themes.Length * viewports.Length,
            layoutChecks,
            themes,
            viewports,
            routes);
    }
}
