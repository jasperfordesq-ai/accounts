using System.Text;
using System.Text.Json;
using Accounts.Api.Services;
using Accounts.Api.Rules;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Sentry;
using Xunit;

namespace Accounts.Tests;

public sealed class MonitoringPrivacyTests
{
    [Fact]
    public void SanitizerRemovesClientIdentifiersSecretsAndFreeFormExceptionMessages()
    {
        Exception exception;
        try
        {
            throw new InvalidOperationException(
                "client@example.ie password=NeverSendThis account Connacht Trading Limited");
        }
        catch (Exception caught)
        {
            exception = caught;
        }

        var safe = MonitoringEventSanitizer.Sanitize(
            exception,
            new ErrorReportContext(
                "GET client@example.ie",
                "/api/companies/742/client@example.ie?token=NeverSendThis",
                "client@example.ie:NeverSendThis"));
        var serialized = JsonSerializer.Serialize(safe);

        Assert.Equal("UNKNOWN", safe.Method);
        Assert.Equal("/api/companies/{id}/{redacted}", safe.Path);
        Assert.StartsWith("corr-", safe.CorrelationId);
        Assert.Equal("unexpected-exception", safe.EventCode);
        Assert.Matches("^[0-9a-f]{64}$", safe.StackFingerprint);
        Assert.DoesNotContain("client@example.ie", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NeverSendThis", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("Connacht Trading", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizerRetainsSafeCorrelationAndRouteShapeForOperations()
    {
        var safe = MonitoringEventSanitizer.Sanitize(
            new MonitoringSmokeException(),
            new ErrorReportContext("post", "/api/companies/42/periods/73", "corr-smoke_2026.07"));

        Assert.Equal("POST", safe.Method);
        Assert.Equal("/api/companies/{id}/periods/{id}", safe.Path);
        Assert.Equal("corr-smoke_2026.07", safe.CorrelationId);
        Assert.Equal("unexpected-exception", safe.EventCode);
        Assert.EndsWith("MonitoringSmokeException", safe.ExceptionType, StringComparison.Ordinal);
    }

    [Fact]
    public void ClientCorrelationAliasAndRouteAllowlistRejectSafeLookingPersonalData()
    {
        var correlation = MonitoringEventSanitizer.SafeClientCorrelationId(
            "JasperFord-ClientSecret\r\nforged-entry",
            "server-correlation");
        var route = MonitoringEventSanitizer.SafeClientPath(
            "/companies/ConnachtTrading/periods/ClientReference2026");

        Assert.Matches("^client-corr-[0-9a-f]{24}$", correlation);
        Assert.DoesNotContain("Jasper", correlation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain('\r', correlation);
        Assert.DoesNotContain('\n', correlation);
        Assert.Equal("/companies/{redacted}/periods/{redacted}", route);
    }

    [Fact]
    public void ServerRouteUsesEndpointTemplateInsteadOfRequestValues()
    {
        var endpoint = new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse("/api/companies/{companyId:int}/periods/{periodId:int}/adjustments/generate"),
            order: 0,
            EndpointMetadataCollection.Empty,
            "test route");

        Assert.Equal(
            "/api/companies/{id}/periods/{id}/adjustments/generate",
            MonitoringEventSanitizer.SafeServerRoute(endpoint));
        Assert.Equal("/{unmatched}", MonitoringEventSanitizer.SafeServerRoute(null));
    }

    [Fact]
    public void FinalLogFieldEncodingRemovesRecordSeparators()
    {
        Assert.Equal("safe__forged", MonitoringEventSanitizer.SafeLogField("safe\r\nforged"));
    }

    [Fact]
    public void BeforeSendScrubberDropsRequestUserServerAndUnapprovedTags()
    {
        var sentryEvent = new SentryEvent(new RedactedMonitoringException())
        {
            Request = new SentryRequest
            {
                Url = "https://accounts.example.ie/api/companies/42?client=client@example.ie",
                Data = "NeverSendThis"
            },
            User = new SentryUser { Email = "client@example.ie", Username = "Client Name" },
            ServerName = "client-server-name",
            TransactionName = "/api/companies/42"
        };
        sentryEvent.SetTag("correlation_id", "corr-safe");
        sentryEvent.SetTag("event_code", "render-exception");
        sentryEvent.SetTag("client_name", "Connacht Trading Limited");

        var scrubbed = MonitoringEventSanitizer.ScrubSentryEvent(sentryEvent);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            scrubbed.WriteTo(writer, null);
        var json = Encoding.UTF8.GetString(stream.ToArray());

        Assert.Equal("/api/companies/{id}", scrubbed.TransactionName);
        Assert.Equal("corr-safe", scrubbed.Tags["correlation_id"]);
        Assert.Equal("render-exception", scrubbed.Tags["event_code"]);
        Assert.DoesNotContain("client_name", scrubbed.Tags.Keys);
        Assert.DoesNotContain("request", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("client@example.ie", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NeverSendThis", json, StringComparison.Ordinal);
        Assert.DoesNotContain("client-server-name", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionMonitoringRequiresRetentionOwnershipAlertingAndEscalationControls()
    {
        var invalid = new MonitoringConfig
        {
            ErrorTrackingProvider = "Sentry-compatible",
            ErrorTrackingDsn = "https://public@sentry.example.ie/1",
            StructuredJsonConsole = true,
            IncludeCorrelationId = true,
            StructuredLogRetentionDays = 7,
            ErrorEventRetentionDays = 5000,
            AlertAcknowledgementMinutes = 45,
            EscalationMinutes = 30,
            OnCallOwner = "",
            AlertRoute = "",
            IncidentRunbookPath = "README.md"
        };

        var failures = ProductionSafetyService.ValidateMonitoringConfiguration(invalid);

        Assert.Contains(failures, failure => failure.Contains("StructuredLogRetentionDays", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("ErrorEventRetentionDays", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("EscalationMinutes", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("OnCallOwner", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("AlertRoute", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("IncidentRunbookPath", StringComparison.Ordinal));
    }

    [Fact]
    public void CompleteProductionMonitoringOperationsConfigurationPasses()
    {
        var failures = ProductionSafetyService.ValidateMonitoringConfiguration(new MonitoringConfig
        {
            ErrorTrackingProvider = "Sentry-compatible",
            ErrorTrackingDsn = "https://public@sentry.example.ie/1",
            StructuredJsonConsole = true,
            IncludeCorrelationId = true,
            StructuredLogRetentionDays = 90,
            ErrorEventRetentionDays = 90,
            AlertAcknowledgementMinutes = 15,
            EscalationMinutes = 30,
            OnCallOwner = "Operations Owner",
            AlertRoute = "production-on-call",
            IncidentRunbookPath = "Docs/operations/monitoring-incident-response.md"
        });

        Assert.Empty(failures);
    }
}
