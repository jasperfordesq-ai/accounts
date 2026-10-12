import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { POLICIES, type ModelName } from '../field-policy.js';
import { API_ROOT, contract } from './helpers.js';

/**
 * Hard-coded on purpose. Iterating Object.keys(POLICIES) would take the list
 * of models to audit from the artifact being audited: drop a model from the
 * policy and the type together and every test would still pass. Adding a
 * gated model means adding it here too, and the first test says so.
 */
const MUST_BE_GATED = [
  'Company', 'CompanyOfficer', 'CompanyDashboardRow', 'QuarantinedCompanySummary', 'UserAdministrationSummary',
  'UserProvisioningResult', 'AuthResponse', 'DirectorLoan', 'DirectorLoanDetail', 'DirectorLoanComplianceResult',
  'RelatedPartyTransaction', 'AuditLog', 'CroOfficerInput', 'B1ShareholderHandoff',
] as const;

/** The product owner's list: each of these must be withheld, whatever else changes. */
const REQUIRED_WITHHELD: Record<string, readonly string[]> = {
  CompanyOfficer: ['address'],
  Company: [
    'registeredOfficeAddress1', 'registeredOfficeAddress2', 'registeredOfficeCity', 'registeredOfficeCounty',
    'registeredOfficeEircode', 'quarantinedByUserId', 'quarantinedByDisplayName', 'quarantineReason',
  ],
  UserAdministrationSummary: ['email', 'displayName'],
  AuthResponse: ['email', 'displayName'],
  DirectorLoan: ['counterpartyName', 'loanTerms', 'reviewNote', 'reviewedBy', 'reviewerRole'],
  DirectorLoanDetail: ['counterpartyName', 'mainConditions', 'reviewedBy', 'reviewerRole'],
  RelatedPartyTransaction: ['partyName', 'terms'],
  AuditLog: ['userId', 'actorDisplayName', 'oldValueJson', 'newValueJson', 'requestId'],
  CroOfficerInput: ['address', 'presenterNotificationEmail', 'identityType', 'identityEvidenceReference', 'identityEvidenceSha256'],
  B1ShareholderHandoff: ['name', 'address'],
  CompanyDashboardRow: ['assignedReviewerName', 'assignedReviewerEmail'],
  QuarantinedCompanySummary: ['quarantinedByDisplayName', 'reason'],
};

const camel = (name: string) => name.charAt(0).toLowerCase() + name.slice(1);

/** Public properties of a C# class, skipping [JsonIgnore] ones. */
export function entityFields(file: string, className: string): string[] {
  const source = readFileSync(resolve(API_ROOT, 'Entities', file), 'utf8');
  const start = source.search(new RegExp(`\\bclass\\s+${className}\\b`));
  assert.ok(start >= 0, `class ${className} not found in ${file}`);
  const open = source.indexOf('{', start);
  let depth = 0;
  let end = open;
  for (let i = open; i < source.length; i += 1) {
    if (source[i] === '{') depth += 1;
    if (source[i] === '}') {
      depth -= 1;
      if (depth === 0) { end = i; break; }
    }
  }
  const body = source.slice(open + 1, end);
  const fields: string[] = [];
  let ignoreNext = false;
  for (const raw of body.split('\n')) {
    const line = raw.trim();
    if (line.startsWith('[JsonIgnore')) {
      ignoreNext = true;
      if (!line.includes('public ')) continue;
    }
    const match = /public\s+(?:required\s+)?[\w<>?,\s[\].]+?\s+(\w+)\s*\{\s*get;/.exec(line);
    if (match) {
      if (!ignoreNext) fields.push(camel(match[1]!));
      ignoreNext = false;
    }
  }
  assert.ok(fields.length > 0, `parsed zero fields from ${className}: the parser is broken, not the entity`);
  return fields;
}

/** Positional parameters of a C# record declaration. */
export function recordFields(file: string, recordName: string): string[] {
  const source = readFileSync(resolve(API_ROOT, file), 'utf8');
  const match = new RegExp(`record\\s+${recordName}\\s*\\(([\\s\\S]*?)\\);`).exec(source);
  assert.ok(match, `record ${recordName} not found in ${file}`);
  const fields = match[1]!
    .split(',')
    .map((part) => part.replace(/\[[^\]]*\]/g, '').trim())
    .filter(Boolean)
    .map((part) => part.split(/\s+/).pop()!.replace(/=.*/, ''))
    .map(camel);
  assert.ok(fields.length > 0, `parsed zero fields from ${recordName}`);
  return fields;
}

function sourceFields(model: ModelName): string[] {
  const source = POLICIES[model].source;
  if ('openapi' in source) {
    const schema = contract().components.schemas[source.openapi];
    assert.ok(schema?.properties, `${source.openapi} is not in the OpenAPI components`);
    return Object.keys(schema.properties);
  }
  if ('entity' in source) return entityFields(source.entity, source.className);
  return recordFields(source.record, source.recordName);
}

test('no gated model has been quietly dropped from the policy', () => {
  assert.deepEqual(Object.keys(POLICIES).sort(), [...MUST_BE_GATED].sort());
});

test('the parsers really see the fields they guard', () => {
  assert.ok(entityFields('CompanyOfficer.cs', 'CompanyOfficer').includes('address'));
  assert.ok(!entityFields('CompanyOfficer.cs', 'CompanyOfficer').includes('company'), 'JsonIgnore navigations are not fields');
  assert.ok(entityFields('Company.cs', 'Company').includes('registeredOfficeEircode'));
  assert.ok(!entityFields('Company.cs', 'Company').includes('annualReturnDateHistory'));
  assert.ok(entityFields('DirectorLoan.cs', 'DirectorLoan').includes('counterpartyName'));
  assert.ok(recordFields('Services/DirectorLoanComplianceService.cs', 'DirectorLoanDetail').includes('mainConditions'));
  assert.ok(Object.keys(contract().components.schemas['UserAdministrationSummary']!.properties!).includes('email'));
});

test('every field of every gated model is classified safe, withheld or nested', () => {
  const unclassified: string[] = [];
  for (const model of MUST_BE_GATED) {
    const policy = POLICIES[model];
    const known = new Set([...policy.safe, ...policy.withheld, ...Object.keys(policy.nested ?? {})]);
    for (const field of sourceFields(model)) {
      if (!known.has(field)) unclassified.push(`${model}.${field}`);
    }
  }
  assert.deepEqual(unclassified, [], 'Classify new fields in mcp/src/field-policy.ts. Withheld is the safe default.');
});

test('no policy entry names a field the model does not have', () => {
  const phantom: string[] = [];
  for (const model of MUST_BE_GATED) {
    const policy = POLICIES[model];
    const real = new Set(sourceFields(model));
    for (const field of [...policy.safe, ...policy.withheld, ...Object.keys(policy.nested ?? {})]) {
      if (!real.has(field)) phantom.push(`${model}.${field}`);
    }
  }
  assert.deepEqual(phantom, []);
});

test('a field is never both safe and withheld', () => {
  for (const model of MUST_BE_GATED) {
    const withheld = new Set(POLICIES[model].withheld);
    assert.deepEqual(POLICIES[model].safe.filter((field) => withheld.has(field)), [], model);
  }
});

test('every field the product owner listed is withheld', () => {
  for (const [model, fields] of Object.entries(REQUIRED_WITHHELD)) {
    const policy = POLICIES[model as ModelName];
    assert.ok(policy, `${model} is not gated`);
    for (const field of fields) {
      assert.ok(policy.withheld.includes(field), `${model}.${field} must be withheld`);
      assert.ok(!policy.safe.includes(field), `${model}.${field} must not be safe`);
    }
  }
});
