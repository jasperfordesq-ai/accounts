import { Server } from '@modelcontextprotocol/sdk/server/index.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import type { Transport } from '@modelcontextprotocol/sdk/shared/transport.js';
import {
  CallToolRequestSchema,
  GetPromptRequestSchema,
  ListPromptsRequestSchema,
  ListResourceTemplatesRequestSchema,
  ListResourcesRequestSchema,
  ListToolsRequestSchema,
  ReadResourceRequestSchema,
} from '@modelcontextprotocol/sdk/types.js';
import { TOOLS } from './catalogue.js';
import { annotationsFor, downloadsOff, runTool, toolInputSchema, uploadsOff, type ToolDefinition } from './tools.js';
import { ApiClient } from './client.js';
import type { TokenSource } from './token-source.js';
import { CONNECTOR_VERSION } from './version.js';
import { INSTRUCTIONS } from './instructions.js';
import { ConnectorError } from './errors.js';
import { errorResult, okResult } from './results.js';
import { SESSION_INFO_TOOL, runSessionInfo } from './session-info.js';
import { createDiagnostics } from './diagnostics.js';
import { findPrompt, promptListing } from './prompts.js';
import { RESOURCES, RESOURCE_TEMPLATES, readResource } from './resources.js';
import {
  FETCH_TOOL,
  SEARCH_TOOL,
  parseSearchArgs,
  pickRecord,
  resolveReference,
  shapeSearchResults,
  webUrlFor,
} from './references.js';
import {
  createPostureResolver,
  fetchSessionPosture,
  fullScopeRefusal,
  permits,
  refusalFor,
  type SessionPosture,
} from './session-level.js';
import { inToolsets, type ToolGroup } from './toolsets.js';

export interface ServerOptions {
  baseUrl: string;
  tokens: TokenSource;
  toolsets?: readonly ToolGroup[] | undefined;
  downloadDir?: string | undefined;
  uploadRoot?: string | undefined;
  verbose?: boolean;
  fetchImpl?: typeof fetch;
  /** Replaced in tests; the default asks FilingBridge. */
  posture?: () => Promise<SessionPosture>;
  /** Where diagnostics go when verbose; stderr by default. */
  log?: (line: string) => void;
}

const CORE_TOOLS = [SESSION_INFO_TOOL, SEARCH_TOOL, FETCH_TOOL] as const;
const CORE_NAMES = new Set<string>(CORE_TOOLS.map((tool) => tool.name));

/** Whether a tool is offered to this session with this configuration. */
export function offered(tool: ToolDefinition, posture: SessionPosture, options: Pick<ServerOptions, 'toolsets' | 'downloadDir' | 'uploadRoot'>): boolean {
  if (!inToolsets(tool.group, options.toolsets)) return false;
  if (!permits(posture.accessLevel, tool)) return false;
  // A tool that refuses every call under this scope is a setting, not a
  // fault; offering it would teach the model the opposite.
  if (tool.requiresFullScope && posture.dataScope !== 'full') return false;
  if (tool.binary && !options.downloadDir) return false;
  if (tool.upload && !options.uploadRoot) return false;
  return true;
}

export function buildToolList(posture: SessionPosture, options: Pick<ServerOptions, 'toolsets' | 'downloadDir' | 'uploadRoot'>) {
  return [
    ...CORE_TOOLS.map((tool) => ({ ...tool })),
    ...TOOLS.filter((tool) => offered(tool, posture, options)).map((tool) => ({
      name: tool.name,
      title: annotationsFor(tool).title,
      description: tool.description,
      inputSchema: toolInputSchema(tool),
      annotations: annotationsFor(tool),
    })),
  ];
}

/**
 * Builds one MCP Server around a token source.
 *
 * stdio mode builds one for the life of the process around the person's
 * refreshing session; phase 2's HTTP mode will build one per request around
 * that request's bearer. Nothing in here knows which.
 */
export function createServer(options: ServerOptions): Server {
  const client = new ApiClient({
    tokens: options.tokens,
    baseUrl: options.baseUrl,
    ...(options.fetchImpl ? { fetchImpl: options.fetchImpl } : {}),
  });
  const server = new Server(
    { name: 'filingbridge', title: 'FilingBridge', version: CONNECTOR_VERSION },
    { capabilities: { tools: {}, prompts: {}, resources: {} }, instructions: INSTRUCTIONS },
  );
  const posture = createPostureResolver(options.posture ?? (() => fetchSessionPosture(client)));
  const diagnostics = createDiagnostics(options.verbose ?? false, options.log);

  async function runFetch(args: Record<string, unknown>): Promise<unknown> {
    const keys = Object.keys(args).filter((key) => key !== 'id');
    if (keys.length > 0) throw new Error(`Unknown argument "${keys[0]}". fetch accepts: id.`);
    const target = resolveReference(args['id']);
    let record: unknown;
    if (!target.pick) {
      record = await dispatch(target.tool, target.args);
    } else if (!target.pick.pages) {
      record = pickRecord(await dispatch(target.tool, target.args), target.pick.id);
    } else {
      for (let page = 1; page <= target.pick.pages.maxPages && record === undefined; page += 1) {
        const payload = await dispatch(target.tool, { ...target.args, page, pageSize: target.pick.pages.pageSize });
        record = pickRecord(payload, target.pick.id);
        const more = payload && typeof payload === 'object' && (payload as Record<string, unknown>)['hasNextPage'] === true;
        if (!more) break;
      }
    }
    if (record === undefined) {
      throw new ConnectorError('REFERENCE_NOT_FOUND', `No ${target.ref.kind} ${target.ref.id} was found where the reference says it is.`, {
        action: 'fix_arguments',
      });
    }
    const title = record && typeof record === 'object'
      ? String((record as Record<string, unknown>)['legalName'] ?? (record as Record<string, unknown>)['name']
        ?? (record as Record<string, unknown>)['title'] ?? (record as Record<string, unknown>)['description'] ?? `${target.ref.kind} ${target.ref.id}`)
      : `${target.ref.kind} ${target.ref.id}`;
    return {
      id: String(args['id']),
      title: title.slice(0, 300),
      text: JSON.stringify(record, null, 2),
      url: webUrlFor(options.baseUrl, target.ref),
      metadata: { kind: target.ref.kind, tool: target.tool, companyId: target.ref.companyId ?? null, periodId: target.ref.periodId ?? null },
    };
  }

  async function runSearch(args: Record<string, unknown>): Promise<unknown> {
    const parsed = parseSearchArgs(args);
    const body = await client.get<unknown>('/api/search', {
      query: {
        q: parsed.query,
        types: parsed.types && parsed.types.length > 0 ? parsed.types.join(',') : undefined,
        companyId: parsed.companyId,
        periodId: parsed.periodId,
        limit: parsed.limit,
      },
    });
    return shapeSearchResults(body, options.baseUrl);
  }

  /**
   * Runs one tool and returns its raw value, or throws. fetch and resource
   * reads come back through here, so every check below applies to them too.
   */
  async function dispatch(name: string, args: Record<string, unknown>): Promise<unknown> {
    if (name === SESSION_INFO_TOOL.name) {
      if (Object.keys(args).length > 0) throw new Error('session_info takes no arguments.');
      return runSessionInfo(client, options);
    }
    if (name === SEARCH_TOOL.name) return runSearch(args);
    if (name === FETCH_TOOL.name) return runFetch(args);

    const tool = TOOLS.find((candidate) => candidate.name === name);
    if (!tool) throw new ConnectorError('UNKNOWN_TOOL', `Unknown tool: ${name}`);

    // Re-checked per call and not only in the listing: a client may call a
    // tool it was never shown.
    if (!inToolsets(tool.group, options.toolsets)) {
      throw new ConnectorError(
        'TOOL_DISABLED',
        `${name} is in the "${tool.group}" toolset, and this connector was started with --toolsets `
          + `${(options.toolsets ?? []).join(',')}. Nothing was sent.`,
      );
    }
    const held = await posture();
    if (!permits(held.accessLevel, tool)) {
      throw new ConnectorError('SESSION_LEVEL_TOO_LOW', refusalFor(held.accessLevel, tool), { action: 'reconnect' });
    }
    if (tool.requiresFullScope && held.dataScope !== 'full') {
      throw new ConnectorError('DATA_SCOPE_WITHHELD', fullScopeRefusal(tool));
    }
    if (tool.binary && !options.downloadDir) throw new ConnectorError('TOOL_DISABLED', downloadsOff(tool));
    if (tool.upload && !options.uploadRoot) throw new ConnectorError('TOOL_DISABLED', uploadsOff(tool));

    return runTool(tool, {
      client,
      allowPersonalData: held.dataScope === 'full',
      downloadDir: options.downloadDir,
      uploadRoot: options.uploadRoot,
    }, args);
  }

  server.setRequestHandler(ListToolsRequestSchema, async () => ({
    tools: buildToolList(await posture(), options),
  }));

  server.setRequestHandler(CallToolRequestSchema, async (request) => {
    const args = (request.params.arguments ?? {}) as Record<string, unknown>;
    const started = Date.now();
    try {
      const value = await dispatch(request.params.name, args);
      diagnostics.toolCall(request.params.name, 'ok', Date.now() - started);
      return okResult(value);
    } catch (error) {
      const result = errorResult(error);
      diagnostics.toolCall(request.params.name, `error ${String(result.structuredContent['code'])}`, Date.now() - started);
      return result;
    }
  });

  server.setRequestHandler(ListPromptsRequestSchema, async () => ({ prompts: promptListing() }));

  server.setRequestHandler(GetPromptRequestSchema, async (request) => {
    const prompt = findPrompt(request.params.name);
    if (!prompt) throw new ConnectorError('UNKNOWN_PROMPT', `Unknown prompt: ${request.params.name}`);
    return {
      description: prompt.description,
      messages: [
        {
          role: 'user' as const,
          content: { type: 'text' as const, text: prompt.build((request.params.arguments ?? {}) as Record<string, string>) },
        },
      ],
    };
  });

  server.setRequestHandler(ListResourcesRequestSchema, async () => ({
    resources: RESOURCES.map((resource) => ({ ...resource })),
  }));

  server.setRequestHandler(ListResourceTemplatesRequestSchema, async () => ({
    resourceTemplates: RESOURCE_TEMPLATES.map((template) => ({ ...template })),
  }));

  server.setRequestHandler(ReadResourceRequestSchema, async (request) => {
    const contents = await readResource(request.params.uri, (tool, args) => dispatch(tool, args));
    return { contents: [contents] };
  });

  return server;
}

export const CORE_TOOL_NAMES: ReadonlySet<string> = CORE_NAMES;

/** stdio mode: one server for the life of the process. */
export async function startStdioServer(options: ServerOptions, transport: Transport = new StdioServerTransport()): Promise<Server> {
  const server = createServer(options);
  await server.connect(transport);
  return server;
}
