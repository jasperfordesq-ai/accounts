import { TOOLS } from './catalogue.js';
import { FETCH_TOOL, SEARCH_TOOL } from './references.js';
import { SESSION_INFO_TOOL } from './session-info.js';
import { APPROVAL_WAIT_TOOL } from './approval-wait.js';

export interface ManifestTool {
  readonly name: string;
  readonly description: string;
}

/**
 * The first sentence of a tool description: what Claude Desktop shows beside
 * the tool's name before the extension is installed. The full description
 * still comes from tools/list once the connector runs.
 */
export function firstSentence(description: string): string {
  const text = description.replace(/\s+/g, ' ').trim();
  const match = /^.*?[.!?](?=\s+[A-Z(]|$)/.exec(text);
  return match ? match[0] : text;
}

/**
 * The tools section of mcp/manifest.json, derived from the catalogue in the
 * order a stdio client lists them: the four core tools, then every route
 * tool. `npm run manifest:tools` writes it; package.test.ts fails when the
 * manifest and the catalogue disagree.
 */
export function manifestTools(): ManifestTool[] {
  return [SESSION_INFO_TOOL, SEARCH_TOOL, FETCH_TOOL, APPROVAL_WAIT_TOOL, ...TOOLS].map((tool) => ({
    name: tool.name,
    description: firstSentence(tool.description),
  }));
}
