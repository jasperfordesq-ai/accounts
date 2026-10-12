import type { ConnectorProfile } from './config.js';

/**
 * Reads a password from a piped stdin. Bytes are accumulated and decoded
 * once, so a multi-byte character split across two reads is not corrupted.
 */
export async function readPasswordFromStdin(stream: AsyncIterable<Buffer>): Promise<string> {
  let bytes = Buffer.alloc(0);
  for await (const chunk of stream) {
    bytes = Buffer.concat([bytes, chunk]);
    const newline = bytes.indexOf(0x0a);
    if (newline !== -1) {
      bytes = bytes.subarray(0, newline);
      break;
    }
  }
  const text = bytes.toString('utf8').replace(/\r$/, '');
  if (text.length === 0) throw new Error('No password was supplied on stdin.');
  return text;
}

/**
 * A piped password is a test affordance for a Development stack. The local
 * profile is already confined to loopback, and refusing a terminal stops a
 * real password being typed where it would land in shell history.
 */
export function assertNonInteractiveConnectAllowed(
  config: { profile: ConnectorProfile; passwordStdin: boolean },
  isTty: boolean,
): void {
  if (!config.passwordStdin) return;
  if (config.profile !== 'local') {
    throw new Error('--password-stdin is only valid with --profile local.');
  }
  if (isTty) {
    throw new Error('--password-stdin expects a pipe, but stdin is a terminal. Run connect without it to be prompted.');
  }
}

/**
 * Approving is the one thing in this connector only a person can do.
 *
 * The value of per-action approval is that the agent which asked for the
 * action cannot also grant it. An agent can spawn a process and write to its
 * standard input; it cannot type at a terminal. Requiring one keeps the two
 * apart, so this refuses a pipe on every profile, the local one included.
 */
export function assertApproveAllowed(
  config: { approvalId?: string | undefined; passwordStdin: boolean },
  isTty: { stdin: boolean; stdout: boolean },
): void {
  if (!config.approvalId) {
    throw new Error(
      'approve needs the identifier FilingBridge printed when it refused the action. Run: filingbridge-mcp approve <id>',
    );
  }
  if (config.passwordStdin) {
    throw new Error(
      'approve does not accept a piped password. The point of approving is that a person does it, at a terminal.',
    );
  }
  if (!isTty.stdin || !isTty.stdout) {
    throw new Error(
      'approve must be run at a terminal, by the person whose account this is. It refuses to run from a script '
        + 'or an agent, because an agent that could approve its own actions would make the approval meaningless.',
    );
  }
}
