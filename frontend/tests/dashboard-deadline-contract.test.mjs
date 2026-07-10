import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import {
  DASHBOARD_DEADLINE_STATES,
  getDashboardDeadlines,
  parseDashboardDeadlineBatch,
} from "../src/lib/api.ts";

test("dashboard deadline batch validates 105 explicitly-stated companies", () => {
  const batch = dashboardBatch(105);
  const parsed = parseDashboardDeadlineBatch(batch);

  assert.equal(parsed.totalCompanies, 105);
  assert.equal(parsed.items.length, 105);
  assert.equal(Object.values(parsed.counts).reduce((sum, count) => sum + count, 0), 105);
  assert.equal(parsed.unavailableCount, parsed.counts.unavailable);
});

test("dashboard deadline batch rejects malformed counts, duplicate scope and deadline-state drift", () => {
  const badCount = dashboardBatch(3);
  badCount.counts.scheduled += 1;
  assert.throws(() => parseDashboardDeadlineBatch(badCount), /counts\.scheduled/);

  const duplicate = dashboardBatch(3);
  duplicate.items[1].companyId = duplicate.items[0].companyId;
  assert.throws(() => parseDashboardDeadlineBatch(duplicate), /companyId values must be unique/);

  const missingEvidence = dashboardBatch(7);
  const scheduled = missingEvidence.items.find((item) => item.state === "scheduled");
  assert.ok(scheduled);
  scheduled.deadline = null;
  assert.throws(() => parseDashboardDeadlineBatch(missingEvidence), /deadline does not match state scheduled/);
});

test("getDashboardDeadlines makes one batch request rather than one request per company", async () => {
  const originalFetch = globalThis.fetch;
  const calls = [];
  globalThis.fetch = async (input) => {
    calls.push(String(input));
    return new Response(JSON.stringify(dashboardBatch(105)), {
      status: 200,
      headers: { "content-type": "application/json" },
    });
  };

  try {
    const result = await getDashboardDeadlines();
    assert.equal(result.totalCompanies, 105);
    assert.deepEqual(calls, ["/api/dashboard/deadlines"]);
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("dashboard daily-work loading does not wait for release-evidence reporting", () => {
  const dashboardPage = readFileSync(new URL("../src/app/page.tsx", import.meta.url), "utf8");

  assert.match(dashboardPage, /const data = await loadCompanies\(\);\s*if \(data\) await loadDeadlines\(data\);/);
  assert.doesNotMatch(
    dashboardPage,
    /Promise\.all\(\[loadCompanies\(\), loadReadiness\(\)/,
    "release assurance must not delay company/deadline work",
  );
});

function dashboardBatch(totalCompanies) {
  const counts = Object.fromEntries(DASHBOARD_DEADLINE_STATES.map((state) => [state, 0]));
  const items = Array.from({ length: totalCompanies }, (_, index) => {
    const companyId = index + 1;
    const state = DASHBOARD_DEADLINE_STATES[index % DASHBOARD_DEADLINE_STATES.length];
    counts[state] += 1;
    const requiresDeadline = ["overdue", "due-soon", "scheduled", "filed"].includes(state);
    return {
      companyId,
      companyName: `Company ${companyId}`,
      state,
      deadline: requiresDeadline ? {
        id: companyId,
        companyId,
        periodId: companyId + 1000,
        deadlineType: "CRO",
        calculatedDueDate: "2026-12-31",
        dueDate: "2026-12-31",
        filedDate: state === "filed" ? "2026-07-01" : null,
        filingReference: null,
        isLate: state === "overdue",
        penaltyAmount: 0,
        notes: null,
      } : null,
      message: `Explicit ${state} state`,
    };
  });
  return {
    totalCompanies,
    unavailableCount: counts.unavailable,
    counts,
    items,
  };
}
