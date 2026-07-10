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
