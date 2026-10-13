import { test } from 'node:test';
import assert from 'node:assert/strict';
import { buildRequest, inputSchemaFor, type ParamSpec } from '../tool-input.js';
import { bodySchemaFor, buildBody, type FieldSpec } from '../tool-body.js';
import { mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createHash } from 'node:crypto';

const PARAMS: ParamSpec[] = [
  { kind: 'intId', name: 'companyId' },
  { kind: 'guid', name: 'snapshotId' },
  { kind: 'page' },
  { kind: 'pageSize', max: 200 },
  { kind: 'flag', name: 'uncategorised', describe: 'x' },
  { kind: 'enum', name: 'sortBy', values: ['date', 'amount'], describe: 'x' },
  { kind: 'dateQuery', name: 'asOf', describe: 'x' },
  { kind: 'text', name: 'search', max: 10, describe: 'x' },
];
const PATH = '/api/companies/{companyId}/x/{snapshotId}';
const GUID = '0f8fad5b-d9cb-469f-a165-70867728950e';

test('identifiers are substituted and the query is built from declared parameters only', () => {
  const built = buildRequest(PATH, PARAMS, {
    companyId: 12, snapshotId: GUID.toUpperCase(), page: 2, pageSize: 50, uncategorised: false, sortBy: 'amount', asOf: '2026-03-31', search: 'rent',
  });
  assert.equal(built.path, `/api/companies/12/x/${GUID}`);
  assert.deepEqual(built.query, { page: '2', pageSize: '50', uncategorised: 'false', sortBy: 'amount', asOf: '2026-03-31', search: 'rent' });
});

test('a path identifier cannot open a path segment, traverse or start a query', () => {
  for (const bad of ['12/../../admin', '12?x=1', '0', '-1', '1.5', '../1', '', 'abc']) {
    assert.throws(() => buildRequest(PATH, PARAMS, { companyId: bad, snapshotId: GUID }), /identifier/, bad);
  }
  assert.throws(() => buildRequest(PATH, PARAMS, { companyId: 1, snapshotId: '../x' }), /GUID/);
});

test('an undeclared argument is refused, not ignored', () => {
  assert.throws(() => buildRequest(PATH, PARAMS, { companyId: 1, snapshotId: GUID, periodId: 3 }), /Unknown argument "periodId"/);
});

test('query values are range- and set-checked', () => {
  const base = { companyId: 1, snapshotId: GUID };
  assert.throws(() => buildRequest(PATH, PARAMS, { ...base, pageSize: 201 }), /pageSize/);
  assert.throws(() => buildRequest(PATH, PARAMS, { ...base, sortBy: 'evil' }), /sortBy/);
  assert.throws(() => buildRequest(PATH, PARAMS, { ...base, asOf: '2026-02-30' }), /asOf/);
  assert.throws(() => buildRequest(PATH, PARAMS, { ...base, search: 'x'.repeat(11) }), /search/);
  assert.throws(() => buildRequest(PATH, PARAMS, { ...base, uncategorised: 'yes' }), /true or false/);
});

test('the advertised schema requires path identifiers', () => {
  const schema = inputSchemaFor(PARAMS);
  assert.deepEqual(schema.required, ['companyId', 'snapshotId']);
  assert.equal((schema.properties['pageSize'] as { maximum: number }).maximum, 200);
});

const FIELDS: FieldSpec[] = [
  { kind: 'string', name: 'name', max: 5, required: true },
  { kind: 'money', name: 'amount' },
  { kind: 'decimal', name: 'rate', min: 0 },
  { kind: 'date', name: 'on', nullable: true },
  { kind: 'enum', name: 'type', values: ['Trade', 'Other'] },
  { kind: 'intIdList', name: 'ids', maxItems: 2 },
  { kind: 'objectList', name: 'moves', maxItems: 2, fields: [{ kind: 'money', name: 'amount', required: true }] },
  { kind: 'fileBytes', name: 'evidence', sha256Field: 'evidenceSha256' },
  { kind: 'sha256', name: 'evidenceSha256', required: true },
];

test('a body is built from declared fields and refuses anything else', () => {
  const body = buildBody(FIELDS, { name: 'Acme', amount: 10.5, rate: 0.125, on: null, type: 'Trade', ids: [1, 2], moves: [{ amount: 1 }], evidenceSha256: 'a'.repeat(64) });
  assert.deepEqual(body, { name: 'Acme', amount: 10.5, rate: 0.125, on: null, type: 'Trade', ids: [1, 2], moves: [{ amount: 1 }], evidenceSha256: 'a'.repeat(64) });
  assert.throws(() => buildBody(FIELDS, { name: 'A', evidenceSha256: 'a'.repeat(64), extra: 1 }), /Unknown field "extra"/);
  assert.throws(() => buildBody(FIELDS, { name: 'A', evidenceSha256: 'a'.repeat(64), moves: [{ amount: 1, x: 2 }] }), /Unknown field "moves\[0\]\.x"/);
});

test('body values are validated', () => {
  const ok = { name: 'A', evidenceSha256: 'a'.repeat(64) };
  assert.throws(() => buildBody(FIELDS, {}), /name is required/);
  assert.throws(() => buildBody(FIELDS, { ...ok, name: 'toolong' }), /5 characters/);
  assert.throws(() => buildBody(FIELDS, { ...ok, amount: 1.234 }), /two decimal places/);
  assert.throws(() => buildBody(FIELDS, { ...ok, amount: '10' }), /amount/);
  assert.throws(() => buildBody(FIELDS, { ...ok, rate: -1 }), /less than 0/);
  assert.throws(() => buildBody(FIELDS, { ...ok, on: '2026-13-01' }), /real date/);
  assert.throws(() => buildBody(FIELDS, { ...ok, type: 'Evil' }), /one of/);
  assert.throws(() => buildBody(FIELDS, { ...ok, ids: [1, 2, 3] }), /up to 2/);
  assert.throws(() => buildBody(FIELDS, { ...ok, name: null }), /cannot be null/);
  assert.throws(() => buildBody(FIELDS, { name: 'A', evidenceSha256: 'xyz' }), /SHA-256/);
});

test('an attached file is read under the upload root and its digest filled in', () => {
  const root = mkdtempSync(join(tmpdir(), 'fb-upload-'));
  writeFileSync(join(root, 'evidence.pdf'), 'PDF bytes');
  const body = buildBody(FIELDS, { name: 'A', evidence: 'evidence.pdf' }, { uploadRoot: root });
  assert.equal(body['evidence'], Buffer.from('PDF bytes').toString('base64'));
  assert.equal(body['evidenceSha256'], createHash('sha256').update('PDF bytes').digest('hex'));
  assert.throws(() => buildBody(FIELDS, { name: 'A', evidence: 'evidence.pdf' }), /--upload-root/);
  assert.throws(() => buildBody(FIELDS, { name: 'A', evidence: '../outside.pdf' }, { uploadRoot: root }), /No such file|outside/);
});

test('the body schema forbids additional properties at every level', () => {
  const schema = bodySchemaFor(FIELDS) as { additionalProperties: boolean; properties: Record<string, { items?: { additionalProperties: boolean } }> };
  assert.equal(schema.additionalProperties, false);
  assert.equal(schema.properties['moves']!.items!.additionalProperties, false);
});
