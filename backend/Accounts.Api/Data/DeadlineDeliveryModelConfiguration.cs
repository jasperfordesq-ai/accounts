using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Data;

public static class DeadlineDeliveryModelConfiguration
{
    public static void ConfigureDeadlineDelivery(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DeadlineReminderOutbox>(entity =>
        {
            entity.ToTable("deadline_reminder_outbox", table =>
            {
                table.HasCheckConstraint(
                    "CK_deadline_reminder_outbox_hashes",
                    "length(\"DeduplicationKeySha256\") = 64 AND (\"ObservedCalculationFingerprintSha256\" IS NULL OR length(\"ObservedCalculationFingerprintSha256\") = 64)");
                table.HasCheckConstraint(
                    "CK_deadline_reminder_outbox_attempts",
                    "\"AttemptCount\" >= 0 AND \"Revision\" > 0");
                table.HasCheckConstraint(
                    "CK_deadline_reminder_outbox_chronology",
                    "\"UpdatedAtUtc\" >= \"CreatedAtUtc\" AND \"NextAttemptAtUtc\" >= \"CreatedAtUtc\" AND (\"LastAttemptAtUtc\" IS NULL OR \"LastAttemptAtUtc\" >= \"CreatedAtUtc\")");
                table.HasCheckConstraint(
                    "CK_deadline_reminder_outbox_terminal_state",
                    "((\"State\" = 'Delivered') = (\"DeliveredAtUtc\" IS NOT NULL)) AND ((\"State\" IN ('Cancelled', 'Superseded')) = (\"CancelledAtUtc\" IS NOT NULL))");
                table.HasCheckConstraint(
                    "CK_deadline_reminder_outbox_provider_reference",
                    "\"ProviderDeliveryReference\" IS NULL OR \"State\" = 'Delivered'");
            });
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ObservedCalculationFingerprintSha256).HasMaxLength(64);
            entity.Property(item => item.DeduplicationKeySha256).HasMaxLength(64).IsRequired();
            entity.Property(item => item.LastFailureCode).HasMaxLength(80);
            entity.Property(item => item.ProviderDeliveryReference).HasMaxLength(160);
            entity.Property(item => item.Revision).IsConcurrencyToken().HasDefaultValue(1);
            entity.HasOne(item => item.Tenant).WithMany().HasForeignKey(item => item.TenantId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.Company).WithMany().HasForeignKey(item => item.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.Period).WithMany().HasForeignKey(item => item.PeriodId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.FilingDeadline).WithMany().HasForeignKey(item => item.FilingDeadlineId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => new { item.TenantId, item.DeduplicationKeySha256 }).IsUnique();
            entity.HasIndex(item => new { item.TenantId, item.State, item.NextAttemptAtUtc });
            entity.HasIndex(item => new { item.TenantId, item.CompanyId, item.ObservedDueDate });
        });

        modelBuilder.Entity<PlatformJobRun>(entity =>
        {
            entity.ToTable("platform_job_runs", table =>
            {
                table.HasCheckConstraint(
                    "CK_platform_job_runs_counts",
                    "\"ExaminedCount\" >= 0 AND \"EnqueuedCount\" >= 0 AND \"DeliveredCount\" >= 0 AND \"FailedCount\" >= 0 AND \"CancelledCount\" >= 0");
                table.HasCheckConstraint(
                    "CK_platform_job_runs_chronology",
                    "\"StartedAtUtc\" >= \"ScheduledSlotUtc\" AND (\"CompletedAtUtc\" IS NULL OR \"CompletedAtUtc\" >= \"StartedAtUtc\")");
                table.HasCheckConstraint(
                    "CK_platform_job_runs_completion",
                    "(\"Status\" = 'Running' AND \"CompletedAtUtc\" IS NULL) OR (\"Status\" <> 'Running' AND \"CompletedAtUtc\" IS NOT NULL)");
                table.HasCheckConstraint(
                    "CK_platform_job_runs_evidence",
                    "\"EvidenceSha256\" IS NULL OR length(\"EvidenceSha256\") = 64");
            });
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Trigger).HasMaxLength(40).IsRequired();
            entity.Property(item => item.FailureCode).HasMaxLength(80);
            entity.Property(item => item.EvidenceSha256).HasMaxLength(64);
            entity.HasOne(item => item.Tenant).WithMany().HasForeignKey(item => item.TenantId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => new { item.TenantId, item.JobKind, item.ScheduledSlotUtc }).IsUnique();
            entity.HasIndex(item => new { item.TenantId, item.Status, item.StartedAtUtc });
        });
    }
}
