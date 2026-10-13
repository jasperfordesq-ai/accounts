#!/usr/bin/env node
// Builds filingbridge.mcpb, the Claude Desktop bundle.
//
//   npm run pack:mcpb --prefix mcp
//
// The bundle carries a fresh build of dist (no tests), the production
// node_modules from package-lock.json, manifest.json, README.md and the
// repository LICENSE, staged in a temporary directory so that sources, tests
// and development dependencies cannot leak into it.
//
// @napi-rs/keyring is a native module installed per platform and CPU, so a
// bundle runs only on the operating system and architecture it was built on.
// Build it on (and for) the machine family that will install it.
import { execFileSync } from 'node:child_process';
import { copyFileSync, existsSync, mkdtempSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { buildThirdPartyNotices } from './third-party-notices.mjs';
import { quoteWindowsArgument } from './windows-args.mjs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

/** The pinned packer. CI validates the manifest with the same version (package.test.ts checks). */
export const MCPB_VERSION = '2.1.2';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const repoRoot = resolve(root, '..');
const output = resolve(root, 'filingbridge.mcpb');
const windows = process.platform === 'win32';


function run(command, args, cwd) {
  // npm and npx are .cmd shims on Windows, which only a shell can start.
  const quoted = windows ? args.map(quoteWindowsArgument) : args;
  execFileSync(command, quoted, { cwd, stdio: 'inherit', shell: windows });
}

const stage = mkdtempSync(join(tmpdir(), 'filingbridge-mcpb-'));
try {
  console.log(`Staging in ${stage}`);
  execFileSync(process.execPath, [resolve(root, 'node_modules/typescript/bin/tsc'), '-p', resolve(root, 'tsconfig.json'), '--outDir', join(stage, 'dist')], { stdio: 'inherit' });
  rmSync(join(stage, 'dist', 'tests'), { recursive: true, force: true });

  for (const file of ['manifest.json', 'README.md', 'package.json', 'package-lock.json']) {
    copyFileSync(resolve(root, file), join(stage, file));
  }
  copyFileSync(resolve(repoRoot, 'LICENSE'), join(stage, 'LICENSE'));
  copyFileSync(resolve(repoRoot, 'NOTICE'), join(stage, 'NOTICE'));

  const manifest = JSON.parse(readFileSync(join(stage, 'manifest.json'), 'utf8'));
  const pkg = JSON.parse(readFileSync(join(stage, 'package.json'), 'utf8'));
  if (manifest.version !== pkg.version) {
    throw new Error(`manifest.json version ${manifest.version} differs from package.json ${pkg.version}.`);
  }
  const { manifestTools } = await import(pathToFileURL(join(stage, 'dist', 'manifest-tools.js')).href);
  if (JSON.stringify(manifest.tools) !== JSON.stringify(manifestTools())) {
    throw new Error('manifest.json tools are stale: run npm run manifest:tools --prefix mcp');
  }

  run('npm', ['ci', '--omit=dev', '--ignore-scripts', '--no-audit', '--no-fund'], stage);
  writeFileSync(join(stage, 'THIRD_PARTY_NOTICES.txt'), buildThirdPartyNotices(stage));
  if (existsSync(output)) rmSync(output);
  run('npx', ['--yes', `@anthropic-ai/mcpb@${MCPB_VERSION}`, 'pack', stage, output], root);

  const mib = (statSync(output).size / (1024 * 1024)).toFixed(2);
  console.log(`\nWrote ${output} (${mib} MiB) for ${process.platform}-${process.arch}.`);
  console.log('The bundled @napi-rs/keyring binary is for this platform only.');
} finally {
  rmSync(stage, { recursive: true, force: true });
}
