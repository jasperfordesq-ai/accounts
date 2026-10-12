using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMcpConnectorSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "connector_action_approvals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    SessionFamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    RoutePattern = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ResourceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TargetCompanyId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConsumedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_connector_action_approvals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_connector_action_approvals_user_accounts_UserId",
                        column: x => x.UserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "connector_activity_events",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionFamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessLevel = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    DataScope = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    RoutePattern = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ResourceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TargetCompanyId = table.Column<int>(type: "integer", nullable: true),
                    StatusCode = table.Column<int>(type: "integer", nullable: false),
                    RequestId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_connector_activity_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_connector_activity_events_user_accounts_UserId",
                        column: x => x.UserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "connector_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyCreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClientKind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    AccessLevel = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    DataScope = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    RefreshTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PasswordLastChangedAtTicks = table.Column<long>(type: "bigint", nullable: false),
                    MfaVerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MfaMethod = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    DeviceLabel = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ClientVersion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevocationReason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    RotatedFromSessionId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_connector_sessions", x => x.Id);
                    table.CheckConstraint("CK_connector_sessions_access_level", "\"AccessLevel\" IN ('Read', 'Write', 'Admin')");
                    table.CheckConstraint("CK_connector_sessions_client_kind", "\"ClientKind\" IN ('McpConnector')");
                    table.CheckConstraint("CK_connector_sessions_data_scope", "\"DataScope\" IN ('Withheld', 'Full')");
                    table.ForeignKey(
                        name: "FK_connector_sessions_user_accounts_UserId",
                        column: x => x.UserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_connector_action_approvals_SessionFamilyId_RequestDigest",
                table: "connector_action_approvals",
                columns: new[] { "SessionFamilyId", "RequestDigest" },
                unique: true,
                filter: "\"ConsumedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_connector_action_approvals_TenantId_UserId_ExpiresAtUtc",
                table: "connector_action_approvals",
                columns: new[] { "TenantId", "UserId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_connector_action_approvals_UserId",
                table: "connector_action_approvals",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_connector_activity_events_TenantId_SessionFamilyId_Occurred~",
                table: "connector_activity_events",
                columns: new[] { "TenantId", "SessionFamilyId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_connector_activity_events_TenantId_UserId_OccurredAtUtc",
                table: "connector_activity_events",
                columns: new[] { "TenantId", "UserId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_connector_activity_events_UserId",
                table: "connector_activity_events",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_connector_sessions_RefreshTokenHash",
                table: "connector_sessions",
                column: "RefreshTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_connector_sessions_TenantId_FamilyId",
                table: "connector_sessions",
                columns: new[] { "TenantId", "FamilyId" });

            migrationBuilder.CreateIndex(
                name: "IX_connector_sessions_TenantId_UserId_RevokedAtUtc_ExpiresAtUtc",
                table: "connector_sessions",
                columns: new[] { "TenantId", "UserId", "RevokedAtUtc", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_connector_sessions_UserId",
                table: "connector_sessions",
                column: "UserId");

            // A connector session family's owner and posture can never be widened by rotation or
            // by any later write; only revocation/usage bookkeeping columns may change.
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

                CREATE TRIGGER "TR_connector_sessions_posture_immutable"
                    BEFORE UPDATE ON connector_sessions
                    FOR EACH ROW EXECUTE FUNCTION accounts_prevent_connector_posture_change();
                """);

            migrationBuilder.Sql(global::Accounts.Api.Data.TenantIsolationMigrationSql.BuildInstallTablesSql(
                global::Accounts.Api.Data.TenantIsolationMigrationSql.ConnectorPolicyInventory));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS "TR_connector_sessions_posture_immutable" ON connector_sessions;
                DROP FUNCTION IF EXISTS accounts_prevent_connector_posture_change();
                """);

            migrationBuilder.DropTable(
                name: "connector_action_approvals");

            migrationBuilder.DropTable(
                name: "connector_activity_events");

            migrationBuilder.DropTable(
                name: "connector_sessions");
        }
    }
}
