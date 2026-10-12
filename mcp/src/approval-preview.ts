import type { ApprovalPreview } from './session.js';

export type ApprovalState = 'pending' | 'approved' | 'consumed' | 'expired';

/**
 * What `approve` prints before it asks for a password.
 *
 * The summary is FilingBridge's, built from the route it matched and the
 * record it found, never from anything the agent sent, and the host is
 * printed first: an approval sent to the wrong host is a password sent to
 * the wrong host.
 */
export function formatApprovalPreview(preview: ApprovalPreview, baseUrl: string): string {
  return (
    `FilingBridge at: ${baseUrl}\n`
    + 'You are about to approve:\n'
    + `  ${preview.summary}\n`
    + `  Action:  ${preview.method} ${preview.routePattern}\n`
    + `  Record:  ${preview.resourceId ?? '(none)'}\n`
    + `  Asked:   ${preview.createdAtUtc ?? 'unknown'}\n`
    + `  Expires: ${preview.expiresAtUtc}\n`
  );
}

export function approvalState(preview: ApprovalPreview, now: Date): ApprovalState {
  if (preview.consumedAtUtc) return 'consumed';
  if (preview.approvedAtUtc) return 'approved';
  const expires = Date.parse(preview.expiresAtUtc);
  if (Number.isFinite(expires) && expires <= now.getTime()) return 'expired';
  return 'pending';
}

export function explainState(state: Exclude<ApprovalState, 'pending'>): string {
  switch (state) {
    case 'approved':
      return 'This approval is already granted. Ask the assistant to try the action again; nothing more is needed here.';
    case 'consumed':
      return 'This approval has already been used. If the action is wanted again, ask the assistant to attempt it and approve the new identifier it prints.';
    case 'expired':
      return 'This approval has expired. Ask the assistant to attempt the action again and approve the new identifier it prints.';
  }
}
