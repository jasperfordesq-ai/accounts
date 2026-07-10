using System.Data;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Accounts.Api.Services;

public sealed record DeadlineReminderRunResult(
    Guid JobRunId,
    int ExaminedCount,
    int EnqueuedCount,
    int DeliveredCount,
    int FailedCount,
    int CancelledCount,
    PlatformJobStatus Status,
    string EvidenceSha256);

public sealed record DeadlineRiskQueueItem(
    Guid OutboxId,
    int CompanyId,
    string CompanyLegalName,
    int PeriodId,
    DeadlineType DeadlineType,
    DeadlineReminderKind ReminderKind,
    DeadlineReminderState State,
    DateOnly DueDate,
    int AttemptCount,
    DateTime NextAttemptAtUtc,
    string? LastFailureCode);

/// <summary>
/// Durable planner/delivery boundary. Provider calls occur only after a compare-and-set claim has
/// committed, and no database transaction remains open while the external provider is contacted.
/// </summary>
public sealed class DeadlineReminderService(
    AccountsDbContext db,
    DeadlineReminderPlanner planner,
    IDeadlineReminderProvider provider,
    IOperatorAlertSink operatorAlerts,
    PlatformMetrics platformMetrics,
    IOptions<DeadlineDeliveryConfig> configuredOptions,
    TimeProvider timeProvider)
{
    private readonly DeadlineDeliveryConfig options = configuredOptions.Value;

    public async Task<DeadlineReminderRunResult> RunTenantAsync(
        int tenantId,
        DateTime scheduledSlotUtc,
        string trigger,
        CancellationToken cancellationToken = default)
    {
        if (tenantId <= 0) throw new ArgumentOutOfRangeException(nameof(tenantId));
        if (scheduledSlotUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("The scheduled slot must be UTC.", nameof(scheduledSlotUtc));
        trigger = NormalizeTrigger(trigger);
        var startedAt = UtcNow();
        if (scheduledSlotUtc > startedAt) throw new BusinessRuleException("A deadline-reminder slot cannot be in the future.");
        var stopwatch = Stopwatch.StartNew();
        PlatformJobRun? job = null;
        try
        {
            var planning = await PlanAsync(tenantId, scheduledSlotUtc, trigger, startedAt, cancellationToken);
            job = planning.Job;
            if (planning.AlreadyCompleted is not null) return planning.AlreadyCompleted;

            var delivery = await DeliverClaimableAsync(tenantId, cancellationToken);
            var status = delivery.FailedCount > 0
                ? delivery.DeliveredCount > 0 ? PlatformJobStatus.PartiallySucceeded : PlatformJobStatus.Failed
                : PlatformJobStatus.Succeeded;
            var completedAt = UtcNow();
            var evidence = EvidenceSha256(
                job!.Id,
                tenantId,
                scheduledSlotUtc,
                planning.ExaminedCount,
                planning.EnqueuedCount,
                delivery.DeliveredCount,
                delivery.FailedCount,
                planning.CancelledCount,
                status);
            job.Status = status;
            job.CompletedAtUtc = completedAt;
            job.ExaminedCount = planning.ExaminedCount;
            job.EnqueuedCount = planning.EnqueuedCount;
            job.DeliveredCount = delivery.DeliveredCount;
            job.FailedCount = delivery.FailedCount;
            job.CancelledCount = planning.CancelledCount;
            job.FailureCode = delivery.FailedCount > 0 ? "delivery-failures" : null;
            job.EvidenceSha256 = evidence;
            await db.SaveChangesAsync(cancellationToken);
            platformMetrics.RecordJob(PlatformJobKind.DeadlineReminder, status, stopwatch.Elapsed);
            return new DeadlineReminderRunResult(
                job.Id,
                planning.ExaminedCount,
                planning.EnqueuedCount,
                delivery.DeliveredCount,
                delivery.FailedCount,
                planning.CancelledCount,
                status,
                evidence);
        }
        catch
        {
            if (job is not null && job.Status == PlatformJobStatus.Running)
            {
                job.Status = PlatformJobStatus.Failed;
                job.CompletedAtUtc = UtcNow();
                job.FailureCode = "job-exception";
                job.EvidenceSha256 = EvidenceSha256(
                    job.Id,
                    tenantId,
                    scheduledSlotUtc,
                    job.ExaminedCount,
                    job.EnqueuedCount,
                    job.DeliveredCount,
                    Math.Max(1, job.FailedCount),
                    job.CancelledCount,
                    job.Status);
                try { await db.SaveChangesAsync(cancellationToken); }
                catch { /* Preserve the original scheduler failure. */ }
            }
            platformMetrics.RecordJob(PlatformJobKind.DeadlineReminder, PlatformJobStatus.Failed, stopwatch.Elapsed);
            throw;
        }
    }

    public async Task<IReadOnlyList<DeadlineRiskQueueItem>> GetAtRiskQueueAsync(
        int tenantId,
        CancellationToken cancellationToken = default) =>
        await db.DeadlineReminderOutbox
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId
                && item.State != DeadlineReminderState.Delivered
                && item.State != DeadlineReminderState.Cancelled
                && item.State != DeadlineReminderState.Superseded)
            .OrderBy(item => item.ObservedDueDate)
            .ThenByDescending(item => item.AttemptCount)
            .ThenBy(item => item.Id)
            .Take(500)
            .Select(item => new DeadlineRiskQueueItem(
                item.Id,
                item.CompanyId,
                item.Company.LegalName,
                item.PeriodId,
                item.DeadlineType,
                item.ReminderKind,
                item.State,
                item.ObservedDueDate,
                item.AttemptCount,
                item.NextAttemptAtUtc,
                item.LastFailureCode))
            .ToListAsync(cancellationToken);

    public async Task<bool> RetryNowAsync(
        int tenantId,
        Guid outboxId,
        CancellationToken cancellationToken = default)
    {
        var now = UtcNow();
        var updated = await db.DeadlineReminderOutbox
            .Where(item => item.Id == outboxId
                && item.TenantId == tenantId
                && item.State == DeadlineReminderState.RetryScheduled)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.NextAttemptAtUtc, now)
                .SetProperty(item => item.AttemptCount, 0)
                .SetProperty(item => item.UpdatedAtUtc, now)
                .SetProperty(item => item.Revision, item => item.Revision + 1),
                cancellationToken);
        return updated == 1;
    }

    public async Task<PlatformOperationalMetricInputs> OperationalMetricsAsync(
        int tenantId,
        CancellationToken cancellationToken = default)
    {
        var pending = await db.DeadlineReminderOutbox.CountAsync(item => item.TenantId == tenantId
            && (item.State == DeadlineReminderState.Pending
                || item.State == DeadlineReminderState.Delivering
                || item.State == DeadlineReminderState.RetryScheduled), cancellationToken);
        var failed = await db.DeadlineReminderOutbox.CountAsync(item => item.TenantId == tenantId
            && item.State == DeadlineReminderState.RetryScheduled
            && item.LastFailureCode != null, cancellationToken);
        var durableJobFailures = await db.PlatformJobRuns.CountAsync(item => item.TenantId == tenantId
            && item.Status == PlatformJobStatus.Failed, cancellationToken);
        var lastBackup = await db.PlatformJobRuns
            .Where(item => item.TenantId == tenantId
                && item.JobKind == PlatformJobKind.Backup
                && item.Status == PlatformJobStatus.Succeeded)
            .MaxAsync(item => (DateTime?)item.CompletedAtUtc, cancellationToken);
        return new PlatformOperationalMetricInputs(pending, failed, lastBackup, durableJobFailures);
    }

    private async Task<PlanningResult> PlanAsync(
        int tenantId,
        DateTime scheduledSlotUtc,
        string trigger,
        DateTime startedAt,
        CancellationToken cancellationToken)
    {
        IDbContextTransaction? transaction = null;
        if (db.Database.IsRelational())
            transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await using var transactionLease = transaction;
        try
        {
            var existing = await db.PlatformJobRuns.SingleOrDefaultAsync(item =>
                item.TenantId == tenantId
                && item.JobKind == PlatformJobKind.DeadlineReminder
                && item.ScheduledSlotUtc == scheduledSlotUtc,
                cancellationToken);
            if (existing is not null && existing.Status != PlatformJobStatus.Running)
            {
                var result = new DeadlineReminderRunResult(
                    existing.Id,
                    existing.ExaminedCount,
                    existing.EnqueuedCount,
                    existing.DeliveredCount,
                    existing.FailedCount,
                    existing.CancelledCount,
                    existing.Status,
                    existing.EvidenceSha256 ?? new string('0', 64));
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return new PlanningResult(existing, 0, 0, 0, result);
            }
            if (existing is not null)
                throw new BusinessRuleException("This deadline-reminder slot is already running.");

            var job = new PlatformJobRun
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                JobKind = PlatformJobKind.DeadlineReminder,
                Trigger = trigger,
                Status = PlatformJobStatus.Running,
                ScheduledSlotUtc = scheduledSlotUtc,
                StartedAtUtc = startedAt,
                CreatedAtUtc = startedAt
            };
            db.PlatformJobRuns.Add(job);

            var staleLeaseCutoff = startedAt.AddMinutes(-options.DeliveryLeaseMinutes);
            await db.DeadlineReminderOutbox
                .Where(item => item.TenantId == tenantId
                    && item.State == DeadlineReminderState.Delivering
                    && item.LastAttemptAtUtc <= staleLeaseCutoff)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.State, DeadlineReminderState.RetryScheduled)
                    .SetProperty(item => item.LastFailureCode, "delivery-lease-expired")
                    .SetProperty(item => item.NextAttemptAtUtc, startedAt)
                    .SetProperty(item => item.UpdatedAtUtc, startedAt)
                    .SetProperty(item => item.Revision, item => item.Revision + 1),
                    cancellationToken);

            var subjects = await db.FilingDeadlines
                .AsNoTracking()
                .Where(deadline => deadline.Company.TenantId == tenantId)
                .OrderBy(deadline => deadline.Id)
                .Select(deadline => new DeadlineReminderSubject(
                    tenantId,
                    deadline.CompanyId,
                    deadline.PeriodId,
                    deadline.Id,
                    deadline.DeadlineType,
                    deadline.DueDate,
                    deadline.FiledDate,
                    deadline.CalculationFingerprintSha256))
                .ToListAsync(cancellationToken);
            var deadlineIds = subjects.Select(subject => subject.FilingDeadlineId).ToArray();
            var histories = await db.DeadlineReminderOutbox
                .Where(item => item.TenantId == tenantId && deadlineIds.Contains(item.FilingDeadlineId))
                .OrderBy(item => item.CreatedAtUtc)
                .ToListAsync(cancellationToken);
            var byDeadline = histories.ToLookup(item => item.FilingDeadlineId);
            var enqueued = 0;
            var cancelled = 0;
            foreach (var subject in subjects)
            {
                var rows = byDeadline[subject.FilingDeadlineId].ToArray();
                var plan = planner.Plan(subject, rows.Select(History).ToArray());
                foreach (var action in plan.Actions)
                {
                    if (action.ActionType == DeadlineReminderPlanActionType.Enqueue)
                    {
                        db.DeadlineReminderOutbox.Add(new DeadlineReminderOutbox
                        {
                            Id = Guid.NewGuid(),
                            TenantId = subject.TenantId,
                            CompanyId = subject.CompanyId,
                            PeriodId = subject.PeriodId,
                            FilingDeadlineId = subject.FilingDeadlineId,
                            DeadlineType = subject.DeadlineType,
                            ReminderKind = action.ReminderKind,
                            State = DeadlineReminderState.Pending,
                            ObservedDueDate = action.ObservedDueDate,
                            ObservedCalculationFingerprintSha256 = subject.CalculationFingerprintSha256,
                            DeduplicationKeySha256 = action.DeduplicationKeySha256!,
                            CreatedAtUtc = startedAt,
                            UpdatedAtUtc = startedAt,
                            NextAttemptAtUtc = startedAt
                        });
                        enqueued++;
                        continue;
                    }

                    var row = rows.SingleOrDefault(item => item.Id == action.ExistingOutboxId);
                    if (row is null || row.State is DeadlineReminderState.Delivered or DeadlineReminderState.Cancelled or DeadlineReminderState.Superseded)
                        continue;
                    row.State = action.ActionType == DeadlineReminderPlanActionType.Cancel
                        ? DeadlineReminderState.Cancelled
                        : DeadlineReminderState.Superseded;
                    row.CancelledAtUtc = startedAt;
                    row.UpdatedAtUtc = startedAt;
                    row.Revision++;
                    cancelled++;
                }
            }

            job.ExaminedCount = subjects.Count;
            job.EnqueuedCount = enqueued;
            job.CancelledCount = cancelled;
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return new PlanningResult(job, subjects.Count, enqueued, cancelled, null);
        }
        catch (DbUpdateException error) when (IsUniqueViolation(error))
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            var existing = await db.PlatformJobRuns.AsNoTracking().SingleAsync(item =>
                item.TenantId == tenantId
                && item.JobKind == PlatformJobKind.DeadlineReminder
                && item.ScheduledSlotUtc == scheduledSlotUtc,
                cancellationToken);
            if (existing.Status == PlatformJobStatus.Running)
                throw new BusinessRuleException("This deadline-reminder slot is already running.");
            return new PlanningResult(
                existing,
                0,
                0,
                0,
                new DeadlineReminderRunResult(
                    existing.Id,
                    existing.ExaminedCount,
                    existing.EnqueuedCount,
                    existing.DeliveredCount,
                    existing.FailedCount,
                    existing.CancelledCount,
                    existing.Status,
                    existing.EvidenceSha256 ?? new string('0', 64)));
        }
    }

    private async Task<DeliveryCounts> DeliverClaimableAsync(int tenantId, CancellationToken cancellationToken)
    {
        var delivered = 0;
        var failed = 0;
        for (var index = 0; index < options.BatchSize; index++)
        {
            var now = UtcNow();
            var candidate = await db.DeadlineReminderOutbox.AsNoTracking()
                .Where(item => item.TenantId == tenantId
                    && (item.State == DeadlineReminderState.Pending || item.State == DeadlineReminderState.RetryScheduled)
                    && item.NextAttemptAtUtc <= now
                    && item.AttemptCount < options.MaximumAutomaticAttempts)
                .OrderBy(item => item.NextAttemptAtUtc)
                .ThenBy(item => item.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (candidate is null) break;

            var claimed = await db.DeadlineReminderOutbox
                .Where(item => item.Id == candidate.Id
                    && item.TenantId == tenantId
                    && item.Revision == candidate.Revision
                    && (item.State == DeadlineReminderState.Pending || item.State == DeadlineReminderState.RetryScheduled)
                    && item.NextAttemptAtUtc <= now)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.State, DeadlineReminderState.Delivering)
                    .SetProperty(item => item.AttemptCount, item => item.AttemptCount + 1)
                    .SetProperty(item => item.LastAttemptAtUtc, now)
                    .SetProperty(item => item.UpdatedAtUtc, now)
                    .SetProperty(item => item.Revision, item => item.Revision + 1),
                    cancellationToken);
            if (claimed == 0) continue;

            var attempt = candidate.AttemptCount + 1;
            var stopwatch = Stopwatch.StartNew();
            var result = await provider.DeliverAsync(
                new DeadlineReminderDeliveryCommand(
                    candidate.Id,
                    tenantId,
                    candidate.ReminderKind,
                    candidate.DeadlineType,
                    candidate.ObservedDueDate,
                    candidate.DeduplicationKeySha256),
                cancellationToken);
            platformMetrics.RecordReminderDelivery(candidate.ReminderKind, result.Delivered, stopwatch.Elapsed);
            if (result.Delivered)
            {
                var completedAt = UtcNow();
                var rows = await db.DeadlineReminderOutbox
                    .Where(item => item.Id == candidate.Id
                        && item.TenantId == tenantId
                        && item.Revision == candidate.Revision + 1
                        && item.State == DeadlineReminderState.Delivering)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(item => item.State, DeadlineReminderState.Delivered)
                        .SetProperty(item => item.DeliveredAtUtc, completedAt)
                        .SetProperty(item => item.ProviderDeliveryReference, result.ProviderDeliveryReference)
                        .SetProperty(item => item.LastFailureCode, (string?)null)
                        .SetProperty(item => item.UpdatedAtUtc, completedAt)
                        .SetProperty(item => item.Revision, item => item.Revision + 1),
                        cancellationToken);
                if (rows == 1) delivered++;
                continue;
            }

            var failedAt = UtcNow();
            var failureCode = DeadlineReminderFailureCodes.Normalize(result.FailureCode);
            var retryAt = failedAt.AddMinutes(RetryDelayMinutes(attempt));
            var failedRows = await db.DeadlineReminderOutbox
                .Where(item => item.Id == candidate.Id
                    && item.TenantId == tenantId
                    && item.Revision == candidate.Revision + 1
                    && item.State == DeadlineReminderState.Delivering)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.State, DeadlineReminderState.RetryScheduled)
                    .SetProperty(item => item.NextAttemptAtUtc, retryAt)
                    .SetProperty(item => item.LastFailureCode, failureCode)
                    .SetProperty(item => item.UpdatedAtUtc, failedAt)
                    .SetProperty(item => item.Revision, item => item.Revision + 1),
                    cancellationToken);
            if (failedRows == 1)
            {
                failed++;
                if (attempt == options.OperatorAlertAfterAttempts || attempt == options.MaximumAutomaticAttempts)
                {
                    await operatorAlerts.AlertDeadlineReminderFailureAsync(
                        new DeadlineReminderFailureAlert(candidate.Id, attempt, failureCode),
                        cancellationToken);
                }
            }
        }
        return new DeliveryCounts(delivered, failed);
    }

    private int RetryDelayMinutes(int attempt)
    {
        var exponent = Math.Min(Math.Max(0, attempt - 1), 20);
        var delay = options.RetryBaseMinutes * Math.Pow(2, exponent);
        return (int)Math.Min(options.RetryMaximumMinutes, delay);
    }

    private DateTime UtcNow()
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc);
    }

    private static DeadlineReminderHistory History(DeadlineReminderOutbox item) => new(
        item.Id,
        item.ReminderKind,
        item.State,
        item.ObservedDueDate,
        item.DeduplicationKeySha256,
        item.CreatedAtUtc);

    private static string NormalizeTrigger(string value) => value?.Trim().ToLowerInvariant() switch
    {
        "scheduled" => "scheduled",
        "manual" => "manual",
        _ => throw new BusinessRuleException("Deadline reminder trigger is invalid.")
    };

    private static bool IsUniqueViolation(DbUpdateException error) =>
        error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static string EvidenceSha256(
        Guid jobId,
        int tenantId,
        DateTime slot,
        int examined,
        int enqueued,
        int delivered,
        int failed,
        int cancelled,
        PlatformJobStatus status)
    {
        var canonical = string.Join('|',
            "deadline-job-v1",
            jobId.ToString("N"),
            tenantId,
            slot.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            examined,
            enqueued,
            delivered,
            failed,
            cancelled,
            status);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private sealed record PlanningResult(
        PlatformJobRun Job,
        int ExaminedCount,
        int EnqueuedCount,
        int CancelledCount,
        DeadlineReminderRunResult? AlreadyCompleted);
    private sealed record DeliveryCounts(int DeliveredCount, int FailedCount);
}

public sealed class DeadlineReminderWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<DeadlineDeliveryConfig> configuredOptions,
    TimeProvider timeProvider,
    ILogger<DeadlineReminderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = configuredOptions.Value;
        if (!options.Enabled) return;
        var interval = TimeSpan.FromMinutes(options.SchedulerIntervalMinutes);
        await RunAllTenantsAsync(interval, stoppingToken);
        using var timer = new PeriodicTimer(interval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RunAllTenantsAsync(interval, stoppingToken);
    }

    private async Task RunAllTenantsAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        IReadOnlyList<int> tenantIds;
        try
        {
            await using var discovery = scopeFactory.CreateAsyncScope();
            tenantIds = await discovery.ServiceProvider
                .GetRequiredService<DatabaseTenantBootstrapResolver>()
                .ListTenantIdsForJobsAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception error)
        {
            logger.LogError(error, "Deadline reminder tenant discovery failed; scheduling will retry on the next interval");
            return;
        }

        var failedTenants = 0;
        foreach (var tenantId in tenantIds)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<DatabaseTenantContext>().SetResolvedTenant(tenantId);
                var now = timeProvider.GetUtcNow().UtcDateTime;
                var slotTicks = now.Ticks - now.Ticks % interval.Ticks;
                var slot = new DateTime(slotTicks, DateTimeKind.Utc);
                await scope.ServiceProvider.GetRequiredService<DeadlineReminderService>()
                    .RunTenantAsync(tenantId, slot, "scheduled", cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                failedTenants++;
                logger.LogError(error, "A tenant-scoped deadline reminder job failed");
            }
        }

        if (failedTenants > 0)
            logger.LogWarning("Deadline reminder scheduling completed with {FailedTenantCount} failed tenant scopes", failedTenants);
    }
}
