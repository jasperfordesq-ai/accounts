using Accounts.Api.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Accounts.Tests;

public sealed class DatabaseTenantIsolationSqlTests
{
    [Fact]
    public void PolicyCatalog_ClassifiesEveryMappedDomainTableExactlyOnce()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase("tenant-policy-model-" + Guid.NewGuid())
            .Options;
        using var db = new AccountsDbContext(options);
        var mappedTables = db.Model.GetEntityTypes()
            .Select(entity => entity.GetTableName())
            .Where(table => !string.IsNullOrWhiteSpace(table))
            .Select(table => table!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(TenantIsolationPolicyCatalog.Policies.Count, TenantIsolationPolicyCatalog.TableNames.Count);
        Assert.Equal(
            TenantIsolationPolicyCatalog.Policies.OrderBy(item => item.TableName),
            TenantIsolationMigrationSql.Version1PolicyInventory.OrderBy(item => item.TableName));
        Assert.Empty(mappedTables.Except(TenantIsolationPolicyCatalog.TableNames, StringComparer.Ordinal));
        Assert.Empty(TenantIsolationPolicyCatalog.TableNames.Except(mappedTables, StringComparer.Ordinal));
    }

    [Fact]
    public void InstallSql_ForcesRlsUsesNarrowRolesAndProtectsBootstrapFunctions()
    {
        var sql = TenantIsolationMigrationSql.BuildInstallSql(TenantIsolationPolicyCatalog.Policies);

        Assert.Contains("CREATE ROLE accounts_api_rls NOLOGIN NOSUPERUSER NOBYPASSRLS", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE ROLE accounts_migration_rls_admin NOLOGIN NOSUPERUSER NOBYPASSRLS", sql, StringComparison.Ordinal);
        Assert.Contains("REVOKE accounts_migration_rls_admin FROM accounts_api_rls", sql, StringComparison.Ordinal);
        Assert.Contains("SECURITY DEFINER", sql, StringComparison.Ordinal);
        Assert.Contains("SET search_path FROM CURRENT", sql, StringComparison.Ordinal);
        Assert.Contains("REVOKE ALL PRIVILEGES ON TABLE tenant_rls_context_keys FROM accounts_api_rls", sql, StringComparison.Ordinal);
        Assert.Contains("REVOKE ALL ON FUNCTION accounts_resolve_login_tenant(text) FROM PUBLIC", sql, StringComparison.Ordinal);
        Assert.Contains("accounts_delete_expired_anonymous_login_events", sql, StringComparison.Ordinal);

        foreach (var policy in TenantIsolationPolicyCatalog.Policies)
        {
            Assert.Contains($"ALTER TABLE \"{policy.TableName}\" ENABLE ROW LEVEL SECURITY", sql, StringComparison.Ordinal);
            Assert.Contains($"ALTER TABLE \"{policy.TableName}\" FORCE ROW LEVEL SECURITY", sql, StringComparison.Ordinal);
            Assert.Contains($"ON \"{policy.TableName}\" AS PERMISSIVE FOR ALL TO \"accounts_migration_rls_admin\"", sql, StringComparison.Ordinal);
        }

        Assert.Contains("\"login_security_events\".\"TenantId\" IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("FROM \"bank_accounts\" AS tenant_bank", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("BYPASSRLS LOGIN", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Config_RejectsWeakKeysUnsafeRolesAndRoleConflation()
    {
        var strongKey = Convert.ToBase64String(Enumerable.Range(1, 64).Select(value => (byte)value).ToArray());
        Assert.True(DatabaseTenantIsolationConfig.IsValid(new DatabaseTenantIsolationConfig
        {
            Required = true,
            ApplicationLoginRole = "accounts_api",
            ContextSigningKey = strongKey
        }));
        Assert.False(DatabaseTenantIsolationConfig.IsValid(new DatabaseTenantIsolationConfig
        {
            Required = true,
            ApplicationLoginRole = "accounts_api",
            ContextSigningKey = Convert.ToBase64String(new byte[16])
        }));
        Assert.False(DatabaseTenantIsolationConfig.IsValid(new DatabaseTenantIsolationConfig
        {
            Required = true,
            ApplicationLoginRole = TenantIsolationPolicyCatalog.ApplicationGroupRole,
            ContextSigningKey = strongKey
        }));
        Assert.False(DatabaseTenantIsolationConfig.IsValid(new DatabaseTenantIsolationConfig
        {
            Required = true,
            ApplicationGroupRole = "unexpected_group",
            ApplicationLoginRole = "accounts_api",
            ContextSigningKey = strongKey
        }));
    }
}
