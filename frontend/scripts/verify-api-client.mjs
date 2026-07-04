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
  return {
    generatedAt: "2026-07-04T12:00:00Z",
    overallStatus: "review-required",
    companiesInDatabase: 1,
    periodsInDatabase: 1,
    sourceLawSnapshot: {
      snapshotDate: "2026-07-03",
      snapshotVersion: "irish-statutory-accounts-sources-2026-07-03",
      sources: [source("frc-frs-105", "FRC FRS 105 current edition and amendments")],
    },
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
        evidenceTestNames: ["AccountsWorkflowTests.GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl"],
        assertions: ["PDF text", "iXBRL parse"],
        evidencePack: {
          outputArtifacts: ["accounts PDF text"],
          decisionGates: ["named qualified-accountant review"],
          expectedValueChecks: ["well-formed iXBRL"],
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
    ],
    assuranceActions: [
      {
        code: "qualified-accountant-signoff",
        label: "Qualified accountant sign-off",
        owner: "Qualified accountant",
        priority: "critical",
        status: "required",
        detail: "No real filing pack can be treated as final until a named qualified accountant has approved it.",
        evidenceRequired: "Named accountant approval recorded against the period.",
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
    monitoringControls: [
      {
        code: "error-tracking",
        label: "Production error tracking",
        provider: "Sentry-compatible",
        required: true,
        productionSafetyGate: "Monitoring:ErrorTrackingDsn",
        evidenceCaptured: "Unhandled exceptions are routed to the configured production error-tracking provider.",
        verification: "Program.cs wires UseSentry and ProductionSafetyService blocks a missing DSN.",
      },
    ],
    visualQaCoverage: {
      artifactName: "visual-smoke-screenshots",
      enforcement: "ci-production-smoke",
      expectedScreenshotCount: 24,
      layoutChecks: ["browser-console-errors", "page-horizontal-overflow", "visible-text-overlap"],
      themes: ["light", "dark"],
      viewports: [{ name: "desktop", width: 1440, height: 1000 }],
      routes: [
        {
          code: "dashboard",
          label: "Dashboard",
          description: "Accountant queue and production readiness overview.",
          requiredText: "Production Readiness",
          openFilingTab: false,
        },
        {
          code: "workbench-preview",
          label: "Workbench preview",
          description: "Internal component preview for accountant workflow primitives and route states.",
          requiredText: "Workbench Component Preview",
          openFilingTab: false,
        },
      ],
    },
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
