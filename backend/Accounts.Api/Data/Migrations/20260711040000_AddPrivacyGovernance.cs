using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    // Canonically ordered after production identity lifecycle hardening.
    /// <inheritdoc />
    public partial class AddPrivacyGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "login_security_events",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: true),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    IdentifierFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OutcomeCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_login_security_events", x => x.Id);
                    table.CheckConstraint("CK_login_security_events_fingerprint", "length(\"IdentifierFingerprint\") = 64");
                    table.CheckConstraint("CK_login_security_events_retention_window", "\"ExpiresAtUtc\" > \"OccurredAtUtc\"");
                });

            migrationBuilder.CreateTable(
                name: "privacy_incident_exercises",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    ExerciseKind = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ReleaseCandidate = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EnvironmentName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ScenarioSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DetectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NotificationRoutedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ContainedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EvidencePreservedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecoveryVerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReviewedByUserId = table.Column<int>(type: "integer", nullable: false),
                    NotificationDecision = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    EvidenceManifestSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReviewDecision = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    UsedSyntheticDataOnly = table.Column<bool>(type: "boolean", nullable: false),
                    TenantIsolationVerified = table.Column<bool>(type: "boolean", nullable: false),
                    AuditIntegrityVerified = table.Column<bool>(type: "boolean", nullable: false),
                    FinancialIntegrityVerified = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_privacy_incident_exercises", x => x.Id);
                    table.CheckConstraint("CK_privacy_incident_exercises_chronology", "\"DetectedAtUtc\" <= \"NotificationRoutedAtUtc\" AND \"NotificationRoutedAtUtc\" <= \"ContainedAtUtc\" AND \"ContainedAtUtc\" <= \"EvidencePreservedAtUtc\" AND \"EvidencePreservedAtUtc\" <= \"RecoveryVerifiedAtUtc\" AND \"RecoveryVerifiedAtUtc\" <= \"ReviewedAtUtc\"");
                    table.CheckConstraint("CK_privacy_incident_exercises_sha256", "length(\"ScenarioSha256\") = 64 AND length(\"EvidenceManifestSha256\") = 64");
                    table.CheckConstraint("CK_privacy_incident_exercises_synthetic_integrity", "\"UsedSyntheticDataOnly\" AND \"TenantIsolationVerified\" AND \"AuditIntegrityVerified\" AND \"FinancialIntegrityVerified\"");
                });

            migrationBuilder.CreateTable(
                name: "privacy_subject_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    SubjectUserId = table.Column<int>(type: "integer", nullable: false),
                    RequestKind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    State = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    RequestedByUserId = table.Column<int>(type: "integer", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DecidedByUserId = table.Column<int>(type: "integer", nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DecisionReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ExportSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ExportByteCount = table.Column<long>(type: "bigint", nullable: true),
                    LocatedRecordCountsJson = table.Column<string>(type: "jsonb", nullable: true),
                    StatutoryRetentionOverrideApplied = table.Column<bool>(type: "boolean", nullable: false),
                    StatutoryRetentionLegalBasis = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    StatutoryRetainUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StatutoryRetentionInventoryJson = table.Column<string>(type: "jsonb", nullable: true),
                    MetadataExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_privacy_subject_requests", x => x.Id);
                    table.CheckConstraint("CK_privacy_subject_requests_export", "(\"ExportSha256\" IS NULL AND \"ExportByteCount\" IS NULL) OR (length(\"ExportSha256\") = 64 AND \"ExportByteCount\" > 0)");
                    table.CheckConstraint("CK_privacy_subject_requests_retention", "\"MetadataExpiresAtUtc\" > \"RequestedAtUtc\"");
                    table.CheckConstraint("CK_privacy_subject_requests_statutory_override", "(NOT \"StatutoryRetentionOverrideApplied\" AND \"StatutoryRetentionLegalBasis\" IS NULL AND \"StatutoryRetainUntilUtc\" IS NULL AND \"StatutoryRetentionInventoryJson\" IS NULL) OR (\"StatutoryRetentionOverrideApplied\" AND length(\"StatutoryRetentionLegalBasis\") >= 20 AND \"StatutoryRetainUntilUtc\" IS NOT NULL AND \"StatutoryRetentionInventoryJson\" IS NOT NULL)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_login_security_events_ExpiresAtUtc",
                table: "login_security_events",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_login_security_events_TenantId_UserId_OccurredAtUtc",
                table: "login_security_events",
                columns: new[] { "TenantId", "UserId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_privacy_incident_exercises_ReleaseCandidate_EnvironmentName",
                table: "privacy_incident_exercises",
                columns: new[] { "ReleaseCandidate", "EnvironmentName" });

            migrationBuilder.CreateIndex(
                name: "IX_privacy_incident_exercises_TenantId_ReviewedAtUtc",
                table: "privacy_incident_exercises",
                columns: new[] { "TenantId", "ReviewedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_privacy_subject_requests_MetadataExpiresAtUtc",
                table: "privacy_subject_requests",
                column: "MetadataExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_privacy_subject_requests_TenantId_SubjectUserId_RequestedAt~",
                table: "privacy_subject_requests",
                columns: new[] { "TenantId", "SubjectUserId", "RequestedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "login_security_events");

            migrationBuilder.DropTable(
                name: "privacy_incident_exercises");

            migrationBuilder.DropTable(
                name: "privacy_subject_requests");
        }
    }
}
