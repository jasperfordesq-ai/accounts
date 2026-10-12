import type { Session } from './session.js';

/**
 * Where a request's bearer comes from.
 *
 * In stdio mode it is the person's own refreshing connector session. In
 * phase 2's `serve --http` mode each inbound MCP request carries its own
 * bearer, and the server builds one MCP `Server` per request around a
 * StaticBearer. The ApiClient and every tool only ever see this interface,
 * so neither mode needs to know about the other.
 */
export interface TokenSource {
  /** A bearer that is current now. */
  accessToken(): Promise<string>;
  /** The last bearer was refused; renew it if this source can. */
  invalidate(): void;
  /** Whether invalidate() can produce a different token. */
  readonly canRenew: boolean;
}

export class RefreshingSession implements TokenSource {
  readonly canRenew = true;
  readonly #session: Session;

  constructor(session: Session) {
    this.#session = session;
  }

  accessToken(): Promise<string> {
    return this.#session.accessToken();
  }

  invalidate(): void {
    this.#session.invalidateAccessToken();
  }
}

/** A bearer supplied from outside (phase 2: the inbound HTTP request's). */
export class StaticBearer implements TokenSource {
  readonly canRenew = false;
  readonly #token: string;

  constructor(token: string) {
    if (!token.startsWith('fbc1.')) throw new Error('A connector access token begins fbc1.');
    this.#token = token;
  }

  async accessToken(): Promise<string> {
    return this.#token;
  }

  invalidate(): void {
    // Nothing to renew: the caller re-authenticates.
  }
}
