using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Microsoft.Extensions.Options;

namespace Accounts.Api.Services;

public enum DocumentMetricKind
{
    AccountsPackage,
    AgmPack,
    CroFilingPack,
    SignaturePage,
    CharityPack,
    Ixbrl
}

public sealed record PlatformOperationalMetricInputs(
    int ReminderPendingCount,
    int ReminderFailureCount,
    DateTime? LastSuccessfulBackupAtUtc,
    int DurableJobFailureCount);

public sealed record RequestMetricSeries(
    string RouteTemplate,
    string Method,
    string StatusClass,
    long Count,
    double P95Milliseconds);

public sealed record OutcomeMetricSeries(string Kind, string Outcome, long Count, double P95Milliseconds);

public sealed record PlatformMetricAlert(
    string Code,
    string Severity,
    double CurrentValue,
    double Threshold,
    string Unit);

public sealed record PlatformMetricsSnapshot(
    DateTime GeneratedAtUtc,
    int WindowMinutes,
    IReadOnlyList<RequestMetricSeries> Requests,
    IReadOnlyList<OutcomeMetricSeries> Jobs,
    IReadOnlyList<OutcomeMetricSeries> Documents,
    IReadOnlyList<OutcomeMetricSeries> ReminderDeliveries,
    int DatabaseActiveConnections,
    double DatabaseConnectionOpenP95Milliseconds,
    int ReminderPendingCount,
    int ReminderFailureCount,
    double? LastSuccessfulBackupAgeHours,
    IReadOnlyList<PlatformMetricAlert> Alerts,
    IReadOnlyList<string> DimensionNames);

/// <summary>
/// Low-cardinality platform telemetry. Every dimension comes from endpoint metadata, an enum or a
/// fixed status bucket; tenant, company, user, recipient and client identifiers are never tags.
/// </summary>
public sealed class PlatformMetrics : IDisposable
{
    public const string MeterName = "Accounts.Platform";
    public static readonly IReadOnlyList<string> SafeDimensionNames =
    [
        "route_template", "method", "status_class", "job_kind", "document_kind",
        "reminder_kind", "outcome", "pool"
    ];

    private readonly Meter meter = new(MeterName, "1.0.0");
    private readonly Counter<long> requestCounter;
    private readonly Histogram<double> requestDuration;
    private readonly Counter<long> jobCounter;
    private readonly Histogram<double> jobDuration;
    private readonly Counter<long> documentCounter;
    private readonly Histogram<double> documentDuration;
    private readonly Counter<long> reminderCounter;
    private readonly Histogram<double> reminderDuration;
    private readonly Counter<long> databaseOpenCounter;
    private readonly Histogram<double> databaseOpenDuration;
    private readonly UpDownCounter<long> databaseActiveCounter;
    private readonly ConcurrentQueue<RequestObservation> requests = new();
    private readonly ConcurrentQueue<OutcomeObservation> jobs = new();
    private readonly ConcurrentQueue<OutcomeObservation> documents = new();
    private readonly ConcurrentQueue<OutcomeObservation> reminders = new();
    private readonly ConcurrentQueue<TimedValue> databaseOpenDurations = new();
    private readonly TimeProvider timeProvider;
    private readonly PlatformMetricsConfig config;
    private int activeDatabaseConnections;

    public PlatformMetrics(TimeProvider timeProvider, IOptions<PlatformMetricsConfig> options)
    {
        this.timeProvider = timeProvider;
        config = options.Value;
        requestCounter = meter.CreateCounter<long>("accounts.http.server.requests", "{request}");
        requestDuration = meter.CreateHistogram<double>("accounts.http.server.duration", "ms");
        jobCounter = meter.CreateCounter<long>("accounts.jobs.runs", "{run}");
        jobDuration = meter.CreateHistogram<double>("accounts.jobs.duration", "ms");
        documentCounter = meter.CreateCounter<long>("accounts.documents.generated", "{document}");
        documentDuration = meter.CreateHistogram<double>("accounts.documents.duration", "ms");
        reminderCounter = meter.CreateCounter<long>("accounts.reminders.delivery", "{attempt}");
        reminderDuration = meter.CreateHistogram<double>("accounts.reminders.delivery.duration", "ms");
        databaseOpenCounter = meter.CreateCounter<long>("accounts.db.pool.checkouts", "{checkout}");
        databaseOpenDuration = meter.CreateHistogram<double>("accounts.db.pool.checkout.duration", "ms");
        databaseActiveCounter = meter.CreateUpDownCounter<long>("accounts.db.pool.active", "{connection}");
    }

    public void RecordRequest(
        string? routeTemplate,
        string? method,
        int statusCode,
        TimeSpan elapsed)
    {
        if (!config.Enabled) return;
        var route = NormalizeRouteTemplate(routeTemplate);
        var safeMethod = NormalizeMethod(method);
        var statusClass = StatusClass(statusCode);
        var milliseconds = NonNegativeMilliseconds(elapsed);
        var observedAt = UtcNow();
        requests.Enqueue(new RequestObservation(observedAt, route, safeMethod, statusClass, milliseconds));
        TrimToBound(requests);
        var tags = new TagList
        {
            { "route_template", route },
            { "method", safeMethod },
            { "status_class", statusClass }
        };
        requestCounter.Add(1, tags);
        requestDuration.Record(milliseconds, tags);
        Prune(observedAt);
    }

    public void RecordJob(PlatformJobKind kind, PlatformJobStatus status, TimeSpan elapsed)
    {
        if (!config.Enabled) return;
        var outcome = status switch
        {
            PlatformJobStatus.Succeeded => "succeeded",
            PlatformJobStatus.PartiallySucceeded => "partial",
            PlatformJobStatus.Failed => "failed",
            _ => "running"
        };
        RecordOutcome(jobs, jobCounter, jobDuration, "job_kind", kind.ToString(), outcome, elapsed);
    }

    public void RecordDocument(DocumentMetricKind kind, bool succeeded, TimeSpan elapsed)
    {
        if (!config.Enabled) return;
        RecordOutcome(
            documents,
            documentCounter,
            documentDuration,
            "document_kind",
            kind.ToString(),
            succeeded ? "succeeded" : "failed",
            elapsed);
    }

    public void RecordReminderDelivery(DeadlineReminderKind kind, bool succeeded, TimeSpan elapsed)
    {
        if (!config.Enabled) return;
        RecordOutcome(
            reminders,
            reminderCounter,
            reminderDuration,
            "reminder_kind",
            kind.ToString(),
            succeeded ? "delivered" : "failed",
            elapsed);
    }

    public void DatabaseConnectionOpened(TimeSpan elapsed)
    {
        if (!config.Enabled) return;
        var milliseconds = NonNegativeMilliseconds(elapsed);
        var observedAt = UtcNow();
        databaseOpenDurations.Enqueue(new TimedValue(observedAt, milliseconds));
        TrimToBound(databaseOpenDurations);
        Interlocked.Increment(ref activeDatabaseConnections);
        databaseOpenCounter.Add(1, new KeyValuePair<string, object?>("pool", "application"));
        databaseOpenDuration.Record(milliseconds, new KeyValuePair<string, object?>("pool", "application"));
        databaseActiveCounter.Add(1, new KeyValuePair<string, object?>("pool", "application"));
        Prune(observedAt);
    }

    public void DatabaseConnectionClosed()
    {
        if (!config.Enabled) return;
        while (true)
        {
            var current = Volatile.Read(ref activeDatabaseConnections);
            if (current <= 0) return;
            if (Interlocked.CompareExchange(ref activeDatabaseConnections, current - 1, current) == current)
            {
                databaseActiveCounter.Add(-1, new KeyValuePair<string, object?>("pool", "application"));
                return;
            }
        }
    }

    public PlatformMetricsSnapshot Snapshot(PlatformOperationalMetricInputs operational)
    {
        ArgumentNullException.ThrowIfNull(operational);
        var now = UtcNow();
        Prune(now);
        var requestRows = requests.ToArray();
        var jobRows = jobs.ToArray();
        var documentRows = documents.ToArray();
        var reminderRows = reminders.ToArray();
        var requestSeries = requestRows
            .GroupBy(row => new { row.RouteTemplate, row.Method, row.StatusClass })
            .OrderBy(group => group.Key.RouteTemplate, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Method, StringComparer.Ordinal)
            .ThenBy(group => group.Key.StatusClass, StringComparer.Ordinal)
            .Select(group => new RequestMetricSeries(
                group.Key.RouteTemplate,
                group.Key.Method,
                group.Key.StatusClass,
                group.LongCount(),
                Percentile95(group.Select(row => row.Milliseconds))))
            .ToArray();
        var jobsSeries = OutcomeSeries(jobRows);
        var documentSeries = OutcomeSeries(documentRows);
        var reminderSeries = OutcomeSeries(reminderRows);
        var activeConnections = Volatile.Read(ref activeDatabaseConnections);
        var dbOpenP95 = Percentile95(databaseOpenDurations.Select(value => value.Value));
        var backupAge = operational.LastSuccessfulBackupAtUtc is { } backupAt
            ? Math.Max(0, (now - EnsureUtc(backupAt)).TotalHours)
            : (double?)null;
        var alerts = EvaluateAlerts(
            requestRows,
            jobRows,
            documentRows,
            activeConnections,
            operational,
            backupAge);

        return new PlatformMetricsSnapshot(
            now,
            config.WindowMinutes,
            requestSeries,
            jobsSeries,
            documentSeries,
            reminderSeries,
            activeConnections,
            dbOpenP95,
            operational.ReminderPendingCount,
            operational.ReminderFailureCount,
            backupAge,
            alerts,
            SafeDimensionNames);
    }

    public void Dispose() => meter.Dispose();

    internal static string NormalizeRouteTemplate(string? routeTemplate)
    {
        var candidate = routeTemplate?.Trim();
        if (string.IsNullOrEmpty(candidate) || candidate.Length > 300 || !candidate.StartsWith('/'))
            return "/unmatched";

        var segments = candidate.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return "/" + string.Join('/', segments.Select(segment =>
        {
            if (segment.StartsWith('{') && segment.EndsWith('}')) return "{parameter}";
            if (segment.Length > 64 || segment.Contains('@') || Guid.TryParse(segment, out _)) return "{redacted}";
            if (segment.All(char.IsDigit)) return "{id}";
            return segment.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.')
                ? segment.ToLowerInvariant()
                : "{redacted}";
        }));
    }

    private void RecordOutcome(
        ConcurrentQueue<OutcomeObservation> target,
        Counter<long> counter,
        Histogram<double> duration,
        string kindDimension,
        string kind,
        string outcome,
        TimeSpan elapsed)
    {
        var observedAt = UtcNow();
        var milliseconds = NonNegativeMilliseconds(elapsed);
        target.Enqueue(new OutcomeObservation(observedAt, kind, outcome, milliseconds));
        TrimToBound(target);
        var tags = new TagList { { kindDimension, kind }, { "outcome", outcome } };
        counter.Add(1, tags);
        duration.Record(milliseconds, tags);
        Prune(observedAt);
    }

    private IReadOnlyList<PlatformMetricAlert> EvaluateAlerts(
        IReadOnlyCollection<RequestObservation> requestRows,
        IReadOnlyCollection<OutcomeObservation> jobRows,
        IReadOnlyCollection<OutcomeObservation> documentRows,
        int activeConnections,
        PlatformOperationalMetricInputs operational,
        double? backupAge)
    {
        var alerts = new List<PlatformMetricAlert>();
        var requestP95 = Percentile95(requestRows.Select(row => row.Milliseconds));
        if (requestP95 > config.RequestLatencyP95Milliseconds)
            alerts.Add(Alert("request-latency-high", requestP95, config.RequestLatencyP95Milliseconds, "ms"));
        var errorRate = requestRows.Count == 0
            ? 0
            : requestRows.Count(row => row.StatusClass == "5xx") * 100d / requestRows.Count;
        if (errorRate >= config.RequestErrorRatePercent)
            alerts.Add(Alert("request-error-rate-high", errorRate, config.RequestErrorRatePercent, "percent"));
        var inMemoryJobFailures = jobRows.Count(row => row.Outcome == "failed");
        var jobFailures = Math.Max(inMemoryJobFailures, operational.DurableJobFailureCount);
        if (jobFailures >= config.JobFailureThreshold)
            alerts.Add(Alert("scheduled-job-failures", jobFailures, config.JobFailureThreshold, "runs"));
        if (activeConnections >= config.DatabaseActiveConnectionThreshold)
            alerts.Add(Alert("database-pool-pressure", activeConnections, config.DatabaseActiveConnectionThreshold, "connections"));
        var documentP95 = Percentile95(documentRows.Select(row => row.Milliseconds));
        if (documentP95 > config.DocumentGenerationP95Milliseconds)
            alerts.Add(Alert("document-generation-slow", documentP95, config.DocumentGenerationP95Milliseconds, "ms"));
        if (backupAge is null || backupAge >= config.BackupMaximumAgeHours)
            alerts.Add(Alert(
                "backup-evidence-stale",
                backupAge ?? config.BackupMaximumAgeHours + 1d,
                config.BackupMaximumAgeHours,
                "hours"));
        if (operational.ReminderFailureCount >= config.ReminderFailureBacklogThreshold)
            alerts.Add(Alert("reminder-delivery-failures", operational.ReminderFailureCount, config.ReminderFailureBacklogThreshold, "reminders"));
        if (operational.ReminderPendingCount >= config.ReminderPendingBacklogThreshold)
            alerts.Add(Alert("reminder-backlog-high", operational.ReminderPendingCount, config.ReminderPendingBacklogThreshold, "reminders"));
        return alerts;
    }

    private static PlatformMetricAlert Alert(string code, double current, double threshold, string unit) =>
        new(code, "critical", current, threshold, unit);

    private static OutcomeMetricSeries[] OutcomeSeries(IEnumerable<OutcomeObservation> rows) => rows
        .GroupBy(row => new { row.Kind, row.Outcome })
        .OrderBy(group => group.Key.Kind, StringComparer.Ordinal)
        .ThenBy(group => group.Key.Outcome, StringComparer.Ordinal)
        .Select(group => new OutcomeMetricSeries(
            group.Key.Kind,
            group.Key.Outcome,
            group.LongCount(),
            Percentile95(group.Select(row => row.Milliseconds))))
        .ToArray();

    private void Prune(DateTime now)
    {
        var cutoff = now.AddMinutes(-config.WindowMinutes);
        Prune(requests, cutoff, item => item.ObservedAtUtc);
        Prune(jobs, cutoff, item => item.ObservedAtUtc);
        Prune(documents, cutoff, item => item.ObservedAtUtc);
        Prune(reminders, cutoff, item => item.ObservedAtUtc);
        Prune(databaseOpenDurations, cutoff, item => item.ObservedAtUtc);
    }

    private static void Prune<T>(ConcurrentQueue<T> queue, DateTime cutoff, Func<T, DateTime> timestamp)
    {
        while (queue.TryPeek(out var item) && timestamp(item) < cutoff)
            queue.TryDequeue(out _);
    }

    private void TrimToBound<T>(ConcurrentQueue<T> queue)
    {
        while (queue.Count > config.MaximumObservationsPerSeries)
            queue.TryDequeue(out _);
    }

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
    private static DateTime EnsureUtc(DateTime value) => value.Kind == DateTimeKind.Utc
        ? value
        : value.ToUniversalTime();
    private static double NonNegativeMilliseconds(TimeSpan elapsed) => Math.Max(0, elapsed.TotalMilliseconds);
    private static string NormalizeMethod(string? method) => method?.Trim().ToUpperInvariant() switch
    {
        "GET" => "GET",
        "POST" => "POST",
        "PUT" => "PUT",
        "PATCH" => "PATCH",
        "DELETE" => "DELETE",
        "HEAD" => "HEAD",
        "OPTIONS" => "OPTIONS",
        _ => "OTHER"
    };
    private static string StatusClass(int statusCode) => statusCode switch
    {
        >= 100 and <= 599 => $"{statusCode / 100}xx",
        _ => "unknown"
    };
    private static double Percentile95(IEnumerable<double> source)
    {
        var values = source.Order().ToArray();
        if (values.Length == 0) return 0;
        var index = (int)Math.Ceiling(values.Length * 0.95d) - 1;
        return values[Math.Clamp(index, 0, values.Length - 1)];
    }

    private sealed record RequestObservation(
        DateTime ObservedAtUtc,
        string RouteTemplate,
        string Method,
        string StatusClass,
        double Milliseconds);
    private sealed record OutcomeObservation(DateTime ObservedAtUtc, string Kind, string Outcome, double Milliseconds);
    private sealed record TimedValue(DateTime ObservedAtUtc, double Value);
}
