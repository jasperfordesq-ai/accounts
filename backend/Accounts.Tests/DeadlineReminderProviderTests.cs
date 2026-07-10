using System.Net;
using System.Text.Json;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace Accounts.Tests;

public sealed class DeadlineReminderProviderTests
{
    [Fact]
    public async Task WebhookPayload_IsFixedShapePseudonymousAndIdempotent()
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Headers = { { "X-Delivery-Reference", "provider:accepted-42" } }
        });
        var provider = Provider(handler);
        var command = Command();

        var result = await provider.DeliverAsync(command);

        Assert.True(result.Delivered);
        Assert.Equal("provider:accepted-42", result.ProviderDeliveryReference);
        Assert.Equal(command.DeduplicationKeySha256, handler.IdempotencyKey);
        using var payload = JsonDocument.Parse(handler.Body!);
        Assert.Equal("deadline-reminder-due-soon", payload.RootElement.GetProperty("eventCode").GetString());
        Assert.StartsWith("scope-", payload.RootElement.GetProperty("scopeReference").GetString());
        Assert.Equal("/operations/deadline-risk", payload.RootElement.GetProperty("workQueuePath").GetString());
        var raw = handler.Body!;
        Assert.DoesNotContain("company", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("client", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("email", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tenantId", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, DeadlineReminderFailureCodes.ProviderRejected)]
    [InlineData(HttpStatusCode.TooManyRequests, DeadlineReminderFailureCodes.ProviderUnavailable)]
    [InlineData(HttpStatusCode.ServiceUnavailable, DeadlineReminderFailureCodes.ProviderUnavailable)]
    public async Task ProviderFailures_ReturnFixedRetryableCodes(HttpStatusCode status, string expected)
    {
        var provider = Provider(new CapturingHandler(new HttpResponseMessage(status)
        {
            Content = new StringContent("customer@example.invalid confidential raw failure")
        }));

        var result = await provider.DeliverAsync(Command());

        Assert.False(result.Delivered);
        Assert.Equal(expected, result.FailureCode);
    }

    [Fact]
    public async Task InvalidProviderConfiguration_FailsClosedWithoutNetworkCall()
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var provider = new WebhookDeadlineReminderProvider(
            new HttpClient(handler),
            Options.Create(new DeadlineDeliveryConfig
            {
                ProviderEndpoint = "http://insecure.invalid",
                ProviderToken = "short"
            }));

        var result = await provider.DeliverAsync(Command());

        Assert.False(result.Delivered);
        Assert.Equal(DeadlineReminderFailureCodes.InvalidConfiguration, result.FailureCode);
        Assert.Equal(0, handler.CallCount);
    }

    private static WebhookDeadlineReminderProvider Provider(HttpMessageHandler handler) => new(
        new HttpClient(handler),
        Options.Create(new DeadlineDeliveryConfig
        {
            ProviderEndpoint = "https://notifications.example.invalid/accounts",
            ProviderToken = new string('s', 48)
        }));

    private static DeadlineReminderDeliveryCommand Command() => new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        7,
        DeadlineReminderKind.DueSoon,
        DeadlineType.CRO,
        new DateOnly(2026, 7, 24),
        new string('a', 64));

    private sealed class CapturingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string? Body { get; private set; }
        public string? IdempotencyKey { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            IdempotencyKey = request.Headers.GetValues("Idempotency-Key").Single();
            return response;
        }
    }
}
