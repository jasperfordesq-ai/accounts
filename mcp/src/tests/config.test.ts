import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, writeFileSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { configuredDirectory, parseArgs } from '../config.js';
import { accountForOrigin, bindCredentialToOrigin, chooseCredentialStore, createFileStore, createMemoryStore, KEYCHAIN_SERVICE } from '../credentials.js';

const env = {};

test('serve is the default; the local profile defaults to the Development frontend at admin level', () => {
  const config = parseArgs(['--profile', 'local'], env);
  assert.equal(config.command, 'serve');
  assert.equal(config.baseUrl, 'http://localhost:3000');
  assert.equal(config.accessLevel, 'admin');
  assert.equal(config.dataScope, 'withheld');
});

test('outside local the default level is write and a base URL is required', () => {
  assert.throws(() => parseArgs(['serve'], env), /--base-url/);
  const config = parseArgs(['--base-url', 'https://accounts.example.ie'], env);
  assert.equal(config.accessLevel, 'write');
  assert.equal(parseArgs([], { FILINGBRIDGE_BASE_URL: 'https://a.example.ie/' }).baseUrl, 'https://a.example.ie');
});

test('profiles pin where a sign-in may go', () => {
  assert.throws(() => parseArgs(['--base-url', 'http://accounts.example.ie'], env), /https/);
  assert.throws(() => parseArgs(['--profile', 'local', '--base-url', 'https://accounts.example.ie'], env), /loopback/);
  assert.throws(() => parseArgs(['--profile', 'local', '--base-url', 'http://127.0.0.1.evil.example:3000'], env), /loopback/);
  assert.throws(() => parseArgs(['--profile', 'private', '--base-url', 'https://accounts.example.ie'], env), /ts\.net/);
  assert.throws(() => parseArgs(['--profile', 'private', '--base-url', 'http://filingbridge.tail1234.ts.net'], env), /loopback/);
  assert.equal(parseArgs(['--profile', 'private', '--base-url', 'http://127.0.0.1:3500'], env).baseUrl, 'http://127.0.0.1:3500');
  assert.equal(parseArgs(['--profile', 'private', '--base-url', 'https://filingbridge.tail1234.ts.net'], env).profile, 'private');
  assert.throws(() => parseArgs(['--base-url', 'https://a.example.ie/api'], env), /no path/);
  assert.throws(() => parseArgs(['--base-url', 'https://user:pw@a.example.ie'], env), /user name/);
  assert.throws(() => parseArgs(['--profile', 'prod'], env), /Unknown profile/);
});

test('connect options are validated', () => {
  const config = parseArgs(['connect', '--profile', 'local', '--tenant-slug', 'demo', '--email', 'o@x.ie', '--access-level', 'read', '--data-scope', 'full', '--totp-code', '123456'], env);
  assert.equal(config.command, 'connect');
  assert.equal(config.tenantSlug, 'demo');
  assert.equal(config.accessLevel, 'read');
  assert.equal(config.dataScope, 'full');
  assert.throws(() => parseArgs(['connect', '--profile', 'local', '--totp-code', '12'], env), /six digits/);
  assert.throws(() => parseArgs(['connect', '--profile', 'local', '--totp-code', '123456', '--recovery-code', 'x'], env), /not both/);
  assert.throws(() => parseArgs(['connect', '--profile', 'local', '--access-level', 'root'], env), /Unknown access level/);
  assert.throws(() => parseArgs(['connect', '--profile', 'local', '--data-scope', 'open'], env), /Unknown data scope/);
});

test('approve takes one identifier', () => {
  assert.equal(parseArgs(['approve', '0f8fad5b-d9cb-469f-a165-70867728950e', '--profile', 'local'], env).approvalId, '0f8fad5b-d9cb-469f-a165-70867728950e');
  assert.throws(() => parseArgs(['approve', 'a;rm -rf', '--profile', 'local'], env), /approval identifier/);
});

test('toolsets, directories and the HTTP flags', () => {
  const config = parseArgs(['--profile', 'local', '--toolsets', 'year_end,filing', '--download-dir', 'out', '--upload-root', 'in'], env);
  assert.deepEqual(config.toolsets, ['year_end', 'filing']);
  assert.equal(config.downloadDir, 'out');
  assert.throws(() => parseArgs(['--profile', 'local', '--toolsets', 'everything'], env), /Unknown toolset/);
  const http = parseArgs(['serve', '--http', '--host', '0.0.0.0', '--port', '3100'], { FILINGBRIDGE_API_URL: 'http://api:8080/' });
  assert.equal(http.http, true);
  assert.equal(http.host, '0.0.0.0');
  assert.equal(http.port, 3100);
  assert.equal(http.baseUrl, 'http://api:8080');
  assert.equal(parseArgs(['serve', '--http'], {}).host, '127.0.0.1');
  assert.throws(() => parseArgs(['serve', '--http', '--download-dir', 'x'], {}), /base64/);
  assert.throws(() => parseArgs(['--profile', 'local', '--port', '3100'], env), /only to serve --http/);
  assert.throws(() => parseArgs(['serve', '--http', '--port', '99999'], {}), /1 to 65535/);
  assert.equal(parseArgs(['--profile', 'local'], { FILINGBRIDGE_DOWNLOAD_DIR: ' d ' }).downloadDir, 'd');
});

test('a blank or unexpanded bundle directory means the feature is off, not a directory', () => {
  // Claude Desktop passes --download-dir ${user_config.download_dir} on every
  // start; a folder left blank arrives as an empty string or as the literal
  // placeholder, depending on the host.
  const bundle = (download: string, upload: string) => parseArgs([
    'serve', '--profile', 'private', '--base-url', 'http://127.0.0.1:3500',
    '--download-dir', download, '--upload-root', upload,
  ], {});
  for (const blank of ['', '   ', '${user_config.download_dir}', ' ${user_config.upload_root} ']) {
    const config = bundle(blank, blank);
    assert.equal(config.downloadDir, undefined, `download dir from ${JSON.stringify(blank)}`);
    assert.equal(config.uploadRoot, undefined, `upload root from ${JSON.stringify(blank)}`);
  }
  const set = bundle('C:\\Users\\me\\FilingBridge downloads', '/home/me/in');
  assert.equal(set.downloadDir, 'C:\\Users\\me\\FilingBridge downloads');
  assert.equal(set.uploadRoot, '/home/me/in');
  // A blank flag leaves the environment to decide, as an absent flag does.
  assert.equal(parseArgs(['--profile', 'local', '--download-dir', ''], { FILINGBRIDGE_DOWNLOAD_DIR: 'd' }).downloadDir, 'd');
  assert.equal(parseArgs(['--profile', 'local'], { FILINGBRIDGE_UPLOAD_ROOT: '${user_config.upload_root}' }).uploadRoot, undefined);
  // A flag with nothing after it is still a mistake.
  assert.throws(() => parseArgs(['--profile', 'local', '--download-dir'], {}), /--download-dir requires a value/);
  // A blank directory is not a reason to refuse serve --http.
  assert.equal(parseArgs(['serve', '--http', '--download-dir', ''], {}).http, true);
  assert.equal(configuredDirectory('${user_config.x}'), undefined);
  assert.equal(configuredDirectory('$HOME/out'), '$HOME/out');
  // Other flags keep refusing an empty value.
  assert.throws(() => parseArgs(['--profile', 'local', '--base-url', ''], {}), /--base-url requires a value/);
});

test('credentials live in the keychain per origin; a file store only on the local profile', () => {
  assert.equal(KEYCHAIN_SERVICE, 'filingbridge-mcp');
  assert.equal(accountForOrigin('HTTPS://A.example.ie'), 'refresh-token:https://a.example.ie');
  let account = '';
  chooseCredentialStore({ profile: 'private', origin: 'http://127.0.0.1:3500', keyring: (a) => { account = a; return createMemoryStore(); } });
  assert.equal(account, 'refresh-token:http://127.0.0.1:3500');
  assert.throws(() => chooseCredentialStore({ profile: 'private', origin: 'x', credentialFile: 'c.json' }), /--profile local/);
  const dir = mkdtempSync(join(tmpdir(), 'fb-cred-'));
  const store = chooseCredentialStore({ profile: 'local', origin: 'http://localhost:3000', credentialFile: join(dir, 'c.json') });
  store.write('fbr1.1.token-aaaaaaaa');
  assert.equal(store.read(), 'fbr1.1.token-aaaaaaaa');
  store.clear();
  assert.equal(existsSync(join(dir, 'c.json')), false);
});

test('a bound credential refuses another origin and an unbound entry', () => {
  const raw = createMemoryStore();
  bindCredentialToOrigin(raw, 'http://127.0.0.1:3500').write('fbr1.1.t-aaaaaaaa');
  assert.throws(() => bindCredentialToOrigin(raw, 'http://localhost:3500').read(), /will not be sent/);
  assert.throws(() => bindCredentialToOrigin(createMemoryStore('plain-token'), 'http://127.0.0.1:3500').read(), /disconnect/);
  const dir = mkdtempSync(join(tmpdir(), 'fb-cred-'));
  writeFileSync(join(dir, 'bad.json'), 'not json');
  assert.equal(createFileStore(join(dir, 'bad.json')).read(), null);
});
