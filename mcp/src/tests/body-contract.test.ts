import { test } from 'node:test';
import assert from 'node:assert/strict';
import { BODY_SCHEMAS } from '../bodies.js';
import { ENUMS } from '../enums.js';
import { TOOLS } from '../catalogue.js';
import type { FieldSpec } from '../tool-body.js';
import { contract } from './helpers.js';

/** The write side of the gate: fields a Withheld session may not set. */
const WITHHELD_INPUTS: Record<string, readonly string[]> = {
  CompanyInput: ['registeredOfficeAddress1', 'registeredOfficeAddress2', 'registeredOfficeCity', 'registeredOfficeCounty', 'registeredOfficeEircode'],
  CompanyOfficerInput: ['address'],
  CreateUserInput: ['email', 'displayName'],
  InviteUserInput: ['email', 'displayName'],
  DirectorLoanInput: ['counterpartyName', 'loanTerms', 'reviewNote'],
  RelatedPartyTransactionInput: ['partyName', 'terms'],
  CroOfficerInput: ['address', 'identityType', 'identityEvidenceReference', 'identityEvidenceSha256', 'presenterNotificationEmail'],
  B1ShareholderHandoff: ['name', 'address'],
};

function enumOf(schemaName: string): string[] {
  const schema = contract().components.schemas[schemaName];
  assert.ok(schema?.enum, `${schemaName} is not an enum in the contract`);
  return (schema.enum as unknown[]).filter((value) => typeof value === 'string' && value !== '') as string[];
}

function refOf(property: Record<string, unknown>): string | null {
  const direct = property['$ref'];
  if (typeof direct === 'string') return direct.split('/').pop()!;
  const oneOf = property['oneOf'];
  if (Array.isArray(oneOf)) {
    const found = oneOf.find((entry) => entry && typeof entry === 'object' && typeof (entry as Record<string, unknown>)['$ref'] === 'string');
    if (found) return String((found as Record<string, unknown>)['$ref']).split('/').pop()!;
  }
  const items = property['items'];
  if (items && typeof items === 'object') return refOf(items as Record<string, unknown>);
  return null;
}

test('every body list declares exactly the fields its contract schema has', () => {
  const schemas = contract().components.schemas;
  for (const [name, fields] of Object.entries(BODY_SCHEMAS)) {
    const schema = schemas[name];
    assert.ok(schema?.properties, `${name} is not in the contract`);
    const declared = fields.map((field) => field.name).sort();
    assert.deepEqual(declared, Object.keys(schema.properties).sort(), `${name}: declared fields differ from the contract`);
  }
});

test('every enum field offers exactly the contract\'s values', () => {
  const schemas = contract().components.schemas;
  for (const [name, fields] of Object.entries(BODY_SCHEMAS)) {
    for (const field of fields) {
      if (field.kind !== 'enum') continue;
      const ref = refOf(schemas[name]!.properties![field.name]!);
      assert.ok(ref, `${name}.${field.name} has no enum reference in the contract`);
      assert.deepEqual([...field.values], enumOf(ref), `${name}.${field.name} values differ from ${ref}`);
    }
  }
});

test('ENUMS equals the contract enums (TaxType from Entities/Enums.cs)', () => {
  for (const [name, values] of Object.entries(ENUMS)) {
    if (name === 'TaxType') continue;
    assert.deepEqual([...values], enumOf(name), name);
  }
  assert.deepEqual([...ENUMS.TaxType], ['CorporationTax', 'Vat', 'Paye', 'Rct']);
});

test('the personal fields of each input are exactly the withheld ones', () => {
  for (const [name, fields] of Object.entries(BODY_SCHEMAS)) {
    const personal = fields.filter((field) => field.personal).map((field) => field.name).sort();
    assert.deepEqual(personal, [...(WITHHELD_INPUTS[name] ?? [])].sort(), `${name}: personal fields differ`);
  }
});

test('every tool body follows the contract schema of its route', () => {
  const paths = contract().paths;
  for (const tool of TOOLS) {
    const operation = paths[tool.path]?.[(tool.method ?? 'GET').toLowerCase()] as { requestBody?: { content?: Record<string, { schema?: Record<string, unknown> }> } } | undefined;
    const content = operation?.requestBody?.content;
    const schema = content ? Object.values(content)[0]?.schema : undefined;
    const contractRef = schema ? refOf(schema) : null;
    if (tool.body) {
      assert.ok(tool.bodySchema, `${tool.name} has a body but no bodySchema`);
      assert.equal(tool.body, BODY_SCHEMAS[tool.bodySchema], `${tool.name} body is not the ${tool.bodySchema} list`);
      if (operation) assert.equal(contractRef, tool.bodySchema, `${tool.name}: the contract body is ${contractRef}`);
    } else if (operation && contractRef) {
      assert.fail(`${tool.name}: the contract takes a ${contractRef} body the tool does not declare`);
    }
  }
});

test('nested object fields use the list for their own schema', () => {
  const schemas = contract().components.schemas;
  const walk = (name: string, fields: readonly FieldSpec[]) => {
    for (const field of fields) {
      if (field.kind !== 'object' && field.kind !== 'objectList') continue;
      const ref = refOf(schemas[name]!.properties![field.name]!);
      assert.ok(ref, `${name}.${field.name} is not a reference in the contract`);
      assert.equal(field.fields, BODY_SCHEMAS[ref], `${name}.${field.name} should use the ${ref} list`);
    }
  };
  for (const [name, fields] of Object.entries(BODY_SCHEMAS)) walk(name, fields);
});

test('fields the contract requires are required, apart from documented server-optional ones', () => {
  // The contract marks positional record parameters required even when the
  // C# type is nullable and the server accepts null; bodies.ts records those
  // as optional. Anything else the contract requires must be required here.
  const schemas = contract().components.schemas;
  const missing: string[] = [];
  for (const [name, fields] of Object.entries(BODY_SCHEMAS)) {
    for (const required of schemas[name]!.required ?? []) {
      const field = fields.find((f) => f.name === required)!;
      const property = schemas[name]!.properties![required]!;
      const nullableInContract = Array.isArray(property['type'])
        ? (property['type'] as unknown[]).includes('null')
        : Array.isArray(property['oneOf']) && (property['oneOf'] as Record<string, unknown>[]).some((entry) => entry['type'] === 'null');
      const autoDigest = fields.some((f) => f.kind === 'fileBytes' && f.sha256Field === required);
      if (!field.required && !nullableInContract && !autoDigest) missing.push(`${name}.${required}`);
    }
  }
  assert.deepEqual(missing, []);
});
