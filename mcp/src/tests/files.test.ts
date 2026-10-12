import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readFileSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { readInsideRoot, resolveInsideRoot, safeFileName, writeDownload, CSV_EXTENSIONS, CSV_MAX_BYTES } from '../files.js';

function root() {
  const dir = mkdtempSync(join(tmpdir(), 'fb-files-'));
  const inside = join(dir, 'inside');
  mkdirSync(inside);
  writeFileSync(join(inside, 'bank.csv'), 'a,b\n');
  writeFileSync(join(dir, 'secret.txt'), 'outside');
  mkdirSync(join(inside, '.git'));
  writeFileSync(join(inside, '.git', 'config'), 'token');
  return { dir, inside };
}

test('reads stay inside the root however the path is spelled', () => {
  const { dir, inside } = root();
  assert.equal(readInsideRoot(inside, 'bank.csv', { maxBytes: CSV_MAX_BYTES, extensions: CSV_EXTENSIONS }).name, 'bank.csv');
  assert.throws(() => resolveInsideRoot(inside, '../secret.txt'), /outside/);
  assert.throws(() => resolveInsideRoot(inside, join(dir, 'secret.txt')), /outside/);
  assert.throws(() => resolveInsideRoot(inside, '.git/config'), /never reads/);
  assert.throws(() => resolveInsideRoot(inside, 'missing.csv'), /No such file/);
  assert.throws(() => resolveInsideRoot(join(dir, 'nope'), 'bank.csv'), /does not exist/);
});

test('a symbolic link out of the root is refused', (t) => {
  const { dir, inside } = root();
  try {
    symlinkSync(join(dir, 'secret.txt'), join(inside, 'link.csv'));
  } catch {
    t.skip('this platform does not allow creating symbolic links here');
    return;
  }
  assert.throws(() => resolveInsideRoot(inside, 'link.csv'), /outside/);
});

test('size and extension limits apply', () => {
  const { inside } = root();
  writeFileSync(join(inside, 'big.csv'), Buffer.alloc(CSV_MAX_BYTES + 1));
  assert.throws(() => readInsideRoot(inside, 'big.csv', { maxBytes: CSV_MAX_BYTES }), /limit/);
  writeFileSync(join(inside, 'x.exe'), 'MZ');
  assert.throws(() => readInsideRoot(inside, 'x.exe', { maxBytes: CSV_MAX_BYTES, extensions: CSV_EXTENSIONS }), /does not accept/);
});

test('downloads are one safe segment inside the directory and never overwrite', () => {
  const dir = mkdtempSync(join(tmpdir(), 'fb-dl-'));
  const first = writeDownload(dir, '../../etc/passwd', Buffer.from('a'), 'text/plain');
  assert.equal(first.filename, 'passwd');
  assert.ok(first.path.startsWith(dir) || first.path.includes('fb-dl-'));
  const second = writeDownload(dir, '../../etc/passwd', Buffer.from('b'), 'text/plain');
  assert.equal(second.filename, 'passwd-1');
  assert.equal(readFileSync(first.path, 'utf8'), 'a');
  assert.equal(safeFileName('..\\..\\x y.pdf', 'f'), 'x_y.pdf');
  assert.equal(safeFileName('...', 'fallback'), 'fallback');
});
