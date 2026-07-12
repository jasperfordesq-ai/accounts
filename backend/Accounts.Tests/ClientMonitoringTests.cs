using System.Text.Json;
using Accounts.Api.Endpoints;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Accounts.Tests;

public sealed class ClientMonitoringTests
{
    [Fact]
    public void ClientEventSanitizesRouteCorrelationAndPayloadBeforeProviderCapture()
    {
        var context = AuthenticatedContext("Client");
        context.TraceIdentifier = "client@example.ie:NeverSendThis";
        var reporter = new RecordingErrorReporter();

        var result = SystemEndpoints.EmitClientMonitoringEvent(
            context,
            new ClientMonitoringEventInput(
                "render-exception",
                "/companies/742/periods/73/client@example.ie?token=NeverSendThis",
                "client@example.ie:NeverSendThis"),
            reporter,
            NullLogger.Instance);

        var status = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status202Accepted, status.StatusCode);
        var value = Assert.IsAssignableFrom<IValueHttpResult>(result).Value;
        var json = JsonSerializer.Serialize(value);

        Assert.IsType<ClientMonitoringException>(reporter.Exception);
        Assert.NotNull(reporter.Context);
        Assert.Equal("CLIENT", reporter.Context!.Method);
        Assert.Equal("/companies/{id}/periods/{id}/{redacted}", reporter.Context.Path);
        Assert.Equal("render-exception", reporter.Context.EventCode);
        Assert.StartsWith("client-corr-", reporter.Context.CorrelationId);
        Assert.DoesNotContain("client@example.ie", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NeverSendThis", json, StringComparison.Ordinal);
        Assert.DoesNotContain("client@example.ie", JsonSerializer.Serialize(reporter.Context), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NeverSendThis", reporter.Exception!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ClientEventAliasesSafeLookingCorrelationTextBeforeLoggingOrProviderCapture()
    {
        var reporter = new RecordingErrorReporter();
        var result = SystemEndpoints.EmitClientMonitoringEvent(
            AuthenticatedContext("Accountant"),
            new ClientMonitoringEventInput(
                "api-server-rejection",
                "/companies/ConnachtTrading/periods/ClientReference2026",
                "JasperFord-ClientSecret"),
            reporter,
            NullLogger.Instance);

        Assert.Equal(
            StatusCodes.Status202Accepted,
            Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.NotNull(reporter.Context);
        var serialized = JsonSerializer.Serialize(reporter.Context);
        Assert.Equal("/companies/{redacted}/periods/{redacted}", reporter.Context!.Path);
        Assert.Matches("^client-corr-[0-9a-f]{24}$", reporter.Context.CorrelationId);
        Assert.DoesNotContain("JasperFord", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ClientSecret", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConnachtTrading", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ClientReference2026", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClientEventRejectsUnknownCodeAndUnauthenticatedCallsWithoutProviderCapture()
    {
        var reporter = new RecordingErrorReporter();
        var unauthenticated = new DefaultHttpContext();
        var unauthenticatedResult = SystemEndpoints.EmitClientMonitoringEvent(
            unauthenticated,
            new ClientMonitoringEventInput("render-exception", "/companies/1", null),
            reporter,
            NullLogger.Instance);
        Assert.Equal(
            StatusCodes.Status401Unauthorized,
            Assert.IsAssignableFrom<IStatusCodeHttpResult>(unauthenticatedResult).StatusCode);

        var invalidResult = SystemEndpoints.EmitClientMonitoringEvent(
            AuthenticatedContext("Accountant"),
            new ClientMonitoringEventInput("free-form-client@example.ie", "/companies/1", null),
            reporter,
            NullLogger.Instance);
        Assert.Equal(
            StatusCodes.Status400BadRequest,
            Assert.IsAssignableFrom<IStatusCodeHttpResult>(invalidResult).StatusCode);
        Assert.Null(reporter.Exception);
    }

    [Fact]
    public void ClientEventAllowlistRedactsEncodedAndFreeFormRouteSegments()
    {
        var reporter = new RecordingErrorReporter();
        var result = SystemEndpoints.EmitClientMonitoringEvent(
            AuthenticatedContext("Accountant"),
            new ClientMonitoringEventInput(
                "api-server-rejection",
                "/companies/client%40example.ie/periods/ACME-2026/statements?token=NeverSendThis",
                "corr-safe"),
            reporter,
            NullLogger.Instance);

        Assert.Equal(
            StatusCodes.Status202Accepted,
            Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.NotNull(reporter.Context);
        Assert.Equal("/companies/{redacted}/periods/{redacted}/statements", reporter.Context!.Path);
        Assert.DoesNotContain("example", JsonSerializer.Serialize(reporter.Context), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ACME", JsonSerializer.Serialize(reporter.Context), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NeverSendThis", JsonSerializer.Serialize(reporter.Context), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Owner")]
    [InlineData("Accountant")]
    [InlineData("Reviewer")]
    [InlineData("Client")]
    public void AuthenticatedRolesCanReportSanitizedClientEvents(string role)
    {
        var user = new AuthenticatedUser(7, 1, "Firm", "person@example.ie", "Person", role);
        var decision = RoleAuthorizationService.Authorize(
            user,
            new PathString("/api/system/monitoring/client-event"),
            HttpMethods.Post);

        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public void PasswordChangeLockStillAllowsSanitizedClientMonitoring()
    {
        var user = new AuthenticatedUser(
            7,
            1,
            "Firm",
            "person@example.ie",
            "Person",
            "Client",
            MustChangePassword: true);

        Assert.True(RoleAuthorizationService.Authorize(
            user,
            new PathString("/api/system/monitoring/client-event"),
            HttpMethods.Post).IsAllowed);
        Assert.False(RoleAuthorizationService.Authorize(
            user,
            new PathString("/api/companies/1/notes"),
            HttpMethods.Post).IsAllowed);
    }

    private static DefaultHttpContext AuthenticatedContext(string role)
    {
        var context = new DefaultHttpContext();
        context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            7,
            1,
            "Firm",
            "person@example.ie",
            "Person",
            role);
        return context;
    }

    private sealed class RecordingErrorReporter : IErrorReporter
    {
        public Exception? Exception { get; private set; }
        public ErrorReportContext? Context { get; private set; }

        public string CaptureUnexpectedException(Exception exception, ErrorReportContext context)
        {
            Exception = exception;
            Context = context;
            return "event-safe-123";
        }
    }
}
