# Runbook: Service API key rotation

The frontend → backend service API key authenticates the Next.js proxy to the Accounts
API (`ApiAccessMiddleware`). In production it is a single key configured as:

- **Backend** validates the presented key against its **SHA-256 hash**:
  `ApiAccess__Keys__0__KeyHash` (env `ACCOUNTS_API_KEY_HASH`). The backend never stores the
  raw key — only the lowercase-hex SHA-256 (`ApiAccessService.HashKey`).
- **Frontend** injects the **raw key** read from the secret file `ACCOUNTS_API_KEY_FILE`
  (`/run/secrets/accounts_api_key`).

Because the two sides hold the hash and the raw value of the *same* secret, a rotation must
update both together and redeploy.

> Scope note (flagged for a future change): this is a **single, unscoped** key — it is not
> per-company-scoped and carries Admin-level service access. Per-company-scoped keys with
> independent rotation are a larger investment tracked separately. Treat this key as a
> high-value secret.

## When to rotate
- On a fixed schedule (recommended: every 90 days).
- Immediately on any suspected exposure (leaked logs, ex-staff with deploy access, etc.).

## Rotation procedure (zero-downtime)

1. **Generate a new key** (32+ random bytes, URL-safe):
   ```bash
   new_key="$(openssl rand -base64 48 | tr -d '\n')"
   ```

2. **Compute its hash** (must equal `ApiAccessService.HashKey` — lowercase hex SHA-256):
   ```bash
   new_hash="$(printf '%s' "$new_key" | sha256sum | awk '{print $1}')"
   ```

3. **Stage the new secret on the frontend** without removing the old key yet. The backend
   `ApiAccess__Keys` list accepts multiple entries, so add the **new hash as a second key**
   first (`ApiAccess__Keys__1__KeyHash`, `ApiAccess__Keys__1__Name=rotation`) and redeploy the
   backend. Both old and new keys are now accepted.

4. **Switch the frontend** to the new raw key: write `new_key` to the `ACCOUNTS_API_KEY_FILE`
   secret (mode `0444`, in the `0700` secrets directory) and redeploy the frontend.

5. **Verify** the frontend can reach the API with the new key:
   ```bash
   # /health/ready exercises the proxy → API call with the injected key
   curl -fsS https://<host>/health/ready | jq .checks.proxyAuth   # expect "accepted"
   ```
   Also run `scripts/smoke-production.ps1` if available.

6. **Retire the old key**: remove the original `ApiAccess__Keys__0__KeyHash`, promote the
   rotation entry to index 0 (or re-number), and redeploy the backend. Only the new key is now
   accepted.

7. **Confirm** old key is rejected (a request carrying the old key must now 401) and update the
   secret store / password manager record with the rotation date.

## Rollback
If step 5 fails, the old key is still valid (you only *added* the new hash in step 3), so revert
the frontend secret file to the old key and redeploy the frontend. No backend change needed.

## Notes
- Never commit the raw key or hash to git; both come from secret files / env at deploy time.
- `ProductionSafetyService` rejects boot if only a development key is configured outside
  development, so production must always carry a real `KeyHash`.
