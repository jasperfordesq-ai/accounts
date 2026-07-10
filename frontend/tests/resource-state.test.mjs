import assert from "node:assert/strict";
import test from "node:test";
import {
  INITIAL_RESOURCE_STATE,
  beginResourceLoad,
  canUseResourceAsEvidence,
  completeResourceLoad,
  failResourceLoad,
  loadResourceGroup,
  shouldRenderResourceEmpty,
} from "../src/lib/resourceState.ts";

test("resource states distinguish loading, empty, stale retry, partial error and hard error", () => {
  const loaded = completeResourceLoad(false);
  const empty = completeResourceLoad(true);
  const retrying = beginResourceLoad(loaded, true);
  const partial = failResourceLoad(
    { failedResourceKeys: ["deadlines"], errors: { deadlines: "HTTP 500" } },
    true,
  );
  const error = failResourceLoad(
    { failedResourceKeys: ["notes"], errors: { notes: "Request timed out" } },
    false,
  );

  assert.equal(INITIAL_RESOURCE_STATE.status, "loading");
  assert.equal(loaded.status, "loaded");
  assert.equal(empty.status, "empty");
  assert.equal(retrying.status, "stale/retrying");
  assert.equal(partial.status, "partial-error");
  assert.equal(error.status, "error");
  assert.equal(canUseResourceAsEvidence(empty), true);
  assert.equal(canUseResourceAsEvidence(partial), false);
  assert.equal(shouldRenderResourceEmpty(empty), true);
  assert.equal(shouldRenderResourceEmpty(error), false);
});

test("500, timeout and malformed failures retain successes and retry only failed resources", async () => {
  const calls = { companies: 0, deadlines: 0, readiness: 0, statements: 0 };
  let retryPhase = false;
  const loaders = {
    companies: async () => {
      calls.companies += 1;
      return [{ id: 1 }];
    },
    deadlines: async () => {
      calls.deadlines += 1;
      if (!retryPhase) throw new Error("HTTP 500");
      return [{ id: 4 }];
    },
    readiness: async () => {
      calls.readiness += 1;
      if (!retryPhase) throw new Error("Request timed out");
      return { status: "review-required" };
    },
    statements: async () => {
      calls.statements += 1;
      if (!retryPhase) throw new Error("Malformed response payload");
      return { balanced: true };
    },
  };

  const first = await loadResourceGroup(loaders);
  assert.deepEqual(first.values.companies, [{ id: 1 }]);
  assert.deepEqual(first.failedResourceKeys, ["deadlines", "readiness", "statements"]);

  retryPhase = true;
  const retried = await loadResourceGroup(loaders, first.failedResourceKeys);
  assert.deepEqual(retried.failedResourceKeys, []);
  assert.deepEqual(calls, { companies: 1, deadlines: 2, readiness: 2, statements: 2 });
});
