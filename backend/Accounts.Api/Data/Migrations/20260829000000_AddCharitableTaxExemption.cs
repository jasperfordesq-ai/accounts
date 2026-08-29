using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCharitableTaxExemption : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HoldsCharitableTaxExemption",
                table: "companies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CharitableTaxExemptionReference",
                table: "companies",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "CharitableTaxExemptionConfirmedDate",
                table: "companies",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CharitableTaxExemptionConfirmedDate",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "CharitableTaxExemptionReference",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "HoldsCharitableTaxExemption",
                table: "companies");
        }
    }
}
