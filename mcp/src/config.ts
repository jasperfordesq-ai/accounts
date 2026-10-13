import { TOOL_GROUPS, type ToolGroup } from './toolsets.js';

const COMMANDS = new Set(['serve', 'connect', 'disconnect', 'status', 'approve', 'help', 'version']);

/**
 * Hosts that cannot leave this machine. Checked on the parsed URL's hostname,
 * never on a string prefix: "http://127.0.0.1.evil.example" begins with a
 * loopback-looking string and resolves wherever its owner chooses.
 */
export const LOOPBACK_HOSTS: ReadonlySet<string> = new Set(['localhost', '127.0.0.1', '[::1]']);

/** The tailnet suffix a Private Server is reached on through Tailscale Serve. */
export const TAILNET_SUFFIX = '.ts.net';

export type ConnectorProfile = 'default' | 'local' | 'private';

interface ProfileRule {
  readonly describe: string;
  /** Plain http, which only a host on this machine may use. */
  readonly allowLoopbackHttp: boolean;
  /** Hostnames are restricted to loopback. */
  readonly loopbackOnly?: boolean;
  /** Hostnames are restricted to loopback or a tailnet name. */
  readonly loopbackOrTailnet?: boolean;
  /** Whether FILINGBRIDGE_CREDENTIAL_FILE may replace the OS credential store. */
  readonly allowCredentialFile: boolean;
}

/**
 * What each profile may reach.
 *
 * A profile is a pin, not a shortcut. The configuration file an AI client
 * reads is treated as something an attacker may write, so the profile
 * decides, before any password is typed, which hosts the sign-in may go to.
 *
 * - `local`: a Development stack on this machine. The only profile that may
 *   keep its credential in a file, for test harnesses.
 * - `private`: a Private Server, on its IPv4 loopback frontend (plain http is
 *   acceptable because the traffic never leaves the host) or through
 *   Tailscale Serve on an https *.ts.net name.
 * - `default`: any https host, for Public Production behind its ingress.
 */
const PROFILES: Record<ConnectorProfile, ProfileRule> = {
  default: {
    describe: 'Any https host (Public Production behind its HTTPS ingress).',
    allowLoopbackHttp: false,
    allowCredentialFile: false,
  },
  local: {
    describe: 'A Development stack on this machine only (http allowed on loopback).',
    allowLoopbackHttp: true,
    loopbackOnly: true,
    allowCredentialFile: true,
  },
  private: {
    describe: 'A Private Server: its loopback frontend, or https on a Tailscale *.ts.net name.',
    allowLoopbackHttp: true,
    loopbackOrTailnet: true,
    allowCredentialFile: false,
  },
};

export const PROFILE_NAMES = Object.keys(PROFILES) as readonly ConnectorProfile[];

export function profileAllowsCredentialFile(profile: ConnectorProfile): boolean {
  return PROFILES[profile].allowCredentialFile;
}

export function profileSummary(indent = '  '): string {
  return PROFILE_NAMES
    .map((name) => `${indent}${name.padEnd(9)}${PROFILES[name].describe}`)
    .join('\n');
}

/** How much authority the session asks for at sign-in. Immutable afterwards. */
export type AccessLevel = 'read' | 'write' | 'admin';
export const ACCESS_LEVELS: readonly AccessLevel[] = ['read', 'write', 'admin'];

/**
 * How much personal data the session may see. Chosen at sign-in and recorded
 * on the session by FilingBridge; `full` requires the Owner role. It is never
 * a serve-time flag, because the AI client's configuration file is not a
 * place a data-protection decision may be taken.
 */
export type DataScope = 'withheld' | 'full';
export const DATA_SCOPES: readonly DataScope[] = ['withheld', 'full'];

export interface ConnectorConfig {
  command: string;
  baseUrl: string;
  profile: ConnectorProfile;
  accessLevel: AccessLevel;
  dataScope: DataScope;
  tenantSlug?: string | undefined;
  email?: string | undefined;
  passwordStdin: boolean;
  totpCode?: string | undefined;
  recoveryCode?: string | undefined;
  deviceLabel?: string | undefined;
  /** The approval to grant. Only meaningful for the approve command. */
  approvalId?: string | undefined;
  /** The one directory files may be read from. Absent means no uploads. */
  uploadRoot?: string | undefined;
  /** The one directory files may be written into. Absent means no downloads. */
  downloadDir?: string | undefined;
  /** The tool groups to offer. Absent means every group. */
  toolsets?: readonly ToolGroup[] | undefined;
  verbose: boolean;
  /** serve --http: Streamable HTTP for remote clients, configured from the environment (http-server.ts). */
  http: boolean;
  /** serve --http: the address to listen on. */
  host: string;
  /** serve --http: the port to listen on. */
  port: number;
}

export const DEFAULT_HTTP_HOST = '127.0.0.1';
export const DEFAULT_HTTP_PORT = 3100;

function nonEmpty(value: string | undefined): string | undefined {
  const trimmed = value?.trim();
  return trimmed ? trimmed : undefined;
}

/**
 * A directory setting that may have arrived from a Claude Desktop bundle.
 *
 * The bundle's manifest passes `--download-dir ${user_config.download_dir}`
 * on every start. When the person leaves an optional folder blank, the host
 * either substitutes an empty string or leaves the placeholder exactly as
 * written. Both mean "not set": the feature is not offered, rather than files
 * landing in a directory literally named after the placeholder.
 */
export function configuredDirectory(value: string | undefined): string | undefined {
  const trimmed = nonEmpty(value);
  if (trimmed === undefined) return undefined;
  if (/^\$\{[^}]*\}$/.test(trimmed)) return undefined;
  return trimmed;
}

function directoryAt(argv: string[], index: number, flag: string): string | undefined {
  const v = argv[index];
  if (v === undefined) throw new Error(`${flag} requires a value`);
  return configuredDirectory(v);
}

function parseUrl(baseUrl: string): URL {
  try {
    return new URL(baseUrl);
  } catch {
    throw new Error(`The base URL is not a valid URL: ${baseUrl}`);
  }
}

/** Applies a profile's pin to a base URL, throwing a refusal that names the rule. */
export function assertBaseUrlAllowed(profile: ConnectorProfile, baseUrl: string): void {
  const rule = PROFILES[profile];
  const url = parseUrl(baseUrl);
  const loopback = LOOPBACK_HOSTS.has(url.hostname);

  if (url.protocol !== 'https:' && url.protocol !== 'http:') {
    throw new Error('The base URL must use http or https.');
  }
  if (url.username || url.password) {
    throw new Error('The base URL must not carry a user name or password.');
  }
  if (url.pathname !== '/' && url.pathname !== '') {
    throw new Error(
      'The base URL is the FilingBridge origin only, such as https://filingbridge.example.ts.net, '
        + `with no path. Got: ${baseUrl}`,
    );
  }
  if (url.search || url.hash) {
    throw new Error(`The base URL must not carry a query or fragment. Got: ${baseUrl}`);
  }
  if (url.protocol === 'http:' && !(rule.allowLoopbackHttp && loopback)) {
    throw new Error(
      rule.allowLoopbackHttp
        ? 'Plain http is accepted only for a loopback address (localhost, 127.0.0.1 or [::1]). '
          + 'Anything else must use https; TLS verification is not optional.'
        : 'The base URL must use https. TLS verification is not optional. For a stack on this '
          + 'machine use --profile local; for a Private Server use --profile private.',
    );
  }
  if (rule.loopbackOnly && !loopback) {
    throw new Error(
      `--profile ${profile} only accepts a loopback base URL (localhost, 127.0.0.1 or [::1]). Got: ${baseUrl}`,
    );
  }
  if (rule.loopbackOrTailnet && !loopback && !url.hostname.endsWith(TAILNET_SUFFIX)) {
    throw new Error(
      `--profile ${profile} only reaches the Private Server's loopback address or an https `
        + `*${TAILNET_SUFFIX} name. Got: ${baseUrl}. A profile is a pin: for another deployment `
        + 'use --profile default.',
    );
  }
}

function valueAt(argv: string[], index: number, flag: string): string {
  const v = argv[index];
  if (v === undefined || v === '') throw new Error(`${flag} requires a value`);
  return v;
}

export function parseArgs(argv: string[], env: NodeJS.ProcessEnv = process.env): ConnectorConfig {
  let command = 'serve';
  let commandSeen = false;
  let baseUrl = nonEmpty(env['FILINGBRIDGE_BASE_URL']);
  let profile: ConnectorProfile = 'default';
  let accessLevel: AccessLevel | undefined;
  let dataScope: DataScope | undefined;
  let tenantSlug: string | undefined;
  let email: string | undefined;
  let passwordStdin = false;
  let totpCode: string | undefined;
  let recoveryCode: string | undefined;
  let deviceLabel: string | undefined;
  let approvalId: string | undefined;
  let uploadRoot: string | undefined;
  let downloadDir: string | undefined;
  let toolsets: ToolGroup[] | undefined;
  let verbose = false;
  let http = false;
  let host: string | undefined;
  let port: number | undefined;

  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i]!;
    if (!commandSeen && COMMANDS.has(arg)) {
      command = arg;
      commandSeen = true;
    } else if (arg === '--help' || arg === '-h') {
      command = 'help';
    } else if (arg === '--version') {
      command = 'version';
    } else if (arg === '--http') {
      http = true;
    } else if (arg === '--host') {
      i += 1;
      host = valueAt(argv, i, arg);
      if (!/^[A-Za-z0-9.:[\]-]{1,255}$/.test(host)) throw new Error(`--host is not an address: ${host}`);
    } else if (arg === '--port') {
      i += 1;
      const v = valueAt(argv, i, arg);
      port = Number(v);
      if (!/^\d{1,5}$/.test(v) || port < 1 || port > 65535) throw new Error(`--port must be 1 to 65535. Got: ${v}`);
    } else if (arg === '--base-url') {
      i += 1;
      baseUrl = valueAt(argv, i, arg).replace(/\/+$/, '');
    } else if (arg === '--profile') {
      i += 1;
      const v = valueAt(argv, i, arg);
      if (!PROFILE_NAMES.includes(v as ConnectorProfile)) {
        throw new Error(`Unknown profile: ${v}. The profiles are:\n${profileSummary()}`);
      }
      profile = v as ConnectorProfile;
    } else if (arg === '--access-level') {
      i += 1;
      const v = valueAt(argv, i, arg);
      if (!ACCESS_LEVELS.includes(v as AccessLevel)) {
        throw new Error(`Unknown access level: ${v}. Use one of: ${ACCESS_LEVELS.join(', ')}.`);
      }
      accessLevel = v as AccessLevel;
    } else if (arg === '--data-scope') {
      i += 1;
      const v = valueAt(argv, i, arg);
      if (!DATA_SCOPES.includes(v as DataScope)) {
        throw new Error(`Unknown data scope: ${v}. Use one of: ${DATA_SCOPES.join(', ')}.`);
      }
      dataScope = v as DataScope;
    } else if (arg === '--tenant-slug' || arg === '--workspace') {
      i += 1;
      tenantSlug = valueAt(argv, i, arg);
    } else if (arg === '--email') {
      i += 1;
      email = valueAt(argv, i, arg);
    } else if (arg === '--password-stdin') {
      passwordStdin = true;
    } else if (arg === '--totp-code') {
      i += 1;
      totpCode = valueAt(argv, i, arg);
      if (!/^\d{6}$/.test(totpCode)) {
        throw new Error('--totp-code must be the six digits your authenticator shows.');
      }
    } else if (arg === '--recovery-code') {
      i += 1;
      recoveryCode = valueAt(argv, i, arg);
    } else if (arg === '--device-label') {
      i += 1;
      deviceLabel = valueAt(argv, i, arg).slice(0, 100);
    } else if (arg === '--upload-root') {
      i += 1;
      uploadRoot = directoryAt(argv, i, arg);
    } else if (arg === '--download-dir') {
      i += 1;
      downloadDir = directoryAt(argv, i, arg);
    } else if (arg === '--toolsets') {
      i += 1;
      const v = valueAt(argv, i, arg);
      if (v === 'all') {
        toolsets = undefined;
      } else {
        const names = v.split(',').map((n) => n.trim()).filter(Boolean);
        for (const name of names) {
          if (!(TOOL_GROUPS as readonly string[]).includes(name)) {
            throw new Error(`Unknown toolset: ${name}. Use one of: ${TOOL_GROUPS.join(', ')}, or all.`);
          }
        }
        toolsets = names as ToolGroup[];
      }
    } else if (arg === '--verbose') {
      verbose = true;
    } else if (command === 'approve' && !arg.startsWith('-') && approvalId === undefined) {
      approvalId = arg;
    } else {
      throw new Error(`Unknown option: ${arg}`);
    }
  }

  if ((host !== undefined || port !== undefined) && !http) {
    throw new Error('--host and --port apply only to serve --http.');
  }
  if (http) {
    if (command !== 'serve') throw new Error('--http applies only to the serve command.');
    if (uploadRoot !== undefined || downloadDir !== undefined) {
      throw new Error(
        'serve --http never reads or writes the files on this machine: uploads and downloads travel base64-encoded. '
          + 'Remove --upload-root and --download-dir.',
      );
    }
    // The API address, the public URL, the service key and the allowed
    // hosts come from the environment; see httpConfigFromEnv.
    return {
      command,
      baseUrl: (nonEmpty(env['FILINGBRIDGE_API_URL']) ?? 'http://api:8080').replace(/\/+$/, ''),
      profile,
      accessLevel: accessLevel ?? 'write',
      dataScope: dataScope ?? 'withheld',
      passwordStdin: false,
      toolsets,
      verbose,
      http: true,
      host: host ?? DEFAULT_HTTP_HOST,
      port: port ?? DEFAULT_HTTP_PORT,
    };
  }

  if (totpCode && recoveryCode) {
    throw new Error('Give either --totp-code or --recovery-code, not both.');
  }

  uploadRoot = uploadRoot ?? configuredDirectory(env['FILINGBRIDGE_UPLOAD_ROOT']);
  downloadDir = downloadDir ?? configuredDirectory(env['FILINGBRIDGE_DOWNLOAD_DIR']);

  const informational = command === 'help' || command === 'version';
  if (baseUrl === undefined) {
    if (informational || profile === 'local') {
      // The Development frontend's documented address.
      baseUrl = 'http://localhost:3000';
    } else {
      throw new Error(
        'Name the FilingBridge address with --base-url (or FILINGBRIDGE_BASE_URL), for example '
          + '--profile private --base-url http://127.0.0.1:3500.',
      );
    }
  }
  if (!informational) assertBaseUrlAllowed(profile, baseUrl);

  if (approvalId !== undefined && !/^[A-Za-z0-9-]{1,64}$/.test(approvalId)) {
    throw new Error('An approval identifier is letters, digits and hyphens, exactly as FilingBridge printed it.');
  }

  return {
    command,
    baseUrl: baseUrl.replace(/\/+$/, ''),
    profile,
    // A Development stack holds nothing real, so its convenient default is
    // full authority. Anywhere else the default withholds the destructive and
    // administrative actions until someone asks for them by name.
    accessLevel: accessLevel ?? (profile === 'local' ? 'admin' : 'write'),
    dataScope: dataScope ?? 'withheld',
    tenantSlug,
    email,
    passwordStdin,
    totpCode,
    recoveryCode,
    deviceLabel,
    approvalId,
    uploadRoot,
    downloadDir,
    toolsets,
    verbose,
    http: false,
    host: DEFAULT_HTTP_HOST,
    port: DEFAULT_HTTP_PORT,
  };
}
