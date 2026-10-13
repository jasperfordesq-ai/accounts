import { test } from 'node:test';
import assert from 'node:assert/strict';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { InMemoryTransport } from '@modelcontextprotocol/sdk/inMemory.js';
import { createServer, type ServerOptions } from '../server.js';
import { CLOSED_POSTURE, type SessionPosture } from '../session-level.js';
import { INLINE_DOWNLOAD_MAX_BYTES, INLINE_UPLOAD_MAX_BASE64_CHARS } from '../files.js';
import { FakeTokens, json, stubFetch, type Responder } from './helpers.js';

const FULL_ADMIN: SessionPosture = { ...CLOSED_POSTURE, accessLevel: 'admin', dataScope: 'full', role: 'Owner', tenantSlug: 'demo' };

async function connect(responder: Responder, options: Partial<ServerOptions> = {}) {
  const stub = stubFetch(responder);
  const server = createServer({
    baseUrl: 'http://api.test:8080',
    publicUrl: 'https://fb.example',
    tokens: new FakeTokens(['fbc1.http-token-aaaaaaaa'], false),
    fetchImpl: stub.fetch,
    posture: async () => FULL_ADMIN,
    fileMode: 'inline',
    ...options,
  });
  const client = new Client({ name: 'test', version: '1.0.0' });
  const [clientSide, serverSide] = InMemoryTransport.createLinkedPair();
  await Promise.all([server.connect(serverSide), client.connect(clientSide)]);
  return { client, calls: stub.calls };
}

function pdf(bytes: number): Response {
  return new Response(new Uint8Array(Buffer.alloc(bytes, 0x25)), {
    status: 200,
    headers: { 'content-type': 'application/pdf', 'content-disposition': 'attachment; filename="accounts-package.pdf"' },
  });
}

test('inline mode offers downloads and uploads without directories, with base64 schemas and no flag names', async () => {
  const { client } = await connect(() => json(200, {}));
  const tools = (await client.listTools()).tools;
  const byName = new Map(tools.map((t) => [t.name, t]));
  assert.ok(byName.has('accounts_package_download'));
  const download = byName.get('accounts_package_download')!;
  assert.doesNotMatch(download.description ?? '', /--download-dir/);
  assert.match(download.description ?? '', /base64/);

  const csv = byName.get('bank_statement_import')!;
  const csvProps = csv.inputSchema.properties as Record<string, { maxLength?: number }>;
  assert.equal(csvProps['contentBase64']?.maxLength, INLINE_UPLOAD_MAX_BASE64_CHARS);
  assert.equal(csvProps['file'], undefined);
  assert.ok((csv.inputSchema.required ?? []).includes('contentBase64'));

  for (const tool of tools) {
    assert.doesNotMatch(tool.description ?? '', /--upload-root|--download-dir/, tool.name);
    assert.doesNotMatch(JSON.stringify(tool.inputSchema), /--upload-root/, tool.name);
  }
  assert.ok(!byName.has('privacy_access_export'), 'a subject access export never enters the conversation, so it is stdio only');
});

test('a download up to 1 MiB comes back as an embedded resource', async () => {
  const { client, calls } = await connect((request) => (request.url.includes('/documents/accounts-package') ? pdf(2048) : json(404, {})));
  const result = await client.callTool({ name: 'accounts_package_download', arguments: { companyId: 1, periodId: 2 } });
  assert.notEqual(result.isError, true, JSON.stringify(result));
  const content = result.content as { type: string; resource?: { uri: string; mimeType: string; blob: string } }[];
  const resource = content.find((c) => c.type === 'resource')!.resource!;
  assert.equal(resource.mimeType, 'application/pdf');
  assert.equal(resource.uri, 'filingbridge://download/accounts-package.pdf');
  assert.equal(Buffer.from(resource.blob, 'base64').length, 2048);
  const summary = result.structuredContent as { file: { bytes: number; sha256: string } };
  assert.equal(summary.file.bytes, 2048);
  assert.match(summary.file.sha256, /^[0-9a-f]{64}$/);
  assert.equal(calls.at(-1)!.headers['authorization'], 'Bearer fbc1.http-token-aaaaaaaa');
});

test('a download over 1 MiB is refused with a pointer to the stdio connector', async () => {
  const { client } = await connect(() => pdf(INLINE_DOWNLOAD_MAX_BYTES + 1));
  const result = await client.callTool({ name: 'accounts_package_download', arguments: { companyId: 1, periodId: 2 } });
  assert.equal(result.isError, true);
  assert.equal((result.structuredContent as { code: string }).code, 'DOWNLOAD_TOO_LARGE');
  assert.match((result.content as { text: string }[])[0]!.text, /stdio connector/);
});

test('the CSV import takes contentBase64 and refuses a path', async () => {
  const statement = 'Date,Description,Amount\n2025-01-02,Sale,100.00\n';
  const { client, calls } = await connect((request) => (request.url.includes('/import') ? json(200, { importedCount: 1 }) : json(404, {})));
  const ok = await client.callTool({
    name: 'bank_statement_import',
    arguments: { companyId: 1, bankAccountId: 3, periodId: 2, contentBase64: Buffer.from(statement).toString('base64'), fileName: 'jan.csv', reason: 'January statement' },
  });
  assert.notEqual(ok.isError, true, JSON.stringify(ok));
  const sent = calls.at(-1)!;
  assert.ok(sent.body instanceof FormData);
  const file = (sent.body as FormData).get('file') as File;
  assert.equal(file.name, 'jan.csv');
  assert.equal(await file.text(), statement);
  assert.equal((ok.structuredContent as { source: { bytes: number } }).source.bytes, Buffer.byteLength(statement));

  const byPath = await client.callTool({ name: 'bank_statement_import', arguments: { companyId: 1, bankAccountId: 3, periodId: 2, file: 'jan.csv', contentBase64: 'YQ==' } });
  assert.equal((byPath.structuredContent as { code: string }).code, 'FILE_ACCESS');

  const notBase64 = await client.callTool({ name: 'bank_statement_import', arguments: { companyId: 1, bankAccountId: 3, periodId: 2, contentBase64: 'not base64!' } });
  assert.equal((notBase64.structuredContent as { code: string }).code, 'FILE_ACCESS');

  const wrongType = await client.callTool({ name: 'bank_statement_import', arguments: { companyId: 1, bankAccountId: 3, periodId: 2, contentBase64: 'YQ==', fileName: 'x.exe' } });
  assert.equal((wrongType.structuredContent as { code: string }).code, 'FILE_ACCESS');

  const tooBig = await client.callTool({
    name: 'bank_statement_import',
    arguments: { companyId: 1, bankAccountId: 3, periodId: 2, contentBase64: 'A'.repeat(INLINE_UPLOAD_MAX_BASE64_CHARS + 4) },
  });
  assert.equal((tooBig.structuredContent as { code: string }).code, 'FILE_ACCESS');
  assert.equal(calls.filter((c) => c.url.includes('/import')).length, 1, 'only the valid import was sent');
});

test('stdio mode is unchanged: no directories means no file tools, and contentBase64 is not accepted', async () => {
  const { client } = await connect(() => json(200, {}), { fileMode: 'local' });
  const names = (await client.listTools()).tools.map((t) => t.name);
  assert.ok(!names.includes('accounts_package_download') && !names.includes('bank_statement_import'));

  const local = await connect(() => json(200, {}), { fileMode: 'local', uploadRoot: '.', downloadDir: '.' });
  const tools = (await local.client.listTools()).tools;
  const csv = tools.find((t) => t.name === 'bank_statement_import')!;
  assert.ok((csv.inputSchema.properties as Record<string, unknown>)['file']);
  assert.equal((csv.inputSchema.properties as Record<string, unknown>)['contentBase64'], undefined);
  assert.ok(tools.some((t) => t.name === 'privacy_access_export'));
});
