namespace Accounts.Api.Rules;

/// <summary>
/// MCP connector credentials. Disabled unless explicitly enabled; when enabled outside
/// Development, <see cref="Services.ProductionSafetyService"/> requires a strong signing key
/// that differs from the browser session and identity keys.
/// </summary>
public sealed class ConnectorConfig
{
    public bool Enabled { get; set; }

    /// <summary>Base64/Base64Url HMAC key for connector access tokens (separate from AuthSession).</summary>
    public string SigningKey { get; set; } = "";

    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshIdleDays { get; set; } = 30;
    public int FamilyAbsoluteDays { get; set; } = 90;
    public int ApprovalMinutes { get; set; } = 15;
    public int WriteBudgetPerMinute { get; set; } = 30;
    public int ActivityRetentionDays { get; set; } = 400;

    /// <summary>Public frontend origin used to build the web approvals link in 428 responses.</summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>
    /// HTTPS hosts an OAuth client may redirect to (exact host match). Loopback HTTP redirects
    /// (127.0.0.1, localhost, [::1]) are always allowed for desktop clients such as Claude Code.
    /// </summary>
    public List<string> OAuthRedirectHosts { get; set; } = ["claude.ai", "claude.com"];

    public static bool IsValid(ConnectorConfig config) =>
        config.AccessTokenMinutes is >= 5 and <= 30
        && config.RefreshIdleDays is >= 1 and <= 90
        && config.FamilyAbsoluteDays is >= 1 and <= 365
        && config.FamilyAbsoluteDays >= config.RefreshIdleDays
        && config.ApprovalMinutes is >= 2 and <= 30
        && config.WriteBudgetPerMinute is >= 5 and <= 120
        && config.ActivityRetentionDays is >= 30 and <= 3650
        && config.OAuthRedirectHosts.All(host => !string.IsNullOrWhiteSpace(host)
            && Uri.CheckHostName(host.Trim()) is UriHostNameType.Dns or UriHostNameType.IPv4)
        && (string.IsNullOrWhiteSpace(config.PublicBaseUrl)
            || (Uri.TryCreate(config.PublicBaseUrl, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)));
}
