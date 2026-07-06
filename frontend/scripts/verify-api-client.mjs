// BL-05 / BL-25: verifies the year-end API client functions (loans, director loans, share
// capital, and the new update/PUT helpers) issue the correct HTTP method and URL. Mocks the
// global fetch so no server is needed.
import assert from "node:assert/strict";
import {
  // loans
  getLoans,
  createLoan,
  updateLoan,
  deleteLoan,
  // loan snapshots
  getLoanSnapshots,
  createLoanSnapshot,
  updateLoanSnapshot,
  deleteLoanSnapshot,
  // director loans
  getDirectorLoans,
  createDirectorLoan,
  updateDirectorLoan,
  deleteDirectorLoan,
  // share capital
  getShareCapital,
  createShareCapital,
  updateShareCapital,
  deleteShareCapital,
  // year-end row updates (BL-25)
  updateDebtor,
  updateCreditor,
  updateFixedAsset,
  updateInventory,
  updateDividend,
  getProductionReadinessReport,
} from "../src/lib/api.ts";
import {
  ACCOUNTANT_WORKFLOW_STAGES,
  expectedVisualSmokeArtifacts,
  expectedVisualSmokeRouteAudits,
  expectedVisualSmokeScreenshotCount,
  visualSmokeLayoutChecks,
  visualSmokeReviewProtocol,
  visualSmokeReviewChecks,
  visualSmokeRoutes,
  visualSmokeThemes,
  visualSmokeViewports,
} from "./visual-smoke-plan.mjs";

const calls = [];
globalThis.fetch = async (url, init) => {
  const requestUrl = String(url);
  calls.push({ url: requestUrl, method: (init?.method ?? "GET").toUpperCase() });
  const body = requestUrl === "/api/system/production-readiness"
    ? productionReadinessReportFixture()
    : { ok: true };

  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { "Content-Type": "application/json" },
  });
};

async function expect(fn, method, url) {
  calls.length = 0;
  await fn();
  assert.equal(calls.length, 1, `expected exactly one fetch for ${method} ${url}`);
  assert.equal(calls[0].method, method, `method for ${url}`);
  assert.equal(calls[0].url, url, `url for ${method}`);
}

const loan = {
  lender: "Bank",
  originalAmount: 1000,
  balance: 1000,
  interestRate: 5,
  isDirectorLoan: false,
  dueWithinYear: 100,
  dueAfterYear: 900,
};
const snapshot = {
  loanId: 7,
  openingBalance: 0,
  drawdowns: 1000,
  repayments: 0,
  closingBalance: 1000,
  dueWithinYear: 100,
  dueAfterYear: 900,
};
const dirLoan = {
  directorId: 3,
  openingBalance: 0,
  advances: 500,
  repayments: 0,
  closingBalance: 500,
  interestRate: 5,
  interestCharged: 0,
  isDocumented: true,
  maxBalanceDuringYear: 500,
};
const share = {
  shareClass: "Ordinary",
  nominalValue: 1,
  numberIssued: 100,
  totalValue: 100,
  isFullyPaid: true,
};

function productionReadinessReportFixture() {
  return withGoldenLegalBasisSnapshots({
    generatedAt: "2026-07-04T12:00:00Z",
    overallStatus: "review-required",
    companiesInDatabase: 1,
    periodsInDatabase: 1,
    sourceLawSnapshot: {
      snapshotDate: "2026-07-03",
      snapshotVersion: "irish-statutory-accounts-sources-2026-07-03",
      contentHash: "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
      sourceCount: 6,
      sources: [
        source("frc-frs-105", "FRC FRS 105 current edition and amendments"),
        source("frc-frs-102", "FRC FRS 102 current edition and amendments"),
        source("cro-guarantee-company", "CRO guarantee company financial statements requirements"),
        source("charities-regulator-annual-report", "Charities Regulator annual report guidance"),
        source("cro-medium-company", "CRO medium company financial statements requirements"),
        source("cro-auditors-report", "CRO auditors report requirements"),
      ],
    },
    sourceLawTraceability: [
      {
        ...source("frc-frs-105", "FRC FRS 105 current edition and amendments"),
        inSnapshot: true,
        usedBy: [
          "golden-corpus:micro-ltd",
          "golden-corpus:dac-small",
          "statutory-rule-matrix:ltd-micro",
          "statutory-rules-coverage:size-classification-thresholds",
          "accountant-acceptance:micro-ltd",
          "accountant-acceptance:dac-small",
        ],
        releaseGateCodes: ["qualified-accountant-review"],
      },
      {
        ...source("frc-frs-102", "FRC FRS 102 current edition and amendments"),
        inSnapshot: true,
        usedBy: [
          "golden-corpus:dac-small",
          "golden-corpus:small-abridged-ltd",
          "golden-corpus:clg-charity",
          "golden-corpus:medium-audit-required",
          "accountant-acceptance:dac-small",
          "accountant-acceptance:small-abridged-ltd",
          "accountant-acceptance:clg-charity",
          "accountant-acceptance:medium-audit-required",
        ],
        releaseGateCodes: ["qualified-accountant-review"],
      },
      {
        ...source("cro-guarantee-company", "CRO guarantee company financial statements requirements"),
        inSnapshot: true,
        usedBy: [
          "golden-corpus:clg-charity",
          "statutory-rule-matrix:clg-charity",
          "accountant-acceptance:clg-charity",
        ],
        releaseGateCodes: ["qualified-accountant-review", "charity-annual-return-review"],
      },
      {
        ...source("charities-regulator-annual-report", "Charities Regulator annual report guidance"),
        inSnapshot: true,
        usedBy: [
          "golden-corpus:clg-charity",
          "statutory-rule-matrix:clg-charity",
          "accountant-acceptance:clg-charity",
        ],
        releaseGateCodes: ["charity-annual-return-review"],
      },
      {
        ...source("cro-medium-company", "CRO medium company financial statements requirements"),
        inSnapshot: true,
        usedBy: [
          "golden-corpus:medium-audit-required",
          "statutory-rule-matrix:medium-audit-required",
          "accountant-acceptance:medium-audit-required",
        ],
        releaseGateCodes: ["auditor-handoff", "manual-professional-handoff"],
      },
      {
        ...source("cro-auditors-report", "CRO auditors report requirements"),
        inSnapshot: true,
        usedBy: [
          "golden-corpus:medium-audit-required",
          "statutory-rule-matrix:medium-audit-required",
          "accountant-acceptance:medium-audit-required",
        ],
        releaseGateCodes: ["auditor-handoff"],
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
      monitoredSourceIds: ["frc-frs-105", "frc-frs-102", "cro-guarantee-company", "charities-regulator-annual-report", "cro-medium-company", "cro-auditors-report"],
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
      sourceLawReviewEntry("frc-frs-105", "FRC FRS 105 current edition and amendments"),
      sourceLawReviewEntry("frc-frs-102", "FRC FRS 102 current edition and amendments"),
      sourceLawReviewEntry("cro-guarantee-company", "CRO guarantee company financial statements requirements"),
      sourceLawReviewEntry("charities-regulator-annual-report", "Charities Regulator annual report guidance"),
      sourceLawReviewEntry("cro-medium-company", "CRO medium company financial statements requirements"),
      sourceLawReviewEntry("cro-auditors-report", "CRO auditors report requirements"),
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
        sourceIds: ["frc-frs-102"],
        releaseGateCodes: ["external-ros-validation", "ixbrl-taxonomy-selection", "source-law-change-review"],
      },
      {
        taxonomyKey: "irish-extension-2023-frs-102",
        accountingStandard: "FRS 102",
        taxonomyDate: "2023-01-01",
        label: "Irish Extension 2023 FRS 102 taxonomy accepted by Revenue",
        schemaRef: "https://xbrl.frc.org.uk/ireland/FRS-102/2023-01-01/ie-FRS-102-2023-01-01.xsd",
        acceptedByRevenue: true,
        automatedPlatformSelectionSupported: true,
        effectiveForPeriodsStartingOnOrAfter: "2023-01-01",
        effectiveForPeriodsStartingBefore: "2024-01-01",
        sourceIds: ["frc-frs-102"],
        releaseGateCodes: ["external-ros-validation", "ixbrl-taxonomy-selection", "source-law-change-review"],
      },
      {
        taxonomyKey: "irish-extension-2025-frs-101",
        accountingStandard: "FRS 101",
        taxonomyDate: "2025-01-01",
        label: "Irish Extension 2025 FRS 101 taxonomy accepted by Revenue",
        schemaRef: "https://xbrl.frc.org.uk/ireland/FRS-101/2025-01-01/ie-FRS-101-2025-01-01.xsd",
        acceptedByRevenue: true,
        automatedPlatformSelectionSupported: false,
        effectiveForPeriodsStartingOnOrAfter: "2024-01-01",
        effectiveForPeriodsStartingBefore: "",
        sourceIds: ["frc-frs-102"],
        releaseGateCodes: ["external-ros-validation", "ixbrl-taxonomy-selection", "source-law-change-review", "manual-professional-handoff"],
      },
      {
        taxonomyKey: "irish-extension-2025-ifrs",
        accountingStandard: "EU IFRS",
        taxonomyDate: "2025-01-01",
        label: "EU IFRS Irish Extension 2025 taxonomy accepted by Revenue",
        schemaRef: "https://xbrl.frc.org.uk/ireland/IFRS/2025-01-01/ie-IFRS-2025-01-01.xsd",
        acceptedByRevenue: true,
        automatedPlatformSelectionSupported: false,
        effectiveForPeriodsStartingOnOrAfter: "2024-01-01",
        effectiveForPeriodsStartingBefore: "",
        sourceIds: ["frc-frs-102"],
        releaseGateCodes: ["external-ros-validation", "ixbrl-taxonomy-selection", "source-law-change-review", "manual-professional-handoff"],
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
      visualQaExpectedScreenshots: expectedVisualSmokeScreenshotCount(),
      requiredOperationalGates: 1,
      openCriticalActions: 1,
      evidenceItems: ["source-law-snapshot-fingerprint", "source-law-traceability-index", "source-law-maintenance-protocol", "source-law-review-ledger", "revenue-taxonomy-range-evidence", "golden-filing-corpus", "golden-evidence-ledger", "golden-verifier-manifest", "audit-evidence-timeline", "visual-smoke-screenshots", "release-blocker-register", "release-review-checklist", "release-verification-manifest", "accountant-acceptance-summary", "accountant-workflow-walkthrough-protocol", "accountant-journey-acceptance-checklist", "production-completion-map"],
      releaseBlockers: [
        "Qualified accountant sign-off required",
        "Source-law change review required",
        "Light/dark visual regression required",
      ],
    },
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
        sources: [source("frc-frs-105", "FRC FRS 105 current edition and amendments")],
      },
      {
        scenarioCode: "small-abridged-ltd",
        label: "Small abridged LTD accountant acceptance",
        required: true,
        acceptanceStatus: "qualified-accountant-review-required",
        reviewScope: ["Full accounts PDF", "Abridged CRO pack", "Section 352 evidence", "iXBRL XML", "tax computation", "notes", "signatory gates"],
        requiredEvidence: ["Named qualified-accountant approval recorded against full and abridged generated packs."],
        requiredSignOffGate: "Named qualified accountant must approve both the full accounts pack and abridged CRO pack before real filing use.",
        evidenceVerifiers: [
          {
            name: "AccountsWorkflowTests.GoldenPath_SmallAuditExemptCompany_MixedAccrualSetBalancesThroughPdfAndIxbrl",
            command: "dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~AccountsWorkflowTests.GoldenPath_SmallAuditExemptCompany_MixedAccrualSetBalancesThroughPdfAndIxbrl",
            ciScope: "default-ci",
            runsInDefaultCi: true,
            environment: "EF Core InMemory golden fixture; CI also runs the broader backend suite on Linux",
            evidenceLevel: "end-to-end golden filing scenario",
          },
          {
            name: "FilingGoldenCorpusScenarioTests.GoldenCorpus_SmallAbridgedLtd_EmitsFullAccountsAbridgedCroPackIxbrlAndReadiness",
            command: "dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~FilingGoldenCorpusScenarioTests.GoldenCorpus_SmallAbridgedLtd_EmitsFullAccountsAbridgedCroPackIxbrlAndReadiness",
            ciScope: "default-ci",
            runsInDefaultCi: true,
            environment: "EF Core InMemory golden fixture; CI also runs the broader backend suite on Linux",
            evidenceLevel: "end-to-end golden filing scenario",
          },
        ],
        sources: [source("frc-frs-102", "FRC FRS 102 current edition and amendments")],
      },
      {
        scenarioCode: "dac-small",
        label: "Small DAC accountant acceptance",
        required: true,
        acceptanceStatus: "qualified-accountant-review-required",
        reviewScope: ["DAC accounts PDF", "iXBRL XML", "filing readiness profile"],
        requiredEvidence: ["Named qualified-accountant approval recorded against the DAC pack."],
        requiredSignOffGate: "Named qualified accountant must approve the DAC generated pack before real filing use.",
        evidenceVerifiers: [
          {
            name: "FilingGoldenCorpusScenarioTests.GoldenCorpus_DacSmall_EmitsAccountsIxbrlAndSourceBackedReadiness",
            command: "dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~FilingGoldenCorpusScenarioTests.GoldenCorpus_DacSmall_EmitsAccountsIxbrlAndSourceBackedReadiness",
            ciScope: "default-ci",
            runsInDefaultCi: true,
            environment: "EF Core InMemory golden fixture; CI also runs the broader backend suite on Linux",
            evidenceLevel: "end-to-end golden filing scenario",
          },
        ],
        sources: [source("frc-frs-102", "FRC FRS 102 current edition and amendments")],
      },
      {
        scenarioCode: "clg-charity",
        label: "CLG charity accountant acceptance",
        required: true,
        acceptanceStatus: "qualified-accountant-review-required",
        reviewScope: ["CLG accounts PDF", "charity number", "SoFA and trustees annual report evidence", "iXBRL XML", "tax computation", "charity notes", "charity annual return review gates"],
        requiredEvidence: [
          "Named qualified-accountant approval recorded against the CLG charity pack.",
          "Charity annual report evidence reviewed before charity filing state advances.",
        ],
        requiredSignOffGate: "Named qualified accountant must approve the CLG charity pack and charity evidence before real filing use.",
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
        sources: [
          source("cro-guarantee-company", "CRO guarantee company financial statements requirements"),
          source("charities-regulator-annual-report", "Charities Regulator annual report guidance"),
          source("frc-frs-102", "FRC FRS 102 current edition and amendments"),
        ],
      },
      {
        scenarioCode: "medium-audit-required",
        label: "Medium audit-required accountant acceptance",
        required: true,
        acceptanceStatus: "qualified-accountant-review-required",
        reviewScope: ["audit report blocker", "manual handoff state", "full accounts PDF", "iXBRL XML", "tax computation", "notes", "auditor evidence"],
        requiredEvidence: [
          "Signed auditor report and manual handoff note reviewed by the qualified accountant.",
          "Normal filing approval remains blocked until auditor evidence is present.",
        ],
        requiredSignOffGate: "Qualified accountant must record manual handoff acceptance before relying on outputs.",
        evidenceVerifiers: [
          {
            name: "FilingGoldenCorpusScenarioTests.GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence",
            command: "dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~FilingGoldenCorpusScenarioTests.GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence",
            ciScope: "default-ci",
            runsInDefaultCi: true,
            environment: "EF Core InMemory golden fixture; CI also runs the broader backend suite on Linux",
            evidenceLevel: "end-to-end golden filing scenario",
          },
        ],
        sources: [
          source("cro-medium-company", "CRO medium company financial statements requirements"),
          source("cro-auditors-report", "CRO auditors report requirements"),
          source("frc-frs-102", "FRC FRS 102 current edition and amendments"),
        ],
      },
    ],
    accountantAcceptanceSummary: {
      scenarioCount: 5,
      automatedVerifierCount: 6,
      professionalSignOffRequiredCount: 5,
      manualHandoffScenarioCount: 1,
      releaseBlockingScenarioCodes: ["clg-charity", "dac-small", "medium-audit-required", "micro-ltd", "small-abridged-ltd"],
      requiredSignOffGates: [
        "Named qualified accountant must approve the CLG charity pack and charity evidence before real filing use.",
        "Named qualified accountant must approve the DAC generated pack before real filing use.",
        "Named qualified accountant must approve both the full accounts pack and abridged CRO pack before real filing use.",
        "Named qualified accountant must approve the generated pack before real filing use.",
        "Qualified accountant must record manual handoff acceptance before relying on outputs.",
      ],
      status: "qualified-accountant-review-required",
    },
    accountantWorkflowWalkthroughProtocol: {
      protocolVersion: "accountant-workflow-walkthrough-v1",
      reviewerRole: "Qualified accountant",
      status: "required-review",
      signOffGate: "accountant-final-signoff",
      failurePolicy: "Block release if a named qualified accountant has not walked the seeded golden corpus through the live accountant workflow and accepted the outputs, gates, wording and evidence.",
      seededScenarioCodes: ["clg-charity", "dac-small", "medium-audit-required", "micro-ltd", "small-abridged-ltd"],
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
      journeyAcceptance("dashboard", "Dashboard", "dashboard", ACCOUNTANT_WORKFLOW_STAGES),
      journeyAcceptance("company-detail", "Company detail", "company", ["Setup"]),
      journeyAcceptance("period-workspace", "Period workspace", "period", ACCOUNTANT_WORKFLOW_STAGES),
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
          outputArtifacts: ["accounts PDF text"],
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
          sourceReferences: [source("frc-frs-105", "FRC FRS 105 current edition and amendments")],
        },
      },
      {
        code: "dac-small",
        label: "Small DAC",
        companyScope: "Designated activity company",
        expectedOutcome: "generated-pack",
        coverageStatus: "covered",
        fixture: {
          legalName: "Atlantic Manufacturing DAC",
          companyType: "DesignatedActivityCompany",
          periodStart: "2026-01-01",
          periodEnd: "2026-12-31",
          expectedSizeClass: "Small",
          expectedRegime: "Small",
          auditExempt: true,
          manualProfessionalReviewRequired: false,
        },
        evidenceTestNames: ["FilingGoldenCorpusScenarioTests.GoldenCorpus_DacSmall_EmitsAccountsIxbrlAndSourceBackedReadiness"],
        evidenceVerifiers: [
          {
            name: "FilingGoldenCorpusScenarioTests.GoldenCorpus_DacSmall_EmitsAccountsIxbrlAndSourceBackedReadiness",
            command: "dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~FilingGoldenCorpusScenarioTests.GoldenCorpus_DacSmall_EmitsAccountsIxbrlAndSourceBackedReadiness",
            ciScope: "default-ci",
            runsInDefaultCi: true,
            environment: "EF Core InMemory golden fixture; CI also runs the broader backend suite on Linux",
            evidenceLevel: "end-to-end golden filing scenario",
          },
        ],
        assertions: ["PDF text", "iXBRL parse", "source-backed readiness"],
        evidencePack: {
          outputArtifacts: ["DAC accounts PDF text"],
          decisionGates: ["named qualified-accountant review"],
          expectedValueChecks: ["well-formed iXBRL"],
          expectedOutputs: {
            pdfTextMarkers: ["Atlantic Manufacturing DAC", "DIRECTORS' REPORT"],
            ixbrlRequiredTags: ["bus:EntityCurrentLegalOrRegisteredName"],
            filingReadinessState: "ready-for-external-filing",
            expectedCorporationTax: 62.5,
            requiredNotes: ["Accounting Policies"],
            filingGateStates: ["director and secretary certification satisfied", "qualified-accountant review recorded"],
            signOffPacketState: "ready-for-external-filing",
          },
          expectedProofPoints: [
            {
              area: "pdf-text",
              expectedEvidence: "DAC accounts PDF text contains the legal name and directors' report.",
              automatedVerifier: "FilingGoldenCorpusScenarioTests.GoldenCorpus_DacSmall_EmitsAccountsIxbrlAndSourceBackedReadiness",
              required: true,
            },
          ],
          sourceReferences: [source("frc-frs-102", "FRC FRS 102 current edition and amendments")],
        },
      },
      {
        code: "small-abridged-ltd",
        label: "Small abridged LTD",
        companyScope: "Private company limited by shares",
        expectedOutcome: "generated-pack",
        coverageStatus: "covered",
        fixture: {
          legalName: "Connacht Digital Solutions Limited",
          companyType: "Private",
          periodStart: "2025-01-01",
          periodEnd: "2025-12-31",
          expectedSizeClass: "Small",
          expectedRegime: "SmallAbridged",
          auditExempt: true,
          manualProfessionalReviewRequired: false,
        },
        evidenceTestNames: [
          "AccountsWorkflowTests.GoldenPath_SmallAuditExemptCompany_MixedAccrualSetBalancesThroughPdfAndIxbrl",
          "FilingGoldenCorpusScenarioTests.GoldenCorpus_SmallAbridgedLtd_EmitsFullAccountsAbridgedCroPackIxbrlAndReadiness",
        ],
        evidenceVerifiers: [
          {
            name: "AccountsWorkflowTests.GoldenPath_SmallAuditExemptCompany_MixedAccrualSetBalancesThroughPdfAndIxbrl",
            command: "dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~AccountsWorkflowTests.GoldenPath_SmallAuditExemptCompany_MixedAccrualSetBalancesThroughPdfAndIxbrl",
            ciScope: "default-ci",
            runsInDefaultCi: true,
            environment: "EF Core InMemory golden fixture; CI also runs the broader backend suite on Linux",
            evidenceLevel: "end-to-end golden filing scenario",
          },
          {
            name: "FilingGoldenCorpusScenarioTests.GoldenCorpus_SmallAbridgedLtd_EmitsFullAccountsAbridgedCroPackIxbrlAndReadiness",
            command: "dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~FilingGoldenCorpusScenarioTests.GoldenCorpus_SmallAbridgedLtd_EmitsFullAccountsAbridgedCroPackIxbrlAndReadiness",
            ciScope: "default-ci",
            runsInDefaultCi: true,
            environment: "EF Core InMemory golden fixture; CI also runs the broader backend suite on Linux",
            evidenceLevel: "end-to-end golden filing scenario",
          },
        ],
        assertions: ["full accounts PDF", "abridged CRO pack", "Section 352 evidence", "iXBRL parse", "source-backed readiness"],
        evidencePack: {
          outputArtifacts: ["full accounts PDF text", "abridged CRO filing pack", "CRO signature page", "iXBRL XML", "tax computation", "notes disclosure set", "filing readiness profile"],
          decisionGates: ["abridgement eligibility", "director and secretary certification", "named qualified-accountant review", "external ROS/iXBRL validation"],
          expectedValueChecks: ["SmallAbridged regime", "Section 352 wording", "public P&L turnover omitted from iXBRL"],
          expectedOutputs: {
            pdfTextMarkers: ["Connacht Digital Solutions Limited", "Section 352", "PROFIT AND LOSS ACCOUNT"],
            ixbrlRequiredTags: ["core:EntityCurrentLegalOrRegisteredName"],
            filingReadinessState: "ready-for-external-filing",
            expectedCorporationTax: 62.5,
            requiredNotes: ["Accounting Policies"],
            filingGateStates: ["abridgement eligibility confirmed", "director and secretary certification satisfied", "qualified-accountant review recorded"],
            signOffPacketState: "ready-for-external-filing",
          },
          expectedProofPoints: [
            {
              area: "pdf-text",
              expectedEvidence: "Full accounts PDF and abridged CRO filing pack contain the required Section 352 evidence.",
              automatedVerifier: "FilingGoldenCorpusScenarioTests.GoldenCorpus_SmallAbridgedLtd_EmitsFullAccountsAbridgedCroPackIxbrlAndReadiness",
              required: true,
            },
            {
              area: "ixbrl-xml",
              expectedEvidence: "iXBRL XML is well-formed and omits public profit-and-loss turnover for the abridged pack.",
              automatedVerifier: "FilingGoldenCorpusScenarioTests.GoldenCorpus_SmallAbridgedLtd_EmitsFullAccountsAbridgedCroPackIxbrlAndReadiness",
              required: true,
            },
          ],
          sourceReferences: [source("frc-frs-102", "FRC FRS 102 current edition and amendments")],
        },
      },
      {
        code: "clg-charity",
        label: "CLG charity annual reporting",
        companyScope: "Company limited by guarantee with charity evidence",
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
        evidenceTestNames: [
          "FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness",
        ],
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
        assertions: ["CLG support", "charity number evidence", "SoFA evidence", "trustees report evidence", "source-backed readiness"],
        evidencePack: {
          outputArtifacts: ["CLG accounts PDF text", "charity readiness profile", "SoFA evidence", "trustees annual report evidence", "iXBRL XML", "tax computation", "notes disclosure set", "accountant sign-off packet"],
          decisionGates: ["charity number", "charity annual return review", "named qualified-accountant review", "accountant sign-off packet state"],
          expectedValueChecks: ["charity evidence satisfied", "Charities Regulator source attached", "CLG source attached", "well-formed iXBRL"],
          expectedOutputs: {
            pdfTextMarkers: ["Dublin Community Support CLG", "Community support and education."],
            ixbrlRequiredTags: ["core:EntityCurrentLegalOrRegisteredName"],
            filingReadinessState: "ready-for-external-filing",
            expectedCorporationTax: 62.5,
            requiredNotes: ["Accounting Policies", "Charity reporting disclosures"],
            filingGateStates: ["charity number satisfied", "SoFA and trustees annual report evidence satisfied", "qualified-accountant review recorded"],
            signOffPacketState: "ready-for-external-filing",
          },
          expectedProofPoints: [
            {
              area: "pdf-text",
              expectedEvidence: "CLG accounts PDF text contains company name and charity objectives.",
              automatedVerifier: "FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness",
              required: true,
            },
            {
              area: "filing-readiness",
              expectedEvidence: "Filing readiness confirms charity number, SoFA and trustees report evidence.",
              automatedVerifier: "FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness",
              required: true,
            },
          ],
          sourceReferences: [
            source("cro-guarantee-company", "CRO guarantee company financial statements requirements"),
            source("charities-regulator-annual-report", "Charities Regulator annual report guidance"),
            source("frc-frs-102", "FRC FRS 102 current edition and amendments"),
          ],
        },
      },
      {
        code: "medium-audit-required",
        label: "Medium audit-required handoff",
        companyScope: "Medium company or non-audit-exempt filing",
        expectedOutcome: "manual-handoff",
        coverageStatus: "covered",
        fixture: {
          legalName: "Midlands Manufacturing Limited",
          companyType: "Private",
          periodStart: "2026-01-01",
          periodEnd: "2026-12-31",
          expectedSizeClass: "Medium",
          expectedRegime: "Medium",
          auditExempt: false,
          manualProfessionalReviewRequired: true,
        },
        evidenceTestNames: [
          "FilingGoldenCorpusScenarioTests.GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence",
        ],
        evidenceVerifiers: [
          {
            name: "FilingGoldenCorpusScenarioTests.GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence",
            command: "dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~FilingGoldenCorpusScenarioTests.GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence",
            ciScope: "default-ci",
            runsInDefaultCi: true,
            environment: "EF Core InMemory golden fixture; CI also runs the broader backend suite on Linux",
            evidenceLevel: "end-to-end golden filing scenario",
          },
        ],
        assertions: ["audit report blocker", "manual handoff", "auditor evidence", "full accounts PDF", "medium iXBRL facts"],
        evidencePack: {
          outputArtifacts: ["full accounts PDF text", "auditor report evidence", "cash flow statement", "statement of changes in equity", "iXBRL XML", "filing readiness profile", "tax computation", "accountant sign-off packet"],
          decisionGates: ["auditor handoff", "manual professional review", "normal CRO approval blocked until auditor evidence", "accountant sign-off packet state"],
          expectedValueChecks: ["Medium regime selected", "audit report blocker present before auditor evidence", "tagged P&L facts present after auditor evidence", "auditor reference appears in PDF text"],
          expectedOutputs: {
            pdfTextMarkers: ["Midlands Manufacturing Limited", "INDEPENDENT AUDITOR'S REPORT", "AUD-2026-MIDLANDS-001", "CASH FLOW STATEMENT", "STATEMENT OF CHANGES IN EQUITY"],
            ixbrlRequiredTags: ["core:TurnoverGrossRevenue", "core:ProfitLossOnOrdinaryActivitiesBeforeTax"],
            filingReadinessState: "manual-handoff-until-auditor-evidence",
            expectedCorporationTax: 62.5,
            requiredNotes: ["Turnover", "Tax on Profit on Ordinary Activities"],
            filingGateStates: ["auditor handoff blocked until signed auditor report", "normal CRO approval blocked until auditor evidence", "manual professional review required"],
            signOffPacketState: "manual-handoff",
          },
          expectedProofPoints: [
            {
              area: "auditor-handoff",
              expectedEvidence: "Signed auditor report reference is mandatory before final output generation.",
              automatedVerifier: "FilingGoldenCorpusScenarioTests.GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence",
              required: true,
            },
            {
              area: "filing-readiness",
              expectedEvidence: "Filing readiness blocks approval before signed auditor report evidence and clears that blocker when evidence is recorded.",
              automatedVerifier: "FilingGoldenCorpusScenarioTests.GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence",
              required: true,
            },
          ],
          sourceReferences: [
            source("cro-medium-company", "CRO medium company financial statements requirements"),
            source("cro-auditors-report", "CRO auditors report requirements"),
            source("frc-frs-102", "FRC FRS 102 current edition and amendments"),
          ],
        },
      },
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
        automatedVerifierCommands: ["dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl"],
        ciScopes: ["default-ci"],
        evidenceLevels: ["end-to-end golden filing scenario"],
        outputArtifacts: ["accounts PDF text"],
        decisionGates: ["named qualified-accountant review"],
        expectedValueChecks: ["well-formed iXBRL"],
        proofPointAreas: ["pdf-text"],
        sourceIds: ["frc-frs-105"],
        expectedCorporationTax: 62.5,
        filingReadinessState: "100% filing readiness",
        signOffPacketState: "review-required",
      },
      {
        scenarioCode: "dac-small",
        label: "Small DAC",
        fixtureLegalName: "Atlantic Manufacturing DAC",
        companyType: "DesignatedActivityCompany",
        expectedOutcome: "generated-pack",
        coverageStatus: "covered",
        acceptanceStatus: "qualified-accountant-review-required",
        requiredSignOffGate: "Named qualified accountant must approve the DAC generated pack before real filing use.",
        blocksRelease: true,
        automatedVerifierNames: ["FilingGoldenCorpusScenarioTests.GoldenCorpus_DacSmall_EmitsAccountsIxbrlAndSourceBackedReadiness"],
        automatedVerifierCommands: ["dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~FilingGoldenCorpusScenarioTests.GoldenCorpus_DacSmall_EmitsAccountsIxbrlAndSourceBackedReadiness"],
        ciScopes: ["default-ci"],
        evidenceLevels: ["end-to-end golden filing scenario"],
        outputArtifacts: ["DAC accounts PDF text"],
        decisionGates: ["named qualified-accountant review"],
        expectedValueChecks: ["well-formed iXBRL"],
        proofPointAreas: ["pdf-text"],
        sourceIds: ["frc-frs-102"],
        expectedCorporationTax: 62.5,
        filingReadinessState: "ready-for-external-filing",
        signOffPacketState: "ready-for-external-filing",
      },
      {
        scenarioCode: "small-abridged-ltd",
        label: "Small abridged LTD",
        fixtureLegalName: "Connacht Digital Solutions Limited",
        companyType: "Private",
        expectedOutcome: "generated-pack",
        coverageStatus: "covered",
        acceptanceStatus: "qualified-accountant-review-required",
        requiredSignOffGate: "Named qualified accountant must approve both the full accounts pack and abridged CRO pack before real filing use.",
        blocksRelease: true,
        automatedVerifierNames: [
          "AccountsWorkflowTests.GoldenPath_SmallAuditExemptCompany_MixedAccrualSetBalancesThroughPdfAndIxbrl",
          "FilingGoldenCorpusScenarioTests.GoldenCorpus_SmallAbridgedLtd_EmitsFullAccountsAbridgedCroPackIxbrlAndReadiness",
        ],
        automatedVerifierCommands: [
          "dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~AccountsWorkflowTests.GoldenPath_SmallAuditExemptCompany_MixedAccrualSetBalancesThroughPdfAndIxbrl",
          "dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~FilingGoldenCorpusScenarioTests.GoldenCorpus_SmallAbridgedLtd_EmitsFullAccountsAbridgedCroPackIxbrlAndReadiness",
        ],
        ciScopes: ["default-ci"],
        evidenceLevels: ["end-to-end golden filing scenario"],
        outputArtifacts: ["full accounts PDF text", "abridged CRO filing pack", "CRO signature page", "iXBRL XML", "tax computation", "notes disclosure set", "filing readiness profile"],
        decisionGates: ["abridgement eligibility", "director and secretary certification", "named qualified-accountant review", "external ROS/iXBRL validation"],
        expectedValueChecks: ["SmallAbridged regime", "Section 352 wording", "public P&L turnover omitted from iXBRL"],
        proofPointAreas: ["pdf-text", "ixbrl-xml"],
        sourceIds: ["frc-frs-102"],
        expectedCorporationTax: 62.5,
        filingReadinessState: "ready-for-external-filing",
        signOffPacketState: "ready-for-external-filing",
      },
      {
        scenarioCode: "clg-charity",
        label: "CLG charity annual reporting",
        fixtureLegalName: "Dublin Community Support CLG",
        companyType: "CompanyLimitedByGuarantee",
        expectedOutcome: "generated-pack-with-charity-gates",
        coverageStatus: "covered",
        acceptanceStatus: "qualified-accountant-review-required",
        requiredSignOffGate: "Named qualified accountant must approve the CLG charity pack and charity evidence before real filing use.",
        blocksRelease: true,
        automatedVerifierNames: [
          "FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness",
        ],
        automatedVerifierCommands: [
          "dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness",
        ],
        ciScopes: ["default-ci"],
        evidenceLevels: ["end-to-end golden filing scenario"],
        outputArtifacts: ["CLG accounts PDF text", "charity readiness profile", "SoFA evidence", "trustees annual report evidence", "iXBRL XML", "tax computation", "notes disclosure set", "accountant sign-off packet"],
        decisionGates: ["charity number", "charity annual return review", "named qualified-accountant review", "accountant sign-off packet state"],
        expectedValueChecks: ["charity evidence satisfied", "Charities Regulator source attached", "CLG source attached", "well-formed iXBRL"],
        proofPointAreas: ["pdf-text", "filing-readiness"],
        sourceIds: ["cro-guarantee-company", "charities-regulator-annual-report", "frc-frs-102"],
        expectedCorporationTax: 62.5,
        filingReadinessState: "ready-for-external-filing",
        signOffPacketState: "ready-for-external-filing",
      },
      {
        scenarioCode: "medium-audit-required",
        label: "Medium audit-required handoff",
        fixtureLegalName: "Midlands Manufacturing Limited",
        companyType: "Private",
        expectedOutcome: "manual-handoff",
        coverageStatus: "covered",
        acceptanceStatus: "qualified-accountant-review-required",
        requiredSignOffGate: "Qualified accountant must record manual handoff acceptance before relying on outputs.",
        blocksRelease: true,
        automatedVerifierNames: [
          "FilingGoldenCorpusScenarioTests.GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence",
        ],
        automatedVerifierCommands: [
          "dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~FilingGoldenCorpusScenarioTests.GoldenCorpus_MediumAuditRequired_BlocksFinalOutputsAndRequiresManualHandoffUntilAuditorEvidence",
        ],
        ciScopes: ["default-ci"],
        evidenceLevels: ["end-to-end golden filing scenario"],
        outputArtifacts: ["full accounts PDF text", "auditor report evidence", "cash flow statement", "statement of changes in equity", "iXBRL XML", "filing readiness profile", "tax computation", "accountant sign-off packet"],
        decisionGates: ["auditor handoff", "manual professional review", "normal CRO approval blocked until auditor evidence", "accountant sign-off packet state"],
        expectedValueChecks: ["Medium regime selected", "audit report blocker present before auditor evidence", "tagged P&L facts present after auditor evidence", "auditor reference appears in PDF text"],
        proofPointAreas: ["auditor-handoff", "filing-readiness"],
        sourceIds: ["cro-medium-company", "cro-auditors-report", "frc-frs-102"],
        expectedCorporationTax: 62.5,
        filingReadinessState: "manual-handoff-until-auditor-evidence",
        signOffPacketState: "manual-handoff",
      },
    ],
    statutoryRuleMatrix: [
      {
        code: "ltd-micro",
        companyScope: "LTD micro",
        sizeOrRegime: "Micro / FRS 105",
        supportLevel: "supported",
        requiredEvidence: ["size classification"],
        requiredOutputs: ["micro accounts PDF"],
        manualHandoffGates: ["qualified accountant review"],
        sources: [source("frc-frs-105", "FRC FRS 105 current edition and amendments")],
      },
    ],
    statutoryRulesCoverage: [
      {
        code: "size-classification-thresholds",
        ruleFamily: "Size classification",
        decisionUnderTest: "Two-of-three thresholds and current/prior movement produce the statutory size class.",
        coverageStatus: "covered",
        automatedVerifierNames: ["AccountsWorkflowTests.SizeClassification_FirstYearMicro_AllowsMicroAndAuditExemption"],
        edgeCases: ["two-of-three threshold rule", "current and prior year classification rule"],
        sources: [source("frc-frs-105", "FRC FRS 105 current edition and amendments")],
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
        code: "no-direct-cro-ros-submission",
        label: "No direct CRO/ROS submission automation",
        required: true,
        status: "enforced",
        detail: "Final filing operations remain recorded workflow states only.",
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
        code: "light-dark-visual-regression",
        label: "Light/dark visual regression",
        owner: "Engineering",
        priority: "high",
        riskRank: 30,
        evidenceStage: "visual-qa-evidence",
        status: "in-progress",
        detail: "Screenshot review covers accountant routes in light and dark mode.",
        evidenceRequired: "Named visual QA reviewer sign-off against the screenshot manifest.",
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
        code: "frontend-ui-ux:light-dark-visual-regression",
        trackCode: "frontend-ui-ux",
        trackLabel: "Frontend UI/UX",
        ownerRole: "Engineering",
        severity: "high",
        riskRank: 30,
        blockingIssue: "Light/dark visual regression required",
        requiredEvidence: "Named visual QA reviewer sign-off against the screenshot manifest.",
        nextAction: "Review each screenshot route-by-route.",
        sourceActionCode: "light-dark-visual-regression",
        releaseChecklistCode: "visual-qa-screenshot-review",
        operationalGateCode: "",
        evidenceArtifact: "visual-smoke-screenshots",
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
        currentEvidence: [
          "Backend golden corpus scenarios are covered.",
          "No direct CRO/ROS submission automation is enforced as recorded workflow states only.",
        ],
        nextActions: ["Run qualified-accountant acceptance on the golden corpus."],
        assuranceActionCodes: ["qualified-accountant-signoff", "source-law-change-review", "no-direct-cro-ros-submission"],
      },
      {
        code: "frontend-ui-ux",
        label: "Frontend UI/UX",
        ownerRole: "Product design",
        status: "in-progress",
        completionCriteria: ["Accountant workflow rail is visually coherent."],
        currentEvidence: ["Visual QA route audit covers the main routes."],
        nextActions: ["Review each screenshot route-by-route."],
        assuranceActionCodes: ["qualified-accountant-signoff", "light-dark-visual-regression"],
      },
      {
        code: "frontend-code",
        label: "Frontend code",
        ownerRole: "Frontend engineering",
        status: "in-progress",
        completionCriteria: ["Typed API contract blocks readiness drift."],
        currentEvidence: [
          "API client invariants validate readiness contracts.",
          "PeriodFilingWorkspace extraction composes review, deadline, warning, output and audit panels behind one focused filing workflow component.",
          "PeriodImportWorkspace extraction composes classification, bank account, opening-balance, CSV upload and import-status panels behind one focused import workflow component.",
          "PeriodCategoriseWorkspace extraction composes metrics, transaction rules, bulk actions, filters and categorisation table behind one focused transaction review component.",
          "PeriodYearEndWorkspace extraction composes questionnaire, completeness, summary metrics and empty-state panels behind one focused year-end workflow component.",
          "PeriodAdjustmentsWorkspace extraction composes generation, summary, filters and approval review cards behind one focused adjustments workflow component.",
          "PeriodStatementsWorkspace extraction composes readiness, statements, notes and charity reporting navigation behind one focused statements workflow component.",
        ],
        nextActions: ["Continue extracting large route files."],
        assuranceActionCodes: ["qualified-accountant-signoff"],
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
        detail: "Named professional approval must be recorded against the period.",
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
        auditEventCodes: ["CroFilingStatusChanged"],
        detail: "Pinned CRO, Revenue, FRC and charity guidance must be reviewed for effective-date or wording changes before release.",
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
        code: "visual-qa-screenshot-review",
        label: "Visual QA screenshot review",
        ownerRole: "Engineering",
        required: true,
        status: "in-progress",
        blocksRelease: true,
        evidenceArtifact: "visual-smoke-screenshots",
        assuranceActionCode: "light-dark-visual-regression",
        operationalGateCode: "",
        auditEventCodes: [],
        detail: "Named visual QA reviewer sign-off must be recorded against the screenshot manifest.",
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
        code: "visual-smoke-light-dark",
        label: "Light/dark desktop/mobile visual smoke",
        ownerRole: "Engineering",
        command: "node scripts/visual-smoke.mjs; node scripts/verify-visual-smoke-artifacts.mjs",
        ciScope: "default-ci",
        runsInDefaultCi: true,
        blocksRelease: true,
        evidenceArtifact: "artifacts/visual-smoke",
        releaseChecklistEvidenceArtifact: "visual-smoke-screenshots",
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
        verification: "Audit log snapshots and integrity hash chain must cover the changed entity.",
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
    visualQaCoverage: {
      artifactName: "visual-smoke-screenshots",
      enforcement: "ci-production-smoke",
      manifestFileName: "visual-smoke-manifest.json",
      expectedScreenshotCount: expectedVisualSmokeScreenshotCount(),
      layoutChecks: visualSmokeLayoutChecks,
      reviewChecks: visualSmokeReviewChecks,
      reviewProtocol: visualSmokeReviewProtocol,
      themes: visualSmokeThemes,
      viewports: visualSmokeViewports,
      routes: visualSmokeRoutes.map(({ name, routeKey, label, description, expectedText, workflowStages, openFilingTab }) => ({
        code: name,
        routeKey,
        label,
        description,
        requiredText: expectedText,
        workflowStages,
        openFilingTab,
      })),
      routeAudits: expectedVisualSmokeRouteAudits().map((audit) => ({
        routeCode: audit.routeName,
        routeKey: audit.routeKey,
        label: audit.label,
        workflowStages: audit.workflowStages,
        screenshotCount: audit.screenshotCount,
        reviewStatus: audit.reviewStatus,
        reviewChecks: audit.reviewChecks,
      })),
      artifacts: expectedVisualSmokeArtifacts().map(({ routeName, routeKey, theme, viewportName, fileName, artifactPath, expectedText, openFilingTab, reviewStatus, layoutChecks }) => ({
        routeCode: routeName,
        routeKey,
        theme,
        viewportName,
        fileName,
        artifactPath,
        requiredText: expectedText,
        openFilingTab,
        reviewStatus,
        layoutChecks,
      })),
    },
  });
}

function withGoldenLegalBasisSnapshots(report) {
  return {
    ...report,
    goldenFilingCorpus: report.goldenFilingCorpus.map((scenario) => ({
      ...scenario,
      legalBasisSnapshot: goldenLegalBasisSnapshot(scenario),
    })),
  };
}

function goldenLegalBasisSnapshot(scenario) {
  const basisByCode = {
    "micro-ltd": {
      legalBasis: "FRS 105 micro-entities regime with CRO financial-statement and Revenue iXBRL filing evidence.",
      sourceIds: [
        "cro-financial-statements-requirements",
        "frc-frs-105",
        "revenue-ixbrl-overview",
        "revenue-accepted-taxonomies",
      ],
    },
    "small-abridged-ltd": {
      legalBasis: "FRS 102 small-company abridgement with Section 352 CRO filing evidence and Revenue iXBRL evidence.",
      sourceIds: [
        "cro-financial-statements-requirements",
        "frc-frs-102",
        "revenue-ixbrl-contents",
        "revenue-accepted-taxonomies",
      ],
    },
    "dac-small": {
      legalBasis: "FRS 102 small-company DAC path with directors' report, CRO certification and Revenue iXBRL evidence.",
      sourceIds: [
        "cro-financial-statements-requirements",
        "frc-frs-102",
        "revenue-ixbrl-overview",
        "revenue-accepted-taxonomies",
      ],
    },
    "clg-charity": {
      legalBasis: "CLG charity reporting path with CRO guarantee-company, Charities Regulator annual-report and FRS 102 evidence.",
      sourceIds: [
        "cro-guarantee-company",
        "charities-regulator-annual-report",
        "frc-frs-102",
        "revenue-ixbrl-overview",
      ],
    },
    "medium-audit-required": {
      legalBasis: "Medium-company audit-required path blocked to manual handoff until auditor report and professional review evidence are present.",
      sourceIds: [
        "cro-medium-company",
        "cro-auditors-report",
        "frc-frs-102",
        "revenue-ixbrl-overview",
      ],
    },
  };
  const basis = basisByCode[scenario.code] ?? {
    legalBasis: `${scenario.label} source-backed statutory filing basis.`,
    sourceIds: scenario.evidencePack.sourceReferences.map((sourceReference) => sourceReference.sourceId),
  };
  const sourceIds = Array.from(
    new Set([
      ...basis.sourceIds,
      ...scenario.evidencePack.sourceReferences.map((sourceReference) => sourceReference.sourceId),
    ]),
  ).sort();

  return {
    scenarioCode: scenario.code,
    companyType: scenario.fixture.companyType,
    sizeClass: scenario.fixture.expectedSizeClass,
    electedRegime: scenario.fixture.expectedRegime,
    auditExempt: scenario.fixture.auditExempt,
    manualProfessionalReviewRequired: scenario.fixture.manualProfessionalReviewRequired,
    legalBasis: basis.legalBasis,
    requiredOutputs: scenario.evidencePack.outputArtifacts,
    professionalGates: scenario.evidencePack.decisionGates,
    sourceIds,
  };
}

function journeyAcceptance(routeCode, routeLabel, routeKey, workflowStages, acceptanceCriteria) {
  return {
    routeCode,
    routeLabel,
    routeKey,
    workflowStages,
    seededScenarioCodes: ["clg-charity", "dac-small", "medium-audit-required", "micro-ltd", "small-abridged-ltd"],
    visualArtifactNames: ["dark-desktop", "dark-mobile", "light-desktop", "light-mobile"].map(
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
    signOffGate: "accountant-final-signoff",
    status: "required-review",
  };
}

function source(sourceId, title) {
  return {
    sourceId,
    title,
    effectiveDate: "2026-07-03",
    url: "https://www.frc.org.uk/",
  };
}

function sourceLawReviewEntry(sourceId, title) {
  const legalSource = source(sourceId, title);

  return {
    sourceId: legalSource.sourceId,
    title: legalSource.title,
    url: legalSource.url,
    pinnedEffectiveDate: legalSource.effectiveDate,
    ownerRole: "Accounting standards reviewer",
    releaseChecklistCode: "source-law-change-review",
    blocksRelease: true,
    reviewChecks: [
      "Confirm source page is reachable at the pinned URL.",
      "Compare pinned effective date against the current source page.",
      "Review guidance wording for statutory filing, exemption, note or taxonomy changes.",
      "Record qualified accountant acceptance before generated packs are used for real filings.",
    ],
    requiredEvidence: [
      "source-law-change-review-note",
      "qualified-accountant-source-law-signoff",
    ],
  };
}

// Loans (company-scoped)
await expect(() => getLoans(1), "GET", "/api/companies/1/loans");
await expect(() => createLoan(1, loan), "POST", "/api/companies/1/loans");
await expect(() => updateLoan(1, 9, loan), "PUT", "/api/companies/1/loans/9");
await expect(() => deleteLoan(1, 9), "DELETE", "/api/companies/1/loans/9");

// Loan snapshots (per period)
await expect(() => getLoanSnapshots(1, 2), "GET", "/api/companies/1/periods/2/loan-balance-snapshots");
await expect(() => createLoanSnapshot(1, 2, snapshot), "POST", "/api/companies/1/periods/2/loan-balance-snapshots");
await expect(() => updateLoanSnapshot(1, 2, 9, snapshot), "PUT", "/api/companies/1/periods/2/loan-balance-snapshots/9");
await expect(() => deleteLoanSnapshot(1, 2, 9), "DELETE", "/api/companies/1/periods/2/loan-balance-snapshots/9");

// Director loans (per period)
await expect(() => getDirectorLoans(1, 2), "GET", "/api/companies/1/periods/2/director-loans");
await expect(() => createDirectorLoan(1, 2, dirLoan), "POST", "/api/companies/1/periods/2/director-loans");
await expect(() => updateDirectorLoan(1, 2, 9, dirLoan), "PUT", "/api/companies/1/periods/2/director-loans/9");
await expect(() => deleteDirectorLoan(1, 2, 9), "DELETE", "/api/companies/1/periods/2/director-loans/9");

// Share capital (company-scoped)
await expect(() => getShareCapital(1), "GET", "/api/companies/1/share-capital");
await expect(() => createShareCapital(1, share), "POST", "/api/companies/1/share-capital");
await expect(() => updateShareCapital(1, 9, share), "PUT", "/api/companies/1/share-capital/9");
await expect(() => deleteShareCapital(1, 9), "DELETE", "/api/companies/1/share-capital/9");

// Year-end row updates (BL-25)
await expect(() => updateDebtor(1, 2, 9, { name: "x", amount: 1, type: "Trade" }), "PUT", "/api/companies/1/periods/2/debtors/9");
await expect(() => updateCreditor(1, 2, 9, { name: "x", amount: 1, type: "Trade", dueWithinYear: true }), "PUT", "/api/companies/1/periods/2/creditors/9");
await expect(() => updateFixedAsset(1, 9, { name: "x", category: "Equipment", cost: 1, acquisitionDate: "2025-01-01", usefulLifeYears: 4, depreciationMethod: "StraightLine" }), "PUT", "/api/companies/1/fixed-assets/9");
await expect(() => updateInventory(1, 2, 9, { description: "x", value: 1, valuationMethod: "FIFO" }), "PUT", "/api/companies/1/periods/2/inventory/9");
await expect(() => updateDividend(1, 2, 9, { amount: 1 }), "PUT", "/api/companies/1/periods/2/dividends/9");

// System assurance report
await expect(() => getProductionReadinessReport(), "GET", "/api/system/production-readiness");

console.log("verify-api-client: all checked client routes OK");
