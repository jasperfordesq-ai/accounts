# Database-enforced tenant isolation

## Security objective

EF Core query filters remain a useful application guard, but they are not the production security
boundary. PostgreSQL forced row-level security (RLS) must prevent the least-privileged API login
from reading or changing another firm's rows even when a query is missing its tenant predicate,
uses `IgnoreQueryFilters`, or is issued as defective raw SQL.

This control protects against application mistakes. It does not make a fully compromised API
process harmless: the process necessarily holds the context-signing key and application database
credential. Host, secret, dependency, and runtime hardening remain separate controls.

## Roles and credentials

Production uses distinct secret connection strings:

- the controlled migrate-only job connects as a migration administrator;
- the long-running API connects as the configured `accounts_api` login;
- `accounts_api_rls` is a `NOLOGIN`, `NOSUPERUSER`, `NOBYPASSRLS` group containing the API login;
- `accounts_migration_rls_admin` is a separate `NOLOGIN` group used only by the controlled
  migration/bootstrap identity.

The API login must not own a protected table or security-definer function, belong to the migration
group, create database/schema objects, read the protected context-key table, create roles or
databases, replicate, or bypass RLS. API startup fails if any of those conditions, any expected
policy, any forced-RLS flag, or any protected function is missing or unsafe.

## Signed connection context

Every pooled API connection is fail-closed when opened. The interceptor sets:

- `accounts.tenant_id`; and
- `accounts.tenant_signature`, an HMAC-SHA-256 over tenant ID and the PostgreSQL backend process ID.

`accounts_current_tenant_id()` is a `SECURITY DEFINER` function with a migration-captured safe
`search_path`. It returns a tenant only when the signature matches the independently provisioned
database key. Binding the signature to `pg_backend_pid()` prevents copying a context between pooled
connections. Both settings are cleared before a connection returns to the pool. Empty, malformed,
unsigned, stale-connection, and wrong-key contexts resolve to no tenant, which causes RLS to expose
no tenant rows.

The application and database receive the same high-entropy context key through separate protected
configuration paths. Rotation is a controlled migration operation: drain API instances, update the
database key through the migrate-only identity, update the application secret, restart, and require
the startup probe to pass before traffic resumes.

## Authentication bootstrap

Login and terminal token flows begin before an authenticated EF tenant exists. They may call only
narrow, parameterised security-definer functions:

- email to tenant ID for first-factor login;
- opaque invitation/password-reset hash and purpose to tenant ID;
- opaque MFA challenge hash to tenant ID;
- a global email-existence bit for Owner-only identity provisioning.

These functions never return passwords, salts, MFA material, tokens, names, roles, company data, or
financial data. A signed, unexpired session envelope supplies its tenant before the authoritative
user/session-version lookup. An invalid envelope never establishes a database context.

Unknown-login telemetry is the sole nullable-tenant exception: an anonymous request may insert a
minimised keyed-fingerprint rejection row, but cannot read, update, or delete it. Expired anonymous
rows are removed only through a fixed server-time cleanup function. Known-user events use normal
tenant policies.

## Ownership paths

`TenantIsolationPolicyCatalog` is the explicit inventory. A metadata test fails when a mapped
domain table is not classified or a removed table remains in the catalog. Supported paths are:

- tenant primary key or direct `TenantId`;
- authoritative company, accounting period, bank account, or director-loan parent;
- both user and company for assignment rows;
- global read-only account categories, with tenant-owned writes only; and
- nullable-tenant anonymous login telemetry with insert-only anonymous access.

Imported transactions deliberately follow their bank account, because a reviewed import may be
unassigned to a period while still belonging unambiguously to a company and tenant.

All protected tables use `ENABLE ROW LEVEL SECURITY` and `FORCE ROW LEVEL SECURITY`. The migration
revokes `PUBLIC` table privileges, grants only the required CRUD/sequence rights to the application
group, and installs exactly the expected application and migration policies. Startup rejects extra
policies as well as missing ones, because an additional permissive policy could weaken isolation.

## Scheduled jobs

Background work must never use a bypass credential. A narrow function enumerates tenant IDs only;
the worker creates a fresh dependency-injection scope and signed database context for one tenant at
a time. EF query filters and RLS then agree for planning, delivery, retry, privacy retention, and
idempotency cleanup. Cross-tenant names, recipient data, and financial values are not metrics or job
dimensions.

## Verification

The release gate uses a real PostgreSQL application login and proves:

- unfiltered raw SQL and `IgnoreQueryFilters` return only the selected tenant;
- guessed cross-tenant updates affect zero rows and inserts fail;
- a manually set tenant ID with a blank or forged signature returns no rows;
- the API cannot read the context key, disable RLS, assume the migration role, or create schema
  objects;
- anonymous bootstrap returns only the tenant ID and cannot read identity rows;
- nullable unknown-login telemetry is write-only to the anonymous context;
- all mapped tables, policies, forced-RLS flags, roles, functions and ownership are complete; and
- connection reuse does not inherit the previous request's context.

Fresh-schema and previous-release migration tests must run this gate with zero environment skips in
release CI. A failed database isolation verifier keeps readiness and the API unhealthy.
