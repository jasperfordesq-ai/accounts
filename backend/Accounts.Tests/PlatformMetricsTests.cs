using System.Text.Json;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace Accounts.Tests;

public sealed class PlatformMetricsTests
{
    [Fact]
    public void Snapshot_UsesFiniteLowCardinalityDimensionsAndRaisesConfiguredAlerts()
    {
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        var clock = new MutableTimeProvider(now);
        using var metrics = new PlatformMetrics(clock, Options.Create(new PlatformMetricsConfig
        {
            Enabled = true,
            WindowMinutes = 15,
            RequestLatencyP95Milliseconds = 100,
            RequestErrorRatePercent = 1,
            JobFailureThreshold = 1,
            DatabaseActiveConnectionThreshold = 1,
            DocumentGenerationP95Milliseconds = 100,
            BackupMaximumAgeHours = 1,
            ReminderFailureBacklogThreshold = 1,
            ReminderPendingBacklogThreshold = 1,
            MaximumObservationsPerSeries = 1_000
        }));

        metrics.RecordRequest(
            "/api/companies/{companyId}/contacts/customer@example.invalid/{contactId}",
            "post",
            500,
            TimeSpan.FromMilliseconds(250));
        metrics.RecordJob(PlatformJobKind.DeadlineReminder, PlatformJobStatus.Failed, TimeSpan.FromMilliseconds(250));
        metrics.RecordDocument(DocumentMetricKind.AccountsPackage, false, TimeSpan.FromMilliseconds(250));
        metrics.RecordReminderDelivery(DeadlineReminderKind.Overdue, false, TimeSpan.FromMilliseconds(250));
        metrics.DatabaseConnectionOpened(TimeSpan.FromMilliseconds(250));

        var snapshot = metrics.Snapshot(new PlatformOperationalMetricInputs(
            ReminderPendingCount: 1,
            ReminderFailureCount: 1,
            LastSuccessfulBackupAtUtc: now.AddHours(-2).UtcDateTime,
            DurableJobFailureCount: 1));

        Assert.Equal(PlatformMetrics.SafeDimensionNames, snapshot.DimensionNames);
        Assert.All(snapshot.Requests, series => Assert.True(double.IsFinite(series.P95Milliseconds)));
        Assert.All(snapshot.Jobs, series => Assert.True(double.IsFinite(series.P95Milliseconds)));
        Assert.All(snapshot.Documents, series => Assert.True(double.IsFinite(series.P95Milliseconds)));
        Assert.All(snapshot.ReminderDeliveries, series => Assert.True(double.IsFinite(series.P95Milliseconds)));
        Assert.True(double.IsFinite(snapshot.DatabaseConnectionOpenP95Milliseconds));
        Assert.True(double.IsFinite(snapshot.LastSuccessfulBackupAgeHours!.Value));
        Assert.All(snapshot.Alerts, alert =>
        {
            Assert.True(double.IsFinite(alert.CurrentValue));
            Assert.True(double.IsFinite(alert.Threshold));
        });
        Assert.Equal(
            [
                "backup-evidence-stale",
                "database-pool-pressure",
                "document-generation-slow",
                "reminder-backlog-high",
                "reminder-delivery-failures",
                "request-error-rate-high",
                "request-latency-high",
                "scheduled-job-failures"
            ],
            snapshot.Alerts.Select(alert => alert.Code).Order(StringComparer.Ordinal).ToArray());

        var serialized = JsonSerializer.Serialize(snapshot);
        Assert.DoesNotContain("customer@example.invalid", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tenantId", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("companyId", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("userId", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("recipient", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.All(snapshot.Requests, series => Assert.DoesNotContain('@', series.RouteTemplate));

        metrics.DatabaseConnectionClosed();
        Assert.Equal(0, metrics.Snapshot(new PlatformOperationalMetricInputs(0, 0, now.UtcDateTime, 0)).DatabaseActiveConnections);
    }

    [Fact]
    public void AlertState_DeduplicatesPerTenantRepeatsAndRearmsResolvedConditions()
    {
        var state = new PlatformMetricAlertState();
        var now = new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);
        var repeat = TimeSpan.FromMinutes(30);

        Assert.True(state.TryAcquire(1, "request-latency-high", now, repeat));
        Assert.False(state.TryAcquire(1, "request-latency-high", now.AddMinutes(29), repeat));
        Assert.True(state.TryAcquire(2, "request-latency-high", now.AddMinutes(1), repeat));
        Assert.True(state.TryAcquire(1, "request-latency-high", now.AddMinutes(30), repeat));

        state.ClearResolved(1, new HashSet<string>(StringComparer.Ordinal));

        Assert.True(state.TryAcquire(1, "request-latency-high", now.AddMinutes(31), repeat));
        Assert.False(state.TryAcquire(2, "request-latency-high", now.AddMinutes(2), repeat));
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
