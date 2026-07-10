using Accounts.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Accounts.Api.Data.Migrations;

[DbContext(typeof(AccountsDbContext))]
[Migration("20260710230000_AddAtomicCompanyOnboarding")]
public partial class AddAtomicCompanyOnboarding : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "company_onboarding_requests",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                TenantId = table.Column<int>(type: "integer", nullable: false),
                IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                RequestSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                CompanyId = table.Column<int>(type: "integer", nullable: true),
                PeriodId = table.Column<int>(type: "integer", nullable: true),
                BankAccountId = table.Column<int>(type: "integer", nullable: true),
                CategoryCount = table.Column<int>(type: "integer", nullable: false),
                CreatedByUserId = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                CreatedByDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ResponseJson = table.Column<string>(type: "text", nullable: true),
                ResponseSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_company_onboarding_requests", x => x.Id);
                table.CheckConstraint("CK_company_onboarding_requests_category_count", "\"CategoryCount\" >= 0");
                table.CheckConstraint("CK_company_onboarding_requests_hashes", "char_length(\"RequestSha256\") = 64 AND (\"ResponseSha256\" IS NULL OR char_length(\"ResponseSha256\") = 64)");
                table.CheckConstraint("CK_company_onboarding_requests_status", "(\"Status\" = 'InProgress' AND \"CompanyId\" IS NULL AND \"PeriodId\" IS NULL AND \"BankAccountId\" IS NULL AND \"CompletedAtUtc\" IS NULL AND \"ResponseJson\" IS NULL AND \"ResponseSha256\" IS NULL) OR (\"Status\" = 'Completed' AND \"CompanyId\" IS NOT NULL AND \"PeriodId\" IS NOT NULL AND \"BankAccountId\" IS NOT NULL AND \"CompletedAtUtc\" IS NOT NULL AND \"ResponseJson\" IS NOT NULL AND \"ResponseSha256\" IS NOT NULL)");
            });

        migrationBuilder.CreateIndex(
            name: "IX_company_onboarding_requests_CompanyId",
            table: "company_onboarding_requests",
            column: "CompanyId");
        migrationBuilder.CreateIndex(
            name: "IX_company_onboarding_requests_RequestSha256",
            table: "company_onboarding_requests",
            column: "RequestSha256");
        migrationBuilder.CreateIndex(
            name: "IX_company_onboarding_requests_TenantId_IdempotencyKey",
            table: "company_onboarding_requests",
            columns: new[] { "TenantId", "IdempotencyKey" },
            unique: true);

        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION accounts_protect_company_onboarding_request()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $function$
            BEGIN
                IF TG_OP = 'UPDATE'
                   AND OLD."Status" = 'InProgress'
                   AND NEW."Status" = 'Completed'
                   AND NEW."TenantId" = OLD."TenantId"
                   AND NEW."IdempotencyKey" = OLD."IdempotencyKey"
                   AND NEW."RequestSha256" = OLD."RequestSha256"
                   AND NEW."CreatedByUserId" = OLD."CreatedByUserId"
                   AND NEW."CreatedByDisplayName" = OLD."CreatedByDisplayName"
                   AND NEW."StartedAtUtc" = OLD."StartedAtUtc"
                THEN
                    RETURN NEW;
                END IF;

                RAISE EXCEPTION 'Company onboarding idempotency evidence is immutable.'
                    USING ERRCODE = '23514', CONSTRAINT = 'CK_company_onboarding_requests_immutable';
            END;
            $function$;

            CREATE TRIGGER "TR_company_onboarding_requests_immutable"
                BEFORE UPDATE OR DELETE ON company_onboarding_requests
                FOR EACH ROW EXECUTE FUNCTION accounts_protect_company_onboarding_request();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS "TR_company_onboarding_requests_immutable" ON company_onboarding_requests;
            DROP FUNCTION IF EXISTS accounts_protect_company_onboarding_request();
            """);
        migrationBuilder.DropTable(name: "company_onboarding_requests");
    }
}
