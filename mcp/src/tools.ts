import type { ApiClient } from './client.js';
import { applyFieldPolicy, type ModelName } from './field-policy.js';
import { buildRequest, inputSchemaFor, isPathParam, paramName, type ParamSpec } from './tool-input.js';
import { bodySchemaFor, buildBody, flattenFields, type FieldSpec } from './tool-body.js';
import { ConnectorError } from './errors.js';
import {
  FileAccessError,
  InlineFile,
  decodeInlineBase64,
  readInsideRoot,
  safeFileName,
  writeDownload,
  CSV_EXTENSIONS,
  CSV_MAX_BYTES,
  INLINE_DOWNLOAD_MAX_BYTES,
  INLINE_UPLOAD_MAX_BASE64_CHARS,
  type FileMode,
} from './files.js';
import type { ToolGroup } from './toolsets.js';
import type { AccessLevel } from './config.js';

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
  /**
   * Offered only where files stay on the person's own machine (stdio). Over
   * HTTP a download comes back inside the conversation; for a file that must
   * never enter it, the sentence says why the tool is withheld there.
   */
  localOnly?: string;
}

export interface ToolContext {
  client: ApiClient;
  allowPersonalData: boolean;
  downloadDir?: string | undefined;
  uploadRoot?: string | undefined;
  /** `inline` in serve --http mode: files travel base64-encoded in arguments and results. */
  fileMode?: FileMode | undefined;
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

const INLINE_UPLOAD_SCHEMA = {
  contentBase64: {
    type: 'string',
    maxLength: INLINE_UPLOAD_MAX_BASE64_CHARS,
    contentEncoding: 'base64',
    description:
      'The CSV bank statement\'s contents, base64-encoded. At most 5 MiB of base64 (the request as a whole is '
      + 'capped at 5 MiB), and at most 5 MB decoded.',
  },
  fileName: {
    type: 'string',
    maxLength: 150,
    description: 'The statement\'s file name, ending .csv or .txt. Defaults to statement.csv.',
  },
};

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
export function toolInputSchema(tool: ToolDefinition, mode: FileMode = 'local'): Record<string, unknown> {
  const fromParams = inputSchemaFor(allParams(tool));
  const fromBody = tool.body ? bodySchemaFor(tool.body, mode) : { properties: {} };
  const properties: Record<string, unknown> = {
    ...fromParams.properties,
    ...(fromBody['properties'] as Record<string, unknown>),
  };
  const required = [...fromParams.required, ...((fromBody['required'] as string[] | undefined) ?? [])];

  if (tool.upload === 'csv' && mode === 'inline') {
    Object.assign(properties, INLINE_UPLOAD_SCHEMA);
    required.push('contentBase64');
  } else if (tool.upload === 'csv') {
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
  const file: Record<string, unknown> = {};

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
    if (tool.upload && (key === 'file' || key === 'contentBase64' || key === 'fileName')) {
      file[key] = value;
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

  if (tool.binary) {
    if (Object.keys(bodyArgs).length > 0) {
      throw new Error(`Unknown argument "${Object.keys(bodyArgs)[0]}". This tool accepts: ${allParams(tool).map(paramName).join(', ') || 'no arguments'}.`);
    }
    const inline = context.fileMode === 'inline';
    if (!context.downloadDir && !inline) {
      throw new ConnectorError('TOOL_DISABLED', downloadsOff(tool));
    }
    if (tool.approval && !reason) {
      throw new ConnectorError('REASON_REQUIRED', `${tool.name} needs a person's approval. Pass a reason saying why.`, { action: 'fix_arguments' });
    }
    const downloaded = await context.client.download(
      request.path,
      { query: request.query, reason, approvalId },
      tool.method ?? 'GET',
    );
    if (inline) {
      if (downloaded.bytes.length > INLINE_DOWNLOAD_MAX_BYTES) {
        throw new ConnectorError(
          'DOWNLOAD_TOO_LARGE',
          `${tool.name} produced a ${(downloaded.bytes.length / 1024 / 1024).toFixed(1)} MB file. Over HTTP the connector `
            + `returns files of up to ${INLINE_DOWNLOAD_MAX_BYTES / 1024 / 1024} MiB inline. Use the stdio connector `
            + '(filingbridge-mcp with --download-dir) on the person\'s own machine, or ask for a smaller artefact.',
        );
      }
      return new InlineFile(
        safeFileName(downloaded.fileName ?? tool.binary.defaultName, tool.binary.defaultName),
        downloaded.contentType,
        downloaded.bytes,
      );
    }
    const downloadDir = context.downloadDir!;
    const written = writeDownload(
      downloadDir,
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
    const csv = context.fileMode === 'inline' ? inlineCsv(file) : localCsv(tool, context, file);
    const result = await context.client.upload<unknown>(
      request.path,
      { fieldName: 'file', fileName: csv.name, contentType: 'text/csv', bytes: csv.bytes },
      { query: request.query, reason },
    );
    return { imported: result, source: { file: csv.name, bytes: csv.bytes.length, sha256: csv.sha256 } };
  }

  const hasBodyArgs = Object.keys(bodyArgs).length > 0;
  let body: Record<string, unknown> | undefined;
  if (tool.body) {
    body = tool.optionalBody && !hasBodyArgs
      ? undefined
      : buildBody(tool.body, bodyArgs, { uploadRoot: context.uploadRoot, fileMode: context.fileMode });
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
  });
  const gated = applyPolicy(tool, raw, context.allowPersonalData);

  if (tool.returnsCredential) {
    const handoff: Record<string, unknown> = { note: CREDENTIAL_NOTE };
    if (typeof bodyArgs['temporaryPassword'] === 'string') handoff['temporaryPassword'] = bodyArgs['temporaryPassword'];
    return { result: gated, credentialHandoff: handoff };
  }
  return gated;
}

/** The CSV named by path under --upload-root (stdio mode). */
function localCsv(tool: ToolDefinition, context: ToolContext, file: Record<string, unknown>) {
  for (const key of ['contentBase64', 'fileName']) {
    if (file[key] !== undefined) {
      throw new Error(`Unknown argument "${key}". This connector reads the statement from --upload-root: pass file.`);
    }
  }
  if (!context.uploadRoot) {
    throw new ConnectorError('TOOL_DISABLED', uploadsOff(tool));
  }
  if (typeof file['file'] !== 'string') throw new FileAccessError('file must be a path under the upload directory.');
  return readInsideRoot(context.uploadRoot, file['file'], { maxBytes: CSV_MAX_BYTES, extensions: CSV_EXTENSIONS });
}

/** The CSV sent base64-encoded in the arguments (serve --http mode). */
function inlineCsv(file: Record<string, unknown>) {
  if (file['file'] !== undefined) {
    throw new FileAccessError(
      'Over HTTP the connector cannot read files by path. Send the statement as contentBase64 (and fileName), '
        + 'or use the stdio connector with --upload-root.',
    );
  }
  if (typeof file['contentBase64'] !== 'string') {
    throw new FileAccessError('contentBase64 must be the CSV statement, base64-encoded.');
  }
  const rawName = file['fileName'] === undefined ? 'statement.csv' : file['fileName'];
  if (typeof rawName !== 'string' || rawName.length === 0 || rawName.length > 150) {
    throw new FileAccessError('fileName must be a file name of up to 150 characters.');
  }
  const name = safeFileName(rawName, 'statement.csv');
  const dot = name.lastIndexOf('.');
  const extension = dot >= 0 ? name.slice(dot).toLowerCase() : '';
  if (!CSV_EXTENSIONS.has(extension)) {
    throw new FileAccessError(`FilingBridge does not accept ${name} here. It accepts: ${[...CSV_EXTENSIONS].join(', ')}.`);
  }
  const decoded = decodeInlineBase64(file['contentBase64'], 'contentBase64', CSV_MAX_BYTES);
  return { name, bytes: decoded.bytes, sha256: decoded.sha256 };
}

/**
 * Descriptions are written for the stdio connector, which reads and writes
 * named directories. Over HTTP the same tools take and return base64 instead,
 * and the description says so rather than naming flags that do not exist.
 */
const INLINE_DESCRIPTION_REWRITES: readonly [RegExp, string][] = [
  [/ into --download-dir/g, ' and return it in the result as a base64 embedded resource (up to 1 MiB)'],
  [/The file must be under --upload-root, at most 5 MB\./g, 'Send the file base64-encoded as contentBase64; the request is capped at 5 MiB.'],
  [/Needs the evidence file under --upload-root\./g, 'Send the evidence file base64-encoded.'],
  [/,? (?:both )?from --upload-root/g, ', sent base64-encoded'],
  [/--upload-root/g, 'base64-encoded content'],
  [/--download-dir/g, 'the result'],
];

export function describeTool(tool: ToolDefinition, mode: FileMode = 'local'): string {
  if (mode !== 'inline') return tool.description;
  let text = tool.description;
  for (const [pattern, replacement] of INLINE_DESCRIPTION_REWRITES) text = text.replace(pattern, replacement);
  return text;
}

export function localOnlyRefusal(tool: ToolDefinition): string {
  return `${tool.name} is offered only by the connector running on the person's own machine (stdio, with `
    + `--download-dir). ${tool.localOnly ?? ''} Nothing was requested.`;
}

export function downloadsOff(tool: ToolDefinition): string {
  return `${tool.name} saves a file onto this machine, so it works only when the connector is started `
    + 'with --download-dir naming a directory the person chose. Nothing was requested.';
}

export function uploadsOff(tool: ToolDefinition): string {
  return `${tool.name} reads a file from this machine, so it works only when the connector is started `
    + 'with --upload-root naming the directory the file is in. Nothing was sent.';
}
