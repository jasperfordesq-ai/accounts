using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase1_ComplianceGaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "InterestCharged",
                table: "director_loans",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "InterestRate",
                table: "director_loans",
                type: "numeric(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "IsDocumented",
                table: "director_loans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LoanTerms",
                table: "director_loans",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxBalanceDuringYear",
                table: "director_loans",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "IsCharitableOrganisation",
                table: "companies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsCreditInstitution",
                table: "companies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsInsuranceUndertaking",
                table: "companies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsListedSecurities",
                table: "companies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsPensionFund",
                table: "companies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "MemberAuditNoticeDate",
                table: "accounting_periods",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MemberAuditNoticeReceived",
                table: "accounting_periods",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "filing_deadlines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    PeriodId = table.Column<int>(type: "integer", nullable: false),
                    DeadlineType = table.Column<string>(type: "text", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    FiledDate = table.Column<DateOnly>(type: "date", nullable: true),
                    IsLate = table.Column<bool>(type: "boolean", nullable: false),
                    PenaltyAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_filing_deadlines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_filing_deadlines_accounting_periods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "accounting_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_filing_deadlines_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "filing_histories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    PeriodId = table.Column<int>(type: "integer", nullable: true),
                    DeadlineType = table.Column<string>(type: "text", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    FiledDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DaysLate = table.Column<int>(type: "integer", nullable: false),
                    PenaltyAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_filing_histories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_filing_histories_accounting_periods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "accounting_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_filing_histories_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_filing_deadlines_CompanyId_PeriodId_DeadlineType",
                table: "filing_deadlines",
                columns: new[] { "CompanyId", "PeriodId", "DeadlineType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_filing_deadlines_PeriodId",
                table: "filing_deadlines",
                column: "PeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_filing_histories_CompanyId_DueDate",
                table: "filing_histories",
                columns: new[] { "CompanyId", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_filing_histories_PeriodId",
                table: "filing_histories",
                column: "PeriodId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "filing_deadlines");

            migrationBuilder.DropTable(
                name: "filing_histories");

            migrationBuilder.DropColumn(
                name: "InterestCharged",
                table: "director_loans");

            migrationBuilder.DropColumn(
                name: "InterestRate",
                table: "director_loans");

            migrationBuilder.DropColumn(
                name: "IsDocumented",
                table: "director_loans");

            migrationBuilder.DropColumn(
                name: "LoanTerms",
                table: "director_loans");

            migrationBuilder.DropColumn(
                name: "MaxBalanceDuringYear",
                table: "director_loans");

            migrationBuilder.DropColumn(
                name: "IsCharitableOrganisation",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "IsCreditInstitution",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "IsInsuranceUndertaking",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "IsListedSecurities",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "IsPensionFund",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "MemberAuditNoticeDate",
                table: "accounting_periods");

            migrationBuilder.DropColumn(
                name: "MemberAuditNoticeReceived",
                table: "accounting_periods");
        }
    }
}
