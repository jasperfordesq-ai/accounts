import { test } from 'node:test';
import assert from 'node:assert/strict';
import { TOOLS } from '../catalogue.js';
import { allParams } from '../tools.js';
import { paramName, isPathParam } from '../tool-input.js';
import { CORE_ROUTES, EXCLUDED_ROUTES, EXTRA_ROUTES } from '../route-coverage.js';
import { contract, contractOperations } from './helpers.js';

const toolRoutes = () => TOOLS.map((tool) => `${tool.method ?? 'GET'} ${tool.path}`);

test('the contract parser really finds the operations', () => {
  const operations = contractOperations();
  // A parser that silently found nothing would make every assertion below pass.
  assert.ok(operations.length >= 253, `expected at least 253 operations, found ${operations.length}`);
  for (const expected of [
    'GET /api/companies',
    'DELETE /api/companies/{companyId}/officers/{id}',
    'POST /api/companies/{companyId}/bank-accounts/{bankAccountId}/import',
    'POST /api/admin/users/{userId}/password-reset',
  ]) {
    assert.ok(operations.includes(expected), `expected to find ${expected}`);
  }
});

test('every contract operation is a tool or an exclusion with a reason', () => {
  const tools = new Set([...toolRoutes(), ...CORE_ROUTES.map((entry) => entry.route)]);
  const excluded = new Set(EXCLUDED_ROUTES.map((entry) => entry.route));
  const uncovered = contractOperations().filter((route) => !tools.has(route) && !excluded.has(route));
  assert.deepEqual(
    uncovered,
    [],
    'An operation in accounts-api-v1.json is neither a tool nor listed in mcp/src/route-coverage.ts. Decide which.',
  );
});

test('no exclusion is stale', () => {
  const operations = new Set(contractOperations());
  const stale = [...EXCLUDED_ROUTES, ...CORE_ROUTES].filter((entry) => !operations.has(entry.route)).map((e) => e.route);
  assert.deepEqual(stale, [], 'An excluded operation no longer exists in the contract. Remove it.');
});

test('a route is never both a tool and an exclusion', () => {
  const tools = new Set([...toolRoutes(), ...CORE_ROUTES.map((entry) => entry.route)]);
  assert.deepEqual(EXCLUDED_ROUTES.filter((entry) => tools.has(entry.route)).map((e) => e.route), []);
});

test('every exclusion and extra route gives a real reason', () => {
  for (const entry of [...EXCLUDED_ROUTES, ...EXTRA_ROUTES]) {
    assert.ok(entry.reason.trim().length > 40, `${entry.route}: the reason must be a sentence someone can disagree with`);
  }
});

test('the excluded set is exactly the browser-auth, health, monitoring, connector-credential and OAuth routes', () => {
  const excluded = EXCLUDED_ROUTES.map((entry) => entry.route).sort();
  assert.deepEqual(excluded, [
    'GET /api/auth/me',
    'GET /api/oauth/authorize/describe',
    'GET /api/oauth/metadata/authorization-server',
    'GET /api/oauth/metadata/protected-resource',
    'GET /health',
    'GET /health/live',
    'GET /health/ready',
    'POST /api/auth/connector/approve',
    'POST /api/auth/connector/login',
    'POST /api/auth/connector/logout',
    'POST /api/auth/connector/refresh',
    'POST /api/auth/invitations/accept',
    'POST /api/auth/login',
    'POST /api/auth/logout',
    'POST /api/auth/mfa/challenge',
    'POST /api/auth/mfa/recovery-codes',
    'POST /api/auth/password',
    'POST /api/auth/reauthenticate',
    'POST /api/auth/recovery/complete',
    'POST /api/oauth/authorize',
    'POST /api/oauth/register',
    'POST /api/oauth/revoke',
    'POST /api/oauth/token',
    'POST /api/system/monitoring/client-event',
    'POST /api/system/monitoring/error-smoke',
  ]);
  assert.ok(!TOOLS.some((tool) => tool.path === '/api/auth/connector/approve'), 'approve must never be a tool');
  assert.ok(!TOOLS.some((tool) => tool.path.startsWith('/api/oauth/')), 'the OAuth server is never a tool');
});

test('every tool calls a route the contract describes, or a declared extra route', () => {
  const operations = new Set(contractOperations());
  const extras = new Set(EXTRA_ROUTES.map((entry) => entry.route));
  const unknown = toolRoutes().filter((route) => !operations.has(route) && !extras.has(route));
  assert.deepEqual(unknown, [], 'A tool calls a route that does not exist. Check the path spelling against the contract.');
});

test('extra routes are not already in the contract', () => {
  const operations = new Set(contractOperations());
  const stale = EXTRA_ROUTES.filter((entry) => operations.has(entry.route)).map((entry) => entry.route);
  assert.deepEqual(stale, [], 'An extra route is now in the committed contract: remove it from EXTRA_ROUTES.');
});

test('tool names are unique snake_case and every route is served once', () => {
  const names = TOOLS.map((tool) => tool.name);
  assert.equal(new Set(names).size, names.length, 'duplicate tool name');
  for (const name of names) assert.match(name, /^[a-z][a-z0-9]*(?:_[a-z0-9]+)*$/, name);
  const routes = toolRoutes();
  assert.equal(new Set(routes).size, routes.length, 'two tools serve the same route');
});

test('path and query parameters agree with the contract', () => {
  const paths = contract().paths;
  for (const tool of TOOLS) {
    const operation = paths[tool.path]?.[(tool.method ?? 'GET').toLowerCase()];
    if (!operation) continue; // an extra route
    const declared = new Map((operation.parameters ?? []).map((p) => [p.name, p]));
    for (const spec of allParams(tool)) {
      const name = paramName(spec);
      const param = declared.get(name);
      assert.ok(param, `${tool.name}: parameter ${name} is not in the contract`);
      const schema = (param.schema ?? {}) as { type?: unknown; format?: unknown };
      const types = Array.isArray(schema.type) ? schema.type : [schema.type];
      if (isPathParam(spec)) assert.equal(param.in, 'path', `${tool.name}: ${name} should be a path parameter`);
      else assert.equal(param.in, 'query', `${tool.name}: ${name} should be a query parameter`);
      if (spec.kind === 'intId' || spec.kind === 'intFilter' || spec.kind === 'page' || spec.kind === 'pageSize' || spec.kind === 'count') {
        assert.ok(types.includes('integer'), `${tool.name}: ${name} is not an integer in the contract`);
      }
      if (spec.kind === 'guid') assert.equal(schema.format, 'uuid', `${tool.name}: ${name} is not a GUID in the contract`);
      if (spec.kind === 'flag') assert.ok(types.includes('boolean'), `${tool.name}: ${name} is not a boolean in the contract`);
    }
    for (const [name, param] of declared) {
      if (param.in === 'path') {
        assert.ok(allParams(tool).some((spec) => paramName(spec) === name), `${tool.name}: path parameter ${name} is not declared`);
      }
    }
  }
});

test('tool counts per group match the catalogue design', () => {
  const counts: Record<string, number> = {};
  for (const tool of TOOLS) counts[tool.group] = (counts[tool.group] ?? 0) + 1;
  assert.deepEqual(counts, {
    companies: 10, officers: 4, periods: 4, banking: 5, transactions: 6, categories: 8, classification: 11,
    year_end: 45, disclosures: 20, adjustments: 7, audit: 4, statements: 7, working_papers: 7, documents: 9,
    revenue: 12, deadlines: 7, charity: 16, filing: 19, handoff: 7, admin_users: 10, privacy: 5, operations: 3,
    system: 2, connector: 5,
  });
  assert.equal(TOOLS.length, 233);
});
