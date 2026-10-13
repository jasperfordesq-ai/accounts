import type { ToolDefinition } from './tools.js';

const CONNECTOR = '/api/auth/connector';

const OWNER_OTHERS = 'Another user\'s, by userId, only for an Owner.';

/**
 * The connected-agent management routes the model may see.
 *
 * Signing in, rotating, signing out and above all granting an approval are
 * connector credential plumbing, used by the CLI and never by the model (see
 * EXCLUDED_ROUTES). Reading what is pending, which agents are connected and
 * what they did, and disconnecting one, are ordinary account housekeeping.
 * None of these tools can grant anything.
 */
export const CONNECTOR_TOOLS: readonly ToolDefinition[] = [
  {
    name: 'connector_approvals_pending',
    group: 'connector',
    description:
      'Approvals waiting for the person you act for: what each one would do and when it expires. Reading them '
      + 'grants nothing; only the person can approve, at a terminal or on the web approvals page.',
    path: `${CONNECTOR}/approvals`,
    noRecordsBecause: 'Approval summaries are built by FilingBridge from public fields of the route and record only.',
  },
  {
    name: 'connector_approval_get',
    group: 'connector',
    description: 'One approval: what it covers, when it expires, and whether it has been granted or used.',
    path: `${CONNECTOR}/approvals/{approvalId}`,
    params: [{ kind: 'guid', name: 'approvalId', describe: 'The approvalId FilingBridge returned in a refusal.' }],
    noRecordsBecause: 'Approval summaries are built by FilingBridge from public fields of the route and record only.',
  },
  {
    name: 'connector_sessions_list',
    group: 'connector',
    description: `The connected agents of the person you act for: access level, data scope, device label and when last used. ${OWNER_OTHERS}`,
    path: `${CONNECTOR}/sessions`,
    params: [{ kind: 'intFilter', name: 'userId', describe: 'Whose agents to list; yourself when omitted. Owner only for others.' }],
    noRecordsBecause: 'Session posture and timestamps; the device label is the name the person gave their own machine.',
  },
  {
    name: 'connector_session_revoke',
    group: 'connector',
    description:
      `Disconnect one connected agent (a whole session family) at once. Its credential stops working and cannot be revived; the person reconnects with filingbridge-mcp connect. ${OWNER_OTHERS}`,
    method: 'POST',
    path: `${CONNECTOR}/sessions/{familyId}/revoke`,
    params: [
      { kind: 'guid', name: 'familyId', describe: 'The session family, as connector_sessions_list returns it.' },
      { kind: 'intFilter', name: 'userId', describe: 'Whose agent to disconnect; yourself when omitted. Owner only for others.' },
    ],
    level: 'write',
    noRecordsBecause: 'An acknowledgement of the change; no records about individuals.',
  },
  {
    name: 'connector_activity_list',
    group: 'connector',
    description: `What connected agents did, newest first: every change and every refusal, with the route, status and recorded reason. ${OWNER_OTHERS}`,
    path: `${CONNECTOR}/activity`,
    params: [
      { kind: 'intFilter', name: 'userId', describe: 'Whose agents\' activity; yourself when omitted. Owner only for others.' },
      { kind: 'page' },
      { kind: 'pageSize', max: 100 },
    ],
    noRecordsBecause: 'Routes, statuses, timestamps and the reasons agents recorded; no record contents.',
  },
];
