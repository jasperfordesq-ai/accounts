/**
 * The tool groups a person can narrow the connector to with --toolsets.
 *
 * `core` is always on: session_info, search and fetch are how an agent learns
 * who it is acting as and finds the identifiers every other tool takes.
 */
export const TOOL_GROUPS = [
  'core',
  'companies',
  'officers',
  'periods',
  'banking',
  'transactions',
  'categories',
  'classification',
  'year_end',
  'disclosures',
  'adjustments',
  'audit',
  'statements',
  'working_papers',
  'documents',
  'revenue',
  'deadlines',
  'charity',
  'filing',
  'handoff',
  'admin_users',
  'privacy',
  'operations',
  'system',
  'connector',
] as const;

export type ToolGroup = (typeof TOOL_GROUPS)[number];

export const ALWAYS_ON: ToolGroup = 'core';

export function inToolsets(group: ToolGroup, toolsets: readonly ToolGroup[] | undefined): boolean {
  if (group === ALWAYS_ON) return true;
  return toolsets === undefined || toolsets.includes(group);
}
