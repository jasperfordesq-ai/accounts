export const PASSWORD_POLICY_DESCRIPTION =
  "Use at least 20 characters with an uppercase letter, a lowercase letter, a number, and a symbol. Known-compromised passwords are rejected.";

export function meetsPasswordCompositionPolicy(password: string): boolean {
  return password.length >= 20
    && /\p{Lu}/u.test(password)
    && /\p{Ll}/u.test(password)
    && /\p{N}/u.test(password)
    && /[^\p{L}\p{N}]/u.test(password);
}
