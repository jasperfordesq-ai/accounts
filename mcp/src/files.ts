import { createHash } from 'node:crypto';
import { readFileSync, realpathSync, statSync, writeFileSync, mkdirSync, chmodSync } from 'node:fs';
import { basename, extname, isAbsolute, join, relative, resolve, sep } from 'node:path';

/**
 * Reading a file to send, and writing one that was downloaded.
 *
 * A download is accounts, filing packs and evidence leaving FilingBridge onto
 * this machine; an upload is a file from this machine entering the company's
 * records. Neither happens unless the person names a directory for it, and
 * nothing outside that directory is read or written, however the path is
 * spelled.
 */

/** FilingBridge's own CSV ceiling (ImportLimitConfig.MaxCsvBytes). */
export const CSV_MAX_BYTES = 5 * 1024 * 1024;
export const CSV_EXTENSIONS = new Set(['.csv', '.txt']);

/** Evidence attached to a JSON body as base64 (signed pages, letters, evidence). */
export const EVIDENCE_MAX_BYTES = 5 * 1024 * 1024;

/**
 * Names never traversed into, whatever the root is. A person who points the
 * upload root at a project directory should not find an agent can read its
 * `.git` configuration or a `.env` file by asking for evidence.
 */
const FORBIDDEN_SEGMENTS = new Set(['.git', 'node_modules', '.ssh', '.env', '.aws', '.gnupg']);

export class FileAccessError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'FileAccessError';
  }
}

function realRootOf(root: string): string {
  try {
    return realpathSync(root);
  } catch {
    throw new FileAccessError(
      `The configured directory does not exist: ${root}. Create it, or start the connector with one that does.`,
    );
  }
}

/**
 * Resolves a path inside a root, refusing anything that escapes it.
 *
 * Containment is decided after resolving symbolic links on both sides,
 * because a link inside the root that points outside it would otherwise pass
 * a check made on the spelling alone.
 */
export function resolveInsideRoot(root: string, requested: string): string {
  if (typeof requested !== 'string' || requested.length === 0 || requested.length > 1024) {
    throw new FileAccessError('Name the file relative to the upload directory.');
  }
  if (requested.includes('\0')) throw new FileAccessError('A file name cannot contain a NUL character.');
  const realRoot = realRootOf(root);
  const candidate = isAbsolute(requested) ? requested : join(realRoot, requested);

  let real: string;
  try {
    real = realpathSync(candidate);
  } catch {
    throw new FileAccessError(`No such file: ${requested}`);
  }

  const within = relative(realRoot, real);
  if (within === '' || within.startsWith('..') || isAbsolute(within)) {
    throw new FileAccessError(
      `${requested} is outside the directory this connector may read (${realRoot}). Nothing was read.`,
    );
  }
  for (const segment of within.split(sep)) {
    if (FORBIDDEN_SEGMENTS.has(segment) || segment.startsWith('.')) {
      throw new FileAccessError(`${requested} goes through "${segment}", which this connector never reads.`);
    }
  }
  return real;
}

export interface ReadFile {
  path: string;
  name: string;
  bytes: Buffer;
  sha256: string;
}

export function readInsideRoot(
  root: string,
  requested: string,
  limits: { maxBytes: number; extensions?: ReadonlySet<string> },
): ReadFile {
  const path = resolveInsideRoot(root, requested);
  const stats = statSync(path);
  if (!stats.isFile()) throw new FileAccessError(`${requested} is not a file.`);
  if (stats.size === 0) throw new FileAccessError(`${requested} is empty.`);
  if (stats.size > limits.maxBytes) {
    throw new FileAccessError(
      `${requested} is ${(stats.size / 1024 / 1024).toFixed(1)} MB; the limit is ${limits.maxBytes / 1024 / 1024} MB.`,
    );
  }
  if (limits.extensions) {
    const extension = extname(path).toLowerCase();
    if (!limits.extensions.has(extension)) {
      throw new FileAccessError(
        `FilingBridge does not accept ${extension || 'files with no extension'} here. It accepts: ${[...limits.extensions].join(', ')}.`,
      );
    }
  }
  const bytes = readFileSync(path);
  return { path, name: basename(path), bytes, sha256: createHash('sha256').update(bytes).digest('hex') };
}

/** A name reduced to one safe path segment. */
export function safeFileName(suggested: string, fallback: string): string {
  const cleaned = basename(suggested.replace(/\\/g, '/'))
    .replace(/[^A-Za-z0-9._-]/g, '_')
    .replace(/^\.+/, '')
    .slice(0, 150);
  return cleaned || fallback;
}

export interface WrittenFile {
  path: string;
  bytes: number;
  sha256: string;
  contentType: string;
  filename: string;
}

/**
 * Writes a downloaded file, owner-only, under the directory the person named.
 *
 * Never overwrites: if the name is taken, a numbered name is chosen instead,
 * so a second download of a filing pack cannot silently replace the copy a
 * person already reviewed or signed.
 */
export function writeDownload(
  directory: string,
  suggestedName: string,
  bytes: Buffer,
  contentType: string,
): WrittenFile {
  mkdirSync(directory, { recursive: true });
  const realDirectory = realpathSync(directory);
  const name = safeFileName(suggestedName, 'download.bin');
  const extension = extname(name);
  const stem = name.slice(0, name.length - extension.length) || 'download';

  for (let attempt = 0; attempt < 1000; attempt += 1) {
    const candidateName = attempt === 0 ? name : `${stem}-${attempt}${extension}`;
    const path = resolve(realDirectory, candidateName);
    const within = relative(realDirectory, path);
    if (within.startsWith('..') || isAbsolute(within) || within.includes(sep)) {
      throw new FileAccessError('The file name could not be made safe to write.');
    }
    try {
      // `wx` fails if the file exists: the check and the create are one step.
      writeFileSync(path, bytes, { mode: 0o600, flag: 'wx' });
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code === 'EEXIST') continue;
      throw new FileAccessError(`Could not write ${candidateName}: ${(error as Error).message}`);
    }
    try {
      chmodSync(path, 0o600);
    } catch {
      // Windows has no POSIX mode; the file inherits the user's access control.
    }
    return {
      path,
      bytes: bytes.length,
      sha256: createHash('sha256').update(bytes).digest('hex'),
      contentType,
      filename: candidateName,
    };
  }
  throw new FileAccessError('Too many files with that name already exist in the download directory.');
}
