#!/usr/bin/env node
// Writes the tools section of manifest.json from the built catalogue.
// Run through `npm run manifest:tools`, which compiles first.
// `--check` reports drift and exits 1 instead of writing.
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const manifestPath = resolve(root, 'manifest.json');
const { manifestTools } = await import(new URL('../dist/manifest-tools.js', import.meta.url).href);

const manifest = JSON.parse(readFileSync(manifestPath, 'utf8'));
const tools = manifestTools();
const current = JSON.stringify(manifest.tools ?? null);

if (process.argv.includes('--check')) {
  if (current !== JSON.stringify(tools)) {
    console.error('manifest.json tools are stale: run npm run manifest:tools --prefix mcp');
    process.exit(1);
  }
  console.log(`manifest.json lists all ${tools.length} tools.`);
} else {
  manifest.tools = tools;
  writeFileSync(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`);
  console.log(`manifest.json now lists ${tools.length} tools.`);
}
