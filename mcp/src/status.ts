import type { SessionPosture } from './session-level.js';

/**
 * What `status` prints. The level and scope come from FilingBridge, because
 * that is where they are held, never from the flags this process was given.
 */
export function formatStatus(baseUrl: string, posture: SessionPosture): string {
  return (
    `Connected to ${baseUrl}\n`
    + `Workspace: ${posture.tenantSlug ?? '(unknown)'}\n`
    + `Role: ${posture.role}\n`
    + `Access level: ${posture.accessLevel.toUpperCase()}\n`
    + `Personal data: ${posture.dataScope === 'full' ? 'FULL (released to the agent)' : 'WITHHELD'}\n`
    + (posture.writeBudgetPerMinute !== null ? `Change budget: ${posture.writeBudgetPerMinute} a minute\n` : '')
    + (posture.approvalMinutes !== null ? `Approvals expire after: ${posture.approvalMinutes} minutes\n` : '')
  );
}
