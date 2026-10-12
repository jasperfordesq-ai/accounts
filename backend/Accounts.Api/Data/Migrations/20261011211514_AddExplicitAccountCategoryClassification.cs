using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExplicitAccountCategoryClassification : Migration
    {
        // Duplicate (CompanyId, Code) among active rows: today's engine silently picks the lowest Id
        // and the working papers throw, so the migration refuses instead of choosing.
        public const string PreflightSql = """
            DO $preflight$
            DECLARE duplicates text;
            BEGIN
                SELECT string_agg(
                           format('company %s code %s (%s rows)', COALESCE(d."CompanyId"::text, 'global'), d."Code", d.row_count),
                           '; ' ORDER BY d."CompanyId" NULLS FIRST, d."Code")
                INTO duplicates
                FROM (
                    SELECT "CompanyId", "Code", count(*) AS row_count
                    FROM account_categories
                    WHERE NOT "IsArchived"
                    GROUP BY "CompanyId", "Code"
                    HAVING count(*) > 1) AS d;
                IF duplicates IS NOT NULL THEN
                    RAISE EXCEPTION 'Explicit account classification cannot be added: duplicate account codes exist: %. Resolve the duplicates (re-code or merge) and re-run the migration; it does not guess which account is authoritative.', duplicates;
                END IF;
            END
            $preflight$;
            """;

        // Mirrors LegacyCategoryClassification.Derive exactly (parity-tested against it).
        public const string BackfillSql = """
            UPDATE account_categories SET
                "StatementLine" = CASE "Type"
                    WHEN 'Income' THEN CASE
                        WHEN left("Code", 1) = '4' AND NOT "IsNonTradingIncome" THEN 'Turnover'
                        ELSE 'OtherOperatingIncome' END
                    WHEN 'Expense' THEN CASE
                        WHEN left("Code", 1) = '5' THEN 'CostOfSales'
                        WHEN left("Code", 2) = '69' THEN 'InterestPayable'
                        WHEN left("Code", 1) = '8' THEN 'TaxOnProfit'
                        WHEN "Code" = '7000' THEN 'Depreciation'
                        ELSE 'AdministrativeExpenses' END
                    WHEN 'Asset' THEN CASE
                        WHEN left("Code", 2) = '00' THEN 'TangibleFixedAssets'
                        WHEN left("Code", 2) = '14' THEN 'CashAtBank'
                        WHEN "Code" = '1000' THEN 'Stocks'
                        WHEN "Code" = '1200' THEN 'Prepayments'
                        ELSE 'Debtors' END
                    WHEN 'Liability' THEN CASE
                        WHEN "Code" = '2700' THEN 'LoansAfterYear'
                        WHEN left("Code", 2) IN ('27', '29') THEN 'OtherCreditorsAfterYear'
                        WHEN "Code" = '2000' THEN 'TradeCreditors'
                        WHEN "Code" = '2100' THEN 'Accruals'
                        WHEN left("Code", 2) IN ('22', '23', '24') THEN 'TaxCreditors'
                        ELSE 'OtherCreditorsWithinYear' END
                    WHEN 'Equity' THEN CASE
                        WHEN "Code" = '3000' THEN 'ShareCapital'
                        WHEN "Code" = '3200' THEN 'DividendsPaid'
                        ELSE 'Reserves' END
                    END,
                "CashFlowClass" = CASE
                    WHEN "Type" = 'Asset' AND left("Code", 2) = '14' THEN 'Cash'
                    WHEN "Type" = 'Asset' AND left("Code", 2) = '00' THEN 'Investing'
                    WHEN left("Code", 2) = '24' OR left("Code", 1) = '8' THEN 'Tax'
                    WHEN "Code" IN ('2800', '3200') THEN 'Dividend'
                    WHEN left("Code", 2) IN ('26', '27') THEN 'Loan'
                    WHEN "Code" = '3000' THEN 'ShareIssue'
                    WHEN left("Code", 2) = '25' OR "Type" = 'Equity' THEN 'OtherFinancing'
                    ELSE 'Operating' END,
                "SystemRole" = CASE "Code"
                    WHEN '1400' THEN 'BankControl'
                    WHEN '3100' THEN 'RetainedEarnings'
                    WHEN '7000' THEN 'Depreciation'
                    WHEN '1200' THEN 'Prepayments'
                    WHEN '1000' THEN 'Stock'
                    WHEN '5000' THEN 'CostOfSalesStockMovement'
                    WHEN '2100' THEN 'Accruals'
                    WHEN '1100' THEN 'TradeDebtors'
                    WHEN '4000' THEN 'TradeDebtorIncome'
                    WHEN '2000' THEN 'TradeCreditors'
                    WHEN '2500' THEN 'DirectorLoanLiability'
                    WHEN '1150' THEN 'DirectorLoanReceivable'
                    WHEN '8000' THEN 'CorporationTaxCharge'
                    WHEN '2400' THEN 'CorporationTaxPayable'
                    WHEN '3200' THEN 'DividendsPaid'
                    WHEN '2800' THEN 'DividendsPayable'
                    WHEN '7050' THEN 'DisposalLoss'
                    WHEN '9000' THEN 'DisposalGain'
                    WHEN '2200' THEN 'VatPayable'
                    WHEN '1300' THEN 'VatReceivable'
                    END
            WHERE "StatementLine" IS NULL;
            """;

        // Fail closed after the backfill: every account must now have an explicit statement line and
        // cash-flow class (an unknown Type would leave the line NULL and the engine would fall back to
        // code rules for it).
        public const string UnclassifiedGuardSql = """
            DO $unclassified$
            DECLARE unclassified text;
            BEGIN
                SELECT string_agg(
                           format('company %s code %s (id %s, type %s)', COALESCE("CompanyId"::text, 'global'), "Code", "Id", "Type"),
                           '; ' ORDER BY "CompanyId" NULLS FIRST, "Code", "Id")
                INTO unclassified
                FROM account_categories
                WHERE "StatementLine" IS NULL OR "CashFlowClass" IS NULL;
                IF unclassified IS NOT NULL THEN
                    RAISE EXCEPTION 'Explicit account classification cannot be completed: accounts are still missing a statement line or cash-flow class after the backfill: %. Correct their account type and re-run the migration.', unclassified;
                END IF;
            END
            $unclassified$;
            """;

        // Run after the backfill, before the unique role index, so a conflict reports its accounts.
        public const string RoleConflictSql = """
            DO $roles$
            DECLARE conflicts text;
            BEGIN
                SELECT string_agg(
                           format('company %s role %s (%s rows)', COALESCE(d."CompanyId"::text, 'global'), d."SystemRole", d.row_count),
                           '; ' ORDER BY d."CompanyId" NULLS FIRST, d."SystemRole")
                INTO conflicts
                FROM (
                    SELECT "CompanyId", "SystemRole", count(*) AS row_count
                    FROM account_categories
                    WHERE "SystemRole" IS NOT NULL
                    GROUP BY "CompanyId", "SystemRole"
                    HAVING count(*) > 1) AS d;
                IF conflicts IS NOT NULL THEN
                    RAISE EXCEPTION 'Explicit account classification cannot be added: more than one account holds a system role: %. Resolve the duplicates and re-run the migration.', conflicts;
                END IF;
            END
            $roles$;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChartOfAccountsVariant",
                table: "companies",
                type: "text",
                nullable: false,
                defaultValue: "GenericTrading");

            migrationBuilder.AddColumn<string>(
                name: "CashFlowClass",
                table: "account_categories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "account_categories",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "StatementLine",
                table: "account_categories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SystemRole",
                table: "account_categories",
                type: "text",
                nullable: true);

            // Fail closed rather than guess which of two same-coded accounts is authoritative.
            migrationBuilder.Sql(PreflightSql);
            migrationBuilder.Sql(BackfillSql);
            migrationBuilder.Sql(UnclassifiedGuardSql);
            migrationBuilder.Sql(RoleConflictSql);

            migrationBuilder.CreateIndex(
                name: "IX_account_categories_CompanyId_SystemRole",
                table: "account_categories",
                columns: new[] { "CompanyId", "SystemRole" },
                unique: true,
                filter: "\"SystemRole\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_account_categories_CompanyId_SystemRole",
                table: "account_categories");

            migrationBuilder.DropColumn(
                name: "ChartOfAccountsVariant",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "CashFlowClass",
                table: "account_categories");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "account_categories");

            migrationBuilder.DropColumn(
                name: "StatementLine",
                table: "account_categories");

            migrationBuilder.DropColumn(
                name: "SystemRole",
                table: "account_categories");
        }
    }
}
