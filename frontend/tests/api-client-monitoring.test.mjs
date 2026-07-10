import assert from "node:assert/strict";
import test from "node:test";
import { ApiContractError } from "../src/lib/apiContracts.ts";
import {
  ApiError,
  getCompanies,
  updateCroFilingStatus,
} from "../src/lib/api.ts";

function installBrowserFetch(context, primaryFetch) {
  const originalWindow = globalThis.window;
  const originalDocument = globalThis.document;
  const originalFetch = globalThis.fetch;
  const monitoringCalls = [];

  globalThis.window = {
    location: {
      pathname: "/companies/42/periods/7/statements",
      search: "?client=client@example.ie&token=NeverSendThis",
      hash: "#amount-999999",
    },
  };
  globalThis.document = { cookie: "accounts_csrf=csrf-test-token" };
  globalThis.fetch = async (url, options) => {
    if (url === "/api/system/monitoring/client-event") {
      monitoringCalls.push({ url, options });
      return new Response("{}", { status: 202 });
    }
    return primaryFetch(url, options);
  };

  context.after(() => {
    globalThis.window = originalWindow;
    globalThis.document = originalDocument;
    globalThis.fetch = originalFetch;
  });
  return monitoringCalls;
}

test("terminal server rejection reports only route shape and safe correlation", async (context) => {
  const monitoringCalls = installBrowserFetch(context, async () => new Response(JSON.stringify({
    error: "client@example.ie balance 999999 token NeverSendThis",
    correlationId: "corr-safe_2026.07",
  }), {
    status: 503,
    statusText: "Service Unavailable",
    headers: { "Content-Type": "application/json" },
  }));

  await assert.rejects(
    updateCroFilingStatus(42, 7, {
      status: "Approved",
      reason: "client@example.ie balance 999999 token NeverSendThis",
    }),
    (error) => error instanceof ApiError && error.status === 503,
  );

  assert.equal(monitoringCalls.length, 1);
  const payload = JSON.parse(monitoringCalls[0].options.body);
  assert.deepEqual(payload, {
    eventCode: "api-server-rejection",
    route: "/companies/{id}/periods/{id}/statements",
    correlationId: "corr-safe_2026.07",
  });
  assert.doesNotMatch(monitoringCalls[0].options.body, /client@example\.ie|NeverSendThis|999999|Approved/);
});

test("runtime contract rejection emits a controlled event without response data", async (context) => {
  const monitoringCalls = installBrowserFetch(
    context,
    async () => new Response(JSON.stringify({
      companies: [{ legalName: "Sensitive Client Limited", taxReference: "NeverSendThis" }],
    }), { status: 200, headers: { "Content-Type": "application/json" } }),
  );

  await assert.rejects(getCompanies(), ApiContractError);

  assert.equal(monitoringCalls.length, 1);
  const payload = JSON.parse(monitoringCalls[0].options.body);
  assert.deepEqual(payload, {
    eventCode: "api-contract-rejection",
    route: "/companies/{id}/periods/{id}/statements",
  });
  assert.doesNotMatch(monitoringCalls[0].options.body, /Sensitive Client|taxReference|NeverSendThis/);
});

test("terminal network failure reports no exception message or request body", async (context) => {
  const monitoringCalls = installBrowserFetch(context, async () => {
    throw new TypeError("fetch failed for client@example.ie with NeverSendThis");
  });

  await assert.rejects(
    updateCroFilingStatus(42, 7, {
      status: "Approved",
      reason: "client@example.ie balance 999999 token NeverSendThis",
    }),
    /Network error/,
  );

  assert.equal(monitoringCalls.length, 1);
  const payload = JSON.parse(monitoringCalls[0].options.body);
  assert.deepEqual(payload, {
    eventCode: "api-network-failure",
    route: "/companies/{id}/periods/{id}/statements",
  });
  assert.doesNotMatch(monitoringCalls[0].options.body, /client@example\.ie|NeverSendThis|999999|Approved|fetch failed/);
});

test("terminal timeout emits only the controlled timeout event", async (context) => {
  const monitoringCalls = installBrowserFetch(context, async () => {
    throw new DOMException("client@example.ie NeverSendThis", "AbortError");
  });

  await assert.rejects(
    updateCroFilingStatus(42, 7, {
      status: "Approved",
      reason: "client@example.ie balance 999999 token NeverSendThis",
    }),
    /Request timed out/,
  );

  assert.equal(monitoringCalls.length, 1);
  assert.deepEqual(JSON.parse(monitoringCalls[0].options.body), {
    eventCode: "api-timeout",
    route: "/companies/{id}/periods/{id}/statements",
  });
  assert.doesNotMatch(monitoringCalls[0].options.body, /client@example\.ie|NeverSendThis|999999|Approved/);
});
