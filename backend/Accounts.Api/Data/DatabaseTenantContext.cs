using Accounts.Api.Services;

namespace Accounts.Api.Data;

/// <summary>
/// Request-scoped tenant state used by the PostgreSQL RLS connection interceptor. Authenticated
/// requests normally resolve from <see cref="AuthContext"/>. Anonymous authentication flows may
/// set a tenant only after a narrow database resolver has mapped a signed session or opaque token
/// to its authoritative tenant.
/// </summary>
public sealed class DatabaseTenantContext(IHttpContextAccessor httpContextAccessor)
{
    private int? resolvedTenantId;

    public int? TenantId => resolvedTenantId
        ?? (httpContextAccessor.HttpContext is { } context
            ? AuthContext.GetUser(context)?.TenantId
            : null);

    public void SetResolvedTenant(int tenantId)
    {
        if (tenantId <= 0)
            throw new ArgumentOutOfRangeException(nameof(tenantId), "A database tenant context must be a positive identifier.");

        var authenticatedTenant = httpContextAccessor.HttpContext is { } context
            ? AuthContext.GetUser(context)?.TenantId
            : null;
        if (authenticatedTenant is > 0 && authenticatedTenant != tenantId)
            throw new InvalidOperationException("Database tenant context cannot differ from the authenticated tenant.");

        if (resolvedTenantId is > 0 && resolvedTenantId != tenantId)
            throw new InvalidOperationException("Database tenant context cannot change tenant within one request scope.");

        resolvedTenantId = tenantId;
    }

    public void ClearResolvedTenant() => resolvedTenantId = null;
}
