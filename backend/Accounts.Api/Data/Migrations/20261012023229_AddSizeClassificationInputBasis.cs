using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSizeClassificationInputBasis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "HistoricAttestedAt",
                table: "size_classifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HistoricAttestedBy",
                table: "size_classifications",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HistoricAttestedEffectiveClass",
                table: "size_classifications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HistoricAttestedRole",
                table: "size_classifications",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "HistoricEvidenceArtifact",
                table: "size_classifications",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HistoricEvidenceSha256",
                table: "size_classifications",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HistoricSourceReference",
                table: "size_classifications",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InputBasis",
                table: "size_classifications",
                type: "text",
                nullable: false,
                // Every existing classification was decided (or saved) by the engine here.
                defaultValue: "Computed");

            migrationBuilder.AddCheckConstraint(
                name: "CK_size_classifications_historic_anchor_evidence",
                table: "size_classifications",
                sql: "\"InputBasis\" <> 'HistoricAnchor' OR (\"HistoricEvidenceArtifact\" IS NOT NULL AND \"HistoricEvidenceSha256\" IS NOT NULL AND length(\"HistoricEvidenceSha256\") = 64 AND \"HistoricAttestedBy\" IS NOT NULL AND \"HistoricAttestedRole\" IN ('Owner', 'Reviewer') AND \"HistoricAttestedAt\" IS NOT NULL AND \"HistoricSourceReference\" IS NOT NULL AND length(btrim(\"HistoricSourceReference\")) > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_size_classifications_input_basis",
                table: "size_classifications",
                sql: "\"InputBasis\" IN ('Computed', 'HistoricAnchor')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_size_classifications_historic_anchor_evidence",
                table: "size_classifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_size_classifications_input_basis",
                table: "size_classifications");

            migrationBuilder.DropColumn(
                name: "HistoricAttestedAt",
                table: "size_classifications");

            migrationBuilder.DropColumn(
                name: "HistoricAttestedBy",
                table: "size_classifications");

            migrationBuilder.DropColumn(
                name: "HistoricAttestedEffectiveClass",
                table: "size_classifications");

            migrationBuilder.DropColumn(
                name: "HistoricAttestedRole",
                table: "size_classifications");

            migrationBuilder.DropColumn(
                name: "HistoricEvidenceArtifact",
                table: "size_classifications");

            migrationBuilder.DropColumn(
                name: "HistoricEvidenceSha256",
                table: "size_classifications");

            migrationBuilder.DropColumn(
                name: "HistoricSourceReference",
                table: "size_classifications");

            migrationBuilder.DropColumn(
                name: "InputBasis",
                table: "size_classifications");
        }
    }
}
