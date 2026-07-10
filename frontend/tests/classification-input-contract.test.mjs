import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

test("classification UI derives prior-period evidence and sends an explicit threshold election", () => {
  const route = readFileSync(
    new URL("../src/app/companies/[companyId]/periods/[periodId]/classify/page.tsx", import.meta.url),
    "utf8",
  );
  const api = readFileSync(new URL("../src/lib/api.ts", import.meta.url), "utf8");

  assert.doesNotMatch(route, /setPriorYearClass|priorYearClass:/);
  assert.match(route, /thresholdElectionEffectiveFrom/);
  assert.match(route, /value="2023-01-01"/);
  assert.match(route, /value="2024-01-01"/);
  assert.match(route, /Prior-period raw figures and the prior effective class are derived/);
  assert.equal((route.match(/min=\{0\}/g) ?? []).length, 3);
  const loadData = route.slice(route.indexOf("const loadData"), route.indexOf("async function handleClassify"));
  assert.doesNotMatch(loadData, /runClassification\(/, "opening the page must not mutate classification state");
  assert.match(route, /decisionInputFingerprintSha256/);
  assert.match(route, /Classification Requires Re-run/);

  const saveInput = api.slice(api.indexOf("export const saveSizeClassification"), api.indexOf("export interface ClassificationResult"));
  assert.doesNotMatch(saveInput, /priorYearClass/);
  assert.match(saveInput, /thresholdElectionEffectiveFrom: "2023-01-01" \| "2024-01-01"/);
  assert.match(api, /rawCurrentClass: string/);
  assert.match(api, /annualisedTurnover: number/);
  assert.match(api, /thresholdScheduleEffectiveFrom\?: string/);
  assert.match(api, /decisionInputFingerprintSha256\?: string/);
});
