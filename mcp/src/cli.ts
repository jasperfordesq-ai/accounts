#!/usr/bin/env node
import { argv, exit, stderr, stdin, stdout, env } from 'node:process';
import { runCli } from './commands.js';
import { redactSecrets } from './redact.js';

runCli(argv.slice(2), {
  stdin,
  stdout,
  stderr,
  env,
  stdinIsTty: stdin.isTTY === true,
  stdoutIsTty: stdout.isTTY === true,
}).catch((error: unknown) => {
  // In serve mode stdout is the MCP protocol stream; anything else written
  // there breaks the client's parser. Every failure goes to stderr.
  stderr.write(`${redactSecrets((error as Error).message)}\n`);
  exit(1);
});
