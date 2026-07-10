using Accounts.Api.Data;
using Accounts.Api.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Accounts.Api.Services;

public sealed record SystemReadinessProbe(
    bool Ready,
    string Database,
    string Migrations,
    string Owner);

/// <summary>
/// Single-flight, short-lived readiness evidence. Load balancer bursts must not fan out into repeated
/// migration discovery and bootstrap-owner queries, while failures are cached only briefly.
/// </summary>
public sealed class SystemReadinessProbeService
{
    private static readonly TimeSpan SuccessfulCacheDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan FailedCacheDuration = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private readonly object cacheLock = new();
    private readonly Func<CancellationToken, Task<SystemReadinessProbe>> executeProbe;
    private readonly TimeProvider timeProvider;
    private SystemReadinessProbe? cached;
    private DateTime cacheExpiresAtUtc;

    public SystemReadinessProbeService(
        IServiceScopeFactory scopeFactory,
        IOptions<BootstrapOwnerConfig> bootstrapOptions,
        TimeProvider timeProvider)
        : this(
            cancellationToken => ProbeDatabaseAsync(scopeFactory, bootstrapOptions.Value, cancellationToken),
            timeProvider)
    {
    }

    public SystemReadinessProbeService(
        Func<CancellationToken, Task<SystemReadinessProbe>> executeProbe,
        TimeProvider timeProvider)
    {
        this.executeProbe = executeProbe ?? throw new ArgumentNullException(nameof(executeProbe));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<SystemReadinessProbe> GetAsync(CancellationToken cancellationToken = default)
    {
        var now = UtcNow();
        lock (cacheLock)
        {
            if (cached is not null && now < cacheExpiresAtUtc)
                return cached;
        }

        await refreshGate.WaitAsync(cancellationToken);
        try
        {
            now = UtcNow();
            lock (cacheLock)
            {
                if (cached is not null && now < cacheExpiresAtUtc)
                    return cached;
            }

            using var timeout = new CancellationTokenSource(ProbeTimeout);
            SystemReadinessProbe result;
            try
            {
                result = await executeProbe(timeout.Token);
            }
            catch
            {
                result = new SystemReadinessProbe(false, "schema_unavailable", "unknown", "unknown");
            }

            lock (cacheLock)
            {
                cached = result;
                cacheExpiresAtUtc = UtcNow().Add(result.Ready ? SuccessfulCacheDuration : FailedCacheDuration);
            }
            return result;
        }
        finally
        {
            refreshGate.Release();
        }
    }

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private static async Task<SystemReadinessProbe> ProbeDatabaseAsync(
        IServiceScopeFactory scopeFactory,
        BootstrapOwnerConfig bootstrap,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        if (!await db.Database.CanConnectAsync(cancellationToken))
            return new SystemReadinessProbe(false, "unavailable", "unknown", "unknown");

        var pendingMigrations = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
        if (pendingMigrations.Length > 0)
            return new SystemReadinessProbe(false, "reachable", "pending", "unknown");

        var ownerReady = await HasActiveOwnerAsync(scope.ServiceProvider, db, bootstrap, cancellationToken);
        return new SystemReadinessProbe(
            ownerReady,
            "reachable",
            "current",
            ownerReady ? "configured" : "missing");
    }

    private static async Task<bool> HasActiveOwnerAsync(
        IServiceProvider services,
        AccountsDbContext db,
        BootstrapOwnerConfig bootstrap,
        CancellationToken cancellationToken)
    {
        var email = bootstrap.OwnerEmail.Trim().ToLowerInvariant();
        var tenantSlug = bootstrap.TenantSlug.Trim().ToLowerInvariant();
        var resolver = services.GetRequiredService<DatabaseTenantBootstrapResolver>();
        if (!string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(tenantSlug))
        {
            var tenantId = await resolver.ResolveLoginTenantAsync(email, cancellationToken);
            return tenantId is > 0 && await db.UserAccounts.AnyAsync(
                user => user.IsActive
                    && user.Role.Trim().ToLower() == "owner"
                    && user.Email.ToLower() == email
                    && user.Tenant.Slug.ToLower() == tenantSlug,
                cancellationToken);
        }

        if (!db.Database.IsRelational())
            return await db.UserAccounts.AnyAsync(
                user => user.IsActive && user.Role.Trim().ToLower() == "owner",
                cancellationToken);

        foreach (var tenantId in await resolver.ListTenantIdsForJobsAsync(cancellationToken))
        {
            await using var tenantScope = services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
            var tenantResolver = tenantScope.ServiceProvider.GetRequiredService<DatabaseTenantBootstrapResolver>();
            tenantResolver.UseVerifiedSessionTenant(tenantId);
            var tenantDb = tenantScope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            if (await tenantDb.UserAccounts.AnyAsync(
                    user => user.IsActive && user.Role.Trim().ToLower() == "owner",
                    cancellationToken))
                return true;
        }
        return false;
    }
}
