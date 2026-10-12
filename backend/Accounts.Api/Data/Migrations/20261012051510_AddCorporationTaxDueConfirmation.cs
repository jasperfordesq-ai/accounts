using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCorporationTaxDueConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ConfirmedDueAt",
                table: "tax_balances",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConfirmedDueBy",
                table: "tax_balances",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConfirmedDueReason",
                table: "tax_balances",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_tax_balances_confirmed_due",
                table: "tax_balances",
                sql: "\"ConfirmedDueAt\" IS NULL OR (char_length(btrim(COALESCE(\"ConfirmedDueBy\", ''))) > 0 AND char_length(btrim(COALESCE(\"ConfirmedDueReason\", ''))) > 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_tax_balances_confirmed_due",
                table: "tax_balances");

            migrationBuilder.DropColumn(
                name: "ConfirmedDueAt",
                table: "tax_balances");

            migrationBuilder.DropColumn(
                name: "ConfirmedDueBy",
                table: "tax_balances");

            migrationBuilder.DropColumn(
                name: "ConfirmedDueReason",
                table: "tax_balances");
        }
    }
}
