using Accounts.Api.Services;

namespace Accounts.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var auth = app.MapGroup("/api/auth").WithTags("Auth");

        auth.MapPost("/login", async (LoginInput input, AuthService authService, HttpContext context) =>
        {
            var result = await authService.LoginAsync(input.Email, input.Password);
            if (!result.Succeeded || result.User is null)
                return Results.Unauthorized();

            var now = DateTimeOffset.UtcNow;
            context.Response.Cookies.Append(
                authService.CookieName,
                authService.CreateSessionCookieValue(result.User, now),
                authService.CreateCookieOptions(now));

            return Results.Ok(AuthResponse.From(result.User));
        });

        auth.MapPost("/logout", (AuthService authService, HttpContext context) =>
        {
            context.Response.Cookies.Delete(authService.CookieName, authService.ClearCookieOptions());
            return Results.NoContent();
        });

        auth.MapGet("/me", (HttpContext context) =>
            Results.Ok(AuthResponse.From(AuthContext.RequireUser(context))));
    }
}

public record LoginInput(string? Email, string? Password);

public record AuthResponse(
    int UserId,
    int TenantId,
    string TenantName,
    string Email,
    string DisplayName,
    string Role)
{
    public static AuthResponse From(AuthenticatedUser user) => new(
        user.UserId,
        user.TenantId,
        user.TenantName,
        user.Email,
        user.DisplayName,
        user.Role);
}
