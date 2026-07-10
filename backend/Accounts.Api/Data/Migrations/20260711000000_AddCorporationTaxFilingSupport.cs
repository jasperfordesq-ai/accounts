using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCorporationTaxFilingSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "corporation_tax_filing_support_reviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PeriodId = table.Column<int>(type: "integer", nullable: false),
                    PriorPeriodStart = table.Column<DateOnly>(type: "date", nullable: true),
                    PriorPeriodEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    PriorPeriodCorporationTaxLiabilityExcludingSurchargeAndS239 = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    PriorPeriodSection239IncomeTax = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    CurrentPeriodSection239IncomeTax = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    PriorLiabilityEvidenceReference = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    HasInterestLimitationRule = table.Column<bool>(type: "boolean", nullable: false),
                    UsesNotionalGroupPaymentAllocation = table.Column<bool>(type: "boolean", nullable: false),
                    HasDirtOrOtherWithholdingCredits = table.Column<bool>(type: "boolean", nullable: false),
                    HasOtherPreliminaryTaxAdjustments = table.Column<bool>(type: "boolean", nullable: false),
                    HasMandatoryElectronicFilingExemption = table.Column<bool>(type: "boolean", nullable: false),
                    PreparedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PreparedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EvidenceNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_corporation_tax_filing_support_reviews", x => x.Id);
                    table.CheckConstraint("CK_corporation_tax_filing_support_reviews_nonnegative", "\"CurrentPeriodSection239IncomeTax\" >= 0 AND (\"PriorPeriodCorporationTaxLiabilityExcludingSurchargeAndS239\" IS NULL OR \"PriorPeriodCorporationTaxLiabilityExcludingSurchargeAndS239\" >= 0) AND (\"PriorPeriodSection239IncomeTax\" IS NULL OR \"PriorPeriodSection239IncomeTax\" >= 0)");
                    table.CheckConstraint("CK_corporation_tax_filing_support_reviews_prior_dates", "\"PriorPeriodEnd\" IS NULL OR (\"PriorPeriodEnd\" >= \"PriorPeriodStart\" AND \"PriorPeriodEnd\" <= (\"PriorPeriodStart\" + INTERVAL '1 year' - INTERVAL '1 day')::date)");
                    table.CheckConstraint("CK_corporation_tax_filing_support_reviews_prior_pair", "((\"PriorPeriodStart\" IS NULL AND \"PriorPeriodEnd\" IS NULL AND \"PriorPeriodCorporationTaxLiabilityExcludingSurchargeAndS239\" IS NULL AND \"PriorPeriodSection239IncomeTax\" IS NULL AND \"PriorLiabilityEvidenceReference\" IS NULL) OR (\"PriorPeriodStart\" IS NOT NULL AND \"PriorPeriodEnd\" IS NOT NULL AND \"PriorPeriodCorporationTaxLiabilityExcludingSurchargeAndS239\" IS NOT NULL AND \"PriorPeriodSection239IncomeTax\" IS NOT NULL AND char_length(btrim(COALESCE(\"PriorLiabilityEvidenceReference\", ''))) >= 20))");
                    table.ForeignKey(
                        name: "FK_corporation_tax_filing_support_reviews_accounting_periods_P~",
                        column: x => x.PeriodId,
                        principalTable: "accounting_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "corporation_tax_payment_records",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PeriodId = table.Column<int>(type: "integer", nullable: false),
                    PaymentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Kind = table.Column<string>(type: "text", maxLength: 40, nullable: false),
                    EvidenceReference = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ExternalPaymentReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RecordedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsVoided = table.Column<bool>(type: "boolean", nullable: false),
                    VoidedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    VoidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VoidReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_corporation_tax_payment_records", x => x.Id);
                    table.CheckConstraint("CK_corporation_tax_payment_records_evidence", "char_length(btrim(\"EvidenceReference\")) >= 20");
                    table.CheckConstraint("CK_corporation_tax_payment_records_positive_amount", "\"Amount\" > 0");
                    table.CheckConstraint("CK_corporation_tax_payment_records_void_state", "(\"IsVoided\" = FALSE AND \"VoidedBy\" IS NULL AND \"VoidedAtUtc\" IS NULL AND \"VoidReason\" IS NULL) OR (\"IsVoided\" = TRUE AND char_length(btrim(COALESCE(\"VoidedBy\", ''))) > 0 AND \"VoidedAtUtc\" IS NOT NULL AND char_length(btrim(COALESCE(\"VoidReason\", ''))) >= 20)");
                    table.ForeignKey(
                        name: "FK_corporation_tax_payment_records_accounting_periods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "accounting_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_corporation_tax_filing_support_reviews_PeriodId",
                table: "corporation_tax_filing_support_reviews",
                column: "PeriodId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_corporation_tax_payment_records_PeriodId_PaymentDate_Amount~",
                table: "corporation_tax_payment_records",
                columns: new[] { "PeriodId", "PaymentDate", "Amount", "Kind", "EvidenceReference" },
                unique: true,
                filter: "\"IsVoided\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_corporation_tax_payment_records_PeriodId_PaymentDate_Id",
                table: "corporation_tax_payment_records",
                columns: new[] { "PeriodId", "PaymentDate", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "corporation_tax_filing_support_reviews");

            migrationBuilder.DropTable(
                name: "corporation_tax_payment_records");
        }
    }
}
