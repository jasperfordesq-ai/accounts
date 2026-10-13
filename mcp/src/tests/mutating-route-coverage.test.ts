import { test } from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { TOOLS } from '../catalogue.js';
import { levelOf } from '../tools.js';
import {
  ADMIN_EXACT_GET,
  ADMIN_EXACT_POST,
  ADMIN_POST_SUFFIXES,
  ADMIN_PREFIXES,
  CONFIRM_CONTAINS,
  CONFIRM_EXEMPT_PREFIXES,
  CONFIRM_EXACT,
  CONFIRM_PREFIXES,
  CONFIRM_SUFFIXES,
  concretePath,
  expectedLevel,
  requiresHumanConfirmation,
} from '../route-coverage.js';
import { API_ROOT, contractOperations } from './helpers.js';

const CATALOG = resolve(API_ROOT, 'Services/PrivilegedRouteCatalog.cs');

/** The body of one method of PrivilegedRouteCatalog, up to the next public member. */
function methodBody(name: string): string {
  assert.ok(existsSync(CATALOG), 'Services/PrivilegedRouteCatalog.cs is missing');
  const text = readFileSync(CATALOG, 'utf8');
  const start = text.indexOf(`public static bool ${name}(`);
  assert.ok(start >= 0, `${name} not found: the parser is broken, not the rule`);
  const next = text.indexOf('public static', start + 10);
  return text.slice(start, next < 0 ? undefined : next);
}

function literals(source: string, call: RegExp): string[] {
  return [...source.matchAll(call)].map((match) => match[1]!).sort();
}

test('the C# rule parser really finds the rules', () => {
  const confirm = methodBody('RequiresHumanConfirmation');
  const suffixes = literals(confirm, /EndsWith\("([^"]+)"/g);
  assert.ok(suffixes.length >= 10, `found only ${suffixes.length} EndsWith rules`);
  assert.ok(suffixes.includes('/mark-filed'));
  assert.ok(literals(methodBody('IsAdminLevel'), /StartsWithSegments\("([^"]+)"/g).length >= 4);
});

test('the connector mirrors FilingBridge\'s human-confirmation rules exactly', () => {
  const source = methodBody('RequiresHumanConfirmation');
  assert.deepEqual(literals(source, /EndsWith\("([^"]+)"/g), [...CONFIRM_SUFFIXES].sort(), 'suffix rules differ');
  assert.deepEqual(literals(source, /Contains\("([^"]+)"/g), [...CONFIRM_CONTAINS].sort(), 'fragment rules differ');
  assert.deepEqual(
    literals(source, /StartsWithSegments\("([^"]+)"/g).filter((prefix) => prefix !== '/api'),
    [...CONFIRM_EXEMPT_PREFIXES, ...CONFIRM_PREFIXES].sort(),
    'prefix rules differ',
  );
  assert.deepEqual(literals(source, /\.Equals\("([^"]+)"/g), [...CONFIRM_EXACT].sort(), 'exact rules differ');
});

test('the connector mirrors FilingBridge\'s admin-level rules exactly', () => {
  const source = methodBody('IsAdminLevel');
  assert.deepEqual(literals(source, /StartsWithSegments\("([^"]+)"/g), [...ADMIN_PREFIXES].sort(), 'admin prefixes differ');
  assert.deepEqual(
    literals(source, /\.Equals\("([^"]+)"/g),
    [...ADMIN_EXACT_GET, ...ADMIN_EXACT_POST].sort(),
    'admin exact routes differ',
  );
  assert.deepEqual(literals(source, /EndsWith\("([^"]+)"/g), [...ADMIN_POST_SUFFIXES].sort(), 'admin suffixes differ');
});

test('every tool marks approval exactly when FilingBridge requires a person to confirm', () => {
  const wrong = TOOLS.filter((tool) =>
    (tool.approval === true) !== requiresHumanConfirmation(tool.method ?? 'GET', concretePath(tool.path)))
    .map((tool) => `${tool.name} (${tool.method ?? 'GET'} ${tool.path}): approval=${tool.approval === true}`);
  assert.deepEqual(wrong, []);
});

test('every DELETE in the contract is a tool that needs approval', () => {
  const deletes = contractOperations().filter((route) => route.startsWith('DELETE '));
  assert.ok(deletes.length >= 20, 'the contract parser found too few DELETE operations');
  for (const route of deletes) {
    const tool = TOOLS.find((t) => `${t.method ?? 'GET'} ${t.path}` === route);
    assert.ok(tool, `${route} has no tool`);
    assert.equal(tool.approval, true, `${tool.name} removes something and must need approval`);
    assert.equal(tool.level, 'admin', `${tool.name} must be admin level`);
  }
});

test('every tool declares exactly the level its route needs', () => {
  const wrong = TOOLS.filter((tool) => levelOf(tool) !== expectedLevel(tool.method ?? 'GET', concretePath(tool.path)))
    .map((tool) => `${tool.name}: ${levelOf(tool)} but the route needs ${expectedLevel(tool.method ?? 'GET', concretePath(tool.path))}`);
  assert.deepEqual(wrong, []);
});

test('no read tool is above read level unless its route is admin level, and no change is below write', () => {
  for (const tool of TOOLS) {
    if (tool.method) assert.notEqual(levelOf(tool), 'read', `${tool.name} changes something at read level`);
  }
});

test('approval tools are marked destructive in their description and annotations', () => {
  for (const tool of TOOLS.filter((t) => t.approval)) {
    assert.match(tool.description, /approve/i, `${tool.name} must describe the approval step`);
  }
});

test('the user credential tools are admin, approved and hand back a credential', () => {
  for (const name of ['user_create', 'user_password_reset', 'user_invite']) {
    const tool = TOOLS.find((t) => t.name === name);
    assert.ok(tool, name);
    assert.equal(tool.level, 'admin');
    assert.equal(tool.approval, true);
    assert.equal(tool.returnsCredential, true);
  }
});
