using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSizeClassificationAnchorRetirement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_size_classifications_input_basis",
                table: "size_classifications");

            migrationBuilder.AddColumn<DateTime>(
                name: "HistoricRetiredAt",
                table: "size_classifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HistoricRetiredBy",
                table: "size_classifications",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HistoricRetiredReason",
                table: "size_classifications",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_size_classifications_historic_anchor_retirement",
                table: "size_classifications",
                sql: "(\"HistoricRetiredAt\" IS NULL AND \"HistoricRetiredBy\" IS NULL AND \"HistoricRetiredReason\" IS NULL AND \"InputBasis\" <> 'RetiredHistoricAnchor') OR (\"InputBasis\" <> 'HistoricAnchor' AND \"HistoricRetiredAt\" IS NOT NULL AND char_length(btrim(COALESCE(\"HistoricRetiredBy\", ''))) > 0 AND char_length(btrim(COALESCE(\"HistoricRetiredReason\", ''))) > 0 AND \"HistoricEvidenceArtifact\" IS NOT NULL AND \"HistoricEvidenceSha256\" IS NOT NULL AND length(\"HistoricEvidenceSha256\") = 64 AND \"HistoricSourceReference\" IS NOT NULL AND \"HistoricAttestedBy\" IS NOT NULL AND \"HistoricAttestedAt\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_size_classifications_input_basis",
                table: "size_classifications",
                sql: "\"InputBasis\" IN ('Computed', 'HistoricAnchor', 'RetiredHistoricAnchor')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_size_classifications_historic_anchor_retirement",
                table: "size_classifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_size_classifications_input_basis",
                table: "size_classifications");

            migrationBuilder.DropColumn(
                name: "HistoricRetiredAt",
                table: "size_classifications");

            migrationBuilder.DropColumn(
                name: "HistoricRetiredBy",
                table: "size_classifications");

            migrationBuilder.DropColumn(
                name: "HistoricRetiredReason",
                table: "size_classifications");

            migrationBuilder.AddCheckConstraint(
                name: "CK_size_classifications_input_basis",
                table: "size_classifications",
                sql: "\"InputBasis\" IN ('Computed', 'HistoricAnchor')");
        }
    }
}
