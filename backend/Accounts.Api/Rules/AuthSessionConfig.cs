namespace Accounts.Api.Rules;

public class AuthSessionConfig
{
    public string CookieName { get; set; } = "accounts_session";
    public string CsrfCookieName { get; set; } = "accounts_csrf";
    public string SigningKey { get; set; } = "";
    public int ExpiryMinutes { get; set; } = 480;
    public bool SecureCookiesInProduction { get; set; } = true;
}
