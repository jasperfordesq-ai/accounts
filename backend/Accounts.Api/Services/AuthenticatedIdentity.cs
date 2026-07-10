namespace Accounts.Api.Services;

public static class AuthenticatedIdentity
{
    public static string ReviewerDisplayName(AuthenticatedUser user) =>
        string.IsNullOrWhiteSpace(user.DisplayName) ? $"User {user.UserId}" : user.DisplayName.Trim();

    public static string AuditUserId(AuthenticatedUser user) =>
        $"user:{user.UserId}";
}
