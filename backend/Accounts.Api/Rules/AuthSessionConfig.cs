namespace Accounts.Api.Rules;

public class AuthSessionConfig
{
    public string CookieName { get; set; } = "accounts_session";
    public string CsrfCookieName { get; set; } = "accounts_csrf";
    public string SigningKey { get; set; } = "";
    public int ExpiryMinutes { get; set; } = 480;
    /// <summary>Sliding inactivity limit.</summary>
    public int IdleTimeoutMinutes { get; set; } = 30;
    /// <summary>Non-sliding maximum session age.</summary>
    public int AbsoluteLifetimeMinutes { get; set; } = 480;
    public int RecentMfaMinutes { get; set; } = 10;
    public int MfaChallengeMinutes { get; set; } = 5;
    public bool RequirePrivilegedMfa { get; set; } = true;
    public bool SecureCookiesInProduction { get; set; } = true;
}
