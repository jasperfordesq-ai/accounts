using Accounts.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Accounts.Api.Data.Migrations;

[DbContext(typeof(AccountsDbContext))]
[Migration("20260710210000_AddCompanyQuarantineEvidence")]
public partial class AddCompanyQuarantineEvidence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Keep the status columns and their consistency constraint in one ordered statement. This
        // avoids provider operation reordering placing the check ahead of one of its new columns.
        migrationBuilder.Sql("""
            ALTER TABLE companies
                ADD COLUMN "IsQuarantined" boolean NOT NULL DEFAULT FALSE,
                ADD COLUMN "QuarantinedAtUtc" timestamp with time zone NULL,
                ADD COLUMN "QuarantinedByUserId" character varying(320) NULL,
                ADD COLUMN "QuarantinedByDisplayName" character varying(200) NULL,
                ADD COLUMN "QuarantineReason" character varying(2000) NULL,
                ADD COLUMN "QuarantineEvidenceSha256" character varying(64) NULL;

            ALTER TABLE companies
                ADD CONSTRAINT "CK_companies_quarantine_evidence"
                CHECK ((NOT "IsQuarantined" AND "QuarantinedAtUtc" IS NULL AND "QuarantinedByUserId" IS NULL AND "QuarantinedByDisplayName" IS NULL AND "QuarantineReason" IS NULL AND "QuarantineEvidenceSha256" IS NULL)
                    OR ("IsQuarantined" AND "QuarantinedAtUtc" IS NOT NULL AND "QuarantinedByUserId" IS NOT NULL AND "QuarantinedByDisplayName" IS NOT NULL AND "QuarantineReason" IS NOT NULL AND "QuarantineEvidenceSha256" IS NOT NULL));
            """);

        migrationBuilder.CreateTable(
            name: "company_quarantine_events",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                CompanyId = table.Column<int>(type: "integer", nullable: false),
                TenantId = table.Column<int>(type: "integer", nullable: false),
                CompanyLegalName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                EventType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                ActorUserId = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                ActorDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                ActorRole = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                TypedConfirmation = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                InventoryJson = table.Column<string>(type: "jsonb", nullable: false),
                InventorySha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                TotalDependentRows = table.Column<long>(type: "bigint", nullable: false),
                PreviousEvidenceSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                EvidenceSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                RequestId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_company_quarantine_events", x => x.Id);
                table.CheckConstraint("CK_company_quarantine_events_hashes", "char_length(\"InventorySha256\") = 64 AND char_length(\"EvidenceSha256\") = 64 AND (\"PreviousEvidenceSha256\" IS NULL OR char_length(\"PreviousEvidenceSha256\") = 64)");
                table.CheckConstraint("CK_company_quarantine_events_reason", "char_length(\"Reason\") BETWEEN 20 AND 2000");
                table.CheckConstraint("CK_company_quarantine_events_type", "\"EventType\" IN ('Quarantined', 'Recovered')");
            });

        migrationBuilder.CreateIndex(
            name: "IX_company_quarantine_events_CompanyId_Id",
            table: "company_quarantine_events",
            columns: new[] { "CompanyId", "Id" });
        migrationBuilder.CreateIndex(
            name: "IX_company_quarantine_events_EvidenceSha256",
            table: "company_quarantine_events",
            column: "EvidenceSha256",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_company_quarantine_events_TenantId_OccurredAtUtc",
            table: "company_quarantine_events",
            columns: new[] { "TenantId", "OccurredAtUtc" });

        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION accounts_prevent_company_quarantine_event_mutation()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $function$
            BEGIN
                RAISE EXCEPTION 'Company quarantine evidence is append-only.'
                    USING ERRCODE = '23514', CONSTRAINT = 'CK_company_quarantine_events_immutable';
            END;
            $function$;

            CREATE TRIGGER "TR_company_quarantine_events_immutable"
                BEFORE UPDATE OR DELETE ON company_quarantine_events
                FOR EACH ROW EXECUTE FUNCTION accounts_prevent_company_quarantine_event_mutation();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS "TR_company_quarantine_events_immutable" ON company_quarantine_events;
            DROP FUNCTION IF EXISTS accounts_prevent_company_quarantine_event_mutation();
            """);
        migrationBuilder.DropTable(name: "company_quarantine_events");
        migrationBuilder.DropCheckConstraint(name: "CK_companies_quarantine_evidence", table: "companies");
        migrationBuilder.DropColumn(name: "IsQuarantined", table: "companies");
        migrationBuilder.DropColumn(name: "QuarantinedAtUtc", table: "companies");
        migrationBuilder.DropColumn(name: "QuarantinedByUserId", table: "companies");
        migrationBuilder.DropColumn(name: "QuarantinedByDisplayName", table: "companies");
        migrationBuilder.DropColumn(name: "QuarantineReason", table: "companies");
        migrationBuilder.DropColumn(name: "QuarantineEvidenceSha256", table: "companies");
    }
}
