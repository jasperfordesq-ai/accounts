using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCorporationTaxDueConfirmedLiability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_tax_balances_confirmed_due",
                table: "tax_balances");

            migrationBuilder.AddColumn<decimal>(
                name: "ConfirmedDueLiability",
                table: "tax_balances",
                type: "numeric(18,2)",
                nullable: true);

            // A confirmation made before the amount was recorded was given for the liability it still
            // carries (saving a different liability already cleared it).
            migrationBuilder.Sql("UPDATE tax_balances SET \"ConfirmedDueLiability\" = \"Liability\" WHERE \"ConfirmedDueAt\" IS NOT NULL;");

            migrationBuilder.AddCheckConstraint(
                name: "CK_tax_balances_confirmed_due",
                table: "tax_balances",
                sql: "(\"ConfirmedDueAt\" IS NULL AND \"ConfirmedDueBy\" IS NULL AND \"ConfirmedDueReason\" IS NULL AND \"ConfirmedDueLiability\" IS NULL) OR (\"TaxType\" = 'CorporationTax' AND \"ConfirmedDueAt\" IS NOT NULL AND char_length(btrim(COALESCE(\"ConfirmedDueBy\", ''))) > 0 AND char_length(btrim(COALESCE(\"ConfirmedDueReason\", ''))) > 0 AND \"ConfirmedDueLiability\" > 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_tax_balances_confirmed_due",
                table: "tax_balances");

            migrationBuilder.DropColumn(
                name: "ConfirmedDueLiability",
                table: "tax_balances");

            migrationBuilder.AddCheckConstraint(
                name: "CK_tax_balances_confirmed_due",
                table: "tax_balances",
                sql: "\"ConfirmedDueAt\" IS NULL OR (char_length(btrim(COALESCE(\"ConfirmedDueBy\", ''))) > 0 AND char_length(btrim(COALESCE(\"ConfirmedDueReason\", ''))) > 0)");
        }
    }
}
