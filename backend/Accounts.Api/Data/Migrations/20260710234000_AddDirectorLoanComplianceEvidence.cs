using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDirectorLoanComplianceEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "DirectorId",
                table: "director_loans",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<decimal>(
                name: "AllowanceMade",
                table: "director_loans",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ArrangementDate",
                table: "director_loans",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArrangementType",
                table: "director_loans",
                type: "text",
                nullable: false,
                defaultValue: "Loan");

            migrationBuilder.AddColumn<string>(
                name: "ComplianceBasis",
                table: "director_loans",
                type: "text",
                nullable: false,
                defaultValue: "Unassessed");

            migrationBuilder.AddColumn<string>(
                name: "CounterpartyName",
                table: "director_loans",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CounterpartyType",
                table: "director_loans",
                type: "text",
                nullable: false,
                defaultValue: "Director");

            migrationBuilder.AddColumn<string>(
                name: "ExceptionEvidenceReference",
                table: "director_loans",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExpenseDischargedDate",
                table: "director_loans",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExpenseIncurredDate",
                table: "director_loans",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NoMoreFavourableTermsConfirmed",
                table: "director_loans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "NoPriorFinancialStatementsConfirmed",
                table: "director_loans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "OrdinaryCourseConfirmed",
                table: "director_loans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "RelevantAssetsAmount",
                table: "director_loans",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "RelevantAssetsAsOfDate",
                table: "director_loans",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RelevantAssetsBasis",
                table: "director_loans",
                type: "text",
                nullable: false,
                defaultValue: "Unassessed");

            migrationBuilder.AddColumn<string>(
                name: "RelevantAssetsFallReview",
                table: "director_loans",
                type: "text",
                nullable: false,
                defaultValue: "Unassessed");

            migrationBuilder.AddColumn<DateOnly>(
                name: "RelevantAssetsReductionAwarenessDate",
                table: "director_loans",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RelevantAssetsReference",
                table: "director_loans",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewDecision",
                table: "director_loans",
                type: "text",
                nullable: false,
                defaultValue: "Unreviewed");

            migrationBuilder.AddColumn<string>(
                name: "ReviewNote",
                table: "director_loans",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAtUtc",
                table: "director_loans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedBy",
                table: "director_loans",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewerRole",
                table: "director_loans",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "SapActivityStartDate",
                table: "director_loans",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "SapCroFilingDate",
                table: "director_loans",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SapCroFilingReference",
                table: "director_loans",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SapDeclarationCoversSection203Matters",
                table: "director_loans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "SapDeclarationDate",
                table: "director_loans",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SapDeclarationReference",
                table: "director_loans",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "SapResolutionDate",
                table: "director_loans",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SapResolutionReference",
                table: "director_loans",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Section236PresumptionEvidenceReference",
                table: "director_loans",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "TermsAmendedDate",
                table: "director_loans",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TermsAmendmentEvidenceReference",
                table: "director_loans",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TermsStatus",
                table: "director_loans",
                type: "text",
                nullable: false,
                defaultValue: "Unassessed");

            migrationBuilder.CreateTable(
                name: "director_loan_movements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DirectorLoanId = table.Column<int>(type: "integer", nullable: false),
                    MovementDate = table.Column<DateOnly>(type: "date", nullable: false),
                    MovementType = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    EvidenceReference = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_director_loan_movements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_director_loan_movements_director_loans_DirectorLoanId",
                        column: x => x.DirectorLoanId,
                        principalTable: "director_loans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_director_loan_movements_DirectorLoanId_MovementDate_Id",
                table: "director_loan_movements",
                columns: new[] { "DirectorLoanId", "MovementDate", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "director_loan_movements");

            migrationBuilder.DropColumn(
                name: "AllowanceMade",
                table: "director_loans");

            migrationBuilder.DropColumn(
                name: "ArrangementDate",
                table: "director_loans");

            migrationBuilder.DropColumn(
                name: "ArrangementType",
                table: "director_loans");

            migrationBuilder.DropColumn(
                name: "ComplianceBasis",
                table: "director_loans");

            migrationBuilder.DropColumn(
                name: "CounterpartyName",
                table: "director_loans");

            migrationBuilder.DropColumn(
                name: "CounterpartyType",
                table: "director_loans");

            migrationBuilder.DropColumn(
                name: "ExceptionEvidenceReference",
                table: "director_loans");

            migrationBuilder.DropColumn(
                name: "ExpenseDischargedDate",
                table: "director_loans");

            migrationBuilder.DropColumn(
                name: "ExpenseIncurredDate",
                table: "director_loans");

            migrationBuilder.DropColumn(
                name: "NoMoreFavourableTermsConfirmed",
                table: "director_loans");

            migrationBuilder.DropColumn(
                name: "NoPriorFinancialStatementsConfirmed",
                table: "director_loans");

            migrationBuilder.DropColumn(
                name: "OrdinaryCourseConfirmed",
                table: "director_loans");

            migrationBuilder.DropColumn(
                name: "RelevantAssetsAmount",
                table: "director_loans");

            migrationBuilder.DropColumn(
                name: "RelevantAssetsAsOfDate",
                table: "director_loans");

            migrationBuilder.DropColumn(
                name: "RelevantAssetsBasis",
                table: "director_loans");

            migrationBuilder.DropColumn(
                name: "RelevantAssetsFallReview",
                table: "director_loans");

            migrationBuilder.DropColumn(
                name: "RelevantAssetsReductionAwarenessDate",
                table: "director_loans");

            migrationBuilder.DropColumn(
                name: "RelevantAssetsReference",
                table: "director_loans");

            migrationBuilder.DropColumn(
                name: "ReviewDecision",
                table: "director_loans");

            migrationBuilder.DropColumn(
                name: "ReviewNote",
                table: "director_loans");

            migrationBuilder.DropColumn(
                name: "ReviewedAtUtc",
                table: "director_loans");

            migrationBuilder.DropColumn(
                name: "ReviewedBy",
                table: "director_loans");

            migrationBuilder.DropColumn(
                name: "ReviewerRole",
                table: "director_loans");

            migrationBuilder.DropColumn(
                name: "SapActivityStartDate",
                table: "director_loans");

            migrationBuilder.DropColumn(
                name: "SapCroFilingDate",
                table: "director_loans");

            migrationBuilder.DropColumn(
                name: "SapCroFilingReference",
                table: "director_loans");

            migrationBuilder.DropColumn(
                name: "SapDeclarationCoversSection203Matters",
                table: "director_loans");

            migrationBuilder.DropColumn(
                name: "SapDeclarationDate",
                table: "director_loans");

            migrationBuilder.DropColumn(
                name: "SapDeclarationReference",
                table: "director_loans");

            migrationBuilder.DropColumn(
                name: "SapResolutionDate",
                table: "director_loans");

            migrationBuilder.DropColumn(
                name: "SapResolutionReference",
                table: "director_loans");

            migrationBuilder.DropColumn(
                name: "Section236PresumptionEvidenceReference",
                table: "director_loans");

            migrationBuilder.DropColumn(
                name: "TermsAmendedDate",
                table: "director_loans");

            migrationBuilder.DropColumn(
                name: "TermsAmendmentEvidenceReference",
                table: "director_loans");

            migrationBuilder.DropColumn(
                name: "TermsStatus",
                table: "director_loans");

            migrationBuilder.AlterColumn<int>(
                name: "DirectorId",
                table: "director_loans",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
