using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Accounts.Tests;

public sealed class DeadlineReminderPostgresTests : IAsyncLifetime
{
    private const string ConnectionEnvVar = "ACCOUNTS_POSTGRES_TEST_CONNECTION";
    private readonly string? baseConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvVar);
    private readonly string schemaName = "deadline_reminder_" + Guid.NewGuid().ToString("N");
    private DbContextOptions<AccountsDbContext>? options;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(baseConnectionString)) return;
        await using var admin = new NpgsqlConnection(baseConnectionString);
        await admin.OpenAsync();
        await using (var command = admin.CreateCommand())
        {
            command.CommandText = $"CREATE SCHEMA \"{schemaName}\"";
            await command.ExecuteNonQueryAsync();
        }
        var scoped = new NpgsqlConnectionStringBuilder(baseConnectionString) { SearchPath = schemaName }.ConnectionString;
        options = new DbContextOptionsBuilder<AccountsDbContext>().UseNpgsql(scoped).Options;
        await using var db = new AccountsDbContext(options);
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(baseConnectionString)) return;
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(baseConnectionString);
        await admin.OpenAsync();
        await using var command = admin.CreateCommand();
        command.CommandText = $"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE";
        await command.ExecuteNonQueryAsync();
    }

    [PostgresFact]
    public async Task FailedDelivery_AlertsStaysVisibleRetriesAndDeliversWithoutDuplicateIntent()
    {
        var configured = options ?? throw new InvalidOperationException($"{ConnectionEnvVar} is required.");
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        var clock = new MutableTimeProvider(now);
        var deliveryOptions = Options.Create(new DeadlineDeliveryConfig
        {
            Enabled = true,
            DueSoonDays = 30,
            BatchSize = 10,
            RetryBaseMinutes = 5,
            RetryMaximumMinutes = 60,
            DeliveryLeaseMinutes = 10,
            MaximumAutomaticAttempts = 3,
            OperatorAlertAfterAttempts = 1
        });
        var provider = new ControllableProvider { Fail = true };
        var alerts = new CapturingAlertSink();

        await using var db = new AccountsDbContext(configured);
        var tenant = new Tenant { Name = "Deadline test firm", Slug = "deadline-" + Guid.NewGuid().ToString("N") };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        var company = new Company
        {
            TenantId = tenant.Id,
            LegalName = "Reminder Queue Limited",
            CompanyType = CompanyType.Private,
            IncorporationDate = new DateOnly(2026, 1, 1),
            IsTrading = true
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var period = new AccountingPeriod
        {
            CompanyId = company.Id,
            PeriodStart = company.IncorporationDate,
            PeriodEnd = new DateOnly(2026, 12, 31),
            IsFirstYear = true
        };
        db.AccountingPeriods.Add(period);
        await db.SaveChangesAsync();
        db.FilingDeadlines.Add(new FilingDeadline
        {
            CompanyId = company.Id,
            PeriodId = period.Id,
            DeadlineType = DeadlineType.CRO,
            CalculatedDueDate = new DateOnly(2026, 7, 24),
            DueDate = new DateOnly(2026, 7, 24),
            CalculationFingerprintSha256 = new string('a', 64)
        });
        await db.SaveChangesAsync();

        using var metrics = new PlatformMetrics(clock, Options.Create(new PlatformMetricsConfig()));
        var service = new DeadlineReminderService(
            db,
            new DeadlineReminderPlanner(clock, deliveryOptions),
            provider,
            alerts,
            metrics,
            deliveryOptions,
            clock);

        var failed = await service.RunTenantAsync(tenant.Id, now.UtcDateTime, "scheduled");

        Assert.Equal(PlatformJobStatus.Failed, failed.Status);
        Assert.Equal(1, failed.EnqueuedCount);
        Assert.Equal(1, failed.FailedCount);
        Assert.Equal(64, failed.EvidenceSha256.Length);
        var retry = await db.DeadlineReminderOutbox.AsNoTracking().SingleAsync();
        Assert.Equal(DeadlineReminderState.RetryScheduled, retry.State);
        Assert.Equal(1, retry.AttemptCount);
        Assert.Equal(DeadlineReminderFailureCodes.ProviderUnavailable, retry.LastFailureCode);
        Assert.Single(alerts.Alerts);
        Assert.Equal(retry.Id, alerts.Alerts[0].OutboxId);
        var queue = await service.GetAtRiskQueueAsync(tenant.Id);
        var queueItem = Assert.Single(queue);
        Assert.Equal("Reminder Queue Limited", queueItem.CompanyLegalName);
        Assert.Equal(DeadlineReminderState.RetryScheduled, queueItem.State);

        Assert.True(await service.RetryNowAsync(tenant.Id, retry.Id));
        provider.Fail = false;
        clock.UtcNow = now.AddMinutes(1);
        var retrySlot = clock.UtcNow.UtcDateTime;
        var delivered = await service.RunTenantAsync(tenant.Id, retrySlot, "manual");

        Assert.Equal(PlatformJobStatus.Succeeded, delivered.Status);
        Assert.Equal(0, delivered.EnqueuedCount);
        Assert.Equal(1, delivered.DeliveredCount);
        var retained = await db.DeadlineReminderOutbox.AsNoTracking().SingleAsync();
        Assert.Equal(DeadlineReminderState.Delivered, retained.State);
        Assert.Equal("provider:delivered", retained.ProviderDeliveryReference);
        Assert.Empty(await service.GetAtRiskQueueAsync(tenant.Id));

        var replay = await service.RunTenantAsync(tenant.Id, retrySlot, "manual");
        Assert.Equal(delivered.JobRunId, replay.JobRunId);
        Assert.Equal(2, provider.CallCount);
        Assert.Equal(2, await db.PlatformJobRuns.CountAsync());
        Assert.Single(await db.DeadlineReminderOutbox.ToListAsync());
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class ControllableProvider : IDeadlineReminderProvider
    {
        public bool Fail { get; set; }
        public int CallCount { get; private set; }

        public Task<DeadlineReminderDeliveryResult> DeliverAsync(
            DeadlineReminderDeliveryCommand command,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(Fail
                ? DeadlineReminderDeliveryResult.Failure(DeadlineReminderFailureCodes.ProviderUnavailable)
                : DeadlineReminderDeliveryResult.Success("provider:delivered"));
        }
    }

    private sealed class CapturingAlertSink : IOperatorAlertSink
    {
        public List<DeadlineReminderFailureAlert> Alerts { get; } = [];

        public Task AlertDeadlineReminderFailureAsync(
            DeadlineReminderFailureAlert alert,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Alerts.Add(alert);
            return Task.CompletedTask;
        }
    }

    private sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionEnvVar)))
                Skip = $"{ConnectionEnvVar} is not set.";
        }
    }
}
