import { test } from 'node:test';
import assert from 'node:assert/strict';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { dirname, resolve } from 'node:path';

const moduleUrl = pathToFileURL(resolve(dirname(fileURLToPath(import.meta.url)), '../../scripts/windows-args.mjs')).href;

test('Windows arguments are quoted by CommandLineToArgvW rules, backslashes included', async () => {
  const { quoteWindowsArgument } = (await import(moduleUrl)) as { quoteWindowsArgument: (value: string) => string };
  const backslash = String.fromCharCode(92);
  const quote = '"';
  assert.equal(quoteWindowsArgument('plain'), 'plain');
  assert.equal(quoteWindowsArgument(''), quote + quote);
  // A path with a space ending in a backslash: the trailing backslash is doubled before the closing quote.
  assert.equal(quoteWindowsArgument(`C:${backslash}a b${backslash}`), `${quote}C:${backslash}a b${backslash}${backslash}${quote}`);
  // Backslashes not before a quote stay literal.
  assert.equal(quoteWindowsArgument(`C:${backslash}a b`), `${quote}C:${backslash}a b${quote}`);
  // An embedded quote is escaped, and the backslashes before it are doubled.
  assert.equal(quoteWindowsArgument(`x${backslash}${quote}y`), `${quote}x${backslash}${backslash}${backslash}${quote}y${quote}`);
  assert.equal(quoteWindowsArgument(`say ${quote}hi${quote}`), `${quote}say ${backslash}${quote}hi${backslash}${quote}${quote}`);
});
