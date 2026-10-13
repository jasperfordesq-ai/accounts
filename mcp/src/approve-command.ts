import type { ConnectorProfile } from './config.js';

/** An approval identifier exactly as FilingBridge issues it, and as `approve` accepts it. */
export const APPROVAL_ID = /^[A-Za-z0-9-]{1,64}$/;

/** Characters a POSIX shell, PowerShell and cmd.exe all pass through unquoted. */
const BARE = /^[A-Za-z0-9_./:-]+$/;

/**
 * Quotes one argument for whichever shell the person uses. Double quotes are
 * the one quoting POSIX shells, PowerShell and cmd.exe share; a value that
 * would need escaping inside them is refused rather than guessed at.
 */
export function shellArgument(value: string): string {
  if (BARE.test(value)) return value;
  if (/["$`\\!%^&|<>\r\n]/.test(value)) {
    throw new Error(`Cannot quote ${JSON.stringify(value)} safely for a shell.`);
  }
  return `"${value}"`;
}

/**
 * The approve command for this connector as it is actually configured.
 * FilingBridge prints `filingbridge-mcp approve <id>`, which signs in against
 * the default profile and whatever FILINGBRIDGE_BASE_URL says; for someone who
 * connected with --profile and --base-url that command fails. The origin is
 * taken from the parsed URL, so nothing but scheme, host and port reaches it.
 */
export function approveCommandFor(approvalId: string, profile: ConnectorProfile, baseUrl: string): string {
  if (!APPROVAL_ID.test(approvalId)) throw new Error('The approval identifier is not one FilingBridge issues.');
  const origin = new URL(baseUrl).origin;
  return ['filingbridge-mcp', 'approve', approvalId, '--profile', profile, '--base-url', origin]
    .map(shellArgument)
    .join(' ');
}
