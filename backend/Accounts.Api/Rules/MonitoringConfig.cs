namespace Accounts.Api.Rules;

public class MonitoringConfig
{
    public bool RequireInProduction { get; set; } = true;
    public string ErrorTrackingProvider { get; set; } = "Sentry-compatible";
    public string ErrorTrackingDsn { get; set; } = "";
    public bool StructuredJsonConsole { get; set; }
    public bool IncludeCorrelationId { get; set; } = true;
    public double TracesSampleRate { get; set; }
    public bool ErrorSmokeEnabled { get; set; }
}
