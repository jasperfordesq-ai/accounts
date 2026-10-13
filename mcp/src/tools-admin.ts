import type { ToolDefinition } from './tools.js';
import { APPROVAL_NOTE, NR } from './tool-helpers.js';
import {
  CREATE_USER_INPUT_FIELDS,
  ERASURE_REQUEST_FIELDS,
  INCIDENT_EXERCISE_INPUT_FIELDS,
  INVITE_USER_INPUT_FIELDS,
  SET_USER_ACTIVE_INPUT_FIELDS,
  SET_USER_COMPANIES_INPUT_FIELDS,
  SET_USER_ROLE_INPUT_FIELDS,
} from './bodies.js';

const USERS = '/api/admin/users';
const USER = `${USERS}/{userId}`;

const WITHHELD_USERS = ' Needs a session with full data scope.';

/** FilingBridge refuses user administration and privacy work outright to a Withheld session. */
const USERS_FULL_SCOPE = 'FilingBridge refuses user administration to a session that withholds personal data: every answer is about identifiable people.';
const PRIVACY_FULL_SCOPE = 'FilingBridge refuses privacy work to a session that withholds personal data: it is about identifiable people by definition.';

const CREDENTIAL =
  ' The result carries a credential for the new or reset user. Hand it to the person you are acting for '
  + 'straight away so they can pass it on over a channel they trust; do not repeat, store or reuse it.';

export const ADMIN_TOOLS: readonly ToolDefinition[] = [
  /* --- users ------------------------------------------------------------- */
  {
    name: 'user_list',
    group: 'admin_users',
    description:
      'Every user in this workspace with their role, company assignments, lock, MFA and lifecycle state. Owner only.'
      + WITHHELD_USERS,
    path: USERS,
    level: 'admin',
    model: 'UserAdministrationSummary',
    requiresFullScope: USERS_FULL_SCOPE,
  },
  {
    name: 'user_create',
    group: 'admin_users',
    description:
      'Create a user with a temporary password they must change at first sign-in. Prefer user_invite, which '
      + 'needs no password to be handled at all. Owner only.' + CREDENTIAL + APPROVAL_NOTE,
    method: 'POST',
    path: USERS,
    level: 'admin',
    approval: true,
    returnsCredential: true,
    body: CREATE_USER_INPUT_FIELDS,
    bodySchema: 'CreateUserInput',
    model: 'UserAdministrationSummary',
    requiresFullScope: USERS_FULL_SCOPE,
  },
  {
    name: 'user_invite',
    group: 'admin_users',
    description:
      'Invite a user: returns a single-use invitation token (with its expiry) for the person to send them. '
      + 'Owner only.' + CREDENTIAL + APPROVAL_NOTE,
    method: 'POST',
    path: `${USERS}/invite`,
    level: 'admin',
    approval: true,
    returnsCredential: true,
    body: INVITE_USER_INPUT_FIELDS,
    bodySchema: 'InviteUserInput',
    model: 'UserProvisioningResult',
    requiresFullScope: USERS_FULL_SCOPE,
  },
  {
    name: 'user_active_set',
    group: 'admin_users',
    description: 'Deactivate a user (revoking their sessions and connectors) or reactivate them. Owner only.' + WITHHELD_USERS + APPROVAL_NOTE,
    method: 'PUT',
    path: `${USER}/active`,
    level: 'admin',
    approval: true,
    body: SET_USER_ACTIVE_INPUT_FIELDS,
    bodySchema: 'SetUserActiveInput',
    model: 'UserAdministrationSummary',
    requiresFullScope: USERS_FULL_SCOPE,
  },
  {
    name: 'user_unlock',
    group: 'admin_users',
    description: 'Clear a user\'s sign-in lockout. Owner only.' + WITHHELD_USERS + APPROVAL_NOTE,
    method: 'POST',
    path: `${USER}/unlock`,
    level: 'admin',
    approval: true,
    model: 'UserAdministrationSummary',
    requiresFullScope: USERS_FULL_SCOPE,
  },
  {
    name: 'user_password_reset',
    group: 'admin_users',
    description:
      'Begin a password reset: returns a single-use reset token (with its expiry) and revokes the user\'s '
      + 'sessions. Owner only.' + CREDENTIAL + APPROVAL_NOTE,
    method: 'POST',
    path: `${USER}/password-reset`,
    level: 'admin',
    approval: true,
    returnsCredential: true,
    model: 'UserProvisioningResult',
    requiresFullScope: USERS_FULL_SCOPE,
  },
  {
    name: 'user_role_set',
    group: 'admin_users',
    description: 'Change a user\'s role (Owner, Accountant, Reviewer or Client). Their sessions are revoked. Owner only.' + WITHHELD_USERS + APPROVAL_NOTE,
    method: 'PUT',
    path: `${USER}/role`,
    level: 'admin',
    approval: true,
    body: SET_USER_ROLE_INPUT_FIELDS,
    bodySchema: 'SetUserRoleInput',
    model: 'UserAdministrationSummary',
    requiresFullScope: USERS_FULL_SCOPE,
  },
  {
    name: 'user_companies_set',
    group: 'admin_users',
    description: 'Replace the list of companies a user may access. Owner only.' + WITHHELD_USERS + APPROVAL_NOTE,
    method: 'PUT',
    path: `${USER}/companies`,
    level: 'admin',
    approval: true,
    body: SET_USER_COMPANIES_INPUT_FIELDS,
    bodySchema: 'SetUserCompaniesInput',
    model: 'UserAdministrationSummary',
    requiresFullScope: USERS_FULL_SCOPE,
  },
  {
    name: 'user_sessions_revoke',
    group: 'admin_users',
    description: 'Sign a user out everywhere: browser sessions and connector sessions alike. Owner only.' + WITHHELD_USERS + APPROVAL_NOTE,
    method: 'POST',
    path: `${USER}/revoke-sessions`,
    level: 'admin',
    approval: true,
    model: 'UserAdministrationSummary',
    requiresFullScope: USERS_FULL_SCOPE,
  },
  {
    name: 'user_offboard',
    group: 'admin_users',
    description: 'Offboard a user permanently: deactivated, sessions and connectors revoked, assignments removed. Owner only.' + WITHHELD_USERS + APPROVAL_NOTE,
    method: 'POST',
    path: `${USER}/offboard`,
    level: 'admin',
    approval: true,
    model: 'UserAdministrationSummary',
    requiresFullScope: USERS_FULL_SCOPE,
  },

  /* --- privacy ----------------------------------------------------------- */
  {
    name: 'privacy_policy',
    group: 'privacy',
    description: 'The workspace\'s privacy governance policy: retention periods, legal holds and the subject-rights process. Owner only.',
    path: '/api/privacy/policy',
    level: 'admin',
    noRecordsBecause: NR.reference,
    requiresFullScope: PRIVACY_FULL_SCOPE,
  },
  {
    name: 'privacy_access_export',
    group: 'privacy',
    description:
      'Build a GDPR subject access export for one user and save it into --download-dir; the export itself never '
      + 'enters this conversation. Owner only.' + APPROVAL_NOTE,
    method: 'POST',
    path: '/api/privacy/subjects/{subjectUserId}/access-export',
    level: 'admin',
    approval: true,
    binary: { kind: 'json', defaultName: 'subject-access-export.json' },
    requiresFullScope: 'A subject access export is all of one person\'s personal data.',
    localOnly: 'Over HTTP the export would be returned into this conversation, and it must never enter it.',
  },
  {
    name: 'privacy_erasure_execute',
    group: 'privacy',
    description:
      'Carry out an approved erasure of one user\'s personal data, subject to legal holds and statutory retention. '
      + 'Irreversible. Owner only.' + APPROVAL_NOTE,
    method: 'POST',
    path: '/api/privacy/subjects/{subjectUserId}/approved-erasure',
    level: 'admin',
    approval: true,
    body: ERASURE_REQUEST_FIELDS,
    bodySchema: 'ErasureRequest',
    noRecordsBecause: 'An erasure decision: identifiers, outcome and retained-evidence counts only.',
    requiresFullScope: PRIVACY_FULL_SCOPE,
  },
  {
    name: 'privacy_retention_run',
    group: 'privacy',
    description: 'Run the retention policy now, minimising records past their retention period. Owner only.' + APPROVAL_NOTE,
    method: 'POST',
    path: '/api/privacy/retention/run',
    level: 'admin',
    approval: true,
    noRecordsBecause: 'Counts of records minimised; no records about individuals.',
    requiresFullScope: PRIVACY_FULL_SCOPE,
  },
  {
    name: 'privacy_incident_exercise_record',
    group: 'privacy',
    description: 'Record the evidence of a completed security or privacy incident exercise. Owner only.' + APPROVAL_NOTE,
    method: 'POST',
    path: '/api/privacy/incident-exercises',
    level: 'admin',
    approval: true,
    body: INCIDENT_EXERCISE_INPUT_FIELDS,
    bodySchema: 'IncidentExerciseInput',
    noRecordsBecause: 'Exercise timings, digests and decisions; no records about individuals.',
    requiresFullScope: PRIVACY_FULL_SCOPE,
  },

  /* --- operations -------------------------------------------------------- */
  {
    name: 'deadline_risk_queue',
    group: 'operations',
    description: 'Every company\'s deadlines ranked by risk, for the people who run this workspace. Owner only.',
    path: '/api/operations/deadline-risk',
    level: 'admin',
    noRecordsBecause: NR.deadlines,
  },
  {
    name: 'deadline_reminders_run',
    group: 'operations',
    description: 'Run the scheduled deadline reminder delivery now. Owner only.' + APPROVAL_NOTE,
    method: 'POST',
    path: '/api/operations/deadline-reminders/run',
    level: 'admin',
    approval: true,
    noRecordsBecause: NR.operations,
  },
  {
    name: 'deadline_reminder_retry',
    group: 'operations',
    description: 'Retry one failed deadline reminder from the operator queue. Owner only.' + APPROVAL_NOTE,
    method: 'POST',
    path: '/api/operations/deadline-reminders/{outboxId}/retry',
    params: [{ kind: 'guid', name: 'outboxId', describe: 'The queued reminder, as the operator queue lists it.' }],
    level: 'admin',
    approval: true,
    noRecordsBecause: NR.operations,
  },

  /* --- system ------------------------------------------------------------ */
  {
    name: 'production_readiness',
    group: 'system',
    description: 'The production-readiness evidence report for this deployment: release gates and their evidence. Restricted.',
    path: '/api/system/production-readiness',
    level: 'admin',
    noRecordsBecause: NR.operations,
  },
  {
    name: 'platform_metrics',
    group: 'system',
    description: 'PII-free operational signals: request rates, error rates and queue depths.',
    path: '/api/system/platform-metrics',
    level: 'admin',
    noRecordsBecause: NR.operations,
  },
];
