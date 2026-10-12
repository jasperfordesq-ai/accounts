import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  METADATA_TTL_MS,
  defaultAllowedHosts,
  httpConfigFromEnv,
  localProtectedResourceMetadata,
  resourceMetadataUrl,
  resourceUrl,
} from '../http-server.js';
import { json } from './helpers.js';
import { PUBLIC_URL, SERVICE_KEY, defaultApi, raw, startService } from './http-helpers.js';

const API_DOCUMENT = {
  resource: `${PUBLIC_URL}/mcp`,
  authorization_servers: [PUBLIC_URL],
  scopes_supported: ['fb:read', 'fb:write', 'fb:admin', 'fb:data-full'],
  bearer_methods_supported: ['header'],
  resource_name: 'FilingBridge',
  from: 'api',
};

test('both protected-resource paths serve the API\'s document, fetched with the service key and cached', async () => {
  let clock = 5_000_000;
  const service = await startService((request) => {
    if (request.url.endsWith('/api/oauth/metadata/protected-resource')) return json(200, API_DOCUMENT);
    return defaultApi(request, 0);
  }, { now: () => clock });
  try {
    const headers = { host: `127.0.0.1:${service.port}` };
    for (const path of ['/.well-known/oauth-protected-resource', '/.well-known/oauth-protected-resource/mcp']) {
      const response = await raw(service.port, 'GET', path, headers);
      assert.equal(response.status, 200, path);
      assert.deepEqual(response.json(), API_DOCUMENT);
    }
    assert.equal(service.calls.length, 1, 'fetched once for both paths');
    const call = service.calls[0]!;
    assert.equal(call.url, 'http://api.test:8080/api/oauth/metadata/protected-resource');
    assert.equal(call.headers['x-accounts-api-key'], SERVICE_KEY);
    assert.equal(call.headers['authorization'], undefined, 'no bearer goes with the metadata request');

    clock += METADATA_TTL_MS + 1;
    await raw(service.port, 'GET', '/.well-known/oauth-protected-resource/mcp', headers);
    assert.equal(service.calls.length, 2, 'fetched again after five minutes');
  } finally {
    await service.close();
  }
});

test('without the API document the service builds one from the public URL', async () => {
  const service = await startService();
  try {
    const response = await raw(service.port, 'GET', '/.well-known/oauth-protected-resource/mcp', { host: `127.0.0.1:${service.port}` });
    assert.equal(response.status, 200);
    const document = response.json() as Record<string, unknown>;
    assert.equal(document['resource'], 'https://fb.example/mcp');
    assert.deepEqual(document['authorization_servers'], ['https://fb.example']);
    assert.deepEqual(document['bearer_methods_supported'], ['header']);
    assert.ok((document['scopes_supported'] as string[]).includes('fb:data-full'));
    assert.equal(document['resource_name'], 'FilingBridge');
  } finally {
    await service.close();
  }
});

test('the resource and the challenge URL are derived from the issuer', () => {
  assert.equal(resourceUrl('https://fb.example'), 'https://fb.example/mcp');
  assert.equal(resourceMetadataUrl('https://fb.example'), 'https://fb.example/.well-known/oauth-protected-resource/mcp');
  assert.deepEqual(localProtectedResourceMetadata('http://127.0.0.1:3500')['authorization_servers'], ['http://127.0.0.1:3500']);
});

test('the HTTP configuration comes from the environment and refuses unsafe values', () => {
  const read = () => `${SERVICE_KEY}\n`;
  const listen = { host: '0.0.0.0', port: 3100 };
  const config = httpConfigFromEnv({
    FILINGBRIDGE_API_URL: 'http://api:8080/',
    FILINGBRIDGE_API_KEY_FILE: '/run/secrets/accounts_mcp_api_key',
    FILINGBRIDGE_PUBLIC_URL: 'https://fb.example/',
  }, listen, read);
  assert.equal(config.apiUrl, 'http://api:8080');
  assert.equal(config.publicUrl, 'https://fb.example');
  assert.equal(config.apiKey, SERVICE_KEY, 'read once and trimmed');
  assert.deepEqual(config.allowedHosts, defaultAllowedHosts('https://fb.example', 3100));
  assert.ok(config.allowedHosts.includes('fb.example') && config.allowedHosts.includes('mcp:3100') && config.allowedHosts.includes('localhost'));

  const loopback = httpConfigFromEnv({ FILINGBRIDGE_API_KEY_FILE: 'k', FILINGBRIDGE_PUBLIC_URL: 'http://127.0.0.1:3500' }, listen, read);
  assert.ok(loopback.allowedHosts.includes('127.0.0.1:3500'));
  assert.equal(loopback.apiUrl, 'http://api:8080', 'the in-stack API by default');

  const explicit = httpConfigFromEnv({
    FILINGBRIDGE_API_KEY_FILE: 'k', FILINGBRIDGE_PUBLIC_URL: 'https://fb.example', FILINGBRIDGE_ALLOWED_HOSTS: 'A.example, mcp:3100',
  }, listen, read);
  assert.deepEqual(explicit.allowedHosts, ['a.example', 'mcp:3100']);

  assert.throws(() => httpConfigFromEnv({ FILINGBRIDGE_API_KEY_FILE: 'k' }, listen, read), /FILINGBRIDGE_PUBLIC_URL/);
  assert.throws(() => httpConfigFromEnv({ FILINGBRIDGE_API_KEY_FILE: 'k', FILINGBRIDGE_PUBLIC_URL: 'http://fb.example' }, listen, read), /https/);
  assert.throws(() => httpConfigFromEnv({ FILINGBRIDGE_API_KEY_FILE: 'k', FILINGBRIDGE_PUBLIC_URL: 'https://fb.example/app' }, listen, read), /origin only/);
  assert.throws(() => httpConfigFromEnv({ FILINGBRIDGE_PUBLIC_URL: 'https://fb.example' }, listen, read), /FILINGBRIDGE_API_KEY_FILE/);
  assert.throws(() => httpConfigFromEnv({ FILINGBRIDGE_API_KEY_FILE: 'k', FILINGBRIDGE_PUBLIC_URL: 'https://fb.example' }, listen, () => 'short'), /at least 16/);
  assert.throws(
    () => httpConfigFromEnv({ FILINGBRIDGE_API_KEY_FILE: 'k', FILINGBRIDGE_PUBLIC_URL: 'https://fb.example' }, listen, () => { throw Object.assign(new Error('nope'), { code: 'ENOENT' }); }),
    (error: Error) => /ENOENT/.test(error.message) && !error.message.includes(SERVICE_KEY),
  );
});
