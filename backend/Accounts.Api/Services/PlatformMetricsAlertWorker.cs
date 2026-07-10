using System.Collections.Concurrent;
using Accounts.Api.Data;
using Accounts.Api.Rules;
using Microsoft.Extensions.Options;

namespace Accounts.Api.Services;

public interface IPlatformMetricAlertSink
{
    Task AlertAsync(PlatformMetricAlert alert, CancellationToken cancellationToken = default);
}

public sealed class MonitoringPlatformMetricAlertSink(IErrorReporter errorReporter) : IPlatformMetricAlertSink
{
    public Task AlertAsync(PlatformMetricAlert alert, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = errorReporter.CaptureUnexpectedException(
            new PlatformMetricThresholdException(),
            new ErrorReportContext(
                "JOB",
                "/internal/jobs/platform-metrics",
                $"metric-{alert.Code}",
                $"platform-{alert.Code}"));
        return Task.CompletedTask;
    }
}

public sealed class PlatformMetricThresholdException()
    : Exception("A fixed platform metric threshold was exceeded; inspect the restricted metrics snapshot.");

/// <summary>
/// Internal alert deduplication. Tenant identifiers never leave the process or become metric/provider
/// dimensions; they only prevent one tenant's active condition suppressing another tenant's alert.
/// </summary>
public sealed class PlatformMetricAlertState
{
    private readonly ConcurrentDictionary<(int TenantId, string Code), DateTime> lastSent = new();

    public bool TryAcquire(int tenantId, string code, DateTime now, TimeSpan repeatInterval)
    {
        var key = (tenantId, code);
        while (true)
        {
            if (!lastSent.TryGetValue(key, out var previous))
                return lastSent.TryAdd(key, now);
            if (now - previous < repeatInterval) return false;
            if (lastSent.TryUpdate(key, now, previous)) return true;
        }
    }

    public void ClearResolved(int tenantId, IReadOnlySet<string> activeCodes)
    {
        foreach (var key in lastSent.Keys.Where(key => key.TenantId == tenantId && !activeCodes.Contains(key.Code)))
            lastSent.TryRemove(key, out _);
    }
}

public sealed class PlatformMetricsAlertWorker(
    IServiceScopeFactory scopeFactory,
    PlatformMetrics platformMetrics,
    PlatformMetricAlertState alertState,
    IPlatformMetricAlertSink alertSink,
    IOptions<PlatformMetricsConfig> configuredOptions,
    TimeProvider timeProvider,
    ILogger<PlatformMetricsAlertWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = configuredOptions.Value;
        if (!options.Enabled) return;
        var interval = TimeSpan.FromMinutes(options.AlertEvaluationIntervalMinutes);
        await EvaluateAsync(options, stoppingToken);
        using var timer = new PeriodicTimer(interval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await EvaluateAsync(options, stoppingToken);
    }

    private async Task EvaluateAsync(PlatformMetricsConfig options, CancellationToken cancellationToken)
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
            logger.LogError(error, "Platform metric tenant discovery failed; evaluation will retry on the next interval");
            return;
        }

        var alertCount = 0;
        foreach (var tenantId in tenantIds)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<DatabaseTenantContext>().SetResolvedTenant(tenantId);
                var operational = await scope.ServiceProvider.GetRequiredService<DeadlineReminderService>()
                    .OperationalMetricsAsync(tenantId, cancellationToken);
                var alerts = platformMetrics.Snapshot(operational).Alerts;
                var activeCodes = alerts.Select(alert => alert.Code).ToHashSet(StringComparer.Ordinal);
                alertState.ClearResolved(tenantId, activeCodes);
                foreach (var alert in alerts)
                {
                    if (!alertState.TryAcquire(
                            tenantId,
                            alert.Code,
                            timeProvider.GetUtcNow().UtcDateTime,
                            TimeSpan.FromMinutes(options.AlertRepeatMinutes)))
                        continue;
                    await alertSink.AlertAsync(alert, cancellationToken);
                    alertCount++;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                logger.LogError(error, "A tenant-scoped platform metric evaluation failed");
            }
        }

        if (alertCount > 0)
            logger.LogWarning("Platform metric evaluation routed {AlertCount} fixed-code alerts", alertCount);
    }
}
