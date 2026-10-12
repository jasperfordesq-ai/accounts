/**
 * The period concurrency tokens FilingBridge hands out.
 *
 * Every route under /api/companies/{companyId}/periods/{periodId} answers with
 * an ETag naming the period's current state. Sending it back as If-Match on a
 * change makes FilingBridge refuse (409) a change based on a stale view, which
 * is exactly the failure an agent cannot otherwise notice: two clients, or a
 * person in the browser, editing the same period.
 *
 * The cache holds the latest token per period, from whatever response last
 * carried one. A refusal drops it, so the agent's re-read fetches a new one.
 */
const PERIOD_PATH = /^\/api\/companies\/(\d+)\/periods\/(\d+)(?:\/|$|\?)/;

export function periodKey(path: string): string | null {
  const match = PERIOD_PATH.exec(path);
  return match ? `${match[1]}/${match[2]}` : null;
}

export class ETagCache {
  readonly #byPeriod = new Map<string, string>();

  /** The token to send as If-Match for a change to this path, if one is held. */
  ifMatchFor(path: string): string | undefined {
    const key = periodKey(path);
    return key ? this.#byPeriod.get(key) : undefined;
  }

  remember(path: string, etag: string | null): void {
    const key = periodKey(path);
    if (!key || !etag) return;
    this.#byPeriod.set(key, etag);
  }

  forget(path: string): void {
    const key = periodKey(path);
    if (key) this.#byPeriod.delete(key);
  }

  size(): number {
    return this.#byPeriod.size;
  }
}
