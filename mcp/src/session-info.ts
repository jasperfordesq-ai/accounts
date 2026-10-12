import type { ApiClient } from './client.js';
import { applyFieldPolicy } from './field-policy.js';
import { SESSION_ROUTE, parsePosture } from './session-level.js';
import { CONNECTOR_VERSION } from './version.js';
import { FILING_BOUNDARY_SENTENCE } from './instructions.js';
import type { ToolAnnotations } from './tools.js';
import type { ToolGroup } from './toolsets.js';

/**
 * Who the connector is acting as, and what it may do.
 *
 * The first tool the instructions name. An agent connected to the wrong
 * workspace, or at read level, otherwise has no way to notice before its
 * first refusal. The person's own name and email follow the same gate as
 * every other record.
 */
export const SESSION_INFO_TOOL = {
  name: 'session_info',
  description:
    'Which FilingBridge workspace this connector is signed in to, the person\'s role, the access level (read, '
    + 'write or admin), the data scope (withheld or full), the change budget and approval window, and which '
    + 'file directories are configured. Call this first.',
  inputSchema: { type: 'object', properties: {}, additionalProperties: false },
  annotations: {
    title: 'Session info',
    readOnlyHint: true,
    destructiveHint: false,
    idempotentHint: true,
    openWorldHint: false,
  } satisfies ToolAnnotations,
};

export interface SessionInfoOptions {
  baseUrl: string;
  fileMode?: 'local' | 'inline' | undefined;
  downloadDir?: string | undefined;
  uploadRoot?: string | undefined;
  toolsets?: readonly ToolGroup[] | undefined;
}

export async function runSessionInfo(client: ApiClient, options: SessionInfoOptions): Promise<Record<string, unknown>> {
  const body = await client.get<Record<string, unknown>>(SESSION_ROUTE);
  const posture = parsePosture(body);
  const full = posture?.dataScope === 'full';
  const user = body && typeof body === 'object' ? body['user'] : undefined;

  return {
    product: 'FilingBridge',
    workspace: { slug: posture?.tenantSlug ?? null },
    session: posture
      ? {
          role: posture.role,
          userId: posture.userId,
          accessLevel: posture.accessLevel,
          dataScope: posture.dataScope,
          mfaVerified: posture.mfaVerified,
          clientKind: posture.clientKind ?? 'McpConnector',
          writeBudgetPerMinute: posture.writeBudgetPerMinute,
          approvalMinutes: posture.approvalMinutes,
        }
      : { note: 'FilingBridge described this session in a form this connector does not understand.' },
    account: user && typeof user === 'object'
      ? applyFieldPolicy('AuthResponse', user, full)
      : full
        ? { note: 'FilingBridge did not include the account details.' }
        : { note: 'Name and email are withheld while the data scope is Withheld.' },
    connector: {
      version: CONNECTOR_VERSION,
      baseUrl: options.baseUrl,
      downloads: options.fileMode === 'inline'
        ? 'inline: returned in the result as a base64 embedded resource, up to 1 MiB'
        : options.downloadDir ? 'enabled (--download-dir)' : 'off: start with --download-dir to enable',
      uploads: options.fileMode === 'inline'
        ? 'inline: send the file base64-encoded (contentBase64), the request capped at 5 MiB'
        : options.uploadRoot ? 'enabled (--upload-root)' : 'off: start with --upload-root to enable',
      toolsets: options.toolsets ? [...options.toolsets] : 'all',
    },
    filingBoundary: FILING_BOUNDARY_SENTENCE,
  };
}
