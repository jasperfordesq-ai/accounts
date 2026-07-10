using Accounts.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Api.Data.Migrations;

[DbContext(typeof(AccountsDbContext))]
[Migration("20260710190000_AddStatutorySizeDecisionEvidence")]
public partial class AddStatutorySizeDecisionEvidence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var column in new[]
                 {
                     "IsFifthScheduleEntity", "IsOtherIneligibleEntity", "IsFinancialHoldingUndertaking",
                     "PreparesGroupFinancialStatements", "IncludedInHigherConsolidatedFinancialStatements"
                 })
        {
            migrationBuilder.AddColumn<bool>(column, "companies", "boolean", nullable: false, defaultValue: false);
        }

        migrationBuilder.AddColumn<decimal>("AnnualisedTurnover", "size_classifications", "numeric(18,2)", nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<string>("DecisionInputFingerprintSha256", "size_classifications", "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<byte[]>("OverrideEvidenceArtifact", "size_classifications", "bytea", nullable: true);
        migrationBuilder.AddColumn<string>("OverrideEvidenceSha256", "size_classifications", "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>("OverrideInputFingerprintSha256", "size_classifications", "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<bool>("OverrideRequiresRereview", "size_classifications", "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<string>("OverrideAuthorityRole", "size_classifications", "character varying(50)", maxLength: 50, nullable: true);
        migrationBuilder.AddColumn<string>("OverrideApprovedBy", "size_classifications", "character varying(200)", maxLength: 200, nullable: true);
        migrationBuilder.AddColumn<DateTime>("OverrideApprovedAt", "size_classifications", "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<decimal>("PeriodLengthInYears", "size_classifications", "numeric(10,6)", nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<bool>("RawCurrentMediumQualified", "size_classifications", "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<bool>("RawCurrentMicroQualified", "size_classifications", "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<bool>("RawCurrentSmallQualified", "size_classifications", "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<string>("RawCurrentClass", "size_classifications", "text", nullable: false, defaultValue: "Micro");
        migrationBuilder.AddColumn<bool>("RawPriorMediumQualified", "size_classifications", "boolean", nullable: true);
        migrationBuilder.AddColumn<bool>("RawPriorMicroQualified", "size_classifications", "boolean", nullable: true);
        migrationBuilder.AddColumn<bool>("RawPriorSmallQualified", "size_classifications", "boolean", nullable: true);
        migrationBuilder.AddColumn<string>("RawPriorClass", "size_classifications", "text", nullable: true);
        migrationBuilder.AddColumn<DateOnly>("ThresholdElectionEffectiveFrom", "size_classifications", "date", nullable: true);
        migrationBuilder.AddColumn<DateOnly>("ThresholdScheduleEffectiveFrom", "size_classifications", "date", nullable: true);
        migrationBuilder.AddColumn<string>("ThresholdScheduleCode", "size_classifications", "character varying(100)", maxLength: 100, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var column in new[]
                 {
                     "IsFifthScheduleEntity", "IsOtherIneligibleEntity", "IsFinancialHoldingUndertaking",
                     "PreparesGroupFinancialStatements", "IncludedInHigherConsolidatedFinancialStatements"
                 })
        {
            migrationBuilder.DropColumn(column, "companies");
        }

        foreach (var column in new[]
                 {
                     "AnnualisedTurnover", "DecisionInputFingerprintSha256", "OverrideEvidenceArtifact",
                     "OverrideEvidenceSha256", "OverrideInputFingerprintSha256", "OverrideRequiresRereview",
                     "OverrideAuthorityRole", "OverrideApprovedBy", "OverrideApprovedAt", "PeriodLengthInYears",
                     "RawCurrentMediumQualified", "RawCurrentMicroQualified", "RawCurrentSmallQualified",
                     "RawCurrentClass", "RawPriorMediumQualified", "RawPriorMicroQualified",
                     "RawPriorSmallQualified", "RawPriorClass", "ThresholdElectionEffectiveFrom",
                     "ThresholdScheduleEffectiveFrom", "ThresholdScheduleCode"
                 })
        {
            migrationBuilder.DropColumn(column, "size_classifications");
        }
    }
}
