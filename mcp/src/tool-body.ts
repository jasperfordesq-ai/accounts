import { readInsideRoot, EVIDENCE_MAX_BYTES, FileAccessError } from './files.js';

/**
 * Validation for request bodies, hand-written for the same reason argument
 * validation is.
 *
 * Deny by default in both directions. A field the tool does not declare is
 * refused rather than forwarded, so a model cannot reach a column the
 * connector never meant to expose by guessing its name. A field the tool
 * declares and the caller omits is left out of the body.
 */
interface Common<N extends string> {
  name: N;
  required?: boolean;
  /** The C# property is nullable, so null is a meaningful value (clears it). */
  nullable?: boolean;
  describe?: string;
  /**
   * Says something about the request rather than about the company or anyone
   * in it (a concurrency stamp, a typed confirmation). Never personal data.
   */
  control?: true;
  /**
   * Personal data the gate withholds on the way out (an officer's address, a
   * director-loan counterparty). A session with data scope Withheld may not
   * write it either: the connector refuses before anything is sent.
   */
  personal?: true;
}

export type FieldSpec<N extends string = string> = Common<N> & (
  | { kind: 'string'; max: number; min?: number; pattern?: string }
  | { kind: 'date' }
  | { kind: 'dateTime' }
  | { kind: 'integer'; min: number; max: number }
  /** Euro amounts: decimal(18,2). */
  | { kind: 'money'; min?: number }
  /** A rate or quantity with up to six decimal places. */
  | { kind: 'decimal'; min?: number; max?: number }
  | { kind: 'boolean' }
  | { kind: 'enum'; values: readonly string[] }
  | { kind: 'intId' }
  | { kind: 'intIdList'; maxItems: number }
  | { kind: 'stringList'; maxItems: number; maxLength: number }
  | { kind: 'sha256' }
  | { kind: 'uuid' }
  /** A bounded nested object with its own declared fields. */
  | { kind: 'object'; fields: readonly FieldSpec[] }
  /** A bounded list of nested objects. */
  | { kind: 'objectList'; fields: readonly FieldSpec[]; maxItems: number }
  /**
   * A file the person put under --upload-root, sent base64-encoded. The
   * caller names the file; the connector reads it. When `sha256Field` is set
   * and the caller left that field out, the connector fills in the file's
   * SHA-256, which is what FilingBridge checks the bytes against.
   */
  | { kind: 'fileBytes'; sha256Field?: string; maxBytes?: number }
);

export interface BodyContext {
  uploadRoot?: string | undefined;
}

const DATE = /^\d{4}-\d{2}-\d{2}$/;
const DATE_TIME = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(?::\d{2}(?:\.\d{1,7})?)?(?:Z|[+-]\d{2}:\d{2})$/;
const SHA256 = /^[0-9a-fA-F]{64}$/;
const UUID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;
const MAX_MONEY = 1_000_000_000_000;

function validDate(value: string): boolean {
  if (!DATE.test(value)) return false;
  const parsed = new Date(`${value}T00:00:00Z`);
  return !Number.isNaN(parsed.getTime()) && parsed.toISOString().startsWith(value);
}

function decimalPlaces(value: number): number {
  if (Number.isInteger(value)) return 0;
  const text = value.toString();
  if (text.includes('e-')) return Number(text.split('e-')[1]);
  return text.split('.')[1]?.length ?? 0;
}

function withNull(schema: Record<string, unknown>, nullable: boolean | undefined): Record<string, unknown> {
  if (!nullable) return schema;
  const type = schema['type'];
  if (typeof type === 'string') return { ...schema, type: [type, 'null'] };
  return schema;
}

export function fieldSchema(field: FieldSpec): Record<string, unknown> {
  const describe = field.describe ? { description: field.describe } : {};
  let schema: Record<string, unknown>;
  switch (field.kind) {
    case 'string':
      schema = {
        type: 'string',
        maxLength: field.max,
        ...(field.min === undefined ? {} : { minLength: field.min }),
        ...(field.pattern === undefined ? {} : { pattern: field.pattern }),
        ...describe,
      };
      break;
    case 'date':
      schema = { type: 'string', pattern: DATE.source, description: field.describe ?? 'A date, as YYYY-MM-DD.' };
      break;
    case 'dateTime':
      schema = {
        type: 'string',
        pattern: DATE_TIME.source,
        description: field.describe ?? 'An instant with its offset, such as 2026-03-31T14:00:00Z.',
      };
      break;
    case 'integer':
      schema = { type: 'integer', minimum: field.min, maximum: field.max, ...describe };
      break;
    case 'money':
      schema = {
        type: 'number',
        ...(field.min === undefined ? {} : { minimum: field.min }),
        maximum: MAX_MONEY,
        description: `${field.describe ?? 'An amount'} In euro, to at most two decimal places.`,
      };
      break;
    case 'decimal':
      schema = {
        type: 'number',
        ...(field.min === undefined ? {} : { minimum: field.min }),
        ...(field.max === undefined ? {} : { maximum: field.max }),
        ...describe,
      };
      break;
    case 'boolean':
      schema = { type: 'boolean', ...describe };
      break;
    case 'enum':
      schema = { type: 'string', enum: [...field.values], ...describe };
      break;
    case 'intId':
      schema = { type: 'integer', minimum: 1, description: field.describe ?? 'Identifier as returned by the matching list tool.' };
      break;
    case 'intIdList':
      schema = {
        type: 'array',
        maxItems: field.maxItems,
        items: { type: 'integer', minimum: 1 },
        ...describe,
      };
      break;
    case 'stringList':
      schema = {
        type: 'array',
        maxItems: field.maxItems,
        items: { type: 'string', maxLength: field.maxLength },
        ...describe,
      };
      break;
    case 'sha256':
      schema = { type: 'string', pattern: SHA256.source, description: field.describe ?? 'A SHA-256 digest, 64 hexadecimal characters.' };
      break;
    case 'uuid':
      schema = { type: 'string', pattern: UUID.source, description: field.describe ?? 'A GUID.' };
      break;
    case 'object': {
      const inner = bodySchemaFor(field.fields);
      schema = { ...inner, ...describe };
      break;
    }
    case 'objectList': {
      schema = {
        type: 'array',
        maxItems: field.maxItems,
        items: bodySchemaFor(field.fields),
        ...describe,
      };
      break;
    }
    case 'fileBytes':
      schema = {
        type: 'string',
        maxLength: 1024,
        description:
          `${field.describe ?? 'A file to attach.'} Give the file's path relative to the connector's `
          + '--upload-root directory; the connector reads it and sends its contents. '
          + `Up to ${(field.maxBytes ?? EVIDENCE_MAX_BYTES) / 1024 / 1024} MB.`
          + (field.sha256Field ? ` If ${field.sha256Field} is left out, the connector fills in the file's SHA-256.` : ''),
      };
      break;
  }
  return withNull(schema, field.nullable);
}

export function bodySchemaFor(fields: readonly FieldSpec[]): Record<string, unknown> {
  const properties: Record<string, unknown> = {};
  const required: string[] = [];
  for (const field of fields) {
    properties[field.name] = fieldSchema(field);
    if (field.required) required.push(field.name);
  }
  const schema: Record<string, unknown> = { type: 'object', properties, additionalProperties: false };
  if (required.length > 0) schema['required'] = required;
  return schema;
}

function refuse(name: string, expectation: string): never {
  throw new Error(`${name} ${expectation}`);
}

function convertValue(field: FieldSpec, path: string, value: unknown, context: BodyContext): unknown {
  if (value === null) {
    if (field.nullable && !field.required) return null;
    refuse(path, 'cannot be null.');
  }
  switch (field.kind) {
    case 'string':
      if (typeof value !== 'string') refuse(path, 'must be text.');
      if (value.length > field.max) refuse(path, `must be ${field.max} characters or fewer.`);
      if (field.min !== undefined && value.trim().length < field.min) {
        refuse(path, `must be at least ${field.min} characters.`);
      }
      if (field.pattern !== undefined && !new RegExp(field.pattern).test(value)) {
        refuse(path, 'is not in the expected format.');
      }
      return value;
    case 'date':
      if (typeof value !== 'string' || !validDate(value)) refuse(path, 'must be a real date as YYYY-MM-DD.');
      return value;
    case 'dateTime':
      if (typeof value !== 'string' || !DATE_TIME.test(value) || Number.isNaN(Date.parse(value))) {
        refuse(path, 'must be an instant with an offset, such as 2026-03-31T14:00:00Z.');
      }
      return value;
    case 'integer':
      if (typeof value !== 'number' || !Number.isInteger(value) || value < field.min || value > field.max) {
        refuse(path, `must be a whole number between ${field.min} and ${field.max}.`);
      }
      return value;
    case 'money':
      if (typeof value !== 'number' || !Number.isFinite(value) || Math.abs(value) > MAX_MONEY) {
        refuse(path, 'must be an amount in euro.');
      }
      if (decimalPlaces(value) > 2) refuse(path, 'must have at most two decimal places.');
      if (field.min !== undefined && value < field.min) refuse(path, `cannot be less than ${field.min}.`);
      return value;
    case 'decimal':
      if (typeof value !== 'number' || !Number.isFinite(value)) refuse(path, 'must be a number.');
      if (decimalPlaces(value) > 6) refuse(path, 'must have at most six decimal places.');
      if (field.min !== undefined && value < field.min) refuse(path, `cannot be less than ${field.min}.`);
      if (field.max !== undefined && value > field.max) refuse(path, `cannot be more than ${field.max}.`);
      return value;
    case 'boolean':
      if (typeof value !== 'boolean') refuse(path, 'must be true or false.');
      return value;
    case 'enum':
      if (typeof value !== 'string' || !field.values.includes(value)) {
        refuse(path, `must be one of: ${field.values.join(', ')}.`);
      }
      return value;
    case 'intId':
      if (typeof value !== 'number' || !Number.isInteger(value) || value < 1 || value > 2_147_483_647) {
        refuse(path, 'must be a positive whole-number identifier.');
      }
      return value;
    case 'intIdList':
      if (!Array.isArray(value) || value.length > field.maxItems
        || !value.every((item) => typeof item === 'number' && Number.isInteger(item) && item >= 1 && item <= 2_147_483_647)) {
        refuse(path, `must be a list of up to ${field.maxItems} positive whole-number identifiers.`);
      }
      return value;
    case 'stringList':
      if (!Array.isArray(value) || value.length > field.maxItems
        || !value.every((item) => typeof item === 'string' && item.length <= field.maxLength)) {
        refuse(path, `must be a list of up to ${field.maxItems} texts of at most ${field.maxLength} characters.`);
      }
      return value;
    case 'sha256':
      if (typeof value !== 'string' || !SHA256.test(value)) refuse(path, 'must be a SHA-256 digest (64 hexadecimal characters).');
      return value.toLowerCase();
    case 'uuid':
      if (typeof value !== 'string' || !UUID.test(value)) refuse(path, 'must be a GUID.');
      return value.toLowerCase();
    case 'object':
      if (value === null || typeof value !== 'object' || Array.isArray(value)) refuse(path, 'must be an object.');
      return buildBody(field.fields, value as Record<string, unknown>, context, `${path}.`);
    case 'objectList':
      if (!Array.isArray(value) || value.length > field.maxItems) refuse(path, `must be a list of up to ${field.maxItems} entries.`);
      return value.map((item, index) => {
        if (item === null || typeof item !== 'object' || Array.isArray(item)) refuse(`${path}[${index}]`, 'must be an object.');
        return buildBody(field.fields, item as Record<string, unknown>, context, `${path}[${index}].`);
      });
    case 'fileBytes':
      // Handled in buildBody, which can also fill the digest beside it.
      return value;
  }
}

/**
 * Turns a tool call's body arguments into the JSON body, refusing anything
 * undeclared or malformed.
 */
export function buildBody(
  fields: readonly FieldSpec[],
  args: Record<string, unknown>,
  context: BodyContext = {},
  prefix = '',
): Record<string, unknown> {
  const declared = new Map(fields.map((field) => [field.name, field]));
  for (const key of Object.keys(args)) {
    if (!declared.has(key)) {
      const accepted = [...declared.keys()].join(', ');
      throw new Error(`Unknown field "${prefix}${key}". This accepts: ${accepted.length > 0 ? accepted : 'no fields'}.`);
    }
  }

  // A digest the connector will compute from an attached file is not missing.
  const filled = new Set<string>();
  for (const field of fields) {
    if (field.kind === 'fileBytes' && field.sha256Field && typeof args[field.name] === 'string') {
      filled.add(field.sha256Field);
    }
  }

  const body: Record<string, unknown> = {};
  for (const field of fields) {
    const value = args[field.name];
    const path = `${prefix}${field.name}`;
    if (value === undefined) {
      if (field.required && !filled.has(field.name)) refuse(path, 'is required.');
      continue;
    }
    if (field.kind === 'fileBytes') {
      if (value === null) {
        if (field.nullable && !field.required) {
          body[field.name] = null;
          continue;
        }
        refuse(path, 'cannot be null.');
      }
      if (typeof value !== 'string') refuse(path, 'must be a file path under the upload directory.');
      if (!context.uploadRoot) {
        throw new FileAccessError(
          `${path} attaches a file from this machine, which needs the connector to be started with `
            + '--upload-root naming the directory the file is in. Nothing was sent.',
        );
      }
      const file = readInsideRoot(context.uploadRoot, value, { maxBytes: field.maxBytes ?? EVIDENCE_MAX_BYTES });
      body[field.name] = file.bytes.toString('base64');
      if (field.sha256Field && args[field.sha256Field] === undefined) {
        body[field.sha256Field] = file.sha256;
      }
      continue;
    }
    body[field.name] = convertValue(field, path, value, context);
  }
  return body;
}

/** Every field name in a spec list, nested ones included, with their path. */
export function flattenFields(fields: readonly FieldSpec[], prefix = ''): { path: string; field: FieldSpec }[] {
  const out: { path: string; field: FieldSpec }[] = [];
  for (const field of fields) {
    const path = `${prefix}${field.name}`;
    out.push({ path, field });
    if (field.kind === 'object' || field.kind === 'objectList') {
      out.push(...flattenFields(field.fields, `${path}.`));
    }
  }
  return out;
}
