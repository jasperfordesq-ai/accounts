import assert from "node:assert/strict";
import test from "node:test";

import { getDeadlineRiskQueue, retryDeadlineReminder } from "../src/lib/operations.ts";

const validItem = {
  outboxId: "11111111-1111-4111-8111-111111111111",
  companyId: 7,
  companyLegalName: "Synthetic Limited",
  periodId: 9,
  deadlineType: 0,
  reminderKind: 1,
  state: 2,
  dueDate: "2026-07-01",
  attemptCount: 2,
  nextAttemptAtUtc: "2026-07-10T12:00:00Z",
  lastFailureCode: "provider-unavailable",
};

test("deadline risk runtime contract accepts the fixed tenant-safe workflow shape", async (context) => {
  const originalFetch = globalThis.fetch;
  context.after(() => { globalThis.fetch = originalFetch; });
  globalThis.fetch = async () => new Response(JSON.stringify([validItem]), { status: 200 });
  const result = await getDeadlineRiskQueue();
  assert.deepEqual(result, [validItem]);
});

test("deadline risk runtime contract rejects recipient or payload PII fields", async (context) => {
  const originalFetch = globalThis.fetch;
  context.after(() => { globalThis.fetch = originalFetch; });
  globalThis.fetch = async () => new Response(JSON.stringify([{ ...validItem, recipientEmail: "client@example.ie" }]), { status: 200 });
  await assert.rejects(getDeadlineRiskQueue(), /Unrecognized key/);
});

test("retry uses the bounded outbox route and does not send a client payload", async (context) => {
  const originalFetch = globalThis.fetch;
  context.after(() => { globalThis.fetch = originalFetch; });
  let captured;
  globalThis.fetch = async (input, init) => {
    captured = { input: String(input), init };
    return new Response(null, { status: 204 });
  };
  await retryDeadlineReminder(validItem.outboxId);
  assert.equal(captured.input, `/api/operations/deadline-reminders/${validItem.outboxId}/retry`);
  assert.equal(captured.init.method, "POST");
  assert.equal(captured.init.body, undefined);
});
