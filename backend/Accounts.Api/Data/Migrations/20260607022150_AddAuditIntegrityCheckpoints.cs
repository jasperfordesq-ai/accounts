using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditIntegrityCheckpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_integrity_checkpoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<int>(type: "integer", nullable: true),
                    LastAuditLogId = table.Column<int>(type: "integer", nullable: false),
                    LastIntegrityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CheckedEntries = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    CreatedByDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RequestId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    KeyId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Signature = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_integrity_checkpoints", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_integrity_checkpoints_CompanyId_Id",
                table: "audit_integrity_checkpoints",
                columns: new[] { "CompanyId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_integrity_checkpoints_CompanyId_LastAuditLogId",
                table: "audit_integrity_checkpoints",
                columns: new[] { "CompanyId", "LastAuditLogId" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_integrity_checkpoints_Signature",
                table: "audit_integrity_checkpoints",
                column: "Signature",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_audit_integrity_checkpoints_TenantId_CreatedAtUtc",
                table: "audit_integrity_checkpoints",
                columns: new[] { "TenantId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_integrity_checkpoints");
        }
    }
}
