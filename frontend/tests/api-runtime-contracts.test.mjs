import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import test from "node:test";
import {
  ApiContractError,
  accountingPeriodSchema,
  adjustmentSchema,
  accountantReviewSummarySchema,
  adjustmentSummarySchema,
  auditorReportSummarySchema,
  croSigningPackSummarySchema,
  directorsApprovalPageSummarySchema,
  directorsApprovalStateSchema,
  authUserSchema,
  balanceSheetSchema,
  charityInfoSchema,
  classificationPrerequisitesSchema,
  classificationResultSchema,
  companySchema,
  directorsReportSchema,
  filingReadinessProfileSchema,
  filingWorkflowStatusSchema,
  importResultSchema,
  mfaChallengeSchema,
  notesDisclosureSchema,
  parseApiContract,
  payrollSummarySchema,
  profitAndLossSchema,
  sizeClassificationSchema,
  sofaSchema,
  transactionPageSchema,
  yearEndSummarySchema,
} from "../src/lib/apiContracts.ts";
import {
  EvidenceUploadTooLargeError,
  assertEvidenceUploadFits,
  directorsApprovalPageUrl,
  generateDirectorsApprovalPage,
  getAdjustmentSummary,
  getClassificationPrerequisites,
  getCompanies,
  getCompany,
  getDirectorsApproval,
  getDirectorsReportData,
  getFilingReadinessProfile,
  getFilingWorkflowStatus,
  getCorporationTaxFilingSupport,
  getNotes,
  getPayroll,
  getPeriod,
  getProfitAndLoss,
  getSofa,
  getTransactions,
  getYearEndSummary,
  readEvidenceFile,
  recordAccountantReview,
  recordAuditorReport,
  recordCroSigningPack,
  recordDirectorsApproval,
  withdrawDirectorsApproval,
} from "../src/lib/api.ts";
import { getArchivedHistoricFigures, recordHistoricSizeFigures, retireHistoricSizeFigures } from "../src/lib/historicSizeFigures.ts";
import { confirmCorporationTaxDue } from "../src/lib/corporationTaxDue.ts";

const parse = (schema, payload, name = "test") => parseApiContract(schema, payload, name);

test("company and period contracts reject missing fields, unknown enums, and impossible dates", () => {
  const company = companyFixture();
  assert.equal(parse(companySchema, company).legalName, "Contract Ltd");
  const exemptCompany = parse(companySchema, {
    ...company,
    holdsCharitableTaxExemption: true,
    charitableTaxExemptionReference: "CHY-12345",
    charitableTaxExemptionConfirmedDate: "2022-12-16",
  });
  assert.equal(exemptCompany.holdsCharitableTaxExemption, true);
  assert.equal(exemptCompany.charitableTaxExemptionReference, "CHY-12345");

  const missing = structuredClone(company);
  delete missing.legalName;
  assert.throws(() => parse(companySchema, missing), /legalName/);

  assert.throws(
    () => parse(companySchema, { ...company, companyType: "MysteryEntity" }),
    /companyType/,
  );
  assert.throws(
    () => parse(companySchema, { ...company, incorporationDate: "2026-02-30" }),
    /incorporationDate/,
  );

  const period = periodFixture();
  assert.equal(parse(accountingPeriodSchema, period).status, "Draft");
  assert.throws(
    () => parse(accountingPeriodSchema, { ...period, status: "Closed" }),
    /status/,
  );
  assert.throws(
    () => parse(accountingPeriodSchema, { ...period, periodStart: "2027-01-01" }),
    /periodEnd/,
  );
});

test("size classification contract distinguishes saved inputs from a recorded decision", () => {
  // Live defect 2026-10-10: inputs saved without a decision were served with column defaults
  // (0 years, "Micro") and the period workspace refused to render on every load.
  const period = periodFixture();
  const inputs = { id: 1, turnover: 1640, balanceSheetTotal: 22358, avgEmployees: 0, thresholdElectionEffectiveFrom: "2024-01-01" };
  const unclassified = parse(accountingPeriodSchema, {
    ...period,
    sizeClassification: {
      ...inputs,
      calculatedClass: null,
      rawCurrentClass: null,
      periodLengthInYears: null,
      annualisedTurnover: null,
      decisionInputFingerprintSha256: null,
    },
  });
  assert.equal(unclassified.sizeClassification.calculatedClass, undefined);
  assert.equal(unclassified.sizeClassification.avgEmployees, 0);

  // The shape the defective API produced stays rejected: a default is not a decision.
  assert.throws(
    () => parse(accountingPeriodSchema, {
      ...period,
      sizeClassification: { ...inputs, calculatedClass: "Micro", rawCurrentClass: "Micro", periodLengthInYears: 0, annualisedTurnover: 0 },
    }),
    /periodLengthInYears/,
  );

  const decision = {
    ...inputs,
    calculatedClass: "Micro",
    rawCurrentClass: "Micro",
    periodLengthInYears: 1,
    annualisedTurnover: 1640,
    decisionInputFingerprintSha256: "a".repeat(64),
  };
  assert.equal(parse(accountingPeriodSchema, { ...period, sizeClassification: decision }).sizeClassification.calculatedClass, "Micro");
  assert.throws(
    () => parse(accountingPeriodSchema, { ...period, sizeClassification: { ...decision, periodLengthInYears: null } }),
    /periodLengthInYears/,
  );

  const result = {
    calculatedClass: "Micro",
    qualificationNotes: "First financial year",
    canUseMicro: true,
    canFileAbridged: true,
    auditExempt: true,
    availableRegimes: ["Micro"],
    isIneligibleEntity: false,
    rawCurrentClass: "Micro",
    annualisedTurnover: 638,
    periodLengthInYears: 1.4483,
  };
  assert.equal(parse(classificationResultSchema, result).periodLengthInYears, 1.4483);
  assert.throws(() => parse(classificationResultSchema, { ...result, periodLengthInYears: 0 }), /periodLengthInYears/);
});

test("classification prerequisites contract requires an explicit blocking period and a consistent ready flag", async () => {
  const blocked = {
    ready: false,
    isFirstYear: false,
    firstBlockingPeriod: { periodId: 1, periodStart: "2017-07-20", periodEnd: "2018-12-30", reason: "not classified yet" },
    message: "Classify 1 Aug 2024 – 31 Jul 2025 first.",
  };
  assert.equal(parse(classificationPrerequisitesSchema, blocked).firstBlockingPeriod.periodId, 1);
  const ready = { ready: true, isFirstYear: true, firstBlockingPeriod: null, message: "First financial year." };
  assert.equal(parse(classificationPrerequisitesSchema, ready).firstBlockingPeriod, null);
  const missing = { ...ready };
  delete missing.firstBlockingPeriod;
  assert.throws(() => parse(classificationPrerequisitesSchema, missing), /firstBlockingPeriod/);
  assert.throws(
    () => parse(classificationPrerequisitesSchema, { ...blocked, ready: true }),
    /firstBlockingPeriod/,
  );
  assert.throws(
    () => parse(classificationPrerequisitesSchema, { ...blocked, firstBlockingPeriod: { ...blocked.firstBlockingPeriod, periodEnd: "2017-01-01" } }),
    /periodEnd/,
  );

  const originalFetch = globalThis.fetch;
  let requested = "";
  globalThis.fetch = async (url) => {
    requested = String(url);
    return new Response(JSON.stringify({ ...blocked, ready: "no" }), {
      status: 200,
      headers: { "Content-Type": "application/json" },
    });
  };
  try {
    await assert.rejects(
      () => getClassificationPrerequisites(1, 9),
      (error) => error instanceof ApiContractError
        && error.contract === "classification prerequisites"
        && error.issuePath === "ready",
    );
    assert.match(requested, /\/api\/companies\/1\/periods\/9\/classification\/prerequisites$/);
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("transaction pagination and aggregate contracts fail closed on drift", () => {
  const page = transactionPageFixture();
  assert.equal(parse(transactionPageSchema, page).items.length, 2);

  const malformedMoney = structuredClone(page);
  malformedMoney.items[0].amount = "12.50";
  assert.throws(() => parse(transactionPageSchema, malformedMoney), /items\.0\.amount/);

  const unknownSort = { ...page, sortBy: "bank" };
  assert.throws(() => parse(transactionPageSchema, unknownSort), /sortBy/);

  const brokenAggregate = structuredClone(page);
  brokenAggregate.aggregates.categorised = 2;
  assert.throws(() => parse(transactionPageSchema, brokenAggregate), /aggregates/);

  const brokenNavigation = { ...page, hasNextPage: true };
  assert.throws(() => parse(transactionPageSchema, brokenNavigation), /hasNextPage/);
});

test("CSV import contract preserves canonical response keys and reconciles counts", () => {
  const result = {
    totalRows: 12,
    importedRows: 10,
    duplicateCandidates: 2,
    autoCategorised: 7,
    warnings: [],
    importBatchId: 91,
    sourceFilename: "bank-january.csv",
    sourceFileSha256: "a".repeat(64),
    sourceFileBytes: 2048,
  };
  assert.equal(parse(importResultSchema, result).importedRows, 10);
  assert.throws(
    () => parse(importResultSchema, { ...result, importedRows: 13 }),
    /totalRows/,
  );
  assert.throws(
    () => parse(importResultSchema, { ...result, autoCategorised: 11 }),
    /autoCategorised/,
  );
});

test("year-end and adjustment aggregates reject internally inconsistent responses", () => {
  const yearEnd = yearEndFixture();
  assert.equal(parse(yearEndSummarySchema, yearEnd).completeness.score, 50);

  const wrongScore = structuredClone(yearEnd);
  wrongScore.completeness.score = 75;
  assert.throws(() => parse(yearEndSummarySchema, wrongScore), /completeness\.score/);

  // Live defect 2026-10-10: a charity with no staff has no payroll. The API omitted the key, so
  // the summary failed its contract. No payroll is now explicit and a missing key stays invalid.
  assert.equal(parse(yearEndSummarySchema, yearEnd).payroll.hasPayroll, false);
  const withPayroll = parse(yearEndSummarySchema, { ...yearEnd, payroll: { hasPayroll: true, grossWages: 1200, staffCount: 1 } });
  assert.equal(withPayroll.payroll.grossWages, 1200);
  const omitted = structuredClone(yearEnd);
  delete omitted.payroll;
  assert.throws(() => parse(yearEndSummarySchema, omitted), /payroll/);
  assert.throws(() => parse(yearEndSummarySchema, { ...yearEnd, payroll: { hasPayroll: true } }), /payroll/);

  const adjustment = adjustmentFixture();
  assert.equal(parse(adjustmentSchema, adjustment).source, "Manual");
  assert.throws(
    () => parse(adjustmentSchema, { ...adjustment, isAuto: true }),
    /isAuto/,
  );
  assert.throws(
    () => parse(adjustmentSchema, { ...adjustment, approvedAt: null }),
    /approvedAt/,
  );

  const summary = {
    autoGenerated: 2,
    manual: 1,
    pendingApproval: 1,
    approved: 2,
    totalImpactOnProfit: 25,
    totalImpactOnAssets: 0,
  };
  assert.equal(parse(adjustmentSummarySchema, summary).approved, 2);
  assert.throws(
    () => parse(adjustmentSummarySchema, { ...summary, approved: 1 }),
    /do not reconcile/,
  );
});

test("optional payroll evidence distinguishes an empty working paper from transport and contract failures", async () => {
  const originalFetch = globalThis.fetch;
  try {
    globalThis.fetch = async () => new Response("null", {
      status: 200,
      headers: { "Content-Type": "application/json" },
    });
    assert.equal(await getPayroll(3, 7), null);

    globalThis.fetch = async () => new Response(JSON.stringify({
      grossWages: -1,
      directorsFees: 0,
      employerPrsi: 0,
      pensionContributions: 0,
      staffCount: 0,
    }), {
      status: 200,
      headers: { "Content-Type": "application/json" },
    });
    await assert.rejects(
      () => getPayroll(3, 7),
      (error) => error instanceof ApiContractError
        && error.contract === "payroll summary"
        && error.issuePath === "grossWages",
    );

    globalThis.fetch = async () => new Response("", { status: 404 });
    await assert.rejects(() => getPayroll(99, 7), /requested resource was not found/i);
  } finally {
    globalThis.fetch = originalFetch;
  }

  assert.equal(parse(payrollSummarySchema.nullable(), null), null);
});

test("financial statement contracts reconcile every displayed subtotal", () => {
  const profitAndLoss = profitAndLossFixture();
  assert.equal(parse(profitAndLossSchema, profitAndLoss).otherIncome, 500);

  assert.throws(
    () => parse(profitAndLossSchema, { ...profitAndLoss, profitAfterTax: 999 }),
    /profitAfterTax/,
  );
  const missingOtherIncome = structuredClone(profitAndLoss);
  delete missingOtherIncome.otherIncome;
  assert.throws(() => parse(profitAndLossSchema, missingOtherIncome), /otherIncome/);

  const balanceSheet = balanceSheetFixture();
  assert.equal(parse(balanceSheetSchema, balanceSheet).balances, true);
  const wrongNetAssets = structuredClone(balanceSheet);
  wrongNetAssets.netAssets = 9_999;
  assert.throws(() => parse(balanceSheetSchema, wrongNetAssets), /netAssets/);
});

test("charity balance sheet contract reconciles the bank overdraft and members' funds", () => {
  const balanceSheet = balanceSheetFixture();
  balanceSheet.currentAssets = { ...balanceSheet.currentAssets, cash: 1_200, total: 1_700 };
  balanceSheet.creditorsWithinYear = { ...balanceSheet.creditorsWithinYear, bankOverdraft: 200, total: 700 };
  balanceSheet.membersFunds = {
    fundsBroughtForward: 750,
    surplusForYear: 1_000,
    otherMovements: 0,
    restrictedFunds: 150,
    total: 2_000,
  };
  const parsed = parse(balanceSheetSchema, balanceSheet);
  assert.equal(parsed.creditorsWithinYear.bankOverdraft, 200);
  assert.equal(parsed.membersFunds.restrictedFunds, 150);

  const unreconciledOverdraft = structuredClone(balanceSheet);
  unreconciledOverdraft.creditorsWithinYear.bankOverdraft = 300;
  assert.throws(() => parse(balanceSheetSchema, unreconciledOverdraft), /creditorsWithinYear/);
  const unreconciledFunds = structuredClone(balanceSheet);
  unreconciledFunds.membersFunds.restrictedFunds = 999;
  assert.throws(() => parse(balanceSheetSchema, unreconciledFunds), /membersFunds/);
});

test("directors report contract requires dated service and retained statutory evidence", () => {
  const report = directorsReportFixture();
  assert.equal(parse(directorsReportSchema, report).directorServicePeriods[0].name, "Named Director");

  assert.throws(
    () => parse(directorsReportSchema, { ...report, directorNames: ["Different Director"] }),
    /directorNames/,
  );
  const futureDirector = structuredClone(report);
  futureDirector.directorServicePeriods[0].appointedDate = "2027-01-01";
  assert.throws(() => parse(directorsReportSchema, futureDirector), /did not serve/);
  const futureSecretary = structuredClone(report);
  futureSecretary.secretaryServicePeriod.appointedDate = "2027-01-01";
  assert.throws(() => parse(directorsReportSchema, futureSecretary), /Secretary did not serve/);
  assert.throws(
    () => parse(directorsReportSchema, {
      ...report,
      principalActivities: "Normal trading activities.",
      principalActivitiesReviewed: false,
      principalActivitiesReviewedBy: null,
      principalActivitiesReviewedAt: null,
    }),
    /visibly marked/,
  );
  assert.throws(
    () => parse(directorsReportSchema, {
      ...report,
      auditInformationEvidenceRecorded: false,
      auditInformationConfirmedBy: null,
      auditInformationConfirmedAt: null,
    }),
    /Audit-information statement lacks complete director evidence/,
  );
});

test("notes and charity/SORP contracts reject missing and unreconciled disclosure data", () => {
  const note = {
    id: 1,
    periodId: 7,
    noteNumber: 1,
    code: "ACC-POLICIES",
    title: "Accounting policies",
    content: "Prepared under FRS 102.",
    isRequired: true,
    isIncluded: true,
    checklistState: "Required",
  };
  assert.equal(parse(notesDisclosureSchema, note).noteNumber, 1);
  assert.throws(() => parse(notesDisclosureSchema, { ...note, noteNumber: 0 }), /noteNumber/);

  const charity = charityInfoFixture();
  assert.equal(parse(charityInfoSchema, charity).sorpTier, 1);
  assert.throws(() => parse(charityInfoSchema, { ...charity, governanceReviewedBy: null }), /governanceEvidenceReference/);
  assert.equal(parse(charityInfoSchema, {
    ...charity,
    governanceCodeCompliant: null,
    governanceCodeNote: null,
    governanceEvidenceReference: null,
    governanceReviewedBy: null,
    governanceReviewedAtUtc: null,
    governanceEvidenceArtifactSha256: null,
  }).governanceCodeCompliant, null);

  const sofa = sofaFixture();
  assert.equal(parse(sofaSchema, sofa).totalClosingFunds, 1_250);
  assert.throws(() => parse(sofaSchema, { ...sofa, totalClosingFunds: 1_251 }), /totalClosingFunds/);
});

test("filing workflow and readiness contracts enforce support boundaries and exact enums", () => {
  const workflow = filingWorkflowFixture();
  assert.equal(parse(filingWorkflowStatusSchema, workflow).readyToFile, false);
  assert.throws(
    () => parse(filingWorkflowStatusSchema, { ...workflow, readyToFile: true }),
    /readyToFile/,
  );
  const unknownStatus = structuredClone(workflow);
  unknownStatus.cro.status = "Filed";
  assert.throws(() => parse(filingWorkflowStatusSchema, unknownStatus), /cro\.status/);
  const falseRevenueClaim = structuredClone(workflow);
  falseRevenueClaim.revenue.ixbrlValid = true;
  assert.throws(() => parse(filingWorkflowStatusSchema, falseRevenueClaim), /generationSupport/);

  const readiness = filingReadinessFixture();
  assert.equal(parse(filingReadinessProfileSchema, readiness).directCroSubmissionSupported, false);
  assert.throws(
    () => parse(filingReadinessProfileSchema, { ...readiness, directCroSubmissionSupported: true }),
    /directCroSubmissionSupported/,
  );
});

test("filing readiness profile: accountant review is never mandatory and the board approves", () => {
  const profile = filingReadinessFixture();
  assert.equal(parse(filingReadinessProfileSchema, profile).approvalAuthority, "board-of-directors-s324");
  assert.throws(() => parse(filingReadinessProfileSchema, { ...profile, accountantReviewRequired: true }), /accountantReviewRequired/);
  assert.throws(() => parse(filingReadinessProfileSchema, { ...profile, approvalAuthority: "qualified-accountant" }), /approvalAuthority/);
  const missing = structuredClone(profile);
  delete missing.approvalAuthority;
  assert.throws(() => parse(filingReadinessProfileSchema, missing), /approvalAuthority/);
  const renamedState = structuredClone(profile);
  renamedState.signOffPacket.state = "ready-for-accountant-review";
  assert.throws(() => parse(filingReadinessProfileSchema, renamedState), /signOffPacket/);
});

const HASH_A = "a".repeat(64);
const HASH_B = "b".repeat(64);

function directorsPageFixture(overrides = {}) {
  return {
    id: 4,
    approvalDate: "2026-03-01",
    firstDirectorName: "Director One",
    secondDirectorName: "Director Two",
    directorsInOfficeCount: 2,
    resolutionReference: "Board minute 2026/03",
    pageSha256: HASH_A,
    accountsPdfSha256: HASH_B,
    recordSha256: HASH_A,
    generatedAtUtc: "2026-03-01T10:00:00.1234567Z",
    ...overrides,
  };
}

test("directors approval state mirrors the backend: nullable members are explicit nulls", () => {
  const empty = { eligibleDirectors: [], latestPage: null, current: null, periodApprovalDate: null, history: [] };
  assert.equal(parse(directorsApprovalStateSchema, empty).current, null);
  for (const key of Object.keys(empty)) {
    const missing = structuredClone(empty);
    delete missing[key];
    assert.throws(() => parse(directorsApprovalStateSchema, missing), new RegExp(key));
  }

  const full = {
    eligibleDirectors: [{ officerId: 1, name: "Director One" }, { officerId: 2, name: "Director Two" }],
    latestPage: directorsPageFixture(),
    current: {
      eventId: 9,
      page: directorsPageFixture(),
      signedPageSha256: HASH_A,
      resolutionSha256: HASH_B,
      recordedByDisplayName: "Reviewer One",
      recordedAtUtc: "2026-03-02T09:00:00Z",
      recordSha256: HASH_B,
    },
    periodApprovalDate: "2026-03-01",
    history: [
      { eventId: 10, kind: "Revoked", approvalDate: "2026-03-01", reason: "withdrawn-superseded", recordedByDisplayName: "Reviewer One", recordedAtUtc: "2026-03-03T09:00:00Z", recordSha256: HASH_A },
      { eventId: 9, kind: "Approved", approvalDate: "2026-03-01", reason: null, recordedByDisplayName: "Reviewer One", recordedAtUtc: "2026-03-02T09:00:00Z", recordSha256: HASH_B },
    ],
  };
  assert.equal(parse(directorsApprovalStateSchema, full).current.eventId, 9);
  assert.equal(parse(directorsApprovalStateSchema, full).history[0].reason, "withdrawn-superseded");
  assert.throws(
    () => parse(directorsApprovalStateSchema, { ...full, history: [{ ...full.history[1], reason: "withdrawn-superseded" }] }),
    /reason/,
  );
  assert.throws(
    () => parse(directorsApprovalStateSchema, { ...full, latestPage: directorsPageFixture({ pageSha256: "short" }) }),
    /pageSha256/,
  );
});

test("directors approval page enforces one signatory for a sole director and two otherwise", () => {
  assert.equal(parse(directorsApprovalPageSummarySchema, directorsPageFixture()).directorsInOfficeCount, 2);
  assert.equal(
    parse(directorsApprovalPageSummarySchema, directorsPageFixture({ secondDirectorName: null, directorsInOfficeCount: 1 })).secondDirectorName,
    null,
  );
  assert.throws(
    () => parse(directorsApprovalPageSummarySchema, directorsPageFixture({ secondDirectorName: null })),
    /secondDirectorName/,
  );
  assert.throws(
    () => parse(directorsApprovalPageSummarySchema, directorsPageFixture({ directorsInOfficeCount: 1 })),
    /secondDirectorName/,
  );
});

test("signing pack, auditor report and accountant review summaries validate their hashes and nullability", () => {
  assert.equal(parse(croSigningPackSummarySchema, {
    signedByDirector: "Director One",
    signedBySecretary: "Secretary One",
    signedAtUtc: "2026-03-03T11:00:00Z",
    signedPdfSha256: HASH_A,
  }).signedByDirector, "Director One");
  assert.throws(() => parse(croSigningPackSummarySchema, {
    signedByDirector: "Director One",
    signedBySecretary: "Secretary One",
    signedAtUtc: "2026-03-03T11:00:00Z",
    signedPdfSha256: "zz",
  }), /signedPdfSha256/);
  assert.equal(parse(auditorReportSummarySchema, {
    reportReference: "AR-1",
    firmName: "Firm One",
    signerName: "Signer One",
    signedAtUtc: "2026-03-03T11:00:00Z",
    reportSha256: HASH_B,
  }).reportReference, "AR-1");
  const review = {
    reviewerName: "Reviewer One",
    professionalBody: "ACCA",
    membershipNumber: "12345",
    reviewDate: "2026-03-04",
    reviewLetterSha256: null,
    state: "Optional review recorded: Reviewer One (ACCA, 12345)",
  };
  assert.equal(parse(accountantReviewSummarySchema, review).reviewLetterSha256, null);
  const missing = structuredClone(review);
  delete missing.reviewLetterSha256;
  assert.throws(() => parse(accountantReviewSummarySchema, missing), /reviewLetterSha256/);
});

test("evidence uploads that would exceed the proxy body limit are refused in the browser with a clear message", async () => {
  const originalFetch = globalThis.fetch;
  let fetched = 0;
  globalThis.fetch = async () => {
    fetched += 1;
    return new Response("{}", { status: 200, headers: { "Content-Type": "application/json" } });
  };
  try {
    const megabytes = (count) => new Blob([new Uint8Array(Math.round(count * 1024 * 1024))], { type: "application/pdf" });
    // Each alone fits; together their base64 encoding exceeds the 5 MB proxy limit.
    assert.doesNotThrow(() => assertEvidenceUploadFits([megabytes(2)]));
    assert.doesNotThrow(() => assertEvidenceUploadFits([megabytes(3.6), null]));
    await assert.rejects(
      () => recordDirectorsApproval(1, 2, { approvalPageId: 4, signedPage: megabytes(2), resolution: megabytes(2) }),
      (error) => error instanceof EvidenceUploadTooLargeError
        && error.message === "These files are too large to upload together (max about 3.7 MB). Please compress or rescan at a lower resolution.",
    );
    const single = {
      name: "EvidenceUploadTooLargeError",
      message: "This file is too large to upload (max about 3.7 MB). Please compress or rescan at a lower resolution.",
    };
    await assert.rejects(
      () => recordCroSigningPack(1, 2, { directorOfficerId: 1, secretaryOfficerId: 3, signedAtUtc: "2026-03-03T11:00:00Z", signedPack: megabytes(4) }),
      single,
    );
    await assert.rejects(
      () => recordAuditorReport(1, 2, {
        firmName: "Firm One", signerName: "Signer One", professionalBody: "ACCA", membershipNumber: "12345",
        reportReference: "AR-1", signedAtUtc: "2026-03-03T11:00:00Z", acceptedByDirectors: true, report: megabytes(4),
      }),
      single,
    );
    await assert.rejects(
      () => recordAccountantReview(1, 2, {
        reviewerName: "Reviewer One", professionalBody: "ACCA", membershipNumber: "12345", reviewDate: "2026-03-04", reviewLetter: megabytes(4),
      }),
      single,
    );
    assert.equal(fetched, 0, "nothing is sent when the upload would not fit");
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("directors approval client calls use the filing routes, send base64 evidence with SHA-256 and validate responses", async () => {
  const originalFetch = globalThis.fetch;
  const requests = [];
  const responses = [];
  globalThis.fetch = async (url, init) => {
    requests.push({ url: String(url), method: (init?.method ?? "GET").toUpperCase(), body: init?.body ? JSON.parse(String(init.body)) : undefined });
    return new Response(JSON.stringify(responses.shift()), { status: 200, headers: { "Content-Type": "application/json" } });
  };
  try {
    const bytes = new Uint8Array([1, 2, 3, 250, 251, 252]);
    const file = new File([bytes], "evidence.pdf", { type: "application/pdf" });
    const payload = await readEvidenceFile(file);
    assert.equal(payload.base64, Buffer.from(bytes).toString("base64"));
    assert.equal(payload.sha256, createHash("sha256").update(bytes).digest("hex"));

    const base = "/api/companies/1/periods/2/filing";
    responses.push({ eligibleDirectors: [], latestPage: null, current: null, periodApprovalDate: null, history: [] });
    await getDirectorsApproval(1, 2, "2026-03-01");
    assert.equal(requests.at(-1).url, base + "/directors-approval?onDate=2026-03-01");

    responses.push(directorsPageFixture());
    await generateDirectorsApprovalPage(1, 2, {
      approvalDate: "2026-03-01",
      firstDirectorOfficerId: 1,
      secondDirectorOfficerId: null,
      resolutionReference: "Board minute 2026/03",
    });
    assert.equal(requests.at(-1).url, base + "/directors-approval-page");
    assert.equal(requests.at(-1).body.secondDirectorOfficerId, null);
    assert.equal(directorsApprovalPageUrl(1, 2, 4), base + "/directors-approval-page/4");

    responses.push({
      eventId: 9, page: directorsPageFixture(), signedPageSha256: HASH_A, resolutionSha256: HASH_B,
      recordedByDisplayName: "Reviewer One", recordedAtUtc: "2026-03-02T09:00:00Z", recordSha256: HASH_B,
    });
    await recordDirectorsApproval(1, 2, { approvalPageId: 4, signedPage: file, resolution: file });
    assert.equal(requests.at(-1).url, base + "/directors-approval");
    assert.deepEqual(Object.keys(requests.at(-1).body).sort(), [
      "approvalPageId", "resolutionArtifact", "resolutionSha256", "signedPageArtifact", "signedPageSha256",
    ]);
    assert.equal(requests.at(-1).body.signedPageSha256, payload.sha256);

    responses.push({
      eventId: 10, kind: "Revoked", approvalDate: "2026-03-01", reason: "withdrawn-entered-in-error",
      recordedByDisplayName: "Reviewer One", recordedAtUtc: "2026-03-02T10:00:00Z", recordSha256: HASH_A,
    });
    await withdrawDirectorsApproval(1, 2, { reason: "entered-in-error", note: "Recorded against the wrong minute." });
    assert.equal(requests.at(-1).url, base + "/directors-approval-withdrawal");
    assert.equal(requests.at(-1).method, "POST");
    assert.deepEqual(requests.at(-1).body, { reason: "entered-in-error", note: "Recorded against the wrong minute." });

    // A revocation must carry its reason; an approval must not.
    responses.push({
      eventId: 10, kind: "Revoked", approvalDate: "2026-03-01", reason: null,
      recordedByDisplayName: "Reviewer One", recordedAtUtc: "2026-03-02T10:00:00Z", recordSha256: HASH_A,
    });
    await assert.rejects(() => withdrawDirectorsApproval(1, 2, { reason: "superseded", note: "A new approval follows." }), ApiContractError);

    responses.push({
      eligibleDirectors: [], latestPage: null, current: null, periodApprovalDate: null,
      history: [{ eventId: 9, kind: "Deleted", approvalDate: "2026-03-01", reason: null, recordedByDisplayName: "Reviewer One", recordedAtUtc: "2026-03-02T09:00:00Z", recordSha256: HASH_B }],
    });
    await assert.rejects(() => getDirectorsApproval(1, 2), ApiContractError);

    responses.push({ signedByDirector: "Director One", signedBySecretary: "Secretary One", signedAtUtc: "2026-03-03T11:00:00Z", signedPdfSha256: HASH_A });
    await recordCroSigningPack(1, 2, { directorOfficerId: 1, secretaryOfficerId: 3, signedAtUtc: "2026-03-03T11:00:00Z", signedPack: file });
    assert.equal(requests.at(-1).url, base + "/cro-signing-pack");
    assert.equal(requests.at(-1).body.signedArtifact, payload.base64);

    responses.push({ reportReference: "AR-1", firmName: "Firm One", signerName: "Signer One", signedAtUtc: "2026-03-03T11:00:00Z", reportSha256: HASH_B });
    await recordAuditorReport(1, 2, {
      firmName: "Firm One", signerName: "Signer One", professionalBody: "ACCA", membershipNumber: "12345",
      reportReference: "AR-1", signedAtUtc: "2026-03-03T11:00:00Z", acceptedByDirectors: true, report: file,
    });
    assert.equal(requests.at(-1).url, base + "/auditor-report");
    assert.equal(requests.at(-1).body.acceptedByDirectors, true);
    assert.equal("reviewedBy" in requests.at(-1).body, false);

    responses.push({ reviewerName: "Reviewer One", professionalBody: "ACCA", membershipNumber: "12345", reviewDate: "2026-03-04", reviewLetterSha256: null, state: "Optional review recorded: Reviewer One (ACCA, 12345)" });
    await recordAccountantReview(1, 2, { reviewerName: "Reviewer One", professionalBody: "ACCA", membershipNumber: "12345", reviewDate: "2026-03-04" });
    assert.equal(requests.at(-1).url, base + "/accountant-review");
    assert.equal(requests.at(-1).body.reviewLetter, null);
    assert.equal(requests.at(-1).body.reviewLetterSha256, null);

    responses.push({ eligibleDirectors: [], latestPage: null, current: null });
    await assert.rejects(() => getDirectorsApproval(1, 2), ApiContractError);
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("auth contract rejects role drift, malformed identities, and duplicate scope", () => {
  const user = {
    userId: 1,
    tenantId: 2,
    tenantName: "Practice",
    tenantSlug: "practice",
    email: "owner@example.ie",
    displayName: "Named Owner",
    role: "Owner",
    allowedCompanyIds: [],
    mustChangePassword: false,
  };
  assert.equal(parse(authUserSchema, user).role, "Owner");
  assert.throws(() => parse(authUserSchema, { ...user, role: "Administrator" }), /role/);
  assert.throws(() => parse(authUserSchema, { ...user, email: "not-an-email" }), /email/);
  assert.throws(() => parse(authUserSchema, { ...user, tenantSlug: "Not a slug" }), /tenantSlug/);
  assert.throws(
    () => parse(authUserSchema, { ...user, role: "Reviewer", allowedCompanyIds: [7, 7] }),
    /allowedCompanyIds/,
  );
});

test("MFA challenge contract requires explicit non-enrollment nulls and rejects inconsistent enrollment", () => {
  const loginChallenge = parse(mfaChallengeSchema, {
    challengeToken: "c".repeat(48),
    requiresEnrollment: false,
    expiresAtUtc: "2026-07-11T02:30:00Z",
    enrollmentSecret: null,
    otpAuthUri: null,
  });
  assert.equal(loginChallenge.enrollmentSecret, null);
  assert.equal(loginChallenge.otpAuthUri, null);

  assert.throws(
    () => parse(mfaChallengeSchema, {
      challengeToken: "c".repeat(48),
      requiresEnrollment: false,
      expiresAtUtc: "2026-07-11T02:30:00Z",
    }),
    /enrollmentSecret/,
  );
  assert.throws(
    () => parse(mfaChallengeSchema, {
      challengeToken: "c".repeat(48),
      requiresEnrollment: true,
      expiresAtUtc: "2026-07-11T02:30:00Z",
      enrollmentSecret: null,
      otpAuthUri: null,
    }),
    /requiresEnrollment/,
  );
  assert.throws(
    () => parse(mfaChallengeSchema, {
      challengeToken: "c".repeat(48),
      requiresEnrollment: false,
      expiresAtUtc: "2026-07-11T02:30:00Z",
      enrollmentSecret: "JBSWY3DPEHPK3PXP",
      otpAuthUri: "otpauth://totp/FilingBridge:owner@example.ie?secret=JBSWY3DPEHPK3PXP",
    }),
    /requiresEnrollment/,
  );
});

test("critical API callers surface ApiContractError rather than consuming malformed JSON", async () => {
  const originalFetch = globalThis.fetch;
  globalThis.fetch = async () => new Response(JSON.stringify([{ ...companyFixture(), legalName: 42 }]), {
    status: 200,
    headers: { "Content-Type": "application/json" },
  });

  try {
    await assert.rejects(
      () => getCompanies(),
      (error) => error instanceof ApiContractError
        && error.contract === "company list"
        && error.issuePath === "0.legalName",
    );
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("every critical client family is behaviorally wired to a fail-closed runtime contract", async () => {
  const originalFetch = globalThis.fetch;
  globalThis.fetch = async () => new Response(JSON.stringify({}), {
    status: 200,
    headers: { "Content-Type": "application/json" },
  });
  const criticalCalls = [
    ["company", () => getCompany(3)],
    ["period", () => getPeriod(3, 7)],
    ["transaction", () => getTransactions(3, 7)],
    ["year-end", () => getYearEndSummary(3, 7)],
    ["adjustment", () => getAdjustmentSummary(3, 7)],
    ["statement", () => getProfitAndLoss(3, 7)],
    ["directors report", () => getDirectorsReportData(3, 7)],
    ["notes", () => getNotes(3, 7)],
    ["charity/SORP", () => getSofa(3, 7)],
    ["filing workflow", () => getFilingWorkflowStatus(3, 7)],
    ["filing readiness", () => getFilingReadinessProfile(3, 7)],
    ["Corporation Tax filing support", () => getCorporationTaxFilingSupport(3, 7)],
  ];

  try {
    for (const [label, call] of criticalCalls) {
      await assert.rejects(
        call,
        ApiContractError,
        `${label} must reject malformed success JSON at the client boundary`,
      );
    }
  } finally {
    globalThis.fetch = originalFetch;
  }
});

function periodFixture() {
  return {
    id: 7,
    companyId: 3,
    periodStart: "2026-01-01",
    periodEnd: "2026-12-31",
    status: "Draft",
    isFirstYear: false,
    memberAuditNoticeReceived: false,
    memberAuditNoticeDate: null,
    goingConcernConfirmed: true,
    goingConcernNote: null,
    sizeClassification: null,
    filingRegime: null,
  };
}

function directorsReportFixture() {
  return {
    companyName: "Contract Ltd",
    periodStart: "2026-01-01",
    periodEnd: "2026-12-31",
    directorNames: ["Named Director"],
    directorServicePeriods: [{
      name: "Named Director",
      role: "Director",
      appointedDate: "2020-01-01",
      resignedDate: null,
    }],
    secretaryName: "Named Secretary",
    secretaryServicePeriod: {
      name: "Named Secretary",
      role: "Secretary",
      appointedDate: "2020-01-01",
      resignedDate: null,
    },
    principalActivities: "The principal activity is the provision of professional services.",
    principalActivitiesReviewed: true,
    principalActivitiesReviewedBy: "Reviewer Name",
    principalActivitiesReviewedAt: "2027-01-15T10:00:00Z",
    resultsAndDividends: "The company recorded neither a profit nor a loss.",
    profitOrLossAfterTax: 0,
    dividendsPaid: 0,
    dividendsDeclaredNotPaid: 0,
    accountingRecordsStatement: "The accounting records are retained at the registered office.",
    postBalanceSheetEvents: "There have been no significant events since year end.",
    postBalanceSheetEventsReviewed: true,
    goingConcernStatement: null,
    auditInformationStatement: "Each director completed the required enquiries.",
    auditInformationEvidenceRequired: true,
    auditInformationEvidenceRecorded: true,
    auditInformationConfirmedBy: "Reviewer Name",
    auditInformationConfirmedAt: "2027-01-15T10:00:00Z",
    officerTimelineComplete: true,
    isMicroExempt: false,
    isSmallExemptFromBusinessReview: true,
    electedRegime: "Small",
  };
}

function companyFixture() {
  return {
    id: 3,
    legalName: "Contract Ltd",
    tradingName: null,
    croNumber: "123456",
    taxReference: null,
    companyType: "Private",
    incorporationDate: "2020-02-29",
    financialYearStartMonth: 1,
    annualReturnDate: "2026-09-15",
    registeredOfficeAddress1: null,
    registeredOfficeAddress2: null,
    registeredOfficeCity: null,
    registeredOfficeCounty: null,
    registeredOfficeEircode: null,
    isGroupMember: false,
    isHolding: false,
    isInvestment: false,
    isSubsidiary: false,
    isDormant: false,
    isTrading: true,
    isVatRegistered: true,
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
    assignedReviewerName: null,
    assignedReviewerEmail: null,
    latestPeriod: periodFixture(),
    periodCount: 1,
  };
}

function transactionPageFixture() {
  return {
    total: 2,
    items: [
      {
        id: 1,
        date: "2026-01-01",
        description: "Receipt",
        amount: 100,
        balance: 100,
        categoryId: 9,
        confidenceScore: 0.9,
        manualOverride: false,
        category: { id: 9, code: "4000", name: "Sales", type: "Income" },
      },
      {
        id: 2,
        date: "2026-01-02",
        description: "Pending",
        amount: -25,
        balance: 75,
        categoryId: null,
        confidenceScore: null,
        manualOverride: false,
        category: null,
      },
    ],
    page: 1,
    pageSize: 50,
    totalPages: 1,
    hasPreviousPage: false,
    hasNextPage: false,
    sortBy: "date",
    sortDirection: "desc",
    aggregates: { total: 2, categorised: 1, uncategorised: 1 },
  };
}

function yearEndFixture() {
  return {
    debtors: { count: 0, total: 0 },
    creditors: { count: 0, total: 0 },
    fixedAssets: { count: 0, totalCost: 0 },
    inventory: { count: 0, totalValue: 0 },
    loans: { count: 0, totalBalance: 0 },
    directorLoans: { count: 0 },
    payroll: { hasPayroll: false },
    taxes: { count: 0, totalLiability: 0, totalBalance: 0 },
    dividends: { count: 0, total: 0 },
    reviewConfirmations: [],
    completeness: { score: 50, completed: 1, total: 2, incomplete: ["Inventory"] },
  };
}

function adjustmentFixture() {
  return {
    id: 4,
    description: "Accrual",
    amount: 25,
    source: "Manual",
    reason: "Year-end cut-off",
    legalBasis: "FRS 102",
    impactOnProfit: -25,
    impactOnAssets: 0,
    isAuto: false,
    approvedBy: "Qualified Reviewer",
    approvedAt: "2026-07-10T10:00:00Z",
  };
}

function profitAndLossFixture() {
  return {
    turnover: 10_000,
    costOfSales: 4_000,
    grossProfit: 6_000,
    otherIncome: 500,
    overheads: [{ code: "6000", name: "Rent", amount: 1_000 }],
    totalOverheads: 1_000,
    operatingProfit: 5_500,
    interestPayable: 100,
    profitBeforeTax: 5_400,
    taxCharge: 675,
    profitAfterTax: 4_725,
    yearEndAdjustments: [{ description: "Accrual", amount: -25, approved: true }],
    totalYearEndAdjustments: -25,
  };
}

function balanceSheetFixture() {
  return {
    fixedAssets: { categories: [{ category: "Equipment", cost: 2_000, depreciation: 500, nbv: 1_500 }], total: 1_500 },
    currentAssets: { stock: 200, debtors: 300, prepayments: 0, cash: 1_000, total: 1_500 },
    creditorsWithinYear: { tradeCreditors: 250, accruals: 100, taxCreditors: 150, otherCreditors: 0, total: 500 },
    netCurrentAssets: 1_000,
    totalAssetsLessCurrentLiabilities: 2_500,
    creditorsAfterYear: { loans: 500, other: 0, total: 500 },
    netAssets: 2_000,
    capitalAndReserves: {
      shareCapital: 100,
      openingRetainedEarnings: 1_000,
      profitForYear: 1_000,
      dividendsPaid: 100,
      otherReserveMovements: 0,
      retainedEarnings: 1_900,
      total: 2_000,
      unexplainedDifference: 0,
    },
    balances: true,
  };
}

function charityInfoFixture() {
  return {
    id: 1,
    companyId: 3,
    charityNumber: "20000001",
    charityType: "CLG",
    grossIncome: 100_000,
    sorpTier: 1,
    charitableObjectives: "Public benefit",
    principalActivities: "Services",
    governanceCodeCompliant: true,
    governanceCodeNote: "Board review complete",
    governanceEvidenceReference: "GOV-BOARD-001",
    governanceReviewedBy: "Niamh Reviewer",
    governanceReviewedAtUtc: "2026-07-10T10:00:00Z",
    governanceEvidenceArtifactSha256: "a".repeat(64),
    hasInternationalTransfers: false,
    internationalTransferDetails: null,
    trusteeRemunerationPaid: false,
    trusteeRemunerationAmount: 0,
    trusteeExpensesDetails: null,
  };
}

function sofaFixture() {
  const fund = {
    fundName: "General",
    fundType: "Unrestricted",
    openingBalance: 1_000,
    incomingResources: 500,
    resourcesExpended: 250,
    transfers: 0,
    gainsLosses: 0,
    closingBalance: 1_250,
  };
  return {
    unrestrictedFunds: [fund],
    restrictedFunds: [],
    endowmentFunds: [],
    totalIncoming: 500,
    totalExpended: 250,
    totalTransfers: 0,
    totalGainsLosses: 0,
    netMovement: 250,
    totalOpeningFunds: 1_000,
    totalClosingFunds: 1_250,
  };
}

function filingWorkflowFixture() {
  return {
    cro: {
      status: "NotStarted",
      accountsPdfReady: false,
      signaturePageReady: false,
      paymentCompleted: false,
      submissionReference: null,
      rejectionReason: null,
      correctionDeadline: null,
    },
    revenue: {
      status: "NotStarted",
      ixbrlReady: false,
      ixbrlInternalChecksPassed: false,
      ixbrlValid: false,
      validationErrors: null,
      ct1Reference: null,
      generationSupport: "manual-handoff-only",
      manualHandoffRequired: true,
      reviewPrototypeChecksPassed: false,
    },
    charity: {
      status: "NotStarted",
      sofaGenerated: false,
      trusteesReportGenerated: false,
      annualReturnReference: null,
      rejectionReason: null,
      correctionDeadline: null,
      submittedBy: null,
      submittedAt: null,
      acceptedBy: null,
      acceptedAt: null,
    },
    blockingIssues: ["Qualified review required"],
    warningIssues: [],
    readyToFile: false,
  };
}

function filingReadinessFixture() {
  const source = {
    sourceId: "cro-accounts",
    title: "CRO requirements",
    effectiveDate: "2026-01-01",
    url: "https://cro.ie/requirements",
  };
  return {
    companyId: 3,
    periodId: 7,
    companyType: "Private",
    sizeClass: "Small",
    electedRegime: "Small",
    auditExempt: true,
    supportedPath: true,
    manualProfessionalReviewRequired: false,
    accountantReviewRequired: false,
    accountantReviewState: "Not required by law — not recorded",
    approvalAuthority: "board-of-directors-s324",
    directCroSubmissionSupported: false,
    directRosSubmissionSupported: false,
    revenueIxbrlGenerationSupported: false,
    revenueManualHandoffRequired: true,
    revenueGenerationSupportReason: "Manual handoff only",
    revenueTaxonomy: {
      taxonomyKey: "ie-gaap-2026",
      taxonomyDate: "2026-01-01",
      label: "Revenue taxonomy",
      schemaRef: "https://revenue.ie/taxonomy.xsd",
      acceptedByRevenue: true,
      effectiveForPeriodsStartingOnOrAfter: "2026-01-01",
      sources: [source],
    },
    signOffPacket: {
      state: "blocked",
      stateLabel: "Blocked before directors' approval",
      readyForDirectorsApproval: false,
      readyForExternalFiling: false,
      approvedBy: null,
      approvedAt: null,
      steps: [{ code: "directors-approval", label: "Approval", state: "blocked", detail: "Required", sources: [source] }],
      openBlockers: ["Directors' approval required"],
      openWarnings: [],
      allowedNextActions: ["Resolve blockers"],
    },
    requiredEvidence: [{ code: "directors-approval", label: "Board approval of the financial statements, s.324", required: true, satisfied: false, detail: "Required", sources: [source] }],
    blockingIssues: [{ code: "directors-approval-required", severity: "blocking", message: "Directors' approval required", sources: [source] }],
    warningIssues: [],
    sourceReferences: [source],
    allowedNextActions: ["Resolve blockers"],
  };
}

test("historic size figures: both routes in the prerequisites, provenance on an anchor, PUT with PDF evidence", async () => {
  const historicFiguresPeriod = {
    periodId: 8, periodStart: "2024-01-01", periodEnd: "2024-12-31", reason: "record historic figures from the filed accounts",
  };
  const blocked = {
    ready: false,
    isFirstYear: false,
    firstBlockingPeriod: { periodId: 1, periodStart: "2018-01-01", periodEnd: "2018-12-31", reason: "not classified yet" },
    message: "Classify 1 Jan 2018 – 31 Dec 2018 first, or record historic figures from the filed accounts for 1 Jan 2024 – 31 Dec 2024.",
    historicFiguresPeriod,
    canRecordHistoricFigures: false,
    suggestedThresholdElection: "2023-01-01",
  };
  assert.equal(parse(classificationPrerequisitesSchema, blocked).historicFiguresPeriod.periodId, 8);
  const older = { ...blocked };
  delete older.historicFiguresPeriod;
  delete older.canRecordHistoricFigures;
  delete older.suggestedThresholdElection;
  assert.equal(parse(classificationPrerequisitesSchema, older).historicFiguresPeriod, null);
  assert.equal(parse(classificationPrerequisitesSchema, older).canRecordHistoricFigures, false);
  assert.equal(parse(classificationPrerequisitesSchema, blocked).suggestedThresholdElection, "2023-01-01");
  assert.equal(parse(classificationPrerequisitesSchema, older).suggestedThresholdElection, null);
  assert.throws(
    () => parse(classificationPrerequisitesSchema, { ...blocked, ready: true, firstBlockingPeriod: null }),
    /historicFiguresPeriod/,
  );

  const anchor = {
    id: 3,
    turnover: 1000000,
    balanceSheetTotal: 500000,
    avgEmployees: 5,
    inputBasis: "HistoricAnchor",
    historicSourceReference: "Fictitious members' accounts",
    historicEvidenceSha256: HASH_A,
    historicAttestedBy: "Reviewer One",
    historicAttestedRole: "Reviewer",
    historicAttestedAt: "2026-10-12T09:00:00Z",
    historicAttestedEffectiveClass: null,
    decisionInputFingerprintSha256: HASH_B,
    calculatedClass: "Large",
    rawCurrentClass: "Small",
    periodLengthInYears: 1,
    annualisedTurnover: 1000000,
  };
  assert.equal(parse(sizeClassificationSchema, anchor).inputBasis, "HistoricAnchor");
  assert.throws(() => parse(sizeClassificationSchema, { ...anchor, historicEvidenceSha256: null }), /historicEvidenceSha256/);
  assert.throws(() => parse(sizeClassificationSchema, { ...anchor, historicSourceReference: null }), /historicSourceReference/);
  assert.throws(() => parse(sizeClassificationSchema, { ...anchor, inputBasis: "Keyed" }), /inputBasis/);

  const originalFetch = globalThis.fetch;
  const requests = [];
  globalThis.fetch = async (url, init) => {
    requests.push({ url: String(url), method: (init?.method ?? "GET").toUpperCase(), body: init?.body ? JSON.parse(String(init.body)) : undefined });
    return new Response(JSON.stringify(anchor), { status: 200, headers: { "Content-Type": "application/json" } });
  };
  try {
    const bytes = new TextEncoder().encode("%PDF-1.4 fictitious filed accounts");
    const evidence = new File([bytes], "accounts.pdf", { type: "application/pdf" });
    const saved = await recordHistoricSizeFigures(1, 8, {
      turnover: 1000000,
      balanceSheetTotal: 500000,
      avgEmployees: 5,
      thresholdElectionEffectiveFrom: "2024-01-01",
      sourceReference: "Fictitious members' accounts",
      evidence,
      attestedEffectiveClass: null,
    });
    assert.equal(saved.inputBasis, "HistoricAnchor");
    assert.equal(requests.length, 1);
    assert.equal(requests[0].method, "PUT");
    assert.match(requests[0].url, /\/api\/companies\/1\/periods\/8\/classification\/historic-anchor$/);
    assert.deepEqual(Object.keys(requests[0].body).sort(), [
      "attestedEffectiveClass", "avgEmployees", "balanceSheetTotal", "evidenceArtifact", "evidenceSha256",
      "sourceReference", "thresholdElectionEffectiveFrom", "turnover",
    ]);
    assert.equal(requests[0].body.evidenceSha256, createHash("sha256").update(bytes).digest("hex"));
    assert.equal(requests[0].body.evidenceArtifact, Buffer.from(bytes).toString("base64"));

    // A file that is not a PDF is refused before anything is sent.
    const notPdf = new File([new TextEncoder().encode("<html></html>")], "accounts.pdf", { type: "application/pdf" });
    await assert.rejects(
      () => recordHistoricSizeFigures(1, 8, {
        turnover: 1, balanceSheetTotal: 1, avgEmployees: 0, thresholdElectionEffectiveFrom: "2024-01-01",
        sourceReference: "Fictitious", evidence: notPdf, attestedEffectiveClass: null,
      }),
      /PDF/,
    );
    assert.equal(requests.length, 1);
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("retired historic figures keep their evidence and retirement record, and the retire and CT due clients post exactly their inputs", async () => {
  const retired = {
    id: 3,
    turnover: 1000000,
    balanceSheetTotal: 500000,
    avgEmployees: 5,
    inputBasis: "RetiredHistoricAnchor",
    historicSourceReference: "Fictitious members' accounts",
    historicEvidenceSha256: HASH_A,
    historicAttestedBy: "Reviewer One",
    historicAttestedRole: "Reviewer",
    historicAttestedAt: "2026-10-12T09:00:00Z",
    historicRetiredAt: "2026-10-12T10:00:00Z",
    historicRetiredBy: "Owner One",
    historicRetiredReason: "Fictitious reason: wrong year",
  };
  assert.equal(parse(sizeClassificationSchema, retired).inputBasis, "RetiredHistoricAnchor");
  assert.throws(() => parse(sizeClassificationSchema, { ...retired, historicRetiredReason: null }), /historicRetiredReason/);
  assert.throws(() => parse(sizeClassificationSchema, { ...retired, historicEvidenceSha256: null }), /historicEvidenceSha256/);
  assert.throws(
    () => parse(sizeClassificationSchema, {
      ...retired,
      decisionInputFingerprintSha256: HASH_B,
      calculatedClass: "Large",
      rawCurrentClass: "Small",
      periodLengthInYears: 1,
      annualisedTurnover: 1000000,
    }),
    /decisionInputFingerprintSha256/,
  );

  const confirmed = {
    id: 4,
    periodId: 3,
    taxType: "CorporationTax",
    liability: 150,
    paid: 0,
    balance: 150,
    confirmedDueBy: "Reviewer One",
    confirmedDueAt: "2026-10-12T09:00:00Z",
    confirmedDueReason: "Income outside the exemption",
    confirmedDueLiability: 150,
    confirmedTaxableProfitOutsideExemption: 1200,
    confirmedNonTradingProfitOutsideExemption: 0,
  };
  const originalFetch = globalThis.fetch;
  const requests = [];
  let response = retired;
  globalThis.fetch = async (url, init) => {
    requests.push({ url: String(url), method: (init?.method ?? "GET").toUpperCase(), body: init?.body ? JSON.parse(String(init.body)) : undefined });
    return new Response(JSON.stringify(response), { status: 200, headers: { "Content-Type": "application/json" } });
  };
  try {
    const saved = await retireHistoricSizeFigures(1, 8, "Fictitious reason: wrong year");
    assert.equal(saved.inputBasis, "RetiredHistoricAnchor");
    assert.equal(requests[0].method, "POST");
    assert.match(requests[0].url, /\/api\/companies\/1\/periods\/8\/classification\/historic-anchor\/retire$/);
    assert.deepEqual(requests[0].body, { reason: "Fictitious reason: wrong year" });

    response = confirmed;
    const due = await confirmCorporationTaxDue(1, 3, "Income outside the exemption", 1200, 0);
    assert.equal(due.confirmedDueReason, "Income outside the exemption");
    assert.equal(requests[1].method, "POST");
    assert.match(requests[1].url, /\/api\/companies\/1\/periods\/3\/tax-balances\/CorporationTax\/confirm-due$/);
    assert.deepEqual(requests[1].body, { reason: "Income outside the exemption", taxableProfitOutsideExemption: 1200, nonTradingProfitOutsideExemption: 0 });

    // A response that does not carry the confirmation is not accepted as one.
    response = { ...confirmed, confirmedDueAt: null };
    await assert.rejects(() => confirmCorporationTaxDue(1, 3, "Income outside the exemption", 1200, 0), /confirmedDueAt/);
    // Nor one given for a different amount.
    response = { ...confirmed, confirmedDueLiability: 120 };
    await assert.rejects(() => confirmCorporationTaxDue(1, 3, "Income outside the exemption", 1200, 0), /confirmedDueLiability/);
    // Nor one without the attested taxable profit outside the exemption.
    response = { ...confirmed, confirmedTaxableProfitOutsideExemption: null };
    await assert.rejects(() => confirmCorporationTaxDue(1, 3, "Income outside the exemption", 1200, 0), /confirmedTaxableProfitOutsideExemption/);
    // Nor one without the attested non-trading profit outside the exemption.
    response = { ...confirmed, confirmedNonTradingProfitOutsideExemption: null };
    await assert.rejects(() => confirmCorporationTaxDue(1, 3, "Income outside the exemption", 1200, 0), /confirmedNonTradingProfitOutsideExemption/);

    // The archive of retired or replaced evidence: read with GET, never carrying the PDF bytes.
    const archivedRecord = {
      id: 31, event: "Retired", archivedAt: "2026-10-12T10:00:00Z", archivedBy: "Owner One",
      turnover: 1000000, balanceSheetTotal: 500000, avgEmployees: 5, sourceReference: "Fictitious members' accounts",
      evidenceSha256: HASH_A, attestedBy: "Reviewer One", attestedRole: "Reviewer", attestedAt: "2026-10-12T09:00:00Z",
      attestedEffectiveClass: null, retiredAt: "2026-10-12T10:00:00Z", retiredBy: "Owner One", retiredReason: "Fictitious reason",
    };
    response = [archivedRecord];
    const archive = await getArchivedHistoricFigures(1, 8);
    assert.equal(archive[0].event, "Retired");
    assert.equal(requests.at(-1).method, "GET");
    assert.match(requests.at(-1).url, /\/api\/companies\/1\/periods\/8\/classification\/historic-anchor\/archive$/);
    response = [{ ...archivedRecord, retiredReason: null }];
    await assert.rejects(() => getArchivedHistoricFigures(1, 8), /retiredReason/);
  } finally {
    globalThis.fetch = originalFetch;
  }
});
