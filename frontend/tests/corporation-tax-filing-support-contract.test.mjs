import assert from "node:assert/strict";
import test from "node:test";

import {
  corporationTaxFilingSupportResponseSchema,
  parseApiContract,
} from "../src/lib/apiContracts.ts";

function filingSupport() {
  return {
    outputKind: "corporation-tax-filing-support-not-ct1-return",
    isCompleteCt1Return: false,
    directRosSubmissionSupported: false,
    companyClass: "Small",
    companyClassLabel: "Small company preliminary-tax rules",
    isShortAccountingPeriod: false,
    currentTotalTaxForPaymentSupport: 7_000,
    annualisedPriorCorporationTax: 6_000,
    preliminaryFirstDueDate: "2025-06-23",
    preliminarySecondOrSingleDueDate: "2025-11-23",
    returnAndBalanceDueDate: "2026-09-23",
    safeHarbourBases: [
      { code: "90-current", label: "90% current", amount: 6_300, available: true },
      { code: "100-prior", label: "100% prior", amount: 6_000, available: true },
    ],
    preliminaryTaxSafeHarbourAmount: 6_000,
    safeHarbourMet: true,
    dueItems: [
      {
        code: "preliminary-single",
        label: "Preliminary Tax single instalment",
        dueDate: "2025-11-23",
        cumulativeTaxRequired: 6_000,
        paidByDueDate: 6_000,
        shortfallAtDueDate: 0,
        basis: "100% preceding-period safe harbour",
      },
      {
        code: "return-balance",
        label: "CT1 return and balance",
        dueDate: "2026-09-23",
        cumulativeTaxRequired: 7_000,
        paidByDueDate: 6_000,
        shortfallAtDueDate: 1_000,
        basis: "Balance of current-period tax",
      },
    ],
    preliminaryTaxPaymentsRecorded: 6_000,
    taxPaymentsRecorded: 6_000,
    estimatedLatePaymentInterest: 0,
    interestSegments: [],
    lateFiling: {
      isLate: false,
      returnDueDate: "2026-09-23",
      exposureDate: "2026-01-15",
      daysLate: 0,
      rate: 0,
      cap: 0,
      estimatedSurcharge: 0,
      reliefRestrictionExposure: false,
      detail: "No late-filing surcharge exposure at the assessment date.",
    },
    manualReviewRequired: true,
    filingSupportReady: true,
    blockingReasons: [],
    warnings: ["Working-paper estimate only."],
    calculationSha256: "a".repeat(64),
  };
}

function response() {
  const support = filingSupport();
  return {
    review: {
      id: 41,
      periodId: 11,
      priorPeriodStart: "2024-01-01",
      priorPeriodEnd: "2024-12-31",
      priorPeriodCorporationTaxLiabilityExcludingSurchargeAndS239: 6_000,
      priorPeriodSection239IncomeTax: 0,
      currentPeriodSection239IncomeTax: 0,
      priorLiabilityEvidenceReference: "Prior signed CT1 retained under evidence CT-2024-01.",
      hasInterestLimitationRule: false,
      usesNotionalGroupPaymentAllocation: false,
      hasDirtOrOtherWithholdingCredits: false,
      hasOtherPreliminaryTaxAdjustments: false,
      hasMandatoryElectronicFilingExemption: false,
      evidenceNote: "Prepared from the signed prior CT1 and current support calculation.",
      preparedBy: "Fixture preparer",
      preparedAtUtc: "2026-01-15T10:30:00Z",
    },
    payments: [{
      id: 51,
      periodId: 11,
      paymentDate: "2025-11-23",
      amount: 6_000,
      kind: "PreliminarySecondOrSingle",
      evidenceReference: "ROS payment receipt retained under reference PT-2025-11-23.",
      externalPaymentReference: "ROS-PT-51",
      recordedBy: "Fixture preparer",
      recordedAtUtc: "2025-11-23T12:00:00Z",
    }],
    filingSupport: support,
    worksheet: {
      outputKind: "scoped-ct1-support-worksheet-not-submittable-return",
      isCompleteCt1Return: false,
      directRosSubmissionSupported: false,
      warning: "SUPPORT WORKSHEET ONLY - NOT A CT1 RETURN - NOTHING IS SUBMITTED TO REVENUE",
      companyName: "Contract Ltd",
      taxReference: "1234567AB",
      periodStart: "2025-01-01",
      periodEnd: "2025-12-31",
      mappingVersion: "Revenue-CT1-2025-Part-38-02-01J",
      yearSpecificMappingAvailable: true,
      generatedAsOf: "2026-01-15",
      fields: [{
        code: "sales-turnover",
        publishedPanelNumber: 3,
        panelTitle: "Extracts from Accounts",
        publishedFieldLabel: "Sales / Receipts / Turnover",
        mappingStatus: "published-exact-field-label",
        valueType: "money",
        numericValue: 100_000,
        source: "Profit and loss statement",
        machineSupported: true,
        note: "Published 2025 field label.",
      }],
      reconciliations: [{
        code: "tax-charge",
        left: 1_000,
        right: 1_000,
        difference: 0,
        reconciles: true,
        detail: "Tax charge reconciles.",
      }],
      preliminaryTax: structuredClone(support),
      supportWorksheetReady: true,
      qualifiedAccountantReviewRequired: true,
      blockingReasons: [],
      manualCompletionItems: ["Complete all unsupported CT1 panels in ROS."],
      sources: [{ code: "revenue-ct1", title: "Revenue CT1 guide", url: "https://www.revenue.ie/example.pdf" }],
      worksheetSha256: "b".repeat(64),
    },
  };
}

test("filing-support contract retains support-only, evidence, date, and arithmetic invariants", () => {
  const parsed = parseApiContract(
    corporationTaxFilingSupportResponseSchema,
    response(),
    "Corporation Tax filing support",
  );

  assert.equal(parsed.filingSupport.directRosSubmissionSupported, false);
  assert.equal(parsed.worksheet.qualifiedAccountantReviewRequired, true);
  assert.equal(parsed.worksheet.fields[0].publishedPanelNumber, 3);
});

test("filing-support contract rejects CT1 or direct-submission claims", () => {
  const direct = response();
  direct.filingSupport.directRosSubmissionSupported = true;
  assert.throws(
    () => parseApiContract(corporationTaxFilingSupportResponseSchema, direct, "filing support"),
    /directRosSubmissionSupported/,
  );

  const complete = response();
  complete.worksheet.isCompleteCt1Return = true;
  assert.throws(
    () => parseApiContract(corporationTaxFilingSupportResponseSchema, complete, "filing support"),
    /isCompleteCt1Return/,
  );
});

test("filing-support contract rejects arithmetic, readiness, and calculation-identity drift", () => {
  const shortfall = response();
  shortfall.filingSupport.dueItems[1].shortfallAtDueDate = 999;
  assert.throws(
    () => parseApiContract(corporationTaxFilingSupportResponseSchema, shortfall, "filing support"),
    /shortfall/i,
  );

  const readiness = response();
  readiness.worksheet.supportWorksheetReady = false;
  assert.throws(
    () => parseApiContract(corporationTaxFilingSupportResponseSchema, readiness, "filing support"),
    /readiness/i,
  );

  const identity = response();
  identity.worksheet.preliminaryTax.calculationSha256 = "c".repeat(64);
  assert.throws(
    () => parseApiContract(corporationTaxFilingSupportResponseSchema, identity, "filing support"),
    /calculations differ/i,
  );
});

test("filing-support contract rejects invalid dates, weak evidence, and unrecognised field mappings", () => {
  const date = response();
  date.payments[0].paymentDate = "2025-02-30";
  assert.throws(
    () => parseApiContract(corporationTaxFilingSupportResponseSchema, date, "filing support"),
    /paymentDate/,
  );

  const evidence = response();
  evidence.payments[0].evidenceReference = "receipt";
  assert.throws(
    () => parseApiContract(corporationTaxFilingSupportResponseSchema, evidence, "filing support"),
    /evidenceReference/,
  );

  const priorPeriod = response();
  delete priorPeriod.review.priorPeriodEnd;
  assert.throws(
    () => parseApiContract(corporationTaxFilingSupportResponseSchema, priorPeriod, "filing support"),
    /priorPeriodEnd/,
  );

  const mapping = response();
  mapping.worksheet.fields[0].mappingStatus = "exact-ct1-field";
  assert.throws(
    () => parseApiContract(corporationTaxFilingSupportResponseSchema, mapping, "filing support"),
    /mappingStatus/,
  );
});
