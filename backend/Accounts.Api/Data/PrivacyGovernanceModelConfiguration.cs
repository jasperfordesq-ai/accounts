using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Data;

public static class PrivacyGovernanceModelConfiguration
{
    public static void ConfigurePrivacyGovernance(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LoginSecurityEvent>(entity =>
        {
            entity.ToTable("login_security_events", table =>
            {
                table.HasCheckConstraint(
                    "CK_login_security_events_fingerprint",
                    "length(\"IdentifierFingerprint\") = 64");
                table.HasCheckConstraint(
                    "CK_login_security_events_retention_window",
                    "\"ExpiresAtUtc\" > \"OccurredAtUtc\"");
            });
            entity.HasKey(item => item.Id);
            entity.Property(item => item.IdentifierFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(item => item.OutcomeCode).HasMaxLength(64).IsRequired();
            entity.Property(item => item.ReasonCode).HasMaxLength(64).IsRequired();
            entity.Property(item => item.CorrelationId).HasMaxLength(128);
            entity.HasOne<Tenant>().WithMany().HasForeignKey(item => item.TenantId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserAccount>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => item.ExpiresAtUtc);
            entity.HasIndex(item => new { item.TenantId, item.UserId, item.OccurredAtUtc });
        });

        modelBuilder.Entity<PrivacySubjectRequest>(entity =>
        {
            entity.ToTable("privacy_subject_requests", table =>
            {
                table.HasCheckConstraint(
                    "CK_privacy_subject_requests_export",
                    "(\"ExportSha256\" IS NULL AND \"ExportByteCount\" IS NULL) OR (length(\"ExportSha256\") = 64 AND \"ExportByteCount\" > 0)");
                table.HasCheckConstraint(
                    "CK_privacy_subject_requests_statutory_override",
                    "(NOT \"StatutoryRetentionOverrideApplied\" AND \"StatutoryRetentionLegalBasis\" IS NULL AND \"StatutoryRetainUntilUtc\" IS NULL AND \"StatutoryRetentionInventoryJson\" IS NULL) OR (\"StatutoryRetentionOverrideApplied\" AND length(\"StatutoryRetentionLegalBasis\") >= 20 AND \"StatutoryRetainUntilUtc\" IS NOT NULL AND \"StatutoryRetentionInventoryJson\" IS NOT NULL)");
                table.HasCheckConstraint(
                    "CK_privacy_subject_requests_retention",
                    "\"MetadataExpiresAtUtc\" > \"RequestedAtUtc\"");
            });
            entity.HasKey(item => item.Id);
            entity.Property(item => item.RequestKind).HasMaxLength(40).IsRequired();
            entity.Property(item => item.State).HasMaxLength(80).IsRequired();
            entity.Property(item => item.DecisionReason).HasMaxLength(2000);
            entity.Property(item => item.ExportSha256).HasMaxLength(64);
            entity.Property(item => item.LocatedRecordCountsJson).HasColumnType("jsonb");
            entity.Property(item => item.StatutoryRetentionLegalBasis).HasMaxLength(1000);
            entity.Property(item => item.StatutoryRetentionInventoryJson).HasColumnType("jsonb");
            entity.HasOne<Tenant>().WithMany().HasForeignKey(item => item.TenantId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserAccount>().WithMany().HasForeignKey(item => item.SubjectUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserAccount>().WithMany().HasForeignKey(item => item.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserAccount>().WithMany().HasForeignKey(item => item.DecidedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => new { item.TenantId, item.SubjectUserId, item.RequestedAtUtc });
            entity.HasIndex(item => item.MetadataExpiresAtUtc);
        });

        modelBuilder.Entity<PrivacyIncidentExercise>(entity =>
        {
            entity.ToTable("privacy_incident_exercises", table =>
            {
                table.HasCheckConstraint(
                    "CK_privacy_incident_exercises_sha256",
                    "length(\"ScenarioSha256\") = 64 AND length(\"EvidenceManifestSha256\") = 64");
                table.HasCheckConstraint(
                    "CK_privacy_incident_exercises_chronology",
                    "\"DetectedAtUtc\" <= \"NotificationRoutedAtUtc\" AND \"NotificationRoutedAtUtc\" <= \"ContainedAtUtc\" AND \"ContainedAtUtc\" <= \"EvidencePreservedAtUtc\" AND \"EvidencePreservedAtUtc\" <= \"RecoveryVerifiedAtUtc\" AND \"RecoveryVerifiedAtUtc\" <= \"ReviewedAtUtc\"");
                table.HasCheckConstraint(
                    "CK_privacy_incident_exercises_synthetic_integrity",
                    "\"UsedSyntheticDataOnly\" AND \"TenantIsolationVerified\" AND \"AuditIntegrityVerified\" AND \"FinancialIntegrityVerified\"");
            });
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ExerciseKind).HasMaxLength(80).IsRequired();
            entity.Property(item => item.ReleaseCandidate).HasMaxLength(200).IsRequired();
            entity.Property(item => item.EnvironmentName).HasMaxLength(100).IsRequired();
            entity.Property(item => item.ScenarioSha256).HasMaxLength(64).IsRequired();
            entity.Property(item => item.NotificationDecision).HasMaxLength(1000).IsRequired();
            entity.Property(item => item.EvidenceManifestSha256).HasMaxLength(64).IsRequired();
            entity.Property(item => item.ReviewDecision).HasMaxLength(50).IsRequired();
            entity.HasOne<Tenant>().WithMany().HasForeignKey(item => item.TenantId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserAccount>().WithMany().HasForeignKey(item => item.ReviewedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => new { item.TenantId, item.ReviewedAtUtc });
            entity.HasIndex(item => new { item.ReleaseCandidate, item.EnvironmentName });
        });
    }
}
