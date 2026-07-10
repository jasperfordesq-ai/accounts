using Accounts.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Api.Data.Migrations;

[DbContext(typeof(AccountsDbContext))]
[Migration("20260710143000_AddFilingReleaseGateEvidence")]
public partial class AddFilingReleaseGateEvidence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<byte[]>("AuditorsReportArtifact", "accounting_periods", "bytea", nullable: true);
        migrationBuilder.AddColumn<string>("AuditorsReportFirmName", "accounting_periods", "character varying(200)", maxLength: 200, nullable: true);
        migrationBuilder.AddColumn<string>("AuditorsReportMembershipNumber", "accounting_periods", "character varying(100)", maxLength: 100, nullable: true);
        migrationBuilder.AddColumn<string>("AuditorsReportProfessionalBody", "accounting_periods", "character varying(200)", maxLength: 200, nullable: true);
        migrationBuilder.AddColumn<DateTime>("AuditorsReportReviewedAt", "accounting_periods", "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<string>("AuditorsReportReviewedBy", "accounting_periods", "character varying(200)", maxLength: 200, nullable: true);
        migrationBuilder.AddColumn<string>("AuditorsReportReviewDecision", "accounting_periods", "character varying(50)", maxLength: 50, nullable: true);
        migrationBuilder.AddColumn<string>("AuditorsReportSha256", "accounting_periods", "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>("AuditorsReportSignerName", "accounting_periods", "character varying(200)", maxLength: 200, nullable: true);
        migrationBuilder.AddColumn<DateTime>("AuditorsReportSignedAt", "accounting_periods", "timestamp with time zone", nullable: true);

        migrationBuilder.AddColumn<byte[]>("AccountsPdfArtifact", "cro_filing_packages", "bytea", nullable: true);
        migrationBuilder.AddColumn<string>("AccountsPdfSha256", "cro_filing_packages", "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>("ApprovedArtifactManifestSha256", "cro_filing_packages", "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>("ApprovedReleaseCandidate", "cro_filing_packages", "character varying(200)", maxLength: 200, nullable: true);
        migrationBuilder.AddColumn<string>("ApprovalCapacity", "cro_filing_packages", "character varying(100)", maxLength: 100, nullable: true);
        migrationBuilder.AddColumn<string>("ApprovalDecision", "cro_filing_packages", "character varying(50)", maxLength: 50, nullable: true);
        migrationBuilder.AddColumn<string>("ApprovalScope", "cro_filing_packages", "character varying(100)", maxLength: 100, nullable: true);
        migrationBuilder.AddColumn<string>("ArtifactReleaseCandidate", "cro_filing_packages", "character varying(200)", maxLength: 200, nullable: true);
        migrationBuilder.AddColumn<DateTime>("ApproverCredentialValidUntil", "cro_filing_packages", "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<string>("ApproverMembershipNumber", "cro_filing_packages", "character varying(100)", maxLength: 100, nullable: true);
        migrationBuilder.AddColumn<string>("ApproverProfessionalBody", "cro_filing_packages", "character varying(200)", maxLength: 200, nullable: true);
        migrationBuilder.AddColumn<int>("ApproverTenantId", "cro_filing_packages", "integer", nullable: true);
        migrationBuilder.AddColumn<byte[]>("ApproverVerificationArtifact", "cro_filing_packages", "bytea", nullable: true);
        migrationBuilder.AddColumn<string>("ApproverVerificationArtifactSha256", "cro_filing_packages", "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>("ApproverVerificationReference", "cro_filing_packages", "character varying(200)", maxLength: 200, nullable: true);
        migrationBuilder.AddColumn<DateTime>("ApproverVerifiedAt", "cro_filing_packages", "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<byte[]>("SignaturePageArtifact", "cro_filing_packages", "bytea", nullable: true);
        migrationBuilder.AddColumn<string>("SignaturePageSha256", "cro_filing_packages", "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<byte[]>("SignedPdfArtifact", "cro_filing_packages", "bytea", nullable: true);
        migrationBuilder.AddColumn<string>("SignedPdfSha256", "cro_filing_packages", "character varying(64)", maxLength: 64, nullable: true);

        migrationBuilder.AddColumn<string>("ApprovedArtifactManifestSha256", "revenue_filing_packages", "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>("ApprovedReleaseCandidate", "revenue_filing_packages", "character varying(200)", maxLength: 200, nullable: true);
        migrationBuilder.AddColumn<string>("ApprovalCapacity", "revenue_filing_packages", "character varying(100)", maxLength: 100, nullable: true);
        migrationBuilder.AddColumn<string>("ApprovalDecision", "revenue_filing_packages", "character varying(50)", maxLength: 50, nullable: true);
        migrationBuilder.AddColumn<string>("ApprovalScope", "revenue_filing_packages", "character varying(100)", maxLength: 100, nullable: true);
        migrationBuilder.AddColumn<string>("ArtifactReleaseCandidate", "revenue_filing_packages", "character varying(200)", maxLength: 200, nullable: true);
        migrationBuilder.AddColumn<DateTime>("ApproverCredentialValidUntil", "revenue_filing_packages", "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<string>("ApproverMembershipNumber", "revenue_filing_packages", "character varying(100)", maxLength: 100, nullable: true);
        migrationBuilder.AddColumn<string>("ApproverProfessionalBody", "revenue_filing_packages", "character varying(200)", maxLength: 200, nullable: true);
        migrationBuilder.AddColumn<int>("ApproverTenantId", "revenue_filing_packages", "integer", nullable: true);
        migrationBuilder.AddColumn<byte[]>("ApproverVerificationArtifact", "revenue_filing_packages", "bytea", nullable: true);
        migrationBuilder.AddColumn<string>("ApproverVerificationArtifactSha256", "revenue_filing_packages", "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>("ApproverVerificationReference", "revenue_filing_packages", "character varying(200)", maxLength: 200, nullable: true);
        migrationBuilder.AddColumn<DateTime>("ApproverVerifiedAt", "revenue_filing_packages", "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<string>("ExternalValidationArtifactSha256", "revenue_filing_packages", "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>("ExternalValidationReference", "revenue_filing_packages", "character varying(200)", maxLength: 200, nullable: true);
        migrationBuilder.AddColumn<byte[]>("ExternalValidationResponseArtifact", "revenue_filing_packages", "bytea", nullable: true);
        migrationBuilder.AddColumn<string>("ExternalValidationResponseSha256", "revenue_filing_packages", "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>("ExternalValidationWarningDisposition", "revenue_filing_packages", "character varying(50)", maxLength: 50, nullable: true);
        migrationBuilder.AddColumn<DateTime>("ExternalValidatedAt", "revenue_filing_packages", "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<string>("ExternalValidatorProvider", "revenue_filing_packages", "character varying(200)", maxLength: 200, nullable: true);
        migrationBuilder.AddColumn<string>("ExternalValidatorVersion", "revenue_filing_packages", "character varying(100)", maxLength: 100, nullable: true);
        migrationBuilder.AddColumn<string>("ExternalTaxonomyPackageSha256", "revenue_filing_packages", "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<byte[]>("IxbrlArtifact", "revenue_filing_packages", "bytea", nullable: true);
        migrationBuilder.AddColumn<string>("IxbrlSha256", "revenue_filing_packages", "character varying(64)", maxLength: 64, nullable: true);

        migrationBuilder.AddColumn<string>("ApprovedArtifactManifestSha256", "charity_filing_packages", "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>("ApprovedReleaseCandidate", "charity_filing_packages", "character varying(200)", maxLength: 200, nullable: true);
        migrationBuilder.AddColumn<string>("ApprovalCapacity", "charity_filing_packages", "character varying(100)", maxLength: 100, nullable: true);
        migrationBuilder.AddColumn<string>("ApprovalDecision", "charity_filing_packages", "character varying(50)", maxLength: 50, nullable: true);
        migrationBuilder.AddColumn<string>("ApprovalScope", "charity_filing_packages", "character varying(100)", maxLength: 100, nullable: true);
        migrationBuilder.AddColumn<string>("ArtifactReleaseCandidate", "charity_filing_packages", "character varying(200)", maxLength: 200, nullable: true);
        migrationBuilder.AddColumn<DateTime>("ApproverCredentialValidUntil", "charity_filing_packages", "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<string>("ApproverMembershipNumber", "charity_filing_packages", "character varying(100)", maxLength: 100, nullable: true);
        migrationBuilder.AddColumn<string>("ApproverProfessionalBody", "charity_filing_packages", "character varying(200)", maxLength: 200, nullable: true);
        migrationBuilder.AddColumn<int>("ApproverTenantId", "charity_filing_packages", "integer", nullable: true);
        migrationBuilder.AddColumn<byte[]>("ApproverVerificationArtifact", "charity_filing_packages", "bytea", nullable: true);
        migrationBuilder.AddColumn<string>("ApproverVerificationArtifactSha256", "charity_filing_packages", "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>("ApproverVerificationReference", "charity_filing_packages", "character varying(200)", maxLength: 200, nullable: true);
        migrationBuilder.AddColumn<DateTime>("ApproverVerifiedAt", "charity_filing_packages", "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<byte[]>("SofaArtifact", "charity_filing_packages", "bytea", nullable: true);
        migrationBuilder.AddColumn<string>("SofaSha256", "charity_filing_packages", "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<byte[]>("TrusteesReportArtifact", "charity_filing_packages", "bytea", nullable: true);
        migrationBuilder.AddColumn<string>("TrusteesReportSha256", "charity_filing_packages", "character varying(64)", maxLength: 64, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("AuditorsReportArtifact", "accounting_periods");
        migrationBuilder.DropColumn("AuditorsReportFirmName", "accounting_periods");
        migrationBuilder.DropColumn("AuditorsReportMembershipNumber", "accounting_periods");
        migrationBuilder.DropColumn("AuditorsReportProfessionalBody", "accounting_periods");
        migrationBuilder.DropColumn("AuditorsReportReviewedAt", "accounting_periods");
        migrationBuilder.DropColumn("AuditorsReportReviewedBy", "accounting_periods");
        migrationBuilder.DropColumn("AuditorsReportReviewDecision", "accounting_periods");
        migrationBuilder.DropColumn("AuditorsReportSha256", "accounting_periods");
        migrationBuilder.DropColumn("AuditorsReportSignerName", "accounting_periods");
        migrationBuilder.DropColumn("AuditorsReportSignedAt", "accounting_periods");

        foreach (var column in new[]
                 {
                     "AccountsPdfArtifact", "AccountsPdfSha256", "ApprovedArtifactManifestSha256",
                     "ApprovedReleaseCandidate", "ArtifactReleaseCandidate", "SignaturePageArtifact",
                     "SignaturePageSha256", "ApproverCredentialValidUntil", "ApproverMembershipNumber",
                     "ApproverProfessionalBody", "ApproverVerificationArtifact", "ApproverVerificationArtifactSha256",
                     "ApproverVerificationReference", "ApproverVerifiedAt", "ApproverTenantId", "ApprovalCapacity",
                     "ApprovalDecision", "ApprovalScope", "SignedPdfArtifact", "SignedPdfSha256"
                 })
            migrationBuilder.DropColumn(column, "cro_filing_packages");

        foreach (var column in new[]
                 {
                     "ApprovedArtifactManifestSha256", "ApprovedReleaseCandidate", "ArtifactReleaseCandidate",
                     "ExternalValidationArtifactSha256", "ExternalValidationReference", "ExternalValidatedAt",
                     "ExternalValidationResponseArtifact", "ExternalValidationResponseSha256",
                     "ExternalValidationWarningDisposition", "ExternalValidatorProvider", "ExternalValidatorVersion",
                     "ExternalTaxonomyPackageSha256", "IxbrlArtifact", "IxbrlSha256", "ApproverCredentialValidUntil",
                     "ApproverMembershipNumber", "ApproverProfessionalBody", "ApproverVerificationArtifact",
                     "ApproverVerificationArtifactSha256", "ApproverVerificationReference", "ApproverVerifiedAt",
                     "ApproverTenantId", "ApprovalCapacity", "ApprovalDecision", "ApprovalScope"
                 })
            migrationBuilder.DropColumn(column, "revenue_filing_packages");

        foreach (var column in new[]
                 {
                     "ApprovedArtifactManifestSha256", "ApprovedReleaseCandidate", "ArtifactReleaseCandidate",
                     "SofaArtifact", "SofaSha256", "TrusteesReportArtifact", "TrusteesReportSha256",
                     "ApproverCredentialValidUntil", "ApproverMembershipNumber", "ApproverProfessionalBody",
                     "ApproverVerificationArtifact", "ApproverVerificationArtifactSha256",
                     "ApproverVerificationReference", "ApproverVerifiedAt", "ApproverTenantId", "ApprovalCapacity",
                     "ApprovalDecision", "ApprovalScope"
                 })
            migrationBuilder.DropColumn(column, "charity_filing_packages");
    }
}
