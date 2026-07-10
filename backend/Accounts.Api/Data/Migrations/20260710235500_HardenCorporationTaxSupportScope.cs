using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class HardenCorporationTaxSupportScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DirectorsFees",
                table: "payroll_summaries",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "CapitalAllowanceEvidence",
                table: "fixed_assets",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CapitalAllowanceReviewedAtUtc",
                table: "fixed_assets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CapitalAllowanceReviewedBy",
                table: "fixed_assets",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CapitalAllowanceTreatment",
                table: "fixed_assets",
                type: "text",
                maxLength: 40,
                nullable: false,
                defaultValue: "Unreviewed");

            migrationBuilder.CreateTable(
                name: "corporation_tax_loss_records",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PeriodId = table.Column<int>(type: "integer", nullable: false),
                    OpeningTradingLoss = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CurrentPeriodTradingLoss = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    TradingLossUsed = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ClosingTradingLoss = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Treatment = table.Column<string>(type: "text", maxLength: 40, nullable: false),
                    CalculationSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    RecordedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_corporation_tax_loss_records", x => x.Id);
                    table.CheckConstraint("CK_corporation_tax_loss_records_nonnegative", "\"OpeningTradingLoss\" >= 0 AND \"CurrentPeriodTradingLoss\" >= 0 AND \"TradingLossUsed\" >= 0 AND \"ClosingTradingLoss\" >= 0");
                    table.ForeignKey(
                        name: "FK_corporation_tax_loss_records_accounting_periods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "accounting_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "corporation_tax_scope_reviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PeriodId = table.Column<int>(type: "integer", nullable: false),
                    IsCloseCompany = table.Column<bool>(type: "boolean", nullable: true),
                    IsServiceCompany = table.Column<bool>(type: "boolean", nullable: true),
                    HasGroupOrConsortiumRelief = table.Column<bool>(type: "boolean", nullable: false),
                    HasChargeableGains = table.Column<bool>(type: "boolean", nullable: false),
                    HasForeignIncomeOrTaxCredits = table.Column<bool>(type: "boolean", nullable: false),
                    HasExceptedTrade = table.Column<bool>(type: "boolean", nullable: false),
                    HasOtherReliefsOrSpecialRegimes = table.Column<bool>(type: "boolean", nullable: false),
                    DeclaredPassiveIncomePresent = table.Column<bool>(type: "boolean", nullable: false),
                    PassiveIncomeClassificationReviewed = table.Column<bool>(type: "boolean", nullable: false),
                    LossTreatment = table.Column<string>(type: "text", maxLength: 40, nullable: false),
                    BroughtForwardTradingLoss = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    BroughtForwardLossEvidence = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PreparedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PreparedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EvidenceNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_corporation_tax_scope_reviews", x => x.Id);
                    table.CheckConstraint("CK_corporation_tax_scope_reviews_brought_forward_loss", "\"BroughtForwardTradingLoss\" >= 0");
                    table.ForeignKey(
                        name: "FK_corporation_tax_scope_reviews_accounting_periods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "accounting_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_corporation_tax_loss_records_PeriodId",
                table: "corporation_tax_loss_records",
                column: "PeriodId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_corporation_tax_scope_reviews_PeriodId",
                table: "corporation_tax_scope_reviews",
                column: "PeriodId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "corporation_tax_loss_records");

            migrationBuilder.DropTable(
                name: "corporation_tax_scope_reviews");

            migrationBuilder.DropColumn(
                name: "DirectorsFees",
                table: "payroll_summaries");

            migrationBuilder.DropColumn(
                name: "CapitalAllowanceEvidence",
                table: "fixed_assets");

            migrationBuilder.DropColumn(
                name: "CapitalAllowanceReviewedAtUtc",
                table: "fixed_assets");

            migrationBuilder.DropColumn(
                name: "CapitalAllowanceReviewedBy",
                table: "fixed_assets");

            migrationBuilder.DropColumn(
                name: "CapitalAllowanceTreatment",
                table: "fixed_assets");
        }
    }
}
