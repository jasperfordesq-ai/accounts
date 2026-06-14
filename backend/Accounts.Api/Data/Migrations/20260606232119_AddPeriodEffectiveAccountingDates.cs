using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPeriodEffectiveAccountingDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_share_capitals_CompanyId",
                table: "share_capitals");

            migrationBuilder.DropIndex(
                name: "IX_loans_CompanyId",
                table: "loans");

            migrationBuilder.AddColumn<DateOnly>(
                name: "CancelledDate",
                table: "share_capitals",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "IssueDate",
                table: "share_capitals",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "BalanceAsOfDate",
                table: "loans",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DrawdownDate",
                table: "loans",
                type: "date",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE bank_accounts AS b
                SET "OpeningBalanceDate" = COALESCE(
                    (SELECT MIN(p."PeriodStart") FROM accounting_periods AS p WHERE p."CompanyId" = b."CompanyId"),
                    c."IncorporationDate")
                FROM companies AS c
                WHERE b."CompanyId" = c."Id"
                    AND b."OpeningBalance" <> 0
                    AND b."OpeningBalanceDate" IS NULL;

                UPDATE loans AS l
                SET "DrawdownDate" = COALESCE(
                        l."DrawdownDate",
                        c."IncorporationDate",
                        (SELECT MIN(p."PeriodStart") FROM accounting_periods AS p WHERE p."CompanyId" = l."CompanyId")),
                    "BalanceAsOfDate" = COALESCE(
                        l."BalanceAsOfDate",
                        (SELECT MAX(p."PeriodEnd") FROM accounting_periods AS p WHERE p."CompanyId" = l."CompanyId"),
                        c."IncorporationDate")
                FROM companies AS c
                WHERE l."CompanyId" = c."Id"
                    AND (l."DrawdownDate" IS NULL OR l."BalanceAsOfDate" IS NULL);

                UPDATE share_capitals AS s
                SET "IssueDate" = COALESCE(
                    s."IssueDate",
                    c."IncorporationDate",
                    (SELECT MIN(p."PeriodStart") FROM accounting_periods AS p WHERE p."CompanyId" = s."CompanyId"))
                FROM companies AS c
                WHERE s."CompanyId" = c."Id"
                    AND s."IssueDate" IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_share_capitals_CompanyId_IssueDate",
                table: "share_capitals",
                columns: new[] { "CompanyId", "IssueDate" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_share_capitals_cancelled_after_issue",
                table: "share_capitals",
                sql: "\"CancelledDate\" IS NULL OR \"CancelledDate\" >= \"IssueDate\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_share_capitals_issue_date_required",
                table: "share_capitals",
                sql: "\"IssueDate\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_loans_CompanyId_BalanceAsOfDate",
                table: "loans",
                columns: new[] { "CompanyId", "BalanceAsOfDate" });

            migrationBuilder.CreateIndex(
                name: "IX_loans_CompanyId_DrawdownDate",
                table: "loans",
                columns: new[] { "CompanyId", "DrawdownDate" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_loans_period_effective_dates_required",
                table: "loans",
                sql: "\"DrawdownDate\" IS NOT NULL AND \"BalanceAsOfDate\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_bank_accounts_opening_balance_date_required",
                table: "bank_accounts",
                sql: "\"OpeningBalance\" = 0 OR \"OpeningBalanceDate\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_share_capitals_CompanyId_IssueDate",
                table: "share_capitals");

            migrationBuilder.DropCheckConstraint(
                name: "CK_share_capitals_cancelled_after_issue",
                table: "share_capitals");

            migrationBuilder.DropCheckConstraint(
                name: "CK_share_capitals_issue_date_required",
                table: "share_capitals");

            migrationBuilder.DropIndex(
                name: "IX_loans_CompanyId_BalanceAsOfDate",
                table: "loans");

            migrationBuilder.DropIndex(
                name: "IX_loans_CompanyId_DrawdownDate",
                table: "loans");

            migrationBuilder.DropCheckConstraint(
                name: "CK_loans_period_effective_dates_required",
                table: "loans");

            migrationBuilder.DropCheckConstraint(
                name: "CK_bank_accounts_opening_balance_date_required",
                table: "bank_accounts");

            migrationBuilder.DropColumn(
                name: "CancelledDate",
                table: "share_capitals");

            migrationBuilder.DropColumn(
                name: "IssueDate",
                table: "share_capitals");

            migrationBuilder.DropColumn(
                name: "BalanceAsOfDate",
                table: "loans");

            migrationBuilder.DropColumn(
                name: "DrawdownDate",
                table: "loans");

            migrationBuilder.CreateIndex(
                name: "IX_share_capitals_CompanyId",
                table: "share_capitals",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_loans_CompanyId",
                table: "loans",
                column: "CompanyId");
        }
    }
}
