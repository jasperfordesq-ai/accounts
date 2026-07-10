import assert from "node:assert/strict";
import test from "node:test";
import { getAuditLog, parseAuditLogPage } from "../src/lib/api.ts";

test("parseAuditLogPage accepts the stable third page of a 125-event trail", () => {
  const parsed = parseAuditLogPage(pageThreeFixture(), { companyId: 7, periodId: 3 });

  assert.equal(parsed.total, 125);
  assert.equal(parsed.page, 3);
  assert.equal(parsed.pageSize, 50);
  assert.equal(parsed.totalPages, 3);
  assert.equal(parsed.hasPreviousPage, true);
  assert.equal(parsed.hasNextPage, false);
  assert.equal(parsed.items.length, 25);
  assert.equal(parsed.items[0].id, 25);
  assert.equal(parsed.items.at(-1).id, 1);
});

test("getAuditLog requests the selected server page and runtime-validates its scope", async () => {
  const originalFetch = globalThis.fetch;
  let requestedUrl = "";
  globalThis.fetch = async (input) => {
    requestedUrl = String(input);
    return new Response(JSON.stringify(pageThreeFixture()), {
      status: 200,
      headers: { "Content-Type": "application/json" },
    });
  };

  try {
    const page = await getAuditLog(7, 3, 3, 50);
    assert.equal(page.page, 3);
  } finally {
    globalThis.fetch = originalFetch;
  }

  const query = new URL(requestedUrl, "http://accounts.test");
  assert.equal(query.pathname, "/api/companies/7/audit-log");
  assert.deepEqual(Object.fromEntries(query.searchParams), {
    page: "3",
    pageSize: "50",
    periodId: "3",
  });
});

test("parseAuditLogPage rejects unstable event order and inconsistent navigation metadata", () => {
  const unstable = pageThreeFixture();
  [unstable.items[0], unstable.items[1]] = [unstable.items[1], unstable.items[0]];
  assert.throws(
    () => parseAuditLogPage(unstable, { companyId: 7, periodId: 3 }),
    /events must use timestamp-descending, ID-descending order/,
  );

  const inconsistent = pageThreeFixture();
  inconsistent.hasNextPage = true;
  assert.throws(
    () => parseAuditLogPage(inconsistent, { companyId: 7, periodId: 3 }),
    /hasNextPage - inconsistent with page/,
  );
});

function pageThreeFixture() {
  const baseTimestamp = Date.UTC(2026, 6, 10, 12, 0, 0);
  return {
    total: 125,
    page: 3,
    pageSize: 50,
    totalPages: 3,
    hasPreviousPage: true,
    hasNextPage: false,
    items: Array.from({ length: 25 }, (_, index) => ({
      id: 25 - index,
      companyId: 7,
      periodId: 3,
      entityType: "ImportedTransaction",
      entityId: 25 - index,
      action: `transaction.audit.${25 - index}`,
      oldValueJson: null,
      newValueJson: JSON.stringify({ sequence: 25 - index }),
      userId: "accountant@example.ie",
      timestamp: new Date(baseTimestamp - (Math.floor(index / 2) * 60_000)).toISOString(),
    })),
  };
}
