using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConfirmedNonTradingProfitOutsideExemption : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_tax_balances_confirmed_due",
                table: "tax_balances");

            migrationBuilder.AddColumn<decimal>(
                name: "ConfirmedNonTradingProfitOutsideExemption",
                table: "tax_balances",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_tax_balances_confirmed_due",
                table: "tax_balances",
                sql: "(\"ConfirmedDueAt\" IS NULL AND \"ConfirmedDueBy\" IS NULL AND \"ConfirmedDueReason\" IS NULL AND \"ConfirmedDueLiability\" IS NULL AND \"ConfirmedTaxableProfitOutsideExemption\" IS NULL AND \"ConfirmedNonTradingProfitOutsideExemption\" IS NULL) OR (\"TaxType\" = 'CorporationTax' AND \"ConfirmedDueAt\" IS NOT NULL AND char_length(btrim(COALESCE(\"ConfirmedDueBy\", ''))) > 0 AND char_length(btrim(COALESCE(\"ConfirmedDueReason\", ''))) > 0 AND \"ConfirmedDueLiability\" > 0 AND (\"ConfirmedTaxableProfitOutsideExemption\" IS NULL OR \"ConfirmedTaxableProfitOutsideExemption\" >= 0) AND (\"ConfirmedNonTradingProfitOutsideExemption\" IS NULL OR \"ConfirmedNonTradingProfitOutsideExemption\" >= 0))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Never drop an attested figure silently: refuse while any confirmation records one.
            migrationBuilder.Sql("""
                DO $refuse$
                BEGIN
                    IF EXISTS (SELECT 1 FROM tax_balances WHERE "ConfirmedNonTradingProfitOutsideExemption" IS NOT NULL) THEN
                        RAISE EXCEPTION 'tax_balances holds attested non-trading profit outside the charitable exemption; export it before rolling back this migration.'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_tax_balances_confirmed_due';
                    END IF;
                END
                $refuse$;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_tax_balances_confirmed_due",
                table: "tax_balances");

            migrationBuilder.DropColumn(
                name: "ConfirmedNonTradingProfitOutsideExemption",
                table: "tax_balances");

            migrationBuilder.AddCheckConstraint(
                name: "CK_tax_balances_confirmed_due",
                table: "tax_balances",
                sql: "(\"ConfirmedDueAt\" IS NULL AND \"ConfirmedDueBy\" IS NULL AND \"ConfirmedDueReason\" IS NULL AND \"ConfirmedDueLiability\" IS NULL AND \"ConfirmedTaxableProfitOutsideExemption\" IS NULL) OR (\"TaxType\" = 'CorporationTax' AND \"ConfirmedDueAt\" IS NOT NULL AND char_length(btrim(COALESCE(\"ConfirmedDueBy\", ''))) > 0 AND char_length(btrim(COALESCE(\"ConfirmedDueReason\", ''))) > 0 AND \"ConfirmedDueLiability\" > 0 AND (\"ConfirmedTaxableProfitOutsideExemption\" IS NULL OR \"ConfirmedTaxableProfitOutsideExemption\" >= 0))");
        }
    }
}
