using System;
using Accounts.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AccountsDbContext))]
    [Migration("20260606230500_AddPeriodReopenMetadata")]
    public partial class AddPeriodReopenMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReopenedAt",
                table: "accounting_periods",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReopenedBy",
                table: "accounting_periods",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReopenReason",
                table: "accounting_periods",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReopenedAt",
                table: "accounting_periods");

            migrationBuilder.DropColumn(
                name: "ReopenedBy",
                table: "accounting_periods");

            migrationBuilder.DropColumn(
                name: "ReopenReason",
                table: "accounting_periods");
        }
    }
}
