import { ConnectorError } from './errors.js';
import type { ToolAnnotations } from './tools.js';

/**
 * Search hands out references, and fetch reads one.
 *
 * Both follow the shape Claude.ai's research mode expects: search returns
 * `{ results: [{ id, title, url }] }` and fetch takes one `id` and returns
 * `{ id, title, text, url, metadata }`. The id is a filingbridge:// URI that
 * names the kind of record and its identifier, plus the company and period
 * the record lives under.
 *
 * fetch is a router and nothing more. It resolves a reference to an ordinary
 * read tool and runs it through the server's normal dispatch, so the level,
 * the toolsets and the personal-data gate apply exactly as they would to that
 * tool, rather than a second time here where they could come to disagree.
 */
export const REFERENCE_SCHEME = 'filingbridge://';

export const SEARCH_KINDS = ['company', 'period', 'officer', 'category', 'note', 'transaction'] as const;
export type SearchKind = (typeof SEARCH_KINDS)[number];

export interface Reference {
  kind: SearchKind;
  id: number;
  companyId?: number;
  periodId?: number;
}

export interface ResolvedReference {
  /** The read tool that answers for this kind. */
  tool: string;
  args: Record<string, unknown>;
  /** For kinds with no single-record route: which item of the list is meant. */
  pick?: { id: number; pages?: { pageSize: number; maxPages: number } };
}

const POSITIVE = /^[1-9]\d{0,14}$/;

function refuse(message: string): never {
  throw new ConnectorError('REFERENCE_INVALID', message, { action: 'fix_arguments' });
}

export function formatReference(ref: Reference): string {
  const query: string[] = [];
  if (ref.companyId !== undefined && ref.kind !== 'company') query.push(`companyId=${ref.companyId}`);
  if (ref.periodId !== undefined && ref.kind !== 'period' && ref.kind !== 'company') query.push(`periodId=${ref.periodId}`);
  return `${REFERENCE_SCHEME}${ref.kind}/${ref.id}${query.length > 0 ? `?${query.join('&')}` : ''}`;
}

/** Parses a filingbridge:// reference, refusing anything not issued by search. */
export function parseReference(value: unknown): Reference {
  if (typeof value !== 'string' || value.trim().length === 0) {
    refuse('Give the id from a search result, such as filingbridge://company/12.');
  }
  const text = value.trim();
  if (!text.startsWith(REFERENCE_SCHEME)) {
    refuse(`A reference begins ${REFERENCE_SCHEME}. Search returns them; they are not written by hand.`);
  }
  const rest = text.slice(REFERENCE_SCHEME.length);
  const [pathPart = '', queryPart = ''] = rest.split('?', 2);
  const [kind = '', id = '', ...extra] = pathPart.split('/');
  if (extra.length > 0) refuse(`"${text}" is not a record reference. A reference is ${REFERENCE_SCHEME}<kind>/<id>.`);
  if (!(SEARCH_KINDS as readonly string[]).includes(kind)) {
    refuse(`"${kind}" is not a kind of record this connector reads. It reads: ${SEARCH_KINDS.join(', ')}.`);
  }
  if (!POSITIVE.test(id)) refuse('The identifier in that reference is not one FilingBridge issues.');

  const ref: Reference = { kind: kind as SearchKind, id: Number(id) };
  if (queryPart) {
    for (const pair of queryPart.split('&')) {
      const [key, raw = ''] = pair.split('=', 2);
      if ((key !== 'companyId' && key !== 'periodId') || !POSITIVE.test(raw)) {
        refuse(`"${pair}" is not part of a reference FilingBridge issues.`);
      }
      ref[key as 'companyId' | 'periodId'] = Number(raw);
    }
  }
  return ref;
}

function need(ref: Reference, key: 'companyId' | 'periodId'): number {
  const value = ref[key];
  if (value === undefined) refuse(`A ${ref.kind} reference must carry ${key}; take it from a search result.`);
  return value;
}

/** Turns a reference into the read tool call that answers it. */
export function resolveReference(value: unknown): ResolvedReference & { ref: Reference } {
  const ref = parseReference(value);
  switch (ref.kind) {
    case 'company':
      return { ref, tool: 'company_get', args: { id: ref.id } };
    case 'period':
      return { ref, tool: 'period_get', args: { companyId: need(ref, 'companyId'), id: ref.id } };
    case 'officer':
      return { ref, tool: 'officer_list', args: { companyId: need(ref, 'companyId') }, pick: { id: ref.id } };
    case 'category':
      return { ref, tool: 'category_list', args: { companyId: need(ref, 'companyId'), include: 'all' }, pick: { id: ref.id } };
    case 'note':
      return {
        ref,
        tool: 'note_list',
        args: { companyId: need(ref, 'companyId'), periodId: need(ref, 'periodId') },
        pick: { id: ref.id },
      };
    case 'transaction':
      return {
        ref,
        tool: 'transaction_list',
        args: { companyId: need(ref, 'companyId'), periodId: need(ref, 'periodId') },
        pick: { id: ref.id, pages: { pageSize: 200, maxPages: 25 } },
      };
  }
}

/** Where a person can look at the record in the web application. */
export function webUrlFor(baseUrl: string, ref: Reference): string {
  const base = baseUrl.replace(/\/+$/, '');
  if (ref.kind === 'company') return `${base}/companies/${ref.id}`;
  if (ref.kind === 'period' && ref.companyId !== undefined) return `${base}/companies/${ref.companyId}/periods/${ref.id}`;
  if (ref.periodId !== undefined && ref.companyId !== undefined) {
    return `${base}/companies/${ref.companyId}/periods/${ref.periodId}`;
  }
  if (ref.companyId !== undefined) return `${base}/companies/${ref.companyId}`;
  return base;
}

/** Finds the record a list tool's payload holds under `id`, in an array or a paged envelope. */
export function pickRecord(payload: unknown, id: number): unknown {
  const list = Array.isArray(payload)
    ? payload
    : payload && typeof payload === 'object' && Array.isArray((payload as Record<string, unknown>)['items'])
      ? ((payload as Record<string, unknown>)['items'] as unknown[])
      : [];
  return list.find((item) => item && typeof item === 'object' && (item as Record<string, unknown>)['id'] === id);
}

const READ_ONLY: ToolAnnotations = {
  title: '',
  readOnlyHint: true,
  destructiveHint: false,
  idempotentHint: true,
  openWorldHint: false,
};

export const SEARCH_TOOL = {
  name: 'search',
  description:
    'Find companies, accounting periods, officers, account categories, notes and bank transactions by text, '
    + 'within the companies this account may access. Returns references (filingbridge://...) for the fetch tool. '
    + 'Categories are searched only within a company (pass companyId); notes and transactions only within a '
    + 'period (pass companyId and periodId).',
  inputSchema: {
    type: 'object',
    properties: {
      query: { type: 'string', minLength: 1, maxLength: 200, description: 'What to look for.' },
      types: {
        type: 'array',
        items: { type: 'string', enum: [...SEARCH_KINDS] },
        maxItems: SEARCH_KINDS.length,
        description: 'Which kinds of record to look in. Omit for all of them.',
      },
      companyId: { type: 'integer', minimum: 1, description: 'Only within this company.' },
      periodId: { type: 'integer', minimum: 1, description: 'Only within this period (needs companyId).' },
      limit: { type: 'integer', minimum: 1, maximum: 50, description: 'Most results to return; 20 by default.' },
    },
    required: ['query'],
    additionalProperties: false,
  },
  annotations: { ...READ_ONLY, title: 'Search' },
};

export const FETCH_TOOL = {
  name: 'fetch',
  description:
    'Read the record behind a reference returned by search, such as filingbridge://period/34?companyId=12. '
    + 'The record comes back exactly as the read tool for its kind returns it, with the same fields withheld.',
  inputSchema: {
    type: 'object',
    properties: {
      id: { type: 'string', maxLength: 300, description: 'A reference as search returned it, beginning filingbridge://' },
    },
    required: ['id'],
    additionalProperties: false,
  },
  annotations: { ...READ_ONLY, title: 'Fetch a record' },
};

export interface SearchArgs {
  query: string;
  types?: SearchKind[];
  companyId?: number;
  periodId?: number;
  limit?: number;
}

export function parseSearchArgs(args: Record<string, unknown>): SearchArgs {
  const allowed = new Set(['query', 'types', 'companyId', 'periodId', 'limit']);
  for (const key of Object.keys(args)) {
    if (!allowed.has(key)) throw new Error(`Unknown argument "${key}". search accepts: ${[...allowed].join(', ')}.`);
  }
  const query = args['query'];
  if (typeof query !== 'string' || query.trim().length === 0 || query.length > 200) {
    throw new Error('query must be some text to look for, up to 200 characters.');
  }
  const out: SearchArgs = { query: query.trim() };
  if (args['types'] !== undefined) {
    const types = args['types'];
    if (!Array.isArray(types) || !types.every((t) => (SEARCH_KINDS as readonly unknown[]).includes(t))) {
      throw new Error(`types may only contain: ${SEARCH_KINDS.join(', ')}.`);
    }
    out.types = [...new Set(types as SearchKind[])];
  }
  for (const key of ['companyId', 'periodId'] as const) {
    const value = args[key];
    if (value === undefined) continue;
    if (typeof value !== 'number' || !Number.isInteger(value) || value < 1) throw new Error(`${key} must be a positive identifier.`);
    out[key] = value;
  }
  if (out.periodId !== undefined && out.companyId === undefined) throw new Error('periodId needs companyId.');
  if (args['limit'] !== undefined) {
    const limit = args['limit'];
    if (typeof limit !== 'number' || !Number.isInteger(limit) || limit < 1 || limit > 50) throw new Error('limit must be 1 to 50.');
    out.limit = limit;
  }
  return out;
}

/** Turns FilingBridge's search hits into Claude.ai-shaped results. */
export function shapeSearchResults(body: unknown, baseUrl: string): { results: { id: string; title: string; url: string }[] } {
  const raw = body && typeof body === 'object' ? (body as Record<string, unknown>)['results'] : undefined;
  const hits = Array.isArray(raw) ? raw : [];
  const results: { id: string; title: string; url: string }[] = [];
  for (const hit of hits) {
    if (!hit || typeof hit !== 'object') continue;
    const record = hit as Record<string, unknown>;
    const kind = record['type'];
    const id = typeof record['id'] === 'number' ? record['id'] : Number(record['id']);
    if (!(SEARCH_KINDS as readonly unknown[]).includes(kind) || !Number.isInteger(id) || id < 1) continue;
    const ref: Reference = { kind: kind as SearchKind, id };
    const companyId = Number(record['companyId']);
    const periodId = Number(record['periodId']);
    if (Number.isInteger(companyId) && companyId > 0) ref.companyId = companyId;
    if (Number.isInteger(periodId) && periodId > 0) ref.periodId = periodId;
    // A company hit is its own company.
    if (ref.kind === 'company') ref.companyId = id;
    results.push({
      id: formatReference(ref),
      title: typeof record['title'] === 'string' ? record['title'].slice(0, 300) : `${ref.kind} ${id}`,
      url: webUrlFor(baseUrl, ref),
    });
  }
  return { results };
}
