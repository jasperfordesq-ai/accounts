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

public sealed class SentryErrorReporter : IErrorReporter
{
    public string CaptureUnexpectedException(Exception exception, ErrorReportContext context)
    {
        var safe = MonitoringEventSanitizer.Sanitize(exception, context);
        var eventId = SentrySdk.CaptureException(new RedactedMonitoringException(), scope =>
        {
            scope.SetTag("correlation_id", safe.CorrelationId);
            scope.SetTag("http_method", safe.Method);
            scope.SetTag("request_path", safe.Path);
            scope.SetTag("exception_type", safe.ExceptionType);
            scope.SetTag("stack_fingerprint", safe.StackFingerprint);
            scope.SetTag("event_code", safe.EventCode);
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

    public static SentryEvent ScrubSentryEvent(SentryEvent sentryEvent)
    {
        ArgumentNullException.ThrowIfNull(sentryEvent);
        sentryEvent.Request = null!;
        sentryEvent.User = null!;
        sentryEvent.ServerName = null;
        sentryEvent.TransactionName = sentryEvent.TransactionName is null
            ? null
            : SafePath(sentryEvent.TransactionName);
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
        return candidate.Length is > 0 and <= 16 && candidate.All(character => character is >= 'A' and <= 'Z')
            ? candidate
            : "UNKNOWN";
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
        return candidate.Length is > 0 and <= 64 && EventCodePattern().IsMatch(candidate)
            ? candidate
            : "invalid-event-code";
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

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]*$")]
    private static partial Regex EventCodePattern();
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
