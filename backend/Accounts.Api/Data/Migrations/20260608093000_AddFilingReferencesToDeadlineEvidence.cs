using Accounts.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AccountsDbContext))]
    [Migration("20260608093000_AddFilingReferencesToDeadlineEvidence")]
    public partial class AddFilingReferencesToDeadlineEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FilingReference",
                table: "filing_histories",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FilingReference",
                table: "filing_deadlines",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FilingReference",
                table: "filing_histories");

            migrationBuilder.DropColumn(
                name: "FilingReference",
                table: "filing_deadlines");
        }
    }
}
