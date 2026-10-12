namespace Accounts.Api.Services;

/// <summary>
/// The single list of API writes that need a fresh human confirmation. Browser sessions satisfy it
/// with a recent TOTP (<see cref="Middleware.RecentAuthenticationMiddleware"/>); connector sessions
/// satisfy it with a single-use per-action approval. Keeping one list stops the two gates drifting.
/// </summary>
public static class PrivilegedRouteCatalog
{
    public static bool RequiresHumanConfirmation(PathString path, string method)
    {
        if (HttpMethods.IsDelete(method)) return path.StartsWithSegments("/api");
        if (!(HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method))) return false;

        if (path.StartsWithSegments("/api/privacy", StringComparison.OrdinalIgnoreCase)) return true;
        if (path.StartsWithSegments("/api/admin/users", StringComparison.OrdinalIgnoreCase)) return true;
        if (path.StartsWithSegments("/api/operations/deadline-reminders", StringComparison.OrdinalIgnoreCase)) return true;
        if (path.Equals("/api/auth/mfa/recovery-codes", StringComparison.OrdinalIgnoreCase)) return true;

        // Routing ignores a trailing slash, so the suffix rules must too.
        var value = (path.Value ?? "").TrimEnd('/');
        return value.Contains("/quarantine", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("/recover", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("/categories/chart-adoption", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("/classification/historic-anchor", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("/classification/historic-anchor/retire", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("/tax-balances/CorporationTax/confirm-due", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("/status", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("/approve", StringComparison.OrdinalIgnoreCase)
            || value.Contains("/year-end-reviews/", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("/mark-filed", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("/revenue-external-validation", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("/filing/directors-approval", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("/filing/directors-approval-withdrawal", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("/filing/cro-not-delivered", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("/filing/cro-signing-pack", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("/filing/auditor-report", StringComparison.OrdinalIgnoreCase)
            || value.Contains("/external-filing-handoff/", StringComparison.OrdinalIgnoreCase)
            || value.Contains("/release-evidence", StringComparison.OrdinalIgnoreCase)
            || value.Contains("/accept", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Routes a connector session may only use when it was connected at the Admin access level.
    /// Every human-confirmation route is admin-level; so is all identity, privacy and operations
    /// administration and company lifecycle.
    /// </summary>
    public static bool IsAdminLevel(PathString path, string method)
    {
        if (RequiresHumanConfirmation(path, method)) return true;
        if (path.StartsWithSegments("/api/admin", StringComparison.OrdinalIgnoreCase)) return true;
        if (path.StartsWithSegments("/api/privacy", StringComparison.OrdinalIgnoreCase)) return true;
        if (path.StartsWithSegments("/api/operations", StringComparison.OrdinalIgnoreCase)) return true;
        if (path.StartsWithSegments("/api/system/production-readiness", StringComparison.OrdinalIgnoreCase)) return true;
        if (path.StartsWithSegments("/api/system/platform-metrics", StringComparison.OrdinalIgnoreCase)) return true;

        var value = (path.Value ?? "").TrimEnd('/');
        if (HttpMethods.IsGet(method)
            && value.Equals("/api/companies/quarantined", StringComparison.OrdinalIgnoreCase))
            return true;
        if (HttpMethods.IsPost(method)
            && (value.Equals("/api/companies", StringComparison.OrdinalIgnoreCase)
                || value.Equals("/api/companies/onboard", StringComparison.OrdinalIgnoreCase)))
            return true;
        if (HttpMethods.IsPost(method)
            && value.EndsWith("/audit-log/integrity/checkpoints", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }
}
