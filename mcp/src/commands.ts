import { createInterface } from 'node:readline/promises';
import { parseArgs, profileSummary, type ConnectorConfig } from './config.js';
import { bindCredentialToOrigin, chooseCredentialStore, originOf, type CredentialStore } from './credentials.js';
import { assertApproveAllowed, assertNonInteractiveConnectAllowed, readPasswordFromStdin } from './connect-input.js';
import { Session, SecondFactorRequiredError } from './session.js';
import { RefreshingSession } from './token-source.js';
import { ApiClient } from './client.js';
import { fetchSessionPosture } from './session-level.js';
import { formatStatus } from './status.js';
import { approvalState, explainState, formatApprovalPreview } from './approval-preview.js';
import { startStdioServer } from './server.js';
import { CONNECTOR_VERSION } from './version.js';
import { TOOL_GROUPS } from './toolsets.js';
import { httpConfigFromEnv, startHttpServer } from './http-server.js';
import { readSecret, type SecretInput } from './terminal-input.js';

export const USAGE = `filingbridge-mcp ${CONNECTOR_VERSION}

The FilingBridge connector: an MCP server that lets an AI client prepare Irish
statutory accounts in FilingBridge as you. Run with no command to serve over
stdio to an AI client.

Commands (run these yourself, in a terminal):
  connect        Sign in and keep a connector credential in the OS keychain.
                 --tenant-slug <workspace> --email <address>
                 --access-level read|write|admin   (default write; admin on --profile local)
                 --data-scope withheld|full        (default withheld; full is Owner only)
                 --totp-code <6 digits> | --recovery-code <code>
                 --device-label <name>             (how this machine appears in FilingBridge)
                 --password-stdin                  (--profile local only, for test harnesses)
  status         Who the stored credential acts as, and the level and scope FilingBridge holds.
  approve <id>   Approve one action FilingBridge refused. Terminal only; shows the
                 action first, then asks for your password and authenticator code.
  disconnect     Revoke the connector session in FilingBridge and forget the credential.
  serve          Serve MCP over stdio (the default).
  serve --http   Serve MCP over Streamable HTTP for remote clients (Claude.ai, Claude Code,
                 Codex) that sign in with FilingBridge's OAuth. Runs inside the stack as
                 the mcp service; see "HTTP service" below.
  help, version

Options:
  --base-url <url>        The FilingBridge web address (or FILINGBRIDGE_BASE_URL).
  --profile <name>        Pin which hosts this may reach:
${profileSummary('                          ')}
  --download-dir <dir>    Allow downloads (accounts PDFs, filing packs, iXBRL), written here.
  --upload-root <dir>     Allow the CSV import and evidence attachments, read only from here.
  --toolsets <a,b>        Offer only these tool groups (core is always on): ${TOOL_GROUPS.filter((g) => g !== 'core').join(', ')}.
  --verbose               Log each tool call (name, outcome, time) to stderr.

HTTP service (serve --http [--host 127.0.0.1] [--port 3100]):
  FILINGBRIDGE_API_URL        The API, e.g. http://api:8080.
  FILINGBRIDGE_API_KEY_FILE   File holding the connector's service key (sent as X-Accounts-Api-Key).
  FILINGBRIDGE_PUBLIC_URL     The FilingBridge origin people use: the OAuth issuer. The MCP
                              resource is <origin>/mcp.
  FILINGBRIDGE_ALLOWED_HOSTS  Comma-separated Host values accepted (default: the public host,
                              mcp:<port> and localhost).
  Files travel base64-encoded over HTTP: downloads up to 1 MiB, CSV imports up to 5 MiB.
`;

export interface CliIo {
  stdin: NodeJS.ReadStream;
  stdout: NodeJS.WritableStream;
  stderr: NodeJS.WritableStream;
  env: NodeJS.ProcessEnv;
  stdinIsTty: boolean;
  stdoutIsTty: boolean;
  /** Overridable for tests. */
  makeStore?: (config: ConnectorConfig) => CredentialStore;
  fetchImpl?: typeof fetch;
}

async function prompt(io: CliIo, question: string, hidden: boolean): Promise<string> {
  // A secret never passes through readline: see terminal-input.ts.
  if (hidden) return readSecret(io.stdin as unknown as SecretInput, io.stdout, question);
  const rl = createInterface({ input: io.stdin, output: io.stdout, terminal: true });
  try {
    return (await rl.question(question)).trim();
  } finally {
    rl.close();
  }
}

function storeFor(config: ConnectorConfig, io: CliIo): CredentialStore {
  const raw = io.makeStore
    ? io.makeStore(config)
    : chooseCredentialStore({
      profile: config.profile,
      origin: originOf(config.baseUrl),
      credentialFile: io.env['FILINGBRIDGE_CREDENTIAL_FILE'],
    });
  return bindCredentialToOrigin(raw, config.baseUrl);
}

export async function runCli(argv: string[], io: CliIo): Promise<void> {
  const config = parseArgs(argv, io.env);
  if (config.command === 'version') {
    io.stdout.write(`filingbridge-mcp ${CONNECTOR_VERSION}\n`);
    return;
  }
  if (config.command === 'help') {
    io.stdout.write(USAGE);
    return;
  }

  // Before any credential store is made: the HTTP service never touches a
  // keychain, and its image does not need the native binding at all.
  if (config.http) {
    await startHttpServer(
      httpConfigFromEnv(io.env, { host: config.host, port: config.port, toolsets: config.toolsets }),
      { handleSignals: true },
    );
    return;
  }

  // Refused before any credential store, network call or prompt.
  if (config.command === 'approve') {
    assertApproveAllowed(config, { stdin: io.stdinIsTty, stdout: io.stdoutIsTty });
  }
  if (config.command === 'connect') {
    assertNonInteractiveConnectAllowed(config, io.stdinIsTty);
  }

  const store = storeFor(config, io);
  const session = new Session({
    baseUrl: config.baseUrl,
    store,
    accessLevel: config.accessLevel,
    dataScope: config.dataScope,
    ...(io.fetchImpl ? { fetchImpl: io.fetchImpl } : {}),
  });

  if (config.command === 'connect') {
    // The target is shown before anything sensitive is typed, so a redirected
    // base URL is noticed before the password goes to it.
    io.stdout.write(`FilingBridge at: ${config.baseUrl}\n`);
    const tenantSlug = config.tenantSlug ?? (await prompt(io, 'Workspace (tenant slug): ', false));
    const email = config.email ?? (await prompt(io, 'Email: ', false));
    const password = config.passwordStdin
      ? await readPasswordFromStdin(io.stdin as AsyncIterable<Buffer>)
      : await prompt(io, 'Password (not shown): ', true);
    const input = {
      tenantSlug,
      email,
      password,
      totpCode: config.totpCode,
      recoveryCode: config.recoveryCode,
      deviceLabel: config.deviceLabel ?? `filingbridge-mcp on ${process.platform}`,
    };
    let result;
    try {
      result = await session.login(input);
    } catch (error) {
      if (!(error instanceof SecondFactorRequiredError) || config.passwordStdin || !io.stdinIsTty || config.totpCode || config.recoveryCode) {
        throw error;
      }
      io.stdout.write('This account needs its authenticator code, or a saved recovery code.\n');
      const code = await prompt(io, 'Authenticator or recovery code (not shown): ', true);
      result = await session.login({
        ...input,
        ...(/^\d{6}$/.test(code.trim()) ? { totpCode: code.trim() } : { recoveryCode: code.trim() }),
      });
    }
    io.stdout.write(
      `Connected as ${result.user.displayName} <${result.user.email}> (${result.user.role})\n`
        + `Workspace: ${result.user.tenantName} (${result.user.tenantSlug})\n`
        + `Access level: ${String(result.session.accessLevel).toUpperCase()}\n`
        + `Personal data: ${String(result.session.dataScope).toUpperCase()}\n`
        + 'The connector stays connected until you run disconnect or revoke it in FilingBridge.\n',
    );
    return;
  }

  if (config.command === 'approve') {
    const preview = await session.describeApproval(config.approvalId!);
    io.stdout.write(formatApprovalPreview(preview, config.baseUrl));
    const state = approvalState(preview, new Date());
    if (state !== 'pending') {
      io.stdout.write(`${explainState(state)}\n`);
      return;
    }
    const password = await prompt(io, 'Password (not shown): ', true);
    const totpCode = (await prompt(io, 'Authenticator code (press Enter if two-factor sign-in is off): ', true)).trim();
    const outcome = await session.approve(config.approvalId!, password, totpCode);
    io.stdout.write(
      `Approved: ${outcome.summary ?? preview.summary}\n`
        + 'Ask the assistant to try the action again with exactly the same arguments plus '
        + `approvalId: ${config.approvalId}. The approval covers that one action and nothing else.\n`,
    );
    return;
  }

  if (config.command === 'disconnect') {
    await session.logout();
    io.stdout.write('Disconnected. The connector session was revoked in FilingBridge and the stored credential removed.\n');
    return;
  }

  if (config.command === 'status') {
    if (!store.read()) {
      io.stdout.write(`Not connected to ${config.baseUrl}. Run: filingbridge-mcp connect\n`);
      return;
    }
    const client = new ApiClient({
      tokens: new RefreshingSession(session),
      baseUrl: config.baseUrl,
      ...(io.fetchImpl ? { fetchImpl: io.fetchImpl } : {}),
    });
    const posture = await fetchSessionPosture(client);
    io.stdout.write(formatStatus(config.baseUrl, posture));
    return;
  }

  await startStdioServer({
    baseUrl: config.baseUrl,
    profile: config.profile,
    tokens: new RefreshingSession(session),
    toolsets: config.toolsets,
    downloadDir: config.downloadDir,
    uploadRoot: config.uploadRoot,
    verbose: config.verbose,
    ...(io.fetchImpl ? { fetchImpl: io.fetchImpl } : {}),
  });
}
