import assert from "node:assert/strict";
import test from "node:test";

import { parseApiContract, taxComputationSchema } from "../src/lib/apiContracts.ts";

function payload() {
  return {
    accountingProfit: 55_000,
    adjustments: [{ description: "Add back: non-deductible expenses", amount: 1_000, basis: "Signed evidence" }],
    taxableProfit: 56_000,
    tradingLossAvailable: 0,
    corporationTaxAt125: 7_000,
    corporationTaxAt25: 0,
    totalCorporationTax: 7_000,
    preliminaryTaxPaid: 1_000,
    balanceDue: 6_000,
    notes: "Support data only.",
    tradingProfitBeforeLossRelief: 56_000,
    tradingProfitAfterLossRelief: 56_000,
    passiveNonTradingIncome: 0,
    broughtForwardTradingLoss: 0,
    tradingLossUsed: 0,
    tradingLossCarriedForward: 0,
    capitalAllowances: 0,
    balancingAllowances: 0,
    balancingCharges: 0,
    supportStatus: "machine-supported-simple-scope",
    finalTaxChargeSupported: true,
    manualReviewRequired: true,
    outputKind: "corporation-tax-support-data-not-ct1-return",
    isCompleteCt1Return: false,
    blockingReasons: [],
    sources: [{ code: "revenue-ct", title: "Revenue CT", url: "https://www.revenue.ie/" }],
    calculationSha256: "a".repeat(64),
  };
}

test("corporation-tax runtime contract retains support-only and reconciliation invariants", () => {
  const parsed = parseApiContract(taxComputationSchema, payload(), "corporation tax support");
  assert.equal(parsed.isCompleteCt1Return, false);
  assert.equal(parsed.outputKind, "corporation-tax-support-data-not-ct1-return");
});

test("corporation-tax runtime contract rejects plausible final-return claims and contradictory support", () => {
  assert.throws(
    () => parseApiContract(taxComputationSchema, { ...payload(), isCompleteCt1Return: true }, "corporation tax support"),
    /isCompleteCt1Return/,
  );
  assert.throws(
    () => parseApiContract(taxComputationSchema, {
      ...payload(),
      finalTaxChargeSupported: true,
      supportStatus: "manual-review-required",
      blockingReasons: ["Close company unsupported"],
    }, "corporation tax support"),
    /Final-charge support flag|Support status/,
  );
  assert.throws(
    () => parseApiContract(taxComputationSchema, { ...payload(), tradingLossCarriedForward: 1 }, "corporation tax support"),
    /Trading-loss movement/,
  );
});

test("corporation-tax runtime contract reconciles an exempt company's charitable-exemption line, and only that line", () => {
  const exempt = {
    ...payload(),
    accountingProfit: 41_000,
    adjustments: [{
      description: "Income within the charitable exemption (confirmed by Example Owner on 12 Oct 2026)",
      amount: -40_000,
      basis: "Taxable profit from activities outside the charitable exemption attested at €1,000.00.",
    }],
    taxableProfit: 1_000,
    corporationTaxAt125: 125,
    totalCorporationTax: 125,
    preliminaryTaxPaid: 0,
    balanceDue: 125,
    tradingProfitBeforeLossRelief: 41_000,
    tradingProfitAfterLossRelief: 41_000,
  };
  assert.equal(parseApiContract(taxComputationSchema, exempt, "corporation tax support").taxableProfit, 1_000);
  // Any other deduction line does not reconcile taxable profit to the streams.
  assert.throws(
    () => parseApiContract(taxComputationSchema, {
      ...exempt,
      adjustments: [{ description: "Deduct: something else", amount: -40_000, basis: "Unrelated" }],
    }, "corporation tax support"),
    /taxableProfit/,
  );
});
