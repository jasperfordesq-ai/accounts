# Firm Login and Tenant Access Control Design

## Context

The Irish Accounts platform already has important production hardening in place:

- API-key middleware protects `/api` routes and supports scoped company access.
- Production startup guardrails block unsafe demo seeding, automatic migrations, development database passwords, localhost origins, and disabled API access in production.
- Security headers, rate limiting, period ownership checks, and period lock checks are present.
- The current dirty worktree adds `Tenant`, `UserAccount`, `Company.TenantId`, a tenant/user migration, and demo user seeding with PBKDF2 password hashes.

The remaining production-readiness gap for Slice 1 is end-user identity. The frontend currently exposes a manual "Reviewer Identity" field and states that the deployment still needs authenticated users, firm roles, and tenant access controls. This slice turns the existing tenant/user groundwork into enforced application access.

## Goal

Build a firm staff login system that creates an authenticated user session, scopes all company data to the signed-in user's tenant, enforces role permissions for sensitive actions, and uses the signed-in user identity for review, approval, finalisation, and audit evidence.

## Non-Goals

- Do not add external identity providers, OAuth, SAML, passkeys, or multi-factor authentication in this slice.
- Do not build full user administration screens in this slice.
- Do not replace the API-key middleware. It remains the service-to-service guard between the frontend and backend.
- Do not redesign the statutory accounting workflow or filing calculations.
- Do not attempt a full production certification of the whole platform in this slice.

## Roles

Use the roles already represented in seed data, normalized to application permissions:

- `Owner`: full tenant administration and all accounting actions.
- `Accountant`: create and edit companies, periods, working papers, classifications, notes, and filing preparation data.
- `Reviewer`: read all tenant data, approve adjustments, confirm year-end reviews, finalise periods, and update filing workflow status.
- `Client`: read assigned tenant data and download outputs, but cannot mutate accounting or filing state.

For implementation, these roles should map to a compact permission model rather than scattering string comparisons through handlers.

## Backend Design

### Authentication

Add an `AuthService` responsible for:

- Looking up active `UserAccount` records by normalized email.
- Verifying PBKDF2-SHA256 password hashes using the stored salt, hash, and algorithm.
- Rejecting inactive users and users with unsupported password algorithms.
- Producing a safe session principal with `UserId`, `TenantId`, `Email`, `DisplayName`, and `Role`.

Add auth endpoints:

- `POST /api/auth/login`: accepts email and password, verifies credentials, and sets an HTTP-only session cookie.
- `POST /api/auth/logout`: clears the session cookie.
- `GET /api/auth/me`: returns the current signed-in user and tenant context.

The session cookie should be HTTP-only, same-site strict or lax, secure in production, and have a bounded lifetime. Session contents must be signed and should not expose password data.

### Request Principal

Add authentication middleware after API-key middleware and before endpoint handlers. It should:

- Allow unauthenticated access only to health and auth login.
- Require a valid session for all other `/api` routes.
- Load the current active user from the database.
- Store the authenticated principal in `HttpContext.Items` for endpoint and service use.
- Return `401` for missing, invalid, expired, inactive, or unsupported sessions.

### Tenant Access

Add tenant access middleware for company-scoped API routes:

- Parse `/api/companies/{companyId}` routes.
- Verify the company exists in the current user's tenant.
- Return `404` instead of `403` for cross-tenant company IDs to avoid disclosing other tenants' records.
- For new company creation, stamp `Company.TenantId` from the current user.
- Keep existing period ownership middleware for period-to-company consistency.

Company list endpoints must only return companies where `Company.TenantId == principal.TenantId`.

### Authorization

Add a role authorization helper that centralizes permissions:

- Safe reads: all signed-in roles.
- Company creation/update: `Owner`, `Accountant`.
- Company deletion: `Owner`.
- Working-paper edits: `Owner`, `Accountant`.
- Review confirmations, adjustment approvals, period finalise/reopen, filing workflow status changes: `Owner`, `Reviewer`.
- Document and filing output downloads: all signed-in roles.

Rejected authenticated requests should return `403` with a concise JSON error.

### Audit and Reviewer Identity

Where the API currently accepts reviewer names from headers or request bodies for audit-sensitive actions, prefer the authenticated user:

- `AuditLog.UserId` should receive the signed-in user's email or stable user identifier.
- Review confirmations should use `principal.DisplayName`.
- Adjustment approvals should use `principal.DisplayName`.
- Period locks should use `principal.DisplayName`.
- Filing workflow actions should use `principal.DisplayName`.

If an existing endpoint must keep a request field for compatibility, the server should ignore it for authenticated identity decisions.

### Production Safety

Extend production safety validation so production cannot start when:

- Session signing configuration is missing or too weak.
- Cookie security settings are unsafe.
- Demo seeded users are enabled outside deliberate demo mode.

Existing API-key production checks remain in force.

## Frontend Design

### Session Flow

Add a login page at `/login` with email and password fields. On successful login:

- Redirect to the dashboard.
- Load `/api/auth/me`.
- Store user session state in a React auth provider.

Add logout from the navbar. Logout clears the backend session and returns to `/login`.

### Route Guarding

The app shell should protect all application pages except `/login`:

- Unknown session: show a compact loading state.
- Unauthenticated session: redirect to `/login`.
- Authenticated session: render the current page.

The proxy route at `frontend/src/app/api/[...path]/route.ts` continues to attach the server-side `ACCOUNTS_API_KEY` and forwards browser cookies to the backend.

### UI Identity and Permissions

Update the navbar to show:

- Tenant or firm name when available.
- User display name.
- Role.
- Logout action.

Remove the manual "Reviewer Identity" warning panel from the period workspace. Use the authenticated user's display name for reviewer-sensitive API calls.

Disable or hide role-ineligible commands in the frontend, while keeping backend authorization as the source of truth.

## Data Model

Use the existing dirty-worktree model as the starting point:

- `Tenant`
- `UserAccount`
- `Company.TenantId`

Implementation may add small fields if needed for session and normalization, such as normalized email or tenant display metadata, but should avoid broad account-management modeling in this slice.

## Tests

Backend tests must cover:

- Valid login returns a session principal.
- Wrong password is rejected.
- Inactive user is rejected.
- Missing session cannot access `/api/companies`.
- Signed-in user sees only companies in their tenant.
- Cross-tenant company access returns `404`.
- Client role cannot mutate accounting data.
- Reviewer role can approve/review/finalise but cannot create companies.
- Audit-sensitive actions use the authenticated principal instead of caller-supplied names.
- Production safety rejects missing or weak session signing configuration.

Frontend verification must include:

- `npm run lint`
- `npm run build`

Backend verification must include:

- `dotnet test`

## Acceptance Criteria

Slice 1 is complete when:

- A seeded demo user can sign in through `/login`.
- The dashboard and company pages require authentication.
- Company lists and company detail routes are tenant-scoped.
- Cross-tenant company IDs are not disclosed.
- Role-denied writes fail on the backend even if the frontend tries to call them.
- Review, approval, finalise, and filing workflow actions record the signed-in user identity.
- The manual reviewer identity warning is removed from the period workspace.
- Backend tests, frontend lint, and frontend production build pass.

## Risks

- Many existing endpoints accept `companyId` and `periodId` directly. Tenant enforcement should be centralized wherever possible to avoid missing handlers.
- Some services operate only on `periodId`. Existing period ownership middleware must remain active so tenant checks and period checks work together.
- Existing dirty tenant/user migration and seed work may need small corrections, but it should not be reverted.
- This slice does not make the full platform production ready by itself; it removes the largest current access-control gap and creates a foundation for subsequent production-hardening slices.
