using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExactArdDeadlineEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A month-only value cannot be converted to an exact statutory ARD without inventing a
            // day. Retain it as non-operational migration evidence and require users to confirm the
            // exact date against CRO CORE before deadline calculation can run.
            migrationBuilder.RenameColumn(
                name: "ArdMonth",
                table: "companies",
                newName: "LegacyArdMonthUnverified");
            migrationBuilder.Sql("""
                ALTER TABLE companies
                ALTER COLUMN "LegacyArdMonthUnverified" DROP NOT NULL;
                """);

            migrationBuilder.AddColumn<DateOnly>(
                name: "AnnualReturnDate",
                table: "filing_deadlines",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AnnualReturnDateRecordId",
                table: "filing_deadlines",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "CalculatedDueDate",
                table: "filing_deadlines",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<string>(
                name: "CalculationFingerprintSha256",
                table: "filing_deadlines",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CalculationRuleVersion",
                table: "filing_deadlines",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CalculationSourceUrl",
                table: "filing_deadlines",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DeliveryDueDate",
                table: "filing_deadlines",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "FinancialStatementsLatestMadeUpToDate",
                table: "filing_deadlines",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MadeUpToDateBroughtForwardForAccountsAge",
                table: "filing_deadlines",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ManualOverrideAtUtc",
                table: "filing_deadlines",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManualOverrideByDisplayName",
                table: "filing_deadlines",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManualOverrideByUserId",
                table: "filing_deadlines",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManualOverrideCalculationFingerprintSha256",
                table: "filing_deadlines",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ManualOverrideDueDate",
                table: "filing_deadlines",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManualOverrideEvidenceReference",
                table: "filing_deadlines",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManualOverrideEvidenceSha256",
                table: "filing_deadlines",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManualOverrideReason",
                table: "filing_deadlines",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManualOverrideStatus",
                table: "filing_deadlines",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ReturnMadeUpToDate",
                table: "filing_deadlines",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "AnnualReturnDate",
                table: "companies",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "annual_return_date_records",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    PreviousAnnualReturnDate = table.Column<DateOnly>(type: "date", nullable: true),
                    AnnualReturnDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    EvidenceReference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    EvidenceSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ChangeReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RecordedByUserId = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    RecordedByDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_annual_return_date_records", x => x.Id);
                    table.CheckConstraint("CK_annual_return_date_records_change", "\"PreviousAnnualReturnDate\" IS NULL OR \"PreviousAnnualReturnDate\" <> \"AnnualReturnDate\"");
                    table.CheckConstraint("CK_annual_return_date_records_effective_date", "\"EffectiveFrom\" <= \"AnnualReturnDate\"");
                    table.CheckConstraint("CK_annual_return_date_records_manual_override", "\"Source\" <> 'ManualOverride' OR (\"EvidenceSha256\" IS NOT NULL AND length(\"ChangeReason\") >= 20)");
                    table.CheckConstraint("CK_annual_return_date_records_sha256", "length(\"RecordSha256\") = 64 AND (\"EvidenceSha256\" IS NULL OR length(\"EvidenceSha256\") = 64)");
                    table.ForeignKey(
                        name: "FK_annual_return_date_records_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Existing effective due dates remain usable as legacy evidence, but cannot pretend to
            // have the new source breakdown. Recalculation supplies the rule version/fingerprint.
            migrationBuilder.Sql("""
                UPDATE filing_deadlines
                SET "CalculatedDueDate" = "DueDate";

                ALTER TABLE filing_deadlines
                ALTER COLUMN "CalculatedDueDate" DROP DEFAULT;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_filing_deadlines_effective_due_date",
                table: "filing_deadlines",
                sql: "(\"ManualOverrideStatus\" = 'Active' AND \"ManualOverrideDueDate\" = \"DueDate\") OR (COALESCE(\"ManualOverrideStatus\", '') <> 'Active' AND \"CalculatedDueDate\" = \"DueDate\")");

            migrationBuilder.AddCheckConstraint(
                name: "CK_filing_deadlines_manual_override_evidence",
                table: "filing_deadlines",
                sql: "\"ManualOverrideStatus\" IS NULL OR (\"ManualOverrideDueDate\" IS NOT NULL AND \"ManualOverrideReason\" IS NOT NULL AND \"ManualOverrideEvidenceReference\" IS NOT NULL AND \"ManualOverrideEvidenceSha256\" IS NOT NULL AND \"ManualOverrideByUserId\" IS NOT NULL AND \"ManualOverrideByDisplayName\" IS NOT NULL AND \"ManualOverrideAtUtc\" IS NOT NULL AND \"ManualOverrideCalculationFingerprintSha256\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_filing_deadlines_manual_override_status",
                table: "filing_deadlines",
                sql: "\"ManualOverrideStatus\" IS NULL OR \"ManualOverrideStatus\" IN ('Active', 'NeedsReview')");

            migrationBuilder.CreateIndex(
                name: "IX_annual_return_date_records_CompanyId_AnnualReturnDate",
                table: "annual_return_date_records",
                columns: new[] { "CompanyId", "AnnualReturnDate" });

            migrationBuilder.CreateIndex(
                name: "IX_annual_return_date_records_CompanyId_RecordedAtUtc",
                table: "annual_return_date_records",
                columns: new[] { "CompanyId", "RecordedAtUtc" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "annual_return_date_records");

            migrationBuilder.DropCheckConstraint(
                name: "CK_filing_deadlines_effective_due_date",
                table: "filing_deadlines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_filing_deadlines_manual_override_evidence",
                table: "filing_deadlines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_filing_deadlines_manual_override_status",
                table: "filing_deadlines");

            migrationBuilder.DropColumn(
                name: "AnnualReturnDate",
                table: "filing_deadlines");

            migrationBuilder.DropColumn(
                name: "AnnualReturnDateRecordId",
                table: "filing_deadlines");

            migrationBuilder.DropColumn(
                name: "CalculatedDueDate",
                table: "filing_deadlines");

            migrationBuilder.DropColumn(
                name: "CalculationFingerprintSha256",
                table: "filing_deadlines");

            migrationBuilder.DropColumn(
                name: "CalculationRuleVersion",
                table: "filing_deadlines");

            migrationBuilder.DropColumn(
                name: "CalculationSourceUrl",
                table: "filing_deadlines");

            migrationBuilder.DropColumn(
                name: "DeliveryDueDate",
                table: "filing_deadlines");

            migrationBuilder.DropColumn(
                name: "FinancialStatementsLatestMadeUpToDate",
                table: "filing_deadlines");

            migrationBuilder.DropColumn(
                name: "MadeUpToDateBroughtForwardForAccountsAge",
                table: "filing_deadlines");

            migrationBuilder.DropColumn(
                name: "ManualOverrideAtUtc",
                table: "filing_deadlines");

            migrationBuilder.DropColumn(
                name: "ManualOverrideByDisplayName",
                table: "filing_deadlines");

            migrationBuilder.DropColumn(
                name: "ManualOverrideByUserId",
                table: "filing_deadlines");

            migrationBuilder.DropColumn(
                name: "ManualOverrideCalculationFingerprintSha256",
                table: "filing_deadlines");

            migrationBuilder.DropColumn(
                name: "ManualOverrideDueDate",
                table: "filing_deadlines");

            migrationBuilder.DropColumn(
                name: "ManualOverrideEvidenceReference",
                table: "filing_deadlines");

            migrationBuilder.DropColumn(
                name: "ManualOverrideEvidenceSha256",
                table: "filing_deadlines");

            migrationBuilder.DropColumn(
                name: "ManualOverrideReason",
                table: "filing_deadlines");

            migrationBuilder.DropColumn(
                name: "ManualOverrideStatus",
                table: "filing_deadlines");

            migrationBuilder.DropColumn(
                name: "ReturnMadeUpToDate",
                table: "filing_deadlines");

            migrationBuilder.DropColumn(
                name: "AnnualReturnDate",
                table: "companies");

            migrationBuilder.RenameColumn(
                name: "LegacyArdMonthUnverified",
                table: "companies",
                newName: "ArdMonth");
            migrationBuilder.Sql("""
                UPDATE companies SET "ArdMonth" = 0 WHERE "ArdMonth" IS NULL;
                ALTER TABLE companies ALTER COLUMN "ArdMonth" SET NOT NULL;
                """);
        }
    }
}
