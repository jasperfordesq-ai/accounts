import type { ApiClient } from './client.js';
import { applyFieldPolicy, type ModelName } from './field-policy.js';
import { buildRequest, inputSchemaFor, isPathParam, paramName, type ParamSpec } from './tool-input.js';
import { bodySchemaFor, buildBody, flattenFields, type FieldSpec } from './tool-body.js';
import { ConnectorError } from './errors.js';
import { FileAccessError, readInsideRoot, writeDownload, CSV_EXTENSIONS, CSV_MAX_BYTES } from './files.js';
import type { ToolGroup } from './toolsets.js';
import type { AccessLevel } from './config.js';
import { honoursIdempotencyKey } from './idempotency.js';

export type BinaryKind = 'pdf' | 'xhtml' | 'csv' | 'zip' | 'json' | 'bin';

export interface ToolDefinition {
  name: string;
  group: ToolGroup;
  description: string;
  /** Absent means a read (GET). */
  method?: 'POST' | 'PUT' | 'DELETE';
  /** The route template exactly as the OpenAPI contract spells it. */
  path: string;
  /**
   * Query parameters, and path parameters that are not plain integer
   * identifiers. Every other {name} in the path is an integer identifier.
   */
  params?: readonly ParamSpec[];
  /** Body fields this tool accepts. Anything else is refused, not forwarded. */
  body?: readonly FieldSpec[];
  /** The OpenAPI component schema the body follows, for the contract tests. */
  bodySchema?: string;
  /** The route accepts no body at all when none of the body fields is given. */
  optionalBody?: boolean;
  /** The least session access level that may call this. Absent means read. */
  level?: 'write' | 'admin';
  /**
   * FilingBridge asks a person to approve this exact request (password and
   * authenticator) before it happens: every removal and every irreversible
   * or privileged transition.
   */
  approval?: boolean;
  /** Every record in the payload is this model, and the gate applies to it. */
  model?: ModelName;
  /** Why this payload needs no gate: it carries no records about people. */
  noRecordsBecause?: string;
  /**
   * The payload cannot be filtered (a file, or prose that interpolates
   * withheld fields), or the write would erase fields this session cannot
   * see. Only a Full data-scope session may use the tool; the sentence says why.
   */
  requiresFullScope?: string;
  /** The response is a file, written to --download-dir rather than returned. */
  binary?: { kind: BinaryKind; defaultName: string };
  /** The result carries a credential meant for the person (handed over, never logged). */
  returnsCredential?: boolean;
  /** The request is a multipart CSV upload read from --upload-root. */
  upload?: 'csv';
}

export interface ToolContext {
  client: ApiClient;
  allowPersonalData: boolean;
  downloadDir?: string | undefined;
  uploadRoot?: string | undefined;
}

export interface ToolAnnotations {
  title: string;
  readOnlyHint: boolean;
  destructiveHint: boolean;
  idempotentHint: boolean;
  openWorldHint: boolean;
}

const PATH_PARAM = /\{([A-Za-z]+)\}/g;

/** Every parameter a tool takes in its URL, the implicit integer ones included. */
export function allParams(tool: ToolDefinition): readonly ParamSpec[] {
  const declared = new Map((tool.params ?? []).map((spec) => [paramName(spec), spec]));
  const out: ParamSpec[] = [];
  for (const match of tool.path.matchAll(PATH_PARAM)) {
    const name = match[1]!;
    out.push(declared.get(name) ?? { kind: 'intId', name });
  }
  for (const spec of tool.params ?? []) {
    if (!isPathParam(spec)) out.push(spec);
  }
  return out;
}

export function levelOf(tool: ToolDefinition): AccessLevel {
  return tool.level ?? 'read';
}

export function titleFor(name: string): string {
  const [first = '', ...rest] = name.split('_');
  return [first.charAt(0).toUpperCase() + first.slice(1), ...rest].join(' ');
}

/**
 * The hints the protocol lets a tool carry, derived from the definition so
 * they cannot disagree with it. A download writes a file onto this machine,
 * so it is not read-only in the protocol's sense, though it changes nothing
 * in FilingBridge.
 */
export function annotationsFor(tool: ToolDefinition): ToolAnnotations {
  const read = tool.method === undefined;
  return {
    title: titleFor(tool.name),
    readOnlyHint: read && tool.binary === undefined,
    destructiveHint: tool.approval === true,
    idempotentHint: read || tool.method === 'PUT' || tool.method === 'DELETE',
    openWorldHint: false,
  };
}

const UPLOAD_FILE_SCHEMA = {
  type: 'string',
  maxLength: 1024,
  description:
    'The CSV bank statement, as a path relative to the connector\'s --upload-root directory. '
    + 'Only .csv and .txt files of up to 5 MB.',
};

/**
 * What a client is told it may send, generated from the same declarations
 * the validator enforces. A change advertises `reason`; a change FilingBridge
 * asks a person to approve also advertises `approvalId`, because a caller has
 * no other way to learn that the identifier handed back in the refusal is
 * meant to come back here.
 */
export function toolInputSchema(tool: ToolDefinition): Record<string, unknown> {
  const fromParams = inputSchemaFor(allParams(tool));
  const fromBody = tool.body ? bodySchemaFor(tool.body) : { properties: {} };
  const properties: Record<string, unknown> = {
    ...fromParams.properties,
    ...(fromBody['properties'] as Record<string, unknown>),
  };
  const bodyRequired = (fromBody['required'] as string[] | undefined) ?? [];
  // A route that also accepts no body at all takes either nothing or a
  // complete body. A schema cannot say "all or none" in a way every client
  // honours, so none is required here, each such field says so, and runTool
  // refuses a partial body.
  const required = [...fromParams.required, ...(tool.optionalBody ? [] : bodyRequired)];
  if (tool.optionalBody) {
    for (const name of bodyRequired) {
      const property = properties[name] as Record<string, unknown>;
      properties[name] = {
        ...property,
        description: `${String(property['description'] ?? '').trim()} ${optionalBodyNote(bodyRequired)}`.trim(),
      };
    }
  }

  if (tool.upload === 'csv') {
    properties['file'] = UPLOAD_FILE_SCHEMA;
    required.push('file');
  }

  if (tool.method) {
    const bodyReason = tool.body?.find((field) => field.name === 'reason');
    properties['reason'] = {
      type: 'string',
      maxLength: bodyReason && bodyReason.kind === 'string' ? Math.min(bodyReason.max, 2000) : 500,
      description: bodyReason?.describe
        ? `${bodyReason.describe} Also recorded against the change in FilingBridge's activity log.`
        : tool.approval
          ? 'Why this is being done. Required, and recorded against the action.'
          : 'Why the change is being made. Recorded against the change.',
    };
    if (tool.approval && !required.includes('reason')) required.push('reason');
    if (tool.approval) {
      properties['approvalId'] = {
        type: 'string',
        pattern: '^[A-Za-z0-9-]{1,64}$',
        description:
          'The approval identifier FilingBridge returned when it last refused this exact call. Include '
          + 'it only after the person has approved it (filingbridge-mcp approve, or the web approvals page).',
      };
    }
  }

  const schema: Record<string, unknown> = { type: 'object', properties, additionalProperties: false };
  if (required.length > 0) schema['required'] = [...new Set(required)];
  return schema;
}

/** What an all-or-nothing body field tells the caller. */
export function optionalBodyNote(required: readonly string[]): string {
  return required.length > 1
    ? `Give all of ${required.join(', ')}, or none of them to use what is already saved.`
    : `Give it, or leave it out to use what is already saved.`;
}

/** Body fields the gate withholds, wherever they sit in the body. */
export function personalFieldPaths(tool: ToolDefinition): readonly string[] {
  if (!tool.body) return [];
  return flattenFields(tool.body).filter((entry) => entry.field.personal).map((entry) => entry.path);
}

/** The personal fields a particular call actually supplies, nested ones included. */
export function personalFieldsInCall(fields: readonly FieldSpec[], args: Record<string, unknown>, prefix = ''): string[] {
  const found: string[] = [];
  for (const field of fields) {
    const value = args[field.name];
    if (value === undefined) continue;
    const path = `${prefix}${field.name}`;
    if (field.personal && value !== null && value !== '') found.push(path);
    if (field.kind === 'object' && value && typeof value === 'object' && !Array.isArray(value)) {
      found.push(...personalFieldsInCall(field.fields, value as Record<string, unknown>, `${path}.`));
    }
    if (field.kind === 'objectList' && Array.isArray(value)) {
      value.forEach((item, index) => {
        if (item && typeof item === 'object' && !Array.isArray(item)) {
          found.push(...personalFieldsInCall(field.fields, item as Record<string, unknown>, `${path}[${index}].`));
        }
      });
    }
  }
  return found;
}

export function applyPolicy(tool: ToolDefinition, raw: unknown, allowPersonalData: boolean): unknown {
  if (tool.model) return applyFieldPolicy(tool.model, raw, allowPersonalData);
  return raw;
}

function partition(tool: ToolDefinition, args: Record<string, unknown>) {
  const urlNames = new Set(allParams(tool).map(paramName));
  const urlArgs: Record<string, unknown> = {};
  const bodyArgs: Record<string, unknown> = {};
  let reason: string | undefined;
  let approvalId: string | undefined;
  let file: unknown;

  for (const [key, value] of Object.entries(args)) {
    if (key === 'reason' && tool.method) {
      if (typeof value !== 'string' || value.trim().length === 0) {
        throw new Error('reason must say why the change is being made.');
      }
      reason = value.trim();
      continue;
    }
    if (key === 'approvalId' && tool.approval) {
      if (typeof value !== 'string' || !/^[A-Za-z0-9-]{1,64}$/.test(value)) {
        throw new Error('approvalId must be the identifier FilingBridge returned, exactly as given.');
      }
      approvalId = value;
      continue;
    }
    if (key === 'file' && tool.upload) {
      file = value;
      continue;
    }
    if (urlNames.has(key)) {
      urlArgs[key] = value;
      continue;
    }
    bodyArgs[key] = value;
  }
  return { urlArgs, bodyArgs, reason, approvalId, file };
}

const CREDENTIAL_NOTE =
  'This result contains a credential for the person, not for you. Show it to the person now so they '
  + 'can pass it to the user over a channel they trust, then do not repeat, store or reuse it.';

export async function runTool(
  tool: ToolDefinition,
  context: ToolContext,
  args: Record<string, unknown> = {},
): Promise<unknown> {
  const { urlArgs, bodyArgs, reason, approvalId, file } = partition(tool, args);
  const request = buildRequest(tool.path, allParams(tool), urlArgs);
  // Only a route that replays a repeated Idempotency-Key may be asked twice.
  const replaySafe = tool.method !== undefined && honoursIdempotencyKey(tool.method, tool.path);

  if (tool.binary) {
    if (Object.keys(bodyArgs).length > 0) {
      throw new Error(`Unknown argument "${Object.keys(bodyArgs)[0]}". This tool accepts: ${allParams(tool).map(paramName).join(', ') || 'no arguments'}.`);
    }
    if (!context.downloadDir) {
      throw new ConnectorError('TOOL_DISABLED', downloadsOff(tool));
    }
    if (tool.approval && !reason) {
      throw new ConnectorError('REASON_REQUIRED', `${tool.name} needs a person's approval. Pass a reason saying why.`, { action: 'fix_arguments' });
    }
    const downloaded = await context.client.download(
      request.path,
      { query: request.query, reason, approvalId, replaySafe },
      tool.method ?? 'GET',
    );
    const written = writeDownload(
      context.downloadDir,
      downloaded.fileName ?? tool.binary.defaultName,
      downloaded.bytes,
      downloaded.contentType,
    );
    return {
      saved: written,
      note: 'The file is on disk at the path above. Its contents are deliberately not returned here.',
    };
  }

  if (!tool.method) {
    if (Object.keys(bodyArgs).length > 0) {
      const accepted = allParams(tool).map(paramName);
      throw new Error(
        `Unknown argument "${Object.keys(bodyArgs)[0]}". This tool accepts: ${accepted.length > 0 ? accepted.join(', ') : 'no arguments'}.`,
      );
    }
    const raw = await context.client.get<unknown>(request.path, { query: request.query });
    return applyPolicy(tool, raw, context.allowPersonalData);
  }

  // Checked here as well as in the listing, because a client may call a tool
  // it was never shown.
  if (!context.allowPersonalData && tool.body) {
    const personal = personalFieldsInCall(tool.body, bodyArgs);
    if (personal.length > 0) {
      throw new ConnectorError(
        'PERSONAL_DATA_WITHHELD',
        `This call would write ${personal.join(', ')}, which this session's data scope withholds. `
          + 'Nothing was sent. Leave those fields out, or ask the Owner to reconnect with --data-scope '
          + 'full if the task genuinely needs them; that is a data-protection decision, not a workaround.',
        { action: 'fix_arguments' },
      );
    }
  }

  if (tool.approval && !reason) {
    throw new ConnectorError(
      'REASON_REQUIRED',
      `${tool.name} needs a person's approval. Pass a reason saying why; it is shown to them and recorded.`,
      { action: 'fix_arguments' },
    );
  }

  // A body field called `reason` takes the same value: the change's reason is
  // what the route records beside it, and peeling it off as the header alone
  // would leave the request without it.
  if (reason && tool.body?.some((field) => field.name === 'reason')) {
    bodyArgs['reason'] = reason;
  }

  if (tool.upload === 'csv') {
    if (!context.uploadRoot) {
      throw new ConnectorError('TOOL_DISABLED', uploadsOff(tool));
    }
    if (typeof file !== 'string') throw new FileAccessError('file must be a path under the upload directory.');
    const csv = readInsideRoot(context.uploadRoot, file, { maxBytes: CSV_MAX_BYTES, extensions: CSV_EXTENSIONS });
    const result = await context.client.upload<unknown>(
      request.path,
      { fieldName: 'file', fileName: csv.name, contentType: 'text/csv', bytes: csv.bytes },
      { query: request.query, reason, replaySafe },
    );
    return { imported: result, source: { file: csv.name, bytes: csv.bytes.length, sha256: csv.sha256 } };
  }

  const hasBodyArgs = Object.keys(bodyArgs).length > 0;
  if (tool.body && tool.optionalBody && hasBodyArgs) {
    const required = tool.body.filter((field) => field.required).map((field) => field.name);
    const missing = required.filter((name) => bodyArgs[name] === undefined);
    if (missing.length > 0) {
      throw new ConnectorError(
        'INVALID_ARGUMENTS',
        `${tool.name} takes either no body at all or a complete one; ${missing.join(', ')} `
          + `${missing.length === 1 ? 'is' : 'are'} missing. ${optionalBodyNote(required)} Nothing was sent.`,
        { action: 'fix_arguments' },
      );
    }
  }
  let body: Record<string, unknown> | undefined;
  if (tool.body) {
    body = tool.optionalBody && !hasBodyArgs
      ? undefined
      : buildBody(tool.body, bodyArgs, { uploadRoot: context.uploadRoot });
  } else if (hasBodyArgs) {
    const accepted = allParams(tool).map(paramName);
    throw new Error(
      `Unknown argument "${Object.keys(bodyArgs)[0]}". This tool accepts: ${[...accepted, 'reason'].join(', ')}.`,
    );
  }

  const raw = await context.client.request<unknown>(tool.method, request.path, {
    query: request.query,
    body,
    reason,
    approvalId,
    replaySafe,
  });
  const gated = applyPolicy(tool, raw, context.allowPersonalData);

  if (tool.returnsCredential) {
    const handoff: Record<string, unknown> = { note: CREDENTIAL_NOTE };
    if (typeof bodyArgs['temporaryPassword'] === 'string') handoff['temporaryPassword'] = bodyArgs['temporaryPassword'];
    return { result: gated, credentialHandoff: handoff };
  }
  return gated;
}

export function downloadsOff(tool: ToolDefinition): string {
  return `${tool.name} saves a file onto this machine, so it works only when the connector is started `
    + 'with --download-dir naming a directory the person chose. Nothing was requested.';
}

export function uploadsOff(tool: ToolDefinition): string {
  return `${tool.name} reads a file from this machine, so it works only when the connector is started `
    + 'with --upload-root naming the directory the file is in. Nothing was sent.';
}
