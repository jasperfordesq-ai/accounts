using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnforceStatutoryNoteChecklist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChecklistState",
                table: "notes_disclosures",
                type: "text",
                maxLength: 30,
                nullable: false,
                defaultValue: "Required");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "notes_disclosures",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewEvidence",
                table: "notes_disclosures",
                type: "character varying(20000)",
                maxLength: 20000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAt",
                table: "notes_disclosures",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedBy",
                table: "notes_disclosures",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE notes_disclosures
                SET "Code" = CASE lower(trim("Title"))
                    WHEN 'accounting policies' THEN 'ACC-POLICIES'
                    WHEN 'tangible fixed assets' THEN 'FIXED-ASSETS'
                    WHEN 'inventories' THEN 'INVENTORIES'
                    WHEN 'stock' THEN 'INVENTORIES'
                    WHEN 'debtors' THEN 'DEBTORS'
                    WHEN 'creditors: amounts falling due within one year' THEN 'CREDITORS-CURRENT'
                    WHEN 'creditors: amounts falling due after more than one year' THEN 'CREDITORS-LONG-TERM'
                    WHEN 'share capital' THEN 'SHARE-CAPITAL'
                    WHEN 'capital and reserves' THEN 'RESERVES'
                    WHEN 'reserves' THEN 'RESERVES'
                    WHEN 'employees and remuneration' THEN 'EMPLOYEES'
                    WHEN 'advances, credits and guarantees to directors' THEN 'DIRECTOR-TRANSACTIONS'
                    WHEN 'directors'' loans and transactions' THEN 'DIRECTOR-TRANSACTIONS'
                    WHEN 'directors'' remuneration' THEN 'DIRECTOR-REMUNERATION'
                    WHEN 'post balance sheet events' THEN 'POST-BALANCE-SHEET-EVENTS'
                    WHEN 'related party transactions' THEN 'RELATED-PARTIES'
                    WHEN 'ultimate controlling party' THEN 'ULTIMATE-CONTROLLING-PARTY'
                    WHEN 'contingent liabilities' THEN 'CONTINGENT-LIABILITIES'
                    WHEN 'going concern' THEN 'GOING-CONCERN'
                    WHEN 'turnover' THEN 'TURNOVER'
                    WHEN 'tax on profit on ordinary activities' THEN 'TAX-ON-PROFIT'
                    WHEN 'dividends' THEN 'DIVIDENDS'
                    WHEN 'financial instruments' THEN 'FINANCIAL-INSTRUMENTS'
                    WHEN 'capital commitments' THEN 'CAPITAL-COMMITMENTS'
                    WHEN 'deferred tax' THEN 'DEFERRED-TAX'
                    WHEN 'approval of financial statements' THEN 'APPROVAL'
                    ELSE NULL
                END,
                "ChecklistState" = 'Required'
                WHERE "IsRequired" = TRUE;

                UPDATE notes_disclosures AS duplicate
                SET "Code" = NULL
                FROM notes_disclosures AS retained
                WHERE duplicate."PeriodId" = retained."PeriodId"
                  AND duplicate."Code" = retained."Code"
                  AND duplicate."Code" IS NOT NULL
                  AND duplicate."Id" > retained."Id";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_notes_disclosures_PeriodId_Code",
                table: "notes_disclosures",
                columns: new[] { "PeriodId", "Code" },
                unique: true,
                filter: "\"Code\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_notes_disclosures_PeriodId_Code",
                table: "notes_disclosures");

            migrationBuilder.DropColumn(
                name: "ChecklistState",
                table: "notes_disclosures");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "notes_disclosures");

            migrationBuilder.DropColumn(
                name: "ReviewEvidence",
                table: "notes_disclosures");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "notes_disclosures");

            migrationBuilder.DropColumn(
                name: "ReviewedBy",
                table: "notes_disclosures");
        }
    }
}
