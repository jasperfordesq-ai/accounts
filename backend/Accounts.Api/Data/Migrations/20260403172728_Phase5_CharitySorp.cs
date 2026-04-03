using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase5_CharitySorp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "charity_infos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    CharityNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CharityType = table.Column<string>(type: "text", nullable: true),
                    GrossIncome = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    SorpTier = table.Column<int>(type: "integer", nullable: false),
                    CharitableObjectives = table.Column<string>(type: "text", nullable: true),
                    PrincipalActivities = table.Column<string>(type: "text", nullable: true),
                    GovernanceCodeCompliant = table.Column<bool>(type: "boolean", nullable: false),
                    GovernanceCodeNote = table.Column<string>(type: "text", nullable: true),
                    HasInternationalTransfers = table.Column<bool>(type: "boolean", nullable: false),
                    InternationalTransferDetails = table.Column<string>(type: "text", nullable: true),
                    TrusteeRemunerationPaid = table.Column<bool>(type: "boolean", nullable: false),
                    TrusteeRemunerationAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    TrusteeExpensesDetails = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_charity_infos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_charity_infos_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fund_balances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PeriodId = table.Column<int>(type: "integer", nullable: false),
                    FundName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    FundType = table.Column<string>(type: "text", nullable: false),
                    OpeningBalance = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    IncomingResources = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ResourcesExpended = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Transfers = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    GainsLosses = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ClosingBalance = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fund_balances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fund_balances_accounting_periods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "accounting_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_charity_infos_CompanyId",
                table: "charity_infos",
                column: "CompanyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fund_balances_PeriodId",
                table: "fund_balances",
                column: "PeriodId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "charity_infos");

            migrationBuilder.DropTable(
                name: "fund_balances");
        }
    }
}
