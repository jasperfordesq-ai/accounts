#!/usr/bin/env node
/**
 * Live end-to-end check: the BUILT connector, driven over stdio by the MCP
 * SDK client, against a running FilingBridge API.
 *
 *   npm run build --prefix mcp
 *   node mcp/scripts/live-e2e.mjs [baseUrl]
 *
 * The base URL defaults to http://127.0.0.1:5091, a throwaway Development
 * API with the connector enabled and a bootstrap Owner (FILINGBRIDGE_E2E_*
 * environment variables override the credentials). The Owner must not yet
 * have enrolled an authenticator: the script enrols one through the browser
 * flow so it can compute codes (RFC 6238). Never point this at a real
 * deployment; it creates companies and deletes officers.
 *
 * The script stands in for the human twice: it types the password into
 * `connect --password-stdin` (local profile only) and it grants the approval
 * by calling POST /api/auth/connector/approve itself.
 */
import { spawn } from 'node:child_process';
import crypto from 'node:crypto';
import { mkdtempSync, readFileSync, writeFileSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { StdioClientTransport } from '@modelcontextprotocol/sdk/client/stdio.js';

const here = dirname(fileURLToPath(import.meta.url));
const CLI = resolve(here, '../dist/cli.js');
const base = process.argv[2] ?? process.env.FILINGBRIDGE_E2E_BASE_URL ?? 'http://127.0.0.1:5091';
const tenantSlug = process.env.FILINGBRIDGE_E2E_TENANT ?? 'mcp-e2e';
const email = process.env.FILINGBRIDGE_E2E_EMAIL ?? 'owner@mcp-e2e.local';
const password = process.env.FILINGBRIDGE_E2E_PASSWORD ?? 'E2eOwner!Accounts-2026-7Kp';
const CLIENT = { 'X-FilingBridge-Client': 'mcp-connector/0.1.0' };

if (!existsSync(CLI)) {
  console.error('Build the connector first: npm run build --prefix mcp');
  process.exit(2);
}

const work = mkdtempSync(join(tmpdir(), 'fb-live-e2e-'));
const downloads = join(work, 'downloads');
const uploads = join(work, 'uploads');
for (const dir of [downloads, uploads]) (await import('node:fs')).mkdirSync(dir, { recursive: true });

const results = [];
function check(name, condition, detail = '') {
  results.push({ name, ok: Boolean(condition) });
  console.log(`${condition ? 'PASS' : 'FAIL'} ${name}${!condition && detail ? ` -- ${String(detail).slice(0, 400)}` : ''}`);
}

/* --- TOTP -------------------------------------------------------------- */
function base32Decode(text) {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
  let bits = '';
  for (const c of text.replace(/=+$/, '').toUpperCase()) bits += alphabet.indexOf(c).toString(2).padStart(5, '0');
  const bytes = [];
  for (let i = 0; i + 8 <= bits.length; i += 8) bytes.push(parseInt(bits.slice(i, i + 8), 2));
  return Buffer.from(bytes);
}
let lastCounter = -1;
async function totp(secret) {
  // A code may be used once: wait for a fresh 30-second window each time.
  let counter = Math.floor(Date.now() / 30000);
  while (counter <= lastCounter) {
    await new Promise((r) => setTimeout(r, 1000));
    counter = Math.floor(Date.now() / 30000);
  }
  lastCounter = counter;
  const buf = Buffer.alloc(8);
  buf.writeBigUInt64BE(BigInt(counter));
  const hmac = crypto.createHmac('sha1', base32Decode(secret)).update(buf).digest();
  const offset = hmac[hmac.length - 1] & 0xf;
  return ((hmac.readUInt32BE(offset) & 0x7fffffff) % 1_000_000).toString().padStart(6, '0');
}

async function http(method, path, { body, token, headers = {} } = {}) {
  const response = await fetch(base + path, {
    method,
    headers: {
      ...(body !== undefined ? { 'content-type': 'application/json' } : {}),
      ...(token ? { authorization: `Bearer ${token}`, ...CLIENT } : {}),
      ...headers,
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await response.text();
  let json = null;
  try { json = text ? JSON.parse(text) : null; } catch { json = text; }
  return { status: response.status, json, text };
}

/* --- the CLI, as a person would run it --------------------------------- */
function runCli(args, { stdin, env = {} } = {}) {
  return new Promise((resolvePromise) => {
    const child = spawn(process.execPath, [CLI, ...args], { env: { ...process.env, ...env }, stdio: ['pipe', 'pipe', 'pipe'] });
    let out = '';
    let err = '';
    child.stdout.on('data', (c) => { out += c; });
    child.stderr.on('data', (c) => { err += c; });
    child.on('close', (code) => resolvePromise({ code, out, err }));
    if (stdin !== undefined) child.stdin.end(stdin);
    else child.stdin.end();
  });
}

async function connect(label, accessLevel, dataScope, secret) {
  const credentialFile = join(work, `${label}.credential.json`);
  const result = await runCli([
    'connect', '--profile', 'local', '--base-url', base, '--password-stdin',
    '--tenant-slug', tenantSlug, '--email', email, '--access-level', accessLevel, '--data-scope', dataScope,
    '--totp-code', await totp(secret), '--device-label', `live-e2e ${label}`,
  ], { stdin: `${password}\n`, env: { FILINGBRIDGE_CREDENTIAL_FILE: credentialFile } });
  check(`connect ${label} (${accessLevel}/${dataScope})`, result.code === 0 && /Connected as/.test(result.out), result.err || result.out);
  check(`connect ${label} never echoes the password`, !result.out.includes(password) && !result.err.includes(password));
  return credentialFile;
}

async function serve(credentialFile) {
  const transport = new StdioClientTransport({
    command: process.execPath,
    args: [CLI, 'serve', '--profile', 'local', '--base-url', base, '--download-dir', downloads, '--upload-root', uploads],
    env: { ...process.env, FILINGBRIDGE_CREDENTIAL_FILE: credentialFile },
    stderr: 'pipe',
  });
  const client = new Client({ name: 'live-e2e', version: '0.1.0' });
  await client.connect(transport);
  return client;
}

async function call(client, name, args = {}) {
  const result = await client.callTool({ name, arguments: args });
  return { error: result.isError === true, data: result.structuredContent ?? {}, text: result.content?.[0]?.text ?? '' };
}

/* --- 1. enrol the Owner's authenticator through the browser flow --------- */
const begin = await http('POST', '/api/auth/login', { body: { tenantSlug, email, password } });
if (begin.status !== 202 || !begin.json?.enrollmentSecret) {
  console.error(`The Owner must be fresh (no authenticator yet). /api/auth/login answered ${begin.status}. Recreate the e2e API and run again.`);
  process.exit(2);
}
const secret = begin.json.enrollmentSecret;
const enrolled = await http('POST', '/api/auth/mfa/challenge', { body: { challengeToken: begin.json.challengeToken, totpCode: await totp(secret) } });
check('browser MFA enrolment', enrolled.status === 200, enrolled.text);

/* --- 2. a Withheld admin agent ------------------------------------------ */
const withheldCred = await connect('withheld-admin', 'admin', 'withheld', secret);
const bound = JSON.parse(JSON.parse(readFileSync(withheldCred, 'utf8')).refreshToken);
check('the stored credential is bound to its origin', bound.origin === new URL(base).origin && bound.refreshToken.startsWith('fbr1.'));
const agent = await serve(withheldCred);
check('initialize carries the filing boundary', /CRO and ROS final submission remain external actions recorded in workflow state\./.test(agent.getInstructions() ?? ''));
const names = (await agent.listTools()).tools.map((t) => t.name);
check('tools listed for a withheld admin session', names.includes('company_create') && names.includes('officer_delete'), names.length);
check('no downloads offered while withheld', !names.includes('accounts_package_download'));
check('approve is never a tool', !names.some((n) => /approve$/.test(n) && n !== 'adjustment_approve'));

const info = await call(agent, 'session_info');
check('session_info reports admin/withheld', info.data.session?.accessLevel === 'admin' && info.data.session?.dataScope === 'withheld', info.text);

const companyArgs = {
  legalName: `Live Connector Check ${Date.now()} Limited`, croNumber: '765433', companyType: 'Private', incorporationDate: '2021-03-01',
  isTrading: true, annualReturnDate: '2022-03-01', financialYearStartMonth: 1, annualReturnDateEffectiveFrom: '2021-03-01',
  annualReturnDateSource: 'CroRecord', annualReturnDateEvidenceReference: 'CRO B1 printout', annualReturnDateEvidenceSha256: 'a'.repeat(64),
  reason: 'live e2e',
};
const withOffice = await call(agent, 'company_create', { ...companyArgs, registeredOfficeAddress1: '1 Test Street' });
check('withheld session cannot write a registered office (client-side)', withOffice.error && withOffice.data.code === 'PERSONAL_DATA_WITHHELD', withOffice.text);
const company = await call(agent, 'company_create', companyArgs);
check('company_create', !company.error && company.data.id, company.text);
const companyId = company.data.id;

const withAddress = await call(agent, 'officer_create', { companyId, name: 'Aoife Example', role: 'Director', address: '12 Private Lane' });
check('withheld session cannot write an officer address', withAddress.error && withAddress.data.code === 'PERSONAL_DATA_WITHHELD', withAddress.text);
const officer = await call(agent, 'officer_create', { companyId, name: 'Aoife Example', role: 'Director', appointedDate: '2021-03-01' });
check('officer_create', !officer.error && officer.data.id, officer.text);
const officers = await call(agent, 'officer_list', { companyId });
check('officer_list carries no address field', !officers.error && !officers.text.includes('"address"'), officers.text);

const period = await call(agent, 'period_create', { companyId, periodStart: '2021-03-01', periodEnd: '2022-02-28', isFirstYear: true, reason: 'live e2e' });
check('period_create', !period.error && period.data.id, period.text);
const periodId = period.data.id;

const debtor = await call(agent, 'debtor_create', { companyId, periodId, name: 'Trade customer', amount: 1250.5, type: 'Trade' });
check('debtor_create', !debtor.error && debtor.data.id, debtor.text);
const debtorUpdate = await call(agent, 'debtor_update', { companyId, periodId, id: debtor.data.id, name: 'Trade customer', amount: 1300, type: 'Trade', reason: 'corrected' });
check('debtor_update with the period If-Match', !debtorUpdate.error, debtorUpdate.text);

const bank = await call(agent, 'bank_account_create', { companyId, name: 'Current account', currency: 'EUR', openingBalance: 0, openingBalanceDate: '2021-03-01' });
check('bank_account_create', !bank.error && bank.data.id, bank.text);
writeFileSync(join(uploads, 'statement.csv'), 'Date,Description,Amount\n15/04/2021,Customer receipt,500.00\n20/04/2021,Office rent,-200.00\n');
const imported = await call(agent, 'bank_statement_import', { companyId, bankAccountId: bank.data.id, periodId, file: 'statement.csv' });
check('bank_statement_import from --upload-root', !imported.error, imported.text);
const escape = await call(agent, 'bank_statement_import', { companyId, bankAccountId: bank.data.id, periodId, file: '../outside.csv' });
check('an upload outside --upload-root is refused', escape.error && escape.data.code === 'FILE_ACCESS', escape.text);

const found = await call(agent, 'search', { query: 'Live Connector Check', types: ['company'] });
const ref = found.data.results?.[0]?.id;
check('search returns a filingbridge:// reference', typeof ref === 'string' && ref === `filingbridge://company/${companyId}`, found.text);
const fetched = await call(agent, 'fetch', { id: ref });
check('fetch reads the record through the gate', !fetched.error && fetched.data.title?.startsWith('Live Connector Check') && !fetched.data.text.includes('registeredOfficeAddress1'), fetched.text);

/* --- 3. the approval flow ----------------------------------------------- */
const ask = await call(agent, 'officer_delete', { companyId, id: officer.data.id, reason: 'recorded in error' });
check('officer_delete answers APPROVAL_REQUIRED', ask.error && ask.data.code === 'APPROVAL_REQUIRED' && ask.data.command === `filingbridge-mcp approve ${ask.data.approvalId}`, ask.text);
const approvalId = ask.data.approvalId;
const early = await call(agent, 'officer_delete', { companyId, id: officer.data.id, reason: 'recorded in error', approvalId });
check('an ungranted approval cannot be spent', early.error && early.data.code === 'APPROVAL_REQUIRED', early.text);

// Standing in for the person: a separate connector sign-in grants it.
const approver = await http('POST', '/api/auth/connector/login', {
  headers: CLIENT,
  body: { tenantSlug, email, password, accessLevel: 'Write', dataScope: 'Withheld', totpCode: await totp(secret), deviceLabel: 'live-e2e approver' },
});
check('approver signs in', approver.status === 200, approver.text);
const wrong = await http('POST', '/api/auth/connector/approve', { token: approver.json?.accessToken, body: { approvalId, password, totpCode: '000000' } });
check('a wrong authenticator code grants nothing', wrong.status === 401 && wrong.json?.code === 'APPROVAL_REFUSED', wrong.text);
const grant = await http('POST', '/api/auth/connector/approve', { token: approver.json?.accessToken, body: { approvalId, password, totpCode: await totp(secret) } });
check('password + authenticator grants the approval', grant.status === 200 && grant.json?.ok === true, grant.text);
const done = await call(agent, 'officer_delete', { companyId, id: officer.data.id, reason: 'recorded in error', approvalId });
check('the identical retry with approvalId succeeds', !done.error, done.text);
const replay = await call(agent, 'officer_delete', { companyId, id: officer.data.id, reason: 'recorded in error', approvalId });
check('an approval is single use', replay.error, replay.text);
await agent.close();

/* --- 4. a Full Owner agent: downloads and audit attribution ------------- */
const fullCred = await connect('full-admin', 'admin', 'full', secret);
const owner = await serve(fullCred);
const fullNames = (await owner.listTools()).tools.map((t) => t.name);
check('downloads offered to a full session with --download-dir', fullNames.includes('accounts_package_download'));
const classified = await call(owner, 'size_classify', { companyId, periodId, turnover: 250000, balanceSheetTotal: 120000, avgEmployees: 3, reason: 'live e2e' });
check('size_classify', !classified.error, classified.text);
const regime = await call(owner, 'filing_regime_determine', { companyId, periodId, reason: 'live e2e' });
check('filing_regime_determine', !regime.error, regime.text);
const pdf = await call(owner, 'accounts_package_download', { companyId, periodId });
if (!pdf.error) {
  const bytes = readFileSync(pdf.data.saved.path);
  check('download written with a matching SHA-256', crypto.createHash('sha256').update(bytes).digest('hex') === pdf.data.saved.sha256 && bytes.subarray(0, 4).toString() === '%PDF');
  check('download contents are not returned to the model', !pdf.text.includes('%PDF'));
} else {
  check('accounts_package_download', false, pdf.text);
}
const audit = await call(owner, 'audit_log_list', { companyId, pageSize: 100 });
check('audit log attributes changes to the connector with the reason', !audit.error && audit.text.includes('McpConnector') && audit.text.includes('recorded in error'), audit.text.slice(0, 300));
await owner.close();

/* --- 5. a Read agent ----------------------------------------------------- */
const readCred = await connect('read', 'read', 'withheld', secret);
const reader = await serve(readCred);
const readNames = (await reader.listTools()).tools.map((t) => t.name);
check('no write tools offered at read level', readNames.includes('debtor_list') && !readNames.includes('debtor_create'));
const refused = await call(reader, 'debtor_create', { companyId, periodId, name: 'x', amount: 1, type: 'Trade' });
check('a write called anyway is refused at read level', refused.error && refused.data.code === 'SESSION_LEVEL_TOO_LOW', refused.text);
const pending = await call(reader, 'connector_sessions_list');
check('connector_sessions_list lists the connected agents', !pending.error, pending.text);
await reader.close();

/* --- 6. disconnect ------------------------------------------------------- */
const out = await runCli(['disconnect', '--profile', 'local', '--base-url', base], { env: { FILINGBRIDGE_CREDENTIAL_FILE: readCred } });
check('disconnect revokes and forgets', out.code === 0 && !existsSync(readCred), out.err || out.out);
const status = await runCli(['status', '--profile', 'local', '--base-url', base], { env: { FILINGBRIDGE_CREDENTIAL_FILE: readCred } });
check('status after disconnect says not connected', /Not connected/.test(status.out), status.out + status.err);

const failed = results.filter((r) => !r.ok);
console.log(`\n${results.length - failed.length}/${results.length} live checks passed.`);
process.exit(failed.length === 0 ? 0 : 1);
