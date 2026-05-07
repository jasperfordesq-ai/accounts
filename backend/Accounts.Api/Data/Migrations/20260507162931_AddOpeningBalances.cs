using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOpeningBalances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "opening_balances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PeriodId = table.Column<int>(type: "integer", nullable: false),
                    AccountCategoryId = table.Column<int>(type: "integer", nullable: false),
                    Debit = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Credit = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    SourceNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    EnteredBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    EnteredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reviewed = table.Column<bool>(type: "boolean", nullable: false),
                    ReviewedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_opening_balances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_opening_balances_account_categories_AccountCategoryId",
                        column: x => x.AccountCategoryId,
                        principalTable: "account_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_opening_balances_accounting_periods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "accounting_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_opening_balances_AccountCategoryId",
                table: "opening_balances",
                column: "AccountCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_opening_balances_PeriodId_AccountCategoryId",
                table: "opening_balances",
                columns: new[] { "PeriodId", "AccountCategoryId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "opening_balances");
        }
    }
}
