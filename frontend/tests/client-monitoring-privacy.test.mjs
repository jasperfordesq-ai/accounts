import assert from "node:assert/strict";
import test from "node:test";
import {
  buildClientMonitoringPayload,
  reportClientMonitoringEvent,
  sanitizeClientRoute,
} from "../src/lib/clientMonitoring.ts";

test("client monitoring payload keeps only controlled event, route shape, and safe correlation", () => {
  const payload = buildClientMonitoringPayload(
    "api-server-rejection",
    "/companies/742/periods/73/client@example.ie?token=NeverSendThis#bank-row",
    "corr-safe_2026.07",
  );

  assert.deepEqual(payload, {
    eventCode: "api-server-rejection",
    route: "/companies/{id}/periods/{id}/{redacted}",
    correlationId: "corr-safe_2026.07",
  });
  const json = JSON.stringify(payload);
  assert.doesNotMatch(json, /client@example\.ie|NeverSendThis|bank-row/);
  assert.deepEqual(Object.keys(payload).sort(), ["correlationId", "eventCode", "route"]);
});

test("unsafe correlation and malformed route segments fail closed without error data", () => {
  const payload = buildClientMonitoringPayload(
    "render-exception",
    "/companies/%E0%A4%A/forms/account%20name",
    "person@example.ie:password=NeverSendThis",
  );

  assert.equal(payload.route, "/companies/{redacted}/{redacted}/{redacted}");
  assert.equal(payload.correlationId, undefined);
  assert.deepEqual(Object.keys(payload).sort(), ["eventCode", "route"]);
});

test("route sanitizer strips query, hash, numeric, GUID, email, and long identifiers", () => {
  assert.equal(
    sanitizeClientRoute("/companies/42/550e8400-e29b-41d4-a716-446655440000/person@example.ie/" + "x".repeat(80) + "?secret=yes"),
    "/companies/{id}/{redacted}/{redacted}/{redacted}",
  );
});

test("route sanitizer allowlists only application route vocabulary", () => {
  assert.equal(
    sanitizeClientRoute("/companies/client%40example.ie/periods/ACME-2026/statements"),
    "/companies/{redacted}/periods/{redacted}/statements",
  );
  assert.equal(
    sanitizeClientRoute("/companies/{id}/periods/{id}/year-end"),
    "/companies/{id}/periods/{id}/year-end",
  );
});

test("reporter sends a minimal sanitized body and deduplicates changing correlations", async (context) => {
  const originalWindow = globalThis.window;
  const originalDocument = globalThis.document;
  const originalFetch = globalThis.fetch;
  const calls = [];
  globalThis.window = { location: { pathname: "/companies/42" } };
  globalThis.document = { cookie: "accounts_csrf=csrf-test-token" };
  globalThis.fetch = async (url, options) => {
    calls.push({ url, options });
    return { ok: true };
  };
  context.after(() => {
    globalThis.window = originalWindow;
    globalThis.document = originalDocument;
    globalThis.fetch = originalFetch;
  });

  await reportClientMonitoringEvent("api-network-failure", {
    route: "/companies/client%40example.ie?token=NeverSendThis",
    correlationId: "corr-one",
  });
  await reportClientMonitoringEvent("api-network-failure", {
    route: "/companies/another%40example.ie?token=DifferentSecret",
    correlationId: "corr-two",
  });

  assert.equal(calls.length, 1);
  assert.equal(calls[0].url, "/api/system/monitoring/client-event");
  const body = JSON.parse(calls[0].options.body);
  assert.deepEqual(body, {
    eventCode: "api-network-failure",
    route: "/companies/{redacted}",
    correlationId: "corr-one",
  });
  assert.doesNotMatch(calls[0].options.body, /example\.ie|NeverSendThis|DifferentSecret/);
});
