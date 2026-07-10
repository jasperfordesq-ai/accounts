namespace Accounts.Api.Rules;

public sealed class DeadlineDeliveryConfig
{
    public bool RequireInProduction { get; set; } = true;
    public bool Enabled { get; set; }
    public string Provider { get; set; } = "Webhook";
    public string ProviderEndpoint { get; set; } = "";
    public string ProviderToken { get; set; } = "";
    public int DueSoonDays { get; set; } = 30;
    public int SchedulerIntervalMinutes { get; set; } = 15;
    public int BatchSize { get; set; } = 50;
    public int RetryBaseMinutes { get; set; } = 5;
    public int RetryMaximumMinutes { get; set; } = 1440;
    public int DeliveryLeaseMinutes { get; set; } = 10;
    public int MaximumAutomaticAttempts { get; set; } = 10;
    public int OperatorAlertAfterAttempts { get; set; } = 1;
}

public sealed class PlatformMetricsConfig
{
    public bool RequireInProduction { get; set; } = true;
    public bool Enabled { get; set; } = true;
    public bool RestrictedSnapshotEnabled { get; set; } = true;
    public int WindowMinutes { get; set; } = 15;
    public int RequestLatencyP95Milliseconds { get; set; } = 1000;
    public double RequestErrorRatePercent { get; set; } = 2;
    public int JobFailureThreshold { get; set; } = 1;
    public int DatabaseActiveConnectionThreshold { get; set; } = 80;
    public int DocumentGenerationP95Milliseconds { get; set; } = 10000;
    public int BackupMaximumAgeHours { get; set; } = 26;
    public int ReminderFailureBacklogThreshold { get; set; } = 1;
    public int ReminderPendingBacklogThreshold { get; set; } = 100;
    public int AlertEvaluationIntervalMinutes { get; set; } = 5;
    public int AlertRepeatMinutes { get; set; } = 30;
    public int MaximumObservationsPerSeries { get; set; } = 100000;
}

public static class PlatformOperationsConfigurationValidator
{
    public static IReadOnlyList<string> Validate(
        DeadlineDeliveryConfig delivery,
        PlatformMetricsConfig metrics)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(metrics);
        var failures = new List<string>();

        if (delivery.RequireInProduction)
        {
            if (!delivery.Enabled)
                failures.Add("DeadlineDelivery:Enabled must be true outside development so filing reminders do not depend on dashboard visits.");
            if (!string.Equals(delivery.Provider, "Webhook", StringComparison.Ordinal))
                failures.Add("DeadlineDelivery:Provider must identify the supported Webhook provider outside development.");
            if (!Uri.TryCreate(delivery.ProviderEndpoint, UriKind.Absolute, out var providerUri)
                || providerUri.Scheme != Uri.UriSchemeHttps)
                failures.Add("DeadlineDelivery:ProviderEndpoint must be an absolute HTTPS endpoint outside development.");
            if (delivery.ProviderToken.Trim().Length < 32)
                failures.Add("DeadlineDelivery:ProviderToken must be a file-backed secret of at least 32 characters outside development.");
        }

        if (delivery.DueSoonDays is < 1 or > 90)
            failures.Add("DeadlineDelivery:DueSoonDays must be between 1 and 90.");
        if (delivery.SchedulerIntervalMinutes is < 1 or > 1440)
            failures.Add("DeadlineDelivery:SchedulerIntervalMinutes must be between 1 and 1440.");
        if (delivery.BatchSize is < 1 or > 500)
            failures.Add("DeadlineDelivery:BatchSize must be between 1 and 500.");
        if (delivery.RetryBaseMinutes is < 1 or > 60)
            failures.Add("DeadlineDelivery:RetryBaseMinutes must be between 1 and 60.");
        if (delivery.RetryMaximumMinutes < delivery.RetryBaseMinutes
            || delivery.RetryMaximumMinutes > 10080)
            failures.Add("DeadlineDelivery:RetryMaximumMinutes must be at least the base delay and no more than seven days.");
        if (delivery.DeliveryLeaseMinutes is < 1 or > 60)
            failures.Add("DeadlineDelivery:DeliveryLeaseMinutes must be between 1 and 60.");
        if (delivery.MaximumAutomaticAttempts is < 1 or > 100)
            failures.Add("DeadlineDelivery:MaximumAutomaticAttempts must be between 1 and 100.");
        if (delivery.OperatorAlertAfterAttempts is < 1 or > 10)
            failures.Add("DeadlineDelivery:OperatorAlertAfterAttempts must be between 1 and 10.");

        if (metrics.RequireInProduction && !metrics.Enabled)
            failures.Add("PlatformMetrics:Enabled must be true outside development.");
        if (metrics.RequireInProduction && !metrics.RestrictedSnapshotEnabled)
            failures.Add("PlatformMetrics:RestrictedSnapshotEnabled must be true outside development.");
        if (metrics.WindowMinutes is < 1 or > 1440)
            failures.Add("PlatformMetrics:WindowMinutes must be between 1 and 1440.");
        if (metrics.RequestLatencyP95Milliseconds is < 50 or > 60000)
            failures.Add("PlatformMetrics:RequestLatencyP95Milliseconds must be between 50 and 60000.");
        if (metrics.RequestErrorRatePercent is < 0.1 or > 100)
            failures.Add("PlatformMetrics:RequestErrorRatePercent must be between 0.1 and 100.");
        if (metrics.JobFailureThreshold is < 1 or > 100)
            failures.Add("PlatformMetrics:JobFailureThreshold must be between 1 and 100.");
        if (metrics.DatabaseActiveConnectionThreshold is < 1 or > 10000)
            failures.Add("PlatformMetrics:DatabaseActiveConnectionThreshold must be between 1 and 10000.");
        if (metrics.DocumentGenerationP95Milliseconds is < 100 or > 300000)
            failures.Add("PlatformMetrics:DocumentGenerationP95Milliseconds must be between 100 and 300000.");
        if (metrics.BackupMaximumAgeHours is < 1 or > 168)
            failures.Add("PlatformMetrics:BackupMaximumAgeHours must be between 1 and 168.");
        if (metrics.ReminderFailureBacklogThreshold is < 1 or > 10000)
            failures.Add("PlatformMetrics:ReminderFailureBacklogThreshold must be between 1 and 10000.");
        if (metrics.ReminderPendingBacklogThreshold is < 1 or > 100000)
            failures.Add("PlatformMetrics:ReminderPendingBacklogThreshold must be between 1 and 100000.");
        if (metrics.AlertEvaluationIntervalMinutes is < 1 or > 60)
            failures.Add("PlatformMetrics:AlertEvaluationIntervalMinutes must be between 1 and 60.");
        if (metrics.AlertRepeatMinutes < metrics.AlertEvaluationIntervalMinutes
            || metrics.AlertRepeatMinutes > 1440)
            failures.Add("PlatformMetrics:AlertRepeatMinutes must be at least the evaluation interval and no more than 1440 minutes.");
        if (metrics.MaximumObservationsPerSeries is < 1000 or > 1000000)
            failures.Add("PlatformMetrics:MaximumObservationsPerSeries must be between 1000 and 1000000.");

        return failures;
    }
}
