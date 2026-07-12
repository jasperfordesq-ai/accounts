using Sentry;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Accounts.Api.Services;

public sealed record ErrorReportContext(
    string Method,
    string Path,
    string CorrelationId,
    string EventCode = "unexpected-exception");

public interface IErrorReporter
{
    string CaptureUnexpectedException(Exception exception, ErrorReportContext context);
}

public sealed class StructuredLogErrorReporter(ILogger<StructuredLogErrorReporter> logger) : IErrorReporter
{
    private static readonly EventId UnexpectedExceptionEvent = new(91001, "PrivateServerUnexpectedException");

    public string CaptureUnexpectedException(Exception exception, ErrorReportContext context)
    {
        var safe = MonitoringEventSanitizer.Sanitize(exception, context);
        var reference = "local-" + safe.StackFingerprint[..16] + "-"
            + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(safe.CorrelationId)))[..12];
        // Do not pass the original exception to ILogger: its message, Data, inner exception, or
        // stack frames may contain client/accounting material. The sanitizer emits a fixed schema.
        logger.LogError(
            UnexpectedExceptionEvent,
            "Sanitized unexpected application event {EventReference} {EventCode} {ExceptionType} {HttpMethod} {RequestPath} {CorrelationId} {StackFingerprint}",
            reference,
            MonitoringEventSanitizer.SafeLogField(safe.EventCode),
            MonitoringEventSanitizer.SafeLogField(safe.ExceptionType),
            MonitoringEventSanitizer.SafeLogField(safe.Method),
            MonitoringEventSanitizer.SafeLogField(safe.Path),
            MonitoringEventSanitizer.SafeLogField(safe.CorrelationId),
            MonitoringEventSanitizer.SafeLogField(safe.StackFingerprint));
        return reference;
    }
}

public sealed class SentryErrorReporter : IErrorReporter
{
    public string CaptureUnexpectedException(Exception exception, ErrorReportContext context)
    {
        var safe = MonitoringEventSanitizer.Sanitize(exception, context);
        var eventId = SentrySdk.CaptureException(new RedactedMonitoringException(), scope =>
        {
            scope.SetTag("correlation_id", MonitoringEventSanitizer.SafeLogField(safe.CorrelationId));
            scope.SetTag("http_method", MonitoringEventSanitizer.SafeLogField(safe.Method));
            scope.SetTag("request_path", MonitoringEventSanitizer.SafeLogField(safe.Path));
            scope.SetTag("exception_type", MonitoringEventSanitizer.SafeLogField(safe.ExceptionType));
            scope.SetTag("stack_fingerprint", MonitoringEventSanitizer.SafeLogField(safe.StackFingerprint));
            scope.SetTag("event_code", MonitoringEventSanitizer.SafeLogField(safe.EventCode));
        });
        return eventId.ToString();
    }
}

public sealed record SanitizedMonitoringEvent(
    string ExceptionType,
    string Method,
    string Path,
    string CorrelationId,
    string StackFingerprint,
    string EventCode);

public static partial class MonitoringEventSanitizer
{
    private const int MaxPathSegmentLength = 64;
    private static readonly HashSet<string> AllowedClientRouteSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "api",
        "about",
        "change-password",
        "charity",
        "classify",
        "companies",
        "login",
        "new",
        "notes",
        "periods",
        "production-readiness",
        "statements",
        "workbench-preview",
        "year-end"
    };
    private static readonly HashSet<string> AllowedEventTags = new(StringComparer.Ordinal)
    {
        "correlation_id",
        "http_method",
        "request_path",
        "exception_type",
        "stack_fingerprint",
        "event_code"
    };

    public static SanitizedMonitoringEvent Sanitize(Exception exception, ErrorReportContext context)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(context);

        var exceptionType = SafeExceptionType(exception.GetType());
        return new SanitizedMonitoringEvent(
            exceptionType,
            SafeMethod(context.Method),
            SafePath(context.Path),
            SafeCorrelationId(context.CorrelationId),
            Fingerprint(exceptionType, exception.StackTrace),
            SafeEventCode(context.EventCode));
    }

    public static string SafeClientPath(string? rawPath)
    {
        var withoutQuery = (rawPath ?? "").Split(['?', '#'], 2)[0];
        var segments = withoutQuery
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(SafeClientPathSegment)
            .ToArray();
        return segments.Length == 0 ? "/" : "/" + string.Join('/', segments);
    }

    public static string SafeServerRoute(Endpoint? endpoint)
    {
        var rawPattern = (endpoint as RouteEndpoint)?.RoutePattern.RawText;
        if (string.IsNullOrWhiteSpace(rawPattern))
            return "/{unmatched}";

        var segments = rawPattern
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(segment => segment.StartsWith('{') && segment.EndsWith('}')
                ? "{id}"
                : SafeServerRouteSegment(segment))
            .ToArray();
        return segments.Length == 0 ? "/" : "/" + string.Join('/', segments);
    }

    public static string SafeClientCorrelationId(string? clientCorrelationId, string? serverCorrelationId)
    {
        if (string.IsNullOrWhiteSpace(clientCorrelationId))
            return SafeCorrelationId(serverCorrelationId);

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(clientCorrelationId.Trim()));
        return "client-corr-" + Convert.ToHexString(digest).ToLowerInvariant()[..24];
    }

    public static string SafeLogField(string? value)
        => (value ?? "").Replace('\r', '_').Replace('\n', '_');

    public static SentryEvent ScrubSentryEvent(SentryEvent sentryEvent)
    {
        ArgumentNullException.ThrowIfNull(sentryEvent);
        sentryEvent.Request = null!;
        sentryEvent.User = null!;
        sentryEvent.ServerName = null;
        sentryEvent.TransactionName = sentryEvent.TransactionName is null
            ? null
            : SafeClientPath(sentryEvent.TransactionName);
        foreach (var key in sentryEvent.Tags.Keys.Where(key => !AllowedEventTags.Contains(key)).ToArray())
            sentryEvent.UnsetTag(key);
        return sentryEvent;
    }

    private static string SafeExceptionType(Type exceptionType)
    {
        var value = exceptionType.FullName ?? exceptionType.Name;
        var safe = TypeNameUnsafeCharacters().Replace(value, "_");
        return safe.Length <= 160 ? safe : safe[..160];
    }

    private static string SafeMethod(string? method)
    {
        var candidate = method?.Trim().ToUpperInvariant() ?? "";
        return candidate switch
        {
            "CLIENT" => "CLIENT",
            "DELETE" => "DELETE",
            "GET" => "GET",
            "HEAD" => "HEAD",
            "OPTIONS" => "OPTIONS",
            "PATCH" => "PATCH",
            "POST" => "POST",
            "PUT" => "PUT",
            _ => "UNKNOWN"
        };
    }

    private static string SafePath(string? rawPath)
    {
        var withoutQuery = (rawPath ?? "").Split(['?', '#'], 2)[0];
        var segments = withoutQuery
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(SafePathSegment)
            .ToArray();
        return segments.Length == 0 ? "/" : "/" + string.Join('/', segments);
    }

    private static string SafePathSegment(string segment)
    {
        if (segment is "{id}" or "{redacted}")
            return segment;
        if (segment.Length > MaxPathSegmentLength || segment.Contains('@') || Guid.TryParse(segment, out _))
            return "{redacted}";
        if (segment.All(char.IsDigit))
            return "{id}";
        return PathSegmentUnsafeCharacters().Replace(segment, "_");
    }

    private static string SafeClientPathSegment(string segment)
    {
        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(segment);
        }
        catch
        {
            return "{redacted}";
        }

        if (decoded is "{id}" or "{redacted}")
            return decoded;
        if (decoded.All(char.IsDigit) && decoded.Length > 0)
            return "{id}";
        return AllowedClientRouteSegments.Contains(decoded) ? decoded.ToLowerInvariant() : "{redacted}";
    }

    private static string SafeServerRouteSegment(string segment)
    {
        if (segment.Length is 0 or > MaxPathSegmentLength)
            return "{redacted}";
        return PathSegmentUnsafeCharacters().Replace(segment, "_");
    }

    private static string SafeCorrelationId(string? correlationId)
    {
        var candidate = correlationId?.Trim() ?? "";
        if (candidate.Length is > 0 and <= 128 && CorrelationIdPattern().IsMatch(candidate))
            return candidate;
        return "corr-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(candidate))).ToLowerInvariant()[..24];
    }

    private static string SafeEventCode(string? eventCode)
    {
        var candidate = eventCode?.Trim().ToLowerInvariant() ?? "";
        return candidate switch
        {
            "api-contract-rejection" => "api-contract-rejection",
            "api-network-failure" => "api-network-failure",
            "api-server-rejection" => "api-server-rejection",
            "api-timeout" => "api-timeout",
            "auth-service-unavailable" => "auth-service-unavailable",
            "render-exception" => "render-exception",
            "unexpected-exception" => "unexpected-exception",
            "unhandled-client-exception" => "unhandled-client-exception",
            _ => "invalid-event-code"
        };
    }

    private static string Fingerprint(string exceptionType, string? stackTrace)
    {
        var bytes = Encoding.UTF8.GetBytes(exceptionType + "\n" + (stackTrace ?? "no-stack"));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    [GeneratedRegex("[^A-Za-z0-9_.+`]")]
    private static partial Regex TypeNameUnsafeCharacters();

    [GeneratedRegex("[^A-Za-z0-9._~-]")]
    private static partial Regex PathSegmentUnsafeCharacters();

    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex CorrelationIdPattern();

}

public sealed class RedactedMonitoringException() : Exception("Unexpected application exception; message and request data redacted.")
{
}

public sealed class MonitoringSmokeException() : Exception("Controlled non-PII monitoring smoke event.")
{
}

public sealed class ClientMonitoringException() : Exception("Controlled non-PII client monitoring event.")
{
}
