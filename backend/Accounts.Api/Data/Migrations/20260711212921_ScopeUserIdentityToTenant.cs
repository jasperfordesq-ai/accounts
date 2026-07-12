using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ScopeUserIdentityToTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_user_accounts_Email",
                table: "user_accounts");

            migrationBuilder.Sql("""
                DO $identity_preflight$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM user_accounts
                        GROUP BY "TenantId", lower(btrim("Email"))
                        HAVING count(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'Tenant-qualified identity migration found duplicate normalized email addresses within one tenant.';
                    END IF;
                END
                $identity_preflight$;

                UPDATE user_accounts
                SET "Email" = lower(btrim("Email"))
                WHERE "Email" IS DISTINCT FROM lower(btrim("Email"));
                """);

            migrationBuilder.CreateIndex(
                name: "IX_user_accounts_TenantId_Email",
                table: "user_accounts",
                columns: new[] { "TenantId", "Email" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_user_accounts_email_normalized",
                table: "user_accounts",
                sql: "\"Email\" = lower(btrim(\"Email\"))");

            migrationBuilder.Sql("""
                DROP FUNCTION IF EXISTS accounts_resolve_login_tenant(text);
                DROP FUNCTION IF EXISTS accounts_email_exists(text);

                CREATE OR REPLACE FUNCTION accounts_resolve_login_tenant(tenant_slug text, lookup_value text)
                RETURNS integer
                LANGUAGE sql
                STABLE
                SECURITY DEFINER
                SET search_path FROM CURRENT
                AS $resolver$
                    SELECT login_user."TenantId"
                    FROM user_accounts AS login_user
                    INNER JOIN tenants AS login_tenant ON login_tenant."Id" = login_user."TenantId"
                    WHERE login_tenant."Slug" = lower(btrim(tenant_slug))
                      AND login_user."Email" = lower(btrim(lookup_value))
                    LIMIT 1
                $resolver$;

                REVOKE ALL ON FUNCTION accounts_resolve_login_tenant(text, text) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION accounts_resolve_login_tenant(text, text) TO accounts_api_rls;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $identity_downgrade_preflight$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM user_accounts
                        GROUP BY "Email"
                        HAVING count(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'Cannot restore global email uniqueness while the same email belongs to more than one tenant.';
                    END IF;
                END
                $identity_downgrade_preflight$;
                """);

            migrationBuilder.DropIndex(
                name: "IX_user_accounts_TenantId_Email",
                table: "user_accounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_user_accounts_email_normalized",
                table: "user_accounts");

            migrationBuilder.CreateIndex(
                name: "IX_user_accounts_Email",
                table: "user_accounts",
                column: "Email",
                unique: true);

            migrationBuilder.Sql("""
                DROP FUNCTION IF EXISTS accounts_resolve_login_tenant(text, text);

                CREATE FUNCTION accounts_resolve_login_tenant(lookup_value text)
                RETURNS integer
                LANGUAGE sql
                STABLE
                SECURITY DEFINER
                SET search_path FROM CURRENT
                AS $resolver$
                    SELECT "TenantId" FROM user_accounts
                    WHERE lower("Email") = lower(lookup_value) AND "IsActive"
                    LIMIT 1
                $resolver$;

                CREATE FUNCTION accounts_email_exists(lookup_value text)
                RETURNS boolean
                LANGUAGE sql
                STABLE
                SECURITY DEFINER
                SET search_path FROM CURRENT
                AS $resolver$
                    SELECT EXISTS (SELECT 1 FROM user_accounts WHERE lower("Email") = lower(lookup_value))
                $resolver$;

                REVOKE ALL ON FUNCTION accounts_resolve_login_tenant(text) FROM PUBLIC;
                REVOKE ALL ON FUNCTION accounts_email_exists(text) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION accounts_resolve_login_tenant(text) TO accounts_api_rls;
                GRANT EXECUTE ON FUNCTION accounts_email_exists(text) TO accounts_api_rls;
                """);
        }
    }
}
