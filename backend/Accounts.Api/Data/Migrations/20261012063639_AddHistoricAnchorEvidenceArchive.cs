using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHistoricAnchorEvidenceArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "historic_anchor_evidence_records",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PeriodId = table.Column<int>(type: "integer", nullable: false),
                    Event = table.Column<string>(type: "text", nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ArchivedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ArchivedByUserId = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    Turnover = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    BalanceSheetTotal = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    AvgEmployees = table.Column<int>(type: "integer", nullable: false),
                    ThresholdElectionEffectiveFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    SourceReference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    EvidenceArtifact = table.Column<byte[]>(type: "bytea", nullable: false),
                    EvidenceSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttestedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AttestedRole = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AttestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AttestedEffectiveClass = table.Column<string>(type: "text", nullable: true),
                    RetiredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RetiredBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RetiredByUserId = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    RetiredReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_historic_anchor_evidence_records", x => x.Id);
                    table.CheckConstraint("CK_historic_anchor_evidence_records_evidence", "length(\"EvidenceSha256\") = 64 AND octet_length(\"EvidenceArtifact\") > 5 AND char_length(btrim(\"SourceReference\")) > 0 AND char_length(btrim(\"AttestedBy\")) > 0 AND \"AttestedRole\" IN ('Owner', 'Reviewer') AND char_length(btrim(\"ArchivedBy\")) > 0");
                    table.CheckConstraint("CK_historic_anchor_evidence_records_retirement", "(\"Event\" = 'Replaced' AND \"RetiredAt\" IS NULL AND \"RetiredBy\" IS NULL AND \"RetiredReason\" IS NULL) OR (\"RetiredAt\" IS NOT NULL AND char_length(btrim(COALESCE(\"RetiredBy\", ''))) > 0 AND char_length(btrim(COALESCE(\"RetiredReason\", ''))) > 0)");
                    table.ForeignKey(
                        name: "FK_historic_anchor_evidence_records_accounting_periods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "accounting_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_historic_anchor_evidence_records_PeriodId_ArchivedAt",
                table: "historic_anchor_evidence_records",
                columns: new[] { "PeriodId", "ArchivedAt" });

            // Append-only: archived historic evidence is never updated or deleted.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION accounts_prevent_historic_anchor_evidence_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    RAISE EXCEPTION 'Archived historic size evidence is append-only.'
                        USING ERRCODE = '23514', CONSTRAINT = 'CK_historic_anchor_evidence_records_immutable';
                END;
                $function$;

                CREATE TRIGGER "TR_historic_anchor_evidence_records_immutable"
                    BEFORE UPDATE OR DELETE ON historic_anchor_evidence_records
                    FOR EACH ROW EXECUTE FUNCTION accounts_prevent_historic_anchor_evidence_mutation();
                """);

            migrationBuilder.Sql(global::Accounts.Api.Data.TenantIsolationMigrationSql.BuildInstallTablesSql(
                global::Accounts.Api.Data.TenantIsolationMigrationSql.HistoricAnchorEvidencePolicyInventory));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Never drop archived evidence silently: refuse while any record exists (export it first).
            migrationBuilder.Sql("""
                DO $refuse$
                BEGIN
                    IF EXISTS (SELECT 1 FROM historic_anchor_evidence_records) THEN
                        RAISE EXCEPTION 'historic_anchor_evidence_records holds archived historic size evidence; export it before rolling back this migration.'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_historic_anchor_evidence_records_immutable';
                    END IF;
                END
                $refuse$;

                DROP TRIGGER IF EXISTS "TR_historic_anchor_evidence_records_immutable" ON historic_anchor_evidence_records;
                DROP FUNCTION IF EXISTS accounts_prevent_historic_anchor_evidence_mutation();
                """);

            migrationBuilder.DropTable(
                name: "historic_anchor_evidence_records");
        }
    }
}
