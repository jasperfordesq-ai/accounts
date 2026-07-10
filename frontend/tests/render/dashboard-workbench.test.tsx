import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { DashboardWorkbench } from "@/components/dashboard/DashboardWorkbench";
import type { Company, FilingDeadline, ProductionReadinessReport } from "@/lib/api";

describe("DashboardWorkbench", () => {
  it("wraps the accountant dashboard in the shared workbench shell", () => {
    render(
      <DashboardWorkbench
        companies={[sampleCompany()]}
        deadlines={{ 7: sampleDeadline() }}
        canCreateCompany
        canRecoverCompany
        canReviewReleaseEvidence
        readinessReport={sampleReadinessReport()}
        readinessError={null}
        error={null}
      />,
    );

    expect(screen.getByRole("heading", { name: "Firm command centre" })).toBeInTheDocument();
    expect(screen.getByText("Deadlines, reviewer ownership and the next action across the practice.")).toBeInTheDocument();
    expect(screen.getAllByText("1 company").length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText("Production gated")).toBeInTheDocument();
    for (const addLink of screen.getAllByRole("link", { name: "Add Company" })) {
      expect(addLink).toHaveAttribute("href", "/companies/new");
    }
    expect(screen.getByText("Accountant Work Queue")).toBeInTheDocument();
    expect(screen.getByText("Practice command summary")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Company directory" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Platform release status" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open release evidence" })).toHaveAttribute("href", "/production-readiness");
    const workQueue = screen.getByText("Accountant Work Queue");
    const releaseStatus = screen.getByRole("heading", { name: "Platform release status" });
    expect(workQueue.compareDocumentPosition(releaseStatus) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(screen.queryByText("Dashboard")).not.toBeInTheDocument();
  });

  it("keeps operational errors visible inside the workbench shell", () => {
    render(
      <DashboardWorkbench
        companies={[]}
        deadlines={{}}
        canCreateCompany={false}
        canRecoverCompany={false}
        canReviewReleaseEvidence
        readinessReport={null}
        readinessError="Readiness API unavailable"
        error="Companies API unavailable"
      />,
    );

    expect(screen.getByRole("heading", { name: "Firm command centre" })).toBeInTheDocument();
    expect(screen.getByText("Companies API unavailable")).toBeInTheDocument();
    expect(screen.getByText("Readiness API unavailable")).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Add Company" })).not.toBeInTheDocument();
  });

  it("gives Owners a recoverable quarantine register", () => {
    render(
      <DashboardWorkbench
        companies={[]}
        deadlines={{}}
        canCreateCompany
        canRecoverCompany
        canReviewReleaseEvidence={false}
        readinessReport={null}
        quarantinedCompanies={[{
          companyId: 19,
          legalName: "Retained Records Limited",
          quarantinedAtUtc: "2026-07-10T08:00:00Z",
          quarantinedByDisplayName: "Owner User",
          reason: "Engagement closed after the final retained-record review.",
          evidenceSha256: "a".repeat(64),
        }]}
      />,
    );

    expect(screen.getByRole("heading", { name: "Quarantined companies" })).toBeInTheDocument();
    expect(screen.getByText("Retained Records Limited")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Recover" })).toBeInTheDocument();
    expect(screen.queryByText("Production clear")).not.toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Platform release status" })).not.toBeInTheDocument();
  });
});

function sampleCompany(): Company {
  return {
    id: 7,
    legalName: "Connacht Visual Limited",
    companyType: "Private",
    incorporationDate: "2024-01-01",
    financialYearStartMonth: 1,
    annualReturnDate: "2026-09-15",
    isGroupMember: false,
    isHolding: false,
    isInvestment: false,
    isSubsidiary: false,
    isDormant: false,
    isTrading: true,
    isVatRegistered: false,
    isEmployer: false,
    hasStock: false,
    ownsAssets: false,
    hasBorrowings: false,
    hasDirectorLoans: false,
    isListedSecurities: false,
    isCreditInstitution: false,
    isInsuranceUndertaking: false,
    isPensionFund: false,
    isCharitableOrganisation: false,
    assignedReviewerName: "Niamh Reviewer",
    assignedReviewerEmail: "niamh.reviewer@example.ie",
    latestPeriod: {
      id: 3,
      companyId: 7,
      periodStart: "2026-01-01",
      periodEnd: "2026-12-31",
      status: "Review",
      isFirstYear: false,
      memberAuditNoticeReceived: false,
      goingConcernConfirmed: true,
    },
  };
}

function sampleDeadline(): FilingDeadline {
  return {
    id: 1,
    companyId: 7,
    periodId: 3,
    deadlineType: "CRO",
    calculatedDueDate: "2026-07-10",
    dueDate: "2026-07-10",
    isLate: false,
    penaltyAmount: 0,
  };
}

function sampleReadinessReport(): ProductionReadinessReport {
  return {
    generatedAt: "2026-07-06T08:00:00Z",
    overallStatus: "blocked",
    releaseDecision: "blocked",
    areas: [
      {
        code: "backend-code",
        label: "Backend code",
        status: "hardened",
        summary: "Rules and filing gates covered.",
        evidence: ["dotnet test"],
        nextActions: [],
      },
    ],
    goldenFilingCorpus: [
      {
        code: "micro-ltd",
        label: "Micro LTD",
        companyScope: "Private LTD micro entity",
        expectedOutcome: "generated-review-required",
        coverageStatus: "covered",
        fixture: {
          legalName: "Connacht Visual Limited",
          companyType: "Private",
          periodStart: "2026-01-01",
          periodEnd: "2026-12-31",
          expectedSizeClass: "Micro",
          expectedRegime: "Micro",
          auditExempt: true,
          manualProfessionalReviewRequired: false,
        },
        evidenceTestNames: ["FilingGoldenCorpusScenarioTests.MicroLtd"],
        evidenceVerifiers: [],
        assertions: ["PDF text"],
        evidencePack: {
          outputArtifacts: ["accounts-pdf"],
          decisionGates: ["named qualified-accountant review"],
          expectedValueChecks: ["balance-sheet-balances"],
          expectedOutputs: {
            pdfTextMarkers: ["Connacht Visual Limited"],
            ixbrlRequiredTags: ["core:EntityCurrentLegalOrRegisteredName"],
            filingReadinessState: "blocked",
            expectedCorporationTax: 0,
            requiredNotes: ["Accounting Policies"],
            filingGateStates: ["qualified-accountant review required"],
            signOffPacketState: "blocked",
          },
          expectedProofPoints: [],
          sourceReferences: [],
        },
        legalBasisSnapshot: {
          scenarioCode: "micro-ltd",
          companyType: "Private",
          sizeClass: "Micro",
          electedRegime: "Micro",
          auditExempt: true,
          manualProfessionalReviewRequired: false,
          legalBasis: "FRS 105 micro-entities regime with CRO financial-statement and Revenue iXBRL filing evidence.",
          requiredOutputs: ["accounts-pdf"],
          professionalGates: ["named qualified-accountant review"],
          sourceIds: ["cro-financial-statements-requirements"],
        },
      },
    ],
    goldenEvidenceLedger: [
      {
        scenarioCode: "micro-ltd",
        label: "Micro LTD",
        fixtureLegalName: "Connacht Visual Limited",
        filingReadinessState: "blocked",
        expectedCorporationTax: 0,
        signOffPacketState: "blocked",
        acceptanceStatus: "awaiting-review",
        blocksRelease: true,
        sourceIds: ["cro-financial-statements-requirements"],
        expectedValueChecks: ["balance-sheet-balances"],
        outputArtifacts: ["accounts-pdf"],
      },
    ],
    sourceLawSnapshot: {
      generatedAt: "2026-07-06T08:00:00Z",
      sourceCount: 1,
      sources: [],
    },
    assuranceActions: [
      {
        code: "qualified-accountant-signoff",
        label: "Qualified accountant sign-off",
        trackCode: "backend-code",
        priority: "critical",
        status: "blocked",
        evidenceRequired: "Named accountant acceptance on the golden corpus.",
        nextAction: "Run qualified-accountant acceptance.",
      },
    ],
    completionTracks: [
      {
        code: "backend-code",
        label: "Backend code",
        ownerRole: "Engineering",
        status: "in-progress",
        assuranceActionCodes: ["qualified-accountant-signoff"],
        nextActions: ["Run qualified-accountant acceptance."],
      },
    ],
    operationalGates: [
      {
        code: "qualified-accountant-review",
        label: "Qualified accountant review",
        status: "enforced",
        detail: "Final filing use requires named accountant approval.",
      },
    ],
    releaseBlockerRegister: [
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
        evidenceArtifact: "light-dark-mobile-tablet-desktop-screenshot-review",
        blocksRelease: true,
      },
    ],
  } as unknown as ProductionReadinessReport;
}
