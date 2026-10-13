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

        if (IsAuthenticatedLogout(path, method)
            || IsAuthenticatedPasswordChange(path, method)
            || IsAuthenticatedSecurityRequest(path, method)
            || path.StartsWithSegments("/api/auth/connector", StringComparison.OrdinalIgnoreCase))
            return RoleAuthorizationDecision.Allowed();

        if (IsClientMonitoringEvent(path, method))
            return RoleAuthorizationDecision.Allowed();

        var role = user.Role.Trim();
        if (IsRestrictedReleaseEvidenceRequest(path))
        {
            return role.Equals("Owner", StringComparison.OrdinalIgnoreCase)
                || role.Equals("Reviewer", StringComparison.OrdinalIgnoreCase)
                ? RoleAuthorizationDecision.Allowed()
                : RoleAuthorizationDecision.Denied(
                    "Owner or explicitly assigned Reviewer access is required for internal release evidence.");
        }

        if (IsInternalWorkingPaperRequest(path))
        {
            if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
            {
                return role.Equals("Owner", StringComparison.OrdinalIgnoreCase)
                    || role.Equals("Accountant", StringComparison.OrdinalIgnoreCase)
                    || role.Equals("Reviewer", StringComparison.OrdinalIgnoreCase)
                    ? RoleAuthorizationDecision.Allowed()
                    : RoleAuthorizationDecision.Denied("Internal accountant working papers are not available to Client users.");
            }

            return role.Equals("Owner", StringComparison.OrdinalIgnoreCase)
                || role.Equals("Accountant", StringComparison.OrdinalIgnoreCase)
                ? RoleAuthorizationDecision.Allowed()
                : RoleAuthorizationDecision.Denied("Only Owner or Accountant users can generate retained working papers.");
        }

        if (IsExternalFilingHandoffRequest(path))
        {
            var access = ExternalFilingHandoffAccess(path, method);
            return access switch
            {
                "read" when role.Equals("Owner", StringComparison.OrdinalIgnoreCase)
                    || role.Equals("Accountant", StringComparison.OrdinalIgnoreCase)
                    || role.Equals("Reviewer", StringComparison.OrdinalIgnoreCase) => RoleAuthorizationDecision.Allowed(),
                "prepare" when role.Equals("Owner", StringComparison.OrdinalIgnoreCase)
                    || role.Equals("Accountant", StringComparison.OrdinalIgnoreCase) => RoleAuthorizationDecision.Allowed(),
                "review" when role.Equals("Owner", StringComparison.OrdinalIgnoreCase)
                    || role.Equals("Reviewer", StringComparison.OrdinalIgnoreCase) => RoleAuthorizationDecision.Allowed(),
                "read" => RoleAuthorizationDecision.Denied("External filing handoff evidence is not available to Client users."),
                "prepare" => RoleAuthorizationDecision.Denied("Only Owner or Accountant users can prepare immutable handoff snapshots."),
                _ => RoleAuthorizationDecision.Denied("Only Owner or Reviewer users can govern authority evidence or record external outcomes.")
            };
        }

        if (IsChartAdoptionRequest(path, method))
        {
            return role.Equals("Owner", StringComparison.OrdinalIgnoreCase)
                ? RoleAuthorizationDecision.Allowed()
                : RoleAuthorizationDecision.Denied("Only an Owner can change the company's chart of accounts.");
        }

        if (IsHistoricAnchorEvidenceArchiveRequest(path))
        {
            return role.Equals("Owner", StringComparison.OrdinalIgnoreCase)
                || role.Equals("Accountant", StringComparison.OrdinalIgnoreCase)
                || role.Equals("Reviewer", StringComparison.OrdinalIgnoreCase)
                ? RoleAuthorizationDecision.Allowed()
                : RoleAuthorizationDecision.Denied("Archived historic size evidence is not available to Client users.");
        }

        if (IsHistoricAnchorRetirement(path, method))
        {
            return role.Equals("Owner", StringComparison.OrdinalIgnoreCase)
                ? RoleAuthorizationDecision.Allowed()
                : RoleAuthorizationDecision.Denied("Only an Owner can retire historic size figures.");
        }

        if (IsOwnerOnlyRequest(path, method))
        {
            return role.Equals("Owner", StringComparison.OrdinalIgnoreCase)
                ? RoleAuthorizationDecision.Allowed()
                : RoleAuthorizationDecision.Denied("Owner access is required for company creation, quarantine, or recovery.");
        }

        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
            return RoleAuthorizationDecision.Allowed();

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
            // Generating the approval page and recording the optional accountant review decide nothing.
            if (HttpMethods.IsPost(method)
                && (IsPeriodFilingAction(SplitPath(path), "directors-approval-page")
                    || IsPeriodFilingAction(SplitPath(path), "accountant-review")))
                return RoleAuthorizationDecision.Allowed();

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

        // Attesting historic size figures from filed accounts is an Owner/Reviewer act.
        if (HttpMethods.IsPut(method) && IsHistoricSizeFiguresRecord(segments))
            return true;

        // So is confirming that an exempt company's corporation-tax liability is due.
        if (HttpMethods.IsPost(method) && IsCorporationTaxDueConfirmation(segments))
            return true;

        if (HttpMethods.IsPut(method)
            && (IsPeriodFilingAction(segments, "cro-status")
                || IsPeriodFilingAction(segments, "revenue-status")
                || IsPeriodFilingAction(segments, "charity-status")))
            return true;

        if (HttpMethods.IsPost(method)
            && (IsPeriodFilingAction(segments, "cro-payment")
                || IsPeriodFilingAction(segments, "revenue-external-validation")
                || IsPeriodFilingAction(segments, "directors-approval")
                || IsPeriodFilingAction(segments, "directors-approval-withdrawal")
                || IsPeriodFilingAction(segments, "cro-not-delivered")
                || IsPeriodFilingAction(segments, "directors-approval-page")
                || IsPeriodFilingAction(segments, "cro-signing-pack")
                || IsPeriodFilingAction(segments, "auditor-report")
                || IsPeriodFilingAction(segments, "accountant-review")
                || IsPeriodAction(segments, "mark-filed")))
        {
            return true;
        }

        return false;
    }

    private static bool IsAuthenticatedLogout(PathString path, string method) =>
        HttpMethods.IsPost(method)
        && path.Equals("/api/auth/logout", StringComparison.OrdinalIgnoreCase);

    private static bool IsAuthenticatedPasswordChange(PathString path, string method) =>
        HttpMethods.IsPost(method)
        && path.Equals("/api/auth/password", StringComparison.OrdinalIgnoreCase);

    private static bool IsClientMonitoringEvent(PathString path, string method) =>
        HttpMethods.IsPost(method)
        && path.Equals("/api/system/monitoring/client-event", StringComparison.OrdinalIgnoreCase);

    private static bool IsRestrictedReleaseEvidenceRequest(PathString path) =>
        path.Equals("/api/system/production-readiness", StringComparison.OrdinalIgnoreCase);

    private static bool IsInternalWorkingPaperRequest(PathString path)
    {
        var segments = SplitPath(path);
        return segments.Length is 6 or 7
            && IsCompanyPeriodPrefix(segments)
            && segments[5].Equals("working-papers", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExternalFilingHandoffRequest(PathString path)
    {
        var segments = SplitPath(path);
        return segments.Length >= 7
            && IsCompanyPeriodPrefix(segments)
            && segments[5].Equals("external-filing-handoff", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExternalFilingHandoffAccess(PathString path, string method)
    {
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
            return "read";

        var segments = SplitPath(path);
        if (segments.Length >= 7
            && (segments[6].Equals("cro", StringComparison.OrdinalIgnoreCase)
                || segments[6].Equals("revenue", StringComparison.OrdinalIgnoreCase)))
            return "prepare";

        return "review";
    }

    private static bool IsOwnerOnlyRequest(PathString path, string method) =>
        path.StartsWithSegments("/api/admin/users", StringComparison.OrdinalIgnoreCase)
        || IsCompanyCreate(path, method)
        || IsCompanyDelete(path, method)
        || IsCompanyRecovery(path, method)
        || (HttpMethods.IsGet(method)
            && path.Equals("/api/companies/quarantined", StringComparison.OrdinalIgnoreCase));

    // Adopting a chart of accounts (and its dry-run plan) re-codes and archives the company's accounts.
    private static bool IsChartAdoptionRequest(PathString path, string method)
    {
        if (!HttpMethods.IsPost(method))
            return false;

        var segments = SplitPath(path);
        return segments.Length is 5 or 6
            && segments[0].Equals("api", StringComparison.OrdinalIgnoreCase)
            && segments[1].Equals("companies", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(segments[2], out _)
            && segments[3].Equals("categories", StringComparison.OrdinalIgnoreCase)
            && segments[4].Equals("chart-adoption", StringComparison.OrdinalIgnoreCase)
            && (segments.Length == 5 || segments[5].Equals("plan", StringComparison.OrdinalIgnoreCase));
    }

    // The archive of retired or replaced historic size evidence (read-only routes).
    private static bool IsHistoricAnchorEvidenceArchiveRequest(PathString path)
    {
        var segments = SplitPath(path);
        return segments.Length >= 8
            && IsCompanyPeriodPrefix(segments)
            && segments[5].Equals("classification", StringComparison.OrdinalIgnoreCase)
            && segments[6].Equals("historic-anchor", StringComparison.OrdinalIgnoreCase)
            && segments[7].Equals("archive", StringComparison.OrdinalIgnoreCase);
    }

    // Retiring historic size figures re-opens a period's classification and makes its successors stale.
    private static bool IsHistoricAnchorRetirement(PathString path, string method)
    {
        if (!HttpMethods.IsPost(method))
            return false;

        var segments = SplitPath(path);
        return segments is { Length: 8 }
            && IsCompanyPeriodPrefix(segments)
            && segments[5].Equals("classification", StringComparison.OrdinalIgnoreCase)
            && segments[6].Equals("historic-anchor", StringComparison.OrdinalIgnoreCase)
            && segments[7].Equals("retire", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCompanyRecovery(PathString path, string method)
    {
        if (!HttpMethods.IsPost(method))
            return false;

        var segments = SplitPath(path);
        return segments is { Length: 4 }
            && segments[0].Equals("api", StringComparison.OrdinalIgnoreCase)
            && segments[1].Equals("companies", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(segments[2], out _)
            && segments[3].Equals("recover", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPasswordChangeAllowedWhileLocked(PathString path, string method) =>
        (HttpMethods.IsGet(method) && path.Equals("/api/auth/me", StringComparison.OrdinalIgnoreCase))
        || IsAuthenticatedLogout(path, method)
        || IsClientMonitoringEvent(path, method)
        || (HttpMethods.IsPost(method) && path.Equals("/api/auth/password", StringComparison.OrdinalIgnoreCase));

    private static bool IsAuthenticatedSecurityRequest(PathString path, string method) =>
        HttpMethods.IsPost(method)
        && (path.Equals("/api/auth/reauthenticate", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/auth/mfa/recovery-codes", StringComparison.OrdinalIgnoreCase));

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

    private static bool IsHistoricSizeFiguresRecord(string[] segments) =>
        segments is { Length: 7 }
        && IsCompanyPeriodPrefix(segments)
        && segments[5].Equals("classification", StringComparison.OrdinalIgnoreCase)
        && segments[6].Equals("historic-anchor", StringComparison.OrdinalIgnoreCase);

    private static bool IsCorporationTaxDueConfirmation(string[] segments) =>
        segments is { Length: 8 }
        && IsCompanyPeriodPrefix(segments)
        && segments[5].Equals("tax-balances", StringComparison.OrdinalIgnoreCase)
        && segments[6].Equals("CorporationTax", StringComparison.OrdinalIgnoreCase)
        && segments[7].Equals("confirm-due", StringComparison.OrdinalIgnoreCase);

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
