using Sentry;

namespace Accounts.Api.Services;

public sealed record ErrorReportContext(string Method, string Path, string CorrelationId);

public interface IErrorReporter
{
    string CaptureUnexpectedException(Exception exception, ErrorReportContext context);
}

public sealed class SentryErrorReporter : IErrorReporter
{
    public string CaptureUnexpectedException(Exception exception, ErrorReportContext context)
    {
        var eventId = SentrySdk.CaptureException(exception, scope =>
        {
            scope.SetTag("correlation_id", context.CorrelationId);
            scope.SetTag("http_method", context.Method);
            scope.SetTag("request_path", context.Path);
        });
        return eventId.ToString();
    }
}

public sealed class MonitoringSmokeException() : Exception("Controlled non-PII monitoring smoke event.")
{
}
