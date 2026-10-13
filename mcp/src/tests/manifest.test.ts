import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { CREATOR_NAME } from '../attribution.js';
import { TOOLS } from '../catalogue.js';
import { PROFILE_NAMES, parseArgs } from '../config.js';
import { firstSentence, manifestTools } from '../manifest-tools.js';
import { CORE_TOOL_NAMES } from '../server.js';
import { CONNECTOR_VERSION } from '../version.js';
import { PACKAGE_ROOT, REPO_ROOT } from './helpers.js';

const read = (path: string) => readFileSync(path, 'utf8');
const manifest = JSON.parse(read(resolve(PACKAGE_ROOT, 'manifest.json'))) as Record<string, any>;
const pkg = JSON.parse(read(resolve(PACKAGE_ROOT, 'package.json'))) as Record<string, any>;

const ARGS = [
  '${__dirname}/dist/cli.js', 'serve',
  '--profile', '${user_config.profile}',
  '--base-url', '${user_config.base_url}',
  '--download-dir', '${user_config.download_dir}',
  '--upload-root', '${user_config.upload_root}',
];

/** What the host does to the args: substitute what is set, leave the rest as written. */
function substitute(values: Record<string, string>): string[] {
  return ARGS.slice(1).map((arg) => arg.replace(/\$\{user_config\.([a-z_]+)\}/, (whole, key: string) => values[key] ?? whole));
}

test('the bundle is the FilingBridge connector at the package version, under the package licence', () => {
  assert.equal(manifest['manifest_version'], '0.2');
  assert.equal(manifest['name'], 'filingbridge');
  assert.equal(manifest['display_name'], 'FilingBridge');
  assert.equal(manifest['version'], pkg['version']);
  assert.equal(manifest['version'], CONNECTOR_VERSION);
  assert.equal(manifest['license'], pkg['license']);
  assert.equal(manifest['license'], 'AGPL-3.0-or-later');
  assert.deepEqual(manifest['author'], { name: CREATOR_NAME });
  assert.equal(manifest['repository'].url, pkg['repository'].url);
  assert.equal(manifest['homepage'], pkg['homepage']);
  assert.equal(manifest['icon'], undefined, 'no icon is shipped until a PNG exists');
});

test('the bundle serves the built CLI over stdio with the profile and address the person chose', () => {
  const server = manifest['server'];
  assert.equal(server.type, 'node');
  assert.equal(server.entry_point, 'dist/cli.js');
  assert.equal(`./${server.entry_point}`, pkg['bin']['filingbridge-mcp']);
  assert.equal(server.mcp_config.command, 'node');
  assert.deepEqual(server.mcp_config.args, ARGS);
  assert.equal(server.mcp_config.env, undefined);
});

test('every flag the bundle passes is one the CLI accepts, set or left blank', () => {
  const all = parseArgs(substitute({
    profile: 'private', base_url: 'http://127.0.0.1:3500', download_dir: '/tmp/fb-out', upload_root: '/tmp/fb-in',
  }), {});
  assert.equal(all.command, 'serve');
  assert.equal(all.http, false);
  assert.equal(all.profile, 'private');
  assert.equal(all.baseUrl, 'http://127.0.0.1:3500');
  assert.equal(all.downloadDir, '/tmp/fb-out');
  assert.equal(all.uploadRoot, '/tmp/fb-in');

  // Optional folders left blank: the placeholder stays as written, or becomes empty.
  for (const blank of [{}, { download_dir: '', upload_root: '' }]) {
    const config = parseArgs(substitute({ profile: 'default', base_url: 'https://fb.example.ie', ...blank }), {});
    assert.equal(config.downloadDir, undefined);
    assert.equal(config.uploadRoot, undefined);
    assert.equal(config.baseUrl, 'https://fb.example.ie');
  }

  // The profile is still a pin when it comes from a dialog.
  assert.throws(() => parseArgs(substitute({ profile: 'private', base_url: 'https://evil.example' }), {}), /only reaches the Private Server's loopback address/);
  assert.throws(() => parseArgs(substitute({ profile: 'anything', base_url: 'https://fb.example.ie' }), {}), /Unknown profile/);
});

test('the bundle asks for the address and profile, and never assumes access to a folder', () => {
  const config = manifest['user_config'] as Record<string, { type: string; required?: boolean; default?: unknown; description: string; sensitive?: boolean }>;
  assert.deepEqual(Object.keys(config).sort(), ['base_url', 'download_dir', 'profile', 'upload_root']);
  assert.equal(config['base_url']!.type, 'string');
  assert.equal(config['base_url']!.required, true);
  assert.equal(config['profile']!.type, 'string');
  assert.equal(config['profile']!.default, 'private');
  assert.ok(PROFILE_NAMES.includes(config['profile']!.default as 'private'));
  for (const name of PROFILE_NAMES) assert.match(config['profile']!.description, new RegExp(`\\b${name}\\b`));
  for (const field of ['download_dir', 'upload_root']) {
    assert.equal(config[field]!.type, 'directory');
    assert.notEqual(config[field]!.required, true);
    assert.match(config[field]!.description, /Leave this empty/);
  }
  for (const [key, option] of Object.entries(config)) {
    assert.notEqual(option.sensitive, true, `${key}: the bundle holds no secret`);
  }
});

test('signing in stays a terminal step the bundle describes, never a password field', () => {
  const text = manifest['long_description'] as string;
  assert.match(text, /never asks for your password/);
  assert.match(text, / connect --profile /);
  assert.ok(text.includes('CRO and ROS final submission remain external actions recorded in workflow state.'));
  assert.ok(!JSON.stringify(manifest['user_config']).toLowerCase().includes('password'));
});

test('the bundle runs on the Node and platforms the package supports', () => {
  assert.equal(manifest['compatibility'].runtimes.node, pkg['engines'].node);
  assert.deepEqual([...manifest['compatibility'].platforms].sort(), ['darwin', 'linux', 'win32']);
});

test('the manifest lists every tool in the catalogue (npm run manifest:tools)', () => {
  assert.deepEqual(manifest['tools'], manifestTools(), 'manifest.json tools are stale: run npm run manifest:tools --prefix mcp');
  const names = (manifest['tools'] as { name: string }[]).map((tool) => tool.name);
  assert.equal(new Set(names).size, names.length);
  assert.equal(names.length, CORE_TOOL_NAMES.size + TOOLS.length);
  for (const core of CORE_TOOL_NAMES) assert.ok(names.includes(core), core);
  assert.equal(manifest['tools_generated'], undefined);
});

test('a tool summary is the first sentence of its description', () => {
  assert.equal(firstSentence('Change a debtor. Replaces the whole record.'), 'Change a debtor.');
  assert.equal(firstSentence('Reads s.280D statements. Then more.'), 'Reads s.280D statements.');
  assert.equal(firstSentence('One line\n  wrapped. (Owner only.)'), 'One line wrapped.');
  assert.equal(firstSentence('No full stop'), 'No full stop');
});

test('the packer is pinned once, and CI validates with the same version', () => {
  const script = read(resolve(PACKAGE_ROOT, 'scripts/pack-mcpb.mjs'));
  const pinned = /export const MCPB_VERSION = '(\d+\.\d+\.\d+)';/.exec(script)?.[1];
  assert.ok(pinned, 'scripts/pack-mcpb.mjs pins an exact @anthropic-ai/mcpb version');
  assert.equal(pkg['scripts']['pack:mcpb'], 'node scripts/pack-mcpb.mjs');
  assert.equal(pkg['scripts']['manifest:tools'], 'tsc -p tsconfig.json && node scripts/manifest-tools.mjs');
  const ci = read(resolve(REPO_ROOT, '.github/workflows/ci.yml'));
  assert.ok(ci.includes(`npx --yes @anthropic-ai/mcpb@${pinned} validate manifest.json`), 'CI validates the manifest with the pinned packer');
  // Not a dependency: its own dependency tree fails npm audit, and the
  // connector never needs it at run time.
  assert.equal({ ...pkg['dependencies'], ...pkg['devDependencies'] }['@anthropic-ai/mcpb'], undefined);
  assert.match(script, /'dist', 'tests'/, 'the bundle carries no tests');
  assert.match(script, /--omit=dev/, 'the bundle carries production dependencies only');
});

test('a built bundle is never committed', () => {
  assert.match(read(resolve(PACKAGE_ROOT, '.gitignore')), /^filingbridge\.mcpb$/m);
  assert.ok((pkg['files'] as string[]).includes('manifest.json'));
});
