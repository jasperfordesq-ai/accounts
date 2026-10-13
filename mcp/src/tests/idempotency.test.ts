import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { TOOLS } from '../catalogue.js';
import { REPLAY_SAFE_ROUTES, honoursIdempotencyKey } from '../idempotency.js';
import { API_ROOT, contractOperations } from './helpers.js';

const ENDPOINTS = resolve(API_ROOT, 'Endpoints');

/** What makes a handler honour the key: the shared contract, or reading the header itself. */
const HONOURS = /IdempotencyHttpContract\.(ExecuteAsync|RequestHeader)\b/;

interface Method { qualified: string; body: string }

/** Every static method of an endpoint class, as Class.Name. */
function methodsOf(file: string, text: string): Method[] {
  const className = /\bclass\s+(\w+)/.exec(text)?.[1] ?? file;
  const starts = [...text.matchAll(/^ {4}(?:public|private|internal) static [^\n(]*?\b(\w+)(?:<[^>(]*>)?\(/gm)];
  return starts.map((match, index) => ({
    qualified: `${className}.${match[1]!}`,
    body: text.slice(match.index!, starts[index + 1]?.index ?? text.length),
  }));
}

/** C# route templates become the contract's: constraints dropped, trailing slash removed. */
function normalise(route: string): string {
  const out = route.replace(/\{(\w+):[^}]+\}/g, '{$1}').replace(/\/+$/, '').replace(/\/{2,}/g, '/');
  return out === '' ? '/' : out;
}

/**
 * Every `METHOD /route` whose handler stores the Idempotency-Key with its
 * outcome, read from backend/Accounts.Api/Endpoints. A lambda handler is the
 * text up to the next mapping; a method-group handler is that method's body,
 * followed into the helpers it calls.
 */
function backendReplaySafeRoutes(): string[] {
  const files = readdirSync(ENDPOINTS).filter((name) => name.endsWith('.cs'));
  const sources = new Map(files.map((name) => [name, readFileSync(resolve(ENDPOINTS, name), 'utf8')]));
  // Keyed Class.Method only: a bare call resolves within its own class
  // (partial classes span files), a qualified one across classes.
  const methods = new Map<string, Method>();
  const classOf = (file: string) => /\bclass\s+(\w+)/.exec(sources.get(file)!)?.[1] ?? file;
  for (const [file, text] of sources) {
    for (const method of methodsOf(file, text)) methods.set(method.qualified, method);
  }
  const resolveCall = (owner: string, call: string) =>
    methods.get(call.includes('.') ? call : `${owner}.${call}`);

  const direct = (owner: string, body: string, seen = new Set<string>()): boolean => {
    if (HONOURS.test(body)) return true;
    for (const call of body.matchAll(/\b((?:\w+\.)?\w+)(?:<[^>(]*>)?\(/g)) {
      const helper = resolveCall(owner, call[1]!);
      if (!helper || seen.has(helper.qualified)) continue;
      seen.add(helper.qualified);
      if (direct(helper.qualified.split('.')[0]!, helper.body, seen)) return true;
    }
    return false;
  };

  const found: string[] = [];
  for (const [file, text] of sources) {
    const owner = classOf(file);
    const maps = [...text.matchAll(/\b(\w+)\.Map(Get|Post|Put|Delete|Patch)\(\s*"([^"]*)"\s*,\s*([\w.]+)/g)];
    for (const [index, match] of maps.entries()) {
      const [, receiver, verb, route, handler] = match;
      if (verb === 'Get') continue;
      let body: string;
      let bodyOwner = owner;
      if (handler === 'async' || handler === '(' || /^[a-z]/.test(handler!)) {
        // Up to the next mapping or the next method, whichever comes first.
        const nextMap = maps[index + 1]?.index ?? text.length;
        const nextMethod = [...text.matchAll(/^ {4}(?:public|private|internal) static /gm)]
          .map((m) => m.index!).find((at) => at > match.index!) ?? text.length;
        body = text.slice(match.index!, Math.min(nextMap, nextMethod));
      } else {
        const method = resolveCall(owner, handler!);
        assert.ok(method, `handler ${handler} for ${verb} ${route} not found: the parser is broken`);
        body = method.body;
        bodyOwner = method.qualified.split('.')[0]!;
      }
      if (!direct(bodyOwner, body)) continue;

      let prefix = '';
      if (receiver !== 'app') {
        const before = text.slice(0, match.index!);
        const declarations = [...before.matchAll(new RegExp(String.raw`var\s+${receiver}\s*=\s*app\.MapGroup\((\$?)"([^"]+)"\)`, 'g'))];
        const declaration = declarations.at(-1);
        assert.ok(declaration, `group ${receiver} for ${verb} ${route} not found: the parser is broken`);
        prefix = declaration[2]!;
        if (declaration[1] === '$') {
          prefix = prefix.replace(/\{(\w+)\}/g, (_m, name: string) => {
            const value = new RegExp(String.raw`(?:var|const string|string)\s+${name}\s*=\s*"([^"]+)"`).exec(before)?.[1];
            assert.ok(value, `${name} in group ${receiver} not resolved: the parser is broken`);
            return value;
          });
        }
      }
      found.push(`${verb!.toUpperCase()} ${normalise(prefix + route)}`);
    }
  }
  return [...new Set(found)].sort();
}

test('the endpoint parser really finds the routes that replay a repeated key', () => {
  const routes = backendReplaySafeRoutes();
  assert.ok(routes.length >= 15, `found only ${routes.length}`);
  for (const expected of [
    'POST /api/companies',
    'POST /api/companies/onboard',
    'PUT /api/companies/{companyId}/periods/{id}/status',
    'POST /api/companies/{companyId}/periods/{periodId}/external-filing-handoff/cro/snapshots',
  ]) {
    assert.ok(routes.includes(expected), `expected ${expected} in ${routes.join(', ')}`);
  }
  assert.ok(!routes.includes('POST /api/companies/{companyId}/officers'), 'officer creation does not honour the key');
  const contract = new Set(contractOperations());
  assert.deepEqual(routes.filter((route) => !contract.has(route)), [], 'a parsed route is not in the contract');
});

test('the connector claims replay exactly where FilingBridge honours Idempotency-Key', () => {
  const backend = backendReplaySafeRoutes();
  const claimed = [...REPLAY_SAFE_ROUTES].sort();
  assert.deepEqual(
    claimed.filter((route) => !backend.includes(route)),
    [],
    'The connector would repeat a change on a route that does not replay it: it would be recorded twice.',
  );
  assert.deepEqual(
    backend.filter((route) => !claimed.includes(route)),
    [],
    'A route now honours Idempotency-Key: list it in mcp/src/idempotency.ts so it gets a key and a safe retry.',
  );
});

test('every replay-safe route is a tool route, and an ordinary create is not replay-safe', () => {
  const toolRoutes = new Set(TOOLS.map((tool) => `${tool.method ?? 'GET'} ${tool.path}`));
  assert.deepEqual(REPLAY_SAFE_ROUTES.filter((route) => !toolRoutes.has(route)), []);
  assert.ok(!honoursIdempotencyKey('POST', '/api/companies/{companyId}/officers'));
});
