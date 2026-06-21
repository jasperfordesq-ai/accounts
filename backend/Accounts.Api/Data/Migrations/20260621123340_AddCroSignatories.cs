using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCroSignatories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SignedAt",
                table: "cro_filing_packages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignedByDirector",
                table: "cro_filing_packages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignedBySecretary",
                table: "cro_filing_packages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignedPdfPath",
                table: "cro_filing_packages",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SignedAt",
                table: "cro_filing_packages");

            migrationBuilder.DropColumn(
                name: "SignedByDirector",
                table: "cro_filing_packages");

            migrationBuilder.DropColumn(
                name: "SignedBySecretary",
                table: "cro_filing_packages");

            migrationBuilder.DropColumn(
                name: "SignedPdfPath",
                table: "cro_filing_packages");
        }
    }
}
