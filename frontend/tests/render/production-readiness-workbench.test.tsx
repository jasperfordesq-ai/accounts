import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { ProductionReadinessWorkbench } from "@/components/readiness/ProductionReadinessWorkbench";
import type { ProductionReadinessReport } from "@/lib/api";

describe("ProductionReadinessWorkbench", () => {
  it("turns backend readiness evidence into a full accountant checklist", () => {
    render(<ProductionReadinessWorkbench report={sampleReport()} />);
    const pageText = () => document.body.textContent ?? "";
    const expectText = (value: RegExp | string) => {
      if (value instanceof RegExp) {
        expect(pageText()).toMatch(value);
      } else {
        expect(pageText()).toContain(value);
      }
    };

    expect(screen.getByRole("heading", { name: "Production Readiness Checklist" })).toBeInTheDocument();
    expect(screen.getAllByText("Review required").length).toBeGreaterThan(1);
    expectText("3 companies");
    expectText("4 periods");
    expect(screen.getByRole("searchbox", { name: "Filter Next assurance actions" })).toBeInTheDocument();
    expect(screen.getByRole("searchbox", { name: "Filter Statutory rules matrix" })).toBeInTheDocument();
    expect(screen.getByRole("searchbox", { name: "Filter Golden filing corpus" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Golden filing corpus" })).toBeInTheDocument();
    expectText("5 of 5 scenarios");
    expect(screen.getAllByText("Micro LTD").length).toBeGreaterThanOrEqual(2);
    expect(screen.getAllByText("Small abridged LTD").length).toBeGreaterThanOrEqual(2);
    expect(screen.getAllByText("DAC small").length).toBeGreaterThanOrEqual(2);
    expect(screen.getAllByText("CLG charity").length).toBeGreaterThanOrEqual(2);
    expect(screen.getAllByText("Medium audit-required").length).toBeGreaterThanOrEqual(2);
    expect(screen.getAllByText("AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl").length).toBeGreaterThan(1);
    expect(screen.getByRole("heading", { name: "Golden evidence pack" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Golden evidence ledger" })).toBeInTheDocument();
    expect(screen.getByRole("searchbox", { name: "Filter Golden evidence ledger" })).toBeInTheDocument();
    expectText("accounts PDF text");
    expectText("accountant sign-off packet");
    expectText("accountant-signoff-packet");
    expectText("Sign-off packet shows reviewer state, open blockers and allowed next actions.");
    expectText("director and secretary certification");
    expectText("well-formed iXBRL");
    expectText("Expected CT:");
    expectText("€62.50");
    expectText("Expected proof points");
    expectText("PDF text contains company name and micro statutory statement.");
    expectText("signatory-gates");
    expect(screen.getAllByRole("link", { name: "FRC FRS 105 current edition and amendments" }))
      .toEqual(expect.arrayContaining([expect.objectContaining({ href: "https://www.frc.org.uk/" })]));
    expectText("Unsupported/manual handoff");
    expectText("PLC and public-company workflows");
    expectText("Operations and security");
    expectText("Named qualified-accountant review");
    expect(screen.getByRole("heading", { name: "Source-backed statutory rules" })).toBeInTheDocument();
    expect(screen.getAllByRole("link", { name: "Revenue accepted iXBRL taxonomies" }))
      .toEqual(expect.arrayContaining([expect.objectContaining({ href: "https://www.revenue.ie/" })]));
    expect(screen.getAllByText("external-ros-validation").length).toBeGreaterThan(0);
    expect(screen.getAllByText("ixbrl-taxonomy-selection").length).toBeGreaterThan(0);
    expect(screen.getByRole("heading", { name: "Next assurance actions" })).toBeInTheDocument();
    expectText("Qualified accountant sign-off");
    expectText("Risk 0");
    expectText("accountant-review-gate");
    expectText("Light/dark visual regression");
    expectText("visual-qa-evidence");
    expectText(/Sentry production error routing configured and reviewed/);
    expect(screen.getByRole("heading", { name: "Production completion map" })).toBeInTheDocument();
    expect(screen.getByRole("searchbox", { name: "Filter Production completion map" })).toBeInTheDocument();
    expectText("Backend code");
    expectText("Frontend UI/UX");
    expectText("Frontend code");
    expectText(/Golden filing corpus proves PDF text/);
    expectText(/Accountant workflow rail is visually coherent/);
    expectText(/Dashboard filing deep links send deadline-pressure and manual-handoff work directly to the period filing review tab/);
    expectText(/Period filing gate snapshot shows supported\/manual path, accountant review state, external filing readiness and allowed next action/);
    expectText(/Permission-denied filing action state keeps evidence visible/);
    expectText(/Typed API contract blocks frontend\/backend readiness drift/);
    expectText(/FilingReviewCentre permission gate blocks approval\/submission actions behind canReview/);
    expectText(/PeriodFilingWorkspace extraction composes review, deadline, warning, output and audit panels/);
    expectText(/PeriodImportWorkspace extraction composes classification, bank account, opening-balance, CSV upload and import-status panels/);
    expectText(/PeriodCategoriseWorkspace extraction composes metrics, transaction rules, bulk actions, filters and categorisation table/);
    expectText(/PeriodYearEndWorkspace extraction composes questionnaire, completeness, summary metrics and empty-state panels/);
    expectText(/PeriodAdjustmentsWorkspace extraction composes generation, summary, filters and approval review cards/);
    expectText(/PeriodStatementsWorkspace extraction composes readiness, statements, notes and charity reporting navigation/);
    expect(screen.getByRole("heading", { name: "Release review checklist" })).toBeInTheDocument();
    expectText("Named accountant final sign-off");
    expectText("named-accountant-approval-record");
    expectText("ci-production-stack-smoke-and-backup-restore");
    expect(screen.getByRole("heading", { name: "Release verification manifest" })).toBeInTheDocument();
    expect(screen.getByRole("searchbox", { name: "Filter Release verification manifest" })).toBeInTheDocument();
    expectText("Backend golden corpus and statutory rules");
    expectText("dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art");
    expectText("PostgreSQL-gated audit durability tests");
    expectText("environment-gated");
    expectText(/ACCOUNTS_POSTGRES_TEST_CONNECTION/);
    expect(screen.getByRole("heading", { name: "Statutory rules matrix" })).toBeInTheDocument();
    expectText("LTD micro");
    expectText("CLG charity");
    expectText("Medium audit-required");
    expectText("Unsupported regulated/group");
    expect(screen.getByRole("heading", { name: "Statutory rules coverage" })).toBeInTheDocument();
    expect(screen.getByRole("searchbox", { name: "Filter Statutory rules coverage" })).toBeInTheDocument();
    expectText("Size classification");
    expectText("two-of-three threshold rule");
    expectText("AccountsWorkflowTests.SizeClassification_FirstYearMicro_AllowsMicroAndAuditExemption");
    expect(screen.getAllByRole("link", { name: "CRO financial statements requirements" }))
      .toEqual(expect.arrayContaining([expect.objectContaining({ href: "https://cro.ie/" })]));
    expect(screen.getByRole("heading", { name: "Visual QA coverage" })).toBeInTheDocument();
    expectText("28 screenshots");
    expect(screen.getAllByText("visual-smoke-manifest.json").length).toBeGreaterThan(1);
    expectText("Visual review protocol");
    expectText("Design reviewer");
    expect(screen.getAllByText("visual-qa-screenshot-review").length).toBeGreaterThan(0);
    expectText("screenshot SHA-256 checksums");
    expectText("named visual QA reviewer sign-off");
    expectText(/Block release if any accountant workbench route/);
    expectText("Light desktop");
    expectText("Dark mobile");
    expectText("Visible text overlap");
    expectText("Route audit summary");
    expect(screen.getByRole("heading", { name: "Visual route review board" })).toBeInTheDocument();
    expectText("7 route reviews open");
    expectText("28 screenshots requiring review");
    expectText("visual-qa-screenshot-review");
    expect(screen.getAllByText("Table scanability").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Theme contrast").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Mobile density").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Filing review").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Capture key filing").length).toBeGreaterThan(0);
    expectText("workbenchPreview");
    expect(screen.getAllByText("visual-smoke-screenshots").length).toBeGreaterThan(1);
    expect(screen.getByRole("heading", { name: "Production auditability" })).toBeInTheDocument();
    expectText("Who changed what");
    expectText("Who approved what");
    expectText("What was generated");
    expectText("Tamper-evident audit chain");
    expectText("audit-log-integrity-chain");
    expect(screen.getAllByText("CroDocumentGenerated").length).toBeGreaterThan(1);
    expect(screen.getByRole("heading", { name: "Audit evidence timeline" })).toBeInTheDocument();
    expectText("Who changed what and when?");
    expectText("At every authenticated write before regenerated outputs can be reviewed.");
    expectText("Generated output audit event must exist before accountant approval can rely on the pack.");
    expect(screen.getAllByText("generated-output-review").length).toBeGreaterThan(0);
    expect(screen.getByRole("heading", { name: "Production monitoring" })).toBeInTheDocument();
    expectText("Production error tracking");
    expectText("Structured JSON logs");
    expectText("Correlation id error responses");
    expectText("Sentry-compatible");
    expectText("Monitoring:ErrorTrackingDsn");
    expectText("Primary on-call accountant and platform owner");
    expectText("Block release if error events cannot be routed to the on-call owner.");
    expect(screen.getByRole("heading", { name: "Dependency policy controls" })).toBeInTheDocument();
    expectText("Frontend dependency vulnerability audit");
    expectText("CI action version hygiene");
    expectText("Fail the release for moderate, high or critical npm advisories.");
    expect(screen.getByRole("heading", { name: "Deployment safety controls" })).toBeInTheDocument();
    expectText("Controlled production migrations");
    expectText("Production demo seed blocking");
    expectText("Backup restore drill");
    expectText("Fail production startup if demo seed data is enabled outside development.");
    expectText("sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
    expectText("1 pinned source");
    expect(screen.getByRole("heading", { name: "Production assurance packet" })).toBeInTheDocument();
    expectText("assurance-sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
    expectText("Golden corpus 5/5");
    expect(screen.getByRole("heading", { name: "Source-law maintenance" })).toBeInTheDocument();
    expectText("source-law-maintenance-v1");
    expectText("Source-law change review");
    expectText("source-law-change-review-note");
    expectText("qualified-accountant-source-law-signoff");
    expect(screen.getByRole("heading", { name: "Source-law review ledger" })).toBeInTheDocument();
    expectText("Taxonomy and corporation tax reviewer");
    expectText("Confirm Revenue-accepted taxonomy and iXBRL content guidance still match generated output assumptions.");
    expect(screen.getByRole("heading", { name: "Revenue taxonomy ranges" })).toBeInTheDocument();
    expect(screen.getByRole("searchbox", { name: "Filter Revenue taxonomy ranges" })).toBeInTheDocument();
    expectText("Irish Extension 2025 FRS 102 taxonomy accepted by Revenue");
    expectText("2024-01-01 onward");
    expectText("Irish Extension 2023 FRS 102 taxonomy accepted by Revenue");
    expectText("2023-01-01 to 2024-01-01");
    expectText("Irish Extension 2025 FRS 101 taxonomy accepted by Revenue");
    expectText("EU IFRS Irish Extension 2023 taxonomy accepted by Revenue");
    expect(screen.getAllByText("Manual handoff").length).toBeGreaterThan(0);
    expectText("revenue-taxonomy-range-evidence");
    expectText(/Block release if any pinned source changes/);
    expect(screen.getAllByText("Qualified accountant sign-off required").length).toBeGreaterThan(1);
    expect(screen.getByRole("heading", { name: "Release blocker register" })).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Production release blockers" })).toHaveAttribute(
      "data-workbench-release-blocker-summary",
      "true",
    );
    expect(screen.getByRole("searchbox", { name: "Filter Release blocker register" })).toBeInTheDocument();
    expectText("backend-code:qualified-accountant-signoff");
    expectText("frontend-ui-ux:light-dark-visual-regression");
    expectText("named-accountant-approval-record");
    expectText("light-dark-desktop-mobile-screenshot-review");
    expectText("Run qualified-accountant acceptance on the golden corpus.");
    expect(screen.getByRole("heading", { name: "Release decision summary" })).toBeInTheDocument();
    expectText("2 professional sign-offs");
    expectText("1 manual handoff scenario");
    expectText("2 automated verifiers");
    expectText("micro-ltd, medium-audit-required");
    expectText("Do not use for real filings");
    expectText("1 critical blocker");
    expectText("Golden corpus covered");
    expectText("5 of 5 scenarios");
    expectText("Visual QA evidence");
    expectText("28 required screenshots");
    expectText("Accountant acceptance");
    expect(screen.getByRole("heading", { name: "Accountant workflow walkthrough" })).toBeInTheDocument();
    expectText("accountant-workflow-walkthrough-v1");
    expectText("Dashboard: identify the client, deadline pressure, blockers, reviewer owner and next action.");
    expectText("Filing review: inspect readiness profile, legal source links, generated outputs, signatory gates and accountant sign-off packet.");
    expectText("Micro LTD walkthrough confirms PDF wording, iXBRL XML, tax computation, notes, signatory gates and 100% filing readiness.");
    expectText("Medium/audit-required walkthrough confirms auditor handoff blocks normal approval until signed auditor report evidence and manual acceptance are recorded.");
    expectText("seeded golden corpus walkthrough note");
    expectText("visual QA screenshot review");
    expectText("golden-corpus-accountant-acceptance");
    expect(screen.getByRole("heading", { name: "Accountant journey acceptance checklist" })).toBeInTheDocument();
    expectText("Dashboard route exposes the relevant accountant workflow state, blockers, next actions and evidence.");
    expectText("Filing review route exposes readiness, source links, generated outputs, signatory gates, accountant sign-off packet, external ROS/iXBRL validation and filing state.");
    expectText("Production readiness route exposes backend checks, filing rules coverage, unsupported paths, security posture, release blockers and accountant review state.");
    expectText("dashboard-light-desktop.png");
    expectText("filing-review-dark-mobile.png");
    expectText("named qualified-accountant route acceptance");
    expect(screen.getByRole("heading", { name: "Accountant acceptance criteria" })).toBeInTheDocument();
    expectText("Micro LTD accountant acceptance");
    expectText("Medium handoff accountant acceptance");
    expect(screen.getAllByText("Example Micro Limited").length).toBeGreaterThan(1);
    expectText("2025-01-01 to 2025-12-31");
    expectText("Named qualified accountant must approve the generated pack before real filing use.");
    expectText("Signed auditor report and manual handoff note reviewed by the qualified accountant.");
    expect(screen.getAllByText("Acceptance verifier").length).toBeGreaterThan(0);
    expect(screen.getAllByText(/dotnet test Accounts\.slnx/).length).toBeGreaterThan(1);
  }, 90000);
});

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
        usedBy: ["statutory-rule-matrix:ltd-micro", "golden-corpus:micro-ltd"],
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
        sourceIds: ["frc-frs-102", "revenue-accepted-taxonomies"],
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
        sourceIds: ["frc-frs-102", "revenue-accepted-taxonomies"],
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
        sourceIds: ["revenue-accepted-taxonomies"],
        releaseGateCodes: ["external-ros-validation", "ixbrl-taxonomy-selection", "source-law-change-review", "manual-professional-handoff"],
      },
      {
        taxonomyKey: "irish-extension-2023-ifrs",
        accountingStandard: "EU IFRS",
        taxonomyDate: "2023-01-01",
        label: "EU IFRS Irish Extension 2023 taxonomy accepted by Revenue",
        schemaRef: "https://xbrl.frc.org.uk/ireland/IFRS/2023-01-01/ie-IFRS-2023-01-01.xsd",
        acceptedByRevenue: true,
        automatedPlatformSelectionSupported: false,
        effectiveForPeriodsStartingOnOrAfter: "2023-01-01",
        effectiveForPeriodsStartingBefore: "2024-01-01",
        sourceIds: ["revenue-accepted-taxonomies"],
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
      statutoryRuleMatrixPaths: 2,
      statutoryRuleCoverageFamilies: 1,
      visualQaExpectedScreenshots: 28,
      requiredOperationalGates: 1,
      openCriticalActions: 1,
      evidenceItems: ["source-law-snapshot-fingerprint", "source-law-traceability-index", "source-law-maintenance-protocol", "source-law-review-ledger", "revenue-taxonomy-range-evidence", "golden-filing-corpus", "golden-evidence-ledger", "golden-verifier-manifest", "audit-evidence-timeline", "visual-smoke-screenshots", "release-review-checklist", "release-verification-manifest", "accountant-acceptance-summary", "accountant-workflow-walkthrough-protocol", "accountant-journey-acceptance-checklist", "production-completion-map"],
      releaseBlockers: ["Qualified accountant sign-off required"],
    },
    accountantAcceptanceCriteria: [
      {
        scenarioCode: "micro-ltd",
        label: "Micro LTD accountant acceptance",
        required: true,
        acceptanceStatus: "qualified-accountant-review-required",
        reviewScope: ["PDF wording", "iXBRL XML", "filing readiness", "tax computation", "notes", "signatory gates"],
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
      {
        scenarioCode: "medium-audit-required",
        label: "Medium handoff accountant acceptance",
        required: true,
        acceptanceStatus: "manual-handoff-review-required",
        reviewScope: ["Auditor handoff", "full accounts PDF", "iXBRL XML", "filing readiness"],
        requiredEvidence: ["Signed auditor report and manual handoff note reviewed by the qualified accountant."],
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
          {
            sourceId: "frc-frs-102",
            title: "FRC FRS 102 current edition and amendments",
            effectiveDate: "2026-07-03",
            url: "https://www.frc.org.uk/",
          },
        ],
      },
    ],
    accountantAcceptanceSummary: {
      scenarioCount: 2,
      automatedVerifierCount: 2,
      professionalSignOffRequiredCount: 2,
      manualHandoffScenarioCount: 1,
      releaseBlockingScenarioCodes: ["micro-ltd", "medium-audit-required"],
      requiredSignOffGates: [
        "Named qualified accountant must approve the generated pack before real filing use.",
        "Qualified accountant must record manual handoff acceptance before relying on outputs.",
      ],
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
      journeyAcceptance("dashboard", "Dashboard", "dashboard", ["Setup", "Import", "Classify", "Year-End", "Statements", "Notes", "Review", "Filing"]),
      journeyAcceptance("company-detail", "Company detail", "company", ["Setup"]),
      journeyAcceptance("period-workspace", "Period workspace", "period", ["Setup", "Import", "Classify", "Year-End", "Statements", "Notes", "Review", "Filing"]),
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
      {
        code: "source-backed-statutory-rules",
        label: "Source-backed statutory rules",
        status: "hardened",
        detail: "Classification and filing decisions include legal source references.",
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
          outputArtifacts: ["accounts PDF text", "CRO filing pack", "iXBRL XML", "accountant sign-off packet"],
          decisionGates: ["named qualified-accountant review", "director and secretary certification", "accountant sign-off packet state"],
          expectedValueChecks: ["Micro regime", "100% filing readiness", "well-formed iXBRL"],
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
            {
              area: "signatory-gates",
              expectedEvidence: "Director and secretary certification gates remain required before filing use.",
              automatedVerifier: "AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl",
              required: true,
            },
            {
              area: "accountant-signoff-packet",
              expectedEvidence: "Sign-off packet shows reviewer state, open blockers and allowed next actions.",
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
      goldenScenario({
        code: "clg-charity",
        label: "CLG charity",
        legalName: "Dublin Community Support CLG",
        companyType: "CompanyLimitedByGuarantee",
        expectedRegime: "Small",
        expectedOutcome: "generated-pack-with-charity-gates",
        verifier: "FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness",
        corporationTax: 62.5,
        readinessState: "ready-for-external-filing",
        signOffPacketState: "ready-for-external-filing",
        proofArea: "charity-readiness",
        sourceId: "charities-regulator-annual-report",
        sourceTitle: "Charities Regulator annual report guidance",
        sourceUrl: "https://www.charitiesregulator.ie/",
      }),
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
        outputArtifacts: ["accounts PDF text", "CRO filing pack", "iXBRL XML", "accountant sign-off packet"],
        decisionGates: ["named qualified-accountant review", "director and secretary certification", "accountant sign-off packet state"],
        expectedValueChecks: ["Micro regime", "100% filing readiness", "well-formed iXBRL"],
        proofPointAreas: ["pdf-text", "signatory-gates", "accountant-signoff-packet"],
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
      goldenLedgerEntry({
        scenarioCode: "clg-charity",
        label: "CLG charity",
        legalName: "Dublin Community Support CLG",
        companyType: "CompanyLimitedByGuarantee",
        expectedOutcome: "generated-pack-with-charity-gates",
        acceptanceStatus: "qualified-accountant-review-required",
        verifier: "FilingGoldenCorpusScenarioTests.GoldenCorpus_ClgCharity_EmitsAccountsIxbrlAndSourceBackedCharityReadiness",
        artifacts: ["CLG accounts PDF text", "charity readiness profile"],
        checks: ["charity evidence satisfied"],
        proofArea: "charity-readiness",
        sourceId: "charities-regulator-annual-report",
        corporationTax: 62.5,
        readinessState: "ready-for-external-filing",
        signOffPacketState: "ready-for-external-filing",
      }),
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
    manualHandoffPaths: ["PLC and public-company workflows"],
    operationalGates: [
      {
        code: "accountant-review",
        label: "Named qualified-accountant review",
        required: true,
        status: "enforced",
        detail: "Real filing packs require named professional approval before use.",
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
        label: "Production monitoring",
        owner: "Operations",
        priority: "high",
        riskRank: 20,
        evidenceStage: "operations-evidence",
        status: "required",
        detail: "Runtime errors must be visible before real filings are processed.",
        evidenceRequired: "Sentry production error routing configured and reviewed.",
      },
      {
        code: "light-dark-visual-regression",
        label: "Light/dark visual regression",
        owner: "Engineering",
        priority: "high",
        riskRank: 30,
        evidenceStage: "visual-qa-evidence",
        status: "in-progress",
        detail: "Capture desktop and mobile screenshots for accountant routes in both themes.",
        evidenceRequired: "Screenshot review attached to CI or release checklist.",
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
        requiredEvidence: "Named accountant approval record.",
        nextAction: "Run qualified-accountant acceptance on the golden corpus.",
        sourceActionCode: "qualified-accountant-signoff",
        releaseChecklistCode: "accountant-final-signoff",
        operationalGateCode: "accountant-review",
        evidenceArtifact: "named-accountant-approval-record",
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
        requiredEvidence: "Screenshot review attached to CI or release checklist.",
        nextAction: "Review each screenshot route-by-route in light and dark mode.",
        sourceActionCode: "light-dark-visual-regression",
        releaseChecklistCode: "visual-qa-screenshot-review",
        operationalGateCode: "",
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
        completionCriteria: [
          "Golden filing corpus proves PDF text, iXBRL XML, tax, notes, readiness and gates.",
          "Source-law snapshot and traceability cover every statutory decision.",
          "Production auditability captures who changed, approved, generated and submitted each pack.",
        ],
        currentEvidence: [
          "Backend golden corpus scenarios are covered by automated verifiers.",
          "Statutory rules coverage is mapped to executable tests.",
          "Production auditability controls and audit evidence timeline are declared.",
        ],
        nextActions: [
          "Run qualified-accountant acceptance on the golden corpus.",
          "Attach external ROS/iXBRL validation evidence for generated iXBRL packs.",
          "Record manual handoff acceptance for audit-required paths.",
        ],
        assuranceActionCodes: [
          "qualified-accountant-signoff",
          "source-law-change-review",
          "external-ros-validation",
          "accountant-acceptance-walkthrough",
        ],
      },
      {
        code: "frontend-ui-ux",
        label: "Frontend UI/UX",
        ownerRole: "Product design",
        status: "in-progress",
        completionCriteria: [
          "Accountant workflow rail is visually coherent across the core journey.",
          "Light/dark visual regression covers desktop and mobile.",
          "Dense review workbench surfaces blockers, evidence, sources and next actions without visual clutter.",
        ],
        currentEvidence: [
          "Visual QA route audit covers the accountant workbench routes.",
          "Dashboard filing deep links send deadline-pressure and manual-handoff work directly to the period filing review tab.",
          "Period filing gate snapshot shows supported/manual path, accountant review state, external filing readiness and allowed next action.",
          "Route-level loading/error states exist for main dynamic routes.",
          "Permission-denied filing action state keeps evidence visible while blocking ineligible review actions.",
          "Workbench primitives are used in the readiness and period review surfaces.",
        ],
        nextActions: [
          "Review each screenshot route-by-route in light and dark mode.",
          "Polish spacing, typography, table density, empty states and mobile flow.",
          "Record named visual acceptance against the smoke manifest.",
        ],
        assuranceActionCodes: [
          "light-dark-visual-regression",
          "accountant-acceptance-walkthrough",
        ],
      },
      {
        code: "frontend-code",
        label: "Frontend code",
        ownerRole: "Frontend engineering",
        status: "in-progress",
        completionCriteria: [
          "Shared workbench primitives cover repeated page patterns.",
          "Typed API contract blocks frontend/backend readiness drift.",
          "Route-level states cover loading, error, empty and permission-denied cases.",
        ],
        currentEvidence: [
          "API client invariants validate production readiness contracts.",
          "Component-preview route exercises shared workbench primitives.",
          "FilingReviewCentre permission gate blocks approval/submission actions behind canReview and renders PermissionDeniedPanel for ineligible roles.",
          "PeriodFilingWorkspace extraction composes review, deadline, warning, output and audit panels behind one focused filing workflow component.",
          "PeriodImportWorkspace extraction composes classification, bank account, opening-balance, CSV upload and import-status panels behind one focused import workflow component.",
          "PeriodCategoriseWorkspace extraction composes metrics, transaction rules, bulk actions, filters and categorisation table behind one focused transaction review component.",
          "PeriodYearEndWorkspace extraction composes questionnaire, completeness, summary metrics and empty-state panels behind one focused year-end workflow component.",
          "PeriodAdjustmentsWorkspace extraction composes generation, summary, filters and approval review cards behind one focused adjustments workflow component.",
          "PeriodStatementsWorkspace extraction composes readiness, statements, notes and charity reporting navigation behind one focused statements workflow component.",
          "Render tests cover accountant dashboards, review panels and workflow routes.",
        ],
        nextActions: [
          "Continue extracting large route files into focused workflow components.",
          "Expand visual regression assertions from screenshot capture into reviewable sign-off.",
          "Keep route fixtures aligned with backend readiness evidence.",
        ],
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
        operationalGateCode: "accountant-review",
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
        operationalGateCode: "accountant-review",
        auditEventCodes: ["CroFilingStatusChanged"],
        detail: "Pinned CRO, Revenue, FRC and charity guidance must be reviewed for effective-date or wording changes before release.",
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
        operationalGateCode: "",
        auditEventCodes: [],
        detail: "Production stack smoke and backup restore evidence must be attached before release.",
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
        operationalGateCode: "",
        auditEventCodes: [],
        detail: "Desktop and mobile screenshots in light and dark mode must be reviewed.",
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
        code: "postgres-gated-audit-tests",
        label: "PostgreSQL-gated audit durability tests",
        ownerRole: "Engineering",
        command: "dotnet test Accounts.slnx -c Release -p:ArtifactsPath=$env:TEMP/accts-art --filter FullyQualifiedName~PostgresIntegration",
        ciScope: "environment-gated",
        runsInDefaultCi: false,
        blocksRelease: true,
        evidenceArtifact: "postgres-integration-test-results",
        releaseChecklistEvidenceArtifact: "ci-production-stack-smoke-and-backup-restore",
        manualFallback: "Set ACCOUNTS_POSTGRES_TEST_CONNECTION to a disposable PostgreSQL database before relying on audit durability evidence.",
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
      {
        code: "who-approved-what",
        label: "Who approved what",
        required: true,
        enforcement: "workflow-gates-plus-audit-log-integrity-chain",
        evidenceCaptured: "Named reviewer identity, approval timestamps and filing status transitions.",
        verification: "Approval endpoints write audit events after readiness gates pass.",
        auditEventCodes: ["AdjustmentApproved", "CroFilingStatusChanged"],
      },
      {
        code: "what-was-generated",
        label: "What was generated",
        required: true,
        enforcement: "server-side-generation-events",
        evidenceCaptured: "Generated accounts documents, iXBRL checks and period linkage.",
        verification: "Generation events are recorded before evidence is marked satisfied.",
        auditEventCodes: ["CroDocumentGenerated"],
      },
      {
        code: "tamper-evident-chain",
        label: "Tamper-evident audit chain",
        required: true,
        enforcement: "audit-log-integrity-chain-and-signed-checkpoint",
        evidenceCaptured: "Previous hash, current hash, checkpoint key id and signed checkpoint anchor.",
        verification: "Signed checkpoint verifies the latest company audit entry.",
        auditEventCodes: ["IxbrlInternalCheckCompleted"],
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
      {
        code: "generated-output-capture",
        stage: "Generated outputs",
        evidenceQuestion: "What was generated and when?",
        capturedWhen: "Immediately after server-side PDF, notes or iXBRL generation completes.",
        requiredActor: "System generation service",
        verification: "Generated output audit event must exist before accountant approval can rely on the pack.",
        auditEventCodes: ["CroDocumentGenerated"],
        blockingGateCodes: ["generated-output-review"],
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
      {
        code: "structured-json-logs",
        label: "Structured JSON logs",
        provider: "ASP.NET Core JSON console",
        required: true,
        productionSafetyGate: "Monitoring:StructuredJsonConsole",
        evidenceCaptured: "Production log lines keep structured fields and scopes for indexing.",
        verification: "Program.cs switches to AddJsonConsole when the production safety gate is enabled.",
        alertRoute: "Platform operations log stream and release reviewer",
        failurePolicy: "Block release if production logs cannot be parsed by timestamp, level, category and correlation id.",
      },
      {
        code: "correlation-id-error-responses",
        label: "Correlation id error responses",
        provider: "ExceptionMiddleware",
        required: true,
        productionSafetyGate: "Monitoring:IncludeCorrelationId",
        evidenceCaptured: "Safe client errors include a correlation id that maps to the server exception log.",
        verification: "ExceptionMiddleware logs unhandled errors and returns the trace identifier.",
        alertRoute: "Support triage queue and platform owner",
        failurePolicy: "Block release if safe error responses omit correlation ids or server logs cannot be matched to the support ticket.",
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
      {
        code: "ci-action-version-hygiene",
        label: "CI action version hygiene",
        required: true,
        enforcement: "Workflow Hygiene job runs node scripts/verify-ci-actions.mjs before downstream jobs.",
        evidenceCaptured: "GitHub Actions used by CI are checked for explicit version hygiene.",
        verification: ".github/workflows/ci.yml Workflow Hygiene job.",
        failurePolicy: "Fail the release if workflow actions are unpinned or bypass the hygiene verifier.",
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
      {
        code: "production-demo-seed-block",
        label: "Production demo seed blocking",
        required: true,
        enforcement: "ProductionSafetyService rejects DatabaseStartup:SeedDemoData outside development.",
        evidenceCaptured: "Startup safety validation blocks known sample companies and demo users in production.",
        verification: "ProductionSafetyService validates SeedDemoData before database startup tasks execute.",
        failurePolicy: "Fail production startup if demo seed data is enabled outside development.",
      },
      {
        code: "backup-restore-drill",
        label: "Backup restore drill",
        required: true,
        enforcement: "CI production stack smoke runs scripts/backup-postgres.ps1 and scripts/verify-postgres-backup.ps1.",
        evidenceCaptured: "Production backup dump, sha256 sidecar and restore verification are produced during CI.",
        verification: ".github/workflows/ci.yml Run production backup restore drill step.",
        failurePolicy: "Fail the release if backup creation, checksum verification or restore verification fails.",
      },
    ],
    statutoryRuleMatrix: [
      {
        code: "ltd-micro",
        companyScope: "LTD micro",
        sizeOrRegime: "Micro / FRS 105",
        supportLevel: "supported",
        requiredEvidence: ["size classification", "director and secretary"],
        requiredOutputs: ["micro accounts PDF", "iXBRL"],
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
      {
        code: "clg-charity",
        companyScope: "CLG charity",
        sizeOrRegime: "Small / charity annual return",
        supportLevel: "supported-with-review",
        requiredEvidence: ["charity number", "SoFA"],
        requiredOutputs: ["accounts PDF", "charity annual return support"],
        manualHandoffGates: ["charity annual return review"],
        sources: [
          {
            sourceId: "charities-regulator-annual-report",
            title: "Charities Regulator annual report guidance",
            effectiveDate: "2026-07-03",
            url: "https://www.charitiesregulator.ie/",
          },
        ],
      },
      {
        code: "medium-audit-required",
        companyScope: "Medium audit-required",
        sizeOrRegime: "Medium / full accounts",
        supportLevel: "manual-handoff",
        requiredEvidence: ["signed auditor report"],
        requiredOutputs: ["full accounts pack"],
        manualHandoffGates: ["auditor handoff required"],
        sources: [
          {
            sourceId: "frc-frs-102",
            title: "FRC FRS 102 current edition and amendments",
            effectiveDate: "2026-07-03",
            url: "https://www.frc.org.uk/",
          },
        ],
      },
      {
        code: "unsupported-regulated-group",
        companyScope: "Unsupported regulated/group",
        sizeOrRegime: "Regulated, group or public company",
        supportLevel: "unsupported",
        requiredEvidence: ["manual professional ownership"],
        requiredOutputs: ["manual handoff record"],
        manualHandoffGates: ["fail closed before filing workflow"],
        sources: [
          {
            sourceId: "cro-group-company",
            title: "CRO group company financial statements requirements",
            effectiveDate: "2026-07-03",
            url: "https://cro.ie/group",
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
        edgeCases: ["two-of-three threshold rule", "current and prior year classification rule"],
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
      outputArtifacts: expectedOutcome === "manual-handoff" ? ["full accounts PDF text", "auditor handoff record"] : ["accounts PDF text", "iXBRL XML"],
      decisionGates: manualProfessionalReviewRequired ? ["signed auditor report", "manual handoff acceptance"] : ["named qualified-accountant review"],
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
      "28 visual smoke screenshots",
      "screenshot SHA-256 checksums",
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
