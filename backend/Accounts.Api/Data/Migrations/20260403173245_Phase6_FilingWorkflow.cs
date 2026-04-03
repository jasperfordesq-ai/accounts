using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase6_FilingWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "revenue_filing_packages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedBy",
                table: "revenue_filing_packages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Ct1Reference",
                table: "revenue_filing_packages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FilingStatus",
                table: "revenue_filing_packages",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IxbrlGenerated",
                table: "revenue_filing_packages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IxbrlValidated",
                table: "revenue_filing_packages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "IxbrlValidationErrors",
                table: "revenue_filing_packages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AccountsPdfGenerated",
                table: "cro_filing_packages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "cro_filing_packages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedBy",
                table: "cro_filing_packages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CorrectionDeadline",
                table: "cro_filing_packages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CroSubmissionReference",
                table: "cro_filing_packages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FilingStatus",
                table: "cro_filing_packages",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "PaymentCompleted",
                table: "cro_filing_packages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "cro_filing_packages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SignaturePageGenerated",
                table: "cro_filing_packages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAt",
                table: "cro_filing_packages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubmittedBy",
                table: "cro_filing_packages",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "revenue_filing_packages");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                table: "revenue_filing_packages");

            migrationBuilder.DropColumn(
                name: "Ct1Reference",
                table: "revenue_filing_packages");

            migrationBuilder.DropColumn(
                name: "FilingStatus",
                table: "revenue_filing_packages");

            migrationBuilder.DropColumn(
                name: "IxbrlGenerated",
                table: "revenue_filing_packages");

            migrationBuilder.DropColumn(
                name: "IxbrlValidated",
                table: "revenue_filing_packages");

            migrationBuilder.DropColumn(
                name: "IxbrlValidationErrors",
                table: "revenue_filing_packages");

            migrationBuilder.DropColumn(
                name: "AccountsPdfGenerated",
                table: "cro_filing_packages");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "cro_filing_packages");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                table: "cro_filing_packages");

            migrationBuilder.DropColumn(
                name: "CorrectionDeadline",
                table: "cro_filing_packages");

            migrationBuilder.DropColumn(
                name: "CroSubmissionReference",
                table: "cro_filing_packages");

            migrationBuilder.DropColumn(
                name: "FilingStatus",
                table: "cro_filing_packages");

            migrationBuilder.DropColumn(
                name: "PaymentCompleted",
                table: "cro_filing_packages");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "cro_filing_packages");

            migrationBuilder.DropColumn(
                name: "SignaturePageGenerated",
                table: "cro_filing_packages");

            migrationBuilder.DropColumn(
                name: "SubmittedAt",
                table: "cro_filing_packages");

            migrationBuilder.DropColumn(
                name: "SubmittedBy",
                table: "cro_filing_packages");
        }
    }
}
