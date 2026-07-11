namespace Accounts.Api.Services;

public partial class ProductionReadinessReportService
{
    private static IReadOnlyList<ProductionReadinessArea> BuildAreas() =>
    [
        new(
            "backend-accounting-engine",
            "Backend accounting engine",
            "in-progress",
            "Machine golden paths exercise classification, regime selection, statements, PDF, iXBRL review output, readiness and workflow gates. Realistic end-to-end breadth, independent expected-value review and real external validation remain open."),
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
            "machine-review-pack",
            "machine-covered-review-pending",
            new(
                "Example Micro Limited",
                "Private",
                "2026-01-01",
                "2026-12-31",
                "Micro",
                "Micro",
                AuditExempt: true,
                ManualProfessionalReviewRequired: false),
            [
                "FilingGoldenCorpusScenarioTests.GoldenCorpus_MicroLtd_EmitsAccountsIxbrlTaxNotesReadinessAndSignatoryGates",
                "AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl",
                "GoldenCorpusPostgresReleaseTests.AllFiveImmutableScenarios_UsePublicDecisionAndArtifactWorkflowsOnPostgres"
            ],
            Verifiers(
                "FilingGoldenCorpusScenarioTests.GoldenCorpus_MicroLtd_EmitsAccountsIxbrlTaxNotesReadinessAndSignatoryGates",
                "AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl",
                "GoldenCorpusPostgresReleaseTests.AllFiveImmutableScenarios_UsePublicDecisionAndArtifactWorkflowsOnPostgres"),
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
                    "machine statement readiness complete; release gates remain open",
                    "balance sheet balances",
                    "well-formed iXBRL",
                    "micro statutory statement present in PDF text"
                ],
                new(
                    [
                        "Example Micro Limited",
                        "31 December 2026",
                        "600",
                        "280D"
                    ],
                    [
                        "core:TurnoverGrossRevenue"
                    ],
                    "machine-statements-ready-release-blocked",
                    62.50m,
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
                    "FilingGoldenCorpusScenarioTests.GoldenCorpus_MicroLtd_EmitsAccountsIxbrlTaxNotesReadinessAndSignatoryGates",
                    [
                        new("pdf-text", "PDF text contains company name, period and micro statutory statement."),
                        new("ixbrl-xml", "iXBRL XML is well-formed and contains the company legal name."),
                        new("filing-readiness", "Machine statement readiness completes while accountant, signatory and external-validation release gates remain open."),
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
                ]),
            LegalBasis(
                "micro-ltd",
                "Private",
                "Micro",
                "Micro",
                auditExempt: true,
                manualProfessionalReviewRequired: false,
                "FRS 105 micro-entities regime with CRO financial-statement and Revenue iXBRL filing evidence.",
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
                    IrishStatutoryRuleSources.CroFinancialStatementsRequirements.SourceId,
                    IrishStatutoryRuleSources.FrcFrs105.SourceId,
                    IrishStatutoryRuleSources.RevenueIxbrlOverview.SourceId,
                    IrishStatutoryRuleSources.RevenueAcceptedTaxonomies.SourceId
                ])),
        new(
            "small-abridged-ltd",
            "Small or small-abridged LTD filing",
            "Private company limited by shares, small/abridged regime",
            "machine-review-pack",
            "machine-covered-review-pending",
            new(
                "Connacht Digital Solutions Limited",
                "Private",
                "2026-01-01",
                "2026-12-31",
                "Small",
                "SmallAbridged",
                AuditExempt: true,
                ManualProfessionalReviewRequired: false),
            [
                "AccountsWorkflowTests.GoldenPath_SmallAuditExemptCompany_MixedAccrualSetBalancesThroughPdfAndIxbrl",
                "FilingGoldenCorpusScenarioTests.GoldenCorpus_SmallAbridgedLtd_EmitsFullAccountsAbridgedCroPackIxbrlAndReadiness",
                "GoldenCorpusPostgresReleaseTests.AllFiveImmutableScenarios_UsePublicDecisionAndArtifactWorkflowsOnPostgres"
            ],
            Verifiers(
                "AccountsWorkflowTests.GoldenPath_SmallAuditExemptCompany_MixedAccrualSetBalancesThroughPdfAndIxbrl",
                "FilingGoldenCorpusScenarioTests.GoldenCorpus_SmallAbridgedLtd_EmitsFullAccountsAbridgedCroPackIxbrlAndReadiness",
                "GoldenCorpusPostgresReleaseTests.AllFiveImmutableScenarios_UsePublicDecisionAndArtifactWorkflowsOnPostgres"),
            [
                "classification selects SmallAbridged",
                "mixed cash/accrual facts reconcile",
                "statutory PDF includes legal name, net assets and P&L",
                "CRO abridged pack omits P&L and cites Section 352",
                "signature page carries director and secretary certification",
                "iXBRL review prototype parses as XML, retains private P&L facts and remains manual-handoff-only",
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
                    "private P&L turnover retained in the manual-handoff review prototype",
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
                        "core:TurnoverGrossRevenue"
                    ],
                    "generated-output-evidence-required",
                    62.50m,
                    [
                        "Accounting Policies",
                        "Tangible Fixed Assets",
                        "Creditors"
                    ],
                    [
                        "abridgement eligibility confirmed",
                        "director and secretary certification required",
                        "qualified-accountant review required",
                        "external ROS/iXBRL validation required"
                    ],
                    "review-required"),
                ProofPoints(
                    "AccountsWorkflowTests.GoldenPath_SmallAuditExemptCompany_MixedAccrualSetBalancesThroughPdfAndIxbrl",
                    [
                        new("pdf-text", "Full accounts PDF text contains legal name, net assets, profit and loss and Section 352 abridgement wording."),
                        new("ixbrl-xml", "The iXBRL review prototype is well-formed, retains Revenue-required private profit-and-loss facts and remains manual-handoff-only."),
                        new("filing-readiness", "Filing readiness confirms machine-generated CRO artifacts and exposes unsatisfied Revenue/external-validation and accountant-review gates."),
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
                ]),
            LegalBasis(
                "small-abridged-ltd",
                "Private",
                "Small",
                "SmallAbridged",
                auditExempt: true,
                manualProfessionalReviewRequired: false,
                "FRS 102 small-company abridgement with Section 352 CRO filing evidence and Revenue iXBRL evidence.",
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
                    IrishStatutoryRuleSources.CroFinancialStatementsRequirements.SourceId,
                    IrishStatutoryRuleSources.FrcFrs102.SourceId,
                    IrishStatutoryRuleSources.RevenueIxbrlContents.SourceId,
                    IrishStatutoryRuleSources.RevenueAcceptedTaxonomies.SourceId
                ])),
        new(
            "dac-small",
            "Small DAC filing",
            "Designated activity company, small FRS 102 regime",
            "machine-review-pack",
            "machine-covered-review-pending",
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
                "FilingGoldenCorpusScenarioTests.GoldenCorpus_DacSmall_EmitsAccountsIxbrlAndSourceBackedReadiness",
                "GoldenCorpusPostgresReleaseTests.AllFiveImmutableScenarios_UsePublicDecisionAndArtifactWorkflowsOnPostgres"
            ],
            Verifiers(
                "FilingGoldenCorpusScenarioTests.GoldenCorpus_DacSmall_EmitsAccountsIxbrlAndSourceBackedReadiness",
                "GoldenCorpusPostgresReleaseTests.AllFiveImmutableScenarios_UsePublicDecisionAndArtifactWorkflowsOnPostgres"),
            [
                "DAC company type remains in the supported path",
                "small FRS 102 regime is selected",
                "directors' report and CRO signature-template bytes are generated",
                "iXBRL review prototype parses as XML while Revenue filing-ready generation stays disabled",
                "accountant sign-off packet remains blocked on genuine reviewer, executed-signature and external-validation evidence"
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
                    "machine-artifacts-generated-release-blocked",
                    62.50m,
                    [
                        "Accounting Policies"
                    ],
                    [
                        "executed director and secretary certification required",
                        "qualified-accountant review required",
                        "external ROS/iXBRL validation required"
                    ],
                    "review-required"),
                ProofPoints(
                    "FilingGoldenCorpusScenarioTests.GoldenCorpus_DacSmall_EmitsAccountsIxbrlAndSourceBackedReadiness",
                    [
                        new("pdf-text", "DAC accounts PDF text contains the legal name and directors' report."),
                        new("ixbrl-xml", "iXBRL XML is well-formed and contains the DAC legal name."),
                        new("filing-readiness", "Filing readiness confirms DAC source-backed support and leaves review, executed-signature and external-validation gates open."),
                        new("tax-computation", "Tax computation is generated for the DAC small-company scenario."),
                        new("notes-disclosure", "Notes include the required accounting policies for the small-company path."),
                        new("signatory-gates", "CRO signature-template bytes exist but executed director and secretary evidence is not fabricated."),
                        new("accountant-signoff-packet", "Accountant sign-off packet remains blocked pending genuine reviewer and external validation evidence.")
                    ]),
                [
                    IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                    IrishStatutoryRuleSources.FrcFrs102,
                    IrishStatutoryRuleSources.RevenueIxbrlOverview,
                    IrishStatutoryRuleSources.RevenueAcceptedTaxonomies
                ]),
            LegalBasis(
                "dac-small",
                "DesignatedActivityCompany",
                "Small",
                "Small",
                auditExempt: true,
                manualProfessionalReviewRequired: false,
                "FRS 102 small-company DAC path with directors' report, CRO certification and Revenue iXBRL evidence.",
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
                    IrishStatutoryRuleSources.CroFinancialStatementsRequirements.SourceId,
                    IrishStatutoryRuleSources.FrcFrs102.SourceId,
                    IrishStatutoryRuleSources.RevenueIxbrlOverview.SourceId,
                    IrishStatutoryRuleSources.RevenueAcceptedTaxonomies.SourceId
                ])),
        new(
            "clg-charity",
            "CLG charity annual reporting",
            "Company limited by guarantee with charity evidence",
            "machine-review-pack-with-charity-gates-open",
            "machine-covered-review-pending",
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
                "FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness",
                "GoldenCorpusPostgresReleaseTests.AllFiveImmutableScenarios_UsePublicDecisionAndArtifactWorkflowsOnPostgres"
            ],
            Verifiers(
                "FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness",
                "GoldenCorpusPostgresReleaseTests.AllFiveImmutableScenarios_UsePublicDecisionAndArtifactWorkflowsOnPostgres"),
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
                    "SoFA evidence requirement (pending genuine trustee review)",
                    "trustees annual report evidence requirement (pending genuine trustee review)",
                    "iXBRL XML",
                    "tax computation",
                    "notes disclosure set",
                    "accountant sign-off packet"
                ],
                [
                    "charity number",
                    "charity annual return review",
                    "named qualified-accountant review",
                    "external ROS/iXBRL validation",
                    "accountant sign-off packet state"
                ],
                [
                    "charity number satisfied while SoFA and trustees evidence remain pending",
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
                        "core:TurnoverGrossRevenue"
                    ],
                    "charity-evidence-review-required",
                    62.50m,
                    [
                        "Accounting Policies",
                        "Charity reporting disclosures"
                    ],
                    [
                        "charity number satisfied",
                        "SoFA and trustees annual report evidence required",
                        "qualified-accountant review required",
                        "external ROS/iXBRL validation required"
                    ],
                    "review-required"),
                ProofPoints(
                    "FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness",
                    [
                        new("pdf-text", "CLG accounts PDF text contains company name and charity objectives."),
                        new("ixbrl-xml", "iXBRL XML is well-formed and contains the CLG legal name."),
                        new("filing-readiness", "Filing readiness confirms the charity number and exposes unsatisfied SoFA, trustees-report and accountant-review evidence."),
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
                ]),
            LegalBasis(
                "clg-charity",
                "CompanyLimitedByGuarantee",
                "Small",
                "Small",
                auditExempt: true,
                manualProfessionalReviewRequired: false,
                "CLG charity reporting path with CRO guarantee-company, Charities Regulator annual-report and FRS 102 evidence.",
                [
                    "CLG accounts PDF text",
                    "charity readiness profile",
                    "SoFA evidence requirement (pending genuine trustee review)",
                    "trustees annual report evidence requirement (pending genuine trustee review)",
                    "iXBRL XML",
                    "tax computation",
                    "notes disclosure set",
                    "accountant sign-off packet"
                ],
                [
                    "charity number",
                    "charity annual return review",
                    "named qualified-accountant review",
                    "external ROS/iXBRL validation",
                    "accountant sign-off packet state"
                ],
                [
                    IrishStatutoryRuleSources.CroGuaranteeCompany.SourceId,
                    IrishStatutoryRuleSources.CharitiesRegulatorAnnualReport.SourceId,
                    IrishStatutoryRuleSources.FrcFrs102.SourceId,
                    IrishStatutoryRuleSources.RevenueIxbrlOverview.SourceId
                ])),
        new(
            "medium-audit-required",
            "Medium audit-required handoff",
            "Medium company or non-audit-exempt filing",
            "manual-handoff",
            "machine-covered-review-pending",
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
                "FilingGoldenCorpusScenarioTests.GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence",
                "GoldenCorpusPostgresReleaseTests.AllFiveImmutableScenarios_UsePublicDecisionAndArtifactWorkflowsOnPostgres"
            ],
            Verifiers(
                "FilingGoldenCorpusScenarioTests.GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence",
                "GoldenCorpusPostgresReleaseTests.AllFiveImmutableScenarios_UsePublicDecisionAndArtifactWorkflowsOnPostgres"),
            [
                "audit report evidence is mandatory",
                "normal filing approval is blocked until auditor evidence exists",
                "manual professional handoff is exposed in readiness",
                "CRO medium-company and auditor-report sources are attached",
                "final accounts PDF generation remains blocked because no genuine signed auditor report is supplied",
                "medium iXBRL review prototype includes tagged P&L facts without becoming filing-ready",
                "accountant sign-off packet records manual handoff and retains the auditor blocker"
            ],
            new(
                [
                    "full accounts PDF blocker evidence",
                    "auditor report evidence requirement (pending genuine evidence)",
                    "cash flow statement requirement",
                    "statement of changes in equity requirement",
                    "iXBRL XML",
                    "filing readiness profile",
                    "tax computation",
                    "accountant sign-off packet"
                ],
                [
                    "auditor handoff",
                    "manual professional review",
                    "normal CRO approval blocked until auditor evidence",
                    "external ROS/iXBRL validation",
                    "accountant sign-off packet state"
                ],
                [
                    "Medium regime selected",
                    "audit report blocker present before auditor evidence",
                    "tagged P&L facts present in the review prototype",
                    "final PDF remains blocked pending genuine auditor evidence"
                ],
                new(
                    [
                        "signed auditor's report is required before final PDF generation"
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
                        "manual professional review required",
                        "external ROS/iXBRL validation required"
                    ],
                    "manual-handoff"),
                ProofPoints(
                    "FilingGoldenCorpusScenarioTests.GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence",
                    [
                        new("pdf-text", "No full accounts PDF is emitted; the diagnostic requires a genuine signed auditor report."),
                        new("ixbrl-xml", "The iXBRL review prototype is well-formed and contains tagged profit-and-loss facts while filing-ready generation remains disabled."),
                        new("filing-readiness", "Filing readiness retains the signed-auditor-report and professional-handoff blockers throughout the machine scenario."),
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
                ]),
            LegalBasis(
                "medium-audit-required",
                "Private",
                "Medium",
                "Medium",
                auditExempt: false,
                manualProfessionalReviewRequired: true,
                "Medium-company audit-required path blocked to manual handoff until auditor report and professional review evidence are present.",
                [
                    "full accounts PDF blocker evidence",
                    "auditor report evidence requirement (pending genuine evidence)",
                    "cash flow statement requirement",
                    "statement of changes in equity requirement",
                    "iXBRL XML",
                    "filing readiness profile",
                    "tax computation",
                    "accountant sign-off packet"
                ],
                [
                    "auditor handoff",
                    "manual professional review",
                    "normal CRO approval blocked until auditor evidence",
                    "external ROS/iXBRL validation",
                    "accountant sign-off packet state"
                ],
                [
                    IrishStatutoryRuleSources.CroMediumCompany.SourceId,
                    IrishStatutoryRuleSources.CroAuditorsReport.SourceId,
                    IrishStatutoryRuleSources.FrcFrs102.SourceId,
                    IrishStatutoryRuleSources.RevenueIxbrlOverview.SourceId
                ]))
    ];

    private static GoldenFilingCorpusLegalBasisSnapshot LegalBasis(
        string scenarioCode,
        string companyType,
        string sizeClass,
        string electedRegime,
        bool auditExempt,
        bool manualProfessionalReviewRequired,
        string legalBasis,
        IReadOnlyList<string> requiredOutputs,
        IReadOnlyList<string> professionalGates,
        IReadOnlyList<string> sourceIds) =>
        new(
            scenarioCode,
            companyType,
            sizeClass,
            electedRegime,
            auditExempt,
            manualProfessionalReviewRequired,
            legalBasis,
            requiredOutputs,
            professionalGates,
            sourceIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());

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
}
