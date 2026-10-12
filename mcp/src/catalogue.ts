import type { ToolDefinition } from './tools.js';
import { COMPANY_TOOLS } from './tools-companies.js';
import { BANKING_TOOLS } from './tools-banking.js';
import { DISCLOSURE_TOOLS, YEAR_END_TOOLS } from './tools-year-end.js';
import { REPORTING_TOOLS } from './tools-reporting.js';
import { FILING_TOOLS, HANDOFF_TOOLS } from './tools-filing.js';
import { ADMIN_TOOLS } from './tools-admin.js';
import { CONNECTOR_TOOLS } from './tools-connector.js';

/**
 * Every tool over a FilingBridge route, in the order a client lists them.
 *
 * The three core tools (session_info, search, fetch) live beside the server
 * because they are not one route each. route-coverage.test.ts requires every
 * operation in the OpenAPI contract to be one of these or an exclusion with
 * a written reason.
 */
export const TOOLS: readonly ToolDefinition[] = [
  ...COMPANY_TOOLS,
  ...BANKING_TOOLS,
  ...YEAR_END_TOOLS,
  ...DISCLOSURE_TOOLS,
  ...REPORTING_TOOLS,
  ...FILING_TOOLS,
  ...HANDOFF_TOOLS,
  ...ADMIN_TOOLS,
  ...CONNECTOR_TOOLS,
];

export function findTool(name: string): ToolDefinition | undefined {
  return TOOLS.find((tool) => tool.name === name);
}
