import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { createServer, type Server } from 'node:net';
import { homedir } from 'node:os';

const HOST = '127.0.0.1';
const FIRST_PORT = 20_000;
const PORT_COUNT = 10_000;
const WAIT_MS = 10_000;

export class ConnectorRefreshLockUnavailableError extends Error {
  constructor() {
    super('Could not safely coordinate session renewal with another connector process. The stored credential was not used; try again.');
    this.name = 'ConnectorRefreshLockUnavailableError';
  }
}

/**
 * A fixed, user-scoped loopback port is an OS-owned cross-process mutex.
 *
 * Several AI clients may each run this connector against the same stored
 * credential. The refresh token is single use, and reuse revokes the whole
 * session family, so two processes refreshing at once would sign the person
 * out. The listener accepts no data and disappears with its process, unlike a
 * stale lock file. Occupation by an unrelated service fails closed.
 */
export function connectorRefreshLockPort(baseUrl: string): number {
  const key = `${homedir().toLowerCase()}\0${new URL(baseUrl).origin.toLowerCase()}\0filingbridge`;
  const hash = createHash('sha256').update(key).digest();
  return FIRST_PORT + (hash.readUInt32BE(0) % PORT_COUNT);
}

export function lockPortWithinEphemeral(port: number, first: number, last: number): boolean {
  return port >= first && port <= last;
}

function assertLinuxLockPortSafe(port: number): void {
  if (process.platform !== 'linux') return;
  let values: number[];
  try {
    values = readFileSync('/proc/sys/net/ipv4/ip_local_port_range', 'utf8')
      .trim().split(/\s+/).map(Number);
  } catch {
    throw new ConnectorRefreshLockUnavailableError();
  }
  const [first, last] = values;
  if (values.length !== 2 || first === undefined || last === undefined
    || !values.every((value) => Number.isInteger(value) && value > 0 && value <= 65535)
    || first > last || lockPortWithinEphemeral(port, first, last)) {
    throw new ConnectorRefreshLockUnavailableError();
  }
}

function tryListen(port: number): Promise<Server | null> {
  return new Promise((resolve, reject) => {
    const server = createServer((socket) => socket.destroy());
    server.once('error', (error: NodeJS.ErrnoException) => {
      if (error.code === 'EADDRINUSE') resolve(null);
      else reject(new ConnectorRefreshLockUnavailableError());
    });
    server.listen({ host: HOST, port, exclusive: true }, () => resolve(server));
  });
}

export async function withConnectorRefreshLock<T>(
  baseUrl: string,
  action: () => Promise<T>,
  waitMs = WAIT_MS,
): Promise<T> {
  const port = connectorRefreshLockPort(baseUrl);
  assertLinuxLockPortSafe(port);
  const deadline = Date.now() + waitMs;
  let server: Server | null = null;
  while (server === null) {
    server = await tryListen(port);
    if (server) break;
    if (Date.now() >= deadline) throw new ConnectorRefreshLockUnavailableError();
    await new Promise((resolve) => setTimeout(resolve, 50));
  }
  try {
    return await action();
  } finally {
    await new Promise<void>((resolve) => server!.close(() => resolve()));
  }
}
