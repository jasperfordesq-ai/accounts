using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase2_InterrogationEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "GoingConcernConfirmed",
                table: "accounting_periods",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "GoingConcernNote",
                table: "accounting_periods",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "contingent_liabilities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PeriodId = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Nature = table.Column<string>(type: "text", nullable: false),
                    EstimatedAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Likelihood = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contingent_liabilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_contingent_liabilities_accounting_periods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "accounting_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "post_balance_sheet_events",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PeriodId = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    EventDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsAdjusting = table.Column<bool>(type: "boolean", nullable: false),
                    FinancialImpact = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    ActionRequired = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_post_balance_sheet_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_post_balance_sheet_events_accounting_periods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "accounting_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "related_party_transactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PeriodId = table.Column<int>(type: "integer", nullable: false),
                    PartyName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Relationship = table.Column<string>(type: "text", nullable: false),
                    TransactionType = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    BalanceOwed = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Terms = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_related_party_transactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_related_party_transactions_accounting_periods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "accounting_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_contingent_liabilities_PeriodId",
                table: "contingent_liabilities",
                column: "PeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_post_balance_sheet_events_PeriodId",
                table: "post_balance_sheet_events",
                column: "PeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_related_party_transactions_PeriodId",
                table: "related_party_transactions",
                column: "PeriodId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contingent_liabilities");

            migrationBuilder.DropTable(
                name: "post_balance_sheet_events");

            migrationBuilder.DropTable(
                name: "related_party_transactions");

            migrationBuilder.DropColumn(
                name: "GoingConcernConfirmed",
                table: "accounting_periods");

            migrationBuilder.DropColumn(
                name: "GoingConcernNote",
                table: "accounting_periods");
        }
    }
}
