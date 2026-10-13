#!/usr/bin/env node
/**
 * Canaries: break one property at a time and require the suite to notice.
 *
 * A green suite proves nothing on its own: tests have passed in this family
 * of products whether or not the code was right (a drift parser that audited
 * an empty list, a stub shaped unlike the real API). Each mutation below
 * removes one guarantee; the suite (typecheck plus node:test) must fail for
 * every one of them. Every file is restored afterwards, whatever happens.
 *
 *   node scripts/canary.mjs            run every canary
 *   node scripts/canary.mjs <name>...  run the named ones
 */
import { spawnSync } from 'node:child_process';
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');

const CANARIES = [
  {
    name: 'withheld-entry-dropped',
    what: 'CompanyOfficer.address no longer withheld by the field policy',
    file: 'src/field-policy.ts',
    find: "    withheld: ['address'],",
    replace: '    withheld: [],',
  },
  {
    name: 'exclusion-deleted',
    what: 'GET /health/live silently dropped from the exclusions',
    file: 'src/route-coverage.ts',
    find: "  { route: 'GET /health/live',",
    replace: "  // { route: 'GET /health/live',",
    lineComment: true,
  },
  {
    name: 'delete-without-approval',
    what: 'officer_delete no longer marked as needing approval',
    file: 'src/tools-companies.ts',
    find: "    path: `${CO}/officers/{id}`,\n    level: 'admin',\n    approval: true,",
    replace: "    path: `${CO}/officers/{id}`,\n    level: 'admin',",
  },
  {
    name: 'boundary-sentence-removed',
    what: 'the filing boundary sentence reworded in the instructions',
    file: 'src/instructions.ts',
    find: "  'CRO and ROS final submission remain external actions recorded in workflow state.';",
    replace: "  'Filing is handled elsewhere.';",
  },
  {
    name: 'undeclared-body-field',
    what: 'a body field the contract does not declare added to DebtorInput',
    file: 'src/bodies.ts',
    find: "export const DEBTOR_INPUT_FIELDS = bodyFields('DebtorInput', [\n",
    replace: "export const DEBTOR_INPUT_FIELDS = bodyFields('DebtorInput', [\n  { kind: 'string', max: 10, name: 'internalNotes' },\n",
  },
  {
    name: 'client-header-stripped',
    what: 'X-FilingBridge-Client no longer sent by the API client',
    file: 'src/client.ts',
    find: '        [CLIENT_HEADER]: CLIENT_HEADER_VALUE,\n        [CORRELATION_HEADER]: correlationId,',
    replace: '        [CORRELATION_HEADER]: correlationId,',
  },
  {
    name: 'sdk-unpinned',
    what: 'the MCP SDK dependency no longer an exact pin',
    file: 'package.json',
    find: '"@modelcontextprotocol/sdk": "1.32.1"',
    replace: '"@modelcontextprotocol/sdk": "^1.32.1"',
  },
  {
    name: 'download-full-scope-dropped',
    what: 'accounts_package_download no longer requires full data scope',
    file: 'src/tools-reporting.ts',
    find: "    binary: { kind: 'pdf', defaultName: 'accounts-package.pdf' },\n    requiresFullScope: FILE_FULL_SCOPE,",
    replace: "    binary: { kind: 'pdf', defaultName: 'accounts-package.pdf' },",
  },
  {
    name: 'write-gate-dropped',
    what: 'an officer address no longer refused on write under Withheld',
    file: 'src/bodies.ts',
    find: "name: 'address', nullable: true, personal: true,",
    replace: "name: 'address', nullable: true,",
  },
  {
    name: 'gate-open',
    what: 'the field policy releases everything whatever the scope',
    file: 'src/field-policy.ts',
    find: '  if (allowPersonalData) return value;\n  const context = gateContextFor(value);',
    replace: '  if (allowPersonalData || model) return value;\n  const context = gateContextFor(value);',
  },
  {
    name: 'confirmation-rule-dropped',
    what: 'mark-filed no longer mirrored as a human-confirmation route',
    file: 'src/route-coverage.ts',
    find: "  '/mark-filed',\n",
    replace: '',
  },
  {
    name: 'replay-claimed-without-backend',
    what: 'officer creation claimed as replaying Idempotency-Key, which FilingBridge does not do there',
    file: 'src/idempotency.ts',
    find: "  'POST /api/companies/onboard',\n",
    replace: "  'POST /api/companies/onboard',\n  'POST /api/companies/{companyId}/officers',\n",
  },
  {
    name: 'unknown-outcome-retried',
    what: 'a change that met a dropped connection is repeated on any route',
    file: 'src/client.ts',
    find: '        if (!replaySafe && !neverConnected(cause)) {',
    replace: '        if (method === \'PATCH\' && !replaySafe && !neverConnected(cause)) {',
  },
  {
    name: 'secret-echoed',
    what: 'the hidden prompt writes each typed character to the terminal',
    file: 'src/terminal-input.ts',
    find: '        bytes.push(byte);\n',
    replace: '        bytes.push(byte);\n        output.write(\'*\');\n',
  },
  {
    name: 'loan-label-released',
    what: 'director-loan messages released with the counterparty name they begin with',
    file: 'src/field-policy.ts',
    find: "  if (typeof message !== 'string') return message;\n",
    replace: "  if (typeof message !== 'string' || message.length > 0) return message;\n",
  },
  {
    name: 'withheld-note-released',
    what: 'the director-transactions note text released to a withheld session',
    file: 'src/field-policy.ts',
    find: "  if (noteIsWithheld(record) && typeof content === 'string' && content.trim() !== '') {",
    replace: "  if (noteIsWithheld(record) && typeof content === 'number') {",
  },
  {
    name: 'optional-body-required',
    what: 'an optional body advertised with its fields required',
    file: 'src/tools.ts',
    find: '...(tool.optionalBody ? [] : bodyRequired)',
    replace: '...bodyRequired',
  },
  {
    name: 'approve-command-unpinned',
    what: 'the approve command shown without the profile and base URL in use',
    file: 'src/client.ts',
    find: 'command: this.#profile ? approveCommandFor(approvalId, this.#profile, this.#baseUrl) : command,',
    replace: 'command: this.#profile ? command : approveCommandFor(approvalId, \'default\', this.#baseUrl),',
  },
];

function runSuite() {
  const tsc = spawnSync(process.execPath, [resolve(root, 'node_modules/typescript/bin/tsc'), '-p', 'tsconfig.json'], { cwd: root, encoding: 'utf8' });
  if (tsc.status !== 0) return { red: true, how: 'typecheck failed' };
  const tests = spawnSync(process.execPath, ['--test', 'dist/tests/*.test.js'], { cwd: root, encoding: 'utf8' });
  if (tests.status !== 0) {
    const failed = (tests.stdout.match(/^not ok \d+ - .*$/gm) ?? []).slice(0, 3).join('; ');
    return { red: true, how: `tests failed: ${failed}` };
  }
  return { red: false, how: 'suite stayed green' };
}

const wanted = process.argv.slice(2);
const selected = wanted.length > 0 ? CANARIES.filter((c) => wanted.includes(c.name)) : CANARIES;
if (selected.length === 0) {
  console.error(`No such canary. Known: ${CANARIES.map((c) => c.name).join(', ')}`);
  process.exit(2);
}

const originals = new Map();
const restoreAll = () => {
  for (const [file, text] of originals) writeFileSync(file, text);
};
process.on('SIGINT', () => { restoreAll(); process.exit(130); });

console.log('Baseline: the unmutated suite must be green.');
const baseline = runSuite();
if (baseline.red) {
  console.error(`CANARY BROKEN: the suite is red before any mutation (${baseline.how}).`);
  process.exit(1);
}

let survivors = 0;
for (const canary of selected) {
  const file = resolve(root, canary.file);
  const original = readFileSync(file, 'utf8');
  // A Windows checkout (core.autocrlf) has CRLF where the repository has LF.
  const eol = original.includes('\r\n') ? '\r\n' : '\n';
  const find = canary.find.replaceAll('\n', eol);
  const replace = canary.replace.replaceAll('\n', eol);
  if (!original.includes(find)) {
    console.error(`CANARY BROKEN: ${canary.name}: the text to mutate was not found in ${canary.file}. Update the canary.`);
    survivors += 1;
    continue;
  }
  originals.set(file, original);
  try {
    let mutated = original.replace(find, replace);
    if (canary.lineComment) {
      // Comment out the whole entry (it may span one line).
      const start = original.indexOf(find);
      const end = original.indexOf('\n', start);
      mutated = original.slice(0, start) + original.slice(end + 1);
    }
    writeFileSync(file, mutated);
    const result = runSuite();
    console.log(`${result.red ? 'KILLED  ' : 'SURVIVED'} ${canary.name}: ${canary.what} -> ${result.how}`);
    if (!result.red) survivors += 1;
  } finally {
    writeFileSync(file, original);
    originals.delete(file);
  }
}

// Leave dist built from the restored sources.
const after = runSuite();
if (after.red) {
  console.error(`CANARY BROKEN: the suite is red after restoring the files (${after.how}).`);
  process.exit(1);
}
console.log(survivors === 0
  ? `All ${selected.length} canaries were killed: the suite noticed every broken guarantee.`
  : `${survivors} canary(ies) survived or could not be applied.`);
process.exit(survivors === 0 ? 0 : 1);
