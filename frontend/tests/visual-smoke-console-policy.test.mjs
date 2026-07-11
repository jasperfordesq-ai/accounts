import assert from "node:assert/strict";
import test from "node:test";
import {
  isExpectedAnonymousSessionProbeConsoleError,
  safeVisualSmokeResponseRoute,
  VISUAL_SMOKE_DASHBOARD_DISCOVERY_TEXT,
  unexpectedVisualSmokeBrowserErrors,
} from "../scripts/visual-smoke.mjs";

const sessionUrl = "https://accounts-smoke.local/api/auth/me";
const expected = {
  state: { id: "login", authMode: "anonymous" },
  message: {
    type: "error",
    text: "Failed to load resource: the server responded with a status of 401 ()",
    location: { url: sessionUrl, lineNumber: 0, columnNumber: 0 },
  },
  failedResponses: [{ url: sessionUrl, status: 401, method: "GET" }],
  pageUrl: "https://accounts-smoke.local/login",
};

test("visual smoke waits for the report-backed dashboard release summary", () => {
  assert.equal(VISUAL_SMOKE_DASHBOARD_DISCOVERY_TEXT, "Platform release status");
});

test("visual smoke permits only the expected anonymous login session probe 401", () => {
  assert.equal(isExpectedAnonymousSessionProbeConsoleError(expected), true);
  assert.equal(isExpectedAnonymousSessionProbeConsoleError({
    ...expected,
    message: { ...expected.message, text: "Failed to load resource: the server responded with a status of 401 (Unauthorized)" },
  }), true);

  const rejected = [
    { ...expected, state: { id: "dashboard", authMode: "authenticated" } },
    { ...expected, state: { id: "login", authMode: "authenticated" } },
    { ...expected, pageUrl: "https://accounts-smoke.local/" },
    { ...expected, pageUrl: "https://accounts-smoke.local/dashboard" },
    { ...expected, pageUrl: "https://accounts-smoke.local/login?probe=1" },
    { ...expected, pageUrl: "not-a-url" },
    { ...expected, message: { ...expected.message, type: "warning" } },
    { ...expected, message: { ...expected.message, text: "Failed to load resource: the server responded with a status of 403 ()" } },
    { ...expected, message: { ...expected.message, location: { url: "https://accounts-smoke.local/api/users" } } },
    { ...expected, message: { ...expected.message, location: { url: `${sessionUrl}?probe=1` } } },
    { ...expected, message: { ...expected.message, location: { url: "https://attacker.invalid/api/auth/me" } } },
    { ...expected, failedResponses: [] },
    { ...expected, failedResponses: [{ url: sessionUrl, status: 401, method: "POST" }] },
    { ...expected, failedResponses: [{ url: sessionUrl, status: 403, method: "GET" }] },
    { ...expected, failedResponses: [{ url: "https://accounts-smoke.local/api/users", status: 401, method: "GET" }] },
  ];

  for (const candidate of rejected) {
    assert.equal(isExpectedAnonymousSessionProbeConsoleError(candidate), false);
  }

  const duplicateMessage = structuredClone(expected.message);
  assert.deepEqual(
    unexpectedVisualSmokeBrowserErrors({
      state: expected.state,
      consoleErrors: [expected.message, duplicateMessage],
      pageErrors: ["synthetic page failure"],
      failedResponses: expected.failedResponses,
      pageUrl: expected.pageUrl,
    }),
    [
      "pageerror: synthetic page failure",
      `console: ${duplicateMessage.text} at /api/auth/me`,
    ],
    "one response may suppress only one matching console event while page errors always remain blocking",
  );
});

test("visual smoke reports unmatched HTTP failures with privacy-safe route evidence", () => {
  const failedUrl = "https://accounts-smoke.local/api/companies/123/periods/456/director-loan-compliance?presenter=jane@example.ie";
  const consoleMessage = {
    type: "error",
    text: "Failed to load resource: the server responded with a status of 404 ()",
    location: { url: failedUrl, lineNumber: 0, columnNumber: 0 },
  };

  assert.deepEqual(
    unexpectedVisualSmokeBrowserErrors({
      state: { id: "year-end", authMode: "authenticated" },
      consoleErrors: [consoleMessage],
      pageErrors: [],
      failedResponses: [{ url: failedUrl, status: 404, method: "GET" }],
      pageUrl: "https://accounts-smoke.local/companies/123/periods/456/year-end",
    }),
    [
      `console: ${consoleMessage.text} at /api/companies/{id}/periods/{id}/director-loan-compliance`,
      "response: GET /api/companies/{id}/periods/{id}/director-loan-compliance returned 404",
    ],
  );
  assert.equal(safeVisualSmokeResponseRoute("https://accounts-smoke.local/reset-password/secret-token"), "/{redacted}");
  assert.equal(safeVisualSmokeResponseRoute("not-a-url"), "");
});
