using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Accounts.Tests;

public sealed class DatabaseTenantIsolationPostgresTests : IAsyncLifetime
{
    private const string ConnectionEnvVar = "ACCOUNTS_POSTGRES_TEST_CONNECTION";
    private readonly string? baseConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvVar);
    private readonly string schemaName = "tenant_rls_" + Guid.NewGuid().ToString("N");
    private readonly string applicationRole = "rls_test_" + Guid.NewGuid().ToString("N")[..20];
    private readonly string applicationPassword = Convert.ToHexString(Guid.NewGuid().ToByteArray());
    private readonly string signingKey = Convert.ToBase64String(
        Enumerable.Range(1, 64).Select(value => (byte)value).ToArray());
    private string? administratorConnectionString;
    private string? applicationConnectionString;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(baseConnectionString)) return;
        var adminBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            SearchPath = schemaName,
            Pooling = false
        };
        administratorConnectionString = adminBuilder.ConnectionString;

        await using (var admin = new NpgsqlConnection(baseConnectionString))
        {
            await admin.OpenAsync();
            await using var command = admin.CreateCommand();
            command.CommandText = $"""
                CREATE ROLE "{applicationRole}" LOGIN PASSWORD '{applicationPassword}'
                    NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION INHERIT;
                CREATE SCHEMA "{schemaName}";
                """;
            await command.ExecuteNonQueryAsync();
        }

        await using (var db = CreateDb(administratorConnectionString))
        {
            await db.Database.MigrateAsync();
            var config = Configuration();
            await new DatabaseTenantIsolationProvisioner(db, Options.Create(config)).EnsureAsync();
            var connection = db.Database.GetDbConnection();
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                REVOKE CREATE ON SCHEMA "{schemaName}" FROM PUBLIC;
                REVOKE CREATE ON SCHEMA "{schemaName}" FROM "{TenantIsolationPolicyCatalog.ApplicationGroupRole}";
                GRANT USAGE ON SCHEMA "{schemaName}" TO "{TenantIsolationPolicyCatalog.ApplicationGroupRole}";
                """;
            await command.ExecuteNonQueryAsync();
        }

        var appBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Username = applicationRole,
            Password = applicationPassword,
            SearchPath = schemaName,
            Pooling = false
        };
        applicationConnectionString = appBuilder.ConnectionString;
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(baseConnectionString)) return;
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(baseConnectionString);
        await admin.OpenAsync();
        await using var command = admin.CreateCommand();
        command.CommandText = $"""
            DROP SCHEMA IF EXISTS "{schemaName}" CASCADE;
            DROP ROLE IF EXISTS "{applicationRole}";
            """;
        await command.ExecuteNonQueryAsync();
    }

    [PostgresFact]
    public async Task ApplicationRoleCanReadButCannotMutateMigrationHistory()
    {
        var appConnection = applicationConnectionString
            ?? throw new InvalidOperationException($"{ConnectionEnvVar} is required.");

        await using (var appDb = CreateDb(appConnection))
        {
            Assert.Empty(await appDb.Database.GetPendingMigrationsAsync());
        }

        await using var connection = new NpgsqlConnection(appConnection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM \"__EFMigrationsHistory\" WHERE FALSE";
        var mutation = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, mutation.SqlState);
    }

    [PostgresFact]
    public async Task DefectiveRawAndIgnoreFilterQueriesCannotCrossTenantOrForgeContext()
    {
        var adminConnection = administratorConnectionString
            ?? throw new InvalidOperationException($"{ConnectionEnvVar} is required.");
        var appConnection = applicationConnectionString
            ?? throw new InvalidOperationException($"{ConnectionEnvVar} is required.");
        int firstTenantId;
        int secondTenantId;
        int firstCompanyId;
        int secondCompanyId;

        await using (var setup = CreateDb(adminConnection))
        {
            var firstTenant = new Tenant { Name = "RLS One", Slug = "rls-one-" + Guid.NewGuid().ToString("N") };
            var secondTenant = new Tenant { Name = "RLS Two", Slug = "rls-two-" + Guid.NewGuid().ToString("N") };
            setup.Tenants.AddRange(firstTenant, secondTenant);
            await setup.SaveChangesAsync();
            firstTenantId = firstTenant.Id;
            secondTenantId = secondTenant.Id;

            setup.UserAccounts.AddRange(
                User(firstTenantId, "rls-one@example.invalid"),
                User(secondTenantId, "rls-two@example.invalid"));
            var firstCompany = Company(firstTenantId, "RLS One Limited");
            var secondCompany = Company(secondTenantId, "RLS Two Limited");
            setup.Companies.AddRange(firstCompany, secondCompany);
            await setup.SaveChangesAsync();
            firstCompanyId = firstCompany.Id;
            secondCompanyId = secondCompany.Id;
            setup.AccountingPeriods.AddRange(
                Period(firstCompanyId, 2025),
                Period(secondCompanyId, 2025));
            await setup.SaveChangesAsync();
        }

        var tenantContext = new DatabaseTenantContext(new HttpContextAccessor());
        tenantContext.SetResolvedTenant(firstTenantId);
        var interceptor = new TenantRlsConnectionInterceptor(tenantContext, Options.Create(Configuration()));
        await using (var connection = new NpgsqlConnection(appConnection))
        {
            await connection.OpenAsync();
            await interceptor.ApplyToOpenConnectionAsync(connection);

            Assert.Equal(firstTenantId, await ScalarIntAsync(connection, "SELECT accounts_current_tenant_id()"));
            Assert.Equal(1, await ScalarIntAsync(connection, "SELECT count(*)::integer FROM companies"));
            Assert.Equal(1, await ScalarIntAsync(connection, "SELECT count(*)::integer FROM accounting_periods"));
            Assert.Equal(0, await ExecuteAsync(
                connection,
                "UPDATE companies SET \"LegalName\" = 'cross-tenant mutation' WHERE \"Id\" = @id",
                new NpgsqlParameter("id", secondCompanyId)));

            var crossInsert = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
                connection,
                "INSERT INTO companies (\"TenantId\", \"LegalName\", \"CompanyType\", \"IncorporationDate\", \"FinancialYearStartMonth\", \"CreatedAt\", \"UpdatedAt\") VALUES (@tenant, 'forbidden', 0, DATE '2020-01-01', 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)",
                new NpgsqlParameter("tenant", secondTenantId)));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, crossInsert.SqlState);

            await SetConfigAsync(connection, firstTenantId.ToString(), new string('0', 64));
            Assert.Null(await ScalarNullableIntAsync(connection, "SELECT accounts_current_tenant_id()"));
            Assert.Equal(0, await ScalarIntAsync(connection, "SELECT count(*)::integer FROM companies"));
            await interceptor.ApplyToOpenConnectionAsync(connection);

            var keyRead = await Assert.ThrowsAsync<PostgresException>(() =>
                ScalarIntAsync(connection, "SELECT count(*)::integer FROM tenant_rls_context_keys"));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, keyRead.SqlState);
            var disableRls = await Assert.ThrowsAsync<PostgresException>(() =>
                ExecuteAsync(connection, "ALTER TABLE companies DISABLE ROW LEVEL SECURITY"));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, disableRls.SqlState);
            var becomeAdmin = await Assert.ThrowsAsync<PostgresException>(() =>
                ExecuteAsync(connection, $"SET ROLE \"{TenantIsolationPolicyCatalog.AdministratorGroupRole}\""));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, becomeAdmin.SqlState);
        }

        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseNpgsql(appConnection)
            .AddInterceptors(interceptor)
            .Options;
        await using (var defectiveEf = new AccountsDbContext(options))
        {
            var companies = await defectiveEf.Companies.IgnoreQueryFilters().OrderBy(company => company.Id).ToListAsync();
            Assert.Single(companies);
            Assert.Equal(firstCompanyId, companies[0].Id);
            await new DatabaseTenantIsolationVerifier(defectiveEf, Options.Create(Configuration())).VerifyAsync();
        }

        var emptyContext = new DatabaseTenantContext(new HttpContextAccessor());
        var emptyInterceptor = new TenantRlsConnectionInterceptor(emptyContext, Options.Create(Configuration()));
        await using (var anonymous = new NpgsqlConnection(appConnection))
        {
            await anonymous.OpenAsync();
            await emptyInterceptor.ApplyToOpenConnectionAsync(anonymous);
            Assert.Equal(firstTenantId, await ScalarIntAsync(
                anonymous,
                "SELECT accounts_resolve_login_tenant(@email)",
                new NpgsqlParameter("email", "rls-one@example.invalid")));
            Assert.Equal(0, await ScalarIntAsync(anonymous, "SELECT count(*)::integer FROM user_accounts"));
            Assert.Equal(1, await ExecuteAsync(
                anonymous,
                """
                INSERT INTO login_security_events
                    ("TenantId", "UserId", "IdentifierFingerprint", "OutcomeCode", "ReasonCode", "OccurredAtUtc", "ExpiresAtUtc")
                VALUES (NULL, NULL, repeat('a', 64), 'rejected', 'unknown-identifier', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP + INTERVAL '30 days')
                """));
            Assert.Equal(0, await ScalarIntAsync(anonymous, "SELECT count(*)::integer FROM login_security_events"));
        }
    }

    private DatabaseTenantIsolationConfig Configuration() => new()
    {
        Required = true,
        ApplicationLoginRole = applicationRole,
        ContextSigningKey = signingKey
    };

    private static AccountsDbContext CreateDb(string connectionString) => new(
        new DbContextOptionsBuilder<AccountsDbContext>().UseNpgsql(connectionString).Options);

    private static UserAccount User(int tenantId, string email) => new()
    {
        TenantId = tenantId,
        Email = email,
        DisplayName = "Synthetic RLS User",
        Role = "Owner",
        PasswordHash = "synthetic-hash",
        PasswordSalt = "synthetic-salt"
    };

    private static Company Company(int tenantId, string name) => new()
    {
        TenantId = tenantId,
        LegalName = name,
        CompanyType = CompanyType.Private,
        IncorporationDate = new DateOnly(2025, 1, 1)
    };

    private static AccountingPeriod Period(int companyId, int year) => new()
    {
        CompanyId = companyId,
        PeriodStart = new DateOnly(year, 1, 1),
        PeriodEnd = new DateOnly(year, 12, 31),
        IsFirstYear = true
    };

    private static async Task SetConfigAsync(NpgsqlConnection connection, string tenant, string signature)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT set_config('accounts.tenant_id', @tenant, false), set_config('accounts.tenant_signature', @signature, false)";
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("signature", signature);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ExecuteAsync(
        NpgsqlConnection connection,
        string sql,
        params NpgsqlParameter[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        return await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ScalarIntAsync(
        NpgsqlConnection connection,
        string sql,
        params NpgsqlParameter[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<int?> ScalarNullableIntAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? null : Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionEnvVar)))
                Skip = $"{ConnectionEnvVar} is not set.";
        }
    }
}
