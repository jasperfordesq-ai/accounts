import assert from "node:assert/strict";
import test from "node:test";

import { accountantWorkingPaperPackSchema } from "../src/lib/apiContracts.ts";
import { accountantWorkingPaperPackFixture } from "./fixtures/accountant-working-paper-pack.ts";

const clone = () => structuredClone(accountantWorkingPaperPackFixture);

test("working-paper contract accepts the retained support-only reconciled fixture", () => {
  const parsed = accountantWorkingPaperPackSchema.parse(clone());
  assert.equal(parsed.outputKind, "internal-accountant-working-paper-pack");
  assert.equal(parsed.isFilingArtifact, false);
  assert.equal(parsed.directSubmissionSupported, false);
  assert.equal(parsed.workingPaperIndex.entries.length, 5);
  assert.equal(parsed.adjustedTrialBalance.totalAdjustedDebits, parsed.adjustedTrialBalance.totalAdjustedCredits);
});

test("working-paper contract rejects filing, submission, identity, and index drift", () => {
  for (const mutate of [
    (pack) => { pack.isFilingArtifact = true; },
    (pack) => { pack.directSubmissionSupported = true; },
    (pack) => { pack.qualifiedAccountantReviewRequired = false; },
    (pack) => { pack.identity.releaseCandidate = ""; },
    (pack) => { pack.identity.sourceDataSha256 = "not-a-hash"; },
    (pack) => { pack.workingPaperIndex.entries[0].artifactSha256 = "f".repeat(64); },
    (pack) => { pack.workingPaperIndex.entries.pop(); },
  ]) {
    const pack = clone();
    mutate(pack);
    assert.equal(accountantWorkingPaperPackSchema.safeParse(pack).success, false);
  }
});

test("working-paper contract rejects transaction, trial-balance, tax, and review-count drift", () => {
  for (const mutate of [
    (pack) => { pack.categorizedTransactions.totalCount += 1; },
    (pack) => { pack.categorizedTransactions.includedNetMovement += 1; },
    (pack) => { pack.categorizedTransactions.rows[0].categoryId = null; },
    (pack) => { pack.adjustedTrialBalance.totalAdjustedDebits += 1; },
    (pack) => { pack.adjustedTrialBalance.reconciliations[0].reconciles = false; },
    (pack) => { pack.leadSchedules.rows[0].closingDebit += 1; },
    (pack) => { pack.corporationTaxBridge.taxableProfit += 1; },
    (pack) => { pack.corporationTaxBridge.balanceDue += 1; },
    (pack) => { pack.reviewExceptions.blockingCount += 1; },
  ]) {
    const pack = clone();
    mutate(pack);
    assert.equal(accountantWorkingPaperPackSchema.safeParse(pack).success, false);
  }
});
