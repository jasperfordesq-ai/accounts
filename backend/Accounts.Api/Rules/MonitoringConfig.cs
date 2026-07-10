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
    public int StructuredLogRetentionDays { get; set; } = 90;
    public int ErrorEventRetentionDays { get; set; } = 90;
    public int AlertAcknowledgementMinutes { get; set; } = 15;
    public int EscalationMinutes { get; set; } = 30;
    public string OnCallOwner { get; set; } = "";
    public string AlertRoute { get; set; } = "";
    public string IncidentRunbookPath { get; set; } = "Docs/operations/monitoring-incident-response.md";
}
