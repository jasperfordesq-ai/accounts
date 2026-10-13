/**
 * Contract operations the connector deliberately does not expose, each with
 * the reason, and the rules that decide each tool's level and approval.
 *
 * "The connector cannot do that" must always be a decision somebody made and
 * wrote down. route-coverage.test.ts requires every operation in
 * backend/Accounts.Api/OpenApi/accounts-api-v1.json to be a tool, a core
 * route, or an entry here, so an operation added later forces the choice to
 * be made again.
 */
export interface ExcludedRoute {
  /** `METHOD /path`, the path exactly as the contract spells it. */
  route: string;
  reason: string;
}

const BROWSER_AUTH =
  'A browser sign-in route that issues or depends on the HTTP-only session cookie and CSRF token. The connector '
  + 'authenticates with its own bearer credential (filingbridge-mcp connect), and FilingBridge refuses these '
  + 'routes for connector sessions.';

const OAUTH_PLUMBING = 'OAuth authorization-server plumbing for remote clients; never a model tool';

export const EXCLUDED_ROUTES: readonly ExcludedRoute[] = [
  { route: 'POST /api/auth/login', reason: `${BROWSER_AUTH} The connector signs in through /api/auth/connector/login instead.` },
  { route: 'POST /api/auth/mfa/challenge', reason: `${BROWSER_AUTH} The connector passes the authenticator code at connect.` },
  { route: 'POST /api/auth/reauthenticate', reason: `${BROWSER_AUTH} Connector actions are confirmed per action by the approval flow instead of a recent-MFA window.` },
  {
    route: 'POST /api/auth/mfa/recovery-codes',
    reason: 'Regenerates the signed-in person\'s own MFA recovery codes. They are an account credential that must stay with the human in the web application, never pass through an agent.',
  },
  {
    route: 'POST /api/auth/invitations/accept',
    reason: 'Completes an invitation by setting the invitee\'s own password. Only the invitee may do that, in the browser.',
  },
  {
    route: 'POST /api/auth/recovery/complete',
    reason: 'Completes a password reset by setting the user\'s own new password. Only the user may do that, in the browser.',
  },
  { route: 'POST /api/auth/logout', reason: `${BROWSER_AUTH} filingbridge-mcp disconnect revokes the connector session instead.` },
  {
    route: 'POST /api/auth/password',
    reason: 'Changes the signed-in person\'s own password after proving the current one. An agent must never handle the person\'s password.',
  },
  { route: 'GET /api/auth/me', reason: `${BROWSER_AUTH} session_info reports the connector session\'s identity and posture instead.` },
  { route: 'GET /health/live', reason: 'A liveness probe for orchestrators. It says nothing about any company and advertises the deployment.' },
  { route: 'GET /health/ready', reason: 'A readiness probe for orchestrators (database, migrations, bootstrap). Infrastructure posture, not accounts data.' },
  { route: 'GET /health', reason: 'A legacy liveness probe for orchestrators. It says nothing about any company.' },
  {
    route: 'POST /api/system/monitoring/error-smoke',
    reason: 'Deliberately raises an error to prove external error tracking works during a release. An operator release step, never an agent action.',
  },
  {
    route: 'POST /api/system/monitoring/client-event',
    reason: 'Receives browser monitoring events from the web frontend. An agent has no browser events to report and must not inject them.',
  },

  // The connector credential's own plumbing. Driven by the CLI and by the
  // connector itself, never offered as tools: above all, an agent able to
  // call approve could grant its own approvals.
  { route: 'POST /api/auth/connector/login', reason: 'Connector credential plumbing; used by the CLI, never by the model. filingbridge-mcp connect sends the person\'s password and authenticator code here.' },
  { route: 'POST /api/auth/connector/refresh', reason: 'Connector credential plumbing; used by the CLI, never by the model. The connector rotates its single-use refresh token here under a cross-process lock.' },
  { route: 'POST /api/auth/connector/logout', reason: 'Connector credential plumbing; used by the CLI, never by the model. filingbridge-mcp disconnect revokes the session family here.' },
  { route: 'POST /api/auth/connector/approve', reason: 'Connector credential plumbing; used by the CLI, never by the model. Granting takes the person\'s password and authenticator code at a terminal (filingbridge-mcp approve) or on the web approvals page; a model able to call it could approve its own actions, so it must never be a tool.' },

  // The OAuth authorization server remote clients (Claude.ai, Claude Code,
  // Codex) use to obtain connector tokens for serve --http.
  { route: 'GET /api/oauth/metadata/authorization-server', reason: OAUTH_PLUMBING },
  { route: 'GET /api/oauth/metadata/protected-resource', reason: OAUTH_PLUMBING },
  { route: 'POST /api/oauth/register', reason: OAUTH_PLUMBING },
  { route: 'GET /api/oauth/authorize/describe', reason: OAUTH_PLUMBING },
  { route: 'POST /api/oauth/authorize', reason: OAUTH_PLUMBING },
  { route: 'POST /api/oauth/token', reason: OAUTH_PLUMBING },
  { route: 'POST /api/oauth/revoke', reason: OAUTH_PLUMBING },
];

/**
 * Contract routes served by the core tools rather than by a ToolDefinition,
 * because each core tool reshapes its route's answer.
 */
export const CORE_ROUTES: readonly { route: string; tool: string }[] = [
  { route: 'GET /api/search', tool: 'search' },
  { route: 'GET /api/auth/connector/session', tool: 'session_info' },
];

/**
 * Routes tools call that the committed contract does not describe. Empty
 * now: the CSV bank import was thought to be missing from the contract but
 * is described there (without a request body, because it is multipart), and
 * /api/search arrived with the connector backend. Kept so a route added
 * ahead of a contract regeneration has somewhere to be declared, with a
 * reason; the test fails once the contract catches up.
 */
export interface ExtraRoute {
  route: string;
  reason: string;
}

export const EXTRA_ROUTES: readonly ExtraRoute[] = [];

/** The method and path of an exclusion or extra route. */
export function splitRoute(route: string): { method: string; path: string } {
  const space = route.indexOf(' ');
  return { method: route.slice(0, space), path: route.slice(space + 1) };
}

/*
 * The routes FilingBridge asks a person to confirm before a connector may
 * perform them: every DELETE, plus the irreversible or privileged writes in
 * Services/PrivilegedRouteCatalog.RequiresHumanConfirmation. Mirrored here
 * literal for literal (mutating-route-coverage.test.ts parses the C#), so a
 * tool cannot quietly lose its approval marking.
 */

/** The connector's own routes never need an approval (approving one would deadlock). */
export const CONFIRM_EXEMPT_PREFIXES = ['/api/auth/connector'] as const;
/** Prefixes (segment-wise) whose every change needs confirmation. */
export const CONFIRM_PREFIXES = ['/api/privacy', '/api/admin/users', '/api/operations/deadline-reminders'] as const;
/** Exact paths whose change needs confirmation. */
export const CONFIRM_EXACT = ['/api/auth/mfa/recovery-codes'] as const;
/** Path endings (trailing slash ignored) whose change needs confirmation. */
export const CONFIRM_SUFFIXES = [
  '/recover',
  '/categories/chart-adoption',
  '/classification/historic-anchor',
  '/classification/historic-anchor/retire',
  '/tax-balances/CorporationTax/confirm-due',
  '/status',
  '/approve',
  '/mark-filed',
  '/revenue-external-validation',
  '/filing/directors-approval',
  '/filing/directors-approval-withdrawal',
  '/filing/cro-not-delivered',
  '/filing/cro-signing-pack',
  '/filing/auditor-report',
] as const;
/** Path fragments whose change needs confirmation. */
export const CONFIRM_CONTAINS = [
  '/quarantine',
  '/year-end-reviews/',
  '/external-filing-handoff/',
  '/release-evidence',
  '/accept',
] as const;

function startsWithSegments(path: string, prefix: string): boolean {
  const p = path.toLowerCase();
  const x = prefix.toLowerCase();
  return p === x || p.startsWith(`${x}/`);
}

export function requiresHumanConfirmation(method: string, path: string): boolean {
  const m = method.toUpperCase();
  if (CONFIRM_EXEMPT_PREFIXES.some((prefix) => startsWithSegments(path, prefix))) return false;
  if (m === 'DELETE') return startsWithSegments(path, '/api');
  if (m !== 'POST' && m !== 'PUT' && m !== 'PATCH') return false;
  if (CONFIRM_PREFIXES.some((prefix) => startsWithSegments(path, prefix))) return true;
  if (CONFIRM_EXACT.some((exact) => path.toLowerCase() === exact.toLowerCase())) return true;
  const value = path.replace(/\/+$/, '').toLowerCase();
  return CONFIRM_SUFFIXES.some((suffix) => value.endsWith(suffix.toLowerCase()))
    || CONFIRM_CONTAINS.some((fragment) => value.includes(fragment.toLowerCase()));
}

/*
 * The routes a connector session may use only at the Admin access level:
 * Services/PrivilegedRouteCatalog.IsAdminLevel, mirrored the same way.
 */
export const ADMIN_PREFIXES = [
  '/api/admin',
  '/api/privacy',
  '/api/operations',
  '/api/system/production-readiness',
  '/api/system/platform-metrics',
] as const;
export const ADMIN_EXACT_GET = ['/api/companies/quarantined'] as const;
export const ADMIN_EXACT_POST = ['/api/companies', '/api/companies/onboard'] as const;
export const ADMIN_POST_SUFFIXES = ['/audit-log/integrity/checkpoints'] as const;

export function isAdminLevel(method: string, path: string): boolean {
  const m = method.toUpperCase();
  if (requiresHumanConfirmation(m, path)) return true;
  if (ADMIN_PREFIXES.some((prefix) => startsWithSegments(path, prefix))) return true;
  const value = path.replace(/\/+$/, '').toLowerCase();
  if (m === 'GET' && ADMIN_EXACT_GET.some((exact) => value === exact)) return true;
  if (m === 'POST' && ADMIN_EXACT_POST.some((exact) => value === exact)) return true;
  if (m === 'POST' && ADMIN_POST_SUFFIXES.some((suffix) => value.endsWith(suffix))) return true;
  return false;
}

/** The session level a route needs: admin as above, otherwise read for a GET and write for a change. */
export function expectedLevel(method: string, path: string): 'read' | 'write' | 'admin' {
  if (isAdminLevel(method, path)) return 'admin';
  return method.toUpperCase() === 'GET' ? 'read' : 'write';
}

/** A route template with sample values in place of its parameters. */
export function concretePath(template: string): string {
  return template.replace(/\{([A-Za-z]+)\}/g, (_match, name: string) => {
    if (name === 'taxType') return 'CorporationTax';
    if (name === 'sectionKey') return 'debtors';
    if (name === 'snapshotId' || name === 'outboxId' || name === 'approvalId' || name === 'familyId') {
      return '00000000-0000-0000-0000-000000000001';
    }
    return '7';
  });
}
