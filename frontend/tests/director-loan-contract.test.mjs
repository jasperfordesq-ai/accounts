import assert from "node:assert/strict";
import test from "node:test";
import {
  directorLoanComplianceSchema,
  directorLoanRowSchema,
  parseApiContract,
} from "../src/lib/apiContracts.ts";

const legalSources = Array.from({ length: 13 }, (_, index) => ({
  code: `source-${index}`,
  title: `Statutory source ${index}`,
  url: `https://revisedacts.lawreform.ie/source/${index}`,
}));

const row = {
  id: 1,
  periodId: 2,
  directorId: 3,
  counterpartyType: "Director",
  counterpartyName: null,
  arrangementType: "Loan",
  arrangementDate: "2026-01-01",
  openingBalance: 100,
  advances: 50,
  repayments: 25,
  closingBalance: 125,
  termsStatus: "WrittenComplete",
  interestRate: 2,
  interestCharged: 2,
  allowanceMade: 0,
  section236PresumptionEvidenceReference: null,
  isDocumented: true,
  loanTerms: "Written repayment and interest terms",
  maxBalanceDuringYear: 150,
  complianceBasis: "Section240BelowTenPercent",
  relevantAssetsBasis: "LastLaidEntityFinancialStatements",
  relevantAssetsAmount: 100000,
  relevantAssetsAsOfDate: "2025-12-31",
  relevantAssetsReference: "last-laid-accounts.pdf#net-assets",
  noPriorFinancialStatementsConfirmed: false,
  relevantAssetsFallReview: "NoRelevantFall",
  relevantAssetsReductionAwarenessDate: null,
  termsAmendedDate: null,
  termsAmendmentEvidenceReference: null,
  exceptionEvidenceReference: null,
  sapDeclarationDate: null,
  sapResolutionDate: null,
  sapActivityStartDate: null,
  sapCroFilingDate: null,
  sapDeclarationReference: null,
  sapResolutionReference: null,
  sapCroFilingReference: null,
  sapDeclarationCoversSection203Matters: false,
  expenseIncurredDate: null,
  expenseDischargedDate: null,
  ordinaryCourseConfirmed: false,
  noMoreFavourableTermsConfirmed: false,
  reviewDecision: "Accepted",
  reviewNote: "Reviewed against retained statutory evidence.",
  reviewedBy: "A Reviewer",
  reviewerRole: "Accountant",
  reviewedAtUtc: "2026-07-10T09:00:00Z",
  balanceMovements: [
    { id: 1, directorLoanId: 1, movementDate: "2026-03-01", movementType: "Advance", amount: 50, evidenceReference: "bank#1" },
    { id: 2, directorLoanId: 1, movementDate: "2026-09-01", movementType: "Repayment", amount: 25, evidenceReference: "bank#2" },
  ],
};

const detail = {
  id: 1,
  counterpartyName: "A Director",
  relatedDirectorName: "A Director",
  counterpartyType: "Director",
  arrangementType: "Loan",
  arrangementDate: "2026-01-01",
  openingBalance: 100,
  advances: 50,
  repayments: 25,
  allowanceMade: 0,
  maxDuringYear: 150,
  closingBalance: 125,
  interestRate: 2,
  interestCharged: 2,
  section236PresumedInterest: 0,
  termsStatus: "WrittenComplete",
  mainConditions: "Written repayment and interest terms",
  complianceBasis: "Section240BelowTenPercent",
  relevantAssets: 100000,
  section240Threshold: 10000,
  section240StrictlyBelowThreshold: true,
  section307DisclosureRequired: false,
  reviewDecision: "Accepted",
  reviewedBy: "A Reviewer",
  reviewerRole: "Accountant",
  reviewedAtUtc: "2026-07-10T09:00:00Z",
  readyForFinalOutput: true,
  blockingIssues: [],
  warnings: [],
  balanceMovements: row.balanceMovements,
};

const compliance = {
  totalDirectorLoans: 125,
  aggregateMaximumExposure: 150,
  disclosureAggregateMaximumExposure: 150,
  disclosureOpeningNetAssets: 90000,
  disclosureClosingNetAssets: 100000,
  section236PresumedInterest: 0,
  hasUnresolvedComplianceBlockers: false,
  requiresAlternativeLegalBasis: false,
  loans: [detail],
  blockingIssues: [],
  warnings: [],
  signOffPacket: {
    state: "accepted",
    readyForArrangementReview: true,
    readyForFinalOutput: true,
    openBlockers: [],
    openWarnings: [],
    legalSources,
  },
  legalSources,
  warning: null,
};

test("director-loan row contract accepts reconciled dated evidence", () => {
  const parsed = parseApiContract(directorLoanRowSchema, row, "director-loan row");
  assert.equal(parsed.closingBalance, 125);
  assert.equal(parsed.balanceMovements.length, 2);
});

test("director-loan row contract rejects aggregates that drift from dated movements", () => {
  assert.throws(
    () => parseApiContract(directorLoanRowSchema, { ...row, advances: 51, closingBalance: 126 }, "director-loan row"),
    /Movement advances do not reconcile/,
  );
});

test("director-loan row contract rejects group-company rows attributed to an individual", () => {
  assert.throws(
    () => parseApiContract(directorLoanRowSchema, { ...row, counterpartyType: "GroupCompany", counterpartyName: "Group Limited" }, "director-loan row"),
    /cannot identify an individual director/,
  );
});

test("director-loan compliance contract ties breach and sign-off flags to blockers", () => {
  assert.equal(parseApiContract(directorLoanComplianceSchema, compliance, "director-loan compliance").signOffPacket.state, "accepted");
  assert.throws(
    () => parseApiContract(directorLoanComplianceSchema, { ...compliance, hasUnresolvedComplianceBlockers: true }, "director-loan compliance"),
    /Compliance-blocker flag must exactly reflect blocking issues/,
  );
  assert.throws(
    () => parseApiContract(directorLoanComplianceSchema, {
      ...compliance,
      signOffPacket: { ...compliance.signOffPacket, state: "evidence-incomplete" },
    }, "director-loan compliance"),
    /Only a blocker-free sign-off packet can be accepted/,
  );
});
