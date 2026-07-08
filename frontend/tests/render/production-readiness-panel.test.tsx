import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { ProductionReadinessPanel } from "@/components/ProductionReadinessPanel";
import type { ProductionReadinessReport } from "@/lib/api";

describe("ProductionReadinessPanel", () => {
  it("surfaces golden corpus, statutory source and operational gate evidence", () => {
    render(<ProductionReadinessPanel report={sampleReport()} />);

    expect(screen.getByText("Production Readiness")).toBeInTheDocument();
    expect(screen.getAllByText("Review required")).toHaveLength(2);
    expect(screen.getAllByText("Micro LTD").length).toBeGreaterThanOrEqual(2);
    expect(screen.getByText("5/5")).toBeInTheDocument();
    expect(screen.getAllByText("Small abridged LTD").length).toBeGreaterThanOrEqual(2);
    expect(screen.getAllByText("DAC small").length).toBeGreaterThanOrEqual(2);
    expect(screen.getAllByText("CLG charity")).toHaveLength(2);
    expect(screen.getAllByText("Medium audit-required")).toHaveLength(2);
    expect(screen.getAllByText("Example Micro Limited")).toHaveLength(2);
    expect(screen.getAllByText("2025-01-01 to 2025-12-31").length).toBeGreaterThanOrEqual(4);
    expect(screen.getByText("AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl")).toBeInTheDocument();
    expect(screen.getByText("Legal basis snapshots")).toBeInTheDocument();
    expect(screen.getByText("FRS 105 micro-entities regime with CRO financial-statement and Revenue iXBRL filing evidence.")).toBeInTheDocument();
    expect(screen.getAllByText("Sources: frc-frs-105").length).toBeGreaterThan(0);
    expect(screen.getByText("Golden evidence ledger")).toBeInTheDocument();
    expect(screen.getAllByText("Expected CT: €62.50").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Readiness: ready-for-external-filing").length).toBeGreaterThanOrEqual(3);
    expect(screen.getByText("Completion tracks")).toBeInTheDocument();
    expect(screen.getByText("Backend code")).toBeInTheDocument();
    expect(screen.getByText("Frontend UI/UX")).toBeInTheDocument();
    expect(screen.getByText("Frontend code")).toBeInTheDocument();
    expect(screen.getByText("Run qualified-accountant acceptance on the golden corpus.")).toBeInTheDocument();
    expect(screen.getByText("Review each screenshot route-by-route.")).toBeInTheDocument();
    expect(screen.getByText("Continue extracting large route files.")).toBeInTheDocument();
    expect(screen.getByText("No direct CRO/ROS submission automation")).toBeInTheDocument();
    expect(screen.getByText("Revenue accepted iXBRL taxonomies")).toBeInTheDocument();
    expect(screen.getByText("Qualified accountant sign-off")).toBeInTheDocument();
    expect(screen.getByText("Named accountant approval recorded against the period.")).toBeInTheDocument();
  });
});

function productionScorecard(): ProductionReadinessReport["productionScorecard"] {
  return {
    currentScore: 619,
    targetScore: 700,
    status: "review-required",
    nextGate: "Complete source-law review, named visual QA, monitoring-provider confirmation, manual handoff and qualified-accountant acceptance evidence.",
    categories: [
      {
        code: "architecture-documentation",
        label: "Architecture and documentation",
        currentScore: 99,
        targetScore: 100,
        status: "release-evidence-required",
        currentEvidence: [
          "Canonical architecture guide and active handoff are present.",
          "source-law-review-template.md is checked in and release-verifier covered.",
          "verify-release-artifact-pack.ps1 is documented for exact release evidence packs with checksum inventory.",
        ],
        remainingGaps: ["Complete release evidence templates with named reviewers, including source-law-review-template.md."],
        completionTrackCodes: ["backend-code", "frontend-ui-ux", "frontend-code"],
        releaseBlockerCodes: ["backend-code:qualified-accountant-signoff", "frontend-ui-ux:light-dark-visual-regression"],
      },
      {
        code: "backend-statutory-accounting-engine",
        label: "Backend statutory/accounting engine",
        currentScore: 204,
        targetScore: 250,
        status: "qualified-accountant-review-required",
        currentEvidence: [
          "Golden filing corpus covers the production scenarios.",
          "Qualified-accountant acceptance evidence uses canonical golden corpus scenario codes.",
          "verify-release-evidence.ps1 rejects qualified-accountant acceptance unless every golden scenario decision and route evidence acceptance row is explicitly accepted.",
          "External ROS/iXBRL validation evidence has template and verifier checks for real references, accepted/remediated warnings and accepted decisions.",
          "Source-law review evidence has template and verifier checks for reachability, effective-date review, guidance comparison, impact classification and accepted decisions.",
          "Manual handoff acceptance evidence has template and verifier checks for real evidence references and accepted reviewer decisions.",
          "CI retains production-readiness-report.json from the live smoke stack with source-law, golden corpus, scorecard and release blocker evidence.",
        ],
        remainingGaps: ["Run and retain verified source-law, qualified-accountant acceptance, external ROS/iXBRL validation, and manual handoff evidence across every canonical golden corpus scenario."],
        completionTrackCodes: ["backend-code"],
        releaseBlockerCodes: ["backend-code:qualified-accountant-signoff"],
      },
      {
        code: "frontend-accountant-workbench",
        label: "Frontend accountant workbench",
        currentScore: 166,
        targetScore: 200,
        status: "visual-acceptance-required",
        currentEvidence: [
          "Visual smoke plan covers the accountant journey.",
          "visual-smoke-evidence-report.json proves screenshot hash, byte-size, PNG dimension, nonblank pixel diversity, per-screenshot layout-check pass results, automated theme-contrast smoke results and route/theme/viewport coverage.",
          "visual-qa-signoff-template.md and verify-release-evidence.ps1 require reviewers to record visual smoke nonblank pixel and contrast metrics before visual QA evidence can pass.",
          "accountant-workbench-evidence-report.json proves route workflow-stage, review-check and qualified-accountant route acceptance coverage.",
          "Frontend parser invariants now require the CI machine evidence pack, production smoke, readiness verification, visual smoke and manual release-verification rows before rendering readiness data.",
        ],
        remainingGaps: ["Complete named visual QA review against the screenshot manifest and visual-smoke-evidence-report.json."],
        completionTrackCodes: ["frontend-ui-ux", "frontend-code"],
        releaseBlockerCodes: ["frontend-ui-ux:light-dark-visual-regression"],
      },
      {
        code: "security-auth-tenant-platform-guardrails",
        label: "Security/auth/tenant/platform guardrails",
        currentScore: 150,
        targetScore: 150,
        status: "operator-confirmation-required",
        currentEvidence: [
          "Auth, tenant and platform release gates are represented in readiness evidence.",
          "CI retains no-direct-filing-submission-report.json for the no-direct CRO/ROS submission verifier.",
          "Release artifact pack verifier validates operational reports together.",
          "release-artifact-pack-report.json records release candidate identity plus per-report SHA-256 and byte-size evidence.",
          "ci-machine-evidence-pack-report.json records exact commit/run identity plus machine-evidence SHA-256 inventory.",
          "verify-production-readiness-report.ps1 now requires default-CI and manual release manifest rows, including the no-direct CRO/ROS control, CI machine evidence pack and release artifact pack.",
          "verify-release-artifact-pack.ps1 now rejects release packs unless production-readiness-verification-report.json proves every required default-CI and manual release manifest row.",
          "verify-release-artifact-pack.ps1 and verify-ci-machine-evidence-pack.ps1 now reject visual evidence packs unless visual-smoke-evidence-report.json carries planned PNG viewport dimensions, passed layout-check results and automated theme-contrast smoke results for every screenshot.",
          "verify-release-evidence.ps1 now rejects completed human evidence when release candidate identity, UTC timestamps, SHA-256 digests, external iXBRL artifact hashes, or monitoring log confirmation fields are malformed.",
          "verify-release-evidence.ps1 emits a consistent releaseCandidate identity for all six human evidence templates, and verify-release-artifact-pack.ps1 rejects packs whose release-evidence-report.json identity does not match the pack CommitSha and GitHubActionsRunUrl.",
          "verify-release-evidence.ps1 emits SHA-256/byte-size manifest entries for all six human release-evidence templates, and verify-release-artifact-pack.ps1 requires those completed templates to be retained in the pack with matching hashes.",
          "Monitoring-provider confirmation evidence requires real provider/event/correlation references, an HTTPS provider base URL, a matched structured-log smoke line and an explicit accepted operator decision.",
        ],
        remainingGaps: ["Confirm the controlled monitoring smoke event inside the configured provider and retain the full release-artifact-pack-report.json after release-evidence-report.json is completed with named human sign-offs."],
        completionTrackCodes: ["backend-code"],
        releaseBlockerCodes: ["backend-code:qualified-accountant-signoff"],
      },
    ],
  };
}

function sampleReport(): ProductionReadinessReport {
  return {
    generatedAt: "2026-07-03T12:00:00Z",
    overallStatus: "review-required",
    companiesInDatabase: 3,
    periodsInDatabase: 4,
    sourceLawSnapshot: {
      snapshotDate: "2026-07-03",
      snapshotVersion: "irish-statutory-accounts-sources-2026-07-03",
      contentHash: "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
      sourceCount: 1,
      sources: [
        {
          sourceId: "revenue-accepted-taxonomies",
          title: "Revenue accepted iXBRL taxonomies",
          effectiveDate: "2025-11-06",
          url: "https://www.revenue.ie/",
        },
      ],
    },
    sourceLawTraceability: [
      {
        sourceId: "revenue-accepted-taxonomies",
        title: "Revenue accepted iXBRL taxonomies",
        effectiveDate: "2025-11-06",
        url: "https://www.revenue.ie/",
        inSnapshot: true,
        usedBy: ["statutory-rule-matrix:ltd-micro"],
        releaseGateCodes: ["external-ros-validation", "ixbrl-taxonomy-selection"],
      },
    ],
    sourceLawMaintenanceProtocol: {
      protocolVersion: "source-law-maintenance-v1",
      ownerRole: "Qualified accountant and engineering",
      status: "required-review",
      reviewCadence: "Before every production release and at least monthly while source-backed filing logic is active.",
      nextReviewDue: "2026-08-03",
      signOffGate: "source-law-change-review",
      changeDetection: "Compare CRO, Revenue, FRC and Charities Regulator guidance pages against the pinned source-law snapshot before release.",
      failurePolicy: "Block release if any pinned source changes, becomes unreachable, gains a newer effective date, or lacks qualified-accountant review.",
      monitoredSourceIds: ["revenue-accepted-taxonomies"],
      acceptanceCriteria: [
        "CRO, Revenue, FRC and Charities Regulator source pages are reachable and reviewed for changes.",
        "Every changed effective date or guidance wording is reflected in source-law snapshot metadata before release.",
        "A qualified accountant accepts the source-law review note before generated filing packs are used for real filings.",
      ],
      requiredEvidence: [
        "source-law-snapshot-fingerprint",
        "source-law-traceability-index",
        "source-law-change-review-note",
        "qualified-accountant-source-law-signoff",
      ],
    },
    sourceLawReviewLedger: [
      {
        sourceId: "revenue-accepted-taxonomies",
        title: "Revenue accepted iXBRL taxonomies",
        url: "https://www.revenue.ie/",
        pinnedEffectiveDate: "2025-11-06",
        ownerRole: "Taxonomy and corporation tax reviewer",
        releaseChecklistCode: "source-law-change-review",
        blocksRelease: true,
        reviewChecks: [
          "Confirm source page is reachable at the pinned URL.",
          "Compare pinned effective date against the current source page.",
          "Review guidance wording for statutory filing, exemption, note or taxonomy changes.",
          "Confirm Revenue-accepted taxonomy and iXBRL content guidance still match generated output assumptions.",
        ],
        requiredEvidence: [
          "source-law-change-review-note",
          "qualified-accountant-source-law-signoff",
        ],
      },
    ],
    revenueTaxonomyRanges: [
      {
        taxonomyKey: "irish-extension-2025-frs-102",
        accountingStandard: "FRS 102",
        taxonomyDate: "2025-01-01",
        label: "Irish Extension 2025 FRS 102 taxonomy accepted by Revenue",
        schemaRef: "https://xbrl.frc.org.uk/ireland/FRS-102/2025-01-01/ie-FRS-102-2025-01-01.xsd",
        acceptedByRevenue: true,
        automatedPlatformSelectionSupported: true,
        effectiveForPeriodsStartingOnOrAfter: "2024-01-01",
        effectiveForPeriodsStartingBefore: "",
        sourceIds: ["revenue-accepted-taxonomies"],
        releaseGateCodes: ["external-ros-validation", "ixbrl-taxonomy-selection", "source-law-change-review"],
      },
    ],
    assurancePacket: {
      packetId: "assurance-sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
      packetVersion: "production-assurance-packet-v1",
      status: "review-required",
      sourceLawSnapshotHash: "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
      goldenCorpusCovered: 5,
      goldenCorpusTotal: 5,
      statutoryRuleMatrixPaths: 1,
      statutoryRuleCoverageFamilies: 1,
      visualQaExpectedScreenshots: 28,
      requiredOperationalGates: 1,
      openCriticalActions: 1,
      evidenceItems: ["source-law-snapshot-fingerprint", "source-law-traceability-index", "source-law-maintenance-protocol", "source-law-review-ledger", "revenue-taxonomy-range-evidence", "golden-filing-corpus", "golden-evidence-ledger", "golden-verifier-manifest", "audit-evidence-timeline", "production-audit-evidence-pack", "operations-evidence-pack", "production-readiness-report", "production-readiness-verification-report", "visual-smoke-screenshots", "accountant-workbench-evidence-report", "release-review-checklist", "release-verification-manifest", "accountant-acceptance-summary", "accountant-workflow-walkthrough-protocol", "accountant-journey-acceptance-checklist", "accountant-workflow-evidence-pack", "accountant-walkthrough-evidence-matrix", "workbench-visual-acceptance-register", "production-completion-map", "production-scorecard"],
      releaseBlockers: ["Qualified accountant sign-off required", "Source-law change review required", "External ROS/iXBRL validation required", "Accountant acceptance walkthrough required", "Light/dark visual regression required"],
    },
    productionScorecard: productionScorecard(),
    accountantAcceptanceCriteria: [
      {
        scenarioCode: "micro-ltd",
        label: "Micro LTD accountant acceptance",
        required: true,
        acceptanceStatus: "qualified-accountant-review-required",
        reviewScope: ["PDF wording", "iXBRL XML", "filing readiness profile"],
        requiredEvidence: ["Named qualified-accountant approval recorded against the generated pack."],
        requiredSignOffGate: "Named qualified accountant must approve the generated pack before real filing use.",
        evidenceVerifiers: [
          {
            name: "AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl",
            command: "dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl",
            ciScope: "default-ci",
            runsInDefaultCi: true,
            environment: "EF Core InMemory golden fixture; CI also runs the broader backend suite on Linux",
            evidenceLevel: "end-to-end golden filing scenario",
          },
        ],
        sources: [
          {
            sourceId: "frc-frs-105",
            title: "FRC FRS 105 current edition and amendments",
            effectiveDate: "2026-07-03",
            url: "https://www.frc.org.uk/",
          },
        ],
      },
    ],
    accountantAcceptanceSummary: {
      scenarioCount: 2,
      automatedVerifierCount: 1,
      professionalSignOffRequiredCount: 1,
      manualHandoffScenarioCount: 0,
      releaseBlockingScenarioCodes: ["micro-ltd"],
      requiredSignOffGates: ["Named qualified accountant must approve the generated pack before real filing use."],
      status: "qualified-accountant-review-required",
    },
    accountantWorkflowWalkthroughProtocol: {
      protocolVersion: "accountant-workflow-walkthrough-v1",
      reviewerRole: "Qualified accountant",
      status: "required-review",
      signOffGate: "golden-corpus-accountant-acceptance",
      failurePolicy: "Block release if a named qualified accountant has not walked the seeded golden corpus through the live accountant workflow and accepted the outputs, gates, wording and evidence.",
      seededScenarioCodes: goldenScenarioCodes(),
      routeSequence: [
        "Dashboard: identify the client, deadline pressure, blockers, reviewer owner and next action.",
        "Company detail: confirm statutory profile, company type, officers, charity flags and period setup.",
        "Period workspace: review import, classification, year-end evidence, statements, notes and workflow rail state.",
        "Financial statements: inspect statement preview, tax computation, source trail and directors' report evidence.",
        "Filing review: inspect readiness profile, legal source links, generated outputs, signatory gates and accountant sign-off packet.",
        "Production readiness: confirm golden corpus, statutory rules coverage, visual QA, release blockers and operational controls.",
      ],
      acceptanceCriteria: [
        "Micro LTD walkthrough confirms PDF wording, iXBRL XML, tax computation, notes, signatory gates and 100% filing readiness.",
        "Small abridged LTD walkthrough confirms full accounts, abridged CRO pack, Section 352 evidence, iXBRL and audit-exemption gates.",
        "CLG charity walkthrough confirms charity number, SoFA, trustees annual report, charity notes and Charities Regulator evidence.",
        "Medium/audit-required walkthrough confirms auditor handoff blocks normal approval until signed auditor report evidence and manual acceptance are recorded.",
        "A named qualified accountant states that the generated outputs, gates, wording and evidence are professionally acceptable for the supported scope.",
      ],
      requiredEvidence: [
        "seeded golden corpus walkthrough note",
        "named qualified-accountant approval",
        "visual QA screenshot review",
        "generated PDF and iXBRL evidence",
        "manual handoff acceptance",
      ],
    },
    accountantJourneyAcceptanceChecklist: [
      journeyAcceptance("dashboard", "Dashboard", "dashboard", accountantWorkflowStages()),
      journeyAcceptance("company-detail", "Company detail", "company", ["Setup"]),
      journeyAcceptance("period-workspace", "Period workspace", "period", accountantWorkflowStages()),
      journeyAcceptance("financial-statements", "Financial statements", "financialStatements", ["Statements"], [
        "Financial statements route exposes statement preview, tax computation, source trail and directors' report evidence before filing review.",
        "A named qualified accountant accepts the Financial statements route outputs, gates, wording and evidence for every seeded golden scenario.",
      ]),
      journeyAcceptance("filing-review", "Filing review", "filing", ["Review", "Filing"], [
        "Filing review route exposes readiness, source links, generated outputs, signatory gates, accountant sign-off packet, external ROS/iXBRL validation and filing state.",
        "A named qualified accountant accepts the Filing review route outputs, gates, wording and evidence for every seeded golden scenario.",
      ]),
      journeyAcceptance("production-readiness", "Production readiness", "readiness", ["Review", "Filing"], [
        "Production readiness route exposes backend checks, filing rules coverage, unsupported paths, security posture, release blockers and accountant review state.",
        "A named qualified accountant accepts the Production readiness route outputs, gates, wording and evidence for every seeded golden scenario.",
      ]),
    ],
    accountantWorkflowEvidencePack: accountantWorkflowEvidencePack(),
    accountantWalkthroughEvidenceMatrix: accountantWalkthroughEvidenceMatrix(),
    workbenchVisualAcceptanceRegister: workbenchVisualAcceptanceRegister(),
    areas: [
      {
        code: "backend-accounting-engine",
        label: "Backend accounting engine",
        status: "hardened",
        detail: "Golden-path coverage exercises outputs and gates.",
      },
    ],
    goldenFilingCorpus: [
      {
        code: "micro-ltd",
        label: "Micro LTD",
        companyScope: "Private company limited by shares",
        expectedOutcome: "generated-pack",
        coverageStatus: "covered",
        fixture: {
          legalName: "Example Micro Limited",
          companyType: "Private",
          periodStart: "2025-01-01",
          periodEnd: "2025-12-31",
          expectedSizeClass: "Micro",
          expectedRegime: "Micro",
          auditExempt: true,
          manualProfessionalReviewRequired: false,
        },
        evidenceTestNames: ["AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl"],
        evidenceVerifiers: [
          {
            name: "AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl",
            command: "dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl",
            ciScope: "default-ci",
            runsInDefaultCi: true,
            environment: "EF Core InMemory golden fixture; CI also runs the broader backend suite on Linux",
            evidenceLevel: "end-to-end golden filing scenario",
          },
        ],
        assertions: ["PDF text", "iXBRL parse"],
        evidencePack: {
          outputArtifacts: ["accounts PDF text", "iXBRL XML"],
          decisionGates: ["named qualified-accountant review"],
          expectedValueChecks: ["well-formed iXBRL"],
          expectedOutputs: {
            pdfTextMarkers: ["Example Micro Limited", "280D"],
            ixbrlRequiredTags: ["core:EntityCurrentLegalOrRegisteredName"],
            filingReadinessState: "100% filing readiness",
            expectedCorporationTax: 62.5,
            requiredNotes: ["Accounting Policies"],
            filingGateStates: ["director and secretary certification required", "qualified-accountant review required"],
            signOffPacketState: "review-required",
          },
          expectedProofPoints: [
            {
              area: "pdf-text",
              expectedEvidence: "PDF text contains company name and micro statutory statement.",
              automatedVerifier: "AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl",
              required: true,
            },
          ],
          sourceReferences: [
            {
              sourceId: "frc-frs-105",
              title: "FRC FRS 105 current edition and amendments",
              effectiveDate: "2026-07-03",
              url: "https://www.frc.org.uk/",
            },
          ],
        },
        legalBasisSnapshot: {
          scenarioCode: "micro-ltd",
          companyType: "Private",
          sizeClass: "Micro",
          electedRegime: "Micro",
          auditExempt: true,
          manualProfessionalReviewRequired: false,
          legalBasis: "FRS 105 micro-entities regime with CRO financial-statement and Revenue iXBRL filing evidence.",
          requiredOutputs: ["accounts PDF text", "iXBRL XML"],
          professionalGates: ["named qualified-accountant review"],
          sourceIds: ["frc-frs-105"],
        },
      },
      goldenScenario({
        code: "small-abridged-ltd",
        label: "Small abridged LTD",
        legalName: "Example Small Abridged Limited",
        companyType: "Private",
        expectedRegime: "Small abridged",
        expectedOutcome: "generated-pack-with-abridged-cro-filing",
        verifier: "FilingGoldenCorpusScenarioTests.GoldenCorpus_SmallAbridgedLtd_ProducesFullAccountsAbridgedCroPackAndSection352Evidence",
        corporationTax: 1875,
        readinessState: "ready-for-external-filing",
        signOffPacketState: "review-required",
        proofArea: "abridgement",
        sourceId: "cro-financial-statements-requirements",
        sourceTitle: "CRO financial statements requirements",
        sourceUrl: "https://cro.ie/",
      }),
      goldenScenario({
        code: "dac-small",
        label: "DAC small",
        legalName: "Example DAC Trading Designated Activity Company",
        companyType: "DesignatedActivityCompany",
        expectedRegime: "Small",
        expectedOutcome: "generated-pack",
        verifier: "FilingGoldenCorpusScenarioTests.GoldenCorpus_DacSmall_ProducesFullAccountsIxbrlAndDirectorCertificationGates",
        corporationTax: 2437.5,
        readinessState: "ready-for-external-filing",
        signOffPacketState: "review-required",
        proofArea: "director-certification",
        sourceId: "cro-financial-statements-requirements",
        sourceTitle: "CRO financial statements requirements",
        sourceUrl: "https://cro.ie/",
      }),
      {
        code: "clg-charity",
        label: "CLG charity",
        companyScope: "Company limited by guarantee",
        expectedOutcome: "generated-pack-with-charity-gates",
        coverageStatus: "covered",
        fixture: {
          legalName: "Dublin Community Support CLG",
          companyType: "CompanyLimitedByGuarantee",
          periodStart: "2026-01-01",
          periodEnd: "2026-12-31",
          expectedSizeClass: "Small",
          expectedRegime: "Small",
          auditExempt: true,
          manualProfessionalReviewRequired: false,
        },
        evidenceTestNames: ["FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness"],
        evidenceVerifiers: [
          {
            name: "FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness",
            command: "dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness",
            ciScope: "default-ci",
            runsInDefaultCi: true,
            environment: "EF Core InMemory golden fixture; CI also runs the broader backend suite on Linux",
            evidenceLevel: "end-to-end golden filing scenario",
          },
        ],
        assertions: ["charity evidence"],
        evidencePack: {
          outputArtifacts: ["CLG accounts PDF text", "charity readiness profile"],
          decisionGates: ["charity number", "charity annual return review"],
          expectedValueChecks: ["charity evidence satisfied"],
          expectedOutputs: {
            pdfTextMarkers: ["Dublin Community Support CLG", "Community support and education."],
            ixbrlRequiredTags: ["core:EntityCurrentLegalOrRegisteredName"],
            filingReadinessState: "ready-for-external-filing",
            expectedCorporationTax: 62.5,
            requiredNotes: ["Accounting Policies", "Charity reporting disclosures"],
            filingGateStates: ["charity number satisfied", "qualified-accountant review recorded"],
            signOffPacketState: "ready-for-external-filing",
          },
          expectedProofPoints: [
            {
              area: "filing-readiness",
              expectedEvidence: "Filing readiness confirms charity number, SoFA and trustees report evidence.",
              automatedVerifier: "FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness",
              required: true,
            },
          ],
          sourceReferences: [
            {
              sourceId: "charities-regulator-annual-report",
              title: "Charities Regulator annual report guidance",
              effectiveDate: "2026-07-03",
              url: "https://www.charitiesregulator.ie/",
            },
          ],
        },
        legalBasisSnapshot: {
          scenarioCode: "clg-charity",
          companyType: "CompanyLimitedByGuarantee",
          sizeClass: "Small",
          electedRegime: "Small",
          auditExempt: true,
          manualProfessionalReviewRequired: false,
          legalBasis: "CLG charity reporting path with CRO guarantee-company, Charities Regulator annual-report and FRS 102 evidence.",
          requiredOutputs: ["CLG accounts PDF text", "charity readiness profile"],
          professionalGates: ["charity number", "charity annual return review"],
          sourceIds: ["charities-regulator-annual-report"],
        },
      },
      goldenScenario({
        code: "medium-audit-required",
        label: "Medium audit-required",
        legalName: "Example Medium Holdings Limited",
        companyType: "Private",
        expectedSizeClass: "Medium",
        expectedRegime: "Full",
        expectedOutcome: "manual-handoff",
        verifier: "FilingGoldenCorpusScenarioTests.GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence",
        corporationTax: 15625,
        readinessState: "manual-handoff",
        signOffPacketState: "manual-handoff",
        proofArea: "auditor-handoff",
        sourceId: "cro-financial-statements-requirements",
        sourceTitle: "CRO financial statements requirements",
        sourceUrl: "https://cro.ie/",
        manualProfessionalReviewRequired: true,
      }),
    ],
    goldenEvidenceLedger: [
      {
        scenarioCode: "micro-ltd",
        label: "Micro LTD",
        fixtureLegalName: "Example Micro Limited",
        companyType: "Private",
        expectedOutcome: "generated-pack",
        coverageStatus: "covered",
        acceptanceStatus: "qualified-accountant-review-required",
        requiredSignOffGate: "Named qualified accountant must approve the generated pack before real filing use.",
        blocksRelease: true,
        automatedVerifierNames: ["AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl"],
        automatedVerifierCommands: ["dotnet test backend/Accounts.slnx --filter FullyQualifiedName~AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl"],
        ciScopes: ["default-ci"],
        evidenceLevels: ["end-to-end golden filing scenario"],
        outputArtifacts: ["accounts PDF text", "iXBRL XML"],
        decisionGates: ["named qualified-accountant review"],
        expectedValueChecks: ["well-formed iXBRL"],
        proofPointAreas: ["pdf-text"],
        sourceIds: ["frc-frs-105"],
        expectedCorporationTax: 62.5,
        filingReadinessState: "100% filing readiness",
        signOffPacketState: "review-required",
      },
      goldenLedgerEntry({
        scenarioCode: "small-abridged-ltd",
        label: "Small abridged LTD",
        legalName: "Example Small Abridged Limited",
        companyType: "Private",
        expectedOutcome: "generated-pack-with-abridged-cro-filing",
        acceptanceStatus: "qualified-accountant-review-required",
        verifier: "FilingGoldenCorpusScenarioTests.GoldenCorpus_SmallAbridgedLtd_ProducesFullAccountsAbridgedCroPackAndSection352Evidence",
        artifacts: ["full accounts PDF text", "abridged CRO accounts pack", "iXBRL XML"],
        checks: ["Section 352 abridgement evidence", "audit exemption gate"],
        proofArea: "abridgement",
        sourceId: "cro-financial-statements-requirements",
        corporationTax: 1875,
        readinessState: "ready-for-external-filing",
        signOffPacketState: "review-required",
      }),
      goldenLedgerEntry({
        scenarioCode: "dac-small",
        label: "DAC small",
        legalName: "Example DAC Trading Designated Activity Company",
        companyType: "DesignatedActivityCompany",
        expectedOutcome: "generated-pack",
        acceptanceStatus: "qualified-accountant-review-required",
        verifier: "FilingGoldenCorpusScenarioTests.GoldenCorpus_DacSmall_ProducesFullAccountsIxbrlAndDirectorCertificationGates",
        artifacts: ["accounts PDF text", "iXBRL XML", "director certification gate"],
        checks: ["director certification", "well-formed iXBRL"],
        proofArea: "director-certification",
        sourceId: "cro-financial-statements-requirements",
        corporationTax: 2437.5,
        readinessState: "ready-for-external-filing",
        signOffPacketState: "review-required",
      }),
      {
        scenarioCode: "clg-charity",
        label: "CLG charity",
        fixtureLegalName: "Dublin Community Support CLG",
        companyType: "CompanyLimitedByGuarantee",
        expectedOutcome: "generated-pack-with-charity-gates",
        coverageStatus: "covered",
        acceptanceStatus: "qualified-accountant-review-required",
        requiredSignOffGate: "Named qualified accountant must approve the CLG charity pack and charity evidence before real filing use.",
        blocksRelease: true,
        automatedVerifierNames: ["FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness"],
        automatedVerifierCommands: ["dotnet test backend/Accounts.slnx --filter FullyQualifiedName~FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness"],
        ciScopes: ["default-ci"],
        evidenceLevels: ["end-to-end golden filing scenario"],
        outputArtifacts: ["CLG accounts PDF text", "charity readiness profile"],
        decisionGates: ["charity number", "charity annual return review"],
        expectedValueChecks: ["charity evidence satisfied"],
        proofPointAreas: ["filing-readiness"],
        sourceIds: ["charities-regulator-annual-report"],
        expectedCorporationTax: 62.5,
        filingReadinessState: "ready-for-external-filing",
        signOffPacketState: "ready-for-external-filing",
      },
      goldenLedgerEntry({
        scenarioCode: "medium-audit-required",
        label: "Medium audit-required",
        legalName: "Example Medium Holdings Limited",
        companyType: "Private",
        expectedOutcome: "manual-handoff",
        acceptanceStatus: "manual-handoff-required",
        verifier: "FilingGoldenCorpusScenarioTests.GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence",
        artifacts: ["full accounts PDF text", "iXBRL XML", "auditor handoff record"],
        checks: ["audit report blocker", "manual handoff state"],
        proofArea: "auditor-handoff",
        sourceId: "cro-financial-statements-requirements",
        corporationTax: 15625,
        readinessState: "manual-handoff",
        signOffPacketState: "manual-handoff",
      }),
    ],
    goldenVerifierManifest: [],
    statutoryRuleMatrix: [
      {
        code: "ltd-micro",
        companyScope: "LTD micro",
        sizeOrRegime: "Micro / FRS 105",
        supportLevel: "supported",
        requiredEvidence: ["size classification"],
        requiredOutputs: ["micro accounts PDF"],
        manualHandoffGates: ["qualified accountant review"],
        sources: [
          {
            sourceId: "cro-financial-statements-requirements",
            title: "CRO financial statements requirements",
            effectiveDate: "2026-07-03",
            url: "https://cro.ie/",
          },
        ],
      },
    ],
    statutoryRulesCoverage: [
      {
        code: "size-classification-thresholds",
        ruleFamily: "Size classification",
        decisionUnderTest: "Two-of-three thresholds and current/prior movement produce the statutory size class.",
        coverageStatus: "covered",
        automatedVerifierNames: ["AccountsWorkflowTests.SizeClassification_FirstYearMicro_AllowsMicroAndAuditExemption"],
        edgeCases: ["two-of-three threshold rule"],
        sources: [
          {
            sourceId: "cro-financial-statements-requirements",
            title: "CRO financial statements requirements",
            effectiveDate: "2026-07-03",
            url: "https://cro.ie/",
          },
        ],
      },
    ],
    manualHandoffPaths: ["PLC and public-company workflows"],
    operationalGates: [
      {
        code: "qualified-accountant-review",
        label: "Named qualified-accountant review",
        required: true,
        status: "required",
        detail: "Real filing packs require named professional approval before use.",
      },
      {
        code: "external-ros-validation",
        label: "External ROS/iXBRL validation",
        required: true,
        status: "required",
        detail: "External validation evidence must be retained before Revenue filing use.",
      },
      {
        code: "no-direct-cro-ros-submission",
        label: "No direct CRO/ROS submission automation",
        required: true,
        status: "enforced",
        detail: "Final filing operations remain recorded workflow states only.",
      },
      {
        code: "production-ci-gates",
        label: "Production CI evidence gates",
        required: true,
        status: "required",
        detail: "Production smoke, backup restore and visual evidence must be retained for the exact release candidate.",
      },
    ],
    assuranceActions: [
      {
        code: "qualified-accountant-signoff",
        label: "Qualified accountant sign-off",
        owner: "Qualified accountant",
        priority: "critical",
        riskRank: 0,
        evidenceStage: "accountant-review-gate",
        status: "required",
        detail: "No real filing pack can be treated as final until a named qualified accountant has approved it.",
        evidenceRequired: "Named accountant approval recorded against the period.",
      },
      {
        code: "source-law-change-review",
        label: "Source-law change review",
        owner: "Qualified accountant and engineering",
        priority: "critical",
        riskRank: 2,
        evidenceStage: "source-law-maintenance",
        status: "required",
        detail: "Pinned CRO, Revenue, FRC and charity guidance must be reviewed for effective-date or wording changes before release.",
        evidenceRequired: "Source-law change review note and qualified-accountant sign-off recorded against the snapshot.",
      },
      {
        code: "external-ros-validation",
        label: "External ROS/iXBRL validation",
        owner: "Reviewer",
        priority: "critical",
        riskRank: 5,
        evidenceStage: "external-validation-gate",
        status: "required",
        detail: "Internal XML checks are not a Revenue acceptance check.",
        evidenceRequired: "External ROS validation evidence uploaded or referenced.",
      },
      {
        code: "no-direct-cro-ros-submission",
        label: "No direct CRO/ROS submission automation",
        owner: "Engineering",
        priority: "critical",
        riskRank: 6,
        evidenceStage: "unsupported-path-gate",
        status: "complete",
        detail: "The platform never automates final CRO or ROS submission.",
        evidenceRequired: "Release reviewer confirms final filing operations remain recorded workflow states only.",
      },
      {
        code: "accountant-acceptance-walkthrough",
        label: "Accountant acceptance walkthrough",
        owner: "Qualified accountant",
        priority: "high",
        riskRank: 10,
        evidenceStage: "golden-corpus-acceptance",
        status: "required",
        detail: "A qualified accountant must accept outputs, gates and wording.",
        evidenceRequired: "Signed acceptance note for the golden corpus.",
      },
      {
        code: "production-monitoring",
        label: "Production monitoring and backup evidence",
        owner: "Operations",
        priority: "high",
        riskRank: 20,
        evidenceStage: "production-operations-evidence",
        status: "required",
        detail: "Production smoke, monitoring routing and backup restore evidence must be retained for the exact release candidate.",
        evidenceRequired: "CI production stack smoke and backup restore evidence.",
      },
      {
        code: "light-dark-visual-regression",
        label: "Light/dark visual regression",
        owner: "Engineering",
        priority: "high",
        riskRank: 30,
        evidenceStage: "visual-qa-evidence",
        status: "in-progress",
        detail: "The accountant journey needs screenshot evidence across light and dark mode.",
        evidenceRequired: "Light/dark desktop/mobile screenshots for the main workflow routes.",
      },
    ],
    releaseBlockerRegister: [
      {
        code: "backend-code:qualified-accountant-signoff",
        trackCode: "backend-code",
        trackLabel: "Backend code",
        ownerRole: "Qualified accountant",
        severity: "critical",
        riskRank: 0,
        blockingIssue: "Qualified accountant sign-off required",
        requiredEvidence: "Named accountant approval recorded against the period.",
        nextAction: "Run qualified-accountant acceptance on the golden corpus.",
        sourceActionCode: "qualified-accountant-signoff",
        releaseChecklistCode: "accountant-final-signoff",
        operationalGateCode: "qualified-accountant-review",
        evidenceArtifact: "named-accountant-approval-record",
        blocksRelease: true,
      },
      {
        code: "backend-code:source-law-change-review",
        trackCode: "backend-code",
        trackLabel: "Backend code",
        ownerRole: "Qualified accountant and engineering",
        severity: "critical",
        riskRank: 2,
        blockingIssue: "Source-law change review required",
        requiredEvidence: "Source-law change review note and qualified-accountant sign-off recorded against the snapshot.",
        nextAction: "Run qualified-accountant acceptance on the golden corpus.",
        sourceActionCode: "source-law-change-review",
        releaseChecklistCode: "source-law-change-review",
        operationalGateCode: "qualified-accountant-review",
        evidenceArtifact: "source-law-change-review-note",
        blocksRelease: true,
      },
      {
        code: "backend-code:external-ros-validation",
        trackCode: "backend-code",
        trackLabel: "Backend code",
        ownerRole: "Reviewer",
        severity: "critical",
        riskRank: 5,
        blockingIssue: "External ROS/iXBRL validation required",
        requiredEvidence: "External ROS validation evidence uploaded or referenced.",
        nextAction: "Attach external ROS/iXBRL validation evidence for generated iXBRL packs.",
        sourceActionCode: "external-ros-validation",
        releaseChecklistCode: "external-ros-validation-evidence",
        operationalGateCode: "external-ros-validation",
        evidenceArtifact: "external-ros-validation-reference",
        blocksRelease: true,
      },
      {
        code: "backend-code:accountant-acceptance-walkthrough",
        trackCode: "backend-code",
        trackLabel: "Backend code",
        ownerRole: "Qualified accountant",
        severity: "high",
        riskRank: 10,
        blockingIssue: "Accountant acceptance walkthrough required",
        requiredEvidence: "Signed acceptance note for the golden corpus.",
        nextAction: "Run qualified-accountant acceptance on the golden corpus.",
        sourceActionCode: "accountant-acceptance-walkthrough",
        releaseChecklistCode: "golden-corpus-accountant-acceptance",
        operationalGateCode: "qualified-accountant-review",
        evidenceArtifact: "signed-golden-corpus-acceptance-note",
        blocksRelease: true,
      },
      {
        code: "frontend-ui-ux:light-dark-visual-regression",
        trackCode: "frontend-ui-ux",
        trackLabel: "Frontend UI/UX",
        ownerRole: "Engineering",
        severity: "high",
        riskRank: 30,
        blockingIssue: "Light/dark visual regression required",
        requiredEvidence: "Light/dark desktop/mobile screenshots for the main workflow routes.",
        nextAction: "Review each screenshot route-by-route in light and dark mode.",
        sourceActionCode: "light-dark-visual-regression",
        releaseChecklistCode: "visual-qa-screenshot-review",
        operationalGateCode: "production-ci-gates",
        evidenceArtifact: "light-dark-desktop-mobile-screenshot-review",
        blocksRelease: true,
      },
      {
        code: "frontend-ui-ux:accountant-acceptance-walkthrough",
        trackCode: "frontend-ui-ux",
        trackLabel: "Frontend UI/UX",
        ownerRole: "Qualified accountant",
        severity: "high",
        riskRank: 10,
        blockingIssue: "Accountant acceptance walkthrough required",
        requiredEvidence: "Signed acceptance note for the golden corpus.",
        nextAction: "Record named visual acceptance against the smoke manifest.",
        sourceActionCode: "accountant-acceptance-walkthrough",
        releaseChecklistCode: "golden-corpus-accountant-acceptance",
        operationalGateCode: "qualified-accountant-review",
        evidenceArtifact: "signed-golden-corpus-acceptance-note",
        blocksRelease: true,
      },
      {
        code: "frontend-code:light-dark-visual-regression",
        trackCode: "frontend-code",
        trackLabel: "Frontend code",
        ownerRole: "Engineering",
        severity: "high",
        riskRank: 30,
        blockingIssue: "Light/dark visual regression required",
        requiredEvidence: "Light/dark desktop/mobile screenshots for the main workflow routes.",
        nextAction: "Expand visual regression assertions from screenshot capture into reviewable sign-off.",
        sourceActionCode: "light-dark-visual-regression",
        releaseChecklistCode: "visual-qa-screenshot-review",
        operationalGateCode: "production-ci-gates",
        evidenceArtifact: "light-dark-desktop-mobile-screenshot-review",
        blocksRelease: true,
      },
    ],
    completionTracks: [
      {
        code: "backend-code",
        label: "Backend code",
        ownerRole: "Engineering",
        status: "review-required",
        completionCriteria: ["Golden filing corpus proves statutory output gates."],
        currentEvidence: ["Backend golden corpus scenarios are covered."],
        nextActions: ["Run qualified-accountant acceptance on the golden corpus."],
        assuranceActionCodes: [
          "qualified-accountant-signoff",
          "source-law-change-review",
          "external-ros-validation",
          "no-direct-cro-ros-submission",
          "accountant-acceptance-walkthrough",
        ],
      },
      {
        code: "frontend-ui-ux",
        label: "Frontend UI/UX",
        ownerRole: "Product design",
        status: "in-progress",
        completionCriteria: ["Accountant workflow rail is visually coherent."],
        currentEvidence: ["Visual QA route audit covers the main routes."],
        nextActions: ["Review each screenshot route-by-route."],
        assuranceActionCodes: ["light-dark-visual-regression", "accountant-acceptance-walkthrough"],
      },
      {
        code: "frontend-code",
        label: "Frontend code",
        ownerRole: "Frontend engineering",
        status: "in-progress",
        completionCriteria: ["Typed API contract blocks readiness drift."],
        currentEvidence: ["API client invariants validate readiness contracts."],
        nextActions: ["Continue extracting large route files."],
        assuranceActionCodes: ["light-dark-visual-regression"],
      },
    ],
    releaseReviewChecklist: [
      {
        code: "accountant-final-signoff",
        label: "Named accountant final sign-off",
        ownerRole: "Qualified accountant",
        required: true,
        status: "required",
        blocksRelease: true,
        evidenceArtifact: "named-accountant-approval-record",
        assuranceActionCode: "qualified-accountant-signoff",
        operationalGateCode: "qualified-accountant-review",
        auditEventCodes: ["CroFilingStatusChanged"],
        detail: "Named professional approval must be recorded against the period before any real filing pack is treated as final.",
      },
      {
        code: "source-law-change-review",
        label: "Source-law change review",
        ownerRole: "Qualified accountant and engineering",
        required: true,
        status: "required",
        blocksRelease: true,
        evidenceArtifact: "source-law-change-review-note",
        assuranceActionCode: "source-law-change-review",
        operationalGateCode: "qualified-accountant-review",
        auditEventCodes: ["CroFilingStatusChanged", "IxbrlInternalCheckCompleted"],
        detail: "Pinned CRO, Revenue, FRC and charity guidance must be reviewed for effective-date or wording changes before release.",
      },
      {
        code: "external-ros-validation-evidence",
        label: "External ROS/iXBRL validation evidence",
        ownerRole: "Reviewer",
        required: true,
        status: "required",
        blocksRelease: true,
        evidenceArtifact: "external-ros-validation-reference",
        assuranceActionCode: "external-ros-validation",
        operationalGateCode: "external-ros-validation",
        auditEventCodes: ["IxbrlInternalCheckCompleted"],
        detail: "Internal XML checks are not enough for Revenue acceptance; the reviewer must retain external validation evidence.",
      },
      {
        code: "no-direct-cro-ros-submission",
        label: "No direct CRO/ROS submission automation",
        ownerRole: "Engineering",
        required: true,
        status: "complete",
        blocksRelease: false,
        evidenceArtifact: "no-direct-cro-ros-submission-control",
        assuranceActionCode: "no-direct-cro-ros-submission",
        operationalGateCode: "no-direct-cro-ros-submission",
        auditEventCodes: [],
        detail: "Release reviewer confirms final filing operations remain recorded workflow states only.",
      },
      {
        code: "golden-corpus-accountant-acceptance",
        label: "Golden corpus accountant acceptance",
        ownerRole: "Qualified accountant",
        required: true,
        status: "required",
        blocksRelease: true,
        evidenceArtifact: "signed-golden-corpus-acceptance-note",
        assuranceActionCode: "accountant-acceptance-walkthrough",
        operationalGateCode: "qualified-accountant-review",
        auditEventCodes: ["CroDocumentGenerated", "IxbrlInternalCheckCompleted", "NotesGenerated"],
        detail: "A qualified accountant must walk the golden scenarios through the live workflow and accept outputs, gates and wording.",
      },
      {
        code: "production-smoke-and-backup",
        label: "Production smoke and backup evidence",
        ownerRole: "Operations",
        required: true,
        status: "required",
        blocksRelease: true,
        evidenceArtifact: "ci-production-stack-smoke-and-backup-restore",
        assuranceActionCode: "production-monitoring",
        operationalGateCode: "production-ci-gates",
        auditEventCodes: [],
        detail: "Release evidence must include successful production stack smoke, visual smoke, monitoring configuration and backup restore drill.",
      },
      {
        code: "visual-qa-screenshot-review",
        label: "Light/dark visual QA screenshot review",
        ownerRole: "Engineering",
        required: true,
        status: "in-progress",
        blocksRelease: true,
        evidenceArtifact: "light-dark-desktop-mobile-screenshot-review",
        assuranceActionCode: "light-dark-visual-regression",
        operationalGateCode: "production-ci-gates",
        auditEventCodes: [],
        detail: "Desktop and mobile screenshots in light and dark mode must be reviewed for the accountant workflow before release.",
      },
    ],
    releaseVerificationManifest: [
      {
        code: "backend-golden-corpus",
        label: "Backend golden corpus and statutory rules",
        ownerRole: "Engineering",
        command: "dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art",
        ciScope: "default-ci",
        runsInDefaultCi: true,
        blocksRelease: true,
        evidenceArtifact: "backend-test-results",
        releaseChecklistEvidenceArtifact: "named-accountant-approval-record",
        manualFallback: "Run the same command locally from backend/ when GitHub Actions is unavailable.",
      },
      {
        code: "frontend-workbench-contract",
        label: "Frontend workbench contract, render and API checks",
        ownerRole: "Engineering",
        command: "npm test",
        ciScope: "default-ci",
        runsInDefaultCi: true,
        blocksRelease: true,
        evidenceArtifact: "frontend-test-results",
        releaseChecklistEvidenceArtifact: "light-dark-desktop-mobile-screenshot-review",
        manualFallback: "Run from frontend/ and retain the unit, render, readiness, proxy, auth and API-client verifier output.",
      },
      {
        code: "frontend-production-build",
        label: "Frontend lint, type-check and production build",
        ownerRole: "Engineering",
        command: "npm run lint; npx tsc --noEmit --incremental false; npm run build",
        ciScope: "default-ci",
        runsInDefaultCi: true,
        blocksRelease: true,
        evidenceArtifact: "frontend-build-results",
        releaseChecklistEvidenceArtifact: "light-dark-desktop-mobile-screenshot-review",
        manualFallback: "Run from frontend/ and retain lint, TypeScript and Next production build output when CI is unavailable.",
      },
      {
        code: "visual-smoke-light-dark",
        label: "Light/dark desktop/mobile visual smoke",
        ownerRole: "Engineering",
        command: "node scripts/visual-smoke.mjs; node scripts/verify-visual-smoke-artifacts.mjs --report-path=artifacts/visual-smoke/visual-smoke-evidence-report.json; node scripts/verify-accountant-workbench-evidence.mjs --visual-report=artifacts/visual-smoke/visual-smoke-evidence-report.json --report-path=artifacts/visual-smoke/accountant-workbench-evidence-report.json",
        ciScope: "default-ci",
        runsInDefaultCi: true,
        blocksRelease: true,
        evidenceArtifact: "artifacts/visual-smoke",
        releaseChecklistEvidenceArtifact: "light-dark-desktop-mobile-screenshot-review",
        manualFallback: "Run visual smoke locally, then retain the manifest verification output and review the generated artifacts manually.",
      },
      {
        code: "source-law-change-review",
        label: "Source-law change review note",
        ownerRole: "Qualified accountant and engineering",
        command: "manual review: compare pinned CRO, Revenue, FRC and charity guidance against source-law snapshot",
        ciScope: "manual-release",
        runsInDefaultCi: false,
        blocksRelease: true,
        evidenceArtifact: "source-law-change-review-note",
        releaseChecklistEvidenceArtifact: "source-law-change-review-note",
        manualFallback: "Retain a dated source-law review note before relying on the generated packs for real filing use.",
      },
      {
        code: "qualified-accountant-final-signoff",
        label: "Named accountant final sign-off evidence",
        ownerRole: "Qualified accountant",
        command: "manual review: record named qualified-accountant approval against the final generated filing pack",
        ciScope: "manual-release",
        runsInDefaultCi: false,
        blocksRelease: true,
        evidenceArtifact: "named-accountant-approval-record",
        releaseChecklistEvidenceArtifact: "named-accountant-approval-record",
        manualFallback: "A named qualified accountant must approve the exact generated filing pack before real filing use.",
      },
      {
        code: "external-ros-validation-evidence",
        label: "External ROS/iXBRL validation evidence",
        ownerRole: "Reviewer",
        command: "manual review: retain external ROS/iXBRL validation reference for the exact generated iXBRL pack",
        ciScope: "manual-release",
        runsInDefaultCi: false,
        blocksRelease: true,
        evidenceArtifact: "external-ros-validation-reference",
        releaseChecklistEvidenceArtifact: "external-ros-validation-reference",
        manualFallback: "Retain the external ROS validation reference before final approval.",
      },
      {
        code: "no-direct-cro-ros-submission-control",
        label: "No direct CRO/ROS submission automation control",
        ownerRole: "Engineering",
        command: "manual review: confirm final CRO and ROS operations remain recorded workflow states only with no direct submission client configured",
        ciScope: "manual-release",
        runsInDefaultCi: false,
        blocksRelease: true,
        evidenceArtifact: "no-direct-cro-ros-submission-control",
        releaseChecklistEvidenceArtifact: "no-direct-cro-ros-submission-control",
        manualFallback: "Confirm final filing operations remain recorded workflow states only: generated, reviewed, approved, marked submitted, payment confirmed, accepted, rejected or corrected.",
      },
      {
        code: "manual-accountant-acceptance",
        label: "Named accountant acceptance walkthrough",
        ownerRole: "Qualified accountant",
        command: "manual walkthrough: micro LTD, small abridged LTD, CLG charity and medium/audit-required handoff",
        ciScope: "manual-release",
        runsInDefaultCi: false,
        blocksRelease: true,
        evidenceArtifact: "signed-golden-corpus-acceptance-note",
        releaseChecklistEvidenceArtifact: "signed-golden-corpus-acceptance-note",
        manualFallback: "A named qualified accountant must accept the generated outputs, gates, wording and source-law evidence.",
      },
      {
        code: "production-readiness-report-verification",
        label: "Production readiness report verification",
        ownerRole: "Engineering",
        command: "pwsh ./scripts/verify-production-readiness-report.ps1 -ReportPath production-readiness-report.json -EvidencePath production-readiness-verification-report.json",
        ciScope: "default-ci",
        runsInDefaultCi: true,
        blocksRelease: true,
        evidenceArtifact: "production-readiness-report",
        releaseChecklistEvidenceArtifact: "ci-production-stack-smoke-and-backup-restore",
        manualFallback: "Run after capturing the live production-readiness response and retain production-readiness-verification-report.json.",
      },
      {
        code: "production-stack-smoke",
        label: "Production compose smoke",
        ownerRole: "Operations",
        command: "pwsh ./scripts/smoke-production.ps1 -CheckMonitoringErrorRouting; pwsh ./scripts/verify-production-readiness-report.ps1; pwsh ./scripts/verify-structured-logs.ps1",
        ciScope: "default-ci",
        runsInDefaultCi: true,
        blocksRelease: true,
        evidenceArtifact: "ci-production-stack-smoke-and-backup-restore",
        releaseChecklistEvidenceArtifact: "ci-production-stack-smoke-and-backup-restore",
        manualFallback: "Run the production smoke script against the production compose profile and retain monitoring, structured log and filing-workflow output.",
      },
      {
        code: "backup-restore-drill",
        label: "PostgreSQL backup and restore drill",
        ownerRole: "Operations",
        command: "pwsh ./scripts/verify-postgres-backup.ps1",
        ciScope: "default-ci",
        runsInDefaultCi: true,
        blocksRelease: true,
        evidenceArtifact: "ci-production-stack-smoke-and-backup-restore",
        releaseChecklistEvidenceArtifact: "ci-production-stack-smoke-and-backup-restore",
        manualFallback: "Run the backup verification script after creating a fresh production-shape dump and retain the checksum and restore verification output.",
      },
      {
        code: "ci-machine-evidence-pack",
        label: "CI machine evidence pack",
        ownerRole: "Engineering",
        command: "pwsh ./scripts/verify-ci-machine-evidence-pack.ps1 -EvidenceDirectory <downloaded-ci-artifacts> -ReportPath ci-machine-evidence-pack-report.json -CommitSha <release-commit-sha> -GitHubActionsRunUrl <ci-run-url>",
        ciScope: "default-ci",
        runsInDefaultCi: true,
        blocksRelease: true,
        evidenceArtifact: "ci-machine-evidence-pack",
        releaseChecklistEvidenceArtifact: "ci-production-stack-smoke-and-backup-restore",
        manualFallback: "Run after downloading CI machine artifacts for the exact candidate.",
      },
      {
        code: "release-artifact-pack",
        label: "Release artifact pack verification",
        ownerRole: "Engineering",
        command: "pwsh ./scripts/verify-release-artifact-pack.ps1 -EvidenceDirectory <release-artifacts> -ReportPath release-artifact-pack-report.json -CommitSha <release-commit-sha> -GitHubActionsRunUrl <ci-run-url>",
        ciScope: "manual-release",
        runsInDefaultCi: false,
        blocksRelease: true,
        evidenceArtifact: "release-artifact-pack-report",
        releaseChecklistEvidenceArtifact: "named-accountant-approval-record",
        manualFallback: "Run against the collected machine and human release evidence reports for the exact release candidate.",
      },
    ],
    auditabilityControls: [
      {
        code: "who-changed-what",
        label: "Who changed what",
        required: true,
        enforcement: "audit-log-integrity-chain",
        evidenceCaptured: "Authenticated user id, timestamp, entity, action and old/new value snapshots.",
        verification: "Hash chain verification covers each company-scoped audit row.",
        auditEventCodes: ["AdjustmentUpdated"],
      },
    ],
    auditEvidenceTimeline: [
      {
        code: "data-change-capture",
        stage: "Working papers",
        evidenceQuestion: "Who changed what and when?",
        capturedWhen: "At every authenticated write before regenerated outputs can be reviewed.",
        requiredActor: "Authenticated firm user",
        verification: "Audit log snapshots and integrity hash chain must cover each changed entity.",
        auditEventCodes: ["AdjustmentUpdated"],
        blockingGateCodes: ["working-paper-review"],
      },
    ],
    auditEvidencePack: [
      {
        code: "who-changed-what",
        label: "Who changed what",
        evidenceQuestion: "Which authenticated user changed statutory, accounting or filing evidence, and what old/new values were captured?",
        requiredArtifact: "tamper-evident-audit-log-entry",
        retainedIn: "audit_logs",
        requiredActor: "Authenticated firm user",
        capturedWhen: "At the same transaction boundary as each supported write.",
        verification: "Audit entry must include entity, action, request correlation, redacted before/after snapshots, integrity hash and previous hash.",
        failurePolicy: "Block release when a supported write path can alter filing evidence without an audit row linked into the integrity chain.",
        auditEventCodes: ["AdjustmentUpdated"],
        blockingGateCodes: ["working-paper-review"],
      },
    ],
    monitoringControls: [
      {
        code: "error-tracking",
        label: "Production error tracking",
        provider: "Sentry-compatible",
        required: true,
        productionSafetyGate: "Monitoring:ErrorTrackingDsn",
        evidenceCaptured: "Unhandled exceptions are routed to the configured production error-tracking provider.",
        verification: "Program.cs wires UseSentry and ProductionSafetyService blocks a missing DSN.",
        alertRoute: "Primary on-call accountant and platform owner",
        failurePolicy: "Block release if error events cannot be routed to the on-call owner.",
      },
    ],
    dependencyPolicyControls: [
      {
        code: "frontend-npm-audit",
        label: "Frontend dependency vulnerability audit",
        required: true,
        enforcement: "CI frontend job runs npm audit --audit-level=moderate after npm ci.",
        evidenceCaptured: "npm audit report for dependencies resolved from frontend/package-lock.json.",
        verification: ".github/workflows/ci.yml Audit frontend dependencies step.",
        failurePolicy: "Fail the release for moderate, high or critical npm advisories.",
      },
    ],
    deploymentSafetyControls: [
      {
        code: "controlled-production-migrations",
        label: "Controlled production migrations",
        required: true,
        enforcement: "Production migrations run through dotnet Accounts.Api.dll --migrate-only before app startup.",
        evidenceCaptured: "CI production image contract and release runbook prove migrations are a separate controlled step.",
        verification: "Program.cs handles --migrate-only and ProductionSafetyService blocks unsafe AutoMigrateOnStartup.",
        failurePolicy: "Fail production startup when AutoMigrateOnStartup is enabled without explicit production approval.",
      },
    ],
    operationsEvidencePack: [
      {
        code: "backup-restore-drill",
        label: "Backup restore drill",
        category: "Deployment safety",
        ownerRole: "Platform owner",
        required: true,
        command: "Run scripts/backup-postgres.ps1 and scripts/verify-postgres-backup.ps1 against the production compose shape before approving the release.",
        requiredArtifact: "postgres-backup-restore-drill-report",
        releaseGateCode: "deployment-safety-controls",
        verification: "Evidence must include the PostgreSQL custom-format dump, sha256 sidecar and successful restore verification report.",
        failurePolicy: "Block release if backup creation, checksum verification or restore verification fails.",
      },
    ],
    visualQaCoverage: {
      artifactName: "visual-smoke-screenshots",
      enforcement: "ci-production-smoke",
      manifestFileName: "visual-smoke-manifest.json",
      expectedScreenshotCount: 28,
      layoutChecks: ["browser-console-errors", "page-horizontal-overflow", "visible-text-overlap"],
      reviewChecks: visualQaReviewChecks(),
      reviewProtocol: visualQaReviewProtocol(),
      themes: ["light", "dark"],
      viewports: [
        { name: "desktop", width: 1440, height: 1000 },
        { name: "mobile", width: 390, height: 844 },
      ],
      routes: visualQaRoutes(),
      routeAudits: visualQaRouteAudits(),
      artifacts: visualQaArtifacts(),
    },
  };
}

function goldenScenarioCodes() {
  return ["clg-charity", "dac-small", "medium-audit-required", "micro-ltd", "small-abridged-ltd"];
}

function goldenScenario({
  code,
  label,
  legalName,
  companyType,
  expectedSizeClass = "Small",
  expectedRegime,
  expectedOutcome,
  verifier,
  corporationTax,
  readinessState,
  signOffPacketState,
  proofArea,
  sourceId,
  sourceTitle,
  sourceUrl,
  manualProfessionalReviewRequired = false,
}: {
  code: string;
  label: string;
  legalName: string;
  companyType: string;
  expectedSizeClass?: string;
  expectedRegime: string;
  expectedOutcome: string;
  verifier: string;
  corporationTax: number;
  readinessState: string;
  signOffPacketState: string;
  proofArea: string;
  sourceId: string;
  sourceTitle: string;
  sourceUrl: string;
  manualProfessionalReviewRequired?: boolean;
}): ProductionReadinessReport["goldenFilingCorpus"][number] {
  const outputArtifacts = expectedOutcome === "manual-handoff"
    ? ["full accounts PDF text", "auditor handoff record"]
    : ["accounts PDF text", "iXBRL XML"];
  const decisionGates = manualProfessionalReviewRequired
    ? ["signed auditor report", "manual handoff acceptance"]
    : ["named qualified-accountant review"];

  return {
    code,
    label,
    companyScope: companyType === "DesignatedActivityCompany" ? "Designated activity company" : "Private company limited by shares",
    expectedOutcome,
    coverageStatus: "covered",
    fixture: {
      legalName,
      companyType,
      periodStart: "2025-01-01",
      periodEnd: "2025-12-31",
      expectedSizeClass,
      expectedRegime,
      auditExempt: !manualProfessionalReviewRequired,
      manualProfessionalReviewRequired,
    },
    evidenceTestNames: [verifier],
    evidenceVerifiers: [
      {
        name: verifier,
        command: `dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~${verifier}`,
        ciScope: "default-ci",
        runsInDefaultCi: true,
        environment: "EF Core InMemory golden fixture; CI also runs the broader backend suite on Linux",
        evidenceLevel: "end-to-end golden filing scenario",
      },
    ],
    assertions: ["PDF text", "iXBRL parse", proofArea],
    evidencePack: {
      outputArtifacts,
      decisionGates,
      expectedValueChecks: [proofArea, "well-formed iXBRL"],
      expectedOutputs: {
        pdfTextMarkers: [legalName],
        ixbrlRequiredTags: ["core:EntityCurrentLegalOrRegisteredName"],
        filingReadinessState: readinessState,
        expectedCorporationTax: corporationTax,
        requiredNotes: ["Accounting Policies"],
        filingGateStates: manualProfessionalReviewRequired
          ? ["signed auditor report required", "manual handoff acceptance required"]
          : ["director and secretary certification required", "qualified-accountant review required"],
        signOffPacketState,
      },
      expectedProofPoints: [
        {
          area: proofArea,
          expectedEvidence: `${label} golden scenario proves ${proofArea} evidence.`,
          automatedVerifier: verifier,
          required: true,
        },
      ],
      sourceReferences: [
        {
          sourceId,
          title: sourceTitle,
          effectiveDate: "2026-07-03",
          url: sourceUrl,
        },
      ],
    },
    legalBasisSnapshot: {
      scenarioCode: code,
      companyType,
      sizeClass: expectedSizeClass ?? expectedRegime,
      electedRegime: expectedRegime,
      auditExempt: !manualProfessionalReviewRequired,
      manualProfessionalReviewRequired,
      legalBasis: `${label} source-backed statutory filing basis.`,
      requiredOutputs: outputArtifacts,
      professionalGates: decisionGates,
      sourceIds: [sourceId],
    },
  };
}

function goldenLedgerEntry({
  scenarioCode,
  label,
  legalName,
  companyType,
  expectedOutcome,
  acceptanceStatus,
  verifier,
  artifacts,
  checks,
  proofArea,
  sourceId,
  corporationTax,
  readinessState,
  signOffPacketState,
}: {
  scenarioCode: string;
  label: string;
  legalName: string;
  companyType: string;
  expectedOutcome: string;
  acceptanceStatus: string;
  verifier: string;
  artifacts: string[];
  checks: string[];
  proofArea: string;
  sourceId: string;
  corporationTax: number;
  readinessState: string;
  signOffPacketState: string;
}): ProductionReadinessReport["goldenEvidenceLedger"][number] {
  return {
    scenarioCode,
    label,
    fixtureLegalName: legalName,
    companyType,
    expectedOutcome,
    coverageStatus: "covered",
    acceptanceStatus,
    requiredSignOffGate: expectedOutcome === "manual-handoff"
      ? "Qualified accountant must record manual handoff acceptance before relying on outputs."
      : "Named qualified accountant must approve the generated pack before real filing use.",
    blocksRelease: true,
    automatedVerifierNames: [verifier],
    automatedVerifierCommands: [`dotnet test backend/Accounts.slnx --filter FullyQualifiedName~${verifier}`],
    ciScopes: ["default-ci"],
    evidenceLevels: ["end-to-end golden filing scenario"],
    outputArtifacts: artifacts,
    decisionGates: expectedOutcome === "manual-handoff" ? ["signed auditor report", "manual handoff acceptance"] : ["named qualified-accountant review"],
    expectedValueChecks: checks,
    proofPointAreas: [proofArea],
    sourceIds: [sourceId],
    expectedCorporationTax: corporationTax,
    filingReadinessState: readinessState,
    signOffPacketState,
  };
}

function journeyAcceptance(
  routeCode: string,
  routeLabel: string,
  routeKey: string,
  workflowStages: string[],
  acceptanceCriteria?: string[],
) {
  return {
    routeCode,
    routeLabel,
    routeKey,
    workflowStages,
    seededScenarioCodes: goldenScenarioCodes(),
    visualArtifactNames: ["light-desktop", "light-mobile", "dark-desktop", "dark-mobile"].map(
      (suffix) => `${routeCode}-${suffix}.png`,
    ),
    requiredEvidence: [
      "named qualified-accountant route acceptance",
      "visual smoke screenshots reviewed",
      "golden corpus evidence accepted",
    ],
    acceptanceCriteria: acceptanceCriteria ?? [
      `${routeLabel} route exposes the relevant accountant workflow state, blockers, next actions and evidence.`,
      `A named qualified accountant accepts the ${routeLabel} route outputs, gates, wording and evidence for every seeded golden scenario.`,
    ],
    signOffGate: "golden-corpus-accountant-acceptance",
    status: "required-review",
  };
}

function accountantWorkflowEvidencePack(): ProductionReadinessReport["accountantWorkflowEvidencePack"] {
  return [
    accountantRouteEvidence("dashboard", "Dashboard", accountantWorkflowStages()),
    accountantRouteEvidence("company-detail", "Company detail", ["Setup"]),
    accountantRouteEvidence("period-workspace", "Period workspace", accountantWorkflowStages()),
    accountantRouteEvidence("financial-statements", "Financial statements", ["Statements"]),
    accountantRouteEvidence(
      "filing-review",
      "Filing review",
      ["Review", "Filing"],
      "Does the filing review route let a qualified accountant accept readiness, source links, generated outputs, signatory gates, external ROS/iXBRL validation, filing state, outputs, gates, wording and evidence?",
    ),
    accountantRouteEvidence(
      "production-readiness",
      "Production readiness",
      ["Review", "Filing"],
      "Does the production readiness route let a qualified accountant accept backend checks, filing rules coverage, unsupported paths, security posture, release blockers, accountant review state, outputs, gates, wording and evidence?",
    ),
  ];
}

function accountantRouteEvidence(
  routeCode: string,
  routeLabel: string,
  workflowStages: string[],
  decisionQuestion?: string,
): ProductionReadinessReport["accountantWorkflowEvidencePack"][number] {
  return {
    routeCode,
    routeLabel,
    workflowStages,
    seededScenarioCodes: goldenScenarioCodes(),
    visualArtifactNames: ["light-desktop", "light-mobile", "dark-desktop", "dark-mobile"].map(
      (suffix) => `${routeCode}-${suffix}.png`,
    ),
    evidenceArtifact: `${routeCode}-accountant-route-acceptance-note`,
    decisionQuestion:
      decisionQuestion ??
      `Does the ${routeLabel} route let a qualified accountant accept the workflow state, blockers, next action, outputs, gates, wording and evidence for every seeded golden scenario?`,
    requiredEvidence: [
      "named qualified-accountant route acceptance",
      "visual smoke screenshots reviewed",
      "golden corpus evidence accepted",
    ],
    signOffGate: "golden-corpus-accountant-acceptance",
    failurePolicy: "Block release until a named qualified accountant accepts this route's outputs, gates, wording and evidence against the seeded golden corpus and reviewed visual artifacts.",
  };
}

function accountantWalkthroughEvidenceMatrix(): ProductionReadinessReport["accountantWalkthroughEvidenceMatrix"] {
  return [
    {
      scenarioCode: "micro-ltd",
      scenarioLabel: "Micro LTD",
      expectedOutcome: "generated-pack",
      filingReadinessState: "100% filing readiness",
      signOffPacketState: "review-required",
      manualProfessionalReviewRequired: false,
      routeCode: "dashboard",
      routeLabel: "Dashboard",
      routeKey: "dashboard",
      workflowStages: accountantWorkflowStages(),
      visualArtifactNames: ["light-desktop", "light-mobile", "dark-desktop", "dark-mobile"].map(
        (suffix) => `dashboard-${suffix}.png`,
      ),
      evidenceArtifact: "micro-ltd-dashboard-walkthrough-note",
      decisionQuestion: "Does the Dashboard route let a qualified accountant accept the workflow state, blockers, next action, outputs, gates, wording and evidence for every seeded golden scenario?",
      requiredEvidence: [
        "named qualified-accountant route acceptance",
        "visual smoke screenshots reviewed",
        "golden corpus evidence accepted",
        "Named qualified-accountant approval recorded against the generated pack.",
      ],
      acceptanceCriteria: [
        "Dashboard route exposes the relevant accountant workflow state, blockers, next actions and evidence.",
        "Micro LTD: qualified-accountant review covers PDF wording and micro statutory statement.",
      ],
      releaseChecklistCode: "golden-corpus-accountant-acceptance",
      signOffGate: "golden-corpus-accountant-acceptance",
      status: "required-review",
      blocksRelease: true,
    },
  ];
}

function workbenchVisualAcceptanceRegister(): ProductionReadinessReport["workbenchVisualAcceptanceRegister"] {
  return [
    workbenchVisualAcceptance("dashboard", "Dashboard", accountantWorkflowStages()),
    workbenchVisualAcceptance("company-detail", "Company detail", ["Setup"]),
    workbenchVisualAcceptance("period-workspace", "Period workspace", accountantWorkflowStages()),
    workbenchVisualAcceptance("financial-statements", "Financial statements", ["Statements"]),
    workbenchVisualAcceptance(
      "filing-review",
      "Filing review",
      ["Review", "Filing"],
      "Accept the filing review screen only after its evidence checklist, source links, generated outputs and filing-state actions are visually clear in light/dark desktop/mobile screenshots.",
    ),
    workbenchVisualAcceptance(
      "production-readiness",
      "Production readiness",
      ["Review", "Filing"],
      "Accept the production readiness screen only after release blockers, rule coverage, visual QA, operational readiness and accountant review state are visually clear in light/dark desktop/mobile screenshots.",
    ),
    workbenchVisualAcceptance("workbench-preview", "Workbench preview", accountantWorkflowStages()),
  ];
}

function workbenchVisualAcceptance(
  routeCode: string,
  routeLabel: string,
  workflowStages: string[],
  nextAction?: string,
): ProductionReadinessReport["workbenchVisualAcceptanceRegister"][number] {
  return {
    routeCode,
    routeLabel,
    workflowStages,
    acceptanceAreas: [
      "accountant-workflow-hierarchy",
      "table-scanability",
      "theme-contrast",
      "mobile-density",
      "loading-error-empty-states",
    ],
    screenshotArtifactNames: ["light-desktop", "light-mobile", "dark-desktop", "dark-mobile"].map(
      (suffix) => `${routeCode}-${suffix}.png`,
    ),
    evidenceArtifact: `${routeCode}-visual-acceptance-note`,
    requiredEvidence: [
      "route-state acceptance note",
      "light/dark desktop/mobile screenshot review",
      "named visual QA reviewer sign-off",
    ],
    releaseGateCode: "visual-qa-screenshot-review",
    status: "required-review",
    failurePolicy: "Block release until this accountant workbench route is visually accepted across workflow hierarchy, table scanability, theme contrast, mobile density and route states.",
    nextAction: nextAction ?? `Accept the ${routeLabel} route only after its workflow hierarchy, tables, contrast, mobile layout, loading/error/empty states and screenshots are professionally reviewed.`,
  };
}

function accountantWorkflowStages() {
  return ["Setup", "Import", "Classify", "Year-End", "Statements", "Notes", "Review", "Filing"];
}

function visualQaReviewChecks() {
  return ["accountant-workflow-hierarchy", "table-scanability", "theme-contrast", "mobile-density", "loading-error-empty-states"];
}

function visualQaReviewProtocol(): ProductionReadinessReport["visualQaCoverage"]["reviewProtocol"] {
  return {
    protocolVersion: "visual-review-v1",
    reviewerRole: "Design reviewer",
    status: "required-review",
    signOffGate: "visual-qa-screenshot-review",
    failurePolicy: "Block release if any accountant workbench route has console errors, horizontal overflow, visible text overlap, inaccessible contrast, unreadable table density, or unresolved light/dark/mobile defects.",
    acceptanceCriteria: [
      "Every configured route is captured in light desktop, dark desktop, light mobile and dark mobile.",
      "No browser console errors, horizontal overflow or visible text overlap are present.",
      "Accountant workflow hierarchy, table scanability, theme contrast, mobile density and route states are professionally acceptable.",
      "A named visual QA reviewer records screenshot-manifest acceptance before real filing release.",
    ],
    requiredEvidence: [
      "visual-smoke-manifest.json",
      "visual-smoke-evidence-report.json",
      "accountant-workbench-evidence-report.json",
      "28 visual smoke screenshots",
      "screenshot SHA-256 checksums",
      "screenshot PNG dimensions",
      "screenshot nonblank pixel diversity evidence",
      "per-screenshot automated theme contrast smoke evidence",
      "route audit summary",
      "named visual QA reviewer sign-off",
    ],
  };
}

function visualQaRoutes(): ProductionReadinessReport["visualQaCoverage"]["routes"] {
  return [
    {
      code: "dashboard",
      routeKey: "dashboard",
      label: "Dashboard",
      description: "Accountant queue and production readiness overview.",
      requiredText: "Production Readiness",
      workflowStages: accountantWorkflowStages(),
      openFilingTab: false,
    },
    {
      code: "production-readiness",
      routeKey: "readiness",
      label: "Production readiness",
      description: "Assurance checklist, statutory rules matrix, source snapshot and operational gates.",
      requiredText: "Production Readiness Checklist",
      workflowStages: ["Review", "Filing"],
      openFilingTab: false,
    },
    {
      code: "company-detail",
      routeKey: "company",
      label: "Company detail",
      description: "Company command centre, statutory profile, officers, charity facts and accounting periods.",
      requiredText: "Company command centre",
      workflowStages: ["Setup"],
      openFilingTab: false,
    },
    {
      code: "period-workspace",
      routeKey: "period",
      label: "Period workspace",
      description: "Import, classification, year-end, statements and filing readiness overview.",
      requiredText: "Filing readiness",
      workflowStages: accountantWorkflowStages(),
      openFilingTab: false,
    },
    {
      code: "filing-review",
      routeKey: "filing",
      label: "Filing review",
      description: "Period workspace filing tab.",
      requiredText: "Filing readiness profile",
      workflowStages: ["Review", "Filing"],
      openFilingTab: true,
    },
    {
      code: "financial-statements",
      routeKey: "financialStatements",
      label: "Financial statements",
      description: "Statement preview, tax computation, source trail and directors' report workbench.",
      requiredText: "Financial Statements",
      workflowStages: ["Statements"],
      openFilingTab: false,
    },
    {
      code: "workbench-preview",
      routeKey: "workbenchPreview",
      label: "Workbench preview",
      description: "Internal component preview for accountant workflow primitives and route states.",
      requiredText: "Workbench Component Preview",
      workflowStages: accountantWorkflowStages(),
      openFilingTab: false,
    },
  ];
}

function visualQaArtifacts(): ProductionReadinessReport["visualQaCoverage"]["artifacts"] {
  const layoutChecks = ["browser-console-errors", "page-horizontal-overflow", "visible-text-overlap"];
  return ["light", "dark"].flatMap((theme) =>
    ["desktop", "mobile"].flatMap((viewportName) =>
      visualQaRoutes().map((route) => {
        const fileName = `${route.code}-${theme}-${viewportName}.png`;
        return {
          routeCode: route.code,
          routeKey: route.routeKey,
          theme,
          viewportName,
          fileName,
          artifactPath: `artifacts/visual-smoke/${fileName}`,
          requiredText: route.requiredText,
          openFilingTab: route.openFilingTab,
          reviewStatus: "required-review",
          layoutChecks,
        };
      }),
    ),
  );
}

function visualQaRouteAudits(): ProductionReadinessReport["visualQaCoverage"]["routeAudits"] {
  return visualQaRoutes().map((route) => ({
    routeCode: route.code,
    routeKey: route.routeKey,
    label: route.label,
    workflowStages: route.workflowStages,
    screenshotCount: 4,
    reviewStatus: "required-review",
    reviewChecks: visualQaReviewChecks(),
  }));
}
