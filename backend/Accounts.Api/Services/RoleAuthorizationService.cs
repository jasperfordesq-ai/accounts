namespace Accounts.Api.Services;

public static class RoleAuthorizationService
{
    public static RoleAuthorizationDecision Authorize(AuthenticatedUser user, PathString path, string method)
    {
        if (user.MustChangePassword)
        {
            return IsPasswordChangeAllowedWhileLocked(path, method)
                ? RoleAuthorizationDecision.Allowed()
                : RoleAuthorizationDecision.Denied("Change your password before continuing.");
        }

        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
            return RoleAuthorizationDecision.Allowed();

        if (IsAuthenticatedLogout(path, method))
            return RoleAuthorizationDecision.Allowed();

        var role = user.Role.Trim();
        if (role.Equals("Owner", StringComparison.OrdinalIgnoreCase))
            return RoleAuthorizationDecision.Allowed();

        if (role.Equals("Client", StringComparison.OrdinalIgnoreCase))
            return RoleAuthorizationDecision.Denied("Client users are read-only.");

        if (role.Equals("Reviewer", StringComparison.OrdinalIgnoreCase))
        {
            return IsReviewerWrite(path, method)
                ? RoleAuthorizationDecision.Allowed()
                : RoleAuthorizationDecision.Denied("Reviewer users can approve, review, finalise, and update filing workflow only.");
        }

        if (role.Equals("Accountant", StringComparison.OrdinalIgnoreCase))
        {
            if (IsReviewerWrite(path, method) || IsCompanyCreate(path, method) || IsCompanyDelete(path, method))
                return RoleAuthorizationDecision.Denied("Accountant users cannot perform reviewer or owner-only actions.");

            return RoleAuthorizationDecision.Allowed();
        }

        return RoleAuthorizationDecision.Denied("User role is not authorised.");
    }

    private static bool IsReviewerWrite(PathString path, string method)
    {
        var segments = SplitPath(path);

        if (HttpMethods.IsPost(method) && IsAdjustmentApproval(segments))
            return true;

        if (HttpMethods.IsPut(method) && IsYearEndReviewConfirmation(segments))
            return true;

        if (HttpMethods.IsPut(method) && IsPeriodStatusUpdate(segments))
            return true;

        if (HttpMethods.IsPut(method) && IsPeriodFilingAction(segments, "cro-status"))
            return true;

        if (HttpMethods.IsPost(method)
            && (IsPeriodFilingAction(segments, "cro-payment")
                || IsPeriodAction(segments, "mark-filed")))
        {
            return true;
        }

        return false;
    }

    private static bool IsAuthenticatedLogout(PathString path, string method) =>
        HttpMethods.IsPost(method)
        && path.Equals("/api/auth/logout", StringComparison.OrdinalIgnoreCase);

    private static bool IsPasswordChangeAllowedWhileLocked(PathString path, string method) =>
        (HttpMethods.IsGet(method) && path.Equals("/api/auth/me", StringComparison.OrdinalIgnoreCase))
        || IsAuthenticatedLogout(path, method)
        || (HttpMethods.IsPost(method) && path.Equals("/api/auth/password", StringComparison.OrdinalIgnoreCase));

    private static bool IsCompanyCreate(PathString path, string method)
    {
        if (!HttpMethods.IsPost(method))
            return false;

        var segments = SplitPath(path);
        return segments is { Length: 2 }
            && segments[0].Equals("api", StringComparison.OrdinalIgnoreCase)
            && segments[1].Equals("companies", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAdjustmentApproval(string[] segments) =>
        segments is { Length: 8 }
        && IsCompanyPeriodPrefix(segments)
        && segments[5].Equals("adjustments", StringComparison.OrdinalIgnoreCase)
        && int.TryParse(segments[6], out _)
        && segments[7].Equals("approve", StringComparison.OrdinalIgnoreCase);

    private static bool IsYearEndReviewConfirmation(string[] segments) =>
        segments is { Length: 7 }
        && IsCompanyPeriodPrefix(segments)
        && segments[5].Equals("year-end-reviews", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(segments[6]);

    private static bool IsPeriodStatusUpdate(string[] segments) =>
        IsPeriodAction(segments, "status");

    private static bool IsPeriodFilingAction(string[] segments, string action) =>
        segments is { Length: 7 }
        && IsCompanyPeriodPrefix(segments)
        && segments[5].Equals("filing", StringComparison.OrdinalIgnoreCase)
        && segments[6].Equals(action, StringComparison.OrdinalIgnoreCase);

    private static bool IsPeriodAction(string[] segments, string action) =>
        segments is { Length: 6 }
        && IsCompanyPeriodPrefix(segments)
        && segments[5].Equals(action, StringComparison.OrdinalIgnoreCase);

    private static bool IsCompanyPeriodPrefix(string[] segments) =>
        segments.Length >= 5
        && segments[0].Equals("api", StringComparison.OrdinalIgnoreCase)
        && segments[1].Equals("companies", StringComparison.OrdinalIgnoreCase)
        && int.TryParse(segments[2], out _)
        && segments[3].Equals("periods", StringComparison.OrdinalIgnoreCase)
        && int.TryParse(segments[4], out _);

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
