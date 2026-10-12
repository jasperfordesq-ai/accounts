/**
 * Path and query arguments, validated by hand.
 *
 * The connector has two runtime dependencies and that is a stated security
 * property: every dependency is code that runs on the person's machine with
 * their credential within reach. The arguments here are identifiers, dates,
 * small integers and enumerations, which is not enough surface to justify a
 * schema library.
 *
 * Deny by default: an argument the tool does not declare is refused rather
 * than ignored, because a model told a filter was applied when it was not
 * has no way to notice.
 */
export type ParamSpec =
  /** A FilingBridge integer identifier in the path. */
  | { kind: 'intId'; name: string; describe?: string }
  /** A GUID identifier in the path. */
  | { kind: 'guid'; name: string; describe?: string }
  /** A path segment drawn from a fixed set. */
  | { kind: 'pathEnum'; name: string; values: readonly string[]; describe: string }
  /** Optional page number. */
  | { kind: 'page' }
  /** Optional page size, with the route's own ceiling. */
  | { kind: 'pageSize'; max: number }
  /** An optional query value from a fixed set. */
  | { kind: 'enum'; name: string; values: readonly string[]; describe: string; required?: boolean }
  /** An optional true/false query value. */
  | { kind: 'flag'; name: string; describe: string }
  /** An optional integer identifier used as a filter. */
  | { kind: 'intFilter'; name: string; describe: string; required?: boolean }
  /** An optional calendar date in the query. */
  | { kind: 'dateQuery'; name: string; describe: string }
  /** A bounded whole number in the query. */
  | { kind: 'count'; name: string; min: number; max: number; describe: string }
  /** Free text in the query, bounded. */
  | { kind: 'text'; name: string; max: number; describe: string; required?: boolean };

const INT_ID = /^[1-9]\d{0,14}$/;
const GUID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;
const DATE = /^\d{4}-\d{2}-\d{2}$/;

export function paramName(spec: ParamSpec): string {
  switch (spec.kind) {
    case 'page':
      return 'page';
    case 'pageSize':
      return 'pageSize';
    default:
      return spec.name;
  }
}

export function isPathParam(spec: ParamSpec): boolean {
  return spec.kind === 'intId' || spec.kind === 'guid' || spec.kind === 'pathEnum';
}

const ID_DESCRIPTIONS: Record<string, string> = {
  companyId: 'The company, as company_list returns it.',
  periodId: 'The accounting period, as period_list returns it.',
  userId: 'The user, as user_list returns it.',
  bankAccountId: 'The bank account, as bank_account_list returns it.',
  categoryId: 'The account category, as category_list returns it.',
};

export function inputSchemaFor(params: readonly ParamSpec[]): {
  properties: Record<string, unknown>;
  required: string[];
} {
  const properties: Record<string, unknown> = {};
  const required: string[] = [];

  for (const spec of params) {
    const name = paramName(spec);
    switch (spec.kind) {
      case 'intId':
        properties[name] = {
          type: 'integer',
          minimum: 1,
          description: spec.describe ?? ID_DESCRIPTIONS[name] ?? 'Identifier as returned by the matching list tool.',
        };
        required.push(name);
        break;
      case 'guid':
        properties[name] = {
          type: 'string',
          pattern: GUID.source,
          description: spec.describe ?? 'Identifier (a GUID) as returned by the matching read tool.',
        };
        required.push(name);
        break;
      case 'pathEnum':
        properties[name] = { type: 'string', enum: [...spec.values], description: spec.describe };
        required.push(name);
        break;
      case 'page':
        properties[name] = { type: 'integer', minimum: 1, description: 'Page number, from 1.' };
        break;
      case 'pageSize':
        properties[name] = {
          type: 'integer',
          minimum: 1,
          maximum: spec.max,
          description: `Records per page, up to ${spec.max}.`,
        };
        break;
      case 'enum':
        properties[name] = { type: 'string', enum: [...spec.values], description: spec.describe };
        if (spec.required) required.push(name);
        break;
      case 'flag':
        properties[name] = { type: 'boolean', description: spec.describe };
        break;
      case 'count':
        properties[name] = { type: 'integer', minimum: spec.min, maximum: spec.max, description: spec.describe };
        break;
      case 'intFilter':
        properties[name] = { type: 'integer', minimum: 1, description: spec.describe };
        if (spec.required) required.push(name);
        break;
      case 'dateQuery':
        properties[name] = { type: 'string', pattern: DATE.source, description: `${spec.describe} As YYYY-MM-DD.` };
        break;
      case 'text':
        properties[name] = { type: 'string', minLength: 1, maxLength: spec.max, description: spec.describe };
        if (spec.required) required.push(name);
        break;
    }
  }
  return { properties, required };
}

function asIntId(name: string, value: unknown): string {
  const text = typeof value === 'number' ? String(value) : value;
  if (typeof text !== 'string' || !INT_ID.test(text)) {
    throw new Error(`${name} must be a positive whole-number identifier.`);
  }
  return text;
}

function validDate(value: string): boolean {
  if (!DATE.test(value)) return false;
  const parsed = new Date(`${value}T00:00:00Z`);
  return !Number.isNaN(parsed.getTime()) && parsed.toISOString().startsWith(value);
}

/**
 * Builds the request path and query from declared parameters, refusing any
 * argument the tool does not declare. Identifiers are checked against a
 * character set with no slash, dot, question mark or hash, so a value can
 * never open a new path segment or begin a query string.
 */
export function buildRequest(
  path: string,
  params: readonly ParamSpec[],
  args: Record<string, unknown>,
): { path: string; query: Record<string, string> } {
  const declared = new Set(params.map(paramName));
  for (const key of Object.keys(args)) {
    if (!declared.has(key)) {
      const accepted = [...declared].join(', ');
      throw new Error(`Unknown argument "${key}". This tool accepts: ${accepted.length > 0 ? accepted : 'no arguments'}.`);
    }
  }

  let out = path;
  const query: Record<string, string> = {};

  for (const spec of params) {
    const name = paramName(spec);
    const value = args[name];

    if (spec.kind === 'intId') {
      out = out.replace(`{${name}}`, asIntId(name, value));
      continue;
    }
    if (spec.kind === 'guid') {
      if (typeof value !== 'string' || !GUID.test(value)) throw new Error(`${name} must be a GUID.`);
      out = out.replace(`{${name}}`, value.toLowerCase());
      continue;
    }
    if (spec.kind === 'pathEnum') {
      if (typeof value !== 'string' || !spec.values.includes(value)) {
        throw new Error(`${name} must be one of: ${spec.values.join(', ')}.`);
      }
      out = out.replace(`{${name}}`, value);
      continue;
    }

    if (value === undefined) {
      if ('required' in spec && spec.required) throw new Error(`${name} is required.`);
      continue;
    }

    switch (spec.kind) {
      case 'page':
        if (typeof value !== 'number' || !Number.isInteger(value) || value < 1 || value > 1_000_000) {
          throw new Error('page must be a whole number from 1.');
        }
        query[name] = String(value);
        break;
      case 'pageSize':
        if (typeof value !== 'number' || !Number.isInteger(value) || value < 1 || value > spec.max) {
          throw new Error(`pageSize must be a whole number between 1 and ${spec.max}.`);
        }
        query[name] = String(value);
        break;
      case 'enum':
        if (typeof value !== 'string' || !spec.values.includes(value)) {
          throw new Error(`${name} must be one of: ${spec.values.join(', ')}.`);
        }
        query[name] = value;
        break;
      case 'flag':
        if (typeof value !== 'boolean') throw new Error(`${name} must be true or false.`);
        query[name] = value ? 'true' : 'false';
        break;
      case 'intFilter':
        query[name] = asIntId(name, value);
        break;
      case 'count':
        if (typeof value !== 'number' || !Number.isInteger(value) || value < spec.min || value > spec.max) {
          throw new Error(`${name} must be a whole number between ${spec.min} and ${spec.max}.`);
        }
        query[name] = String(value);
        break;
      case 'dateQuery':
        if (typeof value !== 'string' || !validDate(value)) throw new Error(`${name} must be a date as YYYY-MM-DD.`);
        query[name] = value;
        break;
      case 'text':
        if (typeof value !== 'string' || value.trim().length === 0) throw new Error(`${name} must be some text.`);
        if (value.length > spec.max) throw new Error(`${name} must be at most ${spec.max} characters.`);
        query[name] = value;
        break;
    }
  }

  if (/\{[A-Za-z]+\}/.test(out)) throw new Error(`Path parameter missing for ${path}.`);
  return { path: out, query };
}
