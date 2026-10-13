/**
 * Secrets this process holds, kept out of every message it prints or returns.
 *
 * Tokens are registered as they arrive and scrubbed from error text and
 * diagnostics. A credential a tool deliberately hands back (a temporary
 * password, an invitation token) is never registered here: it is meant to
 * reach the person, and redacting it from the result would defeat the tool.
 * It is kept out of diagnostics a different way — diagnostics never log
 * arguments or results at all.
 */
const secrets = new Set<string>();

const PATTERNS: RegExp[] = [
  /(Bearer[ \t]+)(\S+)/gi,
  /\b(fbc1\.)[A-Za-z0-9_\-.]+/g,
  /\b(fbr1\.)[A-Za-z0-9_\-.]+/g,
];

export function registerSecret(value: string): void {
  if (value.length > 8) secrets.add(value);
}

export function clearSecrets(): void {
  secrets.clear();
}

export function redactSecrets(input: string): string {
  let out = input;
  const ordered = [...secrets].sort((a, b) => b.length - a.length);
  for (const secret of ordered) out = out.split(secret).join('[redacted]');
  for (const pattern of PATTERNS) out = out.replace(pattern, '$1[redacted]');
  return out;
}
