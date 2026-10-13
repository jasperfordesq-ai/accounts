using System.Text.RegularExpressions;

namespace Accounts.Api.Services;

/// <summary>
/// Refuses connector traffic that carries any sign of a browser. A bearer credential is only
/// CSRF-safe because no browser ever attaches it automatically, so a request that also carries
/// browser navigation headers or the browser session cookies is refused before any credential is read.
/// </summary>
public static partial class ConnectorClientGuard
{
    public const string ClientHeader = "X-FilingBridge-Client";
    public const string ApprovalHeader = "X-FilingBridge-Approval";

    private static readonly string[] BrowserEvidenceHeaders = ["Origin", "Referer", "Sec-Fetch-Site", "Sec-Fetch-Dest"];

    public static string? Evaluate(HttpRequest request, string sessionCookieName, string csrfCookieName)
    {
        var client = request.Headers[ClientHeader].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(client) || !ClientPattern().IsMatch(client.Trim()))
            return ConnectorErrorCodes.BrowserClientRejected;
        foreach (var header in BrowserEvidenceHeaders)
        {
            if (request.Headers.ContainsKey(header))
                return ConnectorErrorCodes.BrowserClientRejected;
        }
        if (request.Cookies.ContainsKey(sessionCookieName) || request.Cookies.ContainsKey(csrfCookieName))
            return ConnectorErrorCodes.BrowserClientRejected;
        return null;
    }

    public static string? ClientVersion(HttpRequest request)
    {
        var client = request.Headers[ClientHeader].FirstOrDefault()?.Trim();
        if (client is null) return null;
        var match = ClientPattern().Match(client);
        return match.Success ? match.Groups["version"].Value : null;
    }

    public static bool HasBearer(HttpRequest request, out string? token)
    {
        token = null;
        var authorization = request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(authorization)
            || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return false;
        token = authorization["Bearer ".Length..].Trim();
        return true;
    }

    public static Task WriteRefusalAsync(HttpContext context, int statusCode, string code, string error)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsJsonAsync(new { error, code });
    }

    [GeneratedRegex(@"^mcp-connector/(?<version>\d{1,4}\.\d{1,4}\.\d{1,6}(?:[-+][0-9A-Za-z.-]{1,40})?)$")]
    private static partial Regex ClientPattern();
}
