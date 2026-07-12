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
    string Role,
    IReadOnlySet<int>? AllowedCompanyIds = null,
    int SessionVersion = 1,
    string CsrfToken = "",
    bool MustChangePassword = false,
    DateTime PasswordLastChangedAt = default,
    DateTimeOffset? SessionIssuedAtUtc = null,
    DateTimeOffset? LastActivityAtUtc = null,
    DateTimeOffset? MfaVerifiedAtUtc = null,
    string? MfaMethod = null)
{
    public IReadOnlySet<int> AllowedCompanyIds { get; init; } = AllowedCompanyIds ?? new HashSet<int>();
    public string TenantSlug { get; init; } = "";
}

public static class UserCompanyAccessPolicy
{
    public static bool RequiresCompanyAssignments(AuthenticatedUser user) =>
        user.Role.Trim().Equals("Client", StringComparison.OrdinalIgnoreCase);

    public static bool CanAccessCompany(AuthenticatedUser user, int companyId) =>
        !RequiresCompanyAssignments(user) || user.AllowedCompanyIds.Contains(companyId);

    public static IQueryable<Accounts.Api.Entities.Company> ApplyToQuery(
        AuthenticatedUser user,
        IQueryable<Accounts.Api.Entities.Company> query)
    {
        if (!RequiresCompanyAssignments(user))
            return query;

        var allowedCompanyIds = user.AllowedCompanyIds.ToArray();
        return query.Where(c => allowedCompanyIds.Contains(c.Id));
    }
}
