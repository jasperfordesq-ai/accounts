import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";

// Every mutating API route (POST/PUT/PATCH/DELETE) the frontend calls must be in the permission
// action catalog, which FrontendPermissionMatrixTests checks against the backend role gate. Routes
// deliberately left out are listed below, each with its reason.

const root = path.resolve(import.meta.dirname, "..");

const ALLOW_LIST = new Map([
  // Machine telemetry from the browser, accepted from every signed-in user (even password-locked); not a user action.
  ["POST /api/system/monitoring/client-event", "browser monitoring telemetry"],
  // Sign-in, before any session or role exists.
  ["POST /api/auth/login", "pre-authentication sign-in"],
  // Second sign-in factor, before any session or role exists.
  ["POST /api/auth/mfa/challenge", "pre-authentication MFA challenge"],
  // Session security step open to every signed-in role (RoleAuthorizationService.IsAuthenticatedSecurityRequest).
  ["POST /api/auth/reauthenticate", "authenticated session re-verification"],
  // One-time invitation token flow, before any session exists.
  ["POST /api/auth/invitations/accept", "pre-authentication invitation token"],
  // One-time password-recovery token flow, before any session exists.
  ["POST /api/auth/recovery/complete", "pre-authentication recovery token"],
  // Effective gate is Owner only (the endpoint refuses others); the role service alone also admits Accountant, so no single catalog permission matches both.
  ["POST /api/companies/onboard", "endpoint narrows the role-service gate to Owner"],
  // Effective gate is Owner only (the endpoint refuses others); the role service alone also admits Accountant, so no single catalog permission matches both.
  ["POST /api/operations/deadline-reminders/run", "endpoint narrows the role-service gate to Owner"],
]);

const segments = (value) => value.split("/").filter(Boolean);

function mutatingRoutes() {
  const document = JSON.parse(fs.readFileSync(path.join(root, "..", "backend", "Accounts.Api", "OpenApi", "accounts-api-v1.json"), "utf8"));
  return Object.entries(document.paths).flatMap(([routePath, operations]) =>
    Object.keys(operations)
      .filter((method) => ["post", "put", "patch", "delete"].includes(method))
      .map((method) => ({ method: method.toUpperCase(), path: routePath })));
}

function sourceFiles(directory) {
  return fs.readdirSync(directory, { withFileTypes: true }).flatMap((entry) => entry.isDirectory()
    ? (entry.name === "generated" ? [] : sourceFiles(path.join(directory, entry.name)))
    : /\.(ts|tsx)$/.test(entry.name) ? [path.join(directory, entry.name)] : []);
}

// The frontend's API calls: every "/api/..." literal, with path helpers such as
// `const base = (id) => \`/api/...\`` resolved, and the HTTP method read from the call.
function frontendCalls() {
  const sources = sourceFiles(path.join(root, "src")).map((file) => fs.readFileSync(file, "utf8"));
  const helpers = new Map();
  for (const source of sources) {
    for (const match of source.matchAll(/const\s+(\w+)\s*=\s*\([^)]*\)\s*=>\s*`(\/api\/[^`]*)`/g)) helpers.set(match[1], match[2]);
  }
  const calls = [];
  for (const raw of sources) {
    const source = raw
      .replace(/\$\{API_BASE\}/g, "")
      .replace(/\$\{(\w+)(\([^)]*\))?\}(?=\/)/g, (all, name) => helpers.get(name) ?? all);
    const literals = [...source.matchAll(/["'`](\/api\/[^"'`]*)["'`]/g)];
    literals.forEach((match, index) => {
      const end = Math.min(index + 1 < literals.length ? literals[index + 1].index : source.length, match.index + 400);
      const before = source.slice(Math.max(0, match.index - 60), match.index);
      const after = source.slice(match.index, end);
      const method = /postApiJson/.test(before) ? "POST"
        : /putApiJson/.test(before) ? "PUT"
        : (after.match(/"(POST|PUT|PATCH|DELETE)"/) ?? [null, "GET"])[1];
      calls.push({ method, path: match[1].replace(/\$\{[^}]*\}/g, "{}").replace(/\?.*$/, "") });
    });
  }
  return calls;
}

// A route template matches a concrete path segment by segment; "{...}" matches any value. A frontend
// placeholder ("{}") may only fill a template parameter.
function matches(template, concrete) {
  const expected = segments(template);
  const actual = segments(concrete);
  return expected.length === actual.length && expected.every((part, index) => part.startsWith("{")
    || (actual[index] !== "{}" && part.toLowerCase() === actual[index].toLowerCase()));
}

test("every mutating route the frontend calls is in the permission action catalog or explicitly allow-listed", () => {
  const catalog = JSON.parse(fs.readFileSync(path.join(root, "src", "lib", "permission-action-catalog.json"), "utf8"));
  const routes = mutatingRoutes();
  const calls = frontendCalls();
  const key = (route) => `${route.method} ${route.path}`;
  const catalogued = (route) => catalog.some((action) => action.method === route.method && matches(route.path, action.path));
  const called = routes.filter((route) => calls.some((call) => call.method === route.method && matches(route.path, call.path)));

  // Guards the scanner itself: it must keep finding the frontend's mutating calls.
  assert.ok(called.length >= 120, `Only ${called.length} mutating frontend calls were found; the scanner is broken.`);

  const missing = called.filter((route) => !catalogued(route) && !ALLOW_LIST.has(key(route))).map(key);
  assert.deepEqual(missing, [], "Add these routes to permission-action-catalog.json (or allow-list them with a reason).");

  for (const [routeKey, reason] of ALLOW_LIST) {
    assert.ok(reason.trim().length > 0, `${routeKey} needs a reason.`);
    const route = routes.find((candidate) => key(candidate) === routeKey);
    assert.ok(route, `Allow-listed ${routeKey} is not a mutating route in the OpenAPI contract.`);
    assert.ok(!catalogued(route), `Allow-listed ${routeKey} is also catalogued; remove it from the allow-list.`);
  }
});
