// Windows command-line quoting for the pack script (npm/npx are .cmd shims started through a shell).
/**
 * Quotes one argument for the Windows command line (CommandLineToArgvW rules): backslashes are
 * literal except before a quote or the closing quote, where each must be doubled.
 */
export function quoteWindowsArgument(value) {
  if (value.length > 0 && !/[\s"]/.test(value)) return value;
  let quoted = '"';
  let backslashes = 0;
  for (const character of value) {
    if (character === '\\') {
      backslashes += 1;
    } else if (character === '"') {
      quoted += '\\'.repeat(backslashes * 2 + 1) + '"';
      backslashes = 0;
    } else {
      quoted += '\\'.repeat(backslashes) + character;
      backslashes = 0;
    }
  }
  return quoted + '\\'.repeat(backslashes * 2) + '"';
}
