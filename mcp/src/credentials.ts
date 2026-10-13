import { createRequire } from 'node:module';
import { readFileSync, writeFileSync, rmSync, existsSync, chmodSync } from 'node:fs';
import { profileAllowsCredentialFile, type ConnectorProfile } from './config.js';

export const KEYCHAIN_SERVICE = 'filingbridge-mcp';
const ACCOUNT = 'refresh-token';

export interface CredentialStore {
  read(): string | null;
  write(token: string): void;
  clear(): void;
}

type KeyringModule = typeof import('@napi-rs/keyring');

/**
 * The native keychain binding, loaded only when a keychain store is made.
 *
 * `serve --http` never touches a keychain (the AI client owns its tokens), so
 * the container image runs without the native module ever being loaded; a
 * platform without a prebuilt binding fails here, with this message, rather
 * than at start-up.
 */
function loadKeyring(): KeyringModule {
  try {
    return createRequire(import.meta.url)('@napi-rs/keyring') as KeyringModule;
  } catch (cause) {
    throw new Error(
      'The operating system credential store binding (@napi-rs/keyring) could not be loaded on this '
        + `platform, so the connector cannot keep a credential here. (${(cause as Error).message})`,
    );
  }
}

export function createKeyringStore(account: string): CredentialStore {
  const { Entry } = loadKeyring();
  const entry = new Entry(KEYCHAIN_SERVICE, account);
  return {
    read() {
      try {
        return entry.getPassword() ?? null;
      } catch {
        return null;
      }
    },
    write(token: string) {
      // A failed write means the session did not persist. Swallowing it would
      // report a sign-in that silently will not survive.
      try {
        entry.setPassword(token);
      } catch (cause) {
        throw new Error(
          'The operating system\'s credential store would not keep the credential, so the '
            + 'sign-in was not saved. On a desktop this usually means the keychain is locked; on '
            + 'a headless Linux machine, that no Secret Service (such as gnome-keyring) is running. '
            + `(${(cause as Error).message})`,
        );
      }
    },
    clear() {
      try {
        entry.deletePassword();
      } catch {
        // "Already absent" and a real failure look alike; the read-back decides.
      }
      let remaining: string | null = null;
      try {
        remaining = entry.getPassword() ?? null;
      } catch {
        return;
      }
      if (remaining !== null) {
        throw new Error('Failed to clear the stored credential: it is still present in the OS credential store.');
      }
    },
  };
}

export function createMemoryStore(initial: string | null = null): CredentialStore {
  let value = initial;
  return {
    read: () => value,
    write: (token: string) => {
      value = token;
    },
    clear: () => {
      value = null;
    },
  };
}

/**
 * A credential store backed by one JSON file, for the local profile only.
 *
 * It exists so a test harness can drive the connector without touching the OS
 * keychain, where it would overwrite the owner's real credential.
 */
export function createFileStore(path: string): CredentialStore {
  return {
    read() {
      try {
        const parsed = JSON.parse(readFileSync(path, 'utf8')) as { refreshToken?: unknown };
        return typeof parsed.refreshToken === 'string' && parsed.refreshToken.length > 0
          ? parsed.refreshToken
          : null;
      } catch {
        return null;
      }
    },
    write(token: string) {
      writeFileSync(path, `${JSON.stringify({ refreshToken: token })}\n`, { mode: 0o600 });
      try {
        chmodSync(path, 0o600);
      } catch {
        // Windows has no POSIX mode; the file inherits the user's ACL.
      }
    },
    clear() {
      rmSync(path, { force: true });
      if (existsSync(path)) {
        throw new Error(`Failed to clear the stored credential: ${path} is still present.`);
      }
    },
  };
}

/** Scheme, host and port, lower-cased. Anything else is not part of identity. */
export function originOf(baseUrl: string): string {
  let url: URL;
  try {
    url = new URL(baseUrl);
  } catch {
    throw new Error(`The base URL is not a valid URL: ${baseUrl}`);
  }
  return url.origin.toLowerCase();
}

/** The keychain account a host's credential lives under: one per origin. */
export function accountForOrigin(origin: string): string {
  return `${ACCOUNT}:${origin.toLowerCase()}`;
}

export function chooseCredentialStore(options: {
  profile: ConnectorProfile;
  origin: string;
  credentialFile?: string | undefined;
  keyring?: (account: string) => CredentialStore;
  file?: (path: string) => CredentialStore;
}): CredentialStore {
  if (options.credentialFile) {
    if (!profileAllowsCredentialFile(options.profile)) {
      throw new Error(
        'FILINGBRIDGE_CREDENTIAL_FILE is only valid with --profile local. Outside that profile '
          + 'the credential lives in the OS credential store.',
      );
    }
    return (options.file ?? createFileStore)(options.credentialFile);
  }
  return (options.keyring ?? createKeyringStore)(accountForOrigin(options.origin));
}

interface BoundCredential {
  v: 1;
  origin: string;
  refreshToken: string;
}

function parseBound(raw: string): BoundCredential | null {
  try {
    const parsed = JSON.parse(raw) as Partial<BoundCredential>;
    if (parsed && parsed.v === 1
      && typeof parsed.origin === 'string'
      && typeof parsed.refreshToken === 'string') {
      return parsed as BoundCredential;
    }
  } catch {
    // Not JSON at all.
  }
  return null;
}

/**
 * Binds a stored refresh token to the host that issued it.
 *
 * The base URL is not a secret: an AI client configuration file, an
 * environment variable, or an agent with write access to either can point the
 * connector at another host. Binding makes that attempt fail before the
 * credential is transmitted. A mismatch throws rather than reporting "not
 * connected", because silence would invite the person to sign in again,
 * against the redirected host. An unbound entry is refused for the same
 * reason: this connector has never written one.
 */
export function bindCredentialToOrigin(store: CredentialStore, baseUrl: string): CredentialStore {
  const origin = originOf(baseUrl);
  return {
    read() {
      const raw = store.read();
      if (raw === null) return null;
      const bound = parseBound(raw);
      if (!bound) {
        throw new Error(
          'The stored FilingBridge credential is not in a form this connector wrote. Run '
            + '`filingbridge-mcp disconnect` and connect again.',
        );
      }
      if (bound.origin !== origin) {
        throw new Error(
          `The stored credential was issued by ${bound.origin} and will not be sent to ${origin}. `
            + 'If the change of host is intended, run `filingbridge-mcp disconnect` and connect '
            + 'again; if it is not, something has changed the connector\'s base URL.',
        );
      }
      return bound.refreshToken;
    },
    write(token: string) {
      const record: BoundCredential = { v: 1, origin, refreshToken: token };
      store.write(JSON.stringify(record));
    },
    clear() {
      store.clear();
    },
  };
}
