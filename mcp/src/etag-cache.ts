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

/** How many callers' caches serve --http keeps at once. */
export const CALLER_ETAG_CACHE_MAX = 1000;
/** No caller's cache outlives this, whatever its token says (the access-token lifetime). */
export const CALLER_ETAG_TTL_MS = 15 * 60_000;

/**
 * serve --http builds a fresh MCP server and ApiClient for every request, so
 * the period tokens one tool call learns would be gone by the next, and a
 * later write would go out without If-Match: FilingBridge treats If-Match as
 * optional and would accept it over someone else's edit.
 *
 * This keeps one ETagCache per caller, keyed by the SHA-256 of their bearer,
 * for no longer than that bearer lives (capped at CALLER_ETAG_TTL_MS). It is
 * never shared between bearers: a new token, even the same person's after a
 * refresh, starts empty, which costs a re-read and never a wrong version.
 */
export class CallerETagCaches {
  readonly #entries = new Map<string, { cache: ETagCache; expires: number }>();
  readonly #now: () => number;
  readonly #max: number;

  constructor(now: () => number = Date.now, max = CALLER_ETAG_CACHE_MAX) {
    this.#now = now;
    this.#max = max;
  }

  /** The caller's cache, created empty if none is current. */
  for(key: string, tokenExpiresAtMs: number): ETagCache {
    const now = this.#now();
    const existing = this.#entries.get(key);
    if (existing && existing.expires > now) {
      // Most recently used last, so eviction takes the idlest caller.
      this.#entries.delete(key);
      this.#entries.set(key, existing);
      return existing.cache;
    }
    if (existing) this.#entries.delete(key);
    for (const [k, entry] of this.#entries) {
      if (entry.expires <= now) this.#entries.delete(k);
    }
    while (this.#entries.size >= this.#max) {
      const oldest = this.#entries.keys().next().value;
      if (oldest === undefined) break;
      this.#entries.delete(oldest);
    }
    const cache = new ETagCache();
    this.#entries.set(key, { cache, expires: Math.min(tokenExpiresAtMs, now + CALLER_ETAG_TTL_MS) });
    return cache;
  }

  /** The API refused this bearer: whatever it learned is no longer trusted. */
  drop(key: string): void {
    this.#entries.delete(key);
  }

  clear(): void {
    this.#entries.clear();
  }

  size(): number {
    return this.#entries.size;
  }
}
