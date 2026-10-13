using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class BindOAuthClientAndCodeSessionVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SessionVersion",
                table: "oauth_authorization_codes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ClientIdHash",
                table: "connector_sessions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            // The issuing OAuth client is part of an agent's immutable posture: rotation copies it and
            // nothing may change it afterwards.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION accounts_prevent_connector_posture_change()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    IF NEW."TenantId" IS DISTINCT FROM OLD."TenantId"
                        OR NEW."UserId" IS DISTINCT FROM OLD."UserId"
                        OR NEW."FamilyId" IS DISTINCT FROM OLD."FamilyId"
                        OR NEW."AccessLevel" IS DISTINCT FROM OLD."AccessLevel"
                        OR NEW."DataScope" IS DISTINCT FROM OLD."DataScope"
                        OR NEW."ClientKind" IS DISTINCT FROM OLD."ClientKind"
                        OR NEW."ClientIdHash" IS DISTINCT FROM OLD."ClientIdHash"
                        OR NEW."RefreshTokenHash" IS DISTINCT FROM OLD."RefreshTokenHash" THEN
                        RAISE EXCEPTION 'Connector session posture is immutable.'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_connector_sessions_posture_immutable';
                    END IF;
                    RETURN NEW;
                END;
                $function$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION accounts_prevent_connector_posture_change()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    IF NEW."TenantId" IS DISTINCT FROM OLD."TenantId"
                        OR NEW."UserId" IS DISTINCT FROM OLD."UserId"
                        OR NEW."FamilyId" IS DISTINCT FROM OLD."FamilyId"
                        OR NEW."AccessLevel" IS DISTINCT FROM OLD."AccessLevel"
                        OR NEW."DataScope" IS DISTINCT FROM OLD."DataScope"
                        OR NEW."ClientKind" IS DISTINCT FROM OLD."ClientKind"
                        OR NEW."RefreshTokenHash" IS DISTINCT FROM OLD."RefreshTokenHash" THEN
                        RAISE EXCEPTION 'Connector session posture is immutable.'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_connector_sessions_posture_immutable';
                    END IF;
                    RETURN NEW;
                END;
                $function$;
                """);

            migrationBuilder.DropColumn(
                name: "SessionVersion",
                table: "oauth_authorization_codes");

            migrationBuilder.DropColumn(
                name: "ClientIdHash",
                table: "connector_sessions");
        }
    }
}
