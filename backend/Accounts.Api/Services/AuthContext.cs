namespace Accounts.Api.Services;

public static class AuthContext
{
    public const string ItemKey = "AuthenticatedUser";

    public static AuthenticatedUser? GetUser(HttpContext context) =>
        context.Items[ItemKey] as AuthenticatedUser;

    public static AuthenticatedUser RequireUser(HttpContext context) =>
        GetUser(context) ?? throw new InvalidOperationException("Authenticated user is not available on this request.");
}

public record AuthenticatedUser(
    int UserId,
    int TenantId,
    string TenantName,
    string Email,
    string DisplayName,
    string Role);
