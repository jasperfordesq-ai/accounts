using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLogEvidenceMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActorDisplayName",
                table: "audit_logs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IntegrityHash",
                table: "audit_logs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviousIntegrityHash",
                table: "audit_logs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestId",
                table: "audit_logs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "audit_logs",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_CompanyId_PeriodId_Timestamp",
                table: "audit_logs",
                columns: new[] { "CompanyId", "PeriodId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_IntegrityHash",
                table: "audit_logs",
                column: "IntegrityHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_PreviousIntegrityHash",
                table: "audit_logs",
                column: "PreviousIntegrityHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_RequestId",
                table: "audit_logs",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_TenantId_Timestamp",
                table: "audit_logs",
                columns: new[] { "TenantId", "Timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_audit_logs_CompanyId_PeriodId_Timestamp",
                table: "audit_logs");

            migrationBuilder.DropIndex(
                name: "IX_audit_logs_IntegrityHash",
                table: "audit_logs");

            migrationBuilder.DropIndex(
                name: "IX_audit_logs_PreviousIntegrityHash",
                table: "audit_logs");

            migrationBuilder.DropIndex(
                name: "IX_audit_logs_RequestId",
                table: "audit_logs");

            migrationBuilder.DropIndex(
                name: "IX_audit_logs_TenantId_Timestamp",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "ActorDisplayName",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "IntegrityHash",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "PreviousIntegrityHash",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "RequestId",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "audit_logs");
        }
    }
}
