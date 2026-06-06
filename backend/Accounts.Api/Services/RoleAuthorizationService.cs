namespace Accounts.Api.Services;

public static class RoleAuthorizationService
{
    public static RoleAuthorizationDecision Authorize(AuthenticatedUser user, PathString path, string method)
    {
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
            return RoleAuthorizationDecision.Allowed();

        var role = user.Role.Trim();
        if (role.Equals("Owner", StringComparison.OrdinalIgnoreCase))
            return RoleAuthorizationDecision.Allowed();

        if (role.Equals("Client", StringComparison.OrdinalIgnoreCase))
            return RoleAuthorizationDecision.Denied("Client users are read-only.");

        if (role.Equals("Reviewer", StringComparison.OrdinalIgnoreCase))
        {
            return IsReviewerWrite(path)
                ? RoleAuthorizationDecision.Allowed()
                : RoleAuthorizationDecision.Denied("Reviewer users can approve, review, finalise, and update filing workflow only.");
        }

        if (role.Equals("Accountant", StringComparison.OrdinalIgnoreCase))
        {
            if (IsReviewerWrite(path) || IsCompanyDelete(path, method))
                return RoleAuthorizationDecision.Denied("Accountant users cannot perform reviewer or owner-only actions.");

            return RoleAuthorizationDecision.Allowed();
        }

        return RoleAuthorizationDecision.Denied("User role is not authorised.");
    }

    private static bool IsReviewerWrite(PathString path)
    {
        var segments = SplitPath(path);
        if (segments.Length == 0)
            return false;

        if (segments.Contains("approve", StringComparer.OrdinalIgnoreCase)
            || segments.Contains("year-end-reviews", StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (segments[^1].Equals("status", StringComparison.OrdinalIgnoreCase))
            return true;

        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (!segments[i].Equals("filing", StringComparison.OrdinalIgnoreCase))
                continue;

            if (segments[i + 1].Equals("cro-status", StringComparison.OrdinalIgnoreCase)
                || segments[i + 1].Equals("cro-payment", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCompanyDelete(PathString path, string method)
    {
        if (!HttpMethods.IsDelete(method))
            return false;

        var segments = SplitPath(path);
        return segments is { Length: 3 }
            && segments[0].Equals("api", StringComparison.OrdinalIgnoreCase)
            && segments[1].Equals("companies", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(segments[2], out _);
    }

    private static string[] SplitPath(PathString path) =>
        path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
}

public record RoleAuthorizationDecision(bool IsAllowed, string? DenialReason)
{
    public static RoleAuthorizationDecision Allowed() => new(true, null);

    public static RoleAuthorizationDecision Denied(string reason) => new(false, reason);
}
