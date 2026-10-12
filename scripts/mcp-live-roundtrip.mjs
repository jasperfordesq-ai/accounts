#!/usr/bin/env node
/**
 * Live round trip against a running stack's remote MCP connector.
 *
 * Proves, through the public HTTPS origin (ingress -> frontend /mcp and
 * /.well-known routes -> mcp service and API), that a remote MCP client such
 * as Claude.ai can discover how to authenticate:
 *
 *   1. /mcp without a bearer answers 401 with
 *      WWW-Authenticate: Bearer resource_metadata="<origin>/.well-known/oauth-protected-resource/mcp"
 *   2. a bearer FilingBridge does not recognise answers 401 error="invalid_token"
 *   3. the protected-resource metadata names <origin>/mcp and the origin as its
 *      authorization server
 *   4. the authorization-server metadata (RFC 8414) advertises the endpoints,
 *      PKCE S256 and public clients only
 *
 * None of this needs a password or an authenticator code. A full OAuth round
 * trip (dynamic client registration -> /api/oauth/authorize with the Owner's
 * password and a TOTP code -> /api/oauth/token with the PKCE verifier ->
 * initialize + tools/list + session_info over /mcp with the access token)
 * belongs after check 4, driven by the disposable MFA handoff that
 * smoke-production.ps1 writes (-EphemeralMfaHandoffPath); it is not run here
 * yet because that handoff is consumed by the visual smoke.
 *
 *   node scripts/mcp-live-roundtrip.mjs --base-url=https://accounts-smoke.local [--report-path=file.json]
 */
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';

function argument(name) {
  const prefix = `--${name}=`;
  const found = process.argv.slice(2).find((value) => value.startsWith(prefix));
  return found === undefined ? undefined : found.slice(prefix.length);
}

const baseArgument = argument('base-url');
if (!baseArgument) {
  console.error('Usage: node scripts/mcp-live-roundtrip.mjs --base-url=https://<origin> [--report-path=<file>]');
  process.exit(2);
}
const base = new URL(baseArgument).origin;
const reportPath = argument('report-path');
const resource = `${base}/mcp`;
const resourceMetadataUrl = `${base}/.well-known/oauth-protected-resource/mcp`;

const checks = [];

async function check(name, run) {
  const started = Date.now();
  try {
    const detail = await run();
    checks.push({ name, passed: true, ms: Date.now() - started, detail });
    console.log(`PASS ${name}`);
  } catch (error) {
    checks.push({ name, passed: false, ms: Date.now() - started, detail: error.message });
    console.error(`FAIL ${name}: ${error.message}`);
  }
}

function expect(condition, message) {
  if (!condition) throw new Error(message);
}

async function request(path, init = {}) {
  return fetch(`${base}${path}`, { redirect: 'manual', signal: AbortSignal.timeout(20_000), ...init });
}

async function json(response) {
  const text = await response.text();
  try {
    return JSON.parse(text);
  } catch {
    throw new Error(`expected JSON, got ${response.status} ${response.headers.get('content-type')}: ${text.slice(0, 200)}`);
  }
}

const initialize = JSON.stringify({
  jsonrpc: '2.0',
  id: 1,
  method: 'initialize',
  params: { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'filingbridge-ci-roundtrip', version: '1.0.0' } },
});
const mcpHeaders = { accept: 'application/json, text/event-stream', 'content-type': 'application/json' };

await check('mcp without a bearer is 401 with the protected-resource challenge', async () => {
  for (const [method, body] of [['GET', undefined], ['POST', initialize]]) {
    const response = await request('/mcp', { method, headers: mcpHeaders, body });
    expect(response.status === 401, `${method} /mcp answered ${response.status}, expected 401`);
    const challenge = response.headers.get('www-authenticate') ?? '';
    expect(/^Bearer\b/i.test(challenge), `${method} /mcp WWW-Authenticate is not a Bearer challenge: "${challenge}"`);
    expect(challenge.includes(`resource_metadata="${resourceMetadataUrl}"`),
      `${method} /mcp WWW-Authenticate does not point at ${resourceMetadataUrl}: "${challenge}"`);
  }
  return { resourceMetadata: resourceMetadataUrl };
});

await check('mcp with an unknown bearer is 401 invalid_token', async () => {
  const response = await request('/mcp', {
    method: 'POST',
    headers: { ...mcpHeaders, authorization: 'Bearer fbc1.ci-roundtrip.not-a-session' },
    body: initialize,
  });
  expect(response.status === 401, `answered ${response.status}, expected 401`);
  const challenge = response.headers.get('www-authenticate') ?? '';
  expect(challenge.includes('error="invalid_token"'), `WWW-Authenticate lacks error="invalid_token": "${challenge}"`);
  expect(challenge.includes(`resource_metadata="${resourceMetadataUrl}"`), `WWW-Authenticate lacks resource_metadata: "${challenge}"`);
  return { status: response.status };
});

await check('protected-resource metadata names the resource and its authorization server', async () => {
  const documents = [];
  for (const path of ['/.well-known/oauth-protected-resource/mcp', '/.well-known/oauth-protected-resource']) {
    const response = await request(path, { headers: { accept: 'application/json' } });
    expect(response.status === 200, `${path} answered ${response.status}`);
    const document = await json(response);
    expect(document.resource === resource, `${path} resource is ${JSON.stringify(document.resource)}, expected ${resource}`);
    expect(Array.isArray(document.authorization_servers) && document.authorization_servers.includes(base),
      `${path} authorization_servers ${JSON.stringify(document.authorization_servers)} does not include ${base}`);
    documents.push(document);
  }
  return { resource: documents[0].resource, authorizationServers: documents[0].authorization_servers, scopes: documents[0].scopes_supported ?? null };
});

await check('authorization-server metadata advertises PKCE S256 and public clients', async () => {
  const response = await request('/.well-known/oauth-authorization-server', { headers: { accept: 'application/json' } });
  expect(response.status === 200, `answered ${response.status}`);
  const document = await json(response);
  expect(document.issuer === base, `issuer is ${JSON.stringify(document.issuer)}, expected ${base}`);
  for (const [field, expected] of [
    ['authorization_endpoint', `${base}/oauth/authorize`],
    ['token_endpoint', `${base}/api/oauth/token`],
    ['registration_endpoint', `${base}/api/oauth/register`],
  ]) {
    expect(document[field] === expected, `${field} is ${JSON.stringify(document[field])}, expected ${expected}`);
  }
  expect(JSON.stringify(document.code_challenge_methods_supported) === '["S256"]', 'code_challenge_methods_supported must be exactly ["S256"]');
  expect(JSON.stringify(document.token_endpoint_auth_methods_supported) === '["none"]', 'token_endpoint_auth_methods_supported must be exactly ["none"]');
  return { issuer: document.issuer, scopes: document.scopes_supported ?? null };
});

// A full OAuth round trip (register -> authorize with password + TOTP -> token
// -> initialize/tools/list over /mcp) goes here; see the header comment.

const passed = checks.every((entry) => entry.passed);
if (reportPath) {
  mkdirSync(dirname(reportPath), { recursive: true });
  writeFileSync(reportPath, `${JSON.stringify({
    status: passed ? 'passed' : 'failed',
    checkedAtUtc: new Date().toISOString(),
    baseUrl: base,
    resource,
    resourceMetadataUrl,
    fullOAuthRoundTrip: 'not-run',
    checks,
  }, null, 2)}\n`);
}
console.log(passed ? `MCP live round trip passed (${checks.length} checks).` : 'MCP live round trip FAILED.');
process.exit(passed ? 0 : 1);
