namespace Accounts.Api.Services;

public static class AuthenticatedIdentity
{
    public static string ReviewerDisplayName(AuthenticatedUser user) =>
        string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email.Trim() : user.DisplayName.Trim();

    public static string AuditUserId(AuthenticatedUser user) =>
        user.Email.Trim().ToLowerInvariant();
}
