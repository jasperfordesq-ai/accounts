import { redactSecrets } from './redact.js';

/**
 * A line per tool call on stderr, when asked for.
 *
 * stderr is the right place: in serve mode stdout is the protocol stream.
 * Neither arguments nor results are ever logged. Arguments can carry personal
 * data, and the results of the credential tools (user_create, user_invite,
 * user_password_reset) carry a credential meant only for the person.
 */
export function createDiagnostics(
  enabled: boolean,
  write: (line: string) => void = (line) => {
    process.stderr.write(`${line}\n`);
  },
) {
  return {
    toolCall(name: string, outcome: string, ms: number): void {
      if (!enabled) return;
      write(redactSecrets(`[filingbridge-mcp] ${name} ${outcome} ${ms}ms`));
    },
  };
}
