using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDirectorsApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AccountsPdfGeneratedAtUtc",
                table: "cro_filing_packages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "directors_approval_pages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PeriodId = table.Column<int>(type: "integer", nullable: false),
                    ApprovalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    FirstDirectorOfficerId = table.Column<int>(type: "integer", nullable: false),
                    SecondDirectorOfficerId = table.Column<int>(type: "integer", nullable: true),
                    FirstDirectorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SecondDirectorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DirectorsInOfficeCount = table.Column<int>(type: "integer", nullable: false),
                    ResolutionReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AccountsPdfSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ArtifactSourceFingerprintSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PageArtifact = table.Column<byte[]>(type: "bytea", nullable: false),
                    PageSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    GeneratedByUserId = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    GeneratedByDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_directors_approval_pages", x => x.Id);
                    table.CheckConstraint("CK_directors_approval_pages_directors", "(\"SecondDirectorOfficerId\" IS NULL AND \"DirectorsInOfficeCount\" = 1) OR (\"SecondDirectorOfficerId\" IS NOT NULL AND \"SecondDirectorOfficerId\" <> \"FirstDirectorOfficerId\" AND \"DirectorsInOfficeCount\" >= 2)");
                    table.CheckConstraint("CK_directors_approval_pages_sha256", "length(\"RecordSha256\") = 64 AND length(\"PageSha256\") = 64 AND length(\"AccountsPdfSha256\") = 64 AND length(\"ArtifactSourceFingerprintSha256\") = 64");
                    table.ForeignKey(
                        name: "FK_directors_approval_pages_accounting_periods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "accounting_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_directors_approval_pages_company_officers_FirstDirectorOffi~",
                        column: x => x.FirstDirectorOfficerId,
                        principalTable: "company_officers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_directors_approval_pages_company_officers_SecondDirectorOff~",
                        column: x => x.SecondDirectorOfficerId,
                        principalTable: "company_officers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "directors_approval_events",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PeriodId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "text", maxLength: 20, nullable: false),
                    ApprovalPageId = table.Column<int>(type: "integer", nullable: false),
                    ApprovalPageSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SignedPageArtifact = table.Column<byte[]>(type: "bytea", nullable: true),
                    SignedPageSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ResolutionArtifact = table.Column<byte[]>(type: "bytea", nullable: true),
                    ResolutionSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RevocationReason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RecordedByUserId = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    RecordedByDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PreviousRecordSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RecordSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_directors_approval_events", x => x.Id);
                    table.CheckConstraint("CK_directors_approval_events_kind", "(\"Kind\" = 'Approved' AND \"SignedPageSha256\" IS NOT NULL AND \"ResolutionSha256\" IS NOT NULL AND \"RevocationReason\" IS NULL) OR (\"Kind\" = 'Revoked' AND \"SignedPageSha256\" IS NULL AND \"ResolutionSha256\" IS NULL AND \"RevocationReason\" IS NOT NULL)");
                    table.CheckConstraint("CK_directors_approval_events_sha256", "length(\"RecordSha256\") = 64 AND length(\"ApprovalPageSha256\") = 64 AND (\"SignedPageSha256\" IS NULL OR length(\"SignedPageSha256\") = 64) AND (\"ResolutionSha256\" IS NULL OR length(\"ResolutionSha256\") = 64)");
                    table.ForeignKey(
                        name: "FK_directors_approval_events_accounting_periods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "accounting_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_directors_approval_events_directors_approval_pages_Approval~",
                        column: x => x.ApprovalPageId,
                        principalTable: "directors_approval_pages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_directors_approval_events_ApprovalPageId",
                table: "directors_approval_events",
                column: "ApprovalPageId");

            migrationBuilder.CreateIndex(
                name: "IX_directors_approval_events_PeriodId_Genesis",
                table: "directors_approval_events",
                column: "PeriodId",
                unique: true,
                filter: "\"PreviousRecordSha256\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_directors_approval_events_PeriodId_RecordedAtUtc",
                table: "directors_approval_events",
                columns: new[] { "PeriodId", "RecordedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_directors_approval_events_PreviousRecordSha256",
                table: "directors_approval_events",
                column: "PreviousRecordSha256",
                unique: true,
                filter: "\"PreviousRecordSha256\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_directors_approval_pages_FirstDirectorOfficerId",
                table: "directors_approval_pages",
                column: "FirstDirectorOfficerId");

            migrationBuilder.CreateIndex(
                name: "IX_directors_approval_pages_PeriodId_GeneratedAtUtc",
                table: "directors_approval_pages",
                columns: new[] { "PeriodId", "GeneratedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_directors_approval_pages_SecondDirectorOfficerId",
                table: "directors_approval_pages",
                column: "SecondDirectorOfficerId");

            migrationBuilder.Sql("""
                UPDATE cro_filing_packages
                SET "AccountsPdfGeneratedAtUtc" = "GeneratedAt"
                WHERE "AccountsPdfGenerated" = TRUE AND "AccountsPdfGeneratedAtUtc" IS NULL;
                """);

            migrationBuilder.Sql(global::Accounts.Api.Data.TenantIsolationMigrationSql.BuildInstallTablesSql(
                global::Accounts.Api.Data.TenantIsolationMigrationSql.DirectorsApprovalPolicyInventory));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "directors_approval_events");

            migrationBuilder.DropTable(
                name: "directors_approval_pages");

            migrationBuilder.DropColumn(
                name: "AccountsPdfGeneratedAtUtc",
                table: "cro_filing_packages");
        }
    }
}
