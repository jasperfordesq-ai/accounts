using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddYearEndReviewConfirmations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "year_end_review_confirmations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PeriodId = table.Column<int>(type: "integer", nullable: false),
                    SectionKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    ConfirmedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ConfirmedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_year_end_review_confirmations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_year_end_review_confirmations_accounting_periods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "accounting_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_year_end_review_confirmations_PeriodId_SectionKey",
                table: "year_end_review_confirmations",
                columns: new[] { "PeriodId", "SectionKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "year_end_review_confirmations");
        }
    }
}
