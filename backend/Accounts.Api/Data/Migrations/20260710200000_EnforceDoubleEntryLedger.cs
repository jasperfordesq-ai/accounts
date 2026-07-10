using Accounts.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Api.Data.Migrations;

/// <inheritdoc />
[DbContext(typeof(AccountsDbContext))]
[Migration("20260710200000_EnforceDoubleEntryLedger")]
public partial class EnforceDoubleEntryLedger : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "ResidualValue",
            table: "fixed_assets",
            type: "decimal(18,2)",
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddCheckConstraint(
            name: "CK_fixed_assets_residual_value",
            table: "fixed_assets",
            sql: "\"ResidualValue\" >= 0 AND \"ResidualValue\" <= \"Cost\"");

        // Remove the historic zero-value retained-earnings marker. It was never a journal and is now
        // replaced by deterministic ledger carry-forward/closing logic.
        migrationBuilder.Sql(
            """
            DELETE FROM adjustments
            WHERE "Amount" = 0
              AND "DebitCategoryId" IS NULL
              AND "CreditCategoryId" IS NULL
              AND "Description" LIKE 'Retained earnings%';

            DELETE FROM adjustments
            WHERE "DebitCategoryId" IS NULL
              AND "CreditCategoryId" IS NULL
              AND "Description" = 'Entertainment add-back marker';

            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM adjustments
                    WHERE "Amount" <= 0
                       OR "DebitCategoryId" IS NULL
                       OR "CreditCategoryId" IS NULL
                       OR "DebitCategoryId" = "CreditCategoryId") THEN
                    RAISE EXCEPTION 'Existing adjustments contain invalid one-sided, non-positive, or same-account journals; remediate them before migration.';
                END IF;
            END
            $$;

            UPDATE adjustments AS journal
            SET "ImpactOnProfit" =
                    (CASE WHEN debit."Type" IN ('Income', 'Expense') THEN -journal."Amount" ELSE 0 END)
                  + (CASE WHEN credit."Type" IN ('Income', 'Expense') THEN journal."Amount" ELSE 0 END),
                "ImpactOnAssets" =
                    (CASE WHEN debit."Type" = 'Asset' THEN journal."Amount" ELSE 0 END)
                  + (CASE WHEN credit."Type" = 'Asset' THEN -journal."Amount" ELSE 0 END)
            FROM account_categories AS debit, account_categories AS credit
            WHERE debit."Id" = journal."DebitCategoryId"
              AND credit."Id" = journal."CreditCategoryId";
            """);

        migrationBuilder.DropForeignKey(
            name: "FK_adjustments_account_categories_CreditCategoryId",
            table: "adjustments");
        migrationBuilder.DropForeignKey(
            name: "FK_adjustments_account_categories_DebitCategoryId",
            table: "adjustments");

        migrationBuilder.AlterColumn<int>(
            name: "DebitCategoryId",
            table: "adjustments",
            type: "integer",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "integer",
            oldNullable: true);
        migrationBuilder.AlterColumn<int>(
            name: "CreditCategoryId",
            table: "adjustments",
            type: "integer",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "integer",
            oldNullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_adjustments_positive_amount",
            table: "adjustments",
            sql: "\"Amount\" > 0");
        migrationBuilder.AddCheckConstraint(
            name: "CK_adjustments_distinct_accounts",
            table: "adjustments",
            sql: "\"DebitCategoryId\" <> \"CreditCategoryId\"");

        migrationBuilder.AddForeignKey(
            name: "FK_adjustments_account_categories_CreditCategoryId",
            table: "adjustments",
            column: "CreditCategoryId",
            principalTable: "account_categories",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(
            name: "FK_adjustments_account_categories_DebitCategoryId",
            table: "adjustments",
            column: "DebitCategoryId",
            principalTable: "account_categories",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_adjustments_distinct_accounts",
            table: "adjustments");
        migrationBuilder.DropCheckConstraint(
            name: "CK_adjustments_positive_amount",
            table: "adjustments");
        migrationBuilder.DropForeignKey(
            name: "FK_adjustments_account_categories_CreditCategoryId",
            table: "adjustments");
        migrationBuilder.DropForeignKey(
            name: "FK_adjustments_account_categories_DebitCategoryId",
            table: "adjustments");

        migrationBuilder.AlterColumn<int>(
            name: "DebitCategoryId",
            table: "adjustments",
            type: "integer",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "integer");
        migrationBuilder.AlterColumn<int>(
            name: "CreditCategoryId",
            table: "adjustments",
            type: "integer",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "integer");

        migrationBuilder.AddForeignKey(
            name: "FK_adjustments_account_categories_CreditCategoryId",
            table: "adjustments",
            column: "CreditCategoryId",
            principalTable: "account_categories",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
        migrationBuilder.AddForeignKey(
            name: "FK_adjustments_account_categories_DebitCategoryId",
            table: "adjustments",
            column: "DebitCategoryId",
            principalTable: "account_categories",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.DropCheckConstraint(
            name: "CK_fixed_assets_residual_value",
            table: "fixed_assets");
        migrationBuilder.DropColumn(
            name: "ResidualValue",
            table: "fixed_assets");
    }
}
