import { test } from 'node:test';
import assert from 'node:assert/strict';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { StreamableHTTPClientTransport } from '@modelcontextprotocol/sdk/client/streamableHttp.js';
import type { Transport } from '@modelcontextprotocol/sdk/shared/transport.js';
import { MAX_BODY_BYTES, POSTURE_TTL_MS } from '../http-server.js';
import { CLIENT_HEADER_VALUE } from '../version.js';
import { json } from './helpers.js';
import {
  GOOD_TOKEN,
  MCP_ACCEPT,
  PUBLIC_URL,
  REVOKED_TOKEN,
  SERVICE_KEY,
  defaultApi,
  raw,
  rpc,
  startService,
} from './http-helpers.js';

const METADATA_URL = `${PUBLIC_URL}/.well-known/oauth-protected-resource/mcp`;

function host(port: number): Record<string, string> {
  return { host: `127.0.0.1:${port}` };
}

test('no bearer: 401 with the protected-resource challenge, and nothing sent to the API', async () => {
  const service = await startService();
  try {
    for (const method of ['POST', 'GET', 'DELETE']) {
      const response = await raw(service.port, method, '/mcp', { ...host(service.port), accept: MCP_ACCEPT, 'content-type': 'application/json' }, method === 'POST' ? rpc('tools/list') : undefined);
      assert.equal(response.status, 401, method);
      assert.equal(response.headers['www-authenticate'], `Bearer resource_metadata="${METADATA_URL}"`);
    }
    const basic = await raw(service.port, 'POST', '/mcp', { ...host(service.port), authorization: 'Basic abc' }, rpc('tools/list'));
    assert.equal(basic.status, 401);
    assert.equal(service.calls.length, 0);
  } finally {
    await service.close();
  }
});

test('a bearer that is not a connector token is invalid_token without asking the API', async () => {
  const service = await startService();
  try {
    const response = await raw(service.port, 'POST', '/mcp', { ...host(service.port), authorization: 'Bearer eyJhbGciOi.not-ours' }, rpc('tools/list'));
    assert.equal(response.status, 401);
    assert.equal(response.headers['www-authenticate'], `Bearer error="invalid_token", resource_metadata="${METADATA_URL}"`);
    assert.equal(service.calls.length, 0);
  } finally {
    await service.close();
  }
});

test('a bearer the API refuses is answered 401 invalid_token', async () => {
  const service = await startService();
  try {
    const response = await raw(service.port, 'POST', '/mcp', {
      ...host(service.port), authorization: `Bearer ${REVOKED_TOKEN}`, accept: MCP_ACCEPT, 'content-type': 'application/json',
    }, rpc('tools/list'));
    assert.equal(response.status, 401);
    assert.equal(response.headers['www-authenticate'], `Bearer error="invalid_token", resource_metadata="${METADATA_URL}"`);
    assert.equal((response.json() as { error: string }).error, 'invalid_token');
    assert.equal(service.calls.length, 1);
  } finally {
    await service.close();
  }
});

test('the bearer is forwarded verbatim with the service key, and no browser header is forwarded', async () => {
  const service = await startService();
  try {
    const response = await raw(service.port, 'POST', '/mcp', {
      ...host(service.port),
      authorization: `Bearer ${GOOD_TOKEN}`,
      accept: MCP_ACCEPT,
      'content-type': 'application/json',
      origin: 'https://evil.example',
      referer: 'https://evil.example/page',
      cookie: 'accounts_session=stolen; accounts_csrf=x',
      'sec-fetch-site': 'cross-site',
      'sec-fetch-mode': 'cors',
      'x-accounts-api-key': 'caller-supplied-key',
    }, rpc('tools/call', { name: 'company_list', arguments: {} }));
    assert.equal(response.status, 200, response.text);
    const body = response.json() as { result: { structuredContent: { data: { legalName: string }[] } } };
    assert.equal(body.result.structuredContent.data[0]!.legalName, 'Green Valley CLG');

    assert.ok(service.calls.length >= 2, 'the posture, then the tool');
    for (const call of service.calls) {
      assert.ok(call.url.startsWith('http://api.test:8080/api/'), call.url);
      assert.equal(call.headers['authorization'], `Bearer ${GOOD_TOKEN}`);
      assert.equal(call.headers['x-accounts-api-key'], SERVICE_KEY, 'the service key, never the caller\'s');
      assert.equal(call.headers['x-filingbridge-client'], CLIENT_HEADER_VALUE);
      for (const name of ['origin', 'referer', 'cookie', 'sec-fetch-site', 'sec-fetch-mode']) {
        assert.equal(call.headers[name], undefined, `${name} must not be forwarded`);
      }
    }
    for (const line of service.logs) {
      assert.doesNotMatch(line, /good-access-token|svc-key/, 'neither the bearer nor the service key is logged');
    }
    const logged = JSON.parse(service.logs.at(-1)!) as { event: string; status: number; rpc: string[] };
    assert.equal(logged.event, 'http_request');
    assert.equal(logged.status, 200);
    assert.deepEqual(logged.rpc, ['tools/call:company_list']);
  } finally {
    await service.close();
  }
});

test('/healthz is open, whatever the Host; other routes refuse a Host not allowed', async () => {
  const service = await startService();
  try {
    const health = await raw(service.port, 'GET', '/healthz', { host: 'anything.example' });
    assert.equal(health.status, 200);
    assert.deepEqual(health.json(), { ok: true });

    const rebound = await raw(service.port, 'POST', '/mcp', {
      host: 'attacker.example', authorization: `Bearer ${GOOD_TOKEN}`, accept: MCP_ACCEPT, 'content-type': 'application/json',
    }, rpc('tools/list'));
    assert.equal(rebound.status, 403);
    const metadata = await raw(service.port, 'GET', '/.well-known/oauth-protected-resource/mcp', { host: 'attacker.example' });
    assert.equal(metadata.status, 403);
    assert.equal(service.calls.length, 0);

    const missing = await raw(service.port, 'GET', '/nothing-here', host(service.port));
    assert.equal(missing.status, 404);
  } finally {
    await service.close();
  }
});

test('a body over the cap is answered 413, declared or streamed', async () => {
  assert.equal(MAX_BODY_BYTES, 5 * 1024 * 1024);
  const service = await startService(defaultApi, { maxBodyBytes: 1024 });
  try {
    const headers = { ...host(service.port), authorization: `Bearer ${GOOD_TOKEN}`, accept: MCP_ACCEPT, 'content-type': 'application/json' };
    const big = rpc('tools/call', { name: 'company_list', arguments: { padding: 'x'.repeat(2000) } });
    const declared = await raw(service.port, 'POST', '/mcp', headers, big);
    assert.equal(declared.status, 413);
    const streamed = await raw(service.port, 'POST', '/mcp', { ...headers, 'transfer-encoding': 'chunked' }, big);
    assert.equal(streamed.status, 413);
    const notJson = await raw(service.port, 'POST', '/mcp', headers, '{not json');
    assert.equal(notJson.status, 400);
  } finally {
    await service.close();
  }
});

test('stateless: GET and DELETE with a valid bearer are 405', async () => {
  const service = await startService();
  try {
    for (const method of ['GET', 'DELETE']) {
      const response = await raw(service.port, method, '/mcp', { ...host(service.port), authorization: `Bearer ${GOOD_TOKEN}`, accept: 'text/event-stream' });
      assert.equal(response.status, 405, method);
    }
    const put = await raw(service.port, 'PUT', '/mcp', host(service.port));
    assert.equal(put.status, 405);
  } finally {
    await service.close();
  }
});

test('an MCP client initializes, lists the tools in HTTP mode and calls one', async () => {
  const service = await startService();
  try {
    const client = new Client({ name: 'http-test', version: '1.0.0' });
    const transport = new StreamableHTTPClientTransport(new URL(`${service.base}/mcp`), {
      requestInit: { headers: { authorization: `Bearer ${GOOD_TOKEN}` } },
    });
    await client.connect(transport as Transport);
    assert.equal(client.getServerVersion()?.name, 'filingbridge');
    assert.match(client.getInstructions() ?? '', /external actions/);

    const tools = (await client.listTools()).tools;
    const names = tools.map((t) => t.name);
    assert.ok(names.includes('company_list') && names.includes('session_info'));
    const importTool = tools.find((t) => t.name === 'bank_statement_import');
    assert.ok(importTool, 'the CSV import is offered over HTTP without an upload directory');
    const properties = importTool.inputSchema.properties as Record<string, unknown>;
    assert.ok(properties['contentBase64'] && !properties['file'], 'the HTTP schema takes contentBase64, not a path');
    assert.doesNotMatch(importTool.description ?? '', /--upload-root/);

    const result = await client.callTool({ name: 'company_list', arguments: {} });
    assert.notEqual(result.isError, true);
    const info = await client.callTool({ name: 'session_info', arguments: {} });
    const connector = (info.structuredContent as { connector: { baseUrl: string; downloads: string } }).connector;
    assert.equal(connector.baseUrl, PUBLIC_URL, 'links and session_info name the public origin, not the internal API');
    assert.match(connector.downloads, /inline/);
    await client.close();
  } finally {
    await service.close();
  }
});

test('the posture is asked once per bearer within the TTL, and forgotten when the API refuses the bearer', async () => {
  assert.ok(POSTURE_TTL_MS <= 60_000);
  let clock = 1_000_000;
  let revoked = false;
  const service = await startService((request) => {
    if (revoked) return json(401, { code: 'CONNECTOR_SESSION_INVALID' });
    return defaultApi(request, 0);
  }, { now: () => clock });
  try {
    const headers = { ...host(service.port), authorization: `Bearer ${GOOD_TOKEN}`, accept: MCP_ACCEPT, 'content-type': 'application/json' };
    const sessionCalls = () => service.calls.filter((c) => c.url.endsWith('/api/auth/connector/session')).length;
    assert.equal((await raw(service.port, 'POST', '/mcp', headers, rpc('tools/list'))).status, 200);
    assert.equal((await raw(service.port, 'POST', '/mcp', headers, rpc('tools/list'))).status, 200);
    assert.equal(sessionCalls(), 1, 'cached');

    clock += POSTURE_TTL_MS + 1;
    assert.equal((await raw(service.port, 'POST', '/mcp', headers, rpc('tools/list'))).status, 200);
    assert.equal(sessionCalls(), 2, 'asked again after the TTL');

    revoked = true;
    const call = await raw(service.port, 'POST', '/mcp', headers, rpc('tools/call', { name: 'company_list', arguments: {} }));
    assert.equal(call.status, 200, 'the tool call itself reports the refusal as a tool error');
    assert.equal((call.json() as { result: { isError: boolean } }).result.isError, true);
    const next = await raw(service.port, 'POST', '/mcp', headers, rpc('tools/list'));
    assert.equal(next.status, 401, 'and the next request is told to re-authenticate');
    assert.match(String(next.headers['www-authenticate']), /invalid_token/);
  } finally {
    await service.close();
  }
});
