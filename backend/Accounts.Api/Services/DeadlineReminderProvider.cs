using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Microsoft.Extensions.Options;

namespace Accounts.Api.Services;

public static class DeadlineReminderEventCodes
{
    public const string DeliveryFailed = "deadline-reminder-delivery-failed";
    public const string DueSoon = "deadline-reminder-due-soon";
    public const string Overdue = "deadline-reminder-overdue";
    public const string Corrected = "deadline-reminder-corrected";
}

public static class DeadlineReminderFailureCodes
{
    public const string ProviderRejected = "provider-rejected";
    public const string ProviderUnavailable = "provider-unavailable";
    public const string ProviderTimeout = "provider-timeout";
    public const string ProviderException = "provider-exception";
    public const string InvalidConfiguration = "invalid-provider-configuration";

    public static string Normalize(string? candidate) => candidate switch
    {
        ProviderRejected => ProviderRejected,
        ProviderUnavailable => ProviderUnavailable,
        ProviderTimeout => ProviderTimeout,
        InvalidConfiguration => InvalidConfiguration,
        _ => ProviderException
    };
}

public sealed record DeadlineReminderDeliveryCommand(
    Guid OutboxId,
    int TenantId,
    DeadlineReminderKind ReminderKind,
    DeadlineType DeadlineType,
    DateOnly DueDate,
    string DeduplicationKeySha256);

public sealed record DeadlineReminderDeliveryResult(
    bool Delivered,
    string? ProviderDeliveryReference,
    string? FailureCode)
{
    public static DeadlineReminderDeliveryResult Success(string? reference) => new(true, reference, null);
    public static DeadlineReminderDeliveryResult Failure(string code) =>
        new(false, null, DeadlineReminderFailureCodes.Normalize(code));
}

public interface IDeadlineReminderProvider
{
    Task<DeadlineReminderDeliveryResult> DeliverAsync(
        DeadlineReminderDeliveryCommand command,
        CancellationToken cancellationToken = default);
}

public sealed record DeadlineReminderWebhookEnvelope(
    string EventCode,
    string ScopeReference,
    string ReminderKind,
    string DeadlineType,
    DateOnly DueDate,
    string WorkQueuePath,
    string DeduplicationKeySha256);

/// <summary>
/// Fixed-schema provider integration. The webhook receives no company/client name, recipient
/// address, tax reference, CRO number, free-form note or raw exception detail.
/// </summary>
public sealed partial class WebhookDeadlineReminderProvider(
    HttpClient httpClient,
    IOptions<DeadlineDeliveryConfig> options) : IDeadlineReminderProvider
{
    private readonly DeadlineDeliveryConfig config = options.Value;

    public async Task<DeadlineReminderDeliveryResult> DeliverAsync(
        DeadlineReminderDeliveryCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(config.ProviderEndpoint, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttps
            || config.ProviderToken.Trim().Length < 32)
        {
            return DeadlineReminderDeliveryResult.Failure(DeadlineReminderFailureCodes.InvalidConfiguration);
        }

        var envelope = new DeadlineReminderWebhookEnvelope(
            EventCode(command.ReminderKind),
            PseudonymousTenantScope(command.TenantId, config.ProviderToken),
            command.ReminderKind.ToString(),
            command.DeadlineType.ToString(),
            command.DueDate,
            "/operations/deadline-risk",
            command.DeduplicationKeySha256);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(envelope)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ProviderToken);
        request.Headers.Add("Idempotency-Key", command.DeduplicationKeySha256);

        try
        {
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var rawReference = response.Headers.TryGetValues("X-Delivery-Reference", out var values)
                    ? values.FirstOrDefault()
                    : null;
                return DeadlineReminderDeliveryResult.Success(SafeProviderReference(rawReference));
            }

            var status = (int)response.StatusCode;
            return DeadlineReminderDeliveryResult.Failure(status is 408 or 425 or 429 or >= 500
                ? DeadlineReminderFailureCodes.ProviderUnavailable
                : DeadlineReminderFailureCodes.ProviderRejected);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return DeadlineReminderDeliveryResult.Failure(DeadlineReminderFailureCodes.ProviderTimeout);
        }
        catch (HttpRequestException)
        {
            return DeadlineReminderDeliveryResult.Failure(DeadlineReminderFailureCodes.ProviderUnavailable);
        }
        catch
        {
            return DeadlineReminderDeliveryResult.Failure(DeadlineReminderFailureCodes.ProviderException);
        }
    }

    private static string EventCode(DeadlineReminderKind kind) => kind switch
    {
        DeadlineReminderKind.DueSoon => DeadlineReminderEventCodes.DueSoon,
        DeadlineReminderKind.Overdue => DeadlineReminderEventCodes.Overdue,
        DeadlineReminderKind.Corrected => DeadlineReminderEventCodes.Corrected,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    internal static string PseudonymousTenantScope(int tenantId, string token)
    {
        var key = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        var payload = Encoding.UTF8.GetBytes($"deadline-scope-v1:{tenantId}");
        return "scope-" + Convert.ToHexStringLower(HMACSHA256.HashData(key, payload))[..24];
    }

    private static string? SafeProviderReference(string? rawReference)
    {
        var candidate = rawReference?.Trim();
        if (string.IsNullOrEmpty(candidate))
            return null;
        if (candidate.Length <= 128 && ProviderReferencePattern().IsMatch(candidate))
            return candidate;
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(candidate)));
    }

    [GeneratedRegex("^[A-Za-z0-9._:-]+$")]
    private static partial Regex ProviderReferencePattern();
}

public sealed record DeadlineReminderFailureAlert(
    Guid OutboxId,
    int AttemptCount,
    string FailureCode);

public interface IOperatorAlertSink
{
    Task AlertDeadlineReminderFailureAsync(
        DeadlineReminderFailureAlert alert,
        CancellationToken cancellationToken = default);
}

public sealed class MonitoringOperatorAlertSink(IErrorReporter errorReporter) : IOperatorAlertSink
{
    public Task AlertDeadlineReminderFailureAsync(
        DeadlineReminderFailureAlert alert,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = errorReporter.CaptureUnexpectedException(
            new DeadlineReminderDeliveryException(),
            new ErrorReportContext(
                "JOB",
                "/internal/jobs/deadline-reminders",
                $"reminder-{alert.OutboxId:N}",
                DeadlineReminderEventCodes.DeliveryFailed));
        return Task.CompletedTask;
    }
}

public sealed class DeadlineReminderDeliveryException()
    : Exception("Deadline reminder provider delivery failed; details are retained as fixed operational codes.");
