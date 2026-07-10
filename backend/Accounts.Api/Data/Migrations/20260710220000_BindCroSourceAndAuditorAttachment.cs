using Accounts.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Api.Data.Migrations;

[DbContext(typeof(AccountsDbContext))]
[Migration("20260710220000_BindCroSourceAndAuditorAttachment")]
public partial class BindCroSourceAndAuditorAttachment : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ArtifactSourceFingerprintSha256",
            table: "cro_filing_packages",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "AttachedAuditorReportSha256",
            table: "cro_filing_packages",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ArtifactSourceFingerprintSha256",
            table: "cro_filing_packages");

        migrationBuilder.DropColumn(
            name: "AttachedAuditorReportSha256",
            table: "cro_filing_packages");
    }
}
