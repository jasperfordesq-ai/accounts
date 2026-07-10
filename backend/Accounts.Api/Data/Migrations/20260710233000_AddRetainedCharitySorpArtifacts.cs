using Accounts.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Api.Data.Migrations;

[DbContext(typeof(AccountsDbContext))]
[Migration("20260710233000_AddRetainedCharitySorpArtifacts")]
public partial class AddRetainedCharitySorpArtifacts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<bool>(
            name: "GovernanceCodeCompliant",
            table: "charity_infos",
            type: "boolean",
            nullable: true,
            oldClrType: typeof(bool),
            oldType: "boolean");

        migrationBuilder.AddColumn<byte[]>(name: "GovernanceEvidenceArtifact", table: "charity_infos", type: "bytea", nullable: true);
        migrationBuilder.AddColumn<string>(name: "GovernanceEvidenceArtifactSha256", table: "charity_infos", type: "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>(name: "GovernanceEvidenceReference", table: "charity_infos", type: "character varying(300)", maxLength: 300, nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "GovernanceReviewedAtUtc", table: "charity_infos", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<string>(name: "GovernanceReviewedBy", table: "charity_infos", type: "character varying(200)", maxLength: 200, nullable: true);

        migrationBuilder.AddColumn<string>(name: "ArtifactSourceFingerprintSha256", table: "charity_filing_packages", type: "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "BalanceSheetNetAssets", table: "charity_filing_packages", type: "numeric(18,2)", nullable: true);
        migrationBuilder.AddColumn<string>(name: "CharityNumberSnapshot", table: "charity_filing_packages", type: "character varying(20)", maxLength: 20, nullable: true);
        migrationBuilder.AddColumn<string>(name: "ManualProfessionalHandoffReason", table: "charity_filing_packages", type: "character varying(1000)", maxLength: 1000, nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "ReconciledAtUtc", table: "charity_filing_packages", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "ReconciliationDifference", table: "charity_filing_packages", type: "numeric(18,2)", nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "SofaClosingFunds", table: "charity_filing_packages", type: "numeric(18,2)", nullable: true);
        migrationBuilder.AddColumn<string>(name: "SofaBasis", table: "charity_filing_packages", type: "character varying(50)", maxLength: 50, nullable: true);
        migrationBuilder.AddColumn<string>(name: "SorpDecisionSha256", table: "charity_filing_packages", type: "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>(name: "SorpFrameworkCode", table: "charity_filing_packages", type: "character varying(50)", maxLength: 50, nullable: true);
        migrationBuilder.AddColumn<int>(name: "SorpTier", table: "charity_filing_packages", type: "integer", nullable: true);
        migrationBuilder.AddColumn<bool>(name: "TrusteeReviewAccepted", table: "charity_filing_packages", type: "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<byte[]>(name: "TrusteeReviewArtifact", table: "charity_filing_packages", type: "bytea", nullable: true);
        migrationBuilder.AddColumn<string>(name: "TrusteeReviewArtifactSha256", table: "charity_filing_packages", type: "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>(name: "TrusteeReviewReference", table: "charity_filing_packages", type: "character varying(300)", maxLength: 300, nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "TrusteeReviewedAtUtc", table: "charity_filing_packages", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<string>(name: "TrusteeReviewedBy", table: "charity_filing_packages", type: "character varying(200)", maxLength: 200, nullable: true);
        migrationBuilder.AddColumn<string>(name: "TrusteePopulationJson", table: "charity_filing_packages", type: "text", nullable: true);
        migrationBuilder.AddColumn<string>(name: "TrusteePopulationSha256", table: "charity_filing_packages", type: "character varying(64)", maxLength: 64, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var column in new[]
        {
            "ArtifactSourceFingerprintSha256", "BalanceSheetNetAssets", "CharityNumberSnapshot",
            "ManualProfessionalHandoffReason", "ReconciledAtUtc", "ReconciliationDifference",
            "SofaClosingFunds", "SofaBasis", "SorpDecisionSha256", "SorpFrameworkCode", "SorpTier",
            "TrusteeReviewAccepted", "TrusteeReviewArtifact", "TrusteeReviewArtifactSha256",
            "TrusteeReviewReference", "TrusteeReviewedAtUtc", "TrusteeReviewedBy",
            "TrusteePopulationJson", "TrusteePopulationSha256"
        })
            migrationBuilder.DropColumn(name: column, table: "charity_filing_packages");

        foreach (var column in new[]
        {
            "GovernanceEvidenceArtifact", "GovernanceEvidenceArtifactSha256",
            "GovernanceEvidenceReference", "GovernanceReviewedAtUtc", "GovernanceReviewedBy"
        })
            migrationBuilder.DropColumn(name: column, table: "charity_infos");

        migrationBuilder.AlterColumn<bool>(
            name: "GovernanceCodeCompliant",
            table: "charity_infos",
            type: "boolean",
            nullable: false,
            defaultValue: false,
            oldClrType: typeof(bool),
            oldType: "boolean",
            oldNullable: true);
    }
}
