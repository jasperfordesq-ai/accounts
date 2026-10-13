/**
 * What serve --http may read from a connector access token without trusting it.
 *
 * FilingBridge issues access tokens as `fbc1.<payload>.<signature>`, the
 * payload being base64url camelCase JSON of the API's ConnectorAccessPayload
 * (backend/Accounts.Api/Services/ConnectorTokenCodec.cs). Only the API can
 * check the signature, so nothing read here grants anything: the expiry is
 * used only to refuse a token early (so the client is told to refresh with
 * an OAuth challenge) and to stop caches outliving the token.
 */

const MAX_PAYLOAD_LENGTH = 4096;

/**
 * When the token stops being accepted, in milliseconds since the epoch, or
 * null when the token does not have FilingBridge's shape or its expiry cannot
 * be read. The API refuses a token whose expiry is at or before now.
 */
export function accessTokenExpiresAtMs(token: string): number | null {
  const parts = token.split('.');
  if (parts.length !== 3 || parts[0] !== 'fbc1' || !parts[1] || !parts[2]) return null;
  const encoded = parts[1];
  if (encoded.length > MAX_PAYLOAD_LENGTH || !/^[A-Za-z0-9_-]+$/.test(encoded)) return null;
  let payload: unknown;
  try {
    payload = JSON.parse(Buffer.from(encoded, 'base64url').toString('utf8'));
  } catch {
    return null;
  }
  if (!payload || typeof payload !== 'object' || Array.isArray(payload)) return null;
  const seconds = (payload as Record<string, unknown>)['expiresAtUnixSeconds'];
  if (typeof seconds !== 'number' || !Number.isSafeInteger(seconds) || seconds <= 0) return null;
  return seconds * 1000;
}

/** Whether the API would refuse this token as expired at `nowMs` (or cannot be told). */
export function accessTokenExpired(token: string, nowMs: number): boolean {
  const expires = accessTokenExpiresAtMs(token);
  return expires === null || expires <= nowMs;
}
