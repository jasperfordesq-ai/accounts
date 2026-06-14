using System.Security.Cryptography;
using System.Text;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.Extensions.Options;

namespace Accounts.Api.Middleware;

public class CsrfProtectionMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-CSRF-Token";

    public async Task InvokeAsync(HttpContext context, IOptions<AuthSessionConfig> sessionOptions)
    {
        if (!RequiresCsrf(context.Request))
        {
            await next(context);
            return;
        }

        var user = AuthContext.GetUser(context);
        if (user is null)
        {
            await next(context);
            return;
        }

        var cookieName = string.IsNullOrWhiteSpace(sessionOptions.Value.CsrfCookieName)
            ? "accounts_csrf"
            : sessionOptions.Value.CsrfCookieName.Trim();
        var headerToken = context.Request.Headers[HeaderName].FirstOrDefault();
        var cookieToken = context.Request.Cookies[cookieName];

        if (!TokenMatches(user.CsrfToken, headerToken) || !TokenMatches(user.CsrfToken, cookieToken))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { error = "CSRF token is missing or invalid." });
            return;
        }

        await next(context);
    }

    private static bool RequiresCsrf(HttpRequest request)
    {
        if (!request.Path.StartsWithSegments("/api"))
            return false;

        if (HttpMethods.IsGet(request.Method)
            || HttpMethods.IsHead(request.Method)
            || HttpMethods.IsOptions(request.Method)
            || HttpMethods.IsTrace(request.Method))
            return false;

        return !(HttpMethods.IsPost(request.Method)
            && request.Path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TokenMatches(string expectedToken, string? presentedToken)
    {
        if (string.IsNullOrWhiteSpace(expectedToken) || string.IsNullOrWhiteSpace(presentedToken))
            return false;

        var expectedBytes = Encoding.UTF8.GetBytes(expectedToken.Trim());
        var presentedBytes = Encoding.UTF8.GetBytes(presentedToken.Trim());

        return expectedBytes.Length == presentedBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, presentedBytes);
    }
}
