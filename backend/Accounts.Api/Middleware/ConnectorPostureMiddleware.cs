using System.Text.Json;
using System.Text.RegularExpressions;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;

namespace Accounts.Api.Middleware;

/// <summary>
/// Applies the posture a connector session was granted at sign-in: access level, personal-data
/// scope and write budget, and records every connector write, refusal and personal-data read.
/// A no-op for browser sessions.
/// </summary>
public sealed partial class ConnectorPostureMiddleware(RequestDelegate next)
{
    private const int MaxInspectedBodyBytes = 6 * 1024 * 1024;

    public async Task InvokeAsync(
        HttpContext context,
        ConnectorWriteBudget? budget = null,
        IServiceScopeFactory? scopeFactory = null,
        TimeProvider? timeProvider = null)
    {
        var connector = ConnectorSessionContext.Get(context);
        var user = AuthContext.GetUser(context);
        if (connector is null || user is null)
        {
            await next(context);
            return;
        }

        var request = context.Request;
        var path = request.Path;
        var method = request.Method;
        var unsafeMethod = IsUnsafe(method);
        var refused = false;
        try
        {
            if (IsBrowserOnlyAuthRoute(path))
            {
                refused = true;
                await ConnectorClientGuard.WriteRefusalAsync(context, StatusCodes.Status403Forbidden,
                    ConnectorErrorCodes.NotPermitted, "This route is only available to the FilingBridge web app.");
                return;
            }

            var selfService = IsConnectorSelfService(path);
            if (!selfService && !connector.IsAdmin && PrivilegedRouteCatalog.IsAdminLevel(path, method))
            {
                refused = true;
                await ConnectorClientGuard.WriteRefusalAsync(context, StatusCodes.Status403Forbidden,
                    ConnectorErrorCodes.SessionLevelTooLow, "Reconnect the agent at the Admin access level to use this action.");
                return;
            }

            if (!selfService && unsafeMethod && connector.IsReadOnly)
            {
                refused = true;
                await ConnectorClientGuard.WriteRefusalAsync(context, StatusCodes.Status403Forbidden,
                    ConnectorErrorCodes.SessionReadOnly, "This agent was connected read-only.");
                return;
            }

            if (!connector.HasFullDataScope)
            {
                if (PersonalDataRouteCatalog.IsWithheldRoute(path, method)
                    || await SetsWithheldFieldAsync(request, PersonalDataRouteCatalog.WithheldBodyFields(path, method)))
                {
                    refused = true;
                    await ConnectorClientGuard.WriteRefusalAsync(context, StatusCodes.Status403Forbidden,
                        ConnectorErrorCodes.DataScopeWithheld,
                        "This agent was connected without personal-data access. An Owner can reconnect with full data scope.");
                    return;
                }
            }

            if (unsafeMethod && budget is not null && !IsBudgetExempt(path))
            {
                var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
                if (!budget.TryTake(connector.FamilyId, now, out var retryAfter))
                {
                    refused = true;
                    context.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    await ConnectorClientGuard.WriteRefusalAsync(context, StatusCodes.Status429TooManyRequests,
                        ConnectorErrorCodes.WriteLimit, $"The agent may make at most {budget.Limit} changes a minute.");
                    return;
                }
            }

            await next(context);
        }
        finally
        {
            var status = context.Response.StatusCode;
            if (scopeFactory is not null
                && (unsafeMethod || refused || IsRecordedStatus(status) || PersonalDataRouteCatalog.IsPersonalDataRead(path, method))
                && !IsBudgetExempt(path))
            {
                await RecordAsync(context, scopeFactory, user, connector, status, timeProvider ?? TimeProvider.System);
            }
        }
    }

    public static string RoutePatternOf(HttpContext context) =>
        (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText is { Length: > 0 } pattern
            ? (pattern.StartsWith('/') ? pattern : "/" + pattern)
            : context.Request.Path.Value ?? "";

    public static string? ResourceIdOf(PathString path)
    {
        var segments = (path.Value ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var index = segments.Length - 1; index >= 0; index--)
        {
            if (ResourceSegment().IsMatch(segments[index])) return segments[index];
        }
        return null;
    }

    private static async Task RecordAsync(
        HttpContext context,
        IServiceScopeFactory scopeFactory,
        AuthenticatedUser user,
        ConnectorSessionContext connector,
        int status,
        TimeProvider timeProvider)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            scope.ServiceProvider.GetService<DatabaseTenantBootstrapResolver>()?.UseVerifiedSessionTenant(user.TenantId);
            var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            int? companyId = TenantAccessMiddleware.TryGetCompanyId(context.Request.Path, out var parsedCompanyId) ? parsedCompanyId : null;
            db.ConnectorActivityEvents.Add(new ConnectorActivityEvent
            {
                TenantId = user.TenantId,
                UserId = user.UserId,
                SessionId = connector.SessionId,
                SessionFamilyId = connector.FamilyId,
                AccessLevel = connector.AccessLevel,
                DataScope = connector.DataScope,
                Method = context.Request.Method.ToUpperInvariant(),
                RoutePattern = Truncate(RoutePatternOf(context), 300),
                ResourceId = ResourceIdOf(context.Request.Path),
                TargetCompanyId = companyId,
                StatusCode = status,
                RequestId = Truncate(context.Request.Headers["X-Correlation-ID"].FirstOrDefault() ?? context.TraceIdentifier, 128),
                Reason = ConnectorAudit.Reason(context),
                OccurredAtUtc = timeProvider.GetUtcNow().UtcDateTime
            });
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            context.RequestServices.GetService<ILogger<ConnectorPostureMiddleware>>()?
                .LogWarning(exception, "Connector activity could not be recorded.");
        }
    }

    private static async Task<bool> SetsWithheldFieldAsync(HttpRequest request, IReadOnlySet<string> withheld)
    {
        if (withheld.Count == 0 || request.ContentLength is 0) return false;
        if (request.ContentLength > MaxInspectedBodyBytes) return true;
        request.EnableBuffering();
        try
        {
            using var document = await JsonDocument.ParseAsync(request.Body);
            return ContainsWithheld(document.RootElement, withheld);
        }
        catch (JsonException)
        {
            return false;
        }
        finally
        {
            request.Body.Position = 0;
        }
    }

    private static bool ContainsWithheld(JsonElement element, IReadOnlySet<string> withheld)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (withheld.Contains(property.Name) && HasValue(property.Value)) return true;
                    if (ContainsWithheld(property.Value, withheld)) return true;
                }
                return false;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    if (ContainsWithheld(item, withheld)) return true;
                return false;
            default:
                return false;
        }
    }

    private static bool HasValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => false,
        JsonValueKind.String => !string.IsNullOrWhiteSpace(value.GetString()),
        _ => true
    };

    private static bool IsBrowserOnlyAuthRoute(PathString path) =>
        path.StartsWithSegments("/api/auth", StringComparison.OrdinalIgnoreCase)
        && !path.StartsWithSegments("/api/auth/connector", StringComparison.OrdinalIgnoreCase)
        && !path.Equals("/api/auth/me", StringComparison.OrdinalIgnoreCase);

    private static bool IsConnectorSelfService(PathString path) =>
        path.StartsWithSegments("/api/auth/connector", StringComparison.OrdinalIgnoreCase);

    private static bool IsBudgetExempt(PathString path) =>
        path.StartsWithSegments("/api/auth/connector", StringComparison.OrdinalIgnoreCase);

    private static bool IsRecordedStatus(int status) =>
        status is StatusCodes.Status401Unauthorized
            or StatusCodes.Status403Forbidden
            or StatusCodes.Status428PreconditionRequired
            or StatusCodes.Status429TooManyRequests;

    private static bool IsUnsafe(string method) =>
        !HttpMethods.IsGet(method)
        && !HttpMethods.IsHead(method)
        && !HttpMethods.IsOptions(method)
        && !HttpMethods.IsTrace(method);

    private static string Truncate(string value, int length) => value.Length > length ? value[..length] : value;

    [GeneratedRegex(@"^(\d+|[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})$")]
    private static partial Regex ResourceSegment();
}
