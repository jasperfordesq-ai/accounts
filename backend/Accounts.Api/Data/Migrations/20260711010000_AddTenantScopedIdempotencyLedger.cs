using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantScopedIdempotencyLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "idempotency_records",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Operation = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    RequestFingerprintSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    CreatedByDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResultResourceType = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    ResultResourceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ResultHttpStatusCode = table.Column<int>(type: "integer", nullable: true),
                    ResponseJson = table.Column<string>(type: "text", nullable: true),
                    ResponseSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_records", x => x.Id);
                    table.CheckConstraint("CK_idempotency_records_expiry", "\"ExpiresAtUtc\" > \"StartedAtUtc\" AND (\"CompletedAtUtc\" IS NULL OR \"ExpiresAtUtc\" > \"CompletedAtUtc\")");
                    table.CheckConstraint("CK_idempotency_records_hashes", "\"RequestFingerprintSha256\" ~ '^[0-9a-f]{64}$' AND (\"ResponseSha256\" IS NULL OR \"ResponseSha256\" ~ '^[0-9a-f]{64}$')");
                    table.CheckConstraint("CK_idempotency_records_key", "char_length(\"IdempotencyKey\") BETWEEN 8 AND 128 AND \"IdempotencyKey\" ~ '^[A-Za-z0-9._:-]+$'");
                    table.CheckConstraint("CK_idempotency_records_status", "(\"Status\" = 'InProgress' AND \"CompletedAtUtc\" IS NULL AND \"ResultResourceType\" IS NULL AND \"ResultResourceId\" IS NULL AND \"ResultHttpStatusCode\" IS NULL AND \"ResponseJson\" IS NULL AND \"ResponseSha256\" IS NULL) OR (\"Status\" = 'Completed' AND \"CompletedAtUtc\" IS NOT NULL AND \"ResultResourceType\" IS NOT NULL AND \"ResultResourceId\" IS NOT NULL AND \"ResultHttpStatusCode\" BETWEEN 100 AND 599 AND \"ResponseJson\" IS NOT NULL AND \"ResponseSha256\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_idempotency_records_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_records_ExpiresAtUtc",
                table: "idempotency_records",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_records_TenantId_IdempotencyKey",
                table: "idempotency_records",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_records_TenantId_Operation_CompletedAtUtc",
                table: "idempotency_records",
                columns: new[] { "TenantId", "Operation", "CompletedAtUtc" });

            // Preserve unexpired one-off onboarding keys in the generic tenant ledger. The generic
            // service intentionally retains the established onboarding request-fingerprint
            // canonicalisation, so the response remains replayable after an in-place upgrade.
            migrationBuilder.Sql("""
                INSERT INTO idempotency_records (
                    "TenantId",
                    "IdempotencyKey",
                    "Operation",
                    "RequestFingerprintSha256",
                    "Status",
                    "CreatedByUserId",
                    "CreatedByDisplayName",
                    "StartedAtUtc",
                    "CompletedAtUtc",
                    "ExpiresAtUtc",
                    "ResultResourceType",
                    "ResultResourceId",
                    "ResultHttpStatusCode",
                    "ResponseJson",
                    "ResponseSha256")
                SELECT
                    legacy."TenantId",
                    legacy."IdempotencyKey",
                    'company.onboard.v1',
                    lower(legacy."RequestSha256"),
                    'Completed',
                    legacy."CreatedByUserId",
                    legacy."CreatedByDisplayName",
                    legacy."StartedAtUtc",
                    legacy."CompletedAtUtc",
                    GREATEST(
                        legacy."CompletedAtUtc" + INTERVAL '30 days',
                        CURRENT_TIMESTAMP + INTERVAL '30 days'),
                    'Company',
                    legacy."CompanyId"::text,
                    201,
                    legacy."ResponseJson",
                    lower(legacy."ResponseSha256")
                FROM company_onboarding_requests AS legacy
                WHERE legacy."Status" = 'Completed'
                  AND legacy."CompletedAtUtc" IS NOT NULL
                  AND legacy."CompanyId" IS NOT NULL
                  AND legacy."ResponseJson" IS NOT NULL
                  AND legacy."ResponseSha256" IS NOT NULL
                ON CONFLICT ("TenantId", "IdempotencyKey") DO NOTHING;
                """);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION accounts_protect_idempotency_record()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    IF TG_OP = 'INSERT' AND NEW."Status" = 'InProgress' THEN
                        RETURN NEW;
                    END IF;

                    IF TG_OP = 'UPDATE'
                       AND OLD."Status" = 'InProgress'
                       AND NEW."Status" = 'Completed'
                       AND NEW."Id" = OLD."Id"
                       AND NEW."TenantId" = OLD."TenantId"
                       AND NEW."IdempotencyKey" = OLD."IdempotencyKey"
                       AND NEW."Operation" = OLD."Operation"
                       AND NEW."RequestFingerprintSha256" = OLD."RequestFingerprintSha256"
                       AND NEW."CreatedByUserId" = OLD."CreatedByUserId"
                       AND NEW."CreatedByDisplayName" = OLD."CreatedByDisplayName"
                       AND NEW."StartedAtUtc" = OLD."StartedAtUtc"
                    THEN
                        RETURN NEW;
                    END IF;

                    IF TG_OP = 'DELETE' AND OLD."ExpiresAtUtc" <= CURRENT_TIMESTAMP THEN
                        RETURN OLD;
                    END IF;

                    RAISE EXCEPTION 'Idempotency evidence is immutable until its retention expiry.'
                        USING ERRCODE = '23514', CONSTRAINT = 'CK_idempotency_records_immutable';
                END;
                $function$;

                CREATE TRIGGER "TR_idempotency_records_immutable"
                    BEFORE INSERT OR UPDATE OR DELETE ON idempotency_records
                    FOR EACH ROW EXECUTE FUNCTION accounts_protect_idempotency_record();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS "TR_idempotency_records_immutable" ON idempotency_records;
                DROP FUNCTION IF EXISTS accounts_protect_idempotency_record();
                """);

            migrationBuilder.DropTable(
                name: "idempotency_records");
        }
    }
}
